using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Core;

/// <summary>
/// Composition of critical-death and background runtime lanes. Kept separate
/// from the annual actor state machine so adding a bounded runtime lane cannot
/// grow the scheduler's progression core.
/// </summary>
internal static partial class XjScheduler
{
	private static bool _runtimeLanesRegistered;

	internal static void EnsureRuntimeLaneRegistration()
	{
		if (_runtimeLanesRegistered) return;
		_runtimeLanesRegistered = true;
		RegisterRuntimeLanes();
	}

	private static void RegisterRuntimeLanes()
	{
		XjDeathLaneRegistry.Register(new XjDeathLaneDescriptor(
			"death.zifu-failure", 10,
			() => XjZiFuFailureDeathLane.HasPending,
			(budget, resolver) => XjZiFuFailureDeathLane.Tick(budget, resolver)));
		XjDeathLaneRegistry.Register(new XjDeathLaneDescriptor(
			"death.rendan", 20,
			() => XjRenDanDeathLane.HasPending,
			(budget, resolver) => XjRenDanDeathLane.Tick(budget, resolver)));
		XjDeathLaneRegistry.Register(new XjDeathLaneDescriptor(
			"death.qiyu-dongtian", 30,
			() => XjQiYuDongTianDeathLane.HasPending,
			(budget, resolver) => XjQiYuDongTianDeathLane.Tick(budget, resolver)));
		XjDeathLaneRegistry.Register(new XjDeathLaneDescriptor(
			"death.shendan", 40,
			() => XjShenDanDeathLane.HasPending,
			(budget, resolver) => XjShenDanDeathLane.Tick(budget, resolver)));

		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.world-lookup", 5,
			() => XjWorldLookupIndex.HasPendingMaintenance,
			TickWorldLookupBackground,
			new XjRuntimeLanePolicy(96, 0.35d, XjRuntimeBacklogPolicy.CoalesceLatest, XjRuntimeYearSemantics.CurrentState)));
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.longshu", 10,
			() => _longShuYearMaintenancePending
				|| _pendingLongShuGrowthPasses > 0
				|| XjLongShuSystem.HasPendingSpawnWork,
			TickLongShuBackground,
			new XjRuntimeLanePolicy(1, 0.80d, XjRuntimeBacklogPolicy.CoalesceLatest, XjRuntimeYearSemantics.CurrentState)));
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.dongtian", 20,
			() => !XjWorldBootstrapLane.HasPending && XjDongTianRegistry.NeedsRuntimeTick,
			TickDongTianBackground,
			new XjRuntimeLanePolicy(DongTianRuntimeBudget, 0.75d, XjRuntimeBacklogPolicy.Exact, XjRuntimeYearSemantics.None)));
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.caiqi-city", 30,
			() => XjCaiQiCityMaintenanceLane.HasPending,
			TickCaiQiCityBackground,
			new XjRuntimeLanePolicy(CaiQiCityMaintenanceBudget, 0.50d, XjRuntimeBacklogPolicy.CoalesceLatest, XjRuntimeYearSemantics.LatestPeriod)));
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.caiqi-site", 40,
			() => XjCaiQiSiteRegistry.HasPendingRegistrations,
			TickCaiQiSiteBackground,
			new XjRuntimeLanePolicy(CaiQiSiteRegistrationBudget, 0.50d, XjRuntimeBacklogPolicy.Exact, XjRuntimeYearSemantics.None)));
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.broadcast", 50,
			() => XjBroadcastSystem.HasPendingAnnouncements,
			TickBroadcastBackground,
			new XjRuntimeLanePolicy(1, 0.50d, XjRuntimeBacklogPolicy.BoundedBestEffort, XjRuntimeYearSemantics.None)));
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.yinsi", 70,
			() => XjYinSiTraitLifecycle.HasPendingRuntimeWork
				|| XjTrueDamageSystem.HasPendingDirectSuppression
				|| XjYinSiExposurePursuitSystem.HasPendingRuntimePursuit,
			TickYinSiBackground,
			new XjRuntimeLanePolicy(1, 0.50d, XjRuntimeBacklogPolicy.Exact, XjRuntimeYearSemantics.None)));
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.codex", 80,
			() => !XjWorldBootstrapLane.HasPending && XjCodexSnapshotPublisher.HasPending,
			TickCodexBackground,
			new XjRuntimeLanePolicy(1, 0.75d, XjRuntimeBacklogPolicy.CoalesceLatest, XjRuntimeYearSemantics.CurrentState)));
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.high-realm-army", 90,
			() => XjHighRealmArmyExclusion.HasPending,
			TickHighRealmArmyBackground,
			new XjRuntimeLanePolicy(64, 0.50d, XjRuntimeBacklogPolicy.CoalesceLatest, XjRuntimeYearSemantics.CurrentState)));
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(
			"background.invariant-reconcile", 95,
			() => XjRuntimeInvariantLane.HasPending,
			XjRuntimeInvariantLane.Tick,
			new XjRuntimeLanePolicy(64, 0.45d, XjRuntimeBacklogPolicy.CoalesceLatest, XjRuntimeYearSemantics.CurrentState)));
	}

	private static void TickWorldLookupBackground(XjCooperativeBudget budget)
	{
		int processed = XjWorldLookupIndex.TickMaintenance(budget);
		if (processed > 0)
		{
			XjPerformanceTelemetry.ObserveQueue("worldLookupPending", XjWorldLookupIndex.PendingMaintenanceCount);
		}
	}

	private static void TickLongShuBackground()
	{
		int growthPasses = _pendingLongShuGrowthPasses;
		bool yearMaintenance = _longShuYearMaintenancePending;
		_pendingLongShuGrowthPasses = 0;
		_longShuYearMaintenancePending = false;
		long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundLongShu, 0);
		XjLongShuSystem.TickRuntime(yearMaintenance, growthPasses);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundLongShu, sample);
	}

	private static void TickDongTianBackground(XjCooperativeBudget budget)
	{
		int worldYear = World.world?.map_stats?.year ?? 0;
		if (!XjDetectionGate.ShouldProcessDongTianRuntime(XjDongTianRegistry.HasActiveRuntimeYear, worldYear))
		{
			return;
		}
		long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundDongTian, 0);
		XjDongTianRegistry.TickRuntime(budget);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundDongTian, sample);
	}

	private static void TickCaiQiCityBackground(XjCooperativeBudget budget)
	{
		long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundCaiQiCity, 0);
		// 0.9.9.6 semantic freeze: a city maintenance item is processed by fixed count,
		// never split into a partial deterministic site set by the cooperative deadline.
		XjCaiQiCityMaintenanceLane.Tick(XjRuntimeWorkBudget.ScaleCount(CaiQiCityMaintenanceBudget, 1));
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundCaiQiCity, sample);
	}

	private static void TickCaiQiSiteBackground(XjCooperativeBudget budget)
	{
		long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundCaiQiSite, 0);
		// 0.9.9.6 semantic freeze: registration drains the same fixed item count as 996.
		XjCaiQiSiteRegistry.TickRegistrationLane(XjRuntimeWorkBudget.ScaleCount(CaiQiSiteRegistrationBudget, 2));
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundCaiQiSite, sample);
	}

	private static void TickBroadcastBackground()
	{
		long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundBroadcast, 0);
		XjBroadcastSystem.TickCriticalAnnouncements();
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundBroadcast, sample);
	}

	private static void TickYinSiBackground()
	{
		long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundYinSi, 0);

		// 阴司终局共享 1 个重操作预算：一帧最多执行一次代码杀/活金丹追索。
		// 其余只做轻量运行态维护，避免同帧死亡快照与历史归档尖峰。
		int used = XjTrueDamageSystem.TickPendingDirectSuppressions(1);
		if (used == 0)
		{
			XjYinSiExposurePursuitSystem.TickPendingDirectPursuits(1);
		}
		XjYinSiTraitLifecycle.TickPendingSpawns();
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundYinSi, sample);
	}

	private static void TickCodexBackground()
	{
		long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundCodexSnapshot, 0);
		XjCodexSnapshotPublisher.Tick();
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundCodexSnapshot, sample);
	}

	private static void TickHighRealmArmyBackground(XjCooperativeBudget budget)
	{
		XjHighRealmArmyExclusion.TickQueue(budget);
	}
}
