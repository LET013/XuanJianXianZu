using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Visual;
using XuanJianVNext.UI.Rank;
using XuanJianVNext.UI.ZongMen;

namespace XuanJianVNext.Core;

internal static class XjCacheRegistry
{
    private static readonly object _clearLock = new();

    internal static void ClearAll()
    {
        lock (_clearLock)
        {
            XjRuntimeCadence.Clear();
            XjBroadcastSystem.Clear();
            XjCaiQiCityMaintenanceLane.Clear();
            XjCaiQiCityOrderIndex.Clear();
            XjCaiQiSiteRegistry.Clear();
            XjFamilyIdentityIndex.Clear();
            XjFamilyMemberIndex.Shared.Clear();
            XjFamilyMemberLedger.Clear();
            XjFamilySurnamePolicy.ClearRuntimeCache();
            XjFamilyCaiQiWarehouse.Clear();
            XjFamilyGongFaWarehouse.Clear();
            XjFamilyFaBaoWarehouse.Clear();
            XjFamilyWarehouseWriter.Clear();
            XjZongMenGongFaPavilion.Clear();
            XjZongMenCaiQiWarehouse.Clear();
		XjZongMenKnowledgeWriter.Clear();
		XjZongMenCultivatorCityIndex.Clear();
		XjZongMenCityData.ClearRuntimeCache();
		XjActorAggroBridge.Clear();
		XjCombatTracker.Clear();
		XuanJianVNext.Systems.WeaponArt.XjWeaponArtCombatTracker.Clear();
		XjVanillaDeathGuard.Clear();
		XjRealmLifespanService.Clear();
			XjYinSiTraitLifecycle.ClearRuntimeQueue();
            XjGuoWeiRegistry.Clear();
            XjJieLinXianRegistry.Clear();
            XjXianJiAccessor.ClearRuntimeCache();
            XjReincarnation.Clear();
            XjDengMingShiRegistry.Clear();
            XjRenDan.Clear();
            XjGuoWeiQuanBingRegistry.Clear();
            XjQuanBingStruggleSystem.Clear();
            XjDongTianRegistry.Clear();
            XjDongTianEntitySystem.Clear();
            XjQianKunDaiRegistry.Clear();
            XjAlchemyInventoryRegistry.Clear();
            XjAlchemyRuntimeRegistry.Clear();
            XjDaoTuCounterState.Clear();
            XjCombatEffectPool.ClearFrameCache();
            XjJinDanDaoSpellRuntime.Clear();
            XjJinDanDaoSpellCooldown.Clear();
            XjDomainSkillRuntime.Clear();
            XjJinDanCombatApi.ClearTerrainEffects();
            XjHuoDeFireBirdSummonSystem.Clear();
            XjSanYinBaoYiXuanLunSystem.Clear();
            XjLongShuDongTianSystem.Clear();
            XjLongShuSystem.Clear();
            XjEraRuntime.Clear();
            XjVisibleActorRenderLane.Clear();
            XjZiFuBackEffectVisualSystem.Clear();
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
            XjWorldArchiveSystem.Clear();
            XjCultivatorCache.Clear();
            XjScheduler.Clear();
        }
    }
}
