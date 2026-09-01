using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Systems.DengMingShi;

namespace XuanJianVNext.Patches;

/// <summary>
/// Failure-only transaction guard for Great Sage native creation.
///
/// ActorManager.spawnNewUnit can mutate native unit/chunk/AI containers before the
/// caller receives its return value. If an exception is thrown inside the native
/// method, C# never assigns the caller's local `spawned`, so ordinary caller cleanup
/// cannot identify a partially inserted actor. This guard snapshots only the rare
/// xj_yaoshu_dasheng_* spawn transaction and removes actors introduced by that failed
/// call. It is never used by normal actors and never runs from annual/UI/frame scans.
/// </summary>
internal static class XjGreatSageSpawnSafetyPatches
{
    private const string GreatSageAssetPrefix = "xj_yaoshu_dasheng_";

    [ThreadStatic]
    private static Stack<SpawnSnapshot> _pendingSnapshots;

    private sealed class SpawnSnapshot
    {
        internal string AssetId = string.Empty;
        internal HashSet<long> ExistingActorIds = new HashSet<long>();
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(ActorManager), "spawnNewUnit", new Type[]
    {
        typeof(string), typeof(WorldTile), typeof(bool), typeof(bool), typeof(float),
        typeof(Subspecies), typeof(bool), typeof(bool)
    })]
    private static void GreatSageSpawnTransactionPrefix(object[] __args)
    {
        string assetId = ResolveRequestedAssetId(__args);
        if (!IsGreatSageAssetId(assetId)) return;

        _pendingSnapshots ??= new Stack<SpawnSnapshot>(2);
        _pendingSnapshots.Push(new SpawnSnapshot
        {
            AssetId = assetId,
            ExistingActorIds = CaptureCurrentActorIds()
        });
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(ActorManager), "spawnNewUnit", new Type[]
    {
        typeof(string), typeof(WorldTile), typeof(bool), typeof(bool), typeof(float),
        typeof(Subspecies), typeof(bool), typeof(bool)
    })]
    private static void GreatSageSpawnTransactionPostfix(Actor __result, object[] __args)
    {
        string assetId = ResolveRequestedAssetId(__args);
        if (!IsGreatSageAssetId(assetId)) return;

        SpawnSnapshot snapshot = PopSnapshot(assetId);
        if (snapshot == null || __result != null) return;

        int removed = CleanupActorsIntroducedByFailedSpawn(snapshot);
        if (removed > 0)
        {
            Debug.LogWarning("[玄鉴][妖属大圣] 原生spawn返回空值，已回滚半成单位 asset="
                + assetId + " removed=" + removed);
        }
    }

    [HarmonyFinalizer]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(ActorManager), "spawnNewUnit", new Type[]
    {
        typeof(string), typeof(WorldTile), typeof(bool), typeof(bool), typeof(float),
        typeof(Subspecies), typeof(bool), typeof(bool)
    })]
    private static Exception GreatSageSpawnTransactionFinalizer(Exception __exception, object[] __args)
    {
        string assetId = ResolveRequestedAssetId(__args);
        if (!IsGreatSageAssetId(assetId) || __exception == null) return __exception;

        SpawnSnapshot snapshot = PopSnapshot(assetId);
        int removed = snapshot == null ? 0 : CleanupActorsIntroducedByFailedSpawn(snapshot);
        Debug.LogWarning("[玄鉴][妖属大圣] 原生spawn事务异常，已执行失败回滚 asset="
            + assetId + " removed=" + removed + " exception=" + __exception.GetType().Name);

        // Preserve the exception. XjYaoShuGreatSageSystem.TryManifest owns the domain
        // failure decision/logging; this finalizer only guarantees native rollback.
        return __exception;
    }

    private static string ResolveRequestedAssetId(object[] args)
    {
        return args != null && args.Length > 0 ? args[0] as string ?? string.Empty : string.Empty;
    }

    private static bool IsGreatSageAssetId(string assetId)
    {
        return !string.IsNullOrWhiteSpace(assetId)
            && assetId.StartsWith(GreatSageAssetPrefix, StringComparison.Ordinal);
    }

    private static HashSet<long> CaptureCurrentActorIds()
    {
        HashSet<long> ids = new HashSet<long>();
        IReadOnlyList<Actor> actors = null;
        try { actors = World.world?.units?.getSimpleList(); }
        catch { }
        if (actors == null) return ids;

        for (int i = 0; i < actors.Count; i++)
        {
            long id = ResolveActorId(actors[i]);
            if (id > 0L) ids.Add(id);
        }
        return ids;
    }

    private static SpawnSnapshot PopSnapshot(string assetId)
    {
        Stack<SpawnSnapshot> stack = _pendingSnapshots;
        if (stack == null || stack.Count == 0) return null;

        SpawnSnapshot snapshot = stack.Peek();
        if (!string.Equals(snapshot.AssetId, assetId, StringComparison.Ordinal))
        {
            // A mismatch means an unexpected nested native call. Do not pop another
            // transaction's state; leaving it intact is safer than cross-cleaning.
            return null;
        }
        return stack.Pop();
    }

    private static int CleanupActorsIntroducedByFailedSpawn(SpawnSnapshot snapshot)
    {
        if (snapshot == null || World.world?.units == null) return 0;

        IReadOnlyList<Actor> actors = null;
        try { actors = World.world.units.getSimpleList(); }
        catch { }
        if (actors == null || actors.Count == 0) return 0;

        // Collect first. Native removal mutates the manager and must never happen while
        // iterating its simple-list view.
        List<Actor> cleanup = new List<Actor>(2);
        for (int i = 0; i < actors.Count; i++)
        {
            Actor candidate = actors[i];
            if (candidate == null) continue;

            long id = ResolveActorId(candidate);
            if (id > 0L && snapshot.ExistingActorIds.Contains(id)) continue;

            string candidateAssetId = ResolveActorAssetId(candidate);
            bool sameAsset = string.Equals(candidateAssetId, snapshot.AssetId, StringComparison.Ordinal);
            bool clearlyIncomplete = candidate.data == null
                || string.IsNullOrWhiteSpace(candidateAssetId)
                || !HasUsableTile(candidate);

            if (sameAsset || clearlyIncomplete) cleanup.Add(candidate);
        }

        int removed = 0;
        for (int i = 0; i < cleanup.Count; i++)
        {
            try
            {
                if (XjDengMingShiSpawnSafety.TryRemoveInvalidActorWithResult(cleanup[i])) removed++;
            }
            catch { }
        }
        return removed;
    }

    private static long ResolveActorId(Actor actor)
    {
        try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
        catch { return 0L; }
    }

    private static string ResolveActorAssetId(Actor actor)
    {
        try { return actor?.asset?.id ?? actor?.data?.asset_id ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static bool HasUsableTile(Actor actor)
    {
        try { return actor?.current_tile?.chunk != null; }
        catch { return false; }
    }
}
