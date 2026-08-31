using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 果位战斗系统
/// 语义：提供金丹果位/权柄的 O(1) 查询与权柄伤害加成。
/// 果位减伤、果位护盾和法宝护盾的实际乘区统一由 XjCombatDamageResolver 结算，
/// 本类不再保留第二套伤害处理入口。
/// 
/// 玄门化原则：
/// - O(1) 数据键/注册表查询
/// - 纯函数，无副作用
/// - 无外部持久化依赖
/// </summary>
internal static class XjGuoWeiCombatSystem
{
    // ====== 道途权柄伤害加成 ======
    // 每个权柄优势提供 +8% 伤害，上限 48%（6权柄全占）
    private const float QuanBingBonusPerPoint = 0.08f;
    private const float MaxQuanBingBonus = 0.48f;

    // ====== 果位融权护体 ======
    // 已完成融合的外道根权柄归入本道果位，而不是成为最初夺柄者的私产。
    // 当前真实持果者每承接一柄融权，额外获得8%全伤减免，最多50%。
    // 该层随果位承继，并在真假伤拆分前结算；果位再次被夺柄时，对应护体同步转移。
    private const float SeizedAuthorityDamageReductionPerPoint = 0.08f;
    private const float MaxSeizedAuthorityDamageReduction = 0.50f;

    // ====== 果位减伤 ======
    private const float GuoWeiDamageReduction = 0.25f;  // 果位：25% 减伤
    private const float RunWeiDamageReduction = 0.15f;    // 闰位：15% 减伤
    private const float YuWeiDamageReduction = 0.10f;     // 余位：10% 减伤

    // ====== 果位护盾 ======
    private const float GuoWeiShieldRatio = 0.30f;      // 果位：吸收 30% 最终伤害
    private const float RunWeiShieldRatio = 0.20f;        // 闰位：吸收 20% 最终伤害
    private const float YuWeiShieldRatio = 0.10f;         // 余位：吸收 10% 最终伤害

    // ====== 果位类型检测常量 ======
    private const string GuoWeiTag = XjGuoWeiCalculator.ZhengWei;
    private const string RunWeiTag = "闰位";
    private const string YuWeiTag = "余位";

    // ==================== 道途权柄伤害加成 ====================

    /// <summary>
    /// 应用道途权柄伤害加成
    /// 比较攻击者与防御者的活跃权柄数量，权柄优势方获得伤害加成
    /// 仅金丹对金丹有效
    /// </summary>
    internal static bool ApplyQuanBingDamageBonus(ref float damage, Actor attacker, Actor defender)
    {
        if (damage <= 0f || attacker == null || defender == null)
            return false;

		return ApplyQuanBingDamageBonus(
			ref damage,
			attacker,
			defender,
			XjRealmSuppression.GetRealmTier(attacker),
			XjRealmSuppression.GetRealmTier(defender));
	}

	internal static bool ApplyQuanBingDamageBonus(
		ref float damage,
		Actor attacker,
		Actor defender,
		int attackerTier,
		int defenderTier)
	{
		if (damage <= 0f || attacker == null || defender == null || attackerTier < 5 || defenderTier < 5)
			return false;

        int attackerQuanBing = GetActiveQuanBingCount(attacker);
        int defenderQuanBing = GetActiveQuanBingCount(defender);

        // 权柄数量仍是主乘区；果位真景只追加最多8%的小幅独立优势。
        int diff = attackerQuanBing - defenderQuanBing;
        float authorityBonus = diff > 0
            ? Mathf.Min(diff * QuanBingBonusPerPoint, MaxQuanBingBonus)
            : 0f;
        float imageBonus = XjGuoWeiImageStateService.ResolveCombatAdvantage(attacker, defender);
        float bonus = authorityBonus + imageBonus;
        if (bonus <= 0f) return false;
        damage *= (1f + bonus);
        return true;
    }

    // 果位减伤与护盾仅保留查询方法，避免与统一伤害解析器形成重复结算。

    // ==================== 查询方法 ====================

    /// <summary>
    /// 获取 Actor 活跃权柄数量（本道 + 夺取，不含外道未炼化和撤回洞天的）
    /// </summary>
    internal static int GetActiveQuanBingCount(Actor actor)
    {
        if (actor?.data == null)
            return 0;

        try
        {
            long actorId = ((BaseSystemData)actor.data).id;
            if (actorId <= 0L)
                return 0;

            if (XjHighRealmAggregateStore.TryGetAuthorityCount(actorId, out int cached))
                return cached;
            if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state))
                return 0;

            // 仅旧档首次命中或导入顺序异常时走一次冷路径回填；后续战斗只读整数缓存。
            XjHighRealmAggregateStore.ApplyAuthority(state);
            return XjHighRealmAggregateStore.TryGetAuthorityCount(actorId, out cached) ? cached : 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 获取已经完成融合的外道权柄护体减伤。只计算 SeizedQuanBing，
    /// 本道根权柄仍由果位本身的减伤/护盾负责，避免重复叠加。
    /// </summary>
    internal static float GetSeizedAuthorityDamageReduction(Actor actor)
    {
        if (actor?.data == null) return 0f;

        try
        {
            long actorId = ((BaseSystemData)actor.data).id;
            if (actorId <= 0L) return 0f;

            XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
            if (string.IsNullOrWhiteSpace(daoTu)
                || !XjDaoLineageStateRegistry.TryResolveActiveFruitHolder(daoTu, out long fruitHolderId, out _)
                || fruitHolderId != actorId)
            {
                return 0f;
            }

            if (!XjHighRealmAggregateStore.TryGetAuthoritySets(
                actorId, out IReadOnlyList<string> local, out IReadOnlyList<string> seized, out IReadOnlyList<string> foreign))
            {
                if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state)) return 0f;
                XjHighRealmAggregateStore.ApplyAuthority(state);
                if (!XjHighRealmAggregateStore.TryGetAuthoritySets(
                    actorId, out local, out seized, out foreign)) return 0f;
            }

