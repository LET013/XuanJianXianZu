using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Warehouse;

internal readonly struct XjFamilyGongFaWarehouseEntry
{
	internal readonly bool Found;
	internal readonly long FamilyId;
	internal readonly long ActorId;
	internal readonly string GongFaName;
	internal readonly int Grade;
	internal readonly int Timestamp;
	internal readonly string SourceType;
	internal readonly string DaoTu;
	internal readonly string BoundGongFaName;
	internal readonly string MappedXianJi;
	internal readonly string BoundAuthority;

	internal XjFamilyGongFaWarehouseEntry(
		bool found,
		long familyId,
		long actorId,
		string gongFaName,
		int grade,
		int timestamp,
		string sourceType,
		string daoTu,
		string boundGongFaName,
		string mappedXianJi,
		string boundAuthority)
	{
		Found = found;
		FamilyId = familyId < 0L ? 0L : familyId;
		ActorId = actorId < 0L ? 0L : actorId;
		GongFaName = gongFaName ?? string.Empty;
		Grade = grade < 0 ? 0 : grade;
		Timestamp = timestamp < 0 ? 0 : timestamp;
		SourceType = sourceType ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		BoundGongFaName = boundGongFaName ?? string.Empty;
		MappedXianJi = mappedXianJi ?? string.Empty;
		BoundAuthority = boundAuthority ?? string.Empty;
	}
}

internal static class XjFamilyGongFaWarehouse
{
	internal const string SourceTypeGongFa = "GongFa";
	internal const string SourceTypeQiuJinFa = "QiuJinFa";

	private static readonly Dictionary<long, List<XjFamilyGongFaWarehouseEntry>> entriesByFamilyId = new Dictionary<long, List<XjFamilyGongFaWarehouseEntry>>();
	private static readonly HashSet<string> entryKeys = new HashSet<string>();

