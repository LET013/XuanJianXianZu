using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 服气求证失败后的性命反震。重伤通常需要20—40年疗养，
/// 伤愈后再用30—60年重新温养金性；重复失败主要提高身死风险，不再额外拉长年限。
/// </summary>
internal static class XjFuQiInjurySystem
{
	internal static bool IsInjured(Actor actor, int currentYear = 0)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor)) return false;
		int year = currentYear > 0 ? currentYear : XjAnnualExecutionContext.ResolveYear(actor);
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, out int untilYear)
			&& untilYear > year;
	}

	internal static void ApplyRuntimePenalty(Actor actor)
	{
		if (actor?.data == null || actor.stats == null || !IsInjured(actor)) return;
		Multiply(actor, "damage", 0.65f);
		Multiply(actor, "speed", 0.80f);
		Multiply(actor, "attack_speed", 0.80f);
		Multiply(actor, "armor", 0.80f);
		Multiply(actor, "critical_chance", 0.75f);
	}

	internal static void RefreshStats(Actor actor)
	{
		if (actor?.data == null) return;
		try
		{
			actor.clearTraitCache();
			actor.setStatsDirty();
			actor.updateStats();
		}
		catch (System.Exception xjCaught43_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjFuQiInjurySystem.cs:43", xjCaught43_1);
			
			XjCombatHotPathCache.Refresh(actor);
		}
	}

	private static void Multiply(Actor actor, string statId, float multiplier)
	{
		float current = XjSafeCore.GetStatSafe(actor, statId, 0f);
		actor.stats[statId] = Math.Max(0f, current * multiplier);
	}
}
