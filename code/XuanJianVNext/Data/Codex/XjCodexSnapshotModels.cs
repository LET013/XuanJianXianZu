using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.Codex;

[Flags]
internal enum XjCodexDirtyFlags
{
	None = 0,
	World = 1 << 0,
	Sect = 1 << 1,
	Family = 1 << 2,
	City = 1 << 3,
	Formation = 1 << 4,
	SecretRealm = 1 << 5,
	Craft = 1 << 6,
	JinDan = 1 << 7,
	AdventureRealm = 1 << 8,
	Conflict = 1 << 9,
	History = 1 << 10,
	SectResource = 1 << 11,
	CenturyAnnals = 1 << 12,
	All = int.MaxValue
}

internal sealed class XjCodexSnapshot
{
	internal static readonly XjCodexSnapshot Empty = new XjCodexSnapshot();
	internal int Version;
	internal int WorldYear;
	internal string PublishedAt = string.Empty;
	internal int KingdomCount;
	internal int CityCount;
	internal int CultivatorCount;
	internal int SectCount;
	internal int FamilyCount;
	internal int JinDanCount;
	internal int KnownByYinSiCount;
	internal int CraftActorCount;
	internal int OpenCraftTaskCount;
	internal int TalismanStockCount;
	internal int OperationalFormationCount;
	internal int BrokenFormationCount;
	internal int OpenAdventureRealmCount;
	internal int ContestedAdventureRealmCount;
	internal int FudiCount;
	internal int DongtianCount;
	internal int ActiveYinSiMissionCount;
	internal int LectureCount;
	internal int HistoryCapacity;
	internal int PersonalBiographyCapacity;
	internal int FamilyChronicleCapacity;
	internal int SectChronicleCapacity;
	internal int CenturyAnnalsCapacity;
	internal int CenturyAnnalsStartYear;
	internal int SectResourceCount;
	internal IReadOnlyList<XjCodexSectItem> Sects = Array.Empty<XjCodexSectItem>();
	internal IReadOnlyList<XjCodexSectResourceItem> SectResources = Array.Empty<XjCodexSectResourceItem>();
	internal IReadOnlyList<XjCodexFamilyItem> Families = Array.Empty<XjCodexFamilyItem>();
	internal IReadOnlyList<XjCodexCityItem> Cities = Array.Empty<XjCodexCityItem>();
	internal IReadOnlyList<XjCodexFormationItem> Formations = Array.Empty<XjCodexFormationItem>();
	internal IReadOnlyList<XjCodexSecretRealmItem> SecretRealms = Array.Empty<XjCodexSecretRealmItem>();
	internal IReadOnlyList<XjCodexCraftItem> CraftActors = Array.Empty<XjCodexCraftItem>();
	internal IReadOnlyList<XjCodexJinDanItem> JinDan = Array.Empty<XjCodexJinDanItem>();
	internal IReadOnlyList<XjCodexAdventureRealmItem> AdventureRealms = Array.Empty<XjCodexAdventureRealmItem>();
	internal IReadOnlyList<XjCodexLectureItem> Lectures = Array.Empty<XjCodexLectureItem>();
	internal IReadOnlyList<XjCodexConflictItem> Conflicts = Array.Empty<XjCodexConflictItem>();
	internal IReadOnlyList<XjCodexHistoryItem> History = Array.Empty<XjCodexHistoryItem>();
	internal IReadOnlyList<XjCodexHistorySubjectItem> HistoryActors = Array.Empty<XjCodexHistorySubjectItem>();
	internal IReadOnlyList<XjCodexHistorySubjectItem> HistoryFamilies = Array.Empty<XjCodexHistorySubjectItem>();
	internal IReadOnlyList<XjCodexHistorySubjectItem> HistorySects = Array.Empty<XjCodexHistorySubjectItem>();
	internal IReadOnlyList<XjCodexHistoryItem> PersonalBiographies = Array.Empty<XjCodexHistoryItem>();
	internal IReadOnlyList<XjCodexHistorySubjectItem> BiographyActors = Array.Empty<XjCodexHistorySubjectItem>();
	internal IReadOnlyList<XjCodexHistoryItem> FamilyChronicles = Array.Empty<XjCodexHistoryItem>();
	internal IReadOnlyList<XjCodexHistorySubjectItem> ChronicleFamilies = Array.Empty<XjCodexHistorySubjectItem>();
	internal IReadOnlyList<XjCodexHistoryItem> SectChronicles = Array.Empty<XjCodexHistoryItem>();
	internal IReadOnlyList<XjCodexHistorySubjectItem> ChronicleSects = Array.Empty<XjCodexHistorySubjectItem>();
	internal XjCodexThreeBookHealth ThreeBookHealth = new XjCodexThreeBookHealth();
	internal IReadOnlyList<XjCodexCenturyAnnalsItem> CenturyAnnals = Array.Empty<XjCodexCenturyAnnalsItem>();
}

