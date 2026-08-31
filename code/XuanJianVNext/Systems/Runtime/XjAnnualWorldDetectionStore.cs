using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// Unified low-frequency world detection snapshot. Whole-cultivator aggregation is
/// scheduled by the annual command lane, then consumed incrementally under both an
/// actor-count budget and a wall-clock budget. No annual or century frame performs
/// an unbounded synchronous scan.
/// </summary>
internal static class XjAnnualWorldDetectionStore
{
	private static int _snapshotYear;
	private static int _snapshotProgressionRevision = -1;
	private static int _snapshotMembershipRevision = -1;
	private static List<XjCenturySummaryItemRecord> _realmStatistics = new List<XjCenturySummaryItemRecord>();
	private static List<XjCenturySummaryItemRecord> _daoSummaries = new List<XjCenturySummaryItemRecord>();
	private static int _cultivatorIdentityCount;
	private static int _ziFuJinDanPathCount;
	private static int _fuQiYangXingPathCount;

	private static IReadOnlyList<long> _scanActorIds = Array.Empty<long>();
	private static Dictionary<string, DaoStat> _pendingDaoStats;
	private static int _pendingYear;
	private static int _pendingProgressionRevision = -1;
	private static int _pendingMembershipRevision = -1;
	private static int _scanCursor;
	private static bool _scanActive;
	private static int _pendingTaiXi;
	private static int _pendingLianQi;
	private static int _pendingZhuJi;
	private static int _pendingHuangGuan;
	private static int _pendingZhenRen;
	private static int _pendingZhenJun;
	private static int _pendingShenDan;
	private static int _pendingCultivatorIdentityCount;
	private static int _pendingZiFuJinDanPathCount;
	private static int _pendingFuQiYangXingPathCount;
	private static int _pendingShiPathCount;
	private static int _pendingAncientShiCount;
	private static int _pendingModernShiCount;
	private static int _pendingShiMonk;
	private static int _pendingShiDharmaMaster;
	private static int _pendingShiLianMin;
	private static int _pendingShiMoHe;
	private static int _pendingShiDharmaForm;
	private static int _pendingShiWorldHonored;

	internal static bool HasPending => _scanActive;
	internal static int PendingCount => _scanActive ? Math.Max(0, _scanActorIds.Count - _scanCursor) : 0;

	internal static bool HasSnapshotForYear(int year)
	{
		return year > 0 && _snapshotYear == year;
	}

	internal static bool IsPendingForYear(int year)
	{
		return year > 0 && _scanActive && _pendingYear == year;
	}

	internal static bool ShouldRefresh(int year, int maxAgeYears)
	{
		if (year <= 0)
		{
			return false;
		}
		if (IsPendingForYear(year))
		{
			return false;
		}
		if (_snapshotYear <= 0)
		{
			return true;
		}
		if (year == _snapshotYear
			&& (_snapshotProgressionRevision != XjActorStateRevisionStore.GlobalProgressionRevision
				|| _snapshotMembershipRevision != XjCultivatorCache.MembershipRevision))
		{
			return true;
		}
		if (year < _snapshotYear)
		{
			return true;
		}
		return year - _snapshotYear >= Math.Max(1, maxAgeYears);
	}

	/// <summary>
	/// Acquires the cache's frozen detection mirror. Scheduling remains O(1): while
	/// the cursor spans frames, membership mutations are recorded as deltas against the
	/// live cache and replayed only after this snapshot lease is released.
	/// </summary>
	internal static bool ScheduleRefresh(int year)
	{
		if (year <= 0)
		{
			return false;
		}
		int progressionRevision = XjActorStateRevisionStore.GlobalProgressionRevision;
		int membershipRevision = XjCultivatorCache.MembershipRevision;
		if (_scanActive)
		{
			return _pendingYear == year;
		}
		if (_snapshotYear == year
			&& _snapshotProgressionRevision == progressionRevision
			&& _snapshotMembershipRevision == membershipRevision)
		{
			XjStageZeroObservation.RecordWorldSnapshotDuplicateSkip();
			return false;
		}

		ResetPendingCounters();
		_pendingDaoStats = CreateDaoStats();
		_pendingYear = year;
		_pendingProgressionRevision = progressionRevision;
		_pendingMembershipRevision = membershipRevision;
		_scanCursor = 0;
		_scanActorIds = XjCultivatorCache.AcquireAllIdsSnapshotForDetection();
		_scanActive = true;
		return true;
	}

