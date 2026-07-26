using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.Rank;

namespace XuanJianVNext.Systems.Codex;

internal static partial class XjCodexSnapshotPublisher
{
	private const int MaxSnapshotPeakMembersPerPeak = 80;
	private const int UnassignedPeakId = -1;

private static List<XjCodexFormationItem> BuildFormations(IReadOnlyList<XjSectFormationArchiveRecord> records, IReadOnlyDictionary<long, string> sectNames)
	{
		List<XjCodexFormationItem> result = new List<XjCodexFormationItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjSectFormationArchiveRecord record = records[i];
			if (record == null || record.SectId <= 0L || !sectNames.ContainsKey(record.SectId)) continue;
			string leadName = string.Empty;
			if (record.LeadFormationMasterId > 0L && XjScheduler.ResolveActor(record.LeadFormationMasterId, out Actor lead)) leadName = SafeActorName(lead);
			result.Add(new XjCodexFormationItem
			{
				SectId = record.SectId,
				SectName = sectNames.TryGetValue(record.SectId, out string sectName) ? sectName : string.Empty,
				Grade = record.Grade,
				BuildState = record.BuildState ?? string.Empty,
				CurrentDurability = record.CurrentDurability,
				MaxDurability = record.MaxDurability,
				OccupationSpeedMultiplier = record.OccupationSpeedMultiplier,
				CompletionGate = record.CompletionGate,
				LeadFormationMasterId = record.LeadFormationMasterId,
				LeadFormationMasterName = leadName,
				LastDamageYear = record.LastDamageYear,
				LastRepairYear = record.LastRepairYear,
				BuildTaskId = record.BuildTaskId,
				RepairTaskId = record.RepairTaskId
			});
		}
		result.Sort((left, right) =>
		{
			int grade = right.Grade.CompareTo(left.Grade);
			return grade != 0 ? grade : left.SectId.CompareTo(right.SectId);
		});
		return result;
	}

