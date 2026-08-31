using System;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.DengMingShi;

/// <summary>
/// Synthetic actor -> native civilization boundary.
///
/// WorldBox remains the sole authority for City/Kingdom creation. A freshly spawned
/// civilization actor is allowed to be cleanly unattached (no city, no kingdom) until
/// native AI decides to found/join a settlement. XuanJian only reconnects an already-
/// existing City -> Kingdom relation; it never creates a city/kingdom from this helper.
/// </summary>
internal static class XjDengMingShiSpawnSafety
{
	internal static bool EnsureNativeCivilizationState(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive())
		{
			return false;
		}
		if (!IsKingdomCiv(actor))
		{
			return true;
		}
		if (HasCompleteNativeCivilizationState(actor) || IsCleanlyUnattachedCivilization(actor))
		{
			return true;
		}

		return TryRepairExistingCivilizationState(actor);
	}

	internal static bool HasCompleteNativeCivilizationState(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}
		if (!IsKingdomCiv(actor))
		{
			return true;
		}

		try
		{
			City city = actor.city;
			Kingdom kingdom = actor.kingdom;
			return city?.data != null
				&& kingdom?.data != null
				&& city.kingdom?.data != null
				&& ReferenceEquals(city.kingdom, kingdom);
		}
		catch
		{
			return false;
		}
	}

	internal static void TryRemoveInvalidActor(Actor actor)
	{
		XjNativeEntityLifecycleInterop.TryRemoveActor(actor);
	}

	private static bool IsKingdomCiv(Actor actor)
	{
		try
		{
			return actor.isKingdomCiv();
		}
		catch
		{
			return false;
		}
	}

	private static bool IsCleanlyUnattachedCivilization(Actor actor)
	{
		try
		{
			if (actor?.data == null || actor.city != null || actor.kingdom != null)
			{
				return false;
			}

			// Negative/zero native IDs mean the graph is intentionally unattached. Positive
			// IDs with null runtime objects are stale references and must not be normalized
			// into a new civilization as a side effect of spawn/save/recovery.
			return actor.data.cityID <= 0L && actor.data.civ_kingdom_id <= 0L;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryRepairExistingCivilizationState(Actor actor)
	{
		try
		{
			City city = actor.city;
			Kingdom cityKingdom = city?.kingdom;
			if (city?.data == null || cityKingdom?.data == null)
			{
				return false;
			}

			if (!ReferenceEquals(actor.kingdom, cityKingdom))
			{
				actor.setKingdom(cityKingdom);
			}
			return HasCompleteNativeCivilizationState(actor);
		}
		catch
		{
			return false;
		}
	}
}
