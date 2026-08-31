using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Chronicle;

internal static partial class XjChronicleWriter
{
	internal static bool RecordBirth(Actor actor, int timestamp)
	{
		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "族人");
		return RecordRich(actor, XjChronicleEventTypes.Birth, timestamp, "新丁降世", actorName + "降生于世");
	}

	internal static bool RecordAptitudeGranted(Actor actor, int timestamp)
	{
		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "族人");
		return RecordRich(actor, XjChronicleEventTypes.AptitudeGranted, timestamp, "灵光初显", actorName + "展露天资，灵光初显");
	}

	internal static bool RecordRealmBreakthrough(Actor actor, int timestamp, string realmId)
	{
		if (!XjRealmHelper.ShouldRecord(realmId))
		{
			return false;
		}

		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "族人");
		string realmName = XjRealmHelper.GetDisplayName(realmId);
		string title;
		string body;
		if (string.IsNullOrWhiteSpace(realmId))
		{
			title = "道行精进";
			body = actorName + "修为精进，踏入新的境界";
		}
		else
		{
			if (XjRealmHelper.IsRealm(realmId, "TaiXi"))
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
				title = "道基初成";
				body = actorName + "筑基有成，气机沉凝，族中晚辈始知凡俗与修士之间，隔着一道真正的门槛。";
			}
			else if (XjRealmHelper.IsRealm(realmId, "ZiFu"))
			{
				title = "紫府开门";
				body = actorName + "开辟紫府，神意内照。族运兴衰，自此已有半分握在自己手中。";
			}
			else if (XjRealmHelper.IsRealm(realmId, "JinDan"))
			{
				title = "金丹成象";
				body = actorName + "丹成。是日族中诸修静坐不语，皆知此脉自此可被诸宗诸族记名。";
			}
			else
			{
				title = "境界突破";
				body = actorName + "突破至" + realmName;
			}
		}
		int importance = XjRealmHelper.IsRealm(realmId, "JinDan") ? 4
			: XjRealmHelper.IsRealm(realmId, "ZiFu") ? 3
			: XjRealmHelper.IsRealm(realmId, "ZhuJi") ? 2
			: 1;
		return RecordRich(actor, "BreakthroughSuccess:" + (realmId ?? string.Empty), timestamp, title, body, importance, importance >= 4, source: "breakthrough.success");
	}

	internal static bool RecordGongFaGenerated(Actor actor, int timestamp, string gongFaName, int grade)
	{
		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "族人");
		string gongFa = string.IsNullOrWhiteSpace(gongFaName) ? "功法" : gongFaName;
		int normalizedGrade = grade > XjGongFaDefinition.MaxGrade ? XjGongFaDefinition.MaxGrade : grade;
		string title = normalizedGrade >= 5 ? "高法归族" : "功法传承";
		string body = actorName + "得授《" + gongFa + "》（" + normalizedGrade + "品）";
		return RecordRich(actor, "TechniqueRecovered:" + gongFa, timestamp, title, body, normalizedGrade >= 5 ? 3 : 1, normalizedGrade >= 5, relatedToHighGradeGongFa: normalizedGrade >= 4, source: "technique.recovered");
	}

	internal static bool RecordGongFaLost(Actor actor, int timestamp, string gongFaName, int grade)
	{
		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "族人");
		string gongFa = string.IsNullOrWhiteSpace(gongFaName) ? "功法" : gongFaName;
		string title = "功法失传";
		string body = actorName + "所修" + gongFa + "（" + grade + "品）失传于世间";
		return RecordRich(actor, XjChronicleEventTypes.GongFaLost, timestamp, title, body, importance: 2);
	}

	internal static bool RecordCaiQiCompleted(Actor actor, int timestamp, string resourceId)
	{
		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "族人");
		string resource = ResolveCaiQiDisplayName(resourceId);
		string title = "采气有成";
		string body = actorName + "采得" + resource + "，纳入囊中";
		return RecordRich(actor, XjChronicleEventTypes.CaiQiCompleted, timestamp, title, body, relatedToFamilyWarehouse: true);
	}

	internal static bool RecordOtherShenTongMisled(
		Actor actor,
		int timestamp,
		string shenTongDaoTu,
		string shenTongName,
		string deceiverName = "",
		string method = "欺骗")
	{
		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "修士");
		string daoTu = string.IsNullOrWhiteSpace(shenTongDaoTu) ? "异道" : shenTongDaoTu.Trim();
		string shenTong = string.IsNullOrWhiteSpace(shenTongName) ? "异道神通" : shenTongName.Trim();
		string sourceName = string.IsNullOrWhiteSpace(deceiverName) ? "一名来历不明的修士" : deceiverName.Trim();
		string cause = string.Equals(method, "蛊惑", System.StringComparison.Ordinal) ? "蛊惑" : "欺骗";
		string body = actorName + "受" + sourceName + cause + "，误修" + daoTu + "道途神通【" + shenTong + "】"
			+ "，已入歧途而不自知，自此金丹前路断绝。";
		return RecordRich(
			actor,
			"OtherShenTongMisled:" + shenTong,
			timestamp,
			"误入歧途",
			body,
			importance: 4,
			isProtected: true,
			relatedToHighGradeGongFa: true,
			source: "shenTong.other.misled");
	}

	internal static bool RecordQiuJinFaComprehended(Actor actor, int timestamp, string qiuJinFaName)
	{
		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "族人");
		string name = string.IsNullOrWhiteSpace(qiuJinFaName) ? "求金法" : qiuJinFaName;
		string title = "金门初启";
		string body = actorName + "悟得求金法《" + name + "》。族谱记曰：从前金丹是天边传闻，自此虽仍遥远，却已有一线可循。";
		return RecordRich(actor, "TechniqueRecovered:" + name, timestamp, title, body, 5, true, relatedToHighGradeGongFa: true, source: "technique.first_qiujin");
	}

	private static string ResolveCaiQiDisplayName(string resourceId)
	{
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string displayName)
			&& !string.IsNullOrWhiteSpace(displayName))
		{
			return displayName.Trim();
		}

		return string.Equals(resourceId, "zaqi", System.StringComparison.Ordinal) ? "杂气" : "灵物";
	}

	internal static bool RecordJinDanSucceeded(
		Actor actor,
		int timestamp,
		string guoWei,
		string jinXing,
		string daoTu,
		bool foundedDongTian,
		string zongMenName,
		string dongTianName)
	{
		string actorName = XjStringHelper.ActorNameWithoutRealmSuffix(actor, "族人");
		string displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		string liveRealmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		bool isFuQiZhenJun = string.Equals(liveRealmId, XjRealmIds.ZhenJunYuShi, System.StringComparison.Ordinal);
		string titleName = XjLongShuSystem.IsLongShu(actor) ? "龙君" : isFuQiZhenJun ? "真君羽士" : "真君";
		string safeGuoWei = string.IsNullOrWhiteSpace(displayGuoWei)
			? (isFuQiZhenJun ? "真君果位" : "金丹果位")
			: displayGuoWei.Trim();
		string safeJinXing = string.IsNullOrWhiteSpace(jinXing) ? "未定金性" : jinXing.Trim();
		string safeDaoTu = string.IsNullOrWhiteSpace(daoTu) ? "未知道途" : daoTu.Trim();
		string familyRealmLabel = isFuQiZhenJun ? "真君" : "金丹";
		string targetRealmId = isFuQiZhenJun ? XjRealmIds.ZhenJunYuShi : XjRealmIds.JinDan;
		XjFamilyRealmAchievement familyAchievement = XjFamilyRealmAchievementNarrative.Resolve(actor, targetRealmId);
		string body = actorName + "证得【" + safeGuoWei + "】，凝成【" + safeJinXing
			+ "】，晋位" + titleName + "。自此【" + safeDaoTu + "】道脉为诸宗诸族所记"
			+ XjFamilyRealmAchievementNarrative.BuildEnding(in familyAchievement, familyRealmLabel);
		if (foundedDongTian)
		{
			string safeZongMen = string.IsNullOrWhiteSpace(zongMenName) ? "宗门" : zongMenName.Trim();
			string safeDongTian = string.IsNullOrWhiteSpace(dongTianName) ? "宗门洞天" : dongTianName.Trim();
			body += "其后于【" + safeZongMen + "】开立【" + safeDongTian
				+ "】，一脉有天可栖、有法可传，道统自此长存。";
		}
		return RecordRich(
			actor,
			XjChronicleEventTypes.JinDanSucceeded,
			timestamp,
			XjFamilyRealmAchievementNarrative.BuildShortTitle(in familyAchievement, familyRealmLabel),
			body,
			importance: 5,
			isProtected: true,
			relatedToHighGradeGongFa: true,
			source: "breakthrough.jindan");
	}

	internal static bool RecordJieLinSucceeded(Actor actor, int timestamp, int activeCount)
	{
		if (actor?.data == null)
		{
			return false;
		}

		string actorName = actor.getName() ?? "族人";
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameBase, out string baseName)
			&& !string.IsNullOrWhiteSpace(baseName))
		{
			actorName = baseName.Trim();
		}

		string titleName = "太阴玄君";
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle)
			&& !string.IsNullOrWhiteSpace(storedTitle))
		{
			titleName = storedTitle.Trim();
		}

		string countText = activeCount > 0
			? "当世结璘仙自此共" + activeCount + "人。"
			: "列于当世结璘仙之中。";
		string body = actorName + "成就结璘，受太阴果位赋予，晋号【" + titleName
			+ "】。初成不占天地位序，积修成熟后可再证余位或闰位。" + countText;
		return RecordRich(
			actor,
			XjChronicleEventTypes.JieLinSucceeded,
			timestamp,
			"结璘成就",
			body,
			importance: 5,
			isProtected: true,
			relatedToHighGradeGongFa: true,
			source: "breakthrough.jielin");
	}

	internal static bool RecordYuYiSucceeded(Actor actor, int timestamp, int activeCount)
	{
		if (actor?.data == null) return false;
		string actorName = actor.getName() ?? "族人";
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameBase, out string baseName)
			&& !string.IsNullOrWhiteSpace(baseName)) actorName = baseName.Trim();
		string titleName = "太阳玄君";
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle)
			&& !string.IsNullOrWhiteSpace(storedTitle)) titleName = storedTitle.Trim();
		string countText = activeCount > 0 ? "当世郁仪仙自此共" + activeCount + "人。" : string.Empty;
		return RecordRich(actor, XjChronicleEventTypes.YuYiSucceeded, timestamp, "郁仪成就",
			actorName + "求金未成，受太阳果位日精垂照，得郁仪文而成郁仪仙，晋号【"
			+ titleName + "】。初成不占天地位序，积修成熟后可再证余位或闰位。" + countText,
			importance: 5, isProtected: true, relatedToHighGradeGongFa: true, source: "breakthrough.yuyi");
	}

	internal static bool RecordBreakthroughBlocked(Actor actor, int timestamp, string targetRealmId)
	{
		string actorName = actor?.getName() ?? "族人";
		string realmName = XjRealmHelper.GetDisplayName(targetRealmId);
		string title = "破境受阻";
		string body = string.IsNullOrWhiteSpace(realmName)
			? actorName + "试图突破境界，却在关键时刻被心魔所阻，功亏一篑"
			: actorName + "试图突破" + realmName + "，却在关键时刻被心魔所阻，功亏一篑";
		return RecordRich(actor, XjChronicleEventTypes.BreakthroughBlocked, timestamp, title, body, importance: 2);
	}

	internal static bool RecordJinDanFailureDemonized(
		Actor actor,
		int timestamp,
		string originalName = null,
		string demonName = null)
	{
		string before = string.IsNullOrWhiteSpace(originalName) ? "无名紫府" : originalName.Trim();
		string after = string.IsNullOrWhiteSpace(demonName)
			? (actor?.getName() ?? "金性妖邪")
			: demonName.Trim();
		string title = "金丹成魔";
		string body = before + "冲击金丹失败，金性不散，化为" + after
			+ "；其金性已惊动阴司，追索将至。";
		return RecordRich(actor, XjChronicleEventTypes.JinDanFailureDemonized, timestamp, title, body, importance: 5, isProtected: true, source: "breakthrough.jindan.demonized");
	}

	internal static bool RecordJinDanFailureDeath(Actor actor, int timestamp)
	{
		string actorName = actor?.getName() ?? "族人";
		string title = "求金陨落";
		string body;
		if (actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, out string narrative)
			&& !string.IsNullOrWhiteSpace(narrative))
		{
			title = "求金受算";
			body = narrative.Trim() + "族中求金之路由此再添一笔阴影。";
		}
		else
		{
			body = actorName + "冲击金丹失败，紫府崩散，真灵归于天地。族中求金之路由此再添一笔血色。";
		}
		return RecordRich(actor, XjChronicleEventTypes.JinDanFailureDeath, timestamp, title, body, importance: 5, isProtected: true, source: "breakthrough.jindan.death");
	}

	internal static bool RecordJinXingYaoXieSuppressed(Actor actor, int timestamp, string yinSiName)
	{
		string actorName = actor?.getName() ?? "金性妖邪";
		string executor = string.IsNullOrWhiteSpace(yinSiName) ? "阴司" : yinSiName.Trim();
		string title = "阴司诛邪";
		string body = executor + "循金性而至，斩灭" + actorName + "，其残存金性自此散入天地。";
		return RecordRich(actor, XjChronicleEventTypes.JinXingYaoXieSuppressed, timestamp, title, body, importance: 5, isProtected: true, source: "jinxing_yaoxie.suppressed");
	}

	internal static bool RecordYinSiDescended(Actor yaoXie, int timestamp, string yinSiName)
	{
		string actorName = yaoXie?.getName() ?? "金性妖邪";
		string executor = string.IsNullOrWhiteSpace(yinSiName) ? "阴司" : yinSiName.Trim();
		string title = "阴司降世";
		string body = executor + "循" + actorName + "残存金性而来，自降世起便与其不死不休。";
		return RecordRich(yaoXie, XjChronicleEventTypes.YinSiDescended, timestamp, title, body, importance: 4, isProtected: true, source: "yinsi.descended");
	}

	internal static bool RecordHighTierSpellTriggered(Actor actor, int timestamp, string spellName)
	{
		string actorName = actor?.getName() ?? "族人";
		string title = "神通自现";
		string body = string.IsNullOrWhiteSpace(spellName)
			? actorName + "引动体内神通，灵光迸发"
			: actorName + "引动" + spellName + "，灵光迸发，威势惊人";
		return RecordRich(actor, XjChronicleEventTypes.HighTierSpellTriggered, timestamp, title, body);
	}

	internal static bool RecordZongMenFounded(Actor actor, int timestamp, string zongMenName, string daoTu)
	{
		string actorName = actor?.getName() ?? "族人";
		string zongMen = string.IsNullOrWhiteSpace(zongMenName) ? "无名宗门" : zongMenName.Trim();
		string dao = string.IsNullOrWhiteSpace(daoTu) ? "未知道途" : daoTu.Trim();
		string title = "开山立派";
		string body = actorName + "创立【" + zongMen + "】，奉【" + dao
			+ "】为宗脉，开山收徒，立法传承。自此山门有主，道统有承。";
		return RecordRich(
			actor,
			XjChronicleEventTypes.ZongMenFounded,
			timestamp,
			title,
			body,
			importance: 4,
			isProtected: true,
			source: "zongmen.foundation");
	}

	internal static bool RecordZongMenBackflow(Actor actor, int timestamp, string gongFaName, int grade, string zongMenName)
	{
		string actorName = actor?.getName() ?? "族人";
		string gongFa = string.IsNullOrWhiteSpace(gongFaName) ? "功法" : gongFaName;
		string zongMen = string.IsNullOrWhiteSpace(zongMenName) ? "宗门" : zongMenName;
		string title = "宗门回馈";
		string body = zongMen + "弟子" + actorName + "将所悟" + gongFa + "（" + grade + "品）回传家族";
		return RecordRich(actor, XjChronicleEventTypes.ZongMenBackflow, timestamp, title, body, relatedToHighGradeGongFa: grade >= 4);
	}
}
