using System;
using System.Collections.Generic;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.UI.Overview;

internal readonly struct XjCultivatorPopulationSummary
{
	internal readonly int Total;
	internal readonly int Unentered;
	internal readonly int TaiXi;
	internal readonly int LianQi;
	internal readonly int ZhuJi;
	internal readonly int ZiFu;
	internal readonly int JinDan;

	internal XjCultivatorPopulationSummary(
		int total,
		int unentered,
		int taiXi,
		int lianQi,
		int zhuJi,
		int ziFu,
		int jinDan)
	{
		Total = total;
		Unentered = unentered;
		TaiXi = taiXi;
		LianQi = lianQi;
		ZhuJi = zhuJi;
		ZiFu = ziFu;
		JinDan = jinDan;
	}

	internal string DisplayValue => Total + "人";

	internal string BuildTooltip(string scopeName)
	{
		StringBuilder builder = new StringBuilder(96);
		builder.Append(string.IsNullOrWhiteSpace(scopeName) ? "当前辖域" : scopeName.Trim())
			.Append("共有修炼者")
			.Append(Total)
			.Append("人");
		AppendRealm(builder, "未入道", Unentered);
		AppendRealm(builder, "胎息", TaiXi);
		AppendRealm(builder, "炼气", LianQi);
		AppendRealm(builder, "筑基", ZhuJi);
		AppendRealm(builder, "紫府", ZiFu);
		AppendRealm(builder, "金丹", JinDan);
		return builder.ToString();
	}

	private static void AppendRealm(StringBuilder builder, string label, int count)
	{
		if (count <= 0)
		{
			return;
		}

		builder.Append('\n').Append(label).Append("：").Append(count).Append("人");
	}
}

/// <summary>
/// 国家/城市概览用的修炼者人口统计。只遍历现有修士缓存，禁止为 UI 进行全世界单位扫描。
/// </summary>
internal static class XjCultivatorPopulationOverview
{
	internal static XjCultivatorPopulationSummary BuildForCity(City city)
	{
		return Build(actor => actor.city == city);
	}

	internal static XjCultivatorPopulationSummary BuildForKingdom(Kingdom kingdom)
	{
		return Build(actor => actor.kingdom == kingdom);
	}

	private static XjCultivatorPopulationSummary Build(Func<Actor, bool> belongs)
	{
		if (belongs == null)
		{
			return default;
		}

		int total = 0;
		int unentered = 0;
		int taiXi = 0;
		int lianQi = 0;
		int zhuJi = 0;
		int ziFu = 0;
		int jinDan = 0;
		IReadOnlyList<long> ids = XjCultivatorCache.GetAllIds();
		for (int i = 0; i < ids.Count; i++)
		{
			long actorId = ids[i];
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| actor?.data == null
				|| !actor.isAlive()
				|| !belongs(actor))
			{
				continue;
			}

			total++;
			int tier = XjCultivatorCache.TryGetRealmTier(actorId, out int cachedTier)
				? cachedTier
				: XjRealmSuppression.GetRealmTier(actor);
			switch (tier)
			{
				case XjRealmSuppression.TierTaiXi:
					taiXi++;
					break;
				case XjRealmSuppression.TierLianQi:
					lianQi++;
					break;
				case XjRealmSuppression.TierZhuJi:
					zhuJi++;
					break;
				case XjRealmSuppression.TierZiFu:
					ziFu++;
					break;
				case XjRealmSuppression.TierJinDan:
					jinDan++;
					break;
				default:
					unentered++;
					break;
			}
		}

		return new XjCultivatorPopulationSummary(total, unentered, taiXi, lianQi, zhuJi, ziFu, jinDan);
	}
}
