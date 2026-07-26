using System.Collections.Generic;
using System.Globalization;

namespace XuanJianVNext.Systems.ZongMen;

/// <summary>
/// 年度预算内执行的宗门持久化自愈。宗门身份独立于角色当前居住城市；
/// 这里只清理死亡、非修士、重复职阶和失效山峰，不因原版迁城移除宗门成员。
/// </summary>
internal static class XjZongMenSelfHeal
{
	internal static bool Repair(in XjZongMenMaintenanceSnapshot snapshot)
	{
		City city = snapshot.City;
		if (city?.data == null || !XjZongMenCityData.HasZongMen(city)) return false;
		bool changed = XjZongMenCityData.BackfillFounderHistory(city);
		city.data.get(XjZongMenCityData.KeySchemaVersion, out int schemaVersion, 0);
		if (schemaVersion != XjZongMenCityData.CurrentSchemaVersion)
		{
			city.data.set(XjZongMenCityData.KeySchemaVersion, XjZongMenCityData.CurrentSchemaVersion);
			changed = true;
		}
		city.data.get(XjZongMenCityData.KeyZongZhuGeneration, out int storedGeneration, 0);
		if (storedGeneration <= 0)
		{
			city.data.get(XjZongMenCityData.KeyLegacyGeneration, out int legacyGeneration, 0);
			XjZongMenCityData.SetGeneration(city, legacyGeneration > 0 ? legacyGeneration : 1);
			changed = true;
		}
		XjZongMenCityTraitAccessor.EnsureDefaults(city);
		XjZongMenCityData.EnsureMainPeak(city);
		changed |= XjZongMenCityData.NormalizeStoredPeakNames(city);

		List<long> validMemberIds = new List<long>();
		HashSet<long> validSet = new HashSet<long>();
		for (int i = 0; i < snapshot.Members.Count; i++)
		{
			XjZongMenMemberSnapshot member = snapshot.Members[i];
			if (member.IsValidMember && validSet.Add(member.ActorId))
			{
				validMemberIds.Add(member.ActorId);
				continue;
			}
			if (member.Actor?.data == null) continue;
			XjZongMenIdentitySnapshot identity = XjZongMenAccessor.BuildIdentity(member.Actor);
			if (identity.Found && identity.ZongMenId == snapshot.ZongMenId)
			{
				XjZongMenAccessor.WriteIdentity(member.Actor, XjZongMenIdentitySnapshot.Empty);
			}
		}

		List<long> storedMembers = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyMemberIds);
		if (!SequenceEqual(storedMembers, validMemberIds))
		{
			XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeyMemberIds, validMemberIds);
			changed = true;
		}

		List<long> storedEvaluated = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyJoinEvaluatedIds);
		List<long> evaluated = FilterEvaluatedIds(storedEvaluated);
		if (!SequenceEqual(storedEvaluated, evaluated))
		{
			XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeyJoinEvaluatedIds, evaluated);
			changed = true;
		}

		List<int> storedPeakIds = XjZongMenCityData.ReadPeakIds(city);
		List<int> peakIds = NormalizePeakIds(city);
		if (!SequenceEqual(storedPeakIds, peakIds))
		{
			XjZongMenCityData.WritePeakIds(city, peakIds);
			changed = true;
		}

		long zongZhuId = XjZongMenCityData.GetZongZhuId(city);
		if (zongZhuId > 0L && !validSet.Contains(zongZhuId))
		{
			XjZongMenCityData.SetZongZhuId(city, 0L);
			city.data.set(XjZongMenCityData.KeyPeakFengZhuPrefix + XjZongMenCityData.MainPeakId, string.Empty);
			zongZhuId = 0L;
			changed = true;
		}

		List<long> elders = FilterRoleList(
			XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeySupremeElders),
			validSet,
			zongZhuId,
			requireJinDan: true);
		List<long> storedElders = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeySupremeElders);
		if (!SequenceEqual(storedElders, elders))
		{
			XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeySupremeElders, elders);
			changed = true;
		}
		if (elders.Count > 0)
		{
			changed |= XjZongMenCityData.EnsureDongTianPeak(city);
			peakIds = XjZongMenCityData.ReadPeakIds(city);
		}
		HashSet<long> elderSet = new HashSet<long>(elders);

		HashSet<long> peakMasterSet = new HashSet<long>();
		for (int i = 0; i < peakIds.Count; i++)
		{
			int peakId = peakIds[i];
			long masterId = 0L;
			XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, out masterId);
			long normalizedMasterId = masterId;
			if (peakId == XjZongMenCityData.MainPeakId)
			{
				normalizedMasterId = zongZhuId;
			}
			else if (peakId == XjZongMenCityData.SupremePeakId
				|| normalizedMasterId <= 0L
				|| !validSet.Contains(normalizedMasterId)
				|| normalizedMasterId == zongZhuId
				|| elderSet.Contains(normalizedMasterId)
				|| peakMasterSet.Contains(normalizedMasterId))
			{
				normalizedMasterId = 0L;
			}
			else
			{
				Actor master = XjZongMenCityData.ResolveActor(normalizedMasterId);
				if (master?.data == null
					|| !master.isAlive()
					|| !XjZongMenCityData.IsCultivator(master)
					|| XjZongMenCityData.GetRealmLevel(master) >= XjZongMenCityData.JinDanRealmLevel)
				{
					normalizedMasterId = 0L;
				}
				else
				{
					peakMasterSet.Add(normalizedMasterId);
				}
			}

			if (peakId == XjZongMenCityData.MainPeakId && normalizedMasterId > 0L) peakMasterSet.Add(normalizedMasterId);
			if (masterId != normalizedMasterId)
			{
				city.data.set(
					XjZongMenCityData.KeyPeakFengZhuPrefix + peakId,
					normalizedMasterId > 0L ? normalizedMasterId.ToString(CultureInfo.InvariantCulture) : string.Empty);
				changed = true;
			}
		}

		HashSet<long> assignedDiscipleIds = new HashSet<long>();
		for (int i = 0; i < peakIds.Count; i++)
		{
			int peakId = peakIds[i];
			List<long> inner = FilterAssignmentList(
				XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyPeakInnerPrefix + peakId),
				validSet,
				zongZhuId,
				elderSet,
				peakMasterSet,
				assignedDiscipleIds);
			List<long> storedInner = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyPeakInnerPrefix + peakId);
			if (!SequenceEqual(storedInner, inner))
			{
				XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeyPeakInnerPrefix + peakId, inner);
				changed = true;
			}
			for (int j = 0; j < inner.Count; j++) assignedDiscipleIds.Add(inner[j]);

			List<long> disciples = FilterAssignmentList(
				XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakId),
				validSet,
				zongZhuId,
				elderSet,
				peakMasterSet,
				assignedDiscipleIds);
			List<long> storedDisciples = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakId);
			if (!SequenceEqual(storedDisciples, disciples))
			{
				XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakId, disciples);
				changed = true;
			}
			for (int j = 0; j < disciples.Count; j++) assignedDiscipleIds.Add(disciples[j]);
		}

		for (int i = 0; i < snapshot.Members.Count; i++)
		{
			XjZongMenMemberSnapshot member = snapshot.Members[i];
			if (!member.IsValidMember || member.Actor?.data == null) continue;
			XjZongMenMembershipWriter.ReconcileActorMirror(city, member.Actor, snapshot.CurrentYear, "SelfHealReconcile");
		}
		return changed;
	}

	private static List<int> NormalizePeakIds(City city)
	{
		List<int> source = XjZongMenCityData.ReadPeakIds(city);
		List<int> result = new List<int> { XjZongMenCityData.MainPeakId };
		HashSet<int> seen = new HashSet<int> { XjZongMenCityData.MainPeakId };
		bool keepDongTian = XjZongMenCityData.HasDongTianPeak(city)
			|| XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeySupremeElders).Count > 0;
		if (keepDongTian)
		{
			result.Add(XjZongMenCityData.SupremePeakId);
			seen.Add(XjZongMenCityData.SupremePeakId);
		}

		List<int> regular = new List<int>();
		for (int i = 0; i < source.Count; i++)
		{
			int peakId = source[i];
			if (peakId < XjZongMenCityData.FirstRegularPeakId || !seen.Add(peakId)) continue;
			city.data.get(XjZongMenCityData.KeyPeakTypePrefix + peakId, out string peakType, string.Empty);
			if (string.Equals(peakType, "DongTian", System.StringComparison.Ordinal))
			{
				XjZongMenCityData.RemovePeak(city, peakId);
				continue;
			}
			regular.Add(peakId);
		}
		regular.Sort();
		int keepRegular = System.Math.Min(XjZongMenCityData.MaxRegularPeakCount, regular.Count);
		for (int i = 0; i < keepRegular && result.Count < XjZongMenCityData.MaxPeakCount; i++) result.Add(regular[i]);
		for (int i = keepRegular; i < regular.Count; i++) XjZongMenCityData.RemovePeak(city, regular[i]);
		result.Sort();
		return result;
	}

	private static List<long> FilterRoleList(List<long> source, HashSet<long> validSet, long excludedActorId, bool requireJinDan)
	{
		List<long> result = new List<long>();
		HashSet<long> seen = new HashSet<long>();
		if (source == null) return result;
		for (int i = 0; i < source.Count; i++)
		{
			long actorId = source[i];
			if (actorId <= 0L || actorId == excludedActorId || !validSet.Contains(actorId) || !seen.Add(actorId)) continue;
			if (requireJinDan)
			{
				Actor actor = XjZongMenCityData.ResolveActor(actorId);
				if (actor?.data == null || XjZongMenCityData.GetRealmLevel(actor) < XjZongMenCityData.JinDanRealmLevel) continue;
			}
			result.Add(actorId);
		}
		return result;
	}

	private static List<long> FilterAssignmentList(
		List<long> source,
		HashSet<long> validSet,
		long excludedActorId,
		HashSet<long> elders,
		HashSet<long> peakMasters,
		HashSet<long> alreadyAssigned)
	{
		List<long> result = new List<long>();
		HashSet<long> seen = new HashSet<long>();
		if (source == null) return result;
		for (int i = 0; i < source.Count; i++)
		{
			long actorId = source[i];
			if (actorId <= 0L
				|| actorId == excludedActorId
				|| !validSet.Contains(actorId)
				|| elders.Contains(actorId)
				|| peakMasters.Contains(actorId)
				|| alreadyAssigned.Contains(actorId)
				|| !seen.Add(actorId))
			{
				continue;
			}
			result.Add(actorId);
		}
		return result;
	}

	private static List<long> FilterEvaluatedIds(List<long> source)
	{
		List<long> result = new List<long>();
		HashSet<long> seen = new HashSet<long>();
		if (source == null) return result;
		for (int i = 0; i < source.Count; i++)
		{
			long actorId = source[i];
			if (actorId <= 0L || !seen.Add(actorId)) continue;
			Actor actor = XjZongMenCityData.ResolveActor(actorId);
			if (actor?.data == null || !actor.isAlive() || !XjZongMenCityData.IsCultivator(actor)) continue;
			result.Add(actorId);
		}
		return result;
	}

	private static bool SequenceEqual(List<long> left, List<long> right)
	{
		if (ReferenceEquals(left, right)) return true;
		if (left == null || right == null || left.Count != right.Count) return false;
		for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false;
		return true;
	}

	private static bool SequenceEqual(List<int> left, List<int> right)
	{
		if (ReferenceEquals(left, right)) return true;
		if (left == null || right == null || left.Count != right.Count) return false;
		for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false;
		return true;
	}
}
