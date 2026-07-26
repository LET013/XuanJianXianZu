using System;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 击退抵抗 Guard
/// 语义：抗性(Resist)抵消击退，修士减少受击停顿时间
/// 
/// 对应 0.5.4 ZhanDouDamagePatches 中的 Resist 抵消击退逻辑
/// 
/// 玄门化原则：
/// - O(1) 属性读取，无遍历
/// - 不持有运行状态
/// - 显式参数传递
/// </summary>
[HarmonyPatch]
internal static class XjKnockbackGuard
{
    // ====== 击退抵抗计算 ======

    /// <summary>
    /// 获取击退抵抗系数（0~1）
    /// 0 = 无抵抗（完全击退）
    /// 1 = 完全免疫（无击退）
    /// 
    /// 计算方式：
    /// - Resist 属性提供基础抵抗（每50点=20%抵抗，上限80%）
    /// - 境界等级提供额外抵抗（筑基30%，紫府60%，金丹100%）
    /// </summary>
    internal static float GetKnockbackResistance(Actor actor)
    {
        if (actor == null)
            return 0f;

        try
        {
            // 基础抵抗：来自 Resist 属性
            float resist = XjSafeCore.GetStatSafe(actor, XjSafeCore.Resist, 0f);
            float baseResistance = Mathf.Clamp(resist / 250f, 0f, 0.8f);

            // 境界抵抗：来自 VNext RealmId / XjRealm trait
            int realmTier = XjRealmSuppression.GetRealmTier(actor);
            float realmResistance = realmTier switch
            {
                5 => 1.0f,  // 金丹 → 完全免疫击退
                4 => 0.6f,  // 紫府 → 60%抵抗
                3 => 0.3f,  // 筑基 → 30%抵抗
                2 => 0.1f,  // 炼气 → 10%抵抗
                1 => 0.05f, // 胎息 → 5%抵抗
                _ => 0f
            };

            return Mathf.Clamp(baseResistance + realmResistance, 0f, 1f);
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// 判断 Actor 是否完全免疫击退
    /// </summary>
    internal static bool IsKnockbackImmune(Actor actor)
    {
        return GetKnockbackResistance(actor) >= 1f;
    }

    // ====== Harmony 补丁 ======

    /// <summary>
    /// 拦截 makeWait，根据击退抵抗减少受击等待时间
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Actor), "makeWait")]
    private static bool Actor_MakeWait_KnockbackGuard_Prefix(
        Actor __instance,
        [HarmonyArgument("pValue")] ref float duration)
    {
        if (__instance == null || duration <= 0f)
            return true;

        try
        {
            float resistance = GetKnockbackResistance(__instance);

            // 完全免疫击退
            if (resistance >= 1f)
                return false;

            // 部分抵抗：减少等待时间
            if (resistance > 0f)
            {
                duration *= (1f - resistance);
                // 如果缩短后时间过短，直接免疫
                if (duration <= 0.05f)
                    return false;
            }
        }
        catch
        {
        }

        return true;
    }
}
