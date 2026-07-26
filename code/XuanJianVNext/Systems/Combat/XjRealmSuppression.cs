using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 境界压制系统。
/// </summary>
internal static class XjRealmSuppression
{
    // ====== 大境界 (Realm Tier) ======
    public const int TierNone = 0;
    public const int TierTaiXi = 1;
    public const int TierLianQi = 2;
    public const int TierZhuJi = 3;
    public const int TierZiFu = 4;
    public const int TierJinDan = 5;

    // ====== 战斗等级 (Combat Level) ======
    // 胎息六轮: 玄景1 承明2 周行3 青元4 玉京5
    // 灵初: 6
    // 炼气九层: 7-15
    // 筑基三期: 16-18
    // 紫府五期: 19-23
    // 金丹四期: 24-27

    /// <summary>
    /// O(1) 获取大境界等级 (Tier)
    /// </summary>
    internal static int GetRealmTier(Actor actor)
    {
        if (actor?.data == null)
            return TierNone;
        if (XjAnnualExecutionContext.TryResolveRealmOverride(actor, out string overrideRealmId))
            return GetRealmTierFromIdForRuntime(overrideRealmId);

        try
        {
            if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
            {
                int storedTier = GetRealmTierFromIdForRuntime(realmId);
                if (storedTier > TierNone) return storedTier;
            }

            if (actor.hasTrait(XjRealmIds.JinDan)) return TierJinDan;
            if (actor.hasTrait(XjRealmIds.ZiFu)) return TierZiFu;
            if (actor.hasTrait(XjRealmIds.ZhuJi)) return TierZhuJi;
            if (actor.hasTrait(XjRealmIds.LianQi)) return TierLianQi;
            if (actor.hasTrait(XjRealmIds.TaiXi)) return TierTaiXi;
        }
        catch { }

        return TierNone;
    }