	internal static bool AddGongFaToFamily(
		long actorId,
		long familyId,
		string gongFaName,
		int grade,
		int timestamp,
		string sourceType,
		string daoTu = "",
		string boundGongFaName = "",
		string mappedXianJi = "",
		string boundAuthority = "")
	{
		if (actorId <= 0L
			|| familyId <= 0L
			|| string.IsNullOrWhiteSpace(gongFaName)
			|| grade <= 0
			|| string.IsNullOrWhiteSpace(sourceType)
			|| (string.Equals(sourceType, SourceTypeGongFa, StringComparison.Ordinal) && grade < 4))
		{
			return false;
		}

		string normalizedDaoTu = string.IsNullOrWhiteSpace(daoTu) ? string.Empty : daoTu.Trim();
		string normalizedBoundGongFaName = string.IsNullOrWhiteSpace(boundGongFaName) ? string.Empty : boundGongFaName.Trim();
		string normalizedMappedXianJi = string.IsNullOrWhiteSpace(mappedXianJi)
			? XjFamilyHighGradeTransmission.ResolveMappedXianJi(normalizedDaoTu, gongFaName, grade, sourceType)
			: mappedXianJi.Trim();
		string normalizedBoundAuthority = string.IsNullOrWhiteSpace(boundAuthority)
			? XjFamilyHighGradeTransmission.ResolveBoundAuthority(normalizedDaoTu, gongFaName, normalizedBoundGongFaName)
			: boundAuthority.Trim();

		if (!entriesByFamilyId.TryGetValue(familyId, out List<XjFamilyGongFaWarehouseEntry> entries))
		{
			entries = new List<XjFamilyGongFaWarehouseEntry>();
			entriesByFamilyId[familyId] = entries;
		}

		var candidate = new XjFamilyGongFaWarehouseEntry(
			true,
			familyId,
			actorId,
			gongFaName.Trim(),
			grade,
			timestamp,
			sourceType.Trim(),
			normalizedDaoTu,
			normalizedBoundGongFaName,
			normalizedMappedXianJi,
			normalizedBoundAuthority);
		int equivalentIndex = FindEquivalentEntry(entries, candidate);
		if (equivalentIndex >= 0)
		{
			if (ShouldPreferEntry(candidate, entries[equivalentIndex]))
			{
				entries[equivalentIndex] = candidate;
				XjWorldArchiveSystem.MarkChanged();
				return true;
			}
			return false;
		}

		string entryKey = BuildStableEntryKey(candidate);
		if (!entryKeys.Add(entryKey))
		{
			return false;
		}

		entries.Add(candidate);
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static IReadOnlyList<XjFamilyGongFaWarehouseEntry> ReadFamilyEntriesView(long familyId)
	{
		if (familyId <= 0L
			|| !entriesByFamilyId.TryGetValue(familyId, out List<XjFamilyGongFaWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyGongFaWarehouseEntry>();
		}

		// 瞬时只读投影，避免家族详情为每个家族复制仓库列表；调用方不得持有或修改。
		return entries;
	}

	internal static IReadOnlyList<XjFamilyGongFaWarehouseEntry> ReadFamilyEntries(long familyId)
	{
		if (familyId <= 0L
			|| !entriesByFamilyId.TryGetValue(familyId, out List<XjFamilyGongFaWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyGongFaWarehouseEntry>();
		}

		return new List<XjFamilyGongFaWarehouseEntry>(entries);
	}

	internal static void Clear()
	{
		entriesByFamilyId.Clear();
		entryKeys.Clear();
	}

	internal static void ExportArchiveRecords(
		List<XjWorldArchiveGongFaRecord> gongFaRecords,
		List<XjWorldArchiveGongFaRecord> qiuJinFaRecords)
	{
		if (gongFaRecords == null || qiuJinFaRecords == null)
		{
			return;
		}

		foreach (KeyValuePair<long, List<XjFamilyGongFaWarehouseEntry>> familyEntry in entriesByFamilyId)
		{
			List<XjFamilyGongFaWarehouseEntry> entries = familyEntry.Value;
			if (entries == null)
			{
				continue;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyGongFaWarehouseEntry entry = entries[i];
				if (!entry.Found || entry.FamilyId <= 0L || entry.ActorId <= 0L || string.IsNullOrWhiteSpace(entry.GongFaName))
				{
					continue;
				}

				XjWorldArchiveGongFaRecord record = new XjWorldArchiveGongFaRecord
				{
					FamilyStableId = entry.FamilyId,
					ActorId = entry.ActorId,
					Name = entry.GongFaName,
					Grade = entry.Grade,
					Year = entry.Timestamp,
					SourceType = entry.SourceType,
					DaoTu = entry.DaoTu,
					BoundGongFaName = entry.BoundGongFaName,
					MappedXianJi = entry.MappedXianJi,
					BoundAuthority = entry.BoundAuthority
				};

				if (entry.SourceType == SourceTypeQiuJinFa)
				{
					qiuJinFaRecords.Add(record);
				}
				else
				{
					gongFaRecords.Add(record);
				}
			}
		}
	}

	internal static void ImportArchiveRecords(
		IReadOnlyList<XjWorldArchiveGongFaRecord> gongFaRecords,
		IReadOnlyList<XjWorldArchiveGongFaRecord> qiuJinFaRecords)
	{
		entriesByFamilyId.Clear();
		entryKeys.Clear();
		ImportArchiveRecords(gongFaRecords, SourceTypeGongFa);
		ImportArchiveRecords(qiuJinFaRecords, SourceTypeQiuJinFa);
	}

	private static void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveGongFaRecord> records, string defaultSourceType)
	{
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveGongFaRecord record = records[i];
			if (record == null
				|| record.FamilyStableId <= 0L
				|| record.ActorId <= 0L
				|| string.IsNullOrWhiteSpace(record.Name)
				|| record.Grade <= 0)
			{
				continue;
			}

			string sourceType = string.IsNullOrWhiteSpace(record.SourceType) ? defaultSourceType : record.SourceType.Trim();
			if (string.Equals(sourceType, SourceTypeGongFa, StringComparison.Ordinal) && record.Grade < 4)
			{
				continue;
			}
			string daoTu = string.IsNullOrWhiteSpace(record.DaoTu) ? string.Empty : record.DaoTu.Trim();
			string boundGongFaName = string.IsNullOrWhiteSpace(record.BoundGongFaName) ? string.Empty : record.BoundGongFaName.Trim();
			string mappedXianJi = string.IsNullOrWhiteSpace(record.MappedXianJi) ? string.Empty : record.MappedXianJi.Trim();
			string boundAuthority = string.IsNullOrWhiteSpace(record.BoundAuthority) ? string.Empty : record.BoundAuthority.Trim();
			if (!entriesByFamilyId.TryGetValue(record.FamilyStableId, out List<XjFamilyGongFaWarehouseEntry> entries))
			{
				entries = new List<XjFamilyGongFaWarehouseEntry>();
				entriesByFamilyId[record.FamilyStableId] = entries;
			}

			var candidate = new XjFamilyGongFaWarehouseEntry(
				true, record.FamilyStableId, record.ActorId, record.Name.Trim(), record.Grade, record.Year,
				sourceType, daoTu, boundGongFaName, mappedXianJi, boundAuthority);
			int equivalentIndex = FindEquivalentEntry(entries, candidate);
			if (equivalentIndex >= 0)
			{
				if (ShouldPreferEntry(candidate, entries[equivalentIndex])) entries[equivalentIndex] = candidate;
				continue;
			}
			string entryKey = BuildStableEntryKey(candidate);
			if (!entryKeys.Add(entryKey)) continue;
			entries.Add(candidate);
		}
	}

	private static int FindEquivalentEntry(List<XjFamilyGongFaWarehouseEntry> entries, in XjFamilyGongFaWarehouseEntry candidate)
	{
		if (entries == null || entries.Count == 0) return -1;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry existing = entries[i];
			if (!existing.Found || !string.Equals(existing.SourceType, candidate.SourceType, StringComparison.Ordinal)) continue;
			if (AreEquivalent(existing, candidate)) return i;
		}
		return -1;
	}

