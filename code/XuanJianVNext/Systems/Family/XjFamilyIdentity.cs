using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Family;

internal sealed class XjFamilyIdentity
{
	internal const string FamilyStableId = "xuanjian.vnext.family.stable_id";
	internal const string FamilyGeneration = "xuanjian.vnext.family.generation";

	internal static XjFamilyIdentity Empty { get; } = new XjFamilyIdentity(
		false,
		0L,
		0L,
		0,
		false,
		"Empty");

	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly long FamilyStableIdValue;
	internal readonly int Generation;
	internal readonly bool MarriageDisplayOnly;
	internal readonly string ReasonCode;

	internal XjFamilyIdentity(
		bool found,
		long actorId,
		long familyStableId,
		int generation,
		bool marriageDisplayOnly,
		string reasonCode)
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		FamilyStableIdValue = familyStableId < 0L ? 0L : familyStableId;
		Generation = generation < 0 ? 0 : generation;
		MarriageDisplayOnly = marriageDisplayOnly;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjFamilyIdentityReasons
{
	internal const string Confirmed = "Confirmed";
	internal const string FatherResolved = "FatherResolved";
	internal const string ParentIndexed = "ParentIndexed";
	internal const string FatherPending = "FatherPending";
	internal const string FatherMissing = "FatherMissing";
	internal const string MarriageDisplayOnly = "MarriageDisplayOnly";
	internal const string RootActor = "RootActor";
	internal const string CityMigrationBranch = "CityMigrationBranch";
	internal const string MaternalLineageSuccession = "MaternalLineageSuccession";
}


internal readonly struct XjFamilyIdentityRecord
{
	internal static XjFamilyIdentityRecord Empty { get; } = new XjFamilyIdentityRecord(false, 0L, string.Empty, 0L, string.Empty, false);

	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string FamilyKey;
	internal readonly long RootActorId;
	internal readonly string ReasonCode;
	internal readonly bool IsMale;

	internal XjFamilyIdentityRecord(
		bool found,
		long actorId,
		string familyKey,
		long rootActorId,
		string reasonCode)
		: this(found, actorId, familyKey, rootActorId, reasonCode, false)
	{
	}

	internal XjFamilyIdentityRecord(
		bool found,
		long actorId,
		string familyKey,
		long rootActorId,
		string reasonCode,
		bool isMale)
	{
		Found = found;
		ActorId = actorId;
		FamilyKey = familyKey ?? string.Empty;
		RootActorId = rootActorId;
		ReasonCode = reasonCode ?? string.Empty;
		IsMale = isMale;
	}
}


internal readonly struct XjFamilyKey
{
	internal static XjFamilyKey Empty { get; } = new XjFamilyKey(false, string.Empty, 0L, string.Empty);

	internal readonly bool Found;
	internal readonly string FamilyKey;
	internal readonly long RootActorId;
	internal readonly string ReasonCode;

	internal XjFamilyKey(
		bool found,
		string familyKey,
		long rootActorId,
		string reasonCode)
	{
		Found = found;
		FamilyKey = familyKey ?? string.Empty;
		RootActorId = rootActorId;
		ReasonCode = reasonCode ?? string.Empty;
	}
}


internal static class XjFamilyKeyResolver
{
	internal static XjFamilyKey TryResolve(Actor actor)
	{
		if (actor == null)
		{
			return new XjFamilyKey(false, string.Empty, 0L, "ActorNull");
		}

		if (actor.data == null)
		{
			return new XjFamilyKey(false, string.Empty, 0L, "NoData");
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return new XjFamilyKey(false, string.Empty, 0L, "NoActorId");
		}

		if (!TryReadParentIds(actor, out long parent1, out long parent2))
		{
			return MakeRootKey(actorId, "RootActor");
		}

		Actor father = null;
		bool hasUnknownParentGender = false;
		TryClassifyParentId(parent1, ref father, ref hasUnknownParentGender);
		TryClassifyParentId(parent2, ref father, ref hasUnknownParentGender);

		if (father != null && father.data != null)
		{
			return MakeParentFamilyKey(father, XjFamilyIdentityReasons.FatherResolved);
		}

		if (TryMakeIndexedParentFamilyKey(parent1, parent2, out XjFamilyKey indexedParentKey))
		{
			return indexedParentKey;
		}

		return MakeRootKey(actorId, hasUnknownParentGender ? XjFamilyIdentityReasons.FatherPending : XjFamilyIdentityReasons.FatherMissing);
	}

