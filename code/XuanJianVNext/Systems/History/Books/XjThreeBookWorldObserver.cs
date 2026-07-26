using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.History.Books;

/// <summary>
/// 三书世界级年度观察器。只读取宗门仓库、宗门档案和最多128条敌意记录；
/// 不扫描角色，不保存重复状态，里程碑与联盟去重完全复用宗门纪事来源事实ID。
/// </summary>
internal static class XjThreeBookWorldObserver
{
	private const int AllianceHostilityThreshold = 80;
	private const int MaxAllianceEventsPerYear = 3;

	internal static void TickYear(int currentYear)
	{
		if (currentYear <= 0) return;
		XjThreeBookDeferredFamilyFacts.TickYear(currentYear);
		RecordResourceMilestones(currentYear);
		RecordCommonEnemyAlliances(currentYear);
		XjThreeBookDiagnostics.AuditYear(currentYear);
	}

	private static void RecordResourceMilestones(int currentYear)
	{
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord sect = sects[i];
			if (sect == null || sect.SectId <= 0L || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
				|| !XjSectRepository.HasValidSectCity(sect.SectId)) continue;
			int cityCount = XjSectRepository.CountValidSectCities(sect.SectId);
			int familyCount = sect.FamilyIds?.Count ?? 0;
			int peakCount = sect.Peaks?.Count ?? 0;
			int inheritanceCount = XjZongMenGongFaPavilion.ReadGongFaEntries(sect.SectId).Count
				+ XjZongMenGongFaPavilion.ReadQiuJinFaEntries(sect.SectId).Count;
			int treasureCount = XjFamilyFaBaoWarehouse.CountSectEntries(sect.SectId);
			bool hasFormation = sect.FormationId > 0L;
			bool hasSecretRealm = sect.SecretRealmId > 0L;
			int score = cityCount * 3 + familyCount * 2 + peakCount * 2 + inheritanceCount * 2 + treasureCount * 3
				+ (hasFormation ? 6 : 0) + (hasSecretRealm ? 8 : 0);
			int tier = score >= 70 ? 4 : score >= 40 ? 3 : score >= 20 ? 2 : score >= 8 ? 1 : 0;
			if (tier <= 0) continue;
			string source = "sect|resource-milestone|" + sect.SectId + "|" + tier;
			if (XjSectChronicleStore.ContainsSourceFact(source)) continue;
			XjThreeBookWriter.RecordSectResourceMilestone(
				sect.SectId, sect.Name, currentYear, tier, cityCount, familyCount, peakCount,
				inheritanceCount, treasureCount, hasFormation, hasSecretRealm);
		}
	}

	private static void RecordCommonEnemyAlliances(int currentYear)
	{
		IReadOnlyList<XjSectHostilityArchiveRecord> hostilities = XjSectWarSystem.ReadHostilities();
		if (hostilities.Count < 2) return;
		Dictionary<long, List<long>> hostileSectsByEnemy = new Dictionary<long, List<long>>();
		for (int i = 0; i < hostilities.Count; i++)
		{
			XjSectHostilityArchiveRecord record = hostilities[i];
			if (record == null || record.Hostility < AllianceHostilityThreshold) continue;
			AddEnemyRelation(hostileSectsByEnemy, record.LeftSectId, record.RightSectId);
			AddEnemyRelation(hostileSectsByEnemy, record.RightSectId, record.LeftSectId);
		}

		int emitted = 0;
		foreach (KeyValuePair<long, List<long>> entry in hostileSectsByEnemy)
		{
			long commonEnemyId = entry.Key;
			List<long> candidates = entry.Value;
			if (candidates == null || candidates.Count < 2) continue;
			candidates.Sort();
			for (int i = 0; i < candidates.Count - 1 && emitted < MaxAllianceEventsPerYear; i++)
			{
				for (int j = i + 1; j < candidates.Count && emitted < MaxAllianceEventsPerYear; j++)
				{
					long leftId = candidates[i];
					long rightId = candidates[j];
					if (leftId <= 0L || rightId <= 0L || leftId == rightId
						|| XjSectWarSystem.GetHostility(leftId, rightId) > 0
						|| !XjSectRepository.HasValidSectCity(leftId)
						|| !XjSectRepository.HasValidSectCity(rightId)) continue;
					string period = (currentYear / 100).ToString();
					string source = "sect|alliance|" + leftId + "|" + rightId + "|" + commonEnemyId + "|" + period;
					if (XjSectChronicleStore.ContainsSourceFact(source)) continue;
					XjThreeBookWriter.RecordSectAlliancePair(
						leftId, ResolveSectName(leftId), rightId, ResolveSectName(rightId),
						commonEnemyId, ResolveSectName(commonEnemyId), currentYear);
					emitted++;
				}
			}
			if (emitted >= MaxAllianceEventsPerYear) break;
		}
	}

	private static void AddEnemyRelation(Dictionary<long, List<long>> map, long commonEnemyId, long hostileSectId)
	{
		if (commonEnemyId <= 0L || hostileSectId <= 0L || commonEnemyId == hostileSectId) return;
		if (!map.TryGetValue(commonEnemyId, out List<long> list))
		{
			list = new List<long>();
			map[commonEnemyId] = list;
		}
		if (!list.Contains(hostileSectId)) list.Add(hostileSectId);
	}

	private static string ResolveSectName(long sectId)
	{
		return sectId > 0L && XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)
			&& sect != null && !string.IsNullOrWhiteSpace(sect.Name) ? sect.Name.Trim() : "某宗";
	}
}
