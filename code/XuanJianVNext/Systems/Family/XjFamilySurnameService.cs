using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.History.Books;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 真正的家族改姓事务。
/// 与原生命名导致的“外姓纠错”不同：改姓会更新家族级姓氏权威、所有在世族人和后续新生者，
/// 但绝不回写已经死亡的先祖姓名，也不改变 FamilyStableId、血脉、仓库、宗门席位与历史事件主键。
/// </summary>
internal static class XjFamilySurnameService
{
	internal static bool TryChangeFamilySurname(long familyStableId, string requestedSurname, out string message)
	{
		message = string.Empty;
		if (!XjWorldSchemaGuard.GameplayEnabled)
		{
			message = "当前世界未启用玄鉴玩法。";
			return false;
		}
		if (familyStableId <= 0L)
		{
			message = "未找到家族。";
			return false;
		}
		if (!XjFamilySurnameRegistry.IsValidRequestedSurname(requestedSurname, out string nextSurname))
		{
			message = "姓氏须为一至两个汉字。";
			return false;
		}

		if (!XjFamilySurnamePolicy.TryGetCanonicalSurname(familyStableId, out string currentSurname)
			|| string.IsNullOrWhiteSpace(currentSurname))
		{
			message = "该家族尚未建立可用的姓氏权威。";
			return false;
		}
		currentSurname = XjFamilySurnameRegistry.EnsureEstablished(
			familyStableId,
			currentSurname,
			GetCurrentYear());
		if (string.Equals(currentSurname, nextSurname, StringComparison.Ordinal))
		{
			message = "家族姓氏未变。";
			return true;
		}

		ResolveInitiator(familyStableId, out long initiatorActorId, out string initiatorActorName);
		int year = GetCurrentYear();
		if (!XjFamilySurnameRegistry.TryChange(
			familyStableId,
			nextSurname,
			year,
			"PlayerFamilyRename",
			initiatorActorId,
			initiatorActorName,
			out string previousSurname,
			out string normalizedSurname))
		{
			message = "家族改姓未能写入。";
			return false;
		}

		XjFamilySurnamePolicy.SetCanonicalSurname(familyStableId, normalizedSurname);
		ApplyToLivingMembers(familyStableId, normalizedSurname);
		// 账本层只改在世成员。历史先祖姓名以及改姓前的三书/世界史快照保持原样。
		XjFamilyMemberLedger.ReconcileFamilySurname(familyStableId, normalizedSurname, includeHistorical: false);
		RecordRenameHistory(
			familyStableId,
			previousSurname,
			normalizedSurname,
			year,
			initiatorActorId,
			initiatorActorName);

		XjFamilyBloodlineAggregateCache.InvalidateFamily(familyStableId);
		XjRelationEntityRevisionStore.MarkFamily(familyStableId);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Family
			| XjCodexDirtyFlags.City
			| XjCodexDirtyFlags.Sect
			| XjCodexDirtyFlags.CenturyAnnals
			| XjCodexDirtyFlags.History);
		message = previousSurname + "氏已改为" + normalizedSurname + "氏；在世族人随族改姓，已故先祖保留旧名。";
		return true;
	}

	internal static string ResolveCurrentSurname(long familyStableId)
	{
		if (XjFamilySurnameRegistry.TryGetCurrentSurname(familyStableId, out string surname)) return surname;
		return XjFamilySurnamePolicy.TryGetCanonicalSurname(familyStableId, out surname) ? surname : string.Empty;
	}

	private static void ApplyToLivingMembers(long familyStableId, string surname)
	{
		// 运行时索引与持久账本取并集：正常情况下两者一致；若刚出生/刚读档角色
		// 尚未来得及写入其中一侧，也不能在正式改姓时漏掉。
		HashSet<long> actorIds = new HashSet<long>();
		IReadOnlyCollection<long> indexedIds = XjFamilyMemberIndex.Shared.GetFamilyMemberIds(familyStableId);
		if (indexedIds != null)
		{
			foreach (long actorId in indexedIds) if (actorId > 0L) actorIds.Add(actorId);
		}

		IReadOnlyList<XjFamilyMemberLedgerEntry> alive = XjFamilyMemberLedger.ReadFamilyAlive(familyStableId);
		if (alive != null)
		{
			for (int i = 0; i < alive.Count; i++)
			{
				if (alive[i].Found && alive[i].ActorId > 0L) actorIds.Add(alive[i].ActorId);
			}
		}

		foreach (long actorId in actorIds)
		{
			if (!XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null) continue;
			// 必须重新核实当前血脉归属，防止婚嫁/分家后的旧账本条目误改他族。
			// 若刚读档运行时索引尚未恢复，先按已持久化身份补回索引再判断。
			if (!XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity identity)
				|| !identity.Found)
			{
				XjFamilyMemberIndex.Shared.RestoreRuntimeIndexAfterLoad(actor);
				XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out identity);
			}
			if (!identity.Found || identity.FamilyStableIdValue != familyStableId) continue;
			XjFamilySurnamePolicy.ApplyCanonicalSurnameForFamilyChange(actor, surname);
			XjFamilyMemberLedger.UpsertConfirmed(actor, identity, "family.surname_change");
		}
	}

	private static void ResolveInitiator(long familyStableId, out long actorId, out string actorName)
	{
		actorId = 0L;
		actorName = string.Empty;
		if (XjFamilyMemberLedger.TryGetAggregate(familyStableId, out XjFamilyLedgerAggregate aggregate))
		{
			actorId = aggregate.RepresentativeActorId;
		}
		if (actorId <= 0L)
		{
			IReadOnlyList<XjFamilyMemberLedgerEntry> alive = XjFamilyMemberLedger.ReadFamilyAlive(familyStableId);
			if (alive != null && alive.Count > 0) actorId = alive[0].ActorId;
		}
		if (actorId > 0L && XjScheduler.ResolveActor(actorId, out Actor actor) && actor?.data != null)
		{
			actorName = actor.getName() ?? string.Empty;
		}
		if (string.IsNullOrWhiteSpace(actorName)
			&& actorId > 0L
			&& XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found)
		{
			actorName = entry.Name ?? string.Empty;
		}
	}

	private static void RecordRenameHistory(
		long familyStableId,
		string previousSurname,
		string nextSurname,
		int year,
		long actorId,
		string actorName)
	{
		string oldFamilyName = previousSurname + "氏";
		string newFamilyName = nextSurname + "氏";
		string body = oldFamilyName + "于" + XjChronology.FormatYear(year) + "易姓" + newFamilyName
			+ "。族脉、世次与家业仍承旧谱，自此在世族人与后世新丁皆从新姓；旧世先祖名讳不改。";
		if (!string.IsNullOrWhiteSpace(actorName)) body = actorName + "主其事，" + body;

		if (actorId > 0L)
		{
			XjFamilyChronicleMemory.Shared.Append(
				new XjChronicleEvent(
					true,
					familyStableId,
					actorId,
					XjChronicleEventTypes.FamilySurnameChanged,
					year,
					"易姓续谱",
					body,
					3,
					true,
					false,
					false,
					"FamilySurnameChanged",
					"PlayerFamilyRename"),
				"family|surname-change|" + familyStableId + "|" + year + "|" + nextSurname);
		}
		XjThreeBookWriter.RecordFamilySurnameChanged(
			familyStableId,
			oldFamilyName,
			newFamilyName,
			previousSurname,
			nextSurname,
			year,
			actorId,
			actorName);
	}

	private static int GetCurrentYear()
	{
		try { return Math.Max(0, World.world?.map_stats?.year ?? 0); }
		catch { return 0; }
	}
}
