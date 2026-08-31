using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Chronicle;

internal static partial class XjChronicleWriter
{
	internal static bool RecordRenDanRefined(Actor sourceActor, Actor targetActor, int timestamp, string gainedXianJi)
	{
		if (sourceActor?.data == null || targetActor?.data == null)
		{
			return false;
		}

		long sourceActorId = ((BaseSystemData)sourceActor.data).id;
		long targetActorId = ((BaseSystemData)targetActor.data).id;
		if (sourceActorId <= 0L || targetActorId <= 0L)
		{
			return false;
		}

		string sourceName = XjStringHelper.ActorNameWithoutRealmSuffix(sourceActor);
		string targetName = XjStringHelper.ActorNameWithoutRealmSuffix(targetActor);
		string xianJi = string.IsNullOrWhiteSpace(gainedXianJi) ? "神通" : gainedXianJi.Trim();
		string title = "人丹之劫";
		string body = sourceName + "早已暗中扶持" + targetName + "修行，并将这名筑基下修炼作人丹，施展续途妙法，残神化入" + xianJi + "，此事入谱为血仇。";
		bool recorded = false;

		if (XjFamilyReadModel.Shared.TryGetFamilyStableId(targetActorId, out long targetFamilyStableId) && targetFamilyStableId > 0L)
		{
			recorded |= RecordRenDanEvent(targetFamilyStableId, targetActorId, timestamp, title, body);
		}

		if (XjFamilyReadModel.Shared.TryGetFamilyStableId(sourceActorId, out long sourceFamilyStableId)
			&& sourceFamilyStableId > 0L
			&& sourceFamilyStableId != targetFamilyStableId)
		{
			recorded |= RecordRenDanEvent(sourceFamilyStableId, sourceActorId, timestamp, title, body);
		}

		XjFamilyVendettaRegistry.RecordRenDanVendetta(
			targetFamilyStableId,
			targetActorId,
			targetName,
			sourceFamilyStableId,
			sourceActorId,
			sourceName,
			xianJi,
			timestamp,
			body);

		return recorded;
	}

	internal static bool RecordRenDanRefinedFromSnapshot(
		long sourceActorId,
		string sourceActorName,
		long sourceFamilyStableId,
		long targetActorId,
		string targetActorName,
		long targetFamilyStableId,
		int timestamp,
		string gainedXianJi)
	{
		if (sourceActorId <= 0L || targetActorId <= 0L)
		{
			return false;
		}

		string sourceName = XjStringHelper.DisplayNameWithoutRealmSuffix(sourceActorName);
		string targetName = XjStringHelper.DisplayNameWithoutRealmSuffix(targetActorName);
		string xianJi = string.IsNullOrWhiteSpace(gainedXianJi) ? "神通" : gainedXianJi.Trim();
		string title = "人丹之劫";
		string body = sourceName + "早已暗中扶持" + targetName + "修行，并将这名筑基下修炼作人丹，施展续途妙法，残神化入" + xianJi + "，此事入谱为血仇。";
		bool recorded = false;

		if (targetFamilyStableId > 0L)
		{
			recorded |= RecordRenDanEvent(targetFamilyStableId, targetActorId, timestamp, title, body);
		}

		if (sourceFamilyStableId > 0L && sourceFamilyStableId != targetFamilyStableId)
		{
			recorded |= RecordRenDanEvent(sourceFamilyStableId, sourceActorId, timestamp, title, body);
		}

		XjFamilyVendettaRegistry.RecordRenDanVendetta(
			targetFamilyStableId,
			targetActorId,
			targetName,
			sourceFamilyStableId,
			sourceActorId,
			sourceName,
			xianJi,
			timestamp,
			body);

		return recorded;
	}

	internal static bool RecordActorDied(Actor actor, int timestamp)
	{
		if (!ShouldRecordActor(actor))
		{
			return false;
		}

		string actorName = actor?.getName() ?? "族人";
		string realm = GetActorRealmSnapshot(actor);
		string title = realm.Contains("金丹") ? "金丹归寂" : realm.Contains("紫府") ? "紫府星沉" : realm.Contains("筑基") ? "筑基陨落" : realm.Contains("胎息") ? "终困胎息" : "修士入谱";
		string body = title == "金丹归寂"
			? actorName + "丹成一世，威名远播，今归寂。族谱以朱笔记之：一丹既失，百年余荫仍在。"
			: title == "紫府星沉"
				? actorName + "紫府既开，曾为族中撑起半片天命。其身故之后，族中议事堂灯火彻夜不灭。"
				: title == "筑基陨落"
					? actorName + "筑基有成，护族多年，今归尘土。"
					: title == "终困胎息"
						? actorName + "一生修至胎息圆满，却始终未得先天之气。其死后，族谱只留八字：志在炼气，命止门前。"
						: actorName + "身故。其名入族谱，不作凡名散去。";
		return RecordRich(actor, "Death", timestamp, title, body, title == "金丹归寂" ? 4 : 2, title == "金丹归寂", source: "death.actor");
	}

