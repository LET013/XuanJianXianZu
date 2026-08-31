namespace XuanJianVNext.Data.Events;

internal sealed class XjOpeningLifespanEventArchiveData
{
	public bool Initialized { get; set; }
	public bool Triggered { get; set; }
	public bool LegacyBaseline { get; set; }
	public int TriggeredYear { get; set; }
}
