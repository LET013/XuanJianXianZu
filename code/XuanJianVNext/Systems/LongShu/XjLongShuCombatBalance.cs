using System;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.LongShu;

/// <summary>
/// 龙属先天龙躯的统一战斗投影。
///
/// WorldBox 原生 armor 在玄鉴统一 getHit 结算中会被 pCheckDamageReduction=false 绕开；
/// 旧实现仍把较高 armor 留在角色面板，因此形成“看起来很硬，实战却不吃护甲”的错觉。
/// 这里把龙鳞 armor 有界折算进玄鉴 DamageReduce，并给龙属保留高于普通修士的同境肉身底盘；
/// 但不再叠加过高的减伤、恢复与同境伤害，以免先天种族优势压过真实境界、神通与器物差距。
/// 全部数值只在 updateStats 时物化，getHit 热路径仅保留一个同境倍率判断。
/// </summary>
internal static partial class XjLongShuSystem
{
	internal const float ZiFuSameTierIncomingMultiplier = 0.80f;
	internal const float JinDanSameTierIncomingMultiplier = 0.85f;
	internal const float DaoTaiSameTierIncomingMultiplier = 0.88f;

	internal static void ApplyInnateCombatBody(Actor actor)
	{
		if (!IsLongShu(actor) || actor?.stats == null) return;

		int tier = XjRealmSuppression.GetRealmTier(actor);
		ResolveBodyProfile(tier,
			out float armorConversionCap,
			out float critTakenReduction,
			out float healback,
			out float shieldRatio,
			out float armorPenetration,
			out float sameRealmDamage);

		// 统一战斗链关闭了原生 armor 二次减伤；龙属把现有龙鳞护甲按百分比点折算
		// 到玄鉴减伤中。只读取已经存在的面板属性，不额外制造一套“龙鳞强度”。
		float nativeArmor = Math.Max(0f, XjSafeCore.GetStatSafe(actor, "armor", 0f));
		float convertedArmor = Math.Min(armorConversionCap, nativeArmor);
		AddBodyPercentPointStat(actor, XjSafeCore.DamageReduce, convertedArmor);

		// 龙躯补足的是稀有生灵本身的战斗底盘，不增加最大生命（旧逻辑已经很高），
		// 重点收束暴击爆杀、真伤穿透后的脆弱感与缺少宗门/家族战斗链的问题。
		AddBodyPercentPointStat(actor, XjSafeCore.CritTakenReduction, critTakenReduction);
		AddBodyPercentPointStat(actor, XjSafeCore.Healback, healback);
		AddBodyPercentPointStat(actor, XjSafeCore.ShieldRatio, shieldRatio);
		AddBodyPercentPointStat(actor, XjSafeCore.ArmorPenPercent, armorPenetration);
		AddBodyPercentPointStat(actor, XjSafeCore.SameRealmDamage, sameRealmDamage);
	}

	/// <summary>
	/// 龙属只对“同一大境界”的外来伤害获得龙躯总伤减免。低境本来就受境界绝对压制；
	/// 高一大境界及以上仍按玄鉴高境绝对压力结算，不允许龙躯跨境抗衡。
	/// 该倍率作用于普通伤害与真伤拆分之前，所以能够解决高面板生命仍被同阶真伤快速融化的问题。
	/// </summary>
	internal static float ResolveInnateSameTierIncomingMultiplier(Actor defender, int attackerTier, int defenderTier)
	{
		if (!IsLongShu(defender)
			|| attackerTier <= XjRealmSuppression.TierNone
			|| attackerTier != defenderTier)
		{
			return 1f;
		}

		return defenderTier switch
		{
			XjRealmSuppression.TierDaoTai => DaoTaiSameTierIncomingMultiplier,
			XjRealmSuppression.TierJinDan => JinDanSameTierIncomingMultiplier,
			_ => ZiFuSameTierIncomingMultiplier
		};
	}

	private static void ResolveBodyProfile(
		int tier,
		out float armorConversionCap,
		out float critTakenReduction,
		out float healback,
		out float shieldRatio,
		out float armorPenetration,
		out float sameRealmDamage)
	{
		if (tier >= XjRealmSuppression.TierDaoTai)
		{
			armorConversionCap = 20f;
			critTakenReduction = 18f;
			healback = 0.60f;
			shieldRatio = 8f;
			armorPenetration = 10f;
			sameRealmDamage = 11f;
			return;
		}

		if (tier >= XjRealmSuppression.TierJinDan)
		{
			armorConversionCap = 17f;
			critTakenReduction = 16f;
			healback = 0.45f;
			shieldRatio = 7f;
			armorPenetration = 8f;
			sameRealmDamage = 9f;
			return;
		}

		armorConversionCap = 14f;
		critTakenReduction = 14f;
		healback = 0.35f;
		shieldRatio = 5f;
		armorPenetration = 6f;
		sameRealmDamage = 7f;
	}

	private static void AddBodyPercentPointStat(Actor actor, string statId, float amount)
	{
		if (actor?.stats == null || string.IsNullOrWhiteSpace(statId) || amount <= 0f) return;
		float current = XjSafeCore.GetStatSafe(actor, statId, 0f);
		actor.stats[statId] = Math.Max(0f, current + amount);
	}
}
