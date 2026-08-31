using System;
using XuanJianVNext.Core;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Events;

/// <summary>
/// WorldBox/Harmony 与玄鉴业务之间的单一事件边界。补丁只上报原生事实；
/// 资格索引、分角色Revision、年度调度与读档初始化由这里归并分发。
/// </summary>
internal static class XjInternalEventBus
{
    internal static void PublishActorDomainChanged(
        Actor actor,
        XjActorStateDomain domains,
        string reason = "")
    {
        if (actor?.data == null || domains == XjActorStateDomain.None) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;
        XjActorStateRevisionStore.Mark(actorId, domains);
        if (MayAffectRuntimeInterest(domains))
        {
            XjRuntimeActorInterestIndex.InvalidateAnnualClassification(actorId);
        }
    }

    internal static void PublishActorDataChanged(Actor actor, string key)
    {
        if (actor?.data == null) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;

        XjActorStateDomain domains = XjActorStateRevisionStore.ResolveDomainForKey(key);
        XjActorStateRevisionStore.Mark(actorId, domains);
        if (MayAffectRuntimeInterest(domains))
        {
            XjRuntimeActorInterestIndex.InvalidateAnnualClassification(actorId);
        }
        XjCultivatorCandidateIndex.NotifyActorDataChanged(actor, key);
        if (string.Equals(key, XjActorDataKeys.RealmId, StringComparison.Ordinal))
        {
            // High-realm army exclusion is event-driven: a character is demobilized
            // once when the authoritative realm crosses into ZiFu+, while future native
            // recruitment is rejected by City.checkCanMakeWarrior. Never annual-sweep Army.
            XjHighRealmArmyExclusion.EnqueueIfHighRealm(actor);
        }
    }

    internal static void PublishActorTraitGrantCompleted(Actor actor, string traitId, bool result, string previousRealmState)
    {
        // AVBS复制/变形期间的addTrait只是把旧单位数据搬到新单位，不能解释为
        // 玩家手动授予境界、资质、出身或百艺，否则会在半构造Actor上重入状态机。
        if (XjExternalUnitTransferContext.IsTraitTransferActive) return;

        XjShiTraitReconciliation.HandleGranted(actor, traitId, result);
        // Projection traits are normally one-way (authority -> visible trait). Only an
        // explicit ActorTraitsEditor transaction may interpret a visible cultivation
        // trait as a player command and write authoritative realm/DaoTu state.
        if (XjManualTraitEditContext.IsActive)
        {
            XjManualRealmTraitReconciliation.HandleGranted(actor, traitId, result, previousRealmState);
        }
        XjCraftTraitRules.HandleTraitGranted(actor, traitId, result);
        if (result || (actor?.data != null && !string.IsNullOrWhiteSpace(traitId) && actor.hasTrait(traitId)))
        {
            PublishActorTraitChanged(actor, traitId, added: true);
        }
    }

    internal static void PublishActorTraitRemovalCompleted(Actor actor, string traitId, bool removed)
    {
        if (XjExternalUnitTransferContext.IsTraitTransferActive) return;

        XjShiTraitReconciliation.HandleRemoved(actor, traitId, removed);
        if (XjManualTraitEditContext.IsActive)
        {
            XjManualRealmTraitReconciliation.HandleRemoved(actor, traitId, removed);
        }
        if (removed) PublishActorTraitChanged(actor, traitId, added: false);
    }

    internal static void PublishActorTraitChanged(Actor actor, string traitId, bool added)
    {
        if (XjExternalUnitTransferContext.IsTraitTransferActive) return;
        if (actor?.data == null || string.IsNullOrWhiteSpace(traitId)) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;

        XjActorStateDomain domains = ResolveTraitDomains(traitId);
        XjActorStateRevisionStore.Mark(actorId, domains);
        XjRuntimeActorInterestIndex.InvalidateAnnualClassification(actorId);
        XjRuntimeActorInterestIndex.InvalidateCombatClassification(actor);
        XjCultivatorCandidateIndex.MarkProgressionDirty(actor);
    }

    internal static void PublishNativeEquipmentSlotChanged(
        ActorEquipmentSlot slot,
        Item item,
        Actor actor)
    {
        if (slot == null || actor?.data == null) return;
        PublishActorDomainChanged(
            actor,
            XjActorStateDomain.Equipment | XjActorStateDomain.Craft,
            "native.equipment_slot");

        if (item == null || !ReferenceEquals(slot.getItem(), item)) return;
        XjEquipmentForgeConsumer.ClaimUnownedFaBao(item, actor);
        int year = 0;
        try { year = World.world?.map_stats?.year ?? 0; }
        catch (Exception exception)
        {
            XjExceptionDiagnostics.Report("XjInternalEventBus.PublishNativeEquipmentSlotChanged", exception);
        }
        XjWeaponArtSystem.ObserveEquippedWeapon(actor, item, year);
    }

