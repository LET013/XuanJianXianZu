using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{
	private const int SovereignFallbackScanIntervalYears = 3;
	private const float SovereignContestZhenYuanRatio = 0.18f;
	private const float SovereignContestVoiceGap = 12f;
	private static readonly HashSet<long> SovereignCandidateSeen = new HashSet<long>();
	private static readonly Dictionary<long, int> SovereignFallbackScanYear = new Dictionary<long, int>();

	private readonly struct SovereignCandidate
	{
		internal SovereignCandidate(Actor actor, long actorId, int realmOrder, float zhenYuan)
		{
			Actor = actor;
			ActorId = actorId;
			RealmOrder = realmOrder;
			ZhenYuan = zhenYuan;
		}

		internal Actor Actor { get; }
		internal long ActorId { get; }
		internal int RealmOrder { get; }
		internal float ZhenYuan { get; }
		internal bool Found => Actor?.data != null && ActorId > 0L;
	}

	internal static bool TryReconcileSovereign(long sectId, int currentYear, bool recordHistory)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L
			|| !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record) || record == null)
		{
			return false;
		}

		Actor current = null;
		bool currentValid = record.SovereignActorId > 0L
			&& XjScheduler.ResolveActor(record.SovereignActorId, out current)
			&& current?.data != null
			&& current.isAlive()
			&& ReadActorSectIdForSovereign(current) == record.SectId;
		bool currentQualified = currentValid
			&& (IsAtLeastZiFuForSovereign(current) || IsStrictZhuJiLateForSovereign(current));
		if (currentQualified)
		{
			// 宗主仍然存活、归属合法且满足任职门槛时保持在位。
			// 继任只在现任失效后触发，避免每年因出现更高境界候选而变成隐性竞选。
			SetActorSovereignMirror(current, record, Math.Max(0, currentYear));
			return false;
		}

		CollectSovereignCandidates(record, currentYear, out SovereignCandidate first, out SovereignCandidate second);
		if (!first.Found) return false;

		SovereignCandidate successor = first;
		bool contested = TryResolveSovereignContest(record, first, second, currentYear, out SovereignCandidate contestWinner,
			out long winnerFamilyId, out long loserFamilyId);
		if (contested) successor = contestWinner;
		if (successor.Actor?.data == null || !TryUpdateSovereign(record.SectId, successor.Actor, currentYear)) return false;

		string successorName = SafeActorName(successor.Actor);
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(successor.ActorId, out long successorFamilyId);
		if (contested)
		{
			TryApplySovereignContestOutcome(record.SectId, winnerFamilyId, loserFamilyId, currentYear);
			string winnerFamilyName = XjFamilyDisplayNameResolver.Resolve(winnerFamilyId);
			string loserFamilyName = XjFamilyDisplayNameResolver.Resolve(loserFamilyId);
			string summary = winnerFamilyName + "与" + loserFamilyName + "候选势均力敌，宗门遂行一次争位；"
				+ successorName + "胜出接掌宗门，胜负同时落入既有话语与供养记录。";
			if (recordHistory)
			{
				XjWorldHistoryStore.RecordDomainEvent(
					XjWorldHistoryCategory.Sect,
					(record.Name ?? "某宗") + "宗主争位",
					summary,
					4,
					actorId: successor.ActorId,
					actorName: successorName,
					sectId: record.SectId,
					familyId: winnerFamilyId,
					cityId: record.CapitalCityId,
					year: currentYear,
					eventType: "SectSovereignContested",
					visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate));
			}
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectSovereignContested",
				Math.Max(0, currentYear),
				record.SectId,
				record.Name,
				4,
				summary,
				successor.ActorId,
				successorName,
				winnerFamilyId);
		}
		else if (recordHistory)
		{
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				(record.Name ?? "某宗") + "宗主继任",
				successorName + "承接宗门权柄，暂摄宗主之位。",
				4,
				actorId: successor.ActorId,
				actorName: successorName,
				sectId: record.SectId,
				familyId: successorFamilyId,
				cityId: record.CapitalCityId,
				year: currentYear,
				eventType: "SectSovereignSucceeded",
				visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate));
		}

		if (recordHistory)
		{
			XjThreeBookWriter.RecordSectLeadership(
				record.SectId,
				record.Name,
				successor.ActorId,
				successorName,
				"宗主",
				currentYear,
				"sect|sovereign|" + record.SectId + "|" + successor.ActorId + "|" + currentYear);
		}
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.History);
		return true;
	}

	private static void CollectSovereignCandidates(
		XjSectArchiveRecord record,
		int currentYear,
		out SovereignCandidate first,
		out SovereignCandidate second)
	{
		first = default;
		second = default;
		if (record == null || record.SectId <= 0L) return;
		HashSet<long> seen = SovereignCandidateSeen;
		seen.Clear();
		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(record.SectId);
		for (int i = 0; i < actorIds.Count; i++)
		{
			EvaluateSovereignCandidate(record, actorIds[i], seen, ref first, ref second);
		}
		if (first.Found || !TryBeginSovereignFallbackScan(record.SectId, currentYear)) return;

		// 登名石、迁城和读档后的索引可能短暂滞后；仅在宗门完全无候选时稀疏兜底。
		IReadOnlyList<long> allCultivatorIds = XjCultivatorCache.GetAllIds();
		for (int i = 0; i < allCultivatorIds.Count; i++)
		{
			EvaluateSovereignCandidate(record, allCultivatorIds[i], seen, ref first, ref second);
		}
	}

	private static bool TryBeginSovereignFallbackScan(long sectId, int currentYear)
	{
		if (sectId <= 0L || currentYear <= 0) return false;
		if (SovereignFallbackScanYear.TryGetValue(sectId, out int lastYear)
			&& currentYear - lastYear < SovereignFallbackScanIntervalYears)
		{
			return false;
		}
		SovereignFallbackScanYear[sectId] = currentYear;
		return true;
	}

	private static void ClearSovereignRuntimeState()
	{
		SovereignCandidateSeen.Clear();
		SovereignFallbackScanYear.Clear();
	}

	private static void ForgetSovereignRuntimeState(long sectId)
	{
		if (sectId > 0L) SovereignFallbackScanYear.Remove(sectId);
	}

	private static void EvaluateSovereignCandidate(
		XjSectArchiveRecord record,
		long actorId,
		HashSet<long> seen,
		ref SovereignCandidate first,
		ref SovereignCandidate second)
	{
		if (record == null || actorId <= 0L || seen == null || !seen.Add(actorId)) return;
		if (!XjScheduler.ResolveActor(actorId, out Actor actor)
			|| actor?.data == null
			|| !actor.isAlive()
			|| ReadActorSectIdForSovereign(actor) != record.SectId
			|| (!IsAtLeastZiFuForSovereign(actor) && !IsStrictZhuJiLateForSovereign(actor)))
		{
			return;
		}

		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		SovereignCandidate candidate = new SovereignCandidate(
			actor,
			actorId,
			XjRealmHelper.GetOrder(realmId),
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float value) ? value : 0f);
		if (!first.Found || CompareSovereignCandidates(candidate, first) < 0)
		{
			second = first;
			first = candidate;
		}
		else if (!second.Found || CompareSovereignCandidates(candidate, second) < 0)
		{
			second = candidate;
		}
	}

	private static int CompareSovereignCandidates(in SovereignCandidate left, in SovereignCandidate right)
	{
		int realm = right.RealmOrder.CompareTo(left.RealmOrder);
		if (realm != 0) return realm;
		int zhenYuan = NormalizeSovereignMetric(right.ZhenYuan).CompareTo(NormalizeSovereignMetric(left.ZhenYuan));
		return zhenYuan != 0 ? zhenYuan : left.ActorId.CompareTo(right.ActorId);
	}

	private static float NormalizeSovereignMetric(float value)
	{
		return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
	}

	private static bool TryResolveSovereignContest(
		XjSectArchiveRecord record,
		in SovereignCandidate first,
		in SovereignCandidate second,
		int currentYear,
		out SovereignCandidate winner,
		out long winnerFamilyId,
		out long loserFamilyId)
	{
		winner = first;
		winnerFamilyId = 0L;
		loserFamilyId = 0L;
		if (record == null || !first.Found || !second.Found || first.RealmOrder != second.RealmOrder) return false;
		if (!XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(first.ActorId, out long firstFamilyId)
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(second.ActorId, out long secondFamilyId)
			|| firstFamilyId <= 0L || secondFamilyId <= 0L || firstFamilyId == secondFamilyId)
		{
			return false;
		}

		float zhenYuanGap = Math.Abs(first.ZhenYuan - second.ZhenYuan);
		float zhenYuanTolerance = Math.Max(2500f, Math.Max(first.ZhenYuan, second.ZhenYuan) * SovereignContestZhenYuanRatio);
		float firstVoice = ReadFamilyVoice(record.SectId, firstFamilyId);
		float secondVoice = ReadFamilyVoice(record.SectId, secondFamilyId);
		if (zhenYuanGap > zhenYuanTolerance || Math.Abs(firstVoice - secondVoice) > SovereignContestVoiceGap) return false;

		int firstWeight = Math.Max(1, (int)Math.Round(50f + firstVoice * 2f + first.ZhenYuan / Math.Max(1000f, zhenYuanTolerance)));
		int secondWeight = Math.Max(1, (int)Math.Round(50f + secondVoice * 2f + second.ZhenYuan / Math.Max(1000f, zhenYuanTolerance)));
		int totalWeight = Math.Min(100000, firstWeight + secondWeight);
		long seed = record.SectId * 1000003L + currentYear * 9176L + first.ActorId * 31L + second.ActorId;
		int roll = XjDeterministicHash.PositiveIndex(seed, "sect.sovereign.contest.v1", totalWeight);
		bool firstWins = roll < firstWeight;
		winner = firstWins ? first : second;
		winnerFamilyId = firstWins ? firstFamilyId : secondFamilyId;
		loserFamilyId = firstWins ? secondFamilyId : firstFamilyId;
		return true;
	}

	private static float ReadFamilyVoice(long sectId, long familyId)
	{
		return TryGetFamilySeat(sectId, familyId, out XjSectFamilySeatArchiveRecord seat) && seat != null
			? Math.Max(0f, seat.VoiceScore)
			: 0f;
	}

	private static long ReadActorSectIdForSovereign(Actor actor)
	{
		return ResolveActorSectId(actor);
	}

	private static bool IsAtLeastZiFuForSovereign(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return XjRealmHelper.GetOrder(realmId) >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
	}

	private static bool IsStrictZhuJiLateForSovereign(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZhuJi, StringComparison.Ordinal)) return false;
		float zhenYuan = XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float value) ? value : 0f;
		if (zhenYuan >= 24000f) return true;
		int xianJiCount = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int count) ? count : 0;
		return string.Equals(XjDaoXingStageRules.FormatDisplay(realmId, zhenYuan, xianJiCount, 0), "筑基后期", StringComparison.Ordinal);
	}
}
