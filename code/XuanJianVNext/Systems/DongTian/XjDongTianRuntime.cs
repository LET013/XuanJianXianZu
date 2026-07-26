using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Era;

namespace XuanJianVNext.Systems.DongTian;

internal static partial class XjDongTianRegistry
{
	private static void ReserveExplorersForOpenRecords(int currentYear)
	{
		if (recordsById.Count == 0)
		{
			return;
		}

		List<XjDongTianRecord> updatedRecords = null;
		IReadOnlyList<long> yearlyCandidateActorIds = null;
		foreach (XjDongTianRecord record in recordsById.Values)
		{
			bool expired = record.OpenUntilYear > 0 && currentYear >= record.OpenUntilYear;
			if (!record.Found
				|| !record.IsOpen
				|| expired
				|| record.RemainingExploreCount <= 0
				|| record.LastExploreReserveYear == currentYear
				|| record.AnchorTileX < 0
				|| record.AnchorTileY < 0)
			{
				continue;
			}

			List<XjQiYuDongTianExplorerRecord> explorerRecords = new List<XjQiYuDongTianExplorerRecord>(record.ExplorerRecords ?? Array.Empty<XjQiYuDongTianExplorerRecord>());
			int reserveSlots = Math.Min(record.RemainingExploreCount, GetAnnualExploreReserveSlots(record));
			int added = 0;
			XjDongTianRecord workingRecord = record;
			for (int slot = 0; slot < reserveSlots; slot++)
			{
				yearlyCandidateActorIds ??= XjCultivatorCandidateIndex.GetRealmEnteredIds();
				if (!TryPickExplorerCandidate(workingRecord, currentYear, yearlyCandidateActorIds, out XjQiYuDongTianExplorerRecord explorerRecord))
				{
					break;
				}

				explorerRecords.Add(explorerRecord);
				added++;
				if (XjScheduler.ResolveActor(explorerRecord.ExplorerActorId, out Actor reservedExplorer))
				{
					XjAdventureRealmClaimSystem.OnExplorerReserved(record.RecordId, reservedExplorer, currentYear);
				}

				workingRecord = new XjDongTianRecord(
					record.Found,
					record.RecordId,
					record.QiYuDongTianId,
					record.DaoTuGroup,
					record.RelatedDaoTuIds,
					record.DisplayName,
					record.CreatedYear,
					record.OpenUntilYear,
					record.MaxExploreActors,
					record.ExploredActorCount + added,
					Math.Max(0, record.RemainingExploreCount - added),
					record.IsOpen,
					record.DeathRateProfileSummary,
					record.RewardPoolSummary,
					record.Summary,
					record.AnchorActorId,
					record.AnchorActorName,
					record.AnchorTileX,
					record.AnchorTileY,
					record.AnchorCityId,
					record.AnchorCityName,
					record.AnchorYear,
					record.AnchorSource,
					currentYear,
					explorerRecords,
					record.EntityAssetId,
					record.EntityBuildingId,
					record.EntitySpawned,
					record.EntityClosed);
			}

			if (added <= 0)
			{
				continue;
			}

			updatedRecords ??= new List<XjDongTianRecord>();
			updatedRecords.Add(workingRecord);
		}

		if (updatedRecords == null)
		{
			return;
		}

		for (int i = 0; i < updatedRecords.Count; i++)
		{
			XjDongTianRecord updated = updatedRecords[i];
			recordsById[updated.RecordId] = updated;
		}

		MarkRecordCacheDirty();
		XjWorldArchiveSystem.MarkChanged();
	}

	private static int GetAnnualExploreReserveSlots(in XjDongTianRecord record)
	{
		int maxExploreActors = Math.Max(1, record.MaxExploreActors);
		return Math.Max(1, (maxExploreActors + XjDongTianRules.OpenDurationYears - 1) / XjDongTianRules.OpenDurationYears);
	}

