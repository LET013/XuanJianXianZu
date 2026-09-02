using System;

using XuanJianVNext.Data.Cultivation;

namespace XuanJianVNext.Data.GongFa;

internal static class XjGongFaNameLibrary
{
	private static readonly string[] SanYangSingleCharLib =
	{
		"阳", "曜", "羲", "炅", "煜", "赫", "九", "昉", "伏", "烜", "耀", "辰", "光", "相", "杲", "尧", "空", "然", "昊", "华", "生", "化", "灵", "威", "晖", "景", "日", "明"
	};

	private static readonly string[] SanYinSingleCharLib =
	{
		"月", "蟾", "桂", "琼", "冰", "寒", "霜", "素", "辰", "魄", "璇", "华", "净", "虚", "灵", "瑶", "影", "幽", "玄", "烛", "涟", "渊", "微", "湖", "夜", "珠", "辉", "荧", "阴"
	};

	private static readonly string[] YuanZhaoSingleCharLib =
	{
		"渊", "照", "月", "鉴", "影", "潜", "返", "涵", "真", "寂", "泓", "澜"
	};

	// 虹霞不并入戊土词池：功法名严格从长霞雾、蝃蝀东、正漱朝、余成绮、混含德五门取字，
	// 不再混入“虹桥、云霞”等泛霞光意象。
	private static readonly string[] HongXiaSingleCharLib =
	{
		"长", "霞", "雾", "蝃", "蝀", "东", "正", "漱", "朝", "余", "成", "绮", "混", "含", "德"
	};

	private static readonly string[] JinDeSingleCharLib =
	{
		"金", "锋", "钧", "鉴", "钺", "铮", "锐", "庚", "白", "肃", "斩", "成", "铸", "鸿", "绝"
	};

	private static readonly string[] MuDeSingleCharLib =
	{
		"木", "桂", "英", "桐", "楠", "榆", "荣", "林", "华", "花", "九", "森"
	};

	private static readonly string[] ShuiDeSingleCharLib =
	{
		"水", "霜", "霖", "汀", "光", "深", "溟", "渊", "涟", "浩", "瀚", "汐", "沧", "澄", "泉", "流", "壬", "癸"
	};

	private static readonly string[] HuoDeSingleCharLib =
	{
		"火", "煜", "翾", "炅", "焕", "烜", "丹", "离", "炽", "焱", "耀", "赫", "曜"
	};

	private static readonly string[] TuDeSingleCharLib =
	{
		"土", "岚", "峄", "岱", "壤", "尘", "坤", "镇", "岳", "垣", "岩", "陵"
	};

	private static readonly string[] SuDeSingleCharLib =
	{
		"素", "宣", "青", "律", "清", "玄", "真", "衡", "简", "正", "仪", "章"
	};

	private static readonly string[] XiaoKuiSingleCharLib = { "鸺", "葵", "枭", "魇", "鬼", "阴", "翳", "冥", "羽", "玄" };
	private static readonly string[] ShangWuSingleCharLib = { "巫", "槐", "荫", "帝", "血", "隐", "祝", "幽", "地", "玄" };
	private static readonly string[] YuZhenSingleCharLib = { "玉", "真", "崖", "锦", "盘", "庭", "白", "青", "合", "问" };
	private static readonly string[] HengZhuSingleCharLib = { "衡", "祝", "祭", "火", "殿", "阳", "灾", "烬", "煌", "赤" };
	private static readonly string[] QuanDanSingleCharLib = { "全", "丹", "铅", "华", "养", "宜", "候", "神", "金", "序" };
	private static readonly string[] ZhiBoSingleCharLib = { "执", "孛", "形", "渡", "阡", "相", "离", "绝", "倏", "动" };
	private static readonly string[] SiTianSingleCharLib = { "司", "天", "听", "曜", "辰", "神", "布", "序", "斗", "衡", "玄" };
	private static readonly string[] DuWeiSingleCharLib = { "都", "卫", "降", "魂", "闻", "东", "利", "山", "西", "璟", "南", "悯", "水", "北", "漠", "庭" };

