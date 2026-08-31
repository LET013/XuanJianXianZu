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
private void DrawHistoryPage(XjCodexSnapshot snapshot)
	{
		DrawHistoryBookPage(snapshot);
	}

private void DrawCenturyAnnalsPage(XjCodexSnapshot snapshot)
	{
		ClearSectDetailState();
		DrawPageHeader("百年世谱", "百年一卷，归纳家族兴衰、宗门更替、人物际遇、境界盛衰与传承流转。");
		DrawSpringAutumnNavigation();
		GUILayout.BeginHorizontal();
		DrawOverviewPill("世谱卷数", snapshot.CenturyAnnals.Count + " / " + snapshot.CenturyAnnalsCapacity, "#A7E08A", GUILayout.Width(210f));
		DrawOverviewPill("开始年份", FormatCenturyAnnalsStartYear(snapshot.CenturyAnnalsStartYear), "#9CD7FF", GUILayout.Width(170f));
		DrawOverviewPill("世界年份", snapshot.WorldYear.ToString(), "#FFD37A", GUILayout.Width(170f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
		DrawCenturyAnnalsPanel(snapshot);
	}

private void DrawSpringAutumnNavigation()
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawOrnamentDivider("#8FA9C7", "春 秋 史 册");
		GUILayout.BeginHorizontal();
		bool compact = ContentWidth < 760f;
		Color old = GUI.backgroundColor;
		GUI.backgroundColor = _currentTab == 11 ? new Color(0.34f, 0.42f, 0.50f) : new Color(0.23f, 0.23f, 0.23f);
		if (GUILayout.Button("玄鉴史册", compact ? GUILayout.ExpandWidth(true) : GUILayout.Width(170f), GUILayout.Height(40f)))
		{
			SelectCodexTab(11);
		}
		GUI.backgroundColor = _currentTab == 12 ? new Color(0.34f, 0.42f, 0.50f) : new Color(0.23f, 0.23f, 0.23f);
		if (GUILayout.Button("百年世谱", compact ? GUILayout.ExpandWidth(true) : GUILayout.Width(170f), GUILayout.Height(40f)))
		{
			SelectCodexTab(12);
		}
		GUI.backgroundColor = old;
		if (!compact) GUILayout.Label("<color=grey>史册保存逐事底本，世谱按百年归纳；只共用导航，不合并记录。</color>");
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		if (compact) GUILayout.Label("<color=grey>史册保存逐事底本，世谱按百年归纳；只共用导航，不合并记录。</color>");
		GUILayout.EndVertical();
		GUILayout.Space(6f);
	}

private void ClearSectDetailState()
	{
		_sectResourceDetailSectId = 0L;
		_sectResourceDetailView = string.Empty;
		_sectPeakDetailSectId = 0L;
		_sectPeakSelectedPeakId = int.MinValue;
	}

private static string FormatCenturyAnnalsStartYear(int startYear)
	{
		return startYear > 0 ? startYear.ToString() : "待定";
	}

private void DrawHistoryCategoryButton(string category)
	{
		Color old = GUI.backgroundColor;
		GUI.backgroundColor = string.Equals(_historyCategory, category, StringComparison.Ordinal)
			? ParseHexColor(HistoryCategoryColor(category), new Color(0.25f, 0.42f, 0.58f))
			: new Color(0.28f, 0.28f, 0.28f);
		if (GUILayout.Button(category, GUILayout.Height(32f), GUILayout.Width(96f)))
		{
			_historyCategory = category;
			_historyClearCategory = category;
			_historyMessage = string.Empty;
			_historyClearConfirm = false;
		}
		GUI.backgroundColor = old;
	}

private static int CountHistory(IReadOnlyList<XjCodexHistoryItem> history, string category, bool importantOnly, bool traceableOnly)
	{
		int count = 0;
		for (int i = 0; i < history.Count; i++) if (MatchesHistory(history[i], category, importantOnly, traceableOnly)) count++;
		return count;
	}

private static bool MatchesHistory(XjCodexHistoryItem item, string category, bool importantOnly, bool traceableOnly)
	{
		if (item == null) return false;
		if (!string.Equals(category, XjWorldHistoryCategory.All, StringComparison.Ordinal)
			&& !string.Equals(category, item.Category, StringComparison.Ordinal)) return false;
		if (importantOnly && item.Importance < 3) return false;
		if (traceableOnly && !HasHistoryTraceTarget(item)) return false;
		return true;
	}

private static bool HasHistoryTraceTarget(XjCodexHistoryItem item)
	{
		// Only mark records as directly traceable when the current card can
		// actually focus a living actor or a stored world coordinate. Sect,
		// family and city IDs remain visible metadata until their detail-page
		// jump commands are implemented.
		return item != null && (item.ActorId > 0L || item.RelatedActorId > 0L || item.FamilyId > 0L || item.SectId > 0L || item.HasLocation);
	}

private string BuildHistoryFilterText()
	{
		string text = _historyCategory;
		if (_historyImportantOnly) text += " · 重要";
		if (_historyTraceableOnly) text += " · 可定位";
		return text;
	}

private void DrawHistoryCard(XjCodexHistoryItem item)
	{
		string color = HistoryCategoryColor(item.Category);
		GUILayout.BeginVertical(GUI.skin.box);
		Rect stripe = GUILayoutUtility.GetRect(100f, 5f, GUILayout.ExpandWidth(true));
		DrawSolidRect(stripe, ParseHexColor(color, Color.white));

		GUILayout.BeginHorizontal();
		string year = item.Year > 0 ? "第 " + item.Year + " 年" : "未知年份";
		GUILayout.Label("<b>" + year + "</b>", GUILayout.Width(120f));
		DrawTag(item.Category, color);
		GUILayout.Label("重要度：<color=" + ImportanceColor(item.Importance) + ">" + BuildImportanceMarks(item.Importance) + "</color>", GUILayout.Width(170f));
		if (!string.IsNullOrWhiteSpace(item.EventType)) DrawTag(TranslateHistoryEventType(item.EventType), "#9CD7FF");
		GUILayout.FlexibleSpace();
		if (item.IsProtected) DrawTag("重点收录", "#FFD37A");
		GUILayout.EndHorizontal();

		string title = string.IsNullOrWhiteSpace(item.Title) ? item.Body : item.Title;
		GUILayout.Label("<size=18><b>" + Rich(title) + "</b></size>");
		if (!string.IsNullOrWhiteSpace(item.Body) && !string.Equals(item.Body, title, StringComparison.Ordinal)) GUILayout.Label(Rich(item.Body));
		string meta = DescribeHistoryMeta(item);
		if (!string.IsNullOrWhiteSpace(meta)) GUILayout.Label("<color=grey>" + Rich(meta) + "</color>");

		GUILayout.BeginHorizontal();
		GUILayout.Label(HasHistoryTraceTarget(item) ? "<color=#9CD7FF>可追索</color>" : "<color=grey>无追索目标</color>", GUILayout.Width(100f));
		long actorId = item.ActorId > 0L ? item.ActorId : item.RelatedActorId;
		long familyId = item.FamilyId > 0L ? item.FamilyId : item.RelatedFamilyId;
		long sectId = item.SectId > 0L ? item.SectId : item.RelatedSectId;
		bool oldEnabled = GUI.enabled;
		GUI.enabled = oldEnabled && actorId > 0L;
		if (GUILayout.Button("修士列传", GUILayout.Width(108f), GUILayout.Height(36f)))
		{
			_historyBookView = "修士列传";
			_historySelectedActorId = actorId;
			_historySubjectSearch = string.Empty;
			_historySubjectListScroll = Vector2.zero;
			_historySubjectDetailScroll = Vector2.zero;
			_currentTab = 11;
			_scrollPosition = Vector2.zero;
			_focusMessage = string.Empty;
		}
		GUI.enabled = oldEnabled && familyId > 0L;
		if (GUILayout.Button("世家纪事", GUILayout.Width(108f), GUILayout.Height(36f)))
		{
			_historyBookView = "世家纪事";
			_historySelectedFamilyId = familyId;
			_historySubjectSearch = string.Empty;
			_historySubjectListScroll = Vector2.zero;
			_historySubjectDetailScroll = Vector2.zero;
			_currentTab = 11;
			_scrollPosition = Vector2.zero;
			_focusMessage = string.Empty;
		}
		GUI.enabled = oldEnabled && sectId > 0L;
		if (GUILayout.Button("宗门纪事", GUILayout.Width(108f), GUILayout.Height(36f)))
		{
			_historyBookView = "宗门纪事";
			_historySelectedSectId = sectId;
			_historySubjectSearch = string.Empty;
			_historySubjectListScroll = Vector2.zero;
			_historySubjectDetailScroll = Vector2.zero;
			_currentTab = 11;
			_scrollPosition = Vector2.zero;
			_focusMessage = string.Empty;
		}
		GUI.enabled = oldEnabled;
		GUILayout.FlexibleSpace();
		bool canActor = actorId > 0L && XjScheduler.ResolveActor(actorId, out Actor actor) && actor?.data != null && actor.isAlive();
		GUI.enabled = oldEnabled && canActor;
		if (GUILayout.Button("定位角色", GUILayout.Width(108f), GUILayout.Height(36f)))
		{
			_historyMessage = TryFocusHistoryActor(actorId) ? "已定位到历史事件相关角色。" : "该角色已经死亡或暂时无法定位。";
		}
		GUI.enabled = oldEnabled && item.HasLocation;
		if (GUILayout.Button("定位地点", GUILayout.Width(108f), GUILayout.Height(36f)))
		{
			_historyMessage = TryFocusHistoryLocation(item) ? "已定位到历史事件发生地点。" : "该地点暂时无法定位。";
		}
		GUI.enabled = oldEnabled;
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		GUILayout.Space(4f);
	}

	private static string TranslateHistoryEventType(string eventType)
	{
		if (string.IsNullOrWhiteSpace(eventType)) return "纪事";
		string value = eventType.Trim();
		if (string.Equals(value, "GuoWeiZhongAiZhengWeiLocked", StringComparison.Ordinal)) return "果位钟爱";
		if (string.Equals(value, "SameDaoTuZhengWeiSuccession", StringComparison.Ordinal)) return "同道夺正";
		if (string.Equals(value, "AdjacentDaoTuZhengWeiSuccession", StringComparison.Ordinal)) return "转道夺正";
		if (string.Equals(value, "GuoWeiOwnerRevealedByProbe", StringComparison.Ordinal)) return "探位显主";
		if (string.Equals(value, "JinDanDaoObstruction", StringComparison.Ordinal)
			|| string.Equals(value, "PersonalJinDanDaoObstructed", StringComparison.Ordinal)
			|| string.Equals(value, "PersonalJinDanDaoObstructor", StringComparison.Ordinal)) return "阻道";
		if (string.Equals(value, "LongShuOrigin", StringComparison.Ordinal)) return "龙属肇生";
		if (string.Equals(value, "LongShuBirth", StringComparison.Ordinal)) return "龙属现世";
		if (string.Equals(value, "LongShuHeShuiInfluence", StringComparison.Ordinal)) return "龙属合水";
		if (string.Equals(value, "HeShuiFruitReservedForLongShu", StringComparison.Ordinal)) return "合水拒证";
		if (string.Equals(value, "LongShuKillReward", StringComparison.Ordinal)) return "斩龙得灵";
		if (string.Equals(value, "PersonalGongFaObtained", StringComparison.Ordinal)) return "功法";
		if (string.Equals(value, "PersonalFuQiCultivationPhase", StringComparison.Ordinal)) return "性命修持";
		if (string.Equals(value, "PersonalFuQiIntentStudy", StringComparison.Ordinal)) return "观剑";
		if (string.Equals(value, "PersonalFuQiHuangGuan", StringComparison.Ordinal)
			|| string.Equals(value, "PersonalFuQiZhenRen", StringComparison.Ordinal)
			|| string.Equals(value, "PersonalFuQiZhenJunYuShi", StringComparison.Ordinal)) return "服气破境";
		if (string.Equals(value, "PersonalFuQiShenMiaoPerfected", StringComparison.Ordinal)) return "神妙圆满";
		if (string.Equals(value, "PersonalFuQiJinDanFailure", StringComparison.Ordinal)) return "求证受挫";
		if (string.Equals(value, "PersonalFuQiLongGengSuccession", StringComparison.Ordinal)) return "长庚承位";
		if (string.Equals(value, "PersonalFuQiToZiFu", StringComparison.Ordinal)) return "改易修法";
		if (value.IndexOf("Breakthrough", StringComparison.OrdinalIgnoreCase) >= 0) return "破境";
		if (value.IndexOf("Death", StringComparison.OrdinalIgnoreCase) >= 0) return "生死";
		if (value.IndexOf("Inheritance", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("Technique", StringComparison.OrdinalIgnoreCase) >= 0) return "传承";
		if (value.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0) return "身份";
		if (value.IndexOf("Conflict", StringComparison.OrdinalIgnoreCase) >= 0
			|| value.IndexOf("Vendetta", StringComparison.OrdinalIgnoreCase) >= 0
			|| value.IndexOf("Chess", StringComparison.OrdinalIgnoreCase) >= 0) return "恩怨";
		if (value.IndexOf("Patronage", StringComparison.OrdinalIgnoreCase) >= 0
			|| value.IndexOf("Override", StringComparison.OrdinalIgnoreCase) >= 0) return "扶持";
		if (value.IndexOf("Craft", StringComparison.OrdinalIgnoreCase) >= 0) return "百艺";
		if (value.IndexOf("Opportunity", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("DongTian", StringComparison.OrdinalIgnoreCase) >= 0) return "机缘";
		if (value.IndexOf("Sect", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("ZongMen", StringComparison.OrdinalIgnoreCase) >= 0) return "宗门";
		if (value.IndexOf("Family", StringComparison.OrdinalIgnoreCase) >= 0) return "家族";
		return "纪事";
	}

private void DrawCenturyAnnalsPanel(XjCodexSnapshot snapshot)
	{
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.Label("<b>百年世谱</b> <color=grey>百年一卷，观世家春秋与修仙界气运流转。</color>");
		if (snapshot.CenturyAnnals.Count == 0)
		{
			DrawEmptyCard("尚无百年世谱。待此世百年期满后，将生成第一卷世谱。", "#777777");
			GUILayout.EndVertical();
			return;
		}
		XjCodexCenturyAnnalsItem selected = ResolveSelectedCenturyAnnals(snapshot.CenturyAnnals);
		GUILayout.BeginHorizontal();
		DrawCenturyAnnalsList(snapshot.CenturyAnnals, selected);
		GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
		DrawCenturyAnnalsCard(selected);
		GUILayout.EndVertical();
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

private XjCodexCenturyAnnalsItem ResolveSelectedCenturyAnnals(IReadOnlyList<XjCodexCenturyAnnalsItem> items)
	{
		if (items == null || items.Count == 0) return null;
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i] != null && items[i].CenturyId == _centuryAnnalsSelectedCenturyId) return items[i];
		}
		XjCodexCenturyAnnalsItem latest = items[items.Count - 1];
		_centuryAnnalsSelectedCenturyId = latest?.CenturyId ?? 0;
		return latest;
	}

private void DrawCenturyAnnalsList(IReadOnlyList<XjCodexCenturyAnnalsItem> items, XjCodexCenturyAnnalsItem selected)
	{
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(270f));
		DrawCardStripe("#6FAE9D");
		DrawOrnamentDivider("#6FAE9D", "世 谱 卷 目");
		GUILayout.Label("<color=grey>百年为卷，按游戏起始纪年连续编录。</color>");
		GUILayout.Space(4f);
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexCenturyAnnalsItem item = items[i];
			if (item == null) continue;
			bool isSelected = selected != null && selected.CenturyId == item.CenturyId;
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = isSelected
				? new Color(0.25f, 0.43f, 0.34f)
				: new Color(0.20f, 0.23f, 0.22f);
			string status = item.IsCompleteCycle ? "正卷" : "补卷";
			string label = (isSelected ? "◆ " : "◇ ") + "第" + item.CenturyId + "世谱"
				+ "\n玄鉴历 " + item.StartYear + "—" + item.EndYear + " 年 · " + status;
			if (GUILayout.Button(label, GUILayout.Width(246f), GUILayout.Height(58f)))
			{
				_centuryAnnalsSelectedCenturyId = item.CenturyId;
				_scrollPosition = Vector2.zero;
			}
			GUI.backgroundColor = old;
			GUILayout.Space(3f);
		}
		DrawOrnamentDivider("#446E64", string.Empty);
		GUILayout.Label("<color=grey>现存世谱 " + items.Count + " 卷。</color>");
		GUILayout.EndVertical();
	}

