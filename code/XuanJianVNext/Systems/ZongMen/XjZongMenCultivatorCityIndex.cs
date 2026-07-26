using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.ZongMen;

internal readonly struct XjCityCultivatorAggregate
{
	internal readonly int CultivatorCount;
	internal readonly int ZiFuCount;
	internal readonly int JinDanCount;

	internal XjCityCultivatorAggregate(int cultivatorCount, int ziFuCount, int jinDanCount)
	{
		CultivatorCount = System.Math.Max(0, cultivatorCount);
		ZiFuCount = System.Math.Max(0, ziFuCount);
		JinDanCount = System.Math.Max(0, jinDanCount);
	}
}

internal readonly struct XjCityFamilyCultivatorAggregate
{
	internal readonly long FamilyId;
	internal readonly int MemberCount;
	internal readonly int TotalRealmWeight;
	internal readonly int HighestRealmOrder;

	internal XjCityFamilyCultivatorAggregate(long familyId, int memberCount, int totalRealmWeight, int highestRealmOrder)
	{
		FamilyId = familyId;
		MemberCount = System.Math.Max(0, memberCount);
		TotalRealmWeight = System.Math.Max(0, totalRealmWeight);
		HighestRealmOrder = System.Math.Max(0, highestRealmOrder);
	}
}

/// <summary>
/// Incremental cultivator-to-city index. Actor callbacks update one entry;
/// annual sect work reads the stable index without rescanning every cultivator.
/// </summary>
internal static class XjZongMenCultivatorCityIndex
{
	private sealed class FamilyCityAccumulator
	{
		internal int MemberCount;
		internal int TotalRealmWeight;
		internal int HighestRealmOrder;
		internal readonly SortedDictionary<int, int> RealmCounts = new SortedDictionary<int, int>();
	}
	private static readonly Dictionary<City, List<long>> ActorIdsByCity = new Dictionary<City, List<long>>();
	private static readonly Dictionary<City, HashSet<long>> ActorIdSetsByCity = new Dictionary<City, HashSet<long>>();
	private static readonly Dictionary<long, City> CityByActorId = new Dictionary<long, City>();
	private static readonly Dictionary<long, int> RealmOrderByActorId = new Dictionary<long, int>();
	private static readonly Dictionary<long, long> FamilyIdByActorId = new Dictionary<long, long>();
	private static readonly Dictionary<long, long> SectIdByActorId = new Dictionary<long, long>();
	private static readonly Dictionary<long, List<long>> ActorIdsBySect = new Dictionary<long, List<long>>();
	private static readonly Dictionary<long, HashSet<long>> ActorIdSetsBySect = new Dictionary<long, HashSet<long>>();
	private static readonly Dictionary<City, int[]> AggregateByCity = new Dictionary<City, int[]>();
	private static readonly Dictionary<City, Dictionary<long, FamilyCityAccumulator>> FamilyAggregateByCity = new Dictionary<City, Dictionary<long, FamilyCityAccumulator>>();
	private static readonly HashSet<City> CandidateCities = new HashSet<City>();
	private static readonly Queue<long> CleanupQueue = new Queue<long>();
	private static readonly HashSet<long> QueuedActorIds = new HashSet<long>();

	internal static void Observe(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}

		if (!actor.isAlive() || XjYinSiTraitLifecycle.IsYinSi(actor)
			|| !XjCultivatorCache.IsCultivator(actorId) || actor.city?.data == null)
		{
			Remove(actorId);
			return;
		}

