using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// Unified low-frequency world detection snapshot. Expensive whole-cultivator
/// statistics are built once for an annual consumer and then reused by century
/// annals, instead of each display feature scanning the same actors separately.
/// </summary>
internal static class XjAnnualWorldDetectionStore
{
	private static int _snapshotYear;
	private static int _snapshotProgressionRevision = -1;
	private static List<XjCenturySummaryItemRecord> _realmStatistics = new List<XjCenturySummaryItemRecord>();
	private static List<XjCenturySummaryItemRecord> _daoSummaries = new List<XjCenturySummaryItemRecord>();

	internal static int SnapshotYear => _snapshotYear;

	internal static bool ShouldRefresh(int year, int maxAgeYears)
	{
		if (year <= 0)
		{
			return false;
		}
		if (_snapshotYear <= 0)
		{
			return true;
		}
		if (year == _snapshotYear
			&& _snapshotProgressionRevision != XjScheduler.ActorProgressionRevision)
		{
			return true;
		}
		if (year < _snapshotYear)
		{
			return true;
		}
		return year - _snapshotYear >= Math.Max(1, maxAgeYears);
	}

	internal static void Refresh(int year)
	{
		if (year <= 0)
		{
			return;
		}
		int progressionRevision = XjScheduler.ActorProgressionRevision;
		if (_snapshotYear == year && _snapshotProgressionRevision == progressionRevision)
		{
			XjStageZeroObservation.RecordWorldSnapshotDuplicateSkip();
			return;
		}

		Dictionary<string, DaoStat> daoStats = CreateDaoStats();
		int taiXi = 0;
		int lianQi = 0;
		int zhuJi = 0;
		int ziFu = 0;
		int jinDan = 0;
		int shenDan = 0;

		IReadOnlyList<long> actorIds = XjCultivatorCache.GetAllIds();
		for (int i = 0; i < actorIds.Count; i++)
		{
			long actorId = actorIds[i];
			if (actorId <= 0L
				|| !XjScheduler.ResolveActor(actorId, out Actor actor)
				|| actor?.data == null
				|| !actor.isAlive())
			{
				continue;
			}

			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (XjRealmHelper.IsRealm(realmId, "TaiXi")) taiXi++;
			else if (XjRealmHelper.IsRealm(realmId, "LianQi")) lianQi++;
			else if (XjRealmHelper.IsRealm(realmId, "ZhuJi")) zhuJi++;
			else if (XjRealmHelper.IsRealm(realmId, "ZiFu")) ziFu++;
			else if (XjRealmHelper.IsRealm(realmId, "ShenDan")) shenDan++;
			else if (XjRealmHelper.IsRealm(realmId, "JinDan")) jinDan++;

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			daoTu = daoTu?.Trim() ?? string.Empty;
			if (daoTu.Length == 0)
			{
				continue;
			}

			if (!daoStats.TryGetValue(daoTu, out DaoStat stat))
			{
				stat = new DaoStat();
			}
			stat.Count++;
			int order = XjRealmHelper.GetOrder(realmId);
			if (XjRealmHelper.IsRealm(realmId, "ShenDan")) stat.ShenDan++;
			else if (XjRealmHelper.IsRealm(realmId, "JinDan")) stat.JinDan++;
			else if (XjRealmHelper.IsRealm(realmId, "ZiFu")) stat.ZiFu++;
			if (IsTaiYinDaoTu(daoTu) && XjXuanJianShenTongSpecials.IsJieLinXian(actor)) stat.JieLin++;
			stat.Score += Math.Max(1, order + 1);
			daoStats[daoTu] = stat;
		}

		int cultivators = taiXi + lianQi + zhuJi + ziFu + jinDan + shenDan;
		List<XjCenturySummaryItemRecord> realm = new List<XjCenturySummaryItemRecord>(8);
		AddStatistic(realm, "cultivator", "修士总数", cultivators, "总览", "本卷生成时存世修士" + cultivators.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "taixi", "胎息", taiXi, "低境", "胎息修士" + taiXi.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "lianqi", "炼气", lianQi, "低境", "炼气修士" + lianQi.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "zhuji", "筑基", zhuJi, "中境", "筑基修士" + zhuJi.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "zifu", "紫府", ziFu, "高境", "紫府修士" + ziFu.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "jindan", "金丹", jinDan, "高境", "金丹修士" + jinDan.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shendan", "神丹", shenDan, "高境", "神丹修士" + shenDan.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "jielin", "结璘仙", XjJieLinXianRegistry.ActiveCount, "特殊", "结璘仙" + XjJieLinXianRegistry.ActiveCount.ToString(CultureInfo.InvariantCulture) + "人");

		List<XjCenturySummaryItemRecord> dao = BuildDaoSummaries(daoStats);
		_snapshotYear = year;
		_snapshotProgressionRevision = progressionRevision;
		_realmStatistics = realm;
		_daoSummaries = dao;
	}

	internal static bool TryRead(
		int year,
		out List<XjCenturySummaryItemRecord> realmStatistics,
		out List<XjCenturySummaryItemRecord> daoSummaries)
	{
		realmStatistics = null;
		daoSummaries = null;
		if (year <= 0
			|| _snapshotYear != year
			|| _snapshotProgressionRevision != XjScheduler.ActorProgressionRevision)
		{
			return false;
		}

		realmStatistics = CloneSummaries(_realmStatistics);
		daoSummaries = CloneSummaries(_daoSummaries);
		return true;
	}

	internal static void Clear()
	{
		_snapshotYear = 0;
		_snapshotProgressionRevision = -1;
		_realmStatistics.Clear();
		_daoSummaries.Clear();
	}

	private static Dictionary<string, DaoStat> CreateDaoStats()
	{
		Dictionary<string, DaoStat> stats = new Dictionary<string, DaoStat>(StringComparer.Ordinal);
		for (int i = 0; i < XjCaiQiCatalog.Entries.Length; i++)
		{
			string daoTu = XjCaiQiCatalog.Entries[i].DisplayName?.Trim() ?? string.Empty;
			if (daoTu.Length > 0 && !stats.ContainsKey(daoTu))
			{
				stats[daoTu] = new DaoStat();
			}
		}
		return stats;
	}

	private static List<XjCenturySummaryItemRecord> BuildDaoSummaries(Dictionary<string, DaoStat> stats)
	{
		List<XjCenturySummaryItemRecord> result = new List<XjCenturySummaryItemRecord>(stats.Count);
		foreach (KeyValuePair<string, DaoStat> pair in stats)
		{
			DaoStat stat = pair.Value;
			int score = stat.Score + stat.ZiFu * 8 + stat.JinDan * 25 + stat.ShenDan * 30 + stat.JieLin * 45;
			result.Add(new XjCenturySummaryItemRecord
			{
				Key = pair.Key,
				Name = pair.Key,
				Score = score,
				Trend = ResolveDaoTrend(score, stat),
				Summary = BuildDaoSummaryText(pair.Key, stat)
			});
		}
		result.Sort(CompareDaoSummaryForDisplay);
		if (result.Count > XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury)
		{
			result.RemoveRange(XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury, result.Count - XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury);
		}
		return result;
	}

	private static void AddStatistic(List<XjCenturySummaryItemRecord> result, string key, string name, int score, string trend, string summary)
	{
		result.Add(new XjCenturySummaryItemRecord
		{
			Key = key,
			Name = name,
			Score = Math.Max(0, score),
			Trend = trend,
			Summary = summary
		});
	}

	private static List<XjCenturySummaryItemRecord> CloneSummaries(IReadOnlyList<XjCenturySummaryItemRecord> source)
	{
		List<XjCenturySummaryItemRecord> result = new List<XjCenturySummaryItemRecord>(source?.Count ?? 0);
		if (source == null)
		{
			return result;
		}
		for (int i = 0; i < source.Count; i++)
		{
			XjCenturySummaryItemRecord item = source[i];
			if (item == null) continue;
			result.Add(new XjCenturySummaryItemRecord
			{
				Key = item.Key ?? string.Empty,
				Name = item.Name ?? string.Empty,
				Score = item.Score,
				Trend = item.Trend ?? string.Empty,
				Summary = item.Summary ?? string.Empty
			});
		}
		return result;
	}

	private static string BuildDaoSummaryText(string daoTu, in DaoStat stat)
	{
		string text = "修士：" + stat.Count.ToString(CultureInfo.InvariantCulture)
			+ "，紫府：" + stat.ZiFu.ToString(CultureInfo.InvariantCulture)
			+ "，金丹：" + stat.JinDan.ToString(CultureInfo.InvariantCulture)
			+ "，神丹：" + stat.ShenDan.ToString(CultureInfo.InvariantCulture);
		return IsTaiYinDaoTu(daoTu)
			? text + "，结璘仙：" + stat.JieLin.ToString(CultureInfo.InvariantCulture)
			: text;
	}

	private static string ResolveDaoTrend(int score, in DaoStat stat)
	{
		if (stat.Count <= 0) return "断传";
		if (stat.JieLin > 0 || stat.ShenDan > 0 || stat.JinDan >= 3 || score >= 160) return "鼎盛";
		if (stat.JinDan > 0 || stat.ZiFu >= 3 || score >= 80) return "兴盛";
		if (stat.ZiFu > 0 || stat.Count >= 8 || score >= 30) return "复苏";
		return "潜隐";
	}

	private static int CompareDaoSummaryForDisplay(XjCenturySummaryItemRecord left, XjCenturySummaryItemRecord right)
	{
		int byState = DaoStateRank(right?.Trend).CompareTo(DaoStateRank(left?.Trend));
		if (byState != 0) return byState;
		int byScore = (right?.Score ?? 0).CompareTo(left?.Score ?? 0);
		if (byScore != 0) return byScore;
		int name = string.Compare(left?.Name, right?.Name, StringComparison.Ordinal);
		if (name != 0) return name;
		int key = string.Compare(left?.Key, right?.Key, StringComparison.Ordinal);
		return key != 0 ? key : string.Compare(left?.Summary, right?.Summary, StringComparison.Ordinal);
	}

	private static int DaoStateRank(string state)
	{
		if (string.Equals(state, "鼎盛", StringComparison.Ordinal)) return 6;
		if (string.Equals(state, "兴盛", StringComparison.Ordinal)) return 5;
		if (string.Equals(state, "复苏", StringComparison.Ordinal)) return 4;
		if (string.Equals(state, "衰退", StringComparison.Ordinal)) return 3;
		if (string.Equals(state, "潜隐", StringComparison.Ordinal)) return 2;
		if (string.Equals(state, "断传", StringComparison.Ordinal)) return 1;
		return 0;
	}

	private static bool IsTaiYinDaoTu(string daoTu)
	{
		return string.Equals((daoTu ?? string.Empty).Trim(), "太阴", StringComparison.Ordinal);
	}

	private struct DaoStat
	{
		internal int Count;
		internal int ZiFu;
		internal int JinDan;
		internal int ShenDan;
		internal int JieLin;
		internal int Score;
	}
}
