using System;
using System.Diagnostics;
using XuanJianVNext.Architecture.Presentation;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Rank;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.DongTian;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// Long-run memory maintenance is scheduled annually but consumed as bounded
/// background stages. Soft cycles only release snapshots and prune bounded logs.
/// Allocation-heavy retained-container rebuilds are remembered as a hard request
/// and executed only at <=2x speed with normal frame pressure. No forced GC is used.
/// </summary>
internal static class XjRuntimeMemoryPruner
{
	private enum CleanupStage : byte
	{
		Idle = 0,
		ReleaseSnapshots = 1,
		PruneWorldHistory = 2,
		PersonalBook = 3,
		FamilyBook = 4,
		SectBook = 5,
		DeferredFacts = 6,
		ReleaseHardCaches = 7,
		Complete = 8
	}

	private const int SoftCleanupIntervalYears = 5;
	private const int HardCompactIntervalYears = 25;
	private const int SuppressedHistoryInspectBudget = 512;
	private const int HardSuppressedHistoryInspectBudget = 2048;

	private static int _pendingYear;
	private static bool _hardCleanupRequested;
	private static bool _hardCleanupActive;
	private static CleanupStage _stage;

	internal static bool HasPending => _stage != CleanupStage.Idle || _hardCleanupRequested;
	internal static bool HasSoftRunnableWork => _stage != CleanupStage.Idle && !_hardCleanupActive;
	internal static bool HasStaleIdSweepWork => XjRuntimeSettings.LongRunMemoryMaintenanceEnabled
		&& XjStaleActorIdEviction.HasTrackedIds;
	internal static bool HasRunnableWork => _stage != CleanupStage.Idle
		|| (_hardCleanupRequested && XjRuntimeWorkBudget.CanRunExpensiveSynchronousMaintenance);

	internal static void RunAnnual(int currentYear)
	{
		if (!XjRuntimeSettings.LongRunMemoryMaintenanceEnabled
			|| currentYear <= 0
			|| currentYear % SoftCleanupIntervalYears != 0)
		{
			return;
		}

		_pendingYear = Math.Max(_pendingYear, currentYear);
		_hardCleanupRequested |= currentYear % HardCompactIntervalYears == 0;
		XjPerformanceTelemetry.ObserveQueue("relationRevisionCache", XjRelationEntityRevisionStore.RuntimeEntryCount);
		XjPerformanceTelemetry.ObserveQueue("heritageControlCache", XjDaoTuHeritageService.RuntimeControlCacheCount);
		XjPerformanceTelemetry.ObserveQueue("annualStatePools", XjScheduler.RetainedAnnualStatePoolCount);
		XjPerformanceTelemetry.ObserveQueue("staleActorIdsTracked", XjStaleActorIdEviction.TrackedCount);
		XjPerformanceTelemetry.ObserveQueue("rankMetricCache", XjRankSortSystem.RuntimeMetricCacheCount);
		if (_stage == CleanupStage.Idle)
		{
			StartCycle(useHardCleanup: _hardCleanupRequested
				&& XjRuntimeWorkBudget.CanRunExpensiveSynchronousMaintenance);
		}
	}

