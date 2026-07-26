using System;
using System.Collections.Generic;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Family;

internal sealed class XjFamilyReadModel
{
	internal static XjFamilyReadModel Shared { get; } = new XjFamilyReadModel(XjFamilyMemberIndex.Shared);

	private readonly XjFamilyMemberIndex memberIndex;

	internal XjFamilyReadModel(XjFamilyMemberIndex memberIndex)
	{
		this.memberIndex = memberIndex ?? XjFamilyMemberIndex.Shared;
	}

	internal bool TryGetIdentity(long actorId, out XjFamilyIdentity identity)
	{
		if (memberIndex.TryGetRecord(actorId, out identity))
		{
			return true;
		}

		if (XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found
			&& entry.FamilyStableId > 0L)
		{
			identity = new XjFamilyIdentity(
				true,
				entry.ActorId,
				entry.FamilyStableId,
				entry.Generation > 0 ? entry.Generation : 1,
				false,
				XjFamilyIdentityReasons.Confirmed);
			return true;
		}

		// 与纪事写入器使用同一末级身份来源。读档早期 memberIndex 尚未重建、
		// ledger 又暂缺记录时，IdentityIndex 仍保存角色归属；若读模型不采用
		// 这一层回退，事件会写入正确家族却在家族纪事窗口中读取不到。
		if (XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord record)
			&& record.Found
			&& record.RootActorId > 0L)
		{
			identity = new XjFamilyIdentity(
				true,
				record.ActorId,
				record.RootActorId,
				1,
				false,
				string.IsNullOrWhiteSpace(record.ReasonCode)
					? XjFamilyIdentityReasons.Confirmed
					: record.ReasonCode);
			return true;
		}

		identity = XjFamilyIdentity.Empty;
		return false;
	}

	internal bool TryGetActor(long actorId, out Actor actor)
	{
		return memberIndex.TryGetActor(actorId, out actor);
	}

	internal bool TryGetFamilyStableId(long actorId, out long familyStableId)
	{
		return TryGetConfirmedFamilyStableId(actorId, out familyStableId);
	}

	internal bool TryGetConfirmedFamilyStableId(long actorId, out long familyStableId)
	{
		if (!TryGetIdentity(actorId, out XjFamilyIdentity identity) || !identity.Found)
		{
			familyStableId = 0L;
			return false;
		}

		familyStableId = identity.FamilyStableIdValue;
		return familyStableId > 0L;
	}

	internal bool TryGetConfirmedIdentity(long actorId, out XjFamilyIdentity identity)
	{
		return TryGetIdentity(actorId, out identity) && identity.Found && identity.FamilyStableIdValue > 0L;
	}

	internal int GetGeneration(long actorId)
	{
		return TryGetIdentity(actorId, out XjFamilyIdentity identity) ? identity.Generation : 0;
	}

	internal bool TryGetBloodlineDetails(long actorId, out XjBloodlineDisplayState state)
	{
		state = XjBloodlineDisplayState.Empty;
		if (!TryGetConfirmedIdentity(actorId, out _))
		{
			return false;
		}

		if (memberIndex.TryGetActor(actorId, out Actor actor)
			&& XjBloodlineBirthRules.TryReadAppliedBloodline(
				actor,
				out string realQuality,
				out int realConcentration,
				out int realGeneration,
				out string originDaoTu))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineSource, out string source);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBloodlineExtraTalentInheritance, out int extraTalent);
			state = new XjBloodlineDisplayState(
				true,
				false,
				realQuality,
				realConcentration,
				realGeneration,
				originDaoTu,
				source,
				extraTalent);
			return true;
		}

		return false;
	}

	internal bool TryGetBloodlineQuality(long actorId, out string quality, out int concentration)
	{
		quality = string.Empty;
		concentration = 0;
		if (!TryGetBloodlineDetails(actorId, out XjBloodlineDisplayState state))
		{
			return false;
		}

		quality = state.Quality;
		concentration = state.Concentration;
		return true;
	}

	internal bool IsPending(long actorId)
	{
		return memberIndex.IsActorPending(actorId);
	}

	internal IReadOnlyCollection<long> GetFamilyMemberIds(long familyId)
	{
		IReadOnlyCollection<long> indexed = memberIndex.GetFamilyMemberIds(familyId);
		if (indexed.Count > 0)
		{
			return indexed;
		}

		IReadOnlyList<XjFamilyMemberLedgerEntry> entries = XjFamilyMemberLedger.ReadFamilyAlive(familyId);
		if (entries.Count == 0)
		{
			return Array.Empty<long>();
		}

		List<long> ids = new List<long>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			ids.Add(entries[i].ActorId);
		}
		return ids;
	}

	internal IReadOnlyList<XjFamilyMemberDisplayItem> BuildMemberDisplayItems(long familyId)
	{
		return memberIndex.BuildMemberDisplayItems(familyId);
	}

	internal IReadOnlyList<XjFamilyPendingDisplayItem> BuildPendingDisplayItems(long familyId, long focusedActorId)
	{
		return memberIndex.BuildPendingDisplayItems(familyId, focusedActorId);
	}

	internal IReadOnlyList<XjFamilyMarriageDisplayItem> BuildMarriageDisplayItems(long familyId, long focusedActorId)
	{
		return memberIndex.BuildMarriageDisplayItems(familyId, focusedActorId);
	}

	internal IEnumerable<Actor> GetFamilyMembers(long familyId)
	{
		IReadOnlyCollection<long> indexedIds = memberIndex.GetFamilyMemberIds(familyId);
		if (indexedIds.Count > 0)
		{
			foreach (Actor indexedActor in memberIndex.GetFamilyMembers(familyId))
			{
				yield return indexedActor;
			}
			yield break;
		}

		IReadOnlyList<XjFamilyMemberLedgerEntry> entries = XjFamilyMemberLedger.ReadFamilyAlive(familyId);
		for (int i = 0; i < entries.Count; i++)
		{
			if (TryGetActor(entries[i].ActorId, out Actor actor) && actor?.data != null && actor.isAlive())
			{
				yield return actor;
			}
		}
	}
}
