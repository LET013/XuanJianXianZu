using System.Collections.Generic;

namespace XuanJianVNext.Systems.ZongMen;

/// <summary>
/// 宗门维护领域服务。这里只处理单个城市与候选索引，不持有调度状态；
/// 分帧、相位和年度请求合并由 <see cref="XjZongMenRuntimeLane"/> 负责。
/// </summary>
internal static class XjZongMenSystem
{
	internal const int MaintenanceBudget = 12;
	private const int CleanupBudget = 64;

	internal static List<City> SelectMaintenanceCandidates(int maintenancePeriod)
	{
		// 这里是旧城镇镜像的招募/师徒维护入口，不负责宗门本体、峰脉、
		// 大阵或洞天。只需处理已经有修士索引的城市，不能为了找少量
		// 招募目标而每五年重新遍历整个世界的城镇列表。
		XjZongMenCultivatorCityIndex.CleanupInvalid(CleanupBudget);
		HashSet<City> candidateCities = XjZongMenCultivatorCityIndex.GetCandidateCities();
		List<City> existingSects = new List<City>(candidateCities?.Count ?? 0);
		if (candidateCities == null || candidateCities.Count == 0) return existingSects;
		foreach (City city in candidateCities)
		{
			if (city?.data == null || !XjZongMenCityData.HasZongMen(city)) continue;
			XjZongMenCityData.BackfillFounderHistory(city);
			existingSects.Add(city);
		}
		existingSects.Sort((left, right) => ((BaseSystemData)left.data).id.CompareTo(((BaseSystemData)right.data).id));
		return SelectOrderedCities(existingSects, maintenancePeriod, MaintenanceBudget);
	}

	internal static Dictionary<City, List<long>> GetCurrentCityIndex()
	{
		return XjZongMenCultivatorCityIndex.GetCityIndex();
	}

	internal static bool TryMaintainExisting(
		City city,
		int currentYear,
		Dictionary<City, List<long>> cityIndex,
		ref int createdPeaks)
	{
		if (city?.data == null || cityIndex == null || !XjZongMenCityData.HasZongMen(city)) return false;

		int lastRecruitYear = XjZongMenCityData.GetLastRecruitYear(city);
		bool recruitDue = lastRecruitYear <= 0
			|| currentYear - lastRecruitYear >= XjZongMenCityData.RecruitIntervalYears;
		if (recruitDue) XjZongMenCityData.TryRecruitCityCultivators(city, currentYear, cityIndex);

		// 峰位与峰主由 XjSectRepository.Peaks 统一维护；弟子名单由该档案
		// 在年度治理队列中投影到城镇持久化层。这里仅保留城市招收职责，
		// 不再独立开峰，避免产生第二套峰主记录。
		_ = createdPeaks;
		if (recruitDue) XjZongMenCityData.SetLastRecruitYear(city, currentYear);
		return true;
	}

	private static List<City> SelectOrderedCities(List<City> ordered, int maintenancePeriod, int candidateBudget)
	{
		if (ordered == null || ordered.Count == 0 || candidateBudget <= 0) return new List<City>(0);
		int total = ordered.Count;
		int budget = System.Math.Min(candidateBudget, total);
		long rotation = (long)System.Math.Max(0, maintenancePeriod - 1) * candidateBudget;
		int start = (int)(rotation % total);
		List<City> selected = new List<City>(budget);
		for (int offset = 0; offset < budget; offset++) selected.Add(ordered[(start + offset) % total]);
		return selected;
	}
}
