using System.Collections.Generic;

namespace XuanJianVNext.Data.Family;

internal sealed class XjFamilySurnameWorldArchiveData
{
	public int SchemaVersion { get; set; } = 1;
	public List<XjFamilySurnameArchiveRecord> Families { get; set; } = new List<XjFamilySurnameArchiveRecord>();
}

internal sealed class XjFamilySurnameArchiveRecord
{
	public long FamilyStableId { get; set; }
	public string CurrentSurname { get; set; } = string.Empty;
	public string OriginalSurname { get; set; } = string.Empty;
	public int EstablishedYear { get; set; }
	public int LastChangedYear { get; set; }
	public long SourceFamilyId { get; set; }
	public List<XjFamilySurnameChangeArchiveRecord> Changes { get; set; } = new List<XjFamilySurnameChangeArchiveRecord>();
}

internal sealed class XjFamilySurnameChangeArchiveRecord
{
	public string PreviousSurname { get; set; } = string.Empty;
	public string NewSurname { get; set; } = string.Empty;
	public int Year { get; set; }
	public string Source { get; set; } = string.Empty;
	public long InitiatorActorId { get; set; }
	public string InitiatorActorName { get; set; } = string.Empty;
}
