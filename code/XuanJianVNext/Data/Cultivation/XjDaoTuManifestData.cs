namespace XuanJianVNext.Data.Cultivation;

/// <summary>
/// 只为并古九途和后起道途保存世界显现状态。常见九途开局即有传承，
/// 不需要逐条写入世界记录。
/// </summary>
internal sealed class XjDaoTuManifestArchiveData
{
	public string DaoTuRootId { get; set; } = string.Empty;
	public bool Discovered { get; set; }
	public int DiscoveredYear { get; set; }
	public long FirstDiscovererActorId { get; set; }
	public bool FuQiManifested { get; set; }
	public int FuQiManifestedYear { get; set; }
	public bool ZiJinManifested { get; set; }
	public int ZiJinManifestedYear { get; set; }
	public bool CaiQiUnlocked { get; set; }
	public int CaiQiUnlockedYear { get; set; }

	internal XjDaoTuManifestArchiveData Clone()
	{
		return new XjDaoTuManifestArchiveData
		{
			DaoTuRootId = DaoTuRootId ?? string.Empty,
			Discovered = Discovered,
			DiscoveredYear = DiscoveredYear,
			FirstDiscovererActorId = FirstDiscovererActorId,
			FuQiManifested = FuQiManifested,
			FuQiManifestedYear = FuQiManifestedYear,
			ZiJinManifested = ZiJinManifested,
			ZiJinManifestedYear = ZiJinManifestedYear,
			CaiQiUnlocked = CaiQiUnlocked,
			CaiQiUnlockedYear = CaiQiUnlockedYear
		};
	}
}
