using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 0.9.8.7 原著具名神通的轻量战斗状态层。
///
/// 只保存当前正在生效的少量角色状态；无状态时所有热路径 O(1) 立即返回。
/// 不扫描世界、不写日志、不写存档。神通失效/法器压制使用延迟回调恢复 stats，
/// 避免为了短时控制增加年度或逐帧遍历。
/// </summary>
internal static class XjCanonicalShenTongMechanicsSystem
{
    private sealed class CombatState
    {
        internal float ShenTongSuppressedUntil;
        internal float ArtifactSuppressedUntil;
        internal float SongShangXueUntil;
        internal float QiDaiYeUntil;
        internal float QiDaiYeSpeedUntil;
        internal int QiDaiYeSpeedStacks;
        internal float QiDaiYeNextAbsorbAt;
        internal bool QiDaiYeSpeedResetScheduled;
        internal float FuQingShanUntil;
        internal int FuQingShanStacks;
        internal float BaoShiMianSeenUntil;
        internal float ZhiJiXiSeenUntil;
        internal float ZhiJiXiNextDeflectAt;
        internal float MiBaiGongSeenUntil;
        internal float MiBaiGongNextRecoveryAt;
    }

    private static readonly Dictionary<long, CombatState> States = new Dictionary<long, CombatState>(64);
    // 紫府同级以上受击热路径使用的正/负识别缓存。未掌握本批被动者30秒内不再拆神通数组。
    private static readonly Dictionary<long, float> PassiveProbeReadyAt = new Dictionary<long, float>(128);

    internal static bool HasAnyState => States.Count > 0;

    internal static void RefreshPassiveCombatStates(Actor actor)
    {
        if (actor?.data == null) return;
        long actorId = GetActorId(actor);
        if (actorId <= 0L) return;
        float now = Time.time;
        if (PassiveProbeReadyAt.TryGetValue(actorId, out float readyAt) && now < readyAt) return;
        string[] learned = XjXianJiAccessor.ReadRawIds(actor);
        RefreshPassiveCombatStates(actor, learned);
        bool hasRelevant = States.TryGetValue(actorId, out CombatState state)
            && (now < state.BaoShiMianSeenUntil || now < state.ZhiJiXiSeenUntil || now < state.MiBaiGongSeenUntil);
        PassiveProbeReadyAt[actorId] = now + (hasRelevant ? 20f : 30f);
    }

    internal static void RefreshPassiveCombatStates(Actor actor, string[] learned)
    {
        if (actor?.data == null || learned == null || learned.Length == 0) return;
        bool baoShiMian = ContainsAny(learned, "抱石眠");
        bool zhiJiXi = ContainsAny(learned, "致缉熙");
        bool miBaiGong = ContainsAny(learned, "制飬宜", "制养宜", "秘白汞", "秘白录");
        if (!baoShiMian && !zhiJiXi && !miBaiGong) return;

        long actorId = GetActorId(actor);
        if (actorId <= 0L) return;
        CombatState state = GetOrCreate(actorId);
        float now = Time.time;
        // 这些是“角色确实掌握该被动神通”的战斗期短缓存，不是技能持续时间。
        // 45秒内角色只要继续参与攻击回调就会被刷新，离战后自然失效。
        if (baoShiMian) state.BaoShiMianSeenUntil = Math.Max(state.BaoShiMianSeenUntil, now + 45f);
        if (zhiJiXi) state.ZhiJiXiSeenUntil = Math.Max(state.ZhiJiXiSeenUntil, now + 45f);
        if (miBaiGong) state.MiBaiGongSeenUntil = Math.Max(state.MiBaiGongSeenUntil, now + 45f);
    }

