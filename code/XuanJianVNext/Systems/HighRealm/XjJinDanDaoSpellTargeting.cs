using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;
using UnityEngine;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjJinDanDaoSpellTargetContext
{
	internal XjJinDanDaoSpellTargetContext(WorldTile centerTile, IReadOnlyList<Actor> targets)
	{
		CenterTile = centerTile;
		Targets = targets ?? Array.Empty<Actor>();
	}

	internal WorldTile CenterTile { get; }
	internal IReadOnlyList<Actor> Targets { get; }
}

internal static class XjJinDanDaoSpellTargeting
{
	private const int MaxLocalImpactCandidates = 64;

	internal static bool TryBuildContext(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		out XjJinDanDaoSpellTargetContext context)
	{
		return TryBuildContext(caster, null, null, definition, out context);
	}

	internal static bool TryBuildContext(
		Actor caster,
		Actor explicitTarget,
		WorldTile explicitTargetTile,
		in XjJinDanDaoSpellDefinition definition,
		out XjJinDanDaoSpellTargetContext context)
	{
		context = default;
		if (caster?.data == null)
		{
			return false;
		}

		Actor directTarget = IsValidDirectTarget(caster, explicitTarget)
			? explicitTarget
			: TryGetDirectTarget(caster);
		bool selfCast = string.Equals(definition.TargetMode, "Self", StringComparison.Ordinal);

		// Event-driven casts may target buildings. In that case the exact attacked
		// tile is still authoritative and nearby hostile actors can be affected.
		// Polling casts, however, never search globally without combat evidence.
		if (directTarget == null && explicitTargetTile == null)
		{
			return false;
		}

		WorldTile casterTile = TryGetCurrentTile(caster);
		bool centeredOnCaster = selfCast
			|| string.Equals(definition.Id, "YingYueLianHua", StringComparison.Ordinal);
		WorldTile centerTile = centeredOnCaster ? casterTile : explicitTargetTile ?? casterTile;
		if (!centeredOnCaster && directTarget != null)
		{
			WorldTile targetTile = TryGetCurrentTile(directTarget);
			if (targetTile != null)
			{
				centerTile = targetTile;
			}
		}

		if (centerTile == null)
		{
			return false;
		}

		List<Actor> targets = CollectTargets(caster, directTarget, centerTile, definition);
		context = new XjJinDanDaoSpellTargetContext(centerTile, targets);
		bool canResolveTileOnlyAreaCast = explicitTargetTile != null
			&& string.Equals(definition.TargetMode, "Area", StringComparison.Ordinal);
		return targets.Count > 0
			|| definition.HealMaxHealthRatio > 0f
			|| selfCast
			|| canResolveTileOnlyAreaCast;
	}

	internal static IReadOnlyList<Actor> CollectLocalImpactTargets(
		Actor caster,
		Actor primaryTarget,
		WorldTile centerTile,
		int radius)
	{
		if (caster?.data == null || centerTile == null || radius <= 0)
		{
			return Array.Empty<Actor>();
		}

		IEnumerable<Actor> source;
		try
		{
			source = Finder.getUnitsFromChunk(centerTile, radius, 0f, false);
		}
		catch
		{
			return Array.Empty<Actor>();
		}

		List<Actor> targets = new List<Actor>(8);
		HashSet<long> seenActorIds = new HashSet<long>();
		AddPrimaryTargetIfValid(caster, primaryTarget, centerTile, radius, targets, seenActorIds);
		if (source == null)
		{
			return targets;
		}

		Vector2Int centerPos = centerTile.pos;
		int radiusSquared = radius * radius;
		int evaluated = 0;
		try
		{
			foreach (Actor candidate in source)
			{
				if (evaluated++ >= MaxLocalImpactCandidates)
				{
					break;
				}

				if (!IsValidAreaTarget(caster, candidate))
				{
					continue;
				}

				WorldTile candidateTile = TryGetCurrentTile(candidate);
				if (candidateTile == null)
				{
					continue;
				}

				Vector2Int candidatePos = candidateTile.pos;
				int dx = candidatePos.x - centerPos.x;
				int dy = candidatePos.y - centerPos.y;
				if (dx * dx + dy * dy > radiusSquared)
				{
					continue;
				}

				long actorId = GetActorId(candidate);
				if (actorId > 0L && !seenActorIds.Add(actorId))
				{
					continue;
				}

				targets.Add(candidate);
			}
		}
		catch
		{
			return targets;
		}

		return targets;
	}

