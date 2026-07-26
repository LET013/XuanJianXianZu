using System;
using System.Collections.Generic;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Core;

/// <summary>
/// Runtime actor references discovered through local actor callbacks.
/// This registry never scans the world.
/// </summary>
internal static class XjActorRegistry
{
	private static readonly Dictionary<long, Actor> ActorsById = new Dictionary<long, Actor>();
	private static readonly Queue<long> CleanupQueue = new Queue<long>();
	private static Actor[] CachedSnapshot = Array.Empty<Actor>();
	private static long RegistryRevision;
	private static long SnapshotRevision = -1L;

	internal static int Count => ActorsById.Count;

	internal static bool Register(Actor actor, out long actorId)
	{
		return Register(actor, out actorId, out _);
	}

	internal static bool Register(Actor actor, out long actorId, out bool isNew)
	{
		actorId = 0L;
		isNew = false;
		if (actor?.data == null)
		{
			return false;
		}

		actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		isNew = !ActorsById.TryGetValue(actorId, out Actor existing);
		if (isNew || !ReferenceEquals(existing, actor))
		{
			ActorsById[actorId] = actor;
			InvalidateSnapshot();
		}
		if (isNew)
		{
			CleanupQueue.Enqueue(actorId);
		}
		return true;
	}

	internal static bool Resolve(long actorId, out Actor actor)
	{
		if (!ActorsById.TryGetValue(actorId, out actor))
		{
			actor = null;
			return false;
		}

		if (actor?.data != null)
		{
			return true;
		}

		// Keep the invalid entry until the bounded cleanup lane removes it and
		// notifies every dependent cache through its removal callback.
		actor = null;
		return false;
	}

	internal static bool ResolveKnownOrWorld(long actorId, out Actor actor)
	{
		if (Resolve(actorId, out actor))
		{
			return true;
		}

		actor = actorId > 0L ? World.world?.units?.get(actorId) : null;
		return actor?.data != null;
	}

	internal static void Unregister(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}
		if (ActorsById.Remove(actorId))
		{
			InvalidateSnapshot();
		}
		XjJinDanDaoSpellRuntime.RemoveActor(actorId);
		XjDomainSkillRuntime.RemoveActor(actorId);
		XjHuoDeFireBirdSummonSystem.RemoveActor(actorId);
		XjSanYinBaoYiXuanLunSystem.RemoveActor(actorId);
	}

	internal static IReadOnlyList<Actor> Snapshot()
	{
		if (SnapshotRevision == RegistryRevision)
		{
			return CachedSnapshot;
		}

		if (ActorsById.Count == 0)
		{
			CachedSnapshot = Array.Empty<Actor>();
			SnapshotRevision = RegistryRevision;
			return CachedSnapshot;
		}

		List<Actor> actors = new List<Actor>(ActorsById.Count);
		foreach (Actor actor in ActorsById.Values)
		{
			if (actor?.data != null)
			{
				actors.Add(actor);
			}
		}

		CachedSnapshot = actors.ToArray();
		SnapshotRevision = RegistryRevision;
		return CachedSnapshot;
	}

	internal static void CleanupInvalid(int maxRemoveCount, Action<long> onRemoved)
	{
		if (maxRemoveCount <= 0 || ActorsById.Count == 0)
		{
			return;
		}

		int checks = Math.Min(Math.Max(maxRemoveCount * 4, 32), CleanupQueue.Count);
		int removed = 0;
		for (int i = 0; i < checks && removed < maxRemoveCount; i++)
		{
			long actorId = CleanupQueue.Dequeue();
			if (ActorsById.TryGetValue(actorId, out Actor actor) && actor?.data != null)
			{
				CleanupQueue.Enqueue(actorId);
				continue;
			}

			if (ActorsById.Remove(actorId))
			{
				removed++;
				InvalidateSnapshot();
				XjJinDanDaoSpellRuntime.RemoveActor(actorId);
				XjDomainSkillRuntime.RemoveActor(actorId);
				XjHuoDeFireBirdSummonSystem.RemoveActor(actorId);
				XjSanYinBaoYiXuanLunSystem.RemoveActor(actorId);
				onRemoved?.Invoke(actorId);
			}
		}
	}

	internal static void ReleaseSnapshotCache()
	{
		CachedSnapshot = Array.Empty<Actor>();
		SnapshotRevision = -1L;
	}

	internal static void Clear()
	{
		ActorsById.Clear();
		CleanupQueue.Clear();
		CachedSnapshot = Array.Empty<Actor>();
		RegistryRevision = 0L;
		SnapshotRevision = -1L;
	}

	private static void InvalidateSnapshot()
	{
		RegistryRevision++;
	}
}
