using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;

using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Events;

namespace XuanJianVNext.Systems.HighRealm;

internal sealed class XjDaoTuPositionCountView
{
    internal string DaoTu = string.Empty;
    internal int ZhengActive;
    internal int ZhengHidden;
    internal int ZhengCapacity = 1;
    // 普通角色持果以计数展示；背景高位、阴司和已封果位没有可扫描的 Actor，
    // 因而必须另存显世状态，不能让 UI 把它们误画成“空缺”或泄露背景设定。
    internal string ZhengStatus = string.Empty;
    internal int YuActive;
    internal int YuDefined;
    internal int YuCapacity;
    internal int RunActive;
    internal int RunDefined;
    internal int RunCapacity;
    internal int ActiveAuthorityHolders;
    internal string AuthorityHolderStatus = string.Empty;
}

internal sealed class XjGuoWeiAuthorityChangeView
{
    internal int Year;
    internal string Kind = string.Empty;
    internal string DaoTu = string.Empty;
    internal string Title = string.Empty;
    internal string Detail = string.Empty;
    internal long ActorId;
}

internal sealed class XjGuoWeiAuthorityCodexSnapshot
{
    internal int Revision;
    internal int WorldYear;
    internal IReadOnlyList<XjDaoTuPositionCountView> PositionCounts = Array.Empty<XjDaoTuPositionCountView>();
    internal IReadOnlyList<XjDerivedPositionArchiveRecord> DerivedPositions = Array.Empty<XjDerivedPositionArchiveRecord>();
    internal IReadOnlyList<XjDaoTaiPositionBindingArchiveRecord> DaoTaiBindings = Array.Empty<XjDaoTaiPositionBindingArchiveRecord>();
    internal IReadOnlyList<XjGuoWeiAuthorityChangeView> Changes = Array.Empty<XjGuoWeiAuthorityChangeView>();
}

/// <summary>
/// 果位权柄页的只读模型。只读取果位账本、权柄账本与余闰位置档案，
/// 不扫描活体角色，也不在UI阶段修复任何状态。
/// </summary>
internal static class XjGuoWeiAuthorityCodexReadModel
{
    internal static int GetRevisionToken()
    {
        unchecked
        {
            return (XjGuoWeiRegistry.Revision * 397)
                ^ (XjGuoWeiQuanBingRegistry.Revision * 31)
                ^ (XjDaoLineageStateRegistry.Revision * 131)
                ^ (XjFruitPositionWorldState.LastUpdatedYear * 17)
                ^ XjFruitPositionWorldState.BindingRevision
                ^ (XjTaiYinHiddenFruitSystem.Revision * 43)
                // 两个节点可在同一年内改变背景位序。若不纳入缓存键，已打开的百科会
                // 继续复用触发前的“空缺”快照，直到跨年才自愈。
                ^ (XjHongXiaLuoXiaEvent.IsTriggered ? 0x2D31 : 0)
                ^ (XjYuanZhaoKongZhengEvent.IsTriggered ? 0x71B9 : 0);
        }
    }

