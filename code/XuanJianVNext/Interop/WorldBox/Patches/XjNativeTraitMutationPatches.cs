using System;
using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Patches;

/// <summary>
/// Actor trait 的唯一原生写边界。
///
/// 0.9.9.8 收束前，addTrait/removeTrait 同时被境界、资质、身份、阴司、剑道、
/// 调试特质、特殊身份与金性妖邪等多组 Harmony 补丁各自拦截/回写。一次原生特质
/// 修改可能触发多套状态机并再次投影可见特质，形成“原生特质 -> 玄鉴权威 -> 再写
/// 原生特质”的二次构建链。
///
/// 现在只保留每个原生 overload 一组 Prefix/Postfix：Prefix 负责统一准入与快照，
/// Postfix 负责把“原生已经发生的事实”提交给各领域。领域系统不再各自占用 Actor
/// trait 生命周期入口。内部可见特质投影仍由 IsVisibleTraitSyncActive 防止反向解释。
/// </summary>
internal static class XjNativeTraitMutationPatches
{
    private struct TraitMutationState
    {
        internal string PreviousRealmState;
        internal XjAptitudeTraitState AptitudeState;
        internal bool WasPresent;
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
    private static bool Actor_addTrait_NativeAuthority_Prefix(
        Actor __instance,
        [HarmonyArgument("pTraitID")] string traitId,
        out TraitMutationState __state,
        ref bool __result)
    {
        __state = CaptureGrantState(__instance, traitId);
        if (ShouldAllowGrant(__instance, traitId)) return true;
        __result = false;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
    private static void Actor_addTrait_NativeAuthority_Postfix(
        Actor __instance,
        [HarmonyArgument("pTraitID")] string traitId,
        bool __result,
        TraitMutationState __state)
    {
        CommitGrant(__instance, traitId, __result, __state);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
    private static bool Actor_addTraitAsset_NativeAuthority_Prefix(
        Actor __instance,
        [HarmonyArgument("pTrait")] ActorTrait trait,
        out TraitMutationState __state,
        ref bool __result)
    {
        string traitId = trait?.id;
        __state = CaptureGrantState(__instance, traitId);
        if (ShouldAllowGrant(__instance, traitId)) return true;
        __result = false;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
    private static void Actor_addTraitAsset_NativeAuthority_Postfix(
        Actor __instance,
        [HarmonyArgument("pTrait")] ActorTrait trait,
        bool __result,
        TraitMutationState __state)
    {
        CommitGrant(__instance, trait?.id, __result, __state);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
    private static bool Actor_removeTrait_NativeAuthority_Prefix(
        Actor __instance,
        [HarmonyArgument("pTraitID")] string traitId,
        out TraitMutationState __state)
    {
        __state = CaptureRemovalState(__instance, traitId);
        return ShouldAllowRemoval(__instance, traitId);
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
    private static void Actor_removeTrait_NativeAuthority_Postfix(
        Actor __instance,
        [HarmonyArgument("pTraitID")] string traitId,
        TraitMutationState __state)
    {
        bool removed = __state.WasPresent && !HasTraitSafe(__instance, traitId);
        CommitRemoval(__instance, traitId, removed);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(ActorTrait) })]
    private static bool Actor_removeTraitAsset_NativeAuthority_Prefix(
        Actor __instance,
        [HarmonyArgument("pTrait")] ActorTrait trait,
        out TraitMutationState __state,
        ref bool __result)
    {
        string traitId = trait?.id;
        __state = CaptureRemovalState(__instance, traitId);
        if (ShouldAllowRemoval(__instance, traitId)) return true;
        __result = false;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(ActorTrait) })]
    private static void Actor_removeTraitAsset_NativeAuthority_Postfix(
        Actor __instance,
        [HarmonyArgument("pTrait")] ActorTrait trait,
        bool __result,
        TraitMutationState __state)
    {
        bool removed = __result || (__state.WasPresent && !HasTraitSafe(__instance, trait?.id));
        CommitRemoval(__instance, trait?.id, removed);
    }

    private static TraitMutationState CaptureGrantState(Actor actor, string traitId)
    {
        TraitMutationState state = new TraitMutationState
        {
            PreviousRealmState = XjManualTraitEditContext.IsActive
                ? XjManualRealmTraitReconciliation.CaptureBeforeGrant(actor, traitId)
                : string.Empty,
            WasPresent = HasTraitSafe(actor, traitId)
        };

        if (XjManualTraitEditContext.IsActive
            && !XjCultivationStateTransitions.IsVisibleTraitSyncActive
            && XjAptitudeTraitLifecycle.IsAptitudeTrait(traitId))
        {
            state.AptitudeState = XjAptitudeTraitLifecycle.CaptureState(actor);
        }
        return state;
    }

    private static TraitMutationState CaptureRemovalState(Actor actor, string traitId)
    {
        return new TraitMutationState
        {
            WasPresent = HasTraitSafe(actor, traitId)
        };
    }

    private static bool ShouldAllowGrant(Actor actor, string traitId)
    {
        if (string.IsNullOrWhiteSpace(traitId)) return true;
        // 世尊之姿的“在世最多二人”属于世界硬约束，单位转移/复制也不能绕过。
        if (!XjShiWorldHonoredPosturePolicy.ShouldAllowGrant(actor, traitId)) return false;
        if (XjExternalUnitTransferContext.IsTraitTransferActive) return true;

        if (XjSafeCore.IsLightningImmortalTraitBlocked(traitId)
            || XjForbiddenVanillaTraitPolicy.ShouldBlock(traitId)
            || XjVisibleTraitSync.ShouldBlockMadnessTrait(actor, traitId)
            || XjVisibleTraitSync.ShouldBlockRealmNativeTraitForShi(actor, traitId)
            || !XjYinSiTraitLifecycle.ShouldAllowVisibleGrant(actor, traitId)
            || XjJianDaoCompatibility.ShouldBlockTraitGrant(actor, traitId))
        {
            return false;
        }

        if (XjManualTraitEditContext.IsActive
            && !XjCultivationStateTransitions.IsVisibleTraitSyncActive
            && XjAptitudeTraitLifecycle.ShouldBlockVisibleGrant(actor, traitId))
        {
            return false;
        }

        return XjVNextDebugTraitHandler.ShouldAllowDaoZhuTraitGrant(actor, traitId);
    }

    private static bool ShouldAllowRemoval(Actor actor, string traitId)
    {
        if (XjExternalUnitTransferContext.IsTraitTransferActive) return true;
        if (string.IsNullOrWhiteSpace(traitId)) return true;

        if (!XjXuanJianShenTongSpecials.ShouldAllowSpecialIdentityTraitRemoval(traitId))
        {
            return false;
        }

        if (string.Equals(traitId, "madness", StringComparison.Ordinal)
            && XjTrueDamageSystem.ShouldLockJinXingYaoXieMadness(actor))
        {
            return false;
        }
        return true;
    }

    private static void CommitGrant(Actor actor, string traitId, bool result, TraitMutationState state)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(traitId)) return;
        bool presentAfterGrant = result || HasTraitSafe(actor, traitId);
        XjShiWorldHonoredPosturePolicy.ObserveGrant(actor, traitId, presentAfterGrant);
        if (XjExternalUnitTransferContext.IsTraitTransferActive) return;

