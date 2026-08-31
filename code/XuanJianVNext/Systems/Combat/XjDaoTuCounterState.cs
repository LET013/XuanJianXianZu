using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 道途克制动态状态。静态克制表只作为世界初始认知；当金丹／真君／道胎等高境
/// 发生确认击杀时，任意不同道途都可以由战果形成或强化新的局内克制关系。
/// 战斗热路径只做两级字典读取，不读取 ActorData、不 Trim，也不拼接临时字符串。
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
			|| IsHongXiaPair(killerDaoTu, victimDaoTu))
		{
			return;
		}

		// 克制不是“一次反杀就整条翻面”。把双方战果视作一条有方向的层级：
		// 同方向击杀逐层强化；反方向击杀先逐层消解，归零后再继续积累才会反转。
		// 若尚无动态记录，则静态原著/设定克制作为第1层世界初始认知参与演化。
		int currentSignedCount = ResolveSignedCount(killerDaoTu, victimDaoTu);
		int nextSignedCount = Mathf.Clamp(currentSignedCount + 1, -MaxCount, MaxCount);
		ApplySignedCount(killerDaoTu, victimDaoTu, nextSignedCount);
		XjWorldArchiveSystem.MarkChanged();
	}

	/// <summary>
	/// Hot-path lookup. Callers should pass DaoTu values materialized in the
	/// combat profile rather than rereading ActorData for every hit.
	/// </summary>
	internal static float GetMultiplier(string attackerDaoTu, string defenderDaoTu)
	{
		if (string.IsNullOrEmpty(attackerDaoTu) || string.IsNullOrEmpty(defenderDaoTu)
			|| IsHongXiaPair(attackerDaoTu, defenderDaoTu))
		{
			return 1f;
		}

		if (Multipliers.TryGetValue(attackerDaoTu, out Dictionary<string, float> targets)
			&& targets.TryGetValue(defenderDaoTu, out float multiplier))
		{
			return Mathf.Max(0f, multiplier);
		}

		// 静态表不是百科装饰，而是世界初始克制。高境确认击杀写入的动态值
		// 只在存在时覆盖基线，因此开局第一场战斗就能体现原有道途克制。
		return XjDaoTuCounter.ResolveStaticCombatMultiplier(attackerDaoTu, defenderDaoTu);
	}

	internal static float GetMultiplierForActors(Actor attacker, Actor defender)
	{
		return GetMultiplier(ReadActorDaoTu(attacker), ReadActorDaoTu(defender));
	}

	internal static void TryRegisterCounterOnDeath(Actor victim, in XjDeathSnapshot snapshot)
	{
		if (victim?.data == null
			|| XjRealmSuppression.GetRealmTier(victim) < XjRealmSuppression.TierJinDan
			|| !snapshot.Found
			|| snapshot.LastAttackerId <= 0L)
		{
			return;
		}

		// 死亡快照中的 LastAttacker* 已经过“最后一击 + 同帧致死 + 高境层级”归因校验。
		// 不再读取 CombatTracker 的长期最后攻击者，避免自然死亡或延迟死亡误写克制关系。
		string killerDaoTu = Normalize(snapshot.LastAttackerDaoTu);
		string victimDaoTu = Normalize(snapshot.DaoTu);
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

	/// <summary>
	/// 当前实战克制：包含静态基线，也包含局内高境战果形成的强化、消解与反转。
	/// 仅用于低频UI/百科读取，不进入命中热路径。
	/// </summary>
	internal static IReadOnlyList<string> GetEffectiveCounterTargets(string daoTu)
	{
		string source = Normalize(daoTu);
		if (string.IsNullOrEmpty(source)) return Array.Empty<string>();
		List<string> result = new List<string>();
		foreach (string target in BuildKnownDaoTuSet())
		{
			if (string.Equals(source, target, StringComparison.Ordinal)) continue;
			if (GetMultiplier(source, target) > 1.001f) result.Add(target);
		}
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	internal static IReadOnlyList<string> GetEffectiveCounteredBy(string daoTu)
	{
		string target = Normalize(daoTu);
		if (string.IsNullOrEmpty(target)) return Array.Empty<string>();
		List<string> result = new List<string>();
		foreach (string source in BuildKnownDaoTuSet())
		{
			if (string.Equals(source, target, StringComparison.Ordinal)) continue;
			if (GetMultiplier(source, target) > 1.001f) result.Add(source);
		}
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	internal static IReadOnlyCollection<string> GetKnownDaoTus()
	{
		return BuildKnownDaoTuSet();
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
			if (record == null || !TryParseArchiveKey(record.Key, out string source, out string target)
				|| IsHongXiaPair(source, target))
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

	private static bool TryGetStoredMultiplier(string source, string target, out float value)
	{
		value = 1f;
		return Multipliers.TryGetValue(source, out Dictionary<string, float> targets)
			&& targets.TryGetValue(target, out value);
	}

	private static void RemoveStoredMultiplier(string source, string target)
	{
		if (!Multipliers.TryGetValue(source, out Dictionary<string, float> targets)) return;
		targets.Remove(target);
		if (targets.Count == 0) Multipliers.Remove(source);
	}

	private static int ResolveSignedCount(string source, string target)
	{
		bool hasForward = TryGetStoredMultiplier(source, target, out float forward);
		bool hasReverse = TryGetStoredMultiplier(target, source, out float reverse);
		if (hasForward || hasReverse)
		{
			int sourceAdvantage = Math.Max(GetCountFromBonus(forward), GetCountFromPenalty(reverse));
			int targetAdvantage = Math.Max(GetCountFromPenalty(forward), GetCountFromBonus(reverse));
			if (sourceAdvantage > 0 || targetAdvantage > 0)
			{
				return Mathf.Clamp(sourceAdvantage - targetAdvantage, -MaxCount, MaxCount);
			}

			// 显式1.0/1.0用于表示“局内战果已经把固有克制消解为中性”，
			// 不能在这里重新跌回静态基线。
			return 0;
		}

		float staticForward = XjDaoTuCounter.ResolveStaticCombatMultiplier(source, target);
		float staticReverse = XjDaoTuCounter.ResolveStaticCombatMultiplier(target, source);
		bool sourceStaticAdvantage = staticForward > 1.001f && staticReverse <= 1.001f;
		bool targetStaticAdvantage = staticReverse > 1.001f && staticForward <= 1.001f;
		if (sourceStaticAdvantage) return 1;
		if (targetStaticAdvantage) return -1;
		return 0;
	}

	private static void ApplySignedCount(string source, string target, int signedCount)
	{
		int count = Mathf.Clamp(Math.Abs(signedCount), 0, MaxCount);
		if (signedCount > 0)
		{
			SetMultiplier(source, target, BonusByCount[count]);
			SetMultiplier(target, source, PenaltyByCount[count]);
			return;
		}
		if (signedCount < 0)
		{
			SetMultiplier(source, target, PenaltyByCount[count]);
			SetMultiplier(target, source, BonusByCount[count]);
			return;
		}

		// 静态克制被战果打到归零时，必须保留显式1.0/1.0覆盖，否则下一次读取
		// 会重新跌回开局基线；原本没有静态关系的动态克制归零后则直接删掉记录，
		// 避免长局把已经消解的中性道途对永久堆在存档里。
		float staticForward = XjDaoTuCounter.ResolveStaticCombatMultiplier(source, target);
		float staticReverse = XjDaoTuCounter.ResolveStaticCombatMultiplier(target, source);
		bool needsNeutralOverride = Mathf.Abs(staticForward - 1f) > 0.001f
			|| Mathf.Abs(staticReverse - 1f) > 0.001f;
		if (needsNeutralOverride)
		{
			SetMultiplier(source, target, 1f);
			SetMultiplier(target, source, 1f);
		}
		else
		{
			RemoveStoredMultiplier(source, target);
			RemoveStoredMultiplier(target, source);
		}
	}

	private static HashSet<string> BuildKnownDaoTuSet()
	{
		HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
		foreach (string value in XjDaoTuCounter.GetKnownDaoTus())
		{
			string normalized = Normalize(value);
			if (!string.IsNullOrEmpty(normalized)) result.Add(normalized);
		}
		foreach (KeyValuePair<string, Dictionary<string, float>> source in Multipliers)
		{
			if (!string.IsNullOrEmpty(source.Key)) result.Add(source.Key);
			foreach (string target in source.Value.Keys)
			{
				if (!string.IsNullOrEmpty(target)) result.Add(target);
			}
		}
		return result;
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

	private static bool IsHongXiaPair(string source, string target)
	{
		return string.Equals(Normalize(source), "虹霞", StringComparison.Ordinal)
			|| string.Equals(Normalize(target), "虹霞", StringComparison.Ordinal);
	}

	private static string Normalize(string value)
	{
		return XjDaoTuCounter.Normalize(value);
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