private static List<XjCodexSectItem> BuildSects(
		IReadOnlyList<XjSectArchiveRecord> records,
		IReadOnlyDictionary<long, XjSectFormationArchiveRecord> formationBySect,
		IReadOnlyDictionary<long, string> cityNames,
		IReadOnlyDictionary<long, int> cityCountBySect,
		IReadOnlyDictionary<long, int> cultivatorsBySect,
		IReadOnlyDictionary<long, int> ziFuBySect,
		IReadOnlyDictionary<long, int> jinDanBySect,
		IReadOnlyDictionary<long, string> peakRosterBySect,
		IReadOnlyDictionary<long, IReadOnlyList<XjCodexSectPeakItem>> peaksBySect,
		IReadOnlyList<XjCodexFamilyItem> familyItems,
		IReadOnlyList<XjSectFamilySeatArchiveRecord> familySeats,
		IReadOnlyDictionary<long, string> familyNames)
	{
		Dictionary<long, XjCodexFamilyItem> familyById = new Dictionary<long, XjCodexFamilyItem>();
		if (familyItems != null)
		{
			for (int i = 0; i < familyItems.Count; i++)
			{
				XjCodexFamilyItem family = familyItems[i];
				if (family != null && family.FamilyId > 0L) familyById[family.FamilyId] = family;
			}
		}
		Dictionary<long, List<XjSectFamilySeatArchiveRecord>> seatsBySect = new Dictionary<long, List<XjSectFamilySeatArchiveRecord>>();
		if (familySeats != null)
		{
			for (int i = 0; i < familySeats.Count; i++)
			{
				XjSectFamilySeatArchiveRecord seat = familySeats[i];
				if (seat == null || seat.SectId <= 0L || seat.FamilyId <= 0L) continue;
				if (!seatsBySect.TryGetValue(seat.SectId, out List<XjSectFamilySeatArchiveRecord> list))
				{
					list = new List<XjSectFamilySeatArchiveRecord>();
					seatsBySect[seat.SectId] = list;
				}
				list.Add(seat);
			}
		}
		List<XjCodexSectItem> result = new List<XjCodexSectItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjSectArchiveRecord record = records[i];
			if (record == null || string.Equals(record.Status, XjSectStatus.Extinct, StringComparison.Ordinal)) continue;
			formationBySect.TryGetValue(record.SectId, out XjSectFormationArchiveRecord formation);
			string formationLeadName = formation == null ? string.Empty : ResolveActorName(formation.LeadFormationMasterId);
			long sovereignActorId = record.SovereignActorId;
			string sovereignName = ResolveSectSovereignForSnapshot(record, ref sovereignActorId);
			seatsBySect.TryGetValue(record.SectId, out List<XjSectFamilySeatArchiveRecord> currentSeats);
			ResolveSectHouseLabels(record, sovereignActorId, currentSeats, familyById, familyNames,
				out long governingFamilyId, out string governingFamilyName,
				out long pillarFamilyId, out string pillarFamilyName,
				out IReadOnlyList<XjCodexSectHouseItem> strongFamilies, out string strongFamilySummary,
				out int dissidentFamilyCount, out string dissidentFamilySummary);
			result.Add(new XjCodexSectItem
			{
				SectId = record.SectId,
				KingdomId = record.KingdomId,
				Name = record.Name ?? string.Empty,
				PredecessorKingdomName = record.PredecessorKingdomName ?? string.Empty,
				Status = record.Status ?? string.Empty,
				FoundingYear = record.FoundingYear,
				FounderActorId = record.FounderActorId,
				FounderName = record.FounderName ?? string.Empty,
				SovereignActorId = sovereignActorId,
				SovereignName = sovereignName,
				GoverningFamilyId = governingFamilyId,
				GoverningFamilyName = governingFamilyName,
				PillarFamilyId = pillarFamilyId,
				PillarFamilyName = pillarFamilyName,
				StrongFamilies = strongFamilies,
				StrongFamilySummary = strongFamilySummary,
				DissidentFamilyCount = dissidentFamilyCount,
				DissidentFamilySummary = dissidentFamilySummary,
				LastMandateYear = record.LastMandateYear,
				CapitalCityId = record.CapitalCityId,
				CapitalCityName = cityNames.TryGetValue(record.CapitalCityId, out string cityName) ? cityName : string.Empty,
				CityCount = cityCountBySect.TryGetValue(record.SectId, out int currentCityCount) ? currentCityCount : 0,
				FamilyCount = record.FamilyIds?.Count ?? 0,
				CultivatorCount = cultivatorsBySect.TryGetValue(record.SectId, out int cultivators) ? cultivators : 0,
				ZiFuCount = ziFuBySect.TryGetValue(record.SectId, out int ziFu) ? ziFu : 0,
				JinDanCount = jinDanBySect.TryGetValue(record.SectId, out int jinDan) ? jinDan : 0,
				PeakCount = CountSectPeaks(record, sovereignActorId),
				PeakCapacity = XjSectRepository.MaxSectPeaks,
				PeakSummary = BuildPeakSummary(record, sovereignActorId, sovereignName),
				PeakRosterSummary = peakRosterBySect != null && peakRosterBySect.TryGetValue(record.SectId, out string peakRoster) ? peakRoster : string.Empty,
				Peaks = peaksBySect != null && peaksBySect.TryGetValue(record.SectId, out IReadOnlyList<XjCodexSectPeakItem> peaks) ? peaks : Array.Empty<XjCodexSectPeakItem>(),
				FormationGrade = formation?.Grade ?? 0,
				FormationName = formation != null && formation.Grade > 0 ? XjFormationEngineeringSystem.FormatFormationName(formation.Grade) : string.Empty,
				FormationCurrentDurability = formation?.CurrentDurability ?? 0,
				FormationMaxDurability = formation?.MaxDurability ?? 0,
				FormationState = formation?.BuildState ?? XjSectFormationBuildState.Unplanned,
				FormationLeadActorId = formation?.LeadFormationMasterId ?? 0L,
				FormationLeadName = formationLeadName,
				SecretRealmState = record.SecretRealmId > 0L ? "秘境已立" : "尚无宗门秘境",
				OwnTreasureCount = XjFamilyFaBaoWarehouse.CountSectEntries(record.SectId),
				OwnTreasureSummary = XjFamilyHeritageProjection.ResolveSectTreasure(record.SectId),
				LastTaskYear = record.LastTaskYear,
				LastTaskSummary = record.LastTaskSummary ?? string.Empty
			});
		}
		AttachSectRelations(result, records);
		result.Sort((left, right) =>
		{
			// 宗门档案按创立时间倒序：新宗门在上，古老宗门在下。
			int founding = right.FoundingYear.CompareTo(left.FoundingYear);
			if (founding != 0) return founding;
			return right.SectId.CompareTo(left.SectId);
		});
		return result;
	}

	private const int MaxRelationsPerSect = 24;
	private const int NeutralRelationSamplesPerSect = 4;
	private const int MaxSharedGroupLinksPerSect = 6;

	private sealed class SectRelationCandidate
	{
		internal long LeftSectId;
		internal long RightSectId;
		internal string Relation = "中立";
		internal int Hostility;
		internal string Reason = string.Empty;
	}

	private static void AttachSectRelations(List<XjCodexSectItem> items, IReadOnlyList<XjSectArchiveRecord> records)
	{
		if (items == null || items.Count <= 1 || records == null) return;
		Dictionary<long, XjCodexSectItem> itemById = new Dictionary<long, XjCodexSectItem>();
		Dictionary<long, XjSectArchiveRecord> recordById = new Dictionary<long, XjSectArchiveRecord>();
		Dictionary<long, List<XjCodexSectRelationItem>> relationsBySect = new Dictionary<long, List<XjCodexSectRelationItem>>();
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexSectItem item = items[i];
			if (item == null || item.SectId <= 0L) continue;
			itemById[item.SectId] = item;
			relationsBySect[item.SectId] = new List<XjCodexSectRelationItem>();
		}
		for (int i = 0; i < records.Count; i++)
		{
			XjSectArchiveRecord record = records[i];
			if (record != null && record.SectId > 0L && itemById.ContainsKey(record.SectId)) recordById[record.SectId] = record;
		}
		if (recordById.Count <= 1) return;

		List<XjSectArchiveRecord> activeRecords = new List<XjSectArchiveRecord>(recordById.Values);
		activeRecords.Sort((left, right) => left.SectId.CompareTo(right.SectId));
		Dictionary<(long Left, long Right), SectRelationCandidate> candidates = new Dictionary<(long, long), SectRelationCandidate>();

		// 敌意账本本身是稀疏数据，优先写入“较差/敌对”；低于阈值的轻微摩擦不单独扩张关系表。
		IReadOnlyList<XjSectHostilityArchiveRecord> hostilities = XjSectWarSystem.ReadHostilities();
		for (int i = 0; i < hostilities.Count; i++)
		{
			XjSectHostilityArchiveRecord hostility = hostilities[i];
			if (hostility == null || hostility.LeftSectId <= 0L || hostility.RightSectId <= 0L
				|| !recordById.ContainsKey(hostility.LeftSectId) || !recordById.ContainsKey(hostility.RightSectId)) continue;
			int hostilityValue = Math.Max(0, hostility.Hostility);
			if (hostilityValue < 35) continue;
			string relation = hostilityValue >= 80 ? "敌对" : "较差";
			UpsertSectRelationCandidate(candidates, hostility.LeftSectId, hostility.RightSectId, relation, hostilityValue,
				string.IsNullOrWhiteSpace(hostility.LastReason) ? "两宗已有难以忽略的嫌隙" : hostility.LastReason.Trim());
		}

		// 亲密与友好只从既有宗门档案派生，不新增逐年关系状态机。
		Dictionary<long, List<long>> sectsByFamily = new Dictionary<long, List<long>>();
		Dictionary<string, List<long>> sectsByPredecessor = new Dictionary<string, List<long>>(StringComparer.Ordinal);
		for (int i = 0; i < activeRecords.Count; i++)
		{
			XjSectArchiveRecord record = activeRecords[i];
			AddSectFamilyGroup(sectsByFamily, record.FounderFamilyId, record.SectId);
			AddSectFamilyGroup(sectsByFamily, record.DominantFamilyId, record.SectId);
			if (record.FamilyIds != null)
			{
				for (int j = 0; j < record.FamilyIds.Count; j++) AddSectFamilyGroup(sectsByFamily, record.FamilyIds[j], record.SectId);
			}
			string predecessor = (record.PredecessorKingdomName ?? string.Empty).Trim();
			if (predecessor.Length > 0) AddSectTextGroup(sectsByPredecessor, predecessor, record.SectId);
		}
		foreach (List<long> group in sectsByFamily.Values)
		{
			LinkBoundedSectGroup(candidates, group, "亲密", "两宗共享重要世家或开宗血脉");
		}
		foreach (List<long> group in sectsByPredecessor.Values)
		{
			LinkBoundedSectGroup(candidates, group, "友好", "两宗承自同一旧国地域");
		}

		// “中立”只取稳定的少量邻位样本，避免仙鉴每次刷新构造全宗门 O(n²) 关系矩阵。
		for (int i = 0; i < activeRecords.Count; i++)
		{
			for (int offset = 1; offset <= NeutralRelationSamplesPerSect && offset < activeRecords.Count; offset++)
			{
				int j = (i + offset) % activeRecords.Count;
				if (j == i) continue;
				UpsertSectRelationCandidate(candidates, activeRecords[i].SectId, activeRecords[j].SectId,
					"中立", 0, "暂无足以改变关系的重大往来");
			}
		}

		foreach (SectRelationCandidate candidate in candidates.Values)
		{
			if (!itemById.TryGetValue(candidate.LeftSectId, out XjCodexSectItem leftItem)
				|| !itemById.TryGetValue(candidate.RightSectId, out XjCodexSectItem rightItem)) continue;
			relationsBySect[candidate.LeftSectId].Add(new XjCodexSectRelationItem
			{
				OtherSectId = candidate.RightSectId,
				OtherSectName = rightItem.Name ?? string.Empty,
				Relation = candidate.Relation,
				Hostility = candidate.Hostility,
				Reason = candidate.Reason
			});
			relationsBySect[candidate.RightSectId].Add(new XjCodexSectRelationItem
			{
				OtherSectId = candidate.LeftSectId,
				OtherSectName = leftItem.Name ?? string.Empty,
				Relation = candidate.Relation,
				Hostility = candidate.Hostility,
				Reason = candidate.Reason
			});
		}

		foreach (KeyValuePair<long, List<XjCodexSectRelationItem>> pair in relationsBySect)
		{
			List<XjCodexSectRelationItem> relations = pair.Value;
			relations.Sort((left, right) =>
			{
				int rank = SectRelationRank(left.Relation).CompareTo(SectRelationRank(right.Relation));
				if (rank != 0) return rank;
				int hostility = right.Hostility.CompareTo(left.Hostility);
				return hostility != 0 ? hostility : string.Compare(left.OtherSectName, right.OtherSectName, StringComparison.Ordinal);
			});
			if (relations.Count > MaxRelationsPerSect) relations.RemoveRange(MaxRelationsPerSect, relations.Count - MaxRelationsPerSect);
			if (itemById.TryGetValue(pair.Key, out XjCodexSectItem item)) item.Relations = relations;
		}
	}

	private static void AddSectFamilyGroup(Dictionary<long, List<long>> groups, long familyId, long sectId)
	{
		if (groups == null || familyId <= 0L || sectId <= 0L) return;
		if (!groups.TryGetValue(familyId, out List<long> values))
		{
			values = new List<long>();
			groups[familyId] = values;
		}
		if (!values.Contains(sectId)) values.Add(sectId);
	}

	private static void AddSectTextGroup(Dictionary<string, List<long>> groups, string key, long sectId)
	{
		if (groups == null || string.IsNullOrWhiteSpace(key) || sectId <= 0L) return;
		string normalized = key.Trim();
		if (!groups.TryGetValue(normalized, out List<long> values))
		{
			values = new List<long>();
			groups[normalized] = values;
		}
		if (!values.Contains(sectId)) values.Add(sectId);
	}

	private static void LinkBoundedSectGroup(
		Dictionary<(long Left, long Right), SectRelationCandidate> candidates,
		List<long> sectIds,
		string relation,
		string reason)
	{
		if (candidates == null || sectIds == null || sectIds.Count <= 1) return;
		sectIds.Sort();
		for (int i = 0; i < sectIds.Count; i++)
		{
			int end = Math.Min(sectIds.Count, i + MaxSharedGroupLinksPerSect + 1);
			for (int j = i + 1; j < end; j++)
			{
				UpsertSectRelationCandidate(candidates, sectIds[i], sectIds[j], relation, 0, reason);
			}
		}
	}

	private static void UpsertSectRelationCandidate(
		Dictionary<(long Left, long Right), SectRelationCandidate> candidates,
		long firstSectId,
		long secondSectId,
		string relation,
		int hostility,
		string reason)
	{
		if (candidates == null || firstSectId <= 0L || secondSectId <= 0L || firstSectId == secondSectId) return;
		long left = Math.Min(firstSectId, secondSectId);
		long right = Math.Max(firstSectId, secondSectId);
		(long Left, long Right) key = (left, right);
		if (candidates.TryGetValue(key, out SectRelationCandidate existing)
			&& SectRelationRank(existing.Relation) <= SectRelationRank(relation))
		{
			return;
		}
		candidates[key] = new SectRelationCandidate
		{
			LeftSectId = left,
			RightSectId = right,
			Relation = string.IsNullOrWhiteSpace(relation) ? "中立" : relation.Trim(),
			Hostility = Math.Max(0, hostility),
			Reason = string.IsNullOrWhiteSpace(reason) ? "暂无足以改变关系的重大往来" : reason.Trim()
		};
	}

	private static int SectRelationRank(string relation)
	{
		if (string.Equals(relation, "敌对", StringComparison.Ordinal)) return 0;
		if (string.Equals(relation, "较差", StringComparison.Ordinal)) return 1;
		if (string.Equals(relation, "亲密", StringComparison.Ordinal)) return 2;
		if (string.Equals(relation, "友好", StringComparison.Ordinal)) return 3;
		return 4;
	}

