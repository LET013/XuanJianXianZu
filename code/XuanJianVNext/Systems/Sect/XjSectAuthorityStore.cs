using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门成员与城镇归属的唯一运行期权威索引。
///
/// 约束：
/// 1. ActorId -> SectId 只保存在这里和 XjSectMemberArchiveRecord；
/// 2. CityId -> SectId 只由 XjSectArchiveRecord.CityIds/CapitalCityId 派生；
/// 3. actor.data 与 city.data 仅由 XjSectProjection 单向写入；
/// 4. Systems/ZongMen 只能通过 XjSectCommands 修改权威状态。
/// </summary>
internal static class XjSectAuthorityStore
{
	private static readonly Dictionary<long, XjSectMemberArchiveRecord> MemberByActorId = new Dictionary<long, XjSectMemberArchiveRecord>();
	private static readonly Dictionary<long, List<long>> ActorIdsBySectId = new Dictionary<long, List<long>>();
	private static readonly Dictionary<long, HashSet<long>> ActorIdSetsBySectId = new Dictionary<long, HashSet<long>>();
	private static readonly Dictionary<long, long> SectIdByCityId = new Dictionary<long, long>();
	private static readonly Queue<long> DirtyProjectionQueue = new Queue<long>();
	private static readonly HashSet<long> DirtyProjectionSet = new HashSet<long>();
	private static long[] CachedMemberActorIds = Array.Empty<long>();
	private static long MembershipRevision;
	private static long CachedMemberActorIdsRevision = -1L;

	internal static bool NeedsLegacyMigration { get; private set; }
	internal static int MemberCount => MemberByActorId.Count;

	/// <summary>
	/// Stable, versioned authority-member id view used by low-frequency invariant
	/// reconciliation. Role/projection changes may invalidate it conservatively,
	/// but callers never enumerate the live dictionary across rendered frames.
	/// </summary>
	internal static IReadOnlyList<long> GetMemberActorIdsSnapshot()
	{
		if (CachedMemberActorIdsRevision == MembershipRevision) return CachedMemberActorIds;
		if (MemberByActorId.Count == 0)
		{
			CachedMemberActorIds = Array.Empty<long>();
			CachedMemberActorIdsRevision = MembershipRevision;
			return CachedMemberActorIds;
		}
		long[] ids = new long[MemberByActorId.Count];
		MemberByActorId.Keys.CopyTo(ids, 0);
		Array.Sort(ids);
		CachedMemberActorIds = ids;
		CachedMemberActorIdsRevision = MembershipRevision;
		return CachedMemberActorIds;
	}
	internal static bool HasDirtyProjection => DirtyProjectionQueue.Count > 0;

	internal static void ImportArchiveRecords(IReadOnlyList<XjSectMemberArchiveRecord> records)
	{
		XjSectReadModel.Shared.Clear();
		ClearMembersOnly();
		bool hadArchiveRecords = records != null && records.Count > 0;
		if (records != null)
		{
			for (int i = 0; i < records.Count; i++)
			{
				XjSectMemberArchiveRecord source = records[i];
				if (source == null || source.ActorId <= 0L || source.SectId <= 0L) continue;
				if (!XjSectRepository.TryGetBySectId(source.SectId, out XjSectArchiveRecord sect) || sect == null) continue;
				XjSectMemberArchiveRecord copy = Clone(source);
				ImportCandidate(copy, force: false);
			}
		}
		RebuildCityAuthorityIndex();
		NeedsLegacyMigration = !hadArchiveRecords && XjSectRepository.Count > 0;
	}

	internal static void ExportArchiveRecords(List<XjSectMemberArchiveRecord> output)
	{
		if (output == null) return;
		output.Clear();
		List<long> actorIds = new List<long>(MemberByActorId.Keys);
		actorIds.Sort();
		for (int i = 0; i < actorIds.Count; i++)
		{
			output.Add(Clone(MemberByActorId[actorIds[i]]));
		}
	}

