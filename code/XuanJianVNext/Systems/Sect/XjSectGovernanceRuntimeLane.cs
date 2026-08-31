using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
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

		// 0.9.8.8：治理是“当前态”维护，但不能把“合并到最新年份”误写成
		// “每逢新年清空尚未消费的宗门/城市身份”。40x 下一个世界年可能比
		// 五个维护相位更快到来，旧实现会反复把相位重置到 Territory，导致
		// BuildProjection 永远到不了；即使偶尔到达，上一轮 PendingSectIds 也会
		// 被下一年直接 Clear。结果就是后排宗门长期从未执行 Prosperity.Evaluate，
		// 出现几百年仍保留在宗谱里的“修士0”空门。
		//
		// 正确做法：年份只向前折叠；待处理身份与正在推进的相位保留。新年度
		// 只把需要再次执行的维护标记 OR 进当前车道，HashSet 负责去重。
		scheduledYear = currentYear;
		pendingFounderScanCount = 0; // 创宗候选由突破/建国/灭宗事件队列提供，不再年度全修士扫描。

		bool newTerritoryDue = currentYear % 5 == 0
			&& XjDetectionGate.TryBeginAnnualJob(XjDetectionJob.SectTerritoryReconcile, currentYear);
		bool newRebellionDue = XjDetectionGate.TryBeginAnnualJob(XjDetectionJob.SectRebellionSweep, currentYear);
		territoryReconcileDue |= newTerritoryDue;
		rebellionSweepDue |= newRebellionDue;

		if (annualMaintenancePhase == AnnualMaintenancePhase.None
			&& PendingCityIds.Count == 0 && PendingSectIds.Count == 0)
		{
			annualMaintenancePhase = territoryReconcileDue
				? AnnualMaintenancePhase.Territory
				: AnnualMaintenancePhase.FounderScan;
		}
	}

	internal static void Tick(int budget)
	{
		if (budget <= 0 || !XjWorldSchemaGuard.GameplayEnabled) return;

		// 已构建的城市/宗门身份必须优先真正消费完。否则极高倍速下每个新年都
		// 可以再次抢占车道，空门就永远轮不到兴衰结算。积累的新年度维护标记
		// 等当前实体队列清空后再开始，不丢年份，也不丢身份。
		if (annualMaintenancePhase == AnnualMaintenancePhase.None
			&& PendingCityIds.Count == 0 && PendingSectIds.Count == 0
			&& (territoryReconcileDue || rebellionSweepDue))
		{
			annualMaintenancePhase = territoryReconcileDue
				? AnnualMaintenancePhase.Territory
				: AnnualMaintenancePhase.FounderScan;
		}

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
			if (XjSectProsperitySystem.Evaluate(sectId, scheduledYear))
			{
				TrySoftSplitSect(sectId, scheduledYear);
			}
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
					territoryReconcileDue = false;
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
					rebellionSweepDue = false;
				}
				annualMaintenancePhase = AnnualMaintenancePhase.BuildProjection;
				return;
			case AnnualMaintenancePhase.BuildProjection:
				BuildAnnualProjectionQueues();
				// 先把本轮已经排入的宗门逐个消费；其间新年产生的 territory/rebellion
				// 标记继续保留，实体队列清空后 Tick 会自然开启下一轮当前态维护。
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
		foreach (long cityId in XjSectCultivatorCityIndex.GetCandidateCityIds())
		{
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






