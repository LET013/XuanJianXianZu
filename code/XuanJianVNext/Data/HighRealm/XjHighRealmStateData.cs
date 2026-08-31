namespace XuanJianVNext.Data.HighRealm;

internal readonly struct XjRenDanState
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly int Year;
	internal readonly string Source;
	internal readonly int ShenTongCount;
	internal readonly long VictimActorId;
	internal readonly string VictimActorName;
	internal readonly string VictimDaoTu;
	internal readonly string Summary;
	internal readonly bool DeathFinalized;
	internal readonly int DeathFinalizedYear;
	internal readonly string Stage;
	internal readonly string Outcome;
	internal readonly long RivalActorId;
	internal readonly string RivalActorName;

	internal XjRenDanState(
		bool found,
		long actorId,
		string actorName,
		int year,
		string source,
		int shenTongCount,
		long victimActorId,
		string victimActorName,
		string victimDaoTu,
		string summary,
		bool deathFinalized = false,
		int deathFinalizedYear = 0,
		string stage = "",
		string outcome = "",
		long rivalActorId = 0L,
		string rivalActorName = "")
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		Year = year < 0 ? 0 : year;
		Source = source ?? string.Empty;
		ShenTongCount = shenTongCount < 0 ? 0 : shenTongCount;
		VictimActorId = victimActorId < 0L ? 0L : victimActorId;
		VictimActorName = victimActorName ?? string.Empty;
		VictimDaoTu = victimDaoTu ?? string.Empty;
		Summary = summary ?? string.Empty;
		DeathFinalized = deathFinalized;
		DeathFinalizedYear = deathFinalizedYear < 0 ? 0 : deathFinalizedYear;
		Stage = stage ?? string.Empty;
		Outcome = outcome ?? string.Empty;
		RivalActorId = rivalActorId < 0L ? 0L : rivalActorId;
		RivalActorName = rivalActorName ?? string.Empty;
	}
}

internal sealed class XjRenDanArchiveData
{
	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public int Year { get; set; }

	public string Source { get; set; } = string.Empty;

	public int ShenTongCount { get; set; }

	public long VictimActorId { get; set; }

	public string VictimActorName { get; set; } = string.Empty;

	public string VictimDaoTu { get; set; } = string.Empty;

	public string Summary { get; set; } = string.Empty;

	public bool DeathFinalized { get; set; }

	public int DeathFinalizedYear { get; set; }

	public string Stage { get; set; } = string.Empty;

	public string Outcome { get; set; } = string.Empty;

	public long RivalActorId { get; set; }

	public string RivalActorName { get; set; } = string.Empty;
}

internal readonly struct XjGuoWeiQuanBingState
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string DaoTu;
	internal readonly string GuoWei;
	internal readonly string LocalQuanBing;
	internal readonly string SeizedQuanBing;
	internal readonly string SeizedQuanBingSources;
	internal readonly string ForeignQuanBing;
	internal readonly string WithdrawnToDongTian;
	internal readonly string GuoWeiZhongAi;
	internal readonly string PendingExternalZhengWeiDaoTu;
	internal readonly int LockUntilYear;
	internal readonly bool IntegrationRetreatActive;
	internal readonly int IntegrationRetreatEndYear;
	internal readonly string Summary;
	internal readonly string LifecycleStatus;
	internal readonly int AcquiredYear;
	internal readonly int ReleasedYear;
	internal readonly string ReleaseReason;

	internal XjGuoWeiQuanBingState(
		bool found,
		long actorId,
		string actorName,
		string daoTu,
		string guoWei,
		string localQuanBing,
		string seizedQuanBing,
		string foreignQuanBing,
		string withdrawnToDongTian,
		string guoWeiZhongAi,
		string pendingExternalZhengWeiDaoTu,
		int lockUntilYear,
		bool integrationRetreatActive,
		int integrationRetreatEndYear,
		string summary,
		string lifecycleStatus = "Active",
		int acquiredYear = 0,
		int releasedYear = 0,
		string releaseReason = "",
		string seizedQuanBingSources = "")
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		GuoWei = guoWei ?? string.Empty;
		LocalQuanBing = localQuanBing ?? string.Empty;
		SeizedQuanBing = seizedQuanBing ?? string.Empty;
		SeizedQuanBingSources = seizedQuanBingSources ?? string.Empty;
		ForeignQuanBing = foreignQuanBing ?? string.Empty;
		WithdrawnToDongTian = withdrawnToDongTian ?? string.Empty;
		GuoWeiZhongAi = guoWeiZhongAi ?? string.Empty;
		PendingExternalZhengWeiDaoTu = pendingExternalZhengWeiDaoTu ?? string.Empty;
		LockUntilYear = lockUntilYear < 0 ? 0 : lockUntilYear;
		IntegrationRetreatActive = integrationRetreatActive;
		IntegrationRetreatEndYear = integrationRetreatEndYear < 0 ? 0 : integrationRetreatEndYear;
		Summary = summary ?? string.Empty;
		LifecycleStatus = lifecycleStatus ?? string.Empty;
		AcquiredYear = acquiredYear < 0 ? 0 : acquiredYear;
		ReleasedYear = releasedYear < 0 ? 0 : releasedYear;
		ReleaseReason = releaseReason ?? string.Empty;
	}
}

internal sealed class XjGuoWeiQuanBingArchiveData
{
	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public string GuoWei { get; set; } = string.Empty;

	public string LocalQuanBing { get; set; } = string.Empty;

	public string SeizedQuanBing { get; set; } = string.Empty;

	public string SeizedQuanBingSources { get; set; } = string.Empty;

	public string ForeignQuanBing { get; set; } = string.Empty;

	public string WithdrawnToDongTian { get; set; } = string.Empty;

	public string GuoWeiZhongAi { get; set; } = string.Empty;

	public string PendingExternalZhengWeiDaoTu { get; set; } = string.Empty;

	public int LockUntilYear { get; set; }

	public bool IntegrationRetreatActive { get; set; }

	public int IntegrationRetreatEndYear { get; set; }

	public string Summary { get; set; } = string.Empty;

	public string LifecycleStatus { get; set; } = "Active";

	public int AcquiredYear { get; set; }

	public int ReleasedYear { get; set; }

	public string ReleaseReason { get; set; } = string.Empty;
}

internal sealed class XjGuoWeiQuanBingLostAuthorityArchiveData
{
	public string SourceDaoTu { get; set; } = string.Empty;

	public string Authority { get; set; } = string.Empty;

	public string TargetDaoTu { get; set; } = string.Empty;

	public int Year { get; set; }

	public string Reason { get; set; } = string.Empty;
}
