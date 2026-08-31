using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Death;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 真人/真君转世的“重证前护道”权威规则。
///
/// 这不是普通免死特质：保护只存在于前世高位尚未重证的这一段重修期，并且只
/// 拦截“可避免的非修炼死亡”。真正的突破失败、寿尽、阴司终局和 TechnicalRemoval
/// 必须放行；转世护道只负责避免前世高修在重证前被普通战斗/事故提前抹去。
///
/// ChuShen6：真人转世，重回真人（紫府/服气真人等效层）前保护。
/// ChuShen7：真君转世，重回真君（金丹/神丹/真君羽士等效层）前保护。
/// </summary>
internal static class XjReincarnationSurvivalGuard
{
    private const int RealPersonReincarnationRank = 6;
    private const int TrueLordReincarnationRank = 7;
    private const int MinimumProtectedHealth = 1;
    private const float RestoredHealthRatio = 0.40f;
    private const float InvincibleSeconds = 8f;

    internal static bool ShouldBlockDirectDeath(Actor actor, XjDeathCause cause)
    {
        if (!IsProtectionActive(actor)) return false;
        if (IsUnprotectedFinality(cause)) return false;

        RecoverFromBlockedDeath(actor);
        return true;
    }

    internal static void ClampIncomingDamage(Actor actor, ref float damage)
    {
        if (damage <= 0f || !IsProtectionActive(actor) || IsCultivationOrTechnicalFinality(actor)) return;
        float health = Math.Max(0f, XjSafeCore.GetHealthSafe(actor, actor.data.health));
        float maximumDamage = Math.Max(0f, health - MinimumProtectedHealth);
        if (damage > maximumDamage) damage = maximumDamage;
    }

    internal static int ResolveProtectedHealthDelta(Actor actor, int delta)
    {
        if (delta >= 0 || !IsProtectionActive(actor) || IsCultivationOrTechnicalFinality(actor)) return delta;
        int health = Math.Max(0, actor.data.health);
        int minimumDelta = MinimumProtectedHealth - health;
        return Math.Max(delta, minimumDelta);
    }

    internal static int ResolveProtectedSetHealth(Actor actor, int requestedHealth)
    {
        if (!IsProtectionActive(actor) || IsCultivationOrTechnicalFinality(actor)) return requestedHealth;
        return Math.Max(MinimumProtectedHealth, requestedHealth);
    }

    internal static bool ShouldBlockDirectDestroy(Actor actor)
    {
        // destroyObject 只救“仍活着却被原生/第三方旁路送进销毁队列”的重修者。
        // 已经寿尽、被阴司处决或因突破失败真正死亡的肉身，绝不能在强制死因令牌
        // 清理后再次被这道边界救回。
        return XjSafeCore.IsAliveActor(actor)
            && IsProtectionActive(actor)
            && !IsCultivationOrTechnicalFinality(actor);
    }

    internal static bool IsProtectionActive(Actor actor)
    {
        if (actor?.data == null) return false;
        if (!TryResolveTargetTier(actor, out int targetTier)) return false;
        return XjRealmSuppression.GetRealmTier(actor) < targetTier;
    }

    private static bool TryResolveTargetTier(Actor actor, out int targetTier)
    {
        targetTier = XjRealmSuppression.TierNone;
        if (actor?.data == null) return false;

        int rank = 0;
        if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ChuShenSpecial, out rank)
            || (rank != RealPersonReincarnationRank && rank != TrueLordReincarnationRank))
        {
            // 可见特质只是权威字段的容错投影；真正运行仍优先读取 ChuShenSpecial。
            try
            {
                if (actor.hasTrait("ChuShen6")) rank = RealPersonReincarnationRank;
                else if (actor.hasTrait("ChuShen7")) rank = TrueLordReincarnationRank;
            }
            catch
            {
                rank = 0;
            }
        }

        if (rank == RealPersonReincarnationRank)
        {
            targetTier = XjRealmSuppression.TierZiFu;
            return true;
        }
        if (rank == TrueLordReincarnationRank)
        {
            targetTier = XjRealmSuppression.TierJinDan;
            return true;
        }
        return false;
    }

    private static bool IsCultivationOrTechnicalFinality(Actor actor)
    {
        return XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.BreakthroughFailure)
            || XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.NaturalOldAge)
            || XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.YinSi)
            || XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.TechnicalRemoval);
    }

    private static bool IsUnprotectedFinality(XjDeathCause cause)
    {
        return cause == XjDeathCause.BreakthroughFailure
            || cause == XjDeathCause.NaturalOldAge
            || cause == XjDeathCause.YinSi
            || cause == XjDeathCause.TechnicalRemoval;
    }

    internal static void RecoverFromBlockedDeath(Actor actor)
    {
        if (actor?.data == null) return;
        float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, 0f);
        int current = Math.Max(MinimumProtectedHealth, actor.data.health);
        int restored = maxHealth > 0f
            ? Mathf.CeilToInt(Mathf.Max(MinimumProtectedHealth, maxHealth * RestoredHealthRatio))
            : current;
        try { actor.setHealth(Math.Max(current, restored)); }
        catch { actor.data.health = Math.Max(current, restored); }
        XjActorAggroBridge.ClearTargets(actor);
        try { ((BaseSimObject)actor).addStatusEffect("invincible", InvincibleSeconds, true); }
        catch (Exception ex) { XjExceptionDiagnostics.Report("ReincarnationSurvival.AddInvincible", ex); }
        try { actor.setStatsDirty(); }
        catch (Exception ex) { XjExceptionDiagnostics.Report("ReincarnationSurvival.SetStatsDirty", ex); }
    }
}
