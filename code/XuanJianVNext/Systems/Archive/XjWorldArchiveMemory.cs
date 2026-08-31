using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Core;
using XuanJianVNext.Architecture.Persistence;

namespace XuanJianVNext.Systems.Archive;

internal static class XjWorldArchiveMemory
{
	internal static XjWorldArchiveData ExportFromMemory()
	{
		XjWorldArchiveData data = new XjWorldArchiveData();
		for (int index = 0; index < XjWorldArchiveSnapshotCache.OrderedSections.Count; index++)
		{
			ExportSection(data, XjWorldArchiveSnapshotCache.OrderedSections[index]);
		}
		return data;
	}

	/// <summary>
	/// Materializes one independent archive section. Every mutable collection owned
	/// by the section is replaced before export so the snapshot cache never exposes
	/// live registry collections and retention merges cannot mutate runtime state.
	/// </summary>
	internal static void ExportSection(XjWorldArchiveData data, XjWorldArchiveSection section)
	{
		if (data == null) return;
		switch (section)
		{
			case XjWorldArchiveSection.Runtime:
				data.ZongMenLastMaintenancePeriod = XjSectRuntimeLane.ExportLastCompletedPeriod();
				data.CultivationLatePenaltyBaselineYear = XjRuntimeCadence.ExportPersistentBaselineYear();
				data.LongRunEcology = XjStageFiveEcologyGuard.ExportState();
				break;

			case XjWorldArchiveSection.Family:
				data.FamilyChronicles = new System.Collections.Generic.List<XjWorldArchiveChronicleRecord>();
				data.FamilyDeathRecords = new System.Collections.Generic.List<XjWorldArchiveDeathRecord>();
				data.FamilyIdentityIndex = new System.Collections.Generic.List<XjWorldArchiveFamilyIdentityRecord>();
				data.FamilyPendingRecords = new System.Collections.Generic.List<XjWorldArchiveFamilyPendingRecord>();
				data.FamilyMemberLedger = new System.Collections.Generic.List<XjWorldArchiveFamilyMemberRecord>();
				XjFamilyChronicleMemory.Shared.ExportArchiveRecords(data.FamilyChronicles, data.FamilyDeathRecords);
				XjFamilyIdentityIndex.ExportArchiveRecords(data.FamilyIdentityIndex);
				XjFamilyMemberIndex.Shared.ExportPendingRecords(data.FamilyPendingRecords);
				XjFamilyMemberLedger.ExportArchiveRecords(data.FamilyMemberLedger);
				break;

			case XjWorldArchiveSection.Warehouses:
				data.FamilyGongFaWarehouse = new System.Collections.Generic.List<XjWorldArchiveGongFaRecord>();
				data.FamilyQiuJinFaWarehouse = new System.Collections.Generic.List<XjWorldArchiveGongFaRecord>();
				data.FamilyCaiQiWarehouse = new System.Collections.Generic.List<XjWorldArchiveCaiQiRecord>();
				data.FamilyFaBaoWarehouse = new System.Collections.Generic.List<XjWorldArchiveFaBaoRecord>();
				data.FamilyLingWuWarehouse = new System.Collections.Generic.List<XjWorldArchiveLingWuRecord>();
				data.LostFaBaoRecords = new System.Collections.Generic.List<XjWorldArchiveFaBaoRecord>();
				data.ZongMenGongFaPavilion = new System.Collections.Generic.List<XjWorldArchiveZongMenGongFaRecord>();
				data.ZongMenQiuJinFaPavilion = new System.Collections.Generic.List<XjWorldArchiveZongMenGongFaRecord>();
				data.ZongMenCaiQiWarehouse = new System.Collections.Generic.List<XjWorldArchiveZongMenCaiQiRecord>();
				data.ZongMenCaiQiFaWarehouse = new System.Collections.Generic.List<XjWorldArchiveZongMenCaiQiRecord>();
				XjFamilyGongFaWarehouse.ExportArchiveRecords(data.FamilyGongFaWarehouse, data.FamilyQiuJinFaWarehouse);
				XjFamilyCaiQiWarehouse.ExportArchiveRecords(data.FamilyCaiQiWarehouse);
				XjFamilyFaBaoWarehouse.ExportArchiveRecords(data.FamilyFaBaoWarehouse);
				XjFamilyLingWuWarehouse.ExportArchiveRecords(data.FamilyLingWuWarehouse);
				XjFamilyFaBaoWarehouse.ExportLostArchiveRecords(data.LostFaBaoRecords);
				XjSectGongFaPavilion.ExportArchiveRecords(data.ZongMenGongFaPavilion, data.ZongMenQiuJinFaPavilion);
				XjSectCaiQiWarehouse.ExportArchiveRecords(data.ZongMenCaiQiWarehouse, data.ZongMenCaiQiFaWarehouse);
				break;

			case XjWorldArchiveSection.Sect:
				data.SectRecords = new System.Collections.Generic.List<XjSectArchiveRecord>();
				data.CityFamilyGovernanceRecords = new System.Collections.Generic.List<XjCityFamilyGovernanceArchiveRecord>();
				data.SectFamilySeatRecords = new System.Collections.Generic.List<XjSectFamilySeatArchiveRecord>();
				data.SectMemberRecords = new System.Collections.Generic.List<XjSectMemberArchiveRecord>();
				data.SectFormationRecords = new System.Collections.Generic.List<XjSectFormationArchiveRecord>();
				data.SectWarRecords = new System.Collections.Generic.List<XjSectWarArchiveRecord>();
				data.SectHostilityRecords = new System.Collections.Generic.List<XjSectHostilityArchiveRecord>();
				data.SectLectureRecords = new System.Collections.Generic.List<XjSectLectureArchiveRecord>();
				XjSectRepository.ExportArchiveRecords(data.SectRecords, data.CityFamilyGovernanceRecords, data.SectFamilySeatRecords, data.SectMemberRecords);
				XjSectFormationRegistry.ExportArchiveRecords(data.SectFormationRecords);
				XjSectWarSystem.ExportArchiveRecords(data.SectWarRecords);
				XjSectWarSystem.ExportHostilityRecords(data.SectHostilityRecords);
				XjSectLectureSystem.ExportArchiveRecords(data.SectLectureRecords);
				break;

			case XjWorldArchiveSection.HighRealm:
				data.GuoWeiRegistry = new System.Collections.Generic.List<XjWorldArchiveGuoWeiRecord>();
				data.JieLinXianRegistry = new System.Collections.Generic.List<XjWorldArchiveJieLinXianRecord>();
				data.ReincarnationRecords = new System.Collections.Generic.List<XjWorldArchiveReincarnationRecord>();
				data.DengMingShiRecords = new System.Collections.Generic.List<XjDengMingShiArchiveData>();
				data.RenDanRecords = new System.Collections.Generic.List<XjRenDanArchiveData>();
				data.QuanBingRecords = new System.Collections.Generic.List<XjGuoWeiQuanBingArchiveData>();
				data.QuanBingLostAuthorities = new System.Collections.Generic.List<XjGuoWeiQuanBingLostAuthorityArchiveData>();
				data.JinDanImmortalityRecords = new System.Collections.Generic.List<XjJinDanImmortalityArchiveRecord>();
				data.GuZunRecords = new System.Collections.Generic.List<XjGuZunArchiveRecord>();
				data.DaoTuHighRealmContinuityRecords = new System.Collections.Generic.List<XjDaoTuHighRealmContinuityRecord>();
				data.YinSiMissionRecords = new System.Collections.Generic.List<XjYinSiMissionArchiveRecord>();
				XjGuoWeiRegistry.ExportArchiveRecords(data.GuoWeiRegistry);
				XjJieLinXianRegistry.ExportArchiveRecords(data.JieLinXianRegistry);
				XjReincarnation.ExportArchiveRecords(data.ReincarnationRecords);
				XjDengMingShiRegistry.ExportArchiveRecords(data.DengMingShiRecords);
				XjRenDan.ExportArchiveRecords(data.RenDanRecords);
				XjGuoWeiQuanBingRegistry.ExportArchiveRecords(data.QuanBingRecords);
				XjGuoWeiQuanBingRegistry.ExportLostAuthorityRecords(data.QuanBingLostAuthorities);
				data.QuanBingStruggle = XjQuanBingStruggleSystem.ExportState();
				XjJinDanImmortalityRegistry.ExportArchiveRecords(data.JinDanImmortalityRecords);
				XjGuZunRegistry.ExportArchiveRecords(data.GuZunRecords, data.DaoTuHighRealmContinuityRecords);
				data.GuZunLastGlobalReappearanceYear = XjGuZunRegistry.ExportLastGlobalReappearanceYear();
				data.DaoTaiMerit = XjDaoTaiMeritSystem.ExportState();
				XjYinSiExposurePursuitSystem.ExportArchiveRecords(data.YinSiMissionRecords);
				break;

			case XjWorldArchiveSection.Cultivation:
				data.DaoTuCounterRecords = new System.Collections.Generic.List<XjWorldArchiveDaoTuCounterRecord>();
				data.SwordIntentRecords = new System.Collections.Generic.List<XjSwordIntentArchiveRecord>();
				data.DaoTuManifestRecords = new System.Collections.Generic.List<XjDaoTuManifestArchiveData>();
				XjDaoTuCounterState.ExportArchiveData(data.DaoTuCounterRecords);
				XjSwordIntentRegistry.ExportArchiveRecords(data.SwordIntentRecords);
				data.FuQiSwordWorld = XjFuQiSwordWorldState.ExportState();
				XjDaoTuManifestRegistry.ExportArchiveRecords(data.DaoTuManifestRecords);
				break;

			case XjWorldArchiveSection.Realms:
				data.QiYuDongTianNextSpawnYear = XjDongTianRegistry.ExportNextSpawnYear();
				data.QiYuDongTianLastProcessedYear = XjDongTianRegistry.ExportLastProcessedYear();
				data.QiYuDongTianSpawnCount = XjDongTianRegistry.ExportSpawnCount();
				data.QiYuDongTianCadenceYears = XjDongTianRegistry.ExportCadenceYears();
				data.DongTianRecords = new System.Collections.Generic.List<XjDongTianArchiveData>();
				data.QianKunDaiRecords = new System.Collections.Generic.List<XjQianKunDaiArchiveData>();
				data.AdventureRealmClaimRecords = new System.Collections.Generic.List<XjAdventureRealmClaimArchiveRecord>();
				data.SecretRealmRecords = new System.Collections.Generic.List<XjSecretRealmArchiveRecord>();
				XjDongTianRegistry.ExportArchiveRecords(data.DongTianRecords);
				XjQianKunDaiRegistry.ExportArchiveRecords(data.QianKunDaiRecords);
				XjAdventureRealmClaimSystem.ExportArchiveRecords(data.AdventureRealmClaimRecords);
				data.EventDongTian = XjEventDongTianSystem.ExportState();
				XjSecretRealmRegistry.ExportArchiveRecords(data.SecretRealmRecords);
				break;

			case XjWorldArchiveSection.Alchemy:
				data.Alchemy = new XjAlchemyArchiveBundle();
				XjAlchemyInventoryRegistry.ExportArchiveRecords(data.Alchemy.Materials, data.Alchemy.Recipes, data.Alchemy.PillBatches);
				XjAlchemyRuntimeRegistry.ExportArchiveRecords(data.Alchemy.Tasks, data.Alchemy.Crafters, data.Alchemy.RecipeProficiencies);
				break;

			case XjWorldArchiveSection.Craft:
				data.Craft = new XjCraftArchiveBundle();
				XjCraftDomainRegistry.ExportArchiveRecords(data.Craft);
				break;

			case XjWorldArchiveSection.History:
				data.WorldHistoryRecords = new System.Collections.Generic.List<XjWorldHistoryArchiveRecord>();
				data.FamilyVendettaRecords = new System.Collections.Generic.List<XjFamilyVendettaArchiveRecord>();
				data.PersonalBiographyRecords = new System.Collections.Generic.List<XjThreeBookArchiveRecord>();
				data.FamilyChronicleBookRecords = new System.Collections.Generic.List<XjThreeBookArchiveRecord>();
				data.SectChronicleRecords = new System.Collections.Generic.List<XjThreeBookArchiveRecord>();
				data.PersonalBiographySourceLedger = new System.Collections.Generic.List<XjThreeBookSourceFactRecord>();
				data.FamilyChronicleSourceLedger = new System.Collections.Generic.List<XjThreeBookSourceFactRecord>();
				data.SectChronicleSourceLedger = new System.Collections.Generic.List<XjThreeBookSourceFactRecord>();
				data.DeferredFamilyChronicleFacts = new System.Collections.Generic.List<XjThreeBookDeferredFamilyFactRecord>();
				data.CenturyAnnalsRecords = new System.Collections.Generic.List<XjCenturyAnnalsArchiveRecord>();
				data.CenturyAnnalsLedger = new System.Collections.Generic.List<XjCenturyAnnalsLedgerRecord>();
				XjWorldHistoryStore.ExportArchiveRecords(data.WorldHistoryRecords);
				XjFamilyVendettaRegistry.ExportArchiveRecords(data.FamilyVendettaRecords);
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
				break;

			case XjWorldArchiveSection.Events:
				data.LongShuPopulation = XjLongShuSystem.ExportPopulationState();
				data.OpeningLifespanEvent = XjZiXuanBorrowedLifespanEvent.ExportState();
				data.YuanZhaoKongZhengEvent = XjYuanZhaoKongZhengEvent.ExportState();
				data.ShiOpeningPrologue = XjShiOpeningPrologueSystem.ExportState();
				break;

			case XjWorldArchiveSection.Modules:
				data.ModuleDocuments = new System.Collections.Generic.List<XjModuleArchiveDocument>();
				XjModuleArchiveRegistry.ExportDocuments(data.ModuleDocuments);
				break;
		}
	}

