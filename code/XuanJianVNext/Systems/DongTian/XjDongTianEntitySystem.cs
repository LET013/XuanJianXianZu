using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XuanJianVNext.Data.DongTian;

namespace XuanJianVNext.Systems.DongTian;

internal static class XjDongTianEntitySystem
{
    private const float DongTianBuildingScale = 2f / 27f;
	private const int FailedPlacementRetryFrames = 1800;
	private const string TemplateId = "house_human_0";
	private const string SpritePath = "buildings/dongtian/";
	private static readonly Dictionary<string, Building> buildingsByRecordId = new Dictionary<string, Building>(StringComparer.Ordinal);
	private static readonly HashSet<string> visibleRecordIds = new HashSet<string>(StringComparer.Ordinal);
	private static readonly Dictionary<string, int> failedPlacementFrameByRecordId = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly FieldInfo visibleField = typeof(Building).GetField("is_visible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly PropertyInfo visibleProperty = typeof(Building).GetProperty("is_visible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly FieldInfo spriteDirtyField = typeof(Building).GetField("sprite_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly MethodInfo clearSpritesMethod = typeof(Building).GetMethod("clearSprites", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo checkSpriteMethod = typeof(Building).GetMethod("checkSpriteToRender", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static bool assetsReady;

	internal static void Init()
	{
		assetsReady = EnsureAssets();
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

			if (record.EntitySpawned && buildingsByRecordId.TryGetValue(record.RecordId, out Building existing))
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

			if (record.EntitySpawned || record.AnchorTileX < 0 || record.AnchorTileY < 0)
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
			asset.scale_base = new Vector3(DongTianBuildingScale, DongTianBuildingScale, DongTianBuildingScale);
			XjDongTianBuildingSafety.EnsureAssetRenderable(asset, SpritePath, SpritePath + id);
			if (cloned && ((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings).get(id) == null)
				((AssetLibrary<BuildingAsset>)(object)AssetManager.buildings).add(asset);
		}
		return ok;
	}

	private static bool TryPlace(in XjDongTianRecord record)
	{
		if (TryPlaceAt(record, record.AnchorTileX, record.AnchorTileY))
		{
			return true;
		}

		for (int radius = 1; radius <= 3; radius++)
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

	private static bool TryPlaceAt(in XjDongTianRecord record, int tileX, int tileY)
	{
		if (tileX < 0 || tileY < 0) return false;
		try
		{
			WorldTile tile = World.world?.GetTileSimple(tileX, tileY);
			if (tile?.chunk == null || tile.building != null) return false;
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

			long candidateId = ((BaseSystemData)candidate.data).id;
			string candidateAssetId = ((Asset)candidate.asset).id;
			if (record.EntityBuildingId > 0L && candidateId != record.EntityBuildingId)
				return false;
			if (!string.IsNullOrWhiteSpace(record.EntityAssetId)
				&& !string.Equals(candidateAssetId, record.EntityAssetId, StringComparison.Ordinal))
				return false;

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

		for (int dy = -2; dy <= 2; dy++)
		{
			for (int dx = -2; dx <= 2; dx++)
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
				catch
				{
				}
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
			&& string.Equals(candidateAssetId, record.EntityAssetId, StringComparison.Ordinal))
			return true;
		return !string.IsNullOrWhiteSpace(record.QiYuDongTianId)
			&& string.Equals(candidateAssetId, record.QiYuDongTianId, StringComparison.Ordinal);
	}

	private static void TrySetVisible(Building building, bool visible)
	{
		try
		{
			if (building == null) return;
			if (visibleField != null) visibleField.SetValue(building, visible);
			if (visibleProperty?.CanWrite == true) visibleProperty.SetValue(building, visible, null);
			spriteDirtyField?.SetValue(building, true);
			if (visible) checkSpriteMethod?.Invoke(building, null);
			else clearSpritesMethod?.Invoke(building, null);
			TrySetReflectedActive(building, visible);
		}
		catch
		{
		}
	}

	private static void TrySetReflectedActive(object target, bool visible)
	{
		if (target == null) return;
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		try
		{
			object gameObject = target.GetType().GetProperty("gameObject", flags)?.GetValue(target, null)
				?? target.GetType().GetField("gameObject", flags)?.GetValue(target);
			gameObject?.GetType().GetMethod("SetActive", flags, null, new[] { typeof(bool) }, null)?.Invoke(gameObject, new object[] { visible });

			string[] rendererNames = { "spriteRenderer", "sprite_renderer", "current_sprite_renderer", "renderer" };
			for (int i = 0; i < rendererNames.Length; i++)
			{
				object renderer = target.GetType().GetProperty(rendererNames[i], flags)?.GetValue(target, null)
					?? target.GetType().GetField(rendererNames[i], flags)?.GetValue(target);
				PropertyInfo enabled = renderer?.GetType().GetProperty("enabled", flags);
				if (enabled?.CanWrite == true) enabled.SetValue(renderer, visible, null);
			}
		}
		catch
		{
		}
	}

	private static bool TryDestroyBuilding(Building building)
	{
		if (building == null) return false;
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		string[] methodNames =
		{
			"removeFromWorld",
			"remove",
			"killHimself",
			"kill",
			"destroy",
			"Destroy"
		};
		for (int i = 0; i < methodNames.Length; i++)
		{
			try
			{
				MethodInfo method = building.GetType().GetMethod(methodNames[i], flags, null, Type.EmptyTypes, null);
				if (method == null) continue;
				method.Invoke(building, null);
				return true;
			}
			catch
			{
			}
		}

		try
		{
			WorldTile tile = ((BaseSimObject)building).current_tile;
			if (tile != null && ReferenceEquals(tile.building, building))
			{
				FieldInfo buildingField = tile.GetType().GetField("building", flags);
				if (buildingField != null) buildingField.SetValue(tile, null);
				PropertyInfo buildingProperty = tile.GetType().GetProperty("building", flags);
				if (buildingProperty?.CanWrite == true) buildingProperty.SetValue(tile, null, null);
			}
		}
		catch
		{
		}
		return false;
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

internal static class XjDongTianRules
{
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

	private static readonly XjDongTianCatalogEntry[] catalog =
	{
		new XjDongTianCatalogEntry("xj_dongtian_sanyang", "三阳", "照世明关", "太阳,少阳,明阳"),
		new XjDongTianCatalogEntry("xj_dongtian_sanyin", "三阴", "月沉幽府", "太阴,少阴,厥阴"),
		new XjDongTianCatalogEntry("xj_dongtian_sanlei", "三雷", "玉枢雷音", "玄雷,霄雷,元雷"),
		new XjDongTianCatalogEntry("xj_dongtian_jinde", "金德", "折锋藏宫", "兑金,逍金,齐金,库金,庚金"),
		new XjDongTianCatalogEntry("xj_dongtian_mude", "木德", "诸木会春", "角木,正木,集木,更木,保木"),
		new XjDongTianCatalogEntry("xj_dongtian_shuide", "水德", "百川归壑", "坎水,渌水,合水,府水,牝水"),
		new XjDongTianCatalogEntry("xj_dongtian_huode", "火德", "焰照离庭", "离火,灴火,并火,真火,牡火"),
		new XjDongTianCatalogEntry("xj_dongtian_tude", "土德", "镇岳坤舆", "艮土,戊土,归土,宝土,宣土"),
		new XjDongTianCatalogEntry("xj_dongtian_shierqi", "十二炁", "列炁归庭", "清炁,紫炁,真炁,邃炁,寒炁,晞炁,瑞炁,煞炁,华炁,谪炁,上仪,下仪")
	};

	internal static IReadOnlyList<XjDongTianCatalogEntry> Catalog => catalog;

	internal static string FormatCatalogSummary()
	{
		return "奇遇洞天：九座按配置间隔轮流开启，显世地标常驻，开启期至多十人可入。";
	}

	internal static string FormatDeathRateProfileSummary()
	{
		return "胎息75%，炼气60%，筑基45%，紫府30%";
	}

	internal static string FormatRewardPoolSummary()
	{
		return "真元、命数、寿元、功法、丹方、法宝、金丹金性";
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

	internal XjQiYuDongTianRewardCandidate(string rewardType, Func<string> tryApply)
	{
		RewardType = rewardType ?? string.Empty;
		TryApply = tryApply;
	}
}
