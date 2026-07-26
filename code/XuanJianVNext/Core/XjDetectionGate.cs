using System.Collections.Generic;

namespace XuanJianVNext.Core;

/// <summary>
/// 世界级年度检测的统一任务标识。具体玩法仍由各领域系统负责，
/// 此枚举只统一门禁键、最低间隔与调度入口，避免散落字符串和独立 lastYear 调度器。
/// </summary>
internal enum XjDetectionJob : byte
{
	NativeMagicRitualGuard = 0,
	AdventureRealm = 1,
	SecretRealm = 2,
	SecretRealmTraining = 3,
	QuanBing = 4,
	YinSi = 5,
	Lecture = 6,
	SectTask = 7,
	CraftRetention = 8,
	Governance = 9,
	SectTerritoryReconcile = 10,
	SectRebellionSweep = 11,
	SectWar = 12,
	FamilySupport = 13,
	WorldSnapshotMaintenance = 14,
	CenturyDetection = 15,
	CenturyAnnals = 16,
	Codex = 17,
	LongShuBuildingRecovery = 18,
	HighRealmArmyExclusion = 19,
	DongTianRuntime = 20,
	ThreeBookChronicle = 21
}

/// <summary>
/// 角色、家族或仓库等实体级维护槽。所有错峰间隔集中在此，
/// 业务系统不再散落 2/3/4/5/6/12 年等魔法数字。
/// </summary>
internal enum XjEntityDetectionJob : byte
{
	FamilyIdentityRepair = 0,
	FamilyBranchAndSurname = 1,
	SectCityObservation = 2,
	QiuJinWarehouseReconcile = 3,
	TalismanDistribution = 4,
	SectIdentityRefresh = 5,
	ZiFuEquipmentMaintenance = 6,
	ZhuJiEquipmentMaintenance = 7,
	SectWarehouseReconcile = 8,
	AptitudeDecliningLifespan = 9,
	TalismanAutonomousPlan = 10,
	TalismanMaterialGather = 11,
	FormationFamilyCommission = 12,
	FormationMaterialGather = 13,
	HighRealmChess = 14,
	ThreeBookSocialObservation = 15,
	GongFaGrade4Attempt = 16,
	GongFaGrade5Attempt = 17,
	GongFaGrade6Attempt = 18
}

/// <summary>
/// 渲染帧内的统一检测节奏。这里只决定何时允许检查，
/// 不承载玩法结算，也不制造额外队列。
/// </summary>
internal enum XjRuntimeDetectionJob : byte
{
	GrowthScheduler = 0,
	FastScheduler = 1,
	ArchiveCommit = 2,
	JinDanTerrain = 3,
	ActorRegistryCleanup = 4
}

/// <summary>
/// Central gates for bounded runtime checks. Keep subsystem lanes dormant until
/// their minimum world state exists, instead of scattering the same checks.
/// </summary>
internal static class XjDetectionGate
{
	private static readonly Dictionary<string, int> LastAnnualJobYearByKey = new Dictionary<string, int>();
	private static readonly HashSet<string> AnnualJobKeysThisYear = new HashSet<string>();
	private static readonly List<string> AnnualJobPruneBuffer = new List<string>(64);
	private static int _activeAnnualYear;
	private const int MaximumRememberedAnnualJobs = 512;
	private const int AnnualJobRetentionYears = 120;

	internal const string SectTerritoryReconcile = "sect.territory_reconcile";
	internal const string SectRebellionSweep = "sect.rebellion_sweep";
	internal const string AnnualAdventureRealm = "annual.adventure_realm";
	internal const string AnnualSecretRealm = "annual.secret_realm";
	internal const string AnnualSecretRealmTraining = "annual.secret_realm_training";
	internal const string AnnualQuanBing = "annual.quanbing";
	internal const string AnnualYinSi = "annual.yinsi";
	internal const string AnnualLecture = "annual.lecture";
	internal const string AnnualSectTask = "annual.sect_task";
	internal const string AnnualCraftRetention = "annual.craft_retention";
	internal const string AnnualGovernance = "annual.governance";
	internal const string AnnualSectWar = "annual.sect_war";
	internal const string AnnualFamilySupport = "annual.family_support";
	internal const string AnnualWorldDetection = "annual.world_detection";
	internal const string AnnualWorldSnapshotMaintenance = "annual.world_snapshot_maintenance";
	internal const string AnnualCenturyAnnals = "annual.century_annals";
	internal const string AnnualCodex = "annual.codex";
	internal const string AnnualNativeMagicRitualGuard = "annual.native_magic_ritual_guard";
	internal const string AnnualLongShuBuildingRecovery = "annual.longshu_building_recovery";
	internal const string AnnualHighRealmArmyExclusion = "annual.high_realm_army_exclusion";
	internal const string AnnualThreeBookChronicle = "annual.three_book_chronicle";
	internal const string DongTianRuntime = "runtime.dongtian";

