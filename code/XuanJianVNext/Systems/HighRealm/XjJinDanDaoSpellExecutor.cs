using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;
using UnityEngine;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjJinDanDaoSpellExecutor
{
	internal static bool TryExecute(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		if (caster?.data == null || string.IsNullOrWhiteSpace(definition.Id))
		{
			return false;
		}

		if (string.Equals(definition.Id, "YingYueLianHua", StringComparison.Ordinal))
		{
			return TryExecuteYingYueLianHua(caster, definition, context);
		}

		if (string.Equals(definition.Id, "CommonLeiFa", StringComparison.Ordinal))
		{
			return TryExecuteCommonLeiFa(caster, definition, context);
		}

		if (string.Equals(definition.Id, "JiuXiaoShenLei", StringComparison.Ordinal))
		{
			return TryExecuteJiuXiaoShenLei(caster, definition, context);
		}

		if (string.Equals(definition.Id, "DaRiZhuiYuan", StringComparison.Ordinal)
			|| string.Equals(definition.Id, "ZhenYueBeng", StringComparison.Ordinal))
		{
			return TryExecuteDelayedImpactSpell(caster, definition, context);
		}

		if (string.Equals(definition.Id, "BinFengTuCi", StringComparison.Ordinal))
		{
			return TryExecuteBinFengTuCi(caster, definition, context);
		}

		if (string.Equals(definition.Id, "QingTengFu", StringComparison.Ordinal))
		{
			return TryExecuteQingTengFu(caster, definition, context);
		}

		if (string.Equals(definition.Id, "ZhuMingFenTianQue", StringComparison.Ordinal))
		{
			return XjHuoDeFireBirdSummonSystem.TrySummon(caster, definition, context);
		}

		if (string.Equals(definition.Id, "SanYinBaoYiXuanLun", StringComparison.Ordinal))
		{
			return XjSanYinBaoYiXuanLunSystem.TryActivate(caster, definition);
		}

		if (string.Equals(definition.Id, "YangYanLianJi", StringComparison.Ordinal))
		{
			return TryExecuteImmediateAreaSpell(caster, definition, context);
		}

		bool applied = false;
		if (definition.HealMaxHealthRatio > 0f)
		{
			applied |= TryHealCaster(caster, definition.HealMaxHealthRatio, definition.Id);
		}

		applied |= ApplyTargetEffects(caster, definition, context);
		applied |= ApplyTerrainEffect(definition, context.CenterTile);

		XjJinDanCombatApi.TryPlayPrimarySpellAnimation(context, definition, out _);
		XjJinDanCombatApi.TryPlaySupplementalVisuals(context.CenterTile, context.Targets, definition, out _);
		return applied;
	}

	private static bool TryExecuteYingYueLianHua(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		bool applied = TryHealCaster(caster, definition.HealMaxHealthRatio, definition.Id);
		XjJinDanDaoSpellTargetContext initialContext = context;
		applied |= ApplyTargetStatuses(definition, initialContext);

		XjJinDanDaoSpellDefinition definitionCopy = definition;
		WorldTile centerTile = context.CenterTile;
		bool staged = XjJinDanCombatApi.TryApplyStagedFrozenTerrain(
			centerTile,
			definition.TerrainRadius,
			definition.TerrainDurationSeconds,
			() =>
			{
				XjJinDanDaoSpellTargetContext finalContext = BuildLocalImpactContext(
					caster,
					null,
					centerTile,
					definitionCopy.Radius);
				ApplyTargetEffects(caster, definitionCopy, finalContext);
			},
			definition.Id,
			out _);
		if (!staged)
		{
			applied |= ApplyTargetEffects(caster, definition, initialContext);
		}

		XjJinDanCombatApi.TryPlayPrimarySpellAnimation(initialContext, definition, out _);
		return staged || applied;
	}

	private static bool TryExecuteCommonLeiFa(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		XjJinDanCombatApi.TryPlayPrimarySpellAnimation(context, definition, out _);
		bool applied = ApplyTargetEffects(caster, definition, context);
		applied |= ApplyTerrainEffect(definition, context.CenterTile);
		return applied;
	}

	private static bool TryExecuteJiuXiaoShenLei(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		Actor strikeTarget = TryGetFirstTarget(context);
		if (strikeTarget?.data == null)
		{
			return false;
		}

		WorldTile strikeTile = XjJinDanCombatApi.ResolveImpactTile(strikeTarget, context.CenterTile);
		if (strikeTile == null)
		{
			return false;
		}

		XjJinDanDaoSpellTargetContext strikeContext = new XjJinDanDaoSpellTargetContext(
			strikeTile,
			new[] { strikeTarget });
		XjJinDanCombatApi.TryPlayPrimarySpellAnimation(strikeContext, definition, out _);
		bool applied = ApplyTargetEffects(caster, definition, strikeContext);
		applied |= XjJinDanCombatApi.TryApplyTerrainEffectAtTile(
			strikeTile,
			definition.TerrainEffect,
			definition.TerrainDurationSeconds,
			definition.Id,
			out _);
		return applied;
	}

	private static bool TryExecuteBinFengTuCi(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		bool applied = false;
		for (int i = 0; i < context.Targets.Count; i++)
		{
			Actor impactTarget = context.Targets[i];
			WorldTile impactTile = XjJinDanCombatApi.ResolveImpactTile(impactTarget, null);
			if (impactTile == null)
			{
				continue;
			}

			IReadOnlyList<Actor> localTargets = XjJinDanDaoSpellTargeting.CollectLocalImpactTargets(
				caster,
				impactTarget,
				impactTile,
				definition.TerrainRadius);
			XjJinDanDaoSpellTargetContext impactContext = new XjJinDanDaoSpellTargetContext(impactTile, localTargets);
			applied |= ApplyTargetEffects(caster, definition, impactContext, definition.TerrainRadius);
			applied |= ApplyTerrainEffect(definition, impactTile);
		}

		XjJinDanCombatApi.TryPlaySupplementalVisuals(context.CenterTile, context.Targets, definition, out _);
		return applied;
	}

	private static bool TryExecuteQingTengFu(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		XjJinDanCombatApi.TryPlaySupplementalVisuals(context.CenterTile, context.Targets, definition, out _);

		XjJinDanDaoSpellDefinition definitionCopy = definition;
		XjJinDanDaoSpellTargetContext contextCopy = context;
		bool scheduled = XjJinDanCombatApi.TryScheduleImpact(
			Mathf.Max(0.01f, definition.AnimationLifetimeSeconds),
			() => ApplyCapturedTargetEffects(caster, definitionCopy, contextCopy),
			definition.Id,
			out _);
		return scheduled || ApplyCapturedTargetEffects(caster, definition, context);
	}

	private static bool TryExecuteImmediateAreaSpell(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		bool applied = ApplyTargetEffects(caster, definition, context);
		applied |= ApplyTerrainEffect(definition, context.CenterTile);
		XjJinDanCombatApi.TryPlayPrimarySpellAnimation(context, definition, out _);
		return applied;
	}

	private static bool TryExecuteDelayedImpactSpell(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		bool visualStarted = XjJinDanCombatApi.TryPlayPrimarySpellAnimation(context, definition, out _);
		if (!visualStarted)
		{
			XjJinDanDaoSpellTargetContext fallbackContext = BuildLocalImpactContext(
				caster,
				TryGetFirstTarget(context),
				context.CenterTile,
				definition.Radius);
			bool fallbackApplied = ApplyTargetEffects(caster, definition, fallbackContext);
			fallbackApplied |= ApplyTerrainEffect(definition, context.CenterTile);
			if (string.Equals(definition.Id, "DaRiZhuiYuan", StringComparison.Ordinal))
			{
				XjJinDanCombatApi.TryPlaySupplementalVisuals(context.CenterTile, fallbackContext.Targets, definition, out _);
			}

			return fallbackApplied;
		}

		XjJinDanDaoSpellDefinition definitionCopy = definition;
		XjJinDanDaoSpellTargetContext contextCopy = context;
		float delay = string.Equals(definition.Id, "DaRiZhuiYuan", StringComparison.Ordinal) ? 1.84f : 0.4f;
		bool scheduled = XjJinDanCombatApi.TryScheduleImpact(
			delay,
			() =>
			{
				Actor trackedTarget = TryGetFirstTarget(contextCopy);
				WorldTile impactTile = XjJinDanCombatApi.ResolveImpactTile(trackedTarget, contextCopy.CenterTile);
				XjJinDanDaoSpellTargetContext impactContext = BuildLocalImpactContext(
					caster,
					trackedTarget,
					impactTile,
					definitionCopy.Radius);
				ApplyTargetEffects(caster, definitionCopy, impactContext);
				ApplyTerrainEffect(definitionCopy, impactTile);
				if (string.Equals(definitionCopy.Id, "DaRiZhuiYuan", StringComparison.Ordinal))
				{
					XjJinDanCombatApi.TryPlaySupplementalVisuals(impactTile, impactContext.Targets, definitionCopy, out _);
				}
			},
			definition.Id,
			out _);
		if (scheduled)
		{
			return true;
		}

		XjJinDanDaoSpellTargetContext immediateContext = BuildLocalImpactContext(
			caster,
			TryGetFirstTarget(context),
			context.CenterTile,
			definition.Radius);
		bool applied = ApplyTargetEffects(caster, definition, immediateContext);
		applied |= ApplyTerrainEffect(definition, context.CenterTile);
		return applied;
	}

	private static XjJinDanDaoSpellTargetContext BuildLocalImpactContext(
		Actor caster,
		Actor primaryTarget,
		WorldTile impactTile,
		int radius)
	{
		IReadOnlyList<Actor> localTargets = XjJinDanDaoSpellTargeting.CollectLocalImpactTargets(
			caster,
			primaryTarget,
			impactTile,
			radius);
		return new XjJinDanDaoSpellTargetContext(impactTile, localTargets);
	}

	private static bool ApplyTargetEffects(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		return ApplyTargetEffects(caster, definition, context, definition.Radius);
	}

	private static bool ApplyTargetEffects(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context,
		int effectRadius)
	{
		bool applied = false;
		for (int i = 0; i < context.Targets.Count; i++)
		{
			Actor target = context.Targets[i];
			if (target?.data == null || !IsTargetWithinRadius(target, context.CenterTile, effectRadius))
			{
				continue;
			}

			applied |= ApplySingleTargetEffects(caster, target, definition);
		}

		return applied;
	}

	private static bool ApplyCapturedTargetEffects(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		bool applied = false;
		for (int i = 0; i < context.Targets.Count; i++)
		{
			Actor target = context.Targets[i];
			if (target?.data != null)
			{
				applied |= ApplySingleTargetEffects(caster, target, definition);
			}
		}

		return applied;
	}

	private static bool ApplySingleTargetEffects(
		Actor caster,
		Actor target,
		in XjJinDanDaoSpellDefinition definition)
	{
		bool applied = TryApplyDamage(caster, target, definition);
		if (!string.IsNullOrWhiteSpace(definition.StatusEffectId) && definition.StatusDurationSeconds > 0f)
		{
			applied |= XjJinDanCombatApi.TryApplyStatus(target, definition.StatusEffectId, definition.StatusDurationSeconds, definition.Id, out _);
		}

		float impactHold = Mathf.Max(definition.FloatDurationSeconds, definition.SmashDurationSeconds);
		if (impactHold > 0f)
		{
			applied |= XjJinDanCombatApi.TryApplyImpactHold(target, impactHold, definition.Id, out _);
		}

		return applied;
	}

	private static bool ApplyTargetStatuses(
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		if (string.IsNullOrWhiteSpace(definition.StatusEffectId) || definition.StatusDurationSeconds <= 0f)
		{
			return false;
		}

		bool applied = false;
		for (int i = 0; i < context.Targets.Count; i++)
		{
			Actor target = context.Targets[i];
			if (target?.data != null)
			{
				applied |= XjJinDanCombatApi.TryApplyStatus(target, definition.StatusEffectId, definition.StatusDurationSeconds, definition.Id, out _);
			}
		}

		return applied;
	}

	private static bool ApplyTerrainEffect(in XjJinDanDaoSpellDefinition definition, WorldTile centerTile)
	{
		return definition.TerrainRadius > 0
			&& !string.IsNullOrWhiteSpace(definition.TerrainEffect)
			&& XjJinDanCombatApi.TryApplyTerrainEffect(
				centerTile,
				definition.TerrainEffect,
				definition.TerrainRadius,
				definition.TerrainDurationSeconds,
				definition.Id,
				out _);
	}

	private static Actor TryGetFirstTarget(in XjJinDanDaoSpellTargetContext context)
	{
		for (int i = 0; i < context.Targets.Count; i++)
		{
			Actor target = context.Targets[i];
			if (target?.data != null)
			{
				return target;
			}
		}

		return null;
	}

	private static bool IsTargetWithinRadius(Actor target, WorldTile centerTile, int radius)
	{
		if (target?.data == null || centerTile == null || radius <= 0)
		{
			return target?.data != null;
		}

		try
		{
			WorldTile targetTile = ((BaseSimObject)target).current_tile;
			if (targetTile == null)
			{
				return false;
			}

			Vector2Int center = centerTile.pos;
			Vector2Int targetPosition = targetTile.pos;
			int dx = targetPosition.x - center.x;
			int dy = targetPosition.y - center.y;
			return dx * dx + dy * dy <= radius * radius;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryApplyDamage(Actor caster, Actor target, in XjJinDanDaoSpellDefinition definition)
	{
		if (caster?.data == null || target?.data == null || definition.DamageMultiplier <= 0f)
		{
			return false;
		}

		float damage = Mathf.Max(1f, XjJinDanCombatApi.GetBaseDamage(caster) * definition.DamageMultiplier);
		if (definition.SameRealmMaxHealthDamageRatio > 0f
			&& XjJinDanDaoSpellRuntime.IsJinDanActor(caster)
			&& XjJinDanDaoSpellRuntime.IsJinDanActor(target))
		{
			damage = Mathf.Max(1f, XjJinDanCombatApi.GetMaxHealth(target) * definition.SameRealmMaxHealthDamageRatio);
		}

		return XjJinDanCombatApi.TryDamageActor(caster, target, damage, definition.Id, out _);
	}

	private static bool TryHealCaster(Actor caster, float maxHealthRatio, string spellId)
	{
		if (caster?.data == null || maxHealthRatio <= 0f)
		{
			return false;
		}

		float amount = Mathf.Max(1f, XjJinDanCombatApi.GetMaxHealth(caster) * maxHealthRatio);
		return XjJinDanCombatApi.TryHealActor(caster, amount, spellId, out _);
	}

}
