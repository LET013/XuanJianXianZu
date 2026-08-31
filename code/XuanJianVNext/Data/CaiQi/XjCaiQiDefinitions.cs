using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.CaiQi;

internal static class XjCaiQiPlaceTypeIds
{
	public const string SanYin = "SanYin";
	public const string SanYang = "SanYang";
	public const string SanLei = "SanLei";
	public const string JinDe = "JinDe";
	public const string MuDe = "MuDe";
	public const string ShuiDe = "ShuiDe";
	public const string HuoDe = "HuoDe";
	public const string TuDe = "TuDe";
	public const string ShiErQi = "ShiErQi";
	public const string BingGu = "BingGu";
	public const string YuanZhao = "YuanZhao";
}

internal static class XjCaiQiBranchIds
{
	public const string TaiYin = "TaiYin";
	public const string ShaoYin = "ShaoYin";
	public const string JueYin = "JueYin";
	public const string TaiYang = "TaiYang";
	public const string ShaoYang = "ShaoYang";
	public const string MingYang = "MingYang";
	public const string XuanLei = "XuanLei";
	public const string XiaoLei = "XiaoLei";
	public const string YuanLei = "YuanLei";
	public const string DuiJin = "DuiJin";
	public const string XiaoJin = "XiaoJin";
	public const string QiJin = "QiJin";
	public const string KuJin = "KuJin";
	public const string GengJin = "GengJin";
	public const string JiaoMu = "JiaoMu";
	public const string ZhengMu = "ZhengMu";
	public const string JiMu = "JiMu";
	public const string GengMu = "GengMu";
	public const string BaoMu = "BaoMu";
	public const string KanShui = "KanShui";
	public const string LuShui = "LuShui";
	public const string HeShui = "HeShui";
	public const string FuShui = "FuShui";
	public const string PinShui = "PinShui";
	public const string LiHuo = "LiHuo";
	public const string HongHuo = "HongHuo";
	public const string BingHuo = "BingHuo";
	public const string ZhenHuo = "ZhenHuo";
	public const string MuHuo = "MuHuo";
	public const string GenTu = "GenTu";
	public const string WuTu = "WuTu";
	public const string GuiTu = "GuiTu";
	public const string BaoTu = "BaoTu";
	public const string XuanTu = "XuanTu";
	public const string HongXia = "HongXia";
	public const string XiaoKui = "XiaoKui";
	public const string ShangWu = "ShangWu";
	public const string YuZhen = "YuZhen";
	public const string HengZhu = "HengZhu";
	public const string QingXuan = "QingXuan";
	public const string QuanDan = "QuanDan";
	public const string ZhiBo = "ZhiBo";
	public const string SiTian = "SiTian";
	public const string DuWei = "DuWei";
	public const string QingQi = "QingQi";
	public const string ZiQi = "ZiQi";
	public const string ZhenQi = "ZhenQi";
	public const string SuiQi = "SuiQi";
	public const string HanQi = "HanQi";
	public const string XiQi = "XiQi";
	public const string RuiQi = "RuiQi";
	public const string ShaQi = "ShaQi";
	public const string HuaQi = "HuaQi";
	public const string ZheQi = "ZheQi";
	public const string ShangYi = "ShangYi";
	public const string XiaYi = "XiaYi";
	public const string YuanZhao = "YuanZhao";
}

internal static class XjCaiQiResultTypes
{
	public const string ZaQi = "ZaQi";
	public const string XianTianQi = "XianTianQi";
}

internal readonly struct XjCaiQiCatalogEntry
{
	internal readonly string PlaceTypeId;
	internal readonly string BranchId;
	internal readonly string DisplayName;
	internal readonly string OldTraitId;
	internal readonly string OldResourceId;

	internal XjCaiQiCatalogEntry(
		string placeTypeId,
		string branchId,
		string displayName,
		string oldTraitId,
		string oldResourceId)
	{
		PlaceTypeId = placeTypeId ?? string.Empty;
		BranchId = branchId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		OldTraitId = oldTraitId ?? string.Empty;
		OldResourceId = oldResourceId ?? string.Empty;
	}
}

