using System;
using UnityEngine;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 金丹同级战斗的唯一品秩模型。
///
/// 大境界 Tier 只回答“能否跨境压制”；本品秩只在 TierJinDan 内回答
/// “同为金丹级战力时谁的成道质量更高”。任何特殊仙身、释修法相、神丹、
/// 仙国假金丹都必须从这里比较，禁止再各自借 CombatLevel 偷渡成正统金丹。
///
/// 固定顺序：
/// 正统金丹 ＞ 古法相≈郁仪 ＞ 结璘 ＞ 神丹 ＞ 仙国假金丹≈今法相。
/// 真君羽士保留服气养性的既有同级数值，只在本表中独立标识，绝不被称为“正统金丹”。
/// </summary>
internal static class XjHighRealmCombatGrade
{
    internal enum Kind
    {
        None = 0,
        ModernDharmaForm = 1,
        XianGuoFalseJinDan = 2,
        ShenDan = 3,
        JieLinXian = 4,
        YuYiXian = 5,
        AncientDharmaForm = 6,
        ZhenJunYuShi = 7,
        OrthodoxJinDan = 8,
    }

    internal const int ModernMinimum = 520;
    internal const int ModernMaximum = 548;
    internal const int XianGuoMinimum = 520;
    internal const int XianGuoMaximum = 548;
    internal const int ShenDanGrade = 570;
    internal const int JieLinGrade = 610;
    internal const int YuYiGrade = 646;
    internal const int AncientMinimum = 638;
    internal const int AncientMaximum = 656;
    internal const int ZhenJunMinimum = 700;
    internal const int ZhenJunMaximum = 730;
    internal const int OrthodoxMinimum = 700;
    internal const int OrthodoxMaximum = 730;

    /// <summary>
    /// 只用于战斗。仙国假金丹可把真实紫府/真人临时投影到 TierJinDan，
    /// 但绝不写回 RealmId、寿元、金性、果位或培养缓存。
    /// </summary>
    internal static int ResolveEffectiveCombatTier(Actor actor, int realTier)
    {
        return XjXianGuoSystem.ResolveEffectiveCombatTier(actor, realTier);
    }

    internal static bool TryResolveGrade(Actor actor, out int grade)
        => TryResolveGrade(actor, out grade, out _);

    internal static bool IsOrthodoxJinDan(Actor actor)
    {
        if (actor?.data == null || !actor.isAlive()
            || XjCultivationPathRules.IsShi(actor)
            || XjXuanJianShenTongSpecials.IsYuYiXian(actor)
            || XjXuanJianShenTongSpecials.IsJieLinXian(actor)
            || XjShenDanAccessor.BuildState(actor).Found) return false;
        if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            || !string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return false;
        XjJinDanState state = XjJinDanAccessor.BuildState(actor);
        return state.Found
            && !string.IsNullOrWhiteSpace(state.JinXing)
            && !string.IsNullOrWhiteSpace(state.GuoWei)
            && state.SuccessYear > 0;
    }


