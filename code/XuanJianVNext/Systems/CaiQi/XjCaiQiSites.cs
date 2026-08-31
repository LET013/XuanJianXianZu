using System;
using System.Collections.Generic;

namespace XuanJianVNext.Systems.CaiQi;

internal readonly struct XjCaiQiSite
{
	internal readonly long SiteId;
	internal readonly long CityId;
	internal readonly string PlaceTypeId;
	internal readonly string SiteName;

	internal XjCaiQiSite(
		long siteId,
		long cityId,
		string placeTypeId,
		string siteName)
	{
		SiteId = siteId;
		CityId = cityId;
		PlaceTypeId = placeTypeId ?? string.Empty;
		SiteName = siteName ?? string.Empty;
	}
}

internal readonly struct XjCaiQiSiteRegistrationRequest
{
	internal readonly long SiteId;
	internal readonly long CityId;
	internal readonly string PlaceTypeId;
	internal readonly string SiteName;

	internal XjCaiQiSiteRegistrationRequest(
		long siteId,
		long cityId,
		string placeTypeId,
		string siteName)
	{
		SiteId = siteId;
		CityId = cityId;
		PlaceTypeId = placeTypeId ?? string.Empty;
		SiteName = siteName ?? string.Empty;
	}
}

internal sealed class XjCaiQiSiteIndex
{
	private readonly Dictionary<long, XjCaiQiSite> _sitesById = new Dictionary<long, XjCaiQiSite>();
	private readonly Dictionary<string, List<long>> _siteIdsByPlaceType = new Dictionary<string, List<long>>(StringComparer.Ordinal);
	private readonly Dictionary<long, List<long>> _siteIdsByCityId = new Dictionary<long, List<long>>();

	internal bool Register(in XjCaiQiSite site)
	{
		if (site.SiteId <= 0L
			|| site.CityId <= 0L
			|| !XjCaiQiNameLibrary.NamesByPlaceTypeId.ContainsKey(site.PlaceTypeId))
		{
			return false;
		}

		Remove(site.SiteId);
		_sitesById[site.SiteId] = site;

		if (!_siteIdsByPlaceType.TryGetValue(site.PlaceTypeId, out List<long> ids))
		{
			ids = new List<long>();
			_siteIdsByPlaceType[site.PlaceTypeId] = ids;
		}

		if (!ids.Contains(site.SiteId))
		{
			ids.Add(site.SiteId);
		}

		if (!_siteIdsByCityId.TryGetValue(site.CityId, out List<long> cityIds))
		{
			cityIds = new List<long>();
			_siteIdsByCityId[site.CityId] = cityIds;
		}

		if (!cityIds.Contains(site.SiteId))
		{
			cityIds.Add(site.SiteId);
		}

		return true;
	}

	internal bool Remove(long siteId)
	{
		if (!_sitesById.TryGetValue(siteId, out XjCaiQiSite existing))
		{
			return false;
		}

		_sitesById.Remove(siteId);
		if (_siteIdsByPlaceType.TryGetValue(existing.PlaceTypeId, out List<long> ids))
		{
			ids.Remove(siteId);
			if (ids.Count == 0)
			{
				_siteIdsByPlaceType.Remove(existing.PlaceTypeId);
			}
		}

		if (_siteIdsByCityId.TryGetValue(existing.CityId, out List<long> cityIds))
		{
			cityIds.Remove(siteId);
			if (cityIds.Count == 0)
			{
				_siteIdsByCityId.Remove(existing.CityId);
			}
		}

		return true;
	}

	internal void Clear()
	{
		_sitesById.Clear();
		_siteIdsByPlaceType.Clear();
		_siteIdsByCityId.Clear();
	}

	internal bool TryGet(long siteId, out XjCaiQiSite site)
	{
		return _sitesById.TryGetValue(siteId, out site);
	}

	internal bool TryGetCitySites(long cityId, out IReadOnlyList<XjCaiQiSite> sites)
	{
		sites = Array.Empty<XjCaiQiSite>();
		if (cityId <= 0L || !_siteIdsByCityId.TryGetValue(cityId, out List<long> ids))
		{
			return false;
		}

		List<XjCaiQiSite> result = new List<XjCaiQiSite>(ids.Count);
		for (int i = 0; i < ids.Count; i++)
		{
			if (_sitesById.TryGetValue(ids[i], out XjCaiQiSite candidate))
			{
				result.Add(candidate);
			}
		}

		if (result.Count == 0)
		{
			return false;
		}

		sites = result;
		return true;
	}

	internal bool HasCitySites(long cityId)
	{
		return cityId > 0L
			&& _siteIdsByCityId.TryGetValue(cityId, out List<long> ids)
			&& ids.Count > 0;
	}

