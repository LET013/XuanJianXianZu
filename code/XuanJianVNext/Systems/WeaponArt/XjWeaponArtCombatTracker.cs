using System.Collections.Generic;

namespace XuanJianVNext.Systems.WeaponArt;

internal readonly struct XjWeaponArtCombatYearState
{
	internal readonly bool Participated;
	internal readonly bool Killed;
	internal readonly bool HigherRealmKill;

	internal XjWeaponArtCombatYearState(bool participated, bool killed, bool higherRealmKill)
	{
		Participated = participated;
		Killed = killed;
		HigherRealmKill = higherRealmKill;
	}
}

/// <summary>
/// 仅记录当年器艺结算需要的三个布尔结果。战斗热路径不写 actor.data，
/// 年度结算读取后自然跨年失效；读档最多损失当年尚未结算的实战加成。
/// </summary>
internal static class XjWeaponArtCombatTracker
{
	private sealed class Record
	{
		internal int Year;
		internal bool Participated;
		internal bool Killed;
		internal bool HigherRealmKill;
	}

	private static readonly Dictionary<long, Record> Records = new Dictionary<long, Record>();

	internal static void MarkParticipation(long actorId, int year)
	{
		if (actorId <= 0L || year <= 0) return;
		Record record = GetOrReset(actorId, year);
		record.Participated = true;
	}

	internal static void MarkKill(long actorId, int year, int attackerTier, int defenderTier)
	{
		if (actorId <= 0L || year <= 0) return;
		Record record = GetOrReset(actorId, year);
		record.Participated = true;
		record.Killed = true;
		if (defenderTier > attackerTier) record.HigherRealmKill = true;
	}

	internal static XjWeaponArtCombatYearState Read(long actorId, int year)
	{
		if (actorId <= 0L || year <= 0) return default;
		if (!Records.TryGetValue(actorId, out Record record) || record == null || record.Year != year)
		{
			return default;
		}
		return new XjWeaponArtCombatYearState(record.Participated, record.Killed, record.HigherRealmKill);
	}

	internal static void Remove(long actorId)
	{
		if (actorId <= 0L) return;
		Records.Remove(actorId);
	}

	internal static void Clear()
	{
		Records.Clear();
	}

	/// <summary>
	/// Combat participation is useful only for the matching annual settlement.
	/// Drop stale actor-year records in bounded long-run maintenance so survivors
	/// do not retain a dictionary entry forever after one old fight.
	/// </summary>
	internal static void PruneBeforeYear(int minimumYear, int removeBudget = 512)
	{
		if (minimumYear <= 0 || removeBudget <= 0 || Records.Count == 0) return;
		List<long> stale = null;
		foreach (KeyValuePair<long, Record> pair in Records)
		{
			if (pair.Value != null && pair.Value.Year >= minimumYear) continue;
			stale ??= new List<long>(System.Math.Min(removeBudget, 64));
			stale.Add(pair.Key);
			if (stale.Count >= removeBudget) break;
		}
		if (stale == null) return;
		for (int i = 0; i < stale.Count; i++) Records.Remove(stale[i]);
	}

	private static Record GetOrReset(long actorId, int year)
	{
		if (!Records.TryGetValue(actorId, out Record record) || record == null)
		{
			record = new Record { Year = year };
			Records[actorId] = record;
		}
		else if (record.Year != year)
		{
			record.Year = year;
			record.Participated = false;
			record.Killed = false;
			record.HigherRealmKill = false;
		}
		return record;
	}
}
