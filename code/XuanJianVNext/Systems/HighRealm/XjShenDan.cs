using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Death;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjShenDanState
{
	internal static XjShenDanState Empty { get; } = new XjShenDanState(false, string.Empty, 0L, string.Empty, 0);

	internal readonly bool Found;
	internal readonly string GuoWei;
	internal readonly long AnchorActorId;
	internal readonly string AnchorName;
	internal readonly int Year;

	internal XjShenDanState(bool found, string guoWei, long anchorActorId, string anchorName, int year)
	{
		Found = found;
		GuoWei = guoWei ?? string.Empty;
		AnchorActorId = anchorActorId < 0L ? 0L : anchorActorId;
		AnchorName = anchorName ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

internal static class XjShenDanAccessor
{
	internal static XjShenDanState BuildState(Actor actor)
	{
		if (actor?.data == null)
		{
			return XjShenDanState.Empty;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenDanGuoWei, out string guoWei);
		XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjShenDanAnchorActorId, out long anchorActorId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenDanAnchorName, out string anchorName);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanYear, out int year);
		bool found = string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			&& anchorActorId > 0L
			&& year > 0;
		return new XjShenDanState(found, guoWei, anchorActorId, anchorName, year);
	}

	internal static void WriteSuccess(Actor actor, string guoWei, long anchorActorId, string anchorName, int year)
	{
		if (actor?.data == null || anchorActorId <= 0L)
		{
			return;
		}

		int safeYear = Math.Max(1, year);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanGuoWei, string.Empty);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjShenDanAnchorActorId, anchorActorId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanAnchorName, (anchorName ?? string.Empty).Trim());
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanYear, safeYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, 0);
	}

	internal static void ClearSuccess(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L)
		{
			XjShenDanRegistry.Forget(actorId);
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanGuoWei, string.Empty);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjShenDanAnchorActorId, 0L);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanAnchorName, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathPending, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathHandled, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathYear, 0);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjShenDanDeathAnchorActorId, 0L);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanDeathAnchorName, string.Empty);
	}
}

internal static class XjShenDanRegistry
{
	private static readonly Dictionary<long, HashSet<long>> DependentsByAnchor = new Dictionary<long, HashSet<long>>();
	private static readonly Dictionary<long, long> AnchorByDependent = new Dictionary<long, long>();

	internal static bool CanAttachToAnchor(
		in XjGuoWeiRegistryEntry anchor,
		long dependentActorId,
		out int currentCount,
		out int capacity)
	{
		currentCount = 0;
		capacity = ResolveCapacity(anchor.GuoWei);
		if (!anchor.Found || !anchor.IsActive || anchor.ActorId <= 0L || dependentActorId <= 0L || anchor.ActorId == dependentActorId)
		{
			return false;
		}
		if (capacity <= 0)
		{
			return false;
		}
		if (!XjScheduler.ResolveActor(anchor.ActorId, out Actor anchorActor)
			|| !XjSafeCore.IsAliveActor(anchorActor)
			|| XjXuanJianShenTongSpecials.IsJieLinXian(anchorActor))
		{
			return false;
		}

		currentCount = CountDependents(anchor.ActorId, dependentActorId);
		return currentCount < capacity;
	}

	internal static bool IsAnchorAtCapacity(long anchorActorId, string guoWei)
	{
		int capacity = ResolveCapacity(guoWei);
		return anchorActorId > 0L && capacity > 0 && CountDependents(anchorActorId, 0L) >= capacity;
	}

	internal static void Observe(Actor actor)
	{
		XjShenDanState state = XjShenDanAccessor.BuildState(actor);
		if (!state.Found || actor?.data == null)
		{
			return;
		}

		Register(((BaseSystemData)actor.data).id, state.AnchorActorId);
	}

	internal static void Register(long dependentActorId, long anchorActorId)
	{
		if (dependentActorId <= 0L || anchorActorId <= 0L || dependentActorId == anchorActorId)
		{
			return;
		}

		if (AnchorByDependent.TryGetValue(dependentActorId, out long oldAnchor) && oldAnchor != anchorActorId)
		{
			RemoveFromAnchor(oldAnchor, dependentActorId);
		}
		AnchorByDependent[dependentActorId] = anchorActorId;
		if (!DependentsByAnchor.TryGetValue(anchorActorId, out HashSet<long> set))
		{
			set = new HashSet<long>();
			DependentsByAnchor[anchorActorId] = set;
		}
		set.Add(dependentActorId);
	}

