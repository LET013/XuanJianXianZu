using System.Collections.Generic;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.QianKunDai;
using Newtonsoft.Json;

namespace XuanJianVNext.Data.Archive;

internal sealed class XjWorldArchiveData
{
	public int Version { get; set; } = 108;

	public List<XjWorldArchiveChronicleRecord> FamilyChronicles { get; set; } = new List<XjWorldArchiveChronicleRecord>();

	public List<XjWorldArchiveGongFaRecord> FamilyGongFaWarehouse { get; set; } = new List<XjWorldArchiveGongFaRecord>();

	public List<XjWorldArchiveGongFaRecord> FamilyQiuJinFaWarehouse { get; set; } = new List<XjWorldArchiveGongFaRecord>();

	public List<XjWorldArchiveCaiQiRecord> FamilyCaiQiWarehouse { get; set; } = new List<XjWorldArchiveCaiQiRecord>();

	public List<XjWorldArchiveFaBaoRecord> FamilyFaBaoWarehouse { get; set; } = new List<XjWorldArchiveFaBaoRecord>();

	public List<XjWorldArchiveLingWuRecord> FamilyLingWuWarehouse { get; set; } = new List<XjWorldArchiveLingWuRecord>();

	public List<XjWorldArchiveFaBaoRecord> LostFaBaoRecords { get; set; } = new List<XjWorldArchiveFaBaoRecord>();

	public List<XjWorldArchiveDeathRecord> FamilyDeathRecords { get; set; } = new List<XjWorldArchiveDeathRecord>();

	public List<XjWorldArchiveFamilyMemberRecord> FamilyMemberLedger { get; set; } = new List<XjWorldArchiveFamilyMemberRecord>();

	public List<XjWorldArchiveZongMenGongFaRecord> ZongMenGongFaPavilion { get; set; } = new List<XjWorldArchiveZongMenGongFaRecord>();

	public List<XjWorldArchiveZongMenGongFaRecord> ZongMenQiuJinFaPavilion { get; set; } = new List<XjWorldArchiveZongMenGongFaRecord>();

	public List<XjWorldArchiveZongMenCaiQiRecord> ZongMenCaiQiWarehouse { get; set; } = new List<XjWorldArchiveZongMenCaiQiRecord>();

	public List<XjWorldArchiveZongMenCaiQiRecord> ZongMenCaiQiFaWarehouse { get; set; } = new List<XjWorldArchiveZongMenCaiQiRecord>();

	public List<XjWorldArchiveGuoWeiRecord> GuoWeiRegistry { get; set; } = new List<XjWorldArchiveGuoWeiRecord>();

	public List<XjWorldArchiveJieLinXianRecord> JieLinXianRegistry { get; set; } = new List<XjWorldArchiveJieLinXianRecord>();

	public List<XjWorldArchiveReincarnationRecord> ReincarnationRecords { get; set; } = new List<XjWorldArchiveReincarnationRecord>();

	public List<XjDengMingShiArchiveData> DengMingShiRecords { get; set; } = new List<XjDengMingShiArchiveData>();

	public List<XjRenDanArchiveData> RenDanRecords { get; set; } = new List<XjRenDanArchiveData>();

	public List<XjGuoWeiQuanBingArchiveData> QuanBingRecords { get; set; } = new List<XjGuoWeiQuanBingArchiveData>();

	public List<XjGuoWeiQuanBingLostAuthorityArchiveData> QuanBingLostAuthorities { get; set; } = new List<XjGuoWeiQuanBingLostAuthorityArchiveData>();

	public XjWorldArchiveQuanBingStruggleState QuanBingStruggle { get; set; } = new XjWorldArchiveQuanBingStruggleState();

	public List<XjDongTianArchiveData> DongTianRecords { get; set; } = new List<XjDongTianArchiveData>();

	public List<XjWorldArchiveDaoTuCounterRecord> DaoTuCounterRecords { get; set; } = new List<XjWorldArchiveDaoTuCounterRecord>();

