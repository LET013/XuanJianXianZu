using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XjRealmHelper = XuanJianVNext.Data.Rules.XjRealmHelper;

namespace XuanJianVNext.Systems.Chronicle;

internal static class XjFamilyChronicleEventRouter
{
	internal static void Handle(in XjFamilyDomainEvent domainEvent)
	{
		if (!domainEvent.Found || domainEvent.FamilyStableId <= 0L || domainEvent.ActorId <= 0L)
		{
			return;
		}

		if (!ShouldRecordDomainEvent(domainEvent))
		{
			return;
		}

		if (!TryBuildChronicle(domainEvent, out string eventType, out string title, out string body, out int importance, out bool relatedToFamilyWarehouse, out bool relatedToHighGradeGongFa))
		{
			return;
		}

		XjChronicleWriter.RecordDomainEvent(
			domainEvent,
			eventType,
			BuildEventKey(domainEvent),
			title,
			body,
			importance,
			relatedToFamilyWarehouse,
			relatedToHighGradeGongFa);
	}

	private static bool TryBuildChronicle(
		in XjFamilyDomainEvent domainEvent,
		out string eventType,
		out string title,
		out string body,
		out int importance,
		out bool relatedToFamilyWarehouse,
		out bool relatedToHighGradeGongFa)
	{
		eventType = domainEvent.EventType;
		title = string.Empty;
		body = string.Empty;
		importance = 1;
		relatedToFamilyWarehouse = false;
		relatedToHighGradeGongFa = false;

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeFamilyMemberConfirmed)
		{
			title = "族人归籍";
			body = string.IsNullOrWhiteSpace(domainEvent.ActorName) ? "族人确认入籍" : domainEvent.ActorName + "确认入籍";
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeBirth)
		{
			eventType = XjChronicleEventTypes.Birth;
			title = "新丁降世";
			body = string.IsNullOrWhiteSpace(domainEvent.ActorName) ? "新族人降生于世" : domainEvent.ActorName + "降生于世";
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeAptitudeGranted)
		{
			eventType = XjChronicleEventTypes.AptitudeGranted;
			title = "灵光初显";
			body = string.IsNullOrWhiteSpace(domainEvent.ActorName) ? "族人展露天资，灵光初显" : domainEvent.ActorName + "展露天资，灵光初显";
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeRealmBreakthrough)
		{
			string actorName = string.IsNullOrWhiteSpace(domainEvent.ActorName) ? "族人" : domainEvent.ActorName;
			string realmId = domainEvent.RealmId;

			if (string.IsNullOrWhiteSpace(realmId))
			{
				title = "道行精进";
				body = actorName + "修为精进，踏入新的境界";
			}
			else if (XjRealmHelper.IsRealm(realmId, "TaiXi"))
			{
				title = "胎息初成";
				body = actorName + "感应气机，步入胎息之境";
			}
			else if (XjRealmHelper.IsRealm(realmId, "LianQi"))
			{
				title = "炼气有成";
				body = actorName + "引气入体，步入炼气之境";
			}
			else if (XjRealmHelper.IsRealm(realmId, "ZhuJi"))
			{
				eventType = "BreakthroughSuccess:ZhuJi";
				title = "道基初成";
				body = actorName + "筑基有成，气机沉凝，族中晚辈始知凡俗与修士之间，隔着一道真正的门槛。";
				importance = 2;
			}
			else if (XjRealmHelper.IsRealm(realmId, "ZiFu"))
			{
				eventType = "BreakthroughSuccess:ZiFu";
				XjFamilyRealmAchievement achievement = XjFamilyRealmAchievementNarrative.Resolve(
					domainEvent.FamilyStableId, domainEvent.ActorId, actorName, XjRealmIds.ZiFu);
				title = XjFamilyRealmAchievementNarrative.BuildShortTitle(in achievement, "紫府");
				body = actorName + "开辟紫府，神意内照"
					+ XjFamilyRealmAchievementNarrative.BuildEnding(in achievement, "紫府");
				importance = 3;
			}
			else if (XjRealmHelper.IsRealm(realmId, "JinDan"))
			{
				eventType = "BreakthroughSuccess:JinDan";
				title = "金丹成象";
				body = actorName + "丹成。是日族中诸修静坐不语，皆知此脉自此可被诸宗诸族记名。";
				importance = 5;
				relatedToHighGradeGongFa = true;
			}
			else
			{
				eventType = "BreakthroughSuccess:" + realmId;
				title = "道行精进";
				body = actorName + "突破至" + realmId;
			}
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeCaiQiCompleted)
		{
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string resource = ResolveCaiQiDisplayName(domainEvent.CaiQiResourceId);
			title = "采气有成";
			body = actorName + "采得" + resource + "，纳入囊中";
			relatedToFamilyWarehouse = true;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeCaiQiFaObtained)
		{
			eventType = XjChronicleEventTypes.CaiQiFaObtained;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string name = string.IsNullOrWhiteSpace(domainEvent.CaiQiFaName) ? "采气法" : domainEvent.CaiQiFaName;
			string daoTu = string.IsNullOrWhiteSpace(domainEvent.DaoTu) ? string.Empty : "（" + domainEvent.DaoTu + "）";
			string sourcePlace = string.IsNullOrWhiteSpace(domainEvent.CaiQiFaSourcePlace) ? string.Empty : "，源自" + domainEvent.CaiQiFaSourcePlace;
			title = "采气法传承";
			body = actorName + "获得采气法：" + name + daoTu + sourcePlace;
			relatedToFamilyWarehouse = true;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeFaBaoObtained)
		{
			eventType = XjChronicleEventTypes.FaBaoObtained;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string name = string.IsNullOrWhiteSpace(domainEvent.FaBaoName) ? "无名器物" : domainEvent.FaBaoName;
			string className = string.IsNullOrWhiteSpace(domainEvent.FaBaoClass) ? "器物" : domainEvent.FaBaoClass.Trim();
			string daoTu = string.IsNullOrWhiteSpace(domainEvent.DaoTu) ? string.Empty : "（" + domainEvent.DaoTu + "）";
			string source = string.IsNullOrWhiteSpace(domainEvent.Source) ? string.Empty : "，源自" + domainEvent.Source;
			title = className + "归位";
			body = actorName + "获得" + className + "《" + name + "》" + daoTu + source;
			relatedToFamilyWarehouse = true;
			relatedToHighGradeGongFa = true;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeGongFaObtained)
		{
			eventType = XjChronicleEventTypes.GongFaGenerated;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string gongFa = string.IsNullOrWhiteSpace(domainEvent.GongFaName) ? "功法" : domainEvent.GongFaName;
			int normalizedGrade = domainEvent.GongFaGrade > XjGongFaDefinition.MaxGrade ? XjGongFaDefinition.MaxGrade : domainEvent.GongFaGrade;
			eventType = "TechniqueRecovered:" + gongFa;
			title = normalizedGrade >= 5 ? "高法归族" : "功法传承";
			body = actorName + "得授《" + gongFa + "》（" + XjGongFaGradeText.Format(normalizedGrade) + "）";
			importance = normalizedGrade >= 5 ? 3 : 1;
			relatedToHighGradeGongFa = normalizedGrade >= 4;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeGongFaPromoted)
		{
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string gongFa = string.IsNullOrWhiteSpace(domainEvent.GongFaName) ? "功法" : domainEvent.GongFaName;
			title = "功法升品";
			body = actorName + "所修" + gongFa + "晋升至" + XjGongFaGradeText.Format(domainEvent.GongFaGrade);
			relatedToHighGradeGongFa = domainEvent.GongFaGrade >= 4;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeQiuJinFaComprehended)
		{
			eventType = XjChronicleEventTypes.QiuJinFaComprehended;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string name = string.IsNullOrWhiteSpace(domainEvent.QiuJinFaName) ? "求金法" : domainEvent.QiuJinFaName;
			eventType = "TechniqueRecovered:" + name;
			title = "金门初启";
			body = actorName + "悟得求金法《" + name + "》。族谱记曰：从前金丹是天边传闻，自此虽仍遥远，却已有一线可循。";
			importance = 5;
			relatedToHighGradeGongFa = true;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeJinDanSucceeded)
		{
			eventType = XjChronicleEventTypes.JinDanSucceeded;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string displayGuoWei = string.IsNullOrWhiteSpace(domainEvent.GuoWei)
				? "金丹果位"
				: XjGuoWeiCalculator.GetDisplayGuoWeiName(domainEvent.GuoWei);
			string daoTu = string.IsNullOrWhiteSpace(domainEvent.DaoTu) ? "未知道途" : domainEvent.DaoTu.Trim();
			title = "金丹成象";
			body = actorName + "证得【" + displayGuoWei + "】，金丹成象。自此【"
				+ daoTu + "】道脉为诸宗诸族所记。";
			importance = 5;
			relatedToHighGradeGongFa = true;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeShenDanSucceeded)
		{
			eventType = XjChronicleEventTypes.ShenDanSucceeded;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string displayGuoWei = string.IsNullOrWhiteSpace(domainEvent.GuoWei)
				? "金丹果位"
				: XjGuoWeiCalculator.GetDisplayGuoWeiName(domainEvent.GuoWei);
			string daoTu = string.IsNullOrWhiteSpace(domainEvent.DaoTu) ? "未知道途" : domainEvent.DaoTu.Trim();
			string anchorName = string.IsNullOrWhiteSpace(domainEvent.Source) ? "金丹真君" : domainEvent.Source.Trim();
			title = "托果成丹";
			body = actorName + "托果于【" + anchorName + "】，借【" + displayGuoWei
				+ "】余荫成就神丹。自此【" + daoTu + "】一脉又添高境。";
			importance = 5;
			relatedToHighGradeGongFa = true;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeDongTianOpened)
		{
			eventType = XjChronicleEventTypes.DongTianOpened;
			string actorName = DisplayActorName(domainEvent.ActorName, "修士");
			string dongTianName = string.IsNullOrWhiteSpace(domainEvent.Source) ? "奇遇洞天" : domainEvent.Source;
			title = "洞天显世";
			body = actorName + "附近显化了" + dongTianName + "，可供探寻";
			importance = 2;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeDongTianSurvived)
		{
			eventType = XjChronicleEventTypes.DongTianSurvived;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string dongTianName = string.IsNullOrWhiteSpace(domainEvent.Source) ? "洞天" : domainEvent.Source;
			string rewardSummary = string.IsNullOrWhiteSpace(domainEvent.CaiQiFaSourcePlace)
				? string.Empty
				: domainEvent.CaiQiFaSourcePlace.Trim();
			title = "洞天生还";
			body = string.IsNullOrWhiteSpace(rewardSummary)
				? actorName + "从" + dongTianName + "中生还"
				: actorName + "从" + dongTianName + "中生还，得" + rewardSummary;
			importance = string.IsNullOrWhiteSpace(rewardSummary) ? 2 : 3;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeDongTianDeath)
		{
			eventType = XjChronicleEventTypes.DongTianDeath;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string dongTianName = string.IsNullOrWhiteSpace(domainEvent.Source) ? "洞天" : domainEvent.Source;
			title = "洞天陨落";
			body = actorName + "在探索" + dongTianName + "时不幸身陨";
			importance = 1;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeDongTianClosed)
		{
			eventType = XjChronicleEventTypes.DongTianClosed;
			string dongTianName = string.IsNullOrWhiteSpace(domainEvent.Source) ? "洞天" : domainEvent.Source;
			title = "洞天暂闭";
			body = dongTianName + "此番显世已尽，洞门复闭，十洞轮转不息";
			importance = 1;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeFaBaoUpgraded)
		{
			eventType = XjChronicleEventTypes.FaBaoUpgraded;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string faBaoName = string.IsNullOrWhiteSpace(domainEvent.FaBaoName) ? "法宝" : domainEvent.FaBaoName;
			string newClass = string.IsNullOrWhiteSpace(domainEvent.GuoWei) ? "灵宝" : domainEvent.GuoWei;
			title = "法宝升阶";
			body = actorName + "的法宝《" + faBaoName + "》晋升为" + newClass;
			importance = 3;
			relatedToHighGradeGongFa = true;
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeJinXingObtained)
		{
			eventType = XjChronicleEventTypes.JinXingObtained;
			string actorName = DisplayActorName(domainEvent.ActorName, "族人");
			string jinXingName = string.IsNullOrWhiteSpace(domainEvent.GuoWei) ? "金性" : domainEvent.GuoWei;
			title = "金性归位";
			body = actorName + "获得" + jinXingName;
			importance = 4;
			relatedToHighGradeGongFa = true;
			return true;
		}

