using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// 阶段1的离散机会时钟。被动数值仍可按经过年数聚合；两年、三年、五年等
/// 周期机会使用持久化逻辑游标，不再依赖“当前年恰好命中哈希相位”。
/// 每条业务在同一执行年最多消费一次，剩余到期机会保留到后续年度管线。
/// </summary>
internal static class XjProgressionOpportunityClock
{
	internal static bool HasExecutionSlot(Actor actor, string executionYearKey, int executionYear)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(executionYearKey) || executionYear <= 0)
		{
			return false;
		}

		return !XjActorAccessor.TryGetInt(actor, executionYearKey, out int lastExecutionYear)
			|| lastExecutionYear < executionYear;
	}

	internal static void MarkExecuted(Actor actor, string executionYearKey, int executionYear)
	{
		if (actor?.data != null && !string.IsNullOrWhiteSpace(executionYearKey) && executionYear > 0)
		{
			XjActorAccessor.SetInt(actor, executionYearKey, executionYear);
		}
	}

	internal static bool TryResolvePhasedDueYear(
		Actor actor,
		int executionYear,
		string nextDueYearKey,
		XjEntityDetectionJob job,
		int eligibilityBaselineYear,
		out int dueYear)
	{
		dueYear = 0;
		if (actor?.data == null || executionYear <= 0 || string.IsNullOrWhiteSpace(nextDueYearKey))
		{
			return false;
		}

		int intervalYears = Math.Max(1, XjDetectionGate.ResolveEntityIntervalYears(job));
		if (!XjActorAccessor.TryGetInt(actor, nextDueYearKey, out int persistedDueYear) || persistedDueYear <= 0)
		{
			long actorId = ((BaseSystemData)actor.data).id;
			int baseline = Math.Max(1, eligibilityBaselineYear);
			persistedDueYear = FindFirstDueYear(job, actorId, baseline, intervalYears);
			XjActorAccessor.SetInt(actor, nextDueYearKey, persistedDueYear);
		}

		dueYear = persistedDueYear;
		return dueYear > 0 && dueYear <= executionYear;
	}

	internal static void ConsumePhasedDueYear(
		Actor actor,
		string nextDueYearKey,
		XjEntityDetectionJob job,
		int dueYear)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(nextDueYearKey) || dueYear <= 0)
		{
			return;
		}

		int intervalYears = Math.Max(1, XjDetectionGate.ResolveEntityIntervalYears(job));
		ConsumeIntervalDueYear(actor, nextDueYearKey, dueYear, intervalYears);
	}

	internal static void ConsumeIntervalDueYear(
		Actor actor,
		string nextDueYearKey,
		int dueYear,
		int intervalYears)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(nextDueYearKey) || dueYear <= 0)
		{
			return;
		}

		long candidate = (long)dueYear + Math.Max(1, intervalYears);
		int nextDueYear = candidate > int.MaxValue ? int.MaxValue : (int)candidate;
		XjActorAccessor.SetInt(actor, nextDueYearKey, nextDueYear);
	}

	internal static bool TryResolveIntervalDueYear(
		int lastLogicalOpportunityYear,
		int intervalYears,
		int executionYear,
		out int dueYear)
	{
		dueYear = 0;
		if (executionYear <= 0)
		{
			return false;
		}

		int interval = Math.Max(1, intervalYears);
		if (lastLogicalOpportunityYear <= 0)
		{
			dueYear = executionYear;
			return true;
		}

		long candidate = (long)lastLogicalOpportunityYear + interval;
		if (candidate > int.MaxValue)
		{
			return false;
		}

		dueYear = (int)candidate;
		return dueYear <= executionYear;
	}

	/// <summary>
	/// 带真实资格基线的周期机会。下一次到期年取“上次真实机会+周期”和
	/// “本阶段最早资格年”两者较晚值，避免为尚未满足年龄/境界年限的时期造债。
	/// </summary>
	internal static bool TryResolveIntervalDueYear(
		int lastLogicalOpportunityYear,
		int intervalYears,
		int eligibilityBaselineYear,
		int executionYear,
		out int dueYear)
	{
		dueYear = 0;
		if (executionYear <= 0 || eligibilityBaselineYear <= 0)
		{
			return false;
		}

		int interval = Math.Max(1, intervalYears);
		long candidate = Math.Max(1, eligibilityBaselineYear);
		if (lastLogicalOpportunityYear > 0)
		{
			long afterLast = (long)lastLogicalOpportunityYear + interval;
			candidate = Math.Max(candidate, afterLast);
		}
		if (candidate > int.MaxValue)
		{
			return false;
		}

		dueYear = (int)candidate;
		return dueYear <= executionYear;
	}

	private static int FindFirstDueYear(
		XjEntityDetectionJob job,
		long actorId,
		int baselineYear,
		int intervalYears)
	{
		int year = Math.Max(1, baselineYear);
		for (int i = 0; i < Math.Max(1, intervalYears); i++, year++)
		{
			if (XjDetectionGate.IsEntityMaintenanceSlot(job, actorId, year))
			{
				return year;
			}
		}

		return Math.Max(1, baselineYear);
	}
}
