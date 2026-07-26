using System;
using System.Globalization;

namespace XuanJianVNext.Data.FaBao;

internal readonly struct XjFaBaoDefinition
{
	internal readonly string Id;
	internal readonly string Name;
	internal readonly string DaoTu;
	internal readonly string ClassName;

	internal XjFaBaoDefinition(string id, string name, string daoTu, string className)
	{
		Id = id ?? string.Empty;
		Name = name ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		ClassName = className ?? string.Empty;
	}
}

internal readonly struct XjFaBaoBonusProfile
{
	internal readonly float CultivationSpeedBonus;
	internal readonly float GuoWeiYiXiangBonus;
	internal readonly float MingShuBonus;
	internal readonly float HuiGuangBonus;
	internal readonly float LifespanBonus;
	internal readonly float AttackBonus;
	internal readonly float DamageReduction;
	internal readonly float HealthBonus;
	internal readonly float ArmorPenetration;
	internal readonly float HealthShield;
	internal readonly float Lifesteal;
	internal readonly float DodgeBonus;
	internal readonly float CritTakenReduction;
	internal readonly float HealbackBonus;
	internal readonly float AccuracyBonus;
	internal readonly float CritBonus;
	internal readonly float AttackSpeedBonus;
	internal readonly float SameRealmDamageBonus;
	internal readonly float ShieldBreakBonus;
	internal readonly float BreakthroughChanceBonus;
	internal readonly float TrueDamageRatio;

	internal XjFaBaoBonusProfile(
		float cultivationSpeedBonus,
		float guoWeiYiXiangBonus,
		float attackBonus,
		float damageReduction,
		float healthBonus,
		float armorPenetration,
		float healthShield,
		float lifesteal,
		float dodgeBonus = 0f,
		float critTakenReduction = 0f,
		float healbackBonus = 0f,
		float mingShuBonus = 0f,
		float huiGuangBonus = 0f,
		float lifespanBonus = 0f,
		float accuracyBonus = 0f,
		float critBonus = 0f,
		float attackSpeedBonus = 0f,
		float sameRealmDamageBonus = 0f,
		float shieldBreakBonus = 0f,
		float breakthroughChanceBonus = 0f,
		float trueDamageRatio = 0f)
	{
		CultivationSpeedBonus = Math.Max(0f, cultivationSpeedBonus);
		GuoWeiYiXiangBonus = Math.Max(0f, guoWeiYiXiangBonus);
		MingShuBonus = Math.Max(0f, mingShuBonus);
		HuiGuangBonus = Math.Max(0f, huiGuangBonus);
		LifespanBonus = Math.Max(0f, lifespanBonus);
		AttackBonus = Math.Max(0f, attackBonus);
		DamageReduction = Math.Max(0f, damageReduction);
		HealthBonus = Math.Max(0f, healthBonus);
		ArmorPenetration = Math.Max(0f, armorPenetration);
		HealthShield = Math.Max(0f, healthShield);
		Lifesteal = Math.Max(0f, lifesteal);
		DodgeBonus = Math.Max(0f, dodgeBonus);
		CritTakenReduction = Math.Max(0f, critTakenReduction);
		HealbackBonus = Math.Max(0f, healbackBonus);
		AccuracyBonus = Math.Max(0f, accuracyBonus);
		CritBonus = Math.Max(0f, critBonus);
		AttackSpeedBonus = Math.Max(0f, attackSpeedBonus);
		SameRealmDamageBonus = Math.Max(0f, sameRealmDamageBonus);
		ShieldBreakBonus = Math.Max(0f, shieldBreakBonus);
		BreakthroughChanceBonus = Math.Max(0f, breakthroughChanceBonus);
		TrueDamageRatio = Math.Max(0f, trueDamageRatio);
	}
}

internal static class XjFaBaoCatalog
{
	internal const string ZhuJiFaQiClass = "筑基法器";
	internal const string ZiFuLingBaoClass = "紫府灵宝";
	internal const string JinDanFaBaoClass = "金丹法宝";
	internal const string RoleAttack = "攻击";
	internal const string RoleDefense = "防御";
	internal const string RoleSupport = "辅助";
	internal const string LegacyRoleDefenseSupport = "防御/辅助";

	internal static readonly XjFaBaoBonusProfile ZhuJiFaQiBonus = new XjFaBaoBonusProfile(
		0.05f,
		0f,
		0.05f,
		0.025f,
		0.05f,
		0.025f,
		0.05f,
		0.025f,
		trueDamageRatio: 0.015f);

