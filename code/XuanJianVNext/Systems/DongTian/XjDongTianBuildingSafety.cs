using System;
using System.Collections.Concurrent;
using UnityEngine;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.DongTian;

internal static class XjDongTianBuildingSafety
{
	// getColor 会被原生渲染工作线程调用；图标注册则可能发生在资产加载路径。
	// 两者不能共享普通 HashSet/Dictionary，否则长线世界的洞天显现会有并发读写风险。
	private static readonly ConcurrentDictionary<BuildingMapIcon, byte> DongTianMapIcons = new ConcurrentDictionary<BuildingMapIcon, byte>();
	private static readonly ConcurrentDictionary<BuildingAsset, BuildingMapIcon> PrivateMapIcons = new ConcurrentDictionary<BuildingAsset, BuildingMapIcon>();
	// 不信任 BuildingAsset.sprite_path 作为“已加载”凭据：EnsureAssets 会在调用前
	// 写目标路径，而 clone() 此时仍可能携带住宅模板的主帧。只有本方法亲自确认
	// 目标目录成功加载后，才登记为可复用的渲染路径。
	private static readonly ConcurrentDictionary<BuildingAsset, string> RenderableSpritePaths = new ConcurrentDictionary<BuildingAsset, string>();

	internal static bool EnsureAssetRenderable(BuildingAsset asset, string mainPath, string spritePath)
	{
		if (asset == null)
		{
			return false;
		}

		try
		{
			// 只有本方法此前确认过“目标目录已经成功加载”的资产才走快速路径。
			// 不能信任 sprite_path 字段本身，因为上游会先赋目标路径，而 clone()
			// 此时仍可能携带住宅模板的主帧。
			bool alreadyLoadedExpectedPath = !string.IsNullOrWhiteSpace(spritePath)
				&& RenderableSpritePaths.TryGetValue(asset, out string loadedSpritePath)
				&& string.Equals(loadedSpritePath, spritePath, StringComparison.Ordinal);
			if (!string.IsNullOrWhiteSpace(mainPath)) asset.main_path = mainPath;
			if (!string.IsNullOrWhiteSpace(spritePath)) asset.sprite_path = spritePath;
			DisableBuildingRecolor(asset);
			DisableRuinAndRandomizedSpritePaths(asset);

			if (alreadyLoadedExpectedPath && HasMainSprite(asset))
			{
				asset.has_sprites_main = true;
				asset.sprites_are_initiated = true;
				RegisterBuildingMapIcons(asset);
				return true;
			}

			// 首次加载或目标目录变化时才允许重建 Sprite。过去每次 EnsureAssets
			// 都先写 false，会让一个坏资源拖着所有正常洞天反复 loadBuildingSprites。
			asset.sprites_are_initiated = false;
			XjNativeBuildingInterop.TryLoadSprites(asset);
			// loadBuildingSprites 可能从克隆模板重新带回地图图标、王国色与废墟分支，
			// 因而加载后再做一次幂等清理。
			DisableBuildingRecolor(asset);
			DisableRuinAndRandomizedSpritePaths(asset);
			RegisterBuildingMapIcons(asset);

			// 不把“加载方法没有抛异常”误当成“首帧已经可绘制”。资源初始化期
			// SpriteTextureLoader 可能暂时返回空数组；若此时创建 Building，原生
			// QuantumSprite 会拿到 null Sprite，而存档却已记为 EntitySpawned。
			// 只有确认原生 BuildingSprites 已有主帧，才允许洞天实体进入地图。
			bool hasMainSprite = HasMainSprite(asset);
			asset.has_sprites_main = hasMainSprite;
			asset.sprites_are_initiated = hasMainSprite;
			if (hasMainSprite && !string.IsNullOrWhiteSpace(spritePath))
			{
				RenderableSpritePaths[asset] = spritePath;
			}
			else
			{
				RenderableSpritePaths.TryRemove(asset, out _);
			}
			return hasMainSprite;
		}
		catch
		{
			RenderableSpritePaths.TryRemove(asset, out _);
			try
			{
				asset.sprites_are_initiated = false;
			}
			catch (System.Exception xjCaught50) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DongTian/XjDongTianBuildingSafety.cs:50", xjCaught50); }
			return false;
		}
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
		return (safeId.StartsWith("xj_", StringComparison.Ordinal)
				&& safeId.IndexOf("dongtian", StringComparison.OrdinalIgnoreCase) >= 0)
			|| string.Equals(safeId, XjDongTianRules.LegacyBingGuQiYuDongTianId, StringComparison.Ordinal);
	}

	private static bool HasMainSprite(BuildingAsset asset)
	{
		return asset?.building_sprites != null
			&& asset.building_sprites.animation_data != null
			&& asset.building_sprites.animation_data.Count > 0
			&& asset.building_sprites.animation_data[0] != null
			&& asset.building_sprites.animation_data[0].main != null
			&& asset.building_sprites.animation_data[0].main.Length > 0;
	}