	/// <summary>
	/// 统一年度任务入口。任务的默认间隔集中定义在 ResolveAnnualIntervalYears，
	/// 调用方不再自行拼接键或重复保存世界级 lastYear。
	/// </summary>
	internal static bool TryBeginAnnualJob(XjDetectionJob job, int year, string discriminator = null)
	{
		string key = ResolveAnnualJobKey(job);
		if (!string.IsNullOrWhiteSpace(discriminator))
		{
			key += "." + discriminator.Trim();
		}
		return TryBeginAnnualJob(key, year, ResolveAnnualIntervalYears(job));
	}

	/// <summary>
	/// Shared yearly gate for expensive current-state reconciliation. Jobs that
	/// do not need to run more than once per year should pass through here rather
	/// than maintaining separate ad-hoc "last year" fields.
	/// </summary>
	internal static bool TryBeginAnnualJob(string key, int year, int intervalYears = 1)
	{
		if (string.IsNullOrEmpty(key) || year <= 0)
		{
			return false;
		}

		if (_activeAnnualYear != year)
		{
			_activeAnnualYear = year;
			AnnualJobKeysThisYear.Clear();
			PruneAnnualJobHistoryIfNeeded(year);
		}

		if (!AnnualJobKeysThisYear.Add(key))
		{
			return false;
		}

		intervalYears = System.Math.Max(1, intervalYears);
		if (LastAnnualJobYearByKey.TryGetValue(key, out int lastYear)
			&& year - lastYear < intervalYears)
		{
			return false;
		}

		LastAnnualJobYearByKey[key] = year;
		return true;
	}

	internal static int ResolveAnnualIntervalYears(XjDetectionJob job)
	{
		return job switch
		{
			// 关闭任务只作为幂等恢复记录保留十二年；三年清理一次足够，
			// 避免每年遍历炼丹与百艺历史任务表。
			XjDetectionJob.CraftRetention => 3,
			XjDetectionJob.SectTerritoryReconcile => 5,
			XjDetectionJob.SectRebellionSweep => 5,
			XjDetectionJob.WorldSnapshotMaintenance => 10,
			_ => 1
		};
	}

	internal static string ResolveAnnualJobKey(XjDetectionJob job)
	{
		return job switch
		{
			XjDetectionJob.NativeMagicRitualGuard => AnnualNativeMagicRitualGuard,
			XjDetectionJob.AdventureRealm => AnnualAdventureRealm,
			XjDetectionJob.SecretRealm => AnnualSecretRealm,
			XjDetectionJob.SecretRealmTraining => AnnualSecretRealmTraining,
			XjDetectionJob.QuanBing => AnnualQuanBing,
			XjDetectionJob.YinSi => AnnualYinSi,
			XjDetectionJob.Lecture => AnnualLecture,
			XjDetectionJob.SectTask => AnnualSectTask,
			XjDetectionJob.CraftRetention => AnnualCraftRetention,
			XjDetectionJob.Governance => AnnualGovernance,
			XjDetectionJob.SectTerritoryReconcile => SectTerritoryReconcile,
			XjDetectionJob.SectRebellionSweep => SectRebellionSweep,
			XjDetectionJob.SectWar => AnnualSectWar,
			XjDetectionJob.FamilySupport => AnnualFamilySupport,
			XjDetectionJob.WorldSnapshotMaintenance => AnnualWorldSnapshotMaintenance,
			XjDetectionJob.CenturyDetection => AnnualWorldDetection,
			XjDetectionJob.CenturyAnnals => AnnualCenturyAnnals,
			XjDetectionJob.Codex => AnnualCodex,
			XjDetectionJob.LongShuBuildingRecovery => AnnualLongShuBuildingRecovery,
			XjDetectionJob.HighRealmArmyExclusion => AnnualHighRealmArmyExclusion,
			XjDetectionJob.ThreeBookChronicle => AnnualThreeBookChronicle,
			XjDetectionJob.DongTianRuntime => DongTianRuntime,
			_ => "annual.unknown"
		};
	}

	internal static bool ShouldProcessAnnualActors(in XjSchedulerContext context)
	{
		return context.IsYearChange || context.ProcessFast;
	}

	internal static bool ShouldRunAnnualMaintenance(
		string key,
		in XjSchedulerContext context,
		int year,
		int intervalYears = 1)
	{
		return context.IsYearChange
			&& year > 0
			&& TryBeginAnnualJob(key, year, intervalYears);
	}