	internal static readonly XjFaBaoBonusProfile ZiFuLingBaoBonus = new XjFaBaoBonusProfile(
		0.10f,
		0f,
		0.10f,
		0.05f,
		0.10f,
		0.05f,
		0.10f,
		0.05f,
		trueDamageRatio: 0.03f);

	internal static readonly XjFaBaoBonusProfile JinDanFaBaoBonus = new XjFaBaoBonusProfile(
		0f,
		0.20f,
		0.20f,
		0.10f,
		0.20f,
		0.10f,
		0.20f,
		0.10f,
		trueDamageRatio: 0.06f);

	internal static readonly string[] CommonWords =
	{
		"青霄", "玄苍", "霜月", "沧澜", "琼华", "碧落", "寒江", "瑶光", "霁云", "泠风",
		"苍梧", "丹霞", "扶摇", "广陵", "流云", "蓬莱", "青云", "若木", "松风", "幽兰"
	};

	internal static readonly string[] WeaponWords =
	{
		"剑", "刀", "枪", "弓", "鼎", "印", "珠", "钟", "鉴", "戒", "幡", "尺"
	};

	internal static readonly string[] AttackWeaponWords = { "剑", "刀", "枪", "弓" };

	internal static readonly string[] SupportWeaponWords = { "鼎", "印", "珠", "钟", "鉴", "戒", "幡", "尺" };

	internal static readonly string[] DefenseEquipmentKinds = { "灵盔", "灵甲", "灵履", "灵戒", "灵符" };

	internal static readonly string[] AttackAffixLabels =
	{
		"伤害提升", "减伤穿透", "真伤转化", "命中提升", "暴击提升", "攻速提升", "同境界伤害", "破盾", "吸血"
	};

	internal static readonly string[] DefenseAffixLabels =
	{
		"伤害减免", "生命提升", "护盾提升", "闪避提升", "受暴击降低"
	};

	internal static readonly string[] SupportAffixLabels =
	{
		"每秒回血", "修炼速度", "果位意象", "命数加成", "慧光加成", "寿命增加", "突破概率"
	};

	private static readonly string[] SanYangWords =
	{
		"晨晖", "旭照", "曜日", "曦光", "朝霞", "晴空", "阳和", "朱明", "炳灵", "晞阳",
		"赤曜", "流景", "扶桑", "日冕", "炎景", "昊阳", "金曦", "曜灵", "天晖", "阳魄"
	};

	private static readonly string[] SanYinWords =
	{
		"幽溟", "玄阴", "霜寒", "冰魄", "月华", "寒渊", "寂夜", "冷泉", "阴霭", "冥晦",
		"素月", "夜魄", "玄霜", "寒星", "月阙", "冰轮", "幽月", "冥泉", "霜魄", "太阴"
	};

	private static readonly string[] SanLeiWords =
	{
		"霹雳", "震电", "紫电", "雷霆", "惊蛰", "天威", "电光", "雷动", "急霆", "迅雷",
		"苍雷", "霄鼓", "雷狱", "玄霆", "帝霄", "奔雷", "轰天", "劫电", "神霄", "雷纹"
	};

	private static readonly string[] JinDeWords =
	{
		"金精", "锋锐", "锐金", "坚钢", "利刃", "刚锐", "金芒", "锐芒", "锋利", "锐利",
		"太白", "庚辰", "白刃", "金阙", "玄锋", "破岳", "银汉", "铁壁", "镇金", "锋魄"
	};

	private static readonly string[] MuDeWords =
	{
		"青木", "翠柏", "松涛", "柳风", "芳草", "苍松", "椿龄", "桂枝", "兰若", "竹影",
		"若木", "扶疏", "灵根", "碧枝", "长春", "青华", "木皇", "森罗", "春生", "翠羽"
	};

	private static readonly string[] ShuiDeWords =
	{
		"沧海", "碧波", "洪流", "寒渊", "清泉", "沧溟", "溪涧", "江渚", "浪涛", "瀑流",
		"玄渊", "潮生", "归墟", "水府", "澜庭", "寒潮", "灵泽", "渊镜", "海若", "流魄"
	};

	private static readonly string[] HuoDeWords =
	{
		"烈焰", "炽炎", "烈火", "焰光", "赤焰", "炎阳", "焚天", "炽烈", "炎火", "赤炎",
		"朱火", "丹焰", "神烬", "流火", "火府", "炎庭", "离明", "焰轮", "赤霄", "烛天"
	};

	private static readonly string[] TuDeWords =
	{
		"厚土", "坤舆", "山岳", "丘陵", "疆域", "埏垓", "垒石", "坤厚", "岱岳", "玄壤",
		"镇岳", "土皇", "昆仑", "岩庭", "地脉", "嵩岳", "坤元", "岳镇", "山河", "厚载"
	};

