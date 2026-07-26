using System;
using System.Collections.Generic;
using System.Linq;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Systems.Alchemy;

internal readonly struct XjAlchemyMaterialStack
{
	internal readonly int Count;
	internal readonly int UpdatedYear;

	internal XjAlchemyMaterialStack(int count, int updatedYear)
	{
		Count = Math.Max(0, count);
		UpdatedYear = Math.Max(0, updatedYear);
	}
}

internal readonly struct XjAlchemyRecipeHolding
{
	internal readonly string RecipeId;
	internal readonly string Source;
	internal readonly long GrantedByActorId;
	internal readonly int AcquiredYear;
	internal readonly bool IsPersonalGrant;

	internal XjAlchemyRecipeHolding(string recipeId, string source, long grantedByActorId, int acquiredYear, bool isPersonalGrant)
	{
		RecipeId = recipeId ?? string.Empty;
		Source = source ?? string.Empty;
		GrantedByActorId = Math.Max(0L, grantedByActorId);
		AcquiredYear = Math.Max(0, acquiredYear);
		IsPersonalGrant = isPersonalGrant;
	}
}

internal readonly struct XjAlchemyPillBatchState
{
	internal readonly string BatchId;
	internal readonly XjAlchemyOwnerKey Owner;
	internal readonly string PillId;
	internal readonly int Quantity;
	internal readonly XjAlchemyQuality Quality;
	internal readonly long CrafterActorId;
	internal readonly int CraftedYear;
	internal readonly int ExpireYear;
	internal readonly string RecipeId;
	internal readonly string TaskId;

	internal XjAlchemyPillBatchState(
		string batchId,
		XjAlchemyOwnerKey owner,
		string pillId,
		int quantity,
		XjAlchemyQuality quality,
		long crafterActorId,
		int craftedYear,
		int expireYear,
		string recipeId,
		string taskId)
	{
		BatchId = batchId ?? string.Empty;
		Owner = owner;
		PillId = pillId ?? string.Empty;
		Quantity = Math.Max(0, quantity);
		// 0.9.6起丹药不再区分成品品质；旧档品质统一折算为固定药效。
		Quality = XjAlchemyQuality.Normal;
		CrafterActorId = Math.Max(0L, crafterActorId);
		CraftedYear = Math.Max(0, craftedYear);
		ExpireYear = Math.Max(0, expireYear);
		RecipeId = recipeId ?? string.Empty;
		TaskId = taskId ?? string.Empty;
	}
}

internal sealed class XjAlchemyInventorySnapshot
{
	internal XjAlchemyOwnerKey Owner { get; }
	internal IReadOnlyDictionary<string, int> Materials { get; }
	internal IReadOnlyList<XjAlchemyRecipeHolding> Recipes { get; }
	internal IReadOnlyList<XjAlchemyPillBatchState> PillBatches { get; }

	internal XjAlchemyInventorySnapshot(
		XjAlchemyOwnerKey owner,
		IReadOnlyDictionary<string, int> materials,
		IReadOnlyList<XjAlchemyRecipeHolding> recipes,
		IReadOnlyList<XjAlchemyPillBatchState> pillBatches)
	{
		Owner = owner;
		Materials = materials ?? new Dictionary<string, int>();
		Recipes = recipes ?? Array.Empty<XjAlchemyRecipeHolding>();
		PillBatches = pillBatches ?? Array.Empty<XjAlchemyPillBatchState>();
	}
}

internal static class XjAlchemyInventoryRegistry
{
	private const int MaterialStackCap = 200;
	private static readonly object sync = new();
	private static readonly Dictionary<XjAlchemyOwnerKey, Dictionary<string, XjAlchemyMaterialStack>> materialsByOwner = new();
	private static readonly Dictionary<XjAlchemyOwnerKey, Dictionary<string, XjAlchemyRecipeHolding>> recipesByOwner = new();
	private static readonly Dictionary<string, XjAlchemyPillBatchState> pillBatchesById = new(StringComparer.Ordinal);
	private static readonly Dictionary<XjAlchemyOwnerKey, HashSet<string>> batchIdsByOwner = new();
	private static readonly Dictionary<string, string> batchIdByTaskAndPill = new(StringComparer.Ordinal);
	private static readonly Dictionary<XjAlchemyOwnerKey, Dictionary<string, int>> pillCountsByOwner = new();
	private static readonly Dictionary<XjAlchemyOwnerKey, int> lastPrunedYearByOwner = new();
	// 仅在 sync 锁内使用，避免每个过期库存检查都分配临时列表。
	private static readonly List<string> expiredPillBatchIds = new();

