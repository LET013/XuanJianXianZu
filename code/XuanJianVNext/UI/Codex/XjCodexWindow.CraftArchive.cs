using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.UI.ShenBing;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private static readonly string[] CraftTreasureViews = { "玄鉴四艺", "万宝录" };
	private string _craftTreasureView = "玄鉴四艺";

	private void DrawCraftTreasurePage(XjCodexSnapshot snapshot)
	{
		DrawPageHeader("玄鉴四艺", "四艺与万宝合归一卷：前页观丹、器、符、阵传承，后页录当世法器、灵宝与法宝。");
		GUILayout.BeginHorizontal(GUI.skin.box);
		for (int i = 0; i < CraftTreasureViews.Length; i++)
		{
			string view = CraftTreasureViews[i];
			GUI.backgroundColor = string.Equals(_craftTreasureView, view, StringComparison.Ordinal)
				? new Color(0.38f, 0.50f, 0.44f, 1f)
				: new Color(0.24f, 0.25f, 0.28f, 1f);
			if (GUILayout.Button(view, GUILayout.Height(38f)))
			{
				_craftTreasureView = view;
				_scrollPosition = Vector2.zero;
			}
		}
		GUI.backgroundColor = Color.white;
		GUILayout.EndHorizontal();
		GUILayout.Space(7f);
		if (string.Equals(_craftTreasureView, "万宝录", StringComparison.Ordinal))
			DrawTreasureRecordPage();
		else
			DrawCraftArchivePage(snapshot);
	}

	private void DrawCraftArchivePage(XjCodexSnapshot snapshot)
	{
		Dictionary<string, CraftProfessionSummary> summaries = GetCraftProfessionSummaries(snapshot);
		DrawCraftSummaryMetrics(snapshot, summaries);
		DrawArchiveViewTabs(CraftArchiveViews, _craftArchiveView, "#9FC9C0", view =>
		{
			_craftArchiveView = view;
			_craftSearchText = string.Empty;
			_craftBusyOnly = false;
			_scrollPosition = Vector2.zero;
		});
		GUILayout.Space(8f);

		switch (_craftArchiveView)
		{
			case "丹道":
				DrawCraftBranchHeader("丹道", "炼丹师、丹药图录与当前炼丹任务", "炼丹师", "#A7E08A", summaries);
				DrawPillCatalogPanel();
				DrawCraftActorRoster(snapshot, "炼丹师");
				break;
			case "器道":
				DrawCraftBranchHeader("器道", "炼器师与在世器物各归其录，器物传承沿用万宝录", "炼器师", "#FFD37A", summaries);
				DrawArtifactCraftLink();
				DrawCraftActorRoster(snapshot, "炼器师");
				break;
			case "符箓":
				DrawCraftBranchHeader("符箓", "符箓师、符箓图录与当前制符任务", "符箓师", "#9CD7FF", summaries);
				DrawTalismanCatalogPanel();
				DrawCraftActorRoster(snapshot, "符箓师");
				break;
			case "阵法":
				DrawCraftBranchHeader("阵法", "阵法师与天下现存宗门大阵相互对照", "阵法师", "#B7A7FF", summaries);
				DrawFormationCraftLedger(snapshot);
				DrawCraftActorRoster(snapshot, "阵法师");
				break;
			default:
				DrawCraftWorldOverview(snapshot, summaries);
				break;
		}
	}

	private Dictionary<string, CraftProfessionSummary> GetCraftProfessionSummaries(XjCodexSnapshot snapshot)
	{
		if (snapshot != null && _craftSummaryCacheVersion == snapshot.Version) return _craftSummaryCache;
		_craftSummaryCache.Clear();
		if (snapshot?.CraftProfessionSummaries != null && snapshot.CraftProfessionSummaries.Count > 0)
		{
			for (int i = 0; i < snapshot.CraftProfessionSummaries.Count; i++)
			{
				XjCodexCraftProfessionSummaryItem item = snapshot.CraftProfessionSummaries[i];
				if (item == null || string.IsNullOrWhiteSpace(item.ProfessionName)) continue;
				_craftSummaryCache[item.ProfessionName] = new CraftProfessionSummary
				{
					Total = item.Total,
					Busy = item.Busy,
					TopRealm = item.TopRealm ?? string.Empty
				};
			}
		}
		else
		{
			Dictionary<string, CraftProfessionSummary> built = BuildCraftProfessionSummaries(snapshot?.CraftActors);
			foreach (KeyValuePair<string, CraftProfessionSummary> pair in built)
			{
				_craftSummaryCache[pair.Key] = pair.Value;
			}
		}
		_craftSummaryCacheVersion = snapshot?.Version ?? -1;
		return _craftSummaryCache;
	}

	private void DrawCraftSummaryMetrics(
		XjCodexSnapshot snapshot,
		IReadOnlyDictionary<string, CraftProfessionSummary> summaries)
	{
		int columns = ContentWidth < 920f ? 2 : 5;
		float width = Mathf.Max(135f, (ContentWidth - 100f) / columns);
		for (int i = 0; i < 5; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			switch (i)
			{
				case 0:
					DrawOverviewPill("炼丹师", CountCraftSummary(summaries, "炼丹师").ToString(), "#A7E08A", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 1:
					DrawOverviewPill("炼器师", CountCraftSummary(summaries, "炼器师").ToString(), "#FFD37A", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 2:
					DrawOverviewPill("符箓师", CountCraftSummary(summaries, "符箓师").ToString(), "#9CD7FF", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 3:
					DrawOverviewPill("阵法师", CountCraftSummary(summaries, "阵法师").ToString(), "#B7A7FF", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 4:
					DrawOverviewPill("进行中任务", snapshot.OpenCraftTaskCount.ToString(), "#FFAA66", GUILayout.Width(width), GUILayout.Height(80f));
					break;
			}
			if (i % columns == columns - 1 || i == 4)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private void DrawCraftWorldOverview(
		XjCodexSnapshot snapshot,
		IReadOnlyDictionary<string, CraftProfessionSummary> summaries)
	{
		DrawSectionTitle("百艺格局", "#9FC9C0");
		string[] professions = { "炼丹师", "炼器师", "符箓师", "阵法师" };
		string[] views = { "丹道", "器道", "符箓", "阵法" };
		float cardWidth = Mathf.Max(120f, (ContentWidth - 46f) / 4f);
		GUILayout.BeginHorizontal();
		for (int i = 0; i < professions.Length; i++)
		{
			DrawCraftOverviewCard(professions[i], views[i], summaries, cardWidth);
			if (i < professions.Length - 1) GUILayout.Space(6f);
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		DrawSectionTitle("当前百工", "#CFC7B2");
		if (snapshot.CraftActorCount == 0)
		{
			DrawEmptyCard("尚无角色获得玄鉴百艺。", "#777777");
			return;
		}
		GUILayout.Label("<color=grey>四艺角色不再混排成一条长名单；进入对应单艺即可检索、查看任务并定位角色。</color>");
	}

	private void DrawCraftOverviewCard(
		string profession,
		string view,
		IReadOnlyDictionary<string, CraftProfessionSummary> summaries,
		float width)
	{
		summaries.TryGetValue(profession, out CraftProfessionSummary summary);
		summary ??= new CraftProfessionSummary();
		string color = ProfessionColor(profession);
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width));
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=22><color=" + color + ">" + Rich(view) + "</color></size></b>");
		GUILayout.FlexibleSpace();
		DrawTag(summary.Total + "人", color);
		GUILayout.EndHorizontal();
		GUILayout.Label("<b>艺修</b>　" + summary.Total + "人　　<b>忙碌</b>　" + summary.Busy + "人");
		GUILayout.Label("<b>最高修为</b>　" + Rich(Empty(summary.TopRealm, "尚无")));
		GUILayout.Label("<color=grey>" + Rich(ResolveCraftBranchDescription(view)) + "</color>");
		if (GUILayout.Button("进入" + view, GUILayout.Height(36f)))
		{
			_craftArchiveView = view;
			_craftSearchText = string.Empty;
			_craftBusyOnly = false;
			_scrollPosition = Vector2.zero;
		}
		GUILayout.EndVertical();
	}

	private void DrawCraftBranchHeader(
		string view,
		string description,
		string profession,
		string color,
		IReadOnlyDictionary<string, CraftProfessionSummary> summaries)
	{
		summaries.TryGetValue(profession, out CraftProfessionSummary summary);
		summary ??= new CraftProfessionSummary();
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=22><color=" + color + ">" + Rich(view) + "</color></size></b>");
		GUILayout.FlexibleSpace();
		DrawTag(summary.Total + "名艺修", color);
		GUILayout.EndHorizontal();
		GUILayout.Label(Rich(description));
		GUILayout.Label("<color=grey>最高修为 " + Rich(Empty(summary.TopRealm, "尚无"))
			+ "　·　任务中 " + summary.Busy + "人</color>");
		GUILayout.EndVertical();
	}

	private void DrawArtifactCraftLink()
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#FFD37A");
		GUILayout.Label("<b>器物成果归万宝录</b>");
		GUILayout.Label("<color=grey>器道页只整理炼器师与任务；法器、灵宝、法宝、主人及传承继续由现有万宝录按需读取，避免仙鉴复制第二份器物总表。</color>");
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("转入万宝录", GUILayout.Width(145f), GUILayout.Height(38f)))
		{
			_craftTreasureView = "万宝录";
			_scrollPosition = Vector2.zero;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		GUILayout.Space(8f);
	}

	private void DrawFormationCraftLedger(XjCodexSnapshot snapshot)
	{
		DrawSectionTitle("阵法实录", "#B7A7FF");
		GUILayout.BeginHorizontal();
		DrawOverviewPill("运转大阵", snapshot.OperationalFormationCount.ToString(), "#A7E08A", GUILayout.Width(155f), GUILayout.Height(78f));
		DrawOverviewPill("已破大阵", snapshot.BrokenFormationCount.ToString(), snapshot.BrokenFormationCount > 0 ? "#FF8877" : "#888888", GUILayout.Width(155f), GUILayout.Height(78f));
		DrawOverviewPill("大阵档案", snapshot.Formations.Count.ToString(), "#B7A7FF", GUILayout.Width(155f), GUILayout.Height(78f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		int shown = Mathf.Min(snapshot.Formations.Count, 12);
		for (int i = 0; i < shown; i++)
		{
			XjCodexFormationItem item = snapshot.Formations[i];
			if (item == null) continue;
			string color = item.CurrentDurability <= 0 ? "#FF6666" : "#A7E08A";
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(color);
			GUILayout.Label("<b>" + Rich(Empty(item.SectName, "未名宗门")) + "</b>");
			GUILayout.BeginHorizontal();
			DrawMiniStat("阵势", ResolveFormationDisplayName(item.Grade), "#B7A7FF", GUILayout.Width(170f));
			DrawMiniStat("状态", TranslateFormationState(item.BuildState), color, GUILayout.Width(145f));
			if (ContentWidth >= 1000f)
			{
				DrawMiniStat("主持阵法师", Empty(item.LeadFormationMasterName, "未任命"), "#9CD7FF", GUILayout.Width(190f));
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (ContentWidth < 1000f)
			{
				GUILayout.Label("<b>主持阵法师</b>　" + Rich(Empty(item.LeadFormationMasterName, "未任命")));
			}
			GUILayout.Label("<color=grey>耐久 当前" + item.CurrentDurability + " · 上限" + item.MaxDurability
				+ "　·　最近维修 " + (item.LastRepairYear > 0 ? XjChronology.FormatYear(item.LastRepairYear) : "未载") + "</color>");
			GUILayout.EndVertical();
		}
		DrawListLimitNotice(snapshot.Formations.Count, shown);
	}

	private void DrawCraftActorRoster(XjCodexSnapshot snapshot, string profession)
	{
		DrawSectionTitle(profession + "名录", ProfessionColor(profession));
		DrawCraftRosterToolbar();
		List<XjCodexCraftItem> items = GetCraftActorDisplayItems(snapshot, profession);
		int matched = items.Count;
		int shown = 0;
		int renderCount = Mathf.Min(matched, MaxRenderedCraftActorsPerPage);
		int columns = ContentWidth < 1120f ? 1 : 2;
		for (int i = 0; i < renderCount; i++)
		{
			XjCodexCraftItem item = items[i];
			if (shown % columns == 0) GUILayout.BeginHorizontal();
			DrawCraftActorArchiveCard(item);
			shown++;
			if (shown % columns == 0 || shown == renderCount)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		if (matched == 0) DrawEmptyCard("当前检索条件下暂无" + profession + "。", "#777777");
		DrawListLimitNotice(matched, shown);
	}

	private List<XjCodexCraftItem> GetCraftActorDisplayItems(XjCodexSnapshot snapshot, string profession)
	{
		string key = profession + "|" + _craftBusyOnly + "|" + (_craftSearchText ?? string.Empty).Trim();
		if (snapshot != null
			&& _craftDisplayCacheVersion == snapshot.Version
			&& string.Equals(_craftDisplayCacheKey, key, StringComparison.Ordinal))
		{
			return _craftDisplayCache;
		}

		_craftDisplayCache.Clear();
		_craftDisplayCacheVersion = snapshot?.Version ?? -1;
		_craftDisplayCacheKey = key;
		if (snapshot?.CraftActors == null) return _craftDisplayCache;
		for (int i = 0; i < snapshot.CraftActors.Count; i++)
		{
			XjCodexCraftItem item = snapshot.CraftActors[i];
			if (MatchesCraftArchiveFilter(item, profession)) _craftDisplayCache.Add(item);
		}
		return _craftDisplayCache;
	}

	private void DrawCraftRosterToolbar()
	{
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>检索</b>", GUILayout.Width(55f));
		_craftSearchText = GUILayout.TextField(_craftSearchText ?? string.Empty, GUILayout.MinWidth(220f), GUILayout.Height(34f));
		Color old = GUI.backgroundColor;
		GUI.backgroundColor = _craftBusyOnly ? new Color(0.55f, 0.38f, 0.22f) : new Color(0.23f, 0.23f, 0.23f);
		if (GUILayout.Button(_craftBusyOnly ? "仅任务中 ✓" : "仅任务中", GUILayout.Width(110f), GUILayout.Height(34f)))
		{
			_craftBusyOnly = !_craftBusyOnly;
		}
		GUI.backgroundColor = old;
		if (GUILayout.Button("清除", GUILayout.Width(72f), GUILayout.Height(34f)))
		{
			_craftSearchText = string.Empty;
			_craftBusyOnly = false;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
	}

	private bool MatchesCraftArchiveFilter(XjCodexCraftItem item, string profession)
	{
		if (item == null || !string.Equals(item.ProfessionName, profession, StringComparison.Ordinal)) return false;
		if (_craftBusyOnly && string.IsNullOrWhiteSpace(item.TaskKind)) return false;
		string query = (_craftSearchText ?? string.Empty).Trim();
		return query.Length == 0
			|| ContainsSearch(item.ActorName, query)
			|| ContainsSearch(item.CraftRank, query)
			|| ContainsSearch(item.Realm, query)
			|| ContainsSearch(item.CityName, query)
			|| ContainsSearch(item.TaskBlueprint, query);
	}

	private void DrawCraftActorArchiveCard(XjCodexCraftItem item)
	{
		string color = ProfessionColor(item.ProfessionName);
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(360f));
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=20>" + Rich(Empty(item.ActorName, "未名艺修")) + "</size></b>");
		GUILayout.FlexibleSpace();
		DrawTag(Empty(item.CraftRank, "未入艺"), color);
		GUILayout.EndHorizontal();
		GUILayout.Label("<b>境界</b>　" + Rich(XjDisplayNameSanitizer.GameTerm(item.Realm, "境界未载"))
			+ "　　<b>所在</b>　" + Rich(Empty(item.CityName, "游离")));
		GUILayout.Label("<b>当前任务</b>　" + Rich(FormatCraftTaskDisplay(item)));
		DrawActorFocusButton("定位艺修", item.ActorId, GUILayout.Width(105f), GUILayout.Height(34f));
		GUILayout.EndVertical();
	}

	private static string ResolveCraftBranchDescription(string view)
	{
		return view switch
		{
			"丹道" => "查看炼丹师、丹药效用与正在进行的炼丹任务。",
			"器道" => "查看炼器师；器物本身继续归入万宝录，不重复建档。",
			"符箓" => "查看符箓师、符箓效用与正在进行的制符任务。",
			"阵法" => "查看阵法师，并与天下宗门大阵的主持和损毁状态对照。",
			_ => "四艺各归其卷。"
		};
	}
}
