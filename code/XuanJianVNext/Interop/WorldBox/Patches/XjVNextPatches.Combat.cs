using System;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Talisman;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{

    // 0.9.8.5：长庚不再把主动法术当作主要输出。原生普攻伤害链保持不动，
    // 只提升其有效出手距离并把可见出手改成飞剑，避免另造一套伤害/暴击判定。
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(Actor), "getAttackRange")]
    public static void XuanJianVNext_Actor_GetAttackRange_LongGeng_Postfix(Actor __instance, ref float __result)
    {
        float range = XjLongGengFlyingSwordSystem.ResolveAttackRange(__instance);
        if (range > __result) __result = range;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(Actor), "getAttackRangeSquared")]
    public static void XuanJianVNext_Actor_GetAttackRangeSquared_LongGeng_Postfix(Actor __instance, ref float __result)
    {
        float range = XjLongGengFlyingSwordSystem.ResolveAttackRange(__instance);
        float squared = range * range;
        if (squared > __result) __result = squared;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(Actor), "tryToAttack")]
    public static void XuanJianVNext_Actor_TryToAttack_LongGengFlyingSword_Postfix(
        Actor __instance, BaseSimObject pTarget, ref bool __result)
    {
        if (!__result) return;
        XjLongGengFlyingSwordSystem.TrySpawnNormalAttackVisual(__instance, pTarget);
        XjLongShuSystem.TryResolveNormalAttack(__instance, pTarget);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(CombatActionLibrary), "showMeleeSlashAttack")]
    public static bool XuanJianVNext_CombatActionLibrary_ShowMeleeSlash_LongGeng_Prefix(AttackData pData)
    {
        Actor actor = null;
        try { actor = pData.initiator?.a; } catch { }
        return !XjLongGengFlyingSwordSystem.IsLongGengCombatant(actor);
    }
    // 0.9.8.4：归因冻结只锁被高境控制服务实际判定命中的角色。
    // 热路径首项是字典 Count 门控；无冻结时只付出一次分支判断。
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "updateParallelChecks")]
    public static bool XuanJianVNext_Actor_UpdateParallelChecks_AttributedFreeze_Prefix(Actor __instance)
    {
        if (!XjAttributedFreezeRegistry.IsFrozen(__instance)) return true;
        XjAttributedFreezeRegistry.MaintainFrozenActor(__instance);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "b6_0_updateDecision")]
    public static bool XuanJianVNext_Actor_UpdateDecision_AttributedFreeze_Prefix(Actor __instance)
    {
        return !XjAttributedFreezeRegistry.IsFrozen(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "b6_updateAI")]
    public static bool XuanJianVNext_Actor_UpdateAI_AttributedFreeze_Prefix(Actor __instance)
    {
        return !XjAttributedFreezeRegistry.IsFrozen(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "tryToAttack")]
    public static bool XuanJianVNext_Actor_TryToAttack_AttributedFreeze_Prefix(Actor __instance)
    {
        return !XjAttributedFreezeRegistry.IsFrozen(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "attackTargetActions")]
    public static bool XuanJianVNext_Actor_AttackTargetActions_AttributedFreeze_Prefix(Actor __instance)
    {
        return !XjAttributedFreezeRegistry.IsFrozen(__instance);
    }

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(MapBox), "applyAttack")]
	public static bool XuanJianVNext_MapBox_ApplyAttack_ProjectileGuard_Prefix(AttackData pData, ref AttackDataResult __result)
	{
		if (XjProjectileGuard.IsAttackProjectileLookupSafe(pData))
		{
			return true;
		}

		__result = AttackDataResult.Miss;
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(CombatActionLibrary), "attackRangeAction")]
	public static bool XuanJianVNext_CombatActionLibrary_AttackRangeAction_ProjectileGuard_Prefix(AttackData pData, ref bool __result)
	{
		if (XjProjectileGuard.IsRangeActionProjectileSafe(pData))
		{
			return true;
		}

		__result = false;
		return false;
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "hasRangeAttack")]
	public static void XuanJianVNext_Actor_HasRangeAttack_InvalidProjectileFallback_Postfix(
		Actor __instance,
		ref bool __result)
	{
		if (!__result || __instance == null) return;
		long actorId = __instance.data is BaseSystemData data ? data.id : 0L;
		if (actorId > 0L && !XjWorldBootstrapLane.HasPending
			&& !XjRuntimeActorInterestIndex.HasStatInterest(actorId)
			&& !XjRuntimeActorInterestIndex.HasFaBaoInterest(actorId)) return;

		try
		{
			if (!XjProjectileGuard.IsValidProjectileAssetId(__instance.getWeaponAsset()?.projectile))
			{
				__result = false;
			}
		}
		catch
		{
			__result = false;
		}
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "hasMeleeAttack")]
	public static void XuanJianVNext_Actor_HasMeleeAttack_InvalidProjectileFallback_Postfix(
		Actor __instance,
		ref bool __result)
	{
		if (__result || __instance == null) return;
		long actorId = __instance.data is BaseSystemData data ? data.id : 0L;
		if (actorId > 0L && !XjWorldBootstrapLane.HasPending
			&& !XjRuntimeActorInterestIndex.HasStatInterest(actorId)
			&& !XjRuntimeActorInterestIndex.HasFaBaoInterest(actorId)) return;

		try
		{
			if (!XjProjectileGuard.IsValidProjectileAssetId(__instance.getWeaponAsset()?.projectile))
			{
				__result = true;
			}
		}
		catch
		{
			__result = true;
		}
	}


	// WorldBox owns warrior eligibility, City.army and ArmyManager lifecycle.
	// XuanJian intentionally has no Army recruitment/setArmy/checkArmyExistence patches.

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(ProjectileManager), "spawn")]
	public static bool XuanJianVNext_ProjectileManager_Spawn_ProjectileGuard_Prefix(
		BaseSimObject pInitiator,
		BaseSimObject pTargetObject,
		string pAssetID,
		ref Projectile __result)
	{
		// 冰封只阻止“冻结后新发出的”弹道；已经在空中的投射物继续按原轨迹结算，
		// 与鬼谷的全时停语义区分开，保持太阴是冰封而不是冻结时间。
		if (XjAttributedFreezeRegistry.IsFrozen(pInitiator))
		{
			__result = null;
			return false;
		}

		// 坐标型/范围型投射物允许 target 为 null；只校验资源 ID。
		// 旧端点校验会把合法的无目标剑影、范围法术全部拦截，表现为远程角色完全打不动。
		if (XjProjectileGuard.IsValidProjectileAssetId(pAssetID))
		{
			return true;
		}

		__result = null;
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Projectile), "start")]
	public static bool XuanJianVNext_Projectile_Start_ProjectileGuard_Prefix(string pAssetID)
	{
		return XjProjectileGuard.IsValidProjectileAssetId(pAssetID);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Projectile), "update", new Type[] { typeof(float) })]
	public static bool XuanJianVNext_Projectile_Update_BrokenAssetGuard_Prefix(Projectile __instance)
	{
		if (!XjProjectileGuard.IsBrokenRuntimeProjectile(__instance))
		{
			return true;
		}

		XjProjectileGuard.RemoveBrokenProjectile(__instance);
		return false;
	}

	// 原生 EnemyFinder 在投射物跨越无效地图块时会从 updateVelocity 抛出
	// 空引用。Prefix 只能预筛当前坐标，无法覆盖本帧内的轨迹变化；这里
	// 只吞掉该已知异常并立即清理投射物，避免同一对象每帧重复写 Player.log。
	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Projectile), "updateVelocity", new Type[] { typeof(float) })]
	public static Exception XuanJianVNext_Projectile_UpdateVelocity_NullChunkFinalizer(
		Projectile __instance,
		Exception __exception)
	{
		if (__exception is NullReferenceException)
		{
			XjProjectileGuard.RemoveBrokenProjectile(__instance);
			return null;
		}
		return __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(ProjectileManager), "update")]
	public static void XuanJianVNext_ProjectileManager_Update_ProjectileGuard_Prefix(ProjectileManager __instance)
	{
		XjProjectileGuard.CullBrokenProjectiles(__instance);
	}

	// A loader can place another dynamic wrapper outside Projectile.updateVelocity,
	// bypassing that method's finalizer.  Retain a narrow manager-level fallback
	// for the known EnemyFinder null-reference so it cannot repeat hundreds of
	// times and turn a recoverable bad projectile into a process exit.
	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(ProjectileManager), "update")]
	public static Exception XuanJianVNext_ProjectileManager_Update_EnemyFinderFallback_Finalizer(
		ProjectileManager __instance,
		Exception __exception)
	{
		if (XjProjectileGuard.IsEnemyFinderNullReference(__exception))
		{
			XjProjectileGuard.RecoverFromEnemyFinderFailure(__instance);
			return null;
		}
		return __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BaseSimObject), "changeHealth", new Type[] { typeof(int) })]
	private static void XuanJianVNext_BaseSimObject_ChangeHealth_ClosedCultivationVitality_Prefix(
		BaseSimObject __instance,
		ref int __0)
	{
		// changeHealth声明在BaseSimObject。旧补丁以Actor为目标时会连同Building一起进入
		// Actor专属读取链，造成城建/攻城期间持续空引用与日志风暴。
		if (__instance is not Actor actor) return;
		__0 = XjDaoTaiSurvivalGuard.ResolveProtectedHealthDelta(actor, __0);
		__0 = XjReincarnationSurvivalGuard.ResolveProtectedHealthDelta(actor, __0);
		if (XjClosedCultivationGuard.IsActivelyProtected(actor) && __0 < 0) __0 = 0;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BaseSimObject), "setHealth", new Type[] { typeof(int), typeof(bool) })]
	private static void XuanJianVNext_BaseSimObject_SetHealth_ClosedCultivationVitality_Prefix(
		BaseSimObject __instance,
		ref int __0)
	{
		// setHealth同样声明在BaseSimObject；只允许真实Actor进入闭关生命保护。
		if (__instance is not Actor actor) return;
		__0 = XjDaoTaiSurvivalGuard.ResolveProtectedSetHealth(actor, __0);
		__0 = XjReincarnationSurvivalGuard.ResolveProtectedSetHealth(actor, __0);
		__0 = XjClosedCultivationGuard.ResolveProtectedHealthValue(actor, __0);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "getHit")]
	private static bool XuanJianVNext_Actor_GetHit_UnifiedCombat_Prefix(
		Actor __instance,
		ref float pDamage,
		bool pFlash,
		AttackType pAttackType,
		BaseSimObject pAttacker,
		bool pSkipIfShake,
		bool pMetallicWeapon,
		ref bool pCheckDamageReduction,
		ref XjSynchronousCombatDecisionState __state)
	{
		XjSynchronousCombatDecision decision = XjSynchronousCombatDecisionKernel.Evaluate(
			__instance,
			pDamage,
			pAttackType,
			pAttacker,
			pCheckDamageReduction);
		pDamage = decision.Damage;
		pCheckDamageReduction = decision.CheckDamageReduction;
		__state = decision.State;
		return decision.ShouldRunOriginal;
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "getHit")]
	private static void XuanJianVNext_Actor_GetHit_UnifiedCombat_Postfix(
		Actor __instance,
		float pDamage,
		BaseSimObject pAttacker,
		XjSynchronousCombatDecisionState __state)
	{
		XjSynchronousCombatDecisionKernel.Complete(__instance, pAttacker, in __state);
	}
}