            int count = seized?.Count ?? 0;
            return Mathf.Min(count * SeizedAuthorityDamageReductionPerPoint, MaxSeizedAuthorityDamageReduction);
        }
        catch (Exception xjCaughtAuthorityDefense)
        {
            XjExceptionDiagnostics.Report("XjGuoWeiCombatSystem.GetSeizedAuthorityDamageReduction", xjCaughtAuthorityDefense);
            return 0f;
        }
    }

    /// <summary>
    /// 获取果位减伤比例
    /// </summary>
    internal static float GetGuoWeiDamageReduction(Actor actor)
    {
        if (!TryGetEffectivePositionTypes(actor, out string primaryType, out string secondaryType))
            return 0f;

        float primary = ResolvePositionDamageReduction(primaryType);
        float secondary = ResolvePositionDamageReduction(secondaryType);
        // 双位均为真实位序，减伤按两个独立护体层顺次结算，避免简单相加把果+闰直接堆到40%。
        return 1f - (1f - primary) * (1f - secondary);
    }

    /// <summary>
    /// 获取果位护盾吸收比例
    /// </summary>
    internal static float GetGuoWeiShieldRatio(Actor actor)
    {
        if (!TryGetEffectivePositionTypes(actor, out string primaryType, out string secondaryType))
            return 0f;

        float primary = ResolvePositionShieldRatio(primaryType);
        float secondary = ResolvePositionShieldRatio(secondaryType);
        // 护盾同样按两层独立位序顺次折算；没有副位时 secondary=0，结果与旧逻辑完全一致。
        return 1f - (1f - primary) * (1f - secondary);
    }

    private static float ResolvePositionDamageReduction(string positionType)
    {
        return positionType switch
        {
            GuoWeiTag => GuoWeiDamageReduction,
            RunWeiTag => RunWeiDamageReduction,
            YuWeiTag => YuWeiDamageReduction,
            _ => 0f
        };
    }

    private static float ResolvePositionShieldRatio(string positionType)
    {
        return positionType switch
        {
            GuoWeiTag => GuoWeiShieldRatio,
            RunWeiTag => RunWeiShieldRatio,
            YuWeiTag => YuWeiShieldRatio,
            _ => 0f
        };
    }

    private static bool TryGetEffectivePositionTypes(Actor actor, out string primaryType, out string secondaryType)
    {
        primaryType = string.Empty;
        secondaryType = string.Empty;
        if (actor?.data == null) return false;

        XjJinDanState state = XjJinDanAccessor.BuildPositionCarrierState(actor);
        if (!state.Found || string.IsNullOrWhiteSpace(state.GuoWei)) return false;
        primaryType = XjGuoWeiRegistry.ResolveTypeFromName(state.GuoWei);

        if (!XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return !string.IsNullOrWhiteSpace(primaryType);
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L
            || !XjFruitPositionWorldState.TryGetDaoTaiBinding(actorId, out XjDaoTaiPositionBindingArchiveRecord binding)
            || binding == null
            || !XjFruitPositionWorldState.TryGetPosition(binding.SecondaryPositionId, out XjDerivedPositionArchiveRecord secondary)
            || secondary == null)
        {
            return !string.IsNullOrWhiteSpace(primaryType);
        }

        secondaryType = XjGuoWeiCalculator.NormalizePositionType(secondary.PositionType);
        return !string.IsNullOrWhiteSpace(primaryType);
    }

    /// <summary>
    /// 获取位置类型："果位" / "闰位" / "余位" / ""。
    /// 只接受当前仍有效的金丹/道胎位置承载状态，禁止转世待继承、旧档残留字段
    /// 直接获得果位战斗减伤与权柄效果。
    /// </summary>
    internal static string GetGuoWeiType(Actor actor)
    {
        if (actor?.data == null)
            return string.Empty;

        try
        {
            XjJinDanState state = XjJinDanAccessor.BuildPositionCarrierState(actor);
            if (!state.Found || string.IsNullOrWhiteSpace(state.GuoWei))
                return string.Empty;

            // 位置名称格式如 "太阴果位"、"坎水二余位"；旧档“正位”先规范化
            string guoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(state.GuoWei);
            if (guoWei.Contains(GuoWeiTag, StringComparison.Ordinal))
                return GuoWeiTag;
            if (guoWei.Contains(RunWeiTag, StringComparison.Ordinal))
                return RunWeiTag;
            if (guoWei.Contains(YuWeiTag, StringComparison.Ordinal))
                return YuWeiTag;
        }
        catch (System.Exception xjCaught181) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjGuoWeiCombatSystem.cs:181", xjCaught181); }

        return string.Empty;
    }

    /// <summary>
    /// 快速检测是否拥有果位（任何类型）
    /// </summary>
    internal static bool HasGuoWei(Actor actor)
    {
        return !string.IsNullOrEmpty(GetGuoWeiType(actor));
    }

    // ==================== 工具方法 ====================

}
