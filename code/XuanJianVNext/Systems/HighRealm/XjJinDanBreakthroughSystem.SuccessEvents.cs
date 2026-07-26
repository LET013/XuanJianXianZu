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
{		internal static void RunJinDanSuccessEventChain(
			Actor actor,
			string jinDanDaoTu,
			string jinXing,
			string guoWei,
			int currentYear,
			in XjActorCultivationSnapshot snapshot)
		{
			if (actor?.data == null
				|| string.IsNullOrWhiteSpace(jinDanDaoTu)
				|| string.IsNullOrWhiteSpace(jinXing)
				|| string.IsNullOrWhiteSpace(guoWei))
			{
				return;
			}
	
			bool hasPublishedYear = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessEventYear, out int publishedYear)
				&& publishedYear > 0;
			bool hasCurrentSchema = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessEventSchema, out int publishedSchema)
				&& publishedSchema >= SuccessEventSchemaVersion;
			if (hasPublishedYear && hasCurrentSchema)
			{
				return;
			}
	
			int worldYear = GetWorldYear(actor);
			if (worldYear <= 0)
			{
				worldYear = Math.Max(1, currentYear);
			}
	
			XjActorCultivationSnapshot successSnapshot = snapshot;
	
			RunJinDanSuccessStep("FamilyIndex", () =>
			{
				XjCultivatorCache.CheckAndUpdate(actor);
				XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
			});
			RunJinDanSuccessStep("QingXuanKongZheng", () =>
				XjQingXuanKongZhengSystem.TryCompleteKongZhengOnJinDan(actor, jinDanDaoTu, worldYear));
	
			bool foundedDongTian = false;
			City zongMenCity = null;
			try
			{
				if (XjLongShuSystem.IsLongShu(actor))
				{
					// 龙属金丹不进入宗门洞天链。成丹事件内立即创建/绑定世界唯一
					// 的远海沧溟鳞宫，并从当年开始 490 年闭关周期。
					XjLongShuDongTianSystem.EnsureForJinDan(actor, worldYear);
				}
				else
				{
					bool reportedNewDongTian = XjZongMenCityData.HandleJinDanPromotion(actor, worldYear);
					XjZongMenCityData.TryFindActorZongMenCity(actor, out zongMenCity);
					foundedDongTian = reportedNewDongTian
						&& zongMenCity != null
						&& XjZongMenCityData.HasDongTianPeak(zongMenCity);
				}
			}
			catch (Exception ex)
			{
				Debug.LogError("[玄鉴][金丹成功链] DongTian ex=" + ex.GetType().Name);
				foundedDongTian = false;
				zongMenCity = null;
			}
	
			string zongMenName = zongMenCity == null ? string.Empty : XjZongMenCityData.GetZongMenName(zongMenCity);
			string dongTianName = zongMenCity == null ? string.Empty : XjZongMenCityData.GetDongTianPeakName(zongMenCity);
	
			RunJinDanSuccessStep("Chronicle", () =>
			{
				if (!HasJinDanChronicle(actor))
				{
					XuanJianVNext.Systems.Chronicle.XjChronicleWriter.RecordJinDanSucceeded(
						actor,
						worldYear,
						guoWei,
						jinXing,
						jinDanDaoTu,
						foundedDongTian && zongMenCity != null,
						zongMenName,
						dongTianName);
				}
			});
	
			bool announcementAccepted = false;
			RunJinDanSuccessStep("Announcement", () =>
			{
				string promotionText = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildJinDanPromotion(
					actor,
					jinDanDaoTu,
					jinXing,
					guoWei);
				if (foundedDongTian && zongMenCity != null)
				{
					string dongTianText = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildJinDanDongTianFoundation(
						actor,
						zongMenCity,
						jinDanDaoTu,
						jinXing,
						guoWei);
					string tipText = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildJinDanDongTianTip(
						actor,
						zongMenCity,
						guoWei);
					announcementAccepted = XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelActorEvent(
						actor,
						promotionText + "\n" + dongTianText,
						tipText,
						XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.JinDanUpgrade);
				}
				else
				{
					announcementAccepted = XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelActorEvent(
						actor,
						promotionText,
						null,
						XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.JinDanUpgrade);
				}
			});
	
			RunJinDanSuccessStep("FaBao", () => XjFaBaoAcquisition.TryGrantOnJinDanSuccess(actor, successSnapshot));
			RunJinDanSuccessStep("FamilyEvent", () =>
				XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.JinDanSucceeded(actor, guoWei, jinDanDaoTu)));
			RunJinDanSuccessStep("Era", () =>
			{
				string eraChangeCause = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildJinDanEraChangeCause(
					actor,
					jinDanDaoTu,
					jinXing,
					guoWei);
				XjEraRuntime.TrySetCurrentAgeForDaoTu(jinDanDaoTu, eraChangeCause);
			});
			// 事件链完成标记与公告窗口是否接受显示无关。公告被限流时也必须
			// 固化纪事/法宝/家族链，避免每年重复执行成功事件。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessEventYear, worldYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessEventSchema, SuccessEventSchemaVersion);
		}

		internal static bool HasJinDanChronicle(Actor actor)
		{
			if (actor?.data == null)
			{
				return false;
			}
	
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L
				|| !XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out long familyId)
				|| familyId <= 0L)
			{
				return false;
			}
	
			var events = XuanJianVNext.Systems.Chronicle.XjChronicleReadModel.Shared.ReadFamilyChronicle(familyId);
			for (int i = 0; i < events.Count; i++)
			{
				var entry = events[i];
				if (entry != null
					&& entry.Found
					&& entry.ActorId == actorId
					&& string.Equals(entry.EventType, XuanJianVNext.Systems.Chronicle.XjChronicleEventTypes.JinDanSucceeded, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		private static void RunJinDanSuccessStep(string step, Action action)
		{
			if (action == null)
			{
				return;
			}
	
			try
			{
				action();
			}
			catch (Exception ex)
			{
				Debug.LogError("[玄鉴][金丹成功链] " + step + " ex=" + ex.GetType().Name);
			}
		}
}

