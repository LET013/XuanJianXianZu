using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门在 city.data 中的兼容持久镜像入口。宗门业务归属以SectId档案为权威；这里只维护稳定字段、列表编解码与缓存索引；
/// 成员、职阶和角色镜像写入统一交给 XjSectMembershipService。
/// </summary>
internal static partial class XjSectCityData
{
	private readonly struct CachedLongList
	{
		internal readonly string Raw;
		internal readonly long[] Ids;

		internal CachedLongList(string raw, long[] ids)
		{
			Raw = raw ?? string.Empty;
			Ids = ids ?? Array.Empty<long>();
		}
	}

	private static readonly Dictionary<string, CachedLongList> IdListCache = new Dictionary<string, CachedLongList>(StringComparer.Ordinal);

	#region City Data Keys

	internal const string KeySchemaVersion = "xuanjian.vnext.city.zongmen.schema_version";
	internal const string KeyLegacyGeneration = "xuanjian.vnext.city.zongmen.generation";
	internal const string KeyZongZhuGeneration = "xuanjian.vnext.city.zongmen.zongzhu_generation";
	internal const string KeyZongMenName = "xuanjian.vnext.city.zongmen.name";
	internal const string KeySectIdMirror = "xuanjian.v1.city.sect_id";
	internal const string KeyCreationYear = "xuanjian.vnext.city.zongmen.creation_year";
	internal const string KeyFounderId = "xuanjian.vnext.city.zongmen.founder_id";
	internal const string KeyFounderName = "xuanjian.vnext.city.zongmen.founder_name";
	internal const string KeyZongZhu = "xuanjian.vnext.city.zongmen.zongzhu_id";
	internal const string KeySupremeElders = "xuanjian.vnext.city.zongmen.supreme_elder_ids";
	internal const string KeyMemberIds = "xuanjian.vnext.city.zongmen.member_ids";
	internal const string KeyJoinEvaluatedIds = "xuanjian.vnext.city.zongmen.join_evaluated_ids";
	internal const string KeyPeakIds = "xuanjian.vnext.city.zongmen.peak_ids";
	internal const string KeyPeakNamePrefix = "xuanjian.vnext.city.zongmen.peak_name_";
	internal const string KeyPeakTypePrefix = "xuanjian.vnext.city.zongmen.peak_type_";
	internal const string KeyPeakFengZhuPrefix = "xuanjian.vnext.city.zongmen.peak_fengzhu_";
	internal const string KeyPeakDisciplePrefix = "xuanjian.vnext.city.zongmen.peak_disciple_";
	internal const string KeyPeakInnerPrefix = "xuanjian.vnext.city.zongmen.peak_inner_";
	internal const string KeyLastAssignPeriod = "xuanjian.vnext.city.zongmen.last_assign_period";
	internal const string KeyLastRecruitYear = "xuanjian.vnext.city.zongmen.last_recruit_year";

	internal const int CurrentSchemaVersion = 2;
	internal const int MainPeakId = XjSectPeakIds.Main;
	internal const int SupremePeakId = XjSectPeakIds.Supreme;
	internal const int FirstRegularPeakId = XjSectPeakIds.FirstRegular;
	internal const int MinDisciplesPerPeak = 5;
	internal const int RecruitIntervalYears = 15;
	internal const int ZiFuRealmLevel = 4;
	internal const int JinDanRealmLevel = 5;
	// 宗门界面最多显示九个山峰席位：主峰、洞天与常规峰均计入总数。
	internal const int MaxPeakCount = 14;
	internal const int MaxRegularPeakCount = 12;

	#endregion

	#region 基础查询

