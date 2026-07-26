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
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.Rank;

namespace XuanJianVNext.Systems.Codex;

internal static partial class XjCodexSnapshotPublisher
{
	private const int MaxSnapshotFamilyMembers = 80;

private static void BuildFamilies(
		IReadOnlyList<XjFamilyLedgerAggregate> aggregates,
		IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance,
		IReadOnlyList<XjSectFamilySeatArchiveRecord> familySeats,
		IReadOnlyList<XjSectArchiveRecord> sectRecords,
		IReadOnlyDictionary<long, string> cityNames,
		IReadOnlyDictionary<long, string> sectNames,
		int worldYear,
		out List<XjCodexFamilyItem> items,
		out Dictionary<long, string> familyNames)
	{
		Dictionary<long, XjCodexFamilyItem> byFamily = new Dictionary<long, XjCodexFamilyItem>();
		Dictionary<long, XjCenturyFamilyStageStateRecord> stageStateByFamily = new Dictionary<long, XjCenturyFamilyStageStateRecord>();
		IReadOnlyList<XjCenturyFamilyStageStateRecord> familyStageStates = XjCenturyAnnalsStore.ReadFamilyStageStateView();
		if (familyStageStates != null)
		{
			for (int i = 0; i < familyStageStates.Count; i++)
			{
				XjCenturyFamilyStageStateRecord state = familyStageStates[i];
				if (state == null || state.FamilyStableId <= 0L) continue;
				if (!stageStateByFamily.TryGetValue(state.FamilyStableId, out XjCenturyFamilyStageStateRecord existing)
					|| state.LastUpdatedYear >= existing.LastUpdatedYear)
				{
					stageStateByFamily[state.FamilyStableId] = state;
				}
			}
		}
		HashSet<long> livingFamilyIds = new HashSet<long>();
		for (int i = 0; i < aggregates.Count; i++)
		{
			XjFamilyLedgerAggregate aggregate = aggregates[i];
			if (aggregate.FamilyStableId <= 0L || aggregate.AliveCount <= 0) continue;
			livingFamilyIds.Add(aggregate.FamilyStableId);
			byFamily[aggregate.FamilyStableId] = new XjCodexFamilyItem
			{
				FamilyId = aggregate.FamilyStableId,
				Name = aggregate.DisplayName,
				AliveCount = aggregate.AliveCount,
				TotalCount = aggregate.TotalCount,
				CultivatorCount = aggregate.CultivatorCount,
				ZiFuCount = aggregate.ZiFuCount,
				JinDanCount = aggregate.JinDanCount,
				HighestRealmOrder = aggregate.HighestRealmOrder,
				HighestRealm = aggregate.CultivatorCount > 0
					? aggregate.HighestRealm
					: "凡俗",
				Representative = aggregate.Representative,
				RepresentativeActorId = aggregate.RepresentativeActorId
			};
		}

		Dictionary<long, XjSectFamilySeatArchiveRecord> seatByFamily = new Dictionary<long, XjSectFamilySeatArchiveRecord>();
		for (int i = 0; i < familySeats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord seat = familySeats[i];
			if (seat == null || seat.FamilyId <= 0L || seat.SectId <= 0L || !sectNames.ContainsKey(seat.SectId)) continue;
			if (!seatByFamily.TryGetValue(seat.FamilyId, out XjSectFamilySeatArchiveRecord existing) || seat.VoiceScore > existing.VoiceScore) seatByFamily[seat.FamilyId] = seat;
		}

		Dictionary<long, List<string>> citiesByFamily = new Dictionary<long, List<string>>();
		for (int i = 0; i < governance.Count; i++)
		{
			XjCityFamilyGovernanceArchiveRecord record = governance[i];
			if (record == null || record.GoverningFamilyId <= 0L) continue;
			if (!livingFamilyIds.Contains(record.GoverningFamilyId)) continue;
			if (!byFamily.TryGetValue(record.GoverningFamilyId, out XjCodexFamilyItem item))
			{
				item = new XjCodexFamilyItem { FamilyId = record.GoverningFamilyId };
				byFamily.Add(record.GoverningFamilyId, item);
			}
			if (!citiesByFamily.TryGetValue(record.GoverningFamilyId, out List<string> names))
			{
				names = new List<string>();
				citiesByFamily.Add(record.GoverningFamilyId, names);
			}
			if (cityNames.TryGetValue(record.CityId, out string cityName)) names.Add(cityName);
			if (item.PrimaryCityId <= 0L) item.PrimaryCityId = record.CityId;
			item.SectId = record.SectId;
			item.SectName = sectNames.TryGetValue(record.SectId, out string sectName) ? sectName : string.Empty;
		}
		for (int i = 0; i < sectRecords.Count; i++)
		{
			XjSectArchiveRecord sect = sectRecords[i];
			if (sect?.FamilyIds == null) continue;
			for (int f = 0; f < sect.FamilyIds.Count; f++)
			{
				long familyId = sect.FamilyIds[f];
				if (!livingFamilyIds.Contains(familyId)) continue;
				if (!byFamily.TryGetValue(familyId, out XjCodexFamilyItem item))
				{
					item = new XjCodexFamilyItem { FamilyId = familyId };
					byFamily.Add(familyId, item);
				}
				if (item.SectId <= 0L)
				{
					item.SectId = sect.SectId;
					item.SectName = sect.Name ?? string.Empty;
				}
			}
		}

		items = new List<XjCodexFamilyItem>(byFamily.Values);
		familyNames = new Dictionary<long, string>();
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexFamilyItem item = items[i];
			item.Name = XjFamilyDisplayNameResolver.Resolve(item.FamilyId);
			if (citiesByFamily.TryGetValue(item.FamilyId, out List<string> names))
			{
				names.Sort(StringComparer.Ordinal);
				item.GoverningCityCount = names.Count;
				item.GoverningCities = string.Join("、", names);
			}
			if (stageStateByFamily.TryGetValue(item.FamilyId, out XjCenturyFamilyStageStateRecord stageState))
			{
				item.ActiveAspiration = stageState.ActiveAspiration ?? string.Empty;
				item.AspirationSinceYear = Math.Max(0, stageState.AspirationSinceYear);
				item.SupportedActorId = Math.Max(0L, stageState.SupportedActorId);
				item.SupportPurpose = stageState.SupportPurpose ?? string.Empty;
				item.SupportedSinceYear = Math.Max(0, stageState.SupportedSinceYear);
				item.ClanLeaderActorId = Math.Max(0L, stageState.LastClanLeaderActorId);
			}
			if (seatByFamily.TryGetValue(item.FamilyId, out XjSectFamilySeatArchiveRecord seat))
			{
				item.SectId = seat.SectId;
				item.SectName = sectNames.TryGetValue(seat.SectId, out string seatSectName) ? seatSectName : item.SectName;
				item.ContributionScore = seat.ContributionScore;
				item.CraftScore = seat.CraftScore;
				item.VoiceScore = seat.VoiceScore;
				item.VoiceTier = seat.VoiceTier ?? string.Empty;
				item.SupplyDebt = seat.SupplyDebt;
				item.Responsibility = seat.Responsibility ?? string.Empty;
				item.PrivilegeHeat = seat.PrivilegeHeat;
				item.PrivilegeIncidentCount = seat.PrivilegeIncidentCount;
				item.LastPrivilegeIncidentYear = seat.LastPrivilegeIncidentYear;
				item.PrivilegeSummary = BuildPrivilegeSummary(seat);
				item.SectRelation = XjCenturyAnnalsBuilder.ResolveSectRelation(seat, Math.Max(1, worldYear - 99));
			}
			else if (item.SectId <= 0L)
			{
				item.Responsibility = item.GoverningCityCount > 0 ? "城镇望族" : "家族自立";
			}
			familyNames[item.FamilyId] = item.Name;
		}
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexFamilyItem item = items[i];
			item.IsMigrationBranch = XjFamilyMemberLedger.IsMigrationBranchFamily(item.FamilyId);
			if (XjFamilyMemberLedger.TryGetBranchSourceFamilyId(item.FamilyId, out long sourceFamilyId))
			{
				item.SourceFamilyId = sourceFamilyId;
				item.SourceFamilyName = familyNames.TryGetValue(sourceFamilyId, out string sourceName)
					? sourceName
					: XjFamilyDisplayNameResolver.Resolve(sourceFamilyId);
			}
			if (XjFamilyMemberLedger.TryGetFamilyOriginCityId(item.FamilyId, out long originCityId))
			{
				item.OriginCityId = originCityId;
				item.OriginCityName = cityNames.TryGetValue(originCityId, out string originCityName)
					? originCityName
					: "城镇#" + originCityId.ToString(CultureInfo.InvariantCulture);
			}
			IReadOnlyList<XjFamilyMemberLedgerEntry> familyEntries = XjFamilyMemberLedger.ReadFamilyEntries(item.FamilyId);
			ApplyLiveFamilyRealmProjection(item, familyEntries);
			ApplyFamilyPoliticalProjection(item, familyEntries);
			ApplyFamilyHeritageProjection(item, familyEntries);
		}
		items.RemoveAll(item => item == null
			|| item.AliveCount <= 0
			|| item.CultivatorCount <= 0 && item.HistoricalCultivatorCount <= 0);
		items.Sort((left, right) =>
		{
			int high = right.JinDanCount.CompareTo(left.JinDanCount);
			if (high != 0) return high;
			high = right.ZiFuCount.CompareTo(left.ZiFuCount);
			if (high != 0) return high;
			high = right.HighestRealmOrder.CompareTo(left.HighestRealmOrder);
			if (high != 0) return high;
			high = right.HistoricalHighestRealmOrder.CompareTo(left.HistoricalHighestRealmOrder);
			if (high != 0) return high;
			high = right.CultivatorCount.CompareTo(left.CultivatorCount);
			if (high != 0) return high;
			int alive = right.AliveCount.CompareTo(left.AliveCount);
			return alive != 0 ? alive : left.FamilyId.CompareTo(right.FamilyId);
		});
	}

