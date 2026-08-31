using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Doctrine;
using XuanJianVNext.Data.History;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Doctrine;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Codex;

internal static partial class XjCodexSnapshotPublisher
{
	private static XjCodexCultivationClimate BuildCultivationClimate(
		int worldYear,
		int totalCultivators,
		IReadOnlyList<XjCodexHistoryItem> history,
		IReadOnlyList<XjCodexCenturyAnnalsItem> centuryAnnals)
	{
		XjCodexCultivationClimate result = new XjCodexCultivationClimate
		{
			ObservationYear = Math.Max(0, worldYear),
			Total = Math.Max(0, totalCultivators),
			RecentWindowStartYear = Math.Max(XjChronology.BaseWorldYear, Math.Max(0, worldYear - 99))
		};

		if (XjAnnualWorldDetectionStore.TryReadLatest(
				out int observationYear,
				out IReadOnlyList<XjCenturySummaryItemRecord> realmStatistics,
				out int observedCultivatorCount,
				out int ziFuJinDanPathCount,
				out int fuQiYangXingPathCount))
		{
			result.HasAnnualObservation = true;
			result.ObservationYear = Math.Max(0, observationYear);
			result.Total = Math.Max(0, observedCultivatorCount);
			result.TaiXi = ReadStatistic(realmStatistics, "taixi");
			result.LianQi = ReadStatistic(realmStatistics, "lianqi");
			result.ZhuJi = ReadStatistic(realmStatistics, "zhuji");
			result.HuangGuan = ReadStatistic(realmStatistics, "huangguan");
			result.ZhenRen = ReadStatistic(realmStatistics, "zhenren");
			result.ZhenJun = ReadStatistic(realmStatistics, "zhenjun");
			result.ZhenJun = Math.Max(result.ZhenJun, XjCultivatorCache.GetZhenJunOrHigherIds().Count);
			result.ShenDan = ReadStatistic(realmStatistics, "shendan");
			result.JieLin = ReadStatistic(realmStatistics, "jielin");
			result.ZiFuJinDanPath = ziFuJinDanPathCount;
			result.FuQiYangXingPath = fuQiYangXingPathCount;
			result.ShiPath = ReadStatistic(realmStatistics, "shi");
			result.AncientShi = ReadStatistic(realmStatistics, "shi_ancient");
			result.ModernShi = ReadStatistic(realmStatistics, "shi_modern");
			result.ShiMonk = ReadStatistic(realmStatistics, "shi_monk");
			result.ShiDharmaMaster = ReadStatistic(realmStatistics, "shi_dharma_master");
			result.ShiLianMin = ReadStatistic(realmStatistics, "shi_lianmin");
			result.ShiMoHe = ReadStatistic(realmStatistics, "shi_mohe");
			result.ShiDharmaForm = ReadStatistic(realmStatistics, "shi_dharma_form");
			result.ShiWorldHonored = ReadStatistic(realmStatistics, "shi_world_honored");
			int entered = result.TaiXi + result.LianQi + result.ZhuJi + result.HuangGuan
				+ result.ZhenRen + result.ZhenJun + result.ShiPath;
			result.Unentered = Math.Max(0, result.Total - entered);
			result.Trends = BuildCultivationTrends(result, centuryAnnals, out string baseline);
			result.TrendBaseline = baseline;
		}
		else
		{
			// 统一年度检测尚未生成时只展示缓存总数，不为打开仙鉴补跑一次
			// 全修士统计。第一份年度快照完成后会自动补齐各境人数。
			result.HasAnnualObservation = false;
			result.ObservationYear = 0;
			result.Trends = Array.Empty<XjCodexCultivationTrendItem>();
			result.TrendBaseline = "尚无可比较的百年卷";
		}

		CountRecentHighRealmEvents(history, worldYear, result);
		result.DoctrineRelations = BuildDoctrineRelations(worldYear);
		result.DoctrineRecentEvents = BuildDoctrineRecentEvents();
		result.Summary = BuildCultivationClimateSummary(result);
		return result;
	}

	private static IReadOnlyList<XjCodexDoctrineRelationItem> BuildDoctrineRelations(int worldYear)
	{
		IReadOnlyList<XjDoctrineRelationSnapshot> source = XjDoctrineConflictSystem.ReadRelationSnapshot(worldYear);
		if (source == null || source.Count == 0) return Array.Empty<XjCodexDoctrineRelationItem>();
		List<XjCodexDoctrineRelationItem> result = new List<XjCodexDoctrineRelationItem>(source.Count);
		for (int i = 0; i < source.Count; i++)
		{
			XjDoctrineRelationSnapshot item = source[i];
			if (item == null) continue;
			result.Add(new XjCodexDoctrineRelationItem
			{
				SourceDoctrineId = item.SourceDoctrineId,
				SourceDoctrineName = item.SourceDoctrineName,
				TargetDoctrineId = item.TargetDoctrineId,
				TargetDoctrineName = item.TargetDoctrineName,
				BaseHostility = item.BaseHostility,
				Grievance = item.Grievance,
				FinalHostility = item.FinalHostility,
				Status = item.Status,
				LastChangedYear = item.LastChangedYear,
				LastReason = item.LastReason
			});
		}
		return result;
	}