private static void ResolveSectHouseLabels(
	XjSectArchiveRecord record,
	long sovereignActorId,
	IReadOnlyList<XjSectFamilySeatArchiveRecord> seats,
	IReadOnlyDictionary<long, XjCodexFamilyItem> familyById,
	IReadOnlyDictionary<long, string> familyNames,
	out long governingFamilyId,
	out string governingFamilyName,
	out long pillarFamilyId,
	out string pillarFamilyName,
	out IReadOnlyList<XjCodexSectHouseItem> strongFamilies,
	out string strongFamilySummary,
	out int dissidentFamilyCount,
	out string dissidentFamilySummary)
{
	governingFamilyId = 0L;
	if (sovereignActorId > 0L) XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(sovereignActorId, out governingFamilyId);
	if (governingFamilyId <= 0L) governingFamilyId = record?.DominantFamilyId > 0L ? record.DominantFamilyId : record?.FounderFamilyId ?? 0L;
	governingFamilyName = ResolveFamilyNameForSectLabel(governingFamilyId, familyNames);

	List<XjCodexSectHouseItem> strong = new List<XjCodexSectHouseItem>();
	List<(long FamilyId, float Severity)> dissidents = null;
	if (seats != null)
	{
		for (int i = 0; i < seats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord seat = seats[i];
			if (seat == null || seat.FamilyId <= 0L) continue;
			XjCodexFamilyItem family = null;
			if (familyById != null) familyById.TryGetValue(seat.FamilyId, out family);
			int qualifiedCount = family?.QualifiedHighRealmCount ?? 0;
			int qualifiedScore = family?.QualifiedHighRealmScore ?? 0;
			if (qualifiedCount > 0)
			{
				strong.Add(new XjCodexSectHouseItem
				{
					FamilyId = seat.FamilyId,
					FamilyName = ResolveFamilyNameForSectLabel(seat.FamilyId, familyNames),
					QualifiedMemberCount = qualifiedCount,
					StrongestRealmOrder = family.StrongestQualifiedRealmOrder,
					StrongestRealm = family.StrongestQualifiedRealm,
					StrengthScore = qualifiedScore + Math.Max(0f, seat.StrengthScore) * 0.1f,
					VoiceScore = seat.VoiceScore
				});
			}

			bool suspended = string.Equals(seat.State, XjSectFamilySeatState.Suspended, StringComparison.Ordinal);
			bool hasPoliticalWeight = qualifiedCount > 0 || seat.VoiceScore >= 35f;
			bool isGoverning = seat.FamilyId == governingFamilyId;
			bool disobedient = suspended
				|| hasPoliticalWeight && !isGoverning && seat.SupplyDebt >= 75f
				|| hasPoliticalWeight && !isGoverning && seat.SupplyDebt >= 55f && seat.VoiceScore >= 35f
				|| hasPoliticalWeight && !isGoverning && seat.PrivilegeHeat >= 80f && seat.SupplyDebt >= 45f;
			if (disobedient)
			{
				float severity = (suspended ? 100f : 0f) + seat.SupplyDebt * 0.65f
					+ Math.Max(0f, seat.PrivilegeHeat - 50f) * 0.25f + seat.VoiceScore * 0.1f;
				dissidents ??= new List<(long, float)>();
				dissidents.Add((seat.FamilyId, severity));
			}
		}
	}

	// 宗门最强世家读取全部门下家族，而不是只读取已经取得正式席位者。
	// 离心仍以席位、债务和特权数据判断，避免把没有治理席位的家族误判为离心。
	if (record?.FamilyIds != null && familyById != null)
	{
		for (int i = 0; i < record.FamilyIds.Count; i++)
		{
			long familyId = record.FamilyIds[i];
			if (familyId <= 0L || !familyById.TryGetValue(familyId, out XjCodexFamilyItem family)
				|| family == null || family.QualifiedHighRealmCount <= 0) continue;
			bool alreadyAdded = false;
			for (int c = 0; c < strong.Count; c++)
			{
				if (strong[c].FamilyId != familyId) continue;
				alreadyAdded = true;
				break;
			}
			if (alreadyAdded) continue;
			strong.Add(new XjCodexSectHouseItem
			{
				FamilyId = familyId,
				FamilyName = ResolveFamilyNameForSectLabel(familyId, familyNames),
				QualifiedMemberCount = family.QualifiedHighRealmCount,
				StrongestRealmOrder = family.StrongestQualifiedRealmOrder,
				StrongestRealm = family.StrongestQualifiedRealm,
				StrengthScore = family.QualifiedHighRealmScore,
				VoiceScore = 0f
			});
		}
	}

	strong.Sort((left, right) =>
	{
		int score = right.StrengthScore.CompareTo(left.StrengthScore);
		if (score != 0) return score;
		int realm = right.StrongestRealmOrder.CompareTo(left.StrongestRealmOrder);
		if (realm != 0) return realm;
		int count = right.QualifiedMemberCount.CompareTo(left.QualifiedMemberCount);
		if (count != 0) return count;
		int voice = right.VoiceScore.CompareTo(left.VoiceScore);
		return voice != 0 ? voice : left.FamilyId.CompareTo(right.FamilyId);
	});
	if (strong.Count > 3) strong.RemoveRange(3, strong.Count - 3);
	strongFamilies = strong;
	pillarFamilyId = strong.Count > 0 ? strong[0].FamilyId : 0L;
	pillarFamilyName = strong.Count > 0 ? strong[0].FamilyName : string.Empty;
	List<string> strongNames = new List<string>(strong.Count);
	for (int i = 0; i < strong.Count; i++)
	{
		strongNames.Add(strong[i].FamilyName + "（" + EmptyText(strong[i].StrongestRealm, "筑基后期") + "·" + strong[i].QualifiedMemberCount + "人）");
	}
	strongFamilySummary = strongNames.Count > 0 ? string.Join("、", strongNames) : "暂无筑基后期以上世家";

	if (dissidents == null || dissidents.Count == 0)
	{
		dissidentFamilyCount = 0;
		dissidentFamilySummary = "无";
		return;
	}
	dissidents.Sort((left, right) =>
	{
		int severity = right.Severity.CompareTo(left.Severity);
		return severity != 0 ? severity : left.FamilyId.CompareTo(right.FamilyId);
	});
	dissidentFamilyCount = dissidents.Count;
	List<string> names = new List<string>(Math.Min(3, dissidents.Count));
	for (int i = 0; i < dissidents.Count && i < 3; i++) names.Add(ResolveFamilyNameForSectLabel(dissidents[i].FamilyId, familyNames));
	dissidentFamilySummary = string.Join("、", names) + (dissidents.Count > names.Count ? "等" + dissidents.Count + "家" : string.Empty);
}