	public int QiYuDongTianNextSpawnYear { get; set; }

	public int QiYuDongTianLastProcessedYear { get; set; } = -1;

	public int ZongMenLastMaintenancePeriod { get; set; } = -1;

	/// <summary>
	/// 紫府后续神通三千年衰减的世界基准年。首次进入新存档时写入，
	/// 退出并重新载入同一存档不得重置。
	/// </summary>
	public int CultivationLatePenaltyBaselineYear { get; set; } = -1;

	public XjLongShuPopulationArchiveData LongShuPopulation { get; set; } = new XjLongShuPopulationArchiveData();

	public List<XjWorldArchiveFamilyIdentityRecord> FamilyIdentityIndex { get; set; } = new List<XjWorldArchiveFamilyIdentityRecord>();

	public List<XjWorldArchiveFamilyPendingRecord> FamilyPendingRecords { get; set; } = new List<XjWorldArchiveFamilyPendingRecord>();

	public List<XjQianKunDaiArchiveData> QianKunDaiRecords { get; set; } = new List<XjQianKunDaiArchiveData>();

	public List<XjSectArchiveRecord> SectRecords { get; set; } = new List<XjSectArchiveRecord>();

	public List<XjCityFamilyGovernanceArchiveRecord> CityFamilyGovernanceRecords { get; set; } = new List<XjCityFamilyGovernanceArchiveRecord>();

	public List<XjSectFamilySeatArchiveRecord> SectFamilySeatRecords { get; set; } = new List<XjSectFamilySeatArchiveRecord>();

	public List<XjSectFormationArchiveRecord> SectFormationRecords { get; set; } = new List<XjSectFormationArchiveRecord>();

	public List<XjSectWarArchiveRecord> SectWarRecords { get; set; } = new List<XjSectWarArchiveRecord>();

	public List<XjSectHostilityArchiveRecord> SectHostilityRecords { get; set; } = new List<XjSectHostilityArchiveRecord>();

	public List<XjFamilyVendettaArchiveRecord> FamilyVendettaRecords { get; set; } = new List<XjFamilyVendettaArchiveRecord>();

	public List<XjJinDanImmortalityArchiveRecord> JinDanImmortalityRecords { get; set; } = new List<XjJinDanImmortalityArchiveRecord>();

	public List<XjWorldHistoryArchiveRecord> WorldHistoryRecords { get; set; } = new List<XjWorldHistoryArchiveRecord>();

	public List<XjThreeBookArchiveRecord> PersonalBiographyRecords { get; set; } = new List<XjThreeBookArchiveRecord>();

	public List<XjThreeBookArchiveRecord> FamilyChronicleBookRecords { get; set; } = new List<XjThreeBookArchiveRecord>();

	public List<XjThreeBookArchiveRecord> SectChronicleRecords { get; set; } = new List<XjThreeBookArchiveRecord>();

	public List<XjThreeBookSourceFactRecord> PersonalBiographySourceLedger { get; set; } = new List<XjThreeBookSourceFactRecord>();

	public List<XjThreeBookSourceFactRecord> FamilyChronicleSourceLedger { get; set; } = new List<XjThreeBookSourceFactRecord>();

	public List<XjThreeBookSourceFactRecord> SectChronicleSourceLedger { get; set; } = new List<XjThreeBookSourceFactRecord>();

	public List<XjThreeBookDeferredFamilyFactRecord> DeferredFamilyChronicleFacts { get; set; } = new List<XjThreeBookDeferredFamilyFactRecord>();

	public List<XjCenturyAnnalsArchiveRecord> CenturyAnnalsRecords { get; set; } = new List<XjCenturyAnnalsArchiveRecord>();

	public XjCenturyAnnalsStateRecord CenturyAnnalsState { get; set; } = new XjCenturyAnnalsStateRecord();

	public List<XjCenturyAnnalsLedgerRecord> CenturyAnnalsLedger { get; set; } = new List<XjCenturyAnnalsLedgerRecord>();

