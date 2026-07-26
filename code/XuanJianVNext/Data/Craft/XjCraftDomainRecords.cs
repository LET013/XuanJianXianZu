using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.Craft;

internal static class XjCraftDomainSchema
{
	internal const int CurrentVersion = 1;
}

internal static class XjCraftProfessionIds
{
	internal const string Alchemy = "Alchemy";
	internal const string ArtifactRefining = "ArtifactRefining";
	internal const string Talisman = "Talisman";
	internal const string Formation = "Formation";
}

internal static class XjCraftOwnerScope
{
	internal const int Personal = 1;
	internal const int Family = 2;
	internal const int Sect = 3;
}

internal static class XjCraftTaskStatus
{
	internal const string Planned = "Planned";
	internal const string MaterialCommitted = "MaterialCommitted";
	internal const string InProgress = "InProgress";
	internal const string Resolved = "Resolved";
	internal const string Completed = "Completed";
	internal const string Interrupted = "Interrupted";
}

internal sealed class XjCraftTaskArchiveRecord
{
	public int SchemaVersion { get; set; } = XjCraftDomainSchema.CurrentVersion;
	public long TaskId { get; set; }
	public string ProfessionId { get; set; } = string.Empty;
	public int OwnerScope { get; set; }
	public long OwnerId { get; set; }
	public long ActorId { get; set; }
	public string BlueprintId { get; set; } = string.Empty;
	public string TaskKind { get; set; } = string.Empty;
	public int CreatedYear { get; set; }
	public int DueYear { get; set; }
	public int LastAdvancedYear { get; set; } = -1;
	public string Status { get; set; } = XjCraftTaskStatus.Planned;
	public string MaterialCommitmentId { get; set; } = string.Empty;
	public bool MaterialsCommitted { get; set; }
	public bool OutcomeCalculated { get; set; }
	public bool Success { get; set; }
	public int Quality { get; set; }
	public int Quantity { get; set; }
	public bool OutputStored { get; set; }
	public bool ProgressApplied { get; set; }
	public bool HistoryWritten { get; set; }
	public string ResolutionKey { get; set; } = string.Empty;
	public string FailureReason { get; set; } = string.Empty;

	public bool IsOpen => !string.Equals(Status, XjCraftTaskStatus.Completed, StringComparison.Ordinal)
		&& !string.Equals(Status, XjCraftTaskStatus.Interrupted, StringComparison.Ordinal);
}

internal sealed class XjCraftResourceArchiveRecord
{
	public int SchemaVersion { get; set; } = XjCraftDomainSchema.CurrentVersion;
	public int OwnerScope { get; set; }
	public long OwnerId { get; set; }
	public string ResourceId { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public int UpdatedYear { get; set; }
}

internal sealed class XjTalismanBatchArchiveRecord
{
	public int SchemaVersion { get; set; } = XjCraftDomainSchema.CurrentVersion;
	public long BatchId { get; set; }
	public int OwnerScope { get; set; }
	public long OwnerId { get; set; }
	public string TalismanId { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public int Quality { get; set; }
	public long CrafterActorId { get; set; }
	public int CraftedYear { get; set; }
	public long TaskId { get; set; }
}

internal sealed class XjTalismanCarryArchiveRecord
{
	public int SchemaVersion { get; set; } = XjCraftDomainSchema.CurrentVersion;
	public long ActorId { get; set; }
	public string TalismanId { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public int AssignedYear { get; set; }
	public string SourceBatchKey { get; set; } = string.Empty;
}

internal sealed class XjCraftArchiveBundle
{
	public List<XjCraftTaskArchiveRecord> Tasks { get; set; } = new List<XjCraftTaskArchiveRecord>();
	public List<XjCraftResourceArchiveRecord> Resources { get; set; } = new List<XjCraftResourceArchiveRecord>();
	public List<XjTalismanBatchArchiveRecord> TalismanBatches { get; set; } = new List<XjTalismanBatchArchiveRecord>();
	public List<XjTalismanCarryArchiveRecord> TalismanCarries { get; set; } = new List<XjTalismanCarryArchiveRecord>();
}
