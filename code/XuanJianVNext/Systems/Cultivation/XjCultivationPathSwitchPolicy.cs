using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 修法重择的唯一决策边界。自然换修不是“碰到另一条道就掷骰”，而是角色先判断
/// 当前道路是否仍有明显前景，再考虑一条整体更弱、但可能解决寿元或证道死局的替代路线。
/// 手动补录、旃檀林强制摄化等显式事务不走这里。
/// </summary>
internal static class XjCultivationPathSwitchPolicy
{
	internal const int ZhuJiToShiConsiderCooldownYears = 10;
	internal const int ZiFuToShiConsiderCooldownYears = 25;
	internal const int JinDanToShiConsiderCooldownYears = 60;

	internal const int ZhuJiToShiMaxBasisPoints = 30; // 单次最高0.30%，且十年最多一次。
	internal const int ZiFuToShiMaxBasisPoints = 8;   // 单次最高0.08%，且二十五年最多一次。
	internal const int JinDanToShiMaxBasisPoints = 2; // 单次最高0.02%，且六十年最多一次。

	/// <summary>
	/// 判断一个已经踏上紫金/服气道路的角色，当前是否真的有理由考虑自然投今释。
	/// 这里只判断动机与冷却，不进行最终概率抽签。
	/// </summary>
	internal static bool CanConsiderNaturalShiConversion(Actor actor, int sourceTier, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive()
			|| XjXianGuoSystem.IsDiMingYang(actor)
			|| XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor)
			|| !XjCultivationPathRules.TryGetPath(actor, out string path)
			|| string.Equals(path, XjCultivationPathIds.Shi, StringComparison.Ordinal)) return false;

		if (!IsConsiderationDue(actor, sourceTier, currentYear)) return false;
		float ageRatio = ResolveAgeRatio(actor);

		if (string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal))
		{
			return CanZiJinConsiderShi(actor, sourceTier, ageRatio);
		}
		if (string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal))
		{
			return CanFuQiConsiderShi(actor, sourceTier, ageRatio);
		}
		return false;
	}

	/// <summary>
	/// 在已经通过动机判定后，给出一次“真的舍旧道投释”的极低单次概率。
	/// 服气本身战力、寿元都优于同阶紫金，更不会轻易为了今释的长生性放弃本路。
	/// </summary>
	internal static int ResolveNaturalShiConversionBasisPoints(Actor actor, int sourceTier, float mingShu)
	{
		int basisPoints;
		if (sourceTier == XjRealmSuppression.TierZhuJi)
		{
			int excess = Math.Max(0, (int)Math.Floor(mingShu - 100f));
			basisPoints = Math.Min(ZhuJiToShiMaxBasisPoints, 10 + excess / 20);
		}
		else if (sourceTier == XjRealmSuppression.TierZiFu)
		{
			int excess = Math.Max(0, (int)Math.Floor(mingShu - 300f));
			basisPoints = Math.Min(ZiFuToShiMaxBasisPoints, 2 + excess / 80);
		}
		else if (sourceTier == XjRealmSuppression.TierJinDan)
		{
			basisPoints = mingShu >= 900f ? JinDanToShiMaxBasisPoints : 1;
		}
		else return 0;

		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			// 同阶服气整体强于紫金且寿元也更长，投今释的现实动机更弱。
			basisPoints = Math.Max(1, basisPoints / 2);
		}
		return Math.Max(0, basisPoints);
	}

	internal static int ResolveNaturalShiConversionCooldownYears(int sourceTier)
	{
		if (sourceTier == XjRealmSuppression.TierZhuJi) return ZhuJiToShiConsiderCooldownYears;
		if (sourceTier == XjRealmSuppression.TierZiFu) return ZiFuToShiConsiderCooldownYears;
		if (sourceTier == XjRealmSuppression.TierJinDan) return JinDanToShiConsiderCooldownYears;
		return int.MaxValue;
	}

	/// <summary>
	/// 五品真人是否值得舍服气转紫金。不是固定65%分流：只有真君前景已经明显偏低，
	/// 而紫金求金仍保留显著更好的“一求金丹”机会时才进入人物意愿判定；失败次数会
	/// 增强改道动机，真君成功前景被转世补偿重新抬高后又会降低改道意愿。
	/// </summary>
	internal static bool ShouldRank5FuQiSwitchToZiJin(
		Actor actor,
		int currentYear,
		int aptitude,
		out float fuQiHighRealmChance,
		out float projectedZiJinJinDanChance)
	{
		fuQiHighRealmChance = 0f;
		projectedZiJinJinDanChance = 0f;
		_ = currentYear;
		if (actor?.data == null || !actor.isAlive() || aptitude != 5
			|| !XjCultivationPathRules.IsFuQiYangXing(actor)) return false;

		fuQiHighRealmChance = Math.Clamp(
			XjFuQiCultivationSystem.ResolveJinDanSuccessChance(actor, aptitude), 0f, 1f);
		projectedZiJinJinDanChance = Math.Clamp(
			0.42f * XjBreakthroughRules.CalculateMingShuFactor(actor, XjRealmIds.JinDan), 0f, 0.42f);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanFailureCount, out int failures);
		failures = Math.Max(0, failures);
		float ageRatio = ResolveAgeRatio(actor);
		float gap = projectedZiJinJinDanChance - fuQiHighRealmChance;

		// 未受挫时，必须是真君前景非常差且紫金求金明显更有希望，才会考虑放弃更强的服气道。
		if (failures <= 0)
		{
			if (fuQiHighRealmChance > 0.06f || projectedZiJinJinDanChance < 0.12f || gap < 0.07f) return false;
		}
		else if (failures == 1)
		{
			if (fuQiHighRealmChance > 0.10f && ageRatio < 0.80f) return false;
			if (projectedZiJinJinDanChance < 0.14f || gap < 0.07f) return false;
		}
		else
		{
			if (projectedZiJinJinDanChance < 0.12f || gap < 0.05f) return false;
		}

		int willingness = failures switch
		{
			0 => 1800,
			1 => 3200,
			_ => 5000 + Math.Min(1500, (failures - 2) * 500)
		};
		willingness += Math.Min(1800, Math.Max(0, (int)Math.Round(gap * 6000f)));
		if (ageRatio >= 0.80f) willingness += 700;
		if (ageRatio >= 0.90f) willingness += 700;
		willingness = Math.Clamp(willingness, 0, 7800);

		long actorId = ((BaseSystemData)actor.data).id;
		// 盐只随失败层次变化；同一失败阶段不会因每年重掷最终必然改道。
		return XjDeterministicHash.PositiveIndex(actorId + failures * 7919L,
			"fuqi_rank5_switch_to_zijin_v2", 10000) < willingness;
	}

	private static bool CanZiJinConsiderShi(Actor actor, int sourceTier, float ageRatio)
	{
		if (sourceTier == XjRealmSuppression.TierZhuJi)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmBreakthroughFailureCount, out int failures);
			return ageRatio >= 0.72f || failures >= 3;
		}
		if (sourceTier == XjRealmSuppression.TierZiFu)
		{
			// 已经握有求金法就仍有正统金丹之路，不会为了战力更低的今释主动弃道。
			if (XjQiuJinFaAccessor.BuildState(actor).Found) return false;
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount, out int qiuJinFailures);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiFailureCount, out int xianJiFailures);
			int shenTongCount = Math.Max(0, XjXianJiAccessor.GetEffectiveShenTongCount(actor));
			bool routeStalled = qiuJinFailures >= 2 || xianJiFailures >= 3;
			if (shenTongCount >= 5 && !routeStalled && ageRatio < 0.85f) return false;
			return routeStalled || ageRatio >= 0.78f;
		}
		if (sourceTier == XjRealmSuppression.TierJinDan)
		{
			// 已成正统金丹之后，投今释只剩“暮年求长生”这一极端动机。
			return ageRatio >= 0.90f;
		}
		return false;
	}

	private static bool CanFuQiConsiderShi(Actor actor, int sourceTier, float ageRatio)
	{
		if (sourceTier == XjRealmSuppression.TierZhuJi)
		{
			// 黄冠本路更强且寿元更长，只有明显暮年仍未成真人才会考虑投释。
			return ageRatio >= 0.82f;
		}
		if (sourceTier == XjRealmSuppression.TierZiFu)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanFailureCount, out int failures);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
			float chance = XjFuQiCultivationSystem.ResolveJinDanSuccessChance(actor, aptitude);
			if (failures < 2 && chance >= 0.10f && ageRatio < 0.90f) return false;
			return failures >= 2 || ageRatio >= 0.90f;
		}
		if (sourceTier == XjRealmSuppression.TierJinDan)
		{
			// 真君羽士已经是本路终境，且寿元长于正统金丹，因此投释比金丹还更罕见。
			return ageRatio >= 0.93f;
		}
		return false;
	}

	private static bool IsConsiderationDue(Actor actor, int sourceTier, int currentYear)
	{
		int year = Math.Max(1, currentYear);
		int cooldown = ResolveNaturalShiConversionCooldownYears(sourceTier);
		if (cooldown == int.MaxValue) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiHighRealmConversionLastAttemptYear, out int lastYear)
			|| lastYear <= 0) return true;
		return year - lastYear >= cooldown;
	}

	private static float ResolveAgeRatio(Actor actor)
	{
		if (actor?.data == null) return 0f;
		// 年度候选筛选不主动刷新整套stats；当前已物化寿元足以判断“是否暮年”，
		// 避免今释法相在观察同城候选时反复触发updateStats。
		float lifespan = XjSafeCore.GetStatSafe(actor, "lifespan", 0f);
		if (lifespan <= 0f) return 0f;
		return Math.Clamp(Math.Max(0f, actor.getAge()) / lifespan, 0f, 2f);
	}
}
