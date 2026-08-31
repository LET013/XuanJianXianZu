using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 神通易象的唯一执行入口。变化必须由“显位道途+来源道途+准确权柄+旧形+新形”
/// 的显式规则触发，并把新形绑定到实际来源权柄。来源权柄转交、撤回或失落后，
/// 仅在没有其他仍有效权柄共同维持该新形时退显；不再按权柄序位或数组下标猜测。
/// </summary>
internal static class XjShenTongMutationService
{
    private const int DefaultPermille = 1000;
    private const int CurrentRecoverySchema = 1;

    private readonly struct AuthorityCandidate
    {
        internal readonly string SourceDaoTu;
        internal readonly string Authority;
        internal readonly string Reason;

        internal AuthorityCandidate(string sourceDaoTu, string authority, string reason)
        {
            SourceDaoTu = (sourceDaoTu ?? string.Empty).Trim();
            Authority = (authority ?? string.Empty).Trim();
            Reason = reason ?? string.Empty;
        }
    }

    internal static void TickAnnual(Actor actor, int currentYear)
    {
        if (actor?.data == null || currentYear <= 0) return;
        string realmId = XjRealmHelper.NormalizeId(
            XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
        if (!string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
            && !string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
            && !string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenTongMutationLastYear, out int lastYear)
            && lastYear >= currentYear) return;
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenTongMutationLastYear, currentYear);
        string bindingSnapshot = ReadBindingsRaw(actor, actorId);

