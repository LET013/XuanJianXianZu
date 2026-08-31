using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.Alchemy;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Craft;
using XuanJianVNext.UI.LingWu;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
private void DrawFamilyWarehouseDetailPage(XjCodexSnapshot snapshot)
	{
		XjCodexFamilyItem family = FindFamily(snapshot, _familyWarehouseDetailFamilyId);
		if (family == null)
		{
			_familyWarehouseDetailFamilyId = 0L;
			DrawFamilyPage(snapshot);
			return;
		}

		string view = string.IsNullOrWhiteSpace(_familyWarehouseDetailView) ? "功法" : _familyWarehouseDetailView.Trim();
		DrawPageHeader("家族仓库 · " + family.Name, "按分类查看该家族仓库中的功法、丹药、符箓与传承器物。");
		if (GUILayout.Button("返回家族诸脉", GUILayout.Width(145f), GUILayout.Height(34f)))
		{
			_familyWarehouseDetailFamilyId = 0L;
			_scrollPosition = Vector2.zero;
			return;
		}
		DrawFamilyWarehouseViewTabs(family.FamilyId, ref view);
		GUILayout.Space(8f);
		int overviewCount = 0;
		SetWarehouseOverviewScratch(overviewCount++, "家族", family.Name, "#FFD37A");
		if (family.IsMigrationBranch)
		{
			SetWarehouseOverviewScratch(overviewCount++, "来源主家", Empty(family.SourceFamilyName, "未载"), "#FFAA66");
		}
		SetWarehouseOverviewScratch(overviewCount++, "当前仓库", view, SectResourceViewColor(view));
		SetWarehouseOverviewScratch(overviewCount++, "库存数量", CountFamilyWarehouseItemsCached(family.FamilyId, view).ToString(), SectResourceViewColor(view));
		if (view == "丹药")
		{
			CountFamilyPillsAndRecipes(family.FamilyId, out int pillCount, out int recipeCount);
			SetWarehouseOverviewScratch(overviewCount++, "成丹库存", pillCount.ToString(), pillCount > 0 ? "#A7E08A" : "#888888");
			SetWarehouseOverviewScratch(overviewCount++, "丹方收录", recipeCount.ToString(), "#FFD37A");
		}
		SetWarehouseOverviewScratch(overviewCount++, "最高境界", Empty(family.HighestRealm, "未载"), "#B7A7FF");
		DrawWarehouseOverviewPills(
			true,
			WarehouseOverviewLabelsScratch,
			WarehouseOverviewValuesScratch,
			WarehouseOverviewColorsScratch,
			overviewCount);
		GUILayout.Space(10f);
		DrawFamilyWarehouseDetailGrid(family.FamilyId, view);
	}

private static void CountFamilyPillsAndRecipes(long familyId, out int pillCount, out int recipeCount)
{
	pillCount = 0;
	recipeCount = 0;
	if (familyId <= 0L) return;
	XjAlchemyOwnerKey owner = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Family, familyId);
	XjAlchemyInventorySnapshot snapshot = XjAlchemyUiModel.ReadSnapshot(owner, ResolveCurrentYear());
	XjAlchemyUiItem[] pills = XjAlchemyUiModel.BuildPills(snapshot);
	XjAlchemyUiItem[] recipes = XjAlchemyUiModel.BuildRecipes(snapshot);
	for (int i = 0; pills != null && i < pills.Length; i++) pillCount += Math.Max(0, pills[i].Count);
	for (int i = 0; recipes != null && i < recipes.Length; i++) recipeCount += Math.Max(1, recipes[i].Count);
}

private void DrawFamilyWarehouseDetailGrid(long familyId, string view)
	{
		List<FamilyResourceDisplayGroup> groups = GetFamilyWarehouseGroupsCached(familyId, view);
		if (string.Equals(view, "药材", StringComparison.Ordinal))
		{
			DrawFamilyWarehouseSection(groups, view, "上品药材", "上品药材", "#B7A7FF", true);
			DrawFamilyWarehouseSection(groups, view, "下品药材", "下品药材", "#A7E08A", true);
			return;
		}
		if (string.Equals(view, "丹药", StringComparison.Ordinal))
		{
			DrawFamilyWarehouseSection(groups, view, "成丹库存", "成丹", "#A7E08A", true);
			DrawFamilyWarehouseSection(groups, view, "丹方收录", "丹方", "#FFD37A", true);
			return;
		}
		if (groups.Count == 0)
		{
			DrawEmptyCard("该仓库暂无库存。", "#777777");
			return;
		}
		DrawFamilyWarehouseTiles(groups, view);
	}

