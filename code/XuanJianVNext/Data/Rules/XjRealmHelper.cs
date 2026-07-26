using System;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Data.Rules;

/// <summary>
/// 境界相关工具方法的统一入口。
/// 消除 XjChronicleWriter.Core / XjFamilyChronicleEventRouter 等处的重复实现。
/// </summary>
internal static class XjRealmHelper
{
    /// <summary>
    /// 判断 realmId 是否匹配指定的境界标识（"TaiXi"/"LianQi"/"ZhuJi"/"ZiFu"/"JinDan"/"ShenDan"）。
    /// 兼容 "XjRealmN" 旧标识和中文名称。
    /// </summary>
    internal static bool IsRealm(string realmId, string realm)
    {
        string value = (realmId ?? string.Empty).Trim();
        if (value.Length == 0) return false;
        return realm switch
        {
            "TaiXi" => string.Equals(value, "TaiXi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "XjRealm1", StringComparison.Ordinal)
                || value.IndexOf("胎息", StringComparison.Ordinal) >= 0,
            "LianQi" => string.Equals(value, "LianQi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "XjRealm2", StringComparison.Ordinal)
                || value.IndexOf("炼气", StringComparison.Ordinal) >= 0
                || value.IndexOf("\u7ec3\u6c14", StringComparison.Ordinal) >= 0,
            "ZhuJi" => string.Equals(value, "ZhuJi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "XjRealm3", StringComparison.Ordinal)
                || value.IndexOf("筑基", StringComparison.Ordinal) >= 0,
            "ZiFu" => string.Equals(value, "ZiFu", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "XjRealm4", StringComparison.Ordinal)
                || value.IndexOf("紫府", StringComparison.Ordinal) >= 0,
            "JinDan" => string.Equals(value, "JinDan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "XjRealm5", StringComparison.Ordinal)
                || value.IndexOf("金丹", StringComparison.Ordinal) >= 0,
            "ShenDan" => string.Equals(value, "ShenDan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "XjShenDan", StringComparison.Ordinal)
                || value.IndexOf("神丹", StringComparison.Ordinal) >= 0,
            _ => false
        };
    }

    /// <summary>
    /// 返回境界的顺序权值：金丹=5, 紫府=4, 筑基=3, 炼气=2, 胎息=1, 其他=0。
    /// </summary>
    internal static int GetOrder(string realmId)
    {
        string value = (realmId ?? string.Empty).Trim();
        if (value.Length == 0) return 0;
        if (IsRealm(value, "JinDan") || IsRealm(value, "ShenDan")) return 5;
        if (IsRealm(value, "ZiFu")) return 4;
        if (IsRealm(value, "ZhuJi")) return 3;
        if (IsRealm(value, "LianQi")) return 2;
        if (IsRealm(value, "TaiXi")) return 1;
        return 0;
    }

    /// <summary>
    /// 筑基及以上返回 true（用于纪事筛选）。
    /// </summary>
    internal static bool ShouldRecord(string realmId)
    {
        return GetOrder(realmId) >= 3;
    }

    /// <summary>
    /// 判断 realmId 是否为已知的境界标识符。
    /// </summary>
    internal static bool IsKnownTag(string realmId)
    {
        return NormalizeId(realmId).Length > 0;
    }

    internal static string NormalizeId(string realmId)
    {
        string value = (realmId ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (IsRealm(value, "TaiXi")) return XjRealmIds.TaiXi;
        if (IsRealm(value, "LianQi")) return XjRealmIds.LianQi;
        if (IsRealm(value, "ZhuJi")) return XjRealmIds.ZhuJi;
        if (IsRealm(value, "ZiFu")) return XjRealmIds.ZiFu;
        if (IsRealm(value, "JinDan")) return XjRealmIds.JinDan;
        if (IsRealm(value, "ShenDan")) return XjRealmIds.ShenDan;
        return string.Empty;
    }

    /// <summary>
    /// 根据境界标识返回中文显示名。
    /// 取代散布全代码库的 15+ 处独立 realmId→中文映射。
    /// </summary>
    internal static string GetDisplayName(string realmId)
    {
        if (string.IsNullOrWhiteSpace(realmId))
        {
            return string.Empty;
        }

        if (string.Equals(realmId, "TaiXi", StringComparison.OrdinalIgnoreCase)
            || string.Equals(realmId, "XjRealm1", StringComparison.Ordinal))
        {
            return "胎息";
        }
        if (string.Equals(realmId, "LianQi", StringComparison.OrdinalIgnoreCase)
            || string.Equals(realmId, "XjRealm2", StringComparison.Ordinal))
        {
            return "炼气";
        }
        if (string.Equals(realmId, "ZhuJi", StringComparison.OrdinalIgnoreCase)
            || string.Equals(realmId, "XjRealm3", StringComparison.Ordinal))
        {
            return "筑基";
        }
        if (string.Equals(realmId, "ZiFu", StringComparison.OrdinalIgnoreCase)
            || string.Equals(realmId, "XjRealm4", StringComparison.Ordinal))
        {
            return "紫府";
        }
        if (string.Equals(realmId, "XianJi", StringComparison.OrdinalIgnoreCase))
        {
            return "仙基";
        }
        if (string.Equals(realmId, "JinDan", StringComparison.OrdinalIgnoreCase)
            || string.Equals(realmId, "XjRealm5", StringComparison.Ordinal))
        {
            return "金丹";
        }
        if (string.Equals(realmId, "ShenDan", StringComparison.OrdinalIgnoreCase)
            || string.Equals(realmId, "XjShenDan", StringComparison.Ordinal))
        {
            return "神丹";
        }
        return realmId;
    }

    /// <summary>
    /// 统一境界检测：优先读存储的 RealmId 数据键，再回退到 trait 快照。
    /// </summary>
    internal static string GetUnifiedId(Actor actor, Func<Actor, string> traitFallback)
    {
        if (actor?.data == null)
        {
            return string.Empty;
        }
        if (XjAnnualExecutionContext.TryResolveRealmOverride(actor, out string overrideRealmId))
        {
            return NormalizeId(overrideRealmId);
        }

        string stored = string.Empty;
        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            && !string.IsNullOrWhiteSpace(realmId))
        {
            stored = NormalizeId(realmId);
        }
        string projected = NormalizeId(traitFallback?.Invoke(actor));
        return GetOrder(projected) > GetOrder(stored) ? projected : stored;
    }

    /// <summary>
    /// 根据 trait 返回境界标识（中文名版本，用于 ChronicleWriter）。
    /// </summary>
    internal static string GetTraitSnapshotForWriter(Actor actor)
    {
        if (actor?.data == null) return string.Empty;
        try
        {
            if (actor.hasTrait("XjRealm5")) return "金丹";
            if (actor.hasTrait("XjRealm4")) return "紫府";
            if (actor.hasTrait("XjRealm3")) return "筑基";
            if (actor.hasTrait("XjRealm2")) return "炼气";
            if (actor.hasTrait("XjRealm1")) return "胎息";
        }
        catch { }
        return string.Empty;
    }

    /// <summary>
    /// 根据 trait 返回境界标识（trait ID 版本，用于 ChronicleEventRouter）。
    /// </summary>
    internal static string GetTraitSnapshotForRouter(Actor actor)
    {
        if (actor?.data == null) return string.Empty;
        try
        {
            if (actor.hasTrait("XjRealm5")) return "XjRealm5";
            if (actor.hasTrait("XjRealm4")) return "XjRealm4";
            if (actor.hasTrait("XjRealm3")) return "XjRealm3";
            if (actor.hasTrait("XjRealm2")) return "XjRealm2";
            if (actor.hasTrait("XjRealm1")) return "XjRealm1";
        }
        catch { }
        return string.Empty;
    }
}
