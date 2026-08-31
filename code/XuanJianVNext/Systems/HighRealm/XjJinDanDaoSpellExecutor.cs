using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.ActorSystem;
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

		if (string.Equals(definition.Id, "GengJinXiaoGuFeng", StringComparison.Ordinal))
		{
			return TryExecuteGengJinXiaoGuFeng(caster, definition, context);
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
		applied |= ApplyCanonicalPostEffects(caster, definition, context);
		applied |= ApplyTerrainEffect(caster, definition, context.CenterTile);

		XjJinDanCombatApi.TryPlayPrimarySpellAnimation(context, definition, out _);
		XjJinDanCombatApi.TryPlaySupplementalVisuals(context.CenterTile, context.Targets, definition, out _);
		XjCanonicalShenTongVisualSystem.TryPlayDirectSupplemental(caster, definition, context);
		return applied;
	}

	private static bool TryExecuteYingYueLianHua(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		bool applied = TryHealCaster(caster, definition.HealMaxHealthRatio, definition.Id);
		XjJinDanDaoSpellTargetContext initialContext = context;
		applied |= ApplyTargetStatuses(caster, definition, initialContext);

		XjJinDanDaoSpellDefinition definitionCopy = definition;
		WorldTile centerTile = context.CenterTile;
		bool staged = XjJinDanCombatApi.TryApplyStagedFrozenTerrain(
			caster,
			centerTile,
			XjDaoTaiSpellScale.ResolveTerrainRadius(caster, definition),
			definition.TerrainDurationSeconds,
			() =>
			{
				XjJinDanDaoSpellTargetContext finalContext = BuildLocalImpactContext(
					caster,
					null,
					centerTile,
					XjDaoTaiSpellScale.ResolveTargetRadius(caster, definitionCopy));
				ApplyTargetEffects(caster, definitionCopy, finalContext);
				ApplyDaoTaiAbsoluteTerrainDamage(caster, definitionCopy, centerTile);
			},
			definition.Id,
			out _);
		if (!staged)
		{
			applied |= ApplyTargetEffects(caster, definition, initialContext);
		}
		applied |= ApplyDaoTaiAbsoluteTerrainDamage(caster, definition, centerTile);

		XjJinDanCombatApi.TryPlayPrimarySpellAnimation(initialContext, definition, out _);
		XjJinDanCombatApi.TryPlaySupplementalVisuals(centerTile, initialContext.Targets, definition, out _);
		return staged || applied;
	}

	private static bool TryExecuteCommonLeiFa(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		XjJinDanCombatApi.TryPlayPrimarySpellAnimation(context, definition, out _);
		bool applied = ApplyTargetEffects(caster, definition, context);
		applied |= ApplyTerrainEffect(caster, definition, context.CenterTile);
		XjJinDanCombatApi.TryPlaySupplementalVisuals(context.CenterTile, context.Targets, definition, out _);
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
			caster,
			strikeTile,
			definition.TerrainEffect,
			definition.TerrainDurationSeconds,
			definition.Id,
			out _);
		applied |= ApplyDaoTaiAbsoluteTerrainDamage(caster, definition, strikeTile);
		XjJinDanCombatApi.TryPlaySupplementalVisuals(strikeTile, strikeContext.Targets, definition, out _);
		return applied;
	}

	private static bool TryExecuteBinFengTuCi(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		bool applied = false;
		// 三个冰封落点允许地形/特效重叠，但同一人物在一次施法中只结算一次
		// 伤害与控制，避免道胎扩大后的局部圆域互相覆盖造成三重命中。
		HashSet<long> affectedActorIds = new HashSet<long>();
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
				XjDaoTaiSpellScale.ResolveTerrainRadius(caster, definition));
			XjJinDanDaoSpellTargetContext impactContext = new XjJinDanDaoSpellTargetContext(impactTile, localTargets);
			applied |= ApplyTargetEffects(caster, definition, impactContext, XjDaoTaiSpellScale.ResolveTerrainRadius(caster, definition), affectedActorIds);
			applied |= ApplyTerrainEffect(caster, definition, impactTile);
		}

		XjJinDanCombatApi.TryPlaySupplementalVisuals(context.CenterTile, context.Targets, definition, out _);
		return applied;
	}

	private static bool TryExecuteGengJinXiaoGuFeng(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		Actor target = TryGetFirstTarget(context);
		if (target?.data == null) return false;

		// 削故锋的视觉是一柄飞剑直取目标；伤害仍走统一真君法术结算，
		// 不让素材投射物自己再结算一次，避免双伤害。
		XjLongGengFlyingSwordSystem.TrySpawnSwordProjectileVisual(caster, target);
		bool applied = ApplyTargetEffects(caster, definition, context);
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
		applied |= ApplyTerrainEffect(caster, definition, context.CenterTile);
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
				XjDaoTaiSpellScale.ResolveTargetRadius(caster, definition));
			bool fallbackApplied = ApplyTargetEffects(caster, definition, fallbackContext);
			fallbackApplied |= ApplyTerrainEffect(caster, definition, context.CenterTile);
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
					XjDaoTaiSpellScale.ResolveTargetRadius(caster, definitionCopy));
				ApplyTargetEffects(caster, definitionCopy, impactContext);
				ApplyTerrainEffect(caster, definitionCopy, impactTile);
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
			XjDaoTaiSpellScale.ResolveTargetRadius(caster, definition));
		bool applied = ApplyTargetEffects(caster, definition, immediateContext);
		applied |= ApplyTerrainEffect(caster, definition, context.CenterTile);
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
		return ApplyTargetEffects(caster, definition, context, XjDaoTaiSpellScale.ResolveTargetRadius(caster, definition));
	}

	private static bool ApplyTargetEffects(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context,
		int effectRadius)
	{
		return ApplyTargetEffects(caster, definition, context, effectRadius, null);
	}

	private static bool ApplyTargetEffects(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context,
		int effectRadius,
		HashSet<long> affectedActorIds)
	{
		bool applied = false;
		for (int i = 0; i < context.Targets.Count; i++)
		{
			Actor target = context.Targets[i];
			if (target?.data == null || !IsTargetWithinRadius(target, context.CenterTile, effectRadius))
			{
				continue;
			}

			if (affectedActorIds != null)
			{
				long actorId = GetActorId(target);
				if (actorId > 0L && !affectedActorIds.Add(actorId))
				{
					continue;
				}
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
		if (XjTaiYinNonAggressionPolicy.ShouldBlockIncomingAttack(caster, target)) return false;
		bool applied = TryApplyDamage(caster, target, definition);
		if (!string.IsNullOrWhiteSpace(definition.StatusEffectId) && definition.StatusDurationSeconds > 0f)
		{
			applied |= XjJinDanCombatApi.TryApplyStatus(caster, target, definition.StatusEffectId, definition.StatusDurationSeconds, definition.Id, out _);
		}

		float impactHold = Mathf.Max(definition.FloatDurationSeconds, definition.SmashDurationSeconds);
		if (impactHold > 0f)
		{
			applied |= XjJinDanCombatApi.TryApplyImpactHold(caster, target, impactHold, definition.Id, out _);
		}

		return applied;
	}

	/// <summary>
	/// Excel 原著神通中不能仅靠 Damage/Status 字段表达的效果统一在此收口。
	/// 这里只读取本次已选定目标，不做额外附近扫描；所有主动仍由上层30秒独立CD
	/// 与8秒全局施法间隔控制。
	/// </summary>
	private static bool ApplyCanonicalPostEffects(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		bool applied = false;
		switch (definition.Id)
		{
			case "SuiQiAnTianYang":
			case "ZiQiKunChenXiu":
				// 闇天殃：神通混乱、法器失色失效；坤辰修：打断施法、灵器打回原形。
				// 玄鉴用统一短时压制层表达，不删除装备、不永久改神通。
				for (int i = 0; i < context.Targets.Count; i++)
				{
					Actor target = context.Targets[i];
					if (target?.data == null || XjTaiYinNonAggressionPolicy.ShouldBlockIncomingAttack(caster, target)) continue;
					XjCanonicalShenTongMechanicsSystem.ApplyShenTongAndArtifactSuppression(
						target, Mathf.Max(1f, definition.StatusDurationSeconds), definition.Id);
					applied = true;
				}
				break;

			case "XiQiQiDaiYe":
				// 乞代夜：收束受到的光、火类攻击；遁速转化保留为后续专门移动Buff接口。
				XjCanonicalShenTongMechanicsSystem.ApplyQiDaiYe(caster, 10f);
				applied = true;
				break;

			case "HanQiSongShangXue":
				// 松上雪：寒雪朔风加持法术剑光并抵御伤势。
				XjCanonicalShenTongMechanicsSystem.ApplySongShangXue(caster, 12f);
				applied = true;
				break;

			case "QingXuanFuQingShan":
				// 伏青山：越战越强，短时最多叠五层；不把它做成永久成长。
				XjCanonicalShenTongMechanicsSystem.ApplyFuQingShan(caster, 14f);
				applied = true;
				break;

			case "ShangYiSheShouWang":
				// 射狩王只交换身体位置，不复制伤害/状态。
				Actor swapTarget = TryGetFirstTarget(context);
				applied |= XjCanonicalShenTongMechanicsSystem.TrySwapPositions(caster, swapTarget);
				break;

			case "ZhiBoJianKuangRang":
				// 僭劻勷原著“分隔四方、斩断气息、使阵不成阵”。WorldBox无阵法单击拆解接口，
				// 先落实为短时施法/法器断联与控场，不凭空删除宗门大阵实体。
				for (int i = 0; i < context.Targets.Count; i++)
				{
					Actor target = context.Targets[i];
					if (target?.data == null || XjTaiYinNonAggressionPolicy.ShouldBlockIncomingAttack(caster, target)) continue;
					XjCanonicalShenTongMechanicsSystem.ApplyShenTongSuppression(target, 1.8f);
					applied = true;
				}
				break;
		}
		return applied;
	}

	private static bool ApplyTargetStatuses(
		Actor caster,
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
			if (target?.data != null && !XjTaiYinNonAggressionPolicy.ShouldBlockIncomingAttack(caster, target))
			{
				applied |= XjJinDanCombatApi.TryApplyStatus(caster, target, definition.StatusEffectId, definition.StatusDurationSeconds, definition.Id, out _);
			}
		}

		return applied;
	}

	private static bool ApplyTerrainEffect(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		WorldTile centerTile)
	{
		bool applied = definition.TerrainRadius > 0
			&& !string.IsNullOrWhiteSpace(definition.TerrainEffect)
			&& XjJinDanCombatApi.TryApplyTerrainEffect(
				caster,
				centerTile,
				definition.TerrainEffect,
				XjDaoTaiSpellScale.ResolveTerrainRadius(caster, definition),
				definition.TerrainDurationSeconds,
				definition.Id,
				out _);
		applied |= ApplyDaoTaiAbsoluteTerrainDamage(caster, definition, centerTile);
		return applied;
	}

	private static bool ApplyDaoTaiAbsoluteTerrainDamage(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		WorldTile centerTile)
	{
		if (!XjDaoTaiSpellScale.IsDaoTaiActor(caster)
			|| centerTile == null
			|| !string.Equals(definition.Id, "CommonLeiFa", StringComparison.Ordinal)
			|| string.Equals(definition.TargetMode, "Self", StringComparison.Ordinal))
		{
			return false;
		}

		return XjJinDanCombatApi.TryApplyDaoTaiTsarBombTerrainEffect(
			centerTile,
			caster,
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
		catch (System.Exception xjCaught426_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanDaoSpellExecutor.cs:426", xjCaught426_1);
			
			return false;
		}
	}

	private static bool TryApplyDamage(Actor caster, Actor target, in XjJinDanDaoSpellDefinition definition)
	{
		if (caster?.data == null || target?.data == null || definition.DamageMultiplier <= 0f)
		{
			return false;
		}

		float damage = Mathf.Max(1f, XjJinDanCombatApi.GetBaseDamage(caster) * XjDaoTaiSpellScale.ResolveDamageMultiplier(caster, definition.DamageMultiplier));
		if (string.Equals(definition.Id, "XiQiWeiQueHua", StringComparison.Ordinal)
			&& XjActorAccessor.TryGetString(target, XjActorDataKeys.DaoTu, out string targetDaoTu))
		{
			// 原著“杀寒止水”：只对寒炁及五水道途增加本次法术威能，不改永久克制表。
			string d = (targetDaoTu ?? string.Empty).Trim();
			if (d is "寒炁" or "坎水" or "渌水" or "合水" or "府水" or "牝水") damage *= 1.25f;
		}
		if (definition.SameRealmMaxHealthDamageRatio > 0f
			&& XjJinDanDaoSpellRuntime.IsJinDanActor(caster)
			&& XjJinDanDaoSpellRuntime.IsJinDanActor(target))
		{
			damage = Mathf.Max(1f, XjJinDanCombatApi.GetMaxHealth(target) * definition.SameRealmMaxHealthDamageRatio);
		}

		return XjJinDanCombatApi.TryDamageActor(caster, target, damage, definition.Id, out _);
	}

	private static long GetActorId(Actor actor)
	{
		try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
		catch { return 0L; }
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
