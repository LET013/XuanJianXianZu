using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Visual;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 火德真君法术“朱明焚天雀”。火鸟是有时限的战斗法相，不注册为人口单位，
/// 因而不会参与城市、家族、存档人口或年度调度；伤害仍由金丹战斗 API 结算。
/// 存续和冷却使用世界年份：最多存续五年，结束后三年方可再次召唤。
/// </summary>
internal static class XjHuoDeFireBirdSummonSystem
{
	private const string ResourceRoot = "effects/Skills/ZhuMingFenTianQue";
	private const float MoveDurationSeconds = 0.34f;
	private const float BetweenAttackSeconds = 0.55f;
	private const float DefaultFrameInterval = 0.09f;
	private const float FireBirdVisualScaleMultiplier = 2f;
	private const int MaxConcurrentSummons = 48;

	private static readonly HashSet<long> ActiveCasterIds = new HashSet<long>();
	private static int _activeSummons;
	private static int _generation;

	internal static bool TrySummon(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		if (caster?.data == null
			|| !XjSafeCore.IsAliveActor(caster)
			|| context.CenterTile == null
			|| _activeSummons >= MaxConcurrentSummons)
		{
			return false;
		}

		long casterId = GetActorId(caster);
		if (casterId <= 0L || ActiveCasterIds.Contains(casterId))
		{
			return false;
		}

		MapBox world = World.world;
		if ((UnityEngine.Object)(object)world == (UnityEngine.Object)null)
		{
			return false;
		}

		int currentYear = ResolveCurrentYear();
		XjActorAccessor.TryGetInt(caster, XjActorDataKeys.XjFireBirdActiveUntilYear, out int activeUntilYear);
		XjActorAccessor.TryGetInt(caster, XjActorDataKeys.XjFireBirdReadyYear, out int readyYear);

		if (activeUntilYear <= currentYear)
		{
			if (readyYear > currentYear)
			{
				return false;
			}

			int durationYears = Math.Max(1, definition.DurationYears);
			int cooldownYears = Math.Max(0, definition.CooldownYears);
			activeUntilYear = currentYear + durationYears;
			readyYear = activeUntilYear + cooldownYears;
			XjActorAccessor.SetInt(caster, XjActorDataKeys.XjFireBirdActiveUntilYear, activeUntilYear);
			XjActorAccessor.SetInt(caster, XjActorDataKeys.XjFireBirdReadyYear, readyYear);
		}

		List<Actor> initialTargets = new List<Actor>(context.Targets.Count);
		for (int i = 0; i < context.Targets.Count; i++)
		{
			Actor target = context.Targets[i];
			if (XjSafeCore.IsAliveActor(target))
			{
				initialTargets.Add(target);
			}
		}

		XjJinDanDaoSpellDefinition definitionCopy = definition;
		WorldTile centerTile = context.CenterTile;
		int generation = _generation;
		try
		{
			ActiveCasterIds.Add(casterId);
			_activeSummons++;
			((MonoBehaviour)world).StartCoroutine(RunSummon(
				caster,
				casterId,
				initialTargets,
				centerTile,
				definitionCopy,
				activeUntilYear,
				generation));
			return true;
		}
		catch (System.Exception xjCaught103_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjHuoDeFireBirdSummonSystem.cs:103", xjCaught103_1);
			
			ActiveCasterIds.Remove(casterId);
			if (generation == _generation)
			{
				_activeSummons = Math.Max(0, _activeSummons - 1);
			}
			return false;
		}
	}

	internal static void RemoveActor(long actorId)
	{
		if (actorId > 0L)
		{
			ActiveCasterIds.Remove(actorId);
		}
	}

	internal static void Clear()
	{
		_generation++;
		_activeSummons = 0;
		ActiveCasterIds.Clear();
	}

