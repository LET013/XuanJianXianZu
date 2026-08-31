using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Keeps native kingdom splits from producing empty states or low-realm rebel
/// polities inside established sect territory. Real ZiFu/JinDan splits are
/// handled by the sect transition and soft-split systems before this guard runs.
/// </summary>
internal static class XjNationSectRebellionGuard
{
	private sealed class SubjugationLock
	{
		// Native kingdom id only. This lock prevents immediate WorldBox-level
		// rebel refounding after subjugation and must never mutate SectId data.
		internal long KingdomId;
		internal int ExpiresYear;
	}

	private const int SubjugationLockYears = 12;
	private const int SuppressedFoundingMemoYears = 2;
	private static int controlledFoundingDepth;
	private static readonly Dictionary<long, SubjugationLock> SubjugationLocksByCity = new Dictionary<long, SubjugationLock>();
	private static readonly Dictionary<long, int> SuppressedFoundingByCity = new Dictionary<long, int>();
	private static readonly List<long> ExpiredCityStateIds = new List<long>();

	internal static bool IsControlledFounding => controlledFoundingDepth > 0;

	internal static void BeginControlledFounding()
	{
		controlledFoundingDepth++;
	}

	internal static void EndControlledFounding()
	{
		if (controlledFoundingDepth > 0) controlledFoundingDepth--;
	}

	internal static void MarkHighRealmSubjugation(City city, Kingdom victor, int currentYear)
	{
		long cityId = city?.data?.id ?? 0L;
		long kingdomId = victor?.data?.id ?? 0L;
		if (cityId <= 0L || kingdomId <= 0L || currentYear <= 0) return;
		SubjugationLocksByCity[cityId] = new SubjugationLock
		{
			KingdomId = kingdomId,
			ExpiresYear = currentYear + SubjugationLockYears
		};
	}

	/// <summary>
	/// 原生反叛只能处理普通国家。宗门领土的分裂必须由软分裂系统统一校验
	/// 紫府/金丹与山门距离，避免先生成零城国家再事后回滚。
	/// This guard is intentionally one-way: it allows or denies native founding
	/// but never assigns cities, actors or families to a sect.
	/// </summary>
	internal static bool AllowNativeKingdomFounding(City city, Actor founder)
	{
		if (IsControlledFounding) return true;
		if (!XjWorldSchemaGuard.GameplayEnabled)
		{
			return true;
		}

		if (founder != null && XjYinSiTraitLifecycle.IsYinSi(founder))
		{
			return false;
		}

		if (city?.data == null)
		{
			return true;
		}

		Kingdom currentKingdom = city.kingdom;
		long currentKingdomId = currentKingdom?.data?.id ?? 0L;
		if (currentKingdomId <= 0L)
		{
			return true;
		}

		if (TryResolveProtectedSectId(city, out _) && IsFreshSubjugation(city, currentKingdomId)) return false;
		return true;
	}

	internal static int Sweep(int currentYear, int budget)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || budget <= 0)
		{
			return 0;
		}

		// Sect lifecycle is city/SectId driven. Native kingdoms are only an
		// optional WorldBox shell, so this maintenance pass must not scan or
		// retire kingdoms merely because their native city list changed.
		PruneExpiredCityState(currentYear);
		return 0;
	}

	private static void PruneExpiredCityState(int currentYear)
	{
		ExpiredCityStateIds.Clear();
		foreach (KeyValuePair<long, SubjugationLock> pair in SubjugationLocksByCity)
		{
			if (pair.Value == null || pair.Value.ExpiresYear < currentYear) ExpiredCityStateIds.Add(pair.Key);
		}
		for (int i = 0; i < ExpiredCityStateIds.Count; i++) SubjugationLocksByCity.Remove(ExpiredCityStateIds[i]);

		ExpiredCityStateIds.Clear();
		foreach (KeyValuePair<long, int> pair in SuppressedFoundingByCity)
		{
			if (currentYear - pair.Value >= SuppressedFoundingMemoYears) ExpiredCityStateIds.Add(pair.Key);
		}
		for (int i = 0; i < ExpiredCityStateIds.Count; i++) SuppressedFoundingByCity.Remove(ExpiredCityStateIds[i]);
		ExpiredCityStateIds.Clear();
	}

	private static bool TryResolveProtectedSectId(City city, out long sectId)
	{
		sectId = 0L;
		if (city?.data == null) return false;
		if (XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect)
			&& sect?.SectId > 0L)
		{
			sectId = sect.SectId;
			return true;
		}
		if (XjSectRepository.TryGetGovernance(city.data.id, out XjCityFamilyGovernanceArchiveRecord governance)
			&& governance?.SectId > 0L)
		{
			sectId = governance.SectId;
			return true;
		}
		return false;
	}

	internal static void ClearRuntimeState()
	{
		controlledFoundingDepth = 0;
		SubjugationLocksByCity.Clear();
		SuppressedFoundingByCity.Clear();
		ExpiredCityStateIds.Clear();
	}

	internal static void MarkSuppressedNativeFounding(City city, Actor founder)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || city?.data == null) return;
		long cityId = city.data.id;
		if (cityId <= 0L) return;

		int year = Math.Max(0, World.world?.map_stats?.year ?? 0);
		if (SuppressedFoundingByCity.TryGetValue(cityId, out int lastYear)
			&& year - lastYear < SuppressedFoundingMemoYears)
		{
			return;
		}

		SuppressedFoundingByCity[cityId] = year;
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
			XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.City
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Conflict);
	}

	private static bool IsFreshSubjugation(City city, long currentKingdomId)
	{
		long cityId = city?.data?.id ?? 0L;
		if (cityId <= 0L || !SubjugationLocksByCity.TryGetValue(cityId, out SubjugationLock state)) return false;
		int year = Math.Max(0, World.world?.map_stats?.year ?? 0);
		if (state.ExpiresYear < year)
		{
			SubjugationLocksByCity.Remove(cityId);
			return false;
		}
		return state.KingdomId <= 0L || currentKingdomId <= 0L || state.KingdomId == currentKingdomId;
	}

}
