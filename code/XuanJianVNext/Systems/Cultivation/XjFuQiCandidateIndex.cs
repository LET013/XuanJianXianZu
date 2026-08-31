using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 服气候选人独立索引。后续剑气、参悟、神妙与求位只遍历此集合，
/// 不把服气条件继续塞进依赖真元、采气和功法的紫金候选索引。
/// </summary>
internal static class XjFuQiCandidateIndex
{
	private static readonly HashSet<long> ActorIds = new HashSet<long>();
	private static long[] Snapshot = Array.Empty<long>();
	private static bool SnapshotDirty = true;

	internal static int Count => ActorIds.Count;

	internal static void Observe(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		bool shouldContain = actor.isAlive()
			&& XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)
			&& XjCultivationPathRules.IsFuQiYangXing(actor);
		if (shouldContain ? ActorIds.Add(actorId) : ActorIds.Remove(actorId))
		{
			SnapshotDirty = true;
		}
	}

	internal static IReadOnlyList<long> GetActorIds()
	{
		if (SnapshotDirty)
		{
			Snapshot = new long[ActorIds.Count];
			ActorIds.CopyTo(Snapshot);
			SnapshotDirty = false;
		}
		return Snapshot;
	}

	internal static bool Contains(long actorId) => actorId > 0L && ActorIds.Contains(actorId);

	internal static void Remove(long actorId)
	{
		if (actorId > 0L && ActorIds.Remove(actorId))
		{
			SnapshotDirty = true;
		}
	}

	internal static void Clear()
	{
		ActorIds.Clear();
		Snapshot = Array.Empty<long>();
		SnapshotDirty = false;
	}
}
