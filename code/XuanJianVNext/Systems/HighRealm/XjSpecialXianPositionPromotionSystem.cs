using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 郁仪仙、结璘仙初成时只取得特殊高境身份，不立即占据果位名额。
/// 随个人道行/修持增长，可按自身仙基结构正式证入余位或闰位；一旦入位，
/// 特殊仙身阶段即告结束，角色回归正式金丹/真君羽士位序与姓名投影。
/// </summary>
internal static class XjSpecialXianPositionPromotionSystem
{
	internal const int ResidualProgressThreshold = 3000;
	internal const int IntercalaryProgressThreshold = 4500;
	private const int AttemptCadenceYears = 5;
	private const string TaiYinDaoTu = "太阴";
	private const string TaiYangDaoTu = "太阳";

	internal static void TickAnnual(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		bool isYuYi = XjXuanJianShenTongSpecials.IsYuYiXian(actor);
		bool isJieLin = XjXuanJianShenTongSpecials.IsJieLinXian(actor);
		if (!isYuYi && !isJieLin) return;

		// 旧档可能已经取得余位/闰位，却仍残留郁仪仙/结璘仙身份。
		// 入位完成后特殊阶段必须结束，并补齐正式金丹/真君羽士的姓名与可见投影。
		if (XjJinDanAccessor.BuildState(actor).Found)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string establishedDaoTu);
			XjXuanJianShenTongSpecials.FinalizeSpecialXianPositionIdentity(actor, establishedDaoTu);
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		int cadenceOffset = XjDeterministicHash.PositiveIndex(
			actorId,
			isYuYi ? "yuyi_position_attempt" : "jielin_position_attempt",
			AttemptCadenceYears);
		if (currentYear % AttemptCadenceYears != cadenceOffset) return;

		string sourceDaoTu = isYuYi ? TaiYangDaoTu : TaiYinDaoTu;
		bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		ResolveDesiredPosition(actor, sourceDaoTu, isFuQi, out string targetDaoTu, out string positionType);
		int progress = ReadProgress(actor, isFuQi);
		int threshold = string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			? IntercalaryProgressThreshold
			: ResidualProgressThreshold;
		if (progress < threshold) return;

		if (!XjGuoWeiQuanBingRegistry.HasEnoughAvailableAuthorities(targetDaoTu, positionType)) return;
		if (!XjGuoWeiRegistry.TryResolveAvailableGuoWeiDetailed(
			targetDaoTu,
			positionType,
			actorId,
			actorId + currentYear * 31L,
			false,
			out string resolvedType,
			out string guoWei,
			out _))
		{
			return;
		}
		if (!string.Equals(resolvedType, positionType, StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(guoWei)) return;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string previousJinXing);
		if (string.IsNullOrWhiteSpace(previousJinXing))
		{
			previousJinXing = XjJinXingCalculator.Calculate(sourceDaoTu, actorId);
		}
		string newJinXing = XjHighRealmDaoStateService.BuildPromotionJinXing(
			actor,
			sourceDaoTu,
			targetDaoTu,
			positionType,
			previousJinXing);
		if (string.IsNullOrWhiteSpace(newJinXing)) return;

		string externalDaoTu = string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			? sourceDaoTu
			: string.Empty;
		if (!XjGuoWeiRegistry.TryClaim(actor, targetDaoTu, newJinXing, guoWei, currentYear, externalDaoTu)) return;

		bool daoTuChanged = false;
		if (string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			// 服气结璘没有五仙基判闰，本系统只会令其证入太阴余位。
			if (isFuQi || !XjCultivationStateTransitions.TrySetDaoTuMetadataOnly(actor, targetDaoTu, false))
			{
				XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
				return;
			}
			daoTuChanged = true;
		}

