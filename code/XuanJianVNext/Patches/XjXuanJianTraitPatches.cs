using System;
using HarmonyLib;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Patches;

[HarmonyPatch]
internal static class XjXuanJianTraitPatches
{
	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
	private static bool Actor_removeTrait_JieLinZhang_Prefix(
		[HarmonyArgument("pTraitID")] string traitId)
	{
		return !XjXuanJianShenTongSpecials.IsJieLinZhangTrait(traitId);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(ActorTrait) })]
	private static bool Actor_removeTraitAsset_JieLinZhang_Prefix(
		[HarmonyArgument("pTrait")] ActorTrait trait,
		ref bool __result)
	{
		if (!XjXuanJianShenTongSpecials.IsJieLinZhangTrait(trait?.id))
		{
			return true;
		}

		__result = false;
		return false;
	}
}
