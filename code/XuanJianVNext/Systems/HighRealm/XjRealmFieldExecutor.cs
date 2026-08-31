using System;
using System.Collections.Generic;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjRealmFieldState
{
	internal readonly long OwnerActorId;
	internal readonly string FieldId;
	internal readonly int StartedYear;
	internal readonly int EndYear;

	internal XjRealmFieldState(long ownerActorId, string fieldId, int startedYear, int endYear)
	{
		OwnerActorId = Math.Max(0L, ownerActorId);
		FieldId = fieldId ?? string.Empty;
		StartedYear = Math.Max(0, startedYear);
		EndYear = Math.Max(StartedYear, endYear);
	}
}

/// <summary>
/// 高位界域共用的轻量生命周期执行器。只保存稳定 id 与年份，不持有 Actor
/// 引用、不逐帧扫描世界。十方界、释修金地、法相及后续道胎领域只复用生命周期，
/// 具体效果仍由各自领域模块实现。
/// </summary>
internal static class XjRealmFieldExecutor
{
	private const int MaximumActiveFields = 128;
	private static readonly Dictionary<string, XjRealmFieldState> ActiveByKey =
		new Dictionary<string, XjRealmFieldState>(StringComparer.Ordinal);

	internal static int ActiveCount => ActiveByKey.Count;

	internal static bool TryBegin(long ownerActorId, string fieldId, int currentYear, int durationYears)
	{
		if (ownerActorId <= 0L || string.IsNullOrWhiteSpace(fieldId)) return false;
		string key = BuildKey(ownerActorId, fieldId);
		if (!ActiveByKey.ContainsKey(key) && ActiveByKey.Count >= MaximumActiveFields) return false;
		int safeYear = Math.Max(0, currentYear);
		ActiveByKey[key] = new XjRealmFieldState(ownerActorId, fieldId.Trim(), safeYear, safeYear + Math.Max(1, durationYears));
		return true;
	}

	internal static bool IsActive(long ownerActorId, string fieldId, int currentYear)
	{
		string key = BuildKey(ownerActorId, fieldId);
		if (!ActiveByKey.TryGetValue(key, out XjRealmFieldState state)) return false;
		if (currentYear <= state.EndYear) return true;
		ActiveByKey.Remove(key);
		return false;
	}

	internal static void End(long ownerActorId, string fieldId)
	{
		ActiveByKey.Remove(BuildKey(ownerActorId, fieldId));
	}

	internal static void TickYear(int currentYear, int maximumRemovals = 32)
	{
		if (ActiveByKey.Count == 0 || maximumRemovals <= 0) return;
		List<string> expired = null;
		foreach (KeyValuePair<string, XjRealmFieldState> pair in ActiveByKey)
		{
			if (pair.Value.EndYear >= currentYear) continue;
			expired ??= new List<string>();
			expired.Add(pair.Key);
			if (expired.Count >= maximumRemovals) break;
		}
		if (expired == null) return;
		for (int i = 0; i < expired.Count; i++) ActiveByKey.Remove(expired[i]);
	}

	internal static void Clear() => ActiveByKey.Clear();

	private static string BuildKey(long ownerActorId, string fieldId)
	{
		return Math.Max(0L, ownerActorId).ToString() + "|" + (fieldId ?? string.Empty).Trim();
	}
}