	/// <summary>
	/// Gives every entity one stable maintenance slot inside an interval. This is
	/// deliberately stateless: actor/family/city business cooldowns remain in
	/// their owning records, while expensive reconciliation scans share one
	/// deterministic scheduler rule.
	/// </summary>
	internal static bool IsAnnualMaintenanceSlot(long entityId, int year, int intervalYears)
	{
		if (entityId <= 0L || year <= 0 || intervalYears <= 1)
		{
			return true;
		}

		long normalizedId = entityId == long.MinValue ? long.MaxValue : System.Math.Abs(entityId);
		return normalizedId % intervalYears == year % intervalYears;
	}

	internal static bool IsEntityMaintenanceSlot(XjEntityDetectionJob job, long entityId, int year)
	{
		int intervalYears = ResolveEntityIntervalYears(job);
		if (entityId <= 0L || year <= 0 || intervalYears <= 1)
		{
			return true;
		}

		int offset = ResolveEntityPhaseOffset(job, entityId, intervalYears);
		return (year + offset) % intervalYears == 0;
	}

	internal static bool IsEntityMaintenanceDueBetween(
		XjEntityDetectionJob job,
		long entityId,
		int fromYearInclusive,
		int toYearInclusive)
	{
		return TryResolveLatestEntityMaintenanceYear(
			job, entityId, fromYearInclusive, toYearInclusive, out _);
	}

	internal static bool TryResolveLatestEntityMaintenanceYear(
		XjEntityDetectionJob job,
		long entityId,
		int fromYearInclusive,
		int toYearInclusive,
		out int dueYear)
	{
		dueYear = 0;
		if (entityId <= 0L || toYearInclusive <= 0 || fromYearInclusive > toYearInclusive)
		{
			return false;
		}

		int intervalYears = ResolveEntityIntervalYears(job);
		int fromYear = System.Math.Max(1, fromYearInclusive);
		if (intervalYears <= 1)
		{
			dueYear = toYearInclusive;
			return true;
		}

		int offset = ResolveEntityPhaseOffset(job, entityId, intervalYears);
		int remainder = (toYearInclusive + offset) % intervalYears;
		if (remainder < 0) remainder += intervalYears;
		int candidate = toYearInclusive - remainder;
		if (candidate < fromYear) return false;
		dueYear = candidate;
		return true;
	}

	internal static int ResolveEntityIntervalYears(XjEntityDetectionJob job)
	{
		return job switch
		{
			XjEntityDetectionJob.FamilyIdentityRepair => 5,
			XjEntityDetectionJob.FamilyBranchAndSurname => 5,
			XjEntityDetectionJob.SectCityObservation => 3,
			XjEntityDetectionJob.QiuJinWarehouseReconcile => 6,
			XjEntityDetectionJob.TalismanDistribution => 4,
			XjEntityDetectionJob.SectIdentityRefresh => 4,
			XjEntityDetectionJob.ZiFuEquipmentMaintenance => 2,
			XjEntityDetectionJob.ZhuJiEquipmentMaintenance => 4,
			XjEntityDetectionJob.SectWarehouseReconcile => 3,
			XjEntityDetectionJob.AptitudeDecliningLifespan => 5,
			XjEntityDetectionJob.TalismanAutonomousPlan => 2,
			XjEntityDetectionJob.TalismanMaterialGather => 2,
			XjEntityDetectionJob.FormationFamilyCommission => 12,
			XjEntityDetectionJob.FormationMaterialGather => 1,
			XjEntityDetectionJob.HighRealmChess => 12,
			XjEntityDetectionJob.ThreeBookSocialObservation => 5,
			XjEntityDetectionJob.GongFaGrade4Attempt => 2,
			XjEntityDetectionJob.GongFaGrade5Attempt => 3,
			XjEntityDetectionJob.GongFaGrade6Attempt => 5,
			_ => 1
		};
	}

	private static int ResolveEntityPhaseOffset(XjEntityDetectionJob job, long entityId, int intervalYears)
	{
		if (intervalYears <= 1) return 0;

		long normalizedId = entityId == long.MinValue ? long.MaxValue : System.Math.Abs(entityId);
		return job switch
		{
			// 保持原上修执棋十二年哈希相位，统一入口但不改变旧档的触发年份。
			XjEntityDetectionJob.HighRealmChess => XjDeterministicHash.PositiveIndex(entityId, "family.high_realm_chess.offset", intervalYears),
			// 保持原三书社交观察的相位：(year + actorId % 5) % 5 == 0。
			XjEntityDetectionJob.ThreeBookSocialObservation => (int)(normalizedId % intervalYears),
			// 保持原功法品级尝试的哈希相位，统一入口但不改变旧档的尝试年份。
			XjEntityDetectionJob.GongFaGrade4Attempt => XjDeterministicHash.PositiveIndex(entityId, "gongfa_attempt_grade_4", intervalYears),
			XjEntityDetectionJob.GongFaGrade5Attempt => XjDeterministicHash.PositiveIndex(entityId, "gongfa_attempt_grade_5", intervalYears),
			XjEntityDetectionJob.GongFaGrade6Attempt => XjDeterministicHash.PositiveIndex(entityId, "gongfa_attempt_grade_6", intervalYears),
			// 其余既有实体槽保持 actorId % interval == year % interval 的原语义。
			_ => (intervalYears - (int)(normalizedId % intervalYears)) % intervalYears
		};
	}

