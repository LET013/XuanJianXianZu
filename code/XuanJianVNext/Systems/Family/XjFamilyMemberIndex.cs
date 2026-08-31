using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Family;

internal sealed class XjFamilyMemberIndex
{
	private const long BranchFamilyIdBase = 9000000000000L;
	private const long BranchFamilyIdSpan = 9000000000000L;
	private const int MaximumLiveFatherRepairDepth = 8;

	[ThreadStatic]
	private static int _liveFatherRepairDepth;

	internal static XjFamilyMemberIndex Shared { get; } = new XjFamilyMemberIndex();

	private readonly Dictionary<long, XjFamilyIdentity> recordsByActorId = new Dictionary<long, XjFamilyIdentity>();
	private readonly Dictionary<long, HashSet<long>> actorIdsByFamilyStableId = new Dictionary<long, HashSet<long>>();
	private readonly Dictionary<long, HashSet<long>> pendingActorIdsByParentId = new Dictionary<long, HashSet<long>>();
	private readonly Dictionary<long, XjFamilyPendingRecord> pendingRecordsByActorId = new Dictionary<long, XjFamilyPendingRecord>();
	private readonly XjFamilyGenerationService generationService;

	internal int RuntimeRecordCount => recordsByActorId.Count;
	internal int RuntimeFamilyBucketCount => actorIdsByFamilyStableId.Count;
	internal int PendingRecordCount => pendingRecordsByActorId.Count;

	internal XjFamilyMemberIndex()
	{
		generationService = new XjFamilyGenerationService(this);
	}

	internal void AddActorToFamily(Actor actor)
	{
		AddActorToFamilyInternal(actor, forceHighRealmConfirmation: false);
	}

	internal void EnsureHighRealmFamily(Actor actor)
	{
		if (HasPersistedFamilyIdentity(actor))
		{
			AddActorToFamilyInternal(actor, forceHighRealmConfirmation: true);
			return;
		}

		EnsureNativeClanForHighRealm(actor);
		AddActorToFamilyInternal(actor, forceHighRealmConfirmation: true);
	}

	/// <summary>
	/// Rebuilds only the runtime family index for an already-persisted member.
	/// It does not create a new family, publish confirmation events or grant
	/// inheritance during the post-load bootstrap.
	/// </summary>
	internal void RestoreRuntimeIndexAfterLoad(Actor actor)
	{
		if (actor?.data == null || XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || recordsByActorId.ContainsKey(actorId))
		{
			return;
		}

		TryRestoreFromLedgerAfterLoad(actor, actorId);
	}

	private static void EnsureNativeClanForHighRealm(Actor actor)
	{
		if (actor?.data == null || actor.hasClan() || MapBox.instance?.clans == null)
		{
			return;
		}

		try
		{
			MapBox.instance.clans.newClan(actor, true);
		}
		catch
		{
			// 原生氏族系统尚未就绪时，不阻断玄鉴家族索引；下次年度整理会重试。
		}
	}

	private bool HasPersistedFamilyIdentity(Actor actor)
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

		if (recordsByActorId.TryGetValue(actorId, out XjFamilyIdentity identity)
			&& identity.Found
			&& identity.FamilyStableIdValue > 0L)
		{
			return true;
		}

		if (XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found
			&& entry.FamilyStableId > 0L)
		{
			return true;
		}

