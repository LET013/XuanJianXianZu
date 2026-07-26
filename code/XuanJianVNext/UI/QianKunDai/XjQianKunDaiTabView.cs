using System;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.UI.Alchemy;
using XuanJianVNext.UI.Craft;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.Common;
using UnityEngine;
using UnityEngine.UI;

namespace XuanJianVNext.UI.QianKunDai;

internal static class XjQianKunDaiTabView
{
	private const int GongFaDisplayCapacity = 7;

	internal static Transform CreateTabContent(UnitWindow window, Transform parent, Actor actor)
	{
		if (parent == null)
		{
			return null;
		}

		Transform borrowed = TryCreateBorrowedGenealogyContent(window, parent);
		if (borrowed != null)
		{
			RefreshContent(borrowed, actor);
			return borrowed;
		}

		return null;
	}

	private static Transform TryCreateBorrowedGenealogyContent(UnitWindow window, Transform parent)
	{
		if (!XjNativeGenealogyTemplateProvider.TryClone(
			window,
			parent,
			"XjQianKunDaiTabRoot",
			7,
			out XjNativeGenealogyClone clone))
		{
			return null;
		}

		for (int i = 0; i < clone.Sections.Count; i++)
		{
			clone.Sections[i].gameObject.SetActive(i < 7);
		}
		SetBorrowedTitle(clone.Root, clone.Sections);
		clone.Root.gameObject.AddComponent<XjQianKunDaiGenealogyMarker>();
		return clone.Root;
	}

	internal static void RefreshContent(Transform root, Actor actor)
	{
		if (root == null)
		{
			return;
		}

		if (root.GetComponent<XjQianKunDaiGenealogyMarker>() != null)
		{
			RefreshBorrowedGenealogyContent(root, actor);
			return;
		}

		// 乾坤袋只使用家谱模板，不再维护第二套自建窗口布局。
	}

	private static void RefreshBorrowedGenealogyContent(Transform root, Actor actor)
	{
		System.Collections.Generic.List<Transform> sections =
			XjNativeGenealogyTemplateProvider.CollectSectionRoots(root, null);
		if (sections.Count < 7)
		{
			return;
		}
		SetBorrowedTitle(root, sections);

		XjQianKunDaiDisplayModel model = XjQianKunDaiTabFormatter.BuildDisplayModelForActor(actor);
		for (int i = 0; i < sections.Count; i++)
		{
			sections[i].gameObject.SetActive(i < 7);
		}

		Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");

		XjAlchemyUiItem[] materials = System.Array.Empty<XjAlchemyUiItem>();
		XjAlchemyUiItem[] pills = System.Array.Empty<XjAlchemyUiItem>();
		XjAlchemyUiItem[] recipes = System.Array.Empty<XjAlchemyUiItem>();
		XjCraftInventoryUiItem[] talismans = System.Array.Empty<XjCraftInventoryUiItem>();
		XjCraftInventoryUiItem[] formationMaterials = System.Array.Empty<XjCraftInventoryUiItem>();
		if (XjAlchemyOwnerResolver.TryGetPersonal(actor, out XjAlchemyOwnerKey personalOwner))
		{
			int year = World.world?.map_stats?.year ?? 0;
			XjAlchemyInventorySnapshot alchemySnapshot = XjAlchemyUiModel.ReadSnapshot(personalOwner, year);
			materials = XjAlchemyUiModel.BuildMaterials(alchemySnapshot);
			pills = XjAlchemyUiModel.BuildPills(alchemySnapshot);
			recipes = XjAlchemyUiModel.BuildRecipes(alchemySnapshot);
		}
		if (actor?.data != null)
		{
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId > 0L)
			{
				XjCraftOwnerKey personalCraftOwner = new XjCraftOwnerKey(XjCraftOwnerScope.Personal, actorId);
				talismans = XjCraftInventoryUiModel.BuildPersonalTalismanInventory(personalCraftOwner, actorId);
				formationMaterials = XjCraftInventoryUiModel.BuildFormationMaterials(personalCraftOwner);
			}
		}

