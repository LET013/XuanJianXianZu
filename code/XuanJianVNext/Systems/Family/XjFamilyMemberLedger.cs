using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Family;

internal readonly struct XjFamilyMemberLedgerEntry
{
	internal readonly bool Found;
	internal readonly long FamilyStableId;
	internal readonly long ActorId;
	internal readonly string Name;
	internal readonly int Generation;
	internal readonly string RealmId;
	internal readonly string RealmDisplay;
	internal readonly bool IsAlive;
	internal readonly int BirthYear;
	internal readonly int DeathYear;
	internal readonly string Source;

	internal XjFamilyMemberLedgerEntry(
		bool found,
		long familyStableId,
		long actorId,
		string name,
		int generation,
		string realmId,
		string realmDisplay,
		bool isAlive,
		int birthYear,
		int deathYear,
		string source)
	{
		Found = found;
		FamilyStableId = familyStableId < 0L ? 0L : familyStableId;
		ActorId = actorId < 0L ? 0L : actorId;
		Name = name ?? string.Empty;
		Generation = generation < 0 ? 0 : generation;
		RealmId = realmId ?? string.Empty;
		RealmDisplay = realmDisplay ?? string.Empty;
		IsAlive = isAlive;
		BirthYear = birthYear < 0 ? 0 : birthYear;
		DeathYear = deathYear < 0 ? 0 : deathYear;
		Source = source ?? string.Empty;
	}
}

internal readonly struct XjFamilyLedgerAggregate
{
	internal readonly long FamilyStableId;
	internal readonly string DisplayName;
	internal readonly int AliveCount;
	internal readonly int TotalCount;
	internal readonly int CultivatorCount;
	internal readonly int ZiFuCount;
	internal readonly int JinDanCount;
	internal readonly string HighestRealm;
	internal readonly int HighestRealmOrder;
	internal readonly string Representative;
	internal readonly long RepresentativeActorId;

	internal XjFamilyLedgerAggregate(long familyStableId, string displayName, int aliveCount, int totalCount,
		int cultivatorCount, int ziFuCount, int jinDanCount, string highestRealm, int highestRealmOrder, string representative, long representativeActorId)
	{
		FamilyStableId = familyStableId;
		DisplayName = displayName ?? string.Empty;
		AliveCount = Math.Max(0, aliveCount);
		TotalCount = Math.Max(0, totalCount);
		CultivatorCount = Math.Max(0, cultivatorCount);
		ZiFuCount = Math.Max(0, ziFuCount);
		JinDanCount = Math.Max(0, jinDanCount);
		HighestRealm = highestRealm ?? string.Empty;
		HighestRealmOrder = Math.Max(0, highestRealmOrder);
		Representative = representative ?? string.Empty;
		RepresentativeActorId = representativeActorId < 0L ? 0L : representativeActorId;
	}
}

internal static class XjFamilyMemberLedger
{
	internal const string SourceBranchCityMigration = "family.branch_city_migration";

	private static readonly Dictionary<long, XjFamilyMemberLedgerEntry> entriesByActorId = new Dictionary<long, XjFamilyMemberLedgerEntry>();
	private static readonly Dictionary<long, HashSet<long>> actorIdsByFamilyStableId = new Dictionary<long, HashSet<long>>();
	// The full ledger is retained for genealogy and total-member counts. Runtime
	// consumers almost always need living members only, so keep that hot path out
	// of the ever-growing historical membership set.
	private static readonly Dictionary<long, HashSet<long>> aliveActorIdsByFamilyStableId = new Dictionary<long, HashSet<long>>();
	private static readonly Dictionary<long, XjFamilyLedgerAggregate> aggregatesByFamilyStableId = new Dictionary<long, XjFamilyLedgerAggregate>();
	private static bool suppressAggregateRebuild;
	private static bool suppressPersistenceSignals;

	internal static bool TryGetByActorId(long actorId, out XjFamilyMemberLedgerEntry entry)
	{
		if (actorId <= 0L || !entriesByActorId.TryGetValue(actorId, out entry) || !entry.Found)
		{
			entry = default;
			return false;
		}

		return true;
	}

