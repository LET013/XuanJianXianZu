using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.DongTian;

internal static class XjAdventureRealmClaimState
{
	internal const string Unclaimed = "Unclaimed";
	internal const string Discovered = "Discovered";
	internal const string ClaimWindow = "ClaimWindow";
	internal const string Guarded = "Guarded";
	internal const string Contested = "Contested";
	internal const string Resolved = "Resolved";
	internal const string Closed = "Closed";
}

internal sealed class XjAdventureRealmClaimArchiveRecord
{
	public int SchemaVersion { get; set; } = 2;
	public string RecordId { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string State { get; set; } = XjAdventureRealmClaimState.Unclaimed;
	public long AnchorCityId { get; set; }
	public string AnchorCityName { get; set; } = string.Empty;
	public int AnchorTileX { get; set; } = -1;
	public int AnchorTileY { get; set; } = -1;
	public long ClaimKingdomId { get; set; }
	public string ClaimKingdomName { get; set; } = string.Empty;
	public long ClaimSectId { get; set; }
	public string ClaimSectName { get; set; } = string.Empty;
	public long GuardianFamilyId { get; set; }
	public string GuardianFamilyName { get; set; } = string.Empty;
	public List<long> GuardianActorIds { get; set; } = new List<long>();
	public long DiscovererActorId { get; set; }
	public string DiscovererName { get; set; } = string.Empty;
	public int DiscoveredYear { get; set; }
	public int ClaimDueYear { get; set; }
	public long ContestingKingdomId { get; set; }
	public string ContestingKingdomName { get; set; } = string.Empty;
	public long ContestingSectId { get; set; }
	public string ContestingSectName { get; set; } = string.Empty;
	public List<long> ContestingActorIds { get; set; } = new List<long>();
	public int ContestStartedYear { get; set; }
	public int ContestDueYear { get; set; }
	public int ContestCooldownUntilYear { get; set; }
	public long WinnerSectId { get; set; }
	public string ContestSummary { get; set; } = string.Empty;
	public int ClosedYear { get; set; }
	public int RuntimeVersion { get; set; }
}
