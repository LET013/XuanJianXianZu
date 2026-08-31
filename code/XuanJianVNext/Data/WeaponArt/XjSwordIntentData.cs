using System;

namespace XuanJianVNext.Data.WeaponArt;

/// <summary>
/// 剑意谱中的永久记录。剑意属于创者的一己成果；创者死亡后记录仍保留，
/// 供玄鉴仙鉴展示及服气剑修的后台参悟项目选择。
/// </summary>
internal sealed class XjSwordIntentArchiveRecord
{
	public string IntentId { get; set; } = string.Empty;
	public string IntentName { get; set; } = string.Empty;
	public long CreatorActorId { get; set; }
	public string CreatorName { get; set; } = string.Empty;
	public int CreatedYear { get; set; }
	public string CreatorPath { get; set; } = string.Empty;
	public string CreatorRealm { get; set; } = string.Empty;
	public string IntentType { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;

	internal XjSwordIntentArchiveRecord Clone()
	{
		return new XjSwordIntentArchiveRecord
		{
			IntentId = IntentId ?? string.Empty,
			IntentName = IntentName ?? string.Empty,
			CreatorActorId = CreatorActorId,
			CreatorName = CreatorName ?? string.Empty,
			CreatedYear = Math.Max(0, CreatedYear),
			CreatorPath = CreatorPath ?? string.Empty,
			CreatorRealm = CreatorRealm ?? string.Empty,
			IntentType = IntentType ?? string.Empty,
			Description = Description ?? string.Empty
		};
	}
}
