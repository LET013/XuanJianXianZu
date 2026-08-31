using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Architecture;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.DongTian;

internal static class XjDongTianEntitySystem
{
    private const float DongTianBuildingScale = 2f / 27f;
	// 落霞山与水月照真的贴图源尺寸小于常规洞天（120px 对 303px）。
	// 若继续统一套用常规缩放，永久道场在地图上只剩约三分之一大小，近乎不可见。
	private const float PermanentWorldSiteBuildingScale = 0.20f;
	private const int FailedPlacementRetryFrames = 1800;
	private const string TemplateId = "house_human_0";
	private const string SpritePath = "buildings/dongtian/";
	private static readonly Dictionary<string, Building> buildingsByRecordId = new Dictionary<string, Building>(StringComparer.Ordinal);
	private static readonly HashSet<string> visibleRecordIds = new HashSet<string>(StringComparer.Ordinal);
	private static readonly Dictionary<string, int> failedPlacementFrameByRecordId = new Dictionary<string, int>(StringComparer.Ordinal);
	private static bool assetsReady;
	private static bool lifecycleRegistered;
	private static bool postLoadRestorePending;
	private static int postLoadRestoreCursor;

	internal static void Init()
	{
		assetsReady = EnsureAssets();
		if (lifecycleRegistered) return;
		lifecycleRegistered = true;
		XjFeatureRegistration.WorldLifecycle(
			"world.dongtian-entity-restore",
			15,
			_ => SchedulePostLoadRestore(),
			Clear);
		XjFeatureRegistration.BackgroundLane(
			"background.dongtian-entity-restore",
			12,
			() => postLoadRestorePending,
			TickPostLoadRestore,
			new XjRuntimeLanePolicy(
				1,
				0.80d,
				XjRuntimeBacklogPolicy.CoalesceLatest,
				XjRuntimeYearSemantics.CurrentState));
	}

	internal static void Tick(IReadOnlyList<XjDongTianRecord> records, int budget)
	{
		if (budget <= 0 || records == null || records.Count == 0 || World.world?.buildings == null)
			return;
		if (!assetsReady) assetsReady = EnsureAssets();
		if (!assetsReady) return;

		int processed = 0;
		for (int i = 0; i < records.Count && processed < budget; i++)
		{
			XjDongTianRecord record = records[i];
			if (!record.Found)
				continue;
			if (record.EntityClosed)
			{
				if (record.EntitySpawned) MarkClosed(record.RecordId);
				continue;
			}

			if (record.EntitySpawned && buildingsByRecordId.TryGetValue(record.RecordId, out Building existing)
				&& existing?.data != null && existing.asset != null)
			{
				processed++;
				EnsureVisible(record.RecordId, existing);
				continue;
			}

			if (record.EntitySpawned && TryResolveAtAnchor(record, out Building resolved))
			{
				processed++;
				buildingsByRecordId[record.RecordId] = resolved;
				EnsureVisible(record.RecordId, resolved);
				continue;
			}

			// 存档只保存实体 ID；读取后该建筑可能已被原生清理或未正确复原。旧逻辑在
			// EntitySpawned=true 时直接跳过，令永久的落霞山/水月照真从此永远不再补图。
			if (record.EntitySpawned)
			{
				buildingsByRecordId.Remove(record.RecordId);
				visibleRecordIds.Remove(record.RecordId);
				XjDongTianRegistry.UpdateEntityState(record.RecordId, string.Empty, 0L, false, false);
			}

			if (record.AnchorTileX < 0 || record.AnchorTileY < 0)
				continue;

			processed++;
			int frame = Time.frameCount;
			if (failedPlacementFrameByRecordId.TryGetValue(record.RecordId, out int lastFailedFrame)
				&& frame - lastFailedFrame < FailedPlacementRetryFrames)
			{
				continue;
			}

			if (!TryPlace(record))
			{
				failedPlacementFrameByRecordId[record.RecordId] = frame;
			}
		}
	}

	internal static void MarkClosed(string recordId)
	{
		if (string.IsNullOrWhiteSpace(recordId)) return;
		Building building = null;
		if (!buildingsByRecordId.TryGetValue(recordId, out building)
			&& XjDongTianRegistry.TryReadRecord(recordId, out XjDongTianRecord record)
			&& (TryResolveAtAnchor(record, out Building resolved)
				|| TryResolveNearAnchor(record, out resolved)))
		{
			building = resolved;
			buildingsByRecordId[recordId] = resolved;
		}
		if (building != null)
		{
			TrySetVisible(building, false);
			TryDestroyBuilding(building);
			buildingsByRecordId.Remove(recordId);
		}
		visibleRecordIds.Remove(recordId);
		failedPlacementFrameByRecordId.Remove(recordId);
		XjDongTianRegistry.UpdateEntityState(recordId, string.Empty, 0L, false, true);
	}

