using System;
using System.Collections.Generic;
using System.Diagnostics;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Architecture.Bootstrap;
using XuanJianVNext.Architecture.Runtime;
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
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Visual;

using XuanJianVNext.Systems.Sect;
namespace XuanJianVNext.Core;

internal static partial class XjScheduler
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
	private const int CleanupInspectBudget = 64;
	private const int BootstrapBudget = 96;
	private const double BootstrapTimeBudgetMs = 0.9;
	private const int SeedFastBudget = 20;
	private const int SeedGrowthBudget = 48;
	private const int SeedYearBudget = 80;
	private const double SeedLaneTimeBudgetMs = 0.65;
	// Native Actor.updateAge runs inside the single WorldBox year-transition frame.
	// Its postfix may only append an O(1) request; all actor custom-data reads,
	// legacy cursor migration and maintenance-interest detection are drained later.
	private const int AnnualIngressActorBudget = 256;
	private const double AnnualIngressLaneTimeBudgetMs = 0.35;
	private const int AnnualCoreStageBudget = 192;
	private const double AnnualCoreLaneTimeBudgetMs = 0.95;
	private const int AnnualSecondaryActorBudget = 192;
	private const double AnnualSecondaryLaneTimeBudgetMs = 0.70;
	private const int AnnualMaintenanceStageBudget = 288;
	private const double AnnualMaintenanceLaneTimeBudgetMs = 0.70;
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
	private const int AnnualWorldStageBudget = 12;
	private const double AnnualWorldTimeBudgetMs = 0.75;
	private const int UnifiedDetectionActorBudget = 384;
	private const int UnifiedDetectionKingdomBudget = 8;
	private const double UnifiedDetectionTimeBudgetMs = 0.45;

	private static int _tickCounter;
	private static int _pendingLongShuGrowthPasses;
	private static bool _longShuYearMaintenancePending;
	private static int _lastCriticalDeathUnityFrame = -1;
	private static int _semanticAuxCursor;
	private static readonly Queue<long> AnnualIngressQueue = new Queue<long>();
	private static readonly Dictionary<long, int> AnnualIngressYears = new Dictionary<long, int>();
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

	static XjScheduler()
	{
		EnsureRuntimeLaneRegistration();
	}

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
		|| AnnualIngressQueue.Count > 0
		|| AnnualActorQueue.Count > 0
		|| AnnualSecondaryQueue.Count > 0
		|| AnnualMaintenanceQueue.Count > 0
		|| AutoCollectRecheckQueue.Count > 0
		|| HasCriticalDeathWork
		|| HasBackgroundWork;

	internal static bool HasUrgentSimulationBacklog => HasCriticalDeathWork;

	internal static int RetainedAnnualStatePoolCount => AnnualActorStatePool.Count
		+ AnnualSecondaryStatePool.Count
		+ AnnualMaintenanceStatePool.Count;

	internal static bool HasAnnualActorBacklog => AnnualIngressQueue.Count > 0
		|| AnnualActorQueue.Count > 0
		|| AnnualSecondaryQueue.Count > 0
		|| AnnualMaintenanceQueue.Count > 0
		|| XjAnnualCultivatorSweepLane.HasPending;

	internal static bool HasAnnualRecoveryBacklog
	{
		get
		{
			int threshold = Math.Max(256, XjCultivatorCache.Count / 4);
			return XjAnnualWorldRuntimeLane.HasPending
				// Fixed-calendar / world-emergence work is semantic debt too. If it is
				// pending, run one fast scheduler pass per rendered frame until caught up;
				// otherwise x20 can advance world years faster than the normal 10-frame
				// background cadence can consume DongTian/LongShu work. Both checks are O(1).
				|| XjDongTianRegistry.NeedsRuntimeTick
				|| XjLongShuSystem.HasPendingSpawnWork
				|| XjAnnualCultivatorSweepLane.HasPending
				|| AnnualIngressQueue.Count >= threshold
				|| AnnualActorQueue.Count >= threshold
				|| AnnualSecondaryQueue.Count >= threshold
				|| AnnualMaintenanceQueue.Count >= threshold;
		}
	}

	// 仅供世界级年度快照失效使用；角色窗口必须读取 XjActorStateRevisionStore。

	private static bool HasCriticalDeathWork => XjDeathLaneRegistry.HasPending
		|| XjDeathArchiveQueue.HasPending
		|| XjJinDanResidualDeathLane.HasPending;

	private static bool HasBackgroundWork => XjAnnualWorldRuntimeLane.HasPending
		|| XjUnifiedDetectionRuntime.HasPending
		|| XjRuntimeMemoryPruner.HasRunnableWork
		|| XjBackgroundLaneRegistry.HasPending;

	internal static void RegisterActor(Actor actor)
	{
		if (XjTrueDamageSystem.EnforceIrreversibleYinSiClaim(actor)) return;
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

		// updateAge 是全体单位的年度热入口：只允许 O(1) 死籍检查。
		// saved_traits 迁移由 RegisterActor / bootstrap / prepareForSave 等冷路径承担。
		if (XjTrueDamageSystem.EnforceIrreversibleYinSiClaimFastRuntime(actor)) return;

		long knownActorId = ((BaseSystemData)actor.data).id;
		if (knownActorId > 0L && XjCultivatorCache.IsCultivator(knownActorId))
		{
			// 已入修炼缓存的角色不再重复走自然资质与剑意兼容判定。
			// 其可见特质或外部互斥状态改变时，由对应 trait 生命周期入口
			// 主动修正缓存；年度回调只做轻量入队。
			EnqueueAnnualIngress(actor, knownActorId);
			return;
		}

		int ageYear = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		bool isAgeFiveWindow = XjAptitudeRuleEvaluator.IsAgeFiveEligibilityWindow(ageYear);
		if (!isAgeFiveWindow
			&& knownActorId > 0L
			&& XjRuntimeActorInterestIndex.HasOrdinaryAnnualClassification(knownActorId))
		{
			return;
		}

		bool hasExplicitGrant = XjCultivationEligibility.HasExplicitCultivationGrant(actor);
		bool hasCultivationState = XjCultivationEligibility.HasCultivationMarkers(actor);
		if (!isAgeFiveWindow && !hasExplicitGrant && !hasCultivationState)
		{
			// Cache the negative annual classification. Trait/data write gateways clear it
			// immediately, so ordinary adults no longer rescan grant/realm markers every year.
			XjRuntimeActorInterestIndex.MarkOrdinaryAnnual(knownActorId);
			return;
		}

		if (TryRegisterEligibleActor(actor, out long actorId))
		{
			EnqueueAnnualIngress(actor, actorId);
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
		if (XjTrueDamageSystem.EnforceIrreversibleYinSiClaim(actor)) return;
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

		bool managedLongShu = XjCultivationEligibility.CanRunManagedLongShuCultivation(actor);
		if (!managedLongShu && !XjCultivationEligibility.CanCultivate(actor))
		{
			if (XjRuntimeSettings.CultivationEnabled
				&& XjCultivationEligibility.ShouldClearUnsupportedCultivationState(actor)
				&& (XjCultivationEligibility.HasCultivationMarkers(actor)
					|| XjCultivationEligibility.HasExplicitCultivationGrant(actor)))
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
		// 0.9.9.8 曾把先天命数从资质曲线拆离；复用现有的限额 bootstrap
		// 注册通道做一次性补绑，不新增任何世界人口扫描。
		XjAptitudeEffectRules.EnsureStoredAptitudeBaseCurve(actor);
		// Rebuild the tiny manual-XjZz6 holder index during normal/bootstrap registration.
		// This keeps the visible-trait grant hot path O(1) without adding a world scan.
		XjAptitudeTraitLifecycle.ObserveRuntimeActor(actor);

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

		XjSectCultivatorCityIndex.Observe(actor);
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
			|| (!XjCultivationEligibility.CanCultivate(actor)
				&& !XjCultivationEligibility.CanRunManagedLongShuCultivation(actor)))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		EnqueueAnnualIngress(actor, actorId);
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
			|| (!XjCultivationEligibility.CanCultivate(actor)
				&& !XjCultivationEligibility.CanRunManagedLongShuCultivation(actor)))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		EnqueueAnnualIngress(actor, actorId, requestedYear);
	}


	private static void EnqueueAnnualIngress(Actor actor, long actorId, int explicitRequestedYear = 0)
	{
		if (actor?.data == null || actorId <= 0L || !actor.isAlive())
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

		if (AnnualIngressYears.TryGetValue(actorId, out int existingYear))
		{
			if (requestedYear > existingYear)
			{
				AnnualIngressYears[actorId] = requestedYear;
			}
			return;
		}

		AnnualIngressYears[actorId] = requestedYear;
		AnnualIngressQueue.Enqueue(actorId);
	}

	private static void DrainAnnualIngressForSave()
	{
		while (AnnualIngressQueue.Count > 0)
		{
			long actorId = AnnualIngressQueue.Dequeue();
			if (!AnnualIngressYears.TryGetValue(actorId, out int requestedYear))
			{
				continue;
			}
			AnnualIngressYears.Remove(actorId);
			if (XjActorRegistry.Resolve(actorId, out Actor actor)
				&& actor?.data != null
				&& actor.isAlive())
			{
				EnqueueAnnualActorCore(actor, actorId, requestedYear);
			}
		}
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
				if (requestedYear <= persistedCompletedYear)
				{
					// Only a stale/duplicate callback needs to recover independently
					// persisted secondary lanes. The normal new-year path restores them
					// after core progression, under the scheduler wall-clock budget.
					EnsureAnnualSecondaryStateLoaded(actor, actorId, persistedCompletedYear);
					EnsureAnnualMaintenanceStateLoaded(actor, actorId, persistedCompletedYear);
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
		if (!XjSchedulerActorPipeline.HasSecondaryAnnualInterest(actor, requestedYear))
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
			int fromYear = Math.Max(lastCompletedYear + 1, qualifiedYear > 0 ? qualifiedYear : 1);
			if (!XjSchedulerActorPipeline.HasMaintenanceAnnualInterest(actor, fromYear, requestedYear))
			{
				// 没有任何统一检测槽到期时只推进兼容维护游标，不创建三阶段队列。
				CompleteAnnualMaintenanceState(actor, requestedYear);
				return;
			}
			state = RentAnnualMaintenanceState(
				fromYear,
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
		// Save is the only synchronous durability boundary. Materialize the cheap
		// ingress requests here so no annual request is lost across a save/load.
		DrainAnnualIngressForSave();
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

		// 先处理形渡阡源/幻身双向关系，再清 ActorRegistry；否则另一端会失去解析入口。
		XjXingDuQianPhantomSystem.HandleActorRemoved(actor);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}
		XjAutoCollectSystem.ClearFavoriteIfInvalid(actor);
		ForgetActorRuntimeState(actorId);
		XjActorRegistry.Unregister(actorId);
	}

	/// <summary>
	/// Clears only actor-id keyed transient state that can be reconstructed from the live target.
	/// This is also the safe pre-handoff path for technical actor replacement: it intentionally
	/// preserves logical ownership/task state such as Sect authority, ShenDan binding, formation,
	/// craft-domain and alchemy work until their dedicated migration/cleanup owner handles them.
	/// </summary>
	internal static void PrepareActorRuntimeForTechnicalReplacement(long actorId)
	{
		if (actorId <= 0L) return;

		AnnualIngressYears.Remove(actorId);
		FlushDeferredAnnualCompletion(actorId);
		FlushDeferredAnnualSecondaryCompletion(actorId);
		FlushDeferredAnnualMaintenanceCompletion(actorId);
		RemoveAnnualActorState(actorId);
		RemoveAnnualSecondaryState(actorId);
		RemoveAnnualMaintenanceState(actorId);
		AutoCollectRecheckSet.Remove(actorId);
		XjAptitudeSeedLane.Forget(actorId);
		XjAptitudeTraitLifecycle.ForgetRuntimeActor(actorId);
		XjPerformanceTelemetry.ForgetActor(actorId);
		XuanJianVNext.Systems.Rank.XjRankSortSystem.RemoveActor(actorId);
		XjYinSiTraitLifecycle.ForgetActorRuntime(actorId);
		XjTrueDamageSystem.ForgetActorRuntime(actorId);
		XjClosedCultivationGuard.ForgetActorRuntime(actorId);
		XjSectDongTianLifecycle.ForgetActorRuntime(actorId);
		XjCultivatorCache.Remove(actorId);
		XjFuQiMethodRules.ForgetRuntimeCache(actorId);
		XjFaBaoBonusService.Forget(actorId);
		XjCombatHotPathCache.Remove(actorId);
		XjCombatTracker.Remove(actorId);
		XuanJianVNext.Systems.WeaponArt.XjWeaponArtCombatTracker.Remove(actorId);
		XjHighRealmMovement.ForgetActorRuntime(actorId);
		XjLongShuSystem.ForgetInnateCombatRuntime(actorId);
		XjHighRealmAggregateStore.RemoveActor(actorId);
		XjFamilyMemberIndex.Shared.ForgetRuntimeActor(actorId);
		XjBloodlineRefreshLane.Forget(actorId);
		XjSectCultivatorCityIndex.Forget(actorId);
		XjCraftActorIndex.Forget(actorId);
	}

	/// <summary>
	/// Single owner for an actor identity leaving the live runtime. It clears rebuildable caches
	/// plus current actor-owned state, but deliberately excludes death rewards, reincarnation and
	/// history writes so bounded repair lanes never fabricate a gameplay death.
	/// </summary>
	internal static void ForgetActorRuntimeState(long actorId)
	{
		if (actorId <= 0L) return;

		PrepareActorRuntimeForTechnicalReplacement(actorId);
		int currentYear = Math.Max(0, XjYearTracker.CurrentYear);
		XjSectCommands.RemoveUnavailableMember(actorId, currentYear);
		XjShenDanRegistry.Forget(actorId);
		XjFormationEngineeringSystem.HandleActorRemoved(actorId);
		XjSecretRealmRegistry.OnActorUnavailable(actorId, currentYear);
		XjCraftDomainRegistry.ForgetUnavailableActor(actorId);
		XjAlchemyRuntimeRegistry.ForgetUnavailableActor(actorId, currentYear);
		XjQianKunDaiRegistry.RemoveInventory(actorId);
		XjDaoTaiPresenceArchive.ForgetUnavailableActor(actorId);
		XjJinDanImmortalityRegistry.MarkRuntimeUnavailable(actorId, currentYear);
	}

	internal static void ForgetUnavailableActor(long actorId)
	{
		if (actorId <= 0L) return;
		ForgetActorRuntimeState(actorId);
		XjActorRegistry.Unregister(actorId);
	}

}
