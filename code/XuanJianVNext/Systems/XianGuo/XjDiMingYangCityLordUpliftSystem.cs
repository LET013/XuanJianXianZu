using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.XianGuo;

/// <summary>
/// 帝明阳旧版“城主反哺”兼容边界。
///
/// 0.9.8.20 起，仙国法不再把 WorldBox 原生城主、城主忠诚或原生官位当成权威状态：
/// 1) 仙朝官位由 <see cref="XjXianGuoCourtSystem"/> 独立档案维护；
/// 2) 持玄、借境、玄秩与仙国假金丹均由官位档案投影到角色；
/// 3) 原生城市与王国只承担地理、人口、战争等世界承载职责。
///
/// 本类只维护既有晋升调用点与旧档帝明阳境界基线，不再保留原生城主扫描、忠诚写入等历史空入口。
/// </summary>
internal static class XjDiMingYangCityLordUpliftSystem
{
	/// <summary>
	/// 新立帝明阳及旧档年度维护先建立当前境界基线，避免加载/投影补录被误判成一次破境。
	/// </summary>
	internal static void EnsureBaseline(Actor emperor)
	{
		if (emperor?.data == null || !XjXianGuoSystem.IsDiMingYang(emperor)) return;
		int currentTier = XjRealmSuppression.GetRealmTier(emperor);
		if (currentTier <= XjRealmSuppression.TierNone) return;
		if (!XjActorAccessor.TryGetInt(emperor, XjActorDataKeys.DiMingYangPatronageRealmTier, out int storedTier)
			|| storedTier <= XjRealmSuppression.TierNone)
		{
			XjActorAccessor.SetInt(emperor, XjActorDataKeys.DiMingYangPatronageRealmTier, currentTier);
		}
	}

	/// <summary>
	/// 由真实境界晋升后处理调用。0.9.8.20 起不再反哺原生城主，只维护帝明阳自身境界基线。
	/// </summary>
	internal static void ObserveRealmPromotion(Actor emperor, string promotedRealmId, int currentYear)
	{
		EnsureBaseline(emperor);
		if (emperor?.data == null || !XjXianGuoSystem.IsDiMingYang(emperor)) return;
		string normalized = XjRealmHelper.NormalizeId(promotedRealmId);
		if (XjRealmHelper.GetOrder(normalized) < XjRealmHelper.GetOrder(XjRealmIds.JinDan)) return;
		XjXianGuoSystem.TryVoluntaryAbdicationAfterRealJinDan(emperor, currentYear, out _);
	}


}
