using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Craft;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门地域资源使用“城市稳定资源型 + 五年共务轻量产出”的抽象。
/// 它借原著中山泽药材、灵脉、灵矿、火脉、灵泉、沃土等地理差异的思路，
/// 但不虚构一套逐格资源模拟，也不扫描地图。资源型由 CityId 稳定决定，
/// 土地易手后收益自然随宗门归属转移，从而让现有草药/灵物/阵材库存真正与疆域相连。
/// </summary>
internal static class XjRegionalResourceSystem
{
	private const int MaxNodesPerDuty = 3;

	private enum ResourceProfile : byte
	{
		LingMai = 0,
		ShanZeYaoGu = 1,
		LingKuang = 2,
		DiHuo = 3,
		LingQuan = 4,
		LingTian = 5
	}

	internal static string ApplySectTerritoryYield(
		XjSectArchiveRecord sect,
		int currentYear,
		XjAlchemyOwnerKey alchemyOwner,
		XjCraftOwnerKey craftOwner)
	{
		if (sect?.CityIds == null || sect.CityIds.Count == 0 || sect.SectId <= 0L || currentYear <= 0)
		{
			return string.Empty;
		}

		int cityCount = sect.CityIds.Count;
		int nodeCount = Math.Min(MaxNodesPerDuty, cityCount);
		int start = XjDeterministicHash.PositiveIndex(sect.SectId + currentYear, "sect.regional.resource.start", cityCount);
		int lowHerbs = 0;
		int highHerbs = 0;
		int spiritMaterial = 0;
		int formationFlag = 0;
		int formationPlate = 0;
		List<string> profileNames = new List<string>(nodeCount);

		for (int i = 0; i < nodeCount; i++)
		{
			long cityId = sect.CityIds[(start + i) % cityCount];
			if (cityId <= 0L) continue;
			ResourceProfile profile = ResolveProfile(cityId);
			profileNames.Add(GetProfileName(profile));
			switch (profile)
			{
				case ResourceProfile.LingMai:
					spiritMaterial += 1;
					lowHerbs += 1;
					break;
				case ResourceProfile.ShanZeYaoGu:
					lowHerbs += 2;
					if (XjDeterministicHash.Roll01(cityId, currentYear, "regional.herb.high", "sect.regional.resource") < 0.25f) highHerbs += 1;
					break;
				case ResourceProfile.LingKuang:
					spiritMaterial += 2;
					formationFlag += 1;
					break;
				case ResourceProfile.DiHuo:
					spiritMaterial += 1;
					if (XjDeterministicHash.Roll01(cityId, currentYear, "regional.fire.plate", "sect.regional.resource") < 0.35f) formationPlate += 1;
					break;
				case ResourceProfile.LingQuan:
					lowHerbs += 1;
					if (XjDeterministicHash.Roll01(cityId, currentYear, "regional.spring.high", "sect.regional.resource") < 0.20f) highHerbs += 1;
					break;
				case ResourceProfile.LingTian:
					lowHerbs += 2;
					break;
			}
		}

		// 地域资源只做“加一点真实库存”的五年级收益，不制造高品资源洪水。
		if (lowHerbs > 0 || highHerbs > 0)
		{
			XjAlchemyInventoryRegistry.TryAddAnnualMaterials(alchemyOwner, lowHerbs, highHerbs, currentYear);
		}
		if (spiritMaterial > 0)
		{
			XjCraftDomainRegistry.TryAddResource(craftOwner, XjCraftCollaborationSystem.SpiritMaterial, spiritMaterial, currentYear);
		}
		if (formationFlag > 0)
		{
			XjCraftDomainRegistry.TryAddResource(craftOwner, XjCraftCollaborationSystem.FormationFlagLow, formationFlag, currentYear);
		}
		if (formationPlate > 0)
		{
			XjCraftDomainRegistry.TryAddResource(craftOwner, XjCraftCollaborationSystem.FormationPlateLow, formationPlate, currentYear);
		}

		if (profileNames.Count == 0) return string.Empty;
		return "辖地取资：" + string.Join("、", profileNames)
			+ "（药材" + lowHerbs + "、上品药材" + highHerbs + "、灵材" + spiritMaterial + "）";
	}

	internal static string GetStableProfileName(long cityId)
	{
		return cityId <= 0L ? "未定" : GetProfileName(ResolveProfile(cityId));
	}

	private static ResourceProfile ResolveProfile(long cityId)
	{
		return (ResourceProfile)XjDeterministicHash.PositiveIndex(cityId, "xuanjian.regional.resource.profile", 6);
	}

	private static string GetProfileName(ResourceProfile profile)
	{
		return profile switch
		{
			ResourceProfile.LingMai => "灵脉福地",
			ResourceProfile.ShanZeYaoGu => "山泽药谷",
			ResourceProfile.LingKuang => "灵矿砂脉",
			ResourceProfile.DiHuo => "地火火脉",
			ResourceProfile.LingQuan => "灵泉水眼",
			ResourceProfile.LingTian => "灵田沃土",
			_ => "山泽地脉"
		};
	}
}
