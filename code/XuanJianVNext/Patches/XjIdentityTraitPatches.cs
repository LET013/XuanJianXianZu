using System;
using HarmonyLib;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Patches;

[HarmonyPatch]
internal static class XjIdentityTraitPatches
{
	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
	private static bool Actor_addTrait_YinSiBlock_Prefix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId)
	{
		return !XjVisibleTraitSync.ShouldBlockMadnessTrait(__instance, traitId)
			&& XjYinSiTraitLifecycle.ShouldAllowVisibleGrant(__instance, traitId);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
	private static bool Actor_addTraitAsset_YinSiBlock_Prefix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait)
	{
		return !XjVisibleTraitSync.ShouldBlockMadnessTrait(__instance, trait?.id)
			&& XjYinSiTraitLifecycle.ShouldAllowVisibleGrant(__instance, trait?.id);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
	private static void Actor_addTrait_Identity_Postfix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		bool __result)
	{
		if (XjCultivationStateTransitions.IsVisibleTraitSyncActive)
		{
			return;
		}

		if (!XjIdentityTraitLifecycle.IsReconciling
			&& XjIdentityTraitLifecycle.IsIdentityTrait(traitId)
			&& (__result || (__instance?.data != null && __instance.hasTrait(traitId))))
		{
			XjIdentityTraitLifecycle.RecordVisibleTraitGranted(__instance, traitId);
		}

		if (!XjYinSiTraitLifecycle.IsReconciling
			&& XjYinSiTraitLifecycle.IsYinSiTrait(traitId)
			&& (__result || (__instance?.data != null && __instance.hasTrait(traitId))))
		{
			XjYinSiTraitLifecycle.RecordVisibleTraitGranted(__instance, traitId);
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
	private static void Actor_addTraitAsset_Identity_Postfix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait,
		bool __result)
	{
		if (XjCultivationStateTransitions.IsVisibleTraitSyncActive)
		{
			return;
		}

		if (!XjIdentityTraitLifecycle.IsReconciling
			&& XjIdentityTraitLifecycle.IsIdentityTrait(trait?.id)
			&& (__result || (__instance?.data != null && !string.IsNullOrWhiteSpace(trait?.id) && __instance.hasTrait(trait.id))))
		{
			XjIdentityTraitLifecycle.RecordVisibleTraitGranted(__instance, trait?.id);
		}

		if (!XjYinSiTraitLifecycle.IsReconciling
			&& XjYinSiTraitLifecycle.IsYinSiTrait(trait?.id)
			&& (__result || (__instance?.data != null && !string.IsNullOrWhiteSpace(trait?.id) && __instance.hasTrait(trait.id))))
		{
			XjYinSiTraitLifecycle.RecordVisibleTraitGranted(__instance, trait?.id);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
	private static void Actor_removeTrait_Identity_Prefix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		out bool __state)
	{
		__state = !XjCultivationStateTransitions.IsVisibleTraitSyncActive
			&& (XjIdentityTraitLifecycle.IsIdentityTrait(traitId)
				|| XjYinSiTraitLifecycle.IsYinSiTrait(traitId))
			&& __instance?.data != null
			&& __instance.hasTrait(traitId);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
	private static void Actor_removeTrait_Identity_Postfix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		bool __state)
	{
		if (!__state
			|| XjCultivationStateTransitions.IsVisibleTraitSyncActive
			|| __instance?.data == null
			|| __instance.hasTrait(traitId))
		{
			return;
		}

		XjIdentityTraitLifecycle.RecordVisibleTraitRemoved(__instance, traitId);
		XjYinSiTraitLifecycle.RecordVisibleTraitRemoved(__instance, traitId);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(ActorTrait) })]
	private static void Actor_removeTraitAsset_Identity_Postfix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait,
		bool __result)
	{
		if (__result && !XjCultivationStateTransitions.IsVisibleTraitSyncActive)
		{
			XjIdentityTraitLifecycle.RecordVisibleTraitRemoved(__instance, trait?.id);
			XjYinSiTraitLifecycle.RecordVisibleTraitRemoved(__instance, trait?.id);
		}
	}
}
