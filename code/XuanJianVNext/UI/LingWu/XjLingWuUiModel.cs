using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.LingWu;

// The historical type name is retained to avoid widening the family-tab patch surface.
// Items now represent every entry in the family treasure warehouse.
internal readonly struct XjLingWuUiItem
{
	internal readonly string Id;
	internal readonly string DisplayName;
	internal readonly string IconPath;
	internal readonly int Count;
	internal readonly string Description;
	internal readonly string Details;

	internal XjLingWuUiItem(string id, string displayName, string iconPath, int count, string description, string details)
	{
		Id = id ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		IconPath = iconPath ?? string.Empty;
		Count = Math.Max(0, count);
		Description = description ?? string.Empty;
		Details = details ?? string.Empty;
	}
}

internal static class XjLingWuUiModel
{
	internal static XjLingWuUiItem[] BuildFamilyItems(long familyStableId)
	{
		IReadOnlyList<XjFamilyLingWuWarehouseEntry> entries = XjFamilyLingWuWarehouse.ReadFamilyEntries(familyStableId);
		if (entries.Count == 0) return Array.Empty<XjLingWuUiItem>();

		List<XjLingWuUiItem> result = new(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyLingWuWarehouseEntry entry = entries[i];
			if (entry.Count <= 0 || !XjFamilyTreasureCatalog.TryResolve(entry.LingWuId, out XjFamilyTreasureDef definition)) continue;
			string details = "类别：" + definition.Category
				+ (string.IsNullOrWhiteSpace(definition.DaoTuGroup) ? string.Empty : "\n对应道途：" + definition.DaoTuGroup)
				+ "\n库存：" + entry.Count + definition.Unit
				+ (entry.FirstAcquiredYear > 0 ? "\n首次入库：玄鉴历" + entry.FirstAcquiredYear + "年" : string.Empty)
				+ (entry.LastAcquiredYear > 0 ? "\n最近入库：玄鉴历" + entry.LastAcquiredYear + "年" : string.Empty)
				+ (!string.IsNullOrWhiteSpace(entry.LastSourceActorName)
					? "\n" + definition.SourceLabel + "：" + entry.LastSourceActorName
					: string.Empty);
			result.Add(new XjLingWuUiItem(
				definition.Id,
				definition.Name,
				definition.IconPath,
				entry.Count,
				definition.Description,
				details));
		}
		return result.ToArray();
	}

	internal static void CreateIconCell(Transform parent, in XjLingWuUiItem item, Font font)
	{
		if (parent == null) return;
		Sprite sprite = XjIconLoader.TryLoadSpritePath(item.IconPath);
		if (sprite == null && !item.IconPath.StartsWith("GameResources/", StringComparison.OrdinalIgnoreCase))
		{
			sprite = XjIconLoader.TryLoadSpritePath("GameResources/" + item.IconPath);
		}
		XjIconShelfRenderer.CreateIconSlot(
			parent,
			"FamilyTreasureWarehouseSlot",
			sprite,
			"重",
			Math.Max(1, item.Count),
			string.IsNullOrWhiteSpace(item.DisplayName) ? "家族重宝" : item.DisplayName,
			item.Description,
			item.Details,
			font,
			showCountWhenOne: false);
	}
}
