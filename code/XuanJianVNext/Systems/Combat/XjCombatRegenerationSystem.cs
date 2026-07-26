using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 有界的修士持续恢复队列。
/// 回血词条只在 stats/cache 刷新时登记，运行期按预算处理，
/// 不在帧循环中扫描全体角色，也不在攻击热路径中计算周期恢复。
/// </summary>
internal static class XjCombatRegenerationSystem
{
	internal static bool HasPending
	{
		get
		{
			lock (Sync)
			{
				return Pending.Count > 0;
			}
		}
	}

	private const float MaxElapsedSeconds = 10f;
	// 合法来源叠加后通常低于 1.5%/秒。2% 是旧档脏值的最终保险阈值，
	// 防止 15/20 这类历史百分比单位错误再次制造“打到固定血线就瞬间回满”。
	private const float MaxHealbackPercentPerSecond = 2f;
	private static readonly Queue<long> Pending = new Queue<long>();
	private static readonly HashSet<long> Registered = new HashSet<long>();
	private static readonly HashSet<long> Queued = new HashSet<long>();
	private static readonly Dictionary<long, float> LastProcessedAt = new Dictionary<long, float>();
	private static readonly Dictionary<long, float> FractionalCarry = new Dictionary<long, float>();
	private static readonly object Sync = new object();

	internal static void Observe(Actor actor, float healbackPercentPerSecond)
	{
		if (!TryGetActorId(actor, out long actorId))
		{
			return;
		}

		if (healbackPercentPerSecond <= 0f || !XjSafeCore.IsAliveActor(actor))
		{
			Forget(actorId);
			return;
		}

		lock (Sync)
		{
			if (Registered.Add(actorId))
			{
				EnqueueIfNeededCore(actorId);
				LastProcessedAt[actorId] = Time.time;
			}
		}
	}

	internal static void Tick(int budget, Func<long, Actor> resolver)
	{
		if (budget <= 0 || resolver == null)
		{
			return;
		}

		float now = Time.time;
		int processed = 0;
		int available;
		lock (Sync)
		{
			available = Pending.Count;
		}
		while (processed < budget && processed < available)
		{
			long actorId;
			lock (Sync)
			{
				if (Pending.Count == 0)
				{
					return;
				}
				actorId = Pending.Dequeue();
				Queued.Remove(actorId);
				if (!Registered.Contains(actorId))
				{
					processed++;
					continue;
				}
			}
			processed++;

			Actor actor = resolver(actorId);
			if (!XjSafeCore.IsAliveActor(actor))
			{
				Forget(actorId);
				continue;
			}

			XjCombatHotProfile profile = XjCombatHotPathCache.Get(actor);
			float percentPerSecond = Mathf.Clamp(profile.HealbackPerSecond, 0f, MaxHealbackPercentPerSecond);
			if (percentPerSecond <= 0f)
			{
				Forget(actorId);
				continue;
			}

			float elapsed;
			lock (Sync)
			{
				if (!Registered.Contains(actorId))
				{
					continue;
				}
				float last = LastProcessedAt.TryGetValue(actorId, out float stored) ? stored : now;
				elapsed = Mathf.Clamp(now - last, 0f, MaxElapsedSeconds);
				LastProcessedAt[actorId] = now;
			}

			if (elapsed > 0f)
			{
				float maxHealth = XjSafeCore.GetMaxHealthSafe(actor);
				float currentHealth = XjSafeCore.GetHealthSafe(actor);
				if (maxHealth > 0f && currentHealth < maxHealth)
				{
					float heal = maxHealth * (percentPerSecond / 100f) * elapsed;
					float wholeHeal;
					lock (Sync)
					{
						if (!Registered.Contains(actorId))
						{
							continue;
						}
						if (FractionalCarry.TryGetValue(actorId, out float carry))
						{
							heal += carry;
						}

						wholeHeal = Mathf.Floor(heal);
						FractionalCarry[actorId] = Mathf.Max(0f, heal - wholeHeal);
					}
					if (wholeHeal >= 1f)
					{
						XjSafeCore.HealActorSafe(actor, wholeHeal);
					}
				}
			}

			EnqueueIfNeeded(actorId);
		}
	}

	internal static void Forget(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		lock (Sync)
		{
			Registered.Remove(actorId);
			Queued.Remove(actorId);
			LastProcessedAt.Remove(actorId);
			FractionalCarry.Remove(actorId);
			// Queue 中的陈旧项由下一轮有界 Tick 惰性跳过，避免 O(n) 删除。
		}
	}

	internal static void Clear()
	{
		lock (Sync)
		{
			Pending.Clear();
			Registered.Clear();
			Queued.Clear();
			LastProcessedAt.Clear();
			FractionalCarry.Clear();
		}
	}

	private static void EnqueueIfNeeded(long actorId)
	{
		lock (Sync)
		{
			EnqueueIfNeededCore(actorId);
		}
	}

	private static void EnqueueIfNeededCore(long actorId)
	{
		if (actorId > 0L && Queued.Add(actorId))
		{
			Pending.Enqueue(actorId);
		}
	}

	private static bool TryGetActorId(Actor actor, out long actorId)
	{
		actorId = 0L;
		if (actor?.data == null)
		{
			return false;
		}

		actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L;
	}
}
