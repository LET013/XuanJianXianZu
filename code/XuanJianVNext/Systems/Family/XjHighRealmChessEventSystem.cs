using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 低频“上修执棋”事件。主持者只从本族高境中选取，棋子只从其他家族的低境修士中选取；
/// 复用修士候选索引与家族聚合，不建立关系网、目标队列或额外全图扫描。
/// </summary>
internal static class XjHighRealmChessEventSystem
{
	private const int TriggerChancePercent = 35;
	private const int PawnCooldownYears = 50;
	private const int MaxPawnCandidateChecks = 96;
	private const string OutcomeEscaped = "Escaped";
	private const string OutcomeSecret = "Secret";
	private const string OutcomeBenefit = "Benefit";

	private static readonly string[] Actions =
	{
		"受命探查一处来历不明的洞府",
		"奉命携一则真假难辨的消息接近外宗修士",
		"被派去试探一桩久悬未决的旧怨",
		"携一件显眼之物引出暗中觊觎者"
	};

	internal static bool ShouldCheck(long familyId, in XjFamilyLedgerAggregate aggregate, int currentYear)
	{
		if (familyId <= 0L || currentYear <= 0 || aggregate.CultivatorCount <= 0
			|| aggregate.ZiFuCount <= 0 && aggregate.JinDanCount <= 0)
		{
			return false;
		}

		// 高境执棋并入50年级次级AI，且只使用5年家族批次能够命中的十个相位桶。
		int phase = XjDeterministicHash.PositiveIndex(familyId, "family.high_realm_chess.phase.v2", 10) * 5;
		if (currentYear % 50 != phase) return false;

		return XjDeterministicHash.PositiveIndex(
			familyId + currentYear,
			"family.high_realm_chess.trigger",
			100) < TriggerChancePercent;
	}

