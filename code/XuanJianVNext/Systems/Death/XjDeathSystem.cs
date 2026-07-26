using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Broadcast;

namespace XuanJianVNext.Systems.Death;

internal delegate bool XjDeathActorResolver(long actorId, out Actor actor);

internal readonly struct XjDeathLaneQueue
{
	private readonly Queue<long> _queue;
	private readonly HashSet<long> _set;

	internal XjDeathLaneQueue(bool _)
	{
		_queue = new Queue<long>();
		_set = new HashSet<long>();
	}

	internal bool TryEnqueue(Actor actor)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !_set.Add(actorId)) return false;
		_queue.Enqueue(actorId);
		return true;
	}

	internal long TryDequeue()
	{
		long actorId = _queue.Dequeue();
		_set.Remove(actorId);
		return actorId;
	}

	internal void Clear()
	{
		_queue.Clear();
		_set.Clear();
	}

	internal int Count => _queue.Count;
}

internal static class XjZiFuFailureDeathLane
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

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuFailureDeathPending, out int pending)
			|| pending != 1)
		{
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuFailureDeathHandled, out int handled)
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

			if (!resolver(actorId, out Actor actor) || actor == null)
			{
				continue;
			}

			XjZiFuFailureDeathExecutor.TryExecute(actor);
		}
	}

	internal static void Clear()
	{
		_queue.Clear();
	}
}

internal static class XjZiFuFailureDeathExecutor
{
	private const int Pending = 1;
	private const int Handled = 1;
	private const int ZiFuFailureAttackType = 5;

	internal static void TryExecute(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return;
		}

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuFailureDeathPending, out int pending)
			|| pending != Pending)
		{
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuFailureDeathHandled, out int handled)
			&& handled == Handled)
		{
			return;
		}

		if (XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierZiFu)
		{
			ClearStalePending(actor);
			return;
		}

		try
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZiFuFailureDeathReason, out string reason);
			string actorName = actor.getName() ?? "筑基修士";
			string announcement = actorName + " 冲击紫府未成，道基崩散，魂归天际。";
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuFailureDeathHandled, Handled);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason,
				string.IsNullOrWhiteSpace(reason) ? "ZiFuBreakthroughFailed" : reason.Trim());
			bool died = XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)ZiFuFailureAttackType, false);
			if (died)
			{
				XjBroadcastSystem.BroadcastBLevelActorEvent(actor, announcement, announcement, XjEventIconCatalog.ZiFuUpgrade);
			}
			else if (XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuFailureDeathHandled, 0);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
			}
		}
		catch
		{
			if (XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuFailureDeathHandled, 0);
			}
		}
	}

	private static void ClearStalePending(Actor actor)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuFailureDeathPending, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuFailureDeathHandled, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZiFuFailureDeathReason, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
	}
}

internal static class XjRenDanDeathLane
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

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjRenDanDeathPending, out int pending)
			|| pending != 1)
		{
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjRenDanDeathHandled, out int handled)
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

			if (!resolver(actorId, out Actor actor) || actor == null)
			{
				continue;
			}

			XjRenDanDeathExecutor.TryExecute(actor);
		}
	}

	internal static void Clear()
	{
		_queue.Clear();
	}
}

internal static class XjRenDanDeathExecutor
{
	private const int Pending = 1;
	private const int Handled = 1;
	private const int RenDanAttackType = 5;

	internal static void TryExecute(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return;
		}

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjRenDanDeathPending, out int pending)
			|| pending != Pending)
		{
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjRenDanDeathHandled, out int handled)
			&& handled == Handled)
		{
			return;
		}

		try
		{
			long victimActorId = ((BaseSystemData)actor.data).id;
			string victimActorName = actor.getName() ?? string.Empty;
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjRenDanDeathSourceActorId, out int sourceActorId);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjRenDanDeathYear, out int deathYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjRenDanDeathHandled, Handled);
			bool died = XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)RenDanAttackType, false);
			if (died)
			{
				XjRenDan.FinalizeVictimDeath(victimActorId, sourceActorId, deathYear, victimActorName);
			}
			else if (XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjRenDanDeathHandled, 0);
			}
		}
		catch
		{
			if (XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjRenDanDeathHandled, 0);
			}
		}
	}
}

internal static class XjQiYuDongTianDeathLane
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

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathPending, out int pending)
			|| pending != 1)
		{
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathHandled, out int handled)
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

			if (!resolver(actorId, out Actor actor) || actor == null)
			{
				continue;
			}

			XjQiYuDongTianDeathExecutor.TryExecute(actor);
		}
	}

	internal static void Clear()
	{
		_queue.Clear();
	}
}

internal static class XjQiYuDongTianDeathExecutor
{
	private const int Pending = 1;
	private const int Handled = 1;
	private const int DongTianAttackType = 5;

	internal static void TryExecute(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return;
		}

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathPending, out int pending)
			|| pending != Pending)
		{
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathHandled, out int handled)
			&& handled == Handled)
		{
			return;
		}

		try
		{
			long actorId = ((BaseSystemData)actor.data).id;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiYuDongTianDeathRecordId, out string recordId);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathYear, out int deathYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathHandled, Handled);
			bool died = XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)DongTianAttackType, false);
			if (died)
			{
				XjDongTianRegistry.FinalizeExplorerDeath(recordId, actorId, deathYear);
			}
			else if (XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathHandled, 0);
			}
		}
		catch
		{
			if (XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiYuDongTianDeathHandled, 0);
			}
		}
	}
}
