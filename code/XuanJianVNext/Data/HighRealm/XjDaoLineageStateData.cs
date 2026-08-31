using System.Collections.Generic;

namespace XuanJianVNext.Data.HighRealm;

internal sealed class XjDaoLineageWorldArchiveData
{
	public int SchemaVersion { get; set; } = 1;
	public List<XjDaoLineageArchiveRecord> Lineages { get; set; } = new List<XjDaoLineageArchiveRecord>();
	public List<XjDaoProofFoundationArchiveData> ProofFoundations { get; set; } = new List<XjDaoProofFoundationArchiveData>();
}

internal sealed class XjDaoLineageArchiveRecord
{
	public string DaoTu { get; set; } = string.Empty;
	public int CoreRevision { get; set; } = 1;
	public int Vitality { get; set; } = 50;
	public string Phase { get; set; } = "守成";
	public string CoreDoctrine { get; set; } = string.Empty;
	public string ShenTongBias { get; set; } = "守本";
	public long RevivalActorId { get; set; }
	public string RevivalActorName { get; set; } = string.Empty;
	public string RevivalDirection { get; set; } = string.Empty;
	public int RevivalYear { get; set; }
	public int LastChangedYear { get; set; }
	public int LastSyncedYear { get; set; }
	public List<XjDaoAuthorityArchiveData> Authorities { get; set; } = new List<XjDaoAuthorityArchiveData>();
}

internal sealed class XjDaoAuthorityArchiveData
{
	public string Name { get; set; } = string.Empty;
	public string Status { get; set; } = "潜";
	public long HolderActorId { get; set; }
	public string HolderName { get; set; } = string.Empty;
	public string SourceDaoTu { get; set; } = string.Empty;
	public int LastChangedYear { get; set; }
}

internal sealed class XjDaoProofFoundationArchiveData
{
	public long CityId { get; set; }
	public string CityName { get; set; } = string.Empty;
	public long FounderActorId { get; set; }
	public string FounderName { get; set; } = string.Empty;
	public string DaoTitle { get; set; } = string.Empty;
	public string SourceDaoTu { get; set; } = string.Empty;
	public string ManifestDaoTu { get; set; } = string.Empty;
	public string PositionType { get; set; } = string.Empty;
	public string Doctrine { get; set; } = string.Empty;
	public string LegacyDoctrine { get; set; } = string.Empty;
	public string JinXing { get; set; } = string.Empty;
	public string AuthorityScope { get; set; } = string.Empty;
	public int FoundedYear { get; set; }
}

/// <summary>
/// 登名石等跨实体恢复使用的角色高境快照。字段仍由统一数据键维护，
/// 这里仅负责稳定封装，避免旁路遗漏闰法、金性沿革与尊修进度。
/// </summary>
internal sealed class XjHighRealmActorArchiveData
{
	public int SchemaVersion { get; set; } = 2;
	public Dictionary<string, string> StringValues { get; set; } = new Dictionary<string, string>();
	public Dictionary<string, int> IntValues { get; set; } = new Dictionary<string, int>();
	public Dictionary<string, long> LongValues { get; set; } = new Dictionary<string, long>();
}
