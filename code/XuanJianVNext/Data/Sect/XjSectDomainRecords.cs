using System.Collections.Generic;

namespace XuanJianVNext.Data.Sect;

internal static class XjSectDomainSchema
{
	internal const int CurrentVersion = 6;
}

internal static class XjSectPeakIds
{
	internal const int Main = 0;
	internal const int Supreme = 1;
	internal const int FirstRegular = 2;
}

internal static class XjSectStatus
{
	internal const string SectRegime = "SectRegime";
	internal const string FudiSect = "FudiSect";
	internal const string DongtianSect = "DongtianSect";
	internal const string LandlessSect = "LandlessSect";
	internal const string Extinct = "Extinct";
}

internal static class XjSectWarStatus
{
	internal const string Active = "Active";
	internal const string Resolved = "Resolved";
	internal const string Cancelled = "Cancelled";
}


internal sealed class XjSectHostilityArchiveRecord
{
	public int SchemaVersion { get; set; } = XjSectDomainSchema.CurrentVersion;
	public long LeftSectId { get; set; }
	public long RightSectId { get; set; }
	public int Hostility { get; set; }
	public int LastConflictYear { get; set; }
	public int LastHostilityYear { get; set; }
	public int LastWarYear { get; set; }
	public long LastAggressorSectId { get; set; }
	public string LastReason { get; set; } = string.Empty;
}

internal sealed class XjSectWarArchiveRecord
{
	public int SchemaVersion { get; set; } = XjSectDomainSchema.CurrentVersion;
	public long WarId { get; set; }
	public long AttackerSectId { get; set; }
	public string AttackerSectName { get; set; } = string.Empty;
	public long DefenderSectId { get; set; }
	public string DefenderSectName { get; set; } = string.Empty;
	public string Status { get; set; } = XjSectWarStatus.Active;
	public string WarGoal { get; set; } = string.Empty;
	public string Reason { get; set; } = string.Empty;
	public int StartYear { get; set; }
	public int LastProcessedYear { get; set; }
	public int EndYear { get; set; }
	public int DurationYears { get; set; }
	public int AttackerDamageDealt { get; set; }
	public int DefenderDamageDealt { get; set; }
	public float AttackerScore { get; set; }
	public float DefenderScore { get; set; }
	public string ResultSummary { get; set; } = string.Empty;
	public int AttackerFormationWarningMask { get; set; }
	public int DefenderFormationWarningMask { get; set; }
	public List<long> AttackerParticipantIds { get; set; } = new List<long>();
	public List<long> DefenderParticipantIds { get; set; } = new List<long>();
}

