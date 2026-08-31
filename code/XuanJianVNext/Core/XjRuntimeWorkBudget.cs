using UnityEngine;

namespace XuanJianVNext.Core;

internal enum XjRuntimeStressTier : byte
{
	Normal = 0,
	Mild = 1,
	Severe = 2,
	Critical = 3
}

/// <summary>
/// Runtime-only work budget derived from recent frame time.
/// It never changes rules or drops queued work; it only limits how much work may
/// be consumed in one frame and how often non-critical background phases run.
/// </summary>
internal static class XjRuntimeWorkBudget
{
	private const float SmoothFactor = 0.08f;
	private const float MildFrameSeconds = 1f / 30f;
	private const float SevereFrameSeconds = 1f / 22f;
	private const float CriticalFrameSeconds = 1f / 14f;
	private const float StressHoldSeconds = 4f;

	private static float _smoothedDelta = 1f / 30f;
	private static float _stressHoldUntil = -1f;
	private static XjRuntimeStressTier _stressTier;
	private static int _backgroundStrideCounter;
	private static int _annualPriorityStrideCounter;
	private static int _memoryMaintenanceStrideCounter;
	private static int _lastSampledUnityFrame = -1;
	private static int _lastFastSchedulerUnityFrame = -1;
	private static int _lastGrowthSchedulerUnityFrame = -1;
	private static int _lastArchiveUnityFrame = -1;
	private static int _lastTerrainUnityFrame = -1;
	private static int _lastMemoryMaintenanceUnityFrame = -1;
	private static int _worldUnitCount;
	private static float _populationPressureMultiplier = 1f;

	internal static XjRuntimeStressTier StressTier => _stressTier;
	internal static float SmoothedFramesPerSecond => _smoothedDelta > 0.0001f ? 1f / _smoothedDelta : 30f;
	internal static float CurrentWorldSpeed => Config.time_scale_asset?.multiplier ?? Time.timeScale;
	internal static int WorldUnitCount => _worldUnitCount;
	internal static float PopulationPressureMultiplier => _populationPressureMultiplier;

	/// <summary>
	/// Full archive encoding and retained-container rebuilds are main-thread, allocation-heavy work.
	/// They are allowed only at low simulation speed and normal frame pressure. Exact WorldBox
	/// save snapshots bypass this gate and still commit synchronously for correctness.
	/// </summary>
	internal static bool CanRunExpensiveSynchronousMaintenance =>
		_stressTier == XjRuntimeStressTier.Normal && CurrentWorldSpeed <= 2.01f;

	internal static void SampleFrame()
	{
		int unityFrame = Time.frameCount;
		if (_lastSampledUnityFrame == unityFrame)
		{
			return;
		}
		_lastSampledUnityFrame = unityFrame;

		try { _worldUnitCount = World.world?.units?.Count ?? 0; }
		catch { _worldUnitCount = 0; }
		_populationPressureMultiplier = ResolvePopulationPressureMultiplier(_worldUnitCount);

		float delta = Time.unscaledDeltaTime;
		if (delta <= 0f)
		{
			return;
		}

		// A multi-second main-thread stall used to be discarded as an outlier.
		// That left the overlay showing the pre-stall FPS and immediately restored
		// full scheduler budgets after the hitch. Record the real stall, clamp only
		// the smoothing input, and hold the runtime in critical back-pressure.
		if (delta > 2f)
		{
			int stallMilliseconds = delta >= int.MaxValue / 1000f
				? int.MaxValue
				: Mathf.RoundToInt(delta * 1000f);
			XjPerformanceTelemetry.ObserveQueue("mainThreadStallMs", stallMilliseconds);
			_smoothedDelta = Mathf.Lerp(_smoothedDelta, 2f, SmoothFactor);
			_stressHoldUntil = Time.unscaledTime + StressHoldSeconds;
			_stressTier = XjRuntimeStressTier.Critical;
			return;
		}

		_smoothedDelta = Mathf.Lerp(_smoothedDelta, delta, SmoothFactor);
		XjRuntimeStressTier tier = XjRuntimeStressTier.Normal;
		if (_smoothedDelta >= CriticalFrameSeconds)
		{
			tier = XjRuntimeStressTier.Critical;
		}
		else if (_smoothedDelta >= SevereFrameSeconds)
		{
			tier = XjRuntimeStressTier.Severe;
		}
		else if (_smoothedDelta >= MildFrameSeconds)
		{
			tier = XjRuntimeStressTier.Mild;
		}

		if (tier >= XjRuntimeStressTier.Severe)
		{
			_stressHoldUntil = Time.unscaledTime + StressHoldSeconds;
		}
		else if (Time.unscaledTime < _stressHoldUntil)
		{
			tier = XjRuntimeStressTier.Severe;
		}

		_stressTier = tier;
	}

	internal static int ScaleCount(int baseBudget, int minimum)
	{
		if (baseBudget <= 0)
		{
			return 0;
		}

		float multiplier = _stressTier switch
		{
			XjRuntimeStressTier.Mild => 0.50f,
			XjRuntimeStressTier.Severe => 0.28f,
			XjRuntimeStressTier.Critical => 0.12f,
			_ => 1f
		};
		multiplier *= _populationPressureMultiplier;
		return Mathf.Max(Mathf.Max(1, minimum), Mathf.RoundToInt(baseBudget * multiplier));
	}

	internal static double ScaleMilliseconds(double baseBudget, double minimum)
	{
		if (baseBudget <= 0d)
		{
			return 0d;
		}

		double multiplier = _stressTier switch
		{
			XjRuntimeStressTier.Mild => 0.55d,
			XjRuntimeStressTier.Severe => 0.32d,
			XjRuntimeStressTier.Critical => 0.16d,
			_ => 1d
		};
		return System.Math.Max(minimum, baseBudget * multiplier);
	}

