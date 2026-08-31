using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.FaBao;

/// <summary>
/// 修炼者专属槽位法器/法宝炼制链。
/// 不接管原版城市材料锻造，不转换普通装备；筑基可炼槽位法器，金丹可炼槽位法宝。
/// 紫府灵宝只允许生成攻击或辅助类本命器，不再炼制头盔、盔甲、靴子、戒指、项链等防具槽灵宝。
/// </summary>
internal static class XjEquipmentForgeConsumer
{
	private const string SlotForgeSource = "CultivatorSlotRefine";
	private const string SlotUpgradeSource = "CultivatorSlotUpgrade";
	private const string EditorGrantSource = "EquipmentEditorGrant";
	private const string LongShuBirthSource = "LongShuBirth";
	private const string WarehouseSurplusSource = "WarehouseSurplusCraft";
	private const int NativeTrainingIntervalYears = 1;
	private static int _controlledEquipmentChangeDepth;
	private static readonly HashSet<long> DeathEquipmentReleaseActorIds = new HashSet<long>();


	internal static bool IsControlledEquipmentChange => _controlledEquipmentChangeDepth > 0;

	/// <summary>
	/// 旧存档可能已经由旧版重复炼器链生成了超额槽位灵宝。
	/// 按境界上限保留主武器与较高品阶、较早生成的槽位器物，清理其余超额项。
	/// </summary>
	internal static void ReconcileManagedItemLimit(Actor actor, string realmId)
	{
		if (actor?.data == null || actor.equipment == null) return;
		int limit = XjFaBaoForgePolicy.ResolveManagedItemLimit(realmId);
		if (limit <= 0) return;

		int primaryCount = XjFaBaoAccessor.HasState(actor) ? 1 : 0;
		int allowedSlotCount = Math.Max(0, limit - primaryCount);
		List<ManagedSlotEntry> entries = new List<ManagedSlotEntry>(CultivatorSlotTypes.Length);
		for (int i = 0; i < CultivatorSlotTypes.Length; i++)
		{
			EquipmentType type = CultivatorSlotTypes[i];
			ActorEquipmentSlot slot = actor.equipment.getSlot(type);
			Item item = slot?.getItem();
			if (item?.data == null || !XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState state)) continue;
			entries.Add(new ManagedSlotEntry(type, slot, item, state));
		}
		if (entries.Count <= allowedSlotCount) return;

		entries.Sort(static (left, right) =>
		{
			int classCompare = ClassPriority(right.State.ClassName).CompareTo(ClassPriority(left.State.ClassName));
			if (classCompare != 0) return classCompare;
			int yearCompare = Math.Max(0, left.State.Year).CompareTo(Math.Max(0, right.State.Year));
			if (yearCompare != 0) return yearCompare;
			return ((int)left.Type).CompareTo((int)right.Type);
		});