internal sealed class XjCodexSectItem
{
	internal long SectId;
	internal long KingdomId;
	internal string Name = string.Empty;
	internal string PredecessorKingdomName = string.Empty;
	internal string Status = string.Empty;
	internal int FoundingYear;
	internal long FounderActorId;
	internal string FounderName = string.Empty;
	internal long SovereignActorId;
	internal string SovereignName = string.Empty;
	internal long GoverningFamilyId;
	internal string GoverningFamilyName = string.Empty;
	internal long PillarFamilyId;
	internal string PillarFamilyName = string.Empty;
	internal string StrongFamilySummary = string.Empty;
	internal IReadOnlyList<XjCodexSectHouseItem> StrongFamilies = Array.Empty<XjCodexSectHouseItem>();
	internal int DissidentFamilyCount;
	internal string DissidentFamilySummary = string.Empty;
	internal int LastMandateYear;
	internal long CapitalCityId;
	internal string CapitalCityName = string.Empty;
	internal int CityCount;
	internal int FamilyCount;
	internal int CultivatorCount;
	internal int ZiFuCount;
	internal int JinDanCount;
	internal int PeakCount;
	internal int PeakCapacity;
	internal string PeakSummary = string.Empty;
	internal string PeakRosterSummary = string.Empty;
	internal IReadOnlyList<XjCodexSectPeakItem> Peaks = Array.Empty<XjCodexSectPeakItem>();
	internal int FormationGrade;
	internal string FormationName = string.Empty;
	internal int FormationCurrentDurability;
	internal int FormationMaxDurability;
	internal string FormationState = string.Empty;
	internal long FormationLeadActorId;
	internal string FormationLeadName = string.Empty;
	internal string SecretRealmState = string.Empty;
	internal int GongFaCount;
	internal int QiuJinFaCount;
	internal int CaiQiCount;
	internal int CaiQiFaCount;
	internal int CraftStockCount;
	internal int PillStockCount;
	internal int ArtifactStockCount;
	internal int OwnTreasureCount;
	internal string OwnTreasureSummary = string.Empty;
	internal int TalismanStockCount;
	internal int FormationStockCount;
	internal string ResourceSummary = string.Empty;
	internal int LastTaskYear;
	internal string LastTaskSummary = string.Empty;
	internal IReadOnlyList<XjCodexSectRelationItem> Relations = Array.Empty<XjCodexSectRelationItem>();
}

internal sealed class XjCodexSectRelationItem
{
	internal long OtherSectId;
	internal string OtherSectName = string.Empty;
	internal string Relation = string.Empty;
	internal int Hostility;
	internal string Reason = string.Empty;
}

internal sealed class XjCodexSectHouseItem
{
	internal long FamilyId;
	internal string FamilyName = string.Empty;
	internal int QualifiedMemberCount;
	internal int StrongestRealmOrder;
	internal string StrongestRealm = string.Empty;
	internal float StrengthScore;
	internal float VoiceScore;
}

