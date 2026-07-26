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
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjJinDanBreakthroughSystem
{
	internal static void TickAnnualGift(Actor actor)
	{
		if (actor?.data == null || XjLongShuSystem.IsLongShu(actor))
		{
			return;
		}

		// 金丹与神丹共用十年赐予；神丹仍无独立果位、金性与闭关权。
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (!string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& !string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return;
		}

		int currentAge = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		if (currentAge <= 0 || currentAge % 10 != 0)
		{
			return;
		}

		// 检查十年内未重复赐福。
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

	internal static bool HasCrossDaoTuXianJi(Actor actor, string ownDaoTu)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(ownDaoTu)) return false;
		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		if (!xianJi.Found || xianJi.Ids == null) return false;
		string normalizedOwn = ownDaoTu.Trim();
		for (int i = 0; i < xianJi.Ids.Length; i++)
		{
			string spellName = (xianJi.Ids[i] ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(spellName)) continue;
			if (XjXianJiCatalog.GetPoolKind(normalizedOwn, spellName) == XjXianJiPoolKind.Other) return true;
		}
		return false;
	}
}
