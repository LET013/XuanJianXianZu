using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.Systems.FaBao;

internal static class XjFaBaoEquipmentSync
{
	private const string ItemKeyMarker = "xuanjian.fabao";
	private const string ItemKeySchemaVersion = "xuanjian.fabao.schema_version";
	private const int CurrentItemSchemaVersion = 6;
	private const string ItemKeyId = "xuanjian.fabao.id";
	private const string ItemKeyName = "xuanjian.fabao.name";
	private const string ItemKeyClass = "xuanjian.fabao.class";
	private const string ItemKeyDaoTu = "xuanjian.fabao.daotu";
	private const string ItemKeyKind = "xuanjian.fabao.kind";
	private const string ItemKeyRole = "xuanjian.fabao.role";
	private const string ItemKeyAffixes = "xuanjian.fabao.affixes";
	private const string ItemKeyDescription = "xuanjian.fabao.description";
	private const string ItemKeySource = "xuanjian.fabao.source";
	private const string ItemKeyYear = "xuanjian.fabao.year";
	private const string ItemKeyBoundPrimary = "xuanjian.fabao.bound_primary";
	private const string FamilyBorrowSource = "家族借用";

	/// <summary>
	/// Returns whether this actor needs the annual FaBao finalize branch.
	/// Most ZhuJi-and-above actors have neither a refining trait nor existing managed
	/// equipment; keeping them out of the third queue stage removes repeated
	/// state construction and slot synchronization from the hot annual path.
	/// </summary>
	internal static bool HasAnnualInterest(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (XjFaBaoForgePolicy.NeedsPersonalZiFuLingBao(actor)
			|| XjCraftTraitRules.CanRefineArtifacts(actor)
			|| XjFaBaoAccessor.HasState(actor))
		{
			return true;
		}

		if (XjFamilyFaBaoWarehouse.HasFamilyEntries && TryFindBestFamilyFaBao(actor, out _, out _))
		{
			return true;
		}

		if (actor.equipment == null)
		{
			return false;
		}

		// Only an equipped weapon can be imported as the actor's primary FaBao.
		// This cold compatibility check preserves editor-granted and legacy items
		// without scheduling every high-realm non-refiner.
		foreach (ActorEquipmentSlot slot in actor.equipment)
		{
			Item item = slot?.getItem();
			if (item?.getAsset()?.equipment_type != EquipmentType.Weapon)
			{
				continue;
			}

			if (item.data != null)
			{
				item.data.get(ItemKeyMarker, out int marker, 0);
				if (marker == 1)
				{
					return true;
				}
			}

			string itemId = GetSafeEquipmentItemId(item);
			if (XjFaBaoEquipmentAssets.TryResolveKind(itemId, out _, out _))
			{
				return true;
			}
		}

		return false;
	}

	internal static bool TryBorrowFamilyFaBao(Actor actor, string realmId, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0)
		{
			return false;
		}

		if (!TryFindBestFamilyFaBao(actor, out XjFamilyFaBaoWarehouseEntry entry, out int entryRank))
		{
			return false;
		}

		XjFaBaoState current = XjFaBaoAccessor.BuildState(actor);
		if (current.Found && ResolveClassRank(current.ClassName) >= entryRank)
		{
			return false;
		}

