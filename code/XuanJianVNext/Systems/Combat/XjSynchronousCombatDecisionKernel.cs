using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.Talisman;

namespace XuanJianVNext.Systems.Combat;

internal struct XjSynchronousCombatDecisionState
{
    internal float BeforeHealth;
    internal float VampirePercent;
    internal bool EnteredRuntimePath;
    internal long SampleStarted;
}

internal readonly struct XjSynchronousCombatDecision
{
    internal readonly bool ShouldRunOriginal;
    internal readonly float Damage;
    internal readonly bool CheckDamageReduction;
    internal readonly XjSynchronousCombatDecisionState State;

    internal XjSynchronousCombatDecision(
        bool shouldRunOriginal,
        float damage,
        bool checkDamageReduction,
        in XjSynchronousCombatDecisionState state)
    {
        ShouldRunOriginal = shouldRunOriginal;
        Damage = damage;
        CheckDamageReduction = checkDamageReduction;
        State = state;
    }
}

/// <summary>
/// Actor.getHit 的同步判定内核。Harmony补丁只采集参数、调用判定并回填结果；
/// 境界压制、投射物隔离、环境伤害、真伤、符箓与追踪顺序只在此处维护。
/// </summary>
internal static class XjSynchronousCombatDecisionKernel
{
    internal static XjSynchronousCombatDecision Evaluate(
        Actor defender,
        float requestedDamage,
        AttackType attackType,
        BaseSimObject attackerObject,
        bool checkDamageReduction)
    {
        float damage = requestedDamage;
        bool checkReduction = checkDamageReduction;
        XjSynchronousCombatDecisionState state = default;
        if (!XjSafeCore.IsAliveActor(defender) || damage <= 0f)
        {
            return Result(true, damage, checkReduction, in state);
        }

        // Most native melee hits already pass the Actor itself. Avoid projectile-source
        // resolution unless the delivery object is not an Actor.
        Actor attacker = attackerObject as Actor ?? XjProjectileGuard.ResolveAttackSourceActor(attackerObject);
        // 0.9.9：道胎/世尊与真人/真君转世的致死伤害必须在原生 getHit 前被截断。
        XjDaoTaiSurvivalGuard.ClampIncomingDamage(defender, ref damage);
        XjReincarnationSurvivalGuard.ClampIncomingDamage(defender, ref damage);
        if (damage <= 0f)
        {
            return Result(false, 0f, checkReduction, in state);
        }
        long defenderId = ((BaseSystemData)defender.data).id;

        // Ordinary-vs-ordinary and ordinary environment damage dominate large worlds.
        // Once both sides have hit the negative combat-interest cache, return to native
        // WorldBox before touching any high-realm archive, Shi, TaiYin, starvation/fire
        // or cultivation guards. Special identities invalidate this classification at
        // their lifecycle write, so the shortcut cannot hide XuanJian actors.
        if (XjRuntimeActorInterestIndex.HasOrdinaryCombatClassification(defenderId))
        {
            if (attacker?.data == null || attacker == defender)
            {
                return Result(true, damage, checkReduction, in state);
            }

            long ordinaryAttackerId = ((BaseSystemData)attacker.data).id;
            if (ordinaryAttackerId > 0L
                && XjRuntimeActorInterestIndex.HasOrdinaryCombatClassification(ordinaryAttackerId))
            {
                return Result(true, damage, checkReduction, in state);
            }
        }

        if (XjDaoTaiPresenceArchive.IsBodyArchived(defenderId))
        {
            damage = 0f;
            return Result(false, damage, checkReduction, in state);
        }

        int precheckDefenderTier = XjCultivatorCache.TryGetRealmTier(defenderId, out int cachedPrecheckDefenderTier)
            ? cachedPrecheckDefenderTier
            : XjRealmSuppression.GetRealmTier(defender);
        if (XjVanillaDeathGuard.ShouldBlockStarvationDamage(ref damage, defender, attackType)
            || XjVanillaDeathGuard.ShouldBlockFireDamage(ref damage, defender, attackType, precheckDefenderTier))
        {
            return Result(false, damage, checkReduction, in state);
        }
        if (XjClosedCultivationGuard.BlockIfClosedCultivation(ref damage, defender, precheckDefenderTier, attacker))
        {
            return Result(false, damage, checkReduction, in state);
        }

        bool hasExternalAttacker = attacker?.data != null && attacker != defender;
        if (hasExternalAttacker && XjZhantanlinSystem.ShouldBlockCombat(attacker, defender))
        {
            damage = 0f;
            return Result(false, damage, checkReduction, in state);
        }
        if (hasExternalAttacker && XjTaiYinNonAggressionPolicy.ShouldBlockIncomingAttack(attacker, defender))
        {
            XjTaiYinNonAggressionPolicy.SuppressPassiveRetaliation(defender);
            damage = 0f;
            return Result(false, damage, checkReduction, in state);
        }
        long attackerId = hasExternalAttacker ? ((BaseSystemData)attacker.data).id : 0L;
        if (attackerId > 0L && XjDaoTaiPresenceArchive.IsBodyArchived(attackerId))
        {
            damage = 0f;
            return Result(false, damage, checkReduction, in state);
        }

        bool bootstrapPending = XjWorldBootstrapLane.HasPending;
        int repairedDefenderTier = XjRealmSuppression.TierNone;
        int repairedAttackerTier = XjRealmSuppression.TierNone;
        bool defenderKnownOrdinary = XjRuntimeActorInterestIndex.HasOrdinaryCombatClassification(defenderId);
        bool defenderInterested = XjRuntimeActorInterestIndex.HasCombatInterest(defenderId)
            || (!defenderKnownOrdinary
                && bootstrapPending
                && XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(defender));
        if (!defenderInterested && !defenderKnownOrdinary)
        {
            defenderInterested = XjRuntimeActorInterestIndex.TryRepairCombatInterest(
                defender,
                out repairedDefenderTier);
        }

        bool attackerKnownOrdinary = hasExternalAttacker
            && XjRuntimeActorInterestIndex.HasOrdinaryCombatClassification(attackerId);
        bool attackerInterested = hasExternalAttacker
            && (XjRuntimeActorInterestIndex.HasCombatInterest(attackerId)
                || (!attackerKnownOrdinary
                    && bootstrapPending
                    && XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(attacker)));
        if (hasExternalAttacker && !attackerInterested && !attackerKnownOrdinary)
        {
            attackerInterested = XjRuntimeActorInterestIndex.TryRepairCombatInterest(
                attacker,
                out repairedAttackerTier);
        }

        if (!defenderInterested && !attackerInterested)
        {
            return Result(true, damage, checkReduction, in state);
        }

        int defenderTier = repairedDefenderTier > XjRealmSuppression.TierNone
            ? repairedDefenderTier
            : defenderInterested
                ? XjCultivatorCache.TryGetRealmTier(defenderId, out int cachedDefenderTier)
                    ? cachedDefenderTier
                    : XjRealmSuppression.GetRealmTier(defender)
                : XjRealmSuppression.TierNone;
        int attackerTier = repairedAttackerTier > XjRealmSuppression.TierNone
            ? repairedAttackerTier
            : attackerInterested
                ? XjCultivatorCache.TryGetRealmTier(attackerId, out int cachedAttackerTier)
                    ? cachedAttackerTier
                    : XjRealmSuppression.GetRealmTier(attacker)
                : defenderTier > XjRealmSuppression.TierNone
                    ? XjRealmSuppression.GetRealmTier(attacker)
                    : XjRealmSuppression.TierNone;

        // 高境晋升后的首轮战斗不能继续相信旧缓存。实机中若角色刚由金丹晋升道胎，
        // 热路径缓存仍可能暂留 TierJinDan，导致本应触发的道胎绝对压制完全不进入。
        // 只在“缓存仍是金丹级”这个窄条件下做 O(1) 权威复核，避免给普通命中增加额外开销。
        if (hasExternalAttacker
            && attackerTier == XjRealmSuppression.TierJinDan
            && XjDaoTaiSpellScale.IsDaoTaiActor(attacker))
        {
            attackerTier = XjRealmSuppression.TierDaoTai;
            XjCultivatorCache.UpdateRealmTierDirect(attackerId, attackerTier);
        }
        if (defenderTier == XjRealmSuppression.TierJinDan
            && XjDaoTaiSpellScale.IsDaoTaiActor(defender))
        {
            defenderTier = XjRealmSuppression.TierDaoTai;
            XjCultivatorCache.UpdateRealmTierDirect(defenderId, defenderTier);
        }

        state.EnteredRuntimePath = true;
        state.SampleStarted = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.GetHit, 511);
        XjCombatHotProfile defenderProfile = XjCombatHotPathCache.Get(defender);
        XjCombatHotProfile attackerProfile = hasExternalAttacker
            ? XjCombatHotPathCache.Get(attacker)
            : default;