    internal static void ApplyShenTongAndArtifactSuppression(Actor target, float durationSeconds, string source)
    {
        if (target?.data == null || durationSeconds <= 0f) return;
        long actorId = GetActorId(target);
        if (actorId <= 0L) return;
        CombatState state = GetOrCreate(actorId);
        float now = Time.time;
        float until = now + durationSeconds;
        state.ShenTongSuppressedUntil = Math.Max(state.ShenTongSuppressedUntil, until);
        state.ArtifactSuppressedUntil = Math.Max(state.ArtifactSuppressedUntil, until);
        RefreshStats(target);

        // 法器/剑艺属性被压制后，必须在控制结束时主动恢复一次 stats；
        // 这里使用现有延迟调度，不建立逐帧计时器。
        XjJinDanCombatApi.TryScheduleImpact(
            durationSeconds + 0.03f,
            () =>
            {
                if (target?.data == null) return;
                long id = GetActorId(target);
                if (id <= 0L || !States.TryGetValue(id, out CombatState s)) return;
                float t = Time.time;
                if (t >= s.ArtifactSuppressedUntil)
                {
                    s.ArtifactSuppressedUntil = 0f;
                    RefreshStats(target);
                }
                if (t >= s.ShenTongSuppressedUntil) s.ShenTongSuppressedUntil = 0f;
                PruneIfEmpty(id, s, t);
            },
            string.IsNullOrWhiteSpace(source) ? "CanonicalSuppress" : source,
            out _);
    }

    /// <summary>
    /// 仅令神通/施法暂时断联，不触碰装备属性。用于东羽山“凝滞灵机”与僭劻勷“斩断气息”。
    /// </summary>
    internal static void ApplyShenTongSuppression(Actor target, float durationSeconds)
    {
        if (target?.data == null || durationSeconds <= 0f) return;
        long actorId = GetActorId(target);
        if (actorId <= 0L) return;
        CombatState state = GetOrCreate(actorId);
        state.ShenTongSuppressedUntil = Math.Max(state.ShenTongSuppressedUntil, Time.time + durationSeconds);
    }

    internal static bool IsShenTongSuppressed(Actor actor)
    {
        if (actor?.data == null || States.Count == 0) return false;
        long actorId = GetActorId(actor);
        if (actorId <= 0L || !States.TryGetValue(actorId, out CombatState state)) return false;
        float now = Time.time;
        bool active = now < state.ShenTongSuppressedUntil;
        if (!active)
        {
            state.ShenTongSuppressedUntil = 0f;
            PruneIfEmpty(actorId, state, now);
        }
        return active;
    }

    internal static bool IsArtifactSuppressed(Actor actor)
    {
        if (actor?.data == null || States.Count == 0) return false;
        long actorId = GetActorId(actor);
        if (actorId <= 0L || !States.TryGetValue(actorId, out CombatState state)) return false;
        float now = Time.time;
        bool active = now < state.ArtifactSuppressedUntil;
        if (!active)
        {
            state.ArtifactSuppressedUntil = 0f;
            PruneIfEmpty(actorId, state, now);
        }
        return active;
    }

    internal static void ApplySongShangXue(Actor actor, float durationSeconds)
    {
        ApplySelfTimedState(actor, durationSeconds, (state, until) => state.SongShangXueUntil = Math.Max(state.SongShangXueUntil, until));
    }

    internal static void ApplyQiDaiYe(Actor actor, float durationSeconds)
    {
        ApplySelfTimedState(actor, durationSeconds, (state, until) => state.QiDaiYeUntil = Math.Max(state.QiDaiYeUntil, until));
    }

    internal static void ApplyFuQingShan(Actor actor, float durationSeconds)
    {
        if (actor?.data == null || durationSeconds <= 0f) return;
        long actorId = GetActorId(actor);
        if (actorId <= 0L) return;
        CombatState state = GetOrCreate(actorId);
        float now = Time.time;
        state.FuQingShanUntil = Math.Max(state.FuQingShanUntil, now + durationSeconds);
        state.FuQingShanStacks = 0;
    }

