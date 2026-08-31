using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Performance;

/// <summary>
/// Optional deterministic benchmark suite. It is installed only when the
/// XUANJIAN_PERF_AUTO environment variable is present, so release gameplay pays
/// no MonoBehaviour/update cost.
/// </summary>
internal static class XjPerformanceBenchmarkInstaller
{
	private static GameObject _host;

	internal static void Initialize()
	{
		string mode = Environment.GetEnvironmentVariable("XUANJIAN_PERF_AUTO");
		if (string.IsNullOrWhiteSpace(mode) || _host != null) return;

		_host = new GameObject("[XuanJian] Performance Benchmark");
		UnityEngine.Object.DontDestroyOnLoad(_host);
		XjPerformanceBenchmarkRunner runner = _host.AddComponent<XjPerformanceBenchmarkRunner>();
		runner.Configure(mode.Trim());
	}

	internal static void Clear()
	{
		// World generation calls the mod's runtime clear path. The benchmark host
		// is DontDestroyOnLoad and must survive that clear so it can continue with
		// the newly generated map; only the current measurement window is reset.
		XjPerformanceTelemetry.CancelBenchmark();
	}
}

internal sealed class XjPerformanceBenchmarkRunner : MonoBehaviour
{
	private const string Prefix = "[玄鉴][自动压测]";
	private const float SetupDelaySeconds = 2f;

	private enum RunnerState : byte
	{
		WaitingForGame = 0,
		PreparingWorld = 1,
		WaitingForWorldLoaded = 2,
		SpawningPopulation = 3,
		WarmingUp = 4,
		Measuring = 5,
		Complete = 6
	}

	private readonly List<XjPerformanceReport> _reports = new List<XjPerformanceReport>();
	private readonly List<WorldTile> _spawnTiles = new List<WorldTile>();
	private RunnerState _state = RunnerState.WaitingForGame;
	private string _mode = "suite";
	private string _mapSize = MapSizeLibrary.iceberg;
	private string _mapTemplate = string.Empty;
	private string _outputDirectory = string.Empty;
	private string[] _speedIds = Array.Empty<string>();
	private int[] _populationTargets = Array.Empty<int>();
	private int _populationIndex;
	private int _speedIndex;
	private int _spawnCursor;
	private int _spawnBatch;
	private float _warmupSeconds;
	private float _measureSeconds;
	private float _stateElapsed;
	private bool _createWorld;
	private bool _quitOnComplete;
	private bool _configured;
	private string _runId = string.Empty;

	internal void Configure(string mode)
	{
		_mode = string.IsNullOrWhiteSpace(mode) ? "suite" : mode.Trim();
		_mapSize = GetEnvString("XUANJIAN_PERF_MAP_SIZE", MapSizeLibrary.iceberg);
		_mapTemplate = GetEnvString("XUANJIAN_PERF_MAP_TEMPLATE", Config.current_map_template);
		_speedIds = ParseStrings(GetEnvString("XUANJIAN_PERF_SPEEDS", "x1,x5,x10,x20,x40"));
		_populationTargets = ParsePositiveInts(GetEnvString("XUANJIAN_PERF_POPULATIONS", "1000,3000,4000,5000"));
		_spawnBatch = Math.Clamp(GetEnvInt("XUANJIAN_PERF_SPAWN_BATCH", 250), 10, 2000);
		_warmupSeconds = Math.Max(1f, GetEnvFloat("XUANJIAN_PERF_WARMUP", 15f));
		_measureSeconds = Math.Max(5f, GetEnvFloat("XUANJIAN_PERF_DURATION", 60f));
		_createWorld = GetEnvBool("XUANJIAN_PERF_CREATE_WORLD", true);
		_quitOnComplete = GetEnvBool("XUANJIAN_PERF_QUIT_ON_DONE", false);
		_outputDirectory = GetEnvString(
			"XUANJIAN_PERF_OUTPUT",
			Path.Combine(Application.persistentDataPath, "XuanJianPerformance"));
		_runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		_configured = _speedIds.Length > 0 && _populationTargets.Length > 0;

		if (!_configured)
		{
			Debug.LogError(Prefix + " 配置无有效速度或人口目标，停止运行。");
			enabled = false;
			return;
		}

		Debug.Log(Prefix
			+ " 模式=" + _mode
			+ " 速度=" + string.Join(",", _speedIds)
			+ " 人口序列=" + string.Join(",", _populationTargets)
			+ " 预热=" + _warmupSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s"
			+ " 测量=" + _measureSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s"
			+ " 输出=" + _outputDirectory);
	}

	private void Update()
	{
		if (!_configured) return;
		_stateElapsed += Math.Max(0f, Time.unscaledDeltaTime);
		try
		{
			switch (_state)
			{
				case RunnerState.WaitingForGame:
					TickWaitingForGame();
					break;
				case RunnerState.PreparingWorld:
					TickPreparingWorld();
					break;
				case RunnerState.WaitingForWorldLoaded:
					TickWaitingForWorldLoaded();
					break;
				case RunnerState.SpawningPopulation:
					TickSpawningPopulation();
					break;
				case RunnerState.WarmingUp:
					TickWarmingUp();
					break;
				case RunnerState.Measuring:
					TickMeasuring();
					break;
			}
		}
		catch (Exception ex)
		{
			XjPerformanceTelemetry.CancelBenchmark();
			Debug.LogError(Prefix + " 运行失败: " + ex);
			enabled = false;
		}
	}

