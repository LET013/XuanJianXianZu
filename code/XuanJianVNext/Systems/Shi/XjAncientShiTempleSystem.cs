using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 古释寺庙是与仙修宗门分离的轻量道场：不占宗门政体、不治理城镇、不设峰脉大阵，
/// 只容纳已经是古释的角色。寺庙以宏愿、法藏、应身余泽作为底蕴。
/// </summary>
internal static class XjAncientShiTempleSystem
{
	private const int CurrentSchemaVersion = 3;
	private const int FoundingVowProgress = 20;
	// 古释寺不是仙宗扩张器：全世界至多六处，每处只以遗经自悟的方式
	// 缓慢补到 5~8 人，不向普通人直接“收徒”或跨城搜人。
	private const int MaxActiveTempleCount = 6;
	private const int DesiredMemberCount = 8;
	private const int RecruitmentProbeIntervalYears = 8;
	private const int LocalRecruitmentCandidateBudget = 24;
	private static readonly Dictionary<long, XjAncientShiTempleRecord> ByTempleId = new Dictionary<long, XjAncientShiTempleRecord>();
	private static readonly Dictionary<long, long> TempleByCityId = new Dictionary<long, long>();
	private static readonly Dictionary<long, long> TempleByActorId = new Dictionary<long, long>();
	private static readonly List<XjAncientShiTempleRecord> ReadView = new List<XjAncientShiTempleRecord>();
	private static bool _readViewDirty = true;
	private static int _lastReadRefreshYear;
	private static long _nextTempleId = 1L;

