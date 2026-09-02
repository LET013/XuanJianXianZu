using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Patches;

internal static class XjVNextDebugTraitHandler
{
    // Debug trait IDs
    internal const string DebugJinDanReincarnationTraitId = "DebugJinDanReincarnation";
    internal const string DebugJinDanGuoWeiYiXiangTraitId = "DebugJinDanGuoWeiYiXiang";
    internal const string DebugJinDanQuanBingTraitId = "DebugJinDanQuanBing";
    internal const string DebugJinDanGuoWeiTraitId = "DebugJinDanGuoWei";
    internal const string DebugGongFaTraitId = "DebugGongFa";
    internal const string DebugClearZaQiTraitId = "DebugClearZaQi";
    internal const string DebugMingShuTraitId = "DebugMingShu";
    internal const string DebugHuiGuangTraitId = "DebugHuiGuang";
    internal const string DebugSwordIntentTraitId = "DebugSwordIntent";
    internal const string DebugFaBaoDengXianTraitId = "DebugFaBaoDengXian";
    internal const string DebugLuoXiaInquiryTraitId = "DebugLuoXiaInquiry";
    internal const string DebugEnterGuShiTraitId = "DebugEnterGuShi";
    internal const string DebugEnterJinShiTraitId = "DebugEnterJinShi";
    internal const string DebugShiGreatDesireTraitId = "DebugShiGreatDesire";
    internal const string DebugShiWrathTraitId = "DebugShiWrath";
    internal const string DebugShiDharmaAdmirationTraitId = "DebugShiDharmaAdmiration";
    internal const string DebugShiDisciplineTraitId = "DebugShiDiscipline";
    internal const string DebugShiGoodJoyTraitId = "DebugShiGoodJoy";
    internal const string DebugShiCompassionTraitId = "DebugShiCompassion";
    internal const string DebugShiEmptinessTraitId = "DebugShiEmptiness";
    internal const string DebugShiMingShuTraitId = "DebugShiMingShu";
    internal const string DebugShiAdvanceTraitId = "DebugShiAdvance";
    internal const string DebugShiSeedMoHeTraitId = "DebugShiSeedMoHe";
    internal const string DebugShiSeedDharmaFormTraitId = "DebugShiSeedDharmaForm";
    internal const string DebugShiAdvanceDharmaFormStageTraitId = "DebugShiAdvanceDharmaFormStage";
    internal const string DebugShiToggleDomainTraitId = "DebugShiToggleDomain";
    internal const string DebugShiAbsorbJinDiTraitId = "DebugShiAbsorbJinDi";
    internal const string DebugShiReincarnationTraitId = "DebugShiReincarnation";
    internal const string DebugShiTrueSpiritLockTraitId = "DebugShiTrueSpiritLock";

    // Debug data keys
    internal const string DebugJinDanReincarnationTriggeredKey = "xuanjian.debug_jindan_reincarnation_triggered";

    // Debug values
    private const int DebugJinDanGuoWeiYiXiangBoostAmount = 2000;
    private const float DebugMingShuPeakCongenitalValue = 100f;
    private const float DebugHuiGuangPeakValue = XjDaoHuiPolicy.Maximum;

    private static readonly HashSet<string> DebugInterventionTraitIds = new HashSet<string>(StringComparer.Ordinal)
    {
        DebugJinDanReincarnationTraitId,
        DebugJinDanGuoWeiYiXiangTraitId,
        DebugJinDanQuanBingTraitId,
        DebugJinDanGuoWeiTraitId,
        DebugGongFaTraitId,
        DebugClearZaQiTraitId,
        DebugMingShuTraitId,
        DebugHuiGuangTraitId,
        DebugSwordIntentTraitId,
        DebugFaBaoDengXianTraitId,
        DebugLuoXiaInquiryTraitId,
    };

    private static int LastDebugInterventionIssueLogFrame = -100000;
    private static int LastDebugInterventionSuccessLogFrame = -100000;

    /// <summary>
    /// 由统一 Actor trait 边界调用。调试特质只是一次显式命令，不再单独占用
    /// Actor.addTrait Harmony 生命周期。
    /// </summary>
    internal static void HandleNativeTraitAdded(Actor actor, string traitId, bool result)
    {
        if (XjExternalUnitTransferContext.IsTraitTransferActive
            || actor?.data == null
            || string.IsNullOrWhiteSpace(traitId)
            || !IsDebugInterventionTraitId(traitId))
        {
            return;
        }

        if (!result)
        {
            try
            {
                if (!actor.hasTrait(traitId)) return;
            }
            catch
            {
                return;
            }
        }
        TryRunDebugInterventionTrait(actor, traitId);
    }

