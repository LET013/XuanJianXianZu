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
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjJinDanBreakthroughSystem
{
	private static bool TryPromoteShenDan(
			Actor actor,
			XjActorCultivationSnapshot snapshot,
			XjXianJiState xianJiState,
			XjGongFaState gongFa,
			XjQiuJinFaState qiuJinFa,
			string jinDanDaoTu,
			string guoWeiType,
			int currentYear)
		{
			if (actor?.data == null
				|| XjXianGuoSystem.IsDiMingYang(actor)
				|| string.IsNullOrWhiteSpace(jinDanDaoTu)
				|| string.IsNullOrWhiteSpace(guoWeiType)
				|| !XjShenDanMethodSystem.CanPursue(
					actor, jinDanDaoTu, guoWeiType, qiuJinFa, currentYear))
			{
				return false;
			}

			long actorId = ((BaseSystemData)actor.data).id;
			if (!XjGuoWeiRegistry.TryFindActiveAnchor(
					jinDanDaoTu,
					guoWeiType,
					candidate => XjShenDanRegistry.CanAttachToAnchor(candidate, actorId, out _, out _),
					out XjGuoWeiRegistryEntry anchor)
				|| anchor.ActorId <= 0L
				|| anchor.ActorId == actorId)
			{
				return false;
			}

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string originalDaoTu);
			if (!XjCultivationStateTransitions.TrySetDaoTu(actor, jinDanDaoTu, false))
			{
				return false;
			}
			if (!XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ShenDan, false))
			{
				if (!string.IsNullOrWhiteSpace(originalDaoTu))
				{
					XjCultivationStateTransitions.TrySetDaoTu(actor, originalDaoTu, false);
				}
				return false;
			}

			string anchorName = string.IsNullOrWhiteSpace(anchor.ActorName) ? "无名金丹" : anchor.ActorName.Trim();
			XjShenDanAccessor.WriteSuccess(actor, anchor.GuoWei, anchor.ActorId, anchorName, currentYear);
			if (!XjShenDanAccessor.BuildState(actor).Found)
			{
				XjShenDanAccessor.ClearSuccess(actor);
				XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false);
				if (!string.IsNullOrWhiteSpace(originalDaoTu))
				{
					XjCultivationStateTransitions.TrySetDaoTu(actor, originalDaoTu, false);
				}
				return false;
			}
			if (!XjShenDanRegistry.TryRegister(actorId, anchor.ActorId))
			{
				XjShenDanAccessor.ClearSuccess(actor);
				XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false);
				if (!string.IsNullOrWhiteSpace(originalDaoTu))
				{
					XjCultivationStateTransitions.TrySetDaoTu(actor, originalDaoTu, false);
				}
				return false;
			}
			if (XjShenDanRegistry.IsAnchorAtCapacity(anchor.ActorId, anchor.GuoWei)
				&& XjScheduler.ResolveActor(anchor.ActorId, out Actor anchorActor))
			{
				XjRealmTitleApplyService.EnsureJinDanAnchorTitleAtShenDanCapacity(anchorActor, anchor.DaoTu);
			}
			XjYinSiTraitLifecycle.EnsureRemovedFromJinDan(actor);
			XjRealmPromotionHelper.ApplyCommonPostRealmWrite(actor, XjRealmIds.ShenDan, currentYear);
			XjRealmTitleApplyService.ApplyOnPromotion(actor, XjRealmIds.ShenDan, jinDanDaoTu);
			XjFamilyHighGradeTransmission.RecordJinDanGongFaSet(actor, jinDanDaoTu, gongFa, qiuJinFa, xianJiState, currentYear);
			XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.ShenDan, "ShenDanPromotion");
			string text = (actor.getName() ?? "无名紫府")
				+ " 托果于 " + anchorName
				+ "，成就神丹，托身于" + anchorName + "。";
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelActorEvent(
				actor,
				text,
				text,
				XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.JinDanUpgrade);
			XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.ShenDanSucceeded(actor, anchor.GuoWei, jinDanDaoTu, anchorName));
			XjStageZeroObservation.RecordJinDanResult("ShenDanSuccess", true);
			return true;
		}
}
