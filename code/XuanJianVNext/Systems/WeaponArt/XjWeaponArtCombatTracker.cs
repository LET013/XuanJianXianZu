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
	private static readonly object Sync = new object();

	internal static void MarkParticipation(long actorId, int year)
	{
		if (actorId <= 0L || year <= 0) return;
		lock (Sync)
		{
			Record record = GetOrReset(actorId, year);
			record.Participated = true;
		}
	}

	internal static void MarkKill(long actorId, int year, int attackerTier, int defenderTier)
	{
		if (actorId <= 0L || year <= 0) return;
		lock (Sync)
		{
			Record record = GetOrReset(actorId, year);
			record.Participated = true;
			record.Killed = true;
			if (defenderTier > attackerTier) record.HigherRealmKill = true;
		}
	}

	internal static XjWeaponArtCombatYearState Read(long actorId, int year)
	{
		if (actorId <= 0L || year <= 0) return default;
		lock (Sync)
		{
			if (!Records.TryGetValue(actorId, out Record record) || record == null || record.Year != year)
			{
				return default;
			}
			return new XjWeaponArtCombatYearState(record.Participated, record.Killed, record.HigherRealmKill);
		}
	}

	internal static void Remove(long actorId)
	{
		if (actorId <= 0L) return;
		lock (Sync) Records.Remove(actorId);
	}

	internal static void Clear()
	{
		lock (Sync) Records.Clear();
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
