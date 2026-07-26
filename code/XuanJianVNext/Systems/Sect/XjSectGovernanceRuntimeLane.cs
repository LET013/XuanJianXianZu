using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Bounded governance lane. It evaluates only existing sect cities and seats;
/// it never scans ordinary population outside the already-maintained city and
/// family ledgers. City replacement requires ten consecutive qualifying years.
/// </summary>
internal static partial class XjSectGovernanceRuntimeLane
{
	private enum AnnualMaintenancePhase : byte
	{
		None = 0,
		Territory = 1,
		FounderScan = 2,
		Transition = 3,
		Rebellion = 4,
		BuildProjection = 5
	}

	private const float ChallengeMultiplier = 1.35f;
	private const int RequiredConsecutiveYears = 10;
	private const int SoftSplitMinimumParentCities = 5;
	private const int SoftSplitMountainGateCityCount = 1;
	private const float SoftSplitDistanceThresholdSquared = 2500f;
	private static readonly BindingFlags ReflectionFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
	private static readonly Queue<long> PendingCityIds = new Queue<long>();
	private static readonly HashSet<long> PendingCitySet = new HashSet<long>();
	private static readonly Queue<long> PendingSectIds = new Queue<long>();
	private static readonly HashSet<long> PendingSectSet = new HashSet<long>();
	private static readonly Dictionary<long, XjFamilyLedgerAggregate> CachedFamilyById = new Dictionary<long, XjFamilyLedgerAggregate>();
	private static readonly Dictionary<long, List<XjSectFamilySeatArchiveRecord>> CachedSeatsBySect = new Dictionary<long, List<XjSectFamilySeatArchiveRecord>>();
	private static readonly Dictionary<long, Dictionary<long, int>> CachedCityCountsBySect = new Dictionary<long, Dictionary<long, int>>();
	private static readonly Dictionary<long, XjCenturyFamilyStageStateRecord> CachedFamilyStageById = new Dictionary<long, XjCenturyFamilyStageStateRecord>();
	private static readonly Dictionary<long, XjSectMandateHostilityCandidate> CachedBloodHostilityBySect = new Dictionary<long, XjSectMandateHostilityCandidate>();
	// 治理投影按年构建；内层集合复用，避免宗门数量增加后每年制造整批 GC。
	private static readonly Stack<List<XjSectFamilySeatArchiveRecord>> ProjectionSeatListPool = new Stack<List<XjSectFamilySeatArchiveRecord>>();
	private static readonly Stack<Dictionary<long, int>> ProjectionCityCountPool = new Stack<Dictionary<long, int>>();
	private static readonly Dictionary<long, float> ControlScoreScratch = new Dictionary<long, float>();
	private static readonly HashSet<long> SeatFamilyScratch = new HashSet<long>();
	private static readonly Dictionary<long, float> RawStrengthScratch = new Dictionary<long, float>();
	private static readonly Dictionary<long, int> RealmRankScratch = new Dictionary<long, int>();
	private static readonly Dictionary<long, float> VoiceScratch = new Dictionary<long, float>();
	private static readonly Dictionary<long, int> EmptyCityCounts = new Dictionary<long, int>();
	private static int scheduledYear = -1;
	private static int pendingFounderScanCount;
	private static bool territoryReconcileDue;
	private static bool rebellionSweepDue;
	private static AnnualMaintenancePhase annualMaintenancePhase;

	internal static bool HasPending => annualMaintenancePhase != AnnualMaintenancePhase.None
		|| PendingCityIds.Count > 0
		|| PendingSectIds.Count > 0;

	internal static void Schedule(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0 || currentYear <= scheduledYear) return;
		scheduledYear = currentYear;

