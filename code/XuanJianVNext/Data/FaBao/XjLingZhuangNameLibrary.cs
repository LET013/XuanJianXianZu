using System;

namespace XuanJianVNext.Data.FaBao;

/// <summary>
/// 玄鉴五类槽位灵装的命名与器型文案。
/// 成品名称沿用攻击法宝的三段式结构：通用意象词 + 道途意象词 + 单字器型。
/// </summary>
internal static class XjLingZhuangNameLibrary
{
	private static readonly string[] HelmetNameSuffixes = { "冠", "盔", "冕" };
	private static readonly string[] ArmorNameSuffixes = { "甲", "铠", "袍", "衣" };
	private static readonly string[] BootsNameSuffixes = { "履", "靴", "屐" };
	private static readonly string[] RingNameSuffixes = { "戒", "环", "玦" };
	private static readonly string[] AmuletNameSuffixes = { "符", "佩", "珏" };

	internal static string BuildAssetTranslationKey(EquipmentType equipmentType, string branch)
	{
		string slotKey = ResolveSlotKey(equipmentType);
		string normalizedBranch = (branch ?? string.Empty).Trim();
		return string.IsNullOrWhiteSpace(slotKey) || string.IsNullOrWhiteSpace(normalizedBranch)
			? string.Empty
			: "xj_fabao_slot_" + slotKey + "_" + normalizedBranch;
	}

	internal static string[] GetNameSuffixes(EquipmentType equipmentType)
	{
		return equipmentType switch
		{
			EquipmentType.Helmet => HelmetNameSuffixes,
			EquipmentType.Armor => ArmorNameSuffixes,
			EquipmentType.Boots => BootsNameSuffixes,
			EquipmentType.Ring => RingNameSuffixes,
			EquipmentType.Amulet => AmuletNameSuffixes,
			_ => Array.Empty<string>()
		};
	}

	internal static bool IsGeneratedNameForType(string name, EquipmentType equipmentType)
	{
		string value = (name ?? string.Empty).Trim();
		if (value.Length != 5 || value.IndexOf("玄鉴灵", StringComparison.Ordinal) >= 0)
		{
			return false;
		}

		string[] suffixes = GetNameSuffixes(equipmentType);
		for (int i = 0; i < suffixes.Length; i++)
		{
			if (value.EndsWith(suffixes[i], StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	internal static string ResolveTypeName(EquipmentType equipmentType)
	{
		return equipmentType switch
		{
			EquipmentType.Helmet => "玄鉴灵盔",
			EquipmentType.Armor => "玄鉴灵甲",
			EquipmentType.Boots => "玄鉴灵履",
			EquipmentType.Ring => "玄鉴灵戒",
			EquipmentType.Amulet => "玄鉴灵符",
			_ => string.Empty
		};
	}

	internal static string ResolveKind(EquipmentType equipmentType)
	{
		return equipmentType switch
		{
			EquipmentType.Helmet => "灵盔",
			EquipmentType.Armor => "灵甲",
			EquipmentType.Boots => "灵履",
			EquipmentType.Ring => "灵戒",
			EquipmentType.Amulet => "灵符",
			_ => string.Empty
		};
	}

	internal static string ResolveSlotLabel(EquipmentType equipmentType)
	{
		return equipmentType switch
		{
			EquipmentType.Helmet => "头盔",
			EquipmentType.Armor => "盔甲",
			EquipmentType.Boots => "鞋履",
			EquipmentType.Ring => "戒指",
			EquipmentType.Amulet => "护符",
			_ => string.Empty
		};
	}

	internal static string ResolveFunctionPhrase(EquipmentType equipmentType)
	{
		return equipmentType switch
		{
			EquipmentType.Helmet => "清光覆首，护持神庭与灵台，使外邪难侵、神识不乱",
			EquipmentType.Armor => "灵纹周流百骸，护住经脉脏腑，并将临身冲击层层卸去",
			EquipmentType.Boots => "炁机承足而行，稳固步罡身法，使进退腾挪皆与道途相应",
			EquipmentType.Ring => "法意收束于指掌之间，调摄真炁，辅助御器与施法",
			EquipmentType.Amulet => "宝光垂护心脉气海，遇险自发感应，镇压侵体异炁",
			_ => "灵机随身流转，护持修士道途"
		};
	}

	internal static bool TryResolveEquipmentTypeFromKind(string kind, out EquipmentType equipmentType)
	{
		equipmentType = default;
		string value = (kind ?? string.Empty).Trim();
		if (string.Equals(value, "灵盔", StringComparison.Ordinal))
		{
			equipmentType = EquipmentType.Helmet;
			return true;
		}
		if (string.Equals(value, "灵甲", StringComparison.Ordinal))
		{
			equipmentType = EquipmentType.Armor;
			return true;
		}
		if (string.Equals(value, "灵履", StringComparison.Ordinal))
		{
			equipmentType = EquipmentType.Boots;
			return true;
		}
		if (string.Equals(value, "灵戒", StringComparison.Ordinal))
		{
			equipmentType = EquipmentType.Ring;
			return true;
		}
		if (string.Equals(value, "灵符", StringComparison.Ordinal))
		{
			equipmentType = EquipmentType.Amulet;
			return true;
		}
		return false;
	}

	private static string ResolveSlotKey(EquipmentType equipmentType)
	{
		return equipmentType switch
		{
			EquipmentType.Helmet => "helmet",
			EquipmentType.Armor => "armor",
			EquipmentType.Boots => "boots",
			EquipmentType.Ring => "ring",
			EquipmentType.Amulet => "amulet",
			_ => string.Empty
		};
	}
}
