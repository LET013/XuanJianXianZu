using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjManualCultivationWake
{
	internal static void EnsureAwake(Actor actor, bool ensureMinimumAptitude = false, bool registerActor = true)
	{
		if (actor?.data == null
			|| !actor.isAlive()
			|| !XjCultivationEligibility.CanCultivate(actor))
		{
			return;
		}

		XjCultivationSeed.EnsureSeedState(actor);
		if (ensureMinimumAptitude)
		{
			EnsureMinimumAptitude(actor);
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			&& aptitude >= 1
			&& aptitude <= 6)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		}

		if (!XjCultivatorCache.CheckAndUpdate(actor))
		{
			return;
		}

		if (registerActor)
		{
			XjScheduler.RegisterActor(actor);
		}
		XjScheduler.EnsureRuntimeIndexesForActor(actor);
		XjScheduler.EnqueueAnnualActor(actor);
	}

	private static void EnsureMinimumAptitude(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (aptitude >= 1 && aptitude <= 6)
		{
			return;
		}

		const int minimumManualAptitude = 1;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, minimumManualAptitude);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(actor, minimumManualAptitude);
		XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(actor, minimumManualAptitude);
		XjVisibleTraitSync.SyncAptitudeTrait(actor, minimumManualAptitude);
	}
}
