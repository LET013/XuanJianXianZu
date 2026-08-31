using System;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.Events;

namespace XuanJianVNext.Systems.Aptitude;

internal readonly struct XjAptitudeExecuteResult
{
	internal readonly bool ActorValid;
	internal readonly bool Checked;
	internal readonly bool Granted;
	internal readonly int XjZz;
	internal readonly string ReasonCode;

	internal XjAptitudeExecuteResult(
		bool actorValid,
		bool checkedNow,
		bool granted,
		int xjZz,
		string reasonCode)
	{
		ActorValid = actorValid;
		Checked = checkedNow;
		Granted = granted;
		XjZz = xjZz;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjAptitudeLocalExecutor
{
	internal static XjAptitudeExecuteResult TryRunAgeFiveCheck(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjAptitudeExecuteResult(false, false, false, 0, "NotEligible");
		}

		if (!XjCultivationEligibility.CanCultivate(actor))
		{
			return new XjAptitudeExecuteResult(false, false, false, 0, "NotSapientCultivator");
		}

		int ageYear = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzCheckedAge5, out int checkedAgeFive);
		if (checkedAgeFive != 0)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int existingXjZz);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int existingOverlayMask);
			if (existingXjZz > 0 || existingOverlayMask != 0)
			{
				XjVisibleTraitSync.SyncCultivationTraits(actor);
			}

			return new XjAptitudeExecuteResult(true, false, false, 0, "AlreadyChecked");
		}

		if (ageYear < 5)
		{
			return new XjAptitudeExecuteResult(true, false, false, 0, "AgeBelowFive");
		}

		// 释修与仙修根骨切割：先独立判定缘法，真正入释后本角色不再抽取/显示 XjZz。
		// 这里仍复用五岁生命周期入口，但两套规则互不传参，也不会让“有资质”成为入释条件。
		int currentYear = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		bool isSongXuan = XjSongXuanEasterEggSystem.IsSongXuan(actor);
		bool isZhangYan = XjZhangYanEasterEggSystem.IsZhangYan(actor);
		if (!isSongXuan && !isZhangYan
			&& !XjCultivationPathRules.TryGetPath(actor, out _)
			&& XjShiEntrySystem.TryEnterInitialIndependent(actor, currentYear))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, 0);
			XjCultivationSeed.RefreshChuShenForCultivationState(actor);
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			XjAptitudeExecuteResult shi = new XjAptitudeExecuteResult(true, true, false, 0, "ShiIndependentEntry");
			XjStageZeroObservation.RecordAptitudeResult(actor, shi);
			return shi;
		}

		if (!XjAptitudeRuleEvaluator.IsAgeFiveEligibilityWindow(ageYear))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
			XjAptitudeExecuteResult missed = new XjAptitudeExecuteResult(true, true, false, 0, "AgeFiveWindowMissed");
			XjStageZeroObservation.RecordAptitudeResult(actor, missed);
			return missed;
		}

		XjAptitudeRollResult result = isZhangYan
			? new XjAptitudeRollResult(true, XjZhangYanEasterEggSystem.ResolveAptitude(actor), 0, "ZhangYanFixed")
			: XjAptitudeRuleEvaluator.EvaluateAtAgeFive(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);

		if (result.Passed)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, result.XjZz);
			// 命数/道慧曲线必须先于落霞、服气、剑道等一次性分流落定。
			// 这里只绑定基础曲线，不发真元等一次性资质效果；后者仍由 ApplyOnAgeFiveResult 统一结算。
			XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(actor, result.XjZz);
			// 落霞山只在五岁资质落定时择门人，先落师承与未来道途偏好，随后才进行首次定路。
			// 这样不会出现角色已有家学/道途后又被强制改路的补丁式逻辑。
			if (isSongXuan)
			{
				XjSongXuanEasterEggSystem.EnsureAgeFiveDestiny(actor);
			}
			else if (isZhangYan)
			{
				XjZhangYanEasterEggSystem.EnsureAgeFiveDestiny(actor);
			}
			else
			{
				XjHongXiaLuoXiaEvent.TryAcceptAgeFiveDisciple(actor, result.XjZz, currentYear);
				XjCultivationPathEntrySystem.EnsureInitialPath(actor, result.XjZz);
			}
			XjCultivationSeed.RefreshChuShenForCultivationState(actor);
		}
		else
		{
			// 未获得仙修根骨便保持无仙修资格；释修缘法已经在上方独立判定，
			// 不再通过 EnsureInitialPath(actor, 0) 反向进入释修。
		}

		if (result.OverlayMask != 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, result.OverlayMask);
		}

		XjAptitudeEffectRules.ApplyOnAgeFiveResult(actor, result);
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		if (result.Passed && result.XjZz >= 1 && result.XjZz <= 6)
		{
			XjFamilySupportSystem.NotifySupportTalentMember(actor);
		}

		XjAptitudeExecuteResult executed = new XjAptitudeExecuteResult(true, true, result.Passed, result.XjZz, result.ReasonCode);
		XjStageZeroObservation.RecordAptitudeResult(actor, executed);
		return executed;
	}
}
