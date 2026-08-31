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
using XuanJianVNext.Data.WeaponArt;
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
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Codex;

/// <summary>
/// Publishes immutable, throttled read snapshots for the Xuanjian codex.
/// UI reads the last completed snapshot; opening or filtering the window never
/// scans actors. Any defensive archive repair done while building a snapshot is
/// gated through XjDetectionGate and may run at most once per world year.
/// </summary>
internal static partial class XjCodexSnapshotPublisher
{
	private static string SafeActorName(Actor actor) => XjStringHelper.ActorName(actor, "未名修士");

	// A codex snapshot joins several bounded archives and sect projections. Keep
	// automatic refresh deliberately coarse; player-requested refreshes bypass it.
	private const double MinimumRealtimeIntervalMilliseconds = 10000d;
	private const int MaxSnapshotSectResourceItems = 4000;
	private const int MaxSnapshotCraftActors = 420;
	private const int MaxSnapshotJinDan = 360;
	private const int MaxSnapshotLectures = 160;
	private const int MaxSnapshotHistory = 1600;
	// 三书页面只需要近期、可阅读的史料窗口；完整显示卷仍由三书自身按容量保留。
	// 降低一次发布时的 Clone/格式化量，避免打开百科或自动刷新时把数千条旧纪事
	// 同时搬到主线程和 UI 堆上。
	private const int MaxSnapshotPersonalBiography = 1200;
	private const int MaxSnapshotFamilyChronicle = 900;
	private const int MaxSnapshotSectChronicle = 900;
	private const int MaxSnapshotCenturyAnnals = 50;
	private static XjCodexSnapshot _current = XjCodexSnapshot.Empty;
	private static XjCodexDirtyFlags _dirty = XjCodexDirtyFlags.All;
	private static int _lastPublishedYear = -1;
	private static int _requestedYear;
	private static int _version;
	private static bool _manualRefresh;
	private static bool _windowOpen;
	private static int _activeTab;
	private static bool _fullSearchActive;
	private static double _lastBuildMilliseconds;
	private static long _lastPublishedTimestamp;

	internal static bool HasPending
	{
		get
		{
			if (_dirty == XjCodexDirtyFlags.None) return false;
			// 主动刷新是明确的玩家操作；高压态只暂停自动全量快照，避免周期性卡顿。
			if (_manualRefresh) return true;
			// 已有不可变快照时先立即展示旧卷。世界变动只标为“待重照”，
			// 不因开窗而在主线程同步联结全部卷宗。
			if ((_current?.Version ?? 0) > 0) return false;
			if (!_windowOpen || (_lastPublishedYear >= 0 && !IsRealtimeThrottleReady())) return false;
			return XjRuntimeWorkBudget.StressTier < XjRuntimeStressTier.Severe;
		}
	}

	internal static XjCodexSnapshot Read() => _current ?? XjCodexSnapshot.Empty;
	internal static int DesiredViewKey => BuildViewKey(_activeTab, _fullSearchActive);
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
		AddDirtyLabel(parts, flags, XjCodexDirtyFlags.SwordIntent, "剑意谱");
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
		try { year = Math.Max(0, World.world?.map_stats?.year ?? 0); } catch (System.Exception xjCaught119) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Codex/XjCodexSnapshotPublisher.cs:119", xjCaught119); }
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

	internal static void SetActiveView(int tabIndex, bool fullSearch)
	{
		int normalizedTab = Math.Max(0, tabIndex);
		if (_activeTab == normalizedTab && _fullSearchActive == fullSearch) return;
		bool previousViewHeldSwordIntents = _fullSearchActive || _activeTab == 13;
		_activeTab = normalizedTab;
		_fullSearchActive = fullSearch;
		// 切页时先释放旧页重载荷，避免旧页与新页在构建阶段同时常驻。
		_current = XjCodexSnapshot.Empty;
		XjWorldEntityViewModelStore.Clear();
		if (previousViewHeldSwordIntents) XjSwordIntentRegistry.ReleaseSnapshotCache();
		_manualRefresh = true;
		MarkDirty(XjCodexDirtyFlags.All);
		// 仙鉴打开时世界处于暂停，不能等待模拟帧调度器推进；切页后立即照录当前卷。
		if (_windowOpen && World.world != null) Tick();
	}

