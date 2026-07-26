using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Warehouse;

// Archive field and runtime type names retain "LingWu" for save compatibility.
// The collection is now the family treasure warehouse and accepts both LingWu and residual JinXing.
internal readonly struct XjFamilyLingWuWarehouseEntry
{
	internal readonly long FamilyStableId;
	internal readonly string LingWuId;
	internal readonly int Count;
	internal readonly int FirstAcquiredYear;
	internal readonly int LastAcquiredYear;
	internal readonly long LastSourceActorId;
	internal readonly string LastSourceActorName;

	internal XjFamilyLingWuWarehouseEntry(
		long familyStableId,
		string lingWuId,
		int count,
		int firstAcquiredYear,
		int lastAcquiredYear,
		long lastSourceActorId,
		string lastSourceActorName)
	{
		FamilyStableId = Math.Max(0L, familyStableId);
		LingWuId = lingWuId ?? string.Empty;
		Count = Math.Max(0, count);
		FirstAcquiredYear = Math.Max(0, firstAcquiredYear);
		LastAcquiredYear = Math.Max(0, lastAcquiredYear);
		LastSourceActorId = Math.Max(0L, lastSourceActorId);
		LastSourceActorName = lastSourceActorName ?? string.Empty;
	}
}

internal static class XjFamilyLingWuWarehouse
{
	private static readonly Dictionary<long, Dictionary<string, XjFamilyLingWuWarehouseEntry>> EntriesByFamily = new();
	// 记录本次读档后发生过显式增减的条目。保存前的防丢合并必须尊重这些
	// 权威变更，不能用旧归档的较大数量把已消耗的灵物/金性“复活”。
	private static readonly HashSet<string> MutatedKeysSinceImport = new(StringComparer.Ordinal);

	internal static bool TryAdd(
		long familyStableId,
		XjLingWuDef definition,
		long sourceActorId,
		string sourceActorName,
		int year)
	{
		return TryAddLingWu(familyStableId, definition, sourceActorId, sourceActorName, year);
	}

	internal static bool TryAddLingWu(
		long familyStableId,
		XjLingWuDef definition,
		long sourceActorId,
		string sourceActorName,
		int year)
	{
		return definition != null
			&& TryAddCore(familyStableId, definition.Id, 1, sourceActorId, sourceActorName, year);
	}

	internal static bool TryAddJinXing(
		long familyStableId,
		string jinXing,
		int amount,
		long sourceActorId,
		string sourceActorName,
		int year)
	{
		string treasureId = XjFamilyTreasureCatalog.BuildJinXingTreasureId(jinXing);
		return TryAddCore(familyStableId, treasureId, amount, sourceActorId, sourceActorName, year);
	}

	private static bool TryAddCore(
		long familyStableId,
		string treasureId,
		int amount,
		long sourceActorId,
		string sourceActorName,
		int year)
	{
		if (familyStableId <= 0L
			|| amount <= 0
			|| string.IsNullOrWhiteSpace(treasureId)
			|| !XjFamilyTreasureCatalog.TryResolve(treasureId, out _))
		{
			return false;
		}

		if (!EntriesByFamily.TryGetValue(familyStableId, out Dictionary<string, XjFamilyLingWuWarehouseEntry> familyEntries))
		{
			familyEntries = new Dictionary<string, XjFamilyLingWuWarehouseEntry>(StringComparer.Ordinal);
			EntriesByFamily[familyStableId] = familyEntries;
		}

		int safeYear = Math.Max(0, year);
		if (familyEntries.TryGetValue(treasureId, out XjFamilyLingWuWarehouseEntry existing))
		{
			familyEntries[treasureId] = new XjFamilyLingWuWarehouseEntry(
				familyStableId,
				treasureId,
				existing.Count + amount,
				existing.FirstAcquiredYear > 0 ? existing.FirstAcquiredYear : safeYear,
				safeYear > 0 ? safeYear : existing.LastAcquiredYear,
				sourceActorId,
				sourceActorName);
		}
		else
		{
			familyEntries[treasureId] = new XjFamilyLingWuWarehouseEntry(
				familyStableId,
				treasureId,
				amount,
				safeYear,
				safeYear,
				sourceActorId,
				sourceActorName);
		}

		MutatedKeysSinceImport.Add(BuildMutationKey(familyStableId, treasureId));
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		if (XjFamilyTreasureCatalog.TryResolve(treasureId, out XjFamilyTreasureDef treasure))
		{
			XjThreeBookWriter.RecordFamilyTreasureAdded(
				familyStableId,
				XjFamilyDisplayNameResolver.Resolve(familyStableId),
				sourceActorId,
				sourceActorName,
				treasureId,
				treasure.Name,
				amount,
				safeYear);
		}
		return true;
	}

