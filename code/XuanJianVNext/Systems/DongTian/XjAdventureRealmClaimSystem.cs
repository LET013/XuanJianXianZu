using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.DongTian;

internal static class XjAdventureRealmClaimSystem
{
	private const int DefaultClaimWindowYears = 3;
	private const int ContestDurationYears = 2;
	private const int ContestCooldownYears = 5;
	private const int MaxContestingActors = 3;
	private const int MaxActiveContests = 2;
	private const int MaxArchivedClaimRecords = 512;
	private static readonly Dictionary<string, XjAdventureRealmClaimArchiveRecord> ByRecordId = new Dictionary<string, XjAdventureRealmClaimArchiveRecord>(StringComparer.Ordinal);
	private static readonly HashSet<string> PendingClaimRecordIds = new HashSet<string>(StringComparer.Ordinal);
	private static readonly HashSet<string> ActiveContestRecordIds = new HashSet<string>(StringComparer.Ordinal);

	internal static bool HasAnnualWork => PendingClaimRecordIds.Count > 0 || ActiveContestRecordIds.Count > 0;

	internal static void OnRealmCreated(in XjDongTianRecord realm)
	{
		if (!realm.Found || string.IsNullOrWhiteSpace(realm.RecordId)) return;
		string id = realm.RecordId.Trim();
		if (ByRecordId.ContainsKey(id)) return;
		ResolveAnchorTerritory(realm.AnchorCityId, out long kingdomId, out string kingdomName, out long sectId, out string sectName);
		XjAdventureRealmClaimArchiveRecord claim = new XjAdventureRealmClaimArchiveRecord
		{
			RecordId = id, DisplayName = realm.DisplayName ?? string.Empty,
			State = sectId > 0L || kingdomId > 0L ? XjAdventureRealmClaimState.ClaimWindow : XjAdventureRealmClaimState.Discovered,
			AnchorCityId = Math.Max(0L, realm.AnchorCityId), AnchorCityName = realm.AnchorCityName ?? string.Empty,
			AnchorTileX = realm.AnchorTileX, AnchorTileY = realm.AnchorTileY,
			ClaimKingdomId = kingdomId, ClaimKingdomName = kingdomName, ClaimSectId = sectId, ClaimSectName = sectName,
			GuardianFamilyId = 0L, GuardianFamilyName = string.Empty, DiscovererActorId = Math.Max(0L, realm.AnchorActorId), DiscovererName = realm.AnchorActorName ?? string.Empty,
			DiscoveredYear = Math.Max(0, realm.CreatedYear), ClaimDueYear = Math.Max(0, realm.CreatedYear) + DefaultClaimWindowYears, RuntimeVersion = 1
		};
		ByRecordId.Add(id, claim);
		if (string.Equals(claim.State, XjAdventureRealmClaimState.ClaimWindow, StringComparison.Ordinal)) PendingClaimRecordIds.Add(id);
		MarkChanged();
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.SecretRealm, claim.DisplayName + "现世", BuildDiscoveryHistoryText(claim), 3,
			sectId: claim.ClaimSectId, cityId: claim.AnchorCityId, year: claim.DiscoveredYear,
			locationX: claim.AnchorTileX, locationY: claim.AnchorTileY, eventType: "AdventureRealmTerritoryDiscovered",
			visibilityFlags: (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate
				| (claim.ClaimSectId > 0L ? XjHistoryVisibility.Sect : XjHistoryVisibility.None)));
	}

	internal static void TickYear(int currentYear)
	{
		int year = Math.Max(0, currentYear);
		ResolveDueClaims(year);
		ResolveDueContests(year);
	}

	internal static void OnExplorerReserved(string recordId, Actor explorer, int currentYear)
	{
		if (string.IsNullOrWhiteSpace(recordId) || explorer?.data == null || !explorer.isAlive()
			|| !ByRecordId.TryGetValue(recordId.Trim(), out XjAdventureRealmClaimArchiveRecord claim)
			|| string.Equals(claim.State, XjAdventureRealmClaimState.Closed, StringComparison.Ordinal)) return;
		long actorId = ((BaseSystemData)explorer.data).id;
		long explorerSectId = ReadActorSectId(explorer);
		if (actorId <= 0L || explorerSectId <= 0L || explorerSectId == claim.ClaimSectId) return;
		if (currentYear < claim.ContestCooldownUntilYear || (ActiveContestRecordIds.Count >= MaxActiveContests && !ActiveContestRecordIds.Contains(claim.RecordId))) return;
		if (string.Equals(claim.State, XjAdventureRealmClaimState.Contested, StringComparison.Ordinal))
		{
			if (claim.ContestingSectId == explorerSectId && !claim.ContestingActorIds.Contains(actorId) && claim.ContestingActorIds.Count < MaxContestingActors)
			{
				claim.ContestingActorIds.Add(actorId);
				claim.RuntimeVersion++;
				MarkChanged();
			}
			return;
		}
		if (!XjSectRepository.TryGetBySectId(explorerSectId, out XjSectArchiveRecord challengerSect)) return;
		claim.State = XjAdventureRealmClaimState.Contested;
		claim.ContestingSectId = explorerSectId;
		claim.ContestingSectName = challengerSect.Name ?? string.Empty;
		claim.ContestingKingdomId = challengerSect.KingdomId;
		claim.ContestingKingdomName = challengerSect.PredecessorKingdomName ?? string.Empty;
		claim.ContestingActorIds.Clear();
		claim.ContestingActorIds.Add(actorId);
		claim.ContestStartedYear = currentYear;
		claim.ContestDueYear = currentYear + ContestDurationYears;
		claim.RuntimeVersion++;
		ActiveContestRecordIds.Add(claim.RecordId);
		MarkChanged();
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.SecretRealm, claim.DisplayName + "争夺开启",
			(claim.ContestingSectName.Length > 0 ? claim.ContestingSectName : "敌宗") + "修士实际进入洞天附近，对既有属地主张发起争夺。", 4,
			actorId: actorId, actorName: SafeActorName(explorer), sectId: explorerSectId, cityId: claim.AnchorCityId, year: currentYear,
			locationX: claim.AnchorTileX, locationY: claim.AnchorTileY, eventType: "AdventureRealmContestStarted",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Sect | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate));
	}

	private static void ResolveDueClaims(int year)
	{
		if (PendingClaimRecordIds.Count == 0) return;
		List<string> due = new List<string>();
		foreach (string id in PendingClaimRecordIds)
		{
			if (!ByRecordId.TryGetValue(id, out XjAdventureRealmClaimArchiveRecord claim) || claim == null
				|| !string.Equals(claim.State, XjAdventureRealmClaimState.ClaimWindow, StringComparison.Ordinal) || year >= claim.ClaimDueYear) due.Add(id);
		}
		for (int i = 0; i < due.Count; i++)
		{
			string id = due[i];
			PendingClaimRecordIds.Remove(id);
			if (!ByRecordId.TryGetValue(id, out XjAdventureRealmClaimArchiveRecord claim) || claim == null
				|| !string.Equals(claim.State, XjAdventureRealmClaimState.ClaimWindow, StringComparison.Ordinal) || year < claim.ClaimDueYear) continue;
			claim.GuardianFamilyId = 0L;
			claim.GuardianFamilyName = string.Empty;
			claim.GuardianActorIds.Clear();
			claim.State = XjAdventureRealmClaimState.Resolved;
			claim.RuntimeVersion++;
			XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.SecretRealm, claim.DisplayName + "属地主张落定", BuildClaimResolvedHistoryText(claim), 2,
				sectId: claim.ClaimSectId, cityId: claim.AnchorCityId, year: year,
				locationX: claim.AnchorTileX, locationY: claim.AnchorTileY, eventType: "AdventureRealmClaimResolved",
				visibilityFlags: (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate
					| (claim.ClaimSectId > 0L ? XjHistoryVisibility.Sect : XjHistoryVisibility.None)));
		}
		MarkChanged();
	}

	private static void ResolveDueContests(int year)
	{
		if (ActiveContestRecordIds.Count == 0) return;
		List<string> due = new List<string>();
		foreach (string id in ActiveContestRecordIds)
		{
			if (!ByRecordId.TryGetValue(id, out XjAdventureRealmClaimArchiveRecord claim) || claim == null
				|| !string.Equals(claim.State, XjAdventureRealmClaimState.Contested, StringComparison.Ordinal) || year >= claim.ContestDueYear) due.Add(id);
		}
		for (int i = 0; i < due.Count; i++)
		{
			string id = due[i];
			ActiveContestRecordIds.Remove(id);
			if (!ByRecordId.TryGetValue(id, out XjAdventureRealmClaimArchiveRecord claim) || claim == null
				|| !string.Equals(claim.State, XjAdventureRealmClaimState.Contested, StringComparison.Ordinal) || year < claim.ContestDueYear) continue;
			int incumbentScore = ScoreSectRepresentatives(claim.ClaimSectId);
			int challengerScore = ScoreActors(claim.ContestingActorIds);
			bool challengerWins = challengerScore > incumbentScore;
			if (challengerWins)
			{
				claim.ClaimSectId = claim.ContestingSectId;
				claim.ClaimSectName = claim.ContestingSectName;
				claim.ClaimKingdomId = claim.ContestingKingdomId;
				claim.ClaimKingdomName = claim.ContestingKingdomName;
				claim.WinnerSectId = claim.ContestingSectId;
			}
			else claim.WinnerSectId = claim.ClaimSectId;
			claim.ContestSummary = challengerWins ? "挑战方凭实际到场高境战力夺得剩余探索主张" : "属地方守住剩余探索主张";
			claim.State = XjAdventureRealmClaimState.Resolved;
			claim.ContestCooldownUntilYear = year + ContestCooldownYears;
			claim.ContestingActorIds.Clear();
			claim.ContestingSectId = 0L;
			claim.ContestingSectName = string.Empty;
			claim.ContestDueYear = 0;
			claim.RuntimeVersion++;
			XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.SecretRealm, claim.DisplayName + "争夺落定", claim.ContestSummary, 4,
				sectId: claim.WinnerSectId, cityId: claim.AnchorCityId, year: year,
				locationX: claim.AnchorTileX, locationY: claim.AnchorTileY, eventType: "AdventureRealmContestResolved",
				visibilityFlags: (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate
					| (claim.WinnerSectId > 0L ? XjHistoryVisibility.Sect : XjHistoryVisibility.None)));
		}
		MarkChanged();
	}

	internal static void OnRealmClosed(string recordId, int currentYear)
	{
		if (string.IsNullOrWhiteSpace(recordId) || !ByRecordId.TryGetValue(recordId.Trim(), out XjAdventureRealmClaimArchiveRecord claim)
			|| string.Equals(claim.State, XjAdventureRealmClaimState.Closed, StringComparison.Ordinal)) return;
		PendingClaimRecordIds.Remove(claim.RecordId);
		ActiveContestRecordIds.Remove(claim.RecordId);
		claim.State = XjAdventureRealmClaimState.Closed;
		claim.ClosedYear = Math.Max(0, currentYear);
		claim.RuntimeVersion++;
		PruneClosedArchive();
		MarkChanged();
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.SecretRealm, claim.DisplayName + "关闭", BuildClosureHistoryText(claim), 2,
			sectId: claim.ClaimSectId, cityId: claim.AnchorCityId, year: claim.ClosedYear,
			locationX: claim.AnchorTileX, locationY: claim.AnchorTileY, eventType: "AdventureRealmClosed",
			visibilityFlags: (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate
				| (claim.ClaimSectId > 0L ? XjHistoryVisibility.Sect : XjHistoryVisibility.None)));
	}

	internal static IReadOnlyList<XjAdventureRealmClaimArchiveRecord> ReadAll()
	{
		List<XjAdventureRealmClaimArchiveRecord> result = new List<XjAdventureRealmClaimArchiveRecord>(ByRecordId.Count);
		foreach (XjAdventureRealmClaimArchiveRecord record in ByRecordId.Values) result.Add(Clone(record));
		result.Sort((a, b) => string.Compare(a.RecordId, b.RecordId, StringComparison.Ordinal));
		return result;
	}

	internal static bool TryRenameSectReferences(long sectId, string sectName)
	{
		if (sectId <= 0L || string.IsNullOrWhiteSpace(sectName) || ByRecordId.Count == 0)
		{
			return false;
		}

		bool changed = false;
		string normalized = sectName.Trim();
		foreach (XjAdventureRealmClaimArchiveRecord record in ByRecordId.Values)
		{
			if (record == null)
			{
				continue;
			}

			if (record.ClaimSectId == sectId
				&& !string.Equals(record.ClaimSectName ?? string.Empty, normalized, StringComparison.Ordinal))
			{
				record.ClaimSectName = normalized;
				changed = true;
			}

			if (record.ContestingSectId == sectId
				&& !string.Equals(record.ContestingSectName ?? string.Empty, normalized, StringComparison.Ordinal))
			{
				record.ContestingSectName = normalized;
				changed = true;
			}
		}

		if (changed)
		{
			MarkChanged();
		}
		return changed;
	}

	internal static void ExportArchiveRecords(List<XjAdventureRealmClaimArchiveRecord> target) { if (target == null) return; target.Clear(); foreach (var record in ReadAll()) target.Add(record); }
	internal static void ImportArchiveRecords(IReadOnlyList<XjAdventureRealmClaimArchiveRecord> source)
	{
		Clear();
		if (source != null) for (int i = 0; i < source.Count; i++)
		{
			XjAdventureRealmClaimArchiveRecord record = source[i];
			if (record == null || string.IsNullOrWhiteSpace(record.RecordId)) continue;
			XjAdventureRealmClaimArchiveRecord copy = Clone(record);
			copy.GuardianFamilyId = 0L;
			copy.GuardianFamilyName = string.Empty;
			copy.GuardianActorIds.Clear();
			if (string.Equals(copy.State, XjAdventureRealmClaimState.Guarded, StringComparison.Ordinal)) copy.State = XjAdventureRealmClaimState.Resolved;
			ByRecordId[copy.RecordId] = copy;
			if (string.Equals(copy.State, XjAdventureRealmClaimState.ClaimWindow, StringComparison.Ordinal)) PendingClaimRecordIds.Add(copy.RecordId);
			if (string.Equals(copy.State, XjAdventureRealmClaimState.Contested, StringComparison.Ordinal)) ActiveContestRecordIds.Add(copy.RecordId);
		}
		PruneClosedArchive();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.AdventureRealm);
	}
	internal static void Clear() { ByRecordId.Clear(); PendingClaimRecordIds.Clear(); ActiveContestRecordIds.Clear(); }

	private static void PruneClosedArchive()
	{
		if (ByRecordId.Count <= MaxArchivedClaimRecords) return;
		List<XjAdventureRealmClaimArchiveRecord> closed = new List<XjAdventureRealmClaimArchiveRecord>();
		foreach (XjAdventureRealmClaimArchiveRecord record in ByRecordId.Values)
		{
			if (record != null && string.Equals(record.State, XjAdventureRealmClaimState.Closed, StringComparison.Ordinal)) closed.Add(record);
		}
		closed.Sort((a, b) => a.ClosedYear != b.ClosedYear ? a.ClosedYear.CompareTo(b.ClosedYear) : string.Compare(a.RecordId, b.RecordId, StringComparison.Ordinal));
		int removeCount = Math.Min(closed.Count, ByRecordId.Count - MaxArchivedClaimRecords);
		for (int i = 0; i < removeCount; i++) ByRecordId.Remove(closed[i].RecordId);
	}

	private static int ScoreSectRepresentatives(long sectId)
	{
		if (sectId <= 0L) return 0;
		return ScoreActors(XjZongMenCultivatorCityIndex.GetActorIdsForSect(sectId));
	}

	private static int ScoreActors(IReadOnlyList<long> actorIds)
	{
		if (actorIds == null) return 0;
		List<int> scores = new List<int>();
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(actorIds[i], out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			int order = XjRealmHelper.GetOrder(realmId);
			scores.Add(order switch { 5 => 2000, 4 => 800, 3 => 300, 2 => 150, _ => 100 });
		}
		scores.Sort((a, b) => b.CompareTo(a));
		int total = 0;
		for (int i = 0; i < scores.Count && i < 3; i++) total += scores[i];
		return total;
	}

	private static long ReadActorSectId(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor);
	}

	private static void ResolveAnchorTerritory(long cityId, out long kingdomId, out string kingdomName, out long sectId, out string sectName)
	{
		kingdomId = 0L; kingdomName = string.Empty; sectId = 0L; sectName = string.Empty;
		if (!XjWorldLookupIndex.TryResolveCity(cityId, out City city) || city?.data == null) return;
		Kingdom kingdom = city.kingdom;
		if (kingdom?.data == null) return;
		kingdomId = kingdom.data.id; kingdomName = kingdom.name ?? string.Empty;
		if (XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect)) { sectId = sect.SectId; sectName = sect.Name ?? string.Empty; }
	}

	private static string BuildDiscoveryHistoryText(XjAdventureRealmClaimArchiveRecord c) => c.DisplayName + (c.ClaimSectId > 0L ? "位于" + (c.ClaimSectName.Length > 0 ? c.ClaimSectName : "某宗") + "疆域" : "现于无主之地") + "，进入属地主张确认期。";
	private static string BuildClaimResolvedHistoryText(XjAdventureRealmClaimArchiveRecord c) => c.DisplayName + "的属地主张确认期结束，由" + (c.ClaimSectName.Length > 0 ? c.ClaimSectName : "所在势力") + "取得优先探索权。";
	private static string BuildClosureHistoryText(XjAdventureRealmClaimArchiveRecord c) => c.DisplayName + "洞门闭合，残余灵机渐散；此前的属地归属与争夺也随之落定。";
	private static string SafeActorName(Actor actor) { try { return actor?.getName() ?? "未名修士"; } catch { return "未名修士"; } }
	private static void MarkChanged() { XjWorldArchiveSystem.MarkChanged(); XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.AdventureRealm | XjCodexDirtyFlags.History); }
	private static XjAdventureRealmClaimArchiveRecord Clone(XjAdventureRealmClaimArchiveRecord s)
	{
		return new XjAdventureRealmClaimArchiveRecord
		{
			SchemaVersion = s.SchemaVersion, RecordId = s.RecordId ?? string.Empty, DisplayName = s.DisplayName ?? string.Empty, State = s.State ?? XjAdventureRealmClaimState.Unclaimed,
			AnchorCityId = s.AnchorCityId, AnchorCityName = s.AnchorCityName ?? string.Empty, ClaimKingdomId = s.ClaimKingdomId, ClaimKingdomName = s.ClaimKingdomName ?? string.Empty,
			AnchorTileX = s.AnchorTileX, AnchorTileY = s.AnchorTileY,
			ClaimSectId = s.ClaimSectId, ClaimSectName = s.ClaimSectName ?? string.Empty, GuardianFamilyId = 0L, GuardianFamilyName = string.Empty,
			GuardianActorIds = new List<long>(), DiscovererActorId = s.DiscovererActorId,
			DiscovererName = s.DiscovererName ?? string.Empty, DiscoveredYear = s.DiscoveredYear, ClaimDueYear = s.ClaimDueYear,
			ContestingKingdomId = s.ContestingKingdomId, ContestingKingdomName = s.ContestingKingdomName ?? string.Empty, ContestingSectId = s.ContestingSectId,
			ContestingSectName = s.ContestingSectName ?? string.Empty, ContestingActorIds = s.ContestingActorIds == null ? new List<long>() : new List<long>(s.ContestingActorIds),
			ContestStartedYear = s.ContestStartedYear, ContestDueYear = s.ContestDueYear, ContestCooldownUntilYear = s.ContestCooldownUntilYear,
			WinnerSectId = s.WinnerSectId, ContestSummary = s.ContestSummary ?? string.Empty, ClosedYear = s.ClosedYear, RuntimeVersion = s.RuntimeVersion
		};
	}
}