    internal static void PublishSimulationTick()
    {
        // All non-critical runtime work now enters through XjRuntimeCadence so x20/x40
        // cannot consume the same lane repeatedly inside one rendered Unity frame.
        XjRuntimeCadence.Tick();
    }

    internal static void PublishActorAnnualBoundary(Actor actor)
    {
        if (actor?.data == null) return;

        // Ordinary actors are the dominant population. Once an actor has been
        // negatively classified, the annual Harmony callback must not re-enter
        // mortality/cultivation routing every year. Ages five/six stay open for the
        // one-time aptitude window, and 80+ stays open for the mortal hard cap.
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId > 0L && XjRuntimeActorInterestIndex.HasOrdinaryAnnualClassification(actorId))
        {
            int ageYear = (int)System.Math.Floor(System.Math.Max(0f, actor.getAge()));
            if (!XjAptitudeRuleEvaluator.IsAgeFiveEligibilityWindow(ageYear) && ageYear < 80)
            {
                return;
            }
        }

        // addTrait is already blocked at the native boundary and managed actors are
        // reconciled by visible-trait sync. Do not call hasTrait("long_liver") for
        // every civilian every year in high-population worlds.
        if (XjVanillaDeathGuard.EnforceMortalCivilianLifespanLimit(actor)) return;
        XjScheduler.RegisterAndEnqueueAnnualActor(actor);
    }



    internal static void PublishWorldLoaded()
    {
        XjRuntimeSettings.LoadFromModConfig(XuanJianVNext.XuanJianMod.I?.GetConfig());
        XjYearTracker.Refresh();
        int currentYear = XjYearTracker.CurrentYear;

        // finishingUpLoading is only the native boundary. Some WorldBox versions
        // attach map_stats.custom_data afterwards, so feature-level load callbacks
        // must not observe pre-archive/default runtime state. Bootstrap owns the
        // archive-ready boundary and publishes module lifecycle exactly once there.
        XjRuntimeCadence.BeginAfterNativeLoad(currentYear);
        XjWorldArchiveSystem.LoadReadOnly();
        XjWorldBootstrapLane.ScheduleAfterLoad(currentYear);
    }

    internal static void PublishWorldCleared()
    {
        XjWorldLifecycleRegistry.PublishCleared();
        XjCacheRegistry.ClearAll();
    }


    private static bool MayAffectRuntimeInterest(XjActorStateDomain domains)
    {
        const XjActorStateDomain relevant = XjActorStateDomain.Progression
            | XjActorStateDomain.HighRealm
            | XjActorStateDomain.Equipment
            | XjActorStateDomain.Identity;
        return (domains & relevant) != 0;
    }

    private static XjActorStateDomain ResolveTraitDomains(string traitId)
    {
        string value = (traitId ?? string.Empty).Trim().ToLowerInvariant();
        XjActorStateDomain result = XjActorStateDomain.Progression | XjActorStateDomain.Identity;
        if (value.IndexOf("craft", StringComparison.Ordinal) >= 0
            || value.IndexOf("alchemy", StringComparison.Ordinal) >= 0
            || value.IndexOf("artifact", StringComparison.Ordinal) >= 0
            || value.IndexOf("talisman", StringComparison.Ordinal) >= 0
            || value.IndexOf("formation", StringComparison.Ordinal) >= 0)
        {
            result |= XjActorStateDomain.Craft;
        }
        if (value.IndexOf("fabao", StringComparison.Ordinal) >= 0
            || value.IndexOf("lingbao", StringComparison.Ordinal) >= 0
            || value.IndexOf("weapon", StringComparison.Ordinal) >= 0)
        {
            result |= XjActorStateDomain.Equipment;
        }
        if (value.IndexOf("guowei", StringComparison.Ordinal) >= 0
            || value.IndexOf("jindan", StringComparison.Ordinal) >= 0
            || value.IndexOf("zhenjun", StringComparison.Ordinal) >= 0
            || value.IndexOf("daotai", StringComparison.Ordinal) >= 0)
        {
            result |= XjActorStateDomain.HighRealm;
        }
        return result;
    }
}
