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

namespace XuanJianVNext.Systems.DongTian;

internal static partial class XjDongTianRegistry
{
	private static readonly Dictionary<string, XjDongTianRecord> recordsById = new Dictionary<string, XjDongTianRecord>(StringComparer.Ordinal);
	private static readonly List<XjDongTianRecord> sortedRecordCache = new List<XjDongTianRecord>();
	private static bool sortedRecordCacheDirty = true;

	private static int nextSpawnYear;

	private enum RuntimeYearPhase : byte
	{
		None = 0,
		ResolveExplorers = 1,
		FinalizeYear = 2
	}

	private static int lastProcessedYear = -1;
	private static int activeProcessingYear;
	private static RuntimeYearPhase runtimeYearPhase;

	internal static bool NeedsRuntimeTick
	{
		get
		{
			return activeProcessingYear > 0
				|| (TryGetCurrentWorldYear(out int currentYear) && currentYear > lastProcessedYear);
		}
	}

	internal static bool HasActiveRuntimeYear => activeProcessingYear > 0;

	internal static void TickRuntime(int budget)
	{
		if (budget <= 0 || !TryGetCurrentWorldYear(out int currentWorldYear))
		{
			return;
		}

		if (lastProcessedYear < 0)
		{
			// Load baseline: process the current year once, never replay the
			// entire historical calendar from year zero.
			lastProcessedYear = Math.Max(0, currentWorldYear - 1);
		}
		if (activeProcessingYear <= 0)
		{
			if (lastProcessedYear >= currentWorldYear)
			{
				return;
			}

			int nextRuntimeYear = lastProcessedYear + 1;
			if (!XjDetectionGate.TryBeginAnnualJob(XjDetectionJob.DongTianRuntime, nextRuntimeYear))
			{
				return;
			}

			activeProcessingYear = nextRuntimeYear;
			runtimeYearPhase = RuntimeYearPhase.ResolveExplorers;
			EnsureNextSpawnYear(activeProcessingYear);
			XjDongTianEntitySystem.Tick(ReadAllEntries(), 1);
		}

		if (runtimeYearPhase == RuntimeYearPhase.ResolveExplorers)
		{
			ResolveExplorerRecords(activeProcessingYear, budget);
			if (HasResolvableExplorerRecords(activeProcessingYear))
			{
				// The year is not complete until every eligible explorer result has
				// consumed its bounded slot. Do not advance the calendar merely
				// because this frame exhausted its budget.
				return;
			}
			runtimeYearPhase = RuntimeYearPhase.FinalizeYear;
		}

		if (runtimeYearPhase == RuntimeYearPhase.FinalizeYear)
		{
			FinalizeRuntimeYear(activeProcessingYear);
			lastProcessedYear = activeProcessingYear;
			activeProcessingYear = 0;
			runtimeYearPhase = RuntimeYearPhase.None;
			// Persist the completed calendar cursor. Saving midway through a large
			// high-speed catch-up can then resume from the first unfinished year
			// instead of baselining at the newly loaded world year.
			XjWorldArchiveSystem.MarkChanged();
		}
	}

	private static void FinalizeRuntimeYear(int currentYear)
	{
		CloseExpiredOpenRecords(currentYear);
		ReserveExplorersForOpenRecords(currentYear);
		if (currentYear < nextSpawnYear)
		{
			return;
		}

		CreateRandomQiYuDongTian(currentYear);
		XjDongTianEntitySystem.Tick(ReadAllEntries(), 1);
		ReserveExplorersForOpenRecords(currentYear);
	}