	internal static int TerrainCadenceFrames => _stressTier switch
	{
		XjRuntimeStressTier.Mild => 5,
		XjRuntimeStressTier.Severe => 8,
		XjRuntimeStressTier.Critical => 14,
		_ => 2
	};

	internal static int TerrainTileBudget => _stressTier switch
	{
		XjRuntimeStressTier.Mild => 10,
		XjRuntimeStressTier.Severe => 5,
		XjRuntimeStressTier.Critical => 2,
		_ => 24
	};

	/// <summary>
	/// WorldBox may execute more than one simulation step inside one rendered
	/// frame at high speed. Fast/background queues are allowed one scheduler pass
	/// per rendered frame; queued work remains pending for the next frame. Year
	/// edges are not suppressed; current-state growth maintenance has its own
	/// rendered-frame gate and coalesces accumulated cadence pulses.
	/// </summary>
	internal static bool TryBeginFastSchedulerPass()
	{
		int unityFrame = Time.frameCount;
		if (_lastFastSchedulerUnityFrame == unityFrame)
		{
			return false;
		}

		_lastFastSchedulerUnityFrame = unityFrame;
		return true;
	}

	internal static bool TryBeginGrowthSchedulerPass()
	{
		return TryBeginRenderedFramePass(ref _lastGrowthSchedulerUnityFrame);
	}

	internal static bool TryBeginArchivePass()
	{
		return TryBeginRenderedFramePass(ref _lastArchiveUnityFrame);
	}

	internal static bool TryBeginTerrainPass()
	{
		return TryBeginRenderedFramePass(ref _lastTerrainUnityFrame);
	}

	internal static bool TryBeginMemoryMaintenancePass()
	{
		return TryBeginRenderedFramePass(ref _lastMemoryMaintenanceUnityFrame);
	}

	private static bool TryBeginRenderedFramePass(ref int lastUnityFrame)
	{
		int unityFrame = Time.frameCount;
		if (lastUnityFrame == unityFrame)
		{
			return false;
		}

		lastUnityFrame = unityFrame;
		return true;
	}

	/// <summary>
	/// Returns true when one background phase may run. Under load, phases are
	/// deferred rather than discarded; the scheduler advances the phase only when
	/// this method returns true.
	/// </summary>
	internal static bool ShouldRunBackgroundPhase()
	{
		int stride = _stressTier switch
		{
			XjRuntimeStressTier.Mild => 3,
			XjRuntimeStressTier.Severe => 5,
			XjRuntimeStressTier.Critical => 8,
			_ => _worldUnitCount >= 3000 ? 2 : 1
		};
		_backgroundStrideCounter++;
		if (_backgroundStrideCounter < stride)
		{
			return false;
		}

		_backgroundStrideCounter = 0;
		return true;
	}

	/// <summary>
	/// Soft memory maintenance receives a tiny reserved cadence even while annual
	/// queues remain busy. Without this reserve, the worlds that most need pruning
	/// can starve the pruner indefinitely and amplify long-run allocation pressure.
	/// </summary>
	internal static bool ShouldRunMemoryMaintenanceReserve()
	{
		int stride = _stressTier switch
		{
			XjRuntimeStressTier.Critical => 12,
			XjRuntimeStressTier.Severe => 10,
			XjRuntimeStressTier.Mild => 8,
			_ => _worldUnitCount >= 3000 ? 8 : 6
		};
		_memoryMaintenanceStrideCounter++;
		if (_memoryMaintenanceStrideCounter < stride) return false;

		_memoryMaintenanceStrideCounter = 0;
		return true;
	}

	/// <summary>
	/// Annual actor work is gameplay-important, but it is also the largest
	/// high-speed backlog. Keep it responsive at normal FPS, then thin its
	/// priority passes under sustained frame pressure so it cannot crowd out the
	/// native simulation every rendered frame.
	/// </summary>
	internal static bool ShouldRunAnnualActorPriorityPass()
	{
		int stride = _stressTier switch
		{
			XjRuntimeStressTier.Mild => _worldUnitCount >= 3000 ? 3 : 2,
			XjRuntimeStressTier.Severe => 3,
			XjRuntimeStressTier.Critical => 4,
			_ => _worldUnitCount >= 3000 ? 2 : 1
		};
		_annualPriorityStrideCounter++;
		if (_annualPriorityStrideCounter < stride)
		{
			return false;
		}

		_annualPriorityStrideCounter = 0;
		return true;
	}

	private static float ResolvePopulationPressureMultiplier(int units)
	{
		if (units >= 4000) return 0.62f;
		if (units >= 3000) return 0.70f;
		if (units >= 2000) return 0.78f;
		if (units >= 1000) return 0.88f;
		return 1f;
	}

	internal static void Clear()
	{
		_smoothedDelta = 1f / 30f;
		_stressHoldUntil = -1f;
		_stressTier = XjRuntimeStressTier.Normal;
		_backgroundStrideCounter = 0;
		_annualPriorityStrideCounter = 0;
		_memoryMaintenanceStrideCounter = 0;
		_lastSampledUnityFrame = -1;
		_lastFastSchedulerUnityFrame = -1;
		_lastGrowthSchedulerUnityFrame = -1;
		_lastArchiveUnityFrame = -1;
		_lastTerrainUnityFrame = -1;
		_lastMemoryMaintenanceUnityFrame = -1;
		_worldUnitCount = 0;
		_populationPressureMultiplier = 1f;
	}
}
