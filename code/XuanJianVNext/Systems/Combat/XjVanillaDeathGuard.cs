using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 玄鉴死亡与环境伤害保护。
///
/// 规则：
/// - 自然寿尽使用独立、可追踪的强制死亡令牌；
/// - 明确环境伤害只限制单次峰值，不设置不可突破的生命下限；
/// - 角色战斗、脚本技能与持续环境伤害最终都可以正常致死。
///
/// 运行时只记录强制死亡令牌，不写入额外战斗状态。
/// </summary>
internal static class XjVanillaDeathGuard
{
    private const string XjFuQiZhenJunYuShi = "XjRealm13";

    // 永生相关特质标记
    private const string HiddenImmortalityKey = "xuanjian.vnext.hidden_immortality";
    private const float NaturalDeathSigmoidSteepness = 100f;
    private const float NaturalDeathSigmoidMidpoint = 0.03f;
    private const float NaturalDeathMaxProbability = 1f;
    // 寿元是标准寿限而不是所有角色共享的同一死亡年份。旧实现把自然死亡概率
    // 与硬上限都压在寿元后的极短区间，同龄紫府会在数年内被成批清空。现在按
    // 角色ID稳定生成0%—10%的起算偏移，再给予3%—6%的个人寿尽窗口。
    private const float NaturalDeathPersonalOnsetSpreadRatio = 0.10f;
    private const float NaturalDeathHardOverageBaseRatio = 0.03f;
    private const float NaturalDeathHardOverageSpreadRatio = 0.03f;
    private const float NaturalDeathHardOverageMinimumYears = 12f;
    private const float LongShuHardOverageYears = 10f;
    private const int NaturalDeathRealmPromotionGraceYears = 20;
    private const int NaturalDeathAttackType = 11;
    private static readonly HashSet<long> ForceDeathActorIds = new HashSet<long>();
    private static readonly object ForceDeathSync = new object();


    /// <summary>
    /// 标记 Actor 拥有隐藏永生保护（年迈金丹等场景使用）
    /// </summary>
    internal static void MarkHiddenImmortality(Actor actor, bool enabled)
    {
        if (actor?.data == null) return;
        try
        {
            XjActorStateWriteGateway.SetExternalBool(
                actor,
                HiddenImmortalityKey,
                enabled,
                XjActorStateDomain.HighRealm | XjActorStateDomain.Progression);
			XjCombatHotPathCache.Refresh(actor);
        }
        catch (System.Exception xjCaught61) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjVanillaDeathGuard.cs:61", xjCaught61); }
    }

    /// <summary>
    /// 判断 Actor 是否需要防暴毙保护
    /// 条件：金丹修士 OR 被标记为隐藏永生
    /// </summary>
    internal static bool HasImmortalityProtection(Actor actor)
    {
		return HasImmortalityProtection(actor, XjRealmSuppression.GetRealmTier(actor));
	}

	internal static bool HasImmortalityProtection(Actor actor, int realmTier)
	{
        if (actor?.data == null) return false;

        try
        {
            // 龙属数量稀少且需要跨越数百年洞天周期；仅防止环境/代码暴毙，
            // Actor 对 Actor 的合法战斗死亡仍由伤害结算链处理。
            if (XjLongShuSystem.IsLongShu(actor))
                return true;

            // 金丹境界 → 需要保护
            if (realmTier >= XjRealmSuppression.TierJinDan)
                return true;

            // 检查隐藏永生标记
            BaseSystemData data = (BaseSystemData)actor.data;
            data.get(HiddenImmortalityKey, out bool marked, false);
            return marked;
        }
        catch { return false; }
    }

    // 死亡入口不再按 AttackType 猜测来源并拦截。环境保护只允许在 getHit
    // 的明确伤害数值阶段执行，防止合法战斗死亡被误判为环境暴毙。

    internal static void Clear()
    {
        lock (ForceDeathSync)
        {
            ForceDeathActorIds.Clear();
        }
    }

