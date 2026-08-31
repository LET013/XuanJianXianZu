using System;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 击退抵抗 Guard。
///
/// WorldBox 0.51.2 的真实物理受力入口是 Actor.addForce：它把 pX/pY/pHeight
/// 写入 velocity，并置 under_forces=true。0.51.2 的 strings.S 并不存在
/// knockback_reduction，因此这里不再依赖其它版本/模组的同名常量。
///
/// 性能边界：
/// - 最终 Resist + 境界只在既有 Actor.updateStats 重建时投影一次到隐藏缓存 stat；
/// - addForce / makeWait 热路径只做一次 O(1) stat 读取与几个浮点运算；
/// - 不扫描、不持有 Actor 引用、不新增 Update/LateUpdate。
/// </summary>
[HarmonyPatch]
internal static class XjKnockbackGuard
{
    internal const string KnockbackResistanceStatId = "XjKnockbackResistance";

    // ====== 击退抵抗计算 ======

    /// <summary>
    /// 读取 updateStats 已计算好的 0~1 击退减免。境界特质本身已经写入同一隐藏 stat，
    /// 因此正常运行的 addForce 热路径不解析境界、不分配临时对象。
    /// </summary>
    internal static float GetKnockbackResistance(Actor actor)
    {
        if (actor?.data == null) return 0f;
        try
        {
            return Mathf.Clamp01(XjSafeCore.GetStatSafe(actor, KnockbackResistanceStatId, 0f));
        }
        catch
        {
            return 0f;
        }
    }

    internal static bool IsKnockbackImmune(Actor actor)
    {
        return GetKnockbackResistance(actor) >= 1f;
    }

    /// <summary>
    /// 把玄鉴最终 Resist / 境界对抗击退的旧语义投影到玄鉴自己的隐藏受力缓存。
    /// 调用点位于既有 Actor.updateStats 重建期，所以仙国借境、太阳乘区、龙属等
    /// 已经改变最终 Resist/境界的机制仍自然参与；战斗受力时不再重复解析境界。
    ///
    /// 境界特质本身也可以给 XjKnockbackResistance 写一个显式下限（用于精确保留旧值，
    /// 如释修僧侣 10%）；最终缓存取“特质下限”和“动态 Resist 投影”的较高者。
    /// </summary>
    internal static void ProjectFinalResistToForceGuard(Actor actor)
    {
        if (actor?.stats == null) return;
        try
        {
            float projected = CalculateProjectedResistance(actor);
            float explicitFloor = XjSafeCore.GetStatSafe(actor, KnockbackResistanceStatId, 0f);
            actor.stats[KnockbackResistanceStatId] = Mathf.Clamp01(Mathf.Max(explicitFloor, projected));
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjKnockbackGuard.ProjectFinalResistToForceGuard", ex);
        }
    }

    private static float CalculateProjectedResistance(Actor actor)
    {
        if (actor?.data == null) return 0f;
        try
        {
            float resist = XjSafeCore.GetStatSafe(actor, XjSafeCore.Resist, 0f);
            float baseResistance = Mathf.Clamp(resist / 250f, 0f, 0.8f);
            int realmTier = XjRealmSuppression.GetRealmTier(actor);
            float realmResistance = realmTier switch
            {
                5 => 1.0f,
                4 => 0.6f,
                3 => 0.3f,
                2 => 0.1f,
                1 => 0.05f,
                _ => 0f
            };
            return Mathf.Clamp01(baseResistance + realmResistance);
        }
        catch
        {
            return 0f;
        }
    }

    // ====== Harmony 补丁 ======

    /// <summary>
    /// WorldBox 0.51.2 的实际击退位移入口。只缩放原生 addForce 的三个力参数，
    /// 其余落地、position_height、can_be_moved_by_powers、under_forces 生命周期全部
    /// 继续交给原生方法处理。100% 抵抗时直接跳过本次普通受力。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Actor), "addForce", new Type[]
    {
        typeof(float), typeof(float), typeof(float), typeof(bool), typeof(bool)
    })]
    private static bool Actor_AddForce_KnockbackGuard_Prefix(
        Actor __instance,
        ref float __0,
        ref float __1,
        ref float __2)
    {
        if (XjHighRealmControlService.NativeGuardBypassActive) return true;
        if (__instance?.data == null) return true;

        try
        {
            float resistance = GetKnockbackResistance(__instance);
            if (resistance >= 1f)
            {
                return false;
            }
            if (resistance <= 0f)
            {
                return true;
            }

            float factor = 1f - resistance;
            __0 *= factor;
            __1 *= factor;
            __2 *= factor;
        }
        catch (Exception ex)
        {
            // Force is a combat-hot native path. Any XuanJian failure must fail open so
            // one malformed actor cannot suppress the native force transaction.
            XjExceptionDiagnostics.Report("XjKnockbackGuard.Actor.addForce", ex);
        }

        return true;
    }

    /// <summary>
    /// 受击停顿与实际位移使用同一份缓存减免，避免“身体不飞但仍长时间僵直”。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Actor), "makeWait")]
    private static bool Actor_MakeWait_KnockbackGuard_Prefix(
        Actor __instance,
        [HarmonyArgument("pValue")] ref float duration)
    {
        if (XjHighRealmControlService.NativeGuardBypassActive)
            return true;
        if (__instance?.data == null || duration <= 0f)
            return true;

        try
        {
            float resistance = GetKnockbackResistance(__instance);
            if (resistance >= 1f)
                return false;

            if (resistance > 0f)
            {
                duration *= 1f - resistance;
                if (duration <= 0.05f)
                    return false;
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjKnockbackGuard.Actor.makeWait", ex);
        }

        return true;
    }
}