private static string ResolveFamilyNameForSectLabel(long familyId, IReadOnlyDictionary<long, string> familyNames)
	{
		if (familyId <= 0L) return string.Empty;
		if (familyNames != null && familyNames.TryGetValue(familyId, out string name) && !string.IsNullOrWhiteSpace(name)) return name.Trim();
		return XjFamilyDisplayNameResolver.Resolve(familyId);
	}

private static string ResolveSectSovereignForSnapshot(XjSectArchiveRecord record, ref long sovereignActorId)
	{
		if (record == null)
		{
			sovereignActorId = 0L;
			return string.Empty;
		}

		if (record.SovereignActorId > 0L
			&& XjScheduler.ResolveActor(record.SovereignActorId, out Actor current)
			&& current?.data != null
			&& current.isAlive()
			&& ReadActorSectId(current) == record.SectId)
		{
			sovereignActorId = record.SovereignActorId;
			return SafeActorName(current);
		}

		Actor ranked = FindSovereignCandidate(record, preferStoredRank: true);
		if (ranked?.data != null)
		{
			sovereignActorId = ((BaseSystemData)ranked.data).id;
			return SafeActorName(ranked);
		}

		Actor realmCandidate = FindSovereignCandidate(record, preferStoredRank: false);
		if (realmCandidate?.data != null)
		{
			sovereignActorId = ((BaseSystemData)realmCandidate.data).id;
			return SafeActorName(realmCandidate);
		}

		sovereignActorId = 0L;
		return string.Empty;
	}

