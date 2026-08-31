using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 今释七相中的“自动杀生度化”后台车道。
///
/// 人口压力下的今释自动度化后台车道。
/// 1) 七相只决定收益与行为倾向，不再决定“是否拥有度化行为”；法师及以上今释均可参与；
/// 2) 优先在本人当前城市寻找目标；本城无目标时只抽样少量城市与少量居民，绝不扫描全世界人口；
/// 3) 每名今释每年最多自动度化一具肉身，并继续受个人三具/年的总上限；
/// 4) 世界年度总额随文明人口增长，使5000人口开闸后能够真实形成调节作用；
/// 5) 不积跨年债务，不建立常驻世界候选池。
/// </summary>
internal static class XjShiDuhuaRuntimeLane
{
	private const int PopulationOpenThreshold = 5000;
	private const int PopulationCloseThreshold = 3000;
	private const int LocalCandidateScanBudget = 96;
	private const int FallbackCityProbeBudget = 12;
	private const int FallbackCandidateScanBudget = 32;
	private const int AutomaticVictimPerTeacherPerYear = 1;

	private static readonly Queue<long> TeacherQueue = new Queue<long>();
	private static readonly HashSet<long> QueuedTeachers = new HashSet<long>();

	private static int _populationSnapshotYear = -1;
	private static int _cachedCivilizedPopulation;
	private static int _globalAutoYear = -1;
	private static int _globalAutoKills;
	private static int _globalAutoCap;
	private static int _lastRosterScheduleYear = -1;

	// 人口闸门不能依赖“恰好有一名今释先跑到年度角色管线”才能被发现。
	// 每个世界年第一次后台检查只统计城市人口（O(城市数)）；一旦开闸，再从
	// 既有释修稀疏索引（今释总量硬上限300）补排法师及以上角色。
	internal static bool HasPending
	{
		get
		{
			if (World.world == null) return false;
			int year = Math.Max(1, XjYearTracker.CurrentYear);
			return _populationSnapshotYear != year
				|| (XjShiDomainState.IsDuhuaPopulationGateActive && TeacherQueue.Count > 0);
		}
	}

	internal static int PendingTeachers => TeacherQueue.Count;

