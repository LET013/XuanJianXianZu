using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Systems.Family;

internal readonly struct XjFamilySurnameStateSnapshot
{
	internal readonly bool Found;
	internal readonly long FamilyStableId;
	internal readonly string CurrentSurname;
	internal readonly string OriginalSurname;
	internal readonly string PreviousSurname;
	internal readonly int EstablishedYear;
	internal readonly int LastChangedYear;
	internal readonly int ChangeCount;
	internal readonly long SourceFamilyId;

	internal bool HasFormalChange => ChangeCount > 0;

	internal XjFamilySurnameStateSnapshot(
		bool found,
		long familyStableId,
		string currentSurname,
		string originalSurname,
		string previousSurname,
		int establishedYear,
		int lastChangedYear,
		int changeCount,
		long sourceFamilyId)
	{
		Found = found;
		FamilyStableId = familyStableId;
		CurrentSurname = currentSurname ?? string.Empty;
		OriginalSurname = originalSurname ?? string.Empty;
		PreviousSurname = previousSurname ?? string.Empty;
		EstablishedYear = Math.Max(0, establishedYear);
		LastChangedYear = Math.Max(0, lastChangedYear);
		ChangeCount = Math.Max(0, changeCount);
		SourceFamilyId = Math.Max(0L, sourceFamilyId);
	}
}

/// <summary>
/// 家族稳定编号对应的持久化姓氏权威。
/// FamilyStableId 永远是血脉主键；姓氏只是可变的历史身份，不参与家族判定。
/// </summary>
internal static class XjFamilySurnameRegistry
{
	private const int MaxChangesPerFamily = 16;
	private static readonly Dictionary<long, XjFamilySurnameArchiveRecord> ByFamily =
		new Dictionary<long, XjFamilySurnameArchiveRecord>();

	internal static bool TryGetCurrentSurname(long familyStableId, out string surname)
	{
		surname = string.Empty;
		if (familyStableId <= 0L
			|| !ByFamily.TryGetValue(familyStableId, out XjFamilySurnameArchiveRecord record)
			|| record == null
			|| string.IsNullOrWhiteSpace(record.CurrentSurname))
		{
			return false;
		}
		surname = record.CurrentSurname.Trim();
		return surname.Length > 0;
	}

	internal static bool TryGetState(long familyStableId, out XjFamilySurnameStateSnapshot snapshot)
	{
		snapshot = default;
		if (familyStableId <= 0L
			|| !ByFamily.TryGetValue(familyStableId, out XjFamilySurnameArchiveRecord record)
			|| record == null)
		{
			return false;
		}
		string previous = string.Empty;
		int count = record.Changes?.Count ?? 0;
		if (count > 0)
		{
			XjFamilySurnameChangeArchiveRecord change = record.Changes[count - 1];
			previous = change?.PreviousSurname ?? string.Empty;
		}
		snapshot = new XjFamilySurnameStateSnapshot(
			true,
			record.FamilyStableId,
			record.CurrentSurname,
			record.OriginalSurname,
			previous,
			record.EstablishedYear,
			record.LastChangedYear,
			count,
			record.SourceFamilyId);
		return true;
	}

	internal static string EnsureEstablished(long familyStableId, string surname, int year, long sourceFamilyId = 0L)
	{
		string normalized = NormalizeSurname(surname);
		if (familyStableId <= 0L || normalized.Length == 0) return string.Empty;
		if (ByFamily.TryGetValue(familyStableId, out XjFamilySurnameArchiveRecord existing) && existing != null)
		{
			if (!string.IsNullOrWhiteSpace(existing.CurrentSurname)) return existing.CurrentSurname.Trim();
			existing.CurrentSurname = normalized;
			if (string.IsNullOrWhiteSpace(existing.OriginalSurname)) existing.OriginalSurname = normalized;
			if (existing.EstablishedYear <= 0) existing.EstablishedYear = Math.Max(0, year);
			if (existing.SourceFamilyId <= 0L) existing.SourceFamilyId = Math.Max(0L, sourceFamilyId);
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
			return normalized;
		}

		ByFamily[familyStableId] = new XjFamilySurnameArchiveRecord
		{
			FamilyStableId = familyStableId,
			CurrentSurname = normalized,
			OriginalSurname = normalized,
			EstablishedYear = Math.Max(0, year),
			LastChangedYear = 0,
			SourceFamilyId = Math.Max(0L, sourceFamilyId),
			Changes = new List<XjFamilySurnameChangeArchiveRecord>()
		};
		XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
		return normalized;
	}

	internal static bool TryChange(
		long familyStableId,
		string nextSurname,
		int year,
		string source,
		long initiatorActorId,
		string initiatorActorName,
		out string previousSurname,
		out string normalizedSurname)
	{
		previousSurname = string.Empty;
		normalizedSurname = NormalizeSurname(nextSurname);
		if (familyStableId <= 0L || normalizedSurname.Length == 0) return false;
		if (!ByFamily.TryGetValue(familyStableId, out XjFamilySurnameArchiveRecord record) || record == null)
		{
			return false;
		}
		previousSurname = NormalizeSurname(record.CurrentSurname);
		if (previousSurname.Length == 0 || string.Equals(previousSurname, normalizedSurname, StringComparison.Ordinal))
		{
			return false;
		}

		record.Changes ??= new List<XjFamilySurnameChangeArchiveRecord>();
		record.Changes.Add(new XjFamilySurnameChangeArchiveRecord
		{
			PreviousSurname = previousSurname,
			NewSurname = normalizedSurname,
			Year = Math.Max(0, year),
			Source = (source ?? string.Empty).Trim(),
			InitiatorActorId = Math.Max(0L, initiatorActorId),
			InitiatorActorName = (initiatorActorName ?? string.Empty).Trim()
		});
		while (record.Changes.Count > MaxChangesPerFamily)
		{
			record.Changes.RemoveAt(0);
		}
		record.CurrentSurname = normalizedSurname;
		record.LastChangedYear = Math.Max(0, year);
		XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
		return true;
	}