	// 十二炁是独立道途。名称库按分支拆分，不能再把“下仪”等其他分支词根混入寒炁功法。
	private static readonly string[] QingQiSingleCharLib = { "清", "澄", "虚", "泠", "玄", "霁", "微", "渟" };
	private static readonly string[] ZiQiSingleCharLib = { "紫", "宸", "霄", "绛", "曜", "霞", "璇", "辉" };
	private static readonly string[] ZhenQiSingleCharLib = { "真", "元", "纯", "太", "湛", "冲", "凝", "一" };
	private static readonly string[] SuiQiSingleCharLib = { "邃", "幽", "渊", "冥", "玄", "寂", "深", "潜" };
	private static readonly string[] HanQiSingleCharLib = { "寒", "霜", "凛", "冰", "雪", "冽", "凝", "沆" };
	private static readonly string[] XiQiSingleCharLib = { "晞", "曦", "晨", "昕", "晓", "光", "昭", "明" };
	private static readonly string[] RuiQiSingleCharLib = { "瑞", "嘉", "祥", "麟", "玉", "华", "灵", "庆" };
	private static readonly string[] ShaQiSingleCharLib = { "煞", "戾", "厉", "刑", "肃", "断", "劫", "峻" };
	private static readonly string[] HuaQiSingleCharLib = { "华", "章", "文", "采", "璧", "辉", "绮", "昭" };
	private static readonly string[] ZheQiSingleCharLib = { "谪", "尘", "孤", "客", "天", "外", "玄", "寥" };
	private static readonly string[] ShangYiSingleCharLib = { "上", "仪", "天", "衡", "清", "升", "玄", "阙" };
	private static readonly string[] XiaYiSingleCharLib = { "下", "仪", "幽", "冥", "黄", "泉", "忘", "川" };

	private static readonly string[] SanLeiSingleCharLib =
	{
		"雷", "霆", "霄", "震", "掣", "耀", "鸿", "殛", "劫", "威", "罡", "镇", "微"
	};

	private static readonly string[] SanYangDoubleCharLib =
	{
		"东皇", "扶桑", "纯阳", "九昉", "羲和", "丹天", "普照", "司昇", "焚邪", "真阳", "赫灵", "耀威", "熔金", "曜明", "长晖", "景焰", "纯晟", "熙泰", "炎光", "无垢", "日昇", "明元", "明华", "东君", "曜灵", "金乌", "昊景", "日轮", "重明", "扶光", "阳燧", "朱明"
	};

	private static readonly string[] SanYinDoubleCharLib =
	{
		"广寒", "幽都", "九幽", "悬月", "沉晖", "孤蟾", "忘川", "无夜", "玄月", "悬冰", "寒坤", "滋阴",
		"湖月", "夜光", "太素", "月府", "素魄", "玄珠", "清辉", "幽荧", "冰轮", "桂魄", "蟾宫", "望舒"
	};

	private static readonly string[] YuanZhaoDoubleCharLib =
	{
		"渊照", "水月", "照真", "沉月", "潜坎", "返景", "涵真", "无波", "玄鉴", "月渊", "影真", "静鉴"
	};

	private static readonly string[] HongXiaDoubleCharLib =
	{
		"长霞雾", "蝃蝀东", "正漱朝", "余成绮", "混含德",
		"长霞", "霞雾", "蝃蝀", "蝀东", "正漱", "漱朝", "余成", "成绮", "混含", "含德"
	};

	private static readonly string[] JinDeDoubleCharLib =
	{
		"金一", "西永", "西极", "太白", "玄兔", "庚辛", "白晟", "肃英", "绝劫", "斩生", "蓐收", "锋神", "破虚", "神锋", "求真", "金石", "锐金", "白帝", "金庭", "兑泽", "金阙", "白藏", "商锋", "玄鉴"
	};

	private static readonly string[] MuDeDoubleCharLib =
	{
		"长养", "东极", "苍灵", "青华", "句芒", "震巽", "凝青", "司荣", "饮妙", "建木", "返虚", "枯荣", "救苦", "万林", "长麓", "青帝", "东华", "青阳", "春元", "扶桑", "荣枯", "苍梧"
	};

	private static readonly string[] ShuiDeDoubleCharLib =
	{
		"四海", "江河", "大陵", "沧渊", "天一", "壬癸", "寒晟", "净涟", "司泉", "浩微", "湍流", "上青", "溟波", "玄府", "沧流", "重泽", "天河", "浩瀚", "北溟", "归流", "寒泉", "水府"
	};

