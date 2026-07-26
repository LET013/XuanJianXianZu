using System;

namespace XuanJianVNext.Systems.Broadcast;

internal static class XjEventIconCatalog
{
	internal const string DanFangAcquire = "DanFangAquire";
	internal const string DengMingShi = "DengMingShi";
	internal const string DongTianClose = "DongTianClose";
	internal const string DongTianDeath = "DongTianDeath";
	internal const string DongTianOpen = "DongTianOpen";
	internal const string FaBaoCreation = "FaBoCreation";
	internal const string GongFaAcquire = "GongFaAquire";
	internal const string HighDanYao = "HighDanYao";
	internal const string HighYaoCai = "HighYaoCai";
	internal const string YaoCaiAcquire = "YaocaiAquire";
	internal const string HighRealmDeath = "HighrealmDeath";
	internal const string Jielin = "Jielin";
	internal const string JinDanDemon = "JindanDemon";
	internal const string JinDanFail = "JindanFail";
	internal const string JinDanUpgrade = "JindaUpgrade";
	internal const string JinXingLegacy = "JinXingLegacy";
	internal const string JiYuanChange = "JiYuanChange";
	internal const string LianDan = "LianDan";
	internal const string LingWuAppear = "LingWuAppear";
	internal const string QiuJinFaAcquire = "QiuJinFaAquire";
	internal const string RenDan = "RenDan";
	internal const string TaiYinYueHua = "TaiYinYueHua";
	internal const string YinSiAppear = "YinSiAppear";
	internal const string YinSiLeave = "YinSiLeave";
	internal const string ZhaLu = "ZhaLu";
	internal const string ZiFuUpgrade = "ZifuUpgrade";
	internal const string ZongMenCreation = "ZongmenCreation";
	internal const string ZongMenChongTu = "ZongMenChongTu";
	// 天下纪事九类总图标。常量值必须与 GameResources/ui/Icons/event 下的新资源同名。
	internal const string HistoryCultivation = "HistoryCultivation";
	internal const string HistoryFamily = "JiaZu";
	internal const string HistorySect = "ZongMen";
	internal const string HistoryInheritance = "ChuanCheng";
	internal const string HistoryCraft = "HistoryCraft";
	internal const string HistoryOpportunity = "JiYuan";
	internal const string HistoryVendetta = "EnYuan";
	internal const string HistoryLifeDeath = "ShengSi";
	internal const string HistoryWorld = "HistoryWorld";

	// 旧调用兼容别名。新代码统一通过 ResolveCategoryIconId 进入九类总图标。
	internal const string HistoryDongTian = HistoryOpportunity;
	internal const string HistoryResource = HistoryInheritance;
	internal const string HistoryYinSi = HistoryLifeDeath;
	internal const string HistoryEvil = HistoryVendetta;

	internal static string BuildIconPath(string iconId)
	{
		string normalized = NormalizeIconId(iconId);
		if (string.Equals(normalized, TaiYinYueHua, StringComparison.Ordinal))
		{
			return "ui/Icons/Qi/iconResTaiYinYueHua";
		}
		return string.IsNullOrEmpty(normalized) ? "ui/Icons/history" : "ui/Icons/event/" + normalized;
	}

	internal static string ResolveCategoryIconId(string category)
	{
		string value = (category ?? string.Empty).Trim();
		if (string.Equals(value, "修行", StringComparison.Ordinal) || string.Equals(value, "高境", StringComparison.Ordinal)) return HistoryCultivation;
		if (string.Equals(value, "家族", StringComparison.Ordinal)) return HistoryFamily;
		if (string.Equals(value, "宗门", StringComparison.Ordinal)) return HistorySect;
		if (string.Equals(value, "传承", StringComparison.Ordinal) || string.Equals(value, "资源", StringComparison.Ordinal)) return HistoryInheritance;
		if (string.Equals(value, "百艺", StringComparison.Ordinal) || string.Equals(value, "四艺", StringComparison.Ordinal)) return HistoryCraft;
		if (string.Equals(value, "机缘", StringComparison.Ordinal) || string.Equals(value, "洞天", StringComparison.Ordinal)) return HistoryOpportunity;
		if (string.Equals(value, "恩怨", StringComparison.Ordinal) || string.Equals(value, "恶行", StringComparison.Ordinal) || string.Equals(value, "劫数", StringComparison.Ordinal)) return HistoryVendetta;
		if (string.Equals(value, "生死", StringComparison.Ordinal) || string.Equals(value, "阴司", StringComparison.Ordinal)) return HistoryLifeDeath;
		return HistoryWorld;
	}

