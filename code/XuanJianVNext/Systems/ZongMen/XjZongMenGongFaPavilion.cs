using System.Collections.Generic;
using System;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.ZongMen;

internal readonly struct XjZongMenGongFaPavilionEntry
{
	internal readonly bool Found;
	internal readonly long ZongMenId;
	internal readonly string ZongMenName;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string Name;
	internal readonly int Grade;
	internal readonly string DaoTu;
	internal readonly string SourceType;
	internal readonly string SourceGongFaName;
	internal readonly string MappedXianJi;
	internal readonly int Year;

	internal XjZongMenGongFaPavilionEntry(
		bool found,
		long zongMenId,
		string zongMenName,
		long actorId,
		string actorName,
		string name,
		int grade,
		string daoTu,
		string sourceType,
		string sourceGongFaName,
		string mappedXianJi,
		int year)
	{
		Found = found;
		ZongMenId = zongMenId < 0L ? 0L : zongMenId;
		ZongMenName = zongMenName ?? string.Empty;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		Name = name ?? string.Empty;
		Grade = grade < 0 ? 0 : grade;
		DaoTu = daoTu ?? string.Empty;
		SourceType = sourceType ?? string.Empty;
		SourceGongFaName = sourceGongFaName ?? string.Empty;
		MappedXianJi = mappedXianJi ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

internal static class XjZongMenGongFaPavilion
{
	internal const string SourceTypeGongFa = "GongFa";
	internal const string SourceTypeQiuJinFa = "QiuJinFa";

	private static readonly Dictionary<long, List<XjZongMenGongFaPavilionEntry>> entriesByZongMenId = new Dictionary<long, List<XjZongMenGongFaPavilionEntry>>();
	private static readonly HashSet<string> entryKeys = new HashSet<string>();

	internal static bool TryAddGongFa(
		long zongMenId,
		string zongMenName,
		long actorId,
		string actorName,
		string name,
		int grade,
		string daoTu,
		string sourceType,
		string mappedXianJi,
		int year)
	{
		if (!IsValidGongFaGrade(grade))
		{
			return false;
		}

		return TryAdd(zongMenId, zongMenName, actorId, actorName, name, grade, daoTu, SourceTypeGongFa, string.Empty, mappedXianJi, year, true);
	}

	internal static bool TryAddQiuJinFa(
		long zongMenId,
		string zongMenName,
		long actorId,
		string actorName,
		string name,
		int grade,
		string daoTu,
		string sourceGongFaName,
		string boundAuthority,
		int year)
	{
		return TryAdd(zongMenId, zongMenName, actorId, actorName, name, grade, daoTu, SourceTypeQiuJinFa, sourceGongFaName, boundAuthority, year, true);
	}

	internal static IReadOnlyList<XjZongMenGongFaPavilionEntry> ReadGongFaEntries(long zongMenId)
	{
		return ReadEntries(zongMenId, SourceTypeGongFa);
	}

	internal static IReadOnlyList<XjZongMenGongFaPavilionEntry> ReadQiuJinFaEntries(long zongMenId)
	{
		return ReadEntries(zongMenId, SourceTypeQiuJinFa);
	}

	internal static bool TransferSectWarehouse(long sourceZongMenId, long targetZongMenId, string targetZongMenName, int year, out int gongFaEntries, out int qiuJinFaEntries)
	{
		gongFaEntries = 0;
		qiuJinFaEntries = 0;
		if (sourceZongMenId <= 0L || targetZongMenId <= 0L || sourceZongMenId == targetZongMenId
			|| !entriesByZongMenId.TryGetValue(sourceZongMenId, out List<XjZongMenGongFaPavilionEntry> sourceEntries))
		{
			return false;
		}

		List<XjZongMenGongFaPavilionEntry> snapshot = new List<XjZongMenGongFaPavilionEntry>(sourceEntries);
		entriesByZongMenId.Remove(sourceZongMenId);
		bool changed = false;
		for (int i = 0; i < snapshot.Count; i++)
		{
			XjZongMenGongFaPavilionEntry entry = snapshot[i];
			if (!entry.Found || string.IsNullOrWhiteSpace(entry.Name) || entry.ActorId <= 0L) continue;
			if (entry.SourceType == SourceTypeQiuJinFa) qiuJinFaEntries++;
			else gongFaEntries++;
			TryAdd(targetZongMenId, targetZongMenName, entry.ActorId, entry.ActorName, entry.Name, entry.Grade,
				entry.DaoTu, entry.SourceType, entry.SourceGongFaName, entry.MappedXianJi, Math.Max(year, entry.Year), false);
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
		List<XjWorldArchiveZongMenGongFaRecord> gongFaRecords,
		List<XjWorldArchiveZongMenGongFaRecord> qiuJinFaRecords)
	{
		if (gongFaRecords == null || qiuJinFaRecords == null)
		{
			return;
		}

		foreach (KeyValuePair<long, List<XjZongMenGongFaPavilionEntry>> zongMenEntry in entriesByZongMenId)
		{
			List<XjZongMenGongFaPavilionEntry> entries = zongMenEntry.Value;
			if (entries == null)
			{
				continue;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				XjZongMenGongFaPavilionEntry entry = entries[i];
				if (!entry.Found || entry.ZongMenId <= 0L || entry.ActorId <= 0L || string.IsNullOrWhiteSpace(entry.Name))
				{
					continue;
				}

				if (entry.SourceType == SourceTypeGongFa && !IsValidGongFaGrade(entry.Grade))
				{
					continue;
				}

				XjWorldArchiveZongMenGongFaRecord record = new XjWorldArchiveZongMenGongFaRecord
				{
					ZongMenId = entry.ZongMenId,
					ZongMenName = entry.ZongMenName,
					ActorId = entry.ActorId,
					ActorName = entry.ActorName,
					Name = entry.Name,
					Grade = entry.Grade,
					DaoTu = entry.DaoTu,
					SourceType = entry.SourceType,
					SourceGongFaName = entry.SourceGongFaName,
					MappedXianJi = entry.MappedXianJi,
					Year = entry.Year
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
		IReadOnlyList<XjWorldArchiveZongMenGongFaRecord> gongFaRecords,
		IReadOnlyList<XjWorldArchiveZongMenGongFaRecord> qiuJinFaRecords)
	{
		Clear();
		ImportArchiveRecords(gongFaRecords, SourceTypeGongFa);
		ImportArchiveRecords(qiuJinFaRecords, SourceTypeQiuJinFa);
	}

	private static bool TryAdd(
		long zongMenId,
		string zongMenName,
		long actorId,
		string actorName,
		string name,
		int grade,
		string daoTu,
		string sourceType,
		string sourceGongFaName,
		string mappedXianJi,
		int year,
		bool markChanged)
	{
		if (zongMenId <= 0L || actorId <= 0L || string.IsNullOrWhiteSpace(name) || grade <= 0 || string.IsNullOrWhiteSpace(sourceType))
		{
			return false;
		}

		string normalizedSource = sourceType.Trim();
		if (normalizedSource == SourceTypeGongFa && !IsValidGongFaGrade(grade))
		{
			return false;
		}

		string normalizedName = name.Trim();
		string normalizedDaoTu = string.IsNullOrWhiteSpace(daoTu) ? string.Empty : daoTu.Trim();
		string normalizedSourceGongFaName = string.IsNullOrWhiteSpace(sourceGongFaName) ? string.Empty : sourceGongFaName.Trim();
		string normalizedMappedXianJi;
		if (!string.IsNullOrWhiteSpace(mappedXianJi))
		{
			normalizedMappedXianJi = mappedXianJi.Trim();
		}
		else if (normalizedSource == SourceTypeQiuJinFa)
		{
			// 求金法条目的该字段承载其绑定权柄；旧存档缺失时按原始功法稳定补算。
			normalizedMappedXianJi = XjFamilyHighGradeTransmission.ResolveBoundAuthority(
				normalizedDaoTu,
				normalizedName,
				normalizedSourceGongFaName);
		}
		else
		{
			normalizedMappedXianJi = XjFamilyHighGradeTransmission.ResolveMappedXianJi(
				normalizedDaoTu,
				normalizedName,
				grade,
				XjFamilyGongFaWarehouse.SourceTypeGongFa);
		}

		if (!entriesByZongMenId.TryGetValue(zongMenId, out List<XjZongMenGongFaPavilionEntry> entries))
		{
			entries = new List<XjZongMenGongFaPavilionEntry>();
			entriesByZongMenId[zongMenId] = entries;
		}

		var candidate = new XjZongMenGongFaPavilionEntry(
			true,
			zongMenId,
			zongMenName,
			actorId,
			actorName,
			normalizedName,
			grade,
			normalizedDaoTu,
			normalizedSource,
			normalizedSourceGongFaName,
			normalizedMappedXianJi,
			year);
		int equivalentIndex = FindEquivalentEntry(entries, candidate);
		if (equivalentIndex >= 0)
		{
			if (ShouldPreferEntry(candidate, entries[equivalentIndex]))
			{
				entries[equivalentIndex] = candidate;
				if (markChanged)
				{
					XjWorldArchiveSystem.MarkChanged();
				}
				return true;
			}
			return false;
		}

		string key = BuildStableEntryKey(candidate);
		if (!entryKeys.Add(key))
		{
			return false;
		}

		entries.Add(candidate);
		if (markChanged)
		{
			XjWorldArchiveSystem.MarkChanged();
		}
		return true;
	}

	private static IReadOnlyList<XjZongMenGongFaPavilionEntry> ReadEntries(long zongMenId, string sourceType)
	{
		if (zongMenId <= 0L || !entriesByZongMenId.TryGetValue(zongMenId, out List<XjZongMenGongFaPavilionEntry> entries) || entries.Count == 0)
		{
			return System.Array.Empty<XjZongMenGongFaPavilionEntry>();
		}

		List<XjZongMenGongFaPavilionEntry> result = new List<XjZongMenGongFaPavilionEntry>();
		for (int i = 0; i < entries.Count; i++)
		{
			XjZongMenGongFaPavilionEntry entry = entries[i];
			if (entry.Found && entry.SourceType == sourceType)
			{
				AddOrReplaceDisplayEntry(result, entry);
			}
		}

		return result;
	}

	private static int FindEquivalentEntry(List<XjZongMenGongFaPavilionEntry> entries, in XjZongMenGongFaPavilionEntry candidate)
	{
		if (entries == null || entries.Count == 0) return -1;
		for (int i = 0; i < entries.Count; i++)
		{
			XjZongMenGongFaPavilionEntry existing = entries[i];
			if (!existing.Found || !string.Equals(existing.SourceType, candidate.SourceType, StringComparison.Ordinal)) continue;
			if (AreEquivalent(existing, candidate)) return i;
		}
		return -1;
	}

	private static bool AreEquivalent(in XjZongMenGongFaPavilionEntry left, in XjZongMenGongFaPavilionEntry right)
	{
		if (!string.Equals(left.SourceType, right.SourceType, StringComparison.Ordinal)) return false;
		if (!string.Equals((left.DaoTu ?? string.Empty).Trim(), (right.DaoTu ?? string.Empty).Trim(), StringComparison.Ordinal)) return false;
		if (string.Equals(left.SourceType, SourceTypeQiuJinFa, StringComparison.Ordinal))
		{
			string leftAuthority = (left.MappedXianJi ?? string.Empty).Trim();
			string rightAuthority = (right.MappedXianJi ?? string.Empty).Trim();
			if (leftAuthority.Length > 0 && rightAuthority.Length > 0) return string.Equals(leftAuthority, rightAuthority, StringComparison.Ordinal);
			return string.Equals(NormalizeEntryName(left.Name), NormalizeEntryName(right.Name), StringComparison.Ordinal);
		}
		if (left.Grade != right.Grade) return false;
		string leftMapped = (left.MappedXianJi ?? string.Empty).Trim();
		string rightMapped = (right.MappedXianJi ?? string.Empty).Trim();
		if (leftMapped.Length > 0 && rightMapped.Length > 0) return string.Equals(leftMapped, rightMapped, StringComparison.Ordinal);
		return string.Equals(NormalizeEntryName(left.Name), NormalizeEntryName(right.Name), StringComparison.Ordinal);
	}

	private static string BuildStableEntryKey(in XjZongMenGongFaPavilionEntry entry)
	{
		string identity = !string.IsNullOrWhiteSpace(entry.MappedXianJi) ? entry.MappedXianJi.Trim() : NormalizeEntryName(entry.Name);
		return entry.ZongMenId + "|" + entry.SourceType + "|" + (entry.DaoTu ?? string.Empty).Trim() + "|" + entry.Grade + "|" + identity;
	}

	private static string NormalizeEntryName(string name)
	{
		return XjFamilyGongFaWarehouseUI.NormalizeGongFaDisplayName(name ?? string.Empty).Trim();
	}

	private static void RebuildEntryKeys()
	{
		entryKeys.Clear();
		foreach (KeyValuePair<long, List<XjZongMenGongFaPavilionEntry>> pair in entriesByZongMenId)
		{
			List<XjZongMenGongFaPavilionEntry> entries = pair.Value;
			if (entries == null) continue;
			for (int i = 0; i < entries.Count; i++)
			{
				XjZongMenGongFaPavilionEntry entry = entries[i];
				if (!entry.Found || entry.ZongMenId <= 0L || entry.ActorId <= 0L || string.IsNullOrWhiteSpace(entry.Name)) continue;
				entryKeys.Add(BuildStableEntryKey(entry));
			}
		}
	}

	private static void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveZongMenGongFaRecord> records, string defaultSourceType)
	{
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveZongMenGongFaRecord record = records[i];
			if (record == null || record.ZongMenId <= 0L || record.ActorId <= 0L || string.IsNullOrWhiteSpace(record.Name) || record.Grade <= 0)
			{
				continue;
			}

			string sourceType = string.IsNullOrWhiteSpace(record.SourceType) ? defaultSourceType : record.SourceType.Trim();
			TryAdd(record.ZongMenId, record.ZongMenName, record.ActorId, record.ActorName, record.Name, record.Grade, record.DaoTu, sourceType, record.SourceGongFaName, record.MappedXianJi, record.Year, false);
		}
	}

	private static void AddOrReplaceDisplayEntry(List<XjZongMenGongFaPavilionEntry> entries, in XjZongMenGongFaPavilionEntry candidate)
	{
		for (int i = 0; i < entries.Count; i++)
		{
			XjZongMenGongFaPavilionEntry existing = entries[i];
			if (!AreEquivalent(existing, candidate)) continue;

			if (ShouldPreferEntry(candidate, existing))
			{
				entries[i] = candidate;
			}
			return;
		}

		entries.Add(candidate);
	}

	private static bool ShouldPreferEntry(in XjZongMenGongFaPavilionEntry candidate, in XjZongMenGongFaPavilionEntry existing)
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

		if (candidate.Year != existing.Year)
		{
			return candidate.Year > existing.Year;
		}

		return string.Compare(candidate.Name, existing.Name, System.StringComparison.Ordinal) < 0;
	}

	private static bool IsValidGongFaGrade(int grade)
	{
		return grade >= 5 && grade <= 6;
	}
}
