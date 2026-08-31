using System;

namespace XuanJianVNext.Data.Formation;

internal static class XjFormationDomainSchema
{
	internal const int CurrentVersion = 1;
}

internal static class XjSectFormationBuildState
{
	internal const string Unplanned = "Unplanned";
	internal const string MaterialCommitted = "MaterialCommitted";
	internal const string Deploying = "Deploying";
	internal const string Operational = "Operational";
	internal const string Damaged = "Damaged";
	internal const string Repairing = "Repairing";
	internal const string Broken = "Broken";
}

internal sealed class XjSectFormationArchiveRecord
{
	public int SchemaVersion { get; set; } = XjFormationDomainSchema.CurrentVersion;
	public long FormationId { get; set; }
	public long SectId { get; set; }
	public string BlueprintId { get; set; } = string.Empty;
	public int Grade { get; set; }
	public string BuildState { get; set; } = XjSectFormationBuildState.Unplanned;
	public int CurrentDurability { get; set; }
	public int MaxDurability { get; set; }
	public float OccupationSpeedMultiplier { get; set; } = 1f;
	public float CompletionGate { get; set; } = 0.99f;
	public long LeadFormationMasterId { get; set; }
	public long BuildTaskId { get; set; }
	public long RepairTaskId { get; set; }
	public int RequiredMaterialGrade { get; set; }
	public int CreatedYear { get; set; }
	public int LastDamageYear { get; set; }
	public int LastRepairYear { get; set; }
	public int RuntimeVersion { get; set; }

	public bool IsOperational => Grade > 0
		&& MaxDurability > 0
		&& CurrentDurability > 0
		&& (string.Equals(BuildState, XjSectFormationBuildState.Operational, StringComparison.Ordinal)
			|| string.Equals(BuildState, XjSectFormationBuildState.Damaged, StringComparison.Ordinal)
			|| string.Equals(BuildState, XjSectFormationBuildState.Repairing, StringComparison.Ordinal));
}
