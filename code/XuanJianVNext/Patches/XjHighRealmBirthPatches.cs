using HarmonyLib;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.Patches;

/// <summary>
/// 高境修士生育采用硬上限，避免概率门禁在高寿元下被大量年度尝试穿透。
/// </summary>
[HarmonyPatch]
internal static class XjHighRealmBirthPatches
{
	[HarmonyPrefix]
	[HarmonyPatch(typeof(BabyMaker), "makeBaby")]
	private static bool BabyMaker_makeBaby_HighRealmCap_Prefix(Actor pParent1, Actor pParent2, ref Actor __result)
	{
		if (XjHighRealmDescendantRules.HasReachedBirthCap(pParent1)
			|| XjHighRealmDescendantRules.HasReachedBirthCap(pParent2))
		{
			__result = null;
			return false;
		}
		return true;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(BabyMaker), "makeBaby")]
	private static void BabyMaker_makeBaby_HighRealmDescendant_Postfix(Actor pParent1, Actor pParent2, Actor __result)
	{
		if (__result?.data == null) return;
		XjHighRealmDescendantRules.RefreshFromParents(__result);
	}
}
