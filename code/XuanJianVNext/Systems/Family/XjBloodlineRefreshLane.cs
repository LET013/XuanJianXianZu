using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 高境血脉刷新属于境界写入后的派生收敛，不参与突破事务提交。
/// WorldBox 在读档、死亡、换身与 UI/家族索引重建的交界帧里可能暂时持有
/// 半失效亲属引用；因此这里只保存 ActorId，在后续渲染帧重新解析活角色后处理。
/// </summary>
internal static class XjBloodlineRefreshLane
{
    private const int DefaultTickBudget = 6;
    private const int MaxTransientRetries = 3;
    private static readonly object Sync = new object();
    private static readonly Queue<long> PendingActorIds = new Queue<long>();
    private static readonly HashSet<long> PendingActorIdSet = new HashSet<long>();
    private static readonly Dictionary<long, int> RequestedYearByActorId = new Dictionary<long, int>();
    private static readonly Dictionary<long, int> RetryCountByActorId = new Dictionary<long, int>();
    private static bool PersistentNullLogged;

    internal static bool HasPending
    {
        get { lock (Sync) return PendingActorIds.Count > 0; }
    }

    internal static int PendingCount
    {
        get { lock (Sync) return PendingActorIds.Count; }
    }

    internal static void Request(Actor actor, int currentYear)
    {
        if (!XjSafeCore.IsAliveActor(actor) || actor.data == null) return;
        // 龙属没有玄鉴修士家族，不进入高境血脉/后裔投影。
        try { if (XjLongShuSystem.IsLongShu(actor)) return; }
        catch { return; }
        long actorId;
        try { actorId = ((BaseSystemData)actor.data).id; }
        catch { return; }
        Request(actorId, currentYear);
    }

    internal static void Request(long actorId, int currentYear)
    {
        if (actorId <= 0L) return;
        int safeYear = Math.Max(0, currentYear);
        lock (Sync)
        {
            if (!RequestedYearByActorId.TryGetValue(actorId, out int previousYear) || safeYear > previousYear)
            {
                RequestedYearByActorId[actorId] = safeYear;
            }
            if (PendingActorIdSet.Add(actorId)) PendingActorIds.Enqueue(actorId);
        }
    }

    internal static void Tick(int budget = DefaultTickBudget)
    {
        int workCount;
        lock (Sync) workCount = Math.Min(Math.Max(1, budget), PendingActorIds.Count);
        for (int i = 0; i < workCount; i++)
        {
            long actorId;
            int requestedYear;
            lock (Sync)
            {
                if (PendingActorIds.Count == 0) return;
                actorId = PendingActorIds.Dequeue();
                if (!PendingActorIdSet.Remove(actorId))
                {
                    continue;
                }
                RequestedYearByActorId.TryGetValue(actorId, out requestedYear);
            }

            if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
                || !XjSafeCore.IsAliveActor(actor)
                || actor.data == null)
            {
                Forget(actorId);
                continue;
            }

            try
            {
                XjBloodlineBirthRules.TryRefreshForRealmPromotion(actor, requestedYear);
                lock (Sync)
                {
                    RequestedYearByActorId.Remove(actorId);
                    RetryCountByActorId.Remove(actorId);
                }
            }
            catch (NullReferenceException exception)
            {
                int retries;
                lock (Sync)
                {
                    RetryCountByActorId.TryGetValue(actorId, out retries);
                    retries++;
                    RetryCountByActorId[actorId] = retries;
                    if (retries <= MaxTransientRetries && PendingActorIdSet.Add(actorId))
                    {
                        PendingActorIds.Enqueue(actorId);
                    }
                }
                if (retries > MaxTransientRetries)
                {
                    Forget(actorId);
                    bool shouldLogFullStack = false;
                    lock (Sync)
                    {
                        if (!PersistentNullLogged)
                        {
                            PersistentNullLogged = true;
                            shouldLogFullStack = true;
                        }
                    }
                    if (shouldLogFullStack)
                    {
                        UnityEngine.Debug.LogError("[玄鉴][P0][血脉] 延迟刷新连续三帧仍发生空引用 actor=" + actorId + "：" + exception);
                    }
                    else
                    {
                        XjExceptionDiagnostics.Report("BloodlineRefreshLane.PersistentNull actor=" + actorId, exception);
                    }
                }
            }
            catch (Exception exception)
            {
                Forget(actorId);
                XjExceptionDiagnostics.Report("BloodlineRefreshLane actor=" + actorId, exception);
            }
        }
    }

    internal static void Forget(long actorId)
    {
        if (actorId <= 0L) return;
        lock (Sync)
        {
            PendingActorIdSet.Remove(actorId);
            RequestedYearByActorId.Remove(actorId);
            RetryCountByActorId.Remove(actorId);
        }
    }

    internal static void Clear()
    {
        lock (Sync)
        {
            PendingActorIds.Clear();
            PendingActorIdSet.Clear();
            RequestedYearByActorId.Clear();
            RetryCountByActorId.Clear();
            PersistentNullLogged = false;
        }
    }
}
