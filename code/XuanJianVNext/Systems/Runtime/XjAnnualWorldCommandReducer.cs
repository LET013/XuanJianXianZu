using System;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Localization;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.YaoShu;

namespace XuanJianVNext.Systems.Runtime;

internal readonly struct XjAnnualWorldCommand
{
    internal readonly XjAnnualWorldRuntimeLane.Stage Stage;
    internal readonly int ActiveYear;
    internal readonly int LatestRequestedYear;
    internal readonly XjUnifiedDetectionSnapshot DetectionSnapshot;

    internal XjAnnualWorldCommand(
        XjAnnualWorldRuntimeLane.Stage stage,
        int activeYear,
        int latestRequestedYear,
        in XjUnifiedDetectionSnapshot detectionSnapshot)
    {
        Stage = stage;
        ActiveYear = Math.Max(0, activeYear);
        LatestRequestedYear = Math.Max(ActiveYear, latestRequestedYear);
        DetectionSnapshot = detectionSnapshot;
    }
}

internal readonly struct XjAnnualWorldReduction
{
    internal readonly XjAnnualWorldRuntimeLane.Stage NextStage;
    internal readonly bool CompleteYear;
    internal readonly bool ClearLane;

    private XjAnnualWorldReduction(
        XjAnnualWorldRuntimeLane.Stage nextStage,
        bool completeYear,
        bool clearLane)
    {
        NextStage = nextStage;
        CompleteYear = completeYear;
        ClearLane = clearLane;
    }

    internal static XjAnnualWorldReduction Continue(XjAnnualWorldRuntimeLane.Stage nextStage)
        => new XjAnnualWorldReduction(nextStage, completeYear: false, clearLane: false);

    internal static XjAnnualWorldReduction Complete()
        => new XjAnnualWorldReduction(XjAnnualWorldRuntimeLane.Stage.None, completeYear: true, clearLane: false);

    internal static XjAnnualWorldReduction Clear()
        => new XjAnnualWorldReduction(XjAnnualWorldRuntimeLane.Stage.None, completeYear: false, clearLane: true);
}

