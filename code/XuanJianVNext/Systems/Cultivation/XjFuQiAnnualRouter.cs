using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 服气年度路由。真元可以作为角色身上的普通数值存在，但不会参与服气境界、
/// 神妙、金性或求证判定；本路由不再清零或消费真元。
/// </summary>
internal static class XjFuQiAnnualRouter
{
	internal static void PrepareActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return;
		}
		// 只校准已进入服气养性的索引角色，不进行全图扫描。
		XjFuQiSensingSystem.EnsureEnteredPathSuccess(actor);
		if (XjAptitudeTraitLifecycle.HasAnnualInterest(actor, currentYear))
		{
			XjAptitudeTraitLifecycle.TickAnnual(actor, currentYear);
		}
	}

	internal static void TickActor(Actor actor, int currentYear)
	{
		XjFuQiCoreRouter.TickActor(actor, currentYear);
		XjFuQiNarrativeEventObserver.ObserveAfterAnnualTick(actor, currentYear);
	}
}
