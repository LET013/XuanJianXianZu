using System;
using XuanJianVNext.Data.Cultivation;

namespace XuanJianVNext.UI.Rank;

/// <summary>
/// 排行榜道途文字色盘。颜色只承载“道途辨识”，不参与战力/位序语义；
/// 分支优先继承根道色，释修与仙国法使用独立色，保证暗色排行榜上仍可读。
/// </summary>
internal static class XjRankDaoTuPalette
{
    internal const string Fallback = "#C7C9C2";
    internal const string AncientShi = "#D9C78A";
    internal const string ModernShi = "#D2B5FF";
    internal const string XianGuo = "#F2C96D";

    internal static string ResolveHex(string daoTu)
    {
        string value = Normalize(daoTu);
        if (value.Length == 0) return Fallback;
        if (value.Contains("古释", StringComparison.Ordinal)) return AncientShi;
        if (value.Contains("今释", StringComparison.Ordinal)) return ModernShi;
        if (value.Contains("仙国", StringComparison.Ordinal)
            || value.Contains("帝明阳", StringComparison.Ordinal)
            || value.Contains("持玄", StringComparison.Ordinal)) return XianGuo;
        if (value.Contains("杂气", StringComparison.Ordinal)) return "#A9A39A";

        if (XjDaoTuCatalog.TryResolve(value, out XjDaoTuDefinition definition))
        {
            return ResolveRootHex(definition.RootId, definition.ElementAffinityRootId);
        }
        return Fallback;
    }

    private static string ResolveRootHex(string rootId, string affinityRootId)
    {
        string root = Normalize(rootId);
        return root switch
        {
            XjDaoTuRootIds.SanYin => "#9BCBFF",
            XjDaoTuRootIds.SanYang => "#FFD37A",
            XjDaoTuRootIds.SanLei => "#C6A7FF",
            XjDaoTuRootIds.JinDe => "#DCE5EC",
            XjDaoTuRootIds.MuDe => "#8ED19A",
            XjDaoTuRootIds.ShuiDe => "#74CFE8",
            XjDaoTuRootIds.HuoDe => "#FF9A6B",
            XjDaoTuRootIds.TuDe => "#D9B36C",
            XjDaoTuRootIds.ShiErQi => "#E6A6D8",
            XjDaoTuRootIds.XiaoKui => "#B6A4DF",
            XjDaoTuRootIds.ShangWu => "#D59A82",
            XjDaoTuRootIds.YuZhen => "#A8DFC4",
            XjDaoTuRootIds.HengZhu => "#E4B878",
            XjDaoTuRootIds.QingXuan => "#79CFC2",
            XjDaoTuRootIds.QuanDan => "#F0D38A",
            XjDaoTuRootIds.ZhiBo => "#D3A0FF",
            XjDaoTuRootIds.SiTian => "#88DDE0",
            XjDaoTuRootIds.DuWei => "#FFB08A",
            XjDaoTuRootIds.LongGeng => "#EDF3F8",
            XjDaoTuRootIds.YuanZhao => "#78C7D8",
            _ => ResolveAffinityHex(affinityRootId)
        };
    }

    private static string ResolveAffinityHex(string rootId)
    {
        return Normalize(rootId) switch
        {
            XjDaoTuRootIds.JinDe => "#DCE5EC",
            XjDaoTuRootIds.MuDe => "#8ED19A",
            XjDaoTuRootIds.ShuiDe => "#74CFE8",
            XjDaoTuRootIds.HuoDe => "#FF9A6B",
            XjDaoTuRootIds.TuDe => "#D9B36C",
            _ => Fallback
        };
    }

    private static string Normalize(string value) => (value ?? string.Empty).Trim();
}
