using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 第一、二批领域神通统一运行器。
///
/// 领域不是新属性，也不写入存档。这里只保存施术者ID、领域类型、中心坐标与
/// 三个时间点。全部领域共用一个有界脉冲调度器，不创建独立MonoBehaviour，
/// 不每帧刷新圆形格子，不长期持有Actor引用。
/// </summary>
internal static class XjDomainSkillRuntime
{
	private sealed class ActiveDomain
	{
		internal long CasterId;
		internal XjDomainSkillDefinition Definition;
		internal int RealmTier;
		internal int CenterX;
		internal int CenterY;
		internal int Radius;
		internal int MaxTargets;
		internal float EndAt;
		internal float NextPulseAt;
		internal float AccumulatedIncomingDamage;
		internal int OriginX;
		internal int OriginY;
		internal int LastVisualCenterX;
		internal int LastVisualCenterY;
		internal int NextVisualRing;
		internal float NextVisualStepAt;
		internal float NextBoundaryRefreshAt;
	}

	private const int MaxActiveDomains = 12;
	private const int MaxLocalCandidates = 64;
	private const float PulseIntervalSeconds = 2f;
	private const float RuntimeCadenceSeconds = 0.5f;
	private const float BoundaryRefreshSeconds = 1f;
	private const float OpeningVisualStepSeconds = 0.12f;
	private const int OpeningRingsPerCadence = 4;
	private const int FieldFlashDurationTicks = 14;
	private const int PulseFlashDurationTicks = 9;
	private const int CleanupFlashDurationTicks = 1;

	private static readonly List<ActiveDomain> Active = new List<ActiveDomain>(MaxActiveDomains);
	private static readonly Dictionary<long, float> CooldownReadyAt = new Dictionary<long, float>();
	private static readonly Dictionary<int, List<Vector2Int>[]> VisualRingsByRadius = new Dictionary<int, List<Vector2Int>[]>();
	private static readonly HashSet<string> LoggedErrors = new HashSet<string>(StringComparer.Ordinal);
	private static int _cursor;
	private static float _nextRuntimeTickAt;

	internal static bool HasActive => Active.Count > 0;
	internal static int ActiveCount => Active.Count;

	internal static bool BindCombatTrigger(ActorTrait trait)
	{
		if (trait == null) return false;
		try
		{
			BaseAugmentationAsset augmentation = (BaseAugmentationAsset)trait;
			AttackAction callback = OnAttackTarget;
			AttackAction current = augmentation.action_attack_target;
			if (ContainsCallback(current, callback)) return true;
			augmentation.action_attack_target = (AttackAction)Delegate.Combine(current, callback);
			return true;
		}
		catch (Exception ex)
		{
			LogOnce("bind", ex);
			return false;
		}
	}

	internal static bool OnAttackTarget(BaseSimObject pSelf, BaseSimObject pTarget, WorldTile pTile = null)
	{
		try
		{
			if (pSelf == null || !pSelf.isActor()) return true;
			Actor caster = pSelf.a;
			Actor target = pTarget != null && pTarget.isActor() ? pTarget.a : null;
			WorldTile targetTile = pTile;
			try { targetTile = pTarget?.current_tile ?? pTile; } catch { }
			TryActivate(caster, target, targetTile);
		}
		catch (Exception ex)
		{
			LogOnce("attack_callback", ex);
		}
		return true;
	}

