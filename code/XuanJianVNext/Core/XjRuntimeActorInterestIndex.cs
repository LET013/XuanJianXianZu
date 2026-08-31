using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Core;

/// <summary>
/// O(1) actor interest gates for Harmony hot paths. The index is updated only
/// at lifecycle boundaries (registration, realm/FaBao/special-identity writes
/// and death), so ordinary WorldBox actors never enter玄鉴 stat/combat/movement
/// resolution after the bounded post-load bootstrap has completed.
/// </summary>
internal static class XjRuntimeActorInterestIndex
{
	private static readonly HashSet<long> StatActorIds = new HashSet<long>();
	private static readonly HashSet<long> CombatActorIds = new HashSet<long>();
	private static readonly HashSet<long> OrdinaryCombatActorIds = new HashSet<long>();
	private static readonly HashSet<long> OrdinaryAnnualActorIds = new HashSet<long>();
	private static readonly HashSet<long> MovementActorIds = new HashSet<long>();
	private static readonly HashSet<long> FaBaoActorIds = new HashSet<long>();

	internal static int RuntimeMembershipCount => StatActorIds.Count + CombatActorIds.Count
		+ OrdinaryCombatActorIds.Count + OrdinaryAnnualActorIds.Count + MovementActorIds.Count + FaBaoActorIds.Count;

	internal static bool HasStatInterest(long actorId)
	{
		return actorId > 0L && StatActorIds.Contains(actorId);
	}

	internal static bool HasCombatInterest(long actorId)
	{
		return actorId > 0L && CombatActorIds.Contains(actorId);
	}

	internal static bool HasOrdinaryCombatClassification(long actorId)
	{
		return actorId > 0L && OrdinaryCombatActorIds.Contains(actorId);
	}

	internal static bool HasOrdinaryAnnualClassification(long actorId)
	{
		return actorId > 0L && OrdinaryAnnualActorIds.Contains(actorId);
	}

	internal static void MarkOrdinaryAnnual(long actorId)
	{
		if (actorId <= 0L || StatActorIds.Contains(actorId)) return;
		XjStaleActorIdEviction.Track(actorId);
		OrdinaryAnnualActorIds.Add(actorId);
	}

	internal static void InvalidateAnnualClassification(long actorId)
	{
		if (actorId > 0L) OrdinaryAnnualActorIds.Remove(actorId);
	}

	internal static bool HasMovementInterest(long actorId)
	{
		return actorId > 0L && MovementActorIds.Contains(actorId);
	}

	/// <summary>
	/// getHit 冷分支的自愈入口。正常命中只读 HashSet；仅当索引缺失时，
	/// 才检查角色落盘标记并重建修士/特殊身份索引。这样可覆盖旧档旁路、
	/// 外部特质编辑和少数未经过标准注册入口的角色，避免玄鉴角色被误走
	/// 原生战斗而绕过境界压制。
	/// </summary>
	internal static bool TryRepairCombatInterest(Actor actor, out int realmTier)
	{
		realmTier = XjRealmSuppression.TierNone;
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L)
		{
			return false;
		}

		if (HasCombatInterest(actorId))
		{
			if (!XjCultivatorCache.TryGetRealmTier(actorId, out realmTier))
			{
				realmTier = XjRealmSuppression.GetRealmTier(actor);
			}
			return true;
		}

		// 普通角色命中负缓存后直接返回，不再在每次受击时重复解析
		// 境界、法宝、龙属与妖邪标记。状态变化入口会主动失效该分类。
		if (HasOrdinaryCombatClassification(actorId))
		{
			return false;
		}

		// 只在正负索引都缺失时进入一次较重的落盘标记检查。
		realmTier = XjRealmSuppression.GetRealmTier(actor);
		if (realmTier <= XjRealmSuppression.TierNone
			&& !MayRequireBootstrapInspection(actor))
		{
			OrdinaryCombatActorIds.Add(actorId);
			return false;
		}

