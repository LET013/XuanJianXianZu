using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.DongTian;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门层的低频高境战争。它只处理 SectId 对 SectId 的高境交锋与护宗大阵，
/// 不创建原生国家战争、不改变城市的 KingdomId、不更换国王，也不把紫府/金丹加入凡人军队。
/// 但宗门大战是真实的高境动员：本宗金丹/真君羽士会立即中止普通洞天闭关，
/// 作为高境战线参战者计入战力；战争结束后才重新允许进入日常闭关。
/// 破阵灭宗时只重绑城市的宗门层 SectId，并按既有吞并事务转移成员与物资。
/// </summary>
internal static partial class XjSectWarSystem
{
	private sealed class Candidate
	{
		internal XjSectArchiveRecord Sect;
		internal float Score;
		internal int HighestTier;
		internal List<long> Participants = new List<long>();
	}

	private const int MaxRetainedRecords = 48;
	private const int MaxActiveWars = 2;
	private const int MaxActiveWarsProcessedPerYear = 2;
	private const int MaxParticipantsPerSide = 8;
	private const int SamePairCooldownYears = 20;
	private const int MaxWarDurationYears = 10;
	private const float MinimumStartingScore = 8f;
	private const float MinimumAggressorScoreRatio = 0.75f;
	private const float LowerRealmCompensationRatio = 1.50f;

	private static readonly Dictionary<long, XjSectWarArchiveRecord> RecordsByWarId = new Dictionary<long, XjSectWarArchiveRecord>();
	private static readonly Dictionary<(long Left, long Right), int> LastPairYear = new Dictionary<(long, long), int>();
	private static readonly List<XjSectWarArchiveRecord> WarBuffer = new List<XjSectWarArchiveRecord>();

	internal static bool HasActiveWars => CountActiveWars() > 0;

	/// <summary>
	/// O(active wars) 查询宗门是否正处于玄鉴宗门战争。供高境闭关入口做硬门禁，
	/// 避免战争已经爆发后金丹又在同一年度重新钻回洞天。
	/// </summary>
	internal static bool IsSectInActiveWar(long sectId)
	{
		return sectId > 0L && HasActiveSect(sectId);
	}

	internal static bool IsActorSectInActiveWar(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		return IsSectInActiveWar(sectId);
	}

	internal static void TickYear(int currentYear)
	{
		ClearPendingAnnualWork();
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0)
		{
			return;
		}

		bool changed = XjFamilyVendettaRegistry.TickYear(currentYear);
		changed |= TickHostilityAndConflicts(currentYear);
		changed |= ProcessActiveWars(currentYear);

