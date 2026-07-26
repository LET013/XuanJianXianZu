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
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.Rank;

namespace XuanJianVNext.Systems.DengMingShi;

internal static class XjDengMingShiPostPlacement
{
	internal static void Reconcile(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive())
		{
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
		XjXianJiAccessor.ReconcileRealmLimit(actor);
		XjActorGongFaCollection.ReconcileWithActor(actor, "DengMingShiPostPlacement");
		XjGongFaWarehouseReconciler.ReconcileActor(actor, year);

		int tier = XjRealmSuppression.GetRealmTier(actor);
		if (tier < XjRealmSuppression.TierZiFu)
		{
			return;
		}

		bool hasStableCity = actor.city?.data != null;
		XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
		XjActorRegistry.Register(actor, out _);

		if (tier >= XjRealmSuppression.TierJinDan)
		{
			XjHighRealmRehydration.ReconcileActor(actor, externalSpawn: true);
			XjGuoWeiRegistry.ReconcileLiveActor(actor);
			XjGuoWeiQuanBingRegistry.ReconcileLiveActorReadOnly(actor);
			XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(actor);
			XjJinDanImmortalityRegistry.EnsureActivated(actor, year);
			if (hasStableCity)
			{
				XjZongMenCityData.HandleJinDanPromotion(actor, year);
			}
		}
		else
		{
			if (hasStableCity)
			{
				XjZongMenCityData.HandleZiFuPromotion(actor, year);
			}
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
			|| actor.hasTrait(XjRealmIds.ZiFu)
			|| actor.hasTrait(XjRealmIds.JinDan);
	}
}
