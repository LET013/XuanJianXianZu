using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Patches;

/// <summary>
/// Narrow native-AI protection for high speed. It never suppresses a search;
/// after an eligible idle actor performs a real search and still finds nothing,
/// only the native retry timeout is extended. Active targets, retaliation,
/// movement, path use, water lock and x1 gameplay are untouched.
/// </summary>
internal static class XjNativeAiPerformancePatches
{
	private readonly struct EnemySearchState
	{
		internal EnemySearchState(long actorId, bool backoffEligible)
		{
			ActorId = actorId;
			BackoffEligible = backoffEligible;
		}

		internal long ActorId { get; }
		internal bool BackoffEligible { get; }
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), nameof(Actor.b3_findEnemyTarget))]
	private static void EnemySearchPrefix(Actor __instance, out EnemySearchState __state)
	{
		__state = default;
		try
		{
			float timeScale = Config.time_scale_asset?.multiplier ?? Time.timeScale;
			if (!XjRuntimeSettings.HighSpeedEnemySearchBackoffEnabled || timeScale < 8f)
			{
				// At normal/low speed the patch is semantically inert. Avoid resolving actor
				// ids, targets and telemetry on one of WorldBox's hottest AI callbacks.
				return;
			}

			long actorId = __instance?.data is BaseSystemData data ? data.id : 0L;
			bool eligible = ShouldBackoffEmptyEnemySearch(__instance);
			XjPerformanceTelemetry.ObserveEnemySearchStart(eligible);
			__state = new EnemySearchState(actorId, eligible);
		}
		catch (System.Exception ex)
		{
			// This is one of the native AI's hottest callbacks. Failing open keeps
			// WorldBox target acquisition intact even if an external object is stale.
			XjExceptionDiagnostics.Report("Interop/Actor/b3_findEnemyTarget-prefix", ex);
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Actor), nameof(Actor.b3_findEnemyTarget))]
	private static void EnemySearchPostfix(Actor __instance, EnemySearchState __state)
	{
		try
		{
			if (!__state.BackoffEligible || __instance == null) return;

			bool foundTarget = XjActorAggroBridge.HasValidCurrentTarget(__instance);
			if (!foundTarget && __instance.has_attack_target)
			{
				XjActorAggroBridge.NormalizeCurrentTargetForNativeCheck(__instance);
			}
			XjPerformanceTelemetry.ObserveEnemySearchResult(__state.ActorId, foundTarget);
			if (foundTarget || __instance._timeout_targets <= 0f)
			{
				return;
			}

			float timeScale = Config.time_scale_asset?.multiplier ?? Time.timeScale;
			if (!ShouldEnableEnemySearchBackoff(timeScale)) return;

			float scale = Mathf.Clamp(timeScale * 0.25f, 1f, 5f);
			if (XjRuntimeWorkBudget.WorldUnitCount >= 4000) scale *= 1.25f;
			else if (XjRuntimeWorkBudget.WorldUnitCount >= 3000) scale *= 1.18f;
			else if (XjRuntimeWorkBudget.WorldUnitCount >= 1000) scale *= 1.10f;
			scale *= XjRuntimeWorkBudget.StressTier switch
			{
				XjRuntimeStressTier.Mild => 1.10f,
				XjRuntimeStressTier.Severe => 1.20f,
				XjRuntimeStressTier.Critical => 1.30f,
				_ => 1f
			};
			scale = Mathf.Min(scale, 6.5f);
			if (scale <= 1f) return;

			__instance._timeout_targets *= scale;
			XjPerformanceTelemetry.ObserveEnemySearchBackoff(scale);
		}
		catch (System.Exception ex)
		{
			XjExceptionDiagnostics.Report("Interop/Actor/b3_findEnemyTarget-postfix", ex);
		}
	}

	private static bool ShouldEnableEnemySearchBackoff(float timeScale)
	{
		// At x5 the backoff is intentionally mild (about 1.25x before population
		// pressure) and only enabled on already-large worlds. x8+ keeps the previous
		// behavior. This reduces repeated empty searches without freezing actor AI.
		if (timeScale >= 8f) return true;
		return timeScale >= 5f && XjRuntimeWorkBudget.WorldUnitCount >= 1000;
	}

	private static bool ShouldBackoffEmptyEnemySearch(Actor actor)
	{
		if (actor == null) return false;
		float timeScale = Config.time_scale_asset?.multiplier ?? Time.timeScale;
		if (!ShouldEnableEnemySearchBackoff(timeScale)) return false;
		if (XjActorAggroBridge.HasValidCurrentTarget(actor)) return false;
		if (actor.has_attack_target) XjActorAggroBridge.NormalizeCurrentTargetForNativeCheck(actor);
		if (actor.attackedBy != null)
		{
			// WorldBox declares attackedBy as BaseSimObject. Only Actor instances can
			// be validated with IsAliveActor; non-Actor sources are left to native AI.
			if (actor.attackedBy is not Actor attackingActor) return false;
			if (XjSafeCore.IsAliveActor(attackingActor)) return false;
			try { actor.attackedBy = null; }
			catch (System.Exception ex) { XjExceptionDiagnostics.Report("NativeAI.ClearStaleAttackedBy", ex); }
		}
		if (actor._timeout_targets > 0f) return false;
		if (actor.is_moving || actor.isUsingPath()) return false;
		if (!actor.isAllowedToLookForEnemies()) return false;
		if (actor.isInWaterAndCantAttack()) return false;
		return true;
	}
}