    internal static bool IsForceDeathActor(Actor actor)
    {
        if (actor?.data == null)
        {
            return false;
        }

        try
        {
            lock (ForceDeathSync)
            {
                return ForceDeathActorIds.Contains(((BaseSystemData)actor.data).id);
            }
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryExecuteForceDeath(Actor actor, AttackType attackType, bool countDeath)
    {
        return TryExecuteForceDeath(actor, attackType, countDeath, XjDeathCause.ScriptedFinality);
    }

    internal static bool TryExecuteForceDeath(Actor actor, AttackType attackType, bool countDeath, XjDeathCause cause)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return false;
        }

        long actorId;
        try
        {
            actorId = ((BaseSystemData)actor.data).id;
        }
        catch
        {
            return false;
        }

        if (actorId <= 0L)
        {
            return false;
        }

        lock (ForceDeathSync)
        {
            ForceDeathActorIds.Add(actorId);
        }
        XjDeathArbitrationPipeline.PushForcedCause(actorId, cause);
        try
        {
            actor.die(countDeath, attackType, true, true);
            return !XjSafeCore.IsAliveActor(actor);
        }
        catch
        {
            try
            {
                actor.dieAndDestroy(attackType);
                return !XjSafeCore.IsAliveActor(actor);
            }
            catch
            {
                return false;
            }
        }
        finally
        {
            lock (ForceDeathSync)
            {
                ForceDeathActorIds.Remove(actorId);
            }
            XjDeathArbitrationPipeline.PopForcedCause(actorId);
        }
    }

    internal static bool ShouldBlockDirectStarvationDeath(Actor actor, AttackType attackType)
    {
        if (attackType != AttackType.Starvation
            || !XjSafeCore.IsAliveActor(actor)
            || IsForceDeathActor(actor))
        {
            return false;
        }

        int realmTier = XjRealmSuppression.GetRealmTier(actor);
		if (realmTier <= XjRealmSuppression.TierNone
			&& !XuanJianVNext.Systems.Cultivation.XjYinSiTraitLifecycle.IsYinSi(actor)
			&& !XjShenDanAccessor.BuildState(actor).Found)
        {
            return false;
        }

        float maxHp = XjSafeCore.GetMaxHealthSafe(actor);
        if (maxHp > 0f)
        {
            actor.setHealth(Mathf.CeilToInt(Mathf.Max(1f, maxHp * 0.15f)));
        }
        return true;
    }

    internal static bool ShouldBlockStarvationDamage(ref float damage, Actor actor, AttackType attackType)
    {
        if (attackType != AttackType.Starvation
            || damage <= 0f
            || !XjSafeCore.IsAliveActor(actor)
            || IsForceDeathActor(actor))
        {
            return false;
        }

        int realmTier = XjRealmSuppression.GetRealmTier(actor);
		if (realmTier <= XjRealmSuppression.TierNone
			&& !XuanJianVNext.Systems.Cultivation.XjYinSiTraitLifecycle.IsYinSi(actor)
			&& !XjShenDanAccessor.BuildState(actor).Found)
        {
            return false;
        }

        float maxHp = XjSafeCore.GetMaxHealthSafe(actor);
        float currentHp = XjSafeCore.GetHealthSafe(actor, 0f);
        if (maxHp > 0f && currentHp < maxHp * 0.15f)
        {
            actor.setHealth(Mathf.CeilToInt(Mathf.Max(1f, maxHp * 0.15f)));
        }

        damage = 0f;
        return true;
    }

    internal static bool ShouldBlockDirectFireDeath(Actor actor, AttackType attackType)
    {
        if (attackType != AttackType.Fire
            || !XjSafeCore.IsAliveActor(actor)
            || IsForceDeathActor(actor))
        {
            return false;
        }

        return IsXuanJianCultivator(actor, XjRealmSuppression.GetRealmTier(actor));
    }