		RenderAlchemySection(sections[0], "乾坤袋·药材", materials, font, "QianKunDaiMaterialSlot");
		RenderEmptySection(sections[1], "乾坤袋·重宝", font);

		// Section 2: 功法
		var gongFaItems = BuildGongFaItems(model);
		if (XjNativeGenealogySectionRenderer.TryPrepare(sections[2], "乾坤袋·功法", font,
			XjIconShelfRenderer.CalculateMetrics(gongFaItems.Length, true),
			out Transform gongFaGrid))
		{
			for (int i = 0; i < gongFaItems.Length; i++)
			{
				var item = gongFaItems[i];
				Sprite sprite = XjIconLoader.TryLoadGongFaResourceSprite(item)
					?? SpriteTextureLoader.getSprite("ui/icons/iconBook")
					?? SpriteTextureLoader.getSprite("ui/icons/iconJianBook");
				XjTooltipBuilder.BuildGongFaTooltip(item, out string title, out string description, out string details);
				XjIconShelfRenderer.CreateIconSlot(
					gongFaGrid, "QianKunDaiGongFaSlot", sprite,
					string.IsNullOrWhiteSpace(item.Name) ? "功" : item.Name,
					item.Count, title, description, details, font);
			}
		}

		RenderCraftSection(sections[3], "乾坤袋·符箓", talismans, font, "QianKunDaiTalismanSlot");
		RenderCraftSection(sections[4], "乾坤袋·阵法材料", formationMaterials, font, "QianKunDaiFormationSlot");
		RenderAlchemySection(sections[5], "乾坤袋·丹药", pills, font, "QianKunDaiPillSlot");
		RenderAlchemySection(sections[6], "乾坤袋·丹方", recipes, font, "QianKunDaiRecipeSlot");

