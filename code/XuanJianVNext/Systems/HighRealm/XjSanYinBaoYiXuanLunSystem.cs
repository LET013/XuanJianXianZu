using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Visual;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 三阴通用金丹增益法术“三阴抱一玄轮”。少阴/厥阴仍获得90个百分比点闪避；
/// 太阴改为随失血递增的最终闪避下限：满血不额外抬升，生命降至20%及以下时达到100%。
/// </summary>
internal static class XjSanYinBaoYiXuanLunSystem
{
	private readonly struct BuffEntry
	{
		internal BuffEntry(float endTime, float dodgeBonus)
		{
			EndTime = endTime;
			DodgeBonus = dodgeBonus;
		}

		internal float EndTime { get; }
		internal float DodgeBonus { get; }
	}

	private static readonly Dictionary<long, BuffEntry> ActiveBuffs = new Dictionary<long, BuffEntry>();

	internal static bool TryActivate(Actor caster, in XjJinDanDaoSpellDefinition definition)
	{
		if (!XjSafeCore.IsAliveActor(caster)
			|| definition.StatusDurationSeconds <= 0f
			|| definition.SelfDodgeBonusPercent <= 0f)
		{
			return false;
		}

		long actorId = GetActorId(caster);
		if (actorId <= 0L)
		{
			return false;
		}

		float duration = Mathf.Max(0.1f, definition.StatusDurationSeconds);
		ActiveBuffs[actorId] = new BuffEntry(
			Time.time + duration,
			Mathf.Clamp(definition.SelfDodgeBonusPercent, 0f, 100f));

		TryPlayOverheadVisual(caster, definition, duration);
		return true;
	}

	internal static float GetRemainingDurationSeconds(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L || !ActiveBuffs.TryGetValue(actorId, out BuffEntry entry))
		{
			return 0f;
		}

		// 角色信息读取必须保持只读；过期条目由实际战斗查询 GetDodgeBonus 清理。
		return Math.Max(0f, entry.EndTime - Time.time);
	}

	internal static float GetDodgeBonus(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L || !ActiveBuffs.TryGetValue(actorId, out BuffEntry entry))
		{
			return 0f;
		}

		if (!XjSafeCore.IsAliveActor(actor) || Time.time >= entry.EndTime)
		{
			ActiveBuffs.Remove(actorId);
			return 0f;
		}

		// 太阴的玄轮不再提供固定90点闪避；其动态曲线由
		// GetDynamicDodgeChanceFloor 作为最终闪避下限结算，避免被命中属性抵消掉
		// “20%生命附近=100%闪避”的核心语义。
		return IsTaiYin(actor) ? 0f : entry.DodgeBonus;
	}

	internal static float GetDynamicDodgeChanceFloor(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L || !ActiveBuffs.TryGetValue(actorId, out BuffEntry entry) || !IsTaiYin(actor))
		{
			return 0f;
		}
		if (!XjSafeCore.IsAliveActor(actor) || Time.time >= entry.EndTime)
		{
			ActiveBuffs.Remove(actorId);
			return 0f;
		}

		float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, 0f);
		float currentHealth = XjSafeCore.GetHealthSafe(actor, maxHealth);
		if (maxHealth <= 0.01f) return 0f;
		float healthRatio = Mathf.Clamp01(currentHealth / maxHealth);
		if (healthRatio <= 0.20f) return 100f;
		// 100%生命=0，80%=25，60%=50，40%=75，20%=100。
		return Mathf.Clamp01((1f - healthRatio) / 0.8f) * 100f;
	}

	internal static void RemoveActor(long actorId)
	{
		if (actorId > 0L)
		{
			ActiveBuffs.Remove(actorId);
		}
	}

	internal static void Clear()
	{
		ActiveBuffs.Clear();
	}

	private static void TryPlayOverheadVisual(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		float duration)
	{
		try
		{
			Sprite[] frames = XjCombatEffectPool.GetCachedAnimationFrames(definition.AnimationPath);
			if (frames == null || frames.Length == 0)
			{
				return;
			}

			float actorWorldSize = XjActorVisualMetrics.ResolveWorldSize(caster);
			float desiredWorldSize = actorWorldSize * 2f;
			float scale = ResolveScale(frames, desiredWorldSize);
			float headOffset = Mathf.Max(1.7f, actorWorldSize * 1.75f);
			float interval = definition.AnimationFrameIntervalSeconds > 0f
				? definition.AnimationFrameIntervalSeconds
				: 0.14f;

			XjCombatEffectPool.TrySpawnTrackedOverlaySpriteAnimationSafe(
				definition.AnimationPath,
				() => ResolveActorAnchor(caster) + Vector2.up * headOffset,
				scale,
				interval,
				duration,
				0f,
				96,
				0f,
				false,
				() => XjSafeCore.IsAliveActor(caster));
		}
		catch (System.Exception xjCaught130) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjSanYinBaoYiXuanLunSystem.cs:130", xjCaught130); }
	}

	private static float ResolveScale(Sprite[] frames, float desiredWorldSize)
	{
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
			? Mathf.Clamp(desiredWorldSize / diameter, 0.05f, 8f)
			: 1f;
	}

	private static Vector2 ResolveActorAnchor(Actor actor)
	{
		try
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
		catch (System.Exception xjCaught169) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjSanYinBaoYiXuanLunSystem.cs:169", xjCaught169); }

		return Vector2.zero;
	}

	private static bool IsTaiYin(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& string.Equals((daoTu ?? string.Empty).Trim(), "太阴", StringComparison.Ordinal);
	}

	private static long GetActorId(Actor actor)
	{
		try
		{
			return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		}
		catch (System.Exception xjCaught178_3)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjSanYinBaoYiXuanLunSystem.cs:178", xjCaught178_3);
			
			return 0L;
		}
	}
}
