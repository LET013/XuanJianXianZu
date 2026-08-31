using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.UI.Alchemy;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Craft;
using XuanJianVNext.UI.DaoTu;
using XuanJianVNext.UI.GuoWei;
using XuanJianVNext.UI.LingWu;
using XuanJianVNext.UI.Rank;
using XuanJianVNext.UI.ShenBing;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{

	private void DrawWorldOverview(XjCodexSnapshot snapshot)
	{
			if (_sectPeakDetailSectId > 0L)
			{
				DrawSectPeakDetailPage(snapshot);
				return;
			}
			DrawPageHeader("天下总览", snapshot.PublishedAt + " · 观天下气数、山门格局与近世风云");

			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#6FAE9D");
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=21><color=#6FAE9D>◇ 当世气象 ◇</color></size></b>");
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("手动重照", GUILayout.Width(145f), GUILayout.Height(34f)))
			{
				XjCodexSnapshotPublisher.RefreshNow();
				ReleaseCompanionRecordCaches();
				snapshot = XjCodexSnapshotPublisher.Read();
				GUI.changed = true;
			}
			GUILayout.EndHorizontal();
			GUILayout.Label("<size=17>" + Rich(Empty(snapshot.CultivationClimate?.Summary, "天下局势尚待玄鉴照录。")) + "</size>");
			GUILayout.Label("<color=grey>国家 " + snapshot.KingdomCount + "　·　城镇 " + snapshot.CityCount
				+ "　·　运转大阵 " + snapshot.OperationalFormationCount + "　·　四艺人才 " + snapshot.CraftActorCount
				+ "　·　阴司活跃追索 " + snapshot.ActiveYinSiMissionCount + "</color>");
			DrawOrnamentDivider("#446E64", string.Empty);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("进入修道大势", GUILayout.Width(150f), GUILayout.Height(36f))) SelectCodexTab(14);
			if (GUILayout.Button("展开天下舆图", GUILayout.Width(150f), GUILayout.Height(36f))) SelectCodexTab(15);
			if (GUILayout.Button("查看仙国法统", GUILayout.Width(150f), GUILayout.Height(36f)))
			{
				_atlasView = "仙国法统";
				SelectCodexTab(15);
			}
			if (GUILayout.Button("查看世家诸脉", GUILayout.Width(150f), GUILayout.Height(36f))) SelectCodexTab(3);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			DrawWorldOverviewMetricGrid(snapshot);

			DrawSectionTitle("待观异动", "#FF8877");
			if (snapshot.Conflicts.Count == 0) DrawEmptyCard("当前未见大阵破损、治理悬空、阴司追索或宗主缺位。", "#777777");
			for (int i = 0; i < snapshot.Conflicts.Count && i < 5; i++) DrawConflictCard(snapshot.Conflicts[i]);
			if (snapshot.Conflicts.Count > 5)
			{
				GUILayout.BeginHorizontal();
				GUILayout.Label("<color=grey>当前另有 " + (snapshot.Conflicts.Count - 5) + " 条异动未在总览展开。</color>");
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("进入天下风险", GUILayout.Width(145f), GUILayout.Height(34f))) SelectCodexTab(10);
				GUILayout.EndHorizontal();
			}

			DrawSectionTitle("近世大事", "#9CD7FF");
			if (snapshot.History.Count == 0) DrawEmptyCard("世界历史尚无可归档事件。", "#777777");
			for (int i = 0; i < snapshot.History.Count && i < 6; i++) DrawHistoryCard(snapshot.History[i]);
		}

	private void DrawWorldOverviewMetricGrid(XjCodexSnapshot snapshot)
	{
		int columns = ContentWidth < 900f ? 2 : ContentWidth < 1260f ? 3 : 4;
		float width = Mathf.Max(128f, (ContentWidth - 100f) / columns);
		int critical = 0;
		for (int i = 0; i < snapshot.Conflicts.Count; i++)
		{
			if (string.Equals(snapshot.Conflicts[i]?.Severity, "危急", StringComparison.Ordinal)) critical++;
		}
		int visibleRealms = snapshot.FudiCount + snapshot.DongtianCount + snapshot.OpenAdventureRealmCount;
		for (int i = 0; i < 8; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			switch (i)
			{
				case 0:
					DrawOverviewPill("天下修士", snapshot.CultivatorCount.ToString(), "#9CD7FF", GUILayout.Width(width), GUILayout.Height(82f));
					break;
				case 1:
					DrawOverviewPill("宗门", snapshot.SectCount.ToString(), "#FFD37A", GUILayout.Width(width), GUILayout.Height(82f));
					break;
				case 2:
					DrawOverviewPill("入谱世家", snapshot.FamilyCount.ToString(), "#A7E08A", GUILayout.Width(width), GUILayout.Height(82f));
					break;
				case 3:
					DrawOverviewPill("存世真君", snapshot.JinDanCount.ToString(), "#D2B5FF", GUILayout.Width(width), GUILayout.Height(82f));
					break;
				case 4:
					DrawOverviewPill("释修", Math.Max(0, snapshot.CultivationClimate?.ShiPath ?? 0).ToString(), "#D9B86C", GUILayout.Width(width), GUILayout.Height(82f));
					break;
				case 5:
					DrawOverviewPill("显世洞天", visibleRealms.ToString(), "#B7A7FF", GUILayout.Width(width), GUILayout.Height(82f));
					break;
				case 6:
					DrawOverviewPill("仙国法统", XjXianGuoSystem.ActiveDynastyCount.ToString(), "#F0CC75", GUILayout.Width(width), GUILayout.Height(82f));
					break;
				case 7:
					DrawOverviewPill("危急示警", critical.ToString(), critical > 0 ? "#FF6666" : "#888888", GUILayout.Width(width), GUILayout.Height(82f));
					break;
			}
			if (i % columns == columns - 1 || i == 7)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

private void DrawSectPage(XjCodexSnapshot snapshot)
		{
			if (_sectEnrollActorId > 0L)
			{
				DrawSectEnrollmentWizard(snapshot);
				return;
			}
			if (_sectPeakDetailSectId > 0L)
			{
				_selectedSectId = _sectPeakDetailSectId;
				_sectPeakDetailSectId = 0L;
				_sectArchiveView = "山峰门人";
			}
			if (_sectResourceDetailSectId > 0L)
			{
				_selectedSectId = _sectResourceDetailSectId;
				if (!string.IsNullOrWhiteSpace(_sectResourceDetailView)) _sectResourceView = _sectResourceDetailView;
				_sectResourceDetailSectId = 0L;
				_sectResourceDetailView = string.Empty;
				_sectArchiveView = "底蕴传承";
			}
			if (_formationSectFilter > 0L || _secretRealmSectFilter > 0L)
			{
				_selectedSectId = _formationSectFilter > 0L ? _formationSectFilter : _secretRealmSectFilter;
				_formationSectFilter = 0L;
				_secretRealmSectFilter = 0L;
				_sectArchiveView = "大阵洞天";
			}
			DrawPageHeader("宗门谱系", "先从名录择宗，再在同一宗门档案内查看山门、峰脉、底蕴、大阵洞天与关系纪事。");
			if (snapshot.Sects.Count == 0)
			{
				DrawEmptyCard("尚无宗门政体。", "#777777");
				return;
			}

			XjCodexSectItem selected = ResolveSelectedSect(snapshot.Sects);
			float panelHeight = ResolveArchivePanelHeight();
			if (IsArchiveStackedLayout())
			{
				DrawSectSelector(snapshot.Sects, selected, Mathf.Min(330f, panelHeight));
				GUILayout.Space(8f);
				if (selected != null) DrawSectArchivePane(snapshot, selected);
			}
			else
			{
				GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
				DrawSectSelector(snapshot.Sects, selected, panelHeight);
				GUILayout.Space(8f);
				GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
				if (selected != null) DrawSectArchivePane(snapshot, selected);
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();
			}
		}

private XjCodexSectItem ResolveSelectedSect(IReadOnlyList<XjCodexSectItem> items)
		{
			XjCodexSectItem first = null;
			for (int i = 0; i < items.Count; i++)
			{
				XjCodexSectItem item = items[i];
				if (item == null) continue;
				first ??= item;
				if (item.SectId == _selectedSectId) return item;
			}
			_selectedSectId = first?.SectId ?? 0L;
			return first;
		}

private static float ResolveArchiveSelectorButtonHeight(string label, float buttonWidth)
		{
			GUIStyle style = _archiveSelectorButton ?? _largeButton ?? GUI.skin.button;
			float measured = style.CalcHeight(new GUIContent(label ?? string.Empty), Mathf.Max(120f, buttonWidth));
			return Mathf.Ceil(Mathf.Max(1f, measured)) + 4f;
		}

private static float ResolveArchiveSelectorViewportHeight(float maximumOuterHeight, float headerHeight, float contentHeight)
		{
			float maximumViewport = Mathf.Max(56f, maximumOuterHeight - headerHeight);
			return Mathf.Clamp(contentHeight + 4f, 56f, maximumViewport);
		}

private void DrawSectSelector(IReadOnlyList<XjCodexSectItem> items, XjCodexSectItem selected, float maximumHeight)
		{
			float width = IsArchiveStackedLayout() ? Mathf.Max(260f, ContentWidth - 70f) : 320f;
			float buttonWidth = Mathf.Max(220f, width - 42f);
			int sourceCount = items?.Count ?? 0;
			int limit = Mathf.Min(sourceCount, MaxRenderedCardsPerPage);
			List<XjCodexSectItem> visibleItems = new List<XjCodexSectItem>(limit);
			List<string> labels = new List<string>(limit);
			List<float> buttonHeights = new List<float>(limit);
			float contentHeight = 0f;
			for (int i = 0; i < limit; i++)
			{
				XjCodexSectItem item = items[i];
				if (item == null) continue;
				bool isSelected = selected != null && item.SectId == selected.SectId;
				string treasure = item.OwnTreasureCount > 0
					? "器库 " + item.OwnTreasureCount + " · " + Truncate(item.OwnTreasureSummary, 12)
					: "器库尚空";
				string tradition = string.IsNullOrWhiteSpace(item.PrimaryDaoTu)
					? "主传承未成"
					: "主传承 " + item.PrimaryDaoTu + " · " + DescribeLoreStrength(item.DaoTuInheritancePercent);
				string label = (isSelected ? "◆ " : "◇ ") + item.Name
					+ "\n" + XjChronology.FormatYear(item.FoundingYear) + " · 城" + item.CityCount + " · 峰" + item.PeakCount + " · 修士" + item.CultivatorCount
					+ "\n" + tradition + " · " + treasure;
				float buttonHeight = ResolveArchiveSelectorButtonHeight(label, buttonWidth);
				visibleItems.Add(item);
				labels.Add(label);
				buttonHeights.Add(buttonHeight);
				contentHeight += buttonHeight + 6f;
			}
			if (sourceCount > visibleItems.Count) contentHeight += 42f;

			const float headerHeight = 106f;
			float viewportHeight = ResolveArchiveSelectorViewportHeight(maximumHeight, headerHeight, contentHeight);
			float resolvedHeight = headerHeight + viewportHeight;
			// 与世家名录相同：随宗门卡片数量收缩，达到安全上限后仅列表内部滚动。
			// 禁止在双栏布局里被右侧详情强行拉高，否则会制造大片空白并遮住后续内容。
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.Height(resolvedHeight), GUILayout.ExpandHeight(false));
			DrawCardStripe("#FFD37A");
			DrawOrnamentDivider("#FFD37A", "宗 门 名 录");
			GUILayout.Label("<color=grey>新宗在上，旧宗在下；山门自有资产与门下世家分开记载。</color>");
			GUILayout.Space(4f);
			_sectListScrollPosition = GUILayout.BeginScrollView(
				_sectListScrollPosition, false, true,
				GUILayout.Width(Mathf.Max(240f, width - 20f)),
				GUILayout.Height(viewportHeight));
			for (int i = 0; i < visibleItems.Count; i++)
			{
				XjCodexSectItem item = visibleItems[i];
				bool isSelected = selected != null && item.SectId == selected.SectId;
				Color old = GUI.backgroundColor;
				GUI.backgroundColor = isSelected ? new Color(0.43f, 0.35f, 0.18f) : new Color(0.22f, 0.22f, 0.20f);
				GUILayout.Space(2f);
				if (GUILayout.Button(labels[i], _archiveSelectorButton, GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeights[i])))
				{
					_selectedSectId = item.SectId;
					_sectDetailScrollPosition = Vector2.zero;
					_scrollPosition = Vector2.zero;
				}
				GUILayout.Space(2f);
				GUI.backgroundColor = old;
			}
			DrawListLimitNotice(sourceCount, visibleItems.Count);
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
		}

