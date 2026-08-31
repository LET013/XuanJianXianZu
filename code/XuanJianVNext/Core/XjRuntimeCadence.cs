using XuanJianVNext.Architecture.Presentation;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Runtime;
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
		XjRuntimeWorkBudget.SampleFrame();
		XjRuntimeFrameGovernor.BeginFrame();
		XjSimulationHeartbeat.ObserveRenderedFrame();

		// 特质编辑器可能复用旧版语言缓存；首个有效渲染帧补齐模拟器特质汉化。
		XjVNextAssetRegistration.EnsureRuntimeLocalization();
		XjWorldArchiveSystem.LoadReadOnly();
		XjWorldArchiveSystem.TryResolveMissingContainerAfterLoadGracePeriod();
		// updateSimulation 在原生暂停分支之前仍会被调用。若不在这里截断，
		// 玄鉴的后台车道会在玩家暂停查看世界时继续消耗完整的一帧预算，
		// 表现为“暂停了仍只有十几帧”。暂停只保留上面的轻量帧观测、
		// 汉化补齐与读档容器解析；所有会推进或扫描世界状态的维护一律等待恢复。
		if (Config.paused || XjRuntimePauseGate.BlocksSimulation)
		{
			return;
		}

		// An unsupported/corrupt archive may have completed the native archive read
		// while deliberately disabling XuanJian gameplay. Resolve that load transaction
		// once instead of leaving bootstrap permanently pending behind GameplayEnabled.
		if (XjWorldBootstrapLane.HasPending
			&& XjWorldArchiveSystem.HasLoadedArchive
			&& !XjWorldSchemaGuard.GameplayEnabled)
		{
			XjWorldBootstrapLane.AbortAfterRejectedArchive();
			return;
		}
		if (!XjWorldSchemaGuard.GameplayEnabled)
		{
			return;
		}
		XjPresentationHooks.EnsureWorldPresentation();

		// Archive-ready -> bootstrap-complete is an exclusive XuanJian load phase.
		// Feature lifecycle callbacks may have scheduled maintenance after archive
		// import, but none of it may run against half-rehydrated actor/index state.
		// WorldBox's native simulation is not paused; only XuanJian runtime work is
		// serialized behind the bounded bootstrap lane.
		if (XjWorldBootstrapLane.HasPending)
		{
			XjScheduler.ProcessAll(new XjSchedulerContext(
				growthPasses: 0,
				processFast: true,
				isYearChange: false));
			return;
		}

		// Phase 9 keeps the independent high-realm bloodline convergence fix, but the
		// Phase 8 save/load projection barriers have been removed.
		if (XjBloodlineRefreshLane.HasPending
			&& XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance))
		{
			XjBloodlineRefreshLane.Tick(XjRuntimeWorkBudget.ScaleCount(6, 1));
			XjRuntimeFrameGovernor.MarkPotentialOverrun("bloodline-refresh");
		}


		if (XjNativeArmySaveGuard.HasPendingRuntimeInspection
			&& XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance))
		{
			// 加载体检同样纳入全局帧预算；它可以延后完成，但不能和年度清算叠峰。
			XjNativeArmySaveGuard.TickRuntimeInspection(
				XjRuntimeWorkBudget.ScaleCount(24, 4));
			XjRuntimeFrameGovernor.MarkPotentialOverrun("native-army-inspection");
		}

		_frameCounter++;
		XjPerformanceTelemetry.TickFrame(Time.unscaledDeltaTime);

		if (XjJinDanCombatApi.HasPendingTerrainEffects
			&& XjDetectionGate.ShouldProcessJinDanTerrainEffects(
				_frameCounter,
				XjRuntimeWorkBudget.TerrainCadenceFrames))
		{
			_terrainDue = true;
		}
		if (_terrainDue
			&& XjJinDanCombatApi.HasPendingTerrainEffects
			&& XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance)
			&& XjRuntimeWorkBudget.TryBeginTerrainPass())
		{
			XjJinDanCombatApi.TickTerrainEffects(XjRuntimeWorkBudget.TerrainTileBudget);
			_terrainDue = false;
			XjRuntimeFrameGovernor.MarkPotentialOverrun("terrain-effects");
		}

		// 长线世界的归档 DTO 投影和 JSON 预准备会产生明显分配。不要因每一处
		// dirty revision 都在 x20/x40 的常规帧提前执行；正式存档仍会在快照前
		// 强制提交，后台通道仅在低压安静帧处理。
		if (XjWorldArchiveSystem.IsBackgroundCommitDue
			&& XjDetectionGate.ShouldRunCadence(XjRuntimeDetectionJob.ArchiveCommit, _frameCounter))
		{
			_archiveDue = true;
		}
		bool archiveQuietFrame = !_terrainDue
			&& !XjScheduler.HasUrgentSimulationBacklog
			&& !XjScheduler.HasAnnualRecoveryBacklog
			&& !XjScheduler.HasAnnualActorBacklog
			&& !XjAnnualWorldRuntimeLane.HasPending
			&& XjRuntimeWorkBudget.CanRunExpensiveSynchronousMaintenance;
		if (_archiveDue
			&& XjWorldArchiveSystem.HasPendingWrite
			&& archiveQuietFrame
			&& XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Background)
			&& XjRuntimeWorkBudget.ShouldRunBackgroundPhase()
			&& XjRuntimeWorkBudget.TryBeginArchivePass())
		{
			// 完整归档导出与 JSON 编码只允许落在低倍速、正常压力且无年度/战斗积压
			// 的安静帧。SaveManager 的快照入口仍会强制提交，不牺牲存档正确性。
			XjWorldArchiveSystem.Tick(1);
			_archiveDue = false;
			XjRuntimeFrameGovernor.MarkPotentialOverrun("archive-commit");
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
			// 虹霞三百年、渊照五百年是固定世界里程碑。它们只做O(1)持久状态判断，
			// 直接挂真实跨年脉冲，不能再被年度逐年追赶车道的其它重任务堵在后面。
			// AnnualWorld 的同年调用仍保留为幂等兜底，目录自身按真实世界年去重。
			XjWorldEventCatalog.TickFixedMilestones(currentYear);
		}

		if (processGrowth || processFast || isYearChange)
		{
			XjScheduler.ProcessAll(new XjSchedulerContext(
				growthPasses: growthPasses,
				processFast: processFast,
				isYearChange: isYearChange));
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

	/// <summary>
	/// Native-load boundary only. Reset frame-local cadence immediately, but do not
	/// initialize persistent runtime baselines until the XuanJian archive has been
	/// imported. This prevents the transient pre-archive state from becoming a new
	/// persistence authority when WorldBox attaches custom_data after finishingUpLoading.
	/// </summary>
	internal static void BeginAfterNativeLoad(int currentYear)
	{
		_frameCounter = 0;
		_lastUnityFrame = -1;
		_lastProcessYear = System.Math.Max(0, currentYear);
		_loadBaselineYear = -1;
		_pendingGrowthPasses = 0;
		_archiveDue = false;
		_terrainDue = false;
	}

	/// <summary>
	/// Archive-ready load boundary. XjWorldArchiveMemory may already have restored
	/// the persisted baseline; only genuinely new/older worlds initialize it here.
	/// </summary>
	internal static void CompleteAfterArchiveLoad(int currentYear)
	{
		int normalizedYear = System.Math.Max(0, currentYear);
		if (_loadBaselineYear < 0)
		{
			_loadBaselineYear = normalizedYear;
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Runtime);
		}
		_lastProcessYear = normalizedYear;
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
		XjRuntimeFrameGovernor.Clear();
		XjPerformanceTelemetry.Clear();
		XjDetectionGate.ClearRuntimeState();
		XjWorldLookupIndex.Clear();
		XjSimulationHeartbeat.Clear();
		XjRuntimePauseGate.Clear();
		XjNativeArmySaveGuard.ClearRuntime();
		XjPresentationHooks.ResetWorldPresentation();
		XjVNextAssetRegistration.ResetRuntimeLocalization();
	}
}