    /// <summary>
    /// 道胎之姿属于普通特质授予规则的一部分。只返回准入结果，不再自行 Patch addTrait。
    /// </summary>
    internal static bool ShouldAllowDaoZhuTraitGrant(Actor actor, string traitId)
    {
        if (XjExternalUnitTransferContext.IsTraitTransferActive) return true;
        if (string.IsNullOrWhiteSpace(traitId) || !string.Equals(traitId, DaoZhuTraitId, StringComparison.Ordinal))
        {
            return true;
        }
        if (actor?.data == null) return false;
        // 支持者彩蛋宋玄不占普通“十名道胎之姿”人工授予槽位；否则满额旧档会让
        // 这名唯一彩蛋失去其设定保证。
        if (XjSongXuanEasterEggSystem.IsSongXuan(actor)) return true;
        try
        {
            if (actor.hasTrait(DaoZhuTraitId)) return true;
        }
        catch
        {
            return false;
        }

        int currentCount = CountActorsWithDaoZhu();
        if (currentCount < MaxDaoZhuCount) return true;
        Debug.Log("[玄鉴][道胎之姿] 已达上限(" + MaxDaoZhuCount + "个)，阻止添加");
        return false;
    }



    private static bool TryRunDebugInterventionTrait(Actor actor, string traitId)
    {
        if (actor == null || actor.data == null || string.IsNullOrWhiteSpace(traitId))
        {
            return true;
        }

        string canonicalTraitId = NormalizeDebugInterventionTraitId(traitId);
        if (string.IsNullOrWhiteSpace(canonicalTraitId))
        {
            return true;
        }

        try
        {
            switch (canonicalTraitId)
            {
                case DebugJinDanGuoWeiYiXiangTraitId:
                    TryTriggerDebugJinDanGuoWeiYiXiang(actor);
                    break;
                case DebugJinDanQuanBingTraitId:
                    TryTriggerDebugJinDanQuanBing(actor);
                    break;
                case DebugJinDanGuoWeiTraitId:
                    TryTriggerDebugJinDanGuoWei(actor);
                    break;
                case DebugGongFaTraitId:
                    TryTriggerDebugGongFa(actor);
                    break;
                case DebugClearZaQiTraitId:
                    TryTriggerDebugClearZaQi(actor);
                    break;
                case DebugMingShuTraitId:
                    TryTriggerDebugMingShu(actor);
                    break;
                case DebugHuiGuangTraitId:
                    TryTriggerDebugHuiGuang(actor);
                    break;
                case DebugSwordIntentTraitId:
                    TryTriggerDebugSwordIntent(actor);
                    break;
                case DebugFaBaoDengXianTraitId:
                    TryTriggerDebugFaBaoDengXian(actor);
                    break;
                case DebugLuoXiaInquiryTraitId:
                    TryTriggerDebugLuoXiaInquiry(actor);
                    break;
                case DebugJinDanReincarnationTraitId:
                    TryTriggerDebugJinDanReincarnation(actor);
                    break;
                case DebugEnterGuShiTraitId:
                    TryTriggerDebugShiEntry(actor, XjShiTraditionIds.Ancient, DebugEnterGuShiTraitId);
                    break;
                case DebugEnterJinShiTraitId:
                    TryTriggerDebugShiEntry(actor, XjShiTraditionIds.Modern, DebugEnterJinShiTraitId);
                    break;
                case DebugShiGreatDesireTraitId:
                    TryTriggerDebugShiLineage(actor, XjShiLineageIds.GreatDesire, canonicalTraitId);
                    break;
                case DebugShiWrathTraitId:
                    TryTriggerDebugShiLineage(actor, XjShiLineageIds.Wrath, canonicalTraitId);
                    break;
                case DebugShiDharmaAdmirationTraitId:
                    TryTriggerDebugShiLineage(actor, XjShiLineageIds.DharmaAdmiration, canonicalTraitId);
                    break;
                case DebugShiDisciplineTraitId:
                    TryTriggerDebugShiLineage(actor, XjShiLineageIds.Discipline, canonicalTraitId);
                    break;
                case DebugShiGoodJoyTraitId:
                    TryTriggerDebugShiLineage(actor, XjShiLineageIds.GoodJoy, canonicalTraitId);
                    break;
                case DebugShiCompassionTraitId:
                    TryTriggerDebugShiLineage(actor, XjShiLineageIds.Compassion, canonicalTraitId);
                    break;
                case DebugShiEmptinessTraitId:
                    TryTriggerDebugShiLineage(actor, XjShiLineageIds.Emptiness, canonicalTraitId);
                    break;
                case DebugShiMingShuTraitId:
                    TryTriggerDebugShiMingShu(actor, canonicalTraitId);
                    break;
                case DebugShiAdvanceTraitId:
                    TryTriggerDebugShiAdvance(actor, canonicalTraitId);
                    break;
                case DebugShiSeedMoHeTraitId:
                    TryTriggerDebugShiSeedMoHe(actor, canonicalTraitId);
                    break;
                case DebugShiSeedDharmaFormTraitId:
                    TryTriggerDebugShiSeedDharmaForm(actor, canonicalTraitId);
                    break;
                case DebugShiAdvanceDharmaFormStageTraitId:
                    TryTriggerDebugShiAdvanceDharmaFormStage(actor, canonicalTraitId);
                    break;
                case DebugShiToggleDomainTraitId:
                    TryTriggerDebugShiToggleDomain(actor, canonicalTraitId);
                    break;
                case DebugShiAbsorbJinDiTraitId:
                    TryTriggerDebugShiAbsorbJinDi(actor, canonicalTraitId);
                    break;
                case DebugShiReincarnationTraitId:
                    TryTriggerDebugShiReincarnation(actor, canonicalTraitId);
                    break;
                case DebugShiTrueSpiritLockTraitId:
                    TryTriggerDebugShiTrueSpiritLock(actor, canonicalTraitId);
                    break;
            }
        }
        catch (Exception ex)
        {
            TryLogDebugInterventionIssue(actor, canonicalTraitId, "exception", ex.GetType().Name);
        }

        return actor != null && actor.data != null && XjSafeCore.IsAliveActor(actor);
    }

