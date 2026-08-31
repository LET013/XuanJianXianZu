using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 紫金青宣的特殊修炼链。
///
/// 低境入口仍由宣土炼气积累青参之气、完成褚羊祭并以〖玄羊子〗成基；
/// 进入紫府后，玄羊子不再只是“五仙基之一”，而是已经成立的核心神通，
/// 其后逐一抬举另外四道仙基，使之在金丹成功以前升格为真实神通。
/// 五神通未齐备时，求金法、六品功法和金丹尝试全部保持关闭。
/// </summary>
internal static class XjQingXuanKongZhengSystem
{
    internal const string DaoTu = "青宣";
    internal const string SourceDaoTu = "宣土";
    internal const string FoundationXianJi = "玄羊子";
    private const string GongFaName = "六堰青云要诀";
    private const int QingCanQiTarget = 10;
    private const int QingXuanEntryOdds = 1000;
    private const int MinimumUpliftYears = 5;
    private const int MaximumUpliftYears = 10;

    private static readonly string[] RequiredKongZhengXianJi =
    {
        "玄羊子",
        "伏青山",
        "青宣岳",
        "上岩神",
        "观地冥"
    };

    // 下阳元是青宣果位已经显现后可能出现的余位末基。它同样必须先被
    // 玄羊子抬举成神通，不能因为是下位仙基就绕过金丹前五神通门槛。
    private static readonly string[] SupportedQingXuanFoundations =
    {
        "玄羊子",
        "伏青山",
        "青宣岳",
        "上岩神",
        "观地冥",
        "下阳元"
    };

    internal static bool IsQingXuanDaoTu(string daoTu)
    {
        return string.Equals((daoTu ?? string.Empty).Trim(), DaoTu, StringComparison.Ordinal);
    }

