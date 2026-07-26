using System;
using System.Collections.Generic;
using System.Reflection;
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
using XuanJianVNext.Systems.FaBao;
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
private void DrawSectResourcePage(XjCodexSnapshot snapshot)
	{
		if (_sectResourceDetailSectId > 0L && !string.IsNullOrWhiteSpace(_sectResourceDetailView))
		{
			XjCodexSectItem detailSect = FindSectById(snapshot.Sects, _sectResourceDetailSectId);
			if (detailSect != null)
			{
				DrawSectResourceDetailPage(snapshot, detailSect, _sectResourceDetailView);
				return;
			}
			_sectResourceDetailSectId = 0L;
			_sectResourceDetailView = string.Empty;
		}

		DrawPageHeader("宗门底蕴", "总览各宗门所藏功法、求金法、丹药、符箓、阵法与传承器物。");
		IReadOnlyDictionary<string, int> resourceCounts = GetSectResourceCountMapCached(snapshot);
		GUILayout.Space(8f);
		GUILayout.BeginHorizontal();
		DrawOverviewPill("底蕴条目", snapshot.SectResourceCount.ToString(), "#FFD37A", GUILayout.Width(145f));
		DrawOverviewPill("宗门", snapshot.Sects.Count.ToString(), "#9CD7FF", GUILayout.Width(125f));
		DrawOverviewPill("功法", CountSectResourcesForView(resourceCounts, "功法").ToString(), "#B7A7FF", GUILayout.Width(120f));
		DrawOverviewPill("求金法", CountSectResourcesForView(resourceCounts, "求金法").ToString(), "#FFD37A", GUILayout.Width(120f));
		DrawOverviewPill("重宝", CountSectResourcesForView(resourceCounts, "重宝").ToString(), "#FFD37A", GUILayout.Width(120f));
		DrawOverviewPill("气", CountSectResourcesForView(resourceCounts, "气").ToString(), "#A7E08A", GUILayout.Width(105f));
		DrawOverviewPill("丹药", CountSectResourcesForView(resourceCounts, "丹药").ToString(), "#A7E08A", GUILayout.Width(120f));
		DrawOverviewPill("炼器", CountSectResourcesForView(resourceCounts, "炼器").ToString(), "#FFD37A", GUILayout.Width(120f));
		DrawOverviewPill("符箓", CountSectResourcesForView(resourceCounts, "符箓").ToString(), "#9CD7FF", GUILayout.Width(120f));
		DrawOverviewPill("阵法", CountSectResourcesForView(resourceCounts, "阵法").ToString(), "#B7A7FF", GUILayout.Width(120f));
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
		if (snapshot.Sects.Count == 0)
		{
			DrawEmptyCard("尚无宗门。", "#777777");
			return;
		}

		int matched = 0;
		int shown = 0;
		for (int i = 0; i < snapshot.Sects.Count; i++)
		{
			XjCodexSectItem sect = snapshot.Sects[i];
			if (sect == null || sect.SectId <= 0L)
			{
				continue;
			}
			int totalCount = CountSectResources(resourceCounts, sect.SectId, "功法")
				+ CountSectResources(resourceCounts, sect.SectId, "求金法")
				+ CountSectResources(resourceCounts, sect.SectId, "重宝")
				+ CountSectResources(resourceCounts, sect.SectId, "气")
				+ CountSectResources(resourceCounts, sect.SectId, "丹药")
				+ CountSectResources(resourceCounts, sect.SectId, "炼器")
				+ CountSectResources(resourceCounts, sect.SectId, "符箓")
				+ CountSectResources(resourceCounts, sect.SectId, "阵法");
			if (totalCount <= 0)
			{
				continue;
			}

			matched++;
			if (shown >= MaxRenderedCardsPerPage)
			{
				continue;
			}

			DrawSectResourceSectCard(resourceCounts, sect, totalCount);
			shown++;
		}
		if (matched == 0)
		{
			DrawEmptyCard("当前暂无宗门底蕴可阅。", "#777777");
		}
		DrawListLimitNotice(matched, shown);
	}

private static string ResolveCaiQiDisplayName(string resourceId)
	{
		if (!string.IsNullOrWhiteSpace(resourceId)
			&& XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId.Trim(), out string displayName)
			&& !string.IsNullOrWhiteSpace(displayName))
		{
			return displayName.Trim();
		}
		return "未名先天之气";
	}

private static int ResolveCurrentYear()
	{
		try
		{
			return World.world == null ? 0 : Math.Max(0, (int)World.world.getCurWorldTime());
		}
		catch
		{
			return 0;
		}
	}