	internal static bool TryActivate(Actor caster, Actor directTarget, WorldTile fallbackTile)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled
			|| !XjSafeCore.IsAliveActor(caster)
			|| XjClosedCultivationGuard.IsInClosedCultivation(caster)
			|| !IsHostile(caster, directTarget))
		{
			return false;
		}

		int realmTier = XjRealmSuppression.GetRealmTier(caster);
		if (realmTier < XjRealmSuppression.TierZiFu
			|| !XjDomainSkillCatalog.TryResolve(caster, out XjDomainSkillDefinition definition))
		{
			return false;
		}

		long casterId = GetActorId(caster);
		if (casterId <= 0L || FindActiveIndex(casterId) >= 0 || Active.Count >= MaxActiveDomains)
		{
			return false;
		}

		float now = Time.time;
		if (CooldownReadyAt.TryGetValue(casterId, out float readyAt) && now < readyAt)
		{
			return false;
		}

		WorldTile centerTile = definition.FollowCaster ? TryGetCurrentTile(caster) : TryGetCurrentTile(directTarget) ?? fallbackTile;
		if (centerTile == null) return false;

		Vector2Int center = centerTile.pos;
		ActiveDomain domain = new ActiveDomain
		{
			CasterId = casterId,
			Definition = definition,
			RealmTier = realmTier,
			CenterX = center.x,
			CenterY = center.y,
			Radius = definition.ResolveRadius(realmTier),
			MaxTargets = definition.ResolveMaxTargets(realmTier),
			EndAt = now + definition.ResolveDurationSeconds(realmTier),
			NextPulseAt = now + (definition.LargeDomain ? 1.5f : 1.0f),
			AccumulatedIncomingDamage = 0f,
			OriginX = center.x,
			OriginY = center.y,
			LastVisualCenterX = center.x,
			LastVisualCenterY = center.y,
			NextVisualRing = 1,
			NextVisualStepAt = now + OpeningVisualStepSeconds,
			NextBoundaryRefreshAt = now + BoundaryRefreshSeconds,
		};
		Active.Add(domain);
		CooldownReadyAt[casterId] = now + definition.ResolveCooldownSeconds(realmTier);
		FlashDomainRing(centerTile, domain, 0, FieldFlashDurationTicks);
		TryFlash(caster);
		return true;
	}

	internal static void Tick(int domainBudget)
	{
		if (Active.Count == 0) return;
		float now = Time.time;
		if (now < _nextRuntimeTickAt) return;
		_nextRuntimeTickAt = now + RuntimeCadenceSeconds;

		// 活跃领域硬上限只有12个；先做一次轻量生命周期清理，避免死亡施术者
		// 或已到期领域占据容量。到期火域在移除前结算一次终结脉冲。
		for (int i = Active.Count - 1; i >= 0; i--)
		{
			ActiveDomain domain = Active[i];
			bool resolved = XjScheduler.ResolveActor(domain.CasterId, out Actor caster);
			if (!resolved
				|| !XjSafeCore.IsAliveActor(caster)
				|| XjClosedCultivationGuard.IsInClosedCultivation(caster))
			{
				ClearDomainVisual(domain, resolved ? caster : null);
				Active.RemoveAt(i);
				continue;
			}
			if (now < domain.EndAt) continue;
			if (domain.Definition.Kind == XjDomainSkillKind.TianXiaHan
				|| domain.Definition.Kind == XjDomainSkillKind.ChiDuanZu)
			{
				Pulse(domain, caster, true);
			}
			ClearDomainVisual(domain, caster);
			Active.RemoveAt(i);
		}
		if (Active.Count == 0) return;

		// 视觉与伤害脉冲分离。最多12个领域，每0.5秒只更新少量预计算圆环，
		// 不创建贴图对象，也不依赖紫府/金丹常驻光环。
		for (int i = 0; i < Active.Count; i++)
		{
			ActiveDomain domain = Active[i];
			if (XjScheduler.ResolveActor(domain.CasterId, out Actor caster)
				&& XjSafeCore.IsAliveActor(caster))
			{
				UpdateDomainVisual(domain, caster, now);
			}
		}

		int budget = Math.Max(1, domainBudget);
		int checkedCount = 0;
		int pulsed = 0;
		while (checkedCount < Active.Count && pulsed < budget && Active.Count > 0)
		{
			if (_cursor >= Active.Count) _cursor = 0;
			ActiveDomain domain = Active[_cursor];
			_cursor = (_cursor + 1) % Math.Max(1, Active.Count);
			checkedCount++;
			if (now < domain.NextPulseAt) continue;
			if (!XjScheduler.ResolveActor(domain.CasterId, out Actor caster) || !XjSafeCore.IsAliveActor(caster)) continue;
			domain.NextPulseAt = now + PulseIntervalSeconds;
			Pulse(domain, caster, false);
			pulsed++;
		}
	}

	internal static void ApplyCombatModifiers(
		ref float damage,
		Actor attacker,
		Actor defender,
		int attackerTier)
	{
		if (damage <= 0f || Active.Count == 0 || attacker?.data == null || defender?.data == null) return;

		float outgoingMultiplier = 1f;
		float incomingMultiplier = 1f;
		for (int i = 0; i < Active.Count; i++)
		{
			ActiveDomain domain = Active[i];
			if (!XjScheduler.ResolveActor(domain.CasterId, out Actor caster) || !XjSafeCore.IsAliveActor(caster)) continue;
			WorldTile center = ResolveCenterTile(domain, caster);
			if (center == null) continue;

			if (domain.Definition.Kind == XjDomainSkillKind.MingDiGuanYuan
				&& IsAlly(caster, attacker)
				&& IsInside(attacker, center, domain.Radius))
			{
				outgoingMultiplier = Math.Max(outgoingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 1.18f : 1.15f);
			}

			if (!IsAlly(caster, defender) || !IsInside(defender, center, domain.Radius)) continue;
			switch (domain.Definition.Kind)
			{
				case XjDomainSkillKind.XuanYinCanYi:
					incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.65f : 0.72f);
					break;
				case XjDomainSkillKind.MingDiGuanYuan:
					incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.80f : 0.85f);
					break;
				case XjDomainSkillKind.ZaiZheHui when attackerTier >= XjRealmSuppression.TierZiFu:
					incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.60f : 0.70f);
					break;
				case XjDomainSkillKind.YangShengZhu:
					incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.70f : 0.78f);
					break;
				case XjDomainSkillKind.RuZhongZhuo:
					incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.78f : 0.84f);
					break;
				case XjDomainSkillKind.BingHongXia:
					incomingMultiplier = Math.Min(incomingMultiplier, attackerTier >= XjRealmSuppression.TierZiFu
						? (domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.68f : 0.75f)
						: (domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.80f : 0.86f));
					break;
				case XjDomainSkillKind.QingQiYunJing:
					incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.80f : 0.86f);
					break;
				case XjDomainSkillKind.LuoChaHai:
					incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.82f : 0.88f);
					break;
			}
		}

		damage *= outgoingMultiplier * incomingMultiplier;
	}

	internal static void RecordResolvedIncomingDamage(Actor defender, float resolvedDamage)
	{
		if (defender?.data == null || resolvedDamage <= 0f || Active.Count == 0) return;
		long defenderId = GetActorId(defender);
		if (defenderId <= 0L) return;
		for (int i = 0; i < Active.Count; i++)
		{
			ActiveDomain domain = Active[i];
			if (domain.CasterId == defenderId
				&& (domain.Definition.Kind == XjDomainSkillKind.TianXiaHan
					|| domain.Definition.Kind == XjDomainSkillKind.ChiDuanZu))
			{
				domain.AccumulatedIncomingDamage = Math.Min(1000000000f, domain.AccumulatedIncomingDamage + resolvedDamage);
				return;
			}
		}
	}

	internal static void RemoveActor(long actorId)
	{
		if (actorId <= 0L) return;
		CooldownReadyAt.Remove(actorId);
		for (int i = Active.Count - 1; i >= 0; i--)
		{
			if (Active[i].CasterId != actorId) continue;
			ClearDomainVisual(Active[i], null);
			Active.RemoveAt(i);
		}
		if (_cursor >= Active.Count) _cursor = 0;
	}

	internal static void Clear()
	{
		for (int i = Active.Count - 1; i >= 0; i--) ClearDomainVisual(Active[i], null);
		Active.Clear();
		CooldownReadyAt.Clear();
		VisualRingsByRadius.Clear();
		LoggedErrors.Clear();
		_cursor = 0;
		_nextRuntimeTickAt = 0f;
	}

	private static void Pulse(ActiveDomain domain, Actor caster, bool finalPulse)
	{
		WorldTile center = ResolveCenterTile(domain, caster);
		if (center == null) return;
		if (!finalPulse) FlashDomainPulse(center, domain);

		IReadOnlyList<Actor> enemies = CollectEnemies(caster, center, domain.Radius, domain.MaxTargets);
		int enemyLimit = enemies.Count;
		float baseDamage = Math.Max(1f, XjJinDanCombatApi.GetBaseDamage(caster));
		float pulseMultiplier = domain.Definition.ResolvePulseDamageMultiplier(domain.RealmTier);

		switch (domain.Definition.Kind)
		{
			case XjDomainSkillKind.XuanYinCanYi:
				for (int i = 0; i < enemyLimit; i++) ApplyControl(enemies[i], 0.45f, true);
				break;
			case XjDomainSkillKind.MingDiGuanYuan:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(enemies[i], 0.25f, true);
				}
				break;
			case XjDomainSkillKind.ZhiYangXuanLei:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(enemies[i], 0.35f, false);
					TryFlash(enemies[i]);
				}
				break;
			case XjDomainSkillKind.ZaiZheHui:
				for (int i = 0; i < enemyLimit; i++)
				{
					XjActorAggroBridge.ClearTargets(enemies[i]);
					TryFlash(enemies[i]);
				}
				break;
			case XjDomainSkillKind.ZhuLiaoHui:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(enemies[i], 0.65f, true);
				}
				break;
			case XjDomainSkillKind.GuiLiuChu:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(enemies[i], 0.25f, false);
				}
				HealActorByRatio(caster, domain.Definition.ResolveHealRatio(domain.RealmTier), domain.Definition.DisplayName);
				break;
			case XjDomainSkillKind.TianXiaHan:
				float accumulatedScale = Math.Min(0.75f, domain.AccumulatedIncomingDamage / Math.Max(1f, baseDamage * 4f));
				float finalScale = finalPulse ? 1.35f : 1f;
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier * (1f + accumulatedScale) * finalScale, domain.Definition.DisplayName);
				}
				break;
			case XjDomainSkillKind.JiaoLuoYuan:
				for (int i = 0; i < enemyLimit; i++) ApplyControl(enemies[i], 0.75f, true);
				break;
			case XjDomainSkillKind.YangShengZhu:
				IReadOnlyList<Actor> allies = CollectAllies(caster, center, domain.Radius, domain.MaxTargets);
				float healRatio = domain.Definition.ResolveHealRatio(domain.RealmTier);
				for (int i = 0; i < allies.Count; i++) HealActorByRatio(allies[i], healRatio, domain.Definition.DisplayName);
				break;
			case XjDomainSkillKind.ChiDuanZu:
				float chiDuanScale = Math.Min(0.60f, domain.AccumulatedIncomingDamage / Math.Max(1f, baseDamage * 5f));
				float chiDuanFinal = finalPulse ? 1.50f : 1f;
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier * (1f + chiDuanScale) * chiDuanFinal, domain.Definition.DisplayName);
					ApplyControl(enemies[i], 0.30f, false);
					TryApplyStatus(enemies[i], "burning", 2.5f, domain.Definition.DisplayName);
				}
				break;
			case XjDomainSkillKind.WeiCongXian:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(enemies[i], 0.60f, true);
				}
				break;
			case XjDomainSkillKind.RuZhongZhuo:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(enemies[i], 0.35f, true);
				}
				break;
			case XjDomainSkillKind.BingHongXia:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					TryApplyStatus(enemies[i], "burning", 2f, domain.Definition.DisplayName);
				}
				break;
			case XjDomainSkillKind.QingQiYunJing:
				for (int i = 0; i < enemyLimit; i++) ApplyControl(enemies[i], 0.45f, true);
				break;
			case XjDomainSkillKind.BuKongJie:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(enemies[i], 0.50f, true);
				}
				break;
			case XjDomainSkillKind.LuoChaHai:
				IReadOnlyList<Actor> luoChaAllies = CollectAllies(caster, center, domain.Radius, domain.MaxTargets);
				float luoChaHeal = domain.Definition.ResolveHealRatio(domain.RealmTier);
				for (int i = 0; i < luoChaAllies.Count; i++) HealActorByRatio(luoChaAllies[i], luoChaHeal, domain.Definition.DisplayName);
				break;
		}
	}

	private static IReadOnlyList<Actor> CollectEnemies(Actor caster, WorldTile center, int radius, int maxTargets)
	{
		IReadOnlyList<Actor> source = XjJinDanDaoSpellTargeting.CollectLocalImpactTargets(
			caster,
			null,
			center,
			radius);
		if (source.Count == 0) return source;
		List<Actor> result = new List<Actor>(Math.Min(maxTargets, source.Count));
		for (int i = 0; i < source.Count && result.Count < maxTargets; i++)
		{
			Actor candidate = source[i];
			if (IsHostile(caster, candidate)) result.Add(candidate);
		}
		return result;
	}

	private static IReadOnlyList<Actor> CollectAllies(Actor caster, WorldTile center, int radius, int maxTargets)
	{
		List<Actor> result = new List<Actor>(Math.Min(maxTargets, 12));
		if (caster?.data == null || center == null || radius <= 0 || maxTargets <= 0) return result;
		IEnumerable<Actor> source;
		try { source = Finder.getUnitsFromChunk(center, radius, 0f, false); }
		catch { return result; }
		if (source == null) return result;

		Vector2Int centerPos = center.pos;
		int radiusSquared = radius * radius;
		int evaluated = 0;
		HashSet<long> seen = new HashSet<long>();
		foreach (Actor candidate in source)
		{
			if (evaluated++ >= MaxLocalCandidates || result.Count >= maxTargets) break;
			if (!XjSafeCore.IsAliveActor(candidate) || !IsAlly(caster, candidate)) continue;
			WorldTile tile = TryGetCurrentTile(candidate);
			if (tile == null) continue;
			int dx = tile.pos.x - centerPos.x;
			int dy = tile.pos.y - centerPos.y;
			if (dx * dx + dy * dy > radiusSquared) continue;
			long id = GetActorId(candidate);
			if (id > 0L && !seen.Add(id)) continue;
			result.Add(candidate);
		}
		return result;
	}

	private static void ApplyDamage(Actor caster, Actor target, float amount, string source)
	{
		if (amount <= 0f || target?.data == null) return;
		XjJinDanCombatApi.TryDamageActor(caster, target, amount, source, out _);
	}

	private static void HealActorByRatio(Actor actor, float ratio, string source)
	{
		if (ratio <= 0f || !XjSafeCore.IsAliveActor(actor)) return;
		float amount = Math.Max(1f, XjSafeCore.GetMaxHealthSafe(actor, 1f) * ratio);
		// 群体领域治疗不为每个目标生成治疗粒子，避免多人养生主同时展开时
		// 视觉对象数量成倍增长；领域脉冲本身已经提供统一视觉。
		XjSafeCore.HealActorSafe(actor, amount);
	}

	private static void TryApplyStatus(Actor actor, string statusId, float durationSeconds, string source)
	{
		if (!XjSafeCore.IsAliveActor(actor) || string.IsNullOrWhiteSpace(statusId) || durationSeconds <= 0f) return;
		XjJinDanCombatApi.TryApplyStatus(actor, statusId, durationSeconds, source, out _);
	}

	private static void ApplyControl(Actor actor, float waitSeconds, bool clearTarget)
	{
		if (!XjSafeCore.IsAliveActor(actor)) return;
		if (clearTarget) XjActorAggroBridge.ClearTargets(actor);
		try { actor.makeWait(waitSeconds); } catch { }
		TryFlash(actor);
	}

	private static WorldTile ResolveCenterTile(ActiveDomain domain, Actor caster)
	{
		if (domain.Definition.FollowCaster)
		{
			WorldTile casterTile = TryGetCurrentTile(caster);
			if (casterTile != null)
			{
				domain.CenterX = casterTile.pos.x;
				domain.CenterY = casterTile.pos.y;
				return casterTile;
			}
		}
		try { return World.world?.GetTileSimple(domain.CenterX, domain.CenterY); }
		catch { return null; }
	}

	private static void UpdateDomainVisual(ActiveDomain domain, Actor caster, float now)
	{
		if (domain == null) return;
		try
		{
			if (domain.NextVisualRing <= domain.Radius)
			{
				if (now < domain.NextVisualStepAt) return;
				WorldTile origin = TryGetTile(domain.OriginX, domain.OriginY);
				if (origin == null) return;
				int rings = 0;
				while (domain.NextVisualRing <= domain.Radius && rings < OpeningRingsPerCadence)
				{
					FlashDomainRing(origin, domain, domain.NextVisualRing, FieldFlashDurationTicks);
					domain.NextVisualRing++;
					rings++;
				}
				domain.NextVisualStepAt = now + OpeningVisualStepSeconds;
				if (domain.NextVisualRing <= domain.Radius) return;
			}

			if (now < domain.NextBoundaryRefreshAt) return;
			WorldTile center = ResolveCenterTile(domain, caster);
			if (center == null) return;
			domain.LastVisualCenterX = center.pos.x;
			domain.LastVisualCenterY = center.pos.y;
			FlashBoundaryPattern(center, domain, FieldFlashDurationTicks);
			domain.NextBoundaryRefreshAt = now + BoundaryRefreshSeconds;
		}
		catch (Exception ex)
		{
			LogOnce("visual_update", ex);
		}
	}

	private static void FlashDomainPulse(WorldTile center, ActiveDomain domain)
	{
		if (center == null || domain == null) return;
		domain.LastVisualCenterX = center.pos.x;
		domain.LastVisualCenterY = center.pos.y;
		FlashDomainRing(center, domain, domain.Radius, PulseFlashDurationTicks);
		FlashDomainRing(center, domain, Math.Max(0, domain.Radius * 2 / 3), PulseFlashDurationTicks);
		FlashDomainRing(center, domain, Math.Max(0, domain.Radius / 3), PulseFlashDurationTicks);
	}

	private static void FlashBoundaryPattern(WorldTile center, ActiveDomain domain, int durationTicks)
	{
		FlashDomainRing(center, domain, domain.Radius, durationTicks);
		if (domain.Radius >= 3) FlashDomainRing(center, domain, domain.Radius - 1, durationTicks);
	}

	private static void ClearDomainVisual(ActiveDomain domain, Actor caster)
	{
		if (domain == null) return;
		try
		{
			WorldTile last = TryGetTile(domain.LastVisualCenterX, domain.LastVisualCenterY);
			if (last != null) FlashAllDomainRings(last, domain, CleanupFlashDurationTicks);

			if (domain.OriginX != domain.LastVisualCenterX || domain.OriginY != domain.LastVisualCenterY)
			{
				WorldTile origin = TryGetTile(domain.OriginX, domain.OriginY);
				if (origin != null) FlashAllDomainRings(origin, domain, CleanupFlashDurationTicks);
			}

			if (caster != null && domain.Definition.FollowCaster)
			{
				WorldTile current = TryGetCurrentTile(caster);
				if (current != null
					&& (current.pos.x != domain.LastVisualCenterX || current.pos.y != domain.LastVisualCenterY))
				{
					FlashAllDomainRings(current, domain, CleanupFlashDurationTicks);
				}
			}
		}
		catch (Exception ex)
		{
			LogOnce("visual_clear", ex);
		}
	}

	private static void FlashAllDomainRings(WorldTile center, ActiveDomain domain, int durationTicks)
	{
		for (int ring = 0; ring <= domain.Radius; ring++)
		{
			FlashDomainRing(center, domain, ring, durationTicks);
		}
	}

	private static void FlashDomainRing(WorldTile center, ActiveDomain domain, int ringIndex, int durationTicks)
	{
		if (center == null || domain == null || World.world?.flash_effects == null) return;
		List<Vector2Int>[] rings = GetVisualRings(domain.Radius);
		if (rings == null || ringIndex < 0 || ringIndex >= rings.Length) return;
		List<Vector2Int> ring = rings[ringIndex];
		ColorType color = ResolveDomainVisualColor(domain.Definition.Kind);
		for (int i = 0; i < ring.Count; i++)
		{
			Vector2Int offset = ring[i];
			WorldTile tile = TryGetTile(center.pos.x + offset.x, center.pos.y + offset.y);
			if (tile == null) continue;
			World.world.flash_effects.flashPixel(tile, Math.Max(1, durationTicks), color);
		}
	}

	private static List<Vector2Int>[] GetVisualRings(int radius)
	{
		radius = Math.Max(1, radius);
		if (VisualRingsByRadius.TryGetValue(radius, out List<Vector2Int>[] cached)) return cached;
		List<Vector2Int>[] rings = new List<Vector2Int>[radius + 1];
		for (int i = 0; i < rings.Length; i++) rings[i] = new List<Vector2Int>();
		int radiusSquared = radius * radius;
		for (int dx = -radius; dx <= radius; dx++)
		{
			for (int dy = -radius; dy <= radius; dy++)
			{
				int squared = dx * dx + dy * dy;
				if (squared > radiusSquared) continue;
				int ring = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(squared)), 0, radius);
				rings[ring].Add(new Vector2Int(dx, dy));
			}
		}
		VisualRingsByRadius[radius] = rings;
		return rings;
	}

	private static ColorType ResolveDomainVisualColor(XjDomainSkillKind kind)
	{
		return kind switch
		{
			XjDomainSkillKind.GuiLiuChu or XjDomainSkillKind.WeiCongXian => ColorType.Blue,
			XjDomainSkillKind.XuanYinCanYi
				or XjDomainSkillKind.ZhiYangXuanLei
				or XjDomainSkillKind.RuZhongZhuo
				or XjDomainSkillKind.QingQiYunJing
				or XjDomainSkillKind.BuKongJie
				or XjDomainSkillKind.LuoChaHai => ColorType.Purple,
			_ => ColorType.White,
		};
	}

	private static WorldTile TryGetTile(int x, int y)
	{
		try { return World.world?.GetTileSimple(x, y); }
		catch { return null; }
	}

	private static bool IsInside(Actor actor, WorldTile center, int radius)
	{
		WorldTile tile = TryGetCurrentTile(actor);
		if (tile == null || center == null) return false;
		int dx = tile.pos.x - center.pos.x;
		int dy = tile.pos.y - center.pos.y;
		return dx * dx + dy * dy <= radius * radius;
	}

	private static bool IsHostile(Actor caster, Actor target)
	{
		if (!XjSafeCore.IsAliveActor(caster) || !XjSafeCore.IsAliveActor(target) || ReferenceEquals(caster, target)) return false;
		if (IsAlly(caster, target)) return false;
		return TryGetCurrentTile(target) != null;
	}

	private static bool IsAlly(Actor caster, Actor candidate)
	{
		if (caster?.data == null || candidate?.data == null) return false;
		if (ReferenceEquals(caster, candidate)) return true;
		try
		{
			if (caster.city != null && candidate.city != null && caster.city == candidate.city) return true;
			if (caster.clan != null && candidate.clan != null && caster.clan == candidate.clan) return true;
			long casterId = GetActorId(caster);
			long candidateId = GetActorId(candidate);
			if (XjZongMenCultivatorCityIndex.TryGetSectId(casterId, out long casterSectId)
				&& XjZongMenCultivatorCityIndex.TryGetSectId(candidateId, out long candidateSectId)
				&& casterSectId > 0L
				&& casterSectId == candidateSectId)
			{
				return true;
			}
			Kingdom casterKingdom = ((BaseSimObject)caster).kingdom;
			Kingdom candidateKingdom = ((BaseSimObject)candidate).kingdom;
			return casterKingdom != null && candidateKingdom != null && casterKingdom == candidateKingdom;
		}
		catch { return false; }
	}

	private static WorldTile TryGetCurrentTile(Actor actor)
	{
		try { return actor?.data == null ? null : ((BaseSimObject)actor).current_tile; }
		catch { return null; }
	}

	private static int FindActiveIndex(long casterId)
	{
		for (int i = 0; i < Active.Count; i++) if (Active[i].CasterId == casterId) return i;
		return -1;
	}

	private static long GetActorId(Actor actor)
	{
		try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
		catch { return 0L; }
	}

	private static void TryFlash(Actor actor)
	{
		try { actor?.startColorEffect(ActorColorEffect.White); } catch { }
	}

	private static bool ContainsCallback(AttackAction current, AttackAction callback)
	{
		if (current == null || callback == null) return false;
		Delegate[] list = current.GetInvocationList();
		for (int i = 0; i < list.Length; i++)
		{
			Delegate item = list[i];
			if (item != null && item.Method == callback.Method) return true;
		}
		return false;
	}

	private static void LogOnce(string key, Exception ex)
	{
		string errorKey = (key ?? "unknown") + "|" + (ex?.GetType().FullName ?? "UnknownException");
		if (!LoggedErrors.Add(errorKey)) return;
		Debug.LogWarning("[玄鉴][领域神通] " + key + " 失败：" + (ex?.Message ?? "未知异常"));
	}
}
