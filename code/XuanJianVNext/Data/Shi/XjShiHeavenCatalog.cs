using System;
using System.Globalization;

namespace XuanJianVNext.Data.Shi;

/// <summary>
/// 北世尊三十二应身及其六十九金地碎片的稳定目录。
/// 三十二天分无量、无边、无央、无等四类，每类八天；其中五天碎为三地，
/// 其余二十七天碎为二地，共六十九金地。旃檀林固定吸纳三十八块碎片，
/// 但“并入释土”不等于无人持有：今释摩诃或法相仍可在旃檀林内掌握其中金地，
/// 与其他法相共同持有这座无上土。
/// </summary>
internal static class XjShiHeavenCatalog
{
	internal const int TotalHeavens = 32;
	internal const int TotalFragments = 69;
	internal const int ZhantanlinFragmentCount = 38;
	internal const string FragmentIdPrefix = "shi:domain:north_fragment:";

	internal const string WuLiang = "wuliang";
	internal const string WuBian = "wubian";
	internal const string WuYang = "wuyang";
	internal const string WuDeng = "wudeng";

	private static readonly int[] TripleFragmentHeavenIndexes = { 0, 5, 10, 15, 24 };
	// 为避免把旃檀林误写成“完整吞下十七重天”，采用碎片级稳定分布：
	// 每重天至少一块碎片在旃檀林内，再额外吸纳六重天的第二块碎片，共三十八块。
	private static readonly int[] ZhantanlinSecondFragmentHeavenIndexes = { 0, 5, 10, 15, 20, 25 };

	// 名称采用佛教经论中常见的天界、净土、法会与觉悟名相重新组织，
	// 不再使用“无量第一天”这类程序占位名。
	private static readonly string[] HeavenDisplayNames =
	{
		"无量光天", "无量寿天", "无量净天", "无量慧天",
		"无量愿天", "无量行天", "无量德天", "无量庄严天",
		"无边空天", "无边识天", "无边愿海天", "无边法界天",
		"无边光明天", "无边净土天", "无边宝藏天", "无边觉海天",
		"无央香积天", "无央莲华天", "无央宝幢天", "无央法云天",
		"无央梵音天", "无央月轮天", "无央日轮天", "无央妙喜天",
		"无等金刚天", "无等菩提天", "无等正觉天", "无等寂光天",
		"无等慈悲天", "无等智慧天", "无等法身天", "无等涅槃天"
	};

	private static readonly string[] HeavenMeanings =
	{
		"光明遍照，摄破幽冥", "寿量绵延，护持真灵", "清净无垢，诸染不生", "慧照诸法，辨明因缘",
		"愿海无尽，摄引群生", "行持不息，精进无退", "福慧同聚，众德成藏", "宝相庄严，万行交映",
		"虚空无际，诸相不碍", "识海澄明，万念归真", "愿力如海，周遍十方", "法界圆融，事事无碍",
		"明照无边，诸暗悉除", "清泰寂然，烦恼不染", "法藏无尽，摄持诸门", "觉海渊深，照见本性",
		"妙香普熏，闻者生善", "莲华含藏，出淤无染", "宝幢高建，降伏诸魔", "法云遍覆，甘露普施",
		"梵音远彻，闻者解脱", "月华清凉，摄养神魂", "日轮赫奕，破除痴暗", "妙喜充满，诸苦暂息",
		"金刚不坏，摧破邪障", "菩提觉满，正道现前", "等正觉成，诸法朗然", "常寂光明，动静一如",
		"慈航普渡，不舍众生", "慧炬高悬，照破无明", "法身周遍，不来不去", "寂灭为乐，诸行归真"
	};

	private static readonly string[] FragmentDisplayNames =
	{
		"遍照金地", "明王金地", "宝焰金地",
		"安养金地", "长寿金地",
		"无垢金地", "清凉金地",
		"般若金地", "慧灯金地",
		"誓海金地", "愿轮金地",
		"精进金地", "行愿金地", "道场金地",
		"福聚金地", "德藏金地",
		"宝严金地", "华藏金地",
		"虚空金地", "无际金地",
		"识海金地", "明心金地",
		"普贤金地", "愿海金地", "摄生金地",
		"法界金地", "圆融金地",
		"光音金地", "普照金地",
		"净居金地", "清泰金地",
		"法藏金地", "宝积金地",
		"觉海金地", "灵山金地", "见性金地",
		"香积金地", "妙香金地",
		"莲华藏金地", "宝莲金地",
		"宝幢金地", "胜幡金地",
		"法云金地", "甘露金地",
		"梵音金地", "雷音金地",
		"月轮金地", "清辉金地",
		"日轮金地", "曜明金地",
		"妙喜金地", "欢喜金地",
		"金刚金地", "不坏金地", "降魔金地",
		"菩提金地", "觉树金地",
		"正觉金地", "等觉金地",
		"寂光金地", "常寂金地",
		"慈航金地", "悲愿金地",
		"智海金地", "慧炬金地",
		"法身金地", "毗卢金地",
		"涅槃金地", "寂灭金地"
	};

	internal static string BuildFragmentId(int fragmentIndex)
	{
		return fragmentIndex >= 0 && fragmentIndex < TotalFragments
			? FragmentIdPrefix + fragmentIndex.ToString("D2", CultureInfo.InvariantCulture)
			: string.Empty;
	}

