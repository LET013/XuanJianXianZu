using System.Diagnostics;

namespace XuanJianVNext.Core;

/// <summary>
/// Priority classes for the single rendered-frame runtime budget. Critical work is
/// correctness-sensitive and may always enter; every other lane must yield when
/// the cumulative mod wall-clock for the current Unity frame is exhausted.
/// </summary>
internal enum XjRuntimeFramePriority : byte
{
	Critical = 0,
	CoreAnnual = 1,
	Maintenance = 2,
	Background = 3
}

/// <summary>
/// Global wall-clock governor for XuanJian runtime work.
///
/// Individual lanes already have local item/time budgets, but before 0.9.11 these
/// budgets could stack in the same rendered frame: annual ingress, core/secondary
/// cultivation, maintenance, deaths, domains, detections and background work were
/// each bounded independently. At x20/x40 that still produced occasional frame
/// spikes. This governor adds the missing cumulative ceiling without discarding
/// semantic queues: unfinished work simply remains pending for a later frame.
/// </summary>
internal static class XjRuntimeFrameGovernor
{
	private static long _frameStartedTimestamp;
	private static double _frameBudgetMilliseconds = 3.0d;
	private static int _unityFrame = -1;
	private static bool _reportedExhaustion;

	internal static double ElapsedMilliseconds
	{
		get
		{
			if (_frameStartedTimestamp <= 0L) return 0d;
			return (Stopwatch.GetTimestamp() - _frameStartedTimestamp) * 1000d / Stopwatch.Frequency;
		}
	}
	internal static double RemainingMilliseconds => System.Math.Max(0d, _frameBudgetMilliseconds - ElapsedMilliseconds);

	internal static void BeginFrame()
	{
		int frame = UnityEngine.Time.frameCount;
		if (_unityFrame == frame && _frameStartedTimestamp > 0L) return;
		_unityFrame = frame;
		_frameStartedTimestamp = Stopwatch.GetTimestamp();
		_reportedExhaustion = false;
		_frameBudgetMilliseconds = ResolveBudgetMilliseconds();
	}

	internal static bool CanContinue(XjRuntimeFramePriority priority)
	{
		if (priority == XjRuntimeFramePriority.Critical) return true;
		if (_frameStartedTimestamp <= 0L) BeginFrame();

		double fraction = priority switch
		{
			XjRuntimeFramePriority.CoreAnnual => 1.00d,
			XjRuntimeFramePriority.Maintenance => 0.88d,
			XjRuntimeFramePriority.Background => 0.72d,
			_ => 1.00d
		};
		bool allowed = ElapsedMilliseconds < _frameBudgetMilliseconds * fraction;
		if (!allowed) ReportExhaustion();
		return allowed;
	}

	internal static void MarkPotentialOverrun(string laneId)
	{
		if (_frameStartedTimestamp <= 0L || ElapsedMilliseconds <= _frameBudgetMilliseconds) return;
		ReportExhaustion();
		if (!string.IsNullOrWhiteSpace(laneId))
		{
			int rounded = ElapsedMilliseconds >= int.MaxValue
				? int.MaxValue
				: System.Math.Max(1, (int)System.Math.Ceiling(ElapsedMilliseconds));
			string key = "frameOverrun." + laneId;
			XjPerformanceTelemetry.ObserveQueue(key, rounded);
		}
	}

	internal static void Clear()
	{
		_frameStartedTimestamp = 0L;
		_frameBudgetMilliseconds = 3.0d;
		_unityFrame = -1;
		_reportedExhaustion = false;
	}

	private static double ResolveBudgetMilliseconds()
	{
		double baseBudget = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 0.60d,
			XjRuntimeStressTier.Severe => 0.92d,
			XjRuntimeStressTier.Mild => 1.45d,
			_ => ResolveNormalBudgetBySpeed(XjRuntimeWorkBudget.CurrentWorldSpeed)
		};
		double scaled = baseBudget * XjRuntimeWorkBudget.PopulationPressureMultiplier;
		return System.Math.Max(0.40d, scaled);
	}

	private static double ResolveNormalBudgetBySpeed(float speed)
	{
		// Release budgets intentionally leave more of the rendered frame to native
		// WorldBox AI/pathfinding. Backlogs are controlled by coalescing/sparse gates
		// instead of buying throughput with 3-5ms of mod work every frame.
		if (speed <= 2.01f) return 3.20d;
		if (speed <= 5.01f) return 2.70d;
		if (speed <= 10.01f) return 2.15d;
		if (speed <= 20.01f) return 1.70d;
		return 1.35d;
	}

	private static void ReportExhaustion()
	{
		if (_reportedExhaustion) return;
		_reportedExhaustion = true;
		XjPerformanceTelemetry.ObserveQueue("frameBudgetExhausted", 1);
	}
}
