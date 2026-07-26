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
		internal bool InFlight;
		internal bool ReleaseRequested;
	}

	private sealed class AnnualMaintenanceState
	{
		internal int FromYearInclusive;
		internal int ActiveYear;
		internal int LatestRequestedYear;
		internal int LastCompletedYear;
		internal XjAnnualMaintenanceStage Stage;
		internal bool Queued;
		internal bool InFlight;
		internal bool ReleaseRequested;
	}

	/// <summary>
	/// Exact-year secondary gameplay cursor. These tasks change proficiency,
	/// production or annual chance outcomes, so unlike compatibility maintenance
	/// every completed core year is consumed in order.
	/// </summary>
	private sealed class AnnualSecondaryState
	{
		internal int ActiveYear;
		internal int LatestRequestedYear;
		internal int LastCompletedYear;
		internal bool Queued;
		internal bool InFlight;
		internal bool ReleaseRequested;
	}

	private const int CleanupRemoveBudget = 24;
	private const int BootstrapBudget = 96;
	private const double BootstrapTimeBudgetMs = 0.9;
	private const int SeedFastBudget = 20;
	private const int SeedGrowthBudget = 48;
	private const int SeedYearBudget = 80;
	private const double SeedLaneTimeBudgetMs = 0.65;
	private const int AnnualCoreStageBudget = 192;
	private const double AnnualCoreLaneTimeBudgetMs = 0.95;
	private const int AnnualSecondaryActorBudget = 192;
	private const double AnnualSecondaryLaneTimeBudgetMs = 0.70;
	private const int AnnualMaintenanceStageBudget = 288;
	private const double AnnualMaintenanceLaneTimeBudgetMs = 0.70;
	private const int AnnualCultivatorSweepBudget = 384;
	private const double AnnualCultivatorSweepTimeBudgetMs = 0.35;
	private const int AnnualStatePoolLimit = 32768;
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
	private static int _actorProgressionRevision;
	private static readonly Queue<long> AnnualActorQueue = new Queue<long>();
	private static readonly Dictionary<long, AnnualActorState> AnnualActorStates = new Dictionary<long, AnnualActorState>();
	private static readonly Stack<AnnualActorState> AnnualActorStatePool = new Stack<AnnualActorState>();
	private static readonly Dictionary<long, int> DeferredAnnualCompletions = new Dictionary<long, int>();
	private static readonly Queue<long> AnnualSecondaryQueue = new Queue<long>();
	private static readonly Dictionary<long, AnnualSecondaryState> AnnualSecondaryStates = new Dictionary<long, AnnualSecondaryState>();
	private static readonly Stack<AnnualSecondaryState> AnnualSecondaryStatePool = new Stack<AnnualSecondaryState>();
	private static readonly Dictionary<long, int> DeferredAnnualSecondaryCompletions = new Dictionary<long, int>();
	private static readonly Queue<long> AnnualMaintenanceQueue = new Queue<long>();
	private static readonly Dictionary<long, AnnualMaintenanceState> AnnualMaintenanceStates = new Dictionary<long, AnnualMaintenanceState>();
	private static readonly Stack<AnnualMaintenanceState> AnnualMaintenanceStatePool = new Stack<AnnualMaintenanceState>();
	private static readonly Dictionary<long, int> DeferredAnnualMaintenanceCompletions = new Dictionary<long, int>();
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
		|| XjStageZeroObservation.HasPending
		|| AnnualActorQueue.Count > 0
		|| AnnualSecondaryQueue.Count > 0
		|| AnnualMaintenanceQueue.Count > 0
		|| AutoCollectRecheckQueue.Count > 0
		|| HasCriticalDeathWork
		|| HasBackgroundWork;

	internal static bool HasUrgentSimulationBacklog => HasCriticalDeathWork;

	internal static bool HasAnnualActorBacklog => AnnualActorQueue.Count > 0
		|| AnnualSecondaryQueue.Count > 0
		|| AnnualMaintenanceQueue.Count > 0
		|| XjAnnualCultivatorSweepLane.HasPending;

	internal static bool HasAnnualRecoveryBacklog
	{
		get
		{
			int threshold = Math.Max(256, XjCultivatorCache.Count / 4);
			return XjAnnualCultivatorSweepLane.HasPending
				|| AnnualActorQueue.Count >= threshold
				|| AnnualSecondaryQueue.Count >= threshold
				|| AnnualMaintenanceQueue.Count >= threshold;
		}
	}

	internal static int ActorProgressionRevision => _actorProgressionRevision;

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

	private static AnnualActorState RentAnnualActorState(
		int activeYear,
		int latestRequestedYear,
		int lastCompletedYear,
		XjAnnualPipelineStage stage)
	{
		bool reused = AnnualActorStatePool.Count > 0;
		AnnualActorState state = reused ? AnnualActorStatePool.Pop() : new AnnualActorState();
		state.ActiveYear = activeYear;
		state.LatestRequestedYear = latestRequestedYear;
		state.LastCompletedYear = lastCompletedYear;
		state.Stage = stage;
		state.Queued = false;
		state.InFlight = false;
		state.ReleaseRequested = false;
		XjStageZeroObservation.RecordAnnualStateRent(maintenance: false, reused: reused);
		return state;
	}

	private static AnnualSecondaryState RentAnnualSecondaryState(
		int activeYear,
		int latestRequestedYear,
		int lastCompletedYear)
	{
		AnnualSecondaryState state = AnnualSecondaryStatePool.Count > 0
			? AnnualSecondaryStatePool.Pop()
			: new AnnualSecondaryState();
		state.ActiveYear = activeYear;
		state.LatestRequestedYear = latestRequestedYear;
		state.LastCompletedYear = lastCompletedYear;
		state.Queued = false;
		state.InFlight = false;
		state.ReleaseRequested = false;
		return state;
	}

	private static AnnualMaintenanceState RentAnnualMaintenanceState(
		int fromYearInclusive,
		int activeYear,
		int latestRequestedYear,
		int lastCompletedYear,
		XjAnnualMaintenanceStage stage)
	{
		bool reused = AnnualMaintenanceStatePool.Count > 0;
		AnnualMaintenanceState state = reused ? AnnualMaintenanceStatePool.Pop() : new AnnualMaintenanceState();
		state.FromYearInclusive = fromYearInclusive;
		state.ActiveYear = activeYear;
		state.LatestRequestedYear = latestRequestedYear;
		state.LastCompletedYear = lastCompletedYear;
		state.Stage = stage;
		state.Queued = false;
		state.InFlight = false;
		state.ReleaseRequested = false;
		XjStageZeroObservation.RecordAnnualStateRent(maintenance: true, reused: reused);
		return state;
	}

	private static void ReleaseAnnualActorState(AnnualActorState state)
	{
		if (state == null) return;
		state.ActiveYear = 0;
		state.LatestRequestedYear = 0;
		state.LastCompletedYear = 0;
		state.Stage = XjAnnualPipelineStage.Prepare;
		state.Queued = false;
		state.InFlight = false;
		state.ReleaseRequested = false;
		if (AnnualActorStatePool.Count < AnnualStatePoolLimit)
		{
			AnnualActorStatePool.Push(state);
		}
	}

	private static void ReleaseAnnualSecondaryState(AnnualSecondaryState state)
	{
		if (state == null) return;
		state.ActiveYear = 0;
		state.LatestRequestedYear = 0;
		state.LastCompletedYear = 0;
		state.Queued = false;
		state.InFlight = false;
		state.ReleaseRequested = false;
		if (AnnualSecondaryStatePool.Count < AnnualStatePoolLimit)
		{
			AnnualSecondaryStatePool.Push(state);
		}
	}

	private static void ReleaseAnnualMaintenanceState(AnnualMaintenanceState state)
	{
		if (state == null) return;
		state.FromYearInclusive = 0;
		state.ActiveYear = 0;
		state.LatestRequestedYear = 0;
		state.LastCompletedYear = 0;
		state.Stage = XjAnnualMaintenanceStage.Identity;
		state.Queued = false;
		state.InFlight = false;
		state.ReleaseRequested = false;
		if (AnnualMaintenanceStatePool.Count < AnnualStatePoolLimit)
		{
			AnnualMaintenanceStatePool.Push(state);
		}
	}

	private static bool RemoveAnnualActorState(long actorId)
	{
		if (!AnnualActorStates.TryGetValue(actorId, out AnnualActorState state)) return false;
		AnnualActorStates.Remove(actorId);
		if (state.InFlight) state.ReleaseRequested = true;
		else ReleaseAnnualActorState(state);
		return true;
	}

	private static bool RemoveAnnualSecondaryState(long actorId)
	{
		if (!AnnualSecondaryStates.TryGetValue(actorId, out AnnualSecondaryState state)) return false;
		AnnualSecondaryStates.Remove(actorId);
		if (state.InFlight) state.ReleaseRequested = true;
		else ReleaseAnnualSecondaryState(state);
		return true;
	}

	private static bool RemoveAnnualMaintenanceState(long actorId)
	{
		if (!AnnualMaintenanceStates.TryGetValue(actorId, out AnnualMaintenanceState state)) return false;
		AnnualMaintenanceStates.Remove(actorId);
		if (state.InFlight) state.ReleaseRequested = true;
		else ReleaseAnnualMaintenanceState(state);
		return true;
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
			if (TryReadPersistedPendingAnnualState(actor, out state)
				&& state.Stage == XjAnnualPipelineStage.Finalize)
			{
				AnnualActorState legacyState = state;
				state = MigrateLegacyFinalizeState(actor, actorId, legacyState);
				if (state == null) ReleaseAnnualActorState(legacyState);
			}

			if (state == null)
			{
				int persistedCompletedYear = ReadEffectiveLastCompletedYear(actor);
				EnsureAnnualSecondaryStateLoaded(actor, actorId, persistedCompletedYear);
				EnsureAnnualMaintenanceStateLoaded(actor, actorId, persistedCompletedYear);
				if (requestedYear <= persistedCompletedYear)
				{
					return;
				}

				state = RentAnnualActorState(
					ResolveNextAnnualActiveYear(persistedCompletedYear, requestedYear),
					requestedYear,
					persistedCompletedYear,
					XjAnnualPipelineStage.Prepare);
			}

			state.LatestRequestedYear = Math.Max(state.LatestRequestedYear, requestedYear);
			AnnualActorStates[actorId] = state;
		}
		else
		{
			if (state.Stage == XjAnnualPipelineStage.Finalize)
			{
				AnnualActorStates.Remove(actorId);
				AnnualActorState legacyState = state;
				state = MigrateLegacyFinalizeState(actor, actorId, legacyState);
				if (state == null)
				{
					ReleaseAnnualActorState(legacyState);
					return;
				}
				AnnualActorStates[actorId] = state;
			}

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
		int effectiveCoreCompletedYear = ReadEffectiveLastCompletedYear(actor);
		EnsureAnnualSecondaryStateLoaded(actor, actorId, effectiveCoreCompletedYear);
		EnsureAnnualMaintenanceStateLoaded(actor, actorId, effectiveCoreCompletedYear);

		if (AnnualActorStates.TryGetValue(actorId, out AnnualActorState existingState))
		{
			if (existingState.Stage == XjAnnualPipelineStage.Finalize)
			{
				AnnualActorStates.Remove(actorId);
				AnnualActorState legacyState = existingState;
				existingState = MigrateLegacyFinalizeState(actor, actorId, legacyState);
				if (existingState == null)
				{
					ReleaseAnnualActorState(legacyState);
					return;
				}
				AnnualActorStates[actorId] = existingState;
			}
			if (existingState.ActiveYear < qualifiedYear)
			{
				existingState.LastCompletedYear = Math.Max(existingState.LastCompletedYear, qualifiedYear - 1);
				existingState.ActiveYear = qualifiedYear;
				existingState.Stage = XjAnnualPipelineStage.Prepare;
			}
			EnsureAnnualSecondaryBaseline(actor, existingState.LastCompletedYear);
			EnqueueAnnualSecondary(actor, actorId, existingState.LastCompletedYear);
			EnsureAnnualMaintenanceBaseline(actor, existingState.LastCompletedYear);
			EnqueueAnnualMaintenance(actor, actorId, existingState.LastCompletedYear);
			QueueAnnualState(actorId, existingState);
			return;
		}

		int lastCompletedYear = ReadEffectiveLastCompletedYear(actor);
		if (TryReadPersistedPendingAnnualState(actor, out AnnualActorState persistedState))
		{
			if (persistedState.Stage == XjAnnualPipelineStage.Finalize)
			{
				AnnualActorState legacyState = persistedState;
				persistedState = MigrateLegacyFinalizeState(actor, actorId, legacyState);
				if (persistedState == null)
				{
					ReleaseAnnualActorState(legacyState);
					return;
				}
			}
			else
			{
				EnsureAnnualSecondaryBaseline(actor, persistedState.LastCompletedYear);
				EnqueueAnnualSecondary(actor, actorId, persistedState.LastCompletedYear);
				EnsureAnnualMaintenanceBaseline(actor, persistedState.LastCompletedYear);
				EnqueueAnnualMaintenance(actor, actorId, persistedState.LastCompletedYear);
			}
			AnnualActorStates[actorId] = persistedState;
			QueueAnnualState(actorId, persistedState);
			return;
		}

		// Idle cultivators do not occupy runtime state. Legacy saves have no
		// maintenance cursor, so completed years are baselined as already maintained
		// rather than replaying centuries of family/equipment compatibility work.
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
			inferredCompletedYear = Math.Max(0, baselineYear);
		}
		EnsureAnnualSecondaryBaseline(actor, inferredCompletedYear);
		EnqueueAnnualSecondary(actor, actorId, inferredCompletedYear);
		EnsureAnnualMaintenanceBaseline(actor, inferredCompletedYear);
		EnqueueAnnualMaintenance(actor, actorId, inferredCompletedYear);

		if (baselineYear > inferredCompletedYear)
		{
			AnnualActorState catchUpState = RentAnnualActorState(
				ResolveNextAnnualActiveYear(inferredCompletedYear, baselineYear),
				baselineYear,
				inferredCompletedYear,
				XjAnnualPipelineStage.Prepare);
			AnnualActorStates[actorId] = catchUpState;
			QueueAnnualState(actorId, catchUpState);
			return;
		}

		CompletePersistedAnnualState(actor, inferredCompletedYear);
		RemoveAnnualActorState(actorId);
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

		state = RentAnnualActorState(
			normalizedActiveYear,
			latestYear,
			Math.Max(0, lastCompletedYear),
			(XjAnnualPipelineStage)stageValue);
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

	private static AnnualActorState MigrateLegacyFinalizeState(Actor actor, long actorId, AnnualActorState legacyState)
	{
		if (actor?.data == null || legacyState == null || actorId <= 0L)
		{
			return null;
		}

		int completedCoreYear = Math.Max(legacyState.LastCompletedYear, legacyState.ActiveYear);
		EnsureAnnualSecondaryStateLoaded(actor, actorId, Math.Max(0, legacyState.ActiveYear - 1));
		EnsureAnnualMaintenanceStateLoaded(actor, actorId, Math.Max(0, legacyState.ActiveYear - 1));
		CompletePersistedAnnualState(actor, completedCoreYear);
		EnqueueAnnualSecondary(actor, actorId, legacyState.ActiveYear);
		EnqueueAnnualMaintenance(actor, actorId, legacyState.ActiveYear);
		XjStageZeroObservation.RecordAnnualCoreCompletion(actor, legacyState.ActiveYear, migratedLegacyFinalize: true);

		if (legacyState.LatestRequestedYear <= completedCoreYear)
		{
			return null;
		}

		legacyState.ActiveYear = ResolveNextAnnualActiveYear(completedCoreYear, legacyState.LatestRequestedYear);
		legacyState.LastCompletedYear = completedCoreYear;
		legacyState.Stage = XjAnnualPipelineStage.Prepare;
		legacyState.Queued = false;
		return legacyState;
	}

	private static bool TryReadPersistedPendingAnnualSecondaryState(Actor actor, out AnnualSecondaryState state)
	{
		state = null;
		if (actor?.data == null) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualSecondaryActiveYear, out int activeYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualSecondaryLatestRequestedYear, out int latestYear);
		int lastCompletedYear = ReadEffectiveAnnualSecondaryLastCompletedYear(actor);
		if (activeYear <= lastCompletedYear || latestYear < activeYear) return false;
		state = RentAnnualSecondaryState(activeYear, latestYear, lastCompletedYear);
		return true;
	}

	private static void EnsureAnnualSecondaryStateLoaded(Actor actor, long actorId, int baselineYear)
	{
		if (actor?.data == null || actorId <= 0L) return;
		if (!AnnualSecondaryStates.ContainsKey(actorId)
			&& TryReadPersistedPendingAnnualSecondaryState(actor, out AnnualSecondaryState persistedState))
		{
			AnnualSecondaryStates[actorId] = persistedState;
			QueueAnnualSecondaryState(actorId, persistedState);
			return;
		}
		EnsureAnnualSecondaryBaseline(actor, baselineYear);
	}

	private static void EnsureAnnualSecondaryBaseline(Actor actor, int baselineYear)
	{
		if (actor?.data == null) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualSecondaryLastCompletedYear, out _)) return;
		int qualifiedYear = ReadCultivationQualifiedYear(actor);
		int normalized = Math.Max(Math.Max(0, baselineYear), qualifiedYear > 0 ? qualifiedYear - 1 : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualSecondaryLastCompletedYear, normalized);
	}

	private static int ReadEffectiveAnnualSecondaryLastCompletedYear(Actor actor)
	{
		if (actor?.data == null) return 0;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualSecondaryLastCompletedYear, out int persistedYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L && DeferredAnnualSecondaryCompletions.TryGetValue(actorId, out int deferredYear))
		{
			persistedYear = Math.Max(persistedYear, deferredYear);
		}
		int qualifiedYear = ReadCultivationQualifiedYear(actor);
		if (qualifiedYear > 0) persistedYear = Math.Max(persistedYear, qualifiedYear - 1);
		return Math.Max(0, persistedYear);
	}

	private static void EnqueueAnnualSecondary(Actor actor, long actorId, int requestedYear)
	{
		if (actor?.data == null || actorId <= 0L || requestedYear <= 0) return;
		int qualifiedYear = ReadCultivationQualifiedYear(actor);
		if (qualifiedYear > 0 && requestedYear < qualifiedYear) return;
		EnsureAnnualSecondaryBaseline(actor, Math.Max(0, requestedYear - 1));
		int lastCompletedYear = ReadEffectiveAnnualSecondaryLastCompletedYear(actor);
		if (requestedYear <= lastCompletedYear) return;

		// Actors without secondary gameplay still advance their exact-year cursor.
		// If they gain an interest later, only subsequent years become eligible; old
		// years from before the prerequisite existed are never fabricated.
		if (!XjSchedulerActorPipeline.HasSecondaryAnnualInterest(actor))
		{
			RemoveAnnualSecondaryState(actorId);
			CompleteAnnualSecondaryState(actor, requestedYear);
			return;
		}

		if (!AnnualSecondaryStates.TryGetValue(actorId, out AnnualSecondaryState state))
		{
			state = RentAnnualSecondaryState(lastCompletedYear + 1, requestedYear, lastCompletedYear);
			AnnualSecondaryStates[actorId] = state;
		}
		else
		{
			state.LatestRequestedYear = Math.Max(state.LatestRequestedYear, requestedYear);
		}
		QueueAnnualSecondaryState(actorId, state);
	}

	private static void CompleteAnnualSecondaryState(Actor actor, int completedYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int normalizedYear = Math.Max(0, completedYear);
		if (actorId <= 0L || normalizedYear <= 0) return;
		if (!DeferredAnnualSecondaryCompletions.TryGetValue(actorId, out int previousYear) || normalizedYear > previousYear)
		{
			DeferredAnnualSecondaryCompletions[actorId] = normalizedYear;
		}
	}

	private static void FlushDeferredAnnualSecondaryCompletion(long actorId)
	{
		if (actorId <= 0L || !DeferredAnnualSecondaryCompletions.TryGetValue(actorId, out int completedYear)) return;
		if (XjActorRegistry.Resolve(actorId, out Actor actor) && actor?.data != null)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualSecondaryLastCompletedYear, completedYear);
		}
		DeferredAnnualSecondaryCompletions.Remove(actorId);
	}

	private static void PersistAnnualSecondaryState(Actor actor, AnnualSecondaryState state)
	{
		if (actor?.data == null || state == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int lastCompletedYear = state.LastCompletedYear;
		if (actorId > 0L && DeferredAnnualSecondaryCompletions.TryGetValue(actorId, out int deferredYear))
		{
			lastCompletedYear = Math.Max(lastCompletedYear, deferredYear);
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualSecondaryActiveYear, Math.Max(0, state.ActiveYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualSecondaryLatestRequestedYear, Math.Max(0, state.LatestRequestedYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualSecondaryLastCompletedYear, Math.Max(0, lastCompletedYear));
	}

	private static bool TryReadPersistedPendingAnnualMaintenanceState(Actor actor, out AnnualMaintenanceState state)
	{
		state = null;
		if (actor?.data == null) return false;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualMaintenanceFromYear, out int fromYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualMaintenanceActiveYear, out int activeYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualMaintenanceLatestRequestedYear, out int latestYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualMaintenanceStage, out int stageValue);
		int lastCompletedYear = ReadEffectiveAnnualMaintenanceLastCompletedYear(actor);
		int qualifiedYear = ReadCultivationQualifiedYear(actor);
		if (activeYear <= lastCompletedYear
			|| latestYear < activeYear
			|| stageValue < (int)XjAnnualMaintenanceStage.Identity
			|| stageValue > (int)XjAnnualMaintenanceStage.Assets)
		{
			return false;
		}

		int minimumFromYear = Math.Max(lastCompletedYear + 1, qualifiedYear > 0 ? qualifiedYear : 1);
		state = RentAnnualMaintenanceState(
			Math.Max(minimumFromYear, fromYear > 0 ? fromYear : minimumFromYear),
			activeYear,
			latestYear,
			lastCompletedYear,
			(XjAnnualMaintenanceStage)stageValue);
		return true;
	}

	private static void EnsureAnnualMaintenanceStateLoaded(Actor actor, long actorId, int baselineYear)
	{
		if (actor?.data == null || actorId <= 0L) return;
		if (!AnnualMaintenanceStates.ContainsKey(actorId)
			&& TryReadPersistedPendingAnnualMaintenanceState(actor, out AnnualMaintenanceState persistedState))
		{
			AnnualMaintenanceStates[actorId] = persistedState;
			QueueAnnualMaintenanceState(actorId, persistedState);
			return;
		}

		EnsureAnnualMaintenanceBaseline(actor, baselineYear);
		EnqueueAnnualMaintenance(actor, actorId, baselineYear);
	}

	private static void PersistAnnualMaintenanceState(Actor actor, AnnualMaintenanceState state)
	{
		if (actor?.data == null || state == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int lastCompletedYear = state.LastCompletedYear;
		if (actorId > 0L
			&& DeferredAnnualMaintenanceCompletions.TryGetValue(actorId, out int deferredYear))
		{
			lastCompletedYear = Math.Max(lastCompletedYear, deferredYear);
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualMaintenanceFromYear, Math.Max(0, state.FromYearInclusive));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualMaintenanceActiveYear, Math.Max(0, state.ActiveYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualMaintenanceLatestRequestedYear, Math.Max(0, state.LatestRequestedYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualMaintenanceStage, (int)state.Stage);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualMaintenanceLastCompletedYear, Math.Max(0, lastCompletedYear));
	}

	private static int ReadEffectiveAnnualMaintenanceLastCompletedYear(Actor actor)
	{
		if (actor?.data == null) return 0;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualMaintenanceLastCompletedYear, out int persistedYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L
			&& DeferredAnnualMaintenanceCompletions.TryGetValue(actorId, out int deferredYear))
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

	private static void EnsureAnnualMaintenanceBaseline(Actor actor, int baselineYear)
	{
		if (actor?.data == null) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualMaintenanceLastCompletedYear, out _))
		{
			return;
		}
		int qualifiedYear = ReadCultivationQualifiedYear(actor);
		int normalized = Math.Max(Math.Max(0, baselineYear), qualifiedYear > 0 ? qualifiedYear - 1 : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualMaintenanceLastCompletedYear, normalized);
	}

	private static void EnqueueAnnualMaintenance(Actor actor, long actorId, int requestedYear)
	{
		if (actor?.data == null || actorId <= 0L || requestedYear <= 0) return;
		int qualifiedYear = ReadCultivationQualifiedYear(actor);
		if (qualifiedYear > 0 && requestedYear < qualifiedYear) return;

		if (!AnnualMaintenanceStates.TryGetValue(actorId, out AnnualMaintenanceState state))
		{
			int lastCompletedYear = ReadEffectiveAnnualMaintenanceLastCompletedYear(actor);
			if (requestedYear <= lastCompletedYear) return;
			state = RentAnnualMaintenanceState(
				Math.Max(lastCompletedYear + 1, qualifiedYear > 0 ? qualifiedYear : 1),
				requestedYear,
				requestedYear,
				lastCompletedYear,
				XjAnnualMaintenanceStage.Identity);
			AnnualMaintenanceStates[actorId] = state;
		}
		else
		{
			// Once a maintenance cycle starts, keep its active year stable so annual
			// craft/weapon/talisman work is not silently retimed. New requests become
			// the next cycle and may still be range-coalesced after this cycle commits.
			state.LatestRequestedYear = Math.Max(state.LatestRequestedYear, requestedYear);
		}
		QueueAnnualMaintenanceState(actorId, state);
	}

	private static void CompleteAnnualMaintenanceState(Actor actor, int completedYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int normalizedYear = Math.Max(0, completedYear);
		if (actorId <= 0L || normalizedYear <= 0) return;
		if (!DeferredAnnualMaintenanceCompletions.TryGetValue(actorId, out int previousYear)
			|| normalizedYear > previousYear)
		{
			DeferredAnnualMaintenanceCompletions[actorId] = normalizedYear;
		}
	}

	private static void FlushDeferredAnnualMaintenanceCompletion(long actorId)
	{
		if (actorId <= 0L
			|| !DeferredAnnualMaintenanceCompletions.TryGetValue(actorId, out int completedYear))
		{
			return;
		}
		if (XjActorRegistry.Resolve(actorId, out Actor actor) && actor?.data != null)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualMaintenanceLastCompletedYear, completedYear);
		}
		DeferredAnnualMaintenanceCompletions.Remove(actorId);
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
	/// 真正保存前将核心与维护完成批次、以及仍在内存队列中的阶段游标一次性写入角色数据。
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
		foreach (KeyValuePair<long, int> pair in DeferredAnnualSecondaryCompletions)
		{
			if (XjActorRegistry.Resolve(pair.Key, out Actor actor) && actor?.data != null)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualSecondaryLastCompletedYear, pair.Value);
			}
		}
		foreach (KeyValuePair<long, AnnualSecondaryState> pair in AnnualSecondaryStates)
		{
			if (pair.Value != null && XjActorRegistry.Resolve(pair.Key, out Actor actor))
			{
				PersistAnnualSecondaryState(actor, pair.Value);
			}
		}
		foreach (KeyValuePair<long, int> pair in DeferredAnnualMaintenanceCompletions)
		{
			if (XjActorRegistry.Resolve(pair.Key, out Actor actor) && actor?.data != null)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualMaintenanceLastCompletedYear, pair.Value);
			}
		}
		foreach (KeyValuePair<long, AnnualMaintenanceState> pair in AnnualMaintenanceStates)
		{
			if (pair.Value != null && XjActorRegistry.Resolve(pair.Key, out Actor actor))
			{
				PersistAnnualMaintenanceState(actor, pair.Value);
			}
		}
		DeferredAnnualCompletions.Clear();
		DeferredAnnualSecondaryCompletions.Clear();
		DeferredAnnualMaintenanceCompletions.Clear();
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


	private static void QueueAnnualSecondaryState(long actorId, AnnualSecondaryState state)
	{
		if (state == null || state.Queued) return;
		state.Queued = true;
		AnnualSecondaryQueue.Enqueue(actorId);
	}

	private static void QueueAnnualMaintenanceState(long actorId, AnnualMaintenanceState state)
	{
		if (state == null || state.Queued) return;
		state.Queued = true;
		AnnualMaintenanceQueue.Enqueue(actorId);
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

	internal static bool TryGetAnnualObservationState(
		long actorId,
		out int activeYear,
		out int latestRequestedYear,
		out int lastCompletedYear,
		out XjAnnualPipelineStage stage)
	{
		activeYear = 0;
		latestRequestedYear = 0;
		lastCompletedYear = 0;
		stage = XjAnnualPipelineStage.Prepare;
		if (actorId <= 0L) return false;

		if (AnnualActorStates.TryGetValue(actorId, out AnnualActorState runtimeState))
		{
			activeYear = runtimeState.ActiveYear;
			latestRequestedYear = runtimeState.LatestRequestedYear;
			lastCompletedYear = runtimeState.LastCompletedYear;
			if (DeferredAnnualCompletions.TryGetValue(actorId, out int deferredYear))
			{
				lastCompletedYear = Math.Max(lastCompletedYear, deferredYear);
			}
			stage = runtimeState.Stage;
			return true;
		}

		if (!XjActorRegistry.Resolve(actorId, out Actor actor) || actor?.data == null)
		{
			return false;
		}
		lastCompletedYear = ReadEffectiveLastCompletedYear(actor);
		return true;
	}

	internal static bool TryGetAnnualMaintenanceObservationState(
		long actorId,
		out int activeYear,
		out int latestRequestedYear,
		out int lastCompletedYear,
		out XjAnnualMaintenanceStage stage)
	{
		activeYear = 0;
		latestRequestedYear = 0;
		lastCompletedYear = 0;
		stage = XjAnnualMaintenanceStage.Identity;
		if (actorId <= 0L) return false;
		if (AnnualMaintenanceStates.TryGetValue(actorId, out AnnualMaintenanceState runtimeState))
		{
			activeYear = runtimeState.ActiveYear;
			latestRequestedYear = runtimeState.LatestRequestedYear;
			lastCompletedYear = runtimeState.LastCompletedYear;
			if (DeferredAnnualMaintenanceCompletions.TryGetValue(actorId, out int deferredYear))
			{
				lastCompletedYear = Math.Max(lastCompletedYear, deferredYear);
			}
			stage = runtimeState.Stage;
			return true;
		}
		if (!XjActorRegistry.Resolve(actorId, out Actor actor) || actor?.data == null) return false;
		lastCompletedYear = ReadEffectiveAnnualMaintenanceLastCompletedYear(actor);
		return true;
	}

	internal static bool ResolveActor(long actorId, out Actor actor)
	{
		return XjActorRegistry.Resolve(actorId, out actor);
	}

	internal static void BaselineSecondaryAfterAmbiguousRealmChange(Actor actor, int transitionYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		int safeYear = Math.Max(0, transitionYear);
		RemoveAnnualSecondaryState(actorId);
		DeferredAnnualSecondaryCompletions.Remove(actorId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualSecondaryActiveYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualSecondaryLatestRequestedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAnnualSecondaryLastCompletedYear, safeYear);
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
		FlushDeferredAnnualSecondaryCompletion(actorId);
		FlushDeferredAnnualMaintenanceCompletion(actorId);
		RemoveAnnualActorState(actorId);
		RemoveAnnualSecondaryState(actorId);
		RemoveAnnualMaintenanceState(actorId);
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
			XjStageZeroObservation.OnYearChanged(
				currentYear,
				AnnualActorQueue.Count,
				AnnualMaintenanceQueue.Count,
				XjAnnualCultivatorSweepLane.PendingCount,
				XjAptitudeSeedLane.PendingCount);
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
		if (AnnualSecondaryQueue.Count > 0)
		{
			TickAnnualSecondary(context);
		}
		if (AnnualMaintenanceQueue.Count > 0)
		{
			TickAnnualMaintenance(context);
		}
		XjStageZeroObservation.SampleQueuePressure(
			AnnualActorQueue.Count,
			AnnualMaintenanceQueue.Count,
			XjAnnualCultivatorSweepLane.PendingCount,
			XjAptitudeSeedLane.PendingCount);
		if (XjStageZeroObservation.HasPending)
		{
			XjStageZeroObservation.Tick();
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
		XjRuntimeDiagnostics.SampleQueue("annualCore", AnnualActorQueue.Count);
		XjRuntimeDiagnostics.SampleQueue("annualSecondary", AnnualSecondaryQueue.Count);
		XjRuntimeDiagnostics.SampleQueue("annualMaintenance", AnnualMaintenanceQueue.Count);
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
		XjStageZeroObservation.Clear();
		_tickCounter = 0;
		_deathPhase = 0;
		_backgroundPhase = 0;
		_pendingLongShuGrowthPasses = 0;
		_longShuYearMaintenancePending = false;
		_actorProgressionRevision = 0;
		AnnualActorQueue.Clear();
		foreach (AnnualActorState state in AnnualActorStates.Values) ReleaseAnnualActorState(state);
		AnnualActorStates.Clear();
		AnnualActorStatePool.Clear();
		DeferredAnnualCompletions.Clear();
		AnnualSecondaryQueue.Clear();
		foreach (AnnualSecondaryState state in AnnualSecondaryStates.Values) ReleaseAnnualSecondaryState(state);
		AnnualSecondaryStates.Clear();
		AnnualSecondaryStatePool.Clear();
		DeferredAnnualSecondaryCompletions.Clear();
		AnnualMaintenanceQueue.Clear();
		foreach (AnnualMaintenanceState state in AnnualMaintenanceStates.Values) ReleaseAnnualMaintenanceState(state);
		AnnualMaintenanceStates.Clear();
		AnnualMaintenanceStatePool.Clear();
		DeferredAnnualMaintenanceCompletions.Clear();
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
		if (!XjDetectionGate.ShouldProcessAnnualActors(context)) return;
		if (AnnualActorQueue.Count == 0)
		{
			TickAutoCollectRechecks(context);
			return;
		}

		// Core cultivation owns an independent guaranteed budget. Maintenance work is
		// never allowed to consume this wall-clock slice or delay the completion cursor.
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
		int baseStageBudget = catchUpFrame ? AnnualCoreStageBudget / 2 : AnnualCoreStageBudget;
		double baseTimeBudgetMs = catchUpFrame ? AnnualCoreLaneTimeBudgetMs * 0.55d : AnnualCoreLaneTimeBudgetMs;
		int stageBudget = XjRuntimeWorkBudget.ScaleCount(baseStageBudget, minimumStages);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(baseTimeBudgetMs, minimumMilliseconds);
		int processedStages = 0;
		long annualStarted = Stopwatch.GetTimestamp();
		while (AnnualActorQueue.Count > 0 && processedStages < stageBudget)
		{
			long actorId = AnnualActorQueue.Dequeue();
			if (!AnnualActorStates.TryGetValue(actorId, out AnnualActorState state)) continue;
			state.Queued = false;
			state.InFlight = true;
			processedStages++;
			if (!XjActorRegistry.Resolve(actorId, out Actor actor))
			{
				RemoveAnnualActorState(actorId);
			}
			else if (!XjCultivatorCache.IsCultivator(actorId) && !XjCultivatorCache.CheckAndUpdate(actor))
			{
				int completedThroughYear = Math.Max(state.LastCompletedYear, state.LatestRequestedYear);
				CompletePersistedAnnualState(actor, completedThroughYear);
				EnsureAnnualSecondaryBaseline(actor, completedThroughYear);
				EnsureAnnualMaintenanceBaseline(actor, completedThroughYear);
				RemoveAnnualActorState(actorId);
			}
			else if (state.Stage == XjAnnualPipelineStage.Finalize)
			{
				AnnualActorStates.Remove(actorId);
				AnnualActorState migrated = MigrateLegacyFinalizeState(actor, actorId, state);
				if (migrated != null)
				{
					AnnualActorStates[actorId] = migrated;
					QueueAnnualState(actorId, migrated);
				}
				else
				{
					state.ReleaseRequested = true;
				}
			}
			else
			{
				int liveYear = Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0);
				if (liveYear > state.LatestRequestedYear) state.LatestRequestedYear = liveYear;
				if (state.Stage == XjAnnualPipelineStage.Prepare && state.ActiveYear < state.LatestRequestedYear)
				{
					state.ActiveYear = state.LatestRequestedYear;
				}

				XjAnnualPipelineStage observedStage = state.Stage;
				if (observedStage == XjAnnualPipelineStage.Prepare)
				{
					XjStageZeroObservation.RecordAnnualPrepare(actor, state.LastCompletedYear, state.ActiveYear, state.LatestRequestedYear);
				}
				XjRuntimeHotspot hotspot = observedStage == XjAnnualPipelineStage.Progression
					? XjRuntimeHotspot.AnnualProgression
					: XjRuntimeHotspot.AnnualPrepare;
				long observationStarted = XjStageZeroObservation.BeginAnnualStage();
				long sampleStarted = XjRuntimeDiagnostics.BeginSample(hotspot, 31);
				bool hasNext = XjSchedulerActorPipeline.ProcessStage(
					actor,
					state.ActiveYear,
					observedStage,
					JinDanCombatLane.EnqueueActor,
					out XjAnnualPipelineStage nextStage);
				XjRuntimeDiagnostics.EndSample(hotspot, sampleStarted);
				XjStageZeroObservation.EndAnnualStage(observedStage, observationStarted);
				bool stillRegistered = AnnualActorStates.TryGetValue(actorId, out AnnualActorState registeredState)
					&& ReferenceEquals(registeredState, state)
					&& !state.ReleaseRequested;
				if (!stillRegistered)
				{
					// Death/removal may synchronously unregister the actor while the stage runs.
					// Do not persist or requeue that detached state; it is returned below.
				}
				else if (hasNext)
				{
					state.Stage = nextStage;
					QueueAnnualState(actorId, state);
				}
				else
				{
					int completedYear = state.ActiveYear;
					state.LastCompletedYear = Math.Max(state.LastCompletedYear, completedYear);
					CompletePersistedAnnualState(actor, state.LastCompletedYear);
					EnsureAnnualSecondaryStateLoaded(actor, actorId, Math.Max(0, completedYear - 1));
					EnqueueAnnualSecondary(actor, actorId, completedYear);
					EnsureAnnualMaintenanceStateLoaded(actor, actorId, Math.Max(0, completedYear - 1));
					EnqueueAnnualMaintenance(actor, actorId, completedYear);
					unchecked { _actorProgressionRevision++; }
					XjStageZeroObservation.RecordAnnualCoreCompletion(actor, completedYear, migratedLegacyFinalize: false);
					if (state.LatestRequestedYear > state.LastCompletedYear)
					{
						state.ActiveYear = ResolveNextAnnualActiveYear(state.LastCompletedYear, state.LatestRequestedYear);
						state.Stage = XjAnnualPipelineStage.Prepare;
						QueueAnnualState(actorId, state);
					}
					else
					{
						RemoveAnnualActorState(actorId);
					}
				}
			}

			state.InFlight = false;
			if (state.ReleaseRequested)
			{
				state.ReleaseRequested = false;
				ReleaseAnnualActorState(state);
			}
			if (HasExceededTimeBudget(annualStarted, timeBudgetMs)) break;
		}
		TickAutoCollectRechecks(context);
	}

	private static void TickAnnualSecondary(in XjSchedulerContext context)
	{
		if (!XjDetectionGate.ShouldProcessAnnualActors(context) || AnnualSecondaryQueue.Count == 0) return;
		bool coreUnderPressure = AnnualActorQueue.Count >= Math.Max(128, XjCultivatorCache.Count / 8);
		int minimumActors = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => coreUnderPressure ? 4 : 12,
			XjRuntimeStressTier.Severe => coreUnderPressure ? 8 : 24,
			XjRuntimeStressTier.Mild => coreUnderPressure ? 20 : 48,
			_ => coreUnderPressure ? 40 : 96
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => coreUnderPressure ? 0.06d : 0.14d,
			XjRuntimeStressTier.Severe => coreUnderPressure ? 0.10d : 0.24d,
			XjRuntimeStressTier.Mild => coreUnderPressure ? 0.20d : 0.40d,
			_ => coreUnderPressure ? 0.35d : 0.70d
		};
		int baseBudget = coreUnderPressure ? AnnualSecondaryActorBudget / 2 : AnnualSecondaryActorBudget;
		double baseMilliseconds = coreUnderPressure ? AnnualSecondaryLaneTimeBudgetMs * 0.5d : AnnualSecondaryLaneTimeBudgetMs;
		int actorBudget = XjRuntimeWorkBudget.ScaleCount(baseBudget, minimumActors);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(baseMilliseconds, minimumMilliseconds);
		int processedActors = 0;
		long started = Stopwatch.GetTimestamp();
		while (AnnualSecondaryQueue.Count > 0 && processedActors < actorBudget)
		{
			long actorId = AnnualSecondaryQueue.Dequeue();
			if (!AnnualSecondaryStates.TryGetValue(actorId, out AnnualSecondaryState state)) continue;
			state.Queued = false;
			state.InFlight = true;
			processedActors++;
			if (!XjActorRegistry.Resolve(actorId, out Actor actor)
				|| (!XjCultivatorCache.IsCultivator(actorId) && !XjCultivatorCache.CheckAndUpdate(actor)))
			{
				RemoveAnnualSecondaryState(actorId);
			}
			else
			{
				int exactYear = Math.Max(state.LastCompletedYear + 1, state.ActiveYear);
				if (exactYear <= state.LatestRequestedYear)
				{
					XjSchedulerActorPipeline.ProcessSecondaryAnnual(actor, exactYear);
					bool stillRegistered = AnnualSecondaryStates.TryGetValue(actorId, out AnnualSecondaryState registeredState)
						&& ReferenceEquals(registeredState, state)
						&& !state.ReleaseRequested;
					if (stillRegistered)
					{
						state.LastCompletedYear = exactYear;
						CompleteAnnualSecondaryState(actor, exactYear);
						if (state.LatestRequestedYear > exactYear)
						{
							state.ActiveYear = exactYear + 1;
							QueueAnnualSecondaryState(actorId, state);
						}
						else
						{
							RemoveAnnualSecondaryState(actorId);
						}
					}
				}
				else
				{
					RemoveAnnualSecondaryState(actorId);
				}
			}
			state.InFlight = false;
			if (state.ReleaseRequested)
			{
				state.ReleaseRequested = false;
				ReleaseAnnualSecondaryState(state);
			}
			if (HasExceededTimeBudget(started, timeBudgetMs)) break;
		}
	}

	private static void TickAnnualMaintenance(in XjSchedulerContext context)
	{
		if (!XjDetectionGate.ShouldProcessAnnualActors(context) || AnnualMaintenanceQueue.Count == 0) return;
		bool coreUnderPressure = AnnualActorQueue.Count >= Math.Max(128, XjCultivatorCache.Count / 8);
		int minimumStages = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => coreUnderPressure ? 4 : 8,
			XjRuntimeStressTier.Severe => coreUnderPressure ? 8 : 16,
			XjRuntimeStressTier.Mild => coreUnderPressure ? 16 : 28,
			_ => coreUnderPressure ? 24 : 48
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => coreUnderPressure ? 0.04d : 0.08d,
			XjRuntimeStressTier.Severe => coreUnderPressure ? 0.08d : 0.14d,
			XjRuntimeStressTier.Mild => coreUnderPressure ? 0.14d : 0.24d,
			_ => coreUnderPressure ? 0.22d : 0.45d
		};
		int baseBudget = coreUnderPressure ? AnnualMaintenanceStageBudget / 2 : AnnualMaintenanceStageBudget;
		double baseMilliseconds = coreUnderPressure ? AnnualMaintenanceLaneTimeBudgetMs * 0.5d : AnnualMaintenanceLaneTimeBudgetMs;
		int stageBudget = XjRuntimeWorkBudget.ScaleCount(baseBudget, minimumStages);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(baseMilliseconds, minimumMilliseconds);
		int processedStages = 0;
		long started = Stopwatch.GetTimestamp();
		while (AnnualMaintenanceQueue.Count > 0 && processedStages < stageBudget)
		{
			long actorId = AnnualMaintenanceQueue.Dequeue();
			if (!AnnualMaintenanceStates.TryGetValue(actorId, out AnnualMaintenanceState state)) continue;
			state.Queued = false;
			state.InFlight = true;
			processedStages++;
			if (!XjActorRegistry.Resolve(actorId, out Actor actor)
				|| (!XjCultivatorCache.IsCultivator(actorId) && !XjCultivatorCache.CheckAndUpdate(actor)))
			{
				RemoveAnnualMaintenanceState(actorId);
			}
			else
			{
				XjRuntimeHotspot hotspot = state.Stage switch
				{
					XjAnnualMaintenanceStage.Ancillary => XjRuntimeHotspot.AnnualMaintenanceAncillary,
					XjAnnualMaintenanceStage.Assets => XjRuntimeHotspot.AnnualMaintenanceAssets,
					_ => XjRuntimeHotspot.AnnualMaintenanceIdentity
				};
				long observationStarted = XjStageZeroObservation.BeginAnnualStage();
				long sampleStarted = XjRuntimeDiagnostics.BeginSample(hotspot, 31);
				XjAnnualMaintenanceStage observedStage = state.Stage;
				bool hasNext = XjSchedulerActorPipeline.ProcessMaintenanceStage(
					actor,
					state.FromYearInclusive,
					state.ActiveYear,
					observedStage,
					out XjAnnualMaintenanceStage nextStage);
				XjRuntimeDiagnostics.EndSample(hotspot, sampleStarted);
				XjStageZeroObservation.EndAnnualMaintenanceStage(observedStage, observationStarted);
				bool stillRegistered = AnnualMaintenanceStates.TryGetValue(actorId, out AnnualMaintenanceState registeredState)
					&& ReferenceEquals(registeredState, state)
					&& !state.ReleaseRequested;
				if (!stillRegistered)
				{
					// Actor removal during maintenance detaches the state immediately.
				}
				else if (hasNext)
				{
					state.Stage = nextStage;
					QueueAnnualMaintenanceState(actorId, state);
				}
				else
				{
					state.LastCompletedYear = Math.Max(state.LastCompletedYear, state.ActiveYear);
					CompleteAnnualMaintenanceState(actor, state.LastCompletedYear);
					XjStageZeroObservation.RecordAnnualMaintenanceCompletion(actor, state.LastCompletedYear);
					if (state.LatestRequestedYear > state.LastCompletedYear)
					{
						state.FromYearInclusive = state.LastCompletedYear + 1;
						state.ActiveYear = state.LatestRequestedYear;
						state.Stage = XjAnnualMaintenanceStage.Identity;
						QueueAnnualMaintenanceState(actorId, state);
					}
					else
					{
						RemoveAnnualMaintenanceState(actorId);
					}
				}
			}
			state.InFlight = false;
			if (state.ReleaseRequested)
			{
				state.ReleaseRequested = false;
				ReleaseAnnualMaintenanceState(state);
			}
			if (HasExceededTimeBudget(started, timeBudgetMs)) break;
		}
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