	private static bool AreEquivalent(in XjFamilyGongFaWarehouseEntry left, in XjFamilyGongFaWarehouseEntry right)
	{
		if (!string.Equals(left.SourceType, right.SourceType, StringComparison.Ordinal)) return false;
		string leftDaoTu = (left.DaoTu ?? string.Empty).Trim();
		string rightDaoTu = (right.DaoTu ?? string.Empty).Trim();
		if (!string.Equals(leftDaoTu, rightDaoTu, StringComparison.Ordinal)) return false;
		if (string.Equals(left.SourceType, SourceTypeQiuJinFa, StringComparison.Ordinal))
		{
			string leftAuthority = (left.BoundAuthority ?? string.Empty).Trim();
			string rightAuthority = (right.BoundAuthority ?? string.Empty).Trim();
			if (leftAuthority.Length > 0 && rightAuthority.Length > 0) return string.Equals(leftAuthority, rightAuthority, StringComparison.Ordinal);
			return string.Equals(NormalizeEntryName(left.GongFaName), NormalizeEntryName(right.GongFaName), StringComparison.Ordinal);
		}
		if (left.Grade != right.Grade) return false;
		string leftMapped = (left.MappedXianJi ?? string.Empty).Trim();
		string rightMapped = (right.MappedXianJi ?? string.Empty).Trim();
		if (leftMapped.Length > 0 && rightMapped.Length > 0) return string.Equals(leftMapped, rightMapped, StringComparison.Ordinal);
		return string.Equals(NormalizeEntryName(left.GongFaName), NormalizeEntryName(right.GongFaName), StringComparison.Ordinal);
	}

	private static string BuildStableEntryKey(in XjFamilyGongFaWarehouseEntry entry)
	{
		string identity;
		if (string.Equals(entry.SourceType, SourceTypeQiuJinFa, StringComparison.Ordinal))
		{
			identity = !string.IsNullOrWhiteSpace(entry.BoundAuthority) ? entry.BoundAuthority.Trim() : NormalizeEntryName(entry.GongFaName);
		}
		else
		{
			identity = !string.IsNullOrWhiteSpace(entry.MappedXianJi) ? entry.MappedXianJi.Trim() : NormalizeEntryName(entry.GongFaName);
		}
		return entry.FamilyId + "|" + entry.SourceType + "|" + (entry.DaoTu ?? string.Empty).Trim() + "|" + entry.Grade + "|" + identity;
	}

