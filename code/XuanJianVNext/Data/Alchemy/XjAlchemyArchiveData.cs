using System.Collections.Generic;

namespace XuanJianVNext.Data.Alchemy;

internal sealed class XjAlchemyMaterialArchiveData
{
	public int OwnerScope { get; set; }
	public long OwnerId { get; set; }
	public string MaterialId { get; set; } = string.Empty;
	public int Count { get; set; }
	public int UpdatedYear { get; set; }
}

internal sealed class XjAlchemyRecipeArchiveData
{
	public int OwnerScope { get; set; }
	public long OwnerId { get; set; }
	public string RecipeId { get; set; } = string.Empty;
	public string Source { get; set; } = string.Empty;
	public long GrantedByActorId { get; set; }
	public int AcquiredYear { get; set; }
	public bool IsPersonalGrant { get; set; }
}

internal sealed class XjAlchemyPillBatchArchiveData
{
	public string BatchId { get; set; } = string.Empty;
	public int OwnerScope { get; set; }
	public long OwnerId { get; set; }
	public string PillId { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public int Quality { get; set; }
	public long CrafterActorId { get; set; }
	public int CraftedYear { get; set; }
	public int ExpireYear { get; set; }
	public string RecipeId { get; set; } = string.Empty;
	public string TaskId { get; set; } = string.Empty;
}

internal sealed class XjAlchemyTaskArchiveData
{
	public string TaskId { get; set; } = string.Empty;
	public int OwnerScope { get; set; }
	public long OwnerId { get; set; }
	public string RecipeId { get; set; } = string.Empty;
	public long CrafterActorId { get; set; }
	public int CreatedYear { get; set; }
	public int ExpectedCompleteYear { get; set; }
	public int LastAdvancedYear { get; set; } = -1;
	public int Status { get; set; }
	public string DeterministicSeedKey { get; set; } = string.Empty;
	public bool MaterialCommitted { get; set; }
	public bool OutcomeCalculated { get; set; }
	public bool Success { get; set; }
	public bool MajorAccident { get; set; }
	public int YieldQuantity { get; set; }
	public int ProductQuality { get; set; }
	public bool Resolved { get; set; }
	public bool ProductStored { get; set; }
	public bool CrafterProgressApplied { get; set; }
	public bool HistoryWritten { get; set; }
}

internal sealed class XjAlchemyCrafterArchiveData
{
	public long ActorId { get; set; }
	public int TotalProficiency { get; set; }
	public int SuccessCount { get; set; }
	public int FailureCount { get; set; }
	public int LastAlchemyYear { get; set; }
	public string CurrentTaskId { get; set; } = string.Empty;
	public int MajorAccidentCount { get; set; }
}

internal sealed class XjAlchemyRecipeProficiencyArchiveData
{
	public long ActorId { get; set; }
	public string RecipeId { get; set; } = string.Empty;
	public int Proficiency { get; set; }
}

internal sealed class XjAlchemyArchiveBundle
{
	public List<XjAlchemyMaterialArchiveData> Materials { get; set; } = new();
	public List<XjAlchemyRecipeArchiveData> Recipes { get; set; } = new();
	public List<XjAlchemyPillBatchArchiveData> PillBatches { get; set; } = new();
	public List<XjAlchemyTaskArchiveData> Tasks { get; set; } = new();
	public List<XjAlchemyCrafterArchiveData> Crafters { get; set; } = new();
	public List<XjAlchemyRecipeProficiencyArchiveData> RecipeProficiencies { get; set; } = new();
}
