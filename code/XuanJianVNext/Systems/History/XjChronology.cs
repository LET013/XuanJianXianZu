using System;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.History;

/// <summary>
/// 玄鉴纪年唯一换算入口。内部状态、冷却和持续时间始终保存 WorldBox 绝对世界年；
/// 只有“玄鉴历第N年”显示、从起录开始的固定里程碑与百年分卷使用相对纪年。
/// </summary>
internal static class XjChronology
{
    internal static int BaseWorldYear
    {
        get
        {
            int persisted = Math.Max(0, XjCenturyAnnalsStore.BaseWorldYear);
            if (persisted > 0) return persisted;
            return Math.Max(0, XjRuntimeCadence.LoadBaselineYear);
        }
    }

    internal static int ToXuanJianYear(int worldYear)
    {
        if (worldYear <= 0) return 0;
        int baseYear = BaseWorldYear;
        if (baseYear <= 0) return worldYear;
        if (worldYear < baseYear) return 0;
        return worldYear - baseYear + 1;
    }

    internal static int ToWorldYear(int xuanJianYear)
    {
        return ToWorldYear(xuanJianYear, BaseWorldYear);
    }

    internal static int ToWorldYear(int xuanJianYear, int baseWorldYear)
    {
        if (xuanJianYear <= 0 || baseWorldYear <= 0) return 0;
        return checked(baseWorldYear + xuanJianYear - 1);
    }

    internal static int ResolvePeriodIndex(int worldYear, int periodYears)
    {
        int xuanJianYear = ToXuanJianYear(worldYear);
        int span = Math.Max(1, periodYears);
        return xuanJianYear <= 0 ? 0 : (xuanJianYear - 1) / span;
    }

    internal static bool IsMilestoneYear(int worldYear, int xuanJianYear)
    {
        return worldYear > 0 && xuanJianYear > 0 && ToXuanJianYear(worldYear) == xuanJianYear;
    }

    internal static string FormatYear(int worldYear)
    {
        int displayYear = ToXuanJianYear(worldYear);
        return displayYear > 0 ? "玄鉴历" + displayYear + "年" : "年代未详";
    }
}