	internal static bool IsExplicitFamilyRelinkSource(string source)
	{
		return string.Equals(source, "family.reincarnation_link", StringComparison.Ordinal)
			|| IsBranchCityMigrationSource(source);
	}

	internal static string BuildBranchCityMigrationSource(long sourceFamilyStableId)
	{
		return sourceFamilyStableId > 0L ? SourceBranchCityMigration + ":" + sourceFamilyStableId : SourceBranchCityMigration;
	}

	private static bool IsBranchCityMigrationSource(string source)
	{
		string value = (source ?? string.Empty).Trim();
		return string.Equals(value, SourceBranchCityMigration, StringComparison.Ordinal)
			|| value.StartsWith(SourceBranchCityMigration + ":", StringComparison.Ordinal);
	}

	private static bool TryParseBranchSourceFamilyId(string source, out long sourceFamilyId)
	{
		sourceFamilyId = 0L;
		string value = (source ?? string.Empty).Trim();
		if (!value.StartsWith(SourceBranchCityMigration + ":", StringComparison.Ordinal)) return false;
		string raw = value.Substring(SourceBranchCityMigration.Length + 1).Trim();
		return long.TryParse(raw, out sourceFamilyId) && sourceFamilyId > 0L;
	}

	internal static bool UpsertConfirmed(Actor actor, in XjFamilyIdentity identity, string source)
	{
		if (actor?.data == null || !identity.Found || identity.FamilyStableIdValue <= 0L || identity.ActorId <= 0L)
		{
			return false;
		}

		string realmId = ResolveRealmId(actor);
		string name = string.IsNullOrWhiteSpace(actor.getName()) ? "未名族人" : actor.getName().Trim();
		return Upsert(new XjFamilyMemberLedgerEntry(
			true,
			identity.FamilyStableIdValue,
			identity.ActorId,
			name,
			identity.Generation,
			realmId,
			ResolveRealmDisplayName(actor, realmId),
			true,
			ResolveBirthYear(actor),
			0,
			source));
	}

	internal static bool UpsertFromDomainEvent(in XjFamilyDomainEvent domainEvent)
	{
		if (!domainEvent.Found || domainEvent.ActorId <= 0L || domainEvent.FamilyStableId <= 0L)
		{
			return false;
		}

		entriesByActorId.TryGetValue(domainEvent.ActorId, out XjFamilyMemberLedgerEntry existing);

		string realmId = string.IsNullOrWhiteSpace(domainEvent.RealmId)
			? existing.RealmId
			: NormalizeRealmId(domainEvent.RealmId);
		string name = string.IsNullOrWhiteSpace(domainEvent.ActorName) ? existing.Name : domainEvent.ActorName.Trim();
		int generation = existing.Generation > 0 ? existing.Generation : XjFamilyReadModel.Shared.GetGeneration(domainEvent.ActorId);
		return Upsert(new XjFamilyMemberLedgerEntry(
			true,
			domainEvent.FamilyStableId,
			domainEvent.ActorId,
			name,
			generation,
			realmId,
			ResolveRealmDisplayName(realmId),
			true,
			existing.BirthYear,
			0,
			string.IsNullOrWhiteSpace(domainEvent.Source) ? domainEvent.EventType : domainEvent.Source));
	}

	internal static bool MarkDead(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L || snapshot.FamilyStableId <= 0L)
		{
			return false;
		}