private static Actor FindSovereignCandidate(XjSectArchiveRecord record, bool preferStoredRank)
	{
		if (record == null) return null;
		Actor best = null;
		int bestOrder = -1;
		float bestZhenYuan = -1f;
		long bestActorId = long.MaxValue;
		HashSet<long> seen = new HashSet<long>();
		IReadOnlyList<long> actorIds = XjZongMenCultivatorCityIndex.GetActorIdsForSect(record.SectId);
		for (int i = 0; i < actorIds.Count; i++)
		{
			EvaluateSnapshotSovereignCandidate(record, preferStoredRank, actorIds[i], seen, ref best, ref bestOrder, ref bestZhenYuan, ref bestActorId);
		}
		return best;
	}

private static void EvaluateSnapshotSovereignCandidate(
		XjSectArchiveRecord record,
		bool preferStoredRank,
		long actorId,
		HashSet<long> seen,
		ref Actor best,
		ref int bestOrder,
		ref float bestZhenYuan,
		ref long bestActorId)
	{
		if (record == null || actorId <= 0L || seen == null || !seen.Add(actorId)
			|| !XjScheduler.ResolveActor(actorId, out Actor actor)
			|| actor?.data == null
			|| !actor.isAlive()
			|| ReadActorSectId(actor) != record.SectId)
		{
			return;
		}

		if (preferStoredRank)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenRank, out string rank);
			if (string.IsNullOrWhiteSpace(rank) || !rank.Contains("宗主")) return;
		}
		else if (!IsAtLeastZiFu(actor) && !IsStrictZhuJiLate(actor))
		{
			return;
		}

		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		int order = XjRealmHelper.GetOrder(realmId);
		float zhenYuan = ReadFloat(actor, XjActorDataKeys.ZhenYuan);
		if (best == null
			|| order > bestOrder
			|| (order == bestOrder && zhenYuan > bestZhenYuan)
			|| (order == bestOrder && Math.Abs(zhenYuan - bestZhenYuan) < 0.001f && actorId < bestActorId))
		{
			best = actor;
			bestOrder = order;
			bestZhenYuan = zhenYuan;
			bestActorId = actorId;
		}
	}

