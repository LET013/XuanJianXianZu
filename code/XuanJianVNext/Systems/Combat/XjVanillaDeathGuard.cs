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
    // 永生相关特质标记
    private const string HiddenImmortalityKey = "xuanjian.vnext.hidden_immortality";
    private const float NaturalDeathSigmoidSteepness = 100f;
    private const float NaturalDeathSigmoidMidpoint = 0.03f;
    private const float NaturalDeathMaxProbability = 1f;
    private const float NaturalDeathHardOverageRatio = 0.05f;
    private const float NaturalDeathHardOverageYears = 10f;
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
            BaseSystemData data = (BaseSystemData)actor.data;
            data.set(HiddenImmortalityKey, enabled);
			XjCombatHotPathCache.Refresh(actor);
        }
        catch { }
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
        result = false;
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return false;
        }

        if (XjLongShuSystem.IsLongShu(actor))
        {
            // 龙属紫府仍遵守八百年寿限；龙属一旦成就金丹／神丹，必须先进入
            // 金丹免自然寿尽分支，不能继续按紫府龙属固定寿元处理。
            if (IsJinDanOrShenDan(actor) || XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor)) return false;
            return TryHandleLongShuNaturalDeath(actor, ref result);
        }

        if (IsJinDanOrShenDan(actor))
        {
            return false;
        }

        if (XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor))
        {
            return false;
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

        // 玄门同款 sigmoid 处理正常寿尽；额外加入确定性上限，禁止角色
        // 在远超有效寿元后仍因连续随机未命中而长期存活。
        float hardOverage = Mathf.Min(
            NaturalDeathHardOverageYears,
            Mathf.Max(1f, maxLifespan * NaturalDeathHardOverageRatio));
        if (currentAge >= maxLifespan + hardOverage)
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

    /// <summary>
    /// Annual deterministic backstop. Native code can skip checkNaturalDeath for an
    /// actor carrying the vanilla immortal trait; XuanJian cultivators must still
    /// obey the lifespan displayed by their effective stats.
    /// </summary>
    internal static bool EnforceHardLifespanLimit(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor)
            || WorldLawLibrary.world_law_old_age == null
            || !WorldLawLibrary.world_law_old_age.isEnabled())
        {
            return false;
        }

        if (XjLongShuSystem.IsLongShu(actor))
        {
            if (IsJinDanOrShenDan(actor) || XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor)) return false;
            return EnforceLongShuHardLifespanLimit(actor);
        }

        if (IsJinDanOrShenDan(actor))
        {
            return false;
        }

        if (XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor)
            || !IsXuanJianManagedActor(actor))
        {
            return false;
        }

        float currentAge = Mathf.Max(0f, actor.getAge());
        float maxLifespan = GetActorLifespan(actor);
        if (maxLifespan <= 0f || currentAge <= maxLifespan)
        {
            return false;
        }

        float hardOverage = Mathf.Min(
            NaturalDeathHardOverageYears,
            Mathf.Max(1f, maxLifespan * NaturalDeathHardOverageRatio));
        return currentAge >= maxLifespan + hardOverage && ExecuteNaturalDeath(actor);
    }

    private static bool IsXuanJianManagedActor(Actor actor)
    {
        return actor?.data != null
            && XjRealmSuppression.GetRealmTier(actor) > XjRealmSuppression.TierNone;
    }

    private static bool IsJinDanOrShenDan(Actor actor)
    {
        if (actor?.data == null)
        {
            return false;
        }

        int realmTier = XjRealmSuppression.GetRealmTier(actor);
        return realmTier >= XjRealmSuppression.TierJinDan
            || XjJinDanAccessor.BuildState(actor).Found
            || XjShenDanAccessor.BuildState(actor).Found;
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
        string actorName = isLongShu ? SafeActorName(actor, "无名龙君") : string.Empty;
        long actorId = isLongShu ? GetActorId(actor) : 0L;
        bool died = TryExecuteForceDeath(actor, (AttackType)NaturalDeathAttackType, true);
        if (died && isLongShu)
        {
            XjWorldHistoryStore.RecordDomainEvent(
                XjWorldHistoryCategory.HighRealm,
                actorName + "归墟",
                actorName + "寿满八百载，鳞光散入沧海，龙渊潮息三日，水族皆伏。",
                4,
                true,
                actorId: actorId,
                actorName: actorName,
                year: Math.Max(0, World.world?.map_stats?.year ?? 0));
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

        float hardOverage = Mathf.Min(
            NaturalDeathHardOverageYears,
            Mathf.Max(1f, maxLifespan * NaturalDeathHardOverageRatio));
        if (currentAge >= maxLifespan + hardOverage)
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

        float hardOverage = Mathf.Min(
            NaturalDeathHardOverageYears,
            Mathf.Max(1f, maxLifespan * NaturalDeathHardOverageRatio));
        return currentAge >= maxLifespan + hardOverage && ExecuteNaturalDeath(actor);
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

    private static string SafeActorName(Actor actor, string fallback)
    {
        try
        {
            string name = actor?.getName();
            return string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        }
        catch
        {
            return fallback;
        }
    }

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
