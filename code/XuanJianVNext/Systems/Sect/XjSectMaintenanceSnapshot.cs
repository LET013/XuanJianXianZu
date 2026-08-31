using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

internal readonly struct XjSectMemberMaintenanceSnapshot
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

	internal XjSectMemberMaintenanceSnapshot(
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
internal readonly struct XjSectMaintenanceSnapshot
{
	internal readonly City City;
	internal readonly long ZongMenId;
	internal readonly string ZongMenName;
	internal readonly int CurrentYear;
	internal readonly IReadOnlyList<XjSectMemberMaintenanceSnapshot> Members;

	internal XjSectMaintenanceSnapshot(
		City city,
		long zongMenId,
		string zongMenName,
		int currentYear,
		IReadOnlyList<XjSectMemberMaintenanceSnapshot> members)
	{
		City = city;
		ZongMenId = zongMenId < 0L ? 0L : zongMenId;
		ZongMenName = zongMenName ?? string.Empty;
		CurrentYear = currentYear < 0 ? 0 : currentYear;
		Members = members ?? Array.Empty<XjSectMemberMaintenanceSnapshot>();
	}

	internal static XjSectMaintenanceSnapshot Build(City city, int currentYear)
	{
		if (city?.data == null || !XjSectOwnership.TryResolve(city, out var sect) || sect?.SectId <= 0L)
		{
			return new XjSectMaintenanceSnapshot(city, 0L, string.Empty, currentYear, Array.Empty<XjSectMemberMaintenanceSnapshot>());
		}

		IReadOnlyList<long> memberIds = XjSectAuthorityStore.GetActorIdsForSect(sect.SectId);
		List<XjSectMemberMaintenanceSnapshot> members = new List<XjSectMemberMaintenanceSnapshot>(memberIds.Count);
		for (int i = 0; i < memberIds.Count; i++)
		{
			long actorId = memberIds[i];
			if (!XjSectAuthorityStore.TryGetMember(actorId, out var membership) || membership?.SectId != sect.SectId) continue;
			Actor actor = XjScheduler.ResolveActor(actorId, out Actor resolved) ? resolved : null;
			bool alive = actor?.data != null && actor.isAlive();
			bool inCity = alive && actor.city == city;
			bool cultivator = alive
				&& !XjLongShuSystem.IsLongShu(actor)
				&& XjCultivationEligibility.CanReceiveXuanJianContent(actor)
				&& !XjCultivationPathRules.IsShi(actor)
				&& XjCultivatorCache.IsCultivator(actorId);
			int realmLevel = alive
				? XjRealmHelper.GetOrder(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter))
				: 0;
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int xianJiCount);
			bool isFoundationLateOrHigher = alive
				&& XjDaoXingStageRules.IsZhuJiLateOrHigher(realmId, zhenYuan, xianJiCount);
			members.Add(new XjSectMemberMaintenanceSnapshot(
				actorId, actor, alive, inCity, cultivator, realmLevel, zhenYuan, isFoundationLateOrHigher,
				XjSectMemberRole.Normalize(membership.Role), Math.Max(0, membership.PeakId)));
		}

		return new XjSectMaintenanceSnapshot(
			city, sect.SectId, sect.Name ?? string.Empty, currentYear, members);
	}

	internal List<XjSectMemberMaintenanceSnapshot> CollectValidMembersSorted()
	{
		List<XjSectMemberMaintenanceSnapshot> result = new List<XjSectMemberMaintenanceSnapshot>();
		HashSet<long> seen = new HashSet<long>();
		for (int i = 0; i < Members.Count; i++)
		{
			if (Members[i].IsValidMember && seen.Add(Members[i].ActorId)) result.Add(Members[i]);
		}
		result.Sort(CompareForLeadership);
		return result;
	}

	internal bool TryGet(long actorId, out XjSectMemberMaintenanceSnapshot member)
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

	internal static int CompareForLeadership(XjSectMemberMaintenanceSnapshot left, XjSectMemberMaintenanceSnapshot right)
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
