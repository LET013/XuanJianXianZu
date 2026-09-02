using System;
using System.Collections.Generic;
using XuanJianVNext.Data.CaiQi;

namespace XuanJianVNext.Data.Cultivation;

/// <summary>
/// 道途根类：现有采气分支（太阴、庚金、宣土等）归属于十八条古有道途根类；
/// 并古九途与后起长庚直接以根类作为当前道途名。道途与修炼方式分离，
/// SupportsFuQi/SupportsZiJin 只声明可行性，不代表对应玩法已经完成。
/// </summary>
internal static class XjDaoTuRootIds
{
	// 常见九途：三阴、三阳、三雷、五德、十二炁。
	internal const string SanYin = "san_yin";
	internal const string SanYang = "san_yang";
	internal const string SanLei = "san_lei";
	internal const string JinDe = "jin_de";
	internal const string MuDe = "mu_de";
	internal const string ShuiDe = "shui_de";
	internal const string HuoDe = "huo_de";
	internal const string TuDe = "tu_de";
	internal const string ShiErQi = "shi_er_qi";

	// 并古九途。
	internal const string XiaoKui = "xiao_kui";
	internal const string ShangWu = "shang_wu";
	internal const string YuZhen = "yu_zhen";
	internal const string HengZhu = "heng_zhu";
	internal const string QingXuan = "qing_xuan";
	internal const string QuanDan = "quan_dan";
	internal const string ZhiBo = "zhi_bo";
	internal const string SiTian = "si_tian";
	internal const string DuWei = "du_wei";

	// 后世新辟。
	internal const string LongGeng = "long_geng";
	internal const string YuanZhao = "yuan_zhao";
	internal const string HongXia = "hong_xia";
}

internal static class XjDaoTuGroupIds
{
	internal const string CommonAncient = "common_ancient";
	internal const string BingGu = "bing_gu";
	internal const string LaterFounded = "later_founded";
}

internal static class XjDaoTuGateIds
{
	internal const string None = "none";
	// 青宣的紫府金丹修法必须在服气青宣（玄羊子）真正显世后才能出现。
	internal const string FuQiBeforeZiJin = "fuqi_before_zijin";
	// 长庚必须先由无名服气剑道开位并命名，后世才可推演紫金长庚。
	internal const string LongGengEstablished = "long_geng_established";
}

internal readonly struct XjDaoTuDefinition
{
	internal readonly string RootId;
	internal readonly string DisplayName;
	internal readonly string GroupId;
	internal readonly bool SupportsFuQi;
	internal readonly bool SupportsZiJin;
	internal readonly string ElementAffinityRootId;
	internal readonly string ZiJinGateId;

	internal XjDaoTuDefinition(
		string rootId,
		string displayName,
		string groupId,
		bool supportsFuQi,
		bool supportsZiJin,
		string elementAffinityRootId,
		string ziJinGateId)
	{
		RootId = rootId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		GroupId = groupId ?? string.Empty;
		SupportsFuQi = supportsFuQi;
		SupportsZiJin = supportsZiJin;
		ElementAffinityRootId = elementAffinityRootId ?? string.Empty;
		ZiJinGateId = ziJinGateId ?? XjDaoTuGateIds.None;
	}

	internal bool IsBingGu => string.Equals(GroupId, XjDaoTuGroupIds.BingGu, StringComparison.Ordinal);
	internal bool IsCommonAncient => string.Equals(GroupId, XjDaoTuGroupIds.CommonAncient, StringComparison.Ordinal);
	internal bool IsLaterFounded => string.Equals(GroupId, XjDaoTuGroupIds.LaterFounded, StringComparison.Ordinal);
}

internal readonly struct XjDaoTuBranchDefinition
{
	internal readonly string RootId;
	internal readonly string BranchId;
	internal readonly string DisplayName;
	internal readonly string TraitId;

	internal XjDaoTuBranchDefinition(string rootId, string branchId, string displayName, string traitId)
	{
		RootId = rootId ?? string.Empty;
		BranchId = branchId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		TraitId = traitId ?? string.Empty;
	}
}