	private static int BuildViewKey(int tabIndex, bool fullSearch)
	{
		return (Math.Max(0, tabIndex) + 1) * 2 + (fullSearch ? 1 : 0);
	}

	internal static void SetWindowOpen(bool open)
	{
		if (_windowOpen == open) return;
		_windowOpen = open;
		if (open)
		{
			if ((_current?.Version ?? 0) <= 0)
			{
				RequestRefresh();
				if (World.world != null) Tick();
			}
		}
		else
		{
			ReleaseSnapshotIfClosed();
		}
	}

	internal static void ReleaseSnapshotIfClosed()
	{
		if (_windowOpen) return;
		// P0：仙鉴关闭即释放综合快照与实体索引。下一次开窗按当前页面重建，
		// 不再用数千条史册、成员与资源记录换取“零等待开窗”。
		_current = XjCodexSnapshot.Empty;
		_dirty |= XjCodexDirtyFlags.All;
		_lastPublishedYear = -1;
		_manualRefresh = false;
		XjWorldEntityViewModelStore.Clear();
		XjPersonalBiographyStore.ReleaseSnapshotCache();
		XjFamilyChronicleBookStore.ReleaseSnapshotCache();
		XjSectChronicleStore.ReleaseSnapshotCache();
		XjSwordIntentRegistry.ReleaseSnapshotCache();
		XjThreeBookNavigationTarget.Clear();
		// 纯规则指南不含世界实况，关窗无需反复销毁；仅世界级 Clear 时重置。
	}

