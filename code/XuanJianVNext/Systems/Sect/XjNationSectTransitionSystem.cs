using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Sect;

internal static class XjNationSectTransitionSystem
{
	private sealed class PendingTransition
	{
		internal long CityId;
		internal long ActorId;
		internal long FamilyId;
		internal int RequestedYear;
		internal float RequestedWorldTime;
		internal int RetryCount;
	}

	private static readonly Dictionary<long, PendingTransition> PendingByCity = new Dictionary<long, PendingTransition>();
	private static readonly Queue<long> PendingOrder = new Queue<long>();
	private static int MissingFounderScanCursor;
	internal static bool HasPending => PendingByCity.Count > 0;

	internal static bool RequestFromZiFuPromotion(Actor actor, int currentYear)
		=> RequestFromHighRealmPromotion(actor, currentYear);

	internal static bool RequestFromHighRealmPromotion(Actor actor, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || actor?.data == null || !actor.isAlive() || XjLongShuSystem.IsLongShu(actor)
			|| XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierZiFu) return false;

		// 帝明阳高境不再被“高境必建宗门”抢先截走。新生帝明阳把现实城镇/国家
		// 作为修行壳层；其建国只能由仙国法的受控 WorldBox bridge 执行。
		if (XjXianGuoSystem.ShouldReserveForXianGuo(actor)) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
			|| familyId <= 0L) return false;

		if (XjFamilySectContinuity.TryJoinPreferredFamilySect(actor, familyId, currentYear))
		{
			return true;
		}
		if (actor.city?.data == null) return false;

		long cityId = actor.city.data.id;
		int requestYear = NormalizeFoundingYear(actor, currentYear);
		if (cityId <= 0L) return false;
		if (XjSectRepository.TryGetByCity(actor.city, out XjSectArchiveRecord existingSect))
		{
			XjSectRepository.RepairFoundingChronology(existingSect, actor);
			return false;
		}

		float worldTime = World.world == null ? Math.Max(0, currentYear) : (float)World.world.getCurWorldTime();
		if (PendingByCity.TryGetValue(cityId, out PendingTransition existing))
		{
			if (IsEarlier(worldTime, actorId, existing))
			{
				existing.ActorId = actorId;
				existing.FamilyId = familyId;
				existing.RequestedYear = requestYear;
				existing.RequestedWorldTime = worldTime;
			}
			return true;
		}

		PendingByCity.Add(cityId, new PendingTransition
		{
			CityId = cityId,
			ActorId = actorId,
			FamilyId = familyId,
			RequestedYear = requestYear,
			RequestedWorldTime = worldTime
		});
		PendingOrder.Enqueue(cityId);
		return true;
	}

	internal static int ScanMissingSectFounders(int currentYear, int budget)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0 || budget <= 0) return 0;
		IReadOnlyList<long> actorIds = XjCultivatorCache.GetZhenRenOrHigherIds();
		if (actorIds == null || actorIds.Count == 0)
		{
			MissingFounderScanCursor = 0;
			return 0;
		}

		if (MissingFounderScanCursor < 0 || MissingFounderScanCursor >= actorIds.Count) MissingFounderScanCursor = 0;
		int processed = 0;
		int available = actorIds.Count;
		while (processed < budget && available-- > 0)
		{
			long actorId = actorIds[MissingFounderScanCursor];
			MissingFounderScanCursor = (MissingFounderScanCursor + 1) % actorIds.Count;
			processed++;
			if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier) || realmTier < XjRealmSuppression.TierZiFu) continue;
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
			if (XjXianGuoSystem.ShouldReserveForXianGuo(actor)) continue;
			if (XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord membership)
				&& membership?.SectId > 0L
				&& XjSectOwnership.TryResolve(membership.SectId, out _))
			{
				// The repair scan only fills genuinely missing sect identities. It must
				// not reinterpret an old, established family split as a fresh promotion.
				continue;
			}
			RequestFromHighRealmPromotion(actor, currentYear);
		}
		return processed;
	}

	internal static int Tick(int budget)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || budget <= 0 || PendingOrder.Count == 0) return 0;
		int processed = 0;
		int available = PendingOrder.Count;
		while (processed < budget && available-- > 0 && PendingOrder.Count > 0)
		{
			long cityId = PendingOrder.Dequeue();
			if (!PendingByCity.TryGetValue(cityId, out PendingTransition pending)) continue;
			processed++;

			if (TryResolveValid(pending, out Actor actor))
			{
				int resolvedYear = NormalizeFoundingYear(actor, Math.Max(pending.RequestedYear, ResolveLiveWorldYear()));
				if (XjFamilySectContinuity.TryJoinPreferredFamilySect(actor, pending.FamilyId, resolvedYear)
					|| (!CityAlreadyHasSect(actor.city)
						&& XjSectRepository.TryCreate(actor.city, actor, pending.FamilyId, resolvedYear, out _)))
				{
					PendingByCity.Remove(cityId);
					continue;
				}
			}

			pending.RetryCount++;
			// TryResolveValid performs the authoritative city check while resolving
			// the founder. Do not locate the city by id through World.world.cities
			// here: a retry queue must stay bounded even on very large worlds.
			if (pending.RetryCount < 8) PendingOrder.Enqueue(cityId);
			else PendingByCity.Remove(cityId);
		}
		return processed;
	}

	internal static void Clear()
	{
		PendingByCity.Clear();
		PendingOrder.Clear();
		MissingFounderScanCursor = 0;
	}

	private static bool TryResolveValid(PendingTransition pending, out Actor actor)
	{
		actor = null;
		if (pending == null || !XjActorRegistry.ResolveKnownOrWorld(pending.ActorId, out actor) || actor?.data == null || !actor.isAlive()
			|| XjLongShuSystem.IsLongShu(actor)
			|| XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierZiFu
			|| actor.city?.data == null || actor.city.data.id != pending.CityId
			|| XjXianGuoSystem.ShouldReserveForXianGuo(actor)
			) return false;
		if (!XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(pending.ActorId, out long currentFamily) || currentFamily != pending.FamilyId) return false;
		return true;
	}

	private static bool CityAlreadyHasSect(City city)
	{
		return city?.data != null && XjSectRepository.TryGetByCity(city, out _);
	}

	private static int ResolveLiveWorldYear()
	{
		return Math.Max(Math.Max(0, XjYearTracker.CurrentYear), Math.Max(0, World.world?.map_stats?.year ?? 0));
	}

	private static int NormalizeFoundingYear(Actor actor, int proposedYear)
	{
		// 新修法接入后不再把“紫府进入年”当唯一来源。正常突破用本次年份，
		// 旧档补扫优先读取当前高境的真实进入年，避免真人在数百年后补建宗门
		// 却被记成补扫当年。
		if (actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int enteredYear)
			&& enteredYear > 0)
		{
			return enteredYear;
		}
		int ziFuEnteredYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(actor);
		if (ziFuEnteredYear > 0) return ziFuEnteredYear;
		return Math.Max(1, proposedYear > 0 ? proposedYear : ResolveLiveWorldYear());
	}

	private static bool IsEarlier(float worldTime, long actorId, PendingTransition existing)
	{
		if (worldTime < existing.RequestedWorldTime) return true;
		return Math.Abs(worldTime - existing.RequestedWorldTime) < 0.0001f && actorId < existing.ActorId;
	}
}
