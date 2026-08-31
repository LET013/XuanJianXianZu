using System.Collections.Generic;

namespace XuanJianVNext.Data.DengMingShi;

public sealed class XjDengMingShiFuQiSnapshot
{
	public int SavedWorldYear { get; set; }
	public Dictionary<string, string> StringValues { get; set; } = new Dictionary<string, string>();

	public Dictionary<string, int> IntValues { get; set; } = new Dictionary<string, int>();
}

internal sealed class XjDengMingShiRecord
{
	public int SchemaVersion { get; set; } = 4;

	public string CultivationPath { get; set; } = string.Empty;

	public string HighRealmPayloadKind { get; set; } = string.Empty;

	public string HighRealmDaoStateJson { get; set; } = string.Empty;

	public XjDengMingShiFuQiSnapshot FuQiState { get; set; } = new XjDengMingShiFuQiSnapshot();

	public string RecordId { get; set; } = string.Empty;

	public string ActorName { get; set; } = string.Empty;

	public long SourceActorId { get; set; }

	public int SavedYear { get; set; }

	public string RaceId { get; set; } = string.Empty;

	public string KingdomId { get; set; } = string.Empty;

	public string CultureId { get; set; } = string.Empty;

	public List<string> TraitIds { get; set; } = new List<string>();

	public string Realm { get; set; } = string.Empty;

	public string RealmId { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public int XjZz { get; set; }

	public float ZhenYuan { get; set; }

	public string GongFaName { get; set; } = string.Empty;

	public int GongFaGrade { get; set; }

	public int GongFaStage { get; set; }

	public float GongFaProgress { get; set; }

	public string GongFaDaoTu { get; set; } = string.Empty;

	public int GongFaCollectionVersion { get; set; }

	public string GongFaCollectionJson { get; set; } = string.Empty;

	public string XianJiIds { get; set; } = string.Empty;

	public int XianJiLastYear { get; set; }

	public string CaiQiFaName { get; set; } = string.Empty;

	public string CaiQiFaDaoTu { get; set; } = string.Empty;

	public string CaiQiFaSourcePlace { get; set; } = string.Empty;

	public int CaiQiFaSourceYear { get; set; }

	public string QiuJinFa { get; set; } = string.Empty;

	public string QiuJinFaSourceGongFaName { get; set; } = string.Empty;

	public int QiuJinFaSourceGongFaGrade { get; set; }

	public string QiuJinFaSourceDaoTu { get; set; } = string.Empty;

	public int QiuJinFaLastYear { get; set; }

	public string QiuJinFaBoundAuthority { get; set; } = string.Empty;

	public string JinDan { get; set; } = string.Empty;

	public string GuoWei { get; set; } = string.Empty;

	public int JinDanSuccessYear { get; set; }

	public string ShenDanGuoWei { get; set; } = string.Empty;

	public long ShenDanAnchorActorId { get; set; }

	public string ShenDanAnchorName { get; set; } = string.Empty;

	public int ShenDanYear { get; set; }

	public string FaBaoSummary { get; set; } = string.Empty;

	public string FaBaoId { get; set; } = string.Empty;

	public string FaBaoName { get; set; } = string.Empty;

	public string FaBaoDaoTu { get; set; } = string.Empty;

	public string FaBaoClass { get; set; } = string.Empty;

	public string FaBaoSource { get; set; } = string.Empty;

	public int FaBaoYear { get; set; }

	public long FamilyStableId { get; set; }

	public string FamilyName { get; set; } = string.Empty;

	public long ZongMenId { get; set; }

	public string ZongMenName { get; set; } = string.Empty;

	public int PlacedCount { get; set; }

	public int LastPlacedYear { get; set; }
}

internal sealed class XjDengMingShiArchiveData
{
	public int SchemaVersion { get; set; } = 4;

	public string CultivationPath { get; set; } = string.Empty;

	public string HighRealmPayloadKind { get; set; } = string.Empty;

	public string HighRealmDaoStateJson { get; set; } = string.Empty;

	public XjDengMingShiFuQiSnapshot FuQiState { get; set; } = new XjDengMingShiFuQiSnapshot();

	public string RecordId { get; set; } = string.Empty;

	public string ActorName { get; set; } = string.Empty;

	public long SourceActorId { get; set; }

	public int SavedYear { get; set; }

	public string RaceId { get; set; } = string.Empty;

	public string KingdomId { get; set; } = string.Empty;

	public string CultureId { get; set; } = string.Empty;

	public List<string> TraitIds { get; set; } = new List<string>();

	public string Realm { get; set; } = string.Empty;

	public string RealmId { get; set; } = string.Empty;

	public string DaoTu { get; set; } = string.Empty;

	public int XjZz { get; set; }

	public float ZhenYuan { get; set; }

	public string GongFaName { get; set; } = string.Empty;

	public int GongFaGrade { get; set; }

	public int GongFaStage { get; set; }

	public float GongFaProgress { get; set; }

	public string GongFaDaoTu { get; set; } = string.Empty;

	public int GongFaCollectionVersion { get; set; }

	public string GongFaCollectionJson { get; set; } = string.Empty;

	public string XianJiIds { get; set; } = string.Empty;

	public int XianJiLastYear { get; set; }

	public string CaiQiFaName { get; set; } = string.Empty;

	public string CaiQiFaDaoTu { get; set; } = string.Empty;

	public string CaiQiFaSourcePlace { get; set; } = string.Empty;

	public int CaiQiFaSourceYear { get; set; }

	public string QiuJinFa { get; set; } = string.Empty;

	public string QiuJinFaSourceGongFaName { get; set; } = string.Empty;

	public int QiuJinFaSourceGongFaGrade { get; set; }

	public string QiuJinFaSourceDaoTu { get; set; } = string.Empty;

	public int QiuJinFaLastYear { get; set; }

	public string QiuJinFaBoundAuthority { get; set; } = string.Empty;

	public string JinDan { get; set; } = string.Empty;

	public string GuoWei { get; set; } = string.Empty;

	public int JinDanSuccessYear { get; set; }

	public string ShenDanGuoWei { get; set; } = string.Empty;

	public long ShenDanAnchorActorId { get; set; }

	public string ShenDanAnchorName { get; set; } = string.Empty;

	public int ShenDanYear { get; set; }

	public string FaBaoSummary { get; set; } = string.Empty;

	public string FaBaoId { get; set; } = string.Empty;

	public string FaBaoName { get; set; } = string.Empty;

	public string FaBaoDaoTu { get; set; } = string.Empty;

	public string FaBaoClass { get; set; } = string.Empty;

	public string FaBaoSource { get; set; } = string.Empty;

	public int FaBaoYear { get; set; }

	public long FamilyStableId { get; set; }

	public string FamilyName { get; set; } = string.Empty;

	public long ZongMenId { get; set; }

	public string ZongMenName { get; set; } = string.Empty;

	public int PlacedCount { get; set; }

	public int LastPlacedYear { get; set; }
}
