using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Warehouse;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 世家族议的唯一持久状态入口。家族一旦选定重点扶持后辈便长期绑定；
/// 只有原对象死亡/离族失效，或新出现的后辈拥有严格更高的稳定天赋分时才允许换人。
/// 5/10年只控制实际资源扶持的发放节奏，不再参与扶持对象轮换；高境长辈的主动干预统一压到50年级。
/// </summary>
internal static class XjFamilySupportSystem
{
	private const int FamilyPeriodicScanYears = 5;
	private const int HighRealmInfluenceIntervalYears = 50;
	private const int HighRealmOverrideChancePercent = 25;

	internal static bool TryResolveClanLeaderActorId(long familyStableId, out long actorId)
	{
		actorId = 0L;
		if (familyStableId <= 0L) return false;

		if (XjCenturyAnnalsStore.TryGetFamilyStageState(familyStableId, out XjCenturyFamilyStageStateRecord state)
			&& IsStoredClanLeaderValid(state))
		{
			actorId = state.LastClanLeaderActorId;
			return actorId > 0L;
		}

		if (XjFamilyMemberLedger.TryGetAggregate(familyStableId, out XjFamilyLedgerAggregate aggregate)
			&& aggregate.RepresentativeActorId > 0L
			&& XjFamilyMemberLedger.TryGetByActorId(aggregate.RepresentativeActorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found
			&& entry.IsAlive
			&& entry.FamilyStableId == familyStableId)
		{
			actorId = aggregate.RepresentativeActorId;
			return true;
		}
		return false;
	}

	private const int FamilyBatchBaseCount = 16;
	private const double FamilyBatchBaseMilliseconds = 0.30d;
	private static IReadOnlyList<XjCenturyFamilyStageStateRecord> _pendingStates = Array.Empty<XjCenturyFamilyStageStateRecord>();
	private static readonly List<XjCenturyFamilyStageStateRecord> PendingStateBuffer = new List<XjCenturyFamilyStageStateRecord>();
	private static readonly Dictionary<long, long> UrgentBetterTalentActorByFamily = new Dictionary<long, long>();
	private static int _pendingYear;
	private static int _pendingIndex;
	private static bool _pendingChanged;

	internal static bool HasPending => _pendingYear > 0 && _pendingIndex < (_pendingStates?.Count ?? 0);
	internal static bool HasPendingForYear(int year) => year > 0 && _pendingYear == year && HasPending;

	/// <summary>
	/// 一至六档资质在真正写入角色时只做一次稳定天赋评分：已有扶持对象时只允许严格更高者触发换人；
	/// 尚无扶持对象时仍需达到天骄阈值才触发紧急族议。实际扶持继续由年度世界车道串行结算。
	/// </summary>
	internal static void NotifySupportTalentMember(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return;
		long actorId = GetActorId(actor);
		int talentScore = ResolveStableSupportTalentScore(actor);
		if (actorId <= 0L || talentScore <= 0
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
			|| familyId <= 0L)
		{
			return;
		}

		// 已有长期扶持对象：所有1~6档新资质都只比较稳定先天天赋，严格更高才允许换。
		// 没有现任对象：仍要求达到“天骄阈值”才触发紧急族议，普通后辈继续等既有家族族议择人。
		if (TryResolvePersistedSupportedActor(familyId, out Actor incumbent))
		{
			long incumbentId = GetActorId(incumbent);
			if (incumbentId == actorId || ResolveStableSupportTalentScore(incumbent) >= talentScore) return;
		}
		else if (talentScore < XjTalentOpportunityRules.FamilyProdigyMinimumScore)
		{
			return;
		}

		if (UrgentBetterTalentActorByFamily.TryGetValue(familyId, out long existingId)
			&& existingId > 0L
			&& XjScheduler.ResolveActor(existingId, out Actor existing)
			&& existing?.data != null
			&& existing.isAlive()
			&& ResolveStableSupportTalentScore(existing) >= talentScore)
		{
			return;
		}
		UrgentBetterTalentActorByFamily[familyId] = actorId;
	}

	/// <summary>
	/// Captures one annual family-support transaction. The ledger view is retained only while
	/// the current annual world stage is pending; gameplay semantics are unchanged, but the
	/// family loop can now yield between rendered frames.
	/// </summary>
	internal static void BeginYear(int currentYear)
	{
		if (currentYear <= 0)
		{
			ClearPending();
			return;
		}
		if (_pendingYear == currentYear && HasPending) return;

		ClearPendingTransaction();
		bool periodicDue = currentYear % FamilyPeriodicScanYears == 0;
		bool urgentDue = UrgentBetterTalentActorByFamily.Count > 0;
		if (!periodicDue && !urgentDue) return;

		_pendingYear = currentYear;

		// 器库对账降为五年主批次；天骄评分触发的紧急族议只处理目标家族，不再顺带扫仓库。
		if (periodicDue)
		{
			XjFamilyFaBaoWarehouse.ReconcileLiveBoundPrimaryTreasures(currentYear);
			XjFamilyFaBaoWarehouse.ReconcileExtinctFamilyTreasures(currentYear);
			XjFamilyFaBaoWarehouse.ReconcileExtinctSectTreasures(currentYear);
		}

		IReadOnlyList<XjCenturyFamilyStageStateRecord> allStates = XjCenturyAnnalsStore.ReadFamilyStageStateView()
			?? Array.Empty<XjCenturyFamilyStageStateRecord>();
		if (periodicDue)
		{
			_pendingStates = allStates;
		}
		else
		{
			PendingStateBuffer.Clear();
			for (int i = 0; i < allStates.Count; i++)
			{
				XjCenturyFamilyStageStateRecord state = allStates[i];
				if (state != null && UrgentBetterTalentActorByFamily.ContainsKey(state.FamilyStableId))
				{
					PendingStateBuffer.Add(state);
				}
			}
			_pendingStates = PendingStateBuffer;
		}

		_pendingIndex = 0;
		_pendingChanged = false;
		if (_pendingStates.Count == 0) CompletePending();
	}

	/// <summary>
	/// Advances the pending family stage under both an item cap and wall-clock cap.
	/// Returns true once the whole year's family-support transaction is complete.
	/// </summary>
	internal static bool TickPending()
	{
		if (_pendingYear <= 0) return true;
		if (!HasPending)
		{
			CompletePending();
			return true;
		}

		int itemBudget = XjRuntimeWorkBudget.ScaleCount(FamilyBatchBaseCount, minimum: 2);
		double timeBudgetMs = XjRuntimeWorkBudget.ScaleMilliseconds(FamilyBatchBaseMilliseconds, minimum: 0.08d);
		long started = System.Diagnostics.Stopwatch.GetTimestamp();
		int processed = 0;

		while (_pendingIndex < _pendingStates.Count && processed < itemBudget)
		{
			if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Background)) break;
			XjCenturyFamilyStageStateRecord state = _pendingStates[_pendingIndex++];
			_pendingChanged |= ProcessFamilyState(state, _pendingYear);
			processed++;
			if ((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency >= timeBudgetMs)
				break;
		}

		if (_pendingIndex < _pendingStates.Count) return false;
		CompletePending();
		return true;
	}