    internal static int GetRealmTierFromIdForRuntime(string realmId)
    {
        if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
            || string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)) return TierJinDan;
        if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return TierZiFu;
        if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return TierZhuJi;
        if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return TierLianQi;
        if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return TierTaiXi;
        return TierNone;
    }

    /// <summary>
    /// 获取真实战斗等级。未写入时返回 0，不用大境界臆造细分层级。
    /// </summary>
    internal static int GetCombatLevel(Actor actor)
    {
        if (actor?.data == null) return 0;

        try
        {
            if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjCombatLevel, out int storedLevel)
                && storedLevel > 0)
            {
                return storedLevel;
            }
        }
        catch { return 0; }

        return 0;
    }

    /// <summary>
    /// 存储战斗等级（突破时由 Cultivation 系统写入）
    /// </summary>
    internal static void SetCombatLevel(Actor actor, int level)
    {
        if (actor?.data == null) return;
        try
        {
            XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCombatLevel, Math.Max(0, level));
        }
        catch { }
    }

    /// <summary>
    /// 从真实道行阶段同步战斗细分等级，不再用大境界臆造 fallback。
    /// </summary>
    internal static void SyncCombatLevel(Actor actor)
    {
        if (actor?.data == null)
        {
            return;
        }

        try
        {
            XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
            XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
            XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int xianJiCount);
            XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int jinDanYiXiang);
            SetCombatLevel(actor, ResolveCombatLevel(realmId, zhenYuan, xianJiCount, XjFaBaoBonusService.GetEffectiveJinDanYiXiang(actor, jinDanYiXiang)));
        }
        catch
        {
            SetCombatLevel(actor, 0);
        }
    }

    internal static int ResolveCombatLevel(string realmId, float zhenYuan, int xianJiCount, int jinDanYiXiang)
    {
        string normalizedRealm = realmId ?? string.Empty;
        if (string.Equals(normalizedRealm, XjRealmIds.TaiXi, StringComparison.Ordinal))
        {
            return XjTaiXiStageRules.ResolveStage(zhenYuan);
        }

        if (string.Equals(normalizedRealm, XjRealmIds.LianQi, StringComparison.Ordinal))
        {
            if (zhenYuan < 400f) return 7;
            if (zhenYuan < 500f) return 8;
            if (zhenYuan < 600f) return 9;
            if (zhenYuan < 700f) return 10;
            if (zhenYuan < 800f) return 11;
            if (zhenYuan < 900f) return 12;
            if (zhenYuan < 1000f) return 13;
            if (zhenYuan < 1100f) return 14;
            return 15;
        }

        if (string.Equals(normalizedRealm, XjRealmIds.ZhuJi, StringComparison.Ordinal))
        {
            if (zhenYuan < 12000f) return 16;
            if (zhenYuan < 24000f) return 17;
            return 18;
        }

        if (string.Equals(normalizedRealm, XjRealmIds.ZiFu, StringComparison.Ordinal))
        {
            if (xianJiCount >= 5) return 23;
            if (xianJiCount >= 4) return 22;
            if (xianJiCount >= 3 && zhenYuan >= 90000f) return 21;
            if (xianJiCount >= 3) return 20;
            return 19;
        }

        if (string.Equals(normalizedRealm, XjRealmIds.JinDan, StringComparison.Ordinal))
        {
            if (jinDanYiXiang >= 6000) return 27;
            if (jinDanYiXiang >= 3000) return 26;
            if (jinDanYiXiang >= 1000) return 25;
            return 24;
        }
        if (string.Equals(normalizedRealm, XjRealmIds.ShenDan, StringComparison.Ordinal))
        {
            return 24;
        }

        return 0;
    }

    internal static string GetRealmTierName(int tier)
    {
        return tier switch
        {
            TierTaiXi => "胎息",
            TierLianQi => "炼气",
            TierZhuJi => "筑基",
            TierZiFu => "紫府",
            TierJinDan => "金丹",
            _ => "凡人"
        };
    }

    internal static bool IsCultivator(Actor actor)
    {
        return GetRealmTier(actor) > TierNone;
    }

    /// <summary>
    /// 应用境界压制。
    ///
    /// 规则：
    /// - 低于目标大境界：普通攻击伤害归零。只有明确授权的特殊权柄链
    ///   （当前为绑定阴司追索金丹）可由统一结算器绕过。
    /// - 高境界攻击低境界：本方法不再写入处决伤害，防御穿透由统一伤害结算器处理。
    /// - 同一大境界：按真实 CombatLevel 差值微调，上限 ±60%。
    /// - 双方均非修士：不处理。
    ///
    /// 返回值：true = damage 已被修改，false = 无需处理。
    /// </summary>
    internal static bool ApplySuppression(ref float damage, Actor attacker, Actor defender)
    {
        if (attacker == null || defender == null || damage <= 0f)
            return false;

        int attackerTier = GetRealmTier(attacker);
        int defenderTier = GetRealmTier(defender);
        return ApplySuppression(ref damage, attacker, defender, attackerTier, defenderTier);
    }

    internal static bool ApplySuppression(ref float damage, Actor attacker, Actor defender, int attackerTier, int defenderTier)
    {
        if (attacker == null || defender == null || damage <= 0f)
            return false;

        if (attackerTier == TierNone && defenderTier == TierNone)
            return false;

        if (attackerTier < defenderTier)
        {
            // 普通角色攻击受大境界绝对压制；规则真伤由独立入口结算。
            damage = 0f;
            return true;
        }

        // 高境界不再通过“目标最大生命值 × 1.05”强制处决。
        // 统一结算器会把本次伤害作为大境界优势真伤处理，保留正常伤害成长与死亡链。
        if (attackerTier > defenderTier)
            return false;

        int attackerCombatLevel = GetCombatLevel(attacker);
        int defenderCombatLevel = GetCombatLevel(defender);
        if (attackerCombatLevel > 0
            && defenderCombatLevel > 0
            && attackerCombatLevel != defenderCombatLevel)
        {
            int diff = attackerCombatLevel - defenderCombatLevel;
            float adjustment = 1f + Mathf.Clamp(diff * 0.12f, -0.6f, 0.6f);
            damage = Mathf.Max(0.01f, damage * adjustment);
            return true;
        }

        return false;
    }

}
