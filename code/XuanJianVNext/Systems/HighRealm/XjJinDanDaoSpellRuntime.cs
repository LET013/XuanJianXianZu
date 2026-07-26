using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using UnityEngine;
using System.Collections.Generic;
using System;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjJinDanDaoSpellRuntime
{
	private static readonly HashSet<long> CastingActorIds = new HashSet<long>();
	private static readonly HashSet<string> LoggedRuntimeErrors = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>
	/// 使用原生攻击事件携带的明确目标触发金丹道法，避免依赖一秒轮询时
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
		if (!IsJinDanActor(actor))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		List<XjJinDanDaoSpellDefinition> candidates = new List<XjJinDanDaoSpellDefinition>(3);
		if (string.IsNullOrWhiteSpace(daoTu)
			|| XjJinDanDaoSpellCatalog.CollectForDaoTu(daoTu, candidates) <= 0)
		{
			return false;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L || CastingActorIds.Contains(actorId))
		{
			return false;
		}

		float now = Time.time;
		try
		{
			CastingActorIds.Add(actorId);
			bool containsCommon = false;
			for (int i = 0; i < candidates.Count; i++)
			{
				XjJinDanDaoSpellDefinition candidate = candidates[i];
				containsCommon |= string.Equals(candidate.Id, "CommonLeiFa", StringComparison.Ordinal);
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
			if (!containsCommon
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
		XjYinSiExposurePursuitSystem.RecordExternalJinDanAction(actor, 1f, "在外界施展金丹道法", currentYear);
	}

	internal static void RemoveActor(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}
		CastingActorIds.Remove(actorId);
		XjJinDanDaoSpellCooldown.RemoveActor(actorId);
	}

	internal static void Clear()
	{
		CastingActorIds.Clear();
		LoggedRuntimeErrors.Clear();
	}

	internal static bool IsJinDanActor(Actor actor)
	{
		if (actor?.data == null || !XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
				|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)))
		{
			return true;
		}

		try
		{
			return actor.hasTrait(XjRealmIds.JinDan) || actor.hasTrait(XjRealmIds.ShenDan);
		}
		catch (Exception ex)
		{
			LogOnce("realm_trait", ex);
			return false;
		}
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
		Debug.LogWarning("[玄鉴][金丹法术] " + normalizedKey + " 失败：" + (ex?.Message ?? "未知异常"));
	}

	private static long GetActorId(Actor actor)
	{
		try
		{
			return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		}
		catch
		{
			return 0L;
		}
	}
}

internal static class XjJinDanDaoSpellCooldown
{
	private static readonly Dictionary<long, Dictionary<string, float>> ReadyTimes =
		new Dictionary<long, Dictionary<string, float>>();

	internal static bool IsReady(long actorId, string spellId, float currentTime)
	{
		if (actorId <= 0L || string.IsNullOrWhiteSpace(spellId))
		{
			return false;
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

		if (!ReadyTimes.TryGetValue(actorId, out Dictionary<string, float> actorCooldowns)
			|| !actorCooldowns.TryGetValue(spellId, out float readyTime))
		{
			return 0f;
		}

		return Math.Max(0f, readyTime - currentTime);
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
	}

	internal static void RemoveActor(long actorId)
	{
		if (actorId > 0L)
		{
			ReadyTimes.Remove(actorId);
		}
	}

	internal static void Clear()
	{
		ReadyTimes.Clear();
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
