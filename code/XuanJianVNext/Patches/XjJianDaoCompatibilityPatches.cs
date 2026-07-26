using System;
using HarmonyLib;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Patches;

[HarmonyPatch]
internal static class XjJianDaoCompatibilityPatches
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
	private static bool Actor_addTrait_JianDaoCompatibility_Prefix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId)
	{
		return !XjJianDaoCompatibility.ShouldBlockTraitGrant(__instance, traitId);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
	private static bool Actor_addTraitAsset_JianDaoCompatibility_Prefix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait)
	{
		return !XjJianDaoCompatibility.ShouldBlockTraitGrant(__instance, trait?.id);
	}
}
