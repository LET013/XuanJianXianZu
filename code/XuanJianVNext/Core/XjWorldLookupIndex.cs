using System;
using System.Collections;
using System.Collections.Generic;

namespace XuanJianVNext.Core;

/// <summary>
/// Shared, read-only lookup cache for WorldBox entities addressed by stable ids.
/// Known lookups stay O(1). Native collection churn no longer forces a synchronous
/// whole-city/whole-kingdom rebuild at the first caller after every count change:
/// the previous stable snapshot keeps serving reads while a bounded maintenance
/// cursor reconciles the replacement index in the background.
/// </summary>
internal static class XjWorldLookupIndex
{
	private static Dictionary<long, City> CitiesById = new Dictionary<long, City>();
	private static Dictionary<long, Kingdom> KingdomsById = new Dictionary<long, Kingdom>();
	private static readonly HashSet<long> MissingCityIds = new HashSet<long>();
	private static readonly HashSet<long> MissingKingdomIds = new HashSet<long>();
	private static IReadOnlyList<City> CitySnapshot = Array.Empty<City>();
	private static IReadOnlyList<Kingdom> KingdomSnapshot = Array.Empty<Kingdom>();

	private static IEnumerator _cityRefreshEnumerator;
	private static IEnumerator _kingdomRefreshEnumerator;
	private static Dictionary<long, City> _pendingCitiesById;
	private static Dictionary<long, Kingdom> _pendingKingdomsById;
	private static List<City> _pendingCitySnapshot;
	private static List<Kingdom> _pendingKingdomSnapshot;
	private static int _pendingCityExpectedCount = -1;
	private static int _pendingKingdomExpectedCount = -1;
	private static bool _cityRefreshRequested;
	private static bool _kingdomRefreshRequested;
	private static bool _preferKingdomNext;
	// Native WorldBox lists may change while their enumerator is advancing.  A
	// failed reconciliation must yield to the game instead of immediately
	// recreating the same enumerator in this maintenance slice.
	private static int _cityRefreshRetryAfterTick;
	private static int _kingdomRefreshRetryAfterTick;
	private static int _cityRefreshFailureCount;
	private static int _kingdomRefreshFailureCount;

	private const int MaximumMissingCityIds = 512;
	private const int MaximumMissingKingdomIds = 256;
	private const int SanityRefreshYears = 8;
	private const int RefreshRetryBaseDelayMs = 250;
	private const int RefreshRetryMaxDelayMs = 4000;
	private static object _cachedWorld;
	private static bool _citiesIndexed;
	private static bool _kingdomsIndexed;
	private static int _indexedCityCount = -1;
	private static int _indexedKingdomCount = -1;
	private static int _negativeCacheYear = -1;
	private static int _lastCitySanityYear = -1;
	private static int _lastKingdomSanityYear = -1;

	internal static bool HasPendingMaintenance
	{
		get
		{
			if (World.world == null) return false;
			EnsureCurrentWorld();
			ObserveNativeCollectionState();
			return _cityRefreshRequested || _kingdomRefreshRequested
				|| _cityRefreshEnumerator != null || _kingdomRefreshEnumerator != null;
		}
	}

	internal static int PendingMaintenanceCount
	{
		get
		{
			int pending = 0;
			if (_cityRefreshRequested || _cityRefreshEnumerator != null) pending++;
			if (_kingdomRefreshRequested || _kingdomRefreshEnumerator != null) pending++;
			return pending;
		}
	}

	internal static int TrackedEntityCount => CitiesById.Count + KingdomsById.Count
		+ MissingCityIds.Count + MissingKingdomIds.Count
		+ (_pendingCitiesById?.Count ?? 0) + (_pendingKingdomsById?.Count ?? 0);

	internal static bool TryResolveCity(long cityId, out City city)
	{
		city = null;
		if (cityId <= 0L || World.world?.cities == null) return false;

		EnsureCurrentWorld();
		RefreshNegativeCacheEpoch();
		EnsureCityIndexAvailable();
		ObserveNativeCollectionState();
		if (CitiesById.TryGetValue(cityId, out city))
		{
			if (city?.data != null && city.data.id == cityId)
			{
				return true;
			}

			// Positive-cache entries can outlive a native City during high-speed churn.
			// Evict the stale value immediately on access; do not wait for the full
			// background snapshot reconciliation lane to get frame budget.
			CitiesById.Remove(cityId);
			city = null;
			RequestCityRefresh();
		}

		if (MissingCityIds.Contains(cityId)) return false;

		// A newly created city may appear before the bounded reconciliation lane has
		// published its next stable snapshot. One targeted miss scan preserves exact
		// lookup semantics without rebuilding every city for unrelated callers.
		foreach (City candidate in World.world.cities)
		{
			if (candidate?.data == null || candidate.data.id != cityId) continue;
			CitiesById[cityId] = candidate;
			MissingCityIds.Remove(cityId);
			RequestCityRefresh();
			city = candidate;
			return true;
		}
		AddMissingCityId(cityId);
		return false;
	}

