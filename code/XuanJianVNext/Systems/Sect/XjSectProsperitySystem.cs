using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Architecture.Presentation;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Sect;

internal readonly struct XjSectProsperitySnapshot
{
	internal readonly int AliveMembers;
	internal readonly int ZiFuCount;
	internal readonly int JinDanCount;
	internal readonly int CityCount;
	internal readonly int FamilyCount;
	internal readonly bool HasSovereign;
	internal readonly bool HasOperationalFormation;

	internal XjSectProsperitySnapshot(int aliveMembers, int ziFuCount, int jinDanCount, int cityCount, int familyCount, bool hasSovereign, bool hasOperationalFormation)
	{
		AliveMembers = Math.Max(0, aliveMembers);
		ZiFuCount = Math.Max(0, ziFuCount);
		JinDanCount = Math.Max(0, jinDanCount);
		CityCount = Math.Max(0, cityCount);
		FamilyCount = Math.Max(0, familyCount);
		HasSovereign = hasSovereign;
		HasOperationalFormation = hasOperationalFormation;
	}
}

/// <summary>
/// 宗门兴衰以现有宗门成员稀疏索引五年结算；不扫描普通人口，也不维护逐帧政治状态。
/// </summary>
internal static class XjSectProsperitySystem
{
	private const int EvaluationIntervalYears = 5;
	private const int FoundingGraceYears = 20;
	private const int RemnantSectExtinctionPeriods = 3;
	private const int ProsperousAnnexThreshold = 70;
	private const int CriticalProsperityThreshold = 15;

