using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// Resumable owner for the annual SectTask stage. The original stage contains
/// two independent domain jobs (inheritance coverage and five-year common
/// duties); this coordinator remembers which sub-job has already started so the
/// DetectionGate is consumed exactly once while work may continue for many
/// rendered frames.
/// </summary>
internal static class XjSectAnnualMaintenanceRuntime
{
	private enum Phase : byte
	{
		None = 0,
		TransmissionCoverage = 1,
		CommonDuties = 2
	}

	private static Phase _phase;
	private static int _activeYear;
	private static bool _phaseStarted;

	internal static bool HasPendingForYear(int currentYear)
	{
		return currentYear > 0 && _activeYear == currentYear && _phase != Phase.None;
	}

	internal static bool BeginYear(int currentYear)
	{
		if (currentYear <= 0) return false;
		if (HasPendingForYear(currentYear)) return true;
		Clear();
		_activeYear = currentYear;
		_phase = Phase.TransmissionCoverage;
		_phaseStarted = false;
		return true;
	}

	internal static bool TickPending()
	{
		if (_phase == Phase.None || _activeYear <= 0) return true;

		if (_phase == Phase.TransmissionCoverage)
		{
			if (!_phaseStarted)
			{
				_phaseStarted = true;
				XjSectTransmissionCoverageSystem.BeginYear(_activeYear);
			}
			if (XjSectTransmissionCoverageSystem.HasPendingForYear(_activeYear)
				&& !XjSectTransmissionCoverageSystem.TickPending(itemBudget: 2, timeBudgetMs: 0.32d))
			{
				return false;
			}
			_phase = Phase.CommonDuties;
			_phaseStarted = false;
		}

		if (_phase == Phase.CommonDuties)
		{
			if (!_phaseStarted)
			{
				_phaseStarted = true;
				XjSectTaskSystem.BeginYear(_activeYear);
			}
			if (XjSectTaskSystem.HasPendingForYear(_activeYear)
				&& !XjSectTaskSystem.TickPending(itemBudget: 24, timeBudgetMs: 0.36d))
			{
				return false;
			}
		}

		ClearRuntimeStateOnly();
		return true;
	}

	internal static void Clear()
	{
		XjSectTransmissionCoverageSystem.ClearPendingAnnualWork();
		XjSectTaskSystem.ClearPendingAnnualWork();
		ClearRuntimeStateOnly();
	}

	private static void ClearRuntimeStateOnly()
	{
		_phase = Phase.None;
		_activeYear = 0;
		_phaseStarted = false;
	}
}