		XjCultivatorCache.CheckAndUpdate(actor);
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out realmTier))
		{
			realmTier = XjRealmSuppression.GetRealmTier(actor);
		}
		bool interested = HasCombatInterest(actorId) || realmTier > XjRealmSuppression.TierNone;
		SetCombatClassification(actorId, interested);
		return interested;
	}

	internal static bool HasFaBaoInterest(long actorId)
	{
		return actorId > 0L && FaBaoActorIds.Contains(actorId);
	}

	internal static bool HasAnyFaBaoInterest => FaBaoActorIds.Count > 0;

	internal static List<long> GetFaBaoActorIdsSnapshot()
	{
		return FaBaoActorIds.Count == 0 ? new List<long>(0) : new List<long>(FaBaoActorIds);
	}

	/// <summary>
	/// 仅供读档 bootstrap 尚未完成时的 updateStats 快速门控。它只检查
	/// 已落盘的玄鉴标记，不解析功法、家族、宗门或装备索引。
	/// </summary>
	internal static bool MayRequireBootstrapInspection(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		long actorId = GetActorId(actor);
		return HasStatInterest(actorId)
			|| XjXingDuQianPhantomSystem.HasPersistedMarker(actor)
			|| XjHouShenShuSystem.IsShenShi(actor)
			|| HasAlchemyLifespanBonus(actor)
			|| XjCultivationEligibility.HasCultivationMarkers(actor)
			|| XjCultivationPathRules.IsShi(actor)
			|| XjLongShuSystem.IsLongShu(actor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor)
			|| XjXianGuoSystem.HasInstitutionalPatronageMarker(actor)
			|| XjFaBaoAccessor.HasState(actor);
	}

	internal static void Observe(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			Remove(GetActorId(actor));
			return;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L)
		{
			return;
		}
		XjStaleActorIdEviction.Track(actorId);

		bool isCultivator = XjCultivatorCache.IsCultivator(actorId);
		int realmTier = XjCultivatorCache.TryGetRealmTier(actorId, out int cachedTier)
			? cachedTier
			: XjRealmSuppression.TierNone;
		bool hasFaBao = XjFaBaoAccessor.HasState(actor) || XjFaBaoEquipmentSync.HasAnyEquippedFaBao(actor);
		bool isXingDuQianPhantom = XjXingDuQianPhantomSystem.IsPhantom(actor);
		bool hasSpecialRuntimeIdentity = XjTrueDamageSystem.IsJinXingYaoXie(actor)
			|| XjLongShuSystem.IsLongShu(actor)
			|| XjHouShenShuSystem.IsShenShi(actor)
			|| XjXianGuoSystem.HasInstitutionalPatronageMarker(actor)
			|| HasAlchemyLifespanBonus(actor)
			|| hasFaBao
			|| isXingDuQianPhantom;
		bool hasRuntimeInterest = isCultivator || realmTier > XjRealmSuppression.TierNone || hasSpecialRuntimeIdentity;

		SetMembership(StatActorIds, actorId, hasRuntimeInterest);
		if (hasRuntimeInterest) OrdinaryAnnualActorIds.Remove(actorId);
		SetCombatClassification(actorId, hasRuntimeInterest);
		// 形渡阡幻身使用原生 AI 移动，不得因为固定高境位格进入玄鉴高境瞬移车道。
		SetMembership(MovementActorIds, actorId,
			realmTier >= XjRealmSuppression.TierZiFu
			&& !XjCultivationPathRules.IsShi(actor)
			&& !isXingDuQianPhantom);
		SetMembership(FaBaoActorIds, actorId, hasFaBao);
	}

	internal static void UpdateRealmTier(long actorId, int realmTier)
	{
		if (actorId <= 0L)
		{
			return;
		}

		if (realmTier > XjRealmSuppression.TierNone)
		{
			StatActorIds.Add(actorId);
			OrdinaryAnnualActorIds.Remove(actorId);
			SetCombatClassification(actorId, true);
		}
		bool isShi = XjActorRegistry.Resolve(actorId, out Actor actor)
			&& XjCultivationPathRules.IsShi(actor);
		SetMembership(MovementActorIds, actorId, realmTier >= XjRealmSuppression.TierZiFu && !isShi);
	}

	internal static void InvalidateCombatClassification(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		CombatActorIds.Remove(actorId);
		OrdinaryCombatActorIds.Remove(actorId);
	}

	internal static void InvalidateCombatClassification(Actor actor)
	{
		InvalidateCombatClassification(GetActorId(actor));
	}

	internal static void Remove(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		StatActorIds.Remove(actorId);
		CombatActorIds.Remove(actorId);
		OrdinaryCombatActorIds.Remove(actorId);
		OrdinaryAnnualActorIds.Remove(actorId);
		MovementActorIds.Remove(actorId);
		FaBaoActorIds.Remove(actorId);
	}

	internal static void Clear()
	{
		StatActorIds.Clear();
		CombatActorIds.Clear();
		OrdinaryCombatActorIds.Clear();
		OrdinaryAnnualActorIds.Clear();
		MovementActorIds.Clear();
		FaBaoActorIds.Clear();
	}

	private static void SetCombatClassification(long actorId, bool interested)
	{
		if (actorId <= 0L)
		{
			return;
		}
		XjStaleActorIdEviction.Track(actorId);

		if (interested)
		{
			CombatActorIds.Add(actorId);
			OrdinaryCombatActorIds.Remove(actorId);
		}
		else
		{
			CombatActorIds.Remove(actorId);
			OrdinaryCombatActorIds.Add(actorId);
		}
	}

	private static bool HasAlchemyLifespanBonus(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyLifespanBonus, out int bonus)
			&& bonus > 0;
	}

	private static void SetMembership(HashSet<long> set, long actorId, bool included)
	{
		if (included)
		{
			set.Add(actorId);
		}
		else
		{
			set.Remove(actorId);
		}
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
