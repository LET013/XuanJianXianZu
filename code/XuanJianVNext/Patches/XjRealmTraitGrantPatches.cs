using System;
using HarmonyLib;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Patches;

/// <summary>
/// Harmony bridge for external/manual realm and DaoTu trait edits.
/// Business reconciliation belongs to Systems.Cultivation.
/// </summary>
[HarmonyPatch]
internal static class XjRealmTraitGrantPatches
{
	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
	private static void Actor_addTrait_ManualRealm_Prefix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		out string __state)
	{
		__state = XjManualRealmTraitReconciliation.CaptureBeforeGrant(__instance, traitId);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
	private static void Actor_addTrait_ManualRealm_Postfix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		bool __result,
		string __state)
	{
		XjManualRealmTraitReconciliation.HandleGranted(__instance, traitId, __result, __state);
		XjCraftTraitRules.HandleTraitGranted(__instance, traitId, __result);
		if (__result || (__instance?.data != null && !string.IsNullOrWhiteSpace(traitId) && __instance.hasTrait(traitId)))
		{
			XjRuntimeActorInterestIndex.InvalidateCombatClassification(__instance);
			XjCultivatorCandidateIndex.MarkProgressionDirty(__instance);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
	private static void Actor_addTraitAsset_ManualRealm_Prefix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait,
		out string __state)
	{
		__state = XjManualRealmTraitReconciliation.CaptureBeforeGrant(__instance, trait?.id);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
	private static void Actor_addTraitAsset_ManualRealm_Postfix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait,
		bool __result,
		string __state)
	{
		XjManualRealmTraitReconciliation.HandleGranted(__instance, trait?.id, __result, __state);
		XjCraftTraitRules.HandleTraitGranted(__instance, trait?.id, __result);
		if (__result || (__instance?.data != null && !string.IsNullOrWhiteSpace(trait?.id) && __instance.hasTrait(trait.id)))
		{
			XjRuntimeActorInterestIndex.InvalidateCombatClassification(__instance);
			XjCultivatorCandidateIndex.MarkProgressionDirty(__instance);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
	private static void Actor_removeTrait_ManualRealm_Prefix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		out bool __state)
	{
		__state = __instance?.data != null
			&& !string.IsNullOrWhiteSpace(traitId)
			&& __instance.hasTrait(traitId);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
	private static void Actor_removeTrait_ManualRealm_Postfix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		bool __state)
	{
		bool removed = __state
			&& __instance?.data != null
			&& !__instance.hasTrait(traitId);
		XjManualRealmTraitReconciliation.HandleRemoved(__instance, traitId, removed);
		if (removed)
		{
			XjRuntimeActorInterestIndex.InvalidateCombatClassification(__instance);
			XjCultivatorCandidateIndex.MarkProgressionDirty(__instance);
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(ActorTrait) })]
	private static void Actor_removeTraitAsset_ManualRealm_Postfix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait,
		bool __result)
	{
		XjManualRealmTraitReconciliation.HandleRemoved(__instance, trait?.id, __result);
		if (__result)
		{
			XjRuntimeActorInterestIndex.InvalidateCombatClassification(__instance);
			XjCultivatorCandidateIndex.MarkProgressionDirty(__instance);
		}
	}
}