	public List<XjAdventureRealmClaimArchiveRecord> AdventureRealmClaimRecords { get; set; } = new List<XjAdventureRealmClaimArchiveRecord>();

	public List<XjSecretRealmArchiveRecord> SecretRealmRecords { get; set; } = new List<XjSecretRealmArchiveRecord>();

	public List<XjYinSiMissionArchiveRecord> YinSiMissionRecords { get; set; } = new List<XjYinSiMissionArchiveRecord>();

	public List<XjSectLectureArchiveRecord> SectLectureRecords { get; set; } = new List<XjSectLectureArchiveRecord>();

	public XjAlchemyArchiveBundle Alchemy { get; set; } = new XjAlchemyArchiveBundle();

	public XjCraftArchiveBundle Craft { get; set; } = new XjCraftArchiveBundle();
}

internal sealed class XjWorldArchiveQuanBingStruggleState
{
	public bool Enabled { get; set; }

	public int StartYear { get; set; }

	public int EndYearExclusive { get; set; }

	public long InitiatorActorId { get; set; }

	public string InitiatorName { get; set; } = string.Empty;

	public int InitialParticipantCount { get; set; }

	public int DeadParticipantCount { get; set; }

	public bool HasExternalAuthoritySeizure { get; set; }

	public int LastProcessedYear { get; set; } = -1;
}

internal sealed class XjWorldArchiveFamilyIdentityRecord
{
	public long ActorId { get; set; }

	public string FamilyKey { get; set; } = string.Empty;

	public long RootActorId { get; set; }

	public string ReasonCode { get; set; } = string.Empty;

	public bool IsMale { get; set; }
}

internal sealed class XjWorldArchiveFamilyPendingRecord
{
	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public long ParentId1 { get; set; }

	public long ParentId2 { get; set; }

	public long FatherActorId { get; set; }

	public string Reason { get; set; } = string.Empty;
}

internal sealed class XjWorldArchiveChronicleRecord
{
	public string EventKey { get; set; } = string.Empty;

	public long FamilyStableId { get; set; }

	public long ActorId { get; set; }

	public string EventType { get; set; } = string.Empty;

	public int Year { get; set; }

	public string Text { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;

	public string Body { get; set; } = string.Empty;

	public string Source { get; set; } = string.Empty;

	public string ActorRealmSnapshot { get; set; } = string.Empty;

	public int Importance { get; set; }

	public bool IsProtected { get; set; }

	public bool RelatedToFamilyWarehouse { get; set; }

	public bool RelatedToHighGradeGongFa { get; set; }
}

internal sealed class XjWorldArchiveGongFaRecord
{
	public long FamilyStableId { get; set; }

	public long ActorId { get; set; }

	public string Name { get; set; } = string.Empty;

	public int Grade { get; set; }

	public int Year { get; set; }

	public string SourceType { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public string BoundGongFaName { get; set; } = string.Empty;

	public string MappedXianJi { get; set; } = string.Empty;

	public string BoundAuthority { get; set; } = string.Empty;
}

internal sealed class XjWorldArchiveCaiQiRecord
{
	public string FamilyKey { get; set; } = string.Empty;

	public string ResourceType { get; set; } = string.Empty;

	public string ResourceName { get; set; } = string.Empty;

	public int Count { get; set; }
}

internal sealed class XjWorldArchiveFaBaoRecord
{
	public long FamilyStableId { get; set; }

	public long SectId { get; set; }

	public string SectName { get; set; } = string.Empty;

	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public string FaBaoId { get; set; } = string.Empty;

	public string FaBaoName { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public string ClassName { get; set; } = string.Empty;

	public string Source { get; set; } = string.Empty;

	public int Year { get; set; }
}

internal sealed class XjWorldArchiveLingWuRecord
{
	public long FamilyStableId { get; set; }

	public string LingWuId { get; set; } = string.Empty;

	public int Count { get; set; }

	public int FirstAcquiredYear { get; set; }

	public int LastAcquiredYear { get; set; }