	internal static bool TryResolveKingdom(long kingdomId, out Kingdom kingdom)
	{
		kingdom = null;
		if (kingdomId <= 0L || World.world?.kingdoms == null) return false;

		EnsureCurrentWorld();
		RefreshNegativeCacheEpoch();
		EnsureKingdomIndexAvailable();
		ObserveNativeCollectionState();
		if (KingdomsById.TryGetValue(kingdomId, out kingdom))
		{
			if (kingdom?.data != null && kingdom.data.id == kingdomId)
			{
				return true;
			}

			// Same rule as City: stale positive refs are semantic garbage, so an
			// O(1) read is allowed to discard that one entry immediately.
			KingdomsById.Remove(kingdomId);
			kingdom = null;
			RequestKingdomRefresh();
		}

		if (MissingKingdomIds.Contains(kingdomId)) return false;

		foreach (Kingdom candidate in World.world.kingdoms)
		{
			if (candidate?.data == null || candidate.data.id != kingdomId) continue;
			KingdomsById[kingdomId] = candidate;
			MissingKingdomIds.Remove(kingdomId);
			RequestKingdomRefresh();
			kingdom = candidate;
			return true;
		}
		AddMissingKingdomId(kingdomId);
		return false;
	}

	/// <summary>
	/// Stable read-only city view. Initial construction remains synchronous once for
	/// semantic compatibility; later native collection churn is reconciled in the
	/// background and atomically swaps the completed snapshot.
	/// </summary>
	internal static IReadOnlyList<City> GetCitySnapshot()
	{
		if (World.world?.cities == null) return Array.Empty<City>();
		EnsureCurrentWorld();
		EnsureCityIndexAvailable();
		ObserveNativeCollectionState();
		return CitySnapshot;
	}

	internal static IReadOnlyList<Kingdom> GetKingdomSnapshot()
	{
		if (World.world?.kingdoms == null) return Array.Empty<Kingdom>();
		EnsureCurrentWorld();
		EnsureKingdomIndexAvailable();
		ObserveNativeCollectionState();
		return KingdomSnapshot;
	}

	/// <summary>
	/// Fast-path observation used by known native creation hooks and fault recovery.
	/// It makes an individual entity immediately resolvable while leaving ordered
	/// snapshot reconciliation to the bounded background lane.
	/// </summary>
	internal static void ObserveKingdom(Kingdom kingdom)
	{
		if (kingdom?.data == null || kingdom.data.id <= 0L) return;
		EnsureCurrentWorld();
		KingdomsById[kingdom.data.id] = kingdom;
		MissingKingdomIds.Remove(kingdom.data.id);
		RequestKingdomRefresh();
	}

	internal static void RequestSanityRefresh()
	{
		if (World.world == null) return;
		EnsureCurrentWorld();
		if (_citiesIndexed) RequestCityRefresh();
		if (_kingdomsIndexed) RequestKingdomRefresh();
	}

	/// <summary>
	/// Advances at most the caller's cooperative slice. Each MoveNext is one item;
	/// completed candidate maps are only published if the native collection count
	/// still matches the count observed when the cursor started.
	/// </summary>
	internal static int TickMaintenance(XjCooperativeBudget budget)
	{
		if (budget == null || budget.ShouldYield || World.world == null) return 0;
		EnsureCurrentWorld();
		ObserveNativeCollectionState();
		int processed = 0;

		while (!budget.ShouldYield)
		{
			bool cityPending = _cityRefreshRequested || _cityRefreshEnumerator != null;
			bool kingdomPending = _kingdomRefreshRequested || _kingdomRefreshEnumerator != null;
			if (!cityPending && !kingdomPending) break;

			int before = processed;
			if (_preferKingdomNext && kingdomPending)
			{
				processed += TickKingdomRefreshStep(budget);
				_preferKingdomNext = false;
			}
			else if (cityPending)
			{
				processed += TickCityRefreshStep(budget);
				_preferKingdomNext = true;
			}
			else
			{
				processed += TickKingdomRefreshStep(budget);
				_preferKingdomNext = false;
			}

			if (processed == before) break;
		}
		return processed;
	}