	private static readonly string[] HuoDeDoubleCharLib =
	{
		"南离", "炎天", "丹成", "朱景", "祝融", "破幻", "烛明", "焕赤", "焚虚", "司火", "赤曜", "景明", "朱焰", "炎阳", "天离", "重光", "火明", "赤帝", "离宫", "丹景", "阳燧", "焕赫", "朱明"
	};

	private static readonly string[] TuDeDoubleCharLib =
	{
		"中黄", "坤厚", "承天", "镇河", "戊己", "后土", "载物", "固玄", "尘渊", "钧天", "司坤", "厚载", "赶山", "熔山", "黄庭", "中宫", "镇岳", "玄垣", "坤舆", "地纪"
	};

	private static readonly string[] SuDeDoubleCharLib =
	{
		"青宣", "素德", "宣真", "青律", "素霞", "玄章", "清衡", "太素", "正仪", "宣玄", "青简", "素元"
	};

	private static readonly string[] XiaoKuiDoubleCharLib = { "鸺葵", "枭狸", "魇鬼", "鬼阴", "黑羽", "幽翳", "玄魇", "冥葵" };
	private static readonly string[] ShangWuDoubleCharLib = { "上巫", "槐荫", "帝王", "饮血", "地巫", "巫祝", "隐身", "幽祝" };
	private static readonly string[] YuZhenDoubleCharLib = { "玉真", "青玉", "道合", "问道", "白玉", "玉庭", "玉盘", "真玉" };
	private static readonly string[] HengZhuDoubleCharLib = { "衡祝", "殿阳", "斛量", "满烬", "祭火", "祝火", "赤祭", "灾火" };
	private static readonly string[] QuanDanDoubleCharLib = { "全丹", "混铅", "制养", "侯神", "金书", "丹性", "全生", "养真" };
	private static readonly string[] ZhiBoDoubleCharLib = { "执孛", "形渡", "相离", "倏动", "断气", "分形", "离绝", "孛变" };
	private static readonly string[] SiTianDoubleCharLib = { "司天", "听曜", "曜辰", "神布", "天序", "斗衡", "南衡", "司辰" };
	private static readonly string[] DuWeiDoubleCharLib = { "都卫", "降魂", "东利", "西璟", "南悯", "北漠", "方镇", "卫庭" };

	private static readonly string[] QingQiDoubleCharLib = { "清虚", "澄明", "泠然", "玄清", "霁野", "渟云", "清微", "澄寂" };
	private static readonly string[] ZiQiDoubleCharLib = { "紫宸", "绛霄", "紫曜", "玄霞", "璇霄", "宸光", "紫庭", "霞明" };
	private static readonly string[] ZhenQiDoubleCharLib = { "真元", "纯一", "太真", "湛然", "玄凝", "冲元", "真一", "元初" };
	private static readonly string[] SuiQiDoubleCharLib = { "邃冥", "幽渊", "玄寂", "深潜", "冥府", "渊玄", "邃古", "幽玄" };
	private static readonly string[] HanQiDoubleCharLib = { "寒世", "入清", "松上", "祢水", "沆砀", "凛冬", "玄冰", "凝霜" };
	private static readonly string[] XiQiDoubleCharLib = { "晞明", "晨光", "昕晓", "昭晨", "晞曜", "曦和", "晓霁", "晨晖" };
	private static readonly string[] RuiQiDoubleCharLib = { "瑞应", "嘉祥", "玉麟", "灵庆", "瑞华", "嘉玉", "祥光", "庆云" };
	private static readonly string[] ShaQiDoubleCharLib = { "煞轮", "肃刑", "厉劫", "峻断", "戾天", "煞威", "刑煞", "劫煞" };
	private static readonly string[] HuaQiDoubleCharLib = { "华藏", "文璧", "昭采", "绮章", "华辉", "璧光", "华章", "采章" };
	private static readonly string[] ZheQiDoubleCharLib = { "谪仙", "孤天", "尘外", "玄客", "天寥", "谪尘", "孤玄", "尘寰" };
	private static readonly string[] ShangYiDoubleCharLib = { "上仪", "天衡", "清升", "玄阙", "上清", "仪天", "天仪", "上玄" };
	private static readonly string[] XiaYiDoubleCharLib = { "下仪", "幽冥", "黄泉", "忘川", "轮回", "阴司", "幽都", "冥河" };

