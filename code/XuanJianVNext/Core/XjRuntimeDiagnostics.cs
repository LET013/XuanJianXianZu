using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Core;

internal enum XjRuntimeHotspot : byte
{
	GetHit = 0,
	UpdateFall = 1,
	AnnualPrepare = 2,
	AnnualProgression = 3,
	AnnualFinalize = 4,
	SeedLane = 5,
	BoundedRuntime = 6,
	CityRuntime = 7,
	Cleanup = 8,
	Bootstrap = 9,
	AutoCollectRecheck = 10,
	DeathRuntime = 11,
	DeathArchive = 12,
	CombatRegeneration = 13,
	ClosedCultivation = 14,
	JinDanCombat = 15,
	BackgroundLongShu = 16,
	BackgroundDongTian = 17,
	BackgroundCaiQiCity = 18,
	BackgroundCaiQiSite = 19,
	BackgroundBroadcast = 20,
	BackgroundZongMen = 21,
	BackgroundYinSi = 22,
	BackgroundSectGovernance = 23,
	BackgroundCodexSnapshot = 24,
	BackgroundAnnualWorld = 25,
	AnnualSnapshot = 26,
	AnnualCultivationGrowth = 27,
	AnnualQingXuan = 28,
	AnnualGongFa = 29,
	AnnualHighRealm = 30,
	AnnualBreakthrough = 31,
	AnnualCraft = 32,
	AnnualTalisman = 33,
	AnnualFinalizeResolve = 34,
	AnnualThreeBookSocial = 35,
	BackgroundThreeBookWorld = 36,
	AnnualMaintenanceIdentity = 37,
	AnnualMaintenanceAncillary = 38,
	AnnualMaintenanceAssets = 39
}

/// <summary>
/// Explicit performance-observation diagnostics. Hot paths are sampled and scheduler
/// lanes are measured only while PerformanceObservation is enabled. A log line is
/// emitted only at Critical pressure with a measured hotspot, with an additional
/// cooldown so diagnostics cannot become a log-pressure source.
/// </summary>
internal static class XjRuntimeDiagnostics
{
	private struct HotspotStats
	{
		internal long TotalTicks;
		internal long MaxTicks;
		internal int Calls;

		internal void Add(long ticks)
		{
			if (ticks < 0L) ticks = 0L;
			TotalTicks += ticks;
			Calls++;
			if (ticks > MaxTicks) MaxTicks = ticks;
		}
	}

	private struct NamedDurationStats
	{
		internal long TotalTicks;
		internal long MaxTicks;
		internal int Calls;

		internal void Add(long ticks)
		{
			if (ticks < 0L) ticks = 0L;
			TotalTicks += ticks;
			Calls++;
			if (ticks > MaxTicks) MaxTicks = ticks;
		}
	}

	private const float NormalFlushSeconds = 12f;
	private const float StressedFlushSeconds = 5f;
	private const double ReportTotalThresholdMs = 0.80d;
	private const double ReportMaxThresholdMs = 0.35d;
	private const int MaxReportedHotspots = 8;
	private const int MaxReportedNamedDurations = 8;
	private const int MaximumNamedDurationKeys = 256;
	private const float CriticalLogCooldownSeconds = 20f;

	private static readonly HotspotStats[] Stats = new HotspotStats[Enum.GetValues(typeof(XjRuntimeHotspot)).Length];
	private static readonly Dictionary<string, NamedDurationStats> NamedDurations = new Dictionary<string, NamedDurationStats>(StringComparer.Ordinal);
	private static readonly List<XjRuntimeHotspot> HotspotSortBuffer = new List<XjRuntimeHotspot>(Stats.Length);
	private static readonly List<KeyValuePair<string, NamedDurationStats>> NamedDurationSortBuffer = new List<KeyValuePair<string, NamedDurationStats>>(16);

	private static int _hotPathSampleCursor;
	private static float _nextFlushAt = -1f;
	private static float _nextCriticalLogAt = -1f;

	internal static long BeginSample(XjRuntimeHotspot hotspot, int sampleMask)
	{
		if (!XjRuntimeSettings.PerformanceObservationEnabled) return 0L;
		if (IsSampledHotPath(hotspot) && sampleMask > 0)
		{
			_hotPathSampleCursor++;
			if ((_hotPathSampleCursor & sampleMask) != 0)
			{
				return 0L;
			}
		}

		return Stopwatch.GetTimestamp();
	}

