using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Craft;

internal readonly struct XjCraftInventoryUiItem
{
	internal readonly string Id;
	internal readonly string DisplayName;
	internal readonly string IconPath;
	internal readonly int Count;
	internal readonly string Description;
	internal readonly string Details;

	internal XjCraftInventoryUiItem(string id, string displayName, string iconPath, int count, string description, string details)
	{
		Id = id ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		IconPath = iconPath ?? string.Empty;
		Count = Math.Max(0, count);
		Description = description ?? string.Empty;
		Details = details ?? string.Empty;
	}
}

internal static class XjCraftInventoryUiModel
{
	private static readonly HashSet<string> TalismanMaterialIds = new HashSet<string>(StringComparer.Ordinal)
	{
		XjTalismanCatalog.LowPaper,
		XjTalismanCatalog.HighPaper,
		XjTalismanCatalog.LowInk,
		XjTalismanCatalog.HighInk,
		XjTalismanCatalog.BrushDurability
	};

	private static readonly HashSet<string> FormationMaterialIds = new HashSet<string>(StringComparer.Ordinal)
	{
		XjCraftCollaborationSystem.FormationFlagLow,
		XjCraftCollaborationSystem.FormationFlagHigh,
		XjCraftCollaborationSystem.FormationPlateLow,
		XjCraftCollaborationSystem.FormationPlateHigh,
		XjCraftCollaborationSystem.FormationRune,
		XjCraftCollaborationSystem.SpiritMaterial
	};

	internal static XjCraftInventoryUiItem[] BuildTalismanInventory(XjCraftOwnerKey owner)
	{
		List<XjCraftInventoryUiItem> items = new List<XjCraftInventoryUiItem>();
		AppendTalismanMaterials(items, owner);
		AppendTalismanBatches(items, owner);
		return items.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
	}

	internal static XjCraftInventoryUiItem[] BuildPersonalTalismanInventory(XjCraftOwnerKey owner, long actorId)
	{
		List<XjCraftInventoryUiItem> items = new List<XjCraftInventoryUiItem>();
		AppendTalismanMaterials(items, owner);
		AppendCarriedTalismans(items, actorId);
		AppendTalismanBatches(items, owner);
		return items.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
	}

	internal static XjCraftInventoryUiItem[] BuildFormationMaterials(XjCraftOwnerKey owner)
	{
		if (!owner.IsValid)
		{
			return Array.Empty<XjCraftInventoryUiItem>();
		}

		List<XjCraftInventoryUiItem> items = new List<XjCraftInventoryUiItem>();
		IReadOnlyList<XjCraftResourceArchiveRecord> resources = XjCraftDomainRegistry.ReadResources();
		for (int i = 0; i < resources.Count; i++)
		{
			XjCraftResourceArchiveRecord resource = resources[i];
			if (resource == null
				|| resource.Quantity <= 0
				|| resource.OwnerScope != owner.Scope
				|| resource.OwnerId != owner.OwnerId
				|| !FormationMaterialIds.Contains(resource.ResourceId))
			{
				continue;
			}

			items.Add(BuildResourceItem(resource.ResourceId, resource.Quantity, resource.UpdatedYear));
		}
		return items.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
	}

	internal static void CreateIconCell(Transform parent, in XjCraftInventoryUiItem item, Font font, string objectName)
	{
		if (parent == null)
		{
			return;
		}

		Sprite sprite = XjIconLoader.TryLoadSpritePath(item.IconPath);
		string fallback = string.IsNullOrWhiteSpace(item.DisplayName) ? "物" : item.DisplayName;
		XjIconShelfRenderer.CreateIconSlot(
			parent,
			objectName,
			sprite,
			fallback,
			Math.Max(1, item.Count),
			string.IsNullOrWhiteSpace(item.DisplayName) ? "未名物品" : item.DisplayName,
			item.Description,
			item.Details,
			font,
			showCountWhenOne: true);
	}

	private static void AppendTalismanMaterials(List<XjCraftInventoryUiItem> items, XjCraftOwnerKey owner)
	{
		if (items == null || !owner.IsValid)
		{
			return;
		}

		IReadOnlyList<XjCraftResourceArchiveRecord> resources = XjCraftDomainRegistry.ReadResources();
		for (int i = 0; i < resources.Count; i++)
		{
			XjCraftResourceArchiveRecord resource = resources[i];
			if (resource == null
				|| resource.Quantity <= 0
				|| resource.OwnerScope != owner.Scope
				|| resource.OwnerId != owner.OwnerId
				|| !TalismanMaterialIds.Contains(resource.ResourceId))
			{
				continue;
			}

			items.Add(BuildResourceItem(resource.ResourceId, resource.Quantity, resource.UpdatedYear));
		}
	}