private static int CountSectPeaks(XjSectArchiveRecord record, long sovereignActorId)
	{
		// 开宗即有主峰；宗主暂缺只影响主峰主持者，不应让主峰从峰位统计中消失。
		int count = record != null && record.SectId > 0L ? 1 : 0;
		if (record?.Peaks != null)
		{
			for (int i = 0; i < record.Peaks.Count; i++)
			{
				XjSectPeakArchiveRecord peak = record.Peaks[i];
				if (IsValidSnapshotPeak(record.SectId, peak) && peak.PeakMasterActorId != sovereignActorId) count++;
			}
		}
		return Math.Min(XjSectRepository.MaxSectPeaks, count);
	}

private static string BuildPeakSummary(XjSectArchiveRecord record, long sovereignActorId, string sovereignName)
	{
		if (record == null || record.SectId <= 0L) return "尚未开峰";
		List<XjSectPeakArchiveRecord> peaks = record?.Peaks == null
			? new List<XjSectPeakArchiveRecord>()
			: new List<XjSectPeakArchiveRecord>(record.Peaks);
		peaks.Sort((left, right) => left.PeakId.CompareTo(right.PeakId));
		List<string> parts = new List<string>();
		parts.Add("主峰：" + (sovereignActorId <= 0L || string.IsNullOrWhiteSpace(sovereignName) ? "宗主待继任" : sovereignName.Trim()));
		for (int i = 0; i < peaks.Count && parts.Count < 4; i++)
		{
			XjSectPeakArchiveRecord peak = peaks[i];
			if (!IsValidSnapshotPeak(record.SectId, peak) || peak.PeakMasterActorId == sovereignActorId) continue;
			parts.Add(FormatSectPeakName(peak.PeakId, peak.PeakName)
				+ "：" + (string.IsNullOrWhiteSpace(peak.PeakMasterName) ? "暂无峰主" : peak.PeakMasterName.Trim()));
		}
		int total = CountSectPeaks(record, sovereignActorId);
		if (total > parts.Count) parts.Add("另" + (total - parts.Count).ToString(CultureInfo.InvariantCulture) + "峰");
		return parts.Count == 0 ? "尚未开峰" : string.Join("；", parts);
	}

private static Dictionary<long, IReadOnlyList<XjCodexSectPeakItem>> BuildSectPeakRosters(IReadOnlyList<XjSectArchiveRecord> records)
	{
		Dictionary<long, Dictionary<int, XjCodexSectPeakItem>> rosters = new Dictionary<long, Dictionary<int, XjCodexSectPeakItem>>();
		if (records != null)
		{
			for (int i = 0; i < records.Count; i++)
			{
				XjSectArchiveRecord record = records[i];
				if (record == null || record.SectId <= 0L) continue;
				Dictionary<int, XjCodexSectPeakItem> byPeak = EnsureSectPeakMap(rosters, record.SectId);
				EnsurePeakItem(byPeak, 0, "主峰");
				if (record.Peaks != null)
				{
					for (int j = 0; j < record.Peaks.Count; j++)
					{
						XjSectPeakArchiveRecord peak = record.Peaks[j];
						if (!IsValidSnapshotPeak(record.SectId, peak)) continue;
						XjCodexSectPeakItem item = EnsurePeakItem(byPeak, peak.PeakId, FormatSnapshotPeakName(peak.PeakId, peak.PeakName));
						item.PeakMasterActorId = peak.PeakMasterActorId;
						item.PeakMasterName = !string.IsNullOrWhiteSpace(peak.PeakMasterName)
							? peak.PeakMasterName.Trim()
							: ResolveActorName(peak.PeakMasterActorId);
					}
				}
				long sovereignActorId = record.SovereignActorId;
				string sovereignName = ResolveSectSovereignForSnapshot(record, ref sovereignActorId);
				if (sovereignActorId > 0L)
				{
					XjCodexSectPeakItem mainPeak = EnsurePeakItem(byPeak, 0, "主峰");
					mainPeak.PeakMasterActorId = sovereignActorId;
					mainPeak.PeakMasterName = sovereignName;
				}
			}
		}

		if (records != null)
		{
			for (int i = 0; i < records.Count; i++)
			{
				XjSectArchiveRecord record = records[i];
				if (record == null || record.SectId <= 0L) continue;
				Dictionary<int, XjCodexSectPeakItem> byPeak = EnsureSectPeakMap(rosters, record.SectId);
				// 峰主卡片必须优先进入有限成员快照，避免大峰弟子达到上限后峰主被截掉。
				foreach (KeyValuePair<int, XjCodexSectPeakItem> peakEntry in byPeak)
				{
					XjCodexSectPeakItem peak = peakEntry.Value;
					if (peak?.PeakMasterActorId > 0L
						&& XjScheduler.ResolveActor(peak.PeakMasterActorId, out Actor peakMaster)
						&& peakMaster?.data != null
						&& peakMaster.isAlive())
					{
						AddSectPeakMember(peak, peakMaster, peak.PeakMasterActorId);
					}
				}
				IReadOnlyList<long> actorIds = XjZongMenCultivatorCityIndex.GetActorIdsForSect(record.SectId);
				for (int j = 0; j < actorIds.Count; j++)
				{
					long actorId = actorIds[j];
					if (actorId <= 0L
						|| !XjScheduler.ResolveActor(actorId, out Actor actor)
						|| actor?.data == null
						|| !actor.isAlive()
						|| ReadActorSectId(actor) != record.SectId)
					{
						continue;
					}

					int storedPeakId = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenPeakId, out int readPeakId) ? readPeakId : 0;
					int peakId = ResolveActiveSnapshotPeakId(byPeak, actorId, storedPeakId);
					XjCodexSectPeakItem peak = byPeak.TryGetValue(peakId, out XjCodexSectPeakItem activePeak)
						? activePeak
						: EnsurePeakItem(byPeak, 0, "主峰");
					AddSectPeakMember(peak, actor, actorId);
				}
			}
		}

		Dictionary<long, IReadOnlyList<XjCodexSectPeakItem>> result = new Dictionary<long, IReadOnlyList<XjCodexSectPeakItem>>();
		foreach (KeyValuePair<long, Dictionary<int, XjCodexSectPeakItem>> entry in rosters)
		{
			List<XjCodexSectPeakItem> peaks = new List<XjCodexSectPeakItem>(entry.Value.Values);
			for (int i = 0; i < peaks.Count; i++)
			{
				XjCodexSectPeakItem peak = peaks[i];
				peak.Members.Sort(ComparePeakMembers);
			}
			peaks.Sort(ComparePeaks);
			result[entry.Key] = peaks;
		}
		return result;
	}

