using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.GongFa;

/// <summary>
/// 功法品级尝试的持久化周期时钟。四/五品仍保持原2/3年哈希相位，
/// 但相位只用于生成下一次逻辑到期年；高倍速折叠后到期年不会被删除。
/// 六品在现行真实调用链中仍保持原本的年度尝试语义，不进入本周期时钟。
/// </summary>
internal static class XjGongFaAttemptSchedule
{
	internal const bool PreservesSkippedDueYears = true;

	internal static bool IsDue(Actor actor, int targetGrade, int currentYear)
	{
		return TryGetDueYear(actor, targetGrade, currentYear, out int dueYear)
			&& dueYear <= currentYear;
	}

	/// <summary>
	/// Candidate-index read path. It resolves (and, for legacy saves, initializes)
	/// the persisted logical due year without consuming the opportunity.
	/// </summary>
	internal static bool TryGetDueYear(Actor actor, int targetGrade, int currentYear, out int dueYear)
	{
		dueYear = 0;
		if (actor?.data == null || currentYear <= 0) return false;
		if (targetGrade <= 3)
		{
			dueYear = currentYear;
			return true;
		}
		return TryResolveDueYear(actor, targetGrade, currentYear, out dueYear);
	}

	internal static bool TryBeginAttempt(Actor actor, int targetGrade, int executionYear, out int attemptYear)
	{
		attemptYear = 0;
		if (actor?.data == null || executionYear <= 0
			|| !XjProgressionOpportunityClock.HasExecutionSlot(
				actor, XjActorDataKeys.XjGongFaLastExecutionYear, executionYear))
		{
			return false;
		}

		if (targetGrade <= 3)
		{
			attemptYear = executionYear;
		}
		else if (!TryResolveDueYear(actor, targetGrade, executionYear, out attemptYear))
		{
			return false;
		}

		XjProgressionOpportunityClock.MarkExecuted(
			actor, XjActorDataKeys.XjGongFaLastExecutionYear, executionYear);
		if (targetGrade >= 4 && TryResolveSchedule(targetGrade, out XjEntityDetectionJob job, out string nextDueKey))
		{
			XjProgressionOpportunityClock.ConsumePhasedDueYear(actor, nextDueKey, job, attemptYear);
		}
		XjStageZeroObservation.RecordOpportunityDebtConsumed("GongFaGrade" + targetGrade, attemptYear, executionYear);
		return true;
	}

	internal static void Clear(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaLastExecutionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaClockTargetGrade, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaClockEligibilityYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade4NextAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade5NextAttemptYear, 0);
	}

	/// <summary>
	/// A grade change activates the next progression stage after the real execution year.
	/// Due years accumulated before the prerequisite grade existed must not cross
	/// into the next grade's clock.
	/// </summary>
	internal static void OnGradeChanged(Actor actor, int newGrade, int executionYear)
	{
		if (actor?.data == null) return;
		int safeYear = Math.Max(0, executionYear);
		int nextTarget = newGrade >= 1 && newGrade < 5 ? newGrade + 1 : 0;
		long nextEligibility = (long)safeYear + 1L;
		int nextEligibilityYear = nextEligibility > int.MaxValue ? int.MaxValue : (int)nextEligibility;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaLastProgressionYear, safeYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaClockTargetGrade, nextTarget);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaClockEligibilityYear, nextTarget > 0 ? Math.Max(1, nextEligibilityYear) : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade4NextAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade5NextAttemptYear, 0);
	}

	private static bool TryResolveDueYear(Actor actor, int targetGrade, int currentYear, out int dueYear)
	{
		dueYear = 0;
		if (actor?.data == null || currentYear <= 0
			|| !TryResolveSchedule(targetGrade, out XjEntityDetectionJob job, out string nextDueKey))
		{
			return false;
		}

		int baselineYear = EnsureCurrentStage(actor, targetGrade, currentYear);

		return XjProgressionOpportunityClock.TryResolvePhasedDueYear(
			actor, currentYear, nextDueKey, job, baselineYear, out dueYear);
	}

	private static int EnsureCurrentStage(Actor actor, int targetGrade, int currentYear)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaClockTargetGrade, out int storedTarget);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaClockEligibilityYear, out int eligibilityYear);
		if (storedTarget != targetGrade || eligibilityYear <= 0)
		{
			// Stage5 saves did not persist the prerequisite activation year. Reset once
			// at the current execution year rather than inheriting a possibly corrupted
			// logical attempt from the previous grade.
			eligibilityYear = Math.Max(1, currentYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaClockTargetGrade, targetGrade);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaClockEligibilityYear, eligibilityYear);
			if (TryResolveSchedule(targetGrade, out _, out string nextDueKey))
			{
				XjActorAccessor.SetInt(actor, nextDueKey, 0);
			}
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaLastProgressionYear, eligibilityYear);
		}
		return eligibilityYear;
	}

	private static bool TryResolveSchedule(
		int targetGrade,
		out XjEntityDetectionJob job,
		out string nextDueKey)
	{
		switch (targetGrade)
		{
			case 4:
				job = XjEntityDetectionJob.GongFaGrade4Attempt;
				nextDueKey = XjActorDataKeys.XjGongFaGrade4NextAttemptYear;
				return true;
			case 5:
				job = XjEntityDetectionJob.GongFaGrade5Attempt;
				nextDueKey = XjActorDataKeys.XjGongFaGrade5NextAttemptYear;
				return true;
			default:
				job = default;
				nextDueKey = string.Empty;
				return false;
		}
	}
}
