using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Shi;
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
		internal readonly int Frame;
		internal readonly bool PredictedLethal;
		internal readonly int AttackerTier;
		internal readonly int DefenderTier;

		internal AttackerRecord(
			long attackerId,
			string name,
			string daoTu,
			float year,
			int frame,
			bool predictedLethal,
			int attackerTier,
			int defenderTier)
		{
			AttackerId = attackerId;
			Name = name ?? string.Empty;
			DaoTu = daoTu ?? string.Empty;
			Year = year;
			Frame = frame;
			PredictedLethal = predictedLethal;
			AttackerTier = attackerTier;
			DefenderTier = defenderTier;
		}
	}

	private static readonly Dictionary<long, AttackerRecord> RecordsByDefenderId = new Dictionary<long, AttackerRecord>();

	internal static void RecordAttacker(Actor defender, Actor attacker, int defenderTier, int attackerTier, float resolvedDamage, float defenderHealthBeforeDamage)
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
		try { currentYear = World.world?.map_stats?.year ?? 0; } catch (System.Exception xjCaught68) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjCombatTracker.cs:68", xjCaught68); }
		// Only actors that have actually bound a weapon art need combat-year marks.
		// The old path wrote two tracker dictionaries for every XuanJian-resolved hit.
		if (XjWeaponArtSystem.HasBoundKind(attacker, out _))
		{
			XjWeaponArtCombatTracker.MarkParticipation(attackerId, currentYear);
		}
		if (XjWeaponArtSystem.HasBoundKind(defender, out _))
		{
			XjWeaponArtCombatTracker.MarkParticipation(defenderId, currentYear);
		}

		// 普通非家族角色通常无需死亡归因；唯一例外是今释法师及以上主动度化
		// 非修士。古释不走杀生度化车道，这里不会为古释额外保留普通目标归因。
		bool shiDuhuaKill = false;
		if (defenderTier <= XjRealmSuppression.TierNone
			&& XjCultivationPathRules.IsShi(attacker)
			&& XjShiEntrySystem.IsDuhuaTarget(defender)
			&& XjShiState.TryBuildSnapshot(attacker, out XjShiSnapshot shiSnapshot)
			&& string.Equals(shiSnapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			shiDuhuaKill = XjShiCatalog.GetRank(shiSnapshot.Realm)
				>= XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster);
		}
		if (defenderTier <= XjRealmSuppression.TierNone
			&& !shiDuhuaKill
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
		bool predictedLethal = resolvedDamage > 0f
			&& defenderHealthBeforeDamage > 0f
			&& resolvedDamage + 0.01f >= defenderHealthBeforeDamage;
		// Main-thread runtime table; do not pay a monitor lock per resolved hit.
		RecordsByDefenderId[defenderId] = new AttackerRecord(
			attackerId,
			name,
			daoTu,
			year,
			Time.frameCount,
			predictedLethal,
			attackerTier,
			defenderTier);
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
		return TryGetValidKillerAttribution(defender, defenderTier, out attackerId, out attackerName, out attackerDaoTu, out _);
	}

	internal static bool TryGetValidKillerAttribution(
		Actor defender,
		int defenderTier,
		out long attackerId,
		out string attackerName,
		out string attackerDaoTu,
		out int attackerTier)
	{
		attackerId = 0L;
		attackerName = string.Empty;
		attackerDaoTu = string.Empty;
		attackerTier = XjRealmSuppression.TierNone;
		if (!TryGet(defender, out AttackerRecord record))
		{
			return false;
		}

		if (record.AttackerId <= 0L
			|| record.AttackerId == ((BaseSystemData)defender.data).id
			|| !record.PredictedLethal
			|| Time.frameCount - record.Frame > 1)
		{
			return false;
		}

		if (!XjScheduler.ResolveActor(record.AttackerId, out Actor attacker)
			|| attacker?.data == null
			|| !attacker.isAlive())
		{
			return false;
		}

		if (defenderTier > XjRealmSuppression.TierNone
			&& record.AttackerTier < defenderTier)
		{
			return false;
		}

		int currentYear = 0;
		try { currentYear = World.world?.map_stats?.year ?? 0; } catch (System.Exception xjCaught164) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjCombatTracker.cs:164", xjCaught164); }
		XjWeaponArtCombatTracker.MarkKill(record.AttackerId, currentYear, record.AttackerTier, defenderTier);

		attackerId = record.AttackerId;
		attackerName = record.Name;
		attackerDaoTu = record.DaoTu;
		attackerTier = record.AttackerTier;
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
			RecordsByDefenderId.Remove(actorId);
		}
	}

	internal static void Clear()
	{
		RecordsByDefenderId.Clear();
	}

	internal static void PruneExpiredRecords(int currentFrame, int removeBudget = 1024)
	{
		if (currentFrame <= 2 || removeBudget <= 0 || RecordsByDefenderId.Count == 0) return;
		List<long> stale = null;
		int minimumUsefulFrame = currentFrame - 2;
		foreach (KeyValuePair<long, AttackerRecord> pair in RecordsByDefenderId)
		{
			if (pair.Value.Frame >= minimumUsefulFrame) continue;
			stale ??= new List<long>(Math.Min(removeBudget, 128));
			stale.Add(pair.Key);
			if (stale.Count >= removeBudget) break;
		}
		if (stale == null) return;
		for (int i = 0; i < stale.Count; i++) RecordsByDefenderId.Remove(stale[i]);
	}

	private static bool TryGet(Actor actor, out AttackerRecord record)
	{
		if (actor?.data != null
			&& RecordsByDefenderId.TryGetValue(((BaseSystemData)actor.data).id, out record))
		{
			return true;
		}
		record = default;
		return false;
	}
}
