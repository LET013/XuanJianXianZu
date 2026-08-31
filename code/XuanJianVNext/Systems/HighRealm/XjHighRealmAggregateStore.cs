using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjHighRealmMutationBinding
{
    internal readonly string SourceDaoTu;
    internal readonly string Authority;
    internal readonly string Lower;
    internal readonly string Upper;

    internal XjHighRealmMutationBinding(string sourceDaoTu, string authority, string lower, string upper)
    {
        SourceDaoTu = (sourceDaoTu ?? string.Empty).Trim();
        Authority = XjAuthorityShenTongMutationCatalog.NormalizeAuthority(authority);
        Lower = XjXianJiCatalog.NormalizeXianJiId(lower);
        Upper = XjXianJiCatalog.NormalizeXianJiId(upper);
    }

    internal bool IsValid => SourceDaoTu.Length > 0
        && Authority.Length > 0
        && Lower.Length > 0
        && Upper.Length > 0;

    internal string Key => SourceDaoTu + "~" + Authority + "~" + Lower + "~" + Upper;
}

/// <summary>
/// 高境运行态单一聚合表。角色字符串与世界档案仍是持久化格式；正常运行、战斗和UI
/// 只读取本表中的权柄计数、果位真景、神通绑定及道论投影。各领域写入在归约作用域内
/// 合并为一次 Revision 递增，避免一次夺柄同时触发数次UI失效和缓存抖动。
/// </summary>
internal static class XjHighRealmAggregateStore
{
    private sealed class Entry
    {
        internal long ActorId;
        internal bool HasAuthority;
        internal XjGuoWeiQuanBingState Authority;
        internal string[] LocalAuthorities = Array.Empty<string>();
        internal string[] SeizedAuthorities = Array.Empty<string>();
        internal string[] ForeignAuthorities = Array.Empty<string>();
        internal int ActiveAuthorityCount;
        internal bool HasImage;
        internal int ImageStage;
        internal string ImageIntegrity = string.Empty;
        internal int ImageCombatScore;
        internal string ImageSummary = string.Empty;
        internal bool HasMutationBindings;
        internal string MutationBindingsRaw = string.Empty;
        internal XjHighRealmMutationBinding[] MutationBindings = Array.Empty<XjHighRealmMutationBinding>();
        internal bool HasDoctrine;
        internal XjHighRealmDoctrineSnapshot Doctrine;
        internal int Revision;
        internal int ReductionDepth;
        internal bool PendingRevision;
        internal int LastTouchedYear;
    }

    internal readonly struct ReductionScope : IDisposable
    {
        private readonly long _actorId;
        private readonly XjActorStateRevisionStore.ReductionScope _actorRevisionScope;

        internal ReductionScope(long actorId, XjActorStateRevisionStore.ReductionScope actorRevisionScope)
        {
            _actorId = actorId;
            _actorRevisionScope = actorRevisionScope;
        }

        public void Dispose()
        {
            EndReduction(_actorId);
            _actorRevisionScope.Dispose();
        }
    }

    private static readonly Dictionary<long, Entry> Entries = new Dictionary<long, Entry>();

    internal static int Count => Entries.Count;

    internal static ReductionScope BeginReduction(long actorId, int currentYear)
    {
        if (actorId <= 0L) return default;
        Entry entry = GetOrCreate(actorId);
        entry.ReductionDepth++;
        entry.LastTouchedYear = Math.Max(entry.LastTouchedYear, currentYear);
        return new ReductionScope(actorId, XjActorStateRevisionStore.BeginReduction(actorId, currentYear));
    }

    internal static void ApplyAuthority(in XjGuoWeiQuanBingState state)
    {
        if (!state.Found || state.ActorId <= 0L) return;
        Entry entry = GetOrCreate(state.ActorId);
        bool changed = !entry.HasAuthority || !AuthorityEquals(in entry.Authority, in state);
        if (!changed) return;

        bool authoritySetsChanged = !entry.HasAuthority
            || !string.Equals(entry.Authority.LocalQuanBing, state.LocalQuanBing, StringComparison.Ordinal)
            || !string.Equals(entry.Authority.SeizedQuanBing, state.SeizedQuanBing, StringComparison.Ordinal)
            || !string.Equals(entry.Authority.ForeignQuanBing, state.ForeignQuanBing, StringComparison.Ordinal);
        if (authoritySetsChanged)
        {
            entry.LocalAuthorities = ParseAuthoritySet(state.LocalQuanBing);
            entry.SeizedAuthorities = ParseAuthoritySet(state.SeizedQuanBing);
            entry.ForeignAuthorities = ParseAuthoritySet(state.ForeignQuanBing);
            entry.ActiveAuthorityCount = entry.LocalAuthorities.Length + entry.SeizedAuthorities.Length;
        }

        entry.HasAuthority = true;
        entry.Authority = state;
        Touch(entry);
    }