        bool mutationAvailable = !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenTongActualMutationLastYear, out int actualYear)
            || actualYear < currentYear;
        if (mutationAvailable)
        {
            bool recoveredMutation = false;
            XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenTongMutationRecoverySchema, out int recoverySchema);
            // 旧档恢复只执行一次。权柄注册表尚未导入时不写版本，避免抢跑后永久漏迁移。
            if (recoverySchema < CurrentRecoverySchema
                && XjGuoWeiQuanBingRegistry.TryGet(actorId, out _))
            {
                recoveredMutation = TryApplyRecoveredAuthorityMutation(actor, currentYear);
                XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenTongMutationRecoverySchema, CurrentRecoverySchema);
            }
            if (!recoveredMutation
                && !string.IsNullOrWhiteSpace(bindingSnapshot))
            {
                TryApplyLostAuthorityRegression(actor, currentYear);
            }
        }
        XjXianJiState state = XjXianJiAccessor.BuildState(actor);
        if (!state.Found || state.Ids.Length == 0)
        {
            XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenTongDifficultyPermille, DefaultPermille);
            return;
        }

        Dictionary<string, string> previous = ParseSnapshot(ReadString(actor, XjActorDataKeys.XjShenTongAuthoritySnapshot));
        Dictionary<string, string> current = new Dictionary<string, string>(StringComparer.Ordinal);
        int totalWeight = 0;
        int valid = 0;
        List<string> meaningfulChanges = new List<string>();
        for (int i = 0; i < state.Ids.Length; i++)
        {
            string id = XjXianJiCatalog.NormalizeXianJiId(state.Ids[i]);
            if (string.IsNullOrWhiteSpace(id) || !XjXianJiCatalog.TryResolveOwningDaoTu(id, out string owner)) continue;
            int weight = XjDaoLineageStateRegistry.ResolveShenTongCandidateWeight(owner, id, out string authority, out string status);
            status = string.IsNullOrWhiteSpace(status) ? "潜" : status.Trim();
            current[id] = status;
            totalWeight += Math.Max(1, weight);
            valid++;
            if (previous.TryGetValue(id, out string oldStatus)
                && !string.Equals(oldStatus, status, StringComparison.Ordinal)
                && IsMeaningfulStatusChange(oldStatus, status))
            {
                string intention = XjDaoIntentionCatalog.Resolve(owner, authority);
                string suffix = string.IsNullOrWhiteSpace(intention) ? string.Empty : "（" + intention + "意向）";
                meaningfulChanges.Add("【" + id + "】" + suffix + "由“" + oldStatus + "”转为“" + status + "”");
            }
        }

        int permille = valid <= 0
            ? DefaultPermille
            : Math.Clamp((int)Math.Round(totalWeight * 125d / valid), 650, 1350);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenTongDifficultyPermille, permille);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongAuthoritySnapshot, SerializeSnapshot(current));

        // 初次建立快照不补写历史；之后只有跨越强状态边界才记入三书，并受果位权柄公告开关与统一配额治理。
        if (previous.Count > 0 && meaningfulChanges.Count > 0)
        {
            string actorName = actor.getName();
            string body = actorName + "所修神通受当世权柄升降牵动：" + string.Join("；", meaningfulChanges)
                + "。此后神通修持" + (permille >= 1000 ? "较前顺遂" : "较前艰涩") + "。";
            AppendHistory(actor, body);
            XjThreeBookWriter.RecordShenTongAuthorityChanged(actor, body, currentYear);
            XjWorldHistoryStore.RecordDomainEvent(
                XjWorldHistoryCategory.Cultivation,
                "神通随柄",
                body,
                importance: 4,
                actorId: ((BaseSystemData)actor.data).id,
                actorName: actorName,
                year: currentYear,
                iconIdOverride: XjEventIconCatalog.HistoryCultivation,
                eventType: "ShenTongAuthorityChanged",
                result: XjHistoryResult.Transfer,
                mirrorToWorldLog: false);
            XjBroadcastSystem.ShowRecordedCategorizedWorldTip(
                "【权柄牵动】" + body,
                XjAnnouncementCategory.AuthorityPosition,
                duration: 5.5f,
                color: "#C69B5A");
        }
    }

    internal static float ResolveCultivationMultiplier(Actor actor)
    {
        if (actor?.data == null) return 1f;
        if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenTongDifficultyPermille, out int value)
            || value <= 0) return 1f;
        return Math.Clamp(value / 1000f, 0.65f, 1.35f);
    }

    /// <summary>
    /// 权柄夺取完成时即时尝试神通易象。一次夺柄至多变化一门神通。
    /// 若角色已持新形（旧档或其他来源先行显化），只登记额外维持来源，不重复公告。
    /// </summary>
    internal static bool OnAuthoritySeized(
        Actor actor, string sourceDaoTu, string authority, int currentYear, string reason)
    {
        if (actor?.data == null) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        using (XjHighRealmAggregateStore.BeginReduction(actorId, currentYear))
        {
            bool yearAvailable = !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenTongActualMutationLastYear, out int lastYear)
                || lastYear < currentYear;
            bool changed = yearAvailable
                && TryApplyAuthorityGain(actor, sourceDaoTu, authority, currentYear, reason, recovered: false);
            if (!changed) TryRegisterHeldUpper(actor, sourceDaoTu, authority);
            XjGuoWeiImageStateService.Refresh(actor, currentYear, recordHistory: true);
            return changed;
        }
    }

    internal static bool OnAuthorityReturned(Actor actor, int currentYear, string reason)
    {
        if (actor?.data == null || currentYear <= 0) return false;
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenTongActualMutationLastYear, out int lastYear)
            && lastYear >= currentYear) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        using (XjHighRealmAggregateStore.BeginReduction(actorId, currentYear))
        {
            bool changed = TryApplyLostAuthorityRegression(actor, currentYear);
            XjGuoWeiImageStateService.Refresh(actor, currentYear, recordHistory: changed);
            return changed;
        }
    }

    private static bool TryApplyAuthorityGain(
        Actor actor, string sourceDaoTu, string authority, int currentYear, string reason, bool recovered)
    {
        if (actor?.data == null || currentYear <= 0 || string.IsNullOrWhiteSpace(authority)) return false;
        string manifest = ResolveManifestDaoTu(actor);
        if (manifest.Length == 0) return false;
        string source = (sourceDaoTu ?? string.Empty).Trim();
        if (source.Length == 0) return false;
        XjXianJiState state = XjXianJiAccessor.BuildState(actor);
        if (!state.Found || !XjAuthorityShenTongMutationCatalog.TryResolveGain(
            manifest, source, authority, state, out XjAuthorityShenTongMutationCatalog.Rule rule)) return false;

        string authorityDisplay = XjDaoIntentionCatalog.FormatAuthority(source, authority);
        string cause = string.IsNullOrWhiteSpace(reason) ? "权柄易位" : reason.Trim();
        string mutationReason = cause + "：得" + source + authorityDisplay + "，据" + rule.ProofMethod;
        string prefix = recovered ? "【旧档易象】" : "【神通易象】";
        string announcement;
        if (string.Equals(manifest, "坎水", StringComparison.Ordinal)
            && string.Equals(source, "府水", StringComparison.Ordinal)
            && string.Equals(rule.Lower, "泾龙王", StringComparison.Ordinal)
            && string.Equals(rule.Upper, "浩瀚海", StringComparison.Ordinal))
        {
            announcement = prefix + actor.getName() + "夺得府水" + authorityDisplay
                + "，坎水神通【泾龙王】随浩瀚权柄易位，化为【浩瀚海】。";
        }
        else
        {
            announcement = prefix + actor.getName() + "得" + source + authorityDisplay + "，其" + manifest
                + "神通【" + rule.Lower + "】受此柄牵引，显化为【" + rule.Upper + "】。";
        }
        if (!TryChangeShenTong(actor, rule.Lower, rule.Upper, currentYear, mutationReason, announcement)) return false;
        AddBinding(actor, new XjHighRealmMutationBinding(rule.SourceDaoTu, rule.SourceAuthority, rule.Lower, rule.Upper));
        return true;
    }

    private static bool TryRegisterHeldUpper(Actor actor, string sourceDaoTu, string authority)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(sourceDaoTu) || string.IsNullOrWhiteSpace(authority)) return false;
        string manifest = ResolveManifestDaoTu(actor);
        XjXianJiState state = XjXianJiAccessor.BuildState(actor);
        if (!state.Found || !XjAuthorityShenTongMutationCatalog.TryResolveHeldUpper(
            manifest, sourceDaoTu, authority, state, out XjAuthorityShenTongMutationCatalog.Rule rule)) return false;
        return AddBinding(actor, new XjHighRealmMutationBinding(rule.SourceDaoTu, rule.SourceAuthority, rule.Lower, rule.Upper));
    }

    /// <summary>
    /// 旧档恢复：逐项扫描实际仍持有的本地、夺取与融入权柄。
    /// 有旧形则完成一次易象；已有新形则只补来源绑定。
    /// </summary>
    private static bool TryApplyRecoveredAuthorityMutation(Actor actor, int currentYear)
    {
        long actorId = ((BaseSystemData)actor.data).id;
        if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState registryState)) return false;
        string manifest = ResolveManifestDaoTu(actor);
        if (manifest.Length == 0) return false;
        if (!XjHighRealmAggregateStore.TryGetAuthoritySets(
            actorId,
            out IReadOnlyList<string> local,
            out IReadOnlyList<string> seized,
            out IReadOnlyList<string> foreign))
        {
            XjHighRealmAggregateStore.ApplyAuthority(in registryState);
            XjHighRealmAggregateStore.TryGetAuthoritySets(actorId, out local, out seized, out foreign);
        }

        List<AuthorityCandidate> candidates = new List<AuthorityCandidate>();
        for (int i = 0; i < local.Count; i++)
        {
            if (XjGuoWeiQuanBingRegistry.IsAuthorityLost(manifest, local[i])) continue;
            candidates.Add(new AuthorityCandidate(manifest, local[i], "本道权柄显化"));
        }

        IReadOnlyList<string>[] sets = { seized, foreign };
        for (int setIndex = 0; setIndex < sets.Length; setIndex++)
        {
            IReadOnlyList<string> authorities = sets[setIndex];
            for (int i = 0; i < authorities.Count; i++)
            {
                if (!TryResolveSourceDaoTu(manifest, authorities[i], registryState, out string sourceDaoTu)) continue;
                candidates.Add(new AuthorityCandidate(sourceDaoTu, authorities[i], "旧档权柄归位"));
            }
        }

        // 先补齐所有“已有新形”的来源绑定，再进行本年度唯一一次实际变化。
        // 这样即使本年第一柄触发了易象，旧档中其他已显化神通也不会失去退显依据。
        for (int i = 0; i < candidates.Count; i++)
            TryRegisterHeldUpper(actor, candidates[i].SourceDaoTu, candidates[i].Authority);
        for (int i = 0; i < candidates.Count; i++)
            if (TryApplyAuthorityGain(actor, candidates[i].SourceDaoTu, candidates[i].Authority, currentYear, candidates[i].Reason, recovered: true)) return true;
        return false;
    }

    /// <summary>
    /// 精确退显：先移除全部已经失去的来源绑定，再判断同一新形是否仍被其他权柄维持。
    /// 每人每年最多实际退显一门，但所有失效绑定会在本次一并清理。
    /// </summary>
    private static bool TryApplyLostAuthorityRegression(Actor actor, int currentYear)
    {
        string manifest = ResolveManifestDaoTu(actor);
        if (manifest.Length == 0) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState registryState))
            return false;
        List<XjHighRealmMutationBinding> bindings = ReadBindings(actor, actorId);
        List<XjHighRealmMutationBinding> active = new List<XjHighRealmMutationBinding>();
        List<XjHighRealmMutationBinding> stale = new List<XjHighRealmMutationBinding>();
        for (int i = 0; i < bindings.Count; i++)
        {
            if (IsAuthorityHeld(actorId, manifest, bindings[i])) active.Add(bindings[i]);
            else stale.Add(bindings[i]);
        }

        XjXianJiState state = XjXianJiAccessor.BuildState(actor);
        List<XjHighRealmMutationBinding> remaining = new List<XjHighRealmMutationBinding>(active);
        int selectedIndex = -1;
        for (int i = 0; i < stale.Count; i++)
        {
            XjHighRealmMutationBinding lost = stale[i];
            // 仍有其他有效权柄维持同一新形时，该失效来源可直接清除。
            if (IsUpperSupported(active, lost.Upper)) continue;
            // 新形已经不存在或旧形已恢复时，不再保留待退显任务。
            if (!HasShenTong(state, lost.Upper) || HasShenTong(state, lost.Lower)) continue;
            // 其余待退显绑定必须保留到后续年度，不能因“一年只退显一门”而丢失因果。
            remaining.Add(lost);
            if (selectedIndex < 0) selectedIndex = remaining.Count - 1;
        }

        if (selectedIndex >= 0)
        {
            XjHighRealmMutationBinding lost = remaining[selectedIndex];
            string authorityDisplay = XjDaoIntentionCatalog.FormatAuthority(lost.SourceDaoTu, lost.Authority);
            string reason = lost.SourceDaoTu + authorityDisplay + "离身";
            string announcement = "【神通退显】" + actor.getName() + "所依" + lost.SourceDaoTu + authorityDisplay
                + "已经离身、归还原道或被他人夺走，神通【" + lost.Upper + "】退显为【" + lost.Lower + "】。";
            if (TryChangeShenTong(actor, lost.Upper, lost.Lower, currentYear, reason, announcement))
            {
                remaining.RemoveAt(selectedIndex);
                SaveBindings(actor, remaining);
                return true;
            }
        }
        SaveBindings(actor, remaining);

        // 兼容没有绑定数据的旧档：仅允许准确的本道失柄规则退显，不再推断外道来源。
        IReadOnlyList<string> localAuthorities = XjGuoWeiAuthorityCatalog.Get(manifest);
        state = XjXianJiAccessor.BuildState(actor);
        for (int i = 0; i < localAuthorities.Count; i++)
        {
            string authority = localAuthorities[i];
            if (!XjGuoWeiQuanBingRegistry.IsAuthorityLost(manifest, authority)) continue;
            if (!state.Found || !XjAuthorityShenTongMutationCatalog.TryResolveLocalLoss(
                manifest, authority, state, out XjAuthorityShenTongMutationCatalog.Rule rule)) continue;
            string authorityDisplay = XjDaoIntentionCatalog.FormatAuthority(manifest, authority);
            string reason = manifest + authorityDisplay + "失落";
            string announcement = "【神通退显】" + actor.getName() + "所依" + manifest + authorityDisplay
                + "已经正式失落，神通【" + rule.Upper + "】退显为【" + rule.Lower + "】。";
            if (TryChangeShenTong(actor, rule.Upper, rule.Lower, currentYear, reason, announcement)) return true;
        }
        return false;
    }

    private static bool TryChangeShenTong(
        Actor actor, string oldId, string newId, int currentYear, string reason, string announcement)
    {
        if (!XjXianJiAccessor.TryReplace(actor, oldId, newId, currentYear, reason)) return false;
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenTongActualMutationLastYear, currentYear);
        AppendHistory(actor, oldId + "→" + newId + "（" + reason + "）");
        XjThreeBookWriter.RecordShenTongChanged(actor, oldId, newId, currentYear, reason);
        XjBroadcastSystem.BroadcastBLevelActorEvent(
            actor,
            announcement,
            announcement,
            XjEventIconCatalog.JinDanUpgrade,
            XjAnnouncementCategory.ShenTong);
        return true;
    }

    private static bool TryResolveSourceDaoTu(
        string manifest, string authority, in XjGuoWeiQuanBingState state, out string sourceDaoTu)
    {
        sourceDaoTu = string.Empty;
        if (XjDaoIntentionCatalog.TryResolveAuthorityOwner(authority, out string exactOwner, out _))
        {
            sourceDaoTu = (exactOwner ?? string.Empty).Trim();
            if (sourceDaoTu.Length > 0) return true;
        }
        if (XjAuthorityShenTongMutationCatalog.TryResolveUniqueSource(manifest, authority, out string unique))
        {
            sourceDaoTu = unique;
            return true;
        }
        string pending = (state.PendingExternalZhengWeiDaoTu ?? string.Empty).Trim();
        if (pending.Length > 0)
        {
            sourceDaoTu = pending;
            return true;
        }
        return false;
    }

    internal static bool HasBinding(Actor actor, string sourceDaoTu, string authority, string upper)
    {
        if (actor?.data == null) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        string expectedSource = (sourceDaoTu ?? string.Empty).Trim();
        string expectedAuthority = XjAuthorityShenTongMutationCatalog.NormalizeAuthority(authority);
        string expectedUpper = XjXianJiCatalog.NormalizeXianJiId(upper);
        List<XjHighRealmMutationBinding> bindings = ReadBindings(actor, actorId);
        for (int i = 0; i < bindings.Count; i++)
        {
            XjHighRealmMutationBinding binding = bindings[i];
            if (string.Equals(binding.SourceDaoTu, expectedSource, StringComparison.Ordinal)
                && XjAuthorityShenTongMutationCatalog.AuthorityEquals(binding.Authority, expectedAuthority)
                && string.Equals(binding.Upper, expectedUpper, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAuthorityHeld(long actorId, string manifest, in XjHighRealmMutationBinding binding)
    {
        if (!binding.IsValid || actorId <= 0L) return false;
        if (string.Equals(manifest, binding.SourceDaoTu, StringComparison.Ordinal))
        {
            return XjHighRealmAggregateStore.ContainsAuthority(
                    actorId, binding.Authority, includeLocal: true, includeSeized: false, includeForeign: false)
                && !XjGuoWeiQuanBingRegistry.IsAuthorityLost(binding.SourceDaoTu, binding.Authority);
        }
        return XjHighRealmAggregateStore.ContainsAuthority(
            actorId, binding.Authority, includeLocal: false, includeSeized: true, includeForeign: true);
    }

    private static bool IsUpperSupported(List<XjHighRealmMutationBinding> bindings, string upper)
    {
        string expected = XjXianJiCatalog.NormalizeXianJiId(upper);
        for (int i = 0; i < bindings.Count; i++)
            if (string.Equals(bindings[i].Upper, expected, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool HasShenTong(in XjXianJiState state, string id)
    {
        if (!state.Found || state.Ids == null) return false;
        string expected = XjXianJiCatalog.NormalizeXianJiId(id);
        for (int i = 0; i < state.Ids.Length; i++)
            if (string.Equals(XjXianJiCatalog.NormalizeXianJiId(state.Ids[i]), expected, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool AddBinding(Actor actor, in XjHighRealmMutationBinding binding)
    {
        if (actor?.data == null || !binding.IsValid) return false;
        List<XjHighRealmMutationBinding> bindings = ReadBindings(actor, ((BaseSystemData)actor.data).id);
        for (int i = 0; i < bindings.Count; i++)
            if (string.Equals(bindings[i].Key, binding.Key, StringComparison.Ordinal)) return false;
        bindings.Add(binding);
        SaveBindings(actor, bindings);
        return true;
    }

    private static void SaveBindings(Actor actor, List<XjHighRealmMutationBinding> bindings)
    {
        if (actor?.data == null) return;
        List<string> entries = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < bindings.Count; i++)
        {
            XjHighRealmMutationBinding binding = bindings[i];
            if (!binding.IsValid || !seen.Add(binding.Key)) continue;
            entries.Add(binding.Key.Replace(";", string.Empty));
        }
        entries.Sort(StringComparer.Ordinal);
        string serialized = string.Join(";", entries);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongMutationBindings, serialized);
        long actorId = ((BaseSystemData)actor.data).id;
        XjHighRealmAggregateStore.ApplyMutationBindings(actorId, serialized);
    }

    private static string ResolveManifestDaoTu(Actor actor)
    {
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string manifest);
        if (string.IsNullOrWhiteSpace(manifest)) XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out manifest);
        if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(manifest, out string display)) manifest = display;
        return (manifest ?? string.Empty).Trim();
    }

    private static bool IsMeaningfulStatusChange(string oldStatus, string newStatus)
    {
        return StatusBand(oldStatus) != StatusBand(newStatus);
    }

    private static int StatusBand(string status)
    {
        return status switch
        {
            "易" or "执" => 4,
            "归" or "显" => 3,
            "潜" or "借" or "藏" => 2,
            "裂" => 1,
            "失" => 0,
            _ => 2
        };
    }

    private static Dictionary<string, string> ParseSnapshot(string text)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] entries = (text ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < entries.Length; i++)
        {
            int split = entries[i].IndexOf('=');
            if (split <= 0 || split >= entries[i].Length - 1) continue;
            result[entries[i].Substring(0, split).Trim()] = entries[i].Substring(split + 1).Trim();
        }
        return result;
    }

    private static string SerializeSnapshot(Dictionary<string, string> map)
    {
        List<string> entries = new List<string>();
        foreach (KeyValuePair<string, string> pair in map)
            entries.Add(pair.Key.Replace(";", string.Empty) + "=" + pair.Value.Replace(";", string.Empty));
        entries.Sort(StringComparer.Ordinal);
        return string.Join(";", entries);
    }

    private static string ReadBindingsRaw(Actor actor, long actorId)
    {
        if (XjHighRealmAggregateStore.TryGetMutationBindingsRaw(actorId, out string cached)) return cached;
        string raw = ReadString(actor, XjActorDataKeys.XjShenTongMutationBindings);
        XjHighRealmAggregateStore.ApplyMutationBindings(actorId, raw);
        return raw;
    }

    private static List<XjHighRealmMutationBinding> ReadBindings(Actor actor, long actorId)
    {
        if (!XjHighRealmAggregateStore.TryGetMutationBindings(
            actorId,
            out IReadOnlyList<XjHighRealmMutationBinding> cached))
        {
            ReadBindingsRaw(actor, actorId);
            XjHighRealmAggregateStore.TryGetMutationBindings(actorId, out cached);
        }
        return cached == null
            ? new List<XjHighRealmMutationBinding>()
            : new List<XjHighRealmMutationBinding>(cached);
    }

    private static string ReadString(Actor actor, string key)
    {
        return XjActorAccessor.TryGetString(actor, key, out string value) ? value ?? string.Empty : string.Empty;
    }

    private static void AppendHistory(Actor actor, string line)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(line)) return;
        string current = ReadString(actor, XjActorDataKeys.XjShenTongMutationHistory);
        List<string> entries = new List<string>((current ?? string.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
        if (!entries.Contains(line)) entries.Add(line.Trim());
        while (entries.Count > 8) entries.RemoveAt(0);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongMutationHistory, string.Join("|", entries));
    }
}
