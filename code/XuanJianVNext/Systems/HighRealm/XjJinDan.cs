using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjJinDanState
{
	internal static XjJinDanState Empty { get; } = new XjJinDanState(
		false,
		string.Empty,
		string.Empty,
		string.Empty,
		0,
		0,
		"Empty");

	internal readonly bool Found;
	internal readonly string JinXing;
	internal readonly string GuoWei;
	internal readonly string FailedState;
	internal readonly int LastAttemptYear;
	internal readonly int SuccessYear;
	internal readonly string ReasonCode;

	internal XjJinDanState(
		bool found,
		string jinXing,
		string guoWei,
		string failedState,
		int lastAttemptYear,
		int successYear,
		string reasonCode)
	{
		Found = found;
		JinXing = jinXing ?? string.Empty;
		GuoWei = guoWei ?? string.Empty;
		FailedState = failedState ?? string.Empty;
		LastAttemptYear = lastAttemptYear < 0 ? 0 : lastAttemptYear;
		SuccessYear = successYear < 0 ? 0 : successYear;
		ReasonCode = reasonCode ?? string.Empty;
	}
}


internal static partial class XjJinDanBreakthroughSystem
{
	private const int SuccessEventSchemaVersion = 2;
	private const float RequiredZhenYuan = 129600f;
	private const int JinDanFailureYaoXieChancePercent = 100;

