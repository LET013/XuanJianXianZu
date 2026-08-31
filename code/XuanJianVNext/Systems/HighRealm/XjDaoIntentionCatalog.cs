using System;
using System.Collections.Generic;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 果位权柄仍以原有全称作为稳定存档键；本目录只为每一柄权柄补充道途专属“意向”。
/// 意向名称以既有原著神通/权柄语义、五德五现与已登记道途描述为依据，只承担玩家展示与易象语义，
/// 不把未明的高阶设定擅自补成新的硬规则。公告、照录与神通易象统一从这里取词。
/// 每组意向与 <see cref="XjGuoWeiAuthorityCatalog"/> 的六柄权柄按序一一对应。
/// </summary>
internal static class XjDaoIntentionCatalog
{
	private static readonly Dictionary<string, string[]> entries = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		["太阴"] = new[] { "月御", "统阴", "道藏", "避劫", "月华", "太素" },
		["少阴"] = new[] { "化羽", "凝冰", "含寒", "阴燥", "枢机", "蟾宫" },
		["厥阴"] = new[] { "生衍", "群交", "身修", "百邪", "玄牝", "青鸾" },
		["太阳"] = new[] { "日御", "五明", "显耀", "统阳", "极盛", "帝冕" },
		["少阳"] = new[] { "东极", "求变", "东君", "青阳", "残阳", "远骋" },
		["明阳"] = new[] { "生合", "纲常", "法统", "天光", "天门", "危机" },
		["玄雷"] = new[] { "雷霆", "靖平", "盛阳", "天鸣", "律演", "封坛" },
		["霄雷"] = new[] { "紫霆", "冬雷", "惊蛰", "轻雷", "斡紫", "周天" },
		["元雷"] = new[] { "元磁", "煞仪", "天罚", "九霄", "霄汉", "劫雷" },
		["兑金"] = new[] { "正金", "兑相", "消劫", "缺损", "折锋", "杀心" },
		["逍金"] = new[] { "蕴金", "逍遥", "归匮", "御金", "商锋", "敛芒" },
		["齐金"] = new[] { "收金", "齐满", "归元", "金戈", "律令", "军威" },
		["库金"] = new[] { "藏金", "法藏", "养精", "销洞", "百宝", "锈炁" },
		["庚金"] = new[] { "变金", "庚相", "殁变", "火炼", "镂坚", "墓棘" },
		["长庚"] = new[] { "剑宗", "意御", "寒照", "断法", "归元", "太白" },
		["角木"] = new[] { "藏生", "余阳", "重林", "逢春", "乙木", "青龙" },
		["正木"] = new[] { "正木", "甲乙", "方正", "专一", "察语", "长青" },
		["集木"] = new[] { "收木", "云集", "栖止", "祸延", "丹参", "定春" },
		["更木"] = new[] { "变木", "病春", "枯荣", "更迭", "常新", "新陈" },
		["保木"] = new[] { "蕴木", "长生", "不坏", "盘根", "森罗", "栋梁" },
		["坎水"] = new[] { "正水", "溪翁", "浩瀚", "中流", "云雪", "江逝" },
		["渌水"] = new[] { "变水", "清浊", "泉声", "夕雨", "癸藏", "洗劫" },
		["合水"] = new[] { "收水", "归流", "雾转", "汇海", "包容", "承平" },
		["府水"] = new[] { "蕴水", "寒雨", "深渊", "穷冬", "广浚", "养命" },
		["牝水"] = new[] { "藏生", "往生", "晨晦", "谿谷", "玄脏", "上善" },
		["离火"] = new[] { "正火", "罗网", "征伐", "离书", "九擭", "焚烬" },
		["灴火"] = new[] { "变火", "燔旧", "白樆", "布燥", "灴夏", "虚宫" },
		["并火"] = new[] { "收火", "焰乌", "欲生", "焚灭", "夺命", "命除" },
		["真火"] = new[] { "蕴火", "天兜", "雉离", "真身", "燎原", "治命" },
		["牡火"] = new[] { "藏火", "牡煞", "韬灾", "高陵", "灴龠", "光明" },
		["艮土"] = new[] { "正土", "赶岳", "源谷", "镇伏", "不动", "艮止" },
		["戊土"] = new[] { "蕴土", "天抚", "无漏", "心岩", "厚德", "中宫" },
		["归土"] = new[] { "收土", "落原", "归尘", "归藏", "藏形", "红尘" },
		["宝土"] = new[] { "藏土", "高垒", "藏纳", "膏泽", "归心", "敛宝" },
		["宣土"] = new[] { "变土", "归心", "神命", "训浚", "宣威", "配天" },
		["清炁"] = new[] { "玄风", "炁母", "清元", "益养", "浮云", "引池" },
		["紫炁"] = new[] { "紫金", "列篇", "绕山", "道兆", "坤辰", "养生" },
		["真炁"] = new[] { "修武", "抱石", "霞羽", "鹤衣", "长生", "蜕凡" },
		["邃炁"] = new[] { "玄焰", "妨天", "闇殃", "逆伦", "众苦", "乱理" },
		["寒炁"] = new[] { "阴寒", "松雪", "清听", "祢寒", "沆砀", "寒世" },
		["晞炁"] = new[] { "阳晞", "未阕", "代夜", "郁燠", "庆垂", "八辟" },
		["瑞炁"] = new[] { "感应", "功箓", "瑞云", "呈祥", "福星", "吉瑞" },
		["煞炁"] = new[] { "凶煞", "空劫", "刹海", "千身", "人屠", "戮天" },
		["华炁"] = new[] { "宝光", "冠旒", "众念", "仪仗", "显华", "琉履" },
		["谪炁"] = new[] { "受谪", "藏舟", "薄渊", "忘川", "惘世", "盼归" },
		["上仪"] = new[] { "脱俗", "明心", "缉熙", "观天", "无极", "射狩" },
		["下仪"] = new[] { "幽冥", "轮回", "黄泉", "忘川", "前尘", "阴敕" },
		["鸺葵"] = new[] { "宿命", "分形", "风魇", "夜翳", "寄命", "藏真" },
		["上巫"] = new[] { "巫命", "通幽", "帝变", "血祭", "绝察", "地祝" },
		["玉真"] = new[] { "玉德", "合命", "青崖", "真虚", "锦障", "承阴" },
		["衡祝"] = new[] { "祝命", "赤虎", "量厄", "血烬", "分灾", "定命" },
		["青宣"] = new[] { "宣土", "镇脉", "司土", "承天", "察地", "敷生" },
		["全丹"] = new[] { "全性", "铅华", "汞养", "候神", "金书", "归真" },
		["执孛"] = new[] { "主变", "形渡", "离绝", "断气", "蜕形", "移相" },
		["司天"] = new[] { "总序", "闻辰", "布象", "斗衡", "辰命", "浑仪" },
		["都卫"] = new[] { "都镇", "降魂", "东岳", "西界", "南水", "北庭" },
		["渊照"] = new[] { "留真", "沉月", "照真", "返景", "定真", "无波" },
		["虹霞"] = new[] { "落霞", "凝华", "虹霓", "朝霞", "绮霞", "含德" }
	};

	internal static IReadOnlyList<string> Get(string daoTu)
	{
		return !string.IsNullOrWhiteSpace(daoTu) && entries.TryGetValue(daoTu.Trim(), out string[] values)
			? values
			: Array.Empty<string>();
	}

	internal static string Resolve(string daoTu, string authority)
	{
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		string normalizedAuthority = NormalizeAuthority(authority);
		if (normalizedDaoTu.Length == 0 || normalizedAuthority.Length == 0) return string.Empty;

		if (string.Equals(normalizedDaoTu, "府水", StringComparison.Ordinal)
			&& (normalizedAuthority.Contains("浩瀚", StringComparison.Ordinal)
				|| normalizedAuthority.Contains("广浚", StringComparison.Ordinal)))
		{
			return "浩瀚";
		}

		IReadOnlyList<string> authorities = XjGuoWeiAuthorityCatalog.Get(normalizedDaoTu);
		IReadOnlyList<string> intentions = Get(normalizedDaoTu);
		int count = Math.Min(authorities.Count, intentions.Count);
		for (int i = 0; i < count; i++)
		{
			if (AuthorityEquals(authorities[i], normalizedAuthority)) return intentions[i];
		}

		return ExtractFallbackIntention(normalizedAuthority);
	}

	internal static string FormatAuthority(string daoTu, string authority)
	{
		string normalizedAuthority = NormalizeAuthority(authority);
		if (normalizedAuthority.Length == 0) return "【无名权柄】";
		string intention = Resolve(daoTu, normalizedAuthority);
		string displayAuthority = ResolveDisplayAuthorityName(daoTu, normalizedAuthority);
		if (intention.Length == 0 || displayAuthority.EndsWith("意向", StringComparison.Ordinal))
			return "【" + displayAuthority + "】";
		return "【" + intention + "意向·" + displayAuthority + "】";
	}

	// 谪炁旧档仍以原权柄全称作为稳定键，只在展示层对齐惘乾坤、盼天赐两门上位神通。
	private static string ResolveDisplayAuthorityName(string daoTu, string authority)
	{
		if (!string.Equals((daoTu ?? string.Empty).Trim(), "谪炁", StringComparison.Ordinal)) return authority;
		if (string.Equals(authority, "谪落人间", StringComparison.Ordinal)) return "惘乾坤";
		if (string.Equals(authority, "幽冥归途", StringComparison.Ordinal)) return "盼天赐";
		return authority;
	}

	internal static string FormatAuthoritySet(string preferredDaoTu, string rawAuthorities)
	{
		string[] values = Split(rawAuthorities);
		if (values.Length == 0) return string.Empty;
		List<string> result = new List<string>(values.Length);
		for (int i = 0; i < values.Length; i++)
		{
			string owner = preferredDaoTu;
			if (!TryResolveAuthorityIndex(owner, values[i], out _)
				&& TryResolveAuthorityOwner(values[i], out string resolvedOwner, out _)) owner = resolvedOwner;
			string formatted = FormatAuthority(owner, values[i]);
			if (!result.Contains(formatted)) result.Add(formatted);
		}
		return string.Join("、", result);
	}

	internal static string ResolveScopeDisplay(string daoTu, string rawScope)
	{
		string[] values = Split(rawScope);
		List<string> result = new List<string>(6);
		for (int i = 0; i < values.Length && result.Count < 6; i++)
		{
			string raw = (values[i] ?? string.Empty).Trim();
			if (raw.Length == 0 || string.Equals(raw, "诸象", StringComparison.Ordinal)
				|| string.Equals(raw, "未定", StringComparison.Ordinal)) continue;
			string owner = daoTu;
			if (!TryResolveAuthorityIndex(owner, raw, out _)
				&& TryResolveAuthorityOwner(raw, out string resolvedOwner, out _)) owner = resolvedOwner;
			string intention = Resolve(owner, raw);
			if (intention.Length > 0 && !string.Equals(intention, "诸象", StringComparison.Ordinal)
				&& !result.Contains(intention)) result.Add(intention);
		}

		if (result.Count == 0)
		{
			IReadOnlyList<string> defaults = Get(daoTu);
			for (int i = 0; i < defaults.Count && result.Count < 6; i++)
				if (!string.IsNullOrWhiteSpace(defaults[i]) && !result.Contains(defaults[i])) result.Add(defaults[i]);
		}
		return result.Count == 0 ? "诸象" : string.Join("、", result);
	}

	internal static bool TryResolveAuthorityOwner(string authority, out string daoTu, out int index)
	{
		daoTu = string.Empty;
		index = -1;
		string normalized = NormalizeAuthority(authority);
		if (normalized.Length == 0) return false;

		string foundDaoTu = string.Empty;
		int foundIndex = -1;
		foreach (string candidateDaoTu in XjGuoWeiAuthorityCatalog.GetAllDaoTus())
		{
			if (!TryResolveAuthorityIndex(candidateDaoTu, normalized, out int candidateIndex)) continue;
			if (foundIndex >= 0 && !string.Equals(foundDaoTu, candidateDaoTu, StringComparison.Ordinal)) return false;
			foundDaoTu = candidateDaoTu;
			foundIndex = candidateIndex;
		}

		if (foundIndex < 0 && (normalized.Contains("浩瀚", StringComparison.Ordinal)
			|| normalized.Contains("广浚", StringComparison.Ordinal)))
		{
			daoTu = "府水";
			index = 4;
			return true;
		}

		daoTu = foundDaoTu;
		index = foundIndex;
		return foundIndex >= 0;
	}

	internal static bool TryResolveAuthorityIndex(string daoTu, string authority, out int index)
	{
		index = -1;
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		string normalizedAuthority = NormalizeAuthority(authority);
		if (normalizedDaoTu.Length == 0 || normalizedAuthority.Length == 0) return false;
		IReadOnlyList<string> authorities = XjGuoWeiAuthorityCatalog.Get(normalizedDaoTu);
		for (int i = 0; i < authorities.Count; i++)
		{
			if (AuthorityEquals(authorities[i], normalizedAuthority))
			{
				index = i;
				return true;
			}
		}
		if (string.Equals(normalizedDaoTu, "府水", StringComparison.Ordinal)
			&& (normalizedAuthority.Contains("浩瀚", StringComparison.Ordinal)
				|| normalizedAuthority.Contains("广浚", StringComparison.Ordinal)))
		{
			index = 4;
			return true;
		}
		return false;
	}

	private static string[] Split(string text)
	{
		return (text ?? string.Empty).Split(new[] { '|', '、', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
	}

	private static bool AuthorityEquals(string left, string right)
	{
		string a = NormalizeAuthority(left);
		string b = NormalizeAuthority(right);
		return string.Equals(a, b, StringComparison.Ordinal)
			|| (a.Length > 0 && b.Contains(a, StringComparison.Ordinal))
			|| (b.Length > 0 && a.Contains(b, StringComparison.Ordinal));
	}

	private static string NormalizeAuthority(string authority)
	{
		string value = (authority ?? string.Empty).Trim().Trim('【', '】', '“', '”', '"');
		int separator = value.IndexOf('·');
		if (separator >= 0 && separator + 1 < value.Length && value.Substring(0, separator).EndsWith("意向", StringComparison.Ordinal))
			value = value.Substring(separator + 1).Trim();
		return XjGuoWeiAuthorityCatalog.NormalizeAuthorityNameAny(value);
	}

	private static string ExtractFallbackIntention(string authority)
	{
		string[] symbols =
		{
			"乾坤", "红尘", "紫金", "不动", "浩瀚", "云", "江", "玉", "泉", "露", "雨", "湖", "海", "河", "渊", "溪", "雪",
			"星", "月", "日", "雷", "火", "焰", "山", "岳", "土", "金", "剑", "风", "炁", "木", "林", "春", "冬", "阴", "阳",
			"命", "魂", "光", "霞", "符", "阵", "川", "泽", "门", "宫", "血", "劫", "律", "意", "心", "身"
		};
		for (int i = 0; i < symbols.Length; i++)
			if (authority.Contains(symbols[i], StringComparison.Ordinal)) return symbols[i];
		return authority.Length <= 2 ? authority : authority.Substring(0, 2);
	}
}
