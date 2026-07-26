using XuanJianVNext.Core;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// Spreads world-wide annual systems across fast cadences. Annual gameplay years
/// are processed in order so high simulation speed does not silently drop chances,
/// while each frame advances only a bounded number of stages under a wall-clock budget.
///
/// Every stage now enters through XjUnifiedDetectionPlan + XjDetectionGate:
/// one cheap index snapshot decides whether the stage has any possible work, and
/// one centralized key/interval gate guarantees that the world job starts once.
/// </summary>
internal static class XjAnnualWorldRuntimeLane
{
	private enum Stage : byte
	{
		None = 0,
		NativeMagicGuard = 1,
		AdventureRealm = 2,
		SecretRealm = 3,
		SecretRealmTraining = 4,
		QuanBing = 5,
		YinSi = 6,
		Lecture = 7,
		SectTask = 8,
		CraftRetention = 9,
		Governance = 10,
		SectWar = 11,
		FamilySupport = 12,
		HighRealmArmyExclusion = 13,
		ThreeBookChronicle = 14,
		WorldSnapshot = 15,
		CenturyDetection = 16,
		CenturyAnnals = 17,
		Codex = 18
	}

	private static Stage _stage;
	private static int _activeYear;
	private static int _latestRequestedYear;
	private static int _lastScheduledYear;
	private static int _detectionSnapshotYear;
	private static XjUnifiedDetectionSnapshot _detectionSnapshot;

	internal static bool HasPending => _stage != Stage.None;

	internal static void Schedule(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0 || currentYear <= _lastScheduledYear)
		{
			return;
		}

