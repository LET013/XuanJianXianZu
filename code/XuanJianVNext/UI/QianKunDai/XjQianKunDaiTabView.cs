using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.Alchemy;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Craft;

namespace XuanJianVNext.UI.QianKunDai;

internal readonly struct XjQianKunDaiRenderSnapshot
{
	internal readonly long ActorId;
	internal readonly int Signature;
	internal readonly XjQianKunDaiDisplayModel Model;
	internal readonly XjAlchemyUiItem[] Materials;
	internal readonly XjAlchemyUiItem[] Pills;
	internal readonly XjAlchemyUiItem[] Recipes;
	internal readonly XjCraftInventoryUiItem[] Talismans;
	internal readonly XjCraftInventoryUiItem[] FormationMaterials;

	internal XjQianKunDaiRenderSnapshot(
		long actorId,
		int signature,
		in XjQianKunDaiDisplayModel model,
		XjAlchemyUiItem[] materials,
		XjAlchemyUiItem[] pills,
		XjAlchemyUiItem[] recipes,
		XjCraftInventoryUiItem[] talismans,
		XjCraftInventoryUiItem[] formationMaterials)
	{
		ActorId = actorId;
		Signature = signature;
		Model = model;
		Materials = materials ?? Array.Empty<XjAlchemyUiItem>();
		Pills = pills ?? Array.Empty<XjAlchemyUiItem>();
		Recipes = recipes ?? Array.Empty<XjAlchemyUiItem>();
		Talismans = talismans ?? Array.Empty<XjCraftInventoryUiItem>();
		FormationMaterials = formationMaterials ?? Array.Empty<XjCraftInventoryUiItem>();
	}
}

internal static class XjQianKunDaiTabView
{
	private const int SectionCount = 7;
	private const int GongFaDisplayCapacity = 7;

	internal static Transform CreateTabContent(UnitWindow window, Transform parent, Actor actor)
	{
		if (parent == null) return null;
		if (!XjNativeGenealogyTemplateProvider.TryClone(
			window,
			parent,
			"XjQianKunDaiTabRoot",
			SectionCount,
			out XjNativeGenealogyClone clone))
		{
			return null;
		}

		for (int i = 0; i < clone.Sections.Count; i++) clone.Sections[i].gameObject.SetActive(i < SectionCount);
		SetBorrowedTitle(clone.Root, clone.Sections);
		clone.Root.gameObject.AddComponent<XjQianKunDaiGenealogyMarker>();
		XjQianKunDaiTabController controller = clone.Root.gameObject.AddComponent<XjQianKunDaiTabController>();
		controller.Initialize(window, clone.Root);
		controller.Bind(window, actor, force: false);
		return clone.Root;
	}

	internal static void Bind(Transform root, UnitWindow window, Actor actor, bool force)
	{
		if (root == null) return;
		XjQianKunDaiTabController controller = root.GetComponent<XjQianKunDaiTabController>();
		if (controller == null) return;
		controller.Bind(window, actor, force);
	}

	/// <summary>
	/// 兼容旧调用。只提交下一帧刷新，不在当前 UnitWindow 回调栈内销毁格子。
	/// </summary>
	internal static void RefreshContent(Transform root, Actor actor)
	{
		Bind(root, null, actor, force: true);
	}

	internal static bool TryBuildSnapshot(Actor actor, out XjQianKunDaiRenderSnapshot snapshot)
	{
		snapshot = default;
		if (!XjSafeCore.IsAliveActor(actor) || actor.data is not BaseSystemData data || data.id <= 0L) return false;
		long actorId = data.id;

		XjQianKunDaiDisplayModel model;
		try { model = XjQianKunDaiTabFormatter.BuildDisplayModelForActor(actor); }
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("QianKunDai.BuildModel", ex);
			model = new XjQianKunDaiDisplayModel(false, actor.getName(), 1, 0, Array.Empty<XjQianKunDaiDisplayItem>());
		}

		XjAlchemyUiItem[] materials = Array.Empty<XjAlchemyUiItem>();
		XjAlchemyUiItem[] pills = Array.Empty<XjAlchemyUiItem>();
		XjAlchemyUiItem[] recipes = Array.Empty<XjAlchemyUiItem>();
		try
		{
			if (XjAlchemyOwnerResolver.TryGetPersonal(actor, out XjAlchemyOwnerKey personalOwner))
			{
				int year = World.world?.map_stats?.year ?? 0;
				XjAlchemyInventorySnapshot alchemySnapshot = XjAlchemyUiModel.ReadSnapshot(personalOwner, year);
				materials = XjAlchemyUiModel.BuildMaterials(alchemySnapshot);
				pills = XjAlchemyUiModel.BuildPills(alchemySnapshot);
				recipes = XjAlchemyUiModel.BuildRecipes(alchemySnapshot);
			}
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("QianKunDai.AlchemySnapshot", ex);
		}

