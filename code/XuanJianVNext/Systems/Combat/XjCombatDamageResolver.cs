using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;

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

		bool daoTaiDominatesLowerRealm = attackerTier >= XjRealmSuppression.TierDaoTai
			&& defenderTier < attackerTier;
		bool taiYangAlwaysHits = string.Equals(attackerProfile.DaoTu, "太阳", System.StringComparison.Ordinal);
		float transientDodgeBonus = XjSanYinBaoYiXuanLunSystem.GetDodgeBonus(defender);
		float taiYinDynamicDodgeFloor = XjSanYinBaoYiXuanLunSystem.GetDynamicDodgeChanceFloor(defender);
		float yuanZhaoDynamicDodgeFloor = XjDomainSkillRuntime.GetYuanZhaoDodgeChanceFloor(defender);
		float dodgeChance = daoTaiDominatesLowerRealm || taiYangAlwaysHits
			? 0f
			: Mathf.Max(
				Mathf.Max(taiYinDynamicDodgeFloor, yuanZhaoDynamicDodgeFloor),
				Mathf.Clamp(
					defenderProfile.Dodge + transientDodgeBonus - attackerProfile.Accuracy,
					0f,
					100f));
		if (dodgeChance > 0f && Randy.randomChance(dodgeChance / 100f))
		{
			damage = 0f;
			return new XjCombatResolution(XjCombatResolutionKind.Dodged, 0f, 0f, false);
		}

		// 被动护身类神通必须允许“尚未来得及反击”的受击者第一次受击即生效。
		// 仅紫府同级及以上进入该检查，避免普通角色战斗热路径反复读取神通数组。
		if (defenderTier >= XjRealmSuppression.TierZiFu)
		{
			XjCanonicalShenTongMechanicsSystem.RefreshPassiveCombatStates(defender);
		}

		// 〖致缉熙〗原著语义是敌攻与钟鼓列臣之象纠缠后被偏移到他处。
		// WorldBox 没有可靠的“重定向既有攻击轨迹”入口，因此由原著神通状态层
		// 低频完整偏移本次攻击；15秒反制间隔只防止常驻无敌，不占主动技能CD。
		if (XjCanonicalShenTongMechanicsSystem.TryDeflectIncoming(defender))
		{
			damage = 0f;
			return new XjCombatResolution(XjCombatResolutionKind.Blocked, 0f, 0f, false);
		}

		XjTrueDamageSystem.NotifyYinSiCombatContact(attacker, defender);
		bool authorizedYinSiPursuit = XjTrueDamageSystem.IsAuthorizedLivingJinDanPursuit(defender, attacker);
		float workingDamage = damage;
		// 仙国假金丹的真实身体仍是紫府/真人。借众之玄只在统一战斗解析器中
		// 提升攻防，不改 actor.stats 和真实 RealmId，失国后下一击立即失效。
		workingDamage *= attackerProfile.BorrowedOutgoingDamageMultiplier > 0f
			? attackerProfile.BorrowedOutgoingDamageMultiplier : 1f;
		workingDamage *= defenderProfile.BorrowedIncomingDamageMultiplier > 0f
			? defenderProfile.BorrowedIncomingDamageMultiplier : 1f;
		workingDamage *= XjJiShengYuSystem.GetOrActivateDamageMultiplier(attacker, attackerTier);
		// 松上雪、乞代夜、伏青山、抱石眠等短时/被动原著神通统一在这里
		// 作用于真实攻击结算，不修改永久 stats，也不产生额外一次伤害。
		workingDamage *= XjCanonicalShenTongMechanicsSystem.ResolveActorAttackMultiplier(
			attacker, defender, attackerProfile.DaoTu);
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
		// 同一大境界按真实 CombatLevel 微调。道胎对低境另设最小有效伤害，
		// 只解决高位角色长期无法终结低境目标的问题，仍保留原生受击与死亡链。
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
				defenderTier,
				attackerProfile.HighRealmGrade,
				defenderProfile.HighRealmGrade);
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

		// 已经“易”入本道果位的外道根权柄会随果位承继，并强化当前真实持果者的位格。
		// 这里在真/常伤拆分前结算，因此同样能抵御真伤；失去果位或融权被再夺后该层立即消失。
		if (defenderTier >= XjRealmSuppression.TierJinDan)
		{
			workingDamage *= 1f - Mathf.Clamp01(XjGuoWeiCombatSystem.GetSeizedAuthorityDamageReduction(defender));
		}

		bool critical = TryApplyCritical(ref workingDamage, attackerProfile, defenderProfile);
		// 龙属的先天龙躯只压低同一大境界的最终入伤，并在真/常伤拆分前结算。
		// 这样既能让面板上的高生命真正转化为同阶耐久，也不会削弱高一境的绝对压制。
		workingDamage *= XjLongShuSystem.ResolveInnateSameTierIncomingMultiplier(
			defender, attackerTier, defenderTier);
		workingDamage = ApplyDaoTaiDominanceFloor(
			workingDamage, attackerTier, defenderTier, defender);
		if (workingDamage <= 0f)
		{
			damage = 0f;
			return new XjCombatResolution(XjCombatResolutionKind.Blocked, 0f, 0f, critical);
		}

		// 高一大境界以上时按真伤语义穿透减伤与护盾。普通跨境仍使用本次攻击伤害；
		// 道胎压制低境时，前一步只设可控的生命比例下限，不直接调用kill或绕过死亡事件。
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
		if (damage > 0f)
		{
			// 全丹秘白汞：原著明确“伤势恢复快”。在原生 getHit 真正扣血后
			// 低频回返本次伤势的一小部分，不改最大生命、不创建常驻再生。
			XjCanonicalShenTongMechanicsSystem.TryScheduleMiBaiGongRecovery(defender, damage);
		}
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
		if (defenderTier >= XjRealmSuppression.TierJinDan)
		{
			normalDamage *= 1f - Mathf.Clamp01(XjGuoWeiCombatSystem.GetSeizedAuthorityDamageReduction(defender));
		}

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

	internal static void EnforceDaoTaiFinalDamageFloor(
		ref float damage,
		int attackerTier,
		int defenderTier,
		Actor defender)
	{
		damage = ApplyDaoTaiDominanceFloor(damage, attackerTier, defenderTier, defender);
	}

	private static float ApplyDaoTaiDominanceFloor(
		float damage,
		int attackerTier,
		int defenderTier,
		Actor defender)
	{
		if (damage <= 0f
			|| attackerTier < XjRealmSuppression.TierDaoTai
			|| defenderTier >= attackerTier
			|| defender?.data == null)
		{
			return damage;
		}

		// 必须读原生实际最大生命。旧实现读取 actor.stats["health"]，在境界倍率、
		// 装备 multiplier_health 与临时加成存在时会低估数倍，导致所谓“110%生命”
		// 实际只打掉真实血条的一小截。
		float maximumHealth = Mathf.Max(1f, XjSafeCore.GetMaxHealthSafe(defender, 1f));
		float currentHealth = Mathf.Max(1f, XjSafeCore.GetHealthSafe(defender, maximumHealth));

		float floor;
		if (defenderTier >= XjRealmSuppression.TierJinDan)
		{
			// 对金丹/真君保持“两次有效命中内终结”的边界。若第一击后已经低于
			// 60%血线，则第二击直接越过剩余生命，避免丹药/护符把角色卡死在残血循环。
			floor = maximumHealth * 0.60f;
			if (currentHealth <= floor) floor = currentHealth + Mathf.Max(1f, maximumHealth * 0.005f);
		}
		else
		{
			// 紫府及以下面对道胎，一次真正命中的攻击必须具备终结性。只提高本次
			// 原生伤害，不直接调用 die/kill，死亡归因、收藏与史册仍走统一死亡链。
			floor = currentHealth + Mathf.Max(1f, maximumHealth * 0.01f);
		}
		return Mathf.Max(damage, floor);
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
