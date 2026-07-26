using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Core;

/// <summary>
/// Specialized read-only candidate sets for systems that previously scanned
/// every aptitude holder. Mutation happens only when cultivation identity,
/// realm or DaoTu changes.
/// </summary>
internal static class XjCultivatorCandidateIndex
{
	private static readonly HashSet<long> RealmEnteredIds = new HashSet<long>();
	private static readonly Dictionary<string, HashSet<long>> ZhuJiIdsByDaoTu =
		new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
	private static readonly Dictionary<long, string> ZhuJiDaoTuByActorId = new Dictionary<long, string>();
	private static long[] realmEnteredSnapshot = Array.Empty<long>();
	private static bool realmEnteredSnapshotDirty;
	private static readonly Dictionary<string, long[]> ZhuJiSnapshotsByDaoTu =
		new Dictionary<string, long[]>(StringComparer.Ordinal);
	private static readonly HashSet<string> DirtyZhuJiSnapshotDaoTu = new HashSet<string>(StringComparer.Ordinal);

	internal static void Observe(Actor actor, bool isCultivator, int realmTier)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L)
		{
			return;
		}

		SetRealmEntered(actorId, isCultivator && realmTier > XjRealmSuppression.TierNone);
		if (isCultivator && realmTier == XjRealmSuppression.TierZhuJi)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			SetZhuJiDaoTu(actorId, NormalizeDaoTu(daoTu));
		}
		else
		{
			SetZhuJiDaoTu(actorId, string.Empty);
		}
	}

	internal static void RefreshDaoTu(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| !XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)
			|| realmTier != XjRealmSuppression.TierZhuJi)
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		SetZhuJiDaoTu(actorId, NormalizeDaoTu(daoTu));
	}

	internal static IReadOnlyList<long> GetRealmEnteredIds()
	{
		if (realmEnteredSnapshotDirty)
		{
			realmEnteredSnapshot = new long[RealmEnteredIds.Count];
			RealmEnteredIds.CopyTo(realmEnteredSnapshot);
			realmEnteredSnapshotDirty = false;
		}
		return realmEnteredSnapshot;
	}

	internal static IReadOnlyList<long> GetZhuJiIdsByDaoTu(string daoTu)
	{
		string normalized = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalized)
			|| !ZhuJiIdsByDaoTu.TryGetValue(normalized, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return Array.Empty<long>();
		}

		if (!ZhuJiSnapshotsByDaoTu.TryGetValue(normalized, out long[] snapshot)
			|| DirtyZhuJiSnapshotDaoTu.Contains(normalized))
		{
			snapshot = new long[actorIds.Count];
			actorIds.CopyTo(snapshot);
			ZhuJiSnapshotsByDaoTu[normalized] = snapshot;
			DirtyZhuJiSnapshotDaoTu.Remove(normalized);
		}
		return snapshot;
	}

	internal static void Remove(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		if (RealmEnteredIds.Remove(actorId))
		{
			realmEnteredSnapshotDirty = true;
		}
		SetZhuJiDaoTu(actorId, string.Empty);
	}

	internal static void Clear()
	{
		RealmEnteredIds.Clear();
		ZhuJiIdsByDaoTu.Clear();
		ZhuJiDaoTuByActorId.Clear();
		realmEnteredSnapshot = Array.Empty<long>();
		realmEnteredSnapshotDirty = false;
		ZhuJiSnapshotsByDaoTu.Clear();
		DirtyZhuJiSnapshotDaoTu.Clear();
	}

	private static void SetRealmEntered(long actorId, bool included)
	{
		bool changed = included ? RealmEnteredIds.Add(actorId) : RealmEnteredIds.Remove(actorId);
		if (changed)
		{
			realmEnteredSnapshotDirty = true;
		}
	}

	private static void SetZhuJiDaoTu(long actorId, string daoTu)
	{
		ZhuJiDaoTuByActorId.TryGetValue(actorId, out string previousDaoTu);
		if (string.Equals(previousDaoTu, daoTu, StringComparison.Ordinal))
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(previousDaoTu)
			&& ZhuJiIdsByDaoTu.TryGetValue(previousDaoTu, out HashSet<long> previousSet))
		{
			previousSet.Remove(actorId);
			DirtyZhuJiSnapshotDaoTu.Add(previousDaoTu);
			if (previousSet.Count == 0)
			{
				ZhuJiIdsByDaoTu.Remove(previousDaoTu);
				ZhuJiSnapshotsByDaoTu.Remove(previousDaoTu);
				DirtyZhuJiSnapshotDaoTu.Remove(previousDaoTu);
			}
		}

		if (string.IsNullOrWhiteSpace(daoTu))
		{
			ZhuJiDaoTuByActorId.Remove(actorId);
			return;
		}

		if (!ZhuJiIdsByDaoTu.TryGetValue(daoTu, out HashSet<long> nextSet))
		{
			nextSet = new HashSet<long>();
			ZhuJiIdsByDaoTu[daoTu] = nextSet;
		}
		nextSet.Add(actorId);
		ZhuJiDaoTuByActorId[actorId] = daoTu;
		DirtyZhuJiSnapshotDaoTu.Add(daoTu);
	}

	private static string NormalizeDaoTu(string daoTu)
	{
		return string.IsNullOrWhiteSpace(daoTu) ? string.Empty : daoTu.Trim();
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
