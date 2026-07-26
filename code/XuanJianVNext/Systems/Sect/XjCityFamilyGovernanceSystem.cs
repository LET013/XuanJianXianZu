using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Deterministic first-pass assignment of local governing families when a
/// sect gains city territory. It only fills empty city records. Later replacement
/// must go through the governance/challenge pipeline and cannot oscillate with
/// a yearly ranking refresh. Sect territory is city-driven; the kingdom argument
/// is kept only for older call sites and is intentionally ignored.
/// </summary>
internal static class XjCityFamilyGovernanceSystem
{

	internal static void InitializeForSect(Kingdom kingdom, XjSectArchiveRecord sect, long founderFamilyId, int currentYear)
	{
		if (sect == null || sect.SectId <= 0L || sect.CityIds == null || sect.CityIds.Count == 0) return;
		for (int i = 0; i < sect.CityIds.Count; i++)
		{
			City city = ResolveCityById(sect.CityIds[i]);
			if (city?.data == null) continue;
			long cityId = city.data.id;
			if (XjSectRepository.TryGetGovernance(cityId, out XjCityFamilyGovernanceArchiveRecord existing))
			{
				XjSectRepository.TryUpdateGovernanceSect(cityId, sect.SectId, currentYear, XjCityFamilyGovernanceState.Stable);
				if (cityId == sect.CapitalCityId && founderFamilyId > 0L && existing.GoverningFamilyId != founderFamilyId)
				{
					XjSectRepository.TryReplaceGovernance(cityId, founderFamilyId, currentYear, XjCityFamilyGovernanceState.Stable);
				}
				continue;
			}
			long familyId = SelectLocalFamily(city, founderFamilyId, cityId == sect.CapitalCityId);
			if (familyId <= 0L) continue;
			XjSectRepository.TryAssignGovernance(sect.SectId, cityId, familyId, currentYear, XjCityFamilyGovernanceState.Stable);
		}
	}

	internal static long SelectProminentFamily(City city)
	{
		return SelectLocalFamily(city, 0L, false);
	}

	private static long SelectLocalFamily(City city, long founderFamilyId, bool isCapital)
	{
		if (city?.data == null) return 0L;
		IReadOnlyList<XjCityFamilyCultivatorAggregate> aggregates = XjZongMenCultivatorCityIndex.ReadFamilyAggregates(city);
		long bestFamilyId = 0L;
		long bestScore = long.MinValue;
		for (int i = 0; i < aggregates.Count; i++)
		{
			XjCityFamilyCultivatorAggregate candidate = aggregates[i];
			if (candidate.FamilyId <= 0L || candidate.MemberCount <= 0) continue;
			long score = (long)candidate.TotalRealmWeight * 55L
				+ (long)candidate.HighestRealmOrder * 200L
				+ (long)candidate.MemberCount * 25L;
			if (bestFamilyId == 0L || score > bestScore || (score == bestScore && candidate.FamilyId < bestFamilyId))
			{
				bestFamilyId = candidate.FamilyId;
				bestScore = score;
			}
		}

		if (founderFamilyId > 0L)
		{
			long founderFloor = (long)RealmWeight(XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) * 55L
				+ (long)XjRealmHelper.GetOrder(XjRealmIds.ZiFu) * 200L + 25L;
			if (isCapital
				|| bestFamilyId == 0L
				|| founderFloor > bestScore
				|| (founderFloor == bestScore && founderFamilyId < bestFamilyId))
			{
				return founderFamilyId;
			}
		}
		return bestFamilyId;
	}

	private static int RealmWeight(int order)
	{
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.JinDan)) return 2000;
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) return 800;
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZhuJi)) return 300;
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.LianQi)) return 120;
		return 10;
	}

	private static City ResolveCityById(long cityId)
	{
		return XjWorldLookupIndex.TryResolveCity(cityId, out City city) ? city : null;
	}
}