	internal static bool TryParseFragmentId(string domainId, out int fragmentIndex)
	{
		fragmentIndex = -1;
		if (string.IsNullOrWhiteSpace(domainId)) return false;
		string id = domainId.Trim();
		if (!id.StartsWith(FragmentIdPrefix, StringComparison.Ordinal)) return false;
		return int.TryParse(id.Substring(FragmentIdPrefix.Length), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out fragmentIndex)
			&& fragmentIndex >= 0 && fragmentIndex < TotalFragments;
	}

	internal static int GetFragmentCountForHeaven(int heavenIndex)
	{
		if (heavenIndex < 0 || heavenIndex >= TotalHeavens) return 0;
		for (int i = 0; i < TripleFragmentHeavenIndexes.Length; i++)
		{
			if (TripleFragmentHeavenIndexes[i] == heavenIndex) return 3;
		}
		return 2;
	}

	internal static int GetFirstFragmentIndex(int heavenIndex)
	{
		if (heavenIndex < 0 || heavenIndex >= TotalHeavens) return -1;
		int cursor = 0;
		for (int i = 0; i < heavenIndex; i++) cursor += GetFragmentCountForHeaven(i);
		return cursor;
	}

	internal static int GetHeavenIndexForFragment(int fragmentIndex)
	{
		if (fragmentIndex < 0 || fragmentIndex >= TotalFragments) return -1;
		int cursor = 0;
		for (int heaven = 0; heaven < TotalHeavens; heaven++)
		{
			int count = GetFragmentCountForHeaven(heaven);
			if (fragmentIndex < cursor + count) return heaven;
			cursor += count;
		}
		return -1;
	}

	internal static int GetFragmentOrdinal(int fragmentIndex)
	{
		int heaven = GetHeavenIndexForFragment(fragmentIndex);
		int first = GetFirstFragmentIndex(heaven);
		return first < 0 ? 0 : fragmentIndex - first + 1;
	}

	internal static bool IsZhantanlinFragment(int fragmentIndex)
	{
		int heavenIndex = GetHeavenIndexForFragment(fragmentIndex);
		int ordinal = GetFragmentOrdinal(fragmentIndex);
		if (heavenIndex < 0 || ordinal <= 0) return false;
		if (ordinal == 1) return true;
		if (ordinal != 2) return false;
		for (int i = 0; i < ZhantanlinSecondFragmentHeavenIndexes.Length; i++)
		{
			if (ZhantanlinSecondFragmentHeavenIndexes[i] == heavenIndex) return true;
		}
		return false;
	}

	internal static string GetHeavenId(int heavenIndex)
	{
		return heavenIndex >= 0 && heavenIndex < TotalHeavens
			? "north_heaven_" + heavenIndex.ToString("D2", CultureInfo.InvariantCulture)
			: string.Empty;
	}

	internal static bool TryParseHeavenId(string heavenId, out int heavenIndex)
	{
		heavenIndex = -1;
		const string prefix = "north_heaven_";
		if (string.IsNullOrWhiteSpace(heavenId) || !heavenId.StartsWith(prefix, StringComparison.Ordinal))
			return false;
		return int.TryParse(heavenId.Substring(prefix.Length), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out heavenIndex)
			&& heavenIndex >= 0 && heavenIndex < TotalHeavens;
	}

	internal static string GetCategoryId(int heavenIndex)
	{
		if (heavenIndex < 0 || heavenIndex >= TotalHeavens) return string.Empty;
		if (heavenIndex < 8) return WuLiang;
		if (heavenIndex < 16) return WuBian;
		if (heavenIndex < 24) return WuYang;
		return WuDeng;
	}

	internal static string GetCategoryDisplay(string categoryId)
	{
		if (string.Equals(categoryId, WuLiang, StringComparison.Ordinal)) return "无量";
		if (string.Equals(categoryId, WuBian, StringComparison.Ordinal)) return "无边";
		if (string.Equals(categoryId, WuYang, StringComparison.Ordinal)) return "无央";
		if (string.Equals(categoryId, WuDeng, StringComparison.Ordinal)) return "无等";
		return "应身未定";
	}

	internal static string GetCategoryMeaning(string categoryId)
	{
		if (string.Equals(categoryId, WuLiang, StringComparison.Ordinal)) return "量不可穷";
		if (string.Equals(categoryId, WuBian, StringComparison.Ordinal)) return "边际不可得";
		if (string.Equals(categoryId, WuYang, StringComparison.Ordinal)) return "应化无央";
		if (string.Equals(categoryId, WuDeng, StringComparison.Ordinal)) return "尊胜无等";
		return "应身意向未定";
	}

	internal static string GetHeavenDisplayName(int heavenIndex)
	{
		return heavenIndex >= 0 && heavenIndex < HeavenDisplayNames.Length
			? HeavenDisplayNames[heavenIndex]
			: "三十二天未定";
	}

	internal static string GetHeavenMeaning(int heavenIndex)
	{
		return heavenIndex >= 0 && heavenIndex < HeavenMeanings.Length
			? HeavenMeanings[heavenIndex]
			: "应身法意未定";
	}

	internal static string GetFragmentDisplayName(int fragmentIndex)
	{
		return fragmentIndex >= 0 && fragmentIndex < FragmentDisplayNames.Length
			? FragmentDisplayNames[fragmentIndex]
			: "无名金地";
	}
}