		return TryReadIdentityMirror(actor, out long familyStableId, out _)
			&& familyStableId > 0L;
	}

	private void AddActorToFamilyInternal(Actor actor, bool forceHighRealmConfirmation)
	{
		if (!XjSafeCore.IsAliveActor(actor) || actor?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}

		if (XjYinSiTraitLifecycle.IsYinSi(actor))
		{
			RemoveActorIdFromCurrentFamily(actorId);
			RemovePendingActor(actorId);
			recordsByActorId.Remove(actorId);
			XjFamilyIdentityIndex.RemoveActor(actorId);
			ClearIdentityMirror(actor);
			return;
		}

		if (XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			RemoveActorIdFromCurrentFamily(actorId);
			RemovePendingActor(actorId);
			recordsByActorId.Remove(actorId);
			XjFamilyIdentityIndex.RemoveActor(actorId);
			ClearIdentityMirror(actor);
			return;
		}

		if (recordsByActorId.TryGetValue(actorId, out XjFamilyIdentity confirmedIdentity)
			&& confirmedIdentity.Found
			&& confirmedIdentity.FamilyStableIdValue > 0L)
		{
			if (TryBranchForCityMigration(actor, actorId, confirmedIdentity, GetCurrentYear(actor)))
			{
				return;
			}

			EnsureFamilyOriginCityMirror(actor);
			PersistIdentityMirror(actor, confirmedIdentity);
			XjFamilySurnamePolicy.EnsureMemberSurname(actor, confirmedIdentity);
			XjFamilyMemberLedger.UpsertConfirmed(actor, confirmedIdentity, "family.refresh");
			XjSongXuanEasterEggSystem.ObserveConfirmedFamilyMember(actor, confirmedIdentity, GetCurrentYear(actor));
			ProcessPendingChildren(actorId);
			return;
		}

		// 读档后 recordsByActorId 为空，但 XjFamilyMemberLedger 已从存档恢复。
		// 如果账本中已有该角色的家族归属，直接恢复到运行时索引中，避免因
		// XjFamilyIdentityIndex 为空导致重新解析产生不同的 FamilyStableId，
		// 进而使家族纪事在旧 FamilyStableId 下变为孤儿记录。
		if (TryRestoreFromLedgerAfterLoad(actor, actorId))
		{
			return;
		}

		// 年龄超时：大于 20 岁的角色不应无限期 pending。
		// 若父系迟迟未入族谱（已死亡/从未注册），直接按根角色解析。
		const int maxPendingAgeYears = 20;
		if (!forceHighRealmConfirmation && ShouldWaitForFather(actor, actorId))
		{
			int actorAge = (int)Math.Floor(Math.Max(0f, actor.getAge()));
			if (actorAge <= maxPendingAgeYears)
			{
				MarkActorPendingByParents(actor, actorId);
				return;
			}

			// 超时：移除 pending 状态，继续正常解析流程
			RemovePendingActor(actorId);
		}

		bool maternalSuccession = TryResolveMaternalLineageSuccession(actor, actorId, out XjFamilyIdentity resolved, out int maternalGeneration);
		if (!maternalSuccession)
		{
			resolved = XjFamilyResolver.ResolveFamilyForActor(actor);
		}
		if (!resolved.Found || resolved.ActorId <= 0L || resolved.FamilyStableIdValue <= 0L)
		{
			if (!forceHighRealmConfirmation && XjFamilyResolver.HasParentReference(actor))
			{
				MarkActorPendingByParents(actor, actorId);
			}
			return;
		}

		int generation = maternalSuccession ? maternalGeneration : generationService.ComputeGeneration(actor);
		if (!forceHighRealmConfirmation && generation <= 0 && XjFamilyResolver.HasParentReference(actor))
		{
			MarkActorPendingByParents(actor, actorId);
			return;
		}

		XjFamilyIdentity identity = new XjFamilyIdentity(
			true,
			resolved.ActorId,
			resolved.FamilyStableIdValue,
			generation <= 0 ? 1 : generation,
			false,
			maternalSuccession ? XjFamilyIdentityReasons.MaternalLineageSuccession : XjFamilyIdentityReasons.Confirmed);

		bool isNewFamilyConfirmation = !recordsByActorId.TryGetValue(identity.ActorId, out XjFamilyIdentity existing)
			|| existing.FamilyStableIdValue != identity.FamilyStableIdValue;

		RemoveActorIdFromCurrentFamily(identity.ActorId);
		RemovePendingActor(identity.ActorId);
		recordsByActorId[identity.ActorId] = identity;
		if (!actorIdsByFamilyStableId.TryGetValue(identity.FamilyStableIdValue, out HashSet<long> actorIds))
		{
			actorIds = new HashSet<long>();
			actorIdsByFamilyStableId[identity.FamilyStableIdValue] = actorIds;
		}

		actorIds.Add(identity.ActorId);
		XjFamilyBloodlineAggregateCache.InvalidateFamily(identity.FamilyStableIdValue);
		SyncIdentityIndex(identity, actor.data.sex == ActorSex.Male);
		PersistIdentityMirror(actor, identity);
		EnsureFamilyOriginCityMirror(actor);
		XjFamilySurnamePolicy.EnsureMemberSurname(actor, identity);
		XjFamilyMemberLedger.UpsertConfirmed(actor, identity, isNewFamilyConfirmation ? "family.confirmed" : "family.refresh");
		XjSongXuanEasterEggSystem.ObserveConfirmedFamilyMember(actor, identity, GetCurrentYear(actor));
		XjBloodlineBirthRules.TryApplyForConfirmedFamily(actor, identity, GetCurrentYear(actor));
		XjCultivationSeed.TryApplyBloodlineSeedInheritance(actor);
		XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out _);
		if (isNewFamilyConfirmation)
		{
			if (IsBirthYear(actor))
			{
				XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.Birth(actor));
			}

			XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.FamilyMemberConfirmed(actor));
			PublishExistingInheritanceAssets(actor);
		}

		ProcessPendingChildren(identity.ActorId);
	}

	internal void ReconcileCityBranch(Actor actor, int currentYear)
	{
		if (actor?.data == null || XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !recordsByActorId.TryGetValue(actorId, out XjFamilyIdentity identity)
			|| !identity.Found
			|| identity.FamilyStableIdValue <= 0L)
		{
			return;
		}

		TryBranchForCityMigration(actor, actorId, identity, currentYear);
	}

	/// <summary>
	/// 读档后恢复：从 XjFamilyMemberLedger 中读取已持久化的家族归属，
	/// 同时回填 XjFamilyIdentityIndex，避免因索引为空导致子代角色
	/// 通过 TryMakeIndexedParentFamilyKey 找不到父系家族 key。
	/// </summary>
	private bool TryRestoreFromLedgerAfterLoad(Actor actor, long actorId)
	{
		if (!XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
			|| !entry.Found
			|| entry.FamilyStableId <= 0L)
		{
			return TryRestoreFromIdentityMirrorAfterLoad(actor, actorId);
		}

		int generation = entry.Generation > 0 ? entry.Generation : 1;
		XjFamilyIdentity identity = new XjFamilyIdentity(
			true,
			entry.ActorId,
			entry.FamilyStableId,
			generation,
			false,
			"LedgerImport");

		RemoveActorIdFromCurrentFamily(identity.ActorId);
		RemovePendingActor(identity.ActorId);
		recordsByActorId[identity.ActorId] = identity;
		if (!actorIdsByFamilyStableId.TryGetValue(identity.FamilyStableIdValue, out HashSet<long> actorIds))
		{
			actorIds = new HashSet<long>();
			actorIdsByFamilyStableId[identity.FamilyStableIdValue] = actorIds;
		}

		actorIds.Add(identity.ActorId);
		XjFamilyBloodlineAggregateCache.InvalidateFamily(identity.FamilyStableIdValue);

		bool isMale = actor.data.sex == ActorSex.Male;
		SyncIdentityIndex(identity, isMale);
		PersistIdentityMirror(actor, identity);
		EnsureFamilyOriginCityMirror(actor);

		// Ledger is already the persisted authority. Rewriting it during bootstrap
		// would change Source to family.ledger_import for every live member, mark the
		// whole archive dirty and trigger a needless full-blob commit after load.
		// Only rebuild runtime indexes here; pending children may now resolve against
		// the restored parent without mutating the parent's ledger row.
		ProcessPendingChildren(identity.ActorId);
		return true;
	}

	internal void RemoveActorFromFamily(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}

		RemoveActorIdFromCurrentFamily(actorId);
		recordsByActorId.Remove(actorId);
		ClearIdentityMirror(actor);
	}

	/// <summary>
	/// Runtime-only invalidation for a dead or unavailable actor. Historical family
	/// identity remains owned by XjFamilyMemberLedger/XjFamilyIdentityIndex; this method
	/// only removes the actor from the live membership and pending-resolution indexes.
	/// </summary>
	internal void ForgetRuntimeActor(long actorId)
	{
		if (actorId <= 0L) return;
		long familyId = 0L;
		if (recordsByActorId.TryGetValue(actorId, out XjFamilyIdentity identity) && identity.Found)
		{
			familyId = identity.FamilyStableIdValue;
		}
		RemoveActorIdFromCurrentFamily(actorId);
		recordsByActorId.Remove(actorId);
		RemovePendingActor(actorId);
		if (familyId > 0L)
		{
			XjRelationEntityRevisionStore.MarkFamily(familyId);
		}
	}

	/// <summary>
	/// 转世/外部重链接：将 actor 直接挂入指定家族，不触发领域事件。
	/// 用于转世后恢复家族归属等场景。
	/// </summary>
	internal void RelinkActorToFamily(Actor actor, long familyStableId)
	{
		if (actor?.data == null || familyStableId <= 0L)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}

		int generation = 1;
		if (XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry existingEntry)
			&& existingEntry.Found
			&& existingEntry.Generation > 0)
		{
			generation = existingEntry.Generation;
		}

		XjFamilyIdentity identity = new XjFamilyIdentity(
			true,
			actorId,
			familyStableId,
			generation,
			false,
			"ReincarnationLink");

		RemoveActorIdFromCurrentFamily(actorId);
		RemovePendingActor(actorId);
		recordsByActorId[actorId] = identity;
		if (!actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds))
		{
			actorIds = new HashSet<long>();
			actorIdsByFamilyStableId[familyStableId] = actorIds;
		}

		actorIds.Add(actorId);
		XjFamilyBloodlineAggregateCache.InvalidateFamily(familyStableId);

		bool isMale = actor.data.sex == ActorSex.Male;
		string familyKey = "actor:" + familyStableId.ToString(System.Globalization.CultureInfo.InvariantCulture);
		XjFamilyIdentityIndex.Register(
			actorId,
			new XjFamilyKey(true, familyKey, familyStableId, "ReincarnationLink"),
			isMale);
		PersistIdentityMirror(actor, identity);
		EnsureFamilyOriginCityMirror(actor);
		XjFamilySurnamePolicy.EnsureMemberSurname(actor, identity);

		// 写入账本但不发布领域事件
		XjFamilyMemberLedger.UpsertConfirmed(actor, identity, "family.reincarnation_link");
		ProcessPendingChildren(actorId);
	}

	internal void ExportPendingRecords(List<XjWorldArchiveFamilyPendingRecord> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (KeyValuePair<long, XjFamilyPendingRecord> entry in pendingRecordsByActorId)
		{
			XjFamilyPendingRecord pending = entry.Value;
			if (!pending.Found || pending.ActorId <= 0L)
			{
				continue;
			}

			records.Add(new XjWorldArchiveFamilyPendingRecord
			{
				ActorId = pending.ActorId,
				ActorName = pending.ActorName,
				ParentId1 = pending.ParentId1,
				ParentId2 = pending.ParentId2,
				FatherActorId = pending.FatherActorId,
				Reason = pending.Reason
			});
		}
	}

	internal void ImportPendingRecords(IReadOnlyList<XjWorldArchiveFamilyPendingRecord> records)
	{
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveFamilyPendingRecord record = records[i];
			if (record == null || record.ActorId <= 0L)
			{
				continue;
			}

			XjFamilyPendingRecord pendingRecord = new XjFamilyPendingRecord(
				true,
				record.ActorId,
				record.ActorName,
				record.ParentId1,
				record.ParentId2,
				record.FatherActorId,
				string.IsNullOrWhiteSpace(record.Reason) ? "ArchiveImport" : record.Reason);

			pendingRecordsByActorId[record.ActorId] = pendingRecord;

			HashSet<long> parentIds = new HashSet<long>();
			if (record.ParentId1 > 0L) parentIds.Add(record.ParentId1);
			if (record.ParentId2 > 0L) parentIds.Add(record.ParentId2);
			if (record.FatherActorId > 0L) parentIds.Add(record.FatherActorId);

			foreach (long parentId in parentIds)
			{
				if (parentId <= 0L || parentId == record.ActorId)
				{
					continue;
				}

				if (!pendingActorIdsByParentId.TryGetValue(parentId, out HashSet<long> pendingActorIds))
				{
					pendingActorIds = new HashSet<long>();
					pendingActorIdsByParentId[parentId] = pendingActorIds;
				}

				pendingActorIds.Add(record.ActorId);
			}
		}
	}

	internal IEnumerable<Actor> GetFamilyMembers(long familyId)
	{
		if (familyId <= 0L || !actorIdsByFamilyStableId.TryGetValue(familyId, out HashSet<long> actorIds) || actorIds.Count == 0)
		{
			yield break;
		}

		foreach (long actorId in actorIds)
		{
			if (TryResolveIndexedActor(actorId, out Actor actor))
			{
				yield return actor;
			}
		}
	}

	internal IReadOnlyList<long> GetRuntimeActorIdsSnapshot()
	{
		if (recordsByActorId.Count == 0) return Array.Empty<long>();
		long[] snapshot = new long[recordsByActorId.Count];
		recordsByActorId.Keys.CopyTo(snapshot, 0);
		return Array.AsReadOnly(snapshot);
	}

	internal IReadOnlyCollection<long> GetFamilyMemberIds(long familyId)
	{
		if (familyId <= 0L || !actorIdsByFamilyStableId.TryGetValue(familyId, out HashSet<long> actorIds) || actorIds.Count == 0)
		{
			return Array.Empty<long>();
		}

		return new List<long>(actorIds);
	}

	internal bool TryGetActor(long actorId, out Actor actor)
	{
		return TryResolveIndexedActor(actorId, out actor);
	}

	internal bool TryGetRecord(long actorId, out XjFamilyIdentity identity)
	{
		if (actorId <= 0L || !recordsByActorId.TryGetValue(actorId, out identity))
		{
			identity = XjFamilyIdentity.Empty;
			return false;
		}

		return true;
	}

	internal void Clear()
	{
		recordsByActorId.Clear();
		actorIdsByFamilyStableId.Clear();
		pendingActorIdsByParentId.Clear();
		pendingRecordsByActorId.Clear();
	}

	internal bool IsActorPending(long actorId)
	{
		if (actorId <= 0L)
		{
			return false;
		}

		return pendingRecordsByActorId.ContainsKey(actorId);
	}

	internal IReadOnlyList<XjFamilyMemberDisplayItem> BuildMemberDisplayItems(long familyId)
	{
		if (familyId <= 0L)
		{
			return Array.Empty<XjFamilyMemberDisplayItem>();
		}

		return XjFamilyDisplayProjection.BuildMemberItems(XjFamilyMemberLedger.ReadFamilyAlive(familyId));
	}


	internal IReadOnlyList<XjFamilyPendingDisplayItem> BuildPendingDisplayItems(long familyId, long focusedActorId)
	{
		if (pendingRecordsByActorId.Count == 0)
		{
			return Array.Empty<XjFamilyPendingDisplayItem>();
		}

		return XjFamilyDisplayProjection.BuildPendingItems(
			pendingRecordsByActorId.Values,
			record =>
			{
				bool belongsToFocusedActor = focusedActorId > 0L && record.ActorId == focusedActorId;
				bool belongsToFamily = familyId > 0L && PendingRecordTouchesFamily(record, familyId);
				return belongsToFocusedActor || belongsToFamily;
			});
	}

	internal IReadOnlyList<XjFamilyMarriageDisplayItem> BuildMarriageDisplayItems(long familyId, long focusedActorId)
	{
		return XjFamilyDisplayProjection.BuildMarriageItems();
	}

	private bool ShouldWaitForFather(Actor actor, long actorId)
	{
		if (!XjFamilyResolver.TryResolveInheritanceParentActorId(actor, out long fatherActorId)
			|| fatherActorId <= 0L
			|| fatherActorId == actorId)
		{
			return false;
		}

		// Paternal authority must not depend on runtime registration order. A live
		// father can legitimately exist before his FamilyMemberIndex row is rebuilt
		// (post-load, H3 runtime eviction/rebind, initial adults, or bounded seed order).
		// First read every persisted authority layer; if none exists, repair exactly
		// this live paternal chain before deciding that the child must remain pending.
		if (TryGetRecord(fatherActorId, out XjFamilyIdentity liveFatherIdentity)
			&& liveFatherIdentity.Found
			&& liveFatherIdentity.FamilyStableIdValue > 0L)
		{
			return false;
		}

		// For a live father, materialize the paternal record before allowing the child
		// to proceed. Merely seeing a ledger/index row is not enough: the resolver uses
		// the father's materialized family key/generation to keep the whole paternal
		// chain in the same family rather than treating the father as a new root.
		if (TryRepairLiveFatherIdentity(fatherActorId))
		{
			return false;
		}

		// Dead/unavailable fathers cannot be materialized as live actors. Their
		// persisted ledger/index remains sufficient authority for an already-known
		// paternal line and must not be downgraded to FatherPending.
		return !HasConfirmedFatherAuthority(fatherActorId);
	}

	private bool HasConfirmedFatherAuthority(long fatherActorId)
	{
		if (fatherActorId <= 0L) return false;

		if (TryGetRecord(fatherActorId, out XjFamilyIdentity fatherIdentity)
			&& fatherIdentity.Found
			&& fatherIdentity.FamilyStableIdValue > 0L)
		{
			return true;
		}

		if (XjFamilyMemberLedger.TryGetByActorId(fatherActorId, out XjFamilyMemberLedgerEntry ledgerEntry)
			&& ledgerEntry.Found
			&& ledgerEntry.FamilyStableId > 0L)
		{
			return true;
		}

		return XjFamilyIdentityIndex.TryGetByActorId(fatherActorId, out XjFamilyIdentityRecord indexedFather)
			&& indexedFather.Found
			&& indexedFather.IsMale
			&& indexedFather.RootActorId > 0L
			&& !string.IsNullOrWhiteSpace(indexedFather.FamilyKey);
	}

	private bool TryRepairLiveFatherIdentity(long fatherActorId)
	{
		if (fatherActorId <= 0L || _liveFatherRepairDepth >= MaximumLiveFatherRepairDepth)
		{
			return false;
		}

		if (!XjActorRegistry.ResolveKnownOrWorld(fatherActorId, out Actor father)
			|| !XjSafeCore.IsAliveActor(father)
			|| father?.data == null
			|| father.data.sex != ActorSex.Male
			|| XjLongShuSystem.IsExcludedFromInheritance(father))
		{
			return false;
		}

		// Suppress pending-child fan-out while repairing an ancestor. Otherwise a
		// ledger restore can synchronously re-enter the same child that triggered this
		// repair, causing duplicate confirmation work in one call stack. Siblings are
		// picked up by their normal bounded FamilyIdentityRepair pass.
		_liveFatherRepairDepth++;
		try
		{
			// Existing worlds should normally recover here without gameplay side effects.
			if (TryRestoreFromLedgerAfterLoad(father, fatherActorId)
				&& HasConfirmedFatherAuthority(fatherActorId))
			{
				return true;
			}

			// If the father is alive but was never materialized into the family index,
			// establish only this paternal chain. The recursion guard caps one repair at
			// eight generations; unresolved deeper ancestry remains pending and is retried
			// later by the existing bounded FamilyIdentityRepair cadence.
			AddActorToFamilyInternal(father, forceHighRealmConfirmation: false);
			return HasConfirmedFatherAuthority(fatherActorId);
		}
		finally
		{
			_liveFatherRepairDepth--;
		}
	}

	private static bool IsBirthYear(Actor actor)
	{
		if (actor == null)
		{
			return false;
		}

		return (int)System.Math.Floor(System.Math.Max(0f, actor.getAge())) <= 1;
	}

	private static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}

	private static void PublishExistingInheritanceAssets(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjGongFaProgression.PublishInheritanceSnapshot(actor, "FamilyConfirmedSnapshot");

		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		if (qiuJinFa.Found)
		{
			XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.QiuJinFaComprehended(
				actor,
				qiuJinFa.Name,
				qiuJinFa.SourceGongFaName,
				qiuJinFa.SourceGongFaGrade,
				qiuJinFa.SourceDaoTu,
				string.Empty,
				qiuJinFa.BoundAuthority));
		}
	}

	private void MarkActorPendingByParents(Actor actor, long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		long parentId1 = actor?.data == null ? 0L : actor.data.parent_id_1;
		long parentId2 = actor?.data == null ? 0L : actor.data.parent_id_2;
		XjFamilyResolver.TryResolveInheritanceParentActorId(actor, out long fatherActorId);
		string reason = fatherActorId > 0L ? XjFamilyIdentityReasons.FatherPending : XjFamilyIdentityReasons.FatherMissing;
		string actorName = actor == null ? string.Empty : actor.getName();
		XjFamilyPendingRecord nextPending = new XjFamilyPendingRecord(
			true,
			actorId,
			actorName,
			parentId1,
			parentId2,
			fatherActorId,
			reason);
		bool pendingChanged = !pendingRecordsByActorId.TryGetValue(actorId, out XjFamilyPendingRecord previousPending)
			|| previousPending.ParentId1 != nextPending.ParentId1
			|| previousPending.ParentId2 != nextPending.ParentId2
			|| previousPending.FatherActorId != nextPending.FatherActorId
			|| !string.Equals(previousPending.ActorName, nextPending.ActorName, StringComparison.Ordinal)
			|| !string.Equals(previousPending.Reason, nextPending.Reason, StringComparison.Ordinal);
		pendingRecordsByActorId[actorId] = nextPending;
		if (pendingChanged)
		{
			XjActorStateRevisionStore.Mark(actorId, XjActorStateDomain.Family | XjActorStateDomain.Relations);
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Family);
		}

		HashSet<long> parentIds = new HashSet<long>();
		XjFamilyResolver.CollectParentIds(actor, parentIds);
		foreach (long parentId in parentIds)
		{
			if (parentId <= 0L || parentId == actorId)
			{
				continue;
			}

			if (!pendingActorIdsByParentId.TryGetValue(parentId, out HashSet<long> pendingActorIds))
			{
				pendingActorIds = new HashSet<long>();
				pendingActorIdsByParentId[parentId] = pendingActorIds;
			}

			pendingActorIds.Add(actorId);
		}
	}

	private void ProcessPendingChildren(long parentActorId)
	{
		// Ancestor repair is a targeted dependency materialization, not a fan-out
		// transaction. Avoid recursively re-entering children while the triggering
		// child is still on the stack; normal calls (depth 0) retain 0.9.8 behavior.
		if (_liveFatherRepairDepth > 0)
		{
			return;
		}

		if (parentActorId <= 0L || !pendingActorIdsByParentId.TryGetValue(parentActorId, out HashSet<long> pendingActorIds) || pendingActorIds.Count == 0)
		{
			return;
		}

		List<long> actorIds = new List<long>(pendingActorIds);
		pendingActorIdsByParentId.Remove(parentActorId);
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (TryResolveIndexedActor(actorIds[i], out Actor pendingActor))
			{
				AddActorToFamily(pendingActor);
			}
		}
	}

	private void RemovePendingActor(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		bool removedPending = pendingRecordsByActorId.Remove(actorId);
		if (removedPending)
		{
			XjActorStateRevisionStore.Mark(actorId, XjActorStateDomain.Family | XjActorStateDomain.Relations);
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Family);
		}
		List<long> emptyParentIds = null;
		foreach (KeyValuePair<long, HashSet<long>> entry in pendingActorIdsByParentId)
		{
			entry.Value.Remove(actorId);
			if (entry.Value.Count == 0)
			{
				emptyParentIds ??= new List<long>();
				emptyParentIds.Add(entry.Key);
			}
		}

		if (emptyParentIds == null)
		{
			return;
		}

		for (int i = 0; i < emptyParentIds.Count; i++)
		{
			pendingActorIdsByParentId.Remove(emptyParentIds[i]);
		}
	}


	private bool TryRestoreFromIdentityMirrorAfterLoad(Actor actor, long actorId)
	{
		if (!TryReadIdentityMirror(actor, out long familyStableId, out int generation)
			|| familyStableId <= 0L)
		{
			return false;
		}

		XjFamilyIdentity identity = new XjFamilyIdentity(
			true,
			actorId,
			familyStableId,
			generation > 0 ? generation : 1,
			false,
			"ActorIdentityMirror");
		RemoveActorIdFromCurrentFamily(actorId);
		RemovePendingActor(actorId);
		recordsByActorId[actorId] = identity;
		if (!actorIdsByFamilyStableId.TryGetValue(familyStableId, out HashSet<long> actorIds))
		{
			actorIds = new HashSet<long>();
			actorIdsByFamilyStableId[familyStableId] = actorIds;
		}
		actorIds.Add(actorId);
		XjFamilyBloodlineAggregateCache.InvalidateFamily(familyStableId);
		SyncIdentityIndex(identity, actor.data.sex == ActorSex.Male);
		EnsureFamilyOriginCityMirror(actor);
		XjFamilySurnamePolicy.EnsureMemberSurname(actor, identity);
		XjFamilyMemberLedger.UpsertConfirmed(actor, identity, "family.actor_identity_mirror");
		ProcessPendingChildren(actorId);
		return true;
	}

	private bool TryBranchForCityMigration(Actor actor, long actorId, in XjFamilyIdentity currentIdentity, int currentYear)
	{
		if (actor?.data == null
			|| actor.city?.data == null
			|| actorId <= 0L
			|| !currentIdentity.Found
			|| currentIdentity.FamilyStableIdValue <= 0L)
		{
			return false;
		}

		long currentCityId = ((BaseSystemData)actor.city.data).id;
		if (currentCityId <= 0L)
		{
			return false;
		}

		if (!TryReadFamilyOriginCity(actor, out long originCityId) || originCityId <= 0L)
		{
			PersistFamilyOriginCity(actor, currentCityId);
			return false;
		}

		if (originCityId == currentCityId)
		{
			return false;
		}

		if (!CanEstablishMigrationBranch(actor, currentIdentity, currentCityId))
		{
			return false;
		}

		long branchFamilyId = BuildBranchFamilyId(currentIdentity.FamilyStableIdValue, currentCityId);
		if (branchFamilyId <= 0L || branchFamilyId == currentIdentity.FamilyStableIdValue)
		{
			return false;
		}

		XjFamilyIdentity branchIdentity = new XjFamilyIdentity(
			true,
			actorId,
			branchFamilyId,
			1,
			false,
			XjFamilyIdentityReasons.CityMigrationBranch);

		RemoveActorIdFromCurrentFamily(actorId);
		RemovePendingActor(actorId);
		recordsByActorId[actorId] = branchIdentity;
		if (!actorIdsByFamilyStableId.TryGetValue(branchFamilyId, out HashSet<long> actorIds))
		{
			actorIds = new HashSet<long>();
			actorIdsByFamilyStableId[branchFamilyId] = actorIds;
		}

		actorIds.Add(actorId);
		XjFamilyBloodlineAggregateCache.InvalidateFamily(currentIdentity.FamilyStableIdValue);
		XjFamilyBloodlineAggregateCache.InvalidateFamily(branchFamilyId);
		SyncIdentityIndex(branchIdentity, actor.data.sex == ActorSex.Male, XjFamilyIdentityReasons.CityMigrationBranch);
		PersistIdentityMirror(actor, branchIdentity);
		PersistFamilyOriginCity(actor, currentCityId);
		PersistBranchSourceFamily(actor, currentIdentity.FamilyStableIdValue);
		XjFamilySurnamePolicy.EnsureMemberSurname(actor, branchIdentity);
		XjFamilyMemberLedger.UpsertConfirmed(actor, branchIdentity, XjFamilyMemberLedger.BuildBranchCityMigrationSource(currentIdentity.FamilyStableIdValue));
		XjBloodlineBirthRules.TryApplyForConfirmedFamily(actor, branchIdentity, currentYear > 0 ? currentYear : GetCurrentYear(actor));
		XjCultivationSeed.TryApplyBloodlineSeedInheritance(actor);
		XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out _);
		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.FamilyMemberConfirmed(actor));
		PublishExistingInheritanceAssets(actor);
		ProcessPendingChildren(actorId);
		return true;
	}

	private static bool CanEstablishMigrationBranch(Actor actor, in XjFamilyIdentity currentIdentity, long currentCityId)
	{
		if (actor?.data == null
			|| actor.data.sex != ActorSex.Male
			|| XjRealmHelper.GetOrder(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter))
				< XjRealmHelper.GetOrder(XjRealmIds.ZiFu)
			|| currentIdentity.Generation < 3
			|| currentIdentity.FamilyStableIdValue <= 0L
			|| currentCityId <= 0L
			|| !XjFamilyMemberLedger.TryGetAggregate(currentIdentity.FamilyStableIdValue, out XjFamilyLedgerAggregate aggregate)
			|| aggregate.AliveCount < 8
			|| aggregate.CultivatorCount < 2)
		{
			return false;
		}

		IReadOnlyList<XjFamilyMemberLedgerEntry> alive = XjFamilyMemberLedger.ReadFamilyAlive(currentIdentity.FamilyStableIdValue);
		int coResident = 0;
		int coResidentMale = 0;
		int coResidentCultivators = 0;
		for (int i = 0; i < alive.Count; i++)
		{
			XjFamilyMemberLedgerEntry entry = alive[i];
			if (!entry.Found || entry.ActorId <= 0L
				|| !XjScheduler.ResolveActor(entry.ActorId, out Actor member)
				|| member?.data == null || !member.isAlive()
				|| member.city?.data == null
				|| ((BaseSystemData)member.city.data).id != currentCityId) continue;
			coResident++;
			if (member.data.sex == ActorSex.Male) coResidentMale++;
			if (XjRealmHelper.GetOrder(entry.RealmId) > 0) coResidentCultivators++;
		}

		return coResident >= 3
			&& coResidentMale >= 2
			&& coResidentCultivators >= 1
			&& aggregate.AliveCount - coResident >= 4;
	}

	private static long BuildBranchFamilyId(long sourceFamilyId, long cityId)
	{
		if (sourceFamilyId <= 0L || cityId <= 0L)
		{
			return 0L;
		}

		long hash = XjDeterministicHash.PositiveHash(
			sourceFamilyId,
			"family.branch.city|" + cityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
		return BranchFamilyIdBase + hash % BranchFamilyIdSpan;
	}

	private static bool TryResolveMaternalLineageSuccession(
		Actor actor,
		long actorId,
		out XjFamilyIdentity identity,
		out int generation)
	{
		identity = XjFamilyIdentity.Empty;
		generation = 0;
		if (actor?.data == null || actorId <= 0L || actor.data.sex != ActorSex.Male) return false;

		long[] parentIds = { actor.data.parent_id_1, actor.data.parent_id_2 };
		XjFamilyMemberLedgerEntry mother = default;
		XjFamilyMemberLedgerEntry father = default;
		for (int i = 0; i < parentIds.Length; i++)
		{
			long parentId = parentIds[i];
			if (parentId <= 0L
				|| !XjActorRegistry.ResolveKnownOrWorld(parentId, out Actor parent)
				|| parent?.data == null
				|| !XjFamilyMemberLedger.TryGetByActorId(parentId, out XjFamilyMemberLedgerEntry entry)
				|| !entry.Found || entry.FamilyStableId <= 0L) continue;
			if (parent.data.sex == ActorSex.Female) mother = entry;
			else if (parent.data.sex == ActorSex.Male) father = entry;
		}

		if (!mother.Found || mother.FamilyStableId <= 0L
			|| mother.FamilyStableId == father.FamilyStableId
			|| !XjFamilyMemberLedger.TryGetAggregate(mother.FamilyStableId, out XjFamilyLedgerAggregate motherAggregate)
			|| motherAggregate.AliveCount > 3) return false;

		int motherHistorical = XjFamilyMemberLedger.GetHistoricalHighestRealmOrder(mother.FamilyStableId);
		if (motherHistorical < XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) return false;

		if (father.Found && father.FamilyStableId > 0L)
		{
			int fatherHistorical = XjFamilyMemberLedger.GetHistoricalHighestRealmOrder(father.FamilyStableId);
			if (fatherHistorical >= motherHistorical
				&& XjFamilyMemberLedger.TryGetAggregate(father.FamilyStableId, out XjFamilyLedgerAggregate fatherAggregate)
				&& fatherAggregate.AliveCount <= 3) return false;
		}

		generation = Math.Max(1, mother.Generation + 1);
		identity = new XjFamilyIdentity(true, actorId, mother.FamilyStableId, generation, false, XjFamilyIdentityReasons.MaternalLineageSuccession);
		return true;
	}

	private static void EnsureFamilyOriginCityMirror(Actor actor)
	{
		if (actor?.data == null || actor.city?.data == null)
		{
			return;
		}

		if (TryReadFamilyOriginCity(actor, out long existingCityId) && existingCityId > 0L)
		{
			return;
		}

		long cityId = ((BaseSystemData)actor.city.data).id;
		if (cityId > 0L)
		{
			PersistFamilyOriginCity(actor, cityId);
		}
	}

	private static bool TryReadFamilyOriginCity(Actor actor, out long cityId)
	{
		cityId = 0L;
		return actor?.data != null
			&& XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjFamilyOriginCityId, out cityId)
			&& cityId > 0L;
	}

	private static void PersistFamilyOriginCity(Actor actor, long cityId)
	{
		if (actor?.data != null && cityId > 0L)
		{
			XjActorAccessor.SetLong(actor, XjActorDataKeys.XjFamilyOriginCityId, cityId);
		}
	}

	private static void PersistBranchSourceFamily(Actor actor, long familyStableId)
	{
		if (actor?.data != null && familyStableId > 0L)
		{
			XjActorAccessor.SetLong(actor, XjActorDataKeys.XjFamilyBranchSourceFamilyId, familyStableId);
		}
	}

	private static bool TryReadIdentityMirror(Actor actor, out long familyStableId, out int generation)
	{
		familyStableId = 0L;
		generation = 0;
		if (actor?.data == null)
		{
			return false;
		}
		try
		{
			BaseSystemData data = (BaseSystemData)actor.data;
			data.get(XjFamilyIdentity.FamilyStableId, out familyStableId, 0L);
			data.get(XjFamilyIdentity.FamilyGeneration, out generation, 0);
			return familyStableId > 0L;
		}
		catch
		{
			familyStableId = 0L;
			generation = 0;
			return false;
		}
	}

	private static void PersistIdentityMirror(Actor actor, in XjFamilyIdentity identity)
	{
		if (actor?.data == null || !identity.Found || identity.FamilyStableIdValue <= 0L)
		{
			return;
		}
		try
		{
			using XjActorStateRevisionStore.ReductionScope reduction = XjActorStateRevisionStore.BeginReduction(identity.ActorId);
			XjActorStateWriteGateway.SetExternalLong(
				actor,
				XjFamilyIdentity.FamilyStableId,
				identity.FamilyStableIdValue,
				XjActorStateDomain.Family | XjActorStateDomain.Relations);
			XjActorStateWriteGateway.SetExternalInt(
				actor,
				XjFamilyIdentity.FamilyGeneration,
				Math.Max(1, identity.Generation),
				XjActorStateDomain.Family | XjActorStateDomain.Relations);
		}
		catch (System.Exception xjCaught1022) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Family/XjFamilyMemberIndex.cs:1022", xjCaught1022); }
	}

	private static void ClearIdentityMirror(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}
		try
		{
			long actorId = ((BaseSystemData)actor.data).id;
			using XjActorStateRevisionStore.ReductionScope reduction = XjActorStateRevisionStore.BeginReduction(actorId);
			XjActorStateWriteGateway.SetExternalLong(
				actor,
				XjFamilyIdentity.FamilyStableId,
				0L,
				XjActorStateDomain.Family | XjActorStateDomain.Relations);
			XjActorStateWriteGateway.SetExternalInt(
				actor,
				XjFamilyIdentity.FamilyGeneration,
				0,
				XjActorStateDomain.Family | XjActorStateDomain.Relations);
		}
		catch (System.Exception xjCaught1039) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Family/XjFamilyMemberIndex.cs:1039", xjCaught1039); }
	}

	private static void SyncIdentityIndex(in XjFamilyIdentity identity, bool isMale, string reasonCode = XjFamilyIdentityReasons.Confirmed)
	{
		if (!identity.Found || identity.ActorId <= 0L || identity.FamilyStableIdValue <= 0L)
		{
			return;
		}

		string familyKey = "actor:" + identity.FamilyStableIdValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
		XjFamilyIdentityIndex.Register(
			identity.ActorId,
			new XjFamilyKey(true, familyKey, identity.FamilyStableIdValue, string.IsNullOrWhiteSpace(reasonCode) ? XjFamilyIdentityReasons.Confirmed : reasonCode),
			isMale);
	}

	private bool PendingRecordTouchesFamily(in XjFamilyPendingRecord record, long familyId)
	{
		return ParentBelongsToFamily(record.ParentId1, familyId)
			|| ParentBelongsToFamily(record.ParentId2, familyId)
			|| ParentBelongsToFamily(record.FatherActorId, familyId);
	}

	private bool ParentBelongsToFamily(long parentId, long familyId)
	{
		return parentId > 0L
			&& familyId > 0L
			&& recordsByActorId.TryGetValue(parentId, out XjFamilyIdentity parentIdentity)
			&& parentIdentity.Found
			&& parentIdentity.FamilyStableIdValue == familyId;
	}



	private void RemoveActorIdFromCurrentFamily(long actorId)
	{
		if (actorId <= 0L || !recordsByActorId.TryGetValue(actorId, out XjFamilyIdentity identity))
		{
			return;
		}

		if (!actorIdsByFamilyStableId.TryGetValue(identity.FamilyStableIdValue, out HashSet<long> actorIds))
		{
			return;
		}

		actorIds.Remove(actorId);
		XjFamilyBloodlineAggregateCache.InvalidateFamily(identity.FamilyStableIdValue);
		if (actorIds.Count == 0)
		{
			actorIdsByFamilyStableId.Remove(identity.FamilyStableIdValue);
		}
	}

	private static bool TryResolveIndexedActor(long actorId, out Actor actor)
	{
		if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out actor) || actor?.data == null)
		{
			actor = null;
			return false;
		}
		try
		{
			if (actor.isAlive()) return true;
		}
		catch { }
		actor = null;
		return false;
	}
}