	private static void CreateRandomQiYuDongTian(int currentYear)
	{
		IReadOnlyList<XjDongTianCatalogEntry> catalog = XjDongTianRules.Catalog;
		if (catalog == null || catalog.Count == 0)
		{
			nextSpawnYear = currentYear + 1;
			XjWorldArchiveSystem.MarkChanged();
			return;
		}

		if (!TryPickScheduledQiYuDongTianEntry(catalog, out XjDongTianCatalogEntry entry))
		{
			nextSpawnYear = currentYear + 1;
			XjWorldArchiveSystem.MarkChanged();
			return;
		}

		if (TryReadQiYuDongTianRecord(entry.QiYuDongTianId, out XjDongTianRecord existingRecord))
		{
			OpenExistingQiYuDongTian(existingRecord, entry, currentYear);
			nextSpawnYear = GetNextQiYuDongTianSpawnYearAfter(currentYear);
			XjWorldArchiveSystem.MarkChanged();
			return;
		}

		if (!TryFindActivityAnchor(currentYear, out XjQiYuDongTianActivityAnchor anchor))
		{
			nextSpawnYear = currentYear + 1;
			XjWorldArchiveSystem.MarkChanged();
			return;
		}

		string recordId = "qiyu_dongtian_" + entry.QiYuDongTianId;
		if (recordsById.ContainsKey(recordId))
		{
			nextSpawnYear = GetNextQiYuDongTianSpawnYearAfter(currentYear);
			XjWorldArchiveSystem.MarkChanged();
			return;
		}

		int openUntilYear = currentYear + XjDongTianRules.OpenDurationYears;
		XjDongTianRecord record = new XjDongTianRecord(
			true,
			recordId,
			entry.QiYuDongTianId,
			entry.DaoTuGroup,
			entry.RelatedDaoTuIds,
			entry.DisplayName,
			currentYear,
			openUntilYear,
			XjDongTianRules.MaxExploreActors,
			0,
			XjDongTianRules.MaxExploreActors,
			true,
			XjDongTianRules.FormatDeathRateProfileSummary(),
			XjDongTianRules.FormatRewardPoolSummary(),
			entry.DisplayName + "（" + entry.DaoTuGroup + "）",
			anchor.ActorId,
			anchor.ActorName,
			anchor.TileX,
			anchor.TileY,
			anchor.CityId,
			anchor.CityName,
			currentYear,
			anchor.Source,
			0,
			Array.Empty<XjQiYuDongTianExplorerRecord>());

		recordsById[recordId] = record;
		MarkRecordCacheDirty();
		XjAdventureRealmClaimSystem.OnRealmCreated(record);
		XjDongTianEntitySystem.Tick(new[] { record }, 1);
		string locationInfo = "世间";
		if (TryResolveKnownActor(anchor.ActorId, out Actor anchorActor))
		{
			try
			{
				string cityName = anchorActor?.city?.data == null
					? string.Empty
					: ((BaseSystemData)anchorActor.city.data).name;
				if (!string.IsNullOrWhiteSpace(cityName))
				{
					locationInfo = cityName;
				}
			}
			catch { }
			XuanJianVNext.Systems.Events.XjFamilyDomainEventRouter.Publish(
				XuanJianVNext.Data.Events.XjFamilyDomainEvent.DongTianOpened(anchorActor, entry.DisplayName));
		}
		XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelWorldEvent(
			entry.DisplayName + " 在" + locationInfo + "附近显化，可供" + XjDongTianRules.MaxExploreActors + "人探寻",
			XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianOpen);
		string eraChangeCause = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildDongTianEraChangeCause(
			entry.DisplayName,
			entry.DaoTuGroup,
			locationInfo);
		XjEraRuntime.TrySetCurrentAgeForDaoTu(entry.DaoTuGroup, eraChangeCause);
		nextSpawnYear = GetNextQiYuDongTianSpawnYearAfter(currentYear);
		XjWorldArchiveSystem.MarkChanged();
	}

