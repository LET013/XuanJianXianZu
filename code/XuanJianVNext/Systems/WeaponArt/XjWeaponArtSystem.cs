using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.WeaponArt;

/// <summary>
/// 刀、枪、弓、剑四门器艺。角色首次以有效兵器定门后终身不改；
/// 熟练度与感悟均在年度管线结算，战斗热路径只留下当年布尔标记。
/// </summary>
internal static class XjWeaponArtSystem
{
	private const string ItemKeyKind = "xuanjian.fabao.kind";
	private const string ItemKeyRole = "xuanjian.fabao.role";
	private const string ItemKeyClass = "xuanjian.fabao.class";
	private const int MaximumProficiency = 200;
	private const int IntentInsightIntervalYears = 5;
	private const int MaximumIntentInsightAttempts = 10;
	private const int IntentLockReasonNone = 0;
	private const int IntentLockReasonRealm = 1;
	private const int IntentLockReasonAttempts = 2;

	// 原著五条剑意既保留为完整词条，也作为命名结构的范式：
	// “四字物候/心象 + 四字玄理/归真 + 剑”。扩展词库以同一语感拼装，
	// 不再把剑意池限制为五条原名或五组半句的25种简单互换。
	private static readonly string[] CanonicalSwordIntentNames =
	{
		"合生秋羽两仪寒火剑",
		"金露沉桂太阴玄华剑",
		"明月听合玄阙秋光剑",
		"大悛初醒一性禀真剑",
		"青月见合万璘归乡剑"
	};

	// 上半句侧重物候、天时、月色、草木与醒悟之象。
	private static readonly string[] SwordIntentOpeningPhrases =
	{
		"合生秋羽", "金露沉桂", "明月听合", "大悛初醒", "青月见合",
		"素羽回秋", "玉露栖桂", "寒月照川", "清商入梦", "玄霜落叶",
		"白虹饮涧", "青冥抱月", "丹霞拂晓", "星河洗尘", "秋水照心",
		"孤鸿渡雪", "桂魄临江", "松风叩月", "霁华生烟", "玉衡垂光",
		"寒枝听雨", "明河见影", "太素初分", "清露含章", "长庚映海",
		"玄鸟归林", "紫气横秋", "白云出岫", "青莲照水", "寒潭抱璧",
		"流萤入夜", "疏钟醒梦", "星槎渡海", "月桂承霜", "玉羽栖风",
		"清霄鸣鹤", "春山含黛", "霜河渡雁"
	};

	// 下半句侧重阴阳、玄门、性命、星宿、归藏与返真之理。
	private static readonly string[] SwordIntentClosingPhrases =
	{
		"两仪寒火", "太阴玄华", "玄阙秋光", "一性禀真", "万璘归乡",
		"四象清晖", "三玄抱一", "少阴凝魄", "上清含真", "玉枢垂象",
		"太素生明", "元一归藏", "九华映雪", "玄都照夜", "紫府含光",
		"灵台见性", "坎离交泰", "阴阳合德", "五气朝元", "三花聚顶",
		"太虚返照", "一炁归元", "万法同尘", "金庭抱月", "玉阙垂星",
		"玄牝藏真", "无极生化", "两仪归寂", "太阴照魄", "少阳启明",
		"天心见我", "本性还真", "万象归一", "九霄流火", "三垣宿曜",
		"六虚含章", "五岳镇灵", "万璘照世", "玄华抱真", "秋光归寂",
		"寒火照心"
	};

	// 剑仙尊号与剑意彻底分离。此池只负责“××剑仙”中的前缀，
	// 不写入剑意字段，也不再复用数量过少的法宝通用词库。
	private static readonly string[] SwordImmortalTitlePrefixes =
	{
		"秋羽", "若木", "蓝若", "青霄", "白虹", "玄月", "明河", "寒潭", "清商", "玉衡",
		"长庚", "桂魄", "松风", "霁华", "素羽", "玉露", "寒月", "青冥", "丹霞", "星河",
		"秋水", "孤鸿", "玄霜", "白云", "青莲", "流萤", "疏钟", "星槎", "月桂", "玉羽",
		"清露", "春山", "霜河", "照夜", "抱月", "归潮", "听雪", "观澜", "藏锋", "洗尘",
		"照心", "承霜", "栖霞", "鸣玉", "含章", "问道", "垂星", "流火", "宿曜", "归藏",
		"凝魄", "启明", "抱一", "还真", "见性", "返照", "含光", "垂象", "照魄", "归寂",
		"照世", "镇灵", "清晖", "玄华", "秋光", "寒火", "金庭", "玉阙", "玄牝", "太素",
		"无极", "两仪", "三玄", "四象", "五气", "九华", "上清", "灵台", "天心", "本性",
		"万象", "九霄", "三垣", "六虚", "五岳", "青玄", "紫阳", "碧落", "赤城", "云岫",
		"鹤鸣", "雁回", "烟渚", "潮生", "照川", "渡海", "临江", "回雪", "栖风", "鸣泉",
		"含烟", "映海", "归林", "横秋", "出岫", "照水", "抱璧", "入夜", "醒梦", "渡雁",
		"含黛", "承露", "临风", "归鹤", "问月", "听潮", "映雪", "观星", "照霜", "洗月",
		"归云", "宿雨", "凝烟", "落桂", "藏月", "问秋", "照影", "清越", "玄微", "太清",
		"元初", "归元", "守一", "证玄", "抱真", "通微", "返本", "清微", "洞玄", "寂照"
		};


	private static readonly float[] BaseInsightChance = { 0f, 0.50f, 0.25f, 0.10f, 0.0025f };
	private static readonly float[] InsightChanceCap = { 0f, 0.85f, 0.60f, 0.30f, 0.005f };
	private static readonly float[] FlatFailureStep = { 0f, 0.02f, 0.02f, 0.01f, 0.0001f };
	private static readonly float[] FlatFailureCap = { 0f, 0.20f, 0.20f, 0.10f, 0.001f };