	internal static void Tick()
	{
		if (!HasPending || World.world == null) return;
		if (_manualRefresh) XjWorldHistoryStore.PruneSuppressedRecords();
		int year = Math.Max(0, World.world.map_stats?.year ?? _requestedYear);
		long started = Stopwatch.GetTimestamp();
		// 不保留旧综合快照直到新快照完成。刷新期间页面显示“照录中”，
		// 以换取显著更低的瞬时峰值，避免大档手动重照时旧新双份共存。
		_current = XjCodexSnapshot.Empty;
		XjWorldEntityViewModelStore.Clear();
		XjCodexSnapshot nextSnapshot = BuildSnapshot(year);
		_current = nextSnapshot ?? XjCodexSnapshot.Empty;
		XjWorldEntityViewModelStore.Publish(_current);
		XjPersonalBiographyStore.ReleaseSnapshotCache();
		XjFamilyChronicleBookStore.ReleaseSnapshotCache();
		XjSectChronicleStore.ReleaseSnapshotCache();
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
		_activeTab = 0;
		_fullSearchActive = false;
		_lastBuildMilliseconds = 0d;
		_lastPublishedTimestamp = 0L;
		XjThreeBookNavigationTarget.Clear();
		XjWorldEntityViewModelStore.Clear();
		XjMechanicsCodexReadModel.Clear();
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
		bool includeAllPagePayloads = _fullSearchActive || _activeTab >= 22;
		bool includeWorldOverview = includeAllPagePayloads || _activeTab == 0 || _activeTab == 16;
		bool includeFamilyMembers = includeAllPagePayloads || _activeTab == 3;
		bool includeSectPeakMembers = includeAllPagePayloads || _activeTab == 1;
		bool includeSectResources = includeAllPagePayloads || _activeTab == 1 || _activeTab == 2;
		bool includeCraftActors = includeAllPagePayloads || _activeTab == 7;
		bool includeJinDanActors = includeAllPagePayloads || _activeTab == 8;
		bool includeConflicts = includeAllPagePayloads || includeWorldOverview || _activeTab == 10;
		bool includeHistoryBooks = includeAllPagePayloads || _activeTab == 11;
		bool includeSwordIntents = includeAllPagePayloads || _activeTab == 13;
		bool includeClimate = includeAllPagePayloads || includeWorldOverview || _activeTab == 14;
		bool includeCenturyAnnals = includeAllPagePayloads || includeClimate || _activeTab == 12;
		bool includeFormationItems = includeAllPagePayloads || includeWorldOverview
			|| _activeTab == 1 || _activeTab == 4 || _activeTab == 5 || _activeTab == 10 || _activeTab == 15;
		bool includeSecretRealmItems = includeAllPagePayloads || includeWorldOverview
			|| _activeTab == 1 || _activeTab == 6 || _activeTab == 8 || _activeTab == 10 || _activeTab == 15;
		bool includeAdventureItems = includeAllPagePayloads || includeWorldOverview
			|| _activeTab == 9 || _activeTab == 10 || _activeTab == 15;
		bool includeLectures = includeAllPagePayloads || _activeTab == 1;
		bool includeYinSiMissions = includeAllPagePayloads || includeWorldOverview
			|| _activeTab == 8 || _activeTab == 10;
		bool includeWorldEntities = includeAllPagePayloads || includeWorldOverview
			|| _activeTab == 1 || _activeTab == 2 || _activeTab == 3 || _activeTab == 4
			|| _activeTab == 5 || _activeTab == 6 || _activeTab == 9 || _activeTab == 10 || _activeTab == 15;

		// 快照发布器只读取领域状态，不在 UI/Codex 刷新时修复宗主或峰主。
		IReadOnlyList<XjSectArchiveRecord> sectRecords = XjSectRepository.ReadAllSects();
		IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance = XjSectRepository.ReadAllGovernance();
		IReadOnlyList<XjSectFormationArchiveRecord> formationRecords = includeFormationItems
			? XjSectFormationRegistry.ReadAll()
			: Array.Empty<XjSectFormationArchiveRecord>();
		IReadOnlyList<XjSectFamilySeatArchiveRecord> familySeatRecords = XjSectRepository.ReadAllFamilySeats();
		IReadOnlyList<XjCraftTaskArchiveRecord> craftTaskRecords = includeCraftActors
			? XjCraftDomainRegistry.ReadTasks()
			: Array.Empty<XjCraftTaskArchiveRecord>();
		IReadOnlyList<XjTalismanBatchArchiveRecord> talismanBatchRecords = includeSectResources
			? XjCraftDomainRegistry.ReadTalismanBatches()
			: Array.Empty<XjTalismanBatchArchiveRecord>();
		IReadOnlyList<XjJinDanImmortalityArchiveRecord> jinDanRecords = includeJinDanActors
			? XjJinDanImmortalityRegistry.ReadAll()
			: Array.Empty<XjJinDanImmortalityArchiveRecord>();
		IReadOnlyList<XjAdventureRealmClaimArchiveRecord> claims = includeAdventureItems
			? XjAdventureRealmClaimSystem.ReadAll()
			: Array.Empty<XjAdventureRealmClaimArchiveRecord>();
		IReadOnlyList<XjDongTianRecord> adventureRealms = includeAdventureItems
			? XjDongTianRegistry.ReadAllEntries()
			: Array.Empty<XjDongTianRecord>();
		IReadOnlyList<XjSecretRealmArchiveRecord> secretRealmRecords = includeSecretRealmItems
			? XjSecretRealmRegistry.ReadAll()
			: Array.Empty<XjSecretRealmArchiveRecord>();
		IReadOnlyList<XjYinSiMissionArchiveRecord> yinSiMissions = includeYinSiMissions
			? XjYinSiExposurePursuitSystem.ReadAll()
			: Array.Empty<XjYinSiMissionArchiveRecord>();
		IReadOnlyList<XjSectLectureArchiveRecord> lectureRecords = includeLectures
			? XjSectLectureSystem.ReadAll()
			: Array.Empty<XjSectLectureArchiveRecord>();

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

		IReadOnlyList<XjFamilyLedgerAggregate> familyAggregates = XjFamilyMemberLedger.ReadAggregateSnapshot();
		List<XjCodexCityItem> cityItems;
		Dictionary<long, string> cityNames;
		Dictionary<long, string> kingdomNames;
		Dictionary<long, int> cityCountBySect;
		int kingdomCount;
		Dictionary<long, int> cultivatorsBySect;
		Dictionary<long, int> ziFuBySect;
		Dictionary<long, int> jinDanBySect;
		Dictionary<long, int> ziFuPathBySect;
		Dictionary<long, int> fuQiZhenRenBySect;
		Dictionary<long, int> ziJinJinDanBySect;
		Dictionary<long, int> zhenJunYuShiBySect;
		Dictionary<long, XjDaoTuTraditionSummary> daoTuTraditionBySect;
		List<XjCodexFamilyItem> familyItems;
		Dictionary<long, string> familyNames;

		if (includeWorldEntities)
		{
			BuildCityMaps(governanceByCity, formationBySect,
				out cityItems, out cityNames, out kingdomNames, out cityCountBySect, out kingdomCount);
			BuildSectCultivatorCountsByActorSect(sectRecords,
				out cultivatorsBySect, out ziFuBySect, out jinDanBySect,
				out ziFuPathBySect, out fuQiZhenRenBySect,
				out ziJinJinDanBySect, out zhenJunYuShiBySect,
				out daoTuTraditionBySect);
			BuildFamilies(familyAggregates, governance, familySeatRecords, sectRecords, cityNames, sectNames,
				worldYear, includeFamilyMembers, out familyItems, out familyNames);
			for (int i = 0; i < cityItems.Count; i++)
			{
				XjCodexCityItem cityItem = cityItems[i];
				if (familyNames.TryGetValue(cityItem.GoverningFamilyId, out string governingFamilyName)) cityItem.GoverningFamilyName = governingFamilyName;
				if (familyNames.TryGetValue(cityItem.ChallengerFamilyId, out string challengerFamilyName)) cityItem.ChallengerFamilyName = challengerFamilyName;
			}
		}
		else
		{
			BuildLightweightCityNames(out cityNames, out kingdomNames, out kingdomCount);
			cityItems = new List<XjCodexCityItem>();
			cityCountBySect = new Dictionary<long, int>();
			cultivatorsBySect = new Dictionary<long, int>();
			ziFuBySect = new Dictionary<long, int>();
			jinDanBySect = new Dictionary<long, int>();
			ziFuPathBySect = new Dictionary<long, int>();
			fuQiZhenRenBySect = new Dictionary<long, int>();
			ziJinJinDanBySect = new Dictionary<long, int>();
			zhenJunYuShiBySect = new Dictionary<long, int>();
			daoTuTraditionBySect = new Dictionary<long, XjDaoTuTraditionSummary>();
			familyItems = new List<XjCodexFamilyItem>();
			familyNames = BuildLightweightFamilyNames(familyAggregates, governance, familySeatRecords);
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

		List<XjCodexFormationItem> formationItems = includeFormationItems
			? BuildFormations(formationRecords, sectNames)
			: new List<XjCodexFormationItem>();
		Dictionary<long, IReadOnlyList<XjCodexSectPeakItem>> peaksBySect = includeSectPeakMembers
			? BuildSectPeakRosters(sectRecords)
			: new Dictionary<long, IReadOnlyList<XjCodexSectPeakItem>>();
		Dictionary<long, string> peakRosterBySect = includeSectPeakMembers
			? BuildSectPeakRosterSummaries(peaksBySect)
			: new Dictionary<long, string>();
		List<XjCodexSectItem> sectItems = includeWorldEntities
			? BuildSects(sectRecords, formationBySect, cityNames,
				cityCountBySect, cultivatorsBySect, ziFuBySect, jinDanBySect,
				ziFuPathBySect, fuQiZhenRenBySect, ziJinJinDanBySect, zhenJunYuShiBySect,
				daoTuTraditionBySect, peakRosterBySect, peaksBySect, familyItems, familySeatRecords, familyNames)
			: new List<XjCodexSectItem>();
		if (includeWorldEntities)
		{
			Dictionary<long, long> capitalSectByCity = new Dictionary<long, long>();
			for (int i = 0; i < sectItems.Count; i++)
			{
				XjCodexSectItem sectItem = sectItems[i];
				if (sectItem?.CapitalCityId > 0L) capitalSectByCity[sectItem.CapitalCityId] = sectItem.SectId;
			}
			for (int i = 0; i < cityItems.Count; i++)
			{
				XjCodexCityItem cityItem = cityItems[i];
				cityItem.IsSectCapital = cityItem != null
					&& capitalSectByCity.TryGetValue(cityItem.CityId, out long capitalSectId)
					&& capitalSectId == cityItem.SectId;
			}
		}
		List<XjCodexSectResourceItem> sectResourceItems = includeSectResources
			? BuildSectResources(sectRecords, sectNames, familySeatRecords, talismanBatchRecords)
			: new List<XjCodexSectResourceItem>();
		int sectResourceTotalCount = sectResourceItems.Count;
		if (includeSectResources)
		{
			ApplySectResourceSummary(sectItems, sectResourceItems);
			TrimList(sectResourceItems, MaxSnapshotSectResourceItems);
		}

		int craftActorTotalCount = XjCraftActorIndex.Count;
		List<XjCodexCraftItem> craftItems = includeCraftActors
			? BuildCraftActors(craftTaskRecords)
			: new List<XjCodexCraftItem>();
		List<XjCodexCraftProfessionSummaryItem> craftProfessionSummaries = includeCraftActors
			? BuildCraftProfessionSnapshotSummaries(craftItems)
			: new List<XjCodexCraftProfessionSummaryItem>();
		if (includeCraftActors) TrimCraftActorsFairly(craftItems, MaxSnapshotCraftActors);

		int jinDanTotalCount = XjJinDanImmortalityRegistry.Count;
		List<XjCodexJinDanItem> jinDanItems = includeJinDanActors
			? BuildJinDan(jinDanRecords, familyNames, activeMissionByTarget, secretRealmBySittingActor, worldYear)
			: new List<XjCodexJinDanItem>();
		int knownByYinSi = 0;
		if (includeJinDanActors)
		{
			for (int i = 0; i < jinDanItems.Count; i++) if (jinDanItems[i].YinSiKnown) knownByYinSi++;
			TrimList(jinDanItems, MaxSnapshotJinDan);
		}
		List<XjCodexAdventureRealmItem> adventureItems = includeAdventureItems
			? BuildAdventureRealms(adventureRealms, claims, sectNames, familyNames, cityNames)
			: new List<XjCodexAdventureRealmItem>();
		List<XjCodexSecretRealmItem> secretRealmItems = includeSecretRealmItems
			? BuildSecretRealms(secretRealmRecords, sectNames)
			: new List<XjCodexSecretRealmItem>();
		List<XjCodexLectureItem> lectureItems = includeLectures
			? BuildLectures(lectureRecords, sectNames)
			: new List<XjCodexLectureItem>();
		int lectureTotalCount = lectureItems.Count;
		if (includeLectures) TrimList(lectureItems, MaxSnapshotLectures);
		List<XjCodexConflictItem> conflicts = includeConflicts
			? BuildConflicts(sectItems, cityItems, formationItems, governance, familyItems, yinSiMissions, secretRealmItems, adventureItems)
			: new List<XjCodexConflictItem>();
		List<XjCodexHistoryItem> history = new List<XjCodexHistoryItem>();
		List<XjCodexHistorySubjectItem> historyActors = new List<XjCodexHistorySubjectItem>();
		List<XjCodexHistorySubjectItem> historyFamilies = new List<XjCodexHistorySubjectItem>();
		List<XjCodexHistorySubjectItem> historySects = new List<XjCodexHistorySubjectItem>();
		List<XjCodexHistoryItem> personalBiographies = new List<XjCodexHistoryItem>();
		List<XjCodexHistorySubjectItem> biographyActors = new List<XjCodexHistorySubjectItem>();
		List<XjCodexHistoryItem> familyChronicles = new List<XjCodexHistoryItem>();
		List<XjCodexHistorySubjectItem> chronicleFamilies = new List<XjCodexHistorySubjectItem>();
		List<XjCodexHistoryItem> sectChronicles = new List<XjCodexHistoryItem>();
		List<XjCodexHistorySubjectItem> chronicleSects = new List<XjCodexHistorySubjectItem>();
		XjCodexThreeBookHealth threeBookHealth = new XjCodexThreeBookHealth();
		if (includeHistoryBooks)
		{
			history = BuildHistory(
				XjWorldHistoryStore.ReadSnapshot(MaxSnapshotHistory), cityNames, sectNames, familyNames,
				out historyActors, out historyFamilies, out historySects);
			personalBiographies = BuildThreeBookHistory(
				XjPersonalBiographyStore.ReadSnapshot(MaxSnapshotPersonalBiography), 0, cityNames, sectNames, familyNames,
				out biographyActors);
			familyChronicles = BuildThreeBookHistory(
				XjFamilyChronicleBookStore.ReadSnapshot(MaxSnapshotFamilyChronicle), 1, cityNames, sectNames, familyNames,
				out chronicleFamilies);
			sectChronicles = BuildThreeBookHistory(
				XjSectChronicleStore.ReadSnapshot(MaxSnapshotSectChronicle), 2, cityNames, sectNames, familyNames,
				out chronicleSects);
			threeBookHealth = XjThreeBookDiagnostics.BuildHealth();
		}
		else if (includeWorldOverview)
		{
			// 天下总览仅需最近数条大事，不携带三书及主体导航索引。
			history = BuildHistory(
				XjWorldHistoryStore.ReadSnapshot(12), cityNames, sectNames, familyNames,
				out historyActors, out historyFamilies, out historySects);
		}

		List<XjCodexCenturyAnnalsItem> centuryAnnals = includeCenturyAnnals
			? BuildCenturyAnnals(XjCenturyAnnalsStore.ReadSnapshot(MaxSnapshotCenturyAnnals))
			: new List<XjCodexCenturyAnnalsItem>();
		IReadOnlyList<XjSwordIntentArchiveRecord> swordIntents = includeSwordIntents
			? XjSwordIntentRegistry.ReadSnapshot()
			: Array.Empty<XjSwordIntentArchiveRecord>();
		int cultivatorTotalCount = XjCultivatorCache.Count;
		XjCodexCultivationClimate cultivationClimate = new XjCodexCultivationClimate();
		if (includeClimate)
		{
			IReadOnlyList<XjCodexHistoryItem> climateHistory = history;
			if (!includeHistoryBooks)
			{
				climateHistory = BuildHistory(
					XjWorldHistoryStore.ReadSnapshot(240), cityNames, sectNames, familyNames,
					out _, out _, out _);
			}
			cultivationClimate = BuildCultivationClimate(
				worldYear, cultivatorTotalCount, climateHistory, centuryAnnals);
		}

		int openCraftTaskCount = XjCraftDomainRegistry.OpenTaskCount;
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
			ViewKey = BuildViewKey(_activeTab, _fullSearchActive),
			WorldYear = worldYear,
			PublishedAt = XjChronology.FormatYear(worldYear),
			MapWidth = Math.Max(0, MapBox.width),
			MapHeight = Math.Max(0, MapBox.height),
			KingdomCount = kingdomCount,
			CityCount = cityItems.Count,
			CultivatorCount = cultivatorTotalCount,
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
			CultivationClimate = cultivationClimate,
			Sects = sectItems,
			SectResources = includeSectResources ? sectResourceItems : Array.Empty<XjCodexSectResourceItem>(),
			Families = familyItems,
			Cities = cityItems,
			Formations = formationItems,
			SecretRealms = secretRealmItems,
			CraftActors = includeCraftActors ? craftItems : Array.Empty<XjCodexCraftItem>(),
			CraftProfessionSummaries = craftProfessionSummaries,
			JinDan = includeJinDanActors ? jinDanItems : Array.Empty<XjCodexJinDanItem>(),
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
			CenturyAnnals = centuryAnnals,
			SwordIntents = swordIntents
		};
	}

