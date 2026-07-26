using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.Systems.FaBao;

internal static class XjFaBaoAcquisition
{
	private const string SourceJinDan = "JinDan";
	private const string SourceZhuJiRefine = "ZhuJiFaQiRefine";
	private const string SourceZiFuRefine = "ZiFuRefine";
	private const string SourceLingBaoUpgrade = "LingBaoUpgrade";
	private const string SourceJieLinGrant = "JieLinGrant";
	private const string SourceJieLinUpgrade = "JieLinUpgrade";
	private const string SourceDongTian = "QiYuDongTian";
	private static readonly HashSet<string> UsedNames = new HashSet<string>(StringComparer.Ordinal);
	private static bool _usedNamesSeeded;

	internal static void ClearRuntimeCache()
	{
		UsedNames.Clear();
		_usedNamesSeeded = false;
	}

	private static bool TryCreateAndPublish(
		Actor actor,
		string daoTu,
		string className,
		string source,
		int year,
		int ordinal,
		string forcedRole = "")
	{
		if (!TryCreateGeneratedState(
			actor, daoTu, className, source, year, ordinal, out XjFaBaoState state,
			forcedRole: forcedRole))
		{
			return false;
		}

		WriteAndPublish(actor, state);
		XjCraftProficiencySystem.RecordArtifactSuccess(actor, state.ClassName);
		return true;
	}


	internal static void TryGrantOnJinDanSuccess(Actor actor, XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null)
		{
			return;
		}

