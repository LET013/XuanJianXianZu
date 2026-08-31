using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门传承覆盖不是“最高品级=自动掌握以下全部”。按 道途×五品/六品/求金法
/// 分格记录真实缺口，借鉴组织知识树的精确掌握方式；每十年检查宗内实际存在的前六条主要道途，
/// 但每宗至多主动处理一个最高优先缺口。通过借抄/通宗换法、洞天访法、门内高修补撰、
/// 延揽同道或把既有敌对升级为传承之争来解决，避免为了“全覆盖”制造年度高压。
/// </summary>
internal static class XjSectTransmissionCoverageSystem
{
	private const int IntervalYears = 10;
	private static int _lastProcessedYear;
	private static readonly Dictionary<long, string> PriorityDaoTuBySectId = new Dictionary<long, string>();
	private static readonly HashSet<long> PendingActiveClaimSectIds = new HashSet<long>();
	// 每十年逐宗处理时复用需求分析容器。每宗事务在单线程年度车道中原子完成，
	// 不会跨帧保存这些 scratch 引用，因此无需为每个宗门重新分配 1 List + 6 Dictionary。
	private static readonly List<SectDemand> DemandScratch = new List<SectDemand>(6);
	private static readonly Dictionary<string, int> ScoreByDaoTuScratch = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly Dictionary<string, long> RepresentativeByDaoTuScratch = new Dictionary<string, long>(StringComparer.Ordinal);
	private static readonly Dictionary<string, string> RepresentativeNameByDaoTuScratch = new Dictionary<string, string>(StringComparer.Ordinal);
	private static readonly Dictionary<string, int> RepresentativeScoreByDaoTuScratch = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly Dictionary<string, bool> HasHighByDaoTuScratch = new Dictionary<string, bool>(StringComparer.Ordinal);
	private static readonly Dictionary<string, bool> HasZiJinZiFuByDaoTuScratch = new Dictionary<string, bool>(StringComparer.Ordinal);
	private static IReadOnlyList<XjSectArchiveRecord> PendingAnnualSects = Array.Empty<XjSectArchiveRecord>();
	private static int _pendingAnnualYear;
	private static int _pendingAnnualIndex;
	private static int _pendingRequired;
	private static int _pendingMissing;
	private static int _pendingHandled;
	internal static float LastGlobalGapRatio { get; private set; }
	internal static string LastGlobalSummary { get; private set; } = "尚未结算宗门传承覆盖。";

	internal static void Clear()
	{
		ClearPendingAnnualWork();
		_lastProcessedYear = 0;
		PriorityDaoTuBySectId.Clear();
		DemandScratch.Clear();
		ScoreByDaoTuScratch.Clear();
		RepresentativeByDaoTuScratch.Clear();
		RepresentativeNameByDaoTuScratch.Clear();
		RepresentativeScoreByDaoTuScratch.Clear();
		HasHighByDaoTuScratch.Clear();
		HasZiJinZiFuByDaoTuScratch.Clear();
		LastGlobalGapRatio = 0f;
		LastGlobalSummary = "尚未结算宗门传承覆盖。";
	}

	private readonly struct SectDemand
	{
		internal readonly string DaoTu;
		internal readonly bool NeedsGrade6;
		internal readonly bool NeedsQiuJin;
		internal readonly long RepresentativeActorId;
		internal readonly string RepresentativeActorName;
		internal readonly int Score;
		internal SectDemand(string daoTu, bool needsGrade6, bool needsQiuJin, long actorId, string actorName, int score)
		{
			DaoTu = daoTu ?? string.Empty;
			NeedsGrade6 = needsGrade6;
			NeedsQiuJin = needsQiuJin;
			RepresentativeActorId = actorId;
			RepresentativeActorName = actorName ?? string.Empty;
			Score = score;
		}
	}

	private enum GapKind { None, Grade5, Grade6, QiuJin }

