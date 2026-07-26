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

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 世家族议的唯一持久状态入口。每年只读现有家族成员账本；同一家族
/// 十年最多尝试一次真实传承，不创建人才池、培养点或独立资源系统。
/// </summary>
internal static class XjFamilySupportSystem
{
	private const int SupportIntervalYears = 10;
	private const int HighRealmInfluenceIntervalYears = 20;
	private const int HighRealmOverrideChancePercent = 25;

	internal static void TickYear(int currentYear)
	{
		if (currentYear <= 0)
		{
			return;
		}

		// 复用家族年度阶段：只检查真正持有器库条目的家族。覆灭家族重宝转入既有遗失池，
		// 不新建调度器，也不扫描全世界角色。
		XjFamilyFaBaoWarehouse.ReconcileExtinctFamilyTreasures(currentYear);
		XjFamilyFaBaoWarehouse.ReconcileExtinctSectTreasures(currentYear);

		IReadOnlyList<XjCenturyFamilyStageStateRecord> states = XjCenturyAnnalsStore.ReadFamilyStageStateView();
		if (states == null || states.Count == 0)
		{
			return;
		}

		bool changed = false;
		for (int i = 0; i < states.Count; i++)
		{
			XjCenturyFamilyStageStateRecord state = states[i];
			if (state == null || state.FamilyStableId <= 0L) continue;
			if (!XjFamilyMemberLedger.TryGetAggregate(state.FamilyStableId, out XjFamilyLedgerAggregate aggregate)
				|| aggregate.AliveCount <= 0)
			{
				changed |= ClearSupport(state);
				if (state.LastClanLeaderActorId != 0L)
				{
					state.LastClanLeaderActorId = 0L;
					changed = true;
				}
				continue;
			}

			IReadOnlyList<XjFamilyMemberLedgerEntry> familyEntries = null;
			if (XjHighRealmChessEventSystem.ShouldCheck(state.FamilyStableId, aggregate, currentYear))
			{
				familyEntries = XjFamilyMemberLedger.ReadFamilyAlive(state.FamilyStableId);
				XjHighRealmChessEventSystem.TryResolve(state.FamilyStableId, familyEntries, currentYear);
			}
			if (!IsStoredClanLeaderValid(state))
			{
				// 家主只在旧家主失效时更替，直接复用家族聚合中的代表人物，
				// 避免旧档首次运行时为每个家族复制并排序成员列表。
				changed |= ObserveClanLeaderChange(state, currentYear, aggregate);
			}
			if (IsSupportGoalAlreadyMet(state, aggregate))
			{
				// 志业在百年世谱结算时正式完成。年度入口只停止继续拨付，
				// 保留本代所举之人，确保本世纪记录不会丢失关键承志者。
				continue;
			}

			string purpose = ResolveSupportPurpose(state.ActiveAspiration, aggregate);
			if (string.IsNullOrWhiteSpace(purpose))
			{
				changed |= ClearSupport(state);
				continue;
			}

			Actor supported = ResolveValidSupportedActor(state, purpose);
			if (supported == null)
			{
				bool hadAssignedActor = state.SupportedActorId > 0L;
				if (!hadAssignedActor
					&& state.LastSupportYear > 0
					&& currentYear - state.LastSupportYear < SupportIntervalYears)
				{
					continue;
				}

				familyEntries ??= XjFamilyMemberLedger.ReadFamilyAlive(state.FamilyStableId);
				SupportSelectionResult selection = SelectSupportedActor(
					familyEntries,
					purpose,
					state.FamilyStableId,
					currentYear,
					aggregate);
				supported = selection.Selected;
				if (supported == null)
				{
					changed |= ClearSupport(state, preserveLastSupportYear: true);
					if (state.LastSupportYear != currentYear)
					{
						state.LastSupportYear = currentYear;
						changed = true;
					}
					continue;
				}

				long actorId = GetActorId(supported);
				bool selectionChanged = state.SupportedActorId != actorId
					|| !string.Equals(state.SupportPurpose, purpose, StringComparison.Ordinal);
				state.SupportedActorId = actorId;
				state.SupportPurpose = purpose;
				state.SupportedSinceYear = currentYear;
				changed = true;
				if (selectionChanged)
				{
					if (selection.PatronOverride)
					{
						RecordHighRealmOverride(
							state.FamilyStableId,
							selection.Patron,
							selection.ObjectiveBest,
							supported,
							purpose,
							currentYear);
					}
					RecordSupportedSelection(
						state.FamilyStableId,
						supported,
						purpose,
						currentYear);
				}
			}
			else if (!string.Equals(state.SupportPurpose, purpose, StringComparison.Ordinal))
			{
				state.SupportPurpose = purpose;
				state.SupportedSinceYear = currentYear;
				changed = true;
			}

			if (state.LastSupportYear > 0 && currentYear - state.LastSupportYear < SupportIntervalYears)
			{
				continue;
			}

			bool highRealmInfluenceDue = IsHighRealmInfluenceDue(state, currentYear);

			// 失败也记作本轮族议已检查，避免仓库缺货时逐年重复扫描。
			// 年份先提交，再写角色收益，保证同年入口重复调用时至多生效一次。
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

			// 上修干预比常规族议更稀有，同年两者同时发生时优先展示上修公告；
			// 两条历史仍都会写入，不丢失因果链。
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
		}

		if (!changed)
		{
			return;
		}

		XjCenturyAnnalsStore.NotifyFamilyStageStateFieldsChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Family | XjCodexDirtyFlags.CenturyAnnals | XjCodexDirtyFlags.History);
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

