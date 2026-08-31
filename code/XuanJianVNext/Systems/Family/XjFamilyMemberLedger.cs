using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Shi;

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
	// ZiFuCount/JinDanCount 是对外真人／真君统计；以下四项保留实际修法拆分。
	internal readonly int ZiFuCount;
	internal readonly int JinDanCount;
	internal readonly int ZiFuPathCount;
	internal readonly int FuQiZhenRenCount;
	internal readonly int ZiJinJinDanCount;
	internal readonly int ZhenJunYuShiCount;
	internal readonly string HighestRealm;
	internal readonly int HighestRealmOrder;
	internal readonly string Representative;
	internal readonly long RepresentativeActorId;

	internal XjFamilyLedgerAggregate(long familyStableId, string displayName, int aliveCount, int totalCount,
		int cultivatorCount, int ziFuCount, int jinDanCount,
		int ziFuPathCount, int fuQiZhenRenCount, int ziJinJinDanCount, int zhenJunYuShiCount,
		string highestRealm, int highestRealmOrder, string representative, long representativeActorId)
	{
		FamilyStableId = familyStableId;
		DisplayName = displayName ?? string.Empty;
		AliveCount = Math.Max(0, aliveCount);
		TotalCount = Math.Max(0, totalCount);
		CultivatorCount = Math.Max(0, cultivatorCount);
		ZiFuCount = Math.Max(0, ziFuCount);
		JinDanCount = Math.Max(0, jinDanCount);
		ZiFuPathCount = Math.Max(0, ziFuPathCount);
		FuQiZhenRenCount = Math.Max(0, fuQiZhenRenCount);
		ZiJinJinDanCount = Math.Max(0, ziJinJinDanCount);
		ZhenJunYuShiCount = Math.Max(0, zhenJunYuShiCount);
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
		if (!XjSafeCore.IsAliveActor(actor) || actor?.data == null || !identity.Found || identity.FamilyStableIdValue <= 0L || identity.ActorId <= 0L)
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
		// Domain events may drain after death; a dead ActorId is terminal.
		if (existing.Found && !existing.IsAlive) return false;

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

	internal static bool RebindActorAfterExternalReplacement(long oldActorId, Actor target)
	{
		if (oldActorId <= 0L || target?.data == null) return false;
		long newActorId = ((BaseSystemData)target.data).id;
		if (newActorId <= 0L || newActorId == oldActorId
			|| !entriesByActorId.TryGetValue(oldActorId, out XjFamilyMemberLedgerEntry source)
			|| !source.Found || source.FamilyStableId <= 0L) return false;

		if (entriesByActorId.TryGetValue(newActorId, out XjFamilyMemberLedgerEntry existingTarget) && existingTarget.Found)
		{
			RemoveActorFromFamilyIndex(newActorId, existingTarget.FamilyStableId, existingTarget.IsAlive);
			entriesByActorId.Remove(newActorId);
			if (existingTarget.FamilyStableId != source.FamilyStableId) RebuildAggregate(existingTarget.FamilyStableId);
		}

		RemoveActorFromFamilyIndex(oldActorId, source.FamilyStableId, source.IsAlive);
		entriesByActorId.Remove(oldActorId);
		string nextName = string.Empty;
		try { nextName = target.getName() ?? string.Empty; } catch { }
		XjFamilyMemberLedgerEntry rebound = new XjFamilyMemberLedgerEntry(
			true,
			source.FamilyStableId,
			newActorId,
			string.IsNullOrWhiteSpace(nextName) ? source.Name : nextName.Trim(),
			source.Generation,
			source.RealmId,
			source.RealmDisplay,
			true,
			source.BirthYear,
			0,
			"external.replacement");
		entriesByActorId[newActorId] = rebound;
		AddActorToFamilyIndex(newActorId, rebound.FamilyStableId, isAlive: true);
		RebuildAggregate(rebound.FamilyStableId);
		XjActorStateRevisionStore.Mark(newActorId, XjActorStateDomain.Family | XjActorStateDomain.Relations);
		XjRelationEntityRevisionStore.MarkFamily(rebound.FamilyStableId);
		XjFamilyBloodlineAggregateCache.InvalidateFamily(rebound.FamilyStableId);
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
			XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.World);
		return true;
	}

	internal static IReadOnlyList<XjFamilyMemberLedgerEntry> ReadFamilyAlive(long familyStableId)
	{
		return XjFamilyReadModel.Shared.ReadAliveMembers(familyStableId);
	}

	internal static XjFamilyMemberLedgerEntry[] BuildAliveReadModelSnapshot(long familyStableId)
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
				&& entry.Found
				&& entry.IsAlive)
			{
				result.Add(entry);
			}
		}

		result.Sort(CompareEntries);
		return result.Count == 0 ? Array.Empty<XjFamilyMemberLedgerEntry>() : result.ToArray();
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
		return GetHistoricalHighestRealmOrderExcluding(familyStableId, 0L);
	}

	/// <summary>
	/// 返回家族历代最高境界，并可排除刚完成晋升的角色。账本的 RealmId
	/// 始终保留该角色一生达到过的最高境界，因此这里同时覆盖已故高位者。
	/// </summary>
	internal static int GetHistoricalHighestRealmOrderExcluding(long familyStableId, long excludedActorId)
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
			if (actorId <= 0L || actorId == excludedActorId) continue;
			if (!entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry) || !entry.Found) continue;
			best = Math.Max(best, GetRealmOrder(entry.RealmId));
		}
		return best;
	}

	/// <summary>
	/// 返回家族当前存世族人的最高境界，并可排除刚完成晋升的角色。优先读取
	/// 活体角色的实时境界，避免账本保留的个人历史最高境界把“重振”误判为“再度”。
	/// </summary>
	internal static int GetCurrentHighestRealmOrderExcluding(long familyStableId, long excludedActorId)
	{
		if (familyStableId <= 0L) return 0;

		// 正常运行时使用活体稀疏索引；读档极早期若该索引尚未恢复，
		// 退回完整家族索引并以 entry.IsAlive 过滤，避免把“再度”误判为“重振”。
		HashSet<long> actorIds;
		bool useAliveIndex = aliveActorIdsByFamilyStableId.TryGetValue(familyStableId, out actorIds)
			&& actorIds != null
			&& actorIds.Count > 0;
		if (!useAliveIndex
			&& (!actorIdsByFamilyStableId.TryGetValue(familyStableId, out actorIds)
				|| actorIds == null
				|| actorIds.Count == 0))
		{
			return 0;
		}

		int best = 0;
		foreach (long actorId in actorIds)
		{
			if (actorId <= 0L || actorId == excludedActorId) continue;
			if (!entriesByActorId.TryGetValue(actorId, out XjFamilyMemberLedgerEntry entry)
				|| !entry.Found
				|| (!useAliveIndex && !entry.IsAlive))
			{
				continue;
			}

			string realmId = string.Empty;
			if (XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor liveActor) && liveActor?.data != null)
			{
				realmId = ResolveRealmId(liveActor);
			}
			if (string.IsNullOrWhiteSpace(realmId) && entry.IsAlive)
			{
				realmId = entry.RealmId;
			}
			best = Math.Max(best, GetRealmOrder(realmId));
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
		return ReconcileFamilySurname(familyStableId, canonicalSurname, includeHistorical: true);
	}

	internal static bool ReconcileFamilySurname(long familyStableId, string canonicalSurname, bool includeHistorical)
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
			if (!includeHistorical && !entry.IsAlive)
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
			XjRelationEntityRevisionStore.MarkFamily(familyStableId);
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
			XjActorStateRevisionStore.Mark(merged.ActorId, XjActorStateDomain.Family | XjActorStateDomain.Relations);
			XjRelationEntityRevisionStore.MarkFamily(existing.FamilyStableId);
			if (merged.FamilyStableId != existing.FamilyStableId) XjRelationEntityRevisionStore.MarkFamily(merged.FamilyStableId);
			if (!suppressPersistenceSignals)
			{
				XjWorldArchiveSystem.MarkChanged();
				XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.World);
			}
			return true;
		}

		entriesByActorId[next.ActorId] = next;
		AddActorToFamilyIndex(next.ActorId, next.FamilyStableId, next.IsAlive);
		XjActorStateRevisionStore.Mark(next.ActorId, XjActorStateDomain.Family | XjActorStateDomain.Relations);
		XjRelationEntityRevisionStore.MarkFamily(next.FamilyStableId);
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
		// Late refreshes cannot resurrect a lifetime ActorId already committed as dead.
		bool mergedAlive = existing.Found && !existing.IsAlive ? false : next.IsAlive;
		return new XjFamilyMemberLedgerEntry(
			true,
			stableFamilyId > 0L ? stableFamilyId : existing.FamilyStableId,
			next.ActorId,
			string.IsNullOrWhiteSpace(next.Name) ? existing.Name : next.Name,
			next.Generation > 0 ? next.Generation : existing.Generation,
			nextRealm,
			realmDisplay,
			mergedAlive,
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
		int ziFuPath = 0;
		int fuQiZhenRen = 0;
		int ziJinJinDan = 0;
		int zhenJunYuShi = 0;
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
			string exactRealm = NormalizeRealmId(entry.RealmId);
			Actor liveActor = null;
			if (entry.ActorId > 0L)
			{
				XjActorRegistry.ResolveKnownOrWorld(entry.ActorId, out liveActor);
			}
			switch (XjHighRealmIdentity.ResolveKind(liveActor, exactRealm))
			{
				case XjHighRealmKind.ZiFu:
					ziFu++;
					ziFuPath++;
					break;
				case XjHighRealmKind.FuQiZhenRen:
					ziFu++;
					fuQiZhenRen++;
					break;
				case XjHighRealmKind.ZiJinJinDan:
					jinDan++;
					ziJinJinDan++;
					break;
				case XjHighRealmKind.ZhenJunYuShi:
					jinDan++;
					zhenJunYuShi++;
					break;
			}
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
			ziFuPath, fuQiZhenRen, ziJinJinDan, zhenJunYuShi,
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

		if (XjCultivationPathRules.IsShi(actor) && XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot shiSnapshot))
		{
			return XjShiCatalog.GetRealmTraitId(shiSnapshot.Realm);
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& !string.IsNullOrWhiteSpace(realmId))
		{
			return NormalizeRealmId(realmId);
		}

		if (actor.hasTrait(XjRealmIds.ShenDan)) return XjRealmIds.ShenDan;
		if (actor.hasTrait(XjRealmIds.ZhenJunYuShi)) return XjRealmIds.ZhenJunYuShi;
		if (actor.hasTrait(XjRealmIds.JinDan)) return XjRealmIds.JinDan;
		if (actor.hasTrait(XjRealmIds.FuQiZhenRen)) return XjRealmIds.FuQiZhenRen;
		if (actor.hasTrait(XjRealmIds.ZiFu)) return XjRealmIds.ZiFu;
		if (actor.hasTrait(XjRealmIds.HuangGuan)) return XjRealmIds.HuangGuan;
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
		if (XjShiCatalog.TryResolveRealmByTrait(value, out _)) return value;
		return XjRealmHelper.NormalizeId(value);
	}

	internal static string ResolveRealmDisplayName(string realmId)
	{
		string value = (realmId ?? string.Empty).Trim();
		if (XjShiCatalog.TryResolveRealmByTrait(value, out string shiRealm)) return XjShiCatalog.GetRealmDisplay(shiRealm);
		return XjRealmHelper.GetDisplayName(value);
	}

	private static string ResolveRealmDisplayName(Actor actor, string realmId)
	{
		string normalizedRealmId = NormalizeRealmId(realmId);
		if (actor?.data == null || string.IsNullOrWhiteSpace(normalizedRealmId))
		{
			return ResolveRealmDisplayName(normalizedRealmId);
		}
		if (XjShiCatalog.TryResolveRealmByTrait(normalizedRealmId, out string shiRealm)) return XjShiCatalog.GetRealmDisplay(shiRealm);

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
		string value = (realmId ?? string.Empty).Trim();
		if (XjShiCatalog.TryResolveRealmByTrait(value, out string shiRealm))
		{
			if (string.Equals(shiRealm, XjShiRealmIds.Monk, StringComparison.Ordinal)) return 1;
			if (string.Equals(shiRealm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)) return 3;
			if (string.Equals(shiRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal)) return 4;
			if (string.Equals(shiRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) return 5;
			if (string.Equals(shiRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return 6;
			if (string.Equals(shiRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return 7;
		}
		return XjRealmHelper.GetOrder(value);
	}

	private static string PickHigherRealm(string left, string right)
	{
		return GetRealmOrder(right) >= GetRealmOrder(left) ? NormalizeRealmId(right) : NormalizeRealmId(left);
	}
}
