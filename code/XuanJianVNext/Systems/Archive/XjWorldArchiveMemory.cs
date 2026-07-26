using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Archive;

internal static class XjWorldArchiveMemory
{
	internal static XjWorldArchiveData ExportFromMemory()
	{
		XjWorldArchiveData data = new XjWorldArchiveData();
		XjFamilyChronicleMemory.Shared.ExportArchiveRecords(data.FamilyChronicles, data.FamilyDeathRecords);
		XjFamilyIdentityIndex.ExportArchiveRecords(data.FamilyIdentityIndex);
		XjFamilyMemberIndex.Shared.ExportPendingRecords(data.FamilyPendingRecords);
		XjFamilyMemberLedger.ExportArchiveRecords(data.FamilyMemberLedger);
		XjFamilyGongFaWarehouse.ExportArchiveRecords(data.FamilyGongFaWarehouse, data.FamilyQiuJinFaWarehouse);
		XjFamilyCaiQiWarehouse.ExportArchiveRecords(data.FamilyCaiQiWarehouse);
		XjFamilyFaBaoWarehouse.ExportArchiveRecords(data.FamilyFaBaoWarehouse);
		XjFamilyLingWuWarehouse.ExportArchiveRecords(data.FamilyLingWuWarehouse);
		XjFamilyFaBaoWarehouse.ExportLostArchiveRecords(data.LostFaBaoRecords);
		XjZongMenGongFaPavilion.ExportArchiveRecords(data.ZongMenGongFaPavilion, data.ZongMenQiuJinFaPavilion);
		XjZongMenCaiQiWarehouse.ExportArchiveRecords(data.ZongMenCaiQiWarehouse, data.ZongMenCaiQiFaWarehouse);
		XjGuoWeiRegistry.ExportArchiveRecords(data.GuoWeiRegistry);
		XjJieLinXianRegistry.ExportArchiveRecords(data.JieLinXianRegistry);
		XjReincarnation.ExportArchiveRecords(data.ReincarnationRecords);
		XjDengMingShiRegistry.ExportArchiveRecords(data.DengMingShiRecords);
		XjRenDan.ExportArchiveRecords(data.RenDanRecords);
		XjGuoWeiQuanBingRegistry.ExportArchiveRecords(data.QuanBingRecords);
		XjGuoWeiQuanBingRegistry.ExportLostAuthorityRecords(data.QuanBingLostAuthorities);
		data.QuanBingStruggle = XjQuanBingStruggleSystem.ExportState();
		XjDaoTuCounterState.ExportArchiveData(data.DaoTuCounterRecords);
		data.ZongMenLastMaintenancePeriod = XjZongMenRuntimeLane.ExportLastCompletedPeriod();
		data.CultivationLatePenaltyBaselineYear = XjRuntimeCadence.ExportPersistentBaselineYear();
		data.QiYuDongTianNextSpawnYear = XjDongTianRegistry.ExportNextSpawnYear();
		data.QiYuDongTianLastProcessedYear = XjDongTianRegistry.ExportLastProcessedYear();
		data.LongShuPopulation = XjLongShuSystem.ExportPopulationState();
		XjDongTianRegistry.ExportArchiveRecords(data.DongTianRecords);
		XjQianKunDaiRegistry.ExportArchiveRecords(data.QianKunDaiRecords);
		XjAlchemyInventoryRegistry.ExportArchiveRecords(
			data.Alchemy.Materials,
			data.Alchemy.Recipes,
			data.Alchemy.PillBatches);
		XjAlchemyRuntimeRegistry.ExportArchiveRecords(
			data.Alchemy.Tasks,
			data.Alchemy.Crafters,
			data.Alchemy.RecipeProficiencies);
		XjSectRepository.ExportArchiveRecords(data.SectRecords, data.CityFamilyGovernanceRecords, data.SectFamilySeatRecords);
		XjSectFormationRegistry.ExportArchiveRecords(data.SectFormationRecords);
		XjSectWarSystem.ExportArchiveRecords(data.SectWarRecords);
		XjSectWarSystem.ExportHostilityRecords(data.SectHostilityRecords);
		XjFamilyVendettaRegistry.ExportArchiveRecords(data.FamilyVendettaRecords);
		XjJinDanImmortalityRegistry.ExportArchiveRecords(data.JinDanImmortalityRecords);
		XjWorldHistoryStore.ExportArchiveRecords(data.WorldHistoryRecords);
		XjPersonalBiographyStore.ExportArchiveRecords(data.PersonalBiographyRecords);
		XjFamilyChronicleBookStore.ExportArchiveRecords(data.FamilyChronicleBookRecords);
		XjSectChronicleStore.ExportArchiveRecords(data.SectChronicleRecords);
		XjPersonalBiographyStore.ExportSourceLedger(data.PersonalBiographySourceLedger);
		XjFamilyChronicleBookStore.ExportSourceLedger(data.FamilyChronicleSourceLedger);
		XjSectChronicleStore.ExportSourceLedger(data.SectChronicleSourceLedger);
		XjThreeBookDeferredFamilyFacts.ExportArchiveRecords(data.DeferredFamilyChronicleFacts);
		XjCenturyAnnalsStore.ExportArchiveRecords(data.CenturyAnnalsRecords);
		data.CenturyAnnalsState = XjCenturyAnnalsStore.ExportState();
		XjCenturyAnnalsStore.ExportLedgerRecords(data.CenturyAnnalsLedger);
		XjAdventureRealmClaimSystem.ExportArchiveRecords(data.AdventureRealmClaimRecords);
		XjSecretRealmRegistry.ExportArchiveRecords(data.SecretRealmRecords);
		XjYinSiExposurePursuitSystem.ExportArchiveRecords(data.YinSiMissionRecords);
		XjSectLectureSystem.ExportArchiveRecords(data.SectLectureRecords);
		XjCraftDomainRegistry.ExportArchiveRecords(data.Craft);
		return data;
	}