	internal static bool TryResolve(
		long sourceFamilyId,
		IReadOnlyList<XjFamilyMemberLedgerEntry> sourceEntries,
		int currentYear)
	{
		if (sourceFamilyId <= 0L || currentYear <= 0 || sourceEntries == null || sourceEntries.Count == 0)
		{
			return false;
		}

		Actor manipulator = ResolveManipulator(sourceEntries);
		if (manipulator == null)
		{
			return false;
		}
		if (XjActorAccessor.TryGetInt(manipulator, XjActorDataKeys.XjHighRealmChessHostedLastYear, out int hostedYear)
			&& hostedYear == currentYear)
		{
			return false;
		}

		int manipulatorOrder = RealmOrder(manipulator);
		if (!ResolveCrossFamilyPawn(sourceFamilyId, manipulator, manipulatorOrder, currentYear, out Actor pawn, out long targetFamilyId))
		{
			return false;
		}

		long manipulatorId = ActorId(manipulator);
		long pawnId = ActorId(pawn);
		XjActorAccessor.SetInt(manipulator, XjActorDataKeys.XjHighRealmChessHostedLastYear, currentYear);
		XjActorAccessor.SetInt(pawn, XjActorDataKeys.XjHighRealmChessLastYear, currentYear);

		int pawnOrder = RealmOrder(pawn);
		bool mayEscape = manipulatorOrder == XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.JinDan)
			&& pawnOrder == XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZiFu);
		string outcome = mayEscape && RollEscape(pawn, sourceFamilyId, manipulatorId, currentYear)
			? OutcomeEscaped
			: ResolveSuccessfulOutcome(manipulatorId, pawnId, currentYear);
		string action = Actions[XjDeterministicHash.PositiveIndex(
			pawnId + manipulatorId + currentYear,
			"family.high_realm_chess.action",
			Actions.Length)];

		if (string.Equals(outcome, OutcomeEscaped, StringComparison.Ordinal))
		{
			ApplyEscapeInsight(pawn);
		}
		else if (string.Equals(outcome, OutcomeSecret, StringComparison.Ordinal))
		{
			ApplyManipulatorInsight(manipulator);
		}
		else
		{
			ApplyManipulatorBenefit(manipulator);
		}

		RecordOutcome(sourceFamilyId, targetFamilyId, manipulator, pawn, currentYear, action, outcome);
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Family | XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
		return true;
	}

	private static Actor ResolveManipulator(IReadOnlyList<XjFamilyMemberLedgerEntry> entries)
	{
		Actor best = null;
		int bestRealm = -1;
		float bestZhenYuan = float.MinValue;
		long bestId = long.MaxValue;
		int ziFuOrder = XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZiFu);
		int jinDanOrder = XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.JinDan);
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyMemberLedgerEntry entry = entries[i];
			if (!entry.Found || !entry.IsAlive || entry.ActorId <= 0L
				|| !XjScheduler.ResolveActor(entry.ActorId, out Actor actor)
				|| !XjSafeCore.IsAliveActor(actor))
			{
				continue;
			}

			int realm = RealmOrder(actor);
			// 操控者只能是紫府或金丹。神丹、筑基、炼气与胎息均不进入该事件。
			if (realm != ziFuOrder && realm != jinDanOrder) continue;
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
			long id = ActorId(actor);
			if (best == null || realm > bestRealm
				|| realm == bestRealm && zhenYuan > bestZhenYuan
				|| realm == bestRealm && Math.Abs(zhenYuan - bestZhenYuan) < 0.001f && id < bestId)
			{
				best = actor;
				bestRealm = realm;
				bestZhenYuan = zhenYuan;
				bestId = id;
			}
		}
		return best;
	}

	private static bool ResolveCrossFamilyPawn(
		long sourceFamilyId,
		Actor manipulator,
		int manipulatorOrder,
		int currentYear,
		out Actor selected,
		out long targetFamilyId)
	{
		selected = null;
		targetFamilyId = 0L;
		IReadOnlyList<long> ids = XjCultivatorCandidateIndex.GetRealmEnteredIds();
		if (ids == null || ids.Count == 0) return false;

		long manipulatorId = ActorId(manipulator);
		int taiXiOrder = XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.TaiXi);
		int zhuJiOrder = XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZhuJi);
		int ziFuOrder = XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZiFu);
		int jinDanOrder = XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.JinDan);
		int maximumPawnOrder = manipulatorOrder == jinDanOrder
			? ziFuOrder
			: manipulatorOrder == ziFuOrder ? zhuJiOrder : -1;
		if (maximumPawnOrder < taiXiOrder) return false;
		int start = XjDeterministicHash.PositiveIndex(
			manipulatorId + sourceFamilyId + currentYear,
			"family.high_realm_chess.cross_family_pawn",
			ids.Count);
		int bestRealm = -1;
		float lowestMingShu = float.MaxValue;
		long bestId = long.MaxValue;

		int checks = Math.Min(ids.Count, MaxPawnCandidateChecks);
		for (int offset = 0; offset < checks; offset++)
		{
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L || candidateId == manipulatorId
				|| !XjScheduler.ResolveActor(candidateId, out Actor actor)
				|| !XjSafeCore.IsAliveActor(actor))
			{
				continue;
			}

			if (!TryResolveFamilyId(candidateId, out long familyId)
				|| familyId <= 0L || familyId == sourceFamilyId)
			{
				continue;
			}

			int realm = RealmOrder(actor);
			// 金丹仅可操控紫府及以下；紫府仅可操控筑基及以下。
			// “及以下”包含炼气与胎息，同境界及更高境界一律禁止。
			if (realm < taiXiOrder || realm > maximumPawnOrder) continue;
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHighRealmChessLastYear, out int lastYear)
				&& lastYear > 0 && currentYear - lastYear < PawnCooldownYears)
			{
				continue;
			}

			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float mingShu);
			long id = ActorId(actor);
			if (selected == null || realm > bestRealm
				|| realm == bestRealm && mingShu < lowestMingShu
				|| realm == bestRealm && Math.Abs(mingShu - lowestMingShu) < 0.001f && id < bestId)
			{
				selected = actor;
				targetFamilyId = familyId;
				bestRealm = realm;
				lowestMingShu = mingShu;
				bestId = id;
			}
		}
		return selected != null && targetFamilyId > 0L;
	}

	private static bool RollEscape(Actor pawn, long sourceFamilyId, long manipulatorId, int currentYear)
	{
		XjActorAccessor.TryGetFloat(pawn, XjActorDataKeys.HuiGuang, out float huiGuang);
		int chance = Math.Min(60, 25 + Math.Max(0, (int)Math.Floor(huiGuang / 4f)));
		return XjDeterministicHash.PositiveIndex(
			ActorId(pawn) + manipulatorId + sourceFamilyId + currentYear,
			"family.high_realm_chess.escape",
			100) < chance;
	}

	private static string ResolveSuccessfulOutcome(long manipulatorId, long pawnId, int currentYear)
	{
		return XjDeterministicHash.PositiveIndex(
			manipulatorId + pawnId + currentYear,
			"family.high_realm_chess.outcome",
			100) < 55 ? OutcomeSecret : OutcomeBenefit;
	}

	private static void ApplyEscapeInsight(Actor pawn)
	{
		XjActorAccessor.TryGetFloat(pawn, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.SetFloat(pawn, XjActorDataKeys.HuiGuang, XjDaoHuiPolicy.Add(huiGuang, 1f, XjDaoHuiPolicy.RareGrowthCeiling));
	}

	private static void ApplyManipulatorInsight(Actor manipulator)
	{
		XjActorAccessor.TryGetFloat(manipulator, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.SetFloat(manipulator, XjActorDataKeys.HuiGuang, XjDaoHuiPolicy.Add(huiGuang, 1f, XjDaoHuiPolicy.RareGrowthCeiling));
	}

	private static void ApplyManipulatorBenefit(Actor manipulator)
	{
		XjMingShuState.AddAcquired(manipulator, 1f);
	}

	private static void RecordOutcome(
		long sourceFamilyId,
		long targetFamilyId,
		Actor manipulator,
		Actor pawn,
		int currentYear,
		string action,
		string outcome)
	{
		long pawnId = ActorId(pawn);
		long manipulatorId = ActorId(manipulator);
		string pawnName = SafeName(pawn);
		string manipulatorName = XjFamilyAnnouncementNameFormatter.FormatHighRealmActor(manipulator);
		string sourceFamilyName = XjFamilyDisplayNameResolver.Resolve(sourceFamilyId);
		string targetFamilyName = XjFamilyDisplayNameResolver.Resolve(targetFamilyId);
		long targetSectId = XjSectRepository.ResolveActorSectId(pawn);
		long sourceSectId = XjSectRepository.ResolveActorSectId(manipulator);
		string summary;
		string title;
		string eventType;
		int importance;
		string result;

		if (string.Equals(outcome, OutcomeEscaped, StringComparison.Ordinal))
		{
			title = "识破上修棋局";
			eventType = "HighRealmChessEscaped";
			importance = 4;
			result = XjHistoryResult.Success;
			summary = pawnName + action + "，临事方察觉这一切皆是" + manipulatorName
				+ "借外族修士布下的棋局。其凭道慧窥见破绽，及时抽身，没有替旁人走完最后一步。";
		}
		else if (string.Equals(outcome, OutcomeSecret, StringComparison.Ordinal))
		{
			title = "借下修探知隐秘";
			eventType = "HighRealmChessSecret";
			importance = 3;
			result = XjHistoryResult.Change;
			summary = pawnName + action + "。待事情收束，他才明白自己一路所见所闻都被" + manipulatorName
				+ "拿去拼出了背后的隐秘；上修道慧因此增长，他却未得半分，徒作嫁衣。";
		}
		else
		{
			title = "借下修收取气数";
			eventType = "HighRealmChessBenefit";
			importance = 3;
			result = XjHistoryResult.Change;
			summary = pawnName + action + "。局成之后，" + manipulatorName
				+ "顺势收走了其中汇聚的气数，后天命数增长一点；直到此时，" + pawnName + "才知自己的奔走尽替他人作了嫁衣。";
		}

		string targetSummary = targetFamilyName + "族人" + summary;
		string sourceSummary = sourceFamilyName + "上修" + manipulatorName + "以" + pawnName + "为棋，"
			+ (string.Equals(outcome, OutcomeEscaped, StringComparison.Ordinal)
				? "却被对方临事识破，所谋未能尽成。"
				: string.Equals(outcome, OutcomeSecret, StringComparison.Ordinal)
					? "由此探明一桩隐秘，道慧增长一点。"
					: "由此截得一线气数，后天命数增长一点。");

		XjCenturyAnnalsStore.ObserveFamilyEvent(
			eventType,
			currentYear,
			targetFamilyId,
			importance,
			targetSummary,
			pawnId,
			pawnName);
		XjCenturyAnnalsStore.ObserveFamilyEvent(
			eventType + "Source",
			currentYear,
			sourceFamilyId,
			importance,
			sourceSummary,
			manipulatorId,
			manipulatorName);
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Vendetta,
			title,
			targetFamilyName + "与" + sourceFamilyName + "之间：" + summary,
			importance,
			actorId: pawnId,
			actorName: pawnName,
			sectId: targetSectId,
			familyId: targetFamilyId,
			year: currentYear,
			eventType: eventType,
			relatedActorId: manipulatorId,
			relatedActorName: manipulatorName,
			relatedFamilyId: sourceFamilyId,
			relatedSectId: sourceSectId,
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family
				| XjHistoryVisibility.Sect | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			result: result);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			summary,
			XjAnnouncementCategory.HighRealmInfluence,
			duration: 10f,
			color: string.Equals(outcome, OutcomeEscaped, StringComparison.Ordinal) ? "#9CD7FF" : "#B98AD9",
			delayFrames: 1);
	}

	private static bool TryResolveFamilyId(long actorId, out long familyId)
	{
		if (XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyId) && familyId > 0L)
		{
			return true;
		}
		if (XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found && entry.FamilyStableId > 0L)
		{
			familyId = entry.FamilyStableId;
			return true;
		}
		familyId = 0L;
		return false;
	}

	private static int RealmOrder(Actor actor)
	{
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return XjFamilyMemberLedger.GetRealmOrder(realmId);
	}

	private static long ActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static string SafeName(Actor actor)
	{
		string name = actor?.getName();
		return string.IsNullOrWhiteSpace(name) ? "未名修士" : name.Trim();
	}
}
