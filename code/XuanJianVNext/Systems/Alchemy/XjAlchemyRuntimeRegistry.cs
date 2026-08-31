using System;
using System.Collections.Generic;
using System.Linq;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Systems.Alchemy;

/// <summary>
/// Open-task lookup key. Alchemy has at most one in-flight task for the same
/// owner/recipe pair, so the hot path must never scan the historical task table.
/// </summary>
internal readonly struct XjAlchemyOwnerRecipeKey : IEquatable<XjAlchemyOwnerRecipeKey>
{
	internal readonly XjAlchemyOwnerKey Owner;
	internal readonly string RecipeId;

	internal XjAlchemyOwnerRecipeKey(XjAlchemyOwnerKey owner, string recipeId)
	{
		Owner = owner;
		RecipeId = recipeId ?? string.Empty;
	}

	public bool Equals(XjAlchemyOwnerRecipeKey other)
	{
		return Owner.Equals(other.Owner)
			&& string.Equals(RecipeId, other.RecipeId, StringComparison.Ordinal);
	}

	public override bool Equals(object obj)
	{
		return obj is XjAlchemyOwnerRecipeKey other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Owner, StringComparer.Ordinal.GetHashCode(RecipeId));
	}
}

internal static class XjAlchemyRuntimeRegistry
{
	private static readonly object sync = new();
	private static readonly Dictionary<string, XjAlchemyTaskArchiveData> tasksById = new(StringComparer.Ordinal);
	private static readonly Dictionary<long, XjAlchemyCrafterArchiveData> craftersByActorId = new();
	private static readonly Dictionary<string, XjAlchemyRecipeProficiencyArchiveData> proficiencyByActorRecipe = new(StringComparer.Ordinal);
	private static readonly Dictionary<long, HashSet<string>> proficiencyKeysByActorId = new();

	// Hot-path indexes. Completed tasks remain in tasksById as idempotency records,
	// but annual actor processing only touches these O(1) maps.
	private static readonly Dictionary<long, string> openTaskIdByCrafter = new();
	private static readonly Dictionary<XjAlchemyOwnerRecipeKey, string> openTaskIdByOwnerRecipe = new();
	private static readonly List<string> closedTaskRemovalBuffer = new();

	internal static int OpenTaskCount { get { lock (sync) return openTaskIdByCrafter.Count; } }
	internal static int CrafterCount { get { lock (sync) return craftersByActorId.Count; } }
	internal static int ProficiencyCount { get { lock (sync) return proficiencyByActorRecipe.Count; } }

	internal static bool HasClosedTasks
	{
		get
		{
			lock (sync)
			{
				return tasksById.Count > openTaskIdByCrafter.Count;
			}
		}
	}