	private static IReadOnlyList<XjCodexDoctrineConflictEventItem> BuildDoctrineRecentEvents()
	{
		IReadOnlyList<XjDoctrineConflictEventSnapshot> source = XjDoctrineConflictSystem.ReadRecentEventSnapshot(6);
		if (source == null || source.Count == 0) return Array.Empty<XjCodexDoctrineConflictEventItem>();
		List<XjCodexDoctrineConflictEventItem> result = new List<XjCodexDoctrineConflictEventItem>(source.Count);
		for (int i = 0; i < source.Count; i++)
		{
			XjDoctrineConflictEventSnapshot item = source[i];
			if (item == null) continue;
			result.Add(new XjCodexDoctrineConflictEventItem
			{
				Year = item.Year,
				SourceDoctrineId = item.SourceDoctrineId,
				SourceDoctrineName = item.SourceDoctrineName,
				TargetDoctrineId = item.TargetDoctrineId,
				TargetDoctrineName = item.TargetDoctrineName,
				Delta = item.Delta,
				Reason = item.Reason
			});
		}
		return result;
	}

	private static int ReadStatistic(IReadOnlyList<XjCenturySummaryItemRecord> items, string key)
	{
		if (items == null || string.IsNullOrWhiteSpace(key)) return 0;
		for (int i = 0; i < items.Count; i++)
		{
			XjCenturySummaryItemRecord item = items[i];
			if (item != null && string.Equals(item.Key, key, StringComparison.Ordinal))
			{
				return Math.Max(0, item.Score);
			}
		}
		return 0;
	}

	private static IReadOnlyList<XjCodexCultivationTrendItem> BuildCultivationTrends(
		XjCodexCultivationClimate current,
		IReadOnlyList<XjCodexCenturyAnnalsItem> centuryAnnals,
		out string baseline)
	{
		baseline = "尚无可比较的百年卷";
		XjCodexCenturyAnnalsItem previous = null;
		if (centuryAnnals != null)
		{
			for (int i = centuryAnnals.Count - 1; i >= 0; i--)
			{
				XjCodexCenturyAnnalsItem candidate = centuryAnnals[i];
				if (candidate == null || !candidate.IsCompleteCycle
					|| candidate.RealmStatistics == null || candidate.RealmStatistics.Count == 0)
				{
					continue;
				}
				if (candidate.GeneratedYear < XjChronology.ToXuanJianYear(current.ObservationYear))
				{
					previous = candidate;
					break;
				}
			}
		}
		if (previous == null) return Array.Empty<XjCodexCultivationTrendItem>();

		baseline = string.IsNullOrWhiteSpace(previous.Title)
			? previous.StartYear + "—" + previous.EndYear + "年百年卷"
			: previous.Title;
		List<XjCodexCultivationTrendItem> result = new List<XjCodexCultivationTrendItem>(9);
		AddTrend(result, previous.RealmStatistics, "taixi", "胎息", current.TaiXi);
		AddTrend(result, previous.RealmStatistics, "lianqi", "炼气", current.LianQi);
		AddTrend(result, previous.RealmStatistics, "zhuji", "筑基", current.ZhuJi);
		AddTrend(result, previous.RealmStatistics, "huangguan", "黄冠", current.HuangGuan);
		AddTrend(result, previous.RealmStatistics, "zhenren", "真人", current.ZhenRen);
		AddTrend(result, previous.RealmStatistics, "zhenjun", "真君", current.ZhenJun);
		AddTrend(result, previous.RealmStatistics, "shi", "释修", current.ShiPath);
		AddTrend(result, previous.RealmStatistics, "shi_ancient", "古释", current.AncientShi);
		AddTrend(result, previous.RealmStatistics, "shi_modern", "今释", current.ModernShi);
		return result;
	}

	private static void AddTrend(
		ICollection<XjCodexCultivationTrendItem> result,
		IReadOnlyList<XjCodexCenturySummaryItem> previous,
		string key,
		string name,
		int current)
	{
		int oldValue = ReadCenturyStatistic(previous, key);
		result.Add(new XjCodexCultivationTrendItem
		{
			Key = key,
			Name = name,
			Current = Math.Max(0, current),
			Previous = oldValue,
			Delta = current - oldValue
		});
	}