private void DrawFamilyWarehouseSection(List<FamilyResourceDisplayGroup> groups, string view, string title, string kind, string color, bool showEmpty)
	{
		DrawOrnamentDivider(color, title);
		List<FamilyResourceDisplayGroup> filtered = new List<FamilyResourceDisplayGroup>();
		for (int i = 0; groups != null && i < groups.Count; i++)
		{
			if (groups[i] != null && string.Equals(groups[i].Kind, kind, StringComparison.Ordinal)) filtered.Add(groups[i]);
		}
		if (filtered.Count == 0)
		{
			if (showEmpty) DrawEmptyCard(title + "暂无库存。", "#777777");
			GUILayout.Space(8f);
			return;
		}
		DrawFamilyWarehouseTiles(filtered, view);
		GUILayout.Space(8f);
	}

private void DrawFamilyWarehouseViewTabs(long familyId, ref string view)
	{
		int columns = ResolveWarehouseColumns(true, 122f, 5);
		for (int i = 0; i < FamilyWarehouseViews.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			string nextView = FamilyWarehouseViews[i];
			int count = CountFamilyWarehouseItemsCached(familyId, nextView);
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(view, nextView, StringComparison.Ordinal)
				? new Color(0.32f, 0.38f, 0.45f)
				: new Color(0.22f, 0.22f, 0.22f);
			if (GUILayout.Button(nextView + " " + count, GUILayout.Width(112f), GUILayout.Height(34f)))
			{
				_familyWarehouseDetailView = nextView;
				view = nextView;
				_scrollPosition = Vector2.zero;
			}
			GUI.backgroundColor = old;
			if (i % columns == columns - 1 || i == FamilyWarehouseViews.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

private void DrawFamilyWarehouseTiles(List<FamilyResourceDisplayGroup> groups, string view)
	{
		int shown = Mathf.Min(groups.Count, MaxSectResourceDetailItems);
		const float tileStride = 148f;
		int columns = ResolveWarehouseColumns(true, tileStride, 5);
		for (int i = 0; i < shown; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			DrawFamilyWarehouseItemTile(groups[i], view);
			if (i % columns == columns - 1 || i == shown - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		DrawListLimitNotice(groups.Count, shown);
	}

private void DrawFamilyWarehouseItemTile(FamilyResourceDisplayGroup item, string view)
	{
		const float cardWidth = 142f;
		const float innerWidth = 132f;
		GUIStyle nameStyle = new GUIStyle(GUI.skin.label)
		{
			richText = true,
			wordWrap = true,
			alignment = TextAnchor.UpperCenter,
			clipping = TextClipping.Clip
		};
		GUIStyle detailStyle = new GUIStyle(GUI.skin.label)
		{
			richText = true,
			wordWrap = true,
			alignment = TextAnchor.UpperLeft,
			clipping = TextClipping.Clip
		};
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(cardWidth), GUILayout.Height(202f));
		DrawCenteredResourceIcon(item.IconPath, 48f);
		GUILayout.Label("<b>" + Rich(item.Name) + "</b>", nameStyle, GUILayout.Width(innerWidth), GUILayout.Height(38f));
		string canonTag = string.Empty;
		if (!string.IsNullOrWhiteSpace(canonTag))
		{
			GUILayout.Label(canonTag, detailStyle, GUILayout.Width(innerWidth), GUILayout.Height(28f));
		}
		GUILayout.Label("<color=" + SectResourceViewColor(view) + ">数量 " + item.Amount + "</color>", detailStyle, GUILayout.Width(innerWidth), GUILayout.Height(20f));
		if (!string.IsNullOrWhiteSpace(item.Detail))
		{
			GUILayout.Label("<color=grey>" + Rich(Truncate(item.Detail, 40)) + "</color>", detailStyle, GUILayout.Width(innerWidth), GUILayout.Height(42f));
		}
		else if (item.Grade > 0)
		{
			GUILayout.Label("<color=grey>" + Rich(FormatGongFaGrade(item.Grade)) + "</color>", detailStyle, GUILayout.Width(innerWidth), GUILayout.Height(24f));
		}
		GUILayout.EndVertical();
	}

private int CountFamilyWarehouseItemsCached(long familyId, string view)
	{
		List<FamilyResourceDisplayGroup> groups = GetFamilyWarehouseGroupsCached(familyId, view);
		string key = familyId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + Empty(view, string.Empty).Trim();
		if (_familyWarehouseCountCache.TryGetValue(key, out int cached))
		{
			return cached;
		}
		int total = 0;
		for (int i = 0; i < groups.Count; i++)
		{
			total += Math.Max(1, groups[i].Amount);
		}
		_familyWarehouseCountCache[key] = total;
		return total;
	}

private List<FamilyResourceDisplayGroup> GetFamilyWarehouseGroupsCached(long familyId, string view)
	{
		string cleanView = Empty(view, string.Empty).Trim();
		int year = ResolveCurrentYear();
		if (familyId <= 0L || string.IsNullOrWhiteSpace(cleanView))
		{
			return new List<FamilyResourceDisplayGroup>();
		}

		bool expired = Time.realtimeSinceStartup - _familyWarehouseCacheRealtime > 1.0f;
		if (_familyWarehouseCacheFamilyId != familyId || _familyWarehouseCacheYear != year || expired)
		{
			_familyWarehouseCacheFamilyId = familyId;
			_familyWarehouseCacheYear = year;
			_familyWarehouseCacheRealtime = Time.realtimeSinceStartup;
			_familyWarehouseGroupCache.Clear();
			_familyWarehouseCountCache.Clear();
		}

		if (!_familyWarehouseGroupCache.TryGetValue(cleanView, out List<FamilyResourceDisplayGroup> groups))
		{
			groups = BuildFamilyWarehouseDisplayGroups(familyId, cleanView);
			_familyWarehouseGroupCache[cleanView] = groups;
		}
		return groups;
	}

private static List<FamilyResourceDisplayGroup> BuildFamilyWarehouseDisplayGroups(long familyId, string view)
	{
		List<FamilyResourceDisplayGroup> result = new List<FamilyResourceDisplayGroup>();
		if (familyId <= 0L || string.IsNullOrWhiteSpace(view))
		{
			return result;
		}

		Dictionary<string, FamilyResourceDisplayGroup> map = new Dictionary<string, FamilyResourceDisplayGroup>(StringComparer.Ordinal);
		string familyKey = BuildFamilyWarehouseKey(familyId);
		if (view == "功法" || view == "求金法")
		{
			IReadOnlyList<XjFamilyGongFaWarehouseEntry> gongFaEntries = XjFamilyGongFaWarehouse.ReadFamilyEntries(familyId);
			for (int i = 0; i < gongFaEntries.Count; i++)
			{
				XjFamilyGongFaWarehouseEntry entry = gongFaEntries[i];
				if (!entry.Found || string.IsNullOrWhiteSpace(entry.GongFaName)) continue;
				bool isQiuJinFa = entry.SourceType == XjFamilyGongFaWarehouse.SourceTypeQiuJinFa;
				if ((view == "求金法") != isQiuJinFa) continue;
				string kind = entry.SourceType == XjFamilyGongFaWarehouse.SourceTypeQiuJinFa ? "求金法" : "功法";
				string detail = isQiuJinFa
					? "求金之法"
					: XuanJianVNext.Data.Cultivation.XjFuQiCoreCatalog.IsKnownMethodName(entry.GongFaName)
						? "服气本命：" + Empty(entry.DaoTu, "本命核心")
						: string.IsNullOrWhiteSpace(entry.MappedXianJi) ? string.Empty : "映照仙基：" + entry.MappedXianJi.Trim();
				AddFamilyResourceGroup(map, result, view, entry.GongFaName, kind, entry.DaoTu, detail, entry.Grade, 1, entry.Timestamp, string.Empty);
			}

			if (view == "功法")
			{
				IReadOnlyDictionary<string, int> caiQiFa = XjFamilyCaiQiWarehouse.ReadFamilyResources(familyKey, XjFamilyCaiQiWarehouse.ResourceTypeCaiQiFa);
				foreach (KeyValuePair<string, int> item in caiQiFa)
				{
					if (item.Value <= 0) continue;
					XjFamilyCaiQiWarehouse.ParseCaiQiFaResourceId(item.Key, out string name, out string daoTu);
					AddFamilyResourceGroup(map, result, view, Empty(name, "未名采气法"), "采气法", daoTu, "采气道途：" + Empty(daoTu, "未定"), 0, item.Value, 0, "GameResources/item/gongfa/CaiQiFa.png");
				}
			}
		}
		else if (view == "气")
		{
			IReadOnlyDictionary<string, int> resources = XjFamilyCaiQiWarehouse.ReadFamilyResources(familyKey, XjFamilyCaiQiWarehouse.ResourceTypeCaiQi);
			foreach (KeyValuePair<string, int> item in resources)
			{
				if (item.Value <= 0) continue;
				string name = ResolveCaiQiDisplayName(item.Key);
				AddFamilyResourceGroup(map, result, view, name, "纳气", string.Empty, string.Empty, 0, item.Value, 0, ResolveCaiQiIconPath(name, string.Empty));
			}
		}
		else if (view == "药材")
		{
			XjAlchemyOwnerKey owner = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Family, familyId);
			XjAlchemyInventorySnapshot snapshot = XjAlchemyUiModel.ReadSnapshot(owner, ResolveCurrentYear());
			AppendAlchemyItems(map, result, view, XjAlchemyUiModel.BuildMaterials(snapshot), "药材");
		}
		else if (view == "重宝")
		{
			XjLingWuUiItem[] items = XjLingWuUiModel.BuildFamilyItems(familyId);
			for (int i = 0; i < items.Length; i++)
			{
				XjLingWuUiItem item = items[i];
				AddFamilyResourceGroup(map, result, view, item.DisplayName, "重宝", string.Empty, FirstLine(item.Details), 0, item.Count, ExtractYear(item.Details), item.IconPath);
			}
		}
		else if (view == "丹药")
		{
			XjAlchemyOwnerKey owner = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Family, familyId);
			XjAlchemyInventorySnapshot snapshot = XjAlchemyUiModel.ReadSnapshot(owner, ResolveCurrentYear());
			AppendAlchemyItems(map, result, view, XjAlchemyUiModel.BuildPills(snapshot), "丹药");
			AppendAlchemyItems(map, result, view, XjAlchemyUiModel.BuildRecipes(snapshot), "丹方");
		}
		else if (view == "炼器")
		{
			IReadOnlyList<XjFamilyFaBaoWarehouseEntry> entries = XjFamilyFaBaoWarehouse.ReadFamilyEntries(familyId);
			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (!entry.Found || string.IsNullOrWhiteSpace(entry.FaBaoName)) continue;
				AddFamilyResourceGroup(map, result, view, entry.FaBaoName, Empty(entry.ClassName, "器物"), entry.DaoTu, string.Empty, 0, 1, entry.Year, string.Empty);
			}
		}
		else if (view == "符箓")
		{
			XjCraftOwnerKey owner = new XjCraftOwnerKey(XjCraftOwnerScope.Family, familyId);
			XjCraftInventoryUiItem[] items = XjCraftInventoryUiModel.BuildTalismanInventory(owner);
			for (int i = 0; i < items.Length; i++)
			{
			XjCraftInventoryUiItem item = items[i];
				AddFamilyResourceGroup(map, result, view, item.DisplayName, "符箓", string.Empty, BuildCraftItemShortDetail(item), 0, item.Count, ExtractYear(item.Details), item.IconPath);
			}
		}

		result.Sort((left, right) =>
		{
			int year = right.LatestYear.CompareTo(left.LatestYear);
			if (year != 0) return year;
			int kind = string.Compare(left.Kind, right.Kind, StringComparison.Ordinal);
			if (kind != 0) return kind;
			int grade = right.Grade.CompareTo(left.Grade);
			if (grade != 0) return grade;
			int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
			if (name != 0) return name;
			int daoTu = string.Compare(left.DaoTu, right.DaoTu, StringComparison.Ordinal);
			return daoTu != 0 ? daoTu : string.Compare(left.Detail, right.Detail, StringComparison.Ordinal);
		});
		return result;
	}

private static string BuildFamilyWarehouseKey(long familyId)
	{
		return familyId <= 0L ? string.Empty : "actor:" + familyId.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

private static void AppendAlchemyItems(Dictionary<string, FamilyResourceDisplayGroup> map, List<FamilyResourceDisplayGroup> result, string view, XjAlchemyUiItem[] items, string kind)
	{
		if (items == null) return;
		for (int i = 0; i < items.Length; i++)
		{
			XjAlchemyUiItem item = items[i];
			string resolvedKind = ResolveAlchemyWarehouseKind(item, kind);
			AddFamilyResourceGroup(map, result, view, item.DisplayName, resolvedKind, string.Empty, BuildAlchemyItemShortDetail(item), 0, item.Count, ExtractYear(item.Details), item.IconPath);
		}
	}

private static string ResolveAlchemyWarehouseKind(in XjAlchemyUiItem item, string fallbackKind)
	{
		if (item.Kind == XjAlchemyUiItemKind.Material)
		{
			string detail = item.Details ?? string.Empty;
			if (detail.Contains("上品药材", StringComparison.Ordinal)) return "上品药材";
			if (detail.Contains("下品药材", StringComparison.Ordinal)) return "下品药材";
		}
		if (item.Kind == XjAlchemyUiItemKind.Pill) return "成丹";
		if (item.Kind == XjAlchemyUiItemKind.Recipe) return "丹方";
		return Empty(fallbackKind, "药材");
	}

private static string BuildAlchemyItemShortDetail(in XjAlchemyUiItem item)
	{
		if (item.Kind == XjAlchemyUiItemKind.Recipe
			&& XjAlchemyCatalog.TryGetRecipe(item.Id, out XjAlchemyRecipeDef recipe))
		{
			return "成丹：" + XjAlchemyText.PillShortDescription(recipe.PillId);
		}
		if (item.Kind == XjAlchemyUiItemKind.Pill)
		{
			return FirstLine(item.Details);
		}
		return FirstLine(item.Details);
	}

private static string BuildCraftItemShortDetail(in XjCraftInventoryUiItem item)
	{
		if (!string.IsNullOrWhiteSpace(item.Description)
			&& !item.Description.Contains("材料", StringComparison.Ordinal)
			&& !item.Description.Contains("符纸", StringComparison.Ordinal)
			&& !item.Description.Contains("灵墨", StringComparison.Ordinal)
			&& !item.Description.Contains("符笔", StringComparison.Ordinal))
		{
			return "效果：" + item.Description.Trim();
		}
		return FirstLine(item.Details);
	}

private static void AddFamilyResourceGroup(
		Dictionary<string, FamilyResourceDisplayGroup> map,
		List<FamilyResourceDisplayGroup> result,
		string view,
		string name,
		string kind,
		string daoTu,
		string detail,
		int grade,
		int amount,
		int year,
		string iconPath)
	{
		string cleanName = Empty(name, "未名物").Trim();
		string cleanKind = Empty(kind, view).Trim();
		string cleanDaoTu = Empty(daoTu, string.Empty).Trim();
		string cleanDetail = Empty(detail, string.Empty).Trim();
		int cleanGrade = Math.Max(0, grade);
		string key = view + "|" + cleanKind + "|" + cleanName + "|" + cleanDaoTu + "|" + cleanGrade + "|" + cleanDetail;
		if (!map.TryGetValue(key, out FamilyResourceDisplayGroup group))
		{
			group = new FamilyResourceDisplayGroup
			{
				Name = cleanName,
				Kind = cleanKind,
				DaoTu = cleanDaoTu,
				Detail = cleanDetail,
				Grade = cleanGrade,
				IconPath = string.IsNullOrWhiteSpace(iconPath) ? ResolveFamilyWarehouseIconPath(view, cleanName, cleanKind, cleanDaoTu, cleanGrade) : iconPath.Trim()
			};
			map[key] = group;
			result.Add(group);
		}
		group.Amount += Math.Max(1, amount);
		group.EntryCount++;
		if (year > group.LatestYear)
		{
			group.LatestYear = year;
		}
	}

private static string ResolveFamilyWarehouseIconPath(string view, string name, string kind, string daoTu, int grade)
	{
		XjCodexSectResourceItem item = new XjCodexSectResourceItem
		{
			Name = name ?? string.Empty,
			ResourceKind = kind ?? string.Empty,
			DaoTu = daoTu ?? string.Empty,
			Grade = grade
		};
		return ResolveSectResourceItemIconPath(item, view);
	}

}
