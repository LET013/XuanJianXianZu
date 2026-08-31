using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Era;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.YaoShu;
using System;
using System.Globalization;

namespace XuanJianVNext.Systems.Cultivation;

internal readonly struct XjCultivationLocalCheckResult
{
	internal readonly bool HasNextRealm;
	internal readonly string CurrentRealmId;
	internal readonly string TargetRealmId;
	internal readonly bool Passed;
	internal readonly string ReasonCode;

	internal XjCultivationLocalCheckResult(
		bool hasNextRealm,
		string currentRealmId,
		string targetRealmId,
		bool passed,
		string reasonCode)
	{
		HasNextRealm = hasNextRealm;
		CurrentRealmId = currentRealmId ?? string.Empty;
		TargetRealmId = targetRealmId ?? string.Empty;
		Passed = passed;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjCultivationLocalCore
{
	internal static XjCultivationLocalCheckResult CheckNextRealm(Actor actor)
	{
		if (actor == null)
		{
			return new XjCultivationLocalCheckResult(false, string.Empty, string.Empty, false, "ActorNull");
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmManualRemoved, out int manualRemoved) && manualRemoved > 0)
		{
			return new XjCultivationLocalCheckResult(false, string.Empty, string.Empty, false, "RealmManuallyRemoved");
		}

		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		XjCaiQiSnapshot caiQiSnapshot = XjCaiQiActorAccessor.BuildSnapshot(actor);
		return CheckNextRealm(snapshot, caiQiSnapshot);
	}

	internal static XjCultivationLocalCheckResult CheckNextRealm(
		in XjActorCultivationSnapshot snapshot,
		in XjCaiQiSnapshot caiQiSnapshot)
	{
		if (!XjCultivationNextRealmResolver.TryGetNextRule(snapshot.RealmId, out XjRealmRule targetRule))
		{
			string reasonCode = string.Equals(snapshot.RealmId, XjRealmIds.DaoTai, System.StringComparison.Ordinal)
				|| string.Equals(snapshot.RealmId, XjRealmIds.FuQiDaoTai, System.StringComparison.Ordinal)
				|| string.Equals(snapshot.RealmId, XjRealmIds.DaoTaiPlaceholder, System.StringComparison.Ordinal)
				? "RuleNotImplemented"
				: "RuleMissing";
			return new XjCultivationLocalCheckResult(false, snapshot.RealmId, string.Empty, false, reasonCode);
		}

		if (targetRule.RequiresFiveXianJi)
		{
			return new XjCultivationLocalCheckResult(true, snapshot.RealmId, targetRule.RealmId, false, "RequiresFiveXianJi");
		}

		XjCultivationRuleCheckResult checkResult = XjCultivationRuleValidator.Check(snapshot, targetRule, caiQiSnapshot);
		return new XjCultivationLocalCheckResult(
			true,
			snapshot.RealmId,
			targetRule.RealmId,
			checkResult.Passed,
			checkResult.ReasonCode);
	}
}

internal static class XjCultivationNextRealmResolver
{
	internal static bool TryGetNextRule(string currentRealmId, out XjRealmRule rule)
	{
		string targetRealmId = ResolveNextRealmId(currentRealmId);
		if (string.IsNullOrEmpty(targetRealmId))
		{
			rule = default;
			return false;
		}

		return XjRealmRules.TryGet(targetRealmId, out rule);
	}

	private static string ResolveNextRealmId(string currentRealmId)
	{
		if (string.IsNullOrWhiteSpace(currentRealmId))
		{
			return XjRealmIds.TaiXi;
		}

		if (string.Equals(currentRealmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			return XjRealmIds.LianQi;
		}

		if (string.Equals(currentRealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return XjRealmIds.ZhuJi;
		}

		if (string.Equals(currentRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return XjRealmIds.ZiFu;
		}

		if (string.Equals(currentRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return XjRealmIds.JinDan;
		}

		if (string.Equals(currentRealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return XjRealmIds.DaoTai;
		}

		return string.Empty;
	}
}

internal readonly struct XjCultivationRuleCheckResult
{
	internal readonly bool Passed;
	internal readonly string ReasonCode;

	internal XjCultivationRuleCheckResult(bool passed, string reasonCode)
	{
		Passed = passed;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjCultivationRuleValidator
{
	internal static XjCultivationRuleCheckResult Check(
		in XjActorCultivationSnapshot snapshot,
		in XjRealmRule targetRule)
	{
		return Check(snapshot, targetRule, XjCaiQiSnapshot.Empty);
	}

	internal static XjCultivationRuleCheckResult Check(
		in XjActorCultivationSnapshot snapshot,
		in XjRealmRule targetRule,
		in XjCaiQiSnapshot caiQiSnapshot)
	{
		if (!targetRule.IsImplemented)
		{
			return new XjCultivationRuleCheckResult(false, "RuleNotImplemented");
		}

		if (snapshot.ZhenYuan < targetRule.RequiredZhenYuan)
		{
			return new XjCultivationRuleCheckResult(false, "InsufficientZhenYuan");
		}

		if (string.Equals(targetRule.RealmId, XjRealmIds.ZhuJi, System.StringComparison.Ordinal)
			&& caiQiSnapshot.LianQiByZaQi)
		{
			return new XjCultivationRuleCheckResult(
				false,
				"LianQiByZaQiBlocksZhuJi");
		}

		if (targetRule.RequiresCaiQi && !caiQiSnapshot.HasCompletedCaiQi)
		{
			return new XjCultivationRuleCheckResult(false, "RequiresCaiQi");
		}

		if (targetRule.RequiresFiveXianJi)
		{
			if (snapshot.XianJiCount < 5)
			{
				return new XjCultivationRuleCheckResult(false, "RequiresFiveXianJi");
			}
		}

		if (targetRule.RequiresQiuJinFa && !snapshot.HasQiuJinFa)
		{
			return new XjCultivationRuleCheckResult(false, "RequiresQiuJinFa");
		}

		return new XjCultivationRuleCheckResult(true, "Ok");
	}
}

internal static class XjCultivationGrowthRules
{
	private const float BaseCultivationPaceMultiplier = 1f;

	internal static float ApplyRealmCap(in XjActorCultivationSnapshot snapshot, float zhenYuan)
	{
		float cap = GetRealmZhenYuanCap(snapshot.RealmId, snapshot.XianJiCount);
		if (cap <= 0f)
		{
			return (float)Math.Floor(Math.Max(0f, zhenYuan));
		}

		return (float)Math.Floor(Math.Max(0f, Math.Min(zhenYuan, cap)));
	}

	internal static float CalculateZhenYuanGain(Actor actor, float baseGain)
	{
		if (actor?.data == null || baseGain <= 0f)
		{
			return 0f;
		}

		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		int ageYear = actor == null ? 0 : (int)Math.Floor(Math.Max(0f, actor.getAge()));
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int overlayMask);
		if ((overlayMask & (1 << 8)) != 0)
		{
			return 0f;
		}

		if ((overlayMask & (1 << 9)) != 0)
		{
			return 0f;
		}

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz) || xjZz <= 0)
		{
			return 0f;
		}

		if (!TryResolveAnnualRange(xjZz, out float min, out float max))
		{
			return 0f;
		}

		// ═══════════════════════════════════════════
		// 0.5.4 兼容：修炼增长 = (血脉基础值 + 随机增长) × 功法乘数 × 年度乘数
		// ═══════════════════════════════════════════
		float bloodlineBase = ResolveBloodlineBaseValue(actor);
		float rolledGain = XjDeterministicHash.RollRange(actorId, ageYear, xjZz, min, max);
		float daoZhuMultiplier = actor.hasTrait("ChuShen8") ? 2f : 1f;
		float annualGain = (bloodlineBase + rolledGain)
			* BaseCultivationPaceMultiplier
			* ResolveGongFaMultiplier(actor)
			* ResolveZhuJiAptitudeMultiplier(actor, xjZz)
			* daoZhuMultiplier
			* XjXuanJianShenTongSpecials.GetCultivationSpeedMultiplier(actor)
			* XjLongShuSystem.GetCultivationSpeedMultiplier(actor)
			* XjYaoShuSapientSpecies.GetCultivationSpeedMultiplier(actor);
		return (float)Math.Floor(Math.Max(0f, annualGain * Math.Max(1f, baseGain)));
	}

	/// <summary>
	/// 筑基阶段资质效率。统一紫府年龄锁只规定90岁前不得尝试，
	/// 实际抵达紫府所需的时间由资质在筑基阶段的积累效率拉开。
	/// 只影响筑基，不改变胎息、炼气、紫府及以上境界的年度增长。
	/// </summary>
	private static float ResolveZhuJiAptitudeMultiplier(Actor actor, int xjZz)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return 1f;
		}

		return xjZz switch
		{
			>= 6 => 3.2f,
			5 => 2f,
			4 => 1.35f,
			_ => 1f
		};
	}

	#region 0.5.4 兼容：修炼增长乘数

	/// <summary>
	/// 功法品级乘数（0.5.4 兼容）
	/// 紫府灵宝按本轮规则提供修炼速度加成；金丹法宝不额外加速修炼。
	/// </summary>
	private static float ResolveGongFaMultiplier(Actor actor)
	{
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int grade) || grade < 1)
			return 1f;
	
		return XjGongFaBonusRules.GetCultivationMultiplier(actor, grade)
			* XjFaBaoBonusService.GetCultivationSpeedMultiplier(actor);
	}

	/// <summary>
	/// 血脉基础值（0.5.4 兼容）
	/// 按血脉品质字符串与浓度阈值计算，不读旧 0.5.4 key。
	/// </summary>
	private static float ResolveBloodlineBaseValue(Actor actor)
	{
		return XjBloodlineBirthRules.GetBloodlineBaseValue(actor);
	}

	#endregion

	private static float GetRealmZhenYuanCap(string realmId, int xianJiCount)
	{
		// 尚未踏入胎息时，真元最多积到胎息入门门槛。这样资质再高也会
		// 先真实进入胎息，再按胎息六层逐年积累，而不是一年从零越过胎息。
		if (string.IsNullOrWhiteSpace(realmId))
		{
			return XjTaiXiStageRules.EntryZhenYuan;
		}

		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			return 300f;
		}

		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return 1200f;
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return 36000f;
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			// 五神通仍是求金的硬性前置，由境界规则校验；真元上限不再按已得
			// 神通数逐级封死。否则高资质紫府可能已具备足够修炼能力，却永远
			// 卡在 112000 真元以下，连 129600 的真实金门都触不到。
			return 129600f;
		}

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return 1000000f;
		}

		return 0f;
	}

	private static bool TryResolveAnnualRange(int xjZz, out float min, out float max)
	{
		switch (xjZz)
		{
			case 1:
				min = 4.5f;
				max = 12f;
				return true;
			case 2:
				min = 12f;
				max = 27f;
				return true;
			case 3:
				min = 30f;
				max = 54f;
				return true;
			case 4:
				min = 65f;
				max = 87f;
				return true;
			case 5:
				min = 79f;
				max = 105f;
				return true;
			case 6:
				min = 103f;
				max = 127f;
				return true;
			default:
				min = 0f;
				max = 0f;
				return false;
		}
	}
}
internal static class XjGongFaBonusRules
{
	internal static float GetCultivationMultiplier(Actor actor, int grade)
	{
		return GetGradeCultivationMultiplier(grade)
			* GetBloodlineOriginDaoTuLegacyMultiplier(actor)
			* XjEraBonusService.GetCultivationMultiplier(actor)
			* XjHighRealmDaoStateService.GetProofFoundationCultivationMultiplier(actor);
	}