	private static void OpenExistingQiYuDongTian(in XjDongTianRecord record, in XjDongTianCatalogEntry entry, int currentYear)
	{
		int openUntilYear = currentYear + XjDongTianRules.OpenDurationYears;
		IReadOnlyList<XjQiYuDongTianExplorerRecord> retainedExplorerRecords = TrimHistoricalExplorerRecords(
			record.ExplorerRecords,
			Math.Max(16, XjDongTianRules.MaxExploreActors * 2));
		XjDongTianRecord updated = new XjDongTianRecord(
			record.Found,
			record.RecordId,
			record.QiYuDongTianId,
			record.DaoTuGroup,
			record.RelatedDaoTuIds,
			record.DisplayName,
			record.CreatedYear,
			openUntilYear,
			XjDongTianRules.MaxExploreActors,
			record.ExploredActorCount,
			XjDongTianRules.MaxExploreActors,
			true,
			record.DeathRateProfileSummary,
			record.RewardPoolSummary,
			record.Summary,
			record.AnchorActorId,
			record.AnchorActorName,
			record.AnchorTileX,
			record.AnchorTileY,
			record.AnchorCityId,
			record.AnchorCityName,
			currentYear,
			record.AnchorSource,
			record.LastExploreReserveYear,
			retainedExplorerRecords,
			record.EntityAssetId,
			record.EntityBuildingId,
			record.EntitySpawned,
			false);
		recordsById[record.RecordId] = updated;
		MarkRecordCacheDirty();
		XjDongTianEntitySystem.Tick(new[] { updated }, 1);

		string locationInfo = string.IsNullOrWhiteSpace(record.AnchorCityName)
			? ResolveCityName(record.AnchorCityId)
			: record.AnchorCityName;
		if (string.IsNullOrWhiteSpace(locationInfo)) locationInfo = "世间";
		XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelWorldEvent(
			entry.DisplayName + " 在" + locationInfo + "附近再度开启，可供" + XjDongTianRules.MaxExploreActors + "人探寻",
			XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianOpen);
		string eraChangeCause = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildDongTianEraChangeCause(
			entry.DisplayName,
			entry.DaoTuGroup,
			locationInfo);
		XjEraRuntime.TrySetCurrentAgeForDaoTu(entry.DaoTuGroup, eraChangeCause);
	}

	// 九座奇遇洞天会反复显世，探索者名单却只服务于当前结算和最近
	// 一轮角色查询。旧的已结算名单若永久附着在同一洞天记录上，会让
	// 千年档的存档、快照和结算复制持续膨胀。
	private static IReadOnlyList<XjQiYuDongTianExplorerRecord> TrimHistoricalExplorerRecords(
		IReadOnlyList<XjQiYuDongTianExplorerRecord> source,
		int resolvedRetention)
	{
		if (source == null || source.Count == 0)
		{
			return Array.Empty<XjQiYuDongTianExplorerRecord>();
		}

		resolvedRetention = Math.Max(1, resolvedRetention);
		List<XjQiYuDongTianExplorerRecord> retained = new List<XjQiYuDongTianExplorerRecord>(
			Math.Min(source.Count, resolvedRetention + XjDongTianRules.MaxExploreActors));
		int resolvedCount = 0;
		for (int i = source.Count - 1; i >= 0; i--)
		{
			XjQiYuDongTianExplorerRecord entry = source[i];
			bool mustKeep = !entry.Resolved || (entry.DeathPending && !entry.DeathFinalized);
			if (mustKeep || resolvedCount < resolvedRetention)
			{
				retained.Add(entry);
				if (!mustKeep)
				{
					resolvedCount++;
				}
			}
		}
		retained.Reverse();
		return retained;
	}

	private static bool TryFindActivityAnchor(int currentYear, out XjQiYuDongTianActivityAnchor anchor)
	{
		anchor = default;
		IReadOnlyList<long> actorIds = XjCultivatorCandidateIndex.GetRealmEnteredIds();
		if (actorIds == null || actorIds.Count == 0)
		{
			return false;
		}

		XjQiYuDongTianActivityAnchor strictPick = default;
		XjQiYuDongTianActivityAnchor relaxedPick = default;
		int strictCount = 0;
		int relaxedCount = 0;
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(actorIds[i], out Actor actor)
				|| !TryBuildAnchor(actor, currentYear, out XjQiYuDongTianActivityAnchor candidate))
			{
				continue;
			}

			if (IsActivityAnchorFarEnough(candidate, XjDongTianRules.QiYuDongTianMinAnchorDistance))
			{
				strictCount++;
				if (UnityEngine.Random.Range(0, strictCount) == 0)
				{
					strictPick = candidate;
				}
				continue;
			}

