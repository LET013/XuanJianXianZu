using System;
using System.Collections.Generic;

namespace XuanJianVNext.Core;

[Flags]
internal enum XjActorStateDomain
{
    None = 0,
    Progression = 1 << 0,
    Craft = 1 << 1,
    Family = 1 << 2,
    Sect = 1 << 3,
    Equipment = 1 << 4,
    Inventory = 1 << 5,
    Relations = 1 << 6,
    HighRealm = 1 << 7,
    Identity = 1 << 8,
    All = Progression | Craft | Family | Sect | Equipment | Inventory | Relations | HighRealm | Identity
}

/// <summary>
/// 角色窗口与分域只读模型使用的稳定版本令牌。它只随当前角色对应领域变化，
/// 不再因为世界中另一名修士完成年度结算而失效。
/// </summary>
internal readonly struct XjActorRevisionToken : IEquatable<XjActorRevisionToken>
{
    internal readonly int Progression;
    internal readonly int Craft;
    internal readonly int Family;
    internal readonly int Sect;
    internal readonly int Equipment;
    internal readonly int Inventory;
    internal readonly int Relations;
    internal readonly int HighRealm;
    internal readonly int Identity;

    internal XjActorRevisionToken(
        int progression,
        int craft,
        int family,
        int sect,
        int equipment,
        int inventory,
        int relations,
        int highRealm,
        int identity)
    {
        Progression = progression;
        Craft = craft;
        Family = family;
        Sect = sect;
        Equipment = equipment;
        Inventory = inventory;
        Relations = relations;
        HighRealm = highRealm;
        Identity = identity;
    }

    internal int CoreHash
    {
        get
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Progression;
                hash = hash * 31 + HighRealm;
                hash = hash * 31 + Identity;
                return hash;
            }
        }
    }

    internal int RelationsHash
    {
        get
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Family;
                hash = hash * 31 + Sect;
                hash = hash * 31 + Relations;
                return hash;
            }
        }
    }

    internal int EquipmentHash
    {
        get
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Equipment;
                hash = hash * 31 + Inventory;
                return hash;
            }
        }
    }

    internal int CraftHash
    {
        get
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Craft;
                hash = hash * 31 + Equipment;
                hash = hash * 31 + Progression;
                hash = hash * 31 + HighRealm;
                return hash;
            }
        }
    }

    public bool Equals(XjActorRevisionToken other)
    {
        return Progression == other.Progression
            && Craft == other.Craft
            && Family == other.Family
            && Sect == other.Sect
            && Equipment == other.Equipment
            && Inventory == other.Inventory
            && Relations == other.Relations
            && HighRealm == other.HighRealm
            && Identity == other.Identity;
    }

    public override bool Equals(object obj)
    {
        return obj is XjActorRevisionToken other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = CoreHash;
            hash = hash * 31 + RelationsHash;
            hash = hash * 31 + EquipmentHash;
            hash = hash * 31 + Craft;
            return hash;
        }
    }

    public static bool operator ==(XjActorRevisionToken left, XjActorRevisionToken right) => left.Equals(right);
    public static bool operator !=(XjActorRevisionToken left, XjActorRevisionToken right) => !left.Equals(right);
}

internal static class XjActorStateRevisionStore
{
    private sealed class Entry
    {
        internal int Progression;
        internal int Craft;
        internal int Family;
        internal int Sect;
        internal int Equipment;
        internal int Inventory;
        internal int Relations;
        internal int HighRealm;
        internal int Identity;
        internal int ReductionDepth;
        internal XjActorStateDomain Pending;
        internal int LastTouchedYear;
    }

    internal readonly struct ReductionScope : IDisposable
    {
        private readonly long _actorId;

        internal ReductionScope(long actorId)
        {
            _actorId = actorId;
        }

        public void Dispose()
        {
            EndReduction(_actorId);
        }
    }

    private static readonly Dictionary<long, Entry> Entries = new Dictionary<long, Entry>();
    private static int _globalProgressionRevision;

    /// <summary>
    /// World-level freshness token for aggregate progression snapshots. Unlike the old
    /// scheduler completion counter, this advances only when a Progression domain is
    /// actually committed (and once per reduction transaction).
    /// </summary>
    internal static int GlobalProgressionRevision => _globalProgressionRevision;

    internal static ReductionScope BeginReduction(long actorId, int currentYear = 0)
    {
        if (actorId <= 0L) return default;
        Entry entry = GetOrCreate(actorId);
        entry.ReductionDepth++;
        entry.LastTouchedYear = Math.Max(entry.LastTouchedYear, currentYear);
        return new ReductionScope(actorId);
    }

    internal static void Mark(long actorId, XjActorStateDomain domains)
    {
        if (actorId <= 0L || domains == XjActorStateDomain.None) return;
        Entry entry = GetOrCreate(actorId);
        if (entry.ReductionDepth > 0)
        {
            entry.Pending |= domains;
            return;
        }
        Increment(entry, domains);
    }

