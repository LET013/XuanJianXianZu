using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Runtime;
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
	private static readonly XjLocalActorQuery.ContextActorPredicate AreaTargetPredicate = IsValidAreaTargetForQuery;

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

		IReadOnlyList<Actor> targets = CollectTargets(caster, directTarget, centerTile, definition);
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
		int radius,
		int maxResults = 0)
	{
		if (caster?.data == null || centerTile == null || radius <= 0) return Array.Empty<Actor>();
		// 0.9.8.28：普通局部技能仍保持64候选；金丹/道胎扩大到128/192，
		// 用于承载0.21式大范围战场压迫。底层 XjLocalActorQuery 原始枚举仍硬封顶512，
		// 因而不会退化成一次施法遍历全世界角色。
		int candidateCap = XjHighRealmPressureWaveSystem.ResolveLocalCandidateCap(caster);
		int resultCap = maxResults > 0 ? Math.Min(candidateCap, maxResults) : candidateCap;
		return XjLocalActorQuery.Collect(
			centerTile,
			radius,
			candidateCap,
			resultCap,
			caster,
			AreaTargetPredicate,
			primaryTarget);
	}

	private static IReadOnlyList<Actor> CollectTargets(
		Actor caster,
		Actor directTarget,
		WorldTile centerTile,
		in XjJinDanDaoSpellDefinition definition)
	{
		if (string.Equals(definition.TargetMode, "Self", StringComparison.Ordinal))
		{
			return Array.Empty<Actor>();
		}

		int radius = XjDaoTaiSpellScale.ResolveTargetRadius(caster, definition);
		if (string.Equals(definition.Id, "JiuXiaoShenLei", StringComparison.Ordinal))
		{
			// Primary target is inserted first by XjLocalActorQuery; tile-only casts are
			// distance sorted. Asking for one result therefore preserves the previous
			// direct-target/nearest-target semantics without allocating a second list.
			return CollectLocalImpactTargets(caster, directTarget, centerTile, radius, 1);
		}

		int maxTargets = definition.MaxTargets > 0 ? definition.MaxTargets : int.MaxValue;
		return CollectLocalImpactTargets(caster, directTarget, centerTile, radius, maxTargets);
	}


	private static WorldTile TryGetCurrentTile(Actor actor)
	{
		try
		{
			return ((BaseSimObject)actor).current_tile;
		}
		catch (System.Exception xjCaught179_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanDaoSpellTargeting.cs:179", xjCaught179_1);
			
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
		catch (System.Exception xjCaught200) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanDaoSpellTargeting.cs:200", xjCaught200); }

		try
		{
			Actor retaliatoryTarget = caster.attackedBy as Actor;
			return IsValidDirectTarget(caster, retaliatoryTarget) ? retaliatoryTarget : null;
		}
		catch (System.Exception xjCaught207_3)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanDaoSpellTargeting.cs:207", xjCaught207_3);
			
			return null;
		}
	}

	private static bool IsValidDirectTarget(Actor caster, Actor target)
	{
		if (caster?.data == null || target?.data == null || ReferenceEquals(caster, target)
			|| XjTaiYinNonAggressionPolicy.ShouldBlockIncomingAttack(caster, target))
		{
			return false;
		}

		try
		{
			return ((BaseSimObject)target).current_tile != null && ((NanoObject)target).isAlive();
		}
		catch (System.Exception xjCaught224_4)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanDaoSpellTargeting.cs:224", xjCaught224_4);
			
			return false;
		}
	}

	private static bool IsValidAreaTargetForQuery(Actor caster, Actor target)
	{
		return IsValidAreaTarget(caster, target);
	}

	internal static bool IsValidAreaTarget(Actor caster, Actor target)
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
		catch (System.Exception xjCaught253_5)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanDaoSpellTargeting.cs:253", xjCaught253_5);
			
			return false;
		}
	}


	private static long GetActorId(Actor actor)
	{
		try
		{
			return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		}
		catch (System.Exception xjCaught305_6)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanDaoSpellTargeting.cs:305", xjCaught305_6);
			
			return 0L;
		}
	}
}
