using System.Collections.Generic;

namespace XuanJianVNext.Core;

/// <summary>
/// Shared, read-only lookup cache for WorldBox entities addressed by stable ids.
/// Most domain systems only need to resolve one known city; rebuilding a local
/// world-wide search in each of them becomes expensive once several archives
/// are active. Cache misses still scan the native list so newly created cities
/// remain visible without relying on fragile creation hooks.
/// </summary>
internal static class XjWorldLookupIndex
{
	private static readonly Dictionary<long, City> CitiesById = new Dictionary<long, City>();
	private static readonly Dictionary<long, Kingdom> KingdomsById = new Dictionary<long, Kingdom>();
	private static readonly HashSet<long> MissingCityIds = new HashSet<long>();
	private static readonly HashSet<long> MissingKingdomIds = new HashSet<long>();
	private static City[] CitySnapshot = System.Array.Empty<City>();
	private static Kingdom[] KingdomSnapshot = System.Array.Empty<Kingdom>();
	private const int MaximumMissingCityIds = 512;
	private const int MaximumMissingKingdomIds = 256;
	private static object _cachedWorld;
	private static bool _citiesIndexed;
	private static bool _kingdomsIndexed;
	private static int _indexedCityCount = -1;
	private static int _indexedKingdomCount = -1;
	private static int _negativeCacheYear = -1;

	internal static bool TryResolveCity(long cityId, out City city)
	{
		city = null;
		if (cityId <= 0L || World.world?.cities == null)
		{
			return false;
		}

		EnsureCurrentWorld();
		RefreshNegativeCacheEpoch();
		EnsureCityIndexCurrent();
		if (CitiesById.TryGetValue(cityId, out city)
			&& city?.data != null
			&& city.data.id == cityId)
		{
			return true;
		}

		if (MissingCityIds.Contains(cityId)) return false;

		// New cities can be created after the first index pass. A miss performs
		// one safe native scan and adds the result for every later caller.
		foreach (City candidate in World.world.cities)
		{
			if (candidate?.data == null || candidate.data.id != cityId) continue;
			CitiesById[cityId] = candidate;
			MissingCityIds.Remove(cityId);
			city = candidate;
			return true;
		}
		AddMissingCityId(cityId);
		return false;
	}

	internal static bool TryResolveKingdom(long kingdomId, out Kingdom kingdom)
	{
		kingdom = null;
		if (kingdomId <= 0L || World.world?.kingdoms == null)
		{
			return false;
		}

		EnsureCurrentWorld();
		RefreshNegativeCacheEpoch();
		EnsureKingdomIndexCurrent();
		if (KingdomsById.TryGetValue(kingdomId, out kingdom)
			&& kingdom?.data != null
			&& kingdom.data.id == kingdomId)
		{
			return true;
		}

		if (MissingKingdomIds.Contains(kingdomId)) return false;

		foreach (Kingdom candidate in World.world.kingdoms)
		{
			if (candidate?.data == null || candidate.data.id != kingdomId) continue;
			KingdomsById[kingdomId] = candidate;
			MissingKingdomIds.Remove(kingdomId);
			kingdom = candidate;
			return true;
		}
		AddMissingKingdomId(kingdomId);
		return false;
	}

	/// <summary>
	/// Stable read-only city view for annual and combat-side audits. The snapshot
	/// only changes when the native city collection changes, while each City
	/// instance still exposes its live owner and population.
	/// </summary>
	internal static IReadOnlyList<City> GetCitySnapshot()
	{
		if (World.world?.cities == null) return System.Array.Empty<City>();
		EnsureCurrentWorld();
		EnsureCityIndexCurrent();
		return CitySnapshot;
	}

	internal static IReadOnlyList<Kingdom> GetKingdomSnapshot()
	{
		if (World.world?.kingdoms == null) return System.Array.Empty<Kingdom>();
		EnsureCurrentWorld();
		EnsureKingdomIndexCurrent();
		return KingdomSnapshot;
	}

	internal static void Invalidate()
	{
		// Manual codex refresh invalidates only derived lookup snapshots; native world data is untouched.
		Clear();
	}

	internal static void Clear()
	{
		CitiesById.Clear();
		KingdomsById.Clear();
		MissingCityIds.Clear();
		MissingKingdomIds.Clear();
		CitySnapshot = System.Array.Empty<City>();
		KingdomSnapshot = System.Array.Empty<Kingdom>();
		_cachedWorld = null;
		_citiesIndexed = false;
		_kingdomsIndexed = false;
		_indexedCityCount = -1;
		_indexedKingdomCount = -1;
		_negativeCacheYear = -1;
	}

	private static void EnsureCurrentWorld()
	{
		object world = World.world;
		if (object.ReferenceEquals(_cachedWorld, world)) return;
		CitiesById.Clear();
		KingdomsById.Clear();
		MissingCityIds.Clear();
		MissingKingdomIds.Clear();
		CitySnapshot = System.Array.Empty<City>();
		KingdomSnapshot = System.Array.Empty<Kingdom>();
		_cachedWorld = world;
		_citiesIndexed = false;
		_kingdomsIndexed = false;
		_indexedCityCount = -1;
		_indexedKingdomCount = -1;
		_negativeCacheYear = -1;
	}

	private static void RefreshNegativeCacheEpoch()
	{
		int year = World.world?.map_stats?.year ?? 0;
		if (_negativeCacheYear == year) return;
		_negativeCacheYear = year;
		MissingCityIds.Clear();
		MissingKingdomIds.Clear();
	}

	private static void EnsureCityIndexCurrent()
	{
		if (!_citiesIndexed || _indexedCityCount != World.world.cities.Count) IndexCities();
	}

	private static void EnsureKingdomIndexCurrent()
	{
		if (!_kingdomsIndexed || _indexedKingdomCount != World.world.kingdoms.Count) IndexKingdoms();
	}

	private static void IndexCities()
	{
		CitiesById.Clear();
		MissingCityIds.Clear();
		_citiesIndexed = true;
		_indexedCityCount = World.world.cities.Count;
		CitySnapshot = new City[_indexedCityCount];
		int snapshotIndex = 0;
		foreach (City city in World.world.cities)
		{
			if (snapshotIndex < CitySnapshot.Length) CitySnapshot[snapshotIndex++] = city;
			if (city?.data == null || city.data.id <= 0L) continue;
			CitiesById[city.data.id] = city;
		}
		if (snapshotIndex != CitySnapshot.Length)
		{
			System.Array.Resize(ref CitySnapshot, snapshotIndex);
		}
	}

	private static void IndexKingdoms()
	{
		KingdomsById.Clear();
		MissingKingdomIds.Clear();
		_kingdomsIndexed = true;
		_indexedKingdomCount = World.world.kingdoms.Count;
		KingdomSnapshot = new Kingdom[_indexedKingdomCount];
		int snapshotIndex = 0;
		foreach (Kingdom kingdom in World.world.kingdoms)
		{
			if (snapshotIndex < KingdomSnapshot.Length) KingdomSnapshot[snapshotIndex++] = kingdom;
			if (kingdom?.data == null || kingdom.data.id <= 0L) continue;
			KingdomsById[kingdom.data.id] = kingdom;
		}
		if (snapshotIndex != KingdomSnapshot.Length)
		{
			System.Array.Resize(ref KingdomSnapshot, snapshotIndex);
		}
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
