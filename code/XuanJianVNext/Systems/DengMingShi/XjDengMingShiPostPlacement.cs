using System;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Rank;

namespace XuanJianVNext.Systems.DengMingShi;

internal static class XjDengMingShiPostPlacement
{
	internal static void Reconcile(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive())
		{
			return;
		}

		// 故尊命痕不是完整的当世修士：不进年度修炼、家族、宗门、果位重建或仙鉴索引。
		// 其修为已由档案快照一次性恢复，之后只作为世界中的短暂显化参与普通行动/战斗。
		if (XjGuZunRegistry.IsManifestationActor(actor))
		{
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId > 0L)
			{
				XjCultivatorCache.Remove(actorId);
				XjGuoWeiRegistry.RemoveEphemeralClaims(actorId);
				XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
			}
			XjRankSortSystem.ClearCache();
			return;
		}

		XjCultivationEligibility.RecordManualCultivationGrant(actor);
		if (!XjCultivationEligibility.CanCultivate(actor))
		{
			return;
		}

		RestoreAptitudeFromVisibleTrait(actor);
		XjIdentityTraitLifecycle.TryReconcileDaoTuFromVisibleTraits(actor);
		XjManualCultivationWake.EnsureAwake(actor, NeedsMinimumAptitudeWake(actor));
		XjCultivatorCache.CheckAndUpdate(actor);
		XjScheduler.RegisterAndEnqueueAnnualActor(actor);
		XjScheduler.EnsureRuntimeIndexesForActor(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		XjRankSortSystem.ClearCache();

		int year = Math.Max(1, XjYearTracker.CurrentYear);
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			// 服气过程数据保持独立，但真人/真君的宗门、家族、果位与不朽等
			// 外部结果必须按紫府/金丹等价恢复。本命功法只能保留一部，不能清空。
			XjFuQiMethodRules.ReconcileExclusiveZiJinData(actor);
			if (XjFuQiCoreRouter.TryResolveActorCore(actor, out XuanJianVNext.Data.Cultivation.XjFuQiCoreDefinition definition))
			{
				XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, year);
			}
			XjFuQiCandidateIndex.Observe(actor);
			XjActorRegistry.Register(actor, out _);
			int fuQiTier = XjRealmSuppression.GetRealmTier(actor);
			bool fuQiHasStableCity = actor.city?.data != null;
			if (fuQiTier >= XjRealmSuppression.TierZiFu)
			{
				XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
			}
			if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
				|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
			{
				XjHighRealmRehydration.ReconcileActor(actor, externalSpawn: true);
				XjJinDanImmortalityRegistry.EnsureActivated(actor, year);
				if (fuQiHasStableCity) XjSectCityData.HandleJinDanPromotion(actor, year);
			}
			else if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
			{
				if (fuQiHasStableCity) XjSectCityData.HandleZiFuPromotion(actor, year);
			}
			XjScheduler.EnsureRuntimeIndexesForActor(actor);
			XjRuntimeActorInterestIndex.Observe(actor);
			XjRankSortSystem.ClearCache();
			return;
		}

		if (!XjCultivationPathRules.IsZiFuJinDan(actor)) return;
		XjXianJiAccessor.ReconcileRealmLimit(actor);
		XjActorGongFaCollection.ReconcileWithActor(actor, "DengMingShiPostPlacement");
		XjGongFaWarehouseReconciler.ReconcileActor(actor, year);
		int tier = XjRealmSuppression.GetRealmTier(actor);
		if (tier < XjRealmSuppression.TierZiFu) return;

		bool hasStableCity = actor.city?.data != null;
		XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
		XjActorRegistry.Register(actor, out _);

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			XjHighRealmRehydration.ReconcileActor(actor, externalSpawn: true);
			XjGuoWeiRegistry.ReconcileLiveActor(actor);
			XjGuoWeiQuanBingRegistry.ReconcileLiveActorReadOnly(actor);
			XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(actor);
			XjJinDanImmortalityRegistry.EnsureActivated(actor, year);
			if (hasStableCity) XjSectCityData.HandleJinDanPromotion(actor, year);
		}
		else if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			if (hasStableCity) XjSectCityData.HandleZiFuPromotion(actor, year);
		}

		XjScheduler.EnsureRuntimeIndexesForActor(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		XjRankSortSystem.ClearCache();
	}

	private static void RestoreAptitudeFromVisibleTrait(Actor actor)
	{
		if (actor?.data == null
			|| (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int existing) && existing > 0))
		{
			return;
		}

		int restored = ResolveVisiblePrimaryAptitude(actor);
		if (restored <= 0)
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, restored);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(actor, restored);
		XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(actor, restored);
		XjVisibleTraitSync.SyncAptitudeTrait(actor, restored);
	}

	private static int ResolveVisiblePrimaryAptitude(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}

		for (int rank = 6; rank >= 1; rank--)
		{
			if (actor.hasTrait("XjZz" + rank))
			{
				return rank;
			}
		}

		return 0;
	}

	private static bool NeedsMinimumAptitudeWake(Actor actor)
	{
		if (actor?.data == null
			|| (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude) && aptitude > 0))
		{
			return false;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& XjRealmHelper.IsKnownTag(realmId))
		{
			return true;
		}

		return actor.hasTrait(XjRealmIds.TaiXi)
			|| actor.hasTrait(XjRealmIds.LianQi)
			|| actor.hasTrait(XjRealmIds.ZhuJi)
			|| actor.hasTrait(XjRealmIds.HuangGuan)
			|| actor.hasTrait(XjRealmIds.ZiFu)
			|| actor.hasTrait(XjRealmIds.FuQiZhenRen)
			|| actor.hasTrait(XjRealmIds.JinDan)
			|| actor.hasTrait(XjRealmIds.ZhenJunYuShi)
			|| actor.hasTrait(XjRealmIds.ShenDan);
	}
}
