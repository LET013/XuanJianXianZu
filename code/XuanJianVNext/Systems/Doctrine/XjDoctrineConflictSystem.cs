using System;
using System.Collections.Generic;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Doctrine;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Doctrine;

/// <summary>
/// 四道统宏观敌对层：定性“固有态度” + 可衰减“当世积怨”。
/// 只在既有年度角色管线中低频尝试主动索敌，不重写 WorldBox 寻路、伤害、
/// 国家外交、宗门战争或家族血仇。这样同道仍可互杀，异道也不会见面即开战。
/// </summary>
internal static class XjDoctrineConflictSystem
{
	private const int MaximumGrievance = 70;
	private const int DecayYears = 100;
	private const int DecayAmount = 5;
	private const int MaximumRecentEvents = 36;
	private const int CandidateBudget = 18;
	private const int RivalryThreshold = 40;
	private const int OpenHostilityThreshold = 60;
	private const int DaoWarThreshold = 80;

	private static readonly Dictionary<string, XjDoctrineGrievanceArchiveRecord> Relations =
		new Dictionary<string, XjDoctrineGrievanceArchiveRecord>(StringComparer.Ordinal);
	private static readonly List<XjDoctrineConflictEventArchiveRecord> RecentEvents =
		new List<XjDoctrineConflictEventArchiveRecord>(MaximumRecentEvents);

	internal static bool HasAnnualInterest(in XjAnnualSecondaryContext context)
	{
		if (context.Actor?.data == null || context.RealmTier < XjRealmSuppression.TierZhuJi) return false;
		if (!XjDoctrineRules.TryResolve(context.Actor, out string sourceDoctrineId)) return false;
		return GetMaximumHostilityFrom(sourceDoctrineId, context.AnnualYear) >= RivalryThreshold;
	}

	internal static void TickAnnual(in XjAnnualSecondaryContext context)
	{
		Actor actor = context.Actor;
		int currentYear = context.AnnualYear;
		if (actor?.data == null || currentYear <= 0 || !actor.isAlive()
			|| context.RealmTier < XjRealmSuppression.TierZhuJi
			|| XjYinSiTraitLifecycle.IsYinSi(actor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor)
			|| XjActorAggroBridge.HasValidCurrentTarget(actor)
			|| !XjDoctrineRules.TryResolve(actor, out string sourceDoctrineId))
		{
			return;
		}

		int maximumHostility = GetMaximumHostilityFrom(sourceDoctrineId, currentYear);
		if (maximumHostility < RivalryThreshold) return;

		bool daoWar = maximumHostility >= DaoWarThreshold;
		bool openHostility = maximumHostility >= OpenHostilityThreshold;
		int intervalYears = daoWar ? 3 : openHostility ? 5 : 10;
		int chancePercent = daoWar ? 28 : openHostility ? 12 : 4;
		int sourceMinimumTier = daoWar ? XjRealmSuppression.TierZhuJi : XjRealmSuppression.TierZiFu;
		int activeThreshold = daoWar ? DaoWarThreshold : openHostility ? OpenHostilityThreshold : RivalryThreshold;
		if (context.RealmTier < sourceMinimumTier || currentYear % intervalYears != 0) return;

		long actorId = context.ActorId;
		if (actorId <= 0L
			|| XjDeterministicHash.PositiveIndex(actorId + currentYear, "doctrine_hostility_trigger", 100) >= chancePercent)
		{
			return;
		}

		IReadOnlyList<long> ids = XjCultivatorCache.GetAllIds();
		if (ids == null || ids.Count <= 1) return;

		long actorFamilyId = 0L;
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out actorFamilyId);
		long actorSectId = 0L;
		XjSectAuthorityStore.TryGetSectId(actorId, out actorSectId);