private static bool IsValidSnapshotPeak(long sectId, XjSectPeakArchiveRecord peak)
	{
		if (sectId <= 0L || peak == null || peak.PeakId <= 0 || peak.PeakMasterActorId <= 0L) return false;
		return XjScheduler.ResolveActor(peak.PeakMasterActorId, out Actor master)
			&& master?.data != null
			&& master.isAlive()
			&& ReadActorSectId(master) == sectId;
	}

private static int ResolveActiveSnapshotPeakId(
		Dictionary<int, XjCodexSectPeakItem> byPeak,
		long actorId,
		int storedPeakId)
	{
		if (byPeak == null || actorId <= 0L) return 0;
		foreach (KeyValuePair<int, XjCodexSectPeakItem> pair in byPeak)
		{
			if (pair.Value != null && pair.Value.PeakMasterActorId == actorId) return pair.Key;
		}

		if (storedPeakId > 0
			&& byPeak.TryGetValue(storedPeakId, out XjCodexSectPeakItem storedPeak)
			&& storedPeak?.PeakMasterActorId > 0L)
		{
			return storedPeakId;
		}
		return 0;
	}

private static Dictionary<long, string> BuildSectPeakRosterSummaries(IReadOnlyDictionary<long, IReadOnlyList<XjCodexSectPeakItem>> rosters)
	{
		Dictionary<long, string> result = new Dictionary<long, string>();
		if (rosters == null) return result;
		foreach (KeyValuePair<long, IReadOnlyList<XjCodexSectPeakItem>> entry in rosters)
		{
			IReadOnlyList<XjCodexSectPeakItem> peaks = entry.Value;
			if (peaks == null || peaks.Count == 0) continue;
			List<string> parts = new List<string>();
			for (int i = 0; i < peaks.Count && parts.Count < 4; i++)
			{
				XjCodexSectPeakItem peak = peaks[i];
				if (peak == null || peak.MemberCount <= 0) continue;
				List<string> names = new List<string>();
				for (int j = 0; j < peak.Members.Count && names.Count < 4; j++)
				{
					if (!string.IsNullOrWhiteSpace(peak.Members[j].Name)) names.Add(peak.Members[j].Name);
				}
				if (names.Count == 0) continue;
				if (peak.MemberCount > names.Count) names.Add("另" + (peak.MemberCount - names.Count).ToString(CultureInfo.InvariantCulture) + "人");
				parts.Add(peak.PeakName + "：" + string.Join("、", names));
			}
			if (peaks.Count > parts.Count) parts.Add("另" + (peaks.Count - parts.Count).ToString(CultureInfo.InvariantCulture) + "峰");
			if (parts.Count > 0) result[entry.Key] = string.Join("；", parts);
		}
		return result;
	}

private static Dictionary<int, XjCodexSectPeakItem> EnsureSectPeakMap(Dictionary<long, Dictionary<int, XjCodexSectPeakItem>> rosters, long sectId)
	{
		if (!rosters.TryGetValue(sectId, out Dictionary<int, XjCodexSectPeakItem> byPeak))
		{
			byPeak = new Dictionary<int, XjCodexSectPeakItem>();
			rosters[sectId] = byPeak;
		}
		return byPeak;
	}

private static XjCodexSectPeakItem EnsurePeakItem(Dictionary<int, XjCodexSectPeakItem> byPeak, int peakId, string peakName)
	{
		if (!byPeak.TryGetValue(peakId, out XjCodexSectPeakItem item))
		{
			item = new XjCodexSectPeakItem
			{
				PeakId = peakId,
				PeakName = string.IsNullOrWhiteSpace(peakName) ? FormatSnapshotPeakName(peakId, string.Empty) : peakName.Trim()
			};
			byPeak[peakId] = item;
		}
		else if (string.IsNullOrWhiteSpace(item.PeakName) || string.Equals(item.PeakName, "未分峰", StringComparison.Ordinal))
		{
			item.PeakName = string.IsNullOrWhiteSpace(peakName) ? FormatSnapshotPeakName(peakId, string.Empty) : peakName.Trim();
		}
		return item;
	}