internal sealed class XjCodexSectPeakItem
{
	internal int PeakId;
	internal string PeakName = string.Empty;
	internal long PeakMasterActorId;
	internal string PeakMasterName = string.Empty;
	internal int MemberCount;
	internal int ZiFuCount;
	internal int JinDanCount;
	internal List<XjCodexSectPeakMemberItem> Members = new List<XjCodexSectPeakMemberItem>();
}

internal sealed class XjCodexSectPeakMemberItem
{
	internal long ActorId;
	internal string Name = string.Empty;
	internal string Realm = string.Empty;
	internal string DaoTu = string.Empty;
	internal string Role = string.Empty;
	internal int RealmOrder;
	internal string Aptitude = string.Empty;
	internal int Age;
	internal string CraftSummary = string.Empty;
	internal int XianJiCount;
	internal string KeySummary = string.Empty;
}

internal sealed class XjCodexSectResourceItem
{
	internal long SectId;
	internal string SectName = string.Empty;
	internal string ResourceKind = string.Empty;
	internal string Name = string.Empty;
	internal int Grade;
	internal string DaoTu = string.Empty;
	internal int Amount;
	internal string Source = string.Empty;
	internal string SourceActorName = string.Empty;
	internal int Year;
	internal string Detail = string.Empty;
}

internal sealed class XjCodexFamilyItem
{
	internal long FamilyId;
	internal string Name = string.Empty;
	internal int AliveCount;
	internal int TotalCount;
	internal int CultivatorCount;
	internal int ZiFuCount;
	internal int JinDanCount;
	internal int HighestRealmOrder;
	internal string HighestRealm = string.Empty;
	internal int HistoricalHighestRealmOrder;
	internal string HistoricalHighestRealm = string.Empty;
	internal int HistoricalCultivatorCount;
	internal string LineageState = string.Empty;
	internal int QualifiedHighRealmCount;
	internal int QualifiedHighRealmScore;
	internal int StrongestQualifiedRealmOrder;
	internal string StrongestQualifiedRealm = string.Empty;
	internal long SectId;
	internal string SectName = string.Empty;
	internal long PrimaryCityId;
	internal bool IsMigrationBranch;
	internal long SourceFamilyId;
	internal string SourceFamilyName = string.Empty;
	internal long OriginCityId;
	internal string OriginCityName = string.Empty;
	internal int GoverningCityCount;
	internal string GoverningCities = string.Empty;
	internal string Representative = string.Empty;
	internal string AncestorName = string.Empty;
	internal string FounderTitle = string.Empty;
	internal string ClanLeaderName = string.Empty;
	internal string HeirName = string.Empty;
	internal string CityOfficeSummary = string.Empty;
	internal float InfluenceScore;
	internal string BiographySummary = string.Empty;
	internal long RepresentativeActorId;
	internal long AncestorActorId;
	internal long ClanLeaderActorId;
	internal long HeirActorId;
	internal float ContributionScore;
	internal float CraftScore;
	internal float VoiceScore;
	internal string VoiceTier = string.Empty;
	internal float SupplyDebt;
	internal string Responsibility = string.Empty;
	internal float PrivilegeHeat;
	internal int PrivilegeIncidentCount;
	internal int LastPrivilegeIncidentYear;
	internal string PrivilegeSummary = string.Empty;
	internal string ActiveAspiration = string.Empty;
	internal int AspirationSinceYear;
	internal string SectRelation = string.Empty;
	internal long PillarActorId;
	internal string PillarName = string.Empty;
	internal string PillarRealm = string.Empty;
	internal long SupportedActorId;
	internal string SupportedActorName = string.Empty;
	internal string SupportPurpose = string.Empty;
	internal int SupportedSinceYear;
	internal string FamilyTreasureSummary = string.Empty;
	internal List<XjCodexFamilyMemberItem> Members = new List<XjCodexFamilyMemberItem>();
}

