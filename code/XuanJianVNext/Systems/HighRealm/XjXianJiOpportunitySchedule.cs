using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// Per-XianJi-stage opportunity clock. XjXianJiLastYear is reserved for the
/// real acquisition year of the latest XianJi; logical failed opportunities
/// are stored separately and are discarded when the prerequisite count changes.
/// </summary>
internal static class XjXianJiOpportunitySchedule
{
	internal static int EnsureStage(
		Actor actor,
		int targetCount,
		int minimumEligibilityYear,
		int executionYear,
		out int lastLogicalAttemptYear)
	{
		lastLogicalAttemptYear = 0;
		if (actor?.data == null || targetCount <= 1 || targetCount > XjXianJiState.MaxCount)
		{
			return 0;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiClockTargetCount, out int storedTarget);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiClockEligibilityYear, out int eligibilityYear);
		if (storedTarget != targetCount || eligibilityYear <= 0)
		{
			// Legacy saves did not distinguish acquisition year from logical attempt
			// year. Start the current stage at the real execution year once, avoiding
			// phantom debt from a prerequisite that did not yet exist.
			eligibilityYear = Math.Max(1, executionYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiClockTargetCount, targetCount);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiClockEligibilityYear, eligibilityYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastLogicalAttemptYear, 0);
		}

		// The persisted value is the real activation year of this prerequisite
		// stage. Age/tenure and the current pill-adjusted interval are applied by
		// the caller, so a later minimum must not overwrite the activation origin.
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiLastLogicalAttemptYear, out lastLogicalAttemptYear);
		return eligibilityYear;
	}

	internal static void MarkLogicalAttempt(Actor actor, int opportunityYear)
	{
		if (actor?.data != null)
		{
			XjActorAccessor.SetInt(
				actor,
				XjActorDataKeys.XjXianJiLastLogicalAttemptYear,
				Math.Max(0, opportunityYear));
		}
	}

	internal static void OnCollectionChanged(Actor actor, int count, int executionYear)
	{
		if (actor?.data == null) return;
		int safeYear = Math.Max(0, executionYear);
		int nextTarget = count >= 1 && count < XjXianJiState.MaxCount ? count + 1 : 0;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiClockTargetCount, nextTarget);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiClockEligibilityYear, nextTarget > 0 ? safeYear : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastLogicalAttemptYear, 0);
		if (count >= XjXianJiState.MaxCount)
		{
			XjQiuJinFaSystem.ActivateEligibility(actor, safeYear);
		}
		else
		{
			XjQiuJinFaSystem.ResetEligibility(actor);
		}
	}

	internal static void Clear(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiClockTargetCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiClockEligibilityYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastLogicalAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastExecutionYear, 0);
	}
}