internal sealed class XjSectArchiveRecord
{
	public int SchemaVersion { get; set; } = XjSectDomainSchema.CurrentVersion;
	public long SectId { get; set; }
	// WorldBox 原生国家外壳或前身记录，只用于显示和兼容原生窗口，不参与宗门归属判断。
	public long KingdomId { get; set; }
	// 创宗前的原生国家名称快照。宗门真实归属始终以 CityId/ActorId/FamilyId -> SectId 为准。
	public string PredecessorKingdomName { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Status { get; set; } = XjSectStatus.SectRegime;
	public int FoundingYear { get; set; }
	public long FounderActorId { get; set; }
	public string FounderName { get; set; } = string.Empty;
	public long FounderFamilyId { get; set; }
	public long DominantFamilyId { get; set; }
	public long SovereignActorId { get; set; }
	public int SovereignGeneration { get; set; } = 1;
	public int LastRecruitYear { get; set; } = -1;
	public long CapitalCityId { get; set; }
	public long FormationId { get; set; }
	public long SecretRealmId { get; set; }
	public int LastTaskYear { get; set; }
	// 1.0宗主法旨仅保存最近一次真实执行年份；不保存法旨队列或政治进度。
	public int LastMandateYear { get; set; }
	public string LastTaskSummary { get; set; } = string.Empty;
	public int LastSecretRealmTrainingYear { get; set; }
	public string LastSecretRealmTrainingSummary { get; set; } = string.Empty;
	// 宗门兴衰以五年结算维护。0为覆灭边缘，100为极盛；不做逐帧或逐人口扫描。
	public int ProsperityValue { get; set; } = 20;
	// 历史峰值用于区分“尚在成长”与“由盛转衰”，避免低兴衰宗门永远只显示静态档位。
	public int PeakProsperityValue { get; set; } = 20;
	public int LastProsperityYear { get; set; }
	public int ConsecutiveCriticalPeriods { get; set; }
	public string ProsperityTier { get; set; } = "初建";
	public string ProsperitySummary { get; set; } = string.Empty;
	public List<long> CityIds { get; set; } = new List<long>();
	public List<long> FamilyIds { get; set; } = new List<long>();
	public List<XjSectPeakArchiveRecord> Peaks { get; set; } = new List<XjSectPeakArchiveRecord>();
}

internal sealed class XjSectPeakArchiveRecord
{
	public int SchemaVersion { get; set; } = XjSectDomainSchema.CurrentVersion;
	public long SectId { get; set; }
	public int PeakId { get; set; }
	public string PeakName { get; set; } = string.Empty;
	public long PeakMasterActorId { get; set; }
	public string PeakMasterName { get; set; } = string.Empty;
	public long FounderActorId { get; set; }
	public int FoundedYear { get; set; }
	public int LastConfirmedYear { get; set; }
}

internal static class XjSectFamilySeatState
{
	internal const string Formal = "Formal";
	internal const string Attached = "Attached";
	internal const string Suspended = "Suspended";
}

internal static class XjSectVoiceTier
{
	internal const string Ordinary = "普通家族";
	internal const string Elder = "长老家族";
	internal const string Major = "宗门重族";
	internal const string First = "宗门首族";
	internal const string Dominant = "把持宗门";
}

internal static class XjCityFamilyGovernanceState
{
	internal const string Prominent = "Prominent";
	internal const string Stable = "Stable";
	internal const string Challenged = "Challenged";
	internal const string Reassigned = "Reassigned";
}

internal sealed class XjSectFamilySeatArchiveRecord
{
	public int SchemaVersion { get; set; } = XjSectDomainSchema.CurrentVersion;
	public long SectId { get; set; }
	public long FamilyId { get; set; }
	public string State { get; set; } = XjSectFamilySeatState.Formal;
	public int JoinedYear { get; set; }
	public int GoverningCityCount { get; set; }
	public float StrengthScore { get; set; }
	public float TerritoryScore { get; set; }
	public float ContributionScore { get; set; }
	public float CraftScore { get; set; }
	public float OfficeHistoryScore { get; set; }
	public float VoiceScore { get; set; }
	public string VoiceTier { get; set; } = XjSectVoiceTier.Ordinary;
	public int LastContributionYear { get; set; }
	public int LastPublishedYear { get; set; }
	public float SupplyDebt { get; set; }
	public string Responsibility { get; set; } = string.Empty;
	public float PrivilegeHeat { get; set; }
	public int PrivilegeIncidentCount { get; set; }
	public int LastPrivilegeIncidentYear { get; set; }
}

internal sealed class XjCityFamilyGovernanceArchiveRecord
{
	public int SchemaVersion { get; set; } = XjSectDomainSchema.CurrentVersion;
	public long CityId { get; set; }
	public long SectId { get; set; }
	public long GoverningFamilyId { get; set; }
	public int ConfirmedYear { get; set; }
	public int LastChallengeYear { get; set; }
	public long ChallengerFamilyId { get; set; }
	public int ChallengeStartYear { get; set; }
	public int ChallengeConsecutiveYears { get; set; }
	public float IncumbentControlScore { get; set; }
	public float ChallengerControlScore { get; set; }
	public string State { get; set; } = XjCityFamilyGovernanceState.Stable;
}
