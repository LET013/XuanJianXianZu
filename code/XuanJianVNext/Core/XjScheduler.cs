using System;
using System.Collections.Generic;
using System.Diagnostics;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Visual;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Core;

internal readonly struct XjSchedulerContext
{
	internal readonly int GrowthPasses;
	internal readonly bool ProcessFast;
	internal readonly bool IsYearChange;

	internal bool ProcessGrowth => GrowthPasses > 0;

	internal XjSchedulerContext(int growthPasses, bool processFast, bool isYearChange)
	{
		GrowthPasses = Math.Max(0, growthPasses);
		ProcessFast = processFast;
		IsYearChange = isYearChange;
	}
}

/// <summary>
/// Central runtime coordinator. It schedules bounded lanes and cultivator-only work.
/// </summary>
internal static class XjScheduler
{
	private sealed class AnnualActorState
	{
		internal int ActiveYear;
		internal int LatestRequestedYear;
		internal int LastCompletedYear;
		internal XjAnnualPipelineStage Stage;
		internal bool Queued;
	}

	private const int CleanupRemoveBudget = 24;
	private const int BootstrapBudget = 96;
	private const double BootstrapTimeBudgetMs = 0.9;
	private const int SeedFastBudget = 20;
	private const int SeedGrowthBudget = 48;
	private const int SeedYearBudget = 80;
	private const double SeedLaneTimeBudgetMs = 0.65;
	private const int AnnualStageBudget = 192;
	private const double AnnualLaneTimeBudgetMs = 0.95;
	private const int AnnualCultivatorSweepBudget = 384;
	private const double AnnualCultivatorSweepTimeBudgetMs = 0.35;
	private const int AutoCollectRecheckBudget = 64;
	private const double AutoCollectRecheckTimeBudgetMs = 0.3;
	private const int DeathLaneBudget = 3;
	private const int DeathArchiveBudget = 5;
	private const int DongTianRuntimeBudget = 4;
	private const int ClosedCultivationBudget = 4;
	private const int CaiQiCityMaintenanceBudget = 2;
	private const int CaiQiSiteRegistrationBudget = 6;
	private const int CombatRegenerationBudget = 24;
	private const int JinDanCombatBudget = 20;
	private const int DomainRuntimeBudget = 4;
	private const int ZongMenCityVisitBudget = 1;
	private const int AnnualWorldStageBudget = 12;
	private const double AnnualWorldTimeBudgetMs = 0.75;

	private static int _tickCounter;
	private static int _deathPhase;
	private static int _backgroundPhase;
	private static int _pendingLongShuGrowthPasses;
	private static bool _longShuYearMaintenancePending;
	private static readonly Queue<long> AnnualActorQueue = new Queue<long>();
	private static readonly Dictionary<long, AnnualActorState> AnnualActorStates = new Dictionary<long, AnnualActorState>();
	private static readonly Dictionary<long, int> DeferredAnnualCompletions = new Dictionary<long, int>();
	private static readonly Queue<long> AutoCollectRecheckQueue = new Queue<long>();
	private static readonly HashSet<long> AutoCollectRecheckSet = new HashSet<long>();
	private static readonly XjJinDanCombatLane JinDanCombatLane = new XjJinDanCombatLane(ResolveActorDirect);

	/// <summary>
	/// O(1) interest gate for the 10-frame cadence. An empty fast cadence no
	/// longer enters the scheduler fan-out at all. Growth/year edges still run
	/// independently.
	/// </summary>
	internal static bool HasFastWork => XjWorldBootstrapLane.HasPending
		|| XjDomainSkillRuntime.HasActive
		|| XjAptitudeSeedLane.PendingCount > 0
		|| XjAnnualCultivatorSweepLane.HasPending
		|| AnnualActorQueue.Count > 0
		|| AutoCollectRecheckQueue.Count > 0
		|| HasCriticalDeathWork
		|| HasBackgroundWork;

	internal static bool HasUrgentSimulationBacklog => HasCriticalDeathWork;

	internal static bool HasAnnualActorBacklog => AnnualActorQueue.Count > 0
		|| XjAnnualCultivatorSweepLane.HasPending;

	internal static bool HasAnnualRecoveryBacklog
	{
		get
		{
			int threshold = Math.Max(256, XjCultivatorCache.Count / 4);
			return XjAnnualCultivatorSweepLane.HasPending || AnnualActorQueue.Count >= threshold;
		}
	}

	private static bool HasCriticalDeathWork => XjZiFuFailureDeathLane.HasPending
		|| XjRenDanDeathLane.HasPending
		|| XjQiYuDongTianDeathLane.HasPending
		|| XjShenDanDeathLane.HasPending
		|| XjDeathArchiveQueue.HasPending
		|| XjJinDanResidualDeathLane.HasPending;

	private static bool HasBackgroundWork => _longShuYearMaintenancePending
		|| _pendingLongShuGrowthPasses > 0
		|| XjLongShuSystem.HasPendingSpawnWork
		|| XjAnnualWorldRuntimeLane.HasPending
		|| XjDongTianRegistry.NeedsRuntimeTick
		|| XjCaiQiCityMaintenanceLane.HasPending
		|| XjCaiQiSiteRegistry.HasPendingRegistrations
		|| XjBroadcastSystem.HasPendingAnnouncements
		|| XjYinSiTraitLifecycle.HasPendingRuntimeWork
		|| XjHighRealmArmyExclusion.HasPending
		|| XjZongMenRuntimeLane.HasPending
		|| XjSectGovernanceRuntimeLane.HasPending
		|| XjCodexSnapshotPublisher.HasPending;

	internal static void RegisterActor(Actor actor)
	{
		TryRegisterEligibleActor(actor, out _);
	}