        XjInternalEventBus.PublishActorTraitGrantCompleted(actor, traitId, result, state.PreviousRealmState);

        if (!XjCultivationStateTransitions.IsVisibleTraitSyncActive)
        {
            bool present = result || HasTraitSafe(actor, traitId);
            if (present)
            {
                if (!XjIdentityTraitLifecycle.IsReconciling && XjIdentityTraitLifecycle.IsIdentityTrait(traitId))
                {
                    XjIdentityTraitLifecycle.RecordVisibleTraitGranted(actor, traitId);
                }
                if (!XjYinSiTraitLifecycle.IsReconciling && XjYinSiTraitLifecycle.IsYinSiTrait(traitId))
                {
                    XjYinSiTraitLifecycle.RecordVisibleTraitGranted(actor, traitId);
                }
                if (result
                    && XjManualTraitEditContext.IsActive
                    && XjAptitudeTraitLifecycle.IsAptitudeTrait(traitId))
                {
                    // Preserve the old aptitude editor contract: adding an already-present
                    // visible aptitude trait is not a second manual edit transaction.
                    XjAptitudeTraitLifecycle.RecordVisibleTraitGranted(actor, traitId, state.AptitudeState);
                }
            }
        }

        XjVNextDebugTraitHandler.HandleNativeTraitAdded(actor, traitId, result);
    }

    private static void CommitRemoval(Actor actor, string traitId, bool removed)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(traitId)) return;
        XjShiWorldHonoredPosturePolicy.ObserveRemoval(actor, traitId, removed);
        if (XjExternalUnitTransferContext.IsTraitTransferActive) return;

        XjInternalEventBus.PublishActorTraitRemovalCompleted(actor, traitId, removed);
        if (!removed
            || XjCultivationStateTransitions.IsVisibleTraitSyncActive
            || HasTraitSafe(actor, traitId))
        {
            return;
        }

        XjIdentityTraitLifecycle.RecordVisibleTraitRemoved(actor, traitId);
        XjYinSiTraitLifecycle.RecordVisibleTraitRemoved(actor, traitId);
        if (XjManualTraitEditContext.IsActive && XjAptitudeTraitLifecycle.IsAptitudeTrait(traitId))
        {
            XjAptitudeTraitLifecycle.RecordVisibleTraitRemoved(actor, traitId);
        }
    }

    private static bool HasTraitSafe(Actor actor, string traitId)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(traitId)) return false;
        try { return actor.hasTrait(traitId); }
        catch { return false; }
    }
}