private static void ApplyLiveFamilyRealmProjection(
	XjCodexFamilyItem item,
	IReadOnlyList<XjFamilyMemberLedgerEntry> entries)
{
	if (item == null || item.FamilyId <= 0L || entries == null || entries.Count == 0) return;
	item.Members.Clear();

	int alive = 0;
	int cultivators = 0;
	int historicalCultivators = 0;
	int ziFu = 0;
	int jinDan = 0;
	int highestOrder = 0;
	int historicalHighestOrder = 0;
	string highestRealm = string.Empty;
	string historicalHighestRealm = string.Empty;
	string representative = string.Empty;
	long representativeActorId = 0L;
	int representativeOrder = -1;
	float representativePower = -1f;
	int representativeGeneration = int.MaxValue;
	long representativeIdForTie = long.MaxValue;
	for (int i = 0; i < entries.Count; i++)
	{
		XjFamilyMemberLedgerEntry entry = entries[i];
		if (!entry.Found) continue;
		int historicalOrder = XjRealmHelper.GetOrder(entry.RealmId);
		if (historicalOrder > 0) historicalCultivators++;
		if (historicalOrder > historicalHighestOrder)
		{
			historicalHighestOrder = historicalOrder;
			historicalHighestRealm = string.IsNullOrWhiteSpace(entry.RealmDisplay)
				? XjRealmHelper.GetDisplayName(entry.RealmId)
				: entry.RealmDisplay;
		}
		if (!entry.IsAlive) continue;
		alive++;
		string realmId = entry.RealmId;
		string realmDisplay = entry.RealmDisplay;
		string actorName = entry.Name;
		Actor actor = null;
		float power = 0f;
		int recordedOrder = XjRealmHelper.GetOrder(realmId);
		bool needsLiveCultivationProjection = item.CultivatorCount > 0 || recordedOrder > 0;
		bool live = needsLiveCultivationProjection
			&& entry.ActorId > 0L
			&& XjScheduler.ResolveActor(entry.ActorId, out actor)
			&& actor?.data != null
			&& actor.isAlive();
		if (live)
		{
			realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			int liveOrder = XjRealmHelper.GetOrder(realmId);
			realmDisplay = BuildLiveRealmDisplay(actor, realmId, liveOrder >= representativeOrder, out power);
			actorName = SafeActorName(actor);
		}

		int order = XjRealmHelper.GetOrder(realmId);
		if (order > 0) cultivators++;
		if (order > 0 && item.Members.Count < MaxSnapshotFamilyMembers)
		{
			XjCodexFamilyMemberItem member = new XjCodexFamilyMemberItem
			{
				ActorId = entry.ActorId,
				Name = actorName ?? string.Empty,
				Realm = string.IsNullOrWhiteSpace(realmDisplay) ? XjRealmHelper.GetDisplayName(realmId) : realmDisplay,
				DaoTu = live && XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu) ? daoTu ?? string.Empty : string.Empty,
				Role = "家族修士",
				RealmOrder = order
			};
			if (live)
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
				member.Aptitude = XjRankSortSystem.GetAptitudeName(aptitude);
				member.Age = (int)Math.Floor(Math.Max(0f, actor.getAge()));
				member.CraftSummary = ResolveFamilyMemberCraftSummary(actor);

				// 家族修士页不再读取慧光、命数、余寿、整套五品功法或求金法。
				// 仅筑基及以上读取已有修炼快照中的仙基数量。
				if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZhuJi))
				{
					member.XianJiCount = XjActorCultivationSnapshotBuilder.Build(actor).XianJiCount;
				}
				member.KeySummary = BuildFamilyMemberKeySummary(member);
			}
			item.Members.Add(member);
		}
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.JinDan)) jinDan++;
		else if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) ziFu++;
		if (order > highestOrder)
		{
			highestOrder = order;
			highestRealm = string.IsNullOrWhiteSpace(realmDisplay) ? XjRealmHelper.GetDisplayName(realmId) : realmDisplay;
		}

		if (live && (order > XjRealmHelper.GetOrder(XjRealmIds.ZhuJi) || IsStrictZhuJiLate(actor)))
		{
			item.QualifiedHighRealmCount++;
			int qualifiedScore = order >= XjRealmHelper.GetOrder(XjRealmIds.JinDan) ? 40
				: order >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu) ? 8 : 1;
			item.QualifiedHighRealmScore += qualifiedScore;
			if (order > item.StrongestQualifiedRealmOrder)
			{
				item.StrongestQualifiedRealmOrder = order;
				item.StrongestQualifiedRealm = string.IsNullOrWhiteSpace(realmDisplay) ? XjRealmHelper.GetDisplayName(realmId) : realmDisplay;
			}
		}

		int generation = entry.Generation > 0 ? entry.Generation : int.MaxValue - 1;
		if (representativeActorId <= 0L
			|| order > representativeOrder
			|| order == representativeOrder && power > representativePower
			|| order == representativeOrder && Math.Abs(power - representativePower) < 0.01f && generation < representativeGeneration
			|| order == representativeOrder && Math.Abs(power - representativePower) < 0.01f && generation == representativeGeneration && entry.ActorId < representativeIdForTie)
		{
			representative = actorName;
			representativeActorId = entry.ActorId;
			representativeOrder = order;
			representativePower = power;
			representativeGeneration = generation;
			representativeIdForTie = entry.ActorId;
		}
	}

	item.AliveCount = alive;
	item.CultivatorCount = cultivators;
	item.HistoricalCultivatorCount = historicalCultivators;
	item.ZiFuCount = ziFu;
	item.JinDanCount = jinDan;
	item.HighestRealmOrder = highestOrder;
	item.HistoricalHighestRealmOrder = historicalHighestOrder;
	item.HistoricalHighestRealm = historicalHighestRealm;
	item.LineageState = cultivators > 0 ? "修脉在兴" : historicalCultivators > 0 ? "衰微余脉" : "凡俗未兴";
	item.HighestRealm = !string.IsNullOrWhiteSpace(highestRealm)
		? highestRealm
		: historicalCultivators > 0 ? "凡俗余脉" : alive > 0 ? "凡俗" : string.Empty;
	if (!string.IsNullOrWhiteSpace(representative)) item.Representative = representative;
	if (representativeActorId > 0L) item.RepresentativeActorId = representativeActorId;
	item.PillarActorId = item.RepresentativeActorId;
	item.PillarName = item.Representative;
	item.PillarRealm = item.HighestRealm;
	item.Members.Sort(CompareFamilyMembers);
}