internal sealed class XjCodexFamilyMemberItem
{
	internal long ActorId;
	internal string Name = string.Empty;
	internal string Realm = string.Empty;
	internal string DaoTu = string.Empty;
	internal string Role = string.Empty;
	internal int RealmOrder;
	internal string Aptitude = string.Empty;
	internal int Age;
	internal int RemainingLife;
	internal float HuiGuang;
	internal float MingShu;
	internal int XianJiCount;
	internal int GradeFiveGongFaCount;
	internal bool HasQiuJinFa;
	internal string CraftSummary = string.Empty;
	internal string KeySummary = string.Empty;
}

internal sealed class XjCodexCityItem
{
	internal long CityId;
	internal string Name = string.Empty;
	internal long KingdomId;
	internal string KingdomName = string.Empty;
	internal long SectId;
	internal string SectName = string.Empty;
	internal long GoverningFamilyId;
	internal string GoverningFamilyName = string.Empty;
	internal string GovernanceState = string.Empty;
	internal long ChallengerFamilyId;
	internal string ChallengerFamilyName = string.Empty;
	internal int ChallengeConsecutiveYears;
	internal float IncumbentControlScore;
	internal float ChallengerControlScore;
	internal int CultivatorCount;
	internal bool FormationProtected;
	internal string FormationName = string.Empty;
	internal int FormationGrade;
	internal int FormationCurrentDurability;
	internal int FormationMaxDurability;
	internal string FormationState = string.Empty;
	internal long FormationLeadActorId;
	internal string FormationLeadName = string.Empty;
}

internal sealed class XjCodexFormationItem
{
	internal long SectId;
	internal string SectName = string.Empty;
	internal int Grade;
	internal string BuildState = string.Empty;
	internal int CurrentDurability;
	internal int MaxDurability;
	internal float OccupationSpeedMultiplier;
	internal float CompletionGate;
	internal long LeadFormationMasterId;
	internal string LeadFormationMasterName = string.Empty;
	internal int LastDamageYear;
	internal int LastRepairYear;
	internal long BuildTaskId;
	internal long RepairTaskId;
}

internal sealed class XjCodexSecretRealmItem
{
	internal long SectId;
	internal string SectName = string.Empty;
	internal long RealmId;
	internal string DisplayName = string.Empty;
	internal string Stage = string.Empty;
	internal bool ConstructionMethodKnown;
	internal string ConstructionMethodSource = string.Empty;
	internal int Stability;
	internal int XuanTaoIntegrity;
	internal int Capacity;
	internal long EntranceCityId;
	internal string EntranceCityName = string.Empty;
	internal string LeadFormationMasterName = string.Empty;
	internal long SittingJinDanActorId;
	internal string SittingJinDanName = string.Empty;
	internal int JinDanCount;
	internal IReadOnlyList<XjCodexSecretRealmJinDanItem> JinDanActors = Array.Empty<XjCodexSecretRealmJinDanItem>();
	internal long ActiveTaskId;
	internal int StageDueYear;
	internal bool EntranceOpen;
	internal string Summary = string.Empty;
}

internal sealed class XjCodexSecretRealmJinDanItem
{
	internal long ActorId;
	internal string Name = string.Empty;
	internal string Realm = string.Empty;
	internal string DaoTu = string.Empty;
}

internal sealed class XjCodexCraftItem
{
	internal long ActorId;
	internal string ActorName = string.Empty;
	internal string ProfessionId = string.Empty;
	internal string ProfessionName = string.Empty;
	internal string CraftRank = string.Empty;
	internal string Realm = string.Empty;
	internal string CityName = string.Empty;
	internal string SectName = string.Empty;
	internal string TaskKind = string.Empty;
	internal string TaskStatus = string.Empty;
	internal string TaskBlueprint = string.Empty;
	internal int TaskDueYear;
}

