using HarmonyLib;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Patches;

/// <summary>
/// 高境修士生育采用硬上限，避免概率门禁在高寿元下被大量年度尝试穿透。
/// </summary>
[HarmonyPatch]
internal static class XjHighRealmBirthPatches
{
	/// <summary>
	/// 统一的高境生育门禁。高层 Native AI/Status 入口和底层 makeBaby 必须复用
	/// 同一判断，避免底层返回 null 后，0.51.2 的原生高层方法继续解引用 child。
	/// </summary>
	internal static bool ShouldBlockBirth(Actor parent)
	{
		if (parent?.data == null) return false;
		return XjZhantanlinSystem.IsModernShiInside(parent)
			|| XjDaoTaiSpellScale.IsDaoTaiActor(parent)
			|| XjHighRealmDescendantRules.HasReachedBirthCap(parent);
	}

	internal static bool ShouldBlockBirth(Actor parent1, Actor parent2)
	{
		return ShouldBlockBirth(parent1) || ShouldBlockBirth(parent2);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(BabyMaker), "makeBaby")]
	private static bool BabyMaker_makeBaby_HighRealmCap_Prefix(Actor pParent1, Actor pParent2, ref Actor __result)
	{
		if (!ShouldBlockBirth(pParent1, pParent2)) return true;
		__result = null;
		return false;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(BabyMaker), "makeBaby")]
	private static void BabyMaker_makeBaby_HighRealmDescendant_Postfix(Actor pParent1, Actor pParent2, Actor __result)
	{
		if (__result?.data == null) return;
		XjHighRealmDescendantRules.RefreshFromParents(__result);
		XjSongXuanEasterEggSystem.ObserveNativeBirth(__result, pParent1, pParent2);
		XjZhangYanEasterEggSystem.ObserveNativeBirth(__result, pParent1, pParent2);
	}
}