	internal static bool TryRepairAutomaticSurname(long familyStableId, string correctedSurname)
	{
		string corrected = NormalizeSurname(correctedSurname);
		if (familyStableId <= 0L || corrected.Length == 0
			|| !ByFamily.TryGetValue(familyStableId, out XjFamilySurnameArchiveRecord record)
			|| record == null
			|| (record.Changes?.Count ?? 0) > 0)
		{
			return false;
		}

		string current = NormalizeSurname(record.CurrentSurname);
		if (string.Equals(current, corrected, StringComparison.Ordinal)) return false;
		record.CurrentSurname = corrected;
		record.OriginalSurname = corrected;
		XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
		return true;
	}

	internal static XjFamilySurnameWorldArchiveData ExportState()
	{
		XjFamilySurnameWorldArchiveData state = new XjFamilySurnameWorldArchiveData();
		List<long> familyIds = new List<long>(ByFamily.Keys);
		familyIds.Sort();
		for (int i = 0; i < familyIds.Count; i++)
		{
			XjFamilySurnameArchiveRecord record = ByFamily[familyIds[i]];
			if (record == null || record.FamilyStableId <= 0L || string.IsNullOrWhiteSpace(record.CurrentSurname)) continue;
			// 普通从未改姓的主家可由始祖/账本无损重建，无需把全世界所有家族都写入模块文档。
			// 只有真实改姓史，或已经从主家分出的独立支房，必须持久化其姓氏状态。
			if ((record.Changes?.Count ?? 0) <= 0 && record.SourceFamilyId <= 0L) continue;
			state.Families.Add(Clone(record));
		}
		return state;
	}

	internal static void ImportState(XjFamilySurnameWorldArchiveData state)
	{
		ByFamily.Clear();
		if (state?.Families == null) return;
		for (int i = 0; i < state.Families.Count; i++)
		{
			XjFamilySurnameArchiveRecord raw = state.Families[i];
			if (raw == null || raw.FamilyStableId <= 0L) continue;
			string current = NormalizeSurname(raw.CurrentSurname);
			if (current.Length == 0) continue;
			XjFamilySurnameArchiveRecord copy = Clone(raw);
			copy.CurrentSurname = current;
			copy.OriginalSurname = NormalizeSurname(copy.OriginalSurname);
			if (copy.OriginalSurname.Length == 0) copy.OriginalSurname = current;
			copy.EstablishedYear = Math.Max(0, copy.EstablishedYear);
			copy.LastChangedYear = Math.Max(0, copy.LastChangedYear);
			copy.SourceFamilyId = Math.Max(0L, copy.SourceFamilyId);
			copy.Changes ??= new List<XjFamilySurnameChangeArchiveRecord>();
			while (copy.Changes.Count > MaxChangesPerFamily) copy.Changes.RemoveAt(0);
			ByFamily[copy.FamilyStableId] = copy;
		}
	}

	internal static void Clear()
	{
		ByFamily.Clear();
	}

	internal static string NormalizeSurname(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.EndsWith("氏", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1).Trim();
		text = text.Replace(" ", string.Empty).Replace("\t", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
		return text;
	}

	internal static bool IsValidRequestedSurname(string value, out string normalized)
	{
		normalized = NormalizeSurname(value);
		if (normalized.Length < 1 || normalized.Length > 2) return false;
		for (int i = 0; i < normalized.Length; i++)
		{
			char c = normalized[i];
			bool cjk = c >= '\u3400' && c <= '\u4DBF' || c >= '\u4E00' && c <= '\u9FFF';
			if (!cjk) return false;
		}
		return true;
	}

	private static XjFamilySurnameArchiveRecord Clone(XjFamilySurnameArchiveRecord source)
	{
		XjFamilySurnameArchiveRecord copy = new XjFamilySurnameArchiveRecord
		{
			FamilyStableId = source.FamilyStableId,
			CurrentSurname = source.CurrentSurname ?? string.Empty,
			OriginalSurname = source.OriginalSurname ?? string.Empty,
			EstablishedYear = source.EstablishedYear,
			LastChangedYear = source.LastChangedYear,
			SourceFamilyId = source.SourceFamilyId,
			Changes = new List<XjFamilySurnameChangeArchiveRecord>()
		};
		if (source.Changes != null)
		{
			for (int i = 0; i < source.Changes.Count; i++)
			{
				XjFamilySurnameChangeArchiveRecord item = source.Changes[i];
				if (item == null) continue;
				copy.Changes.Add(new XjFamilySurnameChangeArchiveRecord
				{
					PreviousSurname = item.PreviousSurname ?? string.Empty,
					NewSurname = item.NewSurname ?? string.Empty,
					Year = item.Year,
					Source = item.Source ?? string.Empty,
					InitiatorActorId = item.InitiatorActorId,
					InitiatorActorName = item.InitiatorActorName ?? string.Empty
				});
			}
		}
		return copy;
	}
}