private static string ResolveFamilyMemberCraftSummary(Actor actor)
{
	if (XjCraftTraitRules.CanPracticeAlchemy(actor)) return "炼丹";
	if (XjCraftTraitRules.CanRefineArtifacts(actor)) return "炼器";
	if (XjCraftTraitRules.CanPracticeTalismans(actor)) return "制符";
	if (XjCraftTraitRules.CanPracticeFormations(actor)) return "阵法";
	return "无";
}

private static string BuildFamilyMemberKeySummary(XjCodexFamilyMemberItem member)
{
	if (member == null) return string.Empty;
	List<string> parts = new List<string>(2);
	if (member.XianJiCount > 0) parts.Add("仙基" + member.XianJiCount.ToString(CultureInfo.InvariantCulture));
	if (!string.IsNullOrWhiteSpace(member.CraftSummary) && member.CraftSummary != "无") parts.Add(member.CraftSummary);
	return parts.Count == 0 ? "暂无关键传承" : string.Join(" · ", parts);
}

private static int CompareFamilyMembers(XjCodexFamilyMemberItem left, XjCodexFamilyMemberItem right)
{
	int realm = (right?.RealmOrder ?? -1).CompareTo(left?.RealmOrder ?? -1);
	if (realm != 0) return realm;
	int name = string.Compare(left?.Name, right?.Name, StringComparison.Ordinal);
	return name != 0 ? name : (left?.ActorId ?? long.MaxValue).CompareTo(right?.ActorId ?? long.MaxValue);
}

