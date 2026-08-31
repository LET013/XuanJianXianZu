namespace XuanJianVNext.Data.Events;

/// <summary>
/// 释修开局一次性状态。除前情公告外，同时保存“天下无释修时由遗经自悟”的
/// 古释首批种子窗口；该窗口每个世界最多开启一次，形成首批种子后不会因灭绝重开。
/// </summary>
internal sealed class XjShiOpeningPrologueArchiveData
{
	public int AncientSeedRuleVersion { get; set; }
	public bool Triggered { get; set; }
	public int TriggeredYear { get; set; }
	public bool AncientSeedBootstrapStarted { get; set; }
	public bool AncientSeedBootstrapClosed { get; set; }
	public int AncientSeedBootstrapYear { get; set; }
	public int AncientSeedLastManifestYear { get; set; }
	public int AncientSeedCount { get; set; }
}
