using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Sect;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.Systems.History.Books;

/// <summary>
/// 结构化业务事实到三书的唯一第一批投影入口。这里直接生成各书正文，
/// 不读取或改写天下公告，也不依赖 Narrative Adapter 的关键词判断。
/// </summary>
internal static partial class XjThreeBookWriter
{
	internal static void RecordPersonalFact(in XjFamilyDomainEvent fact)
	{
		if (!fact.Found || fact.ActorId <= 0L) return;
		string actorName = SafeName(fact.ActorName, "无名修士");
		long familyId = ResolveFamilyId(fact.ActorId, fact.FamilyStableId);
		string familyName = ResolveFamilyName(familyId);
		long sectId = ResolveSectId(fact.ActorId, fact.ZongMenId);
		string sectName = ResolveSectName(sectId, fact.ZongMenName);
		string baseKey = "personal|" + fact.EventType + "|" + fact.ActorId + "|" + fact.Year;

		switch (fact.EventType)
		{
			case XjFamilyDomainEvent.TypeBirth:
				RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalBirth,
					baseKey, XjWorldHistoryCategory.Family, "降生", "降生",
					XjChronology.FormatYear(fact.Year) + "，" + actorName + "降生" + (familyId > 0L ? "于" + familyName : "") + "。初生之时并无惊世异象，只随长辈抚育，静待日后机缘。",
					1, false, familyId, familyName, sectId, sectName);
				break;
			case XjFamilyDomainEvent.TypeAptitudeGranted:
				RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalAptitude,
					baseKey, XjWorldHistoryCategory.Cultivation, "天赋", "根骨初显",
					actorName + "年少时灵机渐显，经长辈查验，确认其根骨与常人有异，自此受到更多关注。",
					2, false, familyId, familyName, sectId, sectName);
				RecordCultivationQualified(fact.ActorId, actorName, familyId, familyName, sectId, sectName, fact.Year);
				break;
			case XjFamilyDomainEvent.TypeRealmBreakthrough:
				RecordPersonalRealm(fact, actorName, familyId, familyName, sectId, sectName);
				RecordSectHighRealm(fact.ActorId, actorName, fact.RealmId, fact.Year, sectId, sectName);
				break;
			case XjFamilyDomainEvent.TypeGongFaObtained:
				if (!string.IsNullOrWhiteSpace(fact.GongFaName) && fact.GongFaGrade >= 6)
				{
					int grade = Math.Max(1, Math.Min(7, fact.GongFaGrade));
					string sourceText = ResolveGongFaSourcePhrase(fact.Source, obtained: true);
					RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalGongFaObtained,
						baseKey + "|" + fact.GongFaName, XjWorldHistoryCategory.Inheritance, "功法", "得授法门",
						actorName + sourceText + "《" + fact.GongFaName + "》，录为" + ResolveGongFaGradeText(grade)
							+ "功法。此法自此收入其修行之中，成为" + SafeName(fact.DaoTu, "所修道途") + "的一份真实传承。",
						grade >= 5 ? 3 : 2, grade >= 5, familyId, familyName, sectId, sectName);
				}
				break;
			case XjFamilyDomainEvent.TypeGongFaPromoted:
				if (!string.IsNullOrWhiteSpace(fact.GongFaName) && fact.GongFaGrade >= 6)
				{
					int grade = Math.Max(2, Math.Min(7, fact.GongFaGrade));
					string sourceText = ResolveGongFaSourcePhrase(fact.Source, obtained: false);
					RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalGongFaObtained,
						baseKey + "|" + fact.GongFaName + "|" + grade,
						XjWorldHistoryCategory.Inheritance,
						"功法",
						"法门升品",
						actorName + sourceText + "《" + fact.GongFaName + "》推至" + ResolveGongFaGradeText(grade)
							+ "。这次升品来自已有功法的真实参悟，并非凭空得授另一部法门。",
						grade >= 5 ? 3 : 2,
						grade >= 5,
						familyId,
						familyName,
						sectId,
						sectName,
						result: XjHistoryResult.Success);
				}
				break;
			case XjFamilyDomainEvent.TypeQiuJinFaComprehended:
				RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalQiuJinFa,
					baseKey, XjWorldHistoryCategory.Inheritance, "求金法", "金门初启",
					actorName + "于多年参悟之后悟得求金法《" + SafeName(fact.QiuJinFaName, "无名求金法") + "》。金丹前路自此虽仍艰险，却已有法可循。",
					4, true, familyId, familyName, sectId, sectName);
				break;
			case XjFamilyDomainEvent.TypeFaBaoObtained:
				if (IsImportantArtifact(fact.FaBaoClass, fact.FaBaoName))
				{
					RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalFaBaoObtained,
						baseKey + "|" + fact.FaBaoId, XjWorldHistoryCategory.Inheritance, "重宝", "灵物随身",
						actorName + "获得" + SafeName(fact.FaBaoName, "一件灵物") + "。其上灵机不凡，日后或将成为修途中的重要助力。",
						3, false, familyId, familyName, sectId, sectName);
				}
				break;
			case XjFamilyDomainEvent.TypeDongTianSurvived:
				RecordPersonalDongTian(fact, actorName, familyId, familyName, sectId, sectName);
				break;
			case XjFamilyDomainEvent.TypeDongTianDeath:
				break;
			case XjFamilyDomainEvent.TypeJinDanSucceeded:
				// 真人登真君羽士已有服气专属个人、家族与宗门纪事；共用金丹成功链
				// 只负责功能闭环，不得再追加一条“凝结金丹”的重复错误文本。
				if (string.Equals(XjRealmHelper.NormalizeId(fact.RealmId), XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) break;
				RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalRealmBreakthrough,
					"personal|realm|" + fact.ActorId + "|JinDan", XjWorldHistoryCategory.Cultivation, "金丹", "金丹成就",
					"历经多年磨炼，" + actorName + "凝结金丹，证得" + SafeName(fact.GuoWei, "果位") + "。丹成之日周身灵机大盛，从此跻身世间高阶修士。",
					5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
				RecordSectHighRealm(fact.ActorId, actorName, XjRealmIds.JinDan, fact.Year, sectId, sectName);
				break;
			case XjFamilyDomainEvent.TypeShenDanSucceeded:
				RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalRealmBreakthrough,
					"personal|realm|" + fact.ActorId + "|ShenDan", XjWorldHistoryCategory.Cultivation, "神丹", "神丹成就",
					actorName + "求金功成，却因果位已有其主而依附其下，成就神丹。自此虽无独立果位，亦已踏入金丹层次。",
					5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
				RecordSectHighRealm(fact.ActorId, actorName, XjRealmIds.ShenDan, fact.Year, sectId, sectName);
				break;
		}
	}

	internal static void RecordFamilyFact(in XjFamilyDomainEvent fact)
	{
		if (!fact.Found || fact.FamilyStableId <= 0L) return;
		if (string.Equals(fact.EventType, XjFamilyDomainEvent.TypeBirth, StringComparison.Ordinal)) return;
		long familyId = fact.FamilyStableId;
		string familyName = ResolveFamilyName(familyId);
		string actorName = SafeName(fact.ActorName, "族中后辈");
		EnsureFamilyFounded(familyId, familyName, fact.ActorId, actorName, fact.Year);
		string baseKey = "family|" + fact.EventType + "|" + familyId + "|" + fact.ActorId + "|" + fact.Year;

		switch (fact.EventType)
		{
			case XjFamilyDomainEvent.TypeAptitudeGranted:
				RecordFamily(familyId, familyName, fact.Year, XjThreeBookEventTypes.FamilyTalentEmerged,
					baseKey, "天资", "后辈显资", familyName + "后辈" + actorName + "展露天资，族中长辈寄望其未来成就。",
					2, false, fact.ActorId, actorName);
				RecordFamily(familyId, familyName, fact.Year, XjThreeBookEventTypes.FamilyCultivatorEmerged,
					"family|cultivator|" + familyId + "|" + fact.ActorId, "修士", "踏入修途", familyName + "后辈" + actorName + "正式踏入修途，为族中新增一名修行之人。",
					2, false, fact.ActorId, actorName);
				break;
			case XjFamilyDomainEvent.TypeRealmBreakthrough:
				RecordFamilyRealm(fact, familyName);
				break;
			case XjFamilyDomainEvent.TypeJinDanSucceeded:
				if (string.Equals(XjRealmHelper.NormalizeId(fact.RealmId), XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) break;
				XjFamilyRealmAchievement jinDanFamily = XjFamilyRealmAchievementNarrative.Resolve(
					familyId, fact.ActorId, actorName, XjRealmIds.JinDan);
				RecordFamily(familyId, familyName, fact.Year, XjThreeBookEventTypes.FamilyHighRealmEmerged,
					"family|realm|" + familyId + "|" + fact.ActorId + "|JinDan", "金丹",
					XjFamilyRealmAchievementNarrative.BuildShortTitle(in jinDanFamily, "金丹"),
					familyName + "族人" + actorName + "凝结金丹"
						+ XjFamilyRealmAchievementNarrative.BuildEnding(in jinDanFamily, "金丹"),
					5, true, fact.ActorId, actorName);
				break;
			case XjFamilyDomainEvent.TypeShenDanSucceeded:
				RecordFamily(familyId, familyName, fact.Year, XjThreeBookEventTypes.FamilyHighRealmEmerged,
					"family|realm|" + familyId + "|" + fact.ActorId + "|ShenDan", "神丹", "神丹出世",
					familyName + "族人" + actorName + "成就神丹，为家门再添一位高境修士。",
					5, true, fact.ActorId, actorName);
				break;
		}
	}

	internal static void RecordCultivationQualified(long actorId, string actorName, long familyId, string familyName, long sectId, string sectName, int year)
	{
		if (actorId <= 0L) return;
		RecordPersonal(actorId, SafeName(actorName, "无名修士"), year, XjThreeBookEventTypes.PersonalCultivationQualified,
			"personal|cultivation-qualified|" + actorId + "|" + year, XjWorldHistoryCategory.Cultivation, "入道", "踏入修途",
			SafeName(actorName, "此人") + "年少之时正式踏入修途，引天地灵气入体。从此不再只是凡俗子弟，而成为修行之人。",
			2, false, familyId, familyName, sectId, sectName);
	}

	internal static void RecordMentorship(Actor teacher, Actor student, int year)
	{
		if (teacher?.data == null || student?.data == null) return;
		long teacherId = ((BaseSystemData)teacher.data).id;
		long studentId = ((BaseSystemData)student.data).id;
		if (teacherId <= 0L || studentId <= 0L) return;
		string studentRealm = XjRealmHelper.GetUnifiedId(student, XjRealmHelper.GetTraitSnapshotForRouter);
		if (XjRealmHelper.GetOrder(studentRealm) > XjRealmHelper.GetOrder(XjRealmIds.ZhuJi)) return;
		string teacherName = SafeActorName(teacher);
		string studentName = SafeActorName(student);
		ResolveActorAffiliations(teacher, out long teacherFamily, out string teacherFamilyName, out long sectId, out string sectName);
		ResolveActorAffiliations(student, out long studentFamily, out string studentFamilyName, out _, out _);
		RecordPersonal(teacherId, teacherName, year, XjThreeBookEventTypes.PersonalStudentAccepted,
			"personal|student|" + teacherId + "|" + studentId, XjWorldHistoryCategory.Inheritance, "传承", "衣钵有继",
			teacherName + "见" + studentName + "尚可造就，遂纳入门下，传授修行关窍。", 2, false,
			teacherFamily, teacherFamilyName, sectId, sectName, studentId, studentName);
		RecordPersonal(studentId, studentName, year, XjThreeBookEventTypes.PersonalMentorAccepted,
			"personal|teacher|" + studentId + "|" + teacherId, XjWorldHistoryCategory.Inheritance, "拜师", "得遇师承",
			studentName + "得" + teacherName + "指点，列入门下，自此修行有了师承。", 2, false,
			studentFamily, studentFamilyName, sectId, sectName, teacherId, teacherName);
		RecordFamilyMentorship(teacher, student, year);
	}

	internal static void RecordDeath(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L || !XjRealmHelper.ShouldRecord(snapshot.RealmId)) return;
		string name = SafeName(snapshot.Name, "无名修士");
		string realm = XjRealmHelper.GetDisplayName(snapshot.RealmId);
		bool battle = snapshot.LastAttackerId > 0L || ContainsAny(snapshot.ReasonCode, "battle", "combat", "attack", "kill");
		string body = battle
			? "一场斗法之后，" + name + "未能归来，最终陨落于此。"
			: "岁月流转，" + name + "寿元尽时，安然坐化。";
		if (!string.IsNullOrWhiteSpace(realm)) body = name + "以" + realm + "之身" + (battle ? "战死，未能归来。" : "寿尽坐化，归于天地。 ");
		string familyName = ResolveFamilyName(snapshot.FamilyStableId);
		int realmOrder = XjRealmHelper.GetOrder(snapshot.RealmId);
		int deathImportance = realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.JinDan) ? 5
			: realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu) ? 4 : 2;
		bool protectDeath = realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
		RecordPersonal(snapshot.ActorId, name, snapshot.Year, XjThreeBookEventTypes.PersonalDeath,
			"personal|death|" + snapshot.ActorId + "|" + snapshot.Year, XjWorldHistoryCategory.LifeAndDeath,
			battle ? "战死" : "寿终", battle ? "斗法陨落" : "寿尽坐化", body,
			deathImportance,
			protectDeath,
			snapshot.FamilyStableId, familyName, 0L, string.Empty, snapshot.LastAttackerId, snapshot.LastAttackerName, XjHistoryResult.Death);
		if (snapshot.FamilyStableId > 0L)
		{
			RecordFamily(snapshot.FamilyStableId, familyName, snapshot.Year, XjThreeBookEventTypes.FamilyMemberDeath,
				"family|death|" + snapshot.FamilyStableId + "|" + snapshot.ActorId + "|" + snapshot.Year,
				"生死", "族人陨落", familyName + "族人" + name + "身故。其名仍留族谱，生前修为与行迹亦为后人所记。",
				deathImportance >= 4 ? 4 : 2,
				protectDeath, snapshot.ActorId, name);
		}
		if (battle) RecordFamilyCombatMerit(snapshot);
	}


	internal static void RecordFamilyStageChanged(long familyId, string familyName, string previousStage, string currentStage, int year, string reason)
	{
		if (familyId <= 0L || string.IsNullOrWhiteSpace(currentStage)
			|| string.Equals(previousStage, currentStage, StringComparison.Ordinal)) return;
		familyName = SafeName(familyName, ResolveFamilyName(familyId));
		bool rising = string.Equals(currentStage, XjCenturyFamilyStage.XingSheng, StringComparison.Ordinal)
			|| string.Equals(currentStage, XjCenturyFamilyStage.DingSheng, StringComparison.Ordinal)
			|| string.Equals(currentStage, XjCenturyFamilyStage.FuZhen, StringComparison.Ordinal);
		bool falling = string.Equals(currentStage, XjCenturyFamilyStage.ZhongShuai, StringComparison.Ordinal)
			|| string.Equals(currentStage, XjCenturyFamilyStage.FenLie, StringComparison.Ordinal)
			|| string.Equals(currentStage, XjCenturyFamilyStage.FuMie, StringComparison.Ordinal);
		string tag = rising ? "家势渐盛" : falling ? "家势衰变" : "家势迁转";
		string title = rising ? "门庭兴盛" : falling ? "家声渐衰" : "家势有变";
		string body;
		if (string.Equals(currentStage, XjCenturyFamilyStage.FuMie, StringComparison.Ordinal))
			body = familyName + "后继无人，族中高手与修士相继凋零，一脉传承至此断绝。";
		else if (rising)
			body = "一代代族人修行有成，" + familyName + "声势渐盛，家门由" + SafeName(previousStage, "旧日") + "转入" + currentStage + "。";
		else if (falling)
			body = familyName + "后继乏力，族中高手凋零，家势由" + SafeName(previousStage, "旧日") + "转为" + currentStage + "。";
		else
			body = familyName + "历经岁月变迁，家势由" + SafeName(previousStage, "草创") + "转入" + currentStage + "。";
		if (!string.IsNullOrWhiteSpace(reason)) body += "其因由记为：" + reason.Trim();
		RecordFamily(familyId, familyName, year, XjThreeBookEventTypes.FamilyStageChanged,
			"family|stage|" + familyId + "|" + currentStage + "|" + year,
			tag, title, body, string.Equals(currentStage, XjCenturyFamilyStage.FuMie, StringComparison.Ordinal) ? 4 : 3,
			string.Equals(currentStage, XjCenturyFamilyStage.FuMie, StringComparison.Ordinal),
			result: falling ? XjHistoryResult.Failure : rising ? XjHistoryResult.Success : XjHistoryResult.None);
	}

	internal static void RecordSectFounded(Actor founder, long sectId, string sectName, string daoTu, int year)
	{
		if (founder?.data == null || sectId <= 0L || string.IsNullOrWhiteSpace(sectName)) return;
		long actorId = ((BaseSystemData)founder.data).id;
		int ziFuEnteredYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(founder);
		if (ziFuEnteredYear > 0) year = Math.Max(year, ziFuEnteredYear);
		year = Math.Max(year, Math.Max(0, World.world?.map_stats?.year ?? 0));
		string actorName = SafeActorName(founder);
		ResolveActorAffiliations(founder, out long familyId, out string familyName, out _, out _);
		string body = XjChronology.FormatYear(year) + "，" + actorName + "于此地立下山门，开创【" + sectName + "】。自此山门有主、法统有承。";
		if (familyId > 0L) body += "其并非孤身立派，而是携" + familyName + "多年积累，于此开山立宗。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalSectFounded,
			"personal|sect-founded|" + actorId + "|" + sectId, XjWorldHistoryCategory.Sect, "开宗", "开山立派",
			actorName + "修有所成后，于此地开创【" + sectName + "】，将自身所学化作一方山门传承。", 4, true,
			familyId, familyName, sectId, sectName);
		if (familyId > 0L)
		{
			EnsureFamilyFounded(familyId, familyName, actorId, actorName, year);
			RecordFamily(familyId, familyName, year, XjThreeBookEventTypes.FamilySectFounded,
				"family|sect-founded|" + familyId + "|" + sectId, "开宗", "传承外显",
				familyName + "族人" + actorName + "携族中积累开创【" + sectName + "】，家门传承自此向山门延伸。", 4, true, actorId, actorName, sectId, sectName);
		}
		RecordSect(sectId, sectName, year, XjThreeBookEventTypes.SectFounded,
			"sect|founded|" + sectId, "开宗", "山门初立", body, 5, true, actorId, actorName, familyId, familyName);
	}

	internal static void RecordFamilySupport(long familyId, Actor actor, string purpose, string detail, int year, bool granted)
	{
		if (familyId <= 0L || actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		string actorName = SafeActorName(actor);
		string familyName = ResolveFamilyName(familyId);
		EnsureFamilyFounded(familyId, familyName, actorId, actorName, year);
		string eventType = granted ? XjThreeBookEventTypes.FamilySupportGranted : XjThreeBookEventTypes.FamilySupportSelected;
		string tag = granted ? "族库扶持" : "族议举后";
		string title = granted ? "资源倾注" : "族议定选";
		string purposeText = SafeName(purpose, "更进一步");
		string goal = purposeText.StartsWith("扶持", StringComparison.Ordinal)
			? purposeText.Substring(2)
			: purposeText;
		string body = granted
			? familyName + "族中开启积累，将" + SafeName(detail, "修行资源") + "交予" + actorName + "，并在此后数年向其倾斜传承，以助其" + goal + "。"
			: familyName + "诸房会于族堂，反复衡量资质、年岁与当前关隘，最终推举" + actorName + "为这一阶段重点扶持的后辈。";
		RecordFamily(familyId, familyName, year, eventType,
			"family|support|" + (granted ? "grant" : "select") + "|" + familyId + "|" + actorId + "|" + year,
			tag, title, body, granted ? 3 : 2, false, actorId, actorName);
	}


	internal static void RecordSectEnrollment(long sectId, string sectName, Actor actor, int year)
	{
		if (sectId <= 0L || actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		string actorName = SafeActorName(actor);
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out _, out _);
		string resolvedSectName = ResolveSectName(sectId, sectName);
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		bool highRealm = XjRealmHelper.GetOrder(realmId) >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
		string body = highRealm
			? actorName + "加入" + resolvedSectName + "，以高境修为列入山门名册，自此与此宗共承法脉。"
			: actorName + "加入" + resolvedSectName + "，自此纳入山门名册，与诸峰同承法脉。";
		RecordSect(sectId, resolvedSectName, year, XjThreeBookEventTypes.SectEnrollment,
			"sect|enrollment|" + sectId + "|" + actorId,
			"入门", highRealm ? "高境入宗" : "山门纳新",
			body,
			highRealm ? 3 : 2, highRealm, actorId, actorName, familyId, familyName);
	}

	internal static void RecordSectLecture(long sectId, string sectName, long lecturerId, string lecturerName, int year, int attendeeCount, bool highRealm)
	{
		if (sectId <= 0L) return;
		string body = highRealm
			? SafeName(lecturerName, "一位真君") + "登坛讲法，言传自身多年所悟，诸峰弟子皆来听讲。"
			: SafeName(lecturerName, "一位真人") + "于山门讲道，门下弟子皆有所获。";
		if (attendeeCount > 0) body += "此番共有" + attendeeCount + "名弟子列席。";
		RecordSect(sectId, ResolveSectName(sectId, sectName), year, XjThreeBookEventTypes.SectLecture,
			"sect|lecture|" + sectId + "|" + lecturerId + "|" + year, "讲法", "开坛讲法", body, highRealm ? 4 : 2, highRealm,
			lecturerId, lecturerName);
	}

	internal static void RecordSectTournament(long sectId, string sectName, int year, int entrantCount, int winnerCount)
	{
		if (sectId <= 0L) return;
		string body = "山门按三年之期举行门中考较，诸峰门人依次论道试法。";
		if (entrantCount > 0) body += "此届共有" + entrantCount + "人入试，";
		body += "最终择出" + Math.Max(1, winnerCount) + "人取得秘境修持名额。";
		RecordSect(sectId, ResolveSectName(sectId, sectName), year, XjThreeBookEventTypes.SectTournament,
			"sect|tournament|" + sectId + "|" + year, "大比", "山门论道", body, 3, false);
	}

	internal static void RecordSectSecretRealmQualification(long sectId, string sectName, long actorId, string actorName, string realmName, int year)
	{
		if (sectId <= 0L || actorId <= 0L) return;
		string resolvedSectName = ResolveSectName(sectId, sectName);
		string resolvedActorName = SafeName(actorName, "门中弟子");
		string resolvedRealmName = SafeName(realmName, "宗门秘境");
		RecordSect(sectId, resolvedSectName, year, XjThreeBookEventTypes.SectSecretRealmQualification,
			"sect|realm-qualified|" + sectId + "|" + actorId + "|" + year, "洞天资格", "入境寻缘",
			resolvedActorName + "经过门中选拔，代表山门进入【" + resolvedRealmName + "】，寻求自身机缘。",
			2, false, actorId, resolvedActorName);

		long familyId = ResolveFamilyId(actorId, 0L);
		string familyName = ResolveFamilyName(familyId);
		RecordPersonal(actorId, resolvedActorName, year, XjThreeBookEventTypes.PersonalSectTournament,
			"personal|sect-tournament|" + actorId + "|" + sectId + "|" + year,
			XjWorldHistoryCategory.Sect, "宗门大比", "大比入选",
			resolvedActorName + "参加" + resolvedSectName + "山门大比，于同门论道中脱颖而出，取得进入【" + resolvedRealmName + "】修炼的资格。",
			2, false, familyId, familyName, sectId, resolvedSectName, result: XjHistoryResult.Success);
		if (familyId > 0L)
		{
			EnsureFamilyFounded(familyId, familyName, actorId, resolvedActorName, year);
			RecordFamily(familyId, familyName, year, XjThreeBookEventTypes.FamilyMemberAchievement,
				"family|sect-tournament|" + familyId + "|" + actorId + "|" + sectId + "|" + year,
				"族人立功", "大比获选",
				familyName + "子弟" + resolvedActorName + "在" + resolvedSectName + "山门大比中脱颖而出，为族中增添声望，并取得【" + resolvedRealmName + "】修炼资格。",
				2, false, actorId, resolvedActorName, sectId, resolvedSectName, XjHistoryResult.Success);
		}
	}

	internal static void RecordSectLeadership(long sectId, string sectName, long actorId, string actorName, string roleName, int year, string sourceFactId)
	{
		if (sectId <= 0L) return;
		bool sovereign = ContainsAny(roleName, "宗主", "掌门");
		RecordSect(sectId, ResolveSectName(sectId, sectName), year,
			sovereign ? XjThreeBookEventTypes.SectSovereignChanged : XjThreeBookEventTypes.SectPeakMasterChanged,
			SafeName(sourceFactId, "sect|leadership|" + sectId + "|" + actorId + "|" + year),
			sovereign ? "宗主更替" : "峰主更替", sovereign ? "山门易主" : "峰位传承",
			sovereign
				? SafeName(actorName, "门中修士") + "承接宗主之位，自此主持山门诸事。"
				: SafeName(actorName, "门中修士") + "承接" + SafeName(roleName, "峰主") + "之位，统领一峰弟子。",
			3, false, actorId, actorName);
	}


	internal static void RecordSectRelationChanged(
		long sectId,
		string sectName,
		long relatedSectId,
		string relatedSectName,
		int year,
		string relationLabel,
		string summary)
	{
		if (sectId <= 0L || relatedSectId <= 0L || sectId == relatedSectId) return;
		string resolvedSectName = ResolveSectName(sectId, sectName);
		string resolvedRelatedName = ResolveSectName(relatedSectId, relatedSectName);
		string relation = SafeName(relationLabel, "关系变化");
		string body;
		if (ContainsAny(relation, "敌对", "宿敌", "较差"))
			body = resolvedSectName + "与" + resolvedRelatedName + "因旧怨积累而彼此戒备，门下往来渐断，冲突也日益增多。";
		else if (ContainsAny(relation, "联盟", "盟约"))
			body = resolvedSectName + "与" + resolvedRelatedName + "面对外部压力，议定暂结同盟，共渡难关。";
		else if (ContainsAny(relation, "友好", "亲善"))
			body = resolvedSectName + "与" + resolvedRelatedName + "多年往来，互通资源，关系日益稳固。";
		else
			body = resolvedSectName + "与" + resolvedRelatedName + "往来生变，双方关系转为" + relation + "。";
		RecordSect(sectId, resolvedSectName, year, XjThreeBookEventTypes.SectRelationChanged,
			"sect|relation|" + sectId + "|" + relatedSectId + "|" + relation + "|" + year,
			relation, "宗门关系", body, ContainsAny(relation, "敌对", "宿敌") ? 3 : 2, ContainsAny(relation, "敌对", "宿敌"),
			relatedSectId: relatedSectId, relatedSectName: resolvedRelatedName);
	}

	internal static void RecordSectWarResult(
		long sectId,
		string sectName,
		long relatedSectId,
		string relatedSectName,
		int year,
		bool victory,
		string summary,
		bool mutualDestruction = false)
	{
		if (sectId <= 0L) return;
		string resolvedSectName = ResolveSectName(sectId, sectName);
		string resolvedRelatedName = ResolveSectName(relatedSectId, relatedSectName);
		string tag = mutualDestruction ? "两宗同毁" : victory ? "宗战告捷" : "山门受创";
		string title = mutualDestruction ? "传承俱伤" : victory ? "宗门扬威" : "战事失利";
		string body = mutualDestruction
			? resolvedSectName + "与" + resolvedRelatedName + "的争端最终走向同毁。两座山门皆遭重创，门中传承与弟子损失惨重。"
			: victory
				? resolvedSectName + "在与" + resolvedRelatedName + "的宗门战争中取得胜势。此战之后，本宗声势大涨。"
				: resolvedSectName + "在与" + resolvedRelatedName + "的宗门战争中失利，山门受创，门中上下由此更加戒备。";
		RecordSect(sectId, resolvedSectName, year, XjThreeBookEventTypes.SectWarResult,
			"sect|war|" + sectId + "|" + relatedSectId + "|" + year + "|" + (mutualDestruction ? "mutual" : victory ? "win" : "loss"),
			tag, title, body, mutualDestruction ? 5 : 4, true,
			relatedSectId: relatedSectId, relatedSectName: resolvedRelatedName,
			result: victory ? XjHistoryResult.Success : XjHistoryResult.Failure);
	}

	internal static void RecordSectExtinct(long sectId, string sectName, int year, string previousStatus)
	{
		if (sectId <= 0L) return;
		string resolvedSectName = ResolveSectName(sectId, sectName);
		RecordSect(sectId, resolvedSectName, year, XjThreeBookEventTypes.SectExtinct,
			"sect|extinct|" + sectId, "宗门覆灭", "山门断绝",
			resolvedSectName + "失去最后一处有效山门，门中传承与组织至此断绝。其旧日由" + SafeName(previousStatus, "山门") + "而兴，也在此年归入宗门旧史。",
			5, true, result: XjHistoryResult.Failure);
	}

	internal static void RecordSectInheritance(long sectId, string sectName, long actorId, string actorName, string inheritanceName, string kind, int year)
	{
		if (sectId <= 0L || string.IsNullOrWhiteSpace(inheritanceName)) return;
		if (ContainsAny(kind, "采气法", "一品", "1品", "二品", "2品", "三品", "3品", "四品", "4品", "五品", "5品")) return;
		string body = "山门收录《" + inheritanceName.Trim() + "》，自此门中又添一份" + SafeName(kind, "传承") + "，可供后辈参阅修习。";
		RecordSect(sectId, ResolveSectName(sectId, sectName), year, XjThreeBookEventTypes.SectInheritanceAdded,
			"sect|inheritance|" + sectId + "|" + kind + "|" + inheritanceName,
			"传承入阁", "山门添法", body, 3, false, actorId, actorName);
	}

	internal static void RecordSectResourceAdded(long sectId, string sectName, long familyId, string familyName, string resourceName, string reason, int year)
	{
		if (sectId <= 0L || string.IsNullOrWhiteSpace(resourceName)) return;
		string resolvedSectName = ResolveSectName(sectId, sectName);
		string body = string.IsNullOrWhiteSpace(reason)
			? resolvedSectName + "将“" + resourceName.Trim() + "”收入宗门共库，山门底蕴由此增长。"
			: reason.Trim();
		RecordSect(sectId, resolvedSectName, year, XjThreeBookEventTypes.SectResourceChanged,
			"sect|resource|" + sectId + "|" + familyId + "|" + resourceName + "|" + year,
			"资源增长", "山门增藏", body, 2, false,
			familyId: familyId, familyName: familyName);
	}

	internal static void RecordFamilyInheritance(long familyId, string familyName, long actorId, string actorName, string inheritanceName, string kind, int year)
	{
		if (familyId <= 0L || string.IsNullOrWhiteSpace(inheritanceName)) return;
		if (ContainsAny(kind, "采气法", "一品", "1品", "二品", "2品", "三品", "3品", "四品", "4品", "五品", "5品")) return;
		familyName = SafeName(familyName, ResolveFamilyName(familyId));
		EnsureFamilyFounded(familyId, familyName, actorId, actorName, year);
		RecordFamily(familyId, familyName, year, XjThreeBookEventTypes.FamilyInheritanceAdded,
			"family|inheritance|" + familyId + "|" + kind + "|" + inheritanceName,
			"传承入族", "家学增益", familyName + "收录《" + inheritanceName.Trim() + "》，族中自此又添一份" + SafeName(kind, "传承") + "。",
			3, false, actorId, actorName);
	}

	internal static void RecordFamilyTreasureAdded(long familyId, string familyName, long actorId, string actorName, string treasureId, string treasureName, int amount, int year)
	{
		if (familyId <= 0L || string.IsNullOrWhiteSpace(treasureId) || amount <= 0) return;
		familyName = SafeName(familyName, ResolveFamilyName(familyId));
		EnsureFamilyFounded(familyId, familyName, actorId, actorName, year);
		string resolvedTreasure = SafeName(treasureName, "一件灵物");
		string amountText = amount > 1 ? "共" + amount + "份" : "一份";
		string sourceText = actorId > 0L ? "由" + SafeName(actorName, "族中修士") + "带回，" : string.Empty;
		RecordFamily(familyId, familyName, year, XjThreeBookEventTypes.FamilyTreasureAdded,
			"family|treasure|" + familyId + "|" + treasureId + "|" + actorId + "|" + year,
			"重宝入库", "底蕴再增",
			familyName + sourceText + resolvedTreasure + amountText + "归入族中重宝仓库，家门底蕴由此再添一分。",
			3, false, actorId, actorName);
	}

	internal static void RecordWeaponArt(Actor actor, string artName, int year)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string art = SafeName(artName, "器艺");
		string weapon = ResolveWeaponArtWeapon(art);
		string rank = ResolveWeaponArtRank(art);
		int importance = string.Equals(rank, "意", StringComparison.Ordinal) ? 3
			: string.Equals(rank, "元", StringComparison.Ordinal) ? 2 : 1;
		string title = weapon + rank + (string.Equals(rank, "意", StringComparison.Ordinal) ? "自成" : "初悟");
		string body;
		if (string.Equals(rank, "意", StringComparison.Ordinal))
		{
			body = actorName + "多年持" + weapon + "，于斗法与修行之间渐悟其中真意。某日锋芒自显，" + weapon + "意自成。";
		}
		else if (string.Equals(rank, "元", StringComparison.Ordinal))
		{
			body = actorName + "持" + weapon + "修行多年，终于将真元运转融入兵刃，悟得" + art + "。";
		}
		else if (string.Equals(rank, "芒", StringComparison.Ordinal))
		{
			body = actorName + "在长期磨砺之中窥见锋芒变化，出手之间渐有" + art + "相随。";
		}
		else
		{
			body = actorName + "持" + weapon + "修行日久，气机渐与兵刃相合，初步悟得" + art + "。";
		}
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalWeaponArt,
			"personal|weapon-art|" + actorId + "|" + art, XjWorldHistoryCategory.Cultivation, weapon + rank, title, body,
			importance, false, familyId, familyName, sectId, sectName);
	}

	internal static void RecordShenTongComprehended(Actor actor, string shenTongName, int year, string source)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string shenTongId = XjXianJiCatalog.NormalizeXianJiId(shenTongName);
		string name = SafeName(shenTongId, "无名神通");
		string body;
		if (XjActorGongFaCollection.TryGetByMappedXianJi(actor, shenTongId, out XjActorGongFaCollection.Record gongFa)
			&& !string.IsNullOrWhiteSpace(gongFa.Name))
		{
			body = actorName + "通过《" + gongFa.Name.Trim() + "》成功修出神通【" + name + "】，百尺竿头更进一步。";
		}
		else
		{
			// 正常主链在写神通前已原子创建映射功法；此分支只用于极早期旧档或异常补录。
			body = actorName + "参悟所修功法，成功修出神通【" + name + "】，百尺竿头更进一步。";
		}
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShenTongComprehended,
			"personal|shentong|" + actorId + "|" + name, XjWorldHistoryCategory.Cultivation, "神通", "神通初成",
			body,
			3, false, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
	}


	internal static void RecordShenTongChanged(
		Actor actor, string oldShenTong, string newShenTong, int year, string reason)
	{
		if (actor?.data == null || year <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string oldName = SafeName(oldShenTong, "旧神通");
		string newName = SafeName(newShenTong, "新神通");
		string cause = SafeName(reason, "权柄迁易");
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShenTongComprehended,
			"personal|shentong-changed|" + actorId + "|" + oldName + "|" + newName + "|" + year,
			XjWorldHistoryCategory.Cultivation, "神通易象", "神通随柄而易",
			actorName + "因" + cause + "，原神通【" + oldName + "】随道统权柄变化，易为【" + newName + "】。",
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Transfer);
	}

	internal static void RecordShenTongAuthorityChanged(Actor actor, string body, int year)
	{
		if (actor?.data == null || year <= 0 || string.IsNullOrWhiteSpace(body)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShenTongComprehended,
			"personal|shentong-authority|" + actorId + "|" + year + "|"
				+ XjDeterministicHash.PositiveHash(actorId + year, "shentong-authority|" + body.Trim()),
			XjWorldHistoryCategory.Cultivation, "神通随柄", "神通显化迁易",
			body.Trim(), 3, false, familyId, familyName, sectId, sectName, result: XjHistoryResult.Transfer);
	}

	internal static void RecordOtherShenTongDeception(
		Actor victim, Actor deceiver, int year, string ownerDaoTu, string shenTongName,
		string method, string source)
	{
		if (victim?.data == null) return;
		long victimId = ((BaseSystemData)victim.data).id;
		if (victimId <= 0L) return;
		ResolveActorAffiliations(victim, out long familyId, out string familyName, out long sectId, out string sectName);
		string victimName = SafeActorName(victim);
		long deceiverId = deceiver?.data == null ? 0L : ((BaseSystemData)deceiver.data).id;
		string deceiverName = deceiverId > 0L ? SafeActorName(deceiver) : "一名来历不明的修士";
		string cause = string.Equals(method, "蛊惑", StringComparison.Ordinal) ? "蛊惑" : "欺骗";
		string daoTu = SafeName(ownerDaoTu, "异道");
		string shenTong = SafeName(shenTongName, "异道神通");
		string sourceText = string.IsNullOrWhiteSpace(source) ? string.Empty : "在" + source.Trim() + "时";
		RecordPersonal(victimId, victimName, year, XjThreeBookEventTypes.PersonalOtherShenTongDeceived,
			"personal|other-shentong|victim|" + victimId + "|" + shenTong + "|" + year,
			XjWorldHistoryCategory.Cultivation, "异道神通", "误入异道",
			victimName + sourceText + "受" + deceiverName + cause + "，误将【" + shenTong + "】当作自身道途法门修成。"
				+ "此神通实属【" + daoTu + "】，与其本途不能相合，求金之路自此断绝。",
			4, true, familyId, familyName, sectId, sectName, deceiverId, deceiverName, XjHistoryResult.Failure);

		if (deceiverId <= 0L) return;
		ResolveActorAffiliations(deceiver, out long otherFamilyId, out string otherFamilyName, out long otherSectId, out string otherSectName);
		RecordPersonal(deceiverId, deceiverName, year, XjThreeBookEventTypes.PersonalOtherShenTongDeceiver,
			"personal|other-shentong|deceiver|" + deceiverId + "|" + victimId + "|" + shenTong + "|" + year,
			XjWorldHistoryCategory.Cultivation, "异道神通", cause + "异修",
			deceiverName + "以【" + daoTu + "】神通【" + shenTong + "】" + cause + victimName
				+ "，使其误入异道，断去求金前路。",
			3, true, otherFamilyId, otherFamilyName, otherSectId, otherSectName, victimId, victimName, XjHistoryResult.Success);
	}

	internal static void RecordJieLinSucceeded(Actor actor, int year, int activeCount)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string countText = activeCount > 0 ? "此时世间结璘仙已有" + activeCount + "人。" : string.Empty;
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalJieLinSucceeded,
			"personal|realm|" + actorId + "|JieLinXian", XjWorldHistoryCategory.Cultivation, "结璘仙", "结璘成仙",
			actorName + "求金未成，却于太阴道途中结璘成仙。初成时不占天地位序，仍以结璘之身立于高境之列；待积修成熟，亦可再证余位或闰位。" + countText,
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
	}

	internal static void RecordYuYiSucceeded(Actor actor, int year, int activeCount)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string countText = activeCount > 0 ? "此时世间郁仪仙已有" + activeCount + "人。" : string.Empty;
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalYuYiSucceeded,
			"personal|realm|" + actorId + "|YuYiXian", XjWorldHistoryCategory.Cultivation, "郁仪仙", "郁仪成仙",
			actorName + "求金未成，却受太阳果位日精垂照，得郁仪文而成郁仪仙。初成时不占天地位序，待积修成熟亦可再证余位或闰位。" + countText,
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
	}

	internal static void RecordSpecialXianPositionPromotion(
		Actor actor,
		string identity,
		string daoTu,
		string positionType,
		string positionName,
		int year)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string specialIdentity = SafeName(identity, "特殊仙身");
		string manifestDaoTu = SafeName(daoTu, "未明道途");
		string kind = SafeName(positionType, "派生位序");
		string position = SafeName(positionName, manifestDaoTu + kind);
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalRealmBreakthrough,
			"personal|special-position|" + actorId + "|" + position + "|" + Math.Max(1, year),
			XjWorldHistoryCategory.Cultivation, specialIdentity, specialIdentity + "入位",
			actorName + "由" + specialIdentity + "之身积年修持，道行终于与天地位序相合，证入【"
				+ position + "】。自此特殊仙身阶段已毕，正式列于" + manifestDaoTu + kind + "，以真君位序行世。",
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
	}

	internal static void RecordJinDanReincarnation(Actor actor, int year, string sourceName)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string sourceText = string.IsNullOrWhiteSpace(sourceName) ? "前尘旧缘" : sourceName.Trim() + "旧缘";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalJinDanReincarnation,
			"personal|reincarnation|" + actorId + "|" + sourceText, XjWorldHistoryCategory.Opportunity, "转世", "宿慧初显",
			actorName + "幼时便有前尘宿慧显露，疑承" + sourceText + "而来。此身虽是新生，修途之上却多了一份难言旧识。",
			4, true, familyId, familyName, sectId, sectName);
	}

	internal static void RecordCraftAbility(Actor actor, string craftName, int year)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string craft = SafeName(craftName, "百艺");
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalCraftAbility,
			"personal|craft-ability|" + actorId + "|" + craft, XjWorldHistoryCategory.Craft, "百艺", craft + "有成",
			actorName + "于修行之外显露" + craft + "天分，自此可在山门或族中执掌一门百艺。",
			2, false, familyId, familyName, sectId, sectName);
	}

	internal static void RecordAlchemyRecipe(Actor actor, string recipeName, int year, string source)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string recipe = SafeName(recipeName, "无名丹方");
		string sourceText = string.IsNullOrWhiteSpace(source) ? string.Empty : "经由" + source.Trim() + "，";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalAlchemyRecipe,
			"personal|alchemy-recipe|" + actorId + "|" + recipe, XjWorldHistoryCategory.Craft, "丹方", "悟得丹方",
			actorName + sourceText + "参悟丹理，悟得《" + recipe + "》。此方自此收入其丹道传承之中。",
			2, false, familyId, familyName, sectId, sectName);
	}

	private static string ResolveWeaponArtWeapon(string art)
	{
		if (ContainsAny(art, "刀")) return "刀";
		if (ContainsAny(art, "枪")) return "枪";
		if (ContainsAny(art, "弓")) return "弓";
		if (ContainsAny(art, "剑")) return "剑";
		return "器";
	}

	private static string ResolveWeaponArtRank(string art)
	{
		if (ContainsAny(art, "意")) return "意";
		if (ContainsAny(art, "元")) return "元";
		if (ContainsAny(art, "芒")) return "芒";
		if (ContainsAny(art, "气")) return "气";
		return "艺";
	}


	private static void RecordSectHighRealm(long actorId, string actorName, string realmId, int year, long sectId, string sectName)
	{
		if (sectId <= 0L || actorId <= 0L) return;
		string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
		int realmOrder = XjRealmHelper.GetOrder(normalizedRealm);
		if (realmOrder < XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) return;
		string realmName = XjRealmHelper.GetDisplayName(normalizedRealm);
		string resolvedSectName = ResolveSectName(sectId, sectName);
		RecordSect(sectId, resolvedSectName, year, XjThreeBookEventTypes.SectHighRealmEmerged,
			"sect|high-realm|" + sectId + "|" + actorId + "|" + normalizedRealm,
			SafeName(realmName, "高境"), "门中高手",
			resolvedSectName + "门中" + SafeName(actorName, "一名修士") + "晋入" + SafeName(realmName, "高境") + "，山门实力与声望由此更进一步。",
			realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.JinDan) ? 5 : 4,
			realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.JinDan),
			actorId, actorName);
	}

	private static void RecordPersonalRealm(in XjFamilyDomainEvent fact, string actorName, long familyId, string familyName, long sectId, string sectName)
	{
		string realmId = XjRealmHelper.NormalizeId(fact.RealmId);
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return; // 普通炼气突破不写史册。
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal) || string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)) return;
		string realmName = XjRealmHelper.GetDisplayName(realmId);
		string body;
		int importance;
		bool protect;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			body = "多年积累之后，" + actorName + "冲破瓶颈，筑成仙基。自此寿元增长，真正迈入修士之列。";
			importance = 2;
			protect = false;
		}
		else if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			body = XjChronology.FormatYear(fact.Year) + "，" + actorName + "紫府初开。自此神通可载，道途也迎来新的天地。";
			importance = 4;
			protect = true;
		}
		else
		{
			body = actorName + "苦修多年，终于踏入" + SafeName(realmName, "更高境界") + "。";
			importance = 2;
			protect = false;
		}
		RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalRealmBreakthrough,
			"personal|realm|" + fact.ActorId + "|" + realmId, XjWorldHistoryCategory.Cultivation,
			SafeName(realmName, "破境"), SafeName(realmName, "境界突破"), body, importance, protect, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
	}

	private static void RecordFamilyRealm(in XjFamilyDomainEvent fact, string familyName)
	{
		string realmId = XjRealmHelper.NormalizeId(fact.RealmId);
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal) || string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)) return;
		string actorName = SafeName(fact.ActorName, "族中后辈");
		string realmName = XjRealmHelper.GetDisplayName(realmId);
		int importance = string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal) ? 4 : 2;
		if (importance >= 4)
		{
			XjFamilyRealmAchievement achievement = XjFamilyRealmAchievementNarrative.Resolve(
				fact.FamilyStableId, fact.ActorId, actorName, realmId);
			RecordFamily(fact.FamilyStableId, familyName, fact.Year, XjThreeBookEventTypes.FamilyHighRealmEmerged,
				"family|realm|" + fact.FamilyStableId + "|" + fact.ActorId + "|" + realmId,
				SafeName(realmName, "破境"), XjFamilyRealmAchievementNarrative.BuildShortTitle(in achievement, realmName),
				familyName + "族人" + actorName + "晋入" + SafeName(realmName, "更高境界")
					+ XjFamilyRealmAchievementNarrative.BuildEnding(in achievement, realmName),
				importance, true, fact.ActorId, actorName);
			return;
		}

		RecordFamily(fact.FamilyStableId, familyName, fact.Year, XjThreeBookEventTypes.FamilyMemberAchievement,
			"family|realm|" + fact.FamilyStableId + "|" + fact.ActorId + "|" + realmId,
			SafeName(realmName, "破境"), "族中进境", familyName + "族人" + actorName + "晋入" + SafeName(realmName, "更高境界") + "，家门实力由此更进一步。",
			importance, false, fact.ActorId, actorName);
	}

	private static void RecordPersonalDongTian(in XjFamilyDomainEvent fact, string actorName, long familyId, string familyName, long sectId, string sectName)
	{
		string dongTian = SafeName(fact.Source, "无名洞天");
		string rewardType = fact.CaiQiFaName;
		string rewardSummary = fact.CaiQiFaSourcePlace;
		string body;
		if (ContainsAny(rewardType, "功法", "GongFa", "法门"))
			body = actorName + "入【" + dongTian + "】寻缘，归来时带回一卷残缺法门。此法虽不知来历，却被其收入自身修行之中。";
		else if (ContainsAny(rewardType, "灵物", "法宝", "FaBao", "LingWu"))
			body = actorName + "自【" + dongTian + "】归来，并带回" + SafeName(rewardSummary, "一件灵物") + "。其上灵机不凡，日后或将成为修途中的重要助力。";
		else if (!string.IsNullOrWhiteSpace(rewardSummary))
			body = actorName + "循机缘进入【" + dongTian + "】，数日后平安归来，并得" + rewardSummary.Trim() + "，修途因此多了一分变化。";
		else
			body = actorName + "曾入【" + dongTian + "】探寻，最终平安归来。此行虽未得外物，却也见识了一番天地机缘。";
		RecordPersonal(fact.ActorId, actorName, fact.Year, XjThreeBookEventTypes.PersonalDongTianJourney,
			"personal|dongtian|" + fact.ActorId + "|" + dongTian + "|" + fact.Year,
			XjWorldHistoryCategory.Opportunity, "洞天", "入洞寻缘", body, 2, false, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
	}

	internal static void RecordGuZunSelfDissolution(
		long actorId,
		string actorName,
		long familyId,
		string familyName,
		long sectId,
		string sectName,
		int year,
		string body)
	{
		string resolvedActorName = SafeName(actorName, "无名故尊");
		string resolvedFamilyName = familyId > 0L
			? SafeName(familyName, ResolveFamilyName(familyId))
			: string.Empty;
		string resolvedSectName = sectId > 0L
			? ResolveSectName(sectId, sectName)
			: string.Empty;
		string narrative = SafeName(body, resolvedActorName + "照见自身并非故尊本尊，遂主动散去。");
		string factKey = actorId + "|" + Math.Max(0, year);

		if (familyId > 0L)
		{
			RecordFamily(
				familyId, resolvedFamilyName, year,
				XjThreeBookEventTypes.FamilyGuZunDissolved,
				"family|guzun_self_dissolve|" + familyId + "|" + factKey,
				"故尊", "故尊辨真散去", narrative,
				5, true, actorId, resolvedActorName, sectId, resolvedSectName,
				result: XjHistoryResult.Death);
		}

		if (sectId > 0L)
		{
			RecordSect(
				sectId, resolvedSectName, year,
				XjThreeBookEventTypes.SectGuZunDissolved,
				"sect|guzun_self_dissolve|" + sectId + "|" + factKey,
				"故尊", "故尊辨真散去", narrative,
				5, true, actorId, resolvedActorName, familyId, resolvedFamilyName,
				result: XjHistoryResult.Death);
		}
	}

	private static void EnsureFamilyFounded(long familyId, string familyName, long actorId, string actorName, int year)
	{
		if (familyId <= 0L) return;
		RecordFamily(familyId, familyName, year, XjThreeBookEventTypes.FamilyFounded,
			"family|founded|" + familyId, "立族", "一脉初成",
			XjChronology.FormatYear(year) + "，" + SafeName(familyName, "此族") + "因族人聚居与传承相续，逐渐形成一脉家门。",
			3, true, actorId, actorName);
	}

	private static bool RecordPersonal(long actorId, string actorName, int year, string eventType, string sourceFactId, string category,
		string tag, string title, string body, int importance, bool isProtected, long familyId, string familyName, long sectId, string sectName,
		long relatedActorId = 0L, string relatedActorName = "", string result = XjHistoryResult.None)
	{
		return XjPersonalBiographyStore.Record(new XjThreeBookArchiveRecord
		{
			SourceFactId = sourceFactId,
			SubjectId = actorId,
			SubjectNameSnapshot = actorName,
			Year = Math.Max(0, year),
			EventType = eventType,
			Category = category,
			Tag = tag,
			Title = title,
			Body = body,
			Importance = importance,
			IsProtected = isProtected,
			Result = result,
			ActorId = actorId,
			ActorName = actorName,
			RelatedActorId = relatedActorId,
			RelatedActorName = relatedActorName,
			FamilyId = familyId,
			FamilyNameSnapshot = familyName,
			SectId = sectId,
			SectNameSnapshot = sectName
		});
	}

	private static bool RecordFamily(long familyId, string familyName, int year, string eventType, string sourceFactId,
		string tag, string title, string body, int importance, bool isProtected, long actorId = 0L, string actorName = "",
		long sectId = 0L, string sectName = "", string result = XjHistoryResult.None)
	{
		return XjFamilyChronicleBookStore.Record(new XjThreeBookArchiveRecord
		{
			SourceFactId = sourceFactId,
			SubjectId = familyId,
			SubjectNameSnapshot = familyName,
			Year = Math.Max(0, year),
			EventType = eventType,
			Category = XjWorldHistoryCategory.Family,
			Tag = tag,
			Title = title,
			Body = body,
			Importance = importance,
			IsProtected = isProtected,
			Result = result,
			ActorId = actorId,
			ActorName = actorName,
			FamilyId = familyId,
			FamilyNameSnapshot = familyName,
			SectId = sectId,
			SectNameSnapshot = sectName
		});
	}

	private static bool RecordSect(long sectId, string sectName, int year, string eventType, string sourceFactId,
		string tag, string title, string body, int importance, bool isProtected, long actorId = 0L, string actorName = "",
		long familyId = 0L, string familyName = "", long relatedSectId = 0L, string relatedSectName = "", string result = XjHistoryResult.None)
	{
		return XjSectChronicleStore.Record(new XjThreeBookArchiveRecord
		{
			SourceFactId = sourceFactId,
			SubjectId = sectId,
			SubjectNameSnapshot = sectName,
			Year = Math.Max(0, year),
			EventType = eventType,
			Category = XjWorldHistoryCategory.Sect,
			Tag = tag,
			Title = title,
			Body = body,
			Importance = importance,
			IsProtected = isProtected,
			Result = result,
			ActorId = actorId,
			ActorName = actorName,
			FamilyId = familyId,
			FamilyNameSnapshot = familyName,
			SectId = sectId,
			SectNameSnapshot = sectName,
			RelatedSectId = relatedSectId,
			RelatedSectNameSnapshot = relatedSectName
		});
	}

	private static void ResolveActorAffiliations(Actor actor, out long familyId, out string familyName, out long sectId, out string sectName)
	{
		familyId = 0L;
		familyName = string.Empty;
		sectId = 0L;
		sectName = string.Empty;
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		familyId = ResolveFamilyId(actorId, 0L);
		familyName = ResolveFamilyName(familyId);
		sectId = XjSectRepository.ResolveActorSectId(actor);
		sectName = ResolveSectName(sectId, string.Empty);
	}

	private static long ResolveFamilyId(long actorId, long supplied)
	{
		if (supplied > 0L) return supplied;
		return actorId > 0L && XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out long familyId) ? familyId : 0L;
	}

	private static long ResolveSectId(long actorId, long supplied)
	{
		if (supplied > 0L) return supplied;
		return actorId > 0L && XjScheduler.ResolveActor(actorId, out Actor actor) && actor?.data != null
			? XjSectRepository.ResolveActorSectId(actor)
			: 0L;
	}

	private static string ResolveFamilyName(long familyId)
	{
		return familyId > 0L ? SafeName(XjFamilyDisplayNameResolver.Resolve(familyId), "未名氏") : string.Empty;
	}

	private static string ResolveSectName(long sectId, string supplied)
	{
		if (!string.IsNullOrWhiteSpace(supplied)) return supplied.Trim();
		return sectId > 0L && XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord record) && record != null
			? SafeName(record.Name, "未名宗门")
			: string.Empty;
	}

	private static string SafeActorName(Actor actor)
	{
		return actor?.data == null ? "无名修士" : SafeName(XjStringHelper.ActorNameWithoutRealmSuffix(actor, actor.getName() ?? "无名修士"), "无名修士");
	}

	private static string SafeName(string value, string fallback)
	{
		return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
	}

	private static bool IsDongTianSource(string source)
	{
		return ContainsAny(source, "洞天", "秘境", "DongTian", "AdventureRealm");
	}

	private static string ResolveGongFaSourcePhrase(string source, bool obtained)
	{
		if (IsDongTianSource(source)) return obtained ? "入洞寻缘归来时获得" : "借洞天所得继续参悟，将";
		if (ContainsAny(source, "家族", "族中", "Family", "Borrow")) return obtained ? "承家族传法，得授" : "承家中前辈指点，将";
		if (ContainsAny(source, "宗门", "山门", "ZongMen", "Sect")) return obtained ? "由山门传法，得授" : "依山门旧藏参悟，将";
		if (ContainsAny(source, "统一参悟", "自行参悟", "Comprehension")) return obtained ? "从修持中自行悟得" : "经年参悟，将";
		return obtained ? "于修行途中得授" : "在原有法门上继续推演，将";
	}

	private static string ResolveGongFaGradeText(int grade)
	{
		return grade switch
		{
			1 => "一品",
			2 => "二品",
			3 => "三品",
			4 => "四品",
			5 => "五品",
			6 => "六品",
			7 => "七品",
			_ => Math.Max(1, grade) + "品"
		};
	}

	private static bool IsImportantArtifact(string artifactClass, string artifactName)
	{
		return ContainsAny(artifactClass, "灵宝", "法宝", "LingBao", "FaBao") || ContainsAny(artifactName, "灵宝", "法宝");
	}

	private static bool ContainsAny(string value, params string[] needles)
	{
		if (string.IsNullOrWhiteSpace(value) || needles == null) return false;
		for (int i = 0; i < needles.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(needles[i]) && value.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
		}
		return false;
	}
}

