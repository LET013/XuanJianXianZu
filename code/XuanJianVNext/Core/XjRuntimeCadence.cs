using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Map;
using XuanJianVNext.Traits;
using UnityEngine;

namespace XuanJianVNext.Core;

/// <summary>
/// Multi-rate runtime cadence. Logical simulation ticks may accumulate at high
/// speed, but heavyweight lanes consume at most one pass per rendered frame.
/// Semantic queues (annual work, deaths, archives, terrain) remain pending;
/// idempotent current-state maintenance pulses are coalesced into one pass.
/// </summary>
internal static class XjRuntimeCadence
{
	private static int _frameCounter;
	private static int _lastUnityFrame = -1;
	private static int _lastProcessYear = -1;
	private static int _loadBaselineYear = -1;
	private static int _pendingGrowthPasses;
	private static bool _archiveDue;
	private static bool _terrainDue;

	internal static void Tick()
	{
		// WorldBox 在 20x/40x 下会在同一渲染帧内多次调用 updateSimulation。
		// 玄鉴的队列入口只允许每个 Unity 渲染帧进入一次；年度业务由
		// Actor.updateAge 直接入队，不会因合并逻辑步而丢失。
		int unityFrame = Time.frameCount;
		if (_lastUnityFrame == unityFrame)
		{
			return;
		}
		_lastUnityFrame = unityFrame;

		// 特质编辑器可能复用旧版语言缓存；首个有效渲染帧补齐模拟器特质汉化。
		XjVNextAssetRegistration.EnsureRuntimeLocalization();
		XjWorldArchiveSystem.LoadReadOnly();
		if (!XjWorldSchemaGuard.GameplayEnabled)
		{
			return;
		}
		XjSectMapLayerSystem.EnsureForCurrentWorld();
		if (XjNativeArmySaveGuard.HasPendingRuntimeInspection)
		{
			// 每帧只处理少量对象。体检可以延后完成，但不能阻塞地图加载。
			XjNativeArmySaveGuard.TickRuntimeInspection(24);
		}

		_frameCounter++;
		XjRuntimeWorkBudget.SampleFrame();

		if (XjJinDanCombatApi.HasPendingTerrainEffects
			&& XjDetectionGate.ShouldProcessJinDanTerrainEffects(
				_frameCounter,
				XjRuntimeWorkBudget.TerrainCadenceFrames))
		{
			_terrainDue = true;
		}
		if (_terrainDue
			&& XjJinDanCombatApi.HasPendingTerrainEffects
			&& XjRuntimeWorkBudget.TryBeginTerrainPass())
		{
			XjJinDanCombatApi.TickTerrainEffects(XjRuntimeWorkBudget.TerrainTileBudget);
			_terrainDue = false;
		}

		if (XjWorldArchiveSystem.IsBackgroundCommitDue
			&& XjDetectionGate.ShouldRunCadence(XjRuntimeDetectionJob.ArchiveCommit, _frameCounter))
		{
			_archiveDue = true;
		}
		if (_archiveDue
			&& XjWorldArchiveSystem.HasPendingWrite
			&& XjRuntimeWorkBudget.TryBeginArchivePass())
		{
			XjWorldArchiveSystem.Tick(1);
			_archiveDue = false;
		}
		else if (!XjWorldArchiveSystem.HasPendingWrite)
		{
			_archiveDue = false;
		}

		bool growthCadenceDue = XjDetectionGate.ShouldRunCadence(
			XjRuntimeDetectionJob.GrowthScheduler, _frameCounter);
		bool fastCadenceDue = XjDetectionGate.ShouldRunCadence(
			XjRuntimeDetectionJob.FastScheduler, _frameCounter);
		if (growthCadenceDue && _pendingGrowthPasses < int.MaxValue)
		{
			_pendingGrowthPasses++;
		}

		bool isYearChange = false;
		int currentYear = _lastProcessYear;
		if (fastCadenceDue)
		{
			XjYearTracker.Refresh();
			currentYear = XjYearTracker.CurrentYear;
			isYearChange = currentYear > _lastProcessYear && currentYear > 0;
		}

		int growthPasses = 0;
		if (_pendingGrowthPasses > 0
			&& XjRuntimeWorkBudget.TryBeginGrowthSchedulerPass())
		{
			// Growth lanes are current-state maintenance: regeneration already uses
			// elapsed real time, while closed-cultivation/combat queues are bounded
			// and idempotent. Coalesce logical cadence debt into one scheduler pass
			// instead of creating a permanent post-speed-up backlog. The pass count
			// is preserved for clocks such as LongShu hostility.
			growthPasses = _pendingGrowthPasses;
			_pendingGrowthPasses = 0;
		}
		bool processGrowth = growthPasses > 0;

		bool priorityFastDue = XjScheduler.HasUrgentSimulationBacklog
			|| XjScheduler.HasAnnualRecoveryBacklog
			|| (XjScheduler.HasAnnualActorBacklog
				&& XjRuntimeWorkBudget.ShouldRunAnnualActorPriorityPass());
		bool processFast = (fastCadenceDue || priorityFastDue)
			&& (priorityFastDue || XjScheduler.HasFastWork)
			&& XjRuntimeWorkBudget.TryBeginFastSchedulerPass();
		if (isYearChange)
		{
			_lastProcessYear = currentYear;
		}

		if (processGrowth || processFast || isYearChange)
		{
			XjScheduler.ProcessAll(new XjSchedulerContext(
				growthPasses: growthPasses,
				processFast: processFast,
				isYearChange: isYearChange));
		}

		if (processGrowth)
		{
			XjCultivatorCache.CleanupIfNeeded();
		}
	}

	internal static int LoadBaselineYear => _loadBaselineYear < 0 ? 0 : _loadBaselineYear;

	internal static int ExportPersistentBaselineYear()
	{
		return _loadBaselineYear;
	}

	internal static void ImportPersistentBaselineYear(int baselineYear)
	{
		_loadBaselineYear = baselineYear >= 0 ? baselineYear : -1;
	}

	internal static bool HasElapsedSinceLoad(int currentYear, int years)
	{
		return years > 0
			&& _loadBaselineYear >= 0
			&& currentYear >= _loadBaselineYear + years;
	}

	internal static void InitializeAfterLoad(int currentYear)
	{
		_frameCounter = 0;
		_lastUnityFrame = -1;
		// 同一存档已持久化的基准年优先；只有新档或旧归档没有该字段时才以本次年份初始化。
		if (_loadBaselineYear < 0)
		{
			_loadBaselineYear = System.Math.Max(0, currentYear);
			XjWorldArchiveSystem.MarkChanged();
		}
		_lastProcessYear = System.Math.Max(0, currentYear);
		_pendingGrowthPasses = 0;
		_archiveDue = false;
		_terrainDue = false;
	}

	internal static void Clear()
	{
		_frameCounter = 0;
		_lastUnityFrame = -1;
		_lastProcessYear = -1;
		_loadBaselineYear = -1;
		_pendingGrowthPasses = 0;
		_archiveDue = false;
		_terrainDue = false;
		XjYearTracker.Clear();
		XjRuntimeWorkBudget.Clear();
		XjDetectionGate.ClearRuntimeState();
		XjWorldLookupIndex.Clear();
		XjNativeArmySaveGuard.ClearRuntime();
		XjSectMapLayerSystem.ResetForWorldUnload();
		XjVNextAssetRegistration.ResetRuntimeLocalization();
	}
}
