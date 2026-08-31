using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using UnityEngine;
using System.Collections.Generic;
using System;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjJinDanDaoSpellRuntime
{
	private static readonly HashSet<long> CastingActorIds = new HashSet<long>();
	private static readonly HashSet<string> LoggedRuntimeErrors = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>
	/// 使用原生攻击事件携带的明确目标触发真君道法，避免依赖一秒轮询时
	/// 短暂存在的 attack_target 字段。
	/// </summary>
	internal static bool OnAttackTarget(BaseSimObject pSelf, BaseSimObject pTarget, WorldTile pTile = null)
	{
		try
		{
			if (pSelf == null || !pSelf.isActor())
			{
				return true;
			}

			Actor caster = pSelf.a;
			Actor directTarget = pTarget != null && pTarget.isActor() ? pTarget.a : null;
			// 纯视觉的原著身法/神职/气象提示不占主动施法槽，也不写全局8秒技能间隔。
			XjCanonicalShenTongVisualSystem.TryPlayPassiveCombatCue(caster);
			XjCanonicalShenTongMechanicsSystem.RefreshPassiveCombatStates(caster);
			WorldTile targetTile = pTile;
			try
			{
				targetTile = pTarget?.current_tile ?? pTile;
			}
			catch (Exception ex)
			{
				LogOnce("target_tile", ex);
			}

			TryTrigger(caster, directTarget, targetTile);
		}
		catch (Exception ex)
		{
			// Trait callbacks must never interrupt the original attack action.
			LogOnce("attack_callback", ex);
		}

		return true;
	}

	internal static bool BindCombatTrigger(ActorTrait trait)
	{
		if (trait == null)
		{
			return false;
		}

		try
		{
			BaseAugmentationAsset augmentation = (BaseAugmentationAsset)trait;
			AttackAction callback = OnAttackTarget;
			AttackAction current = augmentation.action_attack_target;
			if (ContainsCallback(current, callback))
			{
				return true;
			}

			augmentation.action_attack_target = (AttackAction)Delegate.Combine(current, callback);
			return true;
		}
		catch (Exception ex)
		{
			LogOnce("bind_callback", ex);
			return false;
		}
	}

	internal static bool TickActor(Actor actor)
	{
		if (!IsJinDanActor(actor)) return false;

		// 权柄之争唤醒的角色做一次兜底；普通战斗仍由攻击回调触发。
		Actor directTarget = null;
		try
		{
			directTarget = actor.attack_target as Actor;
		}
		catch (Exception ex)
		{
			LogOnce("fallback_target", ex);
		}
		return directTarget != null && TryTrigger(actor, directTarget, null);
	}

	internal static bool TryTrigger(Actor actor, Actor directTarget, WorldTile fallbackTile)
	{
		// 归因冻结不仅拦原生普攻，也必须拦玄鉴自己的法术回调/权柄之争兜底施法，
		// 否则角色会出现“人冻住了但神通还在自动释放”的语义断裂。
		if (XjAttributedFreezeRegistry.IsFrozen(actor)
			|| XjCanonicalShenTongMechanicsSystem.IsShenTongSuppressed(actor)) return false;

		if (!IsJinDanActor(actor))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		if (string.IsNullOrWhiteSpace(daoTu)) return false;
		IReadOnlyList<XjJinDanDaoSpellDefinition> candidates = XjJinDanDaoSpellCatalog.ReadForDaoTu(daoTu);
		if (candidates.Count <= 0) return false;

		long actorId = GetActorId(actor);
		if (actorId <= 0L || CastingActorIds.Contains(actorId))
		{
			return false;
		}

		float now = Time.time;
		// The shared 8-second cast gate makes every spell ineligible. Check it before
		// parsing XianJi or evaluating spell identities so ordinary attack callbacks
		// during the gate stay allocation-light. This is the same gate IsReady checks.
		if (!XjJinDanDaoSpellCooldown.CanAttemptAny(actorId, now)) return false;
		try
		{
			CastingActorIds.Add(actorId);
			bool containsCommon = false;
			string[] learnedShenTongIds = XjXianJiAccessor.ReadRawIds(actor);
			bool hasLearnedCanonicalSpell = HasLearnedCanonicalSpell(learnedShenTongIds, candidates);
			// 领域神通也是角色真实掌握的主动神通。若已经有青玉崖、不空劫、满垠㷐等
			// 具名领域，就不能趁领域30秒冷却时又穿插旧“道途组合技”。领域与普通主动
			// 共用同一8秒施法间隔，但各自保留独立30秒CD。
			bool hasNamedDomainSpell = XjDomainSkillCatalog.HasAnyDefinition(learnedShenTongIds);
			// 0.9.8.7：部分原著神通本来就是命、身、侦查、护持或物性变化，
			// 没有“主动高伤法术”的文本依据。只要角色已经掌握这些明确的原著神通，
			// 就不再拿旧十二炁/并古组合技补空档，避免视觉修对了却仍在实机放自编大招。
			bool hasGroundedPassiveIdentity = HasGroundedPassiveIdentity(learnedShenTongIds);
			bool hasSpecializedActiveSpell = hasLearnedCanonicalSpell || hasNamedDomainSpell || hasGroundedPassiveIdentity;
			for (int i = 0; i < candidates.Count; i++)
			{
				XjJinDanDaoSpellDefinition candidate = candidates[i];
				containsCommon |= string.Equals(candidate.Id, "CommonLeiFa", StringComparison.Ordinal);
				// 已经掌握具名战斗神通的角色，不再在这些神通冷却期间穿插旧“道途组合技”。
				// 这样两门具名主动只会按 30s 独立CD + 8s公共间隔轮转，而不会再塞入第三、第四门兜底技。
				if (hasSpecializedActiveSpell && (candidate.RequiredShenTongIds == null || candidate.RequiredShenTongIds.Count == 0))
				{
					continue;
				}
				// 0.9.8.6：原著具名神通不能再按“只要同一道途就自动会放”处理。
				// RequiredShenTongIds 非空时，必须真正出现在该角色的神通记录中；
				// 这样闇天殃、道始兆、神布序等只由实际掌握者施放。
				if (!HasRequiredShenTong(learnedShenTongIds, candidate))
				{
					continue;
				}
				// 同一道途存在多门道法时，依目录优先级选择第一门已结束冷却且目标适配的技能。
				// 例如太阴的映月莲华冷却中时，会继续尝试三阴抱一玄轮。
				if (!XjJinDanDaoSpellCooldown.IsReady(actorId, candidate.Id, now)
					|| !TryCastDefinition(actor, directTarget, fallbackTile, actorId, now, candidate))
				{
					continue;
				}
				RecordSpellAction(actor);
				return true;
			}

			// 0.5.4的通用金丹雷法仍是最终兜底；只有全部本道途技能均处于冷却
			// 或不适配当前目标时才尝试。
			if (!hasSpecializedActiveSpell
				&& !containsCommon
				&& XjJinDanDaoSpellCatalog.TryGetById("CommonLeiFa", out XjJinDanDaoSpellDefinition common)
				&& XjJinDanDaoSpellCooldown.IsReady(actorId, common.Id, now)
				&& TryCastDefinition(actor, directTarget, fallbackTile, actorId, now, common))
			{
				RecordSpellAction(actor);
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			LogOnce("trigger", ex);
			return false;
		}
		finally
		{
			CastingActorIds.Remove(actorId);
		}
	}

	private static bool TryCastDefinition(
		Actor actor,
		Actor directTarget,
		WorldTile fallbackTile,
		long actorId,
		float now,
		XjJinDanDaoSpellDefinition definition)
	{
		if (string.IsNullOrWhiteSpace(definition.Id)
			|| !XjJinDanDaoSpellCooldown.IsReady(actorId, definition.Id, now)
			|| !XjJinDanDaoSpellTargeting.TryBuildContext(
				actor,
				directTarget,
				fallbackTile,
				definition,
				out XjJinDanDaoSpellTargetContext context))
		{
			return false;
		}

		try
		{
			if (!XjJinDanDaoSpellExecutor.TryExecute(actor, definition, context))
			{
				return false;
			}
			// 0.9.8.28：恢复0.21金丹“出手即压场”的大范围观感，但不把原著
			// 单体/限定目标神通篡改成无差别群攻。主神通结算后，只由统一高境余威
			// 横扫附近低一大境界以上目标；同境仍由原神通、果位与权柄规则决胜。
			XjHighRealmPressureWaveSystem.TryApplyAfterSpell(actor, definition, context);
			XjJinDanDaoSpellCooldown.MarkUsed(actorId, definition.Id, now, definition.CooldownSeconds);
			return true;
		}
		catch (Exception ex)
		{
			LogOnce("execute:" + (definition.Id ?? "unknown"), ex);
			return false;
		}
	}

	private static void RecordSpellAction(Actor actor)
	{
		int currentYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
				XjYinSiExposurePursuitSystem.RecordExternalJinDanAction(actor, 1f, "在外界施展真君道法", currentYear);
	}

	internal static void RemoveActor(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}
		CastingActorIds.Remove(actorId);
		XjJinDanDaoSpellCooldown.RemoveActor(actorId);
		XjCanonicalShenTongVisualSystem.RemoveActor(actorId);
		XjCanonicalShenTongMechanicsSystem.RemoveActor(actorId);
	}

	internal static void Clear()
	{
		CastingActorIds.Clear();
		LoggedRuntimeErrors.Clear();
		// 共用施法门控属于纯运行时状态；清档/换世界时必须同时清掉，
		// 否则旧世界的30秒独立CD与8秒公共CD会污染新世界同ID角色。
		XjJinDanDaoSpellCooldown.Clear();
		XjCanonicalShenTongVisualSystem.Clear();
		XjCanonicalShenTongMechanicsSystem.Clear();
	}

	internal static bool IsJinDanActor(Actor actor)
	{
		if (actor?.data == null || !XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
		{
			string normalized = XjRealmHelper.NormalizeId(realmId);
			if (!string.IsNullOrWhiteSpace(normalized))
			{
				return XjCultivationPathRules.IsJinDanEquivalentRealm(normalized)
					|| string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
					|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
					|| string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal);
			}
		}

		// Only actors with no authoritative realm metadata may fall back to legacy traits.
		try
		{
			return actor.hasTrait(XjRealmIds.JinDan)
				|| actor.hasTrait(XjRealmIds.ZhenJunYuShi)
				|| actor.hasTrait(XjRealmIds.DaoTai)
				|| actor.hasTrait(XjRealmIds.FuQiDaoTai)
				|| actor.hasTrait(XjRealmIds.ShenDan);
		}
		catch (Exception ex)
		{
			LogOnce("realm_trait", ex);
			return false;
		}
	}

	private static bool HasGroundedPassiveIdentity(string[] learned)
	{
		if (learned == null || learned.Length == 0) return false;
		for (int i = 0; i < learned.Length; i++)
		{
			string id = (learned[i] ?? string.Empty).Trim();
			if (id.Length == 0) continue;
			if (id is
				"抱石眠" or "霞羽客" or "鹤挂衣" or "授长生"
				or "入清听" or "祢水寒" or "沆砀满"
				or "好功箓" or "瑞气云" or "冠灵旒"
				or "明心筵" or "致缉熙"
				or "浥铅华" or "混铅华" or "制飬宜" or "制养宜" or "秘白汞" or "秘白录" or "候神殊" or "侯神殊" or "金书序"
				or "形渡阡" or "相离绝"
				or "玄羊子" or "青宣岳" or "上岩神" or "观地冥"
				or "降魂闻" or "北漠庭")
			{
				return true;
			}
		}
		return false;
	}

	private static bool ContainsCallback(AttackAction current, AttackAction callback)
	{
		if (current == null || callback == null)
		{
			return false;
		}

		Delegate[] invocationList = current.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			Delegate item = invocationList[i];
			if (item != null && item.Method == callback.Method)
			{
				return true;
			}
		}

		return false;
	}

	private static void LogOnce(string key, Exception ex)
	{
		string normalizedKey = string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();
		string errorKey = normalizedKey + "|" + (ex?.GetType().FullName ?? "UnknownException");
		if (!LoggedRuntimeErrors.Add(errorKey))
		{
			return;
		}
		Debug.LogWarning("[玄鉴][真君法术] " + normalizedKey + " 失败：" + (ex?.Message ?? "未知异常"));
	}

	private static bool HasLearnedCanonicalSpell(string[] learned, IReadOnlyList<XjJinDanDaoSpellDefinition> candidates)
	{
		if (learned == null || learned.Length == 0 || candidates == null) return false;
		for (int i = 0; i < candidates.Count; i++)
		{
			XjJinDanDaoSpellDefinition candidate = candidates[i];
			if (candidate.RequiredShenTongIds != null
				&& candidate.RequiredShenTongIds.Count > 0
				&& HasRequiredShenTong(learned, candidate))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasRequiredShenTong(string[] learned, in XjJinDanDaoSpellDefinition definition)
	{
		IReadOnlyList<string> required = definition.RequiredShenTongIds;
		if (required == null || required.Count == 0) return true;
		if (learned == null || learned.Length == 0) return false;
		for (int i = 0; i < learned.Length; i++)
		{
			string learnedId = (learned[i] ?? string.Empty).Trim();
			if (learnedId.Length == 0) continue;
			for (int j = 0; j < required.Count; j++)
			{
				if (string.Equals(learnedId, (required[j] ?? string.Empty).Trim(), StringComparison.Ordinal))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static long GetActorId(Actor actor)
	{
		try
		{
			return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		}
		catch (System.Exception xjCaught282_8)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanDaoSpellRuntime.cs:282", xjCaught282_8);
			
			return 0L;
		}
	}
}

internal static class XjJinDanDaoSpellCooldown
{
	internal const float MinimumInterSpellSeconds = 8f;
	private static readonly Dictionary<long, Dictionary<string, float>> ReadyTimes =
		new Dictionary<long, Dictionary<string, float>>();
	private static readonly Dictionary<long, float> NextAnySpellTimes =
		new Dictionary<long, float>();

	internal static bool CanAttemptAny(long actorId, float currentTime)
	{
		if (actorId <= 0L) return false;
		if (!NextAnySpellTimes.TryGetValue(actorId, out float nextAnySpellTime)) return true;
		if (currentTime < nextAnySpellTime) return false;
		NextAnySpellTimes.Remove(actorId);
		return true;
	}

	internal static bool IsReady(long actorId, string spellId, float currentTime)
	{
		if (actorId <= 0L || string.IsNullOrWhiteSpace(spellId))
		{
			return false;
		}

		if (NextAnySpellTimes.TryGetValue(actorId, out float nextAnySpellTime))
		{
			if (currentTime < nextAnySpellTime)
			{
				return false;
			}
			NextAnySpellTimes.Remove(actorId);
		}

		if (!ReadyTimes.TryGetValue(actorId, out Dictionary<string, float> actorCooldowns)
			|| !actorCooldowns.TryGetValue(spellId, out float readyTime))
		{
			return true;
		}

		if (currentTime < readyTime)
		{
			return false;
		}

		actorCooldowns.Remove(spellId);
		if (actorCooldowns.Count == 0)
		{
			ReadyTimes.Remove(actorId);
		}
		return true;
	}

	internal static float GetRemainingSeconds(long actorId, string spellId, float currentTime)
	{
		if (actorId <= 0L || string.IsNullOrWhiteSpace(spellId))
		{
			return 0f;
		}

		float remaining = 0f;
		if (ReadyTimes.TryGetValue(actorId, out Dictionary<string, float> actorCooldowns)
			&& actorCooldowns.TryGetValue(spellId, out float readyTime))
		{
			remaining = Math.Max(0f, readyTime - currentTime);
		}

		// UI/调试读取也必须反映 8 秒公共施法间隔，否则界面会显示另一门法术
		// 已就绪，但运行时仍被全局门控拒绝，形成新的“显示与执行不一致”。
		if (NextAnySpellTimes.TryGetValue(actorId, out float nextAnySpellTime))
		{
			remaining = Math.Max(remaining, Math.Max(0f, nextAnySpellTime - currentTime));
		}
		return remaining;
	}

	internal static void MarkUsed(long actorId, string spellId, float currentTime, float cooldownSeconds)
	{
		if (actorId <= 0L || string.IsNullOrWhiteSpace(spellId) || cooldownSeconds <= 0f)
		{
			return;
		}

		if (!ReadyTimes.TryGetValue(actorId, out Dictionary<string, float> actorCooldowns))
		{
			actorCooldowns = new Dictionary<string, float>(StringComparer.Ordinal);
			ReadyTimes[actorId] = actorCooldowns;
		}

		actorCooldowns[spellId] = currentTime + cooldownSeconds;
		NextAnySpellTimes[actorId] = currentTime + MinimumInterSpellSeconds;
	}

	internal static void RemoveActor(long actorId)
	{
		if (actorId > 0L)
		{
			ReadyTimes.Remove(actorId);
			NextAnySpellTimes.Remove(actorId);
		}
	}

	internal static void Clear()
	{
		ReadyTimes.Clear();
		NextAnySpellTimes.Clear();
	}
}

internal sealed class XjJinDanCombatLane
{
	internal bool HasPending => _queue.Count > 0;
	private const int MaxActorsPerTick = 64;

	private readonly Func<long, Actor> _resolveActor;
	private readonly Queue<long> _queue = new Queue<long>();
	private readonly HashSet<long> _queuedActorIds = new HashSet<long>();

	internal XjJinDanCombatLane(Func<long, Actor> resolveActor)
	{
		_resolveActor = resolveActor;
	}

	internal void EnqueueActor(long actorId)
	{
		if (actorId <= 0L || _queuedActorIds.Contains(actorId))
		{
			return;
		}

		_queuedActorIds.Add(actorId);
		_queue.Enqueue(actorId);
	}

	internal void Tick(int budget)
	{
		if (budget <= 0 || _queue.Count == 0)
		{
			return;
		}

		int initialCount = Math.Min(_queue.Count, Math.Min(MaxActorsPerTick, budget));
		for (int processed = 0; processed < initialCount && _queue.Count > 0; processed++)
		{
			long actorId = _queue.Dequeue();
			_queuedActorIds.Remove(actorId);

			Actor actor = _resolveActor?.Invoke(actorId);
			if (actor?.data == null)
			{
				continue;
			}

			bool authorityWarActive = XjQuanBingStruggleSystem.TickCombatActor(actor);
			if (authorityWarActive)
			{
				// 权柄之争需要持续追敌；法术只顺带进行一次目标兜底。
				XjJinDanDaoSpellRuntime.TickActor(actor);
				EnqueueActor(actorId);
			}
			// 普通金丹本轮即退出。冷却只在角色注销/死亡时清理，
			// 不因退出轮询队列而重置，避免事件施法绕过 CD。
		}
	}

	internal void Clear()
	{
		_queue.Clear();
		_queuedActorIds.Clear();
	}
}
