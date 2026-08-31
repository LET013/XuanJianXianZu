using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.History.Books;

/// <summary>
/// 三书第二批真实业务事实投影。所有入口均由原生关系、模组治理、战斗死亡、
/// 宗门关系或真实成品结果触发，不从公告正文和关键词反推。
/// </summary>
internal static partial class XjThreeBookWriter
{
	internal static void RecordAcquaintance(Actor left, Actor right, int year, string basis)
	{
		RecordSocialPair(left, right, year, XjThreeBookEventTypes.PersonalAcquaintance,
			"结识", "道途相逢",
			(leftName, rightName) => "修行途中，" + leftName + "与" + rightName + "相识。二人因" + SafeName(basis, "修途相逢") + "，自此彼此知名。",
			importance: 1, isProtected: false);
		XjShiMingShuSystem.TryGrantHighFateInteraction(left, right, year, "acquaintance");
		XjShiMingShuSystem.TryGrantHighFateInteraction(right, left, year, "acquaintance");
	}

	internal static void RecordCloseFriend(Actor left, Actor right, int year)
	{
		RecordSocialPair(left, right, year, XjThreeBookEventTypes.PersonalCloseFriend,
			"知交", "相交日深",
			(leftName, rightName) => "多年同行之后，" + leftName + "与" + rightName + "彼此扶持，成为修途中难得的好友。",
			importance: 2, isProtected: false);
		XjShiMingShuSystem.TryGrantHighFateInteraction(left, right, year, "close_friend");
		XjShiMingShuSystem.TryGrantHighFateInteraction(right, left, year, "close_friend");
	}

	internal static void RecordDaoCompanion(Actor left, Actor right, int year)
	{
		RecordSocialPair(left, right, year, XjThreeBookEventTypes.PersonalDaoCompanion,
			"道侣", "红尘结缘",
			(leftName, rightName) => "红尘路上，" + leftName + "与" + rightName + "相遇。二人同行渐久，情意相合，最终结为道侣，自此共行大道，互为扶持。",
			importance: 3, isProtected: true);
	}

	internal static void RecordFamilyCombatMerit(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.Year <= 0 || snapshot.LastAttackerId <= 0L || snapshot.ActorId <= 0L) return;
		int victimRealmOrder = XjRealmHelper.GetOrder(snapshot.RealmId);
		if (victimRealmOrder < XjRealmHelper.GetOrder(XjRealmIds.ZhuJi)) return;
		if (!XjScheduler.ResolveActor(snapshot.LastAttackerId, out Actor attacker) || attacker?.data == null || !attacker.isAlive()) return;
		int attackerRealmOrder = XjRealmHelper.GetOrder(XjRealmHelper.GetUnifiedId(attacker, XjRealmHelper.GetTraitSnapshotForRouter));
		// 只记录同境或越境击杀，避免高境清理低境时刷满家族纪事。
		if (attackerRealmOrder > 0 && victimRealmOrder < attackerRealmOrder) return;
		long attackerFamilyId = ResolveFamilyId(snapshot.LastAttackerId, 0L);
		if (attackerFamilyId <= 0L || attackerFamilyId == snapshot.FamilyStableId) return;