	private static int ReadCenturyStatistic(IReadOnlyList<XjCodexCenturySummaryItem> items, string key)
	{
		if (items == null) return 0;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexCenturySummaryItem item = items[i];
			if (item != null && string.Equals(item.Key, key, StringComparison.Ordinal))
			{
				return Math.Max(0, item.Score);
			}
		}
		return 0;
	}

	private static void CountRecentHighRealmEvents(
		IReadOnlyList<XjCodexHistoryItem> history,
		int worldYear,
		XjCodexCultivationClimate result)
	{
		if (history == null || result == null) return;
		int startYear = Math.Max(XjChronology.BaseWorldYear, Math.Max(0, worldYear - 99));
		HashSet<string> counted = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < history.Count; i++)
		{
			XjCodexHistoryItem item = history[i];
			if (item == null || item.Year < startYear || item.Year > worldYear) continue;
			string eventType = item.EventType ?? string.Empty;
			string text = (item.Title ?? string.Empty) + " " + (item.Body ?? string.Empty);
			bool zhenJunText = ContainsAny(text, "真君", "金丹", "神丹", "结璘", "羽士", "XjRealm5");
			bool zhenRenText = !zhenJunText && ContainsAny(text, "真人", "紫府", "XjRealm4");
			string identity = item.ActorId > 0L
				? item.ActorId.ToString()
				: (item.ActorName ?? string.Empty) + "|" + (item.Title ?? string.Empty);

			if (string.Equals(eventType, "JinDanSucceeded", StringComparison.Ordinal)
				|| string.Equals(eventType, "ShenDanSucceeded", StringComparison.Ordinal)
				|| string.Equals(eventType, "JieLinSucceeded", StringComparison.Ordinal)
				|| string.Equals(eventType, "RealmBreakthrough", StringComparison.Ordinal) && zhenJunText)
			{
				if (counted.Add("new_jun|" + item.Year + "|" + identity)) result.NewZhenJun++;
				continue;
			}
			if (string.Equals(eventType, "RealmBreakthrough", StringComparison.Ordinal) && zhenRenText)
			{
				if (counted.Add("new_ren|" + item.Year + "|" + identity)) result.NewZhenRen++;
				continue;
			}

			bool death = string.Equals(item.Result, XjHistoryResult.Death, StringComparison.Ordinal)
				|| eventType.EndsWith("Death", StringComparison.Ordinal)
				|| string.Equals(eventType, "ActorDeath", StringComparison.Ordinal);
			if (!death) continue;
			if (zhenJunText)
			{
				if (counted.Add("fall_jun|" + item.Year + "|" + identity)) result.FallenZhenJun++;
			}
			else if (zhenRenText)
			{
				if (counted.Add("fall_ren|" + item.Year + "|" + identity)) result.FallenZhenRen++;
			}
		}
	}

	private static string BuildCultivationClimateSummary(XjCodexCultivationClimate item)
	{
		if (item == null || item.Total <= 0)
		{
			return "天下尚未形成可照录的修行传承。";
		}
		if (!item.HasAnnualObservation)
		{
			return "当前仅确认在录修士" + item.Total + "人；统一年度观测尚未完成，境界结构将在下一次检测后入鉴。";
		}

		int low = item.Unentered + item.TaiXi + item.LianQi;
		int middle = item.ZhuJi + item.HuangGuan;
		string structure = middle >= low && middle >= item.ZhenRen + item.ZhenJun
			? "天下修士多聚于筑基、黄冠等中境"
			: low >= middle
				? "天下修士仍多处于未入道、胎息与炼气"
				: "高境修士在当世修道结构中占比显著";
		string highRealm = item.ZhenJun <= 0
			? item.ZhenRen > 0
				? "真人传承尚能维持，但当世未见真君"
				: "真人、真君传承均见断层"
			: item.ZhenRen <= 0
				? "虽有真君坐镇，真人后继却显单薄"
				: "真人与真君传承均有承续";
		string path;
		if (item.ShiPath > 0)
			path = "；修道格局按紫金、服气、古释、今释四道统计，古释" + item.AncientShi + "人、今释" + item.ModernShi + "人";
		else if (item.FuQiYangXingPath > 0) path = "；紫府金丹与服气养性两路皆已入世";
		else path = string.Empty;
		return structure + "；" + highRealm + path + "。";
	}

	private static bool ContainsAny(string text, params string[] tokens)
	{
		if (string.IsNullOrWhiteSpace(text) || tokens == null) return false;
		for (int i = 0; i < tokens.Length; i++)
		{
			if (!string.IsNullOrEmpty(tokens[i])
				&& text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}
}
