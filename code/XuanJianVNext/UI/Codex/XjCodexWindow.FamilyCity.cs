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
{		private void DrawFamilyChronicleDetail(XjCodexSnapshot snapshot)
		{
			XjCodexFamilyItem item = FindFamily(snapshot, _familyChronicleDetailFamilyId);
			string familyName = item != null ? item.Name : "未名氏";
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("返回家族诸脉", GUILayout.Width(140f), GUILayout.Height(36f)))
			{
				_familyChronicleDetailFamilyId = 0L;
				_scrollPosition = Vector2.zero;
				return;
			}
			GUILayout.Label("<b><size=22>" + Rich(familyName) + "</size></b>", GUILayout.Width(260f));
			GUILayout.EndHorizontal();
			GUILayout.Space(5f);
			DrawFamilyChronicleFilter();
	
			IReadOnlyList<XjChronicleEvent> events = XjChronicleReadModel.Shared.ReadFamilyChronicle(_familyChronicleDetailFamilyId);
			if (events.Count == 0)
			{
				DrawEmptyCard("该家族尚无纪事。", "#777777");
				return;
			}
	
			int shown = 0;
			int matched = 0;
			for (int i = events.Count - 1; i >= 0; i--)
			{
				XjChronicleEvent entry = events[i];
				if (entry == null || !entry.Found)
				{
					continue;
				}
	
				string label = ResolveFamilyChronicleLabel(entry.EventType);
				if (!string.Equals(_familyChronicleFilter, "全部", StringComparison.Ordinal)
					&& !string.Equals(_familyChronicleFilter, label, StringComparison.Ordinal))
				{
					continue;
				}
	
				matched++;
				if (shown >= MaxRenderedFamilyChronicleItems)
				{
					continue;
				}
	
				GUILayout.BeginVertical(GUI.skin.box);
				DrawCardStripe(ResolveFamilyChronicleColor(label));
				GUILayout.BeginHorizontal();
				GUILayout.Label("<b>" + Rich(label) + "</b>", GUILayout.Width(80f));
				GUILayout.Label(XjChronology.FormatYear(entry.Timestamp), GUILayout.Width(130f));
				GUILayout.Label("<b>" + Rich(Empty(entry.Title, "族事入卷")) + "</b>");
				if (entry.ActorId > 0L) DrawActorFocusButton("定位角色", entry.ActorId, GUILayout.Width(105f), GUILayout.Height(28f));
				GUILayout.EndHorizontal();
				if (!string.IsNullOrWhiteSpace(entry.Body))
				{
					GUILayout.Label(Rich(entry.Body));
				}
				GUILayout.EndVertical();
				GUILayout.Space(4f);
				shown++;
			}
	
			if (matched == 0) DrawEmptyCard("当前分类下没有纪事。", "#777777");
			DrawListLimitNotice(matched, shown);
		}

private void DrawFamilyChronicleFilter()
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label("纪事分类", GUILayout.Width(80f));
			for (int i = 0; i < FamilyChronicleFilters.Length; i++)
			{
				string filter = FamilyChronicleFilters[i];
				GUI.backgroundColor = string.Equals(_familyChronicleFilter, filter, StringComparison.Ordinal) ? new Color(0.28f, 0.28f, 0.28f) : Color.gray;
				if (GUILayout.Button(filter, GUILayout.Width(80f), GUILayout.Height(32f)))
				{
					_familyChronicleFilter = filter;
					_focusMessage = string.Empty;
				}
			}
			GUI.backgroundColor = Color.white;
			GUILayout.EndHorizontal();
		}

private static XjCodexFamilyItem FindFamily(XjCodexSnapshot snapshot, long familyId)
		{
			if (familyId <= 0L) return null;
			if (XjWorldEntityViewModelStore.TryGetFamilyItem(familyId, out XjCodexFamilyItem indexed)) return indexed;
			if (snapshot?.Families == null)
			{
				return null;
			}
	
			for (int i = 0; i < snapshot.Families.Count; i++)
			{
				XjCodexFamilyItem item = snapshot.Families[i];
				if (item != null && item.FamilyId == familyId)
				{
					return item;
				}
			}
			return null;
		}