	internal static void Schedule(Actor teacher, int annualYear)
	{
		if (teacher?.data == null || !teacher.isAlive() || annualYear <= 0
			|| !XjShiState.TryBuildSnapshot(teacher, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(snapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster))
		{
			return;
		}

		long teacherId = ((BaseSystemData)teacher.data).id;
		if (teacherId <= 0L) return;

		if (!EvaluatePopulationGate(annualYear))
		{
			ResetTeacherQuota(teacher, annualYear, markScheduled: true);
			return;
		}

		EnsureGlobalAutoBudget(annualYear);
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiDuhuaAutoLastScheduledYear, out int lastScheduledYear);
		if (lastScheduledYear < annualYear)
		{
			// 新年只发本年额度，绝不追补往年没吃到的人。
			int alreadyDuhua = XjShiSentientConsumptionSystem.GetDuhuaActualCountThisYear(teacher, annualYear);
			int remainingPersonal = Math.Max(0,
				XjShiSentientConsumptionSystem.AnnualActualVictimLimit - alreadyDuhua);
			int debt = Math.Min(AutomaticVictimPerTeacherPerYear, remainingPersonal);
			if (_globalAutoKills >= _globalAutoCap) debt = 0;

			XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoLastScheduledYear, annualYear);
			XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDebt, debt);
			XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDueYear, debt > 0 ? annualYear : 0);
			XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, 0);
		}

		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDebt, out int pendingDebt);
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDueYear, out int dueYear);
		if (pendingDebt > 0 && dueYear == annualYear && _globalAutoKills < _globalAutoCap)
		{
			EnqueueTeacher(teacherId);
		}
	}

	internal static void Tick()
	{
		int currentYear = Math.Max(1, XjYearTracker.CurrentYear);
		bool gateActive = EvaluatePopulationGate(currentYear);
		if (!gateActive)
		{
			if (TeacherQueue.Count > 0) CancelAllQueuedDebt(currentYear);
			return;
		}

		// 即使某一年的年度角色管线因追赶/初始化问题尚未跑到今释，人口开闸后
		// 也从既有释修索引补排一次。这里只看最多300个今释，不碰世界人口表。
		ScheduleEligibleModernShi(currentYear);
		if (TeacherQueue.Count == 0) return;
		EnsureGlobalAutoBudget(currentYear);
		if (_globalAutoKills >= _globalAutoCap)
		{
			CancelAllQueuedDebt(currentYear);
			return;
		}

		long teacherId = TeacherQueue.Dequeue();
		QueuedTeachers.Remove(teacherId);
		if (!XjActorRegistry.ResolveKnownOrWorld(teacherId, out Actor teacher)
			|| teacher?.data == null || !teacher.isAlive()
			|| !XjShiState.TryBuildSnapshot(teacher, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(snapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster))
		{
			return;
		}
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDueYear, out int dueYear);
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDebt, out int debt);
		if (dueYear != currentYear || debt <= 0)
		{
			ResetTeacherQuota(teacher, currentYear, markScheduled: false);
			return;
		}

		int actualThisYear = XjShiSentientConsumptionSystem.GetDuhuaActualCountThisYear(teacher, currentYear);
		if (actualThisYear >= XjShiSentientConsumptionSystem.AnnualActualVictimLimit)
		{
			FinishCurrentQuota(teacher, currentYear);
			return;
		}

		// 没有本城普通众生可取，就把这一年的自动额度作废，不跨城抓人，也不在
		// 后台队列里无限自旋。
		if (!TryTakeBoundedTarget(teacher, currentYear, out Actor target))
		{
			FinishCurrentQuota(teacher, currentYear);
			return;
		}

		long targetId = target?.data == null ? 0L : ((BaseSystemData)target.data).id;
		if (targetId <= 0L)
		{
			FinishCurrentQuota(teacher, currentYear);
			return;
		}

		string targetName = target.getName();
		XjCombatTracker.ClearAttackerRecord(target);
		bool died = XjVanillaDeathGuard.TryExecuteForceDeath(
			target, (AttackType)11, true, XjDeathCause.Combat);
		if (!died || target.isAlive()
			|| !XjShiSentientConsumptionSystem.TryRecordDuhuaKill(
				teacher, targetId, targetName, currentYear, automated: true))
		{
			// 真正死亡事务失败时也不换一个人连续尝试，避免第三方死亡补丁异常导致杀戮循环。
			FinishCurrentQuota(teacher, currentYear);
			return;
		}

		_globalAutoKills++;
		_cachedCivilizedPopulation = Math.Max(0, _cachedCivilizedPopulation - 1);
		CompleteOneDebt(teacher, currentYear);

		if (_cachedCivilizedPopulation < PopulationCloseThreshold)
		{
			XjShiDomainState.SetDuhuaPopulationGateActive(false);
			CancelAllQueuedDebt(currentYear);
		}
		else if (_globalAutoKills >= _globalAutoCap)
		{
			CancelAllQueuedDebt(currentYear);
		}
	}

	internal static void InvalidateCandidateSnapshot()
	{
		// v5不再维护全世界候选池；只使人口快照与本年释修排程失效。
		_populationSnapshotYear = -1;
		_lastRosterScheduleYear = -1;
	}

	internal static void Clear()
	{
		TeacherQueue.Clear();
		QueuedTeachers.Clear();
		_populationSnapshotYear = -1;
		_cachedCivilizedPopulation = 0;
		_globalAutoYear = -1;
		_globalAutoKills = 0;
		_globalAutoCap = 0;
		_lastRosterScheduleYear = -1;
	}

	private static void ScheduleEligibleModernShi(int year)
	{
		int safeYear = Math.Max(1, year);
		if (_lastRosterScheduleYear == safeYear) return;
		_lastRosterScheduleYear = safeYear;

		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor teacher)
				|| teacher?.data == null || !teacher.isAlive()
				|| !XjShiState.TryBuildSnapshot(teacher, out XjShiSnapshot snapshot)
				|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
				|| XjShiCatalog.GetRank(snapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster))
			{
				continue;
			}
			Schedule(teacher, safeYear);
		}
	}

	private static void EnsureGlobalAutoBudget(int year)
	{
		int safeYear = Math.Max(1, year);
		if (_globalAutoYear == safeYear) return;
		_globalAutoYear = safeYear;
		_globalAutoKills = 0;
		// 自动度化必须真正承担高人口调节职责。按当前文明人口约1%给出年度世界额度，
		// 5000人口约50具、7500约75具，10000以上封顶80具；实际执行仍受“每位今释
		// 每年最多一具自动度化”和本地/有界目标抽样双重限制，不会形成单帧杀戮洪峰。
		_globalAutoCap = Math.Clamp((int)Math.Ceiling(_cachedCivilizedPopulation / 100d), 8, 80);
	}

	private static void EnqueueTeacher(long teacherId)
	{
		if (teacherId > 0L && QueuedTeachers.Add(teacherId)) TeacherQueue.Enqueue(teacherId);
	}

	private static void CompleteOneDebt(Actor teacher, int dueYear)
	{
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDebt, out int debt);
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, out int done);
		debt = Math.Max(0, debt - 1);
		done = Math.Max(0, done) + 1;

		int actualThisYear = XjShiSentientConsumptionSystem.GetDuhuaActualCountThisYear(teacher, dueYear);
		int remaining = Math.Max(0, XjShiSentientConsumptionSystem.AnnualActualVictimLimit - actualThisYear);
		debt = Math.Min(debt, remaining);
		if (debt <= 0)
		{
			if (done > 0)
			{
				XjShiSentientConsumptionSystem.OnAnnualDuhuaBatch(
					teacher, dueYear, done, "众生", "众生");
			}
			XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDebt, 0);
			XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDueYear, 0);
			XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, 0);
			return;
		}

		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDebt, debt);
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDueYear, dueYear);
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, done);
	}

	private static void FinishCurrentQuota(Actor teacher, int dueYear)
	{
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, out int done);
		if (done > 0)
		{
			XjShiSentientConsumptionSystem.OnAnnualDuhuaBatch(
				teacher, Math.Max(1, dueYear), done, "众生", "众生");
		}
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDebt, 0);
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDueYear, 0);
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, 0);
	}

	private static void ResetTeacherQuota(Actor teacher, int year, bool markScheduled)
	{
		if (teacher?.data == null) return;
		if (markScheduled)
		{
			XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoLastScheduledYear, Math.Max(0, year));
		}
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDebt, 0);
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDueYear, 0);
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, 0);
	}

	private static void CancelAllQueuedDebt(int year)
	{
		int resolvedYear = Math.Max(1, year);
		while (TeacherQueue.Count > 0)
		{
			long actorId = TeacherQueue.Dequeue();
			if (XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) && actor?.data != null)
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, out int done);
				if (done > 0) FinishCurrentQuota(actor, resolvedYear);
				else ResetTeacherQuota(actor, resolvedYear, markScheduled: true);
			}
		}
		QueuedTeachers.Clear();
	}

	private static bool EvaluatePopulationGate(int year)
	{
		int safeYear = Math.Max(1, year);
		if (_populationSnapshotYear != safeYear)
		{
			_cachedCivilizedPopulation = CountCivilizedPopulation();
			_populationSnapshotYear = safeYear;
		}

		bool active = XjShiDomainState.IsDuhuaPopulationGateActive;
		bool next = active;
		if (!active && _cachedCivilizedPopulation >= PopulationOpenThreshold) next = true;
		else if (active && _cachedCivilizedPopulation < PopulationCloseThreshold) next = false;
		if (next != active) XjShiDomainState.SetDuhuaPopulationGateActive(next);
		return next;
	}

	private static int CountCivilizedPopulation()
	{
		IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
		if (cities == null || cities.Count == 0) return 0;
		long total = 0L;
		for (int i = 0; i < cities.Count; i++)
		{
			City city = cities[i];
			if (city?.units == null) continue;
			total += city.units.Count;
			if (total >= int.MaxValue) return int.MaxValue;
		}
		return (int)total;
	}

	private static bool TryTakeBoundedTarget(Actor teacher, int currentYear, out Actor target)
	{
		target = null;
		if (teacher?.data == null) return false;
		City localCity = teacher.city;
		if (TryTakeTargetFromCity(teacher, localCity, currentYear, LocalCandidateScanBudget, out target))
			return true;

		// 摩诃、法相等高位今释长期驻于旃檀林时往往没有普通城市居民可度化。
		// 这里不回退成全世界单位扫描，只从已有城市快照里最多探查少量城市，
		// 每城再检查有限居民；因此人口越大也不会把一次度化退化成O(世界人口)。
		IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
		if (cities == null || cities.Count == 0) return false;
		long teacherId = ((BaseSystemData)teacher.data).id;
		int cityStart = XjDeterministicHash.PositiveIndex(
			teacherId * 214013L + currentYear * 2531011L,
			"shi.duhua.fallback_city", Math.Max(1, cities.Count));
		int probes = Math.Min(FallbackCityProbeBudget, cities.Count);
		for (int i = 0; i < probes; i++)
		{
			City city = cities[(cityStart + i) % cities.Count];
			if (city == null || ReferenceEquals(city, localCity)) continue;
			if (TryTakeTargetFromCity(teacher, city, currentYear + i + 1,
				FallbackCandidateScanBudget, out target)) return true;
		}
		return false;
	}

	private static bool TryTakeTargetFromCity(Actor teacher, City city, int currentYear,
		int scanBudget, out Actor target)
	{
		target = null;
		if (teacher?.data == null || city?.units == null || city.units.Count == 0) return false;

		long teacherId = ((BaseSystemData)teacher.data).id;
		int count = city.units.Count;
		int probeCount = Math.Min(Math.Max(1, scanBudget), count);
		int start = XjDeterministicHash.PositiveIndex(
			teacherId * 1103515245L + currentYear * 2654435761L,
			"shi.duhua.city_target", Math.Max(1, count));

		for (int i = 0; i < probeCount; i++)
		{
			Actor candidate = city.units[(start + i) % count];
			if (candidate == null || ReferenceEquals(candidate, teacher)
				|| candidate.city != city || !XjShiEntrySystem.IsDuhuaTarget(candidate)) continue;
			target = candidate;
			return true;
		}
		return false;
	}
}