	internal static void ReleaseSnapshotCaches()
	{
		DisposeRefreshEnumerators();
		CitiesById = new Dictionary<long, City>();
		KingdomsById = new Dictionary<long, Kingdom>();
		MissingCityIds.Clear();
		MissingKingdomIds.Clear();
		CitySnapshot = Array.Empty<City>();
		KingdomSnapshot = Array.Empty<Kingdom>();
		_pendingCitiesById = null;
		_pendingKingdomsById = null;
		_pendingCitySnapshot = null;
		_pendingKingdomSnapshot = null;
		_cityRefreshRequested = false;
		_kingdomRefreshRequested = false;
		_cityRefreshRetryAfterTick = 0;
		_kingdomRefreshRetryAfterTick = 0;
		_cityRefreshFailureCount = 0;
		_kingdomRefreshFailureCount = 0;
		_preferKingdomNext = false;
		_citiesIndexed = false;
		_kingdomsIndexed = false;
		_indexedCityCount = -1;
		_indexedKingdomCount = -1;
		_negativeCacheYear = -1;
		_lastCitySanityYear = -1;
		_lastKingdomSanityYear = -1;
	}

	internal static void Clear()
	{
		ReleaseSnapshotCaches();
		_cachedWorld = null;
	}

	private static void EnsureCurrentWorld()
	{
		object world = World.world;
		if (ReferenceEquals(_cachedWorld, world)) return;
		ReleaseSnapshotCaches();
		_cachedWorld = world;
	}

	private static void RefreshNegativeCacheEpoch()
	{
		int year = World.world?.map_stats?.year ?? 0;
		if (_negativeCacheYear == year) return;
		_negativeCacheYear = year;
		MissingCityIds.Clear();
		MissingKingdomIds.Clear();
	}

	private static void EnsureCityIndexAvailable()
	{
		if (!_citiesIndexed) IndexCitiesSynchronously();
	}

	private static void EnsureKingdomIndexAvailable()
	{
		if (!_kingdomsIndexed) IndexKingdomsSynchronously();
	}

	private static void ObserveNativeCollectionState()
	{
		int year = World.world?.map_stats?.year ?? 0;
		int cityCount = SafeCityCount();
		int kingdomCount = SafeKingdomCount();
		if (_citiesIndexed
			&& cityCount != _indexedCityCount
			&& (_cityRefreshEnumerator == null || cityCount != _pendingCityExpectedCount))
		{
			RequestCityRefresh();
		}
		if (_kingdomsIndexed
			&& kingdomCount != _indexedKingdomCount
			&& (_kingdomRefreshEnumerator == null || kingdomCount != _pendingKingdomExpectedCount))
		{
			RequestKingdomRefresh();
		}
		if (year > 0)
		{
			if (_citiesIndexed && _cityRefreshEnumerator == null
				&& _lastCitySanityYear >= 0 && year - _lastCitySanityYear >= SanityRefreshYears) RequestCityRefresh();
			if (_kingdomsIndexed && _kingdomRefreshEnumerator == null
				&& _lastKingdomSanityYear >= 0 && year - _lastKingdomSanityYear >= SanityRefreshYears) RequestKingdomRefresh();
		}
	}

	private static void RequestCityRefresh()
	{
		if (!_citiesIndexed || !IsRefreshRetryReady(_cityRefreshRetryAfterTick)) return;
		_cityRefreshRequested = true;
		// Count churn invalidates same-year negative ids immediately. Known positive
		// entries keep serving until the bounded replacement snapshot is published.
		MissingCityIds.Clear();
	}

	private static void RequestKingdomRefresh()
	{
		if (!_kingdomsIndexed || !IsRefreshRetryReady(_kingdomRefreshRetryAfterTick)) return;
		_kingdomRefreshRequested = true;
		MissingKingdomIds.Clear();
	}

	private static int TickCityRefreshStep(XjCooperativeBudget budget)
	{
		if (_cityRefreshEnumerator == null)
		{
			if (!_cityRefreshRequested || !IsRefreshRetryReady(_cityRefreshRetryAfterTick) || !StartCityRefresh()) return 0;
		}
		if (_cityRefreshEnumerator == null || !budget.TryTake()) return 0;

		bool hasNext;
		object current = null;
		try
		{
			hasNext = _cityRefreshEnumerator.MoveNext();
			if (hasNext) current = _cityRefreshEnumerator.Current;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Core/WorldLookupIndex.city-reconcile", ex);
			AbortCityRefresh(retry: true);
			return 1;
		}
		if (!hasNext)
		{
			CompleteCityRefresh();
			return 1;
		}
		if (current is City city)
		{
			_pendingCitySnapshot.Add(city);
			if (city?.data != null && city.data.id > 0L) _pendingCitiesById[city.data.id] = city;
		}
		return 1;
	}

