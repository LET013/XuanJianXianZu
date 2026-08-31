using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Sect;

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
		internal int YuanZhaoSkillMask;
	}

	private sealed class PendingDomainRequest
	{
		internal long CasterId;
		internal long TargetId;
		internal int FallbackX;
		internal int FallbackY;
		internal bool HasFallback;
		internal float ExpiresAt;
	}

	private const int MaxActiveDomains = 12;
	private const int MaxPendingDomains = 48;
	private const int MaxPendingPromotionsPerTick = 2;
	private const float PendingDomainLifetimeSeconds = 15f;
	private const int MaxLocalCandidates = 64;
	private const float PulseIntervalSeconds = 2f;
	private const float RuntimeCadenceSeconds = 0.5f;
	private const float BoundaryRefreshSeconds = 1f;
	private const float OpeningVisualStepSeconds = 0.12f;
	private const int OpeningRingsPerCadence = 4;
	private const int FieldFlashDurationTicks = 14;
	private const int PulseFlashDurationTicks = 9;
	private const int CleanupFlashDurationTicks = 1;
	private const int YuanZhaoMoonSinkBit = 1 << 0;
	private const int YuanZhaoTraceSightBit = 1 << 1;
	private const int YuanZhaoReturnSceneBit = 1 << 2;
	private const int YuanZhaoReturnTrueBit = 1 << 3;
	private const int YuanZhaoStillWaterBit = 1 << 4;

	private static readonly List<ActiveDomain> Active = new List<ActiveDomain>(MaxActiveDomains);
	private static readonly Queue<PendingDomainRequest> Pending = new Queue<PendingDomainRequest>(MaxPendingDomains);
	private static readonly HashSet<long> PendingCasterIds = new HashSet<long>();
	private static readonly List<XjDomainSkillDefinition> DefinitionScratch = new List<XjDomainSkillDefinition>(4);
	private static readonly Dictionary<int, List<Vector2Int>[]> VisualRingsByRadius = new Dictionary<int, List<Vector2Int>[]>();
	private static readonly HashSet<string> LoggedErrors = new HashSet<string>(StringComparer.Ordinal);
	private static readonly XjLocalActorQuery.ContextActorPredicate AllyQueryPredicate = IsDomainAllyForQuery;
	private static int _cursor;
	private static float _nextRuntimeTickAt;

	internal static bool HasActive => Active.Count > 0 || Pending.Count > 0;
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
			try { targetTile = pTarget?.current_tile ?? pTile; } catch (System.Exception xjCaught90) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDomainSkillRuntime.cs:90", xjCaught90); }
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
		return TryActivateInternal(caster, directTarget, fallbackTile, true);
	}

	private static bool TryActivateInternal(Actor caster, Actor directTarget, WorldTile fallbackTile, bool allowQueue)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled
			|| !XjSafeCore.IsAliveActor(caster)
			|| XjClosedCultivationGuard.IsInClosedCultivation(caster)
			|| XjCanonicalShenTongMechanicsSystem.IsShenTongSuppressed(caster)
			|| !IsHostile(caster, directTarget))
		{
			return false;
		}

		int realmTier = XjRealmSuppression.GetRealmTier(caster);
		if (realmTier < XjRealmSuppression.TierZiFu) return false;

		long casterId = GetActorId(caster);
		if (casterId <= 0L || FindActiveIndex(casterId) >= 0) return false;

		DefinitionScratch.Clear();
		if (XjDomainSkillCatalog.CollectDefinitions(caster, DefinitionScratch) <= 0) return false;

		float now = Time.time;
		if (!TrySelectReadyDefinition(
			caster, casterId, now, DefinitionScratch,
			out XjDomainSkillDefinition definition,
			out int yuanZhaoActiveSkillBit,
			out string cooldownKey))
		{
			return false;
		}

		WorldTile centerTile = definition.FollowCaster
			? TryGetCurrentTile(caster)
			: TryGetCurrentTile(directTarget) ?? fallbackTile;
		if (centerTile == null) return false;

		if (Active.Count >= MaxActiveDomains)
		{
			return allowQueue && EnqueuePending(casterId, GetActorId(directTarget), fallbackTile, now);
		}

		return ActivateResolvedDomain(
			caster, casterId, realmTier, centerTile, definition,
			yuanZhaoActiveSkillBit, cooldownKey, now);
	}

	private static bool TrySelectReadyDefinition(
		Actor caster,
		long casterId,
		float now,
		List<XjDomainSkillDefinition> definitions,
		out XjDomainSkillDefinition selected,
		out int yuanZhaoActiveSkillBit,
		out string cooldownKey)
	{
		selected = default;
		yuanZhaoActiveSkillBit = 0;
		cooldownKey = string.Empty;
		if (definitions == null || definitions.Count == 0) return false;

		for (int i = 0; i < definitions.Count; i++)
		{
			XjDomainSkillDefinition candidate = definitions[i];
			int candidateYuanZhaoBit = 0;
			string candidateCooldownKey;
			if (candidate.Kind == XjDomainSkillKind.YuanZhaoShuiYueJing)
			{
				int learnedMask = ResolveYuanZhaoSkillMask(caster);
				if (!TryResolveYuanZhaoCast(casterId, learnedMask, now, out candidateYuanZhaoBit, out candidateCooldownKey))
					continue;
			}
			else
			{
				candidateCooldownKey = BuildDomainCooldownKey(candidate.Kind);
				if (!XjJinDanDaoSpellCooldown.IsReady(casterId, candidateCooldownKey, now)) continue;
			}

			selected = candidate;
			yuanZhaoActiveSkillBit = candidateYuanZhaoBit;
			cooldownKey = candidateCooldownKey;
			return true;
		}
		return false;
	}

	private static bool ActivateResolvedDomain(
		Actor caster,
		long casterId,
		int realmTier,
		WorldTile centerTile,
		in XjDomainSkillDefinition definition,
		int yuanZhaoActiveSkillBit,
		string cooldownKey,
		float now)
	{
		if (caster?.data == null || centerTile == null || Active.Count >= MaxActiveDomains) return false;

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
			YuanZhaoSkillMask = yuanZhaoActiveSkillBit,
		};
		Active.Add(domain);
		XjJinDanDaoSpellCooldown.MarkUsed(casterId, cooldownKey, now, definition.ResolveCooldownSeconds(realmTier));
		FlashDomainRing(centerTile, domain, 0, FieldFlashDurationTicks);
		TryFlash(caster);
		XjCanonicalShenTongVisualSystem.TryPlayDomainOpening(
			caster,
			definition.Kind,
			definition.Kind == XjDomainSkillKind.YuanZhaoShuiYueJing
				? ResolveYuanZhaoSkillName(yuanZhaoActiveSkillBit)
				: definition.DisplayName,
			centerTile,
			domain.Radius);
		return true;
	}

	private static bool EnqueuePending(long casterId, long targetId, WorldTile fallbackTile, float now)
	{
		if (casterId <= 0L) return false;
		if (PendingCasterIds.Contains(casterId)) return true;
		if (Pending.Count >= MaxPendingDomains) return false;

		PendingDomainRequest request = new PendingDomainRequest
		{
			CasterId = casterId,
			TargetId = Math.Max(0L, targetId),
			ExpiresAt = now + PendingDomainLifetimeSeconds,
		};
		if (fallbackTile != null)
		{
			request.HasFallback = true;
			request.FallbackX = fallbackTile.pos.x;
			request.FallbackY = fallbackTile.pos.y;
		}
		Pending.Enqueue(request);
		PendingCasterIds.Add(casterId);
		return true;
	}

	private static void PromotePending(float now)
	{
		int attempts = Math.Min(Pending.Count, MaxPendingPromotionsPerTick);
		for (int i = 0; i < attempts && Active.Count < MaxActiveDomains && Pending.Count > 0; i++)
		{
			PendingDomainRequest request = Pending.Dequeue();
			PendingCasterIds.Remove(request.CasterId);
			if (request.ExpiresAt < now) continue;
			if (!XjScheduler.ResolveActor(request.CasterId, out Actor caster) || !XjSafeCore.IsAliveActor(caster)) continue;
			Actor target = null;
			if (request.TargetId > 0L) XjScheduler.ResolveActor(request.TargetId, out target);
			if (!IsHostile(caster, target)) continue;
			WorldTile fallback = request.HasFallback ? TryGetTile(request.FallbackX, request.FallbackY) : null;
			TryActivateInternal(caster, target, fallback, false);
		}
	}

	internal static void Tick(int domainBudget)
	{
		if (Active.Count == 0 && Pending.Count == 0) return;
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
				|| XjClosedCultivationGuard.IsInClosedCultivation(caster)
				|| XjCanonicalShenTongMechanicsSystem.IsShenTongSuppressed(caster))
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

		// 活跃领域容量出现空位后，以 FIFO 有界提升等待者。保持 12 个活跃领域的
		// 性能硬上限，但第 13 人以后不再因为一次 attack callback 恰逢满额就永久失去机会。
		PromotePending(now);
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

	internal static float GetYuanZhaoDodgeChanceFloor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor) || Active.Count == 0) return 0f;
		long actorId = GetActorId(actor);
		if (actorId <= 0L) return 0f;

		bool activeYuanZhao = false;
		bool fullMoonSink = false;
		for (int i = 0; i < Active.Count; i++)
		{
			ActiveDomain domain = Active[i];
			if (domain.CasterId != actorId || domain.Definition.Kind != XjDomainSkillKind.YuanZhaoShuiYueJing) continue;
			activeYuanZhao = true;
			fullMoonSink |= HasYuanZhaoSkill(domain, YuanZhaoMoonSinkBit);
		}
		if (!activeYuanZhao) return 0f;

		float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, 0f);
		if (maxHealth <= 0.01f) return 0f;
		float healthRatio = Mathf.Clamp01(XjSafeCore.GetHealthSafe(actor, maxHealth) / maxHealth);
		if (healthRatio <= 0.20f) return fullMoonSink ? 100f : 90f;
		// 继承太阴“越伤越藏”的曲线，但非【月沉渊】只取九成上限；
		// 月沉渊本身完整继承三阴玄轮的20%血线100%闪避语义。
		float curve = Mathf.Clamp01((1f - healthRatio) / 0.8f);
		return curve * (fullMoonSink ? 100f : 90f);
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
		int yuanZhaoSelfTier = -1;
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
			if (domain.Definition.Kind == XjDomainSkillKind.YuanZhaoShuiYueJing
				&& HasYuanZhaoSkill(domain, YuanZhaoTraceSightBit)
				&& IsAlly(caster, attacker)
				&& IsInside(attacker, center, domain.Radius))
			{
				// 【照无身】不凭肉眼索敌，而是循战场留痕校正真身；这里只给已照见目标轻度战斗优势，
				// 不做全世界反隐扫描。
				outgoingMultiplier = Math.Max(outgoingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 1.18f : 1.12f);
			}

			if (domain.Definition.Kind == XjDomainSkillKind.NanChouShui
				&& ReferenceEquals(caster, defender)
				&& IsInside(defender, center, domain.Radius))
			{
				// 南惆水“身化灰色魔水/虚化”：只保护施术者本身，不把整个友军都做成虚体。
				incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.68f : 0.76f);
			}

			if (domain.Definition.Kind == XjDomainSkillKind.YuanZhaoShuiYueJing
				&& ReferenceEquals(caster, defender)
				&& IsInside(defender, center, domain.Radius))
			{
				// 水月照真首先护住施术者本真：任一渊照领域都有基础护身，
				// 【影归真】再把“返照真身”推到更高一档。
				yuanZhaoSelfTier = Math.Max(yuanZhaoSelfTier, domain.RealmTier);
				incomingMultiplier = Math.Min(incomingMultiplier,
					domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.82f : 0.88f);
			}

			if (domain.Definition.Kind == XjDomainSkillKind.QingYuYa
				&& IsAlly(caster, defender)
				&& IsInside(defender, center, domain.Radius)
				&& !IsInside(attacker, center, domain.Radius))
			{
				// 青玉崖“隔离削弱外来攻击”：仅当攻击来自危崖之外时削减，
				// 不把原著的“擦边/离崖易伤”擅自扩成常驻增伤。
				incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.68f : 0.76f);
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
				case XjDomainSkillKind.ShiFangJie:
					// 原著仅明确“十方界”为华炁上位神通；当前只落实为
					// 界域护持与轻度空间压制，不擅自补写额外伤害权柄。
					incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.76f : 0.84f);
					break;
				case XjDomainSkillKind.YuanZhaoShuiYueJing when HasYuanZhaoSkill(domain, YuanZhaoReturnTrueBit):
					// 【影归真】把留影重新校回本真，金丹档直接压到约38%减伤；
					// 治疗在领域脉冲中另行结算，不把“恢复”伪装成纯减伤。
					incomingMultiplier = Math.Min(incomingMultiplier, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.62f : 0.72f);
					break;
			}
		}

		damage *= outgoingMultiplier * incomingMultiplier;
		if (yuanZhaoSelfTier >= XjRealmSuppression.TierJinDan && attackerTier <= yuanZhaoSelfTier)
		{
			// 【涵真不坠】：水月境已经展开时，同境及以下的一次攻击不能直接抹去
			// 整具法身。金丹单击封顶38%最大生命，道胎封顶30%；来自更高境的
			// 道胎压制不受此保护，仍交给统一境界压制规则处理。
			float maxHealth = XjSafeCore.GetMaxHealthSafe(defender, 0f);
			if (maxHealth > 0f)
			{
				float capRatio = yuanZhaoSelfTier >= XjRealmSuppression.TierDaoTai ? 0.30f : 0.38f;
				damage = Math.Min(damage, maxHealth * capRatio);
			}
		}
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
		for (int i = Active.Count - 1; i >= 0; i--)
		{
			if (Active[i].CasterId != actorId) continue;
			ClearDomainVisual(Active[i], null);
			Active.RemoveAt(i);
		}
		RemovePendingActor(actorId);
		if (_cursor >= Active.Count) _cursor = 0;
	}

	private static void RemovePendingActor(long actorId)
	{
		if (actorId <= 0L || Pending.Count == 0 || !PendingCasterIds.Contains(actorId)) return;
		int count = Pending.Count;
		for (int i = 0; i < count; i++)
		{
			PendingDomainRequest request = Pending.Dequeue();
			if (request.CasterId != actorId) Pending.Enqueue(request);
		}
		PendingCasterIds.Remove(actorId);
	}

	internal static void Clear()
	{
		for (int i = Active.Count - 1; i >= 0; i--) ClearDomainVisual(Active[i], null);
		Active.Clear();
		Pending.Clear();
		PendingCasterIds.Clear();
		DefinitionScratch.Clear();
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

		bool needsEnemyTargets = domain.Definition.Kind != XjDomainSkillKind.YangShengZhu
			&& domain.Definition.Kind != XjDomainSkillKind.LuoChaHai;
		IReadOnlyList<Actor> enemies = needsEnemyTargets
			? CollectEnemies(caster, center, domain.Radius, domain.MaxTargets)
			: Array.Empty<Actor>();
		int enemyLimit = enemies.Count;
		float baseDamage = Math.Max(1f, XjJinDanCombatApi.GetBaseDamage(caster));
		float pulseMultiplier = domain.Definition.ResolvePulseDamageMultiplier(domain.RealmTier);

		switch (domain.Definition.Kind)
		{
			case XjDomainSkillKind.XuanYinCanYi:
				for (int i = 0; i < enemyLimit; i++) ApplyControl(caster, enemies[i], 0.45f, true);
				break;
			case XjDomainSkillKind.MingDiGuanYuan:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(caster, enemies[i], 0.25f, true);
				}
				break;
			case XjDomainSkillKind.ZhiYangXuanLei:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(caster, enemies[i], 0.35f, false);
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
					ApplyControl(caster, enemies[i], 0.65f, true);
				}
				break;
			case XjDomainSkillKind.GuiLiuChu:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(caster, enemies[i], 0.25f, false);
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
				for (int i = 0; i < enemyLimit; i++) ApplyControl(caster, enemies[i], 0.75f, true);
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
					ApplyControl(caster, enemies[i], 0.30f, false);
					TryApplyStatus(caster, enemies[i], "burning", 2.5f, domain.Definition.DisplayName);
				}
				break;
			case XjDomainSkillKind.WeiCongXian:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(caster, enemies[i], 0.60f, true);
				}
				break;
			case XjDomainSkillKind.RuZhongZhuo:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(caster, enemies[i], 0.35f, true);
				}
				break;
			case XjDomainSkillKind.BingHongXia:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					TryApplyStatus(caster, enemies[i], "burning", 2f, domain.Definition.DisplayName);
				}
				break;
			case XjDomainSkillKind.QingQiYunJing:
				for (int i = 0; i < enemyLimit; i++) ApplyControl(caster, enemies[i], 0.45f, true);
				break;
			case XjDomainSkillKind.BuKongJie:
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(caster, enemies[i], 0.50f, true);
				}
				break;
			case XjDomainSkillKind.LuoChaHai:
				IReadOnlyList<Actor> luoChaAllies = CollectAllies(caster, center, domain.Radius, domain.MaxTargets);
				float luoChaHeal = domain.Definition.ResolveHealRatio(domain.RealmTier);
				for (int i = 0; i < luoChaAllies.Count; i++) HealActorByRatio(luoChaAllies[i], luoChaHeal, domain.Definition.DisplayName);
				break;
			case XjDomainSkillKind.ShiFangJie:
				for (int i = 0; i < enemyLimit; i++) ApplyControl(caster, enemies[i], 0.30f, false);
				break;
			case XjDomainSkillKind.QingYuYa:
				// 原著核心是“危崖封锁、走脱不得”，并未给出持续范围伤害。
				// 因此只结算控制；外来攻击隔离在 ApplyCombatModifiers 中处理。
				for (int i = 0; i < enemyLimit; i++) ApplyControl(caster, enemies[i], 0.55f, true);
				break;
			case XjDomainSkillKind.ManYinGuang:
				// 原著明确为“血火幽域”：定住敌人、血火烧伤并掩盖玄机。
				// 这里仅落实可验证的战斗部分，玄机遮蔽暂不伪造全局术算规则。
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, domain.Definition.DisplayName);
					ApplyControl(caster, enemies[i], 0.55f, true);
					TryApplyStatus(caster, enemies[i], "burning", 2.5f, domain.Definition.DisplayName);
				}
				break;
			case XjDomainSkillKind.DongYuShan:
				// 东羽山：重白之气下沉、凝滞灵机、固化太虚。原著还提到破阵光，
				// 这里不直接删除宗门大阵实体，只对域内敌人实施短时施法断联与强控。
				for (int i = 0; i < enemyLimit; i++)
				{
					ApplyControl(caster, enemies[i], 0.60f, true);
					XjCanonicalShenTongMechanicsSystem.ApplyShenTongSuppression(enemies[i], 0.9f);
				}
				break;
			case XjDomainSkillKind.XiTianYuan:
				// 西天塬：太虚陡绝、雪风隔断灵机，“不渡太虚”。落实为封锁与走脱受阻。
				for (int i = 0; i < enemyLimit; i++) ApplyControl(caster, enemies[i], 0.55f, true);
				break;
			case XjDomainSkillKind.NanChouShui:
				// 南惆水：紫水遮蔽、镇压封锁，伴寒风飞雪；原著未给稳定直伤数值，因此不编造脉冲伤害。
				for (int i = 0; i < enemyLimit; i++) ApplyControl(caster, enemies[i], 0.65f, true);
				break;
			case XjDomainSkillKind.YuanZhaoShuiYueJing:
				bool hasMoonSink = HasYuanZhaoSkill(domain, YuanZhaoMoonSinkBit);
				bool hasReturnScene = HasYuanZhaoSkill(domain, YuanZhaoReturnSceneBit);
				bool hasReturnTrue = HasYuanZhaoSkill(domain, YuanZhaoReturnTrueBit);
				bool hasStillWater = HasYuanZhaoSkill(domain, YuanZhaoStillWaterBit);
				// 坎水一侧的“涵养/回流”落实成真实恢复，而不是只写在道意描述里。
				// 任一水月境每次脉冲恢复3%/5%最大生命；【影归真】提升到7%/10%。
				float yuanZhaoHeal = domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.05f : 0.03f;
				if (hasReturnTrue) yuanZhaoHeal = domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.10f : 0.07f;
				HealActorByRatio(caster, yuanZhaoHeal, hasReturnTrue ? "影归真·涵真" : "水月照真·回澜");
				float yuanZhaoWait = hasStillWater
					? (domain.RealmTier >= XjRealmSuppression.TierJinDan ? 0.70f : 0.55f)
					: hasMoonSink ? 0.32f : 0f;
				for (int i = 0; i < enemyLimit; i++)
				{
					if (yuanZhaoWait > 0f) ApplyControl(caster, enemies[i], yuanZhaoWait, hasMoonSink || hasStillWater);
				}
				if (hasReturnScene && pulseMultiplier > 0f)
				{
					// 【回澜鉴】只返映这一领域附近刚刚形成的“法景”强度，不保存世界历史，
					// 不复制长期召唤、突破、权柄变化，也不会递归复制自身。
					int echoLimit = Math.Min(enemyLimit, domain.RealmTier >= XjRealmSuppression.TierJinDan ? 5 : 3);
					// 五门渊照神通已经拆成独立施法；【回澜鉴】不会再与【照无身】同一次叠放，
					// 因而这里只结算返景自身强度，避免历史上“已学技能被动叠乘”的杂糅。
					for (int i = 0; i < echoLimit; i++)
					{
						ApplyDamage(caster, enemies[i], baseDamage * pulseMultiplier, "回澜鉴·返景");
					}
				}
				break;
		}
	}

	private static IReadOnlyList<Actor> CollectEnemies(Actor caster, WorldTile center, int radius, int maxTargets)
	{
		if (caster?.data == null || center == null || radius <= 0 || maxTargets <= 0) return Array.Empty<Actor>();
		// Preserve the Phase9B target semantics exactly: first cap/sort the area-valid
		// candidates, then apply the stricter hostility filter. Filtering hostility
		// inside XjLocalActorQuery would allow hostile actors beyond the historical
		// area-candidate cap to enter the result and would be a gameplay change.
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
		if (caster?.data == null || center == null || radius <= 0 || maxTargets <= 0) return Array.Empty<Actor>();
		return XjLocalActorQuery.Collect(
			center,
			radius,
			MaxLocalCandidates,
			maxTargets,
			caster,
			AllyQueryPredicate,
			caster);
	}

	private static bool IsDomainAllyForQuery(Actor caster, Actor candidate)
	{
		return XjSafeCore.IsAliveActor(candidate) && IsAlly(caster, candidate);
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

	private static void TryApplyStatus(Actor caster, Actor actor, string statusId, float durationSeconds, string source)
	{
		if (!XjSafeCore.IsAliveActor(caster) || !XjSafeCore.IsAliveActor(actor)
			|| string.IsNullOrWhiteSpace(statusId) || durationSeconds <= 0f) return;
		XjJinDanCombatApi.TryApplyStatus(caster, actor, statusId, durationSeconds, source, out _);
	}

	private static void ApplyControl(Actor caster, Actor actor, float waitSeconds, bool clearTarget)
	{
		if (!XjSafeCore.IsAliveActor(caster) || !XjSafeCore.IsAliveActor(actor)) return;
		XjHighRealmControlService.TryApplyHold(caster, actor, waitSeconds, clearTarget, out _);
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
		catch (System.Exception xjCaught511_5) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDomainSkillRuntime.cs:511", xjCaught511_5);
			 return null; }
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
					// 道胎/大型金丹领域只画隔环，不把扩大后的领域重新变成数千格同帧特效。
					domain.NextVisualRing += domain.Radius >= 26 ? 2 : 1;
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
		if (domain == null) return;
		if (domain.Radius >= 26)
		{
			// 大领域结束时只清边界与三层标志环；flash像素本身会按短tick消退，
			// 无需为了“清理”再遍历整片圆域。
			FlashDomainRing(center, domain, 0, durationTicks);
			FlashDomainRing(center, domain, Math.Max(1, domain.Radius / 3), durationTicks);
			FlashDomainRing(center, domain, Math.Max(2, domain.Radius * 2 / 3), durationTicks);
			FlashBoundaryPattern(center, domain, durationTicks);
			return;
		}
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
			XjDomainSkillKind.GuiLiuChu
				or XjDomainSkillKind.WeiCongXian
				or XjDomainSkillKind.YuanZhaoShuiYueJing => ColorType.Blue,
			XjDomainSkillKind.XuanYinCanYi
				or XjDomainSkillKind.ZhiYangXuanLei
				or XjDomainSkillKind.RuZhongZhuo
				or XjDomainSkillKind.QingQiYunJing
				or XjDomainSkillKind.BuKongJie
				or XjDomainSkillKind.LuoChaHai
				or XjDomainSkillKind.ShiFangJie => ColorType.Purple,
			_ => ColorType.White,
		};
	}

	private static string BuildDomainCooldownKey(XjDomainSkillKind kind)
	{
		return "DomainSkill:" + ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static bool TryResolveYuanZhaoCast(
		long casterId, int learnedMask, float now, out int selectedBit, out string cooldownKey)
	{
		selectedBit = 0;
		cooldownKey = string.Empty;
		if (casterId <= 0L || learnedMask == 0) return false;

		// 固定顺序只决定“同一时刻多门都就绪时先尝试谁”；每门各自30秒独立CD，
		// 共用 XjJinDanDaoSpellCooldown 的8秒全法术门控，因此会自然轮转而不会同帧齐放。
		for (int ordinal = 0; ordinal < 5; ordinal++)
		{
			int bit;
			string spellId;
			switch (ordinal)
			{
				case 0: bit = YuanZhaoMoonSinkBit; spellId = "YuanZhao:月沉渊"; break;
				case 1: bit = YuanZhaoTraceSightBit; spellId = "YuanZhao:照无身"; break;
				case 2: bit = YuanZhaoReturnSceneBit; spellId = "YuanZhao:回澜鉴"; break;
				case 3: bit = YuanZhaoReturnTrueBit; spellId = "YuanZhao:影归真"; break;
				default: bit = YuanZhaoStillWaterBit; spellId = "YuanZhao:一泓寂"; break;
			}
			if ((learnedMask & bit) == 0 || !XjJinDanDaoSpellCooldown.IsReady(casterId, spellId, now)) continue;
			selectedBit = bit;
			cooldownKey = spellId;
			return true;
		}
		return false;
	}

	private static string ResolveYuanZhaoSkillName(int bit)
	{
		if (bit == YuanZhaoMoonSinkBit) return "月沉渊";
		if (bit == YuanZhaoTraceSightBit) return "照无身";
		if (bit == YuanZhaoReturnSceneBit) return "回澜鉴";
		if (bit == YuanZhaoReturnTrueBit) return "影归真";
		if (bit == YuanZhaoStillWaterBit) return "一泓寂";
		return string.Empty;
	}

	private static bool HasYuanZhaoSkill(ActiveDomain domain, int bit)
	{
		return domain != null && (domain.YuanZhaoSkillMask & bit) != 0;
	}

	private static int ResolveYuanZhaoSkillMask(Actor actor)
	{
		if (actor?.data == null) return 0;
		int mask = 0;
		string[] learned = XjXianJiAccessor.ReadRawIds(actor);
		for (int i = 0; i < learned.Length; i++)
		{
			switch ((learned[i] ?? string.Empty).Trim())
			{
				case "月沉渊": mask |= YuanZhaoMoonSinkBit; break;
				case "照无身": mask |= YuanZhaoTraceSightBit; break;
				case "回澜鉴": mask |= YuanZhaoReturnSceneBit; break;
				case "影归真": mask |= YuanZhaoReturnTrueBit; break;
				case "一泓寂": mask |= YuanZhaoStillWaterBit; break;
			}
		}
		return mask;
	}

	private static WorldTile TryGetTile(int x, int y)
	{
		try { return World.world?.GetTileSimple(x, y); }
		catch (System.Exception xjCaught658_8) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDomainSkillRuntime.cs:658", xjCaught658_8);
			 return null; }
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
			if (XjSectAuthorityStore.TryGetSectId(casterId, out long casterSectId)
				&& XjSectAuthorityStore.TryGetSectId(candidateId, out long candidateSectId)
				&& casterSectId > 0L
				&& casterSectId == candidateSectId)
			{
				return true;
			}
			Kingdom casterKingdom = ((BaseSimObject)caster).kingdom;
			Kingdom candidateKingdom = ((BaseSimObject)candidate).kingdom;
			return casterKingdom != null && candidateKingdom != null && casterKingdom == candidateKingdom;
		}
		catch (System.Exception xjCaught698_9) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDomainSkillRuntime.cs:698", xjCaught698_9);
			 return false; }
	}

	private static WorldTile TryGetCurrentTile(Actor actor)
	{
		try { return actor?.data == null ? null : ((BaseSimObject)actor).current_tile; }
		catch (System.Exception xjCaught704_10) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDomainSkillRuntime.cs:704", xjCaught704_10);
			 return null; }
	}

	private static int FindActiveIndex(long casterId)
	{
		for (int i = 0; i < Active.Count; i++) if (Active[i].CasterId == casterId) return i;
		return -1;
	}

	private static long GetActorId(Actor actor)
	{
		try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
		catch (System.Exception xjCaught716_11) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDomainSkillRuntime.cs:716", xjCaught716_11);
			 return 0L; }
	}

	private static void TryFlash(Actor actor)
	{
		try { actor?.startColorEffect(ActorColorEffect.White); } catch (System.Exception xjCaught721) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDomainSkillRuntime.cs:721", xjCaught721); }
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
