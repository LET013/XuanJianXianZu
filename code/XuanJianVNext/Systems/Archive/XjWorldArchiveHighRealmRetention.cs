using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.HighRealm;

namespace XuanJianVNext.Systems.Archive;

/// <summary>
/// 金丹历史、转世来源、权柄持有与永久流失均属于不可逆世界史。
/// 保存前把上一次成功读入的高境档案与当前快照合并，防止活体扫描、死亡释放、
/// 读档重建顺序或空缓存把已落盘历史覆盖为空。
/// </summary>
internal static class XjWorldArchiveHighRealmRetention
{
	internal static void CaptureProtectedBaseline(
		XjWorldArchiveData source,
		XjWorldArchiveData target)
	{
		if (source == null || target == null) return;
		// These DTOs are detached from live runtime registries. Snapshot section export
		// replaces the corresponding list/state object before mutation is materialized,
		// so reference capture preserves the last successful save without rebuilding a
		// second indexed archive at the end of every commit.
		target.GuoWeiRegistry = source.GuoWeiRegistry;
		target.JinDanImmortalityRecords = source.JinDanImmortalityRecords;
		target.ReincarnationRecords = source.ReincarnationRecords;
		target.QuanBingRecords = source.QuanBingRecords;
		target.QuanBingLostAuthorities = source.QuanBingLostAuthorities;
		target.QuanBingStruggle = source.QuanBingStruggle;
		target.GuZunRecords = source.GuZunRecords;
		target.DaoTuHighRealmContinuityRecords = source.DaoTuHighRealmContinuityRecords;
		target.DaoTaiMerit = source.DaoTaiMerit;
		target.GuZunLastGlobalReappearanceYear = source.GuZunLastGlobalReappearanceYear;
		target.FuQiSwordWorld = source.FuQiSwordWorld;
		target.YuanZhaoKongZhengEvent = source.YuanZhaoKongZhengEvent;
	}

	internal static XjWorldArchiveData MergeProtectedHighRealmData(
		XjWorldArchiveData baseline,
		XjWorldArchiveData current)
	{
		XjWorldArchiveData target = current ?? new XjWorldArchiveData();
		EnsureLists(target);
		if (baseline == null)
		{
			return target;
		}

		EnsureLists(baseline);
		MergeGuoWei(baseline.GuoWeiRegistry, target.GuoWeiRegistry);
		MergeJinDanImmortality(baseline.JinDanImmortalityRecords, target.JinDanImmortalityRecords);
		MergeReincarnations(baseline.ReincarnationRecords, target.ReincarnationRecords);
		MergeQuanBing(baseline.QuanBingRecords, target.QuanBingRecords);
		MergeLostAuthorities(baseline.QuanBingLostAuthorities, target.QuanBingLostAuthorities);
		MergeQuanBingStruggle(baseline, target);
		MergeGuZunRecords(baseline.GuZunRecords, target.GuZunRecords);
		MergeDaoTuContinuity(baseline.DaoTuHighRealmContinuityRecords, target.DaoTuHighRealmContinuityRecords);
		MergeDaoTaiMerit(baseline.DaoTaiMerit, target);
		target.GuZunLastGlobalReappearanceYear = Math.Max(
			baseline.GuZunLastGlobalReappearanceYear,
			target.GuZunLastGlobalReappearanceYear);
		MergeFuQiSwordWorld(baseline.FuQiSwordWorld, target);
		MergeYuanZhaoKongZhengEvent(baseline.YuanZhaoKongZhengEvent, target);
		return target;
	}

	private static void EnsureLists(XjWorldArchiveData data)
	{
		data.GuoWeiRegistry ??= new List<XjWorldArchiveGuoWeiRecord>();
		data.JinDanImmortalityRecords ??= new List<XjJinDanImmortalityArchiveRecord>();
		data.ReincarnationRecords ??= new List<XjWorldArchiveReincarnationRecord>();
		data.QuanBingRecords ??= new List<XjGuoWeiQuanBingArchiveData>();
		data.QuanBingLostAuthorities ??= new List<XjGuoWeiQuanBingLostAuthorityArchiveData>();
		data.QuanBingStruggle ??= new XjWorldArchiveQuanBingStruggleState();
		data.GuZunRecords ??= new List<XjGuZunArchiveRecord>();
		data.DaoTuHighRealmContinuityRecords ??= new List<XjDaoTuHighRealmContinuityRecord>();
		data.DaoTaiMerit ??= new XjDaoTaiMeritWorldArchiveData();
		data.FuQiSwordWorld ??= new XjFuQiSwordWorldArchiveData();
		data.TaiYuanJinXianEvent ??= new XjTaiYuanJinXianEventArchiveData();
		data.YuanZhaoKongZhengEvent ??= new XjYuanZhaoKongZhengEventArchiveData();
	}

