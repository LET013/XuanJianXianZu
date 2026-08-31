using System;
using UnityEngine;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.XianGuo;

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
			XjBloodlineRefreshLane.Request(actor, currentYear);
		}

		if (syncVisibleTraits)
		{
			RunNonCritical("VisibleTraitSync", () => XjVisibleTraitSync.SyncCultivationTraits(actor));
		}

		if (restoreHealth)
		{
			RunNonCritical("RestoreHealth", () => XjCultivationStateTransitions.RestoreHealthAfterRealmPromotion(actor));
		}

		if (XjHighRealmIdentity.IsHighRealm(realmId))
		{
			// 宗门选择依赖稳定家族身份，真人/真君两条修法统一先补家族再转宗门。
			// 龙属没有修士家族，不得被公共高境后处理误建家族。
			if (!XjLongShuSystem.IsLongShu(actor))
			{
				RunNonCritical("HighRealmFamily", () => XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor));
			}
			RunNonCritical("SectTransition", () => XjNationSectTransitionSystem.RequestFromHighRealmPromotion(actor, currentYear));
			RunNonCritical("LongShuThreshold", () => XjLongShuSystem.NotifyZhenRenLevelAppeared(actor));
			RunNonCritical("DaoTaiMerit", () => XjDaoTaiMeritSystem.ObserveHighRealmPromotion(actor, realmId, currentYear));
		}
		if (XjCultivationPathRules.IsJinDanEquivalentRealm(realmId))
		{
			RunNonCritical("ZhenJunRegistry", () => XjJinDanImmortalityRegistry.EnsureActivated(actor, currentYear));
		}
		if (IsDaoTaiRealm(realmId))
		{
			RunNonCritical("DaoTaiImmortal", () => EnsureDaoTaiImmortalTrait(actor));
			RunNonCritical("DaoTaiRegistry", () => XjJinDanImmortalityRegistry.EnsureActivated(actor, currentYear));
			RunNonCritical("DaoTaiGongFa", () => XjDaoTaiGongFaService.EnsureGradeSeven(actor, currentYear));
		}

		// 高境统一品秩、特殊仙身/神丹标记和仙国借玄都物化在战斗缓存。
		// 只在真正的境界写入后刷新一次，避免命中热路径重建状态。
		RunNonCritical("CombatHotProfile", () => XjCombatHotPathCache.Refresh(actor));

		// Realm aggregation is event-driven. Refresh the actor's city bucket now
		// instead of waiting for the next annual actor pass or a codex rebuild.
		RunNonCritical("CityIndex", () => XjSectCultivatorCityIndex.Observe(actor));

		// 帝明阳只有在真实王位上完成大境界晋升时，才以国势反哺同朝城主。
		// 该系统用持久 Tier 基线去重，因此普通突破器的双后处理也只会结算一次。
		RunNonCritical("DiMingYangCityLordUplift", () =>
			XjDiMingYangCityLordUpliftSystem.ObserveRealmPromotion(actor, realmId, currentYear));
	}

	private static bool IsDaoTaiRealm(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	private static void EnsureDaoTaiImmortalTrait(Actor actor)
	{
		if (actor?.data == null || actor.hasTrait("immortal") || AssetManager.traits?.get("immortal") == null)
		{
			return;
		}

		if (actor.addTrait("immortal", false))
		{
			actor.clearTraitCache();
			actor.setStatsDirty();
		}
	}

	private static void RunNonCritical(string stage, Action action)
	{
		try { action?.Invoke(); }
		catch (Exception ex) { Debug.LogError("[玄鉴][突破后处理] " + stage + "：" + ex); }
	}
}
