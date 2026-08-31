using System;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 眩晕免疫/缩短 Guard
/// 语义：修道者免疫眩晕（或大幅缩短眩晕时间）
/// 
/// 对应 0.5.4 ZhanDouStunPatches
/// 
/// 玄门化原则：
/// - Harmony Prefix 拦截，无遍历
/// - O(1) 特质检测
/// </summary>
[HarmonyPatch]
internal static class XjStunGuard
{
    // 眩晕状态效果ID
    private const string StunStatusId = "stun";

    // 眩晕相关的状态效果ID集合
    private static readonly string[] StunLikeStatusIds = new[]
    {
        "stun", "stunned", "paralysis", "root", "wait"
    };

    // ====== 眩晕免疫 ======

    /// <summary>
    /// 判断 Actor 是否免疫眩晕
    /// 筑基及以上修士免疫眩晕，不写入或复用原版 fire_proof。
    /// </summary>
    internal static bool IsStunImmune(Actor actor)
    {
        if (actor?.traits == null)
            return false;

        try
        {
            int realmTier = XjRealmSuppression.GetRealmTier(actor);
            if (realmTier >= XjRealmSuppression.TierZhuJi)
                return true;
        }
        catch (System.Exception xjCaught47) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjStunGuard.cs:47", xjCaught47); }

        return false;
    }

    /// <summary>
    /// 获取眩晕缩短比例（0=不缩短，1=完全免疫）
    /// 炼气修士缩短50%，胎息缩短25%
    /// </summary>
    internal static float GetStunReduction(Actor actor)
    {
        if (actor?.traits == null)
            return 0f;

        try
        {
            // 先检查是否免疫
            if (IsStunImmune(actor))
                return 1f;

            int realmTier = XjRealmSuppression.GetRealmTier(actor);
            if (realmTier == 2) return 0.5f;
            if (realmTier == 1) return 0.25f;
        }
        catch (System.Exception xjCaught73) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjStunGuard.cs:73", xjCaught73); }

        return 0f;
    }

    // ====== Harmony 补丁 ======


    /// <summary>
    /// 原版眩晕主入口。筑基、紫府、金丹在 makeStunned 写入状态前直接拦截，
    /// 避免只拦 addStatusEffect 时遗漏原版使用的 stunned 状态。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Actor), "makeStunned")]
    private static bool Actor_MakeStunned_RealmGuard_Prefix(
        Actor __instance,
        [HarmonyArgument("pTime")] ref float stunTime)
    {
        if (XjHighRealmControlService.NativeGuardBypassActive)
            return true;
        if (__instance?.data == null || stunTime <= 0f)
            return true;

        if (IsStunImmune(__instance))
            return false;

        float reduction = GetStunReduction(__instance);
        if (reduction <= 0f)
            return true;

        stunTime *= 1f - reduction;
        return stunTime > 0.15f;
    }

    /// <summary>
    /// 拦截 addStatusEffect，对眩晕类效果应用免疫/缩短
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BaseSimObject), "addStatusEffect", new Type[] { typeof(string), typeof(float), typeof(bool) })]
    private static bool Actor_AddStatusEffect_StunGuard_Prefix(
        BaseSimObject __instance,
        [HarmonyArgument("pID")] string effectId,
        [HarmonyArgument("pOverrideTimer")] ref float duration)
    {
        if (XjHighRealmControlService.NativeGuardBypassActive)
            return true;
        if (__instance is not Actor actor || string.IsNullOrWhiteSpace(effectId) || duration <= 0f)
            return true;

        // 检查是否为眩晕类效果
        if (!IsStunLikeEffect(effectId))
            return true;

        try
        {
            // 检查眩晕免疫
            if (IsStunImmune(actor))
            {
                // 完全免疫：跳过效果添加
                return false;
            }

            // 检查眩晕缩短
            float reduction = GetStunReduction(actor);
            if (reduction > 0f)
            {
                duration *= (1f - reduction);
                if (duration <= 0.15f) // 如果缩短后时间过短，直接免疫
                    return false;
            }
        }
        catch (System.Exception xjCaught142) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjStunGuard.cs:142", xjCaught142); }

        return true;
    }

    private static bool IsStunLikeEffect(string effectId)
    {
        if (string.IsNullOrWhiteSpace(effectId))
            return false;

        for (int i = 0; i < StunLikeStatusIds.Length; i++)
        {
            if (string.Equals(effectId, StunLikeStatusIds[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