	private static readonly string[] SanLeiDoubleCharLib =
	{
		"玄兔", "神雷", "九霄", "玉枢", "劫化", "震霆", "冲霄", "诛雷", "司霆", "辰神", "掣虚", "荡魔", "紫微", "威灵", "玄雷", "霄雷", "元雷", "天鼓", "云霆", "雷府", "霆司", "雷泽"
	};

	private static readonly string[] CommonDoubleCharLib =
	{
		"太青", "太上", "太昊", "太元", "玉宸", "洞真", "履化", "无极", "高上", "妙有", "玄钧", "泰和", "玄元",
		"清微", "抱一", "含真", "洞玄", "玉清", "太初", "太易", "太始", "元景", "道枢", "冲和", "真一", "太华", "灵枢"
	};

	private static readonly string[] GongFaSuffixCharLib =
	{
		"诀", "经", "法", "真解", "篇", "录", "真经", "妙诀", "秘典", "道书", "玄章", "宝录"
	};

	private static readonly string[] AncientBookPrefixCharLib =
	{
		"古", "太上", "洞玄", "玉清", "真一", "上清", "玄都", "清微"
	};

	private static readonly string[] AncientBookSuffixCharLib =
	{
		"密旨", "秘要", "玄义", "妙旨", "正解", "真诠", "要略", "枢要", "道纪", "内篇"
	};
	internal static string GenerateName(string daoTu, int grade)
	{
		return GenerateGongFaNameForDaoTuAndGrade(daoTu, grade, 0L);
	}

	internal static string GenerateName(string daoTu, int grade, long seed)
	{
		return GenerateGongFaNameForDaoTuAndGrade(daoTu, grade, seed);
	}

	internal static string GenerateGongFaNameForDaoTuAndGrade(string daoTu, int grade, long seed)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (grade <= 0 || string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return string.Empty;
		}

