using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.ZongMen;

internal static class XjZongMenDongTianLifecycle
{
	private const string StateReady = "Ready";
	private const string StateRetreat = "Retreat";
	private const string StateTravel = "Travel";
	private static readonly Dictionary<long, Building> RetreatBuildingByActorId = new Dictionary<long, Building>();

	internal static void TickAnnual(Actor actor, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive() || currentYear < 0)
		{
			return;
		}

		if (XjLongShuSystem.IsLongShu(actor) || !IsJinDan(actor))
		{
			EndRetreat(actor);
			return;
		}

		if (XjQuanBingStruggleSystem.IsActive)
		{
			XjClosedCultivationGuard.MarkClosedCultivation(
				actor,
				XjQuanBingStruggleSystem.IsWithdrawnParticipant(actor));
			return;
		}

		bool settingsChanged = NeedsApplyRuntimeSettings(actor);
		if (!settingsChanged
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenDongTianLastProcessedYear, out int lastYear)
			&& lastYear >= currentYear)
		{
			return;
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianLastProcessedYear, currentYear);

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenDongTianState, out string state);
		if (settingsChanged)
		{
			ApplyRuntimeSettings(actor, currentYear, state);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenDongTianState, out state);
		}

		if (string.Equals(state, StateRetreat, StringComparison.Ordinal))
		{
			TickRetreat(actor, currentYear);
			return;
		}

		if (string.Equals(state, StateTravel, StringComparison.Ordinal))
		{
			TickTravel(actor, currentYear);
			return;
		}

		if (CanStartRetreat(actor, currentYear))
		{
			BeginRetreat(actor, currentYear);
		}
	}

	internal static bool IsRetreatActive(Actor actor)
	{
		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenDongTianState, out string state)
			&& string.Equals(state, StateRetreat, StringComparison.Ordinal);
	}

	internal static bool TryStartImmediately(Actor actor, int currentYear)
	{
		if (actor?.data == null
			|| !actor.isAlive()
			|| currentYear < 0
			|| XjLongShuSystem.IsLongShu(actor)
			|| !IsJinDan(actor))
		{
			return false;
		}

		if (IsRetreatActive(actor))
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
			return true;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenDongTianState, out string state)
			&& string.Equals(state, StateTravel, StringComparison.Ordinal))
		{
			return false;
		}

		if (!CanStartRetreat(actor, currentYear))
		{
			return false;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianLastProcessedYear, currentYear);
		return BeginRetreat(actor, currentYear);
	}

	internal static bool TryResolveRetreatBuilding(Actor actor, out Building building)
	{
		building = null;
		if (actor?.data == null
			|| !actor.isAlive()
			|| !XjZongMenCityData.TryFindActorZongMenCity(actor, out City city)
			|| !XjZongMenCityData.IsMember(city, actor))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		if (RetreatBuildingByActorId.TryGetValue(actorId, out Building cached)
			&& IsValidCityBuilding(cached, city))
		{
			building = cached;
			return true;
		}
		RetreatBuildingByActorId.Remove(actorId);

		if (actor.is_inside_building
			&& IsValidCityBuilding(actor.inside_building, city))
		{
			return RememberRetreatBuilding(actor, actor.inside_building, out building);
		}

		XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenDongTianTargetBuildingId, out long storedBuildingId);
		long homeBuildingId = actor.data.homeBuildingID;
		if (TryFindCityBuilding(city, storedBuildingId, requireResidential: false, out Building stored))
		{
			return RememberRetreatBuilding(actor, stored, out building);
		}
		if (TryFindCityBuilding(city, homeBuildingId, requireResidential: false, out Building home))
		{
			return RememberRetreatBuilding(actor, home, out building);
		}
		if (TryFindCityBuilding(city, 0L, requireResidential: true, out Building residential))
		{
			return RememberRetreatBuilding(actor, residential, out building);
		}
		if (TryFindCityBuilding(city, 0L, requireResidential: false, out Building any))
		{
			return RememberRetreatBuilding(actor, any, out building);
		}

		return false;
	}

	internal static void ClearRuntimeCache()
	{
		RetreatBuildingByActorId.Clear();
	}

	private static bool CanStartRetreat(Actor actor, int currentYear)
	{
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenDongTianNextEligibleYear, out int eligibleYear)
			&& eligibleYear > currentYear)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState authorityState)
			&& authorityState.IntegrationRetreatActive)
		{
			return false;
		}

		return XjZongMenCityData.TryFindActorZongMenCity(actor, out City city)
			&& XjZongMenCityData.IsMember(city, actor)
			&& XjZongMenCityData.HasDongTianPeak(city)
			&& TryResolveRetreatBuilding(actor, out _);
	}

	private static bool NeedsApplyRuntimeSettings(Actor actor)
	{
		return actor?.data != null
			&& (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenDongTianSettingsRevision, out int appliedRevision)
				|| appliedRevision != XjRuntimeSettings.Revision);
	}

	private static void ApplyRuntimeSettings(Actor actor, int currentYear, string state)
	{
		if (actor?.data == null)
		{
			return;
		}

		if (string.Equals(state, StateRetreat, StringComparison.Ordinal))
		{
			int duration = Math.Max(1, XjRuntimeSettings.RollJinDanDongTianCultivateYears());
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianRetreatEndYear, SafeAdd(currentYear, duration));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, 0);
			MarkRuntimeSettingsApplied(actor);
			return;
		}

		if (string.Equals(state, StateTravel, StringComparison.Ordinal))
		{
			int travelYears = Math.Max(1, XjRuntimeSettings.RollJinDanPostPeaceYears());
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, SafeAdd(currentYear, travelYears));
			MarkRuntimeSettingsApplied(actor);
			return;
		}

		MarkRuntimeSettingsApplied(actor);
	}

	private static void MarkRuntimeSettingsApplied(Actor actor)
	{
		if (actor?.data != null)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianSettingsRevision, XjRuntimeSettings.Revision);
		}
	}

	private static bool BeginRetreat(Actor actor, int currentYear)
	{
		if (!TryResolveRetreatBuilding(actor, out _))
		{
			return false;
		}

		int duration = Math.Max(1, XjRuntimeSettings.RollJinDanDongTianCultivateYears());
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenDongTianState, StateRetreat);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianRetreatEndYear, SafeAdd(currentYear, duration));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, 0);
		MarkRuntimeSettingsApplied(actor);
		XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
		return true;
	}

	private static void TickRetreat(Actor actor, int currentYear)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenDongTianRetreatEndYear, out int endYear);
		if (endYear <= 0 || currentYear < endYear)
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
			return;
		}

		XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
		ClearRetreatBuilding(actor);
		int travelYears = Math.Max(1, XjRuntimeSettings.RollJinDanPostPeaceYears());
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenDongTianState, StateTravel);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, SafeAdd(currentYear, travelYears));
		MarkRuntimeSettingsApplied(actor);
	}

	private static void TickTravel(Actor actor, int currentYear)
	{
		XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, out int endYear);
		if (endYear > 0 && currentYear < endYear)
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenDongTianState, StateReady);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianNextEligibleYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianRetreatEndYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, 0);
		if (CanStartRetreat(actor, currentYear))
		{
			BeginRetreat(actor, currentYear);
		}
	}

	private static void EndRetreat(Actor actor)
	{
		// 角色失去金丹/宗门资格时无条件清理旧闭关标记。旧逻辑只有 state=Retreat
		// 才清理，状态字段异常或读档不一致会留下永久无敌。
		if (XjClosedCultivationGuard.IsInClosedCultivation(actor))
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
		}
		ClearRetreatBuilding(actor);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenDongTianState, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianRetreatEndYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianNextEligibleYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianSettingsRevision, 0);
	}

	private static bool RememberRetreatBuilding(Actor actor, Building candidate, out Building building)
	{
		building = candidate;
		if (actor?.data == null || candidate?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		long buildingId = ((BaseSystemData)candidate.data).id;
		if (actorId <= 0L || buildingId <= 0L)
		{
			building = null;
			return false;
		}

		RetreatBuildingByActorId[actorId] = candidate;
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjZongMenDongTianTargetBuildingId, buildingId);
		return true;
	}

	private static void ClearRetreatBuilding(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L)
		{
			RetreatBuildingByActorId.Remove(actorId);
		}
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjZongMenDongTianTargetBuildingId, 0L);
	}

	private static bool TryFindCityBuilding(City city, long preferredId, bool requireResidential, out Building building)
	{
		building = null;
		if (World.world?.buildings == null)
		{
			return false;
		}

		// 仅在开始闭关或缓存失效时执行一次；命中后按角色缓存，不进入年度全量扫描。
		foreach (Building candidate in World.world.buildings)
		{
			if (!IsValidCityBuilding(candidate, city))
			{
				continue;
			}

			long candidateId = ((BaseSystemData)candidate.data).id;
			if (preferredId > 0L && candidateId != preferredId)
			{
				continue;
			}
			if (requireResidential && (candidate.asset == null || !candidate.asset.can_units_live_here))
			{
				continue;
			}

			building = candidate;
			return true;
		}

		return false;
	}

	private static bool IsValidCityBuilding(Building building, City city)
	{
		if (building?.data == null
			|| city?.data == null
			|| !building.isAlive()
			|| building.current_tile == null
			|| building.current_tile.zone == null
			|| !building.current_tile.zone.hasCity())
		{
			return false;
		}

		return building.city == city;
	}

	private static bool IsJinDan(Actor actor)
	{
		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal);
	}

	private static int SafeAdd(int year, int duration)
	{
		long value = (long)Math.Max(0, year) + Math.Max(0, duration);
		return value > int.MaxValue ? int.MaxValue : (int)value;
	}
}
