using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.UI.GuoWei;
using XuanJianVNext.UI.ShenBing;
using XuanJianVNext.Systems.Rank;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private sealed class CodexRankRow
	{
		internal XjRankItem Item;
		internal float Value;
		internal string Display = string.Empty;
	}

	private static readonly string[] TreasureFilters =
	{
		"全部器物", "攻击类", "防御类", "附属类", "筑基法器", "紫府灵宝", "金丹法宝"
	};
	private static readonly string[] DaoTuCounterFilters =
	{
		"全部", "三阴", "三阳", "三雷", "金德", "木德", "水德", "火德", "土德", "十二炁", "并古"
	};
	private static readonly string[] GuoWeiAuthorityViews = { "位序总览", "持有权柄" };
	private static readonly string[] DaoTuArchiveViews = { "道途总览", "克制关系" };

	private string _rankMetricId = "power";
	private int _rankCacheYear = -1;
	private int _rankCacheCultivatorCount = -1;
	private string _rankCacheMetricId = string.Empty;
	private readonly List<CodexRankRow> _rankRows = new List<CodexRankRow>();
	private int _authorityCacheYear = -1;
	private int _authorityReadModelRevision = int.MinValue;
	private XjGuoWeiAuthorityCodexSnapshot _authorityReadModel = new XjGuoWeiAuthorityCodexSnapshot();
	private List<XjGuoWeiQuanBingWindow.DaoTuOverview> _authorityRows =
		new List<XjGuoWeiQuanBingWindow.DaoTuOverview>();
	private string _treasureFilter = "全部器物";
	private int _treasureCacheYear = -1;
	private string _treasureCacheFilter = string.Empty;
	private List<XjFaBaoRankEntry> _treasureRows = new List<XjFaBaoRankEntry>();
	private string _daoTuCounterFilter = "全部";
	private string _authorityFilter = "全部";
	private string _guoWeiAuthorityView = "位序总览";
	private string _daoTuArchiveView = "道途总览";

	private void DrawCultivatorRankPage()
	{
		DrawPageHeader("玄鉴修士榜", "按当世在录修士排定战力、境界、四艺与剑意诸榜，所列名次均取人物当前真实状态。");
		XjRankSortSystem.Init();
		DrawRankMetricPicker();
		EnsureRankRows();

		string metricName = ResolveRankMetricDefinition()?.Name ?? "战力";
		GUILayout.Label("<color=grey>当前按" + Rich(metricName) + "由高到低排列，共 "
			+ _rankRows.Count + " 人；正文最多展开前 " + MaxRenderedCardsPerPage + " 名。</color>");
		int visible = Math.Min(MaxRenderedCardsPerPage, _rankRows.Count);
		for (int i = 0; i < visible; i++)
		{
			CodexRankRow row = _rankRows[i];
			XjRankItem item = row.Item;
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(i < 3 ? ResolveRankColor(i) : "#6FAE9D");
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=22><color=" + ResolveRankColor(i) + ">#" + (i + 1) + "</color></size></b>", GUILayout.Width(58f));
			GUILayout.BeginVertical(GUILayout.Width(245f));
			GUILayout.Label("<b><size=18>" + Rich(item.Name) + "</size></b>");
			GUILayout.Label("<color=grey>" + Rich(XjDisplayNameSanitizer.GameTerm(item.DaoTu, "道途未定")) + "</color>");
			GUILayout.EndVertical();
			DrawMiniStat("道行", Empty(item.RealmDisplay, "未入道"), "#F0D58B", GUILayout.Width(145f));
			DrawMiniStat(metricName, row.Display, "#9CD7FF", GUILayout.Width(165f));
			GUILayout.FlexibleSpace();
			long actorId = item.Actor?.data == null ? 0L : ((BaseSystemData)item.Actor.data).id;
			GUI.enabled = CanFocusActor(actorId);
			if (GUILayout.Button("照见其人", GUILayout.Width(96f), GUILayout.Height(32f)))
			{
				_focusMessage = TryFocusActor(actorId) ? "已定位到该修士。" : "该修士暂时无法定位。";
			}
			GUI.enabled = true;
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}
		if (_rankRows.Count > visible)
		{
			DrawEmptyCard("其余 " + (_rankRows.Count - visible) + " 人未在本页展开，可切换榜目缩小范围。", "#777777");
		}
	}

	private void DrawRankMetricPicker()
	{
		IReadOnlyList<XjRankSortKeyDef> definitions = XjRankSortSystem.SortKeyDefs;
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#9CD7FF");
		GUILayout.BeginHorizontal();
		for (int i = 0; i < definitions.Count; i++)
		{
			XjRankSortKeyDef definition = definitions[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_rankMetricId, definition.Id, StringComparison.Ordinal)
				? new Color(0.36f, 0.53f, 0.62f, 1f)
				: new Color(0.23f, 0.23f, 0.25f, 1f);
			if (GUILayout.Button(definition.Name, GUILayout.Height(34f)))
			{
				_rankMetricId = definition.Id;
				_rankCacheMetricId = string.Empty;
			}
			GUI.backgroundColor = old;
		}
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

	private void EnsureRankRows()
	{
		int year = 0;
		try { year = Math.Max(0, World.world?.map_stats?.year ?? 0); } catch (System.Exception xjCaught104) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Codex/XjCodexWindow.CompanionRecords.cs:104", xjCaught104); }
		int cultivatorCount = XjCultivatorCache.Count;
		if (_rankCacheYear == year
			&& _rankCacheCultivatorCount == cultivatorCount
			&& string.Equals(_rankCacheMetricId, _rankMetricId, StringComparison.Ordinal)) return;

		_rankRows.Clear();
		XjRankSortKeyDef definition = ResolveRankMetricDefinition();
		IReadOnlyList<XjRankItem> items = XjRankReadModel.Build();
		bool specialized = IsSpecializedRank(_rankMetricId);
		for (int i = 0; i < items.Count; i++)
		{
			XjRankItem item = items[i];
			if (item.Actor?.data == null) continue;
			float value = ResolveRankValue(item, definition);
			if (specialized && value <= 0f) continue;
			_rankRows.Add(new CodexRankRow
			{
				Item = item,
				Value = value,
				Display = ResolveRankDisplay(item, definition)
			});
		}
		_rankRows.Sort((left, right) =>
		{
			int value = right.Value.CompareTo(left.Value);
			if (value != 0) return value;
			int name = string.Compare(left.Item.Name, right.Item.Name, StringComparison.Ordinal);
			if (name != 0) return name;
			long leftId = left.Item.Actor?.data == null ? long.MaxValue : ((BaseSystemData)left.Item.Actor.data).id;
			long rightId = right.Item.Actor?.data == null ? long.MaxValue : ((BaseSystemData)right.Item.Actor.data).id;
			return leftId.CompareTo(rightId);
		});
		_rankCacheYear = year;
		_rankCacheCultivatorCount = cultivatorCount;
		_rankCacheMetricId = _rankMetricId;
	}

	private XjRankSortKeyDef ResolveRankMetricDefinition()
	{
		XjRankSortSystem.Init();
		for (int i = 0; i < XjRankSortSystem.SortKeyDefs.Count; i++)
		{
			XjRankSortKeyDef definition = XjRankSortSystem.SortKeyDefs[i];
			if (string.Equals(definition.Id, _rankMetricId, StringComparison.Ordinal)) return definition;
		}
		return XjRankSortSystem.SortKeyDefs.Count > 0 ? XjRankSortSystem.SortKeyDefs[0] : null;
	}

	private static float ResolveRankValue(in XjRankItem item, XjRankSortKeyDef definition)
	{
		if (definition == null) return (float)item.Power;
		switch (definition.Id)
		{
			case "power": return item.Power >= float.MaxValue ? float.MaxValue : (float)item.Power;
			case "zhenyuan": return item.ZhenYuan;
			case "aptitude": return item.XjZz;
			case "age": return item.Age;
			default:
				try { return definition.GetValue?.Invoke(item.Actor) ?? 0f; }
				catch { return 0f; }
		}
	}

	private static string ResolveRankDisplay(in XjRankItem item, XjRankSortKeyDef definition)
	{
		if (definition == null || string.Equals(definition.Id, "power", StringComparison.Ordinal))
			return XjRankSortSystem.FormatNum((float)item.Power, "战力");
		if (string.Equals(definition.Id, "zhenyuan", StringComparison.Ordinal))
			return XjRankSortSystem.FormatNum(item.ZhenYuan, "真元");
		if (string.Equals(definition.Id, "aptitude", StringComparison.Ordinal))
			return XjRankSortSystem.GetAptitudeName(item.XjZz) + "资质";
		if (string.Equals(definition.Id, "age", StringComparison.Ordinal)) return item.Age + "岁";
		try { return definition.GetDisplay?.Invoke(item.Actor) ?? "未载"; }
		catch { return "未载"; }
	}

	private static bool IsSpecializedRank(string metricId)
	{
		return string.Equals(metricId, "alchemy", StringComparison.Ordinal)
			|| string.Equals(metricId, "artifact", StringComparison.Ordinal)
			|| string.Equals(metricId, "talisman", StringComparison.Ordinal)
			|| string.Equals(metricId, "formation", StringComparison.Ordinal)
			|| string.Equals(metricId, "sword_intent", StringComparison.Ordinal);
	}

	private static string ResolveRankColor(int rank)
	{
		if (rank == 0) return "#FFD36A";
		if (rank == 1) return "#D7D7D7";
		if (rank == 2) return "#D39A62";
		return "#A8A8A8";
	}

	private void DrawDaoTuArchivePage()
	{
		DrawPageHeader("道途总览", "一卷分录当世道势与相生相制；先观道途兴衰、位序和权柄，再查各道之间的克制关系。");
		GUILayout.BeginHorizontal(GUI.skin.box);
		for (int i = 0; i < DaoTuArchiveViews.Length; i++)
		{
			string view = DaoTuArchiveViews[i];
			GUI.backgroundColor = string.Equals(_daoTuArchiveView, view, StringComparison.Ordinal)
				? new Color(0.32f, 0.50f, 0.48f, 1f)
				: new Color(0.24f, 0.25f, 0.28f, 1f);
			if (GUILayout.Button(view, GUILayout.Height(38f)))
			{
				_daoTuArchiveView = view;
				_scrollPosition = Vector2.zero;
			}
		}
		GUI.backgroundColor = Color.white;
		GUILayout.EndHorizontal();
		GUILayout.Space(7f);
		if (string.Equals(_daoTuArchiveView, "克制关系", StringComparison.Ordinal))
			DrawDaoTuCounterPage();
		else
			DrawDaoTuOverviewPage();
	}

	private void DrawDaoTuCounterPage()
	{
		DrawDaoTuCounterFilterBar();
		List<string> daoTus = new List<string>(XjDaoTuCounterState.GetKnownDaoTus());
		daoTus.RemoveAll(daoTu => !MatchesDaoTuCounterFilter(daoTu));
		daoTus.Sort(CompareDaoTuByCategory);
		if (daoTus.Count == 0)
		{
			DrawEmptyCard("暂无道途克制记录。", "#777777");
			return;
		}
		for (int i = 0; i < daoTus.Count; i++)
		{
			string daoTu = daoTus[i];
			IReadOnlyList<string> targets = XjDaoTuCounterState.GetEffectiveCounterTargets(daoTu);
			IReadOnlyList<string> sources = XjDaoTuCounterState.GetEffectiveCounteredBy(daoTu);
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#A7E08A");
			GUILayout.Label("<b><size=20><color=#A7E08A>" + Rich(daoTu) + "</color></size></b>");
			GUILayout.BeginHorizontal();
			DrawMiniStat("当前克制", BuildSortedDaoTuList(targets), "#FF8877", GUILayout.ExpandWidth(true));
			DrawMiniStat("当前受制", BuildSortedDaoTuList(sources), "#9CD7FF", GUILayout.ExpandWidth(true));
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}
	}

	private static string BuildSortedDaoTuList(IReadOnlyList<string> source)
	{
		if (source == null || source.Count == 0) return "无";
		List<string> values = new List<string>(source);
		values.RemoveAll(string.IsNullOrWhiteSpace);
		if (values.Count == 0) return "无";
		values.Sort(CompareDaoTuByCategory);
		List<string> rows = new List<string>();
		for (int i = 0; i < values.Count; i += 4)
		{
			List<string> chunk = new List<string>(4);
			for (int col = 0; col < 4 && i + col < values.Count; col++)
			{
				chunk.Add(values[i + col].Trim());
			}
			rows.Add(string.Join("、", chunk));
		}
		return string.Join("\n", rows);
	}

	private void DrawDaoTuCounterFilterBar()
	{
		_daoTuCounterFilter = DrawDaoTuCategoryFilterBar(_daoTuCounterFilter, "#6FAE9D", new Color(0.36f, 0.53f, 0.43f, 1f));
	}

	private string DrawDaoTuCategoryFilterBar(string selected, string stripeColor, Color selectedColor)
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(stripeColor);
		GUILayout.BeginHorizontal();
		for (int index = 0; index < DaoTuCounterFilters.Length; index++)
		{
			string filter = DaoTuCounterFilters[index];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(selected, filter, StringComparison.Ordinal)
				? selectedColor
				: new Color(0.23f, 0.23f, 0.25f, 1f);
			float width = string.Equals(filter, "十二炁", StringComparison.Ordinal) ? 84f : 58f;
			if (GUILayout.Button(filter, GUILayout.Width(width), GUILayout.Height(32f)))
			{
				selected = filter;
			}
			GUI.backgroundColor = old;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		return selected;
	}

	private bool MatchesDaoTuCounterFilter(string daoTu)
	{
		return MatchesDaoTuCategoryFilter(daoTu, _daoTuCounterFilter);
	}

	private static bool MatchesDaoTuCategoryFilter(string daoTu, string filter)
	{
		if (string.Equals(filter, "全部", StringComparison.Ordinal)) return true;
		if (string.Equals(filter, "并古", StringComparison.Ordinal)) return XjDaoTuCatalog.IsBingGu(daoTu);
		if (!XjDaoTuCatalog.TryResolveRootId(daoTu, out string rootId)) return false;
		return filter switch
		{
			"三阴" => string.Equals(rootId, XjDaoTuRootIds.SanYin, StringComparison.Ordinal),
			"三阳" => string.Equals(rootId, XjDaoTuRootIds.SanYang, StringComparison.Ordinal),
			"三雷" => string.Equals(rootId, XjDaoTuRootIds.SanLei, StringComparison.Ordinal),
			"金德" => string.Equals(rootId, XjDaoTuRootIds.JinDe, StringComparison.Ordinal)
				|| string.Equals(rootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal),
			"木德" => string.Equals(rootId, XjDaoTuRootIds.MuDe, StringComparison.Ordinal),
			"水德" => string.Equals(rootId, XjDaoTuRootIds.ShuiDe, StringComparison.Ordinal),
			"火德" => string.Equals(rootId, XjDaoTuRootIds.HuoDe, StringComparison.Ordinal),
			"土德" => string.Equals(rootId, XjDaoTuRootIds.TuDe, StringComparison.Ordinal),
			"十二炁" => string.Equals(rootId, XjDaoTuRootIds.ShiErQi, StringComparison.Ordinal),
			_ => false
		};
	}

	private static int CompareDaoTuByCategory(string left, string right)
	{
		int leftOrder = ResolveDaoTuCategoryOrder(left);
		int rightOrder = ResolveDaoTuCategoryOrder(right);
		if (leftOrder != rightOrder) return leftOrder.CompareTo(rightOrder);
		return string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
	}

	private static int ResolveDaoTuCategoryOrder(string daoTu)
	{
		if (XjDaoTuCatalog.IsBingGu(daoTu)) return 9;
		if (!XjDaoTuCatalog.TryResolveRootId(daoTu, out string rootId)) return 99;
		if (string.Equals(rootId, XjDaoTuRootIds.SanYin, StringComparison.Ordinal)) return 0;
		if (string.Equals(rootId, XjDaoTuRootIds.SanYang, StringComparison.Ordinal)) return 1;
		if (string.Equals(rootId, XjDaoTuRootIds.SanLei, StringComparison.Ordinal)) return 2;
		if (string.Equals(rootId, XjDaoTuRootIds.JinDe, StringComparison.Ordinal)
			|| string.Equals(rootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal)) return 3;
		if (string.Equals(rootId, XjDaoTuRootIds.MuDe, StringComparison.Ordinal)) return 4;
		if (string.Equals(rootId, XjDaoTuRootIds.ShuiDe, StringComparison.Ordinal)) return 5;
		if (string.Equals(rootId, XjDaoTuRootIds.HuoDe, StringComparison.Ordinal)) return 6;
		if (string.Equals(rootId, XjDaoTuRootIds.TuDe, StringComparison.Ordinal)) return 7;
		if (string.Equals(rootId, XjDaoTuRootIds.ShiErQi, StringComparison.Ordinal)) return 8;
		return 98;
	}

	private void DrawGuoWeiAuthorityPage()
	{
		DrawPageHeader("果位权柄", "分卷查看各道果余闰位序与真实持柄情况，并按时间追溯证位、离位、执柄、失柄和权柄流转。");
		DrawGuoWeiAuthorityViewPicker();
		DrawAuthorityFilterBar();
		int year = 0;
		try { year = Math.Max(0, World.world?.map_stats?.year ?? 0); } catch (System.Exception xjCaught226) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Codex/XjCodexWindow.CompanionRecords.cs:DrawGuoWeiAuthorityPage", xjCaught226); }
		int authorityRevision = XjGuoWeiAuthorityCodexReadModel.GetRevisionToken();
		if (_authorityCacheYear != year || _authorityReadModelRevision != authorityRevision)
		{
			_authorityRows = XjGuoWeiQuanBingWindow.BuildViews();
			_authorityReadModel = XjGuoWeiAuthorityCodexReadModel.Build(year);
			_authorityCacheYear = year;
			_authorityReadModelRevision = authorityRevision;
		}

		if (string.Equals(_guoWeiAuthorityView, "持有权柄", StringComparison.Ordinal))
		{
			DrawHeldAuthorityOverview();
			DrawAuthorityChangeTimeline(_authorityReadModel, authorityView: true);
		}
		else
		{
			DrawPositionCountOverview(_authorityReadModel);
			DrawDerivedPositionDigest(_authorityReadModel);
			DrawAuthorityChangeTimeline(_authorityReadModel, authorityView: false);
		}
	}

	private void DrawGuoWeiAuthorityViewPicker()
	{
		GUILayout.BeginHorizontal(GUI.skin.box);
		for (int i = 0; i < GuoWeiAuthorityViews.Length; i++)
		{
			string view = GuoWeiAuthorityViews[i];
			GUI.backgroundColor = string.Equals(_guoWeiAuthorityView, view, StringComparison.Ordinal)
				? new Color(0.48f, 0.42f, 0.26f, 1f)
				: new Color(0.24f, 0.25f, 0.28f, 1f);
			if (GUILayout.Button(view, GUILayout.Height(36f)))
			{
				_guoWeiAuthorityView = view;
				_scrollPosition = Vector2.zero;
			}
		}
		GUI.backgroundColor = Color.white;
		GUILayout.EndHorizontal();
	}

	private void DrawHeldAuthorityOverview()
	{
		DrawSectionTitle("当前权柄与流转", "#B7A7FF");
		List<XjGuoWeiQuanBingWindow.DaoTuOverview> displayRows = new List<XjGuoWeiQuanBingWindow.DaoTuOverview>(_authorityRows);
		displayRows.RemoveAll(row => row == null || !MatchesDaoTuCategoryFilter(row.DaoTu, _authorityFilter));
		displayRows.Sort((left, right) => CompareDaoTuByCategory(left.DaoTu, right.DaoTu));
		if (displayRows.Count == 0)
		{
			DrawEmptyCard("暂无持柄记录。", "#777777");
			return;
		}
		int visible = Math.Min(MaxRenderedCardsPerPage, displayRows.Count);
		for (int i = 0; i < visible; i++)
		{
			XjGuoWeiQuanBingWindow.DaoTuOverview row = displayRows[i];
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#B7A7FF");
			GUILayout.Label("<b><color=#B7A7FF>" + Rich(XjDisplayNameSanitizer.GameTerm(row.DaoTu, "道途未定")) + "</color></b>");
			IReadOnlyList<string> compactLines = SelectCompactAuthorityLines(row.DetailLines);
			for (int line = 0; line < compactLines.Count; line++) DrawCompactAuthorityLine(compactLines[line]);
			GUILayout.EndVertical();
		}
		DrawListLimitNotice(displayRows.Count, visible);
	}

	private void DrawPositionCountOverview(XjGuoWeiAuthorityCodexSnapshot snapshot)
	{
		IReadOnlyList<XjDaoTuPositionCountView> source = snapshot?.PositionCounts;
		if (source == null || source.Count == 0) return;
		List<XjDaoTuPositionCountView> rows = new List<XjDaoTuPositionCountView>();
		int activeZheng = 0;
		int activeYu = 0;
		int activeRun = 0;
		for (int i = 0; i < source.Count; i++)
		{
			XjDaoTuPositionCountView row = source[i];
			if (row == null || !MatchesDaoTuCategoryFilter(row.DaoTu, _authorityFilter)) continue;
			rows.Add(row);
			activeZheng += row.ZhengActive;
			activeYu += row.YuActive;
			activeRun += row.RunActive;
		}
		rows.Sort((left, right) => CompareDaoTuByCategory(left.DaoTu, right.DaoTu));
		DrawSectionTitle("位序总览", "#F0CC75");
		GUILayout.BeginHorizontal();
		DrawOverviewPill("果位在位", activeZheng.ToString(), "#FFD37A", GUILayout.Width(130f));
		DrawOverviewPill("余位在位", activeYu.ToString(), "#A7E08A", GUILayout.Width(130f));
		DrawOverviewPill("闰位在位", activeRun.ToString(), "#D2B5FF", GUILayout.Width(130f));
		DrawOverviewPill("道途数", rows.Count.ToString(), "#9CD7FF", GUILayout.Width(120f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		int visible = Math.Min(MaxRenderedCardsPerPage, rows.Count);
		for (int i = 0; i < visible; i++)
		{
			XjDaoTuPositionCountView row = rows[i];
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#F0CC75");
			GUILayout.Label("<b><color=#F0CC75>" + Rich(XjDisplayNameSanitizer.GameTerm(row.DaoTu, "道途未定")) + "</color></b>");
			GUILayout.BeginHorizontal();
			// 只展示真实在位人数。动态道势下降只影响后续补位，不会抹去仍在世的既有席位，
			// 因此不再用“在位/当下容量”制造 5/3 这类看似越界的表达。
			string zhengStatus = row.ZhengHidden > 0
				? "晦隐"
				: string.IsNullOrWhiteSpace(row.ZhengStatus)
					? row.ZhengActive > 0 ? "在位" : "空缺"
					: row.ZhengStatus;
			string authorityStatus = string.IsNullOrWhiteSpace(row.AuthorityHolderStatus)
				? row.ActiveAuthorityHolders.ToString()
				: row.AuthorityHolderStatus;
			DrawMiniStat("果", zhengStatus, row.ZhengHidden > 0 ? "#A7A1D8" : "#FFD37A", GUILayout.ExpandWidth(true));
			DrawMiniStat("余", "在位 " + row.YuActive, "#A7E08A", GUILayout.ExpandWidth(true));
			DrawMiniStat("闰", "在位 " + row.RunActive, "#D2B5FF", GUILayout.ExpandWidth(true));
			DrawMiniStat("执柄者", authorityStatus, "#9CD7FF", GUILayout.Width(105f));
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}
		DrawListLimitNotice(rows.Count, visible);
	}

	private void DrawAuthorityChangeTimeline(XjGuoWeiAuthorityCodexSnapshot snapshot, bool authorityView)
	{
		IReadOnlyList<XjGuoWeiAuthorityChangeView> source = snapshot?.Changes;
		if (source == null || source.Count == 0) return;
		DrawSectionTitle(authorityView ? "权柄变动" : "位序变动", "#FFB878");
		int shown = 0;
		for (int i = 0; i < source.Count && shown < 48; i++)
		{
			XjGuoWeiAuthorityChangeView change = source[i];
			if (change == null || !MatchesDaoTuCategoryFilter(change.DaoTu, _authorityFilter)
				|| !MatchesAuthoritySubview(change.Kind, authorityView)) continue;

			// 得失属于历史流水，不再让“一次夺柄/失柄”占一整张大卡。
			// 单行显示年份、类型和摘要；详情裁短后仍保留人物/流向信息。
			string color = ResolveAuthorityChangeColor(change.Kind);
			string daoTu = XjDisplayNameSanitizer.GameTerm(change.DaoTu, "道途未定");
			string detail = string.IsNullOrWhiteSpace(change.Detail)
				? string.Empty
				: NormalizeTimelineClause(TrimArchiveText(change.Detail, 72));
			string summary = "【" + daoTu + "】" + NormalizeTimelineClause(change.Title);
			if (!string.IsNullOrWhiteSpace(detail)) summary += "；" + detail;
			if (!summary.EndsWith("。", StringComparison.Ordinal)) summary += "。";
			GUILayout.BeginHorizontal(GUI.skin.box);
			GUILayout.Label("<b><color=" + color + ">" + XjChronology.FormatYear(change.Year) + "</color></b>", GUILayout.Width(120f), GUILayout.MinHeight(26f));
			GUILayout.Label("<color=" + color + ">" + Rich(XjDisplayNameSanitizer.GameTerm(change.Kind, "变动")) + "</color>", GUILayout.Width(66f));
			GUILayout.Label("<size=12><color=#CFCFCF>" + Rich(summary) + "</color></size>", GUILayout.ExpandWidth(true), GUILayout.MinHeight(26f));
			GUILayout.EndHorizontal();
			shown++;
		}
		if (shown == 0) DrawEmptyCard(authorityView ? "当前筛选下暂无权柄变动。" : "当前筛选下暂无位序变动。", "#777777");
	}

	private static string NormalizeTimelineClause(string raw)
	{
		string value = (raw ?? string.Empty).Trim();
		if (value.Length == 0) return string.Empty;
		while (value.Contains(" · ", StringComparison.Ordinal))
		{
			value = value.Replace(" · ", "；");
		}
		return value.Trim('；', ' ', '\t', '\r', '\n');
	}

	private static bool MatchesAuthoritySubview(string kind, bool authorityView)
	{
		if (authorityView)
		{
			return string.Equals(kind, "执柄", StringComparison.Ordinal)
				|| string.Equals(kind, "夺得", StringComparison.Ordinal)
				|| string.Equals(kind, "失柄", StringComparison.Ordinal)
				|| string.Equals(kind, "权柄异动", StringComparison.Ordinal);
		}
		return string.Equals(kind, "证位", StringComparison.Ordinal)
			|| string.Equals(kind, "离位", StringComparison.Ordinal)
			|| string.Equals(kind, "开位", StringComparison.Ordinal)
			|| string.Equals(kind, "承继", StringComparison.Ordinal);
	}

	private static string ResolveAuthorityChangeColor(string kind)
	{
		return kind switch
		{
			"证位" => "#FFD37A",
			"开位" => "#A7E08A",
			"承继" => "#9CD7FF",
			"执柄" => "#B7A7FF",
			"夺得" => "#74E8FF",
			"失柄" => "#FF9A88",
			"离位" => "#B8B8B8",
			_ => "#FFB878"
		};
	}

	private void DrawAuthorityFilterBar()
	{
		_authorityFilter = DrawDaoTuCategoryFilterBar(_authorityFilter, "#B7A7FF", new Color(0.45f, 0.38f, 0.64f, 1f));
	}

	private void DrawCompactAuthorityLine(string line)
	{
		string text = (line ?? string.Empty).Trim();
		if (text.Length == 0) return;
		const string holderPrefix = "当前持有：";
		const string seizedPrefix = "夺得权柄：";
		const string sealedPrefix = "果位封锁：";
		const string lostPrefix = "永久丢失：";
		const string authorityPrefix = "权柄：";
		const string authorityLorePrefix = "权柄释义：";
		if (text.StartsWith(authorityPrefix, StringComparison.Ordinal))
		{
			DrawAuthorityCompactGrid("本源权柄", text.Substring(authorityPrefix.Length).Trim(), "#B7A7FF", 6, 28);
			return;
		}
		if (text.StartsWith(authorityLorePrefix, StringComparison.Ordinal))
		{
			DrawAuthorityLoreGrid(text.Substring(authorityLorePrefix.Length).Trim());
			return;
		}
		if (text.StartsWith(holderPrefix, StringComparison.Ordinal))
		{
			DrawAuthorityCompactGrid("持有人", text.Substring(holderPrefix.Length).Trim(), "#FFD37A", 3, 48);
			return;
		}
		if (text.StartsWith(seizedPrefix, StringComparison.Ordinal))
		{
			DrawAuthorityCompactGrid("夺得", text.Substring(seizedPrefix.Length).Trim(), "#74E8FF", 3, 34);
			return;
		}
		if (text.StartsWith(sealedPrefix, StringComparison.Ordinal))
		{
			DrawAuthorityCompactGrid("封锁", text.Substring(sealedPrefix.Length).Trim(), "#FFAA66", 3, 42);
			return;
		}
		if (text.StartsWith(lostPrefix, StringComparison.Ordinal))
		{
			DrawAuthorityCompactGrid("丢失", text.Substring(lostPrefix.Length).Trim(), "#FF8877", 3, 36);
			return;
		}
		DrawAuthorityCompactCell(TrimArchiveText(text, 86), "#B7A7FF", 34f);
	}

	private void DrawAuthorityCompactGrid(string label, string raw, string color, int columns, int trimChars)
	{
		string[] values = (raw ?? string.Empty).Split(new[] { '、', ',', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
		if (values.Length == 0) return;
		GUILayout.Label("<size=13><color=grey>" + Rich(label) + "</color></size>");
		int resolvedColumns = Math.Max(1, columns);
		for (int row = 0; row * resolvedColumns < values.Length; row++)
		{
			GUILayout.BeginHorizontal();
			for (int col = 0; col < resolvedColumns; col++)
			{
				int index = row * resolvedColumns + col;
				if (index >= values.Length)
				{
					GUILayout.Space(1f);
					continue;
				}
				string value = TrimArchiveText(values[index].Trim(), trimChars);
				DrawAuthorityCompactCell(value, color, 38f);
			}
			GUILayout.EndHorizontal();
		}
	}

	private void DrawAuthorityLoreGrid(string raw)
	{
		string[] values = (raw ?? string.Empty).Split(new[] { '；' }, StringSplitOptions.RemoveEmptyEntries);
		if (values.Length == 0) return;
		GUILayout.Label("<size=13><color=grey>权柄释义</color></size>");
		const int columns = 2;
		for (int row = 0; row * columns < values.Length; row++)
		{
			GUILayout.BeginHorizontal();
			for (int col = 0; col < columns; col++)
			{
				int index = row * columns + col;
				if (index >= values.Length)
				{
					GUILayout.Space(1f);
					continue;
				}
				DrawAuthorityLoreCell(values[index].Trim());
			}
			GUILayout.EndHorizontal();
		}
	}

	private void DrawAuthorityLoreCell(string value)
	{
		GUIStyle loreStyle = new GUIStyle(GUI.skin.label)
		{
			richText = true,
			fontSize = 13,
			wordWrap = true,
			alignment = TextAnchor.UpperLeft,
			clipping = TextClipping.Overflow,
			padding = new RectOffset(8, 8, 6, 6),
			margin = new RectOffset(0, 0, 0, 0)
		};
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.MinHeight(78f), GUILayout.ExpandWidth(true));
		GUILayout.Label("<color=#D9CBFF>" + Rich(value) + "</color>", loreStyle,
			GUILayout.MinHeight(64f), GUILayout.ExpandWidth(true));
		GUILayout.EndVertical();
	}

	/// <summary>
	/// 紧凑权柄格只让 GUI.skin.box 负责背景；正文必须由正常 Label 单独绘制。
	/// WorldBox 当前皮肤的 box 文本状态并不稳定，直接把富文本交给 box 样式会出现
	/// “卡片存在但文字不可见”的情况。这里保持三列紧凑布局，同时把字体与高度调回
	/// 玩家可读范围，避免中文在低行高下出现“只露半截”的裁切。
	/// </summary>
	private void DrawAuthorityCompactCell(string value, string color, float minHeight)
	{
		GUIStyle cellTextStyle = new GUIStyle(GUI.skin.label)
		{
			richText = true,
			fontSize = 15,
			wordWrap = false,
			alignment = TextAnchor.MiddleLeft,
			clipping = TextClipping.Clip,
			padding = new RectOffset(8, 8, 6, 6),
			margin = new RectOffset(0, 0, 0, 0)
		};
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.MinHeight(minHeight), GUILayout.ExpandWidth(true));
		GUILayout.Label("<color=" + color + ">" + Rich(value) + "</color>", cellTextStyle,
			GUILayout.MinHeight(28f), GUILayout.ExpandWidth(true));
		GUILayout.EndVertical();
	}

	private static IReadOnlyList<string> SelectCompactAuthorityLines(IReadOnlyList<string> source)
	{
		List<string> result = new List<string>(8);
		if (source == null) return result;
		for (int i = 0; i < source.Count && result.Count < 8; i++)
		{
			string line = (source[i] ?? string.Empty).Trim();
			if (line.Length == 0) continue;
			if (line.StartsWith("权柄：", StringComparison.Ordinal)
				|| line.StartsWith("权柄释义：", StringComparison.Ordinal)
				|| line.StartsWith("当前持有：", StringComparison.Ordinal)
				|| line.StartsWith("夺得权柄：", StringComparison.Ordinal)
				|| line.StartsWith("果位封锁：", StringComparison.Ordinal)
				|| line.StartsWith("释土异变：", StringComparison.Ordinal)
				|| line.StartsWith("永久丢失：", StringComparison.Ordinal))
			{
				result.Add(line);
			}
		}
		return result;
	}

	private void DrawDerivedPositionDigest(XjGuoWeiAuthorityCodexSnapshot snapshot)
	{
		IReadOnlyList<XjDerivedPositionArchiveRecord> derived = snapshot?.DerivedPositions;
		if (derived == null || derived.Count == 0) return;

		DrawSectionTitle("近世余闰", "#D2B5FF");
		int visible = Math.Min(12, derived.Count);
		for (int i = 0; i < visible; i++)
		{
			XjDerivedPositionArchiveRecord record = derived[i];
			string kind = string.Equals(record.PositionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal) ? "余位" : "闰位";
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(string.Equals(kind, "余位", StringComparison.Ordinal) ? "#A7E08A" : "#D2B5FF");
			GUILayout.Label("<b>" + Rich(XjGuoWeiCalculator.GetDisplayGuoWeiName(record.PositionId)) + "</b>　<color=grey>" + Rich(kind)
				+ (record.FoundedYear > 0 ? "（开辟于" + XjChronology.FormatYear(record.FoundedYear) + "）" : string.Empty) + "</color>");
			string derivedAuthority = Empty(record.DerivedAuthority, "派生权柄未载");
			if (!string.IsNullOrWhiteSpace(record.SecondaryDerivedAuthority))
			{
				derivedAuthority += "、" + record.SecondaryDerivedAuthority.Trim();
			}
			GUILayout.Label("<size=14>金性：" + Rich(Empty(record.JinXingName, "未名")) + "；派生权柄："
				+ Rich(TrimArchiveText(derivedAuthority, 44)) + "</size>");
			XjDaoTaiPositionBindingArchiveRecord activeBinding = null;
			IReadOnlyList<XjDaoTaiPositionBindingArchiveRecord> bindings = snapshot.DaoTaiBindings;
			if (bindings != null)
			{
				for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
				{
					XjDaoTaiPositionBindingArchiveRecord candidateBinding = bindings[bindingIndex];
					if (candidateBinding != null
						&& (string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(candidateBinding.PrimaryPositionId), record.PositionId, StringComparison.Ordinal)
							|| string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(candidateBinding.SecondaryPositionId), record.PositionId, StringComparison.Ordinal)))
					{
						activeBinding = candidateBinding;
						break;
					}
				}
			}
			if (activeBinding != null)
			{
				string bindingTitle = XjDaoTaiDualPositionSystem.GetBindingTitle(activeBinding);
				string holderName = "一位道胎";
				if (activeBinding.ActorId > 0L
					&& XjActorRegistry.ResolveKnownOrWorld(activeBinding.ActorId, out Actor holder)
					&& holder?.data != null)
				{
					holderName = holder.getName();
				}
				GUILayout.Label("<color=#B7A7FF><size=13>当前合持：" + Rich(bindingTitle) + "；持位者：" + Rich(holderName) + "</size></color>");
			}
			if (!string.IsNullOrWhiteSpace(record.LastHolderDisplay))
			{
				GUILayout.Label("<color=grey><size=13>上任持位者：" + Rich(record.LastHolderDisplay) + "</size></color>");
			}
			string shiLockStatus = XjShiFruitPositionLockSystem.BuildStatusLine(record.PositionId);
			if (!string.IsNullOrWhiteSpace(shiLockStatus))
			{
				GUILayout.Label("<color=#D9C78A><size=13>" + Rich(shiLockStatus) + "</size></color>");
			}
			GUILayout.EndVertical();
		}
		DrawListLimitNotice(derived.Count, visible);
	}

	private void DrawTreasureRecordPage()
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#FFD37A");
		GUILayout.BeginHorizontal();
		for (int i = 0; i < TreasureFilters.Length; i++)
		{
			string filter = TreasureFilters[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_treasureFilter, filter, StringComparison.Ordinal)
				? new Color(0.58f, 0.46f, 0.24f, 1f)
				: new Color(0.23f, 0.23f, 0.25f, 1f);
			if (GUILayout.Button(filter, GUILayout.MinWidth(88f), GUILayout.Height(34f)))
			{
				_treasureFilter = filter;
				_treasureCacheFilter = string.Empty;
			}
			GUI.backgroundColor = old;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		EnsureTreasureRows();
		if (_treasureRows.Count == 0)
		{
			DrawEmptyCard("当前筛选下暂无器物。", "#777777");
			return;
		}
		int visible = Math.Min(MaxRenderedCardsPerPage, _treasureRows.Count);
		for (int i = 0; i < visible; i++)
		{
			XjFaBaoRankEntry entry = _treasureRows[i];
			string tierColor = entry.IsJinDan ? "#FFD36A" : entry.IsFaQi ? "#69B7FF" : "#C895FF";
			GUILayout.BeginHorizontal(GUI.skin.box);
			GUILayout.Label("<b><color=" + ResolveRankColor(i) + ">#" + (i + 1) + "</color></b>", GUILayout.Width(58f));
			GUILayout.Label("<b><color=" + tierColor + ">" + Rich(Empty(entry.Name, "无名器物")) + "</color></b>", GUILayout.Width(210f));
			GUILayout.Label(Rich(Empty(entry.ClassName, "品级未载")), GUILayout.Width(120f));
			GUILayout.Label("用途：" + Rich(XjDisplayNameSanitizer.GameTerm(entry.Role, "附属")) + "；道途：" + Rich(XjDisplayNameSanitizer.GameTerm(entry.DaoTu, "未定")), GUILayout.Width(210f));
			GUILayout.Label("<color=#9CD7FF>威能 " + entry.Power + "；词条 " + entry.AffixCount + "</color>");
			GUILayout.FlexibleSpace();
			GUILayout.Label("<color=grey>主人：" + Rich(Empty(entry.OwnerName, "无主")) + "</color>", GUILayout.Width(170f));
			GUILayout.EndHorizontal();
		}
	}

	private void EnsureTreasureRows()
	{
		int year = 0;
		try { year = Math.Max(0, World.world?.map_stats?.year ?? 0); } catch (System.Exception xjCaught304) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Codex/XjCodexWindow.CompanionRecords.cs:304", xjCaught304); }
		if (_treasureCacheYear == year
			&& string.Equals(_treasureCacheFilter, _treasureFilter, StringComparison.Ordinal)) return;
		_treasureRows = XjShenBingReadModel.BuildRanking(_treasureFilter);
		_treasureCacheYear = year;
		_treasureCacheFilter = _treasureFilter;
	}

	private void ReleaseCompanionRecordCaches()
	{
		_rankRows.Clear();
		_rankCacheYear = -1;
		_rankCacheCultivatorCount = -1;
		_rankCacheMetricId = string.Empty;
		_authorityRows.Clear();
		_authorityCacheYear = -1;
		_authorityReadModelRevision = int.MinValue;
		_authorityReadModel = new XjGuoWeiAuthorityCodexSnapshot();
		_treasureRows.Clear();
		_treasureCacheYear = -1;
		_treasureCacheFilter = string.Empty;
	}
}
