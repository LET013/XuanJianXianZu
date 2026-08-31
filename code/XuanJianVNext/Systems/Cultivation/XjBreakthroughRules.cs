using System;
using System.Globalization;
using UnityEngine;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Core;

using XuanJianVNext.Systems.DongTian;
namespace XuanJianVNext.Systems.Cultivation;

internal readonly struct XjBreakthroughAttemptResult
{
	internal readonly bool CanPromote;
	internal readonly bool ZhenYuanChanged;
	internal readonly string ReasonCode;

	internal XjBreakthroughAttemptResult(bool canPromote, bool zhenYuanChanged, string reasonCode)
	{
		CanPromote = canPromote;
		ZhenYuanChanged = zhenYuanChanged;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjBreakthroughRules
{
	internal static XjBreakthroughAttemptResult Resolve(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjCaiQiSnapshot caiQiSnapshot,
		string targetRealmId)
	{
		if (actor?.data == null)
		{
			return new XjBreakthroughAttemptResult(false, false, "ActorInvalid");
		}

		int year = GetCurrentYear(actor);
		if (!MeetsNaturalAgeAndTenureGate(actor, targetRealmId, year, out string chronologyReason))
		{
			return new XjBreakthroughAttemptResult(false, false, chronologyReason);
		}
		if (string.Equals(targetRealmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			// 胎息入门也占用当年的突破入口，防止同一年另一条年度维护旁路
			// 紧接着把角色推进炼气。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmBreakthroughLastAttemptYear, year);
			return new XjBreakthroughAttemptResult(true, false, "TaiXiEntry");
		}

		if (!actor.hasTrait("ChuShen8")
			&& string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int realmEnteredYear)
			&& realmEnteredYear >= year)
		{
			return new XjBreakthroughAttemptResult(false, false, "TaiXiMinimumStay");
		}
		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			XjGongFaState ziFuGongFa = XjGongFaAccessor.BuildState(actor);
			if (!ziFuGongFa.Found || ziFuGongFa.Grade < 4)
			{
				return new XjBreakthroughAttemptResult(false, false, "ZiFuRequiresGrade4GongFa");
			}
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmBreakthroughLastAttemptYear, out int lastAttemptYear);
		if (lastAttemptYear == year)
		{
			return new XjBreakthroughAttemptResult(false, false, "BreakthroughCooldown");
		}

		if (!XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor)
			&& (snapshot.XjZzOverlayMask & ((1 << 8) | (1 << 9))) != 0)
		{
			return new XjBreakthroughAttemptResult(false, false, "AptitudeInjuryBlocksRealm");
		}

		// 道胎之姿是完整的修炼保障：资质、经脉伤损与随机成败都不能阻断其破境。
		// 采气、功法等世界结构前置仍需真实存在；结构暂缺只延后，不转化为修炼死亡。
		if (!XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor) && !CanAttemptByAptitude(snapshot.XjZz, targetRealmId))
		{
			return new XjBreakthroughAttemptResult(false, false, "AptitudeBlocksRealm");
		}

		if (string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			&& !HasAvailableCaiQiForBreakthrough(actor, caiQiSnapshot))
		{
			return new XjBreakthroughAttemptResult(false, false, "CaiQiNotConsumed");
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmBreakthroughLastAttemptYear, year);
		// 紫府按原设计只进行一次成败判定：四品功法满足条件后以70%上限冲关。
		// 不能再叠乘一次“触发率”，否则实际概率会被压到设计值的三成以下。
		if (!string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& !RollTrigger(actor, targetRealmId, year))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.RealmBreakthroughLastResult, "TriggerMiss:" + targetRealmId);
			return new XjBreakthroughAttemptResult(false, false, "BreakthroughPending");
		}

		if (RollSuccess(actor, targetRealmId, year))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.RealmBreakthroughLastResult, "Success:" + targetRealmId);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmBreakthroughFailureCount, 0);
			return new XjBreakthroughAttemptResult(true, false, "Success");
		}

		float currentZhenYuan = Math.Max(0f, snapshot.ZhenYuan);
		float cost = GetFailureZhenYuanCost(targetRealmId, currentZhenYuan);
		float nextZhenYuan = (float)Math.Floor(Math.Max(0f, currentZhenYuan - cost));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, nextZhenYuan);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmBreakthroughFailureCount, out int failureCount);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmBreakthroughFailureCount, failureCount + 1);
		string failureResult = string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			? "ZiFuFailureDeathPending:" + targetRealmId
			: "Failure:" + targetRealmId;
		XjActorAccessor.SetString(actor, XjActorDataKeys.RealmBreakthroughLastResult, failureResult);
		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			MarkZiFuFailureDeathPending(actor, year);
		}
		return new XjBreakthroughAttemptResult(false, true, string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			? "ZiFuFailureDeathPending"
			: "BreakthroughFailed");
	}

	private static bool MeetsNaturalAgeAndTenureGate(Actor actor, string targetRealmId, int currentYear, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null) { reason = "ActorInvalid"; return false; }
		bool ignoresChronologyLock = actor.hasTrait("ChuShen8");
		int minimumAge = ResolveMinimumBreakthroughAge(targetRealmId);
		int age;
		try { age = (int)Math.Floor(Math.Max(0f, actor.getAge())); } catch (System.Exception xjCaught143_1) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjBreakthroughRules.cs:143", xjCaught143_1);
			 age = 0; }
		if (!ignoresChronologyLock && minimumAge > 0 && age < minimumAge)
		{
			reason = "MinimumAge:" + targetRealmId + ":" + minimumAge;
			return false;
		}

		int minimumStay = ResolveMinimumRealmStay(targetRealmId);
		if (!ignoresChronologyLock && minimumStay > 0 && currentYear > 0)
		{
			if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int enteredYear)
				|| enteredYear <= 0
				|| enteredYear > currentYear)
			{
				// 旧档或旁路缺失境界年份时，绝不能把“未知”解释成已停留足够久。
				// 以当前年修复锚点并阻断本次突破，之后按真实年度继续累计。
				XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmEnteredYear, currentYear);
				reason = "RealmTenureAnchorRepaired:" + targetRealmId;
				return false;
			}
			if (currentYear - enteredYear < minimumStay)
			{
				reason = "MinimumRealmTenure:" + targetRealmId + ":" + minimumStay;
				return false;
			}
		}
		return true;
	}


	internal static int ResolveMinimumBreakthroughAge(string targetRealmId)
	{
		// 年龄锁只保留“筑基”和“紫府”两个统一下限，不再按资质分档。
		// 资质差距通过筑基阶段的年度真元效率体现，而不是让不同资质拥有不同准入年龄。
		if (string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 30;
		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 90;
		return 0;
	}

	internal static int ResolveMinimumRealmStay(string targetRealmId)
	{
		if (string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return 5;
		if (string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 15;
		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 50;
		return 0;
	}

	private static void MarkZiFuFailureDeathPending(Actor actor, int year)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuFailureDeathPending, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuFailureDeathHandled, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuFailureDeathYear, year);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZiFuFailureDeathReason, "ZiFuBreakthroughFailed");
		XjZiFuFailureDeathLane.EnqueueActor(actor);
	}

	internal static string CommitCaiQiForBreakthrough(Actor actor, in XjCaiQiSnapshot caiQiSnapshot)
	{
		if (!HasAvailableCaiQiForBreakthrough(actor, caiQiSnapshot))
		{
			return string.Empty;
		}

		TryResolveFamilyKey(actor, out string familyKey);
		if (TryResolvePreferredCaiQiResourceId(actor, out string preferredResourceId)
			&& XjCaiQiAvailabilityRules.IsResourceCollectible(preferredResourceId)
			&& !string.Equals(preferredResourceId, "zaqi", StringComparison.Ordinal))
		{
			if (string.Equals(caiQiSnapshot.ResourceId, preferredResourceId, StringComparison.Ordinal)
				&& XjCaiQiActorAccessor.TryConsumePersonalResourceForBreakthrough(actor, caiQiSnapshot))
			{
				ApplyDaoTuFromConsumedCaiQiResource(actor, preferredResourceId);
				return preferredResourceId;
			}

			if (!string.IsNullOrWhiteSpace(familyKey)
			&& XjFamilyCaiQiWarehouse.TryGetCount(familyKey, preferredResourceId, out int preferredCount)
			&& preferredCount > 0
			&& XjFamilyCaiQiWarehouse.TryConsume(familyKey, preferredResourceId, 1))
			{
				XjCaiQiActorAccessor.MarkConsumedForBreakthrough(actor);
				ApplyDaoTuFromConsumedCaiQiResource(actor, preferredResourceId);
				return preferredResourceId;
			}
		}

		if (XjCaiQiActorAccessor.TryConsumePersonalResourceForBreakthrough(actor, caiQiSnapshot))
		{
			string personalResourceId = caiQiSnapshot.ResourceId ?? string.Empty;
			ApplyDaoTuFromConsumedCaiQiResource(actor, personalResourceId);
			return personalResourceId;
		}

		if (XjCaiQiActorAccessor.TryResolveWarehouseResourceId(caiQiSnapshot, out string resourceId)
			&& XjCaiQiAvailabilityRules.IsResourceCollectible(resourceId)
			&& !string.IsNullOrWhiteSpace(familyKey)
			&& XjFamilyCaiQiWarehouse.TryGetCount(familyKey, resourceId, out int count)
			&& count > 0
			&& XjFamilyCaiQiWarehouse.TryConsume(familyKey, resourceId, 1))
		{
			XjCaiQiActorAccessor.MarkConsumedForBreakthrough(actor);
			ApplyDaoTuFromConsumedCaiQiResource(actor, resourceId);
			return resourceId;
		}

		return string.Empty;
	}

	private static void ApplyDaoTuFromConsumedCaiQiResource(Actor actor, string resourceId)
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(resourceId)
			|| string.Equals(resourceId, "zaqi", StringComparison.Ordinal)
			|| !XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu)
			|| string.Equals(daoTu, "杂气", StringComparison.Ordinal))
		{
			return;
		}

		if (XjBingGuZiJinCompatibility.ShouldPreserveCurrentDaoTu(actor)) return;
		XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, true);
	}

	internal static void ApplySuccessMingShuReward(Actor actor, string targetRealmId)
	{
		if (actor?.data == null)
		{
			return;
		}

		float reward = ResolveSuccessMingShuReward(targetRealmId);
		if (reward <= 0f)
		{
			return;
		}

		XjMingShuState.AddAcquired(actor, reward);
	}

	private static float ResolveSuccessMingShuReward(string targetRealmId)
	{
		if (string.Equals(targetRealmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			return 1f;
		}

		if (string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return 10f;
		}

		if (string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			return 20f;
		}

		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			return 50f;
		}

		if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return 100f;
		}

		return 0f;
	}

	private static bool HasAvailableCaiQiForBreakthrough(Actor actor, in XjCaiQiSnapshot caiQiSnapshot)
	{
		if (!caiQiSnapshot.HasCompletedCaiQi || XjCaiQiActorAccessor.HasConsumedForBreakthrough(actor))
		{
			return false;
		}

		if (caiQiSnapshot.ResourceCount > 0
			&& !string.IsNullOrWhiteSpace(caiQiSnapshot.ResourceId)
			&& XjCaiQiAvailabilityRules.IsResourceCollectible(caiQiSnapshot.ResourceId))
		{
			return true;
		}

		if (TryResolvePreferredCaiQiResourceId(actor, out string preferredResourceId)
			&& XjCaiQiAvailabilityRules.IsResourceCollectible(preferredResourceId)
			&& TryResolveFamilyKey(actor, out string preferredFamilyKey)
			&& XjFamilyCaiQiWarehouse.TryGetCount(preferredFamilyKey, preferredResourceId, out int preferredCount)
			&& preferredCount > 0)
		{
			return true;
		}

		return XjCaiQiActorAccessor.TryResolveWarehouseResourceId(caiQiSnapshot, out string resourceId)
			&& XjCaiQiAvailabilityRules.IsResourceCollectible(resourceId)
			&& TryResolveFamilyKey(actor, out string familyKey)
			&& XjFamilyCaiQiWarehouse.TryGetCount(familyKey, resourceId, out int count)
			&& count > 0;
	}

	internal static bool TryResolveFamilyKey(Actor actor, out string familyKey)
	{
		familyKey = string.Empty;
		if (actor?.data == null || XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord record)
			|| !record.Found
			|| string.IsNullOrWhiteSpace(record.FamilyKey))
		{
			return false;
		}

		familyKey = record.FamilyKey;
		return true;
	}

	private static bool TryResolvePreferredCaiQiResourceId(Actor actor, out string resourceId)
	{
		resourceId = string.Empty;
		if (!XjFamilyDaoTuRules.TryResolvePreferredDaoTu(actor, out string preferredDaoTu)) return false;
		if (XjBingGuZiJinCompatibility.TryResolveCaiQiProxyResourceId(actor, preferredDaoTu, out resourceId))
		{
			return XjCaiQiAvailabilityRules.IsResourceCollectible(resourceId);
		}
		return XjCaiQiCatalog.TryGetEntryByDisplayName(preferredDaoTu, out XjCaiQiCatalogEntry entry)
			&& XjCaiQiCatalog.TryGetOldResourceIdByBranchId(entry.BranchId, out resourceId)
			&& !string.IsNullOrWhiteSpace(resourceId)
			&& XjCaiQiAvailabilityRules.IsResourceCollectible(resourceId);
	}

	internal static bool CanAttemptByAptitude(int xjZz, string targetRealmId)
	{
		if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return xjZz >= 4 && xjZz <= 6;
		}
		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return xjZz >= 4 && xjZz <= 6;
		}
		if (string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return xjZz >= 3 && xjZz <= 6;
		}
		return (string.Equals(targetRealmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
			&& xjZz >= 1 && xjZz <= 6;
	}


	private static bool RollTrigger(Actor actor, string targetRealmId, int year)
	{
		if (actor != null && (actor.hasTrait("ChuShen7") || actor.hasTrait("ChuShen8")))
		{
			return true;
		}

		ResolveTriggerRange(targetRealmId, out _, out float maximumChance);
		float chance = maximumChance * CalculateMingShuFactor(actor, targetRealmId);
		if (XjDaoTaiGongFaService.HasGradeSeven(actor))
		{
			chance += XjDaoTaiGongFaService.BreakthroughChanceBonus;
		}
		chance += XjMingShuChildSystem.ResolveBreakthroughSuccessBonus(actor);
		chance += XjMingShuSchemeSystem.ResolveBreakthroughSuccessBonus(actor);
		chance = Math.Min(0.98f, chance + XjEventDongTianBonusService.ResolveBreakthroughTriggerBonus(actor, year));
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		return XjDeterministicHash.Roll01(actorId, year, targetRealmId, "trigger") <= chance;
	}

	private static bool RollSuccess(Actor actor, string targetRealmId, int year)
	{
		// 道胎之姿通过全部修炼成败判定；这是修炼保障，不是战斗不死。
		if (XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor))
		{
			return true;
		}
		if (actor != null
			&& actor.hasTrait("ChuShen7")
			&& !string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return true;
		}

		float maxChance = targetRealmId switch
		{
			var id when string.Equals(id, XjRealmIds.LianQi, StringComparison.Ordinal) => 0.90f,
			var id when string.Equals(id, XjRealmIds.ZhuJi, StringComparison.Ordinal) => 0.75f,
			var id when string.Equals(id, XjRealmIds.ZiFu, StringComparison.Ordinal) => 0.70f,
			var id when string.Equals(id, XjRealmIds.JinDan, StringComparison.Ordinal) => 0.42f,
			_ => 1f
		};

		float mingShuFactor = CalculateMingShuFactor(actor, targetRealmId);
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		float faBaoBonus = XjFaBaoBonusService.GetBreakthroughChanceBonus(actor);
		float pillBonus = XjAlchemyPillEffectSystem.TryConsumeBreakthroughBonus(actor, targetRealmId, year);
		float talismanBonus = XjTalismanCombatService.TryConsumeBreakthroughAid(actor, targetRealmId);
		float eventDongTianBonus = XjEventDongTianBonusService.ResolveBreakthroughSuccessBonus(actor, targetRealmId, year);
		float imperialBonus = XjXianGuoSystem.ResolveBreakthroughSuccessBonus(actor, targetRealmId);
		float causalImpersonationBonus = XjMingShuChildSystem.ResolveBreakthroughSuccessBonus(actor);
		float mingYangSchemeBonus = XjMingShuSchemeSystem.ResolveBreakthroughSuccessBonus(actor);
		float finalCap = string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			? Math.Min(0.98f, 0.70f + imperialBonus + causalImpersonationBonus + mingYangSchemeBonus)
			: 0.98f;
		float daoTaiGongFaBonus = XjDaoTaiGongFaService.HasGradeSeven(actor)
			? XjDaoTaiGongFaService.BreakthroughChanceBonus
			: 0f;
		float chance = Math.Max(0f, Math.Min(finalCap, maxChance * mingShuFactor + faBaoBonus + pillBonus + talismanBonus + eventDongTianBonus + daoTaiGongFaBonus + imperialBonus + causalImpersonationBonus + mingYangSchemeBonus));
		chance = XjLongevityRacePenalty.ApplyBreakthroughSuccessPenalty(actor, targetRealmId, chance);
		chance = Math.Max(0f, Math.Min(finalCap, chance));
		return XjDeterministicHash.Roll01(actorId, year, targetRealmId, "success") <= chance;
	}

	/// <summary>
	/// 0.5.4 兼容：命数因子 = SmoothStep(0, 1, (先天命数×境界乘数+后天命数) / cap)
	/// 0.5.4 中称为xueQi，境界乘数用于放大先天命数对不同境界的影响力
	/// </summary>
	internal static float CalculateMingShuFactor(Actor actor, string targetRealmId)
	{
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquired);

		// 0.5.4：各境界对应不同的血气乘数和 cap 值
		ResolveRealmMingShuParams(targetRealmId, out float realmMultiplier, out float cap);
		float total = Math.Max(0f, (float)Math.Floor(congenital) * realmMultiplier + (float)Math.Floor(acquired));
		float normalized = Mathf.Clamp01(total / cap);
		return Mathf.SmoothStep(0f, 1f, normalized);
	}

	/// <summary>
	/// 0.5.4 兼容：获取境界对应的先天命数乘数(血气乘数)和 cap 值
	/// </summary>
	private static void ResolveRealmMingShuParams(string targetRealmId, out float realmMultiplier, out float cap)
	{
		if (string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			realmMultiplier = 1f;
			cap = 100f;
			return;
		}

		if (string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			realmMultiplier = 10f;
			cap = 1000f;
			return;
		}

		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			realmMultiplier = 100f;
			cap = 10000f;
			return;
		}

		if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			realmMultiplier = 1000f;
			cap = 100000f;
			return;
		}

		realmMultiplier = 1f;
		cap = 100f;
	}

	/// <summary>
	/// 0.5.4 兼容：检查直系父母是否具有金丹境界
	/// 若有则 +0.1 突破成功加成
	/// </summary>
	private static bool HasDirectParentJinDan(Actor actor)
	{
		if (actor?.data == null) return false;

		// 从 actor.data 读取父母 actorId（WorldBox 原生字段）
		long parent1 = actor.data.parent_id_1;
		long parent2 = actor.data.parent_id_2;

		if (parent1 > 0L && IsActorJinDan(parent1))
			return true;

		if (parent2 > 0L && IsActorJinDan(parent2))
			return true;

		return false;
	}

	private static bool IsActorJinDan(long targetActorId)
	{
		// 从 XjScheduler 缓存查找（所有活着的 actor 都在此注册）
		if (XjScheduler.ResolveActor(targetActorId, out Actor target))
		{
			XjActorAccessor.TryGetString(target, XjActorDataKeys.RealmId, out string realmId);
			return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal);
		}

		return false;
	}

	private static void ResolveTriggerRange(string targetRealmId, out float min, out float max)
	{
		if (string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			min = 0.28f;
			max = 0.82f;
			return;
		}

		if (string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			min = 0.18f;
			max = 0.66f;
			return;
		}

		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			min = 0.12f;
			max = 0.46f;
			return;
		}

		// 0.5.4 兼容：金丹触发范围
		if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			min = 0.08f;
			max = 0.32f;
			return;
		}

		min = 1f;
		max = 1f;
	}

	private static float GetFailureZhenYuanCost(string targetRealmId, float currentZhenYuan)
	{
		if (string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return 100f;
		}

		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return Math.Max(1000f, currentZhenYuan);
		}

		return 0f;
	}

	private static float Lerp(float min, float max, float t)
	{
		return min + (max - min) * Math.Max(0f, Math.Min(1f, t));
	}

	private static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}
}