	internal static void TickActor(Actor actor, in XjShiSnapshot snapshot, int annualYear)
	{
		if (actor?.data == null || annualYear <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		bool eligibleAncient = actor.isAlive()
			&& XjCultivationPathRules.IsShi(actor)
			&& string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
		if (!eligibleAncient)
		{
			RemoveMember(actorId);
			return;
		}

		if (TempleByActorId.TryGetValue(actorId, out long existingTempleId)
			&& ByTempleId.TryGetValue(existingTempleId, out XjAncientShiTempleRecord existing))
		{
			RefreshTemple(existing, annualYear);
			TryMaintainTempleMembership(existing, actor.city, annualYear);
			return;
		}

		City city = actor.city;
		long cityId = city?.data != null ? city.data.id : 0L;
		if (cityId <= 0L) return;
		if (TempleByCityId.TryGetValue(cityId, out long cityTempleId)
			&& ByTempleId.TryGetValue(cityTempleId, out XjAncientShiTempleRecord cityTemple))
		{
			AddMember(cityTemple, actor, annualYear);
			RefreshTemple(cityTemple, annualYear);
			TryMaintainTempleMembership(cityTemple, city, annualYear);
			return;
		}

		if (XjShiCatalog.GetRank(snapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster)
			|| !XjAncientShiVowSystem.HasDeclared(actor)
			|| XjAncientShiVowSystem.GetProgress(actor) < FoundingVowProgress) return;
		XjAncientShiTempleRecord founded = FoundTemple(actor, city, annualYear);
		if (founded != null)
		{
			RefreshTemple(founded, annualYear);
			TryMaintainTempleMembership(founded, city, annualYear);
		}
	}

	internal static bool TryGetTempleForActor(Actor actor, out XjAncientShiTempleRecord temple)
	{
		temple = null;
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		return TempleByActorId.TryGetValue(actorId, out long templeId)
			&& ByTempleId.TryGetValue(templeId, out temple)
			&& temple != null;
	}

	internal static float GetPracticeMultiplier(Actor actor)
	{
		if (!TryGetTempleForActor(actor, out XjAncientShiTempleRecord temple)) return 1f;
		return 1f + Math.Min(0.08f, Math.Max(0, temple.DharmaArchive) / 12500f);
	}

	internal static int GetDharmaFormChanceBonusPerTenThousand(Actor actor)
	{
		if (!TryGetTempleForActor(actor, out XjAncientShiTempleRecord temple)) return 0;
		return Math.Min(400, Math.Max(0, temple.ResponseLegacy) * 2 / 5);
	}

	internal static bool NotifyAncientJinDiLegacy(long formerOwnerActorId, string domainId, int annualYear)
	{
		if (formerOwnerActorId <= 0L || string.IsNullOrWhiteSpace(domainId)
			|| !TempleByActorId.TryGetValue(formerOwnerActorId, out long templeId)
			|| !ByTempleId.TryGetValue(templeId, out XjAncientShiTempleRecord temple)
			|| temple == null) return false;
		temple.LegacyJinDiDomainIds ??= new List<string>();
		string stableId = domainId.Trim();
		bool changed = false;
		if (!temple.LegacyJinDiDomainIds.Contains(stableId))
		{
			temple.LegacyJinDiDomainIds.Add(stableId);
			changed = true;
		}
		int lastActiveYear = Math.Max(temple.LastActiveYear, Math.Max(1, annualYear));
		changed |= lastActiveYear != temple.LastActiveYear || temple.LastRefreshYear != 0;
		temple.LastActiveYear = lastActiveYear;
		temple.LastRefreshYear = 0;
		_readViewDirty = true;
		if (changed) XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
		return true;
	}

	internal static bool IsTempleLegacyJinDi(string domainId)
	{
		if (string.IsNullOrWhiteSpace(domainId)) return false;
		string stableId = domainId.Trim();
		foreach (XjAncientShiTempleRecord temple in ByTempleId.Values)
		{
			if (temple?.LegacyJinDiDomainIds != null && temple.LegacyJinDiDomainIds.Contains(stableId)) return true;
		}
		return false;
	}

	internal static IReadOnlyList<XjAncientShiTempleRecord> ReadActiveTemples()
	{
		int year = Math.Max(1, XjYearTracker.CurrentYear);
		if (_lastReadRefreshYear != year)
		{
			_lastReadRefreshYear = year;
			List<XjAncientShiTempleRecord> records = new List<XjAncientShiTempleRecord>(ByTempleId.Values);
			for (int i = 0; i < records.Count; i++) RefreshTemple(records[i], year);
		}
		if (_readViewDirty)
		{
			ReadView.Clear();
			foreach (XjAncientShiTempleRecord temple in ByTempleId.Values)
			{
				if (temple == null || temple.LivingMemberCount <= 0) continue;
				ReadView.Add(temple);
			}
			ReadView.Sort((left, right) =>
			{
				int high = right.ResponseLegacy.CompareTo(left.ResponseLegacy);
				if (high != 0) return high;
				high = right.DharmaArchive.CompareTo(left.DharmaArchive);
				if (high != 0) return high;
				return left.TempleId.CompareTo(right.TempleId);
			});
			_readViewDirty = false;
		}
		return ReadView;
	}

	internal static XjAncientShiTempleWorldArchiveData ExportState()
	{
		XjAncientShiTempleWorldArchiveData data = new XjAncientShiTempleWorldArchiveData
		{
			SchemaVersion = CurrentSchemaVersion,
			NextTempleId = Math.Max(1L, _nextTempleId)
		};
		foreach (XjAncientShiTempleRecord temple in ByTempleId.Values)
			if (temple != null) data.Temples.Add(Clone(temple));
		return data;
	}

	internal static void ImportState(XjAncientShiTempleWorldArchiveData data)
	{
		Clear();
		if (data?.Temples == null) return;
		_nextTempleId = Math.Max(1L, data.NextTempleId);
		for (int i = 0; i < data.Temples.Count; i++)
		{
			XjAncientShiTempleRecord record = Clone(data.Temples[i]);
			if (record == null || record.TempleId <= 0L || record.CityId <= 0L) continue;
			ByTempleId[record.TempleId] = record;
			TempleByCityId[record.CityId] = record.TempleId;
			if (record.MemberActorIds == null) record.MemberActorIds = new List<long>();
			if (record.LegacyJinDiDomainIds == null) record.LegacyJinDiDomainIds = new List<string>();
			for (int m = 0; m < record.MemberActorIds.Count; m++)
				if (record.MemberActorIds[m] > 0L) TempleByActorId[record.MemberActorIds[m]] = record.TempleId;
			_nextTempleId = Math.Max(_nextTempleId, record.TempleId + 1L);
		}
		_readViewDirty = true;
	}

	internal static void Clear()
	{
		ByTempleId.Clear();
		TempleByCityId.Clear();
		TempleByActorId.Clear();
		ReadView.Clear();
		_readViewDirty = true;
		_lastReadRefreshYear = 0;
		_nextTempleId = 1L;
	}

	private static XjAncientShiTempleRecord FoundTemple(Actor founder, City city, int year)
	{
		if (founder?.data == null || city?.data == null || !founder.isAlive()
			|| !XjCultivationPathRules.IsShi(founder)
			|| !XjShiState.TryBuildSnapshot(founder, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)) return null;
		long cityId = city.data.id;
		if (cityId <= 0L || TempleByCityId.ContainsKey(cityId)
			|| ByTempleId.Count >= MaxActiveTempleCount) return null;
		long founderId = ((BaseSystemData)founder.data).id;
		XjActorAccessor.TryGetString(founder, XjActorDataKeys.ShiVowId, out string vowId);
		string name = GenerateTempleName(founderId, cityId, vowId);
		string cityName = string.IsNullOrWhiteSpace(city.data.name) ? "无名城" : city.data.name.Trim();
		string founderName = string.IsNullOrWhiteSpace(founder.getName()) ? "未名古释" : founder.getName();
		XjAncientShiTempleRecord temple = new XjAncientShiTempleRecord
		{
			TempleId = _nextTempleId++,
			Name = name,
			CityId = cityId,
			CityName = cityName,
			FoundedYear = year,
			FounderActorId = founderId,
			FounderName = founderName,
			AbbotActorId = founderId,
			AbbotName = founderName,
			PrincipalVowId = vowId,
			LastActiveYear = year,
			LastRefreshYear = 0
		};
		ByTempleId[temple.TempleId] = temple;
		TempleByCityId[cityId] = temple.TempleId;
		AddMember(temple, founder, year);
		_readViewDirty = true;
		XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
		XjWorldHistoryStore.RecordActorEvent(founder,
			"于" + cityName + "立“" + name + "”，以所发宏愿为寺基，聚古释同修而不强纳门徒。",
			XjShiTraitIds.Ancient);
		XjWorldHistoryStore.RecordWorldEvent(
			"【古寺初立】" + founderName + "于" + cityName + "立" + name + "，古释自此有一处清净共修之所。",
			XjShiTraitIds.Ancient);
		return temple;
	}

	private static void AddMember(XjAncientShiTempleRecord temple, Actor actor, int year)
	{
		if (temple == null || actor?.data == null || !actor.isAlive()
			|| !XjCultivationPathRules.IsShi(actor)
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		bool changed = false;
		if (!temple.MemberActorIds.Contains(actorId))
		{
			temple.MemberActorIds.Add(actorId);
			changed = true;
		}
		TempleByActorId[actorId] = temple.TempleId;
		int lastActiveYear = Math.Max(temple.LastActiveYear, year);
		changed |= lastActiveYear != temple.LastActiveYear || temple.LastRefreshYear != 0;
		temple.LastActiveYear = lastActiveYear;
		temple.LastRefreshYear = 0;
		_readViewDirty = true;
		if (changed) XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
	}

	private static void RemoveMember(long actorId)
	{
		if (actorId <= 0L || !TempleByActorId.TryGetValue(actorId, out long templeId)) return;
		TempleByActorId.Remove(actorId);
		bool changed = false;
		if (ByTempleId.TryGetValue(templeId, out XjAncientShiTempleRecord temple) && temple?.MemberActorIds != null)
		{
			changed = temple.MemberActorIds.Remove(actorId);
			changed |= temple.LastRefreshYear != 0;
			temple.LastRefreshYear = 0;
		}
		_readViewDirty = true;
		if (changed) XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
	}

	private static void RefreshTemple(XjAncientShiTempleRecord temple, int year)
	{
		if (temple == null || temple.LastRefreshYear == year) return;
		temple.LastRefreshYear = year;
		int living = 0;
		int vowFoundation = 0;
		int legacyJinDiCount = temple.LegacyJinDiDomainIds?.Count ?? 0;
		int dharmaArchive = Math.Min(200, Math.Max(0, year - temple.FoundedYear) * 2)
			+ Math.Min(400, legacyJinDiCount * 80);
		int responseLegacy = Math.Min(600, legacyJinDiCount * 160);
		long previousAbbotId = temple.AbbotActorId;
		bool currentAbbotValid = false;
		long bestAbbotId = 0L;
		string bestAbbotName = string.Empty;
		int bestRank = -1;
		List<long> invalid = null;
		for (int i = 0; i < temple.MemberActorIds.Count; i++)
		{
			long actorId = temple.MemberActorIds[i];
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)
				|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
				|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
			{
				invalid ??= new List<long>();
				invalid.Add(actorId);
				continue;
			}
			living++;
			if (actorId == previousAbbotId)
			{
				currentAbbotValid = true;
				temple.AbbotName = string.IsNullOrWhiteSpace(actor.getName()) ? temple.AbbotName : actor.getName();
			}
			int rank = XjShiCatalog.GetRank(snapshot.Realm);
			vowFoundation += XjAncientShiVowSystem.GetProgress(actor);
			dharmaArchive += rank <= XjShiCatalog.GetRank(XjShiRealmIds.Monk) ? 4
				: rank <= XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster) ? 12
				: rank <= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm) ? 80 : 220;
			if (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) responseLegacy += 120;
			else if (string.Equals(snapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) responseLegacy += 320;
			if (rank > bestRank || rank == bestRank && actorId < bestAbbotId)
			{
				bestRank = rank;
				bestAbbotId = actorId;
				bestAbbotName = string.IsNullOrWhiteSpace(actor.getName()) ? "未名古释" : actor.getName();
			}
		}
		if (invalid != null)
		{
			for (int i = 0; i < invalid.Count; i++)
			{
				temple.MemberActorIds.Remove(invalid[i]);
				TempleByActorId.Remove(invalid[i]);
			}
		}
		temple.LivingMemberCount = living;
		temple.VowFoundation = Math.Min(1000, Math.Max(0, vowFoundation));
		temple.DharmaArchive = Math.Min(1000, Math.Max(0, dharmaArchive));
		temple.ResponseLegacy = Math.Min(1000, Math.Max(0, responseLegacy));
		if (living > 0 && !currentAbbotValid && bestAbbotId > 0L)
		{
			temple.AbbotActorId = bestAbbotId;
			temple.AbbotName = bestAbbotName;
			temple.LastActiveYear = year;
			if (previousAbbotId > 0L && previousAbbotId != bestAbbotId)
			{
				XjWorldHistoryStore.RecordWorldEvent(
					"【古寺续灯】" + temple.Name + "旧住持不在，" + bestAbbotName + "承接寺中法灯。",
					XjShiTraitIds.Ancient);
			}
		}
		else if (living > 0)
		{
			temple.LastActiveYear = year;
		}
		_readViewDirty = true;
		XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
	}

