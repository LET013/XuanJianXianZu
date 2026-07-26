namespace XuanJianVNext.Data.History;

internal sealed class XjFamilyVendettaArchiveRecord
{
	public long VictimFamilyId { get; set; }
	public long VictimActorId { get; set; }
	public string VictimName { get; set; } = string.Empty;
	public long OffenderFamilyId { get; set; }
	public long OffenderActorId { get; set; }
	public string OffenderName { get; set; } = string.Empty;
	public string XianJi { get; set; } = string.Empty;
	public int CreatedYear { get; set; }
	public int LastAttemptYear { get; set; }
	public int AttemptCount { get; set; }
}
