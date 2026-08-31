using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.DongTian;

internal static partial class XjDongTianRegistry
{
	private static string SafeActorName(Actor actor) => XjStringHelper.ActorName(actor, "未名修士");

	private static readonly Dictionary<string, XjDongTianRecord> recordsById = new Dictionary<string, XjDongTianRecord>(StringComparer.Ordinal);
	private static readonly List<XjDongTianRecord> sortedRecordCache = new List<XjDongTianRecord>();
	private static bool sortedRecordCacheDirty = true;

	private static int nextSpawnYear;
	private static int successfulSpawnCount;
	private static int persistedCadenceYears;
	private static int observedCadenceYears;
	private static bool strictScheduleNeedsReconcile = true;
	private const int MaxLegacyStrictCatchUpSpawns = 3;

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
			if (activeProcessingYear > 0) return true;
			if (!TryGetCurrentWorldYear(out int currentYear)) return false;
			int configuredCadence = XjRuntimeSettings.GetQiYuDongTianSpawnYears();
			return strictScheduleNeedsReconcile
				|| observedCadenceYears != configuredCadence
				|| currentYear > lastProcessedYear
				|| (nextSpawnYear > 0 && nextSpawnYear <= currentYear);
		}
	}

	internal static bool HasActiveRuntimeYear => activeProcessingYear > 0;

	internal static void TickRuntime(XjCooperativeBudget budget)
	{
		if (budget == null || budget.ShouldYield || !TryGetCurrentWorldYear(out int currentWorldYear))
		{
			return;
		}

		if (lastProcessedYear < 0)
		{
			// Load baseline: process the current year once, never replay the
			// entire historical calendar from year zero.
			lastProcessedYear = Math.Max(0, currentWorldYear - 1);
		}

		ReconcileStrictSpawnSchedule(currentWorldYear);

		// 配置缩短或旧档漏期时，lastProcessedYear 可能已经追到当前世界年。
		// 这时仍允许在同一世界年用合作预算一次补一座；若仍欠期，下一帧继续，
		// 不重新扫描人口、不重放历史年度。
		if (activeProcessingYear <= 0
			&& lastProcessedYear >= currentWorldYear
			&& nextSpawnYear > 0
			&& nextSpawnYear <= currentWorldYear)
		{
			if (!budget.TryTake()) return;
			ProcessStrictDueSpawnOnly(currentWorldYear);
			return;
		}

		if (activeProcessingYear <= 0)
		{
			if (lastProcessedYear >= currentWorldYear || !budget.TryTake())
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
			if (!ResolveExplorerRecords(activeProcessingYear, budget))
			{
				// The scan itself is cooperative. If its slice expires, do not follow
				// it with a second unbounded full-record verification pass; simply
				// resume the sparse scan next frame.
				return;
			}
			runtimeYearPhase = RuntimeYearPhase.FinalizeYear;
		}

		if (runtimeYearPhase == RuntimeYearPhase.FinalizeYear)
		{
			if (!budget.TryTake()) return;
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

	internal static int ExportSpawnCount()
	{
		return Math.Max(0, successfulSpawnCount);
	}

	internal static int ExportCadenceYears()
	{
		return persistedCadenceYears > 0
			? persistedCadenceYears
			: XjRuntimeSettings.GetQiYuDongTianSpawnYears();
	}

	internal static void ImportRuntimeYears(int nextYear, int completedYear, int spawnCount, int cadenceYears)
	{
		nextSpawnYear = nextYear < 0 ? 0 : nextYear;
		lastProcessedYear = completedYear < 0 ? -1 : completedYear;
		successfulSpawnCount = Math.Max(0, spawnCount);
		persistedCadenceYears = Math.Max(0, cadenceYears);
		observedCadenceYears = 0;
		strictScheduleNeedsReconcile = true;
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

			bool knownCatalog = XjDongTianRules.TryResolveCatalogEntry(record.QiYuDongTianId, out XjDongTianCatalogEntry catalogEntry);
			string qiYuId = knownCatalog ? catalogEntry.QiYuDongTianId : record.QiYuDongTianId;
			bool permanentWorldSite = XjDongTianRules.IsPermanentWorldSite(qiYuId);
			bool luoXiaShan = XjDongTianRules.IsLuoXiaShanDongTian(qiYuId);
			recordsById[record.RecordId.Trim()] = new XjDongTianRecord(
				true,
				record.RecordId,
				qiYuId,
				knownCatalog ? catalogEntry.DaoTuGroup : record.DaoTuGroup,
				knownCatalog ? catalogEntry.RelatedDaoTuIds : record.RelatedDaoTuIds,
				knownCatalog ? catalogEntry.DisplayName : record.DisplayName,
				record.CreatedYear,
				permanentWorldSite ? 0 : record.OpenUntilYear,
				permanentWorldSite ? 0 : record.MaxExploreActors,
				record.ExploredActorCount,
				permanentWorldSite ? 0 : record.RemainingExploreCount,
				record.IsOpen,
				knownCatalog ? XjDongTianRules.FormatDeathRateProfileSummary(qiYuId) : record.DeathRateProfileSummary,
				knownCatalog ? XjDongTianRules.FormatRewardPoolSummary(qiYuId) : record.RewardPoolSummary,
				knownCatalog ? XjDongTianRules.FormatDongTianSummary(catalogEntry) : record.Summary,
				record.AnchorActorId,
				record.AnchorActorName,
				record.AnchorTileX,
				record.AnchorTileY,
				record.AnchorCityId,
				record.AnchorCityName,
				record.AnchorYear,
				record.AnchorSource,
				record.LastExploreReserveYear,
				luoXiaShan ? Array.Empty<XjQiYuDongTianExplorerRecord>() : ImportExplorerRecords(record.ExplorerRecords),
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
		successfulSpawnCount = 0;
		persistedCadenceYears = 0;
		observedCadenceYears = 0;
		strictScheduleNeedsReconcile = true;
		activeProcessingYear = 0;
		runtimeYearPhase = RuntimeYearPhase.None;
	}

	private static void MarkRecordCacheDirty()
	{
		sortedRecordCacheDirty = true;
	}

	private static void EnsureNextSpawnYear(int currentYear)
	{
		ReconcileStrictSpawnSchedule(currentYear);
		if (nextSpawnYear > 0) return;
		nextSpawnYear = GetCurrentOrNextStrictSpawnYear(currentYear);
		XjWorldArchiveSystem.MarkChanged();
	}

	private static void ReconcileStrictSpawnSchedule(int currentWorldYear)
	{
		int cadence = XjRuntimeSettings.GetQiYuDongTianSpawnYears();
		bool cadenceChanged = persistedCadenceYears > 0 && persistedCadenceYears != cadence;
		bool runtimeChanged = observedCadenceYears > 0 && observedCadenceYears != cadence;
		if (!strictScheduleNeedsReconcile && !cadenceChanged && !runtimeChanged) return;

		int oldNext = nextSpawnYear;
		int oldCount = successfulSpawnCount;
		int xuanJianYear = Math.Max(0, XjChronology.ToXuanJianYear(currentWorldYear));
		int dueSlots = xuanJianYear <= 0 ? 0 : xuanJianYear / cadence;

		if (persistedCadenceYears <= 0 || cadenceChanged)
		{
			// 旧版没有严格周期游标。十大道途首次显世阶段会优先选择未出现洞天，
			// 因而“已存在的普通洞天种类数”是早期存档最可靠的成功次数下界。
			// 为避免几千年旧档改成100年后同帧补几十次，只允许迁移阶段最多欠三期。
			int inferredSuccesses = CountDistinctOrdinaryQiYuRecords();
			if (cadenceChanged && cadence > persistedCadenceYears && persistedCadenceYears > 0)
			{
				// 周期调长时不追溯删除既有洞天，也不把过去较密的显化算作未来欠账。
				inferredSuccesses = dueSlots;
			}
			else if (dueSlots > inferredSuccesses + MaxLegacyStrictCatchUpSpawns)
			{
				inferredSuccesses = dueSlots - MaxLegacyStrictCatchUpSpawns;
			}
			successfulSpawnCount = Math.Max(0, inferredSuccesses);
		}

		persistedCadenceYears = cadence;
		observedCadenceYears = cadence;
		strictScheduleNeedsReconcile = false;

		if (successfulSpawnCount < dueSlots)
		{
			// 已经错过的严格节点保持“欠期”状态；合作调度每次只补一座。
			nextSpawnYear = Math.Max(1, currentWorldYear);
		}
		else
		{
			nextSpawnYear = GetNextQiYuDongTianSpawnYearAfter(currentWorldYear);
		}

		if (nextSpawnYear != oldNext || successfulSpawnCount != oldCount || cadenceChanged || runtimeChanged)
		{
			XjWorldArchiveSystem.MarkChanged();
		}
	}

	private static int CountDistinctOrdinaryQiYuRecords()
	{
		if (recordsById.Count == 0) return 0;
		int count = 0;
		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (!record.Found || string.IsNullOrWhiteSpace(record.QiYuDongTianId)) continue;
			if (XjDongTianRules.IsPermanentWorldSite(record.QiYuDongTianId)) continue;
			count++;
		}
		return count;
	}

	private static int GetCurrentOrNextStrictSpawnYear(int currentWorldYear)
	{
		return ResolveStrictSpawnYear(currentWorldYear, includeCurrentMilestone: true);
	}

	private static int GetNextQiYuDongTianSpawnYearAfter(int currentWorldYear)
	{
		return ResolveStrictSpawnYear(currentWorldYear, includeCurrentMilestone: false);
	}

	private static int ResolveStrictSpawnYear(int currentWorldYear, bool includeCurrentMilestone)
	{
		int cadence = XjRuntimeSettings.GetQiYuDongTianSpawnYears();
		int xuanJianYear = XjChronology.ToXuanJianYear(currentWorldYear);
		if (xuanJianYear <= 0) return Math.Max(1, currentWorldYear) + cadence;

		int slot = includeCurrentMilestone
			? (xuanJianYear + cadence - 1) / cadence
			: xuanJianYear / cadence + 1;
		int targetXuanJianYear = Math.Max(1, slot) * cadence;
		int worldYear = XjChronology.ToWorldYear(targetXuanJianYear);
		return worldYear > 0 ? worldYear : Math.Max(1, currentWorldYear) + cadence;
	}

	private static void ProcessStrictDueSpawnOnly(int currentWorldYear)
	{
		CloseExpiredOpenRecords(currentWorldYear);
		if (nextSpawnYear <= 0 || currentWorldYear < nextSpawnYear) return;
		CreateRandomQiYuDongTian(currentWorldYear);
		XjDongTianEntitySystem.Tick(ReadAllEntries(), 1);
		ReserveExplorersForOpenRecords(currentWorldYear);
	}

	private static void ScheduleAfterSuccessfulStrictSpawn(int currentWorldYear)
	{
		successfulSpawnCount = Math.Max(0, successfulSpawnCount) + 1;
		int cadence = XjRuntimeSettings.GetQiYuDongTianSpawnYears();
		persistedCadenceYears = cadence;
		observedCadenceYears = cadence;
		strictScheduleNeedsReconcile = false;
		int xuanJianYear = Math.Max(0, XjChronology.ToXuanJianYear(currentWorldYear));
		int dueSlots = xuanJianYear <= 0 ? 0 : xuanJianYear / cadence;
		nextSpawnYear = successfulSpawnCount < dueSlots
			? Math.Max(1, currentWorldYear)
			: GetNextQiYuDongTianSpawnYearAfter(currentWorldYear);
		XjWorldArchiveSystem.MarkChanged();
	}

	private static void ScheduleRetryAfterFailedStrictSpawn(int currentWorldYear)
	{
		// 当前节点由于锚点/目录等客观条件无法显化时，只把“欠期”延后一年来重试；
		// 成功以后仍回到玄鉴历100/200/300…这类固定节点，不从实际成功年重新起算。
		nextSpawnYear = Math.Max(1, currentWorldYear + 1);
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static int CountOpenQiYuDongTian(int currentYear)
	{
		if (recordsById.Count == 0) return 0;
		int count = 0;
		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (record.Found && record.IsOpen
				&& (record.OpenUntilYear <= 0 || currentYear < record.OpenUntilYear)
				&& !string.IsNullOrWhiteSpace(record.QiYuDongTianId)) count++;
		}
		return count;
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
				XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianClose,
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.DongTian);
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

	private static bool ResolveExplorerRecords(int currentYear, XjCooperativeBudget budget)
	{
		if (recordsById.Count == 0) return true;
		if (budget == null || budget.ShouldYield) return false;

		bool completedScan = true;
		List<XjDongTianRecord> updatedRecords = null;
		foreach (XjDongTianRecord record in recordsById.Values)
		{
			if (budget.ShouldYield)
			{
				completedScan = false;
				break;
			}
			if (!record.Found || record.ExplorerRecords == null || record.ExplorerRecords.Count == 0)
			{
				continue;
			}

			List<XjQiYuDongTianExplorerRecord> explorerRecords = null;
			for (int i = 0; i < record.ExplorerRecords.Count; i++)
			{
				if (budget.ShouldYield)
				{
					completedScan = false;
					break;
				}
				XjQiYuDongTianExplorerRecord explorerRecord = record.ExplorerRecords[i];
				if (explorerRecord.Resolved
					|| explorerRecord.ReservedYear >= currentYear
					|| (explorerRecord.DeathPending && !explorerRecord.DeathFinalized))
				{
					continue;
				}

				if (!budget.TryTake())
				{
					completedScan = false;
					break;
				}
				explorerRecords ??= new List<XjQiYuDongTianExplorerRecord>(record.ExplorerRecords);
				explorerRecords[i] = ResolveExplorerRecord(record, explorerRecord, currentYear);
			}

			if (explorerRecords != null)
			{
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
			}

			if (!completedScan) break;
		}

		if (updatedRecords != null)
		{
			for (int i = 0; i < updatedRecords.Count; i++)
			{
				XjDongTianRecord updated = updatedRecords[i];
				recordsById[updated.RecordId] = updated;
			}

			MarkRecordCacheDirty();
			XjWorldArchiveSystem.MarkChanged();
		}

		return completedScan;
	}

	private static XjQiYuDongTianExplorerRecord ResolveExplorerRecord(
		XjDongTianRecord record,
		XjQiYuDongTianExplorerRecord explorerRecord,
		int currentYear)
	{
		// 水月照真不是普通洞天冒险：预约记录只代表一次道尊传召，绝不进入随机死亡/通用掉落池。
		if (XjDongTianRules.IsYuanZhaoDongTian(record.QiYuDongTianId))
		{
			return ResolveYuanZhaoAudienceRecord(record, explorerRecord, currentYear);
		}

		if (!TryResolveKnownActor(explorerRecord.ExplorerActorId, out Actor actor)
			|| actor?.data == null
			|| !XjSafeCore.IsAliveActor(actor))
		{
			return BuildResolvedExplorerRecord(explorerRecord, currentYear, false, false, "MissingActor", "探索者已失踪或已死亡");
		}

		XjDongTianRewardProfile profile = XjDongTianRules.GetRewardProfile(record.QiYuDongTianId);
		bool relatedDaoTu = XjDongTianRules.IsRelatedDaoTu(record.QiYuDongTianId, record.RelatedDaoTuIds, explorerRecord.DaoTu);
		float deathChance = ResolveDeathChance(explorerRecord.RealmId) * profile.DeathMultiplier;
		if (relatedDaoTu) deathChance *= XjDongTianRules.RelatedDaoTuDeathMultiplier;
		float sectExpeditionDeathMultiplier = ResolveSectExpeditionDeathMultiplier(record, explorerRecord, actor);
		deathChance *= sectExpeditionDeathMultiplier;
		deathChance = Math.Clamp(deathChance, 0.03f, 0.95f);
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
			XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianOpen,
			XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.DongTian);
		XuanJianVNext.Systems.Events.XjFamilyDomainEventRouter.Publish(
			XuanJianVNext.Data.Events.XjFamilyDomainEvent.DongTianSurvived(
					actor, record.DisplayName, rewardType, rewardApplied ? rewardSummary : string.Empty));
		if (sectExpeditionDeathMultiplier < 0.999f)
		{
			RecordSectExpeditionOutcome(record, explorerRecord, actor, currentYear, relatedDaoTu, rewardApplied, rewardSummary);
		}

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
			XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.DongTianDeath,
			XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.DongTian);
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
		if (!string.IsNullOrWhiteSpace(realmId) && realmId.StartsWith("shi:", StringComparison.Ordinal))
		{
			string shiRealm = realmId.Substring(4);
			int rank = XjShiCatalog.GetRank(shiRealm);
			if (rank >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)) return 0.08f;
			if (rank >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe)) return 0.12f;
			if (rank >= XjShiCatalog.GetRank(XjShiRealmIds.LianMin)) return 0.20f;
			if (rank >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster)) return 0.30f;
			return 0.45f;
		}
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return 0.06f;
		if (XjCultivationPathRules.IsJinDanEquivalentRealm(realmId)) return 0.12f;
		if (XjCultivationPathRules.IsZhenRenEquivalentRealm(realmId))
		{
			return XjDongTianRules.DeathChanceZiFu;
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
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