	private static bool TryMakeIndexedParentFamilyKey(long parent1, long parent2, out XjFamilyKey key)
	{
		if (TryMakeIndexedParentFamilyKey(parent1, out key))
		{
			return true;
		}

		return TryMakeIndexedParentFamilyKey(parent2, out key);
	}

	private static bool TryMakeIndexedParentFamilyKey(long parentId, out XjFamilyKey key)
	{
		key = XjFamilyKey.Empty;
		if (parentId <= 0L)
		{
			return false;
		}

		if (!XjFamilyIdentityIndex.TryGetByActorId(parentId, out XjFamilyIdentityRecord record)
			|| !record.Found
			|| !record.IsMale
			|| string.IsNullOrWhiteSpace(record.FamilyKey)
			|| record.RootActorId <= 0L)
		{
			return false;
		}

		key = new XjFamilyKey(
			true,
			record.FamilyKey,
			record.RootActorId,
			XjFamilyIdentityReasons.ParentIndexed);
		return true;
	}

	private static bool TryReadParentIds(Actor actor, out long parent1, out long parent2)
	{
		parent1 = -1L;
		parent2 = -1L;
		if (actor?.data == null)
		{
			return false;
		}

		parent1 = actor.data.parent_id_1;
		parent2 = actor.data.parent_id_2;
		return parent1 > 0L || parent2 > 0L;
	}

	private static void TryClassifyParentId(long parentId, ref Actor father, ref bool hasUnknownParentGender)
	{
		if (parentId <= 0L || !TryResolveActorById(parentId, out Actor parent) || parent?.data == null)
		{
			return;
		}

		if (!TryClassifyParent(parent, out bool isMale, out _))
		{
			hasUnknownParentGender = true;
			return;
		}

		if (isMale && father == null)
		{
			father = parent;
		}
	}

	private static bool TryResolveActorById(long actorId, out Actor actor)
	{
		return XjActorRegistry.ResolveKnownOrWorld(actorId, out actor);
	}

	private static bool TryClassifyParent(Actor parent, out bool isMale, out bool isFemale)
	{
		isMale = false;
		isFemale = false;
		if (parent?.data == null)
		{
			return false;
		}

		if (parent.data.sex == ActorSex.Male)
		{
			isMale = true;
			return true;
		}

		if (parent.data.sex == ActorSex.Female)
		{
			isFemale = true;
			return true;
		}

		return false;
	}

	private static XjFamilyKey MakeParentFamilyKey(Actor parent, string reason)
	{
		if (parent?.data == null)
		{
			return XjFamilyKey.Empty;
		}

		long parentId = ((BaseSystemData)parent.data).id;
		if (parentId <= 0L)
		{
			return XjFamilyKey.Empty;
		}

		if (XjFamilyIdentityIndex.TryGetByActorId(parentId, out XjFamilyIdentityRecord record)
			&& !string.IsNullOrWhiteSpace(record.FamilyKey))
		{
		string indexedReason = string.Equals(reason, XjFamilyIdentityReasons.FatherResolved, StringComparison.Ordinal)
				? "FatherResolvedIndexed"
				: (reason ?? string.Empty) + "Indexed";

			return new XjFamilyKey(
				true,
				record.FamilyKey,
				record.RootActorId,
				indexedReason);
		}

		return MakeRootKey(parentId, reason);
	}

	private static XjFamilyKey MakeRootKey(long actorId, string reason)
	{
		if (actorId <= 0L)
		{
			return new XjFamilyKey(false, string.Empty, 0L, "NoActorId");
		}

		string familyKey = "actor:" + actorId.ToString(CultureInfo.InvariantCulture);
		return new XjFamilyKey(true, familyKey, actorId, reason);
	}
}


internal static class XjFamilyResolver
{
	internal static XjFamilyIdentity ResolveFamilyForActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return XjFamilyIdentity.Empty;
		}
		if (XjYinSiTraitLifecycle.IsYinSi(actor))
		{
			return XjFamilyIdentity.Empty;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return XjFamilyIdentity.Empty;
		}

		XjFamilyKey key = XjFamilyKeyResolver.TryResolve(actor);
		if (!key.Found || key.RootActorId <= 0L)
		{
			return new XjFamilyIdentity(false, actorId, 0L, 0, false, key.ReasonCode);
		}

