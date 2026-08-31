using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 金性妖邪 + 阴司死印系统。
/// </summary>
internal static class XjTrueDamageSystem
{
    // 金性妖邪标记
    private const string JinXingYaoXieKey = "xuanjian.vnext.jinxing_yaoxie";
    private const string JinXingYaoXieSourceRealmKey = "xuanjian.vnext.jinxing_yaoxie.source_realm";
    // 不可逆死籍只使用隐藏数据：BaseSystemData 键负责运行态/原生存档，
    // saved_traits 中保留未注册的原始字符串作为第三方单位快照兼容标记。
    // 绝不注册/持有可见 ActorTrait，避免“隐藏 tag”泄露到人物特质栏。
    internal const string JinXingYaoXieDoomTraitId = "XjJinXingYaoXieYinSiDoom";
    // 阴司死印标记（对妖邪的即死标记）
    private const string YinSiDeathSealKey = "xuanjian.vnext.yinsi_death_seal";
    private const int YinSiDeathSealAttackType = 11;
    private const string MadnessTraitId = "madness";
    private const string TantrumStatusId = "tantrum";
    private const float LockedMadStateDurationSeconds = 3600f;
    private const float MinDirectSuppressionDelaySeconds = 5f;
    private const float MaxDirectSuppressionDelaySeconds = 10f;
    private const string DirectSuppressionDelaySalt = "xj.jinxing_yaoxie.direct_suppression_delay";
    private const string JinXingYaoXiePreparedKey = "xuanjian.vnext.jinxing_yaoxie.runtime_prepared_v2";
    private const float DirectSuppressionRetryBaseSeconds = 2f;
    private const float DirectSuppressionRetrySlowSeconds = 15f;
    private const int DirectSuppressionFastRetryCount = 3;

    private struct PendingDirectSuppressionState
    {
        internal float DueTime;
        internal int RetryCount;

        internal PendingDirectSuppressionState(float dueTime, int retryCount)
        {
            DueTime = dueTime;
            RetryCount = retryCount;
        }
    }

    private static readonly Dictionary<long, PendingDirectSuppressionState> PendingDirectSuppressions = new Dictionary<long, PendingDirectSuppressionState>();
    private static readonly List<long> PendingDirectSuppressionReadyIds = new List<long>(4);
    // background lane 的 HasPending 会被高频探测。缓存最早到期时间，避免每帧枚举整张死籍队列。
    private static float _nextDirectSuppressionDueTime = float.PositiveInfinity;

    internal static bool HasPendingDirectSuppression
        => PendingDirectSuppressions.Count > 0
            && UnityEngine.Time.unscaledTime >= _nextDirectSuppressionDueTime;

    // ==================== 金性妖邪 ====================

    internal static bool IsJinXingYaoXie(Actor actor)
    {
        if (actor?.data == null)
            return false;

        // 纯查询：热路径绝不在 predicate 内补 trait / 写 ActorData / 标 dirty。
        // 旧档迁移只允许在 Enforce / EnsureCompanion / prepareForSave 等冷入口执行。
        try
        {
            BaseSystemData data = (BaseSystemData)actor.data;
            data.get(XjActorDataKeys.JinXingYaoXieYinSiDoom, out bool doomed, false);
            if (doomed) return true;

            data.get(JinXingYaoXieKey, out bool isYaoXie, false);
            if (isYaoXie) return true;
        }
        catch (System.Exception xjCaught48)
        {
            XjExceptionDiagnostics.Report("XjTrueDamageSystem.IsJinXingYaoXie.Data", xjCaught48);
        }

        return false;
    }

    /// <summary>
    /// 热路径死籍检查：仅看当前 ActorData。
    /// 不扫描 saved_traits，也不读取任何可见 trait，避免 nextJobActor 对全世界单位产生额外成本。
    /// </summary>
    internal static bool HasIrreversibleYinSiClaimFast(Actor actor)
    {
        if (actor?.data == null) return false;

        try
        {
            BaseSystemData data = (BaseSystemData)actor.data;
            data.get(XjActorDataKeys.JinXingYaoXieYinSiDoom, out bool doomed, false);
            if (doomed) return true;
        }
        catch (System.Exception xjCaughtDoomData)
        {
            XjExceptionDiagnostics.Report("XjTrueDamageSystem.HasIrreversibleYinSiClaimFast.Data", xjCaughtDoomData);
        }

        return false;
    }