    /// <summary>
    /// 致缉熙：原著为“敌攻来则与之纠缠并偏移他处”。WorldBox 没有一一对应的
    /// 攻击轨迹重定向 API，因此落实为低频完整偏移本次攻击；不是常驻减伤。
    /// </summary>
    internal static bool TryDeflectIncoming(Actor defender)
    {
        if (defender?.data == null || States.Count == 0) return false;
        long actorId = GetActorId(defender);
        if (actorId <= 0L || !States.TryGetValue(actorId, out CombatState state)) return false;
        float now = Time.time;
        if (now >= state.ZhiJiXiSeenUntil)
        {
            state.ZhiJiXiSeenUntil = 0f;
            PruneIfEmpty(actorId, state, now);
            return false;
        }
        if (now < state.ZhiJiXiNextDeflectAt) return false;
        // 15秒为游戏化反制间隔，避免每次受击都无条件归零；它不是“主动法术CD”。
        state.ZhiJiXiNextDeflectAt = now + 15f;
        return true;
    }

    /// <summary>
    /// 在统一伤害解析中应用当前短时神通状态。这里只处理 Excel 有明确战斗语义的部分：
    /// 松上雪加持斗法/抵御伤势，乞代夜收束光火，伏青山越战越猛，抱石眠肌骨还真。
    /// </summary>
    internal static float ResolveActorAttackMultiplier(Actor attacker, Actor defender, string attackerDaoTu)
    {
        if (States.Count == 0) return 1f;
        float now = Time.time;
        float multiplier = 1f;

        long attackerId = GetActorId(attacker);
        if (attackerId > 0L && States.TryGetValue(attackerId, out CombatState attackState))
        {
            if (now < attackState.SongShangXueUntil) multiplier *= 1.10f;
            if (now < attackState.FuQingShanUntil)
            {
                attackState.FuQingShanStacks = Math.Min(5, attackState.FuQingShanStacks + 1);
                multiplier *= 1f + attackState.FuQingShanStacks * 0.04f;
            }
            PruneIfEmpty(attackerId, attackState, now);
        }

        long defenderId = GetActorId(defender);
        if (defenderId > 0L && States.TryGetValue(defenderId, out CombatState defendState))
        {
            if (now < defendState.SongShangXueUntil) multiplier *= 0.88f;
            if (now < defendState.BaoShiMianSeenUntil) multiplier *= 0.92f;
            if (now < defendState.QiDaiYeUntil && IsLightOrFireDaoTu(attackerDaoTu))
            {
                // 原著：收束光、火类攻击后转化为青蓝之色助自身走脱。伤害吸收沿用0.9.8.7；
                // 遁速只作为6秒短时运行期乘区，不写永久面板。具体比例为WorldBox化数值。
                multiplier *= 0.55f;
                TryAccumulateQiDaiYeSpeed(defender, defenderId, defendState, now);
            }
            PruneIfEmpty(defenderId, defendState, now);
        }
        return multiplier;
    }

    /// <summary>
    /// 乞代夜吸收光火后的短时遁速。每次有效收束叠一层，最多5层；
    /// 每层+8%移动速度，6秒无新吸收后归零。数值是WorldBox化映射，不是原著明示数值。
    /// </summary>
    internal static void ApplyRuntimeMovementMultiplier(Actor actor)
    {
        if (actor?.data == null || actor.stats == null || States.Count == 0) return;
        long actorId = GetActorId(actor);
        if (actorId <= 0L || !States.TryGetValue(actorId, out CombatState state)) return;
        float now = Time.time;
        if (now >= state.QiDaiYeSpeedUntil || state.QiDaiYeSpeedStacks <= 0)
        {
            if (now >= state.QiDaiYeSpeedUntil)
            {
                state.QiDaiYeSpeedUntil = 0f;
                state.QiDaiYeSpeedStacks = 0;
            }
            return;
        }
        float speed = XjSafeCore.GetStatSafe(actor, "speed", 0f);
        if (speed > 0f) actor.stats["speed"] = speed * (1f + 0.08f * Math.Min(5, state.QiDaiYeSpeedStacks));
    }