	internal static bool TryGetCount(long familyStableId, string treasureId, out int count)
	{
		count = 0;
		if (familyStableId <= 0L
			|| string.IsNullOrWhiteSpace(treasureId)
			|| !EntriesByFamily.TryGetValue(familyStableId, out Dictionary<string, XjFamilyLingWuWarehouseEntry> entries)
			|| !entries.TryGetValue(treasureId.Trim(), out XjFamilyLingWuWarehouseEntry entry))
		{
			return false;
		}

		count = Math.Max(0, entry.Count);
		return count > 0;
	}

	internal static bool TryConsume(long familyStableId, string treasureId, int amount)
	{
		if (familyStableId <= 0L
			|| amount <= 0
			|| string.IsNullOrWhiteSpace(treasureId)
			|| !EntriesByFamily.TryGetValue(familyStableId, out Dictionary<string, XjFamilyLingWuWarehouseEntry> entries)
			|| !entries.TryGetValue(treasureId.Trim(), out XjFamilyLingWuWarehouseEntry entry)
			|| entry.Count < amount)
		{
			return false;
		}

		int remaining = entry.Count - amount;
		if (remaining <= 0)
		{
			entries.Remove(entry.LingWuId);
			if (entries.Count == 0) EntriesByFamily.Remove(familyStableId);
		}
		else
		{
			entries[entry.LingWuId] = new XjFamilyLingWuWarehouseEntry(
				entry.FamilyStableId, entry.LingWuId, remaining, entry.FirstAcquiredYear,
				entry.LastAcquiredYear, entry.LastSourceActorId, entry.LastSourceActorName);
		}

		MutatedKeysSinceImport.Add(BuildMutationKey(familyStableId, entry.LingWuId));
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool WasMutatedSinceImport(long familyStableId, string treasureId)
	{
		return familyStableId > 0L
			&& !string.IsNullOrWhiteSpace(treasureId)
			&& MutatedKeysSinceImport.Contains(BuildMutationKey(familyStableId, treasureId));
	}

	internal static bool HasAnyJinXing(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !EntriesByFamily.TryGetValue(familyStableId, out Dictionary<string, XjFamilyLingWuWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return false;
		}

		foreach (KeyValuePair<string, XjFamilyLingWuWarehouseEntry> pair in entries)
		{
			if (pair.Value.Count > 0 && XjFamilyTreasureCatalog.TryGetJinXingValue(pair.Key, out _))
			{
				return true;
			}
		}
		return false;
	}

	internal static bool TryConsumeFirstJinXing(long familyStableId, out string jinXing)
	{
		jinXing = string.Empty;
		if (familyStableId <= 0L
			|| !EntriesByFamily.TryGetValue(familyStableId, out Dictionary<string, XjFamilyLingWuWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return false;
		}

		string selectedId = string.Empty;
		foreach (KeyValuePair<string, XjFamilyLingWuWarehouseEntry> pair in entries)
		{
			if (pair.Value.Count <= 0 || !XjFamilyTreasureCatalog.TryGetJinXingValue(pair.Key, out _)) continue;
			if (selectedId.Length == 0 || string.CompareOrdinal(pair.Key, selectedId) < 0) selectedId = pair.Key;
		}

		if (selectedId.Length == 0
			|| !XjFamilyTreasureCatalog.TryGetJinXingValue(selectedId, out jinXing)
			|| !TryConsume(familyStableId, selectedId, 1))
		{
			jinXing = string.Empty;
			return false;
		}
		return true;
	}

	internal static bool TryTransferFirstTreasure(
		long sourceFamilyStableId,
		long targetFamilyStableId,
		long sourceActorId,
		string sourceActorName,
		int year,
		out string treasureName)
	{
		treasureName = string.Empty;
		if (sourceFamilyStableId <= 0L || targetFamilyStableId <= 0L || sourceFamilyStableId == targetFamilyStableId
			|| !EntriesByFamily.TryGetValue(sourceFamilyStableId, out Dictionary<string, XjFamilyLingWuWarehouseEntry> entries))
		{
			return false;
		}

		string selectedId = string.Empty;
		foreach (KeyValuePair<string, XjFamilyLingWuWarehouseEntry> pair in entries)
		{
			if (pair.Value.Count <= 0 || !XjFamilyTreasureCatalog.TryResolve(pair.Key, out _)) continue;
			if (selectedId.Length == 0 || string.CompareOrdinal(pair.Key, selectedId) < 0) selectedId = pair.Key;
		}
		if (selectedId.Length == 0 || !XjFamilyTreasureCatalog.TryResolve(selectedId, out XjFamilyTreasureDef definition)) return false;

		bool isJinXing = XjFamilyTreasureCatalog.TryGetJinXingValue(selectedId, out string jinXing);
		XjLingWuDef lingWu = null;
		bool isLingWu = !isJinXing && XjLingWuCatalog.TryGet(selectedId, out lingWu);
		if ((!isJinXing && !isLingWu) || !TryConsume(sourceFamilyStableId, selectedId, 1)) return false;

		bool targetAccepted = isJinXing
			? TryAddJinXing(targetFamilyStableId, jinXing, 1, sourceActorId, sourceActorName, year)
			: TryAddLingWu(targetFamilyStableId, lingWu, sourceActorId, sourceActorName, year);
		if (!targetAccepted)
		{
			if (isJinXing) TryAddJinXing(sourceFamilyStableId, jinXing, 1, sourceActorId, sourceActorName, year);
			else TryAddLingWu(sourceFamilyStableId, lingWu, sourceActorId, sourceActorName, year);
			return false;
		}

		treasureName = definition.Name;
		return true;
	}

	internal static IReadOnlyList<XjFamilyLingWuWarehouseEntry> ReadFamilyEntries(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !EntriesByFamily.TryGetValue(familyStableId, out Dictionary<string, XjFamilyLingWuWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return Array.Empty<XjFamilyLingWuWarehouseEntry>();
		}

		List<XjFamilyLingWuWarehouseEntry> result = new(entries.Values);
		result.Sort((left, right) => string.Compare(left.LingWuId, right.LingWuId, StringComparison.Ordinal));
		return result;
	}

	internal static void ExportArchiveRecords(List<XjWorldArchiveLingWuRecord> records)
	{
		if (records == null) return;
		foreach (KeyValuePair<long, Dictionary<string, XjFamilyLingWuWarehouseEntry>> familyPair in EntriesByFamily)
		{
			foreach (XjFamilyLingWuWarehouseEntry entry in familyPair.Value.Values)
			{
				if (entry.FamilyStableId <= 0L || entry.Count <= 0 || string.IsNullOrWhiteSpace(entry.LingWuId)) continue;
				records.Add(new XjWorldArchiveLingWuRecord
				{
					FamilyStableId = entry.FamilyStableId,
					LingWuId = entry.LingWuId,
					Count = entry.Count,
					FirstAcquiredYear = entry.FirstAcquiredYear,
					LastAcquiredYear = entry.LastAcquiredYear,
					LastSourceActorId = entry.LastSourceActorId,
					LastSourceActorName = entry.LastSourceActorName
				});
			}
		}
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveLingWuRecord> records)
	{
		EntriesByFamily.Clear();
		MutatedKeysSinceImport.Clear();
		if (records == null) return;
		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveLingWuRecord record = records[i];
			if (record == null
				|| record.FamilyStableId <= 0L
				|| record.Count <= 0
				|| !XjFamilyTreasureCatalog.TryResolve(record.LingWuId, out _)) continue;

			if (!EntriesByFamily.TryGetValue(record.FamilyStableId, out Dictionary<string, XjFamilyLingWuWarehouseEntry> familyEntries))
			{
				familyEntries = new Dictionary<string, XjFamilyLingWuWarehouseEntry>(StringComparer.Ordinal);
				EntriesByFamily[record.FamilyStableId] = familyEntries;
			}

			if (familyEntries.TryGetValue(record.LingWuId, out XjFamilyLingWuWarehouseEntry existing))
			{
				familyEntries[record.LingWuId] = new XjFamilyLingWuWarehouseEntry(
					record.FamilyStableId,
					record.LingWuId,
					existing.Count + record.Count,
					MinPositive(existing.FirstAcquiredYear, record.FirstAcquiredYear),
					Math.Max(existing.LastAcquiredYear, record.LastAcquiredYear),
					record.LastAcquiredYear >= existing.LastAcquiredYear ? record.LastSourceActorId : existing.LastSourceActorId,
					record.LastAcquiredYear >= existing.LastAcquiredYear ? record.LastSourceActorName : existing.LastSourceActorName);
			}
			else
			{
				familyEntries[record.LingWuId] = new XjFamilyLingWuWarehouseEntry(
					record.FamilyStableId,
					record.LingWuId,
					record.Count,
					record.FirstAcquiredYear,
					record.LastAcquiredYear,
					record.LastSourceActorId,
					record.LastSourceActorName);
			}
		}
	}

	private static string BuildMutationKey(long familyStableId, string treasureId)
	{
		return familyStableId.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "|" + (treasureId ?? string.Empty).Trim();
	}

	private static int MinPositive(int left, int right)
	{
		if (left <= 0) return Math.Max(0, right);
		if (right <= 0) return Math.Max(0, left);
		return Math.Min(left, right);
	}
}
