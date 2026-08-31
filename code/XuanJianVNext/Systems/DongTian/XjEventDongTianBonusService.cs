using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.DongTian;

/// <summary>
/// 事件洞天只写角色持久化短期收益，不创建常驻状态机。突破收益在有效期内
/// 由原有突破入口读取，神通收益由原有紫府参悟入口读取。
/// </summary>
internal static class XjEventDongTianBonusService
{
	internal const int DefaultDurationYears = 50;
	internal const int GeneralBreakthroughBonusBasisPoints = 400;
	internal const int ZiFuExtraBonusBasisPoints = 600;
	internal const int ShenTongBonusBasisPoints = 800;

	internal static void GrantLowerCultivatorBenefits(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		int untilYear = currentYear + DefaultDurationYears;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjEventDongTianBreakthroughBonusBasisPoints,
			GeneralBreakthroughBonusBasisPoints);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjEventDongTianZiFuBonusBasisPoints,
			ZiFuExtraBonusBasisPoints);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjEventDongTianBonusUntilYear, untilYear);
	}

	internal static void GrantShenTongComprehensionBenefit(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjEventDongTianShenTongBonusBasisPoints,
			ShenTongBonusBasisPoints);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjEventDongTianShenTongBonusUntilYear,
			currentYear + DefaultDurationYears);
	}

	internal static float ResolveBreakthroughTriggerBonus(Actor actor, int currentYear)
	{
		if (!TryReadActiveBreakthroughBonus(actor, currentYear, out int general, out _)) return 0f;
		return Math.Clamp(Math.Max(0, general) / 10000f, 0f, 0.08f);
	}

	internal static float ResolveBreakthroughSuccessBonus(Actor actor, string targetRealmId, int currentYear)
	{
		if (!TryReadActiveBreakthroughBonus(actor, currentYear, out int general, out int ziFu)) return 0f;
		int basisPoints = Math.Max(0, general);
		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			basisPoints += Math.Max(0, ziFu);
		}
		return Math.Clamp(basisPoints / 10000f, 0f, 0.15f);
	}

	private static bool TryReadActiveBreakthroughBonus(Actor actor, int currentYear, out int general, out int ziFu)
	{
		general = 0;
		ziFu = 0;
		if (actor?.data == null || currentYear <= 0
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjEventDongTianBonusUntilYear, out int untilYear)
			|| untilYear < currentYear)
		{
			return false;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjEventDongTianBreakthroughBonusBasisPoints, out general);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjEventDongTianZiFuBonusBasisPoints, out ziFu);
		return true;
	}

	internal static float ResolveShenTongBonus(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjEventDongTianShenTongBonusUntilYear, out int untilYear)
			|| untilYear < currentYear
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjEventDongTianShenTongBonusBasisPoints, out int basisPoints))
		{
			return 0f;
		}
		return Math.Clamp(Math.Max(0, basisPoints) / 10000f, 0f, 0.12f);
	}
}