internal static class XjCaiQiCatalog
{
	internal static readonly XjCaiQiCatalogEntry[] Entries =
	{
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.SanYin, XjCaiQiBranchIds.TaiYin, "太阴", "YinYang2", "taiyinzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.SanYin, XjCaiQiBranchIds.ShaoYin, "少阴", "YinYang4", "shaoyinzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.SanYin, XjCaiQiBranchIds.JueYin, "厥阴", "YinYang6", "jueyinzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.SanYang, XjCaiQiBranchIds.TaiYang, "太阳", "YinYang1", "taiyangzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.SanYang, XjCaiQiBranchIds.ShaoYang, "少阳", "YinYang3", "shaoyangzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.SanYang, XjCaiQiBranchIds.MingYang, "明阳", "YinYang5", "mingyangzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.SanLei, XjCaiQiBranchIds.XuanLei, "玄雷", "SanLei1", "xuanleizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.SanLei, XjCaiQiBranchIds.XiaoLei, "霄雷", "SanLei2", "xiaoleizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.SanLei, XjCaiQiBranchIds.YuanLei, "元雷", "SanLei3", "yuanleizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.JinDe, XjCaiQiBranchIds.DuiJin, "兑金", "JinDe4", "duijinzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.JinDe, XjCaiQiBranchIds.XiaoJin, "逍金", "JinDe5", "xiaojinzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.JinDe, XjCaiQiBranchIds.QiJin, "齐金", "JinDe3", "qijinzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.JinDe, XjCaiQiBranchIds.KuJin, "库金", "JinDe1", "kujizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.JinDe, XjCaiQiBranchIds.GengJin, "庚金", "JinDe2", "gengjinzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.MuDe, XjCaiQiBranchIds.JiaoMu, "角木", "MuDe5", "jiaomuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.MuDe, XjCaiQiBranchIds.ZhengMu, "正木", "MuDe2", "zhengmuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.MuDe, XjCaiQiBranchIds.JiMu, "集木", "MuDe1", "jimuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.MuDe, XjCaiQiBranchIds.GengMu, "更木", "MuDe4", "gengmuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.MuDe, XjCaiQiBranchIds.BaoMu, "保木", "MuDe3", "baomuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShuiDe, XjCaiQiBranchIds.KanShui, "坎水", "ShuiDe2", "kanshuizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShuiDe, XjCaiQiBranchIds.LuShui, "渌水", "ShuiDe1", "lushuizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShuiDe, XjCaiQiBranchIds.HeShui, "合水", "ShuiDe4", "heshuiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShuiDe, XjCaiQiBranchIds.FuShui, "府水", "ShuiDe3", "fushuizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShuiDe, XjCaiQiBranchIds.PinShui, "牝水", "ShuiDe5", "pinshuizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.HuoDe, XjCaiQiBranchIds.LiHuo, "离火", "HuoDe5", "lihuozhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.HuoDe, XjCaiQiBranchIds.HongHuo, "灴火", "HuoDe2", "honghuozhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.HuoDe, XjCaiQiBranchIds.BingHuo, "并火", "HuoDe1", "binghuozhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.HuoDe, XjCaiQiBranchIds.ZhenHuo, "真火", "HuoDe4", "zhenhuozhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.HuoDe, XjCaiQiBranchIds.MuHuo, "牡火", "HuoDe3", "muhuozhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.TuDe, XjCaiQiBranchIds.GenTu, "艮土", "TuDe1", "gentuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.TuDe, XjCaiQiBranchIds.WuTu, "戊土", "TuDe2", "wutuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.TuDe, XjCaiQiBranchIds.HongXia, "虹霞", "XjHongXiaDaoTong", "hongxiazhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.TuDe, XjCaiQiBranchIds.GuiTu, "归土", "TuDe4", "guituzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.TuDe, XjCaiQiBranchIds.BaoTu, "宝土", "TuDe5", "baotuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.TuDe, XjCaiQiBranchIds.XuanTu, "宣土", "TuDe3", "xuantuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.BingGu, XjCaiQiBranchIds.XiaoKui, "鸺葵", "XjXiaoKuiDaoTu", "xiaokuizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.BingGu, XjCaiQiBranchIds.ShangWu, "上巫", "XjShangWuDaoTu", "shangwuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.BingGu, XjCaiQiBranchIds.YuZhen, "玉真", "XjYuZhenDaoTu", "yuzhenzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.BingGu, XjCaiQiBranchIds.HengZhu, "衡祝", "XjHengZhuDaoTu", "hengzhuzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.BingGu, XjCaiQiBranchIds.QingXuan, "青宣", "XjQingXuanDaoTu", "qingxuanzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.BingGu, XjCaiQiBranchIds.QuanDan, "全丹", "XjQuanDanDaoTu", "quandanzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.BingGu, XjCaiQiBranchIds.ZhiBo, "执孛", "XjZhiBoDaoTu", "zhibozhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.BingGu, XjCaiQiBranchIds.SiTian, "司天", "XjSiTianDaoTu", "sitianzhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.BingGu, XjCaiQiBranchIds.DuWei, "都卫", "XjDuWeiDaoTu", "duweizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.QingQi, "清炁", "ShiErQi3", "qingqizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.ZiQi, "紫炁", "ShiErQi12", "ziqizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.ZhenQi, "真炁", "ShiErQi11", "zhenqizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.SuiQi, "邃炁", "ShiErQi7", "suiqizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.HanQi, "寒炁", "ShiErQi1", "hanqizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.XiQi, "晞炁", "ShiErQi8", "xiqizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.RuiQi, "瑞炁", "ShiErQi4", "ruiqiZhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.ShaQi, "煞炁", "ShiErQi5", "shaqizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.HuaQi, "华炁", "ShiErQi2", "huaqizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.ZheQi, "谪炁", "ShiErQi10", "zheqizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.ShangYi, "上仪", "ShiErQi6", "shangyizhiqi"),
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.ShiErQi, XjCaiQiBranchIds.XiaYi, "下仪", "ShiErQi9", "xiayizhiqi"),
		// 渊照为五百年空证后才存在的独立后世道途；采气地点只由水月照真洞天登记，不进入普通城市十类随机地点池。
		new XjCaiQiCatalogEntry(XjCaiQiPlaceTypeIds.YuanZhao, XjCaiQiBranchIds.YuanZhao, "渊照", "XjYuanZhaoDaoTong", "yuanzhaozhiqi")
	};

	private static readonly Dictionary<string, XjCaiQiCatalogEntry> EntryByPlaceAndBranch =
		BuildEntryByPlaceAndBranch();
	private static readonly Dictionary<string, XjCaiQiCatalogEntry> EntryByDisplayName =
		BuildEntryByDisplayName();
	private static readonly Dictionary<string, XjCaiQiCatalogEntry> EntryByBranchId =
		BuildEntryByBranchId();
	private static readonly Dictionary<string, string> DisplayNameByResourceId =
		BuildDisplayNameByResourceId();
	private static readonly Dictionary<string, string[]> BranchesByPlaceType =
		BuildBranchesByPlaceType();

	internal static bool IsBranchAllowedForPlaceType(string placeTypeId, string branchId)
	{
		return EntryByPlaceAndBranch.ContainsKey(ComposePlaceBranchKey(placeTypeId, branchId));
	}

	internal static bool TryGetBranchesForPlaceType(string placeTypeId, out IReadOnlyList<string> branchIds)
	{
		if (BranchesByPlaceType.TryGetValue(placeTypeId ?? string.Empty, out string[] result))
		{
			branchIds = result;
			return result.Length > 0;
		}

		branchIds = Array.Empty<string>();
		return false;
	}

	internal static string GetBranchAt(string placeTypeId, int index)
	{
		if (!TryGetBranchesForPlaceType(placeTypeId, out IReadOnlyList<string> branchIds) || branchIds.Count == 0)
		{
			return string.Empty;
		}

		int safeIndex = index % branchIds.Count;
		if (safeIndex < 0)
		{
			safeIndex += branchIds.Count;
		}

		return branchIds[safeIndex] ?? string.Empty;
	}

	internal static bool TryGetEntry(string placeTypeId, string branchId, out XjCaiQiCatalogEntry entry)
	{
		return EntryByPlaceAndBranch.TryGetValue(
			ComposePlaceBranchKey(placeTypeId, branchId),
			out entry);
	}

	internal static bool TryGetEntryByDisplayName(string daoTu, out XjCaiQiCatalogEntry entry)
	{
		string normalized = StripQiSuffix((daoTu ?? string.Empty).Trim());
		return EntryByDisplayName.TryGetValue(normalized, out entry);
	}

	/// <summary>
	/// 通过道途显示名称查找灵气资源ID（金丹赐福使用）
	/// </summary>
	internal static bool TryGetOldResourceIdByDaoTuName(string daoTuName, out string oldResourceId)
	{
		oldResourceId = string.Empty;
		if (string.IsNullOrWhiteSpace(daoTuName))
		{
			return false;
		}

		string trimmed = StripQiSuffix(daoTuName.Trim());
		if (string.Equals(trimmed, "杂气", StringComparison.Ordinal)
			|| string.Equals(trimmed, XjCaiQiResultTypes.ZaQi, StringComparison.OrdinalIgnoreCase))
		{
			oldResourceId = "zaqi";
			return true;
		}

		if (EntryByDisplayName.TryGetValue(trimmed, out XjCaiQiCatalogEntry entry)
			&& !string.IsNullOrWhiteSpace(entry.OldResourceId))
		{
			oldResourceId = entry.OldResourceId;
			return true;
		}

		return false;
	}

	internal static bool TryGetOldResourceIdByBranchId(string branchId, out string oldResourceId)
	{
		oldResourceId = string.Empty;
		if (string.IsNullOrWhiteSpace(branchId))
		{
			return false;
		}

		if (EntryByBranchId.TryGetValue(branchId.Trim(), out XjCaiQiCatalogEntry entry)
			&& !string.IsNullOrWhiteSpace(entry.OldResourceId))
		{
			oldResourceId = entry.OldResourceId;
			return true;
		}

		return false;
	}

	private static string StripQiSuffix(string displayName)
	{
		string value = (displayName ?? string.Empty).Trim();
		return value.EndsWith("之气", StringComparison.Ordinal)
			? value.Substring(0, value.Length - 2)
			: value;
	}

	internal static string EnsureQiSuffix(string displayName)
	{
		string value = (displayName ?? string.Empty).Trim();
		if (value.Length == 0 || string.Equals(value, "杂气", StringComparison.Ordinal) || value.EndsWith("之气", StringComparison.Ordinal)) return value;
		return value + "之气";
	}

	internal static bool TryGetDisplayNameByResourceId(string resourceId, out string displayName)
	{
		displayName = resourceId ?? string.Empty;
		if (string.IsNullOrWhiteSpace(resourceId))
		{
			return false;
		}

		if (string.Equals(resourceId, "zaqi", StringComparison.Ordinal)
			|| string.Equals(resourceId, XjCaiQiResultTypes.ZaQi, StringComparison.Ordinal))
		{
			displayName = "杂气";
			return true;
		}

		if (DisplayNameByResourceId.TryGetValue(resourceId, out string knownDisplayName))
		{
			displayName = knownDisplayName;
			return true;
		}

		return false;
	}

	private static string ComposePlaceBranchKey(string placeTypeId, string branchId)
	{
		return (placeTypeId ?? string.Empty) + "\u001f" + (branchId ?? string.Empty);
	}

	private static Dictionary<string, XjCaiQiCatalogEntry> BuildEntryByPlaceAndBranch()
	{
		Dictionary<string, XjCaiQiCatalogEntry> result =
			new Dictionary<string, XjCaiQiCatalogEntry>(Entries.Length, StringComparer.Ordinal);
		for (int i = 0; i < Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = Entries[i];
			result[ComposePlaceBranchKey(entry.PlaceTypeId, entry.BranchId)] = entry;
		}
		return result;
	}

	private static Dictionary<string, XjCaiQiCatalogEntry> BuildEntryByDisplayName()
	{
		Dictionary<string, XjCaiQiCatalogEntry> result =
			new Dictionary<string, XjCaiQiCatalogEntry>(Entries.Length, StringComparer.Ordinal);
		for (int i = 0; i < Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = Entries[i];
			if (!string.IsNullOrWhiteSpace(entry.DisplayName)) result[entry.DisplayName] = entry;
		}
		return result;
	}

	private static Dictionary<string, XjCaiQiCatalogEntry> BuildEntryByBranchId()
	{
		Dictionary<string, XjCaiQiCatalogEntry> result =
			new Dictionary<string, XjCaiQiCatalogEntry>(Entries.Length, StringComparer.Ordinal);
		for (int i = 0; i < Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = Entries[i];
			if (!string.IsNullOrWhiteSpace(entry.BranchId)) result[entry.BranchId] = entry;
		}
		return result;
	}

	private static Dictionary<string, string> BuildDisplayNameByResourceId()
	{
		Dictionary<string, string> result =
			new Dictionary<string, string>(Entries.Length, StringComparer.Ordinal);
		for (int i = 0; i < Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = Entries[i];
			if (!string.IsNullOrWhiteSpace(entry.OldResourceId)
				&& !string.IsNullOrWhiteSpace(entry.DisplayName))
			{
				result[entry.OldResourceId] = EnsureQiSuffix(entry.DisplayName);
			}
		}
		return result;
	}

	private static Dictionary<string, string[]> BuildBranchesByPlaceType()
	{
		Dictionary<string, List<string>> builders =
			new Dictionary<string, List<string>>(StringComparer.Ordinal);
		for (int i = 0; i < Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = Entries[i];
			if (!builders.TryGetValue(entry.PlaceTypeId, out List<string> branches))
			{
				branches = new List<string>();
				builders[entry.PlaceTypeId] = branches;
			}
			branches.Add(entry.BranchId);
		}

		Dictionary<string, string[]> result =
			new Dictionary<string, string[]>(builders.Count, StringComparer.Ordinal);
		foreach (KeyValuePair<string, List<string>> pair in builders)
		{
			result[pair.Key] = pair.Value.ToArray();
		}
		return result;
	}
}

