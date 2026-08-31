using System;

namespace XuanJianVNext.Core;

internal static class XjDisplayNameSanitizer
{
	internal static string AptitudeName(int rank, string fallback = "资质未定")
	{
		if (rank <= 0) return fallback;
		int normalized = Math.Max(1, Math.Min(6, rank));
		string resolved = EventSource("XjZz" + normalized);
		return string.IsNullOrWhiteSpace(resolved) ? fallback : resolved;
	}

	internal static string Clean(string value, string fallback = "未名")
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0) return fallback;
		text = TranslateKnownToken(text);
		text = text.Replace("/", "-");
		text = text.Replace("\\", "-");
		text = text.Replace("|", string.Empty);
		if (LooksLikeCodeName(text)) return fallback;
		return text;
	}

	internal static string GameTerm(string value, string fallback = "未定")
	{
		string text = Clean(value, fallback);
		if (string.IsNullOrWhiteSpace(text)) return fallback;
		return LooksLikeInternalToken(text) ? fallback : text;
	}

	internal static string TaskKind(string taskKind)
	{
		string key = (taskKind ?? string.Empty).Trim();
		if (key.Length == 0) return "空闲";
		switch (key)
		{
			case "TalismanBatch": return "绘制符箓";
			case "AlchemyBatch":
			case "AlchemyCommission": return "炼制丹药";
			case "ArtifactForge":
			case "ArtifactRefining":
			case "EquipmentForge":
			case "EquipmentEditorGrant":
			case "CultivatorSlotRefine":
			case "CultivatorSlotUpgrade": return "炼制器物";
			case "SectFormationCommission": return "营造护宗大阵";
			case "SectFormationRepair": return "修缮护宗大阵";
			case "FamilyFormationCommission": return "营造家族阵法";
			case "SecretRealmConstruction": return "营造秘境";
			case "SurveyingVoid": return "勘定太虚";
			case "LayingXuanTao": return "布置玄韬";
			case "SuppressingTreasure": return "镇压灵宝";
			case "NourishingSpace": return "滋养空间";
			case "StabilizingEntrance": return "稳定入口";
			case "UpgradingDongtian": return "洞天升格";
			case "DengMingShiSync": return "登名石复载";
			default:
				if (key.IndexOf("Talisman", StringComparison.OrdinalIgnoreCase) >= 0) return "绘制符箓";
				if (key.IndexOf("Alchemy", StringComparison.OrdinalIgnoreCase) >= 0) return "炼制丹药";
				if (key.IndexOf("Artifact", StringComparison.OrdinalIgnoreCase) >= 0
					|| key.IndexOf("Forge", StringComparison.OrdinalIgnoreCase) >= 0
					|| key.IndexOf("Equipment", StringComparison.OrdinalIgnoreCase) >= 0) return "炼制器物";
				if (key.IndexOf("Formation", StringComparison.OrdinalIgnoreCase) >= 0) return "阵法工程";
				if (key.IndexOf("Realm", StringComparison.OrdinalIgnoreCase) >= 0
					|| key.IndexOf("DongTian", StringComparison.OrdinalIgnoreCase) >= 0) return "秘境工程";
				return Clean(key, "百艺任务");
		}
	}

	internal static string SecretRealmStage(string stage)
	{
		string key = (stage ?? string.Empty).Trim();
		switch (key)
		{
			case "None": return "未启";
			case "SurveyingVoid": return "勘定太虚";
			case "LayingXuanTao": return "布置玄韬";
			case "SuppressingTreasure": return "镇压灵宝";
			case "NourishingSpace": return "滋养空间";
			case "StabilizingEntrance": return "稳定入口";
			case "Fudi": return "福地";
			case "Complete": return "秘境已成";
			case "UpgradingDongtian": return "洞天升格";
			case "Dongtian": return "洞天";
			case "Dormant": return "沉寂洞天";
			default: return Clean(key, "未定工程");
		}
	}

	internal static string EventSource(string source)
	{
		string key = (source ?? string.Empty).Trim();
		if (key.Length == 0) return string.Empty;
		if (key.StartsWith("QiYuDongTian:", StringComparison.Ordinal)) return "洞天机缘";
		if (key.StartsWith("LongShuBirth:", StringComparison.Ordinal)) return "龙属天成";
		if (key.StartsWith("UpperCultivatorCustomized:", StringComparison.Ordinal))
		{
			string purpose = key.Substring("UpperCultivatorCustomized:".Length).Trim();
			string purposeName = UpperCultivatorPurpose(purpose);
			return purposeName.Length == 0 ? "上修定制" : "上修定制·" + purposeName;
		}
		if (key.StartsWith("UpperGoldSupport:", StringComparison.Ordinal)) return "上修扶金";
		switch (key)
		{
			case "Ok":
			case "Empty":
			case "PersonalGongFa":
			case "ManualRealmEntry":
				return string.Empty;
			case "LongShuBirth": return "龙属天成";
			case "LongShuHeShuiInfluence": return "龙属合水";
			case "HeShuiFruitReservedForLongShu": return "合水拒证";
			case "QiYuDongTian": return "洞天机缘";
			case "YinSiSuppressed": return "阴司追索";
			case "YinSiPursuit": return "阴司索命";
			case "YinSiNoDeathArchive": return "阴司销名";
			case "YinSiAppear": return "阴司降世";
			case "YinSiLeave": return "阴司离去";
			case "XjRealm1": return "胎息";
			case "XjRealm2": return "炼气";
			case "XjRealm3": return "筑基";
			case "XjRealm4": return "紫府";
			case "XjRealm5": return "金丹";
			case "XjRealm6": return "道胎";
			case "XjRealm7": return "道胎";
			case "XjRealm11": return "黄冠";
			case "XjRealm12": return "真人";
			case "XjRealm13": return "真君羽士";
			case "XjRealm14": return "道胎";
			case "XjRealm21": return "僧侣";
			case "XjRealm22": return "法师";
			case "XjRealm23": return "怜愍";
			case "XjRealm24": return "摩诃";
			case "XjRealm25": return "法相";
			case "XjRealm26": return "世尊";
			case "ZiFuJinDan": return "紫府金丹道";
			case "FuQiYangXing": return "服气养性";
			case "XjGuShiDao": return "古释";
			case "XjJinShiDao": return "今释";
			case "XjJinXingReincarnation": return "金性转世";
			case "XjLongGengDaoTong": return "长庚道途";
			case "XjShenDan": return "神丹";
			case "ZhenYuan": return "真元";
			case "MingShu": return "命数";
			case "HuiGuang": return "道慧";
			case "ChuShen": return "出身";
			case "XjZz1": return "朽木难雕";
			case "XjZz2": return "可琢之材";
			case "XjZz3": return "璞玉之资";
			case "XjZz4": return "上乘根骨";
			case "XjZz5": return "天公垂目";
			case "XjZz6": return "天授道脉";
			case "Hidden": return "未显";
			case "Traced": return "已有踪迹";
			case "NearDiscovery": return "将被察觉";
			case "Known": return "阴司知悉";
			case "Locating": return "寻迹中";
			case "Pursuing": return "追索中";
			case "Evaded": return "已避过";
			case "Dead": return "身故";
			case "Active": return "存续";
			case "PendingReincarnatedZhengWei": return "待转世果位";
			case "JindanDemon":
			case "JinDanDemon": return "金性妖邪";
			case "JinDan": return "金丹成器";
			case "ZiFuRefine": return "紫府炼器";
			case "LingBaoUpgrade": return "灵宝升格";
			case "JieLinGrant": return "结璘赐宝";
			case "JieLinUpgrade": return "结璘升宝";
			case "EquipmentEditorGrant": return "赐器入命";
			case "CultivatorSlotRefine": return "炼器入身";
			case "CultivatorSlotUpgrade": return "灵宝升格";
			case "EquippedItem": return "随身器物";
			case "DengMingShi": return "登名石复载";
			case "FamilyBorrowQiuJinFa": return "家族借法";
			case "ZongMenQiuJinFa": return "宗门借法";
			case "QiuJinFaComprehended": return "自行参悟";
			case "LostOnDeath": return "身后遗留";
			case "LiveCraft": return "族中炼成";
			case "WarehouseSurplusCraft": return "余器入库";
			case "AlchemyBatch":
			case "AlchemyCommission": return "炼丹所得";
			case "TalismanBatch": return "符箓绘制";
			default:
				return LooksLikeInternalToken(key) ? string.Empty : Clean(key, string.Empty);
		}
	}

	internal static string DeathReason(string reasonCode)
	{
		string key = (reasonCode ?? string.Empty).Trim();
		if (key.Length == 0) return string.Empty;
		switch (key)
		{
			case "Ok":
			case "Empty":
				return string.Empty;
			case "QiYuDongTian": return "洞天遇险";
			case "YinSiSuppressed": return "阴司追索";
			case "YinSiPursuit": return "阴司索命";
			case "YinSiNoDeathArchive": return "阴司销名";
			case "BreakthroughFailure": return "破境不成";
			case "JinDanBreakthroughFailure": return "结丹不成";
			case "ZiFuBreakthroughFailure": return "开辟紫府不成";
			case "JieLinFailure": return "结璘不成";
			case "Combat":
			case "War": return "兵祸斗法";
			case "OldAge": return "寿尽";
			default:
				return LooksLikeInternalToken(key) ? string.Empty : Clean(key, string.Empty);
		}
	}

	internal static string BloodlineSource(string source)
	{
		string key = (source ?? string.Empty).Trim();
		if (key.Length == 0) return "其他";
		return key switch
		{
			"Founder" => "始祖",
			"FatherConfirmed" => "父系确认",
			"Atavism" => "返祖",
			"HighRealmOverride" => "境界覆盖",
			"UnknownFather" => "父系不可读",
			_ => GameTerm(key, "其他")
		};
	}

	internal static string ReleaseReason(string reason)
	{
		string key = (reason ?? string.Empty).Trim();
		if (key.Length == 0) return "释放";
		return key switch
		{
			"Death" => "身死释放",
			"Reassigned" => "果位改易",
			"Rollback" => "改易撤销",
			"Migration" => "旧档迁移",
			"AbsoluteDeath" => "真灵俱灭",
			_ => GameTerm(key, "其他缘故")
		};
	}

	internal static string JinDanFailureState(string state)
	{
		string key = (state ?? string.Empty).Trim();
		if (key.Length == 0) return string.Empty;
		if (key.StartsWith("ForcedDeath:", StringComparison.Ordinal)
			|| key.StartsWith("Terminal:", StringComparison.Ordinal)) return "结丹终败";
		return key switch
		{
			"NoGuoWei" => "果位未成",
			"NoGuoWeiDaoTu" => "果位道途未定",
			"QuanBingDeficient" => "权柄不足",
			"CrossDaoTuSpell" => "外道冲突",
			"BreakthroughFailed" => "破境失败",
			"BreakthroughFailure" => "破境失败",
			"JinDanBreakthroughFailure" => "结丹不成",
			_ => GameTerm(key, "结丹未成")
		};
	}

	private static string TranslateKnownToken(string text)
	{
		switch (text)
		{
			case "SurveyingVoid": return "勘定太虚";
			case "StabilizingEntrance": return "稳定入口";
			case "LayingXuanTao": return "布置玄韬";
			case "NourishingSpace": return "滋养空间";
			case "SuppressingTreasure": return "镇压灵宝";
			case "EquipmentEditorGrant": return "天授器物";
			case "TalismanBatch": return "绘制符箓";
			case "AlchemyBatch": return "炼制丹药";
			case "SectFormationCommission": return "营造护宗大阵";
			case "SectFormationRepair": return "修缮护宗大阵";
			case "LongShuBirth": return "龙属天成";
			case "LongShuHeShuiInfluence": return "龙属合水";
			case "HeShuiFruitReservedForLongShu": return "合水拒证";
			case "QiYuDongTian": return "洞天机缘";
			case "YinSiSuppressed": return "阴司追索";
			case "YinSiPursuit": return "阴司索命";
			case "YinSiNoDeathArchive": return "阴司销名";
			case "YinSiAppear": return "阴司降世";
			case "YinSiLeave": return "阴司离去";
			case "XjRealm1": return "胎息";
			case "XjRealm2": return "炼气";
			case "XjRealm3": return "筑基";
			case "XjRealm4": return "紫府";
			case "XjRealm5": return "金丹";
			case "XjRealm6": return "道胎";
			case "XjRealm7": return "道胎";
			case "XjRealm11": return "黄冠";
			case "XjRealm12": return "真人";
			case "XjRealm13": return "真君羽士";
			case "XjRealm14": return "道胎";
			case "XjRealm21": return "僧侣";
			case "XjRealm22": return "法师";
			case "XjRealm23": return "怜愍";
			case "XjRealm24": return "摩诃";
			case "XjRealm25": return "法相";
			case "XjRealm26": return "世尊";
			case "ZiFuJinDan": return "紫府金丹道";
			case "FuQiYangXing": return "服气养性";
			case "XjGuShiDao": return "古释";
			case "XjJinShiDao": return "今释";
			case "XjJinXingReincarnation": return "金性转世";
			case "XjLongGengDaoTong": return "长庚道途";
			case "XjShenDan": return "神丹";
			case "XjZz1": return "朽木难雕";
			case "XjZz2": return "可琢之材";
			case "XjZz3": return "璞玉之资";
			case "XjZz4": return "上乘根骨";
			case "XjZz5": return "天公垂目";
			case "XjZz6": return "天授道脉";
			case "ZhenYuan": return "真元";
			case "MingShu": return "命数";
			case "HuiGuang": return "道慧";
			case "ChuShen": return "出身";
			case "Hidden": return "未显";
			case "Traced": return "已有踪迹";
			case "NearDiscovery": return "将被察觉";
			case "Known": return "阴司知悉";
			case "Locating": return "寻迹中";
			case "Pursuing": return "追索中";
			case "Evaded": return "已避过";
			case "Dead": return "身故";
			case "Active": return "存续";
			case "PendingReincarnatedZhengWei": return "待转世果位";
			case "JindanDemon":
			case "JinDanDemon": return "金性妖邪";
			default:
				return text
					.Replace("SurveyingVoid", "勘定太虚")
					.Replace("StabilizingEntrance", "稳定入口")
					.Replace("LayingXuanTao", "布置玄韬")
					.Replace("NourishingSpace", "滋养空间")
					.Replace("SuppressingTreasure", "镇压灵宝")
					.Replace("EquipmentEditorGrant", "天授器物")
					.Replace("TalismanBatch", "绘制符箓")
					.Replace("AlchemyBatch", "炼制丹药")
					.Replace("SectFormationCommission", "营造护宗大阵")
					.Replace("SectFormationRepair", "修缮护宗大阵")
					.Replace("LongShuBirth", "龙属天成")
					.Replace("QiYuDongTian", "洞天机缘")
					.Replace("YinSiSuppressed", "阴司追索")
					.Replace("YinSiPursuit", "阴司索命")
					.Replace("YinSiNoDeathArchive", "阴司销名")
					.Replace("YinSiAppear", "阴司降世")
					.Replace("YinSiLeave", "阴司离去")
					.Replace("XjRealm13", "真君羽士")
					.Replace("XjRealm12", "真人")
					.Replace("XjRealm11", "黄冠")
					.Replace("XjRealm26", "世尊")
					.Replace("XjRealm25", "法相")
					.Replace("XjRealm24", "摩诃")
					.Replace("XjRealm23", "怜愍")
					.Replace("XjRealm22", "法师")
					.Replace("XjRealm21", "僧侣")
					.Replace("XjJinXingReincarnation", "金性转世")
					.Replace("XjLongGengDaoTong", "长庚道途")
					.Replace("XjGuShiDao", "古释")
					.Replace("XjJinShiDao", "今释")
					.Replace("ZiFuJinDan", "紫府金丹道")
					.Replace("FuQiYangXing", "服气养性")
					.Replace("XjShenDan", "神丹")
					.Replace("XjRealm14", "道胎")
					.Replace("XjRealm6", "道胎")
					.Replace("XjRealm5", "金丹")
					.Replace("XjRealm4", "紫府")
					.Replace("XjRealm3", "筑基")
					.Replace("XjRealm2", "炼气")
					.Replace("XjRealm1", "胎息")
					.Replace("XjRealm7", "道胎")
					.Replace("XjZz6", "天授道脉")
					.Replace("XjZz5", "天公垂目")
					.Replace("XjZz4", "上乘根骨")
					.Replace("XjZz3", "璞玉之资")
					.Replace("XjZz2", "可琢之材")
					.Replace("XjZz1", "朽木难雕")
					.Replace("JindanDemon", "金性妖邪")
					.Replace("JinDanDemon", "金性妖邪");
		}
	}

	private static string UpperCultivatorPurpose(string purpose)
	{
		return (purpose ?? string.Empty).Trim() switch
		{
			"MoonProbe" => "探月行动",
			"LineageHeir" => "道统继承人",
			"StrengthenLineage" => "壮大本道途",
			"InterfereOtherDao" => "干涉其他道途",
			"Other" => "其他后手",
			_ => string.Empty
		};
	}

	private static bool LooksLikeCodeName(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return true;
		if (text.StartsWith("XuanJian_config_", StringComparison.Ordinal)) return true;
		if (text.StartsWith("xuanjian.", StringComparison.OrdinalIgnoreCase)) return true;
		if (text.StartsWith("Xj", StringComparison.Ordinal)
			&& text.Length > 2
			&& char.IsLetterOrDigit(text[2])) return true;
		if (text.StartsWith("XuanJian", StringComparison.Ordinal)) return true;
		if (text.IndexOf("::", StringComparison.Ordinal) >= 0) return true;
		return false;
	}

	private static bool LooksLikeInternalToken(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return true;
		bool hasAsciiLetter = false;
		bool hasLower = false;
		bool hasUpper = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c >= 'a' && c <= 'z')
			{
				hasAsciiLetter = true;
				hasLower = true;
				continue;
			}
			if (c >= 'A' && c <= 'Z')
			{
				hasAsciiLetter = true;
				hasUpper = true;
				continue;
			}
			if ((c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.' || c == ':')
			{
				continue;
			}
			return false;
		}

		return hasAsciiLetter && (hasUpper || text.IndexOf('_') >= 0 || text.IndexOf('.') >= 0);
	}
}