	internal static void EndSample(XjRuntimeHotspot hotspot, long started)
	{
		if (started <= 0L)
		{
			return;
		}

		int index = (int)hotspot;
		if ((uint)index >= (uint)Stats.Length)
		{
			return;
		}

		long elapsed = Stopwatch.GetTimestamp() - started;
		Stats[index].Add(elapsed);
	}

	/// <summary>
	/// Named lane/stage timing for diagnostics. Unlike the fixed hotspot enum this
	/// can attribute legacy atomic work by its concrete scheduler id. Sampling is
	/// disabled with the FPS/performance overlay, so release hot paths do not pay
	/// Stopwatch/dictionary costs unless the user is actively diagnosing pressure.
	/// </summary>
	internal static long BeginNamedSample()
	{
		return XjRuntimeSettings.PerformanceObservationEnabled ? Stopwatch.GetTimestamp() : 0L;
	}

	internal static void EndNamedSample(string name, long started)
	{
		if (started <= 0L || string.IsNullOrWhiteSpace(name)) return;
		long elapsed = Stopwatch.GetTimestamp() - started;
		string key = name.Trim();
		if (!NamedDurations.ContainsKey(key) && NamedDurations.Count >= MaximumNamedDurationKeys) return;
		NamedDurations.TryGetValue(key, out NamedDurationStats stats);
		stats.Add(elapsed);
		NamedDurations[key] = stats;
	}


	internal static void ReportIfDue()
	{
		if (!XjRuntimeSettings.PerformanceObservationEnabled) return;

		float now = Time.unscaledTime;
		if (_nextFlushAt < 0f)
		{
			_nextFlushAt = now + NormalFlushSeconds;
			return;
		}

		float interval = XjRuntimeWorkBudget.StressTier >= XjRuntimeStressTier.Mild
			? StressedFlushSeconds
			: NormalFlushSeconds;
		if (now < _nextFlushAt) return;

		_nextFlushAt = now + interval;
		Flush();
	}

	internal static void Clear()
	{
		Array.Clear(Stats, 0, Stats.Length);
		NamedDurations.Clear();
		HotspotSortBuffer.Clear();
		NamedDurationSortBuffer.Clear();
		_hotPathSampleCursor = 0;
		_nextFlushAt = -1f;
		_nextCriticalLogAt = -1f;
		XjGraphicsMemoryDiagnostics.Clear();
	}

	private static bool IsSampledHotPath(XjRuntimeHotspot hotspot)
	{
		return hotspot == XjRuntimeHotspot.GetHit
			|| hotspot == XjRuntimeHotspot.UpdateFall
			|| hotspot == XjRuntimeHotspot.AnnualSnapshot
			|| hotspot == XjRuntimeHotspot.AnnualCultivationGrowth
			|| hotspot == XjRuntimeHotspot.AnnualQingXuan
			|| hotspot == XjRuntimeHotspot.AnnualGongFa
			|| hotspot == XjRuntimeHotspot.AnnualHighRealm
			|| hotspot == XjRuntimeHotspot.AnnualBreakthrough
			|| hotspot == XjRuntimeHotspot.AnnualCraft
			|| hotspot == XjRuntimeHotspot.AnnualTalisman
			|| hotspot == XjRuntimeHotspot.AnnualFinalizeResolve
			|| hotspot == XjRuntimeHotspot.AnnualThreeBookSocial
			|| hotspot == XjRuntimeHotspot.AnnualMaintenanceIdentity
			|| hotspot == XjRuntimeHotspot.AnnualMaintenanceAncillary
			|| hotspot == XjRuntimeHotspot.AnnualMaintenanceAssets;
	}