internal static class XjDaoTuCatalog
{
	internal static readonly XjDaoTuDefinition[] Definitions =
	{
		new XjDaoTuDefinition(XjDaoTuRootIds.SanYin, "三阴", XjDaoTuGroupIds.CommonAncient, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.SanYang, "三阳", XjDaoTuGroupIds.CommonAncient, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.SanLei, "三雷", XjDaoTuGroupIds.CommonAncient, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.JinDe, "金德", XjDaoTuGroupIds.CommonAncient, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.MuDe, "木德", XjDaoTuGroupIds.CommonAncient, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.ShuiDe, "水德", XjDaoTuGroupIds.CommonAncient, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.HuoDe, "火德", XjDaoTuGroupIds.CommonAncient, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.TuDe, "土德", XjDaoTuGroupIds.CommonAncient, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.ShiErQi, "十二炁", XjDaoTuGroupIds.CommonAncient, true, true, string.Empty, XjDaoTuGateIds.None),

		new XjDaoTuDefinition(XjDaoTuRootIds.XiaoKui, "鸺葵", XjDaoTuGroupIds.BingGu, true, true, XjDaoTuRootIds.ShuiDe, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.ShangWu, "上巫", XjDaoTuGroupIds.BingGu, true, true, XjDaoTuRootIds.MuDe, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.YuZhen, "玉真", XjDaoTuGroupIds.BingGu, true, true, XjDaoTuRootIds.JinDe, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.HengZhu, "衡祝", XjDaoTuGroupIds.BingGu, true, true, XjDaoTuRootIds.HuoDe, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.QingXuan, "青宣", XjDaoTuGroupIds.BingGu, true, true, XjDaoTuRootIds.TuDe, XjDaoTuGateIds.FuQiBeforeZiJin),
		new XjDaoTuDefinition(XjDaoTuRootIds.QuanDan, "全丹", XjDaoTuGroupIds.BingGu, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.ZhiBo, "执孛", XjDaoTuGroupIds.BingGu, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.SiTian, "司天", XjDaoTuGroupIds.BingGu, true, true, string.Empty, XjDaoTuGateIds.None),
		new XjDaoTuDefinition(XjDaoTuRootIds.DuWei, "都卫", XjDaoTuGroupIds.BingGu, true, true, string.Empty, XjDaoTuGateIds.None),

		new XjDaoTuDefinition(XjDaoTuRootIds.LongGeng, "长庚", XjDaoTuGroupIds.LaterFounded, true, true, XjDaoTuRootIds.JinDe, XjDaoTuGateIds.LongGengEstablished),
		// 渊照不是三阴或水德的分支，而是玄鉴历第五百年后由太阴之照、坎水之渊空证出的独立后世道途。
		new XjDaoTuDefinition(XjDaoTuRootIds.YuanZhao, "渊照", XjDaoTuGroupIds.LaterFounded, true, true, string.Empty, XjDaoTuGateIds.None),
		// 虹霞由薛畋借戊土空证，却已独立于阴阳五德、十二炁与并古诸道之外。
		// 虹霞可循服气养性或紫府金丹两条正统路径修持；不把戊土机械映照为克制/亲缘。
		new XjDaoTuDefinition(XjDaoTuRootIds.HongXia, "虹霞", XjDaoTuGroupIds.LaterFounded, true, true, string.Empty, XjDaoTuGateIds.None)
	};

	private static readonly XjDaoTuBranchDefinition[] Branches = BuildBranches();
	private static readonly Dictionary<string, XjDaoTuDefinition> DefinitionByRootId =
		BuildDefinitionByRootId();
	private static readonly Dictionary<string, string> RootIdByAlias =
		BuildRootIdByAlias();
	private static readonly Dictionary<string, XjDaoTuBranchDefinition[]> BranchesByRootId =
		BuildBranchesByRootId();

	internal static bool TryGetByRootId(string rootId, out XjDaoTuDefinition definition)
	{
		string normalized = Normalize(rootId);
		return DefinitionByRootId.TryGetValue(normalized, out definition);
	}

	internal static bool TryResolve(string daoTuOrRoot, out XjDaoTuDefinition definition)
	{
		string normalized = Normalize(daoTuOrRoot);
		if (normalized.Length == 0)
		{
			definition = default;
			return false;
		}
		if (RootIdByAlias.TryGetValue(normalized, out string rootId))
		{
			return DefinitionByRootId.TryGetValue(rootId, out definition);
		}
		definition = default;
		return false;
	}