	internal static void AddOrUpdate(XjDongTianRecord record)
	{
		if (!record.Found || string.IsNullOrWhiteSpace(record.RecordId))
		{
			return;
		}

		recordsById[record.RecordId.Trim()] = record;
		MarkRecordCacheDirty();
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static IReadOnlyList<XjDongTianRecord> ReadAllEntries()
	{
		if (recordsById.Count == 0)
		{
			return Array.Empty<XjDongTianRecord>();
		}

		if (!sortedRecordCacheDirty && sortedRecordCache.Count == recordsById.Count)
		{
			return sortedRecordCache;
		}

		sortedRecordCache.Clear();
		sortedRecordCache.AddRange(recordsById.Values);
		sortedRecordCache.Sort((left, right) =>
		{
			int byYear = left.CreatedYear.CompareTo(right.CreatedYear);
			if (byYear != 0)
			{
				return byYear;
			}

			int name = string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
			return name != 0 ? name : string.Compare(left.RecordId, right.RecordId, StringComparison.Ordinal);
		});
		sortedRecordCacheDirty = false;
		return sortedRecordCache;
	}

	internal static bool TryReadRecord(string recordId, out XjDongTianRecord record)
	{
		record = default;
		return !string.IsNullOrWhiteSpace(recordId) && recordsById.TryGetValue(recordId.Trim(), out record);
	}

	internal static void UpdateEntityState(string recordId, string assetId, long buildingId, bool spawned, bool closed)
	{
		if (string.IsNullOrWhiteSpace(recordId) || !recordsById.TryGetValue(recordId.Trim(), out XjDongTianRecord record))
			return;
		recordsById[record.RecordId] = CopyWithEntity(record, assetId, buildingId, spawned, closed);
		MarkRecordCacheDirty();
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static string GetSummaryForActor(long actorId)
	{
		if (actorId <= 0L || recordsById.Count == 0)
		{
			return "奇遇洞天：无";
		}

		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (!record.Found || record.ExplorerRecords == null || record.ExplorerRecords.Count == 0)
			{
				continue;
			}

			for (int i = 0; i < record.ExplorerRecords.Count; i++)
			{
				XjQiYuDongTianExplorerRecord er = record.ExplorerRecords[i];
				if (er.ExplorerActorId == actorId)
				{
					if (er.DeathFinalized)
					{
						return record.DisplayName + "（已陨落）";
					}
					if (er.Resolved)
					{
						return record.DisplayName + "（已探寻" + (er.RewardApplied ? "，获得奖励" : "，未得奖励") + "）";
					}
					return record.DisplayName + "（待定）";
				}
			}
		}

		return "奇遇洞天：无";
	}

	internal static int ExportNextSpawnYear()
	{
		return nextSpawnYear < 0 ? 0 : nextSpawnYear;
	}

	internal static int ExportLastProcessedYear()
	{
		return lastProcessedYear;
	}

	internal static void ImportRuntimeYears(int nextYear, int completedYear)
	{
		nextSpawnYear = nextYear < 0 ? 0 : nextYear;
		lastProcessedYear = completedYear < 0 ? -1 : completedYear;
		activeProcessingYear = 0;
		runtimeYearPhase = RuntimeYearPhase.None;
	}

	internal static void ExportArchiveRecords(List<XjDongTianArchiveData> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (!record.Found || string.IsNullOrWhiteSpace(record.RecordId))
			{
				continue;
			}

			records.Add(new XjDongTianArchiveData
			{
				RecordId = record.RecordId,
				QiYuDongTianId = record.QiYuDongTianId,
				DaoTuGroup = record.DaoTuGroup,
				RelatedDaoTuIds = record.RelatedDaoTuIds,
				DisplayName = record.DisplayName,
				CreatedYear = record.CreatedYear,
				OpenUntilYear = record.OpenUntilYear,
				MaxExploreActors = record.MaxExploreActors,
				ExploredActorCount = record.ExploredActorCount,
				RemainingExploreCount = record.RemainingExploreCount,
				IsOpen = record.IsOpen,
				DeathRateProfileSummary = record.DeathRateProfileSummary,
				RewardPoolSummary = record.RewardPoolSummary,
				Summary = record.Summary,
				AnchorActorId = record.AnchorActorId,
				AnchorActorName = record.AnchorActorName,
				AnchorTileX = record.AnchorTileX,
				AnchorTileY = record.AnchorTileY,
				AnchorCityId = record.AnchorCityId,
				AnchorCityName = record.AnchorCityName,
				AnchorYear = record.AnchorYear,
				AnchorSource = record.AnchorSource,
				LastExploreReserveYear = record.LastExploreReserveYear,
				ExplorerRecords = ExportExplorerRecords(record.ExplorerRecords),
				EntityAssetId = record.EntityAssetId,
				EntityBuildingId = record.EntityBuildingId,
				EntitySpawned = record.EntitySpawned,
				EntityClosed = record.EntityClosed
			});
		}
	}

