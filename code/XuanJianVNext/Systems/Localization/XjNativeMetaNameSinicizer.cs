using System;
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
