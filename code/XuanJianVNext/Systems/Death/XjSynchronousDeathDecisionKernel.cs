using System;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.Death;

internal readonly struct XjSynchronousDeathPatchState
{
    internal readonly bool AllowOriginal;
    internal readonly bool EquipmentReleaseBegun;
    internal readonly bool ExternalReplacementRemoval;
    internal readonly XjDeathEntryState EntryState;

    internal XjSynchronousDeathPatchState(
        bool allowOriginal,
        bool equipmentReleaseBegun,
        in XjDeathEntryState entryState,
        bool externalReplacementRemoval = false)
    {
        AllowOriginal = allowOriginal;
        EquipmentReleaseBegun = equipmentReleaseBegun;
        ExternalReplacementRemoval = externalReplacementRemoval;
        EntryState = entryState;
    }
}

/// <summary>
/// Actor.die 的同步判定与结算内核。Harmony边界只负责参数转交；
/// 死因仲裁、装备释放、死亡后领域结算和异常撤销均由此处统一排序。
/// </summary>
internal static class XjSynchronousDeathDecisionKernel
{
    internal static XjSynchronousDeathPatchState Begin(Actor actor, AttackType attackType)
    {
        try
        {
            if (XjExternalUnitTransferContext.IsExplicitReplacementRemoval(actor))
            {
                XjDeathEntryState emptyEntryState = default;
                return new XjSynchronousDeathPatchState(true, false, in emptyEntryState, true);
            }

            bool allowOriginal = XjDeathArbitrationPipeline.TryBeginDeath(actor, attackType, out XjDeathEntryState entryState);
            if (!allowOriginal)
            {
                return new XjSynchronousDeathPatchState(false, false, in entryState);
            }

            XjEquipmentForgeConsumer.BeginDeathEquipmentRelease(actor);
            return new XjSynchronousDeathPatchState(true, true, in entryState);
        }
        catch (Exception ex)
        {
            // Prefix errors must not cancel or poison WorldBox's native death action.
            XuanJianVNext.Core.XjExceptionDiagnostics.Report("Interop/Actor/die-prefix", ex);
            XjDeathEntryState emptyEntryState = default;
            return new XjSynchronousDeathPatchState(true, false, in emptyEntryState);
        }
    }

    internal static void Complete(Actor actor, in XjSynchronousDeathPatchState patchState)
    {
        try
        {
            bool externalReplacement = patchState.ExternalReplacementRemoval
                || XjExternalUnitTransferContext.IsExplicitReplacementRemoval(actor);
            if (externalReplacement)
            {
                // replicateNow is an AVBS death action, so the replacement transaction can begin
                // after our Actor.die prefix has already captured a normal death snapshot. If the
                // marker appears before the postfix, unwind the pre-death bookkeeping and do not
                // commit a false XuanJian death for the disposable source actor.
                if (patchState.EquipmentReleaseBegun)
                {
                    XjEquipmentForgeConsumer.EndDeathEquipmentRelease(actor);
                }
                if (!patchState.ExternalReplacementRemoval && patchState.AllowOriginal)
                {
                    XjDeathEntryState abortEntryState = patchState.EntryState;
                    XjDeathArbitrationPipeline.AbortDeath(in abortEntryState);
                }
                return;
            }

            if (patchState.EquipmentReleaseBegun)
            {
                XjEquipmentForgeConsumer.EndDeathEquipmentRelease(actor);
            }
            if (!patchState.AllowOriginal) return;

            XjDeathEntryState entryState = patchState.EntryState;
            if (XjDeathArbitrationPipeline.CompleteDeath(actor, in entryState))
            {
                XjDaoTuCounterState.TryRegisterCounterOnDeath(actor, entryState.Snapshot);
                if (XjTrueDamageSystem.IsJinXingYaoXie(actor))
                {
                    XjTrueDamageSystem.OnJinXingYaoXieDied(actor);
                    XjYinSiTraitLifecycle.OnJinXingYaoXieDied(actor);
                }
                XjLongShuKillRewardSystem.TryGrant(actor, entryState.Snapshot);
                XjShiSentientConsumptionSystem.OnConfirmedKill(actor, entryState.Snapshot);
            }

            if (!XjSafeCore.IsAliveActor(actor))
            {
                int deathYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
                XjYinSiExposurePursuitSystem.OnActorDied(actor, deathYear);
                XjSecretRealmRegistry.OnActorDied(((BaseSystemData)actor.data).id, deathYear);
                XjJinDanImmortalityRegistry.MarkDead(actor, deathYear);
                XjFuQiSwordWorldState.OnActorDied(actor, deathYear);
                XjShenDanRegistry.OnActorDied(actor, deathYear);
                XjScheduler.UnregisterDeadActor(actor);
            }
        }
        catch (Exception ex)
        {
            // This is a postfix after native death. Preserve the original result and
            // leave the remaining reconciliation to its bounded runtime lanes.
            XuanJianVNext.Core.XjExceptionDiagnostics.Report("Interop/Actor/die-postfix", ex);
        }
    }

    internal static Exception Abort(
        Actor actor,
        in XjSynchronousDeathPatchState patchState,
        Exception exception)
    {
        try
        {
            if (patchState.ExternalReplacementRemoval)
            {
                return exception;
            }

            if (patchState.EquipmentReleaseBegun)
            {
                XjEquipmentForgeConsumer.EndDeathEquipmentRelease(actor);
            }
            if (exception != null && patchState.AllowOriginal)
            {
                XjDeathEntryState entryState = patchState.EntryState;
                XjDeathArbitrationPipeline.AbortDeath(in entryState);
            }
        }
        catch (Exception cleanupException)
        {
            // Never replace a native exception with a cleanup exception from the mod.
            XuanJianVNext.Core.XjExceptionDiagnostics.Report("Interop/Actor/die-finalizer", cleanupException);
        }
        return exception;
    }
}