	internal static void ImportArchiveRecords(IEnumerable<XjDongTianArchiveData> records)
	{
		recordsById.Clear();
		MarkRecordCacheDirty();
		if (records == null)
		{
			return;
		}

		foreach (XjDongTianArchiveData record in records)
		{
			if (record == null || string.IsNullOrWhiteSpace(record.RecordId))
			{
				continue;
			}

			recordsById[record.RecordId.Trim()] = new XjDongTianRecord(
				true,
				record.RecordId,
				record.QiYuDongTianId,
				record.DaoTuGroup,
				record.RelatedDaoTuIds,
				record.DisplayName,
				record.CreatedYear,
				record.OpenUntilYear,
				record.MaxExploreActors,
				record.ExploredActorCount,
				record.RemainingExploreCount,
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
				record.LastExploreReserveYear,
				ImportExplorerRecords(record.ExplorerRecords),
				record.EntityAssetId,
				record.EntityBuildingId,
				record.EntitySpawned,
				record.EntityClosed);
		}
		MarkRecordCacheDirty();
	}

	internal static void Clear()
	{
		recordsById.Clear();
		sortedRecordCache.Clear();
		sortedRecordCacheDirty = true;
		nextSpawnYear = 0;
		lastProcessedYear = -1;
		activeProcessingYear = 0;
		runtimeYearPhase = RuntimeYearPhase.None;
	}

	private static void MarkRecordCacheDirty()
	{
		sortedRecordCacheDirty = true;
	}

	private static void EnsureNextSpawnYear(int currentYear)
	{
		if (nextSpawnYear > 0)
		{
			return;
		}

		nextSpawnYear = GetNextQiYuDongTianSpawnYearAfter(currentYear);
		XjWorldArchiveSystem.MarkChanged();
	}

	private static int GetNextSpawnYearAfter(int currentYear)
	{
		return Math.Max(0, currentYear) + Math.Max(1, XjRuntimeSettings.RollQiYuDongTianSpawnOffset());
	}

	private static int GetNextQiYuDongTianSpawnYearAfter(int currentYear)
	{
		return GetNextSpawnYearAfter(currentYear);
	}

	private static void CloseExpiredOpenRecords(int currentYear)
	{
		if (recordsById.Count == 0)
		{
			return;
		}

		List<string> closedRecordIds = null;
		foreach (KeyValuePair<string, XjDongTianRecord> entry in recordsById)
		{
			XjDongTianRecord record = entry.Value;
			if (!record.Found || !record.IsOpen || record.OpenUntilYear <= 0 || currentYear < record.OpenUntilYear)
			{
				continue;
			}

			closedRecordIds ??= new List<string>();
			closedRecordIds.Add(entry.Key);
		}

		if (closedRecordIds == null)
		{
			return;
		}

		for (int i = 0; i < closedRecordIds.Count; i++)
		{
			string recordId = closedRecordIds[i];
			if (!recordsById.TryGetValue(recordId, out XjDongTianRecord record))
			{
				continue;
			}

			recordsById[recordId] = new XjDongTianRecord(
				record.Found,
				record.RecordId,
				record.QiYuDongTianId,
				record.DaoTuGroup,
				record.RelatedDaoTuIds,
				record.DisplayName,
				record.CreatedYear,
				record.OpenUntilYear,
				record.MaxExploreActors,
				record.ExploredActorCount,
				record.RemainingExploreCount,
				false,
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
				record.LastExploreReserveYear,
				record.ExplorerRecords,
				record.EntityAssetId,
				record.EntityBuildingId,
				record.EntitySpawned,
				false);
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelWorldEvent(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildDongTianClosed(record.DisplayName),
				XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianClose);
			XuanJianVNext.Systems.Events.XjFamilyDomainEventRouter.Publish(
				XuanJianVNext.Data.Events.XjFamilyDomainEvent.DongTianClosed(
					record.AnchorActorId,
					record.AnchorActorName,
					currentYear,
					record.DisplayName));
		}

		MarkRecordCacheDirty();
		XjWorldArchiveSystem.MarkChanged();
	}

