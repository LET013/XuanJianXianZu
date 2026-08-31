using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.GongFa;

namespace XuanJianVNext.Systems.Localization;

internal static class XjNativeMetaNameSinicizer
{
	private static readonly string[] Surnames =
	{
		"姜", "花", "李", "陈", "王", "刘", "张", "赵", "孙", "周", "吴", "郑", "冯", "朱", "秦", "许",
		"何", "吕", "施", "孔", "曹", "严", "华", "金", "魏", "陶", "沈", "韩", "杨", "洛", "费", "顾",
		"萧", "谢", "唐", "宋", "齐", "梁", "杜", "崔", "陆", "林", "孟", "袁", "司马", "南宫", "东方", "轩辕"
	};


	private static readonly string[] KingdomPrefixes =
	{
		"大玄", "乾元", "景明", "承天", "永宁", "昭武", "弘文", "泰和", "隆兴", "元祐",
		"天启", "开元", "建宁", "云昭", "青岚", "赤霄", "北辰", "南华", "东宁", "西梁"
	};

	// 1.0 冻结：玄鉴自动生成的国家名只允许“X国 / XX国”。以下长后缀仅用于
	// 清洗旧的原生/玄鉴自动名，绝不再作为新国家命名候选。
	private const string CanonicalKingdomSuffix = "国";
	private static readonly string[] KingdomLegacySuffixes =
	{
		"联合王国", "王国", "帝国", "王朝", "皇朝", "神朝", "天朝", "国", "朝"
	};

	private static readonly string[] SectLikeSuffixes =
	{
		"山庄", "圣地", "仙府", "道场", "真宗", "宗门", "宗", "门", "派", "宫", "观", "阁", "谷",
		"寺", "庵", "院", "楼", "殿", "教", "盟"
	};

	private static readonly string[] ReligionPrefixes =
	{
		"清微", "紫府", "洞玄", "玄都", "太上", "玉宸", "太微", "上景", "青玄", "玄鉴", "丹阳", "太阴",
		"少阳", "真炁", "太玄", "玄霄", "金庭", "黄庭", "玉清", "上清"
	};

	private static readonly string[] ReligionMiddles =
	{
		"洞明", "法箓", "灵章", "道纪", "玄坛", "真诠", "宝诰", "云篆", "丹书", "法脉", "符命", "清箓"
	};

	private static readonly string[] ReligionSuffixes =
	{
		"教", "道", "玄教", "法脉", "道统", "玄坛", "道庭", "真宗"
	};

	private static readonly string[] BookDaoTus =
	{
		"太阳", "少阳", "明阳", "太阴", "少阴", "厥阴", "玄雷", "霄雷", "元雷", "兑金", "角木", "坎水",
		"离火", "艮土", "清炁", "紫炁", "真炁", "谪炁", "上仪", "下仪", "青宣"
	};


	internal static void Normalize(global::Kingdom kingdom)
	{
		if (kingdom?.data == null)
		{
			return;
		}

		string existing = (kingdom.data.name ?? kingdom.name ?? string.Empty).Trim();
		// 玩家主动改名属于玩家表达，不由玄鉴强制覆盖；但帝明阳王号仍会通过同一
		// 国号核心清洗函数取前缀，避免自定义长后缀造成“XX王王”。
		if (kingdom.data.custom_name) return;
		if (IsCanonicalKingdomName(existing)) return;

		long seed = ResolveSeed(kingdom.data.id, existing);
		string baseName = ResolveCanonicalKingdomStem(existing);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			baseName = ResolveCanonicalKingdomStem(Pick(KingdomPrefixes, seed + 13, "native_kingdom_prefix"));
		}
		if (string.IsNullOrWhiteSpace(baseName)) baseName = "玄";

