using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.DengMingShi;

internal static class XjDengMingShiFuQiSnapshotCodec
{
	private static readonly string[] StringKeys =
	{
		XjActorDataKeys.FuQiLineageId,
		XjActorDataKeys.FuQiDaoTuRootId,
		XjActorDataKeys.FuQiCoreType,
		XjActorDataKeys.FuQiCoreId,
		XjActorDataKeys.FuQiSensedQiId,
		XjActorDataKeys.FuQiStudiedIntentIds,
		XjActorDataKeys.FuQiCurrentIntentId,
		XjActorDataKeys.FuQiShenMiaoId,
		XjActorDataKeys.FuQiInheritedJinXing
	};

	private static readonly string[] IntKeys =
	{
		XjActorDataKeys.FuQiCoreProgress,
		XjActorDataKeys.FuQiCoreLastAnnualYear,
		XjActorDataKeys.FuQiEraLastAnnualYear,
		XjActorDataKeys.FuQiCoreProjectStartYear,
		XjActorDataKeys.FuQiCoreProjectCompleteYear,
		XjActorDataKeys.FuQiSenseYear,
		XjActorDataKeys.FuQiSenseResult,
		XjActorDataKeys.FuQiHuangGuanEnteredYear,
		XjActorDataKeys.FuQiZhenRenEnteredYear,
		XjActorDataKeys.FuQiRank4ZhenJunEligible,
		XjActorDataKeys.FuQiSwordQi,
		XjActorDataKeys.FuQiIntentStudyStartYear,
		XjActorDataKeys.FuQiIntentStudyCompleteYear,
		XjActorDataKeys.FuQiYangQingMingCompletedYear,
		XjActorDataKeys.FuQiSwordLastAnnualYear,
		XjActorDataKeys.FuQiBodyProjectStartYear,
		XjActorDataKeys.FuQiBodyProjectCompleteYear,
		XjActorDataKeys.FuQiPerfectionProjectStartYear,
		XjActorDataKeys.FuQiPerfectionProjectCompleteYear,
		XjActorDataKeys.FuQiShenMiaoPerfectionYear,
		XjActorDataKeys.FuQiRank4EligibilityChecked,
		XjActorDataKeys.FuQiRank5HighRealmRouteChecked,
		XjActorDataKeys.FuQiRank5StayFuQi,
		XjActorDataKeys.FuQiTrueSpiritInitialized,
		XjActorDataKeys.FuQiTrueSpirit,
		XjActorDataKeys.FuQiReincarnationBreakthroughBonusPercent,
		XjActorDataKeys.FuQiJinXingReady,
		XjActorDataKeys.FuQiJinXingNurtureCompleteYear,
		XjActorDataKeys.FuQiInjuryUntilYear,
		XjActorDataKeys.FuQiJinDanLastAttemptYear,
		XjActorDataKeys.FuQiJinDanNextAttemptYear,
		XjActorDataKeys.FuQiJinDanFailureCount,
		XjActorDataKeys.FuQiJinDanSuccessYear
	};

	// 这些字段记录的是“世界纪年”，而不是角色年龄。登名石会冻结角色，
	// 跨世界/跨年代放出时必须整体平移到新世界纪年，保留项目剩余时间。
	private static readonly string[] WorldYearKeys =
	{
		XjActorDataKeys.FuQiCoreLastAnnualYear,
		XjActorDataKeys.FuQiEraLastAnnualYear,
		XjActorDataKeys.FuQiCoreProjectStartYear,
		XjActorDataKeys.FuQiCoreProjectCompleteYear,
		XjActorDataKeys.FuQiSenseYear,
		XjActorDataKeys.FuQiHuangGuanEnteredYear,
		XjActorDataKeys.FuQiZhenRenEnteredYear,
		XjActorDataKeys.FuQiIntentStudyStartYear,
		XjActorDataKeys.FuQiIntentStudyCompleteYear,
		XjActorDataKeys.FuQiYangQingMingCompletedYear,
		XjActorDataKeys.FuQiSwordLastAnnualYear,
		XjActorDataKeys.FuQiBodyProjectStartYear,
		XjActorDataKeys.FuQiBodyProjectCompleteYear,
		XjActorDataKeys.FuQiPerfectionProjectStartYear,
		XjActorDataKeys.FuQiPerfectionProjectCompleteYear,
		XjActorDataKeys.FuQiShenMiaoPerfectionYear,
		XjActorDataKeys.FuQiJinXingNurtureCompleteYear,
		XjActorDataKeys.FuQiInjuryUntilYear,
		XjActorDataKeys.FuQiJinDanLastAttemptYear,
		XjActorDataKeys.FuQiJinDanNextAttemptYear,
		XjActorDataKeys.FuQiJinDanSuccessYear
	};

