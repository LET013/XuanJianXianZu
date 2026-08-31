using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.LongShu;

/// <summary>
/// 龙属专属海上洞天。沧溟鳞宫是世界级单例建筑：首位龙属金丹出现时，
/// 在远离陆地的海面生成并永久显世，不提供奇遇入口。所有龙属金丹共享
/// 此洞天闭关；闭关 490 年、游历 10 年为龙属写死规则。
/// </summary>
internal static class XjLongShuDongTianSystem
{
	internal const string DongTianName = "沧溟鳞宫";
	internal const int RetreatYears = 490;
	internal const int TravelYears = 10;
	private const string AssetId = "xj_dongtian_longshu";
	private const string TemplateId = "house_human_0";
	private const string StateReady = "Ready";
	private const string StateRetreat = "Retreat";
	private const string StateTravel = "Travel";
	private const float BuildingScale = 2f / 27f;

	// 世界级共享缓存。仅在缓存失效/读档恢复时扫描一次建筑集合，年度运行不做全图查询。
	private static Building _sharedBuilding;
	private static long _sharedBuildingId;
	private static int _sharedAnchorX = -1;
	private static int _sharedAnchorY = -1;
	private static int _nextAnchorSearchYear = int.MinValue;
	private static bool _assetReady;

	internal static void Init()
	{
		_assetReady = EnsureAsset();
	}

	internal static void Clear()
	{
		_sharedBuilding = null;
		_sharedBuildingId = 0L;
		_sharedAnchorX = -1;
		_sharedAnchorY = -1;
		_nextAnchorSearchYear = int.MinValue;
		_assetReady = false;
	}

	internal static bool EnsureForJinDan(Actor actor, int currentYear)
	{
		if (!IsEligible(actor))
		{
			return false;
		}

		if (!_assetReady)
		{
			_assetReady = EnsureAsset();
		}
		if (!_assetReady)
		{
			return false;
		}

		if (!TryResolveRetreatBuilding(actor, out Building building))
		{
			if (!EnsureSharedAnchor(actor, currentYear, out WorldTile tile))
			{
				return false;
			}

			try
			{
				building = World.world?.buildings?.addBuilding(AssetId, tile, true, false, BuildPlacingType.New);
			}
			catch
			{
				building = null;
			}
			if (!IsValidBuilding(building))
			{
				return false;
			}

			XjDongTianBuildingSafety.EnsureBuildingRenderable(building);
			RememberSharedBuilding(building);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjLongShuDongTianCreatedYear, Math.Max(1, currentYear));
		}

