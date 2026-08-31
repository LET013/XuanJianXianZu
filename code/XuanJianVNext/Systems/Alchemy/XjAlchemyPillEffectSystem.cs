using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Alchemy;

/// <summary>
/// 丹药的实际消费链。库存只在效果成功应用时扣减；个人库存优先，其次家族、宗门。
/// 突破丹与清神丹在对应判定发生时消费，修炼/疗伤丹在年度角色路由中按需消费。
/// </summary>
internal static class XjAlchemyPillEffectSystem
{
	private const int CombatHealRetryFrames = 30;
	private static readonly Dictionary<long, int> nextCombatHealFrameByActor = new();

	/// <summary>
	/// 战斗中的事件式服丹入口。只在最终伤害已经完成玄鉴结算、且本击可能把角色
	/// 压到危险血线时检查一次库存；不增加 Update、轮询或全表扫描。
	/// </summary>
	internal static bool TryUseCombatHealingBeforeDamage(Actor actor, float incomingDamage, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive() || incomingDamage <= 0f || currentYear <= 0) return false;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyPillCombatHealYear, out int healedYear)
			&& healedYear == currentYear) return false;
		float maxHealth = XjSafeCore.GetMaxHealthSafe(actor);
		float health = XjSafeCore.GetHealthSafe(actor);
		if (maxHealth <= 0f || health <= 0f) return false;

		float predictedRatio = Math.Max(0f, health - incomingDamage) / maxHealth;
		float currentRatio = health / maxHealth;
		if (currentRatio > 0.50f && predictedRatio > 0.30f) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		int frame = Time.frameCount;
		if (actorId <= 0L
			|| nextCombatHealFrameByActor.TryGetValue(actorId, out int nextFrame) && frame < nextFrame) return false;
		nextCombatHealFrameByActor[actorId] = frame + CombatHealRetryFrames;

		string realmId = ReadRealmId(actor);
		if (string.IsNullOrWhiteSpace(realmId)) return false;
		// 疗伤丹严格按境界分层，不再允许胎息丹一路跨用至金丹。
		string realmPill = ResolveHealingPill(realmId);
		bool consumed = TryConsumeAndApply(actor, realmPill, currentYear);
		if (consumed) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyPillCombatHealYear, currentYear);
		return consumed;
	}

	internal static void ForgetCombatRuntime(long actorId)
	{
		if (actorId > 0L) nextCombatHealFrameByActor.Remove(actorId);
	}

	internal static void ClearCombatRuntime()
	{
		nextCombatHealFrameByActor.Clear();
	}
	internal static void TickAutomaticMaintenance(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || !actor.isAlive()) return;
		ApplyNanGongSustainedEffect(actor, currentYear);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyPillAutoUseYear, out int processedYear)
			&& processedYear == currentYear) return;

		string realmId = ReadRealmId(actor);
		if (string.IsNullOrWhiteSpace(realmId)
			|| !HasAnyPillFromAvailableOwner(actor, currentYear)) return;

		if (TryConsumeAndApply(actor, XjAlchemyCatalog.PillJiuYaoYanShou, currentYear))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyPillAutoUseYear, currentYear);
			return;
		}

		float health = XjSafeCore.GetHealthSafe(actor);
		float maxHealth = XjSafeCore.GetMaxHealthSafe(actor);
		if (maxHealth > 0f && health > 0f && health / maxHealth < 0.85f)
		{
			string healingPill = ResolveHealingPill(realmId);
			if (TryConsumeAndApply(actor, healingPill, currentYear))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyPillAutoUseYear, currentYear);
				return;
			}
		}

		if (TryConsumeCultivationPill(actor, realmId, currentYear))
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyPillAutoUseYear, currentYear);
	}

	internal static float TryConsumeBreakthroughBonus(Actor actor, string targetRealmId, int currentYear)
	{
		string pillId = string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			? XjAlchemyCatalog.PillSuiYuan
			: string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
				? XjAlchemyCatalog.PillChengJin : string.Empty;
		return TryConsumeChancePill(actor, pillId, XjAlchemyEffectType.BreakthroughChance, targetRealmId, currentYear);
	}

	internal static float TryConsumeBottleneckChanceBonus(Actor actor, string realmId, int currentYear)
	{
		if (!string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			&& !string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 0f;
		return TryConsumeChancePill(actor, XjAlchemyCatalog.PillSanQuan,
			XjAlchemyEffectType.BottleneckChance, string.Empty, currentYear);
	}

	internal static float TryConsumeShenTongChanceBonus(Actor actor, int currentYear)
	{
		return TryConsumeChancePill(actor, XjAlchemyCatalog.PillQingShen,
			XjAlchemyEffectType.ShenTongChance, string.Empty, currentYear);
	}

	/// <summary>
	/// 明真合神丹只在紫府修士已经满足下一次神通修炼的功法与真元条件时启用。
	/// 药效持续十年，将原本五年一次的神通领悟判定压缩为三年一次。
	/// </summary>
	internal static int ResolveShenTongAttemptIntervalYears(Actor actor, int currentYear, int baseIntervalYears)
	{
		int normalizedBase = Math.Max(1, baseIntervalYears);
		if (normalizedBase <= 1 || actor?.data == null || currentYear <= 0
			|| !string.Equals(ReadRealmId(actor), XjRealmIds.ZiFu, StringComparison.Ordinal)) return normalizedBase;

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyMingZhenEffectUntilYear, out int activeUntil)
			&& activeUntil >= currentYear) return Math.Min(normalizedBase, 3);

		if (!XjAlchemyCatalog.TryGetPill(XjAlchemyCatalog.PillMingZhenHeShen, out XjAlchemyPillDef pill)
			|| pill.EffectType != XjAlchemyEffectType.ShenTongTrainingSpeed
			|| !CanUseByCooldown(actor, pill, currentYear)
			|| !HasPillFromAvailableOwner(actor, pill.Id, currentYear)
			|| !TryConsumeFromAvailableOwner(actor, pill.Id, currentYear, out _)) return normalizedBase;

		WriteLastUseYear(actor, pill.Id, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyMingZhenEffectUntilYear, currentYear + 9);
		XjAlchemyStatusAssets.ApplyConsumptionStatus(actor);
		return Math.Min(normalizedBase, 3);
	}

	private static float TryConsumeChancePill(
		Actor actor,
		string pillId,
		XjAlchemyEffectType expectedEffect,
		string targetRealmId,
		int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || string.IsNullOrWhiteSpace(pillId)
			|| !XjAlchemyCatalog.TryGetPill(pillId, out XjAlchemyPillDef pill)
			|| pill.EffectType != expectedEffect
			|| (!string.IsNullOrWhiteSpace(pill.EffectTargetRealmId)
				&& !string.Equals(pill.EffectTargetRealmId, targetRealmId, StringComparison.Ordinal))
			|| !CanUsePillPath(actor, pill)
			|| !CanUseByCooldown(actor, pill, currentYear)) return 0f;

		if (!TryConsumeFromAvailableOwner(actor, pillId, currentYear, out _)) return 0f;
		WriteLastUseYear(actor, pillId, currentYear);
		XjAlchemyStatusAssets.ApplyConsumptionStatus(actor);
		return pill.EffectValue;
	}

	private static bool TryConsumeAndApply(Actor actor, string pillId, int currentYear)
	{
		if (string.IsNullOrWhiteSpace(pillId)
			|| !XjAlchemyCatalog.TryGetPill(pillId, out XjAlchemyPillDef pill)
			|| !CanUsePillPath(actor, pill)
			|| !IsApplicableToCurrentRealm(actor, pill)
			|| !CanUseByCooldown(actor, pill, currentYear)) return false;

		// 大多数角色和仓库没有目标丹药。先用 O(1) 计数索引门禁，
		// 避免无库存时仍构建完整修炼快照或遍历丹药批次。
		if (!HasPillFromAvailableOwner(actor, pillId, currentYear)) return false;
		if (!CanApplyEffect(actor, pill)) return false;
		if (!TryConsumeFromAvailableOwner(actor, pillId, currentYear, out _)) return false;

		// 丹药不再区分成品品质，所有药效均按丹方固定值结算。
		float multiplier = 1f;
		bool applied = pill.EffectType switch
		{
			XjAlchemyEffectType.ZhenYuan => ApplyZhenYuan(actor, pill.EffectValue * multiplier),
			XjAlchemyEffectType.SustainedZhenYuan => StartNanGongSustainedEffect(actor, pill.EffectValue * multiplier, currentYear),
			XjAlchemyEffectType.HealingPercent => ApplyHealing(actor, pill.EffectValue * multiplier),
			XjAlchemyEffectType.LifespanYears => ApplyLifespan(actor, (int)Math.Round(pill.EffectValue), currentYear),
			_ => false
		};
		if (!applied) return false;
		WriteLastUseYear(actor, pillId, currentYear);
		XjAlchemyStatusAssets.ApplyConsumptionStatus(actor);
		return true;
	}

	private static bool CanApplyEffect(Actor actor, XjAlchemyPillDef pill)
	{
		if (pill.EffectType == XjAlchemyEffectType.HealingPercent)
		{
			float max = XjSafeCore.GetMaxHealthSafe(actor);
			return max > 0f && XjSafeCore.GetHealthSafe(actor) < max;
		}
		if (pill.EffectType == XjAlchemyEffectType.ZhenYuan
			|| pill.EffectType == XjAlchemyEffectType.SustainedZhenYuan)
		{
			if (pill.EffectType == XjAlchemyEffectType.SustainedZhenYuan
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyNanGongEffectUntilYear, out int activeUntil)
				&& activeUntil >= Math.Max(0, World.world?.map_stats?.year ?? 0)) return false;
			XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			return XjCultivationGrowthRules.ApplyRealmCap(snapshot, snapshot.ZhenYuan + pill.EffectValue) > snapshot.ZhenYuan;
		}
		if (pill.EffectType == XjAlchemyEffectType.LifespanYears)
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyPillJiuYaoYanShouConsumed, out int consumed)
				&& consumed > 0) return false;
			try
			{
				float remaining = actor.stats["lifespan"] - actor.getAge();
				return remaining <= 100f;
			}
			catch { return false; }
		}
		return false;
	}

	private static bool StartNanGongSustainedEffect(Actor actor, float annualZhenYuan, int currentYear)
	{
		if (actor?.data == null || annualZhenYuan <= 0f || currentYear <= 0) return false;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyNanGongEffectUntilYear, currentYear + 9);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyNanGongLastTickYear, currentYear);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjAlchemyNanGongAnnualZhenYuan, annualZhenYuan);
		return ApplyZhenYuan(actor, annualZhenYuan);
	}

	private static void ApplyNanGongSustainedEffect(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0
			|| !string.Equals(ReadRealmId(actor), XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyNanGongEffectUntilYear, out int activeUntil)
			|| activeUntil < currentYear
			|| XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyNanGongLastTickYear, out int lastTickYear)
				&& lastTickYear >= currentYear) return;

		float annualZhenYuan = XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.XjAlchemyNanGongAnnualZhenYuan, out float stored)
			? Math.Max(0f, stored) : 600f;
		if (annualZhenYuan <= 0f) annualZhenYuan = 600f;
		ApplyZhenYuan(actor, annualZhenYuan);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyNanGongLastTickYear, currentYear);
	}

	private static bool ApplyZhenYuan(Actor actor, float amount)
	{
		if (amount <= 0f) return false;
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		float next = XjCultivationGrowthRules.ApplyRealmCap(snapshot, snapshot.ZhenYuan + amount);
		next = XjBottleneckEventSystem.ApplyGrowthGate(actor, in snapshot, next);
		if (next > snapshot.ZhenYuan)
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, next);
		}
		// 丹力会蓄积在当前瓶颈，冲关成功后完整返还；本次仍视为已服用，避免同年连续吞服。
		return true;
	}

	private static bool ApplyHealing(Actor actor, float percent)
	{
		float max = XjSafeCore.GetMaxHealthSafe(actor);
		float current = XjSafeCore.GetHealthSafe(actor);
		if (max <= 0f || current >= max || percent <= 0f) return false;
		int amount = Math.Max(1, (int)Math.Floor(max * Math.Min(1f, percent)));
		actor.restoreHealth(amount);
		return true;
	}

	private static bool ApplyLifespan(Actor actor, int years, int currentYear)
	{
		if (actor?.data == null || years <= 0
			|| XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyPillJiuYaoYanShouConsumed, out int consumed)
				&& consumed > 0) return false;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyLifespanBonus, out int existing);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyLifespanBonus, Math.Max(0, existing) + years);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyPillJiuYaoYanShouConsumed, 1);
		try { actor.stats["lifespan"] = Math.Max(0f, actor.stats["lifespan"]) + years; } catch (System.Exception xjCaught285) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Alchemy/XjAlchemyPillEffectSystem.cs:285", xjCaught285); }
		try { actor.setStatsDirty(); } catch (System.Exception xjCaught286) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Alchemy/XjAlchemyPillEffectSystem.cs:286", xjCaught286); }

		try
		{
			string actorName = actor.getName();
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Craft,
				actorName + "服下九曜延寿丹",
				actorName + "服下九曜延寿丹，寿元延长" + years + "年。此丹一生只可服用一次。",
				4,
				actorId: ((BaseSystemData)actor.data).id,
				actorName: actorName,
				year: currentYear);
		}
		catch (System.Exception xjCaught300) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Alchemy/XjAlchemyPillEffectSystem.cs:300", xjCaught300); }
		return true;
	}

	private static bool HasAnyPillFromAvailableOwner(Actor actor, int currentYear)
	{
		if (XjAlchemyOwnerResolver.TryGetPersonal(actor, out XjAlchemyOwnerKey personal)
			&& XjAlchemyInventoryRegistry.HasAnyPill(personal, currentYear)) return true;
		if (XjAlchemyOwnerResolver.TryGetFamily(actor, out XjAlchemyOwnerKey family)
			&& XjAlchemyInventoryRegistry.HasAnyPill(family, currentYear)) return true;
		return XjAlchemyOwnerResolver.TryGetZongMen(actor, out XjAlchemyOwnerKey zongMen)
			&& XjAlchemyInventoryRegistry.HasAnyPill(zongMen, currentYear);
	}

	private static bool HasPillFromAvailableOwner(Actor actor, string pillId, int currentYear)
	{
		if (XjAlchemyOwnerResolver.TryGetPersonal(actor, out XjAlchemyOwnerKey personal)
			&& XjAlchemyInventoryRegistry.HasPill(personal, pillId, currentYear)) return true;
		if (XjAlchemyOwnerResolver.TryGetFamily(actor, out XjAlchemyOwnerKey family)
			&& XjAlchemyInventoryRegistry.HasPill(family, pillId, currentYear)) return true;
		return XjAlchemyOwnerResolver.TryGetZongMen(actor, out XjAlchemyOwnerKey zongMen)
			&& XjAlchemyInventoryRegistry.HasPill(zongMen, pillId, currentYear);
	}

	private static bool TryConsumeFromAvailableOwner(Actor actor, string pillId, int currentYear, out XjAlchemyQuality quality)
	{
		quality = XjAlchemyQuality.Normal;
		if (XjAlchemyOwnerResolver.TryGetPersonal(actor, out XjAlchemyOwnerKey personal)
			&& XjAlchemyInventoryRegistry.TryConsumeOnePill(personal, pillId, currentYear, out quality)) return true;
		if (XjAlchemyOwnerResolver.TryGetFamily(actor, out XjAlchemyOwnerKey family)
			&& XjAlchemyInventoryRegistry.TryConsumeOnePill(family, pillId, currentYear, out quality)) return true;
		return XjAlchemyOwnerResolver.TryGetZongMen(actor, out XjAlchemyOwnerKey zongMen)
			&& XjAlchemyInventoryRegistry.TryConsumeOnePill(zongMen, pillId, currentYear, out quality);
	}

	private static bool IsApplicableToCurrentRealm(Actor actor, XjAlchemyPillDef pill)
	{
		int tier = XjRealmSuppression.GetRealmTier(actor);
		return tier >= GetRealmTier(pill.MinApplicableRealmId) && tier <= GetRealmTier(pill.MaxApplicableRealmId);
	}

	private static bool CanUsePillPath(Actor actor, XjAlchemyPillDef pill)
	{
		if (actor?.data == null || pill == null) return false;
		if (pill.PathAffinity == XjAlchemyCultivationPath.Universal) return true;
		bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		return isFuQi
			? pill.PathAffinity == XjAlchemyCultivationPath.FuQi
			: pill.PathAffinity == XjAlchemyCultivationPath.ZiJin;
	}


	private static bool CanUseByCooldown(Actor actor, XjAlchemyPillDef pill, int currentYear)
	{
		if (string.Equals(pill.Id, XjAlchemyCatalog.PillJiuYaoYanShou, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyPillJiuYaoYanShouConsumed, out int consumed)
			&& consumed > 0) return false;
		if (pill.CooldownYears <= 0) return true;
		string key = ResolveLastUseKey(pill.Id);
		return string.IsNullOrWhiteSpace(key)
			|| !XjActorAccessor.TryGetInt(actor, key, out int lastYear)
			|| lastYear <= 0
			|| currentYear >= lastYear + pill.CooldownYears;
	}

	private static void WriteLastUseYear(Actor actor, string pillId, int currentYear)
	{
		string key = ResolveLastUseKey(pillId);
		if (!string.IsNullOrWhiteSpace(key)) XjActorAccessor.SetInt(actor, key, currentYear);
	}

	private static string ResolveLastUseKey(string pillId)
	{
		if (string.Equals(pillId, XjAlchemyCatalog.PillSheYuan, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillSheYuanLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillYangYuan, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillYangYuanLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillSuiYuan, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillSuiYuanLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillYuYa, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillYuYaLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillHuiQiu, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillHuiQiuLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillHuMai, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillHuMaiLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillQingShen, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillQingShenLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillHuanZhen, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillHuanZhenLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillChengJin, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillChengJinLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillBaoYiWenJin, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillChengJinLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillSanQuan, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillSanQuanLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillXuJiu, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillXuJiuLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillCunShenYangShen, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillXuJiuLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillHuiShuiXuanDao, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillHuiShuiXuanDaoLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillXingMingHeZhen, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillHuiShuiXuanDaoLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillJiuYaoYanShou, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillJiuYaoYanShouLastUseYear;
		if (string.Equals(pillId, XjAlchemyCatalog.PillYuLuHuiMing, StringComparison.Ordinal)) return XjActorDataKeys.XjAlchemyPillYuLuHuiMingLastUseYear;
		return string.Empty;
	}

	private static string ResolveHealingPill(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			return XjAlchemyCatalog.PillTianYiTuCui;
		}
		if (string.Equals(normalized, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			return XjAlchemyCatalog.PillHuMai;
		}
		if (string.Equals(normalized, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return XjAlchemyCatalog.PillYangYuan;
		}
		return string.Empty;
	}

	private static bool TryConsumeCultivationPill(Actor actor, string realmId, int currentYear)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (TryConsumeFuQiCultivationPill(actor, normalized, currentYear)) return true;
		if (string.Equals(normalized, XjRealmIds.ZiFu, StringComparison.Ordinal))
			return TryConsumeAndApply(actor, XjAlchemyCatalog.PillNanGongXuanSui, currentYear);
		if (string.Equals(normalized, XjRealmIds.ZhuJi, StringComparison.Ordinal))
			return TryConsumeAndApply(actor, XjAlchemyCatalog.PillHuiQiu, currentYear);
		if (string.Equals(normalized, XjRealmIds.LianQi, StringComparison.Ordinal))
			return TryConsumeAndApply(actor, XjAlchemyCatalog.PillYuYa, currentYear);
		return string.Equals(normalized, XjRealmIds.TaiXi, StringComparison.Ordinal)
			&& TryConsumeAndApply(actor, XjAlchemyCatalog.PillSheYuan, currentYear);
	}

	/// <summary>
	/// 服气养性不使用紫金修炼丹。三种服气成丹只在对应修持工程真实存在时消耗，
	/// 并缩短已经写入角色状态的完成年份；不会创建工程、补真元、塞入功法或改换道途。
	/// </summary>
	private static bool TryConsumeFuQiCultivationPill(Actor actor, string realmId, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0
			|| !XjCultivationPathRules.IsFuQiYangXing(actor)) return false;

		string pillId;
		string projectYearKey;
		XjAlchemyEffectType expectedEffect;
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			pillId = XjAlchemyCatalog.PillCunShenYangShen;
			projectYearKey = XjActorDataKeys.FuQiBodyProjectCompleteYear;
			expectedEffect = XjAlchemyEffectType.FuQiBodyProjectYears;
		}
		else if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			if (XjActorAccessor.TryGetInt(
					actor,
					XjActorDataKeys.FuQiPerfectionProjectCompleteYear,
					out int perfectionCompleteYear)
				&& perfectionCompleteYear > currentYear + 1)
			{
				pillId = XjAlchemyCatalog.PillXingMingHeZhen;
				projectYearKey = XjActorDataKeys.FuQiPerfectionProjectCompleteYear;
				expectedEffect = XjAlchemyEffectType.FuQiPerfectionProjectYears;
			}
			else if (XjFuQiBalancePolicy.CanAcceleratePostFailureNurture(actor, currentYear)
					&& XjActorAccessor.TryGetInt(
						actor,
						XjActorDataKeys.FuQiShenMiaoPerfectionYear,
						out int perfectedYear)
					&& perfectedYear > 0
					&& XjActorAccessor.TryGetInt(
						actor,
						XjActorDataKeys.FuQiJinXingNurtureCompleteYear,
						out int nurtureCompleteYear)
					&& nurtureCompleteYear > currentYear + 1)
			{
				pillId = XjAlchemyCatalog.PillBaoYiWenJin;
				projectYearKey = XjActorDataKeys.FuQiJinXingNurtureCompleteYear;
				expectedEffect = XjAlchemyEffectType.FuQiJinXingNurtureYears;
			}
			else
			{
				return false;
			}
		}
		else
		{
			return false;
		}

		if (!XjActorAccessor.TryGetInt(actor, projectYearKey, out int oldCompleteYear)
			|| oldCompleteYear <= currentYear + 1
			|| !XjAlchemyCatalog.TryGetPill(pillId, out XjAlchemyPillDef pill)
			|| pill.PathAffinity != XjAlchemyCultivationPath.FuQi
			|| pill.EffectType != expectedEffect
			|| !CanUseByCooldown(actor, pill, currentYear)
			|| !HasPillFromAvailableOwner(actor, pillId, currentYear))
		{
			return false;
		}

		int reductionYears = Math.Max(1, (int)Math.Round(pill.EffectValue));
		int nextCompleteYear = Math.Max(currentYear + 1, oldCompleteYear - reductionYears);
		if (nextCompleteYear >= oldCompleteYear
			|| !TryConsumeFromAvailableOwner(actor, pillId, currentYear, out _)) return false;

		XjActorAccessor.SetInt(actor, projectYearKey, nextCompleteYear);
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			&& string.Equals(projectYearKey, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, StringComparison.Ordinal))
		{
			// 丹药缩短性命合炼工程后可能立即跨过真人修持档，及时标记属性重建。
			XjRealmStageStatMultiplierService.MarkDirtyWhenStale(actor);
		}
		WriteLastUseYear(actor, pillId, currentYear);
		XjAlchemyStatusAssets.ApplyConsumptionStatus(actor);
		return true;
	}

	private static string ReadRealmId(Actor actor)
	{
		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			? realmId : string.Empty;
	}

	private static int GetRealmTier(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return XjRealmSuppression.TierJinDan;
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return XjRealmSuppression.TierZhuJi;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return XjRealmSuppression.TierJinDan;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return XjRealmSuppression.TierZhuJi;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return XjRealmSuppression.TierLianQi;
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return XjRealmSuppression.TierTaiXi;
		return XjRealmSuppression.TierNone;
	}
}