	private static int TickKingdomRefreshStep(XjCooperativeBudget budget)
	{
		if (_kingdomRefreshEnumerator == null)
		{
			if (!_kingdomRefreshRequested || !IsRefreshRetryReady(_kingdomRefreshRetryAfterTick) || !StartKingdomRefresh()) return 0;
		}
		if (_kingdomRefreshEnumerator == null || !budget.TryTake()) return 0;

		bool hasNext;
		object current = null;
		try
		{
			hasNext = _kingdomRefreshEnumerator.MoveNext();
			if (hasNext) current = _kingdomRefreshEnumerator.Current;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Core/WorldLookupIndex.kingdom-reconcile", ex);
			AbortKingdomRefresh(retry: true);
			return 1;
		}
		if (!hasNext)
		{
			CompleteKingdomRefresh();
			return 1;
		}
		if (current is Kingdom kingdom)
		{
			_pendingKingdomSnapshot.Add(kingdom);
			if (kingdom?.data != null && kingdom.data.id > 0L) _pendingKingdomsById[kingdom.data.id] = kingdom;
		}
		return 1;
	}

	private static bool StartCityRefresh()
	{
		if (World.world?.cities == null) return false;
		try
		{
			_cityRefreshEnumerator = ((IEnumerable)World.world.cities).GetEnumerator();
			_pendingCityExpectedCount = SafeCityCount();
			_pendingCitiesById = new Dictionary<long, City>(Math.Max(0, _pendingCityExpectedCount));
			_pendingCitySnapshot = new List<City>(Math.Max(0, _pendingCityExpectedCount));
			_cityRefreshRequested = false;
			return true;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Core/WorldLookupIndex.city-start", ex);
			AbortCityRefresh(retry: true);
			return false;
		}
	}

	private static bool StartKingdomRefresh()
	{
		if (World.world?.kingdoms == null) return false;
		try
		{
			_kingdomRefreshEnumerator = ((IEnumerable)World.world.kingdoms).GetEnumerator();
			_pendingKingdomExpectedCount = SafeKingdomCount();
			_pendingKingdomsById = new Dictionary<long, Kingdom>(Math.Max(0, _pendingKingdomExpectedCount));
			_pendingKingdomSnapshot = new List<Kingdom>(Math.Max(0, _pendingKingdomExpectedCount));
			_kingdomRefreshRequested = false;
			return true;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Core/WorldLookupIndex.kingdom-start", ex);
			AbortKingdomRefresh(retry: true);
			return false;
		}
	}

	private static void CompleteCityRefresh()
	{
		DisposeEnumerator(ref _cityRefreshEnumerator);
		if (World.world?.cities == null || SafeCityCount() != _pendingCityExpectedCount)
		{
			AbortCityRefresh(retry: true);
			return;
		}
		CitiesById = _pendingCitiesById ?? new Dictionary<long, City>();
		CitySnapshot = _pendingCitySnapshot ?? (IReadOnlyList<City>)Array.Empty<City>();
		_indexedCityCount = _pendingCityExpectedCount;
		_citiesIndexed = true;
		MissingCityIds.Clear();
		_pendingCitiesById = null;
		_pendingCitySnapshot = null;
		_pendingCityExpectedCount = -1;
		_lastCitySanityYear = World.world?.map_stats?.year ?? 0;
		_cityRefreshFailureCount = 0;
		_cityRefreshRetryAfterTick = 0;
	}

	private static void CompleteKingdomRefresh()
	{
		DisposeEnumerator(ref _kingdomRefreshEnumerator);
		if (World.world?.kingdoms == null || SafeKingdomCount() != _pendingKingdomExpectedCount)
		{
			AbortKingdomRefresh(retry: true);
			return;
		}
		KingdomsById = _pendingKingdomsById ?? new Dictionary<long, Kingdom>();
		KingdomSnapshot = _pendingKingdomSnapshot ?? (IReadOnlyList<Kingdom>)Array.Empty<Kingdom>();
		_indexedKingdomCount = _pendingKingdomExpectedCount;
		_kingdomsIndexed = true;
		MissingKingdomIds.Clear();
		_pendingKingdomsById = null;
		_pendingKingdomSnapshot = null;
		_pendingKingdomExpectedCount = -1;
		_lastKingdomSanityYear = World.world?.map_stats?.year ?? 0;
		_kingdomRefreshFailureCount = 0;
		_kingdomRefreshRetryAfterTick = 0;
	}

