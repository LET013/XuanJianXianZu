using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Five-year sect common duties. This is deliberately sect-seat based rather
/// than actor based: tasks represent families organizing tenants and juniors
/// to plant herbs, tend fields and prepare useful materials.
/// </summary>
internal static class XjSectTaskSystem
{
	private const int TaskIntervalYears = 5;
	private static readonly List<XjSectArchiveRecord> DueSects = new List<XjSectArchiveRecord>();
	private static readonly Dictionary<long, List<XjSectFamilySeatArchiveRecord>> SeatsBySect = new Dictionary<long, List<XjSectFamilySeatArchiveRecord>>();
	private static readonly Dictionary<long, Dictionary<long, int>> GovernedCityCountsBySectFamily = new Dictionary<long, Dictionary<long, int>>();
	private static readonly Stack<List<XjSectFamilySeatArchiveRecord>> SeatListPool = new Stack<List<XjSectFamilySeatArchiveRecord>>();
	private static readonly Stack<Dictionary<long, int>> GovernedCityCountPool = new Stack<Dictionary<long, int>>();
	private static readonly List<string> SummaryParts = new List<string>(4);
	private static readonly Dictionary<long, int> EmptyCityCounts = new Dictionary<long, int>();
	private static int nextDueScanYear;

	private enum AnnualPhase : byte
	{
		None = 0,
		ScanSects = 1,
		BuildSeats = 2,
		BuildGovernance = 3,
		ProcessDueSects = 4
	}

	private static IReadOnlyList<XjSectArchiveRecord> PendingSects = Array.Empty<XjSectArchiveRecord>();
	private static IReadOnlyList<XjSectFamilySeatArchiveRecord> PendingSeatRecords = Array.Empty<XjSectFamilySeatArchiveRecord>();
	private static IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> PendingGovernanceRecords = Array.Empty<XjCityFamilyGovernanceArchiveRecord>();
	private static AnnualPhase _pendingPhase;
	private static int _pendingYear;
	private static int _pendingIndex;
	private static int _pendingEarliestNextYear;

	internal static bool HasPendingForYear(int currentYear)
	{
		return currentYear > 0 && _pendingYear == currentYear && _pendingPhase != AnnualPhase.None;
	}

	internal static bool BeginYear(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0 || nextDueScanYear > currentYear) return false;
		if (HasPendingForYear(currentYear)) return true;
		ClearPendingAnnualWork();
		DueSects.Clear();
		PendingSects = XjSectRepository.ReadAllSects();
		if (PendingSects.Count == 0)
		{
			nextDueScanYear = currentYear + TaskIntervalYears;
			ClearPendingAnnualWork();
			return false;
		}

