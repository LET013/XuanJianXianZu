using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;

namespace XuanJianVNext.Systems.Warehouse;

internal static class XjFamilyCaiQiWarehouse
{
	internal const string ResourceTypeCaiQi = "CaiQi";
	internal const string ResourceTypeGongFa = "GongFa";
	internal const string ResourceTypeQiuJinFa = "QiuJinFa";
	internal const string ResourceTypeCaiQiFa = "CaiQiFa";
	private const int MaxCaiQiResourceStack = 49;

	private static readonly Dictionary<string, Dictionary<string, int>> countsByFamilyKey = new Dictionary<string, Dictionary<string, int>>();
	private static readonly Dictionary<string, Dictionary<string, int>> gongFaCountsByFamilyKey = new Dictionary<string, Dictionary<string, int>>();
	private static readonly Dictionary<string, Dictionary<string, int>> qiuJinFaCountsByFamilyKey = new Dictionary<string, Dictionary<string, int>>();
	private static readonly Dictionary<string, Dictionary<string, int>> caiQiFaCountsByFamilyKey = new Dictionary<string, Dictionary<string, int>>();
	private static readonly HashSet<string> mutatedResourceKeysSinceImport = new HashSet<string>(System.StringComparer.Ordinal);

	internal static bool TryAdd(string familyKey, string resourceId, int count)
	{
		if (string.IsNullOrEmpty(familyKey) || string.IsNullOrEmpty(resourceId) || count <= 0)
		{
			return false;
		}

		if (!countsByFamilyKey.TryGetValue(familyKey, out Dictionary<string, int> resources))
		{
			resources = new Dictionary<string, int>();
			countsByFamilyKey[familyKey] = resources;
		}

		bool added = TryAddToStorage(resources, resourceId, count, MaxCaiQiResourceStack);
		if (added) MarkMutated(familyKey, ResourceTypeCaiQi, resourceId);
		return added;
	}

	internal static bool TryAddResource(string familyKey, string resourceId, int count)
	{
		return TryAdd(familyKey, resourceId, count);
	}

	internal static bool TryAddResource(string familyKey, string resourceType, string resourceId, int count)
	{
		if (string.IsNullOrEmpty(familyKey) || string.IsNullOrEmpty(resourceId) || count <= 0)
		{
			return false;
		}

		if (!TryGetStorage(resourceType, out Dictionary<string, Dictionary<string, int>> storage))
		{
			return false;
		}

		if (!storage.TryGetValue(familyKey, out Dictionary<string, int> resources))
		{
			resources = new Dictionary<string, int>();
			storage[familyKey] = resources;
		}

		int maxStack = string.IsNullOrEmpty(resourceType) || resourceType == ResourceTypeCaiQi
			? MaxCaiQiResourceStack
			: int.MaxValue;
		bool added = TryAddToStorage(resources, resourceId, count, maxStack);
		if (added) MarkMutated(familyKey, resourceType, resourceId);
		return added;
	}