	internal static bool Evaluate(long sectId, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || currentYear <= 0
			|| !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord record) || record == null)
		{
			return false;
		}
		// 权威成员表已经空掉时可O(1)判定灭门，不必等待五年兴衰结算窗口。
		if (XjSectAuthorityStore.CountMembers(sectId) == 0)
		{
			XjSectRepository.TryExtinguishSectByDecline(record.SectId, currentYear, "门人断绝，山门无人承继");
			return false;
		}

		// 治理车道在40x下会把待处理宗门保留到下一两个世界年再消费，因此传入年份
		// 可能是401/402而不是恰好的400。兴衰属于五年当前态维护：永远折叠到最近
		// 已完成的五年边界，不逐期回放，也不能因为 mutable scheduledYear 越过边界就漏算。
		int evaluationYear = currentYear - currentYear % EvaluationIntervalYears;
		if (evaluationYear <= 0 || evaluationYear <= record.LastProsperityYear) return true;

		XjSectProsperitySnapshot snapshot = BuildSnapshot(record);
		// 宗门权威成员已经为0时就是事实灭门，不再受20年初建宽限或连续衰败周期保护。
		// 该检查仍只发生在既有宗门治理队列中，不新增人口扫描。
		if (snapshot.AliveMembers == 0)
		{
			XjSectRepository.TryExtinguishSectByDecline(record.SectId, evaluationYear, "门人断绝，山门无人承继");
			return false;
		}

		int target = CalculateTarget(record, snapshot, evaluationYear);
		int previous = Math.Clamp(record.ProsperityValue, 0, 100);
		bool firstEvaluation = record.LastProsperityYear <= 0;

		// 旧档/高倍速若曾漏掉很多五年边界，不应再让一个百年老宗按“每次+10”从初建
		// 慢慢追几十年。这里仍只读取一次当前态快照，但按已跨过的周期放宽本次收敛步幅，
		// 上下行最多45点；不补播历史期、不重复扫描成员，也不会一帧循环几十次结算。
		int baselineYear = record.LastProsperityYear > 0
			? record.LastProsperityYear
			: Math.Max(0, record.FoundingYear);
		int elapsedPeriods = Math.Max(1, (evaluationYear - baselineYear) / EvaluationIntervalYears);
		int riseCap = Math.Min(45, 10 * Math.Min(5, elapsedPeriods));
		int fallCap = Math.Min(45, 15 * Math.Min(3, elapsedPeriods));
		int next = Math.Clamp(previous + Math.Clamp(target - previous, -fallCap, riseCap), 0, 100);
		string previousTier = string.IsNullOrWhiteSpace(record.ProsperityTier)
			? ResolveTier(previous)
			: record.ProsperityTier.Trim();
		record.PeakProsperityValue = Math.Clamp(Math.Max(record.PeakProsperityValue, next), 0, 100);
		string nextTier = ResolveLifecycleStage(record, evaluationYear, next);

		record.ProsperityValue = next;
		record.LastProsperityYear = evaluationYear;
		record.ProsperityTier = nextTier;
		record.ProsperitySummary = BuildSummary(snapshot, target, next, record.PeakProsperityValue, nextTier);
		if (snapshot.AliveMembers <= 2 && next <= CriticalProsperityThreshold)
		{
			record.ConsecutiveCriticalPeriods = Math.Max(0, record.ConsecutiveCriticalPeriods) + 1;
		}
		else if (snapshot.AliveMembers >= 5 || next >= 25)
		{
			record.ConsecutiveCriticalPeriods = 0;
		}
		else
		{
			record.ConsecutiveCriticalPeriods = Math.Max(0, record.ConsecutiveCriticalPeriods - 1);
		}

		XjSectRepository.RefreshFormationPower(sectId);
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Formation | XjCodexDirtyFlags.History);

		if (!firstEvaluation && !string.Equals(previousTier, nextTier, StringComparison.Ordinal))
		{
			XjThreeBookWriter.RecordSectProsperityChanged(record.SectId, record.Name, evaluationYear, previous, next, nextTier, record.ProsperitySummary);
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectProsperityChanged", evaluationYear, record.SectId, record.Name,
				next < previous ? 3 : 2,
				(record.Name ?? "某宗") + "兴衰由" + previous.ToString(CultureInfo.InvariantCulture) + "变为" + next.ToString(CultureInfo.InvariantCulture) + "（" + nextTier + "）");
		}

		bool graceExpired = evaluationYear - Math.Max(0, record.FoundingYear) >= FoundingGraceYears;
		bool remnantExtinction = snapshot.AliveMembers <= 2 && next <= 8 && record.ConsecutiveCriticalPeriods >= RemnantSectExtinctionPeriods;
		if (!graceExpired || !remnantExtinction) return true;

		if (TryAnnexByProsperousSect(record, snapshot, evaluationYear)) return false;
		XjSectRepository.TryExtinguishSectByDecline(record.SectId, evaluationYear,
			"门人凋零，宗脉再无力维系");
		return false;
	}

	internal static string ResolveTier(int prosperityValue)
	{
		int value = Math.Clamp(prosperityValue, 0, 100);
		return value >= 85 ? "鼎盛"
			: value >= 65 ? "兴盛"
			: value >= 45 ? "兴起"
			: value >= 30 ? "立足"
			: value >= 15 ? "衰败"
			: value > 0 ? "濒灭"
			: "覆灭边缘";
	}

	private static string ResolveLifecycleStage(
		XjSectArchiveRecord record,
		int currentYear,
		int current)
	{
		if (current <= 0) return "覆灭边缘";
		if (current < 15) return "濒灭";

		int age = Math.Max(0, currentYear - Math.Max(0, record?.FoundingYear ?? 0));
		int peak = Math.Max(current, Math.Clamp(record?.PeakProsperityValue ?? current, 0, 100));

		if (age < FoundingGraceYears && peak < 65) return "初建";
		// 一旦真正抵达过兴盛线，跌出兴盛后就进入衰退历程；
		// 只有重新恢复到65以上，才重新显示为兴盛，而不会回到“兴起”。
		if (peak >= 65 && current < 65)
		{
			return current < 30 ? "衰败" : "衰退";
		}
		return ResolveTier(current);
	}

	private static XjSectProsperitySnapshot BuildSnapshot(XjSectArchiveRecord record)
	{
		int alive = 0;
		int ziFu = 0;
		int jinDan = 0;
		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(record.SectId);
		for (int i = 0; i < actorIds.Count; i++)
		{
			long actorId = actorIds[i];
			if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
			if (XjSectRepository.ResolveActorSectId(actor) != record.SectId) continue;
			alive++;
			if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)) continue;
			if (realmTier >= XjRealmSuppression.TierJinDan) jinDan++;
			else if (realmTier >= XjRealmSuppression.TierZiFu) ziFu++;
		}
		bool hasSovereign = record.SovereignActorId > 0L
			&& XjScheduler.ResolveActor(record.SovereignActorId, out Actor sovereign)
			&& sovereign?.data != null && sovereign.isAlive()
			&& XjSectRepository.ResolveActorSectId(sovereign) == record.SectId;
		bool hasFormation = XjSectFormationRegistry.TryGetOperational(record.SectId, out _);
		return new XjSectProsperitySnapshot(
			alive, ziFu, jinDan, record.CityIds?.Count ?? 0, record.FamilyIds?.Count ?? 0, hasSovereign, hasFormation);
	}

	private static int CalculateTarget(XjSectArchiveRecord record, XjSectProsperitySnapshot snapshot, int currentYear)
	{
		int score = 15;
		score += Math.Min(36, snapshot.AliveMembers * 2);
		score += Math.Min(15, snapshot.CityCount * 5);
		score += Math.Min(8, snapshot.FamilyCount * 2);
		score += Math.Min(12, snapshot.ZiFuCount * 4);
		score += Math.Min(18, snapshot.JinDanCount * 9);
		score += snapshot.HasSovereign ? 5 : -8;
		if (record.SecretRealmId > 0L) score += 4;
		if (snapshot.HasOperationalFormation) score += 3;
		if (record.LastTaskYear > 0 && currentYear - record.LastTaskYear <= 10) score += 4;
		if (snapshot.AliveMembers == 0) score -= 45;
		else if (snapshot.AliveMembers <= 2) score -= 25;
		else if (snapshot.AliveMembers <= 4) score -= 12;
		if (snapshot.CityCount <= 0) score -= 40;
		return Math.Clamp(score, 0, 100);
	}

	private static string BuildSummary(
		XjSectProsperitySnapshot snapshot,
		int target,
		int current,
		int peak,
		string stage)
	{
		return "门人" + snapshot.AliveMembers.ToString(CultureInfo.InvariantCulture)
			+ "、城镇" + snapshot.CityCount.ToString(CultureInfo.InvariantCulture)
			+ "、世家" + snapshot.FamilyCount.ToString(CultureInfo.InvariantCulture)
			+ "、真人" + snapshot.ZiFuCount.ToString(CultureInfo.InvariantCulture)
			+ "、真君" + snapshot.JinDanCount.ToString(CultureInfo.InvariantCulture)
			+ "；本期底势" + target.ToString(CultureInfo.InvariantCulture)
			+ "，兴衰结算" + current.ToString(CultureInfo.InvariantCulture)
			+ "，历史峰值" + peak.ToString(CultureInfo.InvariantCulture)
			+ "，历程“" + (string.IsNullOrWhiteSpace(stage) ? "未评" : stage) + "”";
	}

	private static bool TryAnnexByProsperousSect(XjSectArchiveRecord victim, XjSectProsperitySnapshot victimSnapshot, int currentYear)
	{
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		IReadOnlyList<XjSectHostilityArchiveRecord> hostilities = XjSectWarSystem.ReadHostilities();
		long bestSectId = 0L;
		int bestScore = int.MinValue;
		string bestName = string.Empty;
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord candidate = sects[i];
			if (candidate == null || candidate.SectId <= 0L || candidate.SectId == victim.SectId
				|| candidate.ProsperityValue < ProsperousAnnexThreshold
				|| (candidate.CityIds?.Count ?? 0) + Math.Max(1, victim.CityIds?.Count ?? 0) > XjSectRepository.MaxSectCityCount) continue;
			XjSectProsperitySnapshot candidateSnapshot = BuildSnapshot(candidate);
			if (candidateSnapshot.AliveMembers < Math.Max(6, victimSnapshot.AliveMembers * 3)
				|| candidateSnapshot.ZiFuCount + candidateSnapshot.JinDanCount <= 0) continue;
			int hostility = ResolveHostility(hostilities, candidate.SectId, victim.SectId);
			int score = candidate.ProsperityValue * 10 + candidateSnapshot.AliveMembers * 2
				+ candidateSnapshot.ZiFuCount * 20 + candidateSnapshot.JinDanCount * 50 + hostility * 3;
			if (score <= bestScore) continue;
			bestScore = score;
			bestSectId = candidate.SectId;
			bestName = candidate.Name ?? string.Empty;
		}
		if (bestSectId <= 0L) return false;
		string victorName = string.IsNullOrWhiteSpace(bestName) ? "兴盛宗门" : bestName.Trim();
		return XjSectRepository.TryDefeatSectByFormationBreak(
			victim.SectId, bestSectId, currentYear,
			"因兴衰耗尽、山门空虚，被" + victorName + "趁势攻破");
	}

	private static int ResolveHostility(IReadOnlyList<XjSectHostilityArchiveRecord> records, long left, long right)
	{
		if (records == null) return 0;
		for (int i = 0; i < records.Count; i++)
		{
			XjSectHostilityArchiveRecord record = records[i];
			if (record == null) continue;
			if ((record.LeftSectId == left && record.RightSectId == right)
				|| (record.LeftSectId == right && record.RightSectId == left)) return Math.Max(0, record.Hostility);
		}
		return 0;
	}
}

