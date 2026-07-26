using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(MapBox), "updateSimulation")]
	public static void XuanJianVNext_MapBox_UpdateSimulation_RuntimeCadence_Postfix(MapBox __instance, float pElapsed)
	{
		XjRuntimeCadence.Tick();
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(MapBox), "finishingUpLoading")]
	public static void XuanJianVNext_MapBox_FinishingUpLoading_RuntimeSettings_Postfix()
	{
		XjRuntimeSettings.LoadFromModConfig(XuanJianVNext.XuanJianMod.I?.GetConfig());
		XjWorldArchiveSystem.LoadReadOnly();
		XjYearTracker.Refresh();
		int currentYear = XjYearTracker.CurrentYear;
		XjRuntimeCadence.InitializeAfterLoad(currentYear);
		XjZongMenRuntimeLane.InitializeAfterLoad(currentYear);
		XjWorldBootstrapLane.ScheduleAfterLoad(currentYear);
		XjNativeArmySaveGuard.ScheduleAfterLoadInspection();
		XjThreeBookDiagnostics.AuditAfterLoad(currentYear);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(MapBox), "clearWorld")]
	public static void XuanJianVNext_MapBox_ClearWorld_ClearCache_Postfix()
	{
		XjNativeArmySaveGuard.ClearRuntime();
		XjCacheRegistry.ClearAll();
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(WorldAgeManager), "checkAge")]
	public static bool XuanJianVNext_WorldAgeManager_CheckAge_UnknownAge_Prefix(WorldAgeAsset pAsset, ref WorldAgeAsset __result)
	{
		if (!XjEraRuntime.TryReplaceUnknownAge(pAsset, out WorldAgeAsset replacementAge))
		{
			return true;
		}

		__result = replacementAge;
		return false;
	}
}
