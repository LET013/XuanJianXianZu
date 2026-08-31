using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjJieLinXianRegistryEntry
{
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly int Year;

	internal XjJieLinXianRegistryEntry(long actorId, string actorName, int year)
	{
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

/// <summary>
/// 结璘仙初成不占位序；修持成熟后可正式证入余位或闰位，入位后退出结璘仙名册。运行期最多维护六名尚未入位的在世成员。
/// 仅保存稳定 actor id，不扫描世界。
/// </summary>
internal static class XjJieLinXianRegistry
{
	internal const int MaxActiveCount = 6;

	private static readonly Dictionary<long, XjJieLinXianRegistryEntry> EntriesByActorId =
		new Dictionary<long, XjJieLinXianRegistryEntry>();

	internal static int ActiveCount
	{
		get
		{
			CleanupStaleEntries();
			return EntriesByActorId.Count;
		}
	}

	internal static bool HasCapacityFor(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		CleanupStaleEntries();
		return EntriesByActorId.ContainsKey(actorId) || EntriesByActorId.Count < MaxActiveCount;
	}

	internal static bool TryRegister(Actor actor, int year)
	{
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		if (EntriesByActorId.ContainsKey(actorId))
		{
			return true;
		}

		CleanupStaleEntries();
		if (EntriesByActorId.Count >= MaxActiveCount)
		{
			return false;
		}

		EntriesByActorId[actorId] = new XjJieLinXianRegistryEntry(actorId, actor.getName(), year);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static void ReconcileLiveActor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor)
			|| !XjXuanJianShenTongSpecials.IsJieLinXian(actor))
		{
			return;
		}

		int year = 0;
		XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetInt(
			actor,
			XuanJianVNext.Data.Rules.XjActorDataKeys.XjJieLinXianYear,
			out year);
		TryRegister(actor, year);
	}

	internal static void Release(long actorId)
	{
		if (actorId > 0L && EntriesByActorId.Remove(actorId))
		{
			XjWorldArchiveSystem.MarkChanged();
		}
	}

	internal static void ExportArchiveRecords(List<XjWorldArchiveJieLinXianRecord> records)
	{
		if (records == null)
		{
			return;
		}

		CleanupStaleEntries();
		List<XjJieLinXianRegistryEntry> entries = new List<XjJieLinXianRegistryEntry>(EntriesByActorId.Values);
		entries.Sort(CompareEntries);
		for (int i = 0; i < entries.Count && i < MaxActiveCount; i++)
		{
			XjJieLinXianRegistryEntry entry = entries[i];
			records.Add(new XjWorldArchiveJieLinXianRecord
			{
				ActorId = entry.ActorId,
				ActorName = entry.ActorName,
				Year = entry.Year
			});
		}
	}

	internal static void ImportArchiveRecords(IEnumerable<XjWorldArchiveJieLinXianRecord> records)
	{
		EntriesByActorId.Clear();
		if (records == null)
		{
			return;
		}

		List<XjWorldArchiveJieLinXianRecord> ordered = new List<XjWorldArchiveJieLinXianRecord>();
		foreach (XjWorldArchiveJieLinXianRecord record in records)
		{
			if (record != null && record.ActorId > 0L)
			{
				ordered.Add(record);
			}
		}

		ordered.Sort((left, right) =>
		{
			int byYear = left.Year.CompareTo(right.Year);
			return byYear != 0 ? byYear : left.ActorId.CompareTo(right.ActorId);
		});

		for (int i = 0; i < ordered.Count && EntriesByActorId.Count < MaxActiveCount; i++)
		{
			XjWorldArchiveJieLinXianRecord record = ordered[i];
			if (!EntriesByActorId.ContainsKey(record.ActorId))
			{
				EntriesByActorId[record.ActorId] = new XjJieLinXianRegistryEntry(
					record.ActorId,
					record.ActorName,
					record.Year);
			}
		}
	}

	internal static void Clear()
	{
		EntriesByActorId.Clear();
	}

	private static void CleanupStaleEntries()
	{
		if (EntriesByActorId.Count == 0 || World.world?.units == null)
		{
			return;
		}

		List<long> staleIds = null;
		foreach (KeyValuePair<long, XjJieLinXianRegistryEntry> pair in EntriesByActorId)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(pair.Key, out Actor actor)
				|| !XjSafeCore.IsAliveActor(actor)
				|| !XjXuanJianShenTongSpecials.IsJieLinXian(actor))
			{
				staleIds ??= new List<long>();
				staleIds.Add(pair.Key);
			}
		}

		if (staleIds == null)
		{
			return;
		}

		for (int i = 0; i < staleIds.Count; i++)
		{
			EntriesByActorId.Remove(staleIds[i]);
		}
		XjWorldArchiveSystem.MarkChanged();
	}

	private static int CompareEntries(XjJieLinXianRegistryEntry left, XjJieLinXianRegistryEntry right)
	{
		int byYear = left.Year.CompareTo(right.Year);
		return byYear != 0 ? byYear : left.ActorId.CompareTo(right.ActorId);
	}
}
