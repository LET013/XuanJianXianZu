using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;

namespace XuanJianVNext.Systems.FaBao;

/// <summary>
/// 原生装备生成器会从通用词库抽名，名称可能与实际槽位完全无关。
/// 本规则只按物品真实资源ID/装备类型命名，不改变属性、稀有度或资源ID。
/// </summary>
internal static class XjNativeEquipmentNamePolicy
{
	private static readonly string[] HelmetSuffixes = { "盔", "冠", "胄" };
	private static readonly string[] ArmorSuffixes = { "甲", "铠" };
	private static readonly string[] GloveSuffixes = { "手套", "护手" };
	private static readonly string[] BootsSuffixes = { "靴", "履" };
	private static readonly string[] NecklaceSuffixes = { "项链", "灵坠" };
	private static readonly string[] RingSuffixes = { "戒", "指环" };
	private static readonly string[] SwordSuffixes = { "剑" };
	private static readonly string[] BladeSuffixes = { "刀" };
	private static readonly string[] AxeSuffixes = { "斧" };
	private static readonly string[] SpearSuffixes = { "枪" };
	private static readonly string[] BowSuffixes = { "弓" };
	private static readonly string[] HammerSuffixes = { "锤" };

	internal static bool TryNormalize(Item item, EquipmentType expectedType, long ownerId)
	{
		if (item?.data == null) return false;
		EquipmentAsset asset = item.getAsset();
		if (asset == null) return false;
		string assetId = (((Asset)asset).id ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(assetId)
			|| XjFaBaoEquipmentAssets.IsXuanJianEquipmentAsset(assetId))
		{
			return false;
		}
		item.data.get("xuanjian.native_equipment_name_schema", out int schema, 0);
		if (schema >= 1) return true;

		XjNativeEquipmentNameKind kind = ResolveKind(assetId, expectedType);
		string[] suffixes = ResolveSuffixes(kind);
		if (suffixes.Length == 0 || XjFaBaoCatalog.CommonWords.Length == 0) return false;

		long seed = ownerId + item.data.id;
		string common = XjFaBaoCatalog.CommonWords[
			XjDeterministicHash.PositiveIndex(seed, "native_equipment_common|" + assetId, XjFaBaoCatalog.CommonWords.Length)];
		string suffix = suffixes[
			XjDeterministicHash.PositiveIndex(seed, "native_equipment_suffix|" + assetId, suffixes.Length)];
		string name = common + suffix;
		if (string.IsNullOrWhiteSpace(name)) return false;

		try
		{
			item.setName(name, true);
			item.data.set("xuanjian.native_equipment_name_schema", 1);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static XjNativeEquipmentNameKind ResolveKind(string assetId, EquipmentType expectedType)
	{
		string id = assetId.ToLowerInvariant();
		if (ContainsAny(id, "glove", "gauntlet", "handguard")) return XjNativeEquipmentNameKind.Gloves;
		if (ContainsAny(id, "necklace", "amulet", "pendant")) return XjNativeEquipmentNameKind.Necklace;
		if (ContainsAny(id, "helmet", "helm", "crown", "hat")) return XjNativeEquipmentNameKind.Helmet;
		if (ContainsAny(id, "armor", "armour", "robe")) return XjNativeEquipmentNameKind.Armor;
		if (ContainsAny(id, "boots", "boot", "shoes", "shoe")) return XjNativeEquipmentNameKind.Boots;
		if (ContainsAny(id, "ring")) return XjNativeEquipmentNameKind.Ring;
		if (ContainsAny(id, "katana", "saber", "sabre", "blade", "_dao", "dao_")) return XjNativeEquipmentNameKind.Blade;
		if (ContainsAny(id, "sword")) return XjNativeEquipmentNameKind.Sword;
		if (ContainsAny(id, "spear", "lance", "pike")) return XjNativeEquipmentNameKind.Spear;
		if (ContainsAny(id, "bow")) return XjNativeEquipmentNameKind.Bow;
		if (ContainsAny(id, "axe")) return XjNativeEquipmentNameKind.Axe;
		if (ContainsAny(id, "hammer", "mace", "club")) return XjNativeEquipmentNameKind.Hammer;

		return expectedType switch
		{
			EquipmentType.Helmet => XjNativeEquipmentNameKind.Helmet,
			EquipmentType.Armor => XjNativeEquipmentNameKind.Armor,
			EquipmentType.Boots => XjNativeEquipmentNameKind.Boots,
			EquipmentType.Ring => XjNativeEquipmentNameKind.Ring,
			EquipmentType.Amulet => XjNativeEquipmentNameKind.Necklace,
			_ => XjNativeEquipmentNameKind.Unknown
		};
	}

	private static string[] ResolveSuffixes(XjNativeEquipmentNameKind kind)
	{
		return kind switch
		{
			XjNativeEquipmentNameKind.Helmet => HelmetSuffixes,
			XjNativeEquipmentNameKind.Armor => ArmorSuffixes,
			XjNativeEquipmentNameKind.Gloves => GloveSuffixes,
			XjNativeEquipmentNameKind.Boots => BootsSuffixes,
			XjNativeEquipmentNameKind.Necklace => NecklaceSuffixes,
			XjNativeEquipmentNameKind.Ring => RingSuffixes,
			XjNativeEquipmentNameKind.Sword => SwordSuffixes,
			XjNativeEquipmentNameKind.Blade => BladeSuffixes,
			XjNativeEquipmentNameKind.Axe => AxeSuffixes,
			XjNativeEquipmentNameKind.Spear => SpearSuffixes,
			XjNativeEquipmentNameKind.Bow => BowSuffixes,
			XjNativeEquipmentNameKind.Hammer => HammerSuffixes,
			_ => Array.Empty<string>()
		};
	}

	private static bool ContainsAny(string value, params string[] tokens)
	{
		for (int i = 0; i < tokens.Length; i++)
		{
			if (value.IndexOf(tokens[i], StringComparison.Ordinal) >= 0) return true;
		}
		return false;
	}

	private enum XjNativeEquipmentNameKind : byte
	{
		Unknown = 0,
		Helmet = 1,
		Armor = 2,
		Gloves = 3,
		Boots = 4,
		Necklace = 5,
		Ring = 6,
		Sword = 7,
		Blade = 8,
		Axe = 9,
		Spear = 10,
		Bow = 11,
		Hammer = 12
	}
}