	internal static bool TryAddCaiQiFa(string familyKey, string caiQiFaName, string daoTu, string sourcePlace, int year)
	{
		if (string.IsNullOrWhiteSpace(familyKey) || string.IsNullOrWhiteSpace(caiQiFaName) || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		string resourceId = BuildCaiQiFaResourceId(caiQiFaName, daoTu);
		if (!caiQiFaCountsByFamilyKey.TryGetValue(familyKey, out Dictionary<string, int> resources))
		{
			resources = new Dictionary<string, int>();
			caiQiFaCountsByFamilyKey[familyKey] = resources;
		}

		foreach (string existingResourceId in resources.Keys)
		{
			ParseCaiQiFaResourceId(existingResourceId, out _, out string existingDaoTu);
			if (string.Equals(existingDaoTu, daoTu.Trim(), System.StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		if (resources.ContainsKey(resourceId))
		{
			return false;
		}

		resources[resourceId] = 1;
		MarkMutated(familyKey, ResourceTypeCaiQiFa, resourceId);
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool TryGetCount(string familyKey, string resourceId, out int count)
	{
		return TryGetCount(familyKey, ResourceTypeCaiQi, resourceId, out count);
	}

	internal static bool TryGetResourceCount(string familyKey, string resourceId, out int count)
	{
		return TryGetCount(familyKey, resourceId, out count);
	}

	internal static bool TryGetCount(string familyKey, string resourceType, string resourceId, out int count)
	{
		count = 0;
		if (string.IsNullOrEmpty(familyKey) || string.IsNullOrEmpty(resourceId))
		{
			return false;
		}

		if (!TryGetStorage(resourceType, out Dictionary<string, Dictionary<string, int>> storage)
			|| !storage.TryGetValue(familyKey, out Dictionary<string, int> resources))
		{
			return false;
		}

		return resources.TryGetValue(resourceId, out count);
	}

	internal static IReadOnlyDictionary<string, int> ReadFamilyResources(string familyKey)
	{
		return ReadFamilyResources(familyKey, ResourceTypeCaiQi);
	}

	internal static IEnumerable<KeyValuePair<string, int>> EnumerateResources(string familyKey)
	{
		IReadOnlyDictionary<string, int> resources = ReadFamilyResources(familyKey);
		foreach (KeyValuePair<string, int> resource in resources)
		{
			yield return resource;
		}
	}

	internal static IReadOnlyDictionary<string, int> ReadFamilyResources(string familyKey, string resourceType)
	{
		if (string.IsNullOrEmpty(familyKey)
			|| !TryGetStorage(resourceType, out Dictionary<string, Dictionary<string, int>> storage)
			|| !storage.TryGetValue(familyKey, out Dictionary<string, int> resources))
		{
			return new Dictionary<string, int>();
		}

		return new Dictionary<string, int>(resources);
	}

	internal static bool TryConsume(string familyKey, string resourceId, int count)
	{
		if (string.IsNullOrEmpty(familyKey) || string.IsNullOrEmpty(resourceId) || count <= 0)
		{
			return false;
		}

		if (!countsByFamilyKey.TryGetValue(familyKey, out Dictionary<string, int> resources))
		{
			return false;
		}

		if (!resources.TryGetValue(resourceId, out int currentCount) || currentCount < count)
		{
			return false;
		}

		int remainingCount = currentCount - count;
		if (remainingCount > 0)
		{
			resources[resourceId] = remainingCount;
		}
		else
		{
			resources.Remove(resourceId);
		}

		if (resources.Count == 0)
		{
			countsByFamilyKey.Remove(familyKey);
		}

		MarkMutated(familyKey, ResourceTypeCaiQi, resourceId);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool WasMutatedSinceImport(string familyKey, string resourceType, string resourceId)
	{
		return mutatedResourceKeysSinceImport.Contains(BuildMutationKey(familyKey, resourceType, resourceId));
	}

	internal static void Clear()
	{
		countsByFamilyKey.Clear();
		gongFaCountsByFamilyKey.Clear();
		qiuJinFaCountsByFamilyKey.Clear();
		caiQiFaCountsByFamilyKey.Clear();
		mutatedResourceKeysSinceImport.Clear();
	}

	internal static void ExportArchiveRecords(List<XjWorldArchiveCaiQiRecord> records)
	{
		if (records == null)
		{
			return;
		}

		ExportArchiveRecords(records, ResourceTypeCaiQi, countsByFamilyKey);
		ExportArchiveRecords(records, ResourceTypeCaiQiFa, caiQiFaCountsByFamilyKey);
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveCaiQiRecord> records)
	{
		countsByFamilyKey.Clear();
		gongFaCountsByFamilyKey.Clear();
		qiuJinFaCountsByFamilyKey.Clear();
		caiQiFaCountsByFamilyKey.Clear();
		mutatedResourceKeysSinceImport.Clear();
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveCaiQiRecord record = records[i];
			if (record == null
				|| string.IsNullOrWhiteSpace(record.FamilyKey)
				|| string.IsNullOrWhiteSpace(record.ResourceName)
				|| record.Count <= 0
				|| !TryGetStorage(record.ResourceType, out Dictionary<string, Dictionary<string, int>> storage))
			{
				continue;
			}

			string familyKey = record.FamilyKey.Trim();
			string resourceName = record.ResourceName.Trim();
			if (!storage.TryGetValue(familyKey, out Dictionary<string, int> resources))
			{
				resources = new Dictionary<string, int>();
				storage[familyKey] = resources;
			}

			int importedCount = string.IsNullOrEmpty(record.ResourceType) || record.ResourceType == ResourceTypeCaiQi
				? (record.Count > MaxCaiQiResourceStack ? MaxCaiQiResourceStack : record.Count)
				: record.Count;
			resources[resourceName] = importedCount;
		}
	}

	private static void ExportArchiveRecords(
		List<XjWorldArchiveCaiQiRecord> records,
		string resourceType,
		Dictionary<string, Dictionary<string, int>> storage)
	{
		foreach (KeyValuePair<string, Dictionary<string, int>> familyEntry in storage)
		{
			if (string.IsNullOrWhiteSpace(familyEntry.Key) || familyEntry.Value == null)
			{
				continue;
			}

			foreach (KeyValuePair<string, int> resourceEntry in familyEntry.Value)
			{
				if (string.IsNullOrWhiteSpace(resourceEntry.Key) || resourceEntry.Value <= 0)
				{
					continue;
				}

				records.Add(new XjWorldArchiveCaiQiRecord
				{
					FamilyKey = familyEntry.Key,
					ResourceType = resourceType,
					ResourceName = resourceEntry.Key,
					Count = resourceEntry.Value
				});
			}
		}
	}

	private static bool TryGetStorage(string resourceType, out Dictionary<string, Dictionary<string, int>> storage)
	{
		if (string.IsNullOrEmpty(resourceType) || resourceType == ResourceTypeCaiQi)
		{
			storage = countsByFamilyKey;
			return true;
		}

		if (resourceType == ResourceTypeGongFa)
		{
			storage = gongFaCountsByFamilyKey;
			return true;
		}

		if (resourceType == ResourceTypeQiuJinFa)
		{
			storage = qiuJinFaCountsByFamilyKey;
			return true;
		}

		if (resourceType == ResourceTypeCaiQiFa)
		{
			storage = caiQiFaCountsByFamilyKey;
			return true;
		}

		storage = null;
		return false;
	}

	private static bool TryAddToStorage(Dictionary<string, int> resources, string resourceId, int count, int maxStack)
	{
		if (resources == null || string.IsNullOrEmpty(resourceId) || count <= 0)
		{
			return false;
		}

		resources.TryGetValue(resourceId, out int currentCount);
		int nextCount = currentCount + count;
		if (maxStack > 0 && nextCount > maxStack)
		{
			nextCount = maxStack;
		}

		if (nextCount <= currentCount)
		{
			return false;
		}

		resources[resourceId] = nextCount;
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	private static void MarkMutated(string familyKey, string resourceType, string resourceId)
	{
		string key = BuildMutationKey(familyKey, resourceType, resourceId);
		if (!string.IsNullOrEmpty(key)) mutatedResourceKeysSinceImport.Add(key);
	}

	private static string BuildMutationKey(string familyKey, string resourceType, string resourceId)
	{
		if (string.IsNullOrWhiteSpace(familyKey) || string.IsNullOrWhiteSpace(resourceId)) return string.Empty;
		string normalizedType = string.IsNullOrWhiteSpace(resourceType) ? ResourceTypeCaiQi : resourceType.Trim();
		return familyKey.Trim() + "|" + normalizedType + "|" + resourceId.Trim();
	}

	internal static string BuildCaiQiFaResourceId(string caiQiFaName, string daoTu)
	{
		string name = string.IsNullOrWhiteSpace(caiQiFaName) ? string.Empty : caiQiFaName.Trim();
		string normalizedDaoTu = string.IsNullOrWhiteSpace(daoTu) ? string.Empty : daoTu.Trim();
		return normalizedDaoTu + "|" + name;
	}

	internal static void ParseCaiQiFaResourceId(string resourceId, out string caiQiFaName, out string daoTu)
	{
		caiQiFaName = string.Empty;
		daoTu = string.Empty;
		if (string.IsNullOrWhiteSpace(resourceId))
		{
			return;
		}

		string text = resourceId.Trim();
		int separatorIndex = text.IndexOf('|');
		if (separatorIndex < 0)
		{
			caiQiFaName = text;
			return;
		}

		daoTu = text.Substring(0, separatorIndex).Trim();
		caiQiFaName = text.Substring(separatorIndex + 1).Trim();
	}
}


internal readonly struct XjFamilyCaiQiWarehouseEntry
{
	internal static XjFamilyCaiQiWarehouseEntry Empty { get; } = new XjFamilyCaiQiWarehouseEntry(false, string.Empty, string.Empty, 0);

	internal readonly bool Found;
	internal readonly string FamilyKey;
	internal readonly string ResourceId;
	internal readonly int Count;

	internal XjFamilyCaiQiWarehouseEntry(
		bool found,
		string familyKey,
		string resourceId,
		int count)
	{
		Found = found;
		FamilyKey = familyKey ?? string.Empty;
		ResourceId = resourceId ?? string.Empty;
		Count = count;
	}
}


internal readonly struct XjFamilyCaiQiWarehouseView
{
	private readonly string familyKey;

	internal XjFamilyCaiQiWarehouseView(string familyKey)
	{
		this.familyKey = familyKey ?? string.Empty;
	}

	internal bool IsValid => !string.IsNullOrEmpty(familyKey);

	internal bool TryGetResourceCount(string resourceId, out int count)
	{
		if (!IsValid)
		{
			count = 0;
			return false;
		}

		return XjFamilyCaiQiWarehouse.TryGetResourceCount(familyKey, resourceId, out count);
	}

	internal IEnumerable<KeyValuePair<string, int>> EnumerateResources()
	{
		if (!IsValid)
		{
			yield break;
		}

		foreach (KeyValuePair<string, int> resource in XjFamilyCaiQiWarehouse.EnumerateResources(familyKey))
		{
			yield return resource;
		}
	}
}

internal static class XjFamilyCaiQiWarehouseResolver
{
	internal static XjFamilyCaiQiWarehouseView TryResolve(string familyKey)
	{
		return new XjFamilyCaiQiWarehouseView(familyKey);
	}
}


internal readonly struct XjFamilyCaiQiWarehouseUIItem
{
	internal readonly string ResourceId;
	internal readonly string DisplayName;
	internal readonly int Count;

	internal XjFamilyCaiQiWarehouseUIItem(string resourceId, string displayName, int count)
	{
		ResourceId = resourceId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		Count = count;
	}
}

internal static class XjFamilyCaiQiWarehouseUI
{
	private const int MaxDisplayResourceCount = 12;

	internal static string FormatAndRender(string familyKey)
	{
		XjFamilyCaiQiWarehouseUIItem[] items = BuildItems(familyKey);
		if (items.Length == 0)
		{
			return XjFamilyInheritanceTabFormatter.EmptyDisplayText;
		}

		List<string> segments = new List<string>(items.Length + 1);
		for (int i = 0; i < items.Length && i < MaxDisplayResourceCount; i++)
		{
			XjFamilyCaiQiWarehouseUIItem item = items[i];
			if (item.Count > 0)
			{
				segments.Add(item.DisplayName + "×" + item.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}
		}

		if (items.Length > MaxDisplayResourceCount)
		{
			segments.Add("……");
		}

		return segments.Count == 0
			? XjFamilyInheritanceTabFormatter.EmptyDisplayText
			: string.Join("、", segments);
	}

	internal static XjFamilyCaiQiWarehouseUIItem[] BuildItems(string familyKey)
	{
		IReadOnlyDictionary<string, int> resources = XjFamilyCaiQiWarehouse.ReadFamilyResources(familyKey);
		if (resources.Count == 0)
		{
			return System.Array.Empty<XjFamilyCaiQiWarehouseUIItem>();
		}

		List<string> resourceIds = new List<string>(resources.Keys);
		resourceIds.Sort(System.StringComparer.Ordinal);

		List<XjFamilyCaiQiWarehouseUIItem> items = new List<XjFamilyCaiQiWarehouseUIItem>(resourceIds.Count);
		for (int i = 0; i < resourceIds.Count; i++)
		{
			string resourceId = resourceIds[i];
			if (!resources.TryGetValue(resourceId, out int count) || count <= 0)
			{
				continue;
			}

			if (!XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string displayName))
			{
				displayName = string.Equals(resourceId, "zaqi", System.StringComparison.Ordinal) ? "杂气" : "未名先天之气";
			}

			items.Add(new XjFamilyCaiQiWarehouseUIItem(resourceId, displayName, count));
		}

		return items.ToArray();
	}
}
