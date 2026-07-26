using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Core;

/// <summary>
/// 阶段0年度链路观测器。只读取状态、累计计数并低频导出，不修改修炼结果。
/// 人口普查每百年一次，按小预算分帧执行，避免诊断逻辑本身成为长期负担。
/// </summary>
internal static class XjStageZeroObservation
{
	private sealed class WindowCounters
	{
		internal long AnnualPrepareRuns;
		internal long AnnualProgressionRuns;
		internal long AnnualFinalizeRuns;
		internal long AnnualMaintenanceIdentityRuns;
		internal long AnnualMaintenanceAncillaryRuns;
		internal long AnnualMaintenanceAssetsRuns;
		internal long AnnualCoreCompletions;
		internal long AnnualMaintenanceCompletions;
		internal long LegacyFinalizeMigrations;
		internal long AnnualStageTicks;
		internal long AnnualStageTotalTicks;
		internal long AnnualStageMaxTicks;
		internal long CollapsedActors;
		internal long CollapsedYears;
		internal int MaxCollapsedYears;
		internal long RelevantGongFaDueLost;
		internal long RelevantGongFaPhaseStarvation;
		internal long OpportunityDebtRecovered;
		internal long OpportunityDebtYearsRecovered;
		internal readonly Dictionary<string, long> OpportunityDebtByKind = new(StringComparer.Ordinal);
		internal long CandidateRefreshes;
		internal long CandidateDirtyRefreshes;
		internal readonly Dictionary<string, long> CandidateRoutes = new(StringComparer.Ordinal);
		internal readonly Dictionary<string, long> CandidateSkips = new(StringComparer.Ordinal);
		internal long GrowthRuns;
		internal long GrowthCatchUpActors;
		internal long GrowthElapsedYears;
		internal int MaxGrowthElapsedYears;
		internal long GongFaAttempts;
		internal long GongFaSuccesses;
		internal long BreakthroughAttempts;
		internal long BreakthroughSuccesses;
		internal long JinDanAttempts;
		internal long JinDanSuccesses;
		internal long AptitudeChecks;
		internal long AptitudeGranted;
		internal long AptitudeAgeFive;
		internal long AptitudeAgeSix;
		internal long AptitudeWindowMissed;
		internal long AnnualActorStateRents;
		internal long AnnualActorStatePoolHits;
		internal long AnnualMaintenanceStateRents;
		internal long AnnualMaintenanceStatePoolHits;
		internal long IdSnapshotFills;
		internal long IdSnapshotBufferReuses;
		internal long IdSnapshotCapacityGrowths;
		internal long IdSnapshotIdsCopied;
		internal long AnnualLightSnapshots;
		internal long AnnualFullSnapshots;
		internal long NoFaBaoSignatureFastPaths;
		internal long WorldSnapshotDuplicateSkips;
		internal readonly Dictionary<string, long> GongFaAttemptsByGrade = new(StringComparer.Ordinal);
		internal readonly Dictionary<string, long> GongFaSuccessesByGrade = new(StringComparer.Ordinal);
		internal readonly Dictionary<string, long> BreakthroughReasons = new(StringComparer.Ordinal);
		internal readonly Dictionary<string, long> Promotions = new(StringComparer.Ordinal);
		internal readonly Dictionary<string, long> JinDanResults = new(StringComparer.Ordinal);
		internal readonly Dictionary<string, long> AptitudeResults = new(StringComparer.Ordinal);

		internal void Reset()
		{
			AnnualPrepareRuns = 0L;
			AnnualProgressionRuns = 0L;
			AnnualFinalizeRuns = 0L;
			AnnualMaintenanceIdentityRuns = 0L;
			AnnualMaintenanceAncillaryRuns = 0L;
			AnnualMaintenanceAssetsRuns = 0L;
			AnnualCoreCompletions = 0L;
			AnnualMaintenanceCompletions = 0L;
			LegacyFinalizeMigrations = 0L;
			AnnualStageTicks = 0L;
			AnnualStageTotalTicks = 0L;
			AnnualStageMaxTicks = 0L;
			CollapsedActors = 0L;
			CollapsedYears = 0L;
			MaxCollapsedYears = 0;
			RelevantGongFaDueLost = 0L;
			RelevantGongFaPhaseStarvation = 0L;
			OpportunityDebtRecovered = 0L;
			OpportunityDebtYearsRecovered = 0L;
			GrowthRuns = 0L;
			GrowthCatchUpActors = 0L;
			GrowthElapsedYears = 0L;
			MaxGrowthElapsedYears = 0;
			GongFaAttempts = 0L;
			GongFaSuccesses = 0L;
			BreakthroughAttempts = 0L;
			BreakthroughSuccesses = 0L;
			JinDanAttempts = 0L;
			JinDanSuccesses = 0L;
			AptitudeChecks = 0L;
			AptitudeGranted = 0L;
			AptitudeAgeFive = 0L;
			AptitudeAgeSix = 0L;
			AptitudeWindowMissed = 0L;
			AnnualActorStateRents = 0L;
			AnnualActorStatePoolHits = 0L;
			AnnualMaintenanceStateRents = 0L;
			AnnualMaintenanceStatePoolHits = 0L;
			IdSnapshotFills = 0L;
			IdSnapshotBufferReuses = 0L;
			IdSnapshotCapacityGrowths = 0L;
			IdSnapshotIdsCopied = 0L;
			AnnualLightSnapshots = 0L;
			AnnualFullSnapshots = 0L;
			NoFaBaoSignatureFastPaths = 0L;
			WorldSnapshotDuplicateSkips = 0L;
			GongFaAttemptsByGrade.Clear();
			GongFaSuccessesByGrade.Clear();
			BreakthroughReasons.Clear();
			Promotions.Clear();
			JinDanResults.Clear();
			AptitudeResults.Clear();
			OpportunityDebtByKind.Clear();
			CandidateRefreshes = 0L;
			CandidateDirtyRefreshes = 0L;
			CandidateRoutes.Clear();
			CandidateSkips.Clear();
		}
	}

	private sealed class CensusState
	{
		internal int Year;
		internal int KnownActors;
		internal int CultivatorsAtStart;
		internal List<long> ActorIds;
		internal int Cursor;
		internal int Processed;
		internal int MissingActors;
		internal int ObservationErrors;
		internal readonly int[] RealmCounts = new int[7];
		internal readonly int[] GongFaGrades = new int[7];
		internal readonly Dictionary<string, int> Blockers = new(StringComparer.Ordinal);
		internal readonly List<int> AnnualLagYears = new(1024);
		internal readonly List<int> MaintenanceLagYears = new(1024);
		internal int QueueAtSchedule;
		internal int MaintenanceQueueAtSchedule;
		internal int SweepAtSchedule;
		internal int QueueHighWater;
		internal int MaintenanceQueueHighWater;
		internal int SweepHighWater;
		internal int SeedHighWater;
		internal long StartedTicks;
		internal XjProgressionCandidateCounts CandidateCounts;
	}