	internal static void ImportToMemory(XjWorldArchiveData data)
	{
		XjWorldArchiveData archive = data ?? new XjWorldArchiveData();
		archive.Alchemy ??= new XjAlchemyArchiveBundle();
		archive.Craft ??= new XjCraftArchiveBundle();
		XjFamilyChronicleMemory.Shared.ImportArchiveRecords(archive.FamilyChronicles);
		XjFamilyIdentityIndex.ImportArchiveRecords(archive.FamilyIdentityIndex);
		XjFamilyMemberIndex.Shared.ImportPendingRecords(archive.FamilyPendingRecords);
		XjFamilyMemberLedger.ImportArchiveRecords(archive.FamilyMemberLedger);
		XjFamilyGongFaWarehouse.ImportArchiveRecords(archive.FamilyGongFaWarehouse, archive.FamilyQiuJinFaWarehouse);
		XjFamilyCaiQiWarehouse.ImportArchiveRecords(archive.FamilyCaiQiWarehouse);
		XjFamilyFaBaoWarehouse.ImportArchiveRecords(archive.FamilyFaBaoWarehouse);
		XjFamilyLingWuWarehouse.ImportArchiveRecords(archive.FamilyLingWuWarehouse);
		XjFamilyFaBaoWarehouse.ImportLostArchiveRecords(archive.LostFaBaoRecords);
		XjZongMenGongFaPavilion.ImportArchiveRecords(archive.ZongMenGongFaPavilion, archive.ZongMenQiuJinFaPavilion);
		XjZongMenCaiQiWarehouse.ImportArchiveRecords(archive.ZongMenCaiQiWarehouse, archive.ZongMenCaiQiFaWarehouse);
		XjGuoWeiRegistry.ImportArchiveRecords(archive.GuoWeiRegistry);
		XjJieLinXianRegistry.ImportArchiveRecords(archive.JieLinXianRegistry);
		XjReincarnation.ImportArchiveRecords(archive.ReincarnationRecords);
		XjDengMingShiRegistry.ImportArchiveRecords(archive.DengMingShiRecords);
		XjRenDan.ImportArchiveRecords(archive.RenDanRecords);
		XjGuoWeiQuanBingRegistry.ImportArchiveRecords(archive.QuanBingRecords);
		XjGuoWeiQuanBingRegistry.ImportLostAuthorityRecords(archive.QuanBingLostAuthorities);
		XjQuanBingStruggleSystem.ImportState(archive.QuanBingStruggle);
		XjGuoWeiRegistry.BackfillHistoricalRecords(
			archive.QuanBingRecords,
			archive.ReincarnationRecords,
			archive.FamilyDeathRecords);
		XjGuoWeiQuanBingRegistry.BackfillHistoricalRecords(archive.GuoWeiRegistry);
		XjDaoTuCounterState.ImportArchiveData(archive.DaoTuCounterRecords);
		XjZongMenRuntimeLane.ImportLastCompletedPeriod(archive.ZongMenLastMaintenancePeriod);
		XjRuntimeCadence.ImportPersistentBaselineYear(archive.CultivationLatePenaltyBaselineYear);
		XjDongTianRegistry.ImportRuntimeYears(
			archive.QiYuDongTianNextSpawnYear,
			archive.QiYuDongTianLastProcessedYear);
		XjLongShuSystem.ImportPopulationState(archive.LongShuPopulation);
		XjDongTianRegistry.ImportArchiveRecords(archive.DongTianRecords);
		XjQianKunDaiRegistry.ImportArchiveRecords(archive.QianKunDaiRecords);
		XjAlchemyInventoryRegistry.ImportArchiveRecords(
			archive.Alchemy.Materials,
			archive.Alchemy.Recipes,
			archive.Alchemy.PillBatches);
		XjSectRepository.ImportArchiveRecords(archive.SectRecords, archive.CityFamilyGovernanceRecords, archive.SectFamilySeatRecords);
		XjCraftDomainRegistry.ImportArchiveRecords(archive.Craft);
		XjSectFormationRegistry.ImportArchiveRecords(archive.SectFormationRecords);
		XjSectWarSystem.ImportArchiveRecords(archive.SectWarRecords);
		XjSectWarSystem.ImportHostilityRecords(archive.SectHostilityRecords);
		XjFamilyVendettaRegistry.ImportArchiveRecords(archive.FamilyVendettaRecords);
		XjSectFormationRegistry.ReconcileTaskLinksAfterLoad();
		XjJinDanImmortalityRegistry.ImportArchiveRecords(archive.JinDanImmortalityRecords);
		XjWorldHistoryStore.ImportArchiveRecords(archive.WorldHistoryRecords);
		XjPersonalBiographyStore.ImportArchiveRecords(archive.PersonalBiographyRecords, archive.PersonalBiographySourceLedger);
		XjFamilyChronicleBookStore.ImportArchiveRecords(archive.FamilyChronicleBookRecords, archive.FamilyChronicleSourceLedger);
		XjSectChronicleStore.ImportArchiveRecords(archive.SectChronicleRecords, archive.SectChronicleSourceLedger);
		XjThreeBookDeferredFamilyFacts.ImportArchiveRecords(archive.DeferredFamilyChronicleFacts);
		XjCenturyAnnalsStore.ImportArchiveRecords(archive.CenturyAnnalsRecords, archive.CenturyAnnalsState, archive.CenturyAnnalsLedger);
		XjAdventureRealmClaimSystem.ImportArchiveRecords(archive.AdventureRealmClaimRecords);
		XjSecretRealmRegistry.ImportArchiveRecords(archive.SecretRealmRecords);
		XjYinSiExposurePursuitSystem.ImportArchiveRecords(archive.YinSiMissionRecords);
		XjSectLectureSystem.ImportArchiveRecords(archive.SectLectureRecords);
		XjSecretRealmRegistry.ReconcileTaskLinksAfterLoad();
		XjAlchemyRuntimeRegistry.ImportArchiveRecords(
			archive.Alchemy.Tasks,
			archive.Alchemy.Crafters,
			archive.Alchemy.RecipeProficiencies);
	}
}
