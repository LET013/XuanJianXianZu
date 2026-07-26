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

	internal static void TickYear(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0) return;
		if (nextDueScanYear > currentYear) return;
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		if (sects.Count == 0)
		{
			nextDueScanYear = currentYear + TaskIntervalYears;
			return;
		}

		DueSects.Clear();
		int earliestNextYear = int.MaxValue;
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord sect = sects[i];
			if (sect == null || sect.SectId <= 0L
				|| string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
				|| string.Equals(sect.Status, XjSectStatus.LandlessSect, StringComparison.Ordinal))
			{
				continue;
			}

			int baseline = sect.LastTaskYear > 0 ? sect.LastTaskYear : sect.FoundingYear;
			if (baseline <= 0) baseline = currentYear;
			int dueYear = baseline + TaskIntervalYears;
			if (currentYear < dueYear)
			{
				earliestNextYear = Math.Min(earliestNextYear, dueYear);
				continue;
			}
			DueSects.Add(sect);
		}
		if (DueSects.Count == 0)
		{
			nextDueScanYear = earliestNextYear == int.MaxValue ? currentYear + TaskIntervalYears : earliestNextYear;
			return;
		}

		BuildSeatsBySect(SeatsBySect);
		BuildGovernedCityCounts(GovernedCityCountsBySectFamily);
		for (int i = 0; i < DueSects.Count; i++)
		{
			XjSectArchiveRecord sect = DueSects[i];
			if (!SeatsBySect.TryGetValue(sect.SectId, out List<XjSectFamilySeatArchiveRecord> seats) || seats.Count == 0) continue;
			TryResolveSectTask(sect, seats, GovernedCityCountsBySectFamily, currentYear);
		}
		nextDueScanYear = Math.Min(currentYear + TaskIntervalYears, earliestNextYear);
		if (nextDueScanYear == int.MaxValue)
		{
			nextDueScanYear = currentYear + TaskIntervalYears;
		}
	}

	private static void BuildSeatsBySect(Dictionary<long, List<XjSectFamilySeatArchiveRecord>> result)
	{
		foreach (List<XjSectFamilySeatArchiveRecord> seats in result.Values) ReleaseSeatList(seats);
		result.Clear();
		IReadOnlyList<XjSectFamilySeatArchiveRecord> seatRecords = XjSectRepository.ReadAllFamilySeats();
		for (int i = 0; i < seatRecords.Count; i++)
		{
			XjSectFamilySeatArchiveRecord seat = seatRecords[i];
			if (seat == null || seat.SectId <= 0L || seat.FamilyId <= 0L
				|| string.Equals(seat.State, XjSectFamilySeatState.Suspended, StringComparison.Ordinal)) continue;
			if (!result.TryGetValue(seat.SectId, out List<XjSectFamilySeatArchiveRecord> list))
			{
				list = RentSeatList();
				result[seat.SectId] = list;
			}
			list.Add(seat);
		}
	}

	private static void BuildGovernedCityCounts(Dictionary<long, Dictionary<long, int>> result)
	{
		foreach (Dictionary<long, int> counts in result.Values) ReleaseGovernedCityCounts(counts);
		result.Clear();
		IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance = XjSectRepository.ReadAllGovernance();
		for (int i = 0; i < governance.Count; i++)
		{
			XjCityFamilyGovernanceArchiveRecord record = governance[i];
			if (record == null || record.SectId <= 0L || record.GoverningFamilyId <= 0L) continue;
			if (!result.TryGetValue(record.SectId, out Dictionary<long, int> counts))
			{
				counts = RentGovernedCityCounts();
				result[record.SectId] = counts;
			}
			counts.TryGetValue(record.GoverningFamilyId, out int count);
			counts[record.GoverningFamilyId] = count + 1;
		}
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

		int totalLowHerbs = 0;
		int totalHighHerbs = 0;
		int totalGoodMaterials = 0;
		float totalContribution = 0f;
		string leadFamily = string.Empty;
		float bestScore = -1f;

		for (int i = 0; i < seats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord seat = seats[i];
			cityCounts.TryGetValue(seat.FamilyId, out int cityCount);
			int seed = XjDeterministicHash.PositiveIndex(sect.SectId + seat.FamilyId + currentYear, "sect.five_year_task.kind", 3);
			float seatWeight = Math.Max(1f, 1f + cityCount * 0.8f + Math.Max(0f, seat.VoiceScore) / 40f);
			int lowHerbs = 0;
			int highHerbs = 0;
			int goodMaterials = 0;
			string responsibility;
			string dutyPrefix = IsResourceCommandSeat(seat) ? "调度宗门共务" : "宗门共务";
			if (seed == 0)
			{
				lowHerbs = 2 + cityCount;
				highHerbs = seat.VoiceScore >= 35f || cityCount >= 2 ? 1 : 0;
				responsibility = dutyPrefix + "-种植药材";
			}
			else if (seed == 1)
			{
				goodMaterials = 2 + cityCount;
				responsibility = dutyPrefix + "-培育良材";
			}
			else
			{
				lowHerbs = 1 + Math.Max(0, cityCount / 2);
				goodMaterials = 1 + cityCount;
				highHerbs = seat.VoiceScore >= 55f ? 1 : 0;
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
				XjCraftDomainRegistry.TryAddResource(craftOwner, XjCraftCollaborationSystem.SpiritMaterial, Math.Max(1, goodMaterials / 2), currentYear);
				if (cityCount >= 2 || seat.VoiceScore >= 45f)
				{
					XjCraftDomainRegistry.TryAddResource(craftOwner, XjCraftCollaborationSystem.FormationPlateLow, 1, currentYear);
				}
				totalGoodMaterials += goodMaterials;
			}

			float contribution = Math.Max(1f, seatWeight * 2.5f + lowHerbs + highHerbs * 2f + goodMaterials);
			totalContribution += contribution;
			XjSectRepository.TryApplyFamilyContribution(sect.SectId, seat.FamilyId, contribution, goodMaterials * 0.6f, currentYear, responsibility);
			if (contribution > bestScore)
			{
				bestScore = contribution;
				leadFamily = XjFamilyDisplayNameResolver.Resolve(seat.FamilyId);
			}
		}

		string summary = BuildSummary(totalLowHerbs, totalHighHerbs, totalGoodMaterials, leadFamily);
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

	private static string BuildSummary(int lowHerbs, int highHerbs, int goodMaterials, string leadFamily)
	{
		SummaryParts.Clear();
		// 下品药材采集不再进入共务通知，避免信息噪声。
		if (highHerbs > 0) SummaryParts.Add("上品药材" + highHerbs.ToString(CultureInfo.InvariantCulture) + "份");
		if (goodMaterials > 0) SummaryParts.Add("良材" + goodMaterials.ToString(CultureInfo.InvariantCulture) + "批");
		if (!string.IsNullOrWhiteSpace(leadFamily)) SummaryParts.Add(leadFamily + "出力最著");
		return SummaryParts.Count == 0 ? "诸家巡田修圃，暂无额外收成。" : string.Join("，", SummaryParts) + "。";
	}
}
