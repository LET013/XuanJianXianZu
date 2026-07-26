using System;

namespace XuanJianVNext.Core;

/// <summary>
/// 通用的字符串工具方法。
/// 消除散布全代码库的 ~17 份 Normalize(string) 私有实现。
/// </summary>
internal static class XjStringHelper
{
    private static readonly string[] RealmSuffixes =
    {
        "-结璘仙",
        "-神丹",
        "-金丹",
        "-紫府",
        "-筑基",
        "-炼气",
        "-胎息"
    };

    /// <summary>
    /// 去除字符串两端的空白，null 视为空字符串。
    /// </summary>
    internal static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim();
    }

    /// <summary>
    /// 安全获取 Actor 名称，null/死亡/异常时返回 fallback。
    /// </summary>
    internal static string ActorName(Actor actor, string fallback = "无名修士")
    {
        if (actor?.data == null)
        {
            return fallback ?? "无名修士";
        }

        try
        {
            string name = actor.getName();
            return string.IsNullOrWhiteSpace(name) ? (fallback ?? "无名修士") : name.Trim();
        }
        catch
        {
            return fallback ?? "无名修士";
        }
    }

    internal static string StripRealmSuffix(string value)
    {
        string normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < RealmSuffixes.Length; i++)
            {
                string suffix = RealmSuffixes[i];
                if (normalized.EndsWith(suffix, StringComparison.Ordinal))
                {
                    normalized = normalized.Substring(0, normalized.Length - suffix.Length).TrimEnd();
                    changed = true;
                    break;
                }
            }
        }
        while (changed && normalized.Length > 0);

        return normalized;
    }

    internal static string DisplayNameWithoutRealmSuffix(string value, string fallback = "无名修士")
    {
        string normalized = Normalize(value);
        if (normalized.Length == 0 || string.Equals(normalized, "NO_NAME", StringComparison.OrdinalIgnoreCase))
        {
            normalized = fallback ?? "无名修士";
        }

        string stripped = StripRealmSuffix(normalized);
        return stripped.Length == 0 ? (fallback ?? "无名修士") : stripped;
    }

    internal static string ActorNameWithoutRealmSuffix(Actor actor, string fallback = "无名修士")
    {
        return DisplayNameWithoutRealmSuffix(ActorName(actor, fallback), fallback);
    }
}
