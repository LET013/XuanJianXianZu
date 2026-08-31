using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

namespace XuanJianVNext.Core;

/// <summary>
/// Low-allocation performance telemetry shared by normal diagnostics and the
/// automated population/speed benchmark. Hot paths only increment counters or
/// write into a fixed ring buffer; sorting and JSON allocation happen only when
/// a report is explicitly captured.
/// </summary>
internal static class XjPerformanceTelemetry
{
	private const int FrameCapacity = 16384;
	private const int DetailedActorCapacity = 32768;
	private const int MaximumQueueKeys = 256;
	private const float PeriodicLogSeconds = 30f;

	private static readonly float[] FrameTimesMs = new float[FrameCapacity];
	private static readonly Dictionary<string, int> QueueHighWater = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly Dictionary<long, int> LastEmptySearchFrame = new Dictionary<long, int>();

	private static int _frameWriteIndex;
	private static int _frameCount;
	private static double _frameTimeSumMs;
	private static float _frameTimeMaxMs;
	private static long _over33;
	private static long _over50;
	private static long _over100;
	private static long _over200;
	private static long _enemySearchCalls;
	private static long _enemySearchEligible;
	private static long _enemySearchSuccess;
	private static long _enemySearchEmpty;
	private static long _enemySearchRepeatedEmpty;
	private static long _enemySearchBackoffApplied;
	private static double _enemySearchBackoffScaleSum;
	private static float _enemySearchBackoffScaleMax;
	private static int _gc0Baseline;
	private static int _gc1Baseline;
	private static int _gc2Baseline;
	private static long _managedMemoryBaseline;
	private static long _processMemoryBaseline;
	private static float _windowStartedAt;
	private static float _nextPeriodicLogAt = -1f;
	private static bool _benchmarkActive;
	private static string _scenarioId = string.Empty;

	internal static bool BenchmarkActive => _benchmarkActive;
	internal static bool ShouldCollect => _benchmarkActive
		|| XjRuntimeSettings.PerformanceObservationEnabled;
	internal static bool DetailedAiObservationEnabled => _benchmarkActive
		|| XjRuntimeSettings.PerformanceObservationEnabled;

	internal static void TickFrame(float deltaSeconds)
	{
		if (!ShouldCollect || deltaSeconds <= 0f || deltaSeconds > 10f)
		{
			return;
		}

		float milliseconds = deltaSeconds * 1000f;
		if (_frameCount < FrameCapacity)
		{
			FrameTimesMs[_frameWriteIndex] = milliseconds;
			_frameCount++;
			_frameTimeSumMs += milliseconds;
		}
		else
		{
			float replaced = FrameTimesMs[_frameWriteIndex];
			FrameTimesMs[_frameWriteIndex] = milliseconds;
			_frameTimeSumMs += milliseconds - replaced;
		}
		_frameWriteIndex = (_frameWriteIndex + 1) % FrameCapacity;
		if (milliseconds > _frameTimeMaxMs) _frameTimeMaxMs = milliseconds;
		if (milliseconds > 33.333f) _over33++;
		if (milliseconds > 50f) _over50++;
		if (milliseconds > 100f) _over100++;
		if (milliseconds > 200f) _over200++;

		if (!_benchmarkActive && XjRuntimeSettings.PerformanceObservationEnabled)
		{
			float now = Time.unscaledTime;
			if (_nextPeriodicLogAt < 0f) _nextPeriodicLogAt = now + PeriodicLogSeconds;
			if (now >= _nextPeriodicLogAt)
			{
				_nextPeriodicLogAt = now + PeriodicLogSeconds;
				XjPerformanceReport report = CaptureReport("periodic", resetWindow: true);
				Debug.Log("[玄鉴][性能观测] " + report.BuildCompactSummary());
			}
		}
	}

	internal static void BeginBenchmarkScenario(string scenarioId)
	{
		_benchmarkActive = true;
		_scenarioId = scenarioId ?? string.Empty;
		ResetMeasurementWindow();
	}

