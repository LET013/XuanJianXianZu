using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.Cultivation;

/// <summary>
/// 道途关系只描述“道论拓扑”，不覆盖原著具名关系、权柄专属规则与克制表。
/// 业务判定的优先级应为：显式规则 > 近邻拓扑 > 结构远亲 > 完全空证。
///
/// 本表采用当前五现、十二炁六行与存毁二象的可验证部分：
/// - 五德按藏→蕴→正→变→收构成链，不再把同德五支全部视作近邻；
/// - 十二炁按六行图构成环，相对两炁另记“对炁”而非普通近邻；
/// - 五素德与三雷只记录五德映照；全丹/长庚仅保留存毁极的说明语义。
/// 未有依据的木雷、金雷和其他空位不补造。
/// </summary>
internal enum XjDaoTuRelationKind
{
	None = 0,
	DirectAdjacent = 1,
	Counterpart = 2,
	SameRootRemote = 3,
	ElementAffinity = 4
}

internal enum XjDaoTuDoctrinePolarity
{
	Neutral = 0,
	PreservationAspect = 1,
	PreservationPole = 2,
	DestructionAspect = 3,
	DestructionPole = 4
}

internal static class XjDaoTuRelationCatalog
{
	private static readonly string[][] FiveManifestationChains =
	{
		new[] { "库金", "逍金", "兑金", "庚金", "齐金" },
		new[] { "角木", "保木", "正木", "更木", "集木" },
		new[] { "牝水", "府水", "坎水", "渌水", "合水" },
		new[] { "牡火", "真火", "离火", "灴火", "并火" },
		new[] { "宝土", "戊土", "艮土", "宣土", "归土" }
	};

	private static readonly string[] ShiErQiRing =
	{
		"清炁", "晞炁", "华炁", "真炁", "煞炁", "下仪",
		"邃炁", "寒炁", "谪炁", "紫炁", "瑞炁", "上仪"
	};

	private static readonly string[][] ShiErQiCounterpartPairs =
	{
		new[] { "清炁", "邃炁" },
		new[] { "晞炁", "寒炁" },
		new[] { "华炁", "谪炁" },
		new[] { "真炁", "紫炁" },
		new[] { "煞炁", "瑞炁" },
		new[] { "下仪", "上仪" }
	};

	// 三阴、三阳、三雷当前仍各自构成完整的近邻小组；三雷的五德映照是另一层关系。
	private static readonly string[][] DirectCliques =
	{
		new[] { "太阴", "少阴", "厥阴" },
		new[] { "太阳", "少阳", "明阳" },
		new[] { "玄雷", "霄雷", "元雷" }
	};