		City city = actor.city;
		int realmOrder = ResolveRealmOrder(actor);
		long familyId = ResolveFamilyId(actorId);
		long sectId = ResolveSectId(actor);
		if (CityByActorId.TryGetValue(actorId, out City previousCity))
		{
			RealmOrderByActorId.TryGetValue(actorId, out int previousRealmOrder);
			FamilyIdByActorId.TryGetValue(actorId, out long previousFamilyId);
			SectIdByActorId.TryGetValue(actorId, out long previousSectId);
			if (previousCity == city)
			{
				if (previousSectId != sectId)
				{
					RemoveFromSect(actorId, previousSectId);
					AddToSect(actorId, sectId);
					MarkCodexDirty();
				}
				if (previousRealmOrder != realmOrder || previousFamilyId != familyId)
				{
					AdjustAggregate(city, previousRealmOrder, -1);
					AdjustFamilyAggregate(city, previousFamilyId, previousRealmOrder, -1);
					AdjustAggregate(city, realmOrder, 1);
					AdjustFamilyAggregate(city, familyId, realmOrder, 1);
					RealmOrderByActorId[actorId] = realmOrder;
					FamilyIdByActorId[actorId] = familyId;
					MarkCodexDirty();
				}
				return;
			}

			RemoveFromCity(previousCity, actorId);
		}
		else
		{
			EnqueueCleanup(actorId);
		}

		CityByActorId[actorId] = city;
		RealmOrderByActorId[actorId] = realmOrder;
		FamilyIdByActorId[actorId] = familyId;
		AddToSect(actorId, sectId);
		if (!ActorIdsByCity.TryGetValue(city, out List<long> actorIds))
		{
			actorIds = new List<long>();
			ActorIdsByCity[city] = actorIds;
			ActorIdSetsByCity[city] = new HashSet<long>();
			CandidateCities.Add(city);
		}

		if (!ActorIdSetsByCity.TryGetValue(city, out HashSet<long> actorIdSet))
		{
			actorIdSet = new HashSet<long>(actorIds);
			ActorIdSetsByCity[city] = actorIdSet;
		}