	internal static void ClearPending()
	{
		ClearPendingTransaction();
		UrgentBetterTalentActorByFamily.Clear();
	}

	private static void ClearPendingTransaction()
	{
		_pendingStates = Array.Empty<XjCenturyFamilyStageStateRecord>();
		PendingStateBuffer.Clear();
		_pendingYear = 0;
		_pendingIndex = 0;
		_pendingChanged = false;
	}

	private static void CompletePending()
	{
		bool changed = _pendingChanged;
		ClearPendingTransaction();
		UrgentBetterTalentActorByFamily.Clear();
		if (!changed) return;

		XjCenturyAnnalsStore.NotifyFamilyStageStateFieldsChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Family | XjCodexDirtyFlags.CenturyAnnals | XjCodexDirtyFlags.History);
	}

	private static bool ProcessFamilyState(XjCenturyFamilyStageStateRecord state, int currentYear)
	{
		if (state == null || state.FamilyStableId <= 0L) return false;
		bool changed = false;
		if (!XjFamilyMemberLedger.TryGetAggregate(state.FamilyStableId, out XjFamilyLedgerAggregate aggregate)
			|| aggregate.AliveCount <= 0)
		{
			changed |= ClearSupport(state);
			if (state.LastClanLeaderActorId != 0L)
			{
				state.LastClanLeaderActorId = 0L;
				changed = true;
			}
			return changed;
		}

		IReadOnlyList<XjFamilyMemberLedgerEntry> familyEntries = null;
		if (XjMingShuSchemeSystem.ShouldCheck(state.FamilyStableId, aggregate, currentYear))
		{
			familyEntries = XjFamilyMemberLedger.ReadFamilyAlive(state.FamilyStableId);
			XjMingShuSchemeSystem.TryResolve(state.FamilyStableId, familyEntries, currentYear);
		}
		if (XjHighRealmChessEventSystem.ShouldCheck(state.FamilyStableId, aggregate, currentYear))
		{
			familyEntries ??= XjFamilyMemberLedger.ReadFamilyAlive(state.FamilyStableId);
			XjHighRealmChessEventSystem.TryResolve(state.FamilyStableId, familyEntries, currentYear);
		}
		if (!IsStoredClanLeaderValid(state))
		{
			changed |= ObserveClanLeaderChange(state, currentYear, aggregate);
		}

		Actor incumbent = ResolveValidSupportedActor(state);
		bool urgentBetterTalent = TryResolveUrgentSupportTalentActor(state.FamilyStableId, out Actor urgentActor);
		bool replacementByBetterTalent = urgentBetterTalent
			&& incumbent != null
			&& GetActorId(urgentActor) != GetActorId(incumbent)
			&& IsStrictlyBetterSupportTalent(urgentActor, incumbent);

		Actor supported = incumbent;
		if (incumbent == null && urgentBetterTalent)
		{
			supported = urgentActor;
		}
		else if (replacementByBetterTalent)
		{
			supported = urgentActor;
		}

		bool actorChanged = supported != null && state.SupportedActorId != GetActorId(supported);
		if (supported == null)
		{
			// 旧对象死亡/离族后才重新择人。已有长期扶持传统时优先沿用上一任的培养方向，
			// 家族志向只在“从未有过扶持对象”时参与首次定向，不再拥有换人或停扶权限。
			string selectionPurpose = urgentBetterTalent
				? ResolveSupportPurposeForActor(urgentActor)
				: !string.IsNullOrWhiteSpace(state.SupportPurpose)
					? state.SupportPurpose
					: ResolveSupportPurpose(state.ActiveAspiration, aggregate);
			if (string.IsNullOrWhiteSpace(selectionPurpose))
			{
				if (state.SupportedActorId > 0L) changed |= ClearSupport(state);
				return changed;
			}

			if (urgentBetterTalent)
			{
				supported = urgentActor;
			}
			else
			{
				familyEntries ??= XjFamilyMemberLedger.ReadFamilyAlive(state.FamilyStableId);
				SupportSelectionResult selection = SelectSupportedActor(
					familyEntries,
					selectionPurpose,
					state.FamilyStableId,
					currentYear,
					aggregate);
				supported = selection.Selected;
				if (supported != null && selection.PatronOverride)
				{
					RecordHighRealmOverride(
						state.FamilyStableId,
						selection.Patron,
						selection.ObjectiveBest,
						supported,
						selectionPurpose,
						currentYear);
				}
			}

			if (supported == null)
			{
				if (state.SupportedActorId > 0L) changed |= ClearSupport(state);
				return changed;
			}
			actorChanged = state.SupportedActorId != GetActorId(supported);
		}

		string purpose = ResolveSupportPurposeForActor(supported);
		if (actorChanged)
		{
			state.SupportedActorId = GetActorId(supported);
			state.SupportedSinceYear = currentYear;
			state.LastSupportYear = 0;
			if (!string.IsNullOrWhiteSpace(purpose)) state.SupportPurpose = purpose;
			changed = true;
			RecordSupportedSelection(state.FamilyStableId, supported, state.SupportPurpose, currentYear);
		}
		else
		{
			if (state.SupportedSinceYear <= 0)
			{
				state.SupportedSinceYear = currentYear;
				changed = true;
			}
			// 同一个后辈突破后，只推进扶持目标，不重置“自何年起扶持”，更不会换人。
			if (!string.IsNullOrWhiteSpace(purpose)
				&& !string.Equals(state.SupportPurpose, purpose, StringComparison.Ordinal))
			{
				state.SupportPurpose = purpose;
				changed = true;
			}
		}

		// 已经走到金丹/真君等现有家族扶持链的顶端时仍保留长期扶持身份；
		// 此时只是没有新的阶段性资源可发，直到其死亡或出现严格更好的新天骄。
		if (string.IsNullOrWhiteSpace(purpose)) return changed;

		int intervalYears = ResolveSupportGrantIntervalYears(state.FamilyStableId);
		if (!actorChanged
			&& state.LastSupportYear > 0
			&& currentYear - state.LastSupportYear < intervalYears)
		{
			return changed;
		}

		bool highRealmInfluenceDue = IsHighRealmInfluenceDue(state, currentYear);
		state.LastSupportYear = currentYear;
		changed = true;
		bool supportGranted = TryApplyOneRealSupport(
			supported,
			purpose,
			currentYear,
			out string supportSummary);
		Actor patron = null;
		string influenceSummary = string.Empty;
		bool influenceGranted = highRealmInfluenceDue
			&& TryApplyHighRealmPatronage(
				state.FamilyStableId,
				aggregate,
				supported,
				purpose,
				currentYear,
				out patron,
				out influenceSummary);

		if (influenceGranted)
		{
			RecordHighRealmPatronage(
				state.FamilyStableId,
				patron,
				supported,
				purpose,
				influenceSummary,
				currentYear);
		}
		if (supportGranted)
		{
			RecordSupportGranted(
				state.FamilyStableId,
				supported,
				purpose,
				supportSummary,
				currentYear,
				surfaceAnnouncement: !influenceGranted);
		}
		return changed;
	}

	private static int ResolveSupportGrantIntervalYears(long familyStableId)
	{
		return XjDeterministicHash.PositiveIndex(familyStableId, "family.support.interval.v2", 2) == 0 ? 5 : 10;
	}

	private static bool TryResolveUrgentSupportTalentActor(long familyStableId, out Actor actor)
	{
		actor = null;
		if (familyStableId <= 0L
			|| !UrgentBetterTalentActorByFamily.TryGetValue(familyStableId, out long actorId)
			|| actorId <= 0L
			|| !XjScheduler.ResolveActor(actorId, out Actor resolved)
			|| resolved?.data == null
			|| !resolved.isAlive()
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long confirmedFamilyId)
			|| confirmedFamilyId != familyStableId
			|| ResolveStableSupportTalentScore(resolved) <= 0)
		{
			return false;
		}
		actor = resolved;
		return true;
	}

	private static string ResolveSupportPurposeForActor(Actor actor)
	{
		if (actor?.data == null) return string.Empty;
		string realmId = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
			return XjCenturyFamilySupportPurpose.FuChiZhuJi;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
			return XjCenturyFamilySupportPurpose.FuChiZiFu;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
			return XjCenturyFamilySupportPurpose.FuChiQiuJin;
		return string.Empty;
	}

	private static string ResolveSupportPurpose(string aspiration, in XjFamilyLedgerAggregate aggregate)
	{
		string value = aspiration?.Trim() ?? string.Empty;
		if (string.Equals(value, XjCenturyFamilyAspiration.YuFu, StringComparison.Ordinal))
		{
			return XjCenturyFamilySupportPurpose.FuChiZiFu;
		}
		if (string.Equals(value, XjCenturyFamilyAspiration.QiuJin, StringComparison.Ordinal))
		{
			return XjCenturyFamilySupportPurpose.FuChiQiuJin;
		}
		if (!string.Equals(value, XjCenturyFamilyAspiration.QiuFa, StringComparison.Ordinal)
			&& !string.Equals(value, XjCenturyFamilyAspiration.FuZhen, StringComparison.Ordinal))
		{
			return string.Empty;
		}

		if (aggregate.ZiFuCount > 0 && aggregate.JinDanCount <= 0)
		{
			return XjCenturyFamilySupportPurpose.FuChiQiuJin;
		}
		if (aggregate.HighestRealmOrder >= XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZhuJi))
		{
			return XjCenturyFamilySupportPurpose.FuChiZiFu;
		}
		return XjCenturyFamilySupportPurpose.FuChiZhuJi;
	}

	private static Actor ResolveValidSupportedActor(XjCenturyFamilyStageStateRecord state)
	{
		if (state == null || state.SupportedActorId <= 0L) return null;
		if (!XjScheduler.ResolveActor(state.SupportedActorId, out Actor actor)
			|| actor?.data == null
			|| !actor.isAlive()
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(state.SupportedActorId, out long familyId)
			|| familyId != state.FamilyStableId)
		{
			return null;
		}
		return actor;
	}

	private static bool TryResolvePersistedSupportedActor(long familyStableId, out Actor actor)
	{
		actor = null;
		if (familyStableId <= 0L) return false;
		if (!XjCenturyAnnalsStore.TryGetFamilyStageState(familyStableId, out XjCenturyFamilyStageStateRecord state))
		{
			return false;
		}
		actor = ResolveValidSupportedActor(state);
		return actor != null;
	}

	private static int ResolveStableSupportTalentScore(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			|| aptitude < 1 || aptitude > 6)
		{
			return 0;
		}
		return XjTalentOpportunityRules.ResolveFamilySupportTalentScore(actor, aptitude);
	}

	private static bool IsStrictlyBetterSupportTalent(Actor candidate, Actor incumbent)
	{
		if (candidate?.data == null) return false;
		if (incumbent?.data == null) return true;
		return ResolveStableSupportTalentScore(candidate) > ResolveStableSupportTalentScore(incumbent);
	}

	private static SupportSelectionResult SelectSupportedActor(
		IReadOnlyList<XjFamilyMemberLedgerEntry> entries,
		string purpose,
		long familyId,
		int currentYear,
		in XjFamilyLedgerAggregate aggregate)
	{
		Candidate first = default;
		Candidate second = default;
		Candidate third = default;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyMemberLedgerEntry entry = entries[i];
			if (!entry.Found || !entry.IsAlive || entry.ActorId <= 0L
				|| !MatchesPurpose(entry.RealmId, purpose)
				|| !XjScheduler.ResolveActor(entry.ActorId, out Actor actor)
				|| actor?.data == null
				|| !actor.isAlive()
				|| !MatchesPurpose(actor, purpose))
			{
				continue;
			}

			Candidate candidate = BuildCandidate(actor, entry, purpose);
			InsertRankedCandidate(ref first, ref second, ref third, candidate, purpose);
		}

		if (!first.Found)
		{
			return default;
		}

		Actor patron = ResolveHighRealmPatron(familyId, aggregate, purpose);
		if (patron == null || !second.Found || !ShouldPatronOverride(familyId, patron, purpose, currentYear))
		{
			return new SupportSelectionResult(first.Actor, first.Actor, patron, false);
		}

		int alternateCount = third.Found ? 2 : 1;
		long patronId = GetActorId(patron);
		int alternateIndex = XjDeterministicHash.PositiveIndex(
			familyId,
			"family.support.override.pick|" + currentYear + "|" + patronId + "|" + purpose,
			alternateCount);
		Actor selected = alternateIndex == 1 && third.Found ? third.Actor : second.Actor;
		return new SupportSelectionResult(selected, first.Actor, patron, true);
	}

	private static void InsertRankedCandidate(
		ref Candidate first,
		ref Candidate second,
		ref Candidate third,
		in Candidate candidate,
		string purpose)
	{
		if (!first.Found || CompareCandidate(candidate, first, purpose) < 0)
		{
			third = second;
			second = first;
			first = candidate;
			return;
		}

		if (!second.Found || CompareCandidate(candidate, second, purpose) < 0)
		{
			third = second;
			second = candidate;
			return;
		}

		if (!third.Found || CompareCandidate(candidate, third, purpose) < 0)
		{
			third = candidate;
		}
	}

	private static bool ShouldPatronOverride(long familyId, Actor patron, string purpose, int currentYear)
	{
		long patronId = GetActorId(patron);
		if (familyId <= 0L || patronId <= 0L || currentYear <= 0)
		{
			return false;
		}
		// 家族主车道只在5年批次运行，所以把50年周期离散成10个可命中的五年相位桶。
		int phase = XjDeterministicHash.PositiveIndex(familyId, "family.high_realm_override.phase.v2", HighRealmInfluenceIntervalYears / FamilyPeriodicScanYears) * FamilyPeriodicScanYears;
		if (currentYear % HighRealmInfluenceIntervalYears != phase) return false;

		return XjDeterministicHash.PositiveIndex(
			familyId,
			"family.support.override|" + currentYear + "|" + patronId + "|" + purpose,
			100) < HighRealmOverrideChancePercent;
	}


	private static Candidate BuildCandidate(Actor actor, in XjFamilyMemberLedgerEntry entry, string purpose)
	{
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		int grade5Count = 0;
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
		{
			IReadOnlyList<XjActorGongFaCollection.Record> records = XjActorGongFaCollection.ReadRecords(actor);
			for (int i = 0; i < records.Count; i++)
			{
				if (records[i].Grade == 5) grade5Count++;
			}
		}
		return new Candidate(
			actor,
			entry.ActorId,
			snapshot,
			ResolveStableSupportTalentScore(actor),
			grade5Count,
			ResolveRemainingLifespan(actor));
	}

	private static int CompareCandidate(in Candidate left, in Candidate right, string purpose)
	{
		// 初次择人也优先稳定天赋分，避免把当前修炼进度误当成长期培养价值。
		int compare = right.TalentScore.CompareTo(left.TalentScore);
		if (compare != 0) return compare;
		compare = right.Snapshot.XjZz.CompareTo(left.Snapshot.XjZz);
		if (compare != 0) return compare;
		bool fuQiCandidate = IsFuQiSupportRealm(left.Snapshot.RealmId)
			|| IsFuQiSupportRealm(right.Snapshot.RealmId);
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
		{
			if (fuQiCandidate)
			{
				compare = NormalizeCandidateMetric(right.Snapshot.HuiGuang).CompareTo(NormalizeCandidateMetric(left.Snapshot.HuiGuang));
				if (compare != 0) return compare;
				compare = NormalizeCandidateMetric(right.Snapshot.MingShu).CompareTo(NormalizeCandidateMetric(left.Snapshot.MingShu));
				if (compare != 0) return compare;
				compare = NormalizeCandidateMetric(right.RemainingLifespan).CompareTo(NormalizeCandidateMetric(left.RemainingLifespan));
				return compare != 0 ? compare : left.ActorId.CompareTo(right.ActorId);
			}
			compare = right.Grade5Count.CompareTo(left.Grade5Count);
			if (compare != 0) return compare;
			compare = right.Snapshot.XianJiCount.CompareTo(left.Snapshot.XianJiCount);
			if (compare != 0) return compare;
			compare = right.Snapshot.HasQiuJinFa.CompareTo(left.Snapshot.HasQiuJinFa);
			if (compare != 0) return compare;
			compare = NormalizeCandidateMetric(right.Snapshot.ZhenYuan).CompareTo(NormalizeCandidateMetric(left.Snapshot.ZhenYuan));
			if (compare != 0) return compare;
			compare = NormalizeCandidateMetric(right.RemainingLifespan).CompareTo(NormalizeCandidateMetric(left.RemainingLifespan));
			return compare != 0 ? compare : left.ActorId.CompareTo(right.ActorId);
		}

		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZiFu, StringComparison.Ordinal))
		{
			if (fuQiCandidate)
			{
				compare = NormalizeCandidateMetric(right.Snapshot.HuiGuang).CompareTo(NormalizeCandidateMetric(left.Snapshot.HuiGuang));
				if (compare != 0) return compare;
				compare = NormalizeCandidateMetric(right.Snapshot.MingShu).CompareTo(NormalizeCandidateMetric(left.Snapshot.MingShu));
				if (compare != 0) return compare;
				compare = NormalizeCandidateMetric(right.RemainingLifespan).CompareTo(NormalizeCandidateMetric(left.RemainingLifespan));
				return compare != 0 ? compare : left.ActorId.CompareTo(right.ActorId);
			}
			compare = right.Snapshot.XianJiCount.CompareTo(left.Snapshot.XianJiCount);
			if (compare != 0) return compare;
			compare = NormalizeCandidateMetric(right.Snapshot.ZhenYuan).CompareTo(NormalizeCandidateMetric(left.Snapshot.ZhenYuan));
			if (compare != 0) return compare;
			compare = NormalizeCandidateMetric(right.Snapshot.HuiGuang).CompareTo(NormalizeCandidateMetric(left.Snapshot.HuiGuang));
			if (compare != 0) return compare;
			compare = NormalizeCandidateMetric(right.RemainingLifespan).CompareTo(NormalizeCandidateMetric(left.RemainingLifespan));
			return compare != 0 ? compare : left.ActorId.CompareTo(right.ActorId);
		}

		// 扶持筑基：修为完成度→道慧→命数→剩余寿命→稳定角色ID。
		compare = NormalizeCandidateMetric(right.Snapshot.ZhenYuan).CompareTo(NormalizeCandidateMetric(left.Snapshot.ZhenYuan));
		if (compare != 0) return compare;
		compare = NormalizeCandidateMetric(right.Snapshot.HuiGuang).CompareTo(NormalizeCandidateMetric(left.Snapshot.HuiGuang));
		if (compare != 0) return compare;
		compare = NormalizeCandidateMetric(right.Snapshot.MingShu).CompareTo(NormalizeCandidateMetric(left.Snapshot.MingShu));
		if (compare != 0) return compare;
		compare = NormalizeCandidateMetric(right.RemainingLifespan).CompareTo(NormalizeCandidateMetric(left.RemainingLifespan));
		return compare != 0 ? compare : left.ActorId.CompareTo(right.ActorId);
	}

	private static bool IsFuQiSupportRealm(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal);
	}

	private static float NormalizeCandidateMetric(float value)
	{
		return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
	}

	private static bool MatchesPurpose(Actor actor, string purpose)
	{
		if (actor?.data == null) return false;
		string realmId = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		return MatchesPurpose(realmId, purpose);
	}

	private static bool MatchesPurpose(string realmId, string purpose)
	{
		string normalizedRealmId = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZhuJi, StringComparison.Ordinal))
		{
			return string.Equals(normalizedRealmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
				|| string.Equals(normalizedRealmId, XjRealmIds.LianQi, StringComparison.Ordinal);
		}
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZiFu, StringComparison.Ordinal))
		{
			return string.Equals(normalizedRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
				|| string.Equals(normalizedRealmId, XjRealmIds.HuangGuan, StringComparison.Ordinal);
		}
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
		{
			return string.Equals(normalizedRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
				|| string.Equals(normalizedRealmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal);
		}
		return false;
	}

	private static int ResolveSupportedRealmOrder(string purpose)
	{
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZhuJi, StringComparison.Ordinal))
		{
			return XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.LianQi);
		}
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZiFu, StringComparison.Ordinal))
		{
			return XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZhuJi);
		}
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
		{
			return XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZiFu);
		}
		return 0;
	}

	private static Actor ResolveHighRealmPatron(
		long familyId,
		in XjFamilyLedgerAggregate aggregate,
		string purpose)
	{
		long patronId = aggregate.RepresentativeActorId;
		if (familyId <= 0L
			|| patronId <= 0L
			|| !XjScheduler.ResolveActor(patronId, out Actor patron)
			|| patron?.data == null
			|| !patron.isAlive()
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(patronId, out long patronFamilyId)
			|| patronFamilyId != familyId)
		{
			return null;
		}

		string patronRealmId = XjRealmHelper.GetUnifiedId(patron, XjRealmHelper.GetTraitSnapshotForRouter);
		int patronOrder = XjFamilyMemberLedger.GetRealmOrder(patronRealmId);
		int targetOrder = ResolveSupportedRealmOrder(purpose);
		int minimumPatronOrder = XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZiFu);
		return patronOrder >= minimumPatronOrder && patronOrder > targetOrder ? patron : null;
	}

	private static bool IsHighRealmInfluenceDue(XjCenturyFamilyStageStateRecord state, int currentYear)
	{
		if (state == null || state.SupportedSinceYear <= 0 || currentYear <= state.SupportedSinceYear)
		{
			return false;
		}

		int elapsed = currentYear - state.SupportedSinceYear;
		int supportIntervalYears = ResolveSupportGrantIntervalYears(state.FamilyStableId);
		if (elapsed < supportIntervalYears)
		{
			return false;
		}

		// 上修亲自点拨统一为50年级次级AI；只使用5年主批次能命中的十个相位桶。
		int phase = XjDeterministicHash.PositiveIndex(state.FamilyStableId, "family.high_realm_patronage.phase.v2", HighRealmInfluenceIntervalYears / FamilyPeriodicScanYears) * FamilyPeriodicScanYears;
		return currentYear % HighRealmInfluenceIntervalYears == phase;
	}

	private static bool TryApplyHighRealmPatronage(
		long familyId,
		in XjFamilyLedgerAggregate aggregate,
		Actor supported,
		string purpose,
		int currentYear,
		out Actor patron,
		out string summary)
	{
		patron = ResolveHighRealmPatron(familyId, aggregate, purpose);
		summary = string.Empty;
		if (patron?.data == null || supported?.data == null || !supported.isAlive())
		{
			return false;
		}

		long patronId = GetActorId(patron);
		long supportedId = GetActorId(supported);
		if (patronId <= 0L || supportedId <= 0L || patronId == supportedId)
		{
			return false;
		}

		string patronRealmId = XjRealmHelper.GetUnifiedId(patron, XjRealmHelper.GetTraitSnapshotForRouter);
		int patronOrder = XjFamilyMemberLedger.GetRealmOrder(patronRealmId);
		bool jinDanPatron = patronOrder >= XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.JinDan);
		int strength = jinDanPatron ? 2 : 1;
		if (XjCultivationPathRules.IsFuQiYangXing(supported))
		{
			return TryApplyFuQiHighRealmPatronage(patron, supported, purpose, currentYear, strength, out summary);
		}

		XjActorCultivationSnapshot before = XjActorCultivationSnapshotBuilder.Build(supported);
		// 亲自点拨只有五十年级频率，收益必须足以真实推动一个阶段，而不是等同数年自然吐纳。
		// 真人按一份、真君按两份结算；仍受境界真元上限与瓶颈闸门约束，绝不越境直升。
		float zhenYuanGrant = string.Equals(before.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			? 300f * strength
			: string.Equals(before.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
				? 6000f * strength
				: string.Equals(before.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
					? 18000f * strength
					: 0f;

		float nextZhenYuan = before.ZhenYuan;
		if (zhenYuanGrant > 0f)
		{
			float proposed = XjCultivationGrowthRules.ApplyRealmCap(
				before,
				(float)Math.Floor(Math.Max(0f, before.ZhenYuan + zhenYuanGrant)));
			nextZhenYuan = XjBottleneckEventSystem.ApplyGrowthGate(supported, in before, proposed);
			if (nextZhenYuan > before.ZhenYuan + 0.001f)
			{
				XjActorAccessor.SetFloat(supported, XjActorDataKeys.ZhenYuan, nextZhenYuan);
			}
		}

		XjActorAccessor.TryGetFloat(supported, XjActorDataKeys.HuiGuang, out float beforeHuiGuang);
		float nextHuiGuang = XjDaoHuiPolicy.Add(beforeHuiGuang, 3f * strength, XjDaoHuiPolicy.OrdinaryGrowthCeiling);
		if (nextHuiGuang > beforeHuiGuang + 0.001f)
		{
			XjActorAccessor.SetFloat(supported, XjActorDataKeys.HuiGuang, nextHuiGuang);
		}

		bool xianJiGuidance = false;
		if (string.Equals(before.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetFloat(supported, XjActorDataKeys.XjXianJiLectureAidBonus, out float existingBonus);
			float guidanceBonus = jinDanPatron ? 0.25f : 0.12f;
			XjActorAccessor.SetInt(supported, XjActorDataKeys.XjXianJiLectureAidYear, currentYear);
			XjActorAccessor.SetFloat(
				supported,
				XjActorDataKeys.XjXianJiLectureAidBonus,
				Math.Max(Math.Max(0f, existingBonus), guidanceBonus));
			xianJiGuidance = true;
		}

		int zhenYuanDelta = (int)Math.Floor(Math.Max(0f, nextZhenYuan - before.ZhenYuan));
		int huiGuangDelta = (int)Math.Floor(Math.Max(0f, nextHuiGuang - beforeHuiGuang));
		if (zhenYuanDelta <= 0 && huiGuangDelta <= 0 && !xianJiGuidance)
		{
			return false;
		}

		string effect = string.Empty;
		if (zhenYuanDelta > 0) effect = "真元增长" + zhenYuanDelta;
		if (huiGuangDelta > 0) effect += (effect.Length > 0 ? "、" : string.Empty) + "道慧增长" + huiGuangDelta;
		if (xianJiGuidance) effect += (effect.Length > 0 ? "，并" : string.Empty) + "留下仙基参悟指引";

		summary = XjFamilyAnnouncementNameFormatter.FormatHighRealmActor(patron)
			+ "垂意后辈" + SafeActorName(supported)
			+ "，亲自点拨修行，" + effect + "。";
		return true;
	}

	private static bool TryApplyOneRealSupport(Actor actor, string purpose, int currentYear, out string summary)
	{
		summary = string.Empty;
		if (actor?.data == null || !actor.isAlive()) return false;
		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return TryApplyFuQiSupport(actor, purpose, currentYear, 2, 1, out summary);
		}
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZhuJi, StringComparison.Ordinal))
		{
			if (!XjCaiQiFaAcquisition.TryInheritFromFamily(actor, currentYear)) return false;
			XjCaiQiFaState state = XjCaiQiFaAccessor.BuildState(actor);
			summary = state.Found ? "传下采气法《" + state.Name + "》" : "传下家族采气法";
			return true;
		}

		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (XjFamilyHighGradeTransmission.TryBorrowGrade5(actor, snapshot, gongFa))
		{
			XjGongFaState updated = XjGongFaAccessor.BuildState(actor);
			summary = updated.Found ? "传下五品功法《" + updated.Name + "》" : "传下五品功法";
			return true;
		}

		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
		{
			// 求金扶持严格按修炼链顺序：五部真实五品与五门仙基未齐时，
			// 家族不能用一件法宝掩盖前置缺口，也不能提前塞入求金法。
			if (!XjXianJiAccessor.HasFive(actor)
				|| !XjActorGongFaCollection.HasFiveRealGrade5GongFa(actor))
			{
				return false;
			}

			XjQiuJinFaState existingQiuJin = XjQiuJinFaAccessor.BuildState(actor);
			if (!existingQiuJin.Found)
			{
				bool borrowedQiuJinFa;
				try
				{
					borrowedQiuJinFa = XjFamilyHighGradeTransmission.TryBorrowQiuJinFa(
						actor, snapshot, gongFa, currentYear);
				}
				catch (Exception ex)
				{
					XjExceptionDiagnostics.Report("XjFamilySupportSystem.QiuJinBorrow", ex);
					return false;
				}
				if (!borrowedQiuJinFa) return false;

				XjQiuJinFaState qiuJin = XjQiuJinFaAccessor.BuildState(actor);
				if (!qiuJin.Found || !qiuJin.Ready)
				{
					return false;
				}
				XjQiuJinFaSystem.PublishQiuJinFaSuccess(actor, qiuJin);
				summary = "传下求金法《" + qiuJin.Name + "》";
				return true;
			}
		}

		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!XjFaBaoEquipmentSync.TryBorrowFamilyFaBao(actor, realmId, currentYear)) return false;
		XjFaBaoState faBao = XjFaBaoAccessor.BuildState(actor);
		summary = faBao.Found ? "授予重宝“" + faBao.Name + "”" : "授予家族重宝";
		return true;
	}

	private static bool TryApplyFuQiHighRealmPatronage(
		Actor patron,
		Actor supported,
		string purpose,
		int currentYear,
		int strength,
		out string summary)
	{
		if (!TryApplyFuQiSupport(
			supported,
			purpose,
			currentYear,
			Math.Max(10, strength * 10),
			Math.Max(3, strength * 3),
			out string effect))
		{
			summary = string.Empty;
			return false;
		}

		summary = XjFamilyAnnouncementNameFormatter.FormatHighRealmActor(patron)
			+ "垂意后辈" + SafeActorName(supported)
			+ "，以同途性命修持亲自点拨，" + effect + "。";
		return true;
	}

	private static bool TryApplyFuQiSupport(
		Actor actor,
		string purpose,
		int currentYear,
		int shortenedYears,
		int huiGuangGrant,
		out string summary)
	{
		summary = string.Empty;
		if (actor?.data == null
			|| currentYear <= 0
			|| !XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return false;
		}

		string realmId = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		bool projectAdvanced = false;
		string projectName = string.Empty;
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZiFu, StringComparison.Ordinal)
			&& string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			projectAdvanced = ShortenFuQiProject(
				actor,
				XjActorDataKeys.FuQiBodyProjectCompleteYear,
				currentYear,
				shortenedYears);
			projectName = "神妙归身";
		}
		else if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal)
			&& string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, out int perfectedYear);
			string projectKey = perfectedYear > 0
				? XjActorDataKeys.FuQiJinXingNurtureCompleteYear
				: XjActorDataKeys.FuQiPerfectionProjectCompleteYear;
			bool canAdvanceProject = perfectedYear <= 0
				|| XjFuQiBalancePolicy.CanAcceleratePostFailureNurture(actor, currentYear);
			projectAdvanced = canAdvanceProject
				&& ShortenFuQiProject(actor, projectKey, currentYear, shortenedYears);
			projectName = perfectedYear > 0 ? "金性温养" : "神妙圆满";
		}
		else
		{
			return false;
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float beforeHuiGuang);
		float nextHuiGuang = XjDaoHuiPolicy.Add(beforeHuiGuang, huiGuangGrant, XjDaoHuiPolicy.OrdinaryGrowthCeiling);
		bool huiGuangAdvanced = nextHuiGuang > beforeHuiGuang + 0.001f;
		if (huiGuangAdvanced)
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, nextHuiGuang);
		}
		if (!projectAdvanced && !huiGuangAdvanced)
		{
			return false;
		}

		string effect = projectAdvanced
			? projectName + "修期缩短" + Math.Max(1, shortenedYears) + "年"
			: projectName + "心得更明";
		if (huiGuangAdvanced)
		{
			effect += "、道慧增长" + Math.Max(1, (int)Math.Floor(nextHuiGuang - beforeHuiGuang));
		}
		summary = "同途前辈的性命修持心得（" + effect + "）";
		return true;
	}

	private static bool ShortenFuQiProject(Actor actor, string key, int currentYear, int years)
	{
		if (years <= 0
			|| !XjActorAccessor.TryGetInt(actor, key, out int completeYear)
			|| completeYear <= currentYear)
		{
			return false;
		}
		int next = Math.Max(currentYear + 1, completeYear - years);
		if (next >= completeYear) return false;
		XjActorAccessor.SetInt(actor, key, next);
		return true;
	}

	private static bool ObserveClanLeaderChange(
		XjCenturyFamilyStageStateRecord state,
		int currentYear,
		in XjFamilyLedgerAggregate aggregate)
	{
		long leaderId = aggregate.RepresentativeActorId > 0L ? aggregate.RepresentativeActorId : 0L;
		if (state.LastClanLeaderActorId == leaderId) return false;
		// 家主只是当前家族投影中的治理角色，不再作为天下纪事、三卷史册或公告事件。
		// 仍保存ID供家族年度逻辑复用，避免每次结算重复判断。
		state.LastClanLeaderActorId = leaderId;
		return true;
	}

	private static bool IsStoredClanLeaderValid(XjCenturyFamilyStageStateRecord state)
	{
		return state.LastClanLeaderActorId > 0L
			&& XjFamilyMemberLedger.TryGetByActorId(state.LastClanLeaderActorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found
			&& entry.IsAlive
			&& entry.FamilyStableId == state.FamilyStableId;
	}


	private static bool ClearSupport(XjCenturyFamilyStageStateRecord state, bool preserveLastSupportYear = false)
	{
		if (state.SupportedActorId <= 0L
			&& string.IsNullOrWhiteSpace(state.SupportPurpose)
			&& state.SupportedSinceYear <= 0
			&& (preserveLastSupportYear || state.LastSupportYear <= 0))
		{
			return false;
		}
		state.SupportedActorId = 0L;
		state.SupportPurpose = string.Empty;
		state.SupportedSinceYear = 0;
		if (!preserveLastSupportYear) state.LastSupportYear = 0;
		return true;
	}

	private static void RecordSupportedSelection(
		long familyId,
		Actor actor,
		string purpose,
		int currentYear)
	{
		long actorId = GetActorId(actor);
		string actorName = SafeActorName(actor);
		string familyName = XjFamilyDisplayNameResolver.Resolve(familyId);
		string displayPurpose = ResolveSupportPurposeDisplay(actor, purpose);
		string summary = familyName + "诸房会于族堂，反复衡量资质、年岁与当前关隘，最终推举" + actorName + "为长期重点扶持的后辈，" + displayPurpose + "。";
		XjCenturyAnnalsStore.ObserveFamilyEvent(
			"FamilySupportedHeirSelected",
			currentYear,
			familyId,
			2,
			summary,
			actorId,
			actorName);
		XjThreeBookWriter.RecordFamilySupport(familyId, actor, displayPurpose, string.Empty, currentYear, granted: false);
		XjWorldHistoryStore.RecordDomainEvent(
			"家族",
			"族议举后",
			summary,
			2,
			actorId: actorId,
			actorName: actorName,
			familyId: familyId,
			year: currentYear,
			eventType: "FamilySupportedHeirSelected",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.CenturyCandidate));
		// “重点扶持后辈”只是每个家族内部的年度选择，可能在新档首年
		// 同时发生数十次。它继续写入世家纪事与百年世谱，但不再弹世界提示；
		// 真正从族库发放资源时仍由 RecordSupportGranted 按开关公告。
	}

	private static void RecordSupportGranted(
		long familyId,
		Actor actor,
		string purpose,
		string supportSummary,
		int currentYear,
		bool surfaceAnnouncement)
	{
		long actorId = GetActorId(actor);
		string actorName = SafeActorName(actor);
		string familyName = XjFamilyDisplayNameResolver.Resolve(familyId);
		string goal = ResolveSupportGoal(actor, purpose);
		string summary = familyName + "调度族中积累，将" + supportSummary + "交予" + actorName
			+ "，并在此后数年把更多传承与指点向其倾斜，以助其" + goal + "。";
		XjCenturyAnnalsStore.ObserveFamilyEvent(
			"FamilySupportGranted",
			currentYear,
			familyId,
			3,
			summary,
			actorId,
			actorName);
		XjThreeBookWriter.RecordFamilySupport(
			familyId,
			actor,
			ResolveSupportPurposeDisplay(actor, purpose),
			supportSummary,
			currentYear,
			granted: true);
		XjWorldHistoryStore.RecordDomainEvent(
			"家族",
			"族议扶持",
			summary,
			3,
			actorId: actorId,
			actorName: actorName,
			familyId: familyId,
			year: currentYear,
			eventType: "FamilySupportGranted",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.CenturyCandidate));
		if (surfaceAnnouncement)
		{
			XjBroadcastSystem.ShowRecordedCategorizedWorldTip(
				summary,
				XjAnnouncementCategory.FamilyInheritance,
				duration: 8f,
				color: "#C8B36A");
		}
	}

	private static void RecordHighRealmOverride(
		long familyId,
		Actor patron,
		Actor objectiveBest,
		Actor selected,
		string purpose,
		int currentYear)
	{
		long selectedId = GetActorId(selected);
		string selectedName = SafeActorName(selected);
		string patronName = XjFamilyAnnouncementNameFormatter.FormatHighRealmActor(patron);
		string objectiveName = SafeActorName(objectiveBest);
		string familyName = XjFamilyDisplayNameResolver.Resolve(familyId);
		string summary = familyName + "族议原拟推举" + objectiveName + "，"
			+ patronName
			+ "力排众议，改举" + selectedName + "为族中所举，家族资源与传承遂转向其身，以图"
			+ ResolveSupportGoal(selected, purpose) + "。";

		XjCenturyAnnalsStore.ObserveFamilyEvent(
			"FamilyHighRealmOverride",
			currentYear,
			familyId,
			3,
			summary,
			selectedId,
			selectedName);
		XjWorldHistoryStore.RecordDomainEvent(
			"家族",
			"上修定议",
			summary,
			3,
			actorId: selectedId,
			actorName: selectedName,
			familyId: familyId,
			year: currentYear,
			eventType: "FamilyHighRealmOverride",
			relatedActorId: GetActorId(patron),
			relatedActorName: patronName,
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.CenturyCandidate),
			result: XjHistoryResult.Change);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			summary,
			XjAnnouncementCategory.HighRealmInfluence,
			duration: 9f,
			color: "#B98AD9",
			delayFrames: 1);
	}

	private static void RecordHighRealmPatronage(
		long familyId,
		Actor patron,
		Actor supported,
		string purpose,
		string influenceSummary,
		int currentYear)
	{
		long supportedId = GetActorId(supported);
		string supportedName = SafeActorName(supported);
		string familyName = XjFamilyDisplayNameResolver.Resolve(familyId);
		string summary = familyName + "上修扶持族中所举，" + influenceSummary
			+ " 此后其修行与" + ResolveSupportGoal(supported, purpose) + "准备均受此番点拨推动。";

		XjCenturyAnnalsStore.ObserveFamilyEvent(
			"FamilyHighRealmPatronage",
			currentYear,
			familyId,
			3,
			summary,
			supportedId,
			supportedName);
		XjWorldHistoryStore.RecordDomainEvent(
			"家族",
			"上修扶持",
			summary,
			3,
			actorId: supportedId,
			actorName: supportedName,
			familyId: familyId,
			year: currentYear,
			eventType: "FamilyHighRealmPatronage",
			relatedActorId: GetActorId(patron),
			relatedActorName: XjFamilyAnnouncementNameFormatter.FormatHighRealmActor(patron),
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.CenturyCandidate),
			result: XjHistoryResult.Success);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			summary,
			XjAnnouncementCategory.HighRealmInfluence,
			duration: 9f,
			color: "#B98AD9",
			delayFrames: 1);
	}

	private static string ResolveSupportPurposeDisplay(Actor actor, string purpose)
	{
		if (!XjCultivationPathRules.IsFuQiYangXing(actor)) return purpose;
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZiFu, StringComparison.Ordinal))
		{
			return "扶持真人";
		}
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
		{
			return "扶持真君羽士";
		}
		return purpose;
	}

	private static string ResolveSupportGoal(Actor actor, string purpose)
	{
		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZiFu, StringComparison.Ordinal))
			{
				return "求到自身之真";
			}
			if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
			{
				return "求证真君羽士";
			}
		}
		return purpose.Replace("扶持", string.Empty);
	}

	private static float ResolveRemainingLifespan(Actor actor)
	{
		try
		{
			if (actor?.stats == null) return 0f;
			return Math.Max(0f, actor.stats["lifespan"] - Math.Max(0f, actor.getAge()));
		}
		catch
		{
			return 0f;
		}
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static string SafeActorName(Actor actor)
	{
		string name = actor?.getName();
		return string.IsNullOrWhiteSpace(name) ? "未名族人" : name.Trim();
	}

	private readonly struct SupportSelectionResult
	{
		internal readonly Actor Selected;
		internal readonly Actor ObjectiveBest;
		internal readonly Actor Patron;
		internal readonly bool PatronOverride;

		internal SupportSelectionResult(Actor selected, Actor objectiveBest, Actor patron, bool patronOverride)
		{
			Selected = selected;
			ObjectiveBest = objectiveBest;
			Patron = patron;
			PatronOverride = patronOverride;
		}
	}

	private readonly struct Candidate
	{
		internal readonly bool Found;
		internal readonly Actor Actor;
		internal readonly long ActorId;
		internal readonly XjActorCultivationSnapshot Snapshot;
		internal readonly int TalentScore;
		internal readonly int Grade5Count;
		internal readonly float RemainingLifespan;

		internal Candidate(
			Actor actor,
			long actorId,
			in XjActorCultivationSnapshot snapshot,
			int talentScore,
			int grade5Count,
			float remainingLifespan)
		{
			Found = actor?.data != null && actorId > 0L;
			Actor = actor;
			ActorId = Math.Max(0L, actorId);
			Snapshot = snapshot;
			TalentScore = Math.Max(0, talentScore);
			Grade5Count = Math.Max(0, grade5Count);
			RemainingLifespan = Math.Max(0f, remainingLifespan);
		}
	}
}
