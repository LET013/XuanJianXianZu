using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Shared, revision-keyed Sect read model for authority member snapshots.
///
/// The authority store remains the only writer. Consumers receive one immutable
/// sorted snapshot per Sect revision instead of cloning/sorting the same member
/// records independently in projection, audit and UI-adjacent reads.
/// </summary>
internal sealed class XjSectReadModel
{
    private const int MaxCachedSectMemberLists = 1024;

    internal static XjSectReadModel Shared { get; } = new XjSectReadModel();

    private readonly Dictionary<long, MemberSnapshotCacheEntry> _memberSnapshots =
        new Dictionary<long, MemberSnapshotCacheEntry>();

    private readonly struct MemberSnapshotCacheEntry
    {
        internal readonly int Revision;
        internal readonly IReadOnlyList<XjSectMemberArchiveRecord> Members;

        internal MemberSnapshotCacheEntry(int revision, IReadOnlyList<XjSectMemberArchiveRecord> members)
        {
            Revision = revision;
            Members = members ?? Array.Empty<XjSectMemberArchiveRecord>();
        }
    }

    private XjSectReadModel()
    {
    }

    internal IReadOnlyList<XjSectMemberArchiveRecord> ReadMembers(long sectId)
    {
        if (sectId <= 0L) return Array.Empty<XjSectMemberArchiveRecord>();

        int revision = XjRelationEntityRevisionStore.GetSectRevision(sectId);
        if (_memberSnapshots.TryGetValue(sectId, out MemberSnapshotCacheEntry cached)
            && cached.Revision == revision)
        {
            return cached.Members;
        }

        XjSectMemberArchiveRecord[] built = XjSectAuthorityStore.BuildMemberReadModelSnapshot(sectId);
        IReadOnlyList<XjSectMemberArchiveRecord> stable = built.Length == 0
            ? Array.Empty<XjSectMemberArchiveRecord>()
            : Array.AsReadOnly(built);
        if (_memberSnapshots.Count >= MaxCachedSectMemberLists) _memberSnapshots.Clear();
        _memberSnapshots[sectId] = new MemberSnapshotCacheEntry(revision, stable);
        return stable;
    }

    internal void Clear()
    {
        _memberSnapshots.Clear();
    }
}