	private static void Flush()
	{
		XjGraphicsMemoryDiagnostics.SampleIfDue();
		HotspotSortBuffer.Clear();
		bool hasTimedPressure = false;
		for (int i = 0; i < Stats.Length; i++)
		{
			HotspotStats stat = Stats[i];
			if (stat.Calls <= 0)
			{
				continue;
			}

			double totalMs = TicksToMilliseconds(stat.TotalTicks);
			double maxMs = TicksToMilliseconds(stat.MaxTicks);
			if (totalMs >= ReportTotalThresholdMs || maxMs >= ReportMaxThresholdMs)
			{
				hasTimedPressure = true;
			}
			HotspotSortBuffer.Add((XjRuntimeHotspot)i);
		}

		HotspotSortBuffer.Sort((left, right) =>
		{
			int ticks = Stats[(int)right].TotalTicks.CompareTo(Stats[(int)left].TotalTicks);
			return ticks != 0 ? ticks : ((int)left).CompareTo((int)right);
		});

		NamedDurationSortBuffer.Clear();
		foreach (KeyValuePair<string, NamedDurationStats> pair in NamedDurations)
		{
			if (pair.Value.Calls <= 0) continue;
			if (TicksToMilliseconds(pair.Value.TotalTicks) >= ReportTotalThresholdMs
				|| TicksToMilliseconds(pair.Value.MaxTicks) >= ReportMaxThresholdMs)
			{
				hasTimedPressure = true;
			}
			NamedDurationSortBuffer.Add(pair);
		}
		NamedDurationSortBuffer.Sort((left, right) =>
		{
			int ticks = right.Value.TotalTicks.CompareTo(left.Value.TotalTicks);
			return ticks != 0 ? ticks : string.Compare(left.Key, right.Key, StringComparison.Ordinal);
		});

		StringBuilder builder = new StringBuilder(1024);
		builder.Append("[玄鉴][性能] fps=")
			.Append(XjRuntimeWorkBudget.SmoothedFramesPerSecond.ToString("F1", CultureInfo.InvariantCulture))
			.Append(" stress=")
			.Append(XjRuntimeWorkBudget.StressTier)
			.Append(" year=")
			.Append(World.world?.map_stats?.year ?? 0)
			.Append(" speed=")
			.Append(Time.timeScale.ToString("F1", CultureInfo.InvariantCulture));

		string graphicsSummary = XjGraphicsMemoryDiagnostics.ReadLatestSummary();
		if (!string.IsNullOrWhiteSpace(graphicsSummary))
		{
			builder.Append(" gfx=").Append(graphicsSummary);
		}

		if (HotspotSortBuffer.Count > 0)
		{
			builder.Append(" top=");
			int reported = Math.Min(MaxReportedHotspots, HotspotSortBuffer.Count);
			for (int i = 0; i < reported; i++)
			{
				if (i > 0) builder.Append(" | ");
				XjRuntimeHotspot hotspot = HotspotSortBuffer[i];
				HotspotStats stat = Stats[(int)hotspot];
				builder.Append(HotspotName(hotspot))
					.Append(':')
					.Append(TicksToMilliseconds(stat.TotalTicks).ToString("F2", CultureInfo.InvariantCulture))
					.Append("ms/")
					.Append(stat.Calls)
					.Append("c max")
					.Append(TicksToMilliseconds(stat.MaxTicks).ToString("F2", CultureInfo.InvariantCulture))
					.Append("ms");
			}
		}
		else
		{
			builder.Append(" top=none");
		}

		if (NamedDurationSortBuffer.Count > 0)
		{
			builder.Append(" lanes=");
			int reportedDurations = Math.Min(MaxReportedNamedDurations, NamedDurationSortBuffer.Count);
			for (int i = 0; i < reportedDurations; i++)
			{
				if (i > 0) builder.Append(" | ");
				KeyValuePair<string, NamedDurationStats> pair = NamedDurationSortBuffer[i];
				builder.Append(pair.Key)
					.Append(':')
					.Append(TicksToMilliseconds(pair.Value.TotalTicks).ToString("F2", CultureInfo.InvariantCulture))
					.Append("ms/")
					.Append(pair.Value.Calls)
					.Append("c max")
					.Append(TicksToMilliseconds(pair.Value.MaxTicks).ToString("F2", CultureInfo.InvariantCulture))
					.Append("ms");
			}
		}

		string heartbeatSummary = XjSimulationHeartbeat.BuildDiagnosticSummary();
		if (!string.IsNullOrWhiteSpace(heartbeatSummary))
		{
			builder.Append(" heartbeat=").Append(heartbeatSummary);
		}

		int currentYear = World.world?.map_stats?.year ?? 0;
		string semanticSummary = XjSemanticDiagnostics.BuildYearSummary(currentYear);
		if (!string.IsNullOrWhiteSpace(semanticSummary))
		{
			if (semanticSummary.Length > 480)
			{
				semanticSummary = semanticSummary.Substring(0, 480) + "...";
			}
			builder.Append(" semantic=").Append(semanticSummary);
		}

		string report = builder.ToString();
		// 只有“关键压力 + 实测热点越阈”才以20秒上限写一次日志，
		// 避免诊断本身在正常/轻微压力下成为额外IO与字符串高压源。
		float now = Time.unscaledTime;
		if (XjRuntimeWorkBudget.StressTier >= XjRuntimeStressTier.Critical
			&& hasTimedPressure
			&& (_nextCriticalLogAt < 0f || now >= _nextCriticalLogAt))
		{
			_nextCriticalLogAt = now + CriticalLogCooldownSeconds;
			UnityEngine.Debug.Log(report);
		}
		ClearWindow();
	}

