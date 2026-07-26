using HarmonyLib;
using ai;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "setCity", new System.Type[] { typeof(City) })]
	public static bool XuanJianVNext_Actor_SetCity_HighRealmSectResidencePrefix(
		Actor __instance,
		[HarmonyArgument(0)] City pCity)
	{
		return XjSectHighRealmResidenceSystem.AllowNativeCityAssignment(__instance, pCity);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "goTo")]
	public static bool XuanJianVNext_Actor_GoTo_HighRealmMovement_Prefix(
		Actor __instance,
		WorldTile pTile,
		ref ExecuteEvent __result)
	{
		long actorId = __instance?.data == null ? 0L : ((BaseSystemData)__instance.data).id;
		if (actorId > 0L
			&& !XjWorldBootstrapLane.HasPending
			&& !XjRuntimeActorInterestIndex.HasMovementInterest(actorId))
		{
			return true;
		}

		return !XjHighRealmMovement.TryHandleGoTo(__instance, pTile, ref __result);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "updateFall")]
	public static bool XuanJianVNext_Actor_UpdateFall_HighRealmMovement_Prefix(Actor __instance, out long __state)
	{
		__state = 0L;
		long actorId = __instance?.data == null ? 0L : ((BaseSystemData)__instance.data).id;
		if (actorId > 0L
			&& !XjWorldBootstrapLane.HasPending
			&& !XjRuntimeActorInterestIndex.HasMovementInterest(actorId))
		{
			return true;
		}

		__state = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.UpdateFall, 511);
		return !XjHighRealmMovement.ShouldSkipFall(__instance);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "updateFall")]
	public static void XuanJianVNext_Actor_UpdateFall_HighRealmMovement_Postfix(long __state)
	{
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.UpdateFall, __state);
	}
}