		string kind = XjFaBaoAcquisition.ResolveKindFromName(entry.FaBaoName);
		string role = XjFaBaoCatalog.NormalizeRole(kind, XjFaBaoCatalog.ResolveRoleFromWeapon(kind));
		string description = XjFaBaoDescriptionFormatter.BuildGeneratedDescription(
			actor,
			entry.FaBaoName,
			entry.DaoTu,
			entry.ClassName,
			kind,
			role,
			FamilyBorrowSource);
		XjFaBaoState borrowed = new XjFaBaoState(
			true,
			string.IsNullOrWhiteSpace(entry.FaBaoId) ? BuildBorrowedFaBaoId(actor, entry) : entry.FaBaoId,
			entry.FaBaoName,
			entry.DaoTu,
			entry.ClassName,
			kind,
			role,
			string.Empty,
			description,
			FamilyBorrowSource,
			currentYear,
			"Ok");
		XjFaBaoAccessor.WriteState(actor, borrowed);
		TrySyncGeneratedEquipment(actor, borrowed);
		XjAutoCollectSystem.TryCollectFaBaoOwner(actor, "FamilyFaBaoBorrow");
		return true;
	}

	private static bool TryFindBestFamilyFaBao(Actor actor, out XjFamilyFaBaoWarehouseEntry best, out int bestRank)
	{
		best = default;
		bestRank = 0;
		if (actor?.data == null || !XjFamilyFaBaoWarehouse.HasFamilyEntries)
		{
			return false;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
			|| familyId <= 0L
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu)
			|| string.IsNullOrWhiteSpace(actorDaoTu))
		{
			return false;
		}

		int realmRank = ResolveRealmFaBaoRank(actor);
		if (realmRank <= 0)
		{
			return false;
		}

		IReadOnlyList<XjFamilyFaBaoWarehouseEntry> entries = XjFamilyFaBaoWarehouse.ReadFamilyEntries(familyId);
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry candidate = entries[i];
			int candidateRank = ResolveClassRank(candidate.ClassName);
			if (!candidate.Found
				|| candidateRank <= 0
				|| candidateRank > realmRank
				|| string.IsNullOrWhiteSpace(candidate.FaBaoName)
				|| !string.Equals((candidate.DaoTu ?? string.Empty).Trim(), actorDaoTu.Trim(), StringComparison.Ordinal))
			{
				continue;
			}

			if (candidateRank > bestRank
				|| candidateRank == bestRank && candidate.Year > best.Year)
			{
				best = candidate;
				bestRank = candidateRank;
			}
		}

		return bestRank > 0
			&& best.Found
			&& IsHighestEligibleFamilyHolder(actor, familyId, bestRank, actorDaoTu);
	}

	private static bool IsHighestEligibleFamilyHolder(
		Actor actor,
		long familyId,
		int requiredRank,
		string daoTu)
	{
		if (actor?.data == null || familyId <= 0L || requiredRank <= 0 || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L)
		{
			return false;
		}

		int actorRank = ResolveRealmFaBaoRank(actor);
		if (actorRank < requiredRank)
		{
			return false;
		}

		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
		{
			if (member?.data == null || !member.isAlive())
			{
				continue;
			}

			long memberId = GetActorId(member);
			if (memberId <= 0L || memberId == actorId)
			{
				continue;
			}

			if (!XjActorAccessor.TryGetString(member, XjActorDataKeys.DaoTu, out string memberDaoTu)
				|| !string.Equals((memberDaoTu ?? string.Empty).Trim(), daoTu.Trim(), StringComparison.Ordinal))
			{
				continue;
			}

			int memberRank = ResolveRealmFaBaoRank(member);
			if (memberRank < requiredRank)
			{
				continue;
			}

			if (memberRank > actorRank || memberRank == actorRank && memberId < actorId)
			{
				return false;
			}
		}

		return true;
	}

	private static int ResolveRealmFaBaoRank(Actor actor)
	{
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		int order = XjRealmHelper.GetOrder(realmId);
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.JinDan)) return 3;
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) return 2;
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZhuJi)) return 1;
		return 0;
	}

	private static int ResolveClassRank(string className)
	{
		if (XjFaBaoCatalog.IsJinDanFaBao(className)) return 3;
		if (XjFaBaoCatalog.IsZiFuLingBao(className)) return 2;
		if (XjFaBaoCatalog.IsZhuJiFaQi(className)) return 1;
		return 0;
	}

	private static string BuildBorrowedFaBaoId(Actor actor, in XjFamilyFaBaoWarehouseEntry entry)
	{
		return "xj_family_borrow_"
			+ GetActorId(actor).ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "_"
			+ Math.Max(0, entry.Year).ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	internal static void TryImportEquippedFaBao(Actor actor)
	{
		if (actor?.data == null || XjFaBaoAccessor.HasState(actor))
		{
			return;
		}

		if (!TryResolveEquippedFaBaoClass(actor, out string className))
		{
			return;
		}

		if (!TryFindEquippedFaBaoAppearance(actor, out Item item, out string kind, out string role))
		{
			return;
		}

		// 已带完整物品状态的城市锻造法宝，读档后直接继承该状态，不能按持有者道途重造。
		if (TryReadFaBaoState(item, out XjFaBaoState equippedState))
		{
			item.data.get("xuanjian.fabao.owner_id", out long ownerId, 0L);
			long actorId = ((BaseSystemData)actor.data).id;
			if (ownerId <= 0L || ownerId == actorId)
			{
				XjEquipmentForgeConsumer.ClaimUnownedFaBao(item, actor);
				if (!XjFaBaoAccessor.HasState(actor))
				{
					XjFaBaoAcquisition.WriteAndPublish(actor, equippedState);
				}
			}
			return;
		}

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		int year = XjFaBaoAcquisition.GetCurrentYear(actor);
		if (!XjFaBaoAcquisition.TryCreateGeneratedState(actor, daoTu, className, "EquipmentEditorGrant", year, 0, out XjFaBaoState state, kind, role))
		{
			return;
		}

		ApplyFaBaoItemData(item, state);
		XjFaBaoAcquisition.WriteAndPublish(actor, state);
	}

	internal static void ClearEquippedFaBaoForCultivationReset(Actor actor)
	{
		if (actor?.equipment == null)
		{
			return;
		}

		XjEquipmentForgeConsumer.BeginControlledEquipmentChange();
		try
		{
			foreach (ActorEquipmentSlot slot in actor.equipment)
			{
				Item item = slot?.getItem();
				if (item == null)
				{
					continue;
				}

				bool isFaBao = false;
				if (item.data != null)
				{
					item.data.get(ItemKeyMarker, out int markerValue, 0);
					isFaBao = markerValue == 1;
				}
				if (!isFaBao)
				{
					string itemId = GetSafeEquipmentItemId(item);
					isFaBao = XjFaBaoEquipmentAssets.TryResolveKind(itemId, out _, out _);
				}
				if (!isFaBao)
				{
					continue;
				}

				slot.takeAwayItem();
				RemoveLooseItem(item);
			}
		}
		finally
		{
			XjEquipmentForgeConsumer.EndControlledEquipmentChange();
		}
		MarkActorStatsDirty(actor);
	}

	internal static void TryEnsureGeneratedEquipment(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjFaBaoState state = XjFaBaoAccessor.BuildState(actor);
		if (!state.Found)
		{
			TryImportEquippedFaBao(actor);
			return;
		}

		TrySyncGeneratedEquipment(actor, state);
	}

	private static bool TryResolveEquippedFaBaoClass(Actor actor, out string className)
	{
		className = string.Empty;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (string.IsNullOrWhiteSpace(realmId))
		{
			return false;
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			className = XjFaBaoCatalog.ZhuJiFaQiClass;
			return true;
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			className = XjFaBaoCatalog.ZiFuLingBaoClass;
			return true;
		}

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			className = XjFaBaoCatalog.JinDanFaBaoClass;
			return true;
		}

		return false;
	}

	private static bool TryFindEquippedFaBaoAppearance(Actor actor, out Item item, out string kind, out string role)
	{
		item = null;
		kind = string.Empty;
		role = string.Empty;
		if (actor?.equipment == null)
		{
			return false;
		}

		foreach (ActorEquipmentSlot slot in actor.equipment)
		{
			Item current = slot?.getItem();
			if (current?.getAsset()?.equipment_type != EquipmentType.Weapon)
			{
				continue;
			}

			string itemId = GetSafeEquipmentItemId(current);
			if (!XjFaBaoEquipmentAssets.TryResolveKind(itemId, out kind, out role))
			{
				continue;
			}

			item = current;
			return item != null;
		}

		return false;
	}

	internal static bool TryReadFaBaoItem(Item item, out string name, out string className, out string description, out string affixes)
	{
		name = string.Empty;
		className = string.Empty;
		description = string.Empty;
		affixes = string.Empty;
		if (item?.data == null)
		{
			return false;
		}

		item.data.get(ItemKeyMarker, out int marker, 0);
		if (marker != 1)
		{
			return false;
		}

		item.data.get(ItemKeyName, out name, string.Empty);
		item.data.get(ItemKeyClass, out className, string.Empty);
		item.data.get(ItemKeyDescription, out description, string.Empty);
		item.data.get(ItemKeyAffixes, out affixes, string.Empty);
		item.data.get(ItemKeyKind, out string kind, string.Empty);
		item.data.get(ItemKeyRole, out string role, string.Empty);
		role = XjFaBaoCatalog.NormalizeRole(kind, role);
		affixes = XjFaBaoCatalog.NormalizeAffixesForClass(affixes, role, className);
		if (string.IsNullOrWhiteSpace(name))
		{
			name = item.getName(true);
		}

		return !string.IsNullOrWhiteSpace(name);
	}

	internal static bool TryReadFaBaoItemId(Item item, out string id)
	{
		id = string.Empty;
		if (item?.data == null)
		{
			return false;
		}

		item.data.get(ItemKeyMarker, out int marker, 0);
		if (marker != 1)
		{
			return false;
		}

		item.data.get(ItemKeyId, out id, string.Empty);
		return !string.IsNullOrWhiteSpace(id);
	}

	internal static bool TryReadFaBaoState(Item item, out XjFaBaoState state)
	{
		state = XjFaBaoState.Empty;
		if (item?.data == null)
		{
			return false;
		}

		item.data.get(ItemKeyMarker, out int marker, 0);
		if (marker != 1)
		{
			return false;
		}

		item.data.get(ItemKeyId, out string id, string.Empty);
		item.data.get(ItemKeyName, out string name, string.Empty);
		item.data.get(ItemKeyClass, out string className, string.Empty);
		item.data.get(ItemKeyDaoTu, out string daoTu, string.Empty);
		item.data.get(ItemKeyKind, out string kind, string.Empty);
		item.data.get(ItemKeyRole, out string role, string.Empty);
		item.data.get(ItemKeyAffixes, out string affixes, string.Empty);
		role = XjFaBaoCatalog.NormalizeRole(kind, role);
		affixes = XjFaBaoCatalog.NormalizeAffixesForClass(affixes, role, className);
		item.data.get(ItemKeyDescription, out string description, string.Empty);
		item.data.get(ItemKeySource, out string source, "EquippedItem");
		item.data.get(ItemKeyYear, out int year, 0);
		if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(className))
		{
			return false;
		}

		state = new XjFaBaoState(
			true, id, name, daoTu, className, kind, role, affixes, description,
			string.IsNullOrWhiteSpace(source) ? "EquippedItem" : source,
			year < 0 ? 0 : year,
			"Ok");
		return true;
	}

	internal static bool HasAnyEquippedFaBao(Actor actor)
	{
		if (actor?.equipment == null)
		{
			return false;
		}

		foreach (ActorEquipmentSlot slot in actor.equipment)
		{
			Item item = slot?.getItem();
			if (item?.data != null && TryReadFaBaoState(item, out _))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsRealm(Actor actor, string realmId)
	{
		if (actor?.data == null)
		{
			return false;
		}

		string currentRealmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return string.Equals(currentRealmId, XjRealmHelper.NormalizeId(realmId), StringComparison.Ordinal);
	}

	private static string GetSafeEquipmentItemId(Item item)
	{
		if (item?.asset == null)
		{
			return string.Empty;
		}

		return ((Asset)item.asset).id ?? string.Empty;
	}


	internal static void TrySyncGeneratedEquipment(Actor actor, in XjFaBaoState state)
	{
		if (actor?.data == null || !state.Found || string.IsNullOrWhiteSpace(state.Kind))
		{
			return;
		}

		long actorId = GetActorId(actor);
		if (!XjFaBaoEquipmentAssets.TryPickAssetId(state.Kind, actorId, state.Id, out string assetId))
		{
			return;
		}

		EquipmentAsset asset = null;
		try
		{
			asset = ((AssetLibrary<EquipmentAsset>)(object)AssetManager.items).get(assetId);
		}
		catch
		{
			return;
		}

		if (asset == null)
		{
			return;
		}

		Item item = FindActorFaBaoItem(actor, state.Id);
		if (IsFaBaoItemSynced(item, state))
		{
			EnsureBoundPrimaryOwnership(item, actor, state);
			return;
		}

		int syncYear = state.Year > 0 ? state.Year : XjFaBaoAcquisition.GetCurrentYear(actor);
		if (item == null
			&& !XjEquipmentForgeConsumer.TryGenerateManagedItem(
				asset, actor.kingdom, actor.getName(), 0, actor, syncYear, out item))
		{
			return;
		}

		if (item == null)
		{
			return;
		}

		ApplyFaBaoItemData(item, state);
		EquipFaBaoItem(actor, item, asset.equipment_type);
		EnsureBoundPrimaryOwnership(item, actor, state);
		XjNativeHoverTooltip.RegisterPassthrough(state.Name, state.ClassName, state.Description, state.Affixes);
	}

	private static bool IsFaBaoItemSynced(Item item, in XjFaBaoState state)
	{
		if (item?.data == null || !state.Found)
		{
			return false;
		}

		item.data.get(ItemKeyMarker, out int marker, 0);
		if (marker != 1)
		{
			return false;
		}

		item.data.get(ItemKeySchemaVersion, out int schemaVersion, 0);
		if (schemaVersion != CurrentItemSchemaVersion)
		{
			return false;
		}

		item.data.get(ItemKeyId, out string id, string.Empty);
		item.data.get(ItemKeyName, out string name, string.Empty);
		item.data.get(ItemKeyClass, out string className, string.Empty);
		item.data.get(ItemKeyDaoTu, out string daoTu, string.Empty);
		item.data.get(ItemKeyKind, out string kind, string.Empty);
		item.data.get(ItemKeyRole, out string role, string.Empty);
		item.data.get(ItemKeyAffixes, out string affixes, string.Empty);
		item.data.get(ItemKeyDescription, out string description, string.Empty);
		item.data.get(ItemKeySource, out string source, string.Empty);
		item.data.get(ItemKeyYear, out int year, 0);
		return string.Equals(id, state.Id ?? string.Empty, StringComparison.Ordinal)
			&& string.Equals(name, state.Name ?? string.Empty, StringComparison.Ordinal)
			&& string.Equals(className, state.ClassName ?? string.Empty, StringComparison.Ordinal)
			&& string.Equals(daoTu, state.DaoTu ?? string.Empty, StringComparison.Ordinal)
			&& string.Equals(kind, state.Kind ?? string.Empty, StringComparison.Ordinal)
			&& string.Equals(role, state.Role ?? string.Empty, StringComparison.Ordinal)
			&& string.Equals(affixes, state.Affixes ?? string.Empty, StringComparison.Ordinal)
			&& string.Equals(description, state.Description ?? string.Empty, StringComparison.Ordinal)
			&& string.Equals(source, state.Source ?? string.Empty, StringComparison.Ordinal)
			&& year == (state.Year < 0 ? 0 : state.Year);
	}

	private static Item FindActorFaBaoItem(Actor actor, string faBaoId)
	{
		if (actor?.equipment == null)
		{
			return null;
		}

		foreach (ActorEquipmentSlot slot in actor.equipment)
		{
			Item item = slot?.getItem();
			if (item?.data == null)
			{
				continue;
			}

			item.data.get(ItemKeyMarker, out int marker, 0);
			if (marker != 1)
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(faBaoId))
			{
				item.data.get(ItemKeyId, out string currentId, string.Empty);
				if (!string.IsNullOrWhiteSpace(currentId) && !string.Equals(currentId, faBaoId, StringComparison.Ordinal))
				{
					continue;
				}
			}

			return item;
		}

		return null;
	}

	internal static bool IsBoundPrimaryWeapon(Item item)
	{
		if (item?.data == null) return false;
		item.data.get(ItemKeyBoundPrimary, out int bound, 0);
		if (bound == 1) return true;

		EquipmentAsset asset = item.getAsset();
		if (asset?.equipment_type != EquipmentType.Weapon
			|| !TryReadFaBaoState(item, out XjFaBaoState state)) return false;
		return string.Equals(XjFaBaoCatalog.NormalizeRole(state.Kind, state.Role), XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal)
			&& (XjFaBaoCatalog.IsZiFuLingBao(state.ClassName) || XjFaBaoCatalog.IsJinDanFaBao(state.ClassName));
	}

	internal static bool IsBoundPrimaryOwnedBy(Item item, Actor actor)
	{
		if (!IsBoundPrimaryWeapon(item) || item?.data == null || actor?.data == null) return false;
		item.data.get("xuanjian.fabao.owner_id", out long ownerId, 0L);
		return ownerId > 0L && ownerId == ((BaseSystemData)actor.data).id;
	}

	internal static bool HasForeignBoundPrimaryOwner(Item item, Actor actor)
	{
		if (!IsBoundPrimaryWeapon(item) || item?.data == null || actor?.data == null) return false;
		item.data.get("xuanjian.fabao.owner_id", out long ownerId, 0L);
		return ownerId > 0L && ownerId != ((BaseSystemData)actor.data).id;
	}

	private static void EnsureBoundPrimaryOwnership(Item item, Actor actor, in XjFaBaoState state)
	{
		if (item?.data == null || actor?.data == null || actor.equipment == null || !state.Found) return;
		if (!IsBoundPrimaryWeapon(item)) return;
		ActorEquipmentSlot weaponSlot = actor.equipment.getSlot(EquipmentType.Weapon);
		if (!ReferenceEquals(weaponSlot?.getItem(), item)) return;
		item.data.get(ItemKeyId, out string itemId, string.Empty);
		if (!string.Equals(itemId, state.Id ?? string.Empty, StringComparison.Ordinal)) return;
		item.data.set(ItemKeyBoundPrimary, 1);
		item.data.set("xuanjian.fabao.owner_id", ((BaseSystemData)actor.data).id);
	}

	internal static void ApplyFaBaoItemData(Item item, in XjFaBaoState state, bool clearNativeModifiers = false)
	{
		if (item?.data == null)
		{
			return;
		}

		item.data.set(ItemKeyMarker, 1);
		item.data.set(ItemKeySchemaVersion, CurrentItemSchemaVersion);
		item.data.set(ItemKeyId, state.Id ?? string.Empty);
		item.data.set(ItemKeyName, state.Name ?? string.Empty);
		item.data.set(ItemKeyClass, state.ClassName ?? string.Empty);
		item.data.set(ItemKeyDaoTu, state.DaoTu ?? string.Empty);
		item.data.set(ItemKeyKind, state.Kind ?? string.Empty);
		item.data.set(ItemKeyRole, state.Role ?? string.Empty);
		item.data.set(ItemKeyAffixes, state.Affixes ?? string.Empty);
		item.data.set(ItemKeyDescription, state.Description ?? string.Empty);
		item.data.set(ItemKeySource, state.Source ?? string.Empty);
		item.data.set(ItemKeyYear, state.Year < 0 ? 0 : state.Year);
		EquipmentAsset boundAsset = item.getAsset();
		bool boundPrimary = boundAsset?.equipment_type == EquipmentType.Weapon
			&& string.Equals(XjFaBaoCatalog.NormalizeRole(state.Kind, state.Role), XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal)
			&& (XjFaBaoCatalog.IsZiFuLingBao(state.ClassName) || XjFaBaoCatalog.IsJinDanFaBao(state.ClassName));
		item.data.set(ItemKeyBoundPrimary, boundPrimary ? 1 : 0);
		try
		{
			if (clearNativeModifiers)
			{
				item.data.modifiers.Clear();
			}
			item.setName(string.IsNullOrWhiteSpace(state.Name) ? "玄鉴法宝" : state.Name.Trim(), true);
			item.calculateValues();
			ApplyNativeClassStats(item, state.ClassName);
		}
		catch
		{
		}
	}

	internal static void ApplyNativeClassStats(Item item, string className)
	{
		if (item == null) return;
		BaseStats stats = item.getFullStats();
		if (stats == null) return;

		string itemId = item?.asset?.id ?? string.Empty;
		if (!XjFaBaoEquipmentAssets.TryGetDefinition(itemId, out XjFaBaoEquipmentDefinition definition)) return;
		if (string.Equals(definition.Role, XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal))
		{
			int damage = ResolveNativeClassDamage(className);
			if (damage > 0)
			{
				stats.set("damage", damage);
				stats.set("damage_range", 0.6f);
			}
			return;
		}

		// 模板或旧档残留的攻击数值必须清零；防御类统一提供固定生命值。
		stats.set("damage", 0f);
		stats.set("damage_range", 0f);
		if (string.Equals(definition.Role, XjFaBaoCatalog.RoleDefense, StringComparison.Ordinal))
		{
			stats.set("health", 200000f);
		}
	}

	private static int ResolveNativeClassDamage(string className)
	{
		if (XjFaBaoCatalog.IsJinDanFaBao(className)) return 10000;
		if (XjFaBaoCatalog.IsZiFuLingBao(className)) return 4500;
		if (XjFaBaoCatalog.IsZhuJiFaQi(className)) return 1000;
		return 0;
	}

	private static void EquipFaBaoItem(Actor actor, Item item, EquipmentType equipmentType)
	{
		if (actor?.equipment == null || item == null)
		{
			return;
		}

		ActorEquipmentSlot slot = actor.equipment.getSlot(equipmentType);
		if (slot == null)
		{
			RemoveLooseItem(item);
			return;
		}

		if (slot.isEmpty())
		{
			slot.setItem(item, actor);
			item.data.set("xuanjian.fabao.owner_id", ((BaseSystemData)actor.data).id);
			MarkActorStatsDirty(actor);
			return;
		}

		Item oldItem = slot.getItem();
		if (ReferenceEquals(oldItem, item))
		{
			MarkActorStatsDirty(actor);
			return;
		}

		XjEquipmentForgeConsumer.BeginControlledEquipmentChange();
		try
		{
			slot.takeAwayItem();
			if (oldItem != null)
			{
				RemoveLooseItem(oldItem);
			}

			slot.setItem(item, actor);
			item.data.set("xuanjian.fabao.owner_id", ((BaseSystemData)actor.data).id);
			MarkActorStatsDirty(actor);
		}
		finally
		{
			XjEquipmentForgeConsumer.EndControlledEquipmentChange();
		}
	}

	private static void RemoveLooseItem(Item item)
	{
		if (item == null)
		{
			return;
		}

		try
		{
			World.world.items.removeObject(item);
		}
		catch
		{
		}
	}

	private static void MarkActorStatsDirty(Actor actor)
	{
		try
		{
			actor?.setStatsDirty();
		}
		catch
		{
		}
	}


	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
