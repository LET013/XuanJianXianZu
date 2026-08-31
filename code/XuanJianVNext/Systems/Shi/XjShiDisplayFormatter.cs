using System;
using System.Globalization;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Shi;

internal static class XjShiDisplayFormatter
{
	private const string SectionColor = "#B9EEE4";
	private const string LabelColor = "#D8CDAA";
	private const string ValueColor = "#E6EDF2";
	private const string WarnColor = "#FFD37A";

	internal static string Format(Actor actor)
	{
		StringBuilder builder = new StringBuilder(640);
		if (!XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot))
		{
			AppendLine(builder, "状态", "释修数据尚未建立");
			return builder.ToString().TrimEnd();
		}

		AppendSection(builder, "释门");
		AppendLine(builder, "修法", "释修");
		AppendLine(builder, "释统", XjShiCatalog.GetTraditionDisplay(snapshot.Tradition));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		AppendLine(builder, "法脉", XjShiCatalog.GetLineageDisplay(lineageId));
		AppendLine(builder, "法理", XjShiCatalog.GetLineageIdeaDisplay(lineageId));
		string realmDisplay = XjShiCatalog.GetRealmDisplay(snapshot.Realm);
		if (string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			realmDisplay += " · " + XjShiCatalog.GetSeatDisplay(snapshot.SeatId);
		}
		AppendLine(builder, "境界", realmDisplay);
		if (XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaName, out string dharmaName);
			if (!string.IsNullOrWhiteSpace(dharmaName)) AppendLine(builder, "法号", dharmaName);
		}

		AppendSection(builder, "命数修持");
		XjMingShuState.Normalize(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float ordinaryMingShu);
		AppendLine(builder, "普通命数",
			Math.Floor(Math.Max(0f, ordinaryMingShu)).ToString(CultureInfo.InvariantCulture));
		float ownShiMingShu = XjShiMingShuSystem.GetValue(actor);
		float effectiveShiMingShu = XjShiMingShuSystem.GetEffectiveValue(actor, Math.Max(1, XjYearTracker.CurrentYear));
		string mingShuDisplay = Math.Floor(ownShiMingShu).ToString(CultureInfo.InvariantCulture) + " · 上限1000";
		if (effectiveShiMingShu > ownShiMingShu + 0.5f)
			mingShuDisplay += "（借位后等效" + Math.Floor(effectiveShiMingShu).ToString(CultureInfo.InvariantCulture) + "）";
		AppendLine(builder, "释修命数", mingShuDisplay);
		AppendLine(builder, "修持", ResolvePracticeDisplay(snapshot));
		int practiceYear = Math.Max(1, XjYearTracker.CurrentYear);
		AppendLine(builder, "四无方向", XjShiPracticeDirectionSystem.GetDisplay(actor, practiceYear));
		AppendLine(builder, "金地承载", XjShiPracticeDirectionSystem.GetSupportDisplay(actor, practiceYear));
		if (string.Equals(snapshot.EntrySource, XjShiSourceIds.Conversion, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiConversionYear, out int conversionYear);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiConversionSourceTier, out int sourceTier);
			string source = sourceTier == XjRealmSuppression.TierJinDan ? "金丹／真君羽士"
				: sourceTier == XjRealmSuppression.TierZiFu ? "紫府／真人"
				: sourceTier == XjRealmSuppression.TierZhuJi ? "筑基／黄冠" : "旧修法";
			AppendLine(builder, "投释", source + (conversionYear > 0 ? "，于" + XjChronology.FormatYear(conversionYear) + "转修" : "转修"));
		}
		if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiAncientDuhuaCount, out int ancientDuhuaCount);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiAncientDuhuaLastYear, out int ancientDuhuaLastYear);
			string ancientDuhuaDisplay = "已清静点化"
				+ Math.Max(0, ancientDuhuaCount).ToString(CultureInfo.InvariantCulture) + "人";
			if (ancientDuhuaLastYear > 0)
				ancientDuhuaDisplay += "（最近于" + XjChronology.FormatYear(ancientDuhuaLastYear) + "）";
			AppendLine(builder, "古释点化", ancientDuhuaDisplay);
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiVowId, out string ancientVowId)
				&& XjAncientShiVowIds.IsKnown(ancientVowId))
			{
				AppendLine(builder, "宏愿", XjAncientShiVowCatalog.GetShortDisplay(ancientVowId));
				int vowProgress = XjAncientShiVowSystem.GetProgress(actor);
				AppendLine(builder, "愿行", vowProgress >= 100 ? "愿行已成" : vowProgress >= 60 ? "愿行深厚" : vowProgress >= 30 ? "愿行渐著" : "初践其愿");
			}
			if (XjAncientShiTempleSystem.TryGetTempleForActor(actor, out XjAncientShiTempleRecord temple))
			{
				string templePlace = string.IsNullOrWhiteSpace(temple.CityName) ? temple.Name : temple.Name + " · " + temple.CityName;
				AppendLine(builder, "古寺", templePlace);
			}
		}
		else if (XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiConvertedCount, out int convertedCount);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiSentientConsumptionCount, out int consumptionCount);
			AppendLine(builder, "今释度化", "已度化" + Math.Max(0, convertedCount).ToString(CultureInfo.InvariantCulture) + "人");
			AppendLine(builder, "七相摄生", Math.Max(0, consumptionCount).ToString(CultureInfo.InvariantCulture) + "次");
		}

		if (string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			AppendLianMin(builder, actor, snapshot);
		}
		else if (XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			AppendHighRealm(builder, actor, snapshot);
		}

		if (string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
			&& string.Equals(snapshot.RebirthState, XjShiRebirthStateIds.Recovering, StringComparison.Ordinal))
		{
			AppendRebirthRecovery(builder, snapshot);
		}
		return builder.ToString().TrimEnd();
	}

	private static void AppendLianMin(StringBuilder builder, Actor actor, in XjShiSnapshot snapshot)
	{
		AppendSection(builder, "怜愍位次");
		AppendLine(builder, "座位", XjShiCatalog.GetSeatDisplay(snapshot.SeatId));
		AppendLine(builder, "座主", ResolvePatronDisplay(snapshot.PatronActorId));
		AppendLine(builder, "借法", snapshot.BorrowedPower > 0
			? "第" + snapshot.BorrowedPower.ToString(CultureInfo.InvariantCulture) + "档（位次越高，可借法力越多）"
			: "当前无可借法力");
		AppendLine(builder, "契合", snapshot.Alignment.ToString(CultureInfo.InvariantCulture) + " · 上限100");
		AppendLine(builder, "位况", XjShiCatalog.GetPositionStatusDisplay(snapshot.PositionStatus));
		AppendDomainLines(builder, actor, snapshot, includeAuthority: false);
	}

	private static void AppendHighRealm(StringBuilder builder, Actor actor, in XjShiSnapshot snapshot)
	{
		AppendSection(builder, string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal) ? "摩诃" : "高位释修");
		AppendDomainLines(builder, actor, snapshot, includeAuthority: true);
		if (XjShiWorldRegistry.TryGetSeatUsage(actor, out int usedSeats, out int seatCapacity))
		{
			AppendLine(builder, "座下怜愍", "已占" + usedSeats.ToString(CultureInfo.InvariantCulture)
				+ " · 容" + seatCapacity.ToString(CultureInfo.InvariantCulture));
		}
		if (string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormCandidateState, out string candidateState);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDharmaFormCandidateSinceYear, out int candidateSinceYear);
			string candidateDisplay = XjShiCatalog.GetDharmaFormCandidateDisplay(candidateState);
			if (candidateSinceYear > 0 && !string.Equals(candidateState, XjShiDharmaFormCandidateIds.Owner, StringComparison.Ordinal))
				candidateDisplay += "（自" + XjChronology.FormatYear(candidateSinceYear) + "）";
			AppendLine(builder, "法相候位", candidateDisplay);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDharmaFormLastAttemptYear, out int dharmaAttemptYear);
			if (dharmaAttemptYear > 0) AppendLine(builder, "上次争位", XjChronology.FormatYear(dharmaAttemptYear));
			AppendDharmaFormRequirements(builder, actor, snapshot);
		}

		if (string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			int currentLife = Math.Clamp(Math.Max(snapshot.CurrentLife, snapshot.CompletedLives + 1), 1, 9);
			int completedLives = Math.Clamp(Math.Max(snapshot.CompletedLives, currentLife - 1), 0, 8);
			AppendLine(builder, "轮回", completedLives <= 0
				? "初世"
				: "前世已历" + completedLives.ToString(CultureInfo.InvariantCulture)
					+ "世，今为第" + currentLife.ToString(CultureInfo.InvariantCulture) + "世");
			AppendLine(builder, "摩诃世数", XjShiCatalog.GetMoHeStageDisplay(currentLife));
			AppendLine(builder, "轮回锚", string.IsNullOrWhiteSpace(snapshot.RebirthAnchorId) ? "未建立" : "已系于承载地");
			if (snapshot.TrueSpiritLocked > 0)
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiTrueSpiritLockUntilYear, out int lockUntil);
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTrueSpiritLockReason, out string lockReason);
				AppendWarn(builder, "真灵受锁" + (lockUntil > 0 ? "至" + XjChronology.FormatYear(lockUntil) : "（无期限）")
					+ (string.IsNullOrWhiteSpace(lockReason) ? string.Empty : "：" + lockReason));
			}
		}

		if (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			|| string.Equals(snapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
		{
			AppendSection(builder, "法相");
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness, out string worldHonoredReadiness);
			if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
			{
				AppendLine(builder, "真身所在", XjZhantanlinSystem.IsPlaced ? "旃檀林" : "旃檀林尚未开辟");
			}
			AppendLine(builder, "层次", string.Equals(snapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)
				? "世尊圆满"
				: XjShiCatalog.GetDharmaFormStageDisplay(snapshot.DharmaFormStage));
			if (!string.Equals(snapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
				AppendLine(builder, "世尊前置", XjShiCatalog.GetWorldHonoredReadinessDisplay(worldHonoredReadiness));
			AppendLine(builder, "本愿", ResolveVowDisplay(snapshot.VowId));
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiWorldHonoredFailureCount, out int worldFailures);
			if (worldFailures > 0) AppendLine(builder, "证道受挫", worldFailures.ToString(CultureInfo.InvariantCulture) + "次");
			if (snapshot.ResponseBodyRisk > 0)
			{
				AppendLine(builder, "应身反夺", "风险" + snapshot.ResponseBodyRisk.ToString(CultureInfo.InvariantCulture) + " · 上限10000");
			}
		}
		AppendFamilyOrigin(builder, actor);
	}

	private static string ResolveVowDisplay(string vowId)
	{
		string value = (vowId ?? string.Empty).Trim();
		if (value.Length == 0) return "依本性与法脉自立";
		if (XjAncientShiVowIds.IsKnown(value)) return XjAncientShiVowCatalog.GetDisplay(value);
		if (string.Equals(value, XjShiDharmaFormStageIds.OriginalVow, StringComparison.Ordinal))
			return "守本性、立大愿";
		return value.Contains(":", StringComparison.Ordinal) || value.Contains("_", StringComparison.Ordinal)
			? "依本性与法脉自立" : value;
	}

	private static void AppendDharmaFormRequirements(StringBuilder builder, Actor actor, in XjShiSnapshot snapshot)
	{
		int year = Math.Max(1, XjYearTracker.CurrentYear);
		bool modern = string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
		float practiceRequired = modern
			? XjShiCatalog.DharmaFormPracticeThreshold
			: XjShiCatalog.AncientDharmaFormPracticeThreshold;
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, year);
		int minimumLives = modern ? XjShiCatalog.DharmaFormMinimumLives : 0;
		bool livesReady = !modern || snapshot.CompletedLives >= minimumLives;
		string lives = modern
			? (livesReady ? "九世已足" : "尚需第九世（已历" + Math.Max(0, snapshot.CompletedLives).ToString(CultureInfo.InvariantCulture) + "世）")
			: "古释不计轮回世数";
		string practice = Math.Floor(snapshot.Practice).ToString(CultureInfo.InvariantCulture)
			+ "/" + Math.Floor(practiceRequired).ToString(CultureInfo.InvariantCulture)
			+ (snapshot.Practice >= practiceRequired ? " 已足" : " 未足");
		string fate = Math.Floor(mingShu).ToString(CultureInfo.InvariantCulture)
			+ "/" + XjShiCatalog.DharmaFormMinimumMingShu.ToString(CultureInfo.InvariantCulture)
			+ (mingShu >= XjShiCatalog.DharmaFormMinimumMingShu ? " 已足" : " 未足");
		string domain = "未得法相承载金地";
		if (XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord foundation)
			&& foundation != null)
		{
			domain = foundation.Growth.ToString(CultureInfo.InvariantCulture)
				+ "/" + XjShiCatalog.DharmaFormMinimumDomainGrowth.ToString(CultureInfo.InvariantCulture)
				+ (foundation.Growth >= XjShiCatalog.DharmaFormMinimumDomainGrowth ? " 已足" : " 未足");
		}
		AppendLine(builder, "法相门槛", lives);
		AppendLine(builder, "法相修持", practice);
		AppendLine(builder, "法相命数", fate);
		AppendLine(builder, "金地成熟", domain);
		if (modern)
		{
			AppendLine(builder, "法相承载", XjZhantanlinSystem.IsPlaced ? "旃檀林已显世" : "旃檀林未显世");
		}
	}

	private static void AppendDomainLines(StringBuilder builder, Actor actor, in XjShiSnapshot snapshot, bool includeAuthority)
	{
		int year = Math.Max(1, XjYearTracker.CurrentYear);
		if (!XjShiDomainState.TryGetForActor(actor, year, out XjShiDomainRecord domain))
		{
			AppendLine(builder, "承载地", string.IsNullOrWhiteSpace(snapshot.DomainId)
				? "尚未建立"
				: "承载地暂未明晰（" + XjShiCatalog.GetJinDiStatusDisplay(snapshot.JinDiStatus) + "）");
			return;
		}

		bool ancientSelfDomain = string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			&& (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal));
		AppendLine(builder, "承载地", ancientSelfDomain
			? XjShiDomainCatalog.GetDomainDisplayName(domain) + " · 藏于自身"
			: XjShiDomainCatalog.GetDomainDisplayName(domain)
				+ " · " + XjShiDomainCatalog.GetVisibilityDisplay(domain.Visibility));
		if (!includeAuthority) return;
		bool concealedJinDi = (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
			|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
			&& string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal);
		if (concealedJinDi)
		{
			AppendWarn(builder, "金地隐世，外界不可知其内情。");
			return;
		}

		string authority;
		bool highRealm = string.Equals(snapshot.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			|| string.Equals(snapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal);
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		bool ownsNorthJinDi = XjShiDomainState.TryGetOwnedNorthJinDi(actorId, out XjShiDomainRecord ownedNorthJinDi);
		if (highRealm)
		{
			if (ownsNorthJinDi)
				authority = "庙主";
			else if (ancientSelfDomain)
				authority = "古释自证金地主人";
			else authority = "法相未掌金地";
		}
		else if (ownsNorthJinDi)
		{
			authority = "庙主";
		}
		else if (ancientSelfDomain)
		{
			authority = "自证金地主人";
		}
		else if (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal))
		{
			authority = "金地主人";
		}
		else if (string.Equals(domain.DomainType, XjShiDomainTypeIds.Zhantanlin, StringComparison.Ordinal))
		{
			authority = "旃檀林摩诃";
		}
		else
		{
			authority = "应土摩诃";
		}
		AppendLine(builder, "承载身份", authority);
		if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.LianMin)
			&& XjShiDomainState.TryGetMoHePositionUsage(
				XjShiDomainCatalog.ZhantanlinDomainId, out int occupiedMoHe, out int reservedMoHe, out int moHeCapacity))
		{
			string usage = "实占 " + occupiedMoHe.ToString(CultureInfo.InvariantCulture)
				+ " + 轮回预留 " + reservedMoHe.ToString(CultureInfo.InvariantCulture)
				+ " / " + moHeCapacity.ToString(CultureInfo.InvariantCulture);
			AppendLine(builder, "摩诃位序", usage);
			if (occupiedMoHe + reservedMoHe >= moHeCapacity)
				AppendWarn(builder, "旃檀林摩诃位已满；法相、世尊不占摩诃位，只有仍为摩诃的轮回真灵保留原位。");
		}
		if (ownsNorthJinDi)
		{
			AppendLine(builder, "应身来源", XjShiHeavenCatalog.GetHeavenDisplayName(ownedNorthJinDi.SourceHeavenIndex)
				+ " · " + XjShiHeavenCatalog.GetHeavenMeaning(ownedNorthJinDi.SourceHeavenIndex));
			AppendLine(builder, "金地所在", XjShiDomainCatalog.GetDomainDisplayName(ownedNorthJinDi)
				+ (string.Equals(ownedNorthJinDi.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)
					? "（旃檀林内）" : "（释土外）"));
			XjShiDomainState.RefreshHeavenProjection(actor);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiOwnedHeavenFragments, out int ownedFragments);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiRequiredHeavenFragments, out int requiredFragments);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiReformedHeaven, out int reformedHeaven);
			AppendLine(builder, "聚天进度", "已得" + Math.Max(0, ownedFragments).ToString(CultureInfo.InvariantCulture)
				+ " · 需" + Math.Max(0, requiredFragments).ToString(CultureInfo.InvariantCulture)
				+ (reformedHeaven > 0 ? "（已重组三十二天）" : string.Empty));
		}
		XjShiDomainRecord growthDomain = ownsNorthJinDi ? ownedNorthJinDi : domain;
		string growthDisplay = Math.Max(0, growthDomain.Growth).ToString(CultureInfo.InvariantCulture);
		if (TryResolveNextDomainGrowthMilestone(snapshot, out int growthTarget, out string growthTargetName))
		{
			growthDisplay += " / " + growthTarget.ToString(CultureInfo.InvariantCulture)
				+ "（" + growthTargetName + "）";
		}
		AppendLine(builder, "承载增长", growthDisplay);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDomainShockUntilYear, out int shockUntil);
		if (shockUntil >= year)
		{
			string shockEffect = string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
				? "修持与轮回复归效率下降。"
				: "修持效率下降。";
			AppendWarn(builder, "位次震荡持续至" + XjChronology.FormatYear(shockUntil) + "，" + shockEffect);
		}
		// 承载身份已经表达角色与金地／释土的关系，不再重复显示“主人＝自己”，
		// 也不暴露旧版为迁移应土临时设置的主持字段。
		string migration = XjShiDomainCatalog.GetMigrationDisplay(domain.LegacyMigrationState);
		if (!string.IsNullOrWhiteSpace(migration)) AppendWarn(builder, migration);
	}


	private static bool TryResolveNextDomainGrowthMilestone(in XjShiSnapshot snapshot,
		out int threshold, out string display)
	{
		threshold = 0;
		display = string.Empty;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			threshold = XjShiCatalog.DharmaFormMinimumDomainGrowth;
			display = "法相";
			return true;
		}
		if (!string.Equals(snapshot.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return false;
		if (string.Equals(snapshot.DharmaFormStage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal))
		{
			threshold = XjShiCatalog.WorldHonoredDomainGrowthThreshold;
			display = "世尊";
		}
		else if (string.Equals(snapshot.DharmaFormStage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal))
		{
			threshold = XjShiCatalog.WorldHonoredPathDomainGrowthThreshold;
			display = XjShiCatalog.GetDharmaFormStageDisplay(XjShiDharmaFormStageIds.WorldHonoredPath);
		}
		else if (string.Equals(snapshot.DharmaFormStage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal))
		{
			threshold = XjShiCatalog.SelfReturnedDomainGrowthThreshold;
			display = XjShiCatalog.GetDharmaFormStageDisplay(XjShiDharmaFormStageIds.SelfReturned);
		}
		else
		{
			threshold = XjShiCatalog.ResponseBodyDomainGrowthThreshold;
			display = XjShiCatalog.GetDharmaFormStageDisplay(XjShiDharmaFormStageIds.ResponseBody);
		}
		return true;
	}

	private static void AppendFamilyOrigin(StringBuilder builder, Actor actor)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity)
			|| identity == null
			|| !identity.Found
			|| identity.FamilyStableIdValue <= 0L)
		{
			return;
		}

		long familyId = identity.FamilyStableIdValue;
		string familyName = XjFamilyDisplayNameResolver.Resolve(familyId);
		if (string.IsNullOrWhiteSpace(familyName)) familyName = "未名氏";
		string branch = XjFamilyMemberLedger.IsMigrationBranchFamily(familyId) ? "迁城分家" : "本家";
		string familyDisplay = familyName + "-" + branch + "-第"
			+ Math.Max(1, identity.Generation).ToString(CultureInfo.InvariantCulture) + "代";

		AppendSection(builder, "家族律典");
		AppendLine(builder, "家族", familyDisplay);
		if (XjFamilyMemberLedger.IsMigrationBranchFamily(familyId)
			&& XjFamilyMemberLedger.TryGetBranchSourceFamilyId(familyId, out long sourceFamilyId)
			&& sourceFamilyId > 0L)
		{
			string sourceFamily = XjFamilyDisplayNameResolver.Resolve(sourceFamilyId);
			if (!string.IsNullOrWhiteSpace(sourceFamily)) AppendLine(builder, "来源主家", sourceFamily);
		}
		if (XjFamilyMemberLedger.TryGetFamilyOriginCityId(familyId, out long originCityId))
		{
			AppendLine(builder, "发源城", ResolveCityDisplayName(originCityId));
		}
	}

	private static string ResolveCityDisplayName(long cityId)
	{
		if (cityId <= 0L || !XjWorldLookupIndex.TryResolveCity(cityId, out City city) || city?.data == null)
		{
			return cityId > 0L ? "城镇" + cityId.ToString(CultureInfo.InvariantCulture) : "未知城镇";
		}
		string name = ((BaseSystemData)city.data).name;
		return string.IsNullOrWhiteSpace(name)
			? "城镇" + cityId.ToString(CultureInfo.InvariantCulture)
			: name.Trim();
	}

	private static string ResolveActorDisplay(long actorId, string fallback)
	{
		if (actorId > 0L && XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& actor?.data != null)
		{
			return actor.getName();
		}
		return fallback;
	}

	private static void AppendRebirthRecovery(StringBuilder builder, in XjShiSnapshot snapshot)
	{
		AppendSection(builder, "轮回恢复");
		string target = XjShiCatalog.GetRealmDisplay(snapshot.RebirthTargetRealm);
		if (string.Equals(snapshot.RebirthTargetRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
			&& XjShiCatalog.IsKnownSeat(snapshot.RebirthTargetSeat))
		{
			target += " · " + XjShiCatalog.GetSeatDisplay(snapshot.RebirthTargetSeat);
		}
		float threshold = ResolveRecoveryThreshold(snapshot.RebirthTargetRealm);
		AppendLine(builder, "前世目标", target);
		AppendLine(builder, "复归进度", "当前" + Math.Floor(snapshot.RebirthRecovery).ToString(CultureInfo.InvariantCulture)
			+ " · 目标" + threshold.ToString("0", CultureInfo.InvariantCulture));
		if (XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			AppendWarn(builder, "本世已复归摩诃，但尚未恢复前世法相层次；若再度身死，仍须重新入轮回。");
		}
		else
		{
			AppendWarn(builder, "当前仍是重修之身，尚未复归摩诃以前位次，可被正常杀死并中断本次复归。");
		}
	}

	private static string ResolvePracticeDisplay(in XjShiSnapshot snapshot)
	{
		float next = 0f;
		string nextName = string.Empty;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.Monk, StringComparison.Ordinal))
		{
			next = XjShiCatalog.DharmaMasterPracticeThreshold;
			nextName = "法师";
		}
		else if (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)
			&& string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			next = XjShiCatalog.LianMinPracticeThreshold;
			nextName = "怜愍·萨陲座";
		}
		else if (string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
			&& string.Equals(snapshot.SeatId, XjShiSeatIds.SaTuo, StringComparison.Ordinal))
		{
			next = XjShiCatalog.FaHuiPracticeThreshold;
			nextName = "发慧座";
		}
		else if (string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
			&& string.Equals(snapshot.SeatId, XjShiSeatIds.FaHui, StringComparison.Ordinal))
		{
			next = XjShiCatalog.JinLianPracticeThreshold;
			nextName = "金莲座";
		}
		string current = Math.Floor(snapshot.Practice).ToString(CultureInfo.InvariantCulture);
		if (next <= 0f) return current;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)
			&& string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& snapshot.Practice >= XjShiCatalog.LianMinPracticeThreshold)
		{
			return "当前" + current + " · 门槛" + next.ToString("0", CultureInfo.InvariantCulture)
				+ "（常规晋怜愍需摩诃空位；命数与修持足够时可极低概率越阶证摩诃）";
		}
		return "当前" + current + " · 门槛" + next.ToString("0", CultureInfo.InvariantCulture) + "（下一位次：" + nextName + "）";
	}

	private static float ResolveRecoveryThreshold(string realm)
	{
		if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)) return XjShiCatalog.RebirthLianMinRecoveryThreshold;
		if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return XjShiCatalog.RebirthDharmaFormRecoveryThreshold;
		return XjShiCatalog.RebirthMoHeRecoveryThreshold;
	}


	private static string ResolvePatronDisplay(string rawId)
	{
		if (XjShiWorldRegistry.TryResolveLiveActor(rawId, out Actor patron))
		{
			return patron.getName() + "摩诃";
		}
		return string.IsNullOrWhiteSpace(rawId) ? "未得摩诃座主" : "故主已不在世";
	}




	private static void AppendSection(StringBuilder builder, string title)
	{
		if (builder.Length > 0) builder.AppendLine();
		builder.Append("<color=").Append(SectionColor).Append("><b>")
			.Append(Escape(title)).AppendLine("</b></color>");
	}

	private static void AppendLine(StringBuilder builder, string label, string value)
	{
		builder.Append("<color=").Append(LabelColor).Append('>').Append(Escape(label)).Append("：</color>")
			.Append("<color=").Append(ValueColor).Append('>').Append(Escape(value)).AppendLine("</color>");
	}

	private static void AppendWarn(StringBuilder builder, string value)
	{
		builder.Append("<color=").Append(WarnColor).Append(">※ ")
			.Append(Escape(value)).AppendLine("</color>");
	}

	private static string Escape(string value)
	{
		return (value ?? string.Empty).Replace("<", "＜").Replace(">", "＞");
	}
}
