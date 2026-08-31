using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 将宗门权威状态单向投影到 WorldBox actor.data / city.data。
/// 本类是宗门镜像字段的唯一合法写入边界。
/// </summary>
internal static class XjSectProjection
{
	internal static bool HasPending => XjSectAuthorityStore.HasDirtyProjection;

	internal static void Tick(int sectBudget)
	{
		int remaining = Math.Max(0, sectBudget);
		while (remaining-- > 0 && XjSectAuthorityStore.TryDequeueDirtyProjection(out long sectId))
		{
			ProjectSect(sectId);
		}
	}

	internal static void ScheduleAll()
	{
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		for (int i = 0; i < sects.Count; i++)
		{
			if (sects[i]?.SectId > 0L) XjSectAuthorityStore.MarkProjectionDirty(sects[i].SectId);
		}
	}

	internal static void ProjectSect(long sectId)
	{
		if (sectId <= 0L || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null)
		{
			return;
		}
		XjSectAuthorityStore.RegisterSectCities(sect);
		City primaryCity = ResolvePrimaryCity(sect);
		ProjectBaseToAllCities(sect);
		if (primaryCity?.data != null) ProjectPrimaryCity(sect, primaryCity);

		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sectId);
		for (int i = 0; i < actorIds.Count; i++) ProjectActor(actorIds[i]);
	}

	internal static void ProjectActor(long actorId)
	{
		if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null) return;
		if (!XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member)
			|| !XjSectRepository.TryGetBySectId(member.SectId, out XjSectArchiveRecord sect)
			|| sect == null)
		{
			WriteActorIdentity(actor, 0L, string.Empty, string.Empty, 0, string.Empty, 0, string.Empty);
			return;
		}

		string role = XjSectMemberRole.Normalize(member.Role);
		WriteActorIdentity(
			actor,
			sect.SectId,
			sect.Name ?? string.Empty,
			XjSectMemberRole.RankDisplay(role),
			Math.Max(0, member.JoinYear),
			role,
			Math.Max(0, member.PeakId),
			ResolvePeakName(sect, role, member.PeakId));
	}

	/// <summary>
	/// 读取 actor.data 中的宗门投影镜像。权威归属仍以 XjSectAuthorityStore 为准；
	/// 这里只用于审计/恢复判断投影是否需要重建。
	/// </summary>
	internal static long ReadActorMirrorSectId(Actor actor)
	{
		if (actor?.data == null) return 0L;
		if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenId, out long value) && value > 0L) return value;
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenId, out int legacy) && legacy > 0 ? legacy : 0L;
	}

	internal static void ClearActor(long actorId)
	{
		if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null) return;
		ClearActor(actor);
	}

	internal static void ClearActor(Actor actor)
	{
		if (actor?.data == null) return;
		WriteActorIdentity(actor, 0L, string.Empty, string.Empty, 0, string.Empty, 0, string.Empty);
	}

	private static void WriteActorIdentity(
		Actor actor,
		long sectId,
		string sectName,
		string rank,
		int joinYear,
		string role,
		int peakId,
		string peakName)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		using XjActorStateRevisionStore.ReductionScope reduction = XjActorStateRevisionStore.BeginReduction(actorId);
		bool clear = sectId <= 0L
			|| !XjCultivationEligibility.CanReceiveXuanJianContent(actor)
			|| !XjCultivationEligibility.HasCultivationAptitudeTrait(actor);
		SetLongIfChanged(actor, XjActorDataKeys.XjZongMenId, clear ? 0L : sectId);
		SetStringIfChanged(actor, XjActorDataKeys.XjZongMenName, clear ? string.Empty : sectName);
		SetStringIfChanged(actor, XjActorDataKeys.XjZongMenRank, clear ? string.Empty : rank);
		SetIntIfChanged(actor, XjActorDataKeys.XjZongMenJoinYear, clear ? 0 : Math.Max(0, joinYear));
		SetStringIfChanged(actor, XjActorDataKeys.XjZongMenRole, clear ? string.Empty : role);
		SetIntIfChanged(actor, XjActorDataKeys.XjZongMenPeakId, clear ? 0 : Math.Max(0, peakId));
		SetStringIfChanged(actor, XjActorDataKeys.XjZongMenPeakName, clear ? string.Empty : peakName);
	}

	private static void SetStringIfChanged(Actor actor, string key, string value)
	{
		XjActorAccessor.TryGetString(actor, key, out string current);
		string next = value ?? string.Empty;
		if (!string.Equals(current ?? string.Empty, next, StringComparison.Ordinal)) XjActorAccessor.SetString(actor, key, next);
	}

	private static void SetIntIfChanged(Actor actor, string key, int value)
	{
		XjActorAccessor.TryGetInt(actor, key, out int current);
		if (current != value) XjActorAccessor.SetInt(actor, key, value);
	}

	private static void SetLongIfChanged(Actor actor, string key, long value)
	{
		XjActorAccessor.TryGetLong(actor, key, out long current);
		if (current != value) XjActorAccessor.SetLong(actor, key, value);
	}

	private static void ProjectBaseToAllCities(XjSectArchiveRecord sect)
	{
		HashSet<long> cityIds = new HashSet<long>();
		if (sect.CapitalCityId > 0L) cityIds.Add(sect.CapitalCityId);
		if (sect.CityIds != null)
		{
			for (int i = 0; i < sect.CityIds.Count; i++) if (sect.CityIds[i] > 0L) cityIds.Add(sect.CityIds[i]);
		}
		foreach (long cityId in cityIds)
		{
			if (!XjWorldLookupIndex.TryResolveCity(cityId, out City city) || city?.data == null) continue;
			WriteBaseCityMirror(city, sect);
		}
	}

	private static void WriteBaseCityMirror(City city, XjSectArchiveRecord sect)
	{
		if (city?.data == null || sect == null) return;
		city.data.set(XjSectCityData.KeySchemaVersion, XjSectCityData.CurrentSchemaVersion);
		city.data.set(XjSectCityData.KeySectIdMirror, sect.SectId.ToString(CultureInfo.InvariantCulture));
		city.data.set(XjSectCityData.KeyZongMenName, sect.Name ?? string.Empty);
		city.data.set(XjSectCityData.KeyCreationYear, Math.Max(0, sect.FoundingYear));
		city.data.set(XjSectCityData.KeyFounderId, sect.FounderActorId > 0L ? sect.FounderActorId.ToString(CultureInfo.InvariantCulture) : string.Empty);
		city.data.set(XjSectCityData.KeyFounderName, sect.FounderName ?? string.Empty);
		city.data.set(XjSectCityData.KeyZongZhu, sect.SovereignActorId > 0L ? sect.SovereignActorId.ToString(CultureInfo.InvariantCulture) : string.Empty);
		city.data.set(XjSectCityData.KeyZongZhuGeneration, Math.Max(1, sect.SovereignGeneration));
		city.data.set(XjSectCityData.KeyLastRecruitYear, sect.LastRecruitYear);
		XjSectCityTraitAccessor.EnsureDefaults(city);
	}

	private static void ProjectPrimaryCity(XjSectArchiveRecord sect, City city)
	{
		IReadOnlyList<XjSectMemberArchiveRecord> members = XjSectAuthorityStore.ReadMembersForSect(sect.SectId);
		List<long> memberIds = new List<long>(members.Count);
		List<long> elders = new List<long>();
		Dictionary<int, List<long>> disciplesByPeak = new Dictionary<int, List<long>>();
		for (int i = 0; i < members.Count; i++)
		{
			XjSectMemberArchiveRecord member = members[i];
			if (member?.ActorId <= 0L) continue;
			memberIds.Add(member.ActorId);
			string role = XjSectMemberRole.Normalize(member.Role);
			if (role == XjSectMemberRole.SupremeElder)
			{
				elders.Add(member.ActorId);
			}
			else if (role == XjSectMemberRole.Disciple || role == XjSectMemberRole.Member)
			{
				int peakId = role == XjSectMemberRole.Disciple ? Math.Max(0, member.PeakId) : 0;
				if (!disciplesByPeak.TryGetValue(peakId, out List<long> list))
				{
					list = new List<long>();
					disciplesByPeak[peakId] = list;
				}
				list.Add(member.ActorId);
			}
		}
		memberIds.Sort();
		elders.Sort();
		XjSectCityData.WriteIdList(city, XjSectCityData.KeyMemberIds, memberIds);
		XjSectCityData.WriteIdList(city, XjSectCityData.KeySupremeElders, elders);

		List<int> previousPeakIds = XjSectCityData.ReadPeakIds(city);
		HashSet<int> desiredPeakIds = new HashSet<int> { XjSectPeakIds.Main, XjSectPeakIds.Supreme };
		if (sect.Peaks != null)
		{
			for (int i = 0; i < sect.Peaks.Count; i++)
			{
				XjSectPeakArchiveRecord peak = sect.Peaks[i];
				if (peak?.PeakId >= XjSectPeakIds.FirstRegular) desiredPeakIds.Add(peak.PeakId);
			}
		}
		foreach (int peakId in disciplesByPeak.Keys) if (peakId >= 0) desiredPeakIds.Add(peakId);

		HashSet<int> allKnownPeakIds = new HashSet<int>(previousPeakIds);
		foreach (int peakId in desiredPeakIds) allKnownPeakIds.Add(peakId);
		foreach (int peakId in allKnownPeakIds)
		{
			if (!desiredPeakIds.Contains(peakId))
			{
				ClearPeakMirror(city, peakId);
				continue;
			}
			string name = ResolvePeakName(sect, peakId == 1 ? XjSectMemberRole.SupremeElder : XjSectMemberRole.Disciple, peakId);
			string type = peakId == 0 ? "MainPeak" : peakId == 1 ? "DongTian" : "CultivatorPeak";
			city.data.set(XjSectCityData.KeyPeakNamePrefix + peakId, name);
			city.data.set(XjSectCityData.KeyPeakTypePrefix + peakId, type);
			long masterId = peakId == 0 ? sect.SovereignActorId : ResolvePeakMasterId(sect, peakId);
			city.data.set(XjSectCityData.KeyPeakFengZhuPrefix + peakId, masterId > 0L ? masterId.ToString(CultureInfo.InvariantCulture) : string.Empty);
			XjSectCityData.WriteIdList(
				city,
				XjSectCityData.KeyPeakDisciplePrefix + peakId,
				disciplesByPeak.TryGetValue(peakId, out List<long> disciples) ? disciples : new List<long>());
			XjSectCityData.WriteIdList(city, XjSectCityData.KeyPeakInnerPrefix + peakId, new List<long>());
		}
		List<int> ordered = new List<int>(desiredPeakIds);
		ordered.Sort();
		XjSectCityData.WritePeakIds(city, ordered);
	}

	private static void ClearPeakMirror(City city, int peakId)
	{
		city.data.set(XjSectCityData.KeyPeakNamePrefix + peakId, string.Empty);
		city.data.set(XjSectCityData.KeyPeakTypePrefix + peakId, string.Empty);
		city.data.set(XjSectCityData.KeyPeakFengZhuPrefix + peakId, string.Empty);
		XjSectCityData.WriteIdList(city, XjSectCityData.KeyPeakDisciplePrefix + peakId, new List<long>());
		XjSectCityData.WriteIdList(city, XjSectCityData.KeyPeakInnerPrefix + peakId, new List<long>());
	}

	private static City ResolvePrimaryCity(XjSectArchiveRecord sect)
	{
		if (sect == null) return null;
		if (sect.CapitalCityId > 0L && XjWorldLookupIndex.TryResolveCity(sect.CapitalCityId, out City capital) && capital?.data != null) return capital;
		if (sect.CityIds != null)
		{
			for (int i = 0; i < sect.CityIds.Count; i++)
			{
				if (XjWorldLookupIndex.TryResolveCity(sect.CityIds[i], out City city) && city?.data != null) return city;
			}
		}
		return null;
	}

	private static long ResolvePeakMasterId(XjSectArchiveRecord sect, int peakId)
	{
		if (sect?.Peaks == null) return 0L;
		for (int i = 0; i < sect.Peaks.Count; i++) if (sect.Peaks[i]?.PeakId == peakId) return sect.Peaks[i].PeakMasterActorId;
		return 0L;
	}

	internal static string ResolvePeakName(XjSectArchiveRecord sect, string role, int peakId)
	{
		if (peakId == 0 || XjSectMemberRole.Normalize(role) == XjSectMemberRole.Sovereign) return "主峰";
		if (peakId == 1 || XjSectMemberRole.Normalize(role) == XjSectMemberRole.SupremeElder)
		{
			string baseName = sect?.Name ?? string.Empty;
			if (string.IsNullOrWhiteSpace(baseName)) return "宗门洞天";
			string name = baseName.Trim();
			string[] suffixes = { "宗门", "道宫", "仙宫", "玄宫", "宗", "门", "观", "阁", "府", "宫", "院", "寺" };
			for (int i = 0; i < suffixes.Length; i++)
			{
				if (!name.EndsWith(suffixes[i], StringComparison.Ordinal) || name.Length <= suffixes[i].Length) continue;
				name = name.Substring(0, name.Length - suffixes[i].Length);
				break;
			}
			return (string.IsNullOrWhiteSpace(name) ? "宗门" : name) + "洞天";
		}
		if (sect?.Peaks != null)
		{
			for (int i = 0; i < sect.Peaks.Count; i++)
			{
				XjSectPeakArchiveRecord peak = sect.Peaks[i];
				if (peak?.PeakId == peakId && !string.IsNullOrWhiteSpace(peak.PeakName)) return peak.PeakName.Trim();
			}
		}
		return peakId > 0 ? "第" + peakId + "峰" : string.Empty;
	}
}