	private static void MergeYuanZhaoKongZhengEvent(
		XjYuanZhaoKongZhengEventArchiveData historical,
		XjWorldArchiveData target)
	{
		if (historical == null || target == null) return;
		target.YuanZhaoKongZhengEvent ??= new XjYuanZhaoKongZhengEventArchiveData();
		XjYuanZhaoKongZhengEventArchiveData current = target.YuanZhaoKongZhengEvent;
		current.Initialized |= historical.Initialized;
		if (current.BaseWorldYear <= 0 && historical.BaseWorldYear > 0) current.BaseWorldYear = historical.BaseWorldYear;
		if (current.ScheduledTriggerYear <= 0 && historical.ScheduledTriggerYear > 0) current.ScheduledTriggerYear = historical.ScheduledTriggerYear;
		// 道尊隐世事件属于同一条渊照时间轴。计数与冷却只做单调合并；待觐见邀请只有
		// 当前运行态没有有效许可时才从 protected baseline 恢复，避免保存时反复复活过期邀请。
		current.FounderEventSchemaVersion = Math.Max(current.FounderEventSchemaVersion, historical.FounderEventSchemaVersion);
		current.LastAudienceInviteYear = Math.Max(current.LastAudienceInviteYear, historical.LastAudienceInviteYear);
		current.TotalAudienceCount = Math.Max(current.TotalAudienceCount, historical.TotalAudienceCount);
		current.TotalTeachingCount = Math.Max(current.TotalTeachingCount, historical.TotalTeachingCount);
		current.LastProjectionYear = Math.Max(current.LastProjectionYear, historical.LastProjectionYear);
		current.LastAuthorityInterventionYear = Math.Max(current.LastAuthorityInterventionYear, historical.LastAuthorityInterventionYear);
		current.CredentialSchemaVersion = Math.Max(current.CredentialSchemaVersion, historical.CredentialSchemaVersion);
		current.NextCredentialYear = Math.Max(current.NextCredentialYear, historical.NextCredentialYear);
		current.LastCredentialYear = Math.Max(current.LastCredentialYear, historical.LastCredentialYear);
		current.TotalCredentialIssued = Math.Max(current.TotalCredentialIssued, historical.TotalCredentialIssued);
		current.TotalCredentialResolved = Math.Max(current.TotalCredentialResolved, historical.TotalCredentialResolved);
		if (string.IsNullOrWhiteSpace(current.LastCredentialHolderName)) current.LastCredentialHolderName = historical.LastCredentialHolderName ?? string.Empty;
		if (current.ActiveCredentialActorId <= 0L && historical.ActiveCredentialActorId > 0L && historical.ActiveCredentialUntilYear > 0)
		{
			current.ActiveCredentialActorId = historical.ActiveCredentialActorId;
			current.ActiveCredentialUntilYear = historical.ActiveCredentialUntilYear;
		}
		if (current.PendingAudienceActorId <= 0L && historical.PendingAudienceActorId > 0L && historical.PendingAudienceUntilYear > 0)
		{
			current.PendingAudienceActorId = historical.PendingAudienceActorId;
			current.PendingAudienceUntilYear = historical.PendingAudienceUntilYear;
			current.PendingAudienceReason = historical.PendingAudienceReason ?? string.Empty;
		}

		// HF4 性能收束起渊照事件使用“本局首次起录年+500”的权威时间轴。迁移完成后，
		// 当前运行态必须压过旧 protected baseline：否则 RC8 里绝对1000年已经触发的
		// historical=true 会在保存合并时把刚刚撤销的提前空证/洞天重新复活。
		bool currentOwnsDynamicTimeline = current.TimelineMigrated
			&& current.BaseWorldYear > 0
			&& current.ScheduledTriggerYear > current.BaseWorldYear;
		if (currentOwnsDynamicTimeline)
		{
			// 只保留当前动态时间轴自己的触发结果；旧绝对时间轴不可逆信息不再反灌。
			return;
		}

		current.TimelineMigrated |= historical.TimelineMigrated;
		current.LegacyBaseline |= historical.LegacyBaseline;
		current.DaoTuEstablished |= historical.DaoTuEstablished;
		current.LegacyDongTianCreated |= historical.LegacyDongTianCreated;
		if (historical.Triggered)
		{
			current.Triggered = true;
			if (current.TriggeredYear <= 0 || (historical.TriggeredYear > 0 && historical.TriggeredYear < current.TriggeredYear))
				current.TriggeredYear = historical.TriggeredYear;
		}
		if (historical.LegacyDongTianCreatedYear > 0
			&& (current.LegacyDongTianCreatedYear <= 0 || historical.LegacyDongTianCreatedYear < current.LegacyDongTianCreatedYear))
		{
			current.LegacyDongTianCreatedYear = historical.LegacyDongTianCreatedYear;
		}
	}

	private static void MergeDaoTaiMerit(
		XjDaoTaiMeritWorldArchiveData historical,
		XjWorldArchiveData target)
	{
		if (historical == null) return;
		target.DaoTaiMerit ??= new XjDaoTaiMeritWorldArchiveData();
		target.DaoTaiMerit.FirstProvenDaoTus ??= new List<string>();
		target.DaoTaiMerit.FirstZhengWeiDaoTus ??= new List<string>();
		MergeUniqueStrings(historical.FirstProvenDaoTus, target.DaoTaiMerit.FirstProvenDaoTus);
		MergeUniqueStrings(historical.FirstZhengWeiDaoTus, target.DaoTaiMerit.FirstZhengWeiDaoTus);
	}

	private static void MergeUniqueStrings(IReadOnlyList<string> source, List<string> target)
	{
		if (source == null || target == null) return;
		HashSet<string> existing = new HashSet<string>(target, StringComparer.Ordinal);
		for (int i = 0; i < source.Count; i++)
		{
			string value = (source[i] ?? string.Empty).Trim();
			if (value.Length > 0 && existing.Add(value)) target.Add(value);
		}
	}

	private static void MergeFuQiSwordWorld(
		XjFuQiSwordWorldArchiveData historical,
		XjWorldArchiveData target)
	{
		if (historical == null || !historical.Established)
		{
			return;
		}

		XjFuQiSwordWorldArchiveData current =
			target.FuQiSwordWorld ?? new XjFuQiSwordWorldArchiveData();
		bool currentStateWasBlank = !current.Established
			|| current.CurrentHolderActorId <= 0L
				&& current.VacantSinceYear <= 0
				&& (current.HolderHistory == null || current.HolderHistory.Count == 0);
		if (!current.Established)
		{
			// 长庚一经开道即为不可逆世界史。当前快照若退回“未开道”，说明
			// 保存前运行态尚未重建完成，应完整沿用上一次有效档案。
			target.FuQiSwordWorld = historical.Clone();
			return;
		}

		current.DaoName = string.IsNullOrWhiteSpace(current.DaoName) ? historical.DaoName : current.DaoName;
		current.PositionRank = string.IsNullOrWhiteSpace(current.PositionRank) ? historical.PositionRank : current.PositionRank;
		current.FounderActorId = current.FounderActorId > 0L ? current.FounderActorId : historical.FounderActorId;
		current.FounderName = string.IsNullOrWhiteSpace(current.FounderName) ? historical.FounderName : current.FounderName;
		current.EstablishedYear = current.EstablishedYear > 0 ? current.EstablishedYear : historical.EstablishedYear;
		if (!current.SwordSteleCreated && historical.SwordSteleCreated)
		{
			current.SwordSteleCreated = true;
			current.SwordSteleCreatedYear = historical.SwordSteleCreatedYear;
			current.SwordSteleFounderActorId = historical.SwordSteleFounderActorId;
			current.SwordSteleFounderName = historical.SwordSteleFounderName;
			current.SwordSteleCityId = historical.SwordSteleCityId;
			current.SwordSteleCityName = historical.SwordSteleCityName;
			current.SwordSteleInscription = historical.SwordSteleInscription;
		}
		current.SwordSteleInsightCount = Math.Max(current.SwordSteleInsightCount, historical.SwordSteleInsightCount);
		if (currentStateWasBlank)
		{
			current.CurrentHolderActorId = historical.CurrentHolderActorId;
			current.CurrentHolderAcquiredYear = historical.CurrentHolderAcquiredYear;
			current.VacantSinceYear = historical.VacantSinceYear;
		}
		MergeFuQiSwordHolderHistory(historical.HolderHistory, current);
		NormalizeFuQiSwordActiveHistory(current);
		target.FuQiSwordWorld = current;
	}