private static string BuildLiveRealmDisplay(
	Actor actor,
	string realmId,
	bool calculatePower,
	out float power)
	{
		power = 0f;
		string normalizedRealmId = XjRealmHelper.NormalizeId(realmId);
		if (actor?.data == null || string.IsNullOrWhiteSpace(normalizedRealmId)) return string.Empty;
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int jinDanYiXiang);
		if (calculatePower)
		{
			power = XjRankMetrics.CalculatePower(
				actor,
				normalizedRealmId,
				snapshot.ZhenYuan,
				snapshot.XianJiCount,
				Math.Max(0, jinDanYiXiang));
		}
		string display = XjDaoXingStageRules.FormatDisplay(
			normalizedRealmId,
			snapshot.ZhenYuan,
			snapshot.XianJiCount,
			Math.Max(0, jinDanYiXiang));
		return string.IsNullOrWhiteSpace(display) ? XjRealmHelper.GetDisplayName(normalizedRealmId) : display;
	}

private static void ApplyFamilyPoliticalProjection(
	XjCodexFamilyItem item,
	IReadOnlyList<XjFamilyMemberLedgerEntry> entries)
	{
		if (item == null || item.FamilyId <= 0L) return;
		if (entries.Count == 0)
		{
			item.ClanLeaderName = EmptyText(item.Representative, "暂无");
			item.ClanLeaderActorId = item.RepresentativeActorId;
			item.HeirName = "未立";
			item.FounderTitle = item.IsMigrationBranch ? "分支始祖" : "始祖";
			item.CityOfficeSummary = BuildCityOfficeSummary(item);
			item.InfluenceScore = CalculateFamilyInfluence(item);
			item.BiographySummary = BuildFamilyBiographySummary(item);
			return;
		}

		XjFamilyMemberLedgerEntry ancestor = SelectAncestor(entries);
		XjFamilyMemberLedgerEntry leader = FindLivingFamilyMember(entries, item.ClanLeaderActorId);
		if (!leader.Found) leader = SelectPoliticalHead(entries, 0L);
		XjFamilyMemberLedgerEntry heir = SelectPoliticalHead(entries, leader.ActorId);
		item.AncestorName = ancestor.Found ? ancestor.Name : string.Empty;
		item.AncestorActorId = ancestor.Found ? ancestor.ActorId : 0L;
		item.FounderTitle = item.IsMigrationBranch ? "分支始祖" : "始祖";
		item.ClanLeaderName = leader.Found ? leader.Name : EmptyText(item.Representative, "暂无");
		item.ClanLeaderActorId = leader.Found ? leader.ActorId : item.RepresentativeActorId;
		item.HeirName = heir.Found ? heir.Name : "未立";
		item.HeirActorId = heir.Found ? heir.ActorId : 0L;
		if (item.RepresentativeActorId <= 0L)
		{
			item.RepresentativeActorId = item.ClanLeaderActorId > 0L ? item.ClanLeaderActorId : item.AncestorActorId;
		}
		item.CityOfficeSummary = BuildCityOfficeSummary(item);
		item.InfluenceScore = CalculateFamilyInfluence(item);
		item.BiographySummary = BuildFamilyBiographySummary(item);
	}