    internal static bool TryResolveGrade(Actor actor, out int grade, out Kind kind)
    {
        grade = 0;
        kind = Kind.None;
        if (actor?.data == null || !actor.isAlive()) return false;

        // 仙国假金丹的真实 RealmId 仍是紫府/真人，必须先识别借玄位格。
        if (XjXianGuoSystem.TryGetBorrowedCombatGrade(actor, out int borrowedGrade))
        {
            grade = Math.Clamp(borrowedGrade, XianGuoMinimum, XianGuoMaximum);
            kind = Kind.XianGuoFalseJinDan;
            return true;
        }

        // 释修法相的“古/今”是决定高境质量的关键；世尊属于道胎级，不进入本模型。
        if (XjCultivationPathRules.IsShi(actor)
            && XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot shi)
            && string.Equals(shi.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal))
        {
            int stageIndex = ResolveDharmaFormStageIndex(shi.DharmaFormStage);
            if (string.Equals(shi.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
            {
                grade = Math.Clamp(AncientMinimum + stageIndex * 6, AncientMinimum, AncientMaximum);
                kind = Kind.AncientDharmaForm;
            }
            else
            {
                grade = Math.Clamp(ModernMinimum + stageIndex * 9, ModernMinimum, ModernMaximum);
                kind = Kind.ModernDharmaForm;
            }
            return true;
        }

        // 特殊仙身与神丹的底层 RealmId 可能仍呈现金丹级，必须先于“正统金丹”识别。
        if (XjXuanJianShenTongSpecials.IsYuYiXian(actor))
        {
            grade = YuYiGrade;
            kind = Kind.YuYiXian;
            return true;
        }
        if (XjXuanJianShenTongSpecials.IsJieLinXian(actor))
        {
            grade = JieLinGrade;
            kind = Kind.JieLinXian;
            return true;
        }
        if (XjShenDanAccessor.BuildState(actor).Found)
        {
            grade = ShenDanGrade;
            kind = Kind.ShenDan;
            return true;
        }

        if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
        {
            realmId = string.Empty;
        }
        // 真君羽士不纳入“正统金丹”定义，且完全保留RC11.8以前的数值区间。
        if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
        {
            int level = Math.Clamp(XjRealmSuppression.GetCombatLevel(actor), 24, 27);
            grade = ZhenJunMinimum + (level - 24) * 10;
            kind = Kind.ZhenJunYuShi;
            return true;
        }

        // 正统金丹必须是真实紫府金丹道证金：RealmId=JinDan，并且金性、
        // 果/余/闰位和证金纪年已经落入真实金丹状态。特殊仙身和神丹已在上面排除。
        if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
        {
            if (!IsOrthodoxJinDan(actor)) return false;

            XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanDaoXing, out int daoXing);
            if (daoXing <= 0) XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out daoXing);
            int stage = daoXing >= 6000 ? 3 : daoXing >= 3000 ? 2 : daoXing >= 1000 ? 1 : 0;
            grade = OrthodoxMinimum + stage * 10;
            kind = Kind.OrthodoxJinDan;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 同一金丹级内的伤害质量差。临近类别仍可通过属性、神通、克制逆转；
    /// 跨两三个品秩时则形成清晰高境压制，但不使用瞬杀或无条件归零。
    /// </summary>
    internal static float ResolveSameTierDamageMultiplier(int attackerGrade, int defenderGrade)
    {
        if (attackerGrade <= 0 || defenderGrade <= 0 || attackerGrade == defenderGrade) return 1f;
        int diff = Math.Clamp(attackerGrade - defenderGrade, -220, 220);
        double exponent = diff / 220d;
        return Mathf.Clamp((float)Math.Exp(exponent), 0.42f, 2.15f);
    }

    /// <summary>
    /// 高境控制与伤害使用同一品秩来源，避免今法相因为旧 CombatLevel=27
    /// 反过来比金丹初期更难控制。
    /// </summary>
    internal static float ResolveSameTierControlScale(int casterGrade, int targetGrade)
    {
        if (casterGrade <= 0 || targetGrade <= 0) return 1f;
        int diff = Math.Clamp(casterGrade - targetGrade, -220, 220);
        return Mathf.Clamp(1f + diff / 320f, 0.35f, 1.25f);
    }

    /// <summary>
    /// 排行榜只把品秩换算成“境界权重”，不覆盖攻击/生命本身。
    /// 因此同类角色仍会按真实数值区分，但大类顺序不会再被旧法相24~27映射颠倒。
    /// </summary>
    internal static int ResolveUnifiedRankWeight(Actor actor, int fallbackWeight)
    {
        if (!TryResolveGrade(actor, out int grade, out Kind kind)) return fallbackWeight;
        return kind switch
        {
            Kind.ModernDharmaForm => 5000 + Math.Clamp(grade - ModernMinimum, 0, ModernMaximum - ModernMinimum) * 20,
            Kind.XianGuoFalseJinDan => 5000 + Math.Clamp(grade - XianGuoMinimum, 0, XianGuoMaximum - XianGuoMinimum) * 20,
            Kind.ShenDan => 6200,
            Kind.JieLinXian => 7000,
            Kind.YuYiXian => 8000,
            Kind.AncientDharmaForm => 7800 + Math.Clamp(grade - AncientMinimum, 0, AncientMaximum - AncientMinimum) * 20,
            Kind.ZhenJunYuShi => ResolveZhenJunRankWeight(grade),
            Kind.OrthodoxJinDan => ResolveOrthodoxRankWeight(grade),
            _ => fallbackWeight,
        };
    }

    internal static string ResolveKindDisplay(Actor actor)
    {
        if (!TryResolveGrade(actor, out _, out Kind kind)) return string.Empty;
        return kind switch
        {
            Kind.ModernDharmaForm => "今法相",
            Kind.XianGuoFalseJinDan => "仙国假金丹",
            Kind.ShenDan => "神丹修士",
            Kind.JieLinXian => "结璘仙",
            Kind.YuYiXian => "郁仪仙",
            Kind.AncientDharmaForm => "古法相",
            Kind.ZhenJunYuShi => "真君羽士",
            Kind.OrthodoxJinDan => "正统金丹",
            _ => string.Empty,
        };
    }

    private static int ResolveDharmaFormStageIndex(string stage)
    {
        if (string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal)) return 1;
        if (string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal)) return 2;
        if (string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal)) return 3;
        return 0;
    }

    private static int ResolveZhenJunRankWeight(int grade)
    {
        // 保持RC11.8以前真君羽士的排行权重与阶段，不随正统金丹定义变化。
        if (grade >= 730) return 12960;
        if (grade >= 720) return 11700;
        if (grade >= 710) return 10400;
        return 9000;
    }

    private static int ResolveOrthodoxRankWeight(int grade)
    {
        if (grade >= 730) return 12960;
        if (grade >= 720) return 11700;
        if (grade >= 710) return 10400;
        return 9000;
    }
}
