using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门名称单一权威规则：
/// 1. “玄鉴”为模组/仙鉴专用词，不允许进入宗门名称；
/// 2. 活跃宗门之间不允许出现相同名称前缀（如“玄阴门 / 玄阴观”）；
/// 3. 旧档载入时一次性修复保留字与前缀冲突，后续创建和改名统一走同一规则。
/// </summary>
internal static partial class XjSectRepository
{
	private const string ReservedSectTerm = "玄鉴";

	private static readonly string[] SectTerminalSuffixes =
	{
		"宗门", "仙宗", "道院", "书院",
		"府", "宗", "门", "道", "宫", "阁", "观", "院", "派"
	};

	private static readonly string[] UniversalSectPrefixes =
	{
		"太玄", "玉京", "云台", "青冥", "紫府", "洞真", "守一", "含章", "归元", "天衡",
		"太虚", "灵墟", "玄圭", "通明", "抱朴", "致和", "金阙", "翠微", "沧溟", "朝真",
		"栖霞", "鸣玉", "承露", "照影", "藏锋", "问道", "镇岳", "观澜", "丹霞", "白虹",
		"垂云", "辰星", "霄汉", "流火", "霁月", "朔风", "紫渊", "苍梧", "碧落", "空桑",
		"濯缨", "玉潢", "青霄", "凤鸣", "龙渊", "烨衡", "鸿蒙", "钧天", "素问", "玉衡",
		"天枢", "归藏", "云岫", "明真", "静虚", "玉宸", "灵台", "冲和", "景霄", "青华"
	};

	private static readonly string[] RarePrefixHeads =
	{
		"天", "玉", "云", "青", "紫", "丹", "灵", "太", "上", "元",
		"真", "苍", "碧", "金", "白", "赤", "玄", "归", "承", "景"
	};

	private static readonly string[] RarePrefixTails =
	{
		"阙", "台", "华", "渊", "霄", "岳", "宸", "衡", "枢", "虚",
		"墟", "泉", "岚", "光", "阳", "阴", "元", "真", "一", "和"
	};

	internal static bool ContainsReservedSectTerm(string value)
	{
		return !string.IsNullOrWhiteSpace(value)
			&& value.IndexOf(ReservedSectTerm, StringComparison.Ordinal) >= 0;
	}

	internal static string ExtractSectNamePrefix(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;

		for (int i = 0; i < SectTerminalSuffixes.Length; i++)
		{
			string suffix = SectTerminalSuffixes[i];
			if (text.Length <= suffix.Length || !text.EndsWith(suffix, StringComparison.Ordinal)) continue;
			return text.Substring(0, text.Length - suffix.Length).Trim();
		}

		return text;
	}