private static string FirstLine(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return string.Empty;
		string normalized = value.Replace("\r", "\n");
		int index = normalized.IndexOf('\n');
		return (index >= 0 ? normalized.Substring(0, index) : normalized).Trim();
	}

private static int ExtractYear(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return 0;
		string text = value;
		int marker = text.IndexOf("玄鉴历", StringComparison.Ordinal);
		if (marker < 0) marker = text.IndexOf("入库：", StringComparison.Ordinal);
		if (marker < 0) return 0;
		int start = marker;
		while (start < text.Length && !char.IsDigit(text[start])) start++;
		int end = start;
		while (end < text.Length && char.IsDigit(text[end])) end++;
		return start < end && int.TryParse(text.Substring(start, end - start), out int year) ? year : 0;
	}

	private static string ResolveSectResourceViewIconPath(string view)
	{
		if (view == "功法") return "GameResources/item/gongfa/DiPinGongFa.png";
		if (view == "求金法") return "GameResources/item/gongfa/QiuJinFa.png";
		if (view == "重宝") return "GameResources/ui/Icons/items/FaBaoLu.png";
		if (view == "气") return "GameResources/item/caiqi/iconResZaQi.png";
		if (view == "药材") return "GameResources/item/Arts/yaocai/LowYC/LowYc-1.png";
		if (view == "丹药") return "GameResources/item/Arts/danyao/DanYao-1.png";
		if (view == "炼器") return "GameResources/item/Arts/lingwu/LingWu-Qi.png";
		if (view == "符箓") return "GameResources/item/Arts/fulu/FuLu-HuShen.png";
		if (view == "阵法") return "GameResources/item/Arts/zhenfa/ZhenPan.png";
		return "GameResources/ui/Icons/items/XuanJianCodex.png";
	}

private static string ResolveSectResourceItemIconPath(XjCodexSectResourceItem item, string view)
	{
		if (item == null) return ResolveSectResourceViewIconPath(view);
		string kind = item.ResourceKind ?? string.Empty;
		string name = item.Name ?? string.Empty;
		string daoTu = item.DaoTu ?? string.Empty;
		if (view == "功法" || view == "求金法")
		{
			if (kind == "求金法") return "GameResources/item/gongfa/QiuJinFa.png";
			if (kind == "采气法") return "GameResources/item/gongfa/CaiQiFa.png";
			if (item.Grade >= 6) return "GameResources/item/gongfa/LiuPinGongFa.png";
			if (item.Grade == 5) return "GameResources/item/gongfa/WuPinGongFa.png";
			if (item.Grade == 4) return "GameResources/item/gongfa/SiPinGongFa.png";
			return "GameResources/item/gongfa/DiPinGongFa.png";
		}
		if (view == "气")
		{
			return ResolveCaiQiIconPath(name, daoTu);
		}
		if (view == "丹药")
		{
			int index = Mathf.Clamp(item.Grade > 0 ? item.Grade : 1, 1, 14);
			return "GameResources/item/Arts/danyao/DanYao-" + index + ".png";
		}
		if (view == "药材")
		{
			return ResolveSectResourceViewIconPath(view);
		}
		if (view == "符箓")
		{
			if (HasToken(name, "神行")) return "GameResources/item/Arts/fulu/FuLu-ShenXing.png";
			if (HasToken(name, "破阵")) return "GameResources/item/Arts/fulu/FuLu-PoZhen.png";
			if (HasToken(name, "破障")) return "GameResources/item/Arts/fulu/FuLu-PoZhang.png";
			if (HasToken(name, "镇神")) return "GameResources/item/Arts/fulu/FuLu-ZhenShen.png";
			return "GameResources/item/Arts/fulu/FuLu-HuShen.png";
		}
		if (view == "重宝")
		{
			return ResolveLingWuIconPath(name, daoTu);
		}
		if (view == "炼器")
		{
			if (TryResolveFaBaoIconPath(name, kind, item.Year, out string iconPath))
			{
				return iconPath;
			}
			return "GameResources/ui/Icons/items/FaBaoLu.png";
		}
		if (view == "阵法")
		{
			if (HasToken(name, "阵旗")) return "GameResources/item/Arts/zhenfa/ZhenQi.png";
			if (HasToken(name, "阵盘")) return "GameResources/item/Arts/zhenfa/ZhenPan.png";
			if (HasToken(name, "阵纹")) return "GameResources/item/Arts/zhenfa/ZhenTu.png";
			return "GameResources/item/Arts/zhenfa/ZongMenDaZhen.png";
		}
		return ResolveSectResourceViewIconPath(view);
	}

