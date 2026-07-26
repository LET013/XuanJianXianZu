namespace XuanJianVNext.Data.HighRealm;

internal static class XjJinDanYinSiState
{
	internal const string Hidden = "Hidden";
	internal const string Traced = "Traced";
	internal const string NearDiscovery = "NearDiscovery";
	internal const string Known = "Known";
	internal const string Locating = "Locating";
	internal const string Pursuing = "Pursuing";
	internal const string Evaded = "Evaded";
	internal const string Dead = "Dead";
}

internal sealed class XjJinDanImmortalityArchiveRecord
{
	public long ActorId { get; set; }
	public int ActivatedYear { get; set; }
	public float YinSiExposure { get; set; }
	public string LastExposureReason { get; set; } = string.Empty;
	public int LastExposureUpdateYear { get; set; }
	public bool YinSiKnown { get; set; }
	public string YinSiState { get; set; } = XjJinDanYinSiState.Hidden;
	public int PursuitCount { get; set; }
	public int LastKnownYear { get; set; }
	public bool IsAlive { get; set; } = true;
}