internal static class XjCaiQiNameLibrary
{
	internal static readonly IReadOnlyDictionary<string, string[]> NamesByPlaceTypeId =
		new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			[XjCaiQiPlaceTypeIds.SanYin] = new[] { "月华渊", "寒魄池", "太阴潭", "幽月谷", "玄阴涧" },
			[XjCaiQiPlaceTypeIds.SanYang] = new[] { "旭日峰", "扶桑崖", "阳和谷", "金乌岭", "朝霞台" },
			[XjCaiQiPlaceTypeIds.SanLei] = new[] { "雷音谷", "紫电崖", "霹雳渊", "雷霆顶", "惊蛰坡" },
			[XjCaiQiPlaceTypeIds.JinDe] = new[] { "金魄岭", "太白峰", "金铁峡", "藏金山", "锋刃原" },
			[XjCaiQiPlaceTypeIds.MuDe] = new[] { "青木泽", "万木谷", "长春林", "翠微涧", "扶苏岗" },
			[XjCaiQiPlaceTypeIds.ShuiDe] = new[] { "长云溪", "寒潭涧", "流波浦", "清川渡", "九曲湾" },
			[XjCaiQiPlaceTypeIds.HuoDe] = new[] { "炎阳窟", "赤焰谷", "熔岩洞", "焦热岭", "朱雀台" },
			[XjCaiQiPlaceTypeIds.TuDe] = new[] { "承天台", "厚土原", "黄天垒", "坤舆丘", "中岳台" },
			[XjCaiQiPlaceTypeIds.ShiErQi] = new[] { "璇枢崖", "灵炁峰", "万气台", "混元顶", "玄穹岭" },
			[XjCaiQiPlaceTypeIds.BingGu] = new[]
			{
				"太古遗坛", "玄祀幽谷", "上古真墟", "残碑旧庭", "古仪秘境", "遗法天台",
				"断代玄壤", "先民祭丘", "古道藏渊", "并古石府", "玄祝遗谷", "旧世方台"
			},
			[XjCaiQiPlaceTypeIds.YuanZhao] = new[]
			{
				"水月照真渊", "沉月鉴潭", "返景玄池", "无波真渊", "涵真月潭"
			}
		};

	internal static string GetNameAt(string placeTypeId, int index)
	{
		if (!NamesByPlaceTypeId.TryGetValue(placeTypeId, out string[] names) || names.Length == 0)
		{
			return string.Empty;
		}

		int safeIndex = index % names.Length;
		if (safeIndex < 0)
		{
			safeIndex += names.Length;
		}

		return names[safeIndex] ?? string.Empty;
	}
}