		string attackerName = SafeActorName(attacker);
		string familyName = ResolveFamilyName(attackerFamilyId);
		string victimName = SafeName(snapshot.Name, "一名强敌");
		string victimRealm = SafeName(XjRealmHelper.GetDisplayName(snapshot.RealmId), "高境");
		EnsureFamilyFounded(attackerFamilyId, familyName, snapshot.LastAttackerId, attackerName, snapshot.Year);
		RecordFamily(attackerFamilyId, familyName, snapshot.Year, XjThreeBookEventTypes.FamilyMemberMerit,
			"family|combat-merit|" + attackerFamilyId + "|" + snapshot.ActorId,
			"族人立功", "外战扬名",
			familyName + "族人" + attackerName + "于外斗法中斩落" + victimRealm + "修士" + victimName + "，为族中增添声望。",
			victimRealmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu) ? 4 : 3,
			victimRealmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu),
			snapshot.LastAttackerId, attackerName, result: XjHistoryResult.Success);
	}

	internal static void RecordFamilyDiscipline(long familyId, string familyName, long sectId, string sectName,
		int year, string reasonCode, string reason, bool severe)
	{
		if (familyId <= 0L || year <= 0) return;
		familyName = SafeName(familyName, ResolveFamilyName(familyId));
		sectName = ResolveSectName(sectId, sectName);
		string body = severe
			? familyName + "因" + SafeName(reason, "族中供养与行事失当") + "引来宗门与族议震动。议定之后，家门被削减供给与名额，并令相关族人闭门反省。"
			: familyName + "因" + SafeName(reason, "族中行事有失") + "受到责罚，族议与宗门暂削其供给和话语，以儆后来。";
		RecordFamily(familyId, familyName, year, XjThreeBookEventTypes.FamilyDiscipline,
			"family|discipline|" + familyId + "|" + SafeName(reasonCode, "discipline") + "|" + year,
			"族规惩处", severe ? "闭门反省" : "削减供给", body,
			severe ? 4 : 3, severe, sectId: sectId, sectName: sectName, result: XjHistoryResult.Failure);
	}

	internal static void RecordFamilyMentorship(Actor teacher, Actor student, int year)
	{
		if (teacher?.data == null || student?.data == null || year <= 0) return;
		long teacherId = ((BaseSystemData)teacher.data).id;
		long studentId = ((BaseSystemData)student.data).id;
		if (teacherId <= 0L || studentId <= 0L) return;
		string teacherRealm = XjRealmHelper.GetUnifiedId(teacher, XjRealmHelper.GetTraitSnapshotForRouter);
		string studentRealm = XjRealmHelper.GetUnifiedId(student, XjRealmHelper.GetTraitSnapshotForRouter);
		if (XjRealmHelper.GetOrder(teacherRealm) < XjRealmHelper.GetOrder(XjRealmIds.ZhuJi)
			|| XjRealmHelper.GetOrder(studentRealm) > XjRealmHelper.GetOrder(XjRealmIds.ZhuJi)) return;

		ResolveActorAffiliations(teacher, out long teacherFamily, out string teacherFamilyName, out long sectId, out string sectName);
		ResolveActorAffiliations(student, out long studentFamily, out string studentFamilyName, out _, out _);
		string teacherName = SafeActorName(teacher);
		string studentName = SafeActorName(student);

		if (teacherFamily > 0L)
		{
			EnsureFamilyFounded(teacherFamily, teacherFamilyName, teacherId, teacherName, year);
			string body = teacherFamily == studentFamily
				? teacherFamilyName + "族中由" + teacherName + "将" + studentName + "纳入门下，家学在同族师承之间继续相传。"
				: teacherFamilyName + "族人" + teacherName + "将外族后辈" + studentName + "纳入门下，自身所学由此传向家门之外。";
			RecordFamily(teacherFamily, teacherFamilyName, year, XjThreeBookEventTypes.FamilyMentorshipLegacy,
				"family|mentorship|teacher|" + teacherFamily + "|" + teacherId + "|" + studentId,
				"师徒传承", "家学相授", body, 2, false, teacherId, teacherName, sectId, sectName);
		}
		if (studentFamily > 0L && studentFamily != teacherFamily)
		{
			EnsureFamilyFounded(studentFamily, studentFamilyName, studentId, studentName, year);
			RecordFamily(studentFamily, studentFamilyName, year, XjThreeBookEventTypes.FamilyMentorshipLegacy,
				"family|mentorship|student|" + studentFamily + "|" + studentId + "|" + teacherId,
				"外来师承", "后辈得法",
				studentFamilyName + "后辈" + studentName + "得" + teacherName + "指点并列入门下，自此得承外来师法，家门传承也多了一条来路。",
				2, false, studentId, studentName, sectId, sectName);
		}
	}

	internal static void RecordSectFriendlyPair(long leftSectId, string leftSectName, long rightSectId, string rightSectName, int year, string reason)
	{
		if (leftSectId <= 0L || rightSectId <= 0L || leftSectId == rightSectId || year <= 0) return;
		leftSectName = ResolveSectName(leftSectId, leftSectName);
		rightSectName = ResolveSectName(rightSectId, rightSectName);
		RecordSect(leftSectId, leftSectName, year, XjThreeBookEventTypes.SectFriendlyRelation,
			"sect|friendly|" + leftSectId + "|" + rightSectId + "|" + year,
			"宗门友好", "旧隙渐消",
			leftSectName + "与" + rightSectName + "因" + SafeName(reason, "多年未再相争，旧日嫌隙逐渐消解") + "，恢复往来，关系渐趋平稳。",
			2, false, relatedSectId: rightSectId, relatedSectName: rightSectName, result: XjHistoryResult.Success);
		RecordSect(rightSectId, rightSectName, year, XjThreeBookEventTypes.SectFriendlyRelation,
			"sect|friendly|" + rightSectId + "|" + leftSectId + "|" + year,
			"宗门友好", "旧隙渐消",
			rightSectName + "与" + leftSectName + "因" + SafeName(reason, "多年未再相争，旧日嫌隙逐渐消解") + "，恢复往来，关系渐趋平稳。",
			2, false, relatedSectId: leftSectId, relatedSectName: leftSectName, result: XjHistoryResult.Success);
	}

	internal static void RecordSectAlliancePair(long leftSectId, string leftSectName, long rightSectId, string rightSectName,
		long commonEnemySectId, string commonEnemyName, int year)
	{
		if (leftSectId <= 0L || rightSectId <= 0L || commonEnemySectId <= 0L || leftSectId == rightSectId || year <= 0) return;
		leftSectName = ResolveSectName(leftSectId, leftSectName);
		rightSectName = ResolveSectName(rightSectId, rightSectName);
		commonEnemyName = ResolveSectName(commonEnemySectId, commonEnemyName);
		string period = XjChronology.ResolvePeriodIndex(year, 100).ToString();
		RecordSect(leftSectId, leftSectName, year, XjThreeBookEventTypes.SectAlliance,
			"sect|alliance|" + leftSectId + "|" + rightSectId + "|" + commonEnemySectId + "|" + period,
			"宗门联盟", "共御外敌",
			"面对" + commonEnemyName + "带来的外部压力，" + leftSectName + "与" + rightSectName + "暂结同盟，约定互通消息、共同戒备。",
			3, false, relatedSectId: rightSectId, relatedSectName: rightSectName, result: XjHistoryResult.Success);
		RecordSect(rightSectId, rightSectName, year, XjThreeBookEventTypes.SectAlliance,
			"sect|alliance|" + rightSectId + "|" + leftSectId + "|" + commonEnemySectId + "|" + period,
			"宗门联盟", "共御外敌",
			"面对" + commonEnemyName + "带来的外部压力，" + rightSectName + "与" + leftSectName + "暂结同盟，约定互通消息、共同戒备。",
			3, false, relatedSectId: leftSectId, relatedSectName: leftSectName, result: XjHistoryResult.Success);
	}

	internal static void RecordSectResourceMilestone(long sectId, string sectName, int year, int tier,
		int cityCount, int familyCount, int peakCount, int inheritanceCount, int treasureCount, bool hasFormation, bool hasSecretRealm)
	{
		if (sectId <= 0L || year <= 0 || tier <= 0) return;
		sectName = ResolveSectName(sectId, sectName);
		string stageName = tier >= 4 ? "底蕴雄厚" : tier == 3 ? "声势成形" : tier == 2 ? "根基渐厚" : "初具规模";
		string body = "多年积累之后，" + sectName + "山门" + stageName + "，已辖城" + cityCount
			+ "座、入门家族" + familyCount + "支、诸峰" + peakCount + "座、收录传承" + inheritanceCount
			+ "部、宗门重宝" + treasureCount + "件" + (hasFormation ? "，护宗大阵已立" : string.Empty)
			+ (hasSecretRealm ? "，并掌一处洞天福地" : string.Empty) + "。";
		RecordSect(sectId, sectName, year, XjThreeBookEventTypes.SectResourceMilestone,
			"sect|resource-milestone|" + sectId + "|" + tier,
			"山门底蕴", stageName, body, tier >= 3 ? 4 : tier + 1, tier >= 4, result: XjHistoryResult.Success);
	}

	internal static void RecordSectRenamed(long sectId, string oldName, string newName, int year)
	{
		if (sectId <= 0L || string.IsNullOrWhiteSpace(newName)) return;
		string previous = SafeName(oldName, "旧名");
		string current = SafeName(newName, "新名");
		if (string.Equals(previous, current, StringComparison.Ordinal)) return;
		RecordSect(sectId, current, year, XjThreeBookEventTypes.SectRenamed,
			"sect|renamed|" + sectId + "|" + year + "|" + previous + "|" + current,
			"更名", "山门定名",
			previous + "重定山门名号，自此改称" + current + "。旧名仍入宗门旧史，新名承接门人法脉与山门基业。",
			2, false, result: XjHistoryResult.Success);
	}

	internal static void RecordQuanBingSeized(Actor actor, string sourceDaoTu, string authority, int year, string reason)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(authority)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || year <= 0) return;
		string actorName = SafeActorName(actor);
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string source = SafeName(sourceDaoTu, "外道");
		string authorityName = SafeName(authority, "一缕权柄");
		string cause = SafeName(reason, "权柄之争");
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalQuanBingSeized,
			"personal|quanbing-seized|" + actorId + "|" + source + "|" + authorityName + "|" + year,
			XjWorldHistoryCategory.Cultivation, "夺柄", "夺得权柄",
			actorName + "因" + cause + "夺得" + source + "权柄“" + authorityName + "”。此后其借外道之柄壮大己道，原道权柄缺失，声势随之受损。",
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Transfer);
	}

	internal static void RecordQuanBingIntegrated(Actor actor, string sourceDaoTu, string authority, int year, bool success)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(authority)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || year <= 0) return;
		string actorName = SafeActorName(actor);
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string source = SafeName(sourceDaoTu, "外道");
		string authorityName = SafeName(authority, "外道权柄");
		string resultText = success ? XjHistoryResult.Success : XjHistoryResult.Failure;
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalQuanBingIntegrated,
			"personal|quanbing-integrated|" + actorId + "|" + source + "|" + authorityName + "|" + year + "|" + success,
			XjWorldHistoryCategory.Cultivation, success ? "合道" : "合道未成", success ? "融入权柄" : "权柄遁回",
			success
				? actorName + "闭关多年，终将" + source + "权柄“" + authorityName + "”纳入己身。自此此外道权柄正式归其果位运转。"
				: actorName + "尝试融入" + source + "权柄“" + authorityName + "”，终究未能合道。此权柄复归原道，只留一场险证。",
			success ? 5 : 3, success, familyId, familyName, sectId, sectName, result: resultText);
	}

	internal static void RecordZhengWeiSuccession(Actor actor, string previousHolderName, string sourceDaoTu, string guoWei, int year, bool adjacent)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(guoWei)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || year <= 0) return;
		string actorName = SafeActorName(actor);
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string predecessor = SafeName(previousHolderName, "前任正位");
		string daoTu = SafeName(sourceDaoTu, "本道");
		string eventType = XjThreeBookEventTypes.PersonalZhengWeiSuccession;
		RecordPersonal(actorId, actorName, year, eventType,
			"personal|zhengwei-succession|" + actorId + "|" + daoTu + "|" + guoWei.Trim() + "|" + year,
			XjWorldHistoryCategory.Cultivation, adjacent ? "转道承正" : "承正", adjacent ? "相邻承位" : "同道承位",
			adjacent
				? actorName + "斩落" + predecessor + "后，由原本道途转入" + daoTu + "，承继" + guoWei.Trim() + "，退入洞天稳固果位。"
				: actorName + "斩落" + predecessor + "后，承继" + guoWei.Trim() + "，得以补上本道正位，退入洞天稳固果位。",
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Transfer);
	}

	internal static void RecordRareCraft(Actor actor, string productName, string productClass, int quantity, int year)
	{
		if (actor?.data == null || year <= 0 || string.IsNullOrWhiteSpace(productName)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		string actorName = SafeActorName(actor);
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string itemClass = SafeName(productClass, "重宝成品");
		string unit = itemClass.IndexOf("丹", StringComparison.Ordinal) >= 0 ? "枚" : "件";
		string quantityText = quantity <= 1 ? "一" + unit : quantity + unit;
		string key = itemClass + "|" + productName + "|" + year;
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalRareCraft,
			"personal|rare-craft|" + actorId + "|" + key, XjWorldHistoryCategory.Craft,
			itemClass, "百艺成珍", actorName + "以多年百艺积累炼成《" + productName.Trim() + "》" + quantityText + "，此物非寻常成品可比。",
			4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		if (familyId > 0L)
		{
			RecordFamily(familyId, familyName, year, XjThreeBookEventTypes.FamilyRareCraft,
				"family|rare-craft|" + familyId + "|" + actorId + "|" + key,
				itemClass, "族中成珍", familyName + "族人" + actorName + "炼成《" + productName.Trim() + "》" + quantityText + "，家门百艺声名由此更盛。",
				3, false, actorId, actorName, sectId, sectName, result: XjHistoryResult.Success);
		}
		if (sectId > 0L)
		{
			RecordSect(sectId, sectName, year, XjThreeBookEventTypes.SectRareCraft,
				"sect|rare-craft|" + sectId + "|" + actorId + "|" + key,
				itemClass, "门中成珍", sectName + "门中" + actorName + "炼成《" + productName.Trim() + "》" + quantityText + "，山门百艺底蕴再添一笔。",
				3, false, actorId, actorName, familyId, familyName, result: XjHistoryResult.Success);
		}
	}

	private static void RecordSocialPair(Actor left, Actor right, int year, string eventType, string tag, string title,
		Func<string, string, string> bodyBuilder, int importance, bool isProtected)
	{
		if (left?.data == null || right?.data == null || ReferenceEquals(left, right) || year <= 0) return;
		long leftId = ((BaseSystemData)left.data).id;
		long rightId = ((BaseSystemData)right.data).id;
		if (leftId <= 0L || rightId <= 0L || leftId == rightId) return;
		string leftName = SafeActorName(left);
		string rightName = SafeActorName(right);
		ResolveActorAffiliations(left, out long leftFamily, out string leftFamilyName, out long leftSect, out string leftSectName);
		ResolveActorAffiliations(right, out long rightFamily, out string rightFamilyName, out long rightSect, out string rightSectName);
		RecordPersonal(leftId, leftName, year, eventType,
			"personal|social|" + eventType + "|" + leftId + "|" + rightId,
			XjWorldHistoryCategory.Family, tag, title, bodyBuilder(leftName, rightName), importance, isProtected,
			leftFamily, leftFamilyName, leftSect, leftSectName, rightId, rightName, XjHistoryResult.Success);
		RecordPersonal(rightId, rightName, year, eventType,
			"personal|social|" + eventType + "|" + rightId + "|" + leftId,
			XjWorldHistoryCategory.Family, tag, title, bodyBuilder(rightName, leftName), importance, isProtected,
			rightFamily, rightFamilyName, rightSect, rightSectName, leftId, leftName, XjHistoryResult.Success);
	}
}