/// <summary>
/// 世界年度命令唯一归约器。运行车道只负责排队、预算与按年追赶；
/// 哪个阶段执行何种领域动作、门禁如何判定以及下一阶段是什么，只在此处定义。
/// </summary>
internal static class XjAnnualWorldCommandReducer
{
    internal static XjAnnualWorldReduction Reduce(in XjAnnualWorldCommand command)
    {
        int year = command.ActiveYear;
        int latestYear = command.LatestRequestedYear;
        switch (command.Stage)
        {
            case XjAnnualWorldRuntimeLane.Stage.NativeMagicGuard:
                if (XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.CollapseToLatest, year, latestYear)
                    && TryBegin(XjDetectionJob.NativeMagicRitualGuard, in command))
                {
                    XjNativeMagicRitualGuard.EnsureDisabled();
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.WorldEventCatalog);

            case XjAnnualWorldRuntimeLane.Stage.WorldEventCatalog:
                if (TryBegin(XjDetectionJob.WorldEventCatalog, in command))
                {
                    // 固定世界里程碑看最新世界年，不受逐年补算 backlog 影响；
                    // 其它机会型年度事件仍严格按 activeYear 顺序补齐。
                    XjWorldEventCatalog.TickFixedMilestones(Math.Max(year, latestYear));
                    XjWorldEventCatalog.TickAnnual(year);
                    XjYaoShuGreatSageSystem.TickAnnual(year);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.AdventureRealm);

            case XjAnnualWorldRuntimeLane.Stage.AdventureRealm:
                if (TryBegin(XjDetectionJob.AdventureRealm, in command))
                {
                    XjWorldFortuneSystem.TickYear(year);
                    XjAdventureRealmClaimSystem.TickYear(year);
                    XjAncientShiLegacyJinDiEventSystem.TickYear(year);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SecretRealm);

            case XjAnnualWorldRuntimeLane.Stage.SecretRealm:
                if (!XjSecretRealmConstructionSystem.HasPendingForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.SecretRealm, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SecretRealmTraining);
                    }
                    XjSecretRealmConstructionSystem.BeginYear(year);
                }
                if (XjSecretRealmConstructionSystem.HasPendingForYear(year)
                    && !XjSecretRealmConstructionSystem.TickPending(itemBudget: 6, timeBudgetMs: 0.30d))
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SecretRealm);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SecretRealmTraining);

            case XjAnnualWorldRuntimeLane.Stage.SecretRealmTraining:
                if (!XjSecretRealmTrainingSystem.HasPendingForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.SecretRealmTraining, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.DaoLineageMaintenance);
                    }
                    XjSecretRealmTrainingSystem.BeginYear(year);
                }
                if (XjSecretRealmTrainingSystem.HasPendingForYear(year)
                    && !XjSecretRealmTrainingSystem.TickPending(itemBudget: 2, timeBudgetMs: 0.32d))
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SecretRealmTraining);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.DaoLineageMaintenance);

            case XjAnnualWorldRuntimeLane.Stage.DaoLineageMaintenance:
                if (!XjDaoLineageStateRegistry.HasPendingWorldTickForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.DaoLineageMaintenance, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.RuleInvariantAudit);
                    }
                    XjDaoTaiPresenceArchive.TickWorld(year);
                    XjDaoLineageStateRegistry.BeginWorldTick(year);
                }
                if (XjDaoLineageStateRegistry.HasPendingWorldTickForYear(year)
                    && !XjDaoLineageStateRegistry.TickPendingWorldTick(itemBudget: 4, timeBudgetMs: 0.28d))
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.DaoLineageMaintenance);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.RuleInvariantAudit);

            case XjAnnualWorldRuntimeLane.Stage.RuleInvariantAudit:
                if (XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.CollapseToLatest, year, latestYear)
                    && TryBegin(XjDetectionJob.RuleInvariantAudit, in command))
                {
                    XjRuleInvariantAudit.TryRunDeferredAfterLoad();
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.GuZunMaintenance);

            case XjAnnualWorldRuntimeLane.Stage.GuZunMaintenance:
                if (TryBegin(XjDetectionJob.GuZunMaintenance, in command))
                {
                    XjGuZunRegistry.TickDecennial(year);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.KingdomNameNormalization);

            case XjAnnualWorldRuntimeLane.Stage.KingdomNameNormalization:
                if (XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.CollapseToLatest, year, latestYear)
                    && XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.BoundaryOnly, year, latestYear, 10)
                    && TryBegin(XjDetectionJob.KingdomNameNormalization, in command))
                {
                    XjKingdomNameNormalizationLane.Schedule(year);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.QuanBing);

            case XjAnnualWorldRuntimeLane.Stage.QuanBing:
                if (!XjQuanBingStruggleSystem.HasPendingAnnualWorkForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.QuanBing, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.YinSi);
                    }
                    XjQuanBingStruggleSystem.BeginAnnualWork(year);
                }
                if (XjQuanBingStruggleSystem.HasPendingAnnualWorkForYear(year)
                    && !XjQuanBingStruggleSystem.TickPendingAnnualWork(itemBudget: 4, timeBudgetMs: 0.30d))
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.QuanBing);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.YinSi);

            case XjAnnualWorldRuntimeLane.Stage.YinSi:
                if (!XjYinSiExposurePursuitSystem.HasPendingAnnualWorkForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.YinSi, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.Lecture);
                    }
                    XjYinSiExposurePursuitSystem.BeginYear(year);
                }
                if (XjYinSiExposurePursuitSystem.HasPendingAnnualWorkForYear(year)
                    && !XjYinSiExposurePursuitSystem.TickPendingAnnualWork(itemBudget: 4, timeBudgetMs: 0.30d))
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.YinSi);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.Lecture);

            case XjAnnualWorldRuntimeLane.Stage.Lecture:
                if (!XjSectLectureSystem.HasPendingForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.Lecture, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SectTask);
                    }
                    XjSectLectureSystem.BeginYear(year);
                }
                if (XjSectLectureSystem.HasPendingForYear(year)
                    && !XjSectLectureSystem.TickPending(itemBudget: 2, timeBudgetMs: 0.32d))
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.Lecture);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SectTask);

            case XjAnnualWorldRuntimeLane.Stage.SectTask:
                if (!XjSectAnnualMaintenanceRuntime.HasPendingForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.SectTask, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.CraftRetention);
                    }
                    XjSectAnnualMaintenanceRuntime.BeginYear(year);
                }
                if (XjSectAnnualMaintenanceRuntime.HasPendingForYear(year)
                    && !XjSectAnnualMaintenanceRuntime.TickPending())
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SectTask);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.CraftRetention);

            case XjAnnualWorldRuntimeLane.Stage.CraftRetention:
                if (XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.CollapseToLatest, year, latestYear)
                    && TryBegin(XjDetectionJob.CraftRetention, in command))
                {
                    XjCraftDomainRegistry.PruneClosedTasks(year, retentionYears: 12, removeBudget: 512);
                    XjAlchemyRuntimeRegistry.PruneClosedTasks(year, retentionYears: 12, removeBudget: 512);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.Governance);

            case XjAnnualWorldRuntimeLane.Stage.Governance:
                if (XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.CollapseToLatest, year, latestYear)
                    && TryBegin(XjDetectionJob.Governance, in command))
                {
                    XjSectRuntimeLane.Schedule(year);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.XianGuoPolitics);

            case XjAnnualWorldRuntimeLane.Stage.XianGuoPolitics:
                // Succession reads current native political facts; replaying historical
                // years against today's Kingdom state is both expensive and misleading.
                // The latest year can resolve a lawful successor or expire the grace
                // window in one pass without dropping any chance roll.
                if (XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.CollapseToLatest, year, latestYear)
                    && TryBegin(XjDetectionJob.XianGuoPolitics, in command))
                {
                    XjXianGuoSystem.TickAnnualWorld(year);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SectWar);

            case XjAnnualWorldRuntimeLane.Stage.SectWar:
                if (!XjSectWarSystem.HasPendingAnnualWorkForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.SectWar, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.FamilySupport);
                    }
                    XjSectWarSystem.BeginAnnualWork(year);
                }
                if (XjSectWarSystem.HasPendingAnnualWorkForYear(year)
                    && !XjSectWarSystem.TickPendingAnnualWork(itemBudget: 3, timeBudgetMs: 0.34d))
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.SectWar);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.FamilySupport);

            case XjAnnualWorldRuntimeLane.Stage.FamilySupport:
                if (!XjUnifiedDetectionPlan.ShouldRun(XjDetectionJob.FamilySupport, in command.DetectionSnapshot))
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.HighRealmArmyExclusion);
                }
                if (!XjFamilySupportSystem.HasPendingForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.FamilySupport, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.HighRealmArmyExclusion);
                    }
                    XjFamilySupportSystem.BeginYear(year);
                }
                if (XjFamilySupportSystem.HasPendingForYear(year) && !XjFamilySupportSystem.TickPending())
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.FamilySupport);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.HighRealmArmyExclusion);

            case XjAnnualWorldRuntimeLane.Stage.HighRealmArmyExclusion:
                // Retained as an enum-compatible pass-through for old scheduler state.
                // High-realm demobilization is now realm-change/load event driven; an
                // annual sweep repeatedly vacated native captain slots and made WorldBox
                // refill them, producing the observed multi-captain churn.
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.ThreeBookChronicle);

            case XjAnnualWorldRuntimeLane.Stage.ThreeBookChronicle:
                if (!XjThreeBookWorldObserver.HasPendingForYear(year))
                {
                    if (!TryBegin(XjDetectionJob.ThreeBookChronicle, in command))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.WorldSnapshot);
                    }
                    XjThreeBookWorldObserver.BeginYear(year);
                }
                if (XjThreeBookWorldObserver.HasPendingForYear(year))
                {
                    long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundThreeBookWorld, 0);
                    bool complete = XjThreeBookWorldObserver.TickPending(itemBudget: 3, timeBudgetMs: 0.32d);
                    XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundThreeBookWorld, sample);
                    if (!complete)
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.ThreeBookChronicle);
                    }
                }
                XjChronicleChapterComposer.TryComposeYear(year);
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.WorldSnapshot);

            case XjAnnualWorldRuntimeLane.Stage.WorldSnapshot:
                bool snapshotDue = XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.CollapseToLatest, year, latestYear)
                    && XjAnnualWorldDetectionStore.ShouldRefresh(
                        year,
                        XjDetectionGate.ResolveAnnualIntervalYears(XjDetectionJob.WorldSnapshotMaintenance));
                if (snapshotDue
                    && !XjAnnualWorldDetectionStore.HasSnapshotForYear(year)
                    && !XjAnnualWorldDetectionStore.IsPendingForYear(year)
                    && TryBegin(XjDetectionJob.WorldSnapshotMaintenance, in command))
                {
                    XjAnnualWorldDetectionStore.ScheduleRefresh(year);
                }
                if (XjAnnualWorldDetectionStore.IsPendingForYear(year))
                {
                    return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.WorldSnapshot);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.CenturyDetection);

            case XjAnnualWorldRuntimeLane.Stage.CenturyDetection:
                // 百年谱录是“世界时间已完成的卷册”，不能跟随机会型年度车道逐年追赶。
                // 高倍速时ActiveYear可能落后数百年（例如世界1200年而年度车道仍在300年），
                // 旧逻辑因此恰好只生成3卷。这里始终以最新已请求世界年作为卷册目标年。
                int centuryTargetYear = Math.Max(year, latestYear);
                if (XjCenturyAnnalsBuilder.HasPendingCompletedCentury(centuryTargetYear)
                    && !XjAnnualWorldDetectionStore.HasSnapshotForYear(centuryTargetYear))
                {
                    if (!XjAnnualWorldDetectionStore.IsPendingForYear(centuryTargetYear)
                        && XjDetectionGate.TryBeginAnnualJob(
                            XjDetectionJob.CenturyDetection,
                            centuryTargetYear,
                            "century-target"))
                    {
                        XjAnnualWorldDetectionStore.ScheduleRefresh(centuryTargetYear);
                    }
                    if (XjAnnualWorldDetectionStore.IsPendingForYear(centuryTargetYear)
                        || !XjAnnualWorldDetectionStore.HasSnapshotForYear(centuryTargetYear))
                    {
                        return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.CenturyDetection);
                    }
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.CenturyAnnals);

            case XjAnnualWorldRuntimeLane.Stage.CenturyAnnals:
                int annalsTargetYear = Math.Max(year, latestYear);
                int pendingCenturyId = XjCenturyAnnalsBuilder.ResolveNextPendingCompletedCenturyId(annalsTargetYear);
                if (pendingCenturyId > 0
                    && XjDetectionGate.TryBeginAnnualJob(
                        XjDetectionJob.CenturyAnnals,
                        annalsTargetYear,
                        pendingCenturyId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    XjCenturyAnnalsBuilder.TryBuildAndStore(annalsTargetYear);
                }
                return XjAnnualWorldReduction.Continue(
                    XjCenturyAnnalsBuilder.HasPendingCompletedCentury(annalsTargetYear)
                        ? XjAnnualWorldRuntimeLane.Stage.CenturyDetection
                        : XjAnnualWorldRuntimeLane.Stage.ShiReincarnationReturns);

            case XjAnnualWorldRuntimeLane.Stage.ShiReincarnationReturns:
                // 释修归返是“到期即可执行”的当前态事务，不能等待年度机会车道追到世界最新年。
                // 高倍速下 latestYear 会持续领先 activeYear，旧版 CollapseToLatest 因此可能长期永不成立，
                // 造成已经到期的摩诃/法相真灵一直卡在 Pending。这里与百年谱录一样直接以最新世界年
                // 作为资格目标；资格判断只读取待归返计数/释修稳定索引，不扫描世界人口。
                int shiReturnTargetYear = Math.Max(year, latestYear);
                bool hasShiReturnWork = XjReincarnation.PendingShiCount > 0
                    || XjCultivatorCache.GetShiIds().Count > 0;
                if (hasShiReturnWork
                    && XjDetectionGate.TryBeginAnnualJob(
                        XjDetectionJob.ShiReincarnationReturns,
                        shiReturnTargetYear,
                        "shi-return-latest"))
                {
                    XjReincarnation.SchedulePendingShiReturns(shiReturnTargetYear);
                    XjZhantanlinSystem.SchedulePeriodicMoHeReturns(shiReturnTargetYear);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.Codex);

            case XjAnnualWorldRuntimeLane.Stage.Codex:
                if (XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.CollapseToLatest, year, latestYear)
                    && TryBegin(XjDetectionJob.Codex, in command))
                {
                    XjCodexSnapshotPublisher.ScheduleYear(year);
                }
                return XjAnnualWorldReduction.Continue(XjAnnualWorldRuntimeLane.Stage.MemoryMaintenance);

            case XjAnnualWorldRuntimeLane.Stage.MemoryMaintenance:
                if (XjAnnualWorkPolicy.ShouldRun(XjAnnualCatchUpMode.CollapseToLatest, year, latestYear)
                    && TryBegin(XjDetectionJob.MemoryMaintenance, in command))
                {
                    XjRuntimeMemoryPruner.RunAnnual(year);
                }
                return XjAnnualWorldReduction.Complete();

            default:
                return XjAnnualWorldReduction.Clear();
        }
    }

    private static bool TryBegin(
        XjDetectionJob job,
        in XjAnnualWorldCommand command,
        string discriminator = null)
    {
        return XjUnifiedDetectionPlan.ShouldRun(job, in command.DetectionSnapshot)
            && XjDetectionGate.TryBeginAnnualJob(job, command.ActiveYear, discriminator);
    }
}