    internal static bool ShouldBlockFireDamage(ref float damage, Actor actor, AttackType attackType, int realmTier)
    {
        if (attackType != AttackType.Fire
            || damage <= 0f
            || !XjSafeCore.IsAliveActor(actor)
            || IsForceDeathActor(actor)
            || !IsXuanJianCultivator(actor, realmTier))
        {
            return false;
        }

        // 修士仍承受火焰伤害，但火焰本身不能成为最终死因。
        // 将本次致死火伤限制到至少保留 1 点生命；已在 1 点生命时屏蔽后续火伤。
        float currentHp = XjSafeCore.GetHealthSafe(actor, 0f);
        if (currentHp <= 1f)
        {
            damage = 0f;
            return true;
        }

        if (currentHp - damage < 1f)
        {
            damage = Math.Max(0f, currentHp - 1f);
        }

        // 返回 true 只表示本次火伤已被完全屏蔽；非致死或被截断后的火伤继续走原生扣血。
        return damage <= 0f;
    }

    internal static bool TryHandleNaturalDeath(Actor actor, ref bool result)
    {
        return XjDeathArbitrationPipeline.TryHandleNaturalDeath(actor, ref result);
    }

    internal static bool TryHandleNaturalDeathCore(Actor actor, ref bool result)
    {
        result = false;
        if (actor?.data == null) return false;

        // checkNaturalDeath is a native annual callback for the whole civilized
        // population. After bootstrap, a non-indexed civ is guaranteed to be a
        // mortal civilian: resolve its fixed 80-year rule before immortality,
        // LongShu, Shi, realm-marker and JinDan-registry lookups.
        long fastActorId = ((BaseSystemData)actor.data).id;
        if (fastActorId > 0L
            && !XjWorldBootstrapLane.HasPending
            && !XjRuntimeActorInterestIndex.HasStatInterest(fastActorId)
            // 晋升提交与运行时索引刷新可能相差一个回调；只要仍有落盘修炼标记，
            // 就必须退出“80岁凡人快车道”，继续走真实境界/果位免死判断。
            && !XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(actor))
        {
            bool civilized = false;
            try { civilized = actor.asset != null && actor.asset.civ; } catch { }
            if (civilized)
            {
                if (!actor.isAlive()) return false;
                if (WorldLawLibrary.world_law_old_age == null || !WorldLawLibrary.world_law_old_age.isEnabled())
                    return false;
                if (Mathf.Max(0f, actor.getAge()) < XjRealmLifespanService.MortalCivilianLifespanYears)
                    return false;
                result = ExecuteNaturalDeath(actor);
                return false;
            }
        }

        if (!XjSafeCore.IsAliveActor(actor))
        {
            return false;
        }

        bool naturalDeathExempt = XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor);
        if (XjLongShuSystem.IsLongShu(actor))
        {
            // 龙属紫府仍遵守八百年寿限；金丹系高境则统一服从果位/神丹挂靠
            // 的免死判定。余位或失去活锚的神丹不再被“大境界”无条件放行，
            // 而是继续使用当前玄鉴寿元进入正常寿尽。
            if (naturalDeathExempt) return false;
            if (!HasGoldenRealmIdentity(actor))
            {
                return TryHandleLongShuNaturalDeath(actor, ref result);
            }
        }

        if (naturalDeathExempt)
        {
            return false;
        }

        // 凡俗智慧文明角色不再交给原生种族寿命判定。无论人族、精灵、
        // 矮人或兽人，均以八十年为唯一寿限；原生长寿/永生特质不能越过。
        if (XjRealmLifespanService.IsMortalCivilian(actor))
        {
            return TryHandleMortalCivilianNaturalDeath(actor, ref result);
        }

        if (!IsXuanJianManagedActor(actor))
        {
            return true;
        }

        if (WorldLawLibrary.world_law_old_age == null || !WorldLawLibrary.world_law_old_age.isEnabled())
        {
            return false;
        }

        float currentAge = Mathf.Max(0f, actor.getAge());
        float maxLifespan = GetActorLifespan(actor);
        if (maxLifespan <= 0f || currentAge <= maxLifespan)
        {
            return false;
        }

        // 刚刚真实晋升为紫府/真人及以上的角色，先完成境界寿元刷新并保留
        // 二十年稳定期。否则高龄筑基在晋升同年会继续沿用旧寿元直接被清算。
        if (IsWithinHighRealmPromotionGrace(actor))
        {
            return false;
        }

