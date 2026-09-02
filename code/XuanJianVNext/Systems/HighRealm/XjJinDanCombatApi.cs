using XuanJianVNext.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjJinDanCombatApi
{
	internal static bool HasPendingTerrainEffects => PendingTerrainTasks.Count > 0;
	private const int MaxTerrainTilesPerTask = 512;
	private const int MaxPendingTerrainTasks = 96;
	private const int TerrainTaskQuantum = 8;

	// 0.9.8.4：首批只接入能和既有玄鉴神通语义一一对应的命中/控制表现。
	// 素材进入玄鉴自己的 XjCombatEffectPool，不引入鬼谷的动画框架。
	private const string YingYueFreezeOverlayPath = "effects/Skills/TaiYinBingFeng";
	private const string IceDetonationVisualPath = "effects/Skills/Shared/IceDetonation";
	private const string IceLanceVisualPath = "effects/Skills/Shared/IceLance";
	private const string ThunderFormationVisualPath = "effects/Skills/Shared/ThunderFormation";
	private const string ThunderHitVisualPath = "effects/Skills/Shared/ThunderHit";
	private const string JinDeSlashVisualPath = "effects/Skills/Shared/JinDeRadiantSlash";

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
		private readonly long _casterId;
		private int _nextLinearIndex;
		private int _appliedTiles;

		internal TerrainEffectTask(
			WorldTile centerTile,
			int radius,
			string effectType,
			float durationSeconds,
			Action completionAction,
			long casterId)
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
			_casterId = Math.Max(0L, casterId);
		}

		internal bool Advance(MapBox world, int budget, out int consumed)
		{
			consumed = 0;
			Actor caster = null;
			if (_casterId > 0L
				&& (!XjScheduler.ResolveActor(_casterId, out caster) || !IsAlive(caster)))
			{
				// 角色法术的后续地形任务必须继续有真实施法者；施法者已经不存在时
				// 直接结束，绝不把余下格子降级为无主世界力量。
				return true;
			}
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
				if (tile != null && TryApplyTerrainEffectToTile(tile, _effectType, _durationSeconds, caster))
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
			catch (System.Exception xjCaught87) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:87", xjCaught87); }
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
			catch (System.Exception xjCaught177) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:177", xjCaught177); }

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
		return TryApplyStatus(null, actor, statusId, durationSeconds, spellId, out reason);
	}

	internal static bool TryApplyStatus(
		Actor caster,
		Actor actor,
		string statusId,
		float durationSeconds,
		string spellId,
		out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || string.IsNullOrWhiteSpace(statusId) || durationSeconds <= 0f || !IsAlive(actor))
		{
			reason = "invalid_status_args";
			return false;
		}

		// root/frozen/stun 等由高境神通发起时，不再走原生“筑基以上免疫眩晕”语义。
		// 统一交给位格控制服务按双方大境界与 CombatLevel 计算持续时间。
		if (XjHighRealmControlService.CanUseAttributedHighRealmControl(caster)
			&& XjHighRealmControlService.IsControlStatus(statusId))
		{
			return XjHighRealmControlService.TryApplyStatus(caster, actor, statusId, durationSeconds, out reason);
		}

		try
		{
			bool added = ((BaseSimObject)actor).addStatusEffect(statusId, durationSeconds, true);
			if (added)
			{
				return true;
			}

			return TryApplyControlHold(null, actor, statusId, durationSeconds);
		}
		catch (Exception ex)
		{
			reason = "status_api_failed:" + ex.GetType().Name;
			return TryApplyControlHold(null, actor, statusId, durationSeconds);
		}
	}

	internal static bool TryApplyTerrainEffect(WorldTile centerTile, string effectType, int radius, float durationSeconds, string spellId, out string reason)
	{
		return TryApplyTerrainEffect(null, centerTile, effectType, radius, durationSeconds, spellId, out reason);
	}

	internal static bool TryApplyTerrainEffect(
		Actor caster,
		WorldTile centerTile,
		string effectType,
		int radius,
		float durationSeconds,
		string spellId,
		out string reason)
	{
		if (caster?.data != null
			&& XjJinDanDaoSpellRuntime.IsJinDanActor(caster)
			&& IsNativeDestructionTerrainEffect(effectType))
		{
			return TryApplyNativeTerrainDestruction(caster, centerTile, radius, out reason);
		}

		return TryEnqueueTerrainEffect(caster, centerTile, effectType, radius, durationSeconds, null, out reason);
	}

	private static bool IsNativeDestructionTerrainEffect(string effectType)
	{
		return string.Equals(effectType, "CommonLeiFaTerrain", StringComparison.Ordinal)
			|| string.Equals(effectType, "DaRiTerrain", StringComparison.Ordinal)
			|| string.Equals(effectType, "SmashTerrain", StringComparison.Ordinal);
	}

	private static bool TryApplyNativeTerrainDestruction(Actor caster, WorldTile centerTile, int radius, out string reason)
	{
		reason = string.Empty;
		if (centerTile == null || radius <= 0)
		{
			reason = "invalid_native_destruction_args";
			return false;
		}

		try
		{
			// 地形摧毁完全交由 WorldBox 原生圆形 damageWorld 处理；不再通过
			// 512 格采样队列逐格 decreaseTile，因此不会留下隔格破坏的棋盘纹。
			MapAction.damageWorld(centerTile, radius, TerraformLibrary.bomb, caster);
			return true;
		}
		catch (Exception ex)
		{
			reason = "native_terrain_destruction_failed:" + ex.GetType().Name;
			XjExceptionDiagnostics.Report("XjJinDanCombatApi.NativeTerrainDestruction", ex);
			return false;
		}
	}

	internal static bool TryApplyTerrainEffectAtTile(
		WorldTile tile,
		string effectType,
		float durationSeconds,
		string spellId,
		out string reason)
	{
		return TryApplyTerrainEffectAtTile(null, tile, effectType, durationSeconds, spellId, out reason);
	}

	internal static bool TryApplyTerrainEffectAtTile(
		Actor caster,
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

		bool applied = TryApplyTerrainEffectToTile(tile, effectType, durationSeconds, caster);
		if (!applied)
		{
			reason = caster?.data == null
				? "single_tile_terrain_not_applied"
				: "single_tile_terrain_not_attributed_or_blocked";
		}

		return applied;
	}

	internal static bool TryApplyDaoTaiTsarBombTerrainEffect(
		WorldTile centerTile,
		Actor caster,
		string spellId,
		out string reason)
	{
		reason = string.Empty;
		if (centerTile == null)
		{
			reason = "invalid_daotai_tsar_tile";
			return false;
		}

		try
		{
			// 道胎通用雷法仍沿用原生 czar_bomba 的地形表现，但必须保留施法者来源。
			// 这样地形破坏会重新进入 WorldBox 原生 Gaia/生态保护判定，不再以“世界力量”绕过保护。
			BaseSimObject byWho = caster?.data == null ? null : caster;
			MapAction.damageWorld(
				centerTile,
				XjDaoTaiSpellScale.ResolveAbsoluteRadius(caster),
				TerraformLibrary.czar_bomba,
				byWho);
			return true;
		}
		catch (Exception ex)
		{
			reason = "daotai_tsar_terrain_failed:" + ex.GetType().Name;
			XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:DaoTaiTsarBomb", ex);
			return false;
		}
	}

	internal static bool TryApplyStagedFrozenTerrain(
		WorldTile centerTile,
		int radius,
		float durationSeconds,
		Action completionAction,
		string spellId,
		out string reason)
	{
		return TryApplyStagedFrozenTerrain(null, centerTile, radius, durationSeconds, completionAction, spellId, out reason);
	}

	internal static bool TryApplyStagedFrozenTerrain(
		Actor caster,
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
			caster,
			centerTile,
			"FrozenTerrain",
			radius,
			durationSeconds,
			completionAction,
			out reason);
	}

	private static bool TryEnqueueTerrainEffect(
		Actor caster,
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

		long casterId = 0L;
		try { casterId = caster?.data == null ? 0L : ((BaseSystemData)caster.data).id; } catch { casterId = 0L; }
		PendingTerrainTasks.Enqueue(new TerrainEffectTask(
			centerTile,
			radius,
			effectType,
			durationSeconds,
			completionAction,
			casterId));
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
		catch (System.Exception xjCaught345) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:345", xjCaught345); }

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
				return TryPlayYingYueFrozenActorOverlays(targets, definition, out reason);
			}

			if (string.Equals(definition.Id, "QingTengFu", StringComparison.Ordinal))
			{
				return TryPlayQingTengActorOverlays(targets, definition, out reason);
			}

			if (string.Equals(definition.Id, "BinFengTuCi", StringComparison.Ordinal))
			{
				return TryPlayBinFengActorOverlays(targets, definition, out reason);
			}

			if (string.Equals(definition.Id, "CommonLeiFa", StringComparison.Ordinal))
			{
				return TryPlayThunderSupplementalVisuals(centerTile, targets, true, out reason);
			}

			if (string.Equals(definition.Id, "JiuXiaoShenLei", StringComparison.Ordinal))
			{
				return TryPlayThunderSupplementalVisuals(centerTile, targets, false, out reason);
			}

			if (string.Equals(definition.Id, "JinDeJianZhen", StringComparison.Ordinal)
				|| string.Equals(definition.Id, "KuJinJinXiaoDong", StringComparison.Ordinal))
			{
				return TryPlayJinDeJianZhenTargetSwords(targets, definition, out reason);
			}

			if (string.Equals(definition.Id, "DuiJinQingLiang", StringComparison.Ordinal)
				|| string.Equals(definition.Id, "XiaoJinWangShangFeng", StringComparison.Ordinal)
				|| string.Equals(definition.Id, "GengJinXiaoGuFeng", StringComparison.Ordinal))
			{
				return TryPlayJinDeSlashHits(targets, definition, out reason);
			}
		}
		catch (Exception ex)
		{
			reason = "supplemental_visual_failed:" + ex.GetType().Name;
		}

		return false;
	}

	/// <summary>
	/// 高境余威只提供稀疏边界/中心视觉，不为上百个被波及者逐一生成对象。
	/// 这样可以保留0.21“一击震动大片战场”的体感，同时让视觉对象数与人口脱钩。
	/// </summary>
	internal static bool TryPlayHighRealmPressureVisuals(WorldTile centerTile, int radius, bool daoTai)
	{
		if (centerTile == null || radius <= 0) return false;
		try
		{
			bool highLoad = XjCombatEffectPool.IsHighLoad();
			bool spawned = false;
			spawned |= TrySpawnNativeEffectAtTile(centerTile, daoTai ? "fx_explosion_large" : "fx_spark", daoTai ? 1.8f : 1.25f);
			if (highLoad)
			{
				spawned |= TrySpawnNativeRing(centerTile, Math.Max(2, radius * 2 / 3), "fx_spark", daoTai ? 0.9f : 0.65f);
				return spawned;
			}

			spawned |= TrySpawnNativeRing(centerTile, Math.Max(2, radius / 3), "fx_spark", daoTai ? 1.0f : 0.70f);
			spawned |= TrySpawnNativeRing(centerTile, Math.Max(3, radius * 2 / 3), daoTai ? "fx_explosion_small" : "fx_spark", daoTai ? 0.85f : 0.60f);
			spawned |= TrySpawnNativeRing(centerTile, radius, "fx_spark", daoTai ? 0.80f : 0.52f);
			return spawned;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjJinDanCombatApi.TryPlayHighRealmPressureVisuals", ex);
			return false;
		}
	}

	internal static bool TryApplyImpactHold(Actor actor, float durationSeconds, string spellId, out string reason)
	{
		return TryApplyImpactHold(null, actor, durationSeconds, spellId, out reason);
	}

	internal static bool TryApplyImpactHold(
		Actor caster,
		Actor actor,
		float durationSeconds,
		string spellId,
		out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || durationSeconds <= 0f || !IsAlive(actor))
		{
			reason = "invalid_impact_hold_args";
			return false;
		}

		if (XjHighRealmControlService.CanUseAttributedHighRealmControl(caster))
		{
			return XjHighRealmControlService.TryApplyHold(caster, actor, durationSeconds, false, out reason);
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
		catch (System.Exception xjCaught536) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:536", xjCaught536); }

		return 1f;
	}

	internal static float GetMaxHealth(Actor actor)
	{
		try
		{
			return Math.Max(1f, ((BaseSimObject)actor).getMaxHealth());
		}
		catch (System.Exception xjCaught541_12)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:541", xjCaught541_12);
			
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
		catch (System.Exception xjCaught558_13)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:558", xjCaught558_13);
			
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

	private static bool TryPlayYingYueFrozenActorOverlays(
		IReadOnlyList<Actor> targets,
		in XjJinDanDaoSpellDefinition definition,
		out string reason)
	{
		reason = string.Empty;
		if (targets == null || targets.Count == 0)
		{
			reason = "yingyue_freeze_visual_args_missing";
			return false;
		}

		bool spawned = false;
		int visualLimit = XjCombatEffectPool.IsHighLoad() ? 6 : 12;
		int visualized = 0;
		for (int i = 0; i < targets.Count && visualized < visualLimit; i++)
		{
			Actor target = targets[i];
			if (!IsAlive(target) || !XjAttributedFreezeRegistry.IsFrozen(target))
			{
				continue;
			}

			visualized++;
			Vector2 anchorPosition = ResolveEffectAnchor(target, null);
			spawned |= XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
				IceDetonationVisualPath,
				anchorPosition.x,
				anchorPosition.y + 0.3f,
				0.09f,
				0.07f);

			// 冰晶是状态附着层，不承担伤害。真正何时销毁完全由 keepAlive 查询
			// 归因冻结表决定；给协程一个很宽的保险窗口，避免 WorldBox 暂停/低速时
			// Unity 实时时钟先把视觉耗尽。正常情况下冻结到期会立即回收。
			Actor trackedTarget = target;
			float animationDuration = 16f * 0.08f;
			const float visualEnvelope = 3600f;
			spawned |= XjCombatEffectPool.TrySpawnTrackedOverlaySpriteAnimationSafe(
				YingYueFreezeOverlayPath,
				() => ResolveEffectAnchor(trackedTarget, null) + Vector2.up * 0.22f,
				0.32f,
				0.08f,
				animationDuration,
				Mathf.Max(0f, visualEnvelope - animationDuration),
				66,
				0f,
				false,
				() => IsAlive(trackedTarget) && XjAttributedFreezeRegistry.IsFrozen(trackedTarget));
		}

		if (!spawned)
		{
			reason = "yingyue_freeze_visual_not_spawned";
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
				0.15f,
				definition.AnimationLifetimeSeconds,
				definition.StatusDurationSeconds,
				0f);
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

			if (XjAttributedFreezeRegistry.IsFrozen(target))
			{
				Vector2 anchorPosition = ResolveEffectAnchor(target, null);
				spawned |= XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
					IceLanceVisualPath,
					anchorPosition.x,
					anchorPosition.y + 0.15f,
					0.12f,
					0.085f);
			}
		}

		if (!spawned)
		{
			reason = "binfeng_overlay_not_spawned";
		}

		return spawned;
	}

	private static bool TryPlayThunderSupplementalVisuals(
		WorldTile centerTile,
		IReadOnlyList<Actor> targets,
		bool includeFormation,
		out string reason)
	{
		reason = string.Empty;
		bool spawned = false;

		if (includeFormation && centerTile != null)
		{
			Vector2 center = ResolveEffectAnchor(null, centerTile);
			spawned |= XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
				ThunderFormationVisualPath,
				center.x,
				center.y,
				0.10f,
				0.08f);
		}

		if (targets != null && targets.Count > 0)
		{
			int maxHits = includeFormation
				? (XjCombatEffectPool.IsHighLoad() ? 5 : 10)
				: 1;
			int visualized = 0;
			for (int i = 0; i < targets.Count && visualized < maxHits; i++)
			{
				Actor target = targets[i];
				if (!IsAlive(target)) continue;
				visualized++;
				Vector2 anchorPosition = ResolveEffectAnchor(target, null);
				spawned |= XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
					ThunderHitVisualPath,
					anchorPosition.x,
					anchorPosition.y + 0.55f,
					0.20f,
					0.07f);
			}
		}

		if (!spawned)
		{
			reason = "thunder_supplemental_visual_not_spawned";
		}
		return spawned;
	}

	private static bool TryPlayJinDeSlashHits(
		IReadOnlyList<Actor> targets,
		in XjJinDanDaoSpellDefinition definition,
		out string reason)
	{
		reason = string.Empty;
		if (targets == null || targets.Count == 0)
		{
			reason = "jinde_slash_args_missing";
			return false;
		}

		bool spawned = false;
		int limit = Mathf.Min(targets.Count, definition.MaxTargets > 0 ? definition.MaxTargets : 1);
		if (XjCombatEffectPool.IsHighLoad()) limit = Mathf.Min(limit, 2);
		for (int i = 0; i < limit; i++)
		{
			Actor target = targets[i];
			if (!IsAlive(target)) continue;
			Vector2 pos = ResolveEffectAnchor(target, null);
			spawned |= XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
				JinDeSlashVisualPath,
				pos.x,
				pos.y + 0.35f,
				string.Equals(definition.Id, "GengJinXiaoGuFeng", StringComparison.Ordinal) ? 0.13f : 0.11f,
				0.05f);
		}

		if (!spawned) reason = "jinde_slash_not_spawned";
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
		catch (System.Exception xjCaught829_15)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:829", xjCaught829_15);
			
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
		catch (System.Exception xjCaught884) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:884", xjCaught884); }

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
		catch (System.Exception xjCaught940) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:940", xjCaught940); }
	}

	private static bool TryApplyTerrainEffectToTile(WorldTile tile, string effectType, float durationSeconds, Actor caster)
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
				// 0.9.8.28：恢复0.21金丹雷法“落处成灾”的环境压力。地形仍通过
				// 512格采样预算和带施法者来源的 decreaseTile 执行，不做全圆逐格同步破坏，
				// 也不绕过 Gaia/生态保护。结果会形成大片焦土、断路、倒木与废墟，而非纯特效。
				options.damage_buildings = true;
				options.destroy_buildings = true;
				options.make_ruins = true;
				options.remove_roads = true;
				options.remove_trees_fully = true;
				options.set_fire = true;
				options.add_burned = true;
				options.add_heat = 55;
				options.lightning_effect = true;
				options.shake = true;
				options.shake_duration = 1400f;
				options.shake_intensity = 0.006f;
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
			if (caster?.data != null)
			{
				// 角色法术只能走带来源的 WorldBox 重载；当前 Build 若不提供此入口，
				// 宁可该格不改地形，也不退回无来源 decreaseTile 绕过 Gaia。
				return XjAttributedTerrainInterop.TryDecreaseTile(tile, options, caster);
			}
			MapAction.decreaseTile(tile, false, options);
			return true;
		}
		catch (System.Exception xjCaught1004_18)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:1004", xjCaught1004_18);
			
			return false;
		}
	}

	private static bool TryApplyControlHold(Actor caster, Actor actor, string statusId, float durationSeconds)
	{
		if (!string.Equals(statusId, "root", StringComparison.Ordinal)
			&& !string.Equals(statusId, "frozen", StringComparison.Ordinal))
		{
			return false;
		}

		if (XjHighRealmControlService.CanUseAttributedHighRealmControl(caster))
		{
			return XjHighRealmControlService.TryApplyHold(caster, actor, durationSeconds, false, out _);
		}

		try
		{
			actor.stopMovement();
			actor.makeWait(Mathf.Max(0.15f, durationSeconds));
			return true;
		}
		catch (System.Exception xjCaught1024_19)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:1024", xjCaught1024_19);
			return false;
		}
	}

	private static bool IsAlive(Actor actor)
	{
		try
		{
			return XjSafeCore.IsAliveActor(actor);
		}
		catch (System.Exception xjCaught1036_20)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanCombatApi.cs:1036", xjCaught1036_20);
			
			return actor?.data != null;
		}
	}
}
