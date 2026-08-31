using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Visual;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjYinSiExposurePursuitSystem
{
	private const float DiscoveryThreshold = 5f;
	private const float TracedThreshold = 1f;
	private const float NearDiscoveryThreshold = 3f;
	private const float BaseDecayPerYear = 0.08f;
	private const float ShelteredDecayPerYear = 0.22f;
	private const int MaxActiveMissions = 2;
	private const int MaxManifestPerMission = 2;
	private const int GlobalMissionCooldownYears = 5;
	private const int TargetMissionCooldownYears = 25;
	private const int MaxMissionRuntimeYears = 15;
	private const int MaxArchivedMissions = 512;
	private const float DaoTaiGazeChance = 0.10f;
	private const float DirectPursuitChanceScale = DiscoveryThreshold * 4f;
	private const float MaxDirectPursuitKillChance = 0.50f;
	private const int YinSiDirectPursuitAttackType = 11;
	private const float MinDirectPursuitDelaySeconds = 5f;
	private const float MaxDirectPursuitDelaySeconds = 10f;

	private static readonly Dictionary<long, XjYinSiMissionArchiveRecord> ByMissionId = new Dictionary<long, XjYinSiMissionArchiveRecord>();
	private static readonly Dictionary<long, long> ActiveMissionIdByTarget = new Dictionary<long, long>();
	private static readonly Dictionary<long, long> MissionIdByManifestActor = new Dictionary<long, long>();
	private static readonly HashSet<long> PendingKnownTargetIds = new HashSet<long>();

	private struct PendingDirectPursuitState
	{
		internal long TargetActorId;
		internal int CurrentYear;
		internal float KillChance;
		internal float Roll;
		internal float DueTime;

		internal PendingDirectPursuitState(long targetActorId, int currentYear, float killChance, float roll, float dueTime)
		{
			TargetActorId = targetActorId;
			CurrentYear = currentYear;
			KillChance = killChance;
			Roll = roll;
			DueTime = dueTime;
		}
	}

	private static readonly Dictionary<long, PendingDirectPursuitState> PendingDirectPursuits = new Dictionary<long, PendingDirectPursuitState>();
	private static readonly List<long> PendingDirectPursuitReadyIds = new List<long>(2);
	private static float _nextDirectPursuitDueTime = float.PositiveInfinity;
	internal static bool HasPendingRuntimePursuit
		=> PendingDirectPursuits.Count > 0
			&& UnityEngine.Time.unscaledTime >= _nextDirectPursuitDueTime;
	// 道胎垂眸只需要阻止同一角色在同一年重复触发；保留哈希键会随年份永久增长。
	private static readonly Dictionary<long, int> DaoTaiGazeYearByActorId = new Dictionary<long, int>();
	// 年度推进只在主线程调度，复用临时缓冲以避免长期世界每年为阴司任务产生 GC。
	private static readonly List<long> PendingTargetBuffer = new List<long>();
	private static readonly List<long> DueMissionBuffer = new List<long>();
	private static readonly List<XjYinSiMissionArchiveRecord> TerminalMissionBuffer = new List<XjYinSiMissionArchiveRecord>();
	private static readonly List<long> ManifestEndBuffer = new List<long>(MaxManifestPerMission);

	private enum AnnualPhase : byte
	{
		None = 0,
		KnownTargets = 1,
		DueMissions = 2
	}

	private static AnnualPhase _annualPhase;
	private static int _annualYear;
	private static int _annualIndex;
	private static int _lastMissionScheduledYear = -9999;

	internal static int ActiveMissionCount => ActiveMissionIdByTarget.Count;

	internal static bool RecordExternalJinDanAction(Actor actor, float exposure, string reason, int currentYear, bool directlyKnown = false)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || actor?.data == null || exposure < 0f || !XjJinDanImmortalityRegistry.IsPastYinSiAttentionAge(actor)) return false;
		XjJinDanImmortalityRegistry.EnsureActivated(actor, currentYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjJinDanImmortalityRegistry.TryGet(actorId, out XjJinDanImmortalityArchiveRecord record) || !record.IsAlive) return false;
		ApplyLazyDecay(record, currentYear, IsSheltered(actorId));
		if (directlyKnown) record.YinSiExposure = Math.Max(record.YinSiExposure, DiscoveryThreshold);
		else record.YinSiExposure = Math.Max(0f, record.YinSiExposure + exposure);
		record.LastExposureReason = string.IsNullOrWhiteSpace(reason) ? "暂无" : reason.Trim();
		record.LastKnownYear = Math.Max(record.LastKnownYear, currentYear);
		TryTriggerDaoTaiGaze(actor, record, reason, currentYear);
		bool discoveredNow = !record.YinSiKnown && record.YinSiExposure >= DiscoveryThreshold;
		if (discoveredNow)
		{
			record.YinSiKnown = true;
			record.YinSiState = XjJinDanYinSiState.Known;
			WriteDiscoveryHistory(actor, reason, currentYear);
		}
		else if (!record.YinSiKnown)
		{
			record.YinSiState = ResolveUnknownState(record.YinSiExposure);
		}
		XjJinDanImmortalityRegistry.NotifyRecordChanged();
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.JinDan | XjCodexDirtyFlags.History);
		if (record.YinSiKnown && !ActiveMissionIdByTarget.ContainsKey(record.ActorId))
		{
			if (!TryScheduleMission(actor, record, currentYear)) PendingKnownTargetIds.Add(record.ActorId);
		}
		return true;
	}

	internal static float ReadEffectiveExposure(long actorId, int currentYear)
	{
		if (!XjJinDanImmortalityRegistry.TryGet(actorId, out XjJinDanImmortalityArchiveRecord record)) return 0f;
		if (ApplyLazyDecay(record, currentYear, IsSheltered(actorId))) MarkExposureChanged();
		return record.YinSiExposure;
	}

	internal static bool HasPendingAnnualWorkForYear(int currentYear)
	{
		return currentYear > 0 && _annualYear == currentYear && _annualPhase != AnnualPhase.None;
	}

	internal static bool BeginYear(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0) return false;
		if (HasPendingAnnualWorkForYear(currentYear)) return true;
		ClearPendingAnnualWork();
		_annualYear = currentYear;
		_annualPhase = AnnualPhase.KnownTargets;
		_annualIndex = 0;
		// 仅处理已经被阴司知悉、且等待下一轮追索的稀疏目标；暴露度本身只在读取或事件发生时懒衰减。
		if (PendingKnownTargetIds.Count > 0)
		{
			PendingTargetBuffer.AddRange(PendingKnownTargetIds);
			PendingTargetBuffer.Sort();
		}
		return true;
	}

	internal static bool TickPendingAnnualWork(int itemBudget = 4, double timeBudgetMs = 0.30d)
	{
		if (_annualYear <= 0 || _annualPhase == AnnualPhase.None) return true;
		XjCooperativeBudget budget = new XjCooperativeBudget(
			Math.Max(1, itemBudget),
			timeBudgetMs,
			XjRuntimeFramePriority.Background);

		while (_annualPhase != AnnualPhase.None && !budget.ShouldYield)
		{
			if (_annualPhase == AnnualPhase.KnownTargets)
			{
				while (_annualIndex < PendingTargetBuffer.Count && budget.TryTake())
				{
					ProcessPendingKnownTarget(PendingTargetBuffer[_annualIndex++], _annualYear);
				}
				if (_annualIndex < PendingTargetBuffer.Count) return false;

				PendingTargetBuffer.Clear();
				DueMissionBuffer.Clear();
				foreach (KeyValuePair<long, XjYinSiMissionArchiveRecord> pair in ByMissionId)
				{
					XjYinSiMissionArchiveRecord mission = pair.Value;
					if (mission == null || IsTerminal(mission.Stage) || mission.NextActionYear > _annualYear) continue;
					DueMissionBuffer.Add(pair.Key);
				}
				DueMissionBuffer.Sort();
				_annualIndex = 0;
				_annualPhase = AnnualPhase.DueMissions;
				continue;
			}

			if (_annualPhase == AnnualPhase.DueMissions)
			{
				while (_annualIndex < DueMissionBuffer.Count && budget.TryTake())
				{
					long missionId = DueMissionBuffer[_annualIndex++];
					if (ByMissionId.TryGetValue(missionId, out XjYinSiMissionArchiveRecord mission))
					{
						long missionSample = XjRuntimeDiagnostics.BeginNamedSample();
						try
						{
							AdvanceMission(mission, _annualYear);
						}
						finally
						{
							XjRuntimeDiagnostics.EndNamedSample("annual.YinSi.advanceMission", missionSample);
						}
					}
				}
				if (_annualIndex < DueMissionBuffer.Count) return false;
				ClearPendingAnnualWork();
				return true;
			}
		}

		return _annualPhase == AnnualPhase.None;
	}

	private static void ProcessPendingKnownTarget(long actorId, int currentYear)
	{
		if (!XjJinDanImmortalityRegistry.TryGet(actorId, out XjJinDanImmortalityArchiveRecord record)
			|| record == null || !record.IsAlive || !record.YinSiKnown)
		{
			PendingKnownTargetIds.Remove(actorId);
			return;
		}
		if (ActiveMissionIdByTarget.ContainsKey(actorId))
		{
			PendingKnownTargetIds.Remove(actorId);
			return;
		}
		if (currentYear - record.LastKnownYear < TargetMissionCooldownYears) return;
		if (!XjScheduler.ResolveActor(actorId, out Actor target) || target?.data == null || !target.isAlive()) return;
		if (TryScheduleMission(target, record, currentYear)) PendingKnownTargetIds.Remove(actorId);
	}

	internal static void TickYear(int currentYear)
	{
		if (!BeginYear(currentYear)) return;
		while (HasPendingAnnualWorkForYear(currentYear))
		{
			if (_annualPhase == AnnualPhase.KnownTargets)
			{
				while (_annualIndex < PendingTargetBuffer.Count)
					ProcessPendingKnownTarget(PendingTargetBuffer[_annualIndex++], currentYear);
				PendingTargetBuffer.Clear();
				DueMissionBuffer.Clear();
				foreach (KeyValuePair<long, XjYinSiMissionArchiveRecord> pair in ByMissionId)
				{
					XjYinSiMissionArchiveRecord mission = pair.Value;
					if (mission != null && !IsTerminal(mission.Stage) && mission.NextActionYear <= currentYear)
						DueMissionBuffer.Add(pair.Key);
				}
				DueMissionBuffer.Sort();
				_annualIndex = 0;
				_annualPhase = AnnualPhase.DueMissions;
			}
			while (_annualPhase == AnnualPhase.DueMissions && _annualIndex < DueMissionBuffer.Count)
			{
				long missionId = DueMissionBuffer[_annualIndex++];
				if (ByMissionId.TryGetValue(missionId, out XjYinSiMissionArchiveRecord mission)) AdvanceMission(mission, currentYear);
			}
			ClearPendingAnnualWork();
		}
	}

	internal static void ClearPendingAnnualWork()
	{
		PendingTargetBuffer.Clear();
		DueMissionBuffer.Clear();
		_annualPhase = AnnualPhase.None;
		_annualYear = 0;
		_annualIndex = 0;
	}

	internal static bool IsAuthorizedManifest(long manifestActorId, long missionId, long targetActorId)
	{
		if (manifestActorId <= 0L || missionId <= 0L || targetActorId <= 0L || !ByMissionId.TryGetValue(missionId, out XjYinSiMissionArchiveRecord mission)) return false;
		return mission.TargetActorId == targetActorId && !IsTerminal(mission.Stage) && mission.ManifestActorIds != null && mission.ManifestActorIds.Contains(manifestActorId);
	}

	internal static bool TryAuthorizeManifest(long manifestActorId, long missionId, long targetActorId)
	{
		if (manifestActorId <= 0L || missionId <= 0L || targetActorId <= 0L
			|| !ByMissionId.TryGetValue(missionId, out XjYinSiMissionArchiveRecord mission)
			|| mission.TargetActorId != targetActorId || IsTerminal(mission.Stage)) return false;
		mission.ManifestActorIds ??= new List<long>();
		if (!mission.ManifestActorIds.Contains(manifestActorId))
		{
			mission.ManifestActorIds.Add(manifestActorId);
			mission.RuntimeVersion++;
			MarkChanged();
		}
		MissionIdByManifestActor[manifestActorId] = missionId;
		return true;
	}

	internal static void RevokeManifestAuthorization(long manifestActorId, long missionId)
	{
		if (manifestActorId <= 0L || missionId <= 0L || !ByMissionId.TryGetValue(missionId, out XjYinSiMissionArchiveRecord mission)
			|| mission.ManifestActorIds == null || !mission.ManifestActorIds.Remove(manifestActorId)) return;
		RemoveManifestIndex(manifestActorId, missionId);
		mission.RuntimeVersion++;
		MarkChanged();
	}

	internal static void OnActorDied(Actor actor, int currentYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		PendingKnownTargetIds.Remove(actorId);
		DaoTaiGazeYearByActorId.Remove(actorId);
		if (ActiveMissionIdByTarget.TryGetValue(actorId, out long targetMissionId) && ByMissionId.TryGetValue(targetMissionId, out XjYinSiMissionArchiveRecord targetMission))
		{
			ResolveMission(targetMission, "目标已死亡，阴司追索完成", currentYear, true);
			return;
		}
		if (MissionIdByManifestActor.TryGetValue(actorId, out long missionId)
			&& ByMissionId.TryGetValue(missionId, out XjYinSiMissionArchiveRecord manifestMission))
		{
			HandleManifestActorDeath(manifestMission, actorId, currentYear);
		}
		else
		{
			// Compatibility fallback for an archive imported before the runtime
			// manifest index was rebuilt. This scans at most once for the death.
			foreach (XjYinSiMissionArchiveRecord mission in ByMissionId.Values)
			{
				if (mission?.ManifestActorIds == null || !mission.ManifestActorIds.Contains(actorId)) continue;
				HandleManifestActorDeath(mission, actorId, currentYear);
				break;
			}
		}
	}

	private static void HandleManifestActorDeath(XjYinSiMissionArchiveRecord mission, long manifestActorId, int currentYear)
	{
		if (mission?.ManifestActorIds == null || !mission.ManifestActorIds.Remove(manifestActorId))
		{
			RemoveManifestIndex(manifestActorId, mission?.MissionId ?? 0L);
			return;
		}

		RemoveManifestIndex(manifestActorId, mission.MissionId);
		if (string.Equals(mission.Stage, XjYinSiMissionStage.Pursuing, StringComparison.Ordinal))
		{
			mission.DefeatedManifestCount++;
		}
		mission.RuntimeVersion++;
		if (mission.DefeatedManifestCount >= MaxManifestPerMission
			&& string.Equals(mission.MissionType, XjYinSiMissionType.LivingJinDan, StringComparison.Ordinal))
		{
			ResetTargetAfterDefeatingYinSi(mission, currentYear);
		}
		else if (mission.ManifestActorIds.Count == 0 && string.Equals(mission.Stage, XjYinSiMissionStage.Pursuing, StringComparison.Ordinal))
		{
			mission.Stage = XjYinSiMissionStage.Evaded;
			mission.Resolution = "本轮阴司追索未应验";
			mission.NextActionYear = currentYear + TargetMissionCooldownYears;
			ActiveMissionIdByTarget.Remove(mission.TargetActorId);
			PendingKnownTargetIds.Add(mission.TargetActorId);
			if (XjJinDanImmortalityRegistry.TryGet(mission.TargetActorId, out XjJinDanImmortalityArchiveRecord targetRecord))
			{
				targetRecord.YinSiState = XjJinDanYinSiState.Evaded;
				targetRecord.LastKnownYear = currentYear;
			}
		}
		MarkChanged();
	}

	private static void ResetTargetAfterDefeatingYinSi(XjYinSiMissionArchiveRecord mission, int currentYear)
	{
		if (mission == null) return;
		mission.Stage = XjYinSiMissionStage.Resolved;
		mission.Resolution = "目标斩却本轮两名阴司，命痕暂归沉寂";
		mission.LastActionYear = currentYear;
		mission.NextActionYear = 0;
		mission.RuntimeVersion++;
		ActiveMissionIdByTarget.Remove(mission.TargetActorId);
		PendingKnownTargetIds.Remove(mission.TargetActorId);
		if (XjJinDanImmortalityRegistry.TryGet(mission.TargetActorId, out XjJinDanImmortalityArchiveRecord targetRecord)
			&& targetRecord != null)
		{
			targetRecord.YinSiExposure = 0f;
			targetRecord.YinSiKnown = false;
			targetRecord.YinSiState = XjJinDanYinSiState.Hidden;
			targetRecord.LastExposureReason = string.Empty;
			targetRecord.LastKnownYear = 0;
			targetRecord.LastExposureUpdateYear = currentYear;
		}
		XjDaoTaiMeritSystem.AwardForDefeatingYinSi(mission.TargetActorId, currentYear);
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			"阴司追索暂断",
			mission.TargetActorName + "连斩两名阴司，命痕沉入天地罅隙，现世值归零。",
			5,
			actorId: mission.TargetActorId,
			actorName: mission.TargetActorName,
			cityId: mission.LastKnownCityId,
			year: currentYear);
	}

	internal static bool TryGetActiveMissionForTarget(long actorId, out XjYinSiMissionArchiveRecord mission)
	{
		mission = null;
		return actorId > 0L && ActiveMissionIdByTarget.TryGetValue(actorId, out long missionId) && ByMissionId.TryGetValue(missionId, out mission);
	}

	internal static IReadOnlyList<XjYinSiMissionArchiveRecord> ReadAll()
	{
		List<XjYinSiMissionArchiveRecord> result = new List<XjYinSiMissionArchiveRecord>(ByMissionId.Count);
		foreach (XjYinSiMissionArchiveRecord mission in ByMissionId.Values) result.Add(Clone(mission));
		result.Sort((a, b) => a.MissionId.CompareTo(b.MissionId));
		return result;
	}

	internal static void ExportArchiveRecords(List<XjYinSiMissionArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		foreach (XjYinSiMissionArchiveRecord mission in ReadAll()) target.Add(mission);
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjYinSiMissionArchiveRecord> source)
	{
		Clear();
		if (source == null) return;
		for (int i = 0; i < source.Count; i++)
		{
			XjYinSiMissionArchiveRecord mission = source[i];
			if (mission == null || mission.MissionId <= 0L || mission.TargetActorId <= 0L || ByMissionId.ContainsKey(mission.MissionId)) continue;
			XjYinSiMissionArchiveRecord copy = Clone(mission);
			// runtime pending queue不入档；若恰在5~10秒追索等待期存档，
			// 读档后把哨兵恢复成下一年度可重建的 Pursuing 状态。
			if (string.Equals(copy.Stage, XjYinSiMissionStage.Pursuing, StringComparison.Ordinal)
				&& copy.NextActionYear == int.MaxValue)
			{
				copy.NextActionYear = Math.Max(1, copy.LastActionYear + 1);
			}
			ByMissionId[copy.MissionId] = copy;
			IndexMissionManifests(copy);
			if (!IsTerminal(copy.Stage) && !string.Equals(copy.Stage, XjYinSiMissionStage.Evaded, StringComparison.Ordinal)) ActiveMissionIdByTarget[copy.TargetActorId] = copy.MissionId;
			_lastMissionScheduledYear = Math.Max(_lastMissionScheduledYear, copy.DiscoveryYear);
		}
		PruneArchive();
		IReadOnlyList<XjJinDanImmortalityArchiveRecord> jinDan = XjJinDanImmortalityRegistry.ReadAll();
		for (int i = 0; i < jinDan.Count; i++)
		{
			XjJinDanImmortalityArchiveRecord record = jinDan[i];
			if (record != null && record.ActorId > 0L && record.IsAlive && record.YinSiKnown
				&& !ActiveMissionIdByTarget.ContainsKey(record.ActorId)) PendingKnownTargetIds.Add(record.ActorId);
		}
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.JinDan);
	}

	internal static void Clear()
	{
		ByMissionId.Clear();
		ActiveMissionIdByTarget.Clear();
		MissionIdByManifestActor.Clear();
		PendingKnownTargetIds.Clear();
		ClearPendingAnnualWork();
		PendingDirectPursuits.Clear();
		PendingDirectPursuitReadyIds.Clear();
		_nextDirectPursuitDueTime = float.PositiveInfinity;
		DaoTaiGazeYearByActorId.Clear();
		PendingTargetBuffer.Clear();
		DueMissionBuffer.Clear();
		TerminalMissionBuffer.Clear();
		ManifestEndBuffer.Clear();
		_lastMissionScheduledYear = -9999;
	}

	private static void TryTriggerDaoTaiGaze(Actor actor, XjJinDanImmortalityArchiveRecord record, string reason, int currentYear)
	{
		if (actor?.data == null || record == null || record.ActorId <= 0L || currentYear <= 0 || record.YinSiExposure <= 0f) return;
		if (DaoTaiGazeYearByActorId.TryGetValue(record.ActorId, out int handledYear)
			&& handledYear == currentYear) return;
		DaoTaiGazeYearByActorId[record.ActorId] = currentYear;
		if (XjDeterministicHash.Roll01(record.ActorId, currentYear, XjRealmIds.JinDan, "xj.daotai.gaze") >= DaoTaiGazeChance) return;

		string name = SafeActorName(actor);
		string suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : "，缘由：" + reason.Trim();
		XjDaoTaiGazeVisualSystem.Show(record.ActorId, currentYear);
		XjBroadcastSystem.ShowRecordedWorldTipCritical("【道胎垂眸】北天无声开眼，" + name + "命痕已被阴司照见。", false, "top", 8f, "#8FD7FF");
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.HighRealm, "道胎垂眸",
			"北天无声开眼，" + name + "在天地间留下命痕" + suffix + "。", 5,
			actorId: record.ActorId, actorName: name, cityId: actor.city?.data?.id ?? 0L, year: currentYear);
	}

	private static bool TryScheduleMission(Actor target, XjJinDanImmortalityArchiveRecord record, int currentYear)
	{
		if (target?.data == null || record == null || !record.YinSiKnown || !record.IsAlive || ActiveMissionIdByTarget.ContainsKey(record.ActorId)
			|| ActiveMissionIdByTarget.Count >= MaxActiveMissions || currentYear - _lastMissionScheduledYear < GlobalMissionCooldownYears) return false;
		long missionId = BuildMissionId(record.ActorId, record.PursuitCount + 1, currentYear);
		if (ByMissionId.ContainsKey(missionId)) return false;
		bool sheltered = IsSheltered(record.ActorId);
		XjYinSiMissionArchiveRecord mission = new XjYinSiMissionArchiveRecord
		{
			MissionId = missionId,
			TargetActorId = record.ActorId,
			TargetActorName = SafeActorName(target),
			DiscoveryYear = currentYear,
			Stage = XjYinSiMissionStage.Scheduled,
			EscalationLevel = Math.Clamp(record.PursuitCount + 1, 1, 5),
			NextActionYear = currentYear + (sheltered ? 8 : 3),
			LastKnownCityId = target.city?.data?.id ?? 0L,
			SecretRealmId = XjSecretRealmRegistry.IsSheltered(record.ActorId, out XjSecretRealmArchiveRecord realm) ? realm.RealmId : 0L
		};
		ByMissionId.Add(missionId, mission);
		ActiveMissionIdByTarget[record.ActorId] = missionId;
		PendingKnownTargetIds.Remove(record.ActorId);
		_lastMissionScheduledYear = currentYear;
		record.PursuitCount++;
		record.YinSiState = XjJinDanYinSiState.Known;
		record.LastKnownYear = currentYear;
		PruneArchive();
		MarkChanged();
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.HighRealm, "阴司追索将至",
			"两名阴司察觉" + mission.TargetActorName + "寿数已尽，却凭金性久存于世，近来频频显现，已为其金丹金性而来。", 5,
			actorId: record.ActorId, actorName: mission.TargetActorName, cityId: mission.LastKnownCityId, year: currentYear);
		return true;
	}

	private static void AdvanceMission(XjYinSiMissionArchiveRecord mission, int currentYear)
	{
		if (mission == null || IsTerminal(mission.Stage)) return;
		if (!XjScheduler.ResolveActor(mission.TargetActorId, out Actor target) || target?.data == null || !target.isAlive())
		{
			ResolveMission(mission, "目标已不存在", currentYear, true);
			return;
		}
		if (currentYear - mission.DiscoveryYear >= MaxMissionRuntimeYears)
		{
			ResolveMission(mission, "追索期限已尽，阴司追索消退", currentYear, true);
			return;
		}
		bool sheltered = IsSheltered(mission.TargetActorId);
		if (string.Equals(mission.Stage, XjYinSiMissionStage.Scheduled, StringComparison.Ordinal))
		{
			mission.Stage = XjYinSiMissionStage.Locating;
			mission.NextActionYear = currentYear + (sheltered ? 10 : 3);
			mission.LastActionYear = currentYear;
			SetTargetState(mission.TargetActorId, XjJinDanYinSiState.Locating, currentYear);
		}
		else if (string.Equals(mission.Stage, XjYinSiMissionStage.Locating, StringComparison.Ordinal))
		{
			mission.Stage = XjYinSiMissionStage.Manifesting;
			mission.NextActionYear = currentYear;
			mission.LastActionYear = currentYear;
		}
		else if (string.Equals(mission.Stage, XjYinSiMissionStage.Manifesting, StringComparison.Ordinal))
		{
			ResolveDirectPursuit(mission, target, currentYear, sheltered);
		}
		else if (string.Equals(mission.Stage, XjYinSiMissionStage.Pursuing, StringComparison.Ordinal))
		{
			ResolveDirectPursuit(mission, target, currentYear, sheltered);
		}
		mission.RuntimeVersion++;
		MarkChanged();
	}

	private static void ResolveDirectPursuit(XjYinSiMissionArchiveRecord mission, Actor target, int currentYear, bool sheltered)
	{
		if (mission == null || target?.data == null) return;
		if (PendingDirectPursuits.ContainsKey(mission.MissionId))
		{
			return;
		}

		EndManifests(mission);
		mission.Stage = XjYinSiMissionStage.Pursuing;
		mission.LastActionYear = currentYear;
		mission.NextActionYear = currentYear + 1;
		SetTargetState(mission.TargetActorId, XjJinDanYinSiState.Pursuing, currentYear);

		if (!XjJinDanImmortalityRegistry.TryGet(mission.TargetActorId, out XjJinDanImmortalityArchiveRecord record)
			|| record == null || !record.IsAlive)
		{
			ResolveMission(mission, "目标已不在阴司名录中", currentYear, false);
			return;
		}

		float killChance = ResolveDirectPursuitKillChance(record, sheltered);
		float roll = XjDeterministicHash.Roll01(mission.TargetActorId, currentYear, mission.MissionId.ToString(CultureInfo.InvariantCulture), "xj.yinsi.direct_pursuit");
		float delay = ResolveDirectPursuitDelay(mission.MissionId, mission.TargetActorId);
		float dueTime = UnityEngine.Time.unscaledTime + delay;
		PendingDirectPursuits[mission.MissionId] = new PendingDirectPursuitState(
			mission.TargetActorId,
			currentYear,
			killChance,
			roll,
			dueTime);
		if (dueTime < _nextDirectPursuitDueTime) _nextDirectPursuitDueTime = dueTime;
		// 实时等待期间不再被年度 TickYear 反复推进/MarkChanged；
		// 否则40x下5~10个真实秒会产生数十次无意义任务写回。
		mission.NextActionYear = int.MaxValue;

		string pendingText = "两名阴司察觉" + mission.TargetActorName + "寿数已尽，却凭金性久存于世，近来频频显现，今为其金丹金性而来。";
		XjBroadcastSystem.BroadcastSLevelWorldEvent(pendingText, pendingText, "#73506E", 8f, XjEventIconCatalog.YinSiAppear);
	}

	/// <summary>
	/// 活金丹阴司追索使用未缩放时间 + 有界 background.yinsi 预算。
	/// 高倍速不再把多个 5~10 秒 Coroutine 回调挤到同一帧。
	/// </summary>
	internal static int TickPendingDirectPursuits(int budget = 1)
	{
		if (budget <= 0 || PendingDirectPursuits.Count == 0) return 0;

		float now = UnityEngine.Time.unscaledTime;
		PendingDirectPursuitReadyIds.Clear();
		foreach (KeyValuePair<long, PendingDirectPursuitState> pair in PendingDirectPursuits)
		{
			if (pair.Value.DueTime > now) continue;
			PendingDirectPursuitReadyIds.Add(pair.Key);
			if (PendingDirectPursuitReadyIds.Count >= budget) break;
		}

		int processed = 0;
		for (int i = 0; i < PendingDirectPursuitReadyIds.Count; i++)
		{
			long missionId = PendingDirectPursuitReadyIds[i];
			if (!PendingDirectPursuits.TryGetValue(missionId, out PendingDirectPursuitState state)) continue;
			PendingDirectPursuits.Remove(missionId);
			processed++;
			ExecuteDelayedDirectPursuit(missionId, state.TargetActorId, state.CurrentYear, state.KillChance, state.Roll);
		}
		PendingDirectPursuitReadyIds.Clear();
		RecomputeNextDirectPursuitDueTime();
		return processed;
	}

	private static void RecomputeNextDirectPursuitDueTime()
	{
		float next = float.PositiveInfinity;
		foreach (PendingDirectPursuitState state in PendingDirectPursuits.Values)
		{
			if (state.DueTime < next) next = state.DueTime;
		}
		_nextDirectPursuitDueTime = next;
	}

	private static void ExecuteDelayedDirectPursuit(long missionId, long targetActorId, int currentYear, float killChance, float roll)
	{
		if (!ByMissionId.TryGetValue(missionId, out XjYinSiMissionArchiveRecord mission)
			|| mission == null || IsTerminal(mission.Stage))
		{
			return;
		}

		if (!XjScheduler.ResolveActor(targetActorId, out Actor target) || target?.data == null || !target.isAlive())
		{
			ResolveMission(mission, "目标已不存在", currentYear, false);
			return;
		}

		if (roll < killChance && TryExecuteDirectPursuitDeath(target, mission, currentYear, killChance))
		{
			mission.Stage = XjYinSiMissionStage.Resolved;
			mission.Resolution = "阴司追索应验，目标已亡";
			mission.NextActionYear = 0;
			ActiveMissionIdByTarget.Remove(mission.TargetActorId);
			PendingKnownTargetIds.Remove(mission.TargetActorId);
			MarkChanged();
			return;
		}

		mission.Stage = XjYinSiMissionStage.Evaded;
		mission.Resolution = "金丹久存于世，阴司追索未能得手";
		mission.NextActionYear = currentYear + TargetMissionCooldownYears;
		ActiveMissionIdByTarget.Remove(mission.TargetActorId);
		PendingKnownTargetIds.Add(mission.TargetActorId);
		SetTargetState(mission.TargetActorId, XjJinDanYinSiState.Evaded, currentYear);
		string targetName = string.IsNullOrWhiteSpace(mission.TargetActorName) ? SafeActorName(target) : mission.TargetActorName;
		string retreatText = "【阴司退去】" + targetName + "凭金性久存于世，道行太盛，两名阴司追索不成，暂退幽冥。";
		XjBroadcastSystem.ShowRecordedWorldTipCritical(retreatText, false, "top", 7f, "#73506E", iconId: XjEventIconCatalog.YinSiLeave);
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.HighRealm, "阴司追索未应",
			targetName + "凭金性久存于世，道行太盛，两名阴司追索不成，暂退幽冥。", 4,
			actorId: mission.TargetActorId, actorName: targetName, cityId: target.city?.data?.id ?? 0L, year: currentYear);
		MarkChanged();
	}

	private static float ResolveDirectPursuitKillChance(XjJinDanImmortalityArchiveRecord record, bool sheltered)
	{
		if (record == null) return 0f;
		float exposure = Math.Max(0f, record.YinSiExposure);
		float chance = Math.Min(MaxDirectPursuitKillChance, exposure / DirectPursuitChanceScale);
		if (sheltered) chance *= 0.5f;
		return Math.Clamp(chance, 0f, MaxDirectPursuitKillChance);
	}

	private static float ResolveDirectPursuitDelay(long missionId, long targetActorId)
	{
		long seed = missionId > 0L ? missionId : targetActorId;
		int hundredths = XjDeterministicHash.PositiveIndex(seed, "xj.yinsi.direct_pursuit.delay", 501);
		return MinDirectPursuitDelaySeconds
			+ (MaxDirectPursuitDelaySeconds - MinDirectPursuitDelaySeconds) * hundredths / 500f;
	}

	private static bool TryExecuteDirectPursuitDeath(Actor target, XjYinSiMissionArchiveRecord mission, int currentYear, float killChance)
	{
		if (!XjSafeCore.IsAliveActor(target) || target?.data == null || mission == null) return false;
		string targetName = mission.TargetActorName;
		if (string.IsNullOrWhiteSpace(targetName)) targetName = SafeActorName(target);
		try
		{
			XjActorAccessor.SetString(target, XjActorDataKeys.XjDeathAnnouncementReason, "YinSiPursuit");
			bool died = XjVanillaDeathGuard.TryExecuteForceDeath(target, (AttackType)YinSiDirectPursuitAttackType, false, XuanJianVNext.Systems.Death.XjDeathCause.YinSi);
			if (!died)
			{
				XjActorAccessor.SetString(target, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
				return false;
			}
			string text = "两名阴司察觉" + targetName + "寿数已尽，却凭金性久存于世，近来频频显现，今为其金丹金性而来，追索应验，归入幽冥。";
			XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.HighRealm, "阴司追索应验",
				text, 5,
				actorId: mission.TargetActorId, actorName: targetName, cityId: mission.LastKnownCityId, year: currentYear);
			XjBroadcastSystem.ShowRecordedWorldTipCritical(text, false, "top", 8f, "#73506E", iconId: XjEventIconCatalog.YinSiLeave);
			return true;
		}
		catch
		{
			try { XjActorAccessor.SetString(target, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty); } catch (System.Exception xjCaught543) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjYinSiExposurePursuitSystem.cs:543", xjCaught543); }
			return false;
		}
	}

	private static void ResolveMission(XjYinSiMissionArchiveRecord mission, string resolution, int currentYear, bool endManifests)
	{
		if (mission == null) return;
		if (PendingDirectPursuits.Remove(mission.MissionId))
		{
			RecomputeNextDirectPursuitDueTime();
		}
		mission.Stage = XjYinSiMissionStage.Resolved;
		mission.Resolution = resolution ?? string.Empty;
		mission.LastActionYear = currentYear;
		mission.NextActionYear = 0;
		mission.RuntimeVersion++;
		ActiveMissionIdByTarget.Remove(mission.TargetActorId);
		PendingKnownTargetIds.Remove(mission.TargetActorId);
		if (endManifests) EndManifests(mission);
		PruneArchive();
		MarkChanged();
	}


	private static void EndManifests(XjYinSiMissionArchiveRecord mission)
	{
		if (mission?.ManifestActorIds == null || mission.ManifestActorIds.Count == 0) return;
		ManifestEndBuffer.Clear();
		ManifestEndBuffer.AddRange(mission.ManifestActorIds);
		mission.ManifestActorIds.Clear();
		for (int i = 0; i < ManifestEndBuffer.Count; i++)
		{
			long manifestId = ManifestEndBuffer[i];
			RemoveManifestIndex(manifestId, mission.MissionId);
			if (XjScheduler.ResolveActor(manifestId, out Actor manifest)) XjYinSiTraitLifecycle.EndMissionManifest(manifest);
		}
		ManifestEndBuffer.Clear();
	}

	private static void PruneArchive()
	{
		if (ByMissionId.Count <= MaxArchivedMissions) return;
		TerminalMissionBuffer.Clear();
		foreach (XjYinSiMissionArchiveRecord mission in ByMissionId.Values)
		{
			if (mission != null && IsTerminal(mission.Stage)) TerminalMissionBuffer.Add(mission);
		}
		TerminalMissionBuffer.Sort((a, b) => a.DiscoveryYear != b.DiscoveryYear ? a.DiscoveryYear.CompareTo(b.DiscoveryYear) : a.MissionId.CompareTo(b.MissionId));
		int removeCount = Math.Min(TerminalMissionBuffer.Count, ByMissionId.Count - MaxArchivedMissions);
		for (int i = 0; i < removeCount; i++)
		{
			XjYinSiMissionArchiveRecord mission = TerminalMissionBuffer[i];
			RemoveMissionManifestIndexes(mission);
			ByMissionId.Remove(mission.MissionId);
		}
		TerminalMissionBuffer.Clear();
	}

	private static void IndexMissionManifests(XjYinSiMissionArchiveRecord mission)
	{
		if (mission?.ManifestActorIds == null) return;
		for (int i = 0; i < mission.ManifestActorIds.Count; i++)
		{
			long manifestId = mission.ManifestActorIds[i];
			if (manifestId > 0L) MissionIdByManifestActor[manifestId] = mission.MissionId;
		}
	}

	private static void RemoveMissionManifestIndexes(XjYinSiMissionArchiveRecord mission)
	{
		if (mission?.ManifestActorIds == null) return;
		for (int i = 0; i < mission.ManifestActorIds.Count; i++)
		{
			RemoveManifestIndex(mission.ManifestActorIds[i], mission.MissionId);
		}
	}

	private static void RemoveManifestIndex(long manifestActorId, long missionId)
	{
		if (manifestActorId > 0L
			&& MissionIdByManifestActor.TryGetValue(manifestActorId, out long indexedMissionId)
			&& (missionId <= 0L || indexedMissionId == missionId))
		{
			MissionIdByManifestActor.Remove(manifestActorId);
		}
	}

	private static bool ApplyLazyDecay(XjJinDanImmortalityArchiveRecord record, int currentYear, bool sheltered)
	{
		if (record == null || currentYear <= record.LastExposureUpdateYear) return false;
		float beforeExposure = record.YinSiExposure;
		string beforeState = record.YinSiState;
		int delta = currentYear - Math.Max(0, record.LastExposureUpdateYear);
		float decay = delta * (sheltered ? ShelteredDecayPerYear : BaseDecayPerYear);
		record.YinSiExposure = Math.Max(0f, record.YinSiExposure - decay);
		record.LastExposureUpdateYear = currentYear;
		if (!record.YinSiKnown) record.YinSiState = ResolveUnknownState(record.YinSiExposure);
		return Math.Abs(record.YinSiExposure - beforeExposure) > 0.0001f || !string.Equals(beforeState, record.YinSiState, StringComparison.Ordinal);
	}

	private static string ResolveUnknownState(float exposure)
	{
		if (exposure >= NearDiscoveryThreshold) return XjJinDanYinSiState.NearDiscovery;
		if (exposure >= TracedThreshold) return XjJinDanYinSiState.Traced;
		return XjJinDanYinSiState.Hidden;
	}

	private static bool IsSheltered(long actorId)
	{
		if (XjSecretRealmRegistry.IsSheltered(actorId, out _)) return true;
		if (XjScheduler.ResolveActor(actorId, out Actor actor))
		{
			try { return XuanJianVNext.Systems.DongTian.XjSectDongTianLifecycle.IsRetreatActive(actor); } catch (System.Exception xjCaught666) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjYinSiExposurePursuitSystem.cs:666", xjCaught666); }
		}
		return false;
	}

	private static void SetTargetState(long actorId, string state, int currentYear)
	{
		if (!XjJinDanImmortalityRegistry.TryGet(actorId, out XjJinDanImmortalityArchiveRecord record)) return;
		record.YinSiKnown = true;
		record.YinSiState = state;
		record.LastKnownYear = Math.Max(record.LastKnownYear, currentYear);
	}

	private static void WriteDiscoveryHistory(Actor actor, string reason, int currentYear)
	{
		string name = SafeActorName(actor);
		string suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : "，缘由：" + reason.Trim();
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.HighRealm, "阴司知名",
			name + "在天地间留下足以被阴司确认的痕迹" + suffix + "。", 5,
			actorId: ((BaseSystemData)actor.data).id, actorName: name, cityId: actor.city?.data?.id ?? 0L, year: currentYear);
	}

	private static long BuildMissionId(long actorId, int ordinal, int currentYear)
	{
		long id = XjDeterministicHash.PositiveHash(actorId, "xuanjian.yinsi.mission.v1|" + ordinal.ToString(CultureInfo.InvariantCulture) + "|" + currentYear.ToString(CultureInfo.InvariantCulture));
		return id <= 0L ? actorId + Math.Max(1, ordinal) : id;
	}

	private static bool IsTerminal(string stage) => string.Equals(stage, XjYinSiMissionStage.Resolved, StringComparison.Ordinal)
		|| string.Equals(stage, XjYinSiMissionStage.Cancelled, StringComparison.Ordinal)
		|| string.Equals(stage, XjYinSiMissionStage.Evaded, StringComparison.Ordinal);

	private static string SafeActorName(Actor actor)
	{
		try { return actor?.getName() ?? "未名真君"; } catch (System.Exception xjCaught700_4) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjYinSiExposurePursuitSystem.cs:700", xjCaught700_4);
			 return "未名真君"; }
	}

	private static void MarkExposureChanged()
	{
		XjJinDanImmortalityRegistry.NotifyRecordChanged();
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.JinDan);
	}

	private static void MarkChanged()
	{
		// MarkChanged covers both mission mutations and target JinDan record mutations.
		// Extra invalidation for mission-only changes is cheap and keeps live-record writes
		// from escaping the versioned JinDan read snapshot.
		XjJinDanImmortalityRegistry.NotifyRecordChanged();
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.JinDan | XjCodexDirtyFlags.History);
	}

	private static XjYinSiMissionArchiveRecord Clone(XjYinSiMissionArchiveRecord source)
	{
		return new XjYinSiMissionArchiveRecord
		{
			SchemaVersion = source.SchemaVersion, MissionId = source.MissionId, MissionType = source.MissionType ?? XjYinSiMissionType.LivingJinDan,
			TargetActorId = source.TargetActorId, TargetActorName = source.TargetActorName ?? string.Empty,
			DiscoveryYear = source.DiscoveryYear, Stage = source.Stage ?? XjYinSiMissionStage.Scheduled,
			EscalationLevel = Math.Max(0, source.EscalationLevel), NextActionYear = source.NextActionYear,
			LastActionYear = source.LastActionYear, LastKnownCityId = source.LastKnownCityId, SecretRealmId = source.SecretRealmId,
			ManifestActorIds = source.ManifestActorIds == null ? new List<long>() : new List<long>(source.ManifestActorIds),
			DefeatedManifestCount = Math.Max(0, source.DefeatedManifestCount),
			Resolution = source.Resolution ?? string.Empty, RuntimeVersion = source.RuntimeVersion
		};
	}
}