		if (actorIdSet.Add(actorId))
		{
			actorIds.Add(actorId);
			AdjustAggregate(city, realmOrder, 1);
			AdjustFamilyAggregate(city, familyId, realmOrder, 1);
			MarkCodexDirty();
		}
	}

	internal static bool HasCandidateCities => CandidateCities.Count > 0;

	internal static HashSet<City> GetCandidateCities()
	{
		return CandidateCities;
	}

	internal static Dictionary<City, List<long>> GetCityIndex()
	{
		return ActorIdsByCity;
	}

	internal static IReadOnlyList<long> GetActorIdsForSect(long sectId)
	{
		return sectId > 0L && ActorIdsBySect.TryGetValue(sectId, out List<long> actorIds)
			? actorIds
			: System.Array.Empty<long>();
	}

	internal static bool TryGetSectId(long actorId, out long sectId)
	{
		sectId = 0L;
		return actorId > 0L && SectIdByActorId.TryGetValue(actorId, out sectId) && sectId > 0L;
	}

	internal static bool TryGetAggregate(City city, out XjCityCultivatorAggregate aggregate)
	{
		if (city != null && AggregateByCity.TryGetValue(city, out int[] values) && values != null && values.Length >= 3)
		{
			aggregate = new XjCityCultivatorAggregate(values[0], values[1], values[2]);
			return true;
		}
		aggregate = default;
		return false;
	}

	internal static IReadOnlyList<XjCityFamilyCultivatorAggregate> ReadFamilyAggregates(City city)
	{
		if (city == null || !FamilyAggregateByCity.TryGetValue(city, out Dictionary<long, FamilyCityAccumulator> byFamily) || byFamily.Count == 0)
			return System.Array.Empty<XjCityFamilyCultivatorAggregate>();
		List<XjCityFamilyCultivatorAggregate> result = new List<XjCityFamilyCultivatorAggregate>(byFamily.Count);
		foreach (KeyValuePair<long, FamilyCityAccumulator> pair in byFamily)
		{
			if (pair.Key <= 0L || pair.Value == null || pair.Value.MemberCount <= 0) continue;
			result.Add(new XjCityFamilyCultivatorAggregate(pair.Key, pair.Value.MemberCount, pair.Value.TotalRealmWeight, pair.Value.HighestRealmOrder));
		}
		result.Sort((left, right) => left.FamilyId.CompareTo(right.FamilyId));
		return result;
	}

	internal static void CleanupInvalid(int budget)
	{
		int checks = System.Math.Min(System.Math.Max(0, budget), CleanupQueue.Count);
		for (int i = 0; i < checks; i++)
		{
			long actorId = CleanupQueue.Dequeue();
			QueuedActorIds.Remove(actorId);
			if (!XjScheduler.ResolveActor(actorId, out Actor actor)
				|| actor?.data == null
				|| !actor.isAlive()
				|| !XjCultivatorCache.IsCultivator(actorId)
				|| actor.city?.data == null)
			{
				Remove(actorId);
				continue;
			}

			Observe(actor);
			EnqueueCleanup(actorId);
		}
	}

	internal static void Clear()
	{
		ActorIdsByCity.Clear();
		ActorIdSetsByCity.Clear();
		CityByActorId.Clear();
		RealmOrderByActorId.Clear();
		FamilyIdByActorId.Clear();
		SectIdByActorId.Clear();
		ActorIdsBySect.Clear();
		ActorIdSetsBySect.Clear();
		AggregateByCity.Clear();
		FamilyAggregateByCity.Clear();
		CandidateCities.Clear();
		CleanupQueue.Clear();
		QueuedActorIds.Clear();
	}

	internal static void Forget(long actorId)
	{
		Remove(actorId);
		QueuedActorIds.Remove(actorId);
	}

	private static void Remove(long actorId)
	{
		if (!CityByActorId.TryGetValue(actorId, out City city))
		{
			RealmOrderByActorId.Remove(actorId);
			FamilyIdByActorId.Remove(actorId);
			RemoveFromSect(actorId);
			return;
		}

		CityByActorId.Remove(actorId);
		RemoveFromCity(city, actorId);
	}

	private static void RemoveFromCity(City city, long actorId)
	{
		if (city == null || !ActorIdsByCity.TryGetValue(city, out List<long> actorIds))
		{
			RealmOrderByActorId.Remove(actorId);
			FamilyIdByActorId.Remove(actorId);
			RemoveFromSect(actorId);
			return;
		}

		RealmOrderByActorId.TryGetValue(actorId, out int realmOrder);
		FamilyIdByActorId.TryGetValue(actorId, out long familyId);
		RealmOrderByActorId.Remove(actorId);
		FamilyIdByActorId.Remove(actorId);
		RemoveFromSect(actorId);
		actorIds.Remove(actorId);
		if (ActorIdSetsByCity.TryGetValue(city, out HashSet<long> actorIdSet))
		{
			actorIdSet.Remove(actorId);
		}
		AdjustAggregate(city, realmOrder, -1);
		AdjustFamilyAggregate(city, familyId, realmOrder, -1);
		MarkCodexDirty();
		if (actorIds.Count == 0)
		{
			ActorIdsByCity.Remove(city);
			ActorIdSetsByCity.Remove(city);
			AggregateByCity.Remove(city);
			RemoveFamilyAggregatesForCity(city);
			CandidateCities.Remove(city);
		}
	}

	private static int ResolveRealmOrder(Actor actor)
	{
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return System.Math.Max(0, XjRealmHelper.GetOrder(realmId));
	}

	private static void AdjustAggregate(City city, int realmOrder, int delta)
	{
		if (city == null || delta == 0) return;
		if (!AggregateByCity.TryGetValue(city, out int[] values))
		{
			values = new int[3];
			AggregateByCity[city] = values;
		}
		values[0] = System.Math.Max(0, values[0] + delta);
		int jinDanOrder = XjRealmHelper.GetOrder(XuanJianVNext.Data.Rules.XjRealmIds.JinDan);
		int ziFuOrder = XjRealmHelper.GetOrder(XuanJianVNext.Data.Rules.XjRealmIds.ZiFu);
		if (realmOrder >= jinDanOrder) values[2] = System.Math.Max(0, values[2] + delta);
		else if (realmOrder >= ziFuOrder) values[1] = System.Math.Max(0, values[1] + delta);
	}

	private static long ResolveFamilyId(long actorId)
	{
		return actorId > 0L && XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId) && familyId > 0L ? familyId : 0L;
	}

	private static long ResolveSectId(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor);
	}

	private static void AddToSect(long actorId, long sectId)
	{
		SectIdByActorId[actorId] = sectId;
		if (sectId <= 0L) return;
		if (!ActorIdsBySect.TryGetValue(sectId, out List<long> actorIds))
		{
			actorIds = new List<long>();
			ActorIdsBySect.Add(sectId, actorIds);
			ActorIdSetsBySect.Add(sectId, new HashSet<long>());
		}
		if (ActorIdSetsBySect[sectId].Add(actorId)) actorIds.Add(actorId);
	}

	private static void RemoveFromSect(long actorId, long knownSectId = 0L)
	{
		long sectId = knownSectId;
		if (sectId <= 0L) SectIdByActorId.TryGetValue(actorId, out sectId);
		SectIdByActorId.Remove(actorId);
		if (sectId <= 0L || !ActorIdsBySect.TryGetValue(sectId, out List<long> actorIds)) return;
		actorIds.Remove(actorId);
		if (ActorIdSetsBySect.TryGetValue(sectId, out HashSet<long> actorIdSet)) actorIdSet.Remove(actorId);
		if (actorIds.Count > 0) return;
		ActorIdsBySect.Remove(sectId);
		ActorIdSetsBySect.Remove(sectId);
	}

	private static void AdjustFamilyAggregate(City city, long familyId, int realmOrder, int delta)
	{
		if (city == null || familyId <= 0L || delta == 0) return;
		if (!FamilyAggregateByCity.TryGetValue(city, out Dictionary<long, FamilyCityAccumulator> byFamily))
		{
			if (delta < 0) return;
			byFamily = new Dictionary<long, FamilyCityAccumulator>();
			FamilyAggregateByCity.Add(city, byFamily);
		}
		if (!byFamily.TryGetValue(familyId, out FamilyCityAccumulator aggregate))
		{
			if (delta < 0) return;
			aggregate = new FamilyCityAccumulator();
			byFamily.Add(familyId, aggregate);
		}
		aggregate.MemberCount = System.Math.Max(0, aggregate.MemberCount + delta);
		aggregate.TotalRealmWeight = System.Math.Max(0, aggregate.TotalRealmWeight + RealmWeight(realmOrder) * delta);
		aggregate.RealmCounts.TryGetValue(realmOrder, out int realmCount);
		realmCount += delta;
		if (realmCount <= 0) aggregate.RealmCounts.Remove(realmOrder);
		else aggregate.RealmCounts[realmOrder] = realmCount;
		if (delta > 0 && realmOrder > aggregate.HighestRealmOrder)
		{
			aggregate.HighestRealmOrder = realmOrder;
		}
		else if (delta < 0 && realmOrder == aggregate.HighestRealmOrder && realmCount <= 0)
		{
			aggregate.HighestRealmOrder = 0;
			foreach (KeyValuePair<int, int> pair in aggregate.RealmCounts)
			{
				if (pair.Value > 0) aggregate.HighestRealmOrder = pair.Key;
			}
		}
		if (aggregate.MemberCount <= 0) byFamily.Remove(familyId);
		if (byFamily.Count == 0) FamilyAggregateByCity.Remove(city);
	}

	private static void RemoveFamilyAggregatesForCity(City city)
	{
		if (city != null) FamilyAggregateByCity.Remove(city);
	}

	private static int RealmWeight(int order)
	{
		if (order >= XjRealmHelper.GetOrder(XuanJianVNext.Data.Rules.XjRealmIds.JinDan)) return 2000;
		if (order >= XjRealmHelper.GetOrder(XuanJianVNext.Data.Rules.XjRealmIds.ZiFu)) return 800;
		if (order >= XjRealmHelper.GetOrder(XuanJianVNext.Data.Rules.XjRealmIds.ZhuJi)) return 300;
		if (order >= XjRealmHelper.GetOrder(XuanJianVNext.Data.Rules.XjRealmIds.LianQi)) return 120;
		return 10;
	}

	private static void MarkCodexDirty()
	{
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
			XuanJianVNext.Data.Codex.XjCodexDirtyFlags.City | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect);
	}

	private static void EnqueueCleanup(long actorId)
	{
		if (actorId > 0L && QueuedActorIds.Add(actorId))
		{
			CleanupQueue.Enqueue(actorId);
		}
	}
}
