using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Core;

/// <summary>
/// 阶段5长局生态守卫。只消费阶段0百年普查和真实境界迁移事件，
/// 不扫描角色、不修改修炼状态、不自动调整概率或门槛。
/// </summary>
internal static class XjStageFiveEcologyGuard
{
	internal readonly struct CensusInput
	{
		internal readonly int Year;
		internal readonly int Cultivators;
		internal readonly int TaiXi;
		internal readonly int LianQi;
		internal readonly int ZhuJi;
		internal readonly int ZiFu;
		internal readonly int JinDan;
		internal readonly int ShenDan;
		internal readonly double LagAverage;
		internal readonly int LagP95;
		internal readonly int LagMax;
		internal readonly double MaintenanceLagAverage;
		internal readonly int MaintenanceLagP95;
		internal readonly int MaintenanceLagMax;
		internal readonly int ObservationErrors;
		internal readonly long RelevantGongFaDueLost;
		internal readonly long RelevantGongFaPhaseStarvation;
		internal readonly long AptitudeWindowMissed;
		internal readonly long BreakthroughAttempts;
		internal readonly long BreakthroughSuccesses;
		internal readonly long JinDanAttempts;
		internal readonly long JinDanSuccesses;
		internal readonly IReadOnlyDictionary<string, int> Blockers;
		internal readonly IReadOnlyDictionary<string, long> Promotions;

		internal CensusInput(
			int year,
			int cultivators,
			int taiXi,
			int lianQi,
			int zhuJi,
			int ziFu,
			int jinDan,
			int shenDan,
			double lagAverage,
			int lagP95,
			int lagMax,
			double maintenanceLagAverage,
			int maintenanceLagP95,
			int maintenanceLagMax,
			int observationErrors,
			long relevantGongFaDueLost,
			long relevantGongFaPhaseStarvation,
			long aptitudeWindowMissed,
			long breakthroughAttempts,
			long breakthroughSuccesses,
			long jinDanAttempts,
			long jinDanSuccesses,
			IReadOnlyDictionary<string, int> blockers,
			IReadOnlyDictionary<string, long> promotions)
		{
			Year = year;
			Cultivators = cultivators;
			TaiXi = taiXi;
			LianQi = lianQi;
			ZhuJi = zhuJi;
			ZiFu = ziFu;
			JinDan = jinDan;
			ShenDan = shenDan;
			LagAverage = lagAverage;
			LagP95 = lagP95;
			LagMax = lagMax;
			MaintenanceLagAverage = maintenanceLagAverage;
			MaintenanceLagP95 = maintenanceLagP95;
			MaintenanceLagMax = maintenanceLagMax;
			ObservationErrors = observationErrors;
			RelevantGongFaDueLost = relevantGongFaDueLost;
			RelevantGongFaPhaseStarvation = relevantGongFaPhaseStarvation;
			AptitudeWindowMissed = aptitudeWindowMissed;
			BreakthroughAttempts = breakthroughAttempts;
			BreakthroughSuccesses = breakthroughSuccesses;
			JinDanAttempts = jinDanAttempts;
			JinDanSuccesses = jinDanSuccesses;
			Blockers = blockers;
			Promotions = promotions;
		}
	}

	private readonly struct Alert
	{
		internal readonly string Severity;
		internal readonly string Code;
		internal readonly string Message;
		internal readonly double Metric;
		internal readonly double Threshold;

		internal Alert(string severity, string code, string message, double metric, double threshold)
		{
			Severity = severity;
			Code = code;
			Message = message;
			Metric = metric;
			Threshold = threshold;
		}
	}

	private const string EcologyCsvFileName = "long_run_ecology.csv";
	private const string AlertCsvFileName = "long_run_alerts.csv";
	private const string LatestJsonFileName = "long_run_latest.json";

	private static bool _initialized;
	private static int _firstCensusYear;
	private static int _previousCensusYear;
	private static int _previousHighRealmTotal;
	private static int _previousZiFuOrAbove;
	private static int _lastZiFuPromotionYear;
	private static int _lastJinDanPromotionYear;
	private static int _lastShenDanPromotionYear;
	private static long _cumulativeZiFuPromotions;
	private static long _cumulativeJinDanPromotions;
	private static long _cumulativeShenDanPromotions;
	private static bool _reportedIoFailure;