private void DrawSectCard(XjCodexSectItem item)
		{
			string accent = item.JinDanCount > 0 ? "#FFD37A" : item.ZiFuCount > 0 ? "#B7A7FF" : "#9CD7FF";
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(accent);
			GUILayout.BeginHorizontal();
			GUILayout.BeginVertical();
			GUILayout.Label("<b><size=23>" + Rich(item.Name) + "</size></b>");
			GUILayout.Label("<color=grey>" + XjChronology.FormatYear(item.FoundingYear) + "开宗</color>");
			GUILayout.EndVertical();
			GUILayout.FlexibleSpace();
			DrawTag(TranslateSectStatus(item.Status, item.JinDanCount), accent);
			GUILayout.EndHorizontal();

			DrawOrnamentDivider(accent, "山 门 根 基");
			GUILayout.BeginHorizontal();
			DrawMiniStat("宗主", Empty(item.SovereignName, "待继任"), string.IsNullOrWhiteSpace(item.SovereignName) ? "#FF8877" : "#FFD37A", GUILayout.Width(205f));
			DrawMiniStat("开宗者", Empty(item.FounderName, "未知"), "#CFC7B2", GUILayout.Width(205f));
			DrawMiniStat("山门", Empty(item.CapitalCityName, "未定"), "#9CD7FF", GUILayout.Width(170f));
			DrawMiniStat("城镇 / 家族", item.CityCount + " / " + item.FamilyCount, "#A7E08A", GUILayout.Width(145f));
			DrawMiniStat("山峰", Math.Max(0, item.PeakCount).ToString(), "#9CD7FF", GUILayout.Width(120f));
			DrawMiniStat("修士", item.CultivatorCount.ToString(), "#CFC7B2", GUILayout.Width(115f));
			DrawMiniStat("兴衰", item.ProsperityValue + " · " + Empty(item.ProsperityTier, "未评"), ResolveProsperityColor(item.ProsperityValue), GUILayout.Width(150f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			DrawMiniStat("高境坐镇", "真人" + item.ZiFuCount + " · 真君" + item.JinDanCount, accent, GUILayout.Width(190f));
			DrawMiniStat("掌宗世家", Empty(item.GoverningFamilyName, "未定"), "#FFD37A", GUILayout.Width(190f));
			DrawMiniStat("最强世家", Empty(item.PillarFamilyName, "暂无高阶世家"), "#B7A7FF", GUILayout.Width(220f));
			DrawMiniStat("离心家族", item.DissidentFamilyCount > 0 ? item.DissidentFamilySummary : "无", item.DissidentFamilyCount > 0 ? "#FFAA66" : "#888888", GUILayout.Width(235f));
			DrawMiniStat("最近法旨", item.LastMandateYear > 0 ? XjChronology.FormatYear(item.LastMandateYear) : "未颁", "#A7E08A", GUILayout.Width(135f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			DrawMiniStat("主修道途", Empty(item.PrimaryDaoTu, "尚未形成"), string.IsNullOrWhiteSpace(item.PrimaryDaoTu) ? "#888888" : "#74E8FF", GUILayout.Width(165f));
			DrawMiniStat("传承根基", item.DaoTuHighRealmSourceCount > 0
				? item.DaoTuHighRealmSourceCount + "位同层高境 · " + DescribeLoreStrength(item.DaoTuInheritancePercent)
				: "尚无高境定脉", item.DaoTuHighRealmSourceCount > 0 ? "#A7E08A" : "#888888", GUILayout.Width(245f));
			DrawMiniStat("位序掌握", Empty(item.DaoTuControlSummary, "尚未持位"), item.DaoTuMonopoly ? "#FFD37A" : item.DaoTuHoldsFruit ? "#B7A7FF" : "#9CD7FF", GUILayout.Width(365f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			DrawOrnamentDivider("#6FAE9D", "传 承 与 山 峰");
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.Label("<b>宗门大阵</b>　" + Rich(Empty(item.FormationName, "尚未立阵"))
				+ "　<color=grey>品阶 " + item.FormationGrade + " · 耐久当前" + item.FormationCurrentDurability + " · 上限" + item.FormationMaxDurability
				+ " · 兴衰势能×" + item.FormationProsperityMultiplier.ToString("0.00")
				+ " · " + Rich(TranslateFormationState(item.FormationState)) + " · 构造者 " + Rich(Empty(item.FormationLeadName, "未载")) + "</color>");
			GUILayout.Label("<b>宗门兴衰</b>　当前" + item.ProsperityValue + " · 上限100 · " + Rich(Empty(item.ProsperityTier, "未评"))
				+ "　<color=grey>" + Rich(ClampInlineText(Empty(item.ProsperitySummary, "等待五年结算"), 120)) + "</color>");
			GUILayout.Label("<b>秘境</b>　" + Rich(ClampInlineText(Empty(item.SecretRealmState, "秘境未载"), 88)));
			GUILayout.Label("<b>宗门器库</b>　" + Rich(ClampInlineText(item.OwnTreasureCount > 0
				? item.OwnTreasureSummary + "，共" + item.OwnTreasureCount + "件"
				: "暂无宗门自有重宝", 72)) + "　　<b>门下总资源</b>　" + Rich(ClampInlineText(Empty(item.ResourceSummary, "暂无入宗资源"), 72)));
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>诸峰</b>　" + Rich(ClampInlineText(Empty(item.PeakSummary, "尚未开峰"), 120)));
			GUILayout.FlexibleSpace();
			DrawSectPeakDetailButton(item, GUILayout.Width(130f), GUILayout.Height(28f));
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=grey>宗门最强三家：" + Rich(ClampInlineText(Empty(item.StrongFamilySummary, "暂无筑基后期以上世家"), 92)) + "</color>");
			GUILayout.EndVertical();

			DrawSectRelations(item);
			DrawOrnamentDivider(accent, "山 门 操 作");
			GUILayout.BeginHorizontal();
			DrawSectRenameButton(item, GUILayout.Width(82f), GUILayout.Height(40f));
			DrawActorFocusButton("定位宗主", item.SovereignActorId, GUILayout.Width(105f), GUILayout.Height(40f));
			DrawActorFocusButton("定位开宗者", item.FounderActorId, GUILayout.Width(118f), GUILayout.Height(40f));
			if (GUILayout.Button("关系纪事", GUILayout.Width(115f), GUILayout.Height(40f)))
			{
				SelectSectArchiveView("关系纪事");
			}
			if (GUILayout.Button("底蕴传承", GUILayout.Width(115f), GUILayout.Height(40f)))
			{
				_sectResourceView = "功法";
				SelectSectArchiveView("底蕴传承");
			}
			if (GUILayout.Button("大阵洞天", GUILayout.Width(115f), GUILayout.Height(40f)))
			{
				SelectSectArchiveView("大阵洞天");
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			DrawSectRenameEditor(item);
			if (item.LastTaskYear > 0 || !string.IsNullOrWhiteSpace(item.LastTaskSummary))
			{
				GUILayout.Label("<color=grey>上次宗门共务：" + Rich(item.LastTaskYear > 0 ? XjChronology.FormatYear(item.LastTaskYear) : "未载")
					+ "　" + Rich(Empty(item.LastTaskSummary, "暂无记录")) + "</color>");
			}
			GUILayout.EndVertical();
			GUILayout.Space(6f);
		}


	private void DrawSectRelations(XjCodexSectItem item)
	{
		DrawOrnamentDivider("#7AA7A0", "宗 门 关 系");
		if (item?.Relations == null || item.Relations.Count == 0)
		{
			GUILayout.Label("<color=grey>附近尚无其他可确认宗门。</color>");
			return;
		}
		int shown = Math.Min(12, item.Relations.Count);
		for (int i = 0; i < shown;)
		{
			GUILayout.BeginHorizontal();
			int row = 0;
			while (i < shown && row < 4)
			{
				XjCodexSectRelationItem relation = item.Relations[i++];
				if (relation == null) continue;
				string relationColor = ResolveSectRelationColor(relation.Relation);
				GUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(145f), GUILayout.ExpandWidth(true));
				DrawCardStripe(relationColor);
				GUILayout.Label("<b><color=" + relationColor + ">" + Rich(Empty(relation.OtherSectName, "未名宗门")) + "</color></b>");
				GUILayout.Label("<color=grey>" + Rich(Empty(relation.Relation, "中立")) + "</color>");
				GUILayout.EndVertical();
				row++;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
		if (item.Relations.Count > shown) GUILayout.Label("<color=grey>另有 " + (item.Relations.Count - shown) + " 宗保持中立或往来未深。</color>");
	}

	private static string ResolveSectRelationColor(string relation)
	{
		if (string.Equals(relation, "亲密", StringComparison.Ordinal)) return "#D6A7E0";
		if (string.Equals(relation, "友好", StringComparison.Ordinal)) return "#A7E08A";
		if (string.Equals(relation, "较差", StringComparison.Ordinal)) return "#FFAA66";
		if (string.Equals(relation, "敌对", StringComparison.Ordinal)) return "#FF6B6B";
		return "#AAAAAA";
	}

private void DrawFamilyPage(XjCodexSnapshot snapshot)
		{
			if (_familyCultivatorDetailFamilyId > 0L)
			{
				_selectedFamilyId = _familyCultivatorDetailFamilyId;
				_familyCultivatorDetailFamilyId = 0L;
				_familyArchiveView = "在世族人";
			}
			if (_familyWarehouseDetailFamilyId > 0L)
			{
				_selectedFamilyId = _familyWarehouseDetailFamilyId;
				_familyWarehouseDetailFamilyId = 0L;
				_familyArchiveView = "家族传承";
			}
			if (_familyChronicleDetailFamilyId > 0L)
			{
				_selectedFamilyId = _familyChronicleDetailFamilyId;
				_familyChronicleDetailFamilyId = 0L;
				_familyArchiveView = "家族纪事";
			}
			DrawPageHeader("家族诸脉", "只收录现有修士或曾出修士而仍有后人的家族；名录、族档、传承、封邑与纪事始终围绕同一家族展开。");
			DrawFamilyRealmFilter();
			if (snapshot.Families.Count == 0)
			{
				DrawEmptyCard("尚无确认家族。", "#777777");
				return;
			}

			XjCodexFamilyItem selected = ResolveSelectedFamily(snapshot.Families, out int matched);
			if (matched == 0)
			{
				DrawEmptyCard(string.IsNullOrWhiteSpace(_familySearchText)
					? "当前境界筛选下暂无家族。"
					: "当前搜索与境界筛选下暂无家族。", "#777777");
				return;
			}

			float panelHeight = ResolveArchivePanelHeight();
			if (IsArchiveStackedLayout())
			{
				DrawFamilySelector(snapshot.Families, selected, matched, Mathf.Min(350f, panelHeight));
				GUILayout.Space(8f);
				if (selected != null) DrawFamilyArchivePane(snapshot, selected);
			}
			else
			{
				GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
				DrawFamilySelector(snapshot.Families, selected, matched, panelHeight);
				GUILayout.Space(8f);
				GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
				if (selected != null) DrawFamilyArchivePane(snapshot, selected);
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();
			}
		}

private XjCodexFamilyItem ResolveSelectedFamily(IReadOnlyList<XjCodexFamilyItem> items, out int matched)
		{
			matched = 0;
			XjCodexFamilyItem first = null;
			XjCodexFamilyItem selected = null;
			for (int i = 0; i < items.Count; i++)
			{
				XjCodexFamilyItem item = items[i];
				if (item == null || !IsVisibleFamily(item)
					|| !MatchesFamilyRealmFilter(item) || !MatchesFamilySearchFilter(item)) continue;
				matched++;
				first ??= item;
				if (item.FamilyId == _selectedFamilyId) selected = item;
			}
			selected ??= first;
			_selectedFamilyId = selected?.FamilyId ?? 0L;
			return selected;
		}

private void DrawFamilySelector(IReadOnlyList<XjCodexFamilyItem> items, XjCodexFamilyItem selected, int matched, float maximumHeight)
		{
			float width = IsArchiveStackedLayout() ? Mathf.Max(260f, ContentWidth - 70f) : 365f;
			float buttonWidth = Mathf.Max(220f, width - 42f);
			List<XjCodexFamilyItem> visibleItems = new List<XjCodexFamilyItem>(Mathf.Min(matched, MaxRenderedFamilyCardsPerPage));
			List<string> labels = new List<string>(visibleItems.Capacity);
			List<float> buttonHeights = new List<float>(visibleItems.Capacity);
			float contentHeight = 0f;
			for (int i = 0; i < items.Count && visibleItems.Count < MaxRenderedFamilyCardsPerPage; i++)
			{
				XjCodexFamilyItem item = items[i];
				if (item == null || !IsVisibleFamily(item)
					|| !MatchesFamilyRealmFilter(item) || !MatchesFamilySearchFilter(item)) continue;
				bool isSelected = selected != null && item.FamilyId == selected.FamilyId;
				string treasure = string.IsNullOrWhiteSpace(item.FamilyTreasureSummary)
					? "镇族重宝 · 无"
					: "重宝 · " + Truncate(item.FamilyTreasureSummary, 18);
				string tradition = string.IsNullOrWhiteSpace(item.PrimaryDaoTu)
					? "主传承未成"
					: "主传承 " + item.PrimaryDaoTu + " · " + DescribeLoreStrength(item.DaoTuInheritancePercent);
				string label = (isSelected ? "◆ " : "◇ ") + ClampInlineText(item.Name, 18)
					+ "\n" + Empty(item.LineageState, "修脉未明") + " · 在世" + item.AliveCount + " · 修士" + item.CultivatorCount
					+ "\n" + tradition + " · " + treasure;
				float buttonHeight = ResolveArchiveSelectorButtonHeight(label, buttonWidth);
				visibleItems.Add(item);
				labels.Add(label);
				buttonHeights.Add(buttonHeight);
				contentHeight += buttonHeight + 6f;
			}
			if (matched > visibleItems.Count) contentHeight += 42f;

			const float headerHeight = 106f;
			float viewportHeight = ResolveArchiveSelectorViewportHeight(maximumHeight, headerHeight, contentHeight);
			float resolvedHeight = headerHeight + viewportHeight;
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.Height(resolvedHeight));
			DrawCardStripe("#A7E08A");
			DrawOrnamentDivider("#A7E08A", "世 家 名 录");
			GUILayout.Label("<color=grey>名录按内容自适应；镇族重宝直接入目。</color>");
			GUILayout.Space(4f);
			_familyListScrollPosition = GUILayout.BeginScrollView(
				_familyListScrollPosition, false, true,
				GUILayout.Width(Mathf.Max(240f, width - 20f)),
				GUILayout.Height(viewportHeight));
			for (int i = 0; i < visibleItems.Count; i++)
			{
				XjCodexFamilyItem item = visibleItems[i];
				bool isSelected = selected != null && item.FamilyId == selected.FamilyId;
				Color old = GUI.backgroundColor;
				GUI.backgroundColor = isSelected ? new Color(0.27f, 0.42f, 0.27f) : new Color(0.20f, 0.24f, 0.20f);
				GUILayout.Space(2f);
				if (GUILayout.Button(labels[i], _archiveSelectorButton, GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeights[i])))
				{
					_selectedFamilyId = item.FamilyId;
					_familyDetailScrollPosition = Vector2.zero;
					_scrollPosition = Vector2.zero;
				}
				GUILayout.Space(2f);
				GUI.backgroundColor = old;
			}
			DrawListLimitNotice(matched, visibleItems.Count);
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
		}

private void DrawCityPage(XjCodexSnapshot snapshot)
		{
			DrawPageHeader("城镇封邑", "城市望族与居民血脉分离；入宗后望族才承担地方治理和供养责任。");
			if (_cityTargetId > 0L)
			{
				GUILayout.BeginHorizontal(GUI.skin.box);
				DrawTag("示警所指城镇", "#FFAA66");
				GUILayout.Label("<color=grey>当前只展示风险对应的城镇档案。</color>");
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("显示全部城镇", GUILayout.Width(135f), GUILayout.Height(34f)))
				{
					_cityTargetId = 0L;
					_scrollPosition = Vector2.zero;
				}
				GUILayout.EndHorizontal();
			}
			else
			{
				DrawCityFilter();
			}
			if (snapshot.Cities.Count == 0) DrawEmptyCard("世界尚无可读城镇。", "#777777");
			int matched = 0;
			int shown = 0;
			for (int i = 0; i < snapshot.Cities.Count; i++)
			{
				XjCodexCityItem item = snapshot.Cities[i];
				if (!MatchesCityFilter(item))
				{
					continue;
				}
				matched++;
				if (shown >= MaxRenderedCityCardsPerPage)
				{
					continue;
				}
				GUILayout.BeginVertical(GUI.skin.box);
				DrawCardStripe(item.SectId > 0L ? "#FFD37A" : "#777777");
				GUILayout.Label("<b><size=21>" + Rich(item.Name) + "</size></b>  <color=grey>" + Rich(item.KingdomName) + "</color>");
				GUILayout.BeginHorizontal();
				DrawMiniStat("宗门", Empty(item.SectName, "普通国家"), item.SectId > 0L ? "#FFD37A" : "#888888", GUILayout.Width(190f));
				DrawMiniStat(item.SectId > 0L ? "治理家族" : "城镇望族", FormatFamilyDisplayName(item.GoverningFamilyName, item.GoverningFamilyId, "尚未确认"), item.GoverningFamilyId > 0L ? "#A7E08A" : "#FFAA66", GUILayout.Width(190f));
				DrawMiniStat("治理状态", TranslateGovernanceState(item.GovernanceState), "#CFC7B2", GUILayout.Width(145f));
				DrawMiniStat("修士", item.CultivatorCount.ToString(), "#9CD7FF", GUILayout.Width(110f));
				DrawMiniStat("大阵保护", item.FormationGrade > 0 ? TranslateFormationState(item.FormationState) : "无", item.FormationGrade > 0 ? "#A7E08A" : "#888888", GUILayout.Width(135f));
				if (item.ChallengerFamilyId > 0L) DrawMiniStat("挑战家族", FormatFamilyDisplayName(item.ChallengerFamilyName, item.ChallengerFamilyId, "未名氏"), item.ChallengeConsecutiveYears >= 7 ? "#FF8877" : "#FFAA66", GUILayout.Width(185f));
				GUILayout.BeginVertical(GUILayout.Width(118f), GUILayout.Height(82f));
				GUILayout.FlexibleSpace();
				DrawCityFocusButton("定位城镇", item.CityId, GUILayout.Width(110f), GUILayout.Height(40f));
				GUILayout.FlexibleSpace();
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();
				if (item.FormationGrade > 0)
				{
					DrawFormationSummaryRow("阵法：", item.FormationName, item.FormationGrade, item.FormationCurrentDurability, item.FormationMaxDurability, item.FormationState, item.FormationLeadName, GUILayout.ExpandWidth(true));
				}
				if (item.ChallengerFamilyId > 0L)
				{
					string incumbentName = FormatFamilyDisplayName(item.GoverningFamilyName, item.GoverningFamilyId, "现任家族");
					string challengerName = FormatFamilyDisplayName(item.ChallengerFamilyName, item.ChallengerFamilyId, "挑战家族");
					GUILayout.Label("治理权挑战：已连续 <b>" + Math.Max(0, item.ChallengeConsecutiveYears) + "年</b> · 门槛10年；"
						+ "现任控制分 <b>" + Rich(incumbentName) + " " + item.IncumbentControlScore.ToString("F0") + "</b>，"
						+ "挑战控制分 <b>" + Rich(challengerName) + " " + item.ChallengerControlScore.ToString("F0") + "</b>");
				}
				GUILayout.EndVertical();
				GUILayout.Space(5f);
				shown++;
			}
			if (matched == 0 && snapshot.Cities.Count > 0) DrawEmptyCard("当前筛选下暂无城镇。", "#777777");
			DrawListLimitNotice(matched, shown);
		}

private void DrawFormationPage(XjCodexSnapshot snapshot)
{
	DrawPageHeader("宗门大阵", "宗门大阵护持山门城域，破阵前难以夺其根基；统计分行展示，避免横向挤压。");
	if (_formationSectFilter > 0L)
	{
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("返回宗门格局", GUILayout.Width(125f)))
		{
			_currentTab = 1;
			_formationSectFilter = 0L;
			_scrollPosition = Vector2.zero;
			return;
		}
		GUILayout.Label("当前只看所选宗门大阵。", GUILayout.ExpandWidth(true));
		GUILayout.EndHorizontal();
	}
	int matched = 0;
	int shown = 0;
	for (int i = 0; i < snapshot.Formations.Count; i++)
	{
		XjCodexFormationItem item = snapshot.Formations[i];
		if (item == null || _formationSectFilter > 0L && item.SectId != _formationSectFilter) continue;
		matched++;
		if (shown >= MaxRenderedCardsPerPage) continue;
		float ratio = item.MaxDurability <= 0 ? 0f : Mathf.Clamp01((float)item.CurrentDurability / item.MaxDurability);
		string color = ratio <= 0f ? "#FF6666" : ratio < 0.35f ? "#FFAA66" : "#A7E08A";
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		if (item.Grade > 0)
		{
			DrawFormationIcon(ResolveFormationIconPath(item.Grade));
			string stateIconPath = ResolveFormationStateIconPath(item.BuildState, item.CurrentDurability);
			if (!string.IsNullOrWhiteSpace(stateIconPath)) DrawFormationIcon(stateIconPath);
		}
		GUILayout.Label("<b><size=22>" + Rich(Empty(item.SectName, "未知宗门")) + " · " + Rich(ResolveFormationDisplayName(item.Grade)) + "</size></b>", GUILayout.ExpandWidth(true));
		DrawActorFocusButton("定位阵师", item.LeadFormationMasterId, GUILayout.Width(95f));
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		DrawMiniStat("品阶", item.Grade <= 0 ? "未建" : "第" + item.Grade + "阶", "#FFD37A", GUILayout.Width(145f));
		DrawMiniStat("状态", TranslateFormationState(item.BuildState), color, GUILayout.Width(165f));
		DrawMiniStat("主持阵法师", Empty(item.LeadFormationMasterName, "未任命"), "#CFC7B2", GUILayout.Width(220f));
		DrawMiniStat("工程任务", item.BuildTaskId > 0L ? "部署中" : item.RepairTaskId > 0L ? "维修中" : "无", item.BuildTaskId > 0L || item.RepairTaskId > 0L ? "#FFAA66" : "#888888", GUILayout.Width(155f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		DrawMiniStat("占领速度", DescribeLoreMultiplier(item.OccupationSpeedMultiplier), "#9CD7FF", GUILayout.Width(165f));
		DrawMiniStat("完成门禁", DescribeLoreGate(item.CompletionGate), "#B7A7FF", GUILayout.Width(165f));
		DrawMiniStat("最近受损", item.LastDamageYear > 0 ? XjChronology.FormatYear(item.LastDamageYear) : "未载", "#FFAA66", GUILayout.Width(165f));
		DrawMiniStat("最近维修", item.LastRepairYear > 0 ? XjChronology.FormatYear(item.LastRepairYear) : "未载", "#A7E08A", GUILayout.Width(165f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Label("耐久  <b><color=" + color + ">当前" + item.CurrentDurability + " · 上限" + item.MaxDurability + "</color></b>");
		DrawInlineBar(ratio, color, GUILayout.ExpandWidth(true));
		GUILayout.EndVertical();
		GUILayout.Space(7f);
		shown++;
	}
	if (matched == 0) DrawEmptyCard(_formationSectFilter > 0L ? "该宗门尚无大阵记录。" : "尚无宗门大阵记录。", "#777777");
	DrawListLimitNotice(matched, shown);
}

private void DrawSecretRealmPage(XjCodexSnapshot snapshot)
		{
			DrawPageHeader("洞天福地", "宗门秘境由玄韬起势，渐成福地；坐镇真君单独突出，其余真君按名录列出。");
			if (_secretRealmSectFilter > 0L)
			{
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("返回宗门格局", GUILayout.Width(125f)))
				{
					_currentTab = 1;
					_secretRealmSectFilter = 0L;
					_scrollPosition = Vector2.zero;
					return;
				}
				GUILayout.Label("当前只看所选宗门秘境。", GUILayout.ExpandWidth(true));
				GUILayout.EndHorizontal();
			}
			GUILayout.BeginHorizontal();
			DrawOverviewPill("营造秘境", snapshot.SecretRealms.Count.ToString(), "#FFD37A", GUILayout.Width(135f));
			DrawOverviewPill("福地", snapshot.FudiCount.ToString(), "#B7A7FF", GUILayout.Width(105f));
			DrawOverviewPill("洞天", snapshot.DongtianCount.ToString(), "#FFD37A", GUILayout.Width(105f));
			DrawOverviewPill("讲道纪事", snapshot.LectureCount.ToString(), "#9CD7FF", GUILayout.Width(125f));
			DrawOverviewPill("开放奇遇", snapshot.OpenAdventureRealmCount.ToString(), "#CFC7B2", GUILayout.Width(125f));
			DrawOverviewPill("营造中", CountSecretRealmUnderConstruction(snapshot.SecretRealms).ToString(), "#FFAA66", GUILayout.Width(115f));
			DrawOverviewPill("沉寂", CountSecretRealmStage(snapshot.SecretRealms, XjSecretRealmStage.Dormant).ToString(), "#888888", GUILayout.Width(95f));
			DrawOverviewPill("入口开放", CountOpenSecretRealms(snapshot.SecretRealms).ToString(), "#A7E08A", GUILayout.Width(115f));
			DrawOverviewPill("坐镇真君", CountSittingSecretRealms(snapshot.SecretRealms).ToString(), "#FFD37A", GUILayout.Width(115f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(8f);
			if (snapshot.SecretRealms.Count == 0)
			{
				DrawEmptyCard("尚无宗门获得洞天营造之法并开始玄韬工程。奇遇洞天不会被伪装成宗门永久洞天。", "#777777");
			}
			int shown = 0;
			int matchedRealms = 0;
			for (int i = 0; i < snapshot.SecretRealms.Count; i++)
			{
				XjCodexSecretRealmItem item = snapshot.SecretRealms[i];
				if (item == null || _secretRealmSectFilter > 0L && item.SectId != _secretRealmSectFilter) continue;
				matchedRealms++;
				if (shown >= 80) continue;
				string stageColor = item.Stage == XjSecretRealmStage.Dongtian ? "#FFD37A" : item.Stage == XjSecretRealmStage.Fudi ? "#B7A7FF" : item.ActiveTaskId > 0L ? "#FFAA66" : "#CFC7B2";
				GUILayout.BeginVertical(GUI.skin.box);
				DrawCardStripe(stageColor);
				GUILayout.Label("<b><size=22>" + Rich(Empty(item.DisplayName, item.SectName + "秘境")) + "</size></b>  <color=grey>" + Rich(item.SectName) + "</color>");
				GUILayout.BeginHorizontal();
				DrawMiniStat("阶段", TranslateSecretRealmStage(item.Stage), stageColor, GUILayout.Width(165f));
				DrawMiniStat("营造传承", item.ConstructionMethodKnown ? "已掌握" : "未获得", item.ConstructionMethodKnown ? "#A7E08A" : "#888888", GUILayout.Width(145f));
				DrawMiniStat("入口", Empty(item.EntranceCityName, "未稳定"), "#9CD7FF", GUILayout.Width(175f));
				DrawMiniStat("主持阵法师", Empty(item.LeadFormationMasterName, "未任命"), "#CFC7B2", GUILayout.Width(180f));
				DrawMiniStat("工程", item.ActiveTaskId > 0L ? "至" + XjChronology.FormatYear(item.StageDueYear) : "无进行中任务", item.ActiveTaskId > 0L ? "#FFAA66" : "#888888", GUILayout.Width(180f));
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal();
				DrawMiniStat("稳定度", DescribeLoreStrength(item.Stability), item.Stability >= 70 ? "#A7E08A" : "#FFAA66", GUILayout.Width(145f));
				DrawMiniStat("玄韬完整", DescribeLoreStrength(item.XuanTaoIntegrity), item.XuanTaoIntegrity >= 70 ? "#B7A7FF" : "#FF8877", GUILayout.Width(145f));
				DrawMiniStat("承载名额", item.Capacity.ToString(), "#9CD7FF", GUILayout.Width(145f));
				DrawMiniStat("入口状态", item.EntranceOpen ? "开放" : "封闭", item.EntranceOpen ? "#A7E08A" : "#888888", GUILayout.Width(145f));
				GUILayout.EndHorizontal();
				DrawSecretRealmJinDanRoster(item);
				if (!string.IsNullOrWhiteSpace(item.ConstructionMethodSource)) GUILayout.Label("<color=grey>传承来源</color>  " + Rich(item.ConstructionMethodSource));
				GUILayout.Label(Rich(item.Summary));
				GUILayout.EndVertical();
				GUILayout.Space(7f);
				shown++;
			}
			if (matchedRealms == 0 && _secretRealmSectFilter > 0L) DrawEmptyCard("该宗门尚无洞天福地。", "#777777");
			DrawListLimitNotice(matchedRealms, shown);

			DrawSectionTitle("近世讲道", "#9CD7FF");
			if (snapshot.Lectures.Count == 0) DrawEmptyCard("尚无真人开坛或真君讲道纪事。", "#777777");
			int lectureShown = Mathf.Min(snapshot.Lectures.Count, 8);
			for (int i = 0; i < lectureShown; i++)
			{
				XjCodexLectureItem lecture = snapshot.Lectures[i];
				string color = lecture.LectureType == XjSectLectureType.ZhenJun ? "#FFD37A" : "#B7A7FF";
				GUILayout.BeginVertical(GUI.skin.box);
				DrawCardStripe(color);
				GUILayout.BeginHorizontal();
				GUILayout.Label("<b>" + Rich(TranslateLectureType(lecture.LectureType)) + " · " + Rich(lecture.LecturerName) + "</b>");
				GUILayout.Label("<color=grey>" + XjChronology.FormatYear(lecture.Year) + "</color>", GUILayout.Width(135f));
				GUILayout.EndHorizontal();
				GUILayout.Label("宗门：<color=#FFD37A>" + Rich(lecture.SectName) + "</color>    听讲：" + lecture.AttendeeCount + "人    地点：" + (lecture.HeldInsideSecretRealm ? "宗门秘境" : "外界开坛"));
				GUILayout.Label(Rich(lecture.Summary));
				GUILayout.EndVertical();
				GUILayout.Space(5f);
			}
		}

private void DrawSecretRealmJinDanRoster(XjCodexSecretRealmItem item)
{
	if (item == null) return;
	XjCodexSecretRealmJinDanItem sitting = null;
	if (item.JinDanActors != null)
	{
		for (int i = 0; i < item.JinDanActors.Count; i++)
		{
			XjCodexSecretRealmJinDanItem actor = item.JinDanActors[i];
			if (actor != null && actor.ActorId == item.SittingJinDanActorId) { sitting = actor; break; }
		}
	}
	GUILayout.BeginHorizontal();
	GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(430f), GUILayout.Height(120f));
	GUILayout.Label("<b><size=18>坐镇真君</size></b>");
	if (sitting != null || item.SittingJinDanActorId > 0L)
	{
		GUILayout.Label("<color=#FFD37A><b>" + Rich(Empty(sitting?.Name, item.SittingJinDanName)) + "</b></color>");
		GUILayout.Label("境界：" + Rich(XjDisplayNameSanitizer.GameTerm(sitting?.Realm, "金丹")) + "    道途：" + Rich(XjDisplayNameSanitizer.GameTerm(sitting?.DaoTu, "道途未载")));
		DrawActorFocusButton("打开坐镇真君", item.SittingJinDanActorId, GUILayout.Width(130f));
	}
	else GUILayout.Label("<color=#888888>暂无真君坐镇</color>");
	GUILayout.EndVertical();
	GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(120f));
	GUILayout.BeginHorizontal();
	GUILayout.Label("<b><size=18>宗门其他真君</size></b>");
	GUILayout.FlexibleSpace();
	if (GUILayout.Button("查看完整修士名录", GUILayout.Width(145f)))
	{
		_jinDanSectFilter = item.SectId;
		_currentTab = 8;
		_scrollPosition = Vector2.zero;
	}
	GUILayout.EndHorizontal();
	int listed = 0;
	if (item.JinDanActors != null)
	{
		for (int i = 0; i < item.JinDanActors.Count && listed < 6; i++)
		{
			XjCodexSecretRealmJinDanItem actor = item.JinDanActors[i];
			if (actor == null || actor.ActorId == item.SittingJinDanActorId) continue;
			GUILayout.Label("· " + Rich(actor.Name) + "  <color=grey>" + Rich(XjDisplayNameSanitizer.GameTerm(actor.Realm, "境界未载")) + " · " + Rich(XjDisplayNameSanitizer.GameTerm(actor.DaoTu, "道途未载")) + "</color>");
			listed++;
		}
	}
	if (listed == 0) GUILayout.Label("<color=#888888>暂无其他存世真君</color>");
	GUILayout.EndVertical();
	GUILayout.EndHorizontal();
}

private void DrawJinDanPage(XjCodexSnapshot snapshot)
		{
			DrawJinDanArchivePage(snapshot);
		}

private List<XjCodexJinDanItem> BuildJinDanDisplayItems(XjCodexSnapshot snapshot)
		{
			string cacheKey = _jinDanSectFilter + "|" + _jinDanActorFilter + "|"
				+ _jinDanPathFilter + "|" + _jinDanAchievementFilter + "|"
				+ _jinDanYinSiFilter + "|" + (_jinDanSearchText ?? string.Empty).Trim();
			if (snapshot != null
				&& _jinDanDisplayCacheVersion == snapshot.Version
				&& string.Equals(_jinDanDisplayCacheKey, cacheKey, StringComparison.Ordinal))
			{
				return _jinDanDisplayCache;
			}
			_jinDanDisplayCache.Clear();
			_jinDanDisplayCacheVersion = snapshot?.Version ?? -1;
			_jinDanDisplayCacheKey = cacheKey;
			if (snapshot?.JinDan == null) return _jinDanDisplayCache;
			for (int i = 0; i < snapshot.JinDan.Count; i++)
			{
				XjCodexJinDanItem item = snapshot.JinDan[i];
				if (!MatchesJinDanFilters(item)) continue;
				_jinDanDisplayCache.Add(item);
			}
			return _jinDanDisplayCache;
		}

private string ResolveJinDanSectFilterName(XjCodexSnapshot snapshot)
		{
			if (snapshot?.SecretRealms != null)
			{
				for (int i = 0; i < snapshot.SecretRealms.Count; i++)
				{
					XjCodexSecretRealmItem realm = snapshot.SecretRealms[i];
					if (realm != null && realm.SectId == _jinDanSectFilter && !string.IsNullOrWhiteSpace(realm.SectName))
					{
						return realm.SectName;
					}
				}
			}
			if (snapshot?.JinDan != null)
			{
				for (int i = 0; i < snapshot.JinDan.Count; i++)
				{
					XjCodexJinDanItem item = snapshot.JinDan[i];
					if (item != null && item.SectId == _jinDanSectFilter && !string.IsNullOrWhiteSpace(item.SectName))
					{
						return item.SectName;
					}
				}
			}
			return "宗门";
		}

private string ResolveJinDanActorFilterName(XjCodexSnapshot snapshot)
		{
			if (snapshot?.JinDan != null)
			{
				for (int i = 0; i < snapshot.JinDan.Count; i++)
				{
					XjCodexJinDanItem item = snapshot.JinDan[i];
					if (item != null && (item.ActorId == _jinDanActorFilter || item.FocusActorId == _jinDanActorFilter))
					{
						return Empty(item.Name, "未名修士");
					}
				}
			}
			return "旧录修士";
		}

private static string BuildSecretRealmJinDanSummary(XjCodexSecretRealmItem item)
		{
			if (item?.JinDanActors == null || item.JinDanActors.Count == 0)
			{
				return "<color=#888888>无</color>";
			}
			int shown = Mathf.Min(item.JinDanActors.Count, 5);
			string text = string.Empty;
			for (int i = 0; i < shown; i++)
			{
				XjCodexSecretRealmJinDanItem actor = item.JinDanActors[i];
				if (actor == null)
				{
					continue;
				}
				if (!string.IsNullOrEmpty(text))
				{
					text += "、";
				}
				text += "<color=#FFD37A>" + Rich(actor.Name) + "</color>";
				if (!string.IsNullOrWhiteSpace(actor.DaoTu))
				{
					text += "·" + Rich(actor.DaoTu);
				}
			}
			if (item.JinDanActors.Count > shown)
			{
				text += " 等" + item.JinDanActors.Count + "人";
			}
			return text;
		}

private void DrawAdventureRealmPage(XjCodexSnapshot snapshot)
		{
			DrawPageHeader("奇遇洞天", "十大道途洞天按玄鉴历配置的固定周期显化；此页集中呈现地脉、归属、开放与宗门争夺状态。");
			if (!string.IsNullOrWhiteSpace(_adventureTargetName) || _adventureTargetCityId > 0L)
			{
				GUILayout.BeginHorizontal(GUI.skin.box);
				DrawTag("示警所指洞天", "#FFAA66");
				GUILayout.Label("<color=grey>当前只展示风险对应的奇遇洞天。</color>");
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("显示全部洞天", GUILayout.Width(135f), GUILayout.Height(34f)))
				{
					_adventureTargetName = string.Empty;
					_adventureTargetCityId = 0L;
					_scrollPosition = Vector2.zero;
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.BeginHorizontal();
			DrawOverviewPill("洞天总数", snapshot.AdventureRealms.Count.ToString(), "#9CD7FF", GUILayout.Width(135f), GUILayout.Height(82f));
			DrawOverviewPill("当前开放", snapshot.OpenAdventureRealmCount.ToString(), "#B7A7FF", GUILayout.Width(135f), GUILayout.Height(82f));
			DrawOverviewPill("争夺之中", snapshot.ContestedAdventureRealmCount.ToString(), snapshot.ContestedAdventureRealmCount > 0 ? "#FF8877" : "#888888", GUILayout.Width(135f), GUILayout.Height(82f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(8f);
			if (snapshot.AdventureRealms.Count == 0)
			{
				DrawEmptyCard("当前尚无奇遇洞天显世。", "#777777");
				return;
			}
			int matched = 0;
			int shown = 0;
			for (int i = 0; i < snapshot.AdventureRealms.Count && shown < 24; i++)
			{
				XjCodexAdventureRealmItem item = snapshot.AdventureRealms[i];
				if (!MatchesAdventureTarget(item)) continue;
				matched++;
				DrawAdventureRealmTile(item);
				shown++;
			}
			if (matched == 0 && (!string.IsNullOrWhiteSpace(_adventureTargetName) || _adventureTargetCityId > 0L))
			{
				DrawEmptyCard("示警所指洞天已不在当前卷宗中。", "#777777");
			}
			DrawListLimitNotice(matched, shown);
		}

private void DrawAdventureRealmTile(XjCodexAdventureRealmItem item)
		{
			if (item == null) return;
			string color = item.IsOpen ? StateColor(item.State) : "#777777";
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.MinHeight(210f));
			DrawCardStripe(color);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=22>" + Rich(item.Name) + "</size></b>");
			GUILayout.FlexibleSpace();
			DrawTag(TranslateAdventureState(item.State), color);
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			DrawMiniStat("道途", Empty(item.DaoTuGroup, "未载"), "#9CD7FF", GUILayout.Width(132f));
			DrawMiniStat("属地城镇", Empty(item.CityName, "未知地域"), "#9CD7FF", GUILayout.Width(165f));
			DrawMiniStat("当前归属", ResolveAdventureClaimName(item), ResolveAdventureClaimColor(item), GUILayout.Width(175f));
			DrawMiniStat("发现者", Empty(item.DiscovererName, "未载"), "#CFC7B2", GUILayout.Width(138f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			DrawMiniStat("开放状态", item.IsOpen ? "开放" : "沉寂", item.IsOpen ? "#B7A7FF" : "#888888", GUILayout.Width(132f));
			DrawMiniStat("开放至", item.IsOpen && item.OpenUntilYear > 0 ? XjChronology.FormatYear(item.OpenUntilYear) : "未开启", item.IsOpen ? "#B7A7FF" : "#888888", GUILayout.Width(132f));
			DrawMiniStat("剩余额度", item.UnlimitedExplore ? "不限" : Mathf.Max(0, item.RemainingExploreCount).ToString(), "#B7A7FF", GUILayout.Width(118f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			DrawMiniStat("争夺宗门", Empty(item.ContestingSectName, "无"), string.IsNullOrWhiteSpace(item.ContestingSectName) ? "#888888" : "#FF8877", GUILayout.Width(165f));
			DrawMiniStat("结算年份", item.ContestDueYear > 0 ? XjChronology.FormatYear(item.ContestDueYear) : "无", item.ContestDueYear > 0 ? "#FFAA66" : "#888888", GUILayout.Width(132f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			if (!string.IsNullOrWhiteSpace(item.ContestSummary) || !string.IsNullOrWhiteSpace(item.ContestingSectName))
			{
				GUILayout.BeginVertical(GUI.skin.box);
				GUILayout.Label("<b><color=#FFAA66>洞天争夺</color></b>");
				if (!string.IsNullOrWhiteSpace(item.ContestSummary)) GUILayout.Label("<color=grey>" + Rich(item.ContestSummary) + "</color>");
				GUILayout.EndVertical();
			}
			else
			{
				GUILayout.Label(item.UnlimitedExplore
					? "<color=#A7E08A>水月照真常驻显门但属于道尊私域：任何紫府、真人、金丹、真君羽士都不能凭境界自行拜访。洞中约每一两百年才流出一封〖照真请凭函〗，随机认主一位紫府或金丹，获函者可开启一次外层水月游历；真正【水月传召】至少数百年一见，只发生在道统、源流或权柄的关键因缘上。两者都不会让道尊以实体出现。</color>"
					: item.IsOpen
						? "<color=#A7E08A>洞天当前可供符合条件的修士试炼；归属宗门享有优先探索权。</color>"
						: "<color=grey>地标常驻，等待下一次世运开启。</color>");
			}

			GUILayout.BeginHorizontal();
			GUILayout.Label("<color=grey>" + Rich(ResolveAdventureClaimLabel(item)) + "与试炼额度均来自真实洞天记录。</color>");
			GUILayout.FlexibleSpace();
			bool canFocus = item.AnchorCityId > 0L || (item.AnchorTileX >= 0 && item.AnchorTileY >= 0);
			bool oldEnabled = GUI.enabled;
			GUI.enabled = oldEnabled && canFocus;
			if (GUILayout.Button("定位洞天", GUILayout.Width(105f), GUILayout.Height(34f)))
			{
				_focusMessage = TryFocusMapLayerTarget(item.AnchorCityId, item.AnchorTileX, item.AnchorTileY) ? "已定位到奇遇洞天。" : "该奇遇洞天暂时无法定位。";
			}
			GUI.enabled = oldEnabled;
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			GUILayout.Space(7f);
		}

private void DrawConflictPage(XjCodexSnapshot snapshot)
		{
			DrawPageHeader("天下风险", "汇总宗门关系、门内治理、世家权势、城镇封邑、大阵、洞天与阴司隐患；这里只解释风险来源，不再把所有内容统称为宗门冲突。");
			DrawConflictFilters();
			int matched = 0;
			int shown = 0;
			for (int i = 0; i < snapshot.Conflicts.Count; i++)
			{
				XjCodexConflictItem item = snapshot.Conflicts[i];
				if (!MatchesConflictFilter(item)) continue;
				matched++;
				if (shown >= MaxRenderedCardsPerPage) continue;
				DrawConflictCard(item);
				shown++;
			}
			if (matched == 0)
			{
				DrawEmptyCard(snapshot.Conflicts.Count == 0 ? "当前没有达到展示阈值的风险。" : "当前筛选下没有风险记录。", "#777777");
				return;
			}
			DrawListLimitNotice(matched, shown);
		}

private void DrawConflictFilters()
		{
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>严重程度</b>", GUILayout.Width(95f), GUILayout.Height(38f));
			for (int i = 0; i < ConflictSeverityFilters.Length; i++)
			{
				string filter = ConflictSeverityFilters[i];
				Color old = GUI.backgroundColor;
				GUI.backgroundColor = string.Equals(_conflictSeverityFilter, filter, StringComparison.Ordinal)
					? new Color(0.45f, 0.28f, 0.22f)
					: new Color(0.24f, 0.24f, 0.24f);
				if (GUILayout.Button(filter, GUILayout.Width(82f), GUILayout.Height(38f))) _conflictSeverityFilter = filter;
				GUI.backgroundColor = old;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>风险领域</b>", GUILayout.Width(95f), GUILayout.Height(38f));
			for (int i = 0; i < ConflictKindFilters.Length; i++)
			{
				string filter = ConflictKindFilters[i];
				Color old = GUI.backgroundColor;
				GUI.backgroundColor = string.Equals(_conflictKindFilter, filter, StringComparison.Ordinal)
					? new Color(0.28f, 0.38f, 0.48f)
					: new Color(0.24f, 0.24f, 0.24f);
				if (GUILayout.Button(filter, GUILayout.Width(88f), GUILayout.Height(38f))) _conflictKindFilter = filter;
				GUI.backgroundColor = old;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			GUILayout.Space(8f);
		}

private bool MatchesConflictFilter(XjCodexConflictItem item)
		{
			if (item == null) return false;
			if (!string.IsNullOrWhiteSpace(_conflictSeverityFilter)
				&& !string.Equals(_conflictSeverityFilter, "全部", StringComparison.Ordinal)
				&& !string.Equals(Empty(item.Severity, "低"), _conflictSeverityFilter, StringComparison.Ordinal)) return false;
			return string.IsNullOrWhiteSpace(_conflictKindFilter)
				|| string.Equals(_conflictKindFilter, "全部", StringComparison.Ordinal)
				|| string.Equals(Empty(item.Kind, "其他"), _conflictKindFilter, StringComparison.Ordinal);
		}

private void DrawConflictCard(XjCodexConflictItem item)
		{
			string color = item.Severity == "危急" ? "#FF6666" : item.Severity == "高" ? "#FF8877" : item.Severity == "中" ? "#FFAA66" : "#CFC7B2";
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(color);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=21>" + Rich(item.Title) + "</size></b>");
			DrawTag(Empty(item.Kind, "其他"), "#9CD7FF");
			GUILayout.Label("<color=" + color + "><b>" + Rich(item.Severity) + "</b></color>", GUILayout.Width(90f));
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=grey>此事缘由</color>　" + Rich(item.Reason));
			GUILayout.Label("<color=grey>若放任不理</color>　" + Rich(item.NextStep));
			GUILayout.BeginHorizontal();
			GUILayout.Label("<color=grey>对应卷宗</color>　<color=#9CD7FF>" + Rich(item.Observer) + "</color>");
			GUILayout.FlexibleSpace();
			if (GUILayout.Button(ResolveConflictJumpLabel(item), GUILayout.Width(165f), GUILayout.Height(36f)))
			{
				JumpToConflictTarget(item);
			}
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			GUILayout.Space(7f);
		}

}
