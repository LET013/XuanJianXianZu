using System.Collections.Generic;
using System.Text;

namespace XuanJianVNext.Systems.CaiQi;

internal static class XjCaiQiCityOrderIndex
{
	private static readonly Dictionary<long, int> OrdinalByCityId = new Dictionary<long, int>();
	private static int _nextOrdinal;

	internal static int GetOrAddOrdinal(long cityId)
	{
		if (cityId <= 0L)
		{
			return -1;
		}

		if (OrdinalByCityId.TryGetValue(cityId, out int ordinal))
		{
			return ordinal;
		}

		ordinal = _nextOrdinal;
		OrdinalByCityId[cityId] = ordinal;
		_nextOrdinal++;
		return ordinal;
	}

	internal static void Clear()
	{
		OrdinalByCityId.Clear();
		_nextOrdinal = 0;
	}
}

internal static class XjCaiQiCitySiteSource
{
	internal const int SitesPerCity = 5;

	private static readonly string[] PlaceTypeIds =
	{
		XjCaiQiPlaceTypeIds.SanYin,
		XjCaiQiPlaceTypeIds.SanYang,
		XjCaiQiPlaceTypeIds.SanLei,
		XjCaiQiPlaceTypeIds.JinDe,
		XjCaiQiPlaceTypeIds.MuDe,
		XjCaiQiPlaceTypeIds.ShuiDe,
		XjCaiQiPlaceTypeIds.HuoDe,
		XjCaiQiPlaceTypeIds.TuDe,
		XjCaiQiPlaceTypeIds.ShiErQi,
		XjCaiQiPlaceTypeIds.BingGu
	};

	internal static void EnqueueCitySites(long cityId)
	{
		if (cityId <= 0L || XjCaiQiSiteRegistry.HasCitySites(cityId))
		{
			return;
		}

		int ordinal = XjCaiQiCityOrderIndex.GetOrAddOrdinal(cityId);
		if (ordinal < 0)
		{
			return;
		}

		string[] selectedPlaceTypes = SelectPlaceTypes(cityId, ordinal);
		for (int i = 0; i < selectedPlaceTypes.Length; i++)
		{
			string placeTypeId = selectedPlaceTypes[i];
			string siteName = XjCaiQiNameLibrary.GetNameAt(placeTypeId, XjDeterministicHash.PositiveIndex(cityId, placeTypeId + ":name", int.MaxValue));
			if (string.IsNullOrWhiteSpace(siteName))
			{
				continue;
			}

			XjCaiQiSiteRegistrationRequest request = new XjCaiQiSiteRegistrationRequest(
				BuildSiteId(cityId, placeTypeId),
				cityId,
				placeTypeId,
				siteName);

			XjCaiQiSiteRegistry.EnqueueRegistration(request);
		}
	}

	private static string[] SelectPlaceTypes(long cityId, int ordinal)
	{
		// 55条先天之气按十个采气地类型归组。最早两座城市各取连续五类，
		// 即可覆盖全部十类，不需要为了55条道途强行生成十一座城市。
		if (ordinal >= 0 && ordinal < 2)
		{
			int start = ordinal * SitesPerCity;
			string[] guaranteed = new string[SitesPerCity];
			for (int i = 0; i < SitesPerCity; i++)
			{
				guaranteed[i] = PlaceTypeIds[start + i];
			}
			return guaranteed;
		}

		string[] result = new string[SitesPerCity];
		bool[] used = new bool[PlaceTypeIds.Length];
		int startIndex = XjDeterministicHash.PositiveIndex(cityId, "place", PlaceTypeIds.Length);
		int step = XjDeterministicHash.PositiveIndex(cityId, "step", PlaceTypeIds.Length - 1) + 1;

		for (int i = 0; i < SitesPerCity; i++)
		{
			int index = (startIndex + i * step) % PlaceTypeIds.Length;
			for (int probe = 0; probe < PlaceTypeIds.Length && used[index]; probe++)
			{
				index = (index + 1) % PlaceTypeIds.Length;
			}

			used[index] = true;
			result[i] = PlaceTypeIds[index];
		}

		return result;
	}


	private static long BuildSiteId(long cityId, string placeTypeId)
	{
		long hash = XjDeterministicHash.PositiveHash(cityId, placeTypeId);
		return hash <= 0L ? 1L : hash;
	}

}

internal static class XjCaiQiCityMaintenanceLane
{
	internal static bool HasPending => PendingCityIds.Count > 0;
	private static readonly Queue<long> PendingCityIds = new Queue<long>();
	private static readonly HashSet<long> PendingCitySet = new HashSet<long>();

	internal static void MarkCityPending(long cityId)
	{
		if (cityId <= 0L || PendingCitySet.Contains(cityId) || XjCaiQiSiteRegistry.HasCitySites(cityId))
		{
			return;
		}

		PendingCitySet.Add(cityId);
		PendingCityIds.Enqueue(cityId);
	}

	internal static void Tick(int budget)
	{
		if (budget <= 0)
		{
			return;
		}

		for (int processed = 0; processed < budget && PendingCityIds.Count > 0; processed++)
		{
			long cityId = PendingCityIds.Dequeue();
			PendingCitySet.Remove(cityId);
			if (cityId <= 0L)
			{
				continue;
			}

			XjCaiQiCitySiteSource.EnqueueCitySites(cityId);
		}
	}

	internal static void Clear()
	{
		PendingCityIds.Clear();
		PendingCitySet.Clear();
	}
}

internal static class XjCaiQiCityDisplayFormatter
{
	internal static string FormatCitySites(long cityId)
	{
		if (cityId <= 0L || !XjCaiQiSiteRegistry.TryGetCitySites(cityId, out IReadOnlyList<XjCaiQiSite> sites) || sites.Count == 0)
		{
			return string.Empty;
		}

		StringBuilder builder = new StringBuilder();
		bool hasName = false;
		for (int i = 0; i < sites.Count; i++)
		{
			string siteName = sites[i].SiteName;
			if (string.IsNullOrWhiteSpace(siteName))
			{
				continue;
			}

			if (hasName)
			{
				builder.Append('、');
			}

			builder.Append(siteName);
			hasName = true;
		}

		return hasName ? builder.ToString() : string.Empty;
	}
}