		// 高倍速下新年可能在上一轮维护尚未完成前到来。治理维护是当前态
		// 对账，直接合并到最新年份，避免旧年份队列继续制造追赶债务。
		PendingCityIds.Clear();
		PendingCitySet.Clear();
		PendingSectIds.Clear();
		PendingSectSet.Clear();
		ClearProjectionCache();
		pendingFounderScanCount = Math.Min(256, XjCultivatorCache.Count);
		territoryReconcileDue = XjDetectionGate.TryBeginAnnualJob(XjDetectionJob.SectTerritoryReconcile, currentYear);
		rebellionSweepDue = XjDetectionGate.TryBeginAnnualJob(XjDetectionJob.SectRebellionSweep, currentYear);
		annualMaintenancePhase = territoryReconcileDue
			? AnnualMaintenancePhase.Territory
			: AnnualMaintenancePhase.FounderScan;
	}

	internal static void Tick(int budget)
	{
		if (budget <= 0 || !XjWorldSchemaGuard.GameplayEnabled) return;
		if (annualMaintenancePhase != AnnualMaintenancePhase.None)
		{
			TickAnnualMaintenance(Math.Max(1, budget));
			return;
		}

		int processed = 0;
		while (processed < budget && PendingCityIds.Count > 0)
		{
			long cityId = PendingCityIds.Dequeue();
			PendingCitySet.Remove(cityId);
			EvaluateCity(cityId, scheduledYear);
			processed++;
		}
		while (processed < budget && PendingSectIds.Count > 0)
		{
			long sectId = PendingSectIds.Dequeue();
			PendingSectSet.Remove(sectId);
			PublishSectSeats(sectId, scheduledYear);
			ReconcileSovereign(sectId, scheduledYear);
			ResolveHousePolitics(sectId, scheduledYear);
			ReconcileSectTreasury(sectId, scheduledYear);
			XjSectRepository.ReconcilePeaks(sectId, scheduledYear);
			TrySoftSplitSect(sectId, scheduledYear);
			processed++;
		}
	}

	internal static void Clear()
	{
		PendingCityIds.Clear();
		PendingCitySet.Clear();
		PendingSectIds.Clear();
		PendingSectSet.Clear();
		ClearProjectionCache();
		ControlScoreScratch.Clear();
		SeatFamilyScratch.Clear();
		RawStrengthScratch.Clear();
		RealmRankScratch.Clear();
		VoiceScratch.Clear();
		scheduledYear = -1;
		pendingFounderScanCount = 0;
		territoryReconcileDue = false;
		rebellionSweepDue = false;
		annualMaintenancePhase = AnnualMaintenancePhase.None;
	}

	private static void ClearProjectionCache()
	{
		foreach (List<XjSectFamilySeatArchiveRecord> seats in CachedSeatsBySect.Values) ReleaseProjectionSeatList(seats);
		foreach (Dictionary<long, int> counts in CachedCityCountsBySect.Values) ReleaseProjectionCityCounts(counts);
		CachedFamilyById.Clear();
		CachedSeatsBySect.Clear();
		CachedCityCountsBySect.Clear();
		CachedFamilyStageById.Clear();
		CachedBloodHostilityBySect.Clear();
	}

	private static List<XjSectFamilySeatArchiveRecord> RentProjectionSeatList()
	{
		return ProjectionSeatListPool.Count > 0
			? ProjectionSeatListPool.Pop()
			: new List<XjSectFamilySeatArchiveRecord>();
	}

	private static void ReleaseProjectionSeatList(List<XjSectFamilySeatArchiveRecord> seats)
	{
		if (seats == null) return;
		seats.Clear();
		if (seats.Capacity <= 256 && ProjectionSeatListPool.Count < 64) ProjectionSeatListPool.Push(seats);
	}

	private static Dictionary<long, int> RentProjectionCityCounts()
	{
		return ProjectionCityCountPool.Count > 0
			? ProjectionCityCountPool.Pop()
			: new Dictionary<long, int>();
	}

	private static void ReleaseProjectionCityCounts(Dictionary<long, int> counts)
	{
		if (counts == null) return;
		int entryCount = counts.Count;
		counts.Clear();
		if (entryCount <= 128 && ProjectionCityCountPool.Count < 64) ProjectionCityCountPool.Push(counts);
	}

	private static void TickAnnualMaintenance(int budget)
	{
		switch (annualMaintenancePhase)
		{
			case AnnualMaintenancePhase.Territory:
				if (territoryReconcileDue)
				{
					XjSectRepository.ReconcileRuntimeTerritory(scheduledYear);
				}
				annualMaintenancePhase = AnnualMaintenancePhase.FounderScan;
				return;
			case AnnualMaintenancePhase.FounderScan:
				if (pendingFounderScanCount > 0)
				{
					int scanBudget = Math.Min(pendingFounderScanCount, Math.Max(32, budget * 16));
					int scanned = XjNationSectTransitionSystem.ScanMissingSectFounders(scheduledYear, scanBudget);
					pendingFounderScanCount = Math.Max(0, pendingFounderScanCount - Math.Max(1, scanned));
					if (pendingFounderScanCount > 0) return;
				}
				annualMaintenancePhase = AnnualMaintenancePhase.Transition;
				return;
			case AnnualMaintenancePhase.Transition:
				XjNationSectTransitionSystem.Tick(Math.Max(1, Math.Min(8, budget)));
				annualMaintenancePhase = AnnualMaintenancePhase.Rebellion;
				return;
			case AnnualMaintenancePhase.Rebellion:
				if (rebellionSweepDue)
				{
					XjNationSectRebellionGuard.Sweep(scheduledYear, 48);
				}
				annualMaintenancePhase = AnnualMaintenancePhase.BuildProjection;
				return;
			case AnnualMaintenancePhase.BuildProjection:
				BuildAnnualProjectionQueues();
				annualMaintenancePhase = AnnualMaintenancePhase.None;
				return;
			default:
				annualMaintenancePhase = AnnualMaintenancePhase.None;
				return;
		}
	}

	private static void BuildAnnualProjectionQueues()
	{
		IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance = BuildProjectionCache();
		for (int i = 0; i < governance.Count; i++)
		{
			long cityId = governance[i].CityId;
			if (cityId > 0L && PendingCitySet.Add(cityId)) PendingCityIds.Enqueue(cityId);
		}
		foreach (City city in XjZongMenCultivatorCityIndex.GetCandidateCities())
		{
			long cityId = city?.data?.id ?? 0L;
			if (cityId > 0L && PendingCitySet.Add(cityId)) PendingCityIds.Enqueue(cityId);
		}
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		for (int i = 0; i < sects.Count; i++)
		{
			long sectId = sects[i].SectId;
			if (sectId > 0L && PendingSectSet.Add(sectId)) PendingSectIds.Enqueue(sectId);
		}
	}
}







