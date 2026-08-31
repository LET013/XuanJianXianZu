using System.Collections.Generic;

namespace XuanJianVNext.Core;

/// <summary>
/// 家族、宗门、城市这类“一个实体影响多名角色”的只读投影版本。
/// 角色窗口仍以分角色Revision为主，仅额外组合其所属家族/宗门/城市版本，
/// 避免局部家族/宗门变化去驱动无关的全世界角色进度聚合失效。
/// </summary>
internal static class XjRelationEntityRevisionStore
{
    private const int MaximumRetainedRelationEntities = 16384;

    private static Dictionary<long, int> FamilyRevisions = new Dictionary<long, int>();
    private static Dictionary<long, int> SectRevisions = new Dictionary<long, int>();
    private static int RevisionEpoch;

    internal static int RuntimeEntryCount => FamilyRevisions.Count + SectRevisions.Count;

    internal static int GetFamilyRevision(long familyStableId)
    {
        if (familyStableId <= 0L) return 0;
        FamilyRevisions.TryGetValue(familyStableId, out int revision);
        return ComposeRevision(revision);
    }

    internal static int GetSectRevision(long sectId)
    {
        if (sectId <= 0L) return 0;
        SectRevisions.TryGetValue(sectId, out int revision);
        return ComposeRevision(revision);
    }

    internal static void MarkFamily(long familyStableId)
    {
        if (familyStableId <= 0L) return;
        EnsureCapacityForNewEntity(FamilyRevisions, familyStableId);
        unchecked
        {
            FamilyRevisions.TryGetValue(familyStableId, out int revision);
            FamilyRevisions[familyStableId] = revision + 1;
        }
    }

    internal static void MarkSect(long sectId)
    {
        if (sectId <= 0L) return;
        EnsureCapacityForNewEntity(SectRevisions, sectId);
        unchecked
        {
            SectRevisions.TryGetValue(sectId, out int revision);
            SectRevisions[sectId] = revision + 1;
        }
    }

    internal static void Clear()
    {
        FamilyRevisions = new Dictionary<long, int>();
        SectRevisions = new Dictionary<long, int>();
        RevisionEpoch = 0;
    }

    /// <summary>
    /// Relation revisions are rebuildable presentation metadata, not gameplay facts.
    /// Clearing a Dictionary does not release its peak bucket arrays, so long worlds
    /// with many extinct families/sects/cities can retain their historic high-water.
    /// Hard memory maintenance replaces the containers outright after dependent read
    /// models have been invalidated.
    /// </summary>
    internal static void ReleaseRetainedStorage()
    {
        unchecked { RevisionEpoch++; }
        FamilyRevisions = new Dictionary<long, int>();
        SectRevisions = new Dictionary<long, int>();
    }

    private static void EnsureCapacityForNewEntity(Dictionary<long, int> target, long entityId)
    {
        if (target == null || entityId <= 0L || target.ContainsKey(entityId)) return;
        if (RuntimeEntryCount >= MaximumRetainedRelationEntities) ReleaseRetainedStorage();
    }

    private static int ComposeRevision(int localRevision)
    {
        unchecked
        {
            return (RevisionEpoch * 486187739) ^ localRevision;
        }
    }
}