internal sealed class XjCodexJinDanItem
{
	internal long ActorId;
	internal string Name = string.Empty;
	internal int Age;
	internal int ActivatedYear;
	internal string Realm = string.Empty;
	internal string DaoTu = string.Empty;
	internal long SectId;
	internal string SectName = string.Empty;
	internal string FamilyName = string.Empty;
	internal float YinSiExposure;
	internal bool YinSiKnown;
	internal string YinSiState = string.Empty;
	internal string YinSiReason = string.Empty;
	internal int PursuitCount;
	internal int LastKnownYear;
	internal long ActiveMissionId;
	internal string MissionStage = string.Empty;
	internal int NextPursuitYear;
	internal string SecretRealmName = string.Empty;
	internal bool Sheltered;
}

internal sealed class XjCodexAdventureRealmItem
{
	internal string RecordId = string.Empty;
	internal string Name = string.Empty;
	internal string DaoTuGroup = string.Empty;
	internal string State = string.Empty;
	internal bool IsOpen;
	internal int OpenUntilYear;
	internal int RemainingExploreCount;
	internal long AnchorCityId;
	internal int AnchorTileX;
	internal int AnchorTileY;
	internal string CityName = string.Empty;
	internal string ClaimSectName = string.Empty;
	internal string ClaimKingdomName = string.Empty;
	internal string DiscovererName = string.Empty;
	internal string ContestingSectName = string.Empty;
	internal int ContestDueYear;
	internal string ContestSummary = string.Empty;
}


internal sealed class XjCodexLectureItem
{
	internal long LectureId;
	internal long SectId;
	internal string SectName = string.Empty;
	internal string LectureType = string.Empty;
	internal string LecturerName = string.Empty;
	internal int Year;
	internal bool HeldInsideSecretRealm;
	internal int AttendeeCount;
	internal string Summary = string.Empty;
}

internal sealed class XjCodexConflictItem
{
	internal string Severity = string.Empty;
	internal string Kind = string.Empty;
	internal string Title = string.Empty;
	internal string Reason = string.Empty;
	internal string NextStep = string.Empty;
	internal string Observer = string.Empty;
	internal long SectId;
	internal long CityId;
	internal long FamilyId;
}

internal sealed class XjCodexHistoryItem
{
	internal string EventId = string.Empty;
	internal long SortSequence;
	internal int Year;
	internal string EventType = string.Empty;
	internal string Category = string.Empty;
	internal string Title = string.Empty;
	internal string BookTag = string.Empty;
	internal string Body = string.Empty;
	internal string IconId = string.Empty;
	internal int Importance;
	internal bool IsProtected;
	internal int VisibilityFlags;
	internal string Result = string.Empty;
	internal long CauseEventId;
	internal string CenturyStatus = string.Empty;
	internal long ActorId;
	internal string ActorName = string.Empty;
	internal long RelatedActorId;
	internal string RelatedActorName = string.Empty;
	internal string Location = string.Empty;
	internal bool HasLocation;
	internal float LocationX;
	internal float LocationY;
	internal long SectId;
	internal string SectName = string.Empty;
	internal long RelatedSectId;
	internal string RelatedSectName = string.Empty;
	internal long FamilyId;
	internal string FamilyName = string.Empty;
	internal long RelatedFamilyId;
	internal string RelatedFamilyName = string.Empty;
	internal long CityId;
}

internal sealed class XjCodexHistorySubjectItem
{
	internal long SubjectId;
	internal string Name = string.Empty;
	internal string SearchText = string.Empty;
	internal int EventCount;
	internal int LatestYear;
	internal long LatestSortSequence;
	internal int HighestImportance;
	internal string LatestTitle = string.Empty;
	internal bool IsAliveOrActive;
}

internal sealed class XjCodexThreeBookStoreHealth
{
	internal string Name = string.Empty;
	internal int Count;
	internal int Capacity;
	internal int LedgerCount;
	internal int LedgerCapacity;
	internal int EventTypeCount;
	internal int ExpectedEventTypeCount;
	internal string EventTypes = string.Empty;
	internal string MissingEventTypes = string.Empty;
	internal int LastEventYear;
	internal long Attempts;
	internal long Accepted;
	internal long Invalid;
	internal long DuplicateSource;
	internal long DuplicateSignature;
	internal long Trimmed;
	internal long Imported;
	internal long LedgerPruned;
	internal long SoftOverflow;
}