        // 仙国假金丹只在“角色对角色”的战斗决策里获得金丹级借玄位格。
        // 借玄位格已经物化在战斗热缓存；命中热路径不再访问仙国档案、国家反射
        // 或释修/神丹状态。真实培养缓存与环境伤害仍只认本身境界。
        if (hasExternalAttacker)
        {
            if (attackerProfile.BorrowedCombatTier > attackerTier) attackerTier = attackerProfile.BorrowedCombatTier;
            if (defenderProfile.BorrowedCombatTier > defenderTier) defenderTier = defenderProfile.BorrowedCombatTier;
        }

        if (XjProjectileGuard.ShouldBlock(ref damage, attackerObject, defender))
        {
            return Result(false, damage, checkReduction, in state);
        }

        if (!hasExternalAttacker)
        {
            if (defenderTier > XjRealmSuppression.TierNone
                && XjProjectileGuard.IsCombatDeliverySource(attackerObject))
            {
                damage = 0f;
                return Result(false, damage, checkReduction, in state);
            }

            bool useXjDefense = defenderTier > XjRealmSuppression.TierNone
                || defenderProfile.HasCustomCombatStats
                || defenderProfile.HasImmortalityProtection;
            if (!useXjDefense)
            {
                return Result(true, damage, checkReduction, in state);
            }

            checkReduction = false;
            XjVanillaDeathGuard.ApplyNonActorDamageCap(
                ref damage,
                defender,
                attackerObject,
                attackType,
                defenderTier);
            XjCombatResolution environmentResolution = XjCombatDamageResolver.ResolveEnvironmentDamage(
                ref damage,
                defender,
                defenderTier,
                defenderProfile);
            return Result(environmentResolution.ShouldRunOriginal && damage > 0f, damage, checkReduction, in state);
        }