	internal static bool TryAddMaterial(XjAlchemyOwnerKey owner, string materialId, int count, int year)
	{
		if (!owner.IsValid || count <= 0
			|| !XjAlchemyCatalog.TryGetMaterial(materialId, out XjAlchemyMaterialDef definition)
			|| definition.IsLegacyAggregate) return false;
		lock (sync)
		{
			Dictionary<string, XjAlchemyMaterialStack> materials = GetOrCreateMaterialStore(owner);
			AddMaterialUnsafe(materials, materialId, count, year);
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	/// <summary>
	/// Annual gathering writes both material grades in one transaction. This avoids
	/// two global locks and two archive dirty revisions for every cultivator year.
	/// </summary>
	internal static bool TryAddAnnualMaterials(XjAlchemyOwnerKey owner, int lowCount, int highCount, int year)
	{
		lowCount = Math.Max(0, lowCount);
		highCount = Math.Max(0, highCount);
		if (!owner.IsValid || (lowCount == 0 && highCount == 0)) return false;
		string lowId = lowCount > 0
			? XjAlchemyCatalog.LowMaterialIds[PositiveModulo(owner.OwnerId + year, XjAlchemyCatalog.LowMaterialIds.Length)]
			: string.Empty;
		string highId = highCount > 0
			? XjAlchemyCatalog.HighMaterialIds[PositiveModulo(owner.OwnerId + year * 31L, XjAlchemyCatalog.HighMaterialIds.Length)]
			: string.Empty;
		return TryAddGatheredMaterials(owner, lowId, lowCount, highId, highCount, year);
	}

	/// <summary>
	/// 年度采药热路径：最多写入一种下品和一种上品药材，不创建临时 Dictionary。
	/// 两项仍在同一锁与同一次 archive dirty revision 内提交。
	/// </summary>
	internal static bool TryAddGatheredMaterials(
		XjAlchemyOwnerKey owner,
		string lowMaterialId,
		int lowCount,
		string highMaterialId,
		int highCount,
		int year)
	{
		if (!owner.IsValid) return false;
		lowCount = Math.Max(0, lowCount);
		highCount = Math.Max(0, highCount);
		bool hasLow = lowCount > 0
			&& XjAlchemyCatalog.TryGetMaterial(lowMaterialId, out XjAlchemyMaterialDef lowDefinition)
			&& !lowDefinition.IsLegacyAggregate;
		bool hasHigh = highCount > 0
			&& XjAlchemyCatalog.TryGetMaterial(highMaterialId, out XjAlchemyMaterialDef highDefinition)
			&& !highDefinition.IsLegacyAggregate;
		if (!hasLow && !hasHigh) return false;

		lock (sync)
		{
			Dictionary<string, XjAlchemyMaterialStack> materials = GetOrCreateMaterialStore(owner);
			if (hasLow) AddMaterialUnsafe(materials, lowMaterialId, lowCount, year);
			if (hasHigh) AddMaterialUnsafe(materials, highMaterialId, highCount, year);
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	/// <summary>
	/// 将一次采集得到的具体药草在同一事务内写入库存。新生产链禁止再写入抽象品阶资源。
	/// </summary>
	internal static bool TryAddGatheredMaterials(XjAlchemyOwnerKey owner, IReadOnlyDictionary<string, int> gathered, int year)
	{
		if (!owner.IsValid || gathered == null || gathered.Count == 0) return false;
		bool changed = false;
		lock (sync)
		{
			Dictionary<string, XjAlchemyMaterialStack> materials = null;
			foreach (KeyValuePair<string, int> item in gathered)
			{
				if (item.Value <= 0
					|| !XjAlchemyCatalog.TryGetMaterial(item.Key, out XjAlchemyMaterialDef definition)
					|| definition.IsLegacyAggregate) continue;
				materials ??= GetOrCreateMaterialStore(owner);
				AddMaterialUnsafe(materials, item.Key, item.Value, year);
				changed = true;
			}
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
		return changed;
	}

	internal static bool TryGetMaterialCount(XjAlchemyOwnerKey owner, string materialId, out int count)
	{
		count = 0;
		if (!owner.IsValid || string.IsNullOrWhiteSpace(materialId)) return false;
		lock (sync)
		{
			if (!materialsByOwner.TryGetValue(owner, out Dictionary<string, XjAlchemyMaterialStack> materials)
				|| !materials.TryGetValue(materialId, out XjAlchemyMaterialStack stack)) return false;
			count = stack.Count;
			return count > 0;
		}
	}

	/// <summary>
	/// 原子扣料：全部库存满足后才一次性扣减，任何一项不足都不会改变库存。
	/// </summary>
	internal static bool TryConsumeMaterialsAtomic(XjAlchemyOwnerKey owner, IReadOnlyDictionary<string, int> required)
	{
		if (!owner.IsValid || required == null || required.Count == 0) return false;
		lock (sync)
		{
			if (!materialsByOwner.TryGetValue(owner, out Dictionary<string, XjAlchemyMaterialStack> materials)) return false;
			foreach (KeyValuePair<string, int> item in required)
			{
				if (item.Value <= 0 || !XjAlchemyCatalog.TryGetMaterial(item.Key, out _)
					|| !materials.TryGetValue(item.Key, out XjAlchemyMaterialStack current) || current.Count < item.Value)
					return false;
			}

			foreach (KeyValuePair<string, int> item in required)
			{
				XjAlchemyMaterialStack current = materials[item.Key];
				int remaining = current.Count - item.Value;
				if (remaining > 0) materials[item.Key] = new XjAlchemyMaterialStack(remaining, current.UpdatedYear);
				else materials.Remove(item.Key);
			}
			if (materials.Count == 0) materialsByOwner.Remove(owner);
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool TryConsumeRecipeMaterialsAtomic(XjAlchemyOwnerKey owner, XjAlchemyRecipeDef recipe)
	{
		return owner.IsValid && recipe?.RequiredMaterials != null
			&& TryConsumeMaterialsAtomic(owner, recipe.RequiredMaterials);
	}

	internal static void RefundRecipeMaterials(XjAlchemyOwnerKey owner, XjAlchemyRecipeDef recipe, int year)
	{
		if (!owner.IsValid || recipe?.Ingredients == null || recipe.Ingredients.Count == 0) return;
		lock (sync)
		{
			Dictionary<string, XjAlchemyMaterialStack> materials = GetOrCreateMaterialStore(owner);
			for (int i = 0; i < recipe.Ingredients.Count; i++)
			{
				XjAlchemyIngredient ingredient = recipe.Ingredients[i];
				if (ingredient.Count > 0) AddMaterialUnsafe(materials, ingredient.MaterialId, ingredient.Count, year);
			}
		}
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static int GetRecipeMask(XjAlchemyOwnerKey owner)
	{
		if (!owner.IsValid) return 0;
		lock (sync)
		{
			if (!recipesByOwner.TryGetValue(owner, out Dictionary<string, XjAlchemyRecipeHolding> recipes)) return 0;
			int mask = 0;
			foreach (string recipeId in recipes.Keys) mask |= GetRecipeBit(recipeId);
			return mask;
		}
	}

	/// <summary>
	/// Copies all missing recipe holdings in one lock and returns a bit mask of the
	/// recipes actually written to the destination.
	/// </summary>
	internal static int CopyMissingRecipesTo(
		XjAlchemyOwnerKey source,
		XjAlchemyOwnerKey destination,
		string sourceLabel,
		long grantedByActorId,
		int acquiredYear)
	{
		if (!source.IsValid || !destination.IsValid || source.Equals(destination)) return 0;
		int copiedMask = 0;
		lock (sync)
		{
			if (!recipesByOwner.TryGetValue(source, out Dictionary<string, XjAlchemyRecipeHolding> sourceRecipes)
				|| sourceRecipes.Count == 0) return 0;
			if (!recipesByOwner.TryGetValue(destination, out Dictionary<string, XjAlchemyRecipeHolding> destinationRecipes))
			{
				destinationRecipes = new Dictionary<string, XjAlchemyRecipeHolding>(StringComparer.Ordinal);
				recipesByOwner[destination] = destinationRecipes;
			}
			foreach (KeyValuePair<string, XjAlchemyRecipeHolding> item in sourceRecipes)
			{
				if (destinationRecipes.ContainsKey(item.Key)) continue;
				destinationRecipes[item.Key] = new XjAlchemyRecipeHolding(
					item.Key,
					sourceLabel,
					grantedByActorId,
					acquiredYear,
					false);
				copiedMask |= GetRecipeBit(item.Key);
			}
		}
		if (copiedMask != 0) XjWorldArchiveSystem.MarkChanged();
		return copiedMask;
	}

	internal static bool TryGrantRecipe(
		XjAlchemyOwnerKey owner,
		string recipeId,
		string source,
		long grantedByActorId,
		int acquiredYear,
		bool isPersonalGrant)
	{
		if (!owner.IsValid || !XjAlchemyCatalog.TryGetRecipe(recipeId, out _)) return false;
		lock (sync)
		{
			if (!recipesByOwner.TryGetValue(owner, out Dictionary<string, XjAlchemyRecipeHolding> recipes))
			{
				recipes = new Dictionary<string, XjAlchemyRecipeHolding>(StringComparer.Ordinal);
				recipesByOwner[owner] = recipes;
			}
			if (recipes.ContainsKey(recipeId)) return false;
			recipes[recipeId] = new XjAlchemyRecipeHolding(recipeId, source, grantedByActorId, acquiredYear, isPersonalGrant);
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool HasRecipe(XjAlchemyOwnerKey owner, string recipeId)
	{
		if (!owner.IsValid || string.IsNullOrWhiteSpace(recipeId)) return false;
		lock (sync)
		{
			return recipesByOwner.TryGetValue(owner, out Dictionary<string, XjAlchemyRecipeHolding> recipes)
				&& recipes.ContainsKey(recipeId);
		}
	}

	internal static bool TryAddPillBatch(XjAlchemyPillBatchState batch)
	{
		if (!IsValidBatch(batch)) return false;
		lock (sync)
		{
			if (pillBatchesById.TryGetValue(batch.BatchId, out XjAlchemyPillBatchState existing))
				return AreSameBatch(existing, batch);

			string taskKey = BuildTaskPillKey(batch.TaskId, batch.PillId);
			if (!string.IsNullOrEmpty(taskKey) && batchIdByTaskAndPill.TryGetValue(taskKey, out string existingBatchId))
			{
				return pillBatchesById.TryGetValue(existingBatchId, out existing) && AreSameBatch(existing, batch);
			}

			pillBatchesById[batch.BatchId] = batch;
			AddPillCountUnsafe(batch.Owner, batch.PillId, batch.Quantity);
			lastPrunedYearByOwner.Remove(batch.Owner);
			if (!batchIdsByOwner.TryGetValue(batch.Owner, out HashSet<string> ids))
			{
				ids = new HashSet<string>(StringComparer.Ordinal);
				batchIdsByOwner[batch.Owner] = ids;
			}
			ids.Add(batch.BatchId);
			if (!string.IsNullOrEmpty(taskKey)) batchIdByTaskAndPill[taskKey] = batch.BatchId;
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static XjAlchemyInventorySnapshot ReadSnapshot(XjAlchemyOwnerKey owner)
	{
		lock (sync)
		{
			Dictionary<string, int> materialCopy = new(StringComparer.Ordinal);
			if (materialsByOwner.TryGetValue(owner, out Dictionary<string, XjAlchemyMaterialStack> materials))
			{
				foreach (KeyValuePair<string, XjAlchemyMaterialStack> item in materials)
					if (item.Value.Count > 0) materialCopy[item.Key] = item.Value.Count;
			}
			List<XjAlchemyRecipeHolding> recipeCopy = recipesByOwner.TryGetValue(owner, out Dictionary<string, XjAlchemyRecipeHolding> recipes)
				? recipes.Values.OrderBy(value => value.RecipeId, StringComparer.Ordinal).ToList()
				: new List<XjAlchemyRecipeHolding>();
			List<XjAlchemyPillBatchState> batchCopy = new();
			if (batchIdsByOwner.TryGetValue(owner, out HashSet<string> ids))
			{
				foreach (string id in ids)
					if (pillBatchesById.TryGetValue(id, out XjAlchemyPillBatchState batch)) batchCopy.Add(batch);
				batchCopy.Sort((left, right) => string.Compare(left.BatchId, right.BatchId, StringComparison.Ordinal));
			}
			return new XjAlchemyInventorySnapshot(owner, materialCopy, recipeCopy, batchCopy);
		}
	}

	internal static bool TransferSectInventory(
		long sourceSectId,
		long targetSectId,
		int currentYear,
		out int materialStacks,
		out int materialQuantity,
		out int recipeCount,
		out int pillBatches,
		out int pillQuantity)
	{
		materialStacks = 0;
		materialQuantity = 0;
		recipeCount = 0;
		pillBatches = 0;
		pillQuantity = 0;
		if (sourceSectId <= 0L || targetSectId <= 0L || sourceSectId == targetSectId) return false;
		XjAlchemyOwnerKey source = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.ZongMen, sourceSectId);
		XjAlchemyOwnerKey target = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.ZongMen, targetSectId);
		bool changed = false;
		lock (sync)
		{
			if (materialsByOwner.TryGetValue(source, out Dictionary<string, XjAlchemyMaterialStack> sourceMaterials))
			{
				Dictionary<string, XjAlchemyMaterialStack> targetMaterials = GetOrCreateMaterialStore(target);
				List<string> materialIds = sourceMaterials.Keys.ToList();
				for (int i = 0; i < materialIds.Count; i++)
				{
					string materialId = materialIds[i];
					if (!sourceMaterials.TryGetValue(materialId, out XjAlchemyMaterialStack sourceStack) || sourceStack.Count <= 0) continue;
					targetMaterials.TryGetValue(materialId, out XjAlchemyMaterialStack existing);
					int accepted = Math.Min(sourceStack.Count, Math.Max(0, MaterialStackCap - existing.Count));
					if (accepted <= 0) continue;
					targetMaterials[materialId] = new XjAlchemyMaterialStack(
						existing.Count + accepted,
						Math.Max(Math.Max(0, currentYear), Math.Max(existing.UpdatedYear, sourceStack.UpdatedYear)));
					ConsumeMaterialUnsafe(sourceMaterials, materialId, accepted);
					materialStacks++;
					materialQuantity += accepted;
					changed = true;
				}
				if (sourceMaterials.Count == 0) materialsByOwner.Remove(source);
			}

			if (recipesByOwner.TryGetValue(source, out Dictionary<string, XjAlchemyRecipeHolding> sourceRecipes))
			{
				if (!recipesByOwner.TryGetValue(target, out Dictionary<string, XjAlchemyRecipeHolding> targetRecipes))
				{
					targetRecipes = new Dictionary<string, XjAlchemyRecipeHolding>(StringComparer.Ordinal);
					recipesByOwner[target] = targetRecipes;
				}
				foreach (KeyValuePair<string, XjAlchemyRecipeHolding> pair in sourceRecipes)
				{
					if (targetRecipes.ContainsKey(pair.Key)) continue;
					targetRecipes[pair.Key] = new XjAlchemyRecipeHolding(
						pair.Value.RecipeId,
						"宗门战利",
						pair.Value.GrantedByActorId,
						Math.Max(Math.Max(0, currentYear), pair.Value.AcquiredYear),
						false);
					recipeCount++;
					changed = true;
				}
				recipesByOwner.Remove(source);
			}

			if (batchIdsByOwner.TryGetValue(source, out HashSet<string> sourceBatchIds))
			{
				if (!batchIdsByOwner.TryGetValue(target, out HashSet<string> targetBatchIds))
				{
					targetBatchIds = new HashSet<string>(StringComparer.Ordinal);
					batchIdsByOwner[target] = targetBatchIds;
				}
				foreach (string batchId in sourceBatchIds)
				{
					if (!pillBatchesById.TryGetValue(batchId, out XjAlchemyPillBatchState batch) || batch.Quantity <= 0) continue;
					string oldTaskKey = BuildTaskPillKey(batch.TaskId, batch.PillId);
					if (!string.IsNullOrEmpty(oldTaskKey)) batchIdByTaskAndPill.Remove(oldTaskKey);
					pillBatchesById[batchId] = new XjAlchemyPillBatchState(
						batch.BatchId, target, batch.PillId, batch.Quantity, batch.Quality, batch.CrafterActorId,
						batch.CraftedYear, batch.ExpireYear, batch.RecipeId, string.Empty);
					targetBatchIds.Add(batchId);
					pillBatches++;
					pillQuantity += batch.Quantity;
					changed = true;
				}
				batchIdsByOwner.Remove(source);
				pillCountsByOwner.Remove(source);
				RebuildPillCountsForOwnerUnsafe(target);
			}
			lastPrunedYearByOwner.Remove(source);
			lastPrunedYearByOwner.Remove(target);
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
		return changed;
	}


	internal static bool TryMoveMaterial(
		XjAlchemyOwnerKey source,
		XjAlchemyOwnerKey destination,
		string materialId,
		int count,
		int year)
	{
		if (!source.IsValid || !destination.IsValid || source.Equals(destination) || count <= 0
			|| !XjAlchemyCatalog.TryGetMaterial(materialId, out _)) return false;
		lock (sync)
		{
			if (!materialsByOwner.TryGetValue(source, out Dictionary<string, XjAlchemyMaterialStack> sourceStore)
				|| !sourceStore.TryGetValue(materialId, out XjAlchemyMaterialStack current)
				|| current.Count <= 0) return false;
			Dictionary<string, XjAlchemyMaterialStack> destinationStore = GetOrCreateMaterialStore(destination);
			destinationStore.TryGetValue(materialId, out XjAlchemyMaterialStack destinationCurrent);
			int accepted = Math.Min(Math.Min(count, current.Count), Math.Max(0, MaterialStackCap - destinationCurrent.Count));
			if (accepted <= 0) return false;
			ConsumeMaterialUnsafe(sourceStore, materialId, accepted);
			if (sourceStore.Count == 0) materialsByOwner.Remove(source);
			destinationStore[materialId] = new XjAlchemyMaterialStack(
				destinationCurrent.Count + accepted,
				year > 0 ? year : Math.Max(current.UpdatedYear, destinationCurrent.UpdatedYear));
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	/// <summary>
	/// Moves both material grades above the personal reserve in one transaction.
	/// </summary>
	internal static bool TryTransferSurplusToFamily(
		XjAlchemyOwnerKey source,
		XjAlchemyOwnerKey destination,
		int lowReserve,
		int highReserve,
		int year)
	{
		if (!source.IsValid || !destination.IsValid || source.Equals(destination)) return false;
		bool changed = false;
		lock (sync)
		{
			if (!materialsByOwner.TryGetValue(source, out Dictionary<string, XjAlchemyMaterialStack> sourceStore)) return false;
			Dictionary<string, XjAlchemyMaterialStack> destinationStore = null;
			changed |= MoveGradeSurplusUnsafe(sourceStore, ref destinationStore, destination, XjAlchemyMaterialGrade.Low, Math.Max(0, lowReserve), year);
			changed |= MoveGradeSurplusUnsafe(sourceStore, ref destinationStore, destination, XjAlchemyMaterialGrade.High, Math.Max(0, highReserve), year);
			if (sourceStore.Count == 0) materialsByOwner.Remove(source);
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
		return changed;
	}

	internal static int PruneExpiredPills(XjAlchemyOwnerKey owner, int currentYear)
	{
		if (!owner.IsValid || currentYear <= 0) return 0;
		int removed = 0;
		lock (sync)
		{
			if (!batchIdsByOwner.TryGetValue(owner, out HashSet<string> ids) || ids.Count == 0)
			{
				lastPrunedYearByOwner.Remove(owner);
				return 0;
			}
			if (lastPrunedYearByOwner.TryGetValue(owner, out int prunedYear) && prunedYear >= currentYear)
				return 0;
			lastPrunedYearByOwner[owner] = currentYear;
			expiredPillBatchIds.Clear();
			foreach (string id in ids)
			{
				if (pillBatchesById.TryGetValue(id, out XjAlchemyPillBatchState batch)
					&& batch.ExpireYear > 0 && batch.ExpireYear <= currentYear)
				{
					expiredPillBatchIds.Add(id);
				}
			}
			if (expiredPillBatchIds.Count == 0) return 0;
			for (int i = 0; i < expiredPillBatchIds.Count; i++)
			{
				string id = expiredPillBatchIds[i];
				if (!pillBatchesById.TryGetValue(id, out XjAlchemyPillBatchState batch)) continue;
				removed += batch.Quantity;
				pillBatchesById.Remove(id);
				ids.Remove(id);
				RemovePillCountUnsafe(owner, batch.PillId, batch.Quantity);
				string taskKey = BuildTaskPillKey(batch.TaskId, batch.PillId);
				if (!string.IsNullOrEmpty(taskKey)) batchIdByTaskAndPill.Remove(taskKey);
			}
			if (ids.Count == 0)
			{
				batchIdsByOwner.Remove(owner);
				lastPrunedYearByOwner.Remove(owner);
			}
			expiredPillBatchIds.Clear();
		}
		if (removed > 0) XjWorldArchiveSystem.MarkChanged();
		return removed;
	}

	internal static bool HasAnyPill(XjAlchemyOwnerKey owner, int currentYear)
	{
		if (!owner.IsValid) return false;
		if (currentYear > 0) PruneExpiredPills(owner, currentYear);
		lock (sync)
		{
			return pillCountsByOwner.TryGetValue(owner, out Dictionary<string, int> counts)
				&& counts.Count > 0;
		}
	}

	/// <summary>
	/// 非破坏性库存门禁。用于在构建修炼快照或读取生命值前排除绝大多数无丹药角色。
	/// </summary>
	internal static bool HasPill(XjAlchemyOwnerKey owner, string pillId, int currentYear)
	{
		if (!owner.IsValid || string.IsNullOrWhiteSpace(pillId)) return false;
		if (currentYear > 0) PruneExpiredPills(owner, currentYear);
		lock (sync)
		{
			return pillCountsByOwner.TryGetValue(owner, out Dictionary<string, int> counts)
				&& counts.TryGetValue(pillId, out int count)
				&& count > 0;
		}
	}

	internal static int GetPillCount(XjAlchemyOwnerKey owner, string pillId, int currentYear)
	{
		if (!owner.IsValid || string.IsNullOrWhiteSpace(pillId)) return 0;
		if (currentYear > 0) PruneExpiredPills(owner, currentYear);
		lock (sync)
		{
			return pillCountsByOwner.TryGetValue(owner, out Dictionary<string, int> counts)
				&& counts.TryGetValue(pillId, out int count)
				? Math.Max(0, count)
				: 0;
		}
	}

	/// <summary>
	/// 消耗一枚最早失效的指定丹药。品质参数仅为旧存档兼容，当前固定返回 Normal。
	/// </summary>
	internal static bool TryConsumeOnePill(XjAlchemyOwnerKey owner, string pillId, int currentYear, out XjAlchemyQuality quality)
	{
		quality = XjAlchemyQuality.Normal;
		if (!owner.IsValid || string.IsNullOrWhiteSpace(pillId) || !XjAlchemyCatalog.TryGetPill(pillId, out _)) return false;
		if (currentYear > 0) PruneExpiredPills(owner, currentYear);
		bool changed = false;
		lock (sync)
		{
			if (!pillCountsByOwner.TryGetValue(owner, out Dictionary<string, int> counts)
				|| !counts.TryGetValue(pillId, out int available)
				|| available <= 0) return false;
			if (!batchIdsByOwner.TryGetValue(owner, out HashSet<string> ids) || ids.Count == 0) return false;
			string selectedId = null;
			XjAlchemyPillBatchState selected = default;
			foreach (string id in ids)
			{
				if (!pillBatchesById.TryGetValue(id, out XjAlchemyPillBatchState batch)
					|| batch.Quantity <= 0
					|| !string.Equals(batch.PillId, pillId, StringComparison.Ordinal)) continue;
				if (selectedId == null
					|| batch.ExpireYear < selected.ExpireYear
					|| (batch.ExpireYear == selected.ExpireYear && string.Compare(id, selectedId, StringComparison.Ordinal) < 0))
				{
					selectedId = id;
					selected = batch;
				}
			}
			if (selectedId == null) return false;
			quality = XjAlchemyQuality.Normal;
			if (selected.Quantity > 1)
			{
				pillBatchesById[selectedId] = new XjAlchemyPillBatchState(
					selected.BatchId, selected.Owner, selected.PillId, selected.Quantity - 1, selected.Quality,
					selected.CrafterActorId, selected.CraftedYear, selected.ExpireYear, selected.RecipeId, selected.TaskId);
			}
			else
			{
				pillBatchesById.Remove(selectedId);
				ids.Remove(selectedId);
				if (ids.Count == 0) batchIdsByOwner.Remove(owner);
				string taskKey = BuildTaskPillKey(selected.TaskId, selected.PillId);
				if (!string.IsNullOrEmpty(taskKey)) batchIdByTaskAndPill.Remove(taskKey);
			}
			RemovePillCountUnsafe(owner, pillId, 1);
			changed = true;
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
		return changed;
	}

	internal static bool TransferAll(XjAlchemyOwnerKey source, XjAlchemyOwnerKey destination, int year)
	{
		if (!source.IsValid || !destination.IsValid || source.Equals(destination)) return false;
		bool changed = false;
		lock (sync)
		{
			if (materialsByOwner.TryGetValue(source, out Dictionary<string, XjAlchemyMaterialStack> sourceMaterials))
			{
				Dictionary<string, XjAlchemyMaterialStack> destinationMaterials = GetOrCreateMaterialStore(destination);
				List<string> materialIds = sourceMaterials.Keys.ToList();
				for (int i = 0; i < materialIds.Count; i++)
				{
					string materialId = materialIds[i];
					if (!sourceMaterials.TryGetValue(materialId, out XjAlchemyMaterialStack sourceStack) || sourceStack.Count <= 0) continue;
					destinationMaterials.TryGetValue(materialId, out XjAlchemyMaterialStack existing);
					int accepted = Math.Min(sourceStack.Count, Math.Max(0, MaterialStackCap - existing.Count));
					if (accepted <= 0) continue;
					destinationMaterials[materialId] = new XjAlchemyMaterialStack(
						existing.Count + accepted,
						year > 0 ? year : Math.Max(existing.UpdatedYear, sourceStack.UpdatedYear));
					ConsumeMaterialUnsafe(sourceMaterials, materialId, accepted);
					changed = true;
				}
				if (sourceMaterials.Count == 0) materialsByOwner.Remove(source);
			}
			if (recipesByOwner.TryGetValue(source, out Dictionary<string, XjAlchemyRecipeHolding> sourceRecipes))
			{
				if (!recipesByOwner.TryGetValue(destination, out Dictionary<string, XjAlchemyRecipeHolding> destinationRecipes))
				{
					destinationRecipes = new Dictionary<string, XjAlchemyRecipeHolding>(StringComparer.Ordinal);
					recipesByOwner[destination] = destinationRecipes;
				}
				foreach (KeyValuePair<string, XjAlchemyRecipeHolding> item in sourceRecipes)
					if (!destinationRecipes.ContainsKey(item.Key)) destinationRecipes[item.Key] = item.Value;
				recipesByOwner.Remove(source);
				changed = true;
			}
			if (batchIdsByOwner.TryGetValue(source, out HashSet<string> sourceBatchIds))
			{
				if (!batchIdsByOwner.TryGetValue(destination, out HashSet<string> destinationBatchIds))
				{
					destinationBatchIds = new HashSet<string>(StringComparer.Ordinal);
					batchIdsByOwner[destination] = destinationBatchIds;
				}
				foreach (string id in sourceBatchIds)
				{
					if (!pillBatchesById.TryGetValue(id, out XjAlchemyPillBatchState batch)) continue;
					pillBatchesById[id] = new XjAlchemyPillBatchState(
						batch.BatchId, destination, batch.PillId, batch.Quantity, batch.Quality,
						batch.CrafterActorId, batch.CraftedYear, batch.ExpireYear, batch.RecipeId, batch.TaskId);
					destinationBatchIds.Add(id);
				}
				batchIdsByOwner.Remove(source);
				RebuildPillCountsForOwnerUnsafe(source);
				RebuildPillCountsForOwnerUnsafe(destination);
				lastPrunedYearByOwner.Remove(source);
				lastPrunedYearByOwner.Remove(destination);
				changed = true;
			}
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
		return changed;
	}

	internal static bool RemoveAll(XjAlchemyOwnerKey owner)
	{
		if (!owner.IsValid) return false;
		bool changed = false;
		lock (sync)
		{
			changed |= materialsByOwner.Remove(owner);
			changed |= recipesByOwner.Remove(owner);
			changed |= pillCountsByOwner.Remove(owner);
			lastPrunedYearByOwner.Remove(owner);
			if (batchIdsByOwner.TryGetValue(owner, out HashSet<string> ids))
			{
				foreach (string id in ids)
				{
					if (!pillBatchesById.TryGetValue(id, out XjAlchemyPillBatchState batch)) continue;
					pillBatchesById.Remove(id);
					string taskKey = BuildTaskPillKey(batch.TaskId, batch.PillId);
					if (!string.IsNullOrEmpty(taskKey)) batchIdByTaskAndPill.Remove(taskKey);
				}
				batchIdsByOwner.Remove(owner);
				changed = true;
			}
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
		return changed;
	}

	internal static void ExportArchiveRecords(
		List<XjAlchemyMaterialArchiveData> materialRecords,
		List<XjAlchemyRecipeArchiveData> recipeRecords,
		List<XjAlchemyPillBatchArchiveData> pillBatchRecords)
	{
		if (materialRecords == null || recipeRecords == null || pillBatchRecords == null) return;
		lock (sync)
		{
			foreach (KeyValuePair<XjAlchemyOwnerKey, Dictionary<string, XjAlchemyMaterialStack>> ownerEntry in materialsByOwner
				.OrderBy(entry => (int)entry.Key.Scope).ThenBy(entry => entry.Key.OwnerId))
			{
				foreach (KeyValuePair<string, XjAlchemyMaterialStack> item in ownerEntry.Value.OrderBy(entry => entry.Key, StringComparer.Ordinal))
				{
					if (item.Value.Count <= 0) continue;
					materialRecords.Add(new XjAlchemyMaterialArchiveData
					{
						OwnerScope = (int)ownerEntry.Key.Scope,
						OwnerId = ownerEntry.Key.OwnerId,
						MaterialId = item.Key,
						Count = item.Value.Count,
						UpdatedYear = item.Value.UpdatedYear
					});
				}
			}

			foreach (KeyValuePair<XjAlchemyOwnerKey, Dictionary<string, XjAlchemyRecipeHolding>> ownerEntry in recipesByOwner
				.OrderBy(entry => (int)entry.Key.Scope).ThenBy(entry => entry.Key.OwnerId))
			{
				foreach (XjAlchemyRecipeHolding holding in ownerEntry.Value.Values.OrderBy(value => value.RecipeId, StringComparer.Ordinal))
				{
					recipeRecords.Add(new XjAlchemyRecipeArchiveData
					{
						OwnerScope = (int)ownerEntry.Key.Scope,
						OwnerId = ownerEntry.Key.OwnerId,
						RecipeId = holding.RecipeId,
						Source = holding.Source,
						GrantedByActorId = holding.GrantedByActorId,
						AcquiredYear = holding.AcquiredYear,
						IsPersonalGrant = holding.IsPersonalGrant
					});
				}
			}

			foreach (XjAlchemyPillBatchState batch in pillBatchesById.Values.OrderBy(value => value.BatchId, StringComparer.Ordinal))
			{
				pillBatchRecords.Add(new XjAlchemyPillBatchArchiveData
				{
					BatchId = batch.BatchId,
					OwnerScope = (int)batch.Owner.Scope,
					OwnerId = batch.Owner.OwnerId,
					PillId = batch.PillId,
					Quantity = batch.Quantity,
					Quality = (int)batch.Quality,
					CrafterActorId = batch.CrafterActorId,
					CraftedYear = batch.CraftedYear,
					ExpireYear = batch.ExpireYear,
					RecipeId = batch.RecipeId,
					TaskId = batch.TaskId
				});
			}
		}
	}

	internal static void ImportArchiveRecords(
		IEnumerable<XjAlchemyMaterialArchiveData> materialRecords,
		IEnumerable<XjAlchemyRecipeArchiveData> recipeRecords,
		IEnumerable<XjAlchemyPillBatchArchiveData> pillBatchRecords)
	{
		lock (sync)
		{
			ClearUnsafe();
			if (materialRecords != null)
			{
				foreach (XjAlchemyMaterialArchiveData record in materialRecords)
				{
					XjAlchemyOwnerKey owner = ParseOwner(record?.OwnerScope ?? 0, record?.OwnerId ?? 0L);
					if (record == null || !owner.IsValid || record.Count <= 0 || !XjAlchemyCatalog.TryGetMaterial(record.MaterialId, out XjAlchemyMaterialDef definition)) continue;
					Dictionary<string, XjAlchemyMaterialStack> store = GetOrCreateMaterialStore(owner);
					if (definition.IsLegacyAggregate)
					{
						IReadOnlyList<string> ids = definition.Grade == XjAlchemyMaterialGrade.High
							? XjAlchemyCatalog.CoreHighMaterialIds : XjAlchemyCatalog.CoreLowMaterialIds;
						DistributeLegacyMaterialUnsafe(store, ids, record.Count, record.UpdatedYear, owner.OwnerId);
					}
					else AddMaterialUnsafe(store, record.MaterialId, record.Count, record.UpdatedYear);
				}
			}

			if (recipeRecords != null)
			{
				foreach (XjAlchemyRecipeArchiveData record in recipeRecords)
				{
					XjAlchemyOwnerKey owner = ParseOwner(record?.OwnerScope ?? 0, record?.OwnerId ?? 0L);
					string recipeId = NormalizeLegacyRecipeId(record?.RecipeId);
					if (record == null || !owner.IsValid || !XjAlchemyCatalog.TryGetRecipe(recipeId, out _)) continue;
					if (!recipesByOwner.TryGetValue(owner, out Dictionary<string, XjAlchemyRecipeHolding> recipes))
					{
						recipes = new Dictionary<string, XjAlchemyRecipeHolding>(StringComparer.Ordinal);
						recipesByOwner[owner] = recipes;
					}
					recipes[recipeId] = new XjAlchemyRecipeHolding(recipeId, record.Source,
						record.GrantedByActorId, record.AcquiredYear, record.IsPersonalGrant);
				}
			}

			if (pillBatchRecords != null)
			{
				foreach (XjAlchemyPillBatchArchiveData record in pillBatchRecords)
				{
					if (record == null) continue;
					string pillId = NormalizeLegacyPillId(record.PillId);
					string recipeId = NormalizeLegacyRecipeId(record.RecipeId);
					XjAlchemyPillBatchState batch = new(
						record.BatchId,
						ParseOwner(record.OwnerScope, record.OwnerId),
						pillId,
						record.Quantity,
						Enum.IsDefined(typeof(XjAlchemyQuality), record.Quality) ? (XjAlchemyQuality)record.Quality : XjAlchemyQuality.Normal,
						record.CrafterActorId,
						record.CraftedYear,
						record.ExpireYear,
						recipeId,
						record.TaskId);
					if (!IsValidBatch(batch) || pillBatchesById.ContainsKey(batch.BatchId)) continue;
					pillBatchesById[batch.BatchId] = batch;
					AddPillCountUnsafe(batch.Owner, batch.PillId, batch.Quantity);
					if (!batchIdsByOwner.TryGetValue(batch.Owner, out HashSet<string> ids))
					{
						ids = new HashSet<string>(StringComparer.Ordinal);
						batchIdsByOwner[batch.Owner] = ids;
					}
					ids.Add(batch.BatchId);
					string taskKey = BuildTaskPillKey(batch.TaskId, batch.PillId);
					if (!string.IsNullOrEmpty(taskKey) && !batchIdByTaskAndPill.ContainsKey(taskKey))
						batchIdByTaskAndPill[taskKey] = batch.BatchId;
				}
			}
		}
	}

	private static string NormalizeLegacyPillId(string pillId)
	{
		return string.Equals(pillId, XjAlchemyCatalog.PillYuLuHuiMing, StringComparison.Ordinal)
			? XjAlchemyCatalog.PillTianYiTuCui
			: pillId ?? string.Empty;
	}

	private static string NormalizeLegacyRecipeId(string recipeId)
	{
		return string.Equals(recipeId, XjAlchemyCatalog.RecipeYuLuHuiMing, StringComparison.Ordinal)
			? XjAlchemyCatalog.RecipeTianYiTuCui
			: recipeId ?? string.Empty;
	}

	internal static void Clear()
	{
		lock (sync) ClearUnsafe();
	}

	private static Dictionary<string, XjAlchemyMaterialStack> GetOrCreateMaterialStore(XjAlchemyOwnerKey owner)
	{
		if (!materialsByOwner.TryGetValue(owner, out Dictionary<string, XjAlchemyMaterialStack> materials))
		{
			materials = new Dictionary<string, XjAlchemyMaterialStack>(StringComparer.Ordinal);
			materialsByOwner[owner] = materials;
		}
		return materials;
	}

	private static void AddMaterialUnsafe(Dictionary<string, XjAlchemyMaterialStack> materials, string materialId, int count, int year)
	{
		if (materials == null || count <= 0 || string.IsNullOrWhiteSpace(materialId)) return;
		materials.TryGetValue(materialId, out XjAlchemyMaterialStack current);
		long next = Math.Min(MaterialStackCap, (long)current.Count + count);
		if (next <= current.Count) return;
		materials[materialId] = new XjAlchemyMaterialStack(
			(int)next,
			year > 0 ? year : current.UpdatedYear);
	}

	private static void ConsumeMaterialUnsafe(Dictionary<string, XjAlchemyMaterialStack> materials, string materialId, int count)
	{
		if (materials == null || count <= 0 || !materials.TryGetValue(materialId, out XjAlchemyMaterialStack current)) return;
		int remaining = current.Count - count;
		if (remaining > 0) materials[materialId] = new XjAlchemyMaterialStack(remaining, current.UpdatedYear);
		else materials.Remove(materialId);
	}

	private static bool MoveSurplusUnsafe(
		Dictionary<string, XjAlchemyMaterialStack> sourceStore,
		ref Dictionary<string, XjAlchemyMaterialStack> destinationStore,
		XjAlchemyOwnerKey destination,
		string materialId,
		int reserve,
		int year)
	{
		if (!sourceStore.TryGetValue(materialId, out XjAlchemyMaterialStack current) || current.Count <= reserve) return false;
		destinationStore ??= GetOrCreateMaterialStore(destination);
		destinationStore.TryGetValue(materialId, out XjAlchemyMaterialStack destinationCurrent);
		int moved = Math.Min(current.Count - reserve, Math.Max(0, MaterialStackCap - destinationCurrent.Count));
		if (moved <= 0) return false;
		ConsumeMaterialUnsafe(sourceStore, materialId, moved);
		AddMaterialUnsafe(destinationStore, materialId, moved, year);
		return true;
	}

	private static bool MoveGradeSurplusUnsafe(
		Dictionary<string, XjAlchemyMaterialStack> sourceStore,
		ref Dictionary<string, XjAlchemyMaterialStack> destinationStore,
		XjAlchemyOwnerKey destination,
		XjAlchemyMaterialGrade grade,
		int reserve,
		int year)
	{
		List<string> ids = new();
		int total = 0;
		foreach (KeyValuePair<string, XjAlchemyMaterialStack> item in sourceStore)
		{
			if (item.Value.Count <= 0 || !XjAlchemyCatalog.TryGetMaterial(item.Key, out XjAlchemyMaterialDef definition)
				|| definition.Grade != grade) continue;
			ids.Add(item.Key);
			total += item.Value.Count;
		}
		if (total <= reserve) return false;
		ids.Sort(StringComparer.Ordinal);
		int moveRemaining = total - reserve;
		for (int i = ids.Count - 1; i >= 0 && moveRemaining > 0; i--)
		{
			string id = ids[i];
			XjAlchemyMaterialStack current = sourceStore[id];
			destinationStore ??= GetOrCreateMaterialStore(destination);
			destinationStore.TryGetValue(id, out XjAlchemyMaterialStack destinationCurrent);
			int moved = Math.Min(Math.Min(current.Count, moveRemaining), Math.Max(0, MaterialStackCap - destinationCurrent.Count));
			if (moved <= 0) continue;
			ConsumeMaterialUnsafe(sourceStore, id, moved);
			AddMaterialUnsafe(destinationStore, id, moved, year);
			moveRemaining -= moved;
		}
		return true;
	}

	private static void DistributeLegacyMaterialUnsafe(
		Dictionary<string, XjAlchemyMaterialStack> store,
		IReadOnlyList<string> ids,
		int count,
		int year,
		long ownerSeed)
	{
		if (store == null || ids == null || ids.Count == 0 || count <= 0) return;
		int start = PositiveModulo(ownerSeed, ids.Count);
		int baseCount = count / ids.Count;
		int remainder = count % ids.Count;
		for (int i = 0; i < ids.Count; i++)
		{
			int amount = baseCount + (i < remainder ? 1 : 0);
			if (amount <= 0) continue;
			AddMaterialUnsafe(store, ids[(start + i) % ids.Count], amount, year);
		}
	}

	private static void AddPillCountUnsafe(XjAlchemyOwnerKey owner, string pillId, int quantity)
	{
		if (quantity <= 0 || string.IsNullOrWhiteSpace(pillId)) return;
		if (!pillCountsByOwner.TryGetValue(owner, out Dictionary<string, int> counts))
		{
			counts = new Dictionary<string, int>(StringComparer.Ordinal);
			pillCountsByOwner[owner] = counts;
		}
		counts.TryGetValue(pillId, out int current);
		long next = (long)current + quantity;
		counts[pillId] = next > int.MaxValue ? int.MaxValue : (int)next;
	}

	private static void RemovePillCountUnsafe(XjAlchemyOwnerKey owner, string pillId, int quantity)
	{
		if (quantity <= 0 || !pillCountsByOwner.TryGetValue(owner, out Dictionary<string, int> counts)
			|| !counts.TryGetValue(pillId, out int current)) return;
		int remaining = current - quantity;
		if (remaining > 0) counts[pillId] = remaining;
		else counts.Remove(pillId);
		if (counts.Count == 0) pillCountsByOwner.Remove(owner);
	}

	private static void RebuildPillCountsForOwnerUnsafe(XjAlchemyOwnerKey owner)
	{
		pillCountsByOwner.Remove(owner);
		if (!batchIdsByOwner.TryGetValue(owner, out HashSet<string> ids)) return;
		foreach (string id in ids)
			if (pillBatchesById.TryGetValue(id, out XjAlchemyPillBatchState batch))
				AddPillCountUnsafe(owner, batch.PillId, batch.Quantity);
	}

	private static int GetRecipeBit(string recipeId)
	{
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeSheYuan, StringComparison.Ordinal)) return 1 << 0;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeYangYuan, StringComparison.Ordinal)) return 1 << 1;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeSuiYuan, StringComparison.Ordinal)) return 1 << 2;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeYuYa, StringComparison.Ordinal)) return 1 << 3;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeHuiQiu, StringComparison.Ordinal)) return 1 << 4;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeHuMai, StringComparison.Ordinal)) return 1 << 5;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeQingShen, StringComparison.Ordinal)) return 1 << 6;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeHuanZhen, StringComparison.Ordinal)) return 1 << 7;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeChengJin, StringComparison.Ordinal)) return 1 << 8;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeSanQuan, StringComparison.Ordinal)) return 1 << 9;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeXuJiu, StringComparison.Ordinal)) return 1 << 10;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeHuiShuiXuanDao, StringComparison.Ordinal)) return 1 << 11;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeJiuYaoYanShou, StringComparison.Ordinal)) return 1 << 12;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeYuLuHuiMing, StringComparison.Ordinal)) return 1 << 13;
		return 0;
	}

	private static XjAlchemyOwnerKey ParseOwner(int scope, long ownerId)
	{
		return Enum.IsDefined(typeof(XjAlchemyOwnerScope), scope)
			? new XjAlchemyOwnerKey((XjAlchemyOwnerScope)scope, ownerId)
			: default;
	}

	private static bool IsValidBatch(XjAlchemyPillBatchState batch)
	{
		return batch.Owner.IsValid
			&& !string.IsNullOrWhiteSpace(batch.BatchId)
			&& batch.Quantity > 0
			&& batch.ExpireYear >= batch.CraftedYear
			&& XjAlchemyCatalog.TryGetPill(batch.PillId, out _)
			&& XjAlchemyCatalog.TryGetRecipe(batch.RecipeId, out XjAlchemyRecipeDef recipe)
			&& string.Equals(recipe.PillId, batch.PillId, StringComparison.Ordinal);
	}

	private static string BuildTaskPillKey(string taskId, string pillId)
	{
		return string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(pillId)
			? string.Empty
			: taskId.Trim() + "|" + pillId.Trim();
	}

	private static bool AreSameBatch(XjAlchemyPillBatchState left, XjAlchemyPillBatchState right)
	{
		return string.Equals(left.BatchId, right.BatchId, StringComparison.Ordinal)
			&& left.Owner.Equals(right.Owner)
			&& string.Equals(left.PillId, right.PillId, StringComparison.Ordinal)
			&& left.Quantity == right.Quantity
			&& left.Quality == right.Quality
			&& left.CrafterActorId == right.CrafterActorId
			&& left.CraftedYear == right.CraftedYear
			&& left.ExpireYear == right.ExpireYear
			&& string.Equals(left.RecipeId, right.RecipeId, StringComparison.Ordinal)
			&& string.Equals(left.TaskId, right.TaskId, StringComparison.Ordinal);
	}

	private static void ClearUnsafe()
	{
		materialsByOwner.Clear();
		recipesByOwner.Clear();
		pillBatchesById.Clear();
		batchIdsByOwner.Clear();
		batchIdByTaskAndPill.Clear();
		pillCountsByOwner.Clear();
		lastPrunedYearByOwner.Clear();
		expiredPillBatchIds.Clear();
	}

	private static int PositiveModulo(long value, int divisor)
	{
		if (divisor <= 0) return 0;
		long remainder = value % divisor;
		return (int)(remainder < 0 ? remainder + divisor : remainder);
	}

}