internal sealed class XjCodexThreeBookHealth
{
	internal bool IsHealthy = true;
	internal string Summary = "三书接线正常";
	internal int DeferredFamilyFacts;
	internal long DeferredResolved;
	internal long DeferredExpired;
	internal long DeferredDropped;
	internal long NativePartnerChecks;
	internal long NativePartnerDataKeyHits;
	internal long NativePartnerReflectionHits;
	internal long NativePartnerMisses;
	internal XjCodexThreeBookStoreHealth Personal = new XjCodexThreeBookStoreHealth();
	internal XjCodexThreeBookStoreHealth Family = new XjCodexThreeBookStoreHealth();
	internal XjCodexThreeBookStoreHealth Sect = new XjCodexThreeBookStoreHealth();
}

internal sealed class XjCodexCenturyAnnalsItem
{
	internal int CenturyId;
	internal int StartYear;
	internal int EndYear;
	internal int GeneratedYear;
	internal bool IsCompleteCycle;
	internal string Title = string.Empty;
	internal string WorldSummary = string.Empty;
	internal int FamilyCount;
	internal int SectCount;
	internal int ActorCount;
	internal IReadOnlyList<XjCodexCenturyFamilyItem> Families = Array.Empty<XjCodexCenturyFamilyItem>();
	internal IReadOnlyList<XjCodexCenturySummaryItem> Sects = Array.Empty<XjCodexCenturySummaryItem>();
	internal IReadOnlyList<XjCodexCenturySummaryItem> RealmStatistics = Array.Empty<XjCodexCenturySummaryItem>();
	internal IReadOnlyList<XjCodexCenturySummaryItem> DaoSummaries = Array.Empty<XjCodexCenturySummaryItem>();
	internal IReadOnlyList<XjCodexCenturySummaryItem> ArtifactSummaries = Array.Empty<XjCodexCenturySummaryItem>();
	internal IReadOnlyList<XjCodexCenturyActorItem> Actors = Array.Empty<XjCodexCenturyActorItem>();
}

internal sealed class XjCodexCenturyFamilyItem
{
	internal long FamilyId;
	internal string FamilyName = string.Empty;
	internal string PreviousStage = string.Empty;
	internal string CurrentStage = string.Empty;
	internal bool IsExtinct;
	internal int AliveCount;
	internal int CultivatorCount;
	internal int ZiFuCount;
	internal int JinDanCount;
	internal string HighestRealm = string.Empty;
	internal long SectId;
	internal string SectName = string.Empty;
	internal string VoiceTier = string.Empty;
	internal float InfluenceScore;
	internal int GoverningCityCount;
	internal string RepresentativeActorName = string.Empty;
	internal string StageReason = string.Empty;
	internal string Aspiration = string.Empty;
	internal string AspirationStatus = string.Empty;
	internal int AspirationSinceYear;
	internal string AspirationSummary = string.Empty;
	internal string SectRelation = string.Empty;
	internal long SupportedActorId;
	internal string SupportedActorName = string.Empty;
	internal string SupportPurpose = string.Empty;
	internal int SupportedSinceYear;
	internal long PillarActorId;
	internal string PillarActorName = string.Empty;
	internal string PillarRealm = string.Empty;
	internal string FamilyTreasureSummary = string.Empty;
}

internal sealed class XjCodexCenturyActorItem
{
	internal long ActorId;
	internal string ActorName = string.Empty;
	internal long FamilyId;
	internal string FamilyName = string.Empty;
	internal long SectId;
	internal string SectName = string.Empty;
	internal string Realm = string.Empty;
	internal int RealmOrder;
	internal string Reason = string.Empty;
}

internal sealed class XjCodexCenturySummaryItem
{
	internal string Key = string.Empty;
	internal string Name = string.Empty;
	internal int Score;
	internal string Trend = string.Empty;
	internal string Summary = string.Empty;
}