		int specialYear = ReadSpecialFormationYear(actor, isYuYi);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int previousImage);
		XjJinDanAccessor.WriteSuccess(actor, newJinXing, guoWei, currentYear);
		if (!XjJinDanAccessor.BuildState(actor).Found)
		{
			RollbackFailedWrite(actor, sourceDaoTu, previousJinXing, specialYear, daoTuChanged, actorId, guoWei);
			return;
		}

		XjHighRealmDaoStateService.InitializeOnPromotion(
			actor,
			sourceDaoTu,
			targetDaoTu,
			positionType,
			guoWei,
			newJinXing,
			currentYear,
			isFuQi,
			recordFoundation: false,
			affectLineageVitality: true);
		if (previousImage > 0)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int initializedImage);
			if (previousImage > initializedImage)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGuoWeiImage, previousImage);
			}
		}
		XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, targetDaoTu, guoWei, currentYear);

		// 正式入位后不再保留郁仪仙/结璘仙的活动身份；其后完全按
		// 金丹真君或真君羽士参与玄鉴照录、姓名后缀、位序与权柄逻辑。
		XjXuanJianShenTongSpecials.FinalizeSpecialXianPositionIdentity(actor, targetDaoTu);

		PublishPromotion(actor, currentYear, isYuYi, targetDaoTu, positionType, guoWei, progress);
	}

	private static void ResolveDesiredPosition(
		Actor actor,
		string sourceDaoTu,
		bool isFuQi,
		out string targetDaoTu,
		out string positionType)
	{
		targetDaoTu = sourceDaoTu;
		positionType = XjGuoWeiCalculator.YuWei;
		if (isFuQi) return;

		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		if (!xianJi.Found || xianJi.Count < XjXianJiState.MaxCount) return;
		string calculated = XjGuoWeiCalculator.Calculate(actor, sourceDaoTu, xianJi);
		if (!string.Equals(calculated, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) return;
		if (!XjGuoWeiCalculator.TryResolveIntercalaryTargetDaoTu(
			sourceDaoTu,
			xianJi,
			out string intercalaryTarget,
			out _)
			|| string.IsNullOrWhiteSpace(intercalaryTarget)
			|| string.Equals(intercalaryTarget.Trim(), sourceDaoTu, StringComparison.Ordinal))
		{
			return;
		}
		targetDaoTu = intercalaryTarget.Trim();
		positionType = XjGuoWeiCalculator.RunWei;
	}

	private static int ReadProgress(Actor actor, bool isFuQi)
	{
		string key = isFuQi ? XjActorDataKeys.XjZhenJunXiuChi : XjActorDataKeys.XjJinDanDaoXing;
		XjActorAccessor.TryGetInt(actor, key, out int progress);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int compatibilityProgress);
		return Math.Max(0, Math.Max(progress, compatibilityProgress));
	}

	private static int ReadSpecialFormationYear(Actor actor, bool isYuYi)
	{
		XjActorAccessor.TryGetInt(
			actor,
			isYuYi ? XjActorDataKeys.XjYuYiXianYear : XjActorDataKeys.XjJieLinXianYear,
			out int year);
		return Math.Max(0, year);
	}

	private static void RollbackFailedWrite(
		Actor actor,
		string sourceDaoTu,
		string previousJinXing,
		int specialYear,
		bool daoTuChanged,
		long actorId,
		string guoWei)
	{
		XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
		if (daoTuChanged)
		{
			XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, sourceDaoTu, false);
		}
		XjJinDanAccessor.WriteSpecialXianPayload(actor, previousJinXing, specialYear);
	}

	private static void PublishPromotion(
		Actor actor,
		int currentYear,
		bool isYuYi,
		string targetDaoTu,
		string positionType,
		string guoWei,
		int progress)
	{
		string identity = isYuYi ? "郁仪仙" : "结璘仙";
		string actorName = actor?.getName() ?? "无名修士";
		string displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(guoWei);
		string text = "【" + identity + "入位】" + actorName + "积修至" + progress
			+ "，由" + identity + "之身正式证入【" + displayGuoWei + "】，自此列于"
			+ positionType + "位序，并以正式高境身份行世。";
		string iconId = XjEventIconCatalog.JinDanUpgrade;
		XjWorldHistoryRegistry.AddActorEvent(actor, text, iconId);
		XjThreeBookWriter.RecordSpecialXianPositionPromotion(actor, identity, targetDaoTu, positionType, displayGuoWei, currentYear);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTip(
			text,
			XjAnnouncementCategory.AuthorityPosition,
			iconId: iconId);
	}
}
