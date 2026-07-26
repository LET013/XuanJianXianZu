using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Codex;

/// <summary>
/// Publishes immutable, throttled read snapshots for the Xuanjian codex.
/// UI reads the last completed snapshot; opening or filtering the window never
/// scans actors. Any defensive archive repair done while building a snapshot is
/// gated through XjDetectionGate and may run at most once per world year.
/// </summary>
internal static partial class XjCodexSnapshotPublisher
{
	// A codex snapshot joins several bounded archives and sect projections. Keep
	// automatic refresh deliberately coarse; player-requested refreshes bypass it.
	private const double MinimumRealtimeIntervalMilliseconds = 10000d;
	private const int MaxSnapshotSectResourceItems = 4000;
	private const int MaxSnapshotCraftActors = 420;
	private const int MaxSnapshotJinDan = 360;
	private const int MaxSnapshotLectures = 160;
	private const int MaxSnapshotHistory = 8000;
	private const int MaxSnapshotCenturyAnnals = 50;
	private static XjCodexSnapshot _current = XjCodexSnapshot.Empty;
	private static XjCodexDirtyFlags _dirty = XjCodexDirtyFlags.All;
	private static int _lastPublishedYear = -1;
	private static int _requestedYear;
	private static int _version;
	private static bool _manualRefresh;
	private static bool _windowOpen;
	private static double _lastBuildMilliseconds;
	private static long _lastPublishedTimestamp;

	internal static bool HasPending
	{
		get
		{
			if (_dirty == XjCodexDirtyFlags.None) return false;
			// 主动刷新是明确的玩家操作；高压态只暂停自动全量快照，避免周期性卡顿。
			if (_manualRefresh) return true;
			if (!_windowOpen || (_lastPublishedYear >= 0 && !IsRealtimeThrottleReady())) return false;
			return XjRuntimeWorkBudget.StressTier < XjRuntimeStressTier.Severe;
		}
	}

	internal static XjCodexSnapshot Read() => _current ?? XjCodexSnapshot.Empty;
	internal static string DirtySummary => FormatDirtySummary(_dirty);
	internal static int LastPublishedYear => _lastPublishedYear;
	internal static int RequestedYear => _requestedYear;
	internal static int PublishedVersion => _version;
	internal static double LastBuildMilliseconds => _lastBuildMilliseconds;
	internal static bool HasManualRefreshPending => _manualRefresh && _dirty != XjCodexDirtyFlags.None;

