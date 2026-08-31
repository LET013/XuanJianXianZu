using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "updateAge")]
	public static bool XuanJianVNext_Actor_UpdateAge_XingDuQianPhantomGuard_Prefix(Actor __instance)
	{
		if (__instance?.data == null) return true;
		long actorId = ((BaseSystemData)__instance.data).id;
		// 普通人口年度边界只做一次 O(1) interest 查询；读档 bootstrap 期间仅对已有玄鉴落盘标记者补查。
		bool indexed = XjRuntimeActorInterestIndex.HasStatInterest(actorId);
		bool bootstrapCandidate = XjWorldBootstrapLane.HasPending
			&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(__instance);
		return (!indexed && !bootstrapCandidate) || !XjXingDuQianPhantomSystem.IsPhantom(__instance);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "updateAge")]
	public static void XuanJianVNext_Actor_UpdateAge_RegisterPostfix(Actor __instance)
	{
		if (XjXingDuQianPhantomSystem.IsPhantom(__instance)) return;
		// Harmony只上报原生年度边界，寿限、资格与年度入队由内部事件总线统一归并。
		XjInternalEventBus.PublishActorAnnualBoundary(__instance);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "updateNutritionDecay")]
	public static bool XuanJianVNext_Actor_UpdateNutritionDecay_CultivatorStarvationGuard_Prefix(
		Actor __instance,
		[HarmonyArgument(0)] ref bool pDoStarvationDamage)
	{
		if (__instance?.data == null) return true;
		long actorId = ((BaseSystemData)__instance.data).id;
		bool runtimeInterested = actorId > 0L && XjRuntimeActorInterestIndex.HasStatInterest(actorId);
		bool bootstrapCandidate = XjWorldBootstrapLane.HasPending
			&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(__instance);

		if (runtimeInterested && XjXingDuQianPhantomSystem.IsPhantom(__instance))
		{
			// 幻身没有自然寿命、饥饿与社会成长；保留原生移动/战斗 AI。
			pDoStarvationDamage = false;
			return false;
		}

		// Post-bootstrap ordinary population has no玄鉴 starvation exception. Leave
		// immediately after the single O(1) interest lookup; do not repeat alive,
		// realm, LongShu, ShenDan or sanctuary checks on every nutrition tick.
		if (!runtimeInterested && !bootstrapCandidate) return true;

		// Only indexed/bootstrap candidates can be modern Shi. This removes the
		// tradition/domain lookup from nutrition updates for ordinary population.
		if (XjZhantanlinSystem.IsModernShiInside(__instance))
		{
			pDoStarvationDamage = false;
			return false;
		}
		if (!pDoStarvationDamage || !XjSafeCore.IsAliveActor(__instance))
		{
			return true;
		}

		if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier))
		{
			// Never parse the entire trait chain for ordinary starving units. During
			// bootstrap an unindexed persisted cultivator still receives the guard;
			// after bootstrap all eligible actors are served from the O(1) cache.
			if (!XjWorldBootstrapLane.HasPending)
			{
				return true;
			}
			realmTier = XjRealmSuppression.GetRealmTier(__instance);
		}
		if (realmTier > XjRealmSuppression.TierNone
			|| XjLongShuSystem.IsLongShu(__instance)
			|| XjActorRuntimeStatProjection.IsShenDan(__instance))
		{
			// 修士仍会消耗食物，但饥饿不会转化为原生掉血或死亡。
			pDoStarvationDamage = false;
		}
		return true;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "updateStats")]
	public static void XuanJianVNext_Actor_UpdateStats_RealmLifespanRepair_Prefix(Actor __instance)
	{
		try
		{
			if (__instance?.data == null)
			{
				return;
			}

			long actorId = ((BaseSystemData)__instance.data).id;
			if (XjRuntimeActorInterestIndex.HasStatInterest(actorId)
				&& XjXingDuQianPhantomSystem.IsPhantom(__instance)) return;
			if (!XjRuntimeActorInterestIndex.HasStatInterest(actorId)
				&& (!XjWorldBootstrapLane.HasPending
					|| !XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(__instance)))
			{
				return;
			}

			// 仅已索引的玄鉴角色才检查寿元。旧实现会在每个原生角色的
			// updateStats 中解析境界与特质链，是高人口世界的主要热路径之一。
			XjRealmLifespanService.MarkDirtyWhenStale(__instance);
			XjRealmStageStatMultiplierService.MarkDirtyWhenStale(__instance);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Interop/Actor/updateStats-prefix", ex);
		}
	}

	/// <summary>
	/// 玄鉴运行期属性必须只在原版 updateStats 真正重建 BaseStats 的路径内合并。
	/// 旧实现使用 Postfix；当人物窗口/排行榜头像调用 updateStats 而原版因 stats 未脏提前返回时，
	/// Postfix 仍会再次乘入境界、法宝、纪元与功法倍率，造成每次打开页面战力复利膨胀。
	/// 此处改为在 BaseStats.normalize 前注入：原版未重建属性时不会执行，且同一次重建只执行一次。
	/// </summary>
	[HarmonyTranspiler]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "updateStats")]
	public static IEnumerable<CodeInstruction> XuanJianVNext_Actor_UpdateStats_RuntimeBonuses_Transpiler(
		IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
		MethodInfo normalizeMethod = AccessTools.Method(typeof(BaseStats), nameof(BaseStats.normalize));
		MethodInfo applyMethod = AccessTools.Method(
			typeof(XjActorRuntimeStatProjection),
			nameof(XjActorRuntimeStatProjection.Apply));

		int normalizeIndex = codes.FindIndex(code =>
			(code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call)
			&& code.operand is MethodInfo method
			&& (method == normalizeMethod
				|| (method.DeclaringType == typeof(BaseStats)
					&& string.Equals(method.Name, nameof(BaseStats.normalize), StringComparison.Ordinal))));

		if (normalizeIndex < 0 || applyMethod == null)
		{
			Debug.LogWarning("[玄鉴] updateStats 未找到 BaseStats.normalize，已跳过运行期属性注入以避免重复累加。");
			return codes;
		}

		CodeInstruction loadActor = new CodeInstruction(OpCodes.Ldarg_0);
		if (codes[normalizeIndex].labels.Count > 0)
		{
			loadActor.labels.AddRange(codes[normalizeIndex].labels);
			codes[normalizeIndex].labels.Clear();
		}

		codes.InsertRange(normalizeIndex, new[]
		{
			loadActor,
			new CodeInstruction(OpCodes.Call, applyMethod)
		});
		return codes;
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "updateStats")]
	public static void XuanJianVNext_Actor_UpdateStats_RefreshCombatCache_Postfix(Actor __instance)
	{
		try
		{
			if (__instance?.data == null || __instance.stats == null)
			{
				return;
			}

			long actorId = ((BaseSystemData)__instance.data).id;
			bool hasRuntimeStatInterest = XjRuntimeActorInterestIndex.HasStatInterest(actorId);
			bool bootstrapCandidate = XjWorldBootstrapLane.HasPending
				&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(__instance);

			// BaseStats.normalize 之后再次锁定凡俗最终寿元。普通人口只做 civ
			// 判定与一次赋值，随后立即退出；combat cache/reconcile 只属于玄鉴角色。
			XjRealmLifespanService.ClampFinalMortalCivilianLifespanFast(
				__instance, hasRuntimeStatInterest, bootstrapCandidate);
			if (!hasRuntimeStatInterest && !bootstrapCandidate) return;

			bool shouldRefresh = XjRuntimeActorInterestIndex.HasCombatInterest(actorId)
				|| XjCombatHotPathCache.Contains(actorId)
				|| bootstrapCandidate;
			if (shouldRefresh)
			{
				XjCombatHotPathCache.Refresh(__instance);
			}
			XjActorRuntimeStatProjection.ReconcileHealth(__instance, actorId);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Interop/Actor/updateStats-postfix", ex);
		}
	}


	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "die", new Type[]
	{
		typeof(bool),
		typeof(AttackType),
		typeof(bool),
		typeof(bool)
	})]
	private static bool ActorDieArgs_Prefix(
		Actor __instance,
		AttackType __1,
		out XjSynchronousDeathPatchState __state)
	{
		__state = XjSynchronousDeathDecisionKernel.Begin(__instance, __1);
		return __state.AllowOriginal;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "dieAndDestroy", new Type[] { typeof(AttackType) })]
	private static bool ActorDieAndDestroy_DaoTaiSurvival_Prefix(Actor __instance, AttackType __0)
	{
		if (XjExternalUnitTransferContext.IsExplicitReplacementRemoval(__instance)) return true;
		if (XjDaoTaiSurvivalGuard.ShouldBlockDirectDestroy(__instance))
		{
			XjDaoTaiSurvivalGuard.RecoverFromBlockedDeath(__instance);
			return false;
		}
		if (XjReincarnationSurvivalGuard.ShouldBlockDirectDestroy(__instance))
		{
			XjReincarnationSurvivalGuard.RecoverFromBlockedDeath(__instance);
			return false;
		}
		if (XjMingShuChildSystem.TryConsumeDeathWardForDirectDestroy(__instance, __0))
		{
			return false;
		}
		return true;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "die", new Type[]
	{
		typeof(bool),
		typeof(AttackType),
		typeof(bool),
		typeof(bool)
	})]
	private static void ActorDieArgs_Postfix(Actor __instance, XjSynchronousDeathPatchState __state)
	{
		XjSynchronousDeathDecisionKernel.Complete(__instance, in __state);
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(Actor), "die", new Type[]
	{
		typeof(bool),
		typeof(AttackType),
		typeof(bool),
		typeof(bool)
	})]
	private static Exception ActorDieArgs_BoundEquipment_Finalizer(
		Actor __instance,
		XjSynchronousDeathPatchState __state,
		Exception __exception)
	{
		return XjSynchronousDeathDecisionKernel.Abort(__instance, in __state, __exception);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "checkNaturalDeath")]
	private static bool ActorCheckNaturalDeath_XuanJianLifespan_Prefix(Actor __instance, ref bool __result)
	{
		try
		{
			return XjVanillaDeathGuard.TryHandleNaturalDeath(__instance, ref __result);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Interop/Actor/checkNaturalDeath", ex);
			return true;
		}
	}
}
