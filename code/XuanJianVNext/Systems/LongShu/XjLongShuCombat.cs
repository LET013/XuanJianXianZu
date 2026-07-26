using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.LongShu;

internal static partial class XjLongShuSystem
{
	private const int HostilityTickInterval = 30;
	private const int HostilityActorBudget = 1;
	private const int HostilityMaxDistanceSquared = 72 * 72;
	private const int HostilityCandidateBudget = 48;

	private static int _hostilityTickCounter;
	private static int _hostilityCursor;

	private static void TickHostility(int growthPasses)
	{
		_hostilityTickCounter += Math.Max(1, growthPasses);
		if (_hostilityTickCounter < HostilityTickInterval)
		{
			return;
		}

		// Multiple elapsed cadence intervals only require one current-state aggro
		// refresh. Preserve the remainder but never replay identical searches in
		// a burst after high-speed simulation.
		_hostilityTickCounter %= HostilityTickInterval;
		IReadOnlyList<long> actorIds = KnownLongShuIds;
		if (actorIds.Count == 0)
		{
			return;
		}

		int processed = 0;
		int start = Math.Max(0, _hostilityCursor % Math.Max(1, actorIds.Count));
		for (int step = 0; step < actorIds.Count && processed < HostilityActorBudget; step++)
		{
			int index = (start + step) % actorIds.Count;
			if (!XjScheduler.ResolveActor(actorIds[index], out Actor longShu))
			{
				continue;
			}

			if (!IsAlive(longShu) || !IsLongShu(longShu))
			{
				continue;
			}

			processed++;
			if (TryFindHostilityTarget(longShu, out Actor target))
			{
				ForceAggro(longShu, target);
			}
		}

		_hostilityCursor = (start + Math.Max(1, processed)) % Math.Max(1, actorIds.Count);
	}

	private static bool TryFindHostilityTarget(Actor longShu, out Actor target)
	{
		target = null;
		WorldTile origin = GetCurrentTile(longShu);
		if (origin == null)
		{
			return false;
		}

		IEnumerable<Actor> candidates;
		try
		{
			candidates = Finder.getUnitsFromChunk(origin, 72, 0f, false);
		}
		catch
		{
			return false;
		}
		if (candidates == null)
		{
			return false;
		}

		int bestDistance = int.MaxValue;
		int evaluated = 0;
		foreach (Actor candidate in candidates)
		{
			if (evaluated++ >= HostilityCandidateBudget)
			{
				break;
			}

			if (!IsValidHostilityTarget(longShu, candidate))
			{
				continue;
			}

			WorldTile candidateTile = GetCurrentTile(candidate);
			if (candidateTile == null)
			{
				continue;
			}

			int dx = candidateTile.pos.x - origin.pos.x;
			int dy = candidateTile.pos.y - origin.pos.y;
			int distance = dx * dx + dy * dy;
			if (distance > HostilityMaxDistanceSquared || distance >= bestDistance)
			{
				continue;
			}

			bestDistance = distance;
			target = candidate;
		}

		return target != null;
	}

	private static bool IsValidHostilityTarget(Actor longShu, Actor candidate)
	{
		if (!IsAlive(candidate) || candidate == longShu || IsLongShu(candidate))
		{
			return false;
		}

		if (string.Equals(GetActorAssetId(longShu), GetActorAssetId(candidate), StringComparison.Ordinal))
		{
			return false;
		}

		// 船只本身始终属于龙属主动仇恨目标。
		if (candidate.asset?.is_boat == true)
		{
			return true;
		}

		if (!XjCultivationEligibility.CanCultivate(candidate))
		{
			return false;
		}

		// 水德修士不再要求与龙属同境界；任何境界均会被主动攻击。
		if (XjActorAccessor.TryGetString(candidate, XjActorDataKeys.DaoTu, out string daoTu)
			&& IsShuiDeDaoTu(daoTu))
		{
			return true;
		}

		// 非水德修士只要当前处于海面/深海格，也会成为龙属目标。
		return IsSeaTile(GetCurrentTile(candidate));
	}

	private static bool IsShuiDeDaoTu(string daoTu)
	{
		for (int i = 0; i < ShuiDeDaoTu.Length; i++)
		{
			if (string.Equals(daoTu, ShuiDeDaoTu[i], StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static void ForceAggro(Actor actor, Actor target)
	{
		if (actor?.data == null || target?.data == null)
		{
			return;
		}

		XjActorAggroBridge.ForceAggro(actor, target);
		RefreshActor(actor);
	}
}