	internal static bool HasPendingForYear(int currentYear)
	{
		return currentYear > 0
			&& _pendingAnnualYear == currentYear
			&& _pendingAnnualIndex < PendingAnnualSects.Count;
	}

	internal static bool BeginYear(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0) return false;
		if (HasPendingForYear(currentYear)) return true;
		if (_lastProcessedYear > 0 && currentYear - _lastProcessedYear < IntervalYears) return false;

		ClearPendingAnnualWork();
		PriorityDaoTuBySectId.Clear();
		PendingAnnualSects = XjSectRepository.ReadAllSects();
		XjAdventureRealmClaimSystem.FillActiveClaimSectIds(PendingActiveClaimSectIds);
		_pendingAnnualYear = currentYear;
		_pendingAnnualIndex = 0;
		_pendingRequired = 0;
		_pendingMissing = 0;
		_pendingHandled = 0;
		if (PendingAnnualSects.Count > 0) return true;
		CompletePendingAnnualWork();
		return false;
	}

	/// <summary>
	/// Ten-year inheritance coverage now yields between sects. Per-sect demand
	/// resolution remains atomic so one sect cannot be half-mutated across frames.
	/// </summary>
	internal static bool TickPending(int itemBudget, double timeBudgetMs)
	{
		if (_pendingAnnualYear <= 0 || _pendingAnnualIndex >= PendingAnnualSects.Count)
		{
			if (_pendingAnnualYear > 0) CompletePendingAnnualWork();
			return true;
		}

		XjCooperativeBudget budget = new XjCooperativeBudget(
			Math.Max(1, itemBudget),
			timeBudgetMs,
			XjRuntimeFramePriority.Background);
		while (_pendingAnnualIndex < PendingAnnualSects.Count && budget.TryTake())
		{
			XjSectArchiveRecord sect = PendingAnnualSects[_pendingAnnualIndex++];
			ProcessAnnualSect(sect, _pendingAnnualYear);
		}

		if (_pendingAnnualIndex < PendingAnnualSects.Count) return false;
		CompletePendingAnnualWork();
		return true;
	}

	internal static void TickYear(int currentYear)
	{
		if (!BeginYear(currentYear)) return;
		while (_pendingAnnualIndex < PendingAnnualSects.Count)
		{
			XjSectArchiveRecord sect = PendingAnnualSects[_pendingAnnualIndex++];
			ProcessAnnualSect(sect, currentYear);
		}
		CompletePendingAnnualWork();
	}

	private static void ProcessAnnualSect(XjSectArchiveRecord sect, int currentYear)
	{
		if (sect == null || sect.SectId <= 0L || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)) return;
		List<SectDemand> demands = BuildDemands(sect);
		if (demands.Count == 0) return;

		SectDemand priorityDemand = default;
		GapKind priorityGap = GapKind.None;
		for (int demandIndex = 0; demandIndex < demands.Count; demandIndex++)
		{
			SectDemand demand = demands[demandIndex];
			GapKind gap = ResolveGap(sect.SectId, demand, out int sectRequired, out int sectMissing);
			_pendingRequired += sectRequired;
			_pendingMissing += sectMissing;
			if (priorityGap == GapKind.None && gap != GapKind.None)
			{
				priorityDemand = demand;
				priorityGap = gap;
			}
		}

		if (priorityGap == GapKind.None) return;
		PriorityDaoTuBySectId[sect.SectId] = priorityDemand.DaoTu;
		if (_pendingHandled < 12
			&& TryHandleOneGap(sect, priorityDemand, priorityGap, PendingAnnualSects, currentYear))
		{
			_pendingHandled++;
		}
	}

	private static void CompletePendingAnnualWork()
	{
		if (_pendingAnnualYear > 0)
		{
			_lastProcessedYear = Math.Max(_lastProcessedYear, _pendingAnnualYear);
			LastGlobalGapRatio = _pendingRequired <= 0
				? 0f
				: Math.Clamp((float)_pendingMissing / _pendingRequired, 0f, 1f);
			LastGlobalSummary = "传承格" + _pendingRequired + "，缺口" + _pendingMissing
				+ "，本轮主动补缺" + _pendingHandled + "；按宗内实际道途逐格核验。";
		}
		ClearPendingAnnualWork();
	}

	internal static void ClearPendingAnnualWork()
	{
		PendingAnnualSects = Array.Empty<XjSectArchiveRecord>();
		PendingActiveClaimSectIds.Clear();
		_pendingAnnualYear = 0;
		_pendingAnnualIndex = 0;
		_pendingRequired = 0;
		_pendingMissing = 0;
		_pendingHandled = 0;
	}

	/// <summary>
	/// 招募流程只读本系统十年诊断留下的最高优先缺口，不新增人口扫描。
	/// 命中该缺口道途时把原有50%招募门槛提高到80%，使“延揽同道”成为真实AI倾向。
	/// </summary>
	internal static bool IsPriorityRecruitmentDaoTu(long sectId, Actor actor)
	{
		if (sectId <= 0L || actor?.data == null || !PriorityDaoTuBySectId.TryGetValue(sectId, out string priorityDaoTu)
			|| string.IsNullOrWhiteSpace(priorityDaoTu)) return false;
		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu)
			&& string.Equals(Normalize(actorDaoTu), Normalize(priorityDaoTu), StringComparison.Ordinal);
	}

	private static List<SectDemand> BuildDemands(XjSectArchiveRecord sect)
	{
		List<SectDemand> demands = DemandScratch;
		demands.Clear();
		Dictionary<string, int> scoreByDaoTu = ScoreByDaoTuScratch;
		Dictionary<string, long> representativeByDaoTu = RepresentativeByDaoTuScratch;
		Dictionary<string, string> representativeNameByDaoTu = RepresentativeNameByDaoTuScratch;
		Dictionary<string, int> representativeScoreByDaoTu = RepresentativeScoreByDaoTuScratch;
		Dictionary<string, bool> hasHighByDaoTu = HasHighByDaoTuScratch;
		Dictionary<string, bool> hasZiJinZiFuByDaoTu = HasZiJinZiFuByDaoTuScratch;
		scoreByDaoTu.Clear();
		representativeByDaoTu.Clear();
		representativeNameByDaoTu.Clear();
		representativeScoreByDaoTu.Clear();
		hasHighByDaoTu.Clear();
		hasZiJinZiFuByDaoTu.Clear();

		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sect.SectId);
		if (actorIds == null || actorIds.Count == 0) return demands;
		int ziFuOrder = XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
		int jinDanOrder = XjRealmHelper.GetOrder(XjRealmIds.JinDan);
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(actorIds[i], out Actor actor) || actor?.data == null || !actor.isAlive()
				|| XjSectRepository.ResolveActorSectId(actor) != sect.SectId) continue;
			if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu) || string.IsNullOrWhiteSpace(daoTu)) continue;
			daoTu = daoTu.Trim();
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			int order = XjRealmHelper.GetOrder(realmId);
			int score = order >= jinDanOrder ? 9 : order >= ziFuOrder ? 6 : order > 0 ? 2 : 1;
			scoreByDaoTu.TryGetValue(daoTu, out int previous);
			scoreByDaoTu[daoTu] = previous + score;
			representativeScoreByDaoTu.TryGetValue(daoTu, out int previousRepresentativeScore);
			if (!representativeByDaoTu.ContainsKey(daoTu) || score > previousRepresentativeScore)
			{
				representativeByDaoTu[daoTu] = ((BaseSystemData)actor.data).id;
				representativeNameByDaoTu[daoTu] = XjStringHelper.ActorName(actor, "门中高修");
				representativeScoreByDaoTu[daoTu] = score;
			}
			if (order >= ziFuOrder) hasHighByDaoTu[daoTu] = true;
			if (order >= ziFuOrder && XjCultivationPathRules.IsZiFuJinDan(actor)) hasZiJinZiFuByDaoTu[daoTu] = true;
		}

		foreach (KeyValuePair<string, int> pair in scoreByDaoTu)
		{
			hasHighByDaoTu.TryGetValue(pair.Key, out bool high);
			hasZiJinZiFuByDaoTu.TryGetValue(pair.Key, out bool ziJinHigh);
			representativeByDaoTu.TryGetValue(pair.Key, out long representativeId);
			representativeNameByDaoTu.TryGetValue(pair.Key, out string representativeName);
			if (representativeId <= 0L) continue;
			demands.Add(new SectDemand(pair.Key, high, ziJinHigh, representativeId, representativeName, pair.Value));
		}
		demands.Sort((left, right) =>
		{
			int score = right.Score.CompareTo(left.Score);
			return score != 0 ? score : string.Compare(left.DaoTu, right.DaoTu, StringComparison.Ordinal);
		});
		if (demands.Count > 6) demands.RemoveRange(6, demands.Count - 6);
		return demands;
	}

	private static GapKind ResolveGap(long sectId, in SectDemand demand, out int required, out int missing)
	{
		required = 1 + (demand.NeedsGrade6 ? 1 : 0) + (demand.NeedsQiuJin ? 1 : 0);
		missing = 0;
		bool has5 = HasGongFa(sectId, demand.DaoTu, 5);
		bool has6 = !demand.NeedsGrade6 || HasGongFa(sectId, demand.DaoTu, 6);
		bool hasQiuJin = !demand.NeedsQiuJin || HasQiuJinFa(sectId, demand.DaoTu);
		if (!has5) missing++;
		if (!has6) missing++;
		if (!hasQiuJin) missing++;
		if (!has5) return GapKind.Grade5;
		if (!has6) return GapKind.Grade6;
		if (!hasQiuJin) return GapKind.QiuJin;
		return GapKind.None;
	}

	private static bool TryHandleOneGap(XjSectArchiveRecord sect, in SectDemand demand, GapKind gap, IReadOnlyList<XjSectArchiveRecord> sects, int currentYear)
	{
		if (TryAcquireFromFriendlySect(sect, demand, gap, sects, currentYear, out string action))
		{
			RecordAction(sect, demand, currentYear, action, "TransmissionExternalAcquisition");
			return true;
		}

		bool hasRealm = sect.SecretRealmId > 0L || HasClaimedAdventureRealm(sect.SectId);
		if (gap != GapKind.QiuJin && hasRealm && TryCreateMissingGongFa(sect, demand, gap, currentYear, "洞天访法", out action))
		{
			RecordAction(sect, demand, currentYear, action, "TransmissionRealmRecovery");
			return true;
		}

		if (gap != GapKind.QiuJin && TryCreateMissingGongFa(sect, demand, gap, currentYear, "门内补撰", out action))
		{
			RecordAction(sect, demand, currentYear, action, "TransmissionInternalCompletion");
			return true;
		}

		if (TryEscalateInheritanceRivalry(sect, demand, gap, sects, currentYear, out action))
		{
			RecordAction(sect, demand, currentYear, action, "TransmissionRivalry");
			return true;
		}

		// 求金法等不能无中生有的缺口只发布求法方向，让现有收徒、借法、秘境和战争系统
		// 在后续真实世界行为中解决；不直接移动人物、不凭空复制陌生传承。
		string gapName = gap == GapKind.QiuJin ? "求金法" : gap == GapKind.Grade6 ? "六品功法" : "五品功法";
		RecordAction(sect, demand, currentYear,
			"门中检出【" + demand.DaoTu + "】" + gapName + "缺口，遂把求法、访秘境、延揽同道列为近年山门要务。",
			"TransmissionRecruitmentIntent");
		return true;
	}

	private static bool TryAcquireFromFriendlySect(XjSectArchiveRecord target, in SectDemand demand, GapKind gap, IReadOnlyList<XjSectArchiveRecord> sects, int currentYear, out string action)
	{
		action = string.Empty;
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord source = sects[i];
			if (source == null || source.SectId <= 0L || source.SectId == target.SectId
				|| string.Equals(source.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
				|| XjSectWarSystem.GetHostility(target.SectId, source.SectId) > 0) continue;

			if (gap == GapKind.QiuJin)
			{
				IReadOnlyList<XjSectGongFaPavilionEntry> qiuJin = XjSectGongFaPavilion.ReadQiuJinFaEntries(source.SectId);
				for (int j = 0; j < qiuJin.Count; j++)
				{
					XjSectGongFaPavilionEntry entry = qiuJin[j];
					if (!entry.Found || !string.Equals(Normalize(entry.DaoTu), Normalize(demand.DaoTu), StringComparison.Ordinal)) continue;
					if (!XjSectGongFaPavilion.TryAddQiuJinFa(target.SectId, target.Name, entry.ActorId, entry.ActorName,
						entry.Name, entry.Grade, entry.DaoTu, entry.SourceGongFaName, entry.MappedXianJi, currentYear)) continue;
					bool borrow = XjDeterministicHash.PositiveIndex(target.SectId + source.SectId + currentYear, "sect_transmission_qiujin_acquire", 100) < 45;
					action = borrow
						? (target.Name ?? "某宗") + "遣人至" + (source.Name ?? "他宗") + "借抄求金法，补入【" + entry.Name + "】。"
						: (target.Name ?? "某宗") + "与" + (source.Name ?? "他宗") + "通宗换法，补入【" + entry.Name + "】。";
					return true;
				}
				continue;
			}

			int grade = gap == GapKind.Grade6 ? 6 : 5;
			IReadOnlyList<XjSectGongFaPavilionEntry> entries = XjSectGongFaPavilion.ReadGongFaEntries(source.SectId);
			for (int j = 0; j < entries.Count; j++)
			{
				XjSectGongFaPavilionEntry entry = entries[j];
				if (!entry.Found || entry.Grade != grade || !string.Equals(Normalize(entry.DaoTu), Normalize(demand.DaoTu), StringComparison.Ordinal)) continue;
				bool borrow = XjDeterministicHash.PositiveIndex(target.SectId + source.SectId + currentYear + grade, "sect_transmission_gongfa_acquire", 100) < 45;
				string sourceLabel = borrow ? "通宗借抄" : "宗门换法";
				if (!XjSectGongFaPavilion.TryAddGongFa(target.SectId, target.Name, entry.ActorId, entry.ActorName,
					entry.Name, entry.Grade, entry.DaoTu, sourceLabel, entry.MappedXianJi, currentYear)) continue;
				action = borrow
					? (target.Name ?? "某宗") + "遣人至" + (source.Name ?? "他宗") + "借抄【" + entry.Name + "】，录入功法阁。"
					: (target.Name ?? "某宗") + "与" + (source.Name ?? "他宗") + "通宗换法，补入【" + entry.Name + "】。";
				return true;
			}
		}
		return false;
	}

	private static bool TryEscalateInheritanceRivalry(
		XjSectArchiveRecord target,
		in SectDemand demand,
		GapKind gap,
		IReadOnlyList<XjSectArchiveRecord> sects,
		int currentYear,
		out string action)
	{
		action = string.Empty;
		// 不因“别人有功法”凭空造仇。只有双方已经有明显敌意，且敌宗确实掌握本宗缺失的
		// 精确传承格，才把既有冲突升级为传承之争；后续是否宣战仍交给宗门战争权威系统。
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord source = sects[i];
			if (source == null || source.SectId <= 0L || source.SectId == target.SectId
				|| string.Equals(source.Status, XjSectStatus.Extinct, StringComparison.Ordinal)) continue;
			int hostility = XjSectWarSystem.GetHostility(target.SectId, source.SectId);
			if (hostility < 40 || !HasExactGapAsset(source.SectId, demand.DaoTu, gap)) continue;
			if (!XjSectWarSystem.AddHostility(target.SectId, source.SectId, 8, currentYear, "传承之争")) continue;
			action = (target.Name ?? "某宗") + "久缺【" + demand.DaoTu + "】"
				+ (gap == GapKind.QiuJin ? "求金法" : gap == GapKind.Grade6 ? "六品功法" : "五品功法")
				+ "，而宿敌" + (source.Name ?? "他宗") + "恰掌此法，旧怨遂添传承之争。";
			return true;
		}
		return false;
	}

	private static bool HasExactGapAsset(long sectId, string daoTu, GapKind gap)
	{
		return gap == GapKind.QiuJin ? HasQiuJinFa(sectId, daoTu)
			: HasGongFa(sectId, daoTu, gap == GapKind.Grade6 ? 6 : 5);
	}

	private static bool TryCreateMissingGongFa(XjSectArchiveRecord sect, in SectDemand demand, GapKind gap, int currentYear, string sourceLabel, out string action)
	{
		action = string.Empty;
		int grade = gap == GapKind.Grade6 ? 6 : 5;
		if (grade == 6 && !demand.NeedsGrade6) return false;
		long contributorId = demand.RepresentativeActorId;
		string contributorName = demand.RepresentativeActorName;
		if (XjGuoWeiDutySystem.TryResolveSectFruitHolder(sect.SectId, demand.DaoTu, out Actor fruitHolder) && fruitHolder?.data != null)
		{
			contributorId = ((BaseSystemData)fruitHolder.data).id;
			contributorName = XjStringHelper.ActorName(fruitHolder, "门中高修");
			sourceLabel = "持果护道补撰";
		}
		if (contributorId <= 0L) return false;
		string name = XjGongFaNameLibrary.GenerateName(demand.DaoTu, grade,
			sect.SectId * 7919L + currentYear * 101L + grade * 17L);
		if (!XjSectGongFaPavilion.TryAddGongFa(sect.SectId, sect.Name, contributorId, contributorName,
			name, grade, demand.DaoTu, sourceLabel, string.Empty, currentYear)) return false;
		action = (sect.Name ?? "某宗") + "由" + contributorName + sourceLabel + "，将【" + name + "】归入功法阁。";
		return true;
	}

	private static bool HasGongFa(long sectId, string daoTu, int grade)
	{
		IReadOnlyList<XjSectGongFaPavilionEntry> entries = XjSectGongFaPavilion.ReadGongFaEntries(sectId);
		for (int i = 0; i < entries.Count; i++)
		{
			if (entries[i].Found && entries[i].Grade == grade
				&& string.Equals(Normalize(entries[i].DaoTu), Normalize(daoTu), StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static bool HasQiuJinFa(long sectId, string daoTu)
	{
		IReadOnlyList<XjSectGongFaPavilionEntry> entries = XjSectGongFaPavilion.ReadQiuJinFaEntries(sectId);
		for (int i = 0; i < entries.Count; i++)
		{
			if (entries[i].Found && string.Equals(Normalize(entries[i].DaoTu), Normalize(daoTu), StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static bool HasClaimedAdventureRealm(long sectId)
	{
		return sectId > 0L && PendingActiveClaimSectIds.Contains(sectId);
	}

	private static void RecordAction(XjSectArchiveRecord sect, in SectDemand demand, int currentYear, string body, string eventType)
	{
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Inheritance,
			(sect.Name ?? "某宗") + "整饬传承",
			body,
			3,
			actorId: demand.RepresentativeActorId,
			actorName: demand.RepresentativeActorName,
			sectId: sect.SectId,
			cityId: sect.CapitalCityId,
			year: currentYear,
			eventType: eventType + ":" + demand.DaoTu,
			visibilityFlags: (int)(XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
			mirrorToWorldLog: false);
	}

	private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
