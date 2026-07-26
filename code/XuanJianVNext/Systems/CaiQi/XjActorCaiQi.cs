using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.QianKunDai;

namespace XuanJianVNext.Systems.CaiQi;

internal readonly struct XjCaiQiSnapshot
{
	internal static XjCaiQiSnapshot Empty { get; } = new XjCaiQiSnapshot(
		false,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		0,
		false,
		0,
		0);

	internal readonly bool HasCompletedCaiQi;
	internal readonly string PlaceTypeId;
	internal readonly string BranchId;
	internal readonly string SiteName;
	internal readonly string ResultType;
	internal readonly string Status;
	internal readonly string FailureReason;
	internal readonly string ResourceId;
	internal readonly int ResourceCount;
	internal readonly bool LianQiByZaQi;
	internal readonly int LastCaiQiYear;
	internal readonly int NextCaiQiYear;

	internal XjCaiQiSnapshot(
		bool hasCompletedCaiQi,
		string placeTypeId,
		string branchId,
		string siteName,
		string resultType,
		string status,
		string failureReason,
		string resourceId,
		int resourceCount,
		bool lianQiByZaQi,
		int lastCaiQiYear,
		int nextCaiQiYear)
	{
		HasCompletedCaiQi = hasCompletedCaiQi;
		PlaceTypeId = placeTypeId ?? string.Empty;
		BranchId = branchId ?? string.Empty;
		SiteName = siteName ?? string.Empty;
		ResultType = resultType ?? string.Empty;
		Status = status ?? string.Empty;
		FailureReason = failureReason ?? string.Empty;
		ResourceId = resourceId ?? string.Empty;
		ResourceCount = resourceCount < 0 ? 0 : resourceCount;
		LianQiByZaQi = lianQiByZaQi;
		LastCaiQiYear = lastCaiQiYear;
		NextCaiQiYear = nextCaiQiYear;
	}
}

internal static class XjCaiQiStatus
{
	internal const string Pending = "pending";
	internal const string Active = "active";
	internal const string Completed = "completed";
	internal const string Cooldown = "cooldown";
	internal const string Failure = "failure";
}