	internal bool TryFindByCityAndPlaceType(long cityId, string placeTypeId, out XjCaiQiSite site)
	{
		site = default;
		if (cityId <= 0L || string.IsNullOrEmpty(placeTypeId) || !_siteIdsByCityId.TryGetValue(cityId, out List<long> ids))
		{
			return false;
		}

		for (int i = 0; i < ids.Count; i++)
		{
			if (!_sitesById.TryGetValue(ids[i], out XjCaiQiSite candidate)
				|| !string.Equals(candidate.PlaceTypeId, placeTypeId, StringComparison.Ordinal))
			{
				continue;
			}

			site = candidate;
			return true;
		}

		return false;
	}

	internal bool TryFindByPlaceType(string placeTypeId, long stableSeed, out XjCaiQiSite site)
	{
		site = default;
		if (string.IsNullOrWhiteSpace(placeTypeId)
			|| !_siteIdsByPlaceType.TryGetValue(placeTypeId, out List<long> ids)
			|| ids.Count == 0)
		{
			return false;
		}

		int start = XjDeterministicHash.PositiveIndex(stableSeed, "global_caiqi_site|" + placeTypeId, ids.Count);
		for (int probe = 0; probe < ids.Count; probe++)
		{
			long siteId = ids[(start + probe) % ids.Count];
			if (_sitesById.TryGetValue(siteId, out site))
			{
				return true;
			}
		}

		site = default;
		return false;
	}
}

internal sealed class XjCaiQiSiteRegistrationLane
{
	internal bool HasPending => _pendingRequests.Count > 0;
	private readonly Queue<XjCaiQiSiteRegistrationRequest> _pendingRequests = new Queue<XjCaiQiSiteRegistrationRequest>();
	private readonly HashSet<long> _pendingSiteIds = new HashSet<long>();

	internal void Enqueue(in XjCaiQiSiteRegistrationRequest request)
	{
		if (request.SiteId <= 0L
			|| request.CityId <= 0L
			|| string.IsNullOrWhiteSpace(request.SiteName)
			|| !XjCaiQiNameLibrary.NamesByPlaceTypeId.ContainsKey(request.PlaceTypeId)
			|| !_pendingSiteIds.Add(request.SiteId))
		{
			return;
		}

		_pendingRequests.Enqueue(request);
	}

	internal void Tick(int budget)
	{
		if (budget <= 0)
		{
			return;
		}

		int processed = 0;
		while (processed < budget && _pendingRequests.Count > 0)
		{
			XjCaiQiSiteRegistrationRequest request = _pendingRequests.Dequeue();
			_pendingSiteIds.Remove(request.SiteId);

			XjCaiQiSite site = new XjCaiQiSite(
				request.SiteId,
				request.CityId,
				request.PlaceTypeId,
				request.SiteName);

			XjCaiQiSiteRegistry.Register(site);
			processed++;
		}
	}

	internal void Clear()
	{
		_pendingRequests.Clear();
		_pendingSiteIds.Clear();
	}
}

internal static class XjCaiQiSiteRegistry
{
	internal static bool HasPendingRegistrations => RegistrationLane.HasPending;
	private static readonly XjCaiQiSiteIndex Index = new XjCaiQiSiteIndex();
	private static readonly XjCaiQiSiteRegistrationLane RegistrationLane = new XjCaiQiSiteRegistrationLane();

	internal static bool Register(in XjCaiQiSite site)
	{
		return Index.Register(site);
	}

	internal static bool Remove(long siteId)
	{
		return Index.Remove(siteId);
	}

	internal static void Clear()
	{
		Index.Clear();
		RegistrationLane.Clear();
	}

	internal static bool TryGetCitySites(long cityId, out IReadOnlyList<XjCaiQiSite> sites)
	{
		return Index.TryGetCitySites(cityId, out sites);
	}

	internal static bool HasCitySites(long cityId)
	{
		return Index.HasCitySites(cityId);
	}

	internal static bool TryFindByCityAndPlaceType(long cityId, string placeTypeId, out XjCaiQiSite site)
	{
		return Index.TryFindByCityAndPlaceType(cityId, placeTypeId, out site);
	}

	internal static bool TryFindByPlaceType(string placeTypeId, long stableSeed, out XjCaiQiSite site)
	{
		return Index.TryFindByPlaceType(placeTypeId, stableSeed, out site);
	}

	internal static void EnqueueRegistration(in XjCaiQiSiteRegistrationRequest request)
	{
		RegistrationLane.Enqueue(request);
	}

	internal static void TickRegistrationLane(int budget)
	{
		RegistrationLane.Tick(budget);
	}
}