	public long LastSourceActorId { get; set; }

	public string LastSourceActorName { get; set; } = string.Empty;
}

internal sealed class XjWorldArchiveDeathRecord
{
	public long FamilyStableId { get; set; }

	public long ActorId { get; set; }

	public string Name { get; set; } = string.Empty;

	public int Year { get; set; }

	public string Realm { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public string GongFaName { get; set; } = string.Empty;

	public int GongFaGrade { get; set; }

	public string QiuJinFaName { get; set; } = string.Empty;

	public string JinXing { get; set; } = string.Empty;

	public string GuoWei { get; set; } = string.Empty;
}

internal sealed class XjWorldArchiveFamilyMemberRecord
{
	public long FamilyStableId { get; set; }

	public long ActorId { get; set; }

	public string Name { get; set; } = string.Empty;

	public int Generation { get; set; }

	public string RealmId { get; set; } = string.Empty;

	public string RealmDisplay { get; set; } = string.Empty;

	public bool IsAlive { get; set; } = true;

	public int BirthYear { get; set; }

	public int DeathYear { get; set; }

	public string Source { get; set; } = string.Empty;
}

internal sealed class XjWorldArchiveZongMenGongFaRecord
{
	public long ZongMenId { get; set; }

	public string ZongMenName { get; set; } = string.Empty;

	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public int Grade { get; set; }

	public string DaoTu { get; set; } = string.Empty;

	public string SourceType { get; set; } = string.Empty;

	public string SourceGongFaName { get; set; } = string.Empty;

	public string MappedXianJi { get; set; } = string.Empty;

	public int Year { get; set; }
}

internal sealed class XjWorldArchiveZongMenCaiQiRecord
{
	public long ZongMenId { get; set; }

	public string ZongMenName { get; set; } = string.Empty;

	public string ResourceType { get; set; } = string.Empty;

	public string ResourceId { get; set; } = string.Empty;

	public string ResourceName { get; set; } = string.Empty;

	public int Amount { get; set; }

	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public string SourcePlace { get; set; } = string.Empty;

	public int Year { get; set; }
}

internal sealed class XjWorldArchiveGuoWeiRecord
{
	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public string FamilyName { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public string JinXing { get; set; } = string.Empty;

	public string GuoWei { get; set; } = string.Empty;

	public int Year { get; set; }

	public string LifecycleStatus { get; set; } = "Active";

	public int EndedYear { get; set; }

	public string EndReason { get; set; } = string.Empty;
}

internal sealed class XjWorldArchiveJieLinXianRecord
{
	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public int Year { get; set; }
}

internal sealed class XjWorldArchiveReincarnationRecord
{
	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public string RaceKey { get; set; } = string.Empty;

	public long FamilyStableId { get; set; }

	public string RealmId { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public string GongFaName { get; set; } = string.Empty;

	public int GongFaGrade { get; set; }

	public int GongFaStage { get; set; }

	public float GongFaProgress { get; set; }

	public string JinXing { get; set; } = string.Empty;

	public string GuoWei { get; set; } = string.Empty;

	public int JinDanYiXiang { get; set; }

	public int DeathYear { get; set; }

	public string Mode { get; set; } = string.Empty;

	public string GuoWeiZhongAi { get; set; } = string.Empty;

	public long TargetActorId { get; set; }

	public string TargetActorName { get; set; } = string.Empty;

	public int AppliedYear { get; set; }

	public string Status { get; set; } = string.Empty;
}



internal sealed class XjLongShuPopulationArchiveData
{
	public bool PopulationUnlocked { get; set; }
	public bool ActiveCohortComplete { get; set; }
	public int CohortSpawnedCount { get; set; }
	public int NextFullCohortRespawnYear { get; set; }
}

internal sealed class XjWorldArchiveDaoTuCounterRecord
{
	public string Key { get; set; } = string.Empty;

	public float Value { get; set; }
}

internal static class XjWorldArchiveCodec
{
	internal static string Encode(XjWorldArchiveData data)
	{
		return JsonConvert.SerializeObject(data ?? new XjWorldArchiveData(), Formatting.None);
	}