		BeginControlledEquipmentChange();
		try
		{
			for (int i = allowedSlotCount; i < entries.Count; i++)
			{
				ManagedSlotEntry entry = entries[i];
				if (!ReferenceEquals(entry.Slot?.getItem(), entry.Item)) continue;
				entry.Slot.takeAwayItem();
				RemoveItem(entry.Item);
			}
		}
		finally
		{
			EndControlledEquipmentChange();
		}
		actor.setStatsDirty();
		XjFaBaoBonusService.Forget(((BaseSystemData)actor.data).id);
		RefreshFaBaoRuntimeInterest(actor);
	}

	internal static void BeginControlledEquipmentChange()
	{
		_controlledEquipmentChangeDepth++;
	}

	internal static void EndControlledEquipmentChange()
	{
		if (_controlledEquipmentChangeDepth > 0)
		{
			_controlledEquipmentChangeDepth--;
		}
	}

	internal static void BeginDeathEquipmentRelease(Actor actor)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !DeathEquipmentReleaseActorIds.Add(actorId)) return;
		BeginControlledEquipmentChange();
	}

	internal static void EndDeathEquipmentRelease(Actor actor)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !DeathEquipmentReleaseActorIds.Remove(actorId)) return;
		EndControlledEquipmentChange();
	}

	internal static bool IsBoundPrimaryRemovalLocked(Item item)
	{
		if (!XjFaBaoEquipmentSync.IsBoundPrimaryWeapon(item) || item?.data == null) return false;
		item.data.get("xuanjian.fabao.owner_id", out long ownerId, 0L);
		return ownerId > 0L;
	}

	internal static bool IsLivingOwnerLocked(Item item)
	{
		if (item?.data == null)
		{
			return false;
		}

		item.data.get("xuanjian.fabao", out int marker, 0);
		if (marker != 1)
		{
			return false;
		}

		item.data.get("xuanjian.fabao.owner_id", out long ownerId, 0L);
		return ownerId > 0L
			&& XjScheduler.ResolveActor(ownerId, out Actor owner)
			&& XjSafeCore.IsAliveActor(owner);
	}

	private static readonly EquipmentType[] CultivatorSlotTypes =
	{
		EquipmentType.Helmet,
		EquipmentType.Armor,
		EquipmentType.Boots,
		EquipmentType.Ring,
		EquipmentType.Amulet
	};

	private static readonly EquipmentType[] LongShuBirthEquipmentTypes =
	{
		EquipmentType.Weapon,
		EquipmentType.Helmet,
		EquipmentType.Armor,
		EquipmentType.Boots,
		EquipmentType.Ring,
		EquipmentType.Amulet
	};

	private static readonly EquipmentType[] NativeNamingSlotTypes =
	{
		EquipmentType.Weapon,
		EquipmentType.Helmet,
		EquipmentType.Armor,
		EquipmentType.Boots,
		EquipmentType.Ring,
		EquipmentType.Amulet
	};

	private static readonly EquipmentType[] OptionalNativeNamingSlotTypes = BuildOptionalNativeNamingSlotTypes();

	private static readonly NativeTrainingEquipmentEntry[] NativeTrainingEquipment =
	{
		new NativeTrainingEquipmentEntry(EquipmentType.Weapon, "sword_iron"),
		new NativeTrainingEquipmentEntry(EquipmentType.Weapon, "spear_iron"),
		new NativeTrainingEquipmentEntry(EquipmentType.Weapon, "bow_wood"),
		new NativeTrainingEquipmentEntry(EquipmentType.Helmet, "helmet_leather"),
		new NativeTrainingEquipmentEntry(EquipmentType.Armor, "armor_leather"),
		new NativeTrainingEquipmentEntry(EquipmentType.Boots, "boots_leather"),
		new NativeTrainingEquipmentEntry(EquipmentType.Ring, "ring_copper"),
		new NativeTrainingEquipmentEntry(EquipmentType.Amulet, "amulet_bone")
	};

	/// <summary>
	/// 成胎成功时补齐“本命武器 + 五个修士装备槽”的全套金丹品质道途装备。
	/// 已有仙器/金丹法宝保持原物；紫府灵宝原位升格；更低品质或原生装备直接替换。
	/// 该入口只在真实成胎事务调用，不作为年度维护，避免掉装后被无限自动补回。
	/// </summary>
	internal static int EnsureDaoTaiJinDanEquipmentSet(Actor actor, string daoTu, int currentYear)
	{
		if (actor?.data == null
			|| actor.equipment == null
			|| XjCultivationPathRules.IsShi(actor)
			|| XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierDaoTai
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return 0;
		}

		int year = Math.Max(1, currentYear);
		int ensured = XjFaBaoAcquisition.EnsurePrimaryJinDanForDaoTaiAscension(actor, daoTu, year) ? 1 : 0;
		for (int i = 0; i < CultivatorSlotTypes.Length; i++)
		{
			if (EnsureDaoTaiJinDanSlot(actor, CultivatorSlotTypes[i], daoTu, year, i + 1)) ensured++;
		}

		actor.setStatsDirty();
		long actorId = ((BaseSystemData)actor.data).id;
		XjFaBaoBonusService.Forget(actorId);
		RefreshFaBaoRuntimeInterest(actor);
		return ensured;
	}

	private static bool EnsureDaoTaiJinDanSlot(Actor actor, EquipmentType equipmentType, string daoTu, int currentYear, int ordinal)
	{
		ActorEquipmentSlot slot = actor.equipment?.getSlot(equipmentType);
		if (slot == null) return false;
		Item current = slot.getItem();
		if (current?.data != null && XjFaBaoEquipmentSync.TryReadFaBaoState(current, out XjFaBaoState existing))
		{
			if (XjFaBaoCatalog.IsJinDanFaBao(existing.ClassName) || XjFaBaoCatalog.IsXianQi(existing.ClassName)) return true;
			if (XjFaBaoCatalog.IsZiFuLingBao(existing.ClassName))
			{
				return TryUpgradeSlotLingBao(actor, equipmentType, current, existing, daoTu, currentYear,
					XjFaBaoAcquisition.SourceDaoTaiAscensionGrant);
			}
		}

		long actorId = ((BaseSystemData)actor.data).id;
		string salt = actorId + "|daotai_ascension|" + equipmentType + "|" + ordinal + "|" + daoTu;
		if (!XjFaBaoEquipmentAssets.TryPickSlotAssetId(equipmentType, daoTu, actorId, salt, out string assetId)) return false;
		EquipmentAsset asset = GetEquipmentAsset(assetId);
		string kind = XjFaBaoEquipmentAssets.ResolveSlotKind(equipmentType);
		string[] suffixes = XjLingZhuangNameLibrary.GetNameSuffixes(equipmentType);
		if (asset == null
			|| string.IsNullOrWhiteSpace(kind)
			|| suffixes.Length == 0
			|| !XjFaBaoAcquisition.TryCreateGeneratedState(
				actor, daoTu, XjFaBaoCatalog.JinDanFaBaoClass, XjFaBaoAcquisition.SourceDaoTaiAscensionGrant,
				currentYear, BuildForgeOrdinal(actorId, salt), out XjFaBaoState created,
				kind, XjFaBaoCatalog.RoleDefense, string.Empty, suffixes)
			|| !TryGenerateManagedItem(asset, actor.kingdom, actor.getName(), 0, actor, currentYear, out Item item))
		{
			return false;
		}

		XjFaBaoEquipmentSync.ApplyFaBaoItemData(item, created);
		item.data.set("xuanjian.fabao.owner_id", actorId);
		return TryEquipSlotItem(actor, equipmentType, item);
	}

	/// <summary>
	/// 龙属出生时无视炼器资格与资源消耗，补足三件随机紫府灵宝。
	/// 旧存档每年维护时也会调用；已有器物计入数量，因此该方法幂等。
	/// </summary>
	internal static int EnsureLongShuBirthLingBaoSet(Actor actor, string daoTu, int currentYear, int targetCount = 3)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| actor.equipment == null
			|| string.IsNullOrWhiteSpace(daoTu)
			|| targetCount <= 0)
		{
			return 0;
		}

		int safeTarget = Math.Min(3, targetCount);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return 0;
		}

		// 先把旧存档中已装备但尚未回写主状态的武器灵宝导入，避免重复生成武器。
		XjFaBaoEquipmentSync.TryEnsureGeneratedEquipment(actor);
		int managedCount = CountManagedItems(actor);
		if (managedCount >= safeTarget)
		{
			return managedCount;
		}

		List<EquipmentType> candidates = new List<EquipmentType>(LongShuBirthEquipmentTypes.Length);
		for (int i = 0; i < LongShuBirthEquipmentTypes.Length; i++)
		{
			EquipmentType type = LongShuBirthEquipmentTypes[i];
			if (!HasManagedItemOfType(actor, type))
			{
				candidates.Add(type);
			}
		}

		int ordinal = 0;
		while (managedCount < safeTarget && candidates.Count > 0)
		{
			int pick = XjDeterministicHash.PositiveIndex(
				actorId + Math.Max(0, currentYear) + ordinal * 97L,
				"longshu.birth.lingbao|" + daoTu,
				candidates.Count);
			EquipmentType type = candidates[pick];
			candidates.RemoveAt(pick);

			bool created = type == EquipmentType.Weapon
				? TryGrantLongShuPrimaryLingBao(actor, daoTu, currentYear, ordinal)
				: TryGrantLongShuSlotLingBao(actor, type, daoTu, currentYear, ordinal);
			if (created)
			{
				managedCount++;
			}
			ordinal++;
		}

		actor.setStatsDirty();
		XjFaBaoBonusService.Forget(actorId);
		RefreshFaBaoRuntimeInterest(actor);
		return managedCount;
	}

	private static bool TryGrantLongShuPrimaryLingBao(Actor actor, string daoTu, int currentYear, int ordinal)
	{
		if (XjFaBaoAccessor.HasState(actor)
			|| !XjFaBaoAcquisition.TryCreateGeneratedState(
				actor,
				daoTu,
				XjFaBaoCatalog.ZiFuLingBaoClass,
				LongShuBirthSource,
				Math.Max(0, currentYear),
				ordinal,
				out XjFaBaoState state,
				forcedRole: XjFaBaoCatalog.RoleAttack))
		{
			return false;
		}

		XjFaBaoAcquisition.WriteAndPublish(actor, state);
		return XjFaBaoAccessor.HasState(actor);
	}

	private static bool TryGrantLongShuSlotLingBao(
		Actor actor,
		EquipmentType equipmentType,
		string daoTu,
		int currentYear,
		int ordinal)
	{
		ActorEquipmentSlot slot = actor.equipment?.getSlot(equipmentType);
		if (slot == null || HasManagedItemOfType(actor, equipmentType))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		string salt = actorId + "|longshu_birth|" + equipmentType + "|" + ordinal;
		if (!XjFaBaoEquipmentAssets.TryPickSlotAssetId(equipmentType, daoTu, actorId, salt, out string assetId))
		{
			return false;
		}

		EquipmentAsset asset = GetEquipmentAsset(assetId);
		string kind = XjFaBaoEquipmentAssets.ResolveSlotKind(equipmentType);
		string[] suffixes = XjLingZhuangNameLibrary.GetNameSuffixes(equipmentType);
		if (asset == null
			|| string.IsNullOrWhiteSpace(kind)
			|| suffixes.Length == 0
			|| !XjFaBaoAcquisition.TryCreateGeneratedState(
				actor,
				daoTu,
				XjFaBaoCatalog.ZiFuLingBaoClass,
				LongShuBirthSource,
				Math.Max(0, currentYear),
				BuildForgeOrdinal(actorId, salt),
				out XjFaBaoState state,
				kind,
				XjFaBaoCatalog.RoleDefense,
				string.Empty,
				suffixes)
			|| !TryGenerateManagedItem(
				asset, actor.kingdom, actor.getName(), 0, actor, Math.Max(0, currentYear), out Item item))
		{
			return false;
		}

		XjFaBaoEquipmentSync.ApplyFaBaoItemData(item, state);
		item.data.set("xuanjian.fabao.owner_id", actorId);
		return TryEquipSlotItem(actor, equipmentType, item);
	}

	private static int CountManagedItems(Actor actor)
	{
		int count = XjFaBaoAccessor.HasState(actor) ? 1 : 0;
		for (int i = 0; i < CultivatorSlotTypes.Length; i++)
		{
			if (HasManagedItemOfType(actor, CultivatorSlotTypes[i]))
			{
				count++;
			}
		}
		return count;
	}

	private static bool HasManagedItemOfType(Actor actor, EquipmentType equipmentType)
	{
		if (equipmentType == EquipmentType.Weapon)
		{
			return XjFaBaoAccessor.HasState(actor);
		}

		Item item = actor?.equipment?.getSlot(equipmentType)?.getItem();
		return item?.data != null && XjFaBaoEquipmentSync.TryReadFaBaoState(item, out _);
	}

	private static void RefreshFaBaoRuntimeInterest(Actor actor)
	{
		if (actor?.data != null)
		{
			XjRuntimeActorInterestIndex.Observe(actor);
		}
	}

	internal static void TryForgeAnnual(Actor actor, string realmId, int currentYear)
	{
		XjCraftTraitRules.NormalizeExclusive(actor);
		if (actor?.data == null
			|| actor.equipment == null
			|| currentYear <= 0
			|| !XjCraftTraitRules.CanRefineArtifacts(actor))
		{
			return;
		}

		NormalizeNativeEquipmentNames(actor);
		string forgeRealmId = XjFaBaoForgePolicy.ResolvePracticeRealmId(actor, realmId);
		if (IsNativeTrainingRealm(forgeRealmId))
		{
			TryForgeNativeTrainingEquipment(actor, forgeRealmId, currentYear);
			return;
		}

		if (string.IsNullOrWhiteSpace(forgeRealmId)
			|| !TryResolveForgeClass(forgeRealmId, out string className)
			|| !XjFaBaoForgePolicy.CanAttemptScheduled(actor, forgeRealmId, currentYear))
		{
			return;
		}

		string daoTu = ReadDaoTu(actor);
		if (string.IsNullOrWhiteSpace(daoTu)
			|| string.IsNullOrWhiteSpace(XjFaBaoEquipmentAssets.ResolveBranchFromDaoTu(daoTu)))
		{
			return;
		}

		// 真人的本命灵宝仍由三神通后的个人炼宝链完成，炼器师不批量制造
		// 第二件本命灵宝，也不将紫府灵物炼成防具。紫府级炼器师改为消耗
		// 既有先天之气，为家族或宗门器库稳定补充可传承法器。
		if (string.Equals(forgeRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			TryForgeSurplusForWarehouse(
				actor,
				XjRealmIds.ZhuJi,
				daoTu,
				XjFaBaoCatalog.ZhuJiFaQiClass,
				currentYear);
			return;
		}

		bool allowNew = XjFaBaoForgePolicy.CanCreateNewManagedItem(actor, forgeRealmId);
		int candidateCount = 0;
		for (int i = 0; i < CultivatorSlotTypes.Length; i++)
		{
			EquipmentType equipmentType = CultivatorSlotTypes[i];
			ActorEquipmentSlot slot = actor.equipment.getSlot(equipmentType);
			if (slot == null) continue;
			Item currentItem = slot.getItem();
			if (NeedsForgeOrUpgrade(currentItem, equipmentType, daoTu, className, allowNew)) candidateCount++;
			else NormalizeExistingSlotItem(actor, equipmentType, currentItem, daoTu);
		}
		if (candidateCount == 0)
		{
			TryForgeSurplusForWarehouse(actor, forgeRealmId, daoTu, className, currentYear);
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		int targetIndex = XjDeterministicHash.PositiveIndex(
			actorId + currentYear, "equipment_slot_pick|" + forgeRealmId + "|" + daoTu, candidateCount);
		EquipmentType targetType = default;
		bool foundTarget = false;
		for (int i = 0; i < CultivatorSlotTypes.Length; i++)
		{
			EquipmentType equipmentType = CultivatorSlotTypes[i];
			ActorEquipmentSlot slot = actor.equipment.getSlot(equipmentType);
			if (slot == null || !NeedsForgeOrUpgrade(slot.getItem(), equipmentType, daoTu, className, allowNew)) continue;
			if (targetIndex-- > 0) continue;
			targetType = equipmentType;
			foundTarget = true;
			break;
		}
		if (!foundTarget) return;

		int chance;
		string channel;
		bool consumesJinXing = false;
		if (string.Equals(forgeRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			if (!XjArtifactForgeFuel.TryConsumeForZhuJiFaQi(actor, daoTu, out XjArtifactForgeFuelReceipt fuel)) return;
			chance = XjArtifactForgeFuel.ResolveZhuJiFaQiChancePercent(actor, in fuel);
			channel = "equipment_slot_faqi_" + fuel.Kind;
		}
		else if (string.Equals(forgeRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			if (!XjArtifactForgeFuel.TryConsumeForZiFu(actor, daoTu, out XjArtifactForgeFuelReceipt fuel)) return;
			chance = XjArtifactForgeFuel.ResolveZiFuChancePercent(actor, in fuel);
			channel = "equipment_slot_lingbao_" + fuel.Kind;
		}
		else
		{
			if (!XjArtifactForgeFuel.HasJinDanForgeFuel(actor)) return;
			chance = XjFaBaoForgePolicy.ResolveChancePercent(actor, forgeRealmId);
			channel = "equipment_slot_jindan";
			consumesJinXing = true;
		}

		// 资源已投入便视为一次正式炼制；成功与否均进入三年冷却。
		if (!XjFaBaoForgePolicy.TryReserveScheduledAttempt(actor, forgeRealmId, currentYear)) return;
		if (consumesJinXing)
		{
			if (!XjArtifactForgeFuel.TryConsumeForJinDan(actor, out XjArtifactForgeFuelReceipt fuel)) return;
			channel += "_" + fuel.Kind;
		}
		if (!XjFaBaoForgePolicy.RollAnnual(actor, forgeRealmId, currentYear, channel, chance)) return;
		TryForgeSlotItem(actor, targetType, daoTu, className, currentYear, SlotForgeSource);
	}

	private static void TryForgeSurplusForWarehouse(
		Actor actor,
		string forgeRealmId,
		string daoTu,
		string className,
		int currentYear)
	{
		if (!HasWarehouseOwner(actor)
			|| !XjFaBaoForgePolicy.CanAttemptWarehouseForge(actor, forgeRealmId, currentYear))
		{
			return;
		}

		int chance;
		string channel;
		bool consumesJinXing = false;
		if (string.Equals(forgeRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			if (!XjArtifactForgeFuel.TryConsumeForZhuJiFaQi(actor, daoTu, out XjArtifactForgeFuelReceipt fuel)) return;
			chance = XjArtifactForgeFuel.ResolveZhuJiFaQiChancePercent(actor, in fuel);
			channel = "warehouse_faqi_" + fuel.Kind;
		}
		else if (string.Equals(forgeRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			if (!XjArtifactForgeFuel.TryConsumeForZiFu(actor, daoTu, out XjArtifactForgeFuelReceipt fuel)) return;
			chance = XjArtifactForgeFuel.ResolveZiFuChancePercent(actor, in fuel);
			channel = "warehouse_lingbao_" + fuel.Kind;
		}
		else if (string.Equals(forgeRealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			if (!XjArtifactForgeFuel.HasJinDanForgeFuel(actor)) return;
			chance = XjFaBaoForgePolicy.ResolveChancePercent(actor, forgeRealmId);
			channel = "warehouse_fabao";
			consumesJinXing = true;
		}
		else
		{
			return;
		}

		if (!XjFaBaoForgePolicy.TryReserveWarehouseForge(actor, forgeRealmId, currentYear)) return;
		if (consumesJinXing)
		{
			if (!XjArtifactForgeFuel.TryConsumeForJinDan(actor, out XjArtifactForgeFuelReceipt fuel)) return;
			channel += "_" + fuel.Kind;
		}
		if (!XjFaBaoForgePolicy.RollAnnual(actor, forgeRealmId, currentYear, channel, chance)) return;

		long actorId = ((BaseSystemData)actor.data).id;
		int producedCount = XjFaBaoForgePolicy.ReadWarehouseForgeCount(actor, forgeRealmId);
		int pick = XjDeterministicHash.PositiveIndex(
			actorId + currentYear + producedCount * 97L,
			"warehouse_surplus_type|" + forgeRealmId + "|" + daoTu,
			CultivatorSlotTypes.Length);
		EquipmentType equipmentType = CultivatorSlotTypes[pick];
		string kind = XjFaBaoEquipmentAssets.ResolveSlotKind(equipmentType);
		string[] suffixes = XjLingZhuangNameLibrary.GetNameSuffixes(equipmentType);
		int ordinal = BuildForgeOrdinal(
			actorId,
			"warehouse|" + forgeRealmId + "|" + daoTu + "|" + currentYear + "|" + producedCount);
		if (string.IsNullOrWhiteSpace(kind)
			|| suffixes.Length == 0
			|| !XjFaBaoAcquisition.TryCreateGeneratedState(
				actor,
				daoTu,
				className,
				WarehouseSurplusSource,
				currentYear,
				ordinal,
				out XjFaBaoState state,
				kind,
				XjFaBaoCatalog.RoleDefense,
				string.Empty,
				suffixes)
			|| !XjFaBaoAcquisition.TryStoreSurplusCraft(actor, state))
		{
			return;
		}

		XjFaBaoForgePolicy.RecordWarehouseForgeSuccess(actor, forgeRealmId);
		XjCraftProficiencySystem.RecordArtifactSuccess(actor, state.ClassName);
		BroadcastForgeResult(actor, state);
	}

	private static bool HasWarehouseOwner(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L
			&& XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyStableId)
			&& familyStableId > 0L)
		{
			return true;
		}

		long sectId = XjSectRepository.ResolveActorSectId(actor);
		return sectId > 0L && XjSectRepository.TryGetBySectId(sectId, out _);
	}


	/// <summary>
	/// “照剑天心”属于玩家手动干预：点化剑意后必须立刻让角色持有可识别的剑武器。
	/// 普通兵器直接替换为原生剑；若角色已有本命法器、灵宝或法宝，则保留其品阶、
	/// 词条与唯一身份，只把主器重铸为剑形，避免用一柄凡铁剑覆盖高境本命器。
	/// </summary>
	internal static bool EnsureSimulatorSwordWeapon(Actor actor, int currentYear)
	{
		if (!XjSafeCore.IsAliveActor(actor) || actor.equipment == null)
		{
			return false;
		}

		ActorEquipmentSlot weaponSlot = actor.equipment.getSlot(EquipmentType.Weapon);
		if (weaponSlot == null)
		{
			return false;
		}

		Item currentWeapon = weaponSlot.getItem();
		if (currentWeapon?.data != null
			&& string.Equals(
				XjWeaponArtSystem.ResolveItemKindForActor(actor, currentWeapon),
				XjWeaponArtKinds.Sword,
				StringComparison.Ordinal))
		{
			return true;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		int safeYear = currentYear > 0 ? currentYear : Math.Max(1, XjYearTracker.CurrentYear);
		XjFaBaoState primary = XjFaBaoAccessor.BuildState(actor);
		if (primary.Found)
		{
			return TryReforgePrimaryAsSimulatorSword(actor, primary, actorId, safeYear);
		}

		EquipmentAsset swordAsset = GetEquipmentAsset("sword_iron") ?? GetEquipmentAsset("sword_steel");
		if (swordAsset == null
			&& XjFaBaoEquipmentAssets.TryPickAssetId(
				XjWeaponArtKinds.Sword,
				actorId,
				"simulator_sword_fallback",
				out string fallbackAssetId))
		{
			swordAsset = GetEquipmentAsset(fallbackAssetId);
		}
		if (swordAsset == null
			|| !TryGenerateManagedItem(
				swordAsset,
				actor.kingdom,
				actor.getName(),
				0,
				actor,
				safeYear,
				out Item swordItem))
		{
			return false;
		}

		// 原生 sword_* 同时承载刀与剑；角色已被模拟器锁定为剑艺后写入明确器类，
		// 避免后续再由资产歧义随机判成刀。
		swordItem.data.set("xuanjian.fabao.kind", XjWeaponArtKinds.Sword);
		XjNativeEquipmentNamePolicy.TryNormalize(swordItem, EquipmentType.Weapon, actorId);
		if (!TryEquipSlotItem(actor, EquipmentType.Weapon, swordItem))
		{
			return false;
		}

		XjFaBaoBonusService.Forget(actorId);
		RefreshFaBaoRuntimeInterest(actor);
		try { actor.updateStats(); } catch (System.Exception xjCaught646) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/FaBao/XjEquipmentForgeConsumer.cs:646", xjCaught646); }
		return true;
	}

	private static bool TryReforgePrimaryAsSimulatorSword(
		Actor actor,
		in XjFaBaoState primary,
		long actorId,
		int currentYear)
	{
		if (!XjFaBaoEquipmentAssets.TryPickAssetId(
			XjWeaponArtKinds.Sword,
			actorId,
			"simulator_primary_sword|" + (primary.Id ?? string.Empty),
			out string swordAssetId))
		{
			return false;
		}

		EquipmentAsset swordAsset = GetEquipmentAsset(swordAssetId);
		if (swordAsset == null
			|| !TryGenerateManagedItem(
				swordAsset,
				actor.kingdom,
				actor.getName(),
				0,
				actor,
				currentYear,
				out Item swordItem))
		{
			return false;
		}

		string swordName = NormalizeSimulatorSwordName(primary.Name);
		string source = string.IsNullOrWhiteSpace(primary.Source)
			? "LuJiangXianSwordIntent"
			: primary.Source;
		string description = XjFaBaoDescriptionFormatter.BuildGeneratedDescription(
			actor,
			swordName,
			primary.DaoTu,
			primary.ClassName,
			XjWeaponArtKinds.Sword,
			XjFaBaoCatalog.RoleAttack,
			source);
		XjFaBaoState swordState = new XjFaBaoState(
			true,
			primary.Id,
			swordName,
			primary.DaoTu,
			primary.ClassName,
			XjWeaponArtKinds.Sword,
			XjFaBaoCatalog.RoleAttack,
			primary.Affixes,
			description,
			source,
			primary.Year > 0 ? primary.Year : currentYear,
			"Ok");

		XjFaBaoEquipmentSync.ApplyFaBaoItemData(swordItem, swordState, clearNativeModifiers: true);
		swordItem.data.set("xuanjian.fabao.owner_id", actorId);
		if (!TryEquipSlotItem(actor, EquipmentType.Weapon, swordItem))
		{
			return false;
		}

		// 只更新本命器形制，不重复写入“获得法宝”史册与公告。
		XjFaBaoAccessor.WriteState(actor, swordState);
		XjFaBaoBonusService.Forget(actorId);
		RefreshFaBaoRuntimeInterest(actor);
		try { actor.updateStats(); } catch (System.Exception xjCaught716) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/FaBao/XjEquipmentForgeConsumer.cs:716", xjCaught716); }
		return true;
	}

	private static string NormalizeSimulatorSwordName(string name)
	{
		string value = (name ?? string.Empty).Trim();
		if (value.Length == 0)
		{
			return "照心剑";
		}
		if (value.EndsWith("剑", StringComparison.Ordinal))
		{
			return value;
		}

		for (int i = 0; i < XjFaBaoCatalog.WeaponWords.Length; i++)
		{
			string suffix = XjFaBaoCatalog.WeaponWords[i];
			if (!string.IsNullOrWhiteSpace(suffix)
				&& value.EndsWith(suffix, StringComparison.Ordinal))
			{
				return value.Substring(0, value.Length - suffix.Length) + "剑";
			}
		}
		return value + "剑";
	}

	private static void TryForgeNativeTrainingEquipment(Actor actor, string realmId, int currentYear)
	{
		if (actor?.data == null || actor.equipment == null || currentYear <= 0) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjFaBaoLastAttemptYear, out int lastYear)
			&& lastYear > 0
			&& currentYear < lastYear + NativeTrainingIntervalYears)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		int chance = string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal) ? 75 : 60;
		if (XjDeterministicHash.PositiveIndex(actorId + currentYear, "native_equipment_training|" + realmId, 100) >= chance)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoLastAttemptYear, currentYear);
			return;
		}

		// 练手的核心产出是经验，不是无限制造普通装备。装备槽已满时，
		// 仍视为完成一次拆装、锻打或维护练习，避免熟练度被槽位数量硬锁死。
		TryEquipNativeTrainingEquipment(actor, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoLastAttemptYear, currentYear);
		XjCraftProficiencySystem.RecordArtifactTraining(actor, 1);
	}

	private static bool TryEquipNativeTrainingEquipment(Actor actor, int currentYear)
	{
		if (actor?.data == null || actor.equipment == null || NativeTrainingEquipment.Length == 0) return false;

		List<NativeTrainingEquipmentEntry> candidates = new List<NativeTrainingEquipmentEntry>(NativeTrainingEquipment.Length);
		for (int i = 0; i < NativeTrainingEquipment.Length; i++)
		{
			NativeTrainingEquipmentEntry entry = NativeTrainingEquipment[i];
			if (entry.Type == EquipmentType.Weapon
				&& XjWeaponArtSystem.HasBoundKind(actor, out string boundKind)
				&& !XjWeaponArtSystem.IsKindCompatible(boundKind, ResolveNativeTrainingKind(entry.AssetId)))
			{
				continue;
			}
			ActorEquipmentSlot slot = actor.equipment.getSlot(entry.Type);
			if (slot == null || slot.getItem() != null) continue;
			if (GetEquipmentAsset(entry.AssetId) != null)
			{
				candidates.Add(entry);
			}
		}
		if (candidates.Count == 0) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		int pick = XjDeterministicHash.PositiveIndex(actorId + currentYear, "native_equipment_pick", candidates.Count);
		NativeTrainingEquipmentEntry selected = candidates[pick];
		EquipmentAsset asset = GetEquipmentAsset(selected.AssetId);
		if (asset == null
			|| !TryGenerateManagedItem(asset, actor.kingdom, actor.getName(), XjCraftProficiencySystem.GetNativeEquipmentRarityRolls(actor), actor, currentYear, out Item item))
		{
			return false;
		}

		XjNativeEquipmentNamePolicy.TryNormalize(
			item,
			selected.Type,
			((BaseSystemData)actor.data).id);
		return TryEquipSlotItem(actor, selected.Type, item);
	}

	private static void NormalizeNativeEquipmentNames(Actor actor)
	{
		if (actor?.data == null || actor.equipment == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		for (int i = 0; i < NativeNamingSlotTypes.Length; i++)
		{
			EquipmentType type = NativeNamingSlotTypes[i];
			Item item = actor.equipment.getSlot(type)?.getItem();
			if (item?.data != null)
			{
				XjNativeEquipmentNamePolicy.TryNormalize(item, type, actorId);
			}
		}

		for (int i = 0; i < OptionalNativeNamingSlotTypes.Length; i++)
		{
			EquipmentType type = OptionalNativeNamingSlotTypes[i];
			Item item = actor.equipment.getSlot(type)?.getItem();
			if (item?.data != null)
			{
				XjNativeEquipmentNamePolicy.TryNormalize(item, type, actorId);
			}
		}
	}

	private static EquipmentType[] BuildOptionalNativeNamingSlotTypes()
	{
		// build 115的公开枚举通常没有独立手套槽；若宿主或兼容模组提供，
		// 只在类型初始化时解析一次，不在编译期引用不存在的EquipmentType成员。
		List<EquipmentType> result = new List<EquipmentType>(2);
		AddOptionalNativeNamingSlot(result, "Gloves");
		AddOptionalNativeNamingSlot(result, "Glove");
		AddOptionalNativeNamingSlot(result, "Necklace");
		return result.ToArray();
	}

	private static void AddOptionalNativeNamingSlot(List<EquipmentType> target, string slotName)
	{
		if (!Enum.TryParse(slotName, true, out EquipmentType type)
			|| Array.IndexOf(NativeNamingSlotTypes, type) >= 0
			|| target.Contains(type))
		{
			return;
		}
		target.Add(type);
	}

	private static string ResolveNativeTrainingKind(string assetId)
	{
		string id = (assetId ?? string.Empty).ToLowerInvariant();
		if (id.Contains("sword", StringComparison.Ordinal)) return XuanJianVNext.Data.WeaponArt.XjWeaponArtKinds.NativeBladeSword;
		if (id.Contains("spear", StringComparison.Ordinal)) return XuanJianVNext.Data.WeaponArt.XjWeaponArtKinds.Spear;
		if (id.Contains("bow", StringComparison.Ordinal)) return XuanJianVNext.Data.WeaponArt.XjWeaponArtKinds.Bow;
		return string.Empty;
	}

	private static bool IsNativeTrainingRealm(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal);
	}

	internal static bool HasUpgradeableLingBao(Actor actor)
	{
		if (actor?.equipment == null) return false;
		for (int i = 0; i < CultivatorSlotTypes.Length; i++)
		{
			Item item = actor.equipment.getSlot(CultivatorSlotTypes[i])?.getItem();
			if (item?.data != null
				&& XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState state)
				&& XjFaBaoCatalog.IsZiFuLingBao(state.ClassName)) return true;
		}
		return false;
	}

	internal static bool TryUpgradeFirstLingBaoToFaBao(Actor actor, string daoTu, int currentYear, string source)
	{
		if (actor?.data == null || actor.equipment == null) return false;
		for (int i = 0; i < CultivatorSlotTypes.Length; i++)
		{
			EquipmentType type = CultivatorSlotTypes[i];
			Item item = actor.equipment.getSlot(type)?.getItem();
			if (item?.data == null
				|| !XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState state)
				|| !XjFaBaoCatalog.IsZiFuLingBao(state.ClassName)) continue;
			return TryUpgradeSlotLingBao(actor, type, item, state, daoTu, currentYear, source);
		}
		return false;
	}

	private static bool TryForgeSlotItem(
		Actor actor,
		EquipmentType equipmentType,
		string daoTu,
		string className,
		int currentYear,
		string source)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		string salt = actorId + "|" + currentYear + "|" + equipmentType + "|" + daoTu;
		ActorEquipmentSlot currentSlot = actor.equipment?.getSlot(equipmentType);
		Item currentItem = currentSlot?.getItem();
		if (XjFaBaoCatalog.IsJinDanFaBao(className)
			&& currentItem?.data != null
			&& XjFaBaoEquipmentSync.TryReadFaBaoState(currentItem, out XjFaBaoState existingState)
			&& XjFaBaoCatalog.IsZiFuLingBao(existingState.ClassName))
		{
			return TryUpgradeSlotLingBao(actor, equipmentType, currentItem, existingState, daoTu, currentYear, SlotUpgradeSource);
		}

		if (!XjFaBaoEquipmentAssets.TryPickSlotAssetId(equipmentType, daoTu, actorId, salt, out string assetId)) return false;
		EquipmentAsset asset = GetEquipmentAsset(assetId);
		string kind = XjFaBaoEquipmentAssets.ResolveSlotKind(equipmentType);
		string[] nameSuffixes = XjLingZhuangNameLibrary.GetNameSuffixes(equipmentType);
		int ordinal = BuildForgeOrdinal(actorId, salt);
		if (asset == null
			|| string.IsNullOrWhiteSpace(kind)
			|| nameSuffixes.Length == 0
			|| !XjFaBaoAcquisition.TryCreateGeneratedState(
				actor, daoTu, className, source, currentYear, ordinal,
				out XjFaBaoState state, kind, XjFaBaoCatalog.RoleDefense,
				string.Empty, nameSuffixes)
			|| !TryGenerateManagedItem(
				asset, actor.kingdom, actor.getName(), 0, actor, currentYear, out Item item))
		{
			return false;
		}

		XjFaBaoEquipmentSync.ApplyFaBaoItemData(item, state);
		item.data.set("xuanjian.fabao.owner_id", actorId);
		if (!TryEquipSlotItem(actor, equipmentType, item)) return false;
		XjFaBaoBonusService.Forget(actorId);
		RefreshFaBaoRuntimeInterest(actor);
		XjCraftProficiencySystem.RecordArtifactSuccess(actor, state.ClassName);
		BroadcastForgeResult(actor, state);
		return true;
	}

	private static bool TryUpgradeSlotLingBao(
		Actor actor,
		EquipmentType equipmentType,
		Item currentItem,
		in XjFaBaoState existingState,
		string daoTu,
		int currentYear,
		string source)
	{
		if (actor?.data == null || currentItem?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		string salt = actorId + "|" + currentYear + "|" + equipmentType + "|" + daoTu + "|upgrade";
		string upgradeDaoTu = string.IsNullOrWhiteSpace(existingState.DaoTu) ? daoTu : existingState.DaoTu;
		string upgradeKind = string.IsNullOrWhiteSpace(existingState.Kind)
			? XjFaBaoEquipmentAssets.ResolveSlotKind(equipmentType)
			: existingState.Kind;
		string upgradeRole = XjFaBaoCatalog.NormalizeRole(upgradeKind, existingState.Role);
		string upgradeAffixes = XjFaBaoAcquisition.MergeUpgradeAffixes(
			actor, existingState.Affixes, XjFaBaoCatalog.JinDanFaBaoClass,
			upgradeRole, currentYear, BuildForgeOrdinal(actorId, salt));
		string upgradeName = string.IsNullOrWhiteSpace(existingState.Name)
			? actor.getName() + "灵装"
			: existingState.Name;
		string upgradeDescription = XjFaBaoDescriptionFormatter.BuildGeneratedDescription(
			actor, upgradeName, upgradeDaoTu, XjFaBaoCatalog.JinDanFaBaoClass,
			upgradeKind, upgradeRole, source);
		XjFaBaoState upgraded = new XjFaBaoState(
			true,
			string.IsNullOrWhiteSpace(existingState.Id)
				? "slot-upgrade-" + actorId + "-" + equipmentType
				: existingState.Id,
			upgradeName, upgradeDaoTu, XjFaBaoCatalog.JinDanFaBaoClass,
			upgradeKind, upgradeRole, upgradeAffixes, upgradeDescription,
			source, currentYear, "Ok");
		XjFaBaoEquipmentSync.ApplyFaBaoItemData(currentItem, upgraded);
		currentItem.data.set("xuanjian.fabao.owner_id", actorId);
		actor.setStatsDirty();
		XjFaBaoBonusService.Forget(actorId);
		RefreshFaBaoRuntimeInterest(actor);
		if (!string.Equals(source, XjFaBaoAcquisition.SourceDaoTaiAscensionGrant, StringComparison.Ordinal))
		{
			XjCraftProficiencySystem.RecordArtifactSuccess(actor, upgraded.ClassName);
		}
		BroadcastForgeResult(actor, upgraded);
		return true;
	}

	internal static bool CanEquipXuanJianFaBao(Item item, Actor actor)
	{
		if (item?.data == null || actor?.data == null)
		{
			return false;
		}

		EquipmentAsset asset = item.getAsset();
		string assetId = asset == null ? string.Empty : ((Asset)asset).id ?? string.Empty;
		if (XjFaBaoEquipmentAssets.IsLegacyNormalAsset(assetId))
		{
			return false;
		}
		if (!XjFaBaoEquipmentAssets.IsXuanJianFaBaoAsset(assetId))
		{
			return true;
		}

		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!TryResolveForgeClass(realmId, out _))
		{
			return false;
		}

		int realmTier = XjRealmSuppression.GetRealmTier(actor);
		if (XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState itemState))
		{
			if (XjFaBaoCatalog.IsXianQi(itemState.ClassName) && realmTier < XjRealmSuppression.TierDaoTai) return false;
			if (XjFaBaoCatalog.IsJinDanFaBao(itemState.ClassName) && realmTier < XjRealmSuppression.TierJinDan) return false;
			if (XjFaBaoCatalog.IsZiFuLingBao(itemState.ClassName) && realmTier < XjRealmSuppression.TierZiFu) return false;
			if (XjFaBaoCatalog.IsZhuJiFaQi(itemState.ClassName) && realmTier < XjRealmSuppression.TierZhuJi) return false;
		}

		string daoTu = ReadDaoTu(actor);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		if (!XjFaBaoEquipmentAssets.IsMatchingDaoTuAsset(asset, daoTu))
		{
			return false;
		}

		// 系统内部的原位晋升允许替换当前槽位；玩家编辑器与外部装备链则必须遵守
		// “每类一件”和筑基2件/紫府3件/金丹6件的硬上限。
		if (IsControlledEquipmentChange || ReferenceEquals(actor.equipment?.getSlot(asset.equipment_type)?.getItem(), item))
		{
			return true;
		}

		ActorEquipmentSlot slot = actor.equipment?.getSlot(asset.equipment_type);
		Item equipped = slot?.getItem();
		if (equipped?.data != null && XjFaBaoEquipmentSync.TryReadFaBaoState(equipped, out _))
		{
			return false;
		}
		if (asset.equipment_type == EquipmentType.Weapon && XjFaBaoAccessor.HasState(actor))
		{
			return false;
		}

		return XjFaBaoForgePolicy.CanCreateNewManagedItem(actor, realmId);
	}

	private static void BroadcastForgeResult(Actor actor, in XjFaBaoState state)
	{
		if (string.Equals(state.Source, XjFaBaoAcquisition.SourceDaoTaiAscensionGrant, StringComparison.Ordinal)) return;
		if (!XjRuntimeSettings.BroadcastTreasureMilestoneEnabled)
		{
			return;
		}

		try
		{
			XjBroadcastSystem.BroadcastBLevelActorEvent(
				actor,
				XjAnnouncementText.BuildFaBaoResult(actor, state.ClassName, state.Name, state.Source),
				iconId: XjEventIconCatalog.FaBaoCreation,
				category: XjAnnouncementCategory.Treasure);
		}
		catch (System.Exception xjCaught1069) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/FaBao/XjEquipmentForgeConsumer.cs:1069", xjCaught1069); }
	}

	internal static void InitializeGrantedFaBao(Item item, Actor actor)
	{
		if (item?.data == null || actor?.data == null)
		{
			return;
		}

		item.data.get("xuanjian.fabao", out int marker, 0);
		if (marker == 1)
		{
			return;
		}

		EquipmentAsset asset = item.getAsset();
		string assetId = asset == null ? string.Empty : ((Asset)asset).id ?? string.Empty;
		if (!XjFaBaoEquipmentAssets.TryGetDefinition(assetId, out XjFaBaoEquipmentDefinition definition))
		{
			return;
		}

		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!TryResolveForgeClass(realmId, out string className))
		{
			return;
		}

		string daoTu = ReadDaoTu(actor);
		if (string.IsNullOrWhiteSpace(daoTu)
			|| !XjFaBaoEquipmentAssets.IsMatchingDaoTuAsset(asset, daoTu))
		{
			return;
		}

		int year = XjFaBaoAcquisition.GetCurrentYear(actor);
		int grantOrdinal = BuildForgeOrdinal(item.data.id, assetId + "|editor_grant|" + year);
		string[] nameSuffixes = XjFaBaoEquipmentAssets.IsCultivatorSlotType(definition.EquipmentType)
			? XjLingZhuangNameLibrary.GetNameSuffixes(definition.EquipmentType)
			: null;
		if (!XjFaBaoAcquisition.TryCreateGeneratedState(
			actor,
			daoTu,
			className,
			EditorGrantSource,
			year,
			grantOrdinal,
			out XjFaBaoState state,
			definition.Kind,
			definition.Role,
			string.Empty,
			nameSuffixes))
		{
			return;
		}

		XjFaBaoEquipmentSync.ApplyFaBaoItemData(item, state);
		item.data.set("xuanjian.fabao.owner_id", 0L);
		XjFaBaoBonusService.Forget(((BaseSystemData)actor.data).id);
		RefreshFaBaoRuntimeInterest(actor);
	}

	internal static void ClaimUnownedFaBao(Item item, Actor actor)
	{
		if (item?.data == null || actor?.data == null)
		{
			return;
		}

		item.data.get("xuanjian.fabao", out int marker, 0);
		if (marker != 1)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		item.data.get("xuanjian.fabao.owner_id", out long ownerId, 0L);
		if (ownerId > 0L && ownerId != actorId)
		{
			return;
		}

		item.data.set("xuanjian.fabao.owner_id", actorId);
		EquipmentAsset asset = item.getAsset();
		if (asset != null && XjFaBaoEquipmentAssets.IsCultivatorSlotType(asset.equipment_type))
		{
			string daoTu = ReadDaoTu(actor);
			if (!string.IsNullOrWhiteSpace(daoTu))
			{
				NormalizeExistingSlotItem(actor, asset.equipment_type, item, daoTu);
			}
		}

		if (asset?.equipment_type == EquipmentType.Weapon
			&& !XjFaBaoAccessor.HasState(actor)
			&& XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState state))
		{
			XjFaBaoAcquisition.WriteAndPublish(actor, state);
		}
		XjFaBaoBonusService.Forget(actorId);
		RefreshFaBaoRuntimeInterest(actor);
	}

	internal static bool TryGenerateManagedItem(
		EquipmentAsset asset,
		Kingdom kingdom,
		string creator,
		int tries,
		Actor actor,
		int year,
		out Item item)
	{
		item = null;
		if (asset == null || World.world?.items == null)
		{
			return false;
		}

		try
		{
			string safeCreator = string.IsNullOrWhiteSpace(creator) || string.Equals(creator.Trim(), "The Creator", StringComparison.OrdinalIgnoreCase)
				? (actor?.getName() ?? "玄鉴炼器")
				: creator.Trim();
			int safeYear = year > 0 ? year : Math.Max(1, XjYearTracker.CurrentYear);
			item = World.world.items.generateItem(
				asset,
				kingdom ?? actor?.kingdom,
				safeCreator,
				tries,
				actor,
				safeYear,
				false);
			return item != null;
		}
		catch
		{
			item = null;
			return false;
		}
	}

	private static void NormalizeExistingSlotItem(
		Actor actor,
		EquipmentType equipmentType,
		Item item,
		string daoTu)
	{
		if (actor?.data == null
			|| item?.data == null
			|| !XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState state))
		{
			return;
		}

		string kind = XjLingZhuangNameLibrary.ResolveKind(equipmentType);
		if (string.IsNullOrWhiteSpace(kind))
		{
			return;
		}

		string source = string.IsNullOrWhiteSpace(state.Source) ? SlotForgeSource : state.Source;
		string canonicalDaoTu = string.IsNullOrWhiteSpace(state.DaoTu) ? daoTu : state.DaoTu;
		string canonicalName = (state.Name ?? string.Empty).Trim();
		if (!XjLingZhuangNameLibrary.IsGeneratedNameForType(canonicalName, equipmentType))
		{
			long actorId = ((BaseSystemData)actor.data).id;
			int normalizeOrdinal = BuildForgeOrdinal(
				actorId,
				(state.Id ?? string.Empty) + "|lingzhuang_name_migration|" + equipmentType);
			if (!XjFaBaoAcquisition.TryGenerateUniqueLingZhuangName(
				actor,
				canonicalDaoTu,
				state.ClassName,
				source,
				state.Year,
				normalizeOrdinal,
				equipmentType,
				out canonicalName))
			{
				return;
			}
		}
		else
		{
			XjFaBaoAcquisition.RegisterKnownName(canonicalName);
		}
		string canonicalDescription = XjFaBaoDescriptionFormatter.BuildGeneratedDescription(
			actor,
			canonicalName,
			canonicalDaoTu,
			state.ClassName,
			kind,
			XjFaBaoCatalog.RoleDefense,
			source);
		if (string.Equals(state.Name, canonicalName, StringComparison.Ordinal)
			&& string.Equals(state.DaoTu, canonicalDaoTu, StringComparison.Ordinal)
			&& string.Equals(state.Kind, kind, StringComparison.Ordinal)
			&& string.Equals(state.Description, canonicalDescription, StringComparison.Ordinal))
		{
			return;
		}

		XjFaBaoState normalized = new XjFaBaoState(
			true,
			state.Id,
			canonicalName,
			canonicalDaoTu,
			state.ClassName,
			kind,
			XjFaBaoCatalog.RoleDefense,
			state.Affixes,
			canonicalDescription,
			source,
			state.Year,
			"Ok");
		XjFaBaoEquipmentSync.ApplyFaBaoItemData(item, normalized);
	}

	private static bool NeedsForgeOrUpgrade(
		Item item,
		EquipmentType equipmentType,
		string daoTu,
		string targetClass,
		bool allowNew)
	{
		if (item?.data == null)
		{
			return allowNew;
		}

		EquipmentAsset asset = item.getAsset();
		if (asset == null || asset.equipment_type != equipmentType)
		{
			return allowNew;
		}

		// 法器不会直接晋升为灵宝；职业熟练度达到紫府级后，允许另炼一件灵宝
		// 替换已经落后的法器。灵宝晋升法宝仍沿用原有原位晋升链。
		if (XjFaBaoEquipmentSync.TryReadFaBaoItem(item, out _, out string className, out _, out _))
		{
			if (XjFaBaoCatalog.IsZiFuLingBao(targetClass)
				&& XjFaBaoCatalog.IsZhuJiFaQi(className)) return true;
			return XjFaBaoCatalog.IsJinDanFaBao(targetClass)
				&& XjFaBaoCatalog.IsZiFuLingBao(className);
		}

		return allowNew;
	}

	private static bool TryEquipSlotItem(Actor actor, EquipmentType equipmentType, Item item)
	{
		if (actor?.equipment == null || item?.data == null)
		{
			RemoveItem(item);
			return false;
		}

		ActorEquipmentSlot slot = actor.equipment.getSlot(equipmentType);
		if (slot == null)
		{
			RemoveItem(item);
			return false;
		}

		Item oldItem = slot.getItem();
		BeginControlledEquipmentChange();
		try
		{
			if (oldItem != null && !ReferenceEquals(oldItem, item))
			{
				slot.takeAwayItem();
			}

			slot.setItem(item, actor);
			if (!ReferenceEquals(slot.getItem(), item))
			{
				if (oldItem != null && slot.isEmpty())
				{
					slot.setItem(oldItem, actor);
				}
				RemoveItem(item);
				return false;
			}

			if (oldItem != null && !ReferenceEquals(oldItem, item))
			{
				RemoveItem(oldItem);
			}
			actor.setStatsDirty();
			return true;
		}
		catch
		{
			try
			{
				if (oldItem != null && slot.isEmpty())
				{
					slot.setItem(oldItem, actor);
				}
			}
			catch (System.Exception xjCaught1372) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/FaBao/XjEquipmentForgeConsumer.cs:1372", xjCaught1372); }
			RemoveItem(item);
			return false;
		}
		finally
		{
			EndControlledEquipmentChange();
		}
	}

	private static EquipmentAsset GetEquipmentAsset(string assetId)
	{
		if (string.IsNullOrWhiteSpace(assetId) || AssetManager.items == null)
		{
			return null;
		}
		try
		{
			return ((AssetLibrary<EquipmentAsset>)(object)AssetManager.items).get(assetId);
		}
		catch
		{
			return null;
		}
	}

	private static string ReadDaoTu(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			? (daoTu ?? string.Empty).Trim()
			: string.Empty;
	}

	private static bool TryResolveForgeClass(string realmId, out string className)
	{
		className = string.Empty;
		string normalizedRealmId = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(normalizedRealmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalizedRealmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
			|| XjCultivationPathRules.IsJinDanEquivalentRealm(normalizedRealmId))
		{
			className = XjFaBaoCatalog.JinDanFaBaoClass;
			return true;
		}
		if (XjCultivationPathRules.IsZhenRenEquivalentRealm(realmId))
		{
			className = XjFaBaoCatalog.ZiFuLingBaoClass;
			return true;
		}
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			className = XjFaBaoCatalog.ZhuJiFaQiClass;
			return true;
		}
		return false;
	}


	private static int BuildForgeOrdinal(long itemId, string salt)
	{
		int ordinal = PositiveIndex(itemId, salt, int.MaxValue);
		return ordinal <= 0 ? 1 : ordinal;
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

	private static void RemoveItem(Item item)
	{
		if (item == null)
		{
			return;
		}

		try
		{
			City city = item.getCity();
			if (city?.data?.equipment != null && item.data != null)
			{
				EquipmentAsset asset = item.getAsset();
				if (asset != null)
				{
					List<long> ids = city.data.equipment.getEquipmentList(asset.equipment_type);
					if (ids != null)
					{
						for (int i = ids.Count - 1; i >= 0; i--)
						{
							if (ids[i] == item.data.id)
							{
								ids.RemoveAt(i);
							}
						}
					}
				}
			}
			item.clearCity();
		}
		catch (System.Exception xjCaught1489) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/FaBao/XjEquipmentForgeConsumer.cs:1489", xjCaught1489); }

		try
		{
			World.world?.items?.removeObject(item);
		}
		catch (System.Exception xjCaught1497) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/FaBao/XjEquipmentForgeConsumer.cs:1497", xjCaught1497); }
	}

	private static int ClassPriority(string className)
	{
		if (XjFaBaoCatalog.IsXianQi(className)) return 4;
		if (XjFaBaoCatalog.IsJinDanFaBao(className)) return 3;
		if (XjFaBaoCatalog.IsZiFuLingBao(className)) return 2;
		if (XjFaBaoCatalog.IsZhuJiFaQi(className)) return 1;
		return 0;
	}

	private readonly struct ManagedSlotEntry
	{
		internal readonly EquipmentType Type;
		internal readonly ActorEquipmentSlot Slot;
		internal readonly Item Item;
		internal readonly XjFaBaoState State;

		internal ManagedSlotEntry(EquipmentType type, ActorEquipmentSlot slot, Item item, in XjFaBaoState state)
		{
			Type = type;
			Slot = slot;
			Item = item;
			State = state;
		}
	}

	private readonly struct NativeTrainingEquipmentEntry
	{
		internal readonly EquipmentType Type;
		internal readonly string AssetId;

		internal NativeTrainingEquipmentEntry(EquipmentType type, string assetId)
		{
			Type = type;
			AssetId = assetId ?? string.Empty;
		}
	}


}