	// 旧版快照没有 SavedWorldYear。优先从“已经发生/最近处理”的年份推断
	// 保存锚点，不能拿未来项目完成年当保存年，否则会把未完成项目误判为已完成。
	private static readonly string[] LegacyAnchorYearKeys =
	{
		XjActorDataKeys.FuQiCoreLastAnnualYear,
		XjActorDataKeys.FuQiEraLastAnnualYear,
		XjActorDataKeys.FuQiSwordLastAnnualYear,
		XjActorDataKeys.FuQiSenseYear,
		XjActorDataKeys.FuQiHuangGuanEnteredYear,
		XjActorDataKeys.FuQiZhenRenEnteredYear,
		XjActorDataKeys.FuQiYangQingMingCompletedYear,
		XjActorDataKeys.FuQiShenMiaoPerfectionYear,
		XjActorDataKeys.FuQiJinDanLastAttemptYear,
		XjActorDataKeys.FuQiJinDanSuccessYear,
		XjActorDataKeys.FuQiCoreProjectStartYear,
		XjActorDataKeys.FuQiIntentStudyStartYear,
		XjActorDataKeys.FuQiBodyProjectStartYear,
		XjActorDataKeys.FuQiPerfectionProjectStartYear
	};

	internal static XjDengMingShiFuQiSnapshot Capture(Actor actor)
	{
		XjDengMingShiFuQiSnapshot snapshot = new XjDengMingShiFuQiSnapshot();
		if (actor?.data == null) return snapshot;
		snapshot.SavedWorldYear = ResolveCurrentWorldYear();

		for (int i = 0; i < StringKeys.Length; i++)
		{
			if (XjActorAccessor.TryGetString(actor, StringKeys[i], out string value))
			{
				snapshot.StringValues[StringKeys[i]] = value ?? string.Empty;
			}
		}
		for (int i = 0; i < IntKeys.Length; i++)
		{
			if (XjActorAccessor.TryGetInt(actor, IntKeys[i], out int value))
			{
				snapshot.IntValues[IntKeys[i]] = value;
			}
		}
		return snapshot;
	}

	internal static void Restore(Actor actor, XjDengMingShiFuQiSnapshot snapshot)
	{
		if (actor?.data == null || snapshot == null) return;
		if (snapshot.StringValues != null)
		{
			foreach (KeyValuePair<string, string> pair in snapshot.StringValues)
			{
				if (Array.IndexOf(StringKeys, pair.Key) >= 0)
				{
					XjActorAccessor.SetString(actor, pair.Key, pair.Value ?? string.Empty);
				}
			}
		}
		if (snapshot.IntValues != null)
		{
			int savedWorldYear = ResolveSavedWorldYear(snapshot);
			int currentWorldYear = ResolveCurrentWorldYear();
			foreach (KeyValuePair<string, int> pair in snapshot.IntValues)
			{
				if (Array.IndexOf(IntKeys, pair.Key) >= 0)
				{
					int value = TranslateWorldYear(pair.Key, pair.Value, savedWorldYear, currentWorldYear);
					XjActorAccessor.SetInt(actor, pair.Key, value);
				}
			}
		}
	}

	private static int TranslateWorldYear(string key, int value, int savedWorldYear, int currentWorldYear)
	{
		if (value <= 0
			|| savedWorldYear <= 0
			|| currentWorldYear <= 0
			|| Array.IndexOf(WorldYearKeys, key) < 0)
		{
			return value;
		}

		long shifted = (long)value + currentWorldYear - savedWorldYear;
		if (shifted <= 0L) return 1;
		return shifted > int.MaxValue ? int.MaxValue : (int)shifted;
	}

	private static int ResolveSavedWorldYear(XjDengMingShiFuQiSnapshot snapshot)
	{
		if (snapshot == null) return 0;
		if (snapshot.SavedWorldYear > 0) return snapshot.SavedWorldYear;
		if (snapshot.IntValues == null || snapshot.IntValues.Count == 0) return 0;

		int inferred = 0;
		for (int i = 0; i < LegacyAnchorYearKeys.Length; i++)
		{
			if (snapshot.IntValues.TryGetValue(LegacyAnchorYearKeys[i], out int year) && year > inferred)
			{
				inferred = year;
			}
		}
		return inferred;
	}

	private static int ResolveCurrentWorldYear()
	{
		int year = Math.Max(0, XjYearTracker.CurrentYear);
		try
		{
			if (World.world?.map_stats != null) year = Math.Max(year, World.world.map_stats.year);
		}
		catch { }
		return Math.Max(1, year);
	}

	internal static bool HasAny(XjDengMingShiFuQiSnapshot snapshot)
	{
		return snapshot != null
			&& ((snapshot.StringValues?.Count ?? 0) > 0 || (snapshot.IntValues?.Count ?? 0) > 0);
	}

	internal static bool TryGetString(XjDengMingShiFuQiSnapshot snapshot, string key, out string value)
	{
		value = string.Empty;
		return snapshot?.StringValues != null
			&& snapshot.StringValues.TryGetValue(key, out value);
	}

	internal static bool TryGetInt(XjDengMingShiFuQiSnapshot snapshot, string key, out int value)
	{
		value = 0;
		return snapshot?.IntValues != null
			&& snapshot.IntValues.TryGetValue(key, out value);
	}

	internal static XjDengMingShiFuQiSnapshot Clone(XjDengMingShiFuQiSnapshot source)
	{
		XjDengMingShiFuQiSnapshot clone = new XjDengMingShiFuQiSnapshot
		{
			SavedWorldYear = Math.Max(0, source?.SavedWorldYear ?? 0)
		};
		if (source?.StringValues != null)
		{
			clone.StringValues = new Dictionary<string, string>(source.StringValues, StringComparer.Ordinal);
		}
		if (source?.IntValues != null)
		{
			clone.IntValues = new Dictionary<string, int>(source.IntValues, StringComparer.Ordinal);
		}
		return clone;
	}
}
