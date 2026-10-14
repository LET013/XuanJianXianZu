using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.UI.Common;
using XuanJianVNext.Systems.Rank;

namespace XuanJianVNext.UI.Rank;

internal static partial class XjRankWindow
{private static void RefreshFilterChoices()
	{
		// 一次窗口刷新只构建一次候选角色快照，避免三个筛选分类重复执行排行榜读模型。
		List<Actor> actors = GetRankCandidateActors();
		if (actors.Count == 0)
		{
			RefreshKingdomFilters(actors);
			RefreshAssetFilters(actors);
			RefreshTraitFilters(actors);
			filterChoicesInitialized = false;
			return;
		}

		RefreshKingdomFilters(actors);
		RefreshAssetFilters(actors);
		RefreshTraitFilters(actors);
		filterChoicesInitialized = HasAnyAvailableFilterButton();
	}

private static bool ShouldRefreshFilterChoicesOnOpen()
	{
		return !filterChoicesInitialized || !HasAnyAvailableFilterButton();
	}

private static bool HasAnyAvailableFilterButton()
	{
		return kingdomFilterButtons.Count > 0
			|| assetFilterButtons.Count > 0
			|| traitFilterButtons.Count > 0;
	}

private static List<Actor> GetRankCandidateActors()
	{
		List<XjRankItem> items = new List<XjRankItem>(XjRankReadModel.Build());
		List<Actor> actors = new List<Actor>(items.Count);
		HashSet<long> ids = new HashSet<long>();
		for (int i = 0; i < items.Count; i++)
		{
			Actor actor = items[i].Actor;
			if (actor?.data == null) continue;
			long id = ((BaseSystemData)actor.data).id;
			if (ids.Add(id)) actors.Add(actor);
		}
		return actors;
	}

private static void RefreshKingdomFilters(IReadOnlyList<Actor> actors)
	{
		ClearObjectList(kingdomFilterButtons);
		if (kingdomFilterContainer == null) return;
		HashSet<long> seen = new HashSet<long>();
		actors ??= Array.Empty<Actor>();
		for (int i = 0; i < actors.Count; i++)
		{
			if (IsLongShuActor(actors[i]))
			{
				continue;
			}
			Kingdom kingdom = actors[i].kingdom;
			if (kingdom?.data == null || IsSuppressedKingdomFilter(kingdom) || !seen.Add(kingdom.data.id)) continue;
			GameObject button = CreateKingdomFilterButton(kingdom);
			if (button != null) kingdomFilterButtons.Add(button);
		}
		RefreshDynamicGridLayout(kingdomFilterContainer);
	}