	private static void NormalizeFuQiSwordActiveHistory(XjFuQiSwordWorldArchiveData current)
	{
		if (current?.HolderHistory == null) return;
		int releaseYear = current.CurrentHolderActorId > 0L
			? Math.Max(current.EstablishedYear, current.CurrentHolderAcquiredYear)
			: Math.Max(current.EstablishedYear, current.VacantSinceYear);
		for (int i = 0; i < current.HolderHistory.Count; i++)
		{
			XjFuQiSwordPositionHolderArchiveData entry = current.HolderHistory[i];
			if (entry == null || entry.ReleasedYear > 0) continue;
			if (current.CurrentHolderActorId > 0L && entry.ActorId == current.CurrentHolderActorId)
			{
				continue;
			}
			entry.ReleasedYear = Math.Max(entry.AcquiredYear, releaseYear);
		}
	}

	private static void MergeFuQiSwordHolderHistory(
		IReadOnlyList<XjFuQiSwordPositionHolderArchiveData> historical,
		XjFuQiSwordWorldArchiveData current)
	{
		current.HolderHistory ??= new List<XjFuQiSwordPositionHolderArchiveData>();
		if (historical == null || historical.Count == 0)
		{
			return;
		}

		for (int i = 0; i < historical.Count; i++)
		{
			XjFuQiSwordPositionHolderArchiveData source = historical[i];
			if (source == null || source.ActorId <= 0L || source.AcquiredYear <= 0)
			{
				continue;
			}
			XjFuQiSwordPositionHolderArchiveData match = null;
			for (int j = 0; j < current.HolderHistory.Count; j++)
			{
				XjFuQiSwordPositionHolderArchiveData candidate = current.HolderHistory[j];
				if (candidate != null
					&& candidate.ActorId == source.ActorId
					&& candidate.AcquiredYear == source.AcquiredYear
					&& candidate.IsFounder == source.IsFounder)
				{
					match = candidate;
					break;
				}
			}
			if (match == null)
			{
				current.HolderHistory.Add(source.Clone());
				continue;
			}
			if (string.IsNullOrWhiteSpace(match.ActorName)) match.ActorName = source.ActorName;
			match.ReleasedYear = Math.Max(match.ReleasedYear, source.ReleasedYear);
		}

		current.HolderHistory.Sort((left, right) =>
		{
			if (ReferenceEquals(left, right)) return 0;
			if (left == null) return 1;
			if (right == null) return -1;
			int c = left.AcquiredYear.CompareTo(right.AcquiredYear);
			if (c != 0) return c;
			c = left.ActorId.CompareTo(right.ActorId);
			return c != 0 ? c : right.IsFounder.CompareTo(left.IsFounder);
		});
	}

	private static void MergeQuanBingStruggle(XjWorldArchiveData baseline, XjWorldArchiveData target)
	{
		XjWorldArchiveQuanBingStruggleState historical = baseline.QuanBingStruggle;
		XjWorldArchiveQuanBingStruggleState current = target.QuanBingStruggle;
		if (historical == null)
		{
			return;
		}

		bool currentIsBlank = current == null
			|| current.LastProcessedYear < 0
				&& current.StartYear <= 0
				&& current.EndYearExclusive <= 0
				&& current.InitiatorActorId <= 0L;
		if (!currentIsBlank && current.LastProcessedYear >= historical.LastProcessedYear)
		{
			return;
		}

		target.QuanBingStruggle = new XjWorldArchiveQuanBingStruggleState
		{
			Enabled = historical.Enabled,
			Phase = historical.Phase,
			StartYear = historical.StartYear,
			EndYearExclusive = historical.EndYearExclusive,
			PhaseStartedYear = historical.PhaseStartedYear,
			PhaseEndYearExclusive = historical.PhaseEndYearExclusive,
			CooldownEndYear = historical.CooldownEndYear,
			Tension = historical.Tension,
			ProxyConflictCount = historical.ProxyConflictCount,
			InitiatorActorId = historical.InitiatorActorId,
			InitiatorName = historical.InitiatorName,
			InitialParticipantCount = historical.InitialParticipantCount,
			DeadParticipantCount = historical.DeadParticipantCount,
			HasExternalAuthoritySeizure = historical.HasExternalAuthoritySeizure,
			LastProcessedYear = historical.LastProcessedYear
		};
	}


	private static void MergeJinDanImmortality(
		IReadOnlyList<XjJinDanImmortalityArchiveRecord> baseline,
		List<XjJinDanImmortalityArchiveRecord> current)
	{
		if (baseline == null || current == null) return;
		Dictionary<long, XjJinDanImmortalityArchiveRecord> byActor =
			new Dictionary<long, XjJinDanImmortalityArchiveRecord>();
		for (int i = 0; i < current.Count; i++)
		{
			XjJinDanImmortalityArchiveRecord record = current[i];
			if (record != null && record.ActorId > 0L) byActor[record.ActorId] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjJinDanImmortalityArchiveRecord historical = baseline[i];
			if (historical == null || historical.ActorId <= 0L) continue;
			if (!byActor.TryGetValue(historical.ActorId, out XjJinDanImmortalityArchiveRecord live))
			{
				live = CloneJinDanImmortality(historical);
				current.Add(live);
				byActor[live.ActorId] = live;
				continue;
			}

			// 高境死亡/转世名录属于不可逆世界史。当前快照若因为 Actor 已被池回收而缺项，
			// 不能用“空/活”的临时状态覆盖上一份已经落盘的终局快照。
			FillJinDanSnapshotBlanks(live, historical);
			if (!historical.IsAlive && historical.DeathYear > 0
				&& (live.IsAlive || historical.DeathYear >= live.DeathYear))
			{
				live.IsAlive = false;
				live.YinSiState = XjJinDanYinSiState.Dead;
				live.LifeStatus = string.IsNullOrWhiteSpace(historical.LifeStatus) ? "死亡" : historical.LifeStatus;
				live.DeathYear = historical.DeathYear;
				if (!string.IsNullOrWhiteSpace(historical.DeathReason) || string.IsNullOrWhiteSpace(live.DeathReason))
					live.DeathReason = historical.DeathReason ?? string.Empty;
			}
			live.LastKnownYear = Math.Max(live.LastKnownYear, historical.LastKnownYear);
			live.LastObservedYear = Math.Max(live.LastObservedYear, historical.LastObservedYear);
			live.PursuitCount = Math.Max(live.PursuitCount, historical.PursuitCount);
			live.YinSiExposure = Math.Max(live.YinSiExposure, historical.YinSiExposure);
			live.YinSiKnown |= historical.YinSiKnown;
		}
	}