	private static readonly string[] ShiErQiWords =
	{
		"清虚", "太虚", "混元", "灵枢", "玄元", "太初", "真元", "元始", "虚灵", "玄微",
		"玉清", "太素", "太易", "太极", "洞玄", "妙有", "虚皇", "道枢", "元景", "灵台"
	};

	internal static bool TryGetDaoTuWords(string daoTu, out string category, out string[] words)
	{
		category = ResolveDaoTuCategory(daoTu);
		words = category switch
		{
			"三阳" => SanYangWords,
			"三阴" => SanYinWords,
			"三雷" => SanLeiWords,
			"金德" => JinDeWords,
			"木德" => MuDeWords,
			"水德" => ShuiDeWords,
			"火德" => HuoDeWords,
			"土德" => TuDeWords,
			_ => ShiErQiWords
		};

		return words.Length > 0;
	}

	internal static string ResolveDaoTuCategory(string daoTu)
	{
		string value = (daoTu ?? string.Empty).Trim();
		return value switch
		{
			"太阳" or "少阳" or "明阳" => "三阳",
			"太阴" or "少阴" or "厥阴" => "三阴",
			"玄雷" or "霄雷" or "元雷" => "三雷",
			"兑金" or "庚金" or "齐金" or "库金" or "逍金" => "金德",
			"角木" or "正木" or "集木" or "更木" or "保木" => "木德",
			"坎水" or "渌水" or "合水" or "府水" or "牝水" => "水德",
			"离火" or "并火" or "真火" or "灴火" or "牡火" => "火德",
			"艮土" or "戊土" or "宣土" or "归土" or "宝土" or "青宣" => "土德",
			_ => "十二炁"
		};
	}

	internal static bool TryGetBonusProfile(string className, out XjFaBaoBonusProfile profile)
	{
		if (string.Equals(className, ZhuJiFaQiClass, StringComparison.Ordinal))
		{
			profile = ZhuJiFaQiBonus;
			return true;
		}

		if (string.Equals(className, ZiFuLingBaoClass, StringComparison.Ordinal))
		{
			profile = ZiFuLingBaoBonus;
			return true;
		}

		if (string.Equals(className, JinDanFaBaoClass, StringComparison.Ordinal))
		{
			profile = JinDanFaBaoBonus;
			return true;
		}

		profile = default;
		return false;
	}

	internal static bool TryGetBonusProfileForRole(string className, string role, out XjFaBaoBonusProfile profile)
	{
		if (!TryGetBonusProfile(className, out XjFaBaoBonusProfile baseProfile))
		{
			profile = default;
			return false;
		}

		string normalizedRole = NormalizeRole(string.Empty, role);
		if (string.Equals(normalizedRole, RoleAttack, StringComparison.Ordinal))
		{
			profile = new XjFaBaoBonusProfile(
				0f, 0f, baseProfile.AttackBonus, 0f, 0f, baseProfile.ArmorPenetration, 0f, baseProfile.Lifesteal,
				accuracyBonus: baseProfile.AccuracyBonus,
				critBonus: baseProfile.CritBonus,
				attackSpeedBonus: baseProfile.AttackSpeedBonus,
				sameRealmDamageBonus: baseProfile.SameRealmDamageBonus,
				shieldBreakBonus: baseProfile.ShieldBreakBonus,
				trueDamageRatio: baseProfile.TrueDamageRatio);
			return true;
		}

		if (string.Equals(normalizedRole, RoleDefense, StringComparison.Ordinal))
		{
			profile = new XjFaBaoBonusProfile(
				0f, 0f, 0f, baseProfile.DamageReduction, baseProfile.HealthBonus, 0f, baseProfile.HealthShield, 0f,
				dodgeBonus: baseProfile.DodgeBonus,
				critTakenReduction: baseProfile.CritTakenReduction);
			return true;
		}

		profile = new XjFaBaoBonusProfile(
			baseProfile.CultivationSpeedBonus, baseProfile.GuoWeiYiXiangBonus, 0f, 0f, 0f, 0f, 0f, 0f,
			healbackBonus: baseProfile.HealbackBonus,
			mingShuBonus: baseProfile.MingShuBonus,
			huiGuangBonus: baseProfile.HuiGuangBonus,
			lifespanBonus: baseProfile.LifespanBonus,
			breakthroughChanceBonus: baseProfile.BreakthroughChanceBonus);
		return true;
	}

	internal static bool IsZhuJiFaQi(string className)
	{
		return string.Equals(className, ZhuJiFaQiClass, StringComparison.Ordinal);
	}

