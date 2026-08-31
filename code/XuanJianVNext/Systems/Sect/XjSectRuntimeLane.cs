using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 唯一宗门运行车道。权威投影、年度治理与五年维护共用同一预算；
/// 当前态维护在高速追赶时折叠到最新周期，不逐周期回放。
/// </summary>
internal static class XjSectRuntimeLane
{
	private const int YearsPerPeriod = 5;
	private const int RecruitIntervalYears = 15;
	private const int CultivatorIndexCleanupBudget = 64;
	private static List<long> _cityIds = new List<long>();
	private static int _lastCompletedPeriod;
	private static int _queuedThroughPeriod;
	private static int _activePeriod;
	private static int _importedLastCompletedPeriod = -1;
	private static int _currentYear;
	private static int _cursor;
	private static int _maintained;
	private static bool _maintenanceActive;

	internal static bool HasPending => XjNationSectTransitionSystem.HasPending
		|| XjSectProjection.HasPending
		|| XjSectGovernanceRuntimeLane.HasPending
		|| _maintenanceActive
		|| _queuedThroughPeriod > _lastCompletedPeriod;

	internal static void InitializeAfterLoad(int currentYear)
	{
		ResetMaintenance();
		int reachedPeriod = ToPeriod(currentYear);
		_lastCompletedPeriod = _importedLastCompletedPeriod < 0
			? reachedPeriod
			: Math.Min(Math.Max(0, _importedLastCompletedPeriod), reachedPeriod);
		_queuedThroughPeriod = reachedPeriod;
		XjSectProjection.ScheduleAll();
	}

	internal static int ExportLastCompletedPeriod() => Math.Max(0, _lastCompletedPeriod);
	internal static void ImportLastCompletedPeriod(int period) => _importedLastCompletedPeriod = period;

	internal static void Schedule(int currentYear)
	{
		if (currentYear <= 0) return;
		XjSectGovernanceRuntimeLane.Schedule(currentYear);
		int reachedPeriod = ToPeriod(currentYear);
		if (reachedPeriod > _lastCompletedPeriod) _queuedThroughPeriod = Math.Max(_queuedThroughPeriod, reachedPeriod);
		TryBeginLatestMaintenance();
	}

	internal static void Tick(int budget)
	{
		Tick(new XjCooperativeBudget(
			Math.Max(0, budget),
			0d,
			XjRuntimeFramePriority.Background));
	}

	internal static void Tick(XjCooperativeBudget budget)
	{
		if (budget == null || budget.ShouldYield) return;

		int transitionVisits = 0;
		while (transitionVisits < 4 && XjNationSectTransitionSystem.HasPending && budget.TryTake())
		{
			XjNationSectTransitionSystem.Tick(1);
			transitionVisits++;
		}
		if (budget.ShouldYield) return;

		int projectionVisits = 0;
		while (projectionVisits < 2 && XjSectProjection.HasPending && budget.TryTake())
		{
			XjSectProjection.Tick(1);
			projectionVisits++;
		}
		if (budget.ShouldYield) return;

		// 五年招募/师徒维护不能永远排在年度治理之后。维护存在时保留
		// 一个 cooperative item，让治理和招募在高压下都能持续向前推进。
		TryBeginLatestMaintenance();
		int maintenanceReserve = _maintenanceActive && budget.RemainingItems > 0 ? 1 : 0;
		int governanceVisits = 0;
		while (governanceVisits < 4
			&& XjSectGovernanceRuntimeLane.HasPending
			&& budget.RemainingItems > maintenanceReserve
			&& budget.TryTake())
		{
			XjSectGovernanceRuntimeLane.Tick(1);
			governanceVisits++;
		}
		if (budget.ShouldYield || !_maintenanceActive) return;

		while (_maintenanceActive && budget.TryTake())
		{
			TickMaintenance(1);
		}
	}

	internal static void Clear()
	{
		_lastCompletedPeriod = 0;
		_queuedThroughPeriod = 0;
		_importedLastCompletedPeriod = -1;
		XjNationSectTransitionSystem.Clear();
		XjSectGovernanceRuntimeLane.Clear();
		ResetMaintenance();
	}

	private static void TryBeginLatestMaintenance()
	{
		if (_maintenanceActive || _queuedThroughPeriod <= _lastCompletedPeriod) return;
		// 维护是当前态对账，直接折叠到已到达的最新五年周期。
		_activePeriod = _queuedThroughPeriod;
		_currentYear = _activePeriod * YearsPerPeriod;
		_cityIds = SelectMaintenanceCandidateIds(_activePeriod);
		_cursor = 0;
		_maintained = 0;
		_maintenanceActive = _cityIds.Count > 0;
		if (!_maintenanceActive) CompleteMaintenance();
	}