		int start = XjDeterministicHash.PositiveIndex(actorId + currentYear, "doctrine_hostility_target", ids.Count);
		int limit = Math.Min(ids.Count, CandidateBudget);
		Actor selected = null;
		int selectedHostility = -1;
		for (int offset = 0; offset < limit; offset++)
		{
			long targetId = ids[(start + offset) % ids.Count];
			int targetMinimumTier = daoWar ? XjRealmSuppression.TierLianQi
				: openHostility ? XjRealmSuppression.TierZhuJi : XjRealmSuppression.TierZiFu;
			if (targetId <= 0L || targetId == actorId
				|| !XjCultivatorCache.TryGetRealmTier(targetId, out int targetTier)
				|| targetTier < targetMinimumTier
				|| !XjActorRegistry.ResolveKnownOrWorld(targetId, out Actor target)
				|| target?.data == null || !target.isAlive()
				|| XjYinSiTraitLifecycle.IsYinSi(target)
				|| XjTrueDamageSystem.IsJinXingYaoXie(target)
				|| !XjDoctrineRules.TryResolve(target, out string targetDoctrineId)
				|| string.Equals(sourceDoctrineId, targetDoctrineId, StringComparison.Ordinal))
			{
				continue;
			}

			if (actorFamilyId > 0L
				&& XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(targetId, out long targetFamilyId)
				&& targetFamilyId == actorFamilyId)
			{
				continue;
			}
			if (actorSectId > 0L
				&& XjSectAuthorityStore.TryGetSectId(targetId, out long targetSectId)
				&& targetSectId == actorSectId)
			{
				continue;
			}

			int hostility = GetFinalHostility(sourceDoctrineId, targetDoctrineId, currentYear);
			if (hostility < activeThreshold || hostility <= selectedHostility) continue;
			selected = target;
			selectedHostility = hostility;
		}