	internal static void ImportToMemory(XjWorldArchiveData data)
	{
		XjWorldArchiveData archive = data ?? new XjWorldArchiveData();
		archive.Alchemy ??= new XjAlchemyArchiveBundle();
		archive.Craft ??= new XjCraftArchiveBundle();
		archive.LongRunEcology ??= new XjLongRunEcologyArchiveData();
		archive.FuQiSwordWorld ??= new XuanJianVNext.Data.Cultivation.XjFuQiSwordWorldArchiveData();
		archive.ModuleDocuments ??= new System.Collections.Generic.List<XuanJianVNext.Data.Archive.XjModuleArchiveDocument>();
		archive.DaoTuManifestRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.Cultivation.XjDaoTuManifestArchiveData>();
		archive.SectMemberRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.Sect.XjSectMemberArchiveRecord>();
		archive.OpeningLifespanEvent ??= new XuanJianVNext.Data.Events.XjOpeningLifespanEventArchiveData();
		archive.TaiYuanJinXianEvent ??= new XuanJianVNext.Data.Events.XjTaiYuanJinXianEventArchiveData();
		archive.YuanZhaoKongZhengEvent ??= new XuanJianVNext.Data.Events.XjYuanZhaoKongZhengEventArchiveData();
		archive.ShiOpeningPrologue ??= new XuanJianVNext.Data.Events.XjShiOpeningPrologueArchiveData();
		archive.EventDongTian ??= new XuanJianVNext.Data.DongTian.XjEventDongTianWorldArchiveData();
		archive.DaoTaiMerit ??= new XuanJianVNext.Data.HighRealm.XjDaoTaiMeritWorldArchiveData();
		XjFamilyChronicleMemory.Shared.ImportArchiveRecords(archive.FamilyChronicles);
		XjFamilyIdentityIndex.ImportArchiveRecords(archive.FamilyIdentityIndex);
		XjFamilyMemberIndex.Shared.ImportPendingRecords(archive.FamilyPendingRecords);
		XjFamilyMemberLedger.ImportArchiveRecords(archive.FamilyMemberLedger);
		XjFamilyGongFaWarehouse.ImportArchiveRecords(archive.FamilyGongFaWarehouse, archive.FamilyQiuJinFaWarehouse);
		XjFamilyCaiQiWarehouse.ImportArchiveRecords(archive.FamilyCaiQiWarehouse);
		XjFamilyFaBaoWarehouse.ImportArchiveRecords(archive.FamilyFaBaoWarehouse);
		XjFamilyLingWuWarehouse.ImportArchiveRecords(archive.FamilyLingWuWarehouse);
		XjFamilyFaBaoWarehouse.ImportLostArchiveRecords(archive.LostFaBaoRecords);
		XjSectGongFaPavilion.ImportArchiveRecords(archive.ZongMenGongFaPavilion, archive.ZongMenQiuJinFaPavilion);
		XjSectCaiQiWarehouse.ImportArchiveRecords(archive.ZongMenCaiQiWarehouse, archive.ZongMenCaiQiFaWarehouse);
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
		XjSectRuntimeLane.ImportLastCompletedPeriod(archive.ZongMenLastMaintenancePeriod);
		XjRuntimeCadence.ImportPersistentBaselineYear(archive.CultivationLatePenaltyBaselineYear);
		XjDongTianRegistry.ImportRuntimeYears(
			archive.QiYuDongTianNextSpawnYear,
			archive.QiYuDongTianLastProcessedYear,
			archive.QiYuDongTianSpawnCount,
			archive.QiYuDongTianCadenceYears);
		XjLongShuSystem.ImportPopulationState(archive.LongShuPopulation);
		XjDongTianRegistry.ImportArchiveRecords(archive.DongTianRecords);
		XjQianKunDaiRegistry.ImportArchiveRecords(archive.QianKunDaiRecords);
		XjAlchemyInventoryRegistry.ImportArchiveRecords(
			archive.Alchemy.Materials,
			archive.Alchemy.Recipes,
			archive.Alchemy.PillBatches);
		XjSectRepository.ImportArchiveRecords(
			archive.SectRecords,
			archive.CityFamilyGovernanceRecords,
			archive.SectFamilySeatRecords,
			archive.SectMemberRecords);
		XjCraftDomainRegistry.ImportArchiveRecords(archive.Craft);
		XjSectFormationRegistry.ImportArchiveRecords(archive.SectFormationRecords);
		XjSectWarSystem.ImportArchiveRecords(archive.SectWarRecords);
		XjSectWarSystem.ImportHostilityRecords(archive.SectHostilityRecords);
		XjFamilyVendettaRegistry.ImportArchiveRecords(archive.FamilyVendettaRecords);
		XjSectFormationRegistry.ReconcileTaskLinksAfterLoad();
		XjJinDanImmortalityRegistry.ImportArchiveRecords(archive.JinDanImmortalityRecords);
		XjGuZunRegistry.ImportArchiveRecords(
			archive.GuZunRecords,
			archive.DaoTuHighRealmContinuityRecords,
			archive.GuZunLastGlobalReappearanceYear);
		XjDaoTaiMeritSystem.ImportState(archive.DaoTaiMerit, archive.GuoWeiRegistry);
		XjWorldHistoryStore.ImportArchiveRecords(archive.WorldHistoryRecords);
		XjSwordIntentRegistry.ImportArchiveRecords(archive.SwordIntentRecords);
		XjDaoTuManifestRegistry.ImportArchiveRecords(archive.DaoTuManifestRecords);
		XjFuQiSwordWorldState.ImportState(archive.FuQiSwordWorld);
		XjModuleArchiveRegistry.ImportDocuments(archive.ModuleDocuments);
		// 旧档没有 world.fruit-position-domain 文档；在模块文档导入之后，
		// 用既有果位账本补齐动态位置定义。新档已有定义时为零成本空操作。
		XjFruitPositionWorldState.BackfillImportedPositions(XjGuoWeiRegistry.ReadAllEntries());
		XjTaiYinHiddenFruitSystem.ReconcileAfterLoad(System.Math.Max(1, XjYearTracker.CurrentYear));
		XjDaoTuManifestRegistry.ReconcileCaiQiUnlocksAfterLoad();
		XjPersonalBiographyStore.ImportArchiveRecords(archive.PersonalBiographyRecords, archive.PersonalBiographySourceLedger);
		XjFamilyChronicleBookStore.ImportArchiveRecords(archive.FamilyChronicleBookRecords, archive.FamilyChronicleSourceLedger);
		XjSectChronicleStore.ImportArchiveRecords(archive.SectChronicleRecords, archive.SectChronicleSourceLedger);
		XjThreeBookDeferredFamilyFacts.ImportArchiveRecords(archive.DeferredFamilyChronicleFacts);
		XjCenturyAnnalsStore.ImportArchiveRecords(archive.CenturyAnnalsRecords, archive.CenturyAnnalsState, archive.CenturyAnnalsLedger);
		XjAdventureRealmClaimSystem.ImportArchiveRecords(archive.AdventureRealmClaimRecords);
		XjZiXuanBorrowedLifespanEvent.ImportState(archive.OpeningLifespanEvent);
		XjYuanZhaoKongZhengEvent.ImportState(archive.YuanZhaoKongZhengEvent);
		XjShiOpeningPrologueSystem.ImportState(archive.ShiOpeningPrologue);
		XjEventDongTianSystem.ImportState(archive.EventDongTian);
		XjSecretRealmRegistry.ImportArchiveRecords(archive.SecretRealmRecords);
		XjYinSiExposurePursuitSystem.ImportArchiveRecords(archive.YinSiMissionRecords);
		XjSectLectureSystem.ImportArchiveRecords(archive.SectLectureRecords);
		XjSecretRealmRegistry.ReconcileTaskLinksAfterLoad();
		XjAlchemyRuntimeRegistry.ImportArchiveRecords(
			archive.Alchemy.Tasks,
			archive.Alchemy.Crafters,
			archive.Alchemy.RecipeProficiencies);
		XjStageFiveEcologyGuard.ImportState(archive.LongRunEcology);
		XjFuQiSwordWorldState.SyncLineageTraits();
		XjRuleInvariantAudit.RunAfterLoad();
	}
}
