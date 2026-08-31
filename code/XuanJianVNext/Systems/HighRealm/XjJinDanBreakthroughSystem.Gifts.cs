using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjJinDanBreakthroughSystem
{
	private const int CentennialJinXingYield = 3;

	internal static bool HasAnnualGiftDue(Actor actor, int annualYear)
	{
		if (actor?.data == null || annualYear <= 0 || XjLongShuSystem.IsLongShu(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		bool jinDanEquivalent = XjCultivationPathRules.IsJinDanEquivalentRealm(realmId);
		bool shenDan = string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
		if (!jinDanEquivalent && !shenDan) return false;

		if (jinDanEquivalent)
		{
			XjJinDanState state = XjJinDanAccessor.BuildState(actor);
			if (state.Found && state.SuccessYear > 0 && !string.IsNullOrWhiteSpace(state.JinXing))
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanJinXingGiftLastYear, out int lastProducedYear);
				int baselineYear = Math.Max(state.SuccessYear, Math.Max(0, lastProducedYear));
				if (annualYear - baselineYear >= 100) return true;
			}
		}

		int liveYear = XjYearTracker.CurrentYear;
		if (liveYear <= 0) liveYear = World.world?.map_stats?.year ?? annualYear;
		int liveAge = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		int currentAge = Math.Max(0, liveAge - Math.Max(0, liveYear - annualYear));
		if (currentAge <= 0 || currentAge % 50 != 0) return false;
		return !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanGiftLastYear, out int lastGiftYear)
			|| lastGiftYear != currentAge;
	}

	internal static void TickAnnualGift(Actor actor, int annualYear)
	{
		if (actor?.data == null
			|| annualYear <= 0
			|| XjLongShuSystem.IsLongShu(actor))
		{
			return;
		}

		// 金丹与真君羽士每满一百年自然化生三缕本命金性。产出后先由
		// 本人按既有炼器规则尝试补强自身器物，未投入的金性才留在家族
		// 重宝仓库；此链独立于五十年赐气，采气资源映射异常也不能阻断。
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		bool jinDanEquivalent = XjCultivationPathRules.IsJinDanEquivalentRealm(realmId);
		if (jinDanEquivalent)
		{
			TryProduceCentennialJinXing(actor, annualYear);
		}

		// 金丹、真君羽士与神丹共用五十年赐予；神丹仍无独立果位、
		// 金性、百年金性产出与闭关权。
		if (!jinDanEquivalent
			&& !string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return;
		}

		int liveYear = XjYearTracker.CurrentYear;
		if (liveYear <= 0) liveYear = World.world?.map_stats?.year ?? annualYear;
		int liveAge = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		int currentAge = Math.Max(0, liveAge - Math.Max(0, liveYear - annualYear));
		if (currentAge <= 0 || currentAge % 50 != 0)
		{
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanGiftLastYear, out int lastGiftYear)
			&& lastGiftYear == currentAge)
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		if (string.IsNullOrWhiteSpace(daoTu)
			|| !XjCaiQiCatalog.TryGetOldResourceIdByDaoTuName(daoTu, out string resourceId))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGiftResources, out string existingGifts);
		string updatedGifts = string.IsNullOrWhiteSpace(existingGifts)
			? resourceId
			: existingGifts.Trim() + "|" + resourceId;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGiftResources, updatedGifts);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanGiftLastYear, currentAge);
		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.JinDanGift(actor, resourceId, daoTu));
	}

	private static void TryProduceCentennialJinXing(Actor actor, int annualYear)
	{
		XjJinDanState state = XjJinDanAccessor.BuildState(actor);
		if (!state.Found || string.IsNullOrWhiteSpace(state.JinXing) || state.SuccessYear <= 0)
		{
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanJinXingGiftLastYear, out int lastProducedYear);
		int baselineYear = Math.Max(state.SuccessYear, Math.Max(0, lastProducedYear));
		if (annualYear - baselineYear < 100)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		if (!XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out long familyStableId)
			|| familyStableId <= 0L)
		{
			XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
			if (!XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out familyStableId)
				|| familyStableId <= 0L)
			{
				return;
			}
		}

		string actorName;
		try { actorName = actor.getName() ?? "无名真君"; }
		catch (System.Exception xjCaught119_1) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanBreakthroughSystem.Gifts.cs:119", xjCaught119_1);
			 actorName = "无名真君"; }
		if (!XjFamilyLingWuWarehouse.TryAddJinXing(
			familyStableId,
			state.JinXing,
			CentennialJinXingYield,
			actorId,
			actorName,
			annualYear))
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanJinXingGiftLastYear, annualYear);
		TryPrioritizePersonalEquipment(actor, annualYear);
		string announcement = "【金性产出】" + actorName + "性命圆融，每百年自然化生"
			+ CentennialJinXingYield + "缕“" + state.JinXing + "”。";
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor, announcement, announcement, XjEventIconCatalog.JinXingLegacy, XjAnnouncementCategory.LingWu);
	}

	private static void TryPrioritizePersonalEquipment(Actor actor, int annualYear)
	{
		// 这两条入口均保留原有的闭关、熟练度、三年冷却与成功率约束。
		// 只改变同一批金性在“家族共享”之前由产出者先尝试炼养自身的顺序，
		// 不会凭空跳过炼器门槛或额外制造装备。
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (string.IsNullOrWhiteSpace(realmId)) return;
		XjFaBaoAcquisition.TryForgeAnnualIfMissing(actor, realmId, annualYear);
		XjEquipmentForgeConsumer.TryForgeAnnual(actor, realmId, annualYear);
	}

	internal static bool HasCrossDaoTuXianJi(Actor actor, string ownDaoTu)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(ownDaoTu)) return false;
		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		if (!xianJi.Found || xianJi.Ids == null) return false;
		string normalizedOwn = ownDaoTu.Trim();
		int otherCount = 0;
		for (int i = 0; i < xianJi.Ids.Length; i++)
		{
			string spellName = (xianJi.Ids[i] ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(spellName)) continue;
			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(normalizedOwn, spellName);
			if (kind == XjXianJiPoolKind.Other) otherCount++;
		}
		if (otherCount == 0) return false;
		// Other 池只保留有明确道论结构的远亲：同根远支/对炁/五德映照按结构远闰门槛处理；
		// 完全无拓扑关系不再靠 95 道慧“小空证”为闰位，仍应视作求金无门。
		return !string.Equals(
			XjGuoWeiCalculator.Calculate(actor, normalizedOwn, xianJi),
			XjGuoWeiCalculator.RunWei,
			StringComparison.Ordinal);
	}
}
