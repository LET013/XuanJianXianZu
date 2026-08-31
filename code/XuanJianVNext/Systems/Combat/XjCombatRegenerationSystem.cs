using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 有界的修士持续恢复队列。
/// updateStats/getHit can run on WorldBox worker threads. Worker callbacks only
/// publish the latest requested healback value into a ConcurrentDictionary;
/// all Queue/HashSet/Dictionary and Time access is confined to the scheduler lane.
/// </summary>
internal static class XjCombatRegenerationSystem
{
	internal static bool HasPending => !PendingObservations.IsEmpty || Pending.Count > 0;

	private const float MaxElapsedSeconds = 10f;
	private const float MaxHealbackPercentPerSecond = 2f;
	private static readonly ConcurrentDictionary<long, float> PendingObservations = new();
	private static readonly Queue<long> Pending = new Queue<long>();
	private static readonly HashSet<long> Registered = new HashSet<long>();
	private static readonly HashSet<long> Queued = new HashSet<long>();
	private static readonly Dictionary<long, float> LastProcessedAt = new Dictionary<long, float>();
	private static readonly Dictionary<long, float> FractionalCarry = new Dictionary<long, float>();

	internal static void Observe(Actor actor, float healbackPercentPerSecond)
	{
		if (!TryGetActorId(actor, out long actorId)) return;
		PendingObservations[actorId] = healbackPercentPerSecond;
	}

	internal static void Tick(int budget, Func<long, Actor> resolver)
	{
		if (budget <= 0 || resolver == null) return;

		DrainObservations(Math.Max(4, budget), resolver);
		if (Pending.Count == 0) return;

		float now = Time.time;
		int processed = 0;
		int available = Pending.Count;
		while (processed < budget && processed < available)
		{
			if (Pending.Count == 0) return;
			long actorId = Pending.Dequeue();
			Queued.Remove(actorId);
			if (!Registered.Contains(actorId))
			{
				processed++;
				continue;
			}
			processed++;

			Actor actor = resolver(actorId);
			if (!XjSafeCore.IsAliveActor(actor))
			{
				ForgetCore(actorId);
				continue;
			}

			XjCombatHotProfile profile = XjCombatHotPathCache.Get(actor);
			float percentPerSecond = Mathf.Clamp(profile.HealbackPerSecond, 0f, MaxHealbackPercentPerSecond);
			if (percentPerSecond <= 0f)
			{
				ForgetCore(actorId);
				continue;
			}

			if (!Registered.Contains(actorId)) continue;
			float last = LastProcessedAt.TryGetValue(actorId, out float stored) ? stored : now;
			float elapsed = Mathf.Clamp(now - last, 0f, MaxElapsedSeconds);
			LastProcessedAt[actorId] = now;

			if (elapsed > 0f)
			{
				float maxHealth = XjSafeCore.GetMaxHealthSafe(actor);
				float currentHealth = XjSafeCore.GetHealthSafe(actor);
				if (maxHealth > 0f && currentHealth < maxHealth)
				{
					float heal = maxHealth * (percentPerSecond / 100f) * elapsed;
					if (!Registered.Contains(actorId)) continue;
					if (FractionalCarry.TryGetValue(actorId, out float carry)) heal += carry;
					float wholeHeal = Mathf.Floor(heal);
					FractionalCarry[actorId] = Mathf.Max(0f, heal - wholeHeal);
					if (wholeHeal >= 1f) XjSafeCore.HealActorSafe(actor, wholeHeal);
				}
			}

			EnqueueIfNeededCore(actorId);
		}
	}

	internal static void Forget(long actorId)
	{
		if (actorId > 0L) PendingObservations[actorId] = 0f;
	}

	internal static void Clear()
	{
		PendingObservations.Clear();
		Pending.Clear();
		Registered.Clear();
		Queued.Clear();
		LastProcessedAt.Clear();
		FractionalCarry.Clear();
	}

	private static void DrainObservations(int budget, Func<long, Actor> resolver)
	{
		int processed = 0;
		foreach (KeyValuePair<long, float> pair in PendingObservations)
		{
			if (processed >= budget) break;
			if (!PendingObservations.TryRemove(pair.Key, out float healback)) continue;
			processed++;

			Actor actor = resolver(pair.Key);
			if (healback <= 0f || !XjSafeCore.IsAliveActor(actor))
			{
				ForgetCore(pair.Key);
				continue;
			}

			if (Registered.Add(pair.Key))
			{
				LastProcessedAt[pair.Key] = Time.time;
			}
			EnqueueIfNeededCore(pair.Key);
		}
	}

	private static void ForgetCore(long actorId)
	{
		if (actorId <= 0L) return;
		Registered.Remove(actorId);
		Queued.Remove(actorId);
		LastProcessedAt.Remove(actorId);
		FractionalCarry.Remove(actorId);
	}

	private static void EnqueueIfNeededCore(long actorId)
	{
		if (actorId > 0L && Queued.Add(actorId)) Pending.Enqueue(actorId);
	}

	private static bool TryGetActorId(Actor actor, out long actorId)
	{
		actorId = 0L;
		if (actor?.data == null) return false;
		actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L;
	}
}
