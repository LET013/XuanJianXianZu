using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Localization;

namespace XuanJianVNext.Systems.Runtime;

internal enum XjDetectionLaneBudgetKind : byte
{
    Actor = 0,
    Kingdom = 1,
    Shared = 2
}

/// <summary>
/// Single runtime consumer for bounded detection/reconciliation scans.
///
/// Lanes register once and only expose stable pending/cursor operations. The
/// coordinator owns fairness and the shared rendered-frame slice. A round-robin
/// cursor prevents an always-pending whole-world actor scan from starving smaller
/// reconciliation jobs such as kingdom-name normalization.
/// </summary>
internal static class XjUnifiedDetectionRuntime
{
    private sealed class Lane
    {
        internal readonly string Id;
        internal readonly XjDetectionLaneBudgetKind BudgetKind;
        internal readonly Func<bool> HasPending;
        internal readonly Func<int> PendingCount;
        internal readonly Func<int, double, int> Tick;
        internal readonly Action Clear;

        internal Lane(
            string id,
            XjDetectionLaneBudgetKind budgetKind,
            Func<bool> hasPending,
            Func<int> pendingCount,
            Func<int, double, int> tick,
            Action clear)
        {
            Id = id;
            BudgetKind = budgetKind;
            HasPending = hasPending;
            PendingCount = pendingCount;
            Tick = tick;
            Clear = clear;
        }
    }

    private static readonly List<Lane> Lanes = new List<Lane>(8);
    private static bool _defaultsRegistered;
    private static int _roundRobinCursor;

    internal static bool HasPending
    {
        get
        {
            EnsureDefaultLanes();
            for (int i = 0; i < Lanes.Count; i++)
            {
                if (Lanes[i].HasPending()) return true;
            }
            return false;
        }
    }

    internal static int PendingCount
    {
        get
        {
            EnsureDefaultLanes();
            long total = 0L;
            for (int i = 0; i < Lanes.Count; i++)
            {
                Lane lane = Lanes[i];
                if (!lane.HasPending()) continue;
                total += Math.Max(0, lane.PendingCount());
                if (total >= int.MaxValue) return int.MaxValue;
            }
            return (int)total;
        }
    }

    /// <summary>
    /// Registration point for future low-frequency detection/reconciliation lanes.
    /// Duplicate IDs are ignored so feature bootstrap can be safely idempotent.
    /// </summary>
    internal static bool Register(
        string laneId,
        XjDetectionLaneBudgetKind budgetKind,
        Func<bool> hasPending,
        Func<int> pendingCount,
        Func<int, double, int> tick,
        Action clear)
    {
        if (string.IsNullOrWhiteSpace(laneId)
            || hasPending == null
            || pendingCount == null
            || tick == null
            || clear == null)
        {
            return false;
        }

        string normalized = laneId.Trim();
        for (int i = 0; i < Lanes.Count; i++)
        {
            if (string.Equals(Lanes[i].Id, normalized, StringComparison.Ordinal)) return false;
        }

        Lanes.Add(new Lane(normalized, budgetKind, hasPending, pendingCount, tick, clear));
        return true;
    }

    internal static int Tick(int actorBudget, int kingdomBudget, double timeBudgetMs)
    {
        return Tick(actorBudget, kingdomBudget, timeBudgetMs, XjRuntimeFramePriority.Background);
    }