			if (IsActivityAnchorFarEnough(candidate, XjDongTianRules.QiYuDongTianRelaxedAnchorDistance))
			{
				relaxedCount++;
				if (UnityEngine.Random.Range(0, relaxedCount) == 0)
				{
					relaxedPick = candidate;
				}
			}
		}

		if (strictCount > 0)
		{
			anchor = strictPick;
			return true;
		}
		if (relaxedCount > 0)
		{
			anchor = relaxedPick;
			return true;
		}
		return false;
	}

	private static bool TryPickScheduledQiYuDongTianEntry(IReadOnlyList<XjDongTianCatalogEntry> catalog, out XjDongTianCatalogEntry selectedEntry)
	{
		selectedEntry = default;
		if (catalog == null)
		{
			return false;
		}

		for (int i = 0; i < catalog.Count; i++)
		{
			XjDongTianCatalogEntry entry = catalog[i];
			if (string.IsNullOrWhiteSpace(entry.QiYuDongTianId) || HasQiYuDongTianRecord(entry.QiYuDongTianId))
			{
				continue;
			}

			selectedEntry = entry;
			return true;
		}

		int lastIndex = -1;
		int lastOpenYear = int.MinValue;
		for (int i = 0; i < catalog.Count; i++)
		{
			XjDongTianCatalogEntry entry = catalog[i];
			if (TryReadQiYuDongTianRecord(entry.QiYuDongTianId, out XjDongTianRecord record)
				&& record.AnchorYear >= lastOpenYear)
			{
				lastOpenYear = record.AnchorYear;
				lastIndex = i;
			}
		}

		if (catalog.Count <= 0)
		{
			return false;
		}

		int nextIndex = lastIndex < 0 ? 0 : (lastIndex + 1) % catalog.Count;
		selectedEntry = catalog[nextIndex];
		return !string.IsNullOrWhiteSpace(selectedEntry.QiYuDongTianId);
	}

	private static bool HasQiYuDongTianRecord(string qiYuDongTianId)
	{
		return TryReadQiYuDongTianRecord(qiYuDongTianId, out _);
	}

	private static bool TryReadQiYuDongTianRecord(string qiYuDongTianId, out XjDongTianRecord foundRecord)
	{
		foundRecord = default;
		if (string.IsNullOrWhiteSpace(qiYuDongTianId))
		{
			return false;
		}

		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (string.Equals(record.QiYuDongTianId, qiYuDongTianId, StringComparison.Ordinal))
			{
				foundRecord = record;
				return true;
			}
		}

		return false;
	}

	private static bool IsActivityAnchorFarEnough(in XjQiYuDongTianActivityAnchor anchor, int minDistance)
	{
		if (minDistance <= 0)
		{
			return true;
		}

		long minDistanceSquared = (long)minDistance * minDistance;
		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (record.AnchorTileX < 0 || record.AnchorTileY < 0)
			{
				continue;
			}

			long dx = anchor.TileX - record.AnchorTileX;
			long dy = anchor.TileY - record.AnchorTileY;
			if (dx * dx + dy * dy < minDistanceSquared)
			{
				return false;
			}
		}

		return true;
	}

	private static bool TryBuildAnchor(Actor actor, int currentYear, out XjQiYuDongTianActivityAnchor anchor)
	{
		anchor = default;
		if (!TryGetActorBasic(actor, out long actorId, out string actorName, out int tileX, out int tileY))
		{
			return false;
		}

		long cityId = 0L;
		string cityName = string.Empty;
		City city = actor.city;
		if (city?.data != null && city.data.id > 0L)
		{
			cityId = city.data.id;
			try
			{
				cityName = ((BaseSystemData)city.data).name ?? string.Empty;
			}
			catch
			{
				cityName = string.Empty;
			}
		}

		anchor = new XjQiYuDongTianActivityAnchor(actorId, actorName, tileX, tileY, cityId, cityName, currentYear, "KnownActor");
		return true;
	}

	private static string ResolveCityName(long cityId)
	{
		if (!XjWorldLookupIndex.TryResolveCity(cityId, out City city) || city?.data == null) return string.Empty;
		try { return ((BaseSystemData)city.data).name ?? string.Empty; }
		catch { return string.Empty; }
	}

	private static bool TryPickExplorerCandidate(
		XjDongTianRecord record,
		int currentYear,
		IReadOnlyList<long> actorIds,
		out XjQiYuDongTianExplorerRecord explorerRecord)
	{
		explorerRecord = default;
		if (actorIds == null || actorIds.Count == 0)
		{
			return false;
		}

		int localSquared = XjDongTianRules.LocalExploreRadius * XjDongTianRules.LocalExploreRadius;
		int expandedSquared = XjDongTianRules.ExpandedExploreRadius * XjDongTianRules.ExpandedExploreRadius;
		int maxSquared = XjDongTianRules.MaxExploreRadius * XjDongTianRules.MaxExploreRadius;
		XjQiYuDongTianExplorerCandidate bestPick = default;
		int bestScore = int.MinValue;
		long bestActorId = long.MaxValue;

		for (int i = 0; i < actorIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(actorIds[i], out Actor actor)
				|| !TryBuildExplorerCandidate(record, actor, maxSquared, out XjQiYuDongTianExplorerCandidate candidate, out int distanceSquared))
			{
				continue;
			}

			int score = ScoreExplorerCandidate(record, actor, candidate, distanceSquared, localSquared, expandedSquared);
			if (score > bestScore || (score == bestScore && candidate.ActorId < bestActorId))
			{
				bestScore = score;
				bestActorId = candidate.ActorId;
				bestPick = candidate;
			}
		}

		if (bestScore == int.MinValue) return false;
		XjQiYuDongTianExplorerCandidate picked = bestPick;

		explorerRecord = new XjQiYuDongTianExplorerRecord(
			picked.ActorId,
			picked.ActorName,
			picked.RealmId,
			picked.RealmDisplay,
			picked.DaoTu,
			picked.Distance,
			currentYear,
			false,
			0,
			false,
			false,
			string.Empty,
			string.Empty);
		return true;
	}

	private static int ScoreExplorerCandidate(
		in XjDongTianRecord record,
		Actor actor,
		in XjQiYuDongTianExplorerCandidate candidate,
		int distanceSquared,
		int localSquared,
		int expandedSquared)
	{
		int score = distanceSquared <= localSquared
			? 1_000_000
			: distanceSquared <= expandedSquared ? 650_000 : 300_000;
		if (actor?.city?.data != null && actor.city.data.id == record.AnchorCityId)
		{
			score += 180_000;
		}
		if (IsRelatedDaoTu(record, candidate.DaoTu))
		{
			score += 80_000;
		}
		score += Math.Max(1, candidate.Weight) * 20_000;
		score -= Math.Min(19_999, candidate.Distance);
		return score;
	}

	private static bool IsRelatedDaoTu(in XjDongTianRecord record, string daoTu)
	{
		if (string.IsNullOrWhiteSpace(daoTu)) return false;
		string value = daoTu.Trim();
		if (string.Equals(record.DaoTuGroup, value, StringComparison.Ordinal)) return true;
		return !string.IsNullOrWhiteSpace(record.RelatedDaoTuIds)
			&& record.RelatedDaoTuIds.IndexOf(value, StringComparison.Ordinal) >= 0;
	}

	private static bool TryBuildExplorerCandidate(
		XjDongTianRecord record,
		Actor actor,
		int radiusSquared,
		out XjQiYuDongTianExplorerCandidate candidate,
		out int distanceSquared)
	{
		candidate = default;
		distanceSquared = int.MaxValue;
		if (!TryGetActorBasic(actor, out long actorId, out string actorName, out int tileX, out int tileY)
			|| actorId == record.AnchorActorId
			|| HasExplorerRecord(record, actorId))
		{
			return false;
		}

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !TryGetRealmWeight(realmId, out int weight, out string realmDisplay))
		{
			return false;
		}

		int dx = tileX - record.AnchorTileX;
		int dy = tileY - record.AnchorTileY;
		distanceSquared = dx * dx + dy * dy;
		if (distanceSquared > radiusSquared)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		candidate = new XjQiYuDongTianExplorerCandidate(actorId, actorName, realmId, realmDisplay, daoTu, (int)Math.Sqrt(distanceSquared), weight);
		return true;
	}

	private static bool TryGetActorBasic(Actor actor, out long actorId, out string actorName, out int tileX, out int tileY)
	{
		actorId = 0L;
		actorName = string.Empty;
		tileX = -1;
		tileY = -1;
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		var tile = ((BaseSimObject)actor).current_tile;
		if (tile == null)
		{
			return false;
		}

		tileX = tile.pos.x;
		tileY = tile.pos.y;
		actorName = SafeActorName(actor);
		return tileX >= 0 && tileY >= 0;
	}

	private static bool TryGetRealmWeight(string realmId, out int weight, out string realmDisplay)
	{
		weight = 0;
		realmDisplay = string.Empty;
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			weight = 1;
			realmDisplay = "胎息";
			return true;
		}

		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			weight = 2;
			realmDisplay = "炼气";
			return true;
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			weight = 4;
			realmDisplay = "筑基";
			return true;
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			weight = 6;
			realmDisplay = "紫府";
			return true;
		}

		return false;
	}

	private static bool HasExplorerRecord(XjDongTianRecord record, long actorId)
	{
		if (actorId <= 0L || record.ExplorerRecords == null)
		{
			return false;
		}

		for (int i = 0; i < record.ExplorerRecords.Count; i++)
		{
			if (record.ExplorerRecords[i].ExplorerActorId == actorId)
			{
				return true;
			}
		}

		return false;
	}
}
