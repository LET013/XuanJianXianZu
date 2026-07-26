using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

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
	BackgroundThreeBookWorld = 36
}

/// <summary>
/// Low-frequency runtime pressure diagnostics. Hot paths are sampled, while
/// scheduler lanes are measured directly and flushed as one compact log line
/// only under frame pressure or when a lane crosses a small time threshold.
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

	private const float NormalFlushSeconds = 12f;
	private const float StressedFlushSeconds = 5f;
	private const double ReportTotalThresholdMs = 0.80d;
	private const double ReportMaxThresholdMs = 0.35d;
	private const int MaxReportedHotspots = 8;
	private const int MaxReportedQueues = 8;

	private static readonly HotspotStats[] Stats = new HotspotStats[Enum.GetValues(typeof(XjRuntimeHotspot)).Length];
	private static readonly Dictionary<string, int> QueueHighWater = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly List<XjRuntimeHotspot> HotspotSortBuffer = new List<XjRuntimeHotspot>(Stats.Length);
	private static readonly List<KeyValuePair<string, int>> QueueSortBuffer = new List<KeyValuePair<string, int>>(16);

	private static int _hotPathSampleCursor;
	private static float _nextFlushAt = -1f;

	internal static long BeginSample(XjRuntimeHotspot hotspot, int sampleMask)
	{
		if (!XjRuntimeSettings.ShowFpsOverlayEnabled) return 0L;
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

	internal static void SampleQueue(string name, int count)
	{
		if (!XjRuntimeSettings.ShowFpsOverlayEnabled) return;
		if (string.IsNullOrEmpty(name) || count <= 0)
		{
			return;
		}

		if (!QueueHighWater.TryGetValue(name, out int previous) || count > previous)
		{
			QueueHighWater[name] = count;
		}
	}

	internal static void ReportIfDue(int knownActors, int cultivators, int annualBacklog, int seedBacklog)
	{
		if (!XjRuntimeSettings.ShowFpsOverlayEnabled) return;
		SampleQueue("actors", knownActors);
		SampleQueue("cultivators", cultivators);
		SampleQueue("annual", annualBacklog);
		SampleQueue("seed", seedBacklog);

		float now = Time.unscaledTime;
		if (_nextFlushAt < 0f)
		{
			_nextFlushAt = now + NormalFlushSeconds;
			return;
		}

		float interval = XjRuntimeWorkBudget.StressTier >= XjRuntimeStressTier.Mild
			? StressedFlushSeconds
			: NormalFlushSeconds;
		if (now < _nextFlushAt)
		{
			return;
		}

		_nextFlushAt = now + interval;
		Flush();
	}

	internal static void Clear()
	{
		Array.Clear(Stats, 0, Stats.Length);
		QueueHighWater.Clear();
		HotspotSortBuffer.Clear();
		QueueSortBuffer.Clear();
		_hotPathSampleCursor = 0;
		_nextFlushAt = -1f;
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
			|| hotspot == XjRuntimeHotspot.AnnualThreeBookSocial;
	}

	private static void Flush()
	{
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

		bool stressed = XjRuntimeWorkBudget.StressTier >= XjRuntimeStressTier.Mild;
		if (!stressed && !hasTimedPressure)
		{
			ClearWindow();
			return;
		}

		HotspotSortBuffer.Sort((left, right) =>
		{
			int ticks = Stats[(int)right].TotalTicks.CompareTo(Stats[(int)left].TotalTicks);
			return ticks != 0 ? ticks : ((int)left).CompareTo((int)right);
		});

		QueueSortBuffer.Clear();
		foreach (KeyValuePair<string, int> pair in QueueHighWater)
		{
			if (pair.Value > 0)
			{
				QueueSortBuffer.Add(pair);
			}
		}
		QueueSortBuffer.Sort((left, right) =>
		{
			int value = right.Value.CompareTo(left.Value);
			return value != 0 ? value : string.Compare(left.Key, right.Key, StringComparison.Ordinal);
		});

		StringBuilder builder = new StringBuilder(768);
		builder.Append("[玄鉴][性能] fps=")
			.Append(XjRuntimeWorkBudget.SmoothedFramesPerSecond.ToString("F1", CultureInfo.InvariantCulture))
			.Append(" stress=")
			.Append(XjRuntimeWorkBudget.StressTier)
			.Append(" year=")
			.Append(World.world?.map_stats?.year ?? 0)
			.Append(" speed=")
			.Append(Time.timeScale.ToString("F1", CultureInfo.InvariantCulture));

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

		if (QueueSortBuffer.Count > 0)
		{
			builder.Append(" queues=");
			int reportedQueues = Math.Min(MaxReportedQueues, QueueSortBuffer.Count);
			for (int i = 0; i < reportedQueues; i++)
			{
				if (i > 0) builder.Append(',');
				builder.Append(QueueSortBuffer[i].Key)
					.Append(':')
					.Append(QueueSortBuffer[i].Value);
			}
		}

		UnityEngine.Debug.Log(builder.ToString());
		ClearWindow();
	}

	private static void ClearWindow()
	{
		Array.Clear(Stats, 0, Stats.Length);
		QueueHighWater.Clear();
		HotspotSortBuffer.Clear();
		QueueSortBuffer.Clear();
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
			_ => hotspot.ToString()
		};
	}
}
