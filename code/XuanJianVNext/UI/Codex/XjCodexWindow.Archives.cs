using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Codex;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	// IMGUI may invoke the same draw code multiple times per rendered frame. Reuse a
	// tiny scratch triplet instead of allocating List/array objects on every Layout/Repaint.
	private static readonly string[] WarehouseOverviewLabelsScratch = new string[8];
	private static readonly string[] WarehouseOverviewValuesScratch = new string[8];
	private static readonly string[] WarehouseOverviewColorsScratch = new string[8];

	private static void SetWarehouseOverviewScratch(int index, string label, string value, string color)
	{
		if ((uint)index >= (uint)WarehouseOverviewLabelsScratch.Length) return;
		WarehouseOverviewLabelsScratch[index] = label ?? string.Empty;
		WarehouseOverviewValuesScratch[index] = value ?? string.Empty;
		WarehouseOverviewColorsScratch[index] = color ?? "#CFC7B2";
	}
	private bool IsArchiveStackedLayout()
	{
		// 玄鉴仙鉴现有固定侧栏会占去约 215 像素；按正文可用宽度判断双栏。
		return ContentWidth < 1160f;
	}

	private float ResolveArchivePanelHeight()
	{
		return Mathf.Clamp(_windowRect.height - 340f, 360f, 980f);
	}

	private float ResolveArchiveDetailWidth(bool family)
	{
		if (IsArchiveStackedLayout())
		{
			return Mathf.Max(260f, ContentWidth - 36f);
		}

		float selectorWidth = family ? 365f : 320f;
		return Mathf.Max(260f, ContentWidth - selectorWidth - 30f);
	}

	/// <summary>
	/// 仓库详情比普通档案页多一层 box / scroll view。旧版直接拿
	/// ResolveArchiveDetailWidth 计算列数，把滚动条、边框与 GUILayout 间距
	/// 当成可用正文，导致最右一列压到红色滚动条之外。这里统一保留一段
	/// 安全边距；所有家族/宗门仓库卡片和分类按钮都从同一宽度派生。
	/// </summary>
	private float ResolveWarehouseSafeWidth(bool family)
	{
		float reserve = IsArchiveStackedLayout() ? 76f : 68f;
		return Mathf.Max(220f, ResolveArchiveDetailWidth(family) - reserve);
	}

	private int ResolveWarehouseColumns(bool family, float stride, int maxColumns)
	{
		float safeStride = Mathf.Max(1f, stride);
		int columns = Mathf.FloorToInt((ResolveWarehouseSafeWidth(family) + 6f) / safeStride);
		return Mathf.Clamp(columns, 1, Mathf.Max(1, maxColumns));
	}

	private void DrawWarehouseOverviewPills(
		bool family,
		string[] labels,
		string[] values,
		string[] colors,
		int requestedCount = int.MaxValue)
	{
		if (labels == null || values == null || colors == null) return;
		int count = Mathf.Min(Mathf.Max(0, requestedCount), Mathf.Min(labels.Length, Mathf.Min(values.Length, colors.Length)));
		if (count <= 0) return;

		float available = ResolveWarehouseSafeWidth(family);
		int columns = Mathf.Clamp(Mathf.FloorToInt((available + 6f) / 156f), 1, 4);
		float width = Mathf.Max(112f, (available - Math.Max(0, columns - 1) * 6f) / columns);
		for (int i = 0; i < count; i += columns)
		{
			GUILayout.BeginHorizontal();
			for (int column = 0; column < columns && i + column < count; column++)
			{
				int index = i + column;
				DrawOverviewPill(labels[index], values[index], colors[index], GUILayout.Width(width), GUILayout.Height(82f));
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
	}

	private void DrawArchiveBreadcrumb(string volume, string catalog, string subject, string view, string accent)
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(accent);
		GUILayout.Label("<color=grey>" + Rich(volume) + "　／　" + Rich(catalog) + "　／　</color>"
			+ "<color=" + accent + "><b>" + Rich(subject) + "</b></color>"
			+ "<color=grey>　／　" + Rich(view) + "</color>");
		GUILayout.EndVertical();
	}

	private void DrawArchiveViewTabs(string[] views, string selected, string accent, Action<string> select)
	{
		if (views == null || select == null) return;
		int columns = IsArchiveStackedLayout() ? 3 : views.Length;
		for (int i = 0; i < views.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			string view = views[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(selected, view, StringComparison.Ordinal)
				? ParseHexColor(accent, new Color(0.34f, 0.38f, 0.42f))
				: new Color(0.23f, 0.23f, 0.23f);
			if (GUILayout.Button(view, GUILayout.Height(38f)))
			{
				select(view);
			}
			GUI.backgroundColor = old;
			if (i % columns == columns - 1 || i == views.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private void SelectSectArchiveView(string view)
	{
		_sectArchiveView = string.IsNullOrWhiteSpace(view) ? "山门总览" : view;
		_sectDetailScrollPosition = Vector2.zero;
		_sectPeakDetailSectId = 0L;
		_sectResourceDetailSectId = 0L;
		_formationSectFilter = 0L;
		_secretRealmSectFilter = 0L;
	}

	private void SelectFamilyArchiveView(string view)
	{
		_familyArchiveView = string.IsNullOrWhiteSpace(view) ? "家族总览" : view;
		_familyDetailScrollPosition = Vector2.zero;
		_familyCultivatorDetailFamilyId = 0L;
		_familyWarehouseDetailFamilyId = 0L;
		_familyChronicleDetailFamilyId = 0L;
	}

	private void OpenSectArchive(long sectId, string view)
	{
		if (sectId > 0L) _selectedSectId = sectId;
		SelectSectArchiveView(view);
		_currentTab = 1;
		_scrollPosition = Vector2.zero;
	}

	private void OpenFamilyArchive(long familyId, string view)
	{
		if (familyId > 0L) _selectedFamilyId = familyId;
		SelectFamilyArchiveView(view);
		_currentTab = 3;
		_scrollPosition = Vector2.zero;
	}

	private void DrawSectArchivePane(XjCodexSnapshot snapshot, XjCodexSectItem sect)
	{
		if (sect == null) return;
		DrawArchiveBreadcrumb("山河诸势", "宗门谱系", sect.Name, _sectArchiveView, "#D6BE86");
		DrawArchiveViewTabs(SectArchiveViews, _sectArchiveView, "#D6BE86", SelectSectArchiveView);
		GUILayout.Space(6f);

		if (string.Equals(_sectArchiveView, "山峰门人", StringComparison.Ordinal))
		{
			DrawSectPeaksArchive(sect);
			return;
		}
		if (string.Equals(_sectArchiveView, "底蕴传承", StringComparison.Ordinal))
		{
			DrawSectResourcesArchive(snapshot, sect);
			return;
		}
		if (string.Equals(_sectArchiveView, "大阵洞天", StringComparison.Ordinal))
		{
			DrawSectFormationAndRealmArchive(snapshot, sect);
			return;
		}
		if (string.Equals(_sectArchiveView, "关系纪事", StringComparison.Ordinal))
		{
			DrawSectRelations(sect);
			GUILayout.Space(6f);
			DrawSubjectNarrativeTimeline(snapshot.SectChronicles, XjHistoryVisibility.Sect, sect.SectId, sect.Name);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("进入完整宗门纪事", GUILayout.Width(175f), GUILayout.Height(36f)))
			{
				_historyBookView = "宗门纪事";
				_historySelectedSectId = sect.SectId;
				_currentTab = 11;
				_scrollPosition = Vector2.zero;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			return;
		}
		if (ResolveArchiveDetailWidth(false) < 1080f) DrawCompactSectOverview(sect);
		else DrawSectCard(sect);
	}

	private static string ResolveProsperityColor(int value)
	{
		return value >= 85 ? "#FFD37A" : value >= 65 ? "#A7E08A" : value >= 45 ? "#9CD7FF" : value >= 30 ? "#FFAA66" : "#FF7766";
	}

	private void DrawCompactSectOverview(XjCodexSectItem sect)
	{
		string accent = sect.JinDanCount > 0 ? "#FFD37A" : sect.ZiFuCount > 0 ? "#B7A7FF" : "#9CD7FF";
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(accent);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=22>" + Rich(sect.Name) + "</size></b>");
		GUILayout.FlexibleSpace();
		DrawTag(TranslateSectStatus(sect.Status, sect.JinDanCount), accent);
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=grey>" + XjChronology.FormatYear(sect.FoundingYear) + "开宗</color>");

		GUILayout.BeginHorizontal();
		DrawMiniStat("宗主", Empty(sect.SovereignName, "待继任"), "#FFD37A", GUILayout.Width(190f));
		DrawMiniStat("开宗者", Empty(sect.FounderName, "未知"), "#CFC7B2", GUILayout.Width(190f));
		DrawMiniStat("山门", Empty(sect.CapitalCityName, "未定"), "#9CD7FF", GUILayout.Width(165f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		DrawMiniStat("领属", "城镇" + sect.CityCount + " · 家族" + sect.FamilyCount, "#A7E08A", GUILayout.Width(165f));
		DrawMiniStat("山峰", Math.Max(0, sect.PeakCount).ToString(), "#9CD7FF", GUILayout.Width(115f));
		DrawMiniStat("修士", sect.CultivatorCount.ToString(), "#CFC7B2", GUILayout.Width(105f));
		DrawMiniStat("兴衰", sect.ProsperityValue + " · " + Empty(sect.ProsperityTier, "未评"), ResolveProsperityColor(sect.ProsperityValue), GUILayout.Width(145f));
		DrawMiniStat("高境坐镇", "真人" + sect.ZiFuCount + " · 真君" + sect.JinDanCount, accent, GUILayout.Width(180f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		DrawMiniStat("掌宗世家", Empty(sect.GoverningFamilyName, "未定"), "#FFD37A", GUILayout.Width(180f));
		DrawMiniStat("宗门大阵", Empty(sect.FormationName, "尚未立阵") + " 当前" + sect.FormationCurrentDurability + " · 上限" + sect.FormationMaxDurability
			+ " · 兴衰×" + sect.FormationProsperityMultiplier.ToString("0.00"), "#B7A7FF", GUILayout.Width(285f));
		DrawMiniStat("洞天福地", ClampInlineText(Empty(sect.SecretRealmState, "尚无"), 30), "#A7E08A", GUILayout.Width(200f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		DrawSectRelations(sect);
		GUILayout.BeginHorizontal();
		DrawSectRenameButton(sect, GUILayout.Height(38f));
		if (GUILayout.Button("山峰门人", GUILayout.Height(38f))) SelectSectArchiveView("山峰门人");
		if (GUILayout.Button("底蕴传承", GUILayout.Height(38f))) SelectSectArchiveView("底蕴传承");
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("大阵洞天", GUILayout.Height(38f))) SelectSectArchiveView("大阵洞天");
		if (GUILayout.Button("关系纪事", GUILayout.Height(38f))) SelectSectArchiveView("关系纪事");
		DrawActorFocusButton("定位宗主", sect.SovereignActorId, GUILayout.Height(38f));
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

	private void DrawSectPeaksArchive(XjCodexSectItem sect)
	{
		if (sect.Peaks == null || sect.Peaks.Count == 0)
		{
			DrawEmptyCard("该宗门尚无峰脉记录。", "#777777");
			return;
		}
		if (_sectPeakSelectedPeakId == int.MinValue || FindPeakById(sect.Peaks, _sectPeakSelectedPeakId) == null)
		{
			_sectPeakSelectedPeakId = SelectDefaultPeakId(sect);
		}
		DrawSectPeakSelector(sect.Peaks);
		XjCodexSectPeakItem peak = FindPeakById(sect.Peaks, _sectPeakSelectedPeakId);
		if (peak != null) DrawSectPeakDetail(sect, peak);
	}

	private void DrawSectResourcesArchive(XjCodexSnapshot snapshot, XjCodexSectItem sect)
	{
		GetSectResourceCountMapCached(snapshot);
		string view = string.IsNullOrWhiteSpace(_sectResourceView) ? "功法" : _sectResourceView;
		GUILayout.BeginVertical(GUI.skin.box);
		DrawOrnamentDivider("#B7A7FF", "宗 门 底 蕴");
		int columns = ResolveWarehouseColumns(false, 122f, 5);
		for (int i = 0; i < SectResourceViews.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			string nextView = SectResourceViews[i];
			int count = CountSectResources(snapshot.SectResources, sect.SectId, nextView);
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(view, nextView, StringComparison.Ordinal)
				? new Color(0.34f, 0.38f, 0.46f)
				: new Color(0.23f, 0.23f, 0.23f);
			if (GUILayout.Button(nextView + " " + count, GUILayout.Height(34f)))
			{
				_sectResourceView = nextView;
				view = nextView;
				_sectDetailScrollPosition = Vector2.zero;
			}
			GUI.backgroundColor = old;
			if (i % columns == columns - 1 || i == SectResourceViews.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		GUILayout.EndVertical();
		GUILayout.Space(6f);
		DrawSectResourceDetailGrid(snapshot.SectResources, sect.SectId, view);
	}

	private void DrawSectFormationAndRealmArchive(XjCodexSnapshot snapshot, XjCodexSectItem sect)
	{
		XjCodexFormationItem formation = null;
		for (int i = 0; i < snapshot.Formations.Count; i++)
		{
			XjCodexFormationItem candidate = snapshot.Formations[i];
			if (candidate != null && candidate.SectId == sect.SectId)
			{
				formation = candidate;
				break;
			}
		}

		DrawSectionTitle("护宗大阵", "#A7E08A");
		if (formation == null)
		{
			DrawEmptyCard("该宗门尚无护宗大阵记录。", "#777777");
		}
		else
		{
			float ratio = formation.MaxDurability <= 0 ? 0f
				: Mathf.Clamp01((float)formation.CurrentDurability / formation.MaxDurability);
			string color = ratio <= 0f ? "#FF6666" : ratio < 0.35f ? "#FFAA66" : "#A7E08A";
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(color);
			GUILayout.Label("<b><size=21>" + Rich(ResolveFormationDisplayName(formation.Grade)) + "</size></b>");
			GUILayout.BeginHorizontal();
			DrawMiniStat("状态", TranslateFormationState(formation.BuildState), color, GUILayout.Width(150f));
			DrawMiniStat("主持阵法师", Empty(formation.LeadFormationMasterName, "未任命"), "#CFC7B2", GUILayout.Width(210f));
			DrawMiniStat("最近受损", formation.LastDamageYear > 0 ? XjChronology.FormatYear(formation.LastDamageYear) : "未载", "#FFAA66", GUILayout.Width(145f));
			DrawMiniStat("最近维修", formation.LastRepairYear > 0 ? XjChronology.FormatYear(formation.LastRepairYear) : "未载", "#A7E08A", GUILayout.Width(145f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Label("耐久　<b><color=" + color + ">当前" + formation.CurrentDurability + " · 上限" + formation.MaxDurability + "</color></b>");
			DrawInlineBar(ratio, color, GUILayout.ExpandWidth(true));
			GUILayout.EndVertical();
		}

		DrawSectionTitle("洞天福地", "#B7A7FF");
		int shown = 0;
		for (int i = 0; i < snapshot.SecretRealms.Count; i++)
		{
			XjCodexSecretRealmItem realm = snapshot.SecretRealms[i];
			if (realm == null || realm.SectId != sect.SectId) continue;
			string color = realm.Stage == "Dongtian" ? "#FFD37A" : realm.Stage == "Fudi" ? "#B7A7FF" : "#CFC7B2";
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(color);
			GUILayout.Label("<b><size=20>" + Rich(Empty(realm.DisplayName, sect.Name + "秘境")) + "</size></b>");
			GUILayout.BeginHorizontal();
			DrawMiniStat("阶段", TranslateSecretRealmStage(realm.Stage), color, GUILayout.Width(145f));
			DrawMiniStat("入口", Empty(realm.EntranceCityName, "未稳定"), "#9CD7FF", GUILayout.Width(170f));
			DrawMiniStat("稳定度", DescribeLoreStrength(realm.Stability), realm.Stability >= 70 ? "#A7E08A" : "#FFAA66", GUILayout.Width(125f));
			DrawMiniStat("坐镇真君", Empty(realm.SittingJinDanName, "暂无"), "#FFD37A", GUILayout.Width(180f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=grey>" + Rich(Empty(realm.Summary, "秘境档案未详")) + "</color>");
			GUILayout.EndVertical();
			shown++;
		}
		if (shown == 0) DrawEmptyCard("该宗门尚无洞天福地。", "#777777");
	}

	private void DrawFamilyArchivePane(XjCodexSnapshot snapshot, XjCodexFamilyItem family)
	{
		if (family == null) return;
		DrawArchiveBreadcrumb("山河诸势", "家族诸脉", family.Name, _familyArchiveView, "#A7E08A");
		DrawArchiveViewTabs(FamilyArchiveViews, _familyArchiveView, "#A7E08A", SelectFamilyArchiveView);
		GUILayout.Space(6f);

		if (string.Equals(_familyArchiveView, "在世族人", StringComparison.Ordinal))
		{
			DrawFamilyMembersArchive(family);
			return;
		}
		if (string.Equals(_familyArchiveView, "家族传承", StringComparison.Ordinal))
		{
			DrawFamilyInheritanceArchive(family);
			return;
		}
		if (string.Equals(_familyArchiveView, "封邑门位", StringComparison.Ordinal))
		{
			DrawFamilyDomainArchive(snapshot, family);
			return;
		}
		if (string.Equals(_familyArchiveView, "家族纪事", StringComparison.Ordinal))
		{
			DrawSubjectNarrativeTimeline(snapshot.FamilyChronicles, XjHistoryVisibility.Family, family.FamilyId, family.Name);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("进入完整世家纪事", GUILayout.Width(175f), GUILayout.Height(36f)))
			{
				_historyBookView = "世家纪事";
				_historySelectedFamilyId = family.FamilyId;
				_currentTab = 11;
				_scrollPosition = Vector2.zero;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			return;
		}
		DrawCompactFamilyOverview(family);
	}

	private void DrawCompactFamilyOverview(XjCodexFamilyItem family)
	{
		string accent = family.JinDanCount > 0 ? "#FFD37A" : family.ZiFuCount > 0 ? "#B7A7FF" : "#A7E08A";
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(accent);
		GUILayout.Label("<b><size=22>" + Rich(family.Name) + "</size></b>");
		GUILayout.Label("<color=grey>" + Rich(Empty(family.LineageState, "谱系未明"))
			+ "　·　影响力 " + Mathf.RoundToInt(family.InfluenceScore) + "</color>");

		DrawFamilyOverviewGrid(family, accent);
		DrawMiniStat("镇族重宝", Empty(family.FamilyTreasureSummary, "暂无"), "#FFD37A", GUILayout.ExpandWidth(true));

		GUILayout.BeginHorizontal();
		if (GUILayout.Button("在世族人", GUILayout.Height(38f))) SelectFamilyArchiveView("在世族人");
		if (GUILayout.Button("家族传承", GUILayout.Height(38f))) SelectFamilyArchiveView("家族传承");
		if (GUILayout.Button("封邑门位", GUILayout.Height(38f))) SelectFamilyArchiveView("封邑门位");
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("家族纪事", GUILayout.Height(38f))) SelectFamilyArchiveView("家族纪事");
		DrawActorFocusButton("定位家主", family.ClanLeaderActorId, GUILayout.Height(38f));
		DrawActorFocusButton("定位所举", family.SupportedActorId, GUILayout.Height(38f));
		if (GUILayout.Button(_renamingFamilyId == family.FamilyId ? "收起改姓" : "家族改姓", GUILayout.Height(38f)))
		{
			if (_renamingFamilyId == family.FamilyId)
			{
				_renamingFamilyId = 0L;
				_renamingFamilySurname = string.Empty;
				_familyRenameMessage = string.Empty;
			}
			else
			{
				_renamingFamilyId = family.FamilyId;
				_renamingFamilySurname = XjFamilySurnameService.ResolveCurrentSurname(family.FamilyId);
				_familyRenameMessage = string.Empty;
			}
		}
		GUILayout.EndHorizontal();
		if (_renamingFamilyId == family.FamilyId)
		{
			DrawFamilySurnameRenamePanel(family);
		}
		GUILayout.EndVertical();
	}

	private void DrawFamilyOverviewGrid(XjCodexFamilyItem family, string accent)
	{
		string[] labels =
		{
			"在世族人", "历代入谱", "在世修士", "当前最高", "释修",
			"家主", "继承人", "族中支柱",
			"所属宗门", "话语权", "治理城镇",
			"家族志向", "族中所举", "扶持方向"
		};
		string[] values =
		{
			family.AliveCount.ToString(),
			family.HistoricalCultivatorCount.ToString(),
			family.CultivatorCount.ToString(),
			Empty(family.HighestRealm, "未载"),
			family.ShiCount > 0 ? family.ShiCount + "人 · 高位" + family.ShiHighRealmCount : "无",
			Empty(family.ClanLeaderName, Empty(family.Representative, "暂无")),
			Empty(family.HeirName, "未立"),
			Empty(family.PillarName, "暂无"),
			Empty(family.SectName, "未入宗门"),
			Empty(family.VoiceTier, "普通家族"),
			Empty(family.GoverningCities, "暂无"),
			Empty(family.ActiveAspiration, "未立"),
			Empty(family.SupportedActorName, "未举"),
			Empty(family.SupportPurpose, "无")
		};
		string[] colors =
		{
			"#CFC7B2", "#888888", "#9CD7FF", accent, "#D2B5FF",
			"#FFD37A", "#A7E08A", "#B7A7FF",
			"#FFD37A", "#CFC7B2", "#A7E08A",
			"#9CD7FF", "#FFD37A", "#B7A7FF"
		};
		int columns = ResolveFamilyOverviewColumns();
		float width = ResolveFamilyOverviewCardWidth(columns);
		for (int i = 0; i < labels.Length; i += columns)
		{
			GUILayout.BeginHorizontal();
			for (int column = 0; column < columns && i + column < labels.Length; column++)
			{
				int index = i + column;
				DrawMiniStat(labels[index], ClampInlineText(values[index], 22), colors[index], GUILayout.Width(width), GUILayout.Height(74f));
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
	}

	private int ResolveFamilyOverviewColumns()
	{
		float width = ResolveArchiveDetailWidth(true);
		if (width >= 1180f) return 5;
		if (width >= 900f) return 4;
		return 3;
	}

	private float ResolveFamilyOverviewCardWidth(int columns)
	{
		float available = Mathf.Max(480f, ResolveArchiveDetailWidth(true) - 38f);
		return Mathf.Clamp((available - Math.Max(0, columns - 1) * 6f) / Math.Max(1, columns), 128f, 235f);
	}

	private void DrawFamilyMembersArchive(XjCodexFamilyItem family)
	{
		if (family.Members == null || family.Members.Count == 0)
		{
			DrawEmptyCard("该家族暂无可展示的在世修士。", "#777777");
			return;
		}
		float detailWidth = ResolveArchiveDetailWidth(true);
		int columns = ResolveFamilyCultivatorColumns(detailWidth);
		for (int i = 0; i < family.Members.Count; i += columns)
		{
			GUILayout.BeginHorizontal();
			for (int column = 0; column < columns && i + column < family.Members.Count; column++)
			{
				DrawFamilyCultivatorCard(family, family.Members[i + column]);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
		DrawListLimitNotice(family.CultivatorCount, family.Members.Count);
	}

	private void DrawFamilyInheritanceArchive(XjCodexFamilyItem family)
	{
		DrawSectionTitle("道途传承", "#F0CC75");
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(family.DaoTuMonopoly ? "#FFD36A" : "#74E8FF");
		GUILayout.BeginHorizontal();
		DrawMiniStat("主修道途", Empty(family.PrimaryDaoTu, "尚未定脉"), "#74E8FF", GUILayout.Width(180f));
		DrawMiniStat("后辈继承", family.DaoTuInheritancePercent > 0 ? DescribeLoreStrength(family.DaoTuInheritancePercent) : "未形成", "#A7E08A", GUILayout.Width(155f));
		DrawMiniStat("高境定脉", family.DaoTuHighRealmSourceCount > 0 ? family.DaoTuHighRealmSourceCount + "人" : "暂无", "#D2B5FF", GUILayout.Width(145f));
		DrawMiniStat("位序掌握", "本家" + family.DaoTuHeldPositions + " · 天下" + Math.Max(0, family.DaoTuActivePositions), family.DaoTuHoldsFruit ? "#FFD37A" : "#CFC7B2", GUILayout.Width(175f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Label("<b>" + Rich(Empty(family.DaoTuControlSummary, "本家尚未形成稳定高境传承。")) + "</b>");
		GUILayout.Label("<color=grey>传承只由家族当前最高境界层级定脉；低境旁支不会稀释真人、真君或道胎确立的主修道途。"
			+ (family.DaoTuMonopoly ? " 本家已经形成道途垄断，后辈首次定路必然沿主脉。" : string.Empty) + "</color>");
		GUILayout.EndVertical();

		if (family.ShiCount > 0)
		{
			DrawSectionTitle("释脉旁录", "#D2B5FF");
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#D2B5FF");
			GUILayout.Label("<b>本家在世释修 " + family.ShiCount + " 人"
				+ (family.ShiHighRealmCount > 0 ? "　·　释修高位 " + family.ShiHighRealmCount + " 人" : string.Empty) + "</b>");
			GUILayout.Label("<color=grey>释修保留家族血缘与谱系身份，但不参与紫金／服气主修道途的果位垄断计算；此处只作家族旁录，不展开古今释内部规则。</color>");
			GUILayout.EndVertical();
		}

		DrawFamilyHighRealmSources(family);
		DrawSectionTitle("家族资源传承", "#9FC9C0");
		string view = string.IsNullOrWhiteSpace(_familyWarehouseDetailView) ? "功法" : _familyWarehouseDetailView;
		int columns = ResolveWarehouseColumns(true, 122f, 5);
		for (int i = 0; i < FamilyWarehouseViews.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			string nextView = FamilyWarehouseViews[i];
			int count = CountFamilyWarehouseItemsCached(family.FamilyId, nextView);
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(view, nextView, StringComparison.Ordinal)
				? new Color(0.32f, 0.42f, 0.34f)
				: new Color(0.23f, 0.23f, 0.23f);
			if (GUILayout.Button(nextView + " " + count, GUILayout.Height(34f)))
			{
				_familyWarehouseDetailView = nextView;
				view = nextView;
				_familyDetailScrollPosition = Vector2.zero;
			}
			GUI.backgroundColor = old;
			if (i % columns == columns - 1 || i == FamilyWarehouseViews.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		GUILayout.Space(6f);
		DrawFamilyWarehouseDetailGrid(family.FamilyId, view);
	}

	private void DrawFamilyHighRealmSources(XjCodexFamilyItem family)
	{
		if (family?.Members == null || family.Members.Count == 0 || string.IsNullOrWhiteSpace(family.PrimaryDaoTu))
		{
			DrawEmptyCard("当前照录中没有可展开的高境定脉人物。", "#777777");
			return;
		}
		int highest = 0;
		for (int i = 0; i < family.Members.Count; i++)
		{
			XjCodexFamilyMemberItem member = family.Members[i];
			if (member == null || !string.Equals(member.DaoTu, family.PrimaryDaoTu, StringComparison.Ordinal)) continue;
			highest = Math.Max(highest, member.RealmOrder);
		}
		if (highest <= 0) return;
		DrawSectionTitle("定脉高境", "#D2B5FF");
		int shown = 0;
		for (int i = 0; i < family.Members.Count && shown < 8; i++)
		{
			XjCodexFamilyMemberItem member = family.Members[i];
			if (member == null || member.RealmOrder != highest
				|| !string.Equals(member.DaoTu, family.PrimaryDaoTu, StringComparison.Ordinal)) continue;
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#D2B5FF");
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>" + Rich(Empty(member.Name, "未名族人")) + "</b>　" + Rich(XjDisplayNameSanitizer.GameTerm(member.Realm, "境界未载")));
			GUILayout.FlexibleSpace();
			DrawTag(family.PrimaryDaoTu, "#74E8FF");
			DrawActorFocusButton("定位", member.ActorId, GUILayout.Width(72f), GUILayout.Height(30f));
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=grey>" + Rich(Empty(member.KeySummary, Empty(member.CraftSummary, "作为本家当世最高层修士定下主脉。"))) + "</color>");
			GUILayout.EndVertical();
			shown++;
		}
	}

	private void DrawFamilyDomainArchive(XjCodexSnapshot snapshot, XjCodexFamilyItem family)
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#9CD7FF");
		GUILayout.Label("<b>门中位置</b>");
		GUILayout.BeginHorizontal();
		DrawMiniStat("所属宗门", Empty(family.SectName, "未入宗门"), "#FFD37A", GUILayout.Width(190f));
		DrawMiniStat("话语权", Empty(family.VoiceTier, "普通家族"), "#CFC7B2", GUILayout.Width(145f));
		DrawMiniStat("宗门关系", Empty(family.SectRelation, family.SectId > 0L ? "门中平稳" : "未入宗门"), "#A7E08A", GUILayout.Width(180f));
		DrawMiniStat("当前责任", FormatFamilyResponsibility(family), "#B7A7FF", GUILayout.Width(210f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();

		DrawSectionTitle("封邑与官职", "#9CD7FF");
		int shown = 0;
		for (int i = 0; i < snapshot.Cities.Count; i++)
		{
			XjCodexCityItem city = snapshot.Cities[i];
			if (city == null || city.GoverningFamilyId != family.FamilyId && city.ChallengerFamilyId != family.FamilyId) continue;
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(city.GoverningFamilyId == family.FamilyId ? "#A7E08A" : "#FFAA66");
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=20>" + Rich(city.Name) + "</size></b>");
			DrawTag(city.GoverningFamilyId == family.FamilyId ? "现任治理" : "正在争位",
				city.GoverningFamilyId == family.FamilyId ? "#A7E08A" : "#FFAA66");
			GUILayout.FlexibleSpace();
			DrawCityFocusButton("定位城镇", city.CityId, GUILayout.Width(105f), GUILayout.Height(34f));
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=grey>宗门：" + Rich(Empty(city.SectName, "普通国家"))
				+ "　·　治理状态：" + Rich(TranslateGovernanceState(city.GovernanceState))
				+ "　·　修士：" + city.CultivatorCount + "</color>");
			GUILayout.EndVertical();
			shown++;
		}
		if (shown == 0)
		{
			DrawEmptyCard("该家族当前没有确认封邑或治理权挑战。", "#777777");
		}
	}

	private void BeginContextNavigation(int targetTab, string returnLabel)
	{
		if (_currentTab >= 0 && _currentTab < _pageScrollPositions.Length)
		{
			_pageScrollPositions[_currentTab] = _scrollPosition;
		}
		_returnTab = _currentTab;
		_returnScrollPosition = _scrollPosition;
		_returnLabel = string.IsNullOrWhiteSpace(returnLabel) ? "返回来处" : returnLabel;
		_currentTab = Mathf.Clamp(targetTab, 0, TabNames.Length - 1);
		_scrollPosition = Vector2.zero;
	}

	private void ClearContextReturn()
	{
		_returnTab = -1;
		_returnToFullSearch = false;
		_returnScrollPosition = Vector2.zero;
		_returnLabel = string.Empty;
	}

	private void DrawContextReturnBar()
	{
		bool hasTabReturn = _returnTab >= 0 && _returnTab < TabNames.Length;
		if (!hasTabReturn && !_returnToFullSearch) return;
		GUILayout.BeginHorizontal(GUI.skin.box);
		DrawTag(_returnToFullSearch ? "由全卷检索跳转" : "由风险示警跳转", _returnToFullSearch ? "#9CD7FF" : "#FFAA66");
		bool goBack = GUILayout.Button(Empty(_returnLabel, "返回来处"), GUILayout.Width(150f), GUILayout.Height(34f));
		GUILayout.Label(_returnToFullSearch
			? "<color=grey>当前档案来自检索结果；返回后保留检索词、分类、结果和滚动位置。</color>"
			: "<color=grey>当前档案已锁定到示警所指对象；返回后保留原筛选与滚动位置。</color>");
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(5f);
		if (goBack)
		{
			int tab = hasTabReturn ? _returnTab : 0;
			Vector2 scroll = _returnScrollPosition;
			bool returnToSearch = _returnToFullSearch;
			ClearContextReturn();
			_currentTab = tab;
			_scrollPosition = scroll;
			if (returnToSearch)
			{
				_fullSearchOpen = true;
				_scrollPosition = _fullSearchScrollPosition;
			}
		}
	}

	private void JumpToConflictTarget(XjCodexConflictItem item)
	{
		if (item == null) return;
		string kind = Empty(item.Kind, "其他");
		string returnLabel = _currentTab == 10 ? "返回天下风险" : "返回天下总览";
		if (string.Equals(kind, "宗门关系", StringComparison.Ordinal))
		{
			BeginContextNavigation(1, returnLabel);
			_selectedSectId = item.SectId;
			SelectSectArchiveView("关系纪事");
			return;
		}
		if (string.Equals(kind, "宗门治理", StringComparison.Ordinal))
		{
			BeginContextNavigation(1, returnLabel);
			_selectedSectId = item.SectId;
			SelectSectArchiveView("山门总览");
			return;
		}
		if (string.Equals(kind, "世家", StringComparison.Ordinal))
		{
			BeginContextNavigation(3, returnLabel);
			_selectedFamilyId = item.FamilyId;
			SelectFamilyArchiveView("家族总览");
			return;
		}
		if (string.Equals(kind, "城镇", StringComparison.Ordinal))
		{
			BeginContextNavigation(4, returnLabel);
			_cityTargetId = item.CityId;
			return;
		}
		if (string.Equals(kind, "大阵", StringComparison.Ordinal))
		{
			BeginContextNavigation(1, returnLabel);
			_selectedSectId = item.SectId;
			SelectSectArchiveView("大阵洞天");
			return;
		}
		if (string.Equals(kind, "洞天", StringComparison.Ordinal))
		{
			if (!string.IsNullOrWhiteSpace(item.AdventureRealmName))
			{
				BeginContextNavigation(9, returnLabel);
				_adventureTargetCityId = item.CityId;
				_adventureTargetName = item.AdventureRealmName;
			}
			else
			{
				BeginContextNavigation(1, returnLabel);
				_selectedSectId = item.SectId;
				SelectSectArchiveView("大阵洞天");
			}
			return;
		}
		if (string.Equals(kind, "阴司", StringComparison.Ordinal))
		{
			BeginContextNavigation(8, returnLabel);
			_jinDanSectFilter = 0L;
			_jinDanActorFilter = item.ActorId;
			return;
		}
		BeginContextNavigation(10, returnLabel);
	}

	private bool MatchesAdventureTarget(XjCodexAdventureRealmItem item)
	{
		if (item == null) return false;
		if (!string.IsNullOrWhiteSpace(_adventureTargetName))
		{
			return string.Equals(item.Name, _adventureTargetName, StringComparison.Ordinal);
		}
		return _adventureTargetCityId <= 0L || item.AnchorCityId == _adventureTargetCityId;
	}

	private static string ResolveConflictJumpLabel(XjCodexConflictItem item)
	{
		if (item == null) return "查看相关档案";
		return item.Kind switch
		{
			"宗门关系" => "查看关系纪事",
			"宗门治理" => "查看宗门档案",
			"世家" => "查看家族档案",
			"城镇" => "查看城镇档案",
			"大阵" => "查看大阵洞天",
			"洞天" => "查看洞天档案",
			"阴司" => "查看真君档案",
			_ => "查看相关档案"
		};
	}

	private void DrawFamilySurnameRenamePanel(XjCodexFamilyItem item)
		{
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#CFAE6A");
			GUILayout.Label("<b>家族改姓</b>　<color=grey>只改变家族规范姓氏与在世族人的姓；血脉归属、家业仓库、封邑、宗门席位与旧世先祖姓名都不会改变。</color>");
			GUILayout.BeginHorizontal();
			GUILayout.Label("新姓", GUILayout.Width(45f));
			_renamingFamilySurname = GUILayout.TextField(_renamingFamilySurname ?? string.Empty, GUILayout.Width(110f));
			if (GUILayout.Button("确认改姓", GUILayout.Width(100f), GUILayout.Height(30f)))
			{
				bool changed = XjFamilySurnameService.TryChangeFamilySurname(item.FamilyId, _renamingFamilySurname, out string message);
				_familyRenameMessage = message;
				if (changed)
				{
					_renamingFamilySurname = XjFamilySurnameService.ResolveCurrentSurname(item.FamilyId);
					XjCodexSnapshotPublisher.RefreshNow();
					GUI.changed = true;
				}
			}
			if (GUILayout.Button("取消", GUILayout.Width(72f), GUILayout.Height(30f)))
			{
				_renamingFamilyId = 0L;
				_renamingFamilySurname = string.Empty;
				_familyRenameMessage = string.Empty;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!string.IsNullOrWhiteSpace(_familyRenameMessage))
			{
				GUILayout.Label("<color=#FFD37A>" + Rich(_familyRenameMessage) + "</color>");
			}
			GUILayout.EndVertical();
		}
}