	internal static void Clear()
	{
		buildingsByRecordId.Clear();
		visibleRecordIds.Clear();
		failedPlacementFrameByRecordId.Clear();
		postLoadRestorePending = false;
		postLoadRestoreCursor = 0;
	}

	/// <summary>
	/// 归档只保存洞天的逻辑记录；原生建筑缓存不会随模组记录一同存回内存。
	/// 读档后以后台小批次重新绑定/补建，不能等到下一次年度洞天流程才处理。
	/// </summary>
	private static void SchedulePostLoadRestore()
	{
		postLoadRestoreCursor = 0;
		postLoadRestorePending = XjDongTianRegistry.ReadAllEntries().Count > 0;
	}

	private static void TickPostLoadRestore()
	{
		if (!postLoadRestorePending) return;
		if (!assetsReady) assetsReady = EnsureAssets();
		if (!assetsReady || World.world?.buildings == null) return;

		IReadOnlyList<XjDongTianRecord> records = XjDongTianRegistry.ReadAllEntries();
		if (postLoadRestoreCursor >= records.Count)
		{
			postLoadRestorePending = false;
			postLoadRestoreCursor = 0;
			return;
		}

		// 每帧只恢复一处，永久道场可能需要搜索周边空地，不能在读档帧内批量扫图。
		XjDongTianRecord record = records[postLoadRestoreCursor++];
		Tick(new[] { record }, 1);
	}

	/// <summary>
	/// 取得洞天地标的实际落点。永久山门可能因锚点被道路或房屋占据而落在附近，
	/// 舆图归属必须以建筑所在格为准，不能只看最初的事件锚点。
	/// </summary>
	internal static bool TryResolveCurrentTile(in XjDongTianRecord record, out WorldTile tile)
	{
		tile = null;
		if (string.IsNullOrWhiteSpace(record.RecordId)) return false;
		if (buildingsByRecordId.TryGetValue(record.RecordId, out Building cached)
			&& cached?.current_tile != null)
		{
			tile = cached.current_tile;
			return true;
		}
		if (TryResolveAtAnchor(record, out Building atAnchor)
			|| TryResolveNearAnchor(record, out atAnchor))
		{
			buildingsByRecordId[record.RecordId] = atAnchor;
			tile = atAnchor.current_tile;
			return tile != null;
		}
		return false;
	}

