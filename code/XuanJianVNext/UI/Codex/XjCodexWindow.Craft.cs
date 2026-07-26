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
using XuanJianVNext.Data.Rules;
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
private void DrawCraftPage(XjCodexSnapshot snapshot)
	{
		DrawPageHeader("玄鉴四艺", "丹道、器道、符箓、阵法各成一艺，观天下百工修士所执之业。");
		Dictionary<string, CraftProfessionSummary> summaries = BuildCraftProfessionSummaries(snapshot.CraftActors);
		GUILayout.BeginHorizontal();
		DrawOverviewPill("炼丹师", CountCraftSummary(summaries, "炼丹师").ToString(), "#A7E08A", GUILayout.Width(145f));
		DrawOverviewPill("炼器师", CountCraftSummary(summaries, "炼器师").ToString(), "#FFD37A", GUILayout.Width(145f));
		DrawOverviewPill("符箓师", CountCraftSummary(summaries, "符箓师").ToString(), "#9CD7FF", GUILayout.Width(145f));
		DrawOverviewPill("阵法师", CountCraftSummary(summaries, "阵法师").ToString(), "#B7A7FF", GUILayout.Width(145f));
		DrawOverviewPill("进行中任务", snapshot.OpenCraftTaskCount.ToString(), "#FFAA66", GUILayout.Width(165f));
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);

		DrawCraftCatalogButtons();
		DrawCraftCatalogPanel();
		DrawCraftProfessionFilter();
		if (snapshot.CraftActors.Count == 0) DrawEmptyCard("尚无角色获得玄鉴百艺。", "#777777");
		int matched = 0;
		int shown = 0;
		for (int i = 0; i < snapshot.CraftActors.Count; i++)
		{
			XjCodexCraftItem item = snapshot.CraftActors[i];
			if (!MatchesCraftProfessionFilter(item))
			{
				continue;
			}
			matched++;
			if (shown >= MaxRenderedCraftActorsPerPage)
			{
				continue;
			}
			string color = ProfessionColor(item.ProfessionName);
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(color);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=21>" + Rich(item.ActorName) + "</size></b>", GUILayout.Width(260f));
			DrawMiniStat("百艺", item.ProfessionName, color, GUILayout.Width(125f));
			DrawMiniStat("品级", Empty(item.CraftRank, "未入艺"), color, GUILayout.Width(165f));
			DrawMiniStat("所在", Empty(item.CityName, "游离"), "#9CD7FF", GUILayout.Width(160f));
			DrawMiniStat("当前任务", FormatCraftTaskDisplay(item), string.IsNullOrWhiteSpace(item.TaskKind) ? "#888888" : "#FFAA66", GUILayout.Width(230f));
			DrawActorFocusButton("定位", item.ActorId, GUILayout.Width(80f));
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			GUILayout.Space(5f);
			shown++;
		}
		if (matched == 0 && snapshot.CraftActors.Count > 0) DrawEmptyCard("当前筛选下暂无百艺角色。", "#777777");
		DrawListLimitNotice(matched, shown);
	}

private void DrawCraftCatalogButtons()
	{
		GUILayout.BeginHorizontal();
		Color old = GUI.backgroundColor;
		GUI.backgroundColor = string.Equals(_craftCatalogView, "丹药", StringComparison.Ordinal)
			? new Color(0.25f, 0.42f, 0.25f)
			: new Color(0.22f, 0.30f, 0.22f);
		if (GUILayout.Button("丹药图录", GUILayout.Width(130f), GUILayout.Height(34f)))
		{
			_craftCatalogView = string.Equals(_craftCatalogView, "丹药", StringComparison.Ordinal) ? string.Empty : "丹药";
			_scrollPosition = Vector2.zero;
		}
		GUI.backgroundColor = string.Equals(_craftCatalogView, "符箓", StringComparison.Ordinal)
			? new Color(0.22f, 0.34f, 0.45f)
			: new Color(0.20f, 0.27f, 0.34f);
		if (GUILayout.Button("符箓图录", GUILayout.Width(130f), GUILayout.Height(34f)))
		{
			_craftCatalogView = string.Equals(_craftCatalogView, "符箓", StringComparison.Ordinal) ? string.Empty : "符箓";
			_scrollPosition = Vector2.zero;
		}
		GUI.backgroundColor = old;
		GUILayout.Label("<color=grey>查看全部丹药、符箓类型与简要效果。</color>");
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
	}

