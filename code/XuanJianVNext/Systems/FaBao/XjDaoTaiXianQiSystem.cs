using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.FaBao;

/// <summary>
/// 道胎本命法宝升格仙器：每名道胎独立按五百年周期判定一次，单次1‰。
/// 只处理现存的金丹本命法宝，不创建新器、不扫描全世界、不补算跨年概率债务。
/// </summary>
internal static class XjDaoTaiXianQiSystem
{
	internal const int AttemptIntervalYears = 500;
	internal const int ChancePerThousand = 1;

	internal static bool HasEligiblePrimary(Actor actor)
	{
		if (actor?.data == null
			|| XjCultivationPathRules.IsShi(actor)
			|| XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierDaoTai)
		{
			return false;
		}

		XjFaBaoState state = XjFaBaoAccessor.BuildState(actor);
		return state.Found
			&& string.Equals(state.ClassName, XjFaBaoCatalog.JinDanFaBaoClass, StringComparison.Ordinal);
	}

	internal static bool IsAttemptDue(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || !HasEligiblePrimary(actor)) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjDaoTaiXianQiLastAttemptYear, out int lastAttemptYear)
			|| lastAttemptYear <= 0)
		{
			// One first pass initializes the five-hundred-year clock.
			return true;
		}
		return currentYear >= lastAttemptYear + AttemptIntervalYears;
	}

	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || !HasEligiblePrimary(actor)) return;

		int lastAttemptYear = 0;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjDaoTaiXianQiLastAttemptYear, out lastAttemptYear);
		if (lastAttemptYear <= 0)
		{
			int enteredYear = 0;
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out enteredYear);
			lastAttemptYear = enteredYear > 0 && enteredYear <= currentYear ? enteredYear : currentYear;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjDaoTaiXianQiLastAttemptYear, lastAttemptYear);
		}

		if (currentYear < lastAttemptYear + AttemptIntervalYears) return;

		// 严格五百年一次。当前帧无论成功失败都先落判定年，避免读档/高倍速重复抽取。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjDaoTaiXianQiLastAttemptYear, currentYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| XjDeterministicHash.PositiveIndex(
				actorId + currentYear,
				"daotai_xianqi_upgrade_v1",
				1000) >= ChancePerThousand)
		{
			return;
		}

		XjFaBaoAcquisition.TryUpgradePrimaryJinDanToXianQi(actor, currentYear);
	}
}