    internal static void ApplyImage(long actorId, int stage, string integrity, string summary = "")
    {
        if (actorId <= 0L) return;
        Entry entry = GetOrCreate(actorId);
        string normalizedIntegrity = (integrity ?? string.Empty).Trim();
        string normalizedSummary = summary ?? string.Empty;
        int score = ResolveImageScore(stage, normalizedIntegrity);
        bool changed = !entry.HasImage
            || entry.ImageStage != Math.Max(0, stage)
            || entry.ImageCombatScore != score
            || !string.Equals(entry.ImageIntegrity, normalizedIntegrity, StringComparison.Ordinal)
            || (normalizedSummary.Length > 0
                && !string.Equals(entry.ImageSummary, normalizedSummary, StringComparison.Ordinal));
        entry.HasImage = true;
        entry.ImageStage = Math.Max(0, stage);
        entry.ImageIntegrity = normalizedIntegrity;
        entry.ImageCombatScore = score;
        if (normalizedSummary.Length > 0) entry.ImageSummary = normalizedSummary;
        if (changed) Touch(entry);
    }

    internal static void ApplyMutationBindings(long actorId, string rawBindings)
    {
        if (actorId <= 0L) return;
        Entry entry = GetOrCreate(actorId);
        string normalized = (rawBindings ?? string.Empty).Trim();
        if (entry.HasMutationBindings
            && string.Equals(entry.MutationBindingsRaw, normalized, StringComparison.Ordinal)) return;
        entry.HasMutationBindings = true;
        entry.MutationBindingsRaw = normalized;
        entry.MutationBindings = ParseMutationBindings(normalized);
        Touch(entry);
    }

    internal static void ApplyDoctrine(long actorId, in XjHighRealmDoctrineSnapshot doctrine)
    {
        if (actorId <= 0L) return;
        Entry entry = GetOrCreate(actorId);
        bool changed = !entry.HasDoctrine || !DoctrineEquals(in entry.Doctrine, in doctrine);
        entry.HasDoctrine = doctrine.Found;
        entry.Doctrine = doctrine;
        if (changed) Touch(entry);
    }

    internal static bool TryGetAuthority(long actorId, out XjGuoWeiQuanBingState state)
    {
        if (actorId > 0L && Entries.TryGetValue(actorId, out Entry entry) && entry.HasAuthority)
        {
            state = entry.Authority;
            return state.Found;
        }
        state = default;
        return false;
    }

    internal static bool TryGetAuthorityCount(long actorId, out int count)
    {
        if (actorId > 0L && Entries.TryGetValue(actorId, out Entry entry) && entry.HasAuthority)
        {
            count = entry.ActiveAuthorityCount;
            return true;
        }
        count = 0;
        return false;
    }

    internal static bool TryGetAuthoritySets(
        long actorId,
        out IReadOnlyList<string> local,
        out IReadOnlyList<string> seized,
        out IReadOnlyList<string> foreign)
    {
        if (actorId > 0L && Entries.TryGetValue(actorId, out Entry entry) && entry.HasAuthority)
        {
            local = entry.LocalAuthorities ?? Array.Empty<string>();
            seized = entry.SeizedAuthorities ?? Array.Empty<string>();
            foreign = entry.ForeignAuthorities ?? Array.Empty<string>();
            return true;
        }
        local = Array.Empty<string>();
        seized = Array.Empty<string>();
        foreign = Array.Empty<string>();
        return false;
    }

    internal static bool ContainsAuthority(
        long actorId,
        string authority,
        bool includeLocal,
        bool includeSeized,
        bool includeForeign)
    {
        if (actorId <= 0L
            || string.IsNullOrWhiteSpace(authority)
            || !Entries.TryGetValue(actorId, out Entry entry)
            || !entry.HasAuthority)
        {
            return false;
        }

        string expected = XjAuthorityShenTongMutationCatalog.NormalizeAuthority(authority);
        return (includeLocal && ContainsAuthority(entry.LocalAuthorities, expected))
            || (includeSeized && ContainsAuthority(entry.SeizedAuthorities, expected))
            || (includeForeign && ContainsAuthority(entry.ForeignAuthorities, expected));
    }

    internal static bool TryGetImageScore(long actorId, out int score)
    {
        if (actorId > 0L && Entries.TryGetValue(actorId, out Entry entry) && entry.HasImage)
        {
            score = entry.ImageCombatScore;
            return true;
        }
        score = 0;
        return false;
    }

