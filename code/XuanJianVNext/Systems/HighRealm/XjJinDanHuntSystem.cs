using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 金丹对异道途紫府的主动压制。年度运行、每名金丹最多检查少量缓存候选，
/// 只负责把原生战斗目标交给 WorldBox，不重写原生寻路或伤害流程。
/// </summary>
internal static class XjJinDanHuntSystem
{
	private const int IntervalYears = 3;
	private const int CandidateBudget = 24;
	private const int HuntChancePercent = 35;

	internal static void TickAnnual(Actor hunter, in XjActorCultivationSnapshot hunterSnapshot, int currentYear)
	{
		if (hunter?.data == null
			|| currentYear <= 0
			|| (currentYear % IntervalYears) != 0
			|| string.IsNullOrWhiteSpace(hunterSnapshot.DaoTu)
			|| XjYinSiTraitLifecycle.IsYinSi(hunter)
			|| XjTrueDamageSystem.IsJinXingYaoXie(hunter))
		{
			return;
		}

		long hunterId = ((BaseSystemData)hunter.data).id;
		if (hunterId <= 0L || XjDeterministicHash.PositiveIndex(hunterId + currentYear, "jindan_hunt_trigger", 100) >= HuntChancePercent)
		{
			return;
		}

		IReadOnlyList<long> ids = XjCultivatorCache.GetZhenRenOrHigherIds();
		if (ids == null || ids.Count == 0)
		{
			return;
		}

		int start = XjDeterministicHash.PositiveIndex(hunterId + currentYear, "jindan_hunt_target", ids.Count);
		int limit = Math.Min(ids.Count, CandidateBudget);
		for (int offset = 0; offset < limit; offset++)
		{
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L
				|| candidateId == hunterId
				|| !XjCultivatorCache.TryGetRealmTier(candidateId, out int tier)
				|| tier != XjRealmSuppression.TierZiFu
				|| !XjActorRegistry.ResolveKnownOrWorld(candidateId, out Actor target)
				|| target?.data == null
				|| !target.isAlive()
				|| XjYinSiTraitLifecycle.IsYinSi(target)
				|| XjTrueDamageSystem.IsJinXingYaoXie(target))
			{
				continue;
			}

			XjActorCultivationSnapshot targetSnapshot = XjActorCultivationSnapshotBuilder.Build(target);
			if (string.IsNullOrWhiteSpace(targetSnapshot.DaoTu)
				|| string.Equals(hunterSnapshot.DaoTu, targetSnapshot.DaoTu, StringComparison.Ordinal))
			{
				continue;
			}

			hunter.attackedBy = target;
			XjActorAggroBridge.ForceAggro(hunter, target);
			return;
		}
	}
}