private static string ResolveFamilyChronicleLabel(string eventType)
		{
			return eventType switch
			{
				"BreakthroughSuccess:JinDan" or "BreakthroughSuccess:ZiFu" or "BreakthroughSuccess:ZhuJi"
					or "RealmBreakthrough" or "JinDanSucceeded" or "ShenDanSucceeded" or "JieLinSucceeded" or "JinXingObtained" => "破境",
				"GongFaGenerated" or "CaiQiFaObtained" or "QiuJinFaComprehended" or "FaBaoObtained"
					or "FaBaoUpgraded" or "ZongMenBackflow" or "GongFaLost" or "LingWuAppeared"
					or "JinDanResidualAcquired" => "传承",
				"ActorDied" or "JinDanFailureDemonized" or "DongTianDeath" or "JinDanResidualAppeared" => "身故",
				"FamilyVendettaCreated" or "FamilyVendettaAvenged" or "FamilyVendettaFailed" or "FamilyVendettaCounterKilled" or "FamilyVendettaClosed" => "族仇",
				"HighTierSpellTriggered" or "RenDanRefined" or "BreakthroughBlocked"
					or "DongTianSurvived" => "斗法",
				_ => "族事"
			};
		}

private static string ResolveFamilyChronicleColor(string label)
		{
			return label switch
			{
				"破境" => "#FFD37A",
				"传承" => "#A7E08A",
				"身故" => "#999999",
				"族仇" => "#FF8877",
				"斗法" => "#B7A7FF",
				_ => "#9CD7FF"
			};
		}

