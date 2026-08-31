using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Sect;

using XuanJianVNext.Systems.History;
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

		XjFamilyRealmAchievement family = XjFamilyRealmAchievementNarrative.Resolve(actor, XjRealmIds.ZiFu);
		string actorName = ResolveActorName(actor);
		text = XjFamilyRealmAchievementNarrative.BuildTag(in family, "紫府")
			+ actorName + "于今日证得【" + daoTu.Trim() + "】神通【" + xianJi.Ids[0].Trim() + "】"
			+ XjFamilyRealmAchievementNarrative.BuildEnding(in family, "紫府");
		return true;
	}

	internal static string BuildZiFuPromotion(Actor actor)
	{
		return TryBuildZiFuPromotion(actor, out string text) ? text : string.Empty;
	}

	internal static string BuildJinDanPromotion(Actor actor, string daoTu, string jinXing, string guoWei)
	{
		XjFamilyRealmAchievement family = XjFamilyRealmAchievementNarrative.Resolve(actor, XjRealmIds.JinDan);
		string body = BuildHighRealmProofDeclaration(actor, daoTu, jinXing, guoWei, ResolveJinDanTitle(actor), string.Empty);
		return XjFamilyRealmAchievementNarrative.BuildTag(in family, "金丹")
			+ CombineWithFamilyEnding(body, in family, "金丹");
	}

	internal static string BuildFuQiZhenRenPromotion(Actor actor, string daoTu, string coreName)
	{
		string resolvedDaoTu = ResolveFuQiAnnouncementDaoTu(daoTu, coreName);
		XjFamilyRealmAchievement family = XjFamilyRealmAchievementNarrative.Resolve(actor, XjRealmIds.FuQiZhenRen);
		return XjFamilyRealmAchievementNarrative.BuildTag(in family, "真人")
			+ "【服气真人】" + ResolveBaseActorName(actor) + "以【"
			+ Normalize(coreName, "本命神妙") + "】求身有成，登临真人，所修【"
			+ Normalize(resolvedDaoTu, "未知道途") + "】由此再进一步"
			+ XjFamilyRealmAchievementNarrative.BuildEnding(in family, "真人");
	}

	internal static string BuildFuQiZhenJunPromotion(
		Actor actor,
		string daoTu,
		string coreName,
		string jinXing,
		string guoWei)
	{
		daoTu = ResolveFuQiAnnouncementDaoTu(daoTu, coreName);
		XjFamilyRealmAchievement family = XjFamilyRealmAchievementNarrative.Resolve(actor, XjRealmIds.ZhenJunYuShi);
		string body = BuildHighRealmProofDeclaration(actor, daoTu, jinXing, guoWei, "真君羽士", coreName);
		return XjFamilyRealmAchievementNarrative.BuildTag(in family, "真君")
			+ CombineWithFamilyEnding(body, in family, "真君");
	}

	internal static string BuildDaoTaiPromotion(Actor actor, string daoTu, string guoWei, int currentYear)
	{
		XjFamilyRealmAchievement family = XjFamilyRealmAchievementNarrative.Resolve(actor, XjRealmIds.DaoTai);
		string body = BuildDaoTaiDeclaration(actor, daoTu, guoWei, currentYear);
		return XjFamilyRealmAchievementNarrative.BuildTag(in family, "道胎")
			+ CombineWithFamilyEnding(body, in family, "道胎");
	}

	internal static string BuildFuQiShenDanPromotion(
		Actor actor,
		string daoTu,
		string coreName,
		string anchorName,
		string guoWei)
	{
		string displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		daoTu = ResolveFuQiAnnouncementDaoTu(daoTu, coreName);
		return "【服气神丹】" + ResolveBaseActorName(actor) + "圆满【"
			+ Normalize(coreName, "本命神妙") + "】，依附真君【"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(anchorName, "无名真君") + "】所执【"
			+ Normalize(displayGuoWei, "金丹果位") + "】成就神丹，仍以服气养性为修法，列入【"
			+ Normalize(daoTu, "未知道途") + "】真君之席。";
	}

	internal static string BuildHighRealmDeath(string actorName, string realmName, string attackerName)
	{
		string realm = Normalize(realmName, "高阶修士");
		bool isZhenJun = realm.Contains("金丹")
			|| realm.Contains("神丹")
			|| realm.Contains("真君")
			|| realm.Contains("羽士")
			|| realm.Contains("结璘");
		string title = isZhenJun ? "真君归寂" : "真人星沉";
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
		return "【果位钟爱·果位封锁】果位真君【"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(actorName, "无名真君")
			+ "】陨落，其所执【" + Normalize(daoTu, "未知道途")
			+ "】所持果位受天地钟爱牵引，自" + XjChronology.FormatYear(deathYear)
			+ "封锁至" + XjChronology.FormatYear(Math.Max(Math.Max(0, deathYear), lockUntilYear))
			+ "，静待转世承继。";
	}

	internal static string BuildGuoWeiOwnerRevealedByProbe(
		string seekerName,
		string daoTu,
		string guoWei,
		int hiddenYears)
	{
		string displayPosition = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		return "【探位显主】"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(seekerName, "无名紫府")
			+ "求证【" + Normalize(displayPosition, Normalize(daoTu, "未知道途") + "果位")
			+ "】，金性将落、位门将合之际，却被一线沉寂已久的旧道痕截断，于最后一步功败。"
			+ "天下众修由此惊觉：此位竟早已有主存世，据位真君至少已隐伏"
			+ Math.Max(100, hiddenYears) + "年。";
	}

	internal static string BuildGuoWeiOwnerRevealedByProbeTip(string seekerName, string guoWei)
	{
		return "【果位显主】"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(seekerName, "无名紫府")
			+ "求证【" + Normalize(XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei), "未名果位")
			+ "】止于最后一步，天下众修方知此位竟早已有主存世。";
	}

	internal static string BuildSameDaoTuZhengWeiSuccession(
		string successorName,
		string victimName,
		string daoTu,
		string sourceGuoWei)
	{
		return "【同道夺正】"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(successorName, "无名金丹")
			+ "以【" + ResolveGuoWeiLabel(sourceGuoWei) + "】之身斩落果位真君【"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(victimName, "无名真君")
			+ "】，承继【" + Normalize(daoTu, "未知道途")
			+ "】所持果位，旋即退入洞天稳固果位。";
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
			+ "之身斩落果位真君【"
			+ XjStringHelper.DisplayNameWithoutRealmSuffix(victimName, "无名真君")
			+ "】，舍原道转入【" + Normalize(targetDaoTu, "未知道途")
			+ "】，承继果位，旋即退入洞天稳固果位。";
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
			bool fourthAptitude = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
				&& aptitude == 4;
			return (fourthAptitude ? "【大限搏金】" : "【求金受算】") + narrative.Trim();
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
		XjHighRealmDoctrineSnapshot state = XjHighRealmDaoStateService.BuildSnapshot(actor);
		string displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		string method = state.Found && !string.IsNullOrWhiteSpace(state.RunFormula) ? state.RunFormula + "，" : string.Empty;
		return "【金丹晋升】" + ResolveActorName(actor) + method + "证得【"
			+ Normalize(displayGuoWei, "金丹果位") + "】，成就【"
			+ Normalize(jinXing, "未定金性") + "】，引动【"
			+ Normalize(daoTu, "未知道途") + "】道势";
	}

	internal static string BuildFuQiZhenJunEraChangeCause(Actor actor, string daoTu, string jinXing, string guoWei)
	{
		string displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		return "【真君羽士晋升】" + ResolveActorName(actor) + "证得【"
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
		return actorName + "今日成就结璘，受太阴果位赋予，晋号【"
			+ title + "】。太阴月华垂照" + countText + "，天下共鉴。";
	}

	internal static string BuildYuYiPromotion(Actor actor, int activeCount)
	{
		string actorName = ResolveBaseActorName(actor);
		string title = ResolveStoredTitle(actor, "太阳玄君");
		string countText = activeCount > 0 ? "，当世郁仪仙自此共" + activeCount + "人" : string.Empty;
		return actorName + "求金失利，反受太阳果位日精垂照，得【郁仪文】，晋号【"
			+ title + "】" + countText + "。";
	}

	internal static string BuildJinDanDongTianFoundation(
		Actor actor,
		City zongMenCity,
		string daoTu,
		string jinXing,
		string guoWei)
	{
		string zongMenName = Normalize(XjSectCityData.GetZongMenName(zongMenCity), "无名宗门");
		string dongTianName = Normalize(XjSectCityData.GetDongTianPeakName(zongMenCity), "宗门洞天");
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
		string zongMenName = Normalize(XjSectCityData.GetZongMenName(zongMenCity), "无名宗门");
		string dongTianName = Normalize(XjSectCityData.GetDongTianPeakName(zongMenCity), "宗门洞天");
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

		if (string.Equals(safeSource, "DaoTaiXianQi", StringComparison.Ordinal))
		{
			return "【仙器蜕生】" + actorName + "温养本命法宝《" + safeName
				+ "》五百年，忽有仙机自器骨中生，旧器不易其名，升格为" + safeClass + "！";
		}

		if (string.Equals(safeSource, "JieLinUpgrade", StringComparison.Ordinal))
		{
			return actorName + "成就结璘，所持《" + safeName + "》受太阴果位赋予，蜕变为" + safeClass + "。";
		}

		if (string.Equals(safeSource, "LingBaoUpgrade", StringComparison.Ordinal))
		{
			return actorName + "成就金丹，所持《" + safeName + "》由此蜕变为" + safeClass + "。";
		}

		if (string.Equals(safeSource, "JieLinGrant", StringComparison.Ordinal))
		{
			return actorName + "成就结璘，受太阴果位赋予" + safeClass + "《" + safeName + "》。";
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
			+ "】此番显世已尽，洞门复闭，十洞轮转不息。";
	}

	private static string BuildHighRealmProofDeclaration(
		Actor actor, string fallbackDaoTu, string fallbackJinXing, string guoWei, string promotionTitle, string coreName)
	{
		XjHighRealmDoctrineSnapshot state = XjHighRealmDaoStateService.BuildSnapshot(actor);
		string positionType = state.Found
			? XjGuoWeiCalculator.NormalizePositionType(state.PositionType)
			: XjGuoWeiRegistry.ResolveTypeFromName(guoWei);
		string manifest = state.Found ? state.ManifestDaoTu : Normalize(fallbackDaoTu, "未知道途");
		string source = state.Found ? state.SourceDaoTu : manifest;
		string title = state.Found ? Normalize(state.DaoTitle, "玄清") : "玄清";
		string city = state.Found ? Normalize(state.ProofCityName, "世间") : ResolveProofCityName(actor);
		string doctrine = state.Found ? Normalize(state.Doctrine, "大道有凭") : "大道有凭";
		string legacy = state.Found ? Normalize(state.LegacyDoctrine, ShortDao(manifest) + "宣法") : ShortDao(manifest) + "宣法";
		string scope = state.Found ? Normalize(state.AuthorityScope, "诸象") : "诸象";
		string scopeDisplay = ResolveAuthorityScopeDisplay(manifest, scope);
		string jinXing = state.Found ? Normalize(state.JinXing, fallbackJinXing) : Normalize(fallbackJinXing, "未定金性");
		string coreClause = string.IsNullOrWhiteSpace(coreName) ? string.Empty : "，本命【" + coreName.Trim() + "】同证圆满";
		string positionName = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		if (string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			string formula = state.Found ? Normalize(state.RunFormula, "借" + source + "闰" + manifest)
				: "借" + source + "闰" + manifest;
			return "本座" + title + "，今日" + formula + "，于" + city + "证道。"
				+ doctrine + "；成就" + manifest + "闰位，证得" + jinXing + "，晋位" + promotionTitle + coreClause + "。"
				+ "天下" + scopeDisplay + "，凡" + ShortDao(manifest) + ShortDao(source) + "一系灵源"
				+ ResolveElementCategory(manifest, source) + "，皆归予辖。"
				+ city + "留存道基，刻印" + legacy + "，以资后人。";
		}
		if (string.Equals(positionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			bool hasFruitLord = XjGuoWeiRegistry.TryFindActiveAnchor(
				manifest, XjGuoWeiCalculator.ZhengWei, out _);
			string relation = hasFruitLord
				? "正受已有主君，予执偏裨之柄，辅翼主位、镇守旁支，法不僭正，位不夺统。"
				: "正受尚且空悬，予执偏裨之柄，护持道脉、镇守旁支，待主君归位而不使法统断绝。";
			return "本座" + title + "，今日于" + city + "承" + ShortDao(manifest) + "之辅，奉本道正统，列入【"
				+ Normalize(positionName, manifest + "余位") + "】。"
				+ doctrine + "；余以辅正，偏裨持脉，证得" + jinXing + "，晋位" + promotionTitle + coreClause + "。"
				+ relation
				+ "天下" + scopeDisplay + "，凡" + ShortDao(manifest) + "一系灵源"
				+ ResolveElementCategory(manifest, manifest) + "，皆受予护持。"
				+ city + "留存道基，刻印" + legacy + "，以资后人。";
		}
		return "本座" + title + "，今日于" + city + "正受" + ShortDao(manifest)
			+ "，承一道大统，登临【" + Normalize(positionName, manifest + "果位") + "】。"
			+ doctrine + "；正性既定，万象归宗，证得" + jinXing + "，晋位"
			+ promotionTitle + coreClause + "。"
			+ "自今日起，天下" + scopeDisplay + "，凡" + ShortDao(manifest) + "一系灵源"
			+ ResolveElementCategory(manifest, manifest) + "，皆归予辖；"
			+ "本道果、余、闰之位序，神通之显藏，权柄之授夺，传承之兴替，皆由予总摄，为一系主君。"
			+ city + "留存道基，刻印" + legacy + "，定此世" + manifest + "正法，以资后人。";
	}

	private static string CombineWithFamilyEnding(string body, in XjFamilyRealmAchievement family, string realmLabel)
	{
		string ending = XjFamilyRealmAchievementNarrative.BuildEnding(in family, realmLabel);
		if (EndsWithTerminalPunctuation(body) && StartsWithComma(ending))
		{
			ending = ending.Substring(1);
		}
		return body + ending;
	}

	private static bool StartsWithComma(string text)
	{
		if (string.IsNullOrEmpty(text)) return false;
		return text[0] == '，' || text[0] == ',';
	}

	private static bool EndsWithTerminalPunctuation(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return false;
		string trimmed = text.TrimEnd();
		char last = trimmed[trimmed.Length - 1];
		return last == '。' || last == '！' || last == '？' || last == '；'
			|| last == '.' || last == '!' || last == '?' || last == ';';
	}

	private static string BuildDaoTaiDeclaration(Actor actor, string fallbackDaoTu, string guoWei, int currentYear)
	{
		XjHighRealmDoctrineSnapshot state = XjHighRealmDaoStateService.BuildSnapshot(actor);
		string positionType = state.Found
			? XjGuoWeiCalculator.NormalizePositionType(state.PositionType)
			: XjGuoWeiRegistry.ResolveTypeFromName(guoWei);
		string manifest = state.Found ? state.ManifestDaoTu : Normalize(fallbackDaoTu, "未知道途");
		string source = state.Found ? state.SourceDaoTu : manifest;
		string title = state.Found ? Normalize(state.DaoTitle, "玄清") : "玄清";
		string city = state.Found ? Normalize(state.ProofCityName, "世间") : ResolveProofCityName(actor);
		string doctrine = state.Found ? Normalize(state.Doctrine, "大道有凭") : "大道有凭";
		string legacy = state.Found ? Normalize(state.LegacyDoctrine, ShortDao(manifest) + "宣法") : ShortDao(manifest) + "宣法";
		string scope = state.Found ? Normalize(state.AuthorityScope, "诸象") : "诸象";
		string scopeDisplay = ResolveAuthorityScopeDisplay(manifest, scope);
		string jinXing = state.Found ? Normalize(state.JinXing, "未定金性") : "未定金性";
		string positionName = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		string yearText = XjChronology.FormatYear(currentYear);

		if (string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			string formula = state.Found ? Normalize(state.RunFormula, "借" + source + "闰" + manifest)
				: "借" + source + "闰" + manifest;
			return "【道胎成就】" + yearText + "，本座" + title + "，今日" + formula + "，于" + city + "炼果成胎。"
				+ doctrine + "；由【" + Normalize(positionName, manifest + "闰位") + "】贯入胎机，证得" + jinXing + "，晋证道胎。"
				+ "自今日起，天下" + scopeDisplay + "，凡" + ShortDao(manifest) + ShortDao(source) + "一系灵源"
				+ ResolveElementCategory(manifest, source) + "，皆随予一念开阖；"
				+ "本道果、余、闰之位序，神通之显藏，权柄之授夺，尽照入胎中。"
				+ city + "留存道基，刻印" + legacy + "，定此世" + manifest + "闰法，以资后人。";
		}

		if (string.Equals(positionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			return "【道胎成就】" + yearText + "，本座" + title + "，今日于" + city + "承" + ShortDao(manifest)
				+ "之辅，奉本道正统，列入【" + Normalize(positionName, manifest + "余位") + "】而炼成道胎。"
				+ doctrine + "；余以辅正，偏裨持脉，证得" + jinXing + "，胎息已定。"
				+ "自今日起，天下" + scopeDisplay + "，凡" + ShortDao(manifest) + "一系灵源"
				+ ResolveElementCategory(manifest, manifest) + "，皆受予护持；"
				+ "本道果、余、闰之位序，神通之显藏，权柄之授夺，皆由予镇守其旁。"
				+ city + "留存道基，刻印" + legacy + "，以资后人。";
		}

		return "【道胎成就】" + yearText + "，本座" + title + "，今日于" + city + "正受" + ShortDao(manifest)
			+ "，承一道大统，登临【" + Normalize(positionName, manifest + "果位") + "】而炼果成胎。"
			+ doctrine + "；正性既定，万象归宗，证得" + jinXing + "，晋证道胎。"
			+ "自今日起，天下" + scopeDisplay + "，凡" + ShortDao(manifest) + "一系灵源"
			+ ResolveElementCategory(manifest, manifest) + "，皆归予辖；"
			+ "本道果、余、闰之位序，神通之显藏，权柄之授夺，传承之兴替，皆由予总摄，为一系主君。"
			+ city + "留存道基，刻印" + legacy + "，定此世" + manifest + "正法，以资后人。";
	}

	private static string ResolveAuthorityScopeDisplay(string daoTu, string rawScope)
	{
		return XjDaoIntentionCatalog.ResolveScopeDisplay(daoTu, rawScope);
	}

	private static string ResolveProofCityName(Actor actor)
	{
		City city = actor?.city;
		if (city?.data == null && XjSectCityData.TryFindActorZongMenCity(actor, out City zongMenCity)) city = zongMenCity;
		return city?.data == null ? "世间" : Normalize(city.data.name, "世间");
	}

	private static string ResolveElementCategory(string manifest, string source)
	{
		string combined = (manifest ?? string.Empty) + (source ?? string.Empty);
		string[] elements = { "水", "木", "火", "金", "土", "雷", "阴", "阳", "炁", "仪" };
		for (int i = 0; i < elements.Length; i++) if (combined.Contains(elements[i], StringComparison.Ordinal)) return elements[i] + "属";
		return "灵属";
	}

	private static string ShortDao(string daoTu)
	{
		string value = Normalize(daoTu, string.Empty);
		if (value.Length == 2 && "阴阳雷金木水火土炁仪".IndexOf(value[1]) >= 0) return value.Substring(0, 1);
		return value;
	}

	private static string ResolveJinDanTitle(Actor actor)
	{
		return XjLongShuSystem.IsLongShu(actor) ? "龙君" : "真君";
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

		string[] realmSuffixes = { "-郁仪仙", "-结璘仙" };
		for (int i = 0; i < realmSuffixes.Length; i++)
		{
			string realmSuffix = realmSuffixes[i];
			while (name.EndsWith(realmSuffix, StringComparison.Ordinal))
			{
				name = name.Substring(0, name.Length - realmSuffix.Length).TrimEnd();
			}
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

	private static string ResolveFuQiAnnouncementDaoTu(string daoTu, string coreName)
	{
		if (!string.IsNullOrWhiteSpace(daoTu)) return daoTu.Trim();
		if (XjFuQiSwordWorldState.IsEstablished
			&& string.Equals((coreName ?? string.Empty).Trim(), "养青冥", StringComparison.Ordinal))
		{
			return XjFuQiSwordWorldState.EstablishedDaoName;
		}
		return string.Empty;
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
