using System;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.History.Books;

/// <summary>
/// 释修独立史册入口。这里只记录已经提交成功的入释、度化、占位、斗争和释土扩张事实，
/// 不从天下公告复制，也不在 UI 打开时重新生成。
/// </summary>
internal static partial class XjThreeBookWriter
{
	internal static void EnsureShiBiographyBaseline(Actor actor, int year)
	{
		if (actor?.data == null || !actor.isAlive()) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !XjCultivationPathRules.IsShi(actor)) return;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiBiographyBackfillVersion, out int version);
		if (version >= 1) return;

		bool hasShiHistory = XjPersonalBiographyStore.CountEvents(actorId, XjThreeBookEventTypes.PersonalShiEntered, 1) > 0
			|| XjPersonalBiographyStore.CountEvents(actorId, XjThreeBookEventTypes.PersonalShiConversion, 1) > 0
			|| XjPersonalBiographyStore.CountEvents(actorId, XjThreeBookEventTypes.PersonalShiRealmChanged, 1) > 0
			|| XjPersonalBiographyStore.CountEvents(actorId, XjThreeBookEventTypes.PersonalShiPosition, 1) > 0
			|| XjPersonalBiographyStore.CountEvents(actorId, XjThreeBookEventTypes.PersonalShiReincarnation, 1) > 0
			|| XjPersonalBiographyStore.CountEvents(actorId, XjThreeBookEventTypes.PersonalShiWorldHonored, 1) > 0;

		if (!hasShiHistory && XuanJianVNext.Systems.Shi.XjShiState.TryBuildSnapshot(actor, out XuanJianVNext.Data.Shi.XjShiSnapshot snapshot))
		{
			ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
			string actorName = SafeActorName(actor);
			string tradition = XjShiCatalog.GetTraditionDisplay(snapshot.Tradition);
			string lineage = XjShiCatalog.GetLineageDisplay(ReadShiString(actor, XjActorDataKeys.ShiLineageId));
			string realm = XjShiCatalog.GetRealmDisplay(snapshot.Realm);
			bool ancient = string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
			string detail = string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
				? "，承" + XjShiCatalog.GetSeatDisplay(snapshot.SeatId)
				: !ancient && XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe)
					? "，今为第" + Math.Max(1, snapshot.CurrentLife).ToString(CultureInfo.InvariantCulture) + "世"
					: ancient && XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)
						? "，以内证金地、应身自持"
						: string.Empty;
			int eventYear = snapshot.RealmEnteredYear > 0 ? snapshot.RealmEnteredYear : Math.Max(1, year);
			string body = "旧档补录：" + actorName + "已入" + tradition + "，循" + lineage + "法脉修持，现证" + realm + detail
				+ "。此条只补足旧档缺失的释门生平入口，不臆造此前未被记录的具体经过。";
			RecordPersonal(actorId, actorName, eventYear, XjThreeBookEventTypes.PersonalShiBaseline,
				"personal|shi-baseline|" + actorId, XjWorldHistoryCategory.Cultivation,
				"释门", "释门旧档补录", body, Math.Max(2, XjShiCatalog.GetRank(snapshot.Realm)), false,
				familyId, familyName, sectId, sectName, result: XjHistoryResult.None);
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBiographyBackfillVersion, 1);
	}

	private static string ReadShiString(Actor actor, string key)
	{
		return XjActorAccessor.TryGetString(actor, key, out string value) ? value ?? string.Empty : string.Empty;
	}

	internal static void RecordShiEntered(Actor actor, int year, string tradition, string lineageId,
		string entrySource, long masterId, string masterName)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string traditionName = XjShiCatalog.GetTraditionDisplay(tradition);
		string lineageName = XjShiCatalog.GetLineageDisplay(lineageId);
		string master = masterId > 0L ? SafeName(masterName, "一位释修") : string.Empty;
		string body = masterId > 0L
			? actorName + "受" + master + "接引，舍去旧学，依" + traditionName + "·" + lineageName + "法脉入释，初为僧侣。"
			: actorName + "自悟释门因缘，依" + traditionName + "·" + lineageName + "法脉入释，初为僧侣。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiEntered,
			"personal|shi-entered|" + actorId, XjWorldHistoryCategory.Cultivation,
			"释门", "入释修持", body, 2, false, familyId, familyName, sectId, sectName,
			masterId, master, XjHistoryResult.Success);
	}

	internal static void RecordShiConversion(Actor actor, int year, string sourceDisplay,
		string targetRealm, string lineageId)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string source = SafeName(sourceDisplay, "旧途高修");
		string realm = XjShiCatalog.GetRealmDisplay(targetRealm);
		string lineage = XjShiCatalog.GetLineageDisplay(lineageId);
		string body = actorName + "舍" + source + "旧果投释，将原有修为、命数与位格折入释门，转为"
			+ realm + "，自此依" + lineage + "法脉修持。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiConversion,
			"personal|shi-conversion|" + actorId + "|" + year, XjWorldHistoryCategory.Cultivation,
			"投释", "舍旧途入释", body, 4, true, familyId, familyName, sectId, sectName,
			result: XjHistoryResult.Transfer);
	}

	internal static void RecordShiRealmChanged(Actor actor, int year, string previousRealm,
		string nextRealm, bool manualOverride)
	{
		if (actor?.data == null || string.Equals(previousRealm, nextRealm, StringComparison.Ordinal)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string previous = XjShiCatalog.GetRealmDisplay(previousRealm);
		string next = XjShiCatalog.GetRealmDisplay(nextRealm);
		bool worldHonored = string.Equals(nextRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal);
		string eventType = worldHonored ? XjThreeBookEventTypes.PersonalShiWorldHonored
			: XjThreeBookEventTypes.PersonalShiRealmChanged;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiSeatId, out string seatId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCurrentLife, out int currentLife);
		bool ancient = string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
		string title;
		string body;
		if (string.Equals(nextRealm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal))
		{
			title = ancient ? "慧火自明" : "发慧映土";
			body = ancient
				? actorName + "持身清静，法意由心而发；不假释土、不借他位，一念照见本性，自" + SafeName(previous, "僧侣") + "登发慧座，证法师。"
				: actorName + "法慧既生，真灵为释土所照，旃檀真土遥有感应；自" + SafeName(previous, "僧侣") + "登发慧座，证法师。";
		}
		else if (string.Equals(nextRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			title = "莲座不退";
			body = actorName + "命数上系摩诃位下，承" + XjShiCatalog.GetSeatDisplay(seatId)
				+ "，形念从此有所归依；不退转地初成，自" + SafeName(previous, "法师") + "证怜愍。";
		}
		else if (string.Equals(nextRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			title = ancient ? "金地应身" : "摩诃不退";
			body = ancient
				? actorName + "清静求妙，不借诸位，以己身证金地、立应身；自" + SafeName(previous, "前境") + "证摩诃。"
				: actorName + "宿世格位归一，真灵高举，位、形、念三不退；自" + SafeName(previous, "前境")
					+ "证第" + Math.Max(1, currentLife) + "世摩诃。";
		}
		else if (string.Equals(nextRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal))
		{
			title = ancient ? "应身圆成" : "法相应土";
			body = ancient
				? actorName + "金地为基，应身照世，本性与法意俱显；自" + SafeName(previous, "摩诃") + "证成法相。"
				: actorName + "金地稳应身，本愿合真灵，诸相由虚转实；自" + SafeName(previous, "摩诃") + "证成法相。";
		}
		else if (worldHonored)
		{
			title = ancient ? "古释世尊" : "今释世尊";
			body = ancient
				? actorName + "金地、应身、本性俱圆，所证自成一统，世尊位成。"
				: actorName + "本愿、应身、真灵俱圆，法相高举真土，世尊名位显世。";
		}
		else
		{
			title = "修至" + SafeName(next, "新境");
			body = actorName + "修持渐深，由" + SafeName(previous, "前境") + "进至" + SafeName(next, "新境") + "。";
		}
		RecordPersonal(actorId, actorName, year, eventType,
			"personal|shi-realm|" + actorId + "|" + SafeName(nextRealm, "unknown") + "|" + year,
			XjWorldHistoryCategory.Cultivation, worldHonored ? "世尊" : "释修境界", title, body,
			worldHonored ? 7 : Math.Max(2, XjShiCatalog.GetRank(nextRealm)), worldHonored,
			familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
	}

	internal static void RecordShiDharmaFormStage(Actor actor, int year, string previousStage,
		string nextStage, bool setback)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(nextStage)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string next = XjShiCatalog.GetDharmaFormStageDisplay(nextStage);
		string previous = XjShiCatalog.GetDharmaFormStageDisplay(previousStage);
		string title = setback ? "法相退转" : "法相进境";
		string body = setback
			? actorName + "真灵与应身失衡，法相由" + SafeName(previous, "前层") + "退回" + SafeName(next, "本愿") + "。"
			: actorName + "调和本愿、应身与真灵，法相层次由" + SafeName(previous, "前层") + "进至" + SafeName(next, "新层") + "。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiDharmaFormStage,
			"personal|shi-dharma-stage|" + actorId + "|" + SafeName(nextStage, "unknown") + "|" + year,
			XjWorldHistoryCategory.Cultivation, "法相", title, body, setback ? 4 : 5, setback,
			familyId, familyName, sectId, sectName,
			result: setback ? XjHistoryResult.Failure : XjHistoryResult.Success);
	}

	internal static void RecordShiJinDiObtained(Actor actor, int year, string domainId, string domainName)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string name = SafeName(domainName, "一方金地");
		string body = actorName + "与" + name + "相应，成为此地庙主；金地自此记其权属，并参与后续高位修持。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiJinDiObtained,
			"personal|shi-jindi|" + actorId + "|" + SafeName(domainId, "unknown"),
			XjWorldHistoryCategory.Cultivation, "金地", "得主金地", body, 5, true,
			familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
	}

	internal static void RecordShiReincarnation(Actor actor, int year, bool trueSpiritReturn,
		string anchorName, string sourceActorName)
	{
		_ = sourceActorName; // 保留调用签名兼容旧入口；转世不再视作另一人物承接前尘。
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCurrentLife, out int currentLife);
		string body = trueSpiritReturn
			? actorName + "真灵循" + SafeName(anchorName, "所系承载地") + "归返，于释土中重塑肉身，前世法脉与位次随真灵续接。"
			: actorName + "舍去前身而入第" + Math.Max(1, currentLife).ToString(CultureInfo.InvariantCulture)
				+ "世；仍为同一真灵，姓名、尊号、法号与法脉承前世不改，待重新聚合前世位次。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiReincarnation,
			"personal|shi-reincarnation|" + actorId + "|" + year, XjWorldHistoryCategory.Cultivation,
			"轮回", trueSpiritReturn ? "承载归返" : "转世续法", body, 5, true,
			familyId, familyName, sectId, sectName, result: XjHistoryResult.Transfer);
	}

	internal static void RecordShiTrueSpiritResult(Actor actor, int year, bool returned,
		bool annihilated, string anchorName)
	{
		if (actor?.data == null || (!returned && !annihilated)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string eventType = annihilated ? XjThreeBookEventTypes.PersonalShiTrueSpiritAnnihilated
			: XjThreeBookEventTypes.PersonalShiTrueSpiritReturn;
		string title = annihilated ? "真灵俱灭" : "真灵归返";
		string body = annihilated
			? actorName + "真灵俱灭，轮回与承载至此断绝。"
			: actorName + "肉身虽毁，真灵仍循" + SafeName(anchorName, "所系金地") + "归返，等待重塑。";
		RecordPersonal(actorId, actorName, year, eventType,
			"personal|shi-true-spirit|" + actorId + "|" + year + "|" + (annihilated ? "end" : "return"),
			XjWorldHistoryCategory.Cultivation, "真灵", title, body, annihilated ? 7 : 5, true,
			familyId, familyName, sectId, sectName,
			result: annihilated ? XjHistoryResult.Failure : XjHistoryResult.Transfer);
	}
	internal static void RecordShiAncientDuhua(Actor teacher, Actor target, int year, int amount, float selfAward)
	{
		if (teacher?.data == null || target?.data == null || year <= 0 || amount <= 0) return;
		long teacherId = ((BaseSystemData)teacher.data).id;
		long targetId = ((BaseSystemData)target.data).id;
		if (teacherId <= 0L || targetId <= 0L) return;
		ResolveActorAffiliations(teacher, out long familyId, out string familyName, out long sectId, out string sectName);
		string teacherName = SafeActorName(teacher);
		string targetName = SafeActorName(target);
		string selfText = selfAward > 0.0001f
			? "；其愿行有应，自身释修命数亦添" + Math.Floor(selfAward).ToString(CultureInfo.InvariantCulture)
			: "；其自身命数修证已臻上限";
		string body = teacherName + "行过尘世，偶见" + targetName + "命中尚有一线可转，遂垂清静意照其命痕。"
			+ "此照不立门墙、不摄释土，亦不以众生性命为资粮；" + targetName + "后天命数添"
			+ amount.ToString(CultureInfo.InvariantCulture) + selfText + "。一念既毕，缘尽即散，各归其途。";
		RecordPersonal(teacherId, teacherName, year, XjThreeBookEventTypes.PersonalShiAncientDuhua,
			"personal|shi-ancient-duhua|" + teacherId + "|" + targetId + "|" + year,
			XjWorldHistoryCategory.Cultivation, "古释点命", "清静照命", body, 3, false,
			familyId, familyName, sectId, sectName, targetId, targetName, XjHistoryResult.Success);
	}

	internal static void RecordShiReturnToVoid(Actor actor, int year)
	{
		if (actor?.data == null || year <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string body = actorName + "身寿已尽。古释不循释土转世，所修证至此证毕归空。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiReturnToVoid,
			"personal|shi-return-to-void|" + actorId + "|" + year,
			XjWorldHistoryCategory.Cultivation, "古释", "证毕归空", body, 6, true,
			familyId, familyName, sectId, sectName, result: XjHistoryResult.Death);
	}

	internal static void RecordShiDuhuaKill(Actor actor, long targetId, string targetName, int year)
	{
		if (actor?.data == null || targetId <= 0L || year <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string victim = SafeName(targetName, "一名凡人");
		string body = actorName + "亲手杀死" + victim + "，以释门所谓‘度化’收摄一分众生意向；死者并未转入释修。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiPreaching,
			"personal|shi-duhua-kill|" + actorId + "|" + targetId + "|" + year,
			XjWorldHistoryCategory.Cultivation, "释修度化", "度化众生", body,
			2, false, familyId, familyName, sectId, sectName,
			targetId, victim, XjHistoryResult.Success);
	}

	internal static void RecordShiDuhuaBatch(Actor actor, int year, int count,
		string firstTargetName, string lastTargetName)
	{
		if (actor?.data == null || year <= 0 || count <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string first = SafeName(firstTargetName, "凡人");
		string last = SafeName(lastTargetName, first);
		string range = count == 1 ? first
			: string.Equals(first, last, StringComparison.Ordinal) ? first + "等人"
			: first + "、" + last + "等人";
		string body = actorName + "于本年度依释门度化之法杀死" + count.ToString(CultureInfo.InvariantCulture)
			+ "名非修士，其中有" + range + "；众生意向归入其命数与所系释土，死者不会因此成为释修。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiPreaching,
			"personal|shi-duhua-batch|" + actorId + "|" + year,
			XjWorldHistoryCategory.Cultivation, "释修度化", "岁中度化", body,
			count >= 10 ? 3 : 2, false, familyId, familyName, sectId, sectName,
			result: XjHistoryResult.Success);
	}

	internal static void RecordShiPreaching(Actor teacher, Actor student, int year, bool important)
	{
		if (teacher?.data == null || student?.data == null) return;
		long teacherId = ((BaseSystemData)teacher.data).id;
		long studentId = ((BaseSystemData)student.data).id;
		if (teacherId <= 0L || studentId <= 0L) return;
		ResolveActorAffiliations(teacher, out long familyId, out string familyName, out long sectId, out string sectName);
		string teacherName = SafeActorName(teacher);
		string studentName = SafeActorName(student);
		string body = teacherName + "向" + studentName + "宣说法义，使其脱离旧途，正式入释。此举既续法脉，也为所依释土增添一分承载。";
		int importance = important ? 3 : 2;
		RecordPersonal(teacherId, teacherName, year, XjThreeBookEventTypes.PersonalShiPreaching,
			"personal|shi-preaching|" + teacherId + "|" + studentId,
			XjWorldHistoryCategory.Cultivation, "释修", "度化传法", body,
			importance, false, familyId, familyName, sectId, sectName,
			studentId, studentName, XjHistoryResult.Success);
		RecordShiAffiliationMilestone(teacherId, teacherName, familyId, familyName, sectId, sectName, year,
			"preaching|" + studentId, "传法", "度化一人", body, importance, false);
	}

	internal static void RecordShiPositionAttained(Actor actor, int year, string realm, string seatId,
		bool selfProvedJinDi, bool becameLiangLi)
	{
		if (actor?.data == null) return;
		_ = becameLiangLi; // 旧签名兼容；当前模型不再记录主持／量力事件。
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string title;
		string body;
		string key;
		int importance;
		bool protect;
		if (selfProvedJinDi)
		{
			title = "自证金地";
			body = actorName + "以自身修持借来格位，证出一方金地，自此可承载摩诃与怜愍之位。";
			key = "jindi";
			importance = 5;
			protect = true;
		}
		else if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			title = "证得摩诃";
			body = actorName + "证得摩诃，位、形、念由此稳固，可接引怜愍并承托一脉释修。";
			key = "mohe";
			importance = 5;
			protect = true;
		}
		else if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			string seatName = XjShiCatalog.GetSeatDisplay(seatId);
			title = "承" + SafeName(seatName, "怜愍") + "位";
			body = actorName + "得摩诃接引，承得" + SafeName(seatName, "怜愍") + "位，开始借释土之力聚拢性命。";
			key = "lianmin|" + SafeName(seatId, "unknown");
			importance = string.Equals(seatId, XjShiSeatIds.JinLian, StringComparison.Ordinal) ? 4 : 3;
			protect = string.Equals(seatId, XjShiSeatIds.JinLian, StringComparison.Ordinal);
		}
		else return;
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiPosition,
			"personal|shi-position|" + actorId + "|" + key,
			XjWorldHistoryCategory.Cultivation, "释修位次", title, body,
			importance, protect, familyId, familyName, sectId, sectName,
			result: XjHistoryResult.Success);
		RecordShiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName, year,
			"position|" + key, "释修位次", title, body, importance, protect);
	}

	internal static void RecordShiFateDirectAttainment(Actor actor, int year, int currentLife, bool dharmaForm)
	{
		if (actor?.data == null || year <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		int life = Math.Clamp(currentLife, 1, 9);
		string title = dharmaForm ? "宿世直证法相" : "宿世直显摩诃";
		string body = dharmaForm
			? actorName + "命数极盛，九世因缘于一念之间俱现，又与金地相应，不历现世九番轮回而直证法相。"
			: actorName + "命数骤然昭显，宿世格位提前归一，不循寻常次第而直成第"
				+ life.ToString(CultureInfo.InvariantCulture) + "世摩诃。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiPosition,
			"personal|shi-fate-direct|" + actorId + "|" + (dharmaForm ? "dharma" : "mohe") + "|" + life,
			XjWorldHistoryCategory.Cultivation, "释修命数", title, body, dharmaForm ? 7 : 6, true,
			familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordShiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName, year,
			"fate-direct|" + (dharmaForm ? "dharma" : "mohe") + "|" + life,
			"释修命数", title, body, dharmaForm ? 7 : 6, true);
	}

	internal static void RecordShiHighRealmVictory(Actor killer, long victimId, string victimName,
		string victimRealm, int year)
	{
		if (killer?.data == null) return;
		long killerId = ((BaseSystemData)killer.data).id;
		if (killerId <= 0L) return;
		ResolveActorAffiliations(killer, out long familyId, out string familyName, out long sectId, out string sectName);
		string killerName = SafeActorName(killer);
		string resolvedVictim = SafeName(victimName, "异脉高修");
		string realmName = XjShiCatalog.GetRealmDisplay(victimRealm);
		string body = killerName + "与" + resolvedVictim + "交锋并将其击败，夺得一分命数，所依释土也因此扩张。";
		int importance = string.Equals(victimRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal) ? 6
			: string.Equals(victimRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal) ? 5 : 4;
		RecordPersonal(killerId, killerName, year, XjThreeBookEventTypes.PersonalShiHighRealmVictory,
			"personal|shi-victory|" + killerId + "|" + victimId,
			XjWorldHistoryCategory.Vendetta, "释修斗争", "击败" + SafeName(realmName, "异脉高修"), body,
			importance, importance >= 5, familyId, familyName, sectId, sectName,
			victimId, resolvedVictim, XjHistoryResult.Success);
		RecordShiAffiliationMilestone(killerId, killerName, familyId, familyName, sectId, sectName, year,
			"victory|" + victimId, "释修斗争", "胜异脉高修", body, importance, importance >= 5);
	}

	internal static void RecordShiJinDiAbsorbed(Actor actor, string absorbedDomainId, int year)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string domainKey = SafeName(absorbedDomainId, "unknown");
		string body = actorName + "使所系承载地聚合一处无主金地，将其残存格位与承载纳入其中。";
		RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShiJinDiAbsorbed,
			"personal|shi-absorb-jindi|" + actorId + "|" + domainKey,
			XjWorldHistoryCategory.Cultivation, "释土", "吞并金地", body,
			5, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
		RecordShiAffiliationMilestone(actorId, actorName, familyId, familyName, sectId, sectName, year,
			"absorb-jindi|" + domainKey, "释土", "吞并金地", body, 5, true);
	}

	private static void RecordShiAffiliationMilestone(long actorId, string actorName,
		long familyId, string familyName, long sectId, string sectName, int year,
		string factSuffix, string tag, string title, string body, int importance, bool protect)
	{
		if (familyId > 0L)
		{
			RecordFamily(familyId, familyName, year,
				factSuffix.StartsWith("position|", StringComparison.Ordinal)
					? XjThreeBookEventTypes.FamilyShiAchievement : XjThreeBookEventTypes.FamilyShiExpansion,
				"family|shi|" + familyId + "|" + actorId + "|" + factSuffix,
				tag, title, body, importance, protect, actorId, actorName, sectId, sectName,
				result: XjHistoryResult.Success);
		}
		if (sectId > 0L)
		{
			RecordSect(sectId, sectName, year,
				factSuffix.StartsWith("position|", StringComparison.Ordinal)
					? XjThreeBookEventTypes.SectShiAchievement : XjThreeBookEventTypes.SectShiExpansion,
				"sect|shi|" + sectId + "|" + actorId + "|" + factSuffix,
				tag, title, body, importance, protect, actorId, actorName, familyId, familyName,
				result: XjHistoryResult.Success);
		}
	}
}
