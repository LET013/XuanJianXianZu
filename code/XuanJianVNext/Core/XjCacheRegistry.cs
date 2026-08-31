using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Architecture.Bootstrap;
using XuanJianVNext.Architecture.Presentation;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Visual;
using XuanJianVNext.Systems.Rank;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Core;

internal static class XjCacheRegistry
{
    private static readonly object _clearLock = new();

    internal static void ClearAll()
    {
        lock (_clearLock)
        {
            XjRuntimeCadence.Clear();
            XjNativeReflectionInterop.ClearRuntimeCache();
		XjNativeActorAssetInterop.ClearRuntimeCache();
		XjNativeSelectionInterop.ClearRuntimeCache();
		XjNativeCityCaptureInterop.ClearRuntimeCache();
		XjNativeActorDataSanitizerInterop.ClearRuntimeCache();
            XjBroadcastSystem.Clear();
            XjCaiQiCityMaintenanceLane.Clear();
            XjCaiQiCityOrderIndex.Clear();
            XjCaiQiSiteRegistry.Clear();
		XjActorAggroBridge.Clear();
		XjCombatTracker.Clear();
		XuanJianVNext.Systems.WeaponArt.XjWeaponArtCombatTracker.Clear();
		XuanJianVNext.Systems.WeaponArt.XjSwordIntentRegistry.Clear();
		XjFuQiSwordWorldState.Clear();
		XjDaoTuManifestRegistry.Clear();
		XjVanillaDeathGuard.Clear();
		XjRealmLifespanService.Clear();
		XjHighRealmMovement.ClearRuntime();
		XjZiXuanBorrowedLifespanEvent.Clear();
		XjYuanZhaoKongZhengEvent.Clear();
			XjYinSiTraitLifecycle.ClearRuntimeQueue();
            XjGuoWeiRegistry.Clear();
            XjFruitPositionWorldState.Clear();
            XjDaoLineageStateRegistry.Clear();
            XjJieLinXianRegistry.Clear();
			XjYuYiXianRegistry.Clear();
			XjXuanJianShenTongSpecials.ClearRuntime();
			XjRealmFieldExecutor.Clear();
			XjJiShengYuSystem.Clear();
            XjXianJiAccessor.ClearRuntimeCache();
            XjReincarnation.Clear();
            XjDengMingShiRegistry.Clear();
            XjRenDan.Clear();
            XjGuoWeiQuanBingRegistry.Clear();
            XjQuanBingStruggleSystem.Clear();
            XjGuZunRegistry.Clear();
            XjDaoTaiMeritSystem.Clear();
            XjDaoTaiPresenceArchive.Clear();
            XjSemanticDiagnostics.Clear();
            XjExceptionDiagnostics.Clear();
            XjDeathArbitrationPipeline.Clear();
            XjRuleInvariantAudit.Clear();
            XjDongTianRegistry.Clear();
            XjEventDongTianSystem.Clear();
            XjDongTianEntitySystem.Clear();
            XjQianKunDaiRegistry.Clear();
            XjAlchemyInventoryRegistry.Clear();
            XjAlchemyRuntimeRegistry.Clear();
            XjDaoTuCounterState.Clear();
            XjCombatEffectPool.ClearFrameCache();
            XjAttributedFreezeRegistry.Clear();
            XjJinDanDaoSpellRuntime.Clear();
            XjJinDanDaoSpellCooldown.Clear();
            XjDomainSkillRuntime.Clear();
            XjJinDanCombatApi.ClearTerrainEffects();
            XjHuoDeFireBirdSummonSystem.Clear();
            XjSanYinBaoYiXuanLunSystem.Clear();
            XjLongShuDongTianSystem.Clear();
            XjLongShuSystem.Clear();
            XjEraRuntime.Clear();
            XjWorldFortuneSystem.Clear();
            XjChronicleChapterComposer.Clear();
            XjVisibleActorRenderLane.Clear();
            XjJinDanHaloVisualSystem.Clear();
            XjDaoTaiGazeVisualSystem.Clear();
            XjFamilyChronicleMemory.Shared.Clear();
            XjPersonalBiographyStore.Clear();
            XjFamilyChronicleBookStore.Clear();
            XjSectChronicleStore.Clear();
            XjThreeBookDeferredFamilyFacts.Clear();
            XjThreeBookDiagnostics.Clear();
            XjFamilyVendettaRegistry.Clear();
            XjCenturyAnnalsStore.Clear();
            XjDeathArchiveQueue.Clear();
            XjRankSortSystem.ClearCache();
            XjRankReadModel.InvalidateCache();
            XjWorldArchiveSystem.Clear();
            XjCultivatorCache.Clear();
            XjAptitudeTraitLifecycle.ClearRuntimeIndex();
            XjBloodlineRefreshLane.Clear();
            XjActorStateRevisionStore.Clear();
            XjRelationEntityRevisionStore.Clear();
            XjPresentationHooks.ClearActorInfoProjectionCache();

            // Domain feature state (Family/Sect/Shi/Doctrine/XianGuo) is torn down
            // through the module catalog below. Keep this table limited to shared
            // runtime infrastructure and legacy domains that do not yet own a module.
            XjFeatureModuleCatalog.ClearRegisteredRuntimeState();
            XjCultivationEligibility.ClearRuntimeCache();
            XjFuQiMethodRules.ClearRuntimeCache();
            XjFaBaoAcquisition.ClearRuntimeCache();
            XjFaBaoBonusService.Clear();
            XjCombatHotPathCache.Clear();
            XjTrueDamageSystem.ClearRuntime();
            XjHighRealmArmyExclusion.Clear();
            XjProjectileGuard.ClearRuntimeCache();
            XjAdventureRealmClaimSystem.Clear();
            XjSecretRealmRegistry.Clear();
            XjYinSiExposurePursuitSystem.Clear();
            XjCraftActorIndex.Clear();
            XjCraftDomainRegistry.Clear();
            XjJinDanImmortalityRegistry.Clear();
            XjCodexSnapshotPublisher.Clear();
            XjWorldHistoryStore.Clear();
            XjNativeMagicRitualGuard.Clear();
            XjRuntimeDiagnostics.Clear();
            XjScheduler.Clear();
            XjRuntimeResetInvariantAudit.RunAfterClear();
        }
    }
}
