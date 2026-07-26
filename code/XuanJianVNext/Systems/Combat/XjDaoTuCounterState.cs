using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 道途克制动态状态。写入发生在金丹死亡事件，战斗热路径只做两级字典读取，
/// 不读取 ActorData、不 Trim，也不拼接临时字符串。
/// </summary>
internal static class XjDaoTuCounterState
{
	private static readonly float[] BonusByCount = { 1f, 1.3f, 1.36f, 1.42f, 1.48f, 1.54f, 1.6f };
	private static readonly float[] PenaltyByCount = { 1f, 0.7f, 0.66f, 0.62f, 0.58f, 0.54f, 0.5f };
	private const int MaxCount = 6;
	private const string ArchiveSeparator = "=>";

	// sourceDaoTu -> targetDaoTu -> multiplier
	private static readonly Dictionary<string, Dictionary<string, float>> Multipliers =
		new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);

	internal static void RegisterCounter(string killerDaoTu, string victimDaoTu)
	{
		killerDaoTu = Normalize(killerDaoTu);
		victimDaoTu = Normalize(victimDaoTu);
		if (string.IsNullOrEmpty(killerDaoTu)
			|| string.IsNullOrEmpty(victimDaoTu)
			|| string.Equals(killerDaoTu, victimDaoTu, StringComparison.Ordinal)
			|| !IsCounterAllowed(killerDaoTu, victimDaoTu))
		{
			return;
		}

		int currentCount = Math.Max(
			GetCountFromBonus(GetMultiplier(killerDaoTu, victimDaoTu)),
			GetCountFromPenalty(GetMultiplier(victimDaoTu, killerDaoTu)));
		int nextCount = Mathf.Min(currentCount + 1, MaxCount);
		SetMultiplier(killerDaoTu, victimDaoTu, BonusByCount[nextCount]);
		SetMultiplier(victimDaoTu, killerDaoTu, PenaltyByCount[nextCount]);
		XjWorldArchiveSystem.MarkChanged();
	}

	/// <summary>
	/// Hot-path lookup. Callers should pass DaoTu values materialized in the
	/// combat profile rather than rereading ActorData for every hit.
	/// </summary>
	internal static float GetMultiplier(string attackerDaoTu, string defenderDaoTu)
	{
		if (string.IsNullOrEmpty(attackerDaoTu) || string.IsNullOrEmpty(defenderDaoTu))
		{
			return 1f;
		}

		return Multipliers.TryGetValue(attackerDaoTu, out Dictionary<string, float> targets)
			&& targets.TryGetValue(defenderDaoTu, out float multiplier)
			? Mathf.Max(0f, multiplier)
			: 1f;
	}

	internal static float GetMultiplierForActors(Actor attacker, Actor defender)
	{
		return GetMultiplier(ReadActorDaoTu(attacker), ReadActorDaoTu(defender));
	}

	internal static void TryRegisterCounterOnDeath(Actor victim)
	{
		if (victim?.data == null || XjRealmSuppression.GetRealmTier(victim) < 5)
		{
			return;
		}

		string killerDaoTu = XjCombatTracker.GetLastAttackerDaoTu(victim);
		string victimDaoTu = ReadActorDaoTu(victim);
		if (!string.IsNullOrEmpty(killerDaoTu) && !string.IsNullOrEmpty(victimDaoTu))
		{
			RegisterCounter(killerDaoTu, victimDaoTu);
		}
	}

	internal static IReadOnlyList<string> GetAllActiveCounters()
	{
		List<string> result = new List<string>();
		foreach (KeyValuePair<string, Dictionary<string, float>> source in Multipliers)
		{
			foreach (KeyValuePair<string, float> target in source.Value)
			{
				if (target.Value > 1f)
				{
					result.Add(BuildArchiveKey(source.Key, target.Key) + " x" + target.Value.ToString("F2"));
				}
			}
		}
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	internal static void ExportArchiveData(List<XjWorldArchiveDaoTuCounterRecord> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (KeyValuePair<string, Dictionary<string, float>> source in Multipliers)
		{
			foreach (KeyValuePair<string, float> target in source.Value)
			{
				records.Add(new XjWorldArchiveDaoTuCounterRecord
				{
					Key = BuildArchiveKey(source.Key, target.Key),
					Value = target.Value
				});
			}
		}
	}

	internal static void ImportArchiveData(IEnumerable<XjWorldArchiveDaoTuCounterRecord> records)
	{
		Multipliers.Clear();
		if (records == null)
		{
			return;
		}

		foreach (XjWorldArchiveDaoTuCounterRecord record in records)
		{
			if (record == null || !TryParseArchiveKey(record.Key, out string source, out string target))
			{
				continue;
			}
			SetMultiplier(source, target, record.Value);
		}
	}

	internal static void Clear()
	{
		Multipliers.Clear();
	}

	private static void SetMultiplier(string source, string target, float value)
	{
		if (!Multipliers.TryGetValue(source, out Dictionary<string, float> targets))
		{
			targets = new Dictionary<string, float>(StringComparer.Ordinal);
			Multipliers[source] = targets;
		}
		targets[target] = value;
	}

	private static string ReadActorDaoTu(Actor actor)
	{
		if (actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu))
		{
			return Normalize(daoTu);
		}
		return string.Empty;
	}

	private static bool IsCounterAllowed(string source, string target)
	{
		IReadOnlyList<string> targets = XjDaoTuCounter.GetCounterTargets(source);
		for (int i = 0; i < targets.Count; i++)
		{
			if (string.Equals(targets[i], target, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static string BuildArchiveKey(string source, string target)
	{
		return source + ArchiveSeparator + target;
	}

	private static bool TryParseArchiveKey(string key, out string source, out string target)
	{
		source = string.Empty;
		target = string.Empty;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		int separatorIndex = key.IndexOf(ArchiveSeparator, StringComparison.Ordinal);
		if (separatorIndex <= 0 || separatorIndex + ArchiveSeparator.Length >= key.Length)
		{
			return false;
		}

		source = Normalize(key.Substring(0, separatorIndex));
		target = Normalize(key.Substring(separatorIndex + ArchiveSeparator.Length));
		return !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(target);
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}

	private static int GetCountFromBonus(float bonus)
	{
		for (int i = 0; i < BonusByCount.Length; i++)
		{
			if (Mathf.Abs(BonusByCount[i] - bonus) < 0.001f)
			{
				return i;
			}
		}
		return 0;
	}

	private static int GetCountFromPenalty(float penalty)
	{
		for (int i = 0; i < PenaltyByCount.Length; i++)
		{
			if (Mathf.Abs(PenaltyByCount[i] - penalty) < 0.001f)
			{
				return i;
			}
		}
		return 0;
	}
}