	internal static bool TryResolveRootId(string daoTuOrRoot, out string rootId)
	{
		rootId = string.Empty;
		if (!TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)) return false;
		rootId = definition.RootId;
		return rootId.Length > 0;
	}

	internal static bool IsCommonAncient(string daoTuOrRoot)
	{
		return TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition) && definition.IsCommonAncient;
	}

	internal static bool IsBingGu(string daoTuOrRoot)
	{
		return TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition) && definition.IsBingGu;
	}

	internal static bool SupportsFuQi(string daoTuOrRoot)
	{
		return TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition) && definition.SupportsFuQi;
	}

	internal static bool SupportsZiJin(string daoTuOrRoot)
	{
		return TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition) && definition.SupportsZiJin;
	}

	internal static IReadOnlyList<XjDaoTuBranchDefinition> GetBranches(string rootId)
	{
		string normalized = Normalize(rootId);
		if (BranchesByRootId.TryGetValue(normalized, out XjDaoTuBranchDefinition[] result))
		{
			return result;
		}
		return Array.Empty<XjDaoTuBranchDefinition>();
	}

	/// <summary>
	/// 返回可供服气普通闰位牵连的“道论近邻”。
	/// 0.9.7.3 以前这里把同根所有支脉全部等价相邻，导致五德与十二炁的远支也被低门槛闰入。
	/// 现统一读取 XjDaoTuRelationCatalog；结构远亲由服气解析器在高道慧阶段另行尝试。
	/// </summary>
	internal static IReadOnlyList<string> GetFuQiRelatedDaoTus(string daoTuOrRoot)
	{
		IReadOnlyList<string> neighbors = XjDaoTuRelationCatalog.GetDirectNeighbors(daoTuOrRoot);
		if (neighbors.Count == 0) return Array.Empty<string>();

		List<string> related = new List<string>(neighbors.Count);
		for (int i = 0; i < neighbors.Count; i++)
		{
			string candidate = XjDaoTuRelationCatalog.Normalize(neighbors[i]);
			if (candidate.Length > 0 && SupportsFuQi(candidate) && !related.Contains(candidate))
			{
				related.Add(candidate);
			}
		}
		return related;
	}

	private static XjDaoTuBranchDefinition[] BuildBranches()
	{
		List<XjDaoTuBranchDefinition> result = new List<XjDaoTuBranchDefinition>();
		for (int i = 0; i < XjCaiQiCatalog.Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = XjCaiQiCatalog.Entries[i];
			string rootId = ResolveRootIdFromEntry(in entry);
			if (rootId.Length == 0) continue;
			result.Add(new XjDaoTuBranchDefinition(rootId, entry.BranchId, entry.DisplayName, entry.OldTraitId));
		}
		return result.ToArray();
	}

	private static string ResolveRootIdFromEntry(in XjCaiQiCatalogEntry entry)
	{
		if (string.Equals(entry.PlaceTypeId, XjCaiQiPlaceTypeIds.BingGu, StringComparison.Ordinal))
		{
			return entry.BranchId switch
			{
				XjCaiQiBranchIds.XiaoKui => XjDaoTuRootIds.XiaoKui,
				XjCaiQiBranchIds.ShangWu => XjDaoTuRootIds.ShangWu,
				XjCaiQiBranchIds.YuZhen => XjDaoTuRootIds.YuZhen,
				XjCaiQiBranchIds.HengZhu => XjDaoTuRootIds.HengZhu,
				XjCaiQiBranchIds.QingXuan => XjDaoTuRootIds.QingXuan,
				XjCaiQiBranchIds.QuanDan => XjDaoTuRootIds.QuanDan,
				XjCaiQiBranchIds.ZhiBo => XjDaoTuRootIds.ZhiBo,
				XjCaiQiBranchIds.SiTian => XjDaoTuRootIds.SiTian,
				XjCaiQiBranchIds.DuWei => XjDaoTuRootIds.DuWei,
				_ => string.Empty
			};
		}

		return entry.PlaceTypeId switch
		{
			XjCaiQiPlaceTypeIds.SanYin => XjDaoTuRootIds.SanYin,
			XjCaiQiPlaceTypeIds.SanYang => XjDaoTuRootIds.SanYang,
			XjCaiQiPlaceTypeIds.SanLei => XjDaoTuRootIds.SanLei,
			XjCaiQiPlaceTypeIds.JinDe => XjDaoTuRootIds.JinDe,
			XjCaiQiPlaceTypeIds.MuDe => XjDaoTuRootIds.MuDe,
			XjCaiQiPlaceTypeIds.ShuiDe => XjDaoTuRootIds.ShuiDe,
			XjCaiQiPlaceTypeIds.HuoDe => XjDaoTuRootIds.HuoDe,
			XjCaiQiPlaceTypeIds.TuDe => XjDaoTuRootIds.TuDe,
			XjCaiQiPlaceTypeIds.ShiErQi => XjDaoTuRootIds.ShiErQi,
			_ => string.Empty
		};
	}

	private static Dictionary<string, XjDaoTuDefinition> BuildDefinitionByRootId()
	{
		Dictionary<string, XjDaoTuDefinition> result =
			new Dictionary<string, XjDaoTuDefinition>(Definitions.Length, StringComparer.Ordinal);
		for (int i = 0; i < Definitions.Length; i++)
		{
			result[Definitions[i].RootId] = Definitions[i];
		}
		return result;
	}

	private static Dictionary<string, string> BuildRootIdByAlias()
	{
		Dictionary<string, string> result =
			new Dictionary<string, string>(Definitions.Length + Branches.Length * 3, StringComparer.Ordinal);
		for (int i = 0; i < Definitions.Length; i++)
		{
			XjDaoTuDefinition definition = Definitions[i];
			result[definition.RootId] = definition.RootId;
			if (!string.IsNullOrWhiteSpace(definition.DisplayName))
			{
				result[definition.DisplayName] = definition.RootId;
			}
		}

		for (int i = 0; i < Branches.Length; i++)
		{
			XjDaoTuBranchDefinition branch = Branches[i];
			if (!string.IsNullOrWhiteSpace(branch.BranchId)) result[branch.BranchId] = branch.RootId;
			if (!string.IsNullOrWhiteSpace(branch.DisplayName)) result[branch.DisplayName] = branch.RootId;
			if (!string.IsNullOrWhiteSpace(branch.TraitId)) result[branch.TraitId] = branch.RootId;
		}
		return result;
	}

	private static Dictionary<string, XjDaoTuBranchDefinition[]> BuildBranchesByRootId()
	{
		Dictionary<string, List<XjDaoTuBranchDefinition>> builders =
			new Dictionary<string, List<XjDaoTuBranchDefinition>>(StringComparer.Ordinal);
		for (int i = 0; i < Branches.Length; i++)
		{
			XjDaoTuBranchDefinition branch = Branches[i];
			if (!builders.TryGetValue(branch.RootId, out List<XjDaoTuBranchDefinition> items))
			{
				items = new List<XjDaoTuBranchDefinition>();
				builders[branch.RootId] = items;
			}
			items.Add(branch);
		}

		Dictionary<string, XjDaoTuBranchDefinition[]> result =
			new Dictionary<string, XjDaoTuBranchDefinition[]>(builders.Count, StringComparer.Ordinal);
		foreach (KeyValuePair<string, List<XjDaoTuBranchDefinition>> pair in builders)
		{
			result[pair.Key] = pair.Value.ToArray();
		}
		return result;
	}

	private static string Normalize(string value) => XjDaoTuIntentIdentity.ResolveCore(value);
}

/// <summary>
/// 妖、帝意向是对同一修炼道途的身份覆名。数据层只在边界剥去前缀，
/// 让果位、神通、功法继续使用既有本道索引，不复制任何目录或年度扫描。
/// </summary>
internal static class XjDaoTuIntentIdentity
{
	internal const string YaoPrefix = "妖·";
	internal const string DiPrefix = "帝·";

	internal static string ResolveCore(string value)
	{
		string text = (value ?? string.Empty).Trim();
		while (text.StartsWith(YaoPrefix, StringComparison.Ordinal) || text.StartsWith(DiPrefix, StringComparison.Ordinal))
		{
			text = text.Substring(2).Trim();
		}
		return text;
	}

	internal static string Compose(string prefix, string daoTu)
	{
		string core = ResolveCore(daoTu);
		return string.IsNullOrWhiteSpace(core) ? string.Empty : prefix + core;
	}
}
