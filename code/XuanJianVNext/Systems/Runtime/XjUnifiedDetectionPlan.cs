using XuanJianVNext.Core;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Localization;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// 一个世界年只采集一次的轻量检测快照。所有字段都来自现有索引、计数器或
/// 稀疏账本，不扫描 World.world.units/cities/kingdoms。
/// </summary>
internal readonly struct XjUnifiedDetectionSnapshot
{
	internal readonly int Year;
	internal readonly int CultivatorCount;
	internal readonly int SectCount;
	internal readonly int SecretRealmCount;
	internal readonly bool HasJinDan;
	internal readonly bool HasZiFuOrHigher;
	internal readonly bool HasAdventureRealmWork;
	internal readonly bool HasCraftRetentionWork;
	internal readonly bool HasFamilySupportWork;
	internal readonly bool HasGovernanceWork;
	internal readonly bool HasSectWarWork;
	internal readonly bool HasQuanBingWork;
	internal readonly bool HasYinSiWork;
	internal readonly bool HasPendingCentury;
	internal readonly bool HasPendingShiReturns;
	internal readonly bool HasDeferredInvariantAudit;

	internal XjUnifiedDetectionSnapshot(
		int year,
		int cultivatorCount,
		int sectCount,
		int secretRealmCount,
		bool hasJinDan,
		bool hasZiFuOrHigher,
		bool hasAdventureRealmWork,
		bool hasCraftRetentionWork,
		bool hasFamilySupportWork,
		bool hasGovernanceWork,
		bool hasSectWarWork,
		bool hasQuanBingWork,
		bool hasYinSiWork,
		bool hasPendingCentury,
		bool hasPendingShiReturns,
		bool hasDeferredInvariantAudit)
	{
		Year = year;
		CultivatorCount = cultivatorCount;
		SectCount = sectCount;
		SecretRealmCount = secretRealmCount;
		HasJinDan = hasJinDan;
		HasZiFuOrHigher = hasZiFuOrHigher;
		HasAdventureRealmWork = hasAdventureRealmWork;
		HasCraftRetentionWork = hasCraftRetentionWork;
		HasFamilySupportWork = hasFamilySupportWork;
		HasGovernanceWork = hasGovernanceWork;
		HasSectWarWork = hasSectWarWork;
		HasQuanBingWork = hasQuanBingWork;
		HasYinSiWork = hasYinSiWork;
		HasPendingCentury = hasPendingCentury;
		HasPendingShiReturns = hasPendingShiReturns;
		HasDeferredInvariantAudit = hasDeferredInvariantAudit;
	}
}

/// <summary>
/// 世界级年度检测的唯一资格表。年度车道只负责依次消费任务，
/// 任务是否值得启动由这里根据一次性索引快照决定。
/// </summary>
internal static class XjUnifiedDetectionPlan
{
	internal static XjUnifiedDetectionSnapshot Capture(int year)
	{
		int normalizedYear = System.Math.Max(0, year);
		int cultivatorCount = XjCultivatorCache.Count;
		int sectCount = XjSectRepository.Count;
		int secretRealmCount = XjSecretRealmRegistry.Count;
		bool hasJinDan = XjCultivatorCache.HasZhenJunOrHigher;
		bool hasZiFuOrHigher = XjCultivatorCache.HasZhenRenOrHigher;
		bool hasCraftRetention = XjCraftDomainRegistry.HasClosedTasks
			|| XjAlchemyRuntimeRegistry.HasClosedTasks;
		bool hasFamilyStages = XjCenturyAnnalsStore.ReadFamilyStageStateView()?.Count > 0;
		bool hasFamilyWarehouses = XjFamilyFaBaoWarehouse.HasFamilyEntries
			|| XjFamilyFaBaoWarehouse.HasSectEntries
			|| XjFamilyFaBaoWarehouse.HasLostEntries;
		bool hasGovernance = sectCount > 0 || XjSectCultivatorCityIndex.HasCandidateCities;
		bool hasSectWar = sectCount > 1 || XjSectWarSystem.HasActiveWars || XjFamilyVendettaRegistry.Count > 0;
		bool hasQuanBing = hasJinDan || XjQuanBingStruggleSystem.HasPendingCycle;
		bool hasYinSi = XjJinDanImmortalityRegistry.Count > 0
			|| XjYinSiExposurePursuitSystem.ActiveMissionCount > 0;
		bool hasPendingCentury = normalizedYear > 0
			&& XjCenturyAnnalsBuilder.HasPendingCompletedCentury(normalizedYear);

		return new XjUnifiedDetectionSnapshot(
			normalizedYear,
			cultivatorCount,
			sectCount,
			secretRealmCount,
			hasJinDan,
			hasZiFuOrHigher,
			XjAdventureRealmClaimSystem.HasAnnualWork,
			hasCraftRetention,
			hasFamilyStages || hasFamilyWarehouses,
			hasGovernance,
			hasSectWar,
			hasQuanBing,
			hasYinSi,
			hasPendingCentury,
			// 同一年度车道同时承载真灵重塑与摩诃周期归林。只要存在释修，
			// 就允许轻量地检查一次释修稳定ID快照；没有释修时完全跳过。
			XjReincarnation.PendingShiCount > 0 || XjCultivatorCache.GetShiIds().Count > 0,
			XjRuleInvariantAudit.HasDeferredAfterLoadAudit);
	}

