using System.Collections.Generic;
using System;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.Sect;

internal readonly struct XjSectGongFaPavilionEntry
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

	internal XjSectGongFaPavilionEntry(
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

internal static class XjSectGongFaPavilion
{
	internal const string SourceTypeGongFa = "GongFa";
	internal const string SourceTypeQiuJinFa = "QiuJinFa";

	private static readonly Dictionary<long, List<XjSectGongFaPavilionEntry>> entriesByZongMenId = new Dictionary<long, List<XjSectGongFaPavilionEntry>>();
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

	internal static IReadOnlyList<XjSectGongFaPavilionEntry> ReadGongFaEntries(long zongMenId)
	{
		return ReadEntries(zongMenId, SourceTypeGongFa);
	}

	internal static IReadOnlyList<XjSectGongFaPavilionEntry> ReadQiuJinFaEntries(long zongMenId)
	{
		return ReadEntries(zongMenId, SourceTypeQiuJinFa);
	}

	internal static bool TransferSectWarehouse(long sourceZongMenId, long targetZongMenId, string targetZongMenName, int year, out int gongFaEntries, out int qiuJinFaEntries)
	{
		gongFaEntries = 0;
		qiuJinFaEntries = 0;
		if (sourceZongMenId <= 0L || targetZongMenId <= 0L || sourceZongMenId == targetZongMenId
			|| !entriesByZongMenId.TryGetValue(sourceZongMenId, out List<XjSectGongFaPavilionEntry> sourceEntries))
		{
			return false;
		}

		List<XjSectGongFaPavilionEntry> snapshot = new List<XjSectGongFaPavilionEntry>(sourceEntries);
		entriesByZongMenId.Remove(sourceZongMenId);
		bool changed = false;
		for (int i = 0; i < snapshot.Count; i++)
		{
			XjSectGongFaPavilionEntry entry = snapshot[i];
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

		foreach (KeyValuePair<long, List<XjSectGongFaPavilionEntry>> zongMenEntry in entriesByZongMenId)
		{
			List<XjSectGongFaPavilionEntry> entries = zongMenEntry.Value;
			if (entries == null)
			{
				continue;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				XjSectGongFaPavilionEntry entry = entries[i];
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
		bool isFuQiMethod = normalizedSource == SourceTypeGongFa
			&& XjFuQiCoreCatalog.IsKnownMethodName(normalizedName);
		string normalizedSourceGongFaName = isFuQiMethod
			? string.Empty
			: string.IsNullOrWhiteSpace(sourceGongFaName) ? string.Empty : sourceGongFaName.Trim();
		string normalizedMappedXianJi;
		if (isFuQiMethod)
		{
			normalizedMappedXianJi = string.Empty;
		}
		else if (!string.IsNullOrWhiteSpace(mappedXianJi))
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

		if (!entriesByZongMenId.TryGetValue(zongMenId, out List<XjSectGongFaPavilionEntry> entries))
		{
			entries = new List<XjSectGongFaPavilionEntry>();
			entriesByZongMenId[zongMenId] = entries;
		}

		var candidate = new XjSectGongFaPavilionEntry(
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

	private static IReadOnlyList<XjSectGongFaPavilionEntry> ReadEntries(long zongMenId, string sourceType)
	{
		if (zongMenId <= 0L || !entriesByZongMenId.TryGetValue(zongMenId, out List<XjSectGongFaPavilionEntry> entries) || entries.Count == 0)
		{
			return System.Array.Empty<XjSectGongFaPavilionEntry>();
		}

		List<XjSectGongFaPavilionEntry> result = new List<XjSectGongFaPavilionEntry>();
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectGongFaPavilionEntry entry = entries[i];
			if (entry.Found && entry.SourceType == sourceType)
			{
				AddOrReplaceDisplayEntry(result, entry);
			}
		}

		return result;
	}

	private static int FindEquivalentEntry(List<XjSectGongFaPavilionEntry> entries, in XjSectGongFaPavilionEntry candidate)
	{
		if (entries == null || entries.Count == 0) return -1;
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectGongFaPavilionEntry existing = entries[i];
			if (!existing.Found || !string.Equals(existing.SourceType, candidate.SourceType, StringComparison.Ordinal)) continue;
			if (AreEquivalent(existing, candidate)) return i;
		}
		return -1;
	}

	private static bool AreEquivalent(in XjSectGongFaPavilionEntry left, in XjSectGongFaPavilionEntry right)
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
		if (XjFuQiCoreCatalog.IsKnownMethodName(left.Name)
			&& XjFuQiCoreCatalog.IsKnownMethodName(right.Name))
		{
			return true;
		}
		string leftMapped = (left.MappedXianJi ?? string.Empty).Trim();
		string rightMapped = (right.MappedXianJi ?? string.Empty).Trim();
		if (leftMapped.Length > 0 && rightMapped.Length > 0) return string.Equals(leftMapped, rightMapped, StringComparison.Ordinal);
		return string.Equals(NormalizeEntryName(left.Name), NormalizeEntryName(right.Name), StringComparison.Ordinal);
	}

	private static string BuildStableEntryKey(in XjSectGongFaPavilionEntry entry)
	{
		string identity = XjFuQiCoreCatalog.IsKnownMethodName(entry.Name)
			? "FuQiMethod"
			: !string.IsNullOrWhiteSpace(entry.MappedXianJi) ? entry.MappedXianJi.Trim() : NormalizeEntryName(entry.Name);
		return entry.ZongMenId + "|" + entry.SourceType + "|" + (entry.DaoTu ?? string.Empty).Trim() + "|" + entry.Grade + "|" + identity;
	}

	private static string NormalizeEntryName(string name)
	{
		return XjFamilyGongFaWarehouseUI.NormalizeGongFaDisplayName(name ?? string.Empty).Trim();
	}

	private static void RebuildEntryKeys()
	{
		entryKeys.Clear();
		foreach (KeyValuePair<long, List<XjSectGongFaPavilionEntry>> pair in entriesByZongMenId)
		{
			List<XjSectGongFaPavilionEntry> entries = pair.Value;
			if (entries == null) continue;
			for (int i = 0; i < entries.Count; i++)
			{
				XjSectGongFaPavilionEntry entry = entries[i];
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

	private static void AddOrReplaceDisplayEntry(List<XjSectGongFaPavilionEntry> entries, in XjSectGongFaPavilionEntry candidate)
	{
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectGongFaPavilionEntry existing = entries[i];
			if (!AreEquivalent(existing, candidate)) continue;

			if (ShouldPreferEntry(candidate, existing))
			{
				entries[i] = candidate;
			}
			return;
		}

		entries.Add(candidate);
	}

	private static bool ShouldPreferEntry(in XjSectGongFaPavilionEntry candidate, in XjSectGongFaPavilionEntry existing)
	{
		if (candidate.Grade != existing.Grade)
		{
			return candidate.Grade > existing.Grade;
		}

		if (XjFuQiCoreCatalog.IsKnownMethodName(candidate.Name)
			&& XjFuQiCoreCatalog.IsKnownMethodName(existing.Name)
			&& XjFuQiCoreCatalog.TryResolveByDaoTu(candidate.DaoTu, out XjFuQiCoreDefinition currentFuQi))
		{
			bool candidateCurrent = string.Equals(candidate.Name, currentFuQi.MethodName, StringComparison.Ordinal);
			bool existingCurrent = string.Equals(existing.Name, currentFuQi.MethodName, StringComparison.Ordinal);
			if (candidateCurrent != existingCurrent) return candidateCurrent;
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
