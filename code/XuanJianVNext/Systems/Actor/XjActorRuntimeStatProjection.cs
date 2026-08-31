using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.ActorSystem;

internal static class XjActorRuntimeStatProjection
{
	internal static void Apply(Actor __instance)
	{
		if (__instance?.data == null || __instance.stats == null)
		{
			return;
		}

		try
		{
		long actorId = ((BaseSystemData)__instance.data).id;
		bool hasRuntimeStatInterest = XjRuntimeActorInterestIndex.HasStatInterest(actorId);
		bool bootstrapCandidate = XjWorldBootstrapLane.HasPending
			&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(__instance);

		if (hasRuntimeStatInterest && XjXingDuQianPhantomSystem.IsPhantom(__instance))
		{
			XjXingDuQianPhantomSystem.ApplyRuntimeStats(__instance);
			return;
		}

		// Ordinary civilizations take an indexed O(1) lifespan path. Only actors with
		// runtime interest (or the bounded post-load bootstrap window) parse realm/Shi
		// markers and managed lifespan bonuses.
		XjRealmLifespanService.ApplyRuntimeRealmLifespanFast(
			__instance, hasRuntimeStatInterest, bootstrapCandidate);
		if (!hasRuntimeStatInterest && !bootstrapCandidate)
		{
			// 其余玄鉴运行期属性仍只服务已索引角色，普通角色在寿元收口后立即返回。
			return;
		}

		// 百官持玄不是只改一个“境界标签”：真实属性重建时，以所借境界标准底盘的
		// 80% 建立全套属性下限，再继续结算人物自己的功法、法宝、时代等乘区。
		// 持玄官在授命/读档索引阶段已加入 StatInterest；把这一查询放在索引门后，
		// 避免每一名普通原生角色重建属性时都进入仙国档案解析。
		XjXianGuoSystem.ApplyInstitutionalBorrowedRealmStatFloor(__instance);

		bool isShenDan = IsShenDan(__instance);
		bool isTaiYang = IsTaiYangDaoTu(__instance);
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
		// XjRealmCombatBonuses 是紫府金丹道的额外境界规则。服气三境已有独立
		// 境界特质与剑道属性，只参与战斗位格比较，不继承紫金境界奖励。
		XjFaBaoBonusProfile realmProfile = default;
		bool hasRealmCombatBonus = XjCultivationPathRules.IsZiFuJinDan(__instance)
			&& XjRealmCombatBonuses.TryGetProfile(realmTier, out realmProfile);
		// 闇天殃/坤辰修的“法器失效、缴械”只在短时状态内切断法宝、器艺与剑道
		// 的运行时属性投影；装备实体与背包不被移除，状态结束后由延迟回调刷新恢复。
		bool artifactSuppressed = XjCanonicalShenTongMechanicsSystem.IsArtifactSuppressed(__instance);
		// && 短路时 out-var 不保证赋值；先显式 default，避免 CS0165，且不改变抑制语义。
		XjFaBaoBonusProfile faBaoProfile = default;
		XjFaBaoBonusProfile weaponArtProfile = default;
		XjFaBaoBonusProfile swordDaoProfile = default;
		bool hasFaBao = !artifactSuppressed && XjFaBaoBonusService.TryGetProfile(__instance, out faBaoProfile);
		bool hasWeaponArt = !artifactSuppressed && XjWeaponArtSystem.TryGetBonusProfile(__instance, out weaponArtProfile);
		bool hasSwordDaoCombat = !artifactSuppressed && XjSwordDaoCombatSystem.TryGetBonusProfile(__instance, out swordDaoProfile);
		if (!isCultivator && !isTaiYang && !isYaoXie && !isLongShu && !hasFaBao && !hasWeaponArt && !hasSwordDaoCombat && !hasRealmCombatBonus && !hasEraBonus && !hasGongFaAttributeBonus && !isShenDan)
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
			// 龙属原生 armor 在统一战斗链中不会再由 WorldBox 二次结算；
			// 这里把龙鳞与同境战斗底盘物化到玄鉴自定义战斗属性。
			XjLongShuSystem.ApplyInnateCombatBody(__instance);
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
		if (hasSwordDaoCombat)
		{
			ApplyCombatBonusProfile(__instance, swordDaoProfile);
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
		ApplyDaoTaiShenDanLifespanBonus(__instance);
		ApplyHouShenShuLifespanBonus(__instance);
		if (isLongShu)
		{
			XjLongShuSystem.ClampRuntimeLifespan(__instance);
		}
		if (isShenDan)
		{
			ApplyShenDanStatScalar(__instance);
		}
		XjRealmStageStatMultiplierService.TryResolveMultiplier(
			__instance,
			out int stageStatFingerprint,
			out float stageStatMultiplier);
		ApplyRealmStageStatMultiplier(__instance, stageStatMultiplier);
		XjRealmStageStatMultiplierService.MarkApplied(__instance, stageStatFingerprint);
		// 性命反震最后作用于完整战斗属性，避免时代、功法等后置加成绕过伤势。
		XjFuQiInjurySystem.ApplyRuntimePenalty(__instance);
		// 太阳道途是最终独立战斗乘区：不改寿元、道慧、命数与模型尺寸，
		// 只把完整战斗面板提升到3倍；“必定命中”由统一伤害结算器单独保证。
		if (isTaiYang)
		{
			ApplyTaiYangCombatStatScalar(__instance);
		}
		// 抗击退最后从完整 Resist / 境界结果投影到玄鉴受力缓存；该步骤只发生在
		// 已有 updateStats 重建，不进入逐次受击热路径，也保留仙国/龙属/太阳等旧加成语义。
		XjKnockbackGuard.ProjectFinalResistToForceGuard(__instance);
		// 乞代夜收束光火后的青蓝遁势是短时最终移动乘区，不写永久 speed。
		XjCanonicalShenTongMechanicsSystem.ApplyRuntimeMovementMultiplier(__instance);
		if (isCultivator || hasFaBao || hasWeaponArt || hasSwordDaoCombat || hasRealmCombatBonus || isYaoXie || isLongShu || isShenDan)
		{
			EnsureNativeDamageRange(__instance);
		}
		if (isCultivator || hasFaBao || hasWeaponArt || hasSwordDaoCombat || hasRealmCombatBonus || isYaoXie || hasEraBonus || hasGongFaAttributeBonus || isShenDan)
		{
			SuppressVanillaDefenseAndCritical(__instance);
		}
		}
		catch (Exception ex)
		{
			// This method is injected into the native Actor.updateStats rebuild path.
			// A malformed mod record must never escape into WorldBox's parallel stat batch.
			XjExceptionDiagnostics.Report("Interop/Actor/updateStats-runtime-projection", ex);
		}
	}

	internal static void ReconcileHealth(Actor actor, long actorId)
	{
		if (!XjSafeCore.IsAliveActor(actor) || actor.stats == null || actorId <= 0L)
		{
			return;
		}

		bool hasRuntimeStatInterest = XjRuntimeActorInterestIndex.HasStatInterest(actorId);
		bool bootstrapCandidate = XjWorldBootstrapLane.HasPending
			&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(actor);
		if (!hasRuntimeStatInterest && !bootstrapCandidate)
		{
			return;
		}

		int realmTier;
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out realmTier))
		{
			realmTier = XjRealmSuppression.GetRealmTier(actor);
		}
		if (realmTier <= XjRealmSuppression.TierNone
			&& !IsShenDan(actor)
			&& !XjLongShuSystem.IsLongShu(actor))
		{
			return;
		}

