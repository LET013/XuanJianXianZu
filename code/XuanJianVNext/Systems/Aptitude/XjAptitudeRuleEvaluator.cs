using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.Systems.Aptitude;

internal readonly struct XjAptitudeRollResult
{
	internal readonly bool Passed;
	internal readonly int XjZz;
	internal readonly int OverlayMask;
	internal readonly string ReasonCode;

	internal XjAptitudeRollResult(bool passed, int xjZz, int overlayMask, string reasonCode)
	{
		Passed = passed;
		XjZz = xjZz;
		OverlayMask = overlayMask;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjAptitudeRuleEvaluator
{
	private const float FateMingShuMin = 8f;
	private const float FateMingShuPeak = 60f;
	private const float FateMinChance = 0.35f;
	private const float FateMaxChance = 0.60f;
	private const float FateFinalMaxChance = 0.96f;
	private const float AptitudeMingShuMin = 6f;
	private const float AptitudeMingShuPeak = 60f;
	private const float AptitudeMinChance = 0.25f;
	private const float AptitudeFinalMaxChance = 0.94f;
	private const float BloodlineFateChanceScale = 0.40f;
	private const float BloodlineAptitudeChanceScale = 0.33333334f;

	private static readonly XjAptitudeWeight[] AptitudeWeights =
	{
		new XjAptitudeWeight(1, 31f),
		new XjAptitudeWeight(2, 60f),
		new XjAptitudeWeight(3, 5.58f),
		new XjAptitudeWeight(4, 2.33f),
		new XjAptitudeWeight(5, 1.076f),
		new XjAptitudeWeight(6, 0.014f)
	};

	/// <summary>
	/// 原生高倍速下年龄回调可能从四岁直接跨到六岁。六岁仅作为五岁
	/// 出生判定的一次容错，不是对成年角色补发修炼资质。
	/// </summary>
	internal static bool IsAgeFiveEligibilityWindow(int ageYear)
	{
		return ageYear == 5 || ageYear == 6;
	}

	internal static XjAptitudeRollResult EvaluateAtAgeFive(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjAptitudeRollResult(false, 0, 0, "NotEligible");
		}

		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		int ageYear = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		if (!IsAgeFiveEligibilityWindow(ageYear))
		{
			return new XjAptitudeRollResult(false, 0, 0, ageYear < 5 ? "AgeBelowFive" : "AgeFiveWindowMissed");
		}

		// 容错帧不得改变同一名角色的天资结果，始终以五岁作为随机种子。
		const int aptitudeRollYear = 5;

		if (actor.hasTrait("ChuShen8"))
		{
			int daoZhuOverlayMask = RollXianTianDaoTi(actorId, aptitudeRollYear, 6) ? XjAptitudeTraitLifecycle.XjZz7Bit : 0;
			return new XjAptitudeRollResult(true, 6, daoZhuOverlayMask, "DaoZhuGuaranteed");
		}

		if (actor.hasTrait("XjYiDuiYing"))
		{
			int yiDuiYingXjZz = ResolveYiDuiYingAptitude(actor);
			int yiDuiYingOverlayMask = RollXianTianDaoTi(actorId, aptitudeRollYear, yiDuiYingXjZz) ? XjAptitudeTraitLifecycle.XjZz7Bit : 0;
			return new XjAptitudeRollResult(true, yiDuiYingXjZz, yiDuiYingOverlayMask, "YiDuiYingGuaranteed");
		}

		float mingShu = ReadMingShu(actor);
		float huiGuang = ReadHuiGuang(actor);
		int chuShenRank = ReadChuShenRank(actor);
		float score = mingShu + huiGuang * 0.25f + chuShenRank * 1.5f;
		float bloodlineInheritanceBias = XjBloodlineBirthRules.GetAptitudeInheritanceChanceBonus(actor);
		XjBloodlineBirthRules.GetDirectParentRealmAptitudeChanceFloors(
			actor,
			out float directParentFateFloor,
			out float directParentAptitudeFloor);
		float fateChance = Mathf.Clamp(
			Mathf.Max(
				directParentFateFloor,
				CalculateLinearChance(score, FateMingShuMin, FateMingShuPeak, FateMinChance, FateMaxChance, FateFinalMaxChance)
					+ bloodlineInheritanceBias * BloodlineFateChanceScale),
			0f,
			FateFinalMaxChance);
		if (!RollChance(fateChance, actorId, aptitudeRollYear, "fate_check"))
		{
			return new XjAptitudeRollResult(false, 0, 0, "NotEligible");
		}

		float aptitudeMaxChance = Mathf.Clamp(XjRuntimeSettings.AptitudeGrantChanceCap, AptitudeMinChance, 0.40f);
		float aptitudeChance = Mathf.Clamp(
			Mathf.Max(
				directParentAptitudeFloor,
				CalculateLinearChance(score, AptitudeMingShuMin, AptitudeMingShuPeak, AptitudeMinChance, aptitudeMaxChance, AptitudeFinalMaxChance)
					+ bloodlineInheritanceBias * BloodlineAptitudeChanceScale),
			0f,
			AptitudeFinalMaxChance);
		if (!RollChance(aptitudeChance, actorId, aptitudeRollYear, "aptitude_check"))
		{
			return new XjAptitudeRollResult(false, 0, 0, "NotEligible");
		}

		int xjZz = RollXjZz(actor, actorId, aptitudeRollYear);
		int overlayMask = RollXianTianDaoTi(actorId, aptitudeRollYear, xjZz) ? XjAptitudeTraitLifecycle.XjZz7Bit : 0;
		return new XjAptitudeRollResult(true, xjZz, overlayMask, "Ok");
	}

	internal static int ResolveYiDuiYingAptitude(Actor actor)
	{
		if (actor?.data == null)
		{
			return 4;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return Math.Max(4, RollXjZz(actor, actorId, 5));
	}

	private static float ReadMingShu(Actor actor)
	{
		return XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float value) ? Math.Max(0f, value) : 0f;
	}

	private static float ReadHuiGuang(Actor actor)
	{
		return XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float value) ? Math.Max(0f, value) : 0f;
	}

	private static int ReadChuShenRank(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ChuShenSpecial, out int specialRank) && specialRank >= 6 && specialRank <= 8)
		{
			return specialRank;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ChuShen, out int storedRank) && storedRank >= 1 && storedRank <= 5)
		{
			return storedRank;
		}

		for (int i = 8; i >= 1; i--)
		{
			if (actor.hasTrait("ChuShen" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))
			{
				return i;
			}
		}

		return 0;
	}

	private static float CalculateLinearChance(float value, float minValue, float peakValue, float minChance, float maxChance, float finalMaxChance)
	{
		float factor = peakValue <= minValue ? (value >= peakValue ? 1f : 0f) : Mathf.Clamp01((value - minValue) / (peakValue - minValue));
		float chance = minChance + (maxChance - minChance) * factor;
		return Mathf.Clamp(chance, 0f, finalMaxChance);
	}

	private static bool RollChance(float chance, long actorId, int year, string salt)
	{
		float roll = XjDeterministicHash.Roll01(actorId, year, "aptitude", salt);
		return roll < Mathf.Clamp01(chance);
	}

	private static int RollXjZz(Actor actor, long actorId, int year)
	{
		float birthPotentialBias = XjBloodlineBirthRules.GetAptitudeTierWeightBias(actor);
		float totalWeight = 0f;
		for (int i = 0; i < AptitudeWeights.Length; i++)
		{
			XjAptitudeWeight weight = AptitudeWeights[i];
			totalWeight += weight.Weight * GetBirthPotentialWeightMultiplier(weight.Value, birthPotentialBias);
		}

		float roll = XjDeterministicHash.Roll01(actorId, year, "aptitude", "zz_roll") * totalWeight;
		for (int i = 0; i < AptitudeWeights.Length; i++)
		{
			XjAptitudeWeight weight = AptitudeWeights[i];
			float scaledWeight = weight.Weight * GetBirthPotentialWeightMultiplier(weight.Value, birthPotentialBias);
			if (roll < scaledWeight)
			{
				return weight.Value;
			}

			roll -= scaledWeight;
		}

		return AptitudeWeights[AptitudeWeights.Length - 1].Value;
	}

	private static bool RollXianTianDaoTi(long actorId, int year, int xjZz)
	{
		float chance = Mathf.Clamp(0.10f + Math.Max(0, xjZz - 1) * 0.04f, 0.10f, 0.30f);
		return XjDeterministicHash.Roll01(actorId, year, "aptitude", "xjzz7_overlay") < chance;
	}

	private static float GetBirthPotentialWeightMultiplier(int xjZz, float birthPotentialBias)
	{
		float maxMultiplier = xjZz switch
		{
			3 => 1.25f,
			4 => 1.45f,
			5 => 1.75f,
			6 => 2.1f,
			_ => 1f
		};
		return Mathf.Lerp(1f, maxMultiplier, Mathf.Clamp01(birthPotentialBias));
	}

	private readonly struct XjAptitudeWeight
	{
		internal readonly int Value;
		internal readonly float Weight;

		internal XjAptitudeWeight(int value, float weight)
		{
			Value = value;
			Weight = weight;
		}
	}
}