	internal static int Tick(int actorBudget, double timeBudgetMs)
	{
		if (!_scanActive || actorBudget <= 0 || timeBudgetMs <= 0d)
		{
			return 0;
		}

		int processed = 0;
		XjCooperativeBudget budget = new XjCooperativeBudget(
			actorBudget,
			timeBudgetMs,
			XjRuntimeFramePriority.Background);
		while (_scanCursor < _scanActorIds.Count && budget.TryTake())
		{
			long actorId = _scanActorIds[_scanCursor++];
			AggregateActor(actorId);
			processed++;
		}

		if (_scanCursor >= _scanActorIds.Count)
		{
			PublishPendingSnapshot();
		}
		return processed;
	}

	private static void AggregateActor(long actorId)
	{
		if (actorId <= 0L
			|| !XjScheduler.ResolveActor(actorId, out Actor actor)
			|| actor?.data == null
			|| !actor.isAlive())
		{
			return;
		}

		_pendingCultivatorIdentityCount++;
		if (XjCultivationPathRules.IsZiFuJinDan(actor)) _pendingZiFuJinDanPathCount++;
		else if (XjCultivationPathRules.IsFuQiYangXing(actor)) _pendingFuQiYangXingPathCount++;
		else if (XjCultivationPathRules.IsShi(actor))
		{
			_pendingShiPathCount++;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
			if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)) _pendingAncientShiCount++;
			else if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) _pendingModernShiCount++;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string shiRealm);
			if (string.Equals(shiRealm, XjShiRealmIds.Monk, StringComparison.Ordinal)) _pendingShiMonk++;
			else if (string.Equals(shiRealm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)) _pendingShiDharmaMaster++;
			else if (string.Equals(shiRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal)) _pendingShiLianMin++;
			else if (string.Equals(shiRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) _pendingShiMoHe++;
			else if (string.Equals(shiRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) _pendingShiDharmaForm++;
			else if (string.Equals(shiRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) _pendingShiWorldHonored++;
			// 释修是独立第三体系，不得把投释前残留仙道Realm/DaoTu重新计回两路道途统计。
			return;
		}

		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (XjRealmHelper.IsRealm(realmId, "TaiXi")) _pendingTaiXi++;
		else if (XjRealmHelper.IsRealm(realmId, "LianQi")) _pendingLianQi++;
		else if (XjRealmHelper.IsRealm(realmId, "ZhuJi")) _pendingZhuJi++;
		else if (XjRealmHelper.IsRealm(realmId, "HuangGuan")) _pendingHuangGuan++;
		else if (XjHighRealmIdentity.ResolveClass(realmId) == XjHighRealmClass.ZhenRen) _pendingZhenRen++;
		else if (XjHighRealmIdentity.ResolveClass(realmId) == XjHighRealmClass.ZhenJun) _pendingZhenJun++;
		if (XjRealmHelper.IsRealm(realmId, "ShenDan")) _pendingShenDan++;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = daoTu?.Trim() ?? string.Empty;
		if (daoTu.Length == 0)
		{
			return;
		}

		if (!_pendingDaoStats.TryGetValue(daoTu, out DaoStat stat)) stat = new DaoStat();
		stat.Count++;
		int order = XjRealmHelper.GetOrder(realmId);
		XjHighRealmClass highRealmClass = XjHighRealmIdentity.ResolveClass(realmId);
		if (highRealmClass == XjHighRealmClass.ZhenJun) stat.JinDan++;
		else if (highRealmClass == XjHighRealmClass.ZhenRen) stat.ZiFu++;
		if (XjRealmHelper.IsRealm(realmId, "ShenDan")) stat.ShenDan++;
		if (IsTaiYinDaoTu(daoTu) && XjXuanJianShenTongSpecials.IsJieLinXian(actor)) stat.JieLin++;
		if (string.Equals(daoTu, "太阳", StringComparison.Ordinal) && XjXuanJianShenTongSpecials.IsYuYiXian(actor)) stat.YuYi++;
		stat.Score += Math.Max(1, order + 1);
		_pendingDaoStats[daoTu] = stat;
	}

	private static void PublishPendingSnapshot()
	{
		int cultivators = _pendingCultivatorIdentityCount;
		List<XjCenturySummaryItemRecord> realm = new List<XjCenturySummaryItemRecord>(20);
		AddStatistic(realm, "cultivator", "修士总数", cultivators, "总览", "本卷生成时存世修士" + cultivators.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "taixi", "胎息", _pendingTaiXi, "低境", "胎息修士" + _pendingTaiXi.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "lianqi", "炼气", _pendingLianQi, "低境", "炼气修士" + _pendingLianQi.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "zhuji", "筑基", _pendingZhuJi, "中境", "筑基修士" + _pendingZhuJi.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "huangguan", "黄冠", _pendingHuangGuan, "中境", "黄冠修士" + _pendingHuangGuan.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "zhenren", "真人", _pendingZhenRen, "高境", "紫府与服气真人共" + _pendingZhenRen.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "zhenjun", "真君", _pendingZhenJun, "高境", "金丹系高境与真君羽士共" + _pendingZhenJun.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shendan", "神丹", _pendingShenDan, "特殊", "神丹真君" + _pendingShenDan.ToString(CultureInfo.InvariantCulture) + "人（已计入真君）");
		AddStatistic(realm, "jielin", "结璘仙", XjJieLinXianRegistry.ActiveCount, "特殊", "结璘仙" + XjJieLinXianRegistry.ActiveCount.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "yuyi", "郁仪仙", XjYuYiXianRegistry.ActiveCount, "特殊", "郁仪仙" + XjYuYiXianRegistry.ActiveCount.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shi", "释修", _pendingShiPathCount, "第三体系", "古释与今释合计" + _pendingShiPathCount.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shi_ancient", "古释", _pendingAncientShiCount, "释修", "古释" + _pendingAncientShiCount.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shi_modern", "今释", _pendingModernShiCount, "释修", "今释" + _pendingModernShiCount.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shi_monk", "僧侣", _pendingShiMonk, "释修", "僧侣" + _pendingShiMonk.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shi_dharma_master", "法师", _pendingShiDharmaMaster, "释修", "法师" + _pendingShiDharmaMaster.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shi_lianmin", "怜愍", _pendingShiLianMin, "释修高境", "怜愍" + _pendingShiLianMin.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shi_mohe", "摩诃", _pendingShiMoHe, "释修高境", "摩诃" + _pendingShiMoHe.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shi_dharma_form", "法相", _pendingShiDharmaForm, "释修高境", "法相" + _pendingShiDharmaForm.ToString(CultureInfo.InvariantCulture) + "人");
		AddStatistic(realm, "shi_world_honored", "世尊", _pendingShiWorldHonored, "释修高境", "世尊" + _pendingShiWorldHonored.ToString(CultureInfo.InvariantCulture) + "人");

		List<XjCenturySummaryItemRecord> allDaoSummaries = BuildDaoSummaries(_pendingDaoStats, limitForDisplay: false);
		List<XjCenturySummaryItemRecord> dao = CloneSummaries(allDaoSummaries);
		if (dao.Count > XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury)
		{
			dao.RemoveRange(XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury, dao.Count - XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury);
		}

		_snapshotYear = _pendingYear;
		_snapshotProgressionRevision = _pendingProgressionRevision;
		_snapshotMembershipRevision = _pendingMembershipRevision;
		_realmStatistics = realm;
		_daoSummaries = dao;
		XjFruitPositionWorldState.UpdateFromAnnualSummaries(allDaoSummaries, _pendingYear);
		_cultivatorIdentityCount = _pendingCultivatorIdentityCount;
		_ziFuJinDanPathCount = _pendingZiFuJinDanPathCount;
		_fuQiYangXingPathCount = _pendingFuQiYangXingPathCount;

		_scanActive = false;
		_scanCursor = 0;
		_pendingYear = 0;
		_pendingProgressionRevision = -1;
		_pendingMembershipRevision = -1;
		XjCultivatorCache.ReleaseAllIdsSnapshotForDetection();
		_scanActorIds = Array.Empty<long>();
		_pendingDaoStats?.Clear();
		_pendingDaoStats = null;
		ResetPendingCounters();
	}

	internal static bool TryRead(
		int year,
		out List<XjCenturySummaryItemRecord> realmStatistics,
		out List<XjCenturySummaryItemRecord> daoSummaries)
	{
		realmStatistics = null;
		daoSummaries = null;
		if (year <= 0 || _snapshotYear != year)
		{
			return false;
		}

		realmStatistics = CloneSummaries(_realmStatistics);
		daoSummaries = CloneSummaries(_daoSummaries);
		return true;
	}

	/// <summary>
	/// Codex reads the latest completed unified snapshot. Opening UI never starts or
	/// completes a whole-world scan.
	/// </summary>
	internal static bool TryReadLatest(
		out int snapshotYear,
		out IReadOnlyList<XjCenturySummaryItemRecord> realmStatistics,
		out int cultivatorIdentityCount,
		out int ziFuJinDanPathCount,
		out int fuQiYangXingPathCount)
	{
		snapshotYear = _snapshotYear;
		realmStatistics = Array.Empty<XjCenturySummaryItemRecord>();
		cultivatorIdentityCount = 0;
		ziFuJinDanPathCount = 0;
		fuQiYangXingPathCount = 0;
		if (_snapshotYear <= 0)
		{
			return false;
		}

		// 完成后的年度快照只会整体替换，不在读取侧原地修改。Codex 属于纯读消费者，
		// 直接共享稳定 IReadOnlyList，避免每次重照百科再次复制整张境界统计表。
		realmStatistics = _realmStatistics;
		cultivatorIdentityCount = Math.Max(0, _cultivatorIdentityCount);
		ziFuJinDanPathCount = Math.Max(0, _ziFuJinDanPathCount);
		fuQiYangXingPathCount = Math.Max(0, _fuQiYangXingPathCount);
		return true;
	}

	internal static void Clear()
	{
		_snapshotYear = 0;
		_snapshotProgressionRevision = -1;
		_snapshotMembershipRevision = -1;
		_realmStatistics = new List<XjCenturySummaryItemRecord>();
		_daoSummaries = new List<XjCenturySummaryItemRecord>();
		_cultivatorIdentityCount = 0;
		_ziFuJinDanPathCount = 0;
		_fuQiYangXingPathCount = 0;
		_scanActive = false;
		_pendingYear = 0;
		_pendingProgressionRevision = -1;
		_pendingMembershipRevision = -1;
		_scanCursor = 0;
		XjCultivatorCache.ReleaseAllIdsSnapshotForDetection();
		_scanActorIds = Array.Empty<long>();
		_pendingDaoStats?.Clear();
		_pendingDaoStats = null;
		ResetPendingCounters();
	}

	private static void ResetPendingCounters()
	{
		_pendingTaiXi = 0;
		_pendingLianQi = 0;
		_pendingZhuJi = 0;
		_pendingHuangGuan = 0;
		_pendingZhenRen = 0;
		_pendingZhenJun = 0;
		_pendingShenDan = 0;
		_pendingCultivatorIdentityCount = 0;
		_pendingZiFuJinDanPathCount = 0;
		_pendingFuQiYangXingPathCount = 0;
		_pendingShiPathCount = 0;
		_pendingAncientShiCount = 0;
		_pendingModernShiCount = 0;
		_pendingShiMonk = 0;
		_pendingShiDharmaMaster = 0;
		_pendingShiLianMin = 0;
		_pendingShiMoHe = 0;
		_pendingShiDharmaForm = 0;
		_pendingShiWorldHonored = 0;
	}

	private static Dictionary<string, DaoStat> CreateDaoStats()
	{
		Dictionary<string, DaoStat> stats = new Dictionary<string, DaoStat>(StringComparer.Ordinal);
		for (int i = 0; i < XjCaiQiCatalog.Entries.Length; i++)
		{
			string daoTu = XjCaiQiCatalog.Entries[i].DisplayName?.Trim() ?? string.Empty;
			if (daoTu.Length > 0 && !stats.ContainsKey(daoTu)) stats[daoTu] = new DaoStat();
		}
		return stats;
	}

	private static List<XjCenturySummaryItemRecord> BuildDaoSummaries(
		Dictionary<string, DaoStat> stats,
		bool limitForDisplay = true)
	{
		List<XjCenturySummaryItemRecord> result = new List<XjCenturySummaryItemRecord>(stats?.Count ?? 0);
		if (stats == null) return result;
		foreach (KeyValuePair<string, DaoStat> pair in stats)
		{
			DaoStat stat = pair.Value;
			if (XjDaoTuCatalog.TryResolve(pair.Key, out XjDaoTuDefinition definition)
				&& definition.IsBingGu
				&& !XjDaoTuManifestRegistry.IsDiscovered(definition.RootId))
			{
				continue;
			}

			int score = stat.Score + stat.ZiFu * 8 + stat.JinDan * 25 + stat.ShenDan * 5 + stat.JieLin * 45 + stat.YuYi * 45;
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
		if (limitForDisplay && result.Count > XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury)
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
		if (source == null) return result;
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
			+ "，真人：" + stat.ZiFu.ToString(CultureInfo.InvariantCulture)
			+ "，真君：" + stat.JinDan.ToString(CultureInfo.InvariantCulture)
			+ "，神丹：" + stat.ShenDan.ToString(CultureInfo.InvariantCulture);
		if (IsTaiYinDaoTu(daoTu)) return text + "，结璘仙：" + stat.JieLin.ToString(CultureInfo.InvariantCulture);
		if (string.Equals((daoTu ?? string.Empty).Trim(), "太阳", StringComparison.Ordinal))
		{
			return text + "，郁仪仙：" + stat.YuYi.ToString(CultureInfo.InvariantCulture);
		}
		return text;
	}

	private static string ResolveDaoTrend(int score, in DaoStat stat)
	{
		if (stat.Count <= 0) return "断传";
		if (stat.JieLin > 0 || stat.YuYi > 0 || stat.ShenDan > 0 || stat.JinDan >= 3 || score >= 160) return "鼎盛";
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
		internal int YuYi;
		internal int Score;
	}
}
