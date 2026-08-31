using System;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.YaoShu;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Sect-owned membership write boundary.  All enrollment/role changes enter
/// XjSectCommands here; legacy ZongMen callers may forward to this service but Sect
/// runtime code never needs to call back into the compatibility namespace.
/// </summary>
internal static class XjSectMembershipService
{
	internal const string RoleMember = XjSectMemberRole.Member;
	internal const string RoleDisciple = XjSectMemberRole.Disciple;
	internal const string RolePeakMaster = XjSectMemberRole.PeakMaster;
	internal const string RoleSupremeElder = XjSectMemberRole.SupremeElder;
	internal const string RoleZongZhu = XjSectMemberRole.Sovereign;

	internal static bool EnsureMember(City city, Actor actor, int currentYear, string reason)
	{
		if (!CanEnroll(actor) || !XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect) || !CanJoinSect(actor, sect)) return false;
		long actorId = GetActorId(actor);
		bool added = !XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord before)
			|| before.SectId != sect.SectId;
		bool changed = XjSectCommands.EnrollMember(sect.SectId, actor, currentYear);
		if (added && (changed || XjSectAuthorityStore.TryGetSectId(actorId, out _)))
		{
			XjCultivationSeed.RefreshChuShenForCultivationState(actor);
			XjGongFaProgression.PublishInheritanceSnapshot(actor, "ZongMenMemberAdded");
			XjThreeBookWriter.RecordSectEnrollment(sect.SectId, sect.Name, actor, currentYear);
		}
		return changed;
	}

	internal static bool RemoveMember(City city, long actorId, string reason)
	{
		if (actorId <= 0L || !XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return false;
		return XjSectCommands.RemoveMember(sect.SectId, actorId, CurrentYear());
	}

	internal static bool AssignDisciple(City city, int peakId, Actor actor, int currentYear, string reason)
	{
		return CanEnroll(actor)
			&& XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect) && CanJoinSect(actor, sect)
			&& XjSectCommands.AssignDisciple(sect.SectId, peakId, actor, currentYear);
	}

	internal static bool AssignPeakMaster(City city, int peakId, Actor actor, int currentYear, string reason)
	{
		return CanEnroll(actor)
			&& XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect) && CanJoinSect(actor, sect)
			&& XjSectCommands.AssignPeakMaster(sect.SectId, peakId, actor, currentYear, ResolvePeakName(sect, peakId));
	}

	internal static bool ClearPeakMaster(City city, int peakId, int currentYear, string reason)
	{
		return XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)
			&& XjSectCommands.ClearPeakMaster(sect.SectId, peakId, currentYear);
	}

	internal static bool AssignSupremeElder(City city, Actor actor, int currentYear, string reason)
	{
		return CanEnroll(actor)
			&& XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect) && CanJoinSect(actor, sect)
			&& XjSectCommands.AssignSupremeElder(sect.SectId, actor, currentYear);
	}

	internal static bool AssignZongZhu(City city, Actor actor, int currentYear, bool founding, string reason)
	{
		return CanEnroll(actor)
			&& XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect) && CanJoinSect(actor, sect)
			&& XjSectCommands.ChangeSovereign(sect.SectId, actor, currentYear, founding);
	}

	internal static void RemoveFromRoleLists(City city, long actorId, bool includeZongZhu)
	{
		if (actorId <= 0L || !XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return;
		XjSectCommands.RemoveFromRoles(sect.SectId, actorId, CurrentYear(), includeZongZhu);
	}

	internal static void ReconcileActorMirror(City city, Actor actor, int currentYear, string reason)
	{
		if (actor?.data == null) return;
		long actorId = GetActorId(actor);
		if (actorId <= 0L) return;
		if (!CanEnroll(actor) || (XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord resolvedSect) && !CanJoinSect(actor, resolvedSect)))
		{
			if (XjSectAuthorityStore.TryGetSectId(actorId, out long sectId))
				XjSectCommands.RemoveMember(sectId, actorId, currentYear);
			XjSectProjection.ClearActor(actorId);
			return;
		}
		XjSectProjection.ProjectActor(actorId);
	}

	private static bool CanEnroll(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()
			|| !XjCultivationEligibility.CanReceiveXuanJianContent(actor)
			|| XjLongShuSystem.IsLongShu(actor)
			|| XjYinSiTraitLifecycle.IsYinSi(actor)
			|| !XjCultivationEligibility.HasCultivationAptitudeTrait(actor)) return false;

		if (XjXianGuoSystem.IsDiMingYang(actor))
		{
			long actorId = GetActorId(actor);
			return actorId > 0L && XjSectAuthorityStore.TryGetSectId(actorId, out _);
		}
		return true;
	}

	private static bool CanJoinSect(Actor actor, XjSectArchiveRecord sect)
	{
		if (!XjYaoShuGreatSageSystem.IsGreatSage(actor)) return true;
		return !string.IsNullOrWhiteSpace(sect?.Name) && sect.Name.Contains("妖", StringComparison.Ordinal);
	}

	private static long GetActorId(Actor actor)
	{
		try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
		catch { return 0L; }
	}

	private static int CurrentYear()
	{
		try { return Math.Max(0, World.world?.map_stats?.year ?? 0); }
		catch { return 0; }
	}

	private static string ResolvePeakName(XjSectArchiveRecord sect, int peakId)
	{
		if (sect?.Peaks != null)
		{
			for (int i = 0; i < sect.Peaks.Count; i++)
			{
				XjSectPeakArchiveRecord peak = sect.Peaks[i];
				if (peak?.PeakId == peakId && !string.IsNullOrWhiteSpace(peak.PeakName)) return peak.PeakName.Trim();
			}
		}
		return peakId == XjSectPeakIds.Main ? "主峰"
			: peakId == XjSectPeakIds.Supreme ? "洞天峰"
			: peakId >= XjSectPeakIds.FirstRegular ? "无名峰" : string.Empty;
	}
}
