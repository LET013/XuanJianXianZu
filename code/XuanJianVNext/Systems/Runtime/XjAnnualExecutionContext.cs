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
	private static long _realmOverrideActorId;
	private static string _realmOverrideId = string.Empty;

	internal static bool HasActiveYear => _depth > 0 && _currentYear > 0;
	internal static int CurrentYear => HasActiveYear ? _currentYear : 0;

	internal static Scope Enter(int year)
	{
		return EnterCore(year, 0L, string.Empty);
	}

	internal static Scope Enter(int year, Actor actor, string realmId)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		return EnterCore(year, actorId, realmId);
	}

	private static Scope EnterCore(int year, long actorId, string realmId)
	{
		int previousYear = _currentYear;
		int previousDepth = _depth;
		long previousActorId = _realmOverrideActorId;
		string previousRealmId = _realmOverrideId;
		_currentYear = Math.Max(0, year);
		_depth = previousDepth + 1;
		_realmOverrideActorId = actorId > 0L ? actorId : 0L;
		_realmOverrideId = actorId > 0L ? (realmId ?? string.Empty).Trim() : string.Empty;
		return new Scope(previousYear, previousDepth, previousActorId, previousRealmId);
	}

	internal static bool TryResolveRealmOverride(Actor actor, out string realmId)
	{
		realmId = string.Empty;
		if (_depth <= 0 || _realmOverrideActorId <= 0L || string.IsNullOrWhiteSpace(_realmOverrideId)
			|| actor?.data == null || ((BaseSystemData)actor.data).id != _realmOverrideActorId)
		{
			return false;
		}
		realmId = _realmOverrideId;
		return true;
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
		private readonly long _previousRealmActorId;
		private readonly string _previousRealmId;

		internal Scope(int previousYear, int previousDepth, long previousRealmActorId, string previousRealmId)
		{
			_previousYear = previousYear;
			_previousDepth = previousDepth;
			_previousRealmActorId = previousRealmActorId;
			_previousRealmId = previousRealmId;
		}

		public void Dispose()
		{
			_currentYear = _previousYear;
			_depth = _previousDepth;
			_realmOverrideActorId = _previousRealmActorId;
			_realmOverrideId = _previousRealmId;
		}
	}
}