		if (selected == null) return;
		actor.attackedBy = selected;
		XjActorAggroBridge.ForceAggro(actor, selected);
	}

	internal static void OnConfirmedDeath(Actor victim, in XjDeathSnapshot snapshot, XjDeathCause cause)
	{
		if (victim?.data == null || !snapshot.Found || snapshot.ActorId <= 0L
			|| snapshot.LastAttackerId <= 0L || snapshot.LastAttackerId == snapshot.ActorId
			|| (cause != XjDeathCause.Combat && cause != XjDeathCause.ScriptedFinality)
			|| !XjDoctrineRules.TryResolve(victim, out string victimDoctrineId)
			|| !XjActorRegistry.ResolveKnownOrWorld(snapshot.LastAttackerId, out Actor killer)
			|| killer?.data == null || !killer.isAlive()
			|| !XjDoctrineRules.TryResolve(killer, out string killerDoctrineId)
			|| string.Equals(victimDoctrineId, killerDoctrineId, StringComparison.Ordinal))
		{
			return;
		}

		int severity = ResolveDeathSeverity(victim);
		int year = snapshot.Year > 0 ? snapshot.Year : Math.Max(1, XjYearTracker.CurrentYear);
		string victimName = string.IsNullOrWhiteSpace(snapshot.Name) ? "异道修士" : snapshot.Name.Trim();
		string killerName = string.IsNullOrWhiteSpace(snapshot.LastAttackerName) ? killer.getName() : snapshot.LastAttackerName.Trim();
		if (string.IsNullOrWhiteSpace(killerName)) killerName = "异道修士";
		string reason = victimName + "陨于" + killerName + "之手";

		// 被杀一方积怨最深；主动杀人的一方也因冲突公开化积累较小的反向敌意。
		AddGrievance(victimDoctrineId, killerDoctrineId, severity, year, reason);
		AddGrievance(killerDoctrineId, victimDoctrineId, Math.Max(1, severity / 3), year, reason);
	}

	internal static int GetFinalHostility(string sourceDoctrineId, string targetDoctrineId, int currentYear)
	{
		return Math.Max(0, Math.Min(100, GetEffectiveGrievance(sourceDoctrineId, targetDoctrineId, currentYear)));
	}

	internal static IReadOnlyList<XjDoctrineRelationSnapshot> ReadRelationSnapshot(int currentYear)
	{
		List<XjDoctrineRelationSnapshot> result = new List<XjDoctrineRelationSnapshot>(12);
		for (int sourceIndex = 0; sourceIndex < XjDoctrineIds.Ordered.Length; sourceIndex++)
		{
			string sourceId = XjDoctrineIds.Ordered[sourceIndex];
			for (int targetIndex = 0; targetIndex < XjDoctrineIds.Ordered.Length; targetIndex++)
			{
				string targetId = XjDoctrineIds.Ordered[targetIndex];
				if (string.Equals(sourceId, targetId, StringComparison.Ordinal)) continue;
				int baseHostility = 0;
				int grievance = GetEffectiveGrievance(sourceId, targetId, currentYear);
				Relations.TryGetValue(BuildKey(sourceId, targetId), out XjDoctrineGrievanceArchiveRecord record);
				int finalHostility = Math.Max(0, Math.Min(100, grievance));
				result.Add(new XjDoctrineRelationSnapshot
				{
					SourceDoctrineId = sourceId,
					SourceDoctrineName = XjDoctrineRules.GetDisplayName(sourceId),
					TargetDoctrineId = targetId,
					TargetDoctrineName = XjDoctrineRules.GetDisplayName(targetId),
					BaseHostility = baseHostility,
					Grievance = grievance,
					FinalHostility = finalHostility,
					Status = XjDoctrineRules.GetStatus(finalHostility),
					LastChangedYear = record?.LastChangedYear ?? 0,
					LastReason = record?.LastReason ?? string.Empty
				});
			}
		}
		return result;
	}

	internal static IReadOnlyList<XjDoctrineConflictEventSnapshot> ReadRecentEventSnapshot(int limit)
	{
		if (limit <= 0 || RecentEvents.Count == 0) return Array.Empty<XjDoctrineConflictEventSnapshot>();
		int count = Math.Min(limit, RecentEvents.Count);
		List<XjDoctrineConflictEventSnapshot> result = new List<XjDoctrineConflictEventSnapshot>(count);
		for (int offset = 0; offset < count; offset++)
		{
			XjDoctrineConflictEventArchiveRecord record = RecentEvents[RecentEvents.Count - 1 - offset];
			result.Add(new XjDoctrineConflictEventSnapshot
			{
				Year = record.Year,
				SourceDoctrineId = record.SourceDoctrineId,
				SourceDoctrineName = XjDoctrineRules.GetDisplayName(record.SourceDoctrineId),
				TargetDoctrineId = record.TargetDoctrineId,
				TargetDoctrineName = XjDoctrineRules.GetDisplayName(record.TargetDoctrineId),
				Delta = record.Delta,
				Reason = record.Reason ?? string.Empty
			});
		}
		return result;
	}

	internal static XjDoctrineConflictArchiveData ExportState()
	{
		XjDoctrineConflictArchiveData state = new XjDoctrineConflictArchiveData();
		foreach (XjDoctrineGrievanceArchiveRecord record in Relations.Values)
		{
			if (record == null || !IsValidRelation(record.SourceDoctrineId, record.TargetDoctrineId)) continue;
			state.Relations.Add(CloneRelation(record));
		}
		state.Relations.Sort((left, right) => string.CompareOrdinal(
			BuildKey(left.SourceDoctrineId, left.TargetDoctrineId),
			BuildKey(right.SourceDoctrineId, right.TargetDoctrineId)));
		for (int i = 0; i < RecentEvents.Count; i++)
		{
			XjDoctrineConflictEventArchiveRecord record = RecentEvents[i];
			if (record == null || !IsValidRelation(record.SourceDoctrineId, record.TargetDoctrineId)) continue;
			state.RecentEvents.Add(CloneEvent(record));
		}
		return state;
	}

	internal static void ImportState(XjDoctrineConflictArchiveData state)
	{
		ClearRuntime();
		if (state == null) return;
		if (state.Relations != null)
		{
			for (int i = 0; i < state.Relations.Count; i++)
			{
				XjDoctrineGrievanceArchiveRecord source = state.Relations[i];
				if (source == null || !IsValidRelation(source.SourceDoctrineId, source.TargetDoctrineId)) continue;
				XjDoctrineGrievanceArchiveRecord copy = CloneRelation(source);
				copy.Grievance = Math.Max(0, Math.Min(MaximumGrievance, copy.Grievance));
				copy.LastChangedYear = Math.Max(0, copy.LastChangedYear);
				Relations[BuildKey(copy.SourceDoctrineId, copy.TargetDoctrineId)] = copy;
			}
		}
		if (state.RecentEvents != null)
		{
			int start = Math.Max(0, state.RecentEvents.Count - MaximumRecentEvents);
			for (int i = start; i < state.RecentEvents.Count; i++)
			{
				XjDoctrineConflictEventArchiveRecord source = state.RecentEvents[i];
				if (source == null || !IsValidRelation(source.SourceDoctrineId, source.TargetDoctrineId)) continue;
				RecentEvents.Add(CloneEvent(source));
			}
		}
	}

	internal static void ClearRuntime()
	{
		Relations.Clear();
		RecentEvents.Clear();
	}

	private static void AddGrievance(
		string sourceDoctrineId,
		string targetDoctrineId,
		int delta,
		int year,
		string reason)
	{
		if (delta <= 0 || !IsValidRelation(sourceDoctrineId, targetDoctrineId)) return;
		int safeYear = Math.Max(1, year);
		string key = BuildKey(sourceDoctrineId, targetDoctrineId);
		if (!Relations.TryGetValue(key, out XjDoctrineGrievanceArchiveRecord record) || record == null)
		{
			record = new XjDoctrineGrievanceArchiveRecord
			{
				SourceDoctrineId = sourceDoctrineId,
				TargetDoctrineId = targetDoctrineId,
				Grievance = 0,
				LastChangedYear = safeYear
			};
			Relations[key] = record;
		}

		int oldFinal = GetFinalHostility(sourceDoctrineId, targetDoctrineId, safeYear);
		int effective = GetEffectiveGrievance(record, safeYear);
		record.Grievance = Math.Min(MaximumGrievance, effective + delta);
		record.LastChangedYear = safeYear;
		record.LastReason = string.IsNullOrWhiteSpace(reason) ? "异道冲突" : reason.Trim();
		int actualDelta = record.Grievance - effective;
		if (actualDelta <= 0) return;

		RecentEvents.Add(new XjDoctrineConflictEventArchiveRecord
		{
			Year = safeYear,
			SourceDoctrineId = sourceDoctrineId,
			TargetDoctrineId = targetDoctrineId,
			Delta = actualDelta,
			Reason = record.LastReason
		});
		while (RecentEvents.Count > MaximumRecentEvents) RecentEvents.RemoveAt(0);

		int newFinal = Math.Max(0, Math.Min(100, record.Grievance));
		RecordThresholdCrossing(sourceDoctrineId, targetDoctrineId, oldFinal, newFinal, safeYear, record.LastReason);
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.World
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Conflict
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
	}

	private static void RecordThresholdCrossing(
		string sourceDoctrineId,
		string targetDoctrineId,
		int oldFinal,
		int newFinal,
		int year,
		string reason)
	{
		int crossed = 0;
		if (oldFinal < 80 && newFinal >= 80) crossed = 80;
		else if (oldFinal < 60 && newFinal >= 60) crossed = 60;
		else if (oldFinal < 40 && newFinal >= 40) crossed = 40;
		if (crossed <= 0) return;

		string sourceName = XjDoctrineRules.GetDisplayName(sourceDoctrineId);
		string targetName = XjDoctrineRules.GetDisplayName(targetDoctrineId);
		string title = crossed >= 80 ? "【道争将起】" : crossed >= 60 ? "【道统交恶】" : "【道统相争】";
		string body = sourceName + "视" + targetName + "之势已至“" + XjDoctrineRules.GetStatus(newFinal)
			+ "”（" + newFinal + "）。" + (string.IsNullOrWhiteSpace(reason) ? string.Empty : "近因：" + reason + "。")
			+ "此为道统宏观立场，不覆盖既有宗门、家族与个人恩怨。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Cultivation,
			title,
			body,
			crossed >= 80 ? 4 : crossed >= 60 ? 3 : 2,
			year: year,
			eventType: "DoctrineHostilityChanged",
			visibilityFlags: (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			result: XjHistoryResult.Change,
			mirrorToWorldLog: false);
	}

	private static int ResolveDeathSeverity(Actor victim)
	{
		int tier = XjRealmSuppression.GetRealmTier(victim);
		if (tier >= XjRealmSuppression.TierDaoTai) return 25;
		if (tier >= XjRealmSuppression.TierJinDan) return 15;
		if (tier >= XjRealmSuppression.TierZiFu) return 5;
		if (tier >= XjRealmSuppression.TierZhuJi) return 2;
		return 1;
	}

	private static int GetMaximumHostilityFrom(string sourceDoctrineId, int currentYear)
	{
		int maximum = 0;
		for (int i = 0; i < XjDoctrineIds.Ordered.Length; i++)
		{
			string targetDoctrineId = XjDoctrineIds.Ordered[i];
			if (string.Equals(sourceDoctrineId, targetDoctrineId, StringComparison.Ordinal)) continue;
			maximum = Math.Max(maximum, GetFinalHostility(sourceDoctrineId, targetDoctrineId, currentYear));
		}
		return maximum;
	}

	private static int GetEffectiveGrievance(string sourceDoctrineId, string targetDoctrineId, int currentYear)
	{
		return Relations.TryGetValue(BuildKey(sourceDoctrineId, targetDoctrineId), out XjDoctrineGrievanceArchiveRecord record)
			? GetEffectiveGrievance(record, currentYear)
			: 0;
	}

	private static int GetEffectiveGrievance(XjDoctrineGrievanceArchiveRecord record, int currentYear)
	{
		if (record == null || record.Grievance <= 0) return 0;
		int year = Math.Max(0, currentYear);
		if (record.LastChangedYear <= 0 || year <= record.LastChangedYear) return Math.Min(MaximumGrievance, record.Grievance);
		int steps = (year - record.LastChangedYear) / DecayYears;
		return Math.Max(0, Math.Min(MaximumGrievance, record.Grievance - steps * DecayAmount));
	}

	private static bool IsValidRelation(string sourceDoctrineId, string targetDoctrineId)
	{
		return XjDoctrineIds.IsKnown(sourceDoctrineId)
			&& XjDoctrineIds.IsKnown(targetDoctrineId)
			&& !string.Equals(sourceDoctrineId, targetDoctrineId, StringComparison.Ordinal);
	}

	private static string BuildKey(string sourceDoctrineId, string targetDoctrineId)
	{
		return (sourceDoctrineId ?? string.Empty) + ">" + (targetDoctrineId ?? string.Empty);
	}

	private static XjDoctrineGrievanceArchiveRecord CloneRelation(XjDoctrineGrievanceArchiveRecord source)
	{
		return new XjDoctrineGrievanceArchiveRecord
		{
			SourceDoctrineId = source.SourceDoctrineId ?? string.Empty,
			TargetDoctrineId = source.TargetDoctrineId ?? string.Empty,
			Grievance = source.Grievance,
			LastChangedYear = source.LastChangedYear,
			LastReason = source.LastReason ?? string.Empty
		};
	}

	private static XjDoctrineConflictEventArchiveRecord CloneEvent(XjDoctrineConflictEventArchiveRecord source)
	{
		return new XjDoctrineConflictEventArchiveRecord
		{
			Year = Math.Max(0, source.Year),
			SourceDoctrineId = source.SourceDoctrineId ?? string.Empty,
			TargetDoctrineId = source.TargetDoctrineId ?? string.Empty,
			Delta = Math.Max(0, source.Delta),
			Reason = source.Reason ?? string.Empty
		};
	}
}