    private static bool IsDebugInterventionTraitId(string traitId)
    {
        return !string.IsNullOrWhiteSpace(NormalizeDebugInterventionTraitId(traitId));
    }

    private static string NormalizeDebugInterventionTraitId(string traitId)
    {
        if (string.IsNullOrWhiteSpace(traitId))
        {
            return string.Empty;
        }

        return DebugInterventionTraitIds.Contains(traitId) ? traitId : string.Empty;
    }

    private static void TryClearDebugInterventionTrait(Actor actor, string canonicalTraitId)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(canonicalTraitId))
        {
            return;
        }

        try
        {
            if (actor.hasTrait(canonicalTraitId))
            {
                actor.removeTrait(canonicalTraitId);
            }
        }
        catch (System.Exception xjCaught191) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextDebugTraitPatches.cs:191", xjCaught191); }

        if (actor.data.saved_traits == null)
        {
            return;
        }

        for (int num = actor.data.saved_traits.Count - 1; num >= 0; num--)
        {
            string savedTraitId = actor.data.saved_traits[num];
            if (string.Equals(NormalizeDebugInterventionTraitId(savedTraitId), canonicalTraitId, StringComparison.Ordinal))
            {
                actor.data.saved_traits.RemoveAt(num);
            }
        }
    }

    private static void TryLogDebugInterventionIssue(Actor actor, string traitId, string reason, string detail = null)
    {
        if (Time.frameCount - LastDebugInterventionIssueLogFrame < 600)
        {
            return;
        }

        LastDebugInterventionIssueLogFrame = Time.frameCount;
        long actorId = 0L;
        try
        {
            if (actor?.data is BaseSystemData data)
            {
                actorId = data.id;
            }
        }
        catch (System.Exception xjCaught226) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextDebugTraitPatches.cs:226", xjCaught226); }

        Debug.LogWarning("[玄鉴][调试干预] actorId=" + actorId
            + " trait=" + (traitId ?? string.Empty)
            + " reason=" + (reason ?? string.Empty)
            + " detail=" + (detail ?? string.Empty));
    }

    private static void TryLogDebugInterventionSuccess(Actor actor, string traitId, string action, string detail = null)
    {
        if (Time.frameCount - LastDebugInterventionSuccessLogFrame < 600)
        {
            return;
        }

        LastDebugInterventionSuccessLogFrame = Time.frameCount;
        long actorId = 0L;
        try
        {
            if (actor?.data is BaseSystemData data)
            {
                actorId = data.id;
            }
        }
        catch (System.Exception xjCaught252) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextDebugTraitPatches.cs:252", xjCaught252); }

        Debug.Log("[玄鉴][调试干预] actorId=" + actorId
            + " trait=" + (traitId ?? string.Empty)
            + " action=" + (action ?? string.Empty)
            + " detail=" + (detail ?? string.Empty));
    }

    // ====== Handler: DebugEnterGuShi / DebugEnterJinShi (释修入门) ======
    private static void TryTriggerDebugShiEntry(Actor actor, string tradition, string traitId)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            TryClearDebugInterventionTrait(actor, traitId);
            return;
        }

        int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
        bool entered = XjShiState.TryApplyManualTraditionRecord(actor, tradition, year);
        TryClearDebugInterventionTrait(actor, traitId);
        if (entered)
        {
            TryLogDebugInterventionSuccess(actor, traitId, "shi_entry", XjShiCatalog.GetTraditionDisplay(tradition));
        }
        else
        {
            TryLogDebugInterventionIssue(actor, traitId, "entry_rejected", "果位钟爱锁途者不可改修释门");
        }
    }

    private static void TryTriggerDebugShiLineage(Actor actor, string lineageId, string traitId)
    {
        int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
        bool success = XjShiState.TryDebugSetLineage(actor, lineageId, year);
        TryClearDebugInterventionTrait(actor, traitId);
        if (success) TryLogDebugInterventionSuccess(actor, traitId, "shi_lineage", XjShiCatalog.GetLineageDisplay(lineageId));
        else TryLogDebugInterventionIssue(actor, traitId, "lineage_rejected", "角色无法转入今释或法脉无效");
    }

    private static void TryTriggerDebugShiMingShu(Actor actor, string traitId)
    {
        if (actor?.data != null && XjCultivationPathRules.IsShi(actor))
        {
            XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu, XjShiCatalog.MaximumShiMingShu);
            XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShuPending, 0f);
            TryLogDebugInterventionSuccess(actor, traitId, "shi_mingshu", "1000/1000");
        }
        else TryLogDebugInterventionIssue(actor, traitId, "not_shi", "requires_shi");
        TryClearDebugInterventionTrait(actor, traitId);
    }

    private static void TryTriggerDebugShiAdvance(Actor actor, string traitId)
    {
        int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
        bool success = XjShiState.TryDebugAdvance(actor, year);
        TryClearDebugInterventionTrait(actor, traitId);
        if (success) TryLogDebugInterventionSuccess(actor, traitId, "shi_advance");
        else TryLogDebugInterventionIssue(actor, traitId, "advance_blocked", "缺少承载地、座主或已至当前测试上限");
    }

    private static void TryTriggerDebugShiSeedMoHe(Actor actor, string traitId)
    {
        int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
        bool success = XjShiState.TryDebugSeedMoHe(actor, year);
        TryClearDebugInterventionTrait(actor, traitId);
        if (success) TryLogDebugInterventionSuccess(actor, traitId, "shi_seed_mohe");
        else TryLogDebugInterventionIssue(actor, traitId, "seed_failed", "requires_shi");
    }

    private static void TryTriggerDebugShiSeedDharmaForm(Actor actor, string traitId)
    {
        // RC11.7：法相属于金丹级高境，不再允许任何玩家可触达的调试特质直接补录。
        // 内部 TryDebugSeedDharmaForm 仅保留给旧档迁移/一致性修复，不再从 addTrait 桥调用。
        TryClearDebugInterventionTrait(actor, traitId);
        TryLogDebugInterventionIssue(actor, traitId, "manual_high_realm_forbidden",
            "法相必须由真实释修高境状态机证得");
    }

    private static void TryTriggerDebugShiAdvanceDharmaFormStage(Actor actor, string traitId)
    {
        // 推进法相阶段最终可直达世尊尝试，因此也属于高境手动旁路，一并关闭。
        TryClearDebugInterventionTrait(actor, traitId);
        TryLogDebugInterventionIssue(actor, traitId, "manual_high_realm_forbidden",
            "法相进境与世尊必须由年度高境状态机自然推进");
    }

    private static void TryTriggerDebugShiToggleDomain(Actor actor, string traitId)
    {
        int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
        bool success = XjShiDomainState.TryDebugToggleVisibility(actor, year);
        TryClearDebugInterventionTrait(actor, traitId);
        if (success) TryLogDebugInterventionSuccess(actor, traitId, "shi_domain_visibility");
        else TryLogDebugInterventionIssue(actor, traitId, "domain_missing", "requires_bound_domain");
    }

    private static void TryTriggerDebugShiAbsorbJinDi(Actor actor, string traitId)
    {
        int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
        bool success = XjShiExpansionSystem.TryCommitJinDiAbsorption(actor, year, out string absorbedDomainId);
        if (success)
        {
            TryLogDebugInterventionSuccess(actor, traitId, "shi_absorb_jindi", absorbedDomainId);
        }
        else TryLogDebugInterventionIssue(actor, traitId, "no_absorbable_jindi", "需为承载地主人，且存在无主隐世金地");
        TryClearDebugInterventionTrait(actor, traitId);
    }

    private static void TryTriggerDebugShiReincarnation(Actor actor, string traitId)
    {
        if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)
            || !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
            || !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
        {
            TryClearDebugInterventionTrait(actor, traitId);
            TryLogDebugInterventionIssue(actor, traitId, "not_modern_shi", "requires_modern_shi");
            return;
        }
        int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
        bool queued;
        if (string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
            && snapshot.CurrentLife < 9)
        {
            queued = XjReincarnation.RecordVoluntaryShi(actor, year);
        }
        else
        {
            queued = XjDeathSnapshotBuilder.TryBuild(actor, out XjDeathSnapshot deathSnapshot)
                && XjReincarnation.RecordForcedShi(actor, deathSnapshot);
        }
        long actorId = ((BaseSystemData)actor.data).id;
        bool died = queued && XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)11, false,
            XuanJianVNext.Systems.Death.XjDeathCause.ShiVoluntaryReincarnation);
        if (!died) XjReincarnation.CancelPending(actorId);
        TryClearDebugInterventionTrait(actor, traitId);
        if (died) TryLogDebugInterventionSuccess(actor, traitId, "shi_reincarnation");
        else TryLogDebugInterventionIssue(actor, traitId, "reincarnation_failed", "queue_or_death_failed");
    }

    private static void TryTriggerDebugShiTrueSpiritLock(Actor actor, string traitId)
    {
        if (actor?.data != null && XjCultivationPathRules.IsShi(actor))
        {
            XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiTrueSpiritLocked, out int locked);
            if (locked > 0) XjShiHighRealmSystem.ClearTrueSpiritLock(actor);
            else XjShiHighRealmSystem.ApplyTrueSpiritLock(actor, 0, "陆江仙锁定");
            TryLogDebugInterventionSuccess(actor, traitId, "shi_true_spirit_lock", locked > 0 ? "unlocked" : "locked");
        }
        else TryLogDebugInterventionIssue(actor, traitId, "not_shi", "requires_shi");
        TryClearDebugInterventionTrait(actor, traitId);
    }

    // ====== Handler: DebugJinDanReincarnation (转世调试) ======
    private static void TryTriggerDebugJinDanReincarnation(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

        XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
        if (!jinDan.Found)
        {
            TryClearDebugInterventionTrait(actor, DebugJinDanReincarnationTraitId);
            TryLogDebugInterventionIssue(actor, DebugJinDanReincarnationTraitId, "invalid_realm", "requires_jindan");
            return;
        }

        BaseSystemData data = (BaseSystemData)actor.data;
        data.get(DebugJinDanReincarnationTriggeredKey, out bool triggered, false);
        if (triggered)
        {
            TryClearDebugInterventionTrait(actor, DebugJinDanReincarnationTraitId);
            return;
        }

        // Capture death snapshot for reincarnation
        if (XjDeathSnapshotBuilder.TryBuild(actor, out var snapshot))
        {
            XjReincarnation.RecordForcedJinDanFromSnapshot(snapshot);
        }

        XjActorStateWriteGateway.SetExternalBool(
            actor,
            DebugJinDanReincarnationTriggeredKey,
            true,
            XjActorStateDomain.HighRealm | XjActorStateDomain.Progression);

        // Clear the active JinDan payload through the HighRealm authority so the world
        // position registries are released together with actor-side metadata. Raw debug
        // field clears used to leave ghost GuoWei/QuanBing claims behind after force death.
        XjJinDanAccessor.ClearSuccess(actor);

        // Force kill actor through the authorized death scope.
        XjVanillaDeathGuard.TryExecuteForceDeath(
            actor,
            (AttackType)11,
            false,
            XuanJianVNext.Systems.Death.XjDeathCause.TechnicalRemoval);

        TryClearDebugInterventionTrait(actor, DebugJinDanReincarnationTraitId);
        string sourceName = actor.getName();
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "无名金丹真君";
        }

        Debug.Log("[玄鉴] 转世调试: " + sourceName + " 真灵已入轮回。");
    }

    // ====== Handler: DebugJinDanGuoWeiYiXiang (果位意象调试) ======
    private static void TryTriggerDebugJinDanGuoWeiYiXiang(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

        // 果位意象属于金丹等价终境的共享高境字段。调试入口只看真实境界，
        // 不再要求 XjJinDanAccessor 的果位账本已经完整水合；否则旧档/手动恢复的
        // 真君羽士会因为 successYear 等兼容字段尚未回填而被误判为“非金丹”。
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
        if (!XjCultivationPathRules.IsJinDanEquivalentRealm(realmId))
        {
            TryClearDebugInterventionTrait(actor, DebugJinDanGuoWeiYiXiangTraitId);
            TryLogDebugInterventionIssue(actor, DebugJinDanGuoWeiYiXiangTraitId, "invalid_realm", "requires_jindan_or_zhenjun");
            return;
        }

        int currentYear = GetCurrentYear(actor);
        // RC11.5 以前误把“果位意象 +2000”写进 XjJinDanYiXiang（旧兼容道行投影），
        // 会直接把金丹巅峰角色的阶段进度覆盖成2000。先用真实道行/修持修复兼容投影，
        // 再只提升独立的果位意象字段，绝不改动金丹小境界。
        XjHighRealmDaoStateService.EnsureRestoredState(actor, currentYear);
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int currentImage);
        int nextImage = Math.Min(10000, Math.Max(0, currentImage) + DebugJinDanGuoWeiYiXiangBoostAmount);
        using XjActorStateRevisionStore.ReductionScope reduction = XjActorStateRevisionStore.BeginReduction(((BaseSystemData)actor.data).id);
        XjActorStateWriteGateway.SetExternalInt(
            actor,
            XjActorDataKeys.XjGuoWeiImage,
            nextImage,
            XjActorStateDomain.HighRealm | XjActorStateDomain.Progression);
        XjGuoWeiImageStateService.Refresh(actor, currentYear, recordHistory: true);
        TryClearDebugInterventionTrait(actor, DebugJinDanGuoWeiYiXiangTraitId);
        TryLogDebugInterventionSuccess(actor, DebugJinDanGuoWeiYiXiangTraitId, "guoweiyixiang_boost", "image=" + nextImage);
    }

    // ====== Handler: DebugJinDanQuanBing (权柄调试) ======
    private static void TryTriggerDebugJinDanQuanBing(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

        if (!IsJinDanActor(actor))
        {
            TryClearDebugInterventionTrait(actor, DebugJinDanQuanBingTraitId);
            TryLogDebugInterventionIssue(actor, DebugJinDanQuanBingTraitId, "invalid_realm", "requires_jindan");
            return;
        }

        XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei);
        XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
        if (string.IsNullOrWhiteSpace(guoWei) && jinDan.Found)
        {
            guoWei = jinDan.GuoWei;
        }

        if (string.IsNullOrWhiteSpace(daoTu))
        {
            TryClearDebugInterventionTrait(actor, DebugJinDanQuanBingTraitId);
            return;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        if (string.IsNullOrWhiteSpace(guoWei))
        {
            XjManualRealmTraitReconciliation.EnsureHighRealmGongFaSet(actor, daoTu);
            jinDan = XjJinDanAccessor.BuildState(actor);
            guoWei = jinDan.GuoWei;
        }
        if (string.IsNullOrWhiteSpace(guoWei))
        {
            TryClearDebugInterventionTrait(actor, DebugJinDanQuanBingTraitId);
            TryLogDebugInterventionIssue(actor, DebugJinDanQuanBingTraitId, "missing_guowei", "daotu=" + daoTu);
            return;
        }
		int currentYear = GetCurrentYear(actor);
		if (!XjGuoWeiQuanBingLifecycle.TryGrantRandomAdjacentDaoTuAuthority(actor, currentYear))
		{
			TryClearDebugInterventionTrait(actor, DebugJinDanQuanBingTraitId);
			TryLogDebugInterventionIssue(actor, DebugJinDanQuanBingTraitId, "grant_failed", "daotu=" + daoTu);
			return;
		}
        TryClearDebugInterventionTrait(actor, DebugJinDanQuanBingTraitId);
        TryLogDebugInterventionSuccess(actor, DebugJinDanQuanBingTraitId, "quanbing_grant", "daotu=" + daoTu);
    }

    // ====== Handler: DebugJinDanGuoWei (果位调试 - 武装果位钟爱) ======
    private static void TryTriggerDebugJinDanGuoWei(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

        // 该调试特质现在是“成丹时判定”的一次性标记，不再挂在死亡旁路。
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjForceGuoWeiZhongAiOnJinDan, 1);
        if (IsZhengWeiJinDanActor(actor))
        {
            XjGuoWeiQuanBingLifecycle.ForceGuoWeiZhongAi(actor, GetCurrentYear(actor));
        }
        TryClearDebugInterventionTrait(actor, DebugJinDanGuoWeiTraitId);
        TryLogDebugInterventionSuccess(actor, DebugJinDanGuoWeiTraitId, "guowei_zhongai_armed", "trigger=on_jindan");
    }

    // ====== Handler: DebugGongFa (功法调试) ======
    private static void TryTriggerDebugGongFa(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

        XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
        string gongFaName = GenerateDebugSixPinGongFaName(daoTu);
		XjGongFaAccessor.WriteState(actor, new XjGongFaState(
			true,
			gongFaName,
			6,
			0,
			0f,
			daoTu,
			true,
			"DebugGrant"));
		XjGongFaAccessor.WriteSource(actor, "陆江仙模拟器");
		string sourceGradeFive = XjGongFaNameLibrary.NormalizeNameForGrade(gongFaName, daoTu, 5);
		bool qiuJinFaGranted = XjManualRealmTraitReconciliation.EnsureDebugQiuJinFa(actor, daoTu, sourceGradeFive);
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "陆江仙模拟器");

        TryClearDebugInterventionTrait(actor, DebugGongFaTraitId);
        TryLogDebugInterventionSuccess(actor, DebugGongFaTraitId, "gongfa_set", "grade=6 name=" + gongFaName + " qiujin=" + qiuJinFaGranted);
    }

    // ====== Handler: DebugClearZaQi (清除杂气锁) ======
    private static void TryTriggerDebugClearZaQi(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

        if (!ResolvePreferredDaoTuForZaQiClear(actor, out string daoTu))
        {
            TryClearDebugInterventionTrait(actor, DebugClearZaQiTraitId);
            TryLogDebugInterventionIssue(actor, DebugClearZaQiTraitId, "missing_target_daotu");
            return;
        }

        if (!XjCaiQiCatalog.TryGetEntryByDisplayName(daoTu, out XjCaiQiCatalogEntry entry))
        {
            TryClearDebugInterventionTrait(actor, DebugClearZaQiTraitId);
            TryLogDebugInterventionIssue(actor, DebugClearZaQiTraitId, "missing_caiqi_entry", daoTu);
            return;
        }

        XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiCompleted, 0);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.LianQiByZaQi, 0);
        XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiResultType, string.Empty);
        XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiResourceId, string.Empty);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiResourceCount, 0);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiGatheredCount, 0);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiConsumedForBreakthrough, 0);

        XjCaiQiResolvedResult result = new XjCaiQiResolvedResult(
            true,
            XjCaiQiResultTypes.XianTianQi,
            entry.PlaceTypeId,
            entry.BranchId,
            "陆江仙模拟器",
			"陆江仙模拟器清除杂气");
        XjCaiQiActorAccessor.MarkCompleted(actor, result.ResultType, result.PlaceTypeId, result.BranchId, result.SiteName);
        XjCaiQiFaAcquisition.TryAcquireFromCaiQiResult(actor, result);

        TryClearDebugInterventionTrait(actor, DebugClearZaQiTraitId);
        TryLogDebugInterventionSuccess(actor, DebugClearZaQiTraitId, "zaqi_cleared", "daotu=" + daoTu);
    }

    private static bool ResolvePreferredDaoTuForZaQiClear(Actor actor, out string daoTu)
    {
        daoTu = string.Empty;
        if (actor?.data == null) return false;

        XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string currentDaoTu);
        if (!string.IsNullOrWhiteSpace(currentDaoTu) && !string.Equals(currentDaoTu.Trim(), "杂气", StringComparison.Ordinal))
        {
            daoTu = currentDaoTu.Trim();
            return true;
        }

        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaDaoTu, out string gongFaDaoTu);
        if (!string.IsNullOrWhiteSpace(gongFaDaoTu) && !string.Equals(gongFaDaoTu.Trim(), "杂气", StringComparison.Ordinal))
        {
            daoTu = gongFaDaoTu.Trim();
            return true;
        }

        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaSourceDaoTu, out string qiuJinDaoTu);
        if (!string.IsNullOrWhiteSpace(qiuJinDaoTu) && !string.Equals(qiuJinDaoTu.Trim(), "杂气", StringComparison.Ordinal))
        {
            daoTu = qiuJinDaoTu.Trim();
            return true;
        }

        return false;
    }

    // ====== Handler: DebugMingShu (命数调试) ======
    private static void TryTriggerDebugMingShu(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

		float beforeCongenital = GetMingShuCongenital(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquired);
		float safeAcquired = Math.Max(0f, acquired);
		XjMingShuState.Set(actor, DebugMingShuPeakCongenitalValue, safeAcquired);
        TryClearDebugInterventionTrait(actor, DebugMingShuTraitId);
        TryLogDebugInterventionSuccess(actor, DebugMingShuTraitId, "mingshu_peak",
            "beforeCongenital=" + beforeCongenital.ToString("F1", CultureInfo.InvariantCulture)
            + " after=" + DebugMingShuPeakCongenitalValue.ToString("F1", CultureInfo.InvariantCulture)
            + " acquired=" + safeAcquired.ToString("F1", CultureInfo.InvariantCulture));
    }

    // ====== Handler: DebugHuiGuang (道慧调试) ======
    private static void TryTriggerDebugHuiGuang(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

        float beforeHuiGuang = GetHuiGuang(actor);
        XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, DebugHuiGuangPeakValue);
        TryClearDebugInterventionTrait(actor, DebugHuiGuangTraitId);
        TryLogDebugInterventionSuccess(actor, DebugHuiGuangTraitId, "huiguang_peak",
            "before=" + beforeHuiGuang.ToString("F1", CultureInfo.InvariantCulture)
            + " after=" + DebugHuiGuangPeakValue.ToString("F1", CultureInfo.InvariantCulture));
    }


    // ====== Handler: DebugSwordIntent（照剑天心） ======
    private static void TryTriggerDebugSwordIntent(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }
        if (!XjWeaponArtSystem.TryGrantSwordIntentBySimulator(actor, XjYearTracker.CurrentYear, out string alias))
        {
            TryClearDebugInterventionTrait(actor, DebugSwordIntentTraitId);
            if (XjSwordIntentRegistry.Count >= 16)
            {
                TryLogDebugInterventionIssue(actor, DebugSwordIntentTraitId, "sword_intent_cap",
                    "count=" + XjSwordIntentRegistry.Count);
            }
            else
            {
                TryLogDebugInterventionIssue(actor, DebugSwordIntentTraitId, "grant_failed");
            }
            return;
        }
        bool swordEquipped = XjEquipmentForgeConsumer.EnsureSimulatorSwordWeapon(
            actor,
            XjYearTracker.CurrentYear);
        TryClearDebugInterventionTrait(actor, DebugSwordIntentTraitId);
        if (!swordEquipped)
        {
            TryLogDebugInterventionIssue(actor, DebugSwordIntentTraitId, "sword_equip_failed",
                "alias=" + alias + " count=" + XjSwordIntentRegistry.Count);
            return;
        }
        TryLogDebugInterventionSuccess(actor, DebugSwordIntentTraitId, "sword_intent_granted",
            "alias=" + alias + " count=" + XjSwordIntentRegistry.Count + " swordEquipped=true");
    }

    // ====== Handler: DebugFaBaoDengXian（法宝登仙） ======
    private static void TryTriggerDebugFaBaoDengXian(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            TryClearDebugInterventionTrait(actor, DebugFaBaoDengXianTraitId);
            return;
        }

        if (!XjDaoTaiSpellScale.IsDaoTaiActor(actor))
        {
            TryClearDebugInterventionTrait(actor, DebugFaBaoDengXianTraitId);
            TryLogDebugInterventionIssue(actor, DebugFaBaoDengXianTraitId, "not_daotai", "requires_daotai");
            return;
        }

        int year = Math.Max(1, GetCurrentYear(actor));
        bool upgraded = XjFaBaoAcquisition.TryUpgradePrimaryJinDanToXianQi(actor, year);
        TryClearDebugInterventionTrait(actor, DebugFaBaoDengXianTraitId);
        if (upgraded)
        {
            TryLogDebugInterventionSuccess(actor, DebugFaBaoDengXianTraitId, "fabao_dengxian", "year=" + year);
        }
        else
        {
            TryLogDebugInterventionIssue(actor, DebugFaBaoDengXianTraitId, "upgrade_rejected",
                "requires_primary_jindan_bonded_fabao");
        }
    }

    // ====== Handler: DebugLuoXiaInquiry（问道落霞） ======
    private static void TryTriggerDebugLuoXiaInquiry(Actor actor)
    {
        int year = Math.Max(1, GetCurrentYear(actor));
        bool accepted = XjHongXiaLuoXiaEvent.TryAcceptManualInquiryDisciple(
            actor, year, out string daoTu, out string reason);
        TryClearDebugInterventionTrait(actor, DebugLuoXiaInquiryTraitId);
        if (accepted)
        {
            TryLogDebugInterventionSuccess(actor, DebugLuoXiaInquiryTraitId,
                "luoxia_disciple_accepted", "year=" + year + ";daotu=" + daoTu);
        }
        else
        {
            TryLogDebugInterventionIssue(actor, DebugLuoXiaInquiryTraitId,
                "luoxia_inquiry_rejected", reason);
        }
    }

    // ====== Utility Methods ======

    private static bool IsJinDanActor(Actor actor)
    {
        return XjJinDanAccessor.BuildState(actor).Found;
    }

    private static bool IsZhengWeiJinDanActor(Actor actor)
    {
        if (!IsJinDanActor(actor))
        {
            return false;
        }

        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei);
        return !string.IsNullOrWhiteSpace(guoWei)
            && XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
    }


    private static float GetMingShuCongenital(Actor actor)
    {
        if (actor?.data == null)
        {
            return 0f;
        }

        XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float value);
        return value;
    }

    private static float GetHuiGuang(Actor actor)
    {
        if (actor?.data == null)
        {
            return 0f;
        }

        XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float value);
        return value;
    }

    private static string GenerateDebugSixPinGongFaName(string daoTu)
    {
        string[] prefixes = { "太玄", "混元", "玉清", "紫霄", "青冥", "玄黄", "鸿蒙", "太极" };
        string[] suffixes = { "金丹经", "道典", "真解", "玄章", "秘录", "天书" };
        string prefix = prefixes[UnityEngine.Random.Range(0, prefixes.Length)];
        string suffix = suffixes[UnityEngine.Random.Range(0, suffixes.Length)];
        string name = prefix + (string.IsNullOrWhiteSpace(daoTu) ? "" : daoTu) + suffix;
        return name;
    }

    private static int GetCurrentYear(Actor actor)
    {
        int trackedYear = XjYearTracker.CurrentYear;
        if (trackedYear > 0)
        {
            return trackedYear;
        }

        int worldYear = World.world?.map_stats?.year ?? 0;
        if (worldYear > 0)
        {
            return worldYear;
        }

        return actor == null ? 0 : (int)Math.Floor(Math.Max(0f, actor.getAge()));
    }

    // ==================== 道胎之姿 10 角色限制 ====================

    private const string DaoZhuTraitId = "ChuShen8";
    private const int MaxDaoZhuCount = 10;


    private static int CountActorsWithDaoZhu()
    {
        int count = 0;
        IReadOnlyList<Actor> actors = XjScheduler.GetKnownActorsSnapshot();
        for (int i = 0; i < actors.Count; i++)
        {
            Actor actor = actors[i];
            if (!XjSafeCore.IsAliveActor(actor))
            {
                continue;
            }

            try
            {
                if (actor.hasTrait(DaoZhuTraitId))
                {
                    count++;
                }
            }
            catch (System.Exception xjCaught850) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextDebugTraitPatches.cs:850", xjCaught850); }
        }

        return count;
    }
}
