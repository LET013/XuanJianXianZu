using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Death;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjDaoTaiSurvivalGuard
{
	private const float EscapeHealthRatio = 0.10f;
	private const float RestoredHealthRatio = 0.45f;
	private const float InvincibleSeconds = 12f;
	private const int MinimumProtectedHealth = 1;

	/// <summary>
	/// 道胎/世尊真正的第一层保护：尘世运行中的任何死亡都不应成立。
	/// TechnicalRemoval 仅用于清档/换世界/明确销毁对象，必须放行。
	/// </summary>
	internal static bool ShouldBlockDirectDeath(Actor actor, XjDeathCause cause)
	{
		if (actor?.data == null || cause == XjDeathCause.TechnicalRemoval
			|| !XjDaoTaiEquivalentExistenceRules.IsProtectedExistence(actor))
		{
			return false;
		}

		RecoverFromBlockedDeath(actor);
		return true;
	}

	/// <summary>
	/// getHit 在进入原生扣血前就把致死伤害截到至少剩 1 点生命。不能等 Postfix
	/// 再回血，因为原生 getHit 可能已经在内部调用 die。
	/// </summary>
	internal static void ClampIncomingDamage(Actor actor, ref float damage)
	{
		if (damage <= 0f || actor?.data == null || !XjDaoTaiEquivalentExistenceRules.IsProtectedExistence(actor)
			|| XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.TechnicalRemoval)) return;
		float health = Math.Max(0f, XjSafeCore.GetHealthSafe(actor, actor.data.health));
		float maximumDamage = Math.Max(0f, health - MinimumProtectedHealth);
		if (damage > maximumDamage) damage = maximumDamage;
	}

	internal static int ResolveProtectedHealthDelta(Actor actor, int delta)
	{
		if (delta >= 0 || actor?.data == null || !XjDaoTaiEquivalentExistenceRules.IsProtectedExistence(actor)
			|| XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.TechnicalRemoval)) return delta;
		int health = Math.Max(0, actor.data.health);
		int minimumDelta = MinimumProtectedHealth - health;
		return Math.Max(delta, minimumDelta);
	}

	internal static int ResolveProtectedSetHealth(Actor actor, int requestedHealth)
	{
		if (actor?.data == null || !XjDaoTaiEquivalentExistenceRules.IsProtectedExistence(actor)
			|| XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.TechnicalRemoval)) return requestedHealth;
		return Math.Max(MinimumProtectedHealth, requestedHealth);
	}

	internal static bool ShouldBlockDirectDestroy(Actor actor)
	{
		return actor?.data != null
			&& XjDaoTaiEquivalentExistenceRules.IsProtectedExistence(actor)
			&& !XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.TechnicalRemoval);
	}

	internal static void RecoverFromBlockedDeath(Actor actor)
	{
		if (actor?.data == null) return;
		float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, 0f);
		int current = Math.Max(MinimumProtectedHealth, actor.data.health);
		int restored = maxHealth > 0f
			? Mathf.CeilToInt(Mathf.Max(MinimumProtectedHealth, maxHealth * RestoredHealthRatio))
			: current;
		try { actor.setHealth(Math.Max(current, restored)); }
		catch { actor.data.health = Math.Max(current, restored); }
		XjActorAggroBridge.ClearTargets(actor);
		try { ((BaseSimObject)actor).addStatusEffect("invincible", InvincibleSeconds, true); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("DaoTaiSurvival.AddInvincible", ex); }
		try { actor.setStatsDirty(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("DaoTaiSurvival.SetStatsDirty", ex); }
	}

	internal static bool TryHandlePostDamage(Actor actor, BaseSimObject attackerObject)
	{
		if (!XjSafeCore.IsAliveActor(actor) || !XjDaoTaiEquivalentExistenceRules.IsProtectedExistence(actor))
		{
			return false;
		}

		Actor attacker = XjProjectileGuard.ResolveAttackSourceActor(attackerObject);
		// 0.9.9重做中，道胎/世尊常规死亡已在更前层绝对截断；这里的“濒死遁走”只负责
		// 让战斗表现从 1 点生命恢复到可继续运行的稳定状态；攻击者是否同为道胎
		// 不再改变“普通死亡不能成立”这一生存不变量。

		float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, 0f);
		float health = XjSafeCore.GetHealthSafe(actor, 0f);
		if (maxHealth <= 0f || health <= 0f || health > maxHealth * EscapeHealthRatio)
		{
			return false;
		}

		WorldTile escapeTile = ResolveEscapeTile(actor);
		if (escapeTile == null)
		{
			return false;
		}

		try { actor.cancelAllBeh(); } catch (Exception ex) { XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDaoTaiSurvivalGuard.cs:34", ex); }
		XjActorAggroBridge.ClearTargets(actor);
		if (XjSafeCore.IsAliveActor(attacker))
		{
			XjActorAggroBridge.ClearTargets(attacker);
		}

		try
		{
			if (actor.is_inside_building) actor.exitBuilding();
			if (actor.is_inside_boat)
			{
				if (actor.inside_boat != null) actor.inside_boat.removePassenger(actor);
				actor.exitBoat();
			}
		}
		catch (Exception ex) { XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDaoTaiSurvivalGuard.cs:51", ex); }

		try
		{
			// 濒死遁走与金丹/真君、旃檀林共用同一套高境瞬移语义。
			// 目标必须是安全陆地，不再 spawnOn 任意海面/岩浆格。
			if (!XjHighRealmMovement.TryImmediateTeleportSafe(actor, escapeTile)) return false;
			actor.setHealth(Mathf.CeilToInt(Mathf.Max(1f, maxHealth * RestoredHealthRatio)));
			((BaseSimObject)actor).addStatusEffect("invincible", InvincibleSeconds, true);
			actor.stopMovement();
			actor.setStatsDirty();
			return true;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDaoTaiSurvivalGuard.cs:65", ex);
			return false;
		}
	}

	private static WorldTile ResolveEscapeTile(Actor actor)
	{
		WorldTile origin;
		try { origin = ((BaseSimObject)actor).current_tile; }
		catch { origin = null; }
		if (origin == null || World.world == null)
		{
			return origin;
		}

		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		int year = Math.Max(0, World.world?.map_stats?.year ?? XjYearTracker.CurrentYear);
		int width = Math.Max(1, MapBox.width);
		int height = Math.Max(1, MapBox.height);
		for (int i = 0; i < 32; i++)
		{
			int radius = 16 + i * 3;
			int span = radius * 2 + 1;
			int dx = XjDeterministicHash.PositiveIndex(actorId + year + i, "daotai_escape_x", span) - radius;
			int dy = XjDeterministicHash.PositiveIndex(actorId + year + i, "daotai_escape_y", span) - radius;
			if (Math.Abs(dx) + Math.Abs(dy) < radius / 2)
			{
				dx = dx < 0 ? dx - radius / 2 : dx + radius / 2;
			}

			int x = Mathf.Clamp(origin.pos.x + dx, 0, width - 1);
			int y = Mathf.Clamp(origin.pos.y + dy, 0, height - 1);
			WorldTile tile = World.world.GetTileSimple(x, y);
			if (XjHighRealmMovement.IsSafeTeleportDestination(tile))
			{
				return tile;
			}
		}

		return XjHighRealmMovement.IsSafeTeleportDestination(origin) ? origin : null;
	}
}
