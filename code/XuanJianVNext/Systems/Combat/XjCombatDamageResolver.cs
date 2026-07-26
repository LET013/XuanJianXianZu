using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Combat;

internal enum XjCombatResolutionKind
{
	Applied = 0,
	Dodged = 1,
	Blocked = 2,
}

internal readonly struct XjCombatResolution
{
	internal readonly XjCombatResolutionKind Kind;
	internal readonly float NormalDamage;
	internal readonly float TrueDamage;
	internal readonly bool Critical;

	internal XjCombatResolution(
		XjCombatResolutionKind kind,
		float normalDamage,
		float trueDamage,
		bool critical)
	{
		Kind = kind;
		NormalDamage = Mathf.Max(0f, normalDamage);
		TrueDamage = Mathf.Max(0f, trueDamage);
		Critical = critical;
	}

	internal bool ShouldRunOriginal => Kind == XjCombatResolutionKind.Applied;
}

/// <summary>
/// 玄鉴统一战斗结算入口。
///
/// 结算顺序：命中/闪避 → 道途与特殊易伤 → 境界压制 → 权柄 → 自定义暴击
/// → 普通/真伤拆分 → 减伤与减伤穿透 → 果位减伤 → 护盾与破盾。
///
/// 原生 getHit 仍负责实际扣血、死亡和击杀归因；调用方必须在玄鉴战斗中关闭
/// pCheckDamageReduction，防止 WorldBox 原生 armor 再次削减已经结算完成的伤害。
/// </summary>
internal static class XjCombatDamageResolver
{
	private const float CriticalExtraDamage = 0.50f;
	private const float MaxCustomDamageReductionPercent = 70f;
	private const float MaxShieldPercent = 60f;
	private const float MaxTrueDamagePercent = 50f;

	internal static XjCombatResolution ResolveActorAttack(
		ref float damage,
		Actor attacker,
		Actor defender,
		int attackerTier,
		int defenderTier,
		in XjCombatHotProfile attackerProfile,
		in XjCombatHotProfile defenderProfile)
	{
		if (damage <= 0f || attacker?.data == null || !XjSafeCore.IsAliveActor(defender))
		{
			damage = 0f;
			return new XjCombatResolution(XjCombatResolutionKind.Blocked, 0f, 0f, false);
		}

		float transientDodgeBonus = XjSanYinBaoYiXuanLunSystem.GetDodgeBonus(defender);
		float dodgeChance = Mathf.Clamp(
			defenderProfile.Dodge + transientDodgeBonus - attackerProfile.Accuracy,
			0f,
			100f);
		if (dodgeChance > 0f && Randy.randomChance(dodgeChance / 100f))
		{
			damage = 0f;
			return new XjCombatResolution(XjCombatResolutionKind.Dodged, 0f, 0f, false);
		}

		XjTrueDamageSystem.NotifyYinSiCombatContact(attacker, defender);
		bool authorizedYinSiPursuit = XjTrueDamageSystem.IsAuthorizedLivingJinDanPursuit(defender, attacker);
		float workingDamage = damage;
		if (defenderProfile.IsJinXingYaoXie && attackerTier < XjRealmSuppression.TierZiFu)
		{
			damage = 0f;
			return new XjCombatResolution(XjCombatResolutionKind.Blocked, 0f, 0f, false);
		}

		if (attackerTier >= XjRealmSuppression.TierZhuJi
			&& defenderTier >= XjRealmSuppression.TierZhuJi)
		{
			workingDamage *= XjDaoTuCounterState.GetMultiplier(attackerProfile.DaoTu, defenderProfile.DaoTu);
		}

		if (attackerProfile.IsJinXingYaoXie)
		{
			workingDamage *= 1.2f;
		}
		workingDamage *= XjTrueDamageSystem.GetJinXingYaoXieDamageMultiplier(defender, attacker);

		if (attackerTier > XjRealmSuppression.TierNone && attackerTier == defenderTier)
		{
			workingDamage *= 1f + Mathf.Clamp(attackerProfile.SameRealmDamage, 0f, 100f) / 100f;
		}

		// 境界压制只在此处应用一次：低一大境界及以上伤害归零；
		// 同一大境界按真实 CombatLevel 微调；高打低不再按目标最大生命强制处决。
		if (authorizedYinSiPursuit)
		{
			// 阴司追索依靠权柄克制穿透一部分境界压制，但仍按正常伤害链结算，不执行瞬杀。
			workingDamage *= 2.5f;
		}
		else
		{
			XjRealmSuppression.ApplySuppression(
				ref workingDamage,
				attacker,
				defender,
				attackerTier,
				defenderTier);
		}
		bool hasMajorRealmAdvantage = authorizedYinSiPursuit || (attackerTier > XjRealmSuppression.TierNone
			&& attackerTier > defenderTier);

		if (attackerTier >= XjRealmSuppression.TierJinDan
			&& defenderTier >= XjRealmSuppression.TierJinDan)
		{
			XjGuoWeiCombatSystem.ApplyQuanBingDamageBonus(
				ref workingDamage,
				attacker,
				defender,
				attackerTier,
				defenderTier);
		}

		// 领域只在本次战斗热路径中提供临时倍率，不写角色stats，也不建立
		// “领域强度/领域抗性”等新属性。多个领域同类效果只取最强值。
		XjDomainSkillRuntime.ApplyCombatModifiers(
			ref workingDamage,
			attacker,
			defender,
			attackerTier);

		bool critical = TryApplyCritical(ref workingDamage, attackerProfile, defenderProfile);
		if (workingDamage <= 0f)
		{
			damage = 0f;
			return new XjCombatResolution(XjCombatResolutionKind.Blocked, 0f, 0f, critical);
		}

		// 高一大境界以上时按真伤语义穿透减伤与护盾，但仍使用本次攻击的正常伤害值，
		// 不再制造“一碰即死”的 maxHealth 处决伤害。
		float trueRatio = authorizedYinSiPursuit
			? 0.75f
			: hasMajorRealmAdvantage
				? 1f
				: Mathf.Clamp(attackerProfile.TrueDamageRatio, 0f, MaxTrueDamagePercent) / 100f;
		float trueDamage = workingDamage * trueRatio;
		float normalDamage = Mathf.Max(0f, workingDamage - trueDamage);

		normalDamage = ApplyCustomDamageReduction(normalDamage, attackerProfile, defenderProfile);
		if (defenderTier >= XjRealmSuppression.TierJinDan)
		{
			normalDamage *= 1f - Mathf.Clamp01(XjGuoWeiCombatSystem.GetGuoWeiDamageReduction(defender));
		}
		normalDamage = ApplyShield(normalDamage, attackerProfile, defenderProfile, defender);

		float finalDamage = normalDamage + trueDamage;
		damage = finalDamage > 0f ? Mathf.Max(1f, finalDamage) : 0f;
		return damage > 0f
			? new XjCombatResolution(XjCombatResolutionKind.Applied, normalDamage, trueDamage, critical)
			: new XjCombatResolution(XjCombatResolutionKind.Blocked, 0f, 0f, critical);
	}

