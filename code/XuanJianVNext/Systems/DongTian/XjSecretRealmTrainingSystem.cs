using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Sect;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.DongTian;

internal static class XjSecretRealmTrainingSystem
{
	private const int TrainingIntervalYears = 3;
	private const int MaxCandidates = 10;
	// 三年一届后按长期期望缩放：福地≈0.10道慧/年，洞天≈0.30道慧/年。
	private const float FudiHuiGuangChance = 0.15f;
	private const float DongtianHuiGuangChance = 0.30f;
	private const float FudiHuiGuangGain = 2f;
	private const float DongtianHuiGuangGain = 3f;
	private static readonly List<TrainingCandidate> CandidateBuffer = new List<TrainingCandidate>(32);
	private static readonly List<long> ParticipantBuffer = new List<long>(MaxCandidates);
	private static readonly List<string> NameBuffer = new List<string>(MaxCandidates);
	private static IReadOnlyList<XjSectArchiveRecord> PendingAnnualSects = Array.Empty<XjSectArchiveRecord>();
	private static int _pendingAnnualYear;
	private static int _pendingAnnualIndex;

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
		ClearPendingAnnualWork();
		PendingAnnualSects = XjSectRepository.ReadAllSects();
		_pendingAnnualYear = currentYear;
		_pendingAnnualIndex = 0;
		if (PendingAnnualSects.Count > 0) return true;
		ClearPendingAnnualWork();
		return false;
	}

	internal static bool TickPending(int itemBudget, double timeBudgetMs)
	{
		if (_pendingAnnualYear <= 0 || _pendingAnnualIndex >= PendingAnnualSects.Count)
		{
			ClearPendingAnnualWork();
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
		ClearPendingAnnualWork();
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
		ClearPendingAnnualWork();
	}

	private static void ProcessAnnualSect(XjSectArchiveRecord sect, int currentYear)
	{
		if (sect == null || sect.SectId <= 0L
			|| string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
			|| currentYear - Math.Max(0, sect.LastSecretRealmTrainingYear) < TrainingIntervalYears)
		{
			return;
		}
		if (!XjSecretRealmRegistry.TryGetBySectId(sect.SectId, out XjSecretRealmArchiveRecord realm)
			|| !IsOpenTrainingRealm(realm))
		{
			return;
		}
		// 世界门禁已经把本系统降到三年一次，不再为全部宗门逐年做哈希相位判断。
		TryGrantTraining(sect, realm, currentYear);
	}

	internal static void ClearPendingAnnualWork()
	{
		PendingAnnualSects = Array.Empty<XjSectArchiveRecord>();
		_pendingAnnualYear = 0;
		_pendingAnnualIndex = 0;
	}

	private static bool TryGrantTraining(XjSectArchiveRecord sect, XjSecretRealmArchiveRecord realm, int currentYear)
	{
		CandidateBuffer.Clear();
		ParticipantBuffer.Clear();
		NameBuffer.Clear();
		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sect.SectId);
		// 一次遍历同时确认宗门最高层次与候选人，避免先判定金丹、再重复解析全部角色。
		int candidateCeiling = CollectCandidates(actorIds, sect.SectId);
		// 无合法弟子时不开放洞天/福地试炼：不写历史、不记三年冷却。
		if (CandidateBuffer.Count == 0) return false;

		CandidateBuffer.Sort(CompareCandidates);
		int limit = Math.Min(MaxCandidates, Math.Max(1, realm.Capacity));
		int granted = 0;
		bool isDongtian = string.Equals(realm.Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal);
		float huiGuangChance = isDongtian ? DongtianHuiGuangChance : FudiHuiGuangChance;
		float huiGuangGain = isDongtian ? DongtianHuiGuangGain : FudiHuiGuangGain;
		for (int i = 0; i < CandidateBuffer.Count && granted < limit; i++)
		{
			TrainingCandidate candidate = CandidateBuffer[i];
			if (!XjScheduler.ResolveActor(candidate.ActorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive()
				|| XjLongShuSystem.IsLongShu(actor)
				|| XjYinSiTraitLifecycle.IsYinSi(actor)
				|| XjSectRepository.ResolveActorSectId(actor) != sect.SectId
				|| !HasValidCultivationSnapshot(actor))
			{
				continue;
			}

			// 先只锁定本轮真实参与者，不在宗门冷却成功提交前修改角色。
			// 这样同一年入口被重复调用或档案拒绝写入时，不会先发收益后漏记冷却。
			ParticipantBuffer.Add(candidate.ActorId);
			granted++;
			if (NameBuffer.Count < MaxCandidates) NameBuffer.Add(XjStringHelper.ActorName(actor, "无名弟子"));
		}
		if (granted <= 0) return false;

		string realmLabel = ResolveRealmLabel(realm);
		string realmName = string.IsNullOrWhiteSpace(realm.DisplayName) ? realmLabel : realm.DisplayName.Trim();
		string sectName = string.IsNullOrWhiteSpace(sect.Name) ? "某宗" : sect.Name.Trim();
		int shownWinnerCount = Math.Min(3, NameBuffer.Count);
		string winnerNames = shownWinnerCount <= 0
			? "门中弟子"
			: string.Join("、", NameBuffer.GetRange(0, shownWinnerCount));
		bool ziFuMayEnter = candidateCeiling > XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
		string qualificationText = ziFuMayEnter
			? "宗内已有真君坐镇，诸位金丹不入场，本届由紫府真人及以下门人参加"
			: "宗内尚无真君，便由紫府真人主持考校而不参与争席，本届只取筑基及以下门人";
		string winnerSummary = granted == 1
			? winnerNames + "入选，得入【" + realmName + "】修持"
			: winnerNames + (granted > shownWinnerCount ? "等" : "共")
				+ granted.ToString(CultureInfo.InvariantCulture) + "人入选，得入【" + realmName + "】修持";
		string summary = sectName + "举行三年大比。" + qualificationText
			+ "。诸峰门人先论道、后试法，数轮考校之后，" + winnerSummary + "。";
		if (!XjSectRepository.TryRecordSecretRealmTraining(sect.SectId, currentYear, summary)) return false;
		XjThreeBookWriter.RecordSectTournament(sect.SectId, sectName, currentYear, CandidateBuffer.Count, granted);
		RecordTournamentProgression(sect.SectId, currentYear, granted);

		// 冷却提交后才正式确认名次、发放收益并写入三类史册。
		// 这不是把原事件换一层措辞，而是一次真实的抽象宗门大比：
		// 候选按境界、真元和稳定次序排定，前席取得有限洞天名额。
		for (int i = 0; i < ParticipantBuffer.Count; i++)
		{
			if (!XjScheduler.ResolveActor(ParticipantBuffer[i], out Actor actor)
				|| actor?.data == null || !actor.isAlive()
				|| XjSectRepository.ResolveActorSectId(actor) != sect.SectId)
			{
				continue;
			}

			float actualHuiGuangGain = ApplyTraining(actor, huiGuangChance, huiGuangGain, currentYear);
			long actorId = ((BaseSystemData)actor.data).id;
			XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId);
			string actorName = XjStringHelper.ActorName(actor, "无名弟子");
			int rank = i + 1;
			string gainText = actualHuiGuangGain > 0f
				? "，闭关后道慧增长" + ((int)actualHuiGuangGain).ToString(CultureInfo.InvariantCulture)
				: string.Empty;
			string entryText = granted > 1
				? "，与其余" + (granted - 1).ToString(CultureInfo.InvariantCulture) + "名同门共同入内"
				: "，独自入内";
			XjThreeBookWriter.RecordSectSecretRealmQualification(
				sect.SectId,
				sectName,
				actorId,
				actorName,
				realmName,
				currentYear);
			string progressionText = BuildTournamentProgressText(actor);
			string participantBody = sectName + "举行三年大比，" + actorName + "列第"
				+ rank.ToString(CultureInfo.InvariantCulture) + "，取得【" + realmName + "】修炼名额"
				+ entryText + gainText + progressionText + "。";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.SecretRealm,
				realmLabel + "大比入选",
				participantBody,
				isDongtian ? 4 : 3,
				actorId: actorId,
				actorName: actorName,
				sectId: sect.SectId,
				familyId: familyId,
				cityId: realm.EntranceCityId,
				year: currentYear,
				eventType: "SecretRealmTournamentWinner:" + realmLabel + ":"
					+ rank.ToString(CultureInfo.InvariantCulture) + ":"
					+ granted.ToString(CultureInfo.InvariantCulture) + ":"
					+ ((int)actualHuiGuangGain).ToString(CultureInfo.InvariantCulture),
				visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
				mirrorToWorldLog: false);
		}

		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.SecretRealm,
			realmLabel + "宗门大比",
			summary,
			isDongtian ? 4 : 3,
			sectId: sect.SectId,
			cityId: realm.EntranceCityId,
			year: currentYear,
			eventType: "SecretRealmTournament:" + realmLabel + ":" + granted.ToString(CultureInfo.InvariantCulture),
			visibilityFlags: (int)(XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
			mirrorToWorldLog: false);
		return true;
	}

	private static void RecordTournamentProgression(long sectId, int currentYear, int granted)
	{
		int rankedCount = Math.Min(MaxCandidates, CandidateBuffer.Count);
		for (int i = 0; i < rankedCount; i++)
		{
			TrainingCandidate candidate = CandidateBuffer[i];
			if (!XjScheduler.ResolveActor(candidate.ActorId, out Actor actor) || actor?.data == null || !actor.isAlive()
				|| XjSectRepository.ResolveActorSectId(actor) != sectId) continue;
			int rank = i + 1;
			bool qualified = ParticipantBuffer.Contains(candidate.ActorId);
			int pointsGain = rank == 1 ? 9 : rank == 2 ? 7 : rank == 3 ? 5 : qualified ? 4 : 1;
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.SectTournamentPoints, out int points);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.SectTournamentAppearances, out int appearances);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.SectTournamentTopPlacements, out int topPlacements);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.SectTournamentQualificationStreak, out int streak);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.SectTournamentBestStreak, out int bestStreak);
			points = Math.Max(0, points) + pointsGain;
			appearances = Math.Max(0, appearances) + 1;
			if (rank <= 3) topPlacements = Math.Max(0, topPlacements) + 1;
			streak = qualified ? Math.Max(0, streak) + 1 : 0;
			bestStreak = Math.Max(Math.Max(0, bestStreak), streak);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.SectTournamentPoints, points);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.SectTournamentAppearances, appearances);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.SectTournamentTopPlacements, topPlacements);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.SectTournamentQualificationStreak, streak);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.SectTournamentBestStreak, bestStreak);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.SectTournamentLastYear, currentYear);
		}
	}

	private static string BuildTournamentProgressText(Actor actor)
	{
		// 积数、连入前席等仍作为稳定的内部履历数据保留，但不把计分器口吻写进叙事正文。
		return string.Empty;
	}

	private static bool HasValidCultivationSnapshot(Actor actor)
	{
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		return !string.IsNullOrWhiteSpace(snapshot.RealmId);
	}

	private static float ApplyTraining(Actor actor, float huiGuangChance, float huiGuangGain, int currentYear)
	{
		// 参与资格已在提交宗门冷却前完成一次权威快照校验；同一同步调用中
		// 不再重复构建培养快照，避免每名参与者做两次相同派生。
		// 0.7.9 没有“宗门秘境每三年直接灌入数千真元”的旁路。
		// 为恢复旧世界节奏，秘境训练只保留轻量道慧收益，不再绕过
		// 年度修炼主链直接写真元。
		bool changed = false;
		float actualGain = 0f;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		if (XjDeterministicHash.Roll01(((BaseSystemData)actor.data).id, currentYear, "secret.realm.training", "huiguang") < huiGuangChance)
		{
			float nextHuiGuang = XjDaoHuiPolicy.Add(huiGuang, huiGuangGain, XjDaoHuiPolicy.RareGrowthCeiling);
			if (nextHuiGuang > huiGuang)
			{
				XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, nextHuiGuang);
				actualGain = Math.Max(0f, nextHuiGuang - huiGuang);
				changed = true;
			}
		}
		if (changed)
		{
			actor.setStatsDirty();
			return actualGain;
		}
		return 0f;
	}

	private static int CollectCandidates(IReadOnlyList<long> actorIds, long sectId)
	{
		int taiXiOrder = XjRealmHelper.GetOrder(XjRealmIds.TaiXi);
		int ziFuOrder = XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
		int jinDanOrder = XjRealmHelper.GetOrder(XjRealmIds.JinDan);
		bool hasJinDan = false;
		if (actorIds != null)
		{
			for (int i = 0; i < actorIds.Count; i++)
			{
				long actorId = actorIds[i];
				if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor)
					|| actor?.data == null || !actor.isAlive()
					|| XjLongShuSystem.IsLongShu(actor)
					|| XjYinSiTraitLifecycle.IsYinSi(actor)
					|| XjSectRepository.ResolveActorSectId(actor) != sectId)
				{
					continue;
				}

				string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
				int order = XjRealmHelper.GetOrder(realmId);
				if (order >= jinDanOrder)
				{
					hasJinDan = true;
					continue;
				}
				if (order <= taiXiOrder) continue;

				XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
				XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
				CandidateBuffer.Add(new TrainingCandidate(actorId, order, NormalizeSortValue(zhenYuan), NormalizeSortValue(huiGuang)));
			}
		}

		// 无金丹坐镇时视为紫府宗门，真人退出大比；有金丹坐镇时才允许紫府参比。
		if (!hasJinDan)
		{
			for (int i = CandidateBuffer.Count - 1; i >= 0; i--)
			{
				if (CandidateBuffer[i].RealmOrder >= ziFuOrder) CandidateBuffer.RemoveAt(i);
			}
		}
		return hasJinDan ? jinDanOrder : ziFuOrder;
	}

	private static int CompareCandidates(TrainingCandidate left, TrainingCandidate right)
	{
		int cmp = right.RealmOrder.CompareTo(left.RealmOrder);
		if (cmp != 0) return cmp;
		cmp = right.DaoXing.CompareTo(left.DaoXing);
		if (cmp != 0) return cmp;
		cmp = right.ZhenYuan.CompareTo(left.ZhenYuan);
		return cmp != 0 ? cmp : left.ActorId.CompareTo(right.ActorId);
	}

	private static float NormalizeSortValue(float value)
	{
		return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
	}

	private static bool IsOpenTrainingRealm(XjSecretRealmArchiveRecord realm)
	{
		return realm != null && realm.EntranceOpen && realm.Capacity > 0
			&& (string.Equals(realm.Stage, XjSecretRealmStage.Fudi, StringComparison.Ordinal)
				|| string.Equals(realm.Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal));
	}

	private static string ResolveRealmLabel(XjSecretRealmArchiveRecord realm)
	{
		return realm != null && string.Equals(realm.Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal) ? "洞天" : "福地";
	}

	private readonly struct TrainingCandidate
	{
		internal readonly long ActorId;
		internal readonly int RealmOrder;
		internal readonly float ZhenYuan;
		internal readonly float DaoXing;

		internal TrainingCandidate(long actorId, int realmOrder, float zhenYuan, float daoXing)
		{
			ActorId = actorId;
			RealmOrder = realmOrder;
			ZhenYuan = zhenYuan;
			DaoXing = daoXing;
		}
	}
}