        bool useXjCombat = attackerTier > XjRealmSuppression.TierNone
            || defenderTier > XjRealmSuppression.TierNone
            || attackerProfile.HasCustomCombatStats
            || defenderProfile.HasCustomCombatStats
            || attackerProfile.IsJinXingYaoXie
            || defenderProfile.IsJinXingYaoXie;
        if (!useXjCombat)
        {
            return Result(true, damage, checkReduction, in state);
        }

        checkReduction = false;
        state.VampirePercent = Mathf.Clamp(attackerProfile.Vampire, 0f, 100f);
        if (state.VampirePercent > 0f)
        {
            state.BeforeHealth = XjSafeCore.GetHealthSafe(defender, 0f);
        }

        XjCombatResolution resolution = XjCombatDamageResolver.ResolveActorAttack(
            ref damage,
            attacker,
            defender,
            attackerTier,
            defenderTier,
            attackerProfile,
            defenderProfile);

        if (resolution.Kind == XjCombatResolutionKind.Dodged)
        {
            try
            {
                defender.startColorEffect(ActorColorEffect.White);
            }
            catch (Exception exception)
            {
                XjExceptionDiagnostics.Report("XjSynchronousCombatDecisionKernel.Dodge", exception);
            }
            return Result(false, damage, checkReduction, in state);
        }

        if (!resolution.ShouldRunOriginal || damage <= 0f)
        {
            return Result(false, damage, checkReduction, in state);
        }

        if (resolution.Critical)
        {
            XjLongGengFlyingSwordSystem.TryPlayCriticalSwordFeather(attacker, defender);
        }

        XjTalismanCombatService.ApplyActorDamageTalismans(attacker, defender, ref damage);
        if (damage <= 0f)
        {
            return Result(false, damage, checkReduction, in state);
        }

        bool isJinDanCombat = attackerTier >= XjRealmSuppression.TierJinDan
            || defenderTier >= XjRealmSuppression.TierJinDan;
        if (isJinDanCombat && XjTrueDamageSystem.TryExecuteYinSiDeathSeal(defender, attacker))
        {
            return Result(false, damage, checkReduction, in state);
        }

        int currentYear = Math.Max(1, Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0));
        XjAlchemyPillEffectSystem.TryUseCombatHealingBeforeDamage(defender, damage, currentYear);
        // 护符减伤与受击前丹药都发生在统一伤害解析之后，因此必须在它们之后再校验
        // 一次道胎终结下限；否则低境会被“先抬血/再减伤”反复救回，表现为道胎打不死人。
        XjCombatDamageResolver.EnforceDaoTaiFinalDamageFloor(
            ref damage, attackerTier, defenderTier, defender);
        float healthBeforeDamage = XjSafeCore.GetHealthSafe(defender, 0f);
        XjCombatTracker.RecordAttacker(defender, attacker, defenderTier, attackerTier, damage, healthBeforeDamage);
        XjDomainSkillRuntime.RecordResolvedIncomingDamage(defender, damage);
        return Result(true, damage, checkReduction, in state);
    }

    internal static void Complete(
        Actor defender,
        BaseSimObject attackerObject,
        in XjSynchronousCombatDecisionState state)
    {
        if (!state.EnteredRuntimePath) return;

        XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.GetHit, state.SampleStarted);
        XjDaoTaiSurvivalGuard.TryHandlePostDamage(defender, attackerObject);

        Actor attacker = XjProjectileGuard.ResolveAttackSourceActor(attackerObject);
        if (!XjSafeCore.IsAliveActor(attacker) || attacker == defender || state.VampirePercent <= 0f)
        {
            return;
        }

        float afterHealth = XjSafeCore.GetHealthSafe(defender, 0f);
        float damageDone = state.BeforeHealth > afterHealth ? state.BeforeHealth - afterHealth : 0f;
        if (damageDone > 0f)
        {
            XjSafeCore.HealActorSafe(attacker, damageDone * state.VampirePercent / 100f);
        }
    }

    private static XjSynchronousCombatDecision Result(
        bool shouldRunOriginal,
        float damage,
        bool checkDamageReduction,
        in XjSynchronousCombatDecisionState state)
        => new XjSynchronousCombatDecision(
            shouldRunOriginal,
            damage,
            checkDamageReduction,
            in state);
}