	private static bool IsSuppressedKingdomFilter(Kingdom kingdom)
	{
		string name = (kingdom?.data?.name ?? string.Empty).Trim();
		return string.Equals(name, "dragons", StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith("nomads_", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsLongShuActor(Actor actor)
	{
		return string.Equals(actor?.asset?.id, "longshu", StringComparison.Ordinal);
	}

private static GameObject CreateKingdomFilterButton(Kingdom kingdom)
	{
		if (kingdom?.data == null || kingdomFilterContainer == null) return null;
		var colorAsset = kingdom.getColor();
		Color mainColor = colorAsset?.getColorMainSecond() ?? Color.gray;
		Color bannerColor = colorAsset?.getColorBanner() ?? Color.white;
		Sprite background = GetSafeKingdomBackground(kingdom);
		Sprite icon = GetSafeKingdomIcon(kingdom);
		Image image = CreateImage("Kingdom_" + kingdom.data.id.ToString(CultureInfo.InvariantCulture), kingdomFilterContainer, background, mainColor, new Vector2(SideGridCellSize, SideGridCellSize));
		image.type = Image.Type.Sliced;
		Image inner = CreateImage("Icon", image.transform, icon, bannerColor, Vector2.zero);
		inner.preserveAspect = true;
		inner.raycastTarget = false;
		SetRect(inner.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(3, 3), offsetMax: new Vector2(-3, -3));
		Button button = image.gameObject.AddComponent<Button>();
		button.targetGraphic = image;
		button.transition = Selectable.Transition.None;
		long kingdomId = kingdom.data.id;
		string displayName = string.IsNullOrWhiteSpace(kingdom.data.name) ? "未知国家" : kingdom.data.name;
		button.onClick.AddListener(() => AddFilter(
			"kingdom_" + kingdomId.ToString(CultureInfo.InvariantCulture),
			displayName,
			background,
			icon,
			mainColor,
			bannerColor,
			actor => actor?.kingdom?.data != null && actor.kingdom.data.id == kingdomId));
		image.gameObject.AddComponent<XjRankTooltipTrigger>().TooltipText = displayName;
		return image.gameObject;
	}

private static void RefreshAssetFilters(IReadOnlyList<Actor> actors)
	{
		ClearObjectList(assetFilterButtons);
		if (assetFilterContainer == null) return;
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		actors ??= Array.Empty<Actor>();
		for (int i = 0; i < actors.Count; i++)
		{
			ActorAsset asset = actors[i].asset;
			if (asset == null || string.IsNullOrWhiteSpace(asset.id) || !seen.Add(asset.id)) continue;
			GameObject button = CreateAssetFilterButton(asset);
			if (button != null) assetFilterButtons.Add(button);
		}
		RefreshDynamicGridLayout(assetFilterContainer);
	}

private static GameObject CreateAssetFilterButton(ActorAsset asset)
	{
		if (asset == null || assetFilterContainer == null) return null;
		Sprite icon = GetAssetIcon(asset);
		string displayName = GetAssetDisplayName(asset);
		Image image = CreateImage("Asset_" + asset.id, assetFilterContainer, icon, Color.white, new Vector2(SideGridCellSize, SideGridCellSize));
		image.preserveAspect = true;
		Button button = image.gameObject.AddComponent<Button>();
		button.targetGraphic = image;
		button.transition = Selectable.Transition.None;
		string assetId = asset.id;
		button.onClick.AddListener(() => AddFilter(
			"asset_" + assetId,
			displayName,
			icon,
			null,
			Color.white,
			Color.white,
			actor => actor?.asset != null && string.Equals(actor.asset.id, assetId, StringComparison.Ordinal)));
		image.gameObject.AddComponent<XjRankTooltipTrigger>().TooltipText = displayName;
		return image.gameObject;
	}

private static void RefreshTraitFilters(IReadOnlyList<Actor> actors)
	{
		ClearObjectList(traitFilterButtons);
		if (traitFilterContainer == null) return;
		HashSet<string> traitIds = new HashSet<string>(StringComparer.Ordinal);
		actors ??= Array.Empty<Actor>();
		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			if (actor?.data == null) continue;
			if (actor.traits != null)
			{
				foreach (ActorTrait trait in actor.traits)
				{
					if (trait != null && !string.IsNullOrWhiteSpace(trait.id)) traitIds.Add(trait.id);
				}
			}
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) traitIds.Add("XjRealm13");
			if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)) traitIds.Add("XjRealm6");
			if (string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) traitIds.Add("XjRealm14");
		}
		List<string> ordered = new List<string>(traitIds);
		ordered.Sort(StringComparer.Ordinal);
		for (int i = 0; i < ordered.Count; i++)
		{
			ActorTrait trait = AssetManager.traits?.get(ordered[i]);
			if (trait == null) continue;
			GameObject button = CreateTraitFilterButton(trait);
			if (button != null) traitFilterButtons.Add(button);
		}
		RefreshDynamicGridLayout(traitFilterContainer);
	}