	internal static XjCombatResolution ResolveEnvironmentDamage(
		ref float damage,
		Actor defender,
		int defenderTier,
		in XjCombatHotProfile defenderProfile)
	{
		if (damage <= 0f || !XjSafeCore.IsAliveActor(defender))
		{
			damage = 0f;
			return new XjCombatResolution(XjCombatResolutionKind.Blocked, 0f, 0f, false);
		}

		float normalDamage = damage;
		float damageReduction = Mathf.Clamp(
			defenderProfile.DamageReduce,
			0f,
			MaxCustomDamageReductionPercent) / 100f;
		normalDamage *= 1f - damageReduction;

		if (defenderTier >= XjRealmSuppression.TierJinDan)
		{
			normalDamage *= 1f - Mathf.Clamp01(XjGuoWeiCombatSystem.GetGuoWeiDamageReduction(defender));
		}

		normalDamage = ApplyShield(normalDamage, default, defenderProfile, defender);
		damage = normalDamage > 0f ? Mathf.Max(1f, normalDamage) : 0f;
		return damage > 0f
			? new XjCombatResolution(XjCombatResolutionKind.Applied, damage, 0f, false)
			: new XjCombatResolution(XjCombatResolutionKind.Blocked, 0f, 0f, false);
	}

	private static bool TryApplyCritical(
		ref float damage,
		in XjCombatHotProfile attackerProfile,
		in XjCombatHotProfile defenderProfile)
	{
		float chance = Mathf.Clamp(attackerProfile.CritChance, 0f, 100f);
		if (chance <= 0f || !Randy.randomChance(chance / 100f))
		{
			return false;
		}

		float antiCritical = Mathf.Clamp(defenderProfile.CritTakenReduction, 0f, 100f) / 100f;
		float extraDamage = CriticalExtraDamage * (1f - antiCritical);
		damage *= 1f + Mathf.Max(0f, extraDamage);
		return true;
	}

	private static float ApplyCustomDamageReduction(
		float normalDamage,
		in XjCombatHotProfile attackerProfile,
		in XjCombatHotProfile defenderProfile)
	{
		if (normalDamage <= 0f)
		{
			return 0f;
		}

		float effectiveReduction = Mathf.Max(
			0f,
			defenderProfile.DamageReduce - Mathf.Max(0f, attackerProfile.ArmorPenPercent));
		effectiveReduction = Mathf.Clamp(effectiveReduction, 0f, MaxCustomDamageReductionPercent);
		return normalDamage * (1f - effectiveReduction / 100f);
	}

	private static float ApplyShield(
		float normalDamage,
		in XjCombatHotProfile attackerProfile,
		in XjCombatHotProfile defenderProfile,
		Actor defender)
	{
		if (normalDamage <= 0f)
		{
			return 0f;
		}

		float shieldPercent = Mathf.Max(0f, defenderProfile.ShieldRatio);
		shieldPercent += Mathf.Max(0f, XjGuoWeiCombatSystem.GetGuoWeiShieldRatio(defender) * 100f);
		shieldPercent -= Mathf.Max(0f, attackerProfile.ShieldBreak);
		shieldPercent = Mathf.Clamp(shieldPercent, 0f, MaxShieldPercent);
		return normalDamage * (1f - shieldPercent / 100f);
	}
}
