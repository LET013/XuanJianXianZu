using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.History.Books;

/// <summary>
/// 三书世界级五年摘要观察器。重大事件由发生源即时记史；这里只读取宗门仓库、宗门档案和最多128条敌意记录；
/// 不扫描角色，不保存重复状态，里程碑与联盟去重完全复用宗门纪事来源事实ID。
/// </summary>
internal static class XjThreeBookWorldObserver
{
	private const int AllianceHostilityThreshold = 80;
	private const int MaxAllianceEventsPerYear = 3;

	private enum AnnualPhase : byte
	{
		None = 0,
		DeferredFamilyFacts = 1,
		ResourceMilestones = 2,
		CommonEnemyAlliances = 3,
		Diagnostics = 4
	}

	private static readonly List<long> PendingAnnualSectIds = new List<long>();
	private static AnnualPhase _annualPhase;
	private static int _annualYear;
	private static int _annualSectIndex;

	internal static bool HasPendingForYear(int currentYear)
	{
		return currentYear > 0 && _annualYear == currentYear && _annualPhase != AnnualPhase.None;
	}

	internal static bool BeginYear(int currentYear)
	{
		if (currentYear <= 0) return false;
		if (HasPendingForYear(currentYear)) return true;
		ClearPendingAnnualWork();
		_annualYear = currentYear;
		_annualPhase = AnnualPhase.DeferredFamilyFacts;
		XjSectRepository.CopyEstablishedSectIdsTo(PendingAnnualSectIds);
		_annualSectIndex = 0;
		return true;
	}

	internal static bool TickPending(int itemBudget = 3, double timeBudgetMs = 0.32d)
	{
		if (_annualYear <= 0 || _annualPhase == AnnualPhase.None) return true;
		XjCooperativeBudget budget = new XjCooperativeBudget(
			Math.Max(1, itemBudget),
			timeBudgetMs,
			XjRuntimeFramePriority.Background);

		while (_annualPhase != AnnualPhase.None && !budget.ShouldYield)
		{
			if (_annualPhase == AnnualPhase.DeferredFamilyFacts)
			{
				if (!budget.TryTake()) return false;
				XjThreeBookDeferredFamilyFacts.TickYear(_annualYear);
				_annualPhase = AnnualPhase.ResourceMilestones;
				continue;
			}

			if (_annualPhase == AnnualPhase.ResourceMilestones)
			{
				while (_annualSectIndex < PendingAnnualSectIds.Count && budget.TryTake())
				{
					RecordResourceMilestone(PendingAnnualSectIds[_annualSectIndex++], _annualYear);
				}
				if (_annualSectIndex < PendingAnnualSectIds.Count) return false;
				_annualPhase = AnnualPhase.CommonEnemyAlliances;
				continue;
			}

			if (_annualPhase == AnnualPhase.CommonEnemyAlliances)
			{
				if (!budget.TryTake()) return false;
				long allianceSample = XjRuntimeDiagnostics.BeginNamedSample();
				try
				{
					RecordCommonEnemyAlliances(_annualYear);
				}
				finally
				{
					XjRuntimeDiagnostics.EndNamedSample("annual.ThreeBook.alliance", allianceSample);
				}
				_annualPhase = AnnualPhase.Diagnostics;
				continue;
			}

			if (_annualPhase == AnnualPhase.Diagnostics)
			{
				if (!budget.TryTake()) return false;
				long auditSample = XjRuntimeDiagnostics.BeginNamedSample();
				try
				{
					XjThreeBookDiagnostics.AuditYear(_annualYear);
				}
				finally
				{
					XjRuntimeDiagnostics.EndNamedSample("annual.ThreeBook.audit", auditSample);
				}
				ClearPendingAnnualWork();
				return true;
			}
		}

		return _annualPhase == AnnualPhase.None;
	}

	internal static void TickYear(int currentYear)
	{
		if (!BeginYear(currentYear)) return;
		XjThreeBookDeferredFamilyFacts.TickYear(currentYear);
		for (int i = 0; i < PendingAnnualSectIds.Count; i++) RecordResourceMilestone(PendingAnnualSectIds[i], currentYear);
		RecordCommonEnemyAlliances(currentYear);
		XjThreeBookDiagnostics.AuditYear(currentYear);
		ClearPendingAnnualWork();
	}

	internal static void ClearPendingAnnualWork()
	{
		PendingAnnualSectIds.Clear();
		_annualPhase = AnnualPhase.None;
		_annualYear = 0;
		_annualSectIndex = 0;
	}

	private static void RecordResourceMilestone(long sectId, int currentYear)
	{
		if (sectId <= 0L
			|| !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)
			|| sect == null
			|| string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
			|| !XjSectRepository.HasValidSectCity(sectId)) return;
		int cityCount = XjSectRepository.CountValidSectCities(sect.SectId);
		int familyCount = sect.FamilyIds?.Count ?? 0;
		int peakCount = sect.Peaks?.Count ?? 0;
		int inheritanceCount = XjSectGongFaPavilion.ReadGongFaEntries(sect.SectId).Count
			+ XjSectGongFaPavilion.ReadQiuJinFaEntries(sect.SectId).Count;
		int treasureCount = XjFamilyFaBaoWarehouse.CountSectEntries(sect.SectId);
		bool hasFormation = sect.FormationId > 0L;
		bool hasSecretRealm = sect.SecretRealmId > 0L;
		int score = cityCount * 3 + familyCount * 2 + peakCount * 2 + inheritanceCount * 2 + treasureCount * 3
			+ (hasFormation ? 6 : 0) + (hasSecretRealm ? 8 : 0);
		int tier = score >= 70 ? 4 : score >= 40 ? 3 : score >= 20 ? 2 : score >= 8 ? 1 : 0;
		if (tier <= 0) return;
		string source = "sect|resource-milestone|" + sect.SectId + "|" + tier;
		if (XjSectChronicleStore.ContainsSourceFact(source)) return;
		XjThreeBookWriter.RecordSectResourceMilestone(
			sect.SectId, sect.Name, currentYear, tier, cityCount, familyCount, peakCount,
			inheritanceCount, treasureCount, hasFormation, hasSecretRealm);
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
					string period = XjChronology.ResolvePeriodIndex(currentYear, 100).ToString();
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