	internal static bool TryRegisterTask(XjAlchemyTaskArchiveData task)
	{
		if (!IsValidTask(task)
			|| !XjAlchemyCatalog.TryGetRecipe(task.RecipeId, out XjAlchemyRecipeDef recipe)
			|| !recipe.ProductionEnabled) return false;

		XjAlchemyOwnerKey owner = new((XjAlchemyOwnerScope)task.OwnerScope, task.OwnerId);
		XjAlchemyOwnerRecipeKey ownerRecipe = new(owner, task.RecipeId);
		lock (sync)
		{
			if (tasksById.TryGetValue(task.TaskId, out XjAlchemyTaskArchiveData existing))
				return AreSameTask(existing, task);
			if (openTaskIdByOwnerRecipe.ContainsKey(ownerRecipe)
				|| openTaskIdByCrafter.ContainsKey(task.CrafterActorId))
				return false;

			XjAlchemyCrafterArchiveData crafter = GetOrCreateCrafterUnsafe(task.CrafterActorId);
			if (!string.IsNullOrWhiteSpace(crafter.CurrentTaskId)) return false;

			tasksById[task.TaskId] = CloneTask(task);
			openTaskIdByCrafter[task.CrafterActorId] = task.TaskId;
			openTaskIdByOwnerRecipe[ownerRecipe] = task.TaskId;
			crafter.CurrentTaskId = task.TaskId;
			crafter.LastAlchemyYear = Math.Max(crafter.LastAlchemyYear, task.CreatedYear);
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool TryWriteTask(XjAlchemyTaskArchiveData task)
	{
		if (!IsValidTask(task)) return false;
		lock (sync)
		{
			if (!tasksById.TryGetValue(task.TaskId, out XjAlchemyTaskArchiveData existing)) return false;
			if (existing.OwnerScope != task.OwnerScope || existing.OwnerId != task.OwnerId
				|| existing.CrafterActorId != task.CrafterActorId
				|| !string.Equals(existing.RecipeId, task.RecipeId, StringComparison.Ordinal)) return false;

			XjAlchemyOwnerKey owner = new((XjAlchemyOwnerScope)task.OwnerScope, task.OwnerId);
			XjAlchemyOwnerRecipeKey ownerRecipe = new(owner, task.RecipeId);
			bool nextOpen = IsOpen(task);
			if (nextOpen)
			{
				if (openTaskIdByCrafter.TryGetValue(task.CrafterActorId, out string crafterTaskId)
					&& !string.Equals(crafterTaskId, task.TaskId, StringComparison.Ordinal)) return false;
				if (openTaskIdByOwnerRecipe.TryGetValue(ownerRecipe, out string ownerTaskId)
					&& !string.Equals(ownerTaskId, task.TaskId, StringComparison.Ordinal)) return false;
			}

			tasksById[task.TaskId] = CloneTask(task);
			if (nextOpen)
			{
				openTaskIdByCrafter[task.CrafterActorId] = task.TaskId;
				openTaskIdByOwnerRecipe[ownerRecipe] = task.TaskId;
				GetOrCreateCrafterUnsafe(task.CrafterActorId).CurrentTaskId = task.TaskId;
			}
			else
			{
				RemoveOpenIndexesUnsafe(existing);
				XjAlchemyCrafterArchiveData crafter = GetOrCreateCrafterUnsafe(task.CrafterActorId);
				if (string.Equals(crafter.CurrentTaskId, task.TaskId, StringComparison.Ordinal))
					crafter.CurrentTaskId = string.Empty;
			}
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool TryReadTask(string taskId, out XjAlchemyTaskArchiveData task)
	{
		task = null;
		if (string.IsNullOrWhiteSpace(taskId)) return false;
		lock (sync)
		{
			if (!tasksById.TryGetValue(taskId, out XjAlchemyTaskArchiveData stored)) return false;
			task = CloneTask(stored);
			return true;
		}
	}

	internal static bool TryGetOpenTaskForCrafter(long actorId, out XjAlchemyTaskArchiveData task)
	{
		task = null;
		if (actorId <= 0L) return false;
		lock (sync)
		{
			if (!openTaskIdByCrafter.TryGetValue(actorId, out string taskId))
			{
				return false;
			}

			if (!tasksById.TryGetValue(taskId, out XjAlchemyTaskArchiveData stored) || !IsOpen(stored))
			{
				if (stored != null) RemoveOpenIndexesUnsafe(stored);
				else RemoveDanglingTaskIdUnsafe(taskId, actorId);
				if (craftersByActorId.TryGetValue(actorId, out XjAlchemyCrafterArchiveData staleCrafter))
					staleCrafter.CurrentTaskId = string.Empty;
				return false;
			}

			task = CloneTask(stored);
			return true;
		}
	}

	internal static bool HasOpenTask(XjAlchemyOwnerKey owner, string recipeId)
	{
		if (!owner.IsValid || string.IsNullOrWhiteSpace(recipeId)) return false;
		lock (sync)
		{
			return openTaskIdByOwnerRecipe.ContainsKey(new XjAlchemyOwnerRecipeKey(owner, recipeId));
		}
	}

	internal static int GetRecipeProficiency(long actorId, string recipeId)
	{
		if (actorId <= 0L || string.IsNullOrWhiteSpace(recipeId)) return 0;
		lock (sync)
		{
			return proficiencyByActorRecipe.TryGetValue(BuildActorRecipeKey(actorId, recipeId), out XjAlchemyRecipeProficiencyArchiveData value)
				? Math.Max(0, value.Proficiency) : 0;
		}
	}

	internal static XjAlchemyCrafterArchiveData ReadCrafter(long actorId)
	{
		if (actorId <= 0L) return new XjAlchemyCrafterArchiveData();
		lock (sync) return CloneCrafter(GetOrCreateCrafterUnsafe(actorId));
	}

	/// <summary>
	/// 金性转世只继承炼丹者本人已经形成的技艺履历，不继承未完成任务，
	/// 也不把前世 actorId 的运行时索引直接搬到新角色。
	/// </summary>
	internal static void RestoreReincarnatedCrafter(
		long actorId,
		int totalProficiency,
		int successCount,
		int failureCount,
		int majorAccidentCount,
		int lastAlchemyYear)
	{
		if (actorId <= 0L) return;
		lock (sync)
		{
			XjAlchemyCrafterArchiveData crafter = GetOrCreateCrafterUnsafe(actorId);
			crafter.TotalProficiency = Math.Max(crafter.TotalProficiency, Math.Max(0, totalProficiency));
			crafter.SuccessCount = Math.Max(crafter.SuccessCount, Math.Max(0, successCount));
			crafter.FailureCount = Math.Max(crafter.FailureCount, Math.Max(0, failureCount));
			crafter.MajorAccidentCount = Math.Max(crafter.MajorAccidentCount, Math.Max(0, majorAccidentCount));
			crafter.LastAlchemyYear = Math.Max(crafter.LastAlchemyYear, Math.Max(0, lastAlchemyYear));
			crafter.CurrentTaskId = string.Empty;
		}
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static bool ApplyResolvedProgress(string taskId, bool success, bool majorAccident, int year)
	{
		if (string.IsNullOrWhiteSpace(taskId)) return false;
		lock (sync)
		{
			if (!tasksById.TryGetValue(taskId, out XjAlchemyTaskArchiveData task) || task.CrafterProgressApplied) return false;
			XjAlchemyCrafterArchiveData crafter = GetOrCreateCrafterUnsafe(task.CrafterActorId);
			crafter.TotalProficiency = Math.Min(int.MaxValue, crafter.TotalProficiency + (success ? 3 : 1));
			if (success) crafter.SuccessCount = Math.Min(int.MaxValue, crafter.SuccessCount + 1);
			else crafter.FailureCount = Math.Min(int.MaxValue, crafter.FailureCount + 1);
			if (majorAccident) crafter.MajorAccidentCount = Math.Min(int.MaxValue, crafter.MajorAccidentCount + 1);
			crafter.LastAlchemyYear = Math.Max(crafter.LastAlchemyYear, year);
			string key = BuildActorRecipeKey(task.CrafterActorId, task.RecipeId);
			if (!proficiencyByActorRecipe.TryGetValue(key, out XjAlchemyRecipeProficiencyArchiveData proficiency))
			{
				proficiency = new XjAlchemyRecipeProficiencyArchiveData { ActorId = task.CrafterActorId, RecipeId = task.RecipeId };
				proficiencyByActorRecipe[key] = proficiency;
			}
			IndexProficiencyUnsafe(task.CrafterActorId, key);
			proficiency.Proficiency = Math.Min(100, proficiency.Proficiency + (success ? 4 : 1));
			task.CrafterProgressApplied = true;
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static int InterruptTasksForCrafter(long actorId, int year)
	{
		if (actorId <= 0L) return 0;
		lock (sync)
		{
			if (!openTaskIdByCrafter.TryGetValue(actorId, out string taskId)
				|| !tasksById.TryGetValue(taskId, out XjAlchemyTaskArchiveData task)
				|| !IsOpen(task)) return 0;

			task.Status = (int)XjAlchemyTaskStatus.Interrupted;
			task.LastAdvancedYear = Math.Max(task.LastAdvancedYear, year);
			task.OutcomeCalculated = true;
			task.Success = false;
			task.YieldQuantity = 0;
			task.Resolved = true;
			task.ProductStored = true;
			task.CrafterProgressApplied = true;
			task.HistoryWritten = true;
			RemoveOpenIndexesUnsafe(task);
			if (craftersByActorId.TryGetValue(actorId, out XjAlchemyCrafterArchiveData crafter))
				crafter.CurrentTaskId = string.Empty;
		}
		XjWorldArchiveSystem.MarkChanged();
		return 1;
	}

	/// <summary>
	/// Called when the shared actor cleanup confirms an actor is gone. Per-actor
	/// proficiency keys avoid scanning every historical alchemy record on death.
	/// </summary>
	internal static void ForgetActor(long actorId, int year)
	{
		if (actorId <= 0L) return;
		XjAlchemyPillEffectSystem.ForgetCombatRuntime(actorId);
		InterruptTasksForCrafter(actorId, year);
		bool changed;
		lock (sync)
		{
			changed = craftersByActorId.Remove(actorId);
			if (proficiencyKeysByActorId.TryGetValue(actorId, out HashSet<string> keys))
			{
				foreach (string key in keys) changed |= proficiencyByActorRecipe.Remove(key);
				proficiencyKeysByActorId.Remove(actorId);
			}
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
	}

	/// <summary>
	/// AVBS 等第三方换身属于同一逻辑角色的 actorId 技术迁移，不得把炼丹技艺、
	/// 个人库存或正在进行的炼丹任务当作死亡状态清掉。历史已完成任务仍保留旧 id
	/// 作为当年事实，只迁移“当前仍活着”的炼丹状态。
	/// </summary>
	internal static void ForgetUnavailableActor(long actorId, int year)
	{
		if (actorId <= 0L) return;
		ForgetActor(actorId, year);
		XjAlchemyInventoryRegistry.RemoveAll(new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Personal, actorId));
	}

	internal static bool RebindActorAfterExternalReplacement(long oldActorId, long newActorId, int currentYear)
	{
		if (oldActorId <= 0L || newActorId <= 0L || oldActorId == newActorId) return false;

		bool inventoryChanged = XjAlchemyInventoryRegistry.TransferAll(
			new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Personal, oldActorId),
			new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Personal, newActorId),
			Math.Max(0, currentYear));
		bool changed = false;
		lock (sync)
		{
			string movedOpenTaskId = string.Empty;
			if (openTaskIdByCrafter.TryGetValue(oldActorId, out string taskId)
				&& tasksById.TryGetValue(taskId, out XjAlchemyTaskArchiveData task)
				&& IsOpen(task))
			{
				RemoveOpenIndexesUnsafe(task);
				task.CrafterActorId = newActorId;
				if (task.OwnerScope == (int)XjAlchemyOwnerScope.Personal && task.OwnerId == oldActorId)
					task.OwnerId = newActorId;
				openTaskIdByCrafter[newActorId] = task.TaskId;
				XjAlchemyOwnerKey owner = new((XjAlchemyOwnerScope)task.OwnerScope, task.OwnerId);
				openTaskIdByOwnerRecipe[new XjAlchemyOwnerRecipeKey(owner, task.RecipeId)] = task.TaskId;
				movedOpenTaskId = task.TaskId;
				changed = true;
			}
			else
			{
				openTaskIdByCrafter.Remove(oldActorId);
			}

			if (craftersByActorId.TryGetValue(oldActorId, out XjAlchemyCrafterArchiveData crafter))
			{
				craftersByActorId.Remove(oldActorId);
				crafter.ActorId = newActorId;
				if (!string.IsNullOrEmpty(movedOpenTaskId)) crafter.CurrentTaskId = movedOpenTaskId;
				craftersByActorId[newActorId] = crafter;
				changed = true;
			}
			else if (!string.IsNullOrEmpty(movedOpenTaskId))
			{
				GetOrCreateCrafterUnsafe(newActorId).CurrentTaskId = movedOpenTaskId;
				changed = true;
			}

			if (proficiencyKeysByActorId.TryGetValue(oldActorId, out HashSet<string> oldKeys))
			{
				string[] keys = oldKeys.ToArray();
				proficiencyKeysByActorId.Remove(oldActorId);
				for (int i = 0; i < keys.Length; i++)
				{
					string oldKey = keys[i];
					if (!proficiencyByActorRecipe.TryGetValue(oldKey, out XjAlchemyRecipeProficiencyArchiveData proficiency)) continue;
					proficiencyByActorRecipe.Remove(oldKey);
					proficiency.ActorId = newActorId;
					string newKey = BuildActorRecipeKey(newActorId, proficiency.RecipeId);
					if (proficiencyByActorRecipe.TryGetValue(newKey, out XjAlchemyRecipeProficiencyArchiveData existing))
						existing.Proficiency = Math.Max(existing.Proficiency, proficiency.Proficiency);
					else
						proficiencyByActorRecipe[newKey] = proficiency;
					IndexProficiencyUnsafe(newActorId, newKey);
					changed = true;
				}
			}
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
		return changed || inventoryChanged;
	}

	internal static int PruneClosedTasks(int currentYear, int retentionYears, int removeBudget)
	{
		if (currentYear <= 0 || removeBudget <= 0) return 0;
		int cutoffYear = Math.Max(0, currentYear - Math.Max(1, retentionYears));
		int removed = 0;
		lock (sync)
		{
			closedTaskRemovalBuffer.Clear();
			foreach (KeyValuePair<string, XjAlchemyTaskArchiveData> pair in tasksById)
			{
				XjAlchemyTaskArchiveData task = pair.Value;
				if (task == null || !IsOpen(task) && GetTerminalYear(task) <= cutoffYear)
				{
					closedTaskRemovalBuffer.Add(pair.Key);
					if (closedTaskRemovalBuffer.Count >= removeBudget) break;
				}
			}
			for (int i = 0; i < closedTaskRemovalBuffer.Count; i++) tasksById.Remove(closedTaskRemovalBuffer[i]);
			removed = closedTaskRemovalBuffer.Count;
		}
		if (removed > 0) XjWorldArchiveSystem.MarkChanged();
		return removed;
	}

	internal static IReadOnlyList<XjAlchemyTaskArchiveData> ReadOpenTasks()
	{
		lock (sync)
		{
			List<XjAlchemyTaskArchiveData> result = new(openTaskIdByCrafter.Count);
			foreach (string taskId in openTaskIdByCrafter.Values)
			{
				if (tasksById.TryGetValue(taskId, out XjAlchemyTaskArchiveData task) && IsOpen(task))
					result.Add(CloneTask(task));
			}
			result.Sort((left, right) =>
			{
				int byYear = left.ExpectedCompleteYear.CompareTo(right.ExpectedCompleteYear);
				return byYear != 0 ? byYear : string.Compare(left.TaskId, right.TaskId, StringComparison.Ordinal);
			});
			return result;
		}
	}

	internal static void ExportArchiveRecords(List<XjAlchemyTaskArchiveData> tasks, List<XjAlchemyCrafterArchiveData> crafters, List<XjAlchemyRecipeProficiencyArchiveData> proficiencies)
	{
		if (tasks == null || crafters == null || proficiencies == null) return;
		lock (sync)
		{
			tasks.AddRange(tasksById.Values.OrderBy(task => task.TaskId, StringComparer.Ordinal).Select(CloneTask));
			crafters.AddRange(craftersByActorId.Values.OrderBy(crafter => crafter.ActorId).Select(CloneCrafter));
			proficiencies.AddRange(proficiencyByActorRecipe.Values.OrderBy(value => value.ActorId).ThenBy(value => value.RecipeId, StringComparer.Ordinal).Select(CloneProficiency));
		}
	}

	internal static void ImportArchiveRecords(IEnumerable<XjAlchemyTaskArchiveData> tasks, IEnumerable<XjAlchemyCrafterArchiveData> crafters, IEnumerable<XjAlchemyRecipeProficiencyArchiveData> proficiencies)
	{
		lock (sync)
		{
			ClearUnsafe();
			if (tasks != null)
			{
				foreach (XjAlchemyTaskArchiveData task in tasks)
					if (IsValidTask(task) && !tasksById.ContainsKey(task.TaskId)) tasksById[task.TaskId] = CloneTask(task);
			}
			if (crafters != null)
			{
				foreach (XjAlchemyCrafterArchiveData crafter in crafters)
					if (crafter != null && crafter.ActorId > 0L) craftersByActorId[crafter.ActorId] = CloneCrafter(crafter);
			}
			if (proficiencies != null)
			{
				foreach (XjAlchemyRecipeProficiencyArchiveData proficiency in proficiencies)
				{
					if (proficiency == null || proficiency.ActorId <= 0L || !XjAlchemyCatalog.TryGetRecipe(proficiency.RecipeId, out _)) continue;
					string key = BuildActorRecipeKey(proficiency.ActorId, proficiency.RecipeId);
					proficiencyByActorRecipe[key] = CloneProficiency(proficiency);
					IndexProficiencyUnsafe(proficiency.ActorId, key);
				}
			}

			foreach (XjAlchemyCrafterArchiveData crafter in craftersByActorId.Values)
				crafter.CurrentTaskId = string.Empty;
			foreach (XjAlchemyTaskArchiveData task in tasksById.Values
				.Where(IsOpen)
				.OrderBy(value => value.CreatedYear)
				.ThenBy(value => value.TaskId, StringComparer.Ordinal))
			{
				XjAlchemyOwnerKey owner = new((XjAlchemyOwnerScope)task.OwnerScope, task.OwnerId);
				XjAlchemyOwnerRecipeKey ownerRecipe = new(owner, task.RecipeId);
				if (openTaskIdByCrafter.ContainsKey(task.CrafterActorId)
					|| openTaskIdByOwnerRecipe.ContainsKey(ownerRecipe)) continue;
				openTaskIdByCrafter[task.CrafterActorId] = task.TaskId;
				openTaskIdByOwnerRecipe[ownerRecipe] = task.TaskId;
				GetOrCreateCrafterUnsafe(task.CrafterActorId).CurrentTaskId = task.TaskId;
			}
		}
	}

	internal static void Clear()
	{
		lock (sync) ClearUnsafe();
		XjAlchemyPillEffectSystem.ClearCombatRuntime();
	}

	private static void RemoveDanglingTaskIdUnsafe(string taskId, long actorId)
	{
		if (actorId > 0L
			&& openTaskIdByCrafter.TryGetValue(actorId, out string crafterTaskId)
			&& string.Equals(crafterTaskId, taskId, StringComparison.Ordinal))
		{
			openTaskIdByCrafter.Remove(actorId);
		}

		// Corrupt/migrated archives can contain an index target whose task row is
		// absent. This exceptional cleanup may scan the tiny open index, never the
		// historical task table and never the normal annual path.
		XjAlchemyOwnerRecipeKey? staleOwnerRecipe = null;
		foreach (KeyValuePair<XjAlchemyOwnerRecipeKey, string> item in openTaskIdByOwnerRecipe)
		{
			if (!string.Equals(item.Value, taskId, StringComparison.Ordinal)) continue;
			staleOwnerRecipe = item.Key;
			break;
		}
		if (staleOwnerRecipe.HasValue) openTaskIdByOwnerRecipe.Remove(staleOwnerRecipe.Value);
	}

	private static void RemoveOpenIndexesUnsafe(XjAlchemyTaskArchiveData task)
	{
		if (task == null) return;
		if (openTaskIdByCrafter.TryGetValue(task.CrafterActorId, out string crafterTaskId)
			&& string.Equals(crafterTaskId, task.TaskId, StringComparison.Ordinal))
			openTaskIdByCrafter.Remove(task.CrafterActorId);

		XjAlchemyOwnerKey owner = new((XjAlchemyOwnerScope)task.OwnerScope, task.OwnerId);
		XjAlchemyOwnerRecipeKey ownerRecipe = new(owner, task.RecipeId);
		if (openTaskIdByOwnerRecipe.TryGetValue(ownerRecipe, out string ownerTaskId)
			&& string.Equals(ownerTaskId, task.TaskId, StringComparison.Ordinal))
			openTaskIdByOwnerRecipe.Remove(ownerRecipe);
	}

	private static bool IsOpen(XjAlchemyTaskArchiveData task)
	{
		return task != null && task.Status != (int)XjAlchemyTaskStatus.Completed && task.Status != (int)XjAlchemyTaskStatus.Interrupted;
	}

	private static XjAlchemyCrafterArchiveData GetOrCreateCrafterUnsafe(long actorId)
	{
		if (!craftersByActorId.TryGetValue(actorId, out XjAlchemyCrafterArchiveData value))
		{
			value = new XjAlchemyCrafterArchiveData { ActorId = actorId };
			craftersByActorId[actorId] = value;
		}
		return value;
	}

	private static void IndexProficiencyUnsafe(long actorId, string key)
	{
		if (actorId <= 0L || string.IsNullOrEmpty(key)) return;
		if (!proficiencyKeysByActorId.TryGetValue(actorId, out HashSet<string> keys))
		{
			keys = new HashSet<string>(StringComparer.Ordinal);
			proficiencyKeysByActorId[actorId] = keys;
		}
		keys.Add(key);
	}

	private static int GetTerminalYear(XjAlchemyTaskArchiveData task)
	{
		return Math.Max(0, Math.Max(task?.LastAdvancedYear ?? 0, Math.Max(task?.ExpectedCompleteYear ?? 0, task?.CreatedYear ?? 0)));
	}

	private static bool IsValidTask(XjAlchemyTaskArchiveData task)
	{
		if (task == null || string.IsNullOrWhiteSpace(task.TaskId) || task.CrafterActorId <= 0L
			|| task.CreatedYear < 0 || task.ExpectedCompleteYear < task.CreatedYear
			|| !XjAlchemyCatalog.TryGetRecipe(task.RecipeId, out _)) return false;
		XjAlchemyOwnerKey owner = Enum.IsDefined(typeof(XjAlchemyOwnerScope), task.OwnerScope)
			? new XjAlchemyOwnerKey((XjAlchemyOwnerScope)task.OwnerScope, task.OwnerId) : default;
		return owner.IsValid && Enum.IsDefined(typeof(XjAlchemyTaskStatus), task.Status);
	}

	private static bool AreSameTask(XjAlchemyTaskArchiveData left, XjAlchemyTaskArchiveData right)
	{
		return left != null && right != null
			&& string.Equals(left.TaskId, right.TaskId, StringComparison.Ordinal)
			&& left.OwnerScope == right.OwnerScope && left.OwnerId == right.OwnerId
			&& string.Equals(left.RecipeId, right.RecipeId, StringComparison.Ordinal)
			&& left.CrafterActorId == right.CrafterActorId && left.CreatedYear == right.CreatedYear
			&& left.ExpectedCompleteYear == right.ExpectedCompleteYear && left.Status == right.Status
			&& left.MaterialCommitted == right.MaterialCommitted && left.OutcomeCalculated == right.OutcomeCalculated
			&& left.Success == right.Success && left.MajorAccident == right.MajorAccident
			&& left.YieldQuantity == right.YieldQuantity && left.ProductQuality == right.ProductQuality
			&& left.Resolved == right.Resolved && left.ProductStored == right.ProductStored
			&& left.CrafterProgressApplied == right.CrafterProgressApplied && left.HistoryWritten == right.HistoryWritten;
	}

	private static XjAlchemyTaskArchiveData CloneTask(XjAlchemyTaskArchiveData source)
	{
		return new XjAlchemyTaskArchiveData
		{
			TaskId = source.TaskId ?? string.Empty, OwnerScope = source.OwnerScope, OwnerId = source.OwnerId,
			RecipeId = source.RecipeId ?? string.Empty, CrafterActorId = source.CrafterActorId,
			CreatedYear = source.CreatedYear, ExpectedCompleteYear = source.ExpectedCompleteYear,
			LastAdvancedYear = source.LastAdvancedYear, Status = source.Status,
			DeterministicSeedKey = source.DeterministicSeedKey ?? string.Empty,
			MaterialCommitted = source.MaterialCommitted, OutcomeCalculated = source.OutcomeCalculated,
			Success = source.Success, MajorAccident = source.MajorAccident,
			YieldQuantity = Math.Max(0, source.YieldQuantity), ProductQuality = source.ProductQuality,
			Resolved = source.Resolved, ProductStored = source.ProductStored,
			CrafterProgressApplied = source.CrafterProgressApplied, HistoryWritten = source.HistoryWritten
		};
	}

	private static XjAlchemyCrafterArchiveData CloneCrafter(XjAlchemyCrafterArchiveData source)
	{
		return new XjAlchemyCrafterArchiveData
		{
			ActorId = source.ActorId, TotalProficiency = Math.Max(0, source.TotalProficiency),
			SuccessCount = Math.Max(0, source.SuccessCount), FailureCount = Math.Max(0, source.FailureCount),
			LastAlchemyYear = Math.Max(0, source.LastAlchemyYear), CurrentTaskId = source.CurrentTaskId ?? string.Empty,
			MajorAccidentCount = Math.Max(0, source.MajorAccidentCount)
		};
	}

	private static XjAlchemyRecipeProficiencyArchiveData CloneProficiency(XjAlchemyRecipeProficiencyArchiveData source)
	{
		return new XjAlchemyRecipeProficiencyArchiveData { ActorId = source.ActorId, RecipeId = source.RecipeId ?? string.Empty, Proficiency = Math.Max(0, source.Proficiency) };
	}

	private static string BuildActorRecipeKey(long actorId, string recipeId) => actorId + "|" + (recipeId ?? string.Empty);

	private static void ClearUnsafe()
	{
		tasksById.Clear();
		craftersByActorId.Clear();
		proficiencyByActorRecipe.Clear();
		proficiencyKeysByActorId.Clear();
		openTaskIdByCrafter.Clear();
		openTaskIdByOwnerRecipe.Clear();
		closedTaskRemovalBuffer.Clear();
	}
}
