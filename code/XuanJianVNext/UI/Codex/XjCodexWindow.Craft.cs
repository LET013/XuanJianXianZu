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
			DrawCraftTreasurePage(snapshot);
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
			int columns = ResolveCraftCatalogColumns();
			float width = ResolveCraftCatalogCardWidth(columns);
			for (int i = 0; i < pills.Count; i += columns)
			{
				GUILayout.BeginHorizontal();
				for (int c = 0; c < columns && i + c < pills.Count; c++)
				{
					DrawPillCatalogCard(pills[i + c], width);
				}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
		GUILayout.EndVertical();
		GUILayout.Space(8f);
	}

private void DrawPillCatalogCard(XjAlchemyPillDef pill, float width)
		{
			if (pill == null) return;
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width));
		GUILayout.Label("<b><color=#A7E08A>" + Rich(XjAlchemyText.PillName(pill.Id)) + "</color></b>");
		GUILayout.Label("修法：" + Rich(AlchemyPathDisplay(pill.PathAffinity))
			+ "    适用：" + Rich(FormatRealmRange(pill.MinApplicableRealmId, pill.MaxApplicableRealmId)));
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
			int columns = ResolveCraftCatalogColumns();
			float width = ResolveCraftCatalogCardWidth(columns);
			for (int i = 0; i < talismans.Count; i += columns)
			{
				GUILayout.BeginHorizontal();
				for (int c = 0; c < columns && i + c < talismans.Count; c++)
				{
					DrawTalismanCatalogCard(talismans[i + c], width);
				}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
		GUILayout.EndVertical();
		GUILayout.Space(8f);
	}

private void DrawTalismanCatalogCard(XjTalismanDefinition talisman, float width)
		{
			if (talisman == null) return;
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width));
		GUILayout.Label("<b><color=#9CD7FF>" + Rich(talisman.DisplayName) + "</color></b>");
		GUILayout.Label("层次：" + Rich(RealmDisplay(talisman.MinRealmId))
			+ "    成符把握：" + DescribeLoreRatio(talisman.BaseSuccessRate));
		GUILayout.Label("基础产量：" + talisman.MinYield + "-" + talisman.MaxYield + "张    <color=grey>成品效果固定</color>");
		GUILayout.Label("<color=grey>" + Rich(ShortenCatalogText(XjTalismanCatalog.EffectSummary(talisman.Id), 44)) + "</color>");
			GUILayout.EndVertical();
		}

private int ResolveCraftCatalogColumns()
		{
			return ContentWidth < 900f ? 1 : ContentWidth < 1320f ? 2 : 3;
		}

private float ResolveCraftCatalogCardWidth(int columns)
		{
			return Mathf.Max(280f, (ContentWidth - 120f) / Math.Max(1, columns));
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
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return 3;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return 4;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return 5;
		return 0;
	}

private static string AlchemyPathDisplay(XjAlchemyCultivationPath path)
{
	return path switch
	{
		XjAlchemyCultivationPath.ZiJin => "紫府金丹道",
		XjAlchemyCultivationPath.FuQi => "服气养性",
		_ => "两路通用"
	};
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
			return taskName + "至" + XjChronology.FormatYear(item.TaskDueYear);
		}
		return taskName;
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
		if (HasToken(realm, "道胎")) return 6;
		if (HasToken(realm, "金丹") || HasToken(realm, "神丹") || HasToken(realm, "真君")) return 5;
		if (HasToken(realm, "紫府") || HasToken(realm, "真人")) return 4;
		if (HasToken(realm, "筑基") || HasToken(realm, "黄冠")) return 3;
		if (HasToken(realm, "炼气")) return 2;
		if (HasToken(realm, "胎息")) return 1;
		return 0;
	}

private sealed class CraftProfessionSummary
	{
		internal int Total;
		internal int Busy;
		internal string TopRealm = string.Empty;
	}
}
