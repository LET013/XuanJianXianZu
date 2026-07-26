using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Family;

public static partial class XjFamilyInheritanceTabPatches
{
	internal static void XuanJianVNext_CreateFamilyCaiQiWarehouseIconCell(Transform parent, XjFamilyCaiQiWarehouseUIItem item, Font font)
	{
		if (parent == null)
		{
			return;
		}

		XjIconShelfRenderer.CreateIconSlot(
			parent,
			"JiaZuCaiQiWarehouseSlot",
			XuanJianVNext_TryLoadFamilyCaiQiResourceSprite(item.ResourceId),
			string.IsNullOrWhiteSpace(item.DisplayName) ? "气" : item.DisplayName,
			item.Count,
			string.IsNullOrWhiteSpace(item.DisplayName) ? "未名先天之气" : item.DisplayName,
			XuanJianVNext_BuildFamilyCaiQiWarehouseTooltipDescription(item.ResourceId, item.DisplayName),
			"收纳：" + Math.Max(0, item.Count).ToString(CultureInfo.InvariantCulture) + "份",
			font);
	}

	internal static void XuanJianVNext_CreateFamilyFaBaoWarehouseIconCell(Transform parent, XjFamilyFaBaoDisplayItem item, Font font)
	{
		if (parent == null || !item.Found)
		{
			return;
		}

		Sprite sprite = null;
		string kind = XjFaBaoAcquisition.ResolveKindFromName(item.FaBaoName);
		if (XjFaBaoEquipmentAssets.TryPickIconPath(kind, item.Year, item.FaBaoId, out string iconPath))
		{
			sprite = XuanJianVNext_TryLoadFamilyCaiQiSpritePath(iconPath);
		}
		sprite ??= XuanJianVNext_TryLoadFamilyCaiQiSpritePath("GameResources/ui/Icons/items/FaBaoZongLan.png")
			?? XuanJianVNext_TryLoadFamilyCaiQiSpritePath("ui/Icons/items/FaBaoZongLan")
			?? SpriteTextureLoader.getSprite("ui/icons/iconStar");

		string description = string.IsNullOrWhiteSpace(item.ClassName)
			? "家族先辈遗留的法宝。"
			: item.ClassName + "，由家族先辈遗留入库。";
		string details = XjTooltipBuilder.JoinTooltipLines(
			string.IsNullOrWhiteSpace(item.DaoTu) ? string.Empty : "道途：" + item.DaoTu.Trim(),
			FormatFaBaoWarehouseSource(item.Source),
			string.IsNullOrWhiteSpace(item.ActorName) ? string.Empty : "原持有者：" + item.ActorName.Trim(),
			item.Year > 0 ? "入库：" + item.Year.ToString(CultureInfo.InvariantCulture) + "年" : string.Empty,
			XjFaBaoBonusService.BuildBonusText(item.ClassName));
		XjIconShelfRenderer.CreateIconSlot(
			parent,
			"JiaZuFaBaoWarehouseSlot",
			sprite,
			"宝",
			1,
			string.IsNullOrWhiteSpace(item.FaBaoName) ? "未名法宝" : item.FaBaoName,
			description,
			details,
			font);
	}

	private static string FormatFaBaoWarehouseSource(string source)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return string.Empty;
		}

		string display = XjFaBaoReadModel.FormatSource(source);
		if (string.IsNullOrWhiteSpace(display))
		{
			return string.Empty;
		}

		return "来源：" + display;
	}

	internal static string XuanJianVNext_BuildFamilyCaiQiWarehouseTooltipDescription(string resourceId, string displayName)
	{
		return XjTooltipBuilder.BuildCaiQiTooltip(resourceId, displayName);
	}

	internal static Sprite XuanJianVNext_TryLoadFamilyCaiQiResourceSprite(string resourceId)
	{
		return XjIconLoader.TryLoadCaiQiResourceSprite(resourceId);
	}

	internal static Sprite XuanJianVNext_TryLoadFamilyCaiQiMappedIcon(string resourceId)
	{
		return XjIconLoader.TryLoadCaiQiMappedIcon(resourceId);
	}

	private static Sprite XuanJianVNext_TryLoadFamilyCaiQiSpritePaths(params string[] paths)
	{
		return XjIconLoader.TryLoadSpritePaths(paths);
	}

	internal static Sprite XuanJianVNext_TryLoadFamilyCaiQiSpritePath(string path)
	{
		return XjIconLoader.TryLoadSpritePath(path);
	}

	internal static void XuanJianVNext_CreateFamilyGongFaWarehouseIconCell(Transform parent, XjFamilyGongFaWarehouseUIItem item, Font font)
	{
		if (parent == null)
		{
			return;
		}
		Sprite sprite = XuanJianVNext_TryLoadFamilyGongFaResourceSprite(item);
		if (sprite == null)
		{
			sprite = SpriteTextureLoader.getSprite("ui/icons/iconBook")
				?? SpriteTextureLoader.getSprite("ui/icons/iconJianBook");
		}
		XuanJianVNext_BuildFamilyGongFaWarehouseTooltip(item, out string title, out string description, out string details);
		XjIconShelfRenderer.CreateIconSlot(
			parent,
			"JiaZuGongFaWarehouseSlot",
			sprite,
			string.IsNullOrWhiteSpace(item.Name) ? "功" : item.Name,
			item.Count,
			title,
			description,
			details,
			font);
	}

	internal static void XuanJianVNext_BuildFamilyGongFaWarehouseTooltip(
		XjFamilyGongFaWarehouseUIItem item,
		out string title,
		out string description,
		out string details)
	{
		XjTooltipBuilder.BuildGongFaTooltip(item, out title, out description, out details);
	}

	internal static void XuanJianVNext_ApplyTooltipText(TipButton tip, string title, string description, string details)
	{
		if (tip == null)
		{
			return;
		}

		string safeTitle = string.IsNullOrWhiteSpace(title) ? "玄鉴" : title.Trim();
		string safeDescription = description ?? string.Empty;
		string safeDetails = details ?? string.Empty;
		XjNativeHoverTooltip.Ensure(tip, safeTitle, safeDescription, safeDetails);
	}

	internal static Sprite XuanJianVNext_TryLoadFamilyGongFaResourceSprite(XjFamilyGongFaWarehouseUIItem item)
	{
		return XjIconLoader.TryLoadGongFaResourceSprite(item);
	}
}
