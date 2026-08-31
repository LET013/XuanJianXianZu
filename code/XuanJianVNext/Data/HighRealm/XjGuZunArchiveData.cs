using System.Collections.Generic;

namespace XuanJianVNext.Data.HighRealm;

/// <summary>
/// 天地留存的故尊命痕。活体高境候选与已经陨落、可重现的故尊共用一份记录，
/// 以便读档后仍能恢复其一生达到过的最高合法境界，而不是只读取死亡瞬间状态。
/// </summary>
internal sealed class XjGuZunArchiveRecord
{
	public int SchemaVersion { get; set; } = 3;
	public string ArchiveId { get; set; } = string.Empty;
	public long SourceActorId { get; set; }
	public long CurrentActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public string AssetId { get; set; } = string.Empty;
	public string DaoTu { get; set; } = string.Empty;
	public string CultivationPath { get; set; } = string.Empty;
	public string HighestRealmId { get; set; } = string.Empty;
	public int HighestRealmOrder { get; set; }
	public int HighestMinorStage { get; set; }
	public int HighestRealmProgress { get; set; }
	public int HighestJinDanYiXiang { get; set; }
	public string HighestGuoWei { get; set; } = string.Empty;
	public bool EligibleZhengWei { get; set; }
	public string HighestSnapshotJson { get; set; } = string.Empty;
	public List<string> SavedTraitIds { get; set; } = new List<string>();
	public int FirstObservedYear { get; set; }
	public int HighestReachedYear { get; set; }
	public int DeathYear { get; set; }
	public int DeathAge { get; set; }
	public string DeathCause { get; set; } = string.Empty;
	public int DeathTileX { get; set; }
	public int DeathTileY { get; set; }
	public long OriginalFamilyId { get; set; }
	public string OriginalFamilyName { get; set; } = string.Empty;
	public long OriginalZongMenId { get; set; }
	public string OriginalZongMenName { get; set; } = string.Empty;
	public long OriginalCityId { get; set; }
	public string OriginalCityName { get; set; } = string.Empty;
	public bool HeavenFavored { get; set; }
	public int HeavenFavorScore { get; set; }
	public bool IsCurrentlyManifested { get; set; }
	public int ReappearanceCount { get; set; }
	public int LastReappearanceYear { get; set; }
	public int LastSelfTruthCheckYear { get; set; }
}

internal sealed class XjDaoTuHighRealmContinuityRecord
{
	public string DaoTu { get; set; } = string.Empty;
	public int LastHighRealmPresentYear { get; set; }
	public int LastReappearanceYear { get; set; }
}