	internal static bool IsZiFuLingBao(string className)
	{
		return string.Equals(className, ZiFuLingBaoClass, StringComparison.Ordinal);
	}

	internal static bool IsJinDanFaBao(string className)
	{
		return string.Equals(className, JinDanFaBaoClass, StringComparison.Ordinal);
	}

	internal static string ResolveRoleFromWeapon(string weapon)
	{
		string value = (weapon ?? string.Empty).Trim();
		if (ContainsExact(AttackWeaponWords, value))
		{
			return RoleAttack;
		}
		if (ContainsExact(DefenseEquipmentKinds, value))
		{
			return RoleDefense;
		}
		return RoleSupport;
	}

	internal static string NormalizeRole(string kind, string role)
	{
		string normalizedKind = (kind ?? string.Empty).Trim();
		string normalizedRole = (role ?? string.Empty).Trim();
		if (ContainsExact(DefenseEquipmentKinds, normalizedKind))
		{
			return RoleDefense;
		}
		if (ContainsExact(AttackWeaponWords, normalizedKind))
		{
			return RoleAttack;
		}
		if (ContainsExact(SupportWeaponWords, normalizedKind))
		{
			return RoleSupport;
		}
		if (string.Equals(normalizedRole, RoleAttack, StringComparison.Ordinal)
			|| string.Equals(normalizedRole, RoleDefense, StringComparison.Ordinal)
			|| string.Equals(normalizedRole, RoleSupport, StringComparison.Ordinal))
		{
			return normalizedRole;
		}
		if (string.Equals(normalizedRole, LegacyRoleDefenseSupport, StringComparison.Ordinal))
		{
			return RoleSupport;
		}
		return ResolveRoleFromWeapon(normalizedKind);
	}

	internal static string[] GetWeaponWordsForRole(string role)
	{
		return string.Equals(role, RoleAttack, StringComparison.Ordinal)
			? AttackWeaponWords
			: SupportWeaponWords;
	}

	internal static string[] GetAffixLabelsForRole(string role)
	{
		string normalizedRole = NormalizeRole(string.Empty, role);
		if (string.Equals(normalizedRole, RoleAttack, StringComparison.Ordinal))
		{
			return AttackAffixLabels;
		}
		if (string.Equals(normalizedRole, RoleDefense, StringComparison.Ordinal))
		{
			return DefenseAffixLabels;
		}
		return SupportAffixLabels;
	}

	internal static string NormalizeAffixesForRole(string affixes, string role)
	{
		return NormalizeAffixes(affixes, role, int.MaxValue, float.MaxValue);
	}

	internal static string NormalizeAffixesForClass(string affixes, string role, string className)
	{
		int maxCount = IsJinDanFaBao(className) ? 5 : IsZhuJiFaQi(className) ? 1 : 3;
		float maxPercent = IsJinDanFaBao(className) ? 30f : IsZhuJiFaQi(className) ? 5f : 10f;
		return NormalizeAffixes(affixes, role, maxCount, maxPercent);
	}

	private static string NormalizeAffixes(string affixes, string role, int maxCount, float maxPercent)
	{
		if (string.IsNullOrWhiteSpace(affixes) || maxCount <= 0 || maxPercent <= 0f)
		{
			return string.Empty;
		}

		string[] allowed = GetAffixLabelsForRole(role);
		string[] raw = affixes.Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
		var result = new System.Collections.Generic.List<string>(Math.Min(raw.Length, maxCount));
		var usedLabels = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < raw.Length && result.Count < maxCount; i++)
		{
			string part = raw[i].Trim();
			int plus = part.IndexOf('+');
			int percent = part.IndexOf('%', plus + 1);
			if (plus <= 0 || percent <= plus) continue;

			string label = part.Substring(0, plus).Trim();
			if (string.Equals(label, "防御穿透", StringComparison.Ordinal)) label = "减伤穿透";
			else if (string.Equals(label, "辉光加成", StringComparison.Ordinal)) label = "慧光加成";
			if (!ContainsExact(allowed, label) || !usedLabels.Add(label)) continue;

			string valueText = part.Substring(plus + 1, percent - plus - 1).Trim();
			if (!float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) continue;
			value = Math.Clamp(value, 0f, maxPercent);
			string formatted = value.ToString(value < 2f ? "0.0" : "0.#", CultureInfo.InvariantCulture);
			result.Add(label + "+" + formatted + "%");
		}
		return string.Join("/", result);
	}

	private static bool ContainsExact(string[] values, string value)
	{
		if (values == null || string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		for (int i = 0; i < values.Length; i++)
		{
			if (string.Equals(values[i], value, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}
}
