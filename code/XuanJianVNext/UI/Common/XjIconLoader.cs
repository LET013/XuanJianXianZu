using UnityEngine;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.UI.Common;

internal static class XjIconLoader
{
	internal static Sprite TryLoadCaiQiResourceSprite(string resourceId)
	{
		if (string.IsNullOrWhiteSpace(resourceId))
		{
			return null;
		}
		string text = resourceId.Trim();
		Sprite mapped = TryLoadCaiQiMappedIcon(text);
		if (mapped != null)
		{
			return mapped;
		}

		ResourceAsset val = ((AssetLibrary<ResourceAsset>)(object)AssetManager.resources)?.get(text);
		if (val != null && !string.IsNullOrWhiteSpace(val.path_icon))
		{
			Sprite sprite = TryLoadSpritePath(val.path_icon);
			if (sprite != null)
			{
				return sprite;
			}
		}
		return TryLoadSpritePaths(
				text,
				"item/caiqi/" + text,
				"GameResources/item/caiqi/" + text)
			?? SpriteTextureLoader.getSprite("ui/icons/iconStar")
			?? SpriteTextureLoader.getSprite("ui/icons/iconBook");
	}

	internal static Sprite TryLoadCaiQiMappedIcon(string resourceId)
	{
		string normalized = string.IsNullOrWhiteSpace(resourceId) ? string.Empty : resourceId.Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return null;
		}

		if (string.Equals(normalized, "zaqi", System.StringComparison.OrdinalIgnoreCase))
		{
			return TryLoadSpritePaths(
				"item/caiqi/iconResZaQi",
				"GameResources/item/caiqi/iconResZaQi");
		}

		if (string.Equals(normalized, "taiyinyuehua", System.StringComparison.OrdinalIgnoreCase))
		{
			return TryLoadSpritePaths(
				"item/caiqi/iconResTaiYinYueHua",
				"GameResources/item/caiqi/iconResTaiYinYueHua");
		}

		for (int i = 0; i < XjCaiQiCatalog.Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = XjCaiQiCatalog.Entries[i];
			if (!string.Equals(entry.OldResourceId, normalized, System.StringComparison.OrdinalIgnoreCase)
				|| string.IsNullOrWhiteSpace(entry.BranchId))
			{
				continue;
			}

			string iconName = "iconRes" + entry.BranchId.Trim() + "ZhiQi";
			return TryLoadSpritePaths(
				"item/caiqi/" + iconName,
				"GameResources/item/caiqi/" + iconName);
		}

		return null;
	}

	internal static Sprite TryLoadSpritePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		string text = path.Trim().Replace("\\", "/");
		string text2 = text;
		if (text2.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
			|| text2.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase))
			text2 = text2.Substring(0, text2.Length - 4);
		else if (text2.EndsWith(".jpeg", System.StringComparison.OrdinalIgnoreCase))
			text2 = text2.Substring(0, text2.Length - 5);
		Sprite sprite = TryLoadRawSpritePath(text, text2);
		if (sprite != null)
		{
			return sprite;
		}
		if (text2.StartsWith("GameResources/", System.StringComparison.OrdinalIgnoreCase))
		{
			string stripped = text2.Substring("GameResources/".Length);
			sprite = TryLoadRawSpritePath(stripped, stripped);
			if (sprite != null)
			{
				return sprite;
			}
		}
		return Resources.Load<Sprite>(text) ?? Resources.Load<Sprite>(text2) ?? Resources.Load<Sprite>(text2 + ".png");
	}

	private static Sprite TryLoadRawSpritePath(string originalPath, string extensionlessPath)
	{
		return SpriteTextureLoader.getSprite(originalPath)
			?? SpriteTextureLoader.getSprite(extensionlessPath)
			?? SpriteTextureLoader.getSprite(extensionlessPath + ".png")
			?? Resources.Load<Sprite>(originalPath)
			?? Resources.Load<Sprite>(extensionlessPath)
			?? Resources.Load<Sprite>(extensionlessPath + ".png");
	}

	internal static Sprite TryLoadSpritePaths(params string[] paths)
	{
		if (paths == null)
		{
			return null;
		}

		for (int i = 0; i < paths.Length; i++)
		{
			Sprite sprite = TryLoadSpritePath(paths[i]);
			if (sprite != null)
			{
				return sprite;
			}
		}

		return null;
	}

	internal static Sprite TryLoadGongFaResourceSprite(XjFamilyGongFaWarehouseUIItem item)
	{
		if (string.IsNullOrWhiteSpace(item.SourceType))
		{
			return null;
		}
		string sourceType = item.SourceType.Trim();
		if (string.Equals(sourceType, XjFamilyCaiQiWarehouse.ResourceTypeCaiQiFa, System.StringComparison.Ordinal))
		{
			return TryLoadSpritePath("GameResources/item/gongfa/CaiQiFa.png")
				?? TryLoadSpritePath("item/gongfa/CaiQiFa");
		}
		if (string.Equals(sourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, System.StringComparison.Ordinal))
		{
			return TryLoadSpritePath("GameResources/item/gongfa/QiuJinFa.png")
				?? TryLoadSpritePath("item/gongfa/QiuJinFa");
		}
		string imageName = item.Grade switch
		{
			4 => "SiPinGongFa",
			5 => "WuPinGongFa",
			6 => "LiuPinGongFa",
			_ => "DiPinGongFa",
		};
		return TryLoadSpritePath("GameResources/item/gongfa/" + imageName + ".png")
			?? TryLoadSpritePath("item/gongfa/" + imageName);
	}
}