	internal static void TickActor(Actor actor, XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor)
			|| snapshot.ZhenYuan < RequiredZhenYuan
			|| !XjXianJiAccessor.HasFive(actor))
		{
			return;
		}

		// 金丹主链独立于普通境界突破器，必须在此处执行相同资质门槛。
		// 仅 XjZz5-6 可尝试；道主之资继续保留既有的强制通行规则。
		if (!actor.hasTrait("ChuShen8") && (snapshot.XjZz < 5 || snapshot.XjZz > 6))
		{
			return;
		}

		XjJinDanState jinDanState = XjJinDanAccessor.BuildState(actor);
		if (jinDanState.Found)
		{
			return;
		}
		if (!string.IsNullOrWhiteSpace(jinDanState.FailedState))
		{
			// 旧存档失败态由 Prepare 阶段统一迁移/结算；此处不允许
			// 一个字符串把活着的紫府永久锁死在金丹门外。
			ReconcileFailureDemonization(actor);
			if (!XjSafeCore.IsAliveActor(actor))
			{
				return;
			}
			jinDanState = XjJinDanAccessor.BuildState(actor);
			if (!string.IsNullOrWhiteSpace(jinDanState.FailedState))
			{
				return;
			}
		}

		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (!gongFa.Found || gongFa.Grade != 6 || !XjActorGongFaCollection.HasJinDanGongFaSet(actor))
		{
			// 金丹前必须真实持有一部六品与四部五品功法，且五部功法
			// 分别映射五门仙基。临时 UI 快照不能作为突破凭据。
			return;
		}
		// 求金法只用于五品功法晋升六品功法。角色已经真实持有六品
		// 主功法后，成丹不再以求金法状态作为前置。
		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);

		if (string.IsNullOrWhiteSpace(snapshot.DaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(snapshot.DaoTu.Trim(), out _))
		{
			XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out _);
			snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		}
		if (string.IsNullOrWhiteSpace(snapshot.DaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(snapshot.DaoTu.Trim(), out _))
		{
			// 数据修复失败时延后重试，不能把角色错误处死。
			return;
		}

		int currentYear = GetCurrentYear(actor);
		if (jinDanState.LastAttemptYear == currentYear)
		{
			return;
		}

		long actorId = GetActorId(actor);
		bool daoZhu = actor.hasTrait("ChuShen8");
		float triggerChance = daoZhu ? 1f : CalculateTriggerChance(actor, snapshot);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, currentYear);
		XjStageZeroObservation.RecordJinDanAttemptStarted();
		if (PositiveRollBasisPoints(actorId, currentYear, "jindan_trigger") >= (int)(triggerChance * 10000f))
		{
			XjStageZeroObservation.RecordJinDanResult("TriggerMiss", false);
			return;
		}

		XjXianJiState xianJiState = XjXianJiAccessor.BuildState(actor);
		if (HasCrossDaoTuXianJi(actor, snapshot.DaoTu))
		{
			XjStageZeroObservation.RecordJinDanResult("CrossDaoTuSpell", false);
			ResolveForcedDeathFailure(actor, currentYear, "CrossDaoTuSpell");
			return;
		}
		string candidateGuoWeiType = XjGuoWeiCalculator.Calculate(snapshot.DaoTu, xianJiState);
		string candidateJinDanDaoTu = string.Equals(candidateGuoWeiType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal)
			? string.Empty
			: XjGuoWeiCalculator.ResolveManifestDaoTu(snapshot.DaoTu, xianJiState, candidateGuoWeiType);
		XjGuoWeiRegistryEntry schemer = default;
		float schemerMultiplier = 1f;
		bool hasZhengWeiSchemer = false;
		if (!daoZhu)
		{
			hasZhengWeiSchemer = XjGuoWeiRegistry.TryResolveZhengWeiSchemerInterference(
				candidateJinDanDaoTu,
				candidateGuoWeiType,
				actorId,
				out schemer,
				out schemerMultiplier);
		}
		float rawSuccessChance;
		if (XjRenDan.HasPollution(actor))
		{
			// 人丹污染是隐藏终局标记，连道主之资也不能越过：本次求金必定失败。
			rawSuccessChance = 0f;
		}
		else if (daoZhu)
		{
			rawSuccessChance = 1f;
		}
		else
		{
			float aidBonus = XjAlchemyPillEffectSystem.TryConsumeBreakthroughBonus(actor, XjRealmIds.JinDan, currentYear)
				+ XjTalismanCombatService.TryConsumeBreakthroughAid(actor, XjRealmIds.JinDan);
			if (snapshot.XjZz == 5 || snapshot.XjZz == 6)
			{
				aidBonus = Math.Min(0.10f, Math.Max(0f, aidBonus));
			}
			rawSuccessChance = Math.Min(0.98f, CalculateSuccessChance(actor, snapshot) + aidBonus);
			rawSuccessChance = XjLongevityRacePenalty.ApplyJinDanBreakthroughPenalty(actor, rawSuccessChance);
		}
		float successChance = hasZhengWeiSchemer ? rawSuccessChance * schemerMultiplier : rawSuccessChance;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, string.Empty);
		int successRoll = PositiveRollBasisPoints(actorId, currentYear, "jindan_success");
		if (successRoll >= (int)(successChance * 10000f))
		{
			if (hasZhengWeiSchemer && successRoll < (int)(rawSuccessChance * 10000f))
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative,
					BuildZhengWeiSchemerFailureNarrative(actor, schemer));
			}
			XjStageZeroObservation.RecordJinDanResult("BreakthroughFailed", false);
			ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, "BreakthroughFailed");
			return;
		}

		// 太阴、太阳触及金门时，有极低概率被道胎直接截断。该结算只会
		// 出现在原本应当通过的成丹尝试中，不能把普通失败伪装成道胎之劫。
		if (XjJinDanDaoTaiInterception.TryResolve(actor, candidateJinDanDaoTu, currentYear))
		{
			XjStageZeroObservation.RecordJinDanResult("DaoTaiInterception", false);
			return;
		}

		// 果位、道途纯化与权柄容量均在真正触发突破且通过成功率判定后结算。
		// 法门组合自身无门仍属于人物求金失败；世界权柄未初始化、金性计算异常、
		// 注册表冲突等系统状态只延后到下一年重试，不能把程序状态伪装成求金失败。
		string guoWeiType = candidateGuoWeiType;
		if (string.Equals(guoWeiType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal))
		{
			XjStageZeroObservation.RecordJinDanResult("NoGuoWei", false);
			ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, "NoGuoWei");
			return;
		}

		string jinDanDaoTu = candidateJinDanDaoTu;
		if (string.IsNullOrWhiteSpace(jinDanDaoTu))
		{
			RecordDeferredAttempt(actor, "NoGuoWeiDaoTu");
			return;
		}
		if (HasCrossDaoTuXianJi(actor, jinDanDaoTu))
		{
			XjStageZeroObservation.RecordJinDanResult("CrossDaoTuSpell", false);
			ResolveForcedDeathFailure(actor, currentYear, "CrossDaoTuSpell");
			return;
		}
		if (!XjGuoWeiQuanBingRegistry.HasEnoughAvailableAuthorities(jinDanDaoTu, guoWeiType))
		{
			RecordDeferredAttempt(actor, "QuanBingDeficient");
			return;
		}

		string jinXing = XjJinXingCalculator.Calculate(jinDanDaoTu, actorId);
		if (string.IsNullOrWhiteSpace(jinXing))
		{
			RecordDeferredAttempt(actor, "JinXingUnavailable");
			return;
		}
		if (!XjGuoWeiRegistry.TryResolveAvailableGuoWeiDetailed(
				jinDanDaoTu,
				guoWeiType,
				actorId,
				actorId + currentYear,
				false,
				out _,
				out string guoWei,
				out XjGuoWeiAvailabilityReason availabilityReason))
		{
			if (availabilityReason == XjGuoWeiAvailabilityReason.Occupied)
			{
				if (TryPromoteShenDan(actor, snapshot, xianJiState, gongFa, qiuJinFa, jinDanDaoTu, guoWeiType, currentYear))
				{
					return;
				}
				// 果位确有主人但神丹挂靠容量已满：恢复0.5.4的二次失败判定。
				XjStageZeroObservation.RecordJinDanResult("ShenDanCapacityFull", false);
				ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, "ShenDanCapacityFull");
				return;
			}

			RecordDeferredAttempt(actor, "GuoWeiUnavailable:" + availabilityReason);
			return;
		}
		if (!XjGuoWeiRegistry.TryClaim(actor, jinDanDaoTu, jinXing, guoWei, currentYear))
		{
			// 同年抢位后只有在注册表已经出现真实锚点时才转神丹；否则作为瞬时冲突延后。
			if (XjGuoWeiRegistry.TryFindActiveAnchor(jinDanDaoTu, guoWeiType, out _)
				&& TryPromoteShenDan(actor, snapshot, xianJiState, gongFa, qiuJinFa, jinDanDaoTu, guoWeiType, currentYear))
			{
				return;
			}
			if (XjGuoWeiRegistry.TryFindActiveAnchor(jinDanDaoTu, guoWeiType, out _))
			{
				XjStageZeroObservation.RecordJinDanResult("ShenDanCapacityFull", false);
				ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, "ShenDanCapacityFull");
				return;
			}

			RecordDeferredAttempt(actor, "GuoWeiClaimConflict");
			return;
		}
		string originalDaoTu = snapshot.DaoTu;
		// 先写道途、后写境界，避免境界已成金丹而道途写入失败的半提交状态。
		if (!XjCultivationStateTransitions.TrySetDaoTu(actor, jinDanDaoTu, false))
		{
			XjStageZeroObservation.RecordJinDanResult("DaoTuWriteRejected", false);
			XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
			return;
		}
		if (!XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.JinDan, false))
		{
			XjStageZeroObservation.RecordJinDanResult("RealmWriteRejected", false);
			RestoreDaoTuAfterFailedPromotion(actor, originalDaoTu);
			XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
			return;
		}

		XjJinDanAccessor.WriteSuccess(actor, jinXing, guoWei, currentYear);
		if (!XjJinDanAccessor.BuildState(actor).Found)
		{
			XjStageZeroObservation.RecordJinDanResult("SuccessStateWriteFailed", false);
			XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false);
			RestoreDaoTuAfterFailedPromotion(actor, originalDaoTu);
			XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
			return;
		}

		RunJinDanSuccessStep("YinSiCleanup", () => XjYinSiTraitLifecycle.EnsureRemovedFromJinDan(actor));
		RunJinDanSuccessStep("CommonPromotion", () =>
			XjRealmPromotionHelper.ApplyCommonPostRealmWrite(actor, XjRealmIds.JinDan, currentYear));
		RunJinDanSuccessStep("Title", () =>
			XjRealmTitleApplyService.ApplyOnPromotion(actor, XjRealmIds.JinDan, jinDanDaoTu));
		RunJinDanSuccessStep("Authority", () =>
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, jinDanDaoTu, guoWei, currentYear));
		RunJinDanSuccessStep("HighGradeTransmission", () =>
			XjFamilyHighGradeTransmission.RecordJinDanGongFaSet(actor, jinDanDaoTu, gongFa, qiuJinFa, xianJiState, currentYear));
		XjStageZeroObservation.RecordJinDanResult("JinDanSuccess", true);
		RunJinDanSuccessEventChain(actor, jinDanDaoTu, jinXing, guoWei, currentYear, new XjActorCultivationSnapshot(
			snapshot.RealmId,
			jinDanDaoTu,
			snapshot.ZhenYuan,
			snapshot.MingShu,
			snapshot.HuiGuang,
			snapshot.XjZz,
			snapshot.XjZzOverlayMask,
			snapshot.XianJiCount,
			snapshot.HasQiuJinFa));
	}

	private static void RecordDeferredAttempt(Actor actor, string reason)
	{
		if (actor?.data == null)
		{
			return;
		}
		string normalizedReason = (reason ?? string.Empty).Trim();
		XjStageZeroObservation.RecordJinDanResult("Deferred:" + normalizedReason, false);
		XjActorAccessor.SetString(
			actor,
			XjActorDataKeys.XjJinDanDeferredReason,
			normalizedReason);
	}

	private static void ResolveAttemptFailure(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState xianJiState,
		int currentYear,
		string reason)
	{
		// 太阴正位在世时，太阴修士的任一真实求金失败都优先转为结璘仙。
		// 只有不满足结璘条件时，才进入金性妖邪/死亡的通用终局。
		if (XjXuanJianShenTongSpecials.TryResolveJieLinXianOnJinDanFailure(
			actor, snapshot, xianJiState, currentYear))
		{
			return;
		}

		ResolveTerminalFailure(actor, currentYear, reason);
	}

	private static void RestoreDaoTuAfterFailedPromotion(Actor actor, string originalDaoTu)
	{
		if (actor?.data == null)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(originalDaoTu)
			&& XjCultivationStateTransitions.TrySetDaoTu(actor, originalDaoTu, false))
		{
			return;
		}

		XjCultivationStateTransitions.ClearDaoTu(actor, false);
	}
	/// <summary>
	/// 计算金丹突破成功率 = maxChance * mingShuFactor；资质仅负责境界上限，不参与概率
	/// mingShu（0.5.4中称为xueQi）使用境界乘数放大先天部分
	/// </summary>
	/// <summary>
	/// 金丹赐福（金丹自生）：金丹修士每10年自动向家族注入对应道途灵气
	/// 对应 0.5.4 TickAnnualJinDanQiGift
	/// </summary>
}
