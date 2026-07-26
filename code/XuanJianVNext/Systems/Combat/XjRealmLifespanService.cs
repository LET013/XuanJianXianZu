using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// Keeps the lifespan shown by WorldBox and the lifespan used by natural-death
/// checks on the same authoritative realm rules. Realm traits remain the primary
/// source; this service supplies the JinDan YiXiang delta and repairs stale stat
/// caches that were built before the rebuilt trait assets were registered.
/// </summary>
internal static class XjRealmLifespanService
{
	private const float TaiXiBaseBonus = 10f;
	private const float LianQiBaseBonus = 100f;
	private const float ZhuJiBaseBonus = 200f;
	private const float ZiFuBaseBonus = 400f;
	private const float JinDanBaseBonus = 3000f;

	private static readonly HashSet<long> RepairingActorIds = new HashSet<long>();

	internal static void Clear()
	{
		RepairingActorIds.Clear();
	}

	internal static void MarkDirtyWhenStale(Actor actor)
	{
		if (!NeedsRefresh(actor))
		{
			return;
		}

		try
		{
			actor.setStatsDirty();
		}
		catch
		{
		}
	}

	internal static void EnsureFreshStats(Actor actor)
	{
		if (!NeedsRefresh(actor) || actor?.data == null)
		{
			return;
		}

		long actorId;
		try
		{
			actorId = ((BaseSystemData)actor.data).id;
		}
		catch
		{
			return;
		}

		if (actorId <= 0L || !RepairingActorIds.Add(actorId))
		{
			return;
		}

		try
		{
			actor.setStatsDirty();
			actor.updateStats();
		}
		catch
		{
		}
		finally
		{
			RepairingActorIds.Remove(actorId);
		}
	}

	/// <summary>
	/// Called only inside the real Actor.updateStats rebuild, immediately before
	/// BaseStats.normalize. It must therefore be additive exactly once per rebuild.
	/// </summary>
	internal static void ApplyRuntimeRealmLifespan(Actor actor)
	{
		if (actor?.data == null || actor.stats == null)
		{
			return;
		}

		int tier = XjRealmSuppression.GetRealmTier(actor);
		float baseRealmBonus = GetBaseRealmBonus(tier);
		float expectedRealmBonus = GetExpectedRealmBonus(actor, tier);
		if (baseRealmBonus <= 0f && expectedRealmBonus <= 0f)
		{
			return;
		}

		float current = XjSafeCore.GetStatSafe(actor, "lifespan", 0f);
		string expectedTraitId = ResolveRealmTraitId(tier);
		bool hasExpectedTrait = false;
		try
		{
			hasExpectedTrait = !string.IsNullOrWhiteSpace(expectedTraitId)
				&& actor.hasTrait(expectedTraitId);
		}
		catch
		{
		}

		// Primary path: the visible realm trait already contributed its base bonus.
		// Fallback path: an old save may still expose only the race lifespan (for
		// example 64) even though the realm trait exists. Compare against the race
		// asset baseline plus the realm minimum, not against the realm bonus alone.
		float raceBaseLifespan = GetRaceBaseLifespan(actor);
		float minimumWithRealm = raceBaseLifespan + baseRealmBonus;
		if (!hasExpectedTrait)
		{
			// Realm data without the visible trait has no other source for this bonus.
			current += baseRealmBonus;
		}
		else if (current + 0.01f < minimumWithRealm)
		{
			current = minimumWithRealm;
		}

		float highRealmDelta = Mathf.Max(0f, expectedRealmBonus - baseRealmBonus);
		actor.stats["lifespan"] = current + highRealmDelta;
	}

	internal static float GetExpectedRealmBonus(Actor actor)
	{
		return GetExpectedRealmBonus(actor, XjRealmSuppression.GetRealmTier(actor));
	}

	internal static float GetFiniteYinSiAttentionLifespan(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0f;
		}

		float finiteBaseline = GetRaceBaseLifespan(actor) + ResolveJinDanLifespanBonus(actor);
		return XjFaBaoBonusService.GetEffectiveLifespan(actor, finiteBaseline);
	}

	private static bool NeedsRefresh(Actor actor)
	{
		if (actor?.data == null || actor.stats == null)
		{
			return false;
		}

		float expectedBonus = GetExpectedRealmBonus(actor);
		if (expectedBonus <= 0f)
		{
			return false;
		}

		float current = XjSafeCore.GetStatSafe(actor, "lifespan", 0f);
		float minimumExpected = GetRaceBaseLifespan(actor) + expectedBonus;
		if (XjRealmSuppression.GetRealmTier(actor) == XjRealmSuppression.TierJinDan
			&& current > minimumExpected + 1000f)
		{
			return true;
		}
		return current + 0.01f < minimumExpected;
	}

	private static float GetRaceBaseLifespan(Actor actor)
	{
		try
		{
			if (actor?.asset != null)
			{
				float value = actor.asset.base_stats["lifespan"];
				if (value > 0f)
				{
					return value;
				}
			}
		}
		catch
		{
		}

		return 0f;
	}

	private static float GetExpectedRealmBonus(Actor actor, int tier)
	{
		if (tier == XjRealmSuppression.TierJinDan)
		{
			return ResolveJinDanLifespanBonus(actor);
		}
		return GetBaseRealmBonus(tier);
	}

	private static float ResolveJinDanLifespanBonus(Actor actor)
	{
		if (actor?.data == null)
		{
			return JinDanBaseBonus;
		}

		int yiXiang = 0;
		try { XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out yiXiang); } catch { }
		try { yiXiang = XjFaBaoBonusService.GetEffectiveJinDanYiXiang(actor, yiXiang); } catch { }

		if (yiXiang >= 6000) return 6000f;
		if (yiXiang >= 3000) return 5000f;
		if (yiXiang >= 1000) return 4000f;
		return JinDanBaseBonus;
	}

	private static float GetBaseRealmBonus(int tier)
	{
		return tier switch
		{
			XjRealmSuppression.TierTaiXi => TaiXiBaseBonus,
			XjRealmSuppression.TierLianQi => LianQiBaseBonus,
			XjRealmSuppression.TierZhuJi => ZhuJiBaseBonus,
			XjRealmSuppression.TierZiFu => ZiFuBaseBonus,
			XjRealmSuppression.TierJinDan => JinDanBaseBonus,
			_ => 0f
		};
	}

	private static string ResolveRealmTraitId(int tier)
	{
		return tier switch
		{
			XjRealmSuppression.TierTaiXi => XjRealmIds.TaiXi,
			XjRealmSuppression.TierLianQi => XjRealmIds.LianQi,
			XjRealmSuppression.TierZhuJi => XjRealmIds.ZhuJi,
			XjRealmSuppression.TierZiFu => XjRealmIds.ZiFu,
			XjRealmSuppression.TierJinDan => XjRealmIds.JinDan,
			_ => string.Empty
		};
	}
}