    /// <summary>
    /// 秘白汞“伤势恢复快”的 WorldBox 化：受击后低频回返一小部分本次伤势。
    /// 不改最大生命、不做常驻再生；3秒内最多触发一次，避免多段攻击造成治疗风暴。
    /// </summary>
    internal static void TryScheduleMiBaiGongRecovery(Actor defender, float resolvedDamage)
    {
        if (defender?.data == null || resolvedDamage <= 0f || States.Count == 0) return;
        long actorId = GetActorId(defender);
        if (actorId <= 0L || !States.TryGetValue(actorId, out CombatState state)) return;
        float now = Time.time;
        if (now >= state.MiBaiGongSeenUntil || now < state.MiBaiGongNextRecoveryAt) return;
        state.MiBaiGongNextRecoveryAt = now + 3f;
        float healAmount = Math.Max(1f, resolvedDamage * 0.18f);
        XjJinDanCombatApi.TryScheduleImpact(
            0.12f,
            () =>
            {
                if (!XjSafeCore.IsAliveActor(defender)) return;
                float maxHealth = Math.Max(1f, XjSafeCore.GetMaxHealthSafe(defender, 1f));
                XjSafeCore.HealActorSafe(defender, Math.Min(healAmount, maxHealth * 0.05f));
            },
            "秘白汞·养伤",
            out _);
    }

    internal static bool TrySwapPositions(Actor caster, Actor target)
    {
        if (caster?.data == null || target?.data == null || caster == target) return false;
        WorldTile casterTile = TryGetTile(caster);
        WorldTile targetTile = TryGetTile(target);
        if (casterTile == null || targetTile == null
            || !XjHighRealmMovement.IsSafeTeleportDestination(casterTile)
            || !XjHighRealmMovement.IsSafeTeleportDestination(targetTile)) return false;

        // 两次原生 spawnOn 入口完成真正换位；先移动目标，再移动施法者，均不清空正常战斗目标。
        bool targetMoved = XjHighRealmMovement.TryImmediateTeleport(target, casterTile);
        if (!targetMoved) return false;
        bool casterMoved = XjHighRealmMovement.TryImmediateTeleport(caster, targetTile);
        if (casterMoved) return true;

        // 第二步极少数情况下失败时尽量回滚目标，避免“换位失败却单方面把敌人拉到身边”。
        XjHighRealmMovement.TryImmediateTeleport(target, targetTile);
        return false;
    }

    private static void TryAccumulateQiDaiYeSpeed(Actor actor, long actorId, CombatState state, float now)
    {
        if (actor?.data == null || actorId <= 0L || state == null || now < state.QiDaiYeNextAbsorbAt) return;
        state.QiDaiYeNextAbsorbAt = now + 0.35f;
        state.QiDaiYeSpeedStacks = Math.Min(5, Math.Max(0, state.QiDaiYeSpeedStacks) + 1);
        state.QiDaiYeSpeedUntil = Math.Max(state.QiDaiYeSpeedUntil, now + 6f);
        RefreshStats(actor);
        EnsureQiDaiYeSpeedReset(actor, actorId, state);
    }

    private static void EnsureQiDaiYeSpeedReset(Actor actor, long actorId, CombatState state)
    {
        if (state == null || state.QiDaiYeSpeedResetScheduled) return;
        state.QiDaiYeSpeedResetScheduled = true;
        ScheduleQiDaiYeSpeedReset(actor, actorId, 6.05f);
    }