	private static string NormalizeEntryName(string name)
	{
		return XjFamilyGongFaWarehouseUI.NormalizeGongFaDisplayName(name ?? string.Empty).Trim();
	}

	private static bool ShouldPreferEntry(in XjFamilyGongFaWarehouseEntry candidate, in XjFamilyGongFaWarehouseEntry existing)
	{
		if (candidate.Grade != existing.Grade)
		{
			return candidate.Grade > existing.Grade;
		}

		bool candidateMapped = !string.IsNullOrWhiteSpace(candidate.MappedXianJi);
		bool existingMapped = !string.IsNullOrWhiteSpace(existing.MappedXianJi);
		if (candidateMapped != existingMapped)
		{
			return candidateMapped;
		}

		bool candidateAuthority = !string.IsNullOrWhiteSpace(candidate.BoundAuthority);
		bool existingAuthority = !string.IsNullOrWhiteSpace(existing.BoundAuthority);
		if (candidateAuthority != existingAuthority)
		{
			return candidateAuthority;
		}

		if (candidate.Timestamp != existing.Timestamp)
		{
			return candidate.Timestamp > existing.Timestamp;
		}

		return string.Compare(candidate.GongFaName, existing.GongFaName, StringComparison.Ordinal) < 0;
	}
}

internal readonly struct XjFamilyGongFaWarehouseUIItem
{
	internal readonly string Name;
	internal readonly int Grade;
	internal readonly int Count;
	internal readonly string SourceType;
	internal readonly string DaoTu;
	internal readonly string BoundGongFaName;
	internal readonly string MappedXianJi;
	internal readonly string BoundAuthority;

	internal XjFamilyGongFaWarehouseUIItem(
		string name,
		int grade,
		int count,
		string sourceType,
		string daoTu = "",
		string boundGongFaName = "",
		string mappedXianJi = "",
		string boundAuthority = "")
	{
		Name = name ?? string.Empty;
		Grade = grade;
		Count = count;
		SourceType = sourceType ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		BoundGongFaName = boundGongFaName ?? string.Empty;
		MappedXianJi = mappedXianJi ?? string.Empty;
		BoundAuthority = boundAuthority ?? string.Empty;
	}
}

internal static class XjFamilyGongFaWarehouseUI
{
	private const int MaxDisplayItemCount = 12;

	internal static XjFamilyGongFaWarehouseUIItem[] BuildItems(long familyId, string sourceType)
	{
		IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries = XjFamilyGongFaWarehouse.ReadFamilyEntries(familyId);
		if (entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		}

		List<XjFamilyGongFaWarehouseUIItem> items = new List<XjFamilyGongFaWarehouseUIItem>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry entry = entries[i];
			if (!entry.Found || string.IsNullOrWhiteSpace(entry.GongFaName) || entry.Grade <= 0)
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(sourceType) && !string.Equals(entry.SourceType, sourceType, System.StringComparison.Ordinal))
			{
				continue;
			}

