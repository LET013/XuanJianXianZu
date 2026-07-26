using System;
using System.Collections.Generic;
using XuanJianVNext.Data.CaiQi;

namespace XuanJianVNext.Systems.Cultivation;

internal readonly struct XjDaoTuVisibleTraitEntry
{
	internal readonly string TraitId;
	internal readonly string DaoTuGroup;
	internal readonly string DisplayName;

	internal XjDaoTuVisibleTraitEntry(string traitId, string daoTuGroup, string displayName)
	{
		TraitId = traitId ?? string.Empty;
		DaoTuGroup = daoTuGroup ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
	}
}

internal static class XjDaoTuVisibleTraitCatalog
{
	private static readonly XjDaoTuVisibleTraitEntry[] entries = BuildEntries();
	private static readonly string[] allTraitIds = BuildAllTraitIds(entries);

	internal static IReadOnlyList<XjDaoTuVisibleTraitEntry> Entries => entries;

	internal static string[] AllTraitIds => allTraitIds;

	internal static bool TryResolveTraitId(string daoTu, out string traitId)
	{
		traitId = string.Empty;
		string normalized = Normalize(daoTu);
		if (string.IsNullOrEmpty(normalized))
		{
			return false;
		}

		for (int i = 0; i < entries.Length; i++)
		{
			XjDaoTuVisibleTraitEntry entry = entries[i];
			if (string.Equals(Normalize(entry.DisplayName), normalized, StringComparison.Ordinal)
				|| string.Equals(Normalize(entry.TraitId), normalized, StringComparison.Ordinal))
			{
				traitId = entry.TraitId;
				return !string.IsNullOrWhiteSpace(traitId);
			}
		}

		return false;
	}

	internal static bool TryResolveDaoTuByTraitId(string traitId, out string daoTu)
	{
		daoTu = string.Empty;
		string normalized = Normalize(traitId);
		if (string.IsNullOrEmpty(normalized))
		{
			return false;
		}

		for (int i = 0; i < entries.Length; i++)
		{
			XjDaoTuVisibleTraitEntry entry = entries[i];
			if (string.Equals(Normalize(entry.TraitId), normalized, StringComparison.Ordinal))
			{
				daoTu = entry.DisplayName;
				return !string.IsNullOrWhiteSpace(daoTu);
			}
		}

		return false;
	}

	private static XjDaoTuVisibleTraitEntry[] BuildEntries()
	{
		List<XjDaoTuVisibleTraitEntry> result = new List<XjDaoTuVisibleTraitEntry>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < XjCaiQiCatalog.Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = XjCaiQiCatalog.Entries[i];
			if (string.IsNullOrWhiteSpace(entry.OldTraitId) || !seen.Add(entry.OldTraitId))
			{
				continue;
			}

			result.Add(new XjDaoTuVisibleTraitEntry(entry.OldTraitId, ResolveDaoTuGroup(entry.OldTraitId), entry.DisplayName));
		}
		if (seen.Add("QingXuan"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("QingXuan", "SuDe", "青宣"));
		}

		return result.ToArray();
	}

	private static string[] BuildAllTraitIds(XjDaoTuVisibleTraitEntry[] source)
	{
		if (source == null || source.Length == 0)
		{
			return Array.Empty<string>();
		}

		string[] result = new string[source.Length];
		for (int i = 0; i < source.Length; i++)
		{
			result[i] = source[i].TraitId;
		}

		return result;
	}

	private static string ResolveDaoTuGroup(string traitId)
	{
		string id = traitId ?? string.Empty;
		int index = 0;
		while (index < id.Length && !char.IsDigit(id[index]))
		{
			index++;
		}

		return index <= 0 ? id : id.Substring(0, index);
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}
}
