using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.HighRealm;

namespace XuanJianVNext.Systems.Archive;

/// <summary>
/// 金丹历史、转世来源、权柄持有与永久流失均属于不可逆世界史。
/// 保存前把上一次成功读入的高境档案与当前快照合并，防止活体扫描、死亡释放、
/// 读档重建顺序或空缓存把已落盘历史覆盖为空。
/// </summary>
internal static class XjWorldArchiveHighRealmRetention
{
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
		MergeReincarnations(baseline.ReincarnationRecords, target.ReincarnationRecords);
		MergeQuanBing(baseline.QuanBingRecords, target.QuanBingRecords);
		MergeLostAuthorities(baseline.QuanBingLostAuthorities, target.QuanBingLostAuthorities);
		MergeQuanBingStruggle(baseline, target);
		return target;
	}

	private static void EnsureLists(XjWorldArchiveData data)
	{
		data.GuoWeiRegistry ??= new List<XjWorldArchiveGuoWeiRecord>();
		data.ReincarnationRecords ??= new List<XjWorldArchiveReincarnationRecord>();
		data.QuanBingRecords ??= new List<XjGuoWeiQuanBingArchiveData>();
		data.QuanBingLostAuthorities ??= new List<XjGuoWeiQuanBingLostAuthorityArchiveData>();
		data.QuanBingStruggle ??= new XjWorldArchiveQuanBingStruggleState();
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
			StartYear = historical.StartYear,
			EndYearExclusive = historical.EndYearExclusive,
			InitiatorActorId = historical.InitiatorActorId,
			InitiatorName = historical.InitiatorName,
			InitialParticipantCount = historical.InitialParticipantCount,
			DeadParticipantCount = historical.DeadParticipantCount,
			HasExternalAuthoritySeizure = historical.HasExternalAuthoritySeizure,
			LastProcessedYear = historical.LastProcessedYear
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