internal static partial class XjSectRepository
{
	internal static bool TryExtinguishSectByDecline(long sectId, int currentYear, string reason)
	{
		if (sectId <= 0L || !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record) || record == null) return false;
		string sectName = string.IsNullOrWhiteSpace(record.Name) ? "某宗" : record.Name.Trim();
		string summary = string.IsNullOrWhiteSpace(reason) ? "兴衰耗尽，宗脉断绝" : reason.Trim();
		XjThreeBookWriter.RecordSectDeclineExtinct(record.SectId, sectName, currentYear, summary);
		XjCenturyAnnalsStore.ObserveSectEvent("SectDeclineExtinct", Math.Max(0, currentYear), record.SectId, sectName, 5, sectName + summary + "，宗门就此除名。");
		string body = "【宗门衰亡】" + sectName + summary + "，门中传承无人维系，宗门自天下宗谱除名。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Sect,
			sectName + "衰亡",
			body,
			5, true, sectId: record.SectId, cityId: record.CapitalCityId, year: currentYear);
		XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			body, XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.Sect, duration: 10f, color: "#D94C4C", delayFrames: 1);
		RetireSectRecord(record, currentYear, "ProsperityExhausted");
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.City | XjCodexDirtyFlags.Family
			| XjCodexDirtyFlags.Formation | XjCodexDirtyFlags.Conflict | XjCodexDirtyFlags.History);
		XjPresentationHooks.MarkSectMapDirty();
		return true;
	}
}