    internal static bool IsQingXuanFoundationId(string id)
    {
        string normalized = (id ?? string.Empty).Trim();
        if (normalized.Length == 0) return false;
        for (int i = 0; i < SupportedQingXuanFoundations.Length; i++)
        {
            if (string.Equals(SupportedQingXuanFoundations[i], normalized, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    internal static bool CanEnterQingXuan(Actor actor)
    {
        if (actor?.data == null) return false;
        return XjDaoTuManifestRegistry.CanManifestZiJin(XjDaoTuRootIds.QingXuan)
            && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanUnlocked, out int unlocked)
            && unlocked > 0;
    }

    internal static bool HasCompletedKongZheng(Actor actor)
    {
        return actor?.data != null
            && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanKongZhengCompleted, out int completed)
            && completed > 0;
    }

    internal static bool IsFiveShenTongReadyFlag(Actor actor)
    {
        return actor?.data != null
            && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanFiveShenTongReady, out int ready)
            && ready > 0;
    }

    internal static bool HasAnnualInterest(Actor actor, string realmId, string daoTu)
    {
        if (actor?.data == null || !XjDaoTuManifestRegistry.CanManifestZiJin(XjDaoTuRootIds.QingXuan))
        {
            return false;
        }

        if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
            && string.Equals(daoTu, SourceDaoTu, StringComparison.Ordinal)
            && !CanEnterQingXuan(actor))
        {
            if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanQingCanQi, out int qingCanQi)
                && qingCanQi > 0) return true;
            if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanChuYangJi, out int chuYangJi)
                && chuYangJi > 0) return true;
            if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, out int foundation)
                && foundation > 0) return true;

            long actorId = GetActorId(actor);
            return actorId > 0L
                && XjDeterministicHash.PositiveIndex(actorId, "qingxuan_entry_once", QingXuanEntryOdds) == 0;
        }

        return string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
            && IsQingXuanDaoTu(daoTu)
            && CanEnterQingXuan(actor)
            && !HasPreJinDanFiveShenTong(actor);
    }

    internal static void TickActor(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
    {
        if (actor?.data == null
            || currentYear <= 0
            || !XjDaoTuManifestRegistry.CanManifestZiJin(XjDaoTuRootIds.QingXuan))
        {
            return;
        }

        if (string.Equals(snapshot.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
            && IsQingXuanDaoTu(snapshot.DaoTu))
        {
            TickZiFuUplift(actor, currentYear);
            return;
        }

        if (!string.Equals(snapshot.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
            || !string.Equals(snapshot.DaoTu, SourceDaoTu, StringComparison.Ordinal)
            || CanEnterQingXuan(actor))
        {
            return;
        }

        TickEntry(actor, currentYear);
    }

    private static void TickEntry(Actor actor, int currentYear)
    {
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanQingCanQi, out int qingCanQi);
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanChuYangJi, out int chuYangJi);
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, out int foundation);

        if (qingCanQi <= 0 && chuYangJi <= 0 && foundation <= 0 && !RollFirstQingCanQi(actor, currentYear))
        {
            return;
        }
        if (qingCanQi < QingCanQiTarget)
        {
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanQingCanQi, Math.Min(QingCanQiTarget, qingCanQi + 1));
            return;
        }
        if (chuYangJi <= 0)
        {
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanChuYangJi, 1);
            return;
        }
        if (foundation <= 0)
        {
            CompleteFoundation(actor, currentYear);
        }
    }

    /// <summary>
    /// 紫府青宣的金丹前抬举阶段。该入口由高境年度管线保证每个逻辑年调用一次，
    /// 不依赖真元是否在当年增长，也不在战斗热路径执行。
    /// </summary>
    internal static void TickZiFuUplift(Actor actor, int currentYear)
    {
        if (actor?.data == null
            || currentYear <= 0
            || !XjCultivationPathRules.IsZiFuJinDan(actor)
            || !CanEnterQingXuan(actor)
            || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            || !string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
            || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
            || !IsQingXuanDaoTu(daoTu))
        {
            return;
        }

        XjXianJiState state = XjXianJiAccessor.BuildState(actor);
        if (state.Count <= 0 || state.Ids == null || state.Ids.Length == 0)
        {
            ClearInvalidUpliftProject(actor);
            return;
        }

        // 玄羊子在成基时已经是核心神通；后续四基才需要逐一抬举。
        XjXianJiAccessor.RefreshShenTongProjection(actor);
        if (TryMarkFiveShenTongReady(actor, state, currentYear)) return;

        string target = ReadString(actor, XjActorDataKeys.QingXuanUpliftTargetId);
        int completeYear = ReadInt(actor, XjActorDataKeys.QingXuanUpliftCompleteYear);
        if (!string.IsNullOrWhiteSpace(target)
            && (!Contains(state.Ids, target) || IsRaisedShenTong(actor, target)))
        {
            ClearUpliftProject(actor);
            target = string.Empty;
            completeYear = 0;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            target = ResolveNextUnraisedFoundation(actor, state.Ids);
            if (string.IsNullOrWhiteSpace(target))
            {
                TryMarkFiveShenTongReady(actor, state, currentYear);
                return;
            }

            int years = ResolveUpliftYears(actor, target, currentYear);
            XjActorAccessor.SetString(actor, XjActorDataKeys.QingXuanUpliftTargetId, target);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUpliftStartYear, currentYear);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUpliftCompleteYear, currentYear + years);
            return;
        }

        if (completeYear <= 0)
        {
            int years = ResolveUpliftYears(actor, target, currentYear);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUpliftStartYear, currentYear);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUpliftCompleteYear, currentYear + years);
            return;
        }
        if (currentYear < completeYear) return;

        MarkRaisedShenTong(actor, target);
        ClearUpliftProject(actor);
        XjXianJiAccessor.RefreshShenTongProjection(actor);
        int raisedCount = GetRaisedShenTongCount(actor);
        XjThreeBookWriter.RecordQingXuanFoundationRaised(actor, target, currentYear, raisedCount);
        XjFaBaoAcquisition.TryGrantZiFuLingBaoOnXianJi(
            actor,
            XjActorCultivationSnapshotBuilder.Build(actor),
            Math.Max(1, raisedCount),
            currentYear);
        try { actor.setStatsDirty(); actor.updateStats(); } catch (System.Exception xjCaught252) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjQingXuanKongZhengSystem.cs:252", xjCaught252); }

        state = XjXianJiAccessor.BuildState(actor);
        TryMarkFiveShenTongReady(actor, state, currentYear);
    }

    internal static string BuildProgressText(Actor actor)
    {
        if (actor?.data == null || !XjDaoTuManifestRegistry.CanManifestZiJin(XjDaoTuRootIds.QingXuan))
        {
            return string.Empty;
        }

        XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
        if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal) && IsQingXuanDaoTu(daoTu))
        {
            XjXianJiState state = XjXianJiAccessor.BuildState(actor);
            int raised = GetRaisedShenTongCount(actor);
            if (HasPreJinDanFiveShenTong(actor))
            {
                return "玄羊子抬举四基，五神通齐备，已可继续推演求金法";
            }

            string target = ReadString(actor, XjActorDataKeys.QingXuanUpliftTargetId);
            int completeYear = ReadInt(actor, XjActorDataKeys.QingXuanUpliftCompleteYear);
            string progress = "仙基" + state.Count.ToString(CultureInfo.InvariantCulture) + " · 满5  神通"
                + raised.ToString(CultureInfo.InvariantCulture) + " · 满5";
            if (!string.IsNullOrWhiteSpace(target))
            {
                int year = XjAnnualExecutionContext.ResolveYear(actor);
                int remaining = Math.Max(0, completeYear - year);
                progress += "  玄羊子正抬举【" + target + "】";
                if (remaining > 0) progress += "（尚需" + remaining.ToString(CultureInfo.InvariantCulture) + "年）";
            }
            else
            {
                progress += "  待得新基或开始抬举";
            }
            return progress;
        }

        if (CanEnterQingXuan(actor)) return "玄羊子成基";

        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanQingCanQi, out int qingCanQi);
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanChuYangJi, out int chuYangJi);
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, out int foundation);
        return "青参之气当前" + Math.Max(0, Math.Min(QingCanQiTarget, qingCanQi)).ToString(CultureInfo.InvariantCulture)
            + " · 门槛" + QingCanQiTarget.ToString(CultureInfo.InvariantCulture)
            + "  褚羊祭" + (chuYangJi > 0 ? "已成" : "未成")
            + "  玄羊子成基" + (foundation > 0 ? "已成" : "未成");
    }

    internal static string[] BuildRaisedShenTongProjection(Actor actor, string[] xianJiIds)
    {
        if (xianJiIds == null || xianJiIds.Length == 0) return Array.Empty<string>();
        if (actor?.data == null
            || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
            || !IsQingXuanDaoTu(daoTu))
        {
            return CloneIds(xianJiIds);
        }
        string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
        if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
            || string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
            || string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
        {
            // 终境已成立即代表五基抬举完成；0.9.4.35 的逐旗标投影会把手动
            // 补录或中断恢复后的完整五神通错误裁成一门。
            return CloneIds(xianJiIds);
        }

        List<string> raised = new List<string>(xianJiIds.Length);
        for (int i = 0; i < xianJiIds.Length; i++)
        {
            string id = (xianJiIds[i] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(id) && IsRaisedShenTong(actor, id)) raised.Add(id);
        }
        return raised.ToArray();
    }

    internal static int GetRaisedShenTongCount(Actor actor)
    {
        if (actor?.data == null) return 0;
        XjXianJiState state = XjXianJiAccessor.BuildState(actor);
        return BuildRaisedShenTongProjection(actor, state.Ids).Length;
    }

    internal static bool HasPreJinDanFiveShenTong(Actor actor)
    {
        if (actor?.data == null
            || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
            || !IsQingXuanDaoTu(daoTu))
        {
            return false;
        }
        XjXianJiState state = XjXianJiAccessor.BuildState(actor);
        return state.Count == XjXianJiState.MaxCount && AreAllCurrentFoundationsRaised(actor, state.Ids);
    }

    internal static bool IsExactKongZhengUpperSet(Actor actor)
    {
        return actor?.data != null && HasAllKongZhengXianJi(XjXianJiAccessor.BuildState(actor));
    }

    internal static bool TryCompleteKongZhengOnJinDan(
        Actor actor,
        string jinDanDaoTu,
        string guoWei,
        int currentYear)
    {
        if (actor?.data == null
            || !XjDaoTuManifestRegistry.CanManifestZiJin(XjDaoTuRootIds.QingXuan)
            || HasCompletedKongZheng(actor)
            || !IsQingXuanDaoTu(jinDanDaoTu)
            || string.IsNullOrWhiteSpace(guoWei)
            || !guoWei.Trim().EndsWith(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
            || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            || !string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
            || !HasPreJinDanFiveShenTong(actor)
            || !IsExactKongZhengUpperSet(actor))
        {
            return false;
        }

        XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanKongZhengCompleted, 1);
        XjDaoTuManifestRegistry.MarkCaiQiUnlocked(
            XjDaoTuRootIds.QingXuan,
            GetActorId(actor),
            currentYear);
        PublishKongZhengEvent(actor, currentYear);
        XjAutoCollectSystem.TryCollectKongZhengZhenJun(actor, "QingXuanKongZheng");
        return true;
    }

    private static bool TryMarkFiveShenTongReady(Actor actor, in XjXianJiState state, int currentYear)
    {
        if (state.Count < XjXianJiState.MaxCount || !AreAllCurrentFoundationsRaised(actor, state.Ids)) return false;
        if (!IsFiveShenTongReadyFlag(actor))
        {
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanFiveShenTongReady, 1);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanFiveShenTongReadyYear, currentYear);
            ClearUpliftProject(actor);
            XjXianJiAccessor.RefreshShenTongProjection(actor);
            // 第五道神通真正成立的当年才启动完整五年求金法周期；五仙基集齐
            // 的旧年份不能提前积累求金法机会债务。
            XjQiuJinFaSystem.ResetEligibility(actor);
            XjQiuJinFaSystem.ActivateEligibility(actor, currentYear);
            XjThreeBookWriter.RecordQingXuanFiveShenTongReady(actor, currentYear);
            XjActorGongFaCollection.ReconcileWithActor(actor, "QingXuanFiveShenTongReady");
        }
        return true;
    }

    private static bool AreAllCurrentFoundationsRaised(Actor actor, string[] ids)
    {
        if (ids == null || ids.Length != XjXianJiState.MaxCount) return false;
        for (int i = 0; i < ids.Length; i++)
        {
            if (!IsRaisedShenTong(actor, ids[i])) return false;
        }
        return true;
    }

    private static string ResolveNextUnraisedFoundation(Actor actor, string[] ids)
    {
        if (ids == null) return string.Empty;
        for (int order = 1; order < SupportedQingXuanFoundations.Length; order++)
        {
            string preferred = SupportedQingXuanFoundations[order];
            if (Contains(ids, preferred) && !IsRaisedShenTong(actor, preferred)) return preferred;
        }
        for (int i = 0; i < ids.Length; i++)
        {
            string id = (ids[i] ?? string.Empty).Trim();
            if (IsQingXuanFoundationId(id)
                && !string.Equals(id, FoundationXianJi, StringComparison.Ordinal)
                && !IsRaisedShenTong(actor, id)) return id;
        }
        return string.Empty;
    }

    private static bool IsRaisedShenTong(Actor actor, string id)
    {
        string normalized = (id ?? string.Empty).Trim();
        if (string.Equals(normalized, FoundationXianJi, StringComparison.Ordinal))
        {
            return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, out int foundation)
                && foundation > 0;
        }
        int bit = ResolveFoundationBit(normalized);
        if (bit == 0) return false;
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanRaisedShenTongMask, out int mask);
        return (mask & bit) != 0;
    }

    private static void MarkRaisedShenTong(Actor actor, string id)
    {
        int bit = ResolveFoundationBit(id);
        if (actor?.data == null || bit == 0) return;
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanRaisedShenTongMask, out int mask);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanRaisedShenTongMask, mask | bit);
    }

    private static int ResolveFoundationBit(string id)
    {
        string normalized = (id ?? string.Empty).Trim();
        return normalized switch
        {
            "伏青山" => 1 << 1,
            "青宣岳" => 1 << 2,
            "上岩神" => 1 << 3,
            "观地冥" => 1 << 4,
            "下阳元" => 1 << 5,
            _ => 0
        };
    }

    private static int ResolveUpliftYears(Actor actor, string target, int currentYear)
    {
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
        XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
        int years = aptitude switch
        {
            >= 6 => 5,
            5 => 7,
            _ => 9
        };
        if (huiGuang >= 100f) years -= 1;
        else if (huiGuang < 35f) years += 1;
        int jitter = XjDeterministicHash.PositiveIndex(GetActorId(actor) + currentYear, "qingxuan_uplift|" + target, 2);
        return Math.Clamp(years + jitter, MinimumUpliftYears, MaximumUpliftYears);
    }

    private static bool RollFirstQingCanQi(Actor actor, int currentYear)
    {
        if (actor?.data == null || currentYear <= 0) return false;
        long actorId = GetActorId(actor);
        return actorId > 0L
            && XjDeterministicHash.PositiveIndex(actorId, "qingxuan_entry_once", QingXuanEntryOdds) == 0;
    }

    private static void CompleteFoundation(Actor actor, int currentYear)
    {
        if (!XjDaoTuManifestRegistry.CanManifestZiJin(XjDaoTuRootIds.QingXuan)) return;

        XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, 1);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUnlocked, 1);
        if (!XjCultivationStateTransitions.TrySetDaoTu(actor, DaoTu, true))
        {
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, 0);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUnlocked, 0);
            return;
        }

        XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanQingCanQi, QingCanQiTarget);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanChuYangJi, 1);
        XjGongFaAccessor.WriteState(actor, new XjGongFaState(
            true, GongFaName, 4, 0, 0f, DaoTu, false, "QingXuanFoundation"));
        XjGongFaAccessor.WriteSource(actor, "玄羊子成基");
        XjXianJiAccessor.Add(actor, FoundationXianJi, currentYear);
        XjActorGongFaCollection.ReconcileWithActor(actor, "QingXuanFoundation");
        XjDaoTuManifestRegistry.MarkZiJinManifested(XjDaoTuRootIds.QingXuan, GetActorId(actor), currentYear);
    }

    private static void PublishKongZhengEvent(Actor actor, int currentYear)
    {
        if (actor?.data == null) return;
        string actorName = actor.getName() ?? "无名修士";
        string historyText = "【空证开天·青宣】" + actorName
            + "以〖玄羊子〗抬举伏青山、青宣岳、上岩神、观地冥四基，令五基尽升神通。"
            + "五神通在金门之前同举，青堰神岳伏元性由此自旧制之外开辟，空证青宣果位。";
        string tipText = "【空证开天】青宣第六土显世\n" + actorName
            + "以玄羊子抬举四基，五神通齐证金丹，青宣果位照见山河。";
        XjBroadcastSystem.BroadcastSLevelActorEvent(
            actor, historyText, tipText, "#74E8FF", 12f, XjEventIconCatalog.JinDanUpgrade);
    }

    private static bool HasAllKongZhengXianJi(in XjXianJiState xianJi)
    {
        if (xianJi.Ids == null || xianJi.Ids.Length < RequiredKongZhengXianJi.Length) return false;
        for (int requiredIndex = 0; requiredIndex < RequiredKongZhengXianJi.Length; requiredIndex++)
        {
            if (!Contains(xianJi.Ids, RequiredKongZhengXianJi[requiredIndex])) return false;
        }
        return true;
    }

    private static void ClearInvalidUpliftProject(Actor actor)
    {
        if (!string.IsNullOrWhiteSpace(ReadString(actor, XjActorDataKeys.QingXuanUpliftTargetId)))
        {
            ClearUpliftProject(actor);
        }
    }

    private static void ClearUpliftProject(Actor actor)
    {
        if (actor?.data == null) return;
        XjActorAccessor.SetString(actor, XjActorDataKeys.QingXuanUpliftTargetId, string.Empty);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUpliftStartYear, 0);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUpliftCompleteYear, 0);
    }

    private static int ReadInt(Actor actor, string key)
    {
        return actor?.data != null && XjActorAccessor.TryGetInt(actor, key, out int value) ? value : 0;
    }

    private static string ReadString(Actor actor, string key)
    {
        return actor?.data != null && XjActorAccessor.TryGetString(actor, key, out string value)
            ? (value ?? string.Empty).Trim()
            : string.Empty;
    }

    private static string[] CloneIds(string[] ids)
    {
        string[] result = new string[ids.Length];
        Array.Copy(ids, result, ids.Length);
        return result;
    }

    private static bool Contains(string[] ids, string id)
    {
        if (ids == null || string.IsNullOrWhiteSpace(id)) return false;
        for (int i = 0; i < ids.Length; i++)
        {
            if (string.Equals((ids[i] ?? string.Empty).Trim(), id.Trim(), StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static long GetActorId(Actor actor)
    {
        return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
    }
}
