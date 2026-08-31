using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Events;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 服气养性的统一平衡边界。紫府金丹道负责稳定、高效率地承接主流修士；
/// 服气养性保留寿元、道途表现与转世上限，但入口更稀少、求证代价更沉重。
/// 所有入口、求证、重伤和转世概率必须经此策略读取，禁止在 Handler 中另写魔法数。
/// </summary>
internal static class XjFuQiBalancePolicy
{
	internal const int MaxReincarnationBreakthroughBonusPercent = 8;
	internal const float ReincarnationRank6Chance = 0.40f;

	internal static float ResolveSenseChance(
		int aptitude,
		float huiGuangFactor,
		float mingShuFactor,
		bool hasDirectFuQiParent)
	{
		huiGuangFactor = Math.Clamp(huiGuangFactor, 0f, 1f);
		mingShuFactor = Math.Clamp(mingShuFactor, 0f, 1f);
		switch (aptitude)
		{
			case 4:
				// 四档只允许命数与道慧都处于本档高位者破格感气。即使满足门槛，
				// 自然感气率也显著低于五、六档；同道直系只能提供有限加成。
				if (huiGuangFactor * 100f < XjTalentOpportunityRules.Rank4FuQiMinimumHuiGuang
					|| mingShuFactor * 100f < XjTalentOpportunityRules.Rank4FuQiMinimumMingShu
					|| (huiGuangFactor + mingShuFactor) * 100f < XjTalentOpportunityRules.Rank4FuQiMinimumCombined) return 0f;
				return Math.Clamp(
					0.025f
					+ 0.025f * huiGuangFactor
					+ 0.015f * mingShuFactor
					+ (hasDirectFuQiParent ? 0.03f : 0f),
					0f,
					0.09f);
			case 6:
				// 天赐层极罕见，可以较明显地体现服气亲和：自然约27%—31%，
				// 同道直系传承最多抬到36%，但仍绝非必定感气。
				return Math.Clamp(
					0.20f
					+ 0.08f * huiGuangFactor
					+ 0.03f * mingShuFactor
					+ (hasDirectFuQiParent ? 0.06f : 0f),
					0f,
					0.36f);
			case 5:
				// 纯一层承担绝大多数自然服气入口：自然约12%—15%，
				// 同道直系传承最高19%；四档破格者只形成低概率补充。
				return Math.Clamp(
					0.08f
					+ 0.05f * huiGuangFactor
					+ 0.02f * mingShuFactor
					+ (hasDirectFuQiParent ? 0.04f : 0f),
					0f,
					0.19f);
			default:
				return 0f;
		}
	}

	internal static float ResolveHighRealmSuccessChance(Actor actor, int aptitude)
	{
		if (actor?.data == null) return 0f;
		if (XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor)) return 1f;
		float baseCap;
		switch (aptitude)
		{
			case 6:
				baseCap = 0.30f;
				break;
			case 5:
				baseCap = 0.18f;
				break;
			case 4:
				// 沿用四档紫金“可破格叩高境”的思路，但服气这里把成功窗口压得更窄。
				baseCap = 0.08f;
				break;
			default:
				return 0f;
		}

		XjActorAccessor.TryGetInt(
			actor,
			XjActorDataKeys.FuQiReincarnationBreakthroughBonusPercent,
			out int storedBonus);
		int normalizedBonus = Math.Clamp(storedBonus, 0, MaxReincarnationBreakthroughBonusPercent);
		if (normalizedBonus != storedBonus)
		{
			// 旧档中最高可积累 24%，在第一次真实判定时收口到新边界。
			XjActorAccessor.SetInt(
				actor,
				XjActorDataKeys.FuQiReincarnationBreakthroughBonusPercent,
				normalizedBonus);
		}

		float baseChance = Math.Clamp(
			baseCap * XjBreakthroughRules.CalculateMingShuFactor(actor, XjRealmIds.JinDan),
			0f,
			baseCap);
		float reincarnationBonus = normalizedBonus / 100f;
		float resolved = Math.Min(baseCap + MaxReincarnationBreakthroughBonusPercent / 100f,
			baseChance + reincarnationBonus);
		return resolved;
	}

	internal static XjFuQiFailureProfile ResolveFailureProfile(int completedFailureCount)
	{
		int count = Math.Max(1, completedFailureCount);
		if (count == 1)
		{
			return new XjFuQiFailureProfile(0.45f, 0.20f);
		}
		if (count == 2)
		{
			return new XjFuQiFailureProfile(0.30f, 0.20f);
		}
		return new XjFuQiFailureProfile(0.15f, 0.15f);
	}

	internal static int ResolveSevereInjuryYears(long actorId, int currentYear, int completedFailureCount)
	{
		// 伤期回调到服气养性最初节奏：失败风险由死亡、转世和累计失败承担，
		// 不再通过两三百年的停摆惩罚存活者。
		return 20 + XjDeterministicHash.PositiveIndex(
			actorId + Math.Max(1, completedFailureCount),
			"fuqi_jindan_injury|" + currentYear,
			21);
	}

	internal static int ResolveJinXingNurtureYears(long actorId, int currentYear, int completedFailureCount)
	{
		return 30 + XjDeterministicHash.PositiveIndex(
			actorId + Math.Max(1, completedFailureCount),
			"fuqi_jinxing_nurture|" + currentYear,
			31);
	}

	internal static int ResolveReincarnationBonusGain(long actorId, int currentYear, int completedFailureCount)
	{
		return 3 + XjDeterministicHash.PositiveIndex(
			actorId + completedFailureCount,
			"fuqi_reincarnation_breakthrough_bonus_v2|" + currentYear,
			2);
	}

	internal static int ResolveReincarnationAptitude(long targetActorId, long sourceActorId, int currentYear, string lineageId)
	{
		float roll = XjDeterministicHash.Roll01(
			targetActorId + sourceActorId,
			Math.Max(1, currentYear),
			"fuqi_reincarnation_aptitude_v2",
			lineageId ?? string.Empty);
		return roll < ReincarnationRank6Chance ? 6 : 5;
	}

	internal static bool IsSevereInjuryActive(Actor actor, int currentYear)
	{
		return actor?.data != null
			&& currentYear > 0
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, out int injuryUntil)
			&& injuryUntil > currentYear;
	}

	internal static bool CanAcceleratePostFailureNurture(Actor actor, int currentYear)
	{
		return actor?.data != null && currentYear > 0 && !IsSevereInjuryActive(actor, currentYear);
	}
}

internal readonly struct XjFuQiFailureProfile
{
	internal readonly float SevereInjuryChance;
	internal readonly float ReincarnationChance;

	internal XjFuQiFailureProfile(float severeInjuryChance, float reincarnationChance)
	{
		SevereInjuryChance = Math.Clamp(severeInjuryChance, 0f, 1f);
		ReincarnationChance = Math.Clamp(reincarnationChance, 0f, 1f - SevereInjuryChance);
	}

	internal float ReincarnationUpperBound => SevereInjuryChance + ReincarnationChance;
	internal float TerminalDeathChance => Math.Max(0f, 1f - ReincarnationUpperBound);
}
