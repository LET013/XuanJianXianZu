using System;
using System.Collections.Generic;
using UnityEngine;

namespace XuanJianVNext.Systems.FaBao;

internal static class XjFaBaoEquipmentAssets
{
	private const string AttackGroupId = "xj_fabao_attack";
	private const string SupportGroupId = "xj_fabao_defense_support";
	private const string XjHelmetGroupId = "xj_equipment_helmet";
	private const string XjArmorGroupId = "xj_equipment_armor";
	private const string XjBootsGroupId = "xj_equipment_boots";
	private const string XjRingGroupId = "xj_equipment_ring";
	private const string XjAmuletGroupId = "xj_equipment_amulet";
	private const string FaBaoItemClass = "item_class_xuanjian_fabao";
	private const string LegacyNormalPrefix = "xj_gear_";
	private const string LegacyRuntimeForgedPrefix = "xj_runtime_forged_";

	private static bool _initialized;
	private static readonly HashSet<string> VanillaEquipmentIds = BuildVanillaEquipmentIds();
	private static readonly XjFaBaoEquipmentDefinition[] FaBaoDefinitions = BuildFaBaoDefinitions();

	internal static void Init()
	{
		if (_initialized)
		{
			return;
		}

		_initialized = true;
		RegisterGroup(AttackGroupId, "xj_fabao_attack_group", "#F2A86B");
		RegisterGroup(SupportGroupId, "xj_fabao_defense_support_group", "#D7A8FF");
		RegisterGroup(XjHelmetGroupId, "xj_equipment_helmet_group", "#B7C7D9");
		RegisterGroup(XjArmorGroupId, "xj_equipment_armor_group", "#B7C7D9");
		RegisterGroup(XjBootsGroupId, "xj_equipment_boots_group", "#B7C7D9");
		RegisterGroup(XjRingGroupId, "xj_equipment_ring_group", "#B7C7D9");
		RegisterGroup(XjAmuletGroupId, "xj_equipment_amulet_group", "#B7C7D9");

		for (int i = 0; i < FaBaoDefinitions.Length; i++)
		{
			RegisterEquipment(FaBaoDefinitions[i]);
		}
	}

	internal static bool TryResolveKind(string itemId, out string kind, out string role)
	{
		kind = string.Empty;
		role = string.Empty;
		if (!TryGetDefinition(itemId, out XjFaBaoEquipmentDefinition definition))
		{
			return false;
		}

		kind = definition.Kind;
		role = definition.Role;
		return true;
	}

	internal static bool TryGetDefinition(string itemId, out XjFaBaoEquipmentDefinition definition)
	{
		definition = default;
		string id = (itemId ?? string.Empty).Trim();
		if (id.Length == 0)
		{
			return false;
		}

		for (int i = 0; i < FaBaoDefinitions.Length; i++)
		{
			if (string.Equals(FaBaoDefinitions[i].Id, id, StringComparison.Ordinal))
			{
				definition = FaBaoDefinitions[i];
				return true;
			}
		}
		return false;
	}

	internal static bool TryPickAssetId(string kind, long ownerId, string salt, out string assetId)
	{
		assetId = string.Empty;
		string normalizedKind = (kind ?? string.Empty).Trim();
		if (normalizedKind.Length == 0)
		{
			return false;
		}

		int count = 0;
		for (int i = 0; i < FaBaoDefinitions.Length; i++)
		{
			XjFaBaoEquipmentDefinition definition = FaBaoDefinitions[i];
			if (definition.EquipmentType == EquipmentType.Weapon
				&& string.Equals(definition.Kind, normalizedKind, StringComparison.Ordinal))
			{
				count++;
			}
		}
		if (count <= 0)
		{
			return false;
		}

		int selected = PositiveIndex(ownerId, salt, count);
		int seen = 0;
		for (int i = 0; i < FaBaoDefinitions.Length; i++)
		{
			XjFaBaoEquipmentDefinition definition = FaBaoDefinitions[i];
			if (definition.EquipmentType != EquipmentType.Weapon
				|| !string.Equals(definition.Kind, normalizedKind, StringComparison.Ordinal))
			{
				continue;
			}
			if (seen++ == selected)
			{
				assetId = definition.Id;
				return true;
			}
		}
		return false;
	}