	internal static XjPerformanceReport EndBenchmarkScenario()
	{
		XjPerformanceReport report = CaptureReport(_scenarioId, resetWindow: false);
		_benchmarkActive = false;
		_scenarioId = string.Empty;
		return report;
	}

	internal static void CancelBenchmark()
	{
		_benchmarkActive = false;
		_scenarioId = string.Empty;
		ResetMeasurementWindow();
	}

	internal static void ObserveEnemySearchStart(bool eligible)
	{
		if (!ShouldCollect) return;
		_enemySearchCalls++;
		if (eligible) _enemySearchEligible++;
	}

	internal static void ObserveEnemySearchResult(long actorId, bool foundTarget)
	{
		if (!ShouldCollect) return;
		if (foundTarget)
		{
			_enemySearchSuccess++;
			if (actorId > 0L && DetailedAiObservationEnabled) LastEmptySearchFrame.Remove(actorId);
			return;
		}

		_enemySearchEmpty++;
		if (!DetailedAiObservationEnabled || actorId <= 0L) return;
		int frame = Time.frameCount;
		if (LastEmptySearchFrame.TryGetValue(actorId, out int previousFrame)
			&& frame - previousFrame >= 0
			&& frame - previousFrame <= 30)
		{
			_enemySearchRepeatedEmpty++;
		}
		if (LastEmptySearchFrame.Count < DetailedActorCapacity || LastEmptySearchFrame.ContainsKey(actorId))
		{
			LastEmptySearchFrame[actorId] = frame;
		}
	}

	internal static void ForgetActor(long actorId)
	{
		if (actorId > 0L) LastEmptySearchFrame.Remove(actorId);
	}

	internal static void ObserveEnemySearchBackoff(float scale)
	{
		if (!ShouldCollect || scale <= 1f) return;
		_enemySearchBackoffApplied++;
		_enemySearchBackoffScaleSum += scale;
		if (scale > _enemySearchBackoffScaleMax) _enemySearchBackoffScaleMax = scale;
	}

	internal static void ObserveQueue(string name, int count)
	{
		if (!ShouldCollect || string.IsNullOrWhiteSpace(name) || count <= 0) return;
		if (!QueueHighWater.TryGetValue(name, out int previous))
		{
			if (QueueHighWater.Count >= MaximumQueueKeys) return;
			QueueHighWater[name] = count;
			return;
		}
		if (count > previous) QueueHighWater[name] = count;
	}