	private static void ClearWindow()
	{
		Array.Clear(Stats, 0, Stats.Length);
		NamedDurations.Clear();
		HotspotSortBuffer.Clear();
		NamedDurationSortBuffer.Clear();
	}

	private static double TicksToMilliseconds(long ticks)
	{
		return ticks <= 0L ? 0d : ticks * 1000d / Stopwatch.Frequency;
	}

	private static string HotspotName(XjRuntimeHotspot hotspot)
	{
		return hotspot switch
		{
			XjRuntimeHotspot.GetHit => "combat.getHit",
			XjRuntimeHotspot.UpdateFall => "actor.updateFall",
			XjRuntimeHotspot.AnnualPrepare => "annual.prepare",
			XjRuntimeHotspot.AnnualProgression => "annual.progress",
			XjRuntimeHotspot.AnnualFinalize => "annual.finalize",
			XjRuntimeHotspot.SeedLane => "seed",
			XjRuntimeHotspot.BoundedRuntime => "bounded",
			XjRuntimeHotspot.CityRuntime => "city",
			XjRuntimeHotspot.Cleanup => "cleanup",
			XjRuntimeHotspot.Bootstrap => "bootstrap",
			XjRuntimeHotspot.AutoCollectRecheck => "autoCollect",
			XjRuntimeHotspot.DeathRuntime => "death",
			XjRuntimeHotspot.DeathArchive => "deathArchive",
			XjRuntimeHotspot.CombatRegeneration => "combatRegen",
			XjRuntimeHotspot.ClosedCultivation => "closedCultivation",
			XjRuntimeHotspot.JinDanCombat => "jindanCombat",
			XjRuntimeHotspot.BackgroundLongShu => "bg.longshu",
			XjRuntimeHotspot.BackgroundDongTian => "bg.dongtian",
			XjRuntimeHotspot.BackgroundCaiQiCity => "bg.caiqiCity",
			XjRuntimeHotspot.BackgroundCaiQiSite => "bg.caiqiSite",
			XjRuntimeHotspot.BackgroundBroadcast => "bg.broadcast",
			XjRuntimeHotspot.BackgroundZongMen => "bg.zongmen",
			XjRuntimeHotspot.BackgroundYinSi => "bg.yinsi",
			XjRuntimeHotspot.BackgroundSectGovernance => "bg.sectGov",
			XjRuntimeHotspot.BackgroundCodexSnapshot => "bg.codex",
			XjRuntimeHotspot.BackgroundAnnualWorld => "bg.annualWorld",
			XjRuntimeHotspot.AnnualSnapshot => "annual.snapshot",
			XjRuntimeHotspot.AnnualCultivationGrowth => "annual.growth",
			XjRuntimeHotspot.AnnualQingXuan => "annual.qingxuan",
			XjRuntimeHotspot.AnnualGongFa => "annual.gongfa",
			XjRuntimeHotspot.AnnualHighRealm => "annual.highRealm",
			XjRuntimeHotspot.AnnualBreakthrough => "annual.breakthrough",
			XjRuntimeHotspot.AnnualCraft => "annual.craft",
			XjRuntimeHotspot.AnnualTalisman => "annual.talisman",
			XjRuntimeHotspot.AnnualFinalizeResolve => "annual.finalizeResolve",
			XjRuntimeHotspot.AnnualThreeBookSocial => "annual.threeBookSocial",
			XjRuntimeHotspot.AnnualMaintenanceIdentity => "annual.maintenance.identity",
			XjRuntimeHotspot.AnnualMaintenanceAncillary => "annual.maintenance.ancillary",
			XjRuntimeHotspot.AnnualMaintenanceAssets => "annual.maintenance.assets",
			_ => hotspot.ToString()
		};
	}
}
