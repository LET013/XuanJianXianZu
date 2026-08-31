using System;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.Systems.History.Books;

internal static partial class XjThreeBookWriter
{
	internal static void RecordFuQiCultivationPhase(
		Actor actor,
		string phaseKey,
		string title,
		string detail,
		int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string key = string.IsNullOrWhiteSpace(phaseKey) ? "phase" : phaseKey.Trim();
		string body = actorName + "循" + daoTu + "服气养性之法，" + SafeName(detail, "开始一段新的性命修持。");
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiCultivationPhase,
			"personal|fuqi-phase|" + actorId + "|" + key,
			XjWorldHistoryCategory.Cultivation, "性命修持", SafeName(title, "服气修持"), body,
			2, false, familyId, familyName, sectId, sectName);
	}

	internal static void RecordFuQiCoreHuangGuan(Actor actor, string coreName, int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string core = SafeName(coreName, daoTu + "道气");
		string body = actorName + "感应" + daoTu + "之气，长久温养" + core
			+ "，使道气与自身性命初步相合，终于求得本命神妙，自此脱离凡俗，位列黄冠。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiHuangGuan,
			"personal|fuqi-core-huangguan|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "神妙初成", body,
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "huangguan", "黄冠", "神妙初成", "以" + core + "求得本命神妙，位列黄冠。",
			4, false, false, XjHistoryResult.Success);
	}

	internal static void RecordFuQiNatalTalismanHuangGuan(Actor actor, string talismanName, int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string talisman = SafeName(talismanName, daoTu + "本命符箓");
		string body = actorName + "感应" + daoTu + "之气，以符理养性、以性命养符，历年凝炼成" + talisman
			+ "。此符不落纸墨、不入囊中，而与其性命相连；本命符箓初成，遂位列黄冠。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiHuangGuan,
			"personal|fuqi-natal-talisman-huangguan|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "本命符箓初成", body,
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "huangguan", "黄冠", "本命符箓初成", "凝成本命符箓" + talisman + "，位列黄冠。",
			4, false, false, XjHistoryResult.Success);
	}

	internal static void RecordFuQiHengZhuHuangGuan(Actor actor, int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string body = actorName + "感应衡祝之气，以巫祝法守火、以性命养祭，历年温养本命祭火。"
			+ "祭火初定而不落凡炉，不借火德功法，遂求得自身神妙，位列黄冠。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiHuangGuan,
			"personal|fuqi-hengzhu-huangguan|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "本命祭火初成", body,
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "huangguan", "黄冠", "本命祭火初成", "温养本命祭火，求得神妙，位列黄冠。",
			4, false, false, XjHistoryResult.Success);
	}

	internal static void RecordFuQiQuanDanHuangGuan(Actor actor, int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string body = actorName + "感应全丹之气，以性命为炉，长期温养本命丹性。"
			+ "此丹性不取丹方、不耗药材，也并非炼丹百艺；丹性初成，遂求得本命神妙，位列黄冠。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiHuangGuan,
			"personal|fuqi-quandan-huangguan|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "本命丹性初成", body,
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "huangguan", "黄冠", "本命丹性初成", "温养本命丹性，求得神妙，位列黄冠。",
			4, false, false, XjHistoryResult.Success);
	}

	internal static void RecordFuQiSpecialCoreHuangGuan(
		Actor actor,
		string eventKey,
		string title,
		string detail,
		int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string key = string.IsNullOrWhiteSpace(eventKey) ? "special" : eventKey.Trim();
		string eventTitle = SafeName(title, "本命核心初成");
		string body = actorName + SafeName(detail, "温养本命核心，求得自身神妙，位列黄冠。");
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiHuangGuan,
			"personal|fuqi-" + key + "-huangguan|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", eventTitle, body,
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "huangguan", "黄冠", eventTitle, SafeName(detail, "温养本命核心，求得神妙，位列黄冠。"),
			4, false, false, XjHistoryResult.Success);
	}

	internal static void RecordFuQiQingXuanHuangGuan(Actor actor, int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string body = actorName + "感应青宣之气，长久温养神妙〖玄羊子〗，使其与自身性命初步相合。"
			+ "玄羊子初成，青宣服气修法由此显世，其亦自此位列黄冠。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiHuangGuan,
			"personal|fuqi-qingxuan-huangguan|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "玄羊子初成", body,
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "huangguan", "黄冠", "玄羊子初成", "温养神妙〖玄羊子〗，位列黄冠。",
			4, false, false, XjHistoryResult.Success);
	}

	internal static void RecordFuQiCoreZhenRen(Actor actor, string coreName, int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string core = SafeName(coreName, "本命神妙");
		string body = actorName + "继续温养" + core + "，使神妙由外归身，性命与道气相合，终于求到自身之真，晋为真人。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiZhenRen,
			"personal|fuqi-core-zhenren|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "神妙归身", body,
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "zhenren", "真人", "神妙归身", "使" + core + "由外归身，求到自身之真，晋为真人。",
			4, true, true, XjHistoryResult.Success);
	}

	internal static void RecordFuQiCorePerfected(Actor actor, string coreName, int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string core = SafeName(coreName, "本命神妙");
		string body = actorName + "将" + core + "温养至圆满，性命与" + daoTu
			+ "道气浑然如一，金性由此自然化生，已可求证真君羽士。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiShenMiaoPerfected,
			"personal|fuqi-core-perfect|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "神妙圆满", body,
			3, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "perfected", "性命修持", "神妙圆满", "将" + core + "温养圆满，已可化生金性、求证真君。",
			3, false, false, XjHistoryResult.Success);
	}

	internal static void RecordFuQiCoreJinDanFailure(
		Actor actor,
		string coreName,
		int year,
		int injuryYears,
		int nurtureYears,
		int failureCount)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string core = SafeName(coreName, "圆满神妙");
		string body = actorName + "以" + core + "化出金性，尝试求证真君羽士，然天地未曾认可其果。"
			+ "金性散去，神妙反震性命，遂闭关养伤" + Math.Max(5, injuryYears)
			+ "年；伤愈后尚需温养金性" + Math.Max(20, nurtureYears) + "年，方可再行求证。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiJinDanFailure,
			"personal|fuqi-core-jindan-failure|" + actorId + "|" + Math.Max(1, failureCount),
			XjWorldHistoryCategory.Cultivation, "求证真君羽士", "天地未认", body,
			3, false, familyId, familyName, sectId, sectName, result: XjHistoryResult.Failure);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "jindan-failure-" + Math.Max(1, failureCount), "求证受挫", "天地未认",
			"求证真君未获天地认可，闭关养伤" + Math.Max(5, injuryYears) + "年。",
			3, false, false, XjHistoryResult.Failure);
	}

	internal static void RecordFuQiCoreZhenJunYuShi(Actor actor, string coreName, int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out string daoTu,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string core = SafeName(coreName, "圆满神妙");
		string body = actorName + "以" + core + "求证真君羽士，性命、道气与金性合而为一。"
			+ "天地认可其果，其由真人登为真君羽士。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiZhenJunYuShi,
			"personal|fuqi-core-zhenjun-yushi|" + actorId,
			XjWorldHistoryCategory.Cultivation, "天地认果", daoTu + "真君", body,
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "zhenjun-yushi", "真君羽士", daoTu + "真君", "以" + core + "求证，得天地认果，登为真君羽士。",
			5, true, true, XjHistoryResult.Success);
	}


	internal static void RecordFuQiToZiFuConversion(
		Actor actor,
		string sourceDaoTu,
		string targetDaoTu,
		int progressBasisPoints,
		string[] shenTongIds,
		int year)
	{
		if (!TryBuildFuQiContext(actor, out long actorId, out string actorName, out _,
			out long familyId, out string familyName, out long sectId, out string sectName)) return;
		string source = SafeName(sourceDaoTu, "原服气道途");
		string target = SafeName(targetDaoTu, "紫府道途");
		int progress = Math.Max(0, Math.Min(10000, progressBasisPoints));
		int count = shenTongIds?.Length ?? 0;
		string names = count > 0 ? string.Join("、", shenTongIds) : "未录神通";
		string coreStage = progress < 2500 ? "初成" : progress < 5500 ? "渐熟" : progress < 8500 ? "深厚" : "近乎圆满";
		string body = actorName + "自" + source + "服气养性一路转修紫府金丹法，永久舍去原有服气求证之路。"
			+ "其本命核心已温养至" + coreStage + "：转入" + target
			+ "后，核心道理化为" + count + "门紫府神通（" + names + "），并各自衍生一部五品功法。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiToZiFu,
			"personal|fuqi-to-zifu|" + actorId,
			XjWorldHistoryCategory.Cultivation, "改易修法", "服气真人转修紫府", body,
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "to-zifu", "改易修法", "服气转紫府", "舍去原有服气求证之路，转修" + target + "紫府金丹法。",
			4, false, true, XjHistoryResult.Success);
	}

	private static void RecordFuQiAffiliationMilestone(
		long actorId,
		string actorName,
		long familyId,
		string familyName,
		long sectId,
		string sectName,
		int year,
		string milestoneKey,
		string tag,
		string title,
		string detail,
		int importance,
		bool highRealm,
		bool includeSect,
		string result)
	{
		string key = string.IsNullOrWhiteSpace(milestoneKey) ? "milestone" : milestoneKey.Trim();
		string safeDetail = SafeName(detail, "修持有所进境。");
		if (familyId > 0L)
		{
			EnsureFamilyFounded(familyId, familyName, actorId, actorName, year);
			string familyTitle = title;
			string familyBody = familyName + "族人" + actorName + safeDetail;
			bool isZhenRenMilestone = highRealm && string.Equals(key, "zhenren", StringComparison.Ordinal);
			bool isZhenJunMilestone = highRealm && (string.Equals(key, "zhenjun-yushi", StringComparison.Ordinal)
				|| key.StartsWith("longgeng-succession-", StringComparison.Ordinal));
			if (isZhenRenMilestone || isZhenJunMilestone)
			{
				string targetRealm = isZhenRenMilestone
					? XjRealmIds.FuQiZhenRen
					: XjRealmIds.ZhenJunYuShi;
				string realmLabel = isZhenRenMilestone ? "真人" : "真君";
				XjFamilyRealmAchievement achievement = XjFamilyRealmAchievementNarrative.Resolve(
					familyId, actorId, actorName, targetRealm);
				familyTitle = XjFamilyRealmAchievementNarrative.BuildShortTitle(in achievement, realmLabel);
				familyBody = familyName + "族人" + actorName + safeDetail.TrimEnd('。')
					+ XjFamilyRealmAchievementNarrative.BuildEnding(in achievement, realmLabel);
			}
			RecordFamily(
				familyId,
				familyName,
				year,
				highRealm ? XjThreeBookEventTypes.FamilyHighRealmEmerged : XjThreeBookEventTypes.FamilyMemberAchievement,
				"family|fuqi|" + familyId + "|" + actorId + "|" + key,
				tag,
				familyTitle,
				familyBody,
				importance,
				highRealm || importance >= 5,
				actorId,
				actorName,
				sectId,
				sectName,
				result);
		}
		if (includeSect && sectId > 0L)
		{
			RecordSect(
				sectId,
				sectName,
				year,
				highRealm ? XjThreeBookEventTypes.SectHighRealmEmerged : XjThreeBookEventTypes.SectInheritanceAdded,
				"sect|fuqi|" + sectId + "|" + actorId + "|" + key,
				tag,
				title,
				sectName + "门中" + actorName + safeDetail,
				importance,
				highRealm || importance >= 5,
				actorId,
				actorName,
				familyId,
				familyName,
				result: result);
		}
	}

	private static bool TryBuildFuQiContext(
		Actor actor,
		out long actorId,
		out string actorName,
		out string daoTu,
		out long familyId,
		out string familyName,
		out long sectId,
		out string sectName)
	{
		actorId = 0L;
		actorName = string.Empty;
		daoTu = "无名道途";
		familyId = 0L;
		familyName = string.Empty;
		sectId = 0L;
		sectName = string.Empty;
		if (actor?.data == null) return false;
		actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		actorName = SafeActorName(actor);
		XjFuQiSwordWorldState.EnsureEstablishedDaoIdentity(actor, false);
		if (XjActorAccessor.TryGetString(actor, XuanJianVNext.Data.Rules.XjActorDataKeys.DaoTu, out string stored)
			&& !string.IsNullOrWhiteSpace(stored)) daoTu = stored.Trim();
		ResolveActorAffiliations(actor, out familyId, out familyName, out sectId, out sectName);
		return true;
	}
}