		string daoTu = snapshot.DaoTu;
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out daoTu);
		}

		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		TryRefineJinDanFaBao(actor, daoTu, GetCurrentYear(actor), promotionAttempt: true);
	}

	private static void TryRefineJinDanFaBao(Actor actor, string daoTu, int currentYear, bool promotionAttempt)
	{
		if (actor?.data == null
			|| currentYear <= 0
			|| string.IsNullOrWhiteSpace(daoTu)
			|| !string.Equals(XjFaBaoForgePolicy.ResolvePracticeRealmId(actor, XjRealmIds.JinDan), XjRealmIds.JinDan, StringComparison.Ordinal)
			|| !(promotionAttempt
				? XjFaBaoForgePolicy.CanAttemptPromotion(actor, XjRealmIds.JinDan, currentYear)
				: XjFaBaoForgePolicy.CanAttemptScheduled(actor, XjRealmIds.JinDan, currentYear)))
		{
			return;
		}

		XjFaBaoState primary = XjFaBaoAccessor.BuildState(actor);
		bool primaryIsFaBao = primary.Found && XjFaBaoCatalog.IsJinDanFaBao(primary.ClassName);
		bool primaryUpgradeable = primary.Found && XjFaBaoCatalog.IsZiFuLingBao(primary.ClassName);
		bool primaryStaleFaQi = primary.Found && XjFaBaoCatalog.IsZhuJiFaQi(primary.ClassName);
		bool slotUpgradeable = !primaryUpgradeable && !primaryStaleFaQi
			&& XjEquipmentForgeConsumer.HasUpgradeableLingBao(actor);
		bool canCreate = (!primary.Found || primaryStaleFaQi) && !slotUpgradeable
			&& (primaryStaleFaQi || XjFaBaoForgePolicy.CanCreateNewManagedItem(actor, XjRealmIds.JinDan));
		if (primaryIsFaBao || (!primaryUpgradeable && !slotUpgradeable && !canCreate)) return;

		if (!XjArtifactForgeFuel.HasJinDanForgeFuel(actor)) return;
		bool reserved = promotionAttempt
			? XjFaBaoForgePolicy.TryReservePromotionAttempt(actor, XjRealmIds.JinDan, currentYear)
			: XjFaBaoForgePolicy.TryReserveScheduledAttempt(actor, XjRealmIds.JinDan, currentYear);
		if (!reserved
			|| !XjArtifactForgeFuel.TryConsumeForJinDan(actor, out XjArtifactForgeFuelReceipt fuel)
			|| !XjFaBaoForgePolicy.RollAnnual(actor, XjRealmIds.JinDan, currentYear,
				(promotionAttempt ? "jindan_promotion_" : "jindan_annual_") + fuel.Kind))
		{
			return;
		}

		// 晋升金丹时先升武器灵宝；没有武器灵宝才从其余器型中取一件晋升。
		if (primaryUpgradeable)
		{
			UpgradePrimaryLingBao(actor, primary, daoTu, currentYear, SourceLingBaoUpgrade);
			return;
		}
		if (slotUpgradeable
			&& XjEquipmentForgeConsumer.TryUpgradeFirstLingBaoToFaBao(actor, daoTu, currentYear, SourceLingBaoUpgrade))
		{
			return;
		}

		TryCreateAndPublish(
			actor, daoTu, XjFaBaoCatalog.JinDanFaBaoClass, SourceJinDan, currentYear, 0,
			XjFaBaoCatalog.RoleAttack);
	}

	private static bool UpgradePrimaryLingBao(
		Actor actor,
		in XjFaBaoState existing,
		string daoTu,
		int currentYear,
		string source)
	{
		if (!existing.Found || !XjFaBaoCatalog.IsZiFuLingBao(existing.ClassName)) return false;
		// 本命灵宝与本命法宝始终共用原生武器槽。晋升时保留同一器物 ID，
		// 同时强制归一为攻击器型，避免旧档辅助器型占据本命武器链。
		string upgradeKind = XjWeaponArtSystem.ForceGeneratedAttackKind(actor, existing.Kind);
		if (string.IsNullOrWhiteSpace(upgradeKind)) upgradeKind = "剑";
		string upgradeRole = XjFaBaoCatalog.RoleAttack;
		string upgradeDaoTu = string.IsNullOrWhiteSpace(existing.DaoTu) ? daoTu : existing.DaoTu;
		string upgradeAffixes = MergeUpgradeAffixes(
			actor, existing.Affixes, XjFaBaoCatalog.JinDanFaBaoClass,
			upgradeRole, currentYear, 0);
		string upgradeDescription = XjFaBaoDescriptionFormatter.BuildGeneratedDescription(
			actor, existing.Name, upgradeDaoTu, XjFaBaoCatalog.JinDanFaBaoClass,
			upgradeKind, upgradeRole, source);
		XjFaBaoState upgraded = new XjFaBaoState(
			true,
			string.IsNullOrWhiteSpace(existing.Id)
				? BuildFaBaoId(actor, daoTu, XjFaBaoCatalog.JinDanFaBaoClass, currentYear, 0)
				: existing.Id,
			existing.Name, upgradeDaoTu, XjFaBaoCatalog.JinDanFaBaoClass,
			upgradeKind, upgradeRole, upgradeAffixes, upgradeDescription,
			source, currentYear, "Ok");
		WriteAndPublish(actor, upgraded);
		XjCraftProficiencySystem.RecordArtifactSuccess(actor, upgraded.ClassName);
		return true;
	}

	/// <summary>
	/// 结璘仙按独立概率炼制本命法宝；正式尝试同样消耗一缕家族金丹遗留金性。
	/// </summary>
	internal static void TryGrantOnJieLinSuccess(Actor actor, int currentYear)
	{
		TryRefineJieLinFaBao(actor, currentYear, promotionAttempt: true);
	}

	private static void TryRefineJieLinFaBao(Actor actor, int currentYear, bool promotionAttempt)
	{
		if (actor?.data == null
			|| !XjXuanJianShenTongSpecials.IsJieLinXian(actor)
			|| !string.Equals(XjFaBaoForgePolicy.ResolvePracticeRealmId(actor, XjRealmIds.JinDan), XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return;
		}

		int safeYear = currentYear > 0 ? currentYear : GetCurrentYear(actor);
		const string daoTu = "太阴";
		if (!(promotionAttempt
			? XjFaBaoForgePolicy.CanAttemptPromotion(actor, XjRealmIds.JinDan, safeYear)
			: XjFaBaoForgePolicy.CanAttemptScheduled(actor, XjRealmIds.JinDan, safeYear))) return;

		XjFaBaoState primary = XjFaBaoAccessor.BuildState(actor);
		bool primaryIsFaBao = primary.Found && XjFaBaoCatalog.IsJinDanFaBao(primary.ClassName);
		bool primaryUpgradeable = primary.Found && XjFaBaoCatalog.IsZiFuLingBao(primary.ClassName);
		bool primaryStaleFaQi = primary.Found && XjFaBaoCatalog.IsZhuJiFaQi(primary.ClassName);
		bool slotUpgradeable = !primaryUpgradeable && !primaryStaleFaQi
			&& XjEquipmentForgeConsumer.HasUpgradeableLingBao(actor);
		bool canCreate = (!primary.Found || primaryStaleFaQi) && !slotUpgradeable
			&& (primaryStaleFaQi || XjFaBaoForgePolicy.CanCreateNewManagedItem(actor, XjRealmIds.JinDan));
		if (primaryIsFaBao || (!primaryUpgradeable && !slotUpgradeable && !canCreate)) return;

		if (!XjArtifactForgeFuel.HasJinDanForgeFuel(actor)) return;
		bool reserved = promotionAttempt
			? XjFaBaoForgePolicy.TryReservePromotionAttempt(actor, XjRealmIds.JinDan, safeYear)
			: XjFaBaoForgePolicy.TryReserveScheduledAttempt(actor, XjRealmIds.JinDan, safeYear);
		if (!reserved
			|| !XjArtifactForgeFuel.TryConsumeForJinDan(actor, out XjArtifactForgeFuelReceipt fuel)
			|| !XjFaBaoForgePolicy.RollAnnual(actor, XjRealmIds.JinDan, safeYear,
				(promotionAttempt ? "jielin_promotion_" : "jielin_annual_") + fuel.Kind)) return;

		if (primaryUpgradeable)
		{
			UpgradePrimaryLingBao(actor, primary, daoTu, safeYear, SourceJieLinUpgrade);
			return;
		}
		if (slotUpgradeable
			&& XjEquipmentForgeConsumer.TryUpgradeFirstLingBaoToFaBao(actor, daoTu, safeYear, SourceJieLinUpgrade))
		{
			return;
		}
		TryCreateAndPublish(
			actor, daoTu, XjFaBaoCatalog.JinDanFaBaoClass, SourceJieLinGrant, safeYear, 0,
			XjFaBaoCatalog.RoleAttack);
	}

	internal static void TryGrantZiFuLingBaoOnXianJi(Actor actor, XjActorCultivationSnapshot snapshot, int xianJiCount, int currentYear)
	{
		if (actor?.data == null
			|| xianJiCount < 3
			|| !string.Equals(snapshot.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return;
		}
		TryForgeAnnualIfMissing(actor, XjRealmIds.ZiFu, currentYear);
	}

	internal static void TryForgeAnnualIfMissing(Actor actor, string realmId, int currentYear)
	{
		if (actor?.data == null
			|| currentYear <= 0
			|| string.IsNullOrWhiteSpace(realmId))
		{
			return;
		}

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		// 三神通后的本命灵宝是紫府个人炼宝，不属于百艺炼器生产。
		// 达成条件当年即可尝试，失败后每年按40%补炼；每次正式尝试只允许投入
		// 一件同道途紫府灵物，绝不允许使用金丹金性替代。
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& XjFaBaoForgePolicy.NeedsPersonalZiFuLingBao(actor))
		{
			if (!XjFaBaoForgePolicy.CanAttemptPersonalZiFuLingBao(actor, currentYear)
				|| !XjArtifactForgeFuel.TryConsumeForZiFu(actor, daoTu, out XjArtifactForgeFuelReceipt fuel)
				|| !XjFaBaoForgePolicy.TryReservePersonalZiFuLingBaoAttempt(actor, currentYear)
				|| !XjFaBaoForgePolicy.RollAnnual(
					actor,
					XjRealmIds.ZiFu,
					currentYear,
					"personal_zifu_lingbao_" + fuel.Kind,
					XjFaBaoForgePolicy.PersonalZiFuLingBaoChancePercent))
			{
				return;
			}

			TryCreateAndPublish(
				actor,
				daoTu,
				XjFaBaoCatalog.ZiFuLingBaoClass,
				SourceZiFuRefine,
				currentYear,
				0,
				XjFaBaoCatalog.RoleAttack);
			return;
		}

		XjCraftTraitRules.NormalizeExclusive(actor);
		if (!XjCraftTraitRules.CanRefineArtifacts(actor))
		{
			return;
		}

		string forgeRealmId = XjFaBaoForgePolicy.ResolvePracticeRealmId(actor, realmId);
		if (string.IsNullOrWhiteSpace(forgeRealmId)) return;

		if (string.Equals(forgeRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			if (XjFaBaoAccessor.HasState(actor)
				|| !XjFaBaoForgePolicy.CanCreateNewManagedItem(actor, forgeRealmId)
				|| !XjFaBaoForgePolicy.CanAttemptScheduled(actor, forgeRealmId, currentYear)
				|| !XjArtifactForgeFuel.TryConsumeForZhuJiFaQi(actor, daoTu, out XjArtifactForgeFuelReceipt fuel)
				|| !XjFaBaoForgePolicy.TryReserveScheduledAttempt(actor, forgeRealmId, currentYear))
			{
				return;
			}

			int chance = XjArtifactForgeFuel.ResolveZhuJiFaQiChancePercent(actor, in fuel);
			if (!XjFaBaoForgePolicy.RollAnnual(actor, forgeRealmId, currentYear, "primary_faqi_" + fuel.Kind, chance)) return;
			TryCreateAndPublish(actor, daoTu, XjFaBaoCatalog.ZhuJiFaQiClass, SourceZhuJiRefine, currentYear, 0);
			return;
		}


		if (!string.Equals(forgeRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return;
		if (XjXuanJianShenTongSpecials.IsJieLinXian(actor))
		{
			TryRefineJieLinFaBao(actor, currentYear, promotionAttempt: false);
			return;
		}
		TryRefineJinDanFaBao(actor, daoTu, currentYear, promotionAttempt: false);
	}

	internal static bool TryGrantDongTianReward(Actor actor, string daoTu, int year, out string displayName)
	{
		displayName = string.Empty;
		if (actor?.data == null
			|| XjFaBaoAccessor.HasState(actor)
			|| !XjFaBaoForgePolicy.CanCreateNewManagedItem(actor, XjRealmIds.ZiFu)
			|| string.IsNullOrWhiteSpace(daoTu)
			|| !IsRealm(actor, XjRealmIds.ZiFu))
		{
			return false;
		}

		if (!TryCreateGeneratedState(
			actor, daoTu, XjFaBaoCatalog.ZiFuLingBaoClass, SourceDongTian, year < 0 ? 0 : year, 0,
			out XjFaBaoState state, forcedRole: XjFaBaoCatalog.RoleAttack))
		{
			return false;
		}

		WriteAndPublish(actor, state);
		displayName = state.Name;
		return true;
	}

	internal static void RegisterKnownName(string name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			UsedNames.Add(name.Trim());
		}
	}


	internal static bool TryCreateGeneratedState(
		Actor actor,
		string daoTu,
		string className,
		string source,
		int year,
		int ordinal,
		out XjFaBaoState state,
		string forcedKind = "",
		string forcedRole = "",
		string forcedName = "",
		string[] forcedNameSuffixes = null)
	{
		state = XjFaBaoState.Empty;
		string normalizedDaoTu = string.IsNullOrWhiteSpace(daoTu) ? "真炁" : daoTu.Trim();
		long actorId = GetActorId(actor);
		string role = string.IsNullOrWhiteSpace(forcedRole)
			? ResolveGeneratedRole(actor, normalizedDaoTu, className, source, year, ordinal)
			: forcedRole.Trim();
		if (string.Equals(role, XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal))
		{
			forcedKind = XjWeaponArtSystem.ForceGeneratedAttackKind(actor, forcedKind);
		}

		string kind;
		string name;
		if (!string.IsNullOrWhiteSpace(forcedName))
		{
			kind = (forcedKind ?? string.Empty).Trim();
			name = forcedName.Trim();
			if (string.IsNullOrWhiteSpace(kind))
			{
				return false;
			}
		}
		else
		{
			name = GenerateUniqueName(
				normalizedDaoTu,
				className,
				source,
				actorId,
				year,
				ordinal,
				role,
				out kind,
				forcedKind,
				forcedNameSuffixes);
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		role = XjFaBaoCatalog.NormalizeRole(kind, role);
		RegisterKnownName(name);
		string affixes = BuildAffixSummary(actor, className, role, year, ordinal);
		string description = XjFaBaoDescriptionFormatter.BuildGeneratedDescription(actor, name, normalizedDaoTu, className, kind, role, source);
		state = new XjFaBaoState(
			true,
			BuildFaBaoId(actor, normalizedDaoTu, className, year, ordinal),
			name,
			normalizedDaoTu,
			className,
			kind,
			role,
			affixes,
			description,
			source,
			year < 0 ? 0 : year,
			"Ok");
		return true;
	}

	internal static bool TryGenerateUniqueLingZhuangName(
		Actor actor,
		string daoTu,
		string className,
		string source,
		int year,
		int ordinal,
		EquipmentType equipmentType,
		out string name)
	{
		name = string.Empty;
		string kind = XjLingZhuangNameLibrary.ResolveKind(equipmentType);
		string[] suffixes = XjLingZhuangNameLibrary.GetNameSuffixes(equipmentType);
		if (string.IsNullOrWhiteSpace(kind) || suffixes.Length == 0)
		{
			return false;
		}

		name = GenerateUniqueName(
			daoTu,
			className,
			source,
			GetActorId(actor),
			year,
			ordinal,
			XjFaBaoCatalog.RoleDefense,
			out _,
			kind,
			suffixes);
		return !string.IsNullOrWhiteSpace(name);
	}

	private static string GenerateUniqueName(
		string daoTu,
		string className,
		string source,
		long actorId,
		int year,
		int ordinal,
		string role,
		out string kind,
		string forcedKind = "",
		string[] forcedNameSuffixes = null)
	{
		kind = string.Empty;
		if (!XjFaBaoCatalog.TryGetDaoTuWords(daoTu, out _, out string[] daoTuWords))
		{
			return string.Empty;
		}

		SeedUsedNamesFromWarehouse();
		string normalizedForcedKind = (forcedKind ?? string.Empty).Trim();
		string[] weaponWords = forcedNameSuffixes != null && forcedNameSuffixes.Length > 0
			? forcedNameSuffixes
			: string.IsNullOrWhiteSpace(normalizedForcedKind)
				? XjFaBaoCatalog.GetWeaponWordsForRole(role)
				: new[] { normalizedForcedKind };
		int maxAttempts = Math.Max(1, XjFaBaoCatalog.CommonWords.Length * daoTuWords.Length * weaponWords.Length);
		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			string salt = className + "|" + source + "|" + daoTu + "|" + year + "|" + ordinal + "|" + attempt;
			string common = XjFaBaoCatalog.CommonWords[XjDeterministicHash.PositiveIndex(actorId + attempt, salt + "|common", XjFaBaoCatalog.CommonWords.Length)];
			string daoWord = daoTuWords[XjDeterministicHash.PositiveIndex(actorId + attempt * 17L, salt + "|dao", daoTuWords.Length)];
			string weapon = weaponWords[XjDeterministicHash.PositiveIndex(actorId + attempt * 31L, salt + "|weapon", weaponWords.Length)];
			string generatedName = common + daoWord + weapon;
			if (UsedNames.Add(generatedName))
			{
				kind = string.IsNullOrWhiteSpace(normalizedForcedKind) ? weapon : normalizedForcedKind;
				return generatedName;
			}
		}

		if (forcedNameSuffixes != null && forcedNameSuffixes.Length > 0)
		{
			// 槽位灵装必须始终保持“二字通用词 + 二字道途词 + 单字器型”的五字结构。
			// 极端情况下词库组合全部占用时允许同名，也不能追加数字破坏器名格式。
			string salt = className + "|" + source + "|" + daoTu + "|" + year + "|" + ordinal + "|exhausted";
			string common = XjFaBaoCatalog.CommonWords[XjDeterministicHash.PositiveIndex(actorId, salt + "|common", XjFaBaoCatalog.CommonWords.Length)];
			string daoWord = daoTuWords[XjDeterministicHash.PositiveIndex(actorId, salt + "|dao", daoTuWords.Length)];
			string weapon = weaponWords[XjDeterministicHash.PositiveIndex(actorId, salt + "|weapon", weaponWords.Length)];
			string fallback = common + daoWord + weapon;
			UsedNames.Add(fallback);
			kind = normalizedForcedKind;
			return fallback;
		}

		for (int i = 2; i < 100; i++)
		{
			string weapon = weaponWords[XjDeterministicHash.PositiveIndex(actorId + i * 37L, daoTu + "|fallback_weapon", weaponWords.Length)];
			string fallback = XjFaBaoCatalog.CommonWords[XjDeterministicHash.PositiveIndex(actorId + i, daoTu + "|fallback", XjFaBaoCatalog.CommonWords.Length)]
				+ daoTuWords[XjDeterministicHash.PositiveIndex(actorId + i * 19L, daoTu + "|fallback_dao", daoTuWords.Length)]
				+ weapon
				+ ToChineseNumber(i);
			if (UsedNames.Add(fallback))
			{
				kind = string.IsNullOrWhiteSpace(normalizedForcedKind) ? weapon : normalizedForcedKind;
				return fallback;
			}
		}

		return string.Empty;
	}

	private static string ResolveGeneratedRole(Actor actor, string daoTu, string className, string source, int year, int ordinal)
	{
		long actorId = GetActorId(actor);
		int roll = XjDeterministicHash.PositiveIndex(actorId + year + ordinal, className + "|" + source + "|" + daoTu + "|role", 100);
		return roll < 50 ? XjFaBaoCatalog.RoleAttack : XjFaBaoCatalog.RoleSupport;
	}

	private static string BuildAffixSummary(Actor actor, string className, string role, int year, int ordinal)
	{
		string[] labels = XjFaBaoCatalog.GetAffixLabelsForRole(role);
		if (labels.Length == 0)
		{
			return string.Empty;
		}

		long actorId = GetActorId(actor);
		bool isJinDan = XjFaBaoCatalog.IsJinDanFaBao(className);
		bool isFaQi = XjFaBaoCatalog.IsZhuJiFaQi(className);
		int maxAffixCount = isJinDan ? 5 : isFaQi ? 1 : 3;
		int count = 1 + XjDeterministicHash.PositiveIndex(
			actorId + year + ordinal, className + "|" + role + "|affix_count", maxAffixCount);
		List<string> parts = new List<string>(count);
		for (int attempt = 0; parts.Count < count && attempt < labels.Length * 2; attempt++)
		{
			string label = labels[XjDeterministicHash.PositiveIndex(actorId + attempt * 11L, className + "|" + role + "|affix_label|" + attempt, labels.Length)];
			bool exists = false;
			for (int i = 0; i < parts.Count; i++)
			{
				if (parts[i].StartsWith(label + "+", StringComparison.Ordinal))
				{
					exists = true;
					break;
				}
			}

			if (exists)
			{
				continue;
			}

			ResolveAffixPercentRange(label, isJinDan, out float minPercent, out float maxPercent);
			float valueCap = isJinDan ? 30f : isFaQi ? 5f : 10f;
			maxPercent = Math.Min(maxPercent, valueCap);
			minPercent = Math.Min(minPercent, maxPercent);
			float roll = XjDeterministicHash.PositiveIndex(
				actorId + attempt * 17L + year,
				className + "|" + label + "|affix_value",
				1001) / 1000f;
			// 炼器经验不突破法器/灵宝/法宝既有数值上限，只把同层器物的词条稳定推向更高区间。
			float strength = XjCraftProficiencySystem.GetArtifactStatMultiplier(actor);
			float strengthenedRoll = Math.Clamp(roll * strength, 0f, 1f);
			float value = minPercent + (maxPercent - minPercent) * strengthenedRoll;
			string formatted = value.ToString(maxPercent < 2f ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture);
			parts.Add(label + "+" + formatted + "%");
		}

		return string.Join("/", parts);
	}

	/// <summary>
	/// 槽位灵宝晋升法宝时沿用原器物词条，再补入法宝级新词条；
	/// 不通过重新生成整件装备来洗掉原有器物身份。
	/// </summary>
	internal static string MergeUpgradeAffixes(
		Actor actor,
		string existingAffixes,
		string targetClass,
		string role,
		int year,
		int ordinal)
	{
		string generated = BuildAffixSummary(actor, targetClass, role, year, ordinal);
		string combined = string.IsNullOrWhiteSpace(existingAffixes)
			? generated
			: string.IsNullOrWhiteSpace(generated)
				? existingAffixes
				: existingAffixes.Trim() + "/" + generated.Trim();
		return XjFaBaoCatalog.NormalizeAffixesForClass(combined, role, targetClass);
	}

	private static void ResolveAffixPercentRange(string label, bool isJinDan, out float minPercent, out float maxPercent)
	{
		if (string.Equals(label, "每秒回血", StringComparison.Ordinal))
		{
			minPercent = isJinDan ? 0.4f : 0.2f;
			maxPercent = isJinDan ? 0.8f : 0.5f;
			return;
		}

		if (string.Equals(label, "真伤转化", StringComparison.Ordinal))
		{
			minPercent = isJinDan ? 6f : 3f;
			maxPercent = isJinDan ? 12f : 6f;
			return;
		}

		if (string.Equals(label, "吸血", StringComparison.Ordinal)
			|| string.Equals(label, "暴击提升", StringComparison.Ordinal)
			|| string.Equals(label, "突破概率", StringComparison.Ordinal))
		{
			minPercent = isJinDan ? 5f : 3f;
			maxPercent = isJinDan ? 10f : 6f;
			return;
		}

		if (string.Equals(label, "伤害提升", StringComparison.Ordinal)
			|| string.Equals(label, "生命提升", StringComparison.Ordinal)
			|| string.Equals(label, "破盾", StringComparison.Ordinal)
			|| string.Equals(label, "受暴击降低", StringComparison.Ordinal))
		{
			minPercent = isJinDan ? 12f : 8f;
			maxPercent = isJinDan ? 20f : 15f;
			return;
		}

		if (string.Equals(label, "伤害减免", StringComparison.Ordinal)
			|| string.Equals(label, "护盾提升", StringComparison.Ordinal)
			|| string.Equals(label, "闪避提升", StringComparison.Ordinal)
			|| string.Equals(label, "同境界伤害", StringComparison.Ordinal))
		{
			minPercent = isJinDan ? 8f : 5f;
			maxPercent = isJinDan ? 15f : 10f;
			return;
		}

		if (string.Equals(label, "减伤穿透", StringComparison.Ordinal)
			|| string.Equals(label, "命中提升", StringComparison.Ordinal)
			|| string.Equals(label, "攻速提升", StringComparison.Ordinal))
		{
			minPercent = isJinDan ? 10f : 6f;
			maxPercent = isJinDan ? 18f : 12f;
			return;
		}

		minPercent = isJinDan ? 8f : 5f;
		maxPercent = isJinDan ? 15f : 10f;
	}

	internal static string ResolveKindFromName(string name)
	{
		string value = (name ?? string.Empty).Trim();
		if (value.Length == 0)
		{
			return "印";
		}

		for (int i = 0; i < XjFaBaoCatalog.WeaponWords.Length; i++)
		{
			string weapon = XjFaBaoCatalog.WeaponWords[i];
			if (value.EndsWith(weapon, StringComparison.Ordinal))
			{
				return weapon;
			}
		}

		return "印";
	}

	private static void SeedUsedNamesFromWarehouse()
	{
		if (_usedNamesSeeded)
		{
			return;
		}

		_usedNamesSeeded = true;
		IReadOnlyList<XjFamilyFaBaoWarehouseEntry> entries = XjFamilyFaBaoWarehouse.ReadAllEntries();
		for (int i = 0; i < entries.Count; i++)
		{
			RegisterKnownName(entries[i].FaBaoName);
		}
	}

	internal static void WriteAndPublish(Actor actor, in XjFaBaoState state)
	{
		XjFaBaoAccessor.WriteState(actor, state);
		XjWeaponArtSystem.BindFromGeneratedArtifact(actor, state);
		XjFaBaoEquipmentSync.TrySyncGeneratedEquipment(actor, state);
		XjAutoCollectSystem.TryCollectFaBaoOwner(actor, "FaBaoOwner");
		RegisterKnownName(state.Name);
		bool isUpgrade = string.Equals(state.Source, SourceLingBaoUpgrade, System.StringComparison.Ordinal)
			|| string.Equals(state.Source, SourceJieLinUpgrade, System.StringComparison.Ordinal);
		long familyStableId = PersistLiveFaBao(actor, state);
		RecordFaBaoWorldHistory(actor, state, isUpgrade, familyStableId);
		if (XjRuntimeSettings.BroadcastTreasureMilestoneEnabled && ShouldAnnounceFaBaoResult(state.Source))
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelActorEvent(
				actor,
				XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildFaBaoResult(
					actor, state.ClassName, state.Name, state.Source),
				iconId: XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.FaBaoCreation);
		}
		if (isUpgrade)
		{
			XuanJianVNext.Systems.Events.XjFamilyDomainEventRouter.Publish(
				XuanJianVNext.Data.Events.XjFamilyDomainEvent.FaBaoUpgraded(
					actor, state.ClassName, state.Name));
		}
		else
		{
			XuanJianVNext.Systems.Events.XjFamilyDomainEventRouter.Publish(
				XuanJianVNext.Data.Events.XjFamilyDomainEvent.FaBaoObtained(
					actor, state.Id, state.Name, state.DaoTu, state.Source, state.ClassName));
		}
	}

	/// <summary>
	/// 将角色个人装备已满后炼成的余器直接存入器库，不覆盖角色当前主法宝，
	/// 也不生成临时装备对象。个人主器归家族传承；余器在家族与所属宗门之间低频分流。
	/// </summary>
	internal static bool TryStoreSurplusCraft(Actor actor, in XjFaBaoState state)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| !state.Found
			|| string.IsNullOrWhiteSpace(state.Id)
			|| string.IsNullOrWhiteSpace(state.Name)
			|| string.IsNullOrWhiteSpace(state.DaoTu))
		{
			return false;
		}

		string source = string.IsNullOrWhiteSpace(state.Source)
			? XjFamilyFaBaoWarehouse.SourceTypeLiveCraft
			: state.Source;
		long familyStableId = 0L;
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyStableId);
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		XjSectArchiveRecord sect = null;
		bool hasSect = sectId > 0L
			&& XjSectRepository.TryGetBySectId(sectId, out sect)
			&& sect != null;
		bool routeToSect = hasSect && (familyStableId <= 0L || ShouldRouteSurplusCraftToSect(familyStableId, sectId));
		bool stored;
		if (routeToSect)
		{
			stored = XjFamilyFaBaoWarehouse.AddFaBaoToSect(
				actorId,
				SafeActorName(actor),
				sectId,
				sect.Name,
				state.Id,
				state.Name,
				state.DaoTu,
				state.ClassName,
				source,
				state.Year);
			if (stored)
			{
				familyStableId = 0L;
			}
			else if (familyStableId > 0L)
			{
				// 宗门器库写入失败时退回本族器库，余器不能因分流失败而消失。
				stored = XjFamilyFaBaoWarehouse.AddFaBaoToFamily(
					actorId,
					SafeActorName(actor),
					familyStableId,
					state.Id,
					state.Name,
					state.DaoTu,
					state.ClassName,
					source,
					state.Year);
				sectId = 0L;
			}
		}
		else if (familyStableId > 0L)
		{
			stored = XjFamilyFaBaoWarehouse.AddFaBaoToFamily(
				actorId,
				SafeActorName(actor),
				familyStableId,
				state.Id,
				state.Name,
				state.DaoTu,
				state.ClassName,
				source,
				state.Year);
			sectId = 0L;
		}
		else
		{
			stored = false;
			sectId = 0L;
		}

		if (!stored)
		{
			return false;
		}

		RegisterKnownName(state.Name);
		RecordFaBaoWorldHistory(actor, state, isUpgrade: false, familyStableId, sectId);
		if (familyStableId > 0L)
		{
			XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.FaBaoObtained(
				actor, state.Id, state.Name, state.DaoTu, state.Source, state.ClassName));
		}
		return true;
	}

	private static bool ShouldRouteSurplusCraftToSect(long familyStableId, long sectId)
	{
		if (sectId <= 0L) return false;
		int sectOwnCount = XjFamilyFaBaoWarehouse.CountSectEntries(sectId);
		if (sectOwnCount <= 0) return true;
		int familyCount = XjFamilyFaBaoWarehouse.CountFamilyEntries(familyStableId);
		// 个人主器仍归家族传承；只有装备位已满后的余器按约 1:3 留入宗门器库。
		return sectOwnCount * 3 <= familyCount;
	}

	private static long PersistLiveFaBao(Actor actor, in XjFaBaoState state)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| !state.Found
			|| string.IsNullOrWhiteSpace(state.Id)
			|| string.IsNullOrWhiteSpace(state.Name)
			|| string.IsNullOrWhiteSpace(state.DaoTu)
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyStableId)
			|| familyStableId <= 0L)
		{
			return 0L;
		}

		string source = string.IsNullOrWhiteSpace(state.Source)
			? XjFamilyFaBaoWarehouse.SourceTypeLiveCraft
			: state.Source;
		XjFamilyFaBaoWarehouse.AddFaBaoToFamily(
			actorId,
			SafeActorName(actor),
			familyStableId,
			state.Id,
			state.Name,
			state.DaoTu,
			state.ClassName,
			source,
			state.Year);
		return familyStableId;
	}

	private static void RecordFaBaoWorldHistory(
		Actor actor,
		in XjFaBaoState state,
		bool isUpgrade,
		long familyStableId,
		long sectId = 0L)
	{
		if (!state.Found || string.IsNullOrWhiteSpace(state.Name))
		{
			return;
		}

		long actorId = GetActorId(actor);
		string actorName = SafeActorName(actor);
		string className = string.IsNullOrWhiteSpace(state.ClassName) ? "器物" : state.ClassName.Trim();
		string title = isUpgrade ? className + "升阶" : className + "出世";
		string body = actorName + (isUpgrade ? "将" : "炼成") + "「" + state.Name.Trim() + "」";
		if (!string.IsNullOrWhiteSpace(state.DaoTu))
		{
			body += "，道途" + state.DaoTu.Trim();
		}
		string sourceText = XjDisplayNameSanitizer.EventSource(state.Source);
		if (!string.IsNullOrWhiteSpace(sourceText))
		{
			body += "，源自" + sourceText;
		}

		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Craft,
			title,
			body,
			ResolveFaBaoHistoryImportance(state.ClassName),
			isProtected: XjFaBaoCatalog.IsJinDanFaBao(state.ClassName),
			actorId: actorId,
			actorName: actorName,
			sectId: sectId,
			familyId: familyStableId,
			cityId: actor?.city?.data?.id ?? 0L,
			year: state.Year);
	}

	private static int ResolveFaBaoHistoryImportance(string className)
	{
		if (XjFaBaoCatalog.IsJinDanFaBao(className))
		{
			return 4;
		}
		if (XjFaBaoCatalog.IsZiFuLingBao(className))
		{
			return 3;
		}
		return 2;
	}

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名炼器师" : name.Trim();
		}
		catch
		{
			return "未名炼器师";
		}
	}


	private static bool ShouldAnnounceFaBaoResult(string source)
	{
		string value = (source ?? string.Empty).Trim();
		return string.Equals(value, SourceJinDan, StringComparison.Ordinal)
			|| string.Equals(value, SourceZhuJiRefine, StringComparison.Ordinal)
			|| string.Equals(value, SourceZiFuRefine, StringComparison.Ordinal)
			|| string.Equals(value, SourceLingBaoUpgrade, StringComparison.Ordinal)
			|| string.Equals(value, SourceJieLinGrant, StringComparison.Ordinal)
			|| string.Equals(value, SourceJieLinUpgrade, StringComparison.Ordinal)
			|| string.Equals(value, SourceDongTian, StringComparison.Ordinal);
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


	private static string BuildFaBaoId(Actor actor, string daoTu, string className, int year, int ordinal)
	{
		long actorId = GetActorId(actor);
		string category = XjFaBaoCatalog.ResolveDaoTuCategory(daoTu);
		string tier = XjFaBaoCatalog.IsJinDanFaBao(className) ? "jindan"
			: XjFaBaoCatalog.IsZhuJiFaQi(className) ? "zhuji" : "zifu";
		return "xj_" + tier + "_fabao_" + actorId.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "_" + Math.Max(0, year).ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "_" + Math.Max(0, ordinal).ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "_" + Sanitize(category);
	}

	private static string Sanitize(string value)
	{
		string text = (value ?? string.Empty).Trim();
		return text switch
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
			_ => "unknown"
		};
	}

	private static string ToChineseNumber(int number)
	{
		return number switch
		{
			2 => "二",
			3 => "三",
			4 => "四",
			5 => "五",
			6 => "六",
			7 => "七",
			8 => "八",
			9 => "九",
			10 => "十",
			_ => number.ToString(System.Globalization.CultureInfo.InvariantCulture)
		};
	}

	internal static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