	private static List<Actor> CollectTargets(
		Actor caster,
		Actor directTarget,
		WorldTile centerTile,
		in XjJinDanDaoSpellDefinition definition)
	{
		if (string.Equals(definition.TargetMode, "Self", StringComparison.Ordinal))
		{
			return new List<Actor>();
		}

		if (string.Equals(definition.Id, "JiuXiaoShenLei", StringComparison.Ordinal))
		{
			return CollectJiuXiaoTarget(caster, directTarget, centerTile, definition.Radius);
		}

		int maxTargets = definition.MaxTargets > 0 ? definition.MaxTargets : int.MaxValue;
		IReadOnlyList<Actor> localTargets = CollectLocalImpactTargets(
			caster,
			directTarget,
			centerTile,
			definition.Radius);
		List<Actor> targets = new List<Actor>(Math.Min(maxTargets, localTargets.Count));
		for (int i = 0; i < localTargets.Count && targets.Count < maxTargets; i++)
		{
			targets.Add(localTargets[i]);
		}

		return targets;
	}

	private static List<Actor> CollectJiuXiaoTarget(Actor caster, Actor directTarget, WorldTile centerTile, int radius)
	{
		IReadOnlyList<Actor> localTargets = CollectLocalImpactTargets(caster, directTarget, centerTile, radius);
		if (localTargets.Count == 0)
		{
			return new List<Actor>();
		}

		if (directTarget?.data != null && ReferenceEquals(localTargets[0], directTarget))
		{
			return new List<Actor> { directTarget };
		}

		Actor nearest = null;
		int nearestDistanceSquared = int.MaxValue;
		Vector2Int centerPos = centerTile.pos;
		for (int i = 0; i < localTargets.Count; i++)
		{
			Actor candidate = localTargets[i];
			WorldTile candidateTile = TryGetCurrentTile(candidate);
			if (candidateTile == null)
			{
				continue;
			}

			Vector2Int candidatePos = candidateTile.pos;
			int dx = candidatePos.x - centerPos.x;
			int dy = candidatePos.y - centerPos.y;
			int distanceSquared = dx * dx + dy * dy;
			if (distanceSquared < nearestDistanceSquared)
			{
				nearest = candidate;
				nearestDistanceSquared = distanceSquared;
			}
		}

		return nearest == null ? new List<Actor>() : new List<Actor> { nearest };
	}

	private static WorldTile TryGetCurrentTile(Actor actor)
	{
		try
		{
			return ((BaseSimObject)actor).current_tile;
		}
		catch
		{
			return null;
		}
	}

	private static Actor TryGetDirectTarget(Actor caster)
	{
		if (caster == null)
		{
			return null;
		}

		try
		{
			Actor target = caster.attack_target as Actor;
			if (IsValidDirectTarget(caster, target))
			{
				return target;
			}
		}
		catch
		{
		}

		try
		{
			Actor retaliatoryTarget = caster.attackedBy as Actor;
			return IsValidDirectTarget(caster, retaliatoryTarget) ? retaliatoryTarget : null;
		}
		catch
		{
			return null;
		}
	}

	private static bool IsValidDirectTarget(Actor caster, Actor target)
	{
		if (caster?.data == null || target?.data == null || ReferenceEquals(caster, target))
		{
			return false;
		}

		try
		{
			return ((BaseSimObject)target).current_tile != null && ((NanoObject)target).isAlive();
		}
		catch
		{
			return false;
		}
	}

	private static bool IsValidAreaTarget(Actor caster, Actor target)
	{
		if (!IsValidDirectTarget(caster, target))
		{
			return false;
		}

		try
		{
			if (caster.city != null && target.city != null && caster.city == target.city)
			{
				return false;
			}

			if (caster.clan != null && target.clan != null && caster.clan == target.clan)
			{
				return false;
			}

			Kingdom casterKingdom = ((BaseSimObject)caster).kingdom;
			Kingdom targetKingdom = ((BaseSimObject)target).kingdom;
			return casterKingdom == null || targetKingdom == null || casterKingdom != targetKingdom;
		}
		catch
		{
			return false;
		}
	}

	private static void AddPrimaryTargetIfValid(
		Actor caster,
		Actor target,
		WorldTile centerTile,
		int radius,
		List<Actor> targets,
		HashSet<long> seenActorIds)
	{
		if (!IsValidDirectTarget(caster, target))
		{
			return;
		}

		if (radius > 0 && centerTile != null)
		{
			WorldTile targetTile = TryGetCurrentTile(target);
			if (targetTile == null)
			{
				return;
			}

			Vector2Int centerPos = centerTile.pos;
			Vector2Int targetPos = targetTile.pos;
			int dx = targetPos.x - centerPos.x;
			int dy = targetPos.y - centerPos.y;
			if (dx * dx + dy * dy > radius * radius)
			{
				return;
			}
		}

		long actorId = GetActorId(target);
		if (actorId > 0L && !seenActorIds.Add(actorId))
		{
			return;
		}

		targets.Add(target);
	}

	private static long GetActorId(Actor actor)
	{
		try
		{
			return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		}
		catch
		{
			return 0L;
		}
	}
}
