using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 古释“度化”与今释杀生度化完全分流。古释不以释土纳人，也不通过度化制造死亡：
/// 每名古释至多十五年点化一名文明凡人，使其后天命数增加5~8，
/// 自身同步获得同量释修命数。候选只做小预算局部/城市索引探测，不建立逐帧寻人AI。
/// </summary>
internal static class XjShiAncientDuhuaSystem
{
	// 古释点化是稀有因缘而不是常规产出；十五年一次可避免多名古释同时刷屏。
	internal const int IntervalYears = 15;
	private const int LocalCandidateBudget = 32;
	private const int CityProbeBudget = 8;
	private const int UnitProbeBudgetPerCity = 12;

	internal static void TryAnnualBlessing(Actor teacher, int annualYear)
	{
		if (teacher?.data == null || !teacher.isAlive() || annualYear <= 0
			|| !XjShiState.TryBuildSnapshot(teacher, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)) return;

		long teacherId = ((BaseSystemData)teacher.data).id;
		if (teacherId <= 0L) return;
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiAncientDuhuaLastYear, out int lastYear);
		if (lastYear > 0)
		{
			if (annualYear - lastYear < IntervalYears) return;
		}
		else if ((annualYear + teacherId) % IntervalYears != 0L)
		{
			// 首次资格按人物ID错峰，之后严格以最近一次成功点化为十五年间隔。
			return;
		}

		if (!TryResolveTarget(teacher, teacherId, annualYear, out Actor target)) return;
		long targetId = ((BaseSystemData)target.data).id;
		if (targetId <= 0L) return;
		int amount = 5 + XjDeterministicHash.PositiveIndex(
			teacherId * 31L + targetId * 17L + annualYear, "shi_ancient_duhua_mingshu_v2", 4);

		// 被点化者只增加普通后天命数，不转入释修，不受释土承载。
		XjCultivationSeed.EnsureSeedState(target);
		XjMingShuState.Normalize(target);
		XjMingShuState.AddAcquired(target, amount);
		// 古释十五年点化是独立低频因果：自身同步增加5~8释修命数，不占普通年度事件封顶。
		// 若自身已经达到1000总上限，则只保留对受度者的点化结果。
		float selfAward = XjShiMingShuSystem.GrantAncientDuhua(teacher, annualYear, targetId, amount);
		// 古释法相不靠今释杀生/摄化扩张；清静点化本身就是其自证金地的温养来源。
		// 只从法相开始写入自身金地，低境古释不会把贡献误投给同法脉其他人的承载地。
		if (XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)
			&& XjShiDomainState.TryGetDharmaFormFoundation(teacher, annualYear, out XjShiDomainRecord selfDomain)
			&& selfDomain != null && selfDomain.OwnerActorId == teacherId)
		{
			XjShiDomainState.AddHighRealmGrowth(selfDomain.DomainId,
				amount * XjShiCatalog.AncientDuhuaDomainGrowthPerMingShu, annualYear);
		}
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiAncientDuhuaLastYear, annualYear);
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiAncientDuhuaCount, out int count);
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiAncientDuhuaCount, Math.Max(0, count) + 1);
		XjAncientShiVowSystem.OnQuietBlessing(teacher, annualYear);

		// 清静点化只是古释日常修证的一部分：真实命数、宏愿与金地收益保留，
		// 但不再写入玄鉴史册/三书，也不生成天下公告。玩家仍可在释修详情里查看累计点化。
	}

	private static bool TryResolveTarget(Actor teacher, long teacherId, int annualYear, out Actor target)
	{
		target = null;
		if (teacher?.city?.units != null && teacher.city.units.Count > 0)
		{
			int localCount = teacher.city.units.Count;
			int start = XjDeterministicHash.PositiveIndex(teacherId + annualYear * 13L,
				"shi_ancient_duhua_local", localCount);
			int budget = Math.Min(LocalCandidateBudget, localCount);
			for (int offset = 0; offset < budget; offset++)
			{
				Actor candidate = teacher.city.units[(start + offset) % localCount];
				if (IsBlessingTarget(candidate, teacher)) { target = candidate; return true; }
			}
		}

		IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
		if (cities == null || cities.Count == 0) return false;
		int cityStart = XjDeterministicHash.PositiveIndex(teacherId + annualYear * 37L,
			"shi_ancient_duhua_city", cities.Count);
		int cityBudget = Math.Min(CityProbeBudget, cities.Count);
		for (int c = 0; c < cityBudget; c++)
		{
			City city = cities[(cityStart + c) % cities.Count];
			if (city?.units == null || city.units.Count == 0) continue;
			int unitStart = XjDeterministicHash.PositiveIndex(teacherId + annualYear * 53L + c,
				"shi_ancient_duhua_unit", city.units.Count);
			int unitBudget = Math.Min(UnitProbeBudgetPerCity, city.units.Count);
			for (int u = 0; u < unitBudget; u++)
			{
				Actor candidate = city.units[(unitStart + u) % city.units.Count];
				if (IsBlessingTarget(candidate, teacher)) { target = candidate; return true; }
			}
		}
		return false;
	}

	private static bool IsBlessingTarget(Actor candidate, Actor teacher)
	{
		return candidate != null && !ReferenceEquals(candidate, teacher)
			&& XjShiEntrySystem.IsDuhuaTarget(candidate);
	}
}
