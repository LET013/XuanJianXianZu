using System;
using System.Collections.Generic;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.ZongMen;

/// <summary>
/// Five-year compatibility maintenance for the legacy city mirror. New sect
/// creation is event-driven and drained from the Kingdom transition queue.
/// </summary>
internal static class XjZongMenRuntimeLane
{
	private const int YearsPerPeriod = 5;

	private enum MaintenancePhase : byte
	{
		None = 0,
		Maintain = 1
	}

	private static List<City> _cities = new List<City>(0);
	private static int _lastCompletedPeriod;
	private static int _queuedThroughPeriod;
	private static int _activePeriod;
	private static int _importedLastCompletedPeriod = -1;
	private static int _currentYear;
	private static int _cursor;
	private static int _maintained;
	private static int _createdPeaks;
	private static MaintenancePhase _phase;

	internal static bool HasPending => XjNationSectTransitionSystem.HasPending
		|| _phase != MaintenancePhase.None
		|| _queuedThroughPeriod > _lastCompletedPeriod;

	internal static void InitializeAfterLoad(int currentYear)
	{
		ResetActiveCycle();
		int reachedPeriod = ToPeriod(currentYear);
		_lastCompletedPeriod = _importedLastCompletedPeriod < 0
			? reachedPeriod
			: Math.Min(Math.Max(0, _importedLastCompletedPeriod), reachedPeriod);
		_queuedThroughPeriod = reachedPeriod;
	}

	internal static int ExportLastCompletedPeriod() => Math.Max(0, _lastCompletedPeriod);
	internal static void ImportLastCompletedPeriod(int period) => _importedLastCompletedPeriod = period;

	internal static void Schedule(int currentYear)
	{
		int reachedPeriod = ToPeriod(currentYear);
		if (reachedPeriod <= _lastCompletedPeriod) return;
		_queuedThroughPeriod = Math.Max(_queuedThroughPeriod, reachedPeriod);
		TryBeginNextCycle();
	}

	internal static void Tick(int cityVisitBudget)
	{
		int remaining = Math.Max(0, cityVisitBudget);
		if (remaining <= 0) return;

		int transitions = XjNationSectTransitionSystem.Tick(remaining);
		remaining = Math.Max(0, remaining - transitions);

		while (remaining > 0 && (_phase != MaintenancePhase.None || _queuedThroughPeriod > _lastCompletedPeriod))
		{
			if (_phase == MaintenancePhase.None)
			{
				TryBeginNextCycle();
				if (_phase == MaintenancePhase.None)
				{
					remaining--;
					continue;
				}
			}

			Dictionary<City, List<long>> cityIndex = XjZongMenSystem.GetCurrentCityIndex();
			if (cityIndex.Count == 0)
			{
				CompleteActivePeriod();
				remaining--;
				continue;
			}

			remaining -= TickMaintain(cityIndex, remaining);
		}
	}

	internal static void Clear()
	{
		_lastCompletedPeriod = 0;
		_queuedThroughPeriod = 0;
		_importedLastCompletedPeriod = -1;
		XjNationSectTransitionSystem.Clear();
		ResetActiveCycle();
	}

	private static int TickMaintain(Dictionary<City, List<long>> cityIndex, int budget)
	{
		int visited = 0;
		while (visited < budget && _cursor < _cities.Count && _maintained < XjZongMenSystem.MaintenanceBudget)
		{
			City city = _cities[_cursor++];
			visited++;
			if (XjZongMenSystem.TryMaintainExisting(city, _currentYear, cityIndex, ref _createdPeaks)) _maintained++;
			if (cityIndex.TryGetValue(city, out List<long> actorIds))
			{
				XjZongMenMentorshipSystem.TickCity(city, actorIds, _currentYear);
			}
		}

		if (_cursor >= _cities.Count || _maintained >= XjZongMenSystem.MaintenanceBudget) CompleteActivePeriod();
		return Math.Max(1, visited);
	}

	private static void TryBeginNextCycle()
	{
		if (_phase != MaintenancePhase.None || _queuedThroughPeriod <= _lastCompletedPeriod) return;
		_activePeriod = _lastCompletedPeriod + 1;
		_currentYear = _activePeriod * YearsPerPeriod;
		_cities = XjZongMenSystem.SelectMaintenanceCandidates(_activePeriod);
		_cursor = 0;
		_maintained = 0;
		_createdPeaks = 0;
		_phase = MaintenancePhase.Maintain;
		if (_cities.Count == 0) CompleteActivePeriod();
	}

	private static void CompleteActivePeriod()
	{
		if (_activePeriod > 0)
		{
			_lastCompletedPeriod = Math.Max(_lastCompletedPeriod, _activePeriod);
			XjWorldArchiveSystem.MarkChanged();
		}
		ResetActiveCycle();
	}

	private static void ResetActiveCycle()
	{
		_cities.Clear();
		_activePeriod = 0;
		_currentYear = 0;
		_cursor = 0;
		_maintained = 0;
		_createdPeaks = 0;
		_phase = MaintenancePhase.None;
	}

	private static int ToPeriod(int year) => year <= 0 ? 0 : year / YearsPerPeriod;
}