    internal static XjGuoWeiAuthorityCodexSnapshot Build(int worldYear)
    {
        IReadOnlyList<XjGuoWeiRegistryEntry> guoWeiEntries = XjGuoWeiRegistry.ReadAllEntries();
        IReadOnlyList<XjGuoWeiQuanBingState> authorityEntries = XjGuoWeiQuanBingRegistry.ReadAllEntries();
        IReadOnlyList<XjGuoWeiQuanBingLostAuthorityArchiveData> lostAuthorities = XjGuoWeiQuanBingRegistry.ReadLostAuthorityRecords();
        IReadOnlyList<XjDaoLineageArchiveRecord> lineages = XjDaoLineageStateRegistry.ReadAllLineages();
        IReadOnlyList<XjDerivedPositionArchiveRecord> positions = XjFruitPositionWorldState.ReadPositionsSnapshot();
        IReadOnlyList<XjDaoTaiPositionBindingArchiveRecord> daoTaiBindings = XjFruitPositionWorldState.ReadDaoTaiBindingsSnapshot();

        Dictionary<string, XjDaoTuPositionCountView> counts = new Dictionary<string, XjDaoTuPositionCountView>(StringComparer.Ordinal);
        IReadOnlyList<string> allDaoTus = XjGuoWeiAuthorityCatalog.GetAllDaoTus();
        for (int i = 0; i < allDaoTus.Count; i++) EnsureCount(counts, allDaoTus[i]);

        List<XjGuoWeiAuthorityChangeView> changes = new List<XjGuoWeiAuthorityChangeView>();
        HashSet<string> changeKeys = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < guoWeiEntries.Count; i++)
        {
            XjGuoWeiRegistryEntry entry = guoWeiEntries[i];
            if (!entry.Found) continue;
            XjDaoTuPositionCountView count = EnsureCount(counts, entry.DaoTu);
            string type = XjGuoWeiRegistry.ResolveTypeFromName(entry.GuoWei);
            if (entry.IsActive)
            {
                if (string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) count.ZhengActive++;
                else if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) count.RunActive++;
                else count.YuActive++;
            }
            AddChange(changes, changeKeys, entry.Year, "证位", entry.DaoTu,
                "证成" + FormatPositionName(entry.GuoWei),
                BuildHolderDetail(entry.ActorName, entry.FamilyName, "家族"), entry.ActorId);
            if (entry.EndedYear > 0)
            {
                AddChange(changes, changeKeys, entry.EndedYear, "离位", entry.DaoTu,
                    FormatPositionName(entry.GuoWei) + "终结",
                    BuildHolderDetail(entry.ActorName, FormatInternalEndReason(entry.EndReason), "缘由"), entry.ActorId);
            }
        }

        HashSet<string> normallyActivePositions = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < guoWeiEntries.Count; i++)
        {
            XjGuoWeiRegistryEntry entry = guoWeiEntries[i];
            if (entry.Found && entry.IsActive && !string.IsNullOrWhiteSpace(entry.GuoWei))
                normallyActivePositions.Add(XjGuoWeiCalculator.NormalizeGuoWeiName(entry.GuoWei));
        }
        for (int i = 0; i < daoTaiBindings.Count; i++)
        {
            XjDaoTaiPositionBindingArchiveRecord binding = daoTaiBindings[i];
            if (binding == null || binding.ActorId <= 0L
                || !XjFruitPositionWorldState.TryGetPosition(binding.SecondaryPositionId, out XjDerivedPositionArchiveRecord secondary)
                || secondary == null) continue;

            string secondaryId = XjGuoWeiCalculator.NormalizeGuoWeiName(secondary.PositionId);
            if (secondaryId.Length == 0 || normallyActivePositions.Contains(secondaryId)) continue;

            XjDaoTuPositionCountView count = EnsureCount(counts, secondary.DaoTu);
            if (string.Equals(secondary.PositionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) count.ZhengActive++;
            else if (string.Equals(secondary.PositionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)) count.YuActive++;
            else if (string.Equals(secondary.PositionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) count.RunActive++;
            else continue;

            string title = XjDaoTaiDualPositionSystem.GetBindingTitle(binding);
            string pairDisplay = XjDaoTaiDualPositionSystem.TryResolveBindingPair(binding,
                out XjDerivedPositionArchiveRecord fruit, out XjDerivedPositionArchiveRecord derived)
                ? XjGuoWeiCalculator.GetDisplayGuoWeiName(fruit.PositionId) + " ＋ "
                    + XjGuoWeiCalculator.GetDisplayGuoWeiName(derived.PositionId)
                : XjGuoWeiCalculator.GetDisplayGuoWeiName(secondary.PositionId);
            AddChange(changes, changeKeys, binding.BoundYear, "兼位", secondary.DaoTu,
                title + "：" + pairDisplay,
                "持位者：" + NormalizeArchiveClause(ResolveActorDisplay(guoWeiEntries, binding.ActorId)), binding.ActorId);
        }

        for (int i = 0; i < positions.Count; i++)
        {
            XjDerivedPositionArchiveRecord position = positions[i];
            if (position == null) continue;
            XjDaoTuPositionCountView count = EnsureCount(counts, position.DaoTu);
            if (string.Equals(position.PositionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)) count.YuDefined++;
            else if (string.Equals(position.PositionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) count.RunDefined++;
            else continue;

            string kind = string.Equals(position.PositionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? "闰位" : "余位";
            AddChange(changes, changeKeys, position.FoundedYear, "开位", position.DaoTu,
                "开辟" + kind + "「" + XjGuoWeiCalculator.GetDisplayGuoWeiName(position.PositionId) + "」",
                BuildHolderDetail(position.FounderName, position.JinXingName, "金性"), position.FounderActorId);
            if (position.LastHeldYear > 0)
            {
                AddChange(changes, changeKeys, position.LastHeldYear, "承继", position.DaoTu,
                    kind + "「" + XjGuoWeiCalculator.GetDisplayGuoWeiName(position.PositionId) + "」更替",
                    "持位者：" + NormalizeArchiveClause(string.IsNullOrWhiteSpace(position.LastHolderDisplay) ? "未载" : position.LastHolderDisplay),
                    position.LastHolderActorId);
            }
        }

        // 渊照空证前，太阴/坎水对玩家始终显示为“有人在位但不披露”，
        // 防止位序总览把时代封锁误画成空果。这里只改只读统计，不伪造 Actor/果位账本。
        if (XjYuanZhaoFruitSealPolicy.IsHiddenSourceFruitOccupancy(XjYuanZhaoKongZhengEvent.SourceTaiYin, worldYear))
            EnsureCount(counts, XjYuanZhaoKongZhengEvent.SourceTaiYin).ZhengActive = Math.Max(1, EnsureCount(counts, XjYuanZhaoKongZhengEvent.SourceTaiYin).ZhengActive);
        if (XjYuanZhaoFruitSealPolicy.IsHiddenSourceFruitOccupancy(XjYuanZhaoKongZhengEvent.SourceKanShui, worldYear))
            EnsureCount(counts, XjYuanZhaoKongZhengEvent.SourceKanShui).ZhengActive = Math.Max(1, EnsureCount(counts, XjYuanZhaoKongZhengEvent.SourceKanShui).ZhengActive);

        // 薛畋属于背景高位存在，不生成普通 Actor；落霞山只显化山门地标。戊土第一闰在
        // 本局起录前就已经由薛畋执掌，因此从开局起就必须占掉一席；300年事件
        // 以前只隐藏持位者身份。虹霞正果则到空证显世后才成立。
        string reservedWuTuRun = XjGuoWeiCalculator.NormalizeGuoWeiName(XjHongXiaLuoXiaEvent.WuTuReservedRunWeiId);
        if (!normallyActivePositions.Contains(reservedWuTuRun))
        {
            EnsureCount(counts, XjHongXiaLuoXiaEvent.SourceWuTu).RunActive++;
        }
        if (XjHongXiaLuoXiaEvent.IsTriggered)
        {
            XjDaoTuPositionCountView hongXiaCount = EnsureCount(counts, XjHongXiaLuoXiaEvent.DaoTu);
            hongXiaCount.ZhengActive = Math.Max(1, hongXiaCount.ZhengActive);
        }

        ApplyFixedZhengWeiPresentation(counts);


        // 太阴藏果只改变可见/可求状态，不伪造或删除真实果位占用。空果被藏也必须显示“晦隐”，避免误报为空缺。
        if (XjTaiYinHiddenFruitSystem.TryGetActiveVeiledPosition(out string veiledPosition))
        {
            string normalizedVeiled = XjGuoWeiCalculator.NormalizeGuoWeiName(veiledPosition);
            if (normalizedVeiled.EndsWith(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
            {
                string veiledDaoTu = normalizedVeiled.Substring(0, normalizedVeiled.Length - XjGuoWeiCalculator.ZhengWei.Length);
                EnsureCount(counts, veiledDaoTu).ZhengHidden = 1;
            }
        }
        if (XjTaiYinHiddenFruitSystem.IsFamilyLegacyHidden) EnsureCount(counts, XjTaiYinHiddenFruitSystem.TaiYinDaoTu).ZhengHidden = 1;

        for (int i = 0; i < authorityEntries.Count; i++)
        {
            XjGuoWeiQuanBingState state = authorityEntries[i];
            if (!state.Found) continue;
            if (state.ReleasedYear <= 0)
            {
                EnsureCount(counts, state.DaoTu).ActiveAuthorityHolders++;
            }
            string detail = BuildAuthorityDetail(in state);
            AddChange(changes, changeKeys, state.AcquiredYear, "执柄", state.DaoTu,
                "执掌" + FormatPositionName(state.GuoWei) + "权柄", detail, state.ActorId);
            if (state.ReleasedYear > 0)
            {
                AddChange(changes, changeKeys, state.ReleasedYear, "失柄", state.DaoTu,
                    "失去" + FormatPositionName(state.GuoWei) + "权柄",
                    BuildHolderDetail(state.ActorName, XjDisplayNameSanitizer.ReleaseReason(state.ReleaseReason), "缘由"), state.ActorId);
            }
        }

        // 外道夺柄的“得到”过去只藏在当前持有详情里，而时间线只有原道“丢失”。
        // 直接读取权威道统状态，把【借】和【易】分别作为待融夺得/正式融得展示。
        for (int i = 0; i < lineages.Count; i++)
        {
            XjDaoLineageArchiveRecord lineage = lineages[i];
            if (lineage?.Authorities == null) continue;
            for (int a = 0; a < lineage.Authorities.Count; a++)
            {
                XjDaoAuthorityArchiveData authority = lineage.Authorities[a];
                if (authority == null || string.IsNullOrWhiteSpace(authority.Name)
                    || string.IsNullOrWhiteSpace(authority.SourceDaoTu)
                    || string.Equals(authority.SourceDaoTu.Trim(), lineage.DaoTu?.Trim(), StringComparison.Ordinal)) continue;
                string status = (authority.Status ?? string.Empty).Trim();
                if (!string.Equals(status, "借", StringComparison.Ordinal)
                    && !string.Equals(status, "易", StringComparison.Ordinal)) continue;

                string holder = string.IsNullOrWhiteSpace(authority.HolderName)
                    ? string.Empty
                    : "；持有者：" + NormalizeArchiveClause(authority.HolderName.Trim());
                string title = string.Equals(status, "借", StringComparison.Ordinal)
                    ? "夺得权柄「" + authority.Name.Trim() + "」（待融）"
                    : "融得权柄「" + authority.Name.Trim() + "」";
                AddChange(changes, changeKeys, authority.LastChangedYear, "夺得", lineage.DaoTu,
                    title, "流转：" + authority.SourceDaoTu.Trim() + " → " + lineage.DaoTu.Trim() + holder, authority.HolderActorId);
            }
        }

        for (int i = 0; i < lostAuthorities.Count; i++)
        {
            XjGuoWeiQuanBingLostAuthorityArchiveData lost = lostAuthorities[i];
            if (lost == null) continue;
            EnsureCount(counts, lost.SourceDaoTu);
            AddChange(changes, changeKeys, lost.Year, "权柄异动", lost.SourceDaoTu,
                "根权柄「" + lost.Authority + "」失落",
                string.IsNullOrWhiteSpace(lost.TargetDaoTu)
                    ? "缘由：" + NormalizeArchiveClause(lost.Reason)
                    : "流向：" + lost.TargetDaoTu + "；缘由：" + NormalizeArchiveClause(lost.Reason),
                0L);
        }

        List<XjDaoTuPositionCountView> countRows = new List<XjDaoTuPositionCountView>(counts.Values);
        countRows.Sort((left, right) => string.Compare(left.DaoTu, right.DaoTu, StringComparison.Ordinal));
        List<XjDerivedPositionArchiveRecord> derivedPositions = new List<XjDerivedPositionArchiveRecord>();
        for (int i = 0; i < positions.Count; i++)
        {
            XjDerivedPositionArchiveRecord position = positions[i];
            if (position == null
                || string.Equals(position.PositionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
            {
                continue;
            }
            derivedPositions.Add(position);
        }
        changes.Sort((left, right) =>
        {
            int byYear = right.Year.CompareTo(left.Year);
            if (byYear != 0) return byYear;
            int byDao = string.Compare(left.DaoTu, right.DaoTu, StringComparison.Ordinal);
            return byDao != 0 ? byDao : string.Compare(left.Title, right.Title, StringComparison.Ordinal);
        });
        if (changes.Count > 96) changes.RemoveRange(96, changes.Count - 96);

        return new XjGuoWeiAuthorityCodexSnapshot
        {
            Revision = GetRevisionToken(),
            WorldYear = Math.Max(0, worldYear),
            PositionCounts = countRows,
            DerivedPositions = derivedPositions,
            DaoTaiBindings = daoTaiBindings,
            Changes = changes
        };
    }

    private static string ResolveActorDisplay(IReadOnlyList<XjGuoWeiRegistryEntry> entries, long actorId)
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                XjGuoWeiRegistryEntry entry = entries[i];
                if (!entry.Found || entry.ActorId != actorId) continue;
                return FormatActorDisplay(entry.ActorName, entry.FamilyName);
            }
        }
        if (actorId > 0L && XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) && actor?.data != null)
            return actor.getName();
        return "一位道胎";
    }

    private static XjDaoTuPositionCountView EnsureCount(
        Dictionary<string, XjDaoTuPositionCountView> counts,
        string daoTu)
    {
        string normalized = (daoTu ?? string.Empty).Trim();
        if (normalized.Length == 0) normalized = "未定道途";
        if (counts.TryGetValue(normalized, out XjDaoTuPositionCountView existing)) return existing;
        XjFruitPositionCapacity capacity = XjFruitPositionWorldState.GetCapacity(normalized);
        XjDaoTuPositionCountView created = new XjDaoTuPositionCountView
        {
            DaoTu = normalized,
            YuCapacity = capacity.Residual,
            RunCapacity = XjZiJinSwordDaoCatalog.IsLongGeng(normalized) ? 0 : capacity.Intercalary
        };
        counts[normalized] = created;
        return created;
    }

    /// <summary>
    /// 这些位序不是普通地图 Actor 持有，不能从果位/权柄账本中取得人物记录；
    /// 但它们在玩法判定中已被占据或封闭。只读页仅表现“显世/未显”，
    /// 不伪造角色、权柄账本，也不提前泄露背景存在的姓名与归属。
    /// </summary>
    private static void ApplyFixedZhengWeiPresentation(Dictionary<string, XjDaoTuPositionCountView> counts)
    {
        XjDaoTuPositionCountView zheQi = EnsureCount(counts, "谪炁");
        zheQi.ZhengActive = Math.Max(1, zheQi.ZhengActive);
        zheQi.ZhengStatus = "未显";
        zheQi.AuthorityHolderStatus = "未显";

        XjDaoTuPositionCountView xiaYi = EnsureCount(counts, "下仪");
        xiaYi.ZhengActive = Math.Max(1, xiaYi.ZhengActive);
        xiaYi.ZhengStatus = "未显";
        xiaYi.AuthorityHolderStatus = "未显";

        XjDaoTuPositionCountView baoMu = EnsureCount(counts, "保木");
        baoMu.ZhengActive = Math.Max(1, baoMu.ZhengActive);
        baoMu.ZhengStatus = "未显";
        baoMu.AuthorityHolderStatus = "未显";

        XjDaoTuPositionCountView hongXia = EnsureCount(counts, XjHongXiaLuoXiaEvent.DaoTu);
        hongXia.ZhengStatus = XjHongXiaLuoXiaEvent.IsTriggered ? "显世" : "未显";
        hongXia.AuthorityHolderStatus = "未显";
        if (XjHongXiaLuoXiaEvent.IsTriggered)
            hongXia.ZhengActive = Math.Max(1, hongXia.ZhengActive);

        XjDaoTuPositionCountView yuanZhao = EnsureCount(counts, XjYuanZhaoKongZhengEvent.DaoTu);
        yuanZhao.ZhengStatus = XjYuanZhaoKongZhengEvent.IsTriggered ? "显世" : "未显";
        yuanZhao.AuthorityHolderStatus = "未显";
        if (XjYuanZhaoKongZhengEvent.IsTriggered)
            yuanZhao.ZhengActive = Math.Max(1, yuanZhao.ZhengActive);
    }

    private static void AddChange(
        List<XjGuoWeiAuthorityChangeView> changes,
        HashSet<string> keys,
        int year,
        string kind,
        string daoTu,
        string title,
        string detail,
        long actorId)
    {
        if (year <= 0 || string.IsNullOrWhiteSpace(title)) return;
        string key = year + "|" + kind + "|" + daoTu + "|" + title + "|" + actorId;
        if (!keys.Add(key)) return;
        changes.Add(new XjGuoWeiAuthorityChangeView
        {
            Year = year,
            Kind = kind ?? string.Empty,
            DaoTu = (daoTu ?? string.Empty).Trim(),
            Title = title.Trim(),
            Detail = (detail ?? string.Empty).Trim(),
            ActorId = Math.Max(0L, actorId)
        });
    }

    private static string BuildAuthorityDetail(in XjGuoWeiQuanBingState state)
    {
        List<string> parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(state.ActorName)) parts.Add("持有者：" + NormalizeActorDisplay(state.ActorName.Trim()));
        if (!string.IsNullOrWhiteSpace(state.LocalQuanBing)) parts.Add("本柄：" + state.LocalQuanBing.Trim());
        if (!string.IsNullOrWhiteSpace(state.SeizedQuanBing)) parts.Add("夺柄：" + state.SeizedQuanBing.Trim());
        if (!string.IsNullOrWhiteSpace(state.ForeignQuanBing)) parts.Add("客柄：" + state.ForeignQuanBing.Trim());
        return parts.Count == 0 ? "权柄明细未载" : string.Join("；", parts);
    }

    private static string FormatInternalEndReason(string reason)
    {
        string value = (reason ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        return value switch
        {
            "Death" => "身死离位",
            "AbsoluteDeath" => "真灵俱灭",
            "Reassigned" => "位序改易",
            "Rollback" => "改易撤销",
            "Migration" => "旧档迁移",
            _ => XjDisplayNameSanitizer.GameTerm(value, "离位")
        };
    }

    private static string FormatActorDisplay(string actorName, string suffix)
    {
        string actor = NormalizeActorDisplay(string.IsNullOrWhiteSpace(actorName) ? "持位者未载" : actorName.Trim());
        if (string.IsNullOrWhiteSpace(suffix)) return actor;
        return actor + "（" + NormalizeArchiveClause(suffix.Trim()) + "）";
    }

    private static string FormatPositionName(string raw)
    {
        string value = XjGuoWeiCalculator.GetDisplayGuoWeiName(raw);
        return string.IsNullOrWhiteSpace(value) ? "「位序未载」" : "「" + NormalizeArchiveClause(value.Trim()) + "」";
    }

    private static string BuildHolderDetail(string actorName, string suffix, string suffixLabel)
    {
        string actor = NormalizeActorDisplay(string.IsNullOrWhiteSpace(actorName) ? "未载" : actorName.Trim());
        if (string.IsNullOrWhiteSpace(suffix)) return "持有者：" + actor;
        string label = string.IsNullOrWhiteSpace(suffixLabel) ? "附记" : suffixLabel.Trim();
        return "持有者：" + actor + "；" + label + "：" + NormalizeArchiveClause(suffix.Trim());
    }

    private static string NormalizeActorDisplay(string raw)
    {
        string value = (raw ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        string[] parts = value.Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return value;
        string actor = parts[0].Trim();
        List<string> suffixes = new List<string>(parts.Length - 1);
        for (int i = 1; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Length > 0) suffixes.Add(part);
        }
        return suffixes.Count == 0 ? actor : actor + "（" + string.Join("，", suffixes) + "）";
    }

    private static string NormalizeArchiveClause(string raw)
    {
        string value = (raw ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        // 历史档案曾大量用“ · ”把独立字段串成一行。这里仅清理由 UI/档案
        // 产生的带空格分隔符；角色尊号内部真正使用的“·”保持不动。
        while (value.Contains(" · ", StringComparison.Ordinal))
        {
            value = value.Replace(" · ", "；");
        }
        return value.Trim('；', ' ', '\t', '\r', '\n');
    }


}