		return false;
	}

	private static string DisplayActorName(string actorName, string fallback)
	{
		return XjStringHelper.DisplayNameWithoutRealmSuffix(actorName, fallback);
	}

	private static string ResolveCaiQiDisplayName(string resourceId)
	{
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string displayName)
			&& !string.IsNullOrWhiteSpace(displayName))
		{
			return displayName.Trim();
		}

		return string.Equals(resourceId, "zaqi", StringComparison.Ordinal) ? "杂气" : "灵物";
	}

	private static string BuildEventKey(in XjFamilyDomainEvent domainEvent)
	{
		string name = domainEvent.GongFaName;
		if (string.IsNullOrWhiteSpace(name))
		{
			name = domainEvent.QiuJinFaName;
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			name = domainEvent.CaiQiResourceId;
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			name = domainEvent.CaiQiFaName;
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			name = domainEvent.FaBaoId;
		}

		return domainEvent.FamilyStableId
			+ "|"
			+ domainEvent.ActorId
			+ "|"
			+ domainEvent.EventType
			+ "|"
			+ domainEvent.Year
			+ "|"
			+ domainEvent.Source
			+ "|"
			+ (name ?? string.Empty).Trim();
	}

	private static bool ShouldRecordDomainEvent(in XjFamilyDomainEvent domainEvent)
	{
		// 采气法属于入门资源，不进入修士列传、世家纪事或宗门纪事。
		if (domainEvent.EventType == XjFamilyDomainEvent.TypeCaiQiFaObtained)
		{
			return false;
		}

		// 一至五品功法属于常见修行传承，不进入修士列传、世家纪事或宗门纪事。
		if ((domainEvent.EventType == XjFamilyDomainEvent.TypeGongFaObtained
				|| domainEvent.EventType == XjFamilyDomainEvent.TypeGongFaPromoted)
			&& domainEvent.GongFaGrade > 0
			&& domainEvent.GongFaGrade <= 5)
		{
			return false;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeBirth
			|| domainEvent.EventType == XjFamilyDomainEvent.TypeAptitudeGranted
			|| domainEvent.EventType == XjFamilyDomainEvent.TypeFamilyMemberConfirmed
			|| domainEvent.EventType == XjFamilyDomainEvent.TypeJinDanSucceeded)
		{
			// 金丹纪事由 XjChronicleWriter.RecordJinDanSucceeded 统一落盘，
			// 该入口携带金性、果位与宗门洞天信息，禁止领域事件重复写入第二条。
			return false;
		}

		// 洞天事件是全局性机缘事件，无论角色境界均记录
		if (domainEvent.EventType == XjFamilyDomainEvent.TypeDongTianOpened
			|| domainEvent.EventType == XjFamilyDomainEvent.TypeDongTianSurvived
			|| domainEvent.EventType == XjFamilyDomainEvent.TypeDongTianDeath
			|| domainEvent.EventType == XjFamilyDomainEvent.TypeDongTianClosed)
		{
			return true;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeRealmBreakthrough)
		{
			return ShouldRecordRealm(domainEvent.RealmId);
		}

		if (ShouldRecordRealm(domainEvent.RealmId))
		{
			return true;
		}

		if (XjFamilyReadModel.Shared.TryGetActor(domainEvent.ActorId, out Actor actor))
		{
			return ShouldRecordActor(actor);
		}

		return false;
	}

	private static bool ShouldRecordActor(Actor actor)
	{
		return XjRealmHelper.ShouldRecord(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
	}

	private static bool ShouldRecordRealm(string realmId)
	{
		return XjRealmHelper.ShouldRecord(realmId);
	}
}
