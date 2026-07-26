using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Family;

internal readonly struct XjFamilyPendingRecord
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly long ParentId1;
	internal readonly long ParentId2;
	internal readonly long FatherActorId;
	internal readonly string Reason;

	internal XjFamilyPendingRecord(
		bool found,
		long actorId,
		string actorName,
		long parentId1,
		long parentId2,
		long fatherActorId,
		string reason)
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		ParentId1 = parentId1 < 0L ? 0L : parentId1;
		ParentId2 = parentId2 < 0L ? 0L : parentId2;
		FatherActorId = fatherActorId < 0L ? 0L : fatherActorId;
		Reason = reason ?? string.Empty;
	}
}

internal static class XjFamilyIdentityIndex
{
	private static readonly Dictionary<long, XjFamilyIdentityRecord> recordsByActorId = new Dictionary<long, XjFamilyIdentityRecord>();
	private static readonly Dictionary<string, HashSet<long>> actorIdsByFamilyKey = new Dictionary<string, HashSet<long>>();

	internal static bool Register(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		if (XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			RemoveActor(actorId);
			return false;
		}

		XjFamilyKey key = XjFamilyKeyResolver.TryResolve(actor);
		return Register(actorId, key, actor.data.sex == ActorSex.Male);
	}

	internal static bool Register(long actorId, XjFamilyKey key)
	{
		return Register(actorId, key, false);
	}

	internal static bool Register(long actorId, XjFamilyKey key, bool isMale)
	{
		if (actorId <= 0L || !key.Found || string.IsNullOrWhiteSpace(key.FamilyKey))
		{
			return false;
		}

		if (recordsByActorId.TryGetValue(actorId, out XjFamilyIdentityRecord existing))
		{
			RemoveActorIdFromFamilyKey(actorId, existing.FamilyKey);
		}

		XjFamilyIdentityRecord record = new XjFamilyIdentityRecord(
			true,
			actorId,
			key.FamilyKey,
			key.RootActorId,
			string.IsNullOrWhiteSpace(key.ReasonCode) ? XjFamilyIdentityReasons.Confirmed : key.ReasonCode,
			isMale);

		recordsByActorId[actorId] = record;
		if (!actorIdsByFamilyKey.TryGetValue(key.FamilyKey, out HashSet<long> actorIds))
		{
			actorIds = new HashSet<long>();
			actorIdsByFamilyKey[key.FamilyKey] = actorIds;
		}

		actorIds.Add(actorId);
		return true;
	}

	internal static bool TryGetByActorId(long actorId, out XjFamilyIdentityRecord record)
	{
		if (actorId <= 0L || !recordsByActorId.TryGetValue(actorId, out record))
		{
			record = XjFamilyIdentityRecord.Empty;
			return false;
		}

		return true;
	}

	internal static bool TryGetActorIds(string familyKey, out IReadOnlyCollection<long> actorIds)
	{
		actorIds = null;
		if (string.IsNullOrWhiteSpace(familyKey) || !actorIdsByFamilyKey.TryGetValue(familyKey, out HashSet<long> values) || values.Count == 0)
		{
			return false;
		}

		actorIds = values;
		return true;
	}

	internal static void RemoveActor(long actorId)
	{
		if (actorId <= 0L || !recordsByActorId.TryGetValue(actorId, out XjFamilyIdentityRecord existing))
		{
			return;
		}

		recordsByActorId.Remove(actorId);
		RemoveActorIdFromFamilyKey(actorId, existing.FamilyKey);
	}

	internal static void Clear()
	{
		recordsByActorId.Clear();
		actorIdsByFamilyKey.Clear();
	}

	internal static void ExportArchiveRecords(List<XjWorldArchiveFamilyIdentityRecord> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (KeyValuePair<long, XjFamilyIdentityRecord> entry in recordsByActorId)
		{
			XjFamilyIdentityRecord record = entry.Value;
			if (!record.Found || record.ActorId <= 0L || string.IsNullOrWhiteSpace(record.FamilyKey))
			{
				continue;
			}

			records.Add(new XjWorldArchiveFamilyIdentityRecord
			{
				ActorId = record.ActorId,
				FamilyKey = record.FamilyKey,
				RootActorId = record.RootActorId,
				ReasonCode = record.ReasonCode,
				IsMale = record.IsMale
			});
		}
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveFamilyIdentityRecord> records)
	{
		Clear();
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveFamilyIdentityRecord record = records[i];
			if (record == null || record.ActorId <= 0L || string.IsNullOrWhiteSpace(record.FamilyKey) || record.RootActorId <= 0L)
			{
				continue;
			}

			XjFamilyIdentityRecord identityRecord = new XjFamilyIdentityRecord(
				true,
				record.ActorId,
				record.FamilyKey,
				record.RootActorId,
				string.IsNullOrWhiteSpace(record.ReasonCode) ? "ArchiveImport" : record.ReasonCode,
				record.IsMale);

			recordsByActorId[identityRecord.ActorId] = identityRecord;
			if (!actorIdsByFamilyKey.TryGetValue(identityRecord.FamilyKey, out HashSet<long> actorIds))
			{
				actorIds = new HashSet<long>();
				actorIdsByFamilyKey[identityRecord.FamilyKey] = actorIds;
			}

			actorIds.Add(identityRecord.ActorId);
		}
	}

	private static void RemoveActorIdFromFamilyKey(long actorId, string familyKey)
	{
		if (string.IsNullOrWhiteSpace(familyKey) || !actorIdsByFamilyKey.TryGetValue(familyKey, out HashSet<long> actorIds))
		{
			return;
		}

		actorIds.Remove(actorId);
		if (actorIds.Count == 0)
		{
			actorIdsByFamilyKey.Remove(familyKey);
		}
	}
}