	internal static bool IsRegisteredDongTianMapIcon(BuildingMapIcon icon)
	{
		return icon != null && DongTianMapIcons.ContainsKey(icon);
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

	}

	private static void RegisterBuildingMapIcons(BuildingAsset asset)
	{
		if (asset == null)
		{
			return;
		}

		// clone() 可能浅拷贝住宅模板的 BuildingMapIcon。若直接按对象身份把
		// 该共享图标设为透明，会连普通住宅一起影响。这里给每个洞天资产创建
		// 私有图标对象：QuantumSprite 仍得到完整非空对象图，而 getColor Prefix
		// 只会命中洞天自己的私有图标。克隆失败时宁可保留原生图标，也绝不
		// 注册共享模板对象。
		if (!PrivateMapIcons.TryGetValue(asset, out BuildingMapIcon privateIcon) || privateIcon == null)
		{
			BuildingMapIcon source = TryFindMapIcon(asset);
			privateIcon = TryCloneMapIcon(source);
			if (privateIcon == null)
			{
				return;
			}
			privateIcon = PrivateMapIcons.GetOrAdd(asset, privateIcon);
		}

		RegisterMapIcon(privateIcon);
		TryAssignMapIcon(asset, privateIcon);
	}

	private static BuildingMapIcon TryFindMapIcon(BuildingAsset asset)
	{
		if (asset == null) return null;
		string[] names = { "map_icon", "mapIcon", "building_map_icon", "buildingMapIcon", "minimap_icon", "minimapIcon" };
		for (int i = 0; i < names.Length; i++)
		{
			if (TryGetMember(asset, names[i]) is BuildingMapIcon icon) return icon;
		}

		object sprites = TryGetMember(asset, "building_sprites");
		if (sprites != null)
		{
			for (int i = 0; i < 2; i++)
			{
				if (TryGetMember(sprites, i == 0 ? "map_icon" : "mapIcon") is BuildingMapIcon icon) return icon;
			}
		}
		return null;
	}

	private static BuildingMapIcon TryCloneMapIcon(BuildingMapIcon source)
	{
		return XjNativeBuildingInterop.TryCloneMapIcon(source);
	}

	private static void TryAssignMapIcon(BuildingAsset asset, BuildingMapIcon icon)
	{
		if (asset == null || icon == null) return;
		string[] directNames = { "map_icon", "mapIcon", "building_map_icon", "buildingMapIcon", "minimap_icon", "minimapIcon" };
		for (int i = 0; i < directNames.Length; i++) TrySetMember(asset, directNames[i], icon);

		object sprites = TryGetMember(asset, "building_sprites");
		if (sprites != null)
		{
			TrySetMember(sprites, "map_icon", icon);
			TrySetMember(sprites, "mapIcon", icon);
		}
	}


	private static void RegisterMapIcon(object value)
	{
		if (value is BuildingMapIcon icon)
		{
			DongTianMapIcons.TryAdd(icon, 0);
		}
	}

	private static object TryGetMember(object target, string name)
	{
		return XjNativeReflectionInterop.ReadMemberValue(target, name);
	}

	private static void TrySetMember(object target, string name, object value)
	{
		if (value != null) XjNativeReflectionInterop.TryWriteMemberValue(target, name, value);
	}

	private static void TrySetBoolMember(object target, string name, bool value)
	{
		XjNativeReflectionInterop.TryWriteBooleanMember(target, name, value);
	}

	internal static void PrepareForNativeMainSprite(Building building)
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
		catch (System.Exception ex)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("DongTian.Building.PrepareTile", ex);
		}

		try
		{
			if (building.data.frameID < 0) building.data.frameID = 0;
		}
		catch (System.Exception ex)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("DongTian.Building.PrepareSprite", ex);
		}
	}

	internal static void EnsureBuildingRenderable(Building building)
	{
		if (building?.data == null || building.asset == null)
		{
			return;
		}

		PrepareForNativeMainSprite(building);
		try
		{
			// 原生精灵刷新有自身的地图生命周期；反射强行调用会在并行渲染期触发 TargetInvocationException。
			// 标脏后交由原生更新循环刷新即可。
			XjNativeBuildingInterop.MarkSpriteDirty(building);
		}
		catch (System.Exception ex)
		{
			string assetId = string.Empty;
			try { assetId = ((Asset)building.asset).id ?? string.Empty; } catch { }
			XuanJianVNext.Core.XjExceptionDiagnostics.Report(
				"DongTian.Building.EnsureRenderable" + (assetId.Length > 0 ? "." + assetId : string.Empty), ex);
		}
	}
}
