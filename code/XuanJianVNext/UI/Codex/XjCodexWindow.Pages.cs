using System;
using System.Collections.Generic;
using System.Reflection;
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
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.Alchemy;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Craft;
using XuanJianVNext.UI.LingWu;

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
			DrawPageHeader("天下总览", snapshot.PublishedAt + " · 玄鉴卷次 " + snapshot.Version + " · 观天下气数、山门格局与近世风云");

			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#6FAE9D");
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=20><color=#6FAE9D>◇ 天下气象 ◇</color></size></b>");
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("重新照览天下", GUILayout.Width(165f), GUILayout.Height(34f)))
			{
				XjCodexSnapshotPublisher.RefreshNow();
				snapshot = XjCodexSnapshotPublisher.Read();
				GUI.changed = true;
			}
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=grey>玄鉴以国家、宗门、世家与修士为四纲；其余大阵、洞天、阴司与百艺皆归入天下异动。</color>");
			DrawOrnamentDivider("#446E64", string.Empty);
			GUILayout.BeginHorizontal();
			DrawOverviewPill("国家", snapshot.KingdomCount.ToString(), "#9CD7FF", GUILayout.Width(150f));
			DrawOverviewPill("宗门", snapshot.SectCount.ToString(), "#FFD37A", GUILayout.Width(150f));
			DrawOverviewPill("城镇", snapshot.CityCount.ToString(), "#CFC7B2", GUILayout.Width(150f));
			DrawOverviewPill("入谱世家", snapshot.FamilyCount.ToString(), "#A7E08A", GUILayout.Width(150f));
			DrawOverviewPill("天下修士", snapshot.CultivatorCount.ToString(), "#B7A7FF", GUILayout.Width(150f));
			DrawOverviewPill("金丹真君", snapshot.JinDanCount.ToString(), "#FFD37A", GUILayout.Width(150f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			DrawOverviewPill("四艺人才", snapshot.CraftActorCount.ToString(), "#9CD7FF", GUILayout.Width(150f));
			DrawOverviewPill("运转大阵", snapshot.OperationalFormationCount.ToString(), "#A7E08A", GUILayout.Width(150f));
			DrawOverviewPill("已破大阵", snapshot.BrokenFormationCount.ToString(), snapshot.BrokenFormationCount > 0 ? "#FF8877" : "#888888", GUILayout.Width(150f));
			DrawOverviewPill("福地 / 洞天", snapshot.FudiCount + " / " + snapshot.DongtianCount, "#B7A7FF", GUILayout.Width(150f));
			DrawOverviewPill("开放奇遇", snapshot.OpenAdventureRealmCount.ToString(), "#9CD7FF", GUILayout.Width(150f));
			DrawOverviewPill("活跃追索", snapshot.ActiveYinSiMissionCount.ToString(), snapshot.ActiveYinSiMissionCount > 0 ? "#FF6666" : "#888888", GUILayout.Width(150f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=grey>洞天争夺 " + snapshot.ContestedAdventureRealmCount + " 处　·　阴司知悉 " + snapshot.KnownByYinSiCount + " 人　·　讲道纪事 " + snapshot.LectureCount + " 条</color>");
			GUILayout.EndVertical();

			DrawSectionTitle("宗门格局", "#FFD37A");
			if (snapshot.Sects.Count == 0) DrawEmptyCard("天下尚无宗门。首位本土紫府出现后，将在独立宗门层立下山门。", "#777777");
			for (int i = 0; i < snapshot.Sects.Count && i < 6; i++) DrawSectCard(snapshot.Sects[i]);

			DrawSectionTitle("当前风险", "#FF8877");
			if (snapshot.Conflicts.Count == 0) DrawEmptyCard("当前未见大阵破损、治理悬空、阴司追索或宗主缺位。", "#777777");
			for (int i = 0; i < snapshot.Conflicts.Count && i < 5; i++) DrawConflictCard(snapshot.Conflicts[i]);

			DrawSectionTitle("近世大事", "#9CD7FF");
			if (snapshot.History.Count == 0) DrawEmptyCard("世界历史尚无可归档事件。", "#777777");
			for (int i = 0; i < snapshot.History.Count && i < 8; i++) DrawHistoryCard(snapshot.History[i]);
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
				DrawSectPeakDetailPage(snapshot);
				return;
			}
			DrawPageHeader("宗门谱系", "左侧按创宗年份择宗，新宗在上；右侧集中查看山门、世家、重宝与成员收录。");
			if (snapshot.Sects.Count == 0)
			{
				DrawEmptyCard("尚无宗门政体。", "#777777");
				return;
			}

			XjCodexSectItem selected = ResolveSelectedSect(snapshot.Sects);
			GUILayout.BeginHorizontal();
			DrawSectSelector(snapshot.Sects, selected);
			GUILayout.Space(8f);
			GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
			if (selected != null) DrawSectCard(selected);
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
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

private void DrawSectSelector(IReadOnlyList<XjCodexSectItem> items, XjCodexSectItem selected)
		{
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(320f));
			DrawCardStripe("#FFD37A");
			DrawOrnamentDivider("#FFD37A", "宗 门 名 录");
			GUILayout.Label("<color=grey>新宗在上，旧宗在下；山门自有资产与门下世家分开记载。</color>");
			GUILayout.Space(4f);
			int shown = Mathf.Min(items.Count, MaxRenderedCardsPerPage);
			for (int i = 0; i < shown; i++)
			{
				XjCodexSectItem item = items[i];
				if (item == null) continue;
				bool isSelected = selected != null && item.SectId == selected.SectId;
				string treasure = item.OwnTreasureCount > 0
					? "器库 " + item.OwnTreasureCount + " · " + Truncate(item.OwnTreasureSummary, 12)
					: "器库尚空";
				Color old = GUI.backgroundColor;
				GUI.backgroundColor = isSelected ? new Color(0.43f, 0.35f, 0.18f) : new Color(0.22f, 0.22f, 0.20f);
				string label = (isSelected ? "◆ " : "◇ ") + item.Name
					+ "\n玄鉴历" + Math.Max(0, item.FoundingYear) + "年 · 城" + item.CityCount + " · 峰" + item.PeakCount + " · 修士" + item.CultivatorCount
					+ "\n" + treasure;
				if (GUILayout.Button(label, GUILayout.Width(294f), GUILayout.Height(67f)))
				{
					_selectedSectId = item.SectId;
					_scrollPosition = Vector2.zero;
				}
				GUI.backgroundColor = old;
				GUILayout.Space(3f);
			}
			DrawListLimitNotice(items.Count, shown);
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
			GUILayout.Label("<color=grey>玄鉴历" + item.FoundingYear + "年开宗</color>");
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
			DrawMiniStat("山峰", item.PeakCount + "/" + Math.Max(0, item.PeakCapacity), "#9CD7FF", GUILayout.Width(120f));
			DrawMiniStat("修士", item.CultivatorCount.ToString(), "#CFC7B2", GUILayout.Width(115f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			DrawMiniStat("高境坐镇", "紫府" + item.ZiFuCount + " · 真君" + item.JinDanCount, accent, GUILayout.Width(190f));
			DrawMiniStat("掌宗世家", Empty(item.GoverningFamilyName, "未定"), "#FFD37A", GUILayout.Width(190f));
			DrawMiniStat("最强世家", Empty(item.PillarFamilyName, "暂无高阶世家"), "#B7A7FF", GUILayout.Width(220f));
			DrawMiniStat("离心家族", item.DissidentFamilyCount > 0 ? item.DissidentFamilySummary : "无", item.DissidentFamilyCount > 0 ? "#FFAA66" : "#888888", GUILayout.Width(235f));
			DrawMiniStat("最近法旨", item.LastMandateYear > 0 ? item.LastMandateYear + "年" : "未颁", "#A7E08A", GUILayout.Width(135f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			DrawOrnamentDivider("#6FAE9D", "传 承 与 山 峰");
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.Label("<b>宗门大阵</b>　" + Rich(Empty(item.FormationName, "尚未立阵"))
				+ "　<color=grey>品阶 " + item.FormationGrade + " · 耐久 " + item.FormationCurrentDurability + "/" + item.FormationMaxDurability
				+ " · " + Rich(TranslateFormationState(item.FormationState)) + " · 构造者 " + Rich(Empty(item.FormationLeadName, "未载")) + "</color>");
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
			if (GUILayout.Button("阅山门春秋", GUILayout.Width(125f), GUILayout.Height(40f)))
			{
				_historyBookView = "宗门纪事";
				_historySelectedSectId = item.SectId;
				_historySubjectSearch = string.Empty;
				_historySubjectListScroll = Vector2.zero;
				_currentTab = 11;
				_scrollPosition = Vector2.zero;
			}
			if (GUILayout.Button("查看宗门底蕴", GUILayout.Width(135f), GUILayout.Height(40f)))
			{
				_selectedSectId = item.SectId;
				_sectResourceDetailSectId = item.SectId;
				_sectResourceDetailView = "功法";
				_currentTab = 2;
				_scrollPosition = Vector2.zero;
			}
			if (GUILayout.Button("查看大阵", GUILayout.Width(105f), GUILayout.Height(40f)))
			{
				_selectedSectId = item.SectId;
				_formationSectFilter = item.SectId;
				_currentTab = 5;
				_scrollPosition = Vector2.zero;
			}
			if (GUILayout.Button("查看洞天福地", GUILayout.Width(140f), GUILayout.Height(40f)))
			{
				_selectedSectId = item.SectId;
				_secretRealmSectFilter = item.SectId;
				_currentTab = 6;
				_scrollPosition = Vector2.zero;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			DrawSectRenameEditor(item);
			if (item.LastTaskYear > 0 || !string.IsNullOrWhiteSpace(item.LastTaskSummary))
			{
				GUILayout.Label("<color=grey>上次宗门共务：" + Rich(item.LastTaskYear > 0 ? item.LastTaskYear + "年" : "未载")
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
				DrawTag(Empty(relation.OtherSectName, "未名宗门") + " · " + Empty(relation.Relation, "中立"), ResolveSectRelationColor(relation.Relation));
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
				DrawFamilyCultivatorDetailPage(snapshot);
				return;
			}
			if (_familyWarehouseDetailFamilyId > 0L)
			{
				DrawFamilyWarehouseDetailPage(snapshot);
				return;
			}
			if (_familyChronicleDetailFamilyId > 0L)
			{
				DrawPageHeader("家族纪事", "收录本族私史，观其迁徙、兴衰、传承与门中地位。");
				DrawFamilyChronicleDetail(snapshot);
				return;
			}
			DrawPageHeader("世家谱系", "只收录现有修士或曾出修士而仍有后人的家族；衰微余脉仍可查找并由玩家扶持复兴。");
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

			GUILayout.BeginHorizontal();
			DrawFamilySelector(snapshot.Families, selected, matched);
			GUILayout.Space(8f);
			GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
			if (selected != null) DrawFamilyCard(selected);
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
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

private void DrawFamilySelector(IReadOnlyList<XjCodexFamilyItem> items, XjCodexFamilyItem selected, int matched)
		{
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(365f), GUILayout.ExpandHeight(true));
			DrawCardStripe("#A7E08A");
			DrawOrnamentDivider("#A7E08A", "世 家 名 录");
			GUILayout.Label("<color=grey>名录独立滚动，右侧谱系档案保持固定；镇族重宝直接入目。</color>");
			_familyListScrollPosition = GUILayout.BeginScrollView(_familyListScrollPosition, GUILayout.Width(340f), GUILayout.ExpandHeight(true));
			int shown = 0;
			for (int i = 0; i < items.Count && shown < MaxRenderedFamilyCardsPerPage; i++)
			{
				XjCodexFamilyItem item = items[i];
				if (item == null || !IsVisibleFamily(item)
					|| !MatchesFamilyRealmFilter(item) || !MatchesFamilySearchFilter(item)) continue;
				bool isSelected = selected != null && item.FamilyId == selected.FamilyId;
				string treasure = string.IsNullOrWhiteSpace(item.FamilyTreasureSummary)
					? "镇族重宝 · 无"
					: "重宝 · " + Truncate(item.FamilyTreasureSummary, 18);
				Color old = GUI.backgroundColor;
				GUI.backgroundColor = isSelected ? new Color(0.27f, 0.42f, 0.27f) : new Color(0.20f, 0.24f, 0.20f);
				string label = (isSelected ? "◆ " : "◇ ") + ClampInlineText(item.Name, 18)
					+ "\n" + Empty(item.LineageState, "修脉未明") + " · 在世" + item.AliveCount + " · 修士" + item.CultivatorCount
					+ "\n" + treasure;
				if (GUILayout.Button(label, GUILayout.Width(332f), GUILayout.Height(86f)))
				{
					_selectedFamilyId = item.FamilyId;
				}
				GUI.backgroundColor = old;
				GUILayout.Space(4f);
				shown++;
			}
			DrawListLimitNotice(matched, shown);
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
		}

private void DrawFamilyCard(XjCodexFamilyItem item)
		{
			string accent = item.JinDanCount > 0 ? "#FFD37A" : item.ZiFuCount > 0 ? "#B7A7FF" : "#A7E08A";
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(accent);
			GUILayout.BeginHorizontal();
			GUILayout.BeginVertical();
			GUILayout.Label("<b><size=23>" + Rich(item.Name) + "</size></b>");
			GUILayout.Label("<color=grey>" + Rich(Empty(item.LineageState, "谱系未明")) + "　·　历代修士 " + item.HistoricalCultivatorCount + "　·　家族影响力 " + Mathf.RoundToInt(item.InfluenceScore) + "</color>");
			GUILayout.EndVertical();
			GUILayout.FlexibleSpace();
			GUILayout.Label(string.IsNullOrWhiteSpace(item.FamilyTreasureSummary)
				? "<color=#888888>◇ 未有镇族重宝</color>"
				: "<color=#FFD37A>◆ 镇族重宝：" + Rich(item.FamilyTreasureSummary) + "</color>");
			DrawFamilyTreasureButton(item, GUILayout.Width(100f), GUILayout.Height(30f));
			GUILayout.EndHorizontal();

			DrawOrnamentDivider(accent, "族 脉 根 基");
			GUILayout.BeginHorizontal();
			DrawMiniStat("在世族人", item.AliveCount.ToString(), "#CFC7B2", GUILayout.Width(125f));
			DrawMiniStat("历代入谱", item.TotalCount.ToString(), "#888888", GUILayout.Width(125f));
			DrawMiniStat("在世修士", item.CultivatorCount.ToString(), "#9CD7FF", GUILayout.Width(125f));
			DrawMiniStat("高境", "紫府" + item.ZiFuCount + " · 真君" + item.JinDanCount, accent, GUILayout.Width(180f));
			DrawMiniStat("当前最高", Empty(item.HighestRealm, "未载"), "#B7A7FF", GUILayout.Width(150f));
			DrawMiniStat("历代最高", Empty(item.HistoricalHighestRealm, "未载"), "#FFD37A", GUILayout.Width(150f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			DrawMiniStat(item.FounderTitle, Empty(item.AncestorName, "未载"), "#CFC7B2", GUILayout.Width(190f));
			DrawMiniStat("家主", Empty(item.ClanLeaderName, Empty(item.Representative, "暂无")), "#FFD37A", GUILayout.Width(190f));
			DrawMiniStat("继承人", Empty(item.HeirName, "未立"), string.Equals(item.HeirName, "未立", StringComparison.Ordinal) ? "#888888" : "#9CD7FF", GUILayout.Width(180f));
			DrawMiniStat("族中支柱", Empty(item.PillarName, "暂无"), "#B7A7FF", GUILayout.Width(190f));
			DrawMiniStat("代表人物", Empty(item.Representative, "暂无"), "#CFC7B2", GUILayout.Width(190f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			DrawOrnamentDivider("#6FAE9D", "山 门 与 治 理");
			string supportedText = string.IsNullOrWhiteSpace(item.SupportedActorName)
				? "未举"
				: item.SupportedActorName + (item.SupportedSinceYear > 0 ? "·" + item.SupportedSinceYear + "年" : string.Empty);
			string aspirationText = string.IsNullOrWhiteSpace(item.ActiveAspiration)
				? "未立"
				: item.ActiveAspiration + (item.AspirationSinceYear > 0 ? "·自" + item.AspirationSinceYear + "年" : string.Empty);
			GUILayout.BeginHorizontal();
			DrawMiniStat("所属宗门", Empty(item.SectName, "未入宗门"), string.IsNullOrWhiteSpace(item.SectName) ? "#888888" : "#FFD37A", GUILayout.Width(200f));
			DrawMiniStat("话语权", Empty(item.VoiceTier, "普通家族"), item.VoiceScore >= 35f ? "#FFD37A" : "#CFC7B2", GUILayout.Width(160f));
			DrawMiniStat(IsHighRealmFamily(item) ? "宗门取用" : "供养欠缴", FormatFamilyResourceSeat(item), IsHighRealmFamily(item) ? "#FFD37A" : item.SupplyDebt >= 60f ? "#FF8877" : "#A7E08A", GUILayout.Width(145f));
			DrawMiniStat("城市官职", Empty(item.CityOfficeSummary, "无"), "#A7E08A", GUILayout.Width(185f));
			DrawMiniStat("治理城镇", Empty(item.GoverningCities, "暂无"), "#A7E08A", GUILayout.Width(230f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			DrawMiniStat("家族志向", aspirationText, "#FFD37A", GUILayout.Width(260f));
			DrawMiniStat("族中所举", supportedText, string.IsNullOrWhiteSpace(item.SupportedActorName) ? "#888888" : "#9CD7FF", GUILayout.Width(215f));
			DrawMiniStat("扶持方向", Empty(item.SupportPurpose, "无"), "#A7E08A", GUILayout.Width(160f));
			DrawMiniStat("发源地", item.OriginCityId > 0L ? Empty(item.OriginCityName, "旧城镇") : "未载", "#9CD7FF", GUILayout.Width(210f));
			if (item.IsMigrationBranch)
			{
				DrawMiniStat("来源主家", item.SourceFamilyId > 0L ? FormatFamilyDisplayName(item.SourceFamilyName, item.SourceFamilyId, "未名氏") : "未载", "#FFAA66", GUILayout.Width(190f));
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			DrawOrnamentDivider(accent, "谱 系 操 作");
			GUILayout.BeginHorizontal();
			DrawActorFocusButton("定位代表", item.RepresentativeActorId, GUILayout.Width(90f));
			DrawActorFocusButton("定位家主", item.ClanLeaderActorId, GUILayout.Width(90f));
			DrawActorFocusButton("定位继承人", item.HeirActorId, GUILayout.Width(105f));
			DrawActorFocusButton("定位所举", item.SupportedActorId, GUILayout.Width(90f));
			DrawActorFocusButton("定位始祖", item.AncestorActorId, GUILayout.Width(90f));
			DrawFamilyCultivatorButton(item, GUILayout.Width(100f));
			DrawFamilyChronicleButton(item, GUILayout.Width(90f));
			DrawFamilyWarehouseButton(item, GUILayout.Width(100f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!string.IsNullOrWhiteSpace(item.PrivilegeSummary)) GUILayout.Label("<color=#FFD37A>宗门门风：</color>" + Rich(item.PrivilegeSummary));
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.Label("<b>宗门关系</b>　" + Rich(Empty(item.SectRelation, item.SectId > 0L ? "门中平稳" : "未入宗门")));
			GUILayout.Label("<b>族志</b>　" + Rich(Empty(item.BiographySummary, "族志未详")));
			GUILayout.Label("<color=grey>近年贡献 <color=#9CD7FF>" + item.ContributionScore.ToString("F1") + "</color>　·　百艺贡献 <color=#B7A7FF>" + item.CraftScore.ToString("F1") + "</color>　·　当前责任 " + Rich(FormatFamilyResponsibility(item)) + "</color>");
			GUILayout.EndVertical();
			GUILayout.EndVertical();
			GUILayout.Space(6f);
		}

private void DrawCityPage(XjCodexSnapshot snapshot)
		{
			DrawPageHeader("城镇封邑", "城市望族与居民血脉分离；入宗后望族才承担地方治理和供养责任。");
			DrawCityFilter();
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
					GUILayout.Label("治理权挑战：已连续 <b>" + Math.Max(0, item.ChallengeConsecutiveYears) + "/10</b> 年；"
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
		DrawMiniStat("占领速度", Mathf.RoundToInt(item.OccupationSpeedMultiplier * 100f) + "%", "#9CD7FF", GUILayout.Width(165f));
		DrawMiniStat("完成门禁", Mathf.RoundToInt(item.CompletionGate * 100f) + "%", "#B7A7FF", GUILayout.Width(165f));
		DrawMiniStat("最近受损", item.LastDamageYear > 0 ? item.LastDamageYear + "年" : "未载", "#FFAA66", GUILayout.Width(165f));
		DrawMiniStat("最近维修", item.LastRepairYear > 0 ? item.LastRepairYear + "年" : "未载", "#A7E08A", GUILayout.Width(165f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Label("耐久  <b><color=" + color + ">" + item.CurrentDurability + "/" + item.MaxDurability + "</color></b>");
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
				DrawMiniStat("工程", item.ActiveTaskId > 0L ? "至" + item.StageDueYear + "年" : "无进行中任务", item.ActiveTaskId > 0L ? "#FFAA66" : "#888888", GUILayout.Width(180f));
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal();
				DrawMiniStat("稳定度", item.Stability + "%", item.Stability >= 70 ? "#A7E08A" : "#FFAA66", GUILayout.Width(145f));
				DrawMiniStat("玄韬完整", item.XuanTaoIntegrity + "%", item.XuanTaoIntegrity >= 70 ? "#B7A7FF" : "#FF8877", GUILayout.Width(145f));
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
				GUILayout.Label("<color=grey>" + lecture.Year + "年</color>", GUILayout.Width(110f));
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
		GUILayout.Label("境界：" + Rich(Empty(sitting?.Realm, "金丹")) + "    道途：" + Rich(Empty(sitting?.DaoTu, "未载")));
		DrawActorFocusButton("打开坐镇真君", item.SittingJinDanActorId, GUILayout.Width(130f));
	}
	else GUILayout.Label("<color=#888888>暂无真君坐镇</color>");
	GUILayout.EndVertical();
	GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(120f));
	GUILayout.BeginHorizontal();
	GUILayout.Label("<b><size=18>宗门其他真君</size></b>");
	GUILayout.FlexibleSpace();
	if (GUILayout.Button("查看完整真君名录", GUILayout.Width(145f)))
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
			GUILayout.Label("· " + Rich(actor.Name) + "  <color=grey>" + Rich(actor.Realm) + " · " + Rich(actor.DaoTu) + "</color>");
			listed++;
		}
	}
	if (listed == 0) GUILayout.Label("<color=#888888>暂无其他存世真君</color>");
	GUILayout.EndVertical();
	GUILayout.EndHorizontal();
}

private void DrawJinDanPage(XjCodexSnapshot snapshot)
		{
			DrawPageHeader("真君名录", "金丹不走普通寿终；超过有效寿元后，才会逐渐进入阴司关注。");
			List<XjCodexJinDanItem> items = BuildJinDanDisplayItems(snapshot);
			GUILayout.BeginHorizontal();
			DrawOverviewPill("存世真君", snapshot.JinDanCount.ToString(), "#FFD37A", GUILayout.Width(155f), GUILayout.Height(82f));
			if (_jinDanSectFilter > 0L)
			{
				DrawOverviewPill("当前宗门", ResolveJinDanSectFilterName(snapshot), "#9CD7FF", GUILayout.Width(220f), GUILayout.Height(82f));
				DrawOverviewPill("筛选结果", items.Count.ToString(), "#B7A7FF", GUILayout.Width(135f), GUILayout.Height(82f));
				if (GUILayout.Button("显示全部", GUILayout.Width(100f)))
				{
					_jinDanSectFilter = 0L;
					_scrollPosition = Vector2.zero;
				}
			}
			DrawOverviewPill("阴司已知悉", snapshot.KnownByYinSiCount.ToString(), snapshot.KnownByYinSiCount > 0 ? "#FF8877" : "#888888", GUILayout.Width(170f), GUILayout.Height(82f));
			DrawOverviewPill("活跃追索", snapshot.ActiveYinSiMissionCount.ToString(), snapshot.ActiveYinSiMissionCount > 0 ? "#FF6666" : "#888888", GUILayout.Width(155f), GUILayout.Height(82f));
			GUILayout.EndHorizontal();
			GUILayout.Space(8f);
			if (items.Count == 0) DrawEmptyCard(_jinDanSectFilter > 0L ? "该宗门当前没有可追溯的存世金丹真君。" : "尚无存世金丹真君。", "#777777");
			int shown = Mathf.Min(items.Count, MaxRenderedCardsPerPage);
			for (int i = 0; i < shown; i++)
			{
				XjCodexJinDanItem item = items[i];
				string stateColor = item.YinSiKnown ? "#FF8877" : item.YinSiExposure >= 3f ? "#FFAA66" : "#A7E08A";
				GUILayout.BeginVertical(GUI.skin.box);
				DrawCardStripe(item.ActiveMissionId > 0L ? "#FF6666" : item.YinSiKnown ? "#FF8877" : "#FFD37A");
				GUILayout.Label("<b><size=22>" + Rich(item.Name) + "</size></b>  <color=grey>成丹于 " + item.ActivatedYear + " 年</color>");
				GUILayout.BeginHorizontal();
				DrawMiniStat("纪年寿龄", item.Age + "年", "#CFC7B2", GUILayout.Width(145f));
				DrawMiniStat("寿元状态", "无定限", "#FFD37A", GUILayout.Width(145f));
				DrawMiniStat("道途", Empty(item.DaoTu, "未定"), "#B7A7FF", GUILayout.Width(145f));
				DrawMiniStat("宗门", Empty(item.SectName, "散修"), "#9CD7FF", GUILayout.Width(180f));
				DrawMiniStat("家族", Empty(item.FamilyName, "无确认家族"), "#A7E08A", GUILayout.Width(160f));
				DrawMiniStat("阴司状态", TranslateYinSiState(item.YinSiState, item.YinSiKnown), stateColor, GUILayout.Width(170f));
				DrawMiniStat("追索次数", item.PursuitCount.ToString(), item.PursuitCount > 0 ? "#FF8877" : "#888888", GUILayout.Width(125f));
				DrawActorFocusButton("定位真君", item.ActorId, GUILayout.Width(100f));
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal();
				DrawMiniStat("当前所在", item.Sheltered ? Empty(item.SecretRealmName, "宗门秘境") : "外界", item.Sheltered ? "#B7A7FF" : "#CFC7B2", GUILayout.Width(190f));
				DrawMiniStat("追索任务", item.ActiveMissionId > 0L ? TranslateYinSiMissionStage(item.MissionStage) : "无", item.ActiveMissionId > 0L ? "#FF6666" : "#888888", GUILayout.Width(155f));
				DrawMiniStat("下次行动", item.ActiveMissionId > 0L && item.NextPursuitYear > 0 ? item.NextPursuitYear + "年" : "—", item.ActiveMissionId > 0L ? "#FFAA66" : "#888888", GUILayout.Width(145f));
				DrawMiniStat("最近知名", item.LastKnownYear > 0 ? item.LastKnownYear + "年" : "无", "#888888", GUILayout.Width(145f));
				DrawMiniStat("阴司缘由", Empty(item.YinSiReason, "暂无"), "#CFC7B2", GUILayout.Width(280f));
				GUILayout.EndHorizontal();
				GUILayout.Label("暴露度  <b><color=" + stateColor + ">" + item.YinSiExposure.ToString("F1") + "</color></b>");
				DrawInlineBar(Mathf.Clamp01(item.YinSiExposure / 5f), stateColor, GUILayout.Width(480f));
				GUILayout.EndVertical();
				GUILayout.Space(7f);
			}
			DrawListLimitNotice(items.Count, shown);
		}

private List<XjCodexJinDanItem> BuildJinDanDisplayItems(XjCodexSnapshot snapshot)
		{
			List<XjCodexJinDanItem> items = new List<XjCodexJinDanItem>();
			if (snapshot?.JinDan == null)
			{
				return items;
			}
			for (int i = 0; i < snapshot.JinDan.Count; i++)
			{
				XjCodexJinDanItem item = snapshot.JinDan[i];
				if (item == null)
				{
					continue;
				}
				if (_jinDanSectFilter > 0L && item.SectId != _jinDanSectFilter)
				{
					continue;
				}
				items.Add(item);
			}
			return items;
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
			DrawPageHeader("奇遇洞天", "九大道途洞天随世运显化；此页集中呈现地脉、归属、开放与宗门争夺状态。");
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
			int shown = Mathf.Min(snapshot.AdventureRealms.Count, 24);
			for (int i = 0; i < shown; i++)
			{
				DrawAdventureRealmTile(snapshot.AdventureRealms[i]);
			}
			DrawListLimitNotice(snapshot.AdventureRealms.Count, shown);
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
			DrawMiniStat("开放至", item.IsOpen && item.OpenUntilYear > 0 ? item.OpenUntilYear + "年" : "未开启", item.IsOpen ? "#B7A7FF" : "#888888", GUILayout.Width(132f));
			DrawMiniStat("剩余额度", Mathf.Max(0, item.RemainingExploreCount).ToString(), "#B7A7FF", GUILayout.Width(118f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			DrawMiniStat("争夺宗门", Empty(item.ContestingSectName, "无"), string.IsNullOrWhiteSpace(item.ContestingSectName) ? "#888888" : "#FF8877", GUILayout.Width(165f));
			DrawMiniStat("结算年份", item.ContestDueYear > 0 ? item.ContestDueYear + "年" : "无", item.ContestDueYear > 0 ? "#FFAA66" : "#888888", GUILayout.Width(132f));
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
				GUILayout.Label(item.IsOpen
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
			GUILayout.Label("<color=grey>风险来源</color>  " + Rich(item.Reason));
			GUILayout.Label("<color=grey>可能演变</color>  " + Rich(item.NextStep));
			GUILayout.Label("<color=grey>查看入口</color>  <color=#9CD7FF>" + Rich(item.Observer) + "</color>");
			GUILayout.EndVertical();
			GUILayout.Space(7f);
		}

private void DrawDiagnosticsPage(XjCodexSnapshot snapshot)
		{
			DrawPageHeader("照录状态", "查看玄鉴仙鉴当前收录规模与整理状态。");
			GUILayout.BeginHorizontal();
			DrawMiniStat("照录批次", snapshot.Version.ToString(), "#9CD7FF", GUILayout.Width(150f));
			DrawMiniStat("发布年份", XjCodexSnapshotPublisher.LastPublishedYear.ToString(), "#CFC7B2", GUILayout.Width(150f));
			DrawMiniStat("请求年份", XjCodexSnapshotPublisher.RequestedYear.ToString(), "#CFC7B2", GUILayout.Width(150f));
			DrawMiniStat("待整理", XjCodexSnapshotPublisher.HasPending ? "是" : "否", XjCodexSnapshotPublisher.HasPending ? "#FFAA66" : "#A7E08A", GUILayout.Width(130f));
			DrawMiniStat("整理耗时", XjCodexSnapshotPublisher.LastBuildMilliseconds.ToString("0.00") + "毫秒", "#9CD7FF", GUILayout.Width(170f));
			DrawMiniStat("变动来源", XjCodexSnapshotPublisher.DirtySummary, "#B7A7FF", GUILayout.Width(300f));
			GUILayout.EndHorizontal();
			GUILayout.Space(8f);
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.Label("<b>照录规模</b>");
			GUILayout.Label("宗门 " + snapshot.Sects.Count + " - 家族 " + snapshot.Families.Count + " - 城镇 " + snapshot.Cities.Count
				+ " - 大阵 " + snapshot.Formations.Count + " - 营造秘境 " + snapshot.SecretRealms.Count + " - 四艺 " + snapshot.CraftActors.Count
				+ " - 真君 " + snapshot.JinDan.Count + " - 活跃追索 " + snapshot.ActiveYinSiMissionCount + " - 奇遇洞天 " + snapshot.AdventureRealms.Count
				+ " - 讲道 " + snapshot.Lectures.Count + " - 冲突 " + snapshot.Conflicts.Count + " - 历史 " + snapshot.History.Count);
			GUILayout.Label("<color=grey>规则：百科只展示既有信息，不会在浏览时推进创宗、生产、布阵、归属或追索。</color>");
			if (GUILayout.Button("请求重新发布快照", GUILayout.Width(220f), GUILayout.Height(38f)))
			{
				XjCodexSnapshotPublisher.RefreshNow();
				snapshot = XjCodexSnapshotPublisher.Read();
				GUI.changed = true;
			}
			GUILayout.EndVertical();
		}
}