		string name = baseName + CanonicalKingdomSuffix;
		kingdom.name = name;
		SetName(kingdom.data, name);
	}


	internal static void Normalize(global::Family family)
	{
		if (family?.data == null || family.data.custom_name)
		{
			return;
		}

		long seed = ResolveSeed(family.data.id, family.data.name);
		string name = Pick(Surnames, seed, "native_family") + "氏家";
		SetName(family.data, name);
	}

	internal static void Normalize(global::Religion religion)
	{
		if (religion?.data == null || religion.data.custom_name)
		{
			return;
		}

		long seed = ResolveSeed(religion.data.id, religion.data.name);
		string prefix = Pick(ReligionPrefixes, seed + 1, "native_religion_prefix");
		string middle = Pick(ReligionMiddles, seed + 3, "native_religion_middle");
		string suffix = Pick(ReligionSuffixes, seed + 5, "native_religion_suffix");
		string name = prefix + middle + suffix;
		SetName(religion.data, name);
	}

	internal static void Normalize(global::Book book)
	{
		if (book?.data == null || book.data.custom_name)
		{
			return;
		}

		long seed = ResolveSeed(book.data.id, book.data.name);
		string daoTu = Pick(BookDaoTus, seed + 7, "native_book_daotu");
		string name = XjGongFaNameLibrary.GenerateAncientBookGongFaNameInternal(daoTu, seed + 11);
		if (string.IsNullOrWhiteSpace(name))
		{
			name = "玄都清微要略";
		}
		SetBookName(book.data, name);
	}


	internal static string ResolveCanonicalKingdomName(string raw)
	{
		string stem = ResolveCanonicalKingdomStem(raw);
		return (string.IsNullOrWhiteSpace(stem) ? "玄" : stem) + CanonicalKingdomSuffix;
	}

	internal static string ResolveCanonicalKingdomStem(string raw)
	{
		string stem = StripKnownSuffixes(raw);
		if (string.IsNullOrWhiteSpace(stem)) return string.Empty;
		// 玄鉴自动国号必须是1—2个汉字。原生外语/随机串不直接拼“国”，而是
		// 退回玄鉴国号池；帝明阳王号也复用这一核心提取，避免长政体词进入人物前缀。
		return ExtractHanStem(stem, 2);
	}

	private static bool IsCanonicalKingdomName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (!text.EndsWith(CanonicalKingdomSuffix, StringComparison.Ordinal) || text.Length < 2 || text.Length > 3)
			return false;
		string stem = text.Substring(0, text.Length - CanonicalKingdomSuffix.Length).Trim();
		return stem.Length >= 1 && stem.Length <= 2
			&& string.Equals(stem, ExtractHanStem(stem, 2), StringComparison.Ordinal)
			&& !EndsWithAny(stem, SectLikeSuffixes);
	}

	private static bool EndsWithAny(string value, string[] suffixes)
	{
		if (string.IsNullOrWhiteSpace(value) || suffixes == null)
		{
			return false;
		}

		for (int i = 0; i < suffixes.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(suffixes[i])
				&& value.EndsWith(suffixes[i], StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static string StripKnownSuffixes(string value)
	{
		string result = (value ?? string.Empty).Trim();
		bool changed;
		do
		{
			changed = TryStripLongestSuffix(ref result, SectLikeSuffixes)
				|| TryStripLongestSuffix(ref result, KingdomLegacySuffixes);
		}
		while (changed && !string.IsNullOrWhiteSpace(result));
		return result.Trim();
	}

	private static bool TryStripLongestSuffix(ref string value, string[] suffixes)
	{
		if (string.IsNullOrWhiteSpace(value) || suffixes == null)
		{
			return false;
		}

		string matched = string.Empty;
		for (int i = 0; i < suffixes.Length; i++)
		{
			string suffix = suffixes[i] ?? string.Empty;
			if (suffix.Length > matched.Length && value.EndsWith(suffix, StringComparison.Ordinal))
			{
				matched = suffix;
			}
		}
		if (matched.Length == 0)
		{
			return false;
		}
		value = value.Substring(0, value.Length - matched.Length).Trim();
		return true;
	}

	private static string ExtractHanStem(string value, int maximumCharacters)
	{
		if (string.IsNullOrWhiteSpace(value) || maximumCharacters <= 0) return string.Empty;
		char[] buffer = new char[Math.Min(maximumCharacters, value.Length)];
		int count = 0;
		for (int i = 0; i < value.Length && count < maximumCharacters; i++)
		{
			char c = value[i];
			if (!IsHanCharacter(c)) continue;
			buffer[count++] = c;
		}
		return count <= 0 ? string.Empty : new string(buffer, 0, count);
	}

	private static bool IsHanCharacter(char c)
	{
		return (c >= '\u3400' && c <= '\u4DBF')
			|| (c >= '\u4E00' && c <= '\u9FFF')
			|| (c >= '\uF900' && c <= '\uFAFF');
	}

	private static void SetName(MetaObjectData data, string name)
	{
		string text = (name ?? string.Empty).Trim();
		if (data == null || string.IsNullOrWhiteSpace(text))
		{
			return;
		}

		data.name = text;
		data.custom_name = false;
	}

	private static void SetBookName(BookData data, string name)
	{
		string text = (name ?? string.Empty).Trim();
		if (data == null || string.IsNullOrWhiteSpace(text))
		{
			return;
		}

		data.name = text;
		data.custom_name = false;
	}

	private static long ResolveSeed(long id, string fallback)
	{
		if (id > 0L)
		{
			return id;
		}

		return XjDeterministicHash.StableHash(fallback ?? string.Empty);
	}

	private static string Pick(string[] values, long seed, string salt)
	{
		if (values == null || values.Length == 0)
		{
			return string.Empty;
		}

		return values[XjDeterministicHash.PositiveIndex(seed, salt, values.Length)];
	}
}
