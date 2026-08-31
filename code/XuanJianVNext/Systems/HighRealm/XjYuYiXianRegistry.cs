using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Systems.HighRealm;

internal sealed class XjYuYiXianWorldArchiveData
{
	public List<XjYuYiXianArchiveRecord> Records { get; set; } = new List<XjYuYiXianArchiveRecord>();
}

internal sealed class XjYuYiXianArchiveRecord
{
	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public int Year { get; set; }
}

/// <summary>
/// 郁仪仙与结璘仙分别维护六名在世成员；初成不占位序，修持成熟后可正式证入余位或闰位；入位后退出特殊仙身名册。仅持有稳定 actor id。
/// </summary>
internal static class XjYuYiXianRegistry
{
	internal const int MaxActiveCount = 6;
	private static readonly Dictionary<long, XjYuYiXianArchiveRecord> EntriesByActorId =
		new Dictionary<long, XjYuYiXianArchiveRecord>();

	internal static int ActiveCount
	{
		get { CleanupStaleEntries(); return EntriesByActorId.Count; }
	}

	internal static bool HasCapacityFor(Actor actor)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		CleanupStaleEntries();
		return EntriesByActorId.ContainsKey(actorId) || EntriesByActorId.Count < MaxActiveCount;
	}

	internal static bool TryRegister(Actor actor, int year)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		if (EntriesByActorId.ContainsKey(actorId)) return true;
		CleanupStaleEntries();
		if (EntriesByActorId.Count >= MaxActiveCount) return false;
		EntriesByActorId[actorId] = new XjYuYiXianArchiveRecord
		{
			ActorId = actorId,
			ActorName = actor.getName() ?? string.Empty,
			Year = Math.Max(0, year)
		};
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static void ReconcileLiveActor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor) || !XjXuanJianShenTongSpecials.IsYuYiXian(actor)) return;
		XjActorAccessor.TryGetInt(actor, XuanJianVNext.Data.Rules.XjActorDataKeys.XjYuYiXianYear, out int year);
		TryRegister(actor, year);
	}

	internal static void Release(long actorId)
	{
		if (actorId > 0L && EntriesByActorId.Remove(actorId)) XjWorldArchiveSystem.MarkChanged();
	}

	internal static XjYuYiXianWorldArchiveData ExportState()
	{
		CleanupStaleEntries();
		XjYuYiXianWorldArchiveData result = new XjYuYiXianWorldArchiveData();
		foreach (XjYuYiXianArchiveRecord record in EntriesByActorId.Values)
		{
			result.Records.Add(new XjYuYiXianArchiveRecord
			{
				ActorId = record.ActorId,
				ActorName = record.ActorName,
				Year = record.Year
			});
		}
		result.Records.Sort((left, right) =>
		{
			int byYear = left.Year.CompareTo(right.Year);
			return byYear != 0 ? byYear : left.ActorId.CompareTo(right.ActorId);
		});
		return result;
	}

	internal static void ImportState(XjYuYiXianWorldArchiveData source)
	{
		EntriesByActorId.Clear();
		if (source?.Records == null) return;
		List<XjYuYiXianArchiveRecord> ordered = new List<XjYuYiXianArchiveRecord>(source.Records);
		ordered.Sort((left, right) =>
		{
			int byYear = (left?.Year ?? 0).CompareTo(right?.Year ?? 0);
			return byYear != 0 ? byYear : (left?.ActorId ?? 0L).CompareTo(right?.ActorId ?? 0L);
		});
		for (int i = 0; i < ordered.Count && EntriesByActorId.Count < MaxActiveCount; i++)
		{
			XjYuYiXianArchiveRecord record = ordered[i];
			if (record == null || record.ActorId <= 0L || EntriesByActorId.ContainsKey(record.ActorId)) continue;
			EntriesByActorId[record.ActorId] = new XjYuYiXianArchiveRecord
			{
				ActorId = record.ActorId,
				ActorName = record.ActorName ?? string.Empty,
				Year = Math.Max(0, record.Year)
			};
		}
	}

	internal static void Clear() => EntriesByActorId.Clear();

	private static void CleanupStaleEntries()
	{
		if (EntriesByActorId.Count == 0 || World.world?.units == null) return;
		List<long> stale = null;
		foreach (KeyValuePair<long, XjYuYiXianArchiveRecord> pair in EntriesByActorId)
		{
			if (XjActorRegistry.ResolveKnownOrWorld(pair.Key, out Actor actor)
				&& XjSafeCore.IsAliveActor(actor)
				&& XjXuanJianShenTongSpecials.IsYuYiXian(actor)) continue;
			stale ??= new List<long>();
			stale.Add(pair.Key);
		}
		if (stale == null) return;
		for (int i = 0; i < stale.Count; i++) EntriesByActorId.Remove(stale[i]);
		XjWorldArchiveSystem.MarkChanged();
	}
}
