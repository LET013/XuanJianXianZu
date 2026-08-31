using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// Post-clear release guard. It validates only resettable runtime queues/indexes and
/// never reads world entities. This catches the most dangerous regression pattern in
/// the P1-P8 infrastructure: a newly added cursor or queue surviving a world change.
/// </summary>
internal static class XjRuntimeResetInvariantAudit
{
	internal static void RunAfterClear()
	{
		List<string> issues = new List<string>();
		XjSchedulerPressureSnapshot pressure = XjScheduler.CapturePerformancePressureSnapshot();
		if (pressure.AnnualIngress != 0) issues.Add("annualIngress=" + pressure.AnnualIngress);
		if (pressure.AnnualCore != 0) issues.Add("annualCore=" + pressure.AnnualCore);
		if (pressure.AnnualSecondary != 0) issues.Add("annualSecondary=" + pressure.AnnualSecondary);
		if (pressure.AnnualMaintenance != 0) issues.Add("annualMaintenance=" + pressure.AnnualMaintenance);
		if (XjActorRegistry.Count != 0) issues.Add("actorRegistry=" + XjActorRegistry.Count);
		if (XjCultivatorCache.Count != 0) issues.Add("cultivatorCache=" + XjCultivatorCache.Count);
		if (XjAptitudeTraitLifecycle.ManualXjZz6HolderCount != 0) issues.Add("manualXjZz6=" + XjAptitudeTraitLifecycle.ManualXjZz6HolderCount);
		if (XjCultivatorCandidateIndex.RuntimeMembershipCount != 0) issues.Add("cultivatorCandidates=" + XjCultivatorCandidateIndex.RuntimeMembershipCount);
		if (XjFuQiCandidateIndex.Count != 0) issues.Add("fuQiCandidates=" + XjFuQiCandidateIndex.Count);
		if (XjRuntimeActorInterestIndex.RuntimeMembershipCount != 0) issues.Add("actorInterest=" + XjRuntimeActorInterestIndex.RuntimeMembershipCount);
		if (XjFamilyMemberIndex.Shared.RuntimeRecordCount != 0) issues.Add("familyRuntimeMembers=" + XjFamilyMemberIndex.Shared.RuntimeRecordCount);
		if (XjFamilyMemberIndex.Shared.PendingRecordCount != 0) issues.Add("familyPending=" + XjFamilyMemberIndex.Shared.PendingRecordCount);
		if (XjSectAuthorityStore.MemberCount != 0) issues.Add("sectMembers=" + XjSectAuthorityStore.MemberCount);
		if (XjSectCultivatorCityIndex.TrackedActorCount != 0) issues.Add("sectCityActors=" + XjSectCultivatorCityIndex.TrackedActorCount);
		if (XjHighRealmAggregateStore.Count != 0) issues.Add("highRealmAggregate=" + XjHighRealmAggregateStore.Count);
		if (XjDaoTaiPresenceArchive.Count != 0) issues.Add("daoTaiPresence=" + XjDaoTaiPresenceArchive.Count);
		if (XjJinDanImmortalityRegistry.Count != 0) issues.Add("jinDanImmortality=" + XjJinDanImmortalityRegistry.Count);
		if (XjSecretRealmRegistry.Count != 0) issues.Add("secretRealms=" + XjSecretRealmRegistry.Count);
		if (XjQianKunDaiRegistry.Count != 0) issues.Add("qianKunDai=" + XjQianKunDaiRegistry.Count);
		if (XjAlchemyRuntimeRegistry.OpenTaskCount != 0) issues.Add("alchemyOpenTasks=" + XjAlchemyRuntimeRegistry.OpenTaskCount);
		if (XjAlchemyRuntimeRegistry.CrafterCount != 0) issues.Add("alchemyCrafters=" + XjAlchemyRuntimeRegistry.CrafterCount);
		if (XjAlchemyRuntimeRegistry.ProficiencyCount != 0) issues.Add("alchemyProficiency=" + XjAlchemyRuntimeRegistry.ProficiencyCount);
		if (XjAlchemyInventoryRegistry.RuntimeEntryCount != 0) issues.Add("alchemyInventory=" + XjAlchemyInventoryRegistry.RuntimeEntryCount);
		if (XjCraftDomainRegistry.OpenTaskCount != 0) issues.Add("craftOpenTasks=" + XjCraftDomainRegistry.OpenTaskCount);
		if (XjCraftDomainRegistry.ResourceStackCount != 0) issues.Add("craftResources=" + XjCraftDomainRegistry.ResourceStackCount);
		if (XjCraftDomainRegistry.TalismanBatchCount != 0) issues.Add("talismanBatches=" + XjCraftDomainRegistry.TalismanBatchCount);
		if (XjCraftDomainRegistry.TalismanCarryCount != 0) issues.Add("talismanCarries=" + XjCraftDomainRegistry.TalismanCarryCount);
		if (XjAnnualCultivatorSweepLane.PendingCount != 0) issues.Add("annualAudit=" + XjAnnualCultivatorSweepLane.PendingCount);
		if (XjUnifiedDetectionRuntime.PendingCount != 0) issues.Add("unifiedDetection=" + XjUnifiedDetectionRuntime.PendingCount);
		if (XjRuntimeInvariantLane.PendingCount != 0) issues.Add("runtimeInvariant=" + XjRuntimeInvariantLane.PendingCount);
		if (XjWorldLookupIndex.PendingMaintenanceCount != 0) issues.Add("worldLookupPending=" + XjWorldLookupIndex.PendingMaintenanceCount);
		if (XjWorldLookupIndex.TrackedEntityCount != 0) issues.Add("worldLookupEntities=" + XjWorldLookupIndex.TrackedEntityCount);
		if (XjXuanJianShenTongSpecials.KnownYiDuiYingMirrorCount != 0) issues.Add("yiDuiYingMirrors=" + XjXuanJianShenTongSpecials.KnownYiDuiYingMirrorCount);
		if (XjBloodlineRefreshLane.HasPending) issues.Add("bloodlineRefresh=" + XjBloodlineRefreshLane.PendingCount);
		if (XjAnnualWorldRuntimeLane.HasPending) issues.Add("annualWorld=pending");
		if (XjWorldBootstrapLane.HasPending) issues.Add("worldBootstrap=pending");
		if (XjWorldArchiveSystem.HasPendingWrite) issues.Add("archiveWrite=pending");
		if (XjWorldEventCatalog.HasOpeningPending) issues.Add("openingEvent=pending");
		if (XjHongXiaLuoXiaEvent.HasRuntimeState) issues.Add("hongxiaLuoxia=state");

		if (issues.Count == 0) return;
		Debug.LogWarning("[玄鉴][重置自检] world clear 后仍有运行态残留：" + string.Join(", ", issues));
	}
}
