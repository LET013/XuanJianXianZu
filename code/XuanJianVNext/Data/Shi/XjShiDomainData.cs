using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.Shi;

internal static class XjShiDomainTypeIds
{
	internal const string YingTu = "yingtu";
	internal const string JinDi = "jindi";
	internal const string BaoTu = "baotu";
	internal const string YouTanLin = "youtanlin";
	internal const string Zhantanlin = YouTanLin;

	internal static bool IsKnown(string value)
	{
		return string.Equals(value, YingTu, StringComparison.Ordinal)
			|| string.Equals(value, JinDi, StringComparison.Ordinal)
			|| string.Equals(value, BaoTu, StringComparison.Ordinal)
			|| string.Equals(value, YouTanLin, StringComparison.Ordinal);
	}
}

internal static class XjShiDomainVisibilityIds
{
	internal const string Manifest = "manifest";
	internal const string Hidden = "hidden";
	internal const string Unstable = "unstable";
	internal const string Absorbed = "absorbed";

	internal static bool IsKnown(string value)
	{
		return string.Equals(value, Manifest, StringComparison.Ordinal)
			|| string.Equals(value, Hidden, StringComparison.Ordinal)
			|| string.Equals(value, Unstable, StringComparison.Ordinal)
			|| string.Equals(value, Absorbed, StringComparison.Ordinal);
	}
}

internal static class XjShiDomainMigrationIds
{
	internal const string None = "none";
	internal const string MigratedJinDi = "migrated_jindi";
	internal const string MigratedYingTu = "migrated_yingtu";
	internal const string PendingAncientJinDi = "pending_ancient_jindi";
	internal const string LegacyOwnerMissing = "legacy_owner_missing";
	internal const string AncientLegacyJinDi = "ancient_legacy_jindi";
	internal const string ImportedArchive = "imported_archive";
}

internal static class XjAncientShiLegacyEventIds
{
	internal const string None = "none";
	internal const string LegacyManifest = "legacy_manifest";
	internal const string DharmaArchive = "dharma_archive";
	internal const string ResponseBodyAwakening = "response_body_awakening";

	internal static bool IsKnown(string value)
	{
		return string.Equals(value, None, StringComparison.Ordinal)
			|| string.Equals(value, LegacyManifest, StringComparison.Ordinal)
			|| string.Equals(value, DharmaArchive, StringComparison.Ordinal)
			|| string.Equals(value, ResponseBodyAwakening, StringComparison.Ordinal);
	}

	internal static string GetDisplay(string value)
	{
		if (string.Equals(value, LegacyManifest, StringComparison.Ordinal)) return "遗地现世";
		if (string.Equals(value, DharmaArchive, StringComparison.Ordinal)) return "道藏出世";
		if (string.Equals(value, ResponseBodyAwakening, StringComparison.Ordinal)) return "应身苏醒";
		return string.Empty;
	}
}

internal static class XjShiDomainCatalog
{
	internal const string YouTanLinDomainId = "shi:domain:youtanlin";
	internal const string ZhantanlinDomainId = YouTanLinDomainId;

	internal static string GetTypeDisplay(string domainType)
	{
		if (string.Equals(domainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal)) return "应土";
		if (string.Equals(domainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)) return "金地";
		if (string.Equals(domainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal)) return "宝土金地";
		if (string.Equals(domainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal)) return "旃檀林";
		return "承载地未定";
	}