	private static void CloseCompletedOpenRecords(int currentYear)
	{
		// 名额用尽不再关闭洞天；开启窗口只按配置年份结束。
	}

	private static bool HasPendingExplorerResult(in XjDongTianRecord record)
	{
		if (record.ExplorerRecords == null) return false;
		for (int i = 0; i < record.ExplorerRecords.Count; i++)
		{
			XjQiYuDongTianExplorerRecord explorer = record.ExplorerRecords[i];
			if (!explorer.Resolved || (explorer.DeathPending && !explorer.DeathFinalized)) return true;
		}
		return false;
	}

	private static XjDongTianRecord CopyWithEntity(in XjDongTianRecord record, string assetId, long buildingId, bool spawned, bool closed)
	{
		string nextAssetId = closed ? string.Empty : (string.IsNullOrWhiteSpace(assetId) ? record.EntityAssetId : assetId);
		long nextBuildingId = closed ? 0L : (buildingId > 0L ? buildingId : record.EntityBuildingId);
		return new XjDongTianRecord(
			record.Found, record.RecordId, record.QiYuDongTianId, record.DaoTuGroup, record.RelatedDaoTuIds,
			record.DisplayName, record.CreatedYear, record.OpenUntilYear, record.MaxExploreActors, record.ExploredActorCount,
			record.RemainingExploreCount, record.IsOpen, record.DeathRateProfileSummary, record.RewardPoolSummary, record.Summary,
			record.AnchorActorId, record.AnchorActorName, record.AnchorTileX, record.AnchorTileY, record.AnchorCityId,
			record.AnchorCityName, record.AnchorYear, record.AnchorSource, record.LastExploreReserveYear, record.ExplorerRecords,
			nextAssetId,
			nextBuildingId, spawned, closed);
	}

