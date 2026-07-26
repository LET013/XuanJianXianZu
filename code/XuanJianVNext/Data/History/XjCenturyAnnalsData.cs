using System.Collections.Generic;

namespace XuanJianVNext.Data.History;

internal static class XjCenturyAnnalsSchema
{
	internal const int CurrentVersion = 9;
	internal const int YearsPerCentury = 100;
	internal const int MaxCenturyRecords = 50;
	internal const int MaxFamilyStatesPerCentury = 128;
	internal const int MaxSectSummariesPerCentury = 16;
	internal const int MaxDaoSummariesPerCentury = 64;
	internal const int MaxRealmSummariesPerCentury = 12;
	internal const int MaxActorHighlightsPerCentury = 16;
	internal const int MaxEventIdsPerRecord = 24;
	internal const int MaxEventIdsPerFamily = 6;
	internal const int MaxFamilyStageStates = 4096;
	internal const int MaxLedgerRecords = 8000;
}

internal static class XjCenturyFamilyStage
{
	internal const string CaoChuang = "草创";
	internal const string LiZu = "立族";
	internal const string XingSheng = "兴盛";
	internal const string DingSheng = "鼎盛";
	internal const string ZhongShuai = "中衰";
	internal const string FuZhen = "复振";
	internal const string FenLie = "分裂";
	internal const string FuMie = "覆灭";
}

internal static class XjCenturyFamilyAspiration
{
	internal const string QiuFa = "求法";
	internal const string YuFu = "育府";
	internal const string QiuJin = "求金";
	internal const string FuZhen = "复振";
}

internal static class XjCenturyFamilySupportPurpose
{
	internal const string FuChiZhuJi = "扶持筑基";
	internal const string FuChiZiFu = "扶持紫府";
	internal const string FuChiQiuJin = "扶持求金";
}

internal sealed class XjCenturyAnnalsArchiveRecord
{
	public int SchemaVersion { get; set; } = XjCenturyAnnalsSchema.CurrentVersion;
	public int CenturyId { get; set; }
	public int StartYear { get; set; }
	public int EndYear { get; set; }
	public int GeneratedYear { get; set; }
	public bool IsCompleteCycle { get; set; }
	public string Title { get; set; } = string.Empty;
	public string WorldSummary { get; set; } = string.Empty;
	public List<long> ImportantEventIds { get; set; } = new List<long>();
	public List<XjCenturyFamilyStateRecord> FamilyStates { get; set; } = new List<XjCenturyFamilyStateRecord>();
	public List<XjCenturySummaryItemRecord> SectSummaries { get; set; } = new List<XjCenturySummaryItemRecord>();
	public List<XjCenturySummaryItemRecord> RealmStatistics { get; set; } = new List<XjCenturySummaryItemRecord>();
	public List<XjCenturySummaryItemRecord> DaoSummaries { get; set; } = new List<XjCenturySummaryItemRecord>();
	public List<XjCenturySummaryItemRecord> ArtifactSummaries { get; set; } = new List<XjCenturySummaryItemRecord>();
	public List<XjCenturyActorHighlightRecord> RepresentativeActors { get; set; } = new List<XjCenturyActorHighlightRecord>();
}