	internal static bool RecordDeathSnapshot(XjDeathSnapshot snapshot)
	{
		XjThreeBookWriter.RecordDeath(snapshot);
		if (!snapshot.Found || snapshot.FamilyStableId <= 0L || snapshot.ActorId <= 0L)
		{
			return false;
		}

		if (!XjRealmHelper.ShouldRecord(snapshot.RealmId))
		{
			return false;
		}

		BuildDeathChronicle(snapshot, out string title, out string body, out bool relatedToHighGradeGongFa);
		bool isHighRealmDeath = XjHighRealmIdentity.IsHighRealm(snapshot.RealmId);
		XjChronicleEvent chronicleEvent = new XjChronicleEvent(
			true,
			snapshot.FamilyStableId,
			snapshot.ActorId,
			XjChronicleEventTypes.ActorDied,
			snapshot.Year,
			title,
			body,
			isHighRealmDeath ? 4 : 2,
			isHighRealmDeath,
			false,
			relatedToHighGradeGongFa,
			"Ok");

		string eventKey = snapshot.FamilyStableId
			+ "|"
			+ snapshot.ActorId
			+ "|"
			+ XjChronicleEventTypes.ActorDied
			+ "|"
			+ snapshot.Year;
		return XjFamilyChronicleMemory.Shared.Append(chronicleEvent, eventKey);
	}

	internal static bool RecordFamilyMemberConfirmed(Actor actor, int timestamp)
	{
		string actorName = actor?.getName() ?? "族人";
		return RecordRich(actor, XjChronicleEventTypes.FamilyMemberConfirmed, timestamp, "族人归籍", actorName + "确认入籍");
	}

	private static void BuildDeathChronicle(XjDeathSnapshot snapshot, out string title, out string body, out bool relatedToHighGradeGongFa)
	{
		string name = string.IsNullOrWhiteSpace(snapshot.Name) ? "无名族人" : snapshot.Name;
		relatedToHighGradeGongFa = snapshot.GongFaGrade >= 4 || !string.IsNullOrWhiteSpace(snapshot.QiuJinFaName);

		string realm = string.IsNullOrWhiteSpace(snapshot.RealmId) ? string.Empty : XjRealmHelper.GetDisplayName(snapshot.RealmId);
		string gongFaPart = string.IsNullOrWhiteSpace(snapshot.GongFaName)
			? string.Empty
			: "，曾修" + snapshot.GongFaName + "（" + snapshot.GongFaGrade + "品）";
		string qiuJinFaPart = string.IsNullOrWhiteSpace(snapshot.QiuJinFaName)
			? string.Empty
			: "，悟得求金法" + snapshot.QiuJinFaName;
		string faBaoPart = string.IsNullOrWhiteSpace(snapshot.FaBao)
			? string.Empty
			: "，持法宝" + snapshot.FaBao;
		string jinXingPart = string.IsNullOrWhiteSpace(snapshot.JinXing) ? string.Empty : "，金性" + snapshot.JinXing;
		string guoWeiPart = string.IsNullOrWhiteSpace(snapshot.GuoWei) ? string.Empty : "，果位" + snapshot.GuoWei;

		if (!string.IsNullOrWhiteSpace(realm))
		{
			title = realm + "陨落";
			body = name + "（" + realm + "）身殒道消" + gongFaPart + qiuJinFaPart + faBaoPart + jinXingPart + guoWeiPart;
		}
		else
		{
			title = "魂归天地";
			body = name + "身殒道消" + gongFaPart + qiuJinFaPart + faBaoPart + jinXingPart + guoWeiPart;
		}
	}

	private static bool RecordRenDanEvent(long familyStableId, long actorId, int timestamp, string title, string body)
	{
		XjChronicleEvent chronicleEvent = new XjChronicleEvent(
			true,
			familyStableId,
			actorId,
			XjChronicleEventTypes.RenDanRefined,
			timestamp,
			title,
			body,
			3,
			false,
			false,
			true,
			"Ok");

		string eventKey = familyStableId
			+ "|"
			+ actorId
			+ "|"
			+ XjChronicleEventTypes.RenDanRefined
			+ "|"
			+ timestamp
			+ "|"
			+ body;
		return XjFamilyChronicleMemory.Shared.Append(chronicleEvent, eventKey);
	}
}