	private static void FillJinDanSnapshotBlanks(
		XjJinDanImmortalityArchiveRecord target,
		XjJinDanImmortalityArchiveRecord source)
	{
		if (target == null || source == null) return;
		if (target.ActivatedYear <= 0 || source.ActivatedYear > 0 && source.ActivatedYear < target.ActivatedYear)
			target.ActivatedYear = source.ActivatedYear;
		if (string.IsNullOrWhiteSpace(target.Name)) target.Name = source.Name;
		target.Age = Math.Max(target.Age, source.Age);
		if (string.IsNullOrWhiteSpace(target.RealmId)) target.RealmId = source.RealmId;
		if (string.IsNullOrWhiteSpace(target.CultivationPath)) target.CultivationPath = source.CultivationPath;
		if (string.IsNullOrWhiteSpace(target.Achievement)) target.Achievement = source.Achievement;
		if (string.IsNullOrWhiteSpace(target.DaoTu)) target.DaoTu = source.DaoTu;
		if (string.IsNullOrWhiteSpace(target.JinXing)) target.JinXing = source.JinXing;
		if (string.IsNullOrWhiteSpace(target.GuoWei)) target.GuoWei = source.GuoWei;
		target.IsDaoTai |= source.IsDaoTai;
		target.IsKongZheng |= source.IsKongZheng;
		if (target.SectId <= 0L) target.SectId = source.SectId;
		if (string.IsNullOrWhiteSpace(target.SectName)) target.SectName = source.SectName;
		if (target.FamilyId <= 0L) target.FamilyId = source.FamilyId;
		if (string.IsNullOrWhiteSpace(target.DaoTaiPresenceStatus)) target.DaoTaiPresenceStatus = source.DaoTaiPresenceStatus;
		if (target.DaoTaiNextReturnYear <= 0) target.DaoTaiNextReturnYear = source.DaoTaiNextReturnYear;
		if (target.AuthorityCount <= 0) target.AuthorityCount = source.AuthorityCount;
		if (string.IsNullOrWhiteSpace(target.LocalAuthoritySummary)) target.LocalAuthoritySummary = source.LocalAuthoritySummary;
		if (string.IsNullOrWhiteSpace(target.SeizedAuthoritySummary)) target.SeizedAuthoritySummary = source.SeizedAuthoritySummary;
		if (string.IsNullOrWhiteSpace(target.PendingAuthoritySummary)) target.PendingAuthoritySummary = source.PendingAuthoritySummary;
		if (string.IsNullOrWhiteSpace(target.AuthorityLifecycleStatus)) target.AuthorityLifecycleStatus = source.AuthorityLifecycleStatus;
		if (!target.IsAlive)
		{
			target.IntegrationRetreatActive |= source.IntegrationRetreatActive;
			target.IntegrationRetreatEndYear = Math.Max(target.IntegrationRetreatEndYear, source.IntegrationRetreatEndYear);
		}
		if (string.IsNullOrWhiteSpace(target.GuoWeiImageSummary)) target.GuoWeiImageSummary = source.GuoWeiImageSummary;
		if (string.IsNullOrWhiteSpace(target.ShenTongMutationSummary)) target.ShenTongMutationSummary = source.ShenTongMutationSummary;
		target.DaoXing = Math.Max(target.DaoXing, source.DaoXing);
		target.XiuChi = Math.Max(target.XiuChi, source.XiuChi);
		if (string.IsNullOrWhiteSpace(target.LastExposureReason)) target.LastExposureReason = source.LastExposureReason;
		target.LastExposureUpdateYear = Math.Max(target.LastExposureUpdateYear, source.LastExposureUpdateYear);
	}

	private static XjJinDanImmortalityArchiveRecord CloneJinDanImmortality(XjJinDanImmortalityArchiveRecord source)
	{
		return new XjJinDanImmortalityArchiveRecord
		{
			ActorId = source.ActorId,
			ActivatedYear = source.ActivatedYear,
			YinSiExposure = Math.Max(0f, source.YinSiExposure),
			LastExposureReason = source.LastExposureReason ?? string.Empty,
			LastExposureUpdateYear = source.LastExposureUpdateYear,
			YinSiKnown = source.YinSiKnown,
			YinSiState = string.IsNullOrWhiteSpace(source.YinSiState) ? XjJinDanYinSiState.Hidden : source.YinSiState,
			PursuitCount = Math.Max(0, source.PursuitCount),
			LastKnownYear = source.LastKnownYear,
			IsAlive = source.IsAlive,
			Name = source.Name ?? string.Empty,
			Age = Math.Max(0, source.Age),
			RealmId = source.RealmId ?? string.Empty,
			CultivationPath = source.CultivationPath ?? string.Empty,
			Achievement = source.Achievement ?? string.Empty,
			DaoTu = source.DaoTu ?? string.Empty,
			JinXing = source.JinXing ?? string.Empty,
			GuoWei = source.GuoWei ?? string.Empty,
			IsDaoTai = source.IsDaoTai,
			IsKongZheng = source.IsKongZheng,
			SectId = Math.Max(0L, source.SectId),
			SectName = source.SectName ?? string.Empty,
			FamilyId = Math.Max(0L, source.FamilyId),
			DaoTaiPresenceStatus = source.DaoTaiPresenceStatus ?? string.Empty,
			DaoTaiNextReturnYear = Math.Max(0, source.DaoTaiNextReturnYear),
			AuthorityCount = Math.Max(0, source.AuthorityCount),
			LocalAuthoritySummary = source.LocalAuthoritySummary ?? string.Empty,
			SeizedAuthoritySummary = source.SeizedAuthoritySummary ?? string.Empty,
			PendingAuthoritySummary = source.PendingAuthoritySummary ?? string.Empty,
			AuthorityLifecycleStatus = source.AuthorityLifecycleStatus ?? string.Empty,
			IntegrationRetreatActive = source.IntegrationRetreatActive,
			IntegrationRetreatEndYear = Math.Max(0, source.IntegrationRetreatEndYear),
			GuoWeiImageSummary = source.GuoWeiImageSummary ?? string.Empty,
			ShenTongMutationSummary = source.ShenTongMutationSummary ?? string.Empty,
			DaoXing = Math.Max(0, source.DaoXing),
			XiuChi = Math.Max(0, source.XiuChi),
			LifeStatus = string.IsNullOrWhiteSpace(source.LifeStatus) ? (source.IsAlive ? "存世" : "死亡") : source.LifeStatus,
			DeathYear = Math.Max(0, source.DeathYear),
			DeathReason = source.DeathReason ?? string.Empty,
			LastObservedYear = Math.Max(0, source.LastObservedYear)
		};
	}

