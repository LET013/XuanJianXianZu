using System;
using System.Collections.Generic;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjDaoTuCounter
{
    /// <summary>
    /// 道途克制静态参考表／世界初始认知。
    /// Key = 克制方道途，Value = 被克制的道途列表。
    /// 局内高境确认击杀可以在此表之外形成新的动态克制，静态表不再限制战果演化。
    /// </summary>
    private static readonly Dictionary<string, string[]> CounterMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // ===== 阴阳 =====
        { "厥阴", new[] { "明阳" } },
        { "太阳", new[] { "少阳", "明阳", "鸺葵", "都卫" } },
        { "少阳", new[] { "兑金" } },
        { "明阳", new[] { "厥阴", "上巫", "鸺葵" } },

        // ===== 火 =====
        { "离火", new[] { "角木", "兑金", "庚金", "坎水" } },
        { "真火", new[] { "兑金", "庚金" } },
        { "牡火", new[] { "兑金", "庚金", "寒炁" } },
        { "并火", new[] { "兑金", "庚金", "全丹" } },
        { "灴火", new[] { "兑金", "庚金" } },

        // ===== 水 =====
        { "坎水", new[] { "离火" } },
        { "合水", new[] { "离火", "渌水", "全丹" } },

        // ===== 木 =====
        { "正木", new[] { "戊土" } },
        { "角木", new[] { "兑金" } },
        { "集木", new[] { "邃炁" } },

        // ===== 金 =====
        { "兑金", new[] { "玄雷" } },
        { "庚金", new[] { "玄雷" } },

        // ===== 土 =====
        { "艮土", new[] { "煞炁", "玄雷", "坎水" } },
        { "戊土", new[] { "离火", "牡火", "并火", "灴火", "坎水", "牝水", "明阳", "玄雷" } },
        { "宝土", new[] { "真火", "牡火", "并火", "灴火", "坎水" } },
        { "归土", new[] { "离火", "真火", "牡火", "并火", "灴火", "坎水", "玄雷" } },
        { "宣土", new[] { "离火", "真火", "牡火", "并火", "灴火", "坎水", "玄雷" } },

        // ===== 雷 =====
        { "玄雷", new[] { "合水" } },
        { "霄雷", new[] { "鸺葵" } },
        { "元雷", new[] { "全丹" } },

        // ===== 炁 =====
        { "煞炁", new[] { "庚金" } },
        { "晞炁", new[] { "明阳" } },
        { "华炁", new[] { "都卫" } },
        { "邃炁", new[] { "离火", "坎水", "府水", "合水", "晞炁", "都卫" } },

        // ===== 并古 =====
        // 玉真不与任何道途形成固定克制关系，仍保留在图鉴中展示“无”。
        { "玉真", Array.Empty<string>() },
        { "上巫", Array.Empty<string>() },
        { "鸺葵", Array.Empty<string>() },
        { "衡祝", new[] { "上巫", "鸺葵" } },
        // 原著“修越”在模组中登记为“执孛”，旧档若仍保存修越会在 Normalize 中归一。
        { "执孛", new[] { "邃炁" } },
        { "全丹", Array.Empty<string>() },
        { "都卫", new[] { "煞炁" } },

        // 虹霞空证后独立于现有克制网络：不克制任何道途，也不接受任何静态反克制。
        { "虹霞", Array.Empty<string>() },
    };


    internal static bool HasDirectCounter(string sourceDaoTu, string targetDaoTu)
    {
        string source = Normalize(sourceDaoTu);
        string target = Normalize(targetDaoTu);
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)
            || !CounterMap.TryGetValue(source, out string[] targets) || targets == null) return false;
        for (int i = 0; i < targets.Length; i++)
        {
            if (string.Equals(Normalize(targets[i]), target, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// 静态原著/设定克制作为世界初始战斗基线。若双方互有明确克制，则两边都保留
    /// 正向基线；局内高境击杀产生的动态状态会覆盖这个初始值。
    /// </summary>
    internal static float ResolveStaticCombatMultiplier(string attackerDaoTu, string defenderDaoTu)
    {
        string attacker = Normalize(attackerDaoTu);
        string defender = Normalize(defenderDaoTu);
        if (string.Equals(attacker, "虹霞", StringComparison.Ordinal)
            || string.Equals(defender, "虹霞", StringComparison.Ordinal)) return 1f;
        if (HasDirectCounter(attacker, defender)) return 1.30f;
        if (HasDirectCounter(defender, attacker)) return 0.70f;
        return 1f;
    }

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

        return normalized + "克制" + string.Join("、", targets);
    }

    internal static IReadOnlyCollection<string> GetKnownDaoTus()
    {
        // 图鉴需要同时显示“克制方”和“被克制方”；只返回字典 Key 会漏掉
        // 仅作为目标出现的道途。静态表规模很小，此处只在打开界面时构建一次集合。
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string[]> pair in CounterMap)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key)) result.Add(pair.Key);
            string[] targets = pair.Value;
            for (int i = 0; targets != null && i < targets.Length; i++)
            {
                string target = Normalize(targets[i]);
                if (!string.IsNullOrWhiteSpace(target)) result.Add(target);
            }
        }
        return result;
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

    internal static string Normalize(string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized switch
        {
            "修越" => "执孛",
            "庾金" => "庚金",
            "虹火" => "灴火",
            _ => normalized
        };
    }
}
