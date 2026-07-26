using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Core;

/// <summary>
/// 修士缓存 - 仿玄门道界 ActiveActorCache
/// HashSet<long> O(1) 修士检测，定期清理死亡 actor
/// </summary>
internal static class XjCultivatorCache
{
    private static readonly HashSet<long> _cultivatorIds = new HashSet<long>();
    private static readonly List<long> _orderedCultivatorIds = new List<long>();
	private static readonly Dictionary<long, int> _orderedIndexByActorId = new Dictionary<long, int>();
    private static readonly HashSet<long> _ziFuOrHigherIds = new HashSet<long>();
	private static long[] _ziFuOrHigherSnapshot = Array.Empty<long>();
	private static bool _ziFuOrHigherSnapshotDirty = true;
    private static readonly HashSet<long> _jinDanIds = new HashSet<long>();
	private static long[] _jinDanSnapshot = Array.Empty<long>();
	private static bool _jinDanSnapshotDirty = true;
	private static readonly List<long> _cleanupRemovalBuffer = new List<long>(16);
	private static readonly Dictionary<long, int> _realmTierByActorId = new Dictionary<long, int>();
    private static float _lastCleanupTime;
    private const float CleanupInterval = 2f;
    private const int CleanupBudget = 128;
    private static int _cleanupCursor;

    /// <summary>
    /// O(1) 检查 actor 是否为修士，并自动更新缓存
    /// </summary>
    public static bool CheckAndUpdate(Actor actor)
    {
        if (actor?.data == null) return false;

        long actorId = ((BaseSystemData)actor.data).id;
        bool inCache = _cultivatorIds.Contains(actorId);
        bool isCultivator = IsActorCultivator(actor);

        if (isCultivator && !inCache)
        {
            _cultivatorIds.Add(actorId);
			_orderedIndexByActorId[actorId] = _orderedCultivatorIds.Count;
            _orderedCultivatorIds.Add(actorId);
        }
        else if (!isCultivator && inCache)
        {
            _cultivatorIds.Remove(actorId);
			RemoveOrderedActor(actorId);
        }

		int realmTier = isCultivator ? XjRealmSuppression.GetRealmTier(actor) : XjRealmSuppression.TierNone;
		UpdateRealmPresence(actorId, realmTier);
		XjCultivatorCandidateIndex.Observe(actor, isCultivator, realmTier);
		XjRuntimeActorInterestIndex.Observe(actor);

        return isCultivator;
    }

    /// <summary>
    /// O(1) 检查 actorId 是否在缓存中
    /// </summary>
    public static bool IsCultivator(long actorId)
    {
        return _cultivatorIds.Contains(actorId);
    }

	internal static bool TryGetRealmTier(long actorId, out int realmTier)
	{
		realmTier = XjRealmSuppression.TierNone;
		return actorId > 0L && _realmTierByActorId.TryGetValue(actorId, out realmTier);
	}

	/// <summary>
	/// 境界增量更新：直接从外部（如 XjCultivationStateTransitions）接收
	/// 新的境界等级并更新缓存，避免重复调用 GetRealmTier(actor) 的字符串
	/// 匹配/trait 链开销。
	/// </summary>
	internal static void UpdateRealmTierDirect(long actorId, int newTier)
	{
		if (actorId <= 0L || !_cultivatorIds.Contains(actorId))
		{
			return;
		}

		// TierNone is also a valid cached result for aptitude-bearing actors who
		// have not entered TaiXi yet. Keeping zero avoids repeated realm string and
		// five-trait fallback checks in updateStats/getHit hot paths.
		_realmTierByActorId[actorId] = System.Math.Max(XjRealmSuppression.TierNone, newTier);

		SetZiFuOrHigherMembership(actorId, newTier >= XjRealmSuppression.TierZiFu);

		SetJinDanMembership(actorId, newTier >= XjRealmSuppression.TierJinDan);

		XjRuntimeActorInterestIndex.UpdateRealmTier(actorId, newTier);
		if (XjActorRegistry.Resolve(actorId, out Actor actor))
		{
			XjCultivatorCandidateIndex.Observe(actor, true, newTier);
		}
	}

    /// <summary>
    /// 获取当前修士数量
    /// </summary>
    public static int Count => _cultivatorIds.Count;

