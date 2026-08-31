using System;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.History.Books;

internal static partial class XjThreeBookWriter
{
	internal static void RecordFuQiIntentStudy(Actor actor, XjSwordIntentArchiveRecord intent, int studiedCount, int year)
	{
		if (actor?.data == null || intent == null || studiedCount <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string creator = SafeName(intent.CreatorName, "前人");
		string intentName = SafeName(intent.IntentName, "无名剑意");
		bool creatorAlive = XjSwordIntentRegistry.IsCreatorAlive(intent);
		string body = creatorAlive
			? actorName + "远行拜访" + creator + "，亲观其施展《" + intentName + "》，又闭关反复揣摩所见锋意，终于参得其中一分异理。至此已观剑意" + studiedCount + "道。"
			: actorName + "寻至" + creator + "昔年修行之地，于旧迹剑痕间揣摩《" + intentName + "》遗意，终于参得其中一分异理。至此已观剑意" + studiedCount + "道。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiIntentStudy,
			"personal|fuqi-intent-study|" + actorId + "|" + studiedCount,
			XjWorldHistoryCategory.Cultivation, "观剑", "博采诸家", body,
			studiedCount >= 16 ? 3 : 2, false, familyId, familyName, sectId, sectName,
			intent.CreatorActorId, creator, XjHistoryResult.Success);
	}

	internal static void RecordFuQiZhenRen(Actor actor, int year)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		XjWeaponArtSystem.TryGetSwordIntent(actor, out string swordIntent);
		string intentText = string.IsNullOrWhiteSpace(swordIntent) ? "一己剑意" : "《" + swordIntent.Trim() + "》";
		string body = actorName + "以〖养青冥〗温养" + intentText
			+ "，使剑意由外归身，性命与神妙相合，终于求到自身之真，晋为真人。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiZhenRen,
			"personal|fuqi-zhenren|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "神妙归身", body,
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "zhenren", "真人", "神妙归身", "使〖养青冥〗由外归身，求到自身之真，晋为真人。",
			4, true, true, XjHistoryResult.Success);
	}

	internal static void RecordFuQiShenMiaoPerfected(Actor actor, int year)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string body = actorName + "将神妙〖养青冥〗温养至圆满，性命与剑意浑然如一，金性由此自然化生，已可求证真君羽士。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiShenMiaoPerfected,
			"personal|fuqi-perfect|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "神妙圆满", body,
			3, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "perfected", "性命修持", "神妙圆满", "将〖养青冥〗温养圆满，已可化生金性、求证真君。",
			3, false, false, XjHistoryResult.Success);
	}

	internal static void RecordFuQiJinDanFailure(Actor actor, int year, int injuryYears, int nurtureYears, int failureCount)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string body = actorName + "以圆满神妙化出金性，尝试求证真君羽士，然天地未曾认可其果。金性散去，剑意反震性命，遂闭关养伤"
			+ Math.Max(5, injuryYears) + "年；伤愈后尚需温养金性" + Math.Max(20, nurtureYears) + "年，方可再行求证。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiJinDanFailure,
			"personal|fuqi-jindan-failure|" + actorId + "|" + Math.Max(1, failureCount),
			XjWorldHistoryCategory.Cultivation, "求证真君羽士", "天地未认", body,
			3, false, familyId, familyName, sectId, sectName, result: XjHistoryResult.Failure);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "jindan-failure-" + Math.Max(1, failureCount), "求证受挫", "天地未认",
			"以圆满神妙求证真君未获天地认可，闭关养伤" + Math.Max(5, injuryYears) + "年。",
			3, false, false, XjHistoryResult.Failure);
	}

	internal static void RecordFuQiZhenJunYuShi(Actor actor, int year)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string body = actorName + "以圆满神妙求证真君羽士，性命、剑意与金性合而为一。天地认可其果，无名果位由此落世；其为此位命名长庚，登真君羽士。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiZhenJunYuShi,
			"personal|fuqi-zhenjun-yushi|" + actorId,
			XjWorldHistoryCategory.Cultivation, "天地认位", "长庚初名", body,
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "zhenjun-yushi", "真君羽士", "长庚初名", "得天地认果，为长庚果位初名，登真君羽士。",
			5, true, true, XjHistoryResult.Success);
	}

	internal static void RecordFuQiLongGengSuccession(Actor actor, int year)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string body = actorName + "于长庚果位空悬之时，以圆满神妙重新求证真君羽士。"
			+ "天地认可其果，果位由空悬归于有主；其承长庚旧名而登真君羽士。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiLongGengSuccession,
			"personal|fuqi-longgeng-succession|" + actorId + "|" + year,
			XjWorldHistoryCategory.Cultivation, "天地承位", "长庚继任", body,
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "longgeng-succession-" + year, "真君羽士", "长庚继任", "承接长庚果位，登真君羽士。",
			5, true, true, XjHistoryResult.Success);
	}

	internal static void RecordFuQiHuangGuan(Actor actor, int year)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		XjWeaponArtSystem.TryGetSwordIntent(actor, out string swordIntent);
		string intentText = string.IsNullOrWhiteSpace(swordIntent) ? "一己剑意" : "《" + swordIntent.Trim() + "》";
		string body = actorName + "采百二十八道剑气，遍观十六家剑意，洗尽旁人旧痕，终于炼成" + intentText
			+ "。其以性命温养此意，得神妙〖养青冥〗，自此脱离凡俗，位列黄冠。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalFuQiHuangGuan,
			"personal|fuqi-huangguan|" + actorId,
			XjWorldHistoryCategory.Cultivation, "服气养性", "养青冥初成", body,
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordFuQiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName,
			year, "huangguan", "黄冠", "养青冥初成", "炼成自身剑意，得神妙〖养青冥〗，位列黄冠。",
			4, false, false, XjHistoryResult.Success);
	}
}