	private static string FormatDirtySummary(XjCodexDirtyFlags flags)
	{
		if (flags == XjCodexDirtyFlags.None) return "无";
		if ((flags & XjCodexDirtyFlags.All) == XjCodexDirtyFlags.All) return "全部";

		List<string> parts = new List<string>(8);
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.World, "天下");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.Sect, "宗门");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.Family, "家族");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.City, "城镇");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.Formation, "大阵");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.SecretRealm, "福地洞天");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.Craft, "四艺");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.JinDan, "真君");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.AdventureRealm, "奇遇洞天");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.Conflict, "冲突");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.History, "历史");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.SectResource, "宗门资源");
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.CenturyAnnals, "百年世谱");
		return parts.Count == 0 ? "零散变动" : string.Join("、", parts);
	}

	private static void AddDirtyLabel(List<string> parts, XjCodexDirtyFlags source, XjCodexDirtyFlags flag, string label)
	{
		if ((source & flag) == flag) parts.Add(label);
	}

	internal static void MarkDirty(XjCodexDirtyFlags flags = XjCodexDirtyFlags.All)
	{
		_dirty |= flags;
		int year = 0;
		try { year = Math.Max(0, World.world?.map_stats?.year ?? 0); } catch { }
		_requestedYear = Math.Max(_requestedYear, year);
	}

	internal static void ScheduleYear(int worldYear)
	{
		int normalizedYear = Math.Max(0, worldYear);
		_requestedYear = Math.Max(_requestedYear, normalizedYear);
		if (_lastPublishedYear < 0) _dirty |= XjCodexDirtyFlags.All;
		else if (normalizedYear > _lastPublishedYear) _dirty |= XjCodexDirtyFlags.World;
	}

	internal static void RequestRefresh()
	{
		_manualRefresh = true;
		XjWorldLookupIndex.Invalidate();
		MarkDirty(XjCodexDirtyFlags.All);
	}

	internal static bool RefreshNow()
	{
		RequestRefresh();
		if (World.world == null) return false;
		int before = _version;
		Tick();
		return _version != before && _dirty == XjCodexDirtyFlags.None;
	}

	internal static void SetWindowOpen(bool open)
	{
		if (_windowOpen == open) return;
		_windowOpen = open;
		if (open)
		{
			RequestRefresh();
		}
		else
		{
			ReleaseSnapshotIfClosed();
		}
	}

	internal static void ReleaseSnapshotIfClosed()
	{
		if (_windowOpen) return;
		_current = XjCodexSnapshot.Empty;
		XjPersonalBiographyStore.ReleaseSnapshotCache();
		XjFamilyChronicleBookStore.ReleaseSnapshotCache();
		XjSectChronicleStore.ReleaseSnapshotCache();
		XjActorRegistry.ReleaseSnapshotCache();
		XjThreeBookNavigationTarget.Clear();
	}

	internal static void Tick()
	{
		if (!HasPending || World.world == null) return;
		if (_manualRefresh) XjWorldHistoryStore.PruneSuppressedRecords();
		int year = Math.Max(0, World.world.map_stats?.year ?? _requestedYear);
		long started = Stopwatch.GetTimestamp();
		_current = BuildSnapshot(year);
		XjPersonalBiographyStore.ReleaseSnapshotCache();
		XjFamilyChronicleBookStore.ReleaseSnapshotCache();
		XjSectChronicleStore.ReleaseSnapshotCache();
		XjActorRegistry.ReleaseSnapshotCache();
		_lastBuildMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
		_lastPublishedTimestamp = Stopwatch.GetTimestamp();
		_lastPublishedYear = year;
		_requestedYear = year;
		_dirty = XjCodexDirtyFlags.None;
		_manualRefresh = false;
	}

	internal static void Clear()
	{
		_current = XjCodexSnapshot.Empty;
		_dirty = XjCodexDirtyFlags.All;
		_lastPublishedYear = -1;
		_requestedYear = 0;
		_version = 0;
		_manualRefresh = false;
		_windowOpen = false;
		_lastBuildMilliseconds = 0d;
		_lastPublishedTimestamp = 0L;
		XjThreeBookNavigationTarget.Clear();
	}

	private static bool IsRealtimeThrottleReady()
	{
		if (_lastPublishedTimestamp <= 0L) return true;
		double elapsedMilliseconds = (Stopwatch.GetTimestamp() - _lastPublishedTimestamp) * 1000d / Stopwatch.Frequency;
		return elapsedMilliseconds >= ResolveRealtimeIntervalMilliseconds();
	}

	private static double ResolveRealtimeIntervalMilliseconds()
	{
		return XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 15000d,
			XjRuntimeStressTier.Severe => 10000d,
			XjRuntimeStressTier.Mild => 12500d,
			_ => MinimumRealtimeIntervalMilliseconds
		};
	}

	private static void TrimList<T>(List<T> items, int maxCount)
	{
		if (items == null || maxCount <= 0 || items.Count <= maxCount)
		{
			return;
		}

		items.RemoveRange(maxCount, items.Count - maxCount);
	}

	private static XjCodexSnapshot BuildSnapshot(int worldYear)
	{
		// 快照发布器只读取领域状态，不在 UI/Codex 刷新时修复宗主或峰主。
		IReadOnlyList<XjSectArchiveRecord> sectRecords = XjSectRepository.ReadAllSects();
		IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance = XjSectRepository.ReadAllGovernance();
		IReadOnlyList<XjSectFormationArchiveRecord> formationRecords = XjSectFormationRegistry.ReadAll();
		IReadOnlyList<XjSectFamilySeatArchiveRecord> familySeatRecords = XjSectRepository.ReadAllFamilySeats();
		IReadOnlyList<XjCraftTaskArchiveRecord> craftTaskRecords = XjCraftDomainRegistry.ReadTasks();
		IReadOnlyList<XjTalismanBatchArchiveRecord> talismanBatchRecords = XjCraftDomainRegistry.ReadTalismanBatches();
		IReadOnlyList<XjJinDanImmortalityArchiveRecord> jinDanRecords = XjJinDanImmortalityRegistry.ReadAll();
		IReadOnlyList<XjAdventureRealmClaimArchiveRecord> claims = XjAdventureRealmClaimSystem.ReadAll();
		IReadOnlyList<XjDongTianRecord> adventureRealms = XjDongTianRegistry.ReadAllEntries();
		IReadOnlyList<XjSecretRealmArchiveRecord> secretRealmRecords = XjSecretRealmRegistry.ReadAll();
		IReadOnlyList<XjYinSiMissionArchiveRecord> yinSiMissions = XjYinSiExposurePursuitSystem.ReadAll();
		IReadOnlyList<XjSectLectureArchiveRecord> lectureRecords = XjSectLectureSystem.ReadAll();

		Dictionary<long, string> sectNames = new Dictionary<long, string>();
		for (int i = 0; i < sectRecords.Count; i++)
		{
			XjSectArchiveRecord sect = sectRecords[i];
			if (sect == null || sect.SectId <= 0L) continue;
			sectNames[sect.SectId] = sect.Name ?? string.Empty;
		}

		Dictionary<long, XjCityFamilyGovernanceArchiveRecord> governanceByCity = new Dictionary<long, XjCityFamilyGovernanceArchiveRecord>();
		for (int i = 0; i < governance.Count; i++) governanceByCity[governance[i].CityId] = governance[i];
		Dictionary<long, XjSectFormationArchiveRecord> formationBySect = new Dictionary<long, XjSectFormationArchiveRecord>();
		for (int i = 0; i < formationRecords.Count; i++) formationBySect[formationRecords[i].SectId] = formationRecords[i];

		BuildCityMaps(governanceByCity, formationBySect,
			out List<XjCodexCityItem> cityItems,
			out Dictionary<long, string> cityNames,
			out Dictionary<long, string> kingdomNames,
			out Dictionary<long, int> cityCountBySect,
			out int kingdomCount);
		BuildSectCultivatorCountsByActorSect(sectRecords,
			out Dictionary<long, int> cultivatorsBySect,
			out Dictionary<long, int> ziFuBySect,
			out Dictionary<long, int> jinDanBySect);

		IReadOnlyList<XjFamilyLedgerAggregate> familyAggregates = XjFamilyMemberLedger.ReadAggregateSnapshot();
		BuildFamilies(familyAggregates, governance, familySeatRecords, sectRecords, cityNames, sectNames, worldYear,
			out List<XjCodexFamilyItem> familyItems,
			out Dictionary<long, string> familyNames);
		for (int i = 0; i < cityItems.Count; i++)
		{
			XjCodexCityItem cityItem = cityItems[i];
			if (familyNames.TryGetValue(cityItem.GoverningFamilyId, out string governingFamilyName)) cityItem.GoverningFamilyName = governingFamilyName;
			if (familyNames.TryGetValue(cityItem.ChallengerFamilyId, out string challengerFamilyName)) cityItem.ChallengerFamilyName = challengerFamilyName;
		}

		Dictionary<long, XjYinSiMissionArchiveRecord> activeMissionByTarget = new Dictionary<long, XjYinSiMissionArchiveRecord>();
		for (int i = 0; i < yinSiMissions.Count; i++)
		{
			XjYinSiMissionArchiveRecord mission = yinSiMissions[i];
			if (mission == null || mission.TargetActorId <= 0L || string.Equals(mission.Stage, XjYinSiMissionStage.Resolved, StringComparison.Ordinal) || string.Equals(mission.Stage, XjYinSiMissionStage.Cancelled, StringComparison.Ordinal)
				|| string.Equals(mission.Stage, XjYinSiMissionStage.Evaded, StringComparison.Ordinal)) continue;
			activeMissionByTarget[mission.TargetActorId] = mission;
		}
		Dictionary<long, XjSecretRealmArchiveRecord> secretRealmBySittingActor = new Dictionary<long, XjSecretRealmArchiveRecord>();
		for (int i = 0; i < secretRealmRecords.Count; i++) if (secretRealmRecords[i]?.SittingJinDanActorId > 0L) secretRealmBySittingActor[secretRealmRecords[i].SittingJinDanActorId] = secretRealmRecords[i];

		List<XjCodexFormationItem> formationItems = BuildFormations(formationRecords, sectNames);
		Dictionary<long, IReadOnlyList<XjCodexSectPeakItem>> peaksBySect = BuildSectPeakRosters(sectRecords);
		Dictionary<long, string> peakRosterBySect = BuildSectPeakRosterSummaries(peaksBySect);
		List<XjCodexSectItem> sectItems = BuildSects(sectRecords, formationBySect, cityNames,
			cityCountBySect, cultivatorsBySect, ziFuBySect, jinDanBySect, peakRosterBySect, peaksBySect,
			familyItems, familySeatRecords, familyNames);
		List<XjCodexSectResourceItem> sectResourceItems = BuildSectResources(sectRecords, sectNames, familySeatRecords, talismanBatchRecords);
		ApplySectResourceSummary(sectItems, sectResourceItems);
		int sectResourceTotalCount = sectResourceItems.Count;
		TrimList(sectResourceItems, MaxSnapshotSectResourceItems);
		List<XjCodexCraftItem> craftItems = BuildCraftActors(craftTaskRecords);
		int craftActorTotalCount = craftItems.Count;
		TrimList(craftItems, MaxSnapshotCraftActors);
		List<XjCodexJinDanItem> jinDanItems = BuildJinDan(jinDanRecords, familyNames, activeMissionByTarget, secretRealmBySittingActor);
		int jinDanTotalCount = jinDanItems.Count;
		int knownByYinSi = 0;
		for (int i = 0; i < jinDanItems.Count; i++) if (jinDanItems[i].YinSiKnown) knownByYinSi++;
		TrimList(jinDanItems, MaxSnapshotJinDan);
		List<XjCodexAdventureRealmItem> adventureItems = BuildAdventureRealms(adventureRealms, claims, sectNames, familyNames, cityNames);
		List<XjCodexSecretRealmItem> secretRealmItems = BuildSecretRealms(secretRealmRecords, sectNames);
		List<XjCodexLectureItem> lectureItems = BuildLectures(lectureRecords, sectNames);
		int lectureTotalCount = lectureItems.Count;
		TrimList(lectureItems, MaxSnapshotLectures);
		List<XjCodexConflictItem> conflicts = BuildConflicts(sectItems, cityItems, formationItems, governance, familyItems, yinSiMissions, secretRealmItems, adventureItems);
		// The codex only renders the newest bounded history window. Avoid cloning
		// the entire world-history archive before trimming it again.
		List<XjCodexHistoryItem> history = BuildHistory(
			XjWorldHistoryStore.ReadSnapshot(MaxSnapshotHistory), cityNames, sectNames, familyNames,
			out List<XjCodexHistorySubjectItem> historyActors,
			out List<XjCodexHistorySubjectItem> historyFamilies,
			out List<XjCodexHistorySubjectItem> historySects);
		List<XjCodexHistoryItem> personalBiographies = BuildThreeBookHistory(
			XjPersonalBiographyStore.ReadSnapshot(), 0, cityNames, sectNames, familyNames,
			out List<XjCodexHistorySubjectItem> biographyActors);
		List<XjCodexHistoryItem> familyChronicles = BuildThreeBookHistory(
			XjFamilyChronicleBookStore.ReadSnapshot(), 1, cityNames, sectNames, familyNames,
			out List<XjCodexHistorySubjectItem> chronicleFamilies);
		List<XjCodexHistoryItem> sectChronicles = BuildThreeBookHistory(
			XjSectChronicleStore.ReadSnapshot(), 2, cityNames, sectNames, familyNames,
			out List<XjCodexHistorySubjectItem> chronicleSects);
		List<XjCodexCenturyAnnalsItem> centuryAnnals = BuildCenturyAnnals(XjCenturyAnnalsStore.ReadSnapshot(MaxSnapshotCenturyAnnals));
		XjCodexThreeBookHealth threeBookHealth = XjThreeBookDiagnostics.BuildHealth();

		int openCraftTaskCount = 0;
		for (int i = 0; i < craftTaskRecords.Count; i++) if (craftTaskRecords[i]?.IsOpen == true) openCraftTaskCount++;
		int talismanStockCount = 0; // 四艺总览不再汇总符箓库存，避免每次快照遍历全部批次。
		int operationalFormationCount = 0;
		int brokenFormationCount = 0;
		for (int i = 0; i < formationItems.Count; i++)
		{
			if (formationItems[i].CurrentDurability > 0 && formationItems[i].Grade > 0) operationalFormationCount++;
			if (string.Equals(formationItems[i].BuildState, XjSectFormationBuildState.Broken, StringComparison.Ordinal)) brokenFormationCount++;
		}
		int fudiCount = 0;
		int dongtianCount = 0;
		for (int i = 0; i < secretRealmItems.Count; i++)
		{
			if (string.Equals(secretRealmItems[i].Stage, XjSecretRealmStage.Fudi, StringComparison.Ordinal)) fudiCount++;
			if (string.Equals(secretRealmItems[i].Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal) || string.Equals(secretRealmItems[i].Stage, XjSecretRealmStage.Dormant, StringComparison.Ordinal)) dongtianCount++;
		}
		int openAdventure = 0;
		int contestedAdventure = 0;
		for (int i = 0; i < adventureItems.Count; i++)
		{
			if (adventureItems[i].IsOpen) openAdventure++;
			if (string.Equals(adventureItems[i].State, XjAdventureRealmClaimState.Contested, StringComparison.Ordinal)) contestedAdventure++;
		}

		return new XjCodexSnapshot
		{
			Version = ++_version,
			WorldYear = worldYear,
			PublishedAt = "玄鉴纪元 " + worldYear.ToString(CultureInfo.InvariantCulture) + " 年",
			KingdomCount = kingdomCount,
			CityCount = cityItems.Count,
			CultivatorCount = XjCultivatorCache.Count,
			SectCount = sectItems.Count,
			FamilyCount = familyItems.Count,
			JinDanCount = jinDanTotalCount,
			KnownByYinSiCount = knownByYinSi,
			CraftActorCount = craftActorTotalCount,
			OpenCraftTaskCount = openCraftTaskCount,
			TalismanStockCount = talismanStockCount,
			OperationalFormationCount = operationalFormationCount,
			BrokenFormationCount = brokenFormationCount,
			OpenAdventureRealmCount = openAdventure,
			ContestedAdventureRealmCount = contestedAdventure,
			FudiCount = fudiCount,
			DongtianCount = dongtianCount,
			ActiveYinSiMissionCount = activeMissionByTarget.Count,
			LectureCount = lectureTotalCount,
			HistoryCapacity = XjWorldHistoryStore.Capacity,
			PersonalBiographyCapacity = XjPersonalBiographyStore.Capacity,
			FamilyChronicleCapacity = XjFamilyChronicleBookStore.Capacity,
			SectChronicleCapacity = XjSectChronicleStore.Capacity,
			CenturyAnnalsCapacity = XjCenturyAnnalsStore.Capacity,
			CenturyAnnalsStartYear = ResolveCenturyAnnalsStartYear(centuryAnnals),
			SectResourceCount = sectResourceTotalCount,
			Sects = sectItems,
			SectResources = sectResourceItems,
			Families = familyItems,
			Cities = cityItems,
			Formations = formationItems,
			SecretRealms = secretRealmItems,
			CraftActors = craftItems,
			JinDan = jinDanItems,
			AdventureRealms = adventureItems,
			Lectures = lectureItems,
			Conflicts = conflicts,
			History = history,
			HistoryActors = historyActors,
			HistoryFamilies = historyFamilies,
			HistorySects = historySects,
			PersonalBiographies = personalBiographies,
			BiographyActors = biographyActors,
			FamilyChronicles = familyChronicles,
			ChronicleFamilies = chronicleFamilies,
			SectChronicles = sectChronicles,
			ChronicleSects = chronicleSects,
			ThreeBookHealth = threeBookHealth,
			CenturyAnnals = centuryAnnals
		};
	}

	private static int ResolveCenturyAnnalsStartYear(IReadOnlyList<XjCodexCenturyAnnalsItem> centuryAnnals)
	{
		int startYear = Math.Max(0, XjCenturyAnnalsStore.BaseWorldYear);
		if (startYear > 0)
		{
			return startYear;
		}

		if (centuryAnnals == null)
		{
			return 0;
		}

		for (int i = 0; i < centuryAnnals.Count; i++)
		{
			XjCodexCenturyAnnalsItem item = centuryAnnals[i];
			if (item != null && item.StartYear > 0)
			{
				startYear = startYear <= 0 ? item.StartYear : Math.Min(startYear, item.StartYear);
			}
		}
		return startYear;
	}

	private static void BuildCityMaps(
		IReadOnlyDictionary<long, XjCityFamilyGovernanceArchiveRecord> governanceByCity,
		IReadOnlyDictionary<long, XjSectFormationArchiveRecord> formationBySect,
		out List<XjCodexCityItem> cityItems,
		out Dictionary<long, string> cityNames,
		out Dictionary<long, string> kingdomNames,
		out Dictionary<long, int> cityCountBySect,
		out int kingdomCount)
	{
		cityItems = new List<XjCodexCityItem>();
		cityNames = new Dictionary<long, string>();
		kingdomNames = new Dictionary<long, string>();
		cityCountBySect = new Dictionary<long, int>();
		HashSet<long> kingdomIds = new HashSet<long>();
		// The governance lane and the codex are both annual readers. Reuse the
		// immutable world snapshot instead of independently enumerating the native
		// city collection for every published codex snapshot.
		IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
		for (int index = 0; index < cities.Count; index++)
		{
			City city = cities[index];
			if (city?.data == null) continue;
				long cityId = city.data.id;
				string cityName = SafeDataName(city.data, "无名城");
				cityNames[cityId] = cityName;
				long kingdomId = city.kingdom?.data?.id ?? 0L;
				string kingdomName = city.kingdom?.name ?? "无国属";
				if (kingdomId > 0L)
				{
					kingdomIds.Add(kingdomId);
					kingdomNames[kingdomId] = kingdomName;
				}
				XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect);
				if (sect != null) AddCount(cityCountBySect, sect.SectId, 1);
				governanceByCity.TryGetValue(cityId, out XjCityFamilyGovernanceArchiveRecord governance);
				int cultivatorCount = 0;
				if (XjZongMenCultivatorCityIndex.TryGetAggregate(city, out XjCityCultivatorAggregate cityAggregate))
				{
					cultivatorCount = cityAggregate.CultivatorCount;
				}
				cityItems.Add(new XjCodexCityItem
				{
					CityId = cityId,
					Name = cityName,
					KingdomId = kingdomId,
					KingdomName = kingdomName,
					SectId = sect?.SectId ?? 0L,
					SectName = sect?.Name ?? string.Empty,
					GoverningFamilyId = governance?.GoverningFamilyId ?? 0L,
					GovernanceState = governance?.State ?? "未确认",
					ChallengerFamilyId = governance?.ChallengerFamilyId ?? 0L,
					ChallengeConsecutiveYears = governance?.ChallengeConsecutiveYears ?? 0,
					IncumbentControlScore = governance?.IncumbentControlScore ?? 0f,
					ChallengerControlScore = governance?.ChallengerControlScore ?? 0f,
					CultivatorCount = cultivatorCount,
					FormationProtected = sect != null && XjSectFormationRegistry.TryGetOperational(sect.SectId, out _)
				});
				if (sect != null && formationBySect.TryGetValue(sect.SectId, out XjSectFormationArchiveRecord formation) && formation != null && formation.Grade > 0)
				{
					XjCodexCityItem item = cityItems[cityItems.Count - 1];
					item.FormationName = XjFormationEngineeringSystem.FormatFormationName(formation.Grade);
					item.FormationGrade = formation.Grade;
					item.FormationCurrentDurability = formation.CurrentDurability;
					item.FormationMaxDurability = formation.MaxDurability;
					item.FormationState = formation.BuildState ?? string.Empty;
					item.FormationLeadActorId = formation.LeadFormationMasterId;
					item.FormationLeadName = ResolveActorName(formation.LeadFormationMasterId);
				}
		}
		cityItems.Sort((left, right) =>
		{
			int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
			return name != 0 ? name : left.CityId.CompareTo(right.CityId);
		});
		kingdomCount = kingdomIds.Count;
	}

	private static void BuildSectCultivatorCountsByActorSect(
		IReadOnlyList<XjSectArchiveRecord> sectRecords,
		out Dictionary<long, int> cultivatorsBySect,
		out Dictionary<long, int> ziFuBySect,
		out Dictionary<long, int> jinDanBySect)
	{
		cultivatorsBySect = new Dictionary<long, int>();
		ziFuBySect = new Dictionary<long, int>();
		jinDanBySect = new Dictionary<long, int>();
		if (sectRecords == null) return;
		for (int i = 0; i < sectRecords.Count; i++)
		{
			XjSectArchiveRecord record = sectRecords[i];
			if (record == null || record.SectId <= 0L) continue;
			IReadOnlyList<long> actorIds = XjZongMenCultivatorCityIndex.GetActorIdsForSect(record.SectId);
			for (int j = 0; j < actorIds.Count; j++)
			{
				long actorId = actorIds[j];
				if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
				if (ReadActorSectId(actor) != record.SectId) continue;
				AddCount(cultivatorsBySect, record.SectId, 1);
				string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
				int realmOrder = XjRealmHelper.GetOrder(realmId);
				if (realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.JinDan)) AddCount(jinDanBySect, record.SectId, 1);
				else if (realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) AddCount(ziFuBySect, record.SectId, 1);
			}
		}
	}

																																private static readonly string[] SnapshotSectPeakNames =
	{
		"朝真峰", "栖霞峰", "鸣玉峰", "观澜峰", "承露峰", "玄霜峰", "照影峰",
		"藏锋峰", "青岚峰", "云台峰", "问道峰", "含章峰", "镇岳峰"
	};

								private static string EmptyConflict(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
	private static string EmptyText(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
	private static string ResolveCaiQiDisplayName(string resourceId, string resourceName)
	{
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string displayName)
			&& !string.IsNullOrWhiteSpace(displayName))
		{
			return displayName.Trim();
		}

		if (string.Equals(resourceId, "zaqi", StringComparison.Ordinal))
		{
			return "杂气";
		}

		return EmptyText(resourceName, "未名先天之气");
	}

	private static string ResolvePillRealmDisplay(string pillId)
	{
		return XjAlchemyCatalog.TryGetPill(pillId, out XjAlchemyPillDef definition)
			? ResolveRealmDisplay(definition.MinApplicableRealmId)
			: "未载";
	}

	private static string ResolveTalismanRealmDisplay(XjTalismanDefinition definition)
	{
		return definition == null ? "未载" : ResolveRealmDisplay(definition.MinRealmId);
	}

	private static string ResolveRealmDisplay(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return "金丹级";
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return "紫府级";
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return "筑基级";
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return "炼气级";
		return XjRealmHelper.GetDisplayName(realmId);
	}


	private static string EmptyResourceSource(string sourceType, string fallback)
	{
		if (string.Equals(sourceType, XjZongMenGongFaPavilion.SourceTypeGongFa, StringComparison.Ordinal)) return EmptyText(fallback, "功法入阁");
		if (string.Equals(sourceType, XjZongMenGongFaPavilion.SourceTypeQiuJinFa, StringComparison.Ordinal)) return EmptyText(fallback, "求金法入阁");
		return EmptyText(sourceType, fallback);
	}

	private static void AddCount(Dictionary<long, int> map, long key, int count)
	{
		if (key <= 0L || count == 0) return;
		map.TryGetValue(key, out int current);
		map[key] = current + count;
	}

	private static string ResolveProfessionName(string traitId)
	{
		if (string.Equals(traitId, XjCraftTraitRules.AlchemyTraitId, StringComparison.Ordinal)) return "炼丹师";
		if (string.Equals(traitId, XjCraftTraitRules.ArtifactRefiningTraitId, StringComparison.Ordinal)) return "炼器师";
		if (string.Equals(traitId, XjCraftTraitRules.TalismanTraitId, StringComparison.Ordinal)) return "符箓师";
		if (string.Equals(traitId, XjCraftTraitRules.FormationTraitId, StringComparison.Ordinal)) return "阵法师";
		return "未知百艺";
	}

	private static string ResolveActorNames(IReadOnlyList<long> actorIds)
	{
		if (actorIds == null || actorIds.Count == 0) return string.Empty;
		List<string> names = new List<string>();
		for (int i = 0; i < actorIds.Count; i++) if (XjScheduler.ResolveActor(actorIds[i], out Actor actor)) names.Add(SafeActorName(actor));
		return string.Join("、", names);
	}

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名修士" : name.Trim();
		}
		catch { return "未名修士"; }
	}

	private static string SafeDataName(BaseSystemData data, string fallback)
	{
		if (data == null) return fallback;
		try { return string.IsNullOrWhiteSpace(data.name) ? fallback : data.name.Trim(); }
		catch { return fallback; }
	}
}


