using System;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;

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

		if (!XjAptitudeRuleEvaluator.IsAgeFiveEligibilityWindow(ageYear))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
			return new XjAptitudeExecuteResult(true, true, false, 0, "AgeFiveWindowMissed");
		}

		XjAptitudeRollResult result = XjAptitudeRuleEvaluator.EvaluateAtAgeFive(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);

		if (result.Passed)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, result.XjZz);
			XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out _);
			XjCultivationSeed.RefreshChuShenForCultivationState(actor);
			XjGongFaProgression.EnsureEntryGongFa(actor, XjActorCultivationSnapshotBuilder.Build(actor));
		}

		if (result.OverlayMask != 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, result.OverlayMask);
		}

		XjAptitudeEffectRules.ApplyOnAgeFiveResult(actor, result);
		XjVisibleTraitSync.SyncCultivationTraits(actor);

		return new XjAptitudeExecuteResult(true, true, result.Passed, result.XjZz, result.ReasonCode);
	}
}