		XjNativeGenealogyTemplateProvider.ConvergeLayout(root, sections, 7);
	}


	private static void RenderAlchemySection(Transform section, string title, XjAlchemyUiItem[] items, Font font, string objectName)
	{
		items ??= System.Array.Empty<XjAlchemyUiItem>();
		if (!XjNativeGenealogySectionRenderer.TryPrepare(section, title, font,
			XjIconShelfRenderer.CalculateMetrics(items.Length, true), out Transform grid)) return;
		for (int i = 0; i < items.Length; i++) XjAlchemyUiModel.CreateIconCell(grid, items[i], font, objectName);
	}

	private static void RenderCraftSection(Transform section, string title, XjCraftInventoryUiItem[] items, Font font, string objectName)
	{
		items ??= System.Array.Empty<XjCraftInventoryUiItem>();
		if (!XjNativeGenealogySectionRenderer.TryPrepare(section, title, font,
			XjIconShelfRenderer.CalculateMetrics(items.Length, true), out Transform grid)) return;
		for (int i = 0; i < items.Length; i++) XjCraftInventoryUiModel.CreateIconCell(grid, items[i], font, objectName);
	}

	private static void RenderEmptySection(Transform section, string title, Font font)
	{
		XjNativeGenealogySectionRenderer.TryPrepare(section, title, font,
			XjIconShelfRenderer.CalculateMetrics(0, true), out _);
	}

	private static XjFamilyGongFaWarehouseUIItem[] BuildGongFaItems(in XjQianKunDaiDisplayModel model)
	{
		System.Collections.Generic.List<XjFamilyGongFaWarehouseUIItem> items =
			new System.Collections.Generic.List<XjFamilyGongFaWarehouseUIItem>(GongFaDisplayCapacity);
		XjQianKunDaiDisplayItem[] source = model.Items ?? System.Array.Empty<XjQianKunDaiDisplayItem>();
		for (int i = 0; i < source.Length; i++)
		{
			if (IsDisplaySectionCategory(source[i].Category, XjQianKunDaiRegistry.CategoryGongFa))
			{
				items.Add(BuildWarehouseTooltipItem(source[i]));
			}
		}
		System.Collections.Generic.List<XjFamilyGongFaWarehouseUIItem> merged =
			XjFamilyGongFaWarehouseUI.CoalesceGongFaDisplayItems(items);
		if (merged.Count <= GongFaDisplayCapacity)
		{
			return merged.ToArray();
		}

		XjFamilyGongFaWarehouseUIItem[] result = new XjFamilyGongFaWarehouseUIItem[GongFaDisplayCapacity];
		for (int i = 0; i < result.Length; i++)
		{
			result[i] = merged[i];
		}
		return result;
	}

	private static XjFamilyCaiQiWarehouseUIItem[] BuildCaiQiItems(in XjQianKunDaiDisplayModel model)
	{
		System.Collections.Generic.List<XjFamilyCaiQiWarehouseUIItem> items =
			new System.Collections.Generic.List<XjFamilyCaiQiWarehouseUIItem>();
		XjQianKunDaiDisplayItem[] source = model.Items ?? System.Array.Empty<XjQianKunDaiDisplayItem>();
		for (int i = 0; i < source.Length; i++)
		{
			if (string.Equals(source[i].Category, XjQianKunDaiRegistry.CategoryCaiQi, System.StringComparison.Ordinal))
			{
				items.Add(new XjFamilyCaiQiWarehouseUIItem(source[i].ItemId, source[i].Name, source[i].Count));
			}
		}
		return items.ToArray();
	}

	private static void SetBorrowedTitle(Transform root, System.Collections.Generic.List<Transform> sections)
	{
		Text[] texts = root.GetComponentsInChildren<Text>(true);
		for (int i = 0; i < texts.Length; i++)
		{
			if (texts[i] == null || IsInsideAnySection(texts[i].transform, sections))
			{
				continue;
			}

			texts[i].text = "乾坤袋";
			texts[i].font = LocalizedTextManager.current_font ?? texts[i].font;
			LocalizedText localized = texts[i].GetComponent<LocalizedText>();
			if (localized != null)
			{
				localized.enabled = false;
			}
			return;
		}
	}

	private static bool IsInsideAnySection(Transform target, System.Collections.Generic.List<Transform> sections)
	{
		for (int i = 0; i < sections.Count; i++)
		{
			Transform current = target;
			while (current != null)
			{
				if (current == sections[i])
				{
					return true;
				}
				current = current.parent;
			}
		}
		return false;
	}

	private static bool IsDisplaySectionCategory(string itemCategory, string sectionCategory)
	{
		if (string.Equals(itemCategory, sectionCategory, System.StringComparison.Ordinal))
		{
			return true;
		}

		return string.Equals(sectionCategory, XjQianKunDaiRegistry.CategoryGongFa, System.StringComparison.Ordinal)
			&& (string.Equals(itemCategory, XjQianKunDaiTabFormatter.DisplayCategoryQiuJinFa, System.StringComparison.Ordinal)
				|| string.Equals(itemCategory, XjQianKunDaiTabFormatter.DisplayCategoryCaiQiFa, System.StringComparison.Ordinal));
	}

	private static XjFamilyGongFaWarehouseUIItem BuildWarehouseTooltipItem(in XjQianKunDaiDisplayItem item)
	{
		string sourceType = string.Equals(item.Category, XjQianKunDaiTabFormatter.DisplayCategoryQiuJinFa, System.StringComparison.Ordinal)
			? XjFamilyGongFaWarehouse.SourceTypeQiuJinFa
			: string.Equals(item.Category, XjQianKunDaiTabFormatter.DisplayCategoryCaiQiFa, System.StringComparison.Ordinal)
				? XjFamilyCaiQiWarehouse.ResourceTypeCaiQiFa
				: XjFamilyGongFaWarehouse.SourceTypeGongFa;
		string boundGongFa = string.Equals(sourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, System.StringComparison.Ordinal)
			? item.Source
			: string.Empty;
		return new XjFamilyGongFaWarehouseUIItem(
			item.Name,
			item.GongFaGrade > 0 ? item.GongFaGrade : 4,
			item.Count,
			sourceType,
			item.DaoTu,
			boundGongFa,
			item.ItemId.StartsWith("gongfa.inheritance.", System.StringComparison.Ordinal) ? item.Source : string.Empty,
			string.Empty);
	}

}

internal sealed class XjQianKunDaiGenealogyMarker : MonoBehaviour
{
}
