using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.ActorSystem;

using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.History;
namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
    private sealed class DaoTuOverviewRow
    {
        internal string DaoTu = string.Empty;
        internal string Phase = string.Empty;
        internal int Vitality;
        internal string Doctrine = string.Empty;
        internal string ShenTongBias = string.Empty;
        internal string Topology = string.Empty;
        internal string Manifestation = string.Empty;
        internal int LastChangedYear;
        internal string FruitHolder = string.Empty;
        internal int ZhengActive;
        internal int ZhengHidden;
        internal int YuActive;
        internal int YuCapacity;
        internal int RunActive;
        internal int RunCapacity;
        internal int AuthorityHolders;
        internal int Lost;
        internal int Fractured;
        internal int Borrowed;
        internal int Integrated;
        internal int Held;
        internal int Dormant;
        internal int Manifest;
        internal int Hidden;
        internal int Returned;
    }

    private readonly List<DaoTuOverviewRow> _daoTuOverviewRows = new List<DaoTuOverviewRow>();
    private string _daoTuOverviewFilter = "全部";
    private int _daoTuOverviewRevision = int.MinValue;
    private int _daoTuOverviewYear = -1;

    private void DrawDaoTuOverviewPage()
    {
        EnsureDaoTuOverviewRows();

        GUILayout.BeginVertical(GUI.skin.box);
        DrawCardStripe("#B7A7FF");
        GUILayout.BeginHorizontal();
        DrawMiniStat("当世道争", XjQuanBingStruggleSystem.BuildCodexStatus(_daoTuOverviewYear), "#B7A7FF", GUILayout.Width(360f));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label("<color=grey>位序只显示真实在位人数；道势回落不会抹去仍在世的旧席位，只会限制空缺席位再次补入。</color>");
        GUILayout.EndVertical();

        _daoTuOverviewFilter = DrawDaoTuCategoryFilterBar(
            _daoTuOverviewFilter, "#74E8FF", new Color(0.25f, 0.52f, 0.62f, 1f));

        int shown = 0;
        for (int i = 0; i < _daoTuOverviewRows.Count; i++)
        {
            DaoTuOverviewRow row = _daoTuOverviewRows[i];
            if (row == null || !MatchesDaoTuCategoryFilter(row.DaoTu, _daoTuOverviewFilter)) continue;
            DrawDaoTuOverviewRow(row);
            shown++;
        }
        if (shown == 0) DrawEmptyCard("当前筛选下没有可显示的道途。", "#777777");
    }

    private void EnsureDaoTuOverviewRows()
    {
        int year = 0;
        try { year = Math.Max(0, World.world?.map_stats?.year ?? 0); }
        catch (Exception xjCaught) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("UI/Codex/DaoTuOverview:Year", xjCaught); }
        int revision = unchecked((XjDaoLineageStateRegistry.Revision * 397)
            ^ XjGuoWeiAuthorityCodexReadModel.GetRevisionToken()
            ^ XjQuanBingStruggleSystem.GetCodexRevisionToken());
        if (_daoTuOverviewRevision == revision && _daoTuOverviewYear == year) return;

        _daoTuOverviewRows.Clear();
        IReadOnlyList<XjDaoLineageArchiveRecord> lineages = XjDaoLineageStateRegistry.ReadAllLineages();
        Dictionary<string, XjDaoLineageArchiveRecord> lineageByDaoTu = new Dictionary<string, XjDaoLineageArchiveRecord>(StringComparer.Ordinal);
        for (int i = 0; i < lineages.Count; i++)
        {
            XjDaoLineageArchiveRecord lineage = lineages[i];
            if (lineage != null && !string.IsNullOrWhiteSpace(lineage.DaoTu)) lineageByDaoTu[lineage.DaoTu] = lineage;
        }

        XjGuoWeiAuthorityCodexSnapshot authority = XjGuoWeiAuthorityCodexReadModel.Build(year);
        Dictionary<string, XjDaoTuPositionCountView> positionByDaoTu = new Dictionary<string, XjDaoTuPositionCountView>(StringComparer.Ordinal);
        for (int i = 0; i < authority.PositionCounts.Count; i++)
        {
            XjDaoTuPositionCountView position = authority.PositionCounts[i];
            if (position != null && !string.IsNullOrWhiteSpace(position.DaoTu)) positionByDaoTu[position.DaoTu] = position;
        }

        Dictionary<string, string> fruitHolderByDaoTu = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyList<XjGuoWeiRegistryEntry> positions = XjGuoWeiRegistry.ReadAllEntries();
        for (int i = 0; i < positions.Count; i++)
        {
            XjGuoWeiRegistryEntry entry = positions[i];
            if (!entry.Found || !entry.IsActive || string.IsNullOrWhiteSpace(entry.DaoTu)) continue;
            if (!string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(entry.GuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) continue;
            fruitHolderByDaoTu[entry.DaoTu] = string.IsNullOrWhiteSpace(entry.ActorName) ? "果位在世" : entry.ActorName;
        }
        IReadOnlyList<XjDaoTaiPositionBindingArchiveRecord> daoTaiBindings = authority.DaoTaiBindings;
        if (daoTaiBindings != null)
        {
            for (int i = 0; i < daoTaiBindings.Count; i++)
            {
                XjDaoTaiPositionBindingArchiveRecord binding = daoTaiBindings[i];
                if (binding == null || binding.ActorId <= 0L
                    || !XjDaoTaiDualPositionSystem.TryResolveBindingPair(binding,
                        out XjDerivedPositionArchiveRecord fruit, out _)
                    || fruit == null || string.IsNullOrWhiteSpace(fruit.DaoTu)) continue;
                string holder = "一位道胎";
                if (XjActorRegistry.ResolveKnownOrWorld(binding.ActorId, out Actor actor) && actor?.data != null)
                    holder = actor.getName();
                fruitHolderByDaoTu[fruit.DaoTu] = holder;
            }
        }

        if (XjYuanZhaoFruitSealPolicy.IsHiddenSourceFruitOccupancy(XjYuanZhaoKongZhengEvent.SourceTaiYin, year))
            fruitHolderByDaoTu[XjYuanZhaoKongZhengEvent.SourceTaiYin] = "未披露";
        if (XjYuanZhaoFruitSealPolicy.IsHiddenSourceFruitOccupancy(XjYuanZhaoKongZhengEvent.SourceKanShui, year))
            fruitHolderByDaoTu[XjYuanZhaoKongZhengEvent.SourceKanShui] = "未披露";
        if (XjHongXiaLuoXiaEvent.IsTriggered)
            fruitHolderByDaoTu[XjHongXiaLuoXiaEvent.DaoTu] = XjHongXiaLuoXiaEvent.FounderName + "（" + XjHongXiaLuoXiaEvent.FactionName + "）";
        if (XjTaiYinHiddenFruitSystem.TryGetActiveVeiledPosition(out string veiledPosition))
        {
            string normalizedVeiled = XjGuoWeiCalculator.NormalizeGuoWeiName(veiledPosition);
            if (normalizedVeiled.EndsWith(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
                fruitHolderByDaoTu[normalizedVeiled.Substring(0, normalizedVeiled.Length - XjGuoWeiCalculator.ZhengWei.Length)] = "月翳晦隐";
        }
        if (XjTaiYinHiddenFruitSystem.IsFamilyLegacyHidden) fruitHolderByDaoTu[XjTaiYinHiddenFruitSystem.TaiYinDaoTu] = "藏于家门月契";

        IReadOnlyList<string> allDaoTus = XjGuoWeiAuthorityCatalog.GetAllDaoTus();
        for (int i = 0; i < allDaoTus.Count; i++)
        {
            string daoTu = allDaoTus[i];
            lineageByDaoTu.TryGetValue(daoTu, out XjDaoLineageArchiveRecord lineage);
            positionByDaoTu.TryGetValue(daoTu, out XjDaoTuPositionCountView position);
            fruitHolderByDaoTu.TryGetValue(daoTu, out string fruitHolder);
            DaoTuOverviewRow row = new DaoTuOverviewRow
            {
                DaoTu = daoTu,
                Phase = lineage?.Phase ?? "守成",
                Vitality = lineage?.Vitality ?? 0,
                Doctrine = lineage?.CoreDoctrine ?? string.Empty,
                ShenTongBias = lineage?.ShenTongBias ?? string.Empty,
                Topology = XjDaoTuRelationCatalog.BuildDisplaySummary(daoTu),
                Manifestation = XjFiveManifestationCatalog.Resolve(daoTu) == XjFiveManifestationKind.Unknown
                    ? string.Empty
                    : XjFiveManifestationCatalog.GetDisplayName(XjFiveManifestationCatalog.Resolve(daoTu)),
                LastChangedYear = lineage?.LastChangedYear ?? 0,
                FruitHolder = fruitHolder ?? string.Empty,
                ZhengActive = position?.ZhengActive ?? 0,
                ZhengHidden = position?.ZhengHidden ?? 0,
                YuActive = position?.YuActive ?? 0,
                YuCapacity = position?.YuCapacity ?? 0,
                RunActive = position?.RunActive ?? 0,
                RunCapacity = position?.RunCapacity ?? 0,
                AuthorityHolders = position?.ActiveAuthorityHolders ?? 0
            };
            CountAuthorityStates(lineage, row);
            _daoTuOverviewRows.Add(row);
        }
        _daoTuOverviewRows.Sort((left, right) => CompareDaoTuByCategory(left.DaoTu, right.DaoTu));
        _daoTuOverviewRevision = revision;
        _daoTuOverviewYear = year;
    }

    private static void CountAuthorityStates(XjDaoLineageArchiveRecord lineage, DaoTuOverviewRow row)
    {
        if (lineage?.Authorities == null || row == null) return;
        for (int i = 0; i < lineage.Authorities.Count; i++)
        {
            XjDaoAuthorityArchiveData authority = lineage.Authorities[i];
            switch ((authority?.Status ?? string.Empty).Trim())
            {
                case "失": row.Lost++; break;
                case "裂": row.Fractured++; break;
                case "借": row.Borrowed++; break;
                case "易": row.Integrated++; break;
                case "执": row.Held++; break;
                case "潜": row.Dormant++; break;
                case "显": row.Manifest++; break;
                case "藏": row.Hidden++; break;
                case "归": row.Returned++; break;
            }
        }
    }

    private void DrawDaoTuOverviewRow(DaoTuOverviewRow row)
    {
        string accent = row.Lost > 0 ? "#FF6666"
            : row.Fractured > 0 || row.Borrowed > 0 ? "#E68B78"
            : row.ZhengHidden > 0 ? "#A7A1D8"
            : row.ZhengActive > 0 ? "#FFD37A" : "#74E8FF";
        GUILayout.BeginVertical(GUI.skin.box);
        DrawCardStripe(accent);
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b><size=21>" + Rich(XjDisplayNameSanitizer.GameTerm(row.DaoTu, "道途未定")) + "</size></b>", GUILayout.Width(130f));
        DrawTag(Empty(row.Phase, "守成"), accent);
        GUILayout.FlexibleSpace();
        GUILayout.Label("<color=grey>末变：" + (row.LastChangedYear > 0 ? XjChronology.FormatYear(row.LastChangedYear) : "未载") + "</color>");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawMiniStat("道势", row.Vitality.ToString(), ResolveVitalityColor(row.Vitality), GUILayout.Width(115f));
        DrawMiniStat("果位", row.ZhengHidden > 0 ? "晦隐" : row.ZhengActive > 0 ? "在位" : "空缺", row.ZhengHidden > 0 ? "#A7A1D8" : row.ZhengActive > 0 ? "#FFD37A" : "#777777", GUILayout.Width(115f));
        DrawMiniStat("余位", "在位 " + row.YuActive, "#A7E08A", GUILayout.Width(125f));
        DrawMiniStat("闰位", "在位 " + row.RunActive, "#D2B5FF", GUILayout.Width(125f));
        DrawMiniStat("权辖者", row.AuthorityHolders.ToString(), "#74E8FF", GUILayout.Width(125f));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Label("果位持有者：<color=#FFD37A>" + Rich(Empty(row.FruitHolder, "空缺")) + "</color>　"
            + "权柄：" + Rich(BuildAuthorityStateSummary(row)));
        GUILayout.Label("<color=grey>道论：" + Rich(Empty(row.Doctrine, "尚未形成稳定道论"))
            + "　·　神通取向：" + Rich(Empty(row.ShenTongBias, "守本")) + "</color>");
        if (!string.IsNullOrWhiteSpace(row.Manifestation))
            GUILayout.Label("<color=#C8AF75>五现：" + Rich(row.Manifestation) + "</color>");
        GUILayout.Label("<color=#8FB9C7>道网：" + Rich(Empty(row.Topology, "无已立拓扑")) + "</color>");
        GUILayout.EndVertical();
    }

    private static string BuildAuthorityStateSummary(DaoTuOverviewRow row)
    {
        List<string> parts = new List<string>(9);
        AddStatePart(parts, "失", row.Lost);
        AddStatePart(parts, "裂", row.Fractured);
        AddStatePart(parts, "借", row.Borrowed);
        AddStatePart(parts, "易", row.Integrated);
        AddStatePart(parts, "执", row.Held);
        AddStatePart(parts, "显", row.Manifest);
        AddStatePart(parts, "潜", row.Dormant);
        AddStatePart(parts, "藏", row.Hidden);
        AddStatePart(parts, "归", row.Returned);
        return parts.Count == 0 ? "无记录" : string.Join(" · ", parts);
    }

    private static void AddStatePart(List<string> parts, string status, int count)
    {
        if (count > 0) parts.Add(status + count);
    }

    private static string ResolveVitalityColor(int vitality)
    {
        if (vitality >= 75) return "#A7E08A";
        if (vitality >= 50) return "#FFD37A";
        if (vitality >= 35) return "#E68B78";
        return "#FF6666";
    }
}
