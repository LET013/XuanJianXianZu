using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Codex;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private const int MaxFullSearchResults = 160;
	private static readonly string[] FullSearchCategories = { "全部", "宗门", "世家", "城镇", "真君", "史册主体" };

	private sealed class CodexSearchResult
	{
		internal string Key = string.Empty;
		internal string Kind = string.Empty;
		internal string Title = string.Empty;
		internal string Subtitle = string.Empty;
		internal string SearchText = string.Empty;
		internal int Score;
		internal int Year;
		internal long SubjectId;
		internal int CenturyId;
	}

	private void ToggleFullSearch()
	{
		if (_fullSearchOpen)
		{
			_fullSearchScrollPosition = _scrollPosition;
			_fullSearchOpen = false;
			_scrollPosition = _currentTab >= 0 && _currentTab < _pageScrollPositions.Length
				? _pageScrollPositions[_currentTab]
				: Vector2.zero;
			return;
		}

		if (_currentTab >= 0 && _currentTab < _pageScrollPositions.Length)
		{
			_pageScrollPositions[_currentTab] = _scrollPosition;
		}
		_fullSearchOpen = true;
		_scrollPosition = _fullSearchScrollPosition;
		ClearContextReturn();
	}

	private void DrawFullSearchPage(XjCodexSnapshot snapshot)
	{
		if (snapshot != null
			&& snapshot.Version != _fullSearchSnapshotVersion
			&& !string.IsNullOrWhiteSpace(_fullSearchExecutedQuery))
		{
			_fullSearchQuery = _fullSearchExecutedQuery;
			_fullSearchCategory = _fullSearchExecutedCategory;
			ExecuteFullSearch(snapshot);
		}
		DrawPageHeader("全卷检索", "检索玄鉴仙鉴当前已经照录的宗门、世家、城镇、真君与史册主体。");
		DrawFullSearchControls(snapshot);
		DrawFullSearchSummary(snapshot);

		if (_fullSearchResults.Count == 0)
		{
			DrawEmptyCard(_fullSearchMessage, "#777777");
			return;
		}

		int shown = Mathf.Min(_fullSearchResults.Count, MaxRenderedCardsPerPage);
		for (int i = 0; i < shown; i++)
		{
			DrawFullSearchResult(_fullSearchResults[i]);
		}
		DrawListLimitNotice(_fullSearchResults.Count, shown);
	}

	private void DrawFullSearchControls(XjCodexSnapshot snapshot)
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#9CD7FF");
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>检索词</b>", GUILayout.Width(70f), GUILayout.Height(38f));
		_fullSearchQuery = GUILayout.TextField(_fullSearchQuery ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(38f));
		Color old = GUI.backgroundColor;
		GUI.backgroundColor = new Color(0.35f, 0.47f, 0.56f, 1f);
		if (GUILayout.Button("检索四卷", GUILayout.Width(112f), GUILayout.Height(38f)))
		{
			ExecuteFullSearch(snapshot);
		}
		GUI.backgroundColor = new Color(0.30f, 0.30f, 0.30f, 1f);
		if (GUILayout.Button("清空", GUILayout.Width(72f), GUILayout.Height(38f)))
		{
			_fullSearchQuery = string.Empty;
			_fullSearchResults.Clear();
			_fullSearchExecutedQuery = string.Empty;
			_fullSearchMessage = "输入名称或卷宗关键词后检索。";
			_fullSearchSnapshotVersion = snapshot?.Version ?? -1;
			_scrollPosition = Vector2.zero;
		}
		GUI.backgroundColor = old;
		GUILayout.EndHorizontal();

		int columns = ContentWidth < 900f ? 3 : FullSearchCategories.Length;
		for (int i = 0; i < FullSearchCategories.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			string category = FullSearchCategories[i];
			old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_fullSearchCategory, category, StringComparison.Ordinal)
				? new Color(0.34f, 0.44f, 0.52f, 1f)
				: new Color(0.23f, 0.23f, 0.23f, 1f);
			if (GUILayout.Button(category, GUILayout.Height(36f)))
			{
				_fullSearchCategory = category;
				if (!string.IsNullOrWhiteSpace(_fullSearchExecutedQuery)) ExecuteFullSearch(snapshot);
			}
			GUI.backgroundColor = old;
			if (i % columns == columns - 1 || i == FullSearchCategories.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		GUILayout.Label("<color=grey>可检索名称、宗门、世家、封邑、道途、果位及史册主体最近卷题；结果按精确度、近世年份与名称排序。</color>");
		GUILayout.EndVertical();
	}

	private void DrawFullSearchSummary(XjCodexSnapshot snapshot)
	{
		GUILayout.BeginHorizontal();
		DrawOverviewPill("宗门", (snapshot?.Sects?.Count ?? 0).ToString(), "#FFD37A", GUILayout.Width(110f));
		DrawOverviewPill("世家", (snapshot?.Families?.Count ?? 0).ToString(), "#A7E08A", GUILayout.Width(110f));
		DrawOverviewPill("城镇", (snapshot?.Cities?.Count ?? 0).ToString(), "#9CD7FF", GUILayout.Width(110f));
		DrawOverviewPill("真君", (snapshot?.JinDan?.Count ?? 0).ToString(), "#B7A7FF", GUILayout.Width(110f));
		DrawOverviewPill("命中", _fullSearchResults.Count.ToString(), "#CFC7B2", GUILayout.Width(110f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		if (!string.IsNullOrWhiteSpace(_fullSearchExecutedQuery))
		{
			GUILayout.Label("<color=grey>" + Rich(_fullSearchMessage) + "</color>");
		}
		GUILayout.Space(6f);
	}

	private void ExecuteFullSearch(XjCodexSnapshot snapshot)
	{
		_fullSearchResults.Clear();
		_fullSearchSnapshotVersion = snapshot?.Version ?? -1;
		_fullSearchExecutedQuery = (_fullSearchQuery ?? string.Empty).Trim();
		_fullSearchExecutedCategory = _fullSearchCategory;
		_scrollPosition = Vector2.zero;
		if (snapshot == null || snapshot.Version <= 0)
		{
			_fullSearchMessage = "玄鉴卷册尚未照定。";
			return;
		}
		if (_fullSearchExecutedQuery.Length == 0)
		{
			_fullSearchMessage = "请输入至少一个名称或卷宗关键词。";
			return;
		}

		HashSet<string> resultKeys = new HashSet<string>(StringComparer.Ordinal);
		if (SearchCategoryEnabled("宗门")) SearchSects(snapshot.Sects, resultKeys);
		if (SearchCategoryEnabled("世家")) SearchFamilies(snapshot.Families, resultKeys);
		if (SearchCategoryEnabled("城镇")) SearchCities(snapshot.Cities, resultKeys);
		if (SearchCategoryEnabled("真君")) SearchJinDan(snapshot.JinDan, resultKeys);
		if (SearchCategoryEnabled("史册主体"))
		{
			SearchHistorySubjects(snapshot.BiographyActors, "修士列传", resultKeys);
			SearchHistorySubjects(snapshot.ChronicleFamilies, "世家纪事", resultKeys);
			SearchHistorySubjects(snapshot.ChronicleSects, "宗门纪事", resultKeys);
			SearchCenturyAnnals(snapshot.CenturyAnnals, resultKeys);
		}

		_fullSearchResults.Sort(CompareSearchResults);
		if (_fullSearchResults.Count > MaxFullSearchResults)
		{
			_fullSearchResults.RemoveRange(MaxFullSearchResults, _fullSearchResults.Count - MaxFullSearchResults);
			_fullSearchMessage = "命中较多，当前只保留最相关的 " + MaxFullSearchResults + " 项。";
		}
		else
		{
			_fullSearchMessage = _fullSearchResults.Count > 0
				? "共命中 " + _fullSearchResults.Count + " 项。"
				: "当前四卷中没有匹配“" + _fullSearchExecutedQuery + "”的有据条目。";
		}
	}

	private bool SearchCategoryEnabled(string category)
	{
		return string.Equals(_fullSearchCategory, "全部", StringComparison.Ordinal)
			|| string.Equals(_fullSearchCategory, category, StringComparison.Ordinal);
	}

	private void SearchSects(IReadOnlyList<XjCodexSectItem> items, HashSet<string> keys)
	{
		if (items == null) return;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexSectItem item = items[i];
			if (item == null || item.SectId <= 0L) continue;
			string search = JoinSearchText(item.Name, item.SovereignName, item.FounderName, item.GoverningFamilyName,
				item.PillarFamilyName, item.CapitalCityName, item.StrongFamilySummary, item.DissidentFamilySummary,
				item.ResourceSummary, item.LastTaskSummary, item.PrimaryDaoTu, item.DaoTuControlSummary);
			AddSearchResult(keys, "宗门:" + item.SectId, "宗门", item.Name,
				"掌宗世家 " + Empty(item.GoverningFamilyName, "未定") + " · 山门 " + Empty(item.CapitalCityName, "未定")
				+ " · 真人" + item.ZiFuCount + " 真君" + item.JinDanCount,
				search, item.SectId, 0, item.FoundingYear);
		}
	}

	private void SearchFamilies(IReadOnlyList<XjCodexFamilyItem> items, HashSet<string> keys)
	{
		if (items == null) return;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexFamilyItem item = items[i];
			if (item == null || item.FamilyId <= 0L) continue;
			string search = JoinSearchText(item.Name, item.SectName, item.GoverningCities, item.Representative,
				item.AncestorName, item.ClanLeaderName, item.HeirName, item.PillarName, item.SupportedActorName,
				item.ActiveAspiration, item.FamilyTreasureSummary, item.LineageState, item.PrimaryDaoTu, item.DaoTuControlSummary);
			AddSearchResult(keys, "世家:" + item.FamilyId, "世家", item.Name,
				Empty(item.LineageState, "在世家族") + " · " + Empty(item.SectName, "未入宗门")
				+ " · 族中支柱 " + Empty(item.PillarName, "未定"),
				search, item.FamilyId, 0, item.AspirationSinceYear);
		}
	}

	private void SearchCities(IReadOnlyList<XjCodexCityItem> items, HashSet<string> keys)
	{
		if (items == null) return;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexCityItem item = items[i];
			if (item == null || item.CityId <= 0L) continue;
			string search = JoinSearchText(item.Name, item.KingdomName, item.SectName, item.GoverningFamilyName,
				item.ChallengerFamilyName, item.GovernanceState, item.FormationName, item.FormationLeadName);
			AddSearchResult(keys, "城镇:" + item.CityId, "城镇", item.Name,
				Empty(item.SectName, "普通国家") + " · " + FormatFamilyDisplayName(item.GoverningFamilyName, item.GoverningFamilyId, "望族未定")
				+ " · " + TranslateGovernanceState(item.GovernanceState),
				search, item.CityId, 0, 0);
		}
	}

	private void SearchJinDan(IReadOnlyList<XjCodexJinDanItem> items, HashSet<string> keys)
	{
		if (items == null) return;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexJinDanItem item = items[i];
			if (item == null || item.ActorId <= 0L) continue;
			string search = JoinSearchText(item.Name, item.Realm, item.CultivationPath, item.Achievement, item.DaoTu,
				item.JinXing, item.GuoWei, item.SectName, item.FamilyName, item.YinSiState, item.SecretRealmName);
			AddSearchResult(keys, "高境:" + item.ActorId, "高境", item.Name,
				Empty(item.Achievement, item.Realm) + " · " + Empty(item.DaoTu, "道途未载")
				+ " · " + Empty(item.FamilyName, "家世未载") + " · " + Empty(item.SectName, "散修"),
				search, item.ActorId, 0, item.ActivatedYear);
		}
	}

	private void SearchHistorySubjects(IReadOnlyList<XjCodexHistorySubjectItem> items, string kind, HashSet<string> keys)
	{
		if (items == null) return;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexHistorySubjectItem item = items[i];
			if (item == null || item.SubjectId <= 0L) continue;
			string key = HistorySubjectKey(kind, item.SubjectId);
			string search = JoinSearchText(item.Name, item.SearchText, item.LatestTitle, kind);
			AddSearchResult(keys, key, kind, item.Name,
				"收录 " + item.EventCount + " 事 · 最近 " + (item.LatestYear > 0 ? item.LatestYear + "年" : "未载")
				+ " · " + Empty(item.LatestTitle, "卷题未载"),
				search, item.SubjectId, 0, item.LatestYear);
		}
	}

	private void SearchCenturyAnnals(IReadOnlyList<XjCodexCenturyAnnalsItem> items, HashSet<string> keys)
	{
		if (items == null) return;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexCenturyAnnalsItem item = items[i];
			if (item == null || item.CenturyId <= 0) continue;
			string search = JoinSearchText(item.Title, item.WorldSummary, item.StartYear + "年", item.EndYear + "年", "百年世谱");
			AddSearchResult(keys, "百年世谱:" + item.CenturyId, "百年世谱", Empty(item.Title, "百年世谱"),
				item.StartYear + "—" + item.EndYear + "年 · 世家" + item.FamilyCount + " 宗门" + item.SectCount + " 人物" + item.ActorCount,
				search, 0L, item.CenturyId, item.EndYear);
		}
	}

	private static string HistorySubjectKey(string kind, long subjectId)
	{
		if (string.Equals(kind, "史册修士", StringComparison.Ordinal)
			|| string.Equals(kind, "修士列传", StringComparison.Ordinal)) return "史册修士:" + subjectId;
		if (string.Equals(kind, "史册世家", StringComparison.Ordinal)
			|| string.Equals(kind, "世家纪事", StringComparison.Ordinal)) return "史册世家:" + subjectId;
		if (string.Equals(kind, "史册宗门", StringComparison.Ordinal)
			|| string.Equals(kind, "宗门纪事", StringComparison.Ordinal)) return "史册宗门:" + subjectId;
		return kind + ":" + subjectId;
	}

	private void AddSearchResult(HashSet<string> keys, string key, string kind, string title, string subtitle,
		string searchText, long subjectId, int centuryId, int year)
	{
		if (!MatchesFullSearch(searchText, title, out int score) || !keys.Add(key)) return;
		_fullSearchResults.Add(new CodexSearchResult
		{
			Key = key,
			Kind = kind,
			Title = Empty(title, "未名条目"),
			Subtitle = subtitle ?? string.Empty,
			SearchText = searchText ?? string.Empty,
			Score = score,
			Year = Math.Max(0, year),
			SubjectId = subjectId,
			CenturyId = centuryId
		});
	}

	private bool MatchesFullSearch(string searchText, string title, out int score)
	{
		score = int.MaxValue;
		string query = (_fullSearchExecutedQuery ?? string.Empty).Trim();
		if (query.Length == 0) return false;
		string haystack = searchText ?? string.Empty;
		string[] terms = query.Split(new[] { ' ', '　', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < terms.Length; i++)
		{
			if (haystack.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) < 0) return false;
		}
		string safeTitle = title ?? string.Empty;
		if (string.Equals(safeTitle, query, StringComparison.OrdinalIgnoreCase)) score = 0;
		else if (safeTitle.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score = 1;
		else if (safeTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) score = 2;
		else score = 3;
		return true;
	}

	private static int CompareSearchResults(CodexSearchResult left, CodexSearchResult right)
	{
		if (ReferenceEquals(left, right)) return 0;
		if (left == null) return 1;
		if (right == null) return -1;
		int score = left.Score.CompareTo(right.Score);
		if (score != 0) return score;
		int year = right.Year.CompareTo(left.Year);
		if (year != 0) return year;
		int kind = string.Compare(left.Kind, right.Kind, StringComparison.Ordinal);
		return kind != 0 ? kind : string.Compare(left.Title, right.Title, StringComparison.Ordinal);
	}

	private static string JoinSearchText(params string[] parts)
	{
		if (parts == null || parts.Length == 0) return string.Empty;
		return string.Join(" ", parts);
	}

	private void DrawFullSearchResult(CodexSearchResult item)
	{
		if (item == null) return;
		string color = SearchKindColor(item.Kind);
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		DrawTag(item.Kind, color);
		GUILayout.Label("<b><size=20>" + Rich(item.Title) + "</size></b>");
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("打开卷宗", GUILayout.Width(105f), GUILayout.Height(36f)))
		{
			OpenFullSearchResult(item);
		}
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=grey>" + Rich(item.Subtitle) + "</color>");
		GUILayout.EndVertical();
		GUILayout.Space(5f);
	}

	private static string SearchKindColor(string kind)
	{
		if (string.Equals(kind, "宗门", StringComparison.Ordinal) || string.Equals(kind, "史册宗门", StringComparison.Ordinal)
			|| string.Equals(kind, "宗门纪事", StringComparison.Ordinal)) return "#FFD37A";
		if (string.Equals(kind, "世家", StringComparison.Ordinal) || string.Equals(kind, "史册世家", StringComparison.Ordinal)
			|| string.Equals(kind, "世家纪事", StringComparison.Ordinal)) return "#A7E08A";
		if (string.Equals(kind, "城镇", StringComparison.Ordinal)) return "#9CD7FF";
		if (string.Equals(kind, "真君", StringComparison.Ordinal)) return "#B7A7FF";
		if (string.Equals(kind, "百年世谱", StringComparison.Ordinal)) return "#8FA9C7";
		return "#CFC7B2";
	}

	private void OpenFullSearchResult(CodexSearchResult item)
	{
		if (item == null) return;
		_fullSearchScrollPosition = _scrollPosition;
		_returnTab = _currentTab;
		_returnScrollPosition = _currentTab >= 0 && _currentTab < _pageScrollPositions.Length
			? _pageScrollPositions[_currentTab]
			: Vector2.zero;
		_returnLabel = "返回全卷检索";
		_returnToFullSearch = true;
		_fullSearchOpen = false;
		_scrollPosition = Vector2.zero;

		if (string.Equals(item.Kind, "宗门", StringComparison.Ordinal))
		{
			_selectedSectId = item.SubjectId;
			SelectSectArchiveView("山门总览");
			_currentTab = 1;
			return;
		}
		if (string.Equals(item.Kind, "世家", StringComparison.Ordinal))
		{
			_selectedFamilyId = item.SubjectId;
			SelectFamilyArchiveView("家族总览");
			_currentTab = 3;
			return;
		}
		if (string.Equals(item.Kind, "城镇", StringComparison.Ordinal))
		{
			_cityTargetId = item.SubjectId;
			_currentTab = 4;
			return;
		}
		if (string.Equals(item.Kind, "真君", StringComparison.Ordinal))
		{
			_jinDanSectFilter = 0L;
			_jinDanActorFilter = 0L;
			_selectedJinDanActorId = item.SubjectId;
			_jinDanPathFilter = "全部修法";
			_jinDanAchievementFilter = "全部身份";
			_jinDanYinSiFilter = "全部状态";
			_jinDanSearchText = string.Empty;
			_currentTab = 8;
			return;
		}
		if (string.Equals(item.Kind, "百年世谱", StringComparison.Ordinal))
		{
			_centuryAnnalsSelectedCenturyId = item.CenturyId;
			_centuryAnnalsCategory = "总览";
			_currentTab = 12;
			return;
		}

		_currentTab = 11;
		_historySubjectSearch = string.Empty;
		_historySubjectListScroll = Vector2.zero;
		_historySubjectDetailScroll = Vector2.zero;
		if (string.Equals(item.Kind, "史册世家", StringComparison.Ordinal)
			|| string.Equals(item.Kind, "世家纪事", StringComparison.Ordinal))
		{
			_historyBookView = "世家纪事";
			_historySelectedFamilyId = item.SubjectId;
			_historySelectedActorId = 0L;
			_historySelectedSectId = 0L;
		}
		else if (string.Equals(item.Kind, "史册宗门", StringComparison.Ordinal)
			|| string.Equals(item.Kind, "宗门纪事", StringComparison.Ordinal))
		{
			_historyBookView = "宗门纪事";
			_historySelectedSectId = item.SubjectId;
			_historySelectedActorId = 0L;
			_historySelectedFamilyId = 0L;
		}
		else
		{
			_historyBookView = "修士列传";
			_historySelectedActorId = item.SubjectId;
			_historySelectedFamilyId = 0L;
			_historySelectedSectId = 0L;
		}
	}
}
