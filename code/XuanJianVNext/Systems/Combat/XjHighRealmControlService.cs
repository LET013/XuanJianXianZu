using System;
using UnityEngine;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 玄鉴高境控制的唯一入口。
///
/// 原生 stun/root/wait 仍由 XjStunGuard / XjKnockbackGuard 处理；只有明确从
/// 紫府同级及以上神通、领域发起的控制才进入本服务，并按双方真实大境界与
/// CombatLevel 重新裁定持续时间。随后在极短的同步作用域内绕过“原生控制免疫”
/// Harmony Guard，避免高境神通被当成普通眩晕/击退吞掉。
///
/// 绝对位格原则保持：低一大境界不能控制高一大境界；高一大境界对低境不受
/// 普通控制抗性削弱；同大境界才由小境界差与本境韧性缩短控制时间。
/// </summary>
internal static class XjHighRealmControlService
{
    [ThreadStatic]
    private static int _nativeGuardBypassDepth;

    internal static bool NativeGuardBypassActive => _nativeGuardBypassDepth > 0;

    internal static bool IsControlStatus(string statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId)) return false;
        return string.Equals(statusId, "root", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusId, "frozen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusId, "wait", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusId, "stun", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusId, "stunned", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusId, "paralysis", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool CanUseAttributedHighRealmControl(Actor caster)
    {
        return caster?.data != null
            && XjRealmSuppression.GetRealmTier(caster) >= XjRealmSuppression.TierZiFu;
    }

    internal static float ResolveDuration(Actor caster, Actor target, float requestedSeconds)
    {
        float requested = Mathf.Max(0f, requestedSeconds);
        if (requested <= 0f || caster?.data == null || target?.data == null) return 0f;

        int casterTier = XjRealmSuppression.GetRealmTier(caster);
        int targetTier = XjRealmSuppression.GetRealmTier(target);
        if (casterTier < XjRealmSuppression.TierZiFu) return requested;

        // 与伤害热路径共用已物化的高境品秩/借玄位格；控制结算不再重复构建
        // 释修快照、神丹状态或仙国档案。
        XjCombatHotProfile casterProfile = XjCombatHotPathCache.Get(caster);
        XjCombatHotProfile targetProfile = XjCombatHotPathCache.Get(target);
        if (casterProfile.BorrowedCombatTier > casterTier) casterTier = casterProfile.BorrowedCombatTier;
        if (targetProfile.BorrowedCombatTier > targetTier) targetTier = targetProfile.BorrowedCombatTier;

        // 与伤害位格保持同一原则：低一大境界不能靠控制绕过绝对压制。
        if (targetTier > casterTier) return 0f;
        if (targetTier < casterTier) return requested;

        float levelScale = 1f;
        if (casterTier == XjRealmSuppression.TierJinDan
            && casterProfile.HighRealmGrade > 0
            && targetProfile.HighRealmGrade > 0)
        {
            levelScale = XjHighRealmCombatGrade.ResolveSameTierControlScale(
                casterProfile.HighRealmGrade, targetProfile.HighRealmGrade);
        }
        else
        {
            int casterLevel = XjRealmSuppression.GetCombatLevel(caster);
            int targetLevel = XjRealmSuppression.GetCombatLevel(target);
            if (casterLevel > 0 && targetLevel > 0)
            {
                levelScale = Mathf.Clamp(1f + (casterLevel - targetLevel) * 0.08f, 0.45f, 1.15f);
            }
        }

        // 高境并非对“同位格神通”完全免疫，只表现为位格越高越难被长时间镇住。
        float sameTierResilience = targetTier switch
        {
            XjRealmSuppression.TierDaoTai => 0.65f,
            XjRealmSuppression.TierJinDan => 0.75f,
            XjRealmSuppression.TierZiFu => 0.85f,
            _ => 1f
        };
        return Mathf.Max(0f, requested * levelScale * sameTierResilience);
    }

    internal static bool TryApplyStatus(
        Actor caster,
        Actor target,
        string statusId,
        float requestedSeconds,
        out string reason)
    {
        reason = string.Empty;
        if (caster?.data == null || target?.data == null || !IsControlStatus(statusId))
        {
            reason = "invalid_high_realm_control_status";
            return false;
        }

        float duration = ResolveDuration(caster, target, requestedSeconds);
        if (duration <= 0.05f)
        {
            reason = "suppressed_by_higher_realm";
            return false;
        }

        // 太阴/水德的 frozen 不能再只依赖 WorldBox 原生状态。部分版本虽然允许
        // addStatusEffect("frozen")，但 Actor 下一帧仍会重新决策、寻路或出手。
        // 先写入归因冻结表，再把原生状态作为兼容/视觉层；这样低境压高境的
        // 位格门禁仍由 ResolveDuration 唯一裁定，而行为锁不会被原生 AI 覆盖。
        bool attributedFreeze = string.Equals(statusId, "frozen", StringComparison.OrdinalIgnoreCase);
        bool behaviorFreezeApplied = attributedFreeze && XjAttributedFreezeRegistry.Apply(target, duration);

        try
        {
            EnterNativeGuardBypass();
            bool added = ((BaseSimObject)target).addStatusEffect(statusId, duration, true);
            if (added || behaviorFreezeApplied) return true;

            // 非 frozen 控制仍保留旧版降级语义。frozen 若原生状态不可写，行为锁登记
            // 已经完成，不需要每帧 makeWait 伪装成冻结。
            target.stopMovement();
            target.makeWait(Mathf.Max(0.15f, duration));
            return true;
        }
        catch (Exception ex)
        {
            if (behaviorFreezeApplied)
            {
                // 原生 frozen 写入失败不应撤销已经通过位格裁定的玄鉴冻结。
                return true;
            }

            reason = "high_realm_control_status_failed:" + ex.GetType().Name;
            return false;
        }
        finally
        {
            ExitNativeGuardBypass();
        }
    }

    internal static bool TryApplyHold(
        Actor caster,
        Actor target,
        float requestedSeconds,
        bool clearTarget,
        out string reason)
    {
        reason = string.Empty;
        if (caster?.data == null || target?.data == null || requestedSeconds <= 0f)
        {
            reason = "invalid_high_realm_hold";
            return false;
        }

        float duration = ResolveDuration(caster, target, requestedSeconds);
        if (duration <= 0.05f)
        {
            reason = "suppressed_by_higher_realm";
            return false;
        }

        try
        {
            if (clearTarget) XjActorAggroBridge.ClearTargets(target);
            EnterNativeGuardBypass();
            target.stopMovement();
            target.makeWait(Mathf.Max(0.15f, duration));
            return true;
        }
        catch (Exception ex)
        {
            reason = "high_realm_hold_failed:" + ex.GetType().Name;
            return false;
        }
        finally
        {
            ExitNativeGuardBypass();
        }
    }

    private static void EnterNativeGuardBypass()
    {
        _nativeGuardBypassDepth++;
    }

    private static void ExitNativeGuardBypass()
    {
        if (_nativeGuardBypassDepth > 0) _nativeGuardBypassDepth--;
    }
}
