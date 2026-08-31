using System.Collections.Generic;

namespace XuanJianVNext.Data.Shi;

/// <summary>
/// 释修高位轮回紧凑载荷。保存同一真灵的身份、位次、法脉与修持事实；
/// 摩诃主动转世仍保持摩诃身份，只推进世数，普通死亡归返则原位重塑。
/// </summary>
internal sealed class XjShiReincarnationPayload
{
	public string Tradition { get; set; } = string.Empty;
	public string PreviousRealm { get; set; } = string.Empty;
	public string PreviousSeatId { get; set; } = string.Empty;
	public float PreviousPractice { get; set; }
	public float PreviousShiMingShu { get; set; }
	public float PreviousShiMingShuPending { get; set; }
	public float PreviousOrdinaryMingShu { get; set; }
	public string LawIds { get; set; } = string.Empty;
	public string PracticeDirectionId { get; set; } = string.Empty;
	public string PracticeDirectionSource { get; set; } = string.Empty;
	public string LineageId { get; set; } = string.Empty;
	public string PatronActorId { get; set; } = string.Empty;
	public string MasterActorId { get; set; } = string.Empty;
	public string DomainId { get; set; } = string.Empty;
	public string JinDiId { get; set; } = string.Empty;
	public string RebirthAnchorId { get; set; } = string.Empty;
	public int PreviousCurrentLife { get; set; }
	public int PreviousCompletedLives { get; set; }
	public int PreviousAlignment { get; set; }
	public int PreviousConvertedCount { get; set; }
	public int PreviousDuhuaRuleVersion { get; set; }
	public int PreviousSentientConsumptionCount { get; set; }
	public int FateDirectLeapBand { get; set; }
	public bool WasFavorite { get; set; }
	public string DharmaFormStage { get; set; } = string.Empty;
	public string VowId { get; set; } = string.Empty;
	public string BaseName { get; set; } = string.Empty;
	public string HonorificTitle { get; set; } = string.Empty;
	public string DharmaName { get; set; } = string.Empty;
	public string IdentityRootActorId { get; set; } = string.Empty;
	public int ConversionSourceTier { get; set; }
	public int ConversionYear { get; set; }
	public float SourceHuiGuang { get; set; }
	public string ActorAssetId { get; set; } = string.Empty;
	public int ReturnEligibleYear { get; set; }
	public bool IsTrueSpiritReturn { get; set; }
	public int DeathYear { get; set; }
	// 摩诃直属怜愍随真灵而非肉身。把死亡前的座下名单写入轮回载荷，
	// 即使存档/重载跨过年度索引重建，也能把同一批怜愍准确接回新身。
	public List<long> DependentActorIds { get; set; } = new List<long>();
}