private void DrawCenturyAnnalsCard(XjCodexCenturyAnnalsItem item)
	{
		if (item == null) return;
		string accent = item.IsCompleteCycle ? "#A7E08A" : "#FFAA66";
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(accent);
		DrawOrnamentDivider(accent, "玄 鉴 百 年 正 卷");
		GUILayout.BeginHorizontal();
		GUILayout.BeginVertical();
		GUILayout.Label("<b><size=25>第" + Math.Max(1, item.CenturyId) + "世谱</size></b>");
		GUILayout.Label("<color=#9CD7FF><b>玄鉴历 " + item.StartYear + "—" + item.EndYear + " 年</b></color>");
		GUILayout.EndVertical();
		GUILayout.FlexibleSpace();
		DrawTag(item.IsCompleteCycle ? "完整周期" : "补录周期", accent);
		GUILayout.Label("<color=grey>成卷于玄鉴历 " + item.GeneratedYear + " 年</color>", GUILayout.Width(145f));
		GUILayout.EndHorizontal();

		GUILayout.Space(5f);
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#6FAE9D");
		GUILayout.Label("<b><size=19><color=#6FAE9D>◇ 本世纪总览 ◇</color></size></b>");
		GUILayout.Label("<size=16>" + Rich(Empty(item.WorldSummary, "本卷尚无摘要。")) + "</size>");
		GUILayout.EndVertical();

		GUILayout.Space(5f);
		GUILayout.BeginHorizontal();
		DrawOverviewPill("本卷大事", (item.Events?.Count ?? 0).ToString(), "#6FAE9D", GUILayout.Width(145f));
		DrawOverviewPill("入谱家族", item.FamilyCount.ToString(), "#A7E08A", GUILayout.Width(145f));
		DrawOverviewPill("宗门变迁", item.SectCount.ToString(), "#FFD37A", GUILayout.Width(145f));
		DrawOverviewPill("代表人物", item.ActorCount.ToString(), "#B7A7FF", GUILayout.Width(145f));
		DrawOverviewPill("境界分布", (item.RealmStatistics?.Count ?? 0).ToString(), "#FF8877", GUILayout.Width(145f));
		DrawOverviewPill("道途纪要", (item.DaoSummaries?.Count ?? 0).ToString(), "#9CD7FF", GUILayout.Width(145f));
		DrawOverviewPill("传承器物", (item.ArtifactSummaries?.Count ?? 0).ToString(), "#B7A7FF", GUILayout.Width(145f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		DrawCenturyAnnalsCategoryButtons();
		DrawCenturyAnnalsSelectedCategory(item);
		GUILayout.EndVertical();
	}

private void DrawCenturyAnnalsCategoryButtons()
	{
		GUILayout.Space(8f);
		DrawOrnamentDivider("#6FAE9D", "分 卷 阅 览");
		GUILayout.BeginHorizontal();
		for (int i = 0; i < CenturyAnnalsCategories.Length; i++)
		{
			string category = CenturyAnnalsCategories[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_centuryAnnalsCategory, category, StringComparison.Ordinal)
				? new Color(0.25f, 0.43f, 0.34f)
				: new Color(0.20f, 0.23f, 0.22f);
			if (GUILayout.Button("◇ " + category, GUILayout.Height(38f), GUILayout.Width(124f)))
			{
				_centuryAnnalsCategory = category;
			}
			GUI.backgroundColor = old;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(7f);
	}

private void DrawCenturyAnnalsSelectedCategory(XjCodexCenturyAnnalsItem item)
	{
		string category = string.IsNullOrWhiteSpace(_centuryAnnalsCategory) ? "总览" : _centuryAnnalsCategory;
		if (category == "总览")
		{
			DrawCenturyEventsSection(item.Events, 6);
			DrawCenturyAspirationsSection(item.Families, 5);
			DrawCenturyRealmStatistics(item.RealmStatistics, 8);
			DrawCenturySectSummaries(item.Sects, 4);
			DrawCenturyActorsSection(item.Actors, 4);
			return;
		}
		if (category == "本卷大事")
		{
			DrawCenturyEventsSectionOrEmpty(item.Events);
			return;
		}
		if (category == "家族兴衰")
		{
			DrawCenturyFamiliesSection(item.Families);
			return;
		}
		if (category == "宗门变化")
		{
			DrawCenturySectSummaries(item.Sects, 16);
			return;
		}
		if (category == "代表人物")
		{
			DrawCenturyActorsSectionOrEmpty(item.Actors);
			return;
		}
		if (category == "境界统计")
		{
			DrawCenturyRealmStatistics(item.RealmStatistics, item.RealmStatistics?.Count ?? 0);
			return;
		}
		if (category == "道途纪要")
		{
			DrawCenturyDaoFilters();
			DrawCenturyDaoSummarySection(item.DaoSummaries);
			return;
		}
		DrawCenturyArtifactSummaries(item.ArtifactSummaries, 16);
	}

private void DrawCenturyFamiliesSection(IReadOnlyList<XjCodexCenturyFamilyItem> families)
	{
		GUILayout.Label("<b><size=19>家族兴衰</size></b> <color=grey>按本世纪阶段、志业、支柱与重宝归纳世家命运。</color>");
		if (families == null || families.Count == 0)
		{
			DrawEmptyCard("本卷没有入谱家族。", "#777777");
			return;
		}
		for (int i = 0; i < families.Count; i++)
		{
			XjCodexCenturyFamilyItem family = families[i];
			if (family == null) continue;
			string color = CenturyStageColor(family.CurrentStage);
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(color);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=20>" + Rich(Empty(family.FamilyName, "未名氏")) + "</size></b>");
			GUILayout.FlexibleSpace();
			DrawTag(Empty(family.CurrentStage, "未定"), color);
			if (family.FamilyId > 0L && GUILayout.Button("查看世家纪事", GUILayout.Width(136f), GUILayout.Height(38f)))
			{
				ShowHistoryFamily(family.FamilyId);
			}
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			DrawMiniStat("本卷修士", Math.Max(0, family.CultivatorCount).ToString(), "#9CD7FF", GUILayout.Width(125f));
			DrawMiniStat("真人", Math.Max(0, family.ZiFuCount).ToString(), "#B7A7FF", GUILayout.Width(105f));
			DrawMiniStat("真君", Math.Max(0, family.JinDanCount).ToString(), "#FFD37A", GUILayout.Width(105f));
			DrawMiniStat("所属宗门", Empty(family.SectName, "未入宗门"), string.IsNullOrWhiteSpace(family.SectName) ? "#888888" : "#FFD37A", GUILayout.Width(190f));
			DrawMiniStat("族中支柱", Empty(family.PillarActorName, "暂无"), "#B7A7FF", GUILayout.Width(185f));
			DrawMiniStat("镇族重宝", Empty(family.FamilyTreasureSummary, "未有"), "#FFD37A", GUILayout.Width(230f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.Label("<b>阶段判词</b>  <color=grey>" + Rich(Empty(family.StageReason, "暂无阶段说明")) + "</color>");
			if (!string.IsNullOrWhiteSpace(family.Aspiration))
			{
				string since = family.AspirationSinceYear > 0 ? " · 自" + family.AspirationSinceYear + "年" : string.Empty;
				GUILayout.Label("<color=#A7E08A><b>志业</b></color>  " + Rich(family.Aspiration) + " · " + Rich(Empty(family.AspirationStatus, "未竟")) + since);
				if (!string.IsNullOrWhiteSpace(family.AspirationSummary)) GUILayout.Label("<color=grey>" + Rich(family.AspirationSummary) + "</color>");
			}
			if (!string.IsNullOrWhiteSpace(family.SupportedActorName))
			{
				string supportedSince = family.SupportedSinceYear > 0 ? " · 自" + family.SupportedSinceYear + "年" : string.Empty;
				GUILayout.Label("<color=#9CD7FF><b>族中所举</b></color>  " + Rich(family.SupportedActorName)
					+ " · " + Rich(Empty(family.SupportPurpose, "族议扶持")) + supportedSince);
			}
			if (!string.IsNullOrWhiteSpace(family.SectRelation)) GUILayout.Label("<color=#FFD37A><b>宗门关系</b></color>  " + Rich(family.SectRelation));
			GUILayout.EndVertical();
			GUILayout.EndVertical();
			GUILayout.Space(6f);
		}
	}

private void DrawCenturyAspirationsSection(IReadOnlyList<XjCodexCenturyFamilyItem> families, int maxCount)
	{
		if (families == null || families.Count == 0) return;
		int shown = 0;
		for (int i = 0; i < families.Count && shown < maxCount; i++)
		{
			XjCodexCenturyFamilyItem family = families[i];
			if (family == null || string.IsNullOrWhiteSpace(family.Aspiration)) continue;
			if (shown == 0)
			{
				GUILayout.Space(5f);
				GUILayout.Label("<b>世家志业</b>");
			}
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.Label("<b>" + Rich(Empty(family.FamilyName, "未名氏")) + "</b> · " + Rich(family.Aspiration) + " · " + Rich(Empty(family.AspirationStatus, "未竟")));
			if (!string.IsNullOrWhiteSpace(family.AspirationSummary)) GUILayout.Label("<color=grey>" + Rich(family.AspirationSummary) + "</color>");
			GUILayout.EndVertical();
			shown++;
		}
	}

private static string BuildCenturyFamilyRealmText(XjCodexCenturyFamilyItem family)
	{
		if (family == null) return "修士：0";
		List<string> parts = new List<string>
		{
			"修士：" + Math.Max(0, family.CultivatorCount).ToString()
		};
		if (family.ZiFuCount > 0)
		{
			parts.Add("真人：" + family.ZiFuCount.ToString());
		}
		if (family.JinDanCount > 0)
		{
			parts.Add("真君：" + family.JinDanCount.ToString());
		}
		return string.Join(" / ", parts);
	}

private void DrawCenturySummarySection(string title, IReadOnlyList<XjCodexCenturySummaryItem> items, string color, int maxCount)
	{
		if (items == null || items.Count == 0) return;
		GUILayout.Space(5f);
		GUILayout.Label("<b>" + Rich(title) + "</b>");
		int count = Mathf.Min(items.Count, maxCount);
		for (int i = 0; i < count; i++)
		{
			XjCodexCenturySummaryItem item = items[i];
			if (item == null) continue;
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<color=" + color + ">●</color> <b>" + Rich(Empty(item.Name, "未名")) + "</b>", GUILayout.Width(240f));
			GUILayout.Label(Rich(Empty(item.Trend, "见载")), GUILayout.Width(100f));
			if (!string.Equals(title, "道途纪要", StringComparison.Ordinal)) GUILayout.Label(FormatCenturySummaryMetric(title, item.Score), GUILayout.Width(110f));
			if (string.Equals(title, "宗门变化", StringComparison.Ordinal)
				&& long.TryParse(item.Key, out long historySectId) && historySectId > 0L
				&& GUILayout.Button("查看宗门纪事", GUILayout.Width(132f), GUILayout.Height(36f)))
			{
				_historyBookView = "宗门纪事";
				_historySelectedSectId = historySectId;
				_historySubjectSearch = string.Empty;
				_historySubjectListScroll = Vector2.zero;
				_currentTab = 11;
				_scrollPosition = Vector2.zero;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!string.IsNullOrWhiteSpace(item.Summary)) GUILayout.Label("<color=grey>" + Rich(item.Summary) + "</color>");
			GUILayout.EndVertical();
		}
		if (items.Count > count) GUILayout.Label("<color=grey>另有 " + (items.Count - count) + " 条" + Rich(title) + "未展开。</color>");
	}

private static string FormatCenturySummaryMetric(string title, int score)
	{
		if (string.Equals(title, "境界统计", StringComparison.Ordinal))
		{
			return "数量 " + score;
		}
		if (string.Equals(title, "传承器物", StringComparison.Ordinal))
		{
			return "重要度 " + score;
		}
		if (string.Equals(title, "宗门变化", StringComparison.Ordinal))
		{
			return "影响 " + score;
		}
		return "分量 " + score;
	}

private void DrawCenturyDaoFilters()
	{
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.Label("<b>道途筛选</b> <color=grey>按已显现的常见、并古与长庚道途查看本世纪纪要。</color>");
		for (int i = 0; i < CenturyDaoFilters.Length; i++)
		{
			if (i % 7 == 0) GUILayout.BeginHorizontal();
			string filter = CenturyDaoFilters[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_centuryDaoFilter, filter, StringComparison.Ordinal)
				? new Color(0.25f, 0.40f, 0.50f)
				: new Color(0.24f, 0.24f, 0.24f);
			if (GUILayout.Button(filter, GUILayout.Width(92f), GUILayout.Height(38f))) _centuryDaoFilter = filter;
			GUI.backgroundColor = old;
			if (i % 7 == 6 || i == CenturyDaoFilters.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		GUILayout.EndVertical();
	}

private void DrawCenturyDaoSummarySection(IReadOnlyList<XjCodexCenturySummaryItem> items)
	{
		GUILayout.Label("<b>道途纪要</b>");
		if (items == null || items.Count == 0)
		{
			DrawEmptyCard("本卷没有道途纪要记录。", "#777777");
			return;
		}
		int matched = 0;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexCenturySummaryItem item = items[i];
			if (!MatchesCenturyDaoFilter(item)) continue;
			matched++;
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#9CD7FF");
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=18>" + Rich(Empty(item.Name, "未名道途")) + "</size></b>", GUILayout.Width(260f));
			GUILayout.Label("<color=#9CD7FF>" + Rich(Empty(item.Trend, "见载")) + "</color>", GUILayout.Width(110f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!string.IsNullOrWhiteSpace(item.Summary)) GUILayout.Label("<color=grey>" + Rich(item.Summary) + "</color>");
			GUILayout.EndVertical();
		}
		if (matched == 0) DrawEmptyCard("当前道途筛选下没有本世纪记录。", "#777777");
	}

private bool MatchesCenturyDaoFilter(XjCodexCenturySummaryItem item)
	{
		if (item == null) return false;
		if (string.IsNullOrWhiteSpace(_centuryDaoFilter) || string.Equals(_centuryDaoFilter, "全部", StringComparison.Ordinal)) return true;
		string text = (item.Key ?? string.Empty) + " " + (item.Name ?? string.Empty) + " " + (item.Summary ?? string.Empty);
		return _centuryDaoFilter switch
		{
			"三阳" => ContainsAny(text, "太阳", "少阳", "明阳"),
			"三阴" => ContainsAny(text, "太阴", "少阴", "厥阴"),
			"三雷" => ContainsAny(text, "玄雷", "霄雷", "元雷"),
			"金德" => ContainsAny(text, "兑金", "逍金", "齐金", "库金", "庚金"),
			"木德" => ContainsAny(text, "角木", "正木", "集木", "更木", "保木"),
			"水德" => ContainsAny(text, "坎水", "渌水", "合水", "府水", "牝水"),
			"火德" => ContainsAny(text, "离火", "灴火", "并火", "真火", "牡火"),
			"土德" => ContainsAny(text, "艮土", "戊土", "归土", "宝土", "宣土"),
			"十二炁" => ContainsAny(text, "清炁", "紫炁", "真炁", "邃炁", "寒炁", "晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪"),
			"鸺葵" => ContainsAny(text, "鸺葵"),
			"上巫" => ContainsAny(text, "上巫"),
			"玉真" => ContainsAny(text, "玉真"),
			"衡祝" => ContainsAny(text, "衡祝"),
			"青宣" => ContainsAny(text, "青宣"),
			"全丹" => ContainsAny(text, "全丹"),
			"执孛" => ContainsAny(text, "执孛"),
			"司天" => ContainsAny(text, "司天"),
			"都卫" => ContainsAny(text, "都卫"),
			"长庚" => ContainsAny(text, "长庚"),
			_ => false
		};
	}

private void DrawCenturyRealmStatistics(IReadOnlyList<XjCodexCenturySummaryItem> items, int maxCount)
	{
		GUILayout.Space(6f);
		DrawOrnamentDivider("#FF8877", "境 界 统 计");
		if (items == null || items.Count == 0 || maxCount <= 0)
		{
			DrawEmptyCard("本卷没有境界统计记录。", "#777777");
			return;
		}
		int count = Mathf.Min(items.Count, maxCount);
		int maximum = 1;
		for (int i = 0; i < count; i++)
		{
			if (items[i] != null) maximum = Math.Max(maximum, items[i].Score);
		}
		for (int i = 0; i < count; i++)
		{
			XjCodexCenturySummaryItem item = items[i];
			if (item == null) continue;
			string note = Empty(item.Trend, "本卷存量");
			if (!string.IsNullOrWhiteSpace(item.Summary)) note += " · " + item.Summary;
			DrawMetricBar(Empty(item.Name, "未名境界"), Math.Max(0, item.Score), maximum, CenturyRealmColor(item.Name), note);
		}
		if (items.Count > count) GUILayout.Label("<color=grey>另有 " + (items.Count - count) + " 项境界统计未展开。</color>");
	}

	private void DrawCenturySectSummaries(IReadOnlyList<XjCodexCenturySummaryItem> items, int maxCount)
	{
		GUILayout.Space(6f);
		DrawOrnamentDivider("#FFD37A", "宗 门 变 迁");
		if (items == null || items.Count == 0 || maxCount <= 0)
		{
			DrawEmptyCard("本卷没有宗门变化记录。", "#777777");
			return;
		}
		int count = Mathf.Min(items.Count, maxCount);
		for (int i = 0; i < count; i++)
		{
			XjCodexCenturySummaryItem item = items[i];
			if (item == null) continue;
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#FFD37A");
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=18>" + Rich(Empty(item.Name, "未名宗门")) + "</size></b>", GUILayout.Width(270f));
			GUILayout.Label("<color=#FFD37A>" + Rich(Empty(item.Trend, "见载")) + "</color>", GUILayout.Width(125f));
			GUILayout.Label("影响 <b>" + Math.Max(0, item.Score) + "</b>", GUILayout.Width(100f));
			GUILayout.FlexibleSpace();
			if (long.TryParse(item.Key, out long historySectId) && historySectId > 0L
				&& GUILayout.Button("阅山门春秋", GUILayout.Width(110f), GUILayout.Height(27f)))
			{
				_historyBookView = "宗门纪事";
				_historySelectedSectId = historySectId;
				_historySubjectSearch = string.Empty;
				_historySubjectListScroll = Vector2.zero;
				_currentTab = 11;
				_scrollPosition = Vector2.zero;
			}
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=grey>" + Rich(Empty(item.Summary, "本世纪宗门气数有变。")) + "</color>");
			GUILayout.EndVertical();
		}
		if (items.Count > count) GUILayout.Label("<color=grey>另有 " + (items.Count - count) + " 条宗门变迁未展开。</color>");
	}

	private void DrawCenturyArtifactSummaries(IReadOnlyList<XjCodexCenturySummaryItem> items, int maxCount)
	{
		GUILayout.Space(6f);
		DrawOrnamentDivider("#B7A7FF", "传 承 器 物");
		GUILayout.Label("<color=grey>仅收录六品功法、求金法、金丹金性、高阶法宝、丹方与真正稀有的洞天所得；五品功法不入此卷。</color>");
		if (items == null || items.Count == 0 || maxCount <= 0)
		{
			DrawEmptyCard("本卷没有达到传承器物门槛的记录。", "#777777");
			return;
		}
		int count = Mathf.Min(items.Count, maxCount);
		for (int i = 0; i < count; )
		{
			GUILayout.BeginHorizontal();
			int row = 0;
			while (i < count && row < 2)
			{
				XjCodexCenturySummaryItem item = items[i++];
				if (item == null) continue;
				GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(485f), GUILayout.Height(112f));
				DrawCardStripe("#B7A7FF");
				GUILayout.BeginHorizontal();
				GUILayout.Label("<color=#B7A7FF>◆</color> <b><size=17>" + Rich(Empty(item.Name, "未名器物")) + "</size></b>");
				GUILayout.FlexibleSpace();
				GUILayout.Label("重要度 " + Math.Max(0, item.Score), GUILayout.Width(90f));
				GUILayout.EndHorizontal();
				GUILayout.Label("<color=#FFD37A>" + Rich(Empty(item.Trend, "传承见载")) + "</color>");
				GUILayout.Label("<color=grey>" + Rich(Empty(item.Summary, "此物于本世纪进入传承。")) + "</color>");
				GUILayout.EndVertical();
				GUILayout.Space(5f);
				row++;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(4f);
		}
		if (items.Count > count) GUILayout.Label("<color=grey>另有 " + (items.Count - count) + " 件传承器物未展开。</color>");
	}

	private static bool ContainsAny(string value, params string[] needles)
	{
		if (string.IsNullOrWhiteSpace(value) || needles == null || needles.Length == 0) return false;
		for (int i = 0; i < needles.Length; i++)
		{
			string needle = needles[i];
			if (!string.IsNullOrWhiteSpace(needle)
				&& value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		}
		return false;
	}

	private static string CenturyRealmColor(string realm)
	{
		string value = realm ?? string.Empty;
		if (ContainsAny(value, "金丹", "神丹", "结璘", "真君")) return "#FFD37A";
		if (ContainsAny(value, "紫府", "真人")) return "#B7A7FF";
		if (ContainsAny(value, "筑基")) return "#9CD7FF";
		if (ContainsAny(value, "炼气", "胎息")) return "#A7E08A";
		return "#CFC7B2";
	}

private void DrawCenturyEventsSectionOrEmpty(IReadOnlyList<XjCodexCenturyEventItem> events)
	{
		if (events == null || events.Count == 0)
		{
			GUILayout.Label("<b>本卷大事</b>");
			DrawEmptyCard("本卷没有仍可确证的重大史事。旧档补卷只展示世界史与世谱账本中实际留存的证据，不以当前状态倒推旧事。", "#777777");
			return;
		}
		DrawCenturyEventsSection(events, events.Count);
	}

	private void DrawCenturyEventsSection(IReadOnlyList<XjCodexCenturyEventItem> events, int maxCount)
	{
		if (events == null || events.Count == 0 || maxCount <= 0) return;
		GUILayout.Space(6f);
		DrawOrnamentDivider("#6FAE9D", "本 卷 大 事");
		int count = Mathf.Min(events.Count, maxCount);
		for (int i = 0; i < count; i++)
		{
			XjCodexCenturyEventItem item = events[i];
			if (item == null) continue;
			string accent = item.Importance >= 4 ? "#FFD37A" : item.Importance >= 3 ? "#9CD7FF" : "#6FAE9D";
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(accent);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<color=" + accent + "><b>玄鉴历 " + Math.Max(1, item.Year) + " 年</b></color>", GUILayout.Width(150f));
			GUILayout.Label("<b><size=17>" + Rich(Empty(item.Title, "本卷史事")) + "</size></b>");
			GUILayout.FlexibleSpace();
			DrawTag("史重" + Math.Max(1, item.Importance), accent);
			GUILayout.EndHorizontal();
			GUILayout.Label("<size=15>" + Rich(Empty(item.Summary, item.Title)) + "</size>");

			List<string> context = new List<string>();
			if (!string.IsNullOrWhiteSpace(item.ActorName)) context.Add("人物 " + item.ActorName);
			if (!string.IsNullOrWhiteSpace(item.FamilyName)) context.Add("家族 " + item.FamilyName);
			if (!string.IsNullOrWhiteSpace(item.SectName)) context.Add("宗门 " + item.SectName);
			if (context.Count > 0) GUILayout.Label("<color=grey>" + Rich(string.Join("　·　", context)) + "</color>");

			if (item.ActorId > 0L || item.FamilyId > 0L || item.SectId > 0L)
			{
				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				if (item.ActorId > 0L && GUILayout.Button("阅列传", GUILayout.Width(88f), GUILayout.Height(32f))) ShowHistoryActor(item.ActorId);
				if (item.FamilyId > 0L && GUILayout.Button("阅世家", GUILayout.Width(88f), GUILayout.Height(32f))) ShowHistoryFamily(item.FamilyId);
				if (item.SectId > 0L && GUILayout.Button("阅宗门", GUILayout.Width(88f), GUILayout.Height(32f))) ShowHistorySect(item.SectId);
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
			GUILayout.Space(4f);
		}
		if (events.Count > count) GUILayout.Label("<color=grey>本卷另有 " + (events.Count - count) + " 件史事，请切换“本卷大事”阅览。</color>");
	}

private void DrawCenturySummarySectionOrEmpty(string title, IReadOnlyList<XjCodexCenturySummaryItem> items, string color, int maxCount)
	{
		if (items == null || items.Count == 0)
		{
			GUILayout.Label("<b>" + Rich(title) + "</b>");
			DrawEmptyCard("本卷没有" + title + "记录。", "#777777");
			return;
		}
		DrawCenturySummarySection(title, items, color, maxCount);
	}

private void DrawCenturyActorsSection(IReadOnlyList<XjCodexCenturyActorItem> actors)
	{
		DrawCenturyActorsSection(actors, 10);
	}

	private void DrawCenturyActorsSection(IReadOnlyList<XjCodexCenturyActorItem> actors, int maxCount)
	{
		if (actors == null || actors.Count == 0 || maxCount <= 0) return;
		GUILayout.Space(6f);
		DrawOrnamentDivider("#B7A7FF", "代 表 人 物");
		int count = Mathf.Min(actors.Count, maxCount);
		for (int i = 0; i < count; )
		{
			GUILayout.BeginHorizontal();
			int row = 0;
			while (i < count && row < 2)
			{
				XjCodexCenturyActorItem actor = actors[i++];
				if (actor == null) continue;
				DrawCenturyActorCard(actor);
				row++;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(5f);
		}
		if (actors.Count > count) GUILayout.Label("<color=grey>本卷另有 " + (actors.Count - count) + " 位人物未展开。</color>");
	}

	private void DrawCenturyActorCard(XjCodexCenturyActorItem actor)
	{
		string color = CenturyRealmColor(actor?.Realm);
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(485f), GUILayout.Height(132f));
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=18>" + Rich(Empty(actor.ActorName, "未名修士")) + "</size></b>");
		GUILayout.FlexibleSpace();
		GUILayout.Label("<color=" + color + "><b>" + Rich(XjDisplayNameSanitizer.GameTerm(actor.Realm, "境界未载")) + "</b></color>", GUILayout.Width(110f));
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=#A7E08A>家族</color> " + Rich(Empty(actor.FamilyName, "未载"))
			+ "　<color=#FFD37A>宗门</color> " + Rich(Empty(actor.SectName, "未载")));
		GUILayout.Label("<color=grey>入谱因由：</color>" + Rich(Empty(actor.Reason, "本世纪留名")));
		GUILayout.BeginHorizontal();
		GUILayout.Label("<color=grey>玄鉴照录 · 人物卷</color>");
		GUILayout.FlexibleSpace();
		if (actor.ActorId > 0L && GUILayout.Button("阅其列传", GUILayout.Width(106f), GUILayout.Height(36f)))
		{
			ShowHistoryActor(actor.ActorId);
		}
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		GUILayout.Space(5f);
	}

private void DrawCenturyActorsSectionOrEmpty(IReadOnlyList<XjCodexCenturyActorItem> actors)
	{
		if (actors == null || actors.Count == 0)
		{
			GUILayout.Label("<b>代表人物</b>");
			DrawEmptyCard("本卷没有代表人物记录。", "#777777");
			return;
		}
		DrawCenturyActorsSection(actors);
	}

private static string CenturyStageColor(string stage)
	{
		if (stage == XjCenturyFamilyStage.DingSheng) return "#FFD37A";
		if (stage == XjCenturyFamilyStage.XingSheng || stage == XjCenturyFamilyStage.FuZhen) return "#A7E08A";
		if (stage == XjCenturyFamilyStage.FenLie) return "#9CD7FF";
		if (stage == XjCenturyFamilyStage.ZhongShuai) return "#FFAA66";
		if (stage == XjCenturyFamilyStage.FuMie) return "#FF8877";
		return "#9CD7FF";
	}

private static string JoinCenturySummaries(IReadOnlyList<XjCodexCenturySummaryItem> items, int maxCount)
	{
		if (items == null || items.Count == 0) return "未载";
		List<string> parts = new List<string>();
		for (int i = 0; i < items.Count && i < maxCount; i++)
		{
			parts.Add(items[i].Name);
		}
		return parts.Count == 0 ? "未载" : string.Join("、", parts);
	}

private static string JoinCenturyActors(IReadOnlyList<XjCodexCenturyActorItem> items, int maxCount)
	{
		if (items == null || items.Count == 0) return "未载";
		List<string> parts = new List<string>();
		for (int i = 0; i < items.Count && i < maxCount; i++)
		{
			XjCodexCenturyActorItem item = items[i];
			if (item == null || string.IsNullOrWhiteSpace(item.ActorName)) continue;
			parts.Add(item.ActorName + (string.IsNullOrWhiteSpace(item.Realm) ? string.Empty : "（" + item.Realm + "）"));
		}
		return parts.Count == 0 ? "未载" : string.Join("、", parts);
	}

private static void DrawTag(string text, string color)
	{
		GUILayout.Label("<color=" + color + ">●</color> " + Rich(text), GUILayout.Width(112f));
	}

private void DrawFocusMessage()
	{
		if (!string.IsNullOrWhiteSpace(_focusMessage))
		{
			GUILayout.Space(6f);
			GUILayout.Label("<color=#9CD7FF>" + Rich(_focusMessage) + "</color>");
		}
	}

private void DrawActorFocusButton(string label, long actorId, params GUILayoutOption[] options)
	{
		bool oldEnabled = GUI.enabled;
		GUI.enabled = oldEnabled && CanFocusActor(actorId);
		if (GUILayout.Button(label, options))
		{
			_focusMessage = TryFocusActor(actorId) ? "已定位到角色。" : "该角色已经死亡或暂时无法定位。";
		}
		GUI.enabled = oldEnabled;
	}

private void DrawFamilyChronicleButton(XjCodexFamilyItem item, params GUILayoutOption[] options)
	{
		bool oldEnabled = GUI.enabled;
		GUI.enabled = oldEnabled && item != null && item.FamilyId > 0L;
		if (GUILayout.Button("查看纪事", options))
		{
			OpenFamilyArchive(item.FamilyId, "家族纪事");
			_focusMessage = string.Empty;
		}
		GUI.enabled = oldEnabled;
	}

private void DrawFamilyTreasureButton(XjCodexFamilyItem item, params GUILayoutOption[] options)
	{
		bool oldEnabled = GUI.enabled;
		GUI.enabled = oldEnabled && item != null && item.FamilyId > 0L && !string.IsNullOrWhiteSpace(item.FamilyTreasureSummary);
		if (GUILayout.Button("查看重宝", options))
		{
			_familyWarehouseDetailView = "重宝";
			OpenFamilyArchive(item.FamilyId, "家族传承");
			_focusMessage = string.Empty;
		}
		GUI.enabled = oldEnabled;
	}

private void DrawFamilyWarehouseButton(XjCodexFamilyItem item, params GUILayoutOption[] options)
	{
		bool oldEnabled = GUI.enabled;
		GUI.enabled = oldEnabled && item != null && item.FamilyId > 0L;
		if (GUILayout.Button("家族仓库", options))
		{
			_familyWarehouseDetailView = "功法";
			OpenFamilyArchive(item.FamilyId, "家族传承");
			_focusMessage = string.Empty;
		}
		GUI.enabled = oldEnabled;
	}

private static long ResolveFamilyChronicleActorId(XjCodexFamilyItem item)
	{
		if (item == null)
		{
			return 0L;
		}

		long[] ids =
		{
			item.RepresentativeActorId,
			item.ClanLeaderActorId,
			item.HeirActorId,
			item.AncestorActorId
		};
		for (int i = 0; i < ids.Length; i++)
		{
			if (CanFocusActor(ids[i]))
			{
				return ids[i];
			}
		}
		for (int i = 0; i < ids.Length; i++)
		{
			if (ids[i] > 0L)
			{
				return ids[i];
			}
		}
		return 0L;
	}

private static bool CanFocusActor(long actorId)
	{
		return actorId > 0L
			&& XjScheduler.ResolveActor(actorId, out Actor actor)
			&& actor?.data != null
			&& actor.isAlive();
	}

private static string BuildImportanceMarks(int importance)
	{
		return new string('★', Mathf.Clamp(importance, 1, 5));
	}

private static string DescribeHistoryMeta(XjCodexHistoryItem item)
	{
		string meta = string.Empty;
		AppendHistoryMeta(ref meta, "人物", item.ActorName);
		AppendHistoryMeta(ref meta, "相关人物", item.RelatedActorName);
		AppendHistoryMeta(ref meta, "地点", item.Location);
		AppendHistoryMeta(ref meta, "宗门", item.SectName);
		AppendHistoryMeta(ref meta, "相关宗门", item.RelatedSectName);
		AppendHistoryMeta(ref meta, "家族", item.FamilyName);
		AppendHistoryMeta(ref meta, "相关家族", item.RelatedFamilyName);
		return meta;
	}

private static void AppendHistoryMeta(ref string meta, string label, string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return;
		if (!string.IsNullOrEmpty(meta)) meta += "    ";
		meta += label + "：" + value;
	}

private static bool TryFocusHistoryActor(long actorId)
	{
		return TryFocusActor(actorId);
	}

private static bool TryFocusActor(long actorId)
	{
		if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()) return false;
		try
		{
			SelectedUnit.select(actor);
			ActionLibrary.openUnitWindow(actor);
			World.world?.locatePosition(new Vector3(actor.current_position.x, actor.current_position.y, 0f));
			return true;
		}
		catch { return false; }
	}

private static bool TryFocusHistoryLocation(XjCodexHistoryItem item)
	{
		if (item == null || !item.HasLocation || World.world == null) return false;
		try
		{
			World.world.locatePosition(new Vector3(item.LocationX, item.LocationY, 0f));
			return true;
		}
		catch { return false; }
	}
}
