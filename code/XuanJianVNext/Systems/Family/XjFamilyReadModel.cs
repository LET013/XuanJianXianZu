using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Family;

internal sealed class XjFamilyReadModel
{
	internal static XjFamilyReadModel Shared { get; } = new XjFamilyReadModel(XjFamilyMemberIndex.Shared);

	private const int MaxCachedMemberLists = 2048;

	private readonly XjFamilyMemberIndex memberIndex;
	private readonly Dictionary<long, AliveMemberCacheEntry> aliveMemberCache = new Dictionary<long, AliveMemberCacheEntry>();
	private readonly Dictionary<long, MemberListCacheEntry> memberListCache = new Dictionary<long, MemberListCacheEntry>();

	private readonly struct AliveMemberCacheEntry
	{
		internal readonly int Revision;
		internal readonly IReadOnlyList<XjFamilyMemberLedgerEntry> Entries;

		internal AliveMemberCacheEntry(int revision, IReadOnlyList<XjFamilyMemberLedgerEntry> entries)
		{
			Revision = revision;
			Entries = entries ?? Array.Empty<XjFamilyMemberLedgerEntry>();
		}
	}

	private readonly struct MemberListCacheEntry
	{
		internal readonly int Revision;
		internal readonly IReadOnlyList<XjFamilyMemberDisplayItem> Items;

		internal MemberListCacheEntry(int revision, IReadOnlyList<XjFamilyMemberDisplayItem> items)
		{
			Revision = revision;
			Items = items ?? Array.Empty<XjFamilyMemberDisplayItem>();
		}
	}

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

	internal bool IsPending(long actorId)
	{
		return memberIndex.IsActorPending(actorId);
	}

	internal IReadOnlyList<XjFamilyMemberLedgerEntry> ReadAliveMembers(long familyId)
	{
		if (familyId <= 0L) return Array.Empty<XjFamilyMemberLedgerEntry>();

		int revision = XjRelationEntityRevisionStore.GetFamilyRevision(familyId);
		if (aliveMemberCache.TryGetValue(familyId, out AliveMemberCacheEntry cached)
			&& cached.Revision == revision)
		{
			return cached.Entries;
		}

		XjFamilyMemberLedgerEntry[] built = XjFamilyMemberLedger.BuildAliveReadModelSnapshot(familyId);
		IReadOnlyList<XjFamilyMemberLedgerEntry> stable = built.Length == 0
			? Array.Empty<XjFamilyMemberLedgerEntry>()
			: Array.AsReadOnly(built);
		if (aliveMemberCache.Count >= MaxCachedMemberLists) aliveMemberCache.Clear();
		aliveMemberCache[familyId] = new AliveMemberCacheEntry(revision, stable);
		return stable;
	}

	internal IReadOnlyCollection<long> GetFamilyMemberIds(long familyId)
	{
		IReadOnlyCollection<long> indexed = memberIndex.GetFamilyMemberIds(familyId);
		if (indexed.Count > 0)
		{
			return indexed;
		}

		IReadOnlyList<XjFamilyMemberLedgerEntry> entries = ReadAliveMembers(familyId);
		if (entries.Count == 0)
		{
			return Array.Empty<long>();
		}

		List<long> ids = new List<long>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			long actorId = entries[i].ActorId;
			if (TryGetActor(actorId, out Actor actor) && actor?.data != null && actor.isAlive())
			{
				ids.Add(actorId);
			}
		}
		return ids;
	}

	internal IReadOnlyList<XjFamilyMemberDisplayItem> BuildMemberDisplayItems(long familyId)
	{
		if (familyId <= 0L) return Array.Empty<XjFamilyMemberDisplayItem>();

		int revision = XjRelationEntityRevisionStore.GetFamilyRevision(familyId);
		if (memberListCache.TryGetValue(familyId, out MemberListCacheEntry cached)
			&& cached.Revision == revision)
		{
			return cached.Items;
		}

		IReadOnlyList<XjFamilyMemberDisplayItem> built = XjFamilyDisplayProjection.BuildMemberItems(ReadAliveMembers(familyId));
		IReadOnlyList<XjFamilyMemberDisplayItem> stable = FreezeMemberDisplayItems(built);
		if (memberListCache.Count >= MaxCachedMemberLists) memberListCache.Clear();
		memberListCache[familyId] = new MemberListCacheEntry(revision, stable);
		return stable;
	}

	internal void ClearCache()
	{
		aliveMemberCache.Clear();
		memberListCache.Clear();
	}

	private static IReadOnlyList<XjFamilyMemberDisplayItem> FreezeMemberDisplayItems(
		IReadOnlyList<XjFamilyMemberDisplayItem> items)
	{
		if (items == null || items.Count == 0) return Array.Empty<XjFamilyMemberDisplayItem>();
		XjFamilyMemberDisplayItem[] snapshot = new XjFamilyMemberDisplayItem[items.Count];
		for (int i = 0; i < items.Count; i++) snapshot[i] = items[i];
		return Array.AsReadOnly(snapshot);
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

		IReadOnlyList<XjFamilyMemberLedgerEntry> entries = ReadAliveMembers(familyId);
		for (int i = 0; i < entries.Count; i++)
		{
			if (TryGetActor(entries[i].ActorId, out Actor actor) && actor?.data != null && actor.isAlive())
			{
				yield return actor;
			}
		}
	}
}