	private static bool IsSupportGoalAlreadyMet(
		XjCenturyFamilyStageStateRecord state,
		in XjFamilyLedgerAggregate aggregate)
	{
		string value = state?.ActiveAspiration?.Trim() ?? string.Empty;
		if (string.Equals(value, XjCenturyFamilyAspiration.QiuFa, StringComparison.Ordinal))
		{
			return HasHighGradeFamilyInheritance(state?.FamilyStableId ?? 0L);
		}
		if (string.Equals(value, XjCenturyFamilyAspiration.YuFu, StringComparison.Ordinal))
		{
			return aggregate.ZiFuCount > 0 || aggregate.JinDanCount > 0;
		}
		if (string.Equals(value, XjCenturyFamilyAspiration.QiuJin, StringComparison.Ordinal))
		{
			return aggregate.JinDanCount > 0;
		}
		return false;
	}

	private static bool HasHighGradeFamilyInheritance(long familyStableId)
	{
		IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries =
			XjFamilyGongFaWarehouse.ReadFamilyEntriesView(familyStableId);
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry entry = entries[i];
			if (!entry.Found) continue;
			if (string.Equals(entry.SourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, StringComparison.Ordinal)
				|| string.Equals(entry.SourceType, XjFamilyGongFaWarehouse.SourceTypeGongFa, StringComparison.Ordinal)
					&& entry.Grade >= 5)
			{
				return true;
			}
		}
		return false;
	}

	private static Actor ResolveValidSupportedActor(
		XjCenturyFamilyStageStateRecord state,
		string purpose)
	{
		if (state.SupportedActorId <= 0L || !string.Equals(state.SupportPurpose, purpose, StringComparison.Ordinal))
		{
			return null;
		}

		if (!XjScheduler.ResolveActor(state.SupportedActorId, out Actor actor)
			|| actor?.data == null
			|| !actor.isAlive()
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(state.SupportedActorId, out long familyId)
			|| familyId != state.FamilyStableId)
		{
			return null;
		}

		return MatchesPurpose(actor, purpose) ? actor : null;
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
			grade5Count,
			ResolveRemainingLifespan(actor));
	}

	private static int CompareCandidate(in Candidate left, in Candidate right, string purpose)
	{
		int compare;
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
		{
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
			compare = right.Snapshot.XianJiCount.CompareTo(left.Snapshot.XianJiCount);
			if (compare != 0) return compare;
			compare = NormalizeCandidateMetric(right.Snapshot.ZhenYuan).CompareTo(NormalizeCandidateMetric(left.Snapshot.ZhenYuan));
			if (compare != 0) return compare;
			compare = NormalizeCandidateMetric(right.Snapshot.HuiGuang).CompareTo(NormalizeCandidateMetric(left.Snapshot.HuiGuang));
			if (compare != 0) return compare;
			compare = NormalizeCandidateMetric(right.RemainingLifespan).CompareTo(NormalizeCandidateMetric(left.RemainingLifespan));
			return compare != 0 ? compare : left.ActorId.CompareTo(right.ActorId);
		}

		// 扶持筑基：修为完成度→慧光→命数→剩余寿命→稳定角色ID。
		compare = NormalizeCandidateMetric(right.Snapshot.ZhenYuan).CompareTo(NormalizeCandidateMetric(left.Snapshot.ZhenYuan));
		if (compare != 0) return compare;
		compare = NormalizeCandidateMetric(right.Snapshot.HuiGuang).CompareTo(NormalizeCandidateMetric(left.Snapshot.HuiGuang));
		if (compare != 0) return compare;
		compare = NormalizeCandidateMetric(right.Snapshot.MingShu).CompareTo(NormalizeCandidateMetric(left.Snapshot.MingShu));
		if (compare != 0) return compare;
		compare = NormalizeCandidateMetric(right.RemainingLifespan).CompareTo(NormalizeCandidateMetric(left.RemainingLifespan));
		return compare != 0 ? compare : left.ActorId.CompareTo(right.ActorId);
	}

	private static float NormalizeCandidateMetric(float value)
	{
		return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
	}

	private static bool MatchesPurpose(Actor actor, string purpose)
	{
		if (actor?.data == null) return false;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return MatchesPurpose(realmId, purpose);
	}

	private static bool MatchesPurpose(string realmId, string purpose)
	{
		string normalizedRealmId = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZhuJi, StringComparison.Ordinal))
		{
			return string.Equals(normalizedRealmId, XjRealmIds.LianQi, StringComparison.Ordinal);
		}
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiZiFu, StringComparison.Ordinal))
		{
			return string.Equals(normalizedRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal);
		}
		if (string.Equals(purpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
		{
			return string.Equals(normalizedRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal);
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
		if (elapsed < SupportIntervalYears)
		{
			return false;
		}

		// 第一次在举后十年发生，之后每二十年一次；复用族议年份门禁，
		// 不增加新的持久冷却字段。
		return (elapsed - SupportIntervalYears) % HighRealmInfluenceIntervalYears < SupportIntervalYears;
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

		XjActorCultivationSnapshot before = XjActorCultivationSnapshotBuilder.Build(supported);
		float zhenYuanGrant = string.Equals(before.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			? 50f * strength
			: string.Equals(before.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
				? 400f * strength
				: string.Equals(before.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
					? 1200f * strength
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
		float nextHuiGuang = Math.Min(200f, Math.Max(0f, beforeHuiGuang) + strength);
		if (nextHuiGuang > beforeHuiGuang + 0.001f)
		{
			XjActorAccessor.SetFloat(supported, XjActorDataKeys.HuiGuang, nextHuiGuang);
		}

		bool xianJiGuidance = false;
		if (string.Equals(before.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetFloat(supported, XjActorDataKeys.XjXianJiLectureAidBonus, out float existingBonus);
			float guidanceBonus = jinDanPatron ? 0.10f : 0.05f;
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
		if (huiGuangDelta > 0) effect += (effect.Length > 0 ? "、" : string.Empty) + "慧光增长" + huiGuangDelta;
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
				if (!XjFamilyHighGradeTransmission.TryBorrowQiuJinFa(actor, snapshot, gongFa, currentYear))
				{
					return false;
				}

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

	private static string ResolveLedgerActorName(long actorId)
	{
		if (actorId > 0L
			&& XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found
			&& !string.IsNullOrWhiteSpace(entry.Name))
		{
			return entry.Name.Trim();
		}
		return "未名族人";
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
		string summary = familyName + "诸房会于族堂，反复衡量资质、年岁与当前关隘，最终推举" + actorName + "为这一阶段重点扶持的后辈，" + purpose + "。";
		XjCenturyAnnalsStore.ObserveFamilyEvent(
			"FamilySupportedHeirSelected",
			currentYear,
			familyId,
			2,
			summary,
			actorId,
			actorName);
		XjThreeBookWriter.RecordFamilySupport(familyId, actor, purpose, string.Empty, currentYear, granted: false);
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
		string summary = familyName + "开启族库，将" + supportSummary + "交予" + actorName + "，并在此后数年把更多传承与指点向其倾斜，以助其" + purpose.Replace("扶持", string.Empty) + "。";
		XjCenturyAnnalsStore.ObserveFamilyEvent(
			"FamilySupportGranted",
			currentYear,
			familyId,
			3,
			summary,
			actorId,
			actorName);
		XjThreeBookWriter.RecordFamilySupport(familyId, actor, purpose, supportSummary, currentYear, granted: true);
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
			XjBroadcastSystem.ShowCategorizedWorldTip(
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
			+ purpose.Replace("扶持", string.Empty) + "。";

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
		XjBroadcastSystem.ShowCategorizedWorldTipCritical(
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
			+ " 此后其修行与" + purpose.Replace("扶持", string.Empty) + "准备均受此番点拨推动。";

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
		XjBroadcastSystem.ShowCategorizedWorldTipCritical(
			summary,
			XjAnnouncementCategory.HighRealmInfluence,
			duration: 9f,
			color: "#B98AD9",
			delayFrames: 1);
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
		internal readonly int Grade5Count;
		internal readonly float RemainingLifespan;

		internal Candidate(
			Actor actor,
			long actorId,
			in XjActorCultivationSnapshot snapshot,
			int grade5Count,
			float remainingLifespan)
		{
			Found = actor?.data != null && actorId > 0L;
			Actor = actor;
			ActorId = Math.Max(0L, actorId);
			Snapshot = snapshot;
			Grade5Count = Math.Max(0, grade5Count);
			RemainingLifespan = Math.Max(0f, remainingLifespan);
		}
	}
}
