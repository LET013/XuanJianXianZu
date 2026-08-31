using System;
using System.Reflection;
using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), nameof(Actor.hasStatusTantrum))]
	private static void XuanJian_Actor_HasStatusTantrum_JinXingYaoXie_Postfix(Actor __instance, ref bool __result)
	{
		if (__result || !HasSpecialAiInterest(__instance)
			|| !XjTrueDamageSystem.ShouldLockJinXingYaoXieMadness(__instance))
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
		if (!HasSpecialAiInterest(pAttacker)) return true;
		return !XjTrueDamageSystem.ShouldLockJinXingYaoXieMadness(pAttacker);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), nameof(Actor.nextJobActor))]
	private static bool XuanJian_Actor_NextJobActor_YinSiAndJinXingYaoXie_Prefix(Actor pActor, ref string __result)
	{
		if (pActor?.data == null) return true;

		// 第三方复活出的旧对象可能尚未进入玄鉴兴趣索引；死籍检查必须先于缓存门禁。
		// 这里是全世界单位的 AI 热路径，只允许 ActorData + 运行时隐藏 trait O(1) 检查；
		// saved_traits 迁移留给注册/读档冷入口。
		if (XjTrueDamageSystem.EnforceIrreversibleYinSiClaimFastRuntime(pActor))
		{
			__result = XjYinSiTraitLifecycle.GetYinSiRuntimeJobId();
			return false;
		}

		if (!HasSpecialAiInterest(pActor)) return true;

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
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "prepareForSave")]
	private static void XuanJian_Actor_PrepareForSave_JinXingYaoXie_Postfix(Actor __instance)
	{
		// 原生 prepareForSave 可能重建 saved_traits；在其完成后再次补回未注册的
		// 阴司死籍字符串，保证隐藏 tag 可持久化，同时绝不进入人物可见 trait 集合。
		XjTrueDamageSystem.EnsureJinXingYaoXieNativeSaveStateForSave(__instance);
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
		if (__instance == null || !__instance.isActor() || !HasSpecialAiInterest(__instance.a)) return;
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
		catch (System.Exception xjCaught226) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.JinXingYaoXie.cs:226", xjCaught226); }
		return null;
	}

	private static bool HasSpecialAiInterest(Actor actor)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		return XjWorldBootstrapLane.HasPending
			? XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(actor)
			: XjRuntimeActorInterestIndex.HasStatInterest(actorId);
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