	// “毁灭性 × 五德”目前只有原图能够落到现有正式道途的三项，木雷/金雷保持空缺。
	private static readonly Dictionary<string, string> DestructionElementAffinityRootByDaoTu =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["霄雷"] = XjDaoTuRootIds.ShuiDe,
			["元雷"] = XjDaoTuRootIds.TuDe,
			["玄雷"] = XjDaoTuRootIds.HuoDe
		};

	private static readonly Dictionary<string, string> LegacyAliases =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["乙木"] = "角木",
			["巽木"] = "正木",
			["弱水"] = "府水",
			["社土"] = "宣土",
			["稷土"] = "归土",
			["庾金"] = "庚金",
			["虹火"] = "灴火",
			["修越"] = "执孛"
		};

	private static readonly Dictionary<string, HashSet<string>> DirectMap = BuildDirectMap();
	private static readonly Dictionary<string, string> CounterpartMap = BuildCounterpartMap();

	internal static string Normalize(string daoTu)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		return LegacyAliases.TryGetValue(normalized, out string canonical) ? canonical : normalized;
	}

	internal static XjDaoTuRelationKind Resolve(string sourceDaoTu, string targetDaoTu)
	{
		string source = Normalize(sourceDaoTu);
		string target = Normalize(targetDaoTu);
		if (source.Length == 0 || target.Length == 0 || string.Equals(source, target, StringComparison.Ordinal))
		{
			return XjDaoTuRelationKind.None;
		}

		if (IsDirectAdjacent(source, target)) return XjDaoTuRelationKind.DirectAdjacent;
		if (CounterpartMap.TryGetValue(source, out string counterpart)
			&& string.Equals(counterpart, target, StringComparison.Ordinal))
		{
			return XjDaoTuRelationKind.Counterpart;
		}

		if (XjDaoTuCatalog.TryResolveRootId(source, out string sourceRoot)
			&& XjDaoTuCatalog.TryResolveRootId(target, out string targetRoot)
			&& string.Equals(sourceRoot, targetRoot, StringComparison.Ordinal))
		{
			return XjDaoTuRelationKind.SameRootRemote;
		}

		if (IsElementAffinity(source, target)) return XjDaoTuRelationKind.ElementAffinity;
		return XjDaoTuRelationKind.None;
	}

	internal static bool IsDirectAdjacent(string sourceDaoTu, string targetDaoTu)
	{
		string source = Normalize(sourceDaoTu);
		string target = Normalize(targetDaoTu);
		return source.Length > 0
			&& target.Length > 0
			&& !string.Equals(source, target, StringComparison.Ordinal)
			&& DirectMap.TryGetValue(source, out HashSet<string> values)
			&& values.Contains(target);
	}

	internal static bool IsStructuredRemote(string sourceDaoTu, string targetDaoTu)
	{
		XjDaoTuRelationKind relation = Resolve(sourceDaoTu, targetDaoTu);
		return relation == XjDaoTuRelationKind.Counterpart
			|| relation == XjDaoTuRelationKind.SameRootRemote
			|| relation == XjDaoTuRelationKind.ElementAffinity;
	}

	internal static IReadOnlyList<string> GetDirectNeighbors(string daoTu)
	{
		string normalized = Normalize(daoTu);
		if (normalized.Length == 0 || !DirectMap.TryGetValue(normalized, out HashSet<string> values))
		{
			return Array.Empty<string>();
		}
		List<string> result = new List<string>(values);
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	internal static IReadOnlyList<string> GetStructuredRemoteNeighbors(string daoTu)
	{
		string source = Normalize(daoTu);
		if (source.Length == 0) return Array.Empty<string>();

		HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < XjDaoTuCatalog.Definitions.Length; i++)
		{
			XjDaoTuDefinition definition = XjDaoTuCatalog.Definitions[i];
			IReadOnlyList<XjDaoTuBranchDefinition> branches = XjDaoTuCatalog.GetBranches(definition.RootId);
			if (branches.Count == 0)
			{
				string candidate = Normalize(definition.DisplayName);
				if (candidate.Length > 0 && IsStructuredRemote(source, candidate)) result.Add(candidate);
				continue;
			}
			for (int branchIndex = 0; branchIndex < branches.Count; branchIndex++)
			{
				string candidate = Normalize(branches[branchIndex].DisplayName);
				if (candidate.Length > 0 && IsStructuredRemote(source, candidate)) result.Add(candidate);
			}
		}

		// 长庚不是采气表分支，但作为后世新辟道途仍可参与展示；其金德映照只作高层语义，
		// 不通过 Resolve 形成服气/紫金普通远亲，因此这里不会误放入结构远闰候选。
		List<string> sorted = new List<string>(result);
		sorted.Sort(StringComparer.Ordinal);
		return sorted;
	}

	internal static string GetCounterpart(string daoTu)
	{
		string normalized = Normalize(daoTu);
		return normalized.Length > 0 && CounterpartMap.TryGetValue(normalized, out string counterpart)
			? counterpart
			: string.Empty;
	}

	internal static XjDaoTuDoctrinePolarity ResolvePolarity(string daoTu)
	{
		string normalized = Normalize(daoTu);
		if (string.Equals(normalized, "全丹", StringComparison.Ordinal)) return XjDaoTuDoctrinePolarity.PreservationPole;
		if (string.Equals(normalized, "长庚", StringComparison.Ordinal)) return XjDaoTuDoctrinePolarity.DestructionPole;
		if (DestructionElementAffinityRootByDaoTu.ContainsKey(normalized)) return XjDaoTuDoctrinePolarity.DestructionAspect;
		if (TryGetPreservationElementRoot(normalized, out _)) return XjDaoTuDoctrinePolarity.PreservationAspect;
		return XjDaoTuDoctrinePolarity.Neutral;
	}

	internal static string BuildDisplaySummary(string daoTu)
	{
		string normalized = Normalize(daoTu);
		if (normalized.Length == 0) return "未建立";

		List<string> parts = new List<string>();
		IReadOnlyList<string> direct = GetDirectNeighbors(normalized);
		if (direct.Count > 0) parts.Add("近邻：" + string.Join("、", direct));

		string counterpart = GetCounterpart(normalized);
		if (counterpart.Length > 0) parts.Add("对炁：" + counterpart);

		List<string> sameRootRemote = new List<string>();
		List<string> affinity = new List<string>();
		IReadOnlyList<string> remote = GetStructuredRemoteNeighbors(normalized);
		for (int i = 0; i < remote.Count; i++)
		{
			switch (Resolve(normalized, remote[i]))
			{
			case XjDaoTuRelationKind.SameRootRemote:
				sameRootRemote.Add(remote[i]);
				break;
			case XjDaoTuRelationKind.ElementAffinity:
				affinity.Add(remote[i]);
				break;
			}
		}
		if (sameRootRemote.Count > 0)
		{
			bool isShiErQi = XjDaoTuCatalog.TryResolveRootId(normalized, out string rootId)
				&& string.Equals(rootId, XjDaoTuRootIds.ShiErQi, StringComparison.Ordinal);
			parts.Add(isShiErQi
				? "同环远亲：其余" + sameRootRemote.Count + "炁"
				: "同根远亲：" + string.Join("、", sameRootRemote));
		}
		if (affinity.Count > 0)
		{
			parts.Add(TryGetMechanicalElementAffinityRoot(normalized, out string affinityRoot)
				? "五德映照：" + ResolveElementDisplayName(affinityRoot)
				: "五德映照：" + string.Join("、", affinity));
		}

		string doctrine = ResolvePolarityDisplay(normalized);
		if (doctrine.Length > 0) parts.Add("存毁象：" + doctrine);
		return parts.Count == 0 ? "无已立拓扑" : string.Join(" · ", parts);
	}

	internal static IReadOnlyList<string> Validate()
	{
		List<string> issues = new List<string>();
		foreach (KeyValuePair<string, HashSet<string>> pair in DirectMap)
		{
			if (!XjDaoTuCatalog.TryResolve(pair.Key, out _)) issues.Add("道网近邻包含未知道途：" + pair.Key);
			foreach (string target in pair.Value)
			{
				if (string.Equals(pair.Key, target, StringComparison.Ordinal)) issues.Add("道网出现自邻接：" + pair.Key);
				if (!XjDaoTuCatalog.TryResolve(target, out _)) issues.Add("道网近邻包含未知道途：" + target);
				if (!DirectMap.TryGetValue(target, out HashSet<string> reverse) || !reverse.Contains(pair.Key))
				{
					issues.Add("道网近邻不是双向关系：" + pair.Key + "→" + target);
				}
			}
		}

		foreach (KeyValuePair<string, string> pair in CounterpartMap)
		{
			if (!CounterpartMap.TryGetValue(pair.Value, out string reverse)
				|| !string.Equals(reverse, pair.Key, StringComparison.Ordinal))
			{
				issues.Add("十二炁对炁不是双向关系：" + pair.Key + "↔" + pair.Value);
			}
			if (IsDirectAdjacent(pair.Key, pair.Value))
			{
				issues.Add("十二炁对炁被错误登记为普通近邻：" + pair.Key + "↔" + pair.Value);
			}
		}

		return issues;
	}

	private static Dictionary<string, HashSet<string>> BuildDirectMap()
	{
		Dictionary<string, HashSet<string>> result =
			new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		for (int i = 0; i < FiveManifestationChains.Length; i++) AddChain(result, FiveManifestationChains[i]);
		for (int i = 0; i < DirectCliques.Length; i++) AddClique(result, DirectCliques[i]);
		AddRing(result, ShiErQiRing);
		// 渊照只保留两条最核心近邻：太阴之“照”与坎水之“渊”。它一经空证即为独立道途，
		// 不把所有阴、水相关道途都拉成普通近邻。
		AddPair(result, "太阴", "渊照");
		AddPair(result, "渊照", "坎水");
		// 虹霞虽由薛畋借戊土空证，却已经自成一途；闰位只留虹霞↔戊土这一条，
		// 不把其他土德、阴阳或炁途扩成“同源近邻”。
		AddPair(result, "虹霞", "戊土");
		return result;
	}

	private static Dictionary<string, string> BuildCounterpartMap()
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
		for (int i = 0; i < ShiErQiCounterpartPairs.Length; i++)
		{
			string[] pair = ShiErQiCounterpartPairs[i];
			if (pair == null || pair.Length != 2) continue;
			result[pair[0]] = pair[1];
			result[pair[1]] = pair[0];
		}
		return result;
	}

	private static void AddChain(Dictionary<string, HashSet<string>> map, string[] values)
	{
		for (int i = 0; values != null && i + 1 < values.Length; i++) AddPair(map, values[i], values[i + 1]);
	}

	private static void AddRing(Dictionary<string, HashSet<string>> map, string[] values)
	{
		if (values == null || values.Length < 2) return;
		for (int i = 0; i < values.Length; i++) AddPair(map, values[i], values[(i + 1) % values.Length]);
	}

	private static void AddClique(Dictionary<string, HashSet<string>> map, string[] values)
	{
		for (int i = 0; values != null && i < values.Length; i++)
		{
			for (int j = i + 1; j < values.Length; j++) AddPair(map, values[i], values[j]);
		}
	}

	private static void AddPair(Dictionary<string, HashSet<string>> map, string first, string second)
	{
		first = Normalize(first);
		second = Normalize(second);
		if (first.Length == 0 || second.Length == 0 || string.Equals(first, second, StringComparison.Ordinal)) return;
		if (!map.TryGetValue(first, out HashSet<string> firstSet))
		{
			firstSet = new HashSet<string>(StringComparer.Ordinal);
			map[first] = firstSet;
		}
		if (!map.TryGetValue(second, out HashSet<string> secondSet))
		{
			secondSet = new HashSet<string>(StringComparer.Ordinal);
			map[second] = secondSet;
		}
		firstSet.Add(second);
		secondSet.Add(first);
	}

	private static bool IsElementAffinity(string source, string target)
	{
		if (!XjDaoTuCatalog.TryResolveRootId(source, out string sourceRoot)
			|| !XjDaoTuCatalog.TryResolveRootId(target, out string targetRoot))
		{
			return false;
		}

		return TryGetMechanicalElementAffinityRoot(source, out string sourceAffinity)
				&& string.Equals(sourceAffinity, targetRoot, StringComparison.Ordinal)
			|| TryGetMechanicalElementAffinityRoot(target, out string targetAffinity)
				&& string.Equals(targetAffinity, sourceRoot, StringComparison.Ordinal);
	}

	private static bool TryGetMechanicalElementAffinityRoot(string daoTu, out string rootId)
	{
		rootId = string.Empty;
		string normalized = Normalize(daoTu);
		if (DestructionElementAffinityRootByDaoTu.TryGetValue(normalized, out string destructionRoot))
		{
			rootId = destructionRoot;
			return true;
		}

		if (!TryGetPreservationElementRoot(normalized, out string preservationRoot)) return false;
		rootId = preservationRoot;
		return true;
	}

	private static bool TryGetPreservationElementRoot(string daoTu, out string rootId)
	{
		rootId = string.Empty;
		string normalized = Normalize(daoTu);
		// 长庚虽登记金德亲和，但这里只保留“毁灭极·金德映照”的说明语义，
		// 不把它变成普通五德远闰目标。
		if (string.Equals(normalized, "长庚", StringComparison.Ordinal)) return false;
		if (!XjDaoTuCatalog.TryResolve(normalized, out XjDaoTuDefinition definition)
			|| !definition.IsBingGu
			|| string.IsNullOrWhiteSpace(definition.ElementAffinityRootId))
		{
			return false;
		}
		rootId = definition.ElementAffinityRootId;
		return true;
	}

	private static string ResolvePolarityDisplay(string daoTu)
	{
		switch (ResolvePolarity(daoTu))
		{
		case XjDaoTuDoctrinePolarity.PreservationPole:
			return "存续极";
		case XjDaoTuDoctrinePolarity.DestructionPole:
			return "毁灭极·金德映照";
		case XjDaoTuDoctrinePolarity.PreservationAspect:
			return TryGetPreservationElementRoot(daoTu, out string preservationRoot)
				? "存续侧·" + ResolveElementDisplayName(preservationRoot)
				: "存续侧";
		case XjDaoTuDoctrinePolarity.DestructionAspect:
			return DestructionElementAffinityRootByDaoTu.TryGetValue(Normalize(daoTu), out string destructionRoot)
				? "毁灭侧·" + ResolveElementDisplayName(destructionRoot)
				: "毁灭侧";
		default:
			return string.Empty;
		}
	}

	private static string ResolveElementDisplayName(string rootId)
	{
		return rootId switch
		{
			XjDaoTuRootIds.JinDe => "金德",
			XjDaoTuRootIds.MuDe => "木德",
			XjDaoTuRootIds.ShuiDe => "水德",
			XjDaoTuRootIds.HuoDe => "火德",
			XjDaoTuRootIds.TuDe => "土德",
			_ => rootId ?? string.Empty
		};
	}
}
