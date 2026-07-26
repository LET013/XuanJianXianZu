namespace XuanJianVNext.Data.DongTian;

internal readonly struct XjQiYuDongTianExplorerRecord
{
	internal readonly long ExplorerActorId;
	internal readonly string ExplorerActorName;
	internal readonly string RealmId;
	internal readonly string RealmDisplay;
	internal readonly string DaoTu;
	internal readonly int DistanceToAnchor;
	internal readonly int ReservedYear;
	internal readonly bool Resolved;
	internal readonly int ResolvedYear;
	internal readonly bool DeathResolved;
	internal readonly bool RewardApplied;
	internal readonly string RewardType;
	internal readonly string ResultSummary;
	internal readonly bool DeathPending;
	internal readonly int DeathPendingYear;
	internal readonly bool DeathFinalized;
	internal readonly int DeathFinalizedYear;

	internal XjQiYuDongTianExplorerRecord(
		long explorerActorId,
		string explorerActorName,
		string realmId,
		string realmDisplay,
		string daoTu,
		int distanceToAnchor,
		int reservedYear,
		bool resolved,
		int resolvedYear,
		bool deathResolved,
		bool rewardApplied,
		string rewardType,
		string resultSummary,
		bool deathPending = false,
		int deathPendingYear = 0,
		bool deathFinalized = false,
		int deathFinalizedYear = 0)
	{
		ExplorerActorId = explorerActorId < 0L ? 0L : explorerActorId;
		ExplorerActorName = explorerActorName ?? string.Empty;
		RealmId = realmId ?? string.Empty;
		RealmDisplay = realmDisplay ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		DistanceToAnchor = distanceToAnchor < 0 ? 0 : distanceToAnchor;
		ReservedYear = reservedYear < 0 ? 0 : reservedYear;
		Resolved = resolved;
		ResolvedYear = resolvedYear < 0 ? 0 : resolvedYear;
		DeathResolved = deathResolved;
		RewardApplied = rewardApplied;
		RewardType = rewardType ?? string.Empty;
		ResultSummary = resultSummary ?? string.Empty;
		DeathPending = deathPending;
		DeathPendingYear = deathPendingYear < 0 ? 0 : deathPendingYear;
		DeathFinalized = deathFinalized;
		DeathFinalizedYear = deathFinalizedYear < 0 ? 0 : deathFinalizedYear;
	}
}

internal readonly struct XjDongTianRecord
{
	internal readonly bool Found;
	internal readonly string RecordId;
	internal readonly string QiYuDongTianId;
	internal readonly string DaoTuGroup;
	internal readonly string RelatedDaoTuIds;
	internal readonly string DisplayName;
	internal readonly int CreatedYear;
	internal readonly int OpenUntilYear;
	internal readonly int MaxExploreActors;
	internal readonly int ExploredActorCount;
	internal readonly int RemainingExploreCount;
	internal readonly bool IsOpen;
	internal readonly string DeathRateProfileSummary;
	internal readonly string RewardPoolSummary;
	internal readonly string Summary;
	internal readonly long AnchorActorId;
	internal readonly string AnchorActorName;
	internal readonly int AnchorTileX;
	internal readonly int AnchorTileY;
	internal readonly long AnchorCityId;
	internal readonly string AnchorCityName;
	internal readonly int AnchorYear;
	internal readonly string AnchorSource;
	internal readonly int LastExploreReserveYear;
	internal readonly System.Collections.Generic.IReadOnlyList<XjQiYuDongTianExplorerRecord> ExplorerRecords;
	internal readonly string EntityAssetId;
	internal readonly long EntityBuildingId;
	internal readonly bool EntitySpawned;
	internal readonly bool EntityClosed;

	internal XjDongTianRecord(
		bool found,
		string recordId,
		string qiYuDongTianId,
		string daoTuGroup,
		string relatedDaoTuIds,
		string displayName,
		int createdYear,
		int openUntilYear,
		int maxExploreActors,
		int exploredActorCount,
		int remainingExploreCount,
		bool isOpen,
		string deathRateProfileSummary,
		string rewardPoolSummary,
		string summary,
		long anchorActorId = 0L,
		string anchorActorName = "",
		int anchorTileX = -1,
		int anchorTileY = -1,
		long anchorCityId = 0L,
		string anchorCityName = "",
		int anchorYear = 0,
		string anchorSource = "",
		int lastExploreReserveYear = 0,
		System.Collections.Generic.IReadOnlyList<XjQiYuDongTianExplorerRecord> explorerRecords = null,
		string entityAssetId = "",
		long entityBuildingId = 0L,
		bool entitySpawned = false,
		bool entityClosed = false)
	{
		Found = found;
		RecordId = recordId ?? string.Empty;
		QiYuDongTianId = qiYuDongTianId ?? string.Empty;
		DaoTuGroup = daoTuGroup ?? string.Empty;
		RelatedDaoTuIds = relatedDaoTuIds ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		CreatedYear = createdYear < 0 ? 0 : createdYear;
		OpenUntilYear = openUntilYear < 0 ? 0 : openUntilYear;
		MaxExploreActors = maxExploreActors < 0 ? 0 : maxExploreActors;
		ExploredActorCount = exploredActorCount < 0 ? 0 : exploredActorCount;
		RemainingExploreCount = remainingExploreCount < 0 ? 0 : remainingExploreCount;
		IsOpen = isOpen;
		DeathRateProfileSummary = deathRateProfileSummary ?? string.Empty;
		RewardPoolSummary = rewardPoolSummary ?? string.Empty;
		Summary = summary ?? string.Empty;
		AnchorActorId = anchorActorId < 0L ? 0L : anchorActorId;
		AnchorActorName = anchorActorName ?? string.Empty;
		AnchorTileX = anchorTileX;
		AnchorTileY = anchorTileY;
		AnchorCityId = anchorCityId < 0L ? 0L : anchorCityId;
		AnchorCityName = anchorCityName ?? string.Empty;
		AnchorYear = anchorYear < 0 ? 0 : anchorYear;
		AnchorSource = anchorSource ?? string.Empty;
		LastExploreReserveYear = lastExploreReserveYear < 0 ? 0 : lastExploreReserveYear;
		ExplorerRecords = explorerRecords ?? System.Array.Empty<XjQiYuDongTianExplorerRecord>();
		EntityAssetId = entityAssetId ?? string.Empty;
		EntityBuildingId = entityBuildingId < 0L ? 0L : entityBuildingId;
		EntitySpawned = entitySpawned;
		EntityClosed = entityClosed;
	}
}