	private static int ResolveCenturyAnnalsStartYear(IReadOnlyList<XjCodexCenturyAnnalsItem> centuryAnnals)
	{
		int baseWorldYear = Math.Max(0, XjCenturyAnnalsStore.AnnalsBaseWorldYear);
		if (baseWorldYear > 0)
		{
			return 1;
		}
		int startYear = 0;

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

	private static void BuildLightweightCityNames(
		out Dictionary<long, string> cityNames,
		out Dictionary<long, string> kingdomNames,
		out int kingdomCount)
	{
		cityNames = new Dictionary<long, string>();
		kingdomNames = new Dictionary<long, string>();
		HashSet<long> kingdomIds = new HashSet<long>();
		IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
		for (int i = 0; i < cities.Count; i++)
		{
			City city = cities[i];
			if (city?.data == null) continue;
			long cityId = city.data.id;
			cityNames[cityId] = SafeDataName(city.data, "无名城");
			long kingdomId = city.kingdom?.data?.id ?? 0L;
			if (kingdomId <= 0L) continue;
			kingdomIds.Add(kingdomId);
			kingdomNames[kingdomId] = city.kingdom?.name ?? "无国属";
		}
		kingdomCount = kingdomIds.Count;
	}

	private static Dictionary<long, string> BuildLightweightFamilyNames(
		IReadOnlyList<XjFamilyLedgerAggregate> aggregates,
		IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance,
		IReadOnlyList<XjSectFamilySeatArchiveRecord> familySeats)
	{
		HashSet<long> ids = new HashSet<long>();
		if (aggregates != null)
		{
			for (int i = 0; i < aggregates.Count; i++)
			{
				long id = aggregates[i].FamilyStableId;
				if (id > 0L) ids.Add(id);
			}
		}
		if (governance != null)
		{
			for (int i = 0; i < governance.Count; i++)
			{
				XjCityFamilyGovernanceArchiveRecord item = governance[i];
				if (item == null) continue;
				if (item.GoverningFamilyId > 0L) ids.Add(item.GoverningFamilyId);
				if (item.ChallengerFamilyId > 0L) ids.Add(item.ChallengerFamilyId);
			}
		}
		if (familySeats != null)
		{
			for (int i = 0; i < familySeats.Count; i++)
			{
				long id = familySeats[i]?.FamilyId ?? 0L;
				if (id > 0L) ids.Add(id);
			}
		}
		Dictionary<long, string> names = new Dictionary<long, string>(ids.Count);
		foreach (long id in ids) names[id] = XjFamilyDisplayNameResolver.Resolve(id);
		return names;
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
		long luoXiaShanHostCityId = XjDongTianRegistry.TryResolveLuoXiaShanHostCityId(out long resolvedLuoXiaShanHostCityId)
			? resolvedLuoXiaShanHostCityId
			: 0L;
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
				TryReadCityMapPosition(city, out int tileX, out int tileY);
				int cultivatorCount = 0;
				if (XjSectCultivatorCityIndex.TryGetAggregate(city, out XjCityCultivatorAggregate cityAggregate))
				{
					cultivatorCount = cityAggregate.CultivatorCount;
				}
				cityItems.Add(new XjCodexCityItem
				{
					CityId = cityId,
					Name = cityName,
					TileX = tileX,
					TileY = tileY,
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
				HasLuoXiaShan = cityId == luoXiaShanHostCityId,
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
		out Dictionary<long, int> jinDanBySect,
		out Dictionary<long, int> ziFuPathBySect,
		out Dictionary<long, int> fuQiZhenRenBySect,
		out Dictionary<long, int> ziJinJinDanBySect,
		out Dictionary<long, int> zhenJunYuShiBySect,
		out Dictionary<long, XjDaoTuTraditionSummary> daoTuTraditionBySect)
	{
		cultivatorsBySect = new Dictionary<long, int>();
		ziFuBySect = new Dictionary<long, int>();
		jinDanBySect = new Dictionary<long, int>();
		ziFuPathBySect = new Dictionary<long, int>();
		fuQiZhenRenBySect = new Dictionary<long, int>();
		ziJinJinDanBySect = new Dictionary<long, int>();
		zhenJunYuShiBySect = new Dictionary<long, int>();
		daoTuTraditionBySect = new Dictionary<long, XjDaoTuTraditionSummary>();
		if (sectRecords == null) return;
		for (int i = 0; i < sectRecords.Count; i++)
		{
			XjSectArchiveRecord record = sectRecords[i];
			if (record == null || record.SectId <= 0L) continue;

			// 服气真人可以合法担任宗主，但旧的紫府金丹修士索引不保证收录服气路径。
			// 谱系已经能从宗主档案恢复其身份，因此计数时把当前宗主并入同一去重集合，
			// 避免“宗主是真人，高境坐镇却显示真人0”。
			HashSet<long> actorIds = new HashSet<long>();
			IReadOnlyList<long> indexedActorIds = XjSectAuthorityStore.GetActorIdsForSect(record.SectId);
			for (int j = 0; j < indexedActorIds.Count; j++)
			{
				if (indexedActorIds[j] > 0L) actorIds.Add(indexedActorIds[j]);
			}
			long sovereignActorId = record.SovereignActorId;
			ResolveSectSovereignForSnapshot(record, ref sovereignActorId);
			if (sovereignActorId > 0L) actorIds.Add(sovereignActorId);
			List<Actor> traditionActors = new List<Actor>(actorIds.Count);

			foreach (long actorId in actorIds)
			{
				if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
				if (ReadActorSectId(actor) != record.SectId) continue;
				traditionActors.Add(actor);
				AddCount(cultivatorsBySect, record.SectId, 1);
				string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
				switch (XjHighRealmIdentity.ResolveKind(actor, realmId))
				{
					case XjHighRealmKind.ZiFu:
						AddCount(ziFuBySect, record.SectId, 1);
						AddCount(ziFuPathBySect, record.SectId, 1);
						break;
					case XjHighRealmKind.FuQiZhenRen:
						AddCount(ziFuBySect, record.SectId, 1);
						AddCount(fuQiZhenRenBySect, record.SectId, 1);
						break;
					case XjHighRealmKind.ZiJinJinDan:
						AddCount(jinDanBySect, record.SectId, 1);
						AddCount(ziJinJinDanBySect, record.SectId, 1);
						break;
					case XjHighRealmKind.ZhenJunYuShi:
						AddCount(jinDanBySect, record.SectId, 1);
						AddCount(zhenJunYuShiBySect, record.SectId, 1);
						break;
				}
			}
			if (XjDaoTuHeritageService.TryResolveTradition(
				traditionActors, record.SectId, 0L, out XjDaoTuTraditionSummary tradition))
			{
				daoTuTraditionBySect[record.SectId] = tradition;
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
		if (!XjAlchemyCatalog.TryGetPill(pillId, out XjAlchemyPillDef definition)) return "未载";
		string path = definition.PathAffinity switch
		{
			XjAlchemyCultivationPath.ZiJin => "紫府金丹道",
			XjAlchemyCultivationPath.FuQi => "服气养性",
			_ => "两路通用"
		};
		return path + "·" + ResolveRealmDisplay(definition.MinApplicableRealmId);
	}

	private static string ResolveTalismanRealmDisplay(XjTalismanDefinition definition)
	{
		return definition == null ? "未载" : ResolveRealmDisplay(definition.MinRealmId);
	}

	private static string ResolveRealmDisplay(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return "真君级";
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return "真人级";
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return "黄冠级";
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return "金丹级";
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return "紫府级";
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return "筑基级";
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return "炼气级";
		return XjRealmHelper.GetDisplayName(realmId);
	}


	private static string EmptyResourceSource(string sourceType, string fallback)
	{
		if (string.Equals(sourceType, XjSectGongFaPavilion.SourceTypeGongFa, StringComparison.Ordinal)) return EmptyText(fallback, "功法入阁");
		if (string.Equals(sourceType, XjSectGongFaPavilion.SourceTypeQiuJinFa, StringComparison.Ordinal)) return EmptyText(fallback, "求金法入阁");
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


	private static string SafeDataName(BaseSystemData data, string fallback)
	{
		if (data == null) return fallback;
		try { return string.IsNullOrWhiteSpace(data.name) ? fallback : data.name.Trim(); }
		catch { return fallback; }
	}
}
