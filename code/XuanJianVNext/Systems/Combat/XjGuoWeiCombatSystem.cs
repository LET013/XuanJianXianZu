using System;
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

    // ====== 果位减伤 ======
    private const float ZhengWeiDamageReduction = 0.25f;  // 正位：25% 减伤
    private const float RunWeiDamageReduction = 0.15f;    // 闰位：15% 减伤
    private const float YuWeiDamageReduction = 0.10f;     // 余位：10% 减伤

    // ====== 果位护盾 ======
    private const float ZhengWeiShieldRatio = 0.30f;      // 正位：吸收 30% 最终伤害
    private const float RunWeiShieldRatio = 0.20f;        // 闰位：吸收 20% 最终伤害
    private const float YuWeiShieldRatio = 0.10f;         // 余位：吸收 10% 最终伤害

    // ====== 果位类型检测常量 ======
    private const string ZhengWeiTag = "正位";
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
        if (attackerQuanBing <= 0)
            return false;

        int defenderQuanBing = GetActiveQuanBingCount(defender);

        // 权柄差值：攻击者权柄数 - 防御者权柄数
        int diff = attackerQuanBing - defenderQuanBing;
        if (diff <= 0)
            return false;

        // 每个权柄差值 +8% 伤害，上限 48%
        float bonus = Mathf.Min(diff * QuanBingBonusPerPoint, MaxQuanBingBonus);
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

            if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state))
                return 0;

            int count = 0;
            count += CountQuanBingEntries(state.LocalQuanBing);
            count += CountQuanBingEntries(state.SeizedQuanBing);
            // ForeignQuanBing 和 WithdrawnToDongTian 不参与战斗

            return count;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 获取果位减伤比例
    /// </summary>
    internal static float GetGuoWeiDamageReduction(Actor actor)
    {
        string guoWeiType = GetGuoWeiType(actor);
        if (string.IsNullOrEmpty(guoWeiType))
            return 0f;

        return guoWeiType switch
        {
            ZhengWeiTag => ZhengWeiDamageReduction,
            RunWeiTag => RunWeiDamageReduction,
            YuWeiTag => YuWeiDamageReduction,
            _ => 0f
        };
    }

    /// <summary>
    /// 获取果位护盾吸收比例
    /// </summary>
    internal static float GetGuoWeiShieldRatio(Actor actor)
    {
        string guoWeiType = GetGuoWeiType(actor);
        if (string.IsNullOrEmpty(guoWeiType))
            return 0f;

        return guoWeiType switch
        {
            ZhengWeiTag => ZhengWeiShieldRatio,
            RunWeiTag => RunWeiShieldRatio,
            YuWeiTag => YuWeiShieldRatio,
            _ => 0f
        };
    }

    /// <summary>
    /// 获取果位类型："正位" / "闰位" / "余位" / ""
    /// 通过 XjJinDanGuoWei 数据键读取果位名称后判断
    /// </summary>
    internal static string GetGuoWeiType(Actor actor)
    {
        if (actor?.data == null)
            return string.Empty;

        try
        {
            if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei)
                || string.IsNullOrWhiteSpace(guoWei))
                return string.Empty;

            // 果位名称格式如 "太阴正位"、"坎水二余位"
            if (guoWei.Contains(ZhengWeiTag, StringComparison.Ordinal))
                return ZhengWeiTag;
            if (guoWei.Contains(RunWeiTag, StringComparison.Ordinal))
                return RunWeiTag;
            if (guoWei.Contains(YuWeiTag, StringComparison.Ordinal))
                return YuWeiTag;
        }
        catch { }

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

    /// <summary>
    /// 计数权柄条目（逗号分隔列表中的非空条目数）
    /// </summary>
    private static int CountQuanBingEntries(string quanBingList)
    {
        if (string.IsNullOrWhiteSpace(quanBingList))
            return 0;

        string[] entries = quanBingList.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
        int count = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(entries[i]))
                count++;
        }
        return count;
    }
}
