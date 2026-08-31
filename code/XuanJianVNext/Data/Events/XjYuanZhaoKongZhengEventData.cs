namespace XuanJianVNext.Data.Events;

/// <summary>
/// “渊照空证”世界事件状态。触发时点不是绝对玄历1000年，而是本世界第一次
/// 固化的纪年起点所对应的玄鉴历第500年。BaseWorldYear/ScheduledTriggerYear一经建立永久不重算。
/// </summary>
internal sealed class XjYuanZhaoKongZhengEventArchiveData
{
	public bool Initialized { get; set; }
	public int BaseWorldYear { get; set; }
	public int ScheduledTriggerYear { get; set; }
	public bool TimelineMigrated { get; set; }
	public bool Triggered { get; set; }
	public bool LegacyBaseline { get; set; }
	public int TriggeredYear { get; set; }
	public bool DaoTuEstablished { get; set; }
	public bool LegacyDongTianCreated { get; set; }
	public int LegacyDongTianCreatedYear { get; set; }

	// 0.9.8.17：道尊隐世事件账本。只保存因缘/投影的世界级游标，具体角色许可仍写在角色自身。
	// 这样读档不会重复邀请，也不会为了追索道尊本身而另造尘世承载。
	public int FounderEventSchemaVersion { get; set; }
	public int LastAudienceInviteYear { get; set; }
	public long PendingAudienceActorId { get; set; }
	public int PendingAudienceUntilYear { get; set; }
	public string PendingAudienceReason { get; set; } = string.Empty;
	public int TotalAudienceCount { get; set; }
	public int TotalTeachingCount { get; set; }
	public int LastProjectionYear { get; set; }
	public int LastAuthorityInterventionYear { get; set; }

	// 0.9.8.18：“照真请凭函”世界级节奏。与道尊正式【水月传召】分开：
	// 前者是百余年一次的稀有游历资格，后者仍只在真正道统/权柄节点发生。
	public int CredentialSchemaVersion { get; set; }
	public int NextCredentialYear { get; set; }
	public int LastCredentialYear { get; set; }
	public long ActiveCredentialActorId { get; set; }
	public int ActiveCredentialUntilYear { get; set; }
	public int TotalCredentialIssued { get; set; }
	public int TotalCredentialResolved { get; set; }
	public string LastCredentialHolderName { get; set; } = string.Empty;
}
