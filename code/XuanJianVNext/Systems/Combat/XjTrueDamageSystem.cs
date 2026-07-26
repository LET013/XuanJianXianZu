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

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 金性妖邪 + 阴司死印系统。
/// </summary>
internal static class XjTrueDamageSystem
{
    // 金性妖邪标记
    private const string JinXingYaoXieKey = "xuanjian.vnext.jinxing_yaoxie";
    private const string JinXingYaoXieSourceRealmKey = "xuanjian.vnext.jinxing_yaoxie.source_realm";
    // 阴司死印标记（对妖邪的即死标记）
    private const string YinSiDeathSealKey = "xuanjian.vnext.yinsi_death_seal";
    private const int YinSiDeathSealAttackType = 11;
    private const string MadnessTraitId = "madness";
    private const string TantrumStatusId = "tantrum";
    private const float LockedMadStateDurationSeconds = 3600f;
    private const float MinDirectSuppressionDelaySeconds = 5f;
    private const float MaxDirectSuppressionDelaySeconds = 10f;
    private const string DirectSuppressionDelaySalt = "xj.jinxing_yaoxie.direct_suppression_delay";
    private static readonly HashSet<long> PendingDirectSuppressionActorIds = new HashSet<long>();

    // ==================== 金性妖邪 ====================

    internal static bool IsJinXingYaoXie(Actor actor)
    {
        if (actor?.data == null)
            return false;

        try
        {
            BaseSystemData data = (BaseSystemData)actor.data;
            data.get(JinXingYaoXieKey, out bool isYaoXie, false);
            return isYaoXie;
        }
        catch { }

        return false;
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
            BaseSystemData data = (BaseSystemData)actor.data;
            string sourceRealmId = ResolveJinXingYaoXieRealm(actor);
            data.set(JinXingYaoXieKey, true);
            data.set(JinXingYaoXieSourceRealmKey, sourceRealmId);
            EnsureJinXingYaoXieNativeSaveState(actor);
            XjActorAggroBridge.ClearTargets(actor);
            PurgeJinXingYaoXieCultivationPath(actor, sourceRealmId);
            XjRuntimeActorInterestIndex.Observe(actor);
            XjCombatHotPathCache.Refresh(actor);
            EnsureJinXingYaoXieMadState(actor, true);
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

        EnsureJinXingYaoXieMadState(actor);
        EnsureJinXingYaoXieNativeSaveState(actor);
        PurgeJinXingYaoXieCultivationPath(actor, ResolveJinXingYaoXieRealm(actor));
        EnsureJinXingYaoXieMadState(actor);
        ScheduleJinXingYaoXieSuppression(actor);
    }

    internal static void ClearRuntime()
    {
        PendingDirectSuppressionActorIds.Clear();
    }

    internal static void EnsureJinXingYaoXieNativeSaveStateForSave(Actor actor)
    {
        if (actor?.data == null || !IsJinXingYaoXie(actor))
            return;

        EnsureJinXingYaoXieNativeSaveState(actor);
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
        catch
        {
        }
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
        catch
        {
        }
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
        if (actorId <= 0L || !PendingDirectSuppressionActorIds.Add(actorId))
        {
            return false;
        }

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

        float delay = ResolveSuppressionDelay(actorId);
        if (!XjJinDanCombatApi.TryScheduleImpact(delay, () =>
            {
                PendingDirectSuppressionActorIds.Remove(actorId);
                if (XjScheduler.ResolveActor(actorId, out Actor current))
                {
                    SuppressJinXingYaoXieDirectly(current);
                }
            },
            "xj_jinxing_yaoxie_suppression",
            out _))
        {
            PendingDirectSuppressionActorIds.Remove(actorId);
            return SuppressJinXingYaoXieDirectly(actor);
        }

        return true;
    }

    private static float ResolveSuppressionDelay(long actorId)
    {
        int hundredths = XjDeterministicHash.PositiveIndex(actorId, DirectSuppressionDelaySalt, 501);
        return MinDirectSuppressionDelaySeconds
            + (MaxDirectSuppressionDelaySeconds - MinDirectSuppressionDelaySeconds) * hundredths / 500f;
    }

    private static bool SuppressJinXingYaoXieDirectly(Actor actor)
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
            bool died = XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)YinSiDeathSealAttackType, false);
            if (!died && XjSafeCore.IsAliveActor(actor))
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
            try { XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty); } catch { }
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
            XjActorAccessor.SetString(actor, XjActorDataKeys.RealmId, targetRealmId);
            if (!isJinDanOrAbove)
            {
                XjShenDanAccessor.ClearSuccess(actor);
                XjJinDanAccessor.ClearSuccess(actor);
            }
            XjXianJiAccessor.ReconcileRealmLimit(actor);
            XjActorGongFaCollection.ReconcileWithActor(actor, "JinXingYaoXieRealmFallback");
            XjActorGongFaCollection.ReconcileGradeCap(actor, "JinXingYaoXieRealmFallbackGradeCap");
        }

        // 紫府求金失败化妖邪时，终止求金链；已经成就金丹者转为金性妖邪时必须保留金丹身份与金性。
        if (!isJinDanOrAbove)
        {
            XjQiuJinFaAccessor.Clear(actor, "JinXingYaoXieBelowJinDan");
            XjJinDanAccessor.ClearSuccess(actor);
        }

        EnsureJinXingYaoXieRealmTrait(actor, targetRealmId);
        try { XjVisibleTraitSync.SyncCultivationTraits(actor); } catch { }
        // TrySetRealm normally refreshes every combat index. The corruption-tolerant
        // fallback above writes RealmId directly, so close that bypass here as well;
        // otherwise a ZiFu/JinDan yao xie may keep its old cached tier until a later
        // lifecycle sweep and temporarily participate in combat at the wrong realm.
        try { XjCultivatorCache.CheckAndUpdate(actor); } catch { }
        try { XjCombatHotPathCache.Refresh(actor); } catch { }
        try { actor.setStatsDirty(); } catch { }
        try { actor.clearTraitCache(); } catch { }
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

        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            && IsSupportedJinXingYaoXieRealm(realmId))
        {
            return NormalizeJinXingYaoXieRealm(realmId);
        }

        try
        {
            if (actor.hasTrait(XjRealmIds.ShenDan)) return XjRealmIds.ShenDan;
            if (actor.hasTrait(XjRealmIds.JinDan)) return XjRealmIds.JinDan;
            if (actor.hasTrait(XjRealmIds.ZiFu)) return XjRealmIds.ZiFu;
        }
        catch
        {
        }

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
        catch
        {
        }

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
        catch
        {
        }
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
        catch
        {
        }
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
        catch
        {
        }
    }

    private static void EnsureJinXingYaoXieNativeSaveState(Actor actor)
    {
        if (actor?.data == null)
        {
            return;
        }

        try
        {
            if (actor.isKingdomCiv())
            {
                if (!XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(actor))
                {
                    XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
                }
                return;
            }
        }
        catch
        {
        }

        try { actor.setCity(null); } catch { }
        try { actor.setKingdom(null); } catch { }
        try
        {
            actor.data.cityID = -1L;
            actor.data.civ_kingdom_id = -1L;
            actor.data.culture = -1L;
            actor.data.clan = -1L;
            actor.data.family = -1L;
            actor.data.homeBuildingID = -1L;
            actor.data.transportID = -1L;
        }
        catch
        {
        }
    }

}