internal sealed class XjQiYuDongTianExplorerArchiveData
{
	public long ExplorerActorId { get; set; }

	public string ExplorerActorName { get; set; } = string.Empty;

	public string RealmId { get; set; } = string.Empty;

	public string RealmDisplay { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public int DistanceToAnchor { get; set; }

	public int ReservedYear { get; set; }

	public bool Resolved { get; set; }

	public int ResolvedYear { get; set; }

	public bool DeathResolved { get; set; }

	public bool RewardApplied { get; set; }

	public string RewardType { get; set; } = string.Empty;

	public string ResultSummary { get; set; } = string.Empty;

	public bool DeathPending { get; set; }

	public int DeathPendingYear { get; set; }

	public bool DeathFinalized { get; set; }

	public int DeathFinalizedYear { get; set; }
}

internal sealed class XjDongTianArchiveData
{
	public string RecordId { get; set; } = string.Empty;

	public string QiYuDongTianId { get; set; } = string.Empty;

	public string DaoTuGroup { get; set; } = string.Empty;

	public string RelatedDaoTuIds { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public int CreatedYear { get; set; }

	public int OpenUntilYear { get; set; }

	public int MaxExploreActors { get; set; }

	public int ExploredActorCount { get; set; }

	public int RemainingExploreCount { get; set; }

	public bool IsOpen { get; set; }

	public string DeathRateProfileSummary { get; set; } = string.Empty;

	public string RewardPoolSummary { get; set; } = string.Empty;

	public string Summary { get; set; } = string.Empty;

	public long AnchorActorId { get; set; }

	public string AnchorActorName { get; set; } = string.Empty;

	public int AnchorTileX { get; set; } = -1;

	public int AnchorTileY { get; set; } = -1;

	public long AnchorCityId { get; set; }

	public string AnchorCityName { get; set; } = string.Empty;

	public int AnchorYear { get; set; }

	public string AnchorSource { get; set; } = string.Empty;

	public int LastExploreReserveYear { get; set; }

	public System.Collections.Generic.List<XjQiYuDongTianExplorerArchiveData> ExplorerRecords { get; set; } = new System.Collections.Generic.List<XjQiYuDongTianExplorerArchiveData>();

	public string EntityAssetId { get; set; } = string.Empty;

	public long EntityBuildingId { get; set; }

	public bool EntitySpawned { get; set; }

	public bool EntityClosed { get; set; }
}
