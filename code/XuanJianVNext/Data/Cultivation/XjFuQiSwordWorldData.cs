using System.Collections.Generic;

namespace XuanJianVNext.Data.Cultivation;

internal sealed class XjFuQiSwordPositionHolderArchiveData
{
	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public int AcquiredYear { get; set; }
	public int ReleasedYear { get; set; }
	public bool IsFounder { get; set; }

	internal XjFuQiSwordPositionHolderArchiveData Clone()
	{
		return new XjFuQiSwordPositionHolderArchiveData
		{
			ActorId = ActorId,
			ActorName = ActorName ?? string.Empty,
			AcquiredYear = AcquiredYear,
			ReleasedYear = ReleasedYear,
			IsFounder = IsFounder
		};
	}
}

/// <summary>
/// 无名服气剑道得到天地认可后的世界唯一状态。成功以前 DaoName 必须为空，
/// 只有首位开道者完成命名后才允许写入“长庚”。开道与当前持位分开保存，
/// 果位可在历代持有者之间空悬、承继，道途本身不会随首位开道者死亡而消失。
/// </summary>
internal sealed class XjFuQiSwordWorldArchiveData
{
	public bool Established { get; set; }
	public string DaoName { get; set; } = string.Empty;
	public string PositionRank { get; set; } = string.Empty;
	public long FounderActorId { get; set; }
	public string FounderName { get; set; } = string.Empty;
	public int EstablishedYear { get; set; }
	public long CurrentHolderActorId { get; set; }
	public int CurrentHolderAcquiredYear { get; set; }
	public int VacantSinceYear { get; set; }
	public bool SwordSteleCreated { get; set; }
	public int SwordSteleCreatedYear { get; set; }
	public long SwordSteleFounderActorId { get; set; }
	public string SwordSteleFounderName { get; set; } = string.Empty;
	public long SwordSteleCityId { get; set; }
	public string SwordSteleCityName { get; set; } = string.Empty;
	public string SwordSteleInscription { get; set; } = string.Empty;
	public int SwordSteleInsightCount { get; set; }
	public List<XjFuQiSwordPositionHolderArchiveData> HolderHistory { get; set; } = new List<XjFuQiSwordPositionHolderArchiveData>();

	internal XjFuQiSwordWorldArchiveData Clone()
	{
		return new XjFuQiSwordWorldArchiveData
		{
			Established = Established,
			DaoName = DaoName ?? string.Empty,
			PositionRank = PositionRank ?? string.Empty,
			FounderActorId = FounderActorId,
			FounderName = FounderName ?? string.Empty,
			EstablishedYear = EstablishedYear,
			CurrentHolderActorId = CurrentHolderActorId,
			CurrentHolderAcquiredYear = CurrentHolderAcquiredYear,
			VacantSinceYear = VacantSinceYear,
			SwordSteleCreated = SwordSteleCreated,
			SwordSteleCreatedYear = SwordSteleCreatedYear,
			SwordSteleFounderActorId = SwordSteleFounderActorId,
			SwordSteleFounderName = SwordSteleFounderName ?? string.Empty,
			SwordSteleCityId = SwordSteleCityId,
			SwordSteleCityName = SwordSteleCityName ?? string.Empty,
			SwordSteleInscription = SwordSteleInscription ?? string.Empty,
			SwordSteleInsightCount = SwordSteleInsightCount,
			HolderHistory = CloneHistory(HolderHistory)
		};
	}

	private static List<XjFuQiSwordPositionHolderArchiveData> CloneHistory(
		IReadOnlyList<XjFuQiSwordPositionHolderArchiveData> source)
	{
		List<XjFuQiSwordPositionHolderArchiveData> result = new List<XjFuQiSwordPositionHolderArchiveData>();
		if (source == null) return result;
		for (int i = 0; i < source.Count; i++)
		{
			if (source[i] != null) result.Add(source[i].Clone());
		}
		return result;
	}
}