private static void RefreshDynamicGridLayout(Transform gridTransform, int minimumRows = 0)
	{
		if (gridTransform == null) return;
		RectTransform gridRect = gridTransform as RectTransform ?? gridTransform.GetComponent<RectTransform>();
		GridLayoutGroup layout = gridTransform.GetComponent<GridLayoutGroup>();
		LayoutElement element = gridTransform.GetComponent<LayoutElement>();
		if (gridRect == null || layout == null || element == null) return;

		int columns = Mathf.Max(1, layout.constraintCount);
		// 侧栏整体缩放后，图标尺寸不再写死。优先以当前 GridContainer 的真实宽度
		// 均分列宽，窗口尺寸或字体缩放变化时仍能保持方形图标和整齐间距。
		float actualWidth = 0f;
		if (gridTransform.parent is RectTransform gridParentRect) actualWidth = gridParentRect.rect.width;
		if (actualWidth <= 1f && gridRect.rect.width > 1f) actualWidth = gridRect.rect.width;
		if (actualWidth > 1f)
		{
			float innerWidth = Math.Max(1f, actualWidth - 8f);
			float spacingWidth = layout.spacing.x * Math.Max(0, columns - 1);
			float cell = Mathf.Clamp((innerWidth - spacingWidth) / columns, 16f, SideGridCellSize);
			layout.cellSize = new Vector2(cell, cell);
		}
		int contentRows = gridTransform.childCount == 0 ? 0 : Mathf.CeilToInt(gridTransform.childCount / (float)columns);
		int rows = Mathf.Max(Mathf.Max(0, minimumRows), contentRows);
		float height = rows == 0
			? 0f
			: rows * layout.cellSize.y
				+ Mathf.Max(0, rows - 1) * layout.spacing.y
				+ layout.padding.top
				+ layout.padding.bottom;
		element.minHeight = height;
		element.preferredHeight = height;
		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(gridRect);
		if (gridTransform.parent is RectTransform parentRect)
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
		}
	}

private static GameObject CreateTraitFilterButton(ActorTrait trait)
	{
		if (trait == null || traitFilterContainer == null) return null;
		Sprite icon = GetTraitIcon(trait);
		string displayName = ResolveTraitDisplayName(trait);

		// 按钮本体只承担点击/选中背景，特质图标放在独立子 Image 中。
		// 这样 WorldBox/NML 后续刷新 Button targetGraphic 时不会把炼丹师图标本身覆盖掉。
		Image background = CreateImage(
			"Trait_" + trait.id,
			traitFilterContainer,
			GetSafeSprite("ui/special/darkInputFieldEmpty"),
			new Color(1f, 1f, 1f, 0.08f),
			new Vector2(SideGridCellSize, SideGridCellSize));
		background.type = Image.Type.Sliced;
		Image iconImage = CreateImage("Icon", background.transform, icon, Color.white, Vector2.zero);
		iconImage.preserveAspect = true;
		iconImage.raycastTarget = false;
		SetRect(iconImage.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(2, 2), offsetMax: new Vector2(-2, -2));

		Button button = background.gameObject.AddComponent<Button>();
		button.targetGraphic = background;
		button.transition = Selectable.Transition.None;
		string traitId = trait.id;
		string traitName = displayName;
		button.onClick.AddListener(() => AddFilter(
			"trait_" + traitId,
			traitName,
			icon,
			null,
			Color.white,
			Color.white,
			actor => ActorMatchesTraitFilter(actor, traitId)));
		background.gameObject.AddComponent<XjRankTooltipTrigger>().TooltipText = traitName;
		return background.gameObject;
	}

private static bool ActorMatchesTraitFilter(Actor actor, string traitId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(traitId)) return false;
		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		// 标准境界特质只是 RealmId 的可见投影。筛选不能先相信 actor.hasTrait，
		// 否则一个残留/手动错误图标会把角色放进错误境界筛选。
		if (IsAuthoritativeRealmTrait(traitId))
		{
			return string.Equals(realmId, traitId, StringComparison.Ordinal);
		}
		if (actor.hasTrait(traitId)) return true;
		if (string.Equals(traitId, "XjGuShiDao", StringComparison.Ordinal)
			|| string.Equals(traitId, "XjJinShiDao", StringComparison.Ordinal))
		{
			if (!XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetString(
				actor, XjActorDataKeys.ShiTradition, out string tradition)) return false;
			return string.Equals(traitId, "XjGuShiDao", StringComparison.Ordinal)
				? string.Equals(tradition, XuanJianVNext.Data.Shi.XjShiTraditionIds.Ancient, StringComparison.Ordinal)
				: string.Equals(tradition, XuanJianVNext.Data.Shi.XjShiTraditionIds.Modern, StringComparison.Ordinal);
		}
		return false;
	}