	private static void AppendTalismanBatches(List<XjCraftInventoryUiItem> items, XjCraftOwnerKey owner)
	{
		if (items == null || !owner.IsValid)
		{
			return;
		}

		IReadOnlyList<XjTalismanBatchArchiveRecord> batches = XjCraftDomainRegistry.ReadTalismanBatches();
		Dictionary<string, (int Count, int LastYear)> grouped = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
		for (int i = 0; i < batches.Count; i++)
		{
			XjTalismanBatchArchiveRecord batch = batches[i];
			if (batch == null
				|| batch.Quantity <= 0
				|| batch.OwnerScope != owner.Scope
				|| batch.OwnerId != owner.OwnerId
				|| string.IsNullOrWhiteSpace(batch.TalismanId))
			{
				continue;
			}

			grouped.TryGetValue(batch.TalismanId, out (int Count, int LastYear) current);
			current.Count += Math.Max(0, batch.Quantity);
			current.LastYear = Math.Max(current.LastYear, batch.CraftedYear);
			grouped[batch.TalismanId] = current;
		}

		foreach (KeyValuePair<string, (int Count, int LastYear)> pair in grouped)
		{
			if (!XjTalismanCatalog.TryGet(pair.Key, out XjTalismanDefinition definition))
			{
				continue;
			}
			string details = "效果：" + XjTalismanCatalog.EffectSummary(definition.Id)
				+ "\n库存：" + pair.Value.Count + "张";
			if (pair.Value.LastYear > 0)
			{
				details += "\n最近炼成：玄鉴历" + pair.Value.LastYear + "年";
			}
			items.Add(new XjCraftInventoryUiItem(
				definition.Id,
				definition.DisplayName,
				definition.IconPath,
				pair.Value.Count,
				XjTalismanCatalog.EffectSummary(definition.Id),
				details));
		}
	}

	private static void AppendCarriedTalismans(List<XjCraftInventoryUiItem> items, long actorId)
	{
		if (items == null || actorId <= 0L)
		{
			return;
		}

		IReadOnlyList<XjTalismanCarryArchiveRecord> carries = XjCraftDomainRegistry.ReadCarries();
		for (int i = 0; i < carries.Count; i++)
		{
			XjTalismanCarryArchiveRecord carry = carries[i];
			if (carry == null || carry.ActorId != actorId || carry.Quantity <= 0 || !XjTalismanCatalog.TryGet(carry.TalismanId, out XjTalismanDefinition definition))
			{
				continue;
			}

			string details = "效果：" + XjTalismanCatalog.EffectSummary(definition.Id)
				+ "\n随身携带：" + carry.Quantity + "张";
			if (carry.AssignedYear > 0)
			{
				details += "\n携带年份：玄鉴历" + carry.AssignedYear + "年";
			}
			items.Add(new XjCraftInventoryUiItem(
				"carry." + definition.Id,
				definition.DisplayName,
				definition.IconPath,
				carry.Quantity,
				XjTalismanCatalog.EffectSummary(definition.Id),
				details));
		}
	}

	private static XjCraftInventoryUiItem BuildResourceItem(string resourceId, int count, int updatedYear)
	{
		ResolveResource(resourceId, out string name, out string iconPath, out string description);
		string details = "库存：" + Math.Max(0, count) + "份";
		if (updatedYear > 0)
		{
			details += "\n最近入库：玄鉴历" + updatedYear + "年";
		}
		return new XjCraftInventoryUiItem(resourceId, name, iconPath, count, description, details);
	}

	private static void ResolveResource(string resourceId, out string name, out string iconPath, out string description)
	{
		switch (resourceId)
		{
			case XjTalismanCatalog.LowPaper:
				name = "下品符纸";
				iconPath = "GameResources/item/Arts/fulu/FuZhiLow.png";
				description = "绘制炼气层次符箓的常用符纸。";
				return;
			case XjTalismanCatalog.HighPaper:
				name = "上品符纸";
				iconPath = "GameResources/item/Arts/fulu/FuZhiHigh.png";
				description = "绘制筑基、紫府层次符箓的上好符纸。";
				return;
			case XjTalismanCatalog.LowInk:
				name = "下品灵墨";
				iconPath = "GameResources/item/Arts/fulu/LingMoLow..png";
				description = "绘制炼气层次符箓的常用灵墨。";
				return;
			case XjTalismanCatalog.HighInk:
				name = "上品灵墨";
				iconPath = "GameResources/item/Arts/fulu/LingMoHigh.png";
				description = "承载筑基、紫府层次符箓法意的灵墨。";
				return;
			case XjTalismanCatalog.BrushDurability:
				name = "符笔耐久";
				iconPath = "GameResources/item/Arts/fulu/FuBi.png";
				description = "符箓师执笔绘符所消耗的符笔余力。";
				return;
			case XjCraftCollaborationSystem.FormationFlagLow:
				name = "下品阵旗";
				iconPath = "GameResources/item/Arts/zhenfa/ZhenQi.png";
				description = "布置低阶阵法、修补阵脚的基础阵旗。";
				return;
			case XjCraftCollaborationSystem.FormationFlagHigh:
				name = "上品阵旗";
				iconPath = "GameResources/item/Arts/zhenfa/ZhenQi.png";
				description = "承载宗门大阵主脉的上好阵旗。";
				return;
			case XjCraftCollaborationSystem.FormationPlateLow:
				name = "下品阵盘";
				iconPath = "GameResources/item/Arts/zhenfa/ZhenPan.png";
				description = "阵法工程的基础阵盘。";
				return;
			case XjCraftCollaborationSystem.FormationPlateHigh:
				name = "上品阵盘";
				iconPath = "GameResources/item/Arts/zhenfa/ZhenPan.png";
				description = "承托大阵枢纽的高阶阵盘。";
				return;
			case XjCraftCollaborationSystem.FormationRune:
				name = "阵纹";
				iconPath = "GameResources/item/Arts/zhenfa/ZhenTu.png";
				description = "铭刻阵图、秘境工程和宗门大阵所需的阵纹。";
				return;
			case XjCraftCollaborationSystem.SpiritMaterial:
				name = "良材";
				iconPath = "GameResources/item/Arts/zhenfa/ZongMenDaZhen.png";
				description = "可供宗门工程、阵法修复与洞天营造调用的灵性良材。";
				return;
			default:
				name = "未名材料";
				iconPath = "ui/icons/iconStar";
				description = "未登记的四艺材料。";
				return;
		}
	}

}