	internal static bool TryGetMember(long actorId, out XjSectMemberArchiveRecord member)
	{
		member = null;
		return actorId > 0L && MemberByActorId.TryGetValue(actorId, out member) && member?.SectId > 0L;
	}

	internal static bool TryGetSectId(long actorId, out long sectId)
	{
		sectId = 0L;
		if (!TryGetMember(actorId, out XjSectMemberArchiveRecord member)) return false;
		sectId = member.SectId;
		return sectId > 0L;
	}

	internal static bool TryGetSectIdByCity(long cityId, out long sectId)
	{
		sectId = 0L;
		if (cityId <= 0L) return false;
		if (SectIdByCityId.TryGetValue(cityId, out sectId) && sectId > 0L) return true;
		// 领地变化的旧调用仍可能直接改 record.CityIds。缓存未命中时只重建
		// 宗门档案索引，不读取 WorldBox 城镇镜像，也不扫描角色。
		RebuildCityAuthorityIndex();
		return SectIdByCityId.TryGetValue(cityId, out sectId) && sectId > 0L;
	}

	internal static IReadOnlyList<long> GetActorIdsForSect(long sectId)
	{
		return sectId > 0L && ActorIdsBySectId.TryGetValue(sectId, out List<long> actorIds)
			? actorIds
			: Array.Empty<long>();
	}

	internal static IReadOnlyList<XjSectMemberArchiveRecord> ReadMembersForSect(long sectId)
	{
		return XjSectReadModel.Shared.ReadMembers(sectId);
	}