	internal static XjPerformanceReport CaptureReport(string scenarioId, bool resetWindow)
	{
		float[] sorted = new float[_frameCount];
		if (_frameCount > 0)
		{
			if (_frameCount < FrameCapacity)
			{
				Array.Copy(FrameTimesMs, 0, sorted, 0, _frameCount);
			}
			else
			{
				int tail = FrameCapacity - _frameWriteIndex;
				Array.Copy(FrameTimesMs, _frameWriteIndex, sorted, 0, tail);
				Array.Copy(FrameTimesMs, 0, sorted, tail, _frameWriteIndex);
			}
			Array.Sort(sorted);
		}

		int currentYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
		XjSchedulerPressureSnapshot pressure = XjScheduler.CapturePerformancePressureSnapshot();
		long managedNow = GC.GetTotalMemory(false);
		long processNow = TryGetProcessWorkingSetBytes();
		var report = new XjPerformanceReport
		{
			ScenarioId = scenarioId ?? string.Empty,
			CapturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
			DurationSeconds = Math.Max(0f, Time.unscaledTime - _windowStartedAt),
			WorldYear = currentYear,
			WorldSpeed = Config.time_scale_asset?.multiplier ?? Time.timeScale,
			Units = World.world?.units?.Count ?? 0,
			Cities = World.world?.cities?.Count ?? 0,
			Kingdoms = World.world?.kingdoms?.Count ?? 0,
			KnownActors = XjActorRegistry.Count,
			Cultivators = XjCultivatorCache.Count,
			FrameSamples = _frameCount,
			FrameAverageMs = _frameCount > 0 ? _frameTimeSumMs / _frameCount : 0d,
			FrameMaximumMs = _frameTimeMaxMs,
			FrameP95Ms = Percentile(sorted, 0.95d),
			FrameP99Ms = Percentile(sorted, 0.99d),
			FramesOver33Ms = _over33,
			FramesOver50Ms = _over50,
			FramesOver100Ms = _over100,
			FramesOver200Ms = _over200,
			Gc0Collections = Math.Max(0, GC.CollectionCount(0) - _gc0Baseline),
			Gc1Collections = Math.Max(0, GC.CollectionCount(1) - _gc1Baseline),
			Gc2Collections = Math.Max(0, GC.CollectionCount(2) - _gc2Baseline),
			ManagedMemoryStartBytes = _managedMemoryBaseline,
			ManagedMemoryEndBytes = managedNow,
			ManagedMemoryDeltaBytes = managedNow - _managedMemoryBaseline,
			ProcessMemoryStartBytes = _processMemoryBaseline,
			ProcessMemoryEndBytes = processNow,
			ProcessMemoryDeltaBytes = processNow > 0L && _processMemoryBaseline > 0L
				? processNow - _processMemoryBaseline
				: 0L,
			EnemySearchCalls = _enemySearchCalls,
			EnemySearchEligible = _enemySearchEligible,
			EnemySearchSuccess = _enemySearchSuccess,
			EnemySearchEmpty = _enemySearchEmpty,
			EnemySearchRepeatedEmpty = _enemySearchRepeatedEmpty,
			EnemySearchBackoffApplied = _enemySearchBackoffApplied,
			EnemySearchBackoffAverageScale = _enemySearchBackoffApplied > 0
				? _enemySearchBackoffScaleSum / _enemySearchBackoffApplied
				: 0d,
			EnemySearchBackoffMaximumScale = _enemySearchBackoffScaleMax,
			OldestPendingSemanticYear = pressure.OldestPendingSemanticYear,
			SemanticYearLag = pressure.OldestPendingSemanticYear > 0
				? Math.Max(0, currentYear - pressure.OldestPendingSemanticYear)
				: 0,
			AnnualIngressPending = pressure.AnnualIngress,
			AnnualCorePending = pressure.AnnualCore,
			AnnualSecondaryPending = pressure.AnnualSecondary,
			AnnualMaintenancePending = pressure.AnnualMaintenance,
			QueueHighWater = new Dictionary<string, int>(QueueHighWater, StringComparer.Ordinal)
		};

		if (resetWindow) ResetMeasurementWindow();
		return report;
	}

	internal static void ResetMeasurementWindow()
	{
		Array.Clear(FrameTimesMs, 0, FrameTimesMs.Length);
		QueueHighWater.Clear();
		LastEmptySearchFrame.Clear();
		_frameWriteIndex = 0;
		_frameCount = 0;
		_frameTimeSumMs = 0d;
		_frameTimeMaxMs = 0f;
		_over33 = 0;
		_over50 = 0;
		_over100 = 0;
		_over200 = 0;
		_enemySearchCalls = 0;
		_enemySearchEligible = 0;
		_enemySearchSuccess = 0;
		_enemySearchEmpty = 0;
		_enemySearchRepeatedEmpty = 0;
		_enemySearchBackoffApplied = 0;
		_enemySearchBackoffScaleSum = 0d;
		_enemySearchBackoffScaleMax = 0f;
		_gc0Baseline = GC.CollectionCount(0);
		_gc1Baseline = GC.CollectionCount(1);
		_gc2Baseline = GC.CollectionCount(2);
		_managedMemoryBaseline = GC.GetTotalMemory(false);
		_processMemoryBaseline = TryGetProcessWorkingSetBytes();
		_windowStartedAt = Time.unscaledTime;
	}

	private static long TryGetProcessWorkingSetBytes()
	{
		try
		{
			using System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
			return process.WorkingSet64;
		}
		catch
		{
			return 0L;
		}
	}

