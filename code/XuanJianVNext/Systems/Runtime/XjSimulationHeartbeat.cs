using System;
using System.Globalization;
using UnityEngine;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// Minimal always-on health summary built only from signals that are actually
/// wired in production: annual backlog/semantic lag, world-lookup pressure and
/// low-frequency invariant repair results. Native map/actor pipeline probes are
/// deliberately not modeled here unless a real producer is installed.
/// </summary>
internal static class XjSimulationHeartbeat
{
	private const float PendingPressureSeconds = 4.0f;

	private static float _lookupPendingSince = -1f;
	private static int _cachedAnnualBacklog;
	private static int _cachedSemanticLag;

	internal static void ObserveRenderedFrame()
	{
		float now = Time.unscaledTime;
		UpdatePendingSince(ref _lookupPendingSince, XjWorldLookupIndex.PendingMaintenanceCount > 0, now);
	}

	internal static string BuildDiagnosticSummary()
	{
		RefreshSchedulerPressure();
		return BuildSummary(Time.unscaledTime, _cachedAnnualBacklog, _cachedSemanticLag);
	}

	private static void RefreshSchedulerPressure()
	{
		XjSchedulerPressureSnapshot pressure = XjScheduler.CapturePerformancePressureSnapshot();
		_cachedAnnualBacklog = Math.Max(0, pressure.AnnualIngress)
			+ Math.Max(0, pressure.AnnualCore)
			+ Math.Max(0, pressure.AnnualSecondary)
			+ Math.Max(0, pressure.AnnualMaintenance);
		int year = Math.Max(0, World.world?.map_stats?.year ?? 0);
		_cachedSemanticLag = pressure.OldestPendingSemanticYear > 0
			? Math.Max(0, year - pressure.OldestPendingSemanticYear)
			: 0;
	}

	private static string BuildSummary(float now, int annualBacklog, int semanticLag)
	{
		float lookupPendingAge = AgeSeconds(now, _lookupPendingSince);
		int lookupPending = XjWorldLookupIndex.PendingMaintenanceCount;
		int year = Math.Max(0, World.world?.map_stats?.year ?? 0);
		bool recentInvariantDrift = XjRuntimeInvariantLane.LastIssueCount >= 8
			&& XjRuntimeInvariantLane.LastRepairCount > 0
			&& XjRuntimeInvariantLane.LastCompletedYear > 0
			&& year - XjRuntimeInvariantLane.LastCompletedYear <= 1;

		string state = Classify(annualBacklog, semanticLag, lookupPendingAge, recentInvariantDrift);

		return state
			+ ",annual=" + annualBacklog.ToString(CultureInfo.InvariantCulture)
			+ ",lag=" + semanticLag.ToString(CultureInfo.InvariantCulture) + "y"
			+ ",lookup=" + lookupPending.ToString(CultureInfo.InvariantCulture) + "@" + FormatSeconds(lookupPendingAge)
			+ ",invariant=" + XjRuntimeInvariantLane.LastIssueCount.ToString(CultureInfo.InvariantCulture)
			+ "/fix" + XjRuntimeInvariantLane.LastRepairCount.ToString(CultureInfo.InvariantCulture);
	}

	internal static void Clear()
	{
		_lookupPendingSince = -1f;
		_cachedAnnualBacklog = 0;
		_cachedSemanticLag = 0;
	}

	private static string Classify(
		int annualBacklog,
		int semanticLag,
		float lookupPendingAge,
		bool recentInvariantDrift)
	{
		if (World.world == null) return "NO_WORLD";
		if (!IsWorldExpectedToProgress()) return "PAUSED";
		if (recentInvariantDrift) return "INDEX_DRIFT_REPAIRED";
		int heavyBacklog = Math.Max(256, XjCultivatorCache.Count / 4);
		if (semanticLag >= 3 || annualBacklog >= heavyBacklog) return "SCHEDULER_BACKLOG";
		if (lookupPendingAge >= PendingPressureSeconds) return "WORLD_LOOKUP_RECONCILE_PRESSURE";
		return "HEALTHY";
	}

	private static bool IsWorldExpectedToProgress()
	{
		return World.world != null
			&& !Config.paused
			&& XjRuntimeWorkBudget.CurrentWorldSpeed > 0.001f
			&& !XjRuntimePauseGate.BlocksSimulation;
	}

	private static void UpdatePendingSince(ref float startedAt, bool pending, float now)
	{
		if (!pending)
		{
			startedAt = -1f;
			return;
		}
		if (startedAt < 0f) startedAt = now;
	}

	private static float AgeSeconds(float now, float timestamp)
	{
		return timestamp < 0f ? 0f : Math.Max(0f, now - timestamp);
	}

	private static string FormatSeconds(float value)
	{
		return value.ToString("F2", CultureInfo.InvariantCulture) + "s";
	}
}
