using System;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Data.Rules;

/// <summary>
/// 境界相关工具方法的统一入口。境界顺序只用于展示、纪事和跨体系战斗层级比较；
/// 功法、采气、仙基等玩法必须另外读取 CultivationPath。
/// </summary>
internal static class XjRealmHelper
{
    internal static bool IsRealm(string realmId, string realm)
    {
        string value = (realmId ?? string.Empty).Trim();
        if (value.Length == 0) return false;
        return realm switch
        {
            "TaiXi" => string.Equals(value, "TaiXi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.TaiXi, StringComparison.Ordinal)
                || value.IndexOf("胎息", StringComparison.Ordinal) >= 0,
            "LianQi" => string.Equals(value, "LianQi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.LianQi, StringComparison.Ordinal)
                || value.IndexOf("炼气", StringComparison.Ordinal) >= 0
                || value.IndexOf("练气", StringComparison.Ordinal) >= 0,
            "ZhuJi" => string.Equals(value, "ZhuJi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.ZhuJi, StringComparison.Ordinal)
                || value.IndexOf("筑基", StringComparison.Ordinal) >= 0,
            "ZiFu" => string.Equals(value, "ZiFu", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.ZiFu, StringComparison.Ordinal)
                || value.IndexOf("紫府", StringComparison.Ordinal) >= 0,
            "JinDan" => string.Equals(value, "JinDan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.JinDan, StringComparison.Ordinal)
                || value.IndexOf("金丹", StringComparison.Ordinal) >= 0,
            "DaoTai" => string.Equals(value, "DaoTai", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "ZiFuJinDanDaoTai", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.DaoTai, StringComparison.Ordinal),
            "ShenDan" => string.Equals(value, "ShenDan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.ShenDan, StringComparison.Ordinal)
                || value.IndexOf("神丹", StringComparison.Ordinal) >= 0,
            "HuangGuan" => string.Equals(value, "HuangGuan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.HuangGuan, StringComparison.Ordinal)
                || value.IndexOf("黄冠", StringComparison.Ordinal) >= 0,
            "FuQiZhenRen" => string.Equals(value, "FuQiZhenRen", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
                || string.Equals(value, "服气真人", StringComparison.Ordinal)
                || string.Equals(value, "真人", StringComparison.Ordinal),
            "ZhenJunYuShi" => string.Equals(value, "ZhenJunYuShi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
                || value.IndexOf("真君羽士", StringComparison.Ordinal) >= 0,
            "FuQiDaoTai" => string.Equals(value, "FuQiDaoTai", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal),
            _ => false
        };
    }

    /// <summary>
    /// 统一比较层级：真君羽士/金丹=5，真人/紫府=4，黄冠/筑基=3。
    /// 这不表示两套境界相同，只提供跨体系排序。
    /// </summary>
    internal static int GetOrder(string realmId)
    {
        string value = (realmId ?? string.Empty).Trim();
        if (value.Length == 0) return 0;
        if (IsRealm(value, "DaoTai") || IsRealm(value, "FuQiDaoTai")) return 6;
        if (IsRealm(value, "JinDan") || IsRealm(value, "ShenDan") || IsRealm(value, "ZhenJunYuShi")) return 5;
        if (IsRealm(value, "ZiFu") || IsRealm(value, "FuQiZhenRen")) return 4;
        if (IsRealm(value, "ZhuJi") || IsRealm(value, "HuangGuan")) return 3;
        if (IsRealm(value, "LianQi")) return 2;
        if (IsRealm(value, "TaiXi")) return 1;
        return 0;
    }

    internal static bool ShouldRecord(string realmId) => GetOrder(realmId) >= 3;

    internal static bool IsKnownTag(string realmId) => NormalizeId(realmId).Length > 0;

    internal static string NormalizeId(string realmId)
    {
        string value = (realmId ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        if (IsRealm(value, "TaiXi")) return XjRealmIds.TaiXi;
        if (IsRealm(value, "LianQi")) return XjRealmIds.LianQi;
        if (IsRealm(value, "ZhuJi")) return XjRealmIds.ZhuJi;
        if (IsRealm(value, "ZiFu")) return XjRealmIds.ZiFu;
        if (IsRealm(value, "JinDan")) return XjRealmIds.JinDan;
        if (IsRealm(value, "DaoTai")) return XjRealmIds.DaoTai;
        if (IsRealm(value, "ShenDan")) return XjRealmIds.ShenDan;
        if (IsRealm(value, "HuangGuan")) return XjRealmIds.HuangGuan;
        if (IsRealm(value, "FuQiZhenRen")) return XjRealmIds.FuQiZhenRen;
        if (IsRealm(value, "ZhenJunYuShi")) return XjRealmIds.ZhenJunYuShi;
        if (IsRealm(value, "FuQiDaoTai")) return XjRealmIds.FuQiDaoTai;
        return string.Empty;
    }

    internal static string GetDisplayName(string realmId)
    {
        string normalized = NormalizeId(realmId);
        if (string.Equals(normalized, XjRealmIds.TaiXi, StringComparison.Ordinal)) return "胎息";
        if (string.Equals(normalized, XjRealmIds.LianQi, StringComparison.Ordinal)) return "炼气";
        if (string.Equals(normalized, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return "筑基";
        if (string.Equals(normalized, XjRealmIds.ZiFu, StringComparison.Ordinal)) return "紫府";
        if (string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)) return "金丹";
        if (string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)) return "道胎";
        if (string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal)) return "神丹";
        if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return "黄冠";
        if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return "真人";
        if (string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return "真君羽士";
        if (string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return "道胎";
        if (string.Equals(realmId, "XianJi", StringComparison.OrdinalIgnoreCase)) return "仙基";
        return realmId ?? string.Empty;
    }

    internal static string GetUnifiedId(Actor actor, Func<Actor, string> traitFallback)
    {
        if (actor?.data == null) return string.Empty;
        if (XjAnnualExecutionContext.TryResolveRealmOverride(actor, out string overrideRealmId))
        {
            return NormalizeId(overrideRealmId);
        }

        string stored = string.Empty;
        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            && !string.IsNullOrWhiteSpace(realmId))
        {
            stored = NormalizeId(realmId);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                // RealmId is authoritative once present. Visible realm traits are only
                // a compatibility fallback for legacy/manual actors that have no stored
                // realm metadata; a stale/higher projection must never elevate business
                // eligibility or re-enter the cultivation state machine.
                return stored;
            }
        }
        return NormalizeId(traitFallback?.Invoke(actor));
    }

    internal static string GetTraitSnapshotForWriter(Actor actor)
    {
        if (actor?.data == null) return string.Empty;
        try
        {
            if (actor.hasTrait(XjRealmIds.FuQiDaoTai)) return "道胎";
            if (actor.hasTrait(XjRealmIds.DaoTai)) return "道胎";
            if (actor.hasTrait(XjRealmIds.ZhenJunYuShi)) return "真君羽士";
            if (actor.hasTrait(XjRealmIds.JinDan)) return "金丹";
            if (actor.hasTrait(XjRealmIds.FuQiZhenRen)) return "真人";
            if (actor.hasTrait(XjRealmIds.ZiFu)) return "紫府";
            if (actor.hasTrait(XjRealmIds.HuangGuan)) return "黄冠";
            if (actor.hasTrait(XjRealmIds.ZhuJi)) return "筑基";
            if (actor.hasTrait(XjRealmIds.LianQi)) return "炼气";
            if (actor.hasTrait(XjRealmIds.TaiXi)) return "胎息";
        }
        catch (System.Exception xjCaught136) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Data/Rules/XjRealmHelper.cs:136", xjCaught136); }
        return string.Empty;
    }

    internal static string GetTraitSnapshotForRouter(Actor actor)
    {
        if (actor?.data == null) return string.Empty;
        try
        {
            if (actor.hasTrait(XjRealmIds.FuQiDaoTai)) return XjRealmIds.FuQiDaoTai;
            if (actor.hasTrait(XjRealmIds.DaoTai)) return XjRealmIds.DaoTai;
            if (actor.hasTrait(XjRealmIds.ZhenJunYuShi)) return XjRealmIds.ZhenJunYuShi;
            if (actor.hasTrait(XjRealmIds.JinDan)) return XjRealmIds.JinDan;
            if (actor.hasTrait(XjRealmIds.FuQiZhenRen)) return XjRealmIds.FuQiZhenRen;
            if (actor.hasTrait(XjRealmIds.ZiFu)) return XjRealmIds.ZiFu;
            if (actor.hasTrait(XjRealmIds.HuangGuan)) return XjRealmIds.HuangGuan;
            if (actor.hasTrait(XjRealmIds.ZhuJi)) return XjRealmIds.ZhuJi;
            if (actor.hasTrait(XjRealmIds.LianQi)) return XjRealmIds.LianQi;
            if (actor.hasTrait(XjRealmIds.TaiXi)) return XjRealmIds.TaiXi;
        }
        catch (System.Exception xjCaught154) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Data/Rules/XjRealmHelper.cs:154", xjCaught154); }
        return string.Empty;
    }
}