	private static IEnumerator RunSummon(
		Actor caster,
		long casterId,
		List<Actor> initialTargets,
		WorldTile fallbackTile,
		XjJinDanDaoSpellDefinition definition,
		int activeUntilYear,
		int generation)
	{
		GameObject root = null;
		try
		{
			root = new GameObject("xj_huode_firebird_summon");
			root.hideFlags = HideFlags.DontSave;
			SpriteRenderer renderer = XjWorldSpriteRenderLayer.AddRenderer(root, 2);
			if (renderer == null)
			{
				yield break;
			}

			Sprite[] reviveFrames = LoadFrames("revive");
			Sprite[] idleFrames = LoadFrames("idle");
			Sprite[] idleFrames2 = LoadFrames("idle_2");
			Sprite[] moveFrames = LoadFrames("movement");
			Sprite[] attackFrames = LoadFrames("attack");
			Sprite[] attackFrames2 = LoadFrames("attack_2");
			Sprite[] damageFrames = LoadFrames("damage");
			Sprite[] deathFrames = LoadFrames("death");
			Sprite[] projectileFrames = LoadFrames("projectile");

			Sprite[] scaleFrames = idleFrames.Length > 0 ? idleFrames : reviveFrames;
			float actorWorldSize = XjActorVisualMetrics.ResolveWorldSize(caster);
			float visualScale = ResolveScale(scaleFrames, actorWorldSize) * FireBirdVisualScaleMultiplier;
			float summonHeight = Mathf.Max(0.8f, actorWorldSize * 1.15f);
			root.transform.localScale = new Vector3(visualScale, visualScale, 1f);
			root.transform.position = ToWorldPosition(ResolveActorAnchor(caster, fallbackTile), summonHeight);

			yield return PlayFrames(renderer, reviveFrames, DefaultFrameInterval, false);

			float lastCasterHealth = XjSafeCore.GetHealthSafe(caster);
			int attackIndex = 0;
			int idleIndex = 0;

			while (generation == _generation
				&& ResolveCurrentYear() < activeUntilYear
				&& XjSafeCore.IsAliveActor(caster))
			{
				float currentHealth = XjSafeCore.GetHealthSafe(caster);
				if (currentHealth + 0.01f < lastCasterHealth && damageFrames.Length > 0)
				{
					yield return PlayFrames(renderer, damageFrames, DefaultFrameInterval, false);
				}
				lastCasterHealth = currentHealth;

				Actor target = ResolveTarget(caster, initialTargets, fallbackTile, definition.Radius);
				if (!XjSafeCore.IsAliveActor(target))
				{
					Vector2 idleAnchor = ResolveActorAnchor(caster, fallbackTile) + Vector2.up * summonHeight;
					root.transform.position = new Vector3(idleAnchor.x, idleAnchor.y, root.transform.position.z);
					Sprite[] idle = (idleIndex++ & 1) == 0 ? idleFrames : idleFrames2;
					yield return PlayFrames(renderer, idle, DefaultFrameInterval, true, 0.45f);
					continue;
				}

				WorldTile targetTile = XjJinDanCombatApi.ResolveImpactTile(target, fallbackTile);
				Vector2 destination = ResolveActorAnchor(target, targetTile) + Vector2.up * summonHeight;
				yield return MoveTo(root, renderer, moveFrames, destination, MoveDurationSeconds);

				bool secondAttack = (attackIndex++ & 1) == 1;
				Sprite[] selectedAttack = secondAttack ? attackFrames2 : attackFrames;
				yield return PlayFrames(renderer, selectedAttack, DefaultFrameInterval, false);

				ApplyAttack(caster, target, targetTile, definition, secondAttack);
				if (secondAttack && projectileFrames.Length > 0 && targetTile != null)
				{
					Vector2Int pos = targetTile.pos;
					XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
						ResourceRoot + "/projectile",
						pos.x + 0.5f,
						pos.y + 1.2f,
						0.55f,
						DefaultFrameInterval);
				}

				Sprite[] idleAfterAttack = (idleIndex++ & 1) == 0 ? idleFrames : idleFrames2;
				yield return PlayFrames(renderer, idleAfterAttack, DefaultFrameInterval, true, BetweenAttackSeconds);
			}

			if (generation == _generation)
			{
				yield return PlayFrames(renderer, deathFrames, DefaultFrameInterval, false);
			}
		}
		finally
		{
			try
			{
				if (root != null)
				{
					UnityEngine.Object.Destroy(root);
				}
			}
			catch (System.Exception xjCaught231) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjHuoDeFireBirdSummonSystem.cs:231", xjCaught231); }

			ActiveCasterIds.Remove(casterId);
			if (generation == _generation)
			{
				_activeSummons = Math.Max(0, _activeSummons - 1);
			}
		}
	}

	private static void ApplyAttack(
		Actor caster,
		Actor primaryTarget,
		WorldTile impactTile,
		in XjJinDanDaoSpellDefinition definition,
		bool spellAttack)
	{
		if (!XjSafeCore.IsAliveActor(caster) || !XjSafeCore.IsAliveActor(primaryTarget) || impactTile == null)
		{
			return;
		}

		IReadOnlyList<Actor> targets = XjJinDanDaoSpellTargeting.CollectLocalImpactTargets(
			caster,
			primaryTarget,
			impactTile,
			Mathf.Clamp(definition.TerrainRadius, 3, 8));

		float baseDamage = XjJinDanCombatApi.GetBaseDamage(caster);
		int limit = Mathf.Min(
			targets.Count,
			definition.MaxTargets > 0 ? definition.MaxTargets : 3);
		for (int i = 0; i < limit; i++)
		{
			Actor target = targets[i];
			if (!XjSafeCore.IsAliveActor(target))
			{
				continue;
			}

			float maxHealthComponent = Mathf.Min(
				XjJinDanCombatApi.GetMaxHealth(target) * definition.SameRealmMaxHealthDamageRatio,
				baseDamage * 1.5f);
			float attackMultiplier = spellAttack ? 1.1f : 0.85f;
			float damage = Mathf.Max(
				1f,
				baseDamage * definition.DamageMultiplier * attackMultiplier + maxHealthComponent);
			if (i > 0)
			{
				damage *= 0.65f;
			}

			XjJinDanCombatApi.TryDamageActor(caster, target, damage, definition.Id, out _);

			// 火鸟命中直接刷新原版 burning 状态。该状态作用于被命中的角色，
			// 不再依赖地形改造间接点燃，避免目标站在水面或边缘格时漏掉燃烧。
			XjJinDanCombatApi.TryApplyStatus(
				target,
				"burning",
				Mathf.Max(2f, definition.TerrainDurationSeconds),
				definition.Id,
				out _);
		}

		// 仅生成原版火焰 drop，形成可见且可传播的地面燃烧。
		// 严禁再调用 MapAction.decreaseTile / damageWorld，因其会焦化、降级或破坏地形。
		SpawnBurningGround(impactTile, Mathf.Clamp(definition.TerrainRadius, 2, 5));
	}

	private static void SpawnBurningGround(WorldTile centerTile, int radius)
	{
		MapBox world = World.world;
		if (centerTile == null || (UnityEngine.Object)(object)world == (UnityEngine.Object)null || world.drop_manager == null)
		{
			return;
		}

		Vector2Int center = centerTile.pos;
		int safeRadius = Mathf.Clamp(radius, 1, 5);
		int radiusSquared = safeRadius * safeRadius;

		// 棋盘式稀疏铺火：半径 3 时最多生成约 15 个火焰，
		// 足以覆盖命中区域，同时避免多只火鸟长期战斗时制造过量 drop。
		for (int y = -safeRadius; y <= safeRadius; y++)
		{
			for (int x = -safeRadius; x <= safeRadius; x++)
			{
				if (x * x + y * y > radiusSquared || ((x + y) & 1) != 0)
				{
					continue;
				}

				WorldTile tile = world.GetTile(center.x + x, center.y + y);
				if (tile == null)
				{
					continue;
				}

				try
				{
					world.drop_manager.spawn(tile, "fire", 1.5f, -1f, -1L);
				}
				catch (System.Exception xjCaught335) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjHuoDeFireBirdSummonSystem.cs:335", xjCaught335); }
			}
		}
	}

	private static Actor ResolveTarget(
		Actor caster,
		List<Actor> initialTargets,
		WorldTile fallbackTile,
		int radius)
	{
		Actor casterTarget = TryGetCasterTarget(caster);
		if (XjSafeCore.IsAliveActor(casterTarget))
		{
			return casterTarget;
		}

		for (int i = 0; i < initialTargets.Count; i++)
		{
			if (XjSafeCore.IsAliveActor(initialTargets[i]))
			{
				return initialTargets[i];
			}
		}

		WorldTile center = null;
		try
		{
			center = ((BaseSimObject)caster).current_tile;
		}
		catch (System.Exception xjCaught367) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjHuoDeFireBirdSummonSystem.cs:367", xjCaught367); }
		center ??= fallbackTile;
		if (center == null)
		{
			return null;
		}

		IReadOnlyList<Actor> nearby = XjJinDanDaoSpellTargeting.CollectLocalImpactTargets(
			caster,
			null,
			center,
			Mathf.Max(8, radius));
		for (int i = 0; i < nearby.Count; i++)
		{
			if (XjSafeCore.IsAliveActor(nearby[i]))
			{
				return nearby[i];
			}
		}

		return null;
	}

	private static Actor TryGetCasterTarget(Actor caster)
	{
		if (!XjSafeCore.IsAliveActor(caster))
		{
			return null;
		}

		try
		{
			Actor target = caster.attack_target as Actor;
			if (IsHostileTarget(caster, target))
			{
				return target;
			}
		}
		catch (System.Exception xjCaught407) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjHuoDeFireBirdSummonSystem.cs:407", xjCaught407); }

		try
		{
			Actor retaliatoryTarget = caster.attackedBy as Actor;
			return IsHostileTarget(caster, retaliatoryTarget) ? retaliatoryTarget : null;
		}
		catch (System.Exception xjCaught408_6)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjHuoDeFireBirdSummonSystem.cs:408", xjCaught408_6);
			
			return null;
		}
	}

	private static bool IsHostileTarget(Actor caster, Actor target)
	{
		if (!XjSafeCore.IsAliveActor(target) || ReferenceEquals(caster, target))
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
		catch (System.Exception xjCaught436_7)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjHuoDeFireBirdSummonSystem.cs:436", xjCaught436_7);
			
			return false;
		}
	}

	private static IEnumerator MoveTo(
		GameObject root,
		SpriteRenderer renderer,
		Sprite[] frames,
		Vector2 destination,
		float duration)
	{
		if (root == null)
		{
			yield break;
		}

		Vector3 start = root.transform.position;
		Vector3 end = new Vector3(destination.x, destination.y, start.z);
		renderer.flipX = end.x < start.x;
		float safeDuration = Mathf.Max(0.05f, duration);
		float elapsed = 0f;
		while (elapsed < safeDuration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / safeDuration);
			root.transform.position = Vector3.Lerp(start, end, t);
			if (frames.Length > 0)
			{
				int index = Mathf.Min(frames.Length - 1, Mathf.FloorToInt(t * frames.Length));
				renderer.sprite = frames[index];
			}
			yield return null;
		}
		root.transform.position = end;
	}

	private static IEnumerator PlayFrames(
		SpriteRenderer renderer,
		Sprite[] frames,
		float frameInterval,
		bool loop,
		float duration = 0f)
	{
		if (renderer == null || frames == null || frames.Length == 0)
		{
			if (duration > 0f)
			{
				yield return new WaitForSeconds(duration);
			}
			yield break;
		}

		float interval = Mathf.Max(0.03f, frameInterval);
		if (!loop)
		{
			for (int i = 0; i < frames.Length; i++)
			{
				renderer.sprite = frames[i];
				yield return new WaitForSeconds(interval);
			}
			yield break;
		}

		float endTime = Time.time + Mathf.Max(interval, duration);
		int index = 0;
		while (Time.time < endTime)
		{
			renderer.sprite = frames[index++ % frames.Length];
			yield return new WaitForSeconds(interval);
		}
	}

	private static Sprite[] LoadFrames(string action)
	{
		return XjCombatEffectPool.GetCachedAnimationFrames(ResourceRoot + "/" + action);
	}

	private static float ResolveScale(Sprite[] frames, float desiredWorldSize)
	{
		if (frames == null || frames.Length == 0)
		{
			return 1f;
		}

		float diameter = 0f;
		for (int i = 0; i < frames.Length; i++)
		{
			Sprite frame = frames[i];
			if (frame == null)
			{
				continue;
			}
			diameter = Mathf.Max(diameter, Mathf.Max(frame.bounds.size.x, frame.bounds.size.y));
		}

		return diameter > 0.01f
			? Mathf.Clamp(Mathf.Max(0.75f, desiredWorldSize) / diameter, 0.05f, 8f)
			: 1f;
	}

	private static int ResolveCurrentYear()
	{
		int tracked = XjYearTracker.CurrentYear;
		int worldYear = World.world?.map_stats?.year ?? 0;
		return Math.Max(1, Math.Max(tracked, worldYear));
	}

	private static long GetActorId(Actor actor)
	{
		try
		{
			return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		}
		catch (System.Exception xjCaught551_8)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjHuoDeFireBirdSummonSystem.cs:551", xjCaught551_8);
			
			return 0L;
		}
	}

	private static Vector2 ResolveActorAnchor(Actor actor, WorldTile fallbackTile)
	{
		try
		{
			if (XjSafeCore.IsAliveActor(actor))
			{
				BaseSimObject sim = actor;
				Vector2 position = sim.current_position;
				if (position != Vector2.zero)
				{
					return position;
				}
				if (sim.current_tile != null)
				{
					Vector2Int pos = sim.current_tile.pos;
					return new Vector2(pos.x + 0.5f, pos.y + 0.5f);
				}
			}
		}
		catch (System.Exception xjCaught584) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjHuoDeFireBirdSummonSystem.cs:584", xjCaught584); }

		if (fallbackTile != null)
		{
			Vector2Int pos = fallbackTile.pos;
			return new Vector2(pos.x + 0.5f, pos.y + 0.5f);
		}
		return Vector2.zero;
	}

	private static Vector3 ToWorldPosition(Vector2 position, float yOffset)
	{
		return new Vector3(position.x, position.y + yOffset, 0f);
	}
}