internal static class XjCaiQiActorAccessor
{
	internal const int MaxGatheredQiCount = 12;
	internal static XjCaiQiSnapshot BuildSnapshot(Actor actor)
	{
		if (actor?.data == null)
		{
			return XjCaiQiSnapshot.Empty;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.CaiQiCompleted, out int completed);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CaiQiPlaceTypeId, out string placeTypeId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CaiQiBranchId, out string branchId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CaiQiSiteName, out string siteName);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CaiQiResultType, out string resultType);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CaiQiStatus, out string status);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CaiQiFailureReason, out string failureReason);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CaiQiResourceId, out string resourceId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.CaiQiResourceCount, out int resourceCount);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.LianQiByZaQi, out int lianQiByZaQi);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.LastCaiQiYear, out int lastCaiQiYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.NextCaiQiYear, out int nextCaiQiYear);

		return new XjCaiQiSnapshot(
			completed > 0,
			placeTypeId,
			branchId,
			siteName,
			resultType,
			status,
			failureReason,
			resourceId,
			resourceCount,
			lianQiByZaQi > 0,
			lastCaiQiYear,
			nextCaiQiYear);
	}

	internal static bool HasCompletedCaiQi(Actor actor)
	{
		return BuildSnapshot(actor).HasCompletedCaiQi;
	}

	internal static bool IsLianQiByZaQi(Actor actor)
	{
		return BuildSnapshot(actor).LianQiByZaQi;
	}

	internal static bool IsCaiQiResultZaQi(Actor actor)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CaiQiResultType, out string resultType);
		return string.Equals(resultType, XjCaiQiResultTypes.ZaQi, System.StringComparison.Ordinal);
	}

	internal static void MarkLianQiByZaQi(Actor actor)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.LianQiByZaQi, 1);
	}

	internal static void ClearLianQiByZaQi(Actor actor)
	{
		// 杂气锁是终身境界锁。保留兼容入口，但任何调用都不得清除标记。
	}

	internal static int GetGatheredQiCount(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.CaiQiGatheredCount, out int gatheredCount);
		if (gatheredCount > 0)
		{
			return Math.Min(MaxGatheredQiCount, gatheredCount);
		}

		// 旧存档迁移：已有个人采气资源视为已采数量，最多迁移到上限。
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.CaiQiResourceCount, out int legacyResourceCount);
		int migrated = Math.Min(MaxGatheredQiCount, Math.Max(0, legacyResourceCount));
		if (migrated > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiGatheredCount, migrated);
		}
		return migrated;
	}

	internal static bool CanGatherMoreQi(Actor actor)
	{
		return actor?.data != null && GetGatheredQiCount(actor) < MaxGatheredQiCount;
	}

	internal static int GetLastCaiQiYear(Actor actor)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.LastCaiQiYear, out int year);
		return year;
	}

	internal static int GetNextCaiQiYear(Actor actor)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.NextCaiQiYear, out int year);
		return year;
	}

	internal static bool HasConsumedForBreakthrough(Actor actor)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.CaiQiConsumedForBreakthrough, out int consumed);
		return consumed > 0;
	}

	internal static void MarkConsumedForBreakthrough(Actor actor)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiConsumedForBreakthrough, 1);
	}

	internal static bool TryConsumePersonalResourceForBreakthrough(Actor actor, in XjCaiQiSnapshot snapshot)
	{
		if (actor?.data == null
			|| HasConsumedForBreakthrough(actor)
			|| !snapshot.HasCompletedCaiQi
			|| string.IsNullOrWhiteSpace(snapshot.ResourceId))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		bool consumedFromBag = XjQianKunDaiRegistry.TryConsumeItem(
			actorId,
			snapshot.ResourceId,
			XjQianKunDaiRegistry.CategoryCaiQi,
			1);
		if (!consumedFromBag && snapshot.ResourceCount <= 0)
		{
			return false;
		}

		if (snapshot.ResourceCount > 0)
		{
			int nextCount = snapshot.ResourceCount - 1;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiResourceCount, nextCount < 0 ? 0 : nextCount);
		}

		MarkConsumedForBreakthrough(actor);
		return true;
	}

	internal static bool TryResolveWarehouseResourceId(in XjCaiQiSnapshot snapshot, out string resourceId)
	{
		resourceId = string.IsNullOrWhiteSpace(snapshot.ResourceId) ? string.Empty : snapshot.ResourceId.Trim();
		if (!string.IsNullOrWhiteSpace(resourceId))
		{
			return true;
		}

		if (string.Equals(snapshot.ResultType, XjCaiQiResultTypes.ZaQi, System.StringComparison.Ordinal))
		{
			resourceId = "zaqi";
			return true;
		}

		if (string.Equals(snapshot.ResultType, XjCaiQiResultTypes.XianTianQi, System.StringComparison.Ordinal)
			&& XjCaiQiCatalog.TryGetOldResourceIdByBranchId(snapshot.BranchId, out string oldResourceId))
		{
			resourceId = oldResourceId;
			return true;
		}

		return false;
	}

	internal static void SetNextCaiQiYear(Actor actor, int year)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.NextCaiQiYear, year);
	}

	internal static void SetLastAndNextCaiQiYear(Actor actor, int lastYear, int nextYear)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.LastCaiQiYear, lastYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.NextCaiQiYear, nextYear);
	}

	internal static void SetStatus(Actor actor, string status, string failureReason = "")
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(status))
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiStatus, status);
		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiFailureReason, failureReason ?? string.Empty);
	}

	internal static bool ShouldParticipateInCaiQi(Actor actor)
	{
		if (actor?.data == null || !CanGatherMoreQi(actor))
		{
			return false;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz);
		if (xjZz <= 0)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (string.Equals(realmId, XjRealmIds.TaiXi, System.StringComparison.Ordinal))
		{
			return true;
		}

		if (string.Equals(realmId, XjRealmIds.LianQi, System.StringComparison.Ordinal))
		{
			return IsLianQiByZaQi(actor);
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, System.StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, System.StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, System.StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTaiPlaceholder, System.StringComparison.Ordinal))
		{
			return false;
		}

		return false;
	}

	internal static bool ShouldEnqueueForCaiQi(Actor actor)
	{
		if (!ShouldParticipateInCaiQi(actor))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (string.Equals(realmId, XjRealmIds.TaiXi, System.StringComparison.Ordinal))
		{
			return !HasCompletedCaiQi(actor);
		}

		if (string.Equals(realmId, XjRealmIds.LianQi, System.StringComparison.Ordinal))
		{
			return IsLianQiByZaQi(actor);
		}

		return false;
	}

	internal static void MarkCompleted(Actor actor, string placeTypeId, string branchId, string siteName)
	{
		if (actor?.data == null || !CanGatherMoreQi(actor) || !XjCaiQiCatalog.IsBranchAllowedForPlaceType(placeTypeId, branchId))
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiCompleted, 1);
		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiPlaceTypeId, placeTypeId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiBranchId, branchId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiSiteName, siteName);
		WritePersonalResource(actor, XjCaiQiResultTypes.XianTianQi, branchId, siteName);
		PublishCaiQiCompleted(actor, XjCaiQiResultTypes.XianTianQi, branchId);
		SetStatus(actor, XjCaiQiStatus.Completed);
		WriteDaoTuFromCaiQi(actor, placeTypeId, branchId);
	}

	internal static void MarkCompleted(
		Actor actor,
		string resultType,
		string placeTypeId,
		string branchId,
		string siteName)
	{
		if (actor?.data == null || !CanGatherMoreQi(actor) || string.IsNullOrWhiteSpace(resultType))
		{
			return;
		}

		bool isZaQi = string.Equals(resultType, XjCaiQiResultTypes.ZaQi, System.StringComparison.Ordinal);
		bool isXianTianQi = string.Equals(resultType, XjCaiQiResultTypes.XianTianQi, System.StringComparison.Ordinal);
		if (!isZaQi && !isXianTianQi)
		{
			return;
		}

		if (!XjCaiQiCatalog.TryGetBranchesForPlaceType(placeTypeId, out IReadOnlyList<string> _))
		{
			return;
		}

		if (isXianTianQi && !XjCaiQiCatalog.IsBranchAllowedForPlaceType(placeTypeId, branchId))
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiCompleted, 1);
		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiResultType, resultType);
		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiPlaceTypeId, placeTypeId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiBranchId, isZaQi ? string.Empty : branchId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiSiteName, siteName);
		WritePersonalResource(actor, resultType, branchId, siteName);
		PublishCaiQiCompleted(actor, resultType, branchId);
		SetStatus(actor, XjCaiQiStatus.Completed);
		if (isXianTianQi)
		{
			WriteDaoTuFromCaiQi(actor, placeTypeId, branchId);
		}
	}

	private static void WriteDaoTuFromCaiQi(Actor actor, string placeTypeId, string branchId)
	{
		if (actor?.data == null
			|| !XjCaiQiCatalog.TryGetEntry(placeTypeId, branchId, out XjCaiQiCatalogEntry entry)
			|| string.IsNullOrWhiteSpace(entry.DisplayName))
		{
			return;
		}

		if (XjCultivationStateTransitions.TrySetDaoTu(actor, entry.DisplayName, true))
		{
			XuanJianVNext.Systems.GongFa.XjGongFaProgression.ReconcileDaoTu(actor, entry.DisplayName, "采气定途");
		}
	}

	private static void WritePersonalResource(Actor actor, string resultType, string branchId, string siteName)
	{
		if (actor?.data == null || !TryResolveResourceId(resultType, branchId, out string resourceId))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CaiQiResourceId, out string currentResourceId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.CaiQiResourceCount, out int currentCount);
		if (!string.Equals(currentResourceId, resourceId, System.StringComparison.Ordinal))
		{
			currentCount = 0;
		}

		int gatheredCount = GetGatheredQiCount(actor);
		if (gatheredCount >= MaxGatheredQiCount)
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.CaiQiResourceId, resourceId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiResourceCount, currentCount + 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiGatheredCount, gatheredCount + 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.CaiQiConsumedForBreakthrough, 0);
		string displayName = "未名先天之气";
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string resolvedName))
		{
			displayName = resolvedName;
		}
		else if (string.Equals(resourceId, "zaqi", System.StringComparison.Ordinal))
		{
			displayName = "杂气";
		}
		XjQianKunDaiRegistry.TryAddItemCount(
			((BaseSystemData)actor.data).id,
			actor.getName(),
			resourceId,
			displayName,
			XjQianKunDaiRegistry.CategoryCaiQi,
			string.IsNullOrWhiteSpace(siteName) ? "采气" : siteName.Trim(),
			string.Empty,
			1,
			XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
	}

	private static void PublishCaiQiCompleted(Actor actor, string resultType, string branchId)
	{
		if (actor == null || !TryResolveResourceId(resultType, branchId, out string resourceId))
		{
			return;
		}

		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.CaiQiCompleted(actor, resourceId, 1));
	}

	private static bool TryResolveResourceId(string resultType, string branchId, out string resourceId)
	{
		resourceId = string.Empty;
		if (string.Equals(resultType, XjCaiQiResultTypes.ZaQi, System.StringComparison.Ordinal))
		{
			resourceId = "zaqi";
			return true;
		}

		if (string.Equals(resultType, XjCaiQiResultTypes.XianTianQi, System.StringComparison.Ordinal)
			&& XjCaiQiCatalog.TryGetOldResourceIdByBranchId(branchId, out string oldResourceId))
		{
			resourceId = oldResourceId;
			return true;
		}

		return false;
	}
}

