using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.XianGuo;

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
		// 最后一层载荷门禁：帝明阳本人不允许落下任何神丹挂靠状态。
		if (XjXianGuoSystem.IsDiMingYang(actor)) return;

		int safeYear = Math.Max(1, year);
		// 这里只先写神丹候选载荷；活动金丹果位必须等根锚点注册成功后再释放。
		// 若容量竞争或旧档迁移失败，原金丹身份仍可完整回滚。
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanGuoWei, (guoWei ?? string.Empty).Trim());
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjShenDanAnchorActorId, anchorActorId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanAnchorName, (anchorName ?? string.Empty).Trim());
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanYear, safeYear);
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
	private static readonly HashSet<long> PendingDependents = new HashSet<long>();

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
		bool strictActive = XjGuoWeiRegistry.TryGetStrictActiveEntryByActorId(anchor.ActorId, out XjGuoWeiRegistryEntry strictAnchor)
			&& string.Equals(
				XjGuoWeiCalculator.NormalizeGuoWeiName(strictAnchor.GuoWei),
				XjGuoWeiCalculator.NormalizeGuoWeiName(anchor.GuoWei),
				StringComparison.Ordinal);
		bool daoTaiYuAnchor = TryResolveDaoTaiYuAnchor(anchor.ActorId, anchor.GuoWei, out _);
		if ((!strictActive && !daoTaiYuAnchor)
			|| !XjActorRegistry.ResolveKnownOrWorld(anchor.ActorId, out Actor anchorActor)
			|| !XjSafeCore.IsAliveActor(anchorActor)
			|| XjXuanJianShenTongSpecials.IsJieLinXian(anchorActor)
			|| XjXuanJianShenTongSpecials.IsYuYiXian(anchorActor))
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
		if (!state.Found || actor?.data == null) return;
		long dependentId = ((BaseSystemData)actor.data).id;
		if (!TryResolveLegalRootAnchor(state.AnchorActorId, dependentId, state.GuoWei, out XjGuoWeiRegistryEntry root))
		{
			Forget(dependentId);
			PendingDependents.Add(dependentId);
			return;
		}

		// 先完成容量与根锚点注册，再写回角色载荷。这样旧档中的满锚点不会
		// 留下“看似已压平、实际未登记”的半完成神丹关系。
		if (TryRegister(dependentId, root.ActorId))
		{
			WriteAnchorProjection(actor, root);
			PendingDependents.Remove(dependentId);
			return;
		}

		// 果位道胎将本道余位炼入后辈时，根锚点固定为这位道胎本人。
		// 满额后只能等待其名下神丹空出，不得自动改挂到另一位普通余位持有者。
		if (!TryResolveDaoTaiYuAnchor(root.ActorId, root.GuoWei, out _))
		{
			string guoWeiType = XjGuoWeiRegistry.ResolveTypeFromName(root.GuoWei);
			if (XjGuoWeiRegistry.TryFindActiveAnchor(
				root.DaoTu,
				guoWeiType,
				candidate => CanAttachToAnchor(candidate, dependentId, out _, out _),
				out XjGuoWeiRegistryEntry fallback)
				&& TryRegister(dependentId, fallback.ActorId))
			{
				WriteAnchorProjection(actor, fallback);
				PendingDependents.Remove(dependentId);
				return;
			}
		}

		Forget(dependentId);
		PendingDependents.Add(dependentId);
	}

	private static void WriteAnchorProjection(Actor actor, in XjGuoWeiRegistryEntry anchor)
	{
		if (actor?.data == null || !anchor.Found || anchor.ActorId <= 0L) return;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanGuoWei, anchor.GuoWei);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjShenDanAnchorActorId, anchor.ActorId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanAnchorName,
			string.IsNullOrWhiteSpace(anchor.ActorName) ? "无名金丹" : anchor.ActorName);
	}

	internal static void Register(long dependentActorId, long anchorActorId)
	{
		TryRegister(dependentActorId, anchorActorId);
	}

	internal static bool TryRegister(long dependentActorId, long anchorActorId)
	{
		if (!XjActorRegistry.ResolveKnownOrWorld(dependentActorId, out Actor dependent)
			|| !XjSafeCore.IsAliveActor(dependent))
		{
			return false;
		}
		XjShenDanState committed = XjShenDanAccessor.BuildState(dependent);
		if (!committed.Found
			|| !TryResolveLegalRootAnchor(anchorActorId, dependentActorId, committed.GuoWei, out XjGuoWeiRegistryEntry root))
		{
			return false;
		}
		if (!CanAttachToAnchor(root, dependentActorId, out _, out _)) return false;

		if (AnchorByDependent.TryGetValue(dependentActorId, out long oldAnchor) && oldAnchor != root.ActorId)
		{
			RemoveFromAnchor(oldAnchor, dependentActorId);
		}
		AnchorByDependent[dependentActorId] = root.ActorId;
		if (!DependentsByAnchor.TryGetValue(root.ActorId, out HashSet<long> set))
		{
			set = new HashSet<long>();
			DependentsByAnchor[root.ActorId] = set;
		}
		set.Add(dependentActorId);
		PendingDependents.Remove(dependentActorId);
		// 关系登记成功后再提交“金丹→神丹”身份切换。神丹从此不再持有
		// 活动果位，因此严格锚点查询绝不会把它挂成下一枚神丹的根。
		XjJinDanAccessor.ClearActivePayloadForShenDanPromotion(
			dependent,
			committed.Year > 0 ? committed.Year : 1);
		return true;
	}

	/// <summary>
	/// 旧档角色加载顺序不稳定：神丹可能先于其金丹根锚点进入运行时。
	/// 每当一个活动果位重建完成，只重试此前失败的神丹，不再次扫描全世界。
	/// </summary>
	internal static void ReconcilePending()
	{
		if (PendingDependents.Count == 0) return;
		long[] ids = new long[PendingDependents.Count];
		PendingDependents.CopyTo(ids);
		for (int i = 0; i < ids.Length; i++)
		{
			long actorId = ids[i];
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| !XjSafeCore.IsAliveActor(actor)
				|| !XjShenDanAccessor.BuildState(actor).Found)
			{
				PendingDependents.Remove(actorId);
				continue;
			}
			Observe(actor);
		}
	}

	private static bool TryResolveLegalRootAnchor(
		long requestedAnchorId,
		long dependentActorId,
		string desiredGuoWei,
		out XjGuoWeiRegistryEntry root)
	{
		root = default;
		if (requestedAnchorId <= 0L || dependentActorId <= 0L || requestedAnchorId == dependentActorId) return false;
		string desired = XjGuoWeiCalculator.NormalizeGuoWeiName(desiredGuoWei);
		HashSet<long> visited = new HashSet<long> { dependentActorId };
		long current = requestedAnchorId;
		for (int depth = 0; depth < 16 && current > 0L && visited.Add(current); depth++)
		{
			if (XjGuoWeiRegistry.TryGetStrictActiveEntryByActorId(current, out XjGuoWeiRegistryEntry strict)
				&& (desired.Length == 0
					|| string.Equals(
						XjGuoWeiCalculator.NormalizeGuoWeiName(strict.GuoWei),
						desired,
						StringComparison.Ordinal)))
			{
				root = strict;
				return true;
			}
			if (TryResolveDaoTaiYuAnchor(current, desired, out XjGuoWeiRegistryEntry daoTaiYu))
			{
				root = daoTaiYu;
				return true;
			}
			if (!XjActorRegistry.ResolveKnownOrWorld(current, out Actor actor) || !XjSafeCore.IsAliveActor(actor)) return false;
			XjShenDanState state = XjShenDanAccessor.BuildState(actor);
			if (!state.Found || state.AnchorActorId <= 0L) return false;
			if (desired.Length == 0) desired = XjGuoWeiCalculator.NormalizeGuoWeiName(state.GuoWei);
			current = state.AnchorActorId;
		}
		root = default;
		return false;
	}

	internal static bool TryResolveDaoTaiYuAnchor(
		long actorId,
		string desiredGuoWei,
		out XjGuoWeiRegistryEntry root)
	{
		root = default;
		if (actorId <= 0L
			|| !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| !XjSafeCore.IsAliveActor(actor)
			|| !XjDaoTaiSpellScale.IsDaoTaiActor(actor))
		{
			return false;
		}

		// 原著口径不是“道胎必须先兼持一席余位”，而是果位之主可取本道余位
		// 藏入紫府体内，炼成躲在道胎之下的神丹。因此这里构造的是神丹专用
		// 隐锚点：不向果位 Registry 新增一名余位持有者，也不让神丹获得可见果位。
		string actual = XjGuoWeiCalculator.NormalizeGuoWeiName(desiredGuoWei);
		if (actual.Length == 0
			|| !string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(actual), XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			return false;
		}

		string daoTu = XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(actual);
		if (string.IsNullOrWhiteSpace(daoTu)
			|| !XjDaoLineageStateRegistry.TryResolveActiveFruitHolder(daoTu, out long fruitHolderId, out _)
			|| fruitHolderId != actorId)
		{
			return false;
		}

		// 新炼神丹时由事件入口按“当前道势能否生余位”判定；已经炼成的神丹则
		// 不能因为后世道势下降就失去根锚，因此这里只校验世界规则允许的绝对余位槽。
		int slot = XjGuoWeiCalculator.ResolveSlotIndex(actual);
		if (slot <= 0 || slot > XjGuoWeiQuanBingRules.YuWeiSlotCount) return false;

		root = new XjGuoWeiRegistryEntry(
			true,
			actorId,
			actor.getName(),
			string.Empty,
			daoTu,
			XjJinXingCalculator.Calculate(daoTu, actorId),
			actual,
			Math.Max(1, XjYearTracker.CurrentYear),
			XjGuoWeiRegistry.StatusActive,
			0,
			string.Empty);
		return true;
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

	/// <summary>
	/// Rebinds runtime ShenDan relationships when a third-party mod replaces an Actor with
	/// a freshly generated Actor instead of performing a real XuanJian death. The old actor
	/// id must not remain as a root/dependent key after the replacement source disappears.
	/// </summary>
	internal static void RebindActorAfterExternalReplacement(long oldActorId, Actor target)
	{
		if (oldActorId <= 0L || target?.data == null || !target.isAlive())
		{
			return;
		}

		long newActorId = ((BaseSystemData)target.data).id;
		if (newActorId <= 0L || newActorId == oldActorId)
		{
			return;
		}

		// If the replaced actor was itself a ShenDan, remove the obsolete dependent id
		// and let the copied target payload register against its live root.
		if (AnchorByDependent.TryGetValue(oldActorId, out long oldAnchorId))
		{
			AnchorByDependent.Remove(oldActorId);
			RemoveFromAnchor(oldAnchorId, oldActorId);
		}
		PendingDependents.Remove(oldActorId);

		XjShenDanState targetState = XjShenDanAccessor.BuildState(target);
		if (targetState.Found)
		{
			Observe(target);
		}

		if (!DependentsByAnchor.TryGetValue(oldActorId, out HashSet<long> oldDependents)
			|| oldDependents == null
			|| oldDependents.Count == 0)
		{
			DependentsByAnchor.Remove(oldActorId);
			return;
		}

		long[] dependentIds = new long[oldDependents.Count];
		oldDependents.CopyTo(dependentIds);
		DependentsByAnchor.Remove(oldActorId);
		for (int i = 0; i < dependentIds.Length; i++)
		{
			long dependentId = dependentIds[i];
			if (dependentId <= 0L) continue;
			AnchorByDependent.Remove(dependentId);

			if (!XjActorRegistry.ResolveKnownOrWorld(dependentId, out Actor dependent)
				|| !XjSafeCore.IsAliveActor(dependent))
			{
				PendingDependents.Remove(dependentId);
				continue;
			}

			XjShenDanState dependentState = XjShenDanAccessor.BuildState(dependent);
			if (!dependentState.Found
				|| !TryResolveLegalRootAnchor(newActorId, dependentId, dependentState.GuoWei, out XjGuoWeiRegistryEntry root))
			{
				PendingDependents.Add(dependentId);
				continue;
			}

			AnchorByDependent[dependentId] = newActorId;
			if (!DependentsByAnchor.TryGetValue(newActorId, out HashSet<long> newDependents))
			{
				newDependents = new HashSet<long>();
				DependentsByAnchor[newActorId] = newDependents;
			}
			newDependents.Add(dependentId);
			WriteAnchorProjection(dependent, root);
			PendingDependents.Remove(dependentId);
		}
	}

	internal static void Forget(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		PendingDependents.Remove(actorId);
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
		string safeAnchorName = string.IsNullOrWhiteSpace(anchorName) ? "所挂靠真君" : anchorName.Trim();
		string actorName = actor.getName() ?? "神丹修士";
		string announcement = actorName + " 所挂靠的高境锚点 " + safeAnchorName + " 已殁，神丹托身无所寄托，随之魂归天际。";
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
		catch (System.Exception xjCaught454_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjShenDan.cs:454", xjCaught454_1);
			
			if (XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanDeathHandled, 0);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
			}
		}
	}
}
