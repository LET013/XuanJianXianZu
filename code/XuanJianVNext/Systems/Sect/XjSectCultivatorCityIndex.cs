using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

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
internal static class XjSectCultivatorCityIndex
{
	private sealed class FamilyCityAccumulator
	{
		internal int MemberCount;
		internal int TotalRealmWeight;
		internal int HighestRealmOrder;
		internal readonly SortedDictionary<int, int> RealmCounts = new SortedDictionary<int, int>();
	}
	private static readonly Dictionary<long, List<long>> ActorIdsByCityId = new Dictionary<long, List<long>>();
	private static readonly Dictionary<long, HashSet<long>> ActorIdSetsByCityId = new Dictionary<long, HashSet<long>>();
	private static readonly Dictionary<long, long> CityIdByActorId = new Dictionary<long, long>();
	private static readonly Dictionary<long, int> RealmOrderByActorId = new Dictionary<long, int>();
	private static readonly Dictionary<long, long> FamilyIdByActorId = new Dictionary<long, long>();
	private static readonly Dictionary<long, int[]> AggregateByCityId = new Dictionary<long, int[]>();
	private static readonly Dictionary<long, Dictionary<long, FamilyCityAccumulator>> FamilyAggregateByCityId = new Dictionary<long, Dictionary<long, FamilyCityAccumulator>>();
	private static readonly HashSet<long> CandidateCityIds = new HashSet<long>();
	private static readonly Queue<long> CleanupQueue = new Queue<long>();
	private static readonly HashSet<long> QueuedActorIds = new HashSet<long>();

	internal static int TrackedActorCount => CityIdByActorId.Count;
	internal static int PendingCleanupCount => CleanupQueue.Count;

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
		long cityId = city.data.id;
		if (cityId <= 0L)
		{
			Remove(actorId);
			return;
		}
		int realmOrder = ResolveRealmOrder(actor);
		long familyId = ResolveFamilyId(actorId);
		if (CityIdByActorId.TryGetValue(actorId, out long previousCityId))
		{
			RealmOrderByActorId.TryGetValue(actorId, out int previousRealmOrder);
			FamilyIdByActorId.TryGetValue(actorId, out long previousFamilyId);
			if (previousCityId == cityId)
			{
				if (previousRealmOrder != realmOrder || previousFamilyId != familyId)
				{
					AdjustAggregate(cityId, previousRealmOrder, -1);
					AdjustFamilyAggregate(cityId, previousFamilyId, previousRealmOrder, -1);
					AdjustAggregate(cityId, realmOrder, 1);
					AdjustFamilyAggregate(cityId, familyId, realmOrder, 1);
					RealmOrderByActorId[actorId] = realmOrder;
					FamilyIdByActorId[actorId] = familyId;
					MarkCodexDirty();
				}
				return;
			}

			RemoveFromCity(previousCityId, actorId);
		}
		else
		{
			EnqueueCleanup(actorId);
		}

		CityIdByActorId[actorId] = cityId;
		RealmOrderByActorId[actorId] = realmOrder;
		FamilyIdByActorId[actorId] = familyId;
		if (!ActorIdsByCityId.TryGetValue(cityId, out List<long> actorIds))
		{
			actorIds = new List<long>();
			ActorIdsByCityId[cityId] = actorIds;
			ActorIdSetsByCityId[cityId] = new HashSet<long>();
			CandidateCityIds.Add(cityId);
		}

		if (!ActorIdSetsByCityId.TryGetValue(cityId, out HashSet<long> actorIdSet))
		{
			actorIdSet = new HashSet<long>(actorIds);
			ActorIdSetsByCityId[cityId] = actorIdSet;
		}