    internal static int Tick(
        int actorBudget,
        int kingdomBudget,
        double timeBudgetMs,
        XjRuntimeFramePriority priority)
    {
        EnsureDefaultLanes();
        if (Lanes.Count == 0 || timeBudgetMs <= 0d
            || !XjRuntimeFrameGovernor.CanContinue(priority))
        {
            return 0;
        }

        int pendingLaneCount = CountPendingLanes();
        if (pendingLaneCount <= 0) return 0;

        int processed = 0;
        int laneCount = Lanes.Count;
        int start = NormalizeCursor(_roundRobinCursor, laneCount);
        XjCooperativeBudget coordinatorBudget = new XjCooperativeBudget(
            laneCount,
            timeBudgetMs,
            priority);

        int visited = 0;
        int lastVisitedIndex = start;
        while (visited < laneCount && coordinatorBudget.TryTake())
        {
            int index = (start + visited) % laneCount;
            visited++;
            lastVisitedIndex = index;
            Lane lane = Lanes[index];
            if (!lane.HasPending()) continue;

            double remainingMilliseconds = coordinatorBudget.RemainingMilliseconds;
            if (remainingMilliseconds <= 0.02d) break;

            int lanesStillPending = Math.Max(1, pendingLaneCount);
            // Reserve a fair share for the remaining registered work. Small lanes
            // therefore make progress even while a 10k-actor snapshot is active.
            double laneSliceMilliseconds = Math.Max(
                0.02d,
                remainingMilliseconds / lanesStillPending);
            int itemBudget = ResolveItemBudget(lane.BudgetKind, actorBudget, kingdomBudget);
            if (itemBudget <= 0) continue;

            processed += lane.Tick(itemBudget, laneSliceMilliseconds);
            pendingLaneCount = Math.Max(0, pendingLaneCount - 1);
            XjRuntimeFrameGovernor.MarkPotentialOverrun("unified-detection." + lane.Id);
            if (coordinatorBudget.ShouldYield) break;
        }

        // Whether the loop stopped for local time or the global frame governor, the
        // next call starts after the last lane that had a chance to run.
        _roundRobinCursor = laneCount > 0 ? (lastVisitedIndex + 1) % laneCount : 0;
        return processed;
    }

    internal static void CollectInvariantIssues(List<string> issues)
    {
        if (issues == null) return;
        EnsureDefaultLanes();
        string[] required = { "annual-cultivator-audit", "world-actors", "kingdom-names" };
        for (int r = 0; r < required.Length; r++)
        {
            bool found = false;
            for (int i = 0; i < Lanes.Count; i++)
            {
                if (string.Equals(Lanes[i].Id, required[r], StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }
            if (!found) issues.Add("统一 Detection Coordinator 缺少基础车道：" + required[r]);
        }
    }

    internal static string BuildSummary()
    {
        EnsureDefaultLanes();
        return "lanes=" + Lanes.Count + ", cursor=" + _roundRobinCursor;
    }

    internal static void Clear()
    {
        EnsureDefaultLanes();
        for (int i = 0; i < Lanes.Count; i++)
        {
            Lanes[i].Clear();
        }
        _roundRobinCursor = 0;
    }

    private static void EnsureDefaultLanes()
    {
        if (_defaultsRegistered) return;
        _defaultsRegistered = true;
        Register(
            "annual-cultivator-audit",
            XjDetectionLaneBudgetKind.Actor,
            () => XjAnnualCultivatorSweepLane.HasPending,
            () => XjAnnualCultivatorSweepLane.PendingCount,
            XjAnnualCultivatorSweepLane.Tick,
            XjAnnualCultivatorSweepLane.Clear);
        Register(
            "world-actors",
            XjDetectionLaneBudgetKind.Actor,
            () => XjAnnualWorldDetectionStore.HasPending,
            () => XjAnnualWorldDetectionStore.PendingCount,
            XjAnnualWorldDetectionStore.Tick,
            XjAnnualWorldDetectionStore.Clear);
        Register(
            "kingdom-names",
            XjDetectionLaneBudgetKind.Kingdom,
            () => XjKingdomNameNormalizationLane.HasPending,
            () => XjKingdomNameNormalizationLane.PendingCount,
            XjKingdomNameNormalizationLane.Tick,
            XjKingdomNameNormalizationLane.Clear);
    }

    private static int CountPendingLanes()
    {
        int count = 0;
        for (int i = 0; i < Lanes.Count; i++)
        {
            if (Lanes[i].HasPending()) count++;
        }
        return count;
    }

    private static int ResolveItemBudget(
        XjDetectionLaneBudgetKind budgetKind,
        int actorBudget,
        int kingdomBudget)
    {
        return budgetKind switch
        {
            XjDetectionLaneBudgetKind.Actor => Math.Max(1, actorBudget),
            XjDetectionLaneBudgetKind.Kingdom => Math.Max(1, kingdomBudget),
            _ => Math.Max(1, Math.Min(Math.Max(1, actorBudget), Math.Max(1, kingdomBudget)))
        };
    }

    private static int NormalizeCursor(int cursor, int count)
    {
        if (count <= 0) return 0;
        if (cursor < 0) cursor = 0;
        return cursor % count;
    }
}