	private const int CensusIntervalYears = 100;
	private const int CensusActorBudget = 128;
	private const double CensusTimeBudgetMs = 0.22d;
	private const string CsvFileName = "annual_observation.csv";
	private const string EventCsvFileName = "annual_event_windows.csv";
	private const string BlockerCsvFileName = "annual_blockers.csv";
	private const string EventDetailCsvFileName = "annual_event_details.csv";
	private const string LatestJsonFileName = "annual_observation_latest.json";
	private const string SessionInfoFileName = "session_info.txt";

	private static readonly WindowCounters Window = new();
	private static CensusState _census;
	private static int _lastObservedYear;
	private static int _lastScheduledCensusYear;
	private static int _lastWrittenCensusYear;
	private static string _sessionDirectory = string.Empty;
	private static bool _sessionInitialized;
	private static bool _reportedIoFailure;
	private static bool _reportedObservationFailure;

	internal static bool Enabled => XjRuntimeSettings.StageZeroObservationEnabled;
	internal static bool HasPending => Enabled && _census != null;
	internal static string SessionDirectory => _sessionDirectory ?? string.Empty;

	internal static void OnYearChanged(int currentYear, int annualQueue, int maintenanceQueue, int annualSweep, int seedQueue)
	{
		if (!Enabled || currentYear <= 0)
		{
			return;
		}

		_lastObservedYear = Math.Max(_lastObservedYear, currentYear);
		SampleQueuePressure(annualQueue, maintenanceQueue, annualSweep, seedQueue);
		if (_census != null)
		{
			return;
		}

		bool isFirstObservation = _lastScheduledCensusYear <= 0;
		bool isCenturyBoundary = currentYear % CensusIntervalYears == 0;
		if (!isFirstObservation && !isCenturyBoundary)
		{
			return;
		}
		if (_lastScheduledCensusYear == currentYear)
		{
			return;
		}

		try
		{
			ScheduleCensus(currentYear, annualQueue, maintenanceQueue, annualSweep);
		}
		catch (Exception ex)
		{
			ReportObservationFailure(ex);
		}
	}

	internal static void SampleQueuePressure(int annualQueue, int maintenanceQueue, int annualSweep, int seedQueue)
	{
		if (!Enabled || _census == null)
		{
			return;
		}
		_census.QueueHighWater = Math.Max(_census.QueueHighWater, Math.Max(0, annualQueue));
		_census.MaintenanceQueueHighWater = Math.Max(_census.MaintenanceQueueHighWater, Math.Max(0, maintenanceQueue));
		_census.SweepHighWater = Math.Max(_census.SweepHighWater, Math.Max(0, annualSweep));
		_census.SeedHighWater = Math.Max(_census.SeedHighWater, Math.Max(0, seedQueue));
	}

	internal static void Tick()
	{
		if (!Enabled || _census == null)
		{
			return;
		}

		long started = Stopwatch.GetTimestamp();
		int processedThisTick = 0;
		while (_census.Cursor < _census.ActorIds.Count && processedThisTick < CensusActorBudget)
		{
			long actorId = _census.ActorIds[_census.Cursor++];
			processedThisTick++;
			if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive())
			{
				_census.MissingActors++;
			}
			else
			{
				try
				{
					ObserveActor(actor, actorId, _census);
				}
				catch (Exception ex)
				{
					_census.ObservationErrors++;
					ReportObservationFailure(ex);
				}
			}

			if ((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency >= CensusTimeBudgetMs)
			{
				break;
			}
		}

		if (_census.Cursor >= _census.ActorIds.Count)
		{
			CensusState completed = _census;
			_census = null;
			try
			{
				CompleteCensus(completed);
			}
			catch (Exception ex)
			{
				ReportObservationFailure(ex);
			}
			finally
			{
				XjReusableLongBufferPool.Return(completed.ActorIds);
				completed.ActorIds = null;
			}
		}
	}

	internal static void RecordAnnualPrepare(Actor actor, int lastCompletedYear, int activeYear, int latestRequestedYear)
	{
		if (!Enabled || actor?.data == null)
		{
			return;
		}

		Window.AnnualPrepareRuns++;
		int effectiveActiveYear = Math.Max(activeYear, latestRequestedYear);
		int collapsedYears = Math.Max(0, effectiveActiveYear - Math.Max(0, lastCompletedYear) - 1);
		if (collapsedYears <= 0)
		{
			return;
		}

		Window.CollapsedActors++;
		Window.CollapsedYears += collapsedYears;
		Window.MaxCollapsedYears = Math.Max(Window.MaxCollapsedYears, collapsedYears);
		try
		{
			RecordRelevantGongFaOpportunityLoss(actor, Math.Max(1, lastCompletedYear + 1), effectiveActiveYear - 1, effectiveActiveYear);
		}
		catch (Exception ex)
		{
			ReportObservationFailure(ex);
		}
	}

	internal static long BeginAnnualStage()
	{
		return Enabled ? Stopwatch.GetTimestamp() : 0L;
	}