private static void ApplyFamilyHeritageProjection(
	XjCodexFamilyItem item,
	IReadOnlyList<XjFamilyMemberLedgerEntry> entries)
{
	if (item == null || item.FamilyId <= 0L) return;
	item.FamilyTreasureSummary = XjFamilyHeritageProjection.ResolveClanTreasure(item.FamilyId);
	if (item.SupportedActorId <= 0L || entries == null) return;
	for (int i = 0; i < entries.Count; i++)
	{
		XjFamilyMemberLedgerEntry entry = entries[i];
		if (!entry.Found || !entry.IsAlive || entry.ActorId != item.SupportedActorId) continue;
		item.SupportedActorName = entry.Name ?? string.Empty;
		if (XjScheduler.ResolveActor(entry.ActorId, out Actor actor) && actor?.data != null && actor.isAlive())
		{
			item.SupportedActorName = SafeActorName(actor);
		}
		return;
	}
	item.SupportedActorId = 0L;
	item.SupportedActorName = string.Empty;
	item.SupportPurpose = string.Empty;
	item.SupportedSinceYear = 0;
}

private static string BuildPrivilegeSummary(XjSectFamilySeatArchiveRecord seat)
	{
		if (seat == null || seat.PrivilegeHeat < 25f)
		{
			return string.Empty;
		}
		string level = seat.PrivilegeHeat >= 80f ? "权势坐大"
			: seat.PrivilegeHeat >= 55f ? "门第势重"
			: "声势渐盛";
		string year = seat.LastPrivilegeIncidentYear > 0
			? "，最近纪事：" + seat.LastPrivilegeIncidentYear.ToString(CultureInfo.InvariantCulture) + "年"
			: string.Empty;
		return level + "（" + Math.Round(seat.PrivilegeHeat).ToString(CultureInfo.InvariantCulture) + "）" + year;
	}

