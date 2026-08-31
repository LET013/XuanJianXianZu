using System;
using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.UI.Overview;

namespace XuanJianVNext.Patches;

/// <summary>
/// City/Kingdom 的只读修炼者人口概览。
///
/// 这是 R8 Native Authority Isolation 的窄例外：只在 WorldBox 已完成 showStatsRows 后，
/// 通过 StatsRowsContainer 绑定一条玄鉴自有统计行。禁止触碰 Tab、DragOrder、布局、Tween、
/// startShowingWindow，也不建立延迟/周期刷新。统计源只遍历 XjCultivatorCache。
/// </summary>
internal partial class XjVNextPatches
{
    private const string CityCultivatorPopulationRowId = "xuanjian_city_cultivators";
    private const string KingdomCultivatorPopulationRowId = "xuanjian_kingdom_cultivators";

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CityWindow), nameof(CityWindow.showStatsRows))]
    public static void XuanJianVNext_CityWindow_ShowStatsRows_CultivatorPopulation_Postfix(CityWindow __instance)
    {
        City city = SelectedMetas.selected_city;
        if (__instance == null || city?.data == null || __instance.stats_rows_container == null)
        {
            return;
        }

        try
        {
            XjCultivatorPopulationSummary summary = XjCultivatorPopulationOverview.BuildForCity(city);
            BindCultivatorPopulationRow(
                __instance.stats_rows_container,
                CityCultivatorPopulationRowId,
                "城中修炼者",
                ResolveCityName(city),
                summary);
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("CityWindow.CultivatorPopulationOverview", ex);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows))]
    public static void XuanJianVNext_KingdomWindow_ShowStatsRows_CultivatorPopulation_Postfix(KingdomWindow __instance)
    {
        Kingdom kingdom = SelectedMetas.selected_kingdom;
        if (__instance == null || kingdom?.data == null || __instance.stats_rows_container == null)
        {
            return;
        }

        try
        {
            XjCultivatorPopulationSummary summary = XjCultivatorPopulationOverview.BuildForKingdom(kingdom);
            BindCultivatorPopulationRow(
                __instance.stats_rows_container,
                KingdomCultivatorPopulationRowId,
                "国内修炼者",
                ResolveKingdomName(kingdom),
                summary);
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("KingdomWindow.CultivatorPopulationOverview", ex);
        }
    }

    private static void BindCultivatorPopulationRow(
        StatsRowsContainer container,
        string rowId,
        string tooltipTitle,
        string scopeName,
        XjCultivatorPopulationSummary summary)
    {
        if (container == null || string.IsNullOrWhiteSpace(rowId))
        {
            return;
        }

        KeyValueField row = container.getStatRow(rowId);
        if (row == null)
        {
            return;
        }

        if (row.name_text != null)
        {
            row.name_text.text = "修炼者";
        }
        if (row.value != null)
        {
            row.value.text = summary.DisplayValue;
        }
        if (row.icon != null)
        {
            row.icon.gameObject.SetActive(false);
        }

        // Tooltip 生命周期可能长于本次 showStatsRows。这里只捕获已经物化的字符串，
        // 不把 City / Kingdom / Actor 原生引用塞进 UI 委托。
        string title = string.IsNullOrWhiteSpace(tooltipTitle) ? "修炼者" : tooltipTitle.Trim();
        string description = summary.BuildTooltip(scopeName);
        TooltipDataGetter tooltipGetter = delegate
        {
            return new TooltipData
            {
                _tip_name = title,
                _tip_description = description
            };
        };
        row.setMetaForTooltip(0, -1L, rowId + "_info", tooltipGetter);
        row.gameObject.SetActive(true);
    }

    private static string ResolveCityName(City city)
    {
        string name = city?.data?.name;
        return string.IsNullOrWhiteSpace(name) ? "当前城镇" : name.Trim();
    }

    private static string ResolveKingdomName(Kingdom kingdom)
    {
        string name = kingdom?.data?.name;
        return string.IsNullOrWhiteSpace(name) ? "当前国家" : name.Trim();
    }
}