	internal static bool HasZiFuOrHigher => _ziFuOrHigherIds.Count > 0;
	internal static int ZiFuOrHigherCount => _ziFuOrHigherIds.Count;

	/// <summary>
	/// 紫府及以上 ID 的稳定只读快照，供开宗、峰脉、高境事件使用。
	/// 仅在成员跨境界或死亡时重建，避免每年从全部修士队列筛选。
	/// </summary>
	internal static IReadOnlyList<long> GetZiFuOrHigherIds()
	{
		if (_ziFuOrHigherSnapshotDirty)
		{
			_ziFuOrHigherSnapshot = new long[_ziFuOrHigherIds.Count];
			_ziFuOrHigherIds.CopyTo(_ziFuOrHigherSnapshot);
			_ziFuOrHigherSnapshotDirty = false;
		}
		return _ziFuOrHigherSnapshot;
	}

	internal static bool HasJinDan => _jinDanIds.Count > 0;

	/// <summary>
	/// 金丹 ID 的稳定只读快照。仅在境界成员发生变化时重建，年度权柄逻辑
	/// 不再扫描 World.world.units。
	/// </summary>
	internal static IReadOnlyList<long> GetJinDanIds()
	{
		if (_jinDanSnapshotDirty)
		{
			_jinDanSnapshot = new long[_jinDanIds.Count];
			_jinDanIds.CopyTo(_jinDanSnapshot);
			_jinDanSnapshotDirty = false;
		}
		return _jinDanSnapshot;
	}