	internal static string ExtractSectNameSuffix(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text)) return "宗";
		for (int i = 0; i < SectTerminalSuffixes.Length; i++)
		{
			string suffix = SectTerminalSuffixes[i];
			if (text.Length > suffix.Length && text.EndsWith(suffix, StringComparison.Ordinal)) return suffix;
		}
		return "宗";
	}

	internal static bool IsSectNameAvailable(string candidate, long ignoreSectId = 0L)
	{
		string normalized = (candidate ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized) || ContainsReservedSectTerm(normalized)) return false;
		string prefix = ExtractSectNamePrefix(normalized);
		if (string.IsNullOrWhiteSpace(prefix)) return false;

		foreach (XjSectArchiveRecord record in BySectId.Values)
		{
			if (record == null || record.SectId == ignoreSectId || !IsEstablishedSect(record)) continue;
			string existingPrefix = ExtractSectNamePrefix(record.Name);
			if (!string.IsNullOrWhiteSpace(existingPrefix)
				&& string.Equals(existingPrefix, prefix, StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	internal static string BuildUniqueFallbackSectName(long seed, string preferredSuffix = "宗")
	{
		string suffix = NormalizeGeneratedSuffix(preferredSuffix);
		int universalStart = PositiveModulo(seed, UniversalSectPrefixes.Length);
		for (int offset = 0; offset < UniversalSectPrefixes.Length; offset++)
		{
			string prefix = UniversalSectPrefixes[(universalStart + offset) % UniversalSectPrefixes.Length];
			string candidate = prefix + suffix;
			if (IsSectNameAvailable(candidate)) return candidate;
		}

		int combinationCount = RarePrefixHeads.Length * RarePrefixTails.Length;
		int combinationStart = PositiveModulo(seed ^ 0x5A17A53L, combinationCount);
		for (int offset = 0; offset < combinationCount; offset++)
		{
			int index = (combinationStart + offset) % combinationCount;
			string prefix = RarePrefixHeads[index / RarePrefixTails.Length] + RarePrefixTails[index % RarePrefixTails.Length];
			if (ContainsReservedSectTerm(prefix)) continue;
			string candidate = prefix + suffix;
			if (IsSectNameAvailable(candidate)) return candidate;
		}

		// 正常世界规模不会抵达这里；以稳定数字尾确保即便极端模组联动也不破坏前缀唯一性。
		long serial = Math.Abs(seed == long.MinValue ? long.MaxValue : seed);
		for (int attempt = 0; attempt < 10000; attempt++)
		{
			string candidate = "道纪" + (serial + attempt).ToString(System.Globalization.CultureInfo.InvariantCulture) + suffix;
			if (IsSectNameAvailable(candidate)) return candidate;
		}
		return "无名宗";
	}

	/// <summary>
	/// 旧档名称修复：低 SectId 的合法前缀优先保留；后续重复项和所有含“玄鉴”的名称改为唯一安全名。
	/// 不写历史改名事件，避免把一次版本迁移伪装成世界内事件。
	/// </summary>
	internal static int RepairSectNameInvariantsAfterImport()
	{
		if (BySectId.Count == 0) return 0;
		List<long> ids = new List<long>(BySectId.Keys);
		ids.Sort();

		Dictionary<string, long> prefixOwner = new Dictionary<string, long>(StringComparer.Ordinal);
		for (int i = 0; i < ids.Count; i++)
		{
			if (!BySectId.TryGetValue(ids[i], out XjSectArchiveRecord record) || record == null || !IsEstablishedSect(record)) continue;
			string name = (record.Name ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(name) || ContainsReservedSectTerm(name)) continue;
			string prefix = ExtractSectNamePrefix(name);
			if (string.IsNullOrWhiteSpace(prefix) || prefixOwner.ContainsKey(prefix)) continue;
			prefixOwner[prefix] = record.SectId;
		}

		HashSet<string> protectedPrefixes = new HashSet<string>(prefixOwner.Keys, StringComparer.Ordinal);
		HashSet<string> assignedPrefixes = new HashSet<string>(StringComparer.Ordinal);
		int changed = 0;

		for (int i = 0; i < ids.Count; i++)
		{
			if (!BySectId.TryGetValue(ids[i], out XjSectArchiveRecord record) || record == null || !IsEstablishedSect(record)) continue;
			string current = (record.Name ?? string.Empty).Trim();
			string prefix = ExtractSectNamePrefix(current);
			bool keep = !string.IsNullOrWhiteSpace(current)
				&& !ContainsReservedSectTerm(current)
				&& !string.IsNullOrWhiteSpace(prefix)
				&& prefixOwner.TryGetValue(prefix, out long ownerId)
				&& ownerId == record.SectId
				&& assignedPrefixes.Add(prefix);
			if (keep) continue;

			string suffix = ExtractSectNameSuffix(current);
			string replacement = BuildImportedRepairName(record.SectId, suffix, protectedPrefixes, assignedPrefixes);
			if (!string.Equals(current, replacement, StringComparison.Ordinal))
			{
				record.Name = replacement;
				changed++;
			}
			string replacementPrefix = ExtractSectNamePrefix(replacement);
			if (!string.IsNullOrWhiteSpace(replacementPrefix)) assignedPrefixes.Add(replacementPrefix);
		}
		return changed;
	}

	private static string BuildImportedRepairName(long sectId, string suffix, HashSet<string> protectedPrefixes, HashSet<string> assignedPrefixes)
	{
		suffix = NormalizeGeneratedSuffix(suffix);
		int universalStart = PositiveModulo(sectId, UniversalSectPrefixes.Length);
		for (int offset = 0; offset < UniversalSectPrefixes.Length; offset++)
		{
			string prefix = UniversalSectPrefixes[(universalStart + offset) % UniversalSectPrefixes.Length];
			if (protectedPrefixes.Contains(prefix) || assignedPrefixes.Contains(prefix) || ContainsReservedSectTerm(prefix)) continue;
			return prefix + suffix;
		}

		int combinationCount = RarePrefixHeads.Length * RarePrefixTails.Length;
		int combinationStart = PositiveModulo(sectId ^ 0x2D4B9A1L, combinationCount);
		for (int offset = 0; offset < combinationCount; offset++)
		{
			int index = (combinationStart + offset) % combinationCount;
			string prefix = RarePrefixHeads[index / RarePrefixTails.Length] + RarePrefixTails[index % RarePrefixTails.Length];
			if (protectedPrefixes.Contains(prefix) || assignedPrefixes.Contains(prefix) || ContainsReservedSectTerm(prefix)) continue;
			return prefix + suffix;
		}

		long serial = Math.Abs(sectId == long.MinValue ? long.MaxValue : sectId);
		for (int attempt = 0; attempt < 10000; attempt++)
		{
			string prefix = "道纪" + (serial + attempt).ToString(System.Globalization.CultureInfo.InvariantCulture);
			if (protectedPrefixes.Contains(prefix) || assignedPrefixes.Contains(prefix)) continue;
			return prefix + suffix;
		}
		return "无名宗";
	}

	private static string NormalizeGeneratedSuffix(string suffix)
	{
		string value = (suffix ?? string.Empty).Trim();
		for (int i = 0; i < SectTerminalSuffixes.Length; i++)
		{
			if (string.Equals(value, SectTerminalSuffixes[i], StringComparison.Ordinal)) return value;
		}
		return "宗";
	}

	private static int PositiveModulo(long value, int modulus)
	{
		if (modulus <= 0) return 0;
		long result = value % modulus;
		if (result < 0L) result += modulus;
		return (int)result;
	}
}
