using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "updateAge")]
	public static void XuanJianVNext_Actor_UpdateAge_RegisterPostfix(Actor __instance)
	{
		// updateAge 是唯一年度触发入口；资格、注册与年度入队在同一路径完成。
		XjScheduler.RegisterAndEnqueueAnnualActor(__instance);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "updateNutritionDecay")]
	public static void XuanJianVNext_Actor_UpdateNutritionDecay_CultivatorStarvationGuard_Prefix(
		Actor __instance,
		[HarmonyArgument(0)] ref bool pDoStarvationDamage)
	{
		if (!pDoStarvationDamage || __instance?.data == null || !XjSafeCore.IsAliveActor(__instance))
		{
			return;
		}

		long actorId = ((BaseSystemData)__instance.data).id;
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier))
		{
			// Never parse the entire trait chain for ordinary starving units. During
			// bootstrap an unindexed persisted cultivator still receives the guard;
			// after bootstrap all eligible actors are served from the O(1) cache.
			if (!XjWorldBootstrapLane.HasPending)
			{
				return;
			}
			realmTier = XjRealmSuppression.GetRealmTier(__instance);
		}
		if (realmTier > XjRealmSuppression.TierNone
			|| XjLongShuSystem.IsLongShu(__instance)
			|| IsShenDan(__instance))
		{
			// 修士仍会消耗食物，但饥饿不会转化为原生掉血或死亡。
			pDoStarvationDamage = false;
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
	public static bool XuanJianVNext_Actor_AddTrait_BlockLightningImmortal_Prefix(
		Actor __instance,
		[HarmonyArgument("pTraitID")] string traitId)
	{
		return !XjSafeCore.IsLightningImmortalTraitBlocked(traitId);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "updateStats")]
	public static void XuanJianVNext_Actor_UpdateStats_RealmLifespanRepair_Prefix(Actor __instance)
	{
		if (__instance?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)__instance.data).id;
		if (!XjRuntimeActorInterestIndex.HasStatInterest(actorId)
			&& (!XjWorldBootstrapLane.HasPending
				|| !XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(__instance)))
		{
			return;
		}

		// 仅已索引的玄鉴角色才检查寿元。旧实现会在每个原生角色的
		// updateStats 中解析境界与特质链，是高人口世界的主要热路径之一。
		XjRealmLifespanService.MarkDirtyWhenStale(__instance);
	}

	/// <summary>
	/// 玄鉴运行期属性必须只在原版 updateStats 真正重建 BaseStats 的路径内合并。
	/// 旧实现使用 Postfix；当人物窗口/排行榜头像调用 updateStats 而原版因 stats 未脏提前返回时，
	/// Postfix 仍会再次乘入境界、法宝、纪元与功法倍率，造成每次打开页面战力复利膨胀。
	/// 此处改为在 BaseStats.normalize 前注入：原版未重建属性时不会执行，且同一次重建只执行一次。
	/// </summary>
	[HarmonyTranspiler]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "updateStats")]
	public static IEnumerable<CodeInstruction> XuanJianVNext_Actor_UpdateStats_RuntimeBonuses_Transpiler(
		IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
		MethodInfo normalizeMethod = AccessTools.Method(typeof(BaseStats), nameof(BaseStats.normalize));
		MethodInfo applyMethod = AccessTools.Method(
			typeof(XjVNextPatches),
			nameof(XuanJianVNext_Actor_UpdateStats_ApplyRuntimeBonuses));

		int normalizeIndex = codes.FindIndex(code =>
			(code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call)
			&& code.operand is MethodInfo method
			&& (method == normalizeMethod
				|| (method.DeclaringType == typeof(BaseStats)
					&& string.Equals(method.Name, nameof(BaseStats.normalize), StringComparison.Ordinal))));

		if (normalizeIndex < 0 || applyMethod == null)
		{
			Debug.LogWarning("[玄鉴] updateStats 未找到 BaseStats.normalize，已跳过运行期属性注入以避免重复累加。");
			return codes;
		}

		CodeInstruction loadActor = new CodeInstruction(OpCodes.Ldarg_0);
		if (codes[normalizeIndex].labels.Count > 0)
		{
			loadActor.labels.AddRange(codes[normalizeIndex].labels);
			codes[normalizeIndex].labels.Clear();
		}

		codes.InsertRange(normalizeIndex, new[]
		{
			loadActor,
			new CodeInstruction(OpCodes.Call, applyMethod)
		});
		return codes;
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "updateStats")]
	public static void XuanJianVNext_Actor_UpdateStats_RefreshCombatCache_Postfix(Actor __instance)
	{
		if (__instance?.data == null || __instance.stats == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)__instance.data).id;
		bool shouldRefresh = XjRuntimeActorInterestIndex.HasCombatInterest(actorId)
			|| XjCombatHotPathCache.Contains(actorId)
			|| (XjWorldBootstrapLane.HasPending
				&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(__instance));
		if (shouldRefresh)
		{
			XjCombatHotPathCache.Refresh(__instance);
		}
	}

	private static void XuanJianVNext_Actor_UpdateStats_ApplyRuntimeBonuses(Actor __instance)
	{
		if (__instance?.data == null || __instance.stats == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)__instance.data).id;
		bool hasRuntimeStatInterest = XjRuntimeActorInterestIndex.HasStatInterest(actorId);
		bool bootstrapCandidate = XjWorldBootstrapLane.HasPending
			&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(__instance);
		if (!hasRuntimeStatInterest && !bootstrapCandidate)
		{
			// 这是原生 updateStats 的逐角色热路径。玄鉴寿元、纪元、功法和
			// 法宝都只可能作用于已索引修士或读档中的落盘候选；凡俗角色
			// 不得在这里再解析境界特质链。
			return;
		}

		// 境界寿元属于基础生存规则，但兴趣索引会在修士入库、特质变更与
		// 读档 bootstrap 时同步维护；因此可安全避免对所有原生角色重复计算。
		XjRealmLifespanService.ApplyRuntimeRealmLifespan(__instance);

		bool isShenDan = IsShenDan(__instance);
		bool hasEraBonus = XjEraBonusService.TryGetActiveProfile(__instance, out XjEraBonusProfile eraProfile);

		// 仅已建索引或确有落盘玄鉴标记的角色进入功法读取。
		XjGongFaState gongFaState = hasRuntimeStatInterest || bootstrapCandidate
			? XjGongFaAccessor.BuildState(__instance)
			: default;
		bool hasGongFaAttributeBonus = gongFaState.Found && gongFaState.Grade > 0;

		bool isYaoXie = XjTrueDamageSystem.IsJinXingYaoXie(__instance);
		bool isLongShu = XjLongShuSystem.IsLongShu(__instance);
		int realmTier;
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out realmTier))
		{
			realmTier = XjRealmSuppression.GetRealmTier(__instance);
		}
		bool isCultivator = realmTier > XjRealmSuppression.TierNone;
		bool hasRealmCombatBonus = XjRealmCombatBonuses.TryGetProfile(realmTier, out XjFaBaoBonusProfile realmProfile);
		bool hasFaBao = XjFaBaoBonusService.TryGetProfile(__instance, out XjFaBaoBonusProfile faBaoProfile);
		bool hasWeaponArt = XjWeaponArtSystem.TryGetBonusProfile(__instance, out XjFaBaoBonusProfile weaponArtProfile);
		if (!isCultivator && !isYaoXie && !isLongShu && !hasFaBao && !hasWeaponArt && !hasRealmCombatBonus && !hasEraBonus && !hasGongFaAttributeBonus && !isShenDan)
		{
			return;
		}

		if (isYaoXie)
		{
			ApplyJinXingYaoXieHealthBonus(__instance);
		}
		if (isLongShu)
		{
			ApplyLongShuRealmMultiplier(__instance);
		}
		if (hasRealmCombatBonus)
		{
			ApplyRealmCombatBonuses(__instance, realmProfile);
		}
		if (hasFaBao)
		{
			ApplyCombatBonusProfile(__instance, faBaoProfile);
			ApplyFaBaoNativeDamageCurve(__instance);
		}
		if (hasWeaponArt)
		{
			ApplyCombatBonusProfile(__instance, weaponArtProfile);
		}
		if (hasEraBonus)
		{
			ApplyEraBonusProfile(__instance, eraProfile);
		}
		if (hasGongFaAttributeBonus)
		{
			ApplyGongFaAttributeMultiplier(__instance, gongFaState.Grade);
		}
		ApplyAlchemyLifespanBonus(__instance);
		if (isLongShu)
		{
			XjLongShuSystem.ClampRuntimeLifespan(__instance);
		}
		if (isShenDan)
		{
			ApplyShenDanStatScalar(__instance);
		}
		if (isCultivator || hasFaBao || hasWeaponArt || hasRealmCombatBonus || isYaoXie || isLongShu || isShenDan)
		{
			EnsureNativeDamageRange(__instance);
		}
		if (isCultivator || hasFaBao || hasWeaponArt || hasRealmCombatBonus || isYaoXie || hasEraBonus || hasGongFaAttributeBonus || isShenDan)
		{
			SuppressVanillaDefenseAndCritical(__instance);
		}
	}

	private static void ApplyAlchemyLifespanBonus(Actor actor)
	{
		if (actor?.data == null || actor.stats == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyLifespanBonus, out int bonus)
			|| bonus <= 0) return;
		float current = XjSafeCore.GetStatSafe(actor, "lifespan", 0f);
		actor.stats["lifespan"] = Math.Max(0f, current) + bonus;
	}

	private static void ApplyJinXingYaoXieHealthBonus(Actor actor)
	{
		// multiplier_health 是“额外倍率”而不是从 1 起算的最终倍率。
		// 统一通过最终生命因子换算，避免把 +15% 错写成 +115%，也避免基础值为 0 时加成失效。
		ApplyHealthMultiplierStat(actor, 1.15f);
	}

	private static void ApplyLongShuRealmMultiplier(Actor __instance)
	{
		try
		{
			XjLongShuSystem.ApplyRealmStatMultiplier(__instance);
		}
		catch
		{
		}
	}

	private static void ApplyRealmCombatBonuses(Actor __instance, in XjFaBaoBonusProfile profile)
	{
		if (__instance?.stats == null)
		{
			return;
		}

		try
		{
			ApplyCombatBonusProfile(__instance, profile);
		}
		catch
		{
		}
	}

	private static void ApplyCombatBonusProfile(Actor actor, in XjFaBaoBonusProfile profile)
	{
		ApplyMultiplierStat(actor, "damage", 1f + profile.AttackBonus);
		ApplyHealthMultiplierStat(actor, 1f + profile.HealthBonus);
		ApplyPercentPointStat(actor, XjSafeCore.ArmorPenPercent, profile.ArmorPenetration);
		ApplyPercentPointStat(actor, XjSafeCore.Vampire, profile.Lifesteal);
		ApplyPercentPointStat(actor, XjSafeCore.Dodge, profile.DodgeBonus);
		ApplyPercentPointStat(actor, XjSafeCore.Healback, profile.HealbackBonus);
		ApplyPercentPointStat(actor, XjSafeCore.Accuracy, profile.AccuracyBonus);
		ApplyPercentPointStat(actor, XjSafeCore.CritChance, profile.CritBonus);
		ApplyPercentPointStat(actor, XjSafeCore.CritTakenReduction, profile.CritTakenReduction);
		ApplyPercentPointStat(actor, XjSafeCore.ShieldRatio, profile.HealthShield);
		ApplyPercentPointStat(actor, XjSafeCore.ShieldBreak, profile.ShieldBreakBonus);
		ApplyPercentPointStat(actor, XjSafeCore.SameRealmDamage, profile.SameRealmDamageBonus);
		ApplyPercentPointStat(actor, XjSafeCore.TrueDamageRatio, profile.TrueDamageRatio);
		ApplyMultiplierStat(actor, "attack_speed", 1f + profile.AttackSpeedBonus);
		ApplyMultiplierStat(actor, "lifespan", 1f + profile.LifespanBonus);
		ApplyPercentPointStat(actor, XjSafeCore.DamageReduce, profile.DamageReduction);
	}

	private static void ApplyFaBaoNativeDamageCurve(Actor actor)
	{
		XjFaBaoState state = XjFaBaoAccessor.BuildState(actor);
		if (!state.Found || actor?.stats == null) return;

		float bonus = XjFaBaoCatalog.IsJinDanFaBao(state.ClassName)
			? 9000f
			: XjFaBaoCatalog.IsZiFuLingBao(state.ClassName)
				? 3500f
				: XjFaBaoCatalog.IsZhuJiFaQi(state.ClassName) ? 0f : 0f;
		if (bonus <= 0f) return;

		// Base equipment damage is 1000. This produces a stable native curve:
		// 法器 1000, 灵宝 4500, 法宝 10000, before existing realm multipliers.
		float current = XjSafeCore.GetStatSafe(actor, "damage", 0f);
		actor.stats["damage"] = Math.Max(0f, current) + bonus;
	}

	private static void ApplyEraBonusProfile(Actor actor, in XjEraBonusProfile profile)
	{
		ApplyMultiplierStat(actor, "damage", 1f + profile.AttackBonus);
		ApplyMultiplierStat(actor, "health", 1f + profile.HealthBonus);
		ApplyMultiplierStat(actor, "speed", 1f + profile.MovementSpeedBonus);
		ApplyMultiplierStat(actor, "attack_speed", 1f + profile.AttackSpeedBonus);
		ApplyPercentPointStat(actor, XjSafeCore.Dodge, profile.DodgeBonus);
		ApplyPercentPointStat(actor, XjSafeCore.Healback, profile.HealbackBonus);
		ApplyPercentPointStat(actor, XjSafeCore.CritChance, profile.CritBonus);
		ApplyPercentPointStat(actor, XjSafeCore.ArmorPenPercent, profile.ArmorPenetration);
		ApplyPercentPointStat(actor, XjSafeCore.DamageReduce, profile.DamageReduction);
	}

	private static void ApplyGongFaAttributeMultiplier(Actor actor, int grade)
	{
		float multiplier = XjGongFaBonusRules.GetAttributeMultiplier(actor, grade);
		if (multiplier <= 1f)
		{
			return;
		}

		// 功法属性加成正式接入基础战斗属性：攻击、生命、移动速度、攻击速度。
		ApplyMultiplierStat(actor, "damage", multiplier);
		ApplyHealthMultiplierStat(actor, multiplier);
		ApplyMultiplierStat(actor, "speed", multiplier);
		ApplyMultiplierStat(actor, "attack_speed", multiplier);
	}

	private static bool IsShenDan(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	private static void ApplyShenDanStatScalar(Actor actor)
	{
		const float ShenDanScalar = 0.9f;
		ApplyMultiplierStat(actor, "damage", ShenDanScalar);
		ApplyHealthMultiplierStat(actor, ShenDanScalar);
		ApplyMultiplierStat(actor, "speed", ShenDanScalar);
		ApplyMultiplierStat(actor, "attack_speed", ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.ArmorPenPercent, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.Vampire, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.Dodge, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.Healback, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.Accuracy, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.CritChance, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.CritTakenReduction, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.ShieldRatio, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.ShieldBreak, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.SameRealmDamage, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.TrueDamageRatio, ShenDanScalar);
		ApplyMultiplierStat(actor, XjSafeCore.DamageReduce, ShenDanScalar);
	}

	private static void SuppressVanillaDefenseAndCritical(Actor actor)
	{
		if (actor?.stats == null)
		{
			return;
		}

		try
		{
			actor.stats["armor"] = 0f;
		}
		catch
		{
		}

		try
		{
			actor.stats["critical_chance"] = 0f;
		}
		catch
		{
		}
	}

	private static void ApplyMultiplierStat(Actor actor, string statId, float multiplier)
	{
		if (actor?.stats == null || string.IsNullOrWhiteSpace(statId) || multiplier <= 0f)
		{
			return;
		}

		float current = XjSafeCore.GetStatSafe(actor, statId, 0f);
		actor.stats[statId] = current * multiplier;
	}

	private static void ApplyHealthMultiplierStat(Actor actor, float multiplier)
	{
		if (actor?.stats == null || multiplier <= 0f)
		{
			return;
		}

		float currentBonus = XjSafeCore.GetStatSafe(actor, "multiplier_health", 0f);
		float currentFactor = Mathf.Max(0.01f, 1f + currentBonus);
		actor.stats["multiplier_health"] = currentFactor * multiplier - 1f;
	}

	private static void ApplyRatioStat(Actor actor, string statId, float bonus)
	{
		if (actor?.stats == null || string.IsNullOrWhiteSpace(statId) || bonus == 0f)
		{
			return;
		}

		float current = XjSafeCore.GetStatSafe(actor, statId, 0f);
		actor.stats[statId] = current + bonus;
	}

	private static void ApplyPercentPointStat(Actor actor, string statId, float bonus)
	{
		ApplyRatioStat(actor, statId, bonus * 100f);
	}

	private static void EnsureNativeDamageRange(Actor actor)
	{
		if (actor?.stats == null)
		{
			return;
		}

		// 旧版玄鉴装备会覆盖武器模板的 damage_range。该字段为 0 时，
		// 原生面板和近战结算都会将伤害下限算为 0；只修正异常值，
		// 不覆盖原生或其他模组已有的伤害区间。
		float damage = XjSafeCore.GetStatSafe(actor, "damage", 0f);
		float range = XjSafeCore.GetStatSafe(actor, "damage_range", 0f);
		if (damage > 0f && range <= 0f)
		{
			actor.stats["damage_range"] = 0.6f;
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "die", new Type[]
	{
		typeof(bool),
		typeof(AttackType),
		typeof(bool),
		typeof(bool)
	})]
	private static bool ActorDieArgs_Prefix(Actor __instance, AttackType __1, out XjDeathSnapshot __state)
	{
		__state = XjDeathSnapshot.Empty;
		if (XjVanillaDeathGuard.ShouldBlockDirectStarvationDeath(__instance, __1))
		{
			return false;
		}
		if (XjVanillaDeathGuard.ShouldBlockDirectFireDeath(__instance, __1))
		{
			return false;
		}
		// AttackType 无法可靠区分环境效果与脚本化战斗技能。
		// 死亡入口不得再据此拦截，否则会制造 1 HP/5% HP 假死与特殊技能杀不死。
		__state = XjDeathPatchBridge.CaptureBeforeDeath(__instance);
		XjEquipmentForgeConsumer.BeginDeathEquipmentRelease(__instance);
		return true;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), "die", new Type[]
	{
		typeof(bool),
		typeof(AttackType),
		typeof(bool),
		typeof(bool)
	})]
	private static void ActorDieArgs_Postfix(Actor __instance, XjDeathSnapshot __state)
	{
		XjEquipmentForgeConsumer.EndDeathEquipmentRelease(__instance);
		if (XjDeathPatchBridge.CommitAfterDeath(__instance, __state))
		{
			// [玄鉴] 仅在死亡真实完成后备案金丹道途克制关系。
			XjDaoTuCounterState.TryRegisterCounterOnDeath(__instance);
			if (XjTrueDamageSystem.IsJinXingYaoXie(__instance))
			{
				XjYinSiTraitLifecycle.OnJinXingYaoXieDied(__instance);
			}
			XjLongShuKillRewardSystem.TryGrant(__instance, __state);
		}

		if (!XjSafeCore.IsAliveActor(__instance))
		{
			int deathYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
			XjYinSiExposurePursuitSystem.OnActorDied(__instance, deathYear);
			XjSecretRealmRegistry.OnActorDied(((BaseSystemData)__instance.data).id, deathYear);
			XjJinDanImmortalityRegistry.MarkDead(__instance, deathYear);
			XjShenDanRegistry.OnActorDied(__instance, deathYear);
			XjScheduler.UnregisterDeadActor(__instance);
		}
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(Actor), "die", new Type[]
	{
		typeof(bool),
		typeof(AttackType),
		typeof(bool),
		typeof(bool)
	})]
	private static Exception ActorDieArgs_BoundEquipment_Finalizer(Actor __instance, Exception __exception)
	{
		XjEquipmentForgeConsumer.EndDeathEquipmentRelease(__instance);
		return __exception;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "checkNaturalDeath")]
	private static bool ActorCheckNaturalDeath_XuanJianLifespan_Prefix(Actor __instance, ref bool __result)
	{
		return XjVanillaDeathGuard.TryHandleNaturalDeath(__instance, ref __result);
	}
}
