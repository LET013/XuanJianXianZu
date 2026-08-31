using Newtonsoft.Json;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Architecture;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// Composition boundary for Shi runtime services. Shi-specific queues register
/// themselves here instead of adding another hard dependency to XjScheduler.
/// </summary>
internal static class XjShiRuntimeComposition
{
	private static bool _initialized;

	internal static void Initialize()
	{
		if (_initialized) return;
		_initialized = true;
		XjFeatureRegistration.WorldLifecycle(
			"world.shi",
			10,
			currentYear =>
			{
				XjZhantanlinSystem.OnWorldLoaded();
				XjShiOpeningPrologueSystem.ScheduleAfterLoad(currentYear);
				// 旧档中已经到期却因年度追赶饥饿而滞留的释修真灵，读档后立即
				// 重新进入后台归返队列，不必再等待下一次年度车道。这里只读待归返
				// 稀疏索引；旃檀林尚未显世时记录继续保留，待承载条件满足后再归返。
				XjReincarnation.SchedulePendingShiReturns(System.Math.Max(1, currentYear));
				XjZhantanlinSystem.SchedulePeriodicMoHeReturns(System.Math.Max(1, currentYear));
			},
			XjZhantanlinSystem.OnWorldCleared);
		XjFeatureRegistration.AnnualCultivationPath(
			"path.shi",
			10,
			XjCultivationPathRules.IsShi,
			XjShiState.PrepareActor,
			XjShiState.TickActor,
			XjShiPowerRules.GetCombatTier);
		// RC11.10：移除金丹/真君“主动跨国访道”的年度增频入口。
		// 已成高境者只可能在真实接触今释高修时，经统一修法重择策略极低频投释。

		XjFeatureRegistration.ArchiveDocument(
			"cultivation.ancient-shi-temples",
			42,
			2,
			() => JsonConvert.SerializeObject(XjAncientShiTempleSystem.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload))
				{
					XjAncientShiTempleSystem.Clear();
					return;
				}
				XjAncientShiTempleWorldArchiveData state = JsonConvert.DeserializeObject<XjAncientShiTempleWorldArchiveData>(payload);
				XjAncientShiTempleSystem.ImportState(state);
			});
		XjFeatureRegistration.BackgroundLane(
			"background.shi-sanctuary",
			4,
			() => XjZhantanlinSystem.HasRuntimeMaintenancePending,
			XjZhantanlinSystem.EnsureTerrainApplied,
			new XjRuntimeLanePolicy(
				1,
				0.85d,
				XjRuntimeBacklogPolicy.CoalesceLatest,
				XjRuntimeYearSemantics.CurrentState));
		XjFeatureRegistration.BackgroundLane(
			"background.shi-prologue",
			5,
			() => XjShiOpeningPrologueSystem.HasPending,
			XjShiOpeningPrologueSystem.Tick,
			new XjRuntimeLanePolicy(
				1,
				0.20d,
				XjRuntimeBacklogPolicy.CoalesceLatest,
				XjRuntimeYearSemantics.CurrentState));
		XjFeatureRegistration.BackgroundLane(
			"background.shi-return",
			10,
			() => XjReincarnation.HasQueuedShiReturns || XjZhantanlinSystem.HasQueuedPeriodicMoHeReturns,
			() =>
			{
				int year = System.Math.Max(1, XjYearTracker.CurrentYear);
				// BackgroundLane 的 tick 是 Action：消费一次任务即可，不向 void 委托返回值。
				// 真灵重塑优先；没有待重塑肉身时才消费一个摩诃归林对账。
				if (XjReincarnation.HasQueuedShiReturns)
				{
					XjReincarnation.TickPendingShiReturn(year);
				}
				else
				{
					XjZhantanlinSystem.TickQueuedPeriodicMoHeReturn(year);
				}
			},
			new XjRuntimeLanePolicy(
				1,
				1.20d,
				XjRuntimeBacklogPolicy.Exact,
				XjRuntimeYearSemantics.CurrentState));
		XjFeatureRegistration.BackgroundLane(
			"background.shi-duhua",
			15,
			() => XjShiDuhuaRuntimeLane.HasPending,
			XjShiDuhuaRuntimeLane.Tick,
			new XjRuntimeLanePolicy(
				1,
				0.90d,
				XjRuntimeBacklogPolicy.Exact,
				XjRuntimeYearSemantics.ExactYear));
	}

	internal static void ClearRuntime()
	{
		// Shi module owns all of its runtime caches; Core/CacheRegistry no longer
		// needs to know individual Shi subsystems.
		XjShiAnnouncementSystem.ClearRuntime();
		XjShiOpeningPrologueSystem.Clear();
		XjShiDuhuaRuntimeLane.Clear();
		XjShiEntrySystem.ClearRuntime();
		XjShiWorldHonoredPosturePolicy.ClearRuntime();
		XjShiDomainState.Clear();
		XjShiTitleSystem.ClearCache();
		XjShiFruitPositionLockSystem.Clear();
		XjShiWorldRegistry.Clear();
		XjAncientShiTempleSystem.Clear();
	}
}