private static bool IsAuthoritativeRealmTrait(string traitId)
	{
		return string.Equals(traitId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.HuangGuan, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

private static string ResolveTraitDisplayName(ActorTrait trait)
	{
		string traitId = (trait?.id ?? string.Empty).Trim();
		if (traitId.Length == 0) return "未知特质";
		string key = "trait_" + traitId;
		try
		{
			string localized = LM.Get(key);
			if (!string.IsNullOrWhiteSpace(localized)
				&& !string.Equals(localized, key, StringComparison.Ordinal)
				&& !LooksLikeRawDisplayKey(localized))
			{
				return localized.Trim();
			}
		}
		catch (System.Exception xjCaught254) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Rank/XjRankWindow.Filters.cs:254", xjCaught254); }

		switch (traitId)
		{
			case "XjZz1": return "朽木难雕";
			case "XjZz2": return "可琢之材";
			case "XjZz3": return "璞玉之资";
			case "XjZz4": return "上乘根骨";
			case "XjZz5": return "天公垂目";
			case "XjZz6": return "天授道脉";
			case "XjRealm1": return "胎息境";
			case "XjRealm2": return "炼气境";
			case "XjRealm3": return "筑基境";
			case "XjRealm4": return "紫府境";
			case "XjRealm5": return "金丹·真君";
			case "XjRealm6": return "道胎境";
			case "XjRealm11": return "黄冠境";
			case "XjRealm12": return "真人境";
			case "XjRealm13": return "真君羽士境";
			case "XjRealm14": return "道胎境";
			case "XjAlchemyCrafter": return "炼丹师";
			case "XjArtifactRefiner": return "炼器师";
			case "XjTalismanCrafter": return "符箓师";
			case "XjFormationMaster": return "阵法师";
			case "XjGuShiDao": return "古释";
			case "XjJinShiDao": return "今释";
			default:
				return LooksLikeRawDisplayKey(traitId) ? "未知特质" : traitId;
		}
	}

private static bool LooksLikeRawDisplayKey(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return true;
		string value = text.Trim();
		if (value.StartsWith("trait_", StringComparison.Ordinal)) return true;
		if (value.StartsWith("xuanjian_", StringComparison.OrdinalIgnoreCase)) return true;
		if (value.StartsWith("xuanjian.", StringComparison.OrdinalIgnoreCase)) return true;
		if (value.StartsWith("XuanJian_config_", StringComparison.Ordinal)) return true;
		if (value.IndexOf("::", StringComparison.Ordinal) >= 0) return true;
		return false;
	}
private static void AddFilter(
		string id,
		string displayName,
		Sprite icon,
		Sprite innerIcon,
		Color iconColor,
		Color innerColor,
		Func<Actor, bool> filterFunc)
	{
		if (string.IsNullOrWhiteSpace(id) || filterFunc == null || activeFilters.Exists(f => !f.ToBeRemoved && string.Equals(f.Id, id, StringComparison.Ordinal))) return;
		activeFilters.Add(new XjRankFilterSetting(id, displayName, icon, innerIcon, iconColor, innerColor, filterFunc));
		RefreshSelectedFilters();
		RefreshCurrentList(true);
	}

private static bool SafeFilter(XjRankFilterSetting filter, Actor actor)
	{
		if (filter?.FilterFunc == null) return true;
		try { return filter.FilterFunc(actor); }
		catch { return false; }
	}

private static void RefreshSelectedFilters()
	{
		ClearObjectList(selectedFilterButtons);
		activeFilters.RemoveAll(f => f == null || f.ToBeRemoved || IsLegacyShiTraditionFilter(f.Id));
		if (selectedFiltersContainer == null) return;
		for (int i = 0; i < activeFilters.Count; i++)
		{
			selectedFilterButtons.Add(CreateSelectedFilterButton(activeFilters[i]));
		}
		RefreshDynamicGridLayout(selectedFiltersContainer);
	}

private static bool IsLegacyShiTraditionFilter(string id)
	{
		return string.Equals(id, "trait_XjGuShiDao", StringComparison.Ordinal)
			|| string.Equals(id, "trait_XjJinShiDao", StringComparison.Ordinal);
	}

private static GameObject CreateSelectedFilterButton(XjRankFilterSetting filter)
	{
		Image border = CreateImage(filter.Id, selectedFiltersContainer, GetSafeSprite("ui/special/windowInnerSliced"), filter.GetTypeColor(), new Vector2(SideGridCellSize, SideGridCellSize));
		border.type = Image.Type.Sliced;
		LayoutElement layout = border.gameObject.GetComponent<LayoutElement>() ?? border.gameObject.AddComponent<LayoutElement>();
		layout.minWidth = SideGridCellSize;
		layout.minHeight = SideGridCellSize;
		layout.preferredWidth = SideGridCellSize;
		layout.preferredHeight = SideGridCellSize;
		Image icon = CreateImage("Icon", border.transform, filter.Icon ?? GetSafeSprite("ui/icons/iconQuestionMark"), filter.IconColor, Vector2.zero);
		icon.preserveAspect = true;
		icon.raycastTarget = false;
		SetRect(icon.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(2, 2), offsetMax: new Vector2(-2, -2));
		if (filter.InnerIcon != null)
		{
			Image inner = CreateImage("Inner", icon.transform, filter.InnerIcon, filter.InnerColor, Vector2.zero);
			inner.preserveAspect = true;
			inner.raycastTarget = false;
			SetRect(inner.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(2, 2), offsetMax: new Vector2(-2, -2));
		}
		Image typeBackground = CreateImage("Type", border.transform, GetSafeSprite("ui/special/darkInputFieldEmpty"), filter.GetTypeColor(), new Vector2(13, 13));
		typeBackground.type = Image.Type.Sliced;
		typeBackground.raycastTarget = false;
		SetRect(typeBackground.gameObject, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(-1, 1), new Vector2(13, 13));
		Text typeText = CreateText("Text", typeBackground.transform, filter.GetTypeText(), 8, Color.white);
		SetRect(typeText.gameObject, Vector2.zero, Vector2.one, offsetMin: Vector2.zero, offsetMax: Vector2.zero);
		Button button = border.gameObject.AddComponent<Button>();
		button.targetGraphic = border;
		button.transition = Selectable.Transition.None;
		button.onClick.AddListener(() =>
		{
			filter.CycleType();
			border.color = filter.GetTypeColor();
			typeBackground.color = filter.GetTypeColor();
			typeText.text = filter.GetTypeText();
			RefreshCurrentList(true);
		});
		XjRankRightClickHandler rightClick = border.gameObject.AddComponent<XjRankRightClickHandler>();
		rightClick.OnRightClick = () =>
		{
			filter.ToBeRemoved = true;
			RefreshSelectedFilters();
			RefreshCurrentList(true);
		};
		XjRankTooltipTrigger tip = border.gameObject.AddComponent<XjRankTooltipTrigger>();
		tip.TooltipText = filter.DisplayName;
		tip.TooltipDescription = "左键切换 与/或/非，右键移除";
		return border.gameObject;
	}

private static void ClearAllFilters()
	{
		activeFilters.Clear();
		searchQuery = string.Empty;
		searchInput?.SetTextWithoutNotify(string.Empty);
		currentRealmFilter = 0;
		currentDaoTuFilter = 0;
		RefreshSelectedFilters();
		RefreshCurrentList(true);
	}
}