	internal static void Remove(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}
		_cultivatorIds.Remove(actorId);
		RemoveOrderedActor(actorId);
		SetZiFuOrHigherMembership(actorId, false);
		SetJinDanMembership(actorId, false);
		_realmTierByActorId.Remove(actorId);
		XjCultivatorCandidateIndex.Remove(actorId);
		XjRuntimeActorInterestIndex.Remove(actorId);
	}

    /// <summary>
    /// 定期清理死亡 actor（每 2 秒执行一次）
    /// </summary>
    public static void CleanupIfNeeded()
    {
        float currentTime = Time.unscaledTime;
        if (currentTime - _lastCleanupTime < CleanupInterval) return;

        _lastCleanupTime = currentTime;
        CleanupDeadActors(CleanupBudget);
    }

    /// <summary>
    /// 强制清理死亡 actor
    /// </summary>
    public static void ForceCleanup()
    {
		CleanupDeadActors(_orderedCultivatorIds.Count);
		_cleanupCursor = 0;
        _lastCleanupTime = Time.unscaledTime;
    }

    /// <summary>
    /// 重建整个缓存（保留入口；不做全图扫描）
    /// </summary>
    public static void RebuildCache()
    {
        _cultivatorIds.Clear();
        _orderedCultivatorIds.Clear();
		_orderedIndexByActorId.Clear();
		ClearZiFuOrHigherMembership();
		ClearJinDanMembership();
		_realmTierByActorId.Clear();
		XjCultivatorCandidateIndex.Clear();
		XjRuntimeActorInterestIndex.Clear();
        _cleanupCursor = 0;
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    public static void Clear()
    {
        _cultivatorIds.Clear();
        _orderedCultivatorIds.Clear();
		_orderedIndexByActorId.Clear();
		ClearZiFuOrHigherMembership();
		ClearJinDanMembership();
		_realmTierByActorId.Clear();
		XjCultivatorCandidateIndex.Clear();
		XjRuntimeActorInterestIndex.Clear();
        _cleanupCursor = 0;
    }

    /// <summary>
    /// 获取所有已知修士 ID 集合（供调度器迭代使用）
    /// </summary>
    public static IReadOnlyList<long> GetAllIds()
    {
        return _orderedCultivatorIds;
    }

    /// <summary>
    /// 判断 actor 是否为修士（XjZz > 0 或已入道）
    /// </summary>
    private static bool IsActorCultivator(Actor actor)
    {
        if (actor?.data == null) return false;

        // 资质 > 0 即为修士（包括未入道但有资质的）
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz) && xjZz > 0)
        {
            return true;
        }

        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            && IsKnownRealmId(realmId))
        {
            return true;
        }

        if (actor.hasTrait(XjRealmIds.TaiXi)
            || actor.hasTrait(XjRealmIds.LianQi)
            || actor.hasTrait(XjRealmIds.ZhuJi)
            || actor.hasTrait(XjRealmIds.ZiFu)
            || actor.hasTrait(XjRealmIds.JinDan))
        {
            return true;
        }

        return false;
    }

    private static bool IsKnownRealmId(string realmId)
    {
        return XjRealmHelper.IsKnownTag(realmId);
    }

    /// <summary>
    /// 清理死亡 actor
    /// </summary>
    private static void CleanupDeadActors(int budget)
    {
        if (_orderedCultivatorIds.Count == 0 || budget <= 0)
		{
			_cleanupCursor = 0;
			return;
		}

        _cleanupRemovalBuffer.Clear();
		int checks = System.Math.Min(budget, _orderedCultivatorIds.Count);
		for (int i = 0; i < checks; i++)
        {
			if (_cleanupCursor >= _orderedCultivatorIds.Count)
			{
				_cleanupCursor = 0;
			}

			long actorId = _orderedCultivatorIds[_cleanupCursor++];
			XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor);
            if (actor == null || !actor.isAlive() || !IsActorCultivator(actor))
            {
                _cleanupRemovalBuffer.Add(actorId);
            }
        }

        if (_cleanupRemovalBuffer.Count > 0)
        {
            for (int i = 0; i < _cleanupRemovalBuffer.Count; i++)
            {
				long actorId = _cleanupRemovalBuffer[i];
                _cultivatorIds.Remove(actorId);
				RemoveOrderedActor(actorId);
				SetZiFuOrHigherMembership(actorId, false);
				SetJinDanMembership(actorId, false);
				_realmTierByActorId.Remove(actorId);
				XjCultivatorCandidateIndex.Remove(actorId);
				XjRuntimeActorInterestIndex.Remove(actorId);
            }
			if (_cleanupCursor > _orderedCultivatorIds.Count)
			{
				_cleanupCursor = 0;
			}
        }
    }

	private static void UpdateRealmPresence(long actorId, int realmTier)
	{
		if (actorId <= 0L)
		{
			return;
		}

		if (_cultivatorIds.Contains(actorId))
		{
			_realmTierByActorId[actorId] = System.Math.Max(XjRealmSuppression.TierNone, realmTier);
		}
		else
		{
			_realmTierByActorId.Remove(actorId);
		}

		SetZiFuOrHigherMembership(actorId, realmTier >= XjRealmSuppression.TierZiFu);

		SetJinDanMembership(actorId, realmTier >= XjRealmSuppression.TierJinDan);
	}

	private static void SetJinDanMembership(long actorId, bool included)
	{
		if (actorId <= 0L)
		{
			return;
		}

		bool changed = included ? _jinDanIds.Add(actorId) : _jinDanIds.Remove(actorId);
		if (changed)
		{
			_jinDanSnapshotDirty = true;
		}
	}

	private static void SetZiFuOrHigherMembership(long actorId, bool included)
	{
		if (actorId <= 0L)
		{
			return;
		}

		bool changed = included ? _ziFuOrHigherIds.Add(actorId) : _ziFuOrHigherIds.Remove(actorId);
		if (changed)
		{
			_ziFuOrHigherSnapshotDirty = true;
		}
	}

	private static void ClearZiFuOrHigherMembership()
	{
		_ziFuOrHigherIds.Clear();
		_ziFuOrHigherSnapshot = Array.Empty<long>();
		_ziFuOrHigherSnapshotDirty = false;
	}

	private static void ClearJinDanMembership()
	{
		_jinDanIds.Clear();
		_jinDanSnapshot = Array.Empty<long>();
		_jinDanSnapshotDirty = false;
	}

	private static void RemoveOrderedActor(long actorId)
	{
		if (!_orderedIndexByActorId.TryGetValue(actorId, out int index))
		{
			return;
		}

		int lastIndex = _orderedCultivatorIds.Count - 1;
		long lastActorId = _orderedCultivatorIds[lastIndex];
		_orderedCultivatorIds[index] = lastActorId;
		_orderedCultivatorIds.RemoveAt(lastIndex);
		_orderedIndexByActorId.Remove(actorId);
		if (index < lastIndex)
		{
			_orderedIndexByActorId[lastActorId] = index;
		}
		if (_cleanupCursor > index)
		{
			_cleanupCursor--;
		}
	}
}
