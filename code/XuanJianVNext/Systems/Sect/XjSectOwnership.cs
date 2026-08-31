using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Sect ownership is keyed by SectId. Kingdom is only a native shell for map
/// display, diplomacy animation, and legacy save data.
/// </summary>
internal static class XjSectOwnership
{
	internal static bool TryResolve(Actor actor, out XjSectArchiveRecord sect)
	{
		return XjSectRepository.TryGetByActor(actor, out sect);
	}

	internal static bool TryResolve(City city, out XjSectArchiveRecord sect)
	{
		return XjSectRepository.TryGetByCity(city, out sect);
	}

	internal static bool TryResolve(long sectId, out XjSectArchiveRecord sect)
	{
		return XjSectRepository.TryGetBySectId(sectId, out sect)
			&& sect != null
			&& sect.SectId > 0L
			&& !string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal);
	}

	internal static long ResolveSectId(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor);
	}

	internal static long ResolveSectId(City city)
	{
		return TryResolve(city, out XjSectArchiveRecord sect) ? sect.SectId : 0L;
	}

	internal static bool TryResolvePrimaryCity(long sectId, out City city)
	{
		city = null;
		return sectId > 0L
			&& XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)
			&& TryResolvePrimaryCity(sect, out city);
	}

	internal static bool TryResolvePrimaryCity(XjSectArchiveRecord sect, out City city)
	{
		city = null;
		if (sect == null || sect.SectId <= 0L) return false;
		if (sect.CapitalCityId > 0L
			&& XjWorldLookupIndex.TryResolveCity(sect.CapitalCityId, out city)
			&& city?.data != null) return true;
		if (sect.CityIds != null)
		{
			for (int i = 0; i < sect.CityIds.Count; i++)
			{
				if (XjWorldLookupIndex.TryResolveCity(sect.CityIds[i], out city) && city?.data != null) return true;
			}
		}
		city = null;
		return false;
	}

	internal static bool IsSameSect(Actor actor, City city)
	{
		long actorSectId = ResolveSectId(actor);
		long citySectId = ResolveSectId(city);
		return IsSameSect(actorSectId, citySectId);
	}

	internal static bool IsSameSect(long leftSectId, long rightSectId)
	{
		return leftSectId > 0L && leftSectId == rightSectId;
	}
}