	internal static float GetAttributeMultiplier(Actor actor, int grade)
	{
		return GetGradeAttributeMultiplier(grade) * GetBloodlineOriginDaoTuLegacyMultiplier(actor);
	}

	internal static float GetBloodlineOriginDaoTuLegacyMultiplier(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out string originDaoTu)
			|| string.IsNullOrWhiteSpace(originDaoTu))
		{
			return 1f;
		}

		return string.Equals(daoTu.Trim(), originDaoTu.Trim(), StringComparison.Ordinal) ? 1.2f : 1f;
	}

	internal static float GetEpochCultivationMultiplier(Actor actor)
	{
		return XjEraBonusService.GetCultivationMultiplier(actor);
	}

	private static float GetGradeCultivationMultiplier(int grade)
	{
		return grade switch
		{
			1 => 1.1f,
			2 => 1.2f,
			3 => 1.4f,
			4 => 1.6f,
			5 => 2f,
			6 => 2.5f,
			_ => 1f
		};
	}

	private static float GetGradeAttributeMultiplier(int grade)
	{
		return grade switch
		{
			1 => 1.05f,
			2 => 1.1f,
			3 => 1.2f,
			4 => 1.4f,
			5 => 1.8f,
			6 => 2.3f,
			_ => 1f
		};
	}

}
