using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
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
		"寒枝听雨", "明河见影", "太素初分", "清露含章", "白曜映海",
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

	// 器艺法门与剑元阶段短别称的通用词池。剑仙名号不再从这里随机抽取，
	// 而是由角色自己的剑意词组生成，确保名号与剑意同源。
	private static readonly string[] SwordImmortalTitlePrefixes =
	{
		"秋羽", "若木", "蓝若", "青霄", "白虹", "玄月", "明河", "寒潭", "清商", "玉衡",
		"启曜", "桂魄", "松风", "霁华", "素羽", "玉露", "寒月", "青冥", "丹霞", "星河",
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

	// 剑仙名号必须从角色自己的剑意中取字。原著五条剑意使用专属候选，
	// 扩展剑意则按八字剑意的四个二字意象组合。单条剑意可生成十余个
	// 同源名号，既保持“名号—剑意”可辨认，也避免长档中大量重名。
	private static readonly Dictionary<string, string[]> CanonicalSwordImmortalTitleLexicon =
		new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			["合生秋羽两仪寒火剑"] = new[] { "秋羽", "寒火", "两仪", "合生", "秋羽寒火", "合生两仪", "秋羽两仪" },
			["金露沉桂太阴玄华剑"] = new[] { "沉桂", "玄华", "太阴", "金露", "沉桂玄华", "金露太阴", "沉桂太阴" },
			["明月听合玄阙秋光剑"] = new[] { "秋光", "玄阙", "明月", "听合", "听合秋光", "明月玄阙", "听合玄阙" },
			["大悛初醒一性禀真剑"] = new[] { "初醒", "禀真", "一性", "大悛", "初醒禀真", "大悛一性", "初醒一性" },
			["青月见合万璘归乡剑"] = new[] { "归乡", "万璘", "青月", "见合", "见合归乡", "青月万璘", "见合万璘" }
		};

	// A/B/C/D分别是八字剑意中的四个二字意象。顺序本身就是名号词库：
	// 优先使用最具辨识度的后象与心象，再使用跨句组合及完整半句。
	private static readonly string[] SwordImmortalTitlePatternLexicon =
	{
		"B", "D", "A", "C", "AD", "BC", "AB", "CD", "AC", "BD"
	};

	private static readonly string[] SwordImmortalStyleTitlePrefixes =
	{
		"临江", "天心", "藏锋", "照夜", "问月", "听潮", "归云", "寒江", "霜河", "青冥",
		"照心", "承霜", "入夜", "观澜", "洗尘", "问道", "含章", "镇岳", "明河", "清商",
		"秋水", "月魄", "孤鸿", "松风", "星槎", "玉霜", "玄华", "太素", "归藏", "抱真"
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

	private static bool IsFuQiSwordPath(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor)) return false;
		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiLineageId, out string lineage)
			&& string.Equals(lineage, XjFuQiLineageIds.Sword, StringComparison.Ordinal);
	}

	private static bool IsYuZhenDaoTu(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| !XjDaoTuCatalog.TryResolveRootId(daoTu, out string rootId)) return false;
		return string.Equals(rootId, XjDaoTuRootIds.YuZhen, StringComparison.Ordinal);
	}

	private static bool RequiresSwordOnly(Actor actor) => IsFuQiSwordPath(actor) || IsYuZhenDaoTu(actor);

	private static bool CanTrainWeaponArt(Actor actor)
	{
		if (actor?.data == null) return false;
		// 紫府、真人以及更高境界仍可继续修炼既有器艺、积累熟练度和提升法门。
		// 唯一硬边界是“意”必须在紫府等价境界之前悟得；高境未悟意者最多停在元。
		return XjRealmSuppression.GetRealmTier(actor) > XjRealmSuppression.TierNone;
	}

	private static bool CanAttemptSwordIntentAtCurrentRealm(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsZiFuJinDan(actor)) return false;
		int tier = XjRealmSuppression.GetRealmTier(actor);
		if (tier != XjRealmSuppression.TierLianQi && tier != XjRealmSuppression.TierZhuJi) return false;
		return XjTalentOpportunityRules.CanComprehendSwordIntent(actor);
	}

	private static bool EnsureSwordOnlyIdentity(Actor actor, int currentYear, string source)
	{
		if (!RequiresSwordOnly(actor) || actor?.data == null) return false;
		if (HasBoundKind(actor, out string existing))
		{
			if (string.Equals(existing, XjWeaponArtKinds.Sword, StringComparison.Ordinal)) return true;
			// 旧档玉真错误绑定其他器艺时保留进度，但将器艺及法门收口为剑。
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtKind, XjWeaponArtKinds.Sword);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtManualId, string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtManualName, string.Empty);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtManualGrade, 0);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtIntentSource, source ?? string.Empty);
			EnsureManualGradeMatchesRank(actor, Math.Max(1, currentYear));
			XjRuntimeActorInterestIndex.Observe(actor);
			try { actor.updateStats(); } catch (System.Exception xjCaught178) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/WeaponArt/XjWeaponArtSystem.cs:178", xjCaught178); }
			return true;
		}
		BindKind(actor, XjWeaponArtKinds.Sword, Math.Max(1, currentYear), source);
		return HasBoundKind(actor, out string bound)
			&& string.Equals(bound, XjWeaponArtKinds.Sword, StringComparison.Ordinal);
	}

	internal static bool EnsureFuQiSwordIdentity(Actor actor, int currentYear)
	{
		return IsFuQiSwordPath(actor)
			&& EnsureSwordOnlyIdentity(actor, currentYear, "无名剑道接引");
	}

	internal static bool TryReceiveSwordSteleInsight(Actor actor, int currentYear, out string resultText)
	{
		resultText = string.Empty;
		if (actor?.data == null || currentYear <= 0
			|| !CanTrainWeaponArt(actor)
			|| !HasBoundKind(actor, out string kind)
			|| !string.Equals(kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal))
		{
			return false;
		}
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found || state.Rank >= XjWeaponArtRanks.Yi) return false;

		int proficiency = Math.Min(MaximumProficiency, state.Proficiency + 20);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtProficiency, proficiency);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtIntentSource, "长庚剑碑");

		// 剑碑只在角色已经把剑元修到尽头时，提供一次真正的剑意直悟机会；
		// 仍遵守紫府前定局、永久封锁与十次失败上限，不为高境角色补发剑意。
		state = ReadState(actor);
		if (state.Rank == XjWeaponArtRanks.Yuan
			&& proficiency >= XjWeaponArtRanks.RequiredProficiency(XjWeaponArtRanks.Yi)
			&& CanAttemptSwordIntentAtCurrentRealm(actor)
			&& !IsIntentPermanentlyLocked(actor, state)
			&& state.FailureCount < MaximumIntentInsightAttempts)
		{
			long actorId = ((BaseSystemData)actor.data).id;
			float directCap = XjRuntimeSettings.SwordIntentChanceCap;
			float directChance = Math.Clamp(
				directCap * 0.75f * ResolveHuiGuangMultiplier(actor) * ResolveAptitudeMultiplier(actor),
				0f,
				directCap);
			if (XjDeterministicHash.Roll01(actorId, currentYear, "longgeng_sword_stele", "sword_intent") < directChance)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtRank, XjWeaponArtRanks.Yi);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtFailureCount, 0);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtLastInsightYear, currentYear);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentYear, currentYear);
				string alias = BuildSwordAlias(actor, XjWeaponArtRanks.Yi);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, alias);
				EnsureManualGradeMatchesRank(actor, currentYear);
				EnsureSwordImmortalTitle(actor, out _);
				WriteRankHistory(actor, currentYear, XjWeaponArtRanks.Yi);
				XjRuntimeActorInterestIndex.Observe(actor);
				try { actor.updateStats(); } catch (System.Exception xjCaught234) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/WeaponArt/XjWeaponArtSystem.cs:234", xjCaught234); }
				resultText = "于碑文剑痕中悟得剑意“" + alias + "”";
				return true;
			}
		}

		// 未到直悟门槛时，只推进既有剑艺，不凭空跳过芒、气、元三层。
		XjWeaponArtState beforeAdvance = ReadState(actor);
		TryAdvanceRank(actor, currentYear, new XjWeaponArtCombatYearState(true, false, false), proficiency);
		XjWeaponArtState afterAdvance = ReadState(actor);
		resultText = afterAdvance.Rank > beforeAdvance.Rank
			? "观碑悟得" + XjWeaponArtKinds.Sword + XjWeaponArtRanks.Suffix(afterAdvance.Rank)
			: "观碑磨砺剑艺，熟练度提高";
		XjRuntimeActorInterestIndex.Observe(actor);
		try { actor.updateStats(); } catch (System.Exception xjCaught248) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/WeaponArt/XjWeaponArtSystem.cs:248", xjCaught248); }
		return true;
	}

	internal static bool CanEquipWeapon(Actor actor, Item item)
	{
		if (actor?.data == null || item?.data == null) return true;
		EquipmentAsset asset = item.getAsset();
		if (asset == null || asset.equipment_type != EquipmentType.Weapon) return true;

		bool canTrain = CanTrainWeaponArt(actor);
		bool bound = HasBoundKind(actor, out string boundKind);
		if (!canTrain && !bound) return true;

		string candidate = ResolveItemKindForActor(actor, item);
		if (RequiresSwordOnly(actor))
		{
			return string.Equals(candidate, XjWeaponArtKinds.Sword, StringComparison.Ordinal);
		}
		if (!bound)
		{
			// 紫金修士只能从刀、枪、弓、剑中择一为终身器艺；WorldBox 原生 sword_*
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
		if (!CanTrainWeaponArt(actor)) return;
		string candidate = ResolveItemKindForActor(actor, item);
		if (!XjWeaponArtKinds.IsEquipmentCandidate(candidate)) return;
		string kind = ResolveBindingKind(actor, candidate);
		if (!XjWeaponArtKinds.IsSupported(kind)) return;
		BindKind(actor, kind, currentYear, "首次持器");
	}

	internal static void BindFromGeneratedArtifact(Actor actor, in XjFaBaoState state)
	{
		if (actor?.data == null || !state.Found
			|| !CanTrainWeaponArt(actor)
			|| RequiresSwordOnly(actor) && !string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			|| !string.Equals(XjFaBaoCatalog.NormalizeRole(state.Kind, state.Role), XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal)
			|| !XjWeaponArtKinds.IsSupported(state.Kind)
			|| HasBoundKind(actor, out _)) return;
		BindKind(actor, state.Kind, state.Year, state.ClassName);
	}

	internal static string ForceGeneratedAttackKind(Actor actor, string requestedKind)
	{
		if (RequiresSwordOnly(actor)) return XjWeaponArtKinds.Sword;
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
		if (RequiresSwordOnly(actor))
		{
			if (string.Equals(candidate, XjWeaponArtKinds.NativeBladeSword, StringComparison.Ordinal)
				|| string.Equals(candidate, XjWeaponArtKinds.Sword, StringComparison.Ordinal)) return XjWeaponArtKinds.Sword;
			return string.Empty;
		}
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
			|| !CanTrainWeaponArt(actor)
			|| !IsActiveInYear(actor, currentYear)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		RepairLegacyState(actor);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtLastAnnualYear, out int processedYear)
			&& processedYear == currentYear) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtLastAnnualYear, currentYear);

		Item weapon = actor.equipment?.getSlot(EquipmentType.Weapon)?.getItem();
		if (RequiresSwordOnly(actor))
		{
			EnsureSwordOnlyIdentity(actor, currentYear, IsYuZhenDaoTu(actor) ? "玉真剑器定门" : "无名剑道接引");
		}
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
		// 有足够道慧的剑修必须在炼气、筑基阶段完成剑艺积累。
		// XjZz 只保留“修炼承载/推进速度”的轻量加速，不再决定能否悟剑意；
		// 避免尚未抵达剑元就先晋升紫府而永久失去剑意入口。
		if (string.Equals(kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& CanAttemptSwordIntentAtCurrentRealm(actor)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int swordAptitude))
		{
			gain += Math.Clamp(swordAptitude - 3, 1, 3);
		}
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

	internal static bool IsActiveInYear(Actor actor, int annualYear)
	{
		if (actor?.data == null || annualYear <= 0 || !HasAnnualInterest(actor)) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtActivatedYear, out int activatedYear)
			|| activatedYear <= 0)
		{
			activatedYear = annualYear;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtActivatedYear, activatedYear);
		}
		return annualYear >= activatedYear;
	}

	internal static bool HasAnnualInterest(Actor actor)
	{
		if (!CanTrainWeaponArt(actor))
		{
			return false;
		}
		Item weapon = actor.equipment?.getSlot(EquipmentType.Weapon)?.getItem();
		if (RequiresSwordOnly(actor))
		{
			// 旧档若错误绑定了非剑器艺，必须先进入一次年度车道完成纠偏；
			// 正常状态仍只在持有剑器时推进剑艺。
			if (HasBoundKind(actor, out string existing)
				&& !string.Equals(existing, XjWeaponArtKinds.Sword, StringComparison.Ordinal)) return true;
			return weapon?.data != null
				&& string.Equals(ResolveItemKindForActor(actor, weapon), XjWeaponArtKinds.Sword, StringComparison.Ordinal);
		}
		if (HasBoundKind(actor, out _)) return true;
		return weapon?.data != null
			&& XjWeaponArtKinds.IsEquipmentCandidate(ResolveItemKindForActor(actor, weapon));
	}

	internal static bool TryGetBonusProfile(Actor actor, out XjFaBaoBonusProfile profile)
	{
		profile = default;
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found || state.Rank <= XjWeaponArtRanks.None) return false;
		if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& !XjFuQiSwordWorldState.IsCombatDoctrineEstablished) return false;
		Item weapon = actor.equipment?.getSlot(EquipmentType.Weapon)?.getItem();
		bool hasCompatibleWeapon = weapon?.data != null
			&& IsKindCompatible(state.Kind, ResolveItemKindForActor(actor, weapon));
		bool swordArtInternalized = string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& XjSwordDaoCombatSystem.CanUseSwordArtWithoutWeapon(actor);
		if (!hasCompatibleWeapon && !swordArtInternalized) return false;

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
		int effectiveManualGrade = Math.Max(state.ManualGrade, ResolveMinimumManualGradeForRank(state.Rank));
		string manualName = !string.IsNullOrWhiteSpace(state.ManualName)
			? state.ManualName.Trim()
			: (effectiveManualGrade > 0 ? BuildManualName(actor, state.Kind, effectiveManualGrade) : string.Empty);
		string manual = effectiveManualGrade > 0 && !string.IsNullOrWhiteSpace(manualName)
			? ToChineseNumber(effectiveManualGrade) + "品《" + manualName + "》"
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
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtTitle, out string swordTitle)
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

		// 剑仙只是剑意成就的别号，独立保存；人物主尊号始终由境界系统掌管。
		string swordIntent = (state.Alias ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(swordIntent)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtTitle, out string storedSwordTitle);
		string candidateTitle = NormalizeSwordImmortalTitle(storedSwordTitle);
		if (string.IsNullOrWhiteSpace(candidateTitle))
		{
			// 迁移旧档：原主尊号只有与当前剑意同源时，才吸收入独立剑仙别号。
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string legacyMainTitle);
			candidateTitle = NormalizeSwordImmortalTitle(legacyMainTitle);
		}
		if (string.IsNullOrWhiteSpace(candidateTitle)
			|| !IsSwordImmortalTitleAlignedWithIntent(candidateTitle, swordIntent)
			|| IsSwordAliasClaimedByOther(actor, candidateTitle))
		{
			candidateTitle = BuildSwordImmortalTitle(actor, swordIntent);
		}
		if (string.IsNullOrWhiteSpace(candidateTitle)) return false;

		title = candidateTitle;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtTitle, title);
		if (HasRealmTitlePriority(actor))
		{
			// 真人、真君羽士及紫府、金丹等高境只保留境界尊号；旧档若被剑仙覆盖则立即还原。
			RestoreRealmTitleIfSwordTitleLeaked(actor);
		}
		else
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, title);
		}
		return true;
	}

	private static bool HasRealmTitlePriority(Actor actor)
	{
		return actor?.data != null
			&& XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierZiFu;
	}

	private static string ResolveAuthoritativeRealmId(Actor actor)
	{
		if (actor?.data == null) return string.Empty;
		string storedRealm = XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string stored)
			? XjRealmHelper.NormalizeId(stored) : string.Empty;
		string traitRealm = XjRealmHelper.NormalizeId(XjRealmHelper.GetTraitSnapshotForRouter(actor));
		return XjRealmHelper.GetOrder(traitRealm) > XjRealmHelper.GetOrder(storedRealm)
			? traitRealm : storedRealm;
	}

	private static void RestoreRealmTitleIfSwordTitleLeaked(Actor actor)
	{
		if (!HasRealmTitlePriority(actor)) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedMainTitle);
		if (!LooksLikeSwordTitle(storedMainTitle)) return;

		string realmId = ResolveAuthoritativeRealmId(actor);
		if (string.IsNullOrWhiteSpace(realmId)) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, string.Empty);
		XjRealmTitleApplyService.EnsureTitleForRealm(actor, realmId, daoTu);
	}

	private static bool LooksLikeSwordTitle(string value)
	{
		string title = (value ?? string.Empty).Trim();
		return title.EndsWith("剑仙", StringComparison.Ordinal)
			|| title.EndsWith("剑", StringComparison.Ordinal);
	}

	private static string NormalizeSwordImmortalTitle(string value)
	{
		string title = (value ?? string.Empty).Trim();
		if (title.EndsWith("剑仙", StringComparison.Ordinal))
		{
			string prefix = title.Substring(0, title.Length - 2).Trim();
			return prefix.Length > 0 && prefix.Length <= 4 ? title : string.Empty;
		}
		// 旧版把短别称“××剑”直接当成尊号；迁移为“××剑仙”。
		if (title.EndsWith("剑", StringComparison.Ordinal))
		{
			string prefix = title.Substring(0, title.Length - 1).Trim();
			return prefix.Length > 0 && prefix.Length <= 4 ? prefix + "剑仙" : string.Empty;
		}
		return string.Empty;
	}

	private static string BuildSwordImmortalTitle(Actor actor, string swordIntent)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(swordIntent)) return string.Empty;
		long actorId = ((BaseSystemData)actor.data).id;
		List<string> prefixes = BuildSwordImmortalTitlePrefixCandidates(swordIntent);
		if (prefixes.Count == 0) return string.Empty;

		int start = XjDeterministicHash.PositiveIndex(
			actorId, "weapon_art_sword_immortal_title_by_intent_v3|" + swordIntent, prefixes.Count);
		for (int offset = 0; offset < prefixes.Count; offset++)
		{
			string candidate = prefixes[(start + offset) % prefixes.Count] + "剑仙";
			if (!IsSwordAliasClaimedByOther(actor, candidate)) return candidate;
		}

		string core = NormalizeSwordIntentCore(swordIntent);
		string fallback = core.Length >= 2 ? core.Substring(0, 2) : string.Empty;
		return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback + "剑仙";
	}

	private static bool IsSwordImmortalTitleAlignedWithIntent(string title, string swordIntent)
	{
		string normalizedTitle = NormalizeSwordImmortalTitle(title);
		if (string.IsNullOrWhiteSpace(normalizedTitle)) return false;
		string prefix = normalizedTitle.Substring(0, normalizedTitle.Length - 2);
		List<string> candidates = BuildSwordImmortalTitlePrefixCandidates(swordIntent);
		for (int i = 0; i < candidates.Count; i++)
		{
			if (string.Equals(candidates[i], prefix, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static List<string> BuildSwordImmortalTitlePrefixCandidates(string swordIntent)
	{
		List<string> result = new List<string>(20);
		string normalizedIntent = NormalizeSwordIntentName(swordIntent);
		if (CanonicalSwordImmortalTitleLexicon.TryGetValue(normalizedIntent, out string[] canonical))
		{
			for (int i = 0; i < canonical.Length; i++) AddSwordTitlePrefix(result, canonical[i]);
		}

		string core = NormalizeSwordIntentCore(normalizedIntent);
		if (core.Length >= 8)
		{
			string[] words =
			{
				core.Substring(0, 2), core.Substring(2, 2),
				core.Substring(4, 2), core.Substring(6, 2)
			};
			for (int i = 0; i < SwordImmortalTitlePatternLexicon.Length; i++)
			{
				string pattern = SwordImmortalTitlePatternLexicon[i];
				System.Text.StringBuilder builder = new System.Text.StringBuilder(8);
				for (int j = 0; j < pattern.Length; j++)
				{
					int wordIndex = pattern[j] - 'A';
					if (wordIndex >= 0 && wordIndex < words.Length) builder.Append(words[wordIndex]);
				}
				AddSwordTitlePrefix(result, builder.ToString());
			}
		}
		else if (core.Length >= 2)
		{
			for (int i = 0; i + 1 < core.Length; i += 2)
			{
				AddSwordTitlePrefix(result, core.Substring(i, Math.Min(2, core.Length - i)));
			}
			AddSwordTitlePrefix(result, core);
		}
		AddStyleSwordTitlePrefixes(result, normalizedIntent);
		return result;
	}

	private static void AddStyleSwordTitlePrefixes(List<string> result, string normalizedIntent)
	{
		if (result == null || SwordImmortalStyleTitlePrefixes.Length == 0) return;
		int start = XjDeterministicHash.PositiveIndex(
			XjDeterministicHash.StableHash(normalizedIntent ?? string.Empty),
			"sword_immortal_style_title_prefix_v1",
			SwordImmortalStyleTitlePrefixes.Length);
		for (int i = 0; i < SwordImmortalStyleTitlePrefixes.Length; i++)
		{
			AddSwordTitlePrefix(result, SwordImmortalStyleTitlePrefixes[(start + i) % SwordImmortalStyleTitlePrefixes.Length]);
		}
	}

	private static void AddSwordTitlePrefix(List<string> result, string prefix)
	{
		string normalized = (prefix ?? string.Empty).Trim().Replace(" ", string.Empty);
		if (normalized.Length == 0 || normalized.EndsWith("剑", StringComparison.Ordinal))
		{
			if (normalized.EndsWith("剑", StringComparison.Ordinal))
				normalized = normalized.Substring(0, normalized.Length - 1);
		}
		if (normalized.Length == 0 || normalized.Length > 4) return;
		for (int i = 0; i < result.Count; i++)
		{
			if (string.Equals(result[i], normalized, StringComparison.Ordinal)) return;
		}
		result.Add(normalized);
	}

	private static string NormalizeSwordIntentName(string swordIntent)
	{
		return (swordIntent ?? string.Empty).Trim()
			.Replace(" ", string.Empty)
			.Replace("“", string.Empty)
			.Replace("”", string.Empty)
			.Replace("《", string.Empty)
			.Replace("》", string.Empty);
	}

	private static string NormalizeSwordIntentCore(string swordIntent)
	{
		string normalized = NormalizeSwordIntentName(swordIntent);
		if (normalized.EndsWith("剑意", StringComparison.Ordinal))
			normalized = normalized.Substring(0, normalized.Length - 2);
		else if (normalized.EndsWith("剑", StringComparison.Ordinal))
			normalized = normalized.Substring(0, normalized.Length - 1);
		return normalized;
	}

	internal static bool CompleteFuQiSwordIntent(Actor actor, int year)
	{
		if (!IsFuQiSwordPath(actor)
			|| XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierZiFu
			|| !EnsureFuQiSwordIdentity(actor, year)) return false;
		XjWeaponArtState state = ReadState(actor);
		if (state.Found && state.Rank >= XjWeaponArtRanks.Yi && !string.IsNullOrWhiteSpace(state.Alias))
		{
			XjSwordIntentRegistry.Register(actor, year, state.Alias);
			return true;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtRank, XjWeaponArtRanks.Yi);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtProficiency, MaximumProficiency);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtFailureCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonNone);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentYear, Math.Max(0, year));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtIntentSource, "融汇十六家剑意，自成养青冥");
		string alias = BuildSwordAlias(actor, XjWeaponArtRanks.Yi);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, alias);
		EnsureManualGradeMatchesRank(actor, year);
		EnsureSwordImmortalTitle(actor, out _);
		WriteRankHistory(actor, year, XjWeaponArtRanks.Yi);
		XjRuntimeActorInterestIndex.Observe(actor);
		try { actor.updateStats(); } catch (System.Exception xjCaught732) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/WeaponArt/XjWeaponArtSystem.cs:732", xjCaught732); }
		return true;
	}

	internal static bool TryGrantSwordIntentBySimulator(Actor actor, int year, out string alias)
	{
		alias = string.Empty;
		if (!XjSafeCore.IsAliveActor(actor)) return false;
		XjWeaponArtState existing = ReadState(actor);
		if (existing.Found
			&& existing.Rank >= XjWeaponArtRanks.Yi
			&& string.Equals(existing.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(existing.Alias))
		{
			alias = existing.Alias.Trim();
			if (XjSwordIntentRegistry.Count < 16)
			{
				XjSwordIntentRegistry.Register(actor, Math.Max(0, year), alias);
			}
			return true;
		}
		if (XjSwordIntentRegistry.Count >= 16
			|| XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierZiFu) return false;

		int safeYear = Math.Max(0, year);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtKind, XjWeaponArtKinds.Sword);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtActivatedYear, Math.Max(1, safeYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtLastInsightYear, safeYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtRank, XjWeaponArtRanks.Yi);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtProficiency, MaximumProficiency);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtFailureCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLocked, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentLockReason, IntentLockReasonNone);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtIntentYear, safeYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtIntentSource, "陆江仙·照剑天心");
		alias = BuildSwordAlias(actor, XjWeaponArtRanks.Yi);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, alias);
		EnsureManualGradeMatchesRank(actor, safeYear);
		EnsureSwordImmortalTitle(actor, out _);
		WriteRankHistory(actor, safeYear, XjWeaponArtRanks.Yi);
		XjRuntimeActorInterestIndex.Observe(actor);
		try { actor.updateStats(); } catch (System.Exception xjCaught772) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/WeaponArt/XjWeaponArtSystem.cs:772", xjCaught772); }
		return true;
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
		int activationYear = Math.Max(1, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtActivatedYear, activationYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtLastInsightYear, Math.Max(0, year));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtIntentSource, source ?? string.Empty);
		UpdateIntentLock(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		try { actor.updateStats(); } catch (System.Exception xjCaught793) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/WeaponArt/XjWeaponArtSystem.cs:793", xjCaught793); }
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

			if (XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierZiFu
				|| (XjCultivationPathRules.IsZiFuJinDan(actor) && legacySwordRealmLock))
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

		EnsureManualGradeMatchesRank(actor, XjYearTracker.CurrentYear);
		state = ReadState(actor);

		if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal))
		{
			// 旧版曾把剑元别称或剑仙别号写进人物主尊号。剑元泄漏直接移除；
			// 已悟剑意者把剑仙别号迁入独立字段，高境同步恢复真人/真君等境界尊号。
			if (state.Rank >= XjWeaponArtRanks.Yi)
			{
				EnsureSwordImmortalTitle(actor, out _);
			}
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
				if (state.Rank >= XjWeaponArtRanks.Yi) EnsureSwordImmortalTitle(actor, out _);
				XjRealmTitleApplyService.EnsureNoPreZiFuTitle(actor,
					XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string repairedRealmId) ? repairedRealmId : string.Empty);
			}
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtAlias, out string rawAlias);
		bool hadSwordAlias = !string.IsNullOrWhiteSpace(rawAlias)
			&& (rawAlias.Trim().EndsWith("剑仙", StringComparison.Ordinal) || rawAlias.Trim().EndsWith("剑", StringComparison.Ordinal));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtTitle, out string storedSwordTitle);
		bool hadIndependentSwordTitle = !string.IsNullOrWhiteSpace(NormalizeSwordImmortalTitle(storedSwordTitle));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtAlias, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtTitle, string.Empty);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		string title = (storedTitle ?? string.Empty).Trim();
		bool hadSwordTitle = title.EndsWith("剑仙", StringComparison.Ordinal) || title.EndsWith("剑", StringComparison.Ordinal);
		if (hadSwordAlias || hadIndependentSwordTitle || hadSwordTitle)
		{
			if (hadSwordTitle) XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, string.Empty);
			string realmId = ResolveAuthoritativeRealmId(actor);
			if (HasRealmTitlePriority(actor))
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
				XjRealmTitleApplyService.EnsureTitleForRealm(actor, realmId, daoTu);
			}
			else
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

		EnsureManualGradeMatchesRank(actor, XjYearTracker.CurrentYear);

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
			// 服气剑修的一己剑意只能由“128剑气＋16道剑意→养青冥”写入，
			// 通用器艺概率不得提前跳过长庚服气路径的专属修炼。
			if (IsFuQiSwordPath(actor)) return;
			// 紫金剑意只要求炼气/筑基阶段已把剑艺推进到门槛，并具备足够道慧。
			// XjZz 不再作为剑意许可证；紫府以后仍绝不补悟。
			if (string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
				&& !CanAttemptSwordIntentAtCurrentRealm(actor)) return;
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
		try { actor.updateStats(); } catch (System.Exception xjCaught1052) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/WeaponArt/XjWeaponArtSystem.cs:1052", xjCaught1052); }
	}

	private static float ResolveInsightChance(Actor actor, in XjWeaponArtState state, int targetRank, in XjWeaponArtCombatYearState combat)
	{
		bool configuredSwordIntent = targetRank == XjWeaponArtRanks.Yi
			&& string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal);
		float swordIntentCap = XjRuntimeSettings.SwordIntentChanceCap;
		float chance = configuredSwordIntent ? swordIntentCap * 0.50f : BaseInsightChance[targetRank];
		chance *= ResolveRealmMultiplier(actor);
		chance *= ResolveHuiGuangMultiplier(actor);
		chance *= ResolveAptitudeMultiplier(actor);
		chance *= ResolveManualInsightMultiplier(state.ManualGrade);
		chance *= combat.HigherRealmKill ? 1.35f : combat.Killed ? 1.20f : combat.Participated ? 1.10f : 1f;
		if (IsTaiYinOrTaiYang(actor))
		{
			chance *= 1.25f;
			if (configuredSwordIntent) chance *= 1.10f;
		}

		float failureCap = configuredSwordIntent ? swordIntentCap * 0.20f : FlatFailureCap[targetRank];
		float failureStep = configuredSwordIntent ? swordIntentCap * 0.02f : FlatFailureStep[targetRank];
		chance += Math.Min(failureCap, state.FailureCount * failureStep);
		float cap = configuredSwordIntent ? swordIntentCap : InsightChanceCap[targetRank];
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
		if (huiGuang >= 98f) return 1.75f;
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
		if (XjFaBaoCatalog.IsXianQi(itemClass)) return 0.80f;
		if (XjFaBaoCatalog.IsJinDanFaBao(itemClass)) return 0.60f;
		if (XjFaBaoCatalog.IsZiFuLingBao(itemClass)) return 0.40f;
		if (XjFaBaoCatalog.IsZhuJiFaQi(itemClass)) return 0.20f;
		XjFaBaoState state = XjFaBaoAccessor.BuildState(actor);
		if (state.Found && IsKindCompatible(state.Kind, ResolveItemKindForActor(actor, weapon)))
		{
			if (XjFaBaoCatalog.IsXianQi(state.ClassName)) return 0.80f;
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
		int minimumGrade = ResolveMinimumManualGradeForRank(rank);
		int maximumGrade = rank switch
		{
			XjWeaponArtRanks.Mang => 2,
			XjWeaponArtRanks.Qi => 3,
			XjWeaponArtRanks.Yuan => 5,
			XjWeaponArtRanks.Yi => 6,
			_ => 0
		};

		// 器艺境界本身代表角色已经把对应法门推演到足以承载该层次。
		// 因而最低品级不是额外随机奖励；芒/气/元/意分别至少一、三、五、六品。
		if (state.ManualGrade < minimumGrade)
		{
			WriteManual(actor, state.Kind, actorId, minimumGrade);
			state = ReadState(actor);
		}
		if (state.ManualGrade >= maximumGrade) return;

		float chance = 0.12f + rank * 0.08f;
		chance *= ResolveHuiGuangMultiplier(actor);
		if (IsTaiYinOrTaiYang(actor)) chance *= 1.15f;
		if (XjDeterministicHash.Roll01(actorId, year, state.Kind, "weapon_art_manual") >= Math.Clamp(chance, 0f, 0.80f)) return;
		WriteManual(actor, state.Kind, actorId, Math.Min(maximumGrade, state.ManualGrade + 1));
	}

	private static int ResolveMinimumManualGradeForRank(int rank)
	{
		return rank switch
		{
			XjWeaponArtRanks.Mang => 1,
			XjWeaponArtRanks.Qi => 3,
			XjWeaponArtRanks.Yuan => 5,
			XjWeaponArtRanks.Yi => 6,
			_ => 0
		};
	}

	private static void EnsureManualGradeMatchesRank(Actor actor, int year)
	{
		if (actor?.data == null) return;
		XjWeaponArtState state = ReadState(actor);
		if (!state.Found) return;
		int minimumGrade = ResolveMinimumManualGradeForRank(state.Rank);
		if (minimumGrade <= 0 || state.ManualGrade >= minimumGrade) return;
		WriteManual(actor, state.Kind, ((BaseSystemData)actor.data).id, minimumGrade);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtLastInsightYear, Math.Max(0, year));
	}

	private static void WriteManual(Actor actor, string kind, long actorId, int grade)
	{
		if (actor?.data == null || grade <= 0 || !XjWeaponArtKinds.IsSupported(kind)) return;
		string name = BuildManualName(actor, kind, grade);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtManualId,
			"weapon_art_manual_" + actorId.ToString(CultureInfo.InvariantCulture) + "_" + kind);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjWeaponArtManualName, name);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjWeaponArtManualGrade, grade);
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
		if (XjSwordIntentRegistry.IsNameClaimedByOther(actorId, alias)) return true;
		string normalized = NormalizeSwordAliasCollisionKey(alias);
		IReadOnlyList<Actor> snapshot = XjActorRegistry.Snapshot();
		for (int i = 0; i < snapshot.Count; i++)
		{
			Actor other = snapshot[i];
			if (other?.data == null || !other.isAlive() || ((BaseSystemData)other.data).id == actorId) continue;
			if (XjActorAccessor.TryGetString(other, XjActorDataKeys.XjWeaponArtAlias, out string otherAlias)
				&& string.Equals(NormalizeSwordAliasCollisionKey(otherAlias), normalized, StringComparison.Ordinal)) return true;
			if (XjActorAccessor.TryGetString(other, XjActorDataKeys.XjWeaponArtTitle, out string otherSwordTitle)
				&& string.Equals(NormalizeSwordAliasCollisionKey(otherSwordTitle), normalized, StringComparison.Ordinal)) return true;
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
		if (swordIntent && !string.IsNullOrWhiteSpace(state.Alias))
		{
			XjSwordIntentRegistry.Register(actor, year, state.Alias);
		}
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
			XjBroadcastSystem.ShowRecordedWorldTipCritical(
				body,
				duration: 8f,
				color: "#D94C4C",
				iconId: XjEventIconCatalog.GongFaAcquire);
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
