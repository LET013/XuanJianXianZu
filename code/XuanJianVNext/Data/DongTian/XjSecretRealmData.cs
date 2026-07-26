using System;

namespace XuanJianVNext.Data.DongTian;

internal static class XjSecretRealmStage
{
	internal const string None = "None";
	internal const string SurveyingVoid = "SurveyingVoid";
	internal const string LayingXuanTao = "LayingXuanTao";
	internal const string SuppressingTreasure = "SuppressingTreasure";
	internal const string NourishingSpace = "NourishingSpace";
	internal const string StabilizingEntrance = "StabilizingEntrance";
	internal const string Fudi = "Fudi";
	internal const string UpgradingDongtian = "UpgradingDongtian";
	internal const string Dongtian = "Dongtian";
	internal const string Dormant = "Dormant";
}

internal sealed class XjSecretRealmArchiveRecord
{
	public int SchemaVersion { get; set; } = 1;
	public long RealmId { get; set; }
	public long SectId { get; set; }
	public string DisplayName { get; set; } = string.Empty;
	public string Stage { get; set; } = XjSecretRealmStage.None;
	public bool ConstructionMethodKnown { get; set; }
	public string ConstructionMethodSource { get; set; } = string.Empty;
	public int MethodAcquiredYear { get; set; }
	public long EntranceCityId { get; set; }
	public string EntranceCityName { get; set; } = string.Empty;
	public int Stability { get; set; }
	public int XuanTaoIntegrity { get; set; }
	public int Capacity { get; set; }
	public string SuppressingTreasureName { get; set; } = string.Empty;
	public long LeadFormationMasterId { get; set; }
	public long SittingJinDanActorId { get; set; }
	public long ActiveTaskId { get; set; }
	public int StageStartedYear { get; set; }
	public int StageDueYear { get; set; }
	public int LastMaintainedYear { get; set; }
	public int FailureCount { get; set; }
	public bool HasRetainedStageProgress { get; set; }
	public bool EntranceOpen { get; set; }
	public int RuntimeVersion { get; set; }
}
