using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 玄鉴高境神通的“归因冻结”运行时登记表。
///
/// 原生 frozen 状态仍保留作为兼容/表现层，但不再承担唯一行为锁职责：
/// 只有通过 XjHighRealmControlService 位格裁定后的冻结才会写入这里。
/// Harmony 热路径先读一个原子 activeCount；没有任何冻结时直接返回，
/// 不扫描世界、不分配集合、不写日志。ConcurrentDictionary 也避免
/// updateParallelChecks 若由 WorldBox 并行批次调用时与主线程施法写入互撞。
/// </summary>
internal static class XjAttributedFreezeRegistry
{
    private static readonly ConcurrentDictionary<long, float> FreezeUntilByActorId =
        new ConcurrentDictionary<long, float>();

    private static int _activeCount;
    private static int _nextExpiryPruneFrame;

    internal static bool HasActiveFreezes => Volatile.Read(ref _activeCount) > 0;

    internal static bool Apply(Actor target, float durationSeconds)
    {
        if (target?.data == null || durationSeconds <= 0.05f || !IsAlive(target)) return false;

        long actorId = GetActorId(target);
        if (actorId <= 0L) return false;

        float requestedUntil = GetControlTime() + Mathf.Max(0.05f, durationSeconds);
        while (true)
        {
            if (FreezeUntilByActorId.TryGetValue(actorId, out float currentUntil))
            {
                if (currentUntil >= requestedUntil
                    || FreezeUntilByActorId.TryUpdate(actorId, requestedUntil, currentUntil))
                {
                    break;
                }
                continue;
            }

            if (FreezeUntilByActorId.TryAdd(actorId, requestedUntil))
            {
                Interlocked.Increment(ref _activeCount);
                break;
            }
        }

        ForceHaltOnApply(target);
        return true;
    }

    internal static bool IsFrozen(BaseSimObject simObject)
    {
        if (!HasActiveFreezes || simObject == null) return false;
        if (simObject is Actor actor) return IsFrozen(actor);
        try
        {
            return simObject.isActor() && IsFrozen(simObject.a);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsFrozen(Actor actor)
    {
        if (!HasActiveFreezes || actor?.data == null) return false;

        // 防止“最后一个冻结者死亡/停止更新”后过期条目永久保留，导致全世界 Actor
        // 以后都继续做字典查询。仅冻结表非空时每30帧最多由一个调用者做一次小表清理。
        PruneExpiredIfDue();
        if (!HasActiveFreezes) return false;

        long actorId = GetActorId(actor);
        if (actorId <= 0L || !FreezeUntilByActorId.TryGetValue(actorId, out float until)) return false;

        if (!IsAlive(actor) || GetControlTime() >= until)
        {
            RemoveActor(actorId);
            return false;
        }

        return true;
    }

    internal static float GetRemainingSeconds(Actor actor)
    {
        if (!IsFrozen(actor)) return 0f;
        long actorId = GetActorId(actor);
        return actorId > 0L && FreezeUntilByActorId.TryGetValue(actorId, out float until)
            ? Mathf.Max(0f, until - GetControlTime())
            : 0f;
    }

    /// <summary>
    /// updateParallelChecks 前执行。这里只维持“停在原地”，不重复 cancelAllBeh，
    /// 避免每帧重建行为树；AI / 决策 / 攻击由独立 Prefix 直接跳过。
    /// </summary>
    internal static void MaintainFrozenActor(Actor actor)
    {
        if (!IsFrozen(actor)) return;

        try { actor.stopMovement(); } catch { }
        try { actor.clearOldPath(); } catch { }
    }

    internal static void RemoveActor(long actorId)
    {
        if (actorId <= 0L) return;
        if (FreezeUntilByActorId.TryRemove(actorId, out _))
        {
            int after = Interlocked.Decrement(ref _activeCount);
            if (after < 0) Interlocked.Exchange(ref _activeCount, 0);
        }
    }

    internal static void Clear()
    {
        FreezeUntilByActorId.Clear();
        Interlocked.Exchange(ref _activeCount, 0);
        Interlocked.Exchange(ref _nextExpiryPruneFrame, 0);
    }

    private static void PruneExpiredIfDue()
    {
        int frame = Time.frameCount;
        int due = Volatile.Read(ref _nextExpiryPruneFrame);
        if (frame < due) return;
        if (Interlocked.CompareExchange(ref _nextExpiryPruneFrame, frame + 30, due) != due) return;

        float now = GetControlTime();
        foreach (KeyValuePair<long, float> pair in FreezeUntilByActorId)
        {
            if (now >= pair.Value) RemoveActor(pair.Key);
        }
    }

    private static float GetControlTime()
    {
        try
        {
            if (World.world != null) return (float)World.world.getCurWorldTime();
        }
        catch { }
        return Time.time;
    }

    private static void ForceHaltOnApply(Actor actor)
    {
        try { actor.cancelAllBeh(); } catch { }
        try { actor.clearOldPath(); } catch { }
        try { XjActorAggroBridge.ClearTargets(actor, stopMovement: true, clearRetaliation: false); } catch { }
        try { actor.stopMovement(); } catch { }
    }

    private static long GetActorId(Actor actor)
    {
        try
        {
            return actor?.data is BaseSystemData data ? data.id : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    private static bool IsAlive(Actor actor)
    {
        if (actor?.data == null) return false;
        try { return actor.isAlive(); }
        catch { return false; }
    }
}