    internal static bool TryGetImageState(long actorId, out int stage, out string integrity)
    {
        if (actorId > 0L && Entries.TryGetValue(actorId, out Entry entry) && entry.HasImage)
        {
            stage = entry.ImageStage;
            integrity = entry.ImageIntegrity ?? string.Empty;
            return true;
        }
        stage = 0;
        integrity = string.Empty;
        return false;
    }

    internal static bool TryGetImageSummary(long actorId, out string summary)
    {
        if (actorId > 0L && Entries.TryGetValue(actorId, out Entry entry)
            && entry.HasImage && !string.IsNullOrWhiteSpace(entry.ImageSummary))
        {
            summary = entry.ImageSummary;
            return true;
        }
        summary = string.Empty;
        return false;
    }

    internal static bool TryGetMutationBindingsRaw(long actorId, out string rawBindings)
    {
        if (actorId > 0L && Entries.TryGetValue(actorId, out Entry entry) && entry.HasMutationBindings)
        {
            rawBindings = entry.MutationBindingsRaw ?? string.Empty;
            return true;
        }
        rawBindings = string.Empty;
        return false;
    }

    internal static bool TryGetMutationBindings(
        long actorId,
        out IReadOnlyList<XjHighRealmMutationBinding> bindings)
    {
        if (actorId > 0L && Entries.TryGetValue(actorId, out Entry entry) && entry.HasMutationBindings)
        {
            bindings = entry.MutationBindings ?? Array.Empty<XjHighRealmMutationBinding>();
            return true;
        }
        bindings = Array.Empty<XjHighRealmMutationBinding>();
        return false;
    }

    internal static bool TryGetDoctrine(long actorId, out XjHighRealmDoctrineSnapshot doctrine)
    {
        if (actorId > 0L && Entries.TryGetValue(actorId, out Entry entry) && entry.HasDoctrine)
        {
            doctrine = entry.Doctrine;
            return doctrine.Found;
        }
        doctrine = default;
        return false;
    }

    internal static int GetRevision(long actorId)
    {
        return actorId > 0L && Entries.TryGetValue(actorId, out Entry entry)
            ? entry.Revision
            : 0;
    }

    internal static int ResolveImageScore(int stage, string integrity)
    {
        int score = Math.Max(0, stage);
        if (string.Equals(integrity, "圆融", StringComparison.Ordinal)) score++;
        else if (string.Equals(integrity, "残缺", StringComparison.Ordinal)
            || string.Equals(integrity, "异化", StringComparison.Ordinal)) score--;
        return Math.Max(0, score);
    }

    internal static void RemoveAuthority(long actorId)
    {
        if (actorId <= 0L || !Entries.TryGetValue(actorId, out Entry entry)) return;
        if (!entry.HasAuthority) return;
        entry.HasAuthority = false;
        entry.Authority = default;
        entry.LocalAuthorities = Array.Empty<string>();
        entry.SeizedAuthorities = Array.Empty<string>();
        entry.ForeignAuthorities = Array.Empty<string>();
        entry.ActiveAuthorityCount = 0;
        Touch(entry);
    }

    internal static void RemoveActor(long actorId)
    {
        if (actorId > 0L) Entries.Remove(actorId);
    }

    internal static void Clear()
    {
        Entries.Clear();
    }

    private static Entry GetOrCreate(long actorId)
    {
        if (!Entries.TryGetValue(actorId, out Entry entry))
        {
            entry = new Entry { ActorId = actorId };
            Entries[actorId] = entry;
        }
        return entry;
    }

    private static void EndReduction(long actorId)
    {
        if (actorId <= 0L || !Entries.TryGetValue(actorId, out Entry entry)) return;
        if (entry.ReductionDepth > 0) entry.ReductionDepth--;
        if (entry.ReductionDepth == 0 && entry.PendingRevision)
        {
            unchecked { entry.Revision++; }
            entry.PendingRevision = false;
        }
    }

    private static void Touch(Entry entry)
    {
        if (entry == null) return;
        XjActorStateRevisionStore.Mark(entry.ActorId, XjActorStateDomain.HighRealm);
        if (entry.ReductionDepth > 0)
        {
            entry.PendingRevision = true;
            return;
        }
        unchecked { entry.Revision++; }
    }

    private static XjHighRealmMutationBinding[] ParseMutationBindings(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<XjHighRealmMutationBinding>();
        List<XjHighRealmMutationBinding> result = new List<XjHighRealmMutationBinding>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int start = 0;
        for (int i = 0; i <= raw.Length; i++)
        {
            bool end = i == raw.Length;
            if (!end && raw[i] != ';' && raw[i] != '；') continue;
            if (i > start)
            {
                string entry = raw.Substring(start, i - start);
                string[] parts = entry.Split(new[] { '~' }, StringSplitOptions.None);
                if (parts.Length == 4)
                {
                    XjHighRealmMutationBinding binding =
                        new XjHighRealmMutationBinding(parts[0], parts[1], parts[2], parts[3]);
                    if (binding.IsValid && seen.Add(binding.Key)) result.Add(binding);
                }
            }
            start = i + 1;
        }
        return result.Count == 0 ? Array.Empty<XjHighRealmMutationBinding>() : result.ToArray();
    }