	internal static void RecordRealmTransition(string nextRealmId, int year)
	{
		if (!XjRuntimeSettings.StageZeroObservationEnabled || year <= 0)
		{
			return;
		}

		string next = XjRealmHelper.NormalizeId(nextRealmId);
		bool changed = false;
		if (string.Equals(next, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			int updated = Math.Max(_lastZiFuPromotionYear, year);
			changed = updated != _lastZiFuPromotionYear;
			_lastZiFuPromotionYear = updated;
		}
		else if (string.Equals(next, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			int updated = Math.Max(_lastJinDanPromotionYear, year);
			changed = updated != _lastJinDanPromotionYear;
			_lastJinDanPromotionYear = updated;
		}
		else if (string.Equals(nextRealmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(next, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			int updated = Math.Max(_lastShenDanPromotionYear, year);
			changed = updated != _lastShenDanPromotionYear;
			_lastShenDanPromotionYear = updated;
		}

		if (changed)
		{
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Runtime);
		}
	}

	internal static void OnCensus(string sessionDirectory, in CensusInput input)
	{
		if (!XjRuntimeSettings.StageZeroObservationEnabled
			|| string.IsNullOrWhiteSpace(sessionDirectory)
			|| input.Year <= 0)
		{
			return;
		}

		long ziFuPromotions = SumPromotionsTo(input.Promotions, XjRealmIds.ZiFu);
		long jinDanPromotions = SumPromotionsTo(input.Promotions, XjRealmIds.JinDan);
		long shenDanPromotions = SumPromotionsTo(input.Promotions, XjRealmIds.ShenDan);
		_cumulativeZiFuPromotions += ziFuPromotions;
		_cumulativeJinDanPromotions += jinDanPromotions;
		_cumulativeShenDanPromotions += shenDanPromotions;

		int highRealmTotal = Math.Max(0, input.ZiFu + input.JinDan + input.ShenDan);
		int ziFuOrAbove = highRealmTotal;
		bool firstCensus = !_initialized;
		if (firstCensus)
		{
			_initialized = true;
			_firstCensusYear = input.Year;
			_previousCensusYear = input.Year;
			_previousHighRealmTotal = highRealmTotal;
			_previousZiFuOrAbove = ziFuOrAbove;
			if (_lastZiFuPromotionYear <= 0) _lastZiFuPromotionYear = input.Year;
			if (_lastJinDanPromotionYear <= 0) _lastJinDanPromotionYear = input.Year;
			if (_lastShenDanPromotionYear <= 0) _lastShenDanPromotionYear = input.Year;
		}

		if (ziFuPromotions > 0) _lastZiFuPromotionYear = Math.Max(_lastZiFuPromotionYear, input.Year);
		if (jinDanPromotions > 0) _lastJinDanPromotionYear = Math.Max(_lastJinDanPromotionYear, input.Year);
		if (shenDanPromotions > 0) _lastShenDanPromotionYear = Math.Max(_lastShenDanPromotionYear, input.Year);
		// Mark immediately after the persisted census state changes. Later diagnostics
		// are allowed to fail without making this archive mutation invisible.
		XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Runtime);

		int yearsSinceZiFu = Math.Max(0, input.Year - Math.Max(_firstCensusYear, _lastZiFuPromotionYear));
		int yearsSinceJinDan = Math.Max(0, input.Year - Math.Max(_firstCensusYear, _lastJinDanPromotionYear));
		int yearsSinceShenDan = Math.Max(0, input.Year - Math.Max(_firstCensusYear, _lastShenDanPromotionYear));
		int windowYears = firstCensus ? 0 : Math.Max(1, input.Year - _previousCensusYear);

		int ziFuGrade4Blocked = GetBlocker(input.Blockers, "ZiFuRequiresGrade4GongFa");
		int ziFuZhenYuanBlocked = GetBlocker(input.Blockers, "InsufficientZhenYuan:" + XjRealmIds.ZiFu);
		int ziFuReady = GetBlocker(input.Blockers, "ReadyForAttempt:" + XjRealmIds.ZiFu);
		int jinDanXianJiBlocked = GetBlocker(input.Blockers, "JinDanRequiresFiveXianJi");
		int jinDanGrade6Blocked = GetBlocker(input.Blockers, "JinDanRequiresGrade6GongFa");
		int jinDanSetBlocked = GetBlocker(input.Blockers, "JinDanRequiresFiveGongFaSet");
		int jinDanReady = GetBlocker(input.Blockers, "ReadyForAttempt:" + XjRealmIds.JinDan);

		double zhuJiShare = SafeRatio(input.ZhuJi, input.Cultivators);
		double highRealmShare = SafeRatio(highRealmTotal, input.Cultivators);
		double grade4BlockShare = SafeRatio(ziFuGrade4Blocked, input.ZhuJi);
		double ziFuConversionPer100 = input.ZhuJi <= 0 || windowYears <= 0
			? 0d
			: ziFuPromotions * 100d / input.ZhuJi * (100d / windowYears);
		double jinDanConversionPer100 = input.ZiFu <= 0 || windowYears <= 0
			? 0d
			: jinDanPromotions * 100d / input.ZiFu * (100d / windowYears);

		List<Alert> alerts = EvaluateAlerts(
			in input,
			highRealmTotal,
			zhuJiShare,
			grade4BlockShare,
			ziFuGrade4Blocked,
			ziFuReady,
			jinDanReady,
			yearsSinceZiFu,
			yearsSinceJinDan);
		string status = ResolveStatus(alerts);
		string alertCodes = SerializeAlertCodes(alerts);

		try
		{
			AppendEcologyCsv(
				sessionDirectory,
				in input,
				status,
				alertCodes,
				highRealmTotal,
				zhuJiShare,
				highRealmShare,
				ziFuPromotions,
				jinDanPromotions,
				shenDanPromotions,
				yearsSinceZiFu,
				yearsSinceJinDan,
				yearsSinceShenDan,
				ziFuConversionPer100,
				jinDanConversionPer100,
				ziFuGrade4Blocked,
				ziFuZhenYuanBlocked,
				ziFuReady,
				jinDanXianJiBlocked,
				jinDanGrade6Blocked,
				jinDanSetBlocked,
				jinDanReady,
				grade4BlockShare,
				alerts);
			AppendAlertsCsv(sessionDirectory, input.Year, alerts);
			WriteLatestJson(
				sessionDirectory,
				in input,
				status,
				alerts,
				highRealmTotal,
				zhuJiShare,
				highRealmShare,
				ziFuPromotions,
				jinDanPromotions,
				shenDanPromotions,
				yearsSinceZiFu,
				yearsSinceJinDan,
				yearsSinceShenDan,
				ziFuConversionPer100,
				jinDanConversionPer100,
				ziFuGrade4Blocked,
				ziFuZhenYuanBlocked,
				ziFuReady,
				jinDanXianJiBlocked,
				jinDanGrade6Blocked,
				jinDanSetBlocked,
				jinDanReady,
				grade4BlockShare);
		}
		catch (Exception ex)
		{
			ReportIoFailure(ex);
		}

		if (!string.Equals(status, "PASS", StringComparison.Ordinal))
		{
			Debug.LogWarning("[玄鉴][阶段5生态] year=" + input.Year
				+ " status=" + status
				+ " alerts=" + alertCodes
				+ " zhuji=" + input.ZhuJi
				+ " zifu=" + input.ZiFu
				+ " jindan=" + input.JinDan
				+ " shendan=" + input.ShenDan
				+ " zifuDryYears=" + yearsSinceZiFu
				+ " jindanDryYears=" + yearsSinceJinDan);
		}

		_previousCensusYear = input.Year;
		_previousHighRealmTotal = highRealmTotal;
		_previousZiFuOrAbove = ziFuOrAbove;
	}

	internal static XjLongRunEcologyArchiveData ExportState()
	{
		return new XjLongRunEcologyArchiveData
		{
			Initialized = _initialized,
			FirstCensusYear = _firstCensusYear,
			PreviousCensusYear = _previousCensusYear,
			PreviousHighRealmTotal = _previousHighRealmTotal,
			PreviousZiFuOrAbove = _previousZiFuOrAbove,
			LastZiFuPromotionYear = _lastZiFuPromotionYear,
			LastJinDanPromotionYear = _lastJinDanPromotionYear,
			LastShenDanPromotionYear = _lastShenDanPromotionYear,
			CumulativeZiFuPromotions = _cumulativeZiFuPromotions,
			CumulativeJinDanPromotions = _cumulativeJinDanPromotions,
			CumulativeShenDanPromotions = _cumulativeShenDanPromotions
		};
	}

	internal static void ImportState(XjLongRunEcologyArchiveData state)
	{
		XjLongRunEcologyArchiveData data = state ?? new XjLongRunEcologyArchiveData();
		_initialized = data.Initialized;
		_firstCensusYear = Math.Max(0, data.FirstCensusYear);
		_previousCensusYear = Math.Max(0, data.PreviousCensusYear);
		_previousHighRealmTotal = Math.Max(0, data.PreviousHighRealmTotal);
		_previousZiFuOrAbove = Math.Max(0, data.PreviousZiFuOrAbove);
		_lastZiFuPromotionYear = Math.Max(0, data.LastZiFuPromotionYear);
		_lastJinDanPromotionYear = Math.Max(0, data.LastJinDanPromotionYear);
		_lastShenDanPromotionYear = Math.Max(0, data.LastShenDanPromotionYear);
		_cumulativeZiFuPromotions = Math.Max(0L, data.CumulativeZiFuPromotions);
		_cumulativeJinDanPromotions = Math.Max(0L, data.CumulativeJinDanPromotions);
		_cumulativeShenDanPromotions = Math.Max(0L, data.CumulativeShenDanPromotions);
		_reportedIoFailure = false;
	}

	internal static void Clear()
	{
		_initialized = false;
		_firstCensusYear = 0;
		_previousCensusYear = 0;
		_previousHighRealmTotal = 0;
		_previousZiFuOrAbove = 0;
		_lastZiFuPromotionYear = 0;
		_lastJinDanPromotionYear = 0;
		_lastShenDanPromotionYear = 0;
		_cumulativeZiFuPromotions = 0L;
		_cumulativeJinDanPromotions = 0L;
		_cumulativeShenDanPromotions = 0L;
		_reportedIoFailure = false;
	}

	private static List<Alert> EvaluateAlerts(
		in CensusInput input,
		int highRealmTotal,
		double zhuJiShare,
		double grade4BlockShare,
		int ziFuGrade4Blocked,
		int ziFuReady,
		int jinDanReady,
		int yearsSinceZiFu,
		int yearsSinceJinDan)
	{
		List<Alert> alerts = new(8);
		if (input.RelevantGongFaDueLost > 0)
		{
			alerts.Add(new Alert("FAIL", "E001_GONGFA_DUE_LOSS", "发现仍被丢失的功法到期机会", input.RelevantGongFaDueLost, 0d));
		}
		if (input.RelevantGongFaPhaseStarvation > 0)
		{
			alerts.Add(new Alert("FAIL", "E002_PHASE_STARVATION", "发现功法周期相位饥饿", input.RelevantGongFaPhaseStarvation, 0d));
		}
		if (input.LagP95 >= 5)
		{
			alerts.Add(new Alert("FAIL", "E003_CORE_LAG_CRITICAL", "核心年度结算P95持续落后过高", input.LagP95, 5d));
		}
		else if (input.LagP95 > 2)
		{
			alerts.Add(new Alert("WARN", "E003_CORE_LAG", "核心年度结算P95超过两年", input.LagP95, 2d));
		}
		if (input.ObservationErrors > 0)
		{
			alerts.Add(new Alert("WARN", "E004_OBSERVATION_ERRORS", "百年普查出现读取错误", input.ObservationErrors, 0d));
		}
		if (input.AptitudeWindowMissed > 0)
		{
			alerts.Add(new Alert("WARN", "E005_APTITUDE_WINDOW_MISSED", "出现五岁/六岁资质窗口漏判", input.AptitudeWindowMissed, 0d));
		}

		if (input.ZhuJi >= 50 && yearsSinceZiFu >= 500)
		{
			alerts.Add(new Alert("FAIL", "E101_ZIFU_DRY_SPELL_CRITICAL", "大量筑基存在但五百年无新紫府", yearsSinceZiFu, 500d));
		}
		else if (input.ZhuJi >= 25 && yearsSinceZiFu >= 300)
		{
			alerts.Add(new Alert("WARN", "E101_ZIFU_DRY_SPELL", "筑基人口充足但三百年无新紫府", yearsSinceZiFu, 300d));
		}

		if (input.ZiFu >= 5 && yearsSinceJinDan >= 800)
		{
			alerts.Add(new Alert("FAIL", "E102_JINDAN_DRY_SPELL_CRITICAL", "紫府人口充足但八百年无新金丹", yearsSinceJinDan, 800d));
		}
		else if (input.ZiFu >= 3 && yearsSinceJinDan >= 500)
		{
			alerts.Add(new Alert("WARN", "E102_JINDAN_DRY_SPELL", "紫府人口存在但五百年无新金丹", yearsSinceJinDan, 500d));
		}

		if (input.Year >= 1000 && input.Cultivators >= 100 && zhuJiShare >= 0.85d && highRealmTotal == 0)
		{
			alerts.Add(new Alert("FAIL", "E103_ZHUJI_DOMINANCE_CRITICAL", "千年后修士高度集中于筑基且高境归零", zhuJiShare, 0.85d));
		}
		else if (input.Year >= 500 && input.Cultivators >= 100 && zhuJiShare >= 0.80d && highRealmTotal <= 1)
		{
			alerts.Add(new Alert("WARN", "E103_ZHUJI_DOMINANCE", "修士人口过度集中于筑基", zhuJiShare, 0.80d));
		}

		if (input.Year >= 300 && input.ZhuJi >= 50 && grade4BlockShare >= 0.80d && ziFuGrade4Blocked > 0)
		{
			alerts.Add(new Alert("FAIL", "E104_GRADE4_BOTTLENECK_CRITICAL", "超过八成筑基被四品功法阻塞", grade4BlockShare, 0.80d));
		}
		else if (input.Year >= 300 && input.ZhuJi >= 30 && grade4BlockShare >= 0.60d && ziFuGrade4Blocked > 0)
		{
			alerts.Add(new Alert("WARN", "E104_GRADE4_BOTTLENECK", "筑基群体出现集中四品功法阻塞", grade4BlockShare, 0.60d));
		}

		if (ziFuReady >= 20 && yearsSinceZiFu >= 100)
		{
			alerts.Add(new Alert("FAIL", "E105_ZIFU_READY_STALLED_CRITICAL", "大量角色已满足紫府条件但长期没有晋升", ziFuReady, 20d));
		}
		else if (ziFuReady >= 5 && yearsSinceZiFu >= 100)
		{
			alerts.Add(new Alert("WARN", "E105_ZIFU_READY_STALLED", "已有角色满足紫府条件但百年窗口无晋升", ziFuReady, 5d));
		}

		if (jinDanReady >= 10 && yearsSinceJinDan >= 500)
		{
			alerts.Add(new Alert("FAIL", "E106_JINDAN_READY_STALLED_CRITICAL", "多名紫府已满足金丹条件但长期没有晋升", jinDanReady, 10d));
		}
		else if (jinDanReady >= 3 && yearsSinceJinDan >= 300)
		{
			alerts.Add(new Alert("WARN", "E106_JINDAN_READY_STALLED", "已有紫府满足金丹条件但长期没有晋升", jinDanReady, 3d));
		}

		if (_initialized && _previousHighRealmTotal > 0 && highRealmTotal == 0 && input.ZhuJi >= 25)
		{
			alerts.Add(new Alert("FAIL", "E107_HIGH_REALM_EXTINCTION", "上一百年仍有高境修士，本次普查已全部断代", _previousHighRealmTotal, 0d));
		}
		else if (_initialized && _previousZiFuOrAbove >= 4 && ZiFuOrAboveDropRatio(_previousZiFuOrAbove, highRealmTotal) >= 0.50d)
		{
			alerts.Add(new Alert("WARN", "E108_HIGH_REALM_COLLAPSE", "高境人口在一个百年窗口内下降超过一半", SafeRatio(_previousZiFuOrAbove - highRealmTotal, _previousZiFuOrAbove), 0.50d));
		}
		return alerts;
	}

	private static double ZiFuOrAboveDropRatio(int previous, int current)
	{
		return previous <= 0 || current >= previous ? 0d : (previous - current) / (double)previous;
	}

	private static void AppendEcologyCsv(
		string sessionDirectory,
		in CensusInput input,
		string status,
		string alertCodes,
		int highRealmTotal,
		double zhuJiShare,
		double highRealmShare,
		long ziFuPromotions,
		long jinDanPromotions,
		long shenDanPromotions,
		int yearsSinceZiFu,
		int yearsSinceJinDan,
		int yearsSinceShenDan,
		double ziFuConversionPer100,
		double jinDanConversionPer100,
		int ziFuGrade4Blocked,
		int ziFuZhenYuanBlocked,
		int ziFuReady,
		int jinDanXianJiBlocked,
		int jinDanGrade6Blocked,
		int jinDanSetBlocked,
		int jinDanReady,
		double grade4BlockShare,
		List<Alert> alerts)
	{
		string path = Path.Combine(sessionDirectory, EcologyCsvFileName);
		bool writeHeader = !File.Exists(path);
		StringBuilder sb = new(1024);
		if (writeHeader)
		{
			sb.AppendLine("year,status,alert_count,fail_count,warn_count,alert_codes,cultivators,taixi,lianqi,zhuji,zifu,jindan,shendan,high_realm_total,zhuji_share,high_realm_share,zifu_promotions_window,jindan_promotions_window,shendan_promotions_window,zifu_promotions_cumulative,jindan_promotions_cumulative,shendan_promotions_cumulative,years_since_zifu_promotion,years_since_jindan_promotion,years_since_shendan_promotion,zifu_conversion_per100,jindan_conversion_per100,zifu_grade4_blocked,zifu_zhenyuan_blocked,zifu_ready,jindan_xianji_blocked,jindan_grade6_blocked,jindan_set_blocked,jindan_ready,grade4_block_share,lag_avg,lag_p95,lag_max,maintenance_lag_avg,maintenance_lag_p95,maintenance_lag_max,gongfa_due_lost,phase_starvation,aptitude_window_missed,breakthrough_attempts,breakthrough_successes,jindan_attempts,jindan_successes");
		}
		sb.Append(input.Year).Append(',')
			.Append(status).Append(',')
			.Append(alerts.Count).Append(',')
			.Append(CountSeverity(alerts, "FAIL")).Append(',')
			.Append(CountSeverity(alerts, "WARN")).Append(',')
			.Append(CsvEscape(alertCodes)).Append(',')
			.Append(input.Cultivators).Append(',')
			.Append(input.TaiXi).Append(',')
			.Append(input.LianQi).Append(',')
			.Append(input.ZhuJi).Append(',')
			.Append(input.ZiFu).Append(',')
			.Append(input.JinDan).Append(',')
			.Append(input.ShenDan).Append(',')
			.Append(highRealmTotal).Append(',')
			.Append(zhuJiShare.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
			.Append(highRealmShare.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
			.Append(ziFuPromotions).Append(',')
			.Append(jinDanPromotions).Append(',')
			.Append(shenDanPromotions).Append(',')
			.Append(_cumulativeZiFuPromotions).Append(',')
			.Append(_cumulativeJinDanPromotions).Append(',')
			.Append(_cumulativeShenDanPromotions).Append(',')
			.Append(yearsSinceZiFu).Append(',')
			.Append(yearsSinceJinDan).Append(',')
			.Append(yearsSinceShenDan).Append(',')
			.Append(ziFuConversionPer100.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
			.Append(jinDanConversionPer100.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
			.Append(ziFuGrade4Blocked).Append(',')
			.Append(ziFuZhenYuanBlocked).Append(',')
			.Append(ziFuReady).Append(',')
			.Append(jinDanXianJiBlocked).Append(',')
			.Append(jinDanGrade6Blocked).Append(',')
			.Append(jinDanSetBlocked).Append(',')
			.Append(jinDanReady).Append(',')
			.Append(grade4BlockShare.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
			.Append(input.LagAverage.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
			.Append(input.LagP95).Append(',')
			.Append(input.LagMax).Append(',')
			.Append(input.MaintenanceLagAverage.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
			.Append(input.MaintenanceLagP95).Append(',')
			.Append(input.MaintenanceLagMax).Append(',')
			.Append(input.RelevantGongFaDueLost).Append(',')
			.Append(input.RelevantGongFaPhaseStarvation).Append(',')
			.Append(input.AptitudeWindowMissed).Append(',')
			.Append(input.BreakthroughAttempts).Append(',')
			.Append(input.BreakthroughSuccesses).Append(',')
			.Append(input.JinDanAttempts).Append(',')
			.Append(input.JinDanSuccesses).AppendLine();
		File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
	}

	private static void AppendAlertsCsv(string sessionDirectory, int year, List<Alert> alerts)
	{
		if (alerts == null || alerts.Count == 0) return;
		string path = Path.Combine(sessionDirectory, AlertCsvFileName);
		bool writeHeader = !File.Exists(path);
		StringBuilder sb = new(512);
		if (writeHeader) sb.AppendLine("year,severity,code,message,metric,threshold");
		for (int i = 0; i < alerts.Count; i++)
		{
			Alert alert = alerts[i];
			sb.Append(year).Append(',')
				.Append(alert.Severity).Append(',')
				.Append(alert.Code).Append(',')
				.Append(CsvEscape(alert.Message)).Append(',')
				.Append(alert.Metric.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
				.Append(alert.Threshold.ToString("F6", CultureInfo.InvariantCulture)).AppendLine();
		}
		File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
	}

	private static void WriteLatestJson(
		string sessionDirectory,
		in CensusInput input,
		string status,
		List<Alert> alerts,
		int highRealmTotal,
		double zhuJiShare,
		double highRealmShare,
		long ziFuPromotions,
		long jinDanPromotions,
		long shenDanPromotions,
		int yearsSinceZiFu,
		int yearsSinceJinDan,
		int yearsSinceShenDan,
		double ziFuConversionPer100,
		double jinDanConversionPer100,
		int ziFuGrade4Blocked,
		int ziFuZhenYuanBlocked,
		int ziFuReady,
		int jinDanXianJiBlocked,
		int jinDanGrade6Blocked,
		int jinDanSetBlocked,
		int jinDanReady,
		double grade4BlockShare)
	{
		StringBuilder sb = new(2048);
		sb.Append("{\n")
			.Append("  \"year\": ").Append(input.Year).Append(",\n")
			.Append("  \"status\": \"").Append(JsonEscape(status)).Append("\",\n")
			.Append("  \"alerts\": [");
		for (int i = 0; i < alerts.Count; i++)
		{
			if (i > 0) sb.Append(',');
			Alert alert = alerts[i];
			sb.Append("{\"severity\":\"").Append(JsonEscape(alert.Severity))
				.Append("\",\"code\":\"").Append(JsonEscape(alert.Code))
				.Append("\",\"message\":\"").Append(JsonEscape(alert.Message))
				.Append("\",\"metric\":").Append(alert.Metric.ToString("F6", CultureInfo.InvariantCulture))
				.Append(",\"threshold\":").Append(alert.Threshold.ToString("F6", CultureInfo.InvariantCulture)).Append('}');
		}
		sb.Append("],\n")
			.Append("  \"population\": {\"cultivators\":").Append(input.Cultivators)
			.Append(",\"taixi\":").Append(input.TaiXi)
			.Append(",\"lianqi\":").Append(input.LianQi)
			.Append(",\"zhuji\":").Append(input.ZhuJi)
			.Append(",\"zifu\":").Append(input.ZiFu)
			.Append(",\"jindan\":").Append(input.JinDan)
			.Append(",\"shendan\":").Append(input.ShenDan)
			.Append(",\"highRealmTotal\":").Append(highRealmTotal)
			.Append(",\"zhujiShare\":").Append(zhuJiShare.ToString("F6", CultureInfo.InvariantCulture))
			.Append(",\"highRealmShare\":").Append(highRealmShare.ToString("F6", CultureInfo.InvariantCulture)).Append("},\n")
			.Append("  \"promotions\": {\"zifuWindow\":").Append(ziFuPromotions)
			.Append(",\"jindanWindow\":").Append(jinDanPromotions)
			.Append(",\"shendanWindow\":").Append(shenDanPromotions)
			.Append(",\"zifuCumulative\":").Append(_cumulativeZiFuPromotions)
			.Append(",\"jindanCumulative\":").Append(_cumulativeJinDanPromotions)
			.Append(",\"shendanCumulative\":").Append(_cumulativeShenDanPromotions)
			.Append(",\"yearsSinceZiFu\":").Append(yearsSinceZiFu)
			.Append(",\"yearsSinceJinDan\":").Append(yearsSinceJinDan)
			.Append(",\"yearsSinceShenDan\":").Append(yearsSinceShenDan)
			.Append(",\"zifuConversionPer100\":").Append(ziFuConversionPer100.ToString("F6", CultureInfo.InvariantCulture))
			.Append(",\"jindanConversionPer100\":").Append(jinDanConversionPer100.ToString("F6", CultureInfo.InvariantCulture)).Append("},\n")
			.Append("  \"blockers\": {\"zifuGrade4\":").Append(ziFuGrade4Blocked)
			.Append(",\"zifuZhenYuan\":").Append(ziFuZhenYuanBlocked)
			.Append(",\"zifuReady\":").Append(ziFuReady)
			.Append(",\"jindanXianJi\":").Append(jinDanXianJiBlocked)
			.Append(",\"jindanGrade6\":").Append(jinDanGrade6Blocked)
			.Append(",\"jindanGongFaSet\":").Append(jinDanSetBlocked)
			.Append(",\"jindanReady\":").Append(jinDanReady)
			.Append(",\"grade4BlockShare\":").Append(grade4BlockShare.ToString("F6", CultureInfo.InvariantCulture)).Append("},\n")
			.Append("  \"correctness\": {\"lagAverage\":").Append(input.LagAverage.ToString("F3", CultureInfo.InvariantCulture))
			.Append(",\"lagP95\":").Append(input.LagP95)
			.Append(",\"lagMax\":").Append(input.LagMax)
			.Append(",\"gongFaDueLost\":").Append(input.RelevantGongFaDueLost)
			.Append(",\"phaseStarvation\":").Append(input.RelevantGongFaPhaseStarvation)
			.Append(",\"aptitudeWindowMissed\":").Append(input.AptitudeWindowMissed).Append("}\n")
			.Append("}\n");
		File.WriteAllText(Path.Combine(sessionDirectory, LatestJsonFileName), sb.ToString(), new UTF8Encoding(false));
	}

	private static long SumPromotionsTo(IReadOnlyDictionary<string, long> promotions, string targetRealmId)
	{
		if (promotions == null || promotions.Count == 0 || string.IsNullOrWhiteSpace(targetRealmId)) return 0L;
		string suffix = "->" + targetRealmId;
		long total = 0L;
		foreach (KeyValuePair<string, long> pair in promotions)
		{
			if (!string.IsNullOrWhiteSpace(pair.Key)
				&& pair.Key.EndsWith(suffix, StringComparison.Ordinal))
			{
				total += Math.Max(0L, pair.Value);
			}
		}
		return total;
	}

	private static int GetBlocker(IReadOnlyDictionary<string, int> blockers, string key)
	{
		return blockers != null && key != null && blockers.TryGetValue(key, out int value) ? Math.Max(0, value) : 0;
	}

	private static double SafeRatio(int numerator, int denominator)
	{
		return denominator <= 0 ? 0d : Math.Max(0, numerator) / (double)denominator;
	}

	private static string ResolveStatus(List<Alert> alerts)
	{
		if (CountSeverity(alerts, "FAIL") > 0) return "FAIL";
		if (CountSeverity(alerts, "WARN") > 0) return "WARN";
		return "PASS";
	}

	private static int CountSeverity(List<Alert> alerts, string severity)
	{
		if (alerts == null || alerts.Count == 0) return 0;
		int count = 0;
		for (int i = 0; i < alerts.Count; i++)
		{
			if (string.Equals(alerts[i].Severity, severity, StringComparison.Ordinal)) count++;
		}
		return count;
	}

	private static string SerializeAlertCodes(List<Alert> alerts)
	{
		if (alerts == null || alerts.Count == 0) return string.Empty;
		StringBuilder sb = new();
		for (int i = 0; i < alerts.Count; i++)
		{
			if (i > 0) sb.Append(';');
			sb.Append(alerts[i].Code);
		}
		return sb.ToString();
	}

	private static string CsvEscape(string value)
	{
		value ??= string.Empty;
		return "\"" + value.Replace("\"", "\"\"") + "\"";
	}

	private static string JsonEscape(string value)
	{
		return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
	}

	private static void ReportIoFailure(Exception ex)
	{
		if (_reportedIoFailure) return;
		_reportedIoFailure = true;
		Debug.LogWarning("[玄鉴][阶段5生态] 导出失败：" + ex.GetType().Name + ": " + ex.Message);
	}
}