	internal static void Clear()
	{
		_benchmarkActive = false;
		_scenarioId = string.Empty;
		_nextPeriodicLogAt = -1f;
		ResetMeasurementWindow();
	}

	private static double Percentile(float[] sorted, double percentile)
	{
		if (sorted == null || sorted.Length == 0) return 0d;
		int index = Math.Min(sorted.Length - 1, Math.Max(0, (int)Math.Ceiling(sorted.Length * percentile) - 1));
		return sorted[index];
	}
}

internal sealed class XjPerformanceReport
{
	public string ScenarioId { get; set; } = string.Empty;
	public string CapturedAtUtc { get; set; } = string.Empty;
	public float DurationSeconds { get; set; }
	public int WorldYear { get; set; }
	public float WorldSpeed { get; set; }
	public int Units { get; set; }
	public int Cities { get; set; }
	public int Kingdoms { get; set; }
	public int KnownActors { get; set; }
	public int Cultivators { get; set; }
	public int FrameSamples { get; set; }
	public double FrameAverageMs { get; set; }
	public double FrameMaximumMs { get; set; }
	public double FrameP95Ms { get; set; }
	public double FrameP99Ms { get; set; }
	public long FramesOver33Ms { get; set; }
	public long FramesOver50Ms { get; set; }
	public long FramesOver100Ms { get; set; }
	public long FramesOver200Ms { get; set; }
	public int Gc0Collections { get; set; }
	public int Gc1Collections { get; set; }
	public int Gc2Collections { get; set; }
	public long ManagedMemoryStartBytes { get; set; }
	public long ManagedMemoryEndBytes { get; set; }
	public long ManagedMemoryDeltaBytes { get; set; }
	public long ProcessMemoryStartBytes { get; set; }
	public long ProcessMemoryEndBytes { get; set; }
	public long ProcessMemoryDeltaBytes { get; set; }
	public long EnemySearchCalls { get; set; }
	public long EnemySearchEligible { get; set; }
	public long EnemySearchSuccess { get; set; }
	public long EnemySearchEmpty { get; set; }
	public long EnemySearchRepeatedEmpty { get; set; }
	public long EnemySearchBackoffApplied { get; set; }
	public double EnemySearchBackoffAverageScale { get; set; }
	public double EnemySearchBackoffMaximumScale { get; set; }
	public int OldestPendingSemanticYear { get; set; }
	public int SemanticYearLag { get; set; }
	public int AnnualIngressPending { get; set; }
	public int AnnualCorePending { get; set; }
	public int AnnualSecondaryPending { get; set; }
	public int AnnualMaintenancePending { get; set; }
	public Dictionary<string, int> QueueHighWater { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

	internal string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

	internal string BuildCompactSummary()
	{
		return "scenario=" + ScenarioId
			+ " units=" + Units
			+ " speed=" + WorldSpeed.ToString("F1", CultureInfo.InvariantCulture)
			+ " avg=" + FrameAverageMs.ToString("F2", CultureInfo.InvariantCulture) + "ms"
			+ " p95=" + FrameP95Ms.ToString("F2", CultureInfo.InvariantCulture) + "ms"
			+ " p99=" + FrameP99Ms.ToString("F2", CultureInfo.InvariantCulture) + "ms"
			+ " max=" + FrameMaximumMs.ToString("F2", CultureInfo.InvariantCulture) + "ms"
			+ " >100=" + FramesOver100Ms
			+ " >200=" + FramesOver200Ms
			+ " gc=" + Gc0Collections + "/" + Gc1Collections + "/" + Gc2Collections
			+ " managedΔ=" + (ManagedMemoryDeltaBytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) + "MB"
			+ " processΔ=" + (ProcessMemoryDeltaBytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) + "MB"
			+ " enemy=" + EnemySearchCalls + " empty=" + EnemySearchEmpty
			+ " repeated=" + EnemySearchRepeatedEmpty
			+ " backoff=" + EnemySearchBackoffApplied
			+ " yearLag=" + SemanticYearLag;
	}
}