		return new XjFamilyIdentity(
			true,
			actorId,
			key.RootActorId,
			0,
			false,
			key.ReasonCode);
	}

	internal static bool TryResolveFatherActorId(Actor actor, out long fatherActorId)
	{
		fatherActorId = 0L;
		if (actor?.data == null)
		{
			return false;
		}

		return TryResolveMaleParent(actor.data.parent_id_1, out fatherActorId)
			|| TryResolveMaleParent(actor.data.parent_id_2, out fatherActorId);
	}

	internal static bool TryResolveInheritanceParentActorId(Actor actor, out long parentActorId)
	{
		parentActorId = 0L;
		if (actor?.data == null)
		{
			return false;
		}

		if (TryResolveFatherActorId(actor, out parentActorId) && parentActorId > 0L)
		{
			return true;
		}

		return TryResolveIndexedParentActorId(actor.data.parent_id_1, out parentActorId)
			|| TryResolveIndexedParentActorId(actor.data.parent_id_2, out parentActorId);
	}

	internal static bool HasParentReference(Actor actor)
	{
		return actor?.data != null
			&& (actor.data.parent_id_1 > 0L || actor.data.parent_id_2 > 0L);
	}

	internal static void CollectParentIds(Actor actor, HashSet<long> parentIds)
	{
		if (actor?.data == null || parentIds == null)
		{
			return;
		}

		if (actor.data.parent_id_1 > 0L)
		{
			parentIds.Add(actor.data.parent_id_1);
		}

		if (actor.data.parent_id_2 > 0L)
		{
			parentIds.Add(actor.data.parent_id_2);
		}
	}

	private static bool TryResolveMaleParent(long parentId, out long fatherActorId)
	{
		fatherActorId = 0L;
		if (parentId <= 0L || !TryResolveActorById(parentId, out Actor parent) || parent?.data == null)
		{
			return false;
		}

		if (parent.data.sex != ActorSex.Male)
		{
			return false;
		}

		fatherActorId = parentId;
		return true;
	}

	private static bool TryResolveIndexedParentActorId(long parentId, out long parentActorId)
	{
		parentActorId = 0L;
		if (parentId <= 0L)
		{
			return false;
		}

		if (!XjFamilyIdentityIndex.TryGetByActorId(parentId, out XjFamilyIdentityRecord record)
			|| !record.Found
			|| !record.IsMale
			|| string.IsNullOrWhiteSpace(record.FamilyKey))
		{
			return false;
		}

		parentActorId = parentId;
		return true;
	}

	private static bool TryResolveActorById(long actorId, out Actor actor)
	{
		return XjActorRegistry.ResolveKnownOrWorld(actorId, out actor);
	}
}


internal sealed class XjFamilyGenerationService
{
	private readonly XjFamilyMemberIndex memberIndex;

	internal XjFamilyGenerationService(XjFamilyMemberIndex memberIndex)
	{
		this.memberIndex = memberIndex;
	}

	internal int ComputeGeneration(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return 0;
		}

		if (!XjFamilyResolver.TryResolveInheritanceParentActorId(actor, out long fatherActorId))
		{
			return 1;
		}

		if (memberIndex == null
			|| !memberIndex.TryGetRecord(fatherActorId, out XjFamilyIdentity fatherIdentity)
			|| !fatherIdentity.Found)
		{
			if (XjFamilyMemberLedger.TryGetByActorId(fatherActorId, out XjFamilyMemberLedgerEntry ledgerEntry)
				&& ledgerEntry.Found
				&& ledgerEntry.Generation > 0)
			{
				return ledgerEntry.Generation + 1;
			}

			return 0;
		}

		return Math.Max(1, fatherIdentity.Generation) + 1;
	}

	internal bool IsMarriageDisplayOnly(Actor actor, Actor related)
	{
		if (actor?.data == null || related?.data == null || memberIndex == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		long relatedId = ((BaseSystemData)related.data).id;
		if (!memberIndex.TryGetRecord(actorId, out XjFamilyIdentity actorIdentity)
			|| !memberIndex.TryGetRecord(relatedId, out XjFamilyIdentity relatedIdentity))
		{
			return false;
		}

		return actorIdentity.Found
			&& relatedIdentity.Found
			&& actorIdentity.FamilyStableIdValue != relatedIdentity.FamilyStableIdValue;
	}
}
