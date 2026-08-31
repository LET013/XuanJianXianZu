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
private void DrawSectResourceViewSelector(XjCodexSnapshot snapshot)
	{
		GUILayout.Label("展示框");
		int columns = ResolveWarehouseColumns(false, 126f, 5);
		for (int i = 0; i < SectResourceViews.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			string view = SectResourceViews[i];
			int count = CountSectResourcesForView(snapshot.SectResources, view);
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_sectResourceView, view, StringComparison.Ordinal)
				? new Color(0.32f, 0.38f, 0.45f)
				: new Color(0.22f, 0.22f, 0.22f);
			if (GUILayout.Button(view + " " + count, GUILayout.Width(120f), GUILayout.Height(32f)))
			{
				_sectResourceView = view;
				_scrollPosition = Vector2.zero;
			}
			GUI.backgroundColor = old;
			if (i % columns == columns - 1 || i == SectResourceViews.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

private void DrawSectResourceDetailPage(XjCodexSnapshot snapshot, XjCodexSectItem sect, string view)
	{
		// 详情页可以绕过宗门总览直接打开，因此必须在绘制列表前按快照版本
		// 同步数量与分组缓存；否则顶部数量来自新快照，列表却仍是旧快照。
		GetSectResourceCountMapCached(snapshot);
		DrawPageHeader("宗门底蕴 · " + sect.Name + " · " + view, "按仓库分类查看此宗所藏底蕴。");
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("返回宗门格局", GUILayout.Width(145f), GUILayout.Height(34f)))
		{
			_selectedSectId = sect.SectId;
			_sectResourceDetailSectId = 0L;
			_sectResourceDetailView = string.Empty;
			_currentTab = 1;
			_scrollPosition = Vector2.zero;
			return;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		int tabColumns = ResolveWarehouseColumns(false, 118f, 5);
		for (int i = 0; i < SectResourceViews.Length; i++)
		{
			if (i % tabColumns == 0) GUILayout.BeginHorizontal();
			string nextView = SectResourceViews[i];
			int count = CountSectResources(snapshot.SectResources, sect.SectId, nextView);
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(view, nextView, StringComparison.Ordinal)
				? new Color(0.32f, 0.38f, 0.45f)
				: new Color(0.22f, 0.22f, 0.22f);
			if (GUILayout.Button(nextView + " " + count, GUILayout.Width(112f), GUILayout.Height(34f)))
			{
				_sectResourceDetailView = nextView;
				_sectResourceView = nextView;
				_scrollPosition = Vector2.zero;
			}
			GUI.backgroundColor = old;
			if (i % tabColumns == tabColumns - 1 || i == SectResourceViews.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		GUILayout.Space(8f);
		SetWarehouseOverviewScratch(0, "宗门", sect.Name, "#FFD37A");
		SetWarehouseOverviewScratch(1, "山门", Empty(sect.CapitalCityName, "未定"), "#9CD7FF");
		SetWarehouseOverviewScratch(2, "当前仓库", view, SectResourceViewColor(view));
		SetWarehouseOverviewScratch(3, "库存数量", CountSectResources(snapshot.SectResources, sect.SectId, view).ToString(), SectResourceViewColor(view));
		SetWarehouseOverviewScratch(4, "宗门层次", TranslateSectStatus(sect.Status, sect.JinDanCount), "#B7A7FF");
		DrawWarehouseOverviewPills(
			false,
			WarehouseOverviewLabelsScratch,
			WarehouseOverviewValuesScratch,
			WarehouseOverviewColorsScratch,
			5);
		GUILayout.Space(10f);
		DrawSectResourceDetailGrid(snapshot.SectResources, sect.SectId, view);
	}

private void DrawSectResourceSectCard(IReadOnlyDictionary<string, int> resourceCounts, XjCodexSectItem sect, int totalCount)
	{
		string color = sect.JinDanCount > 0 ? "#FFD37A" : sect.ZiFuCount > 0 ? "#B7A7FF" : "#9CD7FF";
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=22>" + Rich(sect.Name) + "</size></b>");
		GUILayout.FlexibleSpace();
		GUILayout.Label("<color=grey>点击下方仓库进入详情。</color>");
		GUILayout.EndHorizontal();
		SetWarehouseOverviewScratch(0, "山门", Empty(sect.CapitalCityName, "未定"), "#9CD7FF");
		SetWarehouseOverviewScratch(1, "层次", TranslateSectStatus(sect.Status, sect.JinDanCount), color);
		SetWarehouseOverviewScratch(2, "总藏", totalCount.ToString(), "#FFD37A");
		DrawWarehouseOverviewPills(
			false,
			WarehouseOverviewLabelsScratch,
			WarehouseOverviewValuesScratch,
			WarehouseOverviewColorsScratch,
			3);
		GUILayout.Space(6f);
		int warehouseColumns = ResolveWarehouseColumns(false, 111f, 5);
		for (int i = 0; i < SectResourceViews.Length; i++)
		{
			if (i % warehouseColumns == 0) GUILayout.BeginHorizontal();
			string view = SectResourceViews[i];
			DrawSectResourceWarehouseTile(sect, view, CountSectResources(resourceCounts, sect.SectId, view));
			if (i % warehouseColumns == warehouseColumns - 1 || i == SectResourceViews.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		GUILayout.EndVertical();
		GUILayout.Space(6f);
	}

private void DrawSectResourceWarehouseTile(XjCodexSectItem sect, string view, int count)
	{
		string color = SectResourceViewColor(view);
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(105f), GUILayout.Height(122f));
		DrawCenteredResourceIcon(ResolveSectResourceViewIconPath(view), 42f);
		GUILayout.Label("<b><color=" + color + ">" + Rich(view) + "</color></b>", GUILayout.Width(96f));
		GUILayout.Label("<size=18>" + count + "</size>", GUILayout.Width(96f));
		bool oldEnabled = GUI.enabled;
		GUI.enabled = oldEnabled && count > 0;
		if (GUILayout.Button("查看", GUILayout.Width(88f), GUILayout.Height(28f)))
		{
			_sectResourceView = view;
			OpenSectArchive(sect.SectId, "底蕴传承");
		}
		GUI.enabled = oldEnabled;
		GUILayout.EndVertical();
	}

private void DrawSectResourceDetailGrid(IReadOnlyList<XjCodexSectResourceItem> resources, long sectId, string view)
	{
		List<SectResourceDisplayGroup> groups = GetSectResourceDisplayGroupsCached(resources, sectId, view);
		if (groups.Count == 0)
		{
			DrawEmptyCard("该仓库暂无库存。", "#777777");
			return;
		}

		int shown = Mathf.Min(groups.Count, MaxSectResourceDetailItems);
		int columns = ResolveWarehouseColumns(false, 148f, 5);
		for (int i = 0; i < shown; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			DrawSectResourceItemTile(groups[i], view);
			if (i % columns == columns - 1 || i == shown - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		DrawListLimitNotice(groups.Count, shown);
	}

private void DrawSectResourceItemTile(SectResourceDisplayGroup item, string view)
	{
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(142f), GUILayout.Height(178f));
		DrawCenteredResourceIcon(item.IconPath, 48f);
		GUILayout.Label("<b>" + Rich(Truncate(item.Name, 8)) + "</b>", GUILayout.Width(132f));
		string canonTag = string.Empty;
		if (!string.IsNullOrWhiteSpace(canonTag))
		{
			GUILayout.Label(canonTag, GUILayout.Width(132f));
		}
		GUILayout.Label("<color=" + SectResourceViewColor(view) + ">数量 " + item.Amount + "</color>", GUILayout.Width(132f));
		if (!string.IsNullOrWhiteSpace(item.Detail))
		{
			GUILayout.Label("<color=grey>" + Rich(Truncate(item.Detail, 24)) + "</color>", GUILayout.Width(132f));
		}
		else if (item.Grade > 0)
		{
			GUILayout.Label("<color=grey>" + Rich(FormatGongFaGrade(item.Grade)) + "</color>", GUILayout.Width(132f));
		}
		GUILayout.EndVertical();
	}

private static int CountSectResourcesForView(IReadOnlyList<XjCodexSectResourceItem> resources, string view)
	{
		if (resources == null || resources.Count == 0) return 0;
		int total = 0;
		for (int i = 0; i < resources.Count; i++)
		{
			XjCodexSectResourceItem item = resources[i];
			if (item == null || !MatchesSectResourceView(item, view)) continue;
			total += SectResourceAmountForView(item, view);
		}
		return total;
	}

private static int CountSectResourcesForView(IReadOnlyDictionary<string, int> counts, string view)
	{
		return CountSectResources(counts, 0L, view);
	}

private static Dictionary<string, int> BuildSectResourceCountMap(IReadOnlyList<XjCodexSectResourceItem> resources)
	{
		Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
		if (resources == null || resources.Count == 0)
		{
			return result;
		}

		for (int i = 0; i < resources.Count; i++)
		{
			XjCodexSectResourceItem item = resources[i];
			if (item == null || item.SectId <= 0L)
			{
				continue;
			}
			for (int viewIndex = 0; viewIndex < SectResourceViews.Length; viewIndex++)
			{
				string view = SectResourceViews[viewIndex];
				if (!MatchesSectResourceView(item, view))
				{
					continue;
				}
				AddSectResourceCount(result, item.SectId, view, SectResourceAmountForView(item, view));
				AddSectResourceCount(result, 0L, view, SectResourceAmountForView(item, view));
			}
		}
		return result;
	}

private IReadOnlyDictionary<string, int> GetSectResourceCountMapCached(XjCodexSnapshot snapshot)
	{
		int version = snapshot?.Version ?? 0;
		if (_sectResourceCacheVersion != version)
		{
			_sectResourceCacheVersion = version;
			_sectResourceCountCache.Clear();
			_sectResourceGroupCache.Clear();
			Dictionary<string, int> counts = BuildSectResourceCountMap(snapshot?.SectResources ?? Array.Empty<XjCodexSectResourceItem>());
			foreach (KeyValuePair<string, int> pair in counts)
			{
				_sectResourceCountCache[pair.Key] = pair.Value;
			}
		}
		return _sectResourceCountCache;
	}

private static void AddSectResourceCount(IDictionary<string, int> counts, long sectId, string view, int amount)
	{
		string key = SectResourceCountKey(sectId, view);
		counts.TryGetValue(key, out int current);
		counts[key] = current + Math.Max(0, amount);
	}

private static int CountSectResources(IReadOnlyDictionary<string, int> counts, long sectId, string view)
	{
		if (counts == null || string.IsNullOrWhiteSpace(view))
		{
			return 0;
		}
		return counts.TryGetValue(SectResourceCountKey(sectId, view), out int count) ? count : 0;
	}

private static int CountSectResources(IReadOnlyList<XjCodexSectResourceItem> resources, long sectId, string view)
	{
		if (resources == null || resources.Count == 0 || sectId <= 0L) return 0;
		int total = 0;
		for (int i = 0; i < resources.Count; i++)
		{
			XjCodexSectResourceItem item = resources[i];
			if (item == null || item.SectId != sectId || !MatchesSectResourceView(item, view)) continue;
			total += SectResourceAmountForView(item, view);
		}
		return total;
	}

private static string SectResourceCountKey(long sectId, string view)
	{
		return sectId.ToString() + "|" + (view ?? string.Empty);
	}

private static int CountSectResourcesByKind(IReadOnlyList<XjCodexSectResourceItem> resources, long sectId, string kind)
	{
		if (resources == null || resources.Count == 0 || sectId <= 0L || string.IsNullOrWhiteSpace(kind)) return 0;
		int total = 0;
		for (int i = 0; i < resources.Count; i++)
		{
			XjCodexSectResourceItem item = resources[i];
			if (item == null || item.SectId != sectId || !string.Equals(item.ResourceKind, kind, StringComparison.Ordinal)) continue;
			total += Math.Max(1, item.Amount);
		}
		return total;
	}

private static int SectResourceAmountForView(XjCodexSectResourceItem item, string view)
	{
		if (item == null) return 0;
		if (string.Equals(view, "气", StringComparison.Ordinal)
			|| string.Equals(view, "丹药", StringComparison.Ordinal)
			|| string.Equals(view, "符箓", StringComparison.Ordinal)
			|| string.Equals(view, "阵法", StringComparison.Ordinal))
		{
			return Math.Max(1, item.Amount);
		}
		return 1;
	}

private static bool MatchesSectResourceView(XjCodexSectResourceItem item, string view)
	{
		if (item == null) return false;
		string kind = item.ResourceKind ?? string.Empty;
		if (view == "功法")
		{
			return kind == "功法" || kind == "采气法";
		}
		if (view == "求金法")
		{
			return kind == "求金法";
		}
		if (view == "气")
		{
			return kind == "纳气" || kind == "气" || HasToken(kind, "先天之气");
		}
		if (view == "重宝")
		{
			return kind == "重宝" || kind == "法器" || kind == "灵宝" || kind == "法宝" || kind == "灵物";
		}
		if (view == "丹药") return kind == "丹药" || kind == "丹方" || kind == "炼丹";
		if (view == "炼器") return kind == "炼器";
		if (view == "符箓") return kind == "符箓" || kind == "制符";
		if (view == "阵法") return kind == "阵法" || kind == "阵材" || kind == "阵法素材";
		return kind == view;
	}

private static string FormatSectResourceLine(XjCodexSectResourceItem item)
	{
		if (item == null) return string.Empty;
		string name = Empty(item.Name, "未名底蕴");
		string kind = Empty(item.ResourceKind, "未载");
		string text = "· <b>" + Rich(name) + "</b>  <color=grey>" + Rich(kind) + "</color>";
		if (!string.IsNullOrWhiteSpace(item.DaoTu)) text += "  道途：" + Rich(XjDisplayNameSanitizer.GameTerm(item.DaoTu, "未知道途"));
		if (item.Grade > 0) text += "  品阶：" + Rich(FormatGongFaGrade(item.Grade));
		if (item.Amount > 1) text += "  数量：" + item.Amount;
		if (item.Year > 0) text += "  入库：" + XjChronology.FormatYear(item.Year);
		string source = !string.IsNullOrWhiteSpace(item.SourceActorName) ? item.SourceActorName : TranslateCraftTaskKind(item.Source);
		if (!string.IsNullOrWhiteSpace(source)) text += "  来源：" + Rich(source.Trim());
		if (!string.IsNullOrWhiteSpace(item.Detail)) text += "  细节：" + Rich(item.Detail.Trim());
		return text;
	}

private static XjCodexSectItem FindSectById(IReadOnlyList<XjCodexSectItem> sects, long sectId)
	{
		if (sectId <= 0L) return null;
		if (XjWorldEntityViewModelStore.TryGetSectItem(sectId, out XjCodexSectItem indexed)) return indexed;
		if (sects == null) return null;
		for (int i = 0; i < sects.Count; i++)
		{
			XjCodexSectItem item = sects[i];
			if (item != null && item.SectId == sectId)
			{
				return item;
			}
		}
		return null;
	}

private static List<SectResourceDisplayGroup> BuildSectResourceDisplayGroups(IReadOnlyList<XjCodexSectResourceItem> resources, long sectId, string view)
	{
		List<SectResourceDisplayGroup> result = new List<SectResourceDisplayGroup>();
		if (resources == null || resources.Count == 0 || sectId <= 0L)
		{
			return result;
		}

		Dictionary<string, SectResourceDisplayGroup> map = new Dictionary<string, SectResourceDisplayGroup>(StringComparer.Ordinal);
		for (int i = 0; i < resources.Count; i++)
		{
			XjCodexSectResourceItem item = resources[i];
			if (item == null || item.SectId != sectId || !MatchesSectResourceView(item, view))
			{
				continue;
			}

			string kind = Empty(item.ResourceKind, view).Trim();
			string name = Empty(item.Name, "未名底蕴").Trim();
			string daoTu = Empty(item.DaoTu, string.Empty).Trim();
			string detail = Empty(item.Detail, string.Empty).Trim();
			int grade = item.Grade;
			string key = view + "|" + kind + "|" + name + "|" + daoTu + "|" + grade + "|" + detail;
			if (!map.TryGetValue(key, out SectResourceDisplayGroup group))
			{
				group = new SectResourceDisplayGroup
				{
					Name = name,
					Kind = kind,
					DaoTu = daoTu,
					Detail = detail,
					Grade = grade,
					IconPath = ResolveSectResourceItemIconPath(item, view)
				};
				map[key] = group;
				result.Add(group);
			}

			group.Amount += SectResourceAmountForView(item, view);
			group.EntryCount++;
			if (item.Year > group.LatestYear)
			{
				group.LatestYear = item.Year;
			}
		}
		return result;
	}

private List<SectResourceDisplayGroup> GetSectResourceDisplayGroupsCached(IReadOnlyList<XjCodexSectResourceItem> resources, long sectId, string view)
	{
		string cleanView = Empty(view, string.Empty).Trim();
		if (sectId <= 0L || string.IsNullOrWhiteSpace(cleanView))
		{
			return new List<SectResourceDisplayGroup>();
		}
		string key = _sectResourceCacheVersion.ToString() + "|" + sectId.ToString() + "|" + cleanView;
		if (!_sectResourceGroupCache.TryGetValue(key, out List<SectResourceDisplayGroup> groups))
		{
			groups = BuildSectResourceDisplayGroups(resources, sectId, cleanView);
			_sectResourceGroupCache[key] = groups;
		}
		return groups;
	}

}
