using System;
using System.Collections.Generic;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjDaoTuCounter
{
    /// <summary>
    /// 道途克制静态参考表
    /// Key = 克制方道途，Value = 被克制的道途列表
    /// 仅当克制关系在此表中存在时，局内金丹击杀才允许记录动态堆叠
    /// </summary>
    private static readonly Dictionary<string, string[]> CounterMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // ===== 阴阳 =====
        { "厥阴", new[] { "明阳" } },
        { "太阳", new[] { "少阳", "明阳" } },
        { "少阳", new[] { "兑金" } },
        { "明阳", new[] { "厥阴" } },

        // ===== 火 =====
        { "离火", new[] { "角木", "兑金", "庚金", "坎水" } },
        { "真火", new[] { "兑金", "庾金" } },
        { "牡火", new[] { "兑金", "庚金", "寒炁" } },
        { "并火", new[] { "兑金", "庚金" } },
        { "灴火", new[] { "兑金", "庚金" } },

        // ===== 水 =====
        { "坎水", new[] { "离火" } },
        { "合水", new[] { "离火", "渌水" } },

        // ===== 木 =====
        { "正木", new[] { "戊土" } },
        { "角木", new[] { "兑金" } },
        { "集木", new[] { "邃炁" } },

        // ===== 金 =====
        { "兑金", new[] { "玄雷" } },
        { "庾金", new[] { "玄雷" } },

        // ===== 土 =====
        { "艮土", new[] { "煞炁", "玄雷", "坎水" } },
        { "戊土", new[] { "离火", "牡火", "并火", "灴火", "坎水", "牝水", "明阳", "玄雷" } },
        { "宝土", new[] { "真火", "牡火", "并火", "虹火", "坎水" } },
        { "归土", new[] { "离火", "真火", "牡火", "并火", "灴火", "坎水", "玄雷" } },
        { "宣土", new[] { "离火", "真火", "牡火", "并火", "灴火", "坎水", "玄雷" } },

        // ===== 雷 =====
        { "玄雷", new[] { "合水" } },

        // ===== 炁 =====
        { "煞炁", new[] { "庚金" } },
        { "晞炁", new[] { "明阳" } },
        { "邃炁", new[] { "离火", "坎水", "府水", "合水", "晞炁" } },
    };

    internal static string GetDisplayText(string daoTu)
    {
        string normalized = Normalize(daoTu);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (!CounterMap.TryGetValue(normalized, out string[] targets) || targets == null || targets.Length == 0)
        {
            return normalized;
        }

        return normalized + " -> " + string.Join("、", targets);
    }

    internal static IReadOnlyCollection<string> GetKnownDaoTus()
    {
        return CounterMap.Keys;
    }

    internal static IReadOnlyList<string> GetCounterTargets(string daoTu)
    {
        string normalized = Normalize(daoTu);
        if (string.IsNullOrWhiteSpace(normalized)
            || !CounterMap.TryGetValue(normalized, out string[] targets)
            || targets == null
            || targets.Length == 0)
        {
            return Array.Empty<string>();
        }

        return new List<string>(targets);
    }

    internal static IReadOnlyList<string> GetCounteredBy(string daoTu)
    {
        string normalized = Normalize(daoTu);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<string>();
        }

        List<string> result = new List<string>();
        foreach (KeyValuePair<string, string[]> pair in CounterMap)
        {
            string[] targets = pair.Value;
            if (targets == null)
            {
                continue;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (string.Equals(targets[i], normalized, StringComparison.Ordinal))
                {
                    result.Add(pair.Key);
                    break;
                }
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim();
    }
}