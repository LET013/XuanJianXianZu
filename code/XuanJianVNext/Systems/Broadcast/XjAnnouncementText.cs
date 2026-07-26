using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Broadcast;

internal static class XjAnnouncementText
{
	internal static bool TryBuildZiFuPromotion(Actor actor, out string text)
	{
		text = string.Empty;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(daoTu.Trim(), out _))
		{
			return false;
		}

		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		if (xianJi.Ids == null || xianJi.Ids.Length == 0 || string.IsNullOrWhiteSpace(xianJi.Ids[0]))
		{
			return false;
		}

		string clanName = ResolveClanName(actor);
		string actorName = ResolveActorName(actor);
		text = clanName + "，" + actorName + "，于今日证得【"
			+ daoTu.Trim() + "】神通【" + xianJi.Ids[0].Trim() + "】，称制紫府仙族。";
		return true;
	}

	internal static string BuildZiFuPromotion(Actor actor)
	{
		return TryBuildZiFuPromotion(actor, out string text) ? text : string.Empty;
	}

	internal static string BuildJinDanPromotion(Actor actor, string daoTu, string jinXing, string guoWei)
	{
		string displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		string promotionTitle = ResolveJinDanTitle(actor);
		return "本座" + ResolveActorName(actor) + "，今日证道【"
			+ Normalize(displayGuoWei, "金丹果位") + "】，成就【"
			+ Normalize(jinXing, "未定金性") + "】，晋位" + promotionTitle + "，天下【"
			+ Normalize(daoTu, "未知道途") + "】将兴！";
	}

	internal static string BuildHighRealmDeath(string actorName, string realmName, string attackerName)
	{
		string realm = Normalize(realmName, "高阶修士");
		string title = realm.Contains("金丹") ? "金丹归寂" : "紫府星沉";
		string cause = string.IsNullOrWhiteSpace(attackerName)
			? "身死道消，旧日威名自此入史"
			: "遭" + XjStringHelper.DisplayNameWithoutRealmSuffix(attackerName, "外敌") + "斩杀，一身道行归于天地";
		return "【" + title + "】" + XjStringHelper.DisplayNameWithoutRealmSuffix(actorName, "无名修士") + cause + "。";
	}

	internal static string BuildGuoWeiZhongAiZhengWeiLocked(
		string actorName,
		string daoTu,
		int deathYear,
		int lockUntilYear)
	{
		return "【果位钟爱·正位封锁】正位真君【"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(actorName, "无名真君")
			+ "】陨落，其所执【" + Normalize(daoTu, "未知道途")
			+ "】正位受果位钟爱牵引，自玄鉴历" + Math.Max(0, deathYear)
			+ "年封锁至" + Math.Max(Math.Max(0, deathYear), lockUntilYear)
			+ "年，静待转世承继。";
	}

	internal static string BuildSameDaoTuZhengWeiSuccession(
		string successorName,
		string victimName,
		string daoTu,
		string sourceGuoWei)
	{
		return "【同道夺正】"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(successorName, "无名金丹")
			+ "以【" + ResolveGuoWeiLabel(sourceGuoWei) + "】之身斩落正位真君【"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(victimName, "无名真君")
			+ "】，承继【" + Normalize(daoTu, "未知道途")
			+ "】正位，旋即退入洞天稳固果位。";
	}

	internal static string BuildAdjacentDaoTuZhengWeiSuccession(
		string successorName,
		string victimName,
		string sourceDaoTu,
		string targetDaoTu,
		string sourceGuoWei)
	{
		return "【转道夺正】"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(successorName, "无名金丹")
			+ "以【" + Normalize(sourceDaoTu, "原道途") + "】" + ResolveGuoWeiLabel(sourceGuoWei)
			+ "之身斩落正位真君【"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(victimName, "无名真君")
			+ "】，舍原道转入【" + Normalize(targetDaoTu, "未知道途")
			+ "】，承继正位，旋即退入洞天稳固果位。";
	}

	internal static string BuildRenDanRefined(string refinerName, string victimName, string gainedXianJi)
	{
		return "【人丹血劫】" + XjStringHelper.DisplayNameWithoutRealmSuffix(refinerName, "无名紫府")
			+ "早已暗中扶持" + XjStringHelper.DisplayNameWithoutRealmSuffix(victimName, "无名筑基")
			+ "修行，并将这名筑基下修炼作人丹，施展续途妙法，借此续接【"
			+ Normalize(gainedXianJi, "未知神通") + "】。";
	}

	internal static string BuildJinDanFailureDeath(Actor actor)
	{
		if (actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, out string narrative)
			&& !string.IsNullOrWhiteSpace(narrative))
		{
			return "【求金受算】" + narrative.Trim();
		}
		return "【求金陨落】" + ResolveActorName(actor)
			+ "冲击金丹失败，紫府崩散，真灵归于天地。";
	}

	internal static string BuildJinDanFailureDemonized(string originalName, string demonName)
	{
		return "【金性成魔】" + XjStringHelper.DisplayNameWithoutRealmSuffix(originalName, "无名紫府")
			+ "求金失败，金性不散，化作【"
			+ Normalize(demonName, "金性妖邪-无名神通") + "】。";
	}

	internal static string BuildYinSiDescended(Actor yinSi, Actor yaoXie)
	{
		return "【阴司降世】两名阴司早得金性异动之讯，循迹而来，已锁定【"
			+ ResolveActorName(yaoXie) + "】。";
	}

	internal static string BuildYinSiSuppressedJinXingYaoXie(string yaoXieName, string yinSiName)
	{
		return "【阴司诛邪】两名阴司早得金性异动之讯，循迹而来，斩灭【"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(yaoXieName, "金性妖邪")
			+ "】，散去其残存金性。";
	}

	internal static string BuildJinDanEraChangeCause(Actor actor, string daoTu, string jinXing, string guoWei)
	{
		string displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		return "【金丹晋升】" + ResolveActorName(actor) + "证得【"
			+ Normalize(displayGuoWei, "金丹果位") + "】，成就【"
			+ Normalize(jinXing, "未定金性") + "】，引动【"
			+ Normalize(daoTu, "未知道途") + "】道势";
	}

	internal static string BuildDongTianEraChangeCause(string dongTianName, string daoTuGroup, string locationInfo)
	{
		string safeLocation = Normalize(locationInfo, "世间");
		string locationText = string.Equals(safeLocation, "世间", StringComparison.Ordinal)
			? "于世间显世"
			: "于" + safeLocation + "附近显世";
		return "【洞天显世】奇遇洞天【" + Normalize(dongTianName, "无名洞天")
			+ "】" + locationText + "，引动【"
			+ Normalize(daoTuGroup, "未知道途") + "】道势";
	}

	internal static string BuildJieLinPromotion(Actor actor, int activeCount)
	{
		string actorName = ResolveBaseActorName(actor);
		string title = ResolveStoredTitle(actor, "太阴玄君");
		string countText = activeCount > 0
			? "，当世结璘仙自此共" + activeCount + "人"
			: string.Empty;
		return actorName + "今日成就结璘，受太阴正位赋予，晋号【"
			+ title + "】。太阴月华垂照" + countText + "，天下共鉴。";
	}

	internal static string BuildJinDanDongTianFoundation(
		Actor actor,
		City zongMenCity,
		string daoTu,
		string jinXing,
		string guoWei)
	{
		string zongMenName = Normalize(XjZongMenCityData.GetZongMenName(zongMenCity), "无名宗门");
		string dongTianName = Normalize(XjZongMenCityData.GetDongTianPeakName(zongMenCity), "宗门洞天");
		string displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		return "【" + zongMenName + "】钟磬齐鸣，云海开裂。"
			+ ResolveActorName(actor) + ResolveJinDanTitle(actor)
			+ "执掌【" + Normalize(displayGuoWei, "金丹果位") + "】，凝成【"
			+ Normalize(jinXing, "未定金性") + "】，于山门之内开立【" + dongTianName + "】。"
			+ "自此【" + Normalize(daoTu, "未知道途")
			+ "】一脉有天可栖、有法可传，诸峰同照，天地共鉴，道统永存。";
	}

	internal static string BuildJinDanDongTianTip(Actor actor, City zongMenCity, string guoWei)
	{
		string zongMenName = Normalize(XjZongMenCityData.GetZongMenName(zongMenCity), "无名宗门");
		string dongTianName = Normalize(XjZongMenCityData.GetDongTianPeakName(zongMenCity), "宗门洞天");
		string displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		return ResolveActorName(actor) + ResolveJinDanTitle(actor)
			+ "证得【" + Normalize(displayGuoWei, "金丹果位") + "】，于【"
			+ zongMenName + "】开立【" + dongTianName + "】！";
	}

	internal static string BuildQiuJinFaComprehension(Actor actor, string qiuJinFa, string gongFa, string daoTu)
	{
		return ResolveActorName(actor) + "，参悟求金法【" + Normalize(qiuJinFa, "无名求金法")
			+ "】，承于" + Normalize(gongFa, "无名功法") + "，为"
			+ Normalize(daoTu, "未知道途") + "开求金新径，天地共鉴。";
	}

	internal static string BuildZongMenFoundation(Actor actor, string zongMenName, string daoTu)
	{
		string sect = Normalize(zongMenName, "无名宗门");
		string dao = Normalize(daoTu, "未知道途");
		return "【" + sect + "】开山立派。" + ResolveActorName(actor)
			+ "于此立下山门，奉【" + dao + "】为宗脉，开峰收徒，定规传法。"
			+ "自此山门有主、法统有承，天下宗门谱再添一席。";
	}

	internal static string BuildZongMenFoundationTip(Actor actor, string zongMenName)
	{
		return ResolveActorName(actor) + "创立【" + Normalize(zongMenName, "无名宗门") + "】，开山收徒！";
	}

	internal static string BuildFaBaoResult(Actor actor, string className, string faBaoName, string source)
	{
		string actorName = ResolveActorName(actor);
		string safeClass = Normalize(className, "法宝");
		string safeName = Normalize(faBaoName, "无名法宝");
		string safeSource = (source ?? string.Empty).Trim();

		if (string.Equals(safeSource, "JieLinUpgrade", StringComparison.Ordinal))
		{
			return actorName + "成就结璘，所持《" + safeName + "》受太阴正位赋予，蜕变为" + safeClass + "。";
		}

		if (string.Equals(safeSource, "LingBaoUpgrade", StringComparison.Ordinal))
		{
			return actorName + "成就金丹，所持《" + safeName + "》由此蜕变为" + safeClass + "。";
		}

		if (string.Equals(safeSource, "JieLinGrant", StringComparison.Ordinal))
		{
			return actorName + "成就结璘，受太阴正位赋予" + safeClass + "《" + safeName + "》。";
		}

		if (string.Equals(safeSource, "QiYuDongTian", StringComparison.Ordinal))
		{
			return actorName + "于洞天机缘中得获" + safeClass + "《" + safeName + "》。";
		}

		return actorName + "炼成" + safeClass + "《" + safeName + "》。";
	}

	internal static string BuildDongTianSurvival(Actor actor, string dongTianName, bool rewardApplied, string rewardSummary)
	{
		string prefix = ResolveActorName(actor) + "从【" + Normalize(dongTianName, "奇遇洞天") + "】中生还";
		if (!rewardApplied)
		{
			return prefix + "，虽无所得，亦全身而退。";
		}

		return prefix + "，并得【" + Normalize(rewardSummary, "一桩机缘") + "】。";
	}

	internal static string BuildDongTianDeath(string explorerName, string dongTianName)
	{
		return XjStringHelper.DisplayNameWithoutRealmSuffix(explorerName, "探索者") + "陨于【"
			+ Normalize(dongTianName, "奇遇洞天") + "】之中。";
	}

	internal static string BuildDongTianClosed(string dongTianName)
	{
		return "【" + Normalize(dongTianName, "奇遇洞天")
			+ "】此番显世已尽，洞门复闭，九洞轮转不息。";
	}

	private static string ResolveJinDanTitle(Actor actor)
	{
		return XjLongShuSystem.IsLongShu(actor) ? "龙君" : "真君";
	}

	private static string ResolveClanName(Actor actor)
	{
		try
		{
			string clanName = actor?.clan?.data == null
				? string.Empty
				: ((BaseSystemData)actor.clan.data).name;
			return Normalize(clanName, "筑基世家");
		}
		catch
		{
			return "筑基世家";
		}
	}

	private static string ResolveActorName(Actor actor)
	{
		return XjStringHelper.ActorNameWithoutRealmSuffix(actor, "无名修士");
	}

	private static string ResolveBaseActorName(Actor actor)
	{
		if (actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameBase, out string baseName)
			&& !string.IsNullOrWhiteSpace(baseName))
		{
			return baseName.Trim();
		}

		string name = ResolveActorName(actor);
		int titleSeparator = name.IndexOf('·');
		if (titleSeparator >= 0 && titleSeparator + 1 < name.Length)
		{
			name = name.Substring(titleSeparator + 1).Trim();
		}

		const string realmSuffix = "-结璘仙";
		while (name.EndsWith(realmSuffix, StringComparison.Ordinal))
		{
			name = name.Substring(0, name.Length - realmSuffix.Length).TrimEnd();
		}

		return Normalize(name, "无名修士");
	}

	private static string ResolveStoredTitle(Actor actor, string fallback)
	{
		if (actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string title)
			&& !string.IsNullOrWhiteSpace(title))
		{
			return title.Trim();
		}

		return fallback;
	}

	private static string ResolveGuoWeiLabel(string guoWei)
	{
		string display = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		if (string.IsNullOrWhiteSpace(display)) return "余闰位";
		if (display.IndexOf(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal) >= 0) return XjGuoWeiCalculator.YuWei;
		if (display.IndexOf(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) >= 0) return XjGuoWeiCalculator.RunWei;
		return display.Trim();
	}

	private static string Normalize(string value, string fallback)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return fallback;
		}

		string normalized = value.Trim();
		return string.Equals(normalized, "NO_NAME", StringComparison.OrdinalIgnoreCase)
			? fallback
			: normalized;
	}
}
