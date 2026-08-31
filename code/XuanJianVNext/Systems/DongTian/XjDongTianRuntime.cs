using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Events;

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
			// 落霞山是势力山门地标，没有公共探索预约；这里直接跳过，避免年度候选扫描。
			if (XjDongTianRules.IsLuoXiaShanDongTian(record.QiYuDongTianId)) continue;
			bool yuanZhaoPermanent = XjDongTianRules.IsYuanZhaoDongTian(record.QiYuDongTianId);
			bool expired = record.OpenUntilYear > 0 && currentYear >= record.OpenUntilYear;
			if (!record.Found
				|| !record.IsOpen
				|| expired
				|| (!yuanZhaoPermanent && record.RemainingExploreCount <= 0)
				|| record.LastExploreReserveYear == currentYear
				|| record.AnchorTileX < 0
				|| record.AnchorTileY < 0)
			{
				continue;
			}

			List<XjQiYuDongTianExplorerRecord> explorerRecords = new List<XjQiYuDongTianExplorerRecord>(record.ExplorerRecords ?? Array.Empty<XjQiYuDongTianExplorerRecord>());
			int reserveSlots = yuanZhaoPermanent
				? GetAnnualExploreReserveSlots(record)
				: Math.Min(record.RemainingExploreCount, GetAnnualExploreReserveSlots(record));
			int added = 0;
			XjDongTianRecord workingRecord = record;
			if (!yuanZhaoPermanent) yearlyCandidateActorIds ??= XjCultivatorCandidateIndex.GetRealmEnteredIds();
			if (!yuanZhaoPermanent
				&& TryBuildClaimedSectExpedition(record, currentYear, yearlyCandidateActorIds, reserveSlots, out List<XjQiYuDongTianExplorerRecord> organizedTeam))
			{
				for (int teamIndex = 0; teamIndex < organizedTeam.Count && added < reserveSlots; teamIndex++)
				{
					XjQiYuDongTianExplorerRecord explorerRecord = organizedTeam[teamIndex];
					explorerRecords.Add(explorerRecord);
					added++;
					if (XjScheduler.ResolveActor(explorerRecord.ExplorerActorId, out Actor reservedExplorer))
					{
						XjAdventureRealmClaimSystem.OnExplorerReserved(record.RecordId, reservedExplorer, currentYear);
					}
				}
				workingRecord = BuildExploreReservationRecord(record, currentYear, yuanZhaoPermanent, added, explorerRecords);
			}

			for (int slot = added; slot < reserveSlots; slot++)
			{
				if (!TryPickExplorerCandidate(workingRecord, currentYear, yearlyCandidateActorIds, out XjQiYuDongTianExplorerRecord explorerRecord))
				{
					break;
				}

				explorerRecords.Add(explorerRecord);
				added++;
				if (XjScheduler.ResolveActor(explorerRecord.ExplorerActorId, out Actor reservedExplorer))
				{
					if (yuanZhaoPermanent) XjYuanZhaoFounderAudienceSystem.MarkAudienceReserved(reservedExplorer, currentYear);
					else XjAdventureRealmClaimSystem.OnExplorerReserved(record.RecordId, reservedExplorer, currentYear);
				}

				workingRecord = BuildExploreReservationRecord(record, currentYear, yuanZhaoPermanent, added, explorerRecords);
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

	private static XjDongTianRecord BuildExploreReservationRecord(
		in XjDongTianRecord record,
		int currentYear,
		bool permanent,
		int added,
		IReadOnlyList<XjQiYuDongTianExplorerRecord> explorerRecords)
	{
		return new XjDongTianRecord(
			record.Found, record.RecordId, record.QiYuDongTianId, record.DaoTuGroup, record.RelatedDaoTuIds, record.DisplayName,
			record.CreatedYear, record.OpenUntilYear, record.MaxExploreActors, record.ExploredActorCount + added,
			permanent ? 0 : Math.Max(0, record.RemainingExploreCount - added), record.IsOpen, record.DeathRateProfileSummary,
			record.RewardPoolSummary, record.Summary, record.AnchorActorId, record.AnchorActorName, record.AnchorTileX, record.AnchorTileY,
			record.AnchorCityId, record.AnchorCityName, record.AnchorYear, record.AnchorSource, currentYear, explorerRecords,
			record.EntityAssetId, record.EntityBuildingId, record.EntitySpawned, record.EntityClosed);
	}

	private static int GetAnnualExploreReserveSlots(in XjDongTianRecord record)
	{
		// 水月照真每次只承接一个已存在的“水月传召”；不存在公共探索席位。
		if (XjDongTianRules.IsYuanZhaoDongTian(record.QiYuDongTianId)) return 1;
		int maxExploreActors = Math.Max(1, record.MaxExploreActors);
		return Math.Max(1, (maxExploreActors + XjDongTianRules.OpenDurationYears - 1) / XjDongTianRules.OpenDurationYears);
	}

	private static bool CreateRandomQiYuDongTian(int currentYear)
	{
		IReadOnlyList<XjDongTianCatalogEntry> catalog = XjDongTianRules.Catalog;
		if (catalog == null || catalog.Count == 0)
		{
			ScheduleRetryAfterFailedStrictSpawn(currentYear);
			return false;
		}

		if (!TryPickScheduledQiYuDongTianEntry(catalog, currentYear, out XjDongTianCatalogEntry entry))
		{
			ScheduleRetryAfterFailedStrictSpawn(currentYear);
			return false;
		}

		if (TryReadQiYuDongTianRecord(entry.QiYuDongTianId, out XjDongTianRecord existingRecord))
		{
			if (!OpenExistingQiYuDongTian(existingRecord, entry, currentYear))
			{
				// 旧记录锚点若落在旃檀林内且本年找不到安全替代位置，只延后当前欠期，
				// 绝不在禁区原地重新显化，也不漂移后续固定周期。
				ScheduleRetryAfterFailedStrictSpawn(currentYear);
				return false;
			}
			ScheduleAfterSuccessfulStrictSpawn(currentYear);
			return true;
		}

		if (!TryFindActivityAnchor(currentYear, out XjQiYuDongTianActivityAnchor anchor))
		{
			ScheduleRetryAfterFailedStrictSpawn(currentYear);
			return false;
		}

		string recordId = "qiyu_dongtian_" + entry.QiYuDongTianId;
		if (recordsById.ContainsKey(recordId))
		{
			ScheduleRetryAfterFailedStrictSpawn(currentYear);
			return false;
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
			XjDongTianRules.FormatDeathRateProfileSummary(entry.QiYuDongTianId),
			XjDongTianRules.FormatRewardPoolSummary(entry.QiYuDongTianId),
			XjDongTianRules.FormatDongTianSummary(entry),
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
		string locationInfo = string.IsNullOrWhiteSpace(anchor.CityName) ? "未探索之地" : anchor.CityName;
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
			catch (System.Exception xjCaught209) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DongTian/XjDongTianRuntime.cs:209", xjCaught209); }
			XuanJianVNext.Systems.Events.XjFamilyDomainEventRouter.Publish(
				XuanJianVNext.Data.Events.XjFamilyDomainEvent.DongTianOpened(anchorActor, entry.DisplayName));
		}
		XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelWorldEvent(
			entry.DisplayName + " 在" + locationInfo + "附近显化，可供" + XjDongTianRules.MaxExploreActors + "人探寻",
			XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianOpen,
			XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.DongTian);
		string eraChangeCause = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildDongTianEraChangeCause(
			entry.DisplayName,
			entry.DaoTuGroup,
			locationInfo);
		XjEraRuntime.TrySetCurrentAgeForDaoTu(entry.DaoTuGroup, eraChangeCause);
		ScheduleAfterSuccessfulStrictSpawn(currentYear);
		return true;
	}

	private static bool OpenExistingQiYuDongTian(in XjDongTianRecord record, in XjDongTianCatalogEntry entry, int currentYear)
	{
		long anchorActorId = record.AnchorActorId;
		string anchorActorName = record.AnchorActorName;
		int anchorTileX = record.AnchorTileX;
		int anchorTileY = record.AnchorTileY;
		long anchorCityId = record.AnchorCityId;
		string anchorCityName = record.AnchorCityName;
		string anchorSource = record.AnchorSource;
		string entityAssetId = record.EntityAssetId;
		long entityBuildingId = record.EntityBuildingId;
		bool entitySpawned = record.EntitySpawned;
		bool entityClosed = false;

		if (XjZhantanlinSystem.IsInsideProtectedBoundary(anchorTileX, anchorTileY))
		{
			if (!TryFindActivityAnchor(currentYear, out XjQiYuDongTianActivityAnchor replacement))
				return false;
			if (record.EntitySpawned || record.EntityBuildingId > 0L)
				XjDongTianEntitySystem.MarkClosed(record.RecordId);
			anchorActorId = replacement.ActorId;
			anchorActorName = replacement.ActorName;
			anchorTileX = replacement.TileX;
			anchorTileY = replacement.TileY;
			anchorCityId = replacement.CityId;
			anchorCityName = replacement.CityName;
			anchorSource = replacement.Source;
			entityAssetId = string.Empty;
			entityBuildingId = 0L;
			entitySpawned = false;
		}

		int openUntilYear = currentYear + XjDongTianRules.OpenDurationYears;
		IReadOnlyList<XjQiYuDongTianExplorerRecord> retainedExplorerRecords = TrimHistoricalExplorerRecords(
			record.ExplorerRecords,
			Math.Max(16, XjDongTianRules.MaxExploreActors * 2));
		XjDongTianRecord updated = new XjDongTianRecord(
			record.Found,
			record.RecordId,
			entry.QiYuDongTianId,
			entry.DaoTuGroup,
			entry.RelatedDaoTuIds,
			entry.DisplayName,
			record.CreatedYear,
			openUntilYear,
			XjDongTianRules.MaxExploreActors,
			record.ExploredActorCount,
			XjDongTianRules.MaxExploreActors,
			true,
			XjDongTianRules.FormatDeathRateProfileSummary(entry.QiYuDongTianId),
			XjDongTianRules.FormatRewardPoolSummary(entry.QiYuDongTianId),
			XjDongTianRules.FormatDongTianSummary(entry),
			anchorActorId,
			anchorActorName,
			anchorTileX,
			anchorTileY,
			anchorCityId,
			anchorCityName,
			currentYear,
			anchorSource,
			record.LastExploreReserveYear,
			retainedExplorerRecords,
			entityAssetId,
			entityBuildingId,
			entitySpawned,
			entityClosed);
		recordsById[record.RecordId] = updated;
		MarkRecordCacheDirty();
		XjDongTianEntitySystem.Tick(new[] { updated }, 1);

		string locationInfo = string.IsNullOrWhiteSpace(anchorCityName)
			? ResolveCityName(anchorCityId)
			: anchorCityName;
		if (string.IsNullOrWhiteSpace(locationInfo)) locationInfo = "未探索之地";
		XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelWorldEvent(
			entry.DisplayName + " 在" + locationInfo + "附近再度开启，可供" + XjDongTianRules.MaxExploreActors + "人探寻",
			XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianOpen,
			XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.DongTian);
		string eraChangeCause = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildDongTianEraChangeCause(
			entry.DisplayName,
			entry.DaoTuGroup,
			locationInfo);
		XjEraRuntime.TrySetCurrentAgeForDaoTu(entry.DaoTuGroup, eraChangeCause);
		return true;
	}

	// 十座奇遇洞天会反复显世，探索者名单却只服务于当前结算和最近
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

	private static bool TryPickScheduledQiYuDongTianEntry(
		IReadOnlyList<XjDongTianCatalogEntry> catalog,
		int currentYear,
		out XjDongTianCatalogEntry selectedEntry)
	{
		selectedEntry = default;
		if (catalog == null)
		{
			return false;
		}

		List<XjDongTianCatalogEntry> ordinaryEntries = new List<XjDongTianCatalogEntry>();
		List<XjDongTianCatalogEntry> undiscoveredEntries = new List<XjDongTianCatalogEntry>();
		string lastOpenedId = string.Empty;
		int lastOpenedYear = int.MinValue;
		for (int i = 0; i < catalog.Count; i++)
		{
			XjDongTianCatalogEntry entry = catalog[i];
			if (XjDongTianRules.IsPermanentWorldSite(entry.QiYuDongTianId)
				|| string.IsNullOrWhiteSpace(entry.QiYuDongTianId))
			{
				continue;
			}

			ordinaryEntries.Add(entry);
			if (!TryReadQiYuDongTianRecord(entry.QiYuDongTianId, out XjDongTianRecord record))
			{
				undiscoveredEntries.Add(entry);
				continue;
			}

			if (record.AnchorYear >= lastOpenedYear)
			{
				lastOpenedYear = record.AnchorYear;
				lastOpenedId = entry.QiYuDongTianId;
			}
		}

		List<XjDongTianCatalogEntry> candidates = undiscoveredEntries.Count > 0
			? undiscoveredEntries
			: ordinaryEntries;
		if (candidates.Count == 0)
		{
			return false;
		}

		// 同一世界年取同一个签，重载/多次尝试不会改写已经形成的世界史；首次显世前仍优先随机未显洞天。
		long seed = (long)Math.Max(1, currentYear) * 486187739L + Math.Max(0, lastOpenedYear);
		int selectedIndex = XjDeterministicHash.PositiveIndex(seed, "qiyu_dongtian_random_schedule_v2", candidates.Count);
		if (candidates.Count > 1
			&& string.Equals(candidates[selectedIndex].QiYuDongTianId, lastOpenedId, StringComparison.Ordinal))
		{
			selectedIndex = (selectedIndex + 1) % candidates.Count;
		}

		selectedEntry = candidates[selectedIndex];
		return true;
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

		string normalizedId = XjDongTianRules.NormalizeQiYuDongTianId(qiYuDongTianId);
		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (string.Equals(
				XjDongTianRules.NormalizeQiYuDongTianId(record.QiYuDongTianId),
				normalizedId,
				StringComparison.Ordinal))
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
		if (XjZhantanlinSystem.IsInsideProtectedBoundary(tileX, tileY)) return false;

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
		else
		{
			// 洞天锚在活动修士所在地图格，而非城市对象；无城处也应能显世。
			// 用地点叙述而非伪造 City，避免后续属地/城市索引把它误认作城镇建筑。
			cityName = "未探索之地";
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
		if (XjDongTianRules.IsYuanZhaoDongTian(record.QiYuDongTianId))
		{
			// 私域洞天不扫描高境候选、不看距离，只消费事件层已经点名的一名受邀者。
			if (!XjYuanZhaoFounderAudienceSystem.TryGetPendingAudienceActorId(currentYear, out long invitedActorId)
				|| !XjScheduler.ResolveActor(invitedActorId, out Actor invitedActor)
				|| !XjYuanZhaoFounderAudienceSystem.HasActiveInvitation(invitedActor, currentYear, out string inviteReason)
				|| XjDongTianRegistry.HasYuanZhaoAudienceRecord(invitedActor, inviteReason)
				|| !TryBuildExplorerCandidate(record, invitedActor, int.MaxValue, out XjQiYuDongTianExplorerCandidate invited, out _))
			{
				return false;
			}

			explorerRecord = new XjQiYuDongTianExplorerRecord(
				invited.ActorId, invited.ActorName, invited.RealmId, invited.RealmDisplay, invited.DaoTu,
				0, currentYear, false, 0, false, false, string.Empty, string.Empty);
			return true;
		}

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
		if (XjDongTianRules.IsYuanZhaoDongTian(record.QiYuDongTianId))
		{
			return string.Equals(value, "渊照", StringComparison.Ordinal)
				|| string.Equals(value, "太阴", StringComparison.Ordinal)
				|| string.Equals(value, "坎水", StringComparison.Ordinal);
		}
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
		bool yuanZhaoPrivate = XjDongTianRules.IsYuanZhaoDongTian(record.QiYuDongTianId);
		long actorId;
		string actorName;
		int tileX = -1;
		int tileY = -1;
		if (yuanZhaoPrivate)
		{
			// 因缘牵引不依赖地表坐标。道尊可以从闭关/洞天名单中照见受邀者，
			// 因此不能先要求 current_tile 存在，否则高境离开主地图时会错误失去许可。
			if (!XjSafeCore.IsAliveActor(actor) || actor?.data == null) return false;
			actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L) return false;
			actorName = SafeActorName(actor);
		}
		else if (!TryGetActorBasic(actor, out actorId, out actorName, out tileX, out tileY)
			|| actorId == record.AnchorActorId
			|| HasExplorerRecord(record, actor, actorId))
		{
			return false;
		}

		string realmId;
		string realmDisplay;
		string daoTu;
		int weight;
		if (yuanZhaoPrivate)
		{
			// 水月照真当前只接受紫金/服气高境的“水月传召”。释修以及其它体系不能
			// 因摩诃/法相等位格自动获得拜访资格；未来若需接触道尊，应另做专属因缘事件。
			if (XjCultivationPathRules.IsShi(actor)) return false;
			realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (!TryGetYuanZhaoRealmWeight(realmId, out weight, out realmDisplay)) return false;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out daoTu);

			// 受邀者是由“水月倒影/因果牵引”入洞，不是从地表走到洞口。距离对觐见无意义。
			distanceSquared = 0;
			candidate = new XjQiYuDongTianExplorerCandidate(actorId, actorName, realmId, realmDisplay, daoTu, 0, weight);
			return true;
		}

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out realmId)
			|| !TryGetRealmWeight(realmId, out weight, out realmDisplay)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out daoTu);
		int dx = tileX - record.AnchorTileX;
		int dy = tileY - record.AnchorTileY;
		distanceSquared = dx * dx + dy * dy;
		if (distanceSquared > radiusSquared)
		{
			return false;
		}

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

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			weight = 4;
			realmDisplay = string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal) ? "黄冠" : "筑基";
			return true;
		}

		if (XjCultivationPathRules.IsZhenRenEquivalentRealm(realmId))
		{
			weight = 6;
			realmDisplay = string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal) ? "真人" : "紫府";
			return true;
		}

		return false;
	}

	private static bool TryGetYuanZhaoRealmWeight(string realmId, out int weight, out string realmDisplay)
	{
		weight = 0;
		realmDisplay = string.Empty;
		if (XjCultivationPathRules.IsZhenRenEquivalentRealm(realmId))
		{
			weight = 6;
			realmDisplay = string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal) ? "真人" : "紫府";
			return true;
		}
		if (XjCultivationPathRules.IsJinDanEquivalentRealm(realmId))
		{
			weight = 8;
			realmDisplay = string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal) ? "真君羽士" : "金丹";
			return true;
		}
		return false;
	}

	private static bool HasExplorerRecord(XjDongTianRecord record, Actor actor, long actorId)
	{
		if (actorId <= 0L || record.ExplorerRecords == null) return false;
		if (HasExplorerActorId(record, actorId)) return true;
		if (!XjDongTianRules.IsYuanZhaoDongTian(record.QiYuDongTianId) || actor?.data == null) return false;

		// “每人永远一次”按同一转世人物链判定，而不是只认当前肉身ID。转世记录本身
		// 是持久化世界档案，因此这里沿 target->source O(1) 反向索引回溯，既不扫历史表，
		// 也不会因为前世 Actor 已被原生回收而失去限制。32层只是损坏档的循环保护。
		long lineageActorId = actorId;
		for (int depth = 0; depth < 32
			&& XjReincarnation.TryGetReincarnationSourceActorId(lineageActorId, out long sourceActorId); depth++)
		{
			if (sourceActorId <= 0L || sourceActorId == lineageActorId) break;
			if (HasExplorerActorId(record, sourceActorId)) return true;
			lineageActorId = sourceActorId;
		}

		// 今释摩诃/法相轮回在设定上仍是同一真灵：identity root 作为旧档兼容旁证，
		// 保证早于 target->source 索引建立的释修轮回也不会重复进入。
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiIdentityRootActorId, out string identityRoot)
			&& long.TryParse(identityRoot, out long rootActorId)
			&& rootActorId > 0L
			&& HasExplorerActorId(record, rootActorId))
		{
			return true;
		}

		// 旧档若只有角色字段、对应历史转世记录曾被早期版本裁掉，仍用直接前世字段兜底。
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationSourceActorId, out string priorSourceActorIdText)
			&& long.TryParse(priorSourceActorIdText, out long priorActorId)
			&& priorActorId > 0L
			&& HasExplorerActorId(record, priorActorId))
		{
			return true;
		}

		return false;
	}

	private static bool HasExplorerActorId(in XjDongTianRecord record, long actorId)
	{
		if (actorId <= 0L || record.ExplorerRecords == null) return false;
		for (int i = 0; i < record.ExplorerRecords.Count; i++)
		{
			if (record.ExplorerRecords[i].ExplorerActorId == actorId) return true;
		}
		return false;
	}
}