		return grade <= 3
			? GenerateLowPinGongFaName(normalizedDaoTu, seed + grade)
			: GenerateHighPinGongFaName(normalizedDaoTu, seed + grade);
	}

	internal static string NormalizeNameForGrade(string currentName, string daoTu, int grade)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return string.IsNullOrWhiteSpace(currentName) ? string.Empty : currentName.Trim();
		}

		if (grade <= 3)
		{
			return string.IsNullOrWhiteSpace(currentName) ? GenerateLowPinGongFaName(normalizedDaoTu, grade) : currentName.Trim();
		}

		return ConvertGongFaNameToHighPin(currentName, normalizedDaoTu);
	}

	internal static string ConvertGongFaNameToHighPin(string currentName, string daoTu)
	{
		string text = (currentName ?? string.Empty).Trim();
		if (LooksLikeHighPinName(text))
		{
			return text;
		}

		return GenerateHighPinGongFaName(daoTu, XjDeterministicHash.StableHash(text + "|" + NormalizeDaoTu(daoTu)));
	}

	internal static string GenerateLowPinGongFaName(string daoTu, long seed)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return string.Empty;
		}

		string[] singleLib = GetDaoTuSingleCharLib(normalizedDaoTu);
		string[] doubleLib = GetDaoTuDoubleCharLib(normalizedDaoTu);
		int roll = XjDeterministicHash.PositiveIndex(seed, normalizedDaoTu + "|low", 10);
		string suffix = Pick(GongFaSuffixCharLib, seed + 19, normalizedDaoTu);

		if (roll < 6)
		{
			string first = Pick(singleLib, seed + 1, normalizedDaoTu);
			return first + PickDistinctComponent(singleLib, first, seed + 2, normalizedDaoTu) + suffix;
		}

		if (roll < 7)
		{
			string first = Pick(doubleLib, seed + 3, normalizedDaoTu);
			return first + PickDistinctComponent(singleLib, first, seed + 4, normalizedDaoTu) + suffix;
		}

		if (roll < 8)
		{
			string first = Pick(singleLib, seed + 5, normalizedDaoTu);
			return first + PickDistinctComponent(doubleLib, first, seed + 6, normalizedDaoTu) + suffix;
		}

		return Pick(doubleLib, seed + 7, normalizedDaoTu) + suffix;
	}

	internal static string GenerateHighPinGongFaName(string daoTu, long seed)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return string.Empty;
		}

		string[] singleLib = GetDaoTuSingleCharLib(normalizedDaoTu);
		string[] doubleLib = GetDaoTuDoubleCharLib(normalizedDaoTu);
		string suffix = Pick(GongFaSuffixCharLib, seed + 23, normalizedDaoTu);
		int roll = XjDeterministicHash.PositiveIndex(seed, normalizedDaoTu + "|high", 30);

		if (roll < 6)
		{
			string first = Pick(CommonDoubleCharLib, seed + 1, normalizedDaoTu);
			return first + PickDistinctComponent(doubleLib, first, seed + 2, normalizedDaoTu) + suffix;
		}

		if (roll < 14)
		{
			string first = Pick(doubleLib, seed + 3, normalizedDaoTu);
			string second = PickDifferent(doubleLib, first, seed + 4, normalizedDaoTu);
			return first + second + suffix;
		}

		if (roll < 15)
		{
			string first = Pick(doubleLib, seed + 5, normalizedDaoTu);
			return first + PickDistinctComponent(singleLib, first, seed + 6, normalizedDaoTu) + suffix;
		}

		if (roll < 16)
		{
			string first = Pick(singleLib, seed + 7, normalizedDaoTu);
			return first + PickDistinctComponent(doubleLib, first, seed + 8, normalizedDaoTu) + suffix;
		}

		if (roll < 17)
		{
			string first = Pick(CommonDoubleCharLib, seed + 9, normalizedDaoTu);
			return first + PickDistinctComponent(singleLib, first, seed + 10, normalizedDaoTu) + suffix;
		}

		if (roll < 18)
		{
			string first = Pick(singleLib, seed + 11, normalizedDaoTu);
			return first + PickDistinctComponent(CommonDoubleCharLib, first, seed + 12, normalizedDaoTu) + suffix;
		}

		if (roll < 24)
		{
			string first = Pick(doubleLib, seed + 13, normalizedDaoTu);
			return first + PickDistinctComponent(CommonDoubleCharLib, first, seed + 14, normalizedDaoTu) + suffix;
		}

		return GenerateAncientBookGongFaNameInternal(normalizedDaoTu, seed + 31);
	}

	internal static string GenerateAncientBookGongFaNameInternal(string daoTu, long seed)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return string.Empty;
		}

		string[] singleLib = GetDaoTuSingleCharLib(normalizedDaoTu);
		string[] doubleLib = GetDaoTuDoubleCharLib(normalizedDaoTu);
		string prefix = Pick(AncientBookPrefixCharLib, seed + 1, normalizedDaoTu);
		string bookSuffix = Pick(AncientBookSuffixCharLib, seed + 3, normalizedDaoTu);
		int roll = XjDeterministicHash.PositiveIndex(seed, normalizedDaoTu + "|book", 6);

		if (roll < 2)
		{
			string first = Pick(singleLib, seed + 4, normalizedDaoTu);
			return prefix + first + PickDistinctComponent(singleLib, first, seed + 5, normalizedDaoTu) + bookSuffix;
		}

		if (roll < 3)
		{
			string first = Pick(doubleLib, seed + 6, normalizedDaoTu);
			return prefix + first + PickDistinctComponent(doubleLib, first, seed + 7, normalizedDaoTu) + bookSuffix;
		}

		if (roll < 4)
		{
			string first = Pick(doubleLib, seed + 8, normalizedDaoTu);
			return prefix + first + PickDistinctComponent(CommonDoubleCharLib, first, seed + 9, normalizedDaoTu) + bookSuffix;
		}

		if (roll < 5)
		{
			string first = Pick(CommonDoubleCharLib, seed + 10, normalizedDaoTu);
			return prefix + first + PickDistinctComponent(doubleLib, first, seed + 11, normalizedDaoTu) + bookSuffix;
		}

		return prefix + Pick(doubleLib, seed + 12, normalizedDaoTu) + bookSuffix;
	}

	private static bool LooksLikeHighPinName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		for (int i = 0; i < AncientBookPrefixCharLib.Length; i++)
		{
			if (name.StartsWith(AncientBookPrefixCharLib[i], StringComparison.Ordinal))
			{
				return true;
			}
		}

		return name.Length >= 4;
	}

	private static string[] GetDaoTuSingleCharLib(string daoTu)
	{
		if (IsSanYin(daoTu)) return SanYinSingleCharLib;
		if (IsYuanZhao(daoTu)) return YuanZhaoSingleCharLib;
		if (IsHongXia(daoTu)) return HongXiaSingleCharLib;
		if (IsSanYang(daoTu)) return SanYangSingleCharLib;
		if (IsJinDe(daoTu)) return JinDeSingleCharLib;
		if (IsMuDe(daoTu)) return MuDeSingleCharLib;
		if (IsShuiDe(daoTu)) return ShuiDeSingleCharLib;
		if (IsHuoDe(daoTu)) return HuoDeSingleCharLib;
		if (IsTuDe(daoTu)) return TuDeSingleCharLib;
		if (IsSuDe(daoTu)) return SuDeSingleCharLib;
		if (IsXiaoKui(daoTu)) return XiaoKuiSingleCharLib;
		if (IsShangWu(daoTu)) return ShangWuSingleCharLib;
		if (IsYuZhen(daoTu)) return YuZhenSingleCharLib;
		if (IsHengZhu(daoTu)) return HengZhuSingleCharLib;
		if (IsQuanDan(daoTu)) return QuanDanSingleCharLib;
		if (IsZhiBo(daoTu)) return ZhiBoSingleCharLib;
		if (IsSiTian(daoTu)) return SiTianSingleCharLib;
		if (IsDuWei(daoTu)) return DuWeiSingleCharLib;
		if (IsSanLei(daoTu)) return SanLeiSingleCharLib;
		if (IsShiErQi(daoTu)) return GetShiErQiSingleCharLib(daoTu);
		return CommonDoubleCharLib;
	}

	private static string[] GetDaoTuDoubleCharLib(string daoTu)
	{
		if (IsSanYin(daoTu)) return SanYinDoubleCharLib;
		if (IsYuanZhao(daoTu)) return YuanZhaoDoubleCharLib;
		if (IsHongXia(daoTu)) return HongXiaDoubleCharLib;
		if (IsSanYang(daoTu)) return SanYangDoubleCharLib;
		if (IsJinDe(daoTu)) return JinDeDoubleCharLib;
		if (IsMuDe(daoTu)) return MuDeDoubleCharLib;
		if (IsShuiDe(daoTu)) return ShuiDeDoubleCharLib;
		if (IsHuoDe(daoTu)) return HuoDeDoubleCharLib;
		if (IsTuDe(daoTu)) return TuDeDoubleCharLib;
		if (IsSuDe(daoTu)) return SuDeDoubleCharLib;
		if (IsXiaoKui(daoTu)) return XiaoKuiDoubleCharLib;
		if (IsShangWu(daoTu)) return ShangWuDoubleCharLib;
		if (IsYuZhen(daoTu)) return YuZhenDoubleCharLib;
		if (IsHengZhu(daoTu)) return HengZhuDoubleCharLib;
		if (IsQuanDan(daoTu)) return QuanDanDoubleCharLib;
		if (IsZhiBo(daoTu)) return ZhiBoDoubleCharLib;
		if (IsSiTian(daoTu)) return SiTianDoubleCharLib;
		if (IsDuWei(daoTu)) return DuWeiDoubleCharLib;
		if (IsSanLei(daoTu)) return SanLeiDoubleCharLib;
		if (IsShiErQi(daoTu)) return GetShiErQiDoubleCharLib(daoTu);
		return CommonDoubleCharLib;
	}

	private static string[] GetShiErQiSingleCharLib(string daoTu)
	{
		switch (daoTu)
		{
			case "清炁": return QingQiSingleCharLib;
			case "紫炁": return ZiQiSingleCharLib;
			case "真炁": return ZhenQiSingleCharLib;
			case "邃炁": return SuiQiSingleCharLib;
			case "寒炁": return HanQiSingleCharLib;
			case "晞炁": return XiQiSingleCharLib;
			case "瑞炁": return RuiQiSingleCharLib;
			case "煞炁": return ShaQiSingleCharLib;
			case "华炁": return HuaQiSingleCharLib;
			case "谪炁": return ZheQiSingleCharLib;
			case "上仪": return ShangYiSingleCharLib;
			case "下仪": return XiaYiSingleCharLib;
			default: return CommonDoubleCharLib;
		}
	}

	private static string[] GetShiErQiDoubleCharLib(string daoTu)
	{
		switch (daoTu)
		{
			case "清炁": return QingQiDoubleCharLib;
			case "紫炁": return ZiQiDoubleCharLib;
			case "真炁": return ZhenQiDoubleCharLib;
			case "邃炁": return SuiQiDoubleCharLib;
			case "寒炁": return HanQiDoubleCharLib;
			case "晞炁": return XiQiDoubleCharLib;
			case "瑞炁": return RuiQiDoubleCharLib;
			case "煞炁": return ShaQiDoubleCharLib;
			case "华炁": return HuaQiDoubleCharLib;
			case "谪炁": return ZheQiDoubleCharLib;
			case "上仪": return ShangYiDoubleCharLib;
			case "下仪": return XiaYiDoubleCharLib;
			default: return CommonDoubleCharLib;
		}
	}

	private static bool IsSanYin(string daoTu) => daoTu == "太阴" || daoTu == "少阴" || daoTu == "厥阴";
	private static bool IsYuanZhao(string daoTu) => daoTu == "渊照";
	private static bool IsHongXia(string daoTu) => daoTu == "虹霞";
	private static bool IsSanYang(string daoTu) => daoTu == "太阳" || daoTu == "少阳" || daoTu == "明阳";
	private static bool IsJinDe(string daoTu) => daoTu == "兑金" || daoTu == "逍金" || daoTu == "齐金" || daoTu == "库金" || daoTu == "庚金" || daoTu == "长庚";
	private static bool IsMuDe(string daoTu) => daoTu == "角木" || daoTu == "正木" || daoTu == "集木" || daoTu == "更木" || daoTu == "保木";
	private static bool IsShuiDe(string daoTu) => daoTu == "坎水" || daoTu == "渌水" || daoTu == "合水" || daoTu == "府水" || daoTu == "牝水";
	private static bool IsHuoDe(string daoTu) => daoTu == "离火" || daoTu == "灴火" || daoTu == "并火" || daoTu == "真火" || daoTu == "牡火";
	private static bool IsTuDe(string daoTu) => daoTu == "艮土" || daoTu == "戊土" || daoTu == "归土" || daoTu == "宝土" || daoTu == "宣土";
	private static bool IsSuDe(string daoTu) => daoTu == "青宣";
	private static bool IsXiaoKui(string daoTu) => daoTu == "鸺葵";
	private static bool IsShangWu(string daoTu) => daoTu == "上巫";
	private static bool IsYuZhen(string daoTu) => daoTu == "玉真";
	private static bool IsHengZhu(string daoTu) => daoTu == "衡祝";
	private static bool IsQuanDan(string daoTu) => daoTu == "全丹";
	private static bool IsZhiBo(string daoTu) => daoTu == "执孛";
	private static bool IsSiTian(string daoTu) => daoTu == "司天";
	private static bool IsDuWei(string daoTu) => daoTu == "都卫";
	private static bool IsSanLei(string daoTu) => daoTu == "玄雷" || daoTu == "霄雷" || daoTu == "元雷";
	private static bool IsShiErQi(string daoTu) => daoTu is "清炁" or "紫炁" or "真炁" or "邃炁" or "寒炁" or "晞炁"
		or "瑞炁" or "煞炁" or "华炁" or "谪炁" or "上仪" or "下仪";

	private static string NormalizeDaoTu(string daoTu)
	{
		string text = XjDaoTuIntentIdentity.ResolveCore(daoTu);
		if (string.IsNullOrWhiteSpace(text)
			|| string.Equals(text, "基础", StringComparison.Ordinal)
			|| string.Equals(text, "玄门", StringComparison.Ordinal)
			|| string.Equals(text, "无道途", StringComparison.Ordinal)
			|| !IsKnownDaoTu(text))
		{
			return string.Empty;
		}

		return text;
	}

	private static bool IsKnownDaoTu(string daoTu)
	{
		return IsSanYin(daoTu)
			|| IsYuanZhao(daoTu)
			|| IsHongXia(daoTu)
			|| IsSanYang(daoTu)
			|| IsJinDe(daoTu)
			|| IsMuDe(daoTu)
			|| IsShuiDe(daoTu)
			|| IsHuoDe(daoTu)
			|| IsTuDe(daoTu)
			|| IsSuDe(daoTu)
			|| IsXiaoKui(daoTu)
			|| IsShangWu(daoTu)
			|| IsYuZhen(daoTu)
			|| IsHengZhu(daoTu)
			|| IsQuanDan(daoTu)
			|| IsZhiBo(daoTu)
			|| IsSiTian(daoTu)
			|| IsDuWei(daoTu)
			|| IsSanLei(daoTu)
			|| IsShiErQi(daoTu);
	}

	private static string Pick(string[] values, long seed, string salt)
	{
		if (values == null || values.Length == 0)
		{
			return string.Empty;
		}

		return values[XjDeterministicHash.PositiveIndex(seed, salt, values.Length)];
	}

	private static string PickDistinctComponent(string[] values, string current, long seed, string salt)
	{
		if (values == null || values.Length == 0)
		{
			return string.Empty;
		}

		int start = XjDeterministicHash.PositiveIndex(seed, salt, values.Length);
		for (int offset = 0; offset < values.Length; offset++)
		{
			string candidate = values[(start + offset) % values.Length];
			if (!IsRedundantBoundary(current, candidate))
			{
				return candidate;
			}
		}

		return values[start];
	}

	private static bool IsRedundantBoundary(string left, string right)
	{
		string first = left ?? string.Empty;
		string second = right ?? string.Empty;
		if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
		{
			return false;
		}

		return string.Equals(first, second, StringComparison.Ordinal)
			|| first.EndsWith(second, StringComparison.Ordinal)
			|| second.StartsWith(first, StringComparison.Ordinal)
			|| first[first.Length - 1] == second[0];
	}

	private static string PickDifferent(string[] values, string current, long seed, string salt)
	{
		string picked = Pick(values, seed, salt);
		if (values == null || values.Length <= 1 || !string.Equals(picked, current, StringComparison.Ordinal))
		{
			return picked;
		}

		int index = (XjDeterministicHash.PositiveIndex(seed, salt, values.Length) + 1) % values.Length;
		return values[index];
	}

}

