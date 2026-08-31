using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Death;

namespace XuanJianVNext.Systems.Alchemy;

/// <summary>
/// Death is the single transfer boundary for personal alchemy assets.
/// It closes the crafter's in-flight task first, then transfers the personal
/// medicine bag into the confirmed family medicine warehouse when possible.
/// </summary>
internal static class XjAlchemyDeathHandler
{
	internal static void Handle(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L)
		{
			return;
		}

		RefundUnstartedTask(snapshot.ActorId, snapshot.Year);
		XjAlchemyRuntimeRegistry.InterruptTasksForCrafter(snapshot.ActorId, snapshot.Year);

		XjAlchemyOwnerKey personal = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Personal, snapshot.ActorId);
		if (snapshot.FamilyStableId > 0L)
		{
			XjAlchemyOwnerKey family = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Family, snapshot.FamilyStableId);
			XjAlchemyInventoryRegistry.TransferAll(personal, family, snapshot.Year);
		}
		else
		{
			XjAlchemyInventoryRegistry.RemoveAll(personal);
		}
	}

	private static void RefundUnstartedTask(long actorId, int deathYear)
	{
		if (!XjAlchemyRuntimeRegistry.TryGetOpenTaskForCrafter(actorId, out XjAlchemyTaskArchiveData task)
			|| !task.MaterialCommitted
			|| task.LastAdvancedYear >= task.CreatedYear
			|| !XjAlchemyCatalog.TryGetRecipe(task.RecipeId, out XjAlchemyRecipeDef recipe))
		{
			return;
		}

		XjAlchemyOwnerKey owner = new XjAlchemyOwnerKey((XjAlchemyOwnerScope)task.OwnerScope, task.OwnerId);
		if (!owner.IsValid)
		{
			return;
		}

		Dictionary<string, int> refund = new Dictionary<string, int>(StringComparer.Ordinal);
		for (int i = 0; i < recipe.Ingredients.Count; i++)
		{
			XjAlchemyIngredient ingredient = recipe.Ingredients[i];
			Add(refund, ingredient.MaterialId, ingredient.Count);
		}
		foreach (KeyValuePair<string, int> pair in refund)
		{
			XjAlchemyInventoryRegistry.TryAddMaterial(owner, pair.Key, pair.Value, deathYear);
		}
	}

	private static void Add(Dictionary<string, int> values, string id, int count)
	{
		if (string.IsNullOrWhiteSpace(id) || count <= 0)
		{
			return;
		}

		values.TryGetValue(id, out int existing);
		values[id] = existing + count;
	}
}
