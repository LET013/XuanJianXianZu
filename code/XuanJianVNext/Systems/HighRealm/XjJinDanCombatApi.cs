using XuanJianVNext.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjJinDanCombatApi
{
	internal static bool HasPendingTerrainEffects => PendingTerrainTasks.Count > 0;
	private const int MaxTerrainTilesPerTask = 512;
	private const int MaxPendingTerrainTasks = 96;
	private const int TerrainTaskQuantum = 8;

	private static readonly Queue<TerrainEffectTask> PendingTerrainTasks = new Queue<TerrainEffectTask>();

	private sealed class TerrainEffectTask
	{
		private readonly Vector2Int _center;
		private readonly int _radius;
		private readonly int _radiusSquared;
		private readonly int _width;
		private readonly int _totalCells;
		private readonly int _samplingStride;
		private readonly string _effectType;
		private readonly float _durationSeconds;
		private readonly Action _completionAction;
		private int _nextLinearIndex;
		private int _appliedTiles;

		internal TerrainEffectTask(
			WorldTile centerTile,
			int radius,
			string effectType,
			float durationSeconds,
			Action completionAction)
		{
			_center = centerTile.pos;
			_radius = Mathf.Max(1, radius);
			_radiusSquared = _radius * _radius;
			_width = _radius * 2 + 1;
			_totalCells = _width * _width;
			int estimatedTileCount = Mathf.Max(1, Mathf.RoundToInt(Mathf.PI * _radiusSquared));
			_samplingStride = Mathf.Max(1, Mathf.CeilToInt((float)estimatedTileCount / MaxTerrainTilesPerTask));
			_effectType = effectType;
			_durationSeconds = durationSeconds;
			_completionAction = completionAction;
		}

		internal bool Advance(MapBox world, int budget, out int consumed)
		{
			consumed = 0;
			while (consumed < budget
				&& _nextLinearIndex < _totalCells
				&& _appliedTiles < MaxTerrainTilesPerTask)
			{
				int index = _nextLinearIndex;
				_nextLinearIndex += _samplingStride;
				consumed++;

				int dx = index % _width - _radius;
				int dy = index / _width - _radius;
				if (dx * dx + dy * dy > _radiusSquared)
				{
					continue;
				}

				WorldTile tile = TryGetTile(world, _center.x + dx, _center.y + dy);
				if (tile != null && TryApplyTerrainEffectToTile(tile, _effectType, _durationSeconds))
				{
					_appliedTiles++;
				}
			}

			return _nextLinearIndex >= _totalCells || _appliedTiles >= MaxTerrainTilesPerTask;
		}

		internal void Complete()
		{
			try
			{
				_completionAction?.Invoke();
			}
			catch
			{
			}
		}
	}

	internal static void TickTerrainEffects(int tileBudget)
	{
		if (tileBudget <= 0 || PendingTerrainTasks.Count == 0)
		{
			return;
		}

		MapBox world = World.world;
		if ((UnityEngine.Object)(object)world == (UnityEngine.Object)null)
		{
			return;
		}

		int remaining = tileBudget;
		int tasksThisPass = PendingTerrainTasks.Count;
		for (int i = 0; i < tasksThisPass && remaining > 0 && PendingTerrainTasks.Count > 0; i++)
		{
			TerrainEffectTask task = PendingTerrainTasks.Dequeue();
			int quantum = Mathf.Min(TerrainTaskQuantum, remaining);
			bool completed = task.Advance(world, quantum, out int consumed);
			remaining -= consumed;
			if (completed)
			{
				task.Complete();
			}
			else
			{
				PendingTerrainTasks.Enqueue(task);
			}
		}
	}

	internal static void ClearTerrainEffects()
	{
		PendingTerrainTasks.Clear();
	}

	internal static bool TryDamageActor(Actor caster, Actor target, float damage, string spellId, out string reason)
	{
		reason = string.Empty;
		if (caster?.data == null || target?.data == null || damage <= 0f)
		{
			reason = "invalid_damage_args";
			return false;
		}

		if (!IsAlive(target))
		{
			reason = "target_not_alive";
			return false;
		}

		BaseSimObject targetObject = target;
		BaseSimObject casterObject = caster;

		try
		{
			targetObject.getHit(damage, true, (AttackType)12, casterObject, false, false, false);
			return true;
		}
		catch (Exception ex)
		{
			reason = "damage_api_failed:" + ex.GetType().Name;
			return false;
		}
	}

	internal static bool TryHealActor(Actor actor, float amount, string spellId, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || amount <= 0f || !IsAlive(actor))
		{
			reason = "invalid_heal_args";
			return false;
		}

		try
		{
			int safeAmount = Mathf.Max(1, Mathf.CeilToInt(amount));
			actor.restoreHealth(safeAmount);
			try
			{
				actor.spawnParticle(Toolbox.color_heal);
			}
			catch
			{
			}

			return true;
		}
		catch (Exception ex)
		{
			reason = "heal_api_failed:" + ex.GetType().Name;
			return false;
		}
	}

	internal static bool TryApplyStatus(Actor actor, string statusId, float durationSeconds, string spellId, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || string.IsNullOrWhiteSpace(statusId) || durationSeconds <= 0f || !IsAlive(actor))
		{
			reason = "invalid_status_args";
			return false;
		}

		try
		{
			bool added = ((BaseSimObject)actor).addStatusEffect(statusId, durationSeconds, true);
			if (added)
			{
				return true;
			}

			return TryApplyControlHold(actor, statusId, durationSeconds);
		}
		catch (Exception ex)
		{
			reason = "status_api_failed:" + ex.GetType().Name;
			return TryApplyControlHold(actor, statusId, durationSeconds);
		}
	}

	internal static bool TryApplyTerrainEffect(WorldTile centerTile, string effectType, int radius, float durationSeconds, string spellId, out string reason)
	{
		return TryEnqueueTerrainEffect(centerTile, effectType, radius, durationSeconds, null, out reason);
	}

	internal static bool TryApplyTerrainEffectAtTile(
		WorldTile tile,
		string effectType,
		float durationSeconds,
		string spellId,
		out string reason)
	{
		reason = string.Empty;
		if (tile == null || string.IsNullOrWhiteSpace(effectType))
		{
			reason = "invalid_single_tile_terrain_args";
			return false;
		}

		bool applied = TryApplyTerrainEffectToTile(tile, effectType, durationSeconds);
		if (!applied)
		{
			reason = "single_tile_terrain_not_applied";
		}

		return applied;
	}

	internal static bool TryApplyStagedFrozenTerrain(
		WorldTile centerTile,
		int radius,
		float durationSeconds,
		Action completionAction,
		string spellId,
		out string reason)
	{
		reason = string.Empty;
		if (centerTile == null || radius <= 0 || durationSeconds <= 0f)
		{
			reason = "invalid_staged_frozen_args";
			return false;
		}

		return TryEnqueueTerrainEffect(
			centerTile,
			"FrozenTerrain",
			radius,
			durationSeconds,
			completionAction,
			out reason);
	}

	private static bool TryEnqueueTerrainEffect(
		WorldTile centerTile,
		string effectType,
		int radius,
		float durationSeconds,
		Action completionAction,
		out string reason)
	{
		reason = string.Empty;
		if (centerTile == null || radius <= 0 || string.IsNullOrWhiteSpace(effectType))
		{
			reason = "invalid_terrain_args";
			return false;
		}

		if ((UnityEngine.Object)(object)World.world == (UnityEngine.Object)null)
		{
			reason = "world_not_ready";
			return false;
		}

		if (PendingTerrainTasks.Count >= MaxPendingTerrainTasks)
		{
			reason = "terrain_queue_full";
			return false;
		}

		PendingTerrainTasks.Enqueue(new TerrainEffectTask(
			centerTile,
			radius,
			effectType,
			durationSeconds,
			completionAction));
		return true;
	}

	internal static bool TryScheduleImpact(float delaySeconds, Action impactAction, string spellId, out string reason)
	{
		reason = string.Empty;
		if (impactAction == null || delaySeconds < 0f)
		{
			reason = "invalid_scheduled_impact_args";
			return false;
		}

		MapBox world = World.world;
		if ((UnityEngine.Object)(object)world == (UnityEngine.Object)null)
		{
			reason = "world_not_ready";
			return false;
		}

		try
		{
			((MonoBehaviour)world).StartCoroutine(RunScheduledImpact(delaySeconds, impactAction));
			return true;
		}
		catch (Exception ex)
		{
			reason = "scheduled_impact_failed:" + ex.GetType().Name;
			return false;
		}
	}

	internal static WorldTile ResolveImpactTile(Actor trackedTarget, WorldTile fallbackTile)
	{
		try
		{
			if (IsAlive(trackedTarget))
			{
				WorldTile targetTile = ((BaseSimObject)trackedTarget).current_tile;
				if (targetTile != null)
				{
					return targetTile;
				}
			}
		}
		catch
		{
		}

		return fallbackTile;
	}

	internal static bool TryPlayAnimation(
		WorldTile tile,
		string animationId,
		float scale,
		float frameIntervalSeconds,
		string spellId,
		out string reason)
	{
		reason = string.Empty;
		if (tile == null || string.IsNullOrWhiteSpace(animationId))
		{
			reason = "invalid_animation_args";
			return false;
		}

		try
		{
			Vector2Int pos = tile.pos;
			bool spawned = XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
				animationId.Trim(),
				pos.x + 0.5f,
				pos.y + 0.5f,
				scale <= 0f ? 1f : scale,
				frameIntervalSeconds);
			if (!spawned)
				reason = "animation_spawn_failed";
			return spawned;
		}
		catch (Exception ex)
		{
			reason = "animation_api_failed:" + ex.GetType().Name;
			return false;
		}
	}

	internal static bool TryPlayPrimarySpellAnimation(
		in XjJinDanDaoSpellTargetContext context,
		in XjJinDanDaoSpellDefinition definition,
		out string reason)
	{
		reason = string.Empty;
		WorldTile tile = context.CenterTile;
		if (tile == null || string.IsNullOrWhiteSpace(definition.AnimationPath))
		{
			reason = "invalid_primary_animation_args";
			return false;
		}

		if (string.Equals(definition.Id, "XuanQiFengZhen", StringComparison.Ordinal))
		{
			Vector2Int pos = tile.pos;
			bool spawned = XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
				definition.AnimationPath,
				pos.x + 0.5f,
				pos.y + 9f,
				definition.AnimationScale,
				definition.AnimationFrameIntervalSeconds,
				2);
			if (!spawned)
			{
				reason = "wind_formation_animation_spawn_failed";
			}
			return spawned;
		}

		if (string.Equals(definition.Id, "DaRiZhuiYuan", StringComparison.Ordinal))
		{
			return TryPlayFallingAnimation(
				tile,
				TryGetFirstTarget(context.Targets),
				definition.AnimationPath,
				5.6f,
				0.36f,
				1.04f,
				definition.AnimationFrameIntervalSeconds,
				Mathf.Max(0.24f, definition.AnimationLifetimeSeconds),
				0.14f,
				out reason);
		}

		if (string.Equals(definition.Id, "ZhenYueBeng", StringComparison.Ordinal))
		{
			return TryPlayFallingAnimation(
				tile,
				TryGetFirstTarget(context.Targets),
				definition.AnimationPath,
				5.6f,
				ResolveZhenYueBengImpactScale() * 0.2f * 0.88f,
				ResolveZhenYueBengImpactScale() * 0.2f,
				definition.AnimationFrameIntervalSeconds,
				Mathf.Max(0.24f, definition.SmashDurationSeconds),
				0.03f,
				out reason);
		}

		return TryPlayAnimation(
			tile,
			definition.AnimationPath,
			definition.AnimationScale,
			definition.AnimationFrameIntervalSeconds,
			definition.Id,
			out reason);
	}

	internal static bool TryPlaySupplementalVisuals(
		WorldTile centerTile,
		IReadOnlyList<Actor> targets,
		in XjJinDanDaoSpellDefinition definition,
		out string reason)
	{
		reason = string.Empty;
		if (centerTile == null || string.IsNullOrWhiteSpace(definition.Id))
		{
			reason = "invalid_supplemental_visual_args";
			return false;
		}

		try
		{
			if (string.Equals(definition.Id, "DaRiZhuiYuan", StringComparison.Ordinal))
			{
				return TryPlayDaRiFireRings(centerTile, definition.TerrainRadius, out reason);
			}

			if (string.Equals(definition.Id, "YingYueLianHua", StringComparison.Ordinal))
			{
				return false;
			}

			if (string.Equals(definition.Id, "QingTengFu", StringComparison.Ordinal))
			{
				return TryPlayQingTengActorOverlays(targets, definition, out reason);
			}

			if (string.Equals(definition.Id, "BinFengTuCi", StringComparison.Ordinal))
			{
				return TryPlayBinFengActorOverlays(targets, definition, out reason);
			}

			if (string.Equals(definition.Id, "JinDeJianZhen", StringComparison.Ordinal))
			{
				return TryPlayJinDeJianZhenTargetSwords(targets, definition, out reason);
			}
		}
		catch (Exception ex)
		{
			reason = "supplemental_visual_failed:" + ex.GetType().Name;
		}

		return false;
	}

	internal static bool TryApplyImpactHold(Actor actor, float durationSeconds, string spellId, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || durationSeconds <= 0f || !IsAlive(actor))
		{
			reason = "invalid_impact_hold_args";
			return false;
		}

		try
		{
			actor.stopMovement();
			actor.makeWait(Mathf.Max(0.15f, durationSeconds));
			return true;
		}
		catch (Exception ex)
		{
			reason = "impact_hold_failed:" + ex.GetType().Name;
			return false;
		}
	}

	internal static float GetBaseDamage(Actor caster)
	{
		try
		{
			BaseSimObject sim = caster;
			if (sim?.stats != null)
			{
				return Math.Max(1f, sim.stats["damage"]);
			}
		}
		catch
		{
		}

		return 1f;
	}

	internal static float GetMaxHealth(Actor actor)
	{
		try
		{
			return Math.Max(1f, ((BaseSimObject)actor).getMaxHealth());
		}
		catch
		{
			return 1f;
		}
	}

	private static WorldTile TryGetTile(MapBox world, int x, int y)
	{
		if ((UnityEngine.Object)(object)world == (UnityEngine.Object)null || x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height)
		{
			return null;
		}

		try
		{
			return world.GetTileSimple(x, y);
		}
		catch
		{
			return null;
		}
	}

	private static bool TryPlayFallingAnimation(
		WorldTile tile,
		Actor trackedTarget,
		string animationPath,
		float startHeight,
		float startScale,
		float endScale,
		float frameIntervalSeconds,
		float moveDurationSeconds,
		float holdSeconds,
		out string reason)
	{
		reason = string.Empty;
		if (tile == null || string.IsNullOrWhiteSpace(animationPath))
		{
			reason = "invalid_falling_animation_args";
			return false;
		}

		try
		{
			bool isZhenYue = string.Equals(animationPath, "effects/Skills/ZhenYueBeng/ZhenYueBeng", StringComparison.Ordinal);
			float hoverDuration = isZhenYue ? 0.16f : Mathf.Max(0.01f, moveDurationSeconds);
			float smashDuration = isZhenYue ? Mathf.Max(0.01f, moveDurationSeconds) : 0.24f;
			bool spawned = XjCombatEffectPool.TrySpawnTrackingSpriteAnimationSafe(
				animationPath.Trim(),
				() => ResolveEffectAnchor(trackedTarget, tile),
				startHeight,
				startScale,
				endScale,
				frameIntervalSeconds,
				hoverDuration,
				smashDuration,
				holdSeconds,
				isZhenYue ? 64 : 0);
			if (!spawned)
			{
				reason = "falling_animation_spawn_failed";
			}

			return spawned;
		}
		catch (Exception ex)
		{
			reason = "falling_animation_failed:" + ex.GetType().Name;
			return false;
		}
	}

	private static bool TryPlayDaRiFireRings(WorldTile centerTile, int terrainRadius, out string reason)
	{
		reason = string.Empty;
		bool spawned = false;
		spawned |= TrySpawnNativeEffectAtTile(centerTile, "fx_fire_large", 1.1f);

		int innerRadius = Mathf.Clamp(terrainRadius / 4, 4, 10);
		int outerRadius = Mathf.Clamp(terrainRadius / 2, innerRadius + 2, 18);
		spawned |= TrySpawnNativeRing(centerTile, innerRadius, "fx_fire_small", 0.85f);
		spawned |= TrySpawnNativeRing(centerTile, outerRadius, "fx_fire_small", 0.65f);
		if (!spawned)
		{
			reason = "dari_fire_ring_not_spawned";
		}

		return spawned;
	}

	private static bool TryPlayQingTengActorOverlays(
		IReadOnlyList<Actor> targets,
		in XjJinDanDaoSpellDefinition definition,
		out string reason)
	{
		reason = string.Empty;
		if (targets == null || targets.Count == 0 || string.IsNullOrWhiteSpace(definition.AnimationPath))
		{
			reason = "qingteng_overlay_args_missing";
			return false;
		}

		bool spawned = false;
		int limit = Mathf.Min(targets.Count, definition.MaxTargets > 0 ? definition.MaxTargets : targets.Count);
		for (int i = 0; i < limit; i++)
		{
			Actor target = targets[i];
			if (!IsAlive(target))
			{
				continue;
			}

			spawned |= TryPlayActorOverlay(
				target,
				definition.AnimationPath,
				Mathf.Max(0.1f, definition.AnimationScale),
				definition.AnimationFrameIntervalSeconds,
				1f,
				definition.AnimationLifetimeSeconds,
				definition.StatusDurationSeconds,
				90f);
		}

		if (!spawned)
		{
			reason = "qingteng_overlay_not_spawned";
		}

		return spawned;
	}

	private static bool TryPlayBinFengActorOverlays(
		IReadOnlyList<Actor> targets,
		in XjJinDanDaoSpellDefinition definition,
		out string reason)
	{
		reason = string.Empty;
		if (targets == null || targets.Count == 0 || string.IsNullOrWhiteSpace(definition.AnimationPath))
		{
			reason = "binfeng_overlay_args_missing";
			return false;
		}

		bool spawned = false;
		int limit = Mathf.Min(targets.Count, definition.MaxTargets > 0 ? definition.MaxTargets : targets.Count);
		for (int i = 0; i < limit; i++)
		{
			Actor target = targets[i];
			if (!IsAlive(target))
			{
				continue;
			}

			spawned |= TryPlayActorOverlay(
				target,
				definition.AnimationPath,
				Mathf.Max(0.1f, definition.AnimationScale),
				definition.AnimationFrameIntervalSeconds,
				0.8f,
				definition.AnimationLifetimeSeconds,
				0f,
				0f);
		}

		if (!spawned)
		{
			reason = "binfeng_overlay_not_spawned";
		}

		return spawned;
	}

	private static bool TryPlayJinDeJianZhenTargetSwords(
		IReadOnlyList<Actor> targets,
		in XjJinDanDaoSpellDefinition definition,
		out string reason)
	{
		reason = string.Empty;
		if (targets == null || targets.Count == 0 || string.IsNullOrWhiteSpace(definition.AnimationPath))
		{
			reason = "jinde_jianzhen_args_missing";
			return false;
		}

		bool spawned = false;
		int limit = Mathf.Min(targets.Count, definition.MaxTargets > 0 ? definition.MaxTargets : targets.Count);
		for (int i = 0; i < limit; i++)
		{
			Actor target = targets[i];
			if (!IsAlive(target))
			{
				continue;
			}

			WorldTile tile = ResolveImpactTile(target, null);
			if (tile == null)
			{
				continue;
			}

			spawned |= TryPlayAnimation(
				tile,
				definition.AnimationPath,
				Mathf.Max(0.1f, definition.AnimationScale),
				definition.AnimationFrameIntervalSeconds,
				definition.Id,
				out _);
		}

		if (!spawned)
		{
			reason = "jinde_jianzhen_not_spawned";
		}

		return spawned;
	}

	private static bool TrySpawnNativeRing(WorldTile centerTile, int radius, string effectId, float scale)
	{
		if (centerTile == null || radius <= 0)
		{
			return false;
		}

		MapBox world = World.world;
		if ((UnityEngine.Object)(object)world == (UnityEngine.Object)null)
		{
			return false;
		}

		Vector2Int center = centerTile.pos;
		bool spawned = false;
		const int ringCount = 8;
		for (int i = 0; i < ringCount; i++)
		{
			float angle = Mathf.PI * 2f * i / ringCount;
			int x = center.x + Mathf.RoundToInt(Mathf.Cos(angle) * radius);
			int y = center.y + Mathf.RoundToInt(Mathf.Sin(angle) * radius);
			WorldTile tile = TryGetTile(world, x, y);
			if (tile == null)
			{
				continue;
			}

			spawned |= TrySpawnNativeEffectAtTile(tile, effectId, scale);
		}

		return spawned;
	}

	private static bool TrySpawnNativeEffectAtTile(WorldTile tile, string effectId, float scale)
	{
		if (tile == null || string.IsNullOrWhiteSpace(effectId))
		{
			return false;
		}

		Vector2Int pos = tile.pos;
		return XjCombatEffectPool.TrySpawnEffectSafe(effectId, pos.x + 0.5f, pos.y + 0.5f, scale);
	}

	private static bool TryPlayActorOverlay(
		Actor actor,
		string animationId,
		float scale,
		float frameIntervalSeconds,
		float yOffset,
		float animationDurationSeconds,
		float holdDurationSeconds,
		float rotationZ)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(animationId))
		{
			return false;
		}

		try
		{
			return XjCombatEffectPool.TrySpawnTrackedOverlaySpriteAnimationSafe(
				animationId.Trim(),
				() => ResolveEffectAnchor(actor, null) + Vector2.up * yOffset,
				scale,
				frameIntervalSeconds,
				Mathf.Max(0.01f, animationDurationSeconds),
				Mathf.Max(0f, holdDurationSeconds),
				64,
				rotationZ);
		}
		catch
		{
			return false;
		}
	}

	private static Actor TryGetFirstTarget(IReadOnlyList<Actor> targets)
	{
		if (targets == null)
		{
			return null;
		}

		for (int i = 0; i < targets.Count; i++)
		{
			if (IsAlive(targets[i]))
			{
				return targets[i];
			}
		}

		return null;
	}

	private static Vector2 ResolveEffectAnchor(Actor trackedTarget, WorldTile fallbackTile)
	{
		try
		{
			if (IsAlive(trackedTarget))
			{
				BaseSimObject targetObject = trackedTarget;
				if (targetObject.current_tile != null)
				{
					Vector2Int pos = targetObject.current_tile.pos;
					return new Vector2(pos.x + 0.5f, pos.y + 0.5f);
				}

				Vector2 current = targetObject.current_position;
				return new Vector2(current.x, current.y);
			}

			if (fallbackTile != null)
			{
				Vector2Int pos = fallbackTile.pos;
				return new Vector2(pos.x + 0.5f, pos.y + 0.5f);
			}
		}
		catch
		{
		}

		return Vector2.zero;
	}

	private static float ResolveZhenYueBengImpactScale()
	{
		Sprite[] mountainFrames = XjCombatEffectPool.GetCachedAnimationFrames("effects/Skills/ZhenYueBeng/ZhenYueBeng");
		if (mountainFrames == null || mountainFrames.Length == 0)
		{
			mountainFrames = XjCombatEffectPool.GetCachedSingleSpriteFrame("effects/Skills/ZhenYueBeng/ZhenYueBeng");
		}

		Sprite mountainSprite = mountainFrames != null && mountainFrames.Length > 0
			? mountainFrames[0]
			: null;
		if ((UnityEngine.Object)(object)mountainSprite == (UnityEngine.Object)null)
		{
			return 3.6f;
		}

		float mountainDiameter = Mathf.Max(mountainSprite.bounds.size.x, mountainSprite.bounds.size.y);
		if (mountainDiameter <= 0.01f)
		{
			return 3.6f;
		}

		Sprite[] daRiFrames = XjCombatEffectPool.GetCachedAnimationFrames("effects/Skills/DaRiZhuiYuan");
		Sprite daRiSprite = daRiFrames != null && daRiFrames.Length > 0 ? daRiFrames[0] : null;
		if ((UnityEngine.Object)(object)daRiSprite == (UnityEngine.Object)null)
		{
			return 3.6f;
		}

		float daRiDiameter = Mathf.Max(daRiSprite.bounds.size.x, daRiSprite.bounds.size.y) * (1.04f * 1.08f);
		if (daRiDiameter <= 0.01f)
		{
			return 3.6f;
		}

		return Mathf.Clamp(daRiDiameter / mountainDiameter, 2.8f, 6.5f);
	}

	private static IEnumerator RunScheduledImpact(float delaySeconds, Action impactAction)
	{
		if (delaySeconds > 0f)
		{
			yield return new WaitForSeconds(delaySeconds);
		}

		try
		{
			impactAction?.Invoke();
		}
		catch
		{
		}
	}

	private static bool TryApplyTerrainEffectToTile(WorldTile tile, string effectType, float durationSeconds)
	{
		if (tile == null)
		{
			return false;
		}

		try
		{
			if (string.Equals(effectType, "FrozenTerrain", StringComparison.Ordinal))
			{
				if (!tile.canBeFrozen())
				{
					return false;
				}

				tile.freeze(Mathf.Max(1, Mathf.RoundToInt(durationSeconds)));
				return true;
			}

			TerraformOptions options = new TerraformOptions();
			if (string.Equals(effectType, "CommonLeiFaTerrain", StringComparison.Ordinal))
			{
				options.set_fire = true;
				options.add_burned = true;
				options.add_heat = 25;
				options.lightning_effect = true;
				options.shake = true;
				options.shake_duration = 900f;
				options.shake_intensity = 0.004f;
			}
			else if (string.Equals(effectType, "JiuXiaoTerrain", StringComparison.Ordinal))
			{
				options.set_fire = true;
				options.add_burned = true;
				options.add_heat = 50;
			}
			else if (string.Equals(effectType, "DaRiTerrain", StringComparison.Ordinal))
			{
				options.damage_buildings = true;
				options.destroy_buildings = true;
				options.make_ruins = true;
				options.remove_roads = true;
				options.remove_trees_fully = true;
				options.set_fire = true;
				options.add_burned = true;
				options.add_heat = 110;
			}
			else if (string.Equals(effectType, "FireTerrain", StringComparison.Ordinal))
			{
				options.set_fire = true;
				options.add_burned = true;
				options.add_heat = 120;
			}
			else if (string.Equals(effectType, "SmashTerrain", StringComparison.Ordinal))
			{
				options.damage_buildings = true;
				options.destroy_buildings = true;
				options.make_ruins = true;
				options.remove_roads = true;
				options.remove_trees_fully = true;
			}
			else
			{
				return false;
			}

			options.damage = 0;
			MapAction.decreaseTile(tile, false, options);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryApplyControlHold(Actor actor, string statusId, float durationSeconds)
	{
		if (!string.Equals(statusId, "root", StringComparison.Ordinal)
			&& !string.Equals(statusId, "frozen", StringComparison.Ordinal))
		{
			return false;
		}

		try
		{
			actor.stopMovement();
			actor.makeWait(Mathf.Max(0.15f, durationSeconds));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsAlive(Actor actor)
	{
		try
		{
			return XjSafeCore.IsAliveActor(actor);
		}
		catch
		{
			return actor?.data != null;
		}
	}
}
