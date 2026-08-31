using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.DongTian;

internal static class XjSecretRealmRegistry
{
	private static readonly Dictionary<long, XjSecretRealmArchiveRecord> ByRealmId = new Dictionary<long, XjSecretRealmArchiveRecord>();
	private static readonly Dictionary<long, long> RealmIdBySectId = new Dictionary<long, long>();
	private static IReadOnlyList<XjSecretRealmArchiveRecord> CachedReadAll = Array.Empty<XjSecretRealmArchiveRecord>();
	private static int _readRevision = 1;
	private static int _cachedReadRevision;

	internal static int Count => ByRealmId.Count;
	internal static bool TryGetByRealmId(long realmId, out XjSecretRealmArchiveRecord record) => ByRealmId.TryGetValue(realmId, out record);
	internal static bool TryGetBySectId(long sectId, out XjSecretRealmArchiveRecord record)
	{
		record = null;
		return sectId > 0L && RealmIdBySectId.TryGetValue(sectId, out long realmId) && ByRealmId.TryGetValue(realmId, out record);
	}

	internal static XjSecretRealmArchiveRecord EnsureForSect(long sectId, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)) return null;
		if (TryGetBySectId(sectId, out XjSecretRealmArchiveRecord existing)) return existing;
		long realmId = BuildRealmId(sectId);
		while (ByRealmId.ContainsKey(realmId)) realmId++;
		XjSecretRealmArchiveRecord created = new XjSecretRealmArchiveRecord
		{
			RealmId = realmId,
			SectId = sectId,
			DisplayName = string.IsNullOrWhiteSpace(sect.Name) ? "玄韬秘境" : sect.Name + "玄韬秘境",
			EntranceCityId = sect.CapitalCityId,
			EntranceCityName = ResolveCityName(sect.CapitalCityId),
			StageStartedYear = Math.Max(0, currentYear),
			LastMaintainedYear = Math.Max(0, currentYear)
		};
		ByRealmId.Add(realmId, created);
		RealmIdBySectId[sectId] = realmId;
		MarkChanged();
		return created;
	}

	internal static bool TryGrantConstructionMethod(long sectId, string source, int currentYear)
	{
		XjSecretRealmArchiveRecord record = EnsureForSect(sectId, currentYear);
		if (record == null) return false;
		if (record.ConstructionMethodKnown) return false;
		record.ConstructionMethodKnown = true;
		record.ConstructionMethodSource = string.IsNullOrWhiteSpace(source) ? "古修遗留" : source.Trim();
		record.MethodAcquiredYear = Math.Max(0, currentYear);
		record.RuntimeVersion++;
		XjSectRepository.TryBindSecretRealm(sectId, record.RealmId, XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) ? sect.Status : XjSectStatus.SectRegime);
		MarkChanged();
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.SecretRealm, "洞天营造之法入宗",
			(record.DisplayName ?? "某宗秘境") + "所属宗门获得洞天营造之法，始具营造玄韬与福地之资格。", 4,
			sectId: sectId, cityId: record.EntranceCityId, year: currentYear);
		return true;
	}

	internal static bool TryBeginStage(long sectId, string stage, long actorId, long taskId, int startYear, int dueYear)
	{
		if (!TryGetBySectId(sectId, out XjSecretRealmArchiveRecord record) || record.ActiveTaskId > 0L || actorId <= 0L || taskId <= 0L) return false;
		record.Stage = stage ?? XjSecretRealmStage.None;
		if (XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)) XjSectRepository.TryBindSecretRealm(sectId, record.RealmId, sect.Status);
		record.HasRetainedStageProgress = false;
		record.LeadFormationMasterId = actorId;
		record.ActiveTaskId = taskId;
		record.StageStartedYear = Math.Max(0, startYear);
		record.StageDueYear = Math.Max(record.StageStartedYear, dueYear);
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	internal static bool TryResumeStage(long sectId, long actorId, long taskId, int startYear, int dueYear)
	{
		if (!TryGetBySectId(sectId, out XjSecretRealmArchiveRecord record) || record.ActiveTaskId > 0L
			|| !record.HasRetainedStageProgress || actorId <= 0L || taskId <= 0L) return false;
		record.HasRetainedStageProgress = false;
		record.LeadFormationMasterId = actorId;
		record.ActiveTaskId = taskId;
		record.StageStartedYear = Math.Max(0, startYear);
		record.StageDueYear = Math.Max(record.StageStartedYear, dueYear);
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	internal static bool TryDelayStage(long sectId, long taskId, int dueYear)
	{
		if (!TryGetBySectId(sectId, out XjSecretRealmArchiveRecord record) || record.ActiveTaskId != taskId) return false;
		record.StageDueYear = Math.Max(record.StageDueYear, dueYear);
		record.FailureCount++;
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	internal static bool TryCompleteStage(long sectId, long taskId, string completedStage, int qualityPenalty, int currentYear)
	{
		if (!TryGetBySectId(sectId, out XjSecretRealmArchiveRecord record) || record.ActiveTaskId != taskId) return false;
		record.ActiveTaskId = 0L;
		record.LeadFormationMasterId = 0L;
		record.HasRetainedStageProgress = false;
		record.StageDueYear = 0;
		record.LastMaintainedYear = Math.Max(record.LastMaintainedYear, currentYear);
		int penalty = Math.Clamp(qualityPenalty, 0, 30);
		switch (completedStage)
		{
			case XjSecretRealmStage.SurveyingVoid:
				record.Stage = XjSecretRealmStage.LayingXuanTao;
				record.Stability = Math.Max(record.Stability, 10 - penalty / 3);
				break;
			case XjSecretRealmStage.LayingXuanTao:
				record.Stage = XjSecretRealmStage.SuppressingTreasure;
				record.XuanTaoIntegrity = Math.Max(record.XuanTaoIntegrity, 35 - penalty);
				break;
			case XjSecretRealmStage.SuppressingTreasure:
				record.Stage = XjSecretRealmStage.NourishingSpace;
				record.SuppressingTreasureName = "镇压灵宝";
				record.Stability = Math.Max(record.Stability, 35 - penalty);
				break;
			case XjSecretRealmStage.NourishingSpace:
				record.Stage = XjSecretRealmStage.StabilizingEntrance;
				record.XuanTaoIntegrity = Math.Max(record.XuanTaoIntegrity, 65 - penalty);
				record.Stability = Math.Max(record.Stability, 60 - penalty);
				break;
			case XjSecretRealmStage.StabilizingEntrance:
				record.Stage = XjSecretRealmStage.Fudi;
				record.XuanTaoIntegrity = Math.Max(record.XuanTaoIntegrity, 80 - penalty);
				record.Stability = Math.Max(record.Stability, 75 - penalty);
				record.Capacity = Math.Max(record.Capacity, 12);
				record.EntranceOpen = true;
				XjSectRepository.TryBindSecretRealm(sectId, record.RealmId, XjSectStatus.FudiSect);
				RecordEntranceOpened(record, "福地洞开", "福地入口初定，宗门可在其中讲法、坐镇与避劫。", 4, currentYear);
				break;
			case XjSecretRealmStage.UpgradingDongtian:
				record.Stage = XjSecretRealmStage.Dongtian;
				record.XuanTaoIntegrity = Math.Max(record.XuanTaoIntegrity, 95 - penalty);
				record.Stability = Math.Max(record.Stability, 90 - penalty);
				record.Capacity = Math.Max(record.Capacity, 30);
				record.EntranceOpen = true;
				XjSectRepository.TryBindSecretRealm(sectId, record.RealmId, XjSectStatus.DongtianSect);
				RecordEntranceOpened(record, "洞天升格", "福地玄韬升格为洞天，承载之数与避劫之力皆大增。", 5, currentYear);
				break;
		}
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	private static void RecordEntranceOpened(XjSecretRealmArchiveRecord record, string title, string effect, int importance, int currentYear)
	{
		if (record == null) return;
		string name = string.IsNullOrWhiteSpace(record.DisplayName) ? "宗门秘境" : record.DisplayName.Trim();
		string summary = name + "入口开放，容量" + Math.Max(0, record.Capacity).ToString(CultureInfo.InvariantCulture)
			+ "席，稳定" + Math.Clamp(record.Stability, 0, 100).ToString(CultureInfo.InvariantCulture)
			+ "，玄韬" + Math.Clamp(record.XuanTaoIntegrity, 0, 100).ToString(CultureInfo.InvariantCulture)
			+ "。" + effect;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.SecretRealm,
			title,
			summary,
			importance,
			true,
			sectId: record.SectId,
			cityId: record.EntranceCityId,
			year: Math.Max(0, currentYear));
	}

	internal static bool TryAssignSittingJinDan(long sectId, long actorId, int currentYear)
	{
		if (!TryGetBySectId(sectId, out XjSecretRealmArchiveRecord record) || actorId <= 0L) return false;
		if (!XjScheduler.ResolveActor(actorId, out Actor actor)
			|| actor?.data == null
			|| !actor.isAlive()
			|| XjSectRepository.ResolveActorSectId(actor) != sectId)
		{
			return false;
		}
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!XjHighRealmIdentity.IsZhenJun(realmId)) return false;
		if (record.SittingJinDanActorId == actorId) return false;
		record.SittingJinDanActorId = actorId;
		record.LastMaintainedYear = Math.Max(record.LastMaintainedYear, currentYear);
		if (string.Equals(record.Stage, XjSecretRealmStage.Dormant, StringComparison.Ordinal)) record.Stage = XjSecretRealmStage.Dongtian;
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	/// <summary>
	/// AVBS 等换身不会制造一次真实死亡；秘境中的当前主持者/坐关金丹应原位换键。
	/// </summary>
	internal static bool RebindActorAfterExternalReplacement(long oldActorId, long newActorId)
	{
		if (oldActorId <= 0L || newActorId <= 0L || oldActorId == newActorId) return false;
		bool changed = false;
		foreach (XjSecretRealmArchiveRecord record in ByRealmId.Values)
		{
			if (record == null) continue;
			bool recordChanged = false;
			if (record.LeadFormationMasterId == oldActorId)
			{
				record.LeadFormationMasterId = newActorId;
				recordChanged = true;
			}
			if (record.SittingJinDanActorId == oldActorId)
			{
				record.SittingJinDanActorId = newActorId;
				recordChanged = true;
			}
			if (!recordChanged) continue;
			record.RuntimeVersion++;
			changed = true;
		}
		if (changed) MarkChanged();
		return changed;
	}

	internal static void OnActorUnavailable(long actorId, int currentYear)
	{
		// This owner only clears current references/task leases; it writes no death history.
		OnActorDied(actorId, currentYear);
	}

	internal static void OnActorDied(long actorId, int currentYear)
	{
		if (actorId <= 0L) return;
		bool changed = false;
		foreach (XjSecretRealmArchiveRecord record in ByRealmId.Values)
		{
			if (record.LeadFormationMasterId == actorId)
			{
				record.LeadFormationMasterId = 0L;
				if (record.ActiveTaskId > 0L)
				{
					record.ActiveTaskId = 0L;
					record.StageDueYear = 0;
					record.HasRetainedStageProgress = true;
				}
				record.RuntimeVersion++;
				changed = true;
			}
			if (record.SittingJinDanActorId == actorId)
			{
				record.SittingJinDanActorId = 0L;
				if (string.Equals(record.Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal)) record.Stage = XjSecretRealmStage.Dormant;
				record.LastMaintainedYear = Math.Max(record.LastMaintainedYear, currentYear);
				record.RuntimeVersion++;
				changed = true;
			}
		}
		if (changed) MarkChanged();
	}

	internal static void OnActorLostZhenJunStatus(long actorId, int currentYear)
	{
		if (actorId <= 0L) return;
		bool changed = false;
		foreach (XjSecretRealmArchiveRecord record in ByRealmId.Values)
		{
			if (record.SittingJinDanActorId != actorId) continue;
			record.SittingJinDanActorId = 0L;
			if (string.Equals(record.Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal))
				record.Stage = XjSecretRealmStage.Dormant;
			record.LastMaintainedYear = Math.Max(record.LastMaintainedYear, currentYear);
			record.RuntimeVersion++;
			changed = true;
		}
		if (changed) MarkChanged();
	}

	internal static bool IsSheltered(long actorId, out XjSecretRealmArchiveRecord realm)
	{
		realm = null;
		if (actorId <= 0L) return false;
		foreach (XjSecretRealmArchiveRecord record in ByRealmId.Values)
		{
			if (record.SittingJinDanActorId == actorId && record.EntranceOpen
				&& (string.Equals(record.Stage, XjSecretRealmStage.Fudi, StringComparison.Ordinal)
				|| string.Equals(record.Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal)
				|| string.Equals(record.Stage, XjSecretRealmStage.Dormant, StringComparison.Ordinal)))
			{
				realm = record;
				return true;
			}
		}
		return false;
	}

	internal static IReadOnlyList<XjSecretRealmArchiveRecord> ReadAll()
	{
		if (_cachedReadRevision == _readRevision) return CachedReadAll;
		if (ByRealmId.Count == 0)
		{
			CachedReadAll = Array.Empty<XjSecretRealmArchiveRecord>();
			_cachedReadRevision = _readRevision;
			return CachedReadAll;
		}

		XjSecretRealmArchiveRecord[] result = new XjSecretRealmArchiveRecord[ByRealmId.Count];
		int index = 0;
		foreach (XjSecretRealmArchiveRecord record in ByRealmId.Values)
		{
			if (record != null) result[index++] = Clone(record);
		}
		if (index != result.Length) Array.Resize(ref result, index);
		Array.Sort(result, (a, b) => a.RealmId.CompareTo(b.RealmId));
		CachedReadAll = result.Length == 0 ? Array.Empty<XjSecretRealmArchiveRecord>() : Array.AsReadOnly(result);
		_cachedReadRevision = _readRevision;
		return CachedReadAll;
	}

	internal static void ExportArchiveRecords(List<XjSecretRealmArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		foreach (XjSecretRealmArchiveRecord record in ReadAll()) target.Add(record);
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjSecretRealmArchiveRecord> source)
	{
		Clear();
		if (source == null) return;
		for (int i = 0; i < source.Count; i++)
		{
			XjSecretRealmArchiveRecord record = source[i];
			if (record == null || record.RealmId <= 0L || record.SectId <= 0L || RealmIdBySectId.ContainsKey(record.SectId)) continue;
			XjSecretRealmArchiveRecord copy = Clone(record);
			ByRealmId[copy.RealmId] = copy;
			RealmIdBySectId[copy.SectId] = copy.RealmId;
		}
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.SecretRealm | XjCodexDirtyFlags.Sect);
	}

	internal static void ReconcileTaskLinksAfterLoad()
	{
		bool changed = false;
		foreach (XjSecretRealmArchiveRecord record in ByRealmId.Values)
		{
			if (record.ActiveTaskId <= 0L) continue;
			if (!XuanJianVNext.Systems.Craft.XjCraftDomainRegistry.TryGetTaskById(record.ActiveTaskId, out var task) || task == null || !task.IsOpen)
			{
				record.ActiveTaskId = 0L;
				record.LeadFormationMasterId = 0L;
				record.StageDueYear = 0;
				record.HasRetainedStageProgress = true;
				record.RuntimeVersion++;
				changed = true;
			}
		}
		if (changed) MarkChanged();
	}

	internal static void Clear()
	{
		ByRealmId.Clear();
		RealmIdBySectId.Clear();
		CachedReadAll = Array.Empty<XjSecretRealmArchiveRecord>();
		BumpReadRevision();
		_cachedReadRevision = 0;
	}

	private static long BuildRealmId(long sectId)
	{
		long id = XjDeterministicHash.PositiveHash(sectId, "xuanjian.secret_realm.v1");
		return id <= 0L ? sectId : id;
	}

	private static string ResolveCityName(long cityId)
	{
		if (!XjWorldLookupIndex.TryResolveCity(cityId, out City city) || city?.data == null) return string.Empty;
		try { return city.data.name ?? string.Empty; } catch { return string.Empty; }
	}

	private static void MarkChanged()
	{
		BumpReadRevision();
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.SecretRealm | XjCodexDirtyFlags.Sect);
	}

	private static void BumpReadRevision()
	{
		unchecked
		{
			_readRevision++;
			if (_readRevision <= 0) _readRevision = 1;
		}
	}

	private static XjSecretRealmArchiveRecord Clone(XjSecretRealmArchiveRecord source)
	{
		return new XjSecretRealmArchiveRecord
		{
			SchemaVersion = source.SchemaVersion, RealmId = source.RealmId, SectId = source.SectId,
			DisplayName = source.DisplayName ?? string.Empty, Stage = source.Stage ?? XjSecretRealmStage.None,
			ConstructionMethodKnown = source.ConstructionMethodKnown, ConstructionMethodSource = source.ConstructionMethodSource ?? string.Empty,
			MethodAcquiredYear = source.MethodAcquiredYear, EntranceCityId = source.EntranceCityId,
			EntranceCityName = source.EntranceCityName ?? string.Empty, Stability = Math.Clamp(source.Stability, 0, 100),
			XuanTaoIntegrity = Math.Clamp(source.XuanTaoIntegrity, 0, 100), Capacity = Math.Max(0, source.Capacity),
			SuppressingTreasureName = source.SuppressingTreasureName ?? string.Empty,
			LeadFormationMasterId = source.LeadFormationMasterId, SittingJinDanActorId = source.SittingJinDanActorId,
			ActiveTaskId = source.ActiveTaskId, StageStartedYear = source.StageStartedYear, StageDueYear = source.StageDueYear,
			LastMaintainedYear = source.LastMaintainedYear, FailureCount = Math.Max(0, source.FailureCount), HasRetainedStageProgress = source.HasRetainedStageProgress,
			EntranceOpen = source.EntranceOpen, RuntimeVersion = source.RuntimeVersion
		};
	}
}