	private static int CountDependents(long anchorActorId, long ignoreDependentActorId)
	{
		if (anchorActorId <= 0L || !DependentsByAnchor.TryGetValue(anchorActorId, out HashSet<long> set))
		{
			return 0;
		}

		int count = 0;
		foreach (long dependentId in set)
		{
			if (dependentId > 0L && dependentId != ignoreDependentActorId)
			{
				count++;
			}
		}
		return count;
	}

	private static int ResolveCapacity(string guoWei)
	{
		string type = XjGuoWeiRegistry.ResolveTypeFromName(guoWei);
		if (string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) return 4;
		if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) return 3;
		if (string.Equals(type, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)) return 2;
		return 0;
	}

	internal static void Forget(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		if (AnchorByDependent.TryGetValue(actorId, out long anchorActorId))
		{
			AnchorByDependent.Remove(actorId);
			RemoveFromAnchor(anchorActorId, actorId);
		}
		DependentsByAnchor.Remove(actorId);
	}

	internal static void OnActorDied(Actor actor, int deathYear)
	{
		if (actor?.data == null)
		{
			return;
		}

		long deadActorId = ((BaseSystemData)actor.data).id;
		if (deadActorId <= 0L)
		{
			return;
		}

		if (DependentsByAnchor.TryGetValue(deadActorId, out HashSet<long> dependents))
		{
			long[] ids = new long[dependents.Count];
			dependents.CopyTo(ids);
			for (int i = 0; i < ids.Length; i++)
			{
				if (XjScheduler.ResolveActor(ids[i], out Actor dependent) && XjSafeCore.IsAliveActor(dependent))
				{
					MarkDependentDeath(dependent, deadActorId, actor.getName(), deathYear);
				}
			}
		}

		Forget(deadActorId);
	}

	private static void MarkDependentDeath(Actor actor, long anchorActorId, string anchorName, int deathYear)
	{
		int safeYear = Math.Max(1, deathYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathPending, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathHandled, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathYear, safeYear);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjShenDanDeathAnchorActorId, anchorActorId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanDeathAnchorName, anchorName ?? string.Empty);
		XjShenDanDeathLane.EnqueueActor(actor);
	}

	private static void RemoveFromAnchor(long anchorActorId, long dependentActorId)
	{
		if (DependentsByAnchor.TryGetValue(anchorActorId, out HashSet<long> set))
		{
			set.Remove(dependentActorId);
			if (set.Count == 0)
			{
				DependentsByAnchor.Remove(anchorActorId);
			}
		}
	}
}

internal static class XjShenDanDeathLane
{
	internal static bool HasPending => _queue.Count > 0;
	private static readonly XjDeathLaneQueue _queue = new XjDeathLaneQueue(true);

	internal static void EnqueueActor(Actor actor)
	{
		_queue.TryEnqueue(actor);
	}

	internal static void EnqueueIfPending(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return;
		}

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanDeathPending, out int pending)
			|| pending != 1)
		{
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanDeathHandled, out int handled)
			&& handled == 1)
		{
			return;
		}

		_queue.TryEnqueue(actor);
	}

	internal static void Tick(int budget, XjDeathActorResolver resolver)
	{
		if (budget <= 0 || resolver == null || _queue.Count == 0)
		{
			return;
		}

		int processed = 0;
		while (processed < budget && _queue.Count > 0)
		{
			long actorId = _queue.TryDequeue();
			processed++;
			if (resolver(actorId, out Actor actor) && actor != null)
			{
				XjShenDanDeathExecutor.TryExecute(actor);
			}
		}
	}

	internal static void Clear()
	{
		_queue.Clear();
	}
}

internal static class XjShenDanDeathExecutor
{
	private const int ShenDanAttackType = 5;

	internal static void TryExecute(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor)
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanDeathPending, out int pending)
			|| pending != 1)
		{
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanDeathHandled, out int handled)
			&& handled == 1)
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenDanDeathAnchorName, out string anchorName);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanDeathYear, out int deathYear);
		string safeAnchorName = string.IsNullOrWhiteSpace(anchorName) ? "所挂靠金丹" : anchorName.Trim();
		string actorName = actor.getName() ?? "神丹修士";
		string announcement = actorName + " 所挂靠金丹 " + safeAnchorName + " 已殁，神丹果位无所寄托，随之魂归天际。";
		try
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathHandled, 1);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, "ShenDanAnchorDeath");
			bool died = XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)ShenDanAttackType, false);
			if (died)
			{
				XjBroadcastSystem.BroadcastBLevelActorEvent(actor, announcement, announcement, XjEventIconCatalog.JinDanFail);
			}
			else if (XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathHandled, 0);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
			}
			_ = deathYear;
		}
		catch
		{
			if (XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathHandled, 0);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
			}
		}
	}
}
