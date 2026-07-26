using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.ZongMen;

/// <summary>
/// 宗门在 city.data 中的持久化数据入口。这里只维护稳定字段、列表编解码与缓存索引；
/// 成员、职阶和角色镜像写入统一交给 XjZongMenMembershipWriter。
/// </summary>
internal static partial class XjZongMenCityData
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
	private static readonly Dictionary<long, City> ZongMenCityById = new Dictionary<long, City>();

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
	internal const int MainPeakId = 0;
	internal const int SupremePeakId = 1;
	internal const int FirstRegularPeakId = 2;
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
		if (city?.data == null) return false;
		city.data.get(KeyZongMenName, out string name, string.Empty);
		if (string.IsNullOrWhiteSpace(name)) return false;

		// 宗门名称是城市宗门存在性的权威标记。旧存档或中途版本可能缺少
		// schema/generation 字段，但读取阶段必须保持只读，也不能因此把
		// 已有宗门误判为空城并再次进入创宗链。
		long cacheId = GetCityId(city);
		city.data.get(KeySectIdMirror, out string sectIdRaw, string.Empty);
		if (long.TryParse(sectIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long sectId) && sectId > 0L)
		{
			cacheId = sectId;
		}
		if (cacheId > 0L) ZongMenCityById[cacheId] = city;
		return true;
	}

	internal static string GetZongMenName(City city)
	{
		if (city?.data == null) return string.Empty;
		city.data.get(KeyZongMenName, out string name, string.Empty);
		return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
	}

	internal static int GetCreationYear(City city)
	{
		if (city?.data == null) return -1;
		city.data.get(KeyCreationYear, out int year, -1);
		return year;
	}

	internal static int GetLastRecruitYear(City city)
	{
		if (city?.data == null) return -1;
		city.data.get(KeyLastRecruitYear, out int year, -1);
		return year;
	}

	internal static string GetFounderName(City city)
	{
		if (city?.data == null) return string.Empty;
		city.data.get(KeyFounderName, out string name, string.Empty);
		return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
	}

	internal static string GetZongZhuName(City city)
	{
		Actor actor = GetZongZhu(city);
		return actor?.data == null ? string.Empty : actor.getName() ?? string.Empty;
	}

	internal static Actor GetZongZhu(City city)
	{
		if (!TryReadActorId(city, KeyZongZhu, out long actorId)) return null;
		Actor actor = ResolveActor(actorId);
		return actor?.data != null && actor.isAlive() ? actor : null;
	}

	internal static long GetZongZhuId(City city)
	{
		return TryReadActorId(city, KeyZongZhu, out long actorId) ? actorId : 0L;
	}

	internal static void SetZongZhuId(City city, long actorId)
	{
		if (city?.data == null) return;
		string next = actorId > 0L ? actorId.ToString(CultureInfo.InvariantCulture) : string.Empty;
		city.data.get(KeyZongZhu, out string current, string.Empty);
		if (!string.Equals(current ?? string.Empty, next, StringComparison.Ordinal)) city.data.set(KeyZongZhu, next);
	}

	internal static int GetGeneration(City city)
	{
		if (city?.data == null || !HasZongMen(city)) return 0;
		city.data.get(KeyZongZhuGeneration, out int generation, 0);
		if (generation > 0) return generation;
		city.data.get(KeyLegacyGeneration, out int legacyGeneration, 0);
		return legacyGeneration > 0 ? legacyGeneration : 1;
	}

	internal static void SetGeneration(City city, int generation)
	{
		if (city?.data == null) return;
		int next = generation <= 0 ? 1 : generation;
		city.data.get(KeyZongZhuGeneration, out int current, 0);
		if (current != next) city.data.set(KeyZongZhuGeneration, next);
	}

	internal static int GetSupremeElderCount(City city)
	{
		return city?.data == null ? 0 : ReadIdList(city, KeySupremeElders).Count;
	}

	internal static List<Actor> GetSupremeElders(City city)
	{
		List<Actor> result = new List<Actor>();
		if (city?.data == null) return result;
		List<long> ids = ReadIdList(city, KeySupremeElders);
		for (int i = 0; i < ids.Count; i++)
		{
			Actor actor = ResolveActor(ids[i]);
			if (actor?.data != null && actor.isAlive()) result.Add(actor);
		}
		return result;
	}

	internal static void SetLastRecruitYear(City city, int year)
	{
		if (city?.data == null) return;
		city.data.get(KeyLastRecruitYear, out int current, -1);
		if (current != year) city.data.set(KeyLastRecruitYear, year);
	}

	#endregion

	#region 成员管理

	internal static bool IsMember(City city, Actor actor)
	{
		if (city?.data == null || actor?.data == null) return false;
		long actorId = GetActorId(actor);
		return actorId > 0L && ContainsId(city, KeyMemberIds, actorId);
	}

	internal static void AddMember(City city, Actor actor)
	{
		XjZongMenMembershipWriter.EnsureMember(city, actor, GetCurrentYearOrZero(), "CityDataAddMember");
	}

	internal static void RemoveMember(City city, long actorId)
	{
		XjZongMenMembershipWriter.RemoveMember(city, actorId, "CityDataRemoveMember");
	}

	internal static List<Actor> CollectMembers(City city)
	{
		List<Actor> result = new List<Actor>();
		if (city?.data == null) return result;
		List<long> ids = ReadIdList(city, KeyMemberIds);
		for (int i = 0; i < ids.Count; i++)
		{
			Actor actor = ResolveActor(ids[i]);
			if (actor?.data != null && actor.isAlive()) result.Add(actor);
		}
		return result;
	}

	internal static List<long> GetMemberIds(City city)
	{
		return city?.data == null ? new List<long>(0) : ReadIdList(city, KeyMemberIds);
	}

	internal static int GetStoredMemberCount(City city)
	{
		return city?.data == null ? 0 : GetIdCount(city, KeyMemberIds);
	}

	internal static long GetZongMenId(City city)
	{
		if (!HasZongMen(city) || city?.data == null) return 0L;
		city.data.get(KeySectIdMirror, out string raw, string.Empty);
		if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long sectId) && sectId > 0L)
		{
			return sectId;
		}
		return GetCityId(city);
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

		if (changed)
		{
			long cityId = GetCityId(city);
			if (cityId > 0L) ZongMenCityById.Remove(cityId);
			ZongMenCityById[sectId] = city;
		}
		return changed;
	}

	internal static bool TryResolveZongMenCity(long zongMenId, out City city)
	{
		city = null;
		if (zongMenId <= 0L) return false;
		if (ZongMenCityById.TryGetValue(zongMenId, out City cached)
			&& cached?.data != null
			&& HasZongMen(cached)
			&& GetZongMenId(cached) == zongMenId)
		{
			city = cached;
			return true;
		}

		ZongMenCityById.Remove(zongMenId);
		if (XjWorldLookupIndex.TryResolveCity(zongMenId, out City indexed)
			&& indexed?.data != null
			&& HasZongMen(indexed)
			&& GetZongMenId(indexed) == zongMenId)
		{
			ZongMenCityById[zongMenId] = indexed;
			city = indexed;
			return true;
		}
		return false;
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
		if (city?.data == null) return new List<long>();
		city.data.get(key, out string raw, string.Empty);
		raw ??= string.Empty;
		string cacheKey = BuildListCacheKey(city, key);
		if (IdListCache.TryGetValue(cacheKey, out CachedLongList cached)
			&& string.Equals(cached.Raw, raw, StringComparison.Ordinal))
		{
			return cached.Ids.Length == 0 ? new List<long>() : new List<long>(cached.Ids);
		}

		if (string.IsNullOrWhiteSpace(raw))
		{
			IdListCache[cacheKey] = new CachedLongList(raw, Array.Empty<long>());
			return new List<long>();
		}

		List<long> result = new List<long>();
		string[] parts = raw.Split('|');
		for (int i = 0; i < parts.Length; i++)
		{
			if (long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && id > 0L)
			{
				result.Add(id);
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

	private static int GetIdCount(City city, string key)
	{
		return city?.data == null ? 0 : ReadIdArray(city, key).Length;
	}

	private static long[] ReadIdArray(City city, string key)
	{
		if (city?.data == null) return Array.Empty<long>();
		city.data.get(key, out string raw, string.Empty);
		raw ??= string.Empty;
		string cacheKey = BuildListCacheKey(city, key);
		if (IdListCache.TryGetValue(cacheKey, out CachedLongList cached)
			&& string.Equals(cached.Raw, raw, StringComparison.Ordinal))
		{
			return cached.Ids;
		}

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

	internal static void ClearRuntimeCache()
	{
		IdListCache.Clear();
		ZongMenCityById.Clear();
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
		if (city?.data == null) return result;
		city.data.get(KeyPeakIds, out string raw, string.Empty);
		if (string.IsNullOrWhiteSpace(raw)) return result;
		string[] parts = raw.Split('|');
		for (int i = 0; i < parts.Length; i++)
		{
			if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id >= 0)
			{
				result.Add(id);
			}
		}
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
		List<int> peakIds = ReadPeakIds(city);
		List<long> memberIds = ReadIdList(city, KeyMemberIds);
		for (int i = 0; i < memberIds.Count; i++)
		{
			Actor actor = ResolveActor(memberIds[i]);
			if (actor?.data != null) XjZongMenAccessor.WriteIdentity(actor, XjZongMenIdentitySnapshot.Empty);
		}

		for (int i = 0; i < peakIds.Count; i++)
		{
			int peakId = peakIds[i];
			city.data.set(KeyPeakNamePrefix + peakId, string.Empty);
			city.data.set(KeyPeakTypePrefix + peakId, string.Empty);
			city.data.set(KeyPeakFengZhuPrefix + peakId, string.Empty);
			city.data.set(KeyPeakDisciplePrefix + peakId, string.Empty);
			city.data.set(KeyPeakInnerPrefix + peakId, string.Empty);
		}

		long cityId = GetCityId(city);
		long sectId = GetZongMenId(city);
		if (cityId > 0L) ZongMenCityById.Remove(cityId);
		if (sectId > 0L) ZongMenCityById.Remove(sectId);
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
		XjZongMenCityTraitAccessor.Clear(city);
	}

	#endregion
}