	private static bool EnsureAssets()
	{
		if (AssetManager.buildings == null) return false;
		bool ok = true;
		IReadOnlyList<XjDongTianCatalogEntry> catalog = XjDongTianRules.Catalog;
		for (int i = 0; i < catalog.Count; i++)
		{
			string id = catalog[i].QiYuDongTianId;
			BuildingAsset asset = ((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings).get(id);
			bool cloned = false;
			if (asset == null)
			{
				asset = ((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings).clone(id, TemplateId);
				cloned = asset != null;
			}
			if (asset == null)
			{
				ok = false;
				continue;
			}

			((Asset)asset).id = id;
			asset.main_path = SpritePath;
			asset.sprite_path = SpritePath + id;
			// 沿用0.5.4的独立遗迹建筑边界：借用ruins资产归类，但彻底关闭
			// 王国染色、城市归属、住房、废墟和掉落链路。
			asset.kingdom = "ruins";
			asset.group = "nature";
			asset.building_type = BuildingType.Building_Civ;
			asset.type = "type_house";
			asset.has_kingdom_color = false;
			asset.random_flip = false;
			asset.city_building = false;
			asset.ignored_by_cities = true;
			asset.can_be_abandoned = false;
			asset.can_be_upgraded = false;
			asset.upgrade_to = string.Empty;
			asset.upgraded_from = string.Empty;
			asset.destroy_on_liquid = false;
			asset.removed_by_sponge = false;
			asset.spawn_units = false;
			asset.spawn_drops = false;
			asset.sparkle_effect = false;
			asset.auto_remove_ruin = false;
			asset.remove_ruins = false;
			asset.has_ruin_state = false;
			asset.has_ruins_graphics = false;
			asset.has_sprites_ruin = false;
			asset.can_be_demolished = false;
			asset.check_for_close_building = false;
			asset.tower = false;
			asset.can_be_damaged_by_tornado = false;
			asset.can_be_placed_on_liquid = true;
			asset.can_be_placed_on_blocks = false;
			asset.damaged_by_rain = false;
			asset.affected_by_lava = false;
			asset.affected_by_acid = false;
			asset.burnable = false;
			asset.prevent_freeze = true;
			asset.can_units_live_here = false;
			asset.housing_slots = 0;
			asset.housing_happiness = 0;
			asset.max_houses = 0;
			asset.can_be_living_house = false;
			asset.can_be_living_plant = false;
			asset.fundament = new BuildingFundament(0, 0, 0, 0);
			float scale = ResolveBuildingScale(id);
			asset.scale_base = new Vector3(scale, scale, scale);
			// 资源加载尚未完成时不要提前把洞天写入“已生成”状态；Tick 会在
			// 后续原生资源周期再次调用 EnsureAssets，而不是留下无贴图建筑。
			ok &= XjDongTianBuildingSafety.EnsureAssetRenderable(asset, SpritePath, SpritePath + id);
			if (cloned && ((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings).get(id) == null)
				((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings).add(asset);
		}

		// 0.9.9.8 曾误写为 xj_dontian_binggu。仅保留为读档别名，
		// 不加入洞天目录与显世循环；贴图始终读取正确的 xj_dongtian_binggu 目录。
		BuildingAsset legacyBingGu = ((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings)
			.get(XjDongTianRules.LegacyBingGuQiYuDongTianId);
		bool legacyCloned = false;
		if (legacyBingGu == null)
		{
			legacyBingGu = ((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings).clone(
				XjDongTianRules.LegacyBingGuQiYuDongTianId,
				XjDongTianRules.BingGuQiYuDongTianId);
			legacyCloned = legacyBingGu != null;
		}
		if (legacyBingGu != null)
		{
			((Asset)legacyBingGu).id = XjDongTianRules.LegacyBingGuQiYuDongTianId;
			legacyBingGu.main_path = SpritePath;
			legacyBingGu.sprite_path = SpritePath + XjDongTianRules.BingGuQiYuDongTianId;
			ok &= XjDongTianBuildingSafety.EnsureAssetRenderable(
				legacyBingGu, SpritePath, SpritePath + XjDongTianRules.BingGuQiYuDongTianId);
			if (legacyCloned && ((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings)
				.get(XjDongTianRules.LegacyBingGuQiYuDongTianId) == null)
			{
				((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings).add(legacyBingGu);
			}
		}
		return ok;
	}

	private static bool TryPlace(in XjDongTianRecord record)
	{
		if (TryPlaceAt(record, record.AnchorTileX, record.AnchorTileY))
		{
			return true;
		}

		// 永久道场的锚点常落在城镇活动范围内；只查三格会被房屋/道路完全占满，
		// 导致存档有记录却从未生成建筑。常规奇遇仍维持原来的紧邻范围。
		int maxPlacementRadius = XjDongTianRules.IsPermanentWorldSite(record.QiYuDongTianId) ? 12 : 3;
		for (int radius = 1; radius <= maxPlacementRadius; radius++)
		{
			for (int dy = -radius; dy <= radius; dy++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
					{
						continue;
					}

					if (TryPlaceAt(record, record.AnchorTileX + dx, record.AnchorTileY + dy))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static float ResolveBuildingScale(string qiYuDongTianId)
	{
		return XjDongTianRules.IsPermanentWorldSite(qiYuDongTianId)
			? PermanentWorldSiteBuildingScale
			: DongTianBuildingScale;
	}

	private static bool TryPlaceAt(in XjDongTianRecord record, int tileX, int tileY)
	{
		if (tileX < 0 || tileY < 0) return false;
		try
		{
			WorldTile tile = World.world?.GetTileSimple(tileX, tileY);
			if (tile?.chunk == null || tile.building != null
				|| XjZhantanlinSystem.IsInsideProtectedBoundary(tile)) return false;
			Building building = World.world.buildings.addBuilding(record.QiYuDongTianId, tile, true, false, BuildPlacingType.New);
			if (building?.data == null) return false;
			XjDongTianBuildingSafety.EnsureBuildingRenderable(building);
			long buildingId = ((BaseSystemData)building.data).id;
			buildingsByRecordId[record.RecordId] = building;
			TrySetVisible(building, true);
			visibleRecordIds.Add(record.RecordId);
			failedPlacementFrameByRecordId.Remove(record.RecordId);
			XjDongTianRegistry.UpdateEntityState(record.RecordId, record.QiYuDongTianId, buildingId, true, false);
			return true;
		}
		catch
		{
			// Runtime will retry on a later yearly tick.
			return false;
		}
	}

	private static bool TryResolveAtAnchor(in XjDongTianRecord record, out Building building)
	{
		building = null;
		if (record.AnchorTileX < 0 || record.AnchorTileY < 0)
			return false;

		try
		{
			WorldTile tile = World.world?.GetTileSimple(record.AnchorTileX, record.AnchorTileY);
			Building candidate = tile?.building;
			if (candidate?.data == null || candidate.asset == null)
				return false;

			// 原生存档重建建筑时 numeric ID 可能变化。资产 ID 仍是洞天身份的稳定键，
			// 因此不能因旧 ID 不一致就把锚点上的既有洞天误判为丢失实体。
			if (!IsRecordBuilding(record, candidate)) return false;

			XjDongTianBuildingSafety.EnsureBuildingRenderable(candidate);
			building = candidate;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryResolveNearAnchor(in XjDongTianRecord record, out Building building)
	{
		building = null;
		if (record.AnchorTileX < 0 || record.AnchorTileY < 0)
			return false;

		int resolveRadius = XjDongTianRules.IsPermanentWorldSite(record.QiYuDongTianId) ? 12 : 2;
		for (int dy = -resolveRadius; dy <= resolveRadius; dy++)
		{
			for (int dx = -resolveRadius; dx <= resolveRadius; dx++)
			{
				if (dx == 0 && dy == 0)
					continue;

				try
				{
					WorldTile tile = World.world?.GetTileSimple(record.AnchorTileX + dx, record.AnchorTileY + dy);
					Building candidate = tile?.building;
					if (IsRecordBuilding(record, candidate))
					{
						XjDongTianBuildingSafety.EnsureBuildingRenderable(candidate);
						building = candidate;
						return true;
					}
				}
				catch (System.Exception xjCaught294) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DongTian/XjDongTianEntitySystem.cs:294", xjCaught294); }
			}
		}
		return false;
	}

	private static void EnsureVisible(string recordId, Building building)
	{
		if (string.IsNullOrWhiteSpace(recordId) || building == null)
			return;
		if (visibleRecordIds.Contains(recordId))
			return;
		XjDongTianBuildingSafety.EnsureBuildingRenderable(building);
		TrySetVisible(building, true);
		visibleRecordIds.Add(recordId);
	}

	private static bool IsRecordBuilding(in XjDongTianRecord record, Building candidate)
	{
		if (candidate?.data == null || candidate.asset == null)
			return false;

		long candidateId = ((BaseSystemData)candidate.data).id;
		string candidateAssetId = ((Asset)candidate.asset).id;
		if (record.EntityBuildingId > 0L && candidateId == record.EntityBuildingId)
			return true;
		if (!string.IsNullOrWhiteSpace(record.EntityAssetId)
			&& string.Equals(
				XjDongTianRules.NormalizeQiYuDongTianId(candidateAssetId),
				XjDongTianRules.NormalizeQiYuDongTianId(record.EntityAssetId),
				StringComparison.Ordinal))
			return true;
		return !string.IsNullOrWhiteSpace(record.QiYuDongTianId)
			&& string.Equals(
				XjDongTianRules.NormalizeQiYuDongTianId(candidateAssetId),
				XjDongTianRules.NormalizeQiYuDongTianId(record.QiYuDongTianId),
				StringComparison.Ordinal);
	}

	private static void TrySetVisible(Building building, bool visible)
	{
		try
		{
			XjNativeBuildingInterop.SetVisible(building, visible);
		}
		catch (System.Exception xjCaught341)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report(
				"code/XuanJianVNext/Systems/DongTian/XjDongTianEntitySystem.cs:TrySetVisible",
				xjCaught341);
		}
	}

	private static bool TryDestroyBuilding(Building building)
	{
		return XjNativeBuildingInterop.TryDestroy(building);
	}

}

internal readonly struct XjDongTianCatalogEntry
{
	internal readonly string QiYuDongTianId;
	internal readonly string DaoTuGroup;
	internal readonly string DisplayName;
	internal readonly string RelatedDaoTuIds;

	internal XjDongTianCatalogEntry(string qiYuDongTianId, string daoTuGroup, string displayName, string relatedDaoTuIds)
	{
		QiYuDongTianId = qiYuDongTianId ?? string.Empty;
		DaoTuGroup = daoTuGroup ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		RelatedDaoTuIds = relatedDaoTuIds ?? string.Empty;
	}
}

internal readonly struct XjDongTianRewardProfile
{
	internal readonly string SpecialtySummary;
	internal readonly string RewardPoolSummary;
	internal readonly float DeathMultiplier;
	internal readonly float ZhenYuanMultiplier;
	internal readonly float MingShuMultiplier;
	internal readonly float SecondRewardChance;
	internal readonly float AlchemyRecipeChance;
	internal readonly float HuiGuangChance;
	internal readonly float GongFaChanceMultiplier;
	internal readonly float FaBaoChance;
	internal readonly float JinXingChance;
	internal readonly float SecretRealmMethodChance;
	internal readonly float LingWuChance;
	internal readonly float CaiQiChance;
	internal readonly float LifespanChance;
	internal readonly float FormationMaterialChance;

	internal XjDongTianRewardProfile(
		string specialtySummary,
		string rewardPoolSummary,
		float deathMultiplier,
		float zhenYuanMultiplier,
		float mingShuMultiplier,
		float secondRewardChance,
		float alchemyRecipeChance,
		float huiGuangChance,
		float gongFaChanceMultiplier,
		float faBaoChance,
		float jinXingChance,
		float secretRealmMethodChance,
		float lingWuChance,
		float caiQiChance,
		float lifespanChance,
		float formationMaterialChance)
	{
		SpecialtySummary = specialtySummary ?? string.Empty;
		RewardPoolSummary = rewardPoolSummary ?? string.Empty;
		DeathMultiplier = Math.Clamp(deathMultiplier, 0.5f, 1.5f);
		ZhenYuanMultiplier = Math.Max(0.1f, zhenYuanMultiplier);
		MingShuMultiplier = Math.Max(0.1f, mingShuMultiplier);
		SecondRewardChance = Math.Clamp(secondRewardChance, 0f, 0.85f);
		AlchemyRecipeChance = Math.Clamp(alchemyRecipeChance, 0f, 0.85f);
		HuiGuangChance = Math.Clamp(huiGuangChance, 0f, 0.85f);
		GongFaChanceMultiplier = Math.Max(0.1f, gongFaChanceMultiplier);
		FaBaoChance = Math.Clamp(faBaoChance, 0f, 0.50f);
		JinXingChance = Math.Clamp(jinXingChance, 0f, 0.10f);
		SecretRealmMethodChance = Math.Clamp(secretRealmMethodChance, 0f, 0.85f);
		LingWuChance = Math.Clamp(lingWuChance, 0f, 0.50f);
		CaiQiChance = Math.Clamp(caiQiChance, 0f, 0.85f);
		LifespanChance = Math.Clamp(lifespanChance, 0f, 0.50f);
		FormationMaterialChance = Math.Clamp(formationMaterialChance, 0f, 0.85f);
	}
}

internal static class XjDongTianRules
{
	internal const string BingGuQiYuDongTianId = "xj_dongtian_binggu";
	internal const string LegacyBingGuQiYuDongTianId = "xj_dontian_binggu";
	internal const string YuanZhaoDongTianId = "xj_dongtian_yuanzhao";
	internal const string LuoXiaShanDongTianId = "xj_dongtian_luoxiashan";
	internal const int OpenDurationYears = 5;
	internal const int MaxExploreActors = 10;
	internal const int LocalExploreRadius = 64;
	internal const int ExpandedExploreRadius = 128;
	internal const int MaxExploreRadius = 256;
	internal const int QiYuDongTianMinAnchorDistance = 128;
	internal const int QiYuDongTianRelaxedAnchorDistance = 64;

	internal const float DeathChanceTaiXi = 0.75f;
	internal const float DeathChanceLianQi = 0.60f;
	internal const float DeathChanceZhuJi = 0.45f;
	internal const float DeathChanceZiFu = 0.30f;

	internal const float SixGradeGongFaChance = 0.01f;
	internal const float FiveGradeGongFaChance = 0.16f;
	internal const float FourGradeGongFaChance = 0.46f;
	internal const int RewardCandidateRetryCount = 2;
	internal const float ZhenYuanTopChance = 0.05f;
	internal const float ZhenYuanMidChance = 0.10f;
	internal const float ZhenYuanLowChance = 0.30f;
	internal const float ZhenYuanBaseChance = 0.50f;
	internal const float ZhenYuanTopAmount = 20000f;
	internal const float ZhenYuanMidAmount = 15000f;
	internal const float ZhenYuanLowAmount = 10000f;
	internal const float ZhenYuanBaseAmount = 5000f;
	internal const float MingShuTopChance = 0.05f;
	internal const float MingShuMidChance = 0.20f;
	internal const float MingShuLowChance = 0.40f;
	internal const float MingShuBaseChance = 0.60f;
	internal const float MingShuTopAmount = 80f;
	internal const float MingShuMidAmount = 60f;
	internal const float MingShuLowAmount = 40f;
	internal const float MingShuBaseAmount = 20f;
	internal const float SecondRewardChance = 0.30f;
	internal const float FaBaoChance = 0.02f;
	internal const float JinXingChance = 0.001f;
	internal const float AlchemyRecipeChance = 0.18f;
	internal const float RelatedDaoTuDeathMultiplier = 0.80f;
	internal const float RelatedDaoTuRareRewardMultiplier = 1.25f;

	private static readonly XjDongTianCatalogEntry[] catalog =
	{
		new XjDongTianCatalogEntry("xj_dongtian_sanyang", "三阳", "照世明关", "太阳,少阳,明阳"),
		new XjDongTianCatalogEntry("xj_dongtian_sanyin", "三阴", "月沉幽府", "太阴,少阴,厥阴"),
		new XjDongTianCatalogEntry("xj_dongtian_sanlei", "三雷", "玉枢雷音", "玄雷,霄雷,元雷"),
		new XjDongTianCatalogEntry("xj_dongtian_jinde", "金德", "折锋藏宫", "兑金,逍金,齐金,库金,庚金,长庚"),
		new XjDongTianCatalogEntry("xj_dongtian_mude", "木德", "诸木会春", "角木,正木,集木,更木,保木"),
		new XjDongTianCatalogEntry("xj_dongtian_shuide", "水德", "百川归壑", "坎水,渌水,合水,府水,牝水"),
		new XjDongTianCatalogEntry("xj_dongtian_huode", "火德", "焰照离庭", "离火,灴火,并火,真火,牡火"),
		new XjDongTianCatalogEntry("xj_dongtian_tude", "土德", "镇岳坤舆", "艮土,戊土,归土,宝土,宣土"),
		new XjDongTianCatalogEntry("xj_dongtian_shierqi", "十二炁", "列炁归庭", "清炁,紫炁,真炁,邃炁,寒炁,晞炁,瑞炁,煞炁,华炁,谪炁,上仪,下仪"),
		new XjDongTianCatalogEntry(BingGuQiYuDongTianId, "并古", "万古同庭", "鸺葵,上巫,玉真,衡祝,青宣,全丹,执孛,司天,都卫"),
		// 渊照与落霞山都是长期道场，只借既有洞天实体层显化，不参与普通五年奇遇轮换。
		new XjDongTianCatalogEntry(YuanZhaoDongTianId, "渊照", "水月照真", "渊照,太阴,坎水"),
		new XjDongTianCatalogEntry(LuoXiaShanDongTianId, "虹霞", "落霞山", "虹霞,戊土")
	};

	internal static IReadOnlyList<XjDongTianCatalogEntry> Catalog => catalog;

	internal static bool IsYuanZhaoDongTian(string qiYuDongTianId)
	{
		return string.Equals(NormalizeQiYuDongTianId(qiYuDongTianId), YuanZhaoDongTianId, StringComparison.Ordinal);
	}

	internal static bool IsLuoXiaShanDongTian(string qiYuDongTianId)
	{
		return string.Equals(NormalizeQiYuDongTianId(qiYuDongTianId), LuoXiaShanDongTianId, StringComparison.Ordinal);
	}

	internal static bool IsPermanentWorldSite(string qiYuDongTianId)
	{
		return IsYuanZhaoDongTian(qiYuDongTianId) || IsLuoXiaShanDongTian(qiYuDongTianId);
	}

	internal static string NormalizeQiYuDongTianId(string qiYuDongTianId)
	{
		string id = (qiYuDongTianId ?? string.Empty).Trim();
		return string.Equals(id, LegacyBingGuQiYuDongTianId, StringComparison.Ordinal)
			? BingGuQiYuDongTianId
			: id;
	}

	internal static bool TryResolveCatalogEntry(string qiYuDongTianId, out XjDongTianCatalogEntry entry)
	{
		string id = NormalizeQiYuDongTianId(qiYuDongTianId);
		for (int i = 0; i < catalog.Length; i++)
		{
			if (!string.Equals(catalog[i].QiYuDongTianId, id, StringComparison.Ordinal)) continue;
			entry = catalog[i];
			return true;
		}
		entry = default;
		return false;
	}

	internal static XjDongTianRewardProfile GetRewardProfile(string qiYuDongTianId)
	{
		return NormalizeQiYuDongTianId(qiYuDongTianId) switch
		{
			"xj_dongtian_sanyang" => new XjDongTianRewardProfile(
				"显照破妄，命数与寿元机缘更盛",
				"命数、寿元、三阳灵物；兼有功法、丹方与真元",
				0.95f, 1.00f, 1.65f, 0.35f, 0.18f, 0.55f, 1.00f, 0.02f, 0.001f, 0.08f, 0.04f, 0.04f, 0.06f, 0.03f),
			"xj_dongtian_sanyin" => new XjDongTianRewardProfile(
				"敛魂藏魄，道慧、命数与灵物机缘更盛",
				"道慧、命数、三阴灵物；兼有寿元与功法",
				0.90f, 0.90f, 1.30f, 0.40f, 0.16f, 0.80f, 0.90f, 0.015f, 0.0005f, 0.06f, 0.05f, 0.04f, 0.04f, 0.02f),
			"xj_dongtian_sanlei" => new XjDongTianRewardProfile(
				"雷霆洗炼，真元、功法与法宝机缘更盛，凶险略高",
				"真元、雷道功法、法宝、三雷灵物与先天之气",
				1.10f, 1.50f, 1.00f, 0.35f, 0.12f, 0.55f, 1.30f, 0.05f, 0.001f, 0.05f, 0.05f, 0.08f, 0.01f, 0.04f),
			"xj_dongtian_jinde" => new XjDongTianRewardProfile(
				"收锋藏器，法宝、金性与金德灵物机缘更盛",
				"法宝、金丹金性、金德灵物、功法与阵器资材",
				1.05f, 1.00f, 1.00f, 0.35f, 0.14f, 0.50f, 1.20f, 0.10f, 0.015f, 0.05f, 0.07f, 0.08f, 0.01f, 0.08f),
			"xj_dongtian_mude" => new XjDongTianRewardProfile(
				"生机荣发，丹方、寿元与木德灵物机缘更盛",
				"丹方、寿元、木德灵物；兼有命数与真元",
				0.82f, 0.85f, 1.00f, 0.40f, 0.45f, 0.55f, 0.85f, 0.015f, 0.0005f, 0.06f, 0.06f, 0.04f, 0.10f, 0.04f),
			"xj_dongtian_shuide" => new XjDongTianRewardProfile(
				"百川归藏，真元、先天之气与双重机缘更盛",
				"真元、先天之气、水德灵物与双重奖励",
				0.88f, 1.40f, 1.00f, 0.55f, 0.14f, 0.55f, 1.00f, 0.02f, 0.001f, 0.06f, 0.05f, 0.10f, 0.03f, 0.04f),
			"xj_dongtian_huode" => new XjDongTianRewardProfile(
				"炼火明神，功法、法宝与真元机缘更盛，凶险较高",
				"火道功法、法宝、真元、火德灵物与双重奖励",
				1.12f, 1.30f, 1.00f, 0.45f, 0.12f, 0.55f, 1.45f, 0.07f, 0.002f, 0.05f, 0.06f, 0.06f, 0.01f, 0.04f),
			"xj_dongtian_tude" => new XjDongTianRewardProfile(
				"厚土承载，洞天营造法与阵法资材机缘更盛",
				"洞天营造法、阵旗阵纹、灵材、土德灵物",
				0.78f, 0.85f, 1.00f, 0.35f, 0.12f, 0.50f, 0.85f, 0.015f, 0.0005f, 0.30f, 0.07f, 0.03f, 0.02f, 0.35f),
			"xj_dongtian_shierqi" => new XjDongTianRewardProfile(
				"列炁朝元，先天之气、功法与道慧机缘更盛",
				"先天之气、十二炁功法、道慧、灵物与双重奖励",
				0.95f, 1.10f, 1.10f, 0.45f, 0.14f, 0.65f, 1.55f, 0.03f, 0.001f, 0.10f, 0.06f, 0.30f, 0.03f, 0.06f),
			BingGuQiYuDongTianId => new XjDongTianRewardProfile(
				"九道古意同庭并世，灵物、古法残章与多重机缘更盛",
				"并古灵物、古法残章、道慧、命数、寿元与多重奖励",
				0.92f, 1.10f, 1.30f, 0.60f, 0.18f, 0.70f, 1.45f, 0.035f, 0.002f, 0.12f, 0.10f, 0.42f, 0.04f, 0.12f),
			YuanZhaoDongTianId => new XjDongTianRewardProfile(
				"道尊私域，水月为门；无传召者纵有高境，也只见一片虚影",
				"受召者所遇随因缘而异：传人点法、源道问疑、权柄来路，皆可映在水月之中",
				0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
			LuoXiaShanDongTianId => new XjDongTianRewardProfile(
				"群霞所宗，灵机冠绝；日月交替时第一缕霞光自山中而出，山门深闭，非受召者难窥真境",
				"落霞择徒重在道行与师承，外人无由以寻常寻幽之法取宝",
				0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
			_ => new XjDongTianRewardProfile(
				"道途机缘尚未细分",
				"真元、命数、功法、丹方、法宝及少量稀有产出",
				1.00f, 1.00f, 1.00f, SecondRewardChance, AlchemyRecipeChance, 0.55f, 1.00f, FaBaoChance, JinXingChance, 0.08f, 0.03f, 0.04f, 0.02f, 0.03f)
		};
	}

	internal static bool IsRelatedDaoTu(string relatedDaoTuIds, string daoTuOrRoot)
	{
		return IsRelatedDaoTu(string.Empty, relatedDaoTuIds, daoTuOrRoot);
	}

	internal static bool IsRelatedDaoTu(string qiYuDongTianId, string relatedDaoTuIds, string daoTuOrRoot)
	{
		if (string.IsNullOrWhiteSpace(relatedDaoTuIds) || string.IsNullOrWhiteSpace(daoTuOrRoot)) return false;
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition actorDaoTu)) return false;
		string[] parts = relatedDaoTuIds.Split(new[] { ',', '，', '/', '、' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			if (!XjDaoTuCatalog.TryResolve(parts[i], out XjDaoTuDefinition related)) continue;
			if (IsYuanZhaoDongTian(qiYuDongTianId))
			{
				// 水月照真只认渊照本身以及创道所据的太阴、坎水。
				// 不能沿三阴/水德 RootId 扩散到少阴、府水等整组道途。
				if (string.Equals(actorDaoTu.DisplayName, related.DisplayName, StringComparison.Ordinal)) return true;
				continue;
			}
			if (string.Equals(actorDaoTu.RootId, related.RootId, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	internal static string FormatCatalogSummary()
	{
		return "奇遇洞天：十座常规洞天优先从未显者中随机显世，全部显世后继续随机轮换且不连续重复；每座开启五年，地标常驻，开启期至多十人可入。水月照真为渊照道尊私域，只承接【水月传召】；落霞山则是虹霞道统上宗，作为背景山门地标与师承世界锚点存在，不属于奇遇洞天，也不参与探索、属地争夺或宗门远征。";
	}

	internal static string FormatDeathRateProfileSummary(string qiYuDongTianId)
	{
		XjDongTianRewardProfile profile = GetRewardProfile(qiYuDongTianId);
		if (IsYuanZhaoDongTian(qiYuDongTianId))
		{
			return "水月私域：无传召不得入；受邀者只以一念映入水月，不以肉身涉险，道尊真身亦不因此临世。";
		}
		if (IsLuoXiaShanDongTian(qiYuDongTianId))
		{
			return "落霞山门不向外人开寻幽门户；无师承牵引者不得循常法入山，自然也无所谓闯山寻宝之险。";
		}
		return "胎息" + DescribeExplorationRisk(DeathChanceTaiXi * profile.DeathMultiplier)
			+ "，炼气" + DescribeExplorationRisk(DeathChanceLianQi * profile.DeathMultiplier)
			+ "，筑基" + DescribeExplorationRisk(DeathChanceZhuJi * profile.DeathMultiplier)
			+ "，紫府/真人" + DescribeExplorationRisk(DeathChanceZiFu * profile.DeathMultiplier)
			+ "；同道途修士更易避开本门洞天杀机。";
	}

	internal static string FormatRewardPoolSummary(string qiYuDongTianId)
	{
		return GetRewardProfile(qiYuDongTianId).RewardPoolSummary;
	}

	internal static string FormatDongTianSummary(in XjDongTianCatalogEntry entry)
	{
		XjDongTianRewardProfile profile = GetRewardProfile(entry.QiYuDongTianId);
		return entry.DisplayName + "（" + entry.DaoTuGroup + "）·" + profile.SpecialtySummary;
	}

	private static string DescribeExplorationRisk(float value)
	{
		float risk = Math.Clamp(value, 0.01f, 0.95f);
		if (risk < 0.08f) return "风险甚微";
		if (risk < 0.18f) return "风险偏低";
		if (risk < 0.32f) return "颇为凶险";
		if (risk < 0.50f) return "九死一生";
		return "近乎绝地";
	}
}

internal readonly struct XjQiYuDongTianActivityAnchor
{
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly int TileX;
	internal readonly int TileY;
	internal readonly long CityId;
	internal readonly string CityName;
	internal readonly int Year;
	internal readonly string Source;

	internal XjQiYuDongTianActivityAnchor(long actorId, string actorName, int tileX, int tileY, long cityId, string cityName, int year, string source)
	{
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		TileX = tileX;
		TileY = tileY;
		CityId = cityId < 0L ? 0L : cityId;
		CityName = cityName ?? string.Empty;
		Year = year < 0 ? 0 : year;
		Source = source ?? string.Empty;
	}
}

internal readonly struct XjQiYuDongTianExplorerCandidate
{
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string RealmId;
	internal readonly string RealmDisplay;
	internal readonly string DaoTu;
	internal readonly int Distance;
	internal readonly int Weight;

	internal XjQiYuDongTianExplorerCandidate(long actorId, string actorName, string realmId, string realmDisplay, string daoTu, int distance, int weight)
	{
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		RealmId = realmId ?? string.Empty;
		RealmDisplay = realmDisplay ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		Distance = distance < 0 ? 0 : distance;
		Weight = weight <= 0 ? 1 : weight;
	}
}

internal sealed class XjQiYuDongTianRewardCandidate
{
	internal readonly string RewardType;
	internal readonly Func<string> TryApply;
	internal readonly int Priority;

	internal XjQiYuDongTianRewardCandidate(string rewardType, Func<string> tryApply, int priority = 0)
	{
		RewardType = rewardType ?? string.Empty;
		TryApply = tryApply;
		Priority = Math.Clamp(priority, 0, 3);
	}
}