		_pendingYear = currentYear;
		_pendingPhase = AnnualPhase.ScanSects;
		_pendingIndex = 0;
		_pendingEarliestNextYear = int.MaxValue;
		return true;
	}

	/// <summary>
	/// The five-year sect duty job is split into resumable phases: determine due
	/// sects, index family seats, index governed cities, then commit one sect duty
	/// at a time. Snapshot materialization is still atomic, but all record traversal
	/// and domain mutation yields under the shared frame governor.
	/// </summary>
	internal static bool TickPending(int itemBudget, double timeBudgetMs)
	{
		if (_pendingPhase == AnnualPhase.None || _pendingYear <= 0) return true;
		XjCooperativeBudget budget = new XjCooperativeBudget(
			Math.Max(1, itemBudget),
			timeBudgetMs,
			XjRuntimeFramePriority.Background);

		while (_pendingPhase != AnnualPhase.None && budget.TryTake())
		{
			switch (_pendingPhase)
			{
				case AnnualPhase.ScanSects:
					ProcessSectDueScanItem();
					break;
				case AnnualPhase.BuildSeats:
					ProcessSeatIndexItem();
					break;
				case AnnualPhase.BuildGovernance:
					ProcessGovernanceIndexItem();
					break;
				case AnnualPhase.ProcessDueSects:
					ProcessDueSectItem();
					break;
			}
		}

		return _pendingPhase == AnnualPhase.None;
	}

	internal static void TickYear(int currentYear)
	{
		if (!BeginYear(currentYear)) return;
		while (_pendingPhase != AnnualPhase.None)
		{
			switch (_pendingPhase)
			{
				case AnnualPhase.ScanSects: ProcessSectDueScanItem(); break;
				case AnnualPhase.BuildSeats: ProcessSeatIndexItem(); break;
				case AnnualPhase.BuildGovernance: ProcessGovernanceIndexItem(); break;
				case AnnualPhase.ProcessDueSects: ProcessDueSectItem(); break;
			}
		}
	}

	private static void ProcessSectDueScanItem()
	{
		if (_pendingIndex >= PendingSects.Count)
		{
			if (DueSects.Count == 0)
			{
				nextDueScanYear = _pendingEarliestNextYear == int.MaxValue
					? _pendingYear + TaskIntervalYears
					: _pendingEarliestNextYear;
				ClearPendingAnnualWork();
				return;
			}

			PendingSeatRecords = XjSectRepository.ReadAllFamilySeats();
			_pendingPhase = AnnualPhase.BuildSeats;
			_pendingIndex = 0;
			return;
		}

		XjSectArchiveRecord sect = PendingSects[_pendingIndex++];
		if (sect == null || sect.SectId <= 0L
			|| string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
			|| string.Equals(sect.Status, XjSectStatus.LandlessSect, StringComparison.Ordinal))
		{
			return;
		}

		int baseline = sect.LastTaskYear > 0 ? sect.LastTaskYear : sect.FoundingYear;
		if (baseline <= 0) baseline = _pendingYear;
		int dueYear = baseline + TaskIntervalYears;
		if (_pendingYear < dueYear)
		{
			_pendingEarliestNextYear = Math.Min(_pendingEarliestNextYear, dueYear);
			return;
		}
		DueSects.Add(sect);
	}

	private static void ProcessSeatIndexItem()
	{
		if (_pendingIndex >= PendingSeatRecords.Count)
		{
			PendingGovernanceRecords = XjSectRepository.ReadAllGovernance();
			_pendingPhase = AnnualPhase.BuildGovernance;
			_pendingIndex = 0;
			return;
		}

		XjSectFamilySeatArchiveRecord seat = PendingSeatRecords[_pendingIndex++];
		if (seat == null || seat.SectId <= 0L || seat.FamilyId <= 0L
			|| string.Equals(seat.State, XjSectFamilySeatState.Suspended, StringComparison.Ordinal)) return;
		if (!SeatsBySect.TryGetValue(seat.SectId, out List<XjSectFamilySeatArchiveRecord> list))
		{
			list = RentSeatList();
			SeatsBySect[seat.SectId] = list;
		}
		list.Add(seat);
	}

	private static void ProcessGovernanceIndexItem()
	{
		if (_pendingIndex >= PendingGovernanceRecords.Count)
		{
			_pendingPhase = AnnualPhase.ProcessDueSects;
			_pendingIndex = 0;
			return;
		}

		XjCityFamilyGovernanceArchiveRecord record = PendingGovernanceRecords[_pendingIndex++];
		if (record == null || record.SectId <= 0L || record.GoverningFamilyId <= 0L) return;
		if (!GovernedCityCountsBySectFamily.TryGetValue(record.SectId, out Dictionary<long, int> counts))
		{
			counts = RentGovernedCityCounts();
			GovernedCityCountsBySectFamily[record.SectId] = counts;
		}
		counts.TryGetValue(record.GoverningFamilyId, out int count);
		counts[record.GoverningFamilyId] = count + 1;
	}

	private static void ProcessDueSectItem()
	{
		if (_pendingIndex >= DueSects.Count)
		{
			int next = _pendingEarliestNextYear == int.MaxValue
				? _pendingYear + TaskIntervalYears
				: Math.Min(_pendingYear + TaskIntervalYears, _pendingEarliestNextYear);
			nextDueScanYear = next;
			ClearPendingAnnualWork();
			return;
		}

		XjSectArchiveRecord sect = DueSects[_pendingIndex++];
		if (sect == null || !SeatsBySect.TryGetValue(sect.SectId, out List<XjSectFamilySeatArchiveRecord> seats) || seats.Count == 0) return;
		TryResolveSectTask(sect, seats, GovernedCityCountsBySectFamily, _pendingYear);
	}

	private static void ResetSeatAndGovernanceMaps()
	{
		foreach (List<XjSectFamilySeatArchiveRecord> seats in SeatsBySect.Values) ReleaseSeatList(seats);
		SeatsBySect.Clear();
		foreach (Dictionary<long, int> counts in GovernedCityCountsBySectFamily.Values) ReleaseGovernedCityCounts(counts);
		GovernedCityCountsBySectFamily.Clear();
	}

	internal static void ClearPendingAnnualWork()
	{
		ResetSeatAndGovernanceMaps();
		PendingSects = Array.Empty<XjSectArchiveRecord>();
		PendingSeatRecords = Array.Empty<XjSectFamilySeatArchiveRecord>();
		PendingGovernanceRecords = Array.Empty<XjCityFamilyGovernanceArchiveRecord>();
		DueSects.Clear();
		_pendingPhase = AnnualPhase.None;
		_pendingYear = 0;
		_pendingIndex = 0;
		_pendingEarliestNextYear = int.MaxValue;
	}

	internal static void Clear()
	{
		ClearPendingAnnualWork();
		nextDueScanYear = 0;
		SeatListPool.Clear();
		GovernedCityCountPool.Clear();
		SummaryParts.Clear();
	}

	private static List<XjSectFamilySeatArchiveRecord> RentSeatList()
	{
		return SeatListPool.Count > 0
			? SeatListPool.Pop()
			: new List<XjSectFamilySeatArchiveRecord>();
	}

	private static void ReleaseSeatList(List<XjSectFamilySeatArchiveRecord> seats)
	{
		if (seats == null) return;
		seats.Clear();
		if (seats.Capacity <= 256 && SeatListPool.Count < 64) SeatListPool.Push(seats);
	}

	private static Dictionary<long, int> RentGovernedCityCounts()
	{
		return GovernedCityCountPool.Count > 0
			? GovernedCityCountPool.Pop()
			: new Dictionary<long, int>();
	}

	private static void ReleaseGovernedCityCounts(Dictionary<long, int> counts)
	{
		if (counts == null) return;
		int entryCount = counts.Count;
		counts.Clear();
		if (entryCount <= 128 && GovernedCityCountPool.Count < 64) GovernedCityCountPool.Push(counts);
	}

	private static void TryResolveSectTask(
		XjSectArchiveRecord sect,
		List<XjSectFamilySeatArchiveRecord> seats,
		Dictionary<long, Dictionary<long, int>> governedCityCountBySectFamily,
		int currentYear)
	{
		XjAlchemyOwnerKey alchemyOwner = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.ZongMen, sect.SectId);
		XjCraftOwnerKey craftOwner = new XjCraftOwnerKey(XjCraftOwnerScope.Sect, sect.SectId);
		governedCityCountBySectFamily.TryGetValue(sect.SectId, out Dictionary<long, int> cityCounts);
		cityCounts ??= EmptyCityCounts;

		int taskKind = XjDeterministicHash.PositiveIndex(sect.SectId + currentYear, "sect.five_year_task.kind", 3);
		XjSectTaskCapabilityRouter.Route route = XjSectTaskCapabilityRouter.Resolve(
			sect,
			seats,
			cityCounts,
			taskKind,
			currentYear);

		int totalLowHerbs = 0;
		int totalHighHerbs = 0;
		int totalGoodMaterials = 0;
		float totalContribution = 0f;
		for (int i = 0; i < seats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord seat = seats[i];
			if (seat == null || seat.FamilyId <= 0L) continue;
			cityCounts.TryGetValue(seat.FamilyId, out int cityCount);
			bool isLead = seat.FamilyId == route.LeadFamilyId;
			float leadMultiplier = isLead ? 1.25f : 1f;
			float seatWeight = Math.Max(1f, 1f + cityCount * 0.8f + Math.Max(0f, seat.VoiceScore) / 40f) * leadMultiplier;
			int lowHerbs = 0;
			int highHerbs = 0;
			int goodMaterials = 0;
			string dutyPrefix = isLead
				? "主理宗门共务"
				: IsResourceCommandSeat(seat) ? "调度宗门共务" : "宗门共务";
			string responsibility;
			if (taskKind == 0)
			{
				lowHerbs = 3 + cityCount * 2 + (isLead ? 2 : 0);
				highHerbs = seat.VoiceScore >= 35f || cityCount >= 2 || isLead && route.CapabilityScore >= 1000 ? 1 : 0;
				if (isLead && route.CapabilityScore >= 2000 || cityCount >= 4) highHerbs++;
				responsibility = dutyPrefix + "-种植药材";
			}
			else if (taskKind == 1)
			{
				goodMaterials = 3 + cityCount * 2 + (isLead ? 2 : 0);
				responsibility = dutyPrefix + "-培育良材";
			}
			else
			{
				lowHerbs = 2 + cityCount;
				goodMaterials = 2 + cityCount * 2 + (isLead ? 1 : 0);
				highHerbs = seat.VoiceScore >= 55f || isLead && route.CapabilityScore >= 1000 ? 1 : 0;
				responsibility = dutyPrefix + "-整饬灵田";
			}

			if (lowHerbs > 0 || highHerbs > 0)
			{
				XjAlchemyInventoryRegistry.TryAddAnnualMaterials(alchemyOwner, lowHerbs, highHerbs, currentYear);
				totalLowHerbs += lowHerbs;
				totalHighHerbs += highHerbs;
			}
			if (goodMaterials > 0)
			{
				XjCraftDomainRegistry.TryAddResource(craftOwner, XjCraftCollaborationSystem.FormationFlagLow, goodMaterials, currentYear);
				XjCraftDomainRegistry.TryAddResource(craftOwner, XjCraftCollaborationSystem.SpiritMaterial, Math.Max(1, goodMaterials * 2 / 3), currentYear);
				if (cityCount >= 2 || seat.VoiceScore >= 45f || isLead && route.CapabilityScore >= 2000)
				{
					int plateAmount = cityCount >= 3 || isLead && route.CapabilityScore >= 2000 ? 2 : 1;
					XjCraftDomainRegistry.TryAddResource(craftOwner, XjCraftCollaborationSystem.FormationPlateLow, plateAmount, currentYear);
				}
				totalGoodMaterials += goodMaterials;
			}

			float contribution = Math.Max(1f, seatWeight * 3f + lowHerbs + highHerbs * 3f + goodMaterials);
			totalContribution += contribution;
			XjSectRepository.TryApplyFamilyContribution(
				sect.SectId,
				seat.FamilyId,
				contribution,
				goodMaterials * 0.6f,
				currentYear,
				responsibility);
		}

		string regionalSummary = XjRegionalResourceSystem.ApplySectTerritoryYield(sect, currentYear, alchemyOwner, craftOwner);
		string summary = BuildSummary(totalLowHerbs, totalHighHerbs, totalGoodMaterials, route);
		if (!string.IsNullOrWhiteSpace(regionalSummary)) summary += " " + regionalSummary + "。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Sect,
			(sect.Name ?? "某宗") + "宗门共务",
			(sect.Name ?? "某宗") + "五年一行宗门共务，" + summary,
			totalContribution >= 45f ? 3 : 2,
			sectId: sect.SectId,
			cityId: sect.CapitalCityId,
			year: currentYear,
			eventType: "SectCommonDuty",
			visibilityFlags: (int)(XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
			mirrorToWorldLog: false);
		XjSectRepository.TryMarkSectTaskCompleted(sect.SectId, currentYear, summary);
	}

	private static bool IsResourceCommandSeat(XjSectFamilySeatArchiveRecord seat)
	{
		if (seat == null) return false;
		return seat.StrengthScore >= 900f
			|| string.Equals(seat.VoiceTier, XjSectVoiceTier.Dominant, StringComparison.Ordinal)
			|| string.Equals(seat.VoiceTier, XjSectVoiceTier.First, StringComparison.Ordinal)
			|| string.Equals(seat.VoiceTier, XjSectVoiceTier.Major, StringComparison.Ordinal);
	}

	private static string BuildSummary(
		int lowHerbs,
		int highHerbs,
		int goodMaterials,
		XjSectTaskCapabilityRouter.Route route)
	{
		SummaryParts.Clear();
		// 下品药材采集不再进入共务通知，避免信息噪声。
		if (highHerbs > 0) SummaryParts.Add("上品药材" + highHerbs.ToString(CultureInfo.InvariantCulture) + "份");
		if (goodMaterials > 0) SummaryParts.Add("良材" + goodMaterials.ToString(CultureInfo.InvariantCulture) + "批");
		if (!string.IsNullOrWhiteSpace(route.LeadFamilyName)) SummaryParts.Add(route.LeadFamilyName + "主理此务");
		if (route.WasReassigned && !string.IsNullOrWhiteSpace(route.PreferredFamilyName))
		{
			SummaryParts.Add("原拟由" + route.PreferredFamilyName + "承办，因艺业不合转派");
		}
		return SummaryParts.Count == 0 ? "诸家巡田修圃，暂无额外收成。" : string.Join("，", SummaryParts) + "。";
	}
}