	private static void AbortCityRefresh(bool retry)
	{
		DisposeEnumerator(ref _cityRefreshEnumerator);
		_pendingCitiesById = null;
		_pendingCitySnapshot = null;
		_pendingCityExpectedCount = -1;
		_cityRefreshRequested = retry && _citiesIndexed;
		if (retry && _citiesIndexed)
		{
			_cityRefreshFailureCount = Math.Min(_cityRefreshFailureCount + 1, 6);
			_cityRefreshRetryAfterTick = Environment.TickCount + GetRefreshRetryDelayMs(_cityRefreshFailureCount);
		}
		else
		{
			_cityRefreshFailureCount = 0;
			_cityRefreshRetryAfterTick = 0;
		}
	}

	private static void AbortKingdomRefresh(bool retry)
	{
		DisposeEnumerator(ref _kingdomRefreshEnumerator);
		_pendingKingdomsById = null;
		_pendingKingdomSnapshot = null;
		_pendingKingdomExpectedCount = -1;
		_kingdomRefreshRequested = retry && _kingdomsIndexed;
		if (retry && _kingdomsIndexed)
		{
			_kingdomRefreshFailureCount = Math.Min(_kingdomRefreshFailureCount + 1, 6);
			_kingdomRefreshRetryAfterTick = Environment.TickCount + GetRefreshRetryDelayMs(_kingdomRefreshFailureCount);
		}
		else
		{
			_kingdomRefreshFailureCount = 0;
			_kingdomRefreshRetryAfterTick = 0;
		}
	}

	private static bool IsRefreshRetryReady(int retryAfterTick)
	{
		return unchecked(Environment.TickCount - retryAfterTick) >= 0;
	}

	private static int GetRefreshRetryDelayMs(int failureCount)
	{
		int exponent = Math.Max(0, Math.Min(4, failureCount - 1));
		return Math.Min(RefreshRetryMaxDelayMs, RefreshRetryBaseDelayMs * (1 << exponent));
	}

	private static void IndexCitiesSynchronously()
	{
		Dictionary<long, City> nextById = new Dictionary<long, City>();
		List<City> nextSnapshot = new List<City>(Math.Max(0, SafeCityCount()));
		foreach (City city in World.world.cities)
		{
			nextSnapshot.Add(city);
			if (city?.data == null || city.data.id <= 0L) continue;
			nextById[city.data.id] = city;
		}
		CitiesById = nextById;
		CitySnapshot = nextSnapshot;
		_citiesIndexed = true;
		_indexedCityCount = SafeCityCount();
		_lastCitySanityYear = World.world?.map_stats?.year ?? 0;
		MissingCityIds.Clear();
	}

	private static void IndexKingdomsSynchronously()
	{
		Dictionary<long, Kingdom> nextById = new Dictionary<long, Kingdom>();
		List<Kingdom> nextSnapshot = new List<Kingdom>(Math.Max(0, SafeKingdomCount()));
		foreach (Kingdom kingdom in World.world.kingdoms)
		{
			nextSnapshot.Add(kingdom);
			if (kingdom?.data == null || kingdom.data.id <= 0L) continue;
			nextById[kingdom.data.id] = kingdom;
		}
		KingdomsById = nextById;
		KingdomSnapshot = nextSnapshot;
		_kingdomsIndexed = true;
		_indexedKingdomCount = SafeKingdomCount();
		_lastKingdomSanityYear = World.world?.map_stats?.year ?? 0;
		MissingKingdomIds.Clear();
	}

	private static int SafeCityCount()
	{
		try { return Math.Max(0, World.world?.cities?.Count ?? 0); }
		catch { return _indexedCityCount < 0 ? 0 : _indexedCityCount; }
	}

	private static int SafeKingdomCount()
	{
		try { return Math.Max(0, World.world?.kingdoms?.Count ?? 0); }
		catch { return _indexedKingdomCount < 0 ? 0 : _indexedKingdomCount; }
	}

	private static void DisposeRefreshEnumerators()
	{
		DisposeEnumerator(ref _cityRefreshEnumerator);
		DisposeEnumerator(ref _kingdomRefreshEnumerator);
	}

	private static void DisposeEnumerator(ref IEnumerator enumerator)
	{
		try { (enumerator as IDisposable)?.Dispose(); }
		catch { }
		enumerator = null;
	}

	private static void AddMissingCityId(long cityId)
	{
		if (MissingCityIds.Count >= MaximumMissingCityIds) MissingCityIds.Clear();
		MissingCityIds.Add(cityId);
	}

	private static void AddMissingKingdomId(long kingdomId)
	{
		if (MissingKingdomIds.Count >= MaximumMissingKingdomIds) MissingKingdomIds.Clear();
		MissingKingdomIds.Add(kingdomId);
	}
}