        // 正常寿尽继续使用逐年概率；最终硬上限改为角色稳定错峰值，
        // 禁止同寿元、同世代修士在“寿元+10年”同时死亡。
        float personalOnset = ResolvePersonalNaturalDeathOnsetYears(actor, maxLifespan);
        float hardOverage = ResolvePersonalHardOverageYears(actor, maxLifespan);
        if (currentAge >= maxLifespan + personalOnset + hardOverage)
        {
            result = ExecuteNaturalDeath(actor);
            return false;
        }

        // 每名角色拥有稳定且不同的寿尽起算偏移。面板寿元仍是境界的标准寿限，
        // 但同龄同境修士不会在达到同一个数字后的数年内集体死亡。
        float probability = CalculateNaturalDeathProbability(currentAge - personalOnset, maxLifespan);
        if (probability <= 0f || !Randy.randomChance(probability))
        {
            return false;
        }

        result = ExecuteNaturalDeath(actor);
        return false;
    }

    private static bool TryHandleMortalCivilianNaturalDeath(Actor actor, ref bool result)
    {
        if (WorldLawLibrary.world_law_old_age == null || !WorldLawLibrary.world_law_old_age.isEnabled())
        {
            return false;
        }

        float currentAge = Mathf.Max(0f, actor.getAge());
        if (currentAge < XjRealmLifespanService.MortalCivilianLifespanYears)
        {
            return false;
        }

        result = ExecuteNaturalDeath(actor);
        return false;
    }

    /// <summary>
    /// 原生 immortal / long_liver 等特质可能令 checkNaturalDeath 根本不被调用。
    /// 因此在 Actor.updateAge 后增加一次 O(1) 年度硬上限，确保凡俗文明角色
    /// 到八十岁即寿尽。关闭“老死”世界法则时仍尊重玩家设置，不执行强制死亡。
    /// </summary>
    internal static bool EnforceMortalCivilianLifespanLimit(Actor actor)
    {
        return XjDeathArbitrationPipeline.EnforceMortalCivilianLifespanLimit(actor);
    }

    internal static bool EnforceMortalCivilianLifespanLimitCore(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor)
            || WorldLawLibrary.world_law_old_age == null
            || !WorldLawLibrary.world_law_old_age.isEnabled())
        {
            return false;
        }

        // Age is the cheapest and most selective gate. Previously every annual
        // update for every 0-79 year old civilian first resolved civilization,
        // LongShu/Shi identity and cultivation cache state even though death was
        // impossible. On 3k-4k unit worlds this multiplied directly with years/sec.
        float currentAge = Mathf.Max(0f, actor.getAge());
        if (currentAge < XjRealmLifespanService.MortalCivilianLifespanYears)
        {
            return false;
        }

        return XjRealmLifespanService.IsMortalCivilian(actor)
            && ExecuteNaturalDeath(actor);
    }

    /// <summary>
    /// Annual deterministic backstop. Native code can skip checkNaturalDeath for an
    /// actor carrying the vanilla immortal trait; XuanJian cultivators must still
    /// obey the lifespan displayed by their effective stats.
    /// </summary>
    internal static bool EnforceHardLifespanLimit(Actor actor)
    {
        return XjDeathArbitrationPipeline.EnforceHardLifespanLimit(actor);
    }

    internal static bool EnforceHardLifespanLimitCore(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor)
            || WorldLawLibrary.world_law_old_age == null
            || !WorldLawLibrary.world_law_old_age.isEnabled())
        {
            return false;
        }

        bool naturalDeathExempt = XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor);
        if (XjLongShuSystem.IsLongShu(actor))
        {
            if (naturalDeathExempt) return false;
            if (!HasGoldenRealmIdentity(actor))
            {
                return EnforceLongShuHardLifespanLimit(actor);
            }
        }

        if (naturalDeathExempt
            || !IsXuanJianManagedActor(actor))
        {
            return false;
        }

        float currentAge = Mathf.Max(0f, actor.getAge());
        float maxLifespan = GetActorLifespan(actor);
        if (maxLifespan <= 0f || currentAge <= maxLifespan
            || IsWithinHighRealmPromotionGrace(actor))
        {
            return false;
        }

        float personalOnset = ResolvePersonalNaturalDeathOnsetYears(actor, maxLifespan);
        float hardOverage = ResolvePersonalHardOverageYears(actor, maxLifespan);
        return currentAge >= maxLifespan + personalOnset + hardOverage && ExecuteNaturalDeath(actor);
    }

    private static bool IsXuanJianManagedActor(Actor actor)
    {
        return actor?.data != null
            && XjRealmSuppression.GetRealmTier(actor) > XjRealmSuppression.TierNone;
    }

    private static bool HasGoldenRealmIdentity(Actor actor)
    {
        if (actor?.data == null)
        {
            return false;
        }

        int realmTier = XjRealmSuppression.GetRealmTier(actor);
        return realmTier >= XjRealmSuppression.TierJinDan
            || HasTrait(actor, XjFuQiZhenJunYuShi)
            || XjJinDanAccessor.BuildState(actor).Found
            || XjShenDanAccessor.BuildState(actor).Found;
    }

    private static bool HasTrait(Actor actor, string traitId)
    {
        try
        {
            return actor?.data != null
                && !string.IsNullOrWhiteSpace(traitId)
                && actor.hasTrait(traitId);
        }
        catch
        {
            return false;
        }
    }

    internal static void MarkRealmPromotionGrace(Actor actor)
    {
        if (actor?.data == null
            || XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierZiFu) return;

        int logicalYear = Math.Max(0, XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
        int worldYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
        int graceBaseYear = Math.Max(logicalYear, worldYear);
        if (graceBaseYear > 0)
        {
            XjActorAccessor.SetInt(
                actor,
                XjActorDataKeys.RealmNaturalDeathGraceUntilYear,
                graceBaseYear + NaturalDeathRealmPromotionGraceYears);
        }
    }

    internal static void ClearRealmPromotionGrace(Actor actor)
    {
        if (actor?.data == null) return;
        XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmNaturalDeathGraceUntilYear, 0);
    }

    /// <summary>
    /// 原生或第三方若绕过 checkNaturalDeath 直接 actor.die(Age)，仍需服从玄鉴的
    /// 果位免寿尽与高境晋升稳定期。玄鉴自身 TryExecuteForceDeath 带强制令牌，
    /// 不会被这里反向拦截。
    /// </summary>
    internal static bool ShouldBlockDirectNaturalOldAgeDeath(Actor actor, AttackType attackType)
    {
        if (attackType != AttackType.Age
            || !XjSafeCore.IsAliveActor(actor)
            || IsForceDeathActor(actor))
        {
            return false;
        }

        return XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor)
            || IsWithinHighRealmPromotionGrace(actor);
    }

    private static bool IsWithinHighRealmPromotionGrace(Actor actor)
    {
        if (actor?.data == null
            || XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierZiFu)
        {
            return false;
        }

        // 实际世界年保护处理“旧年度债务中刚刚完成的晋升”：逻辑晋升年份可能
        // 很早，但角色是在当前真实帧才完成境界提交，不能下一次换年便被老死。
        int worldYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmNaturalDeathGraceUntilYear, out int graceUntilYear)
            && graceUntilYear > 0
            && worldYear > 0
            && worldYear < graceUntilYear)
        {
            return true;
        }

        // 年度积压内部仍以正在处理的逻辑年份判断，防止连续补算时保护期失效。
        if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int enteredYear)
            || enteredYear <= 0)
        {
            return false;
        }
        int currentYear = Math.Max(0, XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
        return currentYear >= enteredYear
            && currentYear - enteredYear < NaturalDeathRealmPromotionGraceYears;
    }

    private static float ResolvePersonalNaturalDeathOnsetYears(Actor actor, float maxLifespan)
    {
        if (maxLifespan <= 0f) return 0f;
        // 紫府从500岁开始进入寿尽概率，不再额外把起算点推迟到550岁附近。
        if (IsExactZiFuRealm(actor)) return 0f;
        long actorId = GetActorId(actor);
        float stable01 = actorId > 0L
            ? XjDeterministicHash.PositiveIndex(actorId, "natural_death_personal_onset_v2", 10001) / 10000f
            : 0.5f;
        return maxLifespan * NaturalDeathPersonalOnsetSpreadRatio * Mathf.Clamp01(stable01);
    }

    private static float ResolvePersonalHardOverageYears(Actor actor, float maxLifespan)
    {
        if (maxLifespan <= 0f) return NaturalDeathHardOverageMinimumYears;
        long actorId = GetActorId(actor);
        if (IsExactZiFuRealm(actor))
        {
            // 每名紫府获得稳定、可读档复现的1—40年寿尽窗口：
            // 标准寿限500，最迟在501—540岁之间寿尽，不再锁死同一年。
            return 1f + (actorId > 0L
                ? XjDeterministicHash.PositiveIndex(actorId, "zifu_hard_lifespan_500_540_v1", 40)
                : 19);
        }
        float stable01 = actorId > 0L
            ? XjDeterministicHash.PositiveIndex(actorId, "natural_death_hard_overage_v2", 10001) / 10000f
            : 0.5f;
        float ratio = NaturalDeathHardOverageBaseRatio
            + NaturalDeathHardOverageSpreadRatio * Mathf.Clamp01(stable01);
        return Mathf.Max(NaturalDeathHardOverageMinimumYears, maxLifespan * ratio);
    }

    private static bool IsExactZiFuRealm(Actor actor)
    {
        return actor?.data != null
            && XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            && string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZiFu, StringComparison.Ordinal);
    }

    internal static float CalculateNaturalDeathProbability(float currentAge, float maxLifespan)
    {
        if (maxLifespan <= 0f)
        {
            return 0f;
        }

        float overageRatio = (currentAge - maxLifespan) / maxLifespan;
        if (overageRatio <= 0f)
        {
            return 0f;
        }

        float exponent = -NaturalDeathSigmoidSteepness * (overageRatio - NaturalDeathSigmoidMidpoint);
        float probability = 1f / (1f + (float)Math.Exp(exponent));
        return Mathf.Clamp(probability, 0f, NaturalDeathMaxProbability);
    }

    private static bool ExecuteNaturalDeath(Actor actor)
    {
        bool isLongShu = XjLongShuSystem.IsLongShu(actor);
        string actorName = isLongShu ? XjStringHelper.ActorName(actor, "无名龙君") : string.Empty;
        long actorId = isLongShu ? GetActorId(actor) : 0L;
        bool died = TryExecuteForceDeath(actor, (AttackType)NaturalDeathAttackType, true, XjDeathCause.NaturalOldAge);
        if (died && isLongShu)
        {
            string body = actorName + "寿满八百载，鳞光散入沧海，龙渊潮息三日，水族皆伏。";
            XjWorldHistoryStore.RecordDomainEvent(
                XjWorldHistoryCategory.HighRealm,
                actorName + "归墟",
                body,
                4,
                true,
                actorId: actorId,
                actorName: actorName,
                year: Math.Max(0, World.world?.map_stats?.year ?? 0));
            XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
                body, XjAnnouncementCategory.HighRealm, duration: 9f, color: "#78BFEA", delayFrames: 1);
        }
        return died;
    }

    private static bool TryHandleLongShuNaturalDeath(Actor actor, ref bool result)
    {
        if (WorldLawLibrary.world_law_old_age == null || !WorldLawLibrary.world_law_old_age.isEnabled())
        {
            return false;
        }

        float currentAge = Mathf.Max(0f, actor.getAge());
        float maxLifespan = XjLongShuSystem.GetFixedLifespanLimit(actor);
        if (maxLifespan <= 0f || currentAge <= maxLifespan)
        {
            return false;
        }

        if (currentAge >= maxLifespan + LongShuHardOverageYears)
        {
            result = ExecuteNaturalDeath(actor);
            return false;
        }

        float probability = CalculateNaturalDeathProbability(currentAge, maxLifespan);
        if (probability <= 0f || !Randy.randomChance(probability))
        {
            return false;
        }

        result = ExecuteNaturalDeath(actor);
        return false;
    }

    private static bool EnforceLongShuHardLifespanLimit(Actor actor)
    {
        float currentAge = Mathf.Max(0f, actor.getAge());
        float maxLifespan = XjLongShuSystem.GetFixedLifespanLimit(actor);
        if (maxLifespan <= 0f || currentAge <= maxLifespan)
        {
            return false;
        }

        return currentAge >= maxLifespan + LongShuHardOverageYears && ExecuteNaturalDeath(actor);
    }

    internal static float GetEffectiveLifespan(Actor actor)
    {
        try
        {
            // Self-heal saves whose cached stats were built before the realm trait
            // assets were rebuilt. FaBao lifespan is already merged by updateStats.
            XjRealmLifespanService.EnsureFreshStats(actor);
            return actor?.stats == null ? 0f : Mathf.Max(0f, actor.stats["lifespan"]);
        }
        catch
        {
            return 0f;
        }
    }

    private static float GetActorLifespan(Actor actor) => GetEffectiveLifespan(actor);

    private static long GetActorId(Actor actor)
    {
        try
        {
            return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
        }
        catch
        {
            return 0L;
        }
    }

    // 已删除旧版“致死伤害钳到 5% HP”的非致死保护。
    // 该逻辑会令角色在战斗中形成不可突破的生命下限；现在仅保留环境单次伤害上限。

    /// <summary>
    /// 对来自非 Actor 源的伤害做上限控制
    /// 对应 0.5.4 ApplyNonActorDamageCap
    /// 规则：非Actor源对修士造成的伤害上限为 maxHp * 0.15
    /// </summary>
    internal static bool ApplyNonActorDamageCap(
		ref float damage,
		Actor defender,
		BaseSimObject attacker,
		AttackType attackType,
		int realmTier)
    {
        if (damage <= 0f || defender == null)
            return false;

        if (IsForceDeathActor(defender))
            return false;

        Actor attackerActor = XjProjectileGuard.ResolveAttackSourceActor(attacker);
        if (XjProjectileGuard.IsCombatDeliverySource(attacker)
            || XjSafeCore.IsAliveActor(attackerActor)
            || !IsEnvironmentalAttackType(attackType))
            return false;

        bool preventStarvationDeath = attackType == AttackType.Starvation
            && HasXuanJianStarvationProtection(defender, realmTier);

        // 非修士、非百艺/持器角色不受玄鉴环境伤害限制
        if (realmTier <= 0 && !preventStarvationDeath)
            return false;

        float maxHp = XjSafeCore.GetMaxHealthSafe(defender);
        if (maxHp <= 0f) return false;

        if (preventStarvationDeath)
        {
            damage = 0f;
            return true;
        }

        float cap = maxHp * 0.15f;
        if (damage > cap)
        {
            damage = cap;
            return true;
        }

        return false;
    }

    private static bool HasXuanJianStarvationProtection(Actor actor, int realmTier)
    {
        if (actor?.data == null)
        {
            return false;
        }

        if (realmTier > 0 || XjLongShuSystem.IsLongShu(actor))
        {
            return true;
        }

        if (XjCraftTraitRules.HasAnyCraftTrait(actor))
        {
            return true;
        }

        return XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoId, out string faBaoId)
            && !string.IsNullOrWhiteSpace(faBaoId);
    }

    private static bool IsXuanJianCultivator(Actor actor, int realmTier)
    {
        if (actor?.data == null)
        {
            return false;
        }

        if (realmTier > XjRealmSuppression.TierNone)
        {
            return true;
        }

        return XuanJianVNext.Systems.Cultivation.XjYinSiTraitLifecycle.IsYinSi(actor)
            || XjShenDanAccessor.BuildState(actor).Found;
    }

    internal static bool IsEnvironmentalAttackType(AttackType attackType)
    {
        return attackType == AttackType.Poison
            || attackType == AttackType.Eaten
            || attackType == AttackType.Infection
            || attackType == AttackType.AshFever
            || attackType == AttackType.Plague
            || attackType == AttackType.Metamorphosis
            || attackType == AttackType.Starvation
            || attackType == AttackType.Tumor
            || attackType == AttackType.Water
            || attackType == AttackType.Drowning
            || attackType == AttackType.Gravity
            || attackType == AttackType.Fire
            || attackType == AttackType.Acid
            || attackType == AttackType.Age;
    }

}
