using System;
using System.Collections.Generic;
using UnityEngine;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// 有界局部角色查询。所有范围技能共享同一条区块查询规则：限制半径、原始扫描量、
/// 合法候选数和最终结果数，不允许调用方为“附近目标”退化为全世界角色遍历。
///
/// maxCandidates 表示“合法候选上限”，不再表示 Finder 枚举顺序中的前 N 个 Actor。
/// 原始枚举另有硬上限，合法候选按距离稳定排序，因此人口密集城市不会因为前 64 个
/// 恰好是友军/无效单位而让大范围神通表现失真。
/// </summary>
internal static class XjLocalActorQuery
{
    internal delegate bool ContextActorPredicate(Actor context, Actor candidate);

    [ThreadStatic] private static HashSet<long> _seenScratch;
    [ThreadStatic] private static List<Candidate> _candidateScratch;
    [ThreadStatic] private static int _scratchDepth;

    private readonly struct Candidate
    {
        internal Candidate(Actor actor, long actorId, int distanceSquared)
        {
            Actor = actor;
            ActorId = actorId;
            DistanceSquared = distanceSquared;
        }

        internal Actor Actor { get; }
        internal long ActorId { get; }
        internal int DistanceSquared { get; }
    }

    internal static IReadOnlyList<Actor> Collect(
        WorldTile centerTile,
        int radius,
        int maxCandidates,
        int maxResults,
        Func<Actor, bool> predicate,
        Actor primaryTarget = null)
    {
        return CollectCore(
            centerTile,
            radius,
            maxCandidates,
            maxResults,
            predicate,
            null,
            null,
            primaryTarget);
    }

    /// <summary>
    /// Context-aware overload for combat hot paths. Callers can pass a cached static
    /// delegate plus the caster/owner as context instead of allocating a closure for
    /// every range query. Semantics are identical to the Func&lt;Actor,bool&gt; overload.
    /// </summary>
    internal static IReadOnlyList<Actor> Collect(
        WorldTile centerTile,
        int radius,
        int maxCandidates,
        int maxResults,
        Actor predicateContext,
        ContextActorPredicate predicate,
        Actor primaryTarget = null)
    {
        return CollectCore(
            centerTile,
            radius,
            maxCandidates,
            maxResults,
            null,
            predicateContext,
            predicate,
            primaryTarget);
    }

    private static IReadOnlyList<Actor> CollectCore(
        WorldTile centerTile,
        int radius,
        int maxCandidates,
        int maxResults,
        Func<Actor, bool> predicate,
        Actor predicateContext,
        ContextActorPredicate contextPredicate,
        Actor primaryTarget)
    {
        if (centerTile == null || radius <= 0 || maxCandidates <= 0 || maxResults <= 0)
            return Array.Empty<Actor>();

        List<Actor> result = new List<Actor>(Math.Min(Math.Min(maxCandidates, maxResults), 16));
        bool ownsScratch = _scratchDepth == 0;
        _scratchDepth++;
        HashSet<long> seen = ownsScratch
            ? (_seenScratch ??= new HashSet<long>())
            : new HashSet<long>();
        List<Candidate> candidates = ownsScratch
            ? (_candidateScratch ??= new List<Candidate>(64))
            : new List<Candidate>(Math.Min(maxCandidates, 64));
        seen.Clear();
        candidates.Clear();
        try
        {
            TryAddPrimary(
                primaryTarget,
                centerTile,
                radius,
                predicate,
                predicateContext,
                contextPredicate,
                result,
                seen,
                maxResults);
            if (result.Count >= maxResults) return result;

            IEnumerable<Actor> source;
            try { source = Finder.getUnitsFromChunk(centerTile, radius, 0f, false); }
            catch { return result; }
            if (source == null) return result;

            int rawScanLimit = Math.Max(maxCandidates, Math.Min(512, maxCandidates * 4));
            int rawScanned = 0;
            try
            {
                foreach (Actor actor in source)
                {
                    if (rawScanned++ >= rawScanLimit || candidates.Count >= maxCandidates) break;
                    if (!TryBuildCandidate(
                        actor,
                        centerTile,
                        radius,
                        predicate,
                        predicateContext,
                        contextPredicate,
                        seen,
                        out Candidate candidate)) continue;
                    candidates.Add(candidate);
                }
            }
            catch (System.Exception ex)
            {
                XuanJianVNext.Core.XjExceptionDiagnostics.Report(
                    "code/XuanJianVNext/Systems/Runtime/XjLocalActorQuery.cs:Collect",
                    ex);
            }

            candidates.Sort(CompareCandidates);
            int remaining = maxResults - result.Count;
            for (int i = 0; i < candidates.Count && i < remaining; i++)
            {
                Actor actor = candidates[i].Actor;
                if (actor?.data != null) result.Add(actor);
            }
            return result;
        }
        finally
        {
            // Candidate holds Actor references; clear immediately so the thread-local
            // scratch never extends actor lifetime beyond the current query.
            candidates.Clear();
            seen.Clear();
            _scratchDepth = Math.Max(0, _scratchDepth - 1);
        }
    }

    private static int CompareCandidates(Candidate left, Candidate right)
    {
        int distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
        if (distance != 0) return distance;
        return left.ActorId.CompareTo(right.ActorId);
    }

    private static bool TryBuildCandidate(
        Actor actor,
        WorldTile centerTile,
        int radius,
        Func<Actor, bool> predicate,
        Actor predicateContext,
        ContextActorPredicate contextPredicate,
        HashSet<long> seen,
        out Candidate candidate)
    {
        candidate = default;
        if (actor?.data == null || !MatchesPredicate(actor, predicate, predicateContext, contextPredicate)) return false;

        WorldTile tile;
        try { tile = ((BaseSimObject)actor).current_tile; }
        catch { tile = null; }
        if (tile == null) return false;

        Vector2Int center = centerTile.pos;
        Vector2Int pos = tile.pos;
        int dx = pos.x - center.x;
        int dy = pos.y - center.y;
        int distanceSquared = dx * dx + dy * dy;
        if (distanceSquared > radius * radius) return false;

        long actorId = GetActorId(actor);
        if (actorId > 0L && !seen.Add(actorId)) return false;
        candidate = new Candidate(actor, actorId, distanceSquared);
        return true;
    }

    private static void TryAddPrimary(
        Actor actor,
        WorldTile centerTile,
        int radius,
        Func<Actor, bool> predicate,
        Actor predicateContext,
        ContextActorPredicate contextPredicate,
        List<Actor> result,
        HashSet<long> seen,
        int maxResults)
    {
        if (actor?.data == null || result.Count >= maxResults
            || !MatchesPredicate(actor, predicate, predicateContext, contextPredicate)) return;
        WorldTile tile;
        try { tile = ((BaseSimObject)actor).current_tile; }
        catch { tile = null; }
        if (tile == null) return;

        Vector2Int center = centerTile.pos;
        Vector2Int pos = tile.pos;
        int dx = pos.x - center.x;
        int dy = pos.y - center.y;
        if (dx * dx + dy * dy > radius * radius) return;

        long actorId = GetActorId(actor);
        if (actorId > 0L && !seen.Add(actorId)) return;
        result.Add(actor);
    }

    private static bool MatchesPredicate(
        Actor actor,
        Func<Actor, bool> predicate,
        Actor predicateContext,
        ContextActorPredicate contextPredicate)
    {
        if (contextPredicate != null) return contextPredicate(predicateContext, actor);
        return predicate == null || predicate(actor);
    }

    private static long GetActorId(Actor actor)
    {
        try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
        catch { return 0L; }
    }
}