		if (actorIdSet.Add(actorId))
		{
			actorIds.Add(actorId);
			AdjustAggregate(cityId, realmOrder, 1);
			AdjustFamilyAggregate(cityId, familyId, realmOrder, 1);
			MarkCodexDirty();
		}
	}

	internal static bool HasCandidateCities => CandidateCityIds.Count > 0;

	internal static IReadOnlyCollection<long> GetCandidateCityIds()
	{
		return CandidateCityIds;
	}

	internal static IReadOnlyDictionary<long, List<long>> GetCityIndex()
	{
		return ActorIdsByCityId;
	}

	internal static bool TryGetActorIds(City city, out List<long> actorIds)
	{
		actorIds = null;
		return city?.data != null && city.data.id > 0L
			&& ActorIdsByCityId.TryGetValue(city.data.id, out actorIds);
	}

	internal static bool TryGetActorIds(long cityId, out List<long> actorIds)
	{
		actorIds = null;
		return cityId > 0L && ActorIdsByCityId.TryGetValue(cityId, out actorIds);
	}
	internal static bool TryGetAggregate(City city, out XjCityCultivatorAggregate aggregate)
	{
		if (city?.data != null && AggregateByCityId.TryGetValue(city.data.id, out int[] values) && values != null && values.Length >= 3)
		{
			aggregate = new XjCityCultivatorAggregate(values[0], values[1], values[2]);
			return true;
		}
		aggregate = default;
		return false;
	}

	internal static IReadOnlyList<XjCityFamilyCultivatorAggregate> ReadFamilyAggregates(City city)
	{
		if (city?.data == null || !FamilyAggregateByCityId.TryGetValue(city.data.id, out Dictionary<long, FamilyCityAccumulator> byFamily) || byFamily.Count == 0)
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
		ActorIdsByCityId.Clear();
		ActorIdSetsByCityId.Clear();
		CityIdByActorId.Clear();
		RealmOrderByActorId.Clear();
		FamilyIdByActorId.Clear();
		AggregateByCityId.Clear();
		FamilyAggregateByCityId.Clear();
		CandidateCityIds.Clear();
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
		if (!CityIdByActorId.TryGetValue(actorId, out long cityId))
		{
			RealmOrderByActorId.Remove(actorId);
			FamilyIdByActorId.Remove(actorId);
			return;
		}

		CityIdByActorId.Remove(actorId);
		RemoveFromCity(cityId, actorId);
	}

	private static void RemoveFromCity(long cityId, long actorId)
	{
		if (cityId <= 0L || !ActorIdsByCityId.TryGetValue(cityId, out List<long> actorIds))
		{
			RealmOrderByActorId.Remove(actorId);
			FamilyIdByActorId.Remove(actorId);
			return;
		}

		RealmOrderByActorId.TryGetValue(actorId, out int realmOrder);
		FamilyIdByActorId.TryGetValue(actorId, out long familyId);
		RealmOrderByActorId.Remove(actorId);
		FamilyIdByActorId.Remove(actorId);
		actorIds.Remove(actorId);
		if (ActorIdSetsByCityId.TryGetValue(cityId, out HashSet<long> actorIdSet))
		{
			actorIdSet.Remove(actorId);
		}
		AdjustAggregate(cityId, realmOrder, -1);
		AdjustFamilyAggregate(cityId, familyId, realmOrder, -1);
		MarkCodexDirty();
		if (actorIds.Count == 0)
		{
			ActorIdsByCityId.Remove(cityId);
			ActorIdSetsByCityId.Remove(cityId);
			AggregateByCityId.Remove(cityId);
			RemoveFamilyAggregatesForCity(cityId);
			CandidateCityIds.Remove(cityId);
		}
	}

	private static int ResolveRealmOrder(Actor actor)
	{
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return System.Math.Max(0, XjRealmHelper.GetOrder(realmId));
	}

	private static void AdjustAggregate(long cityId, int realmOrder, int delta)
	{
		if (cityId <= 0L || delta == 0) return;
		if (!AggregateByCityId.TryGetValue(cityId, out int[] values))
		{
			values = new int[3];
			AggregateByCityId[cityId] = values;
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

	private static void AdjustFamilyAggregate(long cityId, long familyId, int realmOrder, int delta)
	{
		if (cityId <= 0L || familyId <= 0L || delta == 0) return;
		if (!FamilyAggregateByCityId.TryGetValue(cityId, out Dictionary<long, FamilyCityAccumulator> byFamily))
		{
			if (delta < 0) return;
			byFamily = new Dictionary<long, FamilyCityAccumulator>();
			FamilyAggregateByCityId.Add(cityId, byFamily);
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
		if (byFamily.Count == 0) FamilyAggregateByCityId.Remove(cityId);
	}

	private static void RemoveFamilyAggregatesForCity(long cityId)
	{
		if (cityId > 0L) FamilyAggregateByCityId.Remove(cityId);
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