	internal static XjWeaponArtState ReadState(Actor actor)
	{
		if (actor?.data == null) return default;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtKind, out string kind);
		if (!XjWeaponArtKinds.IsSupported(kind)) return default;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtRank, out int rank);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtProficiency, out int proficiency);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtFailureCount, out int failures);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtLastInsightYear, out int lastInsightYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtIntentYear, out int intentYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtAlias, out string alias);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtManualId, out string manualId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtManualName, out string manualName);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtManualGrade, out int manualGrade);
		string normalizedKind = kind.Trim();
		proficiency = Math.Clamp(proficiency, 0, MaximumProficiency);
		if (!string.Equals(normalizedKind, XjWeaponArtKinds.Sword, StringComparison.Ordinal))
		{
			alias = string.Empty;
		}
		return new XjWeaponArtState(true, normalizedKind, rank, proficiency, failures, lastInsightYear,
			intentYear, alias, manualId, manualName, manualGrade);
	}

	internal static bool HasBoundKind(Actor actor, out string kind)
	{
		kind = string.Empty;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtKind, out string stored)
			|| !XjWeaponArtKinds.IsSupported(stored)) return false;
		kind = stored.Trim();
		return true;
	}

	internal static bool CanEquipWeapon(Actor actor, Item item)
	{
		if (actor?.data == null || item?.data == null) return true;
		EquipmentAsset asset = item.getAsset();
		if (asset == null || asset.equipment_type != EquipmentType.Weapon) return true;

		bool isCultivator = XjRealmSuppression.GetRealmTier(actor) > XjRealmSuppression.TierNone;
		bool bound = HasBoundKind(actor, out string boundKind);
		if (!isCultivator && !bound) return true;

		string candidate = ResolveItemKindForActor(actor, item);
		if (!bound)
		{
			// 修士只能从刀、枪、弓、剑中择一为终身器艺；WorldBox 原生 sword_*
			// 属于“刀剑”共同候选，首次装备时再按角色稳定种子确定刀或剑。
			return XjWeaponArtKinds.IsEquipmentCandidate(candidate);
		}
		return IsKindCompatible(boundKind, candidate);
	}

	internal static void ObserveEquippedWeapon(Actor actor, Item item, int currentYear)
	{
		if (actor?.data == null || item?.data == null) return;
		EquipmentAsset asset = item.getAsset();
		if (asset == null || asset.equipment_type != EquipmentType.Weapon) return;
		if (HasBoundKind(actor, out string existingKind))
		{
			TryStampAmbiguousNativeWeapon(item, existingKind);
			return;
		}
		if (XjRealmSuppression.GetRealmTier(actor) <= XjRealmSuppression.TierNone) return;
		string candidate = ResolveItemKindForActor(actor, item);
		if (!XjWeaponArtKinds.IsEquipmentCandidate(candidate)) return;
		string kind = ResolveBindingKind(actor, candidate);
		if (!XjWeaponArtKinds.IsSupported(kind)) return;
		BindKind(actor, kind, currentYear, "首次持器");
	}

	internal static void BindFromGeneratedArtifact(Actor actor, in XjFaBaoState state)
	{
		if (actor?.data == null || !state.Found
			|| !string.Equals(XjFaBaoCatalog.NormalizeRole(state.Kind, state.Role), XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal)
			|| !XjWeaponArtKinds.IsSupported(state.Kind)
			|| HasBoundKind(actor, out _)) return;
		BindKind(actor, state.Kind, state.Year, state.ClassName);
	}

	internal static string ForceGeneratedAttackKind(Actor actor, string requestedKind)
	{
		if (HasBoundKind(actor, out string boundKind)) return boundKind;
		return XjWeaponArtKinds.IsSupported(requestedKind) ? requestedKind.Trim() : string.Empty;
	}

	internal static bool IsKindCompatible(string boundKind, string candidateKind)
	{
		string bound = (boundKind ?? string.Empty).Trim();
		string candidate = (candidateKind ?? string.Empty).Trim();
		if (!XjWeaponArtKinds.IsSupported(bound) || !XjWeaponArtKinds.IsSupported(candidate)) return false;
		return string.Equals(bound, candidate, StringComparison.Ordinal);
	}

	private static string ResolveBindingKind(Actor actor, string candidateKind)
	{
		string candidate = (candidateKind ?? string.Empty).Trim();
		if (XjWeaponArtKinds.IsSupported(candidate)) return candidate;
		if (!string.Equals(candidate, XjWeaponArtKinds.NativeBladeSword, StringComparison.Ordinal) || actor?.data == null) return string.Empty;
		long actorId = ((BaseSystemData)actor.data).id;
		return XjDeterministicHash.PositiveIndex(actorId, "weapon_art_native_blade_sword", 2) == 0
			? XjWeaponArtKinds.Blade
			: XjWeaponArtKinds.Sword;
	}

	internal static string ResolveItemKindForActor(Actor actor, Item item)
	{
		string candidate = ResolveItemKind(item);
		if (!string.Equals(candidate, XjWeaponArtKinds.NativeBladeSword, StringComparison.Ordinal)) return candidate;
		string resolved = HasBoundKind(actor, out string boundKind)
			&& (string.Equals(boundKind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
				|| string.Equals(boundKind, XjWeaponArtKinds.Blade, StringComparison.Ordinal))
			? boundKind
			: ResolveBindingKind(actor, candidate);
		if (XjWeaponArtKinds.IsSupported(resolved)) TryStampAmbiguousNativeWeapon(item, resolved);
		return resolved;
	}

	private static void TryStampAmbiguousNativeWeapon(Item item, string resolvedKind)
	{
		if (item?.data == null || !XjWeaponArtKinds.IsSupported(resolvedKind)) return;
		item.data.get(ItemKeyKind, out string storedKind, string.Empty);
		if (XjWeaponArtKinds.IsSupported(storedKind)) return;
		EquipmentAsset asset = item.getAsset();
		string id = asset == null ? string.Empty : (((Asset)asset).id ?? string.Empty).Trim().ToLowerInvariant();
		if (id.Length == 0 || !HasAnyToken(id, "sword")
			|| HasAnyToken(id, "katana", "saber", "sabre", "scimitar", "knife", "blade", "_dao", "dao_", "jian")) return;
		item.data.set(ItemKeyKind, resolvedKind.Trim());
	}

	internal static string ResolveItemKind(Item item)
	{
		if (item?.data == null) return string.Empty;
		item.data.get(ItemKeyKind, out string storedKind, string.Empty);
		if (XjWeaponArtKinds.IsSupported(storedKind)) return storedKind.Trim();

		EquipmentAsset asset = item.getAsset();
		string id = asset == null ? string.Empty : (((Asset)asset).id ?? string.Empty).Trim().ToLowerInvariant();
		if (id.Length == 0) return string.Empty;
		if (HasAnyToken(id, "katana", "saber", "sabre", "scimitar", "knife", "blade", "_dao", "dao_")) return XjWeaponArtKinds.Blade;
		if (HasAnyToken(id, "sword")) return XjWeaponArtKinds.NativeBladeSword;
		if (HasAnyToken(id, "jian")) return XjWeaponArtKinds.Sword;
		if (HasAnyToken(id, "spear", "lance", "pike", "halberd", "qiang")) return XjWeaponArtKinds.Spear;
		if (HasAnyToken(id, "bow", "crossbow", "gong")) return XjWeaponArtKinds.Bow;
		return string.Empty;
	}

	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0
			|| XjRealmSuppression.GetRealmTier(actor) <= XjRealmSuppression.TierNone) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		RepairLegacyState(actor);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtLastAnnualYear, out int processedYear)
			&& processedYear == currentYear) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtLastAnnualYear, currentYear);

		Item weapon = actor.equipment?.getSlot(EquipmentType.Weapon)?.getItem();
		if (!HasBoundKind(actor, out string kind))
		{
			if (weapon?.data == null) return;
			string candidate = ResolveItemKindForActor(actor, weapon);
			if (!XjWeaponArtKinds.IsEquipmentCandidate(candidate)) return;
			string currentKind = ResolveBindingKind(actor, candidate);
			if (!XjWeaponArtKinds.IsSupported(currentKind)) return;
			BindKind(actor, currentKind, currentYear, "旧档持器补录");
			kind = currentKind;
		}
		UpdateIntentLock(actor);
		if (weapon?.data == null || !IsKindCompatible(kind, ResolveItemKindForActor(actor, weapon))) return;

		XjWeaponArtState state = ReadState(actor);
		XjWeaponArtCombatYearState combat = XjWeaponArtCombatTracker.Read(actorId, currentYear);
		int gain = 1;
		if (combat.Participated) gain++;
		if (combat.Killed) gain++;
		if (combat.HigherRealmKill) gain++;
		if (RollExtraProgress(actorId, currentYear, "artifact", ResolveArtifactTrainingChance(actor, weapon))) gain++;
		if (RollExtraProgress(actorId, currentYear, "manual", ResolveManualExtraProgressChance(state.ManualGrade))) gain++;
		if (IsTaiYinOrTaiYang(actor) && RollExtraProgress(actorId, currentYear, "yin_yang", 0.25f)) gain++;
		gain = Math.Clamp(gain, 1, 5);

		int proficiency = Math.Min(MaximumProficiency, state.Proficiency + gain);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtProficiency, proficiency);
		TryAdvanceRank(actor, currentYear, combat, proficiency);
	}

	internal static bool TryGetBonusProfile(Actor actor, out XjFaBaoBonusProfile profile)
	{
		profile = default;
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found || state.Rank <= XjWeaponArtRanks.None) return false;
		Item weapon = actor.equipment?.getSlot(EquipmentType.Weapon)?.getItem();
		if (weapon?.data == null || !IsKindCompatible(state.Kind, ResolveItemKindForActor(actor, weapon))) return false;

		float manualAttack = state.ManualGrade switch
		{
			1 => 0.01f, 2 => 0.02f, 3 => 0.03f, 4 => 0.04f, 5 => 0.06f, 6 => 0.08f, _ => 0f
		};
		bool solar = IsDaoTu(actor, "太阳");
		bool lunar = IsDaoTu(actor, "太阴");
		float attack = 0f;
		float armor = 0f;
		float crit = 0f;
		float accuracy = 0f;
		float speed = 0f;
		float sameRealm = 0f;
		float dodge = 0f;
		float shieldBreak = 0f;

		if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal))
		{
			attack = RankValue(state.Rank, 0.10f, 0.20f, 0.32f, 0.50f);
			crit = RankValue(state.Rank, 4f, 8f, 13f, 20f);
			armor = RankValue(state.Rank, 3f, 6f, 10f, 16f);
			if (state.Rank >= XjWeaponArtRanks.Yi) sameRealm = 10f;
		}
		else if (string.Equals(state.Kind, XjWeaponArtKinds.Blade, StringComparison.Ordinal))
		{
			attack = RankValue(state.Rank, 0.07f, 0.14f, 0.24f, 0.36f);
			armor = RankValue(state.Rank, 4f, 8f, 13f, 19f);
			shieldBreak = RankValue(state.Rank, 3f, 6f, 10f, 15f);
		}
		else if (string.Equals(state.Kind, XjWeaponArtKinds.Spear, StringComparison.Ordinal))
		{
			attack = RankValue(state.Rank, 0.06f, 0.12f, 0.20f, 0.30f);
			armor = RankValue(state.Rank, 3f, 6f, 10f, 14f);
			accuracy = RankValue(state.Rank, 3f, 6f, 10f, 15f);
		}
		else if (string.Equals(state.Kind, XjWeaponArtKinds.Bow, StringComparison.Ordinal))
		{
			attack = RankValue(state.Rank, 0.05f, 0.10f, 0.17f, 0.25f);
			accuracy = RankValue(state.Rank, 6f, 11f, 17f, 24f);
			speed = RankValue(state.Rank, 0.04f, 0.08f, 0.13f, 0.19f);
		}
		attack += manualAttack + (solar ? 0.05f : 0f);
		if (solar) crit += 3f;
		if (lunar)
		{
			accuracy += 5f;
			dodge += 5f;
		}
		profile = new XjFaBaoBonusProfile(
			0f, 0f, attack, 0f, 0f, armor, 0f, 0f,
			dodgeBonus: dodge,
			accuracyBonus: accuracy,
			critBonus: crit,
			attackSpeedBonus: speed,
			sameRealmDamageBonus: sameRealm,
			shieldBreakBonus: shieldBreak);
		return true;
	}

	internal static string BuildDisplaySummary(Actor actor)
	{
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found) return string.Empty;
		string stage = state.Rank <= 0 ? "未入门" : state.Kind + XjWeaponArtRanks.Suffix(state.Rank);
		int next = state.Rank >= XjWeaponArtRanks.Yi ? MaximumProficiency : XjWeaponArtRanks.RequiredProficiency(state.Rank + 1);
		string manual = state.ManualGrade > 0 && !string.IsNullOrWhiteSpace(state.ManualName)
			? ToChineseNumber(state.ManualGrade) + "品《" + state.ManualName.Trim() + "》"
			: "暂无";
		System.Text.StringBuilder builder = new System.Text.StringBuilder(128);
		builder.Append("门类：").Append(state.Kind).AppendLine("艺");
		builder.Append("境界：").AppendLine(stage);
		builder.Append("熟练度：").Append(state.Proficiency.ToString(CultureInfo.InvariantCulture));
		if (state.Rank < XjWeaponArtRanks.Yi) builder.Append('/').Append(next.ToString(CultureInfo.InvariantCulture));
		builder.AppendLine();
		builder.Append("法门：").Append(manual);
		if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& state.Rank >= XjWeaponArtRanks.Yi
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string swordTitle)
			&& !string.IsNullOrWhiteSpace(swordTitle))
		{
			builder.AppendLine().Append("称号：").Append(swordTitle.Trim());
		}
		if (!string.IsNullOrWhiteSpace(state.Alias))
		{
			string aliasLabel = string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
				&& state.Rank >= XjWeaponArtRanks.Yi ? "剑意：" : "别称：";
			builder.AppendLine().Append(aliasLabel).Append(state.Alias.Trim());
		}
		return builder.ToString().TrimEnd();
	}


	internal static bool TryGetLowRealmSwordDisplay(Actor actor, out string title, out string intentSuffix)
	{
		title = string.Empty;
		intentSuffix = string.Empty;
		if (actor?.data == null || XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierZiFu) return false;
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found
			|| !string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			|| state.Rank < XjWeaponArtRanks.Yi) return false;

		// “××剑”只是剑元阶段的器艺别称，不得写入人物姓名。
		// 只有真正悟得剑意后，角色才获得“××剑仙”尊号；长剑意仍只显示在玄鉴照录中。
		return EnsureSwordImmortalTitle(actor, out title);
	}

	internal static bool TryGetLowRealmSwordTitle(Actor actor, out string title)
	{
		return TryGetLowRealmSwordDisplay(actor, out title, out _);
	}

	internal static bool TryGetSwordIntent(Actor actor, out string swordIntent)
	{
		swordIntent = string.Empty;
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found
			|| !string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			|| state.Rank < XjWeaponArtRanks.Yi
			|| string.IsNullOrWhiteSpace(state.Alias)) return false;
		swordIntent = state.Alias.Trim();
		return true;
	}

	internal static bool EnsureSwordImmortalTitle(Actor actor, out string title)
	{
		title = string.Empty;
		if (actor?.data == null) return false;
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found
			|| !string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			|| state.Rank < XjWeaponArtRanks.Yi) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		string candidateTitle = NormalizeSwordImmortalTitle(storedTitle);
		if (string.IsNullOrWhiteSpace(candidateTitle) || IsSwordAliasClaimedByOther(actor, candidateTitle))
		{
			// 优先把剑元阶段已经存在的“××剑”尊号升格为“××剑仙”。
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string previousTitle))
			{
				candidateTitle = NormalizeSwordImmortalTitle(previousTitle);
			}
			if (string.IsNullOrWhiteSpace(candidateTitle) || IsSwordAliasClaimedByOther(actor, candidateTitle))
			{
				candidateTitle = BuildSwordImmortalTitle(actor);
			}
		}
		if (string.IsNullOrWhiteSpace(candidateTitle)) return false;
		title = candidateTitle;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, title);
		return true;
	}

	private static string NormalizeSwordImmortalTitle(string value)
	{
		string title = (value ?? string.Empty).Trim();
		if (title.EndsWith("剑仙", StringComparison.Ordinal)) return title;
		// 旧版把短别称“××剑”直接当成尊号；迁移为“××剑仙”。
		if (title.Length <= 6 && title.EndsWith("剑", StringComparison.Ordinal))
		{
			return title.Substring(0, title.Length - 1) + "剑仙";
		}
		return string.Empty;
	}

	private static string BuildSwordImmortalTitle(Actor actor)
	{
		if (actor?.data == null) return string.Empty;
		long actorId = ((BaseSystemData)actor.data).id;
		XjFaBaoState faBao = XjFaBaoAccessor.BuildState(actor);
		if (faBao.Found && string.Equals(faBao.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(faBao.Name))
		{
			string name = faBao.Name.Trim();
			if (name.EndsWith("剑", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 1);
			string prefix = name.Length >= 2 ? name.Substring(0, 2) : name;
			string artifactTitle = prefix + "剑仙";
			if (!IsSwordAliasClaimedByOther(actor, artifactTitle)) return artifactTitle;
		}
		int count = SwordImmortalTitlePrefixes.Length;
		int start = XjDeterministicHash.PositiveIndex(actorId, "weapon_art_sword_immortal_title_v2", count);
		for (int offset = 0; offset < count; offset++)
		{
			string candidate = SwordImmortalTitlePrefixes[(start + offset) % count] + "剑仙";
			if (!IsSwordAliasClaimedByOther(actor, candidate)) return candidate;
		}
		// 极端长档才回退到通用器物词，不让常规角色共享狭窄旧池。
		count = XjFaBaoCatalog.CommonWords.Length;
		start = XjDeterministicHash.PositiveIndex(actorId, "weapon_art_sword_immortal_title_fallback", count);
		for (int offset = 0; offset < count; offset++)
		{
			string candidate = XjFaBaoCatalog.CommonWords[(start + offset) % count] + "剑仙";
			if (!IsSwordAliasClaimedByOther(actor, candidate)) return candidate;
		}
		return "玄鉴" + actorId.ToString(CultureInfo.InvariantCulture) + "剑仙";
	}

	private static void BindKind(Actor actor, string kind, int year, string source)
	{
		if (actor?.data == null || !XjWeaponArtKinds.IsSupported(kind) || HasBoundKind(actor, out _)) return;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtKind, kind.Trim());
		if (!string.Equals(kind.Trim(), XjWeaponArtKinds.Sword, StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, string.Empty);
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtRank, XjWeaponArtRanks.None);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtProficiency, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtFailureCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtLastInsightYear, Math.Max(0, year));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtIntentSource, source ?? string.Empty);
		UpdateIntentLock(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		try { actor.updateStats(); } catch { }
	}

	private static void RepairLegacyState(Actor actor)
	{
		if (actor?.data == null) return;
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found) return;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtProficiency, out int storedProficiency);
		bool legacyOverflowIntent = storedProficiency >= 900 && state.Rank >= XjWeaponArtRanks.Yi;
		int normalizedProficiency = Math.Clamp(storedProficiency, 0, MaximumProficiency);
		if (storedProficiency != normalizedProficiency)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtProficiency, normalizedProficiency);
		}
		if (state.Rank == XjWeaponArtRanks.Yuan)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, out int legacyIntentLocked);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, out int lockReason);
			bool legacySwordRealmLock = string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtSwordIntentLocked, out int legacySwordLocked)
				&& legacySwordLocked == 1;

			if (XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierZiFu || legacySwordRealmLock)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 1);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonRealm);
			}
			else if (state.FailureCount >= MaximumIntentInsightAttempts)
			{
				// 十次器意尝试已经耗尽：统一压回上限并永久封闭。
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtFailureCount, MaximumIntentInsightAttempts);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 1);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonAttempts);
			}
			else if (legacyIntentLocked == 1
				&& lockReason == IntentLockReasonNone
				&& state.FailureCount >= 5)
			{
				// alpha.17 的五次上限锁：角色仍在紫府前时解除旧锁，保留失败次数，
				// 继续获得第6—10次机会。紫府锁与旧剑意紫府锁不会进入此分支。
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 0);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonNone);
			}
			state = ReadState(actor);
		}

		// alpha.12—15 的异常档可能把新建器艺直接写到 999 熟练度并同时得到器意。
		// 仅对这一明确异常哨兵回退到“元”，保留门类与法门，再按新概率重新感悟。
		if (legacyOverflowIntent)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtRank, XjWeaponArtRanks.Yuan);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtFailureCount, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentYear, 0);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtIntentSource, "旧版异常器意回退");
			if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal))
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, BuildSwordAlias(actor, XjWeaponArtRanks.Yuan));
			}
			else
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, string.Empty);
			}
			UpdateIntentLock(actor);
			state = ReadState(actor);
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string migratedRealmId))
			{
				XjRealmTitleApplyService.EnsureNoPreZiFuTitle(actor, migratedRealmId);
			}
		}

		if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal))
		{
			// 0.9.7.2—0.9.7.5 曾把剑元阶段的“××剑”别称写进人物尊号，
			// 导致排行榜出现大量“××剑·姓名-筑基”。旧档在年度器艺校准时移除这一泄漏；
			// 已悟剑意的“××剑仙”不受影响。
			if (state.Rank < XjWeaponArtRanks.Yi
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string leakedTitle))
			{
				string normalizedTitle = (leakedTitle ?? string.Empty).Trim();
				if (normalizedTitle.EndsWith("剑", StringComparison.Ordinal)
					&& !normalizedTitle.EndsWith("剑仙", StringComparison.Ordinal))
				{
					XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, string.Empty);
					if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string currentRealmId))
					{
						XjRealmTitleApplyService.EnsureNoPreZiFuTitle(actor, currentRealmId);
					}
				}
			}

			bool aliasNeedsRepair = state.Rank >= XjWeaponArtRanks.Yuan
				&& (string.IsNullOrWhiteSpace(state.Alias)
					|| IsSwordAliasClaimedByOther(actor, state.Alias)
					|| state.Rank >= XjWeaponArtRanks.Yi && IsLegacyGenericSwordIntentAlias(state.Alias));
			if (aliasNeedsRepair)
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, BuildSwordAlias(actor, state.Rank));
				XjRealmTitleApplyService.EnsureNoPreZiFuTitle(actor,
					XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string repairedRealmId) ? repairedRealmId : string.Empty);
			}
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtAlias, out string rawAlias);
		bool hadSwordAlias = !string.IsNullOrWhiteSpace(rawAlias)
			&& (rawAlias.Trim().EndsWith("剑仙", StringComparison.Ordinal) || rawAlias.Trim().EndsWith("剑", StringComparison.Ordinal));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, string.Empty);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		string title = (storedTitle ?? string.Empty).Trim();
		bool hadSwordTitle = title.EndsWith("剑仙", StringComparison.Ordinal) || title.EndsWith("剑", StringComparison.Ordinal);
		if (hadSwordAlias || hadSwordTitle)
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, string.Empty);
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
			{
				XjRealmTitleApplyService.EnsureNoPreZiFuTitle(actor, realmId);
			}
		}
	}


	/// <summary>
	/// 刀意、枪意、弓意、剑意均只允许在紫府之前悟得。角色第一次踏入紫府或更高境界时，
	/// 若其唯一器艺尚未达到“意”，则永久写入封锁；之后即使境界回退也不能重新尝试。
	/// </summary>
	internal static void OnRealmChanged(Actor actor)
	{
		UpdateIntentLock(actor);
	}

	private static void UpdateIntentLock(Actor actor)
	{
		if (actor?.data == null) return;
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found || state.Rank >= XjWeaponArtRanks.Yi) return;

		// alpha.13 的剑意专用锁迁移为四门共用锁。
		if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtSwordIntentLocked, out int legacyLocked)
			&& legacyLocked == 1)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonRealm);
			return;
		}

		if (XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierZiFu) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonRealm);
	}

	private static bool IsIntentPermanentlyLocked(Actor actor, in XjWeaponArtState state)
	{
		if (actor?.data == null || !state.Found || state.Rank >= XjWeaponArtRanks.Yi) return false;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, out int locked) && locked == 1)
		{
			return true;
		}

		// 兼容 alpha.13 已落盘的剑意封锁。
		if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtSwordIntentLocked, out int legacyLocked)
			&& legacyLocked == 1)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonRealm);
			return true;
		}
		return false;
	}

	private static void TryAdvanceRank(Actor actor, int year, in XjWeaponArtCombatYearState combat, int proficiency)
	{
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found || state.Rank >= XjWeaponArtRanks.Yi) return;
		int targetRank = state.Rank + 1;
		if (proficiency < XjWeaponArtRanks.RequiredProficiency(targetRank)) return;
		if (targetRank == XjWeaponArtRanks.Yi)
		{
			UpdateIntentLock(actor);
			if (IsIntentPermanentlyLocked(actor, state)) return;
			if (state.FailureCount >= MaximumIntentInsightAttempts)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 1);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonAttempts);
				return;
			}
			if (state.LastInsightYear > 0 && year - state.LastInsightYear < IntentInsightIntervalYears) return;
		}
		if (state.LastInsightYear == year) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtLastInsightYear, year);

		float chance = ResolveInsightChance(actor, state, targetRank, combat);
		long actorId = ((BaseSystemData)actor.data).id;
		bool success = XjDeterministicHash.Roll01(actorId, year, state.Kind + targetRank, "weapon_art_insight") < chance;
		if (!success)
		{
			int failures = state.FailureCount + 1;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtFailureCount, failures);
			if (targetRank == XjWeaponArtRanks.Yi && failures >= MaximumIntentInsightAttempts)
			{
				// 器意一生最多十次感悟机会；第十次失败后立即永久封闭。
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 1);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonAttempts);
			}
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtRank, targetRank);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtFailureCount, 0);
		if (targetRank >= XjWeaponArtRanks.Yuan && string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal))
		{
			string alias = BuildSwordAlias(actor, targetRank);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, alias);
		}
		else if (!string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, string.Empty);
		}
		if (targetRank >= XjWeaponArtRanks.Yi)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentYear, year);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtIntentSource, ResolveInsightSource(combat));
		}
		TryComprehendOrImproveManual(actor, year, targetRank);
		if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& targetRank >= XjWeaponArtRanks.Yi)
		{
			EnsureSwordImmortalTitle(actor, out _);
		}
		XjRealmTitleApplyService.EnsureNoPreZiFuTitle(actor,
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId) ? realmId : string.Empty);
		WriteRankHistory(actor, year, targetRank);
		XjRuntimeActorInterestIndex.Observe(actor);
		try { actor.updateStats(); } catch { }
	}

	private static float ResolveInsightChance(Actor actor, in XjWeaponArtState state, int targetRank, in XjWeaponArtCombatYearState combat)
	{
		float chance = BaseInsightChance[targetRank];
		chance *= ResolveRealmMultiplier(actor);
		chance *= ResolveHuiGuangMultiplier(actor);
		chance *= ResolveAptitudeMultiplier(actor);
		chance *= ResolveManualInsightMultiplier(state.ManualGrade);
		chance *= combat.HigherRealmKill ? 1.35f : combat.Killed ? 1.20f : combat.Participated ? 1.10f : 1f;
		if (IsTaiYinOrTaiYang(actor))
		{
			chance *= 1.25f;
			if (targetRank == XjWeaponArtRanks.Yi && string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)) chance *= 1.10f;
		}
		chance += Math.Min(FlatFailureCap[targetRank], state.FailureCount * FlatFailureStep[targetRank]);
		float cap = InsightChanceCap[targetRank];
		// “意”属于紫府前的稀有定局：常规单次概率最多约千分之五；
		// 太阴、太阳保留既定优势，但也只放宽到千分之七点五。
		if (targetRank == XjWeaponArtRanks.Yi && IsTaiYinOrTaiYang(actor)) cap = 0.0075f;
		return Math.Clamp(chance, 0f, cap);
	}

	private static float ResolveRealmMultiplier(Actor actor)
	{
		int tier = XjRealmSuppression.GetRealmTier(actor);
		if (tier >= XjRealmSuppression.TierJinDan) return 1.30f;
		if (tier >= XjRealmSuppression.TierZiFu) return 1.10f;
		if (tier >= XjRealmSuppression.TierZhuJi) return 0.90f;
		if (tier >= XjRealmSuppression.TierLianQi) return 0.60f;
		return 0.35f;
	}

	private static float ResolveHuiGuangMultiplier(Actor actor)
	{
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		huiGuang = XjFaBaoBonusService.GetEffectiveHuiGuang(actor, Math.Max(0f, huiGuang));
		if (huiGuang >= 120f) return 1.75f;
		if (huiGuang >= 90f) return 1.50f;
		if (huiGuang >= 60f) return 1.25f;
		if (huiGuang >= 30f) return 1.00f;
		return 0.80f;
	}

	private static float ResolveAptitudeMultiplier(Actor actor)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		float value = aptitude switch
		{
			1 => 0.80f,
			2 => 1.00f,
			3 => 1.15f,
			4 => 1.30f,
			5 => 1.45f,
			6 or 7 => 1.65f,
			8 or 9 => 0.70f,
			_ => 0.90f
		};
		return value;
	}

	private static float ResolveManualInsightMultiplier(int grade) => grade switch
	{
		1 => 1.05f, 2 => 1.10f, 3 => 1.15f, 4 => 1.25f, 5 => 1.40f, 6 => 1.60f, _ => 1f
	};

	private static float ResolveManualExtraProgressChance(int grade) => grade switch
	{
		1 => 0.05f, 2 => 0.10f, 3 => 0.15f, 4 => 0.25f, 5 => 0.35f, 6 => 0.50f, _ => 0f
	};

	private static float ResolveArtifactTrainingChance(Actor actor, Item weapon)
	{
		if (weapon?.data == null) return 0f;
		weapon.data.get(ItemKeyClass, out string itemClass, string.Empty);
		if (XjFaBaoCatalog.IsJinDanFaBao(itemClass)) return 0.60f;
		if (XjFaBaoCatalog.IsZiFuLingBao(itemClass)) return 0.40f;
		if (XjFaBaoCatalog.IsZhuJiFaQi(itemClass)) return 0.20f;
		XjFaBaoState state = XjFaBaoAccessor.BuildState(actor);
		if (state.Found && IsKindCompatible(state.Kind, ResolveItemKindForActor(actor, weapon)))
		{
			if (XjFaBaoCatalog.IsJinDanFaBao(state.ClassName)) return 0.60f;
			if (XjFaBaoCatalog.IsZiFuLingBao(state.ClassName)) return 0.40f;
			if (XjFaBaoCatalog.IsZhuJiFaQi(state.ClassName)) return 0.20f;
		}
		return 0f;
	}

	private static bool RollExtraProgress(long actorId, int year, string salt, float chance)
	{
		return chance > 0f && XjDeterministicHash.Roll01(actorId, year, "weapon_art_progress", salt) < Math.Clamp(chance, 0f, 1f);
	}

	private static void TryComprehendOrImproveManual(Actor actor, int year, int rank)
	{
		if (actor?.data == null || rank <= XjWeaponArtRanks.None) return;
		XjWeaponArtState state = ReadState(actor);
		long actorId = ((BaseSystemData)actor.data).id;
		int currentGrade = state.ManualGrade;
		int maximumGrade = rank switch
		{
			XjWeaponArtRanks.Mang => 2,
			XjWeaponArtRanks.Qi => 3,
			XjWeaponArtRanks.Yuan => 5,
			XjWeaponArtRanks.Yi => 6,
			_ => 0
		};
		if (currentGrade >= maximumGrade) return;
		float chance = currentGrade <= 0 ? 0.25f + rank * 0.10f : 0.12f + rank * 0.08f;
		chance *= ResolveHuiGuangMultiplier(actor);
		if (IsTaiYinOrTaiYang(actor)) chance *= 1.15f;
		if (XjDeterministicHash.Roll01(actorId, year, state.Kind, "weapon_art_manual") >= Math.Clamp(chance, 0f, 0.80f)) return;
		int nextGrade = currentGrade <= 0 ? Math.Min(maximumGrade, Math.Max(1, rank)) : Math.Min(maximumGrade, currentGrade + 1);
		string name = BuildManualName(actor, state.Kind, nextGrade);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtManualId,
			"weapon_art_manual_" + actorId.ToString(CultureInfo.InvariantCulture) + "_" + state.Kind);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtManualName, name);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtManualGrade, nextGrade);
	}

	private static string BuildManualName(Actor actor, string kind, int grade)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		string prefix = SwordImmortalTitlePrefixes[XjDeterministicHash.PositiveIndex(actorId + grade, "weapon_art_manual_prefix_v2", SwordImmortalTitlePrefixes.Length)];
		string daoWord = string.IsNullOrWhiteSpace(daoTu) ? string.Empty : daoTu.Trim();
		string suffix = kind switch
		{
			XjWeaponArtKinds.Sword => grade >= 6 ? "剑经" : grade >= 5 ? "剑诀" : "剑法",
			XjWeaponArtKinds.Blade => "刀法",
			XjWeaponArtKinds.Spear => "枪法",
			XjWeaponArtKinds.Bow => "弓术",
			_ => "器艺法门"
		};
		return prefix + daoWord + suffix;
	}

	private static string BuildSwordAlias(Actor actor, int rank)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		if (rank < XjWeaponArtRanks.Yi)
		{
			string prefix = string.Empty;
			XjFaBaoState faBao = XjFaBaoAccessor.BuildState(actor);
			if (faBao.Found && string.Equals(faBao.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
				&& !string.IsNullOrWhiteSpace(faBao.Name))
			{
				string name = faBao.Name.Trim();
				if (name.EndsWith("剑", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 1);
				prefix = name.Length >= 2 ? name.Substring(0, 2) : name;
			}
			if (!string.IsNullOrWhiteSpace(prefix))
			{
				string artifactCandidate = prefix + "剑";
				if (!IsSwordAliasClaimedByOther(actor, artifactCandidate)) return artifactCandidate;
			}
			int count = SwordImmortalTitlePrefixes.Length;
			int start = XjDeterministicHash.PositiveIndex(actorId, "weapon_art_sword_alias_v2", count);
			for (int offset = 0; offset < count; offset++)
			{
				string candidate = SwordImmortalTitlePrefixes[(start + offset) % count] + "剑";
				if (!IsSwordAliasClaimedByOther(actor, candidate)) return candidate;
			}
			return "玄鉴" + actorId.ToString(CultureInfo.InvariantCulture) + "剑";
		}

		int openingCount = SwordIntentOpeningPhrases.Length;
		int closingCount = SwordIntentClosingPhrases.Length;
		int combinationCount = openingCount * closingCount;
		int canonicalCount = CanonicalSwordIntentNames.Length;
		int candidateCount = canonicalCount + combinationCount;
		int candidateStart = XjDeterministicHash.PositiveIndex(actorId, "weapon_art_sword_intent_expanded", candidateCount);
		HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
		for (int offset = 0; offset < candidateCount; offset++)
		{
			int index = (candidateStart + offset) % candidateCount;
			string candidate;
			if (index < canonicalCount)
			{
				candidate = CanonicalSwordIntentNames[index];
			}
			else
			{
				int combinationIndex = index - canonicalCount;
				candidate = SwordIntentOpeningPhrases[combinationIndex / closingCount]
					+ SwordIntentClosingPhrases[combinationIndex % closingCount]
					+ "剑";
			}
			if (!visited.Add(candidate)) continue;
			if (!IsSwordAliasClaimedByOther(actor, candidate)) return candidate;
		}

		// 极端长档中扩展组合也全部占用后，以人物名嵌入同一八字句式，继续保持可读与全局去重。
		int combinationStart = XjDeterministicHash.PositiveIndex(actorId, "weapon_art_sword_intent_personal", combinationCount);
		string actorName = actor.getName() ?? string.Empty;
		string personal = actorName.Trim();
		int separator = personal.LastIndexOf('·');
		if (separator >= 0 && separator + 1 < personal.Length) personal = personal.Substring(separator + 1);
		int realmSeparator = personal.LastIndexOf('-');
		if (realmSeparator > 0) personal = personal.Substring(0, realmSeparator);
		if (personal.Length > 2) personal = personal.Substring(personal.Length - 2, 2);
		if (string.IsNullOrWhiteSpace(personal)) personal = actorId.ToString(CultureInfo.InvariantCulture);
		for (int offset = 0; offset < combinationCount; offset++)
		{
			int index = (combinationStart + offset) % combinationCount;
			string candidate = SwordIntentOpeningPhrases[index / closingCount]
				+ personal
				+ SwordIntentClosingPhrases[index % closingCount]
				+ "剑";
			if (!IsSwordAliasClaimedByOther(actor, candidate)) return candidate;
		}
		return "合生秋羽" + personal + actorId.ToString(CultureInfo.InvariantCulture) + "两仪寒火剑";
	}

	private static bool IsLegacyGenericSwordIntentAlias(string alias)
	{
		if (string.IsNullOrWhiteSpace(alias)) return false;
		string value = alias.Trim();
		return value.EndsWith("剑仙", StringComparison.Ordinal)
			|| value.Length <= 4 && value.EndsWith("剑", StringComparison.Ordinal);
	}

	private static string NormalizeSwordAliasCollisionKey(string alias)
	{
		string value = (alias ?? string.Empty).Trim().Replace(" ", string.Empty);
		if (value.EndsWith("剑仙", StringComparison.Ordinal))
		{
			value = value.Substring(0, value.Length - 2) + "剑";
		}
		return value;
	}

	private static bool IsSwordAliasClaimedByOther(Actor actor, string alias)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(alias)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		string normalized = NormalizeSwordAliasCollisionKey(alias);
		IReadOnlyList<Actor> snapshot = XjActorRegistry.Snapshot();
		for (int i = 0; i < snapshot.Count; i++)
		{
			Actor other = snapshot[i];
			if (other?.data == null || !other.isAlive() || ((BaseSystemData)other.data).id == actorId) continue;
			if (XjActorAccessor.TryGetString(other, XjActorDataKeys.XjWeaponArtAlias, out string otherAlias)
				&& string.Equals(NormalizeSwordAliasCollisionKey(otherAlias), normalized, StringComparison.Ordinal)) return true;
			if (XjActorAccessor.TryGetString(other, XjActorDataKeys.XjNameTitle, out string otherTitle)
				&& string.Equals(NormalizeSwordAliasCollisionKey(otherTitle), normalized, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static void WriteRankHistory(Actor actor, int year, int rank)
	{
		XjWeaponArtState state = ReadState(actor);
		string stage = state.Kind + XjWeaponArtRanks.Suffix(rank);
		string actorName = actor.getName();
		long actorId = ((BaseSystemData)actor.data).id;
		long familyId = 0L;
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyId);
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		bool swordIntent = rank >= XjWeaponArtRanks.Yi && string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal);
		int importance = swordIntent ? 5 : rank >= XjWeaponArtRanks.Yi ? 3 : rank >= XjWeaponArtRanks.Yuan ? 2 : 1;
		int visibility = (int)XjHistoryVisibility.Personal;
		if (familyId > 0L) visibility |= (int)XjHistoryVisibility.Family;
		if (sectId > 0L) visibility |= (int)XjHistoryVisibility.Sect;
		if (swordIntent) visibility |= (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate);
		string body;
		if (swordIntent)
		{
			EnsureSwordImmortalTitle(actor, out string swordTitle);
			string baseName = ResolveActorBaseName(actor);
			body = baseName + "悟得剑意“" + state.Alias + "”，从此有“" + swordTitle + "”之称。";
		}
		else
		{
			body = actorName + "在长期持" + state.Kind + "修行中悟得" + stage + "。";
		}
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Cultivation,
			actorName + "悟得" + stage,
			body,
			importance,
			isProtected: swordIntent,
			actorId: actorId,
			actorName: actorName,
			sectId: sectId,
			familyId: familyId,
			year: year,
			eventType: "WeaponArtInsight:" + state.Kind + ":" + rank,
			visibilityFlags: visibility,
			result: XjHistoryResult.Success,
			mirrorToWorldLog: swordIntent);
		string personalArt = rank >= XjWeaponArtRanks.Yi && !string.IsNullOrWhiteSpace(state.Alias) ? state.Alias : stage;
		XjThreeBookWriter.RecordWeaponArt(actor, personalArt, year);
		if (swordIntent)
		{
			XjAutoCollectSystem.TryCollectSwordImmortal(actor, "SwordImmortalInsight");
			XjBroadcastSystem.BroadcastSLevelActorEvent(actor, body, iconId: XjEventIconCatalog.GongFaAcquire);
		}
	}

	private static string ResolveActorBaseName(Actor actor)
	{
		if (actor?.data == null) return "未名剑修";
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameBase, out string stored)
			&& !string.IsNullOrWhiteSpace(stored)) return stored.Trim();
		string name = actor.getName() ?? string.Empty;
		int dot = name.IndexOf('·');
		if (dot >= 0 && dot + 1 < name.Length) name = name.Substring(dot + 1);
		int dash = name.IndexOf('-');
		if (dash > 0) name = name.Substring(0, dash);
		return string.IsNullOrWhiteSpace(name) ? "未名剑修" : name.Trim();
	}

	private static string ResolveInsightSource(in XjWeaponArtCombatYearState combat)
	{
		if (combat.HigherRealmKill) return "越境斗法";
		if (combat.Killed) return "生死斗法";
		if (combat.Participated) return "持器实战";
		return "年度感悟";
	}

	private static bool IsTaiYinOrTaiYang(Actor actor) => IsDaoTu(actor, "太阴") || IsDaoTu(actor, "太阳");

	private static bool IsDaoTu(Actor actor, string expected)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& string.Equals(daoTu?.Trim(), expected, StringComparison.Ordinal);
	}

	private static float RankValue(int rank, float rank1, float rank2, float rank3, float rank4) => rank switch
	{
		XjWeaponArtRanks.Mang => rank1,
		XjWeaponArtRanks.Qi => rank2,
		XjWeaponArtRanks.Yuan => rank3,
		XjWeaponArtRanks.Yi => rank4,
		_ => 0f
	};

	private static bool HasAnyToken(string value, params string[] tokens)
	{
		if (string.IsNullOrWhiteSpace(value) || tokens == null) return false;
		for (int i = 0; i < tokens.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(tokens[i]) && value.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
		}
		return false;
	}

	private static string ToChineseNumber(int value) => value switch
	{
		1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", 6 => "六", _ => value.ToString(CultureInfo.InvariantCulture)
	};
}