	internal static XjSectMemberArchiveRecord[] BuildMemberReadModelSnapshot(long sectId)
	{
		IReadOnlyList<long> actorIds = GetActorIdsForSect(sectId);
		if (actorIds.Count == 0) return Array.Empty<XjSectMemberArchiveRecord>();

		List<XjSectMemberArchiveRecord> result = new List<XjSectMemberArchiveRecord>(actorIds.Count);
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (MemberByActorId.TryGetValue(actorIds[i], out XjSectMemberArchiveRecord member) && member != null)
			{
				result.Add(Clone(member));
			}
		}
		result.Sort(CompareMembers);
		return result.Count == 0 ? Array.Empty<XjSectMemberArchiveRecord>() : result.ToArray();
	}

	internal static int CountMembers(long sectId)
	{
		return sectId > 0L && ActorIdsBySectId.TryGetValue(sectId, out List<long> actorIds) ? actorIds.Count : 0;
	}

	internal static bool UpsertMember(
		long actorId,
		long sectId,
		int joinYear,
		string role,
		int peakId,
		int currentYear,
		bool force)
	{
		if (actorId <= 0L || sectId <= 0L || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null)
		{
			return false;
		}
		// 最底层权威写口也执行释修硬禁令，避免归档导入或未来旁路绕过命令层。
		if (XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor resolvedActor)
			&& resolvedActor?.data != null && XjCultivationPathRules.IsShi(resolvedActor))
		{
			RemoveMember(actorId, out _);
			return false;
		}

		string normalizedRole = XjSectMemberRole.Normalize(role);
		int normalizedPeakId = NormalizePeakId(normalizedRole, peakId);
		if (MemberByActorId.TryGetValue(actorId, out XjSectMemberArchiveRecord existing) && existing != null)
		{
			if (!force && !ShouldReplaceExisting(existing, sectId, normalizedRole, joinYear)) return false;
			bool changed = existing.SectId != sectId
				|| existing.JoinYear != Math.Max(0, joinYear)
				|| existing.PeakId != normalizedPeakId
				|| !string.Equals(existing.Role, normalizedRole, StringComparison.Ordinal);
			if (!changed)
			{
				int nextUpdatedYear = Math.Max(existing.LastUpdatedYear, currentYear);
				if (nextUpdatedYear != existing.LastUpdatedYear)
				{
					existing.LastUpdatedYear = nextUpdatedYear;
					XjRelationEntityRevisionStore.MarkSect(existing.SectId);
				}
				return false;
			}

			long previousSectId = existing.SectId;
			RemoveFromSectIndex(actorId, previousSectId);
			existing.SchemaVersion = XjSectDomainSchema.CurrentVersion;
			existing.SectId = sectId;
			existing.JoinYear = Math.Max(0, joinYear);
			existing.Role = normalizedRole;
			existing.PeakId = normalizedPeakId;
			existing.LastUpdatedYear = Math.Max(0, currentYear);
			AddToSectIndex(actorId, sectId);
			MarkProjectionDirty(previousSectId);
			MarkProjectionDirty(sectId);
			MarkActorMembershipChanged(actorId);
			return true;
		}

		XjSectMemberArchiveRecord created = new XjSectMemberArchiveRecord
		{
			SchemaVersion = XjSectDomainSchema.CurrentVersion,
			ActorId = actorId,
			SectId = sectId,
			JoinYear = Math.Max(0, joinYear),
			Role = normalizedRole,
			PeakId = normalizedPeakId,
			LastUpdatedYear = Math.Max(0, currentYear)
		};
		MemberByActorId[actorId] = created;
		AddToSectIndex(actorId, sectId);
		MarkProjectionDirty(sectId);
		MarkActorMembershipChanged(actorId);
		return true;
	}

	internal static bool RemoveMember(long actorId, out long previousSectId)
	{
		previousSectId = 0L;
		if (actorId <= 0L || !MemberByActorId.TryGetValue(actorId, out XjSectMemberArchiveRecord member) || member == null)
		{
			return false;
		}
		previousSectId = member.SectId;
		MemberByActorId.Remove(actorId);
		RemoveFromSectIndex(actorId, previousSectId);
		MarkProjectionDirty(previousSectId);
		MarkActorMembershipChanged(actorId);
		return true;
	}

	internal static bool SetMemberRole(long actorId, string role, int peakId, int currentYear)
	{
		if (!TryGetMember(actorId, out XjSectMemberArchiveRecord member)) return false;
		string normalizedRole = XjSectMemberRole.Normalize(role);
		int normalizedPeakId = NormalizePeakId(normalizedRole, peakId);
		if (string.Equals(member.Role, normalizedRole, StringComparison.Ordinal) && member.PeakId == normalizedPeakId)
		{
			int nextUpdatedYear = Math.Max(member.LastUpdatedYear, currentYear);
			if (nextUpdatedYear != member.LastUpdatedYear)
			{
				member.LastUpdatedYear = nextUpdatedYear;
				XjRelationEntityRevisionStore.MarkSect(member.SectId);
			}
			return false;
		}
		member.Role = normalizedRole;
		member.PeakId = normalizedPeakId;
		member.LastUpdatedYear = Math.Max(0, currentYear);
		MarkProjectionDirty(member.SectId);
		MarkActorMembershipChanged(actorId);
		return true;
	}

	internal static void RegisterSectCities(XjSectArchiveRecord sect)
	{
		if (sect?.SectId <= 0L) return;
		RemoveCityMappingsForSect(sect.SectId);
		if (sect.CapitalCityId > 0L) SectIdByCityId[sect.CapitalCityId] = sect.SectId;
		if (sect.CityIds == null) return;
		for (int i = 0; i < sect.CityIds.Count; i++)
		{
			long cityId = sect.CityIds[i];
			if (cityId > 0L) SectIdByCityId[cityId] = sect.SectId;
		}
	}

	internal static void UnregisterSect(long sectId)
	{
		if (sectId <= 0L) return;
		RemoveCityMappingsForSect(sectId);
		if (ActorIdsBySectId.TryGetValue(sectId, out List<long> actorIds))
		{
			long[] snapshot = actorIds.ToArray();
			for (int i = 0; i < snapshot.Length; i++)
			{
				MemberByActorId.Remove(snapshot[i]);
				MarkActorMembershipChanged(snapshot[i]);
			}
		}
		ActorIdsBySectId.Remove(sectId);
		ActorIdSetsBySectId.Remove(sectId);
		MarkProjectionDirty(sectId);
	}

	internal static void RebuildCityAuthorityIndex()
	{
		SectIdByCityId.Clear();
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord sect = sects[i];
			if (sect == null || sect.SectId <= 0L || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)) continue;
			if (sect.CapitalCityId > 0L) SectIdByCityId[sect.CapitalCityId] = sect.SectId;
			if (sect.CityIds == null) continue;
			for (int j = 0; j < sect.CityIds.Count; j++)
			{
				long cityId = sect.CityIds[j];
				if (cityId > 0L) SectIdByCityId[cityId] = sect.SectId;
			}
		}
	}

	internal static void MarkProjectionDirty(long sectId)
	{
		if (sectId <= 0L) return;
		// Revision is a data-version signal, not a queue-version signal. Multiple
		// authority mutations may be coalesced into one projection queue node, but
		// every mutation must invalidate shared read models immediately.
		XjRelationEntityRevisionStore.MarkSect(sectId);
		if (!DirtyProjectionSet.Add(sectId)) return;
		DirtyProjectionQueue.Enqueue(sectId);
	}

	internal static bool TryDequeueDirtyProjection(out long sectId)
	{
		sectId = 0L;
		while (DirtyProjectionQueue.Count > 0)
		{
			long candidate = DirtyProjectionQueue.Dequeue();
			DirtyProjectionSet.Remove(candidate);
			if (candidate <= 0L) continue;
			sectId = candidate;
			return true;
		}
		return false;
	}

	internal static void CompleteLegacyMigration()
	{
		NeedsLegacyMigration = false;
	}

	internal static void Clear()
	{
		ClearMembersOnly();
		SectIdByCityId.Clear();
		DirtyProjectionQueue.Clear();
		DirtyProjectionSet.Clear();
		NeedsLegacyMigration = false;
	}

	private static void ClearMembersOnly()
	{
		MemberByActorId.Clear();
		ActorIdsBySectId.Clear();
		ActorIdSetsBySectId.Clear();
		DirtyProjectionQueue.Clear();
		DirtyProjectionSet.Clear();
		InvalidateMemberActorIdSnapshot();
		CachedMemberActorIds = Array.Empty<long>();
		CachedMemberActorIdsRevision = MembershipRevision;
	}

	private static void ImportCandidate(XjSectMemberArchiveRecord candidate, bool force)
	{
		if (candidate == null) return;
		string role = XjSectMemberRole.Normalize(candidate.Role);
		int peakId = candidate.PeakId;
		// 修复旧档中已经被下放成普通门人/弟子的金丹、真君、道胎。
		// 峰主和宗主保持原位，不用“高境归洞天”反向覆盖领导席位。
		if ((role == XjSectMemberRole.Member || role == XjSectMemberRole.Disciple)
			&& XjActorRegistry.ResolveKnownOrWorld(candidate.ActorId, out Actor actor)
			&& actor?.data != null && actor.isAlive())
		{
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (XjDaoTaiSpellScale.IsDaoTaiActor(actor) || XjCultivationPathRules.IsJinDanEquivalentRealm(realmId))
			{
				role = XjSectMemberRole.SupremeElder;
				peakId = 1;
			}
		}
		UpsertMember(
			candidate.ActorId,
			candidate.SectId,
			candidate.JoinYear,
			role,
			peakId,
			candidate.LastUpdatedYear,
			force);
	}

	private static bool ShouldReplaceExisting(XjSectMemberArchiveRecord existing, long nextSectId, string nextRole, int nextJoinYear)
	{
		int currentPriority = XjSectMemberRole.Priority(existing.Role);
		int nextPriority = XjSectMemberRole.Priority(nextRole);
		if (nextPriority != currentPriority) return nextPriority > currentPriority;
		if (nextSectId != existing.SectId) return nextSectId < existing.SectId;
		int currentJoinYear = existing.JoinYear <= 0 ? int.MaxValue : existing.JoinYear;
		int candidateJoinYear = nextJoinYear <= 0 ? int.MaxValue : nextJoinYear;
		return candidateJoinYear < currentJoinYear;
	}

	private static int NormalizePeakId(string role, int peakId)
	{
		return XjSectMemberRole.Normalize(role) switch
		{
			XjSectMemberRole.Sovereign => 0,
			XjSectMemberRole.SupremeElder => 1,
			XjSectMemberRole.PeakMaster => Math.Max(2, peakId),
			XjSectMemberRole.Disciple => Math.Max(0, peakId),
			_ => 0
		};
	}

	private static void AddToSectIndex(long actorId, long sectId)
	{
		if (actorId <= 0L || sectId <= 0L) return;
		if (!ActorIdsBySectId.TryGetValue(sectId, out List<long> actorIds))
		{
			actorIds = new List<long>();
			ActorIdsBySectId[sectId] = actorIds;
			ActorIdSetsBySectId[sectId] = new HashSet<long>();
		}
		if (!ActorIdSetsBySectId.TryGetValue(sectId, out HashSet<long> actorIdSet))
		{
			actorIdSet = new HashSet<long>(actorIds);
			ActorIdSetsBySectId[sectId] = actorIdSet;
		}
		if (!actorIdSet.Add(actorId)) return;
		actorIds.Add(actorId);
		actorIds.Sort();
	}

	private static void RemoveFromSectIndex(long actorId, long sectId)
	{
		if (actorId <= 0L || sectId <= 0L || !ActorIdsBySectId.TryGetValue(sectId, out List<long> actorIds)) return;
		actorIds.Remove(actorId);
		if (ActorIdSetsBySectId.TryGetValue(sectId, out HashSet<long> set)) set.Remove(actorId);
		if (actorIds.Count > 0) return;
		ActorIdsBySectId.Remove(sectId);
		ActorIdSetsBySectId.Remove(sectId);
	}

	private static void RemoveCityMappingsForSect(long sectId)
	{
		if (sectId <= 0L || SectIdByCityId.Count == 0) return;
		List<long> remove = null;
		foreach (KeyValuePair<long, long> pair in SectIdByCityId)
		{
			if (pair.Value != sectId) continue;
			remove ??= new List<long>();
			remove.Add(pair.Key);
		}
		if (remove == null) return;
		for (int i = 0; i < remove.Count; i++) SectIdByCityId.Remove(remove[i]);
	}

	private static void MarkActorMembershipChanged(long actorId)
	{
		InvalidateMemberActorIdSnapshot();
		XjActorStateRevisionStore.Mark(actorId, XjActorStateDomain.Sect | XjActorStateDomain.Relations);
	}

	private static void InvalidateMemberActorIdSnapshot()
	{
		unchecked { MembershipRevision++; }
	}

	private static int CompareMembers(XjSectMemberArchiveRecord left, XjSectMemberArchiveRecord right)
	{
		if (ReferenceEquals(left, right)) return 0;
		if (left == null) return 1;
		if (right == null) return -1;
		int byPriority = XjSectMemberRole.Priority(right.Role).CompareTo(XjSectMemberRole.Priority(left.Role));
		if (byPriority != 0) return byPriority;
		int byPeak = left.PeakId.CompareTo(right.PeakId);
		return byPeak != 0 ? byPeak : left.ActorId.CompareTo(right.ActorId);
	}

	private static XjSectMemberArchiveRecord Clone(XjSectMemberArchiveRecord source)
	{
		return new XjSectMemberArchiveRecord
		{
			SchemaVersion = XjSectDomainSchema.CurrentVersion,
			ActorId = source.ActorId,
			SectId = source.SectId,
			JoinYear = Math.Max(0, source.JoinYear),
			PeakId = Math.Max(0, source.PeakId),
			Role = XjSectMemberRole.Normalize(source.Role),
			LastUpdatedYear = Math.Max(0, source.LastUpdatedYear)
		};
	}
}
