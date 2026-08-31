using System;
using System.Collections.Generic;
using System.Diagnostics;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Architecture.Bootstrap;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Visual;

using XuanJianVNext.Systems.Sect;
namespace XuanJianVNext.Core;

internal readonly struct XjSchedulerContext
{
	internal readonly int GrowthPasses;
	internal readonly bool ProcessFast;
	internal readonly bool IsYearChange;

	internal bool ProcessGrowth => GrowthPasses > 0;

	internal XjSchedulerContext(int growthPasses, bool processFast, bool isYearChange)
	{
		GrowthPasses = Math.Max(0, growthPasses);
		ProcessFast = processFast;
		IsYearChange = isYearChange;
	}
}

/// <summary>
/// Central runtime coordinator. It schedules bounded lanes and cultivator-only work.
/// </summary>
internal static partial class XjScheduler
{
	internal static void ProcessAll(in XjSchedulerContext context)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled) return;
		if (XjRuntimePauseGate.BlocksSimulation)
		{
			ReportSchedulerPressure();
			return;
		}
		CaptureBackgroundEdges(context);
		if (context.IsYearChange)
		{
			int currentYear = World.world?.map_stats?.year ?? 0;
			XjAnnualWorldRuntimeLane.Schedule(currentYear);
			XjAnnualCultivatorSweepLane.Schedule(currentYear);
			XjStageZeroObservation.OnYearChanged(
				currentYear,
				AnnualIngressQueue.Count + AnnualActorQueue.Count,
				AnnualMaintenanceQueue.Count,
				XjAnnualCultivatorSweepLane.PendingCount,
				XjAptitudeSeedLane.PendingCount);
		}
		else if (!XjAnnualWorldRuntimeLane.HasPending)
		{
			int currentYear = World.world?.map_stats?.year ?? 0;
			if (XjCenturyAnnalsBuilder.HasPendingCompletedCentury(currentYear))
			{
				XjAnnualWorldRuntimeLane.ScheduleCenturyCatchUp(currentYear);
			}
		}

		// Bootstrap and death facts are correctness boundaries. Bootstrap may block
		// gameplay until migration is complete; death work must not sit behind a
		// large annual backlog. Everything else yields to the cumulative frame cap.
		if (XjWorldBootstrapLane.HasPending)
		{
			long bootstrapSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.Bootstrap, 0);
			TickBootstrapLane();
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.Bootstrap, bootstrapSample);
			XjRuntimeFrameGovernor.MarkPotentialOverrun("bootstrap");
			if (XjWorldBootstrapLane.HasPending)
			{
				ReportSchedulerPressure();
				return;
			}
		}
		TickCriticalDeathRuntime();
		// 0.9.9.22: world-semantic work owns a tiny slice before bulk actor queues.
		// At 5k population the total frame cap is ~1ms at x20; previously actor annual
		// work could consume the entire Background threshold and starve sect recruitment,
		// dongtian due work, LongShu spawning and queued critical announcements for years.
		TickReservedSemanticControlPlane();
		TickReservedMemoryMaintenance();
		if (ShouldYieldFrame(XjRuntimeFramePriority.CoreAnnual)) return;

		if (XjAptitudeSeedLane.PendingCount > 0)
		{
			long seedSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.SeedLane, 15);
			TickSeedLane(context);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.SeedLane, seedSample);
			if (ShouldYieldFrame(XjRuntimeFramePriority.CoreAnnual)) return;
		}
		if (AnnualIngressQueue.Count > 0)
		{
			TickAnnualIngress();
			if (ShouldYieldFrame(XjRuntimeFramePriority.CoreAnnual)) return;
		}
		if (AnnualActorQueue.Count > 0 || AutoCollectRecheckQueue.Count > 0)
		{
			TickCultivators(context);
			if (ShouldYieldFrame(XjRuntimeFramePriority.CoreAnnual)) return;
		}
		if (AnnualSecondaryQueue.Count > 0)
		{
			TickAnnualSecondary(context);
			if (ShouldYieldFrame(XjRuntimeFramePriority.CoreAnnual)) return;
		}
		if (AnnualMaintenanceQueue.Count > 0)
		{
			TickAnnualMaintenance(context);
			if (ShouldYieldFrame(XjRuntimeFramePriority.Maintenance)) return;
		}

		XjStageZeroObservation.SampleQueuePressure(
			AnnualIngressQueue.Count + AnnualActorQueue.Count,
			AnnualMaintenanceQueue.Count,
			XjAnnualCultivatorSweepLane.PendingCount,
			XjAptitudeSeedLane.PendingCount);
		if (XjStageZeroObservation.HasPending
			&& XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance))
		{
			XjStageZeroObservation.Tick();
		}

		bool hasActiveRuntime = HasCriticalDeathWork || HasActiveRuntimeWork(context);
		bool hasBackgroundRuntime = context.ProcessFast && HasBackgroundWork;
		if ((hasActiveRuntime || hasBackgroundRuntime)
			&& (HasCriticalDeathWork || XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance)))
		{
			long boundedSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BoundedRuntime, 15);
			TickBoundedRuntime(context);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BoundedRuntime, boundedSample);
		}

		if (XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Background))
		{
			long cleanupSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.Cleanup, 63);
			TickCleanup();
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.Cleanup, cleanupSample);
		}
		ReportSchedulerPressure();
	}

	private static bool ShouldYieldFrame(XjRuntimeFramePriority priority)
	{
		if (XjRuntimeFrameGovernor.CanContinue(priority)) return false;
		ReportSchedulerPressure();
		return true;
	}

	internal static XjSchedulerPressureSnapshot CapturePerformancePressureSnapshot()
	{
		return new XjSchedulerPressureSnapshot(
			AnnualIngressQueue.Count,
			AnnualActorQueue.Count,
			AnnualSecondaryQueue.Count,
			AnnualMaintenanceQueue.Count,
			FindOldestPendingSemanticYear());
	}

	private static int FindOldestPendingSemanticYear()
	{
		int oldest = int.MaxValue;
		foreach (int year in AnnualIngressYears.Values)
		{
			if (year > 0 && year < oldest) oldest = year;
		}
		foreach (AnnualActorState state in AnnualActorStates.Values)
		{
			if (state.ActiveYear > 0 && state.ActiveYear < oldest) oldest = state.ActiveYear;
		}
		foreach (AnnualSecondaryState state in AnnualSecondaryStates.Values)
		{
			if (state.ActiveYear > 0 && state.ActiveYear < oldest) oldest = state.ActiveYear;
		}
		foreach (AnnualMaintenanceState state in AnnualMaintenanceStates.Values)
		{
			if (state.ActiveYear > 0 && state.ActiveYear < oldest) oldest = state.ActiveYear;
		}
		return oldest == int.MaxValue ? 0 : oldest;
	}

	private static void ReportSchedulerPressure()
	{
		// Quantitative queue/high-water telemetry has one owner. Gate once here so
		// normal release frames do not pay a chain of no-op ObserveQueue calls.
		if (!XjPerformanceTelemetry.ShouldCollect)
		{
			XjRuntimeDiagnostics.ReportIfDue();
			return;
		}

		XjPerformanceTelemetry.ObserveQueue("annualIngress", AnnualIngressQueue.Count);
		XjPerformanceTelemetry.ObserveQueue("annualCore", AnnualActorQueue.Count);
		XjPerformanceTelemetry.ObserveQueue("annualSecondary", AnnualSecondaryQueue.Count);
		XjPerformanceTelemetry.ObserveQueue("annualMaintenance", AnnualMaintenanceQueue.Count);
		XjPerformanceTelemetry.ObserveQueue("annualAudit", XjAnnualCultivatorSweepLane.PendingCount);
		XjPerformanceTelemetry.ObserveQueue("worldDetection", XjUnifiedDetectionRuntime.PendingCount);
		XjPerformanceTelemetry.ObserveQueue("annualWorldYearLag", XjAnnualWorldRuntimeLane.PendingYearLag);
		XjPerformanceTelemetry.ObserveQueue("announcementPending", XjBroadcastSystem.PendingAnnouncementCount);
		XjPerformanceTelemetry.ObserveQueue("autoCollect", AutoCollectRecheckQueue.Count);
		XjPerformanceTelemetry.ObserveQueue("longshuGrowth", _pendingLongShuGrowthPasses);
		XjPerformanceTelemetry.ObserveQueue("death", HasCriticalDeathWork ? 1 : 0);
		XjPerformanceTelemetry.ObserveQueue("background", HasBackgroundWork ? 1 : 0);
		XjPerformanceTelemetry.ObserveQueue("memoryPrune", XjRuntimeMemoryPruner.HasPending ? 1 : 0);
		XjPerformanceTelemetry.ObserveQueue("jindanCombat", JinDanCombatLane.HasPending ? 1 : 0);
		XjPerformanceTelemetry.ObserveQueue("domains", XjDomainSkillRuntime.ActiveCount);
		long droppedVisuals = XjCombatEffectPool.GetDroppedCustomVisualSpawnCount();
		if (droppedVisuals > 0L)
		{
			XjPerformanceTelemetry.ObserveQueue(
				"visualDrops",
				droppedVisuals > int.MaxValue ? int.MaxValue : (int)droppedVisuals);
		}

		XjRuntimeDiagnostics.ReportIfDue();
	}

	/// <summary>
	/// Releases backing-array high-water retained by scheduler queues/state pools.
	/// This is memory-only maintenance: queue contents and active dictionaries are
	/// preserved, and pooled state objects above the live-world headroom are simply
	/// allowed to return to the GC. Called only by the 25-year hard memory pass.
	/// </summary>
	internal static void ReleaseLongRunRetainedContainers()
	{
		AnnualIngressQueue.TrimExcess();
		AnnualActorQueue.TrimExcess();
		AnnualSecondaryQueue.TrimExcess();
		AnnualMaintenanceQueue.TrimExcess();
		AutoCollectRecheckQueue.TrimExcess();

		int retainedPoolTarget = Math.Max(512, Math.Min(12288, XjCultivatorCache.Count * 2 + 512));
		TrimStatePool(AnnualActorStatePool, retainedPoolTarget);
		TrimStatePool(AnnualSecondaryStatePool, retainedPoolTarget);
		TrimStatePool(AnnualMaintenanceStatePool, retainedPoolTarget);
	}

	private static void TrimStatePool<TState>(Stack<TState> pool, int retainedCount) where TState : class
	{
		if (pool == null) return;
		int target = Math.Max(0, retainedCount);
		while (pool.Count > target) pool.Pop();
		pool.TrimExcess();
	}

	/// <summary>
	/// Clears scheduler-owned queues, cursors and lane-local state only. Domain
	/// caches are owned by XjCacheRegistry/module reset actions and must not be
	/// mirrored here; the old double-clear fan-out made world reloads harder to
	/// reason about and caused feature ownership to leak into the scheduler.
	/// </summary>
	internal static void Clear()
	{
		XjActorRegistry.Clear();
		XjAptitudeSeedLane.Clear();
		XjAnnualCultivatorSweepLane.Clear();
		XjRuntimeInvariantLane.Clear();
		XjStageZeroObservation.Clear();
		_tickCounter = 0;
		XjDeathLaneRegistry.ResetCursor();
		XjBackgroundLaneRegistry.ResetCursor();
		_pendingLongShuGrowthPasses = 0;
		_longShuYearMaintenancePending = false;
		_lastCriticalDeathUnityFrame = -1;
		_semanticAuxCursor = 0;

		AnnualIngressQueue.Clear();
		AnnualIngressYears.Clear();
		AnnualActorQueue.Clear();
		foreach (AnnualActorState state in AnnualActorStates.Values) ReleaseAnnualActorState(state);
		AnnualActorStates.Clear();
		AnnualActorStatePool.Clear();
		DeferredAnnualCompletions.Clear();
		AnnualSecondaryQueue.Clear();
		foreach (AnnualSecondaryState state in AnnualSecondaryStates.Values) ReleaseAnnualSecondaryState(state);
		AnnualSecondaryStates.Clear();
		AnnualSecondaryStatePool.Clear();
		DeferredAnnualSecondaryCompletions.Clear();
		AnnualMaintenanceQueue.Clear();
		foreach (AnnualMaintenanceState state in AnnualMaintenanceStates.Values) ReleaseAnnualMaintenanceState(state);
		AnnualMaintenanceStates.Clear();
		AnnualMaintenanceStatePool.Clear();
		DeferredAnnualMaintenanceCompletions.Clear();
		AutoCollectRecheckQueue.Clear();
		AutoCollectRecheckSet.Clear();
		ReleaseLongRunRetainedContainers();

		XjWorldBootstrapLane.Clear();
		XjZiFuFailureDeathLane.Clear();
		XjRenDanDeathLane.Clear();
		XjQiYuDongTianDeathLane.Clear();
		XjJinDanResidualDeathLane.Clear();
		XjClosedCultivationGuard.Clear();
		JinDanCombatLane.Clear();
		XjUnifiedDetectionRuntime.Clear();
		XjAnnualWorldRuntimeLane.Clear();
	}

	private static void CaptureBackgroundEdges(in XjSchedulerContext context)
	{
		if (context.IsYearChange && XjDetectionGate.ShouldProcessZiFuRuntime())
		{
			_longShuYearMaintenancePending = true;
		}
		if (context.GrowthPasses > 0 && XjLongShuSystem.HasKnownLongShu)
		{
			long total = (long)_pendingLongShuGrowthPasses + context.GrowthPasses;
			_pendingLongShuGrowthPasses = total >= int.MaxValue ? int.MaxValue : (int)total;
		}
	}

	private static bool HasActiveRuntimeWork(in XjSchedulerContext context)
	{
		// 领域按真实秒计时，由fast cadence推进，不依赖年度增长脉冲。
		if (XjDomainSkillRuntime.HasActive)
		{
			return true;
		}
		if (!context.ProcessGrowth)
		{
			return false;
		}

		return XjCombatRegenerationSystem.HasPending
			|| XjClosedCultivationGuard.HasActive
			|| JinDanCombatLane.HasPending;
	}

	private static void TickBootstrapLane()
	{
		if (!XjWorldBootstrapLane.HasPending)
		{
			return;
		}

		XjWorldBootstrapLane.Tick(
			XjRuntimeWorkBudget.ScaleCount(BootstrapBudget, 24),
			XjRuntimeWorkBudget.ScaleMilliseconds(BootstrapTimeBudgetMs, 0.5d));
	}

	private static void TickSeedLane(in XjSchedulerContext context)
	{
		if (XjAptitudeSeedLane.PendingCount <= 0)
		{
			return;
		}

		int budget = context.IsYearChange
			? SeedYearBudget
			: context.ProcessGrowth
				? SeedGrowthBudget
				: SeedFastBudget;
		int minimumBudget = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 2,
			XjRuntimeStressTier.Severe => 4,
			XjRuntimeStressTier.Mild => 8,
			_ => 12
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 0.08d,
			XjRuntimeStressTier.Severe => 0.14d,
			XjRuntimeStressTier.Mild => 0.25d,
			_ => 0.5d
		};
		XjAptitudeSeedLane.Tick(
			XjRuntimeWorkBudget.ScaleCount(budget, minimumBudget),
			XjRuntimeWorkBudget.ScaleMilliseconds(SeedLaneTimeBudgetMs, minimumMilliseconds));
	}


	private static void TickAnnualIngress()
	{
		if (AnnualIngressQueue.Count == 0)
		{
			return;
		}

		int minimumBudget = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 16,
			XjRuntimeStressTier.Severe => 32,
			XjRuntimeStressTier.Mild => 64,
			_ => 128
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 0.08d,
			XjRuntimeStressTier.Severe => 0.12d,
			XjRuntimeStressTier.Mild => 0.20d,
			_ => 0.35d
		};
		int budget = XjRuntimeWorkBudget.ScaleCount(AnnualIngressActorBudget, minimumBudget);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(
			AnnualIngressLaneTimeBudgetMs,
			minimumMilliseconds);
		XjCooperativeBudget workBudget = new XjCooperativeBudget(
			budget,
			timeBudgetMs,
			XjRuntimeFramePriority.CoreAnnual);
		while (AnnualIngressQueue.Count > 0 && workBudget.TryTake())
		{
			long actorId = AnnualIngressQueue.Dequeue();
			if (!AnnualIngressYears.TryGetValue(actorId, out int requestedYear))
			{
				continue;
			}
			AnnualIngressYears.Remove(actorId);
			if (XjActorRegistry.Resolve(actorId, out Actor actor)
				&& actor?.data != null
				&& actor.isAlive())
			{
				EnqueueAnnualActorCore(actor, actorId, requestedYear);
			}
		}
	}

	private static void TickCultivators(in XjSchedulerContext context)
	{
		if (!XjDetectionGate.ShouldProcessAnnualActors(context)) return;
		if (AnnualActorQueue.Count == 0)
		{
			TickAutoCollectRechecks(context);
			return;
		}

		// Core cultivation owns an independent guaranteed budget. Maintenance work is
		// never allowed to consume this wall-clock slice or delay the completion cursor.
		bool catchUpFrame = context.ProcessFast && !context.IsYearChange;
		int heavyBacklogThreshold = Math.Max(256, XjCultivatorCache.Count / 4);
		bool heavyBacklog = AnnualActorQueue.Count >= heavyBacklogThreshold;
		int minimumStages = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => heavyBacklog ? 48 : (catchUpFrame ? 8 : 12),
			XjRuntimeStressTier.Severe => heavyBacklog ? 72 : (catchUpFrame ? 12 : 20),
			XjRuntimeStressTier.Mild => heavyBacklog ? 112 : (catchUpFrame ? 24 : 36),
			_ => heavyBacklog ? 160 : (catchUpFrame ? 48 : 64)
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => heavyBacklog ? 0.45d : (catchUpFrame ? 0.10d : 0.14d),
			XjRuntimeStressTier.Severe => heavyBacklog ? 0.60d : (catchUpFrame ? 0.16d : 0.22d),
			XjRuntimeStressTier.Mild => heavyBacklog ? 0.80d : (catchUpFrame ? 0.24d : 0.36d),
			_ => heavyBacklog ? 1.05d : (catchUpFrame ? 0.45d : 0.75d)
		};
		int baseStageBudget = catchUpFrame ? AnnualCoreStageBudget / 2 : AnnualCoreStageBudget;
		double baseTimeBudgetMs = catchUpFrame ? AnnualCoreLaneTimeBudgetMs * 0.55d : AnnualCoreLaneTimeBudgetMs;
		int stageBudget = XjRuntimeWorkBudget.ScaleCount(baseStageBudget, minimumStages);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(baseTimeBudgetMs, minimumMilliseconds);
		XjCooperativeBudget workBudget = new XjCooperativeBudget(
			stageBudget,
			timeBudgetMs,
			XjRuntimeFramePriority.CoreAnnual);
		while (AnnualActorQueue.Count > 0 && workBudget.TryTake())
		{
			long actorId = AnnualActorQueue.Dequeue();
			if (!AnnualActorStates.TryGetValue(actorId, out AnnualActorState state)) continue;
			state.Queued = false;
			state.InFlight = true;
			if (!XjActorRegistry.Resolve(actorId, out Actor actor))
			{
				RemoveAnnualActorState(actorId);
			}
			else if (!XjCultivatorCache.IsCultivator(actorId) && !XjCultivatorCache.CheckAndUpdate(actor))
			{
				int completedThroughYear = Math.Max(state.LastCompletedYear, state.LatestRequestedYear);
				CompletePersistedAnnualState(actor, completedThroughYear);
				EnsureAnnualSecondaryBaseline(actor, completedThroughYear);
				EnsureAnnualMaintenanceBaseline(actor, completedThroughYear);
				RemoveAnnualActorState(actorId);
			}
			else if (state.Stage == XjAnnualPipelineStage.Finalize)
			{
				AnnualActorStates.Remove(actorId);
				AnnualActorState migrated = MigrateLegacyFinalizeState(actor, actorId, state);
				if (migrated != null)
				{
					AnnualActorStates[actorId] = migrated;
					QueueAnnualState(actorId, migrated);
				}
				else
				{
					state.ReleaseRequested = true;
				}
			}
			else
			{
				int liveYear = Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0);
				if (liveYear > state.LatestRequestedYear) state.LatestRequestedYear = liveYear;
				if (state.Stage == XjAnnualPipelineStage.Prepare && state.ActiveYear < state.LatestRequestedYear)
				{
					state.ActiveYear = state.LatestRequestedYear;
				}

				XjAnnualPipelineStage observedStage = state.Stage;
				if (observedStage == XjAnnualPipelineStage.Prepare)
				{
					XjStageZeroObservation.RecordAnnualPrepare(actor, state.LastCompletedYear, state.ActiveYear, state.LatestRequestedYear);
				}
				XjRuntimeHotspot hotspot = observedStage == XjAnnualPipelineStage.Progression
					? XjRuntimeHotspot.AnnualProgression
					: XjRuntimeHotspot.AnnualPrepare;
				long observationStarted = XjStageZeroObservation.BeginAnnualStage();
				long sampleStarted = XjRuntimeDiagnostics.BeginSample(hotspot, 31);
				bool hasNext = XjSchedulerActorPipeline.ProcessStage(
					actor,
					state.ActiveYear,
					observedStage,
					JinDanCombatLane.EnqueueActor,
					out XjAnnualPipelineStage nextStage);
				XjRuntimeDiagnostics.EndSample(hotspot, sampleStarted);
				XjStageZeroObservation.EndAnnualStage(observedStage, observationStarted);
				bool stillRegistered = AnnualActorStates.TryGetValue(actorId, out AnnualActorState registeredState)
					&& ReferenceEquals(registeredState, state)
					&& !state.ReleaseRequested;
				if (!stillRegistered)
				{
					// Death/removal may synchronously unregister the actor while the stage runs.
					// Do not persist or requeue that detached state; it is returned below.
				}
				else if (hasNext)
				{
					state.Stage = nextStage;
					QueueAnnualState(actorId, state);
				}
				else
				{
					int completedYear = state.ActiveYear;
					state.LastCompletedYear = Math.Max(state.LastCompletedYear, completedYear);
					CompletePersistedAnnualState(actor, state.LastCompletedYear);
					EnsureAnnualSecondaryStateLoaded(actor, actorId, Math.Max(0, completedYear - 1));
					EnqueueAnnualSecondary(actor, actorId, completedYear);
					EnsureAnnualMaintenanceStateLoaded(actor, actorId, Math.Max(0, completedYear - 1));
					EnqueueAnnualMaintenance(actor, actorId, completedYear);
					XjStageZeroObservation.RecordAnnualCoreCompletion(actor, completedYear, migratedLegacyFinalize: false);
					if (state.LatestRequestedYear > state.LastCompletedYear)
					{
						state.ActiveYear = ResolveNextAnnualActiveYear(state.LastCompletedYear, state.LatestRequestedYear);
						state.Stage = XjAnnualPipelineStage.Prepare;
						QueueAnnualState(actorId, state);
					}
					else
					{
						RemoveAnnualActorState(actorId);
					}
				}
			}

			state.InFlight = false;
			if (state.ReleaseRequested)
			{
				state.ReleaseRequested = false;
				ReleaseAnnualActorState(state);
			}
		}
		TickAutoCollectRechecks(context);
	}

	private static void TickAnnualSecondary(in XjSchedulerContext context)
	{
		if (!XjDetectionGate.ShouldProcessAnnualActors(context) || AnnualSecondaryQueue.Count == 0) return;
		bool coreUnderPressure = AnnualActorQueue.Count >= Math.Max(128, XjCultivatorCache.Count / 8);
		int minimumActors = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => coreUnderPressure ? 4 : 12,
			XjRuntimeStressTier.Severe => coreUnderPressure ? 8 : 24,
			XjRuntimeStressTier.Mild => coreUnderPressure ? 20 : 48,
			_ => coreUnderPressure ? 40 : 96
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => coreUnderPressure ? 0.06d : 0.14d,
			XjRuntimeStressTier.Severe => coreUnderPressure ? 0.10d : 0.24d,
			XjRuntimeStressTier.Mild => coreUnderPressure ? 0.20d : 0.40d,
			_ => coreUnderPressure ? 0.35d : 0.70d
		};
		int baseBudget = coreUnderPressure ? AnnualSecondaryActorBudget / 2 : AnnualSecondaryActorBudget;
		double baseMilliseconds = coreUnderPressure ? AnnualSecondaryLaneTimeBudgetMs * 0.5d : AnnualSecondaryLaneTimeBudgetMs;
		int actorBudget = XjRuntimeWorkBudget.ScaleCount(baseBudget, minimumActors);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(baseMilliseconds, minimumMilliseconds);
		XjCooperativeBudget workBudget = new XjCooperativeBudget(
			actorBudget,
			timeBudgetMs,
			XjRuntimeFramePriority.CoreAnnual);
		while (AnnualSecondaryQueue.Count > 0 && workBudget.TryTake())
		{
			long actorId = AnnualSecondaryQueue.Dequeue();
			if (!AnnualSecondaryStates.TryGetValue(actorId, out AnnualSecondaryState state)) continue;
			state.Queued = false;
			state.InFlight = true;
			if (!XjActorRegistry.Resolve(actorId, out Actor actor)
				|| (!XjCultivatorCache.IsCultivator(actorId) && !XjCultivatorCache.CheckAndUpdate(actor)))
			{
				RemoveAnnualSecondaryState(actorId);
			}
			else
			{
				int exactYear = Math.Max(state.LastCompletedYear + 1, state.ActiveYear);
				int skippedEmptyYears = 0;
				// Sparse secondary jobs (lost-fabao discovery, JinDan gifts, scheduled
				// forging, DaoTai XianQi) no longer force one dequeue/requeue cycle for
				// every empty historical year. Fast-forward a bounded run of years that
				// provably have no exact-year extension interest, then execute at most one
				// real opportunity in this actor turn. Craft/weapon-art actors still replay
				// every year because their queue-interest remains true.
				while (exactYear <= state.LatestRequestedYear
					&& skippedEmptyYears < 12
					&& !workBudget.ShouldYield
					&& !XjSchedulerActorPipeline.HasSecondaryAnnualInterest(actor, exactYear))
				{
					state.LastCompletedYear = exactYear;
					CompleteAnnualSecondaryState(actor, exactYear);
					exactYear++;
					skippedEmptyYears++;
				}

				if (exactYear <= state.LatestRequestedYear
					&& !workBudget.ShouldYield
					&& XjSchedulerActorPipeline.HasSecondaryAnnualInterest(actor, exactYear))
				{
					XjSchedulerActorPipeline.ProcessSecondaryAnnual(actor, exactYear);
					bool stillRegistered = AnnualSecondaryStates.TryGetValue(actorId, out AnnualSecondaryState registeredState)
						&& ReferenceEquals(registeredState, state)
						&& !state.ReleaseRequested;
					if (stillRegistered)
					{
						state.LastCompletedYear = exactYear;
						CompleteAnnualSecondaryState(actor, exactYear);
						exactYear++;
					}
				}

				bool remainsRegistered = AnnualSecondaryStates.TryGetValue(actorId, out AnnualSecondaryState remainingState)
					&& ReferenceEquals(remainingState, state)
					&& !state.ReleaseRequested;
				if (remainsRegistered)
				{
					if (state.LatestRequestedYear >= exactYear)
					{
						state.ActiveYear = exactYear;
						QueueAnnualSecondaryState(actorId, state);
					}
					else
					{
						RemoveAnnualSecondaryState(actorId);
					}
				}
			}
			state.InFlight = false;
			if (state.ReleaseRequested)
			{
				state.ReleaseRequested = false;
				ReleaseAnnualSecondaryState(state);
			}
		}
	}

	private static void TickAnnualMaintenance(in XjSchedulerContext context)
	{
		if (!XjDetectionGate.ShouldProcessAnnualActors(context) || AnnualMaintenanceQueue.Count == 0) return;
		bool coreUnderPressure = AnnualActorQueue.Count >= Math.Max(128, XjCultivatorCache.Count / 8);
		int minimumStages = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => coreUnderPressure ? 4 : 8,
			XjRuntimeStressTier.Severe => coreUnderPressure ? 8 : 16,
			XjRuntimeStressTier.Mild => coreUnderPressure ? 16 : 28,
			_ => coreUnderPressure ? 24 : 48
		};
		double minimumMilliseconds = XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => coreUnderPressure ? 0.04d : 0.08d,
			XjRuntimeStressTier.Severe => coreUnderPressure ? 0.08d : 0.14d,
			XjRuntimeStressTier.Mild => coreUnderPressure ? 0.14d : 0.24d,
			_ => coreUnderPressure ? 0.22d : 0.45d
		};
		int baseBudget = coreUnderPressure ? AnnualMaintenanceStageBudget / 2 : AnnualMaintenanceStageBudget;
		double baseMilliseconds = coreUnderPressure ? AnnualMaintenanceLaneTimeBudgetMs * 0.5d : AnnualMaintenanceLaneTimeBudgetMs;
		int stageBudget = XjRuntimeWorkBudget.ScaleCount(baseBudget, minimumStages);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(baseMilliseconds, minimumMilliseconds);
		XjCooperativeBudget workBudget = new XjCooperativeBudget(
			stageBudget,
			timeBudgetMs,
			XjRuntimeFramePriority.Maintenance);
		while (AnnualMaintenanceQueue.Count > 0 && workBudget.TryTake())
		{
			long actorId = AnnualMaintenanceQueue.Dequeue();
			if (!AnnualMaintenanceStates.TryGetValue(actorId, out AnnualMaintenanceState state)) continue;
			state.Queued = false;
			state.InFlight = true;
			if (!XjActorRegistry.Resolve(actorId, out Actor actor)
				|| (!XjCultivatorCache.IsCultivator(actorId) && !XjCultivatorCache.CheckAndUpdate(actor)))
			{
				RemoveAnnualMaintenanceState(actorId);
			}
			else
			{
				XjRuntimeHotspot hotspot = state.Stage switch
				{
					XjAnnualMaintenanceStage.Ancillary => XjRuntimeHotspot.AnnualMaintenanceAncillary,
					XjAnnualMaintenanceStage.Assets => XjRuntimeHotspot.AnnualMaintenanceAssets,
					_ => XjRuntimeHotspot.AnnualMaintenanceIdentity
				};
				long observationStarted = XjStageZeroObservation.BeginAnnualStage();
				long sampleStarted = XjRuntimeDiagnostics.BeginSample(hotspot, 31);
				XjAnnualMaintenanceStage observedStage = state.Stage;
				bool hasNext = XjSchedulerActorPipeline.ProcessMaintenanceStage(
					actor,
					state.FromYearInclusive,
					state.ActiveYear,
					observedStage,
					out XjAnnualMaintenanceStage nextStage);
				XjRuntimeDiagnostics.EndSample(hotspot, sampleStarted);
				XjStageZeroObservation.EndAnnualMaintenanceStage(observedStage, observationStarted);
				bool stillRegistered = AnnualMaintenanceStates.TryGetValue(actorId, out AnnualMaintenanceState registeredState)
					&& ReferenceEquals(registeredState, state)
					&& !state.ReleaseRequested;
				if (!stillRegistered)
				{
					// Actor removal during maintenance detaches the state immediately.
				}
				else if (hasNext)
				{
					state.Stage = nextStage;
					QueueAnnualMaintenanceState(actorId, state);
				}
				else
				{
					state.LastCompletedYear = Math.Max(state.LastCompletedYear, state.ActiveYear);
					CompleteAnnualMaintenanceState(actor, state.LastCompletedYear);
					XjStageZeroObservation.RecordAnnualMaintenanceCompletion(actor, state.LastCompletedYear);
					if (state.LatestRequestedYear > state.LastCompletedYear)
					{
						state.FromYearInclusive = state.LastCompletedYear + 1;
						state.ActiveYear = state.LatestRequestedYear;
						state.Stage = XjAnnualMaintenanceStage.Identity;
						if (XjSchedulerActorPipeline.HasMaintenanceAnnualInterest(
							actor,
							state.FromYearInclusive,
							state.ActiveYear))
						{
							QueueAnnualMaintenanceState(actorId, state);
						}
						else
						{
							state.LastCompletedYear = state.ActiveYear;
							CompleteAnnualMaintenanceState(actor, state.LastCompletedYear);
							RemoveAnnualMaintenanceState(actorId);
						}
					}
					else
					{
						RemoveAnnualMaintenanceState(actorId);
					}
				}
			}
			state.InFlight = false;
			if (state.ReleaseRequested)
			{
				state.ReleaseRequested = false;
				ReleaseAnnualMaintenanceState(state);
			}
		}
	}

	private static void TickAutoCollectRechecks(in XjSchedulerContext context)
	{
		if ((!context.ProcessFast && !context.IsYearChange) || AutoCollectRecheckQueue.Count == 0)
		{
			return;
		}

		int budget = XjRuntimeWorkBudget.ScaleCount(AutoCollectRecheckBudget, 32);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(AutoCollectRecheckTimeBudgetMs, 0.35d);
		long sampleStarted = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AutoCollectRecheck, 0);
		XjCooperativeBudget workBudget = new XjCooperativeBudget(
			budget,
			timeBudgetMs,
			XjRuntimeFramePriority.CoreAnnual);
		while (AutoCollectRecheckQueue.Count > 0 && workBudget.TryTake())
		{
			long actorId = AutoCollectRecheckQueue.Dequeue();
			if (!AutoCollectRecheckSet.Remove(actorId))
			{
				continue;
			}
			if (XjActorRegistry.Resolve(actorId, out Actor actor)
				&& actor?.data != null
				&& actor.isAlive())
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
				XjAutoCollectSystem.TickActor(actor, realmId);
			}
		}
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AutoCollectRecheck, sampleStarted);
	}

	/// <summary>
	/// Non-starvable semantic control-plane slice. This does not increase the global
	/// XuanJian frame cap; it only reserves the first small part of that existing cap
	/// before thousands of actor annual states are allowed to consume it. All work is
	/// already-pending O(1)/bounded work and at most one auxiliary lane is advanced.
	/// </summary>
	private static void TickReservedSemanticControlPlane()
	{
		if (XjWorldBootstrapLane.HasPending
			|| !XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.CoreAnnual))
		{
			return;
		}

		if (XjAnnualWorldRuntimeLane.HasPending)
		{
			int lag = XjAnnualWorldRuntimeLane.PendingYearLag;
			int stageBudget = lag >= 8 ? 8 : lag >= 3 ? 6 : 4;
			double timeBudgetMs = lag >= 8 ? 0.28d : lag >= 3 ? 0.22d : 0.16d;
			long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundAnnualWorld, 0);
			XjAnnualWorldRuntimeLane.Tick(
				stageBudget,
				timeBudgetMs,
				XjRuntimeFramePriority.CoreAnnual);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundAnnualWorld, sample);
		}

		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.CoreAnnual)) return;

		// Four semantic auxiliaries share one tiny slot. A lane that has no work is
		// skipped immediately, so the slot is not lost merely because its turn is idle.
		for (int probe = 0; probe < 4; probe++)
		{
			int lane = (_semanticAuxCursor + probe) & 3;
			switch (lane)
			{
				case 0:
					if (!XjUnifiedDetectionRuntime.HasPending) continue;
					XjUnifiedDetectionRuntime.Tick(
						24,
						2,
						0.08d,
						XjRuntimeFramePriority.CoreAnnual);
					break;

				case 1:
					if (!XjBroadcastSystem.HasPendingAnnouncements) continue;
					XjBroadcastSystem.TickCriticalAnnouncements();
					break;

				case 2:
					if (!XjLongShuSystem.HasPendingSpawnWork) continue;
					// Spawn only: yearly LongShu maintenance/growth remains in its normal lane.
					XjLongShuSystem.TickRuntime(isYearChange: false, growthPasses: 0);
					break;

				default:
					if (!XjDongTianRegistry.NeedsRuntimeTick) continue;
					TickDongTianBackground(new XjCooperativeBudget(
						1,
						0.10d,
						XjRuntimeFramePriority.CoreAnnual));
					break;
			}

			_semanticAuxCursor = (lane + 1) & 3;
			XjRuntimeFrameGovernor.MarkPotentialOverrun("semantic-control-plane");
			return;
		}

		_semanticAuxCursor = (_semanticAuxCursor + 1) & 3;
	}

	private static void TickReservedMemoryMaintenance()
	{
		bool hasStaleSweep = XjRuntimeMemoryPruner.HasStaleIdSweepWork;
		bool hasSoftCycle = XjRuntimeMemoryPruner.HasSoftRunnableWork;
		if (XjWorldBootstrapLane.HasPending
			|| (!hasStaleSweep && !hasSoftCycle)
			|| !XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance)
			|| !XjRuntimeWorkBudget.ShouldRunMemoryMaintenanceReserve()
			|| !XjRuntimeWorkBudget.TryBeginMemoryMaintenancePass())
		{
			return;
		}

		// H3: stale-id eviction is the high-speed semantic cleanup layer. It only
		// probes stable ids and clears confirmed unavailable runtime state; no
		// TrimExcess/container replacement is allowed here.
		if (hasStaleSweep)
		{
			XjRuntimeMemoryPruner.TickStaleIdEviction(
				XjRuntimeWorkBudget.ScaleCount(32, 4),
				XjRuntimeWorkBudget.ScaleMilliseconds(0.08d, 0.02d));
		}

		if (hasSoftCycle && XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance))
		{
			XjRuntimeMemoryPruner.Tick(
				1,
				XjRuntimeWorkBudget.ScaleMilliseconds(0.10d, 0.03d));
		}
		XjRuntimeFrameGovernor.MarkPotentialOverrun("memory-prune-reserve");
	}

	private static void TickBoundedRuntime(in XjSchedulerContext context)
	{
		TickCriticalDeathRuntime();
		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance)) return;

		if (context.ProcessGrowth && XjCombatRegenerationSystem.HasPending)
		{
			long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.CombatRegeneration, 0);
			XjCombatRegenerationSystem.Tick(
				XjRuntimeWorkBudget.ScaleCount(CombatRegenerationBudget, 12),
				ResolveActorDirect);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.CombatRegeneration, sample);
		}
		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance)) return;
		if (context.ProcessGrowth && XjClosedCultivationGuard.HasActive)
		{
			long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.ClosedCultivation, 0);
			XjClosedCultivationGuard.TickRuntime(
				XjRuntimeWorkBudget.ScaleCount(ClosedCultivationBudget, 2),
				ResolveActorDirect);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.ClosedCultivation, sample);
		}
		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance)) return;
		if (context.ProcessGrowth && JinDanCombatLane.HasPending)
		{
			long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.JinDanCombat, 0);
			JinDanCombatLane.Tick(XjRuntimeWorkBudget.ScaleCount(JinDanCombatBudget, 8));
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.JinDanCombat, sample);
		}
		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Maintenance)) return;
		if (context.ProcessFast && XjDomainSkillRuntime.HasActive)
		{
			XjDomainSkillRuntime.Tick(XjRuntimeWorkBudget.ScaleCount(DomainRuntimeBudget, 1));
		}
		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Background)) return;
		// 所有低频全局检测只通过一个后台消费者执行。先让已经登记的
		// 分帧扫描前进，再让年度命令车道检查是否可以越过等待阶段。
		if (context.ProcessFast && !XjWorldBootstrapLane.HasPending && XjUnifiedDetectionRuntime.HasPending)
		{
			XjUnifiedDetectionRuntime.Tick(
				XjRuntimeWorkBudget.ScaleCount(UnifiedDetectionActorBudget, 32),
				XjRuntimeWorkBudget.ScaleCount(UnifiedDetectionKingdomBudget, 2),
				XjRuntimeWorkBudget.ScaleMilliseconds(UnifiedDetectionTimeBudgetMs, 0.10d));
		}
		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Background)) return;

		// 年度世界车道是高倍速下的控制面，不再与普通后台十相位争抢唯一槽位。
		// 每个 fast pass 只给它一个严格的阶段数与墙钟预算，既能持续追赶年份，
		// 又不会把多个重任务无界叠到同一渲染帧。
		if (context.ProcessFast && !XjWorldBootstrapLane.HasPending && XjAnnualWorldRuntimeLane.HasPending)
		{
			long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundAnnualWorld, 0);
			XjAnnualWorldRuntimeLane.Tick(
				XjRuntimeWorkBudget.ScaleCount(AnnualWorldStageBudget, 2),
				XjRuntimeWorkBudget.ScaleMilliseconds(AnnualWorldTimeBudgetMs, 0.20d));
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundAnnualWorld, sample);
		}
		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Background)) return;

		// Memory maintenance is never executed inside the annual-world stage itself.
		// Wait until core/secondary/maintenance recovery has drained, then perform at
		// most one small structural stage per fast cadence.
		if (context.ProcessFast
			&& !XjWorldBootstrapLane.HasPending
			&& !XjAnnualWorldRuntimeLane.HasPending
			&& !HasAnnualActorBacklog
			&& XjRuntimeMemoryPruner.HasRunnableWork
			&& XjRuntimeWorkBudget.TryBeginMemoryMaintenancePass())
		{
			XjRuntimeMemoryPruner.Tick(
				1,
				XjRuntimeWorkBudget.ScaleMilliseconds(0.25d, 0.08d));
		}
		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Background)) return;

		if (context.ProcessFast && XjRuntimeWorkBudget.ShouldRunBackgroundPhase())
		{
			TickBackgroundRuntime();
		}
	}

	private static void TickCriticalDeathRuntime()
	{
		if (!HasCriticalDeathWork) return;
		int unityFrame = UnityEngine.Time.frameCount;
		if (_lastCriticalDeathUnityFrame == unityFrame) return;
		_lastCriticalDeathUnityFrame = unityFrame;

		XjDeathActorResolver resolver = ResolveActor;
		int deathBudget = XjRuntimeWorkBudget.ScaleCount(DeathLaneBudget, 1);
		long deathSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.DeathRuntime, 0);
		TickOnePendingDeathLane(deathBudget, resolver);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.DeathRuntime, deathSample);

		bool archiveWorkedThisFrame = false;
		if (XjDeathArchiveQueue.HasPending)
		{
			long archiveSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.DeathArchive, 0);
			XjDeathArchiveQueue.Tick(XjRuntimeWorkBudget.ScaleCount(DeathArchiveBudget, 2));
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.DeathArchive, archiveSample);
			archiveWorkedThisFrame = true;
		}
		// 金性入库与公告继续强制错开死亡归档，避免同帧堆叠重业务。
		if (!archiveWorkedThisFrame && XjJinDanResidualDeathLane.HasPending)
		{
			XjJinDanResidualDeathLane.Tick(1);
		}
		XjRuntimeFrameGovernor.MarkPotentialOverrun("critical-death");
	}

	private static void TickOnePendingDeathLane(int budget, XjDeathActorResolver resolver)
	{
		XjDeathLaneRegistry.TickNext(budget, resolver);
	}

	private static void TickBackgroundRuntime()
	{
		// Player-triggered codex refresh is an explicit priority request rather than
		// a permanent priority embedded in the round-robin registry.
		if (!XjWorldBootstrapLane.HasPending && XjCodexSnapshotPublisher.HasManualRefreshPending)
		{
			TickCodexBackground();
			return;
		}

		XjBackgroundLaneRegistry.TickNext();
	}

	private static void TickCleanup()
	{
		_tickCounter++;
		if (!XjDetectionGate.ShouldRunCadence(XjRuntimeDetectionJob.LifecycleAudit, _tickCounter))
		{
			return;
		}

		_tickCounter = 0;
		XjActorRegistry.CleanupInvalid(CleanupRemoveBudget, ForgetActorRuntimeState);
		XjCultivatorCache.CleanupInvalid(
			CleanupInspectBudget,
			ForgetUnavailableActor);
	}

	private static Actor ResolveActorDirect(long actorId)
	{
		XjActorRegistry.Resolve(actorId, out Actor actor);
		return actor;
	}

	private static bool IsJinDanActor(Actor actor)
	{
		return actor?.data != null
			&& XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetString(
				actor,
				XuanJianVNext.Data.Rules.XjActorDataKeys.RealmId,
				out string realmId)
			&& (XuanJianVNext.Data.Rules.XjCultivationPathRules.IsJinDanEquivalentRealm(realmId)
				|| string.Equals(realmId, XuanJianVNext.Data.Rules.XjRealmIds.DaoTai, StringComparison.Ordinal)
				|| string.Equals(realmId, XuanJianVNext.Data.Rules.XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
				|| string.Equals(realmId, XuanJianVNext.Data.Rules.XjRealmIds.ShenDan, StringComparison.Ordinal));
	}
}