	private static bool HasResolvableExplorerRecords(int currentYear)
	{
		if (recordsById.Count == 0)
		{
			return false;
		}

		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (!record.Found || record.ExplorerRecords == null)
			{
				continue;
			}

			for (int i = 0; i < record.ExplorerRecords.Count; i++)
			{
				XjQiYuDongTianExplorerRecord explorer = record.ExplorerRecords[i];
				if (!explorer.Resolved
					&& explorer.ReservedYear < currentYear
					&& !(explorer.DeathPending && !explorer.DeathFinalized))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static void ResolveExplorerRecords(int currentYear, int budget)
	{
		if (recordsById.Count == 0 || budget <= 0)
		{
			return;
		}

		int resolvedCount = 0;
		List<XjDongTianRecord> updatedRecords = null;
		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (!record.Found || record.ExplorerRecords == null || record.ExplorerRecords.Count == 0)
			{
				continue;
			}

			bool changed = false;
			List<XjQiYuDongTianExplorerRecord> explorerRecords = new List<XjQiYuDongTianExplorerRecord>(record.ExplorerRecords.Count);
			for (int i = 0; i < record.ExplorerRecords.Count; i++)
			{
				XjQiYuDongTianExplorerRecord explorerRecord = record.ExplorerRecords[i];
				if (explorerRecord.Resolved
					|| explorerRecord.ReservedYear >= currentYear
					|| (explorerRecord.DeathPending && !explorerRecord.DeathFinalized)
					|| resolvedCount >= budget)
				{
					explorerRecords.Add(explorerRecord);
					continue;
				}

				XjQiYuDongTianExplorerRecord resolved = ResolveExplorerRecord(record, explorerRecord, currentYear);
				explorerRecords.Add(resolved);
				changed = true;
				resolvedCount++;
			}

			if (!changed)
			{
				continue;
			}

			updatedRecords ??= new List<XjDongTianRecord>();
			updatedRecords.Add(new XjDongTianRecord(
				record.Found,
				record.RecordId,
				record.QiYuDongTianId,
				record.DaoTuGroup,
				record.RelatedDaoTuIds,
				record.DisplayName,
				record.CreatedYear,
				record.OpenUntilYear,
				record.MaxExploreActors,
				record.ExploredActorCount,
				record.RemainingExploreCount,
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
				record.LastExploreReserveYear,
				explorerRecords,
				record.EntityAssetId,
				record.EntityBuildingId,
				record.EntitySpawned,
				record.EntityClosed));

			if (resolvedCount >= budget)
			{
				break;
			}
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

	private static XjQiYuDongTianExplorerRecord ResolveExplorerRecord(
		XjDongTianRecord record,
		XjQiYuDongTianExplorerRecord explorerRecord,
		int currentYear)
	{
		if (!TryResolveKnownActor(explorerRecord.ExplorerActorId, out Actor actor)
			|| actor?.data == null
			|| !XjSafeCore.IsAliveActor(actor))
		{
			return BuildResolvedExplorerRecord(explorerRecord, currentYear, false, false, "MissingActor", "探索者已失踪或已死亡");
		}

		float deathChance = ResolveDeathChance(explorerRecord.RealmId);
		if (UnityEngine.Random.value < deathChance)
		{
			MarkQiYuDongTianDeathPending(actor, record.RecordId, currentYear);
			XjQiYuDongTianDeathLane.EnqueueActor(actor);
			return BuildDeathPendingExplorerRecord(explorerRecord, currentYear);
		}

		string rewardType = "None";
		string rewardSummary = ApplyRewardSet(actor, record, currentYear, out rewardType);
		bool rewardApplied = !string.IsNullOrWhiteSpace(rewardSummary);
		if (!rewardApplied)
		{
			rewardSummary = "未获得可安全接入奖励";
		}

		XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor,
			XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildDongTianSurvival(
				actor, record.DisplayName, rewardApplied, rewardSummary),
			null,
			XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianOpen);
		XuanJianVNext.Systems.Events.XjFamilyDomainEventRouter.Publish(
			XuanJianVNext.Data.Events.XjFamilyDomainEvent.DongTianSurvived(
					actor, record.DisplayName, rewardType, rewardApplied ? rewardSummary : string.Empty));

		return BuildResolvedExplorerRecord(explorerRecord, currentYear, false, rewardApplied, rewardType, rewardSummary);
	}

	private static XjQiYuDongTianExplorerRecord BuildResolvedExplorerRecord(
		XjQiYuDongTianExplorerRecord source,
		int currentYear,
		bool deathResolved,
		bool rewardApplied,
		string rewardType,
		string resultSummary)
	{
		return new XjQiYuDongTianExplorerRecord(
			source.ExplorerActorId,
			source.ExplorerActorName,
			source.RealmId,
			source.RealmDisplay,
			source.DaoTu,
			source.DistanceToAnchor,
			source.ReservedYear,
			true,
			currentYear,
			deathResolved,
			rewardApplied,
			rewardType,
			resultSummary,
			false,
			source.DeathPendingYear,
			source.DeathFinalized,
			source.DeathFinalizedYear);
	}

	private static XjQiYuDongTianExplorerRecord BuildDeathPendingExplorerRecord(XjQiYuDongTianExplorerRecord source, int currentYear)
	{
		return new XjQiYuDongTianExplorerRecord(
			source.ExplorerActorId,
			source.ExplorerActorName,
			source.RealmId,
			source.RealmDisplay,
			source.DaoTu,
			source.DistanceToAnchor,
			source.ReservedYear,
			false,
			0,
			false,
			false,
			string.Empty,
			string.Empty,
			true,
			currentYear,
			false,
			0);
	}

	internal static bool FinalizeExplorerDeath(string recordId, long actorId, int currentYear)
	{
		string safeRecordId = recordId == null ? string.Empty : recordId.Trim();
		if (string.IsNullOrWhiteSpace(safeRecordId) || actorId <= 0L || !recordsById.TryGetValue(safeRecordId, out XjDongTianRecord record))
		{
			return false;
		}

		if (record.ExplorerRecords == null || record.ExplorerRecords.Count == 0)
		{
			return false;
		}

		bool changed = false;
		int finalizedYear = currentYear > 0 ? currentYear : 0;
		List<XjQiYuDongTianExplorerRecord> explorerRecords = new List<XjQiYuDongTianExplorerRecord>(record.ExplorerRecords.Count);
		for (int i = 0; i < record.ExplorerRecords.Count; i++)
		{
			XjQiYuDongTianExplorerRecord explorerRecord = record.ExplorerRecords[i];
			if (!changed
				&& explorerRecord.ExplorerActorId == actorId
				&& explorerRecord.DeathPending
				&& !explorerRecord.DeathFinalized)
			{
				int safeYear = currentYear > 0 ? currentYear : explorerRecord.DeathPendingYear;
				finalizedYear = safeYear;
				explorerRecords.Add(new XjQiYuDongTianExplorerRecord(
					explorerRecord.ExplorerActorId,
					explorerRecord.ExplorerActorName,
					explorerRecord.RealmId,
					explorerRecord.RealmDisplay,
					explorerRecord.DaoTu,
					explorerRecord.DistanceToAnchor,
					explorerRecord.ReservedYear,
					true,
					safeYear,
					true,
					false,
					"Death",
					"探索身死",
					false,
					explorerRecord.DeathPendingYear,
					true,
					safeYear));
				changed = true;
				continue;
			}

			explorerRecords.Add(explorerRecord);
		}

		if (!changed)
		{
			return false;
		}

		string explorerName = "探索者";
		for (int i = 0; i < record.ExplorerRecords.Count; i++)
		{
			XjQiYuDongTianExplorerRecord explorerRecord = record.ExplorerRecords[i];
			if (explorerRecord.ExplorerActorId == actorId)
			{
				explorerName = string.IsNullOrWhiteSpace(explorerRecord.ExplorerActorName)
					? "探索者"
					: explorerRecord.ExplorerActorName;
				break;
			}
		}
		XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelWorldEvent(
			XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildDongTianDeath(explorerName, record.DisplayName),
			XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianDeath);
		XuanJianVNext.Systems.Events.XjFamilyDomainEventRouter.Publish(
			XuanJianVNext.Data.Events.XjFamilyDomainEvent.DongTianDeath(
				actorId, explorerName, finalizedYear, record.DisplayName));

		recordsById[record.RecordId] = new XjDongTianRecord(
			record.Found,
			record.RecordId,
			record.QiYuDongTianId,
			record.DaoTuGroup,
			record.RelatedDaoTuIds,
			record.DisplayName,
			record.CreatedYear,
			record.OpenUntilYear,
			record.MaxExploreActors,
			record.ExploredActorCount,
			record.RemainingExploreCount,
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
			record.LastExploreReserveYear,
			explorerRecords,
			record.EntityAssetId,
			record.EntityBuildingId,
			record.EntitySpawned,
			record.EntityClosed);
		MarkRecordCacheDirty();
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	private static void MarkQiYuDongTianDeathPending(Actor actor, string recordId, int currentYear)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathPending, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathYear, currentYear < 0 ? 0 : currentYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiYuDongTianDeathRecordId, recordId ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiYuDongTianDeathReason, "奇遇洞天探索身死");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathHandled, 0);
	}

	private static bool TryResolveKnownActor(long actorId, out Actor actor)
	{
		if (actorId <= 0L)
		{
			actor = null;
			return false;
		}

		return XjScheduler.ResolveActor(actorId, out actor);
	}

	private static float ResolveDeathChance(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return XjDongTianRules.DeathChanceZiFu;
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return XjDongTianRules.DeathChanceZhuJi;
		}

		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return XjDongTianRules.DeathChanceLianQi;
		}

		return XjDongTianRules.DeathChanceTaiXi;
	}
}