private void DrawFamilyRealmFilter()
		{
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.BeginHorizontal();
			GUILayout.Label("境界筛选", GUILayout.Width(90f), GUILayout.Height(38f));
			for (int i = 0; i < FamilyRealmFilters.Length; i++)
			{
				string filter = FamilyRealmFilters[i];
				Color old = GUI.backgroundColor;
				GUI.backgroundColor = string.Equals(_familyRealmFilter, filter, StringComparison.Ordinal)
					? new Color(0.32f, 0.38f, 0.45f)
					: new Color(0.22f, 0.22f, 0.22f);
				if (GUILayout.Button(filter, GUILayout.Width(92f), GUILayout.Height(38f)))
				{
					_familyRealmFilter = filter;
					_scrollPosition = Vector2.zero;
				}
				GUI.backgroundColor = old;
			}
			GUILayout.Space(18f);
			GUILayout.Label("搜索", GUILayout.Width(42f), GUILayout.Height(38f));
			string nextSearch = GUILayout.TextField(_familySearchText ?? string.Empty, 24, GUILayout.Width(270f), GUILayout.Height(38f));
			if (!string.Equals(nextSearch, _familySearchText, StringComparison.Ordinal))
			{
				_familySearchText = nextSearch ?? string.Empty;
				_scrollPosition = Vector2.zero;
			}
			if (!string.IsNullOrWhiteSpace(_familySearchText)
				&& GUILayout.Button("清空", GUILayout.Width(68f), GUILayout.Height(38f)))
			{
				_familySearchText = string.Empty;
				_scrollPosition = Vector2.zero;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			GUILayout.Space(7f);
		}

private bool MatchesFamilyRealmFilter(XjCodexFamilyItem item)
		{
			if (item == null)
			{
				return false;
			}
			string filter = string.IsNullOrWhiteSpace(_familyRealmFilter) ? "全部" : _familyRealmFilter.Trim();
			if (filter == "全部")
			{
				return true;
			}
			if (filter == "余脉")
			{
				return item.CultivatorCount <= 0 && item.HistoricalCultivatorCount > 0;
			}
			if (filter == "真人")
			{
				return item.ZiFuCount > 0;
			}
			if (filter == "真君")
			{
				return item.JinDanCount > 0;
			}
			if (filter == "释修")
			{
				return item.ShiCount > 0;
			}
			return ContainsRealm(item.HighestRealm ?? string.Empty, filter);
		}

private bool MatchesFamilySearchFilter(XjCodexFamilyItem item)
		{
			if (item == null)
			{
				return false;
			}
			string query = NormalizeSearchText(_familySearchText);
			if (string.IsNullOrWhiteSpace(query))
			{
				return true;
			}

			return ContainsSearch(item.Name, query)
				|| ContainsSearch(item.SectName, query)
				|| ContainsSearch(item.GoverningCities, query)
				|| ContainsSearch(item.Representative, query)
				|| ContainsSearch(item.AncestorName, query)
				|| ContainsSearch(item.ClanLeaderName, query)
				|| ContainsSearch(item.HeirName, query)
				|| ContainsSearch(item.HighestRealm, query)
				|| ContainsSearch(item.HistoricalHighestRealm, query)
				|| ContainsSearch(item.LineageState, query)
				|| ContainsSearch(item.VoiceTier, query)
				|| ContainsSearch(item.CityOfficeSummary, query)
				|| ContainsSearch(item.BiographySummary, query);
		}

private static bool ContainsSearch(string value, string normalizedQuery)
		{
			return !string.IsNullOrWhiteSpace(normalizedQuery)
				&& NormalizeSearchText(value).IndexOf(normalizedQuery, StringComparison.Ordinal) >= 0;
		}

private static string NormalizeSearchText(string value)
		{
			return string.IsNullOrWhiteSpace(value)
				? string.Empty
				: value.Trim().Replace(" ", string.Empty).ToLowerInvariant();
		}

private static bool IsVisibleFamily(XjCodexFamilyItem item)
		{
			return item != null && item.AliveCount > 0
				&& (item.CultivatorCount > 0 || item.HistoricalCultivatorCount > 0);
		}

private static bool ContainsRealm(string realm, string token)
		{
			return !string.IsNullOrWhiteSpace(realm)
				&& !string.IsNullOrWhiteSpace(token)
				&& realm.IndexOf(token, StringComparison.Ordinal) >= 0;
		}

private void DrawCityFilter()
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label("城镇筛选", GUILayout.Width(90f));
			for (int i = 0; i < CityFilters.Length; i++)
			{
				string filter = CityFilters[i];
				Color old = GUI.backgroundColor;
				GUI.backgroundColor = string.Equals(_cityFilter, filter, StringComparison.Ordinal)
					? new Color(0.32f, 0.38f, 0.45f)
					: new Color(0.22f, 0.22f, 0.22f);
				if (GUILayout.Button(filter, GUILayout.Width(105f), GUILayout.Height(32f)))
				{
					_cityFilter = filter;
					_scrollPosition = Vector2.zero;
				}
				GUI.backgroundColor = old;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(8f);
		}

private bool MatchesCityFilter(XjCodexCityItem item)
		{
			if (item == null) return false;
			if (_cityTargetId > 0L) return item.CityId == _cityTargetId;
			if (string.Equals(_cityFilter, "宗门城", StringComparison.Ordinal)) return item.SectId > 0L;
			if (string.Equals(_cityFilter, "有阵法", StringComparison.Ordinal)) return item.FormationGrade > 0;
			if (string.Equals(_cityFilter, "治理争议", StringComparison.Ordinal)) return item.ChallengerFamilyId > 0L || string.Equals(item.GovernanceState, XjCityFamilyGovernanceState.Challenged, StringComparison.Ordinal) || string.Equals(item.GovernanceState, "Contested", StringComparison.Ordinal);
			if (string.Equals(_cityFilter, "有望族", StringComparison.Ordinal)) return item.GoverningFamilyId > 0L;
			return true;
		}
}
