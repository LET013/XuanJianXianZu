using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.YaoShu;

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
	// Detection keeps a second ordered id mirror. While the annual detector holds a
	// lease this mirror is frozen; membership mutations append O(1) deltas instead
	// of copying N ids. Releasing the lease replays only those deltas, making the
	// next snapshot current again without an annual scheduling spike.
	private static List<long> _detectionSnapshotIds = new List<long>();
	private static Dictionary<long, int> _detectionSnapshotIndexByActorId = new Dictionary<long, int>();
	private static readonly List<long> _detectionMembershipDeltas = new List<long>(32);
	private static bool _detectionSnapshotLeased;
    private static readonly HashSet<long> _ziFuOrHigherIds = new HashSet<long>();
	private static long[] _ziFuOrHigherSnapshot = Array.Empty<long>();
	private static bool _ziFuOrHigherSnapshotDirty = true;
    private static readonly HashSet<long> _jinDanIds = new HashSet<long>();
	private static long[] _jinDanSnapshot = Array.Empty<long>();
	private static bool _jinDanSnapshotDirty = true;
	// 跨修法世界身份缓存：真人级＝紫府／服气真人，真君级＝金丹／神丹／真君羽士。
	// 旧缓存继续只服务紫府金丹专属玩法，二者不可混用。
	private static readonly HashSet<long> _zhenRenOrHigherIds = new HashSet<long>();
	private static long[] _zhenRenOrHigherSnapshot = Array.Empty<long>();
	private static bool _zhenRenOrHigherSnapshotDirty = true;
	private static readonly HashSet<long> _zhenJunOrHigherIds = new HashSet<long>();
	private static long[] _zhenJunOrHigherSnapshot = Array.Empty<long>();
	private static bool _zhenJunOrHigherSnapshotDirty = true;
	// 释修业务域索引：释土、金地、轮回和七相系统只遍历真实释修，
	// 不再从全部修士中逐个筛选。成员变化时精确失效稳定 ID 快照。
	private static readonly HashSet<long> _shiIds = new HashSet<long>();
	private static long[] _shiSnapshot = Array.Empty<long>();
	private static bool _shiSnapshotDirty = true;
	private static readonly List<long> _cleanupRemovalBuffer = new List<long>(16);
	private static readonly Dictionary<long, int> _realmTierByActorId = new Dictionary<long, int>();
	private const int AnnualAuditBucketCount = 8;
	private static readonly HashSet<long>[] _annualAuditIdsByBucket = CreateAnnualAuditBuckets();
	private static readonly long[][] _annualAuditSnapshots = new long[AnnualAuditBucketCount][];
	private static readonly bool[] _annualAuditSnapshotDirty = CreateDirtyAnnualAuditFlags();
    private static int _cleanupCursor;
    private static int _membershipRevision;

    /// <summary>Stable membership freshness token for world-level cultivator aggregates.</summary>
    internal static int MembershipRevision => _membershipRevision;

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
            unchecked { _membershipRevision++; }
			_orderedIndexByActorId[actorId] = _orderedCultivatorIds.Count;
            _orderedCultivatorIds.Add(actorId);
			TrackDetectionMembershipChange(actorId, added: true);
			AddAnnualAuditMembership(actorId);
        }
        else if (!isCultivator && inCache)
        {
            _cultivatorIds.Remove(actorId);
            unchecked { _membershipRevision++; }
			RemoveOrderedActor(actorId);
			TrackDetectionMembershipChange(actorId, added: false);
			RemoveAnnualAuditMembership(actorId);
        }

		int realmTier = isCultivator ? XjRealmSuppression.GetRealmTier(actor) : XjRealmSuppression.TierNone;
		UpdateRealmPresence(actorId, realmTier,
			XjCultivationPathRules.IsZiFuJinDan(actor) || XjCultivationPathRules.IsFuQiYangXing(actor));
		SetShiMembership(actorId, isCultivator && XjCultivationPathRules.IsShi(actor));
		XjCultivatorCandidateIndex.Observe(actor, isCultivator, realmTier);
		XjFuQiCandidateIndex.Observe(actor);
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

		bool hasActor = XjActorRegistry.Resolve(actorId, out Actor actor);
		bool isZiFuPath = hasActor && XjCultivationPathRules.IsZiFuJinDan(actor);
		bool isSharedImmortalHighRealmPath = hasActor
			&& (isZiFuPath || XjCultivationPathRules.IsFuQiYangXing(actor));
		SetZiFuOrHigherMembership(actorId, isZiFuPath && newTier >= XjRealmSuppression.TierZiFu);

		SetJinDanMembership(actorId, isZiFuPath && newTier >= XjRealmSuppression.TierJinDan);
		SetZhenRenOrHigherMembership(actorId, isSharedImmortalHighRealmPath && newTier >= XjRealmSuppression.TierZiFu);
		SetZhenJunOrHigherMembership(actorId, isSharedImmortalHighRealmPath && newTier >= XjRealmSuppression.TierJinDan);
		SetShiMembership(actorId, hasActor && XjCultivationPathRules.IsShi(actor));

		XjRuntimeActorInterestIndex.UpdateRealmTier(actorId, newTier);
		if (actor != null)
		{
			XjCultivatorCandidateIndex.Observe(actor, true, newTier);
			XjFuQiCandidateIndex.Observe(actor);
		}
	}

    /// <summary>
    /// 获取当前修士数量
    /// </summary>
    public static int Count => _cultivatorIds.Count;

	internal static bool HasZiFuOrHigher => _ziFuOrHigherIds.Count > 0;

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

	internal static bool HasZhenRenOrHigher => _zhenRenOrHigherIds.Count > 0;
	internal static int ZhenRenOrHigherCount => _zhenRenOrHigherIds.Count;
	internal static bool HasZhenJunOrHigher => _zhenJunOrHigherIds.Count > 0;

	internal static IReadOnlyList<long> GetZhenRenOrHigherIds()
	{
		if (_zhenRenOrHigherSnapshotDirty)
		{
			_zhenRenOrHigherSnapshot = new long[_zhenRenOrHigherIds.Count];
			_zhenRenOrHigherIds.CopyTo(_zhenRenOrHigherSnapshot);
			_zhenRenOrHigherSnapshotDirty = false;
		}
		return _zhenRenOrHigherSnapshot;
	}

	internal static IReadOnlyList<long> GetZhenJunOrHigherIds()
	{
		if (_zhenJunOrHigherSnapshotDirty)
		{
			_zhenJunOrHigherSnapshot = new long[_zhenJunOrHigherIds.Count];
			_zhenJunOrHigherIds.CopyTo(_zhenJunOrHigherSnapshot);
			_zhenJunOrHigherSnapshotDirty = false;
		}
		return _zhenJunOrHigherSnapshot;
	}

	internal static int ShiCount => _shiIds.Count;

	/// <summary>
	/// 释修 ID 的稳定只读快照。仅在入释、脱离释修身份、死亡或读档重建时更新，
	/// 供释土、金地、七相与轮回系统限定业务域扫描。
	/// </summary>
	internal static IReadOnlyList<long> GetShiIds()
	{
		if (_shiSnapshotDirty)
		{
			_shiSnapshot = new long[_shiIds.Count];
			_shiIds.CopyTo(_shiSnapshot);
			_shiSnapshotDirty = false;
		}
		return _shiSnapshot;
	}

	internal static void Remove(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}
		bool removed = _cultivatorIds.Remove(actorId);
		RemoveOrderedActor(actorId);
		if (removed)
		{
            unchecked { _membershipRevision++; }
			TrackDetectionMembershipChange(actorId, added: false);
		}
		RemoveAnnualAuditMembership(actorId);
		SetZiFuOrHigherMembership(actorId, false);
		SetJinDanMembership(actorId, false);
		SetZhenRenOrHigherMembership(actorId, false);
		SetZhenJunOrHigherMembership(actorId, false);
		SetShiMembership(actorId, false);
		_realmTierByActorId.Remove(actorId);
		XjCultivatorCandidateIndex.Remove(actorId);
		XjFuQiCandidateIndex.Remove(actorId);
		XjRuntimeActorInterestIndex.Remove(actorId);
	}

    /// <summary>
    /// 调度器统一触发的限定生命周期检测。这里只检查固定数量的已知修士，
    /// 不维护独立计时器，也不扫描全世界角色。
    /// </summary>
	internal static void CleanupInvalid(int maxChecks, Action<long> onInvalid)
    {
		if (_orderedCultivatorIds.Count == 0 || maxChecks <= 0)
		{
			_cleanupCursor = 0;
			return;
		}

		_cleanupRemovalBuffer.Clear();
		int checks = System.Math.Min(maxChecks, _orderedCultivatorIds.Count);
		for (int i = 0; i < checks; i++)
		{
			if (_cleanupCursor >= _orderedCultivatorIds.Count)
			{
				_cleanupCursor = 0;
			}

			long actorId = _orderedCultivatorIds[_cleanupCursor++];
			XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor);
			if (actor == null || !actor.isAlive())
			{
				_cleanupRemovalBuffer.Add(actorId);
			}
			else if (!IsActorCultivator(actor))
			{
				// 活着但已失去修炼身份的角色只退出修士索引，不能走死亡回收链。
				Remove(actorId);
			}
		}

		for (int i = 0; i < _cleanupRemovalBuffer.Count; i++)
		{
			onInvalid?.Invoke(_cleanupRemovalBuffer[i]);
		}

		if (_cleanupCursor > _orderedCultivatorIds.Count)
		{
			_cleanupCursor = 0;
		}
		_cleanupRemovalBuffer.Clear();
    }

    /// <summary>
    /// 重建整个缓存（保留入口；不做全图扫描）
    /// </summary>
    public static void RebuildCache()
    {
        _cultivatorIds.Clear();
        _orderedCultivatorIds.Clear();
		ResetDetectionSnapshotMirror();
		_orderedIndexByActorId.Clear();
		ClearAnnualAuditMembership();
		ClearZiFuOrHigherMembership();
		ClearJinDanMembership();
		ClearZhenRenOrHigherMembership();
		ClearZhenJunOrHigherMembership();
		ClearShiMembership();
		_realmTierByActorId.Clear();
		XjCultivatorCandidateIndex.Clear();
		XjFuQiCandidateIndex.Clear();
		XjRuntimeActorInterestIndex.Clear();
        _cleanupCursor = 0;
        _membershipRevision = 0;
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    public static void Clear()
    {
        _cultivatorIds.Clear();
        _orderedCultivatorIds.Clear();
		ResetDetectionSnapshotMirror();
		_orderedIndexByActorId.Clear();
		ClearAnnualAuditMembership();
		ClearZiFuOrHigherMembership();
		ClearJinDanMembership();
		ClearZhenRenOrHigherMembership();
		ClearZhenJunOrHigherMembership();
		ClearShiMembership();
		_realmTierByActorId.Clear();
		XjCultivatorCandidateIndex.Clear();
		XjFuQiCandidateIndex.Clear();
		XjRuntimeActorInterestIndex.Clear();
        _cleanupCursor = 0;
        _membershipRevision = 0;
    }
	/// <summary>
	/// 获取所有已知修士 ID 集合（供只读即时查询使用）。普通调用方不得跨帧保存此
	/// 可变 List 引用。跨帧 detection 必须使用 AcquireAllIdsSnapshotForDetection，
	/// 由独立冻结镜像保证本轮游标期间的成员集合不发生漂移。
	/// </summary>
	public static IReadOnlyList<long> GetAllIds()
	{
		return _orderedCultivatorIds;
	}

	/// <summary>
	/// Acquires an immutable-for-the-lease ordered id view without copying N ids. The
	/// mirror is maintained alongside the normal membership list while idle. During a
	/// lease, add/remove operations are recorded as O(1) deltas and replayed on release.
	/// </summary>
	internal static IReadOnlyList<long> AcquireAllIdsSnapshotForDetection()
	{
		if (!_detectionSnapshotLeased)
		{
			_detectionSnapshotLeased = true;
			_detectionMembershipDeltas.Clear();
		}
		return _detectionSnapshotIds;
	}

	internal static void ReleaseAllIdsSnapshotForDetection()
	{
		if (!_detectionSnapshotLeased) return;
		_detectionSnapshotLeased = false;
		for (int i = 0; i < _detectionMembershipDeltas.Count; i++)
		{
			long delta = _detectionMembershipDeltas[i];
			if (delta > 0L) AddDetectionMirror(delta);
			else if (delta < 0L) RemoveDetectionMirror(-delta);
		}
		_detectionMembershipDeltas.Clear();
	}


	/// <summary>
	/// 年度补漏只审计一个稳定分片。原生 updateAge 仍是正常年度入口；
	/// 八个世界年轮换一遍全部已知修士，用于修复极端掉帧或外部模组跳过回调，
	/// 避免每年复制并解析全部修士。
	/// </summary>
	internal static IReadOnlyList<long> GetAnnualAuditIds(int year)
	{
		int bucket = PositiveModulo(year, AnnualAuditBucketCount);
		if (_annualAuditSnapshotDirty[bucket])
		{
			HashSet<long> ids = _annualAuditIdsByBucket[bucket];
			long[] snapshot = new long[ids.Count];
			ids.CopyTo(snapshot);
			_annualAuditSnapshots[bucket] = snapshot;
			_annualAuditSnapshotDirty[bucket] = false;
		}

		return _annualAuditSnapshots[bucket] ?? Array.Empty<long>();
	}

    /// <summary>
    /// 判断 actor 是否为修士（XjZz > 0 或已入道）
    /// </summary>
    private static bool IsActorCultivator(Actor actor)
    {
        if (actor?.data == null || XjGuZunRegistry.IsManifestationActor(actor)) return false;

        // 妖属大圣是固定十二席的服气修士。不能等待可见境界 trait 的下一次同步，
        // 否则化生后到年度维护前会从修士缓存和排行榜候选集中漏掉。
        if (XjYaoShuGreatSageSystem.IsGreatSage(actor))
        {
            return true;
        }

        if (XjCultivationPathRules.IsShi(actor)
            || (XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string shiRealm)
                && !string.IsNullOrWhiteSpace(shiRealm)))
        {
            return true;
        }

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
            || actor.hasTrait(XjRealmIds.JinDan)
            || actor.hasTrait(XjRealmIds.DaoTai)
            || actor.hasTrait(XjRealmIds.ShenDan)
            || actor.hasTrait(XjRealmIds.HuangGuan)
            || actor.hasTrait(XjRealmIds.FuQiZhenRen)
            || actor.hasTrait(XjRealmIds.ZhenJunYuShi)
            || actor.hasTrait(XjRealmIds.FuQiDaoTai))
        {
            return true;
        }

        return false;
    }

    private static bool IsKnownRealmId(string realmId)
    {
        return XjRealmHelper.IsKnownTag(realmId);
    }

	private static void UpdateRealmPresence(long actorId, int realmTier, bool isSharedImmortalHighRealmPath)
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

		bool isZiFuPath = false;
		bool isShi = false;
		if (XjActorRegistry.Resolve(actorId, out Actor actor))
		{
			isZiFuPath = XjCultivationPathRules.IsZiFuJinDan(actor);
			isShi = XjCultivationPathRules.IsShi(actor);
		}
		SetZiFuOrHigherMembership(actorId, isZiFuPath && realmTier >= XjRealmSuppression.TierZiFu);

		SetJinDanMembership(actorId, isZiFuPath && realmTier >= XjRealmSuppression.TierJinDan);
		SetZhenRenOrHigherMembership(actorId, isSharedImmortalHighRealmPath && realmTier >= XjRealmSuppression.TierZiFu);
		SetZhenJunOrHigherMembership(actorId, isSharedImmortalHighRealmPath && realmTier >= XjRealmSuppression.TierJinDan);
		SetShiMembership(actorId, isShi);
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

	private static void SetZhenRenOrHigherMembership(long actorId, bool included)
	{
		if (actorId <= 0L) return;
		bool changed = included ? _zhenRenOrHigherIds.Add(actorId) : _zhenRenOrHigherIds.Remove(actorId);
		if (changed) _zhenRenOrHigherSnapshotDirty = true;
	}

	private static void SetZhenJunOrHigherMembership(long actorId, bool included)
	{
		if (actorId <= 0L) return;
		bool changed = included ? _zhenJunOrHigherIds.Add(actorId) : _zhenJunOrHigherIds.Remove(actorId);
		if (changed) _zhenJunOrHigherSnapshotDirty = true;
	}

	private static void SetShiMembership(long actorId, bool included)
	{
		if (actorId <= 0L) return;
		bool changed = included ? _shiIds.Add(actorId) : _shiIds.Remove(actorId);
		if (changed) _shiSnapshotDirty = true;
	}

	private static void ClearShiMembership()
	{
		_shiIds.Clear();
		_shiSnapshot = Array.Empty<long>();
		_shiSnapshotDirty = false;
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

	private static void ClearZhenRenOrHigherMembership()
	{
		_zhenRenOrHigherIds.Clear();
		_zhenRenOrHigherSnapshot = Array.Empty<long>();
		_zhenRenOrHigherSnapshotDirty = false;
	}

	private static void ClearZhenJunOrHigherMembership()
	{
		_zhenJunOrHigherIds.Clear();
		_zhenJunOrHigherSnapshot = Array.Empty<long>();
		_zhenJunOrHigherSnapshotDirty = false;
	}

	private static void TrackDetectionMembershipChange(long actorId, bool added)
	{
		if (actorId <= 0L) return;
		if (_detectionSnapshotLeased)
		{
			_detectionMembershipDeltas.Add(added ? actorId : -actorId);
			return;
		}
		if (added) AddDetectionMirror(actorId);
		else RemoveDetectionMirror(actorId);
	}

	private static void AddDetectionMirror(long actorId)
	{
		if (actorId <= 0L || _detectionSnapshotIndexByActorId.ContainsKey(actorId)) return;
		_detectionSnapshotIndexByActorId[actorId] = _detectionSnapshotIds.Count;
		_detectionSnapshotIds.Add(actorId);
	}

	private static void RemoveDetectionMirror(long actorId)
	{
		if (!_detectionSnapshotIndexByActorId.TryGetValue(actorId, out int index)) return;
		int lastIndex = _detectionSnapshotIds.Count - 1;
		long lastActorId = _detectionSnapshotIds[lastIndex];
		_detectionSnapshotIds[index] = lastActorId;
		_detectionSnapshotIds.RemoveAt(lastIndex);
		_detectionSnapshotIndexByActorId.Remove(actorId);
		if (index < lastIndex) _detectionSnapshotIndexByActorId[lastActorId] = index;
	}

	private static void ResetDetectionSnapshotMirror()
	{
		// Replace rather than Clear so a detector that is being torn down cannot
		// observe its leased list mutate underneath it during world reset.
		_detectionSnapshotIds = new List<long>();
		_detectionSnapshotIndexByActorId = new Dictionary<long, int>();
		_detectionMembershipDeltas.Clear();
		_detectionSnapshotLeased = false;
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

	private static HashSet<long>[] CreateAnnualAuditBuckets()
	{
		HashSet<long>[] buckets = new HashSet<long>[AnnualAuditBucketCount];
		for (int i = 0; i < buckets.Length; i++)
		{
			buckets[i] = new HashSet<long>();
		}
		return buckets;
	}

	private static bool[] CreateDirtyAnnualAuditFlags()
	{
		bool[] flags = new bool[AnnualAuditBucketCount];
		for (int i = 0; i < flags.Length; i++)
		{
			flags[i] = true;
		}
		return flags;
	}

	private static void AddAnnualAuditMembership(long actorId)
	{
		if (actorId <= 0L) return;
		int bucket = PositiveModulo(actorId, AnnualAuditBucketCount);
		if (_annualAuditIdsByBucket[bucket].Add(actorId))
		{
			_annualAuditSnapshotDirty[bucket] = true;
		}
	}

	private static void RemoveAnnualAuditMembership(long actorId)
	{
		if (actorId <= 0L) return;
		int bucket = PositiveModulo(actorId, AnnualAuditBucketCount);
		if (_annualAuditIdsByBucket[bucket].Remove(actorId))
		{
			_annualAuditSnapshotDirty[bucket] = true;
		}
	}

	private static void ClearAnnualAuditMembership()
	{
		for (int i = 0; i < AnnualAuditBucketCount; i++)
		{
			_annualAuditIdsByBucket[i].Clear();
			_annualAuditSnapshots[i] = Array.Empty<long>();
			_annualAuditSnapshotDirty[i] = false;
		}
	}

	private static int PositiveModulo(long value, int modulus)
	{
		long remainder = value % modulus;
		return (int)(remainder < 0L ? remainder + modulus : remainder);
	}
}
