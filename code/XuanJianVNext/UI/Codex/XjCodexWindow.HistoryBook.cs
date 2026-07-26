using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private static readonly string[] HistoryBookViews = { "天下纪事", "修士列传", "世家纪事", "宗门纪事" };
	private const float HistorySubjectSelectorWidth = 218f;
	private const float HistorySubjectListWidth = 198f;
	private const float HistorySubjectCardWidth = 184f;
	private string _historyBookView = "天下纪事";
	private string _historySubjectSearch = string.Empty;
	private Vector2 _historySubjectListScroll = Vector2.zero;
	private Vector2 _historySubjectDetailScroll = Vector2.zero;
	private long _historySelectedActorId;
	private long _historySelectedFamilyId;
	private long _historySelectedSectId;
	private bool _historyMaintenanceExpanded;
	private bool _historyDiagnosticsExpanded;

	internal static void ShowHistoryActor(long actorId)
	{
		EnsurePersonalHistoryAnchor(actorId);
		OpenHistoryBook("修士列传", actorId, 0L, 0L);
	}

	private static void EnsurePersonalHistoryAnchor(long actorId)
	{
		if (actorId <= 0L || !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| actor?.data == null || !actor.isAlive()) return;
		// 跳转只负责修复角色当前境界投影，绝不再以“当前年份”补写入道事件。
		// 旧实现会把一次查看操作变成新的生涯边界，导致紫府、金丹之后的旧事全被过滤。
		XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
	}

	internal static void ShowHistoryFamily(long familyId)
	{
		OpenHistoryBook("世家纪事", 0L, familyId, 0L);
	}

	internal static void ShowHistorySect(long sectId)
	{
		OpenHistoryBook("宗门纪事", 0L, 0L, sectId);
	}

	private static void OpenHistoryBook(string view, long actorId, long familyId, long sectId)
	{
		Ensure();
		if (_instance == null) return;
		_instance._visible = true;
		_instance._currentTab = 11;
		_instance._historyBookView = view;
		_instance._historySelectedActorId = Math.Max(0L, actorId);
		_instance._historySelectedFamilyId = Math.Max(0L, familyId);
		_instance._historySelectedSectId = Math.Max(0L, sectId);
		_instance._historySubjectSearch = string.Empty;
		_instance._historySubjectListScroll = Vector2.zero;
		_instance._historySubjectDetailScroll = Vector2.zero;
		_instance._scrollPosition = Vector2.zero;
		_instance._historyMessage = string.Empty;
		XjThreeBookNavigationTarget.Set(actorId, familyId, sectId);
		XjCodexSnapshotPublisher.SetWindowOpen(true);
		// Target and optional personal anchor are now in place; publish synchronously so
		// the first rendered frame cannot reuse the previously selected subject.
		XjCodexSnapshotPublisher.RefreshNow();
		_instance.ApplyCodexPause();
		_instance.SyncOverlay();
	}

	private void DrawHistoryBookPage(XjCodexSnapshot snapshot)
	{
		ClearSectDetailState();
		DrawPageHeader("玄鉴史册", "天下纪事照录天下大势；修士列传、世家纪事与宗门纪事以真实事件为骨，分别写成人物生平、家门兴衰与山门岁月。");
		DrawHistoryBookViewTabs(snapshot);
		if (_historyDiagnosticsExpanded) DrawThreeBookDiagnostics(snapshot?.ThreeBookHealth);
		GUILayout.Space(8f);
		if (string.Equals(_historyBookView, "天下纪事", StringComparison.Ordinal))
		{
			DrawWorldChronicleView(snapshot);
			return;
		}
		if (string.Equals(_historyBookView, "修士列传", StringComparison.Ordinal))
		{
			DrawSubjectChronicleView(snapshot.PersonalBiographies, snapshot.BiographyActors, XjHistoryVisibility.Personal, "修士列传", ref _historySelectedActorId);
			return;
		}
		if (string.Equals(_historyBookView, "世家纪事", StringComparison.Ordinal))
		{
			DrawSubjectChronicleView(snapshot.FamilyChronicles, snapshot.ChronicleFamilies, XjHistoryVisibility.Family, "世家纪事", ref _historySelectedFamilyId);
			return;
		}
		DrawSubjectChronicleView(snapshot.SectChronicles, snapshot.ChronicleSects, XjHistoryVisibility.Sect, "宗门纪事", ref _historySelectedSectId);
	}

	private void DrawHistoryBookViewTabs(XjCodexSnapshot snapshot)
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawOrnamentDivider("#6FAE9D", "玄 鉴 分 卷");
		GUILayout.BeginHorizontal();
		for (int i = 0; i < HistoryBookViews.Length; i++)
		{
			string view = HistoryBookViews[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_historyBookView, view, StringComparison.Ordinal)
				? new Color(0.28f, 0.42f, 0.36f)
				: new Color(0.24f, 0.24f, 0.24f);
			if (GUILayout.Button(view, GUILayout.Width(166f), GUILayout.Height(42f)))
			{
				_historyBookView = view;
				_historySubjectSearch = string.Empty;
				_historySubjectListScroll = Vector2.zero;
				_historySubjectDetailScroll = Vector2.zero;
				_scrollPosition = Vector2.zero;
				_historyMessage = string.Empty;
			}
			GUI.backgroundColor = old;
		}
		GUILayout.FlexibleSpace();
		_historyDiagnosticsExpanded = false;
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

	private void DrawThreeBookDiagnostics(XjCodexThreeBookHealth health)
	{
		if (health == null) return;
		string accent = health.IsHealthy ? "#A7E08A" : "#FF8A7A";
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(accent);
		GUILayout.Label("<b><color=" + accent + ">三书接线诊断：</color></b>" + Rich(health.Summary));
		GUILayout.Label("<color=grey>待补世家事实 " + health.DeferredFamilyFacts
			+ "；已补 " + health.DeferredResolved + "；过期 " + health.DeferredExpired + "；容量丢弃 " + health.DeferredDropped + "。</color>");
		GUILayout.Label("<color=grey>原生道侣读取：检查 " + health.NativePartnerChecks + "；数据键命中 " + health.NativePartnerDataKeyHits
			+ "；反射命中 " + health.NativePartnerReflectionHits + "；未识别 " + health.NativePartnerMisses + "。</color>");
		DrawThreeBookStoreHealth(health.Personal);
		DrawThreeBookStoreHealth(health.Family);
		DrawThreeBookStoreHealth(health.Sect);
		GUILayout.EndVertical();
	}

	private static void DrawThreeBookStoreHealth(XjCodexThreeBookStoreHealth item)
	{
		if (item == null) return;
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>" + Rich(item.Name) + "</b>", GUILayout.Width(65f));
		GUILayout.Label("底本 " + item.Count + "/" + item.Capacity, GUILayout.Width(125f));
		GUILayout.Label("事实账本 " + item.LedgerCount + "/" + item.LedgerCapacity, GUILayout.Width(165f));
		GUILayout.Label("事件型 " + item.EventTypeCount + "/" + item.ExpectedEventTypeCount, GUILayout.Width(105f));
		GUILayout.Label("写入 " + item.Accepted + "/尝试 " + item.Attempts, GUILayout.Width(145f));
		GUILayout.Label("去重 " + (item.DuplicateSource + item.DuplicateSignature), GUILayout.Width(105f));
		GUILayout.Label("无效 " + item.Invalid, GUILayout.Width(85f));
		GUILayout.Label("裁剪 " + item.Trimmed, GUILayout.Width(85f));
		GUILayout.Label("最近 " + (item.LastEventYear > 0 ? "玄鉴历" + item.LastEventYear + "年" : "年代未详"), GUILayout.Width(145f));
		if (item.SoftOverflow > 0L) GUILayout.Label("<color=#FF8A7A>保护溢出 " + item.SoftOverflow + "</color>");
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=grey>载入 " + item.Imported + "；事实账本裁剪 " + item.LedgerPruned + "；已载事件型：</color>"
			+ Rich(string.IsNullOrWhiteSpace(item.EventTypes) ? "未载" : item.EventTypes));
		GUILayout.Label("<color=grey>尚未命中：</color>" + Rich(string.IsNullOrWhiteSpace(item.MissingEventTypes) ? "无" : item.MissingEventTypes));
	}

	private void DrawWorldChronicleView(XjCodexSnapshot snapshot)
	{
		int worldCount = CountHistoryBookEvents(snapshot.History, XjHistoryVisibility.World, 0L, _historyCategory, _historyImportantOnly);
		GUILayout.BeginHorizontal();
		DrawOverviewPill("天下纪事", worldCount.ToString(), "#FFD37A", GUILayout.Width(150f));
		DrawOverviewPill("全部事件", snapshot.History.Count.ToString(), "#CFC7B2", GUILayout.Width(150f));
		DrawOverviewPill("史册容量", snapshot.History.Count + "/" + snapshot.HistoryCapacity, "#B7A7FF", GUILayout.Width(190f));
		DrawOverviewPill("入谱候选", CountVisibility(snapshot.History, XjHistoryVisibility.CenturyCandidate).ToString(), "#A7E08A", GUILayout.Width(160f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(6f);

		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.Label("<b>天下筛选</b> <color=grey>这里只显示影响天下格局或重要度足够的事件；普通修士与家族细事进入各自纪事。</color>");
		GUILayout.BeginHorizontal();
		for (int i = 0; i < XjWorldHistoryCategory.Ordered.Length; i++) DrawHistoryCategoryButton(XjWorldHistoryCategory.Ordered[i]);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		_historyImportantOnly = GUILayout.Toggle(_historyImportantOnly, "只看重要事件", GUILayout.Width(170f));
		_historyTraceableOnly = GUILayout.Toggle(_historyTraceableOnly, "只看可定位事件", GUILayout.Width(190f));
		GUILayout.FlexibleSpace();
		_historyMaintenanceExpanded = GUILayout.Toggle(_historyMaintenanceExpanded, "历史整理", GUILayout.Width(120f));
		GUILayout.EndHorizontal();
		if (_historyMaintenanceExpanded) DrawHistoryBookMaintenance();
		GUILayout.EndVertical();
		DrawHistoryMessage();
		GUILayout.Space(8f);
		DrawHistoryTimeline(snapshot.History, XjHistoryVisibility.World, 0L, _historyCategory, _historyImportantOnly, _historyTraceableOnly);
	}

	private void DrawSubjectChronicleView(
		IReadOnlyList<XjCodexHistoryItem> history,
		IReadOnlyList<XjCodexHistorySubjectItem> subjects,
		XjHistoryVisibility visibility,
		string title,
		ref long selectedId)
	{
		long requestedId = selectedId;
		XjCodexHistorySubjectItem selected = ResolveHistorySubject(subjects, requestedId, out int matched);
		if (selected != null) selectedId = selected.SubjectId;
		else if (requestedId <= 0L) selectedId = 0L;
		float panelHeight = Mathf.Clamp(_windowRect.height - 300f, 420f, 820f);
		GUILayout.BeginHorizontal();
		DrawHistorySubjectSelector(subjects, selected, matched, title, ref selectedId, panelHeight);
		GUILayout.Space(8f);
		_historySubjectDetailScroll = GUILayout.BeginScrollView(
			_historySubjectDetailScroll,
			false,
			false,
			GUILayout.Height(panelHeight),
			GUILayout.ExpandWidth(true));
		GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
		if (selected == null)
		{
			string emptyMessage = requestedId > 0L
				? "没有找到指定的历史主体。该人物、世家或宗门可能已经失效，或尚未生成可读纪事。"
				: string.IsNullOrWhiteSpace(_historySubjectSearch) ? "尚无可读纪事。" : "没有符合搜索条件的历史主体。";
			DrawEmptyCard(emptyMessage, "#777777");
		}
		else
		{
			DrawHistorySubjectHeader(selected, visibility, history);
			DrawSubjectNarrativeTimeline(history, visibility, selected.SubjectId, selected.Name);
		}
		GUILayout.Space(18f);
		GUILayout.EndVertical();
		GUILayout.EndScrollView();
		GUILayout.EndHorizontal();
	}

	private void DrawHistorySubjectSelector(
		IReadOnlyList<XjCodexHistorySubjectItem> subjects,
		XjCodexHistorySubjectItem selected,
		int matched,
		string title,
		ref long selectedId,
		float panelHeight)
	{
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(HistorySubjectSelectorWidth), GUILayout.Height(panelHeight));
		DrawOrnamentDivider("#6FAE9D", title);
		GUILayout.BeginHorizontal(GUILayout.Width(HistorySubjectListWidth));
		GUILayout.Label("搜索", GUILayout.Width(48f), GUILayout.Height(36f));
		_historySubjectSearch = GUILayout.TextField(_historySubjectSearch ?? string.Empty, GUILayout.Width(135f), GUILayout.Height(36f));
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=grey>共 " + matched + " 卷 · 新录在前</color>", GUILayout.Width(HistorySubjectListWidth));
		_historySubjectListScroll = GUILayout.BeginScrollView(
			_historySubjectListScroll,
			false,
			true,
			GUILayout.Width(HistorySubjectListWidth),
			GUILayout.Height(Mathf.Max(300f, panelHeight - 112f)));
		int shown = 0;
		for (int i = 0; i < subjects.Count; i++)
		{
			XjCodexHistorySubjectItem item = subjects[i];
			if (item == null || !MatchesHistorySubjectSearch(item)) continue;
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = selected != null && selected.SubjectId == item.SubjectId
				? new Color(0.28f, 0.42f, 0.36f)
				: new Color(0.24f, 0.24f, 0.24f);
			string state = item.IsAliveOrActive ? "存续" : "旧录";
			string latest = FormatHistorySubjectYear(item.LatestYear);
			string label = (selected != null && selected.SubjectId == item.SubjectId ? "◆ " : "◇ ") + ClampInlineText(Empty(item.Name, "未名"), 10) + "\n"
				+ state + " · " + item.EventCount + "事 · " + latest;
			if (GUILayout.Button(label, GUILayout.Width(HistorySubjectCardWidth), GUILayout.Height(64f)))
			{
				selectedId = item.SubjectId;
				_historySubjectDetailScroll = Vector2.zero;
				_scrollPosition = Vector2.zero;
			}
			GUI.backgroundColor = old;
			GUILayout.Space(4f);
			shown++;
			if (shown >= 60)
			{
				GUILayout.Label("<color=grey>名录先显示60项，请使用搜索缩小范围。</color>");
				break;
			}
		}
		GUILayout.Space(22f);
		GUILayout.EndScrollView();
		GUILayout.EndVertical();
	}

	private void DrawHistorySubjectHeader(
		XjCodexHistorySubjectItem subject,
		XjHistoryVisibility visibility,
		IReadOnlyList<XjCodexHistoryItem> history)
	{
		string state = subject.IsAliveOrActive ? "今录·存续" : "旧录·已终";
		string stateColor = subject.IsAliveOrActive ? "#A7E08A" : "#888888";
		string accent = visibility == XjHistoryVisibility.Personal ? "#9CD7FF" : visibility == XjHistoryVisibility.Family ? "#A7E08A" : "#FFD37A";
		string subjectDisplayName = CleanThreeBookSubjectName(Empty(subject.Name, "未名"));
		string bookTitle = visibility == XjHistoryVisibility.Personal
			? subjectDisplayName + "列传"
			: visibility == XjHistoryVisibility.Family
				? subjectDisplayName + "家录"
				: subjectDisplayName + "山门实录";

		int earliestYear = 0;
		int latestYear = 0;
		int eventCount = 0;
		for (int i = 0; i < history.Count; i++)
		{
			XjCodexHistoryItem item = history[i];
			if (!MatchesHistoryBookEvent(item, visibility, subject.SubjectId, XjWorldHistoryCategory.All, false, false)) continue;
			eventCount++;
			if (item.Year <= 0) continue;
			if (earliestYear <= 0 || item.Year < earliestYear) earliestYear = item.Year;
			if (item.Year > latestYear) latestYear = item.Year;
		}
		string span = earliestYear > 0
			? (latestYear > earliestYear ? "玄鉴历 " + earliestYear + "—" + latestYear + " 年" : "玄鉴历 " + earliestYear + " 年")
			: "年代未详";

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(accent);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=25>" + Rich(bookTitle) + "</size></b>");
		DrawTag(state, stateColor);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Label("<b><color=" + accent + ">" + Rich(span) + "</color></b>　<color=grey>依真实经历按年编次；同一件事落在不同分卷中，会呈现人物、世家与宗门各自的意义。</color>");
		GUILayout.BeginHorizontal();
		DrawOverviewPill("载事", eventCount.ToString(), "#CFC7B2", GUILayout.Width(110f));
		DrawOverviewPill("史重", BuildImportanceMarks(subject.HighestImportance), ImportanceColor(subject.HighestImportance), GUILayout.Width(125f));
		DrawOverviewPill("最近", FormatHistorySubjectYear(subject.LatestYear), "#9CD7FF", GUILayout.Width(150f));
		DrawOverviewPill("最近一事", Truncate(Empty(subject.LatestTitle, "未载"), 38), "#FFD37A", GUILayout.Width(470f));
		GUILayout.FlexibleSpace();
		if (visibility == XjHistoryVisibility.Personal)
		{
			bool old = GUI.enabled;
			GUI.enabled = subject.IsAliveOrActive && CanFocusActor(subject.SubjectId);
			if (GUILayout.Button("定位其人", GUILayout.Width(105f), GUILayout.Height(34f))) TryFocusActor(subject.SubjectId);
			GUI.enabled = old;
		}
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		GUILayout.Space(8f);
	}

	private static string FormatHistorySubjectYear(int year)
	{
		return year > 0 ? "玄鉴历" + year + "年" : "年代未详";
	}

	private static string CleanThreeBookSubjectName(string value)
	{
		string text = string.IsNullOrWhiteSpace(value) ? "未名" : value.Trim();
		string[] realms = { "神丹", "金丹", "紫府", "筑基", "炼气", "胎息" };
		for (int i = 0; i < realms.Length; i++)
		{
			string suffix = "-" + realms[i];
			string fullWidthSuffix = "－" + realms[i];
			if (text.EndsWith(suffix, StringComparison.Ordinal)) return text.Substring(0, text.Length - suffix.Length).TrimEnd();
			if (text.EndsWith(fullWidthSuffix, StringComparison.Ordinal)) return text.Substring(0, text.Length - fullWidthSuffix.Length).TrimEnd();
		}
		return text;
	}

	private XjCodexHistorySubjectItem ResolveHistorySubject(
		IReadOnlyList<XjCodexHistorySubjectItem> subjects,
		long selectedId,
		out int matched)
	{
		matched = 0;
		XjCodexHistorySubjectItem first = null;
		XjCodexHistorySubjectItem selected = null;
		if (subjects == null) return null;
		for (int i = 0; i < subjects.Count; i++)
		{
			XjCodexHistorySubjectItem item = subjects[i];
			if (item == null || !MatchesHistorySubjectSearch(item)) continue;
			matched++;
			first ??= item;
			if (item.SubjectId == selectedId) selected = item;
		}
		return selectedId > 0L ? selected : first;
	}

	private bool MatchesHistorySubjectSearch(XjCodexHistorySubjectItem item)
	{
		if (item == null) return false;
		string search = (_historySubjectSearch ?? string.Empty).Trim();
		return search.Length == 0
			|| (item.Name ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
			|| (item.SearchText ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
			|| item.SubjectId.ToString().IndexOf(search, StringComparison.Ordinal) >= 0;
	}

	private void DrawSubjectNarrativeTimeline(
		IReadOnlyList<XjCodexHistoryItem> history,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		int matched = 0;
		for (int i = 0; i < history.Count; i++)
		{
			if (MatchesHistoryBookEvent(history[i], visibility, subjectId, XjWorldHistoryCategory.All, false, false)) matched++;
		}
		if (matched == 0)
		{
			DrawEmptyCard("当前对象尚无可写入实录的旧事。", "#777777");
			return;
		}

		int olderEntries = Math.Max(0, matched - MaxRenderedHistoryCardsPerPage);
		int shown = 0;
		int previousYear = int.MinValue;
		string accent = visibility == XjHistoryVisibility.Personal ? "#9CD7FF" : visibility == XjHistoryVisibility.Family ? "#A7E08A" : "#FFD37A";

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(accent);
		DrawOrnamentDivider(accent, "纪 年 实 录");
		GUILayout.Label("<color=grey>每一卷只围绕眼前之人、这一家或这一宗展开。天下大事只是背景，真正留下来的，是他们各自走过的岁月。</color>");
		GUILayout.Space(5f);

		// 快照已按“年份倒序、同年事件序号倒序”统一排序；四种史册共用同一顺序。
		for (int i = 0; i < history.Count; i++)
		{
			XjCodexHistoryItem item = history[i];
			if (!MatchesHistoryBookEvent(item, visibility, subjectId, XjWorldHistoryCategory.All, false, false)) continue;
			bool sameYear = shown > 0 && item.Year > 0 && item.Year == previousYear;
			DrawHistoryRecordLine(item, visibility, subjectId, subjectName, sameYear);
			previousYear = item.Year;
			shown++;
			if (shown >= MaxRenderedHistoryCardsPerPage) break;
		}

		if (olderEntries > 0)
		{
			GUILayout.Space(5f);
			GUILayout.Label("<color=grey>本卷共载 " + matched + " 事；为控制界面开销，较早的 " + olderEntries + " 事暂折叠，当前展示最新 " + shown + " 事。</color>");
		}
		GUILayout.Space(16f);
		GUILayout.EndVertical();
	}

	private void DrawHistoryRecordLine(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName,
		bool sameYear)
	{
		string accent = visibility == XjHistoryVisibility.Personal ? "#9CD7FF" : visibility == XjHistoryVisibility.Family ? "#A7E08A" : "#FFD37A";
		string tag = string.IsNullOrWhiteSpace(item.BookTag) ? (string.IsNullOrWhiteSpace(item.Title) ? "纪事" : item.Title) : item.BookTag;
		string text = CleanThreeBookNarrativeText(string.IsNullOrWhiteSpace(item.Body) ? item.Title : item.Body);
		string yearLabel = sameYear
			? "○ 同年："
			: item.Year > 0 ? "● 玄鉴历" + item.Year + "年：" : "● 年代未详：";

		GUILayout.BeginVertical();
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>" + yearLabel + "</b>", GUILayout.Width(155f));
		GUILayout.Label("<b><color=" + accent + ">【" + Rich(tag) + "】</color></b>", GUILayout.Width(125f));
		GUILayout.Label(text, GUILayout.ExpandWidth(true));
		GUILayout.EndHorizontal();
		DrawHistoryRecordActions(item, visibility);
		GUILayout.Space(3f);
		GUILayout.Box(GUIContent.none, GUILayout.Height(1f), GUILayout.ExpandWidth(true));
		GUILayout.Space(4f);
		GUILayout.EndVertical();
	}

	private static string CleanThreeBookNarrativeText(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;
		string value = text.Trim();
		string[] realms = { "胎息", "炼气", "筑基", "紫府", "金丹", "神丹", "结璘仙" };
		for (int i = 0; i < realms.Length; i++)
		{
			value = value.Replace("-" + realms[i], string.Empty).Replace("－" + realms[i], string.Empty);
		}
		return value;
	}

	private void DrawHistoryRecordActions(XjCodexHistoryItem item, XjHistoryVisibility visibility)
	{
		long actorId = item.ActorId > 0L ? item.ActorId : item.RelatedActorId;
		long familyId = item.FamilyId > 0L ? item.FamilyId : item.RelatedFamilyId;
		long sectId = item.SectId > 0L ? item.SectId : item.RelatedSectId;
		bool oldEnabled = GUI.enabled;
		GUILayout.BeginHorizontal();
		GUILayout.Space(280f);
		GUI.enabled = oldEnabled && actorId > 0L && visibility != XjHistoryVisibility.Personal;
		if (GUILayout.Button("阅其人", GUILayout.Width(82f), GUILayout.Height(32f)))
		{
			_historyBookView = "修士列传";
			_historySelectedActorId = actorId;
			_historySubjectSearch = string.Empty;
			_historySubjectListScroll = Vector2.zero;
			_scrollPosition = Vector2.zero;
		}
		GUI.enabled = oldEnabled && familyId > 0L && visibility != XjHistoryVisibility.Family;
		if (GUILayout.Button("阅其族", GUILayout.Width(82f), GUILayout.Height(32f)))
		{
			_historyBookView = "世家纪事";
			_historySelectedFamilyId = familyId;
			_historySubjectSearch = string.Empty;
			_historySubjectListScroll = Vector2.zero;
			_scrollPosition = Vector2.zero;
		}
		GUI.enabled = oldEnabled && sectId > 0L && visibility != XjHistoryVisibility.Sect;
		if (GUILayout.Button("阅其宗", GUILayout.Width(82f), GUILayout.Height(32f)))
		{
			_historyBookView = "宗门纪事";
			_historySelectedSectId = sectId;
			_historySubjectSearch = string.Empty;
			_historySubjectListScroll = Vector2.zero;
			_scrollPosition = Vector2.zero;
		}
		bool canActor = actorId > 0L && XjScheduler.ResolveActor(actorId, out Actor actor) && actor?.data != null && actor.isAlive();
		GUI.enabled = oldEnabled && canActor;
		if (GUILayout.Button("定位", GUILayout.Width(72f), GUILayout.Height(32f))) _historyMessage = TryFocusHistoryActor(actorId) ? "已定位相关人物。" : "人物已不在世。";
		GUI.enabled = oldEnabled && item.HasLocation;
		if (GUILayout.Button("旧址", GUILayout.Width(72f), GUILayout.Height(32f))) _historyMessage = TryFocusHistoryLocation(item) ? "已定位旧事发生处。" : "旧址暂不可寻。";
		GUI.enabled = oldEnabled;
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
	}

	private void DrawHistoryTimeline(
		IReadOnlyList<XjCodexHistoryItem> history,
		XjHistoryVisibility visibility,
		long subjectId,
		string category,
		bool importantOnly,
		bool traceableOnly)
	{
		int shown = 0;
		int hidden = 0;
		for (int i = 0; i < history.Count; i++)
		{
			XjCodexHistoryItem item = history[i];
			if (!MatchesHistoryBookEvent(item, visibility, subjectId, category, importantOnly, traceableOnly))
			{
				hidden++;
				continue;
			}
			DrawHistoryCard(item);
			shown++;
			if (shown >= MaxRenderedHistoryCardsPerPage)
			{
				GUILayout.Label("<color=grey>本页先列最近 " + MaxRenderedHistoryCardsPerPage + " 条纪事。</color>");
				break;
			}
		}
		if (shown == 0) DrawEmptyCard("当前对象尚无符合条件的纪事。", "#777777");
		else if (hidden > 0) GUILayout.Label("<color=grey>另有 " + hidden + " 条事件属于其他史册视图或筛选条件。</color>");
	}

	private static bool MatchesHistoryBookEvent(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string category,
		bool importantOnly,
		bool traceableOnly)
	{
		if (item == null) return false;
		if (!HasVisibility(item, visibility)) return false;
		if (visibility != XjHistoryVisibility.World && IsSuppressedThreeBookItem(item, visibility)) return false;
		if (subjectId > 0L)
		{
			long actualSubjectId = visibility == XjHistoryVisibility.Personal
				? item.ActorId
				: visibility == XjHistoryVisibility.Family ? item.FamilyId : visibility == XjHistoryVisibility.Sect ? item.SectId : 0L;
			if (actualSubjectId != subjectId) return false;
		}
		if (!string.Equals(category, XjWorldHistoryCategory.All, StringComparison.Ordinal)
			&& !string.Equals(category, item.Category, StringComparison.Ordinal)) return false;
		if (importantOnly && item.Importance < 3) return false;
		if (traceableOnly && !HasHistoryTraceTarget(item)) return false;
		return true;
	}

	private static bool IsSuppressedThreeBookItem(XjCodexHistoryItem item, XjHistoryVisibility visibility)
	{
		if (item == null) return false;
		string type = item.EventType ?? string.Empty;
		string text = type + " "
			+ (item.Title ?? string.Empty) + " "
			+ (item.Body ?? string.Empty) + " "
			+ (item.BookTag ?? string.Empty);
		if (text.IndexOf("CaiQiFa", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("采气法", StringComparison.Ordinal) >= 0) return true;

		bool isGongFa = type.IndexOf("GongFa", StringComparison.OrdinalIgnoreCase) >= 0
			|| type.IndexOf("TechniqueRecovered", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("功法", StringComparison.Ordinal) >= 0;
		if (!isGongFa) return false;
		if (text.IndexOf("四品", StringComparison.Ordinal) >= 0
			|| text.IndexOf("4品", StringComparison.OrdinalIgnoreCase) >= 0) return true;

		// 早期结构化三书正文未写品阶时，按当前家族/宗门真实功法库回查。
		string inheritanceName = ExtractBookInheritanceName(item.Body);
		if (inheritanceName.Length == 0) return false;
		if (visibility == XjHistoryVisibility.Family && item.FamilyId > 0L)
		{
			IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries = XjFamilyGongFaWarehouse.ReadFamilyEntries(item.FamilyId);
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Found && entries[i].Grade == 4
					&& string.Equals(entries[i].GongFaName, inheritanceName, StringComparison.Ordinal)) return true;
			}
		}
		if (visibility == XjHistoryVisibility.Sect && item.SectId > 0L)
		{
			IReadOnlyList<XjZongMenGongFaPavilionEntry> entries = XjZongMenGongFaPavilion.ReadGongFaEntries(item.SectId);
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Found && entries[i].Grade == 4
					&& string.Equals(entries[i].Name, inheritanceName, StringComparison.Ordinal)) return true;
			}
		}
		return false;
	}

	private static string ExtractBookInheritanceName(string body)
	{
		if (string.IsNullOrWhiteSpace(body)) return string.Empty;
		int start = body.IndexOf('《');
		if (start < 0) return string.Empty;
		int end = body.IndexOf('》', start + 1);
		return end > start + 1 ? body.Substring(start + 1, end - start - 1).Trim() : string.Empty;
	}

	private static bool HasVisibility(XjCodexHistoryItem item, XjHistoryVisibility visibility)
	{
		return item != null && (item.VisibilityFlags & (int)visibility) != 0;
	}

	private static int CountHistoryBookEvents(
		IReadOnlyList<XjCodexHistoryItem> history,
		XjHistoryVisibility visibility,
		long subjectId,
		string category,
		bool importantOnly)
	{
		int count = 0;
		for (int i = 0; i < history.Count; i++)
		{
			if (MatchesHistoryBookEvent(history[i], visibility, subjectId, category, importantOnly, false)) count++;
		}
		return count;
	}

	private static int CountVisibility(IReadOnlyList<XjCodexHistoryItem> history, XjHistoryVisibility visibility)
	{
		int count = 0;
		for (int i = 0; i < history.Count; i++) if (HasVisibility(history[i], visibility)) count++;
		return count;
	}

	private void DrawHistoryBookMaintenance()
	{
		GUILayout.Space(6f);
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.Label("<b>历史整理</b> <color=grey>只整理天下纪事；三卷独立史册不受此处清理影响。</color>");
		GUILayout.BeginHorizontal();
		GUILayout.Label("类别", GUILayout.Width(45f));
		for (int i = 0; i < XjWorldHistoryCategory.Ordered.Length; i++)
		{
			string category = XjWorldHistoryCategory.Ordered[i];
			if (GUILayout.Button(category, GUILayout.Width(86f), GUILayout.Height(27f)))
			{
				_historyClearCategory = category;
				_historyClearConfirm = false;
			}
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		GUILayout.Label("重要度上限", GUILayout.Width(90f));
		for (int importance = 1; importance <= 5; importance++)
		{
			if (GUILayout.Button(importance.ToString(), GUILayout.Width(40f), GUILayout.Height(27f)))
			{
				_historyClearMaxImportance = importance;
				_historyClearConfirm = false;
			}
		}
		_historyClearKeepProtected = GUILayout.Toggle(_historyClearKeepProtected, "保留重点", GUILayout.Width(105f));
		GUILayout.FlexibleSpace();
		int pending = XjWorldHistoryStore.CountClearableByFilter(_historyClearCategory, _historyClearMaxImportance, _historyClearKeepProtected);
		if (GUILayout.Button(_historyClearConfirm ? "确认清理 " + pending : "按条件清理 " + pending, GUILayout.Width(145f), GUILayout.Height(28f)))
		{
			if (_historyClearConfirm)
			{
				int removed = XjWorldHistoryStore.ClearByFilter(_historyClearCategory, _historyClearMaxImportance, _historyClearKeepProtected);
				_historyMessage = "已清理 " + removed + " 条天下纪事。";
				_historyClearConfirm = false;
				XjCodexSnapshotPublisher.RefreshNow();
				GUI.changed = true;
			}
			else _historyClearConfirm = true;
		}
		if (_historyClearConfirm && GUILayout.Button("取消", GUILayout.Width(65f), GUILayout.Height(28f))) _historyClearConfirm = false;
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

	private void DrawHistoryMessage()
	{
		if (!string.IsNullOrWhiteSpace(_historyMessage))
		{
			GUILayout.Space(5f);
			GUILayout.Label("<color=#FFD37A>" + Rich(_historyMessage) + "</color>");
		}
	}
}