	private static void TickMaintenance(int budget)
	{
		IReadOnlyDictionary<long, List<long>> cityIndex = XjSectCultivatorCityIndex.GetCityIndex();
		int visited = 0;
		while (visited < budget && _cursor < _cityIds.Count)
		{
			long cityId = _cityIds[_cursor++];
			visited++;
			if (cityId <= 0L || !XjWorldLookupIndex.TryResolveCity(cityId, out City city) || city?.data == null) continue;
			if (TryMaintainExisting(city, _currentYear, cityIndex)) _maintained++;
			// 师徒关系属于“同宗门”而不是“同一座山门城市”。旧版只把代表城当前
			// 在城修士交给师徒系统，门人一旦分布到属城/外地，整宗就会表现成不收徒。
			// 权威成员表本就是稀疏稳定ID列表，直接按SectId读取，不扫描普通人口。
			long sectId = XjSectOwnership.ResolveSectId(city);
			if (sectId > 0L)
			{
				IReadOnlyList<long> memberIds = XjSectAuthorityStore.GetActorIdsForSect(sectId);
				XjSectMentorshipSystem.TickCity(city, memberIds, _currentYear);
			}
		}
		if (_cursor >= _cityIds.Count) CompleteMaintenance();
	}

	private static List<long> SelectMaintenanceCandidateIds(int maintenancePeriod)
	{
		// 五年维护跨渲染帧保存的只有稳定 CityId；真正执行时再经统一世界索引解析。
		// 这避免维护车道把 WorldBox City 实例跨帧长期留在自己的运行态中。
		XjSectCultivatorCityIndex.CleanupInvalid(CultivatorIndexCleanupBudget);
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		List<(long SectId, long CityId)> candidates = new List<(long SectId, long CityId)>(sects?.Count ?? 0);
		if (sects == null || sects.Count == 0) return new List<long>();

		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord sect = sects[i];
			if (sect?.SectId <= 0L || !XjSectOwnership.TryResolvePrimaryCity(sect, out City city) || city?.data == null) continue;
			long cityId = city.data.id;
			if (cityId > 0L) candidates.Add((sect.SectId, cityId));
		}
		candidates.Sort((left, right) => left.SectId.CompareTo(right.SectId));
		List<long> result = new List<long>(candidates.Count);
		// 多宗门共享外部散修池后，五年维护不能永远由最老 SectId 先拿候选人。
		// 每个五年周期把起点向后轮转一宗；仍会完整处理全部现存宗门。
		int start = candidates.Count <= 1 ? 0 : Math.Max(0, maintenancePeriod - 1) % candidates.Count;
		for (int i = 0; i < candidates.Count; i++) result.Add(candidates[(start + i) % candidates.Count].CityId);
		return result;
	}

	private static bool TryMaintainExisting(City city, int currentYear, IReadOnlyDictionary<long, List<long>> cityIndex)
	{
		if (city?.data == null || cityIndex == null || !XjSectOwnership.TryResolve(city, out XjSectArchiveRecord sect) || sect == null) return false;

		XjSectMaintenanceSnapshot snapshot = XjSectMaintenanceSnapshot.Build(city, currentYear);
		XjSectSelfHeal.Repair(snapshot);

		int lastRecruitYear = sect.LastRecruitYear;
		int memberCount = XjSectAuthorityStore.CountMembers(sect.SectId);
		bool recovery = memberCount > 0 && memberCount < XjSectCityData.MinimumHealthySectMembers;
		// 1~17人的小宗仍处于恢复期：每个五年维护都继续补员；达到健康线后
		// 才恢复15年常规纳新。成功收一两人不能让小宗再次沉睡十五年。
		bool recruitDue = recovery
			|| lastRecruitYear <= 0
			|| currentYear - lastRecruitYear >= RecruitIntervalYears;
		int recruited = recruitDue
			? XjSectCityData.TryRecruitSectCultivators(city, currentYear, cityIndex)
			: 0;
		// 空招不消耗冷却；下一个五年维护周期自然重试。
		if (recruited > 0) XjSectCommands.SetLastRecruitYear(sect.SectId, currentYear);

		// 兴衰本来就是五年宗门当前态维护，直接跟随这条有保留预算的车道结算。
		// 年度治理仍保留幂等兜底调用，但 LastProsperityYear 会保证同一边界最多扫描一次成员。
		XjSectProsperitySystem.Evaluate(sect.SectId, currentYear);
		return true;
	}

	private static void CompleteMaintenance()
	{
		if (_activePeriod > 0)
		{
			_lastCompletedPeriod = Math.Max(_lastCompletedPeriod, _activePeriod);
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Runtime);
		}
		ResetMaintenance();
	}

	private static void ResetMaintenance()
	{
		_cityIds.Clear();
		_activePeriod = 0;
		_currentYear = 0;
		_cursor = 0;
		_maintained = 0;
		_maintenanceActive = false;
	}

	private static int ToPeriod(int year) => year <= 0 ? 0 : year / YearsPerPeriod;
}