	internal static int Tick(int maxStages, double timeBudgetMs)
	{
		if (!XjRuntimeSettings.LongRunMemoryMaintenanceEnabled)
		{
			Clear();
			return 0;
		}
		if (maxStages <= 0 || timeBudgetMs <= 0d)
		{
			return 0;
		}

		if (_stage == CleanupStage.Idle)
		{
			if (!_hardCleanupRequested || !XjRuntimeWorkBudget.CanRunExpensiveSynchronousMaintenance)
			{
				return 0;
			}
			StartCycle(useHardCleanup: true);
		}

		int processed = 0;
		long started = Stopwatch.GetTimestamp();
		while (_stage != CleanupStage.Idle && processed < maxStages)
		{
			CleanupStage current = _stage;
			AdvanceOneStage(current);
			processed++;
			if ((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency >= timeBudgetMs)
			{
				break;
			}
		}
		return processed;
	}


	/// <summary>
	/// H3 fast-speed lane: remove confirmed dead/missing stable actor ids without
	/// rebuilding any Dictionary/Queue backing storage. This is deliberately
	/// independent from the 5/25-year soft/hard compact cycle so x20/x40 worlds
	/// continue draining stale runtime state even while annual work is backlogged.
	/// </summary>
	internal static int TickStaleIdEviction(int maxChecks, double timeBudgetMs)
	{
		if (!XjRuntimeSettings.LongRunMemoryMaintenanceEnabled
			|| maxChecks <= 0
			|| timeBudgetMs <= 0d
			|| !XjStaleActorIdEviction.HasTrackedIds)
		{
			return 0;
		}

		return XjStaleActorIdEviction.Tick(
			maxChecks,
			timeBudgetMs,
			XjScheduler.ForgetUnavailableActor);
	}

	internal static void Clear()
	{
		_pendingYear = 0;
		_hardCleanupRequested = false;
		_hardCleanupActive = false;
		_stage = CleanupStage.Idle;
	}

	private static void StartCycle(bool useHardCleanup)
	{
		_hardCleanupActive = useHardCleanup;
		_stage = CleanupStage.ReleaseSnapshots;
	}

	private static void AdvanceOneStage(CleanupStage current)
	{
		switch (current)
		{
			case CleanupStage.ReleaseSnapshots:
				XjActorRegistry.ReleaseSnapshotCache();
				XjCodexSnapshotPublisher.ReleaseSnapshotIfClosed();
				XjCultivatorCandidateIndex.ReleaseSnapshotCaches();
				XjPersonalBiographyStore.ReleaseSnapshotCache();
				XjFamilyChronicleBookStore.ReleaseSnapshotCache();
				XjSectChronicleStore.ReleaseSnapshotCache();
				// Combat attribution is useful for roughly one rendered frame and weapon-art
				// participation only for the current annual settlement. Bound their runtime
				// dictionaries even when the actors themselves live for millennia.
				XjCombatTracker.PruneExpiredRecords(UnityEngine.Time.frameCount, 1024);
				XjWeaponArtCombatTracker.PruneBeforeYear(Math.Max(1, _pendingYear - 2), 512);
				XjXianGuoSystem.PruneHistoricalState(_pendingYear, _hardCleanupActive);
				_stage = CleanupStage.PruneWorldHistory;
				break;
			case CleanupStage.PruneWorldHistory:
				XjWorldHistoryStore.PruneSuppressedRecords(
					_hardCleanupActive ? HardSuppressedHistoryInspectBudget : SuppressedHistoryInspectBudget);
				_stage = CleanupStage.PersonalBook;
				break;
			case CleanupStage.PersonalBook:
				if (_hardCleanupActive) XjPersonalBiographyStore.CompactMemory(rebuildStorage: true);
				_stage = CleanupStage.FamilyBook;
				break;
			case CleanupStage.FamilyBook:
				if (_hardCleanupActive) XjFamilyChronicleBookStore.CompactMemory(rebuildStorage: true);
				_stage = CleanupStage.SectBook;
				break;
			case CleanupStage.SectBook:
				if (_hardCleanupActive) XjSectChronicleStore.CompactMemory(rebuildStorage: true);
				_stage = CleanupStage.DeferredFacts;
				break;
			case CleanupStage.DeferredFacts:
				if (_hardCleanupActive) XjThreeBookDeferredFamilyFacts.CompactMemory();
				_stage = CleanupStage.ReleaseHardCaches;
				break;
			case CleanupStage.ReleaseHardCaches:
				if (_hardCleanupActive)
				{
					XjWorldLookupIndex.ReleaseSnapshotCaches();
					XjSwordIntentRegistry.ReleaseSnapshotCache();
					XjReusableLongBufferPool.Clear();
					ReleaseRebuildableRuntimeCaches();
				}
				_stage = CleanupStage.Complete;
				break;
			case CleanupStage.Complete:
				SampleManagedMemory();
				if (_hardCleanupActive) _hardCleanupRequested = false;
				_pendingYear = 0;
				_hardCleanupActive = false;
				_stage = CleanupStage.Idle;
				break;
			default:
				_stage = CleanupStage.Idle;
				break;
		}
	}

	private static void ReleaseRebuildableRuntimeCaches()
	{
		int relationRevisionEntries = XjRelationEntityRevisionStore.RuntimeEntryCount;
		int heritageCacheEntries = XjDaoTuHeritageService.RuntimeControlCacheCount;
		if (relationRevisionEntries > 0) XjPerformanceTelemetry.ObserveQueue("relationRevisionCache", relationRevisionEntries);
		if (heritageCacheEntries > 0) XjPerformanceTelemetry.ObserveQueue("heritageControlCache", heritageCacheEntries);

		// These caches form one revision-keyed presentation/read-model cluster. Clear
		// dependents first, then replace the revision dictionaries so extinct entity IDs
		// do not pin historic Dictionary bucket arrays for the entire world session.
		XjDaoTuHeritageService.ReleaseRuntimeCacheStorage();
		XjStaleActorIdEviction.RebuildStorage();
		XjFamilyReadModel.Shared.ClearCache();
		XjSectReadModel.Shared.Clear();
		XjPresentationHooks.ClearActorInfoProjectionCache();
		XjRelationEntityRevisionStore.ReleaseRetainedStorage();

		XjScheduler.ReleaseLongRunRetainedContainers();
		XjRankSortSystem.ClearCache();
		XjPresentationHooks.InvalidateActorInfo();
		XjFamilyBloodlineAggregateCache.Clear();
		XjFaBaoBonusService.Clear();
		XjFaBaoAcquisition.ClearRuntimeCache();
		XjCultivationEligibility.ClearRuntimeCache();
		XjFamilySurnamePolicy.ClearRuntimeCache();
		XjXianJiAccessor.ClearRuntimeCache();
		XjSectCityData.ClearRuntimeCache();
		XjSectDongTianLifecycle.ClearRuntimeCache();
	}

	private static void SampleManagedMemory()
	{
		try
		{
			long bytes = GC.GetTotalMemory(false);
			long megabytes = bytes > 0L ? bytes / (1024L * 1024L) : 0L;
			if (megabytes > 0L)
			{
				XjPerformanceTelemetry.ObserveQueue(
					"managedMB",
					megabytes > int.MaxValue ? int.MaxValue : (int)megabytes);
			}

			// 玩家反馈出现“32GB 内存跑满”时，只看 GC.GetTotalMemory 会遗漏 Unity
			// 原生纹理、对象池与进程工作集。这里仍沿用五年后台整理的低频采样，
			// 只把总进程工作集写入诊断缓存，不额外打日志，也不逐帧调用 Process。
			using Process process = Process.GetCurrentProcess();
			long workingSetBytes = process.WorkingSet64;
			long workingSetMb = workingSetBytes > 0L ? workingSetBytes / (1024L * 1024L) : 0L;
			if (workingSetMb > 0L)
			{
				XjPerformanceTelemetry.ObserveQueue(
					"processMB",
					workingSetMb > int.MaxValue ? int.MaxValue : (int)workingSetMb);
			}
		}
		catch (System.Exception xjCaught184)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report(
				"code/XuanJianVNext/Systems/Runtime/XjRuntimeMemoryPruner.cs:SampleManagedMemory",
				xjCaught184);
		}
	}
}
