using XuanJianVNext.Architecture;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Systems.YaoShu;

namespace XuanJianVNext.Systems.Events;

/// <summary>
/// 世界级事件的加载后投递边界。开局公告必须等世界 UI 与玄鉴归档都稳定后再发，
/// 但不能依赖年度队列是否恰好在第十年前获得执行机会。
/// </summary>
internal static class XjWorldEventRuntimeComposition
{
	private static bool _initialized;

	internal static void Initialize()
	{
		if (_initialized) return;
		_initialized = true;
		XjFeatureRegistration.WorldLifecycle(
			"world.events",
			5,
			currentYear =>
			{
				XjWorldEventCatalog.ScheduleAfterLoad(currentYear);
				XjHongXiaLuoXiaEvent.ReconcileAfterLoad(currentYear);
				XjYaoShuGreatSageSystem.ReconcileAfterLoad(currentYear);
			},
			XjWorldEventCatalog.ClearRuntime);
		XjFeatureRegistration.ArchiveDocument(
			XjHongXiaLuoXiaEvent.ModuleId,
			57,
			1,
			XjHongXiaLuoXiaEvent.ExportPayload,
			XjHongXiaLuoXiaEvent.ImportPayload);
		XjFeatureRegistration.ArchiveDocument(
			XjYaoShuGreatSageSystem.ModuleId,
			67,
			1,
			XjYaoShuGreatSageSystem.ExportPayload,
			XjYaoShuGreatSageSystem.ImportPayload);
		XjFeatureRegistration.BackgroundLane(
			"background.opening-events",
			4,
			() => XjWorldEventCatalog.HasOpeningPending,
			XjWorldEventCatalog.TickOpening,
			new XjRuntimeLanePolicy(
				1,
				0.20d,
				XjRuntimeBacklogPolicy.CoalesceLatest,
				XjRuntimeYearSemantics.CurrentState));
	}

	internal static void ClearRuntime()
	{
		XjWorldEventCatalog.ClearRuntime();
		XjHongXiaLuoXiaEvent.ClearRuntime();
		XjYaoShuGreatSageSystem.ClearRuntime();
	}
}
