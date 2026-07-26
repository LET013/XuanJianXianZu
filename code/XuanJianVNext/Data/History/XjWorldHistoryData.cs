using System;

namespace XuanJianVNext.Data.History;

internal static class XjWorldHistorySchema
{
	internal const int CurrentVersion = 3;
}

[Flags]
internal enum XjHistoryVisibility
{
	None = 0,
	Personal = 1 << 0,
	Family = 1 << 1,
	Sect = 1 << 2,
	World = 1 << 3,
	CenturyCandidate = 1 << 4
}

internal static class XjHistoryCenturyStatus
{
	internal const string Pending = "Pending";
	internal const string Referenced = "Referenced";
	internal const string Compressed = "Compressed";
	internal const string Permanent = "Permanent";
}

internal static class XjHistoryResult
{
	internal const string None = "";
	internal const string Success = "Success";
	internal const string Failure = "Failure";
	internal const string Death = "Death";
	internal const string Transfer = "Transfer";
	internal const string Change = "Change";
}

internal static class XjWorldHistoryCategory
{
	internal const string All = "全部";
	internal const string Cultivation = "修行";
	internal const string Family = "家族";
	internal const string Sect = "宗门";
	internal const string Inheritance = "传承";
	internal const string Craft = "百艺";
	internal const string Opportunity = "机缘";
	internal const string Vendetta = "恩怨";
	internal const string LifeAndDeath = "生死";
	internal const string World = "天下";

	// 兼容旧代码中的类别名称。
	internal const string Resource = Inheritance;
	internal const string HighRealm = Cultivation;
	internal const string SecretRealm = Opportunity;
	internal const string YinSi = LifeAndDeath;
	internal const string Evil = Vendetta;

	internal static readonly string[] Ordered =
	{
		All,
		Cultivation,
		Family,
		Sect,
		Inheritance,
		Craft,
		Opportunity,
		Vendetta,
		LifeAndDeath,
		World
	};
}

internal sealed class XjWorldHistoryArchiveRecord
{
	public int SchemaVersion { get; set; } = XjWorldHistorySchema.CurrentVersion;
	public long EventId { get; set; }
	public int Year { get; set; }
	public string EventType { get; set; } = string.Empty;
	public string Category { get; set; } = XjWorldHistoryCategory.World;
	public string Title { get; set; } = string.Empty;
	public string Body { get; set; } = string.Empty;
	public string IconId { get; set; } = string.Empty;
	public int Importance { get; set; } = 1;
	public bool IsProtected { get; set; }
	public int VisibilityFlags { get; set; }
	public string Result { get; set; } = XjHistoryResult.None;
	public long CauseEventId { get; set; }
	public string CenturyStatus { get; set; } = XjHistoryCenturyStatus.Pending;

	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public long RelatedActorId { get; set; }
	public string RelatedActorName { get; set; } = string.Empty;

	public long SectId { get; set; }
	public string SectNameSnapshot { get; set; } = string.Empty;
	public long RelatedSectId { get; set; }
	public string RelatedSectNameSnapshot { get; set; } = string.Empty;

	public long FamilyId { get; set; }
	public string FamilyNameSnapshot { get; set; } = string.Empty;
	public long RelatedFamilyId { get; set; }
	public string RelatedFamilyNameSnapshot { get; set; } = string.Empty;

	public long CityId { get; set; }
	public string CityNameSnapshot { get; set; } = string.Empty;
	public int LocationX { get; set; }
	public int LocationY { get; set; }
	public bool HasLocation { get; set; }
}
