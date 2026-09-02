using HarmonyLib;
using ai.behaviours;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.YaoShu;

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

	// 原生 AI 会在 checkForBabies 中先组建 Family，之后才查询可否怀胎。
	// 必须在行为入口截断，才能让已满额的高境夫妇完全不进入生育事务。
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BehCheckForBabiesFromSexualReproduction), "execute")]
	private static bool SexualReproduction_HighRealmCap_Prefix(Actor pActor, ref BehResult __result)
	{
		if (!ShouldBlockBirth(pActor, pActor?.lover)) return true;
		__result = BehResult.Stop;
		return false;
	}

	// 覆盖无性、孕育与魂生等同样依赖 BabyHelper 的原生 AI 入口。
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BabyHelper), "canMakeBabies")]
	private static bool BabyHelper_CanMakeBabies_HighRealmCap_Prefix(Actor pActor, ref bool __result)
	{
		if (!ShouldBlockBirth(pActor)) return true;
		__result = false;
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BabyHelper), "isMetaLimitsReached")]
	private static bool BabyHelper_IsMetaLimitsReached_HighRealmCap_Prefix(Actor pActor, ref bool __result)
	{
		if (!ShouldBlockBirth(pActor)) return true;
		__result = true;
		return false;
	}

	// 怀胎时尚未满额、分娩前被其他子嗣占满名额的情形，必须在状态完成回调
	// 之前视作一次正常的“无产结束”，不能再让回调下探至 BabyMaker.makeBaby。
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(StatusLibrary), "actionPregnancyFinish")]
	private static bool StatusLibrary_PregnancyFinish_HighRealmCap_Prefix(BaseSimObject pTarget, ref bool __result)
	{
		if (!ShouldBlockBirth(pTarget?.a, pTarget?.a?.lover)) return true;
		__result = true;
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(StatusLibrary), "actionPregnancyParthenogenesisFinish")]
	private static bool StatusLibrary_ParthenogenesisFinish_HighRealmCap_Prefix(BaseSimObject pTarget, ref bool __result)
	{
		if (!ShouldBlockBirth(pTarget?.a)) return true;
		__result = true;
		return false;
	}

	// 两个公开出生包装入口也要在调用 makeBaby 前停止，防止外部模组或原生
	// 即时产仔路径越过 AI 行为入口。
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BabyMaker), "makeBabiesViaSexual")]
	private static bool BabyMaker_MakeBabiesViaSexual_HighRealmCap_Prefix(Actor pMotherTarget, Actor pParentA, Actor pParentB)
	{
		return !ShouldBlockBirth(pMotherTarget, pParentA) && !ShouldBlockBirth(pParentB);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BabyMaker), "makeBabyFromPregnancy")]
	private static bool BabyMaker_MakeBabyFromPregnancy_HighRealmCap_Prefix(Actor pActor)
	{
		return !ShouldBlockBirth(pActor, pActor?.lover);
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
		XjYaoShuSapientSpecies.ObserveNativeBirth(__result, pParent1, pParent2);
	}
}
