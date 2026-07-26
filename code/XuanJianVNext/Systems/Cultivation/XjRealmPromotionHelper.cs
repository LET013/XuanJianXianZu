using System;
using UnityEngine;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjRealmPromotionHelper
{
	internal static void ApplyCommonPostRealmWrite(
		Actor actor,
		string realmId,
		int currentYear,
		bool applyMingShuReward = true,
		bool refreshBloodline = true,
		bool syncVisibleTraits = true,
		bool restoreHealth = true)
	{
		if (actor?.data == null)
		{
			return;
		}

		if (applyMingShuReward)
		{
			RunNonCritical("MingShuReward", () => XjBreakthroughRules.ApplySuccessMingShuReward(actor, realmId));
			RunNonCritical("CraftTrait", () => XjCraftTraitAcquisitionSystem.TryGrantOnRealmBreakthrough(actor, realmId, currentYear));
		}

		if (refreshBloodline)
		{
			RunNonCritical("BloodlineRefresh", () => XjBloodlineBirthRules.TryRefreshForRealmPromotion(actor, currentYear));
		}

		if (syncVisibleTraits)
		{
			RunNonCritical("VisibleTraitSync", () => XjVisibleTraitSync.SyncCultivationTraits(actor));
		}

		if (restoreHealth)
		{
			RunNonCritical("RestoreHealth", () => XjCultivationStateTransitions.RestoreHealthAfterRealmPromotion(actor));
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, System.StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, System.StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, System.StringComparison.Ordinal))
		{
			RunNonCritical("SectTransition", () => XjNationSectTransitionSystem.RequestFromZiFuPromotion(actor, currentYear));
		}
		if (string.Equals(realmId, XjRealmIds.JinDan, System.StringComparison.Ordinal))
		{
			RunNonCritical("JinDanRegistry", () => XjJinDanImmortalityRegistry.EnsureActivated(actor, currentYear));
		}

		// Realm aggregation is event-driven. Refresh the actor's city bucket now
		// instead of waiting for the next annual actor pass or a codex rebuild.
		RunNonCritical("CityIndex", () => XjZongMenCultivatorCityIndex.Observe(actor));
	}

	private static void RunNonCritical(string stage, Action action)
	{
		try { action?.Invoke(); }
		catch (Exception ex) { Debug.LogError("[玄鉴][突破后处理] " + stage + "：" + ex.GetType().Name); }
	}
}