	internal static void EndAnnualStage(XjAnnualPipelineStage stage, long started)
	{
		if (!Enabled)
		{
			return;
		}

		switch (stage)
		{
			case XjAnnualPipelineStage.Progression:
				Window.AnnualProgressionRuns++;
				break;
			case XjAnnualPipelineStage.Finalize:
				Window.AnnualFinalizeRuns++;
				break;
		}
		if (started <= 0L)
		{
			return;
		}
		long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - started);
		Window.AnnualStageTicks++;
		Window.AnnualStageTotalTicks += elapsed;
		Window.AnnualStageMaxTicks = Math.Max(Window.AnnualStageMaxTicks, elapsed);
	}

	internal static void EndAnnualMaintenanceStage(XjAnnualMaintenanceStage stage, long started)
	{
		if (!Enabled) return;
		switch (stage)
		{
			case XjAnnualMaintenanceStage.Identity:
				Window.AnnualMaintenanceIdentityRuns++;
				break;
			case XjAnnualMaintenanceStage.Ancillary:
				Window.AnnualMaintenanceAncillaryRuns++;
				break;
			case XjAnnualMaintenanceStage.Assets:
				Window.AnnualMaintenanceAssetsRuns++;
				break;
		}
		if (started <= 0L) return;
		long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - started);
		Window.AnnualStageTicks++;
		Window.AnnualStageTotalTicks += elapsed;
		Window.AnnualStageMaxTicks = Math.Max(Window.AnnualStageMaxTicks, elapsed);
	}

	internal static void RecordAnnualCoreCompletion(Actor actor, int completedYear, bool migratedLegacyFinalize)
	{
		if (!Enabled || actor?.data == null || completedYear <= 0) return;
		Window.AnnualCoreCompletions++;
		if (migratedLegacyFinalize) Window.LegacyFinalizeMigrations++;
	}

	internal static void RecordAnnualMaintenanceCompletion(Actor actor, int completedYear)
	{
		if (!Enabled || actor?.data == null || completedYear <= 0) return;
		Window.AnnualMaintenanceCompletions++;
	}

	internal static void RecordCultivationGrowth(int previousYear, int currentYear, float elapsedYears)
	{
		if (!Enabled)
		{
			return;
		}
		int elapsed = Math.Max(1, (int)Math.Round(Math.Max(1f, elapsedYears)));
		Window.GrowthRuns++;
		Window.GrowthElapsedYears += elapsed;
		Window.MaxGrowthElapsedYears = Math.Max(Window.MaxGrowthElapsedYears, elapsed);
		if (elapsed > 1 || (previousYear > 0 && currentYear - previousYear > 1))
		{
			Window.GrowthCatchUpActors++;
		}
	}

	internal static void RecordOpportunityDebtConsumed(string kind, int opportunityYear, int executionYear)
	{
		if (!Enabled || opportunityYear <= 0 || executionYear <= opportunityYear)
		{
			return;
		}

		Window.OpportunityDebtRecovered++;
		Window.OpportunityDebtYearsRecovered += executionYear - opportunityYear;
		Increment(Window.OpportunityDebtByKind, string.IsNullOrWhiteSpace(kind) ? "Unknown" : kind.Trim());
	}

	internal static void RecordGongFaAttempt(int targetGrade, bool success)
	{
		if (!Enabled)
		{
			return;
		}
		Window.GongFaAttempts++;
		string gradeKey = "grade" + Math.Max(0, targetGrade);
		Increment(Window.GongFaAttemptsByGrade, gradeKey);
		if (success)
		{
			Window.GongFaSuccesses++;
			Increment(Window.GongFaSuccessesByGrade, gradeKey);
		}
	}

	internal static void RecordBreakthroughResult(string targetRealmId, string reasonCode, bool canPromote)
	{
		if (!Enabled)
		{
			return;
		}
		Window.BreakthroughAttempts++;
		if (canPromote)
		{
			Window.BreakthroughSuccesses++;
		}
		string realmKey = string.IsNullOrWhiteSpace(targetRealmId) ? "UnknownRealm" : XjRealmHelper.NormalizeId(targetRealmId);
		Increment(Window.BreakthroughReasons, realmKey + "|" + NormalizeReason(reasonCode));
	}

	internal static void RecordJinDanAttemptStarted()
	{
		if (Enabled) Window.JinDanAttempts++;
	}

	internal static void RecordJinDanResult(string resultCode, bool success)
	{
		if (!Enabled) return;
		if (success) Window.JinDanSuccesses++;
		Increment(Window.JinDanResults, NormalizeReason(resultCode));
	}

	internal static void RecordRealmTransition(Actor actor, string previousRealmId, string nextRealmId)
	{
		if (!Enabled || actor?.data == null)
		{
			return;
		}
		string previous = XjRealmHelper.NormalizeId(previousRealmId);
		string next = XjRealmHelper.NormalizeId(nextRealmId);
		if (string.IsNullOrEmpty(next) || string.Equals(previous, next, StringComparison.Ordinal))
		{
			return;
		}
		Increment(Window.Promotions, (string.IsNullOrEmpty(previous) ? "None" : previous) + "->" + next);
		int transitionYear = XjAnnualExecutionContext.ResolveYear(actor);
		XjStageFiveEcologyGuard.RecordRealmTransition(nextRealmId, transitionYear);
	}

	internal static void RecordAptitudeResult(Actor actor, in XjAptitudeExecuteResult result)
	{
		if (!Enabled || actor?.data == null || !result.Checked)
		{
			return;
		}
		Window.AptitudeChecks++;
		if (result.Granted) Window.AptitudeGranted++;
		int age = 0;
		try { age = (int)Math.Floor(Math.Max(0f, actor.getAge())); } catch { }
		if (age == 5) Window.AptitudeAgeFive++;
		else if (age == 6) Window.AptitudeAgeSix++;
		string aptitudeKey = NormalizeReason(result.ReasonCode)
			+ (result.Granted ? "|granted:xjzz" + Math.Max(0, result.XjZz) : "|not_granted");
		Increment(Window.AptitudeResults, aptitudeKey);
		if (string.Equals(result.ReasonCode, "AgeFiveWindowMissed", StringComparison.Ordinal))
		{
			Window.AptitudeWindowMissed++;
		}
	}

	internal static void RecordCandidateRefresh(bool wasDirty)
	{
		if (!Enabled) return;
		Window.CandidateRefreshes++;
		if (wasDirty) Window.CandidateDirtyRefreshes++;
	}

	internal static void RecordCandidateRouting(string kind, bool routed)
	{
		if (!Enabled) return;
		Increment(routed ? Window.CandidateRoutes : Window.CandidateSkips, kind);
	}

	internal static void RecordAnnualStateRent(bool maintenance, bool reused)
	{
		if (!Enabled) return;
		if (maintenance)
		{
			Window.AnnualMaintenanceStateRents++;
			if (reused) Window.AnnualMaintenanceStatePoolHits++;
		}
		else
		{
			Window.AnnualActorStateRents++;
			if (reused) Window.AnnualActorStatePoolHits++;
		}
	}

	internal static void RecordIdSnapshotFill(
		string kind,
		int count,
		int capacityBefore,
		int capacityAfter,
		bool reused = true)
	{
		if (!Enabled) return;
		Window.IdSnapshotFills++;
		Window.IdSnapshotIdsCopied += Math.Max(0, count);
		if (reused) Window.IdSnapshotBufferReuses++;
		if (capacityAfter > capacityBefore) Window.IdSnapshotCapacityGrowths++;
	}

	internal static void RecordAnnualSnapshotBuild(bool full)
	{
		if (!Enabled) return;
		if (full) Window.AnnualFullSnapshots++;
		else Window.AnnualLightSnapshots++;
	}

	internal static void RecordNoFaBaoSignatureFastPath()
	{
		if (Enabled) Window.NoFaBaoSignatureFastPaths++;
	}

	internal static void RecordWorldSnapshotDuplicateSkip()
	{
		if (Enabled) Window.WorldSnapshotDuplicateSkips++;
	}

	internal static void Clear()
	{
		Window.Reset();
		if (_census != null)
		{
			XjReusableLongBufferPool.Return(_census.ActorIds);
		}
		_census = null;
		XjReusableLongBufferPool.Clear();
		_lastObservedYear = 0;
		_lastScheduledCensusYear = 0;
		_lastWrittenCensusYear = 0;
		_sessionDirectory = string.Empty;
		_sessionInitialized = false;
		_reportedIoFailure = false;
		_reportedObservationFailure = false;
		XjStageFiveEcologyGuard.Clear();
	}

	private static void ScheduleCensus(int year, int annualQueue, int maintenanceQueue, int annualSweep)
	{
		IReadOnlyList<long> ids = XjCultivatorCache.GetAllIds();
		int count = ids?.Count ?? 0;
		List<long> snapshot = XjReusableLongBufferPool.Rent(count, out bool reused, out int capacityBefore);
		for (int i = 0; i < count; i++) snapshot.Add(ids[i]);
		RecordIdSnapshotFill("Stage0Census", count, capacityBefore, snapshot.Capacity, reused);
		_census = new CensusState
		{
			Year = year,
			KnownActors = XjScheduler.GetKnownActorCount(),
			CultivatorsAtStart = count,
			ActorIds = snapshot,
			QueueAtSchedule = Math.Max(0, annualQueue),
			MaintenanceQueueAtSchedule = Math.Max(0, maintenanceQueue),
			SweepAtSchedule = Math.Max(0, annualSweep),
			QueueHighWater = Math.Max(0, annualQueue),
			MaintenanceQueueHighWater = Math.Max(0, maintenanceQueue),
			SweepHighWater = Math.Max(0, annualSweep),
			StartedTicks = Stopwatch.GetTimestamp(),
			CandidateCounts = XjCultivatorCandidateIndex.GetProgressionCounts(year)
		};
		_lastScheduledCensusYear = year;
		EnsureSessionInitialized();
	}

	private static void ObserveActor(Actor actor, long actorId, CensusState census)
	{
		census.Processed++;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string rawRealmId);
		string realmId = XjRealmHelper.NormalizeId(rawRealmId);
		int realmIndex = ResolveRealmIndex(realmId, rawRealmId);
		census.RealmCounts[realmIndex]++;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int gongFaGrade);
		gongFaGrade = Math.Clamp(gongFaGrade, 0, 6);
		census.GongFaGrades[gongFaGrade]++;

		if (XjScheduler.TryGetAnnualObservationState(actorId, out int activeYear, out int latestRequestedYear,
			out int lastCompletedYear, out _))
		{
			int effectiveYear = Math.Max(lastCompletedYear, activeYear > 0 ? activeYear - 1 : 0);
			census.AnnualLagYears.Add(Math.Max(0, census.Year - effectiveYear));
		}
		else
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAnnualLastCompletedYear, out int completedYear);
			census.AnnualLagYears.Add(Math.Max(0, census.Year - Math.Max(0, completedYear)));
		}
		if (XjScheduler.TryGetAnnualMaintenanceObservationState(actorId, out int maintenanceActiveYear,
			out _, out int maintenanceCompletedYear, out _))
		{
			int effectiveMaintenanceYear = Math.Max(maintenanceCompletedYear, maintenanceActiveYear > 0 ? maintenanceActiveYear - 1 : 0);
			census.MaintenanceLagYears.Add(Math.Max(0, census.Year - effectiveMaintenanceYear));
		}

		string blocker = ResolvePrimaryBlocker(actor, realmId, census.Year);
		Increment(census.Blockers, blocker);
	}

	private static string ResolvePrimaryBlocker(Actor actor, string realmId, int currentYear)
	{
		if (actor?.data == null)
		{
			return "ActorInvalid";
		}
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz) || xjZz <= 0)
		{
			return "NoAptitude";
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int overlayMask);
		if ((overlayMask & ((1 << 8) | (1 << 9))) != 0)
		{
			return "AptitudeInjury";
		}
		if (!XjCultivationNextRealmResolver.TryGetNextRule(realmId, out XjRealmRule targetRule))
		{
			return "NoNextRealm";
		}
		if (!targetRule.IsImplemented)
		{
			return "RuleNotImplemented";
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		if (zhenYuan < targetRule.RequiredZhenYuan)
		{
			return "InsufficientZhenYuan:" + targetRule.RealmId;
		}

		bool daoZhu = false;
		try { daoZhu = actor.hasTrait("ChuShen8"); } catch { }
		if (!daoZhu && !CanAttemptByAptitude(xjZz, targetRule.RealmId))
		{
			return "AptitudeBlocksRealm:" + targetRule.RealmId;
		}

		int age = 0;
		try { age = (int)Math.Floor(Math.Max(0f, actor.getAge())); } catch { }
		int minimumAge = string.Equals(targetRule.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal) ? 30
			: string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal) ? 90 : 0;
		if (!daoZhu && minimumAge > 0 && age < minimumAge)
		{
			return "MinimumAge:" + targetRule.RealmId;
		}

		int minimumStay = string.Equals(targetRule.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal) ? 5
			: string.Equals(targetRule.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal) ? 15
			: string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal) ? 50 : 0;
		if (!daoZhu && minimumStay > 0)
		{
			if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int enteredYear)
				|| enteredYear <= 0 || enteredYear > currentYear)
			{
				return "RealmTenureMissing:" + targetRule.RealmId;
			}
			if (currentYear - enteredYear < minimumStay)
			{
				return "MinimumRealmTenure:" + targetRule.RealmId;
			}
		}

		if (string.Equals(targetRule.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			XjCaiQiSnapshot caiQi = XjCaiQiActorAccessor.BuildSnapshot(actor);
			if (!caiQi.HasCompletedCaiQi)
			{
				return "RequiresCaiQi";
			}
		}
		if (string.Equals(targetRule.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			&& XjCaiQiActorAccessor.IsLianQiByZaQi(actor))
		{
			return "LianQiByZaQiBlocksZhuJi";
		}
		if (string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int grade);
			if (grade < 4)
			{
				return "ZiFuRequiresGrade4GongFa";
			}
		}
		if (string.Equals(targetRule.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			if (!XjXianJiAccessor.HasFive(actor)) return "JinDanRequiresFiveXianJi";
			XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
			if (!gongFa.Found || gongFa.Grade != 6) return "JinDanRequiresGrade6GongFa";
			if (!XjActorGongFaCollection.HasJinDanGongFaSet(actor)) return "JinDanRequiresFiveGongFaSet";
			XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
			if (jinDan.Found) return "AlreadyJinDan";
			if (!string.IsNullOrWhiteSpace(jinDan.FailedState)) return "JinDanFailedStatePending";
		}

		return "ReadyForAttempt:" + targetRule.RealmId;
	}

	private static bool CanAttemptByAptitude(int xjZz, string targetRealmId)
	{
		if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return xjZz >= 5 && xjZz <= 6;
		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return xjZz >= 4 && xjZz <= 6;
		if (string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return xjZz >= 3 && xjZz <= 6;
		return (string.Equals(targetRealmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
			&& xjZz >= 1 && xjZz <= 6;
	}

	private static void RecordRelevantGongFaOpportunityLoss(Actor actor, int skippedStartYear, int skippedEndYear, int activeYear)
	{
		if (XjGongFaAttemptSchedule.PreservesSkippedDueYears)
		{
			return;
		}
		if (actor?.data == null || skippedEndYear < skippedStartYear)
		{
			return;
		}
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int grade)
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz)
			|| xjZz <= 0)
		{
			return;
		}
		int targetGrade = grade + 1;
		int maximumAllowedGrade = Math.Min(
			XjGongFaAptitudeRules.GetAptitudeGradeCap(actor, xjZz),
			XjGongFaAptitudeRules.GetRealmGradeCap(actor));
		if (targetGrade > maximumAllowedGrade)
		{
			return;
		}
		if (targetGrade >= 1 && targetGrade <= 3)
		{
			Window.RelevantGongFaDueLost += skippedEndYear - skippedStartYear + 1;
			return;
		}
		XjEntityDetectionJob job;
		switch (targetGrade)
		{
			case 4:
				job = XjEntityDetectionJob.GongFaGrade4Attempt;
				break;
			case 5:
				job = XjEntityDetectionJob.GongFaGrade5Attempt;
				break;
			case 6:
				job = XjEntityDetectionJob.GongFaGrade6Attempt;
				break;
			default:
				return;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		int skippedDue = CountDueYears(job, actorId, skippedStartYear, skippedEndYear);
		if (skippedDue <= 0)
		{
			return;
		}
		Window.RelevantGongFaDueLost += skippedDue;
		if (!XjDetectionGate.IsEntityMaintenanceSlot(job, actorId, activeYear))
		{
			Window.RelevantGongFaPhaseStarvation++;
		}
	}

	private static int CountDueYears(XjEntityDetectionJob job, long actorId, int startYear, int endYear)
	{
		if (endYear < startYear) return 0;
		int interval = Math.Max(1, XjDetectionGate.ResolveEntityIntervalYears(job));
		int firstDue = -1;
		int probeEnd = Math.Min(endYear, startYear + interval - 1);
		for (int year = startYear; year <= probeEnd; year++)
		{
			if (XjDetectionGate.IsEntityMaintenanceSlot(job, actorId, year))
			{
				firstDue = year;
				break;
			}
		}
		return firstDue < 0 ? 0 : 1 + (endYear - firstDue) / interval;
	}

	private static int ResolveRealmIndex(string normalizedRealmId, string rawRealmId)
	{
		if (string.Equals(normalizedRealmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return 1;
		if (string.Equals(normalizedRealmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return 2;
		if (string.Equals(normalizedRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 3;
		if (string.Equals(normalizedRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 4;
		if (string.Equals(normalizedRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return 5;
		if (string.Equals(rawRealmId, XjRealmIds.ShenDan, StringComparison.Ordinal)) return 6;
		return 0;
	}

	private static void CompleteCensus(CensusState census)
	{
		if (census == null || census.Year <= _lastWrittenCensusYear)
		{
			return;
		}
		census.AnnualLagYears.Sort();
		int lagCount = census.AnnualLagYears.Count;
		double lagAverage = 0d;
		for (int i = 0; i < lagCount; i++) lagAverage += census.AnnualLagYears[i];
		if (lagCount > 0) lagAverage /= lagCount;
		int lagP95 = lagCount <= 0 ? 0 : census.AnnualLagYears[Math.Min(lagCount - 1, (int)Math.Floor((lagCount - 1) * 0.95d))];
		int lagMax = lagCount <= 0 ? 0 : census.AnnualLagYears[lagCount - 1];
		census.MaintenanceLagYears.Sort();
		int maintenanceLagCount = census.MaintenanceLagYears.Count;
		double maintenanceLagAverage = 0d;
		for (int i = 0; i < maintenanceLagCount; i++) maintenanceLagAverage += census.MaintenanceLagYears[i];
		if (maintenanceLagCount > 0) maintenanceLagAverage /= maintenanceLagCount;
		int maintenanceLagP95 = maintenanceLagCount <= 0 ? 0 : census.MaintenanceLagYears[Math.Min(maintenanceLagCount - 1, (int)Math.Floor((maintenanceLagCount - 1) * 0.95d))];
		int maintenanceLagMax = maintenanceLagCount <= 0 ? 0 : census.MaintenanceLagYears[maintenanceLagCount - 1];
		double stageTotalMs = Window.AnnualStageTotalTicks * 1000d / Stopwatch.Frequency;
		double stageMaxMs = Window.AnnualStageMaxTicks * 1000d / Stopwatch.Frequency;
		int completedWorldYear = Math.Max(census.Year, World.world?.map_stats?.year ?? census.Year);
		int censusSpanYears = Math.Max(0, completedWorldYear - census.Year);
		double censusDurationMs = census.StartedTicks <= 0L
			? 0d
			: Math.Max(0L, Stopwatch.GetTimestamp() - census.StartedTicks) * 1000d / Stopwatch.Frequency;

		EnsureSessionInitialized();
		try
		{
			AppendCensusCsv(census, completedWorldYear, censusSpanYears, censusDurationMs, lagAverage, lagP95, lagMax, maintenanceLagAverage, maintenanceLagP95, maintenanceLagMax, stageTotalMs, stageMaxMs);
			AppendEventCsv(census.Year, stageTotalMs, stageMaxMs);
			AppendNormalizedDetails(census);
			WriteLatestJson(census, completedWorldYear, censusSpanYears, censusDurationMs, lagAverage, lagP95, lagMax, maintenanceLagAverage, maintenanceLagP95, maintenanceLagMax, stageTotalMs, stageMaxMs);
			XjStageFiveEcologyGuard.CensusInput ecologyInput = new(
				census.Year,
				census.Processed,
				census.RealmCounts[1],
				census.RealmCounts[2],
				census.RealmCounts[3],
				census.RealmCounts[4],
				census.RealmCounts[5],
				census.RealmCounts[6],
				lagAverage,
				lagP95,
				lagMax,
				maintenanceLagAverage,
				maintenanceLagP95,
				maintenanceLagMax,
				census.ObservationErrors,
				Window.RelevantGongFaDueLost,
				Window.RelevantGongFaPhaseStarvation,
				Window.AptitudeWindowMissed,
				Window.BreakthroughAttempts,
				Window.BreakthroughSuccesses,
				Window.JinDanAttempts,
				Window.JinDanSuccesses,
				census.Blockers,
				Window.Promotions);
			XjStageFiveEcologyGuard.OnCensus(_sessionDirectory, in ecologyInput);
		}
		catch (Exception ex)
		{
			ReportIoFailure(ex);
		}
		_lastWrittenCensusYear = census.Year;
		UnityEngine.Debug.Log("[玄鉴][年度观测] 百年快照完成 year=" + census.Year
			+ " cultivators=" + census.Processed
			+ " zhuji=" + census.RealmCounts[3]
			+ " zifu=" + census.RealmCounts[4]
			+ " jindan=" + census.RealmCounts[5]
			+ " observationErrors=" + census.ObservationErrors
			+ " lagP95=" + lagP95
			+ " collapsedYears=" + Window.CollapsedYears
			+ " phaseStarvationSamples=" + Window.RelevantGongFaPhaseStarvation
			+ " completedWorldYear=" + completedWorldYear
			+ " censusDurationMs=" + censusDurationMs.ToString("F2", CultureInfo.InvariantCulture)
			+ " path=" + _sessionDirectory);
		Window.Reset();
	}

	private static void EnsureSessionInitialized()
	{
		if (_sessionInitialized)
		{
			return;
		}
		_sessionInitialized = true;
		try
		{
			string root = Path.Combine(Application.persistentDataPath, "XuanJianDiagnostics", "Stage0");
			string sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
				+ "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
			_sessionDirectory = Path.Combine(root, sessionName);
			Directory.CreateDirectory(_sessionDirectory);
			File.WriteAllText(Path.Combine(_sessionDirectory, SessionInfoFileName),
				"玄鉴仙族 年度链路与长局生态观测\n"
				+ "session=" + sessionName + "\n"
				+ "started=" + DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + "\n"
				+ "started_world_year=" + Math.Max(0, World.world?.map_stats?.year ?? 0) + "\n"
				+ "known_actors=" + XjScheduler.GetKnownActorCount() + "\n"
				+ "cultivators=" + XjCultivatorCache.Count + "\n"
				+ "census_interval_years=" + CensusIntervalYears + "\n"
				+ "semantics=stage5_long_run_ecology_guard\n",
				new UTF8Encoding(false));
		}
		catch (Exception ex)
		{
			ReportIoFailure(ex);
		}
	}

	private static void AppendCensusCsv(CensusState census, int completedWorldYear, int censusSpanYears, double censusDurationMs, double lagAverage, int lagP95, int lagMax, double maintenanceLagAverage, int maintenanceLagP95, int maintenanceLagMax, double stageTotalMs, double stageMaxMs)
	{
		if (string.IsNullOrWhiteSpace(_sessionDirectory)) return;
		string path = Path.Combine(_sessionDirectory, CsvFileName);
		bool writeHeader = !File.Exists(path);
		StringBuilder sb = new(1024);
		if (writeHeader)
		{
			sb.AppendLine("year,completed_world_year,census_span_years,census_duration_ms,known_actors,cultivators_start,processed,missing,observation_errors,realm_none,taixi,lianqi,zhuji,zifu,jindan,shendan,gongfa_0,gongfa_1,gongfa_2,gongfa_3,gongfa_4,gongfa_5,gongfa_6,lag_avg,lag_p95,lag_max,maintenance_lag_avg,maintenance_lag_p95,maintenance_lag_max,annual_queue_at_schedule,maintenance_queue_at_schedule,annual_sweep_at_schedule,annual_queue_high,maintenance_queue_high,sweep_high,seed_high,collapsed_actors,collapsed_years,max_collapsed_years,relevant_gongfa_due_lost,relevant_gongfa_phase_starvation,opportunity_debt_recovered,opportunity_debt_years_recovered,growth_runs,growth_catchup_actors,growth_elapsed_years,max_growth_elapsed_years,annual_stage_runs,annual_stage_total_ms,annual_stage_max_ms,gongfa_attempts,gongfa_successes,breakthrough_attempts,breakthrough_successes,jindan_attempts,jindan_successes,aptitude_checks,aptitude_granted,aptitude_age5,aptitude_age6,aptitude_window_missed,candidate_indexed,candidate_dirty,candidate_zhenyuan_blocked,candidate_gongfa_blocked,candidate_caiqi_blocked,candidate_chronology_blocked,candidate_breakthrough_ready,candidate_gongfa_due,candidate_caiqi_due,candidate_zifu_tracked,candidate_jindan_tracked,candidate_refreshes,candidate_dirty_refreshes,candidate_routes,candidate_skips,annual_actor_state_rents,annual_actor_state_pool_hits,annual_maintenance_state_rents,annual_maintenance_state_pool_hits,id_snapshot_fills,id_snapshot_buffer_reuses,id_snapshot_capacity_growths,id_snapshot_ids_copied,annual_light_snapshots,annual_full_snapshots,no_fabao_signature_fast_paths,world_snapshot_duplicate_skips,blockers");
		}
		sb.Append(census.Year).Append(',')
			.Append(completedWorldYear).Append(',')
			.Append(censusSpanYears).Append(',')
			.Append(censusDurationMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
			.Append(census.KnownActors).Append(',')
			.Append(census.CultivatorsAtStart).Append(',')
			.Append(census.Processed).Append(',')
			.Append(census.MissingActors).Append(',')
			.Append(census.ObservationErrors);
		for (int i = 0; i < census.RealmCounts.Length; i++) sb.Append(',').Append(census.RealmCounts[i]);
		for (int i = 0; i < census.GongFaGrades.Length; i++) sb.Append(',').Append(census.GongFaGrades[i]);
		sb.Append(',').Append(lagAverage.ToString("F3", CultureInfo.InvariantCulture))
			.Append(',').Append(lagP95)
			.Append(',').Append(lagMax)
			.Append(',').Append(maintenanceLagAverage.ToString("F3", CultureInfo.InvariantCulture))
			.Append(',').Append(maintenanceLagP95)
			.Append(',').Append(maintenanceLagMax)
			.Append(',').Append(census.QueueAtSchedule)
			.Append(',').Append(census.MaintenanceQueueAtSchedule)
			.Append(',').Append(census.SweepAtSchedule)
			.Append(',').Append(census.QueueHighWater)
			.Append(',').Append(census.MaintenanceQueueHighWater)
			.Append(',').Append(census.SweepHighWater)
			.Append(',').Append(census.SeedHighWater)
			.Append(',').Append(Window.CollapsedActors)
			.Append(',').Append(Window.CollapsedYears)
			.Append(',').Append(Window.MaxCollapsedYears)
			.Append(',').Append(Window.RelevantGongFaDueLost)
			.Append(',').Append(Window.RelevantGongFaPhaseStarvation)
			.Append(',').Append(Window.OpportunityDebtRecovered)
			.Append(',').Append(Window.OpportunityDebtYearsRecovered)
			.Append(',').Append(Window.GrowthRuns)
			.Append(',').Append(Window.GrowthCatchUpActors)
			.Append(',').Append(Window.GrowthElapsedYears)
			.Append(',').Append(Window.MaxGrowthElapsedYears)
			.Append(',').Append(Window.AnnualStageTicks)
			.Append(',').Append(stageTotalMs.ToString("F3", CultureInfo.InvariantCulture))
			.Append(',').Append(stageMaxMs.ToString("F3", CultureInfo.InvariantCulture))
			.Append(',').Append(Window.GongFaAttempts)
			.Append(',').Append(Window.GongFaSuccesses)
			.Append(',').Append(Window.BreakthroughAttempts)
			.Append(',').Append(Window.BreakthroughSuccesses)
			.Append(',').Append(Window.JinDanAttempts)
			.Append(',').Append(Window.JinDanSuccesses)
			.Append(',').Append(Window.AptitudeChecks)
			.Append(',').Append(Window.AptitudeGranted)
			.Append(',').Append(Window.AptitudeAgeFive)
			.Append(',').Append(Window.AptitudeAgeSix)
			.Append(',').Append(Window.AptitudeWindowMissed)
			.Append(',').Append(census.CandidateCounts.Indexed)
			.Append(',').Append(census.CandidateCounts.Dirty)
			.Append(',').Append(census.CandidateCounts.ZhenYuanBlocked)
			.Append(',').Append(census.CandidateCounts.GongFaBlocked)
			.Append(',').Append(census.CandidateCounts.CaiQiBlocked)
			.Append(',').Append(census.CandidateCounts.ChronologyBlocked)
			.Append(',').Append(census.CandidateCounts.BreakthroughReady)
			.Append(',').Append(census.CandidateCounts.GongFaDue)
			.Append(',').Append(census.CandidateCounts.CaiQiDue)
			.Append(',').Append(census.CandidateCounts.ZiFuTracked)
			.Append(',').Append(census.CandidateCounts.JinDanTracked)
			.Append(',').Append(Window.CandidateRefreshes)
			.Append(',').Append(Window.CandidateDirtyRefreshes)
			.Append(',').Append(CsvEscape(SerializeDictionary(Window.CandidateRoutes)))
			.Append(',').Append(CsvEscape(SerializeDictionary(Window.CandidateSkips)))
			.Append(',').Append(Window.AnnualActorStateRents)
			.Append(',').Append(Window.AnnualActorStatePoolHits)
			.Append(',').Append(Window.AnnualMaintenanceStateRents)
			.Append(',').Append(Window.AnnualMaintenanceStatePoolHits)
			.Append(',').Append(Window.IdSnapshotFills)
			.Append(',').Append(Window.IdSnapshotBufferReuses)
			.Append(',').Append(Window.IdSnapshotCapacityGrowths)
			.Append(',').Append(Window.IdSnapshotIdsCopied)
			.Append(',').Append(Window.AnnualLightSnapshots)
			.Append(',').Append(Window.AnnualFullSnapshots)
			.Append(',').Append(Window.NoFaBaoSignatureFastPaths)
			.Append(',').Append(Window.WorldSnapshotDuplicateSkips)
			.Append(',').Append(CsvEscape(SerializeDictionary(census.Blockers)))
			.AppendLine();
		File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
	}

	private static void AppendEventCsv(int year, double stageTotalMs, double stageMaxMs)
	{
		if (string.IsNullOrWhiteSpace(_sessionDirectory)) return;
		string path = Path.Combine(_sessionDirectory, EventCsvFileName);
		bool writeHeader = !File.Exists(path);
		StringBuilder sb = new(768);
		if (writeHeader)
		{
			sb.AppendLine("year,prepare_runs,progression_runs,finalize_runs,maintenance_identity_runs,maintenance_ancillary_runs,maintenance_assets_runs,core_completions,maintenance_completions,legacy_finalize_migrations,stage_total_ms,stage_max_ms,candidate_refreshes,candidate_dirty_refreshes,candidate_routes,candidate_skips,gongfa_attempts_by_grade,gongfa_successes_by_grade,breakthrough_reasons,promotions,jindan_results,aptitude_results");
		}
		sb.Append(year).Append(',')
			.Append(Window.AnnualPrepareRuns).Append(',')
			.Append(Window.AnnualProgressionRuns).Append(',')
			.Append(Window.AnnualFinalizeRuns).Append(',')
			.Append(Window.AnnualMaintenanceIdentityRuns).Append(',')
			.Append(Window.AnnualMaintenanceAncillaryRuns).Append(',')
			.Append(Window.AnnualMaintenanceAssetsRuns).Append(',')
			.Append(Window.AnnualCoreCompletions).Append(',')
			.Append(Window.AnnualMaintenanceCompletions).Append(',')
			.Append(Window.LegacyFinalizeMigrations).Append(',')
			.Append(stageTotalMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
			.Append(stageMaxMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
			.Append(Window.CandidateRefreshes).Append(',')
			.Append(Window.CandidateDirtyRefreshes).Append(',')
			.Append(CsvEscape(SerializeDictionary(Window.CandidateRoutes))).Append(',')
			.Append(CsvEscape(SerializeDictionary(Window.CandidateSkips))).Append(',')
			.Append(CsvEscape(SerializeDictionary(Window.GongFaAttemptsByGrade))).Append(',')
			.Append(CsvEscape(SerializeDictionary(Window.GongFaSuccessesByGrade))).Append(',')
			.Append(CsvEscape(SerializeDictionary(Window.BreakthroughReasons))).Append(',')
			.Append(CsvEscape(SerializeDictionary(Window.Promotions))).Append(',')
			.Append(CsvEscape(SerializeDictionary(Window.JinDanResults))).Append(',')
			.Append(CsvEscape(SerializeDictionary(Window.AptitudeResults))).AppendLine();
		File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
	}

	private static void AppendNormalizedDetails(CensusState census)
	{
		if (string.IsNullOrWhiteSpace(_sessionDirectory) || census == null) return;
		AppendDictionaryRows(Path.Combine(_sessionDirectory, BlockerCsvFileName), census.Year, "blocker", census.Blockers);
		string eventPath = Path.Combine(_sessionDirectory, EventDetailCsvFileName);
		AppendDictionaryRows(eventPath, census.Year, "gongfa_attempt", Window.GongFaAttemptsByGrade);
		AppendDictionaryRows(eventPath, census.Year, "gongfa_success", Window.GongFaSuccessesByGrade);
		AppendDictionaryRows(eventPath, census.Year, "breakthrough", Window.BreakthroughReasons);
		AppendDictionaryRows(eventPath, census.Year, "promotion", Window.Promotions);
		AppendDictionaryRows(eventPath, census.Year, "jindan", Window.JinDanResults);
		AppendDictionaryRows(eventPath, census.Year, "aptitude", Window.AptitudeResults);
		AppendDictionaryRows(eventPath, census.Year, "opportunity_debt", Window.OpportunityDebtByKind);
		AppendDictionaryRows(eventPath, census.Year, "candidate_route", Window.CandidateRoutes);
		AppendDictionaryRows(eventPath, census.Year, "candidate_skip", Window.CandidateSkips);
	}

	private static void AppendDictionaryRows<T>(string path, int year, string category, Dictionary<string, T> values)
	{
		if (string.IsNullOrWhiteSpace(path) || values == null || values.Count == 0) return;
		bool writeHeader = !File.Exists(path);
		List<string> keys = new(values.Keys);
		keys.Sort(StringComparer.Ordinal);
		StringBuilder sb = new();
		if (writeHeader) sb.AppendLine("year,category,key,count");
		for (int i = 0; i < keys.Count; i++)
		{
			string key = keys[i];
			sb.Append(year).Append(',')
				.Append(CsvEscape(category)).Append(',')
				.Append(CsvEscape(key)).Append(',')
				.Append(values[key]).AppendLine();
		}
		File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
	}

	private static void WriteLatestJson(CensusState census, int completedWorldYear, int censusSpanYears, double censusDurationMs, double lagAverage, int lagP95, int lagMax, double maintenanceLagAverage, int maintenanceLagP95, int maintenanceLagMax, double stageTotalMs, double stageMaxMs)
	{
		if (string.IsNullOrWhiteSpace(_sessionDirectory)) return;
		StringBuilder sb = new(2048);
		sb.Append("{\n")
			.Append("  \"year\": ").Append(census.Year).Append(",\n")
			.Append("  \"completedWorldYear\": ").Append(completedWorldYear).Append(",\n")
			.Append("  \"censusSpanYears\": ").Append(censusSpanYears).Append(",\n")
			.Append("  \"censusDurationMs\": ").Append(censusDurationMs.ToString("F3", CultureInfo.InvariantCulture)).Append(",\n")
			.Append("  \"knownActors\": ").Append(census.KnownActors).Append(",\n")
			.Append("  \"cultivators\": ").Append(census.Processed).Append(",\n")
			.Append("  \"observationErrors\": ").Append(census.ObservationErrors).Append(",\n")
			.Append("  \"realms\": {\"none\":").Append(census.RealmCounts[0])
			.Append(",\"taixi\":").Append(census.RealmCounts[1])
			.Append(",\"lianqi\":").Append(census.RealmCounts[2])
			.Append(",\"zhuji\":").Append(census.RealmCounts[3])
			.Append(",\"zifu\":").Append(census.RealmCounts[4])
			.Append(",\"jindan\":").Append(census.RealmCounts[5])
			.Append(",\"shendan\":").Append(census.RealmCounts[6]).Append("},\n")
			.Append("  \"annualLag\": {\"average\":").Append(lagAverage.ToString("F3", CultureInfo.InvariantCulture))
			.Append(",\"p95\":").Append(lagP95).Append(",\"max\":").Append(lagMax).Append("},\n")
			.Append("  \"maintenanceLag\": {\"average\":").Append(maintenanceLagAverage.ToString("F3", CultureInfo.InvariantCulture))
			.Append(",\"p95\":").Append(maintenanceLagP95).Append(",\"max\":").Append(maintenanceLagMax).Append("},\n")
			.Append("  \"collapsedYears\": ").Append(Window.CollapsedYears).Append(",\n")
			.Append("  \"relevantGongFaDueLost\": ").Append(Window.RelevantGongFaDueLost).Append(",\n")
			.Append("  \"relevantGongFaPhaseStarvation\": ").Append(Window.RelevantGongFaPhaseStarvation).Append(",\n")
			.Append("  \"opportunityDebtRecovered\": ").Append(Window.OpportunityDebtRecovered).Append(",\n")
			.Append("  \"opportunityDebtYearsRecovered\": ").Append(Window.OpportunityDebtYearsRecovered).Append(",\n")
			.Append("  \"opportunityDebtByKind\": ").Append(ToJsonObject(Window.OpportunityDebtByKind)).Append(",\n")
			.Append("  \"annualStageTotalMs\": ").Append(stageTotalMs.ToString("F3", CultureInfo.InvariantCulture)).Append(",\n")
			.Append("  \"annualStageMaxMs\": ").Append(stageMaxMs.ToString("F3", CultureInfo.InvariantCulture)).Append(",\n")
			.Append("  \"candidateIndex\": {\"indexed\":").Append(census.CandidateCounts.Indexed)
			.Append(",\"dirty\":").Append(census.CandidateCounts.Dirty)
			.Append(",\"zhenYuanBlocked\":").Append(census.CandidateCounts.ZhenYuanBlocked)
			.Append(",\"gongFaBlocked\":").Append(census.CandidateCounts.GongFaBlocked)
			.Append(",\"caiQiBlocked\":").Append(census.CandidateCounts.CaiQiBlocked)
			.Append(",\"chronologyBlocked\":").Append(census.CandidateCounts.ChronologyBlocked)
			.Append(",\"breakthroughReady\":").Append(census.CandidateCounts.BreakthroughReady)
			.Append(",\"gongFaDue\":").Append(census.CandidateCounts.GongFaDue)
			.Append(",\"caiQiDue\":").Append(census.CandidateCounts.CaiQiDue)
			.Append(",\"zifuTracked\":").Append(census.CandidateCounts.ZiFuTracked)
			.Append(",\"jindanTracked\":").Append(census.CandidateCounts.JinDanTracked).Append("},\n")
			.Append("  \"candidateRoutes\": ").Append(ToJsonObject(Window.CandidateRoutes)).Append(",\n")
			.Append("  \"candidateSkips\": ").Append(ToJsonObject(Window.CandidateSkips)).Append(",\n")
			.Append("  \"stage4Allocation\": {\"actorStateRents\":").Append(Window.AnnualActorStateRents)
			.Append(",\"actorStatePoolHits\":").Append(Window.AnnualActorStatePoolHits)
			.Append(",\"maintenanceStateRents\":").Append(Window.AnnualMaintenanceStateRents)
			.Append(",\"maintenanceStatePoolHits\":").Append(Window.AnnualMaintenanceStatePoolHits)
			.Append(",\"idSnapshotFills\":").Append(Window.IdSnapshotFills)
			.Append(",\"idSnapshotBufferReuses\":").Append(Window.IdSnapshotBufferReuses)
			.Append(",\"idSnapshotCapacityGrowths\":").Append(Window.IdSnapshotCapacityGrowths)
			.Append(",\"idSnapshotIdsCopied\":").Append(Window.IdSnapshotIdsCopied)
			.Append(",\"lightSnapshots\":").Append(Window.AnnualLightSnapshots)
			.Append(",\"fullSnapshots\":").Append(Window.AnnualFullSnapshots)
			.Append(",\"noFaBaoFastPaths\":").Append(Window.NoFaBaoSignatureFastPaths)
			.Append(",\"worldSnapshotDuplicateSkips\":").Append(Window.WorldSnapshotDuplicateSkips).Append("},\n")
			.Append("  \"blockers\": ").Append(ToJsonObject(census.Blockers)).Append(",\n")
			.Append("  \"gongFaAttemptsByGrade\": ").Append(ToJsonObject(Window.GongFaAttemptsByGrade)).Append(",\n")
			.Append("  \"gongFaSuccessesByGrade\": ").Append(ToJsonObject(Window.GongFaSuccessesByGrade)).Append(",\n")
			.Append("  \"breakthroughReasons\": ").Append(ToJsonObject(Window.BreakthroughReasons)).Append(",\n")
			.Append("  \"promotions\": ").Append(ToJsonObject(Window.Promotions)).Append(",\n")
			.Append("  \"jindanResults\": ").Append(ToJsonObject(Window.JinDanResults)).Append(",\n")
			.Append("  \"aptitudeResults\": ").Append(ToJsonObject(Window.AptitudeResults)).Append("\n")
			.Append("}\n");
		File.WriteAllText(Path.Combine(_sessionDirectory, LatestJsonFileName), sb.ToString(), new UTF8Encoding(false));
	}

	private static string SerializeDictionary<T>(Dictionary<string, T> values)
	{
		if (values == null || values.Count == 0) return string.Empty;
		List<string> keys = new(values.Keys);
		keys.Sort(StringComparer.Ordinal);
		StringBuilder sb = new();
		for (int i = 0; i < keys.Count; i++)
		{
			if (i > 0) sb.Append(';');
			string key = keys[i];
			sb.Append(key).Append('=').Append(values[key]);
		}
		return sb.ToString();
	}

	private static string ToJsonObject<T>(Dictionary<string, T> values)
	{
		if (values == null || values.Count == 0) return "{}";
		List<string> keys = new(values.Keys);
		keys.Sort(StringComparer.Ordinal);
		StringBuilder sb = new("{");
		for (int i = 0; i < keys.Count; i++)
		{
			if (i > 0) sb.Append(',');
			string key = keys[i];
			sb.Append('"').Append(JsonEscape(key)).Append("\":").Append(values[key]);
		}
		return sb.Append('}').ToString();
	}

	private static string NormalizeReason(string reason)
	{
		return string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason.Trim();
	}

	private static void Increment(Dictionary<string, long> values, string key)
	{
		key = NormalizeReason(key);
		values.TryGetValue(key, out long count);
		values[key] = count + 1L;
	}

	private static void Increment(Dictionary<string, int> values, string key)
	{
		key = NormalizeReason(key);
		values.TryGetValue(key, out int count);
		values[key] = count + 1;
	}

	private static string CsvEscape(string value)
	{
		value ??= string.Empty;
		return "\"" + value.Replace("\"", "\"\"") + "\"";
	}

	private static string JsonEscape(string value)
	{
		return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
	}

	private static void ReportObservationFailure(Exception ex)
	{
		if (_reportedObservationFailure) return;
		_reportedObservationFailure = true;
		UnityEngine.Debug.LogWarning("[玄鉴][年度观测] 读取观测状态失败，已隔离并继续游戏："
			+ ex.GetType().Name + ": " + ex.Message);
	}

	private static void ReportIoFailure(Exception ex)
	{
		if (_reportedIoFailure) return;
		_reportedIoFailure = true;
		UnityEngine.Debug.LogWarning("[玄鉴][年度观测] 导出失败：" + ex.GetType().Name + ": " + ex.Message);
	}
}