		XjCraftInventoryUiItem[] talismans = Array.Empty<XjCraftInventoryUiItem>();
		XjCraftInventoryUiItem[] formationMaterials = Array.Empty<XjCraftInventoryUiItem>();
		try
		{
			XjCraftOwnerKey personalCraftOwner = new XjCraftOwnerKey(XjCraftOwnerScope.Personal, actorId);
			talismans = XjCraftInventoryUiModel.BuildPersonalTalismanInventory(personalCraftOwner, actorId);
			formationMaterials = XjCraftInventoryUiModel.BuildFormationMaterials(personalCraftOwner);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("QianKunDai.CraftSnapshot", ex);
		}

		int signature = ComputeSignature(actorId, in model, materials, pills, recipes, talismans, formationMaterials);
		snapshot = new XjQianKunDaiRenderSnapshot(
			actorId,
			signature,
			in model,
			materials,
			pills,
			recipes,
			talismans,
			formationMaterials);
		return true;
	}

	internal static bool ApplySnapshot(Transform root, in XjQianKunDaiRenderSnapshot snapshot)
	{
		if (root == null || !root.gameObject.activeInHierarchy) return false;
		List<Transform> sections = XjNativeGenealogyTemplateProvider.CollectSectionRoots(root, null);
		if (sections.Count < SectionCount) return false;
		SetBorrowedTitle(root, sections);
		for (int i = 0; i < sections.Count; i++) sections[i].gameObject.SetActive(i < SectionCount);
		Font font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

		RenderAlchemySection(sections[0], "乾坤袋·药材", snapshot.Materials, font, "QianKunDaiMaterialSlot");
		RenderEmptySection(sections[1], "乾坤袋·重宝", font);
		XjFamilyGongFaWarehouseUIItem[] gongFaItems = BuildGongFaItems(snapshot.Model);
		if (XjNativeGenealogySectionRenderer.TryPrepare(
			sections[2],
			"乾坤袋·功法",
			font,
			XjIconShelfRenderer.CalculateMetrics(gongFaItems.Length, true),
			out Transform gongFaGrid))
		{
			for (int i = 0; i < gongFaItems.Length; i++)
			{
				XjFamilyGongFaWarehouseUIItem item = gongFaItems[i];
				Sprite sprite = XjIconLoader.TryLoadGongFaResourceSprite(item)
					?? SpriteTextureLoader.getSprite("ui/icons/iconBook")
					?? SpriteTextureLoader.getSprite("ui/icons/iconJianBook");
				XjTooltipBuilder.BuildGongFaTooltip(item, out string title, out string description, out string details);
				XjIconShelfRenderer.CreateIconSlot(
					gongFaGrid,
					"QianKunDaiGongFaSlot",
					sprite,
					string.IsNullOrWhiteSpace(item.Name) ? "功" : item.Name,
					item.Count,
					title,
					description,
					details,
					font,
					blockWorldInput: true);
			}
		}
		RenderCraftSection(sections[3], "乾坤袋·符箓", snapshot.Talismans, font, "QianKunDaiTalismanSlot");
		RenderCraftSection(sections[4], "乾坤袋·阵法材料", snapshot.FormationMaterials, font, "QianKunDaiFormationSlot");
		RenderAlchemySection(sections[5], "乾坤袋·丹药", snapshot.Pills, font, "QianKunDaiPillSlot");
		RenderAlchemySection(sections[6], "乾坤袋·丹方", snapshot.Recipes, font, "QianKunDaiRecipeSlot");
		XjNativeGenealogyTemplateProvider.ConvergeLayout(root, sections, SectionCount);
		XjNativeHoverTooltip.RepairHierarchy(root);
		return true;
	}

	private static void RenderAlchemySection(Transform section, string title, XjAlchemyUiItem[] items, Font font, string objectName)
	{
		items ??= Array.Empty<XjAlchemyUiItem>();
		if (!XjNativeGenealogySectionRenderer.TryPrepare(
			section,
			title,
			font,
			XjIconShelfRenderer.CalculateMetrics(items.Length, true),
			out Transform grid)) return;
		for (int i = 0; i < items.Length; i++) XjAlchemyUiModel.CreateIconCell(grid, items[i], font, objectName, blockWorldInput: true);
	}

	private static void RenderCraftSection(Transform section, string title, XjCraftInventoryUiItem[] items, Font font, string objectName)
	{
		items ??= Array.Empty<XjCraftInventoryUiItem>();
		if (!XjNativeGenealogySectionRenderer.TryPrepare(
			section,
			title,
			font,
			XjIconShelfRenderer.CalculateMetrics(items.Length, true),
			out Transform grid)) return;
		for (int i = 0; i < items.Length; i++) XjCraftInventoryUiModel.CreateIconCell(grid, items[i], font, objectName, blockWorldInput: true);
	}

	private static void RenderEmptySection(Transform section, string title, Font font)
	{
		XjNativeGenealogySectionRenderer.TryPrepare(
			section,
			title,
			font,
			XjIconShelfRenderer.CalculateMetrics(0, true),
			out _);
	}

	private static XjFamilyGongFaWarehouseUIItem[] BuildGongFaItems(in XjQianKunDaiDisplayModel model)
	{
		List<XjFamilyGongFaWarehouseUIItem> items = new List<XjFamilyGongFaWarehouseUIItem>(GongFaDisplayCapacity);
		XjQianKunDaiDisplayItem[] source = model.Items ?? Array.Empty<XjQianKunDaiDisplayItem>();
		for (int i = 0; i < source.Length; i++)
		{
			if (IsDisplaySectionCategory(source[i].Category, XjQianKunDaiRegistry.CategoryGongFa)) items.Add(BuildWarehouseTooltipItem(source[i]));
		}
		List<XjFamilyGongFaWarehouseUIItem> merged = XjFamilyGongFaWarehouseUI.CoalesceGongFaDisplayItems(items);
		if (merged.Count <= GongFaDisplayCapacity) return merged.ToArray();
		XjFamilyGongFaWarehouseUIItem[] result = new XjFamilyGongFaWarehouseUIItem[GongFaDisplayCapacity];
		for (int i = 0; i < result.Length; i++) result[i] = merged[i];
		return result;
	}

	private static void SetBorrowedTitle(Transform root, IReadOnlyList<Transform> sections)
	{
		Text[] texts = root.GetComponentsInChildren<Text>(true);
		for (int i = 0; i < texts.Length; i++)
		{
			if (texts[i] == null || IsInsideAnySection(texts[i].transform, sections)) continue;
			texts[i].text = "乾坤袋";
			texts[i].font = LocalizedTextManager.current_font ?? texts[i].font;
			LocalizedText localized = texts[i].GetComponent<LocalizedText>();
			if (localized != null) localized.enabled = false;
			return;
		}
	}

	private static bool IsInsideAnySection(Transform target, IReadOnlyList<Transform> sections)
	{
		for (int i = 0; i < sections.Count; i++)
		{
			for (Transform current = target; current != null; current = current.parent)
			{
				if (current == sections[i]) return true;
			}
		}
		return false;
	}

	private static bool IsDisplaySectionCategory(string itemCategory, string sectionCategory)
	{
		if (string.Equals(itemCategory, sectionCategory, StringComparison.Ordinal)) return true;
		return string.Equals(sectionCategory, XjQianKunDaiRegistry.CategoryGongFa, StringComparison.Ordinal)
			&& (string.Equals(itemCategory, XjQianKunDaiTabFormatter.DisplayCategoryQiuJinFa, StringComparison.Ordinal)
				|| string.Equals(itemCategory, XjQianKunDaiTabFormatter.DisplayCategoryCaiQiFa, StringComparison.Ordinal));
	}

	private static XjFamilyGongFaWarehouseUIItem BuildWarehouseTooltipItem(in XjQianKunDaiDisplayItem item)
	{
		string sourceType = string.Equals(item.Category, XjQianKunDaiTabFormatter.DisplayCategoryQiuJinFa, StringComparison.Ordinal)
			? XjFamilyGongFaWarehouse.SourceTypeQiuJinFa
			: string.Equals(item.Category, XjQianKunDaiTabFormatter.DisplayCategoryCaiQiFa, StringComparison.Ordinal)
				? XjFamilyCaiQiWarehouse.ResourceTypeCaiQiFa
				: XjFamilyGongFaWarehouse.SourceTypeGongFa;
		string boundGongFa = string.Equals(sourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, StringComparison.Ordinal) ? item.Source : string.Empty;
		return new XjFamilyGongFaWarehouseUIItem(
			item.Name,
			item.GongFaGrade > 0 ? item.GongFaGrade : 4,
			item.Count,
			sourceType,
			item.DaoTu,
			boundGongFa,
			item.ItemId.StartsWith("gongfa.inheritance.", StringComparison.Ordinal) ? item.Source : string.Empty,
			string.Empty);
	}

	private static int ComputeSignature(
		long actorId,
		in XjQianKunDaiDisplayModel model,
		IReadOnlyList<XjAlchemyUiItem> materials,
		IReadOnlyList<XjAlchemyUiItem> pills,
		IReadOnlyList<XjAlchemyUiItem> recipes,
		IReadOnlyList<XjCraftInventoryUiItem> talismans,
		IReadOnlyList<XjCraftInventoryUiItem> formationMaterials)
	{
		unchecked
		{
			int hash = 17;
			hash = hash * 31 + actorId.GetHashCode();
			hash = hash * 31 + model.UpdatedYear;
			XjQianKunDaiDisplayItem[] items = model.Items ?? Array.Empty<XjQianKunDaiDisplayItem>();
			for (int i = 0; i < items.Length; i++)
			{
				hash = hash * 31 + StringComparer.Ordinal.GetHashCode(items[i].Category ?? string.Empty);
				hash = hash * 31 + StringComparer.Ordinal.GetHashCode(items[i].ItemId ?? string.Empty);
				hash = hash * 31 + items[i].Count;
			}
			AppendSignature(ref hash, materials);
			AppendSignature(ref hash, pills);
			AppendSignature(ref hash, recipes);
			AppendSignature(ref hash, talismans);
			AppendSignature(ref hash, formationMaterials);
			return hash;
		}
	}

	private static void AppendSignature(ref int hash, IReadOnlyList<XjAlchemyUiItem> items)
	{
		for (int i = 0; i < items.Count; i++)
		{
			hash = hash * 31 + StringComparer.Ordinal.GetHashCode(items[i].Id ?? string.Empty);
			hash = hash * 31 + items[i].Count;
		}
	}

	private static void AppendSignature(ref int hash, IReadOnlyList<XjCraftInventoryUiItem> items)
	{
		for (int i = 0; i < items.Count; i++)
		{
			hash = hash * 31 + StringComparer.Ordinal.GetHashCode(items[i].Id ?? string.Empty);
			hash = hash * 31 + items[i].Count;
		}
	}
}

