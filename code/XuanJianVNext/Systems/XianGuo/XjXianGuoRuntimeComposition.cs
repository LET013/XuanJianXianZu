using Newtonsoft.Json;
using XuanJianVNext.Architecture;
using XuanJianVNext.Architecture.Runtime;

namespace XuanJianVNext.Systems.XianGuo;

internal static class XjXianGuoRuntimeComposition
{
	private static bool _initialized;

	internal static void Initialize()
	{
		if (_initialized) return;
		_initialized = true;
		XjFeatureRegistration.AnnualActorExtension(
			"annual.xianguo.imperial-mingyang",
			65,
			context => XjXianGuoSystem.HasAnnualInterest(context.Actor),
			context => XjXianGuoSystem.HasAnnualInterest(context.Actor),
			context => XjXianGuoSystem.TickAnnualActor(context.Actor, context.AnnualYear));

		XjFeatureRegistration.BackgroundLane(
			"background.xianguo.native-sovereign-projection",
			55,
			() => XjXianGuoSystem.HasPendingNativeProjectionRepairs,
			XjXianGuoSystem.TickNativeProjectionRepair,
			new XjRuntimeLanePolicy(
				1, 0.60d,
				XjRuntimeBacklogPolicy.BoundedBestEffort,
				XjRuntimeYearSemantics.CurrentState));

		XjFeatureRegistration.ArchiveDocument(
			"world.xianguo",
			58,
			3,
			() => JsonConvert.SerializeObject(XjXianGuoSystem.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload))
				{
					XjXianGuoSystem.ClearRuntime();
					return;
				}
				XjXianGuoArchiveData state = JsonConvert.DeserializeObject<XjXianGuoArchiveData>(payload);
				XjXianGuoSystem.ImportState(state);
			});
	}

	internal static void ClearRuntime()
	{
		XjXianGuoSystem.ClearRuntime();
	}
}
