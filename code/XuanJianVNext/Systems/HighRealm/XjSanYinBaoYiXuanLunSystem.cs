using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Visual;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 三阴通用金丹增益法术“三阴抱一玄轮”。激活后提供 90 个百分比点的
/// 攻击闪避，持续 30 秒；法术本身由金丹法术冷却表控制 60 秒冷却。
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

		return entry.DodgeBonus;
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
		catch
		{
		}
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
		catch
		{
		}

		return Vector2.zero;
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