private static void AddSectPeakMember(XjCodexSectPeakItem peak, Actor actor, long actorId)
	{
		if (peak == null || actor?.data == null || actorId <= 0L || ContainsPeakMember(peak.Members, actorId)) return;
		peak.MemberCount++;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		int order = XjRealmHelper.GetOrder(realmId);
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.JinDan)) peak.JinDanCount++;
		else if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) peak.ZiFuCount++;
		if (peak.Members.Count >= MaxSnapshotPeakMembersPerPeak) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		int xianJiCount = 0;
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZhuJi))
		{
			xianJiCount = XjActorCultivationSnapshotBuilder.Build(actor).XianJiCount;
		}
		string craft = ResolveFamilyMemberCraftSummary(actor);
		string keySummary = xianJiCount > 0
			? "仙基" + xianJiCount.ToString(CultureInfo.InvariantCulture)
			: string.Empty;
		if (!string.IsNullOrWhiteSpace(craft) && craft != "无")
		{
			keySummary = keySummary.Length == 0 ? craft : keySummary + " · " + craft;
		}
		peak.Members.Add(new XjCodexSectPeakMemberItem
		{
			ActorId = actorId,
			Name = SafeActorName(actor),
			Realm = ResolveActorRealmDisplay(actor, realmId),
			DaoTu = XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu) ? daoTu : string.Empty,
			Role = ResolvePeakMemberRole(peak, actorId, order),
			RealmOrder = order,
			Aptitude = XjRankSortSystem.GetAptitudeName(aptitude),
			Age = (int)Math.Floor(Math.Max(0f, actor.getAge())),
			CraftSummary = craft,
			XianJiCount = xianJiCount,
			KeySummary = keySummary.Length == 0 ? "暂无关键传承" : keySummary
		});
	}

private static bool ContainsPeakMember(List<XjCodexSectPeakMemberItem> members, long actorId)
	{
		if (members == null) return false;
		for (int i = 0; i < members.Count; i++)
		{
			if (members[i]?.ActorId == actorId) return true;
		}
		return false;
	}

private static string ResolveActorRealmDisplay(Actor actor, string realmId)
	{
		float zhenYuan = ReadFloat(actor, XjActorDataKeys.ZhenYuan);
		int xianJiCount = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int count) ? count : 0;
		string display = XjDaoXingStageRules.FormatDisplay(realmId, zhenYuan, xianJiCount, 0);
		return string.IsNullOrWhiteSpace(display) ? XjRealmHelper.GetDisplayName(realmId) : display;
	}

private static string ResolvePeakMemberRole(XjCodexSectPeakItem peak, long actorId, int realmOrder)
	{
		if (peak.PeakMasterActorId == actorId)
		{
			return peak.PeakId == 0 ? "宗主" : "峰主";
		}
		if (realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.JinDan)) return "真君";
		if (realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) return "真人";
		return "弟子";
	}

private static string FormatSnapshotPeakName(int peakId, string storedName)
	{
		if (peakId == UnassignedPeakId) return "未分峰";
		if (peakId == 0) return "主峰";
		return FormatSectPeakName(peakId, storedName);
	}

private static int ComparePeaks(XjCodexSectPeakItem left, XjCodexSectPeakItem right)
	{
		int leftOrder = PeakSortOrder(left?.PeakId ?? UnassignedPeakId);
		int rightOrder = PeakSortOrder(right?.PeakId ?? UnassignedPeakId);
		int order = leftOrder.CompareTo(rightOrder);
		return order != 0 ? order : string.Compare(left?.PeakName, right?.PeakName, StringComparison.Ordinal);
	}

private static int PeakSortOrder(int peakId)
	{
		if (peakId == 0) return 0;
		if (peakId == UnassignedPeakId) return int.MaxValue;
		return peakId;
	}

private static int ComparePeakMembers(XjCodexSectPeakMemberItem left, XjCodexSectPeakMemberItem right)
	{
		int realm = (right?.RealmOrder ?? -1).CompareTo(left?.RealmOrder ?? -1);
		if (realm != 0) return realm;
		int role = PeakRoleSortOrder(left?.Role).CompareTo(PeakRoleSortOrder(right?.Role));
		if (role != 0) return role;
		int name = string.Compare(left?.Name, right?.Name, StringComparison.Ordinal);
		return name != 0 ? name : (left?.ActorId ?? long.MaxValue).CompareTo(right?.ActorId ?? long.MaxValue);
	}

private static int PeakRoleSortOrder(string role)
	{
		if (string.Equals(role, "宗主", StringComparison.Ordinal)) return 0;
		if (string.Equals(role, "峰主", StringComparison.Ordinal)) return 1;
		if (string.Equals(role, "真君", StringComparison.Ordinal)) return 2;
		if (string.Equals(role, "真人", StringComparison.Ordinal)) return 3;
		return 4;
	}

private static long ReadActorSectId(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor);
	}

private static bool IsAtLeastZiFu(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return XjRealmHelper.GetOrder(realmId) >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
	}

private static bool IsStrictZhuJiLate(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZhuJi, StringComparison.Ordinal)) return false;
		float zhenYuan = ReadFloat(actor, XjActorDataKeys.ZhenYuan);
		if (zhenYuan >= 24000f) return true;
		int xianJiCount = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int count) ? count : 0;
		return string.Equals(XjDaoXingStageRules.FormatDisplay(realmId, zhenYuan, xianJiCount, 0), "筑基后期", StringComparison.Ordinal);
	}

private static float ReadFloat(Actor actor, string key)
	{
		return XjActorAccessor.TryGetFloat(actor, key, out float value) ? value : 0f;
	}

private static string ResolveActorName(long actorId)
	{
		if (actorId <= 0L)
		{
			return string.Empty;
		}
		return XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) ? SafeActorName(actor) : string.Empty;
	}
}