private void DrawCraftCatalogPanel()
	{
		if (string.Equals(_craftCatalogView, "丹药", StringComparison.Ordinal))
		{
			DrawPillCatalogPanel();
		}
		else if (string.Equals(_craftCatalogView, "符箓", StringComparison.Ordinal))
		{
			DrawTalismanCatalogPanel();
		}
	}

private void DrawPillCatalogPanel()
	{
		List<XjAlchemyPillDef> pills = new List<XjAlchemyPillDef>(XjAlchemyCatalog.Pills.Values);
		pills.RemoveAll(pill => pill == null || !pill.ProductionEnabled);
		pills.Sort((left, right) =>
		{
			int realm = RealmOrder(left?.MinApplicableRealmId).CompareTo(RealmOrder(right?.MinApplicableRealmId));
			if (realm != 0) return realm;
			int name = string.Compare(XjAlchemyText.PillName(left?.Id), XjAlchemyText.PillName(right?.Id), StringComparison.Ordinal);
			return name != 0 ? name : string.Compare(left?.Id, right?.Id, StringComparison.Ordinal);
		});

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#A7E08A");
		GUILayout.Label("<b>丹药图录</b> <color=grey>收录当前已登记丹药与服用效果。</color>");
		for (int i = 0; i < pills.Count; i += 3)
		{
			GUILayout.BeginHorizontal();
			for (int c = 0; c < 3 && i + c < pills.Count; c++)
			{
				DrawPillCatalogCard(pills[i + c]);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
		GUILayout.EndVertical();
		GUILayout.Space(8f);
	}

private void DrawPillCatalogCard(XjAlchemyPillDef pill)
	{
		if (pill == null) return;
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(340f));
		GUILayout.Label("<b><color=#A7E08A>" + Rich(XjAlchemyText.PillName(pill.Id)) + "</color></b>");
		GUILayout.Label("适用：" + Rich(FormatRealmRange(pill.MinApplicableRealmId, pill.MaxApplicableRealmId)));
		string detail = XjAlchemyText.PillShortDescription(pill.Id);
		GUILayout.Label("<color=grey>药效：" + Rich(ShortenCatalogText(detail, 44)) + "</color>");
		if (pill.CooldownYears > 0)
		{
			GUILayout.Label("<color=#777777>间隔 " + pill.CooldownYears + " 年可再用</color>");
		}
		GUILayout.EndVertical();
	}

private void DrawTalismanCatalogPanel()
	{
		IReadOnlyList<XjTalismanDefinition> talismans = XjTalismanCatalog.All;
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#9CD7FF");
		GUILayout.Label("<b>符箓图录</b> <color=grey>收录当前已登记符箓与携带效果。</color>");
		for (int i = 0; i < talismans.Count; i += 3)
		{
			GUILayout.BeginHorizontal();
			for (int c = 0; c < 3 && i + c < talismans.Count; c++)
			{
				DrawTalismanCatalogCard(talismans[i + c]);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
		GUILayout.EndVertical();
		GUILayout.Space(8f);
	}

private void DrawTalismanCatalogCard(XjTalismanDefinition talisman)
	{
		if (talisman == null) return;
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(340f));
		GUILayout.Label("<b><color=#9CD7FF>" + Rich(talisman.DisplayName) + "</color></b>");
		GUILayout.Label("层次：" + Rich(RealmDisplay(talisman.MinRealmId))
			+ "    基础成功率：" + Mathf.RoundToInt(talisman.BaseSuccessRate * 100f) + "%");
		GUILayout.Label("基础产量：" + talisman.MinYield + "-" + talisman.MaxYield + "张    <color=grey>成品效果固定</color>");
		GUILayout.Label("<color=grey>" + Rich(ShortenCatalogText(XjTalismanCatalog.EffectSummary(talisman.Id), 44)) + "</color>");
		GUILayout.EndVertical();
	}

private static string FormatRealmRange(string minRealmId, string maxRealmId)
	{
		string min = RealmDisplay(minRealmId);
		string max = RealmDisplay(maxRealmId);
		if (string.IsNullOrWhiteSpace(max) || string.Equals(min, max, StringComparison.Ordinal))
		{
			return min;
		}
		return min + "至" + max;
	}

private static string RealmDisplay(string realmId)
	{
		return XjRealmRules.TryGet(realmId, out XjRealmRule rule)
			? rule.DisplayName
			: Empty(realmId, "未载");
	}

private static int RealmOrder(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return 1;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return 2;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 3;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 4;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)) return 5;
		return 0;
	}