			items.Add(new XjFamilyGongFaWarehouseUIItem(
				entry.GongFaName.Trim(),
				entry.Grade,
				1,
				entry.SourceType,
				entry.DaoTu,
				entry.BoundGongFaName,
				entry.MappedXianJi,
				entry.BoundAuthority));
		}

		if (items.Count == 0)
		{
			return System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		}

		items = CoalesceGongFaDisplayItems(items);
		items.Sort((left, right) =>
		{
			int gradeCompare = right.Grade.CompareTo(left.Grade);
			if (gradeCompare != 0)
			{
				return gradeCompare;
			}

			int sourceCompare = string.Compare(left.SourceType, right.SourceType, System.StringComparison.Ordinal);
			if (sourceCompare != 0) return sourceCompare;
			int nameCompare = string.Compare(left.Name, right.Name, System.StringComparison.Ordinal);
			if (nameCompare != 0) return nameCompare;
			int daoTuCompare = string.Compare(left.DaoTu, right.DaoTu, System.StringComparison.Ordinal);
			if (daoTuCompare != 0) return daoTuCompare;
			int boundCompare = string.Compare(left.BoundGongFaName, right.BoundGongFaName, System.StringComparison.Ordinal);
			if (boundCompare != 0) return boundCompare;
			int xianJiCompare = string.Compare(left.MappedXianJi, right.MappedXianJi, System.StringComparison.Ordinal);
			return xianJiCompare != 0
				? xianJiCompare
				: string.Compare(left.BoundAuthority, right.BoundAuthority, System.StringComparison.Ordinal);
		});

		List<XjFamilyGongFaWarehouseUIItem> result = new List<XjFamilyGongFaWarehouseUIItem>(System.Math.Min(items.Count, MaxDisplayItemCount));
		for (int i = 0; i < items.Count && i < MaxDisplayItemCount; i++)
		{
			result.Add(items[i]);
		}

		return result.ToArray();
	}

	internal static List<XjFamilyGongFaWarehouseUIItem> CoalesceGongFaDisplayItems(IReadOnlyList<XjFamilyGongFaWarehouseUIItem> source)
	{
		List<XjFamilyGongFaWarehouseUIItem> result = new List<XjFamilyGongFaWarehouseUIItem>();
		if (source == null || source.Count == 0)
		{
			return result;
		}

		for (int i = 0; i < source.Count; i++)
		{
			XjFamilyGongFaWarehouseUIItem item = source[i];
			if (string.IsNullOrWhiteSpace(item.Name) || item.Grade <= 0)
			{
				continue;
			}

			int existingIndex = FindEquivalentGongFaDisplayItem(result, item);
			if (existingIndex < 0)
			{
				result.Add(item);
				continue;
			}

			if (ShouldPreferDisplayItem(item, result[existingIndex]))
			{
				result[existingIndex] = item;
			}
		}

		return result;
	}

	internal static string NormalizeGongFaDisplayName(string value)
	{
		string result = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
		string[] suffixes =
		{
			"（一品）",
			"（二品）",
			"（三品）",
			"（四品）",
			"（五品）",
			"（六品）"
		};
		for (int i = 0; i < suffixes.Length; i++)
		{
			if (result.EndsWith(suffixes[i], System.StringComparison.Ordinal))
			{
				return result.Substring(0, result.Length - suffixes[i].Length).Trim();
			}
		}
		return result;
	}

	private static int FindEquivalentGongFaDisplayItem(List<XjFamilyGongFaWarehouseUIItem> items, in XjFamilyGongFaWarehouseUIItem target)
	{
		string targetName = NormalizeGongFaDisplayName(target.Name);
		for (int i = 0; i < items.Count; i++)
		{
			XjFamilyGongFaWarehouseUIItem item = items[i];
			if (!string.Equals(item.SourceType, target.SourceType, System.StringComparison.Ordinal))
			{
				continue;
			}

			bool sameName = string.Equals(NormalizeGongFaDisplayName(item.Name), targetName, System.StringComparison.Ordinal);
			bool sameMappedXianJi = string.Equals(
				item.MappedXianJi ?? string.Empty,
				target.MappedXianJi ?? string.Empty,
				System.StringComparison.Ordinal);
			if (sameName && item.Grade == target.Grade && sameMappedXianJi)
			{
				return i;
			}
		}
		return -1;
	}

	private static bool ShouldPreferDisplayItem(in XjFamilyGongFaWarehouseUIItem candidate, in XjFamilyGongFaWarehouseUIItem existing)
	{
		if (candidate.Grade != existing.Grade)
		{
			return candidate.Grade > existing.Grade;
		}

		bool candidateMapped = !string.IsNullOrWhiteSpace(candidate.MappedXianJi);
		bool existingMapped = !string.IsNullOrWhiteSpace(existing.MappedXianJi);
		if (candidateMapped != existingMapped)
		{
			return candidateMapped;
		}

		bool candidateBound = !string.IsNullOrWhiteSpace(candidate.BoundGongFaName)
			|| !string.IsNullOrWhiteSpace(candidate.BoundAuthority);
		bool existingBound = !string.IsNullOrWhiteSpace(existing.BoundGongFaName)
			|| !string.IsNullOrWhiteSpace(existing.BoundAuthority);
		if (candidateBound != existingBound)
		{
			return candidateBound;
		}

		return string.Compare(candidate.Name, existing.Name, System.StringComparison.Ordinal) < 0;
	}
}