    internal static bool HasIrreversibleYinSiClaim(Actor actor)
    {
        if (actor?.data == null) return false;
        if (HasIrreversibleYinSiClaimFast(actor)) return true;

        // saved_traits 只用于读档/第三方保存对象迁移，不允许进入 AI/战斗热路径。
        try
        {
            if (actor.data.saved_traits != null)
            {
                for (int i = 0; i < actor.data.saved_traits.Count; i++)
                {
                    if (string.Equals(actor.data.saved_traits[i], JinXingYaoXieDoomTraitId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }
        catch (System.Exception xjCaughtDoomSavedTrait)
        {
            XjExceptionDiagnostics.Report("XjTrueDamageSystem.HasIrreversibleYinSiClaim.SavedTrait", xjCaughtDoomSavedTrait);
        }

        return false;
    }

    /// <summary>
    /// 在角色重新注册、读档恢复或第三方复活后重新落实阴司死籍。
    /// 如果第三方只复制了隐藏特质而丢掉当前妖邪 ActorData，说明这是旁路复活，直接代码斩杀；
    /// 正常仍存妖邪状态者沿用原本 5~10 秒阴司收束演出。
    /// </summary>
    internal static bool EnforceIrreversibleYinSiClaim(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor) || actor?.data == null) return false;

        bool hasClaim = HasIrreversibleYinSiClaim(actor);
        if (!hasClaim && !TryRecoverLegacyYaoXieDoomFromSnapshot(actor)) return false;

        // 冷入口允许检查/修复 saved_traits，并清除旧版泄露到人物栏的死籍 ActorTrait；
        // 真正的终局仍只入 background.yinsi，不在注册/读档回调栈里同步 die。
        return EnforceKnownIrreversibleYinSiClaim(actor, ensureSavedTrait: true, purgeLegacyVisibleTrait: true);
    }

    /// <summary>
    /// AI / 年度热入口专用。只读取 ActorData 与运行时隐藏 trait，绝不扫描 saved_traits，
    /// 也绝不在调用栈内同步执行死亡。第三方复活出的逃籍对象会被立即加入阴司重终局队列。
    /// </summary>
    internal static bool EnforceIrreversibleYinSiClaimFastRuntime(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor) || actor?.data == null || !HasIrreversibleYinSiClaimFast(actor))
        {
            return false;
        }

        return EnforceKnownIrreversibleYinSiClaim(actor, ensureSavedTrait: false, purgeLegacyVisibleTrait: false);
    }

    private static bool EnforceKnownIrreversibleYinSiClaim(Actor actor, bool ensureSavedTrait, bool purgeLegacyVisibleTrait)
    {
        bool currentMarker = HasCurrentJinXingYaoXieMarker(actor);
        // nextJobActor/updateAge 只需要把已持久化的隐藏死籍状态写回 ActorData 并排队。
        // 不在全局热路径扫描 saved_traits，也不创建任何可见 ActorTrait。
        EnsureIrreversibleYinSiClaimTag(actor, ensureSavedTrait, purgeLegacyVisibleTrait);

        if (!currentMarker)
        {
            XjActorStateWriteGateway.SetExternalBool(
                actor,
                JinXingYaoXieKey,
                true,
                XjActorStateDomain.HighRealm | XjActorStateDomain.Identity | XjActorStateDomain.Progression);

            // 逃籍对象不重新获得修炼窗口，但也不在 nextJobActor/updateAge 里同步杀。
            // 零延迟只表示“下一次 background.yinsi 预算立即处理”，仍遵守一帧一个重终局。
            QueueJinXingYaoXieSuppression(actor, 0f, announce: false);
            return true;
        }

        ScheduleJinXingYaoXieSuppression(actor);
        return true;
    }