	/// <summary>
	/// Shared cadence check for scheduler-owned runtime housekeeping. The caller
	/// owns the counter; the key documents the lane and keeps cadence decisions
	/// out of individual subsystems.
	/// </summary>
	internal static bool ShouldRunCadence(string key, int tickCounter, int intervalTicks)
	{
		return !string.IsNullOrEmpty(key)
			&& intervalTicks > 0
			&& tickCounter > 0
			&& tickCounter % intervalTicks == 0;
	}

	internal static bool ShouldRunCadence(
		XjRuntimeDetectionJob job,
		int tickCounter,
		int intervalOverride = 0)
	{
		int interval = intervalOverride > 0 ? intervalOverride : ResolveRuntimeIntervalTicks(job);
		return interval > 0 && tickCounter > 0 && tickCounter % interval == 0;
	}

	internal static int ResolveRuntimeIntervalTicks(XjRuntimeDetectionJob job)
	{
		return job switch
		{
			XjRuntimeDetectionJob.GrowthScheduler => 60,
			XjRuntimeDetectionJob.FastScheduler => 10,
			XjRuntimeDetectionJob.ArchiveCommit => 30,
			// 旧值 256 会让万人长档中的失效引用滞留数分钟；64 仍是低频常数级检查。
			XjRuntimeDetectionJob.ActorRegistryCleanup => 64,
			// 地形特效根据压力档动态调整，必须由调用方传 intervalOverride。
			XjRuntimeDetectionJob.JinDanTerrain => 0,
			_ => 0
		};
	}

	internal static bool ShouldProcessZiFuRuntime()
	{
		return XjCultivatorCache.HasZiFuOrHigher;
	}

	internal static bool ShouldProcessJinDanTerrainEffects(int frameCounter, int cadenceFrames)
	{
		return ShouldRunCadence(XjRuntimeDetectionJob.JinDanTerrain, frameCounter, cadenceFrames)
			&& XjCultivatorCache.HasJinDan;
	}

	internal static bool ShouldProcessArchive(int frameCounter, int cadenceFrames)
	{
		return ShouldRunCadence(XjRuntimeDetectionJob.ArchiveCommit, frameCounter, cadenceFrames);
	}

	internal static bool ShouldProcessDongTianRuntime(bool hasActiveRuntimeYear, int worldYear)
	{
		// The registry claims its exact calendar year through TryBeginAnnualJob
		// when it starts work. Do not mutate the annual gate while merely polling
		// background work, otherwise a pending year can be consumed before its
		// bounded explorer pass starts.
		return hasActiveRuntimeYear || worldYear > 0;
	}

	internal static bool ShouldProcessCleanup(int tickCounter, int intervalTicks)
	{
		return ShouldRunCadence(XjRuntimeDetectionJob.ActorRegistryCleanup, tickCounter, intervalTicks);
	}

	private static void PruneAnnualJobHistoryIfNeeded(int currentYear)
	{
		if (LastAnnualJobYearByKey.Count <= MaximumRememberedAnnualJobs || currentYear <= AnnualJobRetentionYears)
		{
			return;
		}

		int threshold = currentYear - AnnualJobRetentionYears;
		AnnualJobPruneBuffer.Clear();
		foreach (KeyValuePair<string, int> pair in LastAnnualJobYearByKey)
		{
			if (pair.Value < threshold) AnnualJobPruneBuffer.Add(pair.Key);
		}
		for (int i = 0; i < AnnualJobPruneBuffer.Count; i++)
		{
			LastAnnualJobYearByKey.Remove(AnnualJobPruneBuffer[i]);
		}
		AnnualJobPruneBuffer.Clear();
	}

	internal static void ClearRuntimeState()
	{
		LastAnnualJobYearByKey.Clear();
		AnnualJobKeysThisYear.Clear();
		AnnualJobPruneBuffer.Clear();
		_activeAnnualYear = 0;
	}
}
