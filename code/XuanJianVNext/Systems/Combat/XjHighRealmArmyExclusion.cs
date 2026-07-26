using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// Keeps ZiFu/JinDan actors out of vanilla kingdom armies. Native wars are still
/// allowed to run, but high-realm cultivators should enter combat through
/// explicit XuanJian sect-war systems instead of WorldBox conscription.
/// </summary>
internal static class XjHighRealmArmyExclusion
{
	private const int TickBudget = 64;
	private const int MinimumExcludedRealmTier = XjRealmSuppression.TierZiFu;
	private static readonly Queue<long> PendingActorIds = new Queue<long>();
	private static readonly HashSet<long> PendingActorIdSet = new HashSet<long>();
	private static int _sectWarArmyScopeDepth;

	internal static bool IsSectWarArmyScope => _sectWarArmyScopeDepth > 0;
	internal static bool HasPending => PendingActorIds.Count > 0;

	internal static IDisposable EnterSectWarArmyScope()
	{
		_sectWarArmyScopeDepth++;
		return SectWarArmyScope.Instance;
	}

	internal static bool ShouldBlockNativeArmyAssignment(Actor actor)
	{
		if (IsSectWarArmyScope || actor?.data == null || !actor.isAlive())
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)
			&& realmTier >= MinimumExcludedRealmTier;
	}

	internal static void DetachNowIfHighRealm(Actor actor)
	{
		if (ShouldBlockNativeArmyAssignment(actor))
		{
			ClearNativeArmyState(actor);
		}
	}

	internal static void FlushBeforeSave()
	{
		IReadOnlyList<long> actorIds = XjCultivatorCache.GetZiFuOrHigherIds();
		for (int i = 0; i < actorIds.Count; i++)
		{
			Enqueue(actorIds[i]);
		}
		TickQueue(Math.Max(TickBudget, PendingActorIds.Count));
	}

	internal static void EnqueueIfHighRealm(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)
			|| realmTier < MinimumExcludedRealmTier)
		{
			return;
		}

		Enqueue(actorId);
	}

	internal static void ScheduleAnnualCleanup()
	{
		IReadOnlyList<long> actorIds = XjCultivatorCache.GetZiFuOrHigherIds();
		for (int i = 0; i < actorIds.Count; i++)
		{
			Enqueue(actorIds[i]);
		}
	}

	internal static void TickQueue(int budget)
	{
		if (budget <= 0)
		{
			return;
		}

		int processed = 0;
		while (PendingActorIds.Count > 0 && processed < budget)
		{
			processed++;
			long actorId = PendingActorIds.Dequeue();
			PendingActorIdSet.Remove(actorId);
			ProcessActor(actorId);
		}
	}

	internal static void Clear()
	{
		PendingActorIds.Clear();
		PendingActorIdSet.Clear();
		_sectWarArmyScopeDepth = 0;
	}

	private static void Enqueue(long actorId)
	{
		if (actorId <= 0L || !PendingActorIdSet.Add(actorId))
		{
			return;
		}

		PendingActorIds.Enqueue(actorId);
	}

	private static void ProcessActor(long actorId)
	{
		if (actorId <= 0L
			|| !XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)
			|| realmTier < MinimumExcludedRealmTier
			|| !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| actor?.data == null
			|| !actor.isAlive())
		{
			return;
		}

		ClearNativeArmyState(actor);
	}

	private static void ClearNativeArmyState(Actor actor)
	{
		// 高境脱离原生军队必须同时登记 Actor、Army 与 City 三端。
		// 禁止在此处再手写 removeFromArmy，以免与保存前清理形成两套规则。
		XjNativeArmySaveGuard.DetachActorFromNativeArmy(actor, "high_realm_native_army_exclusion");
	}

	private sealed class SectWarArmyScope : IDisposable
	{
		internal static readonly SectWarArmyScope Instance = new SectWarArmyScope();

		public void Dispose()
		{
			if (_sectWarArmyScopeDepth > 0)
			{
				_sectWarArmyScopeDepth--;
			}
		}
	}
}