private static bool TryResolveFaBaoIconPath(string name, string kind, int year, out string iconPath)
	{
		iconPath = string.Empty;
		string resolvedKind = XjFaBaoAcquisition.ResolveKindFromName(name);
		if (string.IsNullOrWhiteSpace(resolvedKind))
		{
			resolvedKind = XjFaBaoAcquisition.ResolveKindFromName(kind);
		}
		if (string.IsNullOrWhiteSpace(resolvedKind))
		{
			return false;
		}

		long seed = year > 0 ? year : 1L;
		return XjFaBaoEquipmentAssets.TryPickIconPath(resolvedKind, seed, name ?? string.Empty, out iconPath)
			&& !string.IsNullOrWhiteSpace(iconPath);
	}

private static string ResolveCaiQiIconPath(string name, string daoTu)
	{
		string text = (name ?? string.Empty) + " " + (daoTu ?? string.Empty);
		if (HasToken(text, "杂")) return "GameResources/item/caiqi/iconResZaQi.png";
		if (HasToken(text, "真炁") || HasToken(text, "真气")) return "GameResources/item/caiqi/iconResZhenQiZhiQi.png";
		if (HasToken(text, "青宣")) return "GameResources/item/caiqi/iconResQingXuanZhiQi.png";
		if (HasToken(text, "太阳")) return "GameResources/item/caiqi/iconResTaiYangZhiQi.png";
		if (HasToken(text, "太阴")) return "GameResources/item/caiqi/iconResTaiYinZhiQi.png";
		if (HasToken(text, "少阳")) return "GameResources/item/caiqi/iconResShaoYangZhiQi.png";
		if (HasToken(text, "少阴")) return "GameResources/item/caiqi/iconResShaoYinZhiQi.png";
		if (HasToken(text, "金")) return "GameResources/item/caiqi/iconResGengJinZhiQi.png";
		if (HasToken(text, "木")) return "GameResources/item/caiqi/iconResJiMuZhiQi.png";
		if (HasToken(text, "水")) return "GameResources/item/caiqi/iconResKanShuiZhiQi.png";
		if (HasToken(text, "火")) return "GameResources/item/caiqi/iconResLiHuoZhiQi.png";
		if (HasToken(text, "土")) return "GameResources/item/caiqi/iconResWuTuZhiQi.png";
		if (HasToken(text, "雷")) return "GameResources/item/caiqi/iconResXuanLeiZhiQi.png";
		return "GameResources/item/caiqi/iconResZhenQiZhiQi.png";
	}

private static string ResolveLingWuIconPath(string name, string daoTu)
	{
		string text = (name ?? string.Empty) + " " + (daoTu ?? string.Empty);
		if (HasToken(text, "金")) return "GameResources/item/Arts/lingwu/LingWu-Jin.png";
		if (HasToken(text, "木")) return "GameResources/item/Arts/lingwu/LingWu-Mu.png";
		if (HasToken(text, "水")) return "GameResources/item/Arts/lingwu/LingWu-Shui.png";
		if (HasToken(text, "火")) return "GameResources/item/Arts/lingwu/LingWu-Huo.png";
		if (HasToken(text, "土")) return "GameResources/item/Arts/lingwu/LingWu-Tu.png";
		if (HasToken(text, "雷")) return "GameResources/item/Arts/lingwu/LingWu-Lei.png";
		if (HasToken(text, "阳")) return "GameResources/item/Arts/lingwu/LingWu-Yang.png";
		if (HasToken(text, "阴")) return "GameResources/item/Arts/lingwu/LingWu-Yin.png";
		return "GameResources/item/Arts/lingwu/LingWu-Qi.png";
	}

private sealed class SectResourceDisplayGroup
	{
		internal string Name = string.Empty;
		internal string Kind = string.Empty;
		internal string DaoTu = string.Empty;
		internal string Detail = string.Empty;
		internal int Grade;
		internal int Amount;
		internal int EntryCount;
		internal int LatestYear;
		internal string IconPath = string.Empty;
	}

private sealed class FamilyResourceDisplayGroup
	{
		internal string Name = string.Empty;
		internal string Kind = string.Empty;
		internal string DaoTu = string.Empty;
		internal string Detail = string.Empty;
		internal int Grade;
		internal int Amount;
		internal int EntryCount;
		internal int LatestYear;
		internal string IconPath = string.Empty;
	}
}



