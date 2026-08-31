using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 今释高位被真实高境击杀时的真灵斩灭判定。
/// 只在死亡事务中运行一次，不进入战斗热路径。
///
/// 摩诃战斗位格等同紫府。此前普通战斗死亡只要没有脚本锁灵就必然归返，
/// 导致正统金丹/真君羽士斩杀摩诃也无法真正终结其真灵。现在按完整大境界差
/// 增加斩灭概率，并以统一金丹品秩细分一境优势时的斩灵能力。
/// </summary>
internal static class XjShiTrueSpiritAnnihilationPolicy
{
    internal const int OneTierFallbackChancePerTenThousand = 5500;
    internal const int TwoOrMoreTierChancePerTenThousand = 9500;

    internal static bool ShouldAnnihilateMoHe(
        in XjDeathSnapshot snapshot,
        in XjShiSnapshot shi,
        XuanJianVNext.Systems.Death.XjDeathCause cause,
        int year,
        out int chancePerTenThousand)
    {
        chancePerTenThousand = 0;
        if (cause != XuanJianVNext.Systems.Death.XjDeathCause.Combat
            || !string.Equals(shi.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
            || !string.Equals(shi.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
            || snapshot.LastAttackerId <= 0L)
        {
            return false;
        }

        int victimTier = XjRealmSuppression.TierZiFu;
        int attackerTier = snapshot.LastAttackerTier;
        if (attackerTier <= victimTier)
        {
            return false;
        }

        int tierGap = attackerTier - victimTier;
        chancePerTenThousand = tierGap >= 2
            ? TwoOrMoreTierChancePerTenThousand
            : ResolveOneTierChance(snapshot.LastAttackerId);
        chancePerTenThousand = Math.Clamp(chancePerTenThousand, 0, 10000);
        if (chancePerTenThousand <= 0) return false;

        unchecked
        {
            long seed = snapshot.ActorId
                + snapshot.LastAttackerId * 37L
                + Math.Max(1, year) * 1009L;
            int roll = XjDeterministicHash.PositiveIndex(
                seed,
                "shi_mohe_true_spirit_annihilation_v1",
                10000);
            return roll < chancePerTenThousand;
        }
    }

    private static int ResolveOneTierChance(long attackerId)
    {
        if (attackerId <= 0L
            || !XjActorRegistry.ResolveKnownOrWorld(attackerId, out Actor attacker)
            || attacker?.data == null
            || !attacker.isAlive())
        {
            return OneTierFallbackChancePerTenThousand;
        }

        if (!XjHighRealmCombatGrade.TryResolveGrade(attacker, out _, out XjHighRealmCombatGrade.Kind kind))
        {
            return OneTierFallbackChancePerTenThousand;
        }

        return kind switch
        {
            // 真正证金与真君羽士最擅长以完整高境位格压碎摩诃真灵。
            XjHighRealmCombatGrade.Kind.OrthodoxJinDan => 7000,
            XjHighRealmCombatGrade.Kind.ZhenJunYuShi => 7000,

            // 古法相与郁仪接近正统高位，但仍略逊完整真金/真君。
            XjHighRealmCombatGrade.Kind.AncientDharmaForm => 6200,
            XjHighRealmCombatGrade.Kind.YuYiXian => 6200,
            XjHighRealmCombatGrade.Kind.JieLinXian => 5800,
            XjHighRealmCombatGrade.Kind.ShenDan => 5200,

            // 他玄金丹级战力虽跨一大境界，斩灵稳定性最低。
            XjHighRealmCombatGrade.Kind.XianGuoFalseJinDan => 4500,
            XjHighRealmCombatGrade.Kind.ModernDharmaForm => 4500,
            _ => OneTierFallbackChancePerTenThousand,
        };
    }
}