	internal static string GetVisibilityDisplay(string visibility)
	{
		if (string.Equals(visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) return "显世";
		if (string.Equals(visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal)) return "隐世";
		if (string.Equals(visibility, XjShiDomainVisibilityIds.Unstable, StringComparison.Ordinal)) return "不稳定";
		if (string.Equals(visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)) return "已并入释土";
		return "状态未定";
	}

	internal static string GetMigrationDisplay(string migrationState)
	{
		if (string.Equals(migrationState, XjShiDomainMigrationIds.PendingAncientJinDi, StringComparison.Ordinal)) return "金地承载暂未明晰";
		if (string.Equals(migrationState, XjShiDomainMigrationIds.LegacyOwnerMissing, StringComparison.Ordinal)) return "释土权属暂未明晰";
		if (string.Equals(migrationState, XjShiDomainMigrationIds.AncientLegacyJinDi, StringComparison.Ordinal)) return "古释遗金地";
		return string.Empty;
	}

	internal static string GetDomainDisplayName(XjShiDomainRecord domain)
	{
		if (domain == null) return "承载地未定";
		if (!string.IsNullOrWhiteSpace(domain.DisplayName)) return domain.DisplayName.Trim();
		return GetTypeDisplay(domain.DomainType);
	}
}

public sealed class XjShiDomainRecord
{
	public string DomainId { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string DomainType { get; set; } = string.Empty;
	public string Tradition { get; set; } = string.Empty;
	public string LineageId { get; set; } = string.Empty;
	public long OwnerActorId { get; set; }
	public long HostMoHeId { get; set; }
	public string Visibility { get; set; } = XjShiDomainVisibilityIds.Hidden;
	public int Growth { get; set; }
	public int MoHePositionCapacity { get; set; }
	public int LianMinPositionBaseCapacity { get; set; }
	public int LianMinPositionCapacity { get; set; }
	public int DharmaFormPositionCapacity { get; set; }
	public int CreatedYear { get; set; }
	public int LastManifestYear { get; set; }
	public int LastHiddenYear { get; set; }
	public int UnstableSinceYear { get; set; }
	public int LastVisibilityConsequenceYear { get; set; }
	public int LastSuccessionAttemptYear { get; set; }
	public int OccupiedMoHePositions { get; set; }
	public int OccupiedLianMinPositions { get; set; }
	public int SuccessionCandidateCount { get; set; }
	public int OccupiedDharmaFormPositions { get; set; }
	public int DharmaFormCandidateCount { get; set; }
	public int LastDharmaFormClaimYear { get; set; }
	public int LastDharmaFormAttemptYear { get; set; }
	public int LastWorldHonoredAttemptYear { get; set; }
	public int AbsorbedJinDiCount { get; set; }
	public string AbsorbedByDomainId { get; set; } = string.Empty;
	public int AbsorbedYear { get; set; }
	public int LastGrowthYear { get; set; }
	public int LastAbsorptionYear { get; set; }
	public string LegacyMigrationState { get; set; } = XjShiDomainMigrationIds.None;
	public int MapCenterX { get; set; }
	public int MapCenterY { get; set; }
	public int MapRadius { get; set; }
	public int MapTerrainSchema { get; set; }
	public int LastPlacedYear { get; set; }
	public int IsNorthWorldHonoredFragment { get; set; }
	public string SourceHeavenId { get; set; } = string.Empty;
	public string SourceHeavenCategory { get; set; } = string.Empty;
	public int SourceHeavenIndex { get; set; } = -1;
	public int SourceHeavenFragmentOrdinal { get; set; }
	public int SourceHeavenFragmentCount { get; set; }
	// 古释遗金地独立事件状态。只用于已经死亡的古释自证金地，
	// 不把金地所有权迁给今释，也不把应身苏醒等价为旧主人复活。
	public int AncientLegacySinceYear { get; set; }
	public int AncientLegacyLastEventYear { get; set; }
	public int AncientLegacyEventCount { get; set; }
	public int AncientLegacyManifestUntilYear { get; set; }
	public int AncientLegacyResponseAwakened { get; set; }
	public int AncientLegacyResponseAwakenedYear { get; set; }
	public long AncientLegacyLastDiscovererActorId { get; set; }
	public string AncientLegacyFormerOwnerName { get; set; } = string.Empty;
	public string AncientLegacyLastEventId { get; set; } = XjAncientShiLegacyEventIds.None;

	internal XjShiDomainRecord Clone()
	{
		return new XjShiDomainRecord
		{
			DomainId = DomainId ?? string.Empty,
			DisplayName = DisplayName ?? string.Empty,
			DomainType = DomainType ?? string.Empty,
			Tradition = Tradition ?? string.Empty,
			LineageId = LineageId ?? string.Empty,
			OwnerActorId = Math.Max(0L, OwnerActorId),
			HostMoHeId = Math.Max(0L, HostMoHeId),
			Visibility = Visibility ?? string.Empty,
			Growth = Math.Max(0, Growth),
			MoHePositionCapacity = Math.Max(0, MoHePositionCapacity),
			LianMinPositionBaseCapacity = Math.Max(0, LianMinPositionBaseCapacity),
			LianMinPositionCapacity = Math.Max(0, LianMinPositionCapacity),
			DharmaFormPositionCapacity = Math.Max(0, DharmaFormPositionCapacity),
			CreatedYear = Math.Max(0, CreatedYear),
			LastManifestYear = Math.Max(0, LastManifestYear),
			LastHiddenYear = Math.Max(0, LastHiddenYear),
			UnstableSinceYear = Math.Max(0, UnstableSinceYear),
			LastVisibilityConsequenceYear = Math.Max(0, LastVisibilityConsequenceYear),
			LastSuccessionAttemptYear = Math.Max(0, LastSuccessionAttemptYear),
			OccupiedMoHePositions = Math.Max(0, OccupiedMoHePositions),
			OccupiedLianMinPositions = Math.Max(0, OccupiedLianMinPositions),
			SuccessionCandidateCount = Math.Max(0, SuccessionCandidateCount),
			OccupiedDharmaFormPositions = Math.Max(0, OccupiedDharmaFormPositions),
			DharmaFormCandidateCount = Math.Max(0, DharmaFormCandidateCount),
			LastDharmaFormClaimYear = Math.Max(0, LastDharmaFormClaimYear),
			LastDharmaFormAttemptYear = Math.Max(0, LastDharmaFormAttemptYear),
			LastWorldHonoredAttemptYear = Math.Max(0, LastWorldHonoredAttemptYear),
			AbsorbedJinDiCount = Math.Max(0, AbsorbedJinDiCount),
			AbsorbedByDomainId = AbsorbedByDomainId ?? string.Empty,
			AbsorbedYear = Math.Max(0, AbsorbedYear),
			LastGrowthYear = Math.Max(0, LastGrowthYear),
			LastAbsorptionYear = Math.Max(0, LastAbsorptionYear),
			LegacyMigrationState = LegacyMigrationState ?? string.Empty,
			MapCenterX = MapCenterX,
			MapCenterY = MapCenterY,
			MapRadius = Math.Max(0, MapRadius),
			MapTerrainSchema = Math.Max(0, MapTerrainSchema),
			LastPlacedYear = Math.Max(0, LastPlacedYear),
			IsNorthWorldHonoredFragment = IsNorthWorldHonoredFragment > 0 ? 1 : 0,
			SourceHeavenId = SourceHeavenId ?? string.Empty,
			SourceHeavenCategory = SourceHeavenCategory ?? string.Empty,
			SourceHeavenIndex = SourceHeavenIndex,
			SourceHeavenFragmentOrdinal = Math.Max(0, SourceHeavenFragmentOrdinal),
			SourceHeavenFragmentCount = Math.Max(0, SourceHeavenFragmentCount),
			AncientLegacySinceYear = Math.Max(0, AncientLegacySinceYear),
			AncientLegacyLastEventYear = Math.Max(0, AncientLegacyLastEventYear),
			AncientLegacyEventCount = Math.Max(0, AncientLegacyEventCount),
			AncientLegacyManifestUntilYear = Math.Max(0, AncientLegacyManifestUntilYear),
			AncientLegacyResponseAwakened = AncientLegacyResponseAwakened > 0 ? 1 : 0,
			AncientLegacyResponseAwakenedYear = Math.Max(0, AncientLegacyResponseAwakenedYear),
			AncientLegacyLastDiscovererActorId = Math.Max(0L, AncientLegacyLastDiscovererActorId),
			AncientLegacyFormerOwnerName = AncientLegacyFormerOwnerName ?? string.Empty,
			AncientLegacyLastEventId = XjAncientShiLegacyEventIds.IsKnown(AncientLegacyLastEventId)
				? AncientLegacyLastEventId : XjAncientShiLegacyEventIds.None
		};
	}
}

public sealed class XjShiDomainWorldArchiveData
{
	public int LastReconciledYear { get; set; }
	public int MigrationVersion { get; set; }
	// 度化人口闸门采用5000开启/3000关闭的滞回规则；持久化状态避免读档落在
	// 3000~4999区间时丢失上一阶段的开关语义。
	public bool DuhuaPopulationGateActive { get; set; }
	public List<XjShiDomainRecord> Domains { get; set; } = new List<XjShiDomainRecord>();
}
