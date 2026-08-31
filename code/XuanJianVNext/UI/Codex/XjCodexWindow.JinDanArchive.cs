using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private int _highRealmRosterView;

	private void DrawJinDanArchivePage(XjCodexSnapshot snapshot)
	{
		DrawPageHeader("高境名录", "修士与妖属大圣分卷并列；两者都保留境界、道途与历史记录。");
		_highRealmRosterView = GUILayout.Toolbar(_highRealmRosterView, new[] { "修士名录", "妖属大圣" });
		GUILayout.Space(8f);
		if (_highRealmRosterView == 1)
		{
			DrawYaoShuGreatSagePage();
			return;
		}
		List<XjCodexJinDanItem> items = BuildJinDanDisplayItems(snapshot);
		DrawJinDanSummary(snapshot, items.Count);
		DrawJinDanFilters(snapshot);
		items = BuildJinDanDisplayItems(snapshot);
		EnsureSelectedJinDan(items);

		if (items.Count == 0)
		{
			DrawEmptyCard("当前筛选下没有符合条件的修士记录。", "#777777");
			return;
		}

		if (IsArchiveStackedLayout())
		{
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#D6BE86");
			GUILayout.Label("<b>修士名录</b>　<color=grey>" + items.Count + "人（含史录）</color>");
			_jinDanListScrollPosition = GUILayout.BeginScrollView(_jinDanListScrollPosition, false, true, GUILayout.Height(Mathf.Clamp(_windowRect.height * 0.30f, 250f, 430f)));
			DrawJinDanRoster(items, false);
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			XjCodexJinDanItem selected = FindSelectedJinDan(items);
			if (selected != null)
			{
				GUILayout.Space(8f);
				DrawJinDanDetail(selected);
			}
			return;
		}

		float panelHeight = Mathf.Clamp(_windowRect.height - 610f, 300f, 760f);
		GUILayout.BeginHorizontal();
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(Mathf.Clamp(ContentWidth * 0.29f, 340f, 475f)));
		DrawCardStripe("#D6BE86");
		GUILayout.Label("<b>修士名录</b>　<color=grey>" + items.Count + "人</color>");
		_jinDanListScrollPosition = GUILayout.BeginScrollView(_jinDanListScrollPosition, false, true, GUILayout.Height(panelHeight));
		DrawJinDanRoster(items, true);
		GUILayout.EndScrollView();
		GUILayout.EndVertical();

		GUILayout.BeginVertical(GUI.skin.box);
		XjCodexJinDanItem selectedItem = FindSelectedJinDan(items);
		if (selectedItem != null) DrawJinDanDetail(selectedItem);
		GUILayout.EndVertical();
		GUILayout.EndHorizontal();
	}

	private void DrawJinDanSummary(XjCodexSnapshot snapshot, int filteredCount)
	{
		int alertCount = Math.Max(0, snapshot.ActiveYinSiMissionCount);
		int liveCount = 0;
		if (snapshot.JinDan != null)
		{
			for (int i = 0; i < snapshot.JinDan.Count; i++)
			{
				XjCodexJinDanItem item = snapshot.JinDan[i];
				if (item != null && !item.IsHistorical) liveCount++;
			}
		}
		int columns = ContentWidth < 760f ? 1 : 3;
		float width = Mathf.Max(170f, (ContentWidth - 70f) / columns);
		for (int i = 0; i < 3; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			switch (i)
			{
				case 0:
					DrawOverviewPill("在世修士", liveCount.ToString(), "#FFD37A", GUILayout.Width(width), GUILayout.Height(68f));
					break;
				case 1:
					DrawOverviewPill("卷内（含史录）", filteredCount.ToString(), "#9CD7FF", GUILayout.Width(width), GUILayout.Height(68f));
					break;
				case 2:
					DrawOverviewPill("阴司追索", alertCount.ToString(), alertCount > 0 ? "#FF6666" : "#888888", GUILayout.Width(width), GUILayout.Height(68f));
					break;
			}
			if (i % columns == columns - 1 || i == 2)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		if (_jinDanSectFilter > 0L || _jinDanActorFilter > 0L)
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label("<color=grey>定向：</color>", GUILayout.Width(55f));
			if (_jinDanSectFilter > 0L) DrawTag(ResolveJinDanSectFilterName(snapshot), "#B7A7FF");
			if (_jinDanActorFilter > 0L) DrawTag(ResolveJinDanActorFilterName(snapshot), "#FFAA66");
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
	}


	private void DrawJinDanFilters(XjCodexSnapshot snapshot)
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#8FA9C7");
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>卷内检索</b>", GUILayout.Width(90f));
		string nextSearch = GUILayout.TextField(_jinDanSearchText ?? string.Empty, GUILayout.MinWidth(220f), GUILayout.Height(34f));
		if (!string.Equals(nextSearch, _jinDanSearchText, StringComparison.Ordinal))
		{
			_jinDanSearchText = nextSearch;
			InvalidateJinDanSelection();
		}
		if (GUILayout.Button("清除筛选", GUILayout.Width(105f), GUILayout.Height(34f))) ResetJinDanFilters();
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		DrawJinDanChoiceRow("修法", JinDanPathFilters, _jinDanPathFilter, value =>
		{
			_jinDanPathFilter = value;
			InvalidateJinDanSelection();
		});
		DrawJinDanChoiceRow("身份", JinDanAchievementFilters, _jinDanAchievementFilter, value =>
		{
			_jinDanAchievementFilter = value;
			InvalidateJinDanSelection();
		});
		DrawJinDanChoiceRow("阴司", JinDanYinSiFilters, _jinDanYinSiFilter, value =>
		{
			_jinDanYinSiFilter = value;
			InvalidateJinDanSelection();
		});

		if (_jinDanSectFilter > 0L || _jinDanActorFilter > 0L)
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label("<color=grey>来自其他卷宗的定向查看：</color>");
			if (_jinDanSectFilter > 0L) GUILayout.Label("<color=#B7A7FF>" + Rich(ResolveJinDanSectFilterName(snapshot)) + "</color>");
			if (_jinDanActorFilter > 0L) GUILayout.Label("<color=#FFAA66>" + Rich(ResolveJinDanActorFilterName(snapshot)) + "</color>");
			if (GUILayout.Button("解除定向", GUILayout.Width(105f), GUILayout.Height(32f)))
			{
				_jinDanSectFilter = 0L;
				_jinDanActorFilter = 0L;
				InvalidateJinDanSelection();
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
		GUILayout.EndVertical();
	}

	private void DrawJinDanChoiceRow(string label, string[] options, string selected, Action<string> select)
	{
		if (options == null || select == null) return;
		int columns = ContentWidth < 920f ? 3 : options.Length;
		for (int i = 0; i < options.Length; i++)
		{
			if (i % columns == 0)
			{
				GUILayout.BeginHorizontal();
				GUILayout.Label(i == 0 ? "<b>" + Rich(label) + "</b>" : string.Empty, GUILayout.Width(70f));
			}
			string option = options[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(option, selected, StringComparison.Ordinal)
				? new Color(0.40f, 0.44f, 0.53f)
				: new Color(0.23f, 0.23f, 0.23f);
			if (GUILayout.Button(option, GUILayout.Height(31f))) select(option);
			GUI.backgroundColor = old;
			if (i % columns == columns - 1 || i == options.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private bool MatchesJinDanFilters(XjCodexJinDanItem item)
	{
		if (item == null) return false;
		if (_jinDanSectFilter > 0L && item.SectId != _jinDanSectFilter) return false;
		if (_jinDanActorFilter > 0L
			&& item.ActorId != _jinDanActorFilter
			&& item.FocusActorId != _jinDanActorFilter) return false;
		if (!string.Equals(_jinDanPathFilter, "全部修法", StringComparison.Ordinal)
			&& !string.Equals(item.CultivationPath, _jinDanPathFilter, StringComparison.Ordinal)) return false;
		if (!string.Equals(_jinDanAchievementFilter, "全部身份", StringComparison.Ordinal)
			&& !string.Equals(item.Achievement, _jinDanAchievementFilter, StringComparison.Ordinal)) return false;
		if (string.Equals(_jinDanYinSiFilter, "安稳", StringComparison.Ordinal)
			&& (item.YinSiKnown || item.ActiveMissionId > 0L)) return false;
		if (string.Equals(_jinDanYinSiFilter, "阴司知悉", StringComparison.Ordinal) && !item.YinSiKnown) return false;
		if (string.Equals(_jinDanYinSiFilter, "追索中", StringComparison.Ordinal) && item.ActiveMissionId <= 0L) return false;

		string query = (_jinDanSearchText ?? string.Empty).Trim();
		if (query.Length == 0) return true;
		return ContainsSearch(item.Name, query)
			|| ContainsSearch(item.SectName, query)
			|| ContainsSearch(item.FamilyName, query)
			|| ContainsSearch(item.DaoTu, query)
			|| ContainsSearch(item.Achievement, query)
			|| ContainsSearch(item.GuoWei, query)
			|| ContainsSearch(item.LocalAuthoritySummary, query)
			|| ContainsSearch(item.SeizedAuthoritySummary, query)
			|| ContainsSearch(item.GuoWeiImageSummary, query)
			|| ContainsSearch(item.ShenTongMutationSummary, query)
			|| ContainsSearch(item.LifeStatus, query)
			|| ContainsSearch(item.DeathReason, query);
	}

	private void DrawJinDanRoster(IReadOnlyList<XjCodexJinDanItem> items, bool compact)
	{
		int shown = items?.Count ?? 0;
		for (int i = 0; i < shown; i++)
		{
			XjCodexJinDanItem item = items[i];
			if (item == null) continue;
			string accent = ResolveJinDanAccent(item);
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = item.ActorId == _selectedJinDanActorId
				? ParseHexColor(accent, new Color(0.42f, 0.38f, 0.28f))
				: new Color(0.22f, 0.22f, 0.22f);
			string label = "<b>" + Rich(Empty(item.Name, "未名修士")) + "</b>"
				+ "\n<size=14>" + Rich(Empty(item.Achievement, "修士")) + " · "
				+ Rich(Empty(item.DaoTu, "道途未载")) + " · "
				+ Rich(Empty(item.FamilyName, Empty(item.SectName, "散修"))) + " · "
				+ "<color=" + ResolveLifeStatusColor(item.LifeStatus) + ">" + Rich(Empty(item.LifeStatus, "存世")) + "</color></size>";
			if (GUILayout.Button(label, GUILayout.Height(compact ? 62f : 68f)))
			{
				_selectedJinDanActorId = item.ActorId;
			}
			GUI.backgroundColor = old;
			GUILayout.Space(3f);
		}
	}

	private void DrawJinDanDetail(XjCodexJinDanItem item)
	{
		if (item == null) return;
		string accent = ResolveJinDanAccent(item);
		DrawArchiveBreadcrumb("修行道统", "修士名录", Empty(item.Name, "未名高境"), "人物", accent);

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(accent);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=23>" + Rich(Empty(item.Name, "未名修士")) + "</size></b>");
		GUILayout.FlexibleSpace();
		DrawTag(Empty(item.Achievement, "修士"), accent);
		DrawTag(Empty(item.LifeStatus, "存世"), ResolveLifeStatusColor(item.LifeStatus));
		GUILayout.EndHorizontal();
		bool reincarnated = string.Equals(item.LifeStatus, "转世", StringComparison.Ordinal) || item.IsReincarnationContinuation;
		string lifeTime = !reincarnated && item.IsHistorical && item.DeathYear > 0
			? "卒于" + XjChronology.FormatYear(item.DeathYear) + " · 终年" + item.Age + "岁"
			: item.Age + "岁";
		string chronology = item.ActivatedYear > 0 ? XjChronology.FormatYear(item.ActivatedYear) + "初入高境" : "入境年份未载";
		if (reincarnated && item.ReincarnationYear > 0) chronology += " · " + XjChronology.FormatYear(item.ReincarnationYear) + "转世";
		GUILayout.Label("<color=grey>" + chronology
			+ " · " + Rich(lifeTime) + " · " + Rich(Empty(item.CultivationPath, "修法未定")) + "</color>");
		GUILayout.EndVertical();

		DrawJinDanDetailStatGrid(item);
		if (!item.IsShi) DrawJinDanCultivationAuthorityDetail(item);

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#A7E08A");
		GUILayout.Label("世家：<color=#A7E08A>" + Rich(Empty(item.FamilyName, "无")) + "</color>　宗门：<color=#FFD37A>"
			+ Rich(Empty(item.SectName, "散修")) + "</color>");
		GUILayout.BeginHorizontal();
		if (item.FamilyId > 0L && GUILayout.Button("世家", GUILayout.Width(82f), GUILayout.Height(32f))) OpenFamilyArchive(item.FamilyId, "家族总览");
		if (item.SectId > 0L && GUILayout.Button("宗门", GUILayout.Width(82f), GUILayout.Height(32f))) OpenSectArchive(item.SectId, "山门总览");
		if (item.CanFocusActor) DrawActorFocusButton("定位", item.FocusActorId > 0L ? item.FocusActorId : item.ActorId, GUILayout.Width(82f), GUILayout.Height(32f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();

		DrawCultivatorLifeStatusCard(item);

		if (item.IsShi)
		{
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#D9B86C");
			GUILayout.Label("<b>释修修持</b>");
			GUILayout.BeginHorizontal();
			DrawMiniStat("修法", Empty(item.ShiTradition, item.CultivationPath), "#D9B86C", GUILayout.Width(150f));
			DrawMiniStat("境界", Empty(item.ShiRealm, item.Realm), "#D2B5FF", GUILayout.Width(150f));
			DrawMiniStat("修持", item.ShiPractice.ToString(), "#9CD7FF", GUILayout.Width(145f));
			DrawMiniStat("世数", Math.Max(1, item.ShiCurrentLife).ToString(), "#A7E08A", GUILayout.Width(125f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=grey>释修不纳入阴司追索；今释转世链由真灵转世记录连续展示。</color>");
			GUILayout.EndVertical();
			return;
		}

		if (item.IsDaoTai)
		{
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#D2B5FF");
			GUILayout.BeginHorizontal();
			DrawMiniStat("所在", ResolveDaoTaiLocation(item), "#D2B5FF", GUILayout.Width(165f));
			if (item.DaoTaiNextReturnYear > 0)
			{
				DrawMiniStat("返世", XjChronology.FormatYear(item.DaoTaiNextReturnYear), "#9CD7FF", GUILayout.Width(165f));
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			return;
		}

		string stateColor = item.IsDaoTai ? "#D2B5FF"
			: item.ActiveMissionId > 0L ? "#FF6666"
			: item.YinSiKnown || item.YinSiExposure >= 3f ? "#FF8877" : "#A7E08A";
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(stateColor);
		GUILayout.BeginHorizontal();
		DrawMiniStat("阴司", item.IsDaoTai ? "不入阴司" : TranslateYinSiState(item.YinSiState, item.YinSiKnown), stateColor, GUILayout.Width(150f));
		DrawMiniStat("追索", item.IsDaoTai ? "无追索" : item.ActiveMissionId > 0L ? TranslateYinSiMissionStage(item.MissionStage) : "无", item.ActiveMissionId > 0L ? "#FF6666" : "#888888", GUILayout.Width(150f));
		DrawMiniStat("所在", item.Sheltered ? Empty(item.SecretRealmName, "宗门秘境") : "外界", item.Sheltered ? "#B7A7FF" : "#CFC7B2", GUILayout.Width(185f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		if (item.IsDaoTai || item.YinSiKnown || item.ActiveMissionId > 0L)
		{
			GUILayout.Label("<color=grey>" + Rich(TrimArchiveText(Empty(item.YinSiReason, "暂无"), 48)) + "</color>");
		}
		GUILayout.EndVertical();
	}

	private void DrawCultivatorLifeStatusCard(XjCodexJinDanItem item)
	{
		if (item == null) return;
		string state = Empty(item.LifeStatus, item.IsHistorical ? "死亡" : "存世");
		string color = ResolveLifeStatusColor(state);
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>生死状态</b>");
		GUILayout.FlexibleSpace();
		DrawTag(state, color);
		GUILayout.EndHorizontal();

		if (string.Equals(state, "转世", StringComparison.Ordinal) || item.IsReincarnationContinuation)
		{
			string sourceName = Empty(item.ReincarnationSourceName, "前世名讳未载");
			GUILayout.Label("前世：<color=#9CD7FF>" + Rich(sourceName) + "</color>"
				+ (item.ReincarnationYear > 0 ? "　转世于" + XjChronology.FormatYear(item.ReincarnationYear) : string.Empty));
			if (item.FocusActorId > 0L)
			{
				GUILayout.Label("现世：<color=#A7E08A>" + Rich(Empty(item.Name, "未名转世身")) + "</color>　当前境界：" + Rich(Empty(item.Realm, "未载")));
			}
			else
			{
				GUILayout.Label("<color=grey>转世记录已成立，当前肉身尚未能从世界索引中确认。</color>");
			}
		}
		else if (string.Equals(state, "死亡", StringComparison.Ordinal))
		{
			GUILayout.Label("身故：" + (item.DeathYear > 0 ? XjChronology.FormatYear(item.DeathYear) : "年份未载"));
			GUILayout.Label("<color=grey>" + Rich(TranslateHighRealmDeathReason(item.DeathReason, state)) + "</color>");
		}
		else if (string.Equals(state, "天外", StringComparison.Ordinal))
		{
			GUILayout.Label("<color=grey>此身已离开现世活动层，人物史录仍保留。</color>");
		}
		else if (string.Equals(state, "闭关", StringComparison.Ordinal))
		{
			GUILayout.Label("<color=grey>此身仍存世，当前处于闭关状态。</color>");
		}
		else
		{
			GUILayout.Label("<color=grey>此身仍在现世活动。</color>");
		}
		GUILayout.EndVertical();
	}

	private void DrawJinDanCultivationAuthorityDetail(XjCodexJinDanItem item)
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#9CD7FF");
		GUILayout.Label("<b>修持与果位真景</b>");
		GUILayout.BeginHorizontal();
		DrawMiniStat(item.CultivationPath == "服气养性" ? "真君修持" : "金丹道行",
			(item.CultivationPath == "服气养性" ? item.XiuChi : item.DaoXing).ToString(), "#9CD7FF", GUILayout.Width(165f));
		DrawMiniStat("实际权辖", item.AuthorityCount.ToString(), "#FFD37A", GUILayout.Width(135f));
		DrawMiniStat("权柄状态", ResolveAuthorityLifecycleDisplay(item), item.IntegrationRetreatActive ? "#B7A7FF" : "#A7E08A", GUILayout.Width(205f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Label("果位真景：<color=#D2B5FF>" + Rich(Empty(item.GuoWeiImageSummary, "尚未形成稳定真景")) + "</color>");
		GUILayout.EndVertical();

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#FFD37A");
		GUILayout.Label("<b>所持权辖</b>");
		GUILayout.Label("本道：<color=#A7E08A>" + Rich(Empty(item.LocalAuthoritySummary, "无")) + "</color>");
		GUILayout.Label("已融外柄：<color=#FFD37A>" + Rich(Empty(item.SeizedAuthoritySummary, "无")) + "</color>");
		if (!string.IsNullOrWhiteSpace(item.PendingAuthoritySummary) || item.IntegrationRetreatActive)
		{
			GUILayout.Label("待融外柄：<color=#B7A7FF>" + Rich(Empty(item.PendingAuthoritySummary, "待融信息未载")) + "</color>");
		}
		GUILayout.EndVertical();

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#B7A7FF");
		GUILayout.Label("<b>神通易象</b>");
		GUILayout.Label(string.IsNullOrWhiteSpace(item.ShenTongMutationSummary)
			? "<color=grey>当前没有由权柄维持的神通易象。</color>"
			: Rich(item.ShenTongMutationSummary));
		GUILayout.EndVertical();
	}

	private static string ResolveAuthorityLifecycleDisplay(XjCodexJinDanItem item)
	{
		if (item == null) return "未载";
		if (item.IntegrationRetreatActive)
		{
			return item.IntegrationRetreatEndYear > 0 ? "合道至" + XjChronology.FormatYear(item.IntegrationRetreatEndYear) : "外柄合道中";
		}
		return Empty(item.AuthorityLifecycleStatus, item.AuthorityCount > 0 ? "执柄安稳" : "暂无权柄");
	}

	private static string ResolveDaoTaiLocation(XjCodexJinDanItem item)
	{
		if (item == null) return "外界";
		if (string.Equals(item.LifeStatus, "死亡", StringComparison.Ordinal)
			|| string.Equals(item.LifeStatus, "转世", StringComparison.Ordinal)) return item.LifeStatus;
		if (string.Equals(item.LifeStatus, "天外", StringComparison.Ordinal)) return "天外";
		if (string.Equals(item.LifeStatus, "闭关", StringComparison.Ordinal)) return "闭关";
		string status = (item.DaoTaiPresenceStatus ?? string.Empty).Trim();
		if (string.Equals(status, XjDaoTaiPresenceStatus.BeyondWorld, StringComparison.Ordinal)
			|| string.Equals(status, XjDaoTaiPresenceStatus.LegacyBeyondWorld, StringComparison.Ordinal))
		{
			return "天外";
		}
		if (string.Equals(status, XjDaoTaiPresenceStatus.Traveling, StringComparison.Ordinal)) return "游历";
		if (string.Equals(status, XjDaoTaiPresenceStatus.ClosedCultivation, StringComparison.Ordinal)) return "闭关";
		if (item.Sheltered) return Empty(item.SecretRealmName, "宗门秘境");
		return "外界";
	}

	private void DrawJinDanDetailStatGrid(XjCodexJinDanItem item)
	{
		if (item?.IsShi == true)
		{
			int shiColumns = ContentWidth < 820f ? 2 : 4;
			float shiWidth = Mathf.Max(165f, (ContentWidth * (IsArchiveStackedLayout() ? 0.92f : 0.64f)) / shiColumns);
			for (int i = 0; i < 4; i++)
			{
				if (i % shiColumns == 0) GUILayout.BeginHorizontal();
				switch (i)
				{
					case 0: DrawMiniStat("身份", Empty(item.ShiRealm, item.Achievement), "#D2B5FF", GUILayout.Width(shiWidth)); break;
					case 1: DrawMiniStat("修法", Empty(item.ShiTradition, item.CultivationPath), "#D9B86C", GUILayout.Width(shiWidth)); break;
					case 2: DrawMiniStat("修持", Math.Max(0, item.ShiPractice).ToString(), "#9CD7FF", GUILayout.Width(shiWidth)); break;
					case 3: DrawMiniStat("世数", Math.Max(1, item.ShiCurrentLife).ToString(), "#A7E08A", GUILayout.Width(shiWidth)); break;
				}
				if (i % shiColumns == shiColumns - 1 || i == 3) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); }
			}
			return;
		}

		int columns = ContentWidth < 820f ? 2 : 4;
		float width = Mathf.Max(165f, (ContentWidth * (IsArchiveStackedLayout() ? 0.92f : 0.64f)) / columns);
		for (int i = 0; i < 4; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			switch (i)
			{
				case 0:
					DrawMiniStat("身份", Empty(item.Achievement, "修士"), item.IsKongZheng ? "#74E8FF" : "#FFD37A", GUILayout.Width(width));
					break;
				case 1:
					DrawMiniStat("道途", Empty(item.DaoTu, "未定"), "#B7A7FF", GUILayout.Width(width));
					break;
				case 2:
				{
					bool isShenDan = !string.IsNullOrWhiteSpace(item.Achievement)
						&& item.Achievement.IndexOf("神丹", StringComparison.Ordinal) >= 0;
					DrawMiniStat(isShenDan ? "挂靠" : "果位", Empty(item.GuoWei, isShenDan ? "未明" : "无"), "#D2B5FF", GUILayout.Width(width));
					break;
				}
				case 3:
					DrawMiniStat("金性", Empty(item.JinXing, "无"), "#A7E08A", GUILayout.Width(width));
					break;
			}
			if (i % columns == columns - 1 || i == 3)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private static string ResolveJinDanAccent(XjCodexJinDanItem item)
	{
		if (item == null) return "#888888";
		if (item.IsHistorical) return ResolveLifeStatusColor(item.LifeStatus);
		if (item.IsDaoTai) return "#D2B5FF";
		if (item.IsShi) return string.Equals(item.ShiTradition, "古释", StringComparison.Ordinal) ? "#D9C78A" : "#D2B5FF";
		if (item.ActiveMissionId > 0L) return "#FF6666";
		if (item.YinSiKnown) return "#FF8877";
		return item.CultivationPath == "服气养性" ? "#74E8FF" : "#FFD37A";
	}

	private static string ResolveLifeStatusColor(string lifeStatus)
	{
		if (string.Equals(lifeStatus, "死亡", StringComparison.Ordinal)) return "#888888";
		if (string.Equals(lifeStatus, "转世", StringComparison.Ordinal)) return "#9CD7FF";
		if (string.Equals(lifeStatus, "天外", StringComparison.Ordinal)) return "#D2B5FF";
		if (string.Equals(lifeStatus, "闭关", StringComparison.Ordinal)) return "#B7A7FF";
		return "#A7E08A";
	}

	private static string TranslateHighRealmDeathReason(string reason, string lifeStatus)
	{
		string raw = (reason ?? string.Empty).Trim();
		if (string.Equals(lifeStatus, "转世", StringComparison.Ordinal))
		{
			if (raw.IndexOf("FuQi", StringComparison.OrdinalIgnoreCase) >= 0) return "金性护住真灵，已入转世。";
			return "真灵未绝，已入转世。";
		}
		// 仅兼容 RC11.1 及更早存档中已经发生的旧死亡原因；新版本不再产生此事件。
		if (raw.IndexOf("DaoTaiAscensionCalamity", StringComparison.OrdinalIgnoreCase) >= 0) return "成胎杀劫中身死。";
		if (raw.IndexOf("JinDanFailure", StringComparison.OrdinalIgnoreCase) >= 0) return "求金证果失败，身死道消。";
		if (raw.IndexOf("FuQi", StringComparison.OrdinalIgnoreCase) >= 0) return "服气求证失败，身死道消。";
		if (raw.Length == 0) return "身死道消，死因未载。";
		return "身死道消。";
	}

	private static string TrimArchiveText(string value, int maximum)
	{
		string text = (value ?? string.Empty).Trim();
		if (maximum <= 1 || text.Length <= maximum) return text;
		return text.Substring(0, maximum - 1) + "…";
	}

	private void EnsureSelectedJinDan(IReadOnlyList<XjCodexJinDanItem> items)
	{
		if (_jinDanActorFilter > 0L && items != null)
		{
			for (int i = 0; i < items.Count; i++)
			{
				XjCodexJinDanItem filtered = items[i];
				if (filtered != null && (filtered.ActorId == _jinDanActorFilter || filtered.FocusActorId == _jinDanActorFilter))
				{
					_selectedJinDanActorId = filtered.ActorId;
					break;
				}
			}
		}
		if (FindSelectedJinDan(items) != null) return;
		_selectedJinDanActorId = items != null && items.Count > 0 ? items[0].ActorId : 0L;
	}

	private XjCodexJinDanItem FindSelectedJinDan(IReadOnlyList<XjCodexJinDanItem> items)
	{
		if (items == null) return null;
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i]?.ActorId == _selectedJinDanActorId) return items[i];
		}
		return null;
	}

	private void InvalidateJinDanSelection()
	{
		_jinDanDisplayCacheVersion = -1;
		_selectedJinDanActorId = 0L;
		_jinDanListScrollPosition = Vector2.zero;
	}

	private void ResetJinDanFilters()
	{
		_jinDanPathFilter = "全部修法";
		_jinDanAchievementFilter = "全部身份";
		_jinDanYinSiFilter = "全部状态";
		_jinDanSearchText = string.Empty;
		_jinDanSectFilter = 0L;
		_jinDanActorFilter = 0L;
		InvalidateJinDanSelection();
	}
}