		SyncActorToSharedBuilding(actor, building);
		if (XjQuanBingStruggleSystem.IsFinalConflict)
		{
			XjClosedCultivationGuard.MarkClosedCultivation(
				actor,
				XjQuanBingStruggleSystem.IsWithdrawnParticipant(actor));
			return true;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenDongTianState, out string state);
		if (string.IsNullOrWhiteSpace(state) || string.Equals(state, StateReady, StringComparison.Ordinal))
		{
			BeginRetreat(actor, Math.Max(1, currentYear));
		}
		else if (string.Equals(state, StateRetreat, StringComparison.Ordinal))
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
		}
		return true;
	}

	internal static void TickAnnual(Actor actor, int currentYear)
	{
		if (!IsEligible(actor) || currentYear <= 0)
		{
			return;
		}

		if (XjQuanBingStruggleSystem.IsFinalConflict)
		{
			EnsureForJinDan(actor, currentYear);
			XjClosedCultivationGuard.MarkClosedCultivation(
				actor,
				XjQuanBingStruggleSystem.IsWithdrawnParticipant(actor));
			return;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenDongTianLastProcessedYear, out int lastYear)
			&& lastYear >= currentYear)
		{
			return;
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianLastProcessedYear, currentYear);

		if (!EnsureForJinDan(actor, currentYear))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenDongTianState, out string state);
		if (string.Equals(state, StateRetreat, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenDongTianRetreatEndYear, out int endYear);
			if (endYear <= 0)
			{
				BeginRetreat(actor, currentYear);
				return;
			}
			if (currentYear < endYear)
			{
				XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
				XjHighRealmDaoStateService.TickRetreat(actor, currentYear);
				return;
			}

			XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenDongTianState, StateTravel);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, SafeAdd(currentYear, TravelYears));
			return;
		}

		if (string.Equals(state, StateTravel, StringComparison.Ordinal))
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, out int endYear);
			if (endYear > currentYear)
			{
				return;
			}
			BeginRetreat(actor, currentYear);
			return;
		}

		BeginRetreat(actor, currentYear);
	}

	internal static bool TryResolveRetreatBuilding(Actor actor, out Building building)
	{
		building = null;
		if (!IsEligible(actor))
		{
			return false;
		}

		if (IsValidBuilding(_sharedBuilding))
		{
			building = _sharedBuilding;
			SyncActorToSharedBuilding(actor, building);
			return true;
		}

		_sharedBuilding = null;
		_sharedBuildingId = 0L;

		// 优先读取角色保存的坐标和建筑 ID。每位龙属金丹都保存同一份锚点，
		// 因而无需在正常年度处理中遍历全世界建筑。
		if (TryResolveActorStoredBuilding(actor, out Building stored))
		{
			RememberSharedBuilding(stored);
			SyncActorToSharedBuilding(actor, stored);
			building = stored;
			return true;
		}

		// 旧存档恢复同样只走高境索引：所有龙属金丹共用同一建筑，
		// 只需从已登记金丹的持久化锚点中恢复。禁止为兼容旧档扫描
		// World.world.buildings，否则大世界读档会产生与建筑总量相关的峰值。
		if (TryRecoverSharedBuildingFromIndexedJinDan(out Building recovered))
		{
			RememberSharedBuilding(recovered);
			SyncActorToSharedBuilding(actor, recovered);
			building = recovered;
			return true;
		}

		return false;
	}

	private static void BeginRetreat(Actor actor, int currentYear)
	{
		if (!TryResolveRetreatBuilding(actor, out Building building))
		{
			return;
		}

		SyncActorToSharedBuilding(actor, building);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenDongTianState, StateRetreat);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianRetreatEndYear, SafeAdd(currentYear, RetreatYears));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianTravelEndYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenDongTianNextEligibleYear, 0);
		XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
	}

	private static bool EnsureSharedAnchor(Actor actor, int currentYear, out WorldTile tile)
	{
		tile = null;
		if (_sharedAnchorX >= 0 && _sharedAnchorY >= 0)
		{
			WorldTile shared = World.world?.GetTileSimple(_sharedAnchorX, _sharedAnchorY);
			if (IsAvailableRemoteSeaTile(shared))
			{
				tile = shared;
				return true;
			}
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLongShuDongTianAnchorX, out int storedX);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLongShuDongTianAnchorY, out int storedY);
		WorldTile stored = World.world?.GetTileSimple(storedX, storedY);
		if (IsAvailableRemoteSeaTile(stored))
		{
			RememberSharedAnchor(stored);
			tile = stored;
			return true;
		}

		// 极小地图或纯陆地图可能暂时没有满足条件的远海锚点。失败后按
		// 世界级冷却重试，禁止每名龙属金丹每年重复执行数万次邻域检查。
		if (_nextAnchorSearchYear > currentYear)
		{
			return false;
		}

		long seed = GetActorId(actor) + Math.Max(1, currentYear) * 7919L;
		if (!XjLongShuSystem.TryFindRemoteSeaTile(seed, out tile))
		{
			_nextAnchorSearchYear = SafeAdd(Math.Max(1, currentYear), 5);
			return false;
		}

		_nextAnchorSearchYear = int.MinValue;
		RememberSharedAnchor(tile);
		return true;
	}

	private static bool TryResolveActorStoredBuilding(Actor actor, out Building building)
	{
		building = null;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLongShuDongTianAnchorX, out int x);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLongShuDongTianAnchorY, out int y);
		XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjLongShuDongTianBuildingId, out long storedId);
		WorldTile tile = World.world?.GetTileSimple(x, y);
		Building candidate = tile?.building;
		if (!IsValidBuilding(candidate))
		{
			return false;
		}

		long candidateId = ((BaseSystemData)candidate.data).id;
		if (storedId > 0L && candidateId != storedId)
		{
			return false;
		}

		building = candidate;
		return true;
	}

	private static bool TryRecoverSharedBuildingFromIndexedJinDan(out Building building)
	{
		building = null;
		var ids = XjCultivatorCache.GetJinDanIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjActorRegistry.Resolve(ids[i], out Actor candidateActor)
				|| candidateActor == null
				|| !IsEligible(candidateActor))
			{
				continue;
			}

			if (TryResolveActorStoredBuilding(candidateActor, out Building candidate))
			{
				building = candidate;
				return true;
			}
		}
		return false;
	}

	private static void RememberSharedBuilding(Building building)
	{
		if (!IsValidBuilding(building))
		{
			return;
		}

		XjDongTianBuildingSafety.EnsureBuildingRenderable(building);
		_sharedBuilding = building;
		_sharedBuildingId = ((BaseSystemData)building.data).id;
		RememberSharedAnchor(building.current_tile);
	}

	private static void RememberSharedAnchor(WorldTile tile)
	{
		if (tile == null)
		{
			return;
		}
		_sharedAnchorX = tile.pos.x;
		_sharedAnchorY = tile.pos.y;
	}

	private static bool EnsureAsset()
	{
		if (AssetManager.buildings == null)
		{
			return false;
		}
		try
		{
			AssetLibrary<BuildingAsset> library = (AssetLibrary<BuildingAsset>)(object)AssetManager.buildings;
			BuildingAsset asset = library.get(AssetId);
			bool cloned = false;
			if (asset == null)
			{
				asset = library.clone(AssetId, TemplateId);
				cloned = asset != null;
			}
			if (asset == null)
			{
				return false;
			}

			((Asset)asset).id = AssetId;
			asset.main_path = "buildings/dongtian/";
			asset.sprite_path = "buildings/dongtian/" + AssetId;
			asset.kingdom = "ruins";
			asset.city_building = false;
			asset.ignored_by_cities = true;
			asset.can_be_abandoned = false;
			asset.can_be_upgraded = false;
			asset.destroy_on_liquid = false;
			asset.spawn_units = false;
			asset.spawn_drops = false;
			asset.auto_remove_ruin = false;
			asset.can_be_demolished = false;
			asset.check_for_close_building = false;
			asset.burnable = false;
			asset.can_units_live_here = true;
			asset.max_houses = 1;
			asset.scale_base = new Vector3(BuildingScale, BuildingScale, BuildingScale);
			XjDongTianBuildingSafety.EnsureAssetRenderable(asset, "buildings/dongtian/", "buildings/dongtian/" + AssetId);
			if (cloned && library.get(AssetId) == null)
			{
				library.add(asset);
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsEligible(Actor actor)
	{
		return XjSafeCore.IsAliveActor(actor)
			&& XjLongShuSystem.IsLongShu(actor)
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal);
	}

	private static bool IsValidBuilding(Building building)
	{
		return building?.data != null
			&& building.isAlive()
			&& building.current_tile != null
			&& building.asset != null
			&& string.Equals(((Asset)building.asset).id, AssetId, StringComparison.Ordinal);
	}

	private static bool IsAvailableRemoteSeaTile(WorldTile tile)
	{
		return tile != null
			&& tile.chunk != null
			&& tile.Type != null
			&& !tile.hasBuilding()
			&& XjLongShuSystem.IsRemoteSeaAnchor(tile);
	}

	private static void SyncActorToSharedBuilding(Actor actor, Building building)
	{
		if (actor?.data == null || !IsValidBuilding(building))
		{
			return;
		}

		RememberSharedBuilding(building);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjLongShuDongTianBuildingId, _sharedBuildingId);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjZongMenDongTianTargetBuildingId, _sharedBuildingId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjLongShuDongTianAnchorX, _sharedAnchorX);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjLongShuDongTianAnchorY, _sharedAnchorY);
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}


	private static int SafeAdd(int year, int duration)
	{
		long value = (long)Math.Max(0, year) + Math.Max(0, duration);
		return value > int.MaxValue ? int.MaxValue : (int)value;
	}
}
