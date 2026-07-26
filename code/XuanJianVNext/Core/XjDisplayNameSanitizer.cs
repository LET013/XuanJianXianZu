using System;

namespace XuanJianVNext.Core;

internal static class XjDisplayNameSanitizer
{
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
		switch (key)
		{
			case "Ok":
			case "Empty":
			case "PersonalGongFa":
			case "ManualRealmEntry":
				return string.Empty;
			case "LongShuBirth": return "龙属天成";
			case "QiYuDongTian": return "洞天机缘";
			case "YinSiSuppressed": return "阴司追索";
			case "YinSiPursuit": return "阴司索命";
			case "YinSiNoDeathArchive": return "阴司销名";
			case "YinSiAppear": return "阴司降世";
			case "YinSiLeave": return "阴司离去";
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
			case "QiYuDongTian": return "洞天机缘";
			case "YinSiSuppressed": return "阴司追索";
			case "YinSiPursuit": return "阴司索命";
			case "YinSiNoDeathArchive": return "阴司销名";
			case "YinSiAppear": return "阴司降世";
			case "YinSiLeave": return "阴司离去";
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
					.Replace("JindanDemon", "金性妖邪")
					.Replace("JinDanDemon", "金性妖邪");
		}
	}

	private static bool LooksLikeCodeName(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return true;
		if (text.StartsWith("XuanJian_config_", StringComparison.Ordinal)) return true;
		if (text.StartsWith("xuanjian.", StringComparison.OrdinalIgnoreCase)) return true;
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
			if ((c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')
			{
				continue;
			}
			return false;
		}

		return hasAsciiLetter && (hasUpper || text.IndexOf('_') >= 0 || text.IndexOf('.') >= 0);
	}
}