    private static string[] ParseAuthoritySet(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        List<string> result = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int start = 0;
        for (int i = 0; i <= raw.Length; i++)
        {
            bool end = i == raw.Length;
            char c = end ? '\0' : raw[i];
            if (!end && c != ',' && c != '，' && c != '|' && c != '、' && c != ';' && c != '；')
            {
                continue;
            }

            if (i > start)
            {
                string value = raw.Substring(start, i - start).Trim();
                if (value.Length > 0 && seen.Add(value)) result.Add(value);
            }
            start = i + 1;
        }
        return result.Count == 0 ? Array.Empty<string>() : result.ToArray();
    }

    private static bool ContainsAuthority(string[] values, string expected)
    {
        if (values == null || values.Length == 0 || string.IsNullOrWhiteSpace(expected)) return false;
        for (int i = 0; i < values.Length; i++)
        {
            if (XjAuthorityShenTongMutationCatalog.AuthorityEquals(values[i], expected)) return true;
        }
        return false;
    }

    private static bool AuthorityEquals(in XjGuoWeiQuanBingState left, in XjGuoWeiQuanBingState right)
    {
        return left.Found == right.Found
            && left.ActorId == right.ActorId
            && string.Equals(left.ActorName, right.ActorName, StringComparison.Ordinal)
            && string.Equals(left.DaoTu, right.DaoTu, StringComparison.Ordinal)
            && string.Equals(left.GuoWei, right.GuoWei, StringComparison.Ordinal)
            && string.Equals(left.LocalQuanBing, right.LocalQuanBing, StringComparison.Ordinal)
            && string.Equals(left.SeizedQuanBing, right.SeizedQuanBing, StringComparison.Ordinal)
            && string.Equals(left.SeizedQuanBingSources, right.SeizedQuanBingSources, StringComparison.Ordinal)
            && string.Equals(left.ForeignQuanBing, right.ForeignQuanBing, StringComparison.Ordinal)
            && string.Equals(left.WithdrawnToDongTian, right.WithdrawnToDongTian, StringComparison.Ordinal)
            && string.Equals(left.GuoWeiZhongAi, right.GuoWeiZhongAi, StringComparison.Ordinal)
            && string.Equals(left.PendingExternalZhengWeiDaoTu, right.PendingExternalZhengWeiDaoTu, StringComparison.Ordinal)
            && left.LockUntilYear == right.LockUntilYear
            && left.IntegrationRetreatActive == right.IntegrationRetreatActive
            && left.IntegrationRetreatEndYear == right.IntegrationRetreatEndYear
            && string.Equals(left.Summary, right.Summary, StringComparison.Ordinal)
            && string.Equals(left.LifecycleStatus, right.LifecycleStatus, StringComparison.Ordinal)
            && left.AcquiredYear == right.AcquiredYear
            && left.ReleasedYear == right.ReleasedYear
            && string.Equals(left.ReleaseReason, right.ReleaseReason, StringComparison.Ordinal);
    }

    private static bool DoctrineEquals(in XjHighRealmDoctrineSnapshot left, in XjHighRealmDoctrineSnapshot right)
    {
        return left.Found == right.Found
            && string.Equals(left.PositionType, right.PositionType, StringComparison.Ordinal)
            && string.Equals(left.SourceDaoTu, right.SourceDaoTu, StringComparison.Ordinal)
            && string.Equals(left.ManifestDaoTu, right.ManifestDaoTu, StringComparison.Ordinal)
            && string.Equals(left.RunFormula, right.RunFormula, StringComparison.Ordinal)
            && string.Equals(left.Doctrine, right.Doctrine, StringComparison.Ordinal)
            && string.Equals(left.DaoTitle, right.DaoTitle, StringComparison.Ordinal)
            && string.Equals(left.JinXing, right.JinXing, StringComparison.Ordinal)
            && string.Equals(left.AuthorityScope, right.AuthorityScope, StringComparison.Ordinal)
            && string.Equals(left.ProofCityName, right.ProofCityName, StringComparison.Ordinal)
            && string.Equals(left.LegacyDoctrine, right.LegacyDoctrine, StringComparison.Ordinal)
            && left.DaoProgress == right.DaoProgress
            && left.PositionImage == right.PositionImage
            && string.Equals(left.RespectPractice, right.RespectPractice, StringComparison.Ordinal);
    }
}