private static XjFamilyMemberLedgerEntry SelectAncestor(IReadOnlyList<XjFamilyMemberLedgerEntry> entries)
	{
		XjFamilyMemberLedgerEntry best = default;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyMemberLedgerEntry entry = entries[i];
			if (!entry.Found) continue;
			if (!best.Found
				|| CompareAncestor(entry, best) < 0)
			{
				best = entry;
			}
		}
		return best;
	}

private static int CompareAncestor(in XjFamilyMemberLedgerEntry left, in XjFamilyMemberLedgerEntry right)
	{
		int leftGeneration = left.Generation > 0 ? left.Generation : int.MaxValue;
		int rightGeneration = right.Generation > 0 ? right.Generation : int.MaxValue;
		int byGeneration = leftGeneration.CompareTo(rightGeneration);
		if (byGeneration != 0) return byGeneration;
		int leftBirth = left.BirthYear > 0 ? left.BirthYear : int.MaxValue;
		int rightBirth = right.BirthYear > 0 ? right.BirthYear : int.MaxValue;
		int byBirth = leftBirth.CompareTo(rightBirth);
		return byBirth != 0 ? byBirth : left.ActorId.CompareTo(right.ActorId);
	}

private static XjFamilyMemberLedgerEntry FindLivingFamilyMember(
	IReadOnlyList<XjFamilyMemberLedgerEntry> entries,
	long actorId)
	{
		if (actorId <= 0L) return default;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyMemberLedgerEntry entry = entries[i];
			if (entry.Found && entry.IsAlive && entry.ActorId == actorId) return entry;
		}
		return default;
	}