    private static void ScheduleQiDaiYeSpeedReset(Actor actor, long actorId, float delaySeconds)
    {
        bool scheduled = XjJinDanCombatApi.TryScheduleImpact(
            Math.Max(0.05f, delaySeconds),
            () =>
            {
                if (actor?.data == null || actorId <= 0L || !States.TryGetValue(actorId, out CombatState state)) return;
                float now = Time.time;
                if (now < state.QiDaiYeSpeedUntil)
                {
                    ScheduleQiDaiYeSpeedReset(actor, actorId, state.QiDaiYeSpeedUntil - now + 0.03f);
                    return;
                }
                state.QiDaiYeSpeedUntil = 0f;
                state.QiDaiYeSpeedStacks = 0;
                state.QiDaiYeNextAbsorbAt = 0f;
                state.QiDaiYeSpeedResetScheduled = false;
                RefreshStats(actor);
                PruneIfEmpty(actorId, state, now);
            },
            "乞代夜·遁势归静",
            out _);

        if (!scheduled && States.TryGetValue(actorId, out CombatState fallbackState))
        {
            // 世界/调度器尚未就绪时不能把“已安排归静”永久留成 true；
            // 安全回退为立刻清掉运行时遁速，不让短时增益在一次失败后常驻。
            fallbackState.QiDaiYeSpeedUntil = 0f;
            fallbackState.QiDaiYeSpeedStacks = 0;
            fallbackState.QiDaiYeNextAbsorbAt = 0f;
            fallbackState.QiDaiYeSpeedResetScheduled = false;
            RefreshStats(actor);
            PruneIfEmpty(actorId, fallbackState, Time.time);
        }
    }

    internal static void RemoveActor(long actorId)
    {
        if (actorId <= 0L) return;
        States.Remove(actorId);
        PassiveProbeReadyAt.Remove(actorId);
    }

    internal static void Clear()
    {
        States.Clear();
        PassiveProbeReadyAt.Clear();
    }

    private static void ApplySelfTimedState(Actor actor, float durationSeconds, Action<CombatState, float> apply)
    {
        if (actor?.data == null || durationSeconds <= 0f || apply == null) return;
        long actorId = GetActorId(actor);
        if (actorId <= 0L) return;
        CombatState state = GetOrCreate(actorId);
        apply(state, Time.time + durationSeconds);
    }

    private static CombatState GetOrCreate(long actorId)
    {
        if (!States.TryGetValue(actorId, out CombatState state))
        {
            state = new CombatState();
            States[actorId] = state;
        }
        return state;
    }

    private static void PruneIfEmpty(long actorId, CombatState state, float now)
    {
        if (state == null) return;
        if (now < state.ShenTongSuppressedUntil
            || now < state.ArtifactSuppressedUntil
            || now < state.SongShangXueUntil
            || now < state.QiDaiYeUntil
            || now < state.QiDaiYeSpeedUntil
            || now < state.FuQingShanUntil
            || now < state.BaoShiMianSeenUntil
            || now < state.ZhiJiXiSeenUntil
            || now < state.MiBaiGongSeenUntil) return;
        States.Remove(actorId);
    }

    private static bool IsLightOrFireDaoTu(string daoTu)
    {
        string value = (daoTu ?? string.Empty).Trim();
        return value is "太阳" or "少阳" or "明阳" or "离火" or "真火" or "并火" or "牡火" or "灴火" or "晞炁";
    }

    private static bool ContainsAny(string[] learned, params string[] names)
    {
        if (learned == null || names == null) return false;
        for (int i = 0; i < learned.Length; i++)
        {
            string current = (learned[i] ?? string.Empty).Trim();
            if (current.Length == 0) continue;
            for (int j = 0; j < names.Length; j++)
            {
                if (string.Equals(current, names[j], StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    private static void RefreshStats(Actor actor)
    {
        if (actor?.data == null) return;
        try { actor.setStatsDirty(); } catch { }
        try { actor.updateStats(); } catch { }
    }

    private static long GetActorId(Actor actor)
    {
        try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
        catch { return 0L; }
    }

    private static WorldTile TryGetTile(Actor actor)
    {
        try { return actor?.data == null ? null : ((BaseSimObject)actor).current_tile; }
        catch { return null; }
    }
}