		entriesByActorId.TryGetValue(snapshot.ActorId, out XjFamilyMemberLedgerEntry existing);
		string realmId = string.IsNullOrWhiteSpace(snapshot.RealmId) ? existing.RealmId : NormalizeRealmId(snapshot.RealmId);
		string name = string.IsNullOrWhiteSpace(snapshot.Name) ? existing.Name : snapshot.Name.Trim();
		XjFamilyBloodlineAggregateCache.InvalidateFamily(snapshot.FamilyStableId);
		return Upsert(new XjFamilyMemberLedgerEntry(
			true,
			snapshot.FamilyStableId,
			snapshot.ActorId,
			string.IsNullOrWhiteSpace(name) ? "未名族人" : name,
			existing.Generation,
			realmId,
			ResolveRealmDisplayName(realmId),
			false,
			existing.BirthYear,
			snapshot.Year,
			"death.snapshot"));
	}

	internal static IReadOnlyList<XjFamilyMemberLedgerEntry> ReadFamilyAlive(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !aliveActorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return Array.Empty<XjFamilyMemberLedgerEntry>();
		}

		List<XjFamilyMemberLedgerEntry> result = new List<XjFamilyMemberLedgerEntry>(actorIds.Count);
		foreach (long actorId in actorIds)
		{
			if (entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry)
				&& entry.Found)
			{
				result.Add(entry);
			}
		}

		result.Sort(CompareEntries);
		return result;
	}

	internal static IReadOnlyList<XjFamilyMemberLedgerEntry> ReadFamilyEntries(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return Array.Empty<XjFamilyMemberLedgerEntry>();
		}

		List<XjFamilyMemberLedgerEntry> result = new List<XjFamilyMemberLedgerEntry>(actorIds.Count);
		foreach (long actorId in actorIds)
		{
			if (entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry)
				&& entry.Found)
			{
				result.Add(entry);
			}
		}

		result.Sort(CompareEntries);
		return result;
	}

	internal static int GetHistoricalHighestRealmOrder(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return 0;
		}
		int best = 0;
		foreach (long actorId in actorIds)
		{
			if (!entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry) || !entry.Found) continue;
			best = Math.Max(best, GetRealmOrder(entry.RealmId));
		}
		return best;
	}

	internal static IReadOnlyList<XjFamilyLedgerAggregate> ReadAggregateSnapshot()
	{
		if (aggregatesByFamilyStableId.Count == 0) return Array.Empty<XjFamilyLedgerAggregate>();
		List<XjFamilyLedgerAggregate> result = new List<XjFamilyLedgerAggregate>(aggregatesByFamilyStableId.Values);
		result.Sort((left, right) => left.FamilyStableId.CompareTo(right.FamilyStableId));
		return result;
	}

	internal static bool TryGetAggregate(long familyStableId, out XjFamilyLedgerAggregate aggregate)
	{
		if (familyStableId <= 0L || !aggregatesByFamilyStableId.TryGetValue(familyStableId, out aggregate))
		{
			aggregate = default;
			return false;
		}

		return aggregate.FamilyStableId == familyStableId;
	}

	internal static bool IsMigrationBranchFamily(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return false;
		}

		bool isBranch = false;
		foreach (long actorId in actorIds)
		{
			if (entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry)
				&& entry.Found
				&& IsBranchCityMigrationSource(entry.Source))
			{
				isBranch = true;
				break;
			}
			if (TryReadActorLong(actorId, XjActorDataKeys.XjFamilyBranchSourceFamilyId, out long sourceFamilyId)
				&& sourceFamilyId > 0L)
			{
				isBranch = true;
				break;
			}
		}

		return isBranch && !IsPromotedMainBranch(familyStableId);
	}

	internal static bool TryGetBranchSourceFamilyId(long familyStableId, out long sourceFamilyId)
	{
		sourceFamilyId = 0L;
		if (familyStableId <= 0L
			|| !actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return false;
		}

		foreach (long actorId in actorIds)
		{
			if (entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry)
				&& entry.Found
				&& TryParseBranchSourceFamilyId(entry.Source, out sourceFamilyId))
			{
				return !IsPromotedMainBranch(familyStableId, sourceFamilyId);
			}
			if (TryReadActorLong(actorId, XjActorDataKeys.XjFamilyBranchSourceFamilyId, out long value)
				&& value > 0L)
			{
				sourceFamilyId = value;
				return !IsPromotedMainBranch(familyStableId, sourceFamilyId);
			}
		}

		return false;
	}

	private static bool IsPromotedMainBranch(long familyStableId)
	{
		return TryGetRawBranchSourceFamilyId(familyStableId, out long sourceFamilyId)
			&& IsPromotedMainBranch(familyStableId, sourceFamilyId);
	}

	private static bool IsPromotedMainBranch(long familyStableId, long sourceFamilyId)
	{
		if (familyStableId <= 0L || sourceFamilyId <= 0L || familyStableId == sourceFamilyId)
		{
			return false;
		}

		if (TryGetAggregate(sourceFamilyId, out XjFamilyLedgerAggregate sourceAggregate)
			&& sourceAggregate.AliveCount > 0)
		{
			return false;
		}

		long promoted = 0L;
		int bestRealm = -1;
		int bestCultivators = -1;
		int bestAlive = -1;
		foreach (XjFamilyLedgerAggregate aggregate in aggregatesByFamilyStableId.Values)
		{
			if (aggregate.FamilyStableId <= 0L
				|| aggregate.AliveCount <= 0
				|| !TryGetRawBranchSourceFamilyId(aggregate.FamilyStableId, out long candidateSource)
				|| candidateSource != sourceFamilyId)
			{
				continue;
			}

			if (promoted <= 0L
				|| aggregate.HighestRealmOrder > bestRealm
				|| aggregate.HighestRealmOrder == bestRealm && aggregate.CultivatorCount > bestCultivators
				|| aggregate.HighestRealmOrder == bestRealm && aggregate.CultivatorCount == bestCultivators && aggregate.AliveCount > bestAlive
				|| aggregate.HighestRealmOrder == bestRealm && aggregate.CultivatorCount == bestCultivators && aggregate.AliveCount == bestAlive && aggregate.FamilyStableId < promoted)
			{
				promoted = aggregate.FamilyStableId;
				bestRealm = aggregate.HighestRealmOrder;
				bestCultivators = aggregate.CultivatorCount;
				bestAlive = aggregate.AliveCount;
			}
		}

		return promoted == familyStableId;
	}

	private static bool TryGetRawBranchSourceFamilyId(long familyStableId, out long sourceFamilyId)
	{
		sourceFamilyId = 0L;
		if (familyStableId <= 0L
			|| !actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return false;
		}

		foreach (long actorId in actorIds)
		{
			if (entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry)
				&& entry.Found
				&& TryParseBranchSourceFamilyId(entry.Source, out sourceFamilyId))
			{
				return true;
			}
			if (TryReadActorLong(actorId, XjActorDataKeys.XjFamilyBranchSourceFamilyId, out long value)
				&& value > 0L)
			{
				sourceFamilyId = value;
				return true;
			}
		}

		return false;
	}

	internal static bool TryGetFamilyOriginCityId(long familyStableId, out long cityId)
	{
		cityId = 0L;
		if (familyStableId <= 0L
			|| !actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return false;
		}

		foreach (long actorId in actorIds)
		{
			if (TryReadActorLong(actorId, XjActorDataKeys.XjFamilyOriginCityId, out long value)
				&& value > 0L)
			{
				cityId = value;
				return true;
			}
		}

		return false;
	}

	internal static void ExportArchiveRecords(List<XjWorldArchiveFamilyMemberRecord> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (XjFamilyMemberLedgerEntry entry in entriesByActorId.Values)
		{
			if (!entry.Found || entry.ActorId <= 0L || entry.FamilyStableId <= 0L)
			{
				continue;
			}

			records.Add(new XjWorldArchiveFamilyMemberRecord
			{
				FamilyStableId = entry.FamilyStableId,
				ActorId = entry.ActorId,
				Name = entry.Name,
				Generation = entry.Generation,
				RealmId = entry.RealmId,
				RealmDisplay = entry.RealmDisplay,
				IsAlive = entry.IsAlive,
				BirthYear = entry.BirthYear,
				DeathYear = entry.DeathYear,
				Source = entry.Source
			});
		}
	}

	private static bool TryReadActorLong(long actorId, string key, out long value)
	{
		value = 0L;
		return actorId > 0L
			&& !string.IsNullOrWhiteSpace(key)
			&& XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& actor?.data != null
			&& XjActorAccessor.TryGetLong(actor, key, out value);
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveFamilyMemberRecord> records)
	{
		Clear();
		if (records == null || records.Count == 0) return;
		suppressAggregateRebuild = true;
		suppressPersistenceSignals = true;
		try
		{
			for (int i = 0; i < records.Count; i++)
			{
				XjWorldArchiveFamilyMemberRecord record = records[i];
				if (record == null || record.FamilyStableId <= 0L || record.ActorId <= 0L) continue;
				string realmId = NormalizeRealmId(record.RealmId);
				Upsert(new XjFamilyMemberLedgerEntry(
					true, record.FamilyStableId, record.ActorId, record.Name, record.Generation, realmId,
					string.IsNullOrWhiteSpace(record.RealmDisplay) ? ResolveRealmDisplayName(realmId) : record.RealmDisplay,
					record.IsAlive, record.BirthYear, record.DeathYear, record.Source));
			}
		}
		finally
		{
			suppressAggregateRebuild = false;
			suppressPersistenceSignals = false;
			RebuildAllAggregates();
		}
	}

	internal static bool ReconcileFamilySurname(long familyStableId, string canonicalSurname)
	{
		if (familyStableId <= 0L
			|| string.IsNullOrWhiteSpace(canonicalSurname)
			|| !actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return false;
		}

		bool changed = false;
		foreach (long actorId in actorIds)
		{
			if (!entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry) || !entry.Found)
			{
				continue;
			}

			string nextName = XjFamilySurnamePolicy.RewriteDisplaySurname(entry.Name, canonicalSurname);
			if (string.IsNullOrWhiteSpace(nextName) || string.Equals(nextName, entry.Name, StringComparison.Ordinal))
			{
				continue;
			}

			entriesByActorId[actorId] = new XjFamilyMemberLedgerEntry(
				true, entry.FamilyStableId, entry.ActorId, nextName, entry.Generation,
				entry.RealmId, entry.RealmDisplay, entry.IsAlive, entry.BirthYear, entry.DeathYear, entry.Source);
			changed = true;
		}

		if (changed)
		{
			RebuildAggregate(familyStableId);
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family);
		}
		return changed;
	}

	internal static void Clear()
	{
		entriesByActorId.Clear();
		actorIdsByFamilyStableId.Clear();
		aliveActorIdsByFamilyStableId.Clear();
		aggregatesByFamilyStableId.Clear();
		suppressAggregateRebuild = false;
		suppressPersistenceSignals = false;
	}

	private static bool Upsert(in XjFamilyMemberLedgerEntry next)
	{
		if (!next.Found || next.ActorId <= 0L || next.FamilyStableId <= 0L)
		{
			return false;
		}

		if (entriesByActorId.TryGetValue(next.ActorId, out XjFamilyMemberLedgerEntry existing))
		{
			XjFamilyMemberLedgerEntry merged = Merge(existing, next);
			if (AreEquivalent(existing, merged))
			{
				return false;
			}

			RemoveActorFromFamilyIndex(next.ActorId, existing.FamilyStableId, existing.IsAlive);
			entriesByActorId[merged.ActorId] = merged;
			AddActorToFamilyIndex(merged.ActorId, merged.FamilyStableId, merged.IsAlive);
			if (!suppressAggregateRebuild)
			{
				RebuildAggregate(existing.FamilyStableId);
				if (merged.FamilyStableId != existing.FamilyStableId) RebuildAggregate(merged.FamilyStableId);
			}
			if (!suppressPersistenceSignals)
			{
				XjWorldArchiveSystem.MarkChanged();
				XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.World);
			}
			return true;
		}

		entriesByActorId[next.ActorId] = next;
		AddActorToFamilyIndex(next.ActorId, next.FamilyStableId, next.IsAlive);
		if (!suppressAggregateRebuild) RebuildAggregate(next.FamilyStableId);
		if (!suppressPersistenceSignals)
		{
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.World);
		}
		return true;
	}

	private static bool AreEquivalent(in XjFamilyMemberLedgerEntry left, in XjFamilyMemberLedgerEntry right)
	{
		return left.Found == right.Found
			&& left.FamilyStableId == right.FamilyStableId
			&& left.ActorId == right.ActorId
			&& string.Equals(left.Name, right.Name, StringComparison.Ordinal)
			&& left.Generation == right.Generation
			&& string.Equals(left.RealmId, right.RealmId, StringComparison.Ordinal)
			&& string.Equals(left.RealmDisplay, right.RealmDisplay, StringComparison.Ordinal)
			&& left.IsAlive == right.IsAlive
			&& left.BirthYear == right.BirthYear
			&& left.DeathYear == right.DeathYear
			&& string.Equals(left.Source, right.Source, StringComparison.Ordinal);
	}

	private static XjFamilyMemberLedgerEntry Merge(in XjFamilyMemberLedgerEntry existing, in XjFamilyMemberLedgerEntry next)
	{
		string nextRealm = PickHigherRealm(existing.RealmId, next.RealmId);
		string realmDisplay = PickRealmDisplay(existing, next, nextRealm);
		bool explicitRelink = IsExplicitFamilyRelinkSource(next.Source);
		long stableFamilyId = existing.FamilyStableId > 0L && !explicitRelink
			? existing.FamilyStableId
			: next.FamilyStableId;
		return new XjFamilyMemberLedgerEntry(
			true,
			stableFamilyId > 0L ? stableFamilyId : existing.FamilyStableId,
			next.ActorId,
			string.IsNullOrWhiteSpace(next.Name) ? existing.Name : next.Name,
			next.Generation > 0 ? next.Generation : existing.Generation,
			nextRealm,
			realmDisplay,
			next.IsAlive,
			next.BirthYear > 0 ? next.BirthYear : existing.BirthYear,
			next.DeathYear > 0 ? next.DeathYear : existing.DeathYear,
			SelectLedgerSource(existing.Source, next.Source));
	}

	private static string SelectLedgerSource(string existingSource, string nextSource)
	{
		if (IsBranchCityMigrationSource(nextSource)) return nextSource;
		if (IsBranchCityMigrationSource(existingSource)
			&& (string.IsNullOrWhiteSpace(nextSource)
				|| string.Equals(nextSource, "death.snapshot", StringComparison.Ordinal)
				|| string.Equals(nextSource, "family.refresh", StringComparison.Ordinal)))
		{
			return existingSource;
		}
		return string.IsNullOrWhiteSpace(nextSource) ? existingSource : nextSource;
	}

	private static string PickRealmDisplay(
		in XjFamilyMemberLedgerEntry existing,
		in XjFamilyMemberLedgerEntry next,
		string selectedRealm)
	{
		string normalizedExistingRealm = NormalizeRealmId(existing.RealmId);
		string normalizedNextRealm = NormalizeRealmId(next.RealmId);
		string genericDisplay = ResolveRealmDisplayName(selectedRealm);
		bool nextMatches = string.Equals(selectedRealm, normalizedNextRealm, StringComparison.Ordinal);
		bool existingMatches = string.Equals(selectedRealm, normalizedExistingRealm, StringComparison.Ordinal);

		if (nextMatches && !string.IsNullOrWhiteSpace(next.RealmDisplay)
			&& !string.Equals(next.RealmDisplay, genericDisplay, StringComparison.Ordinal))
		{
			return next.RealmDisplay;
		}

		if (existingMatches && !string.IsNullOrWhiteSpace(existing.RealmDisplay)
			&& !string.Equals(existing.RealmDisplay, genericDisplay, StringComparison.Ordinal))
		{
			return existing.RealmDisplay;
		}

		if (nextMatches && !string.IsNullOrWhiteSpace(next.RealmDisplay))
		{
			return next.RealmDisplay;
		}

		if (existingMatches && !string.IsNullOrWhiteSpace(existing.RealmDisplay))
		{
			return existing.RealmDisplay;
		}

		return genericDisplay;
	}

	private static void AddActorToFamilyIndex(long actorId, long familyStableId, bool isAlive)
	{
		if (actorId <= 0L || familyStableId <= 0L)
		{
			return;
		}

		if (!actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds))
		{
			actorIds = new HashSet<long>();
			actorIdsByFamilyStableId[familyStableId] = actorIds;
		}
		actorIds.Add(actorId);

		if (!isAlive)
		{
			return;
		}
		if (!aliveActorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> aliveActorIds))
		{
			aliveActorIds = new HashSet<long>();
			aliveActorIdsByFamilyStableId[familyStableId] = aliveActorIds;
		}
		aliveActorIds.Add(actorId);
	}

	private static void RemoveActorFromFamilyIndex(long actorId, long familyStableId, bool wasAlive)
	{
		if (actorId <= 0L || familyStableId <= 0L)
		{
			return;
		}

		if (!actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds))
		{
			return;
		}

		actorIds.Remove(actorId);
		if (wasAlive && aliveActorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> aliveActorIds))
		{
			aliveActorIds.Remove(actorId);
			if (aliveActorIds.Count == 0)
			{
				aliveActorIdsByFamilyStableId.Remove(familyStableId);
			}
		}
		if (actorIds.Count == 0)
		{
			actorIdsByFamilyStableId.Remove(familyStableId);
			aliveActorIdsByFamilyStableId.Remove(familyStableId);
			aggregatesByFamilyStableId.Remove(familyStableId);
		}
	}

	private static void RebuildAllAggregates()
	{
		aggregatesByFamilyStableId.Clear();
		foreach (long familyId in actorIdsByFamilyStableId.Keys) RebuildAggregate(familyId);
	}

	private static void RebuildAggregate(long familyStableId)
	{
		if (familyStableId <= 0L || !actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds) || actorIds.Count == 0)
		{
			aggregatesByFamilyStableId.Remove(familyStableId);
			return;
		}

		int alive = 0;
		int total = 0;
		int cultivators = 0;
		int ziFu = 0;
		int jinDan = 0;
		int highestOrder = 0;
		int representativeOrder = -1;
		int representativeGeneration = int.MaxValue;
		long representativeId = long.MaxValue;
		string highestRealm = string.Empty;
		string representative = string.Empty;
		string anyName = string.Empty;
		foreach (long actorId in actorIds)
		{
			if (!entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry) || !entry.Found) continue;
			total++;
			if (string.IsNullOrWhiteSpace(anyName) && !string.IsNullOrWhiteSpace(entry.Name)) anyName = entry.Name;
			if (!entry.IsAlive) continue;
			alive++;
			int order = GetRealmOrder(entry.RealmId);
			if (order > 0) cultivators++;
			if (order >= GetRealmOrder(XjRealmIds.JinDan)) jinDan++;
			else if (order >= GetRealmOrder(XjRealmIds.ZiFu)) ziFu++;
			if (order > highestOrder)
			{
				highestOrder = order;
				highestRealm = string.IsNullOrWhiteSpace(entry.RealmDisplay) ? ResolveRealmDisplayName(entry.RealmId) : entry.RealmDisplay;
			}
			int generation = entry.Generation > 0 ? entry.Generation : int.MaxValue - 1;
			if (string.IsNullOrWhiteSpace(representative) || order > representativeOrder
				|| (order == representativeOrder && generation < representativeGeneration)
				|| (order == representativeOrder && generation == representativeGeneration && actorId < representativeId))
			{
				representative = entry.Name;
				representativeOrder = order;
				representativeGeneration = generation;
				representativeId = actorId;
			}
		}
		if (string.IsNullOrWhiteSpace(representative)) representative = anyName;
		if (representativeId == long.MaxValue) representativeId = familyStableId;
		aggregatesByFamilyStableId[familyStableId] = new XjFamilyLedgerAggregate(
			familyStableId, ResolveFamilyDisplayName(representative), alive, total, cultivators, ziFu, jinDan,
			highestRealm, highestOrder, representative, representativeId);
	}

	private static string ResolveFamilyDisplayName(string actorName)
	{
		return XjFamilyDisplayNameResolver.FromActorName(actorName);
	}

	private static int CompareEntries(XjFamilyMemberLedgerEntry left, XjFamilyMemberLedgerEntry right)
	{
		int byRealm = GetRealmOrder(right.RealmId).CompareTo(GetRealmOrder(left.RealmId));
		if (byRealm != 0) return byRealm;
		int byGeneration = left.Generation.CompareTo(right.Generation);
		if (byGeneration != 0) return byGeneration;
		int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
		return name != 0 ? name : left.ActorId.CompareTo(right.ActorId);
	}

	private static string ResolveRealmId(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& !string.IsNullOrWhiteSpace(realmId))
		{
			return NormalizeRealmId(realmId);
		}

		if (actor.hasTrait(XjRealmIds.ShenDan)) return XjRealmIds.ShenDan;
		if (actor.hasTrait(XjRealmIds.JinDan)) return XjRealmIds.JinDan;
		if (actor.hasTrait(XjRealmIds.ZiFu)) return XjRealmIds.ZiFu;
		if (actor.hasTrait(XjRealmIds.ZhuJi)) return XjRealmIds.ZhuJi;
		if (actor.hasTrait(XjRealmIds.LianQi)) return XjRealmIds.LianQi;
		if (actor.hasTrait(XjRealmIds.TaiXi)) return XjRealmIds.TaiXi;
		return string.Empty;
	}

	private static int ResolveBirthYear(Actor actor)
	{
		int currentYear = XjYearTracker.CurrentYear;
		if (currentYear <= 0 || actor == null)
		{
			return 0;
		}

		return Math.Max(0, currentYear - (int)Math.Floor(Math.Max(0f, actor.getAge())));
	}

	internal static string NormalizeRealmId(string realmId)
	{
		string value = (realmId ?? string.Empty).Trim();
		if (value.Length == 0) return string.Empty;
		if (string.Equals(value, XjRealmIds.TaiXi, StringComparison.Ordinal) || string.Equals(value, "TaiXi", StringComparison.OrdinalIgnoreCase) || value.Contains("胎息")) return XjRealmIds.TaiXi;
		if (string.Equals(value, XjRealmIds.LianQi, StringComparison.Ordinal) || string.Equals(value, "LianQi", StringComparison.OrdinalIgnoreCase) || (value.Contains("炼气") || value.Contains("\u7ec3\u6c14"))) return XjRealmIds.LianQi;
		if (string.Equals(value, XjRealmIds.ZhuJi, StringComparison.Ordinal) || string.Equals(value, "ZhuJi", StringComparison.OrdinalIgnoreCase) || value.Contains("筑基")) return XjRealmIds.ZhuJi;
		if (string.Equals(value, XjRealmIds.ZiFu, StringComparison.Ordinal) || string.Equals(value, "ZiFu", StringComparison.OrdinalIgnoreCase) || value.Contains("紫府")) return XjRealmIds.ZiFu;
		if (string.Equals(value, XjRealmIds.ShenDan, StringComparison.Ordinal) || string.Equals(value, "ShenDan", StringComparison.OrdinalIgnoreCase) || value.Contains("神丹")) return XjRealmIds.ShenDan;
		if (string.Equals(value, XjRealmIds.JinDan, StringComparison.Ordinal) || string.Equals(value, "JinDan", StringComparison.OrdinalIgnoreCase) || value.Contains("金丹")) return XjRealmIds.JinDan;
		return value;
	}

	internal static string ResolveRealmDisplayName(string realmId)
	{
		return NormalizeRealmId(realmId) switch
		{
			XjRealmIds.TaiXi => "胎息",
			XjRealmIds.LianQi => "炼气",
			XjRealmIds.ZhuJi => "筑基",
			XjRealmIds.ZiFu => "紫府",
			XjRealmIds.JinDan => "金丹",
			XjRealmIds.ShenDan => "神丹",
			_ => string.Empty
		};
	}

	private static string ResolveRealmDisplayName(Actor actor, string realmId)
	{
		string normalizedRealmId = NormalizeRealmId(realmId);
		if (actor?.data == null || string.IsNullOrWhiteSpace(normalizedRealmId))
		{
			return ResolveRealmDisplayName(normalizedRealmId);
		}

		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int jinDanYiXiang);
		string display = XjDaoXingStageRules.FormatDisplay(
			normalizedRealmId,
			snapshot.ZhenYuan,
			snapshot.XianJiCount,
			Math.Max(0, jinDanYiXiang));
		return string.IsNullOrWhiteSpace(display)
			? ResolveRealmDisplayName(normalizedRealmId)
			: display;
	}

	internal static int GetRealmOrder(string realmId)
	{
		return NormalizeRealmId(realmId) switch
		{
			XjRealmIds.TaiXi => 1,
			XjRealmIds.LianQi => 2,
			XjRealmIds.ZhuJi => 3,
			XjRealmIds.ZiFu => 4,
			XjRealmIds.JinDan => 5,
			XjRealmIds.ShenDan => 5,
			_ => 0
		};
	}

	private static string PickHigherRealm(string left, string right)
	{
		return GetRealmOrder(right) >= GetRealmOrder(left) ? NormalizeRealmId(right) : NormalizeRealmId(left);
	}
}
