using System;
using System.Collections.Generic;
using XuanJianVNext.Architecture.Presentation;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
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

		XjStaleActorIdEviction.Track(actorId);
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

		if (IsCurrentIdentity(actorId, actor))
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
			XjStaleActorIdEviction.Track(actorId);
			return true;
		}

		actor = actorId > 0L ? World.world?.units?.get(actorId) : null;
		if (actor?.data == null || ((BaseSystemData)actor.data).id != actorId)
		{
			actor = null;
			return false;
		}
		XjStaleActorIdEviction.Track(actorId);
		return true;
	}

	/// <summary>
	/// Strict native-world liveness probe for bounded stale-id maintenance. Unlike
	/// Resolve(), this intentionally does not trust a retained Actor reference: an
	/// id is live only when the current WorldBox unit index resolves the same id and
	/// the resolved actor is alive. No world scan and no registry mutation occur.
	/// </summary>
	internal static bool TryResolveLiveWorldActor(long actorId, out Actor actor)
	{
		actor = null;
		if (actorId <= 0L || World.world?.units == null)
		{
			return false;
		}

		try
		{
			actor = World.world.units.get(actorId);
			return actor?.data != null
				&& ((BaseSystemData)actor.data).id == actorId
				&& actor.isAlive();
		}
		catch
		{
			actor = null;
			return false;
		}
	}

	internal static void Unregister(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}
		// Explicit native death/removal makes the id non-stale by definition. Drop
		// tracker membership and its ring node immediately in O(1).
		XjStaleActorIdEviction.Forget(actorId);
		if (ActorsById.Remove(actorId))
		{
			InvalidateSnapshot();
		}
		XjJinDanDaoSpellRuntime.RemoveActor(actorId);
		XjAttributedFreezeRegistry.RemoveActor(actorId);
		XjDomainSkillRuntime.RemoveActor(actorId);
		XjHuoDeFireBirdSummonSystem.RemoveActor(actorId);
		XjSanYinBaoYiXuanLunSystem.RemoveActor(actorId);
		InvalidateActorProjection(actorId);
		if (!XuanJianVNext.Systems.Runtime.XjWorldBootstrapLane.HasPending)
		{
			XjFuQiSwordWorldState.OnActorUnavailable(actorId, XjYearTracker.CurrentYear);
		}
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
		foreach (KeyValuePair<long, Actor> pair in ActorsById)
		{
			if (IsCurrentIdentity(pair.Key, pair.Value))
			{
				actors.Add(pair.Value);
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
			if (ActorsById.TryGetValue(actorId, out Actor actor) && IsCurrentIdentity(actorId, actor))
			{
				CleanupQueue.Enqueue(actorId);
				continue;
			}

			if (ActorsById.Remove(actorId))
			{
				removed++;
				InvalidateSnapshot();
				XjJinDanDaoSpellRuntime.RemoveActor(actorId);
				XjAttributedFreezeRegistry.RemoveActor(actorId);
				XjDomainSkillRuntime.RemoveActor(actorId);
				XjHuoDeFireBirdSummonSystem.RemoveActor(actorId);
				XjSanYinBaoYiXuanLunSystem.RemoveActor(actorId);
				InvalidateActorProjection(actorId);
				if (!XuanJianVNext.Systems.Runtime.XjWorldBootstrapLane.HasPending)
				{
					XjFuQiSwordWorldState.OnActorUnavailable(actorId, XjYearTracker.CurrentYear);
				}
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
		XjStaleActorIdEviction.Clear();
	}


	private static bool IsCurrentIdentity(long actorId, Actor actor)
	{
		if (actorId <= 0L || actor?.data == null) return false;
		return ((BaseSystemData)actor.data).id == actorId;
	}

	private static void InvalidateSnapshot()
	{
		RegistryRevision++;
	}

	private static void InvalidateActorProjection(long actorId)
	{
		if (actorId <= 0L) return;
		XjActorStateRevisionStore.RemoveActor(actorId);
		XjPresentationHooks.RemoveActorInfoProjection(actorId);
	}
}