private static XjFamilyMemberLedgerEntry SelectPoliticalHead(IReadOnlyList<XjFamilyMemberLedgerEntry> entries, long excludedActorId)
	{
		XjFamilyMemberLedgerEntry best = default;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyMemberLedgerEntry entry = entries[i];
			if (!entry.Found || !entry.IsAlive || entry.ActorId == excludedActorId) continue;
			if (!best.Found || ComparePoliticalHead(entry, best) < 0) best = entry;
		}
		return best;
	}

private static int ComparePoliticalHead(in XjFamilyMemberLedgerEntry left, in XjFamilyMemberLedgerEntry right)
	{
		int byRealm = XjFamilyMemberLedger.GetRealmOrder(right.RealmId).CompareTo(XjFamilyMemberLedger.GetRealmOrder(left.RealmId));
		if (byRealm != 0) return byRealm;
		int leftGeneration = left.Generation > 0 ? left.Generation : int.MaxValue;
		int rightGeneration = right.Generation > 0 ? right.Generation : int.MaxValue;
		int byGeneration = leftGeneration.CompareTo(rightGeneration);
		if (byGeneration != 0) return byGeneration;
		int leftBirth = left.BirthYear > 0 ? left.BirthYear : int.MaxValue;
		int rightBirth = right.BirthYear > 0 ? right.BirthYear : int.MaxValue;
		int byBirth = leftBirth.CompareTo(rightBirth);
		return byBirth != 0 ? byBirth : left.ActorId.CompareTo(right.ActorId);
	}

private static string BuildCityOfficeSummary(XjCodexFamilyItem item)
	{
		if (item.GoverningCityCount > 0)
		{
			return item.SectId > 0L
				? "治理" + item.GoverningCityCount.ToString(CultureInfo.InvariantCulture) + "城"
				: "望族" + item.GoverningCityCount.ToString(CultureInfo.InvariantCulture) + "城";
		}
		if (!string.IsNullOrWhiteSpace(item.VoiceTier) && item.VoiceTier != XjSectVoiceTier.Ordinary)
		{
			return item.VoiceTier;
		}
		return item.SectId > 0L ? "宗门附属家族" : "地方家族";
	}

private static float CalculateFamilyInfluence(XjCodexFamilyItem item)
	{
		if (item == null) return 0f;
		return Math.Max(0f,
			item.VoiceScore
			+ item.GoverningCityCount * 25f
			+ item.ZiFuCount * 35f
			+ item.JinDanCount * 120f
			+ item.CultivatorCount * 3f
			+ item.ContributionScore * 0.4f
			+ item.CraftScore * 0.6f);
	}

private static string BuildFamilyBiographySummary(XjCodexFamilyItem item)
	{
		List<string> parts = new List<string>(5);
		if (!string.IsNullOrWhiteSpace(item.AncestorName)) parts.Add(item.FounderTitle + "：" + item.AncestorName);
		if (!string.IsNullOrWhiteSpace(item.ClanLeaderName) && item.ClanLeaderName != "暂无") parts.Add("家主：" + item.ClanLeaderName);
		if (!string.IsNullOrWhiteSpace(item.HeirName) && item.HeirName != "未立") parts.Add("继承人：" + item.HeirName);
		if (item.IsMigrationBranch) parts.Add("由主家迁城别立");
		if (item.GoverningCityCount > 0) parts.Add((item.SectId > 0L ? "治理" : "望族") + item.GoverningCityCount.ToString(CultureInfo.InvariantCulture) + "城");
		return parts.Count == 0 ? "族志未详" : string.Join("；", parts);
	}
}

