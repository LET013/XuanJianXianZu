using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// Runtime-only recent attacker index. Combat no longer writes four actor.data
/// fields per hit; death capture reads this bounded O(1) record instead.
/// </summary>
internal static class XjCombatTracker
{
	private readonly struct AttackerRecord
	{
		internal readonly long AttackerId;
		internal readonly string Name;
		internal readonly string DaoTu;
		internal readonly float Year;
		internal readonly int AttackerTier;
		internal readonly int DefenderTier;

		internal AttackerRecord(long attackerId, string name, string daoTu, float year, int attackerTier, int defenderTier)
		{
			AttackerId = attackerId;
			Name = name ?? string.Empty;
			DaoTu = daoTu ?? string.Empty;
			Year = year;
			AttackerTier = attackerTier;
			DefenderTier = defenderTier;
		}
	}

	private static readonly Dictionary<long, AttackerRecord> RecordsByDefenderId = new Dictionary<long, AttackerRecord>();
	private static readonly object Sync = new object();

	internal static void RecordAttacker(Actor defender, Actor attacker, int defenderTier, int attackerTier)
	{
		if (defender?.data == null || attacker?.data == null)
		{
			return;
		}

		long defenderId = ((BaseSystemData)defender.data).id;
		long attackerId = ((BaseSystemData)attacker.data).id;
		if (defenderId <= 0L || attackerId <= 0L)
		{
			return;
		}

		int currentYear = 0;
		try { currentYear = World.world?.map_stats?.year ?? 0; } catch { }
		XjWeaponArtCombatTracker.MarkParticipation(attackerId, currentYear);
		XjWeaponArtCombatTracker.MarkParticipation(defenderId, currentYear);

		// Ordinary non-family actors do not participate in XuanJian death history.
		if (defenderTier <= XjRealmSuppression.TierNone
			&& !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(defenderId, out _))
		{
			return;
		}
		if (attackerTier < defenderTier
			&& !XjTrueDamageSystem.IsAuthorizedLivingJinDanPursuit(defender, attacker))
		{
			return;
		}

		string name = attacker.data.name ?? string.Empty;
		string daoTu = attackerTier >= XjRealmSuppression.TierJinDan
			&& XjActorAccessor.TryGetString(attacker, XjActorDataKeys.DaoTu, out string value)
			? value?.Trim() ?? string.Empty
			: string.Empty;
		float year = World.world?.map_stats?.year ?? 0f;
		lock (Sync)
		{
			RecordsByDefenderId[defenderId] = new AttackerRecord(attackerId, name, daoTu, year, attackerTier, defenderTier);
		}
	}

	internal static long GetLastAttackerId(Actor actor)
	{
		return TryGet(actor, out AttackerRecord record) ? record.AttackerId : 0L;
	}

	internal static string GetLastAttackerName(Actor actor)
	{
		return TryGet(actor, out AttackerRecord record) ? record.Name : string.Empty;
	}

	internal static string GetLastAttackerDaoTu(Actor actor)
	{
		return TryGet(actor, out AttackerRecord record) ? record.DaoTu : string.Empty;
	}

	internal static float GetLastAttackedTimestamp(Actor actor)
	{
		return TryGet(actor, out AttackerRecord record) ? record.Year : 0f;
	}

	internal static bool TryGetValidKillerAttribution(
		Actor defender,
		int defenderTier,
		out long attackerId,
		out string attackerName,
		out string attackerDaoTu)
	{
		attackerId = 0L;
		attackerName = string.Empty;
		attackerDaoTu = string.Empty;
		if (!TryGet(defender, out AttackerRecord record))
		{
			return false;
		}

		int currentYear = 0;
		try { currentYear = World.world?.map_stats?.year ?? 0; } catch { }
		XjWeaponArtCombatTracker.MarkKill(record.AttackerId, currentYear, record.AttackerTier, defenderTier);

		if (defenderTier > XjRealmSuppression.TierNone
			&& record.AttackerTier < defenderTier)
		{
			return false;
		}

		attackerId = record.AttackerId;
		attackerName = record.Name;
		attackerDaoTu = record.DaoTu;
		return attackerId > 0L;
	}

	internal static void ClearAttackerRecord(Actor actor)
	{
		if (actor?.data != null)
		{
			Remove(((BaseSystemData)actor.data).id);
		}
	}

	internal static void Remove(long actorId)
	{
		if (actorId > 0L)
		{
			lock (Sync)
			{
				RecordsByDefenderId.Remove(actorId);
			}
		}
	}

	internal static void Clear()
	{
		lock (Sync)
		{
			RecordsByDefenderId.Clear();
		}
	}

	private static bool TryGet(Actor actor, out AttackerRecord record)
	{
		if (actor?.data != null)
		{
			lock (Sync)
			{
				if (RecordsByDefenderId.TryGetValue(((BaseSystemData)actor.data).id, out record))
				{
					return true;
				}
			}
		}
		record = default;
		return false;
	}
}
