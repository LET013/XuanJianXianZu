using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Broadcast;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjJinDanBreakthroughSystem
{
	internal static void RunJinDanSuccessEventChain(
		Actor actor,
		string jinDanDaoTu,
		string jinXing,
		string guoWei,
		int currentYear,
		in XjActorCultivationSnapshot snapshot,
		bool publishPromotionAnnouncement = true,
		string eraChangeCauseOverride = "",
		string promotionTextOverride = "")
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(jinDanDaoTu)
			|| string.IsNullOrWhiteSpace(jinXing)
			|| string.IsNullOrWhiteSpace(guoWei))
		{
			return;
		}

		bool hasPublishedYear = XjActorAccessor.TryGetInt(
			actor, XjActorDataKeys.XjJinDanSuccessEventYear, out int publishedYear)
			&& publishedYear > 0;
		bool hasCurrentSchema = XjActorAccessor.TryGetInt(
			actor, XjActorDataKeys.XjJinDanSuccessEventSchema, out int publishedSchema)
			&& publishedSchema >= SuccessEventSchemaVersion;
		bool coreAlreadyCompleted = hasPublishedYear && hasCurrentSchema;

		int worldYear = GetWorldYear(actor);
		if (worldYear <= 0)
		{
			worldYear = Math.Max(1, currentYear);
		}

		XjActorCultivationSnapshot successSnapshot = snapshot;
		string successRealmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!XjCultivationPathRules.IsJinDanEquivalentRealm(successRealmId)
			&& !string.Equals(successRealmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			successRealmId = XjRealmIds.JinDan;
		}

		bool foundedDongTian = false;
		City zongMenCity = null;
		if (!coreAlreadyCompleted)
		{
			RunJinDanSuccessStep("FamilyIndex", () =>
			{
				XjCultivatorCache.CheckAndUpdate(actor);
				XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
			});
			RunJinDanSuccessStep("QingXuanKongZheng", () =>
				XjQingXuanKongZhengSystem.TryCompleteKongZhengOnJinDan(actor, jinDanDaoTu, guoWei, worldYear));
			RunJinDanSuccessStep("YinYangRarePhenomena", () =>
				XjYinYangRarePhenomenaSystem.OnJinDanSucceeded(actor, jinDanDaoTu, worldYear));

			try
			{
				if (XjLongShuSystem.IsLongShu(actor))
				{
					XjLongShuDongTianSystem.EnsureForJinDan(actor, worldYear);
				}
				else
				{
					bool reportedNewDongTian = XjSectCityData.HandleJinDanPromotion(actor, worldYear);
					XjSectCityData.TryFindActorZongMenCity(actor, out zongMenCity);
					foundedDongTian = reportedNewDongTian
						&& zongMenCity != null
						&& XjSectCityData.HasDongTianPeak(zongMenCity);
				}
			}
			catch (Exception ex)
			{
				Debug.LogError("[玄鉴][金丹成功链] DongTian ex=" + ex.GetType().Name);
				foundedDongTian = false;
				zongMenCity = null;
			}

			string zongMenName = zongMenCity == null ? string.Empty : XjSectCityData.GetZongMenName(zongMenCity);
			string dongTianName = zongMenCity == null ? string.Empty : XjSectCityData.GetDongTianPeakName(zongMenCity);
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

			RunJinDanSuccessStep("FaBao", () => XjFaBaoAcquisition.TryGrantOnJinDanSuccess(actor, successSnapshot));
			RunJinDanSuccessStep("FamilyEvent", () =>
				XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.JinDanSucceeded(
					actor, guoWei, jinDanDaoTu, successRealmId)));
			RunJinDanSuccessStep("Era", () =>
			{
				string eraChangeCause = string.IsNullOrWhiteSpace(eraChangeCauseOverride)
					? XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildJinDanEraChangeCause(
						actor,
						jinDanDaoTu,
						jinXing,
						guoWei)
					: eraChangeCauseOverride.Trim();
				XjEraRuntime.TrySetCurrentAgeForDaoTu(jinDanDaoTu, eraChangeCause);
			});

			// 核心成功事务与公告投递是两个独立幂等域。核心链完成后立即落盘，
			// 不能再因为右上角公告被限流而在下一年重复送法宝、家族事件或纪元切换。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessEventYear, worldYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessEventSchema, SuccessEventSchemaVersion);
		}
		else if (!XjLongShuSystem.IsLongShu(actor))
		{
			// 只读补齐公告所需山门上下文；不得再次执行创洞天事务。
			XjSectCityData.TryFindActorZongMenCity(actor, out zongMenCity);
			foundedDongTian = zongMenCity != null && XjSectCityData.HasDongTianPeak(zongMenCity);
		}

		if (publishPromotionAnnouncement)
		{
			bool hasAnnouncement = XjActorAccessor.TryGetInt(
				actor, XjActorDataKeys.XjJinDanPromotionAnnouncementYear, out int announcementYear)
				&& announcementYear > 0;
			// 老档已经跑完成功链但没有独立公告标记时，不在多年后突然补播；
			// 仅允许“本年先静默补链、随后正式晋升”的同年调用补回长公告。
			bool mayPublishNow = !hasAnnouncement
				&& (!coreAlreadyCompleted || publishedYear == worldYear);
			if (mayPublishNow)
			{
				RunJinDanSuccessStep("Announcement", () => PublishPromotionAnnouncement(
					actor,
					jinDanDaoTu,
					jinXing,
					guoWei,
					worldYear,
					zongMenCity,
					foundedDongTian,
					promotionTextOverride));
			}
		}
	}

	private static void PublishPromotionAnnouncement(
		Actor actor,
		string jinDanDaoTu,
		string jinXing,
		string guoWei,
		int worldYear,
		City zongMenCity,
		bool foundedDongTian,
		string promotionTextOverride)
	{
		if (actor?.data == null) return;

		bool hasOverride = !string.IsNullOrWhiteSpace(promotionTextOverride);
		string promotionText = hasOverride
			? promotionTextOverride.Trim()
			: XjAnnouncementText.BuildJinDanPromotion(actor, jinDanDaoTu, jinXing, guoWei);
		if (!hasOverride && foundedDongTian && zongMenCity != null)
		{
			promotionText += "\n" + XjAnnouncementText.BuildJinDanDongTianFoundation(
				actor, zongMenCity, jinDanDaoTu, jinXing, guoWei);
		}

		long actorId = ((BaseSystemData)actor.data).id;
		long familyId = 0L;
		XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out familyId);
		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "修士");
		string historyTitle = XjCultivationPathRules.IsFuQiYangXing(actor) ? "真君羽士证道" : "金丹证道";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Cultivation,
			historyTitle,
			promotionText,
			5,
			isProtected: true,
			actorId: actorId,
			actorName: actorName,
			familyId: familyId,
			year: worldYear,
			iconIdOverride: XjEventIconCatalog.JinDanUpgrade,
			eventType: XuanJianVNext.Systems.Chronicle.XjChronicleEventTypes.JinDanSucceeded,
			result: XjHistoryResult.Success,
			mirrorToWorldLog: true);

		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			promotionText,
			XjAnnouncementCategory.HighRealm,
			duration: 12f,
			color: "#F0CC75",
			delayFrames: 1,
			iconId: XjEventIconCatalog.JinDanUpgrade);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanPromotionAnnouncementYear, worldYear);
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