		if (changed)
		{
			PruneRecords();
			MarkChanged();
		}
	}

	/// <summary>
	/// 给后续事件链或玩家调试入口使用。这里只建立宗门战争账本，不触碰国家外交。
	/// </summary>
	internal static bool TryStartWar(long attackerSectId, long defenderSectId, int currentYear, string reason)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled
			|| currentYear <= 0
			|| attackerSectId <= 0L
			|| defenderSectId <= 0L
			|| attackerSectId == defenderSectId
			|| CountActiveWars() >= MaxActiveWars
			|| HasActiveSect(attackerSectId)
			|| HasActiveSect(defenderSectId)
			|| HasActivePair(attackerSectId, defenderSectId)
			|| IsPairOnCooldown(attackerSectId, defenderSectId, currentYear)
			|| !TryBuildCandidate(attackerSectId, out Candidate attacker)
			|| !TryBuildCandidate(defenderSectId, out Candidate defender)
			|| !CanInitiateWar(attacker, defender))
		{
			return false;
		}

		bool created = CreateWar(attacker, defender, currentYear, reason);
		if (created)
		{
			PruneRecords();
			MarkChanged();
		}
		return created;
	}

	internal static IReadOnlyList<XjSectWarArchiveRecord> ReadActiveWars()
	{
		if (RecordsByWarId.Count == 0)
		{
			return Array.Empty<XjSectWarArchiveRecord>();
		}

		List<XjSectWarArchiveRecord> result = new List<XjSectWarArchiveRecord>();
		foreach (XjSectWarArchiveRecord record in RecordsByWarId.Values)
		{
			if (record != null && string.Equals(record.Status, XjSectWarStatus.Active, StringComparison.Ordinal))
			{
				result.Add(Clone(record));
			}
		}
		result.Sort((left, right) => left.StartYear != right.StartYear
			? left.StartYear.CompareTo(right.StartYear)
			: left.WarId.CompareTo(right.WarId));
		return result;
	}

	internal static void ExportArchiveRecords(List<XjSectWarArchiveRecord> target)
	{
		if (target == null)
		{
			return;
		}

		target.Clear();
		WarBuffer.Clear();
		foreach (XjSectWarArchiveRecord record in RecordsByWarId.Values)
		{
			if (record != null)
			{
				WarBuffer.Add(Clone(record));
			}
		}
		WarBuffer.Sort((left, right) => left.StartYear != right.StartYear
			? left.StartYear.CompareTo(right.StartYear)
			: left.WarId.CompareTo(right.WarId));
		for (int i = 0; i < WarBuffer.Count; i++)
		{
			target.Add(WarBuffer[i]);
		}
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjSectWarArchiveRecord> source)
	{
		Clear();
		if (source == null)
		{
			return;
		}

		for (int i = 0; i < source.Count; i++)
		{
			XjSectWarArchiveRecord record = Normalize(Clone(source[i]));
			if (record == null
				|| record.WarId <= 0L
				|| record.AttackerSectId <= 0L
				|| record.DefenderSectId <= 0L
				|| record.AttackerSectId == record.DefenderSectId)
			{
				continue;
			}

			RecordsByWarId[record.WarId] = record;
			int pairYear = string.Equals(record.Status, XjSectWarStatus.Active, StringComparison.Ordinal)
				? Math.Max(record.StartYear, record.LastProcessedYear)
				: Math.Max(record.StartYear, record.EndYear);
			LastPairYear[ResolvePair(record.AttackerSectId, record.DefenderSectId)] = pairYear;
		}
		PruneRecords();
	}

	internal static void Clear()
	{
		ClearPendingAnnualWork();
		RecordsByWarId.Clear();
		LastPairYear.Clear();
		WarBuffer.Clear();
		ClearHostilityRuntime();
	}

	private static bool ProcessActiveWars(int currentYear)
	{
		if (RecordsByWarId.Count == 0)
		{
			return false;
		}

		WarBuffer.Clear();
		foreach (XjSectWarArchiveRecord record in RecordsByWarId.Values)
		{
			if (record != null && string.Equals(record.Status, XjSectWarStatus.Active, StringComparison.Ordinal))
			{
				WarBuffer.Add(record);
			}
		}
		WarBuffer.Sort((left, right) => left.LastProcessedYear != right.LastProcessedYear
			? left.LastProcessedYear.CompareTo(right.LastProcessedYear)
			: left.WarId.CompareTo(right.WarId));

		bool changed = false;
		int processed = 0;
		for (int i = 0; i < WarBuffer.Count && processed < MaxActiveWarsProcessedPerYear; i++)
		{
			if (ProcessWar(WarBuffer[i], currentYear))
			{
				changed = true;
				processed++;
			}
		}
		return changed;
	}

	private static bool ProcessWar(XjSectWarArchiveRecord war, int currentYear)
	{
		if (war == null || war.LastProcessedYear >= currentYear)
		{
			return false;
		}

		if (!TryResolveLiveSect(war.AttackerSectId, out XjSectArchiveRecord attacker)
			|| !TryResolveLiveSect(war.DefenderSectId, out XjSectArchiveRecord defender))
		{
			CancelWar(war, currentYear, "参战宗门传承已断，战事自行止息。");
			PublishWarHistory(war, currentYear, war.ResultSummary, 3, "SectWarCancelled");
			return true;
		}

		// 战争每年结算前重新执行一次稀疏高境动员。新晋金丹若在战争中途出现，
		// 同样不能继续普通闭关；死亡/转宗者则由当前宗门索引自然剔除。
		MobilizeSectJinDan(attacker.SectId, currentYear);
		MobilizeSectJinDan(defender.SectId, currentYear);

		float attackerScore = ComputeHighRealmScore(attacker.SectId, war.AttackerParticipantIds);
		float defenderScore = ComputeHighRealmScore(defender.SectId, war.DefenderParticipantIds);
		war.AttackerScore = attackerScore;
		war.DefenderScore = defenderScore;
		war.LastProcessedYear = currentYear;
		war.DurationYears = Math.Max(0, currentYear - war.StartYear + 1);

		if (attackerScore < MinimumStartingScore || defenderScore < MinimumStartingScore)
		{
			ResolveWar(war, currentYear, "一方已无足够高境战力维持宗门战，双方收兵。", XjSectWarStatus.Resolved);
			PublishWarHistory(war, currentYear, war.ResultSummary, 3, "SectWarEnded");
			return true;
		}

		bool attackerOperational = XjSectFormationRegistry.TryGetOperational(attacker.SectId, out _);
		bool defenderOperational = XjSectFormationRegistry.TryGetOperational(defender.SectId, out _);
		if (!attackerOperational || !defenderOperational)
		{
			ResolveFormationOutcome(war, currentYear, attackerOperational, defenderOperational);
			return true;
		}

		int attackerDamage = ResolveDamage(attackerScore, defenderScore);
		int defenderDamage = ResolveDamage(defenderScore, attackerScore);
		int appliedToDefender = XjSectFormationRegistry.ApplyOccupationDamage(defender.SectId, attackerDamage, currentYear);
		int appliedToAttacker = XjSectFormationRegistry.ApplyOccupationDamage(attacker.SectId, defenderDamage, currentYear);
		war.AttackerDamageDealt += appliedToDefender;
		war.DefenderDamageDealt += appliedToAttacker;
		AnnounceFormationThresholds(war, currentYear);

		attackerOperational = XjSectFormationRegistry.TryGetOperational(attacker.SectId, out _);
		defenderOperational = XjSectFormationRegistry.TryGetOperational(defender.SectId, out _);
		if (!attackerOperational || !defenderOperational)
		{
			ResolveFormationOutcome(war, currentYear, attackerOperational, defenderOperational);
			return true;
		}

		if (war.DurationYears >= MaxWarDurationYears)
		{
			ResolveWar(war, currentYear, "宗门战争持续十年仍未破阵，双方久攻不下，各自收束战力。", XjSectWarStatus.Resolved);
			PublishWarHistory(war, currentYear, war.ResultSummary, 3, "SectWarEnded");
			return true;
		}

		if ((currentYear - war.StartYear) > 0 && (currentYear - war.StartYear) % 5 == 0)
		{
			PublishWarHistory(war, currentYear, BuildProgressSummary(war), 3, "SectWarProgress");
		}
		return true;
	}

	private static void ResolveFormationOutcome(
		XjSectWarArchiveRecord war,
		int currentYear,
		bool attackerOperational,
		bool defenderOperational)
	{
		string summary;
		if (!attackerOperational && !defenderOperational)
		{
			summary = war.AttackerSectName + "与" + war.DefenderSectName + "护宗大阵在同年交锋中全部失效，两宗俱存，战事就此止息。";
		}
		else if (!defenderOperational)
		{
			int cityCount = XjSectRepository.CountValidSectCities(war.DefenderSectId);
			bool defeated = XjSectRepository.TryDefeatSectByFormationBreak(
				war.DefenderSectId,
				war.AttackerSectId,
				currentYear,
				"护宗大阵被" + war.AttackerSectName + "攻破");
			if (!defeated)
			{
				KeepWarPendingForDefeatRetry(war, currentYear, war.AttackerSectName + "已攻破" + war.DefenderSectName + "护宗大阵，但山门归属尚未完全落定，下一年度将继续收束战局。" );
				return;
			}
			summary = war.AttackerSectName + "攻破" + war.DefenderSectName + "护宗大阵，" + war.DefenderSectName + "覆灭；弟子、物资及" + cityCount.ToString(CultureInfo.InvariantCulture) + "座宗门城镇尽归胜宗。";
		}
		else
		{
			int cityCount = XjSectRepository.CountValidSectCities(war.AttackerSectId);
			bool defeated = XjSectRepository.TryDefeatSectByFormationBreak(
				war.AttackerSectId,
				war.DefenderSectId,
				currentYear,
				"护宗大阵被" + war.DefenderSectName + "反破");
			if (!defeated)
			{
				KeepWarPendingForDefeatRetry(war, currentYear, war.DefenderSectName + "已反破" + war.AttackerSectName + "护宗大阵，但山门归属尚未完全落定，下一年度将继续收束战局。" );
				return;
			}
			summary = war.DefenderSectName + "反破" + war.AttackerSectName + "护宗大阵，" + war.AttackerSectName + "覆灭；弟子、物资及" + cityCount.ToString(CultureInfo.InvariantCulture) + "座宗门城镇尽归胜宗。";
		}

		ResolveWar(war, currentYear, summary, XjSectWarStatus.Resolved);
		RegisterWarResolved(war, currentYear, !attackerOperational && !defenderOperational);
		if (!attackerOperational && !defenderOperational)
		{
			XjThreeBookWriter.RecordSectWarResult(war.AttackerSectId, war.AttackerSectName, war.DefenderSectId, war.DefenderSectName, currentYear, false, summary, mutualDestruction: true);
			XjThreeBookWriter.RecordSectWarResult(war.DefenderSectId, war.DefenderSectName, war.AttackerSectId, war.AttackerSectName, currentYear, false, summary, mutualDestruction: true);
		}
		else if (!defenderOperational)
		{
			XjThreeBookWriter.RecordSectWarResult(war.AttackerSectId, war.AttackerSectName, war.DefenderSectId, war.DefenderSectName, currentYear, true, summary);
			XjThreeBookWriter.RecordSectWarResult(war.DefenderSectId, war.DefenderSectName, war.AttackerSectId, war.AttackerSectName, currentYear, false, summary);
		}
		else
		{
			XjThreeBookWriter.RecordSectWarResult(war.DefenderSectId, war.DefenderSectName, war.AttackerSectId, war.AttackerSectName, currentYear, true, summary);
			XjThreeBookWriter.RecordSectWarResult(war.AttackerSectId, war.AttackerSectName, war.DefenderSectId, war.DefenderSectName, currentYear, false, summary);
		}
		PublishWarHistory(war, currentYear, summary, 5, !attackerOperational && !defenderOperational ? "SectWarMutualDestruction" : "SectWarVictory");
	}

	private static void KeepWarPendingForDefeatRetry(XjSectWarArchiveRecord war, int currentYear, string summary)
	{
		if (war == null || string.IsNullOrWhiteSpace(summary))
		{
			return;
		}
		bool firstFailure = !string.Equals(war.ResultSummary, summary, StringComparison.Ordinal);
		war.ResultSummary = summary;
		if (firstFailure)
		{
			PublishWarHistory(war, currentYear, summary, 4, "SectWarDefeatTransferPending");
		}
	}

	private static bool TryBuildCandidate(long sectId, out Candidate candidate)
	{
		candidate = null;
		if (!TryResolveLiveSect(sectId, out XjSectArchiveRecord sect)
			|| !XjSectRepository.HasValidSectCity(sectId)
			|| !XjSectFormationRegistry.TryGetOperational(sectId, out _))
		{
			return false;
		}

		List<long> participants = new List<long>(MaxParticipantsPerSide);
		float score = ComputeHighRealmScore(sectId, participants);
		if (score < MinimumStartingScore)
		{
			return false;
		}

		candidate = new Candidate
		{
			Sect = sect,
			Score = score,
			HighestTier = ResolveHighestTier(participants),
			Participants = participants
		};
		return true;
	}

	private static bool CanInitiateWar(Candidate attacker, Candidate defender)
	{
		if (attacker?.Sect == null || defender?.Sect == null
			|| attacker.Score < MinimumStartingScore
			|| defender.Score < MinimumStartingScore
			|| attacker.Score < defender.Score * MinimumAggressorScoreRatio)
		{
			return false;
		}

		// 最高境界低一层时，仅在人数与总战力形成绝对优势后才允许主动开战。
		// 因此“仅有紫府的一宗主动报复有金丹坐镇的宗门”会被直接阻止。
		return attacker.HighestTier >= defender.HighestTier
			|| attacker.Score >= defender.Score * LowerRealmCompensationRatio;
	}

	private static int ResolveHighestTier(IReadOnlyList<long> participantIds)
	{
		int highest = XjRealmSuppression.TierNone;
		if (participantIds == null) return highest;
		for (int i = 0; i < participantIds.Count; i++)
		{
			if (XjScheduler.ResolveActor(participantIds[i], out Actor actor)
				&& actor?.data != null
				&& actor.isAlive())
			{
				highest = Math.Max(highest, XjRealmSuppression.GetRealmTier(actor));
			}
		}
		return highest;
	}

	private static bool CanSectInitiateWar(long attackerSectId, long defenderSectId)
	{
		return TryBuildCandidate(attackerSectId, out Candidate attacker)
			&& TryBuildCandidate(defenderSectId, out Candidate defender)
			&& CanInitiateWar(attacker, defender);
	}

	private static bool CreateWar(Candidate attacker, Candidate defender, int currentYear, string reason)
	{
		if (attacker?.Sect == null || defender?.Sect == null)
		{
			return false;
		}

		long warId = XjDeterministicHash.PositiveHash(
			attacker.Sect.SectId,
			"xuanjian.sect_war.stable.v1|"
			+ defender.Sect.SectId.ToString(CultureInfo.InvariantCulture)
			+ "|"
			+ currentYear.ToString(CultureInfo.InvariantCulture));
		if (warId <= 0L || RecordsByWarId.ContainsKey(warId))
		{
			return false;
		}

		XjSectWarArchiveRecord record = new XjSectWarArchiveRecord
		{
			WarId = warId,
			AttackerSectId = attacker.Sect.SectId,
			AttackerSectName = attacker.Sect.Name ?? string.Empty,
			DefenderSectId = defender.Sect.SectId,
			DefenderSectName = defender.Sect.Name ?? string.Empty,
			Status = XjSectWarStatus.Active,
			WarGoal = "破阵夺势",
			Reason = NormalizeReasonForDisplay(reason, "宗门高境冲突"),
			StartYear = currentYear,
			LastProcessedYear = currentYear - 1,
			AttackerScore = attacker.Score,
			DefenderScore = defender.Score,
			AttackerParticipantIds = new List<long>(attacker.Participants),
			DefenderParticipantIds = new List<long>(defender.Participants)
		};
		RecordsByWarId[record.WarId] = record;
		LastPairYear[ResolvePair(record.AttackerSectId, record.DefenderSectId)] = currentYear;
		RegisterWarStarted(record.AttackerSectId, record.DefenderSectId, currentYear, record.Reason);
		MobilizeSectJinDan(record.AttackerSectId, currentYear);
		MobilizeSectJinDan(record.DefenderSectId, currentYear);
		PublishWarHistory(
			record,
			currentYear,
			record.AttackerSectName + "与" + record.DefenderSectName + "敌意积至临界，宗门大战爆发；双方高境直指护宗大阵。",
			4,
			"SectWarStarted");
		return true;
	}

	private static void MobilizeSectJinDan(long sectId, int currentYear)
	{
		if (sectId <= 0L) return;
		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sectId);
		if (actorIds == null || actorIds.Count == 0) return;

		// 宗门战争一年最多只结算两场，这里扫描的是已经维护好的“本宗成员索引”，
		// 不是全世界单位表。不能再截前128人，否则大宗门里排在索引后段的金丹
		// 仍会留在洞天闭关，既不动员也不计入战力。
		for (int i = 0; i < actorIds.Count; i++)
		{
			long actorId = actorIds[i];
			if (actorId <= 0L) continue;
			if (!XjScheduler.ResolveActor(actorId, out Actor actor)
				|| actor?.data == null
				|| !actor.isAlive()
				|| XjSectRepository.ResolveActorSectId(actor) != sectId
				|| XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierJinDan)
			{
				continue;
			}

			XjSectDongTianLifecycle.InterruptForSectWar(actor, currentYear);
		}
	}

	private static float ComputeHighRealmScore(long sectId, List<long> participantIds)
	{
		participantIds?.Clear();
		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sectId);
		if (actorIds == null || actorIds.Count == 0)
		{
			return 0f;
		}

		float score = 0f;
		// 与动员使用同一权威宗门索引全量核算；高境战争本身是年度低频事务，
		// 正确计入一个大宗门真正拥有的紫府/金丹比截断128人更重要。
		for (int i = 0; i < actorIds.Count; i++)
		{
			long actorId = actorIds[i];
			if (actorId <= 0L)
			{
				continue;
			}
			if (!XjScheduler.ResolveActor(actorId, out Actor actor)
				|| actor?.data == null
				|| !actor.isAlive()
				|| XjSectRepository.ResolveActorSectId(actor) != sectId)
			{
				continue;
			}

			int tier = XjRealmSuppression.GetRealmTier(actor);
			if (tier >= XjRealmSuppression.TierJinDan)
			{
				score += 24f;
				AddParticipant(participantIds, actorId);
			}
			else if (tier >= XjRealmSuppression.TierZiFu)
			{
				score += 9f;
				AddParticipant(participantIds, actorId);
			}
		}
		return score;
	}

	private static void AddParticipant(List<long> participantIds, long actorId)
	{
		if (participantIds == null || actorId <= 0L || participantIds.Count >= MaxParticipantsPerSide)
		{
			return;
		}
		participantIds.Add(actorId);
	}

	private static int ResolveDamage(float ownScore, float enemyScore)
	{
		float ratio = enemyScore <= 0f ? 2f : ownScore / enemyScore;
		float multiplier = ratio >= 2f ? 1.75f : ratio >= 1.25f ? 1.25f : 1f;
		return Math.Max(1, Math.Min(1200, (int)Math.Ceiling(ownScore * 25f * multiplier)));
	}

	private static void AnnounceFormationThresholds(XjSectWarArchiveRecord war, int currentYear)
	{
		if (war == null)
		{
			return;
		}

		int attackerWarningMask = war.AttackerFormationWarningMask;
		AnnounceFormationThreshold(
			war,
			currentYear,
			war.AttackerSectId,
			war.AttackerSectName,
			ref attackerWarningMask);
		war.AttackerFormationWarningMask = attackerWarningMask;

		int defenderWarningMask = war.DefenderFormationWarningMask;
		AnnounceFormationThreshold(
			war,
			currentYear,
			war.DefenderSectId,
			war.DefenderSectName,
			ref defenderWarningMask);
		war.DefenderFormationWarningMask = defenderWarningMask;
	}

	private static void AnnounceFormationThreshold(
		XjSectWarArchiveRecord war,
		int currentYear,
		long sectId,
		string sectName,
		ref int warningMask)
	{
		if (!XjSectFormationRegistry.TryGet(sectId, out var formation)
			|| formation == null
			|| formation.MaxDurability <= 0
			|| formation.CurrentDurability <= 0)
		{
			return;
		}

		float ratio = formation.CurrentDurability / (float)formation.MaxDurability;
		int bit = ratio <= 0.25f ? 2 : ratio <= 0.50f ? 1 : 0;
		if (bit == 0 || (warningMask & bit) != 0)
		{
			return;
		}

		warningMask |= bit;
		string summary = (string.IsNullOrWhiteSpace(sectName) ? "某宗" : sectName)
			+ (bit == 2 ? "护宗大阵耐久跌破四分之一，阵基已近崩解。" : "护宗大阵耐久跌破半数，宗门战势转危。" );
		PublishWarHistory(war, currentYear, summary, 4, bit == 2 ? "SectWarFormationQuarter" : "SectWarFormationHalf");
	}

	private static int CountActiveWars()
	{
		int count = 0;
		foreach (XjSectWarArchiveRecord record in RecordsByWarId.Values)
		{
			if (record != null && string.Equals(record.Status, XjSectWarStatus.Active, StringComparison.Ordinal))
			{
				count++;
			}
		}
		return count;
	}

	private static bool HasActiveSect(long sectId)
	{
		if (sectId <= 0L)
		{
			return false;
		}

		foreach (XjSectWarArchiveRecord record in RecordsByWarId.Values)
		{
			if (record != null
				&& string.Equals(record.Status, XjSectWarStatus.Active, StringComparison.Ordinal)
				&& (record.AttackerSectId == sectId || record.DefenderSectId == sectId))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasActivePair(long left, long right)
	{
		(long Left, long Right) pair = ResolvePair(left, right);
		foreach (XjSectWarArchiveRecord record in RecordsByWarId.Values)
		{
			if (record != null
				&& string.Equals(record.Status, XjSectWarStatus.Active, StringComparison.Ordinal)
				&& ResolvePair(record.AttackerSectId, record.DefenderSectId).Equals(pair))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsPairOnCooldown(long left, long right, int currentYear)
	{
		return LastPairYear.TryGetValue(ResolvePair(left, right), out int lastYear)
			&& currentYear - lastYear < SamePairCooldownYears;
	}

	private static (long Left, long Right) ResolvePair(long left, long right)
	{
		return left <= right ? (left, right) : (right, left);
	}

	private static bool TryResolveLiveSect(long sectId, out XjSectArchiveRecord record)
	{
		record = null;
		return sectId > 0L
			&& XjSectRepository.TryGetBySectId(sectId, out record)
			&& record != null
			&& !string.Equals(record.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
			&& XjSectRepository.HasValidSectCity(sectId);
	}

	private static void CancelWar(XjSectWarArchiveRecord war, int currentYear, string summary)
	{
		ResolveWar(war, currentYear, summary, XjSectWarStatus.Cancelled);
	}

	private static void ResolveWar(XjSectWarArchiveRecord war, int currentYear, string summary, string status)
	{
		war.Status = status;
		war.EndYear = currentYear;
		war.ResultSummary = summary ?? string.Empty;
		war.DurationYears = Math.Max(0, currentYear - war.StartYear + 1);
		LastPairYear[ResolvePair(war.AttackerSectId, war.DefenderSectId)] = currentYear;
		RegisterWarEnded(war.AttackerSectId, war.DefenderSectId, currentYear);
	}

	private static string BuildProgressSummary(XjSectWarArchiveRecord war)
	{
		return war.AttackerSectName + "与" + war.DefenderSectName + "仍在高境交锋，双方护宗大阵持续承压。";
	}

	private static void PublishWarHistory(
		XjSectWarArchiveRecord war,
		int currentYear,
		string summary,
		int importance,
		string eventType)
	{
		if (war == null || string.IsNullOrWhiteSpace(summary))
		{
			return;
		}

		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Sect,
			importance >= 5 ? "宗门战局分晓" : "宗门战事",
			summary,
			importance,
			importance >= 4,
			sectId: war.AttackerSectId,
			year: currentYear);
		if (importance >= 5)
		{
			XjBroadcastSystem.ShowRecordedWorldTipCritical(summary, color: "#D94C4C", duration: 9f);
		}
		else if (importance >= 4)
		{
			XjBroadcastSystem.ShowRecordedWorldTipCritical(summary, color: "#F3961F", duration: 8f, delayFrames: 1);
		}

		XjCenturyAnnalsStore.ObserveSectEvent(
			eventType,
			currentYear,
			war.AttackerSectId,
			war.AttackerSectName,
			importance,
			summary);
	}

	private static void PruneRecords()
	{
		if (RecordsByWarId.Count <= MaxRetainedRecords)
		{
			return;
		}

		WarBuffer.Clear();
		foreach (XjSectWarArchiveRecord record in RecordsByWarId.Values)
		{
			if (record != null && !string.Equals(record.Status, XjSectWarStatus.Active, StringComparison.Ordinal))
			{
				WarBuffer.Add(record);
			}
		}
		WarBuffer.Sort((left, right) =>
		{
			int leftYear = left.EndYear > 0 ? left.EndYear : left.LastProcessedYear;
			int rightYear = right.EndYear > 0 ? right.EndYear : right.LastProcessedYear;
			return leftYear != rightYear ? leftYear.CompareTo(rightYear) : left.WarId.CompareTo(right.WarId);
		});
		for (int i = 0; RecordsByWarId.Count > MaxRetainedRecords && i < WarBuffer.Count; i++)
		{
			RecordsByWarId.Remove(WarBuffer[i].WarId);
		}
	}

	private static void MarkChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Sect
			| XjCodexDirtyFlags.Formation
			| XjCodexDirtyFlags.Conflict
			| XjCodexDirtyFlags.History);
	}

	private static XjSectWarArchiveRecord Normalize(XjSectWarArchiveRecord source)
	{
		if (source == null)
		{
			return null;
		}

		source.SchemaVersion = XjSectDomainSchema.CurrentVersion;
		source.AttackerSectName = (source.AttackerSectName ?? string.Empty).Trim();
		source.DefenderSectName = (source.DefenderSectName ?? string.Empty).Trim();
		source.Status = string.IsNullOrWhiteSpace(source.Status) ? XjSectWarStatus.Active : source.Status.Trim();
		source.WarGoal = source.WarGoal ?? string.Empty;
		source.Reason = NormalizeReasonForDisplay(source.Reason, string.Empty);
		source.ResultSummary = source.ResultSummary ?? string.Empty;
		source.AttackerParticipantIds ??= new List<long>();
		source.DefenderParticipantIds ??= new List<long>();
		if (source.LastProcessedYear <= 0)
		{
			source.LastProcessedYear = Math.Max(0, source.StartYear - 1);
		}
		if (!string.Equals(source.Status, XjSectWarStatus.Active, StringComparison.Ordinal) && source.EndYear <= 0)
		{
			source.EndYear = source.LastProcessedYear;
		}
		source.DurationYears = Math.Max(
			source.DurationYears,
			Math.Max(0, (source.EndYear > 0 ? source.EndYear : source.LastProcessedYear) - source.StartYear + 1));
		return source;
	}

	private static XjSectWarArchiveRecord Clone(XjSectWarArchiveRecord source)
	{
		if (source == null)
		{
			return null;
		}

		return new XjSectWarArchiveRecord
		{
			SchemaVersion = source.SchemaVersion,
			WarId = source.WarId,
			AttackerSectId = source.AttackerSectId,
			AttackerSectName = source.AttackerSectName ?? string.Empty,
			DefenderSectId = source.DefenderSectId,
			DefenderSectName = source.DefenderSectName ?? string.Empty,
			Status = source.Status ?? XjSectWarStatus.Active,
			WarGoal = source.WarGoal ?? string.Empty,
			Reason = NormalizeReasonForDisplay(source.Reason, string.Empty),
			StartYear = source.StartYear,
			LastProcessedYear = source.LastProcessedYear,
			EndYear = source.EndYear,
			DurationYears = source.DurationYears,
			AttackerDamageDealt = source.AttackerDamageDealt,
			DefenderDamageDealt = source.DefenderDamageDealt,
			AttackerScore = source.AttackerScore,
			DefenderScore = source.DefenderScore,
			ResultSummary = source.ResultSummary ?? string.Empty,
			AttackerFormationWarningMask = source.AttackerFormationWarningMask,
			DefenderFormationWarningMask = source.DefenderFormationWarningMask,
			AttackerParticipantIds = source.AttackerParticipantIds == null
				? new List<long>()
				: new List<long>(source.AttackerParticipantIds),
			DefenderParticipantIds = source.DefenderParticipantIds == null
				? new List<long>()
				: new List<long>(source.DefenderParticipantIds)
		};
	}
}
