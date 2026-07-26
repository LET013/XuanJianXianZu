using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjGuoWeiRegistry
{		internal static void Clear()
		{
			if (activeEntriesByGuoWei.Count > 0 || historyEntriesByActorId.Count > 0)
			{
				activeEntriesByGuoWei.Clear();
				historyEntriesByActorId.Clear();
				revision++;
			}
		}

		internal static IReadOnlyList<XjGuoWeiRegistryEntry> ReadAllEntries()
		{
			if (historyEntriesByActorId.Count == 0)
			{
				return Array.Empty<XjGuoWeiRegistryEntry>();
			}
	
			List<XjGuoWeiRegistryEntry> entries = new List<XjGuoWeiRegistryEntry>(historyEntriesByActorId.Count);
			foreach (XjGuoWeiRegistryEntry entry in historyEntriesByActorId.Values)
			{
				entries.Add(NormalizeActiveHistoryEntry(entry));
			}
			entries.Sort((left, right) =>
			{
				int byYear = NormalizeSortYear(left.Year).CompareTo(NormalizeSortYear(right.Year));
				if (byYear != 0)
				{
					return byYear;
				}
	
				int byEndYear = NormalizeSortYear(left.EndedYear).CompareTo(NormalizeSortYear(right.EndedYear));
				if (byEndYear != 0)
				{
					return byEndYear;
				}
	
				return left.ActorId.CompareTo(right.ActorId);
			});
			return entries;
		}
}

