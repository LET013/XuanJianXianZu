using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Events;

internal static class XjFamilyDomainEventRouter
{
	internal static void Publish(in XjFamilyDomainEvent domainEvent)
	{
		if (domainEvent.ActorId > 0L
			&& XjActorRegistry.Resolve(domainEvent.ActorId, out Actor actor)
			&& XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return;
		}

		// 修士列传不依赖家族确认；先投影个人事实，避免出生早期处于家族 Pending 时永久丢事。
		XjThreeBookWriter.RecordPersonalFact(domainEvent);

		XjZongMenKnowledgeWriter.Handle(domainEvent);

		if (!TryResolveFamily(domainEvent, out XjFamilyDomainEvent resolvedEvent))
		{
			XjThreeBookDeferredFamilyFacts.Enqueue(domainEvent);
			return;
		}

		// 先补回该角色在家族 Pending 阶段遗漏的世家事实，保持原始年份顺序。
		XjThreeBookDeferredFamilyFacts.TryFlushActor(domainEvent.ActorId, 16);
		XjFamilyMemberLedger.UpsertFromDomainEvent(resolvedEvent);
		XjCenturyAnnalsStore.ObserveDomainEvent(resolvedEvent);
		XjFamilyWarehouseWriter.Handle(resolvedEvent);
		XjFamilyChronicleEventRouter.Handle(resolvedEvent);
		XjThreeBookWriter.RecordFamilyFact(resolvedEvent);
	}

	internal static bool TryResolveFamily(in XjFamilyDomainEvent domainEvent, out XjFamilyDomainEvent resolvedEvent)
	{
		resolvedEvent = default;
		if (!domainEvent.Found || domainEvent.ActorId <= 0L || string.IsNullOrWhiteSpace(domainEvent.EventType))
		{
			return false;
		}

		long familyStableId = domainEvent.FamilyStableId;
		string familyKey = domainEvent.FamilyKey;

		if (XjFamilyReadModel.Shared.IsPending(domainEvent.ActorId))
		{
			return false;
		}

		if (familyStableId <= 0L && XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(domainEvent.ActorId, out long readModelFamilyId))
		{
			familyStableId = readModelFamilyId;
		}

		if (string.IsNullOrWhiteSpace(familyKey)
			&& XjFamilyIdentityIndex.TryGetByActorId(domainEvent.ActorId, out XjFamilyIdentityRecord record)
			&& record.RootActorId > 0L)
		{
			if (string.Equals(record.ReasonCode, XjFamilyIdentityReasons.FatherPending, System.StringComparison.Ordinal)
				|| string.Equals(record.ReasonCode, XjFamilyIdentityReasons.FatherMissing, System.StringComparison.Ordinal))
			{
				return false;
			}

			familyKey = record.FamilyKey;
			if (familyStableId <= 0L)
			{
				familyStableId = record.RootActorId;
			}
		}

		if (familyStableId <= 0L)
		{
			return false;
		}

		if (string.IsNullOrWhiteSpace(familyKey))
		{
			familyKey = "actor:" + familyStableId;
		}

		resolvedEvent = domainEvent.WithFamily(familyStableId, familyKey);
		return true;
	}
}
