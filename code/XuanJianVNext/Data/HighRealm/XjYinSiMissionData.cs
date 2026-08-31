using System.Collections.Generic;

namespace XuanJianVNext.Data.HighRealm;

internal static class XjYinSiMissionType
{
	internal const string JinXingYaoXie = "JinXingYaoXie";
	internal const string LivingJinDan = "LivingJinDan";
}

internal static class XjYinSiMissionStage
{
	internal const string Scheduled = "Scheduled";
	internal const string Locating = "Locating";
	internal const string Manifesting = "Manifesting";
	internal const string Pursuing = "Pursuing";
	internal const string Evaded = "Evaded";
	internal const string Resolved = "Resolved";
	internal const string Cancelled = "Cancelled";
}

internal sealed class XjYinSiMissionArchiveRecord
{
	public int SchemaVersion { get; set; } = 1;
	public long MissionId { get; set; }
	public string MissionType { get; set; } = XjYinSiMissionType.LivingJinDan;
	public long TargetActorId { get; set; }
	public string TargetActorName { get; set; } = string.Empty;
	public int DiscoveryYear { get; set; }
	public string Stage { get; set; } = XjYinSiMissionStage.Scheduled;
	public int EscalationLevel { get; set; }
	public int NextActionYear { get; set; }
	public int LastActionYear { get; set; }
	public long LastKnownCityId { get; set; }
	public long SecretRealmId { get; set; }
	public List<long> ManifestActorIds { get; set; } = new List<long>();
	public int DefeatedManifestCount { get; set; }
	public string Resolution { get; set; } = string.Empty;
	public int RuntimeVersion { get; set; }
}