private static string ShortenCatalogText(string value, int maxLength)
	{
		string text = string.IsNullOrWhiteSpace(value) ? "暂无效果说明。" : value.Trim();
		if (maxLength <= 0 || text.Length <= maxLength) return text;
		return text.Substring(0, maxLength).TrimEnd('，', '；', '。', ' ') + "。";
	}

private static string FormatCraftTaskDisplay(XjCodexCraftItem item)
	{
		if (item == null || string.IsNullOrWhiteSpace(item.TaskKind))
		{
			return "空闲";
		}

		string taskName = TranslateCraftTaskKind(item.TaskKind);
		if (item.TaskDueYear > 0)
		{
			return taskName + "至" + item.TaskDueYear + "年";
		}
		return taskName;
	}

private void DrawCraftProfessionFilter()
	{
		GUILayout.BeginHorizontal();
		GUILayout.Label("百艺筛选", GUILayout.Width(90f));
		for (int i = 0; i < CraftProfessionFilters.Length; i++)
		{
			string filter = CraftProfessionFilters[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_craftProfessionFilter, filter, StringComparison.Ordinal)
				? new Color(0.32f, 0.38f, 0.45f)
				: new Color(0.22f, 0.22f, 0.22f);
			if (GUILayout.Button(filter, GUILayout.Width(105f), GUILayout.Height(32f)))
			{
				_craftProfessionFilter = filter;
				_scrollPosition = Vector2.zero;
			}
			GUI.backgroundColor = old;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
	}

private bool MatchesCraftProfessionFilter(XjCodexCraftItem item)
	{
		if (item == null) return false;
		if (string.Equals(_craftProfessionFilter, "任务中", StringComparison.Ordinal)) return !string.IsNullOrWhiteSpace(item.TaskKind);
		if (!string.Equals(_craftProfessionFilter, "全部", StringComparison.Ordinal)) return string.Equals(item.ProfessionName, _craftProfessionFilter, StringComparison.Ordinal);
		return true;
	}

private static string TranslateCraftTaskKind(string taskKind)
	{
		return XjDisplayNameSanitizer.TaskKind(taskKind);
	}

private static int CountCraftSummary(IReadOnlyDictionary<string, CraftProfessionSummary> summaries, string key)
		=> summaries != null && summaries.TryGetValue(key, out CraftProfessionSummary summary) ? summary.Total : 0;

private static Dictionary<string, CraftProfessionSummary> BuildCraftProfessionSummaries(IReadOnlyList<XjCodexCraftItem> actors)
	{
		Dictionary<string, CraftProfessionSummary> result = new Dictionary<string, CraftProfessionSummary>(StringComparer.Ordinal);
		if (actors == null)
		{
			return result;
		}

		for (int i = 0; i < actors.Count; i++)
		{
			XjCodexCraftItem item = actors[i];
			if (item == null)
			{
				continue;
			}
			string profession = Empty(item.ProfessionName, "未定百艺");
			if (!result.TryGetValue(profession, out CraftProfessionSummary summary))
			{
				summary = new CraftProfessionSummary();
				result[profession] = summary;
			}
			summary.Total++;
			if (!string.IsNullOrWhiteSpace(item.TaskKind))
			{
				summary.Busy++;
			}
			if (IsRealmHigher(item.Realm, summary.TopRealm))
			{
				summary.TopRealm = item.Realm;
				summary.TopSectName = item.SectName;
			}
		}
		return result;
	}

private static bool IsRealmHigher(string candidate, string current)
	{
		return RealmRank(candidate) > RealmRank(current);
	}

private static int RealmRank(string realm)
	{
		if (string.IsNullOrWhiteSpace(realm)) return 0;
		if (HasToken(realm, "金丹") || HasToken(realm, "神丹")) return 5;
		if (HasToken(realm, "紫府")) return 4;
		if (HasToken(realm, "筑基")) return 3;
		if (HasToken(realm, "炼气")) return 2;
		if (HasToken(realm, "胎息")) return 1;
		return 0;
	}

private sealed class CraftProfessionSummary
	{
		internal int Total;
		internal int Busy;
		internal string TopRealm = string.Empty;
		internal string TopSectName = string.Empty;
	}
}