	internal static bool TryDecode(string raw, out XjWorldArchiveData data)
	{
		data = new XjWorldArchiveData();
		if (string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}

		try
		{
			XjWorldArchiveData decoded = JsonConvert.DeserializeObject<XjWorldArchiveData>(raw);
			if (decoded == null)
			{
				return false;
			}

			decoded.FamilyChronicles ??= new System.Collections.Generic.List<XjWorldArchiveChronicleRecord>();
			decoded.FamilyIdentityIndex ??= new System.Collections.Generic.List<XjWorldArchiveFamilyIdentityRecord>();
			decoded.FamilyPendingRecords ??= new System.Collections.Generic.List<XjWorldArchiveFamilyPendingRecord>();
			decoded.FamilyGongFaWarehouse ??= new System.Collections.Generic.List<XjWorldArchiveGongFaRecord>();
			decoded.FamilyQiuJinFaWarehouse ??= new System.Collections.Generic.List<XjWorldArchiveGongFaRecord>();
			decoded.FamilyCaiQiWarehouse ??= new System.Collections.Generic.List<XjWorldArchiveCaiQiRecord>();
			decoded.FamilyFaBaoWarehouse ??= new System.Collections.Generic.List<XjWorldArchiveFaBaoRecord>();
			decoded.FamilyLingWuWarehouse ??= new System.Collections.Generic.List<XjWorldArchiveLingWuRecord>();
			decoded.LostFaBaoRecords ??= new System.Collections.Generic.List<XjWorldArchiveFaBaoRecord>();
			decoded.FamilyDeathRecords ??= new System.Collections.Generic.List<XjWorldArchiveDeathRecord>();
			decoded.FamilyMemberLedger ??= new System.Collections.Generic.List<XjWorldArchiveFamilyMemberRecord>();
			decoded.ZongMenGongFaPavilion ??= new System.Collections.Generic.List<XjWorldArchiveZongMenGongFaRecord>();
			decoded.ZongMenQiuJinFaPavilion ??= new System.Collections.Generic.List<XjWorldArchiveZongMenGongFaRecord>();
			decoded.ZongMenCaiQiWarehouse ??= new System.Collections.Generic.List<XjWorldArchiveZongMenCaiQiRecord>();
			decoded.ZongMenCaiQiFaWarehouse ??= new System.Collections.Generic.List<XjWorldArchiveZongMenCaiQiRecord>();
			decoded.GuoWeiRegistry ??= new System.Collections.Generic.List<XjWorldArchiveGuoWeiRecord>();
			decoded.JieLinXianRegistry ??= new System.Collections.Generic.List<XjWorldArchiveJieLinXianRecord>();
			decoded.ReincarnationRecords ??= new System.Collections.Generic.List<XjWorldArchiveReincarnationRecord>();
			decoded.DengMingShiRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.DengMingShi.XjDengMingShiArchiveData>();
			decoded.RenDanRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.HighRealm.XjRenDanArchiveData>();
			decoded.QuanBingRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.HighRealm.XjGuoWeiQuanBingArchiveData>();
			decoded.QuanBingLostAuthorities ??= new System.Collections.Generic.List<XuanJianVNext.Data.HighRealm.XjGuoWeiQuanBingLostAuthorityArchiveData>();
			decoded.QuanBingStruggle ??= new XjWorldArchiveQuanBingStruggleState();
			decoded.DongTianRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.DongTian.XjDongTianArchiveData>();
			decoded.DaoTuCounterRecords ??= new System.Collections.Generic.List<XjWorldArchiveDaoTuCounterRecord>();
			decoded.LongShuPopulation ??= new XjLongShuPopulationArchiveData();
			decoded.QianKunDaiRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.QianKunDai.XjQianKunDaiArchiveData>();
			decoded.SectRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.Sect.XjSectArchiveRecord>();
			decoded.CityFamilyGovernanceRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.Sect.XjCityFamilyGovernanceArchiveRecord>();
			decoded.SectFamilySeatRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.Sect.XjSectFamilySeatArchiveRecord>();
			decoded.SectFormationRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.Formation.XjSectFormationArchiveRecord>();
			decoded.SectWarRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.Sect.XjSectWarArchiveRecord>();
			decoded.SectHostilityRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.Sect.XjSectHostilityArchiveRecord>();
			decoded.FamilyVendettaRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjFamilyVendettaArchiveRecord>();
			decoded.JinDanImmortalityRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.HighRealm.XjJinDanImmortalityArchiveRecord>();
			decoded.WorldHistoryRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjWorldHistoryArchiveRecord>();
			decoded.PersonalBiographyRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjThreeBookArchiveRecord>();
			decoded.FamilyChronicleBookRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjThreeBookArchiveRecord>();
			decoded.SectChronicleRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjThreeBookArchiveRecord>();
			decoded.PersonalBiographySourceLedger ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjThreeBookSourceFactRecord>();
			decoded.FamilyChronicleSourceLedger ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjThreeBookSourceFactRecord>();
			decoded.SectChronicleSourceLedger ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjThreeBookSourceFactRecord>();
			decoded.DeferredFamilyChronicleFacts ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjThreeBookDeferredFamilyFactRecord>();
			decoded.CenturyAnnalsRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjCenturyAnnalsArchiveRecord>();
			decoded.CenturyAnnalsState ??= new XuanJianVNext.Data.History.XjCenturyAnnalsStateRecord();
			decoded.CenturyAnnalsLedger ??= new System.Collections.Generic.List<XuanJianVNext.Data.History.XjCenturyAnnalsLedgerRecord>();
			decoded.AdventureRealmClaimRecords ??= new System.Collections.Generic.List<XuanJianVNext.Data.DongTian.XjAdventureRealmClaimArchiveRecord>();
			decoded.Alchemy ??= new XuanJianVNext.Data.Alchemy.XjAlchemyArchiveBundle();
			decoded.Alchemy.Materials ??= new System.Collections.Generic.List<XuanJianVNext.Data.Alchemy.XjAlchemyMaterialArchiveData>();
			decoded.Alchemy.Recipes ??= new System.Collections.Generic.List<XuanJianVNext.Data.Alchemy.XjAlchemyRecipeArchiveData>();
			decoded.Alchemy.PillBatches ??= new System.Collections.Generic.List<XuanJianVNext.Data.Alchemy.XjAlchemyPillBatchArchiveData>();
			decoded.Alchemy.Tasks ??= new System.Collections.Generic.List<XuanJianVNext.Data.Alchemy.XjAlchemyTaskArchiveData>();
			decoded.Alchemy.Crafters ??= new System.Collections.Generic.List<XuanJianVNext.Data.Alchemy.XjAlchemyCrafterArchiveData>();
			decoded.Alchemy.RecipeProficiencies ??= new System.Collections.Generic.List<XuanJianVNext.Data.Alchemy.XjAlchemyRecipeProficiencyArchiveData>();
			decoded.Craft ??= new XuanJianVNext.Data.Craft.XjCraftArchiveBundle();
			decoded.Craft.Tasks ??= new System.Collections.Generic.List<XuanJianVNext.Data.Craft.XjCraftTaskArchiveRecord>();
			decoded.Craft.Resources ??= new System.Collections.Generic.List<XuanJianVNext.Data.Craft.XjCraftResourceArchiveRecord>();
			decoded.Craft.TalismanBatches ??= new System.Collections.Generic.List<XuanJianVNext.Data.Craft.XjTalismanBatchArchiveRecord>();
			decoded.Craft.TalismanCarries ??= new System.Collections.Generic.List<XuanJianVNext.Data.Craft.XjTalismanCarryArchiveRecord>();
			data = decoded;
			return true;
		}
		catch
		{
			data = new XjWorldArchiveData();
			return false;
		}
	}
}
