using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.LingWu;

internal sealed class XjLingWuDef
{
	internal string Id { get; }
	internal string DaoTuGroup { get; }
	internal string Name { get; }
	internal string Description { get; }
	internal string IconPath { get; }

	internal XjLingWuDef(string id, string daoTuGroup, string name, string description, string iconPath)
	{
		Id = id ?? string.Empty;
		DaoTuGroup = daoTuGroup ?? string.Empty;
		Name = name ?? string.Empty;
		Description = description ?? string.Empty;
		IconPath = iconPath ?? string.Empty;
	}
}

internal static class XjLingWuCatalog
{
	internal const string SanYin = "xj_lingwu_sanyin";
	internal const string SanYang = "xj_lingwu_sanyang";
	internal const string JinDe = "xj_lingwu_jinde";
	internal const string MuDe = "xj_lingwu_mude";
	internal const string ShuiDe = "xj_lingwu_shuide";
	internal const string HuoDe = "xj_lingwu_huode";
	internal const string TuDe = "xj_lingwu_tude";
	internal const string SuDe = "xj_lingwu_sude";
	internal const string SanLei = "xj_lingwu_sanlei";
	internal const string ShiErQi = "xj_lingwu_shierqi";
	internal const string BingGu = "xj_lingwu_binggu";
	internal const string LongGeng = "xj_lingwu_longgeng";

	private static readonly Dictionary<string, XjLingWuDef> ById = new(StringComparer.Ordinal)
	{
		[SanYin] = new XjLingWuDef(
			SanYin,
			"三阴",
			"太幽照魄珠",
			"银月环抱幽紫明珠，像紫府神魂沉入太阴后留下的一点清光。“照魄”兼具映照魂魄、留存道痕之意。",
			"GameResources/item/Arts/lingwu/LingWu-Yin.png"),
		[SanYang] = new XjLingWuDef(
			SanYang,
			"三阳",
			"纯阳曜真轮",
			"三重阳辉聚成大日宝轮，中央光核炽盛。“曜真”体现三阳显照、破妄、昭明的道性。",
			"GameResources/item/Arts/lingwu/LingWu-Yang.png"),
		[JinDe] = new XjLingWuDef(
			JinDe,
			"金德",
			"素锋藏华莲",
			"金白莲瓣托起锋芒星核，外柔内锐。“素锋”对应金德清肃，“藏华”对应锋芒敛于灵物之中。",
			"GameResources/item/Arts/lingwu/LingWu-Jin.png"),
		[MuDe] = new XjLingWuDef(
			MuDe,
			"木德",
			"青枝蕴生胎",
			"古木枝蔓环抱青翠灵胎，明显带有生发、滋养、延续之意，适合木德紫府遗蜕。",
			"GameResources/item/Arts/lingwu/LingWu-Mu.png"),
		[ShuiDe] = new XjLingWuDef(
			ShuiDe,
			"水德",
			"沧渊回澜珠",
			"水流环绕澄澈宝珠，内部呈旋涡之形。“回澜”体现水德流转、归聚、化解和回返。",
			"GameResources/item/Arts/lingwu/LingWu-Shui.png"),
		[HuoDe] = new XjLingWuDef(
			HuoDe,
			"火德",
			"离焰抱真心",
			"赤红莲焰包裹中央火种，像一颗永不熄灭的道心。“抱真”表示火焰中仍保存紫府生前的一线真意。",
			"GameResources/item/Arts/lingwu/LingWu-Huo.png"),
		[TuDe] = new XjLingWuDef(
			TuDe,
			"土德",
			"坤岳镇灵胎",
			"群山承托厚重圆胎，山川土气聚于一心。“镇灵”突出土德承载、镇压和稳固。",
			"GameResources/item/Arts/lingwu/LingWu-Tu.png"),
		[SuDe] = new XjLingWuDef(
			SuDe,
			"素德",
			"青宣素律简",
			"青白玄光凝作素简，清衡为骨、宣律为文，象征素德一脉的裁定与昭明。",
			"GameResources/item/Arts/lingwu/LingWu-Qi.png"),
		[SanLei] = new XjLingWuDef(
			SanLei,
			"三雷",
			"玉枢聚霆珠",
			"三道雷月环拱中央紫雷核心，对应三雷汇于玉枢、雷霆凝而不散。",
			"GameResources/item/Arts/lingwu/LingWu-Lei.png"),
		[ShiErQi] = new XjLingWuDef(
			ShiErQi,
			"十二炁",
			"列炁朝元轮",
			"十二枚不同色泽的炁珠环绕中央本源，正是十二炁各归其位、共同朝元的形态。",
			"GameResources/item/Arts/lingwu/LingWu-Qi.png"),
		[BingGu] = new XjLingWuDef(
			BingGu,
			"并古",
			"并古玄章玺",
			"金纹古玺裹纳旧世道痕，层层玄章如山河旧契。“并古”不取单一五德，而取并世古道遗意凝为一印。",
			"GameResources/item/Arts/lingwu/LingWu-Gu.png"),
		[LongGeng] = new XjLingWuDef(
			LongGeng,
			"长庚",
			"长庚照夜剑",
			"霜白剑影贯入星轮，寒锋内敛而庚金自明。“照夜”取长庚启明、剑气破暗之象。",
			"GameResources/item/Arts/lingwu/LingWu-Jian.png")
	};

	internal static bool TryGet(string lingWuId, out XjLingWuDef definition)
	{
		return ById.TryGetValue((lingWuId ?? string.Empty).Trim(), out definition);
	}

	internal static bool TryResolveByDaoTu(string daoTu, out XjLingWuDef definition)
	{
		definition = null;
		string value = (daoTu ?? string.Empty).Trim();
		if (value.Length == 0) return false;

		string id = value switch
		{
			"三阴" or "太阴" or "少阴" or "厥阴" => SanYin,
			"三阳" or "太阳" or "少阳" or "明阳" => SanYang,
			"长庚" => LongGeng,
			"金德" or "兑金" or "逍金" or "齐金" or "库金" or "庚金" => JinDe,
			"木德" or "角木" or "正木" or "集木" or "更木" or "保木" => MuDe,
			"水德" or "坎水" or "渌水" or "合水" or "府水" or "牝水" => ShuiDe,
			"火德" or "离火" or "灴火" or "并火" or "真火" or "牡火" => HuoDe,
			"土德" or "艮土" or "戊土" or "归土" or "宝土" or "宣土" => TuDe,
			"素德" => SuDe,
			"三雷" or "天雷" or "玄雷" or "霄雷" or "元雷" => SanLei,
			"十二炁" or "上仪" or "下仪" => ShiErQi,
			"并古" or "鸺葵" or "上巫" or "玉真" or "衡祝" or "青宣" or "全丹" or "执孛" or "司天" or "都卫" => BingGu,
			_ => value.EndsWith("炁", StringComparison.Ordinal) ? ShiErQi : string.Empty
		};
		return id.Length > 0 && ById.TryGetValue(id, out definition);
	}
}
