using XuanJianVNext.Core;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Localization;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;

using XuanJianVNext.Systems.Events;
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
	internal enum Stage : byte
	{
		None = 0,
		NativeMagicGuard = 1,
		WorldEventCatalog = 2,
		AdventureRealm = 3,
		SecretRealm = 4,
		SecretRealmTraining = 5,
		DaoLineageMaintenance = 6,
		RuleInvariantAudit = 7,
		GuZunMaintenance = 8,
		KingdomNameNormalization = 9,
		QuanBing = 10,
		YinSi = 11,
		Lecture = 12,
		SectTask = 13,
		CraftRetention = 14,
		Governance = 15,
		SectWar = 16,
		FamilySupport = 17,
		HighRealmArmyExclusion = 18,
		ThreeBookChronicle = 19,
		WorldSnapshot = 20,
		CenturyDetection = 21,
		CenturyAnnals = 22,
		ShiReincarnationReturns = 23,
		Codex = 24,
		MemoryMaintenance = 25,
		XianGuoPolitics = 26
	}

	private static Stage _stage;
	private static int _activeYear;
	private static int _latestRequestedYear;
	private static int _lastScheduledYear;
	private static int _detectionSnapshotYear;
	private static XjUnifiedDetectionSnapshot _detectionSnapshot;

	internal static bool HasPending => _stage != Stage.None;

	/// <summary>
	/// Number of live world years currently ahead of the oldest annual-world transaction.
	/// O(1): used only by the scheduler to size a tiny non-starvable control-plane slice.
	/// </summary>
	internal static int PendingYearLag
	{
		get
		{
			if (_stage == Stage.None || _activeYear <= 0) return 0;
			int currentYear = World.world?.map_stats?.year ?? 0;
			return currentYear > _activeYear ? currentYear - _activeYear : 0;
		}
	}

	internal static void Schedule(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0 || currentYear <= _lastScheduledYear)
		{
			return;
		}

		// 统一年度入口只负责“请求”一次；Invariant lane 自己以8年周期、快照和协作预算限频。
		XjRuntimeInvariantLane.Schedule(currentYear);

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
		Tick(stageBudget, timeBudgetMs, XjRuntimeFramePriority.Background);
	}

	internal static void Tick(
		int stageBudget,
		double timeBudgetMs,
		XjRuntimeFramePriority priority)
	{
		if (_stage == Stage.None || !XjWorldSchemaGuard.GameplayEnabled || stageBudget <= 0)
		{
			return;
		}

		XjCooperativeBudget budget = new XjCooperativeBudget(
			stageBudget,
			timeBudgetMs,
			priority);
		while (_stage != Stage.None && budget.TryTake())
		{
			Stage previousStage = _stage;
			long stageSample = XjRuntimeDiagnostics.BeginNamedSample();
			try
			{
				TickOneStage();
			}
			finally
			{
				XjRuntimeDiagnostics.EndNamedSample("annual." + previousStage, stageSample);
			}
			XjRuntimeFrameGovernor.MarkPotentialOverrun("annual-world." + previousStage);
			// A reducer may deliberately keep the same stage while a bounded detection
			// scan is still pending. Yield immediately instead of polling the same gate
			// repeatedly inside one rendered frame.
			if (_stage == previousStage) break;
		}
	}

	private static void TickOneStage()
	{
		if (_stage == Stage.None || !XjWorldSchemaGuard.GameplayEnabled)
		{
			return;
		}

		EnsureDetectionSnapshot();
		XjAnnualWorldCommand command = new XjAnnualWorldCommand(
			_stage,
			_activeYear,
			_latestRequestedYear,
			in _detectionSnapshot);
		XjAnnualWorldReduction reduction = XjAnnualWorldCommandReducer.Reduce(in command);
		if (reduction.ClearLane)
		{
			Clear();
			return;
		}
		if (reduction.CompleteYear)
		{
			CompleteActiveYear();
			return;
		}
		_stage = reduction.NextStage;
	}

	internal static void Clear()
	{
		_stage = Stage.None;
		_activeYear = 0;
		_latestRequestedYear = 0;
		_lastScheduledYear = 0;
		_detectionSnapshotYear = 0;
		_detectionSnapshot = default;
		XjFamilySupportSystem.ClearPending();
		XjSectLectureSystem.ClearPendingAnnualWork();
		XjSecretRealmConstructionSystem.ClearPendingAnnualWork();
		XjSecretRealmTrainingSystem.ClearPendingAnnualWork();
		XjDaoLineageStateRegistry.ClearPendingWorldTick();
		XjQuanBingStruggleSystem.ClearPendingAnnualWork();
		XjYinSiExposurePursuitSystem.ClearPendingAnnualWork();
		XjSectAnnualMaintenanceRuntime.Clear();
		XjSectWarSystem.ClearPendingAnnualWork();
		XjThreeBookWorldObserver.ClearPendingAnnualWork();
		XjRuntimeMemoryPruner.Clear();
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
