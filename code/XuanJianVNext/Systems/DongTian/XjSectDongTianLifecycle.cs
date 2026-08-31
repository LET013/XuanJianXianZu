using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.DongTian;

internal static class XjSectDongTianLifecycle
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

		if (XjQuanBingStruggleSystem.IsFinalConflict)
		{
			XjClosedCultivationGuard.MarkClosedCultivation(
				actor,
				XjQuanBingStruggleSystem.IsWithdrawnParticipant(actor));
			return;
		}

		// 普通洞天闭关必须服从宗门战争动员。权柄终局仍是更高优先级的独立
		// 高境事务；除该终局外，只要本宗处于大战，金丹就保持出关参战。
		if (XjSectWarSystem.IsActorSectInActiveWar(actor))
		{
			InterruptForSectWar(actor, currentYear);
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

		if (XjSectWarSystem.IsActorSectInActiveWar(actor))
		{
			InterruptForSectWar(actor, currentYear);
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
			|| !TryResolveSectCity(actor, out City city, out _))
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
		if (TryFindBestCityBuilding(city, storedBuildingId, homeBuildingId, out Building resolved))
		{
			return RememberRetreatBuilding(actor, resolved, out building);
		}

		return false;
	}

	internal static void ForgetActorRuntime(long actorId)
	{
		if (actorId > 0L) RetreatBuildingByActorId.Remove(actorId);
	}

	internal static void ClearRuntimeCache()
	{
		RetreatBuildingByActorId.Clear();
	}

	private static bool CanStartRetreat(Actor actor, int currentYear)
	{
		if (XjSectWarSystem.IsActorSectInActiveWar(actor))
		{
			return false;
		}

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

		return TryResolveSectCity(actor, out _, out XjSectArchiveRecord sect)
			&& HasDongTianAuthority(sect)
			&& TryResolveRetreatBuilding(actor, out _);
	}

	private static bool TryResolveSectCity(Actor actor, out City city, out XjSectArchiveRecord sect)
	{
		city = null;
		sect = null;
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member)
			|| member?.SectId <= 0L
			|| !XjSectRepository.TryGetBySectId(member.SectId, out sect)
			|| sect == null
			|| !XjSectOwnership.TryResolvePrimaryCity(sect, out city)
			|| city?.data == null)
		{
			city = null;
			sect = null;
			return false;
		}
		return true;
	}

	private static bool HasDongTianAuthority(XjSectArchiveRecord sect)
	{
		if (sect?.SectId <= 0L) return false;
		if (sect.SecretRealmId > 0L) return true;
		IReadOnlyList<XjSectMemberArchiveRecord> members = XjSectAuthorityStore.ReadMembersForSect(sect.SectId);
		for (int i = 0; i < members.Count; i++)
		{
			if (members[i] != null
				&& string.Equals(XjSectMemberRole.Normalize(members[i].Role), XjSectMemberRole.SupremeElder, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
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
			XjHighRealmDaoStateService.TickRetreat(actor, currentYear);
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

	/// <summary>
	/// 宗门战争动员：只中断日常洞天闭关，不改人物境界/道途/权柄。
	/// 退出建筑并清掉闭关无敌后，把状态置回 Ready；战争仍在时 CanStartRetreat
	/// 会持续阻止重新入洞，战争结束后的下一次年度事务才允许恢复日常闭关。
	/// </summary>
	internal static bool InterruptForSectWar(Actor actor, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive() || !IsJinDan(actor)) return false;
		// 权柄终局是比宗门战争更高优先级的高境事务。CreateWar/年度动员可能
		// 先于本角色TickAnnual触发，因此这里本身也必须守住这一边界。
		if (XjQuanBingStruggleSystem.IsFinalConflict) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenDongTianState, out string state);
		bool managedRetreatState = string.Equals(state, StateRetreat, StringComparison.Ordinal)
			|| string.Equals(state, StateTravel, StringComparison.Ordinal);
		bool closed = XjClosedCultivationGuard.IsInClosedCultivation(actor);

		// 已经完成动员、正在世界中正常行动的金丹只需要保持“战争期间不得再闭关”
		// 的门禁，不应每年重新 cancelAllBeh() 一次，否则反而会打断其当前战斗/移动。
		if (!managedRetreatState && !closed)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianNextEligibleYear, SafeAdd(Math.Max(0, currentYear), 1));
			return false;
		}

		// 只有真正处于本系统 Retreat/Travel 或闭关保护中的角色才执行一次“出关动员”。
		// MarkClosed(false) 会同步退出本系统记录的洞天建筑、撤销 peaceful/invincible 并重置AI。
		XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
		ClearRetreatBuilding(actor);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenDongTianState, StateReady);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianRetreatEndYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianNextEligibleYear, SafeAdd(Math.Max(0, currentYear), 1));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianLastProcessedYear, Math.Max(0, currentYear));
		return true;
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

	private static bool TryFindBestCityBuilding(
		City city,
		long preferredStoredId,
		long homeBuildingId,
		out Building building)
	{
		building = null;
		if (city?.buildings == null) return false;

		Building home = null;
		Building residential = null;
		Building any = null;
		// City already owns an authoritative building collection. The old fallback
		// scanned World.world.buildings up to four times for one retreat cache miss.
		// One local pass preserves the intended priority: saved target -> home ->
		// residential -> any valid building in this city.
		foreach (Building candidate in city.buildings)
		{
			if (!IsValidCityBuilding(candidate, city)) continue;
			long candidateId = ((BaseSystemData)candidate.data).id;
			if (preferredStoredId > 0L && candidateId == preferredStoredId)
			{
				building = candidate;
				return true;
			}
			if (home == null && homeBuildingId > 0L && candidateId == homeBuildingId) home = candidate;
			if (residential == null && candidate.asset != null && candidate.asset.can_units_live_here) residential = candidate;
			any ??= candidate;
		}

		building = home ?? residential ?? any;
		return building != null;
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
			&& XjCultivationPathRules.IsJinDanEquivalentRealm(realmId);
	}

	private static int SafeAdd(int year, int duration)
	{
		long value = (long)Math.Max(0, year) + Math.Max(0, duration);
		return value > int.MaxValue ? int.MaxValue : (int)value;
	}
}