    internal static XjActorRevisionToken GetToken(long actorId)
    {
        if (actorId <= 0L || !Entries.TryGetValue(actorId, out Entry entry))
        {
            return default;
        }

        return new XjActorRevisionToken(
            entry.Progression,
            entry.Craft,
            entry.Family,
            entry.Sect,
            entry.Equipment,
            entry.Inventory,
            entry.Relations,
            entry.HighRealm,
            entry.Identity);
    }

    internal static void RemoveActor(long actorId)
    {
        if (actorId > 0L) Entries.Remove(actorId);
    }

    internal static void Clear()
    {
        Entries.Clear();
        _globalProgressionRevision = 0;
    }

    internal static XjActorStateDomain ResolveDomainForKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return XjActorStateDomain.Progression;
        string value = key.Trim().ToLowerInvariant();

        XjActorStateDomain result = XjActorStateDomain.None;
        if (ContainsAny(value, "family", "bloodline", "surname", "father", "mother", "child", "generation", "lineage"))
        {
            result |= XjActorStateDomain.Family | XjActorStateDomain.Relations;
        }
        if (ContainsAny(value, "zongmen", "sect", "peak", "fengzhu", "membership", "join_year", "宗门"))
        {
            result |= XjActorStateDomain.Sect | XjActorStateDomain.Relations;
        }
        if (ContainsAny(value, "fabao", "lingbao", "equipment", "weapon_art", "sword_intent", "jianyi", "sword", "jiandao", "器艺"))
        {
            result |= XjActorStateDomain.Equipment | XjActorStateDomain.Craft;
        }
        if (ContainsAny(value, "qiankun", "inventory", "warehouse", "bag", "乾坤"))
        {
            result |= XjActorStateDomain.Inventory;
        }
        if (ContainsAny(value, "alchemy", "artifact", "talisman", "formation", "craft", "liandan", "lianqi", "fulu", "zhenfa"))
        {
            result |= XjActorStateDomain.Craft;
        }
        if (ContainsAny(value, "guowei", "quanbing", "authority", "shentong", "jinxing", "daotai", "doctrine", "mutation", "fruit_position"))
        {
            result |= XjActorStateDomain.HighRealm | XjActorStateDomain.Progression;
        }
        if (ContainsAny(value, "name", "title", "identity", "chushen", "reincarnation", "former_life", "yin_si", "yinsi"))
        {
            result |= XjActorStateDomain.Identity | XjActorStateDomain.Relations;
        }
        if (ContainsAny(value, "realm", "zhenyuan", "ming", "huiguang", "xjzz", "aptitude", "gongfa", "caiqi", "xianji", "qiujin", "jindan", "fuqi", "cultivation", "dao_tu", "daotu"))
        {
            result |= XjActorStateDomain.Progression;
        }

        return result == XjActorStateDomain.None ? XjActorStateDomain.Progression : result;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        if (string.IsNullOrEmpty(value) || needles == null) return false;
        for (int i = 0; i < needles.Length; i++)
        {
            if (!string.IsNullOrEmpty(needles[i]) && value.IndexOf(needles[i], StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static Entry GetOrCreate(long actorId)
    {
        XjStaleActorIdEviction.Track(actorId);
        if (!Entries.TryGetValue(actorId, out Entry entry))
        {
            entry = new Entry();
            Entries[actorId] = entry;
        }
        return entry;
    }

    private static void EndReduction(long actorId)
    {
        if (actorId <= 0L || !Entries.TryGetValue(actorId, out Entry entry)) return;
        if (entry.ReductionDepth > 0) entry.ReductionDepth--;
        if (entry.ReductionDepth != 0 || entry.Pending == XjActorStateDomain.None) return;
        XjActorStateDomain pending = entry.Pending;
        entry.Pending = XjActorStateDomain.None;
        Increment(entry, pending);
    }

    private static void Increment(Entry entry, XjActorStateDomain domains)
    {
        if (entry == null) return;
        unchecked
        {
            if ((domains & XjActorStateDomain.Progression) != 0)
            {
                entry.Progression++;
                _globalProgressionRevision++;
            }
            if ((domains & XjActorStateDomain.Craft) != 0) entry.Craft++;
            if ((domains & XjActorStateDomain.Family) != 0) entry.Family++;
            if ((domains & XjActorStateDomain.Sect) != 0) entry.Sect++;
            if ((domains & XjActorStateDomain.Equipment) != 0) entry.Equipment++;
            if ((domains & XjActorStateDomain.Inventory) != 0) entry.Inventory++;
            if ((domains & XjActorStateDomain.Relations) != 0) entry.Relations++;
            if ((domains & XjActorStateDomain.HighRealm) != 0) entry.HighRealm++;
            if ((domains & XjActorStateDomain.Identity) != 0) entry.Identity++;
        }
    }
}