internal sealed class XjQianKunDaiTabController : MonoBehaviour
{
	private UnitWindow _window;
	private Transform _root;
	private long _actorId;
	private bool _initialized;
	private bool _pending;
	private bool _force;
	private int _executeFrame;
	private int _lastSignature;
	private long _lastRenderedActorId;

	internal void Initialize(UnitWindow window, Transform root)
	{
		_window = window;
		_root = root;
		_initialized = root != null;
	}

	internal void Bind(UnitWindow window, Actor actor, bool force)
	{
		if (!_initialized || actor?.data is not BaseSystemData data || data.id <= 0L) return;
		_window = window ?? _window;
		bool actorChanged = data.id != _actorId;
		_actorId = data.id;
		_pending = true;
		_force |= force || actorChanged;
		_executeFrame = Time.frameCount + 1;
		enabled = true;
	}

	private void OnEnable()
	{
		if (_initialized && _actorId > 0L)
		{
			_pending = true;
			_executeFrame = Time.frameCount + 1;
			enabled = true;
		}
	}

	private void LateUpdate()
	{
		if (!_pending || Time.frameCount < _executeFrame) return;
		if (XjUiInputBlocker.IsInteractionActive)
		{
			_executeFrame = Time.frameCount + 1;
			return;
		}

		_pending = false;
		enabled = false;
		if (!TryResolveBoundActor(out Actor actor) || !IsBindingCurrent(actor)) return;
		if (!XjQianKunDaiTabView.TryBuildSnapshot(actor, out XjQianKunDaiRenderSnapshot snapshot)) return;
		if (!IsBindingCurrent(actor) || snapshot.ActorId != _actorId) return;
		if (!_force && _lastRenderedActorId == _actorId && _lastSignature == snapshot.Signature) return;
		_force = false;
		try
		{
			if (XjQianKunDaiTabView.ApplySnapshot(_root, in snapshot))
			{
				_lastRenderedActorId = _actorId;
				_lastSignature = snapshot.Signature;
			}
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("QianKunDai.Render", ex);
		}
	}

	private bool TryResolveBoundActor(out Actor actor)
	{
		actor = null;
		return _actorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(_actorId, out actor)
			&& XjSafeCore.IsAliveActor(actor);
	}

	private bool IsBindingCurrent(Actor actor)
	{
		if (!_initialized || _root == null || !_root.gameObject.activeInHierarchy || !XjSafeCore.IsAliveActor(actor)) return false;
		if (_window?.actor?.data == null) return true;
		return ((BaseSystemData)_window.actor.data).id == _actorId;
	}
}

internal sealed class XjQianKunDaiGenealogyMarker : MonoBehaviour
{
}
