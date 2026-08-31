using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Death;

namespace XuanJianVNext.Core;

/// <summary>
/// 事件驱动的长局语义诊断。这里只在真实突破、死亡、定途和世界事件落定时记数，
/// 不为输出统计重新扫描全世界角色。默认只保留当前年与上一年的聚合桶。
/// </summary>
internal static class XjSemanticDiagnostics
{
	private sealed class YearBucket
	{
		internal readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
		internal readonly Dictionary<string, int> HighRealmDeaths = new Dictionary<string, int>(StringComparer.Ordinal);
		internal readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);
	}

	private static readonly Dictionary<int, YearBucket> Buckets = new Dictionary<int, YearBucket>();
	private const int HighRealmDeathWarningThreshold = 6;

	internal static void RecordDeath(Actor actor, XjDeathCause cause, bool committed)
	{
		if (!XjRuntimeSettings.PerformanceObservationEnabled) return;
		int year = CurrentYear;
		string realm = ResolveRealm(actor);
		Increment(year, "death|" + realm + "|" + cause + "|" + (committed ? "committed" : "rejected"));
		if (!committed || !IsHighRealmDiagnosticRealm(realm)) return;
		YearBucket bucket = GetBucket(year);
		bucket.HighRealmDeaths.TryGetValue(realm, out int count);
		count++;
		bucket.HighRealmDeaths[realm] = count;
		string warningKey = "death_cluster|" + realm;
		if (count >= HighRealmDeathWarningThreshold && bucket.Warned.Add(warningKey))
		{
			Debug.LogWarning("[玄鉴][语义诊断] year=" + year.ToString(CultureInfo.InvariantCulture)
				+ " realm=" + realm + " deaths=" + count.ToString(CultureInfo.InvariantCulture)
				+ "；同一年度高境死亡集中，请核对寿尽、突破保护与死亡仲裁来源。");
		}
	}

	internal static void RecordRealmTransition(Actor actor, string previousRealmId, string nextRealmId, int year)
	{
		if (!XjRuntimeSettings.PerformanceObservationEnabled) return;
		string previous = XjRealmHelper.NormalizeId(previousRealmId);
		string next = XjRealmHelper.NormalizeId(nextRealmId);
		if (string.Equals(previous, next, StringComparison.Ordinal)) return;
		Increment(SafeYear(year), "realm|" + previous + "->" + next);
	}

	internal static void RecordDaoTuAssignment(Actor actor, string previousDaoTu, string nextDaoTu, string source)
	{
		if (!XjRuntimeSettings.PerformanceObservationEnabled) return;
		string previous = Normalize(previousDaoTu);
		string next = Normalize(nextDaoTu);
		if (string.IsNullOrWhiteSpace(next) || string.Equals(previous, next, StringComparison.Ordinal)) return;
		Increment(CurrentYear, "daotu|" + next + "|" + Normalize(source));
	}

	internal static void RecordEvent(string eventId, string result)
	{
		if (!XjRuntimeSettings.PerformanceObservationEnabled) return;
		Increment(CurrentYear, "event|" + Normalize(eventId) + "|" + Normalize(result));
	}

	internal static void RecordMentorshipTeaching(string result)
	{
		if (!XjRuntimeSettings.PerformanceObservationEnabled) return;
		Increment(CurrentYear, "mentorship|" + Normalize(result));
	}

	internal static void RecordGuZun(string action, string daoTu)
	{
		if (!XjRuntimeSettings.PerformanceObservationEnabled) return;
		Increment(CurrentYear, "guzun|" + Normalize(action) + "|" + Normalize(daoTu));
	}

	internal static string BuildYearSummary(int year)
	{
		if (!Buckets.TryGetValue(year, out YearBucket bucket) || bucket.Counts.Count == 0) return string.Empty;
		List<string> keys = new List<string>(bucket.Counts.Keys);
		keys.Sort(StringComparer.Ordinal);
		StringBuilder builder = new StringBuilder(256);
		for (int i = 0; i < keys.Count; i++)
		{
			if (i > 0) builder.Append("; ");
			builder.Append(keys[i]).Append('=').Append(bucket.Counts[keys[i]].ToString(CultureInfo.InvariantCulture));
		}
		return builder.ToString();
	}

	internal static void Clear() => Buckets.Clear();

	private static void Increment(int year, string key)
	{
		if (year <= 0 || string.IsNullOrWhiteSpace(key)) return;
		YearBucket bucket = GetBucket(year);
		bucket.Counts.TryGetValue(key, out int count);
		bucket.Counts[key] = count + 1;
		Prune(year);
	}

	private static YearBucket GetBucket(int year)
	{
		if (!Buckets.TryGetValue(year, out YearBucket bucket))
		{
			bucket = new YearBucket();
			Buckets[year] = bucket;
		}
		return bucket;
	}

	private static void Prune(int currentYear)
	{
		if (Buckets.Count <= 3) return;
		List<int> remove = null;
		foreach (int year in Buckets.Keys)
		{
			if (year >= currentYear - 1) continue;
			remove ??= new List<int>();
			remove.Add(year);
		}
		if (remove == null) return;
		for (int i = 0; i < remove.Count; i++) Buckets.Remove(remove[i]);
	}

	private static bool IsHighRealmDiagnosticRealm(string realm)
	{
		return string.Equals(realm, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realm, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| string.Equals(realm, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realm, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	private static string ResolveRealm(Actor actor)
	{
		try { return XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter); }
		catch { return "unknown"; }
	}

	private static int SafeYear(int year) => year > 0 ? year : CurrentYear;
	private static int CurrentYear => Math.Max(0, World.world?.map_stats?.year ?? XjYearTracker.CurrentYear);
	private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
}