internal readonly struct XjGongFaDefinition
{
	internal const int MaxGrade = 7;
	internal readonly string Name;
	internal readonly int Grade;
	internal readonly string DaoTu;
	internal readonly bool IsReadOnly;

	internal XjGongFaDefinition(string name, int grade, string daoTu, bool isReadOnly)
	{
		Name = name ?? string.Empty;
		Grade = grade;
		DaoTu = daoTu ?? string.Empty;
		IsReadOnly = isReadOnly;
	}

	internal static bool IsValidGrade(int grade)
	{
		return grade >= 1 && grade <= MaxGrade;
	}

	internal static bool CanAdvanceActively(int grade)
	{
		return grade >= 1 && grade <= 5;
	}
}


internal readonly struct XjGongFaState
{
	internal static XjGongFaState Empty { get; } = new XjGongFaState(
		false,
		string.Empty,
		0,
		0,
		0f,
		string.Empty,
		false,
		"Empty");

	internal readonly bool Found;
	internal readonly string Name;
	internal readonly int Grade;
	internal readonly int Stage;
	internal readonly float Progress;
	internal readonly string DaoTu;
	internal readonly bool IsReadOnly;
	internal readonly string ReasonCode;

	internal XjGongFaState(
		bool found,
		string name,
		int grade,
		int stage,
		float progress,
		string daoTu,
		bool isReadOnly,
		string reasonCode)
	{
		Found = found;
		Name = name ?? string.Empty;
		Grade = grade;
		Stage = stage;
		Progress = progress;
		DaoTu = daoTu ?? string.Empty;
		IsReadOnly = isReadOnly;
		ReasonCode = reasonCode ?? string.Empty;
	}
}
