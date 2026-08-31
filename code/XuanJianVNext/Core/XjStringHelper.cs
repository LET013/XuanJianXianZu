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
        // 按“长后缀优先”排列。角色称号在高境阶段会把小境界一并拼入姓名，
        // 公告/史册去境界时必须先剥离完整阶段，避免只删掉“-金丹”之类的短尾。
        "-真君羽士巅峰", "-真君羽士后期", "-真君羽士中期", "-真君羽士初期",
        "-金丹巅峰", "-金丹后期", "-金丹中期", "-金丹初期",
        "-真人巅峰", "-真人后期", "-真人中期", "-真人初期",
        "-紫府巅峰", "-紫府后期", "-紫府中期", "-紫府初期",
        "-九世摩诃", "-八世摩诃", "-七世摩诃", "-六世摩诃", "-五世摩诃",
        "-四世摩诃", "-三世摩诃", "-二世摩诃", "-一世摩诃",
        "-世尊", "-法相", "-摩诃", "-怜愍", "-法师", "-僧侣",
        "-真君羽士", "-郁仪仙", "-结璘仙", "-道胎", "-神丹",
        "-金丹", "-真人", "-黄冠", "-紫府", "-筑基", "-炼气", "-胎息"
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
                string fullWidthSuffix = suffix.Length > 1 && suffix[0] == '-'
                    ? "－" + suffix.Substring(1)
                    : suffix;
                string matchedSuffix = normalized.EndsWith(suffix, StringComparison.Ordinal)
                    ? suffix
                    : normalized.EndsWith(fullWidthSuffix, StringComparison.Ordinal) ? fullWidthSuffix : string.Empty;
                if (matchedSuffix.Length > 0)
                {
                    normalized = normalized.Substring(0, normalized.Length - matchedSuffix.Length).TrimEnd();
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
