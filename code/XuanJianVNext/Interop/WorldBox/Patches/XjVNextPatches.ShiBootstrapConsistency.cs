using System;
using System.Collections.Generic;
using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Patches;

/// <summary>
/// 释修读档/注册一致性桥。合法释修可能没有普通城镇/国家归属；若旧档仍保留
/// ShiRealm + ShiTradition，却丢了 CultivationPath，则在角色注册冷路径补回 shi，
/// 并立即刷新释修缓存，避免仙鉴同年显示“0人”。
/// </summary>
internal partial class XjVNextPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjCultivationEligibility), "CanCultivate")]
    private static void XuanJianVNext_ShiBootstrap_CanCultivate_Postfix(Actor actor, ref bool __result)
    {
        if (__result || !HasPersistedShiIdentityForBootstrap(actor)) return;
        // 原入口的运行开关、剑道兼容与四族硬边界仍然保留；这里只解除
        // 已入释角色对普通 city/kingdom 文明上下文的依赖。
        if (!XjRuntimeSettings.CultivationEnabled
            || !XjJianDaoCompatibility.ShouldAllowCultivation(actor)
            || !XjCultivationEligibility.CanReceiveXuanJianContent(actor)) return;
        __result = true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(XjScheduler), "TryRegisterEligibleActor")]
    private static void XuanJianVNext_ShiBootstrap_Register_Prefix(Actor actor, out bool __state)
    {
        __state = IsCachedShiActor(actor);
        ReconcilePersistedShiPathForBootstrap(actor);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjScheduler), "TryRegisterEligibleActor")]
    private static void XuanJianVNext_ShiBootstrap_Register_Postfix(Actor actor, bool __result, bool __state)
    {
        if (!__result || actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return;
        if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string shiRealm)
            || string.IsNullOrWhiteSpace(shiRealm)) return;

        // 绕开普通资质种子队列的显示延迟；这是已建立的释修身份，只做单角色
        // 缓存恢复，不扫描 World.world.units。
        XjCultivatorCache.CheckAndUpdate(actor);
        if (__state != IsCachedShiActor(actor)) XjShiWorldRegistry.Invalidate();
    }

    private static bool HasPersistedShiIdentityForBootstrap(Actor actor)
    {
        if (actor?.data == null) return false;
        if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string shiRealm)
            || string.IsNullOrWhiteSpace(shiRealm)
            || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition)
            || (!string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
                && !string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))) return false;
        if (XjCultivationPathRules.IsShi(actor)) return true;
        // 只补“确实缺失”的 path；已有其他道途时不擅自覆盖。
        return !XjActorAccessor.TryGetString(actor, XjActorDataKeys.CultivationPath, out string rawPath)
            || string.IsNullOrWhiteSpace(rawPath);
    }

    private static void ReconcilePersistedShiPathForBootstrap(Actor actor)
    {
        if (actor?.data == null || XjCultivationPathRules.IsShi(actor)
            || !HasPersistedShiIdentityForBootstrap(actor)) return;
        XjCultivationPathTransitions.SetPathMetadataForAuthorityTransition(actor, XjCultivationPathIds.Shi);
    }

    private static bool IsCachedShiActor(Actor actor)
    {
        if (actor?.data == null) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return false;
        IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == actorId) return true;
        }
        return false;
    }
}
