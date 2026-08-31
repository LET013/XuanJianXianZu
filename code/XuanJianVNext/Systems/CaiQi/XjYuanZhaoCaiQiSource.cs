using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;

namespace XuanJianVNext.Systems.CaiQi;

/// <summary>
/// 渊照不是开局常见先天之气，其采气源只随“水月照真洞天”这五百年遗泽建立。
/// 本入口只维护一个按洞天锚点确定的索引地点，不扫描城市、建筑或地块。
/// </summary>
internal static class XjYuanZhaoCaiQiSource
{
	internal const string SiteName = "水月照真渊";

	internal static void RemoveForTimelineMigration(long cityId)
	{
		if (cityId <= 0L) return;
		if (XjCaiQiSiteRegistry.TryFindByCityAndPlaceType(cityId, XjCaiQiPlaceTypeIds.YuanZhao, out XjCaiQiSite site))
		{
			XjCaiQiSiteRegistry.Remove(site.SiteId);
		}
	}

	internal static bool Ensure(long cityId, string stableRecordId)
	{
		if (cityId <= 0L || string.IsNullOrWhiteSpace(stableRecordId)) return false;
		if (XjCaiQiSiteRegistry.TryFindByCityAndPlaceType(cityId, XjCaiQiPlaceTypeIds.YuanZhao, out _)) return true;

		long siteId = XjDeterministicHash.PositiveHash(
			cityId,
			"xuanjian.yuanzhao.caiqi.v1|" + stableRecordId.Trim());
		return XjCaiQiSiteRegistry.Register(new XjCaiQiSite(
			Math.Max(1L, siteId),
			cityId,
			XjCaiQiPlaceTypeIds.YuanZhao,
			SiteName));
	}
}