	private static void TryMaintainTempleMembership(XjAncientShiTempleRecord temple, City city, int year)
	{
		if (temple == null || city?.units == null || city.units.Count == 0
			|| temple.LivingMemberCount >= DesiredMemberCount
			|| temple.LastRecruitmentProbeYear > 0
				&& year - temple.LastRecruitmentProbeYear < RecruitmentProbeIntervalYears)
		{
			return;
		}

		// 缺员越多也只在本次探测中显现一人。这样寺庙可缓慢恢复到五人，
		// 但不会在一个年度把一座城改造成古释人口池。
		temple.LastRecruitmentProbeYear = year;
		int count = city.units.Count;
		int start = XjDeterministicHash.PositiveIndex(
			temple.TempleId * 37L + year * 11L, "ancient_temple_scripture_candidate_v1", count);
		int budget = Math.Min(LocalRecruitmentCandidateBudget, count);
		for (int offset = 0; offset < budget; offset++)
		{
			Actor candidate = city.units[(start + offset) % count];
			if (!IsLocalScriptureCandidate(candidate)) continue;
			if (!XjShiState.TryEnter(candidate, XjShiTraditionIds.Ancient, year,
				XjShiSourceIds.Scripture, 0L, XjShiLineageIds.NorthWorldHonored, string.Empty)) continue;
			AddMember(temple, candidate, year);
			RefreshTemple(temple, year);
			break;
		}
		XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Modules);
	}

	private static bool IsLocalScriptureCandidate(Actor candidate)
	{
		if (candidate?.data == null || !candidate.isAlive()
			|| Math.Max(0f, candidate.getAge()) < 16f
			|| XjCultivationPathRules.TryGetPath(candidate, out _)
			|| !XjShiEntrySystem.CanAddLivingAncientShi(candidate)) return false;
		XjCultivationSeed.EnsureSeedState(candidate);
		XjMingShuState.Normalize(candidate);
		return XjActorAccessor.TryGetFloat(candidate, XjActorDataKeys.MingShu, out float mingShu)
			&& mingShu >= XjShiCatalog.AncientSeedMingShuThreshold;
	}

	private static string GenerateTempleName(long founderId, long cityId, string vowId)
	{
		string[] secondary = { "慈云", "明照", "寂照", "法雨", "净明", "慧灯", "圆照", "莲台" };
		string title = XjAncientShiVowCatalog.GetTempleTitle(vowId);
		if (XjDeterministicHash.PositiveIndex(founderId + cityId, "ancient_temple_title_mix", 100) < 45)
			title = secondary[XjDeterministicHash.PositiveIndex(founderId * 31L + cityId, "ancient_temple_title", secondary.Length)];
		string suffix = XjDeterministicHash.PositiveIndex(founderId + cityId * 13L, "ancient_temple_suffix", 2) == 0 ? "寺" : "庙";
		return title + suffix;
	}

	private static XjAncientShiTempleRecord Clone(XjAncientShiTempleRecord source)
	{
		if (source == null) return null;
		return new XjAncientShiTempleRecord
		{
			TempleId = source.TempleId,
			Name = source.Name ?? string.Empty,
			CityId = source.CityId,
			CityName = source.CityName ?? string.Empty,
			FoundedYear = source.FoundedYear,
			FounderActorId = source.FounderActorId,
			FounderName = source.FounderName ?? string.Empty,
			AbbotActorId = source.AbbotActorId,
			AbbotName = source.AbbotName ?? string.Empty,
			PrincipalVowId = source.PrincipalVowId ?? string.Empty,
			LivingMemberCount = source.LivingMemberCount,
			VowFoundation = source.VowFoundation,
			DharmaArchive = source.DharmaArchive,
			ResponseLegacy = source.ResponseLegacy,
			LastActiveYear = source.LastActiveYear,
			LastRefreshYear = source.LastRefreshYear,
			LastRecruitmentProbeYear = source.LastRecruitmentProbeYear,
			LegacyJinDiDomainIds = source.LegacyJinDiDomainIds == null ? new List<string>() : new List<string>(source.LegacyJinDiDomainIds),
			MemberActorIds = source.MemberActorIds == null ? new List<long>() : new List<long>(source.MemberActorIds)
		};
	}
}