    private static bool HasCurrentJinXingYaoXieMarker(Actor actor)
    {
        if (actor?.data == null) return false;
        try
        {
            BaseSystemData data = (BaseSystemData)actor.data;
            data.get(JinXingYaoXieKey, out bool current, false);
            return current;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureIrreversibleYinSiClaimTag(Actor actor, bool ensureSavedTrait = true, bool purgeLegacyVisibleTrait = true)
    {
        if (actor?.data == null) return;

        try
        {
            BaseSystemData data = (BaseSystemData)actor.data;
            data.get(XjActorDataKeys.JinXingYaoXieYinSiDoom, out bool doomed, false);
            if (!doomed)
            {
                XjActorStateWriteGateway.SetExternalBool(
                    actor,
                    XjActorDataKeys.JinXingYaoXieYinSiDoom,
                    true,
                    XjActorStateDomain.HighRealm | XjActorStateDomain.Identity | XjActorStateDomain.Progression);
            }
        }
        catch (System.Exception xjCaughtDoomWrite)
        {
            XjExceptionDiagnostics.Report("XjTrueDamageSystem.EnsureDoomTag.Data", xjCaughtDoomWrite);
        }

        // 旧版本曾把死籍注册为真实 ActorTrait。show_in_meta_editor=false 只隐藏编辑器，
        // 人物面板仍会显示已持有特质。冷入口遇到旧档时直接从运行时 trait 集合移除；
        // saved_traits 的原始字符串仍保留，继续承担第三方快照的隐藏兼容标记。
        if (purgeLegacyVisibleTrait)
        {
            PurgeLegacyVisibleDoomTrait(actor);
        }

        if (ensureSavedTrait)
        {
            TryEnsureSavedTrait(actor, JinXingYaoXieDoomTraitId);
        }
    }

    private static void PurgeLegacyVisibleDoomTrait(Actor actor)
    {
        if (actor?.data == null || actor.traits == null) return;
        try
        {
            List<ActorTrait> remove = null;
            foreach (ActorTrait trait in actor.traits)
            {
                if (trait != null && string.Equals(trait.id, JinXingYaoXieDoomTraitId, StringComparison.Ordinal))
                {
                    remove ??= new List<ActorTrait>(1);
                    remove.Add(trait);
                }
            }
            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++) actor.traits.Remove(remove[i]);
            actor.clearTraitCache();
        }
        catch (System.Exception xjCaughtLegacyVisibleDoom)
        {
            XjExceptionDiagnostics.Report("XjTrueDamageSystem.PurgeLegacyVisibleDoomTrait", xjCaughtLegacyVisibleDoom);
        }
    }

    private static bool TryRecoverLegacyYaoXieDoomFromSnapshot(Actor actor)
    {
        if (actor?.data == null) return false;

        string actorName;
        try { actorName = actor.getName() ?? string.Empty; }
        catch { actorName = string.Empty; }
        if (!actorName.StartsWith("金性妖邪-", StringComparison.Ordinal)) return false;

        // 旧妖邪固定带疯狂；加这一层避免普通玩家仅改同名就被误判。
        try
        {
            if (!actor.hasTrait(MadnessTraitId)) return false;
        }
        catch
        {
            return false;
        }

        try
        {
            XjActorStateWriteGateway.SetExternalBool(
                actor,
                JinXingYaoXieKey,
                true,
                XjActorStateDomain.HighRealm | XjActorStateDomain.Identity | XjActorStateDomain.Progression);
            XjActorStateWriteGateway.SetExternalString(
                actor,
                JinXingYaoXieSourceRealmKey,
                ResolveJinXingYaoXieRealm(actor),
                XjActorStateDomain.HighRealm | XjActorStateDomain.Identity | XjActorStateDomain.Progression);
            EnsureIrreversibleYinSiClaimTag(actor);
            return true;
        }
        catch (System.Exception xjCaughtLegacyDoom)
        {
            XjExceptionDiagnostics.Report("XjTrueDamageSystem.LegacyDoomRecovery", xjCaughtLegacyDoom);
            return false;
        }
    }

    internal static bool MarkAsJinXingYaoXie(Actor actor)
    {
        return MarkAsJinXingYaoXieCore(actor, bindYinSi: true);
    }

    internal static bool MarkAsJinXingYaoXieDebugOnly(Actor actor)
    {
        return MarkAsJinXingYaoXieCore(actor, bindYinSi: false);
    }

    private static bool MarkAsJinXingYaoXieCore(Actor actor, bool bindYinSi)
    {
        if (actor?.data == null || !XjRuntimeSettings.SpawnJinXingYaoXieEnabled)
            return false;

        try
        {
            string sourceRealmId = ResolveJinXingYaoXieRealm(actor);
            using XjActorStateRevisionStore.ReductionScope reduction = XjActorStateRevisionStore.BeginReduction(((BaseSystemData)actor.data).id);
            XjActorStateWriteGateway.SetExternalBool(
                actor,
                JinXingYaoXieKey,
                true,
                XjActorStateDomain.HighRealm | XjActorStateDomain.Identity | XjActorStateDomain.Progression);
            XjActorStateWriteGateway.SetExternalString(
                actor,
                JinXingYaoXieSourceRealmKey,
                sourceRealmId,
                XjActorStateDomain.HighRealm | XjActorStateDomain.Identity | XjActorStateDomain.Progression);
            EnsureIrreversibleYinSiClaimTag(actor);
            // 原生文明/存档关系只在真正 prepareForSave 时修复。
            // 妖邪化当帧禁止为了“存档安全”新建国家/城市，避免把求金失败放大为世界结构重建峰值。
            XjActorAggroBridge.ClearTargets(actor);
            PurgeJinXingYaoXieCultivationPath(actor, sourceRealmId);
            XjRuntimeActorInterestIndex.Observe(actor);
            XjCombatHotPathCache.Refresh(actor);
            EnsureJinXingYaoXieMadState(actor, true);
            MarkJinXingYaoXiePrepared(actor);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void EnsureJinXingYaoXieCompanion(Actor actor)
    {
        if (actor?.data == null || !IsJinXingYaoXie(actor))
            return;

        // 新生成妖邪在 MarkAsJinXingYaoXieCore 已完成一次完整清理。
        // 年度管线不应每年重复 Realm/GongFa/XianJi/trait cache 全套重建；
        // 只有旧档或第三方恢复后缺少 prepared 标记时才做一次兼容修复。
        if (!HasIrreversibleYinSiClaimFast(actor))
        {
            EnsureIrreversibleYinSiClaimTag(actor);
        }

        bool prepared = IsJinXingYaoXiePrepared(actor);
        if (prepared && IsDirectSuppressionQueued(actor))
        {
            return;
        }

        if (!prepared)
        {
            PurgeJinXingYaoXieCultivationPath(actor, ResolveJinXingYaoXieRealm(actor));
            EnsureJinXingYaoXieMadState(actor, true);
            MarkJinXingYaoXiePrepared(actor);
        }
        else
        {
            EnsureJinXingYaoXieMadState(actor);
        }

        ScheduleJinXingYaoXieSuppression(actor);
    }

    private static bool IsDirectSuppressionQueued(Actor actor)
    {
        if (actor?.data == null) return false;
        try
        {
            long actorId = ((BaseSystemData)actor.data).id;
            return actorId > 0L && PendingDirectSuppressions.ContainsKey(actorId);
        }
        catch
        {
            return false;
        }
    }

    internal static void ForgetActorRuntime(long actorId)
    {
        if (actorId <= 0L || !PendingDirectSuppressions.Remove(actorId)) return;
        RecomputeNextDirectSuppressionDueTime();
    }

    internal static void ClearRuntime()
    {
        PendingDirectSuppressions.Clear();
        PendingDirectSuppressionReadyIds.Clear();
        _nextDirectSuppressionDueTime = float.PositiveInfinity;
    }

    private static bool IsJinXingYaoXiePrepared(Actor actor)
    {
        if (actor?.data == null) return false;
        try
        {
            ((BaseSystemData)actor.data).get(JinXingYaoXiePreparedKey, out bool prepared, false);
            return prepared;
        }
        catch
        {
            return false;
        }
    }

    private static void MarkJinXingYaoXiePrepared(Actor actor)
    {
        if (actor?.data == null || IsJinXingYaoXiePrepared(actor)) return;
        XjActorStateWriteGateway.SetExternalBool(
            actor,
            JinXingYaoXiePreparedKey,
            true,
            XjActorStateDomain.HighRealm | XjActorStateDomain.Identity | XjActorStateDomain.Progression);
    }

    internal static void EnsureJinXingYaoXieNativeSaveStateForSave(Actor actor)
    {
        if (actor?.data == null || !IsJinXingYaoXie(actor))
            return;

        // 保存入口只保证不可逆死籍能被 ActorData/saved_traits 持久化。
        // 禁止在 prepareForSave 期间创建国家/城市或重挂文明关系；序列化过程中改世界结构
        // 会把一次阴司事件放大成城市/王国/军队重建峰值。损坏 civ 引用由 saveKingdomCiv
        // 的妖邪专用短路按“无文明归属”保存。
        EnsureIrreversibleYinSiClaimTag(actor);
    }

    internal static bool ShouldLockJinXingYaoXieMadness(Actor actor)
    {
        return XjSafeCore.IsAliveActor(actor) && IsJinXingYaoXie(actor);
    }

    internal static void EnsureJinXingYaoXieMadState(Actor actor, bool force = false)
    {
        if (!ShouldLockJinXingYaoXieMadness(actor))
        {
            return;
        }

        EnsureMadnessTrait(actor);
        try
        {
            if (!force && actor.hasStatus(TantrumStatusId))
            {
                return;
            }

            StatusAsset tantrum = AssetManager.status.get(TantrumStatusId);
            if (tantrum != null)
            {
                actor.addStatusEffect(tantrum, LockedMadStateDurationSeconds, true);
            }
        }
        catch (System.Exception xjCaught139) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:139", xjCaught139); }
    }

    private static void EnsureMadnessTrait(Actor actor)
    {
        if (!ShouldLockJinXingYaoXieMadness(actor))
        {
            return;
        }

        try
        {
            if (actor.hasTrait(MadnessTraitId))
            {
                return;
            }

            ActorTrait trait = AssetManager.traits.get(MadnessTraitId);
            if (trait == null)
            {
                return;
            }

            if (!trait.affects_mind || !actor.hasTag("strong_mind"))
            {
                actor.addTrait(trait, true);
                return;
            }

            // 紫府通常拥有强心智标签；金性妖邪必须绕过该免疫并永久维持疯狂。
            if (trait.traits_to_remove != null)
            {
                actor.removeTraits(trait.traits_to_remove);
            }
            actor.removeOppositeTraits(trait);
            actor.traits.Add(trait);
            trait.action_on_augmentation_add?.Invoke(actor, trait);
            actor.setStatsDirty();
            actor.clearTraitCache();
        }
        catch (System.Exception xjCaught181) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:181", xjCaught181); }
    }

    /// <summary>
    /// 金性妖邪伤害倍率。
    /// 阴司已改为非实体规则结算，不再存在绑定阴司的特殊攻击倍率。
    /// </summary>
    internal static float GetJinXingYaoXieDamageMultiplier(Actor target, Actor attacker)
    {
        if (!IsJinXingYaoXie(target))
            return 1f;

        return 0.2f;
    }

    // ==================== 阴司系统 ====================

    /// <summary>
    /// 判断 Actor 是否为阴司（执掌死亡的修士）
    /// </summary>
    internal static bool IsYinSi(Actor actor)
    {
        return XjYinSiTraitLifecycle.IsYinSi(actor);
    }


    internal static bool IsAuthorizedLivingJinDanPursuit(Actor target, Actor attacker)
    {
        return false;
    }

    internal static void NotifyYinSiCombatContact(Actor attacker, Actor defender)
    {
        // 阴司不再显化为可战斗实体，战斗接触不再作为追索来源。
    }

    /// <summary>
    /// 旧版阴司实体死印斩杀已停用；金性妖邪由延迟规则结算处理。
    /// </summary>
    internal static bool TryExecuteYinSiDeathSeal(Actor target, Actor attacker)
    {
        return false;
    }

    internal static bool ScheduleJinXingYaoXieSuppression(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor) || actor?.data == null || !IsJinXingYaoXie(actor))
        {
            return false;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return false;

        // 已在队列中视作已经绑定成功，避免高倍速下每个游戏年都重新补 trait / saved_traits / 公告。
        if (PendingDirectSuppressions.ContainsKey(actorId)) return true;
        if (!HasIrreversibleYinSiClaimFast(actor))
        {
            EnsureIrreversibleYinSiClaimTag(actor);
        }
        return QueueJinXingYaoXieSuppression(actor, ResolveSuppressionDelay(actorId), announce: true);
    }

    private static bool QueueJinXingYaoXieSuppression(Actor actor, float delaySeconds, bool announce)
    {
        if (!XjSafeCore.IsAliveActor(actor) || actor?.data == null || !IsJinXingYaoXie(actor))
        {
            return false;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return false;
        if (PendingDirectSuppressions.ContainsKey(actorId)) return true;

        float dueTime = UnityEngine.Time.unscaledTime + Math.Max(0f, delaySeconds);
        PendingDirectSuppressions[actorId] = new PendingDirectSuppressionState(dueTime, 0);
        if (dueTime < _nextDirectSuppressionDueTime)
        {
            _nextDirectSuppressionDueTime = dueTime;
        }

        if (announce)
        {
            string yaoXieName;
            try { yaoXieName = actor.getName() ?? "金性妖邪"; }
            catch { yaoXieName = "金性妖邪"; }

            string pendingText = "【阴司降世】两名阴司早得金性异动之讯，循迹而来，已锁定【"
                + XjStringHelper.DisplayNameWithoutRealmSuffix(yaoXieName, "金性妖邪") + "】。";
            XjBroadcastSystem.BroadcastSLevelWorldEvent(
                pendingText,
                pendingText,
                "#73506E",
                8f,
                XjEventIconCatalog.YinSiAppear);
        }

        return true;
    }

    /// <summary>
    /// 阴司代码杀改由 background.yinsi 以未缩放时间执行。
    /// 每次最多处理 budget 个对象，避免 20x/40x 倍速把多个 WaitForSeconds 回调压进同一帧。
    /// 返回本轮真正尝试过的终局数量，供共享 YinSi lane 做总预算。
    /// </summary>
    internal static int TickPendingDirectSuppressions(int budget = 1)
    {
        if (budget <= 0 || PendingDirectSuppressions.Count == 0) return 0;

        float now = UnityEngine.Time.unscaledTime;
        PendingDirectSuppressionReadyIds.Clear();
        foreach (KeyValuePair<long, PendingDirectSuppressionState> pair in PendingDirectSuppressions)
        {
            if (pair.Value.DueTime > now) continue;
            PendingDirectSuppressionReadyIds.Add(pair.Key);
            if (PendingDirectSuppressionReadyIds.Count >= budget) break;
        }

        int processed = 0;
        for (int i = 0; i < PendingDirectSuppressionReadyIds.Count; i++)
        {
            long actorId = PendingDirectSuppressionReadyIds[i];
            if (!PendingDirectSuppressions.TryGetValue(actorId, out PendingDirectSuppressionState state)) continue;

            if (!XjScheduler.ResolveActor(actorId, out Actor current) || !XjSafeCore.IsAliveActor(current))
            {
                PendingDirectSuppressions.Remove(actorId);
                continue;
            }

            processed++;
            // 前三次只走统一死亡仲裁。反射硬移除属于最后兜底，不能和首次失败的半完成
            // death pipeline 叠在同一帧，否则第三方死亡替身很容易形成重入/双清理。
            bool allowHardRemovalFallback = state.RetryCount >= DirectSuppressionFastRetryCount;
            if (SuppressJinXingYaoXieDirectly(current, allowHardRemovalFallback) || !XjSafeCore.IsAliveActor(current))
            {
                PendingDirectSuppressions.Remove(actorId);
                continue;
            }

            // 第三方死亡保护/替身若暂时拦截终局，只做有界慢重试，不重新广播、不忙等。
            state.RetryCount++;
            float backoff = state.RetryCount <= DirectSuppressionFastRetryCount
                ? DirectSuppressionRetryBaseSeconds * state.RetryCount
                : DirectSuppressionRetrySlowSeconds;
            state.DueTime = now + backoff;
            PendingDirectSuppressions[actorId] = state;
        }
        PendingDirectSuppressionReadyIds.Clear();
        RecomputeNextDirectSuppressionDueTime();
        return processed;
    }

    private static void RecomputeNextDirectSuppressionDueTime()
    {
        float next = float.PositiveInfinity;
        foreach (PendingDirectSuppressionState state in PendingDirectSuppressions.Values)
        {
            if (state.DueTime < next) next = state.DueTime;
        }
        _nextDirectSuppressionDueTime = next;
    }

    internal static void OnJinXingYaoXieDied(Actor actor)
    {
        if (actor?.data == null) return;
        try
        {
            long actorId = ((BaseSystemData)actor.data).id;
            if (actorId <= 0L || !PendingDirectSuppressions.Remove(actorId)) return;
            RecomputeNextDirectSuppressionDueTime();
        }
        catch
        {
            // 死亡清理必须 best-effort，不能反向阻断原生 death。
        }
    }

    private static float ResolveSuppressionDelay(long actorId)
    {
        int hundredths = XjDeterministicHash.PositiveIndex(actorId, DirectSuppressionDelaySalt, 501);
        return MinDirectSuppressionDelaySeconds
            + (MaxDirectSuppressionDelaySeconds - MinDirectSuppressionDelaySeconds) * hundredths / 500f;
    }

    private static bool SuppressJinXingYaoXieDirectly(Actor actor, bool allowHardRemovalFallback)
    {
        if (!XjSafeCore.IsAliveActor(actor) || actor?.data == null || !IsJinXingYaoXie(actor))
        {
            return false;
        }

        string yaoXieName;
        try { yaoXieName = actor.getName() ?? "金性妖邪"; }
        catch { yaoXieName = "金性妖邪"; }

        try
        {
            XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, "YinSiSuppressed");
            bool died = XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)YinSiDeathSealAttackType, false, XuanJianVNext.Systems.Death.XjDeathCause.YinSi);
            if (!died && allowHardRemovalFallback && XjSafeCore.IsAliveActor(actor))
            {
                XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
                died = !XjSafeCore.IsAliveActor(actor);
            }

            if (!died)
            {
                XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
                return false;
            }

            int year = World.world?.map_stats?.year ?? 0;
            XjChronicleWriter.RecordJinXingYaoXieSuppressed(actor, year, "阴司");
            string announcement = XjAnnouncementText.BuildYinSiSuppressedJinXingYaoXie(yaoXieName, "阴司");
            XjBroadcastSystem.BroadcastSLevelWorldEvent(
                announcement,
                announcement,
                "#73506E",
                9f,
                XjEventIconCatalog.YinSiLeave);
            return true;
        }
        catch
        {
            try { XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty); } catch (System.Exception xjCaught319) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:319", xjCaught319); }
            return false;
        }
    }

    private static void PurgeJinXingYaoXieCultivationPath(Actor actor, string sourceRealmId)
    {
        if (actor?.data == null)
        {
            return;
        }

        string targetRealmId = NormalizeJinXingYaoXieRealm(sourceRealmId);
        bool isJinDanOrAbove = XjRealmHelper.GetOrder(targetRealmId) >= XjRealmHelper.GetOrder(XjRealmIds.JinDan);
        if (!XjCultivationStateTransitions.TrySetRealm(actor, targetRealmId, syncVisibleTraits: false))
        {
            // Preserve the original corruption-tolerant behavior, but still bring
            // XianJi and the real GongFa collection back to the target realm limit.
            XjCultivationStateTransitions.RestoreRealmMetadataOnlyForRecovery(
                actor, targetRealmId, "JinXingYaoXie");
            if (!isJinDanOrAbove)
            {
                XjShenDanAccessor.ClearSuccess(actor);
                XjJinDanAccessor.ClearSuccess(actor);
            }
        }

        // 紫府求金失败化妖邪时，终止求金链；已经成就金丹者转为金性妖邪时必须保留金丹身份与金性。
        if (!isJinDanOrAbove)
        {
            XjQiuJinFaAccessor.Clear(actor, "JinXingYaoXieBelowJinDan");
            XjJinDanAccessor.ClearSuccess(actor);
        }

        EnsureJinXingYaoXieRealmTrait(actor, targetRealmId);
        try { XjVisibleTraitSync.SyncCultivationTraits(actor); } catch (System.Exception xjCaught356) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:356", xjCaught356); }
        // TrySetRealm normally refreshes every combat index. The corruption-tolerant
        // fallback above writes RealmId directly, so close that bypass here as well;
        // otherwise a ZiFu/JinDan yao xie may keep its old cached tier until a later
        // lifecycle sweep and temporarily participate in combat at the wrong realm.
        try { XjCultivatorCache.CheckAndUpdate(actor); } catch (System.Exception xjCaught361) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:361", xjCaught361); }
        try { XjCombatHotPathCache.Refresh(actor); } catch (System.Exception xjCaught362) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:362", xjCaught362); }
        try { actor.setStatsDirty(); } catch (System.Exception xjCaught363) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:363", xjCaught363); }
        try { actor.clearTraitCache(); } catch (System.Exception xjCaught364) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:364", xjCaught364); }
    }

    private static string ResolveJinXingYaoXieRealm(Actor actor)
    {
        if (actor?.data == null)
        {
            return XjRealmIds.ZiFu;
        }

        if (XjActorAccessor.TryGetString(actor, JinXingYaoXieSourceRealmKey, out string storedRealmId)
            && IsSupportedJinXingYaoXieRealm(storedRealmId))
        {
            return NormalizeJinXingYaoXieRealm(storedRealmId);
        }

        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
        {
            string normalizedStored = XjRealmHelper.NormalizeId(realmId);
            if (!string.IsNullOrWhiteSpace(normalizedStored))
            {
                // Existing authority wins even if corruption left a stale higher realm trait.
                // JinXingYaoXie supports ZiFu+ only; an invalid lower stored realm degrades to
                // the safe ZiFu corruption floor instead of being promoted by its projection.
                return IsSupportedJinXingYaoXieRealm(normalizedStored)
                    ? NormalizeJinXingYaoXieRealm(normalizedStored)
                    : XjRealmIds.ZiFu;
            }
        }

        try
        {
            if (actor.hasTrait(XjRealmIds.ShenDan)) return XjRealmIds.ShenDan;
            if (actor.hasTrait(XjRealmIds.JinDan)) return XjRealmIds.JinDan;
            if (actor.hasTrait(XjRealmIds.ZiFu)) return XjRealmIds.ZiFu;
        }
        catch (System.Exception xjCaught392) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:392", xjCaught392); }

        return XjRealmIds.ZiFu;
    }

    private static bool IsSupportedJinXingYaoXieRealm(string realmId)
    {
        string normalized = NormalizeJinXingYaoXieRealm(realmId);
        return string.Equals(normalized, XjRealmIds.ZiFu, StringComparison.Ordinal)
            || string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
            || string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal);
    }

    private static string NormalizeJinXingYaoXieRealm(string realmId)
    {
        string normalized = XjRealmHelper.NormalizeId(realmId);
        if (string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
            || string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal))
        {
            return normalized;
        }

        return XjRealmIds.ZiFu;
    }

    private static void EnsureJinXingYaoXieRealmTrait(Actor actor, string realmId)
    {
        if (actor?.data == null)
        {
            return;
        }

        string targetRealmId = NormalizeJinXingYaoXieRealm(realmId);
        string targetVisibleTraitId = string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
            ? XjRealmIds.ZiFu
            : XjRealmIds.JinDan;

        TryRemoveTraitIfDifferent(actor, XjRealmIds.ZiFu, targetVisibleTraitId);
        TryRemoveTraitIfDifferent(actor, XjRealmIds.JinDan, targetVisibleTraitId);
        TryRemoveTraitIfDifferent(actor, XjRealmIds.ShenDan, targetVisibleTraitId);
        TryAddTrait(actor, targetVisibleTraitId);

        try
        {
            if (actor.data.saved_traits == null)
            {
                return;
            }

            for (int i = actor.data.saved_traits.Count - 1; i >= 0; i--)
            {
                string traitId = actor.data.saved_traits[i];
                if ((string.Equals(traitId, XjRealmIds.ZiFu, StringComparison.Ordinal)
                        || string.Equals(traitId, XjRealmIds.JinDan, StringComparison.Ordinal)
                        || string.Equals(traitId, XjRealmIds.ShenDan, StringComparison.Ordinal))
                    && !string.Equals(traitId, targetVisibleTraitId, StringComparison.Ordinal))
                {
                    actor.data.saved_traits.RemoveAt(i);
                }
            }
        }
        catch (System.Exception xjCaught455) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:455", xjCaught455); }

        TryEnsureSavedTrait(actor, targetVisibleTraitId);
    }

    private static void TryRemoveTraitIfDifferent(Actor actor, string traitId, string targetTraitId)
    {
        if (!string.Equals(traitId, targetTraitId, StringComparison.Ordinal))
        {
            TryRemoveTrait(actor, traitId);
        }
    }

    private static void TryRemoveTrait(Actor actor, string traitId)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(traitId))
        {
            return;
        }

        try
        {
            if (actor.hasTrait(traitId))
            {
                actor.removeTrait(traitId);
            }
        }
        catch (System.Exception xjCaught484) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:484", xjCaught484); }
    }

    private static void TryAddTrait(Actor actor, string traitId)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(traitId))
        {
            return;
        }

        try
        {
            if (!actor.hasTrait(traitId))
            {
                actor.addTrait(traitId, false);
            }
        }
        catch (System.Exception xjCaught503) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:503", xjCaught503); }
    }

    private static void TryEnsureSavedTrait(Actor actor, string traitId)
    {
        if (actor?.data?.saved_traits == null || string.IsNullOrWhiteSpace(traitId))
        {
            return;
        }

        try
        {
            for (int i = 0; i < actor.data.saved_traits.Count; i++)
            {
                if (string.Equals(actor.data.saved_traits[i], traitId, StringComparison.Ordinal))
                {
                    return;
                }
            }

            actor.data.saved_traits.Add(traitId);
        }
        catch (System.Exception xjCaught527) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjTrueDamageSystem.cs:527", xjCaught527); }
    }


}


