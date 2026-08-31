using System.Collections.Generic;
using System;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.Systems.Sect;

internal readonly struct XjSectCaiQiWarehouseEntry
{
	internal readonly bool Found;
	internal readonly long ZongMenId;
	internal readonly string ZongMenName;
	internal readonly string ResourceType;
	internal readonly string ResourceId;
	internal readonly string ResourceName;
	internal readonly int Amount;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string DaoTu;
	internal readonly string SourcePlace;
	internal readonly int Year;

	internal XjSectCaiQiWarehouseEntry(
		bool found,
		long zongMenId,
		string zongMenName,
		string resourceType,
		string resourceId,
		string resourceName,
		int amount,
		long actorId,
		string actorName,
		string daoTu,
		string sourcePlace,
		int year,
		bool markChanged = true)
	{
		Found = found;
		ZongMenId = zongMenId < 0L ? 0L : zongMenId;
		ZongMenName = zongMenName ?? string.Empty;
		ResourceType = resourceType ?? string.Empty;
		ResourceId = resourceId ?? string.Empty;
		ResourceName = resourceName ?? string.Empty;
		Amount = amount < 0 ? 0 : amount;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		SourcePlace = sourcePlace ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

internal static class XjSectCaiQiWarehouse
{
	internal const string ResourceTypeCaiQi = "CaiQi";
	internal const string ResourceTypeCaiQiFa = "CaiQiFa";
	private const int MaxCaiQiResourceStack = 20;

	private static readonly Dictionary<long, List<XjSectCaiQiWarehouseEntry>> entriesByZongMenId = new Dictionary<long, List<XjSectCaiQiWarehouseEntry>>();
	private static readonly HashSet<string> entryKeys = new HashSet<string>();

	internal static bool TryAddCaiQiResource(
		long zongMenId,
		string zongMenName,
		string resourceId,
		int amount,
		long actorId,
		string actorName,
		string sourcePlace,
		int year,
		bool markChanged = true)
	{
		if (zongMenId <= 0L || string.IsNullOrWhiteSpace(resourceId) || amount <= 0)
		{
			return false;
		}

		string normalizedResourceId = resourceId.Trim();
		string displayName = ResolveCaiQiDisplayName(normalizedResourceId);
		string key = zongMenId + "|" + ResourceTypeCaiQi + "|" + normalizedResourceId;
		if (!entriesByZongMenId.TryGetValue(zongMenId, out List<XjSectCaiQiWarehouseEntry> entries))
		{
			entries = new List<XjSectCaiQiWarehouseEntry>();
			entriesByZongMenId[zongMenId] = entries;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			XjSectCaiQiWarehouseEntry existing = entries[i];
			if (existing.ResourceType == ResourceTypeCaiQi && existing.ResourceId == normalizedResourceId)
			{
				int nextAmount = existing.Amount + amount;
				if (nextAmount > MaxCaiQiResourceStack)
				{
					nextAmount = MaxCaiQiResourceStack;
				}

				if (nextAmount <= existing.Amount)
				{
					return false;
				}

				entries[i] = new XjSectCaiQiWarehouseEntry(
					true,
					existing.ZongMenId,
					string.IsNullOrWhiteSpace(existing.ZongMenName) ? zongMenName : existing.ZongMenName,
					ResourceTypeCaiQi,
					normalizedResourceId,
					displayName,
					nextAmount,
					existing.ActorId,
					existing.ActorName,
					existing.DaoTu,
					existing.SourcePlace,
					existing.Year);
		if (markChanged)
		{
			XjWorldArchiveSystem.MarkChanged();
		}
		return true;
			}
		}

		entryKeys.Add(key);
		entries.Add(new XjSectCaiQiWarehouseEntry(
			true,
			zongMenId,
			zongMenName,
			ResourceTypeCaiQi,
			normalizedResourceId,
			displayName,
			amount > MaxCaiQiResourceStack ? MaxCaiQiResourceStack : amount,
			actorId,
			actorName,
			string.Empty,
			sourcePlace,
			year));
		if (markChanged)
		{
			XjWorldArchiveSystem.MarkChanged();
		}
		return true;
	}

	internal static bool TryAddCaiQiFa(
		long zongMenId,
		string zongMenName,
		string name,
		string daoTu,
		string sourcePlace,
		long actorId,
		string actorName,
		int year,
		bool markChanged = true)
	{
		if (zongMenId <= 0L || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		string resourceId = XjFamilyCaiQiWarehouse.BuildCaiQiFaResourceId(name, daoTu);
		string key = zongMenId + "|" + ResourceTypeCaiQiFa + "|" + resourceId;
		if (!entryKeys.Add(key))
		{
			return false;
		}

		if (!entriesByZongMenId.TryGetValue(zongMenId, out List<XjSectCaiQiWarehouseEntry> entries))
		{
			entries = new List<XjSectCaiQiWarehouseEntry>();
			entriesByZongMenId[zongMenId] = entries;
		}

		entries.Add(new XjSectCaiQiWarehouseEntry(
			true,
			zongMenId,
			zongMenName,
			ResourceTypeCaiQiFa,
			resourceId,
			name.Trim(),
			1,
			actorId,
			actorName,
			daoTu.Trim(),
			sourcePlace,
			year));
		if (markChanged)
		{
			XjWorldArchiveSystem.MarkChanged();
		}
		return true;
	}

	internal static bool TryGetCaiQiCount(long zongMenId, string resourceId, out int count)
	{
		count = 0;
		if (zongMenId <= 0L || string.IsNullOrWhiteSpace(resourceId)
			|| !entriesByZongMenId.TryGetValue(zongMenId, out List<XjSectCaiQiWarehouseEntry> entries))
		{
			return false;
		}

		string normalizedResourceId = resourceId.Trim();
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectCaiQiWarehouseEntry entry = entries[i];
			if (entry.Found
				&& entry.ResourceType == ResourceTypeCaiQi
				&& entry.ResourceId == normalizedResourceId)
			{
				count = entry.Amount;
				return count > 0;
			}
		}
		return false;
	}

	internal static bool TryConsumeCaiQi(long zongMenId, string resourceId, int count)
	{
		if (zongMenId <= 0L || string.IsNullOrWhiteSpace(resourceId) || count <= 0
			|| !entriesByZongMenId.TryGetValue(zongMenId, out List<XjSectCaiQiWarehouseEntry> entries))
		{
			return false;
		}

		string normalizedResourceId = resourceId.Trim();
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectCaiQiWarehouseEntry entry = entries[i];
			if (!entry.Found
				|| entry.ResourceType != ResourceTypeCaiQi
				|| entry.ResourceId != normalizedResourceId
				|| entry.Amount < count)
			{
				continue;
			}

			int remaining = entry.Amount - count;
			if (remaining <= 0)
			{
				entries.RemoveAt(i);
			}
			else
			{
				entries[i] = new XjSectCaiQiWarehouseEntry(
					true, entry.ZongMenId, entry.ZongMenName, entry.ResourceType,
					entry.ResourceId, entry.ResourceName, remaining, entry.ActorId,
					entry.ActorName, entry.DaoTu, entry.SourcePlace, entry.Year);
			}

			if (entries.Count == 0)
			{
				entriesByZongMenId.Remove(zongMenId);
			}
			XjWorldArchiveSystem.MarkChanged();
			return true;
		}
		return false;
	}

	internal static IReadOnlyList<XjSectCaiQiWarehouseEntry> ReadCaiQiResources(long zongMenId)
	{
		return ReadEntries(zongMenId, ResourceTypeCaiQi);
	}

	internal static IReadOnlyList<XjSectCaiQiWarehouseEntry> ReadCaiQiFaResources(long zongMenId)
	{
		return ReadEntries(zongMenId, ResourceTypeCaiQiFa);
	}

	internal static bool TransferSectWarehouse(long sourceZongMenId, long targetZongMenId, string targetZongMenName, int year, out int caiQiStacks, out int caiQiAmount, out int caiQiFaEntries)
	{
		caiQiStacks = 0;
		caiQiAmount = 0;
		caiQiFaEntries = 0;
		if (sourceZongMenId <= 0L || targetZongMenId <= 0L || sourceZongMenId == targetZongMenId
			|| !entriesByZongMenId.TryGetValue(sourceZongMenId, out List<XjSectCaiQiWarehouseEntry> sourceEntries))
		{
			return false;
		}

		List<XjSectCaiQiWarehouseEntry> snapshot = new List<XjSectCaiQiWarehouseEntry>(sourceEntries);
		entriesByZongMenId.Remove(sourceZongMenId);
		if (!entriesByZongMenId.TryGetValue(targetZongMenId, out List<XjSectCaiQiWarehouseEntry> targetEntries))
		{
			targetEntries = new List<XjSectCaiQiWarehouseEntry>();
			entriesByZongMenId[targetZongMenId] = targetEntries;
		}

		bool changed = false;
		for (int i = 0; i < snapshot.Count; i++)
		{
			XjSectCaiQiWarehouseEntry entry = snapshot[i];
			if (!entry.Found || string.IsNullOrWhiteSpace(entry.ResourceId) || entry.Amount <= 0) continue;
			int existingIndex = -1;
			for (int j = 0; j < targetEntries.Count; j++)
			{
				if (targetEntries[j].ResourceType == entry.ResourceType && targetEntries[j].ResourceId == entry.ResourceId)
				{
					existingIndex = j;
					break;
				}
			}
			if (entry.ResourceType == ResourceTypeCaiQi)
			{
				caiQiStacks++;
				caiQiAmount += entry.Amount;
			}
			else if (entry.ResourceType == ResourceTypeCaiQiFa)
			{
				caiQiFaEntries++;
			}

			if (existingIndex >= 0)
			{
				XjSectCaiQiWarehouseEntry existing = targetEntries[existingIndex];
				if (entry.ResourceType == ResourceTypeCaiQi)
				{
					long next = (long)existing.Amount + entry.Amount;
					targetEntries[existingIndex] = new XjSectCaiQiWarehouseEntry(
						true, targetZongMenId, targetZongMenName, existing.ResourceType, existing.ResourceId,
						existing.ResourceName, next >= int.MaxValue ? int.MaxValue : (int)next, existing.ActorId,
						existing.ActorName, existing.DaoTu, existing.SourcePlace, Math.Max(Math.Max(0, year), existing.Year));
				}
			}
			else
			{
				targetEntries.Add(new XjSectCaiQiWarehouseEntry(
					true, targetZongMenId, targetZongMenName, entry.ResourceType, entry.ResourceId, entry.ResourceName,
					entry.Amount, entry.ActorId, entry.ActorName, entry.DaoTu, entry.SourcePlace, Math.Max(Math.Max(0, year), entry.Year)));
			}
			changed = true;
		}
		if (!changed) return false;
		RebuildEntryKeys();
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static void Clear()
	{
		entriesByZongMenId.Clear();
		entryKeys.Clear();
	}

	internal static void ExportArchiveRecords(
		List<XjWorldArchiveZongMenCaiQiRecord> caiQiRecords,
		List<XjWorldArchiveZongMenCaiQiRecord> caiQiFaRecords)
	{
		if (caiQiRecords == null || caiQiFaRecords == null)
		{
			return;
		}

		foreach (KeyValuePair<long, List<XjSectCaiQiWarehouseEntry>> zongMenEntry in entriesByZongMenId)
		{
			List<XjSectCaiQiWarehouseEntry> entries = zongMenEntry.Value;
			if (entries == null)
			{
				continue;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				XjSectCaiQiWarehouseEntry entry = entries[i];
				if (!entry.Found || entry.ZongMenId <= 0L || string.IsNullOrWhiteSpace(entry.ResourceId) || entry.Amount <= 0)
				{
					continue;
				}

				XjWorldArchiveZongMenCaiQiRecord record = new XjWorldArchiveZongMenCaiQiRecord
				{
					ZongMenId = entry.ZongMenId,
					ZongMenName = entry.ZongMenName,
					ResourceType = entry.ResourceType,
					ResourceId = entry.ResourceId,
					ResourceName = entry.ResourceName,
					Amount = entry.Amount,
					ActorId = entry.ActorId,
					ActorName = entry.ActorName,
					DaoTu = entry.DaoTu,
					SourcePlace = entry.SourcePlace,
					Year = entry.Year
				};

				if (entry.ResourceType == ResourceTypeCaiQiFa)
				{
					caiQiFaRecords.Add(record);
				}
				else
				{
					caiQiRecords.Add(record);
				}
			}
		}
	}

	internal static void ImportArchiveRecords(
		IReadOnlyList<XjWorldArchiveZongMenCaiQiRecord> caiQiRecords,
		IReadOnlyList<XjWorldArchiveZongMenCaiQiRecord> caiQiFaRecords)
	{
		Clear();
		ImportArchiveRecords(caiQiRecords, ResourceTypeCaiQi);
		ImportArchiveRecords(caiQiFaRecords, ResourceTypeCaiQiFa);
	}

	private static IReadOnlyList<XjSectCaiQiWarehouseEntry> ReadEntries(long zongMenId, string resourceType)
	{
		if (zongMenId <= 0L || !entriesByZongMenId.TryGetValue(zongMenId, out List<XjSectCaiQiWarehouseEntry> entries) || entries.Count == 0)
		{
			return System.Array.Empty<XjSectCaiQiWarehouseEntry>();
		}

		List<XjSectCaiQiWarehouseEntry> result = new List<XjSectCaiQiWarehouseEntry>();
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectCaiQiWarehouseEntry entry = entries[i];
			if (entry.Found && entry.ResourceType == resourceType)
			{
				result.Add(entry);
			}
		}

		return result;
	}

	private static void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveZongMenCaiQiRecord> records, string defaultResourceType)
	{
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveZongMenCaiQiRecord record = records[i];
			if (record == null || record.ZongMenId <= 0L || string.IsNullOrWhiteSpace(record.ResourceId) || record.Amount <= 0)
			{
				continue;
			}

			string resourceType = string.IsNullOrWhiteSpace(record.ResourceType) ? defaultResourceType : record.ResourceType.Trim();
			if (resourceType == ResourceTypeCaiQiFa)
			{
				TryAddCaiQiFa(record.ZongMenId, record.ZongMenName, record.ResourceName, record.DaoTu, record.SourcePlace, record.ActorId, record.ActorName, record.Year, false);
			}
			else
			{
				TryAddCaiQiResource(record.ZongMenId, record.ZongMenName, record.ResourceId, record.Amount, record.ActorId, record.ActorName, record.SourcePlace, record.Year, false);
			}
		}
	}

	private static void RebuildEntryKeys()
	{
		entryKeys.Clear();
		foreach (KeyValuePair<long, List<XjSectCaiQiWarehouseEntry>> pair in entriesByZongMenId)
		{
			List<XjSectCaiQiWarehouseEntry> entries = pair.Value;
			if (entries == null) continue;
			for (int i = 0; i < entries.Count; i++)
			{
				XjSectCaiQiWarehouseEntry entry = entries[i];
				if (!entry.Found || entry.ZongMenId <= 0L || string.IsNullOrWhiteSpace(entry.ResourceId)) continue;
				entryKeys.Add(entry.ZongMenId + "|" + entry.ResourceType + "|" + entry.ResourceId);
			}
		}
	}

	private static string ResolveCaiQiDisplayName(string resourceId)
	{
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string displayName)
			&& !string.IsNullOrWhiteSpace(displayName))
		{
			return displayName.Trim();
		}

		return string.Equals(resourceId, "zaqi", System.StringComparison.Ordinal) ? "杂气" : "未名先天之气";
	}
}
