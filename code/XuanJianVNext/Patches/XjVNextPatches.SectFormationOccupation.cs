using System;
using HarmonyLib;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(City), nameof(City.addCapturePoints), new Type[] { typeof(Kingdom), typeof(int) })]
	public static void XuanJianVNext_City_AddCapturePoints_SectFormationPrefix(City __instance, [HarmonyArgument(0)] Kingdom attackingKingdom, [HarmonyArgument(1)] ref int nativeDelta)
	{
		XjSectFormationOccupationAdapter.BeginNativeCapture(__instance, attackingKingdom, ref nativeDelta);
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(City), nameof(City.addCapturePoints), new Type[] { typeof(Kingdom), typeof(int) })]
	public static void XuanJianVNext_City_AddCapturePoints_SectFormationPostfix(City __instance) => XjSectFormationOccupationAdapter.EndNativeCapture(__instance);

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(City), nameof(City.addCapturePoints), new Type[] { typeof(Kingdom), typeof(int) })]
	public static Exception XuanJianVNext_City_AddCapturePoints_SectFormationFinalizer(City __instance, Exception __exception)
	{
		XjSectFormationOccupationAdapter.EndNativeCapture(__instance);
		return __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(City), "setKingdom", new Type[] { typeof(Kingdom), typeof(bool) })]
	public static bool XuanJianVNext_City_SetKingdom2_SectFormationGate(City __instance, [HarmonyArgument(0)] Kingdom targetKingdom) => XjSectFormationOccupationAdapter.AllowKingdomTransfer(__instance, targetKingdom);

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(City), "makeOwnKingdom", new Type[] { typeof(Actor), typeof(bool), typeof(bool) })]
	public static bool XuanJianVNext_City_MakeOwnKingdom_SectRebellionGate(
		City __instance,
		[HarmonyArgument(0)] Actor founder,
		ref Kingdom __result)
	{
		if (XjNationSectRebellionGuard.AllowNativeKingdomFounding(__instance, founder))
		{
			return true;
		}

		__result = __instance?.kingdom;
		XjNationSectRebellionGuard.MarkSuppressedNativeFounding(__instance, founder);
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(KingdomManager), "makeNewCivKingdom", new Type[] { typeof(Actor), typeof(string), typeof(bool) })]
	public static bool XuanJianVNext_KingdomManager_MakeNewCivKingdom_SectRebellionGate(
		[HarmonyArgument(0)] Actor founder,
		ref Kingdom __result)
	{
		if (XjNationSectRebellionGuard.IsControlledFounding
			|| XjNationSectRebellionGuard.AllowNativeKingdomFounding(founder?.city, founder))
		{
			return true;
		}

		__result = founder?.kingdom;
		XjNationSectRebellionGuard.MarkSuppressedNativeFounding(founder?.city, founder);
		return false;
	}

}
