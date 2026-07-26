using System;
using ai;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjHighRealmMovement
{
	private const string FlightFlag = "xuanjian.vnext.realm_flight";
	private const float FlightHeight = 6f;

	internal static bool TryHandleGoTo(Actor actor, WorldTile target, ref ExecuteEvent result)
	{
		if (actor?.data == null || target == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier) || !actor.isAlive())
		{
			return false;
		}

		if (realmTier == XjRealmSuppression.TierJinDan)
		{
			ClearFlight(actor);
			actor.current_path.Clear();
			actor.setCurrentTilePosition(target);
			result = ExecuteEvent.True;
			return true;
		}

		if (realmTier != XjRealmSuppression.TierZiFu)
		{
			ClearFlight(actor);
			return false;
		}

		BaseSystemData data = (BaseSystemData)actor.data;
		if (!data.hasFlag(FlightFlag))
		{
			data.addFlag(FlightFlag);
			actor.setFlying(true);
			actor.precalcMovementSpeed(true);
		}

		actor.clearOldPath();
		actor.setTileTarget(target);
		actor.current_path.Add(target);
		result = ExecuteEvent.True;
		return true;
	}

	internal static bool ShouldSkipFall(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)
			|| realmTier != XjRealmSuppression.TierZiFu
			|| !actor.isAlive())
		{
			ClearFlight(actor);
			return false;
		}

		BaseSystemData data = (BaseSystemData)actor.data;
		if (!data.hasFlag(FlightFlag))
		{
			data.addFlag(FlightFlag);
			actor.setFlying(true);
			actor.precalcMovementSpeed(true);
		}
		actor.position_height = FlightHeight;
		return true;
	}

	internal static void ReconcileFlightStateAfterRealmWrite(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)
			|| realmTier != XjRealmSuppression.TierZiFu)
		{
			ClearFlight(actor);
		}
	}

	private static void ClearFlight(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		BaseSystemData data = (BaseSystemData)actor.data;
		if (!data.hasFlag(FlightFlag))
		{
			return;
		}

		data.removeFlag(FlightFlag);
		actor.setFlying(false);
		actor.precalcMovementSpeed(true);
	}
}
