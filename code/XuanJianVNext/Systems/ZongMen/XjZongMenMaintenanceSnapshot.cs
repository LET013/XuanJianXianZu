using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.ZongMen;

internal readonly struct XjZongMenMemberSnapshot
{
	internal readonly long ActorId;
	internal readonly Actor Actor;
	internal readonly bool IsAlive;
	internal readonly bool IsInCity;
	internal readonly bool IsCultivator;
	internal readonly int RealmLevel;
	internal readonly float ZhenYuan;
	internal readonly bool IsFoundationLateOrHigher;
	internal readonly string Role;
	internal readonly int PeakId;

	internal bool IsValidMember => ActorId > 0L
		&& Actor?.data != null
		&& IsAlive
		&& IsCultivator;

	internal XjZongMenMemberSnapshot(
		long actorId,
		Actor actor,
		bool isAlive,
		bool isInCity,
		bool isCultivator,
		int realmLevel,
		float zhenYuan,
		bool isFoundationLateOrHigher,
		string role,
		int peakId)
	{
		ActorId = actorId < 0L ? 0L : actorId;
		Actor = actor;
		IsAlive = isAlive;
		IsInCity = isInCity;
		IsCultivator = isCultivator;
		RealmLevel = realmLevel < 0 ? 0 : realmLevel;
		ZhenYuan = zhenYuan < 0f ? 0f : zhenYuan;
		IsFoundationLateOrHigher = isFoundationLateOrHigher;
		Role = role ?? string.Empty;
		PeakId = peakId < 0 ? 0 : peakId;
	}
}

/// <summary>
/// 一轮宗门维护只构建一次的成员快照。境界、真元、真实道行阶段与角色镜像均在此读取，
/// 后续自愈、继任、分峰与升降不再重复构建 read model。
/// </summary>
internal readonly struct XjZongMenMaintenanceSnapshot
{
	internal readonly City City;
	internal readonly long ZongMenId;
	internal readonly string ZongMenName;
	internal readonly int CurrentYear;
	internal readonly IReadOnlyList<XjZongMenMemberSnapshot> Members;

	internal XjZongMenMaintenanceSnapshot(
		City city,
		long zongMenId,
		string zongMenName,
		int currentYear,
		IReadOnlyList<XjZongMenMemberSnapshot> members)
	{
		City = city;
		ZongMenId = zongMenId < 0L ? 0L : zongMenId;
		ZongMenName = zongMenName ?? string.Empty;
		CurrentYear = currentYear < 0 ? 0 : currentYear;
		Members = members ?? Array.Empty<XjZongMenMemberSnapshot>();
	}

	internal static XjZongMenMaintenanceSnapshot Build(City city, int currentYear)
	{
		if (city?.data == null || !XjZongMenCityData.HasZongMen(city))
		{
			return new XjZongMenMaintenanceSnapshot(city, 0L, string.Empty, currentYear, Array.Empty<XjZongMenMemberSnapshot>());
		}

		long zongMenId = XjZongMenCityData.GetZongMenId(city);
		List<long> memberIds = CollectPersistedCandidateIds(city);
		List<XjZongMenMemberSnapshot> members = new List<XjZongMenMemberSnapshot>(memberIds.Count);
		for (int i = 0; i < memberIds.Count; i++)
		{
			long actorId = memberIds[i];
			Actor actor = XjZongMenCityData.ResolveActor(actorId);
			bool alive = actor?.data != null && actor.isAlive();
			XjZongMenIdentitySnapshot identity = alive ? XjZongMenAccessor.BuildIdentity(actor) : XjZongMenIdentitySnapshot.Empty;
			if (identity.Found && identity.ZongMenId > 0L && identity.ZongMenId != zongMenId) continue;
			bool inCity = alive && actor.city == city;
			bool cultivator = alive
				&& !XjLongShuSystem.IsLongShu(actor)
				&& XjZongMenCityData.IsCultivator(actor);
			int realmLevel = alive ? XjZongMenCityData.GetRealmLevel(actor) : 0;
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int xianJiCount);
			bool isFoundationLateOrHigher = alive
				&& XjDaoXingStageRules.IsZhuJiLateOrHigher(realmId, zhenYuan, xianJiCount);
			members.Add(new XjZongMenMemberSnapshot(
				actorId,
				actor,
				alive,
				inCity,
				cultivator,
				realmLevel,
				zhenYuan,
				isFoundationLateOrHigher,
				identity.Role,
				identity.PeakId));
		}

		return new XjZongMenMaintenanceSnapshot(
			city,
			zongMenId,
			XjZongMenCityData.GetZongMenName(city),
			currentYear,
			members);
	}

	private static List<long> CollectPersistedCandidateIds(City city)
	{
		List<long> result = new List<long>();
		HashSet<long> seen = new HashSet<long>();
		void Add(long actorId)
		{
			if (actorId > 0L && seen.Add(actorId)) result.Add(actorId);
		}

		List<long> storedMembers = XjZongMenCityData.GetMemberIds(city);
		for (int i = 0; i < storedMembers.Count; i++) Add(storedMembers[i]);
		if (XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyFounderId, out long founderId)) Add(founderId);
		Add(XjZongMenCityData.GetZongZhuId(city));
		List<long> elders = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeySupremeElders);
		for (int i = 0; i < elders.Count; i++) Add(elders[i]);
		List<int> peakIds = XjZongMenCityData.ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			int peakId = peakIds[i];
			if (XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, out long masterId)) Add(masterId);
			List<long> inner = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyPeakInnerPrefix + peakId);
			for (int j = 0; j < inner.Count; j++) Add(inner[j]);
			List<long> disciples = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakId);
			for (int j = 0; j < disciples.Count; j++) Add(disciples[j]);
		}
		return result;
	}

	internal List<XjZongMenMemberSnapshot> CollectValidMembersSorted()
	{
		List<XjZongMenMemberSnapshot> result = new List<XjZongMenMemberSnapshot>();
		HashSet<long> seen = new HashSet<long>();
		for (int i = 0; i < Members.Count; i++)
		{
			if (Members[i].IsValidMember && seen.Add(Members[i].ActorId)) result.Add(Members[i]);
		}
		result.Sort(CompareForLeadership);
		return result;
	}

	internal bool TryGet(long actorId, out XjZongMenMemberSnapshot member)
	{
		for (int i = 0; i < Members.Count; i++)
		{
			if (Members[i].ActorId != actorId) continue;
			member = Members[i];
			return true;
		}
		member = default;
		return false;
	}

	internal static int CompareForLeadership(XjZongMenMemberSnapshot left, XjZongMenMemberSnapshot right)
	{
		int cmp = right.RealmLevel.CompareTo(left.RealmLevel);
		if (cmp != 0) return cmp;
		cmp = NormalizeSortValue(right.ZhenYuan).CompareTo(NormalizeSortValue(left.ZhenYuan));
		return cmp != 0 ? cmp : left.ActorId.CompareTo(right.ActorId);
	}

	private static float NormalizeSortValue(float value)
	{
		return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
	}
}
