using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History.Books;
using XjRealmHelper = XuanJianVNext.Data.Rules.XjRealmHelper;

namespace XuanJianVNext.Systems.Chronicle;

internal static partial class XjChronicleWriter
{
	internal static bool RecordDomainEvent(
		in XjFamilyDomainEvent domainEvent,
		string eventType,
		string eventKey,
		string title,
		string body,
		int importance,
		bool relatedToFamilyWarehouse,
		bool relatedToHighGradeGongFa)
	{
		if (!domainEvent.Found
			|| domainEvent.FamilyStableId <= 0L
			|| domainEvent.ActorId <= 0L
			|| string.IsNullOrWhiteSpace(eventType))
		{
			return false;
		}

		XjChronicleEvent chronicleEvent = new XjChronicleEvent(
			true,
			domainEvent.FamilyStableId,
			domainEvent.ActorId,
			eventType,
			domainEvent.Year,
			title,
			body,
			importance < 1 ? 1 : importance,
			importance >= 5,
			relatedToFamilyWarehouse,
			relatedToHighGradeGongFa,
			"Ok",
			string.IsNullOrWhiteSpace(domainEvent.Source) ? "domain_event" : domainEvent.Source,
			domainEvent.RealmId);

		return XjFamilyChronicleMemory.Shared.Append(chronicleEvent, eventKey);
	}

	internal static bool Record(
		Actor actor,
		string eventType,
		int timestamp,
		string summary,
		bool relatedToFamilyWarehouse,
		bool relatedToHighGradeGongFa)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(eventType))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		if (!ShouldRecordActor(actor))
		{
			return false;
		}

		// 同 RecordRich：若读模型两级回退均失败，最后尝试 XjFamilyIdentityIndex
		if (!XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out long familyStableId) || familyStableId <= 0L)
		{
			if (!XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord record)
				|| !record.Found
				|| record.RootActorId <= 0L)
			{
				return false;
			}

			familyStableId = record.RootActorId;
		}

		XjChronicleEvent chronicleEvent = new XjChronicleEvent(
			true,
			familyStableId,
			actorId,
			eventType,
			timestamp,
			summary,
			string.Empty,
			1,
			false,
			relatedToFamilyWarehouse,
			relatedToHighGradeGongFa,
			"Ok",
			"legacy_record",
			GetActorRealmSnapshot(actor));

		return XjFamilyChronicleMemory.Shared.Append(chronicleEvent);
	}

	private static bool RecordRich(
		Actor actor,
		string eventType,
		int timestamp,
		string title,
		string body,
		int importance = 1,
		bool isProtected = false,
		bool relatedToFamilyWarehouse = false,
		bool relatedToHighGradeGongFa = false,
		string source = "")
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(eventType))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		// 优先走家族读模型（memberIndex + ledger 两级回退），
		// 若仍失败则尝试 XjFamilyIdentityIndex 作为最后防线。
		// 读档后 memberIndex 尚未重建、ledger 又缺少该角色时，
		// XjFamilyIdentityIndex 仍可能持有旧的家族归属记录，
		// 避免纪事事件被静默丢弃。
		if (!XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out long familyStableId) || familyStableId <= 0L)
		{
			if (!XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord record)
				|| !record.Found
				|| record.RootActorId <= 0L)
			{
				return false;
			}

			familyStableId = record.RootActorId;
		}

		XjChronicleEvent chronicleEvent = new XjChronicleEvent(
			true,
			familyStableId,
			actorId,
			eventType,
			timestamp,
			title ?? string.Empty,
			body ?? string.Empty,
			importance < 1 ? 1 : importance,
			isProtected,
			relatedToFamilyWarehouse,
			relatedToHighGradeGongFa,
			"Ok",
			string.IsNullOrWhiteSpace(source) ? eventType : source,
			GetActorRealmSnapshot(actor));

		return XjFamilyChronicleMemory.Shared.Append(chronicleEvent);
	}

	private static bool ShouldRecordActor(Actor actor)
	{
		return XjRealmHelper.ShouldRecord(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForWriter));
	}

	private static string GetActorRealmSnapshot(Actor actor)
	{
		return XjRealmHelper.GetTraitSnapshotForWriter(actor);
	}

	internal static bool RecordAlchemyHighMaterialGain(Actor actor, int year, string materialName, int amount)
	{
		// 药材采集属于日常资源流转，不写入史册。
		return false;
	}

	internal static bool RecordAlchemyRecipe(Actor actor, int year, string recipeName, string source)
	{
		XjThreeBookWriter.RecordAlchemyRecipe(actor, recipeName, year, source);
		return false;
	}

	internal static bool RecordAlchemyResult(Actor actor, int year, string pillName, int quantity, bool success, bool majorAccident)
	{
		string name = string.IsNullOrWhiteSpace(pillName) ? "丹药" : pillName.Trim();
		if (!success || majorAccident || name.IndexOf("延寿丹", StringComparison.Ordinal) < 0)
		{
			return false;
		}
		string crafter = actor?.getName() ?? "族人";
		return RecordRich(actor, "AlchemySucceeded:" + name, year, "延寿丹成",
			crafter + "炼成《" + name + "》" + Math.Max(0, quantity) + "枚，已收入药库。",
			importance: 4, isProtected: true, relatedToFamilyWarehouse: true, source: "alchemy.succeeded.lifespan");
	}


	internal static bool RecordLingWuAppeared(in XjDeathSnapshot snapshot, XjLingWuDef definition)
	{
		if (!snapshot.Found
			|| snapshot.FamilyStableId <= 0L
			|| snapshot.ActorId <= 0L
			|| definition == null
			|| string.IsNullOrWhiteSpace(definition.Id))
		{
			return false;
		}

		string actorName = string.IsNullOrWhiteSpace(snapshot.Name) ? "族中紫府" : snapshot.Name;
		XjChronicleEvent chronicleEvent = new XjChronicleEvent(
			true,
			snapshot.FamilyStableId,
			snapshot.ActorId,
			XjChronicleEventTypes.LingWuAppeared,
			snapshot.Year,
			"紫府遗蜕化生灵物",
			actorName + "身故后，其" + definition.DaoTuGroup + "道痕凝聚为“" + definition.Name + "”，收入家族重宝仓库。",
			4,
			true,
			true,
			false,
			"Ok",
			"lingwu.zifu_death",
			snapshot.RealmId);
		string eventKey = "LingWuAppeared:" + snapshot.ActorId + ":" + snapshot.Year + ":" + definition.Id;
		return XjFamilyChronicleMemory.Shared.Append(chronicleEvent, eventKey);
	}
}
