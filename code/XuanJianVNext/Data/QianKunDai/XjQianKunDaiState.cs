namespace XuanJianVNext.Data.QianKunDai;

internal readonly struct XjQianKunDaiItem
{
	internal readonly string ItemId;
	internal readonly string DisplayName;
	internal readonly string Category;
	internal readonly string Source;
	internal readonly string DaoTu;
	internal readonly int Count;
	internal readonly int AcquiredYear;

	internal XjQianKunDaiItem(string itemId, string displayName, string category, string source, string daoTu, int count, int acquiredYear)
	{
		ItemId = itemId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		Category = category ?? string.Empty;
		Source = source ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		Count = count < 0 ? 0 : count;
		AcquiredYear = acquiredYear < 0 ? 0 : acquiredYear;
	}
}

internal readonly struct XjQianKunDaiState
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string CaiQiSummary;
	internal readonly string FaBaoSummary;
	internal readonly string ResourceSummary;
	internal readonly int UpdatedYear;
	internal readonly string Summary;
	internal readonly int Capacity;
	internal readonly System.Collections.Generic.IReadOnlyList<XjQianKunDaiItem> Items;

	internal XjQianKunDaiState(
		bool found,
		long actorId,
		string actorName,
		string caiQiSummary,
		string faBaoSummary,
		string resourceSummary,
		int updatedYear,
		string summary,
		int capacity = 6,
		System.Collections.Generic.IReadOnlyList<XjQianKunDaiItem> items = null)
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		CaiQiSummary = caiQiSummary ?? string.Empty;
		FaBaoSummary = faBaoSummary ?? string.Empty;
		ResourceSummary = resourceSummary ?? string.Empty;
		UpdatedYear = updatedYear < 0 ? 0 : updatedYear;
		Summary = summary ?? string.Empty;
		Capacity = capacity <= 0 ? 6 : capacity;
		Items = items ?? System.Array.Empty<XjQianKunDaiItem>();
	}
}

internal sealed class XjQianKunDaiItemArchiveData
{
	public string ItemId { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string Category { get; set; } = string.Empty;
	public string Source { get; set; } = string.Empty;
	public string DaoTu { get; set; } = string.Empty;
	public int Count { get; set; }
	public int AcquiredYear { get; set; }
}

internal sealed class XjQianKunDaiArchiveData
{
	public long ActorId { get; set; }

	public string ActorName { get; set; } = string.Empty;

	public string CaiQiSummary { get; set; } = string.Empty;

	public string FaBaoSummary { get; set; } = string.Empty;

	public string ResourceSummary { get; set; } = string.Empty;

	public int UpdatedYear { get; set; }

	public string Summary { get; set; } = string.Empty;

	public int Capacity { get; set; } = 6;

	public System.Collections.Generic.List<XjQianKunDaiItemArchiveData> Items { get; set; } = new System.Collections.Generic.List<XjQianKunDaiItemArchiveData>();
}