		try
		{
			float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, 0f);
			float currentHealth = XjSafeCore.GetHealthSafe(actor, 0f);
			if (maxHealth <= 1f || currentHealth <= 0f)
			{
				return;
			}

			bool hadPrevious = XjActorAccessor.TryGetFloat(
				actor,
				XjActorDataKeys.XjRuntimeLastMaxHealth,
				out float previousMaxHealth);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjRuntimeLastMaxHealth, maxHealth);

			if (bootstrapCandidate)
			{
				// 读档恢复阶段 WorldBox 原生模拟仍可能继续。玄鉴属性重建会先后抬高
				// 最大生命；若仍按“新增上限慢慢补”处理，角色会在半恢复血量下直接
				// 进入战斗。Bootstrap 内每次 stats 达到新上限都直接补满，阶段结束后
				// 立即恢复普通受伤语义，不把读档保护带入正常运行。
				int fullHealth = maxHealth >= int.MaxValue
					? int.MaxValue
					: Math.Max(1, Mathf.CeilToInt(maxHealth));
				if (currentHealth < fullHealth - 1f)
				{
					try { actor.setHealth(fullHealth); }
					catch { actor.data.health = fullHealth; }
				}
				return;
			}

			float healAmount = 0f;
			if (!hadPrevious || previousMaxHealth <= 1f)
			{
				// 旧档或刚成为修士的角色没有基准值；第一次看到时按当前玄鉴上限校正。
				healAmount = maxHealth - currentHealth;
			}
			else if (maxHealth > previousMaxHealth + 1f)
			{
				// 最大生命因境界、功法、法宝等玄鉴加成提高时，只补新增上限，不抹平既有伤势。
				healAmount = maxHealth - previousMaxHealth;
			}

			if (healAmount > 1f && currentHealth < maxHealth - 1f)
			{
				int restoreAmount = healAmount >= int.MaxValue
					? int.MaxValue
					: Math.Max(1, Mathf.CeilToInt(healAmount));
				actor.restoreHealth(restoreAmount);
			}
		}
		catch (System.Exception xjCaught292)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjActorRuntimeStatProjection:292", xjCaught292);
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

	private static void ApplyDaoTaiShenDanLifespanBonus(Actor actor)
	{
		if (actor?.data == null || actor.stats == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjDaoTaiShenDanLifespanBonus, out int bonus)
			|| bonus <= 0) return;
		float current = XjSafeCore.GetStatSafe(actor, "lifespan", 0f);
		actor.stats["lifespan"] = Math.Max(0f, current) + bonus;
	}

	private static void ApplyHouShenShuLifespanBonus(Actor actor)
	{
		if (actor?.data == null || actor.stats == null || !XjHouShenShuSystem.IsShenShi(actor)
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHouShenShuLifespanBonus, out int bonus)
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
		catch (System.Exception xjCaught318) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjActorRuntimeStatProjection:318", xjCaught318); }
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
		catch (System.Exception xjCaught334) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjActorRuntimeStatProjection:334", xjCaught334); }
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

		float bonus = XjFaBaoCatalog.IsXianQi(state.ClassName)
			? 49000f
			: XjFaBaoCatalog.IsJinDanFaBao(state.ClassName)
			? 9000f
			: XjFaBaoCatalog.IsZiFuLingBao(state.ClassName)
				? 3500f
				: XjFaBaoCatalog.IsZhuJiFaQi(state.ClassName) ? 0f : 0f;
		if (bonus <= 0f) return;

		// Base equipment damage is 1000. This produces a stable native curve:
		// 法器 1000, 灵宝 4500, 法宝 10000, 仙器 50000, before existing realm multipliers.
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

	internal static bool IsShenDan(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	private static void ApplyRealmStageStatMultiplier(Actor actor, float multiplier)
	{
		if (actor?.stats == null || multiplier <= 1f) return;

		// 小境界作为最终属性乘区，覆盖境界提供的基础数值与玄鉴自定义战斗属性。
		// 仅寿元独立结算，不进入本乘区。
		ApplyMultiplierStat(actor, "damage", multiplier);
		ApplyHealthMultiplierStat(actor, multiplier);
		ApplyMultiplierStat(actor, "speed", multiplier);
		ApplyMultiplierStat(actor, "attack_speed", multiplier);
		ApplyMultiplierStat(actor, "Resist", multiplier);
		ApplyMultiplierStat(actor, "warfare", multiplier);
		ApplyMultiplierStat(actor, "mass", multiplier);
		ApplyMultiplierStat(actor, "area_of_effect", multiplier);
		ApplyMultiplierStat(actor, "targets", multiplier);
		ApplyMultiplierStat(actor, "accuracy", multiplier);
		ApplyMultiplierStat(actor, "stamina", multiplier);
		ApplyMultiplierStat(actor, "range", multiplier);
		ApplyMultiplierStat(actor, "scale", multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.ArmorPenPercent, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.Vampire, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.Dodge, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.Healback, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.Accuracy, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.CritChance, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.CritTakenReduction, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.ShieldRatio, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.ShieldBreak, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.SameRealmDamage, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.TrueDamageRatio, multiplier);
		ApplyMultiplierStat(actor, XjSafeCore.DamageReduce, multiplier);
	}

	private static bool IsTaiYangDaoTu(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& string.Equals((daoTu ?? string.Empty).Trim(), "太阳", StringComparison.Ordinal);
	}

	private static void ApplyTaiYangCombatStatScalar(Actor actor)
	{
		const float TaiYangScalar = 3f;
		ApplyMultiplierStat(actor, "damage", TaiYangScalar);
		ApplyHealthMultiplierStat(actor, TaiYangScalar);
		ApplyMultiplierStat(actor, "speed", TaiYangScalar);
		ApplyMultiplierStat(actor, "attack_speed", TaiYangScalar);
		ApplyMultiplierStat(actor, "Resist", TaiYangScalar);
		ApplyMultiplierStat(actor, "warfare", TaiYangScalar);
		ApplyMultiplierStat(actor, "mass", TaiYangScalar);
		ApplyMultiplierStat(actor, "area_of_effect", TaiYangScalar);
		ApplyMultiplierStat(actor, "targets", TaiYangScalar);
		ApplyMultiplierStat(actor, "accuracy", TaiYangScalar);
		ApplyMultiplierStat(actor, "stamina", TaiYangScalar);
		ApplyMultiplierStat(actor, "range", TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.ArmorPenPercent, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.Vampire, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.Dodge, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.Healback, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.Accuracy, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.CritChance, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.CritTakenReduction, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.ShieldRatio, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.ShieldBreak, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.SameRealmDamage, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.TrueDamageRatio, TaiYangScalar);
		ApplyMultiplierStat(actor, XjSafeCore.DamageReduce, TaiYangScalar);
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
		catch (System.Exception xjCaught477) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjActorRuntimeStatProjection:477", xjCaught477); }

		try
		{
			actor.stats["critical_chance"] = 0f;
		}
		catch (System.Exception xjCaught485) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjActorRuntimeStatProjection:485", xjCaught485); }
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

}