	internal static bool TryPickSlotAssetId(
		EquipmentType equipmentType,
		string daoTu,
		long ownerId,
		string salt,
		out string assetId)
	{
		assetId = string.Empty;
		if (!IsCultivatorSlotType(equipmentType))
		{
			return false;
		}

		string branch = ResolveBranchFromDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(branch))
		{
			return false;
		}

		int count = 0;
		for (int i = 0; i < FaBaoDefinitions.Length; i++)
		{
			XjFaBaoEquipmentDefinition definition = FaBaoDefinitions[i];
			if (definition.EquipmentType == equipmentType
				&& string.Equals(definition.Branch, branch, StringComparison.Ordinal))
			{
				count++;
			}
		}
		if (count <= 0)
		{
			return false;
		}

		int selected = PositiveIndex(ownerId, salt, count);
		int seen = 0;
		for (int i = 0; i < FaBaoDefinitions.Length; i++)
		{
			XjFaBaoEquipmentDefinition definition = FaBaoDefinitions[i];
			if (definition.EquipmentType != equipmentType
				|| !string.Equals(definition.Branch, branch, StringComparison.Ordinal))
			{
				continue;
			}
			if (seen++ == selected)
			{
				assetId = definition.Id;
				return true;
			}
		}
		return false;
	}

	internal static bool TryPickIconPath(string kind, long ownerId, string salt, out string iconPath)
	{
		iconPath = string.Empty;
		if (!TryPickAssetId(kind, ownerId, salt, out string assetId)
			|| !TryGetDefinition(assetId, out XjFaBaoEquipmentDefinition definition))
		{
			return false;
		}

		iconPath = BuildIconPath(definition.SpriteFolder, definition.SpriteId);
		return true;
	}

	internal static bool IsXuanJianEquipmentAsset(string itemId)
	{
		return TryGetDefinition(itemId, out _);
	}

	internal static bool IsXuanJianFaBaoAsset(string itemId)
	{
		return TryGetDefinition(itemId, out _);
	}

	internal static bool IsCultivatorSlotFaBaoAsset(string itemId)
	{
		return TryGetDefinition(itemId, out XjFaBaoEquipmentDefinition definition)
			&& IsCultivatorSlotType(definition.EquipmentType);
	}

	internal static bool IsLegacyNormalAsset(string itemId)
	{
		string id = (itemId ?? string.Empty).Trim();
		return id.StartsWith(LegacyNormalPrefix, StringComparison.Ordinal)
			|| id.StartsWith(LegacyRuntimeForgedPrefix, StringComparison.Ordinal);
	}

	internal static bool TryGetOwnIcon(EquipmentAsset asset, out Sprite sprite)
	{
		sprite = null;
		if (asset == null)
		{
			return false;
		}

		string id = ((Asset)asset).id ?? string.Empty;
		if (!IsXuanJianEquipmentAsset(id))
		{
			return false;
		}

		string path = ((BaseUnlockableAsset)asset).path_icon ?? string.Empty;
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		sprite = SpriteTextureLoader.getSprite(path)
			?? SpriteTextureLoader.getSprite("GameResources/" + path);
		return sprite != null;
	}

	internal static bool IsCultivatorSlotType(EquipmentType equipmentType)
	{
		return equipmentType == EquipmentType.Helmet
			|| equipmentType == EquipmentType.Armor
			|| equipmentType == EquipmentType.Boots
			|| equipmentType == EquipmentType.Ring
			|| equipmentType == EquipmentType.Amulet;
	}

	internal static bool IsCoveredEquipmentType(EquipmentType equipmentType)
	{
		return equipmentType == EquipmentType.Weapon || IsCultivatorSlotType(equipmentType);
	}

	internal static bool IsVanillaEquipmentAsset(EquipmentAsset asset)
	{
		if (asset == null || !IsCoveredEquipmentType(asset.equipment_type))
		{
			return false;
		}

		string id = (((Asset)asset).id ?? string.Empty).Trim();
		return id.StartsWith("$", StringComparison.Ordinal)
			|| VanillaEquipmentIds.Contains(id);
	}

	internal static bool ShouldHideVanillaEditorAsset(EquipmentAsset asset)
	{
		if (asset == null)
		{
			return false;
		}

		string id = ((Asset)asset).id ?? string.Empty;
		if (IsXuanJianEquipmentAsset(id))
		{
			return false;
		}
		return IsLegacyNormalAsset(id) || IsVanillaEquipmentAsset(asset);
	}

	internal static bool IsMatchingDaoTuAsset(EquipmentAsset asset, string daoTu)
	{
		if (asset == null)
		{
			return false;
		}

		string id = ((Asset)asset).id ?? string.Empty;
		if (!TryGetDefinition(id, out XjFaBaoEquipmentDefinition definition))
		{
			return false;
		}
		if (!IsCultivatorSlotType(definition.EquipmentType))
		{
			return true;
		}

		string branch = ResolveBranchFromDaoTu(daoTu);
		return !string.IsNullOrWhiteSpace(branch)
			&& string.Equals(definition.Branch, branch, StringComparison.Ordinal);
	}

	internal static string ResolveBranchFromDaoTu(string daoTu)
	{
		return XjFaBaoCatalog.ResolveDaoTuCategory(daoTu) switch
		{
			"三阳" => "sanyang",
			"三阴" => "sanyin",
			"三雷" => "sanlei",
			"金德" => "jinde",
			"木德" => "mude",
			"水德" => "shuide",
			"火德" => "huode",
			"土德" => "tude",
			"十二炁" => "shierqi",
			"并古" => "binggu",
			_ => string.Empty
		};
	}

	internal static string ResolveSlotKind(EquipmentType equipmentType)
	{
		return XjLingZhuangNameLibrary.ResolveKind(equipmentType);
	}

	private static int PositiveIndex(long seed, string salt, int count)
	{
		if (count <= 1)
		{
			return 0;
		}

		unchecked
		{
			long hash = 1469598103934665603L ^ seed;
			string value = salt ?? string.Empty;
			for (int i = 0; i < value.Length; i++)
			{
				hash ^= value[i];
				hash *= 1099511628211L;
			}
			if (hash == long.MinValue)
			{
				hash = 0L;
			}
			return (int)(Math.Abs(hash) % count);
		}
	}

	private static HashSet<string> BuildVanillaEquipmentIds()
	{
		var result = new HashSet<string>(StringComparer.Ordinal);
		string[] commonMetals = { "copper", "bronze", "iron", "steel", "silver", "mythril", "adamantine" };
		AddVanillaSeries(result, "amulet", commonMetals, "bone");
		AddVanillaSeries(result, "ring", commonMetals, "bone");
		AddVanillaSeries(result, "boots", commonMetals, "leather");
		AddVanillaSeries(result, "armor", commonMetals, "leather");
		AddVanillaSeries(result, "helmet", commonMetals, "leather");
		AddVanillaSeries(result, "bow", commonMetals, "wood");
		AddVanillaSeries(result, "sword", commonMetals, "wood", "stone");
		AddVanillaSeries(result, "spear", commonMetals, "wood", "stone");
		AddVanillaSeries(result, "axe", commonMetals, "wood", "stone");
		AddVanillaSeries(result, "hammer", commonMetals, "wood", "stone");
		result.Add("flame_sword");
		result.Add("flame_hammer");
		result.Add("ice_hammer");
		return result;
	}

	private static void AddVanillaSeries(
		HashSet<string> result,
		string prefix,
		IEnumerable<string> commonMaterials,
		params string[] extraMaterials)
	{
		foreach (string material in commonMaterials)
		{
			result.Add(prefix + "_" + material);
		}
		for (int i = 0; i < extraMaterials.Length; i++)
		{
			result.Add(prefix + "_" + extraMaterials[i]);
		}
	}

	private static XjFaBaoEquipmentDefinition[] BuildFaBaoDefinitions()
	{
		var result = new List<XjFaBaoEquipmentDefinition>(105);
		AddWeaponSeries(result, "jian", "剑", "xj_fabao_kind_jian");
		AddWeaponSeries(result, "dao", "刀", "xj_fabao_kind_dao");
		AddWeaponSeries(result, "qiang", "枪", "xj_fabao_kind_qiang");
		AddWeaponSeries(result, "gong", "弓", "xj_fabao_kind_gong");
		AddSupportSeries(result, "ding", "鼎", "xj_fabao_kind_ding");
		AddSupportSeries(result, "yin", "印", "xj_fabao_kind_yin");
		AddSupportSeries(result, "zhu", "珠", "xj_fabao_kind_zhu");
		AddSupportSeries(result, "zhong", "钟", "xj_fabao_kind_zhong");
		AddSupportSeries(result, "jian_mirror", "鉴", "xj_fabao_kind_jian_mirror");
		AddSupportSeries(result, "jie", "戒", "xj_fabao_kind_jie");
		AddSupportSeries(result, "fan", "幡", "xj_fabao_kind_fan");
		AddSupportSeries(result, "chi", "尺", "xj_fabao_kind_chi");
		AddDaoTuSlotDefinitions(result);
		return result.ToArray();
	}

	private static void AddWeaponSeries(
		List<XjFaBaoEquipmentDefinition> result,
		string idKind,
		string kind,
		string translationKey)
	{
		for (int i = 1; i <= 5; i++)
		{
			string spriteId = "xj_" + idKind + "-" + i;
			result.Add(new XjFaBaoEquipmentDefinition(
				spriteId,
				kind,
				XjFaBaoCatalog.RoleAttack,
				"Attack",
				translationKey,
				AttackGroupId,
				EquipmentType.Weapon,
				spriteId,
				string.Empty));
		}
	}

	private static void AddSupportSeries(
		List<XjFaBaoEquipmentDefinition> result,
		string idKind,
		string kind,
		string translationKey)
	{
		for (int i = 1; i <= 5; i++)
		{
			string spriteId = "xj_" + idKind + "-" + i;
			result.Add(new XjFaBaoEquipmentDefinition(
				spriteId,
				kind,
				XjFaBaoCatalog.RoleSupport,
				"Defense-Support",
				translationKey,
				SupportGroupId,
				EquipmentType.Weapon,
				spriteId,
				string.Empty));
		}
	}

	private static void AddDaoTuSlotDefinitions(List<XjFaBaoEquipmentDefinition> result)
	{
		AddDaoTuSlotSeries(result, "helmet", "灵盔", "Helmets", "Helmet", EquipmentType.Helmet, XjHelmetGroupId);
		AddDaoTuSlotSeries(result, "armor", "灵甲", "Armor", "Armor", EquipmentType.Armor, XjArmorGroupId);
		AddDaoTuSlotSeries(result, "boots", "灵履", "Boot", "Boots", EquipmentType.Boots, XjBootsGroupId);
		AddDaoTuSlotSeries(result, "ring", "灵戒", "ring", "Ring", EquipmentType.Ring, XjRingGroupId);
		AddDaoTuSlotSeries(result, "amulet", "灵链", "Amulet", "Amulet", EquipmentType.Amulet, XjAmuletGroupId);
	}

	private static void AddDaoTuSlotSeries(
		List<XjFaBaoEquipmentDefinition> result,
		string idSlot,
		string kind,
		string folder,
		string fileSuffix,
		EquipmentType equipmentType,
		string groupId)
	{
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, "Sanyang-" + fileSuffix, equipmentType, groupId, "sanyang");
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, "Sanyin-" + fileSuffix, equipmentType, groupId, "sanyin");
		string sanleiSpriteId = folder == "Armor" && fileSuffix == "Armor" ? "Sanlei--Armor" : "Sanlei-" + fileSuffix;
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, sanleiSpriteId, equipmentType, groupId, "sanlei");
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, "Jinde-" + fileSuffix, equipmentType, groupId, "jinde");
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, "Mude-" + fileSuffix, equipmentType, groupId, "mude");
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, "Shuide-" + fileSuffix, equipmentType, groupId, "shuide");
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, "Huode-" + fileSuffix, equipmentType, groupId, "huode");
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, "Tude-" + fileSuffix, equipmentType, groupId, "tude");
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, "Shierqi-" + fileSuffix, equipmentType, groupId, "shierqi");
		// 并古五类灵装使用用户本地提供的独立资源：Binggu-Helmet/Armor/Boots/Ring/Amulet。
		AddDaoTuSlotDefinition(result, idSlot, kind, folder, "Binggu-" + fileSuffix, equipmentType, groupId, "binggu");
	}

	private static void AddDaoTuSlotDefinition(
		List<XjFaBaoEquipmentDefinition> result,
		string idSlot,
		string kind,
		string folder,
		string spriteId,
		EquipmentType equipmentType,
		string groupId,
		string branch)
	{
		string translationKey = XjLingZhuangNameLibrary.BuildAssetTranslationKey(equipmentType, branch);
		result.Add(new XjFaBaoEquipmentDefinition(
			"xj_fabao_" + idSlot + "_" + branch,
			kind,
			XjFaBaoCatalog.RoleDefense,
			folder,
			translationKey,
			groupId,
			equipmentType,
			spriteId,
			branch));
	}

	private static void RegisterGroup(string groupId, string nameKey, string color)
	{
		if (AssetManager.item_groups == null)
		{
			return;
		}

		try
		{
			ItemGroupAsset group = ((AssetLibrary<ItemGroupAsset>)(object)AssetManager.item_groups).get(groupId);
			if (group == null)
			{
				group = new ItemGroupAsset { id = groupId };
				((AssetLibrary<ItemGroupAsset>)(object)AssetManager.item_groups).add(group);
			}
			group.name = nameKey;
			group.color = color;
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴][法宝装备] 分组注册跳过 id=" + groupId + " ex=" + ex.GetType().Name);
		}
	}

	private static void RegisterEquipment(in XjFaBaoEquipmentDefinition definition)
	{
		if (AssetManager.items == null || string.IsNullOrWhiteSpace(definition.Id))
		{
			return;
		}

		try
		{
			EquipmentAsset asset = GetOrCreateEquipmentAsset(in definition);
			if (asset == null)
			{
				return;
			}

			((Asset)asset).id = definition.Id;
			((ItemAsset)asset).material = string.Empty;
			((ItemAsset)asset).translation_key = definition.TranslationKey;
			((BaseAugmentationAsset)asset).group_id = definition.GroupId;
			((ItemAsset)asset).animated = false;
			((ItemAsset)asset).is_pool_weapon = false;
			((BaseUnlockableAsset)asset).unlock(true);
			// 启动资产注册阶段只挂空 BaseStats。具体法宝战斗数值由运行时物品同步统一写入；
			// 这样与 0.6.3 稳定链一致，也避免 ActorStats 尚未完整就绪时 stats.set 空引用。
			((BaseUnlockableAsset)asset).base_stats = new BaseStats();
			((ItemAsset)asset).name_templates = AssetLibrary<EquipmentAsset>.l<string>(new[] { "flame_sword_name" });
			((ItemAsset)asset).equipment_value = 4000;
			((ItemAsset)asset).quality = (Rarity)3;
			((ItemAsset)asset).equipment_type = definition.EquipmentType;
			((ItemAsset)asset).name_class = FaBaoItemClass;
			((BaseUnlockableAsset)asset).path_icon = BuildIconPath(definition.SpriteFolder, definition.SpriteId);
			((ItemAsset)asset).path_gameplay_sprite = definition.EquipmentType == EquipmentType.Weapon
				? BuildGameplaySpritePath(definition.SpriteFolder, definition.SpriteId)
				: string.Empty;
			((ItemAsset)asset).gameplay_sprites = definition.EquipmentType == EquipmentType.Weapon
				? LoadSprites(definition.SpriteFolder, definition.SpriteId)
				: Array.Empty<Sprite>();
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴][法宝装备] 注册跳过 id=" + definition.Id + " ex=" + ex.GetType().Name);
		}
	}

	private static EquipmentAsset GetOrCreateEquipmentAsset(in XjFaBaoEquipmentDefinition definition)
	{
		ItemAsset existing = (ItemAsset)(object)((AssetLibrary<EquipmentAsset>)(object)AssetManager.items).get(definition.Id);
		if (existing is EquipmentAsset equipment)
		{
			return equipment;
		}

		string templateId = ResolveCloneTemplate(in definition);
		EquipmentAsset template = ((AssetLibrary<EquipmentAsset>)(object)AssetManager.items).get(templateId);
		if (template == null)
		{
			templateId = "$amulet";
		}
		return ((AssetLibrary<EquipmentAsset>)(object)AssetManager.items).clone(definition.Id, templateId);
	}

	private static string ResolveCloneTemplate(in XjFaBaoEquipmentDefinition definition)
	{
		return definition.EquipmentType switch
		{
			EquipmentType.Helmet => "helmet_steel",
			EquipmentType.Armor => "armor_steel",
			EquipmentType.Boots => "boots_steel",
			EquipmentType.Ring => "ring_steel",
			EquipmentType.Amulet => "amulet_steel",
			EquipmentType.Weapon when string.Equals(definition.Kind, "枪", StringComparison.Ordinal) => "spear_steel",
			EquipmentType.Weapon when string.Equals(definition.Kind, "弓", StringComparison.Ordinal) => "bow_steel",
			EquipmentType.Weapon when string.Equals(definition.Role, XjFaBaoCatalog.RoleSupport, StringComparison.Ordinal) => "hammer_steel",
			EquipmentType.Weapon => "sword_steel",
			_ => "$amulet"
		};
	}

	private static string BuildIconPath(string folder, string spriteId)
	{
		return "item/Arts/Equipment/" + folder + "/" + spriteId;
	}

	private static string BuildGameplaySpritePath(string folder, string spriteId)
	{
		return "item/Arts/Equipment/" + folder + "/" + spriteId;
	}

	private static Sprite[] LoadSprites(string folder, string spriteId)
	{
		string path = "item/Arts/Equipment/" + folder + "/" + spriteId;
		Sprite sprite = SpriteTextureLoader.getSprite(path)
			?? SpriteTextureLoader.getSprite("GameResources/" + path);
		return sprite == null ? Array.Empty<Sprite>() : new[] { sprite };
	}
}

internal readonly struct XjFaBaoEquipmentDefinition
{
	internal readonly string Id;
	internal readonly string Kind;
	internal readonly string Role;
	internal readonly string SpriteFolder;
	internal readonly string TranslationKey;
	internal readonly string GroupId;
	internal readonly EquipmentType EquipmentType;
	internal readonly string SpriteId;
	internal readonly string Branch;

	internal XjFaBaoEquipmentDefinition(
		string id,
		string kind,
		string role,
		string spriteFolder,
		string translationKey,
		string groupId,
		EquipmentType equipmentType,
		string spriteId,
		string branch)
	{
		Id = id ?? string.Empty;
		Kind = kind ?? string.Empty;
		Role = role ?? string.Empty;
		SpriteFolder = spriteFolder ?? string.Empty;
		TranslationKey = translationKey ?? string.Empty;
		GroupId = groupId ?? string.Empty;
		EquipmentType = equipmentType;
		SpriteId = string.IsNullOrWhiteSpace(spriteId) ? Id : spriteId.Trim();
		Branch = branch ?? string.Empty;
	}
}
