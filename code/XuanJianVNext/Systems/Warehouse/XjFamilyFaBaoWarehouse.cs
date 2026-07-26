using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Codex;

namespace XuanJianVNext.Systems.Warehouse;

internal readonly struct XjFamilyFaBaoWarehouseEntry
{
	internal readonly bool Found;
	internal readonly long FamilyStableId;
	internal readonly long SectId;
	internal readonly string SectName;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string FaBaoId;
	internal readonly string FaBaoName;
	internal readonly string DaoTu;
	internal readonly string ClassName;
	internal readonly string Source;
	internal readonly int Year;

	internal XjFamilyFaBaoWarehouseEntry(
		bool found,
		long familyStableId,
		long actorId,
		string actorName,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
		: this(
			found,
			familyStableId,
			0L,
			string.Empty,
			actorId,
			actorName,
			faBaoId,
			faBaoName,
			daoTu,
			className,
			source,
			year)
	{
	}

	internal XjFamilyFaBaoWarehouseEntry(
		bool found,
		long familyStableId,
		long sectId,
		string sectName,
		long actorId,
		string actorName,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
	{
		Found = found;
		FamilyStableId = familyStableId < 0L ? 0L : familyStableId;
		SectId = sectId < 0L ? 0L : sectId;
		SectName = sectName ?? string.Empty;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		FaBaoId = faBaoId ?? string.Empty;
		FaBaoName = faBaoName ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		ClassName = className ?? string.Empty;
		Source = source ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

internal static class XjFamilyFaBaoWarehouse
{
	internal const string SourceTypeJinDan = "JinDan";
	internal const string SourceTypeLiveCraft = "LiveCraft";
	internal const string SourceTypeDeathSnapshot = "DeathSnapshot";
	internal const string SourceTypeLostDiscovery = "LostDiscovery";
	internal const string SourceTypeFamilyExtinction = "FamilyExtinction";
	internal const string SourceTypeSectExtinction = "SectExtinction";
	internal const string SourceTypeSectContribution = "SectContribution";

	private static readonly Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>> entriesByFamilyId = new Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>>();
	private static readonly HashSet<string> entryKeys = new HashSet<string>();
	private static readonly Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>> entriesBySectId = new Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>>();
	private static readonly HashSet<string> sectEntryKeys = new HashSet<string>();
	private static readonly List<XjFamilyFaBaoWarehouseEntry> lostEntries = new List<XjFamilyFaBaoWarehouseEntry>();
	private static readonly HashSet<string> lostEntryKeys = new HashSet<string>();
	private static readonly HashSet<string> mutatedLostEntryKeysSinceImport = new HashSet<string>(System.StringComparer.Ordinal);
	// 保存保护层默认会把缺失的永久器物从基线档案补回。所有真实所有权转移/散佚必须显式标记，
	// 否则家族或宗门条目会在保存时被复活，造成一物两份。
	private static readonly HashSet<string> mutatedOwnedEntryKeysSinceImport = new HashSet<string>(System.StringComparer.Ordinal);
	private static readonly List<long> extinctFamilyScratch = new List<long>();
	private static readonly List<long> extinctSectScratch = new List<long>();
	private static readonly HashSet<long> explicitlyExtinctFamilyScratch = new HashSet<long>();

	internal static bool HasLostEntries => lostEntries.Count > 0;

	internal static bool HasLostEntriesAtOrBeforeYear(int year)
	{
		if (year <= 0) return false;
		for (int i = 0; i < lostEntries.Count; i++)
		{
			if (lostEntries[i].Year <= year) return true;
		}
		return false;
	}
	internal static bool HasFamilyEntries => entryKeys.Count > 0;
	internal static bool HasSectEntries => sectEntryKeys.Count > 0;

	internal static bool AddFaBaoToFamily(
		long actorId,
		string actorName,
		long familyStableId,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
	{
		if (actorId <= 0L
			|| familyStableId <= 0L
			|| string.IsNullOrWhiteSpace(faBaoId)
			|| string.IsNullOrWhiteSpace(faBaoName)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		string key = familyStableId + "|" + faBaoId.Trim() + "|" + daoTu.Trim();
		if (!entryKeys.Add(key))
		{
			return TryUpdateFamilyEntry(
				familyStableId,
				key,
				new XjFamilyFaBaoWarehouseEntry(
					true,
					familyStableId,
					actorId,
					actorName,
					faBaoId.Trim(),
					faBaoName.Trim(),
					daoTu.Trim(),
					className,
					source,
					year));
		}

		if (!entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries))
		{
			entries = new List<XjFamilyFaBaoWarehouseEntry>();
			entriesByFamilyId[familyStableId] = entries;
		}

		entries.Add(new XjFamilyFaBaoWarehouseEntry(
			true,
			familyStableId,
			actorId,
			actorName,
			faBaoId.Trim(),
			faBaoName.Trim(),
			daoTu.Trim(),
			className,
			source,
			year));
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool AddFaBaoToSect(
		long actorId,
		string actorName,
		long sectId,
		string sectName,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
	{
		if (actorId <= 0L
			|| sectId <= 0L
			|| string.IsNullOrWhiteSpace(faBaoId)
			|| string.IsNullOrWhiteSpace(faBaoName)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		string key = sectId + "|" + faBaoId.Trim() + "|" + daoTu.Trim();
		XjFamilyFaBaoWarehouseEntry replacement = new XjFamilyFaBaoWarehouseEntry(
			true,
			0L,
			sectId,
			sectName,
			actorId,
			actorName,
			faBaoId.Trim(),
			faBaoName.Trim(),
			daoTu.Trim(),
			className,
			source,
			year);
		if (!sectEntryKeys.Add(key))
		{
			return TryUpdateSectEntry(sectId, key, replacement);
		}

		if (!entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries))
		{
			entries = new List<XjFamilyFaBaoWarehouseEntry>();
			entriesBySectId[sectId] = entries;
		}

		entries.Add(replacement);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	private static bool TryUpdateFamilyEntry(long familyStableId, string key, XjFamilyFaBaoWarehouseEntry replacement)
	{
		if (!entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries == null
			|| entries.Count == 0)
		{
			return false;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry existing = entries[i];
			string existingKey = existing.FamilyStableId + "|" + existing.FaBaoId.Trim() + "|" + existing.DaoTu.Trim();
			if (!string.Equals(existingKey, key, System.StringComparison.Ordinal))
			{
				continue;
			}

			if (string.Equals(existing.FaBaoName, replacement.FaBaoName, System.StringComparison.Ordinal)
				&& string.Equals(existing.ClassName, replacement.ClassName, System.StringComparison.Ordinal)
				&& string.Equals(existing.Source, replacement.Source, System.StringComparison.Ordinal)
				&& existing.Year == replacement.Year
				&& existing.ActorId == replacement.ActorId)
			{
				return false;
			}

			entries[i] = replacement;
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
			return true;
		}

		return false;
	}

	private static bool TryUpdateSectEntry(long sectId, string key, XjFamilyFaBaoWarehouseEntry replacement)
	{
		if (!entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries == null
			|| entries.Count == 0)
		{
			return false;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry existing = entries[i];
			string existingKey = existing.SectId + "|" + existing.FaBaoId.Trim() + "|" + existing.DaoTu.Trim();
			if (!string.Equals(existingKey, key, System.StringComparison.Ordinal))
			{
				continue;
			}

			if (string.Equals(existing.FaBaoName, replacement.FaBaoName, System.StringComparison.Ordinal)
				&& string.Equals(existing.ClassName, replacement.ClassName, System.StringComparison.Ordinal)
				&& string.Equals(existing.Source, replacement.Source, System.StringComparison.Ordinal)
				&& string.Equals(existing.SectName, replacement.SectName, System.StringComparison.Ordinal)
				&& existing.Year == replacement.Year
				&& existing.ActorId == replacement.ActorId)
			{
				return false;
			}

			entries[i] = replacement;
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
			return true;
		}

		return false;
	}

	internal static bool RecordLostFaBao(
		long actorId,
		string actorName,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
	{
		if (actorId <= 0L
			|| string.IsNullOrWhiteSpace(faBaoId)
			|| string.IsNullOrWhiteSpace(faBaoName)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		string key = BuildLostKey(actorId, faBaoId, daoTu);
		if (!lostEntryKeys.Add(key))
		{
			return false;
		}

		lostEntries.Add(new XjFamilyFaBaoWarehouseEntry(
			true,
			0L,
			actorId,
			actorName,
			faBaoId.Trim(),
			faBaoName.Trim(),
			daoTu.Trim(),
			className,
			string.IsNullOrWhiteSpace(source) ? "LostOnDeath" : source,
			year));
		mutatedLostEntryKeysSinceImport.Add(key);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadFamilyEntriesView(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoWarehouseEntry>();
		}

		// 瞬时只读投影，避免家族详情为每个家族复制仓库列表；调用方不得持有或修改。
		return entries;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadFamilyEntries(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoWarehouseEntry>();
		}

		return new List<XjFamilyFaBaoWarehouseEntry>(entries);
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadSectEntriesView(long sectId)
	{
		if (sectId <= 0L
			|| !entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoWarehouseEntry>();
		}

		// 瞬时只读投影，避免宗门列表为每个宗门复制器库。调用方不得持有或修改。
		return entries;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadSectEntries(long sectId)
	{
		IReadOnlyList<XjFamilyFaBaoWarehouseEntry> entries = ReadSectEntriesView(sectId);
		return entries.Count == 0
			? System.Array.Empty<XjFamilyFaBaoWarehouseEntry>()
			: new List<XjFamilyFaBaoWarehouseEntry>(entries);
	}

	internal static int CountFamilyEntries(long familyStableId)
	{
		return familyStableId > 0L
			&& entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			&& entries != null
			? entries.Count
			: 0;
	}

	internal static int CountSectEntries(long sectId)
	{
		return sectId > 0L
			&& entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			&& entries != null
			? entries.Count
			: 0;
	}

	/// <summary>
	/// 将家族余器中的最低阶一件真实转入宗门共库。家族至少保留一件器物，
	/// 且宗门仅在尚无自有重宝时执行；家族扣除后写入宗门，写入失败立即回滚。
	/// </summary>
	internal static bool TryContributeSurplusFamilyTreasureToSect(
		long familyStableId,
		long sectId,
		string sectName,
		int currentYear,
		out string treasureName)
	{
		treasureName = string.Empty;
		if (familyStableId <= 0L || sectId <= 0L || CountSectEntries(sectId) > 0
			|| !entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries == null || entries.Count < 2)
		{
			return false;
		}

		int candidateIndex = -1;
		int candidateRank = int.MaxValue;
		int validEntryCount = 0;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry candidate = entries[i];
			if (!candidate.Found || candidate.ActorId <= 0L || string.IsNullOrWhiteSpace(candidate.FaBaoId)
				|| string.IsNullOrWhiteSpace(candidate.FaBaoName) || string.IsNullOrWhiteSpace(candidate.DaoTu)) continue;
			validEntryCount++;
			int rank = ResolveArtifactRank(candidate.ClassName);
			if (candidateIndex < 0 || rank < candidateRank
				|| rank == candidateRank && candidate.Year < entries[candidateIndex].Year
				|| rank == candidateRank && candidate.Year == entries[candidateIndex].Year
					&& string.CompareOrdinal(candidate.FaBaoName, entries[candidateIndex].FaBaoName) > 0)
			{
				candidateIndex = i;
				candidateRank = rank;
			}
		}
		if (candidateIndex < 0 || validEntryCount < 2) return false;

		XjFamilyFaBaoWarehouseEntry selected = entries[candidateIndex];
		string familyKey = familyStableId + "|" + selected.FaBaoId.Trim() + "|" + selected.DaoTu.Trim();
		string familyOwnerKey = BuildOwnedKey(familyStableId, 0L, selected.FaBaoId, selected.DaoTu);
		bool familyOwnerWasAlreadyMutated = mutatedOwnedEntryKeysSinceImport.Contains(familyOwnerKey);
		mutatedOwnedEntryKeysSinceImport.Add(familyOwnerKey);
		entries.RemoveAt(candidateIndex);
		entryKeys.Remove(familyKey);
		bool added = AddFaBaoToSect(
			selected.ActorId,
			selected.ActorName,
			sectId,
			sectName,
			selected.FaBaoId,
			selected.FaBaoName,
			selected.DaoTu,
			selected.ClassName,
			SourceTypeSectContribution,
			Math.Max(0, currentYear));
		if (!added)
		{
			entries.Insert(Math.Min(candidateIndex, entries.Count), selected);
			entryKeys.Add(familyKey);
			if (!familyOwnerWasAlreadyMutated) mutatedOwnedEntryKeysSinceImport.Remove(familyOwnerKey);
			return false;
		}

		treasureName = selected.FaBaoName.Trim();
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SectResource
			| XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
		return true;
	}

	private static int ResolveArtifactRank(string className)
	{
		string value = className?.Trim() ?? string.Empty;
		if (value.IndexOf("法宝", StringComparison.Ordinal) >= 0) return 4;
		if (value.IndexOf("灵宝", StringComparison.Ordinal) >= 0) return 3;
		if (value.IndexOf("法器", StringComparison.Ordinal) >= 0) return 2;
		return 1;
	}

	/// <summary>
	/// 家族覆灭后，器库中的真实法器、灵宝与法宝回到既有遗失池，继续由修士低概率拾得。
	/// 只遍历实际拥有器物的家族键，不扫描全部角色或全部家族。
	/// </summary>
	internal static int ReconcileExtinctFamilyTreasures(int currentYear)
	{
		if (entriesByFamilyId.Count == 0) return 0;
		extinctFamilyScratch.Clear();
		explicitlyExtinctFamilyScratch.Clear();
		IReadOnlyList<XjCenturyFamilyStageStateRecord> familyStates = XjCenturyAnnalsStore.ReadFamilyStageStateView();
		for (int i = 0; i < familyStates.Count; i++)
		{
			XjCenturyFamilyStageStateRecord state = familyStates[i];
			if (state != null && state.FamilyStableId > 0L && state.WasExtinct)
			{
				explicitlyExtinctFamilyScratch.Add(state.FamilyStableId);
			}
		}
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> pair in entriesByFamilyId)
		{
			if (pair.Key <= 0L || pair.Value == null || pair.Value.Count == 0) continue;
			if (XjFamilyMemberLedger.TryGetAggregate(pair.Key, out XjFamilyLedgerAggregate aggregate))
			{
				if (aggregate.AliveCount <= 0) extinctFamilyScratch.Add(pair.Key);
			}
			else if (explicitlyExtinctFamilyScratch.Contains(pair.Key))
			{
				// 旧档若家族账本尚未恢复，不把“暂时缺少聚合”误判为覆灭；仅接受已归档的覆灭证据。
				extinctFamilyScratch.Add(pair.Key);
			}
		}

		int movedTotal = 0;
		bool storageChanged = false;
		for (int familyIndex = 0; familyIndex < extinctFamilyScratch.Count; familyIndex++)
		{
			long familyStableId = extinctFamilyScratch[familyIndex];
			if (!entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
				|| entries == null || entries.Count == 0)
			{
				storageChanged |= entriesByFamilyId.Remove(familyStableId);
				continue;
			}

			int movedForFamily = 0;
			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				// 即使旧档条目损坏，也必须先移除原所有权键，避免失效家族留下幽灵去重键。
				entryKeys.Remove(familyStableId + "|" + (entry.FaBaoId ?? string.Empty).Trim() + "|" + (entry.DaoTu ?? string.Empty).Trim());
				mutatedOwnedEntryKeysSinceImport.Add(BuildOwnedKey(familyStableId, 0L, entry.FaBaoId, entry.DaoTu));
				if (!entry.Found || entry.ActorId <= 0L || string.IsNullOrWhiteSpace(entry.FaBaoId)
					|| string.IsNullOrWhiteSpace(entry.FaBaoName) || string.IsNullOrWhiteSpace(entry.DaoTu)) continue;

				string lostKey = BuildLostKey(entry.ActorId, entry.FaBaoId, entry.DaoTu);
				if (!lostEntryKeys.Add(lostKey)) continue;
				lostEntries.Add(new XjFamilyFaBaoWarehouseEntry(
					true,
					0L,
					entry.ActorId,
					entry.ActorName,
					entry.FaBaoId,
					entry.FaBaoName,
					entry.DaoTu,
					entry.ClassName,
					SourceTypeFamilyExtinction,
					Math.Max(0, currentYear)));
				mutatedLostEntryKeysSinceImport.Add(lostKey);
				movedForFamily++;
			}

			storageChanged |= entriesByFamilyId.Remove(familyStableId);
			if (movedForFamily <= 0) continue;
			movedTotal += movedForFamily;
			string familyName = XjFamilyDisplayNameResolver.Resolve(familyStableId);
			if (string.IsNullOrWhiteSpace(familyName)) familyName = "一支旧族";
			string summary = familyName + "覆灭，器库中" + movedForFamily + "件重宝散佚于世，后世修士仍可能寻得。";
			XjCenturyAnnalsStore.ObserveFamilyEvent(
				"FamilyTreasuresLostOnExtinction",
				Math.Max(1, currentYear),
				familyStableId,
				3,
				summary);
			XjWorldHistoryStore.RecordDomainEvent(
				"家族",
				"重宝散佚",
				summary,
				3,
				familyId: familyStableId,
				year: Math.Max(0, currentYear));
		}

		extinctFamilyScratch.Clear();
		explicitlyExtinctFamilyScratch.Clear();
		if (!storageChanged) return 0;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SectResource
			| XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
		return movedTotal;
	}

	/// <summary>
	/// 宗门自有器物在灭宗后同样进入既有遗失池，防止新补充的宗门器库形成悬空资产。
	/// 只遍历真正持有宗门器物的键，不建立额外宗门扫描。
	/// </summary>
	internal static int ReconcileExtinctSectTreasures(int currentYear)
	{
		if (entriesBySectId.Count == 0) return 0;
		extinctSectScratch.Clear();
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> pair in entriesBySectId)
		{
			if (pair.Key <= 0L || pair.Value == null || pair.Value.Count == 0) continue;
			if (!XjSectRepository.TryGetBySectId(pair.Key, out XjSectArchiveRecord sect)
				|| sect == null || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal))
			{
				extinctSectScratch.Add(pair.Key);
			}
		}

		int movedTotal = 0;
		bool storageChanged = false;
		for (int sectIndex = 0; sectIndex < extinctSectScratch.Count; sectIndex++)
		{
			long sectId = extinctSectScratch[sectIndex];
			if (!entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries)
				|| entries == null || entries.Count == 0)
			{
				storageChanged |= entriesBySectId.Remove(sectId);
				continue;
			}

			int movedForSect = 0;
			string sectName = string.Empty;
			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (string.IsNullOrWhiteSpace(sectName) && !string.IsNullOrWhiteSpace(entry.SectName)) sectName = entry.SectName.Trim();
				// 灭宗清库时同步清理所有原宗门键，损坏条目也不能继续占用去重集合。
				sectEntryKeys.Remove(sectId + "|" + (entry.FaBaoId ?? string.Empty).Trim() + "|" + (entry.DaoTu ?? string.Empty).Trim());
				mutatedOwnedEntryKeysSinceImport.Add(BuildOwnedKey(0L, sectId, entry.FaBaoId, entry.DaoTu));
				if (!entry.Found || entry.ActorId <= 0L || string.IsNullOrWhiteSpace(entry.FaBaoId)
					|| string.IsNullOrWhiteSpace(entry.FaBaoName) || string.IsNullOrWhiteSpace(entry.DaoTu)) continue;
				string lostKey = BuildLostKey(entry.ActorId, entry.FaBaoId, entry.DaoTu);
				if (!lostEntryKeys.Add(lostKey)) continue;
				lostEntries.Add(new XjFamilyFaBaoWarehouseEntry(
					true,
					0L,
					entry.ActorId,
					entry.ActorName,
					entry.FaBaoId,
					entry.FaBaoName,
					entry.DaoTu,
					entry.ClassName,
					SourceTypeSectExtinction,
					Math.Max(0, currentYear)));
				mutatedLostEntryKeysSinceImport.Add(lostKey);
				movedForSect++;
			}
			storageChanged |= entriesBySectId.Remove(sectId);
			if (movedForSect <= 0) continue;
			movedTotal += movedForSect;
			if (string.IsNullOrWhiteSpace(sectName)) sectName = "一座旧宗";
			string summary = sectName + "覆灭，宗门器库中" + movedForSect + "件重宝散佚于世。";
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectTreasuresLostOnExtinction",
				Math.Max(1, currentYear),
				sectId,
				sectName,
				3,
				summary);
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				"宗门重宝散佚",
				summary,
				3,
				sectId: sectId,
				year: Math.Max(0, currentYear));
		}

		extinctSectScratch.Clear();
		if (!storageChanged) return 0;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SectResource | XjCodexDirtyFlags.History
			| XjCodexDirtyFlags.CenturyAnnals);
		return movedTotal;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadAllEntries()
	{
		if (entriesByFamilyId.Count == 0 && entriesBySectId.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoWarehouseEntry>();
		}

		List<XjFamilyFaBaoWarehouseEntry> result = new List<XjFamilyFaBaoWarehouseEntry>();
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> familyEntries in entriesByFamilyId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = familyEntries.Value;
			if (entries == null || entries.Count == 0)
			{
				continue;
			}

			result.AddRange(entries);
		}
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> sectEntries in entriesBySectId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = sectEntries.Value;
			if (entries == null || entries.Count == 0)
			{
				continue;
			}

			result.AddRange(entries);
		}

		result.Sort((left, right) =>
		{
			int byYear = left.Year.CompareTo(right.Year);
			if (byYear != 0)
			{
				return byYear;
			}

			int name = string.Compare(left.FaBaoName, right.FaBaoName, System.StringComparison.Ordinal);
			if (name != 0) return name;
			int id = string.Compare(left.FaBaoId, right.FaBaoId, System.StringComparison.Ordinal);
			if (id != 0) return id;
			int family = left.FamilyStableId.CompareTo(right.FamilyStableId);
			return family != 0 ? family : left.SectId.CompareTo(right.SectId);
		});
		return result;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadLostEntries()
	{
		return lostEntries.Count == 0
			? System.Array.Empty<XjFamilyFaBaoWarehouseEntry>()
			: new List<XjFamilyFaBaoWarehouseEntry>(lostEntries);
	}

	internal static bool TryDiscoverLostFaBao(Actor finder, int year)
	{
		if (finder?.data == null || lostEntries.Count == 0)
		{
			return false;
		}

		long finderId = ((BaseSystemData)finder.data).id;
		if (finderId <= 0L
			|| !XjFamilyMemberIndex.Shared.TryGetRecord(finderId, out XjFamilyIdentity identity)
			|| !identity.Found
			|| identity.FamilyStableIdValue <= 0L
			|| XjFamilyMemberIndex.Shared.IsActorPending(finderId))
		{
			return false;
		}

		int index = PickLostEntryIndex(finderId, year);
		if (index < 0 || index >= lostEntries.Count)
		{
			return false;
		}

		XjFamilyFaBaoWarehouseEntry entry = lostEntries[index];
		string lostKey = BuildLostKey(entry.ActorId, entry.FaBaoId, entry.DaoTu);
		bool lostKeyWasAlreadyMutated = mutatedLostEntryKeysSinceImport.Contains(lostKey);
		lostEntries.RemoveAt(index);
		lostEntryKeys.Remove(lostKey);
		mutatedLostEntryKeysSinceImport.Add(lostKey);

		bool added = AddFaBaoToFamily(
			finderId,
			finder.getName(),
			identity.FamilyStableIdValue,
			entry.FaBaoId,
			entry.FaBaoName,
			entry.DaoTu,
			entry.ClassName,
			SourceTypeLostDiscovery,
			year);
		if (!added)
		{
			// 发现者写入失败时必须把同一件真实器物放回原位置；否则一次重复键、
			// 存档中途状态或家族写入异常都会让遗失池中的重宝永久消失。
			lostEntries.Insert(Math.Min(index, lostEntries.Count), entry);
			lostEntryKeys.Add(lostKey);
			if (!lostKeyWasAlreadyMutated) mutatedLostEntryKeysSinceImport.Remove(lostKey);
			return false;
		}

		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.FaBaoObtained(
			finder,
			entry.FaBaoId,
			entry.FaBaoName,
			entry.DaoTu,
			SourceTypeLostDiscovery,
			entry.ClassName));
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool WasLostEntryMutatedSinceImport(long actorId, string faBaoId, string daoTu)
	{
		return mutatedLostEntryKeysSinceImport.Contains(BuildLostKey(actorId, faBaoId, daoTu));
	}

	internal static bool WasOwnedEntryMutatedSinceImport(
		long familyStableId,
		long sectId,
		string faBaoId,
		string daoTu)
	{
		return mutatedOwnedEntryKeysSinceImport.Contains(BuildOwnedKey(familyStableId, sectId, faBaoId, daoTu));
	}

	internal static void Clear()
	{
		entriesByFamilyId.Clear();
		entryKeys.Clear();
		entriesBySectId.Clear();
		sectEntryKeys.Clear();
		lostEntries.Clear();
		lostEntryKeys.Clear();
		mutatedLostEntryKeysSinceImport.Clear();
		mutatedOwnedEntryKeysSinceImport.Clear();
	}

	internal static void ExportArchiveRecords(List<XjWorldArchiveFaBaoRecord> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> familyEntry in entriesByFamilyId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = familyEntry.Value;
			if (entries == null)
			{
				continue;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (!entry.Found
					|| entry.FamilyStableId <= 0L
					|| entry.ActorId <= 0L
					|| string.IsNullOrWhiteSpace(entry.FaBaoId)
					|| string.IsNullOrWhiteSpace(entry.FaBaoName))
				{
					continue;
				}

				records.Add(new XjWorldArchiveFaBaoRecord
				{
					FamilyStableId = entry.FamilyStableId,
					ActorId = entry.ActorId,
					ActorName = entry.ActorName,
					FaBaoId = entry.FaBaoId,
					FaBaoName = entry.FaBaoName,
					DaoTu = entry.DaoTu,
					ClassName = entry.ClassName,
					Source = entry.Source,
					Year = entry.Year
				});
			}
		}

		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> sectEntry in entriesBySectId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = sectEntry.Value;
			if (entries == null)
			{
				continue;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (!entry.Found
					|| entry.SectId <= 0L
					|| entry.ActorId <= 0L
					|| string.IsNullOrWhiteSpace(entry.FaBaoId)
					|| string.IsNullOrWhiteSpace(entry.FaBaoName))
				{
					continue;
				}

				records.Add(new XjWorldArchiveFaBaoRecord
				{
					FamilyStableId = 0L,
					SectId = entry.SectId,
					SectName = entry.SectName,
					ActorId = entry.ActorId,
					ActorName = entry.ActorName,
					FaBaoId = entry.FaBaoId,
					FaBaoName = entry.FaBaoName,
					DaoTu = entry.DaoTu,
					ClassName = entry.ClassName,
					Source = entry.Source,
					Year = entry.Year
				});
			}
		}
	}

	internal static void ExportLostArchiveRecords(List<XjWorldArchiveFaBaoRecord> records)
	{
		if (records == null)
		{
			return;
		}

		for (int i = 0; i < lostEntries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry entry = lostEntries[i];
			if (!entry.Found
				|| entry.ActorId <= 0L
				|| string.IsNullOrWhiteSpace(entry.FaBaoId)
				|| string.IsNullOrWhiteSpace(entry.FaBaoName))
			{
				continue;
			}

			records.Add(new XjWorldArchiveFaBaoRecord
			{
				FamilyStableId = 0L,
				ActorId = entry.ActorId,
				ActorName = entry.ActorName,
				FaBaoId = entry.FaBaoId,
				FaBaoName = entry.FaBaoName,
				DaoTu = entry.DaoTu,
				ClassName = entry.ClassName,
				Source = entry.Source,
				Year = entry.Year
			});
		}
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveFaBaoRecord> records)
	{
		entriesByFamilyId.Clear();
		entryKeys.Clear();
		entriesBySectId.Clear();
		sectEntryKeys.Clear();
		mutatedOwnedEntryKeysSinceImport.Clear();
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveFaBaoRecord record = records[i];
			if (record == null
				|| (record.FamilyStableId <= 0L && record.SectId <= 0L)
				|| record.ActorId <= 0L
				|| string.IsNullOrWhiteSpace(record.FaBaoId)
				|| string.IsNullOrWhiteSpace(record.FaBaoName)
				|| string.IsNullOrWhiteSpace(record.DaoTu))
			{
				continue;
			}

			if (record.FamilyStableId > 0L)
			{
				string familyKey = record.FamilyStableId + "|" + record.FaBaoId.Trim() + "|" + record.DaoTu.Trim();
				if (!entryKeys.Add(familyKey))
				{
					continue;
				}

				if (!entriesByFamilyId.TryGetValue(record.FamilyStableId, out List<XjFamilyFaBaoWarehouseEntry> familyEntries))
				{
					familyEntries = new List<XjFamilyFaBaoWarehouseEntry>();
					entriesByFamilyId[record.FamilyStableId] = familyEntries;
				}

				familyEntries.Add(new XjFamilyFaBaoWarehouseEntry(
					true,
					record.FamilyStableId,
					record.ActorId,
					record.ActorName,
					record.FaBaoId.Trim(),
					record.FaBaoName.Trim(),
					record.DaoTu.Trim(),
					record.ClassName,
					record.Source,
					record.Year));
				continue;
			}

			string sectKey = record.SectId + "|" + record.FaBaoId.Trim() + "|" + record.DaoTu.Trim();
			if (!sectEntryKeys.Add(sectKey))
			{
				continue;
			}

			if (!entriesBySectId.TryGetValue(record.SectId, out List<XjFamilyFaBaoWarehouseEntry> sectEntries))
			{
				sectEntries = new List<XjFamilyFaBaoWarehouseEntry>();
				entriesBySectId[record.SectId] = sectEntries;
			}

			sectEntries.Add(new XjFamilyFaBaoWarehouseEntry(
				true,
				0L,
				record.SectId,
				record.SectName,
				record.ActorId,
				record.ActorName,
				record.FaBaoId.Trim(),
				record.FaBaoName.Trim(),
				record.DaoTu.Trim(),
				record.ClassName,
				record.Source,
				record.Year));
		}
	}

	internal static void ImportLostArchiveRecords(IReadOnlyList<XjWorldArchiveFaBaoRecord> records)
	{
		lostEntries.Clear();
		lostEntryKeys.Clear();
		mutatedLostEntryKeysSinceImport.Clear();
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveFaBaoRecord record = records[i];
			if (record == null
				|| record.ActorId <= 0L
				|| string.IsNullOrWhiteSpace(record.FaBaoId)
				|| string.IsNullOrWhiteSpace(record.FaBaoName)
				|| string.IsNullOrWhiteSpace(record.DaoTu))
			{
				continue;
			}

			string key = BuildLostKey(record.ActorId, record.FaBaoId, record.DaoTu);
			if (!lostEntryKeys.Add(key))
			{
				continue;
			}

			lostEntries.Add(new XjFamilyFaBaoWarehouseEntry(
				true,
				0L,
				record.ActorId,
				record.ActorName,
				record.FaBaoId.Trim(),
				record.FaBaoName.Trim(),
				record.DaoTu.Trim(),
				record.ClassName,
				record.Source,
			record.Year));
		}
	}

	private static int PickLostEntryIndex(long actorId, int year)
	{
		if (lostEntries.Count == 0 || year <= 0)
		{
			return -1;
		}

		int eligibleCount = 0;
		for (int i = 0; i < lostEntries.Count; i++)
		{
			if (lostEntries[i].Year <= year) eligibleCount++;
		}
		if (eligibleCount <= 0) return -1;

		long mixed = actorId * 1103515245L + year * 12345L;
		int seed = (int)System.Math.Abs(mixed % 2147483647L);
		int eligibleIndex = seed % eligibleCount;
		for (int i = 0; i < lostEntries.Count; i++)
		{
			if (lostEntries[i].Year > year) continue;
			if (eligibleIndex-- == 0) return i;
		}
		return -1;
	}

	private static string BuildOwnedKey(long familyStableId, long sectId, string faBaoId, string daoTu)
	{
		string owner = familyStableId > 0L
			? "family:" + familyStableId
			: "sect:" + Math.Max(0L, sectId);
		return owner + "|" + (faBaoId ?? string.Empty).Trim() + "|" + (daoTu ?? string.Empty).Trim();
	}

	private static string BuildLostKey(long actorId, string faBaoId, string daoTu)
	{
		return actorId + "|" + (faBaoId ?? string.Empty).Trim() + "|" + (daoTu ?? string.Empty).Trim();
	}
}