internal sealed class XjCenturyFamilyStateRecord
{
	public int SchemaVersion { get; set; } = XjCenturyAnnalsSchema.CurrentVersion;
	public long FamilyStableId { get; set; }
	public string FamilyName { get; set; } = string.Empty;
	public string PreviousStage { get; set; } = string.Empty;
	public string CurrentStage { get; set; } = string.Empty;
	public int FirstRecordedYear { get; set; }
	public bool IsExtinct { get; set; }
	public int AliveCount { get; set; }
	public int TotalCount { get; set; }
	public int CultivatorCount { get; set; }
	public int ZiFuCount { get; set; }
	public int JinDanCount { get; set; }
	public string HighestRealm { get; set; } = string.Empty;
	public int HighestRealmOrder { get; set; }
	public long SectId { get; set; }
	public string SectName { get; set; } = string.Empty;
	public string VoiceTier { get; set; } = string.Empty;
	public float InfluenceScore { get; set; }
	public int GoverningCityCount { get; set; }
	public string CityInfluence { get; set; } = string.Empty;
	public string CraftSummary { get; set; } = string.Empty;
	public string GongFaSummary { get; set; } = string.Empty;
	public string FaBaoSummary { get; set; } = string.Empty;
	public long RepresentativeActorId { get; set; }
	public string RepresentativeActorName { get; set; } = string.Empty;
	public List<long> ImportantEventIds { get; set; } = new List<long>();
	public List<XjCenturyActorHighlightRecord> ImportantActors { get; set; } = new List<XjCenturyActorHighlightRecord>();
	public string StageReason { get; set; } = string.Empty;
	public string Aspiration { get; set; } = string.Empty;
	public string AspirationStatus { get; set; } = string.Empty;
	public int AspirationSinceYear { get; set; }
	public string AspirationSummary { get; set; } = string.Empty;
	public string SectRelation { get; set; } = string.Empty;
	public long SupportedActorId { get; set; }
	public string SupportedActorName { get; set; } = string.Empty;
	public string SupportPurpose { get; set; } = string.Empty;
	public int SupportedSinceYear { get; set; }
	public long PillarActorId { get; set; }
	public string PillarActorName { get; set; } = string.Empty;
	public string PillarRealm { get; set; } = string.Empty;
	public string FamilyTreasureSummary { get; set; } = string.Empty;
}

internal sealed class XjCenturyActorHighlightRecord
{
	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public long FamilyStableId { get; set; }
	public string FamilyName { get; set; } = string.Empty;
	public long SectId { get; set; }
	public string SectName { get; set; } = string.Empty;
	public string Realm { get; set; } = string.Empty;
	public int RealmOrder { get; set; }
	public string Reason { get; set; } = string.Empty;
}

internal sealed class XjCenturySummaryItemRecord
{
	public string Key { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public int Score { get; set; }
	public string Trend { get; set; } = string.Empty;
	public string State { get; set; } = string.Empty;
	public string Summary { get; set; } = string.Empty;
}

internal sealed class XjCenturyAnnalsStateRecord
{
	public int SchemaVersion { get; set; } = XjCenturyAnnalsSchema.CurrentVersion;
	public int BaseWorldYear { get; set; }
	public int LastCompletedCenturyId { get; set; }
	public int LastGeneratedYear { get; set; }
	public List<int> GeneratedCenturyIds { get; set; } = new List<int>();
	public List<XjCenturyFamilyStageStateRecord> FamilyStageStates { get; set; } = new List<XjCenturyFamilyStageStateRecord>();
}

internal sealed class XjCenturyFamilyStageStateRecord
{
	public long FamilyStableId { get; set; }
	public string Stage { get; set; } = string.Empty;
	public int FirstRecordedYear { get; set; }
	public int LastUpdatedYear { get; set; }
	public int LastScore { get; set; }
	public int ConsecutiveDeclineCycles { get; set; }
	public int ConsecutiveRecoveryCycles { get; set; }
	public bool WasExtinct { get; set; }
	public string ActiveAspiration { get; set; } = string.Empty;
	public int AspirationSinceYear { get; set; }
	public long SupportedActorId { get; set; }
	public string SupportPurpose { get; set; } = string.Empty;
	public int SupportedSinceYear { get; set; }
	public int LastSupportYear { get; set; }
	public long LastClanLeaderActorId { get; set; }
}

internal sealed class XjCenturyAnnalsLedgerRecord
{
	public int SchemaVersion { get; set; } = XjCenturyAnnalsSchema.CurrentVersion;
	public int CenturyId { get; set; }
	public long FamilyStableId { get; set; }
	public string FamilyName { get; set; } = string.Empty;
	public string EventType { get; set; } = string.Empty;
	public int Year { get; set; }
	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public string DaoTu { get; set; } = string.Empty;
	public int Grade { get; set; }
	public string ItemClass { get; set; } = string.Empty;
	public long SectId { get; set; }
	public string SectName { get; set; } = string.Empty;
	public int Importance { get; set; }
	public string Summary { get; set; } = string.Empty;
}