	private void TickWaitingForGame()
	{
		if (!Config.game_loaded || World.world == null || AssetManager.actor_library == null) return;
		SetState(RunnerState.PreparingWorld);
	}

	private void TickPreparingWorld()
	{
		Config.paused = false;
		if (_createWorld)
		{
			Config.customMapSize = _mapSize;
			Config.current_map_template = _mapTemplate;
			Debug.Log(Prefix + " 生成测试世界 地图尺寸=" + _mapSize + " 模板=" + _mapTemplate);
			World.world.generateNewMap();
			SetState(RunnerState.WaitingForWorldLoaded);
			return;
		}
		PrepareSpawnTiles();
		SetState(RunnerState.SpawningPopulation);
	}

	private void TickWaitingForWorldLoaded()
	{
		if (SmoothLoader.isLoading() || _stateElapsed < SetupDelaySeconds) return;
		PrepareSpawnTiles();
		SetState(RunnerState.SpawningPopulation);
	}

	private void TickSpawningPopulation()
	{
		if (SmoothLoader.isLoading() || _stateElapsed < 0.25f) return;
		Config.paused = false;
		Config.setWorldSpeed("x1");

		int target = _populationTargets[_populationIndex];
		int current = CountUnits();
		if (current < target)
		{
			int request = Math.Min(_spawnBatch, target - current);
			int spawned = SpawnHumans(request);
			if (spawned <= 0)
			{
				throw new InvalidOperationException("无法继续投放测试人口；请检查地图模板是否存在可用陆地。 当前=" + current + " 目标=" + target);
			}
			return;
		}

		_speedIndex = Math.Clamp(_speedIndex, 0, _speedIds.Length - 1);
		Config.setWorldSpeed(_speedIds[_speedIndex]);
		Debug.Log(Prefix + " 场景准备 目标人口=" + target + " 实际人口=" + current + " 速度=" + _speedIds[_speedIndex]);
		SetState(RunnerState.WarmingUp);
	}

	private void TickWarmingUp()
	{
		Config.paused = false;
		Config.setWorldSpeed(_speedIds[_speedIndex]);
		if (_stateElapsed < _warmupSeconds) return;

		string scenario = BuildScenarioId();
		XjPerformanceTelemetry.BeginBenchmarkScenario(scenario);
		Debug.Log(Prefix + " 开始测量 " + scenario + " 实际单位数=" + CountUnits());
		SetState(RunnerState.Measuring);
	}

	private void TickMeasuring()
	{
		Config.paused = false;
		Config.setWorldSpeed(_speedIds[_speedIndex]);
		if (_stateElapsed < _measureSeconds) return;

		XjPerformanceReport report = XjPerformanceTelemetry.EndBenchmarkScenario();
		_reports.Add(report);
		WriteScenarioReport(report);
		Debug.Log(Prefix + " 完成 " + report.BuildCompactSummary());
		AdvanceScenario();
	}

	private void AdvanceScenario()
	{
		_speedIndex++;
		if (_speedIndex < _speedIds.Length)
		{
			SetState(RunnerState.WarmingUp);
			return;
		}

		_speedIndex = 0;
		_populationIndex++;
		if (_populationIndex < _populationTargets.Length)
		{
			SetState(RunnerState.SpawningPopulation);
			return;
		}

		WriteSuiteReports();
		SetState(RunnerState.Complete);
		Debug.Log(Prefix + " 全部场景完成 reports=" + _reports.Count + " 输出=" + _outputDirectory);
		if (_quitOnComplete) Application.Quit();
	}

	private string BuildScenarioId()
	{
		return "population-" + _populationTargets[_populationIndex]
			+ "_speed-" + _speedIds[_speedIndex];
	}

	private void PrepareSpawnTiles()
	{
		_spawnTiles.Clear();
		_spawnCursor = 0;
		var zones = World.world?.zone_calculator?.zones;
		if (zones == null) return;
		for (int i = 0; i < zones.Count; i++)
		{
			TileZone zone = zones[i];
			WorldTile tile = FindSpawnTile(zone);
			if (tile != null) _spawnTiles.Add(tile);
		}
		if (_spawnTiles.Count == 0)
		{
			throw new InvalidOperationException("测试世界没有可用的人类出生地块。");
		}
	}

	private int SpawnHumans(int amount)
	{
		if (amount <= 0) return 0;
		if (_spawnTiles.Count == 0) PrepareSpawnTiles();
		int spawned = 0;
		for (int i = 0; i < amount; i++)
		{
			WorldTile tile = _spawnTiles[_spawnCursor++ % _spawnTiles.Count];
			Actor actor = World.world.units.spawnNewUnit(
				"human",
				tile,
				pSpawnSound: false,
				pMiracleSpawn: true,
				pSpawnHeight: 0f,
				pSubspecies: null,
				pGiveOwnerlessItems: false,
				pAdultAge: true);
			if (actor != null) spawned++;
		}
		return spawned;
	}

