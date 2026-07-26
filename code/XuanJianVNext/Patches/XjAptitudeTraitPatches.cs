using System;
using HarmonyLib;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Patches;

[HarmonyPatch]
internal static class XjAptitudeTraitPatches
{
	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
	private static bool Actor_addTrait_Aptitude_Prefix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		out XjAptitudeTraitState __state)
	{
		__state = !XjCultivationStateTransitions.IsVisibleTraitSyncActive
			&& XjAptitudeTraitLifecycle.IsAptitudeTrait(traitId)
			? XjAptitudeTraitLifecycle.CaptureState(__instance)
			: default;

		if (!XjCultivationStateTransitions.IsVisibleTraitSyncActive
			&& XjAptitudeTraitLifecycle.ShouldBlockVisibleGrant(__instance, traitId))
		{
			return false;
		}

		return true;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
	private static void Actor_addTrait_Aptitude_Postfix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		bool __result,
		XjAptitudeTraitState __state)
	{
		if (!XjCultivationStateTransitions.IsVisibleTraitSyncActive
			&& __result
			&& XjAptitudeTraitLifecycle.IsAptitudeTrait(traitId))
		{
			XjAptitudeTraitLifecycle.RecordVisibleTraitGranted(__instance, traitId, __state);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
	private static bool Actor_addTraitAsset_Aptitude_Prefix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait,
		out XjAptitudeTraitState __state)
	{
		__state = !XjCultivationStateTransitions.IsVisibleTraitSyncActive
			&& XjAptitudeTraitLifecycle.IsAptitudeTrait(trait?.id)
			? XjAptitudeTraitLifecycle.CaptureState(__instance)
			: default;

		if (!XjCultivationStateTransitions.IsVisibleTraitSyncActive
			&& XjAptitudeTraitLifecycle.ShouldBlockVisibleGrant(__instance, trait?.id))
		{
			return false;
		}

		return true;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
	private static void Actor_addTraitAsset_Aptitude_Postfix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait,
		bool __result,
		XjAptitudeTraitState __state)
	{
		if (!XjCultivationStateTransitions.IsVisibleTraitSyncActive
			&& __result
			&& XjAptitudeTraitLifecycle.IsAptitudeTrait(trait?.id))
		{
			XjAptitudeTraitLifecycle.RecordVisibleTraitGranted(__instance, trait.id, __state);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
	private static void Actor_removeTrait_Aptitude_Prefix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId,
		out bool __state)
	{
		__state = !XjCultivationStateTransitions.IsVisibleTraitSyncActive
			&& XjAptitudeTraitLifecycle.IsAptitudeTrait(traitId)
			&& __instance?.data != null
			&& __instance.hasTrait(traitId);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
	private static void Actor_removeTrait_Aptitude_Postfix(
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

		XjAptitudeTraitLifecycle.RecordVisibleTraitRemoved(__instance, traitId);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(ActorTrait) })]
	private static void Actor_removeTraitAsset_Aptitude_Postfix(
		Actor __instance,
		[HarmonyArgument("pTrait")] ActorTrait trait,
		bool __result)
	{
		if (__result && !XjCultivationStateTransitions.IsVisibleTraitSyncActive)
		{
			XjAptitudeTraitLifecycle.RecordVisibleTraitRemoved(__instance, trait?.id);
		}
	}
}
