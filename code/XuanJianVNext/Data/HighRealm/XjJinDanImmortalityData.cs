namespace XuanJianVNext.Data.HighRealm;

internal static class XjJinDanYinSiState
{
	internal const string Hidden = "Hidden";
	internal const string Traced = "Traced";
	internal const string NearDiscovery = "NearDiscovery";
	internal const string Known = "Known";
	internal const string Locating = "Locating";
	internal const string Pursuing = "Pursuing";
	internal const string Evaded = "Evaded";
	internal const string Dead = "Dead";
}

internal sealed class XjJinDanImmortalityArchiveRecord
{
	public long ActorId { get; set; }
	public int ActivatedYear { get; set; }
	public float YinSiExposure { get; set; }
	public string LastExposureReason { get; set; } = string.Empty;
	public int LastExposureUpdateYear { get; set; }
	public bool YinSiKnown { get; set; }
	public string YinSiState { get; set; } = XjJinDanYinSiState.Hidden;
	public int PursuitCount { get; set; }
	public int LastKnownYear { get; set; }
	public bool IsAlive { get; set; } = true;

	// 修士名录历史快照。角色死亡后原生 Actor 会被回收，因此玩家可继续查看的
	// 人物资料必须随世界档案保存，而不能再依赖 ResolveActor。旧档缺失字段时按空值兼容。
	public string Name { get; set; } = string.Empty;
	public int Age { get; set; }
	public string RealmId { get; set; } = string.Empty;
	public string CultivationPath { get; set; } = string.Empty;
	public string Achievement { get; set; } = string.Empty;
	public string DaoTu { get; set; } = string.Empty;
	public string JinXing { get; set; } = string.Empty;
	public string GuoWei { get; set; } = string.Empty;
	public bool IsDaoTai { get; set; }
	public bool IsKongZheng { get; set; }
	public long SectId { get; set; }
	public string SectName { get; set; } = string.Empty;
	public long FamilyId { get; set; }
	public string DaoTaiPresenceStatus { get; set; } = string.Empty;
	public int DaoTaiNextReturnYear { get; set; }
	public int AuthorityCount { get; set; }
	public string LocalAuthoritySummary { get; set; } = string.Empty;
	public string SeizedAuthoritySummary { get; set; } = string.Empty;
	public string PendingAuthoritySummary { get; set; } = string.Empty;
	public string AuthorityLifecycleStatus { get; set; } = string.Empty;
	public bool IntegrationRetreatActive { get; set; }
	public int IntegrationRetreatEndYear { get; set; }
	public string GuoWeiImageSummary { get; set; } = string.Empty;
	public string ShenTongMutationSummary { get; set; } = string.Empty;
	public int DaoXing { get; set; }
	public int XiuChi { get; set; }
	public string LifeStatus { get; set; } = "存世";
	public int DeathYear { get; set; }
	public string DeathReason { get; set; } = string.Empty;
	public int LastObservedYear { get; set; }
}
