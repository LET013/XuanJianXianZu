using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectCityData
{
	#region 山峰管理

	internal static List<(int id, string name)> GetPeaks(City city)
	{
		List<(int id, string name)> result = new List<(int id, string name)>();
		if (city?.data == null) return result;
		List<int> peakIds = ReadPeakIds(city);
		peakIds.Sort();
		for (int i = 0; i < peakIds.Count; i++) result.Add((peakIds[i], GetPeakName(city, peakIds[i])));
		return result;
	}

	internal static List<int> GetRegularPeakIds(City city)
	{
		List<int> result = new List<int>();
		List<int> peakIds = ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			if (peakIds[i] >= FirstRegularPeakId) result.Add(peakIds[i]);
		}
		result.Sort();
		return result;
	}

	internal static string GetPeakName(City city, int peakId)
	{
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return peakId == MainPeakId ? "主峰" : "无名峰";
		string role = peakId == MainPeakId ? XjSectMemberRole.Sovereign
			: peakId == SupremePeakId ? XjSectMemberRole.SupremeElder
			: XjSectMemberRole.Disciple;
		return XjSectProjection.ResolvePeakName(sect, role, peakId);
	}

	internal static string GetDongTianPeakName(City city)
	{
		return GetPeakName(city, SupremePeakId);
	}

	internal static Actor GetPeakFengZhu(City city, int peakId)
	{
		if (!TryReadActorId(city, KeyPeakFengZhuPrefix + peakId, out long actorId)) return null;
		Actor actor = ResolveActor(actorId);
		return actor?.data != null && actor.isAlive() ? actor : null;
	}

	internal static void SetPeakFengZhu(City city, int peakId, Actor actor)
	{
		XjSectMembershipService.AssignPeakMaster(city, peakId, actor, GetCurrentYearOrZero(), "SetPeakFengZhu");
	}

	internal static void ClearPeakFengZhu(City city, int peakId)
	{
		XjSectMembershipService.ClearPeakMaster(city, peakId, GetCurrentYearOrZero(), "ClearPeakFengZhu");
	}

	internal static List<Actor> GetPeakDisciples(City city, int peakId)
	{
		List<Actor> result = new List<Actor>();
		if (city?.data == null) return result;
		List<long> ids = ReadIdList(city, KeyPeakDisciplePrefix + peakId);
		for (int i = 0; i < ids.Count; i++)
		{
			Actor actor = ResolveActor(ids[i]);
			if (actor?.data != null && actor.isAlive()) result.Add(actor);
		}
		return result;
	}

	internal static void AddPeakDisciple(City city, int peakId, Actor actor)
	{
		XjSectMembershipService.AssignDisciple(city, peakId, actor, GetCurrentYearOrZero(), "AddPeakDisciple");
	}

	internal static void RemoveActorFromAllPeaks(City city, long actorId)
	{
		if (actorId <= 0L || !XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return;
		XjSectCommands.RemoveFromRoles(sect.SectId, actorId, GetCurrentYearOrZero(), includeSovereign: false);
	}

	internal static int GetPeakDiscipleCount(City city, int peakId)
	{
		return city?.data == null ? 0 : ReadIdList(city, KeyPeakDisciplePrefix + peakId).Count;
	}

	#endregion

	#region 山峰创建与治理

	internal static void EnsureMainPeak(City city)
	{
		if (XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) XjSectAuthorityStore.MarkProjectionDirty(sect.SectId);
	}

	internal static int CreatePeak(City city, string name, string type)
	{
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return -1;
		if (GetPeaks(city).Count >= MaxPeakCount || GetRegularPeakIds(city).Count >= MaxRegularPeakCount) return -1;
		return XjSectCommands.CreatePeak(sect.SectId, NormalizePeakName(name), GetCurrentYearOrZero());
	}

	internal static bool RemovePeak(City city, int peakId)
	{
		return peakId >= FirstRegularPeakId
			&& XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)
			&& XjSectCommands.RemovePeak(sect.SectId, peakId, GetCurrentYearOrZero());
	}

	internal static int FindVacantRegularPeak(City city)
	{
		List<int> regularPeakIds = GetRegularPeakIds(city);
		for (int i = 0; i < regularPeakIds.Count; i++)
		{
			if (!TryReadActorId(city, KeyPeakFengZhuPrefix + regularPeakIds[i], out _)) return regularPeakIds[i];
		}
		return -1;
	}

	internal static int CreateDynamicPeakForFounder(City city, Actor actor, int currentYear)
	{
		return AssignOrCreatePeakForFounder(city, actor, currentYear, out _);
	}

	internal static int AssignOrCreatePeakForFounder(City city, Actor actor, int currentYear, out bool created)
	{
		created = false;
		if (city?.data == null || actor?.data == null || !IsEligiblePeakFounder(city, actor)) return -1;

		int vacantPeakId = FindVacantRegularPeak(city);
		if (vacantPeakId >= FirstRegularPeakId)
		{
			return XjSectMembershipService.AssignPeakMaster(city, vacantPeakId, actor, currentYear, "FillVacantPeakMaster")
				? vacantPeakId
				: -1;
		}

		if (!CanCreateRegularPeak(city)) return -1;
		string peakName = GeneratePeakName(city);
		int peakId = CreatePeak(city, peakName, "CultivatorPeak");
		if (peakId < 0) return -1;
		if (!XjSectMembershipService.AssignPeakMaster(city, peakId, actor, currentYear, "DynamicPeakFounder"))
		{
			RemovePeak(city, peakId);
			return -1;
		}
		created = true;
		return peakId;
	}

	private static bool CanCreateRegularPeak(City city)
	{
		List<int> peakIds = ReadPeakIds(city);
		List<int> regularPeakIds = GetRegularPeakIds(city);
		if (peakIds.Count >= MaxPeakCount || regularPeakIds.Count >= MaxRegularPeakCount) return false;
		if (FindVacantRegularPeak(city) >= FirstRegularPeakId) return false;

		long zongZhuId = GetZongZhuId(city);
		HashSet<long> elders = new HashSet<long>(ReadIdList(city, KeySupremeElders));
		int availableForRegularRoles = 0;
		List<long> memberIds = ReadIdList(city, KeyMemberIds);
		for (int i = 0; i < memberIds.Count; i++)
		{
			if (memberIds[i] == zongZhuId || elders.Contains(memberIds[i])) continue;
			Actor member = ResolveActor(memberIds[i]);
			if (member?.data != null && member.isAlive() && IsCultivator(member)) availableForRegularRoles++;
		}

		// 新峰必须同时有峰主和至少五名弟子；有宗主时主峰也保留一名门人。
		int targetRegularPeakCount = regularPeakIds.Count + 1;
		int requiredMembers = targetRegularPeakCount * (MinDisciplesPerPeak + 1) + (zongZhuId > 0L ? 1 : 0);
		return availableForRegularRoles >= requiredMembers;
	}

	internal static bool NormalizeStoredPeakNames(City city)
	{
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect) || sect?.Peaks == null) return false;
		bool changed = false;
		for (int i = 0; i < sect.Peaks.Count; i++)
		{
			XjSectPeakArchiveRecord peak = sect.Peaks[i];
			if (peak == null || peak.PeakId < FirstRegularPeakId) continue;
			string next = NormalizePeakName(peak.PeakName);
			if (string.Equals(peak.PeakName ?? string.Empty, next, StringComparison.Ordinal)) continue;
			peak.PeakName = next;
			changed = true;
		}
		if (changed) XjSectAuthorityStore.MarkProjectionDirty(sect.SectId);
		return changed;
	}

	internal static bool EnsureDongTianPeak(City city)
	{
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return false;
		bool existed = HasDongTianPeak(city);
		XjSectAuthorityStore.MarkProjectionDirty(sect.SectId);
		return !existed;
	}


	internal static bool HasDongTianPeak(City city)
	{
		return XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)
			&& (sect.SecretRealmId > 0L || GetSupremeElderCount(city) > 0);
	}

	internal static bool IsEligiblePeakFounder(City city, Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || !IsMember(city, actor) || HasActorAnyPeak(city, actor)) return false;
		int realmLevel = GetRealmLevel(actor);
		if (realmLevel >= JinDanRealmLevel) return false;
		if (realmLevel >= ZiFuRealmLevel) return true;
		if (realmLevel != 3) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int xianJiCount);
		return XjDaoXingStageRules.IsZhuJiLateOrHigher(realmId, zhenYuan, xianJiCount);
	}

	internal static bool IsEligiblePeakFounder(City city, in XjSectMemberMaintenanceSnapshot member)
	{
		if (!member.IsValidMember || member.RealmLevel >= JinDanRealmLevel || HasActorAnyPeak(city, member.ActorId)) return false;
		return member.RealmLevel >= ZiFuRealmLevel || (member.RealmLevel == 3 && member.IsFoundationLateOrHigher);
	}

	internal static bool HasActorAnyPeak(City city, Actor actor)
	{
		return HasActorAnyPeak(city, GetActorId(actor));
	}

	internal static bool HasActorAnyPeak(City city, long actorId)
	{
		if (actorId <= 0L || !XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member)) return false;
		return member.SectId == GetZongMenId(city)
			&& (XjSectMemberRole.Normalize(member.Role) == XjSectMemberRole.PeakMaster || member.PeakId >= FirstRegularPeakId);
	}

	internal static string NormalizePeakName(string name)
	{
		string value = string.IsNullOrWhiteSpace(name) ? "无名峰" : name.Trim();
		if (!value.EndsWith("峰", StringComparison.Ordinal)) value += "峰";
		return value.Length <= 3 ? value : value.Substring(0, 2) + "峰";
	}

	#endregion
}