	private static void MergeGuoWei(
		IReadOnlyList<XjWorldArchiveGuoWeiRecord> baseline,
		List<XjWorldArchiveGuoWeiRecord> current)
	{
		Dictionary<long, XjWorldArchiveGuoWeiRecord> byActor = new Dictionary<long, XjWorldArchiveGuoWeiRecord>();
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveGuoWeiRecord record = current[i];
			if (record != null && record.ActorId > 0L) byActor[record.ActorId] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveGuoWeiRecord historical = baseline[i];
			if (historical == null || historical.ActorId <= 0L) continue;
			if (!byActor.TryGetValue(historical.ActorId, out XjWorldArchiveGuoWeiRecord live))
			{
				current.Add(CloneGuoWei(historical));
				byActor[historical.ActorId] = current[current.Count - 1];
				continue;
			}

			if (string.IsNullOrWhiteSpace(live.ActorName)) live.ActorName = historical.ActorName;
			if (string.IsNullOrWhiteSpace(live.FamilyName)) live.FamilyName = historical.FamilyName;
			if (string.IsNullOrWhiteSpace(live.DaoTu)) live.DaoTu = historical.DaoTu;
			if (string.IsNullOrWhiteSpace(live.JinXing)) live.JinXing = historical.JinXing;
			if (string.IsNullOrWhiteSpace(live.GuoWei)) live.GuoWei = historical.GuoWei;
			if (live.Year <= 0 || historical.Year > 0 && historical.Year < live.Year) live.Year = historical.Year;
			if (historical.EndedYear > live.EndedYear)
			{
				live.EndedYear = historical.EndedYear;
				live.LifecycleStatus = string.IsNullOrWhiteSpace(historical.LifecycleStatus) ? "Deceased" : historical.LifecycleStatus;
				live.EndReason = historical.EndReason;
			}
			else
			{
				if (string.IsNullOrWhiteSpace(live.LifecycleStatus)) live.LifecycleStatus = historical.LifecycleStatus;
				if (string.IsNullOrWhiteSpace(live.EndReason)) live.EndReason = historical.EndReason;
			}
		}
	}

	private static void MergeReincarnations(
		IReadOnlyList<XjWorldArchiveReincarnationRecord> baseline,
		List<XjWorldArchiveReincarnationRecord> current)
	{
		Dictionary<long, XjWorldArchiveReincarnationRecord> bySource = new Dictionary<long, XjWorldArchiveReincarnationRecord>();
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveReincarnationRecord record = current[i];
			if (record != null && record.ActorId > 0L) bySource[record.ActorId] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveReincarnationRecord historical = baseline[i];
			if (historical == null || historical.ActorId <= 0L) continue;
			if (!bySource.TryGetValue(historical.ActorId, out XjWorldArchiveReincarnationRecord live))
			{
				current.Add(CloneReincarnation(historical));
				bySource[historical.ActorId] = current[current.Count - 1];
				continue;
			}

			FillReincarnationBlanks(live, historical);
			bool historicalApplied = historical.TargetActorId > 0L || string.Equals(historical.Status, "Applied", StringComparison.Ordinal);
			bool liveApplied = live.TargetActorId > 0L || string.Equals(live.Status, "Applied", StringComparison.Ordinal);
			if (historicalApplied && !liveApplied)
			{
				live.TargetActorId = historical.TargetActorId;
				live.TargetActorName = historical.TargetActorName;
				live.AppliedYear = historical.AppliedYear;
				live.Status = historical.Status;
			}
		}
	}

	private static void MergeQuanBing(
		IReadOnlyList<XjGuoWeiQuanBingArchiveData> baseline,
		List<XjGuoWeiQuanBingArchiveData> current)
	{
		Dictionary<long, XjGuoWeiQuanBingArchiveData> byActor = new Dictionary<long, XjGuoWeiQuanBingArchiveData>();
		for (int i = 0; i < current.Count; i++)
		{
			XjGuoWeiQuanBingArchiveData record = current[i];
			if (record != null && record.ActorId > 0L) byActor[record.ActorId] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjGuoWeiQuanBingArchiveData historical = baseline[i];
			if (historical == null || historical.ActorId <= 0L) continue;
			if (!byActor.TryGetValue(historical.ActorId, out XjGuoWeiQuanBingArchiveData live))
			{
				current.Add(CloneQuanBing(historical));
				byActor[historical.ActorId] = current[current.Count - 1];
				continue;
			}

			if (string.IsNullOrWhiteSpace(live.ActorName)) live.ActorName = historical.ActorName;
			if (string.IsNullOrWhiteSpace(live.DaoTu)) live.DaoTu = historical.DaoTu;
			if (string.IsNullOrWhiteSpace(live.GuoWei)) live.GuoWei = historical.GuoWei;
			if (live.AcquiredYear <= 0 || (historical.AcquiredYear > 0 && historical.AcquiredYear < live.AcquiredYear))
			{
				live.AcquiredYear = historical.AcquiredYear;
			}

			bool historicalReleased = IsReleased(historical.LifecycleStatus, historical.ReleasedYear);
			bool liveReleased = IsReleased(live.LifecycleStatus, live.ReleasedYear);
			if (historicalReleased && !liveReleased)
			{
				// 已经落盘的死亡/释放是不可逆事实，不能被当前空缓存或活体扫描覆盖。
				CopyReleasedSnapshot(live, historical);
				continue;
			}

			if (liveReleased)
			{
				// 释放后的记录是冻结历史。允许用上一份完整快照补齐旧版在死亡时清空的权柄明细，
				// 但不把旧的封锁/闭关运行态灌回当前世界。
				FillReleasedAuthorityDetails(live, historical);
				if (historical.ReleasedYear > live.ReleasedYear)
				{
					live.ReleasedYear = historical.ReleasedYear;
					live.LifecycleStatus = string.IsNullOrWhiteSpace(historical.LifecycleStatus) ? "Released" : historical.LifecycleStatus;
					live.ReleaseReason = historical.ReleaseReason;
				}
				else
				{
					if (string.IsNullOrWhiteSpace(live.LifecycleStatus)) live.LifecycleStatus = historical.LifecycleStatus;
					if (string.IsNullOrWhiteSpace(live.ReleaseReason)) live.ReleaseReason = historical.ReleaseReason;
				}
			}
			// 两边都处于活动态时，以当前内存为准。权柄融合、消耗、封锁解除均是合法变化，
			// 不能因为上一份存档有值就把已经清空的旧运行态复活。
		}
	}

	private static bool IsReleased(string lifecycleStatus, int releasedYear)
	{
		return releasedYear > 0
			|| string.Equals(lifecycleStatus, "Released", StringComparison.Ordinal);
	}

	private static void CopyReleasedSnapshot(
		XjGuoWeiQuanBingArchiveData target,
		XjGuoWeiQuanBingArchiveData source)
	{
		target.ActorName = string.IsNullOrWhiteSpace(source.ActorName) ? target.ActorName : source.ActorName;
		target.DaoTu = string.IsNullOrWhiteSpace(source.DaoTu) ? target.DaoTu : source.DaoTu;
		target.GuoWei = string.IsNullOrWhiteSpace(source.GuoWei) ? target.GuoWei : source.GuoWei;
		if (!string.IsNullOrWhiteSpace(source.LocalQuanBing)) target.LocalQuanBing = source.LocalQuanBing;
		if (!string.IsNullOrWhiteSpace(source.SeizedQuanBing)) target.SeizedQuanBing = source.SeizedQuanBing;
		if (!string.IsNullOrWhiteSpace(source.SeizedQuanBingSources)) target.SeizedQuanBingSources = source.SeizedQuanBingSources;
		if (!string.IsNullOrWhiteSpace(source.ForeignQuanBing)) target.ForeignQuanBing = source.ForeignQuanBing;
		if (!string.IsNullOrWhiteSpace(source.WithdrawnToDongTian)) target.WithdrawnToDongTian = source.WithdrawnToDongTian;
		if (!string.IsNullOrWhiteSpace(source.GuoWeiZhongAi)) target.GuoWeiZhongAi = source.GuoWeiZhongAi;
		target.PendingExternalZhengWeiDaoTu = string.Empty;
		target.LockUntilYear = 0;
		target.IntegrationRetreatActive = false;
		target.IntegrationRetreatEndYear = 0;
		if (!string.IsNullOrWhiteSpace(source.Summary)) target.Summary = source.Summary;
		target.LifecycleStatus = string.IsNullOrWhiteSpace(source.LifecycleStatus) ? "Released" : source.LifecycleStatus;
		target.AcquiredYear = source.AcquiredYear > 0 ? source.AcquiredYear : target.AcquiredYear;
		target.ReleasedYear = source.ReleasedYear;
		target.ReleaseReason = source.ReleaseReason;
	}

	private static void FillReleasedAuthorityDetails(
		XjGuoWeiQuanBingArchiveData target,
		XjGuoWeiQuanBingArchiveData source)
	{
		if (string.IsNullOrWhiteSpace(target.LocalQuanBing)) target.LocalQuanBing = source.LocalQuanBing;
		if (string.IsNullOrWhiteSpace(target.SeizedQuanBing)) target.SeizedQuanBing = source.SeizedQuanBing;
		if (string.IsNullOrWhiteSpace(target.SeizedQuanBingSources)) target.SeizedQuanBingSources = source.SeizedQuanBingSources;
		if (string.IsNullOrWhiteSpace(target.ForeignQuanBing)) target.ForeignQuanBing = source.ForeignQuanBing;
		if (string.IsNullOrWhiteSpace(target.WithdrawnToDongTian)) target.WithdrawnToDongTian = source.WithdrawnToDongTian;
		if (string.IsNullOrWhiteSpace(target.GuoWeiZhongAi)) target.GuoWeiZhongAi = source.GuoWeiZhongAi;
		if (string.IsNullOrWhiteSpace(target.Summary)) target.Summary = source.Summary;
	}

	private static void MergeLostAuthorities(
		IReadOnlyList<XjGuoWeiQuanBingLostAuthorityArchiveData> baseline,
		List<XjGuoWeiQuanBingLostAuthorityArchiveData> current)
	{
		Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData> byKey =
			new Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjGuoWeiQuanBingLostAuthorityArchiveData record = current[i];
			if (record != null) byKey[BuildLostAuthorityKey(record)] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjGuoWeiQuanBingLostAuthorityArchiveData historical = baseline[i];
			if (historical == null) continue;
			string key = BuildLostAuthorityKey(historical);
			if (!byKey.TryGetValue(key, out XjGuoWeiQuanBingLostAuthorityArchiveData live))
			{
				current.Add(CloneLostAuthority(historical));
				byKey[key] = current[current.Count - 1];
				continue;
			}
			if (string.IsNullOrWhiteSpace(live.TargetDaoTu)) live.TargetDaoTu = historical.TargetDaoTu;
			if (live.Year <= 0) live.Year = historical.Year;
			if (string.IsNullOrWhiteSpace(live.Reason)) live.Reason = historical.Reason;
		}
	}


	private static void MergeGuZunRecords(
		IReadOnlyList<XjGuZunArchiveRecord> baseline,
		List<XjGuZunArchiveRecord> current)
	{
		Dictionary<string, XjGuZunArchiveRecord> byId = new Dictionary<string, XjGuZunArchiveRecord>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjGuZunArchiveRecord record = current[i];
			if (record != null && !string.IsNullOrWhiteSpace(record.ArchiveId)) byId[record.ArchiveId] = record;
		}
		for (int i = 0; i < baseline.Count; i++)
		{
			XjGuZunArchiveRecord historical = baseline[i];
			if (historical == null || string.IsNullOrWhiteSpace(historical.ArchiveId)
				|| historical.DeathYear > 0 && !historical.HeavenFavored && !historical.IsCurrentlyManifested) continue;
			if (!byId.TryGetValue(historical.ArchiveId, out XjGuZunArchiveRecord live))
			{
				live = CloneGuZun(historical);
				current.Add(live);
				byId[live.ArchiveId] = live;
				continue;
			}

			if (string.IsNullOrWhiteSpace(live.ActorName)) live.ActorName = historical.ActorName;
			if (string.IsNullOrWhiteSpace(live.AssetId)) live.AssetId = historical.AssetId;
			if (string.IsNullOrWhiteSpace(live.DaoTu)) live.DaoTu = historical.DaoTu;
			if (string.IsNullOrWhiteSpace(live.CultivationPath)) live.CultivationPath = historical.CultivationPath;
			if (historical.HighestRealmOrder > live.HighestRealmOrder
				|| historical.HighestRealmOrder == live.HighestRealmOrder && historical.HighestMinorStage > live.HighestMinorStage
				|| historical.HighestRealmOrder == live.HighestRealmOrder && historical.HighestMinorStage == live.HighestMinorStage
					&& historical.HighestRealmProgress > live.HighestRealmProgress)
			{
				CopyHighestGuZunSnapshot(live, historical);
			}
			else if (string.IsNullOrWhiteSpace(live.HighestSnapshotJson))
			{
				CopyHighestGuZunSnapshot(live, historical);
			}
			if (live.FirstObservedYear <= 0 || historical.FirstObservedYear > 0 && historical.FirstObservedYear < live.FirstObservedYear)
				live.FirstObservedYear = historical.FirstObservedYear;
			live.HeavenFavored |= historical.HeavenFavored;
			live.HeavenFavorScore = Math.Max(live.HeavenFavorScore, historical.HeavenFavorScore);
			live.ReappearanceCount = Math.Max(live.ReappearanceCount, historical.ReappearanceCount);
			live.LastReappearanceYear = Math.Max(live.LastReappearanceYear, historical.LastReappearanceYear);
			if (historical.DeathYear > live.DeathYear)
			{
				live.DeathYear = historical.DeathYear;
				live.DeathAge = historical.DeathAge;
				live.DeathCause = historical.DeathCause;
				live.DeathTileX = historical.DeathTileX;
				live.DeathTileY = historical.DeathTileY;
			}
			// CurrentActorId/IsCurrentlyManifested 是可逆运行态，不属于受保护的世界史。
			// 保存前当前注册表已经是唯一权威，绝不能用旧快照把已死亡故尊重新标活。
		}
	}

	private static void MergeDaoTuContinuity(
		IReadOnlyList<XjDaoTuHighRealmContinuityRecord> baseline,
		List<XjDaoTuHighRealmContinuityRecord> current)
	{
		Dictionary<string, XjDaoTuHighRealmContinuityRecord> byDaoTu = new Dictionary<string, XjDaoTuHighRealmContinuityRecord>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjDaoTuHighRealmContinuityRecord record = current[i];
			if (record != null && !string.IsNullOrWhiteSpace(record.DaoTu)) byDaoTu[record.DaoTu.Trim()] = record;
		}
		for (int i = 0; i < baseline.Count; i++)
		{
			XjDaoTuHighRealmContinuityRecord historical = baseline[i];
			if (historical == null || string.IsNullOrWhiteSpace(historical.DaoTu)) continue;
			string key = historical.DaoTu.Trim();
			if (!byDaoTu.TryGetValue(key, out XjDaoTuHighRealmContinuityRecord live))
			{
				live = new XjDaoTuHighRealmContinuityRecord
				{
					DaoTu = key,
					LastHighRealmPresentYear = historical.LastHighRealmPresentYear,
					LastReappearanceYear = historical.LastReappearanceYear
				};
				current.Add(live);
				byDaoTu[key] = live;
				continue;
			}
			live.LastHighRealmPresentYear = Math.Max(live.LastHighRealmPresentYear, historical.LastHighRealmPresentYear);
			live.LastReappearanceYear = Math.Max(live.LastReappearanceYear, historical.LastReappearanceYear);
		}
	}

	private static void CopyHighestGuZunSnapshot(XjGuZunArchiveRecord target, XjGuZunArchiveRecord source)
	{
		target.DaoTu = source.DaoTu;
		target.CultivationPath = source.CultivationPath;
		target.HighestRealmId = source.HighestRealmId;
		target.HighestRealmOrder = source.HighestRealmOrder;
		target.HighestMinorStage = source.HighestMinorStage;
		target.HighestRealmProgress = source.HighestRealmProgress;
		target.HighestJinDanYiXiang = source.HighestJinDanYiXiang;
		target.HighestSnapshotJson = source.HighestSnapshotJson;
		target.HighestReachedYear = source.HighestReachedYear;
		target.SavedTraitIds = source.SavedTraitIds == null ? new List<string>() : new List<string>(source.SavedTraitIds);
	}

	private static XjGuZunArchiveRecord CloneGuZun(XjGuZunArchiveRecord source)
	{
		return new XjGuZunArchiveRecord
		{
			SchemaVersion = source.SchemaVersion,
			ArchiveId = source.ArchiveId,
			SourceActorId = source.SourceActorId,
			CurrentActorId = 0L,
			ActorName = source.ActorName,
			AssetId = source.AssetId,
			DaoTu = source.DaoTu,
			CultivationPath = source.CultivationPath,
			HighestRealmId = source.HighestRealmId,
			HighestRealmOrder = source.HighestRealmOrder,
			HighestMinorStage = source.HighestMinorStage,
			HighestRealmProgress = source.HighestRealmProgress,
			HighestJinDanYiXiang = source.HighestJinDanYiXiang,
			HighestSnapshotJson = source.HighestSnapshotJson,
			SavedTraitIds = source.SavedTraitIds == null ? new List<string>() : new List<string>(source.SavedTraitIds),
			FirstObservedYear = source.FirstObservedYear,
			HighestReachedYear = source.HighestReachedYear,
			DeathYear = source.DeathYear,
			DeathAge = source.DeathAge,
			DeathCause = source.DeathCause,
			DeathTileX = source.DeathTileX,
			DeathTileY = source.DeathTileY,
			HeavenFavored = source.HeavenFavored,
			HeavenFavorScore = source.HeavenFavorScore,
			IsCurrentlyManifested = false,
			ReappearanceCount = source.ReappearanceCount,
			LastReappearanceYear = source.LastReappearanceYear
		};
	}

	private static string BuildLostAuthorityKey(XjGuoWeiQuanBingLostAuthorityArchiveData record)
	{
		return (record.SourceDaoTu ?? string.Empty).Trim() + "|" + (record.Authority ?? string.Empty).Trim();
	}

	private static XjWorldArchiveGuoWeiRecord CloneGuoWei(XjWorldArchiveGuoWeiRecord source)
	{
		return new XjWorldArchiveGuoWeiRecord
		{
			ActorId = source.ActorId,
			ActorName = source.ActorName,
			FamilyName = source.FamilyName,
			DaoTu = source.DaoTu,
			JinXing = source.JinXing,
			GuoWei = source.GuoWei,
			Year = source.Year,
			LifecycleStatus = source.LifecycleStatus,
			EndedYear = source.EndedYear,
			EndReason = source.EndReason
		};
	}

	private static XjWorldArchiveReincarnationRecord CloneReincarnation(XjWorldArchiveReincarnationRecord source)
	{
		return new XjWorldArchiveReincarnationRecord
		{
			ActorId = source.ActorId,
			ActorName = source.ActorName,
			RaceKey = source.RaceKey,
			FamilyStableId = source.FamilyStableId,
			RealmId = source.RealmId,
			DaoTu = source.DaoTu,
			GongFaName = source.GongFaName,
			GongFaGrade = source.GongFaGrade,
			// 旧档字段保留在 JSON schema 中，但不再跨存档合并或恢复。
			GongFaStage = 0,
			GongFaProgress = 0f,
			JinXing = source.JinXing,
			GuoWei = source.GuoWei,
			JinDanYiXiang = source.JinDanYiXiang,
			DeathYear = source.DeathYear,
			Mode = source.Mode,
			GuoWeiZhongAi = source.GuoWeiZhongAi,
			FuQiPayload = source.FuQiPayload,
			TargetActorId = source.TargetActorId,
			TargetActorName = source.TargetActorName,
			AppliedYear = source.AppliedYear,
			Status = source.Status
		};
	}

	private static void FillReincarnationBlanks(
		XjWorldArchiveReincarnationRecord live,
		XjWorldArchiveReincarnationRecord historical)
	{
		if (string.IsNullOrWhiteSpace(live.ActorName)) live.ActorName = historical.ActorName;
		if (string.IsNullOrWhiteSpace(live.RaceKey)) live.RaceKey = historical.RaceKey;
		if (live.FamilyStableId <= 0L) live.FamilyStableId = historical.FamilyStableId;
		if (string.IsNullOrWhiteSpace(live.RealmId)) live.RealmId = historical.RealmId;
		if (string.IsNullOrWhiteSpace(live.DaoTu)) live.DaoTu = historical.DaoTu;
		if (string.IsNullOrWhiteSpace(live.GongFaName)) live.GongFaName = historical.GongFaName;
		if (live.GongFaGrade <= 0) live.GongFaGrade = historical.GongFaGrade;
		live.GongFaStage = 0;
		live.GongFaProgress = 0f;
		if (string.IsNullOrWhiteSpace(live.JinXing)) live.JinXing = historical.JinXing;
		if (string.IsNullOrWhiteSpace(live.GuoWei)) live.GuoWei = historical.GuoWei;
		if (live.JinDanYiXiang <= 0) live.JinDanYiXiang = historical.JinDanYiXiang;
		if (live.DeathYear <= 0) live.DeathYear = historical.DeathYear;
		if (string.IsNullOrWhiteSpace(live.Mode)) live.Mode = historical.Mode;
		if (string.IsNullOrWhiteSpace(live.GuoWeiZhongAi)) live.GuoWeiZhongAi = historical.GuoWeiZhongAi;
		if (string.IsNullOrWhiteSpace(live.FuQiPayload)) live.FuQiPayload = historical.FuQiPayload;
		if (string.IsNullOrWhiteSpace(live.Status)) live.Status = historical.Status;
	}

	private static XjGuoWeiQuanBingArchiveData CloneQuanBing(XjGuoWeiQuanBingArchiveData source)
	{
		return new XjGuoWeiQuanBingArchiveData
		{
			ActorId = source.ActorId,
			ActorName = source.ActorName,
			DaoTu = source.DaoTu,
			GuoWei = source.GuoWei,
			LocalQuanBing = source.LocalQuanBing,
			SeizedQuanBing = source.SeizedQuanBing,
			SeizedQuanBingSources = source.SeizedQuanBingSources,
			ForeignQuanBing = source.ForeignQuanBing,
			WithdrawnToDongTian = source.WithdrawnToDongTian,
			GuoWeiZhongAi = source.GuoWeiZhongAi,
			PendingExternalZhengWeiDaoTu = source.PendingExternalZhengWeiDaoTu,
			LockUntilYear = source.LockUntilYear,
			IntegrationRetreatActive = source.IntegrationRetreatActive,
			IntegrationRetreatEndYear = source.IntegrationRetreatEndYear,
			Summary = source.Summary,
			LifecycleStatus = source.LifecycleStatus,
			AcquiredYear = source.AcquiredYear,
			ReleasedYear = source.ReleasedYear,
			ReleaseReason = source.ReleaseReason
		};
	}

	private static XjGuoWeiQuanBingLostAuthorityArchiveData CloneLostAuthority(XjGuoWeiQuanBingLostAuthorityArchiveData source)
	{
		return new XjGuoWeiQuanBingLostAuthorityArchiveData
		{
			SourceDaoTu = source.SourceDaoTu,
			Authority = source.Authority,
			TargetDaoTu = source.TargetDaoTu,
			Year = source.Year,
			Reason = source.Reason
		};
	}
}