		int firstPendingYear = _lastScheduledYear > 0
			? _lastScheduledYear + 1
			: currentYear;
		_lastScheduledYear = currentYear;
		_latestRequestedYear = currentYear;
		if (_stage == Stage.None)
		{
			// 高倍速下 Fast cadence 可能一次跨过数个世界年。首次载入不回放历史，
			// 之后则从上次已请求年份的下一年开始，逐年补齐机会型年度事件。
			BeginYear(System.Math.Min(firstPendingYear, currentYear), Stage.NativeMagicGuard);
		}
	}

	internal static void ScheduleCenturyCatchUp(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0)
		{
			return;
		}

		_latestRequestedYear = System.Math.Max(_latestRequestedYear, currentYear);
		if (_stage == Stage.None)
		{
			BeginYear(currentYear, Stage.CenturyDetection);
		}
	}

	/// <summary>
	/// 在一个渲染帧预算内连续推进若干年度阶段。无资格的空阶段会被快速跳过，
	/// 真正执行过的任务仍受统一年度门禁保护；高倍速下不会再因“一次只走一格”
	/// 让待处理年份永久落后于世界年份。
	/// </summary>
	internal static void Tick(int stageBudget, double timeBudgetMs)
	{
		if (_stage == Stage.None || !XjWorldSchemaGuard.GameplayEnabled || stageBudget <= 0)
		{
			return;
		}

		long started = System.Diagnostics.Stopwatch.GetTimestamp();
		for (int advanced = 0; advanced < stageBudget && _stage != Stage.None; advanced++)
		{
			TickOneStage();
			if (timeBudgetMs > 0d)
			{
				long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
				if (elapsed > timeBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000d)
				{
					break;
				}
			}
		}
	}

	private static void TickOneStage()
	{
		if (_stage == Stage.None || !XjWorldSchemaGuard.GameplayEnabled)
		{
			return;
		}

		EnsureDetectionSnapshot();
		switch (_stage)
		{
			case Stage.NativeMagicGuard:
				if (TryBegin(XjDetectionJob.NativeMagicRitualGuard))
				{
					XjNativeMagicRitualGuard.EnsureDisabled();
				}
				_stage = Stage.AdventureRealm;
				return;
			case Stage.AdventureRealm:
				if (TryBegin(XjDetectionJob.AdventureRealm))
				{
					XjAdventureRealmClaimSystem.TickYear(_activeYear);
				}
				_stage = Stage.SecretRealm;
				return;
			case Stage.SecretRealm:
				if (TryBegin(XjDetectionJob.SecretRealm))
				{
					XjSecretRealmConstructionSystem.TickYear(_activeYear);
				}
				_stage = Stage.SecretRealmTraining;
				return;
			case Stage.SecretRealmTraining:
				if (TryBegin(XjDetectionJob.SecretRealmTraining))
				{
					XjSecretRealmTrainingSystem.TickYear(_activeYear);
				}
				_stage = Stage.QuanBing;
				return;
			case Stage.QuanBing:
				if (TryBegin(XjDetectionJob.QuanBing))
				{
					XjQuanBingStruggleSystem.TickAnnual(_activeYear);
				}
				_stage = Stage.YinSi;
				return;
			case Stage.YinSi:
				if (TryBegin(XjDetectionJob.YinSi))
				{
					XjYinSiExposurePursuitSystem.TickYear(_activeYear);
				}
				_stage = Stage.Lecture;
				return;
			case Stage.Lecture:
				if (TryBegin(XjDetectionJob.Lecture))
				{
					XjSectLectureSystem.TickYear(_activeYear);
				}
				_stage = Stage.SectTask;
				return;
			case Stage.SectTask:
				if (TryBegin(XjDetectionJob.SectTask))
				{
					XjSectTaskSystem.TickYear(_activeYear);
				}
				_stage = Stage.CraftRetention;
				return;
			case Stage.CraftRetention:
				if (TryBegin(XjDetectionJob.CraftRetention))
				{
					// 关闭任务只作为幂等恢复记录保留。统一门禁将该扫描限制为三年一次，
					// 每次最多各清理 512 条，避免长期世界在同一帧清理全部历史任务。
					XjCraftDomainRegistry.PruneClosedTasks(_activeYear, retentionYears: 12, removeBudget: 512);
					XjAlchemyRuntimeRegistry.PruneClosedTasks(_activeYear, retentionYears: 12, removeBudget: 512);
				}
				_stage = Stage.Governance;
				return;
			case Stage.Governance:
				if (TryBegin(XjDetectionJob.Governance))
				{
					XjSectGovernanceRuntimeLane.Schedule(_activeYear);
					XjZongMenRuntimeLane.Schedule(_activeYear);
				}
				_stage = Stage.SectWar;
				return;
			case Stage.SectWar:
				if (TryBegin(XjDetectionJob.SectWar))
				{
					XjSectWarSystem.TickYear(_activeYear);
				}
				_stage = Stage.FamilySupport;
				return;
			case Stage.FamilySupport:
				if (TryBegin(XjDetectionJob.FamilySupport))
				{
					XjFamilySupportSystem.TickYear(_activeYear);
				}
				_stage = Stage.HighRealmArmyExclusion;
				return;
			case Stage.HighRealmArmyExclusion:
				if (TryBegin(XjDetectionJob.HighRealmArmyExclusion))
				{
					XjHighRealmArmyExclusion.ScheduleAnnualCleanup();
				}
				_stage = Stage.ThreeBookChronicle;
				return;
			case Stage.ThreeBookChronicle:
				if (TryBegin(XjDetectionJob.ThreeBookChronicle))
				{
					// 独立三书只读取现有宗门档案、仓库计数与最多128条敌意记录；
					// 不进入角色扫描，也不复用天下纪事正文。
					long threeBookSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundThreeBookWorld, 0);
					XjThreeBookWorldObserver.TickYear(_activeYear);
					XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundThreeBookWorld, threeBookSample);
				}
				_stage = Stage.WorldSnapshot;
				return;
			case Stage.WorldSnapshot:
				if (XjAnnualWorldDetectionStore.ShouldRefresh(
						_activeYear,
						XjDetectionGate.ResolveAnnualIntervalYears(XjDetectionJob.WorldSnapshotMaintenance))
					&& TryBegin(XjDetectionJob.WorldSnapshotMaintenance))
				{
					XjAnnualWorldDetectionStore.Refresh(_activeYear);
				}
				_stage = Stage.CenturyDetection;
				return;
			case Stage.CenturyDetection:
				if (TryBegin(XjDetectionJob.CenturyDetection))
				{
					// 百年卷生成必须使用同一世界快照；只在确有缺卷时刷新。
					XjAnnualWorldDetectionStore.Refresh(_activeYear);
				}
				_stage = Stage.CenturyAnnals;
				return;
			case Stage.CenturyAnnals:
				int pendingCenturyId = XjCenturyAnnalsBuilder.ResolveNextPendingCompletedCenturyId(_activeYear);
				if (pendingCenturyId > 0
					&& TryBegin(
						XjDetectionJob.CenturyAnnals,
						pendingCenturyId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
				{
					XjCenturyAnnalsBuilder.TryBuildAndStore(_activeYear);
				}
				_stage = XjCenturyAnnalsBuilder.HasPendingCompletedCentury(_activeYear)
					? Stage.CenturyDetection
					: Stage.Codex;
				return;
			case Stage.Codex:
				if (TryBegin(XjDetectionJob.Codex))
				{
					XjCodexSnapshotPublisher.ScheduleYear(_activeYear);
				}
				XjRuntimeMemoryPruner.RunAnnual(_activeYear);
				CompleteActiveYear();
				return;
			default:
				Clear();
				return;
		}
	}

	internal static void Clear()
	{
		_stage = Stage.None;
		_activeYear = 0;
		_latestRequestedYear = 0;
		_lastScheduledYear = 0;
		_detectionSnapshotYear = 0;
		_detectionSnapshot = default;
	}

	private static bool TryBegin(XjDetectionJob job, string discriminator = null)
	{
		return XjUnifiedDetectionPlan.ShouldRun(job, in _detectionSnapshot)
			&& XjDetectionGate.TryBeginAnnualJob(job, _activeYear, discriminator);
	}

	private static void EnsureDetectionSnapshot()
	{
		if (_detectionSnapshotYear == _activeYear)
		{
			return;
		}
		_detectionSnapshot = XjUnifiedDetectionPlan.Capture(_activeYear);
		_detectionSnapshotYear = _activeYear;
	}

	private static void BeginYear(int year, Stage firstStage)
	{
		_activeYear = System.Math.Max(0, year);
		_stage = _activeYear > 0 ? firstStage : Stage.None;
		_detectionSnapshotYear = 0;
		_detectionSnapshot = default;
	}

	private static void CompleteActiveYear()
	{
		if (XjCenturyAnnalsBuilder.HasPendingCompletedCentury(_activeYear))
		{
			_stage = Stage.CenturyDetection;
			return;
		}

		if (_latestRequestedYear > _activeYear)
		{
			// 机会型年度逻辑不得跳过中间年份；按预算逐年追赶。
			BeginYear(_activeYear + 1, Stage.NativeMagicGuard);
			return;
		}

		_stage = Stage.None;
		_activeYear = 0;
		_detectionSnapshotYear = 0;
		_detectionSnapshot = default;
	}
}