	internal static bool HasZongMen(City city)
	{
		return XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect) && sect?.SectId > 0L;
	}

	internal static string GetZongMenName(City city)
	{
		return XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect) && sect != null
			? (sect.Name ?? string.Empty).Trim()
			: string.Empty;
	}

	internal static int GetCreationYear(City city)
	{
		return XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect) && sect != null ? sect.FoundingYear : -1;
	}

	internal static int GetLastRecruitYear(City city)
	{
		return XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect) && sect != null ? sect.LastRecruitYear : -1;
	}

	internal static string GetFounderName(City city)
	{
		return XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect) && sect != null
			? (sect.FounderName ?? string.Empty).Trim()
			: string.Empty;
	}

	internal static string GetZongZhuName(City city)
	{
		Actor actor = GetZongZhu(city);
		return actor?.data == null ? string.Empty : actor.getName() ?? string.Empty;
	}

	internal static Actor GetZongZhu(City city)
	{
		long actorId = GetZongZhuId(city);
		Actor actor = ResolveActor(actorId);
		return actor?.data != null && actor.isAlive() ? actor : null;
	}

	internal static long GetZongZhuId(City city)
	{
		return XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect) && sect != null ? sect.SovereignActorId : 0L;
	}

	internal static void SetZongZhuId(City city, long actorId)
	{
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return;
		if (actorId <= 0L)
		{
			XjSectCommands.ClearSovereign(sect.SectId, GetCurrentYearOrZero());
			return;
		}
		Actor actor = ResolveActor(actorId);
		if (actor?.data != null) XjSectCommands.ChangeSovereign(sect.SectId, actor, GetCurrentYearOrZero(), founding: false);
	}

	internal static int GetGeneration(City city)
	{
		return XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect) && sect != null ? Math.Max(1, sect.SovereignGeneration) : 0;
	}

	internal static void SetGeneration(City city, int generation)
	{
		if (XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) XjSectCommands.SetSovereignGeneration(sect.SectId, generation);
	}

	internal static int GetSupremeElderCount(City city)
	{
		return ReadAuthorityMemberIds(city, XjSectMemberRole.SupremeElder).Count;
	}

	internal static List<Actor> GetSupremeElders(City city)
	{
		List<Actor> result = new List<Actor>();
		List<long> ids = ReadAuthorityMemberIds(city, XjSectMemberRole.SupremeElder);
		for (int i = 0; i < ids.Count; i++)
		{
			Actor actor = ResolveActor(ids[i]);
			if (actor?.data != null && actor.isAlive()) result.Add(actor);
		}
		return result;
	}

	internal static void SetLastRecruitYear(City city, int year)
	{
		if (XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) XjSectCommands.SetLastRecruitYear(sect.SectId, year);
	}

	#endregion

	#region 成员管理

	internal static bool IsMember(City city, Actor actor)
	{
		if (actor?.data == null || !XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return false;
		long actorId = GetActorId(actor);
		return XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member) && member.SectId == sect.SectId;
	}

	internal static void AddMember(City city, Actor actor)
	{
		XjSectMembershipService.EnsureMember(city, actor, GetCurrentYearOrZero(), "CityDataAddMember");
	}

	internal static void RemoveMember(City city, long actorId)
	{
		XjSectMembershipService.RemoveMember(city, actorId, "CityDataRemoveMember");
	}

	internal static List<Actor> CollectMembers(City city)
	{
		List<Actor> result = new List<Actor>();
		List<long> ids = GetMemberIds(city);
		for (int i = 0; i < ids.Count; i++)
		{
			Actor actor = ResolveActor(ids[i]);
			if (actor?.data != null && actor.isAlive()) result.Add(actor);
		}
		return result;
	}


	internal static List<Actor> CollectOrdinaryMembers(City city)
	{
		List<Actor> result = new List<Actor>();
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return result;
		IReadOnlyList<long> ids = XjSectAuthorityStore.GetActorIdsForSect(sect.SectId);
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjSectAuthorityStore.TryGetMember(ids[i], out XjSectMemberArchiveRecord member)) continue;
			string role = XjSectMemberRole.Normalize(member.Role);
			if (!string.Equals(role, XjSectMemberRole.Member, StringComparison.Ordinal)
				&& !string.Equals(role, XjSectMemberRole.Disciple, StringComparison.Ordinal)) continue;
			Actor actor = ResolveActor(ids[i]);
			if (actor?.data != null && actor.isAlive()) result.Add(actor);
		}
		return result;
	}

	internal static List<long> GetMemberIds(City city)
	{
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return new List<long>();
		return new List<long>(XjSectAuthorityStore.GetActorIdsForSect(sect.SectId));
	}

	internal static int GetStoredMemberCount(City city)
	{
		return XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect) ? XjSectAuthorityStore.CountMembers(sect.SectId) : 0;
	}

	internal static long GetZongMenId(City city)
	{
		return XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect) && sect != null ? sect.SectId : 0L;
	}

	internal static bool RebindSectMirror(City city, long sectId, string sectName)
	{
		if (city?.data == null || sectId <= 0L) return false;
		bool changed = false;
		string nextSectId = sectId.ToString(CultureInfo.InvariantCulture);
		city.data.get(KeySectIdMirror, out string currentSectId, string.Empty);
		if (!string.Equals(currentSectId ?? string.Empty, nextSectId, StringComparison.Ordinal))
		{
			city.data.set(KeySectIdMirror, nextSectId);
			changed = true;
		}

		if (!string.IsNullOrWhiteSpace(sectName) && HasZongMen(city))
		{
			string nextName = sectName.Trim();
			city.data.get(KeyZongMenName, out string currentName, string.Empty);
			if (!string.Equals(currentName ?? string.Empty, nextName, StringComparison.Ordinal))
			{
				city.data.set(KeyZongMenName, nextName);
				changed = true;
			}
		}

		return changed;
	}

	internal static bool TryResolveZongMenCity(long zongMenId, out City city)
	{
		return XjSectOwnership.TryResolvePrimaryCity(zongMenId, out city);
	}

	#endregion

	#region 工具

	internal static int GetRealmLevel(Actor actor)
	{
		if (actor?.data == null) return 0;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return XjRealmHelper.GetOrder(realmId);
	}

	internal static long GetActorId(Actor actor)
	{
		return actor?.data != null ? ((BaseSystemData)actor.data).id : 0L;
	}

	internal static long GetCityId(City city)
	{
		return city?.data != null ? city.data.id : 0L;
	}

	internal static int GetCurrentYearOrZero()
	{
		int year = XjYearTracker.CurrentYear;
		if (year < 0) year = World.world?.map_stats?.year ?? -1;
		return year < 0 ? 0 : year;
	}

	internal static Actor ResolveActor(long actorId)
	{
		if (actorId <= 0L) return null;
		return XjScheduler.ResolveActor(actorId, out Actor actor) ? actor : null;
	}

	#endregion

	#region ID 列表存储

	internal static List<long> ReadIdList(City city, string key)
	{
		if (key == KeyMemberIds) return GetMemberIds(city);
		if (key == KeySupremeElders) return ReadAuthorityMemberIds(city, XjSectMemberRole.SupremeElder);
		if (key != null && key.StartsWith(KeyPeakDisciplePrefix, StringComparison.Ordinal)
			&& int.TryParse(key.Substring(KeyPeakDisciplePrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int peakId))
		{
			return ReadAuthorityPeakDisciples(city, peakId);
		}
		return ReadLegacyIdList(city, key);
	}

	internal static List<long> ReadLegacyIdList(City city, string key)
	{
		if (city?.data == null) return new List<long>();
		city.data.get(key, out string raw, string.Empty);
		raw ??= string.Empty;
		string cacheKey = BuildListCacheKey(city, key);
		if (IdListCache.TryGetValue(cacheKey, out CachedLongList cached)
			&& string.Equals(cached.Raw, raw, StringComparison.Ordinal))
		{
			return cached.Ids.Length == 0 ? new List<long>() : new List<long>(cached.Ids);
		}
		List<long> result = new List<long>();
		if (!string.IsNullOrWhiteSpace(raw))
		{
			string[] parts = raw.Split('|');
			for (int i = 0; i < parts.Length; i++)
			{
				if (long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && id > 0L) result.Add(id);
			}
		}
		IdListCache[cacheKey] = new CachedLongList(raw, result.ToArray());
		return result;
	}

	internal static bool ContainsId(City city, string key, long actorId)
	{
		if (city?.data == null || actorId <= 0L) return false;
		long[] ids = ReadIdArray(city, key);
		for (int i = 0; i < ids.Length; i++) if (ids[i] == actorId) return true;
		return false;
	}


	private static long[] ReadIdArray(City city, string key)
	{
		List<long> ids = ReadIdList(city, key);
		return ids.Count == 0 ? Array.Empty<long>() : ids.ToArray();
	}

	internal static void WriteIdList(City city, string key, List<long> ids)
	{
		if (city?.data == null) return;
		string cacheKey = BuildListCacheKey(city, key);
		string nextRaw = ids == null || ids.Count == 0 ? string.Empty : string.Join("|", ids);
		city.data.get(key, out string currentRaw, string.Empty);
		if (!string.Equals(currentRaw ?? string.Empty, nextRaw, StringComparison.Ordinal))
		{
			city.data.set(key, nextRaw);
		}

		IdListCache[cacheKey] = new CachedLongList(
			nextRaw,
			ids == null || ids.Count == 0 ? Array.Empty<long>() : ids.ToArray());
	}

	private static List<long> ReadAuthorityMemberIds(City city, string role)
	{
		List<long> result = new List<long>();
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return result;
		IReadOnlyList<long> ids = XjSectAuthorityStore.GetActorIdsForSect(sect.SectId);
		for (int i = 0; i < ids.Count; i++)
		{
			if (XjSectAuthorityStore.TryGetMember(ids[i], out XjSectMemberArchiveRecord member)
				&& string.Equals(XjSectMemberRole.Normalize(member.Role), role, StringComparison.Ordinal)) result.Add(ids[i]);
		}
		return result;
	}

	private static List<long> ReadAuthorityPeakDisciples(City city, int peakId)
	{
		List<long> result = new List<long>();
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return result;
		IReadOnlyList<long> ids = XjSectAuthorityStore.GetActorIdsForSect(sect.SectId);
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjSectAuthorityStore.TryGetMember(ids[i], out XjSectMemberArchiveRecord member)) continue;
			string role = XjSectMemberRole.Normalize(member.Role);
			if ((role == XjSectMemberRole.Disciple && member.PeakId == peakId)
				|| (role == XjSectMemberRole.Member && peakId == MainPeakId)) result.Add(ids[i]);
		}
		return result;
	}

	internal static void ClearRuntimeCache()
	{
		IdListCache.Clear();
	}

	private static void ClearListCache(City city)
	{
		if (city?.data == null) return;
		string prefix = GetCityId(city).ToString(CultureInfo.InvariantCulture) + "|";
		List<string> keysToRemove = null;
		foreach (KeyValuePair<string, CachedLongList> pair in IdListCache)
		{
			if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
			keysToRemove ??= new List<string>();
			keysToRemove.Add(pair.Key);
		}
		if (keysToRemove == null) return;
		for (int i = 0; i < keysToRemove.Count; i++) IdListCache.Remove(keysToRemove[i]);
	}

	private static string BuildListCacheKey(City city, string key)
	{
		return GetCityId(city).ToString(CultureInfo.InvariantCulture) + "|" + (key ?? string.Empty);
	}

	internal static List<int> ReadPeakIds(City city)
	{
		List<int> result = new List<int>();
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) return result;
		result.Add(MainPeakId);
		// 洞天是宗门固定的高境席位，不再等到出现老祖或秘境后才临时生成。
		result.Add(SupremePeakId);
		if (sect.Peaks != null)
		{
			for (int i = 0; i < sect.Peaks.Count; i++)
			{
				int id = sect.Peaks[i]?.PeakId ?? -1;
				if (id >= FirstRegularPeakId && !result.Contains(id)) result.Add(id);
			}
		}
		result.Sort();
		return result;
	}

	internal static void WritePeakIds(City city, List<int> ids)
	{
		if (city?.data == null) return;
		string next = ids == null || ids.Count == 0 ? string.Empty : string.Join("|", ids);
		city.data.get(KeyPeakIds, out string current, string.Empty);
		if (!string.Equals(current ?? string.Empty, next, StringComparison.Ordinal)) city.data.set(KeyPeakIds, next);
	}

	internal static bool TryReadActorId(City city, string key, out long actorId)
	{
		actorId = 0L;
		if (key == KeyZongZhu)
		{
			actorId = GetZongZhuId(city);
			return actorId > 0L;
		}
		if (key != null && key.StartsWith(KeyPeakFengZhuPrefix, StringComparison.Ordinal)
			&& int.TryParse(key.Substring(KeyPeakFengZhuPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int peakId)
			&& XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect))
		{
			if (peakId == MainPeakId) actorId = sect.SovereignActorId;
			else if (sect.Peaks != null)
			{
				for (int i = 0; i < sect.Peaks.Count; i++) if (sect.Peaks[i]?.PeakId == peakId) { actorId = sect.Peaks[i].PeakMasterActorId; break; }
			}
			return actorId > 0L;
		}
		return TryReadLegacyActorId(city, key, out actorId);
	}

	internal static bool TryReadLegacyActorId(City city, string key, out long actorId)
	{
		actorId = 0L;
		if (city?.data == null) return false;
		city.data.get(key, out string raw, string.Empty);
		return !string.IsNullOrWhiteSpace(raw)
			&& long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out actorId)
			&& actorId > 0L;
	}

	#endregion

	#region 清理

	internal static void Clear(City city)
	{
		if (city?.data == null) return;
		// 这里只清理指定城镇的原生显示镜像。角色宗门身份由 Sect 权威表决定，
		// 城镇失去宗门绑定时绝不能顺带清空整个宗门成员的角色镜像。
		List<int> peakIds = ReadPeakIds(city);

		for (int i = 0; i < peakIds.Count; i++)
		{
			int peakId = peakIds[i];
			city.data.set(KeyPeakNamePrefix + peakId, string.Empty);
			city.data.set(KeyPeakTypePrefix + peakId, string.Empty);
			city.data.set(KeyPeakFengZhuPrefix + peakId, string.Empty);
			city.data.set(KeyPeakDisciplePrefix + peakId, string.Empty);
			city.data.set(KeyPeakInnerPrefix + peakId, string.Empty);
		}

		ClearListCache(city);
		city.data.set(KeySchemaVersion, 0);
		city.data.set(KeyLegacyGeneration, 0);
		city.data.set(KeyZongZhuGeneration, 0);
		city.data.set(KeyZongMenName, string.Empty);
		city.data.set(KeySectIdMirror, string.Empty);
		city.data.set(KeyCreationYear, -1);
		city.data.set(KeyFounderId, string.Empty);
		city.data.set(KeyFounderName, string.Empty);
		city.data.set(KeyZongZhu, string.Empty);
		city.data.set(KeyMemberIds, string.Empty);
		city.data.set(KeyJoinEvaluatedIds, string.Empty);
		city.data.set(KeyPeakIds, string.Empty);
		city.data.set(KeyLastAssignPeriod, -1);
		city.data.set(KeyLastRecruitYear, -1);
		city.data.set(KeySupremeElders, string.Empty);
		XjSectCityTraitAccessor.Clear(city);
	}

	#endregion
}