	private static WorldTile FindSpawnTile(TileZone zone)
	{
		if (zone == null) return null;
		if (IsHumanSpawnTile(zone.centerTile)) return zone.centerTile;
		if (zone.tiles == null) return null;
		for (int i = 0; i < zone.tiles.Length; i++)
		{
			if (IsHumanSpawnTile(zone.tiles[i])) return zone.tiles[i];
		}
		return null;
	}

	private static bool IsHumanSpawnTile(WorldTile tile)
	{
		if (tile?.Type == null) return false;
		return !tile.Type.liquid && !tile.Type.lava && !tile.Type.block;
	}

	private void WriteScenarioReport(XjPerformanceReport report)
	{
		Directory.CreateDirectory(_outputDirectory);
		string file = Path.Combine(_outputDirectory, "xuanjian_perf_" + _runId + "_" + Sanitize(report.ScenarioId) + ".json");
		File.WriteAllText(file, report.ToJson(), new UTF8Encoding(false));
	}

	private void WriteSuiteReports()
	{
		Directory.CreateDirectory(_outputDirectory);
		string jsonPath = Path.Combine(_outputDirectory, "xuanjian_perf_" + _runId + "_suite.json");
		File.WriteAllText(jsonPath, JsonConvert.SerializeObject(_reports, Formatting.Indented), new UTF8Encoding(false));

		string csvPath = Path.Combine(_outputDirectory, "xuanjian_perf_" + _runId + "_suite.csv");
		var builder = new StringBuilder(4096);
		builder.AppendLine("scenario,units,speed,duration_s,frame_avg_ms,frame_p95_ms,frame_p99_ms,frame_max_ms,over33,over50,over100,over200,gc0,gc1,gc2,managed_memory_delta_bytes,process_memory_delta_bytes,enemy_calls,enemy_empty,enemy_repeated,enemy_backoff,oldest_semantic_year,semantic_year_lag");
		for (int i = 0; i < _reports.Count; i++)
		{
			XjPerformanceReport report = _reports[i];
			builder.Append(Csv(report.ScenarioId)).Append(',')
				.Append(report.Units).Append(',')
				.Append(report.WorldSpeed.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
				.Append(report.DurationSeconds.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
				.Append(report.FrameAverageMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
				.Append(report.FrameP95Ms.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
				.Append(report.FrameP99Ms.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
				.Append(report.FrameMaximumMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
				.Append(report.FramesOver33Ms).Append(',')
				.Append(report.FramesOver50Ms).Append(',')
				.Append(report.FramesOver100Ms).Append(',')
				.Append(report.FramesOver200Ms).Append(',')
				.Append(report.Gc0Collections).Append(',')
				.Append(report.Gc1Collections).Append(',')
				.Append(report.Gc2Collections).Append(',')
				.Append(report.ManagedMemoryDeltaBytes).Append(',')
				.Append(report.ProcessMemoryDeltaBytes).Append(',')
				.Append(report.EnemySearchCalls).Append(',')
				.Append(report.EnemySearchEmpty).Append(',')
				.Append(report.EnemySearchRepeatedEmpty).Append(',')
				.Append(report.EnemySearchBackoffApplied).Append(',')
				.Append(report.OldestPendingSemanticYear).Append(',')
				.Append(report.SemanticYearLag)
				.AppendLine();
		}
		File.WriteAllText(csvPath, builder.ToString(), new UTF8Encoding(false));
	}

	private static int CountUnits() => World.world?.units?.Count ?? 0;

	private void SetState(RunnerState state)
	{
		_state = state;
		_stateElapsed = 0f;
	}

	private static string[] ParseStrings(string raw)
	{
		return (raw ?? string.Empty)
			.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(value => value.Trim())
			.Where(value => value.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static int[] ParsePositiveInts(string raw)
	{
		return (raw ?? string.Empty)
			.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(value => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0)
			.Where(value => value > 0)
			.Distinct()
			.OrderBy(value => value)
			.ToArray();
	}

	private static string GetEnvString(string key, string fallback)
	{
		string value = Environment.GetEnvironmentVariable(key);
		return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
	}

	private static int GetEnvInt(string key, int fallback)
	{
		string value = Environment.GetEnvironmentVariable(key);
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
	}

	private static float GetEnvFloat(string key, float fallback)
	{
		string value = Environment.GetEnvironmentVariable(key);
		return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;
	}

	private static bool GetEnvBool(string key, bool fallback)
	{
		string value = Environment.GetEnvironmentVariable(key);
		if (string.IsNullOrWhiteSpace(value)) return fallback;
		value = value.Trim();
		return value == "1"
			|| value.Equals("true", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("yes", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("on", StringComparison.OrdinalIgnoreCase);
	}

	private static string Sanitize(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return "scenario";
		foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
		return value.Replace(' ', '_');
	}

	private static string Csv(string value)
	{
		value ??= string.Empty;
		return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
			? "\"" + value.Replace("\"", "\"\"") + "\""
			: value;
	}
}
