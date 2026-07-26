using System;
using System.Reflection;
using HarmonyLib;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), nameof(Actor.hasStatusTantrum))]
	private static void XuanJian_Actor_HasStatusTantrum_JinXingYaoXie_Postfix(Actor __instance, ref bool __result)
	{
		if (__result || !XjTrueDamageSystem.ShouldLockJinXingYaoXieMadness(__instance))
		{
			return;
		}

		__result = true;
		XjTrueDamageSystem.EnsureJinXingYaoXieMadState(__instance);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "checkSpecialAttackLogic")]
	private static bool XuanJian_Actor_CheckSpecialAttackLogic_JinXingYaoXie_Prefix(
		Actor __instance,
		Actor pAttacker,
		AttackType pAttackType,
		float pInitialDamage,
		out float pDamageFinal)
	{
		pDamageFinal = pInitialDamage;
		return !XjTrueDamageSystem.ShouldLockJinXingYaoXieMadness(pAttacker);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), nameof(Actor.nextJobActor))]
	private static bool XuanJian_Actor_NextJobActor_YinSiAndJinXingYaoXie_Prefix(Actor pActor, ref string __result)
	{
		if (pActor?.data == null)
		{
			return true;
		}

		if (XjYinSiTraitLifecycle.ShouldUseYinSiRuntimeJob(pActor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(pActor))
		{
			__result = XjYinSiTraitLifecycle.GetYinSiRuntimeJobId();
			return false;
		}

		return true;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(Actor), nameof(Actor.nextJobActor))]
	private static Exception XuanJian_Actor_NextJobActor_YinSiAndJinXingYaoXie_Finalizer(Actor pActor, Exception __exception, ref string __result)
	{
		if (__exception == null)
		{
			return null;
		}
		if (__exception is not NullReferenceException and not ArgumentNullException)
		{
			return __exception;
		}

		if (pActor?.data == null
			|| XjYinSiTraitLifecycle.ShouldUseYinSiRuntimeJob(pActor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(pActor))
		{
			__result = XjYinSiTraitLifecycle.GetYinSiRuntimeJobId();
			XjActorAggroBridge.ClearTargets(pActor);
			return null;
		}

		return __exception;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "prepareForSave")]
	private static void XuanJian_Actor_PrepareForSave_JinXingYaoXie_Prefix(Actor __instance)
	{
		XjTrueDamageSystem.EnsureJinXingYaoXieNativeSaveStateForSave(__instance);
		XjYinSiTraitLifecycle.EnsureYinSiNativeSaveStateForSave(__instance);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(BaseSimObject), nameof(BaseSimObject.finishStatusEffect))]
	private static bool XuanJian_BaseSimObject_FinishStatusEffect_JinXingYaoXie_Prefix(
		BaseSimObject __instance,
		string pID)
	{
		if (__instance == null || !__instance.isActor() || !string.Equals(pID, "tantrum", StringComparison.Ordinal))
		{
			return true;
		}

		return !XjTrueDamageSystem.ShouldLockJinXingYaoXieMadness(__instance.a);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(BaseSimObject), nameof(BaseSimObject.finishAllStatusEffects))]
	private static void XuanJian_BaseSimObject_FinishAllStatusEffects_JinXingYaoXie_Postfix(BaseSimObject __instance)
	{
		if (__instance == null || !__instance.isActor())
		{
			return;
		}

		XjTrueDamageSystem.EnsureJinXingYaoXieMadState(__instance.a, true);
	}

	private static readonly MethodInfo JinXingYaoXieBaseAddStatusEffect = AccessTools.Method(
		typeof(BaseSimObject),
		nameof(BaseSimObject.addStatusEffect),
		new[] { typeof(StatusAsset), typeof(float), typeof(bool) });

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), nameof(Actor.addStatusEffect), new[] { typeof(StatusAsset), typeof(float), typeof(bool) })]
	private static bool XuanJian_Actor_AddStatusEffect_JinXingYaoXie_Prefix(
		Actor __instance,
		StatusAsset pStatusAsset,
		float pOverrideTimer,
		bool pColorEffect,
		ref bool __result)
	{
		if (pStatusAsset == null
			|| !string.Equals(pStatusAsset.id, "tantrum", StringComparison.Ordinal)
			|| !XjTrueDamageSystem.ShouldLockJinXingYaoXieMadness(__instance)
			|| !pStatusAsset.affects_mind
			|| !__instance.hasTag("strong_mind"))
		{
			return true;
		}

		try
		{
			__result = JinXingYaoXieBaseAddStatusEffect != null
				&& (bool)JinXingYaoXieBaseAddStatusEffect.Invoke(
					__instance,
					new object[] { pStatusAsset, pOverrideTimer, pColorEffect });
			if (__result && pColorEffect)
			{
				__instance.startColorEffect(ActorColorEffect.White);
			}
		}
		catch
		{
			__result = false;
		}
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), nameof(Actor.removeTrait), new[] { typeof(ActorTrait) })]
	private static bool XuanJian_Actor_RemoveTraitAsset_JinXingYaoXie_Prefix(Actor __instance, ActorTrait pTrait)
	{
		return pTrait == null
			|| !string.Equals(pTrait.id, "madness", StringComparison.Ordinal)
			|| !XjTrueDamageSystem.ShouldLockJinXingYaoXieMadness(__instance);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), nameof(Actor.removeTrait), new[] { typeof(string) })]
	private static bool XuanJian_Actor_RemoveTraitId_JinXingYaoXie_Prefix(Actor __instance, string pTraitID)
	{
		return !string.Equals(pTraitID, "madness", StringComparison.Ordinal)
			|| !XjTrueDamageSystem.ShouldLockJinXingYaoXieMadness(__instance);
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(Actor), "checkCurrentEnemyTarget")]
	private static Exception XuanJian_Actor_CheckCurrentEnemyTarget_JinXingYaoXie_Finalizer(Actor __instance, Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		if (__exception is not NullReferenceException)
		{
			return __exception;
		}

		if (ShouldSwallowCurrentEnemyTargetNull(__instance))
		{
			XjActorAggroBridge.ClearTargets(__instance);
			return null;
		}

		return __exception;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(Actor), "b4_checkTaskVerifier")]
	private static Exception XuanJian_Actor_CheckTaskVerifier_YinSiAndJinXingYaoXie_Finalizer(Actor __instance, Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		if (__exception is not NullReferenceException)
		{
			return __exception;
		}

		if (!ShouldSwallowCurrentEnemyTargetNull(__instance))
		{
			return __exception;
		}

		try
		{
			XjActorAggroBridge.ClearTargets(__instance);
			if (XjYinSiTraitLifecycle.IsYinSi(__instance))
			{
				XjYinSiTraitLifecycle.EnsureTransientState(__instance);
			}
			else if (XjTrueDamageSystem.IsJinXingYaoXie(__instance))
			{
				XjTrueDamageSystem.EnsureJinXingYaoXieMadState(__instance, true);
			}
		}
		catch
		{
		}
		return null;
	}

	private static bool ShouldSwallowCurrentEnemyTargetNull(Actor actor)
	{
		if (actor?.data == null || actor.asset == null)
		{
			return true;
		}
		if (XjTrueDamageSystem.IsJinXingYaoXie(actor) || XjYinSiTraitLifecycle.IsYinSi(actor))
		{
			return true;
		}

		try
		{
			BaseSimObject target = actor.attack_target;
			if (target != null && !target.isAlive())
			{
				return true;
			}
			if (target != null && target.isActor() && target.a?.data == null)
			{
				return true;
			}
		}
		catch
		{
			return true;
		}

		return false;
	}
}