	/// <summary>
	/// 原生 updateAge 的单一入口：资格判断、注册和年度入队只执行一次。
	/// </summary>
	internal static void RegisterAndEnqueueAnnualActor(Actor actor)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled
			|| actor?.data == null
			|| !actor.isAlive())
		{
			return;
		}

		long knownActorId = ((BaseSystemData)actor.data).id;
		if (knownActorId > 0L && XjCultivatorCache.IsCultivator(knownActorId))
		{
			// 已入修炼缓存的角色不再重复走自然资质与剑意兼容判定。
			// 其可见特质或外部互斥状态改变时，由对应 trait 生命周期入口
			// 主动修正缓存；年度回调只做轻量入队。
			EnqueueAnnualActorCore(actor, knownActorId);
			return;
		}

		int ageYear = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		bool isAgeFiveWindow = XjAptitudeRuleEvaluator.IsAgeFiveEligibilityWindow(ageYear);
		bool hasExplicitGrant = XjCultivationEligibility.HasExplicitCultivationGrant(actor);
		bool hasCultivationState = XjCultivationEligibility.HasCultivationMarkers(actor);
		if (!isAgeFiveWindow && !hasExplicitGrant && !hasCultivationState)
		{
			// 资质只在五岁判定；未经玩家授予、也没有玄鉴状态的凡人不再
			// 参与年度注册，避免高人口世界每年对全体角色重复扫描。
			return;
		}

		if (TryRegisterEligibleActor(actor, out long actorId))
		{
			EnqueueAnnualActorCore(actor, actorId);
		}
	}

	/// <summary>
	/// Cold-path registration used by the bounded post-load bootstrap. It rebuilds
	/// runtime indexes but never grants annual growth, attempts breakthroughs or
	/// emits a new annual event.
	/// </summary>
	internal static void RegisterActorForBootstrap(Actor actor, int baselineYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled) return;
		if (!TryRegisterEligibleActor(actor, out long actorId))
		{
			return;
		}

		if (XjCultivatorCache.IsCultivator(actorId))
		{
			EnsureCultivationQualifiedYear(actor, baselineYear);
			RestoreAnnualStateAfterLoad(actor, actorId, baselineYear);
		}
	}

	private static bool TryRegisterEligibleActor(Actor actor, out long actorId)
	{
		actorId = 0L;
		if (XjYinSiTraitLifecycle.IsYinSi(actor))
		{
			XjActorRegistry.Register(actor, out actorId, out _);
			XjYinSiTraitLifecycle.EnsureTransientState(actor);
			return false;
		}

		if (!XjCultivationEligibility.CanCultivate(actor))
		{
			if (XjRuntimeSettings.CultivationEnabled
				&& XjCultivationEligibility.ShouldClearUnsupportedCultivationState(actor)
				&& XjCultivationEligibility.HasCultivationMarkers(actor))
			{
				XjVisibleTraitSync.ClearUnsupportedCultivationState(actor);
				if (actor?.data != null)
				{
					XjCultivatorCache.Remove(((BaseSystemData)actor.data).id);
				}
			}
			return false;
		}

		if (!XjActorRegistry.Register(actor, out actorId, out bool isNew))
		{
			return false;
		}

		if (isNew)
		{
			XjLongShuSystem.TrackKnownActor(actor);
		}

		bool wasCultivator = XjCultivatorCache.IsCultivator(actorId);
		if (!wasCultivator
			&& XjCultivationEligibility.HasExplicitCultivationGrant(actor)
			&& XjIdentityTraitLifecycle.IsIdentityTraitPresent(actor))
		{
			XjManualCultivationWake.EnsureAwake(actor, ensureMinimumAptitude: false, registerActor: false);
			wasCultivator = XjCultivatorCache.IsCultivator(actorId);
		}

		bool needsSeed = !XjAptitudeSeedLane.IsComplete(actorId)
			&& XjAptitudeSeedLane.NeedsProcessingEligibleActor(actor);
		if (needsSeed)
		{
			// 五岁是严格且不可补发的资质窗口。普通队列在高倍速下可能落后
			// 数个模拟年，因此该窗口在 updateAge 当次同步完成；其余年龄仍走
			// 限额种子通道，避免对万人世界引入全量同步扫描。
			if (!XjAptitudeSeedLane.TryProcessAgeFiveDeadline(actor))
			{
				XjAptitudeSeedLane.Enqueue(actorId);
			}
		}
		else if (isNew || !wasCultivator)
		{
			XjCultivatorCache.CheckAndUpdate(actor);
		}

		// 家族恢复、城市索引、死亡队列和闭关缓存只在角色确认成为修士
		// 时初始化。首次发现的凡人只允许补种子/五岁判定，不进入修士
		// 运行索引，避免 seed 队列把大量非修士推入年度维护体系。
		if (!wasCultivator && XjCultivatorCache.IsCultivator(actorId))
		{
			EnsureRuntimeIndexes(actor, actorId);
		}
		return true;
	}

	/// <summary>
	/// 资质种子完成后的定向索引补齐。该入口不触发年度业务，只把刚成为
	/// 修士的角色接入城市、死亡、闭关和金丹战斗索引。
	/// </summary>
	internal static void EnsureRuntimeIndexesForActor(Actor actor)
	{
		if (actor?.data == null) return;
		EnsureRuntimeIndexes(actor, ((BaseSystemData)actor.data).id);
	}

	private static void EnsureRuntimeIndexes(Actor actor, long actorId)
	{
		if (actor?.data == null || actorId <= 0L)
		{
			return;
		}

		// This is a read-only ledger restore when the runtime index is empty. It is
		// safe for new actors (no ledger row) and closes the load race where an
		// updateAge callback reaches a cultivator before the bounded bootstrap.
		XjFamilyMemberIndex.Shared.RestoreRuntimeIndexAfterLoad(actor);
		if (!XjCultivatorCache.IsCultivator(actorId))
		{
			return;
		}

		XjZongMenCultivatorCityIndex.Observe(actor);
		XjCraftActorIndex.Observe(actor);
		if (actor.city?.data != null)
		{
			XjCaiQiCityMaintenanceLane.MarkCityPending(((BaseSystemData)actor.city.data).id);
		}
		XjZiFuFailureDeathLane.EnqueueIfPending(actor);
		XjRenDanDeathLane.EnqueueIfPending(actor);
		XjQiYuDongTianDeathLane.EnqueueIfPending(actor);
		XjShenDanRegistry.Observe(actor);
		XjShenDanDeathLane.EnqueueIfPending(actor);
		XjClosedCultivationGuard.TrackIfClosedCultivation(actor);
		if (IsJinDanActor(actor))
		{
			JinDanCombatLane.EnqueueActor(actorId);
		}
	}

	internal static IReadOnlyList<Actor> GetKnownActorsSnapshot()
	{
		return XjActorRegistry.Snapshot();
	}

	internal static void EnqueueAnnualActor(Actor actor)
	{
		if (actor?.data == null
			|| !actor.isAlive()
			|| !XjCultivationEligibility.CanCultivate(actor))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		EnqueueAnnualActorCore(actor, actorId);
	}

	/// <summary>
	/// 已确认修士缓存的年度补漏入口。只允许年度修士补漏通道调用，
	/// 不重新扫描世界，也不授予资质。
	/// </summary>
	internal static void EnqueueAnnualActorForSweep(Actor actor, int requestedYear)
	{
		if (actor?.data == null
			|| requestedYear <= 0
			|| !actor.isAlive()
			|| !XjCultivationEligibility.CanCultivate(actor))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		EnqueueAnnualActorCore(actor, actorId, requestedYear);
	}

	private static void EnqueueAnnualActorCore(Actor actor, long actorId, int explicitRequestedYear = 0)
	{
		if (actorId <= 0L
			|| (!XjCultivatorCache.IsCultivator(actorId) && !XjCultivatorCache.CheckAndUpdate(actor)))
		{
			return;
		}

		int requestedYear = explicitRequestedYear > 0
			? explicitRequestedYear
			: ResolveAnnualRequestYear(actor);
		if (requestedYear <= 0)
		{
			return;
		}

		int qualifiedYear = EnsureCultivationQualifiedYear(actor, requestedYear);
		if (qualifiedYear <= 0 || requestedYear < qualifiedYear)
		{
			return;
		}

		if (!AnnualActorStates.TryGetValue(actorId, out AnnualActorState state))
		{
			if (!TryReadPersistedPendingAnnualState(actor, out state))
			{
				int persistedCompletedYear = ReadEffectiveLastCompletedYear(actor);
				if (requestedYear <= persistedCompletedYear)
				{
					return;
				}

				state = new AnnualActorState
				{
					ActiveYear = ResolveNextAnnualActiveYear(persistedCompletedYear, requestedYear),
					LatestRequestedYear = requestedYear,
					LastCompletedYear = persistedCompletedYear,
					Stage = XjAnnualPipelineStage.Prepare,
					Queued = false
				};
			}
			state.LatestRequestedYear = Math.Max(state.LatestRequestedYear, requestedYear);
			AnnualActorStates[actorId] = state;
		}
		else
		{
			state.LatestRequestedYear = Math.Max(state.LatestRequestedYear, requestedYear);
			if (state.ActiveYear < qualifiedYear)
			{
				state.LastCompletedYear = Math.Max(state.LastCompletedYear, qualifiedYear - 1);
				state.ActiveYear = qualifiedYear;
				state.Stage = XjAnnualPipelineStage.Prepare;
			}
			if (requestedYear <= state.LastCompletedYear)
			{
				return;
			}
		}

		QueueAnnualState(actorId, state);
	}

	private static void RestoreAnnualStateAfterLoad(Actor actor, long actorId, int baselineYear)
	{
		if (actor?.data == null || actorId <= 0L)
		{
			return;
		}

		int qualifiedYear = EnsureCultivationQualifiedYear(actor, baselineYear);
		if (qualifiedYear <= 0) return;

		// An updateAge callback can register and queue this actor before the bounded
		// bootstrap reaches it. In that case the runtime state already reflects the
		// persisted stage; normalize it to the first grounded cultivation year rather
		// than allowing an old cursor to replay the actor's childhood.
		if (AnnualActorStates.TryGetValue(actorId, out AnnualActorState existingState))
		{
			if (existingState.ActiveYear < qualifiedYear)
			{
				existingState.LastCompletedYear = Math.Max(existingState.LastCompletedYear, qualifiedYear - 1);
				existingState.ActiveYear = qualifiedYear;
				existingState.Stage = XjAnnualPipelineStage.Prepare;
			}
			QueueAnnualState(actorId, existingState);
			return;
		}

		int lastCompletedYear = ReadEffectiveLastCompletedYear(actor);
		if (!TryReadPersistedPendingAnnualState(actor, out AnnualActorState persistedState))
		{
			// Idle cultivators do not occupy the runtime state dictionary. Legacy
			// saves predate the annual state keys, so use the existing cultivation
			// year as the only grounded completion marker. If a saved actor really
			// fell behind the world year, rebuild a bounded catch-up state instead of
			// silently baselining away those missing annual passes.
			int inferredCompletedYear = Math.Max(0, lastCompletedYear);
			if (inferredCompletedYear <= 0
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLastCultivationYear, out int legacyCultivationYear)
				&& legacyCultivationYear > 0)
			{
				inferredCompletedYear = baselineYear > 0
					? Math.Min(legacyCultivationYear, baselineYear)
					: legacyCultivationYear;
			}
			if (inferredCompletedYear <= 0)
			{
				// No reliable legacy marker exists. Baseline at the loaded year rather
				// than replaying the actor's entire lifetime.
				inferredCompletedYear = Math.Max(0, baselineYear);
			}

			if (baselineYear > inferredCompletedYear)
			{
				AnnualActorState catchUpState = new AnnualActorState
				{
					ActiveYear = ResolveNextAnnualActiveYear(inferredCompletedYear, baselineYear),
					LatestRequestedYear = baselineYear,
					LastCompletedYear = inferredCompletedYear,
					Stage = XjAnnualPipelineStage.Prepare,
					Queued = false
				};
				AnnualActorStates[actorId] = catchUpState;
				QueueAnnualState(actorId, catchUpState);
				return;
			}

			CompletePersistedAnnualState(actor, inferredCompletedYear);
			AnnualActorStates.Remove(actorId);
			return;
		}

		AnnualActorStates[actorId] = persistedState;
		QueueAnnualState(actorId, persistedState);
	}

	private static bool TryReadPersistedPendingAnnualState(Actor actor, out AnnualActorState state)
	{
		state = null;
		if (actor?.data == null)
		{
			return false;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualActiveYear, out int activeYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualLatestRequestedYear, out int latestYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualStage, out int stageValue);
		int qualifiedYear = ReadCultivationQualifiedYear(actor);
		int lastCompletedYear = ReadEffectiveLastCompletedYear(actor);
		if (qualifiedYear > 0 && activeYear < qualifiedYear)
		{
			if (latestYear < qualifiedYear) return false;
			activeYear = qualifiedYear;
			stageValue = (int)XjAnnualPipelineStage.Prepare;
			lastCompletedYear = Math.Max(lastCompletedYear, qualifiedYear - 1);
		}
		if (activeYear <= 0
			|| latestYear < activeYear
			|| activeYear <= lastCompletedYear
			|| stageValue < (int)XjAnnualPipelineStage.Prepare
			|| stageValue > (int)XjAnnualPipelineStage.Finalize)
		{
			return false;
		}

		int normalizedActiveYear = stageValue == (int)XjAnnualPipelineStage.Prepare
			? ResolveNextAnnualActiveYear(lastCompletedYear, latestYear)
			: activeYear;
		if (normalizedActiveYear <= 0)
		{
			return false;
		}

		state = new AnnualActorState
		{
			ActiveYear = normalizedActiveYear,
			LatestRequestedYear = latestYear,
			LastCompletedYear = Math.Max(0, lastCompletedYear),
			Stage = (XjAnnualPipelineStage)stageValue,
			Queued = false
		};
		return true;
	}

	private static int ResolveNextAnnualActiveYear(int lastCompletedYear, int latestRequestedYear)
	{
		int completed = Math.Max(0, lastCompletedYear);
		int requested = Math.Max(0, latestRequestedYear);
		if (requested <= completed)
		{
			return 0;
		}

		// 年度业务是“当前状态维护”，不是历史事件回放。高倍速下逐年重放
		// 会让万人修士队列永久落后；直接合并到最新年份，遗漏的被动修炼
		// 由 XjLastCultivationYear 计算经过年数补齐。
		return requested;
	}

	private static int ReadEffectiveLastCompletedYear(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualLastCompletedYear, out int persistedYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L
			&& DeferredAnnualCompletions.TryGetValue(actorId, out int deferredYear))
		{
			persistedYear = Math.Max(persistedYear, deferredYear);
		}
		int qualifiedYear = ReadCultivationQualifiedYear(actor);
		if (qualifiedYear > 0)
		{
			persistedYear = Math.Max(persistedYear, qualifiedYear - 1);
		}
		return Math.Max(0, persistedYear);
	}

	private static void PersistAnnualState(Actor actor, AnnualActorState state)
	{
		if (actor?.data == null || state == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		int lastCompletedYear = state.LastCompletedYear;
		if (actorId > 0L
			&& DeferredAnnualCompletions.TryGetValue(actorId, out int deferredYear))
		{
			lastCompletedYear = Math.Max(lastCompletedYear, deferredYear);
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualActiveYear, Math.Max(0, state.ActiveYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualLatestRequestedYear, Math.Max(0, state.LatestRequestedYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualStage, (int)state.Stage);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualLastCompletedYear, Math.Max(0, lastCompletedYear));
	}

	private static void CompletePersistedAnnualState(Actor actor, int completedYear)
	{
		if (actor?.data == null)
		{
			return;
		}

		// 年度完成态只进入内存批次；不在每个修士、每个年份都写 custom-data。
		// 保存前统一落盘，角色离开运行时索引时则定向加急写回。
		long actorId = ((BaseSystemData)actor.data).id;
		int normalizedYear = Math.Max(0, completedYear);
		if (actorId <= 0L || normalizedYear <= 0)
		{
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualLastCompletedYear, out int persistedYear);
		if (normalizedYear <= persistedYear
			&& !DeferredAnnualCompletions.ContainsKey(actorId))
		{
			return;
		}

		if (!DeferredAnnualCompletions.TryGetValue(actorId, out int previousYear)
			|| normalizedYear > previousYear)
		{
			DeferredAnnualCompletions[actorId] = normalizedYear;
		}
	}

	private static void FlushDeferredAnnualCompletion(long actorId)
	{
		if (actorId <= 0L
			|| !DeferredAnnualCompletions.TryGetValue(actorId, out int completedYear))
		{
			return;
		}

		if (XjActorRegistry.Resolve(actorId, out Actor actor) && actor?.data != null)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualLastCompletedYear, completedYear);
		}
		DeferredAnnualCompletions.Remove(actorId);
	}

	/// <summary>
	/// 真正保存前将年度完成批次和仍在内存队列中的三阶段游标一次性写入角色数据。
	/// 运行期不再按角色、按阶段、按年份持续写 custom-data。
	/// </summary>
	internal static void FlushPendingAnnualStatesForSave()
	{
		foreach (KeyValuePair<long, int> pair in DeferredAnnualCompletions)
		{
			if (XjActorRegistry.Resolve(pair.Key, out Actor actor) && actor?.data != null)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualLastCompletedYear, pair.Value);
			}
		}

		foreach (KeyValuePair<long, AnnualActorState> pair in AnnualActorStates)
		{
			if (pair.Value != null && XjActorRegistry.Resolve(pair.Key, out Actor actor))
			{
				PersistAnnualState(actor, pair.Value);
			}
		}
		DeferredAnnualCompletions.Clear();
	}

	internal static void EnqueueKnownActorsForAutoCollectRecheck()
	{
		// Runtime setting callbacks only need to re-evaluate favourite status. Do
		// not re-enter cultivation, forging, lost-FaBao discovery or sect annual
		// work merely because an auto-collect option was enabled.
		IReadOnlyList<Actor> actors = XjActorRegistry.Snapshot();
		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			if (actor?.data == null || !actor.isAlive())
			{
				continue;
			}

			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId > 0L && AutoCollectRecheckSet.Add(actorId))
			{
				AutoCollectRecheckQueue.Enqueue(actorId);
			}
		}
	}

	private static void QueueAnnualState(long actorId, AnnualActorState state)
	{
		if (state == null || state.Queued)
		{
			return;
		}
		state.Queued = true;
		AnnualActorQueue.Enqueue(actorId);
	}

	internal static int ReadCultivationQualifiedYear(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjCultivationQualifiedYear, out int year))
		{
			return 0;
		}
		return Math.Max(0, year);
	}

	private static int EnsureCultivationQualifiedYear(Actor actor, int fallbackYear)
	{
		if (actor?.data == null) return 0;
		int existing = ReadCultivationQualifiedYear(actor);
		if (existing > 0) return existing;
		int resolved = Math.Max(0, fallbackYear);
		if (resolved <= 0)
		{
			resolved = Math.Max(Math.Max(0, XjYearTracker.CurrentYear), Math.Max(0, World.world?.map_stats?.year ?? 0));
		}
		if (resolved <= 0) return 0;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCultivationQualifiedYear, resolved);
		return resolved;
	}

	private static int ResolveAnnualRequestYear(Actor actor)
	{
		// updateAge runs inside the native year transition and can precede the
		// cadence refresh. Prefer the live world year when it is ahead of the
		// cached tracker, otherwise every actor callback in that transition can be
		// misclassified as the already-completed previous year.
		int trackedYear = Math.Max(0, XjYearTracker.CurrentYear);
		int worldYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
		int resolvedYear = Math.Max(trackedYear, worldYear);
		if (resolvedYear > 0)
		{
			return resolvedYear;
		}
		return actor == null ? 0 : (int)Math.Floor(Math.Max(0f, actor.getAge()));
	}

	internal static int GetKnownActorCount()
	{
		return XjActorRegistry.Count;
	}

	internal static bool ResolveActor(long actorId, out Actor actor)
	{
		return XjActorRegistry.Resolve(actorId, out actor);
	}

	internal static void UnregisterDeadActor(Actor actor)
	{
		if (actor?.data == null || actor.isAlive())
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}
		XjAutoCollectSystem.ClearFavoriteIfInvalid(actor);
		ForgetActorRuntimeState(actorId);
		XjActorRegistry.Unregister(actorId);
	}

	private static void ForgetActorRuntimeState(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		FlushDeferredAnnualCompletion(actorId);
		AnnualActorStates.Remove(actorId);
		AutoCollectRecheckSet.Remove(actorId);
		XjAptitudeSeedLane.Forget(actorId);
		XjCultivatorCache.Remove(actorId);
		XjFaBaoBonusService.Forget(actorId);
		XjCombatHotPathCache.Remove(actorId);
		XjCombatTracker.Remove(actorId);
		XuanJianVNext.Systems.WeaponArt.XjWeaponArtCombatTracker.Remove(actorId);
		XjZongMenCultivatorCityIndex.Forget(actorId);
		XjCraftActorIndex.Forget(actorId);
		XjShenDanRegistry.Forget(actorId);
		XjFormationEngineeringSystem.HandleActorRemoved(actorId);
		XjSecretRealmRegistry.OnActorDied(actorId, World.world?.map_stats?.year ?? 0);
		XjCraftDomainRegistry.ForgetActor(actorId);
		XjAlchemyRuntimeRegistry.ForgetActor(actorId, World.world?.map_stats?.year ?? 0);
	}

	internal static void ProcessAll(in XjSchedulerContext context)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled) return;
		if (XjRuntimePauseGate.BlocksSimulation)
		{
			ReportSchedulerPressure();
			return;
		}
		CaptureBackgroundEdges(context);
		if (context.IsYearChange)
		{
			int currentYear = World.world?.map_stats?.year ?? 0;
			XjAnnualWorldRuntimeLane.Schedule(currentYear);
			XjAnnualCultivatorSweepLane.Schedule(currentYear);
		}
		else if (!XjAnnualWorldRuntimeLane.HasPending)
		{
			int currentYear = World.world?.map_stats?.year ?? 0;
			if (XjCenturyAnnalsBuilder.HasPendingCompletedCentury(currentYear))
			{
				XjAnnualWorldRuntimeLane.ScheduleCenturyCatchUp(currentYear);
			}
		}

		// 必要队列：只有对应队列存在时才进入分支，避免空转 fan-out。
		if (XjWorldBootstrapLane.HasPending)
		{
			long bootstrapSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.Bootstrap, 0);
			TickBootstrapLane();
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.Bootstrap, bootstrapSample);
			if (XjWorldBootstrapLane.HasPending)
			{
				ReportSchedulerPressure();
				return;
			}
		}
		if (XjAptitudeSeedLane.PendingCount > 0)
		{
			long seedSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.SeedLane, 15);
			TickSeedLane(context);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.SeedLane, seedSample);
		}
		if (XjAnnualCultivatorSweepLane.HasPending)
		{
			TickAnnualCultivatorSweep();
		}
		if (AnnualActorQueue.Count > 0 || AutoCollectRecheckQueue.Count > 0)
		{
			TickCultivators(context);
		}

		bool hasActiveRuntime = HasCriticalDeathWork || HasActiveRuntimeWork(context);
		bool hasBackgroundRuntime = context.ProcessFast && HasBackgroundWork;
		if (hasActiveRuntime || hasBackgroundRuntime)
		{
			long boundedSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BoundedRuntime, 15);
			TickBoundedRuntime(context);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BoundedRuntime, boundedSample);
		}


		long cleanupSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.Cleanup, 63);
		TickCleanup();
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.Cleanup, cleanupSample);
		ReportSchedulerPressure();
	}

	private static void ReportSchedulerPressure()
	{
		if (!XjRuntimeSettings.ShowFpsOverlayEnabled) return;
		XjRuntimeDiagnostics.SampleQueue("annualSweep", XjAnnualCultivatorSweepLane.PendingCount);
		XjRuntimeDiagnostics.SampleQueue("autoCollect", AutoCollectRecheckQueue.Count);
		XjRuntimeDiagnostics.SampleQueue("longshuGrowth", _pendingLongShuGrowthPasses);
		XjRuntimeDiagnostics.SampleQueue("death", HasCriticalDeathWork ? 1 : 0);
		XjRuntimeDiagnostics.SampleQueue("background", HasBackgroundWork ? 1 : 0);
		XjRuntimeDiagnostics.SampleQueue("jindanCombat", JinDanCombatLane.HasPending ? 1 : 0);
		XjRuntimeDiagnostics.SampleQueue("domains", XjDomainSkillRuntime.ActiveCount);
		XjRuntimeDiagnostics.ReportIfDue(
			XjActorRegistry.Count,
			XjCultivatorCache.Count,
			AnnualActorQueue.Count,
			XjAptitudeSeedLane.PendingCount);
	}

	internal static void Clear()
	{
		XjCultivationEligibility.ClearRuntimeCache();
		XjActorRegistry.Clear();
		XjAptitudeSeedLane.Clear();
		XjAnnualCultivatorSweepLane.Clear();
		_tickCounter = 0;
		_deathPhase = 0;
		_backgroundPhase = 0;
		_pendingLongShuGrowthPasses = 0;
		_longShuYearMaintenancePending = false;
		AnnualActorQueue.Clear();
		AnnualActorStates.Clear();
		DeferredAnnualCompletions.Clear();
		AutoCollectRecheckQueue.Clear();
		AutoCollectRecheckSet.Clear();
		XjWorldBootstrapLane.Clear();
		XjZiFuFailureDeathLane.Clear();
		XjRenDanDeathLane.Clear();
		XjQiYuDongTianDeathLane.Clear();
		XjJinDanResidualDeathLane.Clear();
		XjClosedCultivationGuard.Clear();
		JinDanCombatLane.Clear();
		XjFaBaoAcquisition.ClearRuntimeCache();
		XjFaBaoBonusService.Clear();
		XjJieLinXianRegistry.Clear();
		XjXianJiAccessor.ClearRuntimeCache();
		XjCombatHotPathCache.Clear();
		XjTrueDamageSystem.ClearRuntime();
		XjActorAggroBridge.Clear();
		XjCombatTracker.Clear();
		XuanJianVNext.Systems.WeaponArt.XjWeaponArtCombatTracker.Clear();
		XjCombatEffectPool.ClearFrameCache();
		XjHighRealmArmyExclusion.Clear();
		XjRealmLifespanService.Clear();
		XjVisibleActorRenderLane.Clear();
		XjZiFuBackEffectVisualSystem.Clear();
		XjJinDanHaloVisualSystem.Clear();
		XjProjectileGuard.ClearRuntimeCache();
		XjDaoTuCounterState.Clear();
		XjAdventureRealmClaimSystem.Clear();
		XjSecretRealmRegistry.Clear();
		XjYinSiExposurePursuitSystem.Clear();
		XjSectLectureSystem.Clear();
		XjZongMenCultivatorCityIndex.Clear();
		XjCraftActorIndex.Clear();
		XjCraftDomainRegistry.Clear();
		XjSectGovernanceRuntimeLane.Clear();
		XjNationSectRebellionGuard.ClearRuntimeState();
		XjZongMenCityData.ClearRuntimeCache();
		XjZongMenRuntimeLane.Clear();
		XjSectFormationOccupationAdapter.Clear();
		XjSectFormationRegistry.Clear();
		XjSectWarSystem.Clear();
		XjSectRepository.Clear();
		XjJinDanImmortalityRegistry.Clear();
		XjCodexSnapshotPublisher.Clear();
		XjWorldHistoryStore.Clear();
		XjPersonalBiographyStore.Clear();
		XjFamilyChronicleBookStore.Clear();
		XjSectChronicleStore.Clear();
		XjThreeBookDeferredFamilyFacts.Clear();
		XjThreeBookDiagnostics.Clear();
		XjJinDanDaoSpellRuntime.Clear();
		XjJinDanDaoSpellCooldown.Clear();
		XjDomainSkillRuntime.Clear();
		XjJinDanCombatApi.ClearTerrainEffects();
		XjHuoDeFireBirdSummonSystem.Clear();
		XjSanYinBaoYiXuanLunSystem.Clear();
		XjRuntimeDiagnostics.Clear();
		XjNativeMagicRitualGuard.Clear();
		XjAnnualWorldDetectionStore.Clear();
		XjAnnualWorldRuntimeLane.Clear();
	}

	private static void CaptureBackgroundEdges(in XjSchedulerContext context)
	{
		if (context.IsYearChange && XjDetectionGate.ShouldProcessZiFuRuntime())
		{
			_longShuYearMaintenancePending = true;
		}
		if (context.GrowthPasses > 0 && XjLongShuSystem.HasKnownLongShu)
		{
			long total = (long)_pendingLongShuGrowthPasses + context.GrowthPasses;
			_pendingLongShuGrowthPasses = total >= int.MaxValue ? int.MaxValue : (int)total;
		}
	}

	private static bool HasActiveRuntimeWork(in XjSchedulerContext context)
	{
		// 领域按真实秒计时，由fast cadence推进，不依赖年度增长脉冲。
		if (XjDomainSkillRuntime.HasActive)
		{
			return true;
		}
		if (!context.ProcessGrowth)
		{
			return false;
		}

		return XjCombatRegenerationSystem.HasPending
			|| XjClosedCultivationGuard.HasActive
			|| JinDanCombatLane.HasPending;
	}

	private static void TickBootstrapLane()
	{
		if (!XjWorldBootstrapLane.HasPending)
		{
			return;
		}

		XjWorldBootstrapLane.Tick(
			XjRuntimeWorkBudget.ScaleCount(BootstrapBudget, 24),
			XjRuntimeWorkBudget.ScaleMilliseconds(BootstrapTimeBudgetMs, 0.5d));
	}

	private static void TickSeedLane(in XjSchedulerContext context)
	{
		if (XjAptitudeSeedLane.PendingCount <= 0)
		{
			return;
		}

		int budget = context.IsYearChange
			? SeedYearBudget
			: context.ProcessGrowth
				? SeedGrowthBudget
				: SeedFastBudget;
		int minimumBudget = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 2,
			XjRuntimeStressTier.Severe => 4,
			XjRuntimeStressTier.Mild => 8,
			_ => 12
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 0.08d,
			XjRuntimeStressTier.Severe => 0.14d,
			XjRuntimeStressTier.Mild => 0.25d,
			_ => 0.5d
		};
		XjAptitudeSeedLane.Tick(
			XjRuntimeWorkBudget.ScaleCount(budget, minimumBudget),
			XjRuntimeWorkBudget.ScaleMilliseconds(SeedLaneTimeBudgetMs, minimumMilliseconds));
	}

	private static void TickAnnualCultivatorSweep()
	{
		int minimumBudget = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 64,
			XjRuntimeStressTier.Severe => 96,
			XjRuntimeStressTier.Mild => 160,
			_ => 256
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 0.12d,
			XjRuntimeStressTier.Severe => 0.18d,
			XjRuntimeStressTier.Mild => 0.25d,
			_ => 0.35d
		};
		XjAnnualCultivatorSweepLane.Tick(
			XjRuntimeWorkBudget.ScaleCount(AnnualCultivatorSweepBudget, minimumBudget),
			XjRuntimeWorkBudget.ScaleMilliseconds(AnnualCultivatorSweepTimeBudgetMs, minimumMilliseconds));
	}

	private static void TickCultivators(in XjSchedulerContext context)
	{
		if (!XjDetectionGate.ShouldProcessAnnualActors(context))
		{
			return;
		}

		if (AnnualActorQueue.Count == 0)
		{
			TickAutoCollectRechecks(context);
			return;
		}

		// 年度队列一旦覆盖大量修士，就不能继续按照普通后台任务降到近乎
		// 零预算，否则高倍速世界会永远追不上年份。仍由墙钟预算硬截断，
		// 但在大队列时保证每帧获得一小段稳定时间。
		bool catchUpFrame = context.ProcessFast && !context.IsYearChange;
		int heavyBacklogThreshold = Math.Max(256, XjCultivatorCache.Count / 4);
		bool heavyBacklog = AnnualActorQueue.Count >= heavyBacklogThreshold;
		int minimumStages = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => heavyBacklog ? 48 : (catchUpFrame ? 8 : 12),
			XjRuntimeStressTier.Severe => heavyBacklog ? 72 : (catchUpFrame ? 12 : 20),
			XjRuntimeStressTier.Mild => heavyBacklog ? 112 : (catchUpFrame ? 24 : 36),
			_ => heavyBacklog ? 160 : (catchUpFrame ? 48 : 64)
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => heavyBacklog ? 0.45d : (catchUpFrame ? 0.10d : 0.14d),
			XjRuntimeStressTier.Severe => heavyBacklog ? 0.60d : (catchUpFrame ? 0.16d : 0.22d),
			XjRuntimeStressTier.Mild => heavyBacklog ? 0.80d : (catchUpFrame ? 0.24d : 0.36d),
			_ => heavyBacklog ? 1.05d : (catchUpFrame ? 0.45d : 0.75d)
		};
		int baseStageBudget = catchUpFrame ? AnnualStageBudget / 2 : AnnualStageBudget;
		double baseTimeBudgetMs = catchUpFrame ? AnnualLaneTimeBudgetMs * 0.55d : AnnualLaneTimeBudgetMs;
		int stageBudget = XjRuntimeWorkBudget.ScaleCount(baseStageBudget, minimumStages);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(baseTimeBudgetMs, minimumMilliseconds);
		int processedStages = 0;
		long annualStarted = Stopwatch.GetTimestamp();
		while (AnnualActorQueue.Count > 0 && processedStages < stageBudget)
		{
			long actorId = AnnualActorQueue.Dequeue();
			if (!AnnualActorStates.TryGetValue(actorId, out AnnualActorState state))
			{
				continue;
			}
			state.Queued = false;
			processedStages++;
			if (!XjActorRegistry.Resolve(actorId, out Actor actor))
			{
				AnnualActorStates.Remove(actorId);
			}
			else if (!XjCultivatorCache.IsCultivator(actorId) && !XjCultivatorCache.CheckAndUpdate(actor))
			{
				// The actor is no longer a cultivator. Retire every registered annual
				// request instead of leaving an old completion cursor that would grant
				// retroactive cultivation years if the actor is re-enabled later.
				CompletePersistedAnnualState(
					actor,
					Math.Max(state.LastCompletedYear, state.LatestRequestedYear));
				AnnualActorStates.Remove(actorId);
			}
			else
			{
				int liveYear = Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0);
				if (liveYear > state.LatestRequestedYear)
				{
					state.LatestRequestedYear = liveYear;
				}
				if (state.Stage == XjAnnualPipelineStage.Prepare
					&& state.ActiveYear < state.LatestRequestedYear)
				{
					state.ActiveYear = state.LatestRequestedYear;
				}

				XjRuntimeHotspot hotspot = state.Stage switch
				{
					XjAnnualPipelineStage.Progression => XjRuntimeHotspot.AnnualProgression,
					XjAnnualPipelineStage.Finalize => XjRuntimeHotspot.AnnualFinalize,
					_ => XjRuntimeHotspot.AnnualPrepare
				};
				long sampleStarted = XjRuntimeDiagnostics.BeginSample(hotspot, 31);
				bool hasNext = XjSchedulerActorPipeline.ProcessStage(
					actor,
					state.ActiveYear,
					state.Stage,
					JinDanCombatLane.EnqueueActor,
					out XjAnnualPipelineStage nextStage);
				XjRuntimeDiagnostics.EndSample(hotspot, sampleStarted);
				if (hasNext)
				{
					state.Stage = nextStage;
					QueueAnnualState(actorId, state);
				}
				else
				{
					state.LastCompletedYear = Math.Max(state.LastCompletedYear, state.ActiveYear);
					if (state.LatestRequestedYear > state.LastCompletedYear)
					{
						state.ActiveYear = ResolveNextAnnualActiveYear(state.LastCompletedYear, state.LatestRequestedYear);
						state.Stage = XjAnnualPipelineStage.Prepare;
						QueueAnnualState(actorId, state);
					}
					else
					{
						CompletePersistedAnnualState(actor, state.LastCompletedYear);
						AnnualActorStates.Remove(actorId);
					}
				}
			}

			// A single annual stage can touch family, sect and warehouse state. Check
			// the wall-clock budget after every stage so eight expensive actors cannot
			// monopolize one rendered frame on high simulation speed.
			if (HasExceededTimeBudget(annualStarted, timeBudgetMs))
			{
				break;
			}
		}

		TickAutoCollectRechecks(context);
	}

	private static void TickAutoCollectRechecks(in XjSchedulerContext context)
	{
		if ((!context.ProcessFast && !context.IsYearChange) || AutoCollectRecheckQueue.Count == 0)
		{
			return;
		}

		int budget = XjRuntimeWorkBudget.ScaleCount(AutoCollectRecheckBudget, 32);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(AutoCollectRecheckTimeBudgetMs, 0.35d);
		long started = Stopwatch.GetTimestamp();
		long sampleStarted = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AutoCollectRecheck, 0);
		int processed = 0;
		while (AutoCollectRecheckQueue.Count > 0 && processed < budget)
		{
			long actorId = AutoCollectRecheckQueue.Dequeue();
			if (!AutoCollectRecheckSet.Remove(actorId))
			{
				continue;
			}
			processed++;
			if (XjActorRegistry.Resolve(actorId, out Actor actor)
				&& actor?.data != null
				&& actor.isAlive())
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
				XjAutoCollectSystem.TickActor(actor, realmId);
			}

			if ((processed & 15) == 0 && HasExceededTimeBudget(started, timeBudgetMs))
			{
				break;
			}
		}
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AutoCollectRecheck, sampleStarted);
	}

	private static bool HasExceededTimeBudget(long startedTimestamp, double budgetMilliseconds)
	{
		long elapsedTicks = Stopwatch.GetTimestamp() - startedTimestamp;
		return elapsedTicks > budgetMilliseconds * Stopwatch.Frequency / 1000.0;
	}

	private static void TickBoundedRuntime(in XjSchedulerContext context)
	{
		if (HasCriticalDeathWork)
		{
			XjDeathActorResolver resolver = ResolveActor;
			int deathBudget = XjRuntimeWorkBudget.ScaleCount(DeathLaneBudget, 1);
			long deathSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.DeathRuntime, 0);
			TickOnePendingDeathLane(deathBudget, resolver);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.DeathRuntime, deathSample);
			bool archiveWorkedThisFrame = false;
			if (XjDeathArchiveQueue.HasPending)
			{
				long archiveSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.DeathArchive, 0);
				XjDeathArchiveQueue.Tick(XjRuntimeWorkBudget.ScaleCount(DeathArchiveBudget, 2));
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.DeathArchive, archiveSample);
				archiveWorkedThisFrame = true;
			}
			// 金性入库与公告必须落到后续帧，避免和死亡归档、装备转移、存档保护提交叠在同一帧。
			if (!archiveWorkedThisFrame && XjJinDanResidualDeathLane.HasPending) XjJinDanResidualDeathLane.Tick(1);
		}

		if (context.ProcessGrowth && XjCombatRegenerationSystem.HasPending)
		{
			long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.CombatRegeneration, 0);
			XjCombatRegenerationSystem.Tick(
				XjRuntimeWorkBudget.ScaleCount(CombatRegenerationBudget, 12),
				ResolveActorDirect);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.CombatRegeneration, sample);
		}
		if (context.ProcessGrowth && XjClosedCultivationGuard.HasActive)
		{
			long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.ClosedCultivation, 0);
			XjClosedCultivationGuard.TickRuntime(
				XjRuntimeWorkBudget.ScaleCount(ClosedCultivationBudget, 2),
				ResolveActorDirect);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.ClosedCultivation, sample);
		}
		if (context.ProcessGrowth && JinDanCombatLane.HasPending)
		{
			long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.JinDanCombat, 0);
			JinDanCombatLane.Tick(XjRuntimeWorkBudget.ScaleCount(JinDanCombatBudget, 8));
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.JinDanCombat, sample);
		}
		if (context.ProcessFast && XjDomainSkillRuntime.HasActive)
		{
			XjDomainSkillRuntime.Tick(XjRuntimeWorkBudget.ScaleCount(DomainRuntimeBudget, 1));
		}
		// 年度世界车道是高倍速下的控制面，不再与普通后台十相位争抢唯一槽位。
		// 每个 fast pass 只给它一个严格的阶段数与墙钟预算，既能持续追赶年份，
		// 又不会把多个重任务无界叠到同一渲染帧。
		if (context.ProcessFast && !XjWorldBootstrapLane.HasPending && XjAnnualWorldRuntimeLane.HasPending)
		{
			long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundAnnualWorld, 0);
			XjAnnualWorldRuntimeLane.Tick(
				XjRuntimeWorkBudget.ScaleCount(AnnualWorldStageBudget, 2),
				XjRuntimeWorkBudget.ScaleMilliseconds(AnnualWorldTimeBudgetMs, 0.20d));
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundAnnualWorld, sample);
		}

		if (context.ProcessFast && XjRuntimeWorkBudget.ShouldRunBackgroundPhase())
		{
			TickBackgroundRuntime();
		}
	}

	private static void TickOnePendingDeathLane(int budget, XjDeathActorResolver resolver)
	{
		// Death sources are mutually independent. Drain one non-empty lane per
		// cadence to avoid three death finalizers piling onto the same frame.
		for (int attempt = 0; attempt < 4; attempt++)
		{
			int phase = _deathPhase;
			_deathPhase = (_deathPhase + 1) % 4;
			switch (phase)
			{
				case 0 when XjZiFuFailureDeathLane.HasPending:
					XjZiFuFailureDeathLane.Tick(budget, resolver);
					return;
				case 1 when XjRenDanDeathLane.HasPending:
					XjRenDanDeathLane.Tick(budget, resolver);
					return;
				case 2 when XjQiYuDongTianDeathLane.HasPending:
					XjQiYuDongTianDeathLane.Tick(budget, resolver);
					return;
				case 3 when XjShenDanDeathLane.HasPending:
					XjShenDanDeathLane.Tick(budget, resolver);
					return;
			}
		}
	}

	private static void TickBackgroundRuntime()
	{
		// 玩家主动点击“重新照览天下”时优先发布一次，不再等待十相位轮转。
		if (!XjWorldBootstrapLane.HasPending && XjCodexSnapshotPublisher.HasManualRefreshPending)
		{
			long manualSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundCodexSnapshot, 0);
			XjCodexSnapshotPublisher.Tick();
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundCodexSnapshot, manualSample);
			return;
		}

		// 一次 fast cadence 只运行一个后台相位；空相位直接跳过。
		for (int attempt = 0; attempt < 10; attempt++)
		{
			int phase = _backgroundPhase;
			_backgroundPhase = (_backgroundPhase + 1) % 10;
			switch (phase)
			{
			case 0 when _longShuYearMaintenancePending || _pendingLongShuGrowthPasses > 0 || XjLongShuSystem.HasPendingSpawnWork:
				{
					int growthPasses = _pendingLongShuGrowthPasses;
					bool yearMaintenance = _longShuYearMaintenancePending;
					_pendingLongShuGrowthPasses = 0;
					_longShuYearMaintenancePending = false;
					long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundLongShu, 0);
					XjLongShuSystem.TickRuntime(yearMaintenance, growthPasses);
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundLongShu, sample);
					return;
				}
				case 1 when !XjWorldBootstrapLane.HasPending && XjDongTianRegistry.NeedsRuntimeTick:
				{
					int worldYear = World.world?.map_stats?.year ?? 0;
					if (!XjDetectionGate.ShouldProcessDongTianRuntime(XjDongTianRegistry.HasActiveRuntimeYear, worldYear))
					{
						return;
					}
					long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundDongTian, 0);
					XjDongTianRegistry.TickRuntime(XjRuntimeWorkBudget.ScaleCount(DongTianRuntimeBudget, 2));
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundDongTian, sample);
					return;
				}
				case 2 when XjCaiQiCityMaintenanceLane.HasPending:
				{
					long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundCaiQiCity, 0);
					XjCaiQiCityMaintenanceLane.Tick(XjRuntimeWorkBudget.ScaleCount(CaiQiCityMaintenanceBudget, 1));
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundCaiQiCity, sample);
					return;
				}
				case 3 when XjCaiQiSiteRegistry.HasPendingRegistrations:
				{
					long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundCaiQiSite, 0);
					XjCaiQiSiteRegistry.TickRegistrationLane(XjRuntimeWorkBudget.ScaleCount(CaiQiSiteRegistrationBudget, 2));
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundCaiQiSite, sample);
					return;
				}
				case 4 when XjBroadcastSystem.HasPendingAnnouncements:
				{
					long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundBroadcast, 0);
					XjBroadcastSystem.TickCriticalAnnouncements();
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundBroadcast, sample);
					return;
				}
				case 5 when !XjWorldBootstrapLane.HasPending && XjZongMenRuntimeLane.HasPending:
				{
					long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundZongMen, 0);
					XjZongMenRuntimeLane.Tick(XjRuntimeWorkBudget.ScaleCount(ZongMenCityVisitBudget, 1));
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundZongMen, sample);
					return;
				}
				case 6 when XjYinSiTraitLifecycle.HasPendingRuntimeWork:
				{
					long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundYinSi, 0);
					XjYinSiTraitLifecycle.TickPendingSpawns();
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundYinSi, sample);
					return;
				}
				case 7 when !XjWorldBootstrapLane.HasPending && XjSectGovernanceRuntimeLane.HasPending:
				{
					long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundSectGovernance, 0);
					XjSectGovernanceRuntimeLane.Tick(XjRuntimeWorkBudget.ScaleCount(8, 2));
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundSectGovernance, sample);
					return;
				}
				case 8 when !XjWorldBootstrapLane.HasPending && XjCodexSnapshotPublisher.HasPending:
				{
					long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundCodexSnapshot, 0);
					XjCodexSnapshotPublisher.Tick();
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundCodexSnapshot, sample);
					return;
				}
				case 9 when XjHighRealmArmyExclusion.HasPending:
				{
					XjHighRealmArmyExclusion.TickQueue(XjRuntimeWorkBudget.ScaleCount(64, 12));
					return;
				}
			}
		}
	}

	private static void TickCleanup()
	{
		_tickCounter++;
		if (!XjDetectionGate.ShouldRunCadence(XjRuntimeDetectionJob.ActorRegistryCleanup, _tickCounter))
		{
			return;
		}

		_tickCounter = 0;
		XjActorRegistry.CleanupInvalid(CleanupRemoveBudget, ForgetActorRuntimeState);
	}

	private static Actor ResolveActorDirect(long actorId)
	{
		XjActorRegistry.Resolve(actorId, out Actor actor);
		return actor;
	}

	private static bool IsJinDanActor(Actor actor)
	{
		return actor?.data != null
			&& XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetString(
				actor,
				XuanJianVNext.Data.Rules.XjActorDataKeys.RealmId,
				out string realmId)
			&& (string.Equals(realmId, XuanJianVNext.Data.Rules.XjRealmIds.JinDan, StringComparison.Ordinal)
				|| string.Equals(realmId, XuanJianVNext.Data.Rules.XjRealmIds.ShenDan, StringComparison.Ordinal));
	}
}
