using System;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// Scoped world-year context for a deferred annual actor pass. High-speed play
/// may move the live world year ahead while an older actor-year is still queued;
/// annual systems must therefore resolve the queued year rather than the latest
/// world value. Runtime execution is main-thread only.
/// </summary>
internal static class XjAnnualExecutionContext
{
	private static int _currentYear;
	private static int _depth;

	internal static bool HasActiveYear => _depth > 0 && _currentYear > 0;
	internal static int CurrentYear => HasActiveYear ? _currentYear : 0;

	internal static Scope Enter(int year)
	{
		int previousYear = _currentYear;
		int previousDepth = _depth;
		_currentYear = Math.Max(0, year);
		_depth = previousDepth + 1;
		return new Scope(previousYear, previousDepth);
	}

	internal static int ResolveYear(Actor actor)
	{
		if (HasActiveYear)
		{
			return _currentYear;
		}

		int trackedYear = XjYearTracker.CurrentYear;
		if (trackedYear > 0)
		{
			return trackedYear;
		}

		int worldYear = World.world?.map_stats?.year ?? 0;
		if (worldYear > 0)
		{
			return worldYear;
		}

		return actor == null ? 0 : (int)Math.Floor(Math.Max(0f, actor.getAge()));
	}

	internal readonly struct Scope : IDisposable
	{
		private readonly int _previousYear;
		private readonly int _previousDepth;

		internal Scope(int previousYear, int previousDepth)
		{
			_previousYear = previousYear;
			_previousDepth = previousDepth;
		}

		public void Dispose()
		{
			_currentYear = _previousYear;
			_depth = _previousDepth;
		}
	}
}