internal readonly struct XjActorCaiQiCandidate
{
	internal static XjActorCaiQiCandidate Empty { get; } = new XjActorCaiQiCandidate(
		false,
		0L,
		0L,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty);

	internal readonly bool Found;
	internal readonly long CityId;
	internal readonly long SiteId;
	internal readonly string PlaceTypeId;
	internal readonly string BranchId;
	internal readonly string SiteName;
	internal readonly string ReasonCode;

	internal XjActorCaiQiCandidate(
		bool found,
		long cityId,
		long siteId,
		string placeTypeId,
		string branchId,
		string siteName,
		string reasonCode)
	{
		Found = found;
		CityId = cityId;
		SiteId = siteId;
		PlaceTypeId = placeTypeId ?? string.Empty;
		BranchId = branchId ?? string.Empty;
		SiteName = siteName ?? string.Empty;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal readonly struct XjCaiQiResolvedResult
{
	internal static XjCaiQiResolvedResult Empty { get; } = new XjCaiQiResolvedResult(
		false,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty);

	internal readonly bool Success;
	internal readonly string ResultType;
	internal readonly string PlaceTypeId;
	internal readonly string BranchId;
	internal readonly string SiteName;
	internal readonly string ReasonCode;

	internal XjCaiQiResolvedResult(
		bool success,
		string resultType,
		string placeTypeId,
		string branchId,
		string siteName,
		string reasonCode)
	{
		Success = success;
		ResultType = resultType ?? string.Empty;
		PlaceTypeId = placeTypeId ?? string.Empty;
		BranchId = branchId ?? string.Empty;
		SiteName = siteName ?? string.Empty;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjCaiQiResultResolver
{
	internal static XjCaiQiResolvedResult Resolve(Actor actor, in XjActorCaiQiCandidate candidate, int currentYear)
	{
		if (!candidate.Found)
		{
			return new XjCaiQiResolvedResult(false, string.Empty, string.Empty, string.Empty, string.Empty, "NoCandidate");
		}

		if (!XjCaiQiActorAccessor.CanGatherMoreQi(actor))
		{
			return new XjCaiQiResolvedResult(false, string.Empty, candidate.PlaceTypeId, string.Empty, candidate.SiteName, "GatherLimitReached");
		}

		// 道主之资：必定获取自己需要的采气（先天炁）
		if (actor != null && actor.hasTrait("ChuShen8"))
		{
			return new XjCaiQiResolvedResult(
				true,
				XjCaiQiResultTypes.XianTianQi,
				candidate.PlaceTypeId,
				candidate.BranchId,
				candidate.SiteName,
				"DaoZhuAutoSuccess");
		}

		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		bool lianQiByZaQi = XjCaiQiActorAccessor.IsLianQiByZaQi(actor);
		bool matchingCaiQiFa = HasMatchingCaiQiFa(actor, candidate.PlaceTypeId, candidate.BranchId);
		bool matchingPreferredDaoTu = HasMatchingPreferredDaoTu(actor, candidate.BranchId);
		float successChance = ResolveGatherSuccessChance(actor, lianQiByZaQi, matchingCaiQiFa);
		float successRoll = XjDeterministicHash.Roll01(
			actorId,
			currentYear,
			"caiqi:" + candidate.SiteId,
			"gather_success");
		if (successRoll > successChance)
		{
			return new XjCaiQiResolvedResult(
				false,
				string.Empty,
				candidate.PlaceTypeId,
				string.Empty,
				candidate.SiteName,
				"GatherRollFailed");
		}

		string branchId = candidate.BranchId;
		if (string.IsNullOrWhiteSpace(branchId))
		{
			return new XjCaiQiResolvedResult(false, string.Empty, candidate.PlaceTypeId, string.Empty, candidate.SiteName, "NoBranch");
		}

		// 杂气炼气修士可以继续采气至个人上限，但杂气锁永不解除。
		if (lianQiByZaQi || matchingCaiQiFa || matchingPreferredDaoTu)
		{
			return new XjCaiQiResolvedResult(
				true,
				XjCaiQiResultTypes.XianTianQi,
				candidate.PlaceTypeId,
				branchId,
				candidate.SiteName,
				matchingCaiQiFa
					? "MatchingCaiQiFa"
					: matchingPreferredDaoTu
						? "PreferredDaoTuGather"
						: "ZaQiLockedGather");
		}

		long resultRoll = XjDeterministicHash.PositiveHash(actorId + candidate.SiteId + currentYear, "caiqi_result");
		if (resultRoll % 2L == 0L)
		{
			return new XjCaiQiResolvedResult(
				true,
				XjCaiQiResultTypes.ZaQi,
				candidate.PlaceTypeId,
				string.Empty,
				candidate.SiteName,
				"Ok");
		}

		return new XjCaiQiResolvedResult(
			true,
			XjCaiQiResultTypes.XianTianQi,
			candidate.PlaceTypeId,
			branchId,
			candidate.SiteName,
			"Ok");
	}

	private static float ResolveGatherSuccessChance(Actor actor, bool lianQiByZaQi, bool matchingCaiQiFa)
	{
		if (matchingCaiQiFa)
		{
			return 1f;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		aptitude = Math.Max(1, Math.Min(6, aptitude));
		// 首次采气约 33.5%～51%；杂气炼气补采约 22.5%～35%。
		return lianQiByZaQi
			? 0.20f + aptitude * 0.025f
			: 0.30f + aptitude * 0.035f;
	}

	private static bool HasMatchingCaiQiFa(Actor actor, string placeTypeId, string branchId)
	{
		XjCaiQiFaState state = XjCaiQiFaAccessor.BuildState(actor);
		return state.Found
			&& XjCaiQiCatalog.TryGetEntry(placeTypeId, branchId, out XjCaiQiCatalogEntry entry)
			&& string.Equals(state.DaoTu.Trim(), entry.DisplayName, StringComparison.Ordinal);
	}

	private static bool HasMatchingPreferredDaoTu(Actor actor, string branchId)
	{
		return !string.IsNullOrWhiteSpace(branchId)
			&& XjFamilyDaoTuRules.TryResolvePreferredDaoTu(actor, out string preferredDaoTu)
			&& XjCaiQiCatalog.TryGetEntryByDisplayName(preferredDaoTu, out XjCaiQiCatalogEntry entry)
			&& string.Equals(entry.BranchId, branchId, StringComparison.Ordinal);
	}

}

internal static class XjActorCaiQiCandidateResolver
{
	internal static XjActorCaiQiCandidate TryResolve(Actor actor)
	{
		if (actor == null)
		{
			return new XjActorCaiQiCandidate(false, 0L, 0L, string.Empty, string.Empty, string.Empty, "ActorNull");
		}

		if (actor.data == null)
		{
			return new XjActorCaiQiCandidate(false, 0L, 0L, string.Empty, string.Empty, string.Empty, "NoData");
		}

		City city = actor.city;
		if (city?.data == null || city.data.id <= 0L)
		{
			return new XjActorCaiQiCandidate(false, 0L, 0L, string.Empty, string.Empty, string.Empty, "NoCity");
		}

		long cityId = city.data.id;
		if (!XjCaiQiSiteRegistry.TryGetCitySites(cityId, out IReadOnlyList<XjCaiQiSite> sites) || sites.Count == 0)
		{
			XjCaiQiCitySiteSource.EnqueueCitySites(cityId);
			XjCaiQiSiteRegistry.TickRegistrationLane(3);
			if (!XjCaiQiSiteRegistry.TryGetCitySites(cityId, out sites) || sites.Count == 0)
			{
				XjCaiQiCityMaintenanceLane.MarkCityPending(cityId);
				return new XjActorCaiQiCandidate(false, cityId, 0L, string.Empty, string.Empty, string.Empty, "NoCitySites");
			}
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (XjFamilyDaoTuRules.TryResolveCaiQiFamilyPreference(actor, out string preferredDaoTu)
			&& XjCaiQiCatalog.TryGetEntryByDisplayName(preferredDaoTu, out XjCaiQiCatalogEntry preferredEntry))
		{
			for (int i = 0; i < sites.Count; i++)
			{
				XjCaiQiSite familySite = sites[i];
				if (string.Equals(familySite.PlaceTypeId, preferredEntry.PlaceTypeId, StringComparison.Ordinal))
				{
					return new XjActorCaiQiCandidate(true, cityId, familySite.SiteId, familySite.PlaceTypeId, preferredEntry.BranchId, familySite.SiteName, "FamilyDaoTu");
				}
			}

			if (actor.hasTrait("ChuShen8"))
			{
				string siteName = string.IsNullOrWhiteSpace(preferredEntry.DisplayName)
					? "道主采气"
					: preferredEntry.DisplayName.Trim();
				return new XjActorCaiQiCandidate(true, cityId, 0L, preferredEntry.PlaceTypeId, preferredEntry.BranchId, siteName, "DaoZhuPreferredDaoTu");
			}

			// 普通家族偏好在本城没有对应地点时回落到普通稳定哈希，
			// 不因身份链预写道途而锁死采气。道主保底仍在上方单独处理。
		}

		XjCaiQiSite site = sites[XjDeterministicHash.PositiveIndex(actorId + cityId, "site", sites.Count)];
		string branchId = XjCaiQiCatalog.GetBranchAt(site.PlaceTypeId, XjDeterministicHash.PositiveIndex(actorId + site.SiteId, "branch", int.MaxValue));
		if (string.IsNullOrWhiteSpace(branchId))
		{
			return new XjActorCaiQiCandidate(false, cityId, site.SiteId, site.PlaceTypeId, string.Empty, site.SiteName, "NoBranch");
		}

		return new XjActorCaiQiCandidate(true, cityId, site.SiteId, site.PlaceTypeId, branchId, site.SiteName, "Ok");
	}

}
