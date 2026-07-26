using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;

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
    internal const string DebugShenTongZaoHuaTraitId = "DebugShenTongZaoHua";

    // Debug data keys
    internal const string DebugJinDanReincarnationTriggeredKey = "xuanjian.debug_jindan_reincarnation_triggered";

    // Debug values
    private const int DebugJinDanGuoWeiYiXiangBoostAmount = 2000;
    private const float DebugMingShuPeakCongenitalValue = 100f;
    private const float DebugHuiGuangPeakValue = 120f;

    private static readonly string[] DebugInterventionTraitIds = new string[9]
    {
        DebugJinDanReincarnationTraitId,
        DebugJinDanGuoWeiYiXiangTraitId,
        DebugJinDanQuanBingTraitId,
        DebugJinDanGuoWeiTraitId,
        DebugGongFaTraitId,
        DebugClearZaQiTraitId,
        DebugMingShuTraitId,
        DebugHuiGuangTraitId,
        DebugShenTongZaoHuaTraitId
    };

    private static int LastDebugInterventionIssueLogFrame = -100000;
    private static int LastDebugInterventionSuccessLogFrame = -100000;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
    private static void Actor_addTrait_DebugIntervention_Postfix(Actor __instance, string pTraitID)
    {
        if (__instance == null || __instance.data == null || string.IsNullOrWhiteSpace(pTraitID))
        {
            return;
        }

        if (!IsDebugInterventionTraitId(pTraitID))
        {
            return;
        }

        TryRunDebugInterventionTrait(__instance, pTraitID);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
    private static void Actor_addTraitAsset_DebugIntervention_Postfix(Actor __instance, [HarmonyArgument("pTrait")] ActorTrait trait)
    {
        string traitId = trait?.id;
        if (__instance == null || __instance.data == null || string.IsNullOrWhiteSpace(traitId))
        {
            return;
        }

        if (!IsDebugInterventionTraitId(traitId))
        {
            return;
        }

        TryRunDebugInterventionTrait(__instance, traitId);
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
                case DebugShenTongZaoHuaTraitId:
                    TryTriggerDebugShenTongZaoHua(actor);
                    break;
                case DebugJinDanReincarnationTraitId:
                    TryTriggerDebugJinDanReincarnation(actor);
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

        for (int i = 0; i < DebugInterventionTraitIds.Length; i++)
        {
            if (string.Equals(traitId, DebugInterventionTraitIds[i], StringComparison.Ordinal))
            {
                return DebugInterventionTraitIds[i];
            }
        }

        return string.Empty;
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
        catch
        {
        }

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
        catch
        {
        }

        Debug.LogWarning("[XuanJianVNext][DebugIntervention] actorId=" + actorId
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
        catch
        {
        }

        Debug.Log("[XuanJianVNext][DebugIntervention] actorId=" + actorId
            + " trait=" + (traitId ?? string.Empty)
            + " action=" + (action ?? string.Empty)
            + " detail=" + (detail ?? string.Empty));
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

        data.set(DebugJinDanReincarnationTriggeredKey, true);

        // Clear JinDan identity for reincarnation flow
        try
        {
            XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, string.Empty);
            XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, string.Empty);
            XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, 0);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, 0);
        }
        catch
        {
        }

        // Force kill actor through the authorized death scope.
        XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)11, false);

        TryClearDebugInterventionTrait(actor, DebugJinDanReincarnationTraitId);
        string sourceName = actor.getName();
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "无名金丹真君";
        }

        Debug.Log("[XuanJianVNext] 转世调试: " + sourceName + " 真灵已入轮回。");
    }

    // ====== Handler: DebugJinDanGuoWeiYiXiang (果位意象调试) ======
    private static void TryTriggerDebugJinDanGuoWeiYiXiang(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

        if (!IsJinDanActor(actor))
        {
            TryClearDebugInterventionTrait(actor, DebugJinDanGuoWeiYiXiangTraitId);
            return;
        }

        BaseSystemData data = (BaseSystemData)actor.data;
        data.get("xuanjian.jindan_guowei_yixiang", out int currentYiXiang, 0);
        currentYiXiang += DebugJinDanGuoWeiYiXiangBoostAmount;
        data.set("xuanjian.jindan_guowei_yixiang", currentYiXiang);
        data.set(XjActorDataKeys.XjJinDanYiXiang, currentYiXiang);
        TryClearDebugInterventionTrait(actor, DebugJinDanGuoWeiYiXiangTraitId);
        TryLogDebugInterventionSuccess(actor, DebugJinDanGuoWeiYiXiangTraitId, "guoweiyixiang_boost", "amount=" + currentYiXiang);
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
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jinXing);

        if (string.IsNullOrWhiteSpace(daoTu))
        {
            TryClearDebugInterventionTrait(actor, DebugJinDanQuanBingTraitId);
            return;
        }

        long actorId = ((BaseSystemData)actor.data).id;
		XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, daoTu, guoWei, GetCurrentYear(actor));
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

    // ====== Handler: DebugShenTongZaoHua (神通造化) ======
    private static void TryTriggerDebugShenTongZaoHua(Actor actor)
    {
        if (!XjSafeCore.IsAliveActor(actor))
        {
            return;
        }

        if (!TryResolveDebugDaoTu(actor, out string daoTu))
        {
            TryClearDebugInterventionTrait(actor, DebugShenTongZaoHuaTraitId);
            TryLogDebugInterventionIssue(actor, DebugShenTongZaoHuaTraitId, "missing_daotu");
            return;
        }

        XjXianJiState state = XjXianJiAccessor.BuildState(actor);
        if (!state.Found || state.Count <= 0 || state.Ids == null || state.Ids.Length == 0)
        {
            TryClearDebugInterventionTrait(actor, DebugShenTongZaoHuaTraitId);
            TryLogDebugInterventionIssue(actor, DebugShenTongZaoHuaTraitId, "missing_shentong");
            return;
        }

        long actorId = actor.data is BaseSystemData data ? data.id : 0L;
        int currentYear = GetCurrentYear(actor);
        List<string> retained = new List<string>(state.Ids.Length);
        for (int i = 0; i < state.Ids.Length; i++)
        {
            string current = (state.Ids[i] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(current)
                && XjXianJiCatalog.GetPoolKind(daoTu, current) != XjXianJiPoolKind.Other
                && !retained.Contains(current))
            {
                retained.Add(current);
            }
        }

        string[] nextIds = new string[state.Ids.Length];
        Dictionary<string, string> replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        List<string> changeDetails = new List<string>();
        for (int i = 0; i < state.Ids.Length; i++)
        {
            string current = (state.Ids[i] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(current)
                || XjXianJiCatalog.GetPoolKind(daoTu, current) != XjXianJiPoolKind.Other)
            {
                nextIds[i] = current;
                continue;
            }

            if (!XjXianJiCatalog.TryPickUpperLowerAdjacentReplacement(
                    daoTu,
                    actorId,
                    currentYear,
                    i,
                    current,
                    retained.ToArray(),
                    out string replacement))
            {
                TryClearDebugInterventionTrait(actor, DebugShenTongZaoHuaTraitId);
                TryLogDebugInterventionIssue(actor, DebugShenTongZaoHuaTraitId, "replacement_pool_empty", current);
                return;
            }

            nextIds[i] = replacement;
            retained.Add(replacement);
            replacements[current] = replacement;
            changeDetails.Add(current + "→" + replacement);
        }

        if (replacements.Count == 0)
        {
            TryClearDebugInterventionTrait(actor, DebugShenTongZaoHuaTraitId);
            TryLogDebugInterventionSuccess(actor, DebugShenTongZaoHuaTraitId, "no_other_shentong", "daotu=" + daoTu);
            return;
        }

        string source = "陆江仙模拟器·神通造化";
        if (!XjActorGongFaCollection.TryRemapXianJiMappings(actor, daoTu, replacements, source, out int remappedGongFa))
        {
            TryClearDebugInterventionTrait(actor, DebugShenTongZaoHuaTraitId);
            TryLogDebugInterventionIssue(actor, DebugShenTongZaoHuaTraitId, "gongfa_remap_failed");
            return;
        }

        if (!XjXianJiAccessor.RestoreSnapshot(actor, string.Join("|", nextIds), state.LastYear))
        {
            Dictionary<string, string> rollback = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in replacements)
            {
                rollback[pair.Value] = pair.Key;
            }
            XjActorGongFaCollection.TryRemapXianJiMappings(actor, daoTu, rollback, "神通造化回滚", out _);
            TryClearDebugInterventionTrait(actor, DebugShenTongZaoHuaTraitId);
            TryLogDebugInterventionIssue(actor, DebugShenTongZaoHuaTraitId, "xianji_write_failed");
            return;
        }

        XjActorGongFaCollection.ReconcileWithActor(actor, "神通造化");
        bool mappingComplete = true;
        for (int i = 0; i < nextIds.Length; i++)
        {
            string id = (nextIds[i] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }
            if (!XjActorGongFaCollection.TryGetByMappedXianJi(actor, id, out _))
            {
                XjActorGongFaCollection.EnsureForXianJi(actor, id, currentYear, string.Empty, source);
            }
            if (!XjActorGongFaCollection.TryGetByMappedXianJi(actor, id, out _))
            {
                mappingComplete = false;
                break;
            }
        }
        if (!mappingComplete)
        {
            XjActorGongFaCollection.ReplaceAllForManualDaoTu(actor, daoTu, nextIds, source);
        }

        XjGongFaProgression.PublishInheritanceSnapshot(actor, source);
        TryClearDebugInterventionTrait(actor, DebugShenTongZaoHuaTraitId);
        TryLogDebugInterventionSuccess(
            actor,
            DebugShenTongZaoHuaTraitId,
            "other_shentong_replaced",
            "daotu=" + daoTu + " count=" + replacements.Count + " gongfa=" + remappedGongFa + " " + string.Join(";", changeDetails));
    }

    private static bool TryResolveDebugDaoTu(Actor actor, out string daoTu)
    {
        daoTu = string.Empty;
        if (actor?.data == null)
        {
            return false;
        }
        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string current)
            && !string.IsNullOrWhiteSpace(current)
            && !string.Equals(current.Trim(), "杂气", StringComparison.Ordinal))
        {
            daoTu = current.Trim();
            return true;
        }
        XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
        if (gongFa.Found
            && !string.IsNullOrWhiteSpace(gongFa.DaoTu)
            && !string.Equals(gongFa.DaoTu.Trim(), "杂气", StringComparison.Ordinal))
        {
            daoTu = gongFa.DaoTu.Trim();
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

    // ====== Handler: DebugHuiGuang (慧光调试) ======
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
        return !string.IsNullOrWhiteSpace(guoWei) && guoWei.Contains("正位", StringComparison.Ordinal);
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

    // ==================== 道主之资 9 角色限制 ====================

    private const string DaoZhuTraitId = "ChuShen8";
    private const int MaxDaoZhuCount = 9;

    /// <summary>
    /// 限制道主之资（ChuShen8）最多只能有9个角色拥有
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
    private static bool Actor_addTrait_DaoZhuLimit_Prefix(Actor __instance, string pTraitID)
    {
        if (string.IsNullOrWhiteSpace(pTraitID) || !string.Equals(pTraitID, DaoZhuTraitId, StringComparison.Ordinal))
        {
            return true; // 非道主之资，正常添加
        }

        if (__instance == null || __instance.data == null)
        {
            return false;
        }

        // 如果角色已经有道主之资，允许（重复添加的防御）
        if (__instance.hasTrait(DaoZhuTraitId))
        {
            return true;
        }

        // 计数当前世界活着的道主之资角色
        int currentCount = CountActorsWithDaoZhu();
        if (currentCount >= MaxDaoZhuCount)
        {
            Debug.Log("[玄鉴][道主之资] 已达上限(" + MaxDaoZhuCount + "个)，阻止添加");
            return false; // 阻止添加
        }

        return true;
    }

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
            catch { }
        }

        return count;
    }
}
