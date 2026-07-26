using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace XuanJianVNext.Systems.DongTian;

internal static class XjDongTianBuildingSafety
{
	private static readonly MethodInfo LoadSpritesMethod = typeof(BuildingAsset).GetMethod("loadBuildingSprites", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo ClearSpritesMethod = typeof(Building).GetMethod("clearSprites", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo CheckSpriteMethod = typeof(Building).GetMethod("checkSpriteToRender", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly FieldInfo SpriteDirtyField = typeof(Building).GetField("sprite_dirty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly HashSet<BuildingMapIcon> DongTianMapIcons = new HashSet<BuildingMapIcon>();
	private static readonly Dictionary<object, bool> QuantumDrawBypassCache = new Dictionary<object, bool>();

	internal static bool EnsureAssetRenderable(BuildingAsset asset, string mainPath, string spritePath)
	{
		if (asset == null)
		{
			return false;
		}

		try
		{
			if (!string.IsNullOrWhiteSpace(mainPath)) asset.main_path = mainPath;
			if (!string.IsNullOrWhiteSpace(spritePath)) asset.sprite_path = spritePath;
			DisableBuildingRecolor(asset);
			DisableRuinAndRandomizedSpritePaths(asset);
			DisableBuildingMapIcon(asset);
			asset.sprites_are_initiated = false;
			LoadSpritesMethod?.Invoke(asset, null);
			// loadBuildingSprites 可能从克隆模板重新带回地图图标、王国色与废墟分支，
			// 因而加载后再做一次幂等清理。
			DisableBuildingRecolor(asset);
			DisableRuinAndRandomizedSpritePaths(asset);
			DisableBuildingMapIcon(asset);
			SanitizeSpriteReferences(asset);
			asset.has_sprites_main = true;
			asset.sprites_are_initiated = true;
			return true;
		}
		catch
		{
			try
			{
				asset.sprites_are_initiated = false;
			}
			catch
			{
			}
			return false;
		}
	}

	internal static bool ShouldBypassQuantumDraw(object quantumAsset)
	{
		if (quantumAsset == null) return false;
		if (QuantumDrawBypassCache.TryGetValue(quantumAsset, out bool cached)) return cached;
		bool bypass = false;
		try
		{
			bypass = ContainsXuanJianDongTianReference(quantumAsset, 0, new HashSet<object>());
		}
		catch
		{
			bypass = false;
		}
		QuantumDrawBypassCache[quantumAsset] = bypass;
		return bypass;
	}

	private static bool ContainsXuanJianDongTianReference(object target, int depth, HashSet<object> visited)
	{
		if (target == null || depth > 3) return false;
		if (target is Building building)
		{
			if (!IsXuanJianDongTianBuilding(building)) return false;
			EnsureBuildingRenderable(building);
			return true;
		}
		if (target is BuildingAsset buildingAsset) return IsXuanJianDongTianAssetId(TryGetAssetId(buildingAsset));
		if (target is string text) return IsXuanJianDongTianAssetId(text);
		Type type = target.GetType();
		if (!type.IsValueType && !visited.Add(target)) return false;
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		FieldInfo[] fields;
		try { fields = type.GetFields(flags); } catch { return false; }
		for (int i = 0; i < fields.Length; i++)
		{
			FieldInfo field = fields[i];
			if (field == null || field.FieldType.IsValueType) continue;
			object value;
			try { value = field.GetValue(target); } catch { continue; }
			if (ContainsXuanJianDongTianReference(value, depth + 1, visited)) return true;
		}
		return false;
	}

	internal static bool IsXuanJianDongTianBuilding(Building building)
	{
		return building?.asset != null && IsXuanJianDongTianAssetId(TryGetAssetId(building.asset));
	}

	internal static bool IsXuanJianDongTianAssetId(string assetId)
	{
		if (string.IsNullOrWhiteSpace(assetId))
		{
			return false;
		}

		string safeId = assetId.Trim();
		return safeId.StartsWith("xj_", StringComparison.Ordinal)
			&& safeId.IndexOf("dongtian", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	internal static bool IsRegisteredDongTianMapIcon(BuildingMapIcon icon)
	{
		return icon != null && DongTianMapIcons.Contains(icon);
	}

	private static string TryGetAssetId(BuildingAsset asset)
	{
		if (asset == null)
		{
			return string.Empty;
		}

		try
		{
			return ((Asset)asset).id ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static void DisableBuildingRecolor(BuildingAsset asset)
	{
		if (asset == null)
		{
			return;
		}

		TrySetBoolMember(asset, "need_colored_sprite", false);
		TrySetBoolMember(asset, "needs_colored_sprite", false);
		TrySetBoolMember(asset, "colored", false);
		TrySetBoolMember(asset, "can_be_colored", false);
		TrySetBoolMember(asset, "use_dynamic_sprites", false);
	}

	private static void DisableRuinAndRandomizedSpritePaths(BuildingAsset asset)
	{
		if (asset == null)
		{
			return;
		}

		TrySetBoolMember(asset, "has_ruin_state", false);
		TrySetBoolMember(asset, "has_ruins_graphics", false);
		TrySetBoolMember(asset, "auto_remove_ruin", false);
		TrySetBoolMember(asset, "remove_ruins", false);
		TrySetBoolMember(asset, "random_flip", false);
		TrySetBoolMember(asset, "random_flip_x", false);
		TrySetBoolMember(asset, "random_flip_y", false);
		TrySetBoolMember(asset, "randomize_sprite", false);
		TrySetBoolMember(asset, "randomize_frame", false);

		TrySetNullMember(asset, "sprite_ruins");
		TrySetNullMember(asset, "sprites_ruins");
		TrySetNullMember(asset, "ruin_sprite");
		TrySetNullMember(asset, "ruin_sprites");
	}

	private static void DisableBuildingMapIcon(BuildingAsset asset)
	{
		if (asset == null)
		{
			return;
		}

		RegisterMapIcon(TryGetMember(asset, "map_icon"));
		RegisterMapIcon(TryGetMember(asset, "mapIcon"));
		RegisterMapIcon(TryGetMember(asset, "building_map_icon"));
		RegisterMapIcon(TryGetMember(asset, "buildingMapIcon"));
		RegisterMapIcon(TryGetMember(asset, "minimap_icon"));
		RegisterMapIcon(TryGetMember(asset, "minimapIcon"));

		TrySetNullMember(asset, "map_icon");
		TrySetNullMember(asset, "mapIcon");
		TrySetNullMember(asset, "building_map_icon");
		TrySetNullMember(asset, "buildingMapIcon");
		TrySetNullMember(asset, "minimap_icon");
		TrySetNullMember(asset, "minimapIcon");

		// WorldBox实际把地图图标挂在BuildingSprites上。克隆住宅模板后必须
		// 清理这一层，否则地图缩放时仍会进入BuildingMapIcon.getColor。
		object sprites = TryGetMember(asset, "building_sprites");
		if (sprites == null)
		{
			return;
		}

		RegisterMapIcon(TryGetMember(sprites, "map_icon"));
		RegisterMapIcon(TryGetMember(sprites, "mapIcon"));
		TrySetNullMember(sprites, "map_icon");
		TrySetNullMember(sprites, "mapIcon");
		TrySetNullMember(sprites, "sprite_ruins");
		TrySetNullMember(sprites, "sprites_ruins");
		TrySetNullMember(sprites, "ruin_sprite");
		TrySetNullMember(sprites, "ruin_sprites");
	}

	private static void RegisterMapIcon(object value)
	{
		if (value is BuildingMapIcon icon)
		{
			DongTianMapIcons.Add(icon);
		}
	}

	private static object TryGetMember(object target, string name)
	{
		if (target == null || string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		try
		{
			FieldInfo field = target.GetType().GetField(name, flags);
			if (field != null)
			{
				return field.GetValue(target);
			}

			PropertyInfo property = target.GetType().GetProperty(name, flags);
			return property?.CanRead == true ? property.GetValue(target, null) : null;
		}
		catch
		{
			return null;
		}
	}

	private static void TrySetBoolMember(object target, string name, bool value)
	{
		if (target == null || string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		try
		{
			FieldInfo field = target.GetType().GetField(name, flags);
			if (field != null && field.FieldType == typeof(bool))
			{
				field.SetValue(target, value);
				return;
			}

			PropertyInfo property = target.GetType().GetProperty(name, flags);
			if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
			{
				property.SetValue(target, value, null);
			}
		}
		catch
		{
		}
	}

	private static void TrySetNullMember(object target, string name)
	{
		if (target == null || string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		try
		{
			FieldInfo field = target.GetType().GetField(name, flags);
			if (field != null && !field.FieldType.IsValueType)
			{
				field.SetValue(target, null);
				return;
			}

			PropertyInfo property = target.GetType().GetProperty(name, flags);
			if (property != null && property.CanWrite && !property.PropertyType.IsValueType)
			{
				property.SetValue(target, null, null);
			}
		}
		catch
		{
		}
	}

	private static void SanitizeSpriteReferences(object target)
	{
		SanitizeSpriteReferences(target, 0, new HashSet<object>());
	}

	private static void SanitizeSpriteReferences(object target, int depth, HashSet<object> visited)
	{
		if (target == null || depth > 4 || target is string || target is Sprite)
		{
			return;
		}

		try
		{
			if (!target.GetType().IsValueType && !visited.Add(target))
			{
				return;
			}
		}
		catch
		{
		}

		if (target is IList list)
		{
			for (int i = list.Count - 1; i >= 0; i--)
			{
				object item = null;
				try { item = list[i]; } catch { }
				if (item == null)
				{
					try { list.RemoveAt(i); } catch { }
					continue;
				}
				SanitizeSpriteReferences(item, depth + 1, visited);
			}
			return;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		FieldInfo[] fields;
		try { fields = target.GetType().GetFields(flags); }
		catch { return; }
		for (int i = 0; i < fields.Length; i++)
		{
			FieldInfo field = fields[i];
			if (field == null || field.FieldType.IsValueType || field.FieldType == typeof(string)) continue;
			object value;
			try { value = field.GetValue(target); }
			catch { continue; }
			if (value == null || value is Sprite) continue;
			string typeName = value.GetType().Name;
			if (value is IList || typeName.IndexOf("Sprite", StringComparison.OrdinalIgnoreCase) >= 0 || typeName.IndexOf("Animation", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				SanitizeSpriteReferences(value, depth + 1, visited);
			}
		}
	}

	internal static void EnsureBuildingRenderable(Building building)
	{
		if (building?.data == null || building.asset == null)
		{
			return;
		}

		try
		{
			WorldTile tile = ((BaseSimObject)building).current_tile;
			if (tile == null && World.world != null)
			{
				int x = building.data.mainX;
				int y = building.data.mainY;
				if (x >= 0 && y >= 0)
				{
					tile = World.world.GetTileSimple(x, y);
					if (tile != null)
					{
						((BaseSimObject)building).current_tile = tile;
					}
				}
			}
			if (tile != null)
			{
				building.data.mainX = tile.pos.x;
				building.data.mainY = tile.pos.y;
				building.current_position = tile.posV3;
			}
		}
		catch
		{
		}

		try
		{
			if (building.data.frameID < 0)
			{
				building.data.frameID = 0;
			}
			SanitizeSpriteReferences(building.asset);
			SpriteDirtyField?.SetValue(building, true);
			ClearSpritesMethod?.Invoke(building, null);
			CheckSpriteMethod?.Invoke(building, null);
		}
		catch
		{
		}
	}
}