	internal static bool ShouldRun(XjDetectionJob job, in XjUnifiedDetectionSnapshot snapshot)
	{
		if (snapshot.Year <= 0)
		{
			return false;
		}

		return job switch
		{
			XjDetectionJob.NativeMagicRitualGuard => XjNativeMagicRitualGuard.NeedsRegistrationRecheck,
			XjDetectionJob.AdventureRealm => snapshot.HasAdventureRealmWork,
			XjDetectionJob.SecretRealm => snapshot.SectCount > 0 || snapshot.SecretRealmCount > 0,
			XjDetectionJob.SecretRealmTraining => snapshot.SecretRealmCount > 0,
			XjDetectionJob.QuanBing => snapshot.HasJinDan || snapshot.HasQuanBingWork,
			XjDetectionJob.YinSi => snapshot.HasYinSiWork,
			XjDetectionJob.Lecture => snapshot.SectCount > 0 && snapshot.CultivatorCount > 0,
			XjDetectionJob.SectTask => snapshot.SectCount > 0,
			XjDetectionJob.CraftRetention => snapshot.HasCraftRetentionWork,
			XjDetectionJob.Governance => snapshot.HasGovernanceWork,
			XjDetectionJob.SectWar => snapshot.HasSectWarWork,
			XjDetectionJob.FamilySupport => snapshot.HasFamilySupportWork,
			XjDetectionJob.HighRealmArmyExclusion => snapshot.HasZiFuOrHigher,
			XjDetectionJob.ThreeBookChronicle => snapshot.CultivatorCount > 0 || snapshot.SectCount > 0 || snapshot.HasFamilySupportWork,
			XjDetectionJob.WorldSnapshotMaintenance => snapshot.CultivatorCount > 0,
			XjDetectionJob.CenturyDetection => snapshot.HasPendingCentury,
			XjDetectionJob.CenturyAnnals => snapshot.HasPendingCentury,
			XjDetectionJob.WorldEventCatalog => true,
			XjDetectionJob.DaoLineageMaintenance => true,
			XjDetectionJob.RuleInvariantAudit => snapshot.HasDeferredInvariantAudit,
			XjDetectionJob.GuZunMaintenance => true,
			XjDetectionJob.KingdomNameNormalization => XjKingdomNameNormalizationLane.NeedsInitialSchedule,
			XjDetectionJob.ShiReincarnationReturns => snapshot.HasPendingShiReturns,
			XjDetectionJob.MemoryMaintenance => true,
			XjDetectionJob.XianGuoPolitics => XjXianGuoSystem.HasWorldPoliticalWork,
			// Codex 自身会继续检查 dirty、窗口状态与实时节流；这里只保留年度请求。
			XjDetectionJob.Codex => true,
			_ => true
		};
	}
}
