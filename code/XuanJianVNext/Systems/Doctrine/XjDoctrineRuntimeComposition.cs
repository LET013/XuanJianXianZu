using Newtonsoft.Json;
using XuanJianVNext.Architecture;
using XuanJianVNext.Data.Doctrine;

namespace XuanJianVNext.Systems.Doctrine;

internal static class XjDoctrineRuntimeComposition
{
	private static bool _initialized;

	internal static void Initialize()
	{
		if (_initialized) return;
		_initialized = true;

		XjFeatureRegistration.AnnualActorExtension(
			"annual.doctrine-hostility",
			70,
			context => XjDoctrineConflictSystem.HasAnnualInterest(context),
			context => XjDoctrineConflictSystem.HasAnnualInterest(context),
			context => XjDoctrineConflictSystem.TickAnnual(context));

		XjFeatureRegistration.ArchiveDocument(
			"world.doctrine-conflict",
			55,
			1,
			() => JsonConvert.SerializeObject(XjDoctrineConflictSystem.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload))
				{
					XjDoctrineConflictSystem.ClearRuntime();
					return;
				}
				XjDoctrineConflictArchiveData state = JsonConvert.DeserializeObject<XjDoctrineConflictArchiveData>(payload);
				XjDoctrineConflictSystem.ImportState(state);
			});
	}

	internal static void ClearRuntime()
	{
		XjDoctrineConflictSystem.ClearRuntime();
	}
}