	internal static string NormalizeIconId(string iconId)
	{
		if (string.IsNullOrWhiteSpace(iconId))
		{
			return string.Empty;
		}

		string value = iconId.Trim();
		if (string.Equals(value, "iconResTaiYinYueHua", StringComparison.Ordinal))
		{
			return TaiYinYueHua;
		}
		if (value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
		{
			value = value.Substring(0, value.Length - 4);
		}

		const string prefix = "ui/Icons/event/";
		if (value.StartsWith(prefix, StringComparison.Ordinal))
		{
			value = value.Substring(prefix.Length);
		}

		if (string.Equals(value, "iconResTaiYinYueHua", StringComparison.Ordinal))
		{
			return TaiYinYueHua;
		}

		return value;
	}

	internal static string ResolveFromWorldHistoryText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		if (HasAny(text, "纪元", "时代更易", "天时", "气运改易"))
		{
			return JiYuanChange;
		}

		if (HasAny(text, "纳气入库", "之气归入", "纳气仓库"))
		{
			return TaiYinYueHua;
		}

		if (HasAny(text, "阴司离去", "阴司归去", "阴司退去", "诛邪既毕"))
		{
			return YinSiLeave;
		}

		if (HasAny(text, "阴司", "阴司降世", "阴司现世"))
		{
			return YinSiAppear;
		}

		if (HasAny(text, "结璘", "结璘仙"))
		{
			return Jielin;
		}

		if (HasAny(text, "金性妖邪", "成魔", "妖邪诞生", "堕为妖邪"))
		{
			return JinDanDemon;
		}

		if (HasAny(text, "金丹失败", "求金失败", "冲击金丹失败", "金门失守", "求金未成"))
		{
			return JinDanFail;
		}

		if (HasAny(text, "证得金丹", "成就金丹", "金丹成象", "晋升金丹", "金丹初成"))
		{
			return JinDanUpgrade;
		}

		if (HasAny(text, "紫府", "开紫府", "晋升紫府", "紫府开门"))
		{
			return ZiFuUpgrade;
		}

		if (HasAny(text, "洞天暂闭", "洞天关闭", "洞门复闭", "洞天隐去"))
		{
			return DongTianClose;
		}

		if (HasAny(text, "洞天身陨", "洞天陨落", "探索洞天身死", "洞天中身死"))
		{
			return DongTianDeath;
		}

		if (HasAny(text, "洞天", "洞门", "显化", "洞天开启", "洞天现世"))
		{
			return DongTianOpen;
		}

		if (HasAny(text, "宗门宣战", "宗门冲突", "传檄", "开坛点将", "伐"))
		{
			return ZongMenChongTu;
		}

		if (HasAny(text, "开山立派", "创立宗门", "宗门", "立下山门"))
		{
			return ZongMenCreation;
		}

		if (HasAny(text, "求金法", "求金之法", "金门初启"))
		{
			return QiuJinFaAcquire;
		}

		if (HasAny(text, "功法", "功阁", "上法入谱", "高法归族", "家学传承"))
		{
			return GongFaAcquire;
		}

		if (HasAny(text, "灵宝", "法宝", "炼宝", "法宝归位", "法宝升阶"))
		{
			return FaBaoCreation;
		}

		if (HasAny(text, "丹方"))
		{
			return DanFangAcquire;
		}

		if (HasAny(text, "金性遗留", "金性不散", "遗留金性"))
		{
			return JinXingLegacy;
		}

		if (HasAny(text, "灵物现世", "凝成灵物", "家族重宝仓库"))
		{
			return LingWuAppear;
		}

		if (HasAny(text, "上品药材", "珍稀药草"))
		{
			return YaoCaiAcquire;
		}

		if (HasAny(text, "高阶丹药", "宝丹", "大丹"))
		{
			return HighDanYao;
		}

		if (HasAny(text, "人丹"))
		{
			return RenDan;
		}

		if (HasAny(text, "炸炉", "爆炉", "丹炉炸裂", "炉毁"))
		{
			return ZhaLu;
		}

		if (HasAny(text, "炼丹", "开炉", "丹事", "炼丹大事", "炼成丹药", "丹成出炉", "炼成《"))
		{
			return LianDan;
		}

		if (HasAny(text, "陨落", "身死", "死亡", "坐化"))
		{
			return HighRealmDeath;
		}

		if (HasAny(text, "登名石", "札录", "记名"))
		{
			return DengMingShi;
		}

		return string.Empty;
	}

	private static bool HasAny(string text, params string[] keywords)
	{
		for (int i = 0; i < keywords.Length; i++)
		{
			if (!string.IsNullOrEmpty(keywords[i]) && text.IndexOf(keywords[i], StringComparison.Ordinal) >= 0)
			{
				return true;
			}
		}

		return false;
	}
}
