using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Data.Codex;

namespace XuanJianVNext.Systems.Craft;

internal readonly struct XjCraftOwnerKey : IEquatable<XjCraftOwnerKey>
{
	internal XjCraftOwnerKey(int scope, long ownerId)
	{
		Scope = scope;
		OwnerId = ownerId;
	}

	internal int Scope { get; }
	internal long OwnerId { get; }
	internal bool IsValid => OwnerId > 0L && Scope >= XjCraftOwnerScope.Personal && Scope <= XjCraftOwnerScope.Sect;

	public bool Equals(XjCraftOwnerKey other) => Scope == other.Scope && OwnerId == other.OwnerId;
	public override bool Equals(object obj) => obj is XjCraftOwnerKey other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Scope, OwnerId);
	public override string ToString() => Scope.ToString(CultureInfo.InvariantCulture) + ":" + OwnerId.ToString(CultureInfo.InvariantCulture);
}

internal readonly struct XjCraftResourceKey : IEquatable<XjCraftResourceKey>
{
	internal XjCraftResourceKey(XjCraftOwnerKey owner, string resourceId)
	{
		Owner = owner;
		ResourceId = resourceId ?? string.Empty;
	}

	internal XjCraftOwnerKey Owner { get; }
	internal string ResourceId { get; }
	public bool Equals(XjCraftResourceKey other) => Owner.Equals(other.Owner) && string.Equals(ResourceId, other.ResourceId, StringComparison.Ordinal);
	public override bool Equals(object obj) => obj is XjCraftResourceKey other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Owner, StringComparer.Ordinal.GetHashCode(ResourceId));
}

internal readonly struct XjTalismanOwnerTypeKey : IEquatable<XjTalismanOwnerTypeKey>
{
	internal XjTalismanOwnerTypeKey(XjCraftOwnerKey owner, string talismanId)
	{
		Owner = owner;
		TalismanId = talismanId ?? string.Empty;
	}

	internal XjCraftOwnerKey Owner { get; }
	internal string TalismanId { get; }
	public bool Equals(XjTalismanOwnerTypeKey other) => Owner.Equals(other.Owner) && string.Equals(TalismanId, other.TalismanId, StringComparison.Ordinal);
	public override bool Equals(object obj) => obj is XjTalismanOwnerTypeKey other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Owner, StringComparer.Ordinal.GetHashCode(TalismanId));
}

internal static class XjCraftDomainRegistry
{
	private const int FormationResourceStackCap = 200;
	private const int TalismanStackCap = 200;
	private static readonly Dictionary<long, XjCraftTaskArchiveRecord> TasksById = new Dictionary<long, XjCraftTaskArchiveRecord>();
	private static readonly Dictionary<long, long> OpenTaskIdByActor = new Dictionary<long, long>();
	private static readonly Dictionary<XjCraftResourceKey, XjCraftResourceArchiveRecord> Resources = new Dictionary<XjCraftResourceKey, XjCraftResourceArchiveRecord>();
	private static readonly Dictionary<long, XjTalismanBatchArchiveRecord> TalismanBatchesById = new Dictionary<long, XjTalismanBatchArchiveRecord>();
	private static readonly Dictionary<XjTalismanOwnerTypeKey, List<long>> TalismanBatchIdsByOwnerType = new Dictionary<XjTalismanOwnerTypeKey, List<long>>();
	private static readonly Dictionary<string, XjTalismanCarryArchiveRecord> CarriesByActorType = new Dictionary<string, XjTalismanCarryArchiveRecord>(StringComparer.Ordinal);
	// 符箓携带上限很低，但死亡回收原先仍会遍历全部携带记录。按角色维护
	// 反向索引后，角色离场和携带数量查询均为 O(该角色携带数)。
	private static readonly Dictionary<long, List<string>> CarryKeysByActor = new Dictionary<long, List<string>>();
	private static readonly Dictionary<long, int> CarryCountByActor = new Dictionary<long, int>();
	private static readonly List<long> ClosedTaskRemovalBuffer = new List<long>();

	internal static int OpenTaskCount => OpenTaskIdByActor.Count;
	internal static bool HasClosedTasks => TasksById.Count > OpenTaskIdByActor.Count;
	internal static int ResourceStackCount => Resources.Count;
	internal static int TalismanBatchCount => TalismanBatchesById.Count;
	internal static int TalismanCarryCount => CarriesByActorType.Count;

	internal static bool TryRegisterTask(XjCraftTaskArchiveRecord task)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || !IsValidTask(task)) return false;
		if (TasksById.TryGetValue(task.TaskId, out XjCraftTaskArchiveRecord existing)) return existing.IsOpen && AreSameTask(existing, task);
		if (OpenTaskIdByActor.ContainsKey(task.ActorId)) return false;
		XjCraftTaskArchiveRecord copy = Clone(task);
		TasksById.Add(copy.TaskId, copy);
		if (copy.IsOpen) OpenTaskIdByActor[copy.ActorId] = copy.TaskId;
		MarkChanged();
		return true;
	}


	internal static bool TryCommitTaskWithResources(XjCraftTaskArchiveRecord task, XjCraftOwnerKey owner, IReadOnlyList<(string ResourceId, int Amount)> costs, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || !IsValidTask(task) || !owner.IsValid || costs == null || costs.Count == 0) return false;
		if (TasksById.TryGetValue(task.TaskId, out XjCraftTaskArchiveRecord existing)) return existing.IsOpen && existing.MaterialsCommitted && AreSameTask(existing, task);
		if (OpenTaskIdByActor.ContainsKey(task.ActorId)) return false;
		for (int i = 0; i < costs.Count; i++)
		{
			(string resourceId, int amount) = costs[i];
			if (string.IsNullOrWhiteSpace(resourceId) || amount <= 0 || GetResourceCount(owner, resourceId) < amount) return false;
		}
		for (int i = 0; i < costs.Count; i++)
		{
			(string resourceId, int amount) = costs[i];
			XjCraftResourceKey key = new XjCraftResourceKey(owner, resourceId.Trim());
			XjCraftResourceArchiveRecord record = Resources[key];
			record.Quantity -= amount;
			record.UpdatedYear = Math.Max(record.UpdatedYear, currentYear);
			if (record.Quantity <= 0) Resources.Remove(key);
		}
		XjCraftTaskArchiveRecord copy = Clone(task);
		TasksById.Add(copy.TaskId, copy);
		if (copy.IsOpen) OpenTaskIdByActor[copy.ActorId] = copy.TaskId;
		MarkChanged();
		return true;
	}

	internal static bool TryAbortTaskAndRefund(long taskId, XjCraftOwnerKey owner, IReadOnlyList<(string ResourceId, int Amount)> costs, int currentYear, string reason)
	{
		if (taskId <= 0L || !TasksById.TryGetValue(taskId, out XjCraftTaskArchiveRecord task) || !task.MaterialsCommitted) return false;
		task.Status = XjCraftTaskStatus.Interrupted;
		task.FailureReason = reason ?? string.Empty;
		task.LastAdvancedYear = Math.Max(task.LastAdvancedYear, currentYear);
		if (OpenTaskIdByActor.TryGetValue(task.ActorId, out long openId) && openId == taskId) OpenTaskIdByActor.Remove(task.ActorId);
		task.MaterialsCommitted = false;
		if (owner.IsValid && costs != null)
		{
			for (int i = 0; i < costs.Count; i++)
			{
				(string resourceId, int amount) = costs[i];
				if (string.IsNullOrWhiteSpace(resourceId) || amount <= 0) continue;
				XjCraftResourceKey key = new XjCraftResourceKey(owner, resourceId.Trim());
				if (!Resources.TryGetValue(key, out XjCraftResourceArchiveRecord record))
				{
					record = new XjCraftResourceArchiveRecord { OwnerScope = owner.Scope, OwnerId = owner.OwnerId, ResourceId = resourceId.Trim() };
					Resources.Add(key, record);
				}
				long next = ClampResourceStack(resourceId, (long)Math.Max(0, record.Quantity) + amount);
				record.Quantity = next >= int.MaxValue ? int.MaxValue : (int)next;
				record.UpdatedYear = Math.Max(record.UpdatedYear, currentYear);
			}
		}
		MarkChanged();
		return true;
	}

	internal static bool TryWriteTask(XjCraftTaskArchiveRecord task)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || !IsValidTask(task) || !TasksById.TryGetValue(task.TaskId, out XjCraftTaskArchiveRecord existing)) return false;
		if (existing.ActorId != task.ActorId || existing.OwnerScope != task.OwnerScope || existing.OwnerId != task.OwnerId
			|| !string.Equals(existing.ProfessionId, task.ProfessionId, StringComparison.Ordinal)) return false;
		XjCraftTaskArchiveRecord copy = Clone(task);
		TasksById[copy.TaskId] = copy;
		if (copy.IsOpen) OpenTaskIdByActor[copy.ActorId] = copy.TaskId;
		else if (OpenTaskIdByActor.TryGetValue(copy.ActorId, out long currentTaskId) && currentTaskId == copy.TaskId) OpenTaskIdByActor.Remove(copy.ActorId);
		MarkChanged();
		return true;
	}

	internal static bool TryGetTaskById(long taskId, out XjCraftTaskArchiveRecord task)
	{
		task = null;
		if (taskId <= 0L || !TasksById.TryGetValue(taskId, out XjCraftTaskArchiveRecord stored)) return false;
		task = Clone(stored);
		return true;
	}

	internal static bool TryGetOpenTask(long actorId, out XjCraftTaskArchiveRecord task)
	{
		task = null;
		if (actorId <= 0L || !OpenTaskIdByActor.TryGetValue(actorId, out long taskId)) return false;
		if (!TasksById.TryGetValue(taskId, out XjCraftTaskArchiveRecord stored) || !stored.IsOpen)
		{
			OpenTaskIdByActor.Remove(actorId);
			return false;
		}
		task = Clone(stored);
		return true;
	}

	internal static IReadOnlyList<XjCraftTaskArchiveRecord> ReadTasks()
	{
		if (TasksById.Count == 0) return Array.Empty<XjCraftTaskArchiveRecord>();
		List<XjCraftTaskArchiveRecord> result = new List<XjCraftTaskArchiveRecord>(TasksById.Count);
		foreach (XjCraftTaskArchiveRecord task in TasksById.Values) if (task != null) result.Add(Clone(task));
		result.Sort((left, right) => left.TaskId.CompareTo(right.TaskId));
		return result;
	}

	/// <summary>
	/// Completed tasks only guard short-term retry/idempotency paths. Keeping every
	/// historical task makes the craft archive and Codex snapshots grow forever in
	/// long worlds, so the annual coordinator compacts terminal rows after a small
	/// recovery window. Narrative history is already persisted separately.
	/// </summary>
	internal static int PruneClosedTasks(int currentYear, int retentionYears, int removeBudget)
	{
		if (currentYear <= 0 || removeBudget <= 0 || TasksById.Count == 0) return 0;
		int cutoffYear = Math.Max(0, currentYear - Math.Max(1, retentionYears));
		ClosedTaskRemovalBuffer.Clear();
		foreach (KeyValuePair<long, XjCraftTaskArchiveRecord> pair in TasksById)
		{
			XjCraftTaskArchiveRecord task = pair.Value;
			if (task == null || !task.IsOpen && GetTerminalYear(task) <= cutoffYear)
			{
				ClosedTaskRemovalBuffer.Add(pair.Key);
				if (ClosedTaskRemovalBuffer.Count >= removeBudget) break;
			}
		}

		for (int i = 0; i < ClosedTaskRemovalBuffer.Count; i++) TasksById.Remove(ClosedTaskRemovalBuffer[i]);
		if (ClosedTaskRemovalBuffer.Count > 0) MarkChanged();
		return ClosedTaskRemovalBuffer.Count;
	}

	internal static int GetResourceCount(XjCraftOwnerKey owner, string resourceId)
	{
		if (!owner.IsValid || string.IsNullOrWhiteSpace(resourceId)) return 0;
		return Resources.TryGetValue(new XjCraftResourceKey(owner, resourceId.Trim()), out XjCraftResourceArchiveRecord record)
			? Math.Max(0, record.Quantity) : 0;
	}

	internal static bool TryAddResource(XjCraftOwnerKey owner, string resourceId, int amount, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || !owner.IsValid || string.IsNullOrWhiteSpace(resourceId) || amount <= 0) return false;
		string normalized = resourceId.Trim();
		XjCraftResourceKey key = new XjCraftResourceKey(owner, normalized);
		if (!Resources.TryGetValue(key, out XjCraftResourceArchiveRecord record))
		{
			record = new XjCraftResourceArchiveRecord { OwnerScope = owner.Scope, OwnerId = owner.OwnerId, ResourceId = normalized };
			Resources.Add(key, record);
		}
		long next = ClampResourceStack(normalized, (long)Math.Max(0, record.Quantity) + amount);
		if (next <= Math.Max(0, record.Quantity))
		{
			return false;
		}
		record.Quantity = next >= int.MaxValue ? int.MaxValue : (int)next;
		record.UpdatedYear = Math.Max(record.UpdatedYear, currentYear);
		MarkChanged();
		return true;
	}

	internal static bool TryConsumeResources(XjCraftOwnerKey owner, IReadOnlyList<(string ResourceId, int Amount)> costs, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || !owner.IsValid || costs == null || costs.Count == 0) return false;
		for (int i = 0; i < costs.Count; i++)
		{
			(string resourceId, int amount) = costs[i];
			if (string.IsNullOrWhiteSpace(resourceId) || amount <= 0 || GetResourceCount(owner, resourceId) < amount) return false;
		}
		for (int i = 0; i < costs.Count; i++)
		{
			(string resourceId, int amount) = costs[i];
			XjCraftResourceKey key = new XjCraftResourceKey(owner, resourceId.Trim());
			XjCraftResourceArchiveRecord record = Resources[key];
			record.Quantity -= amount;
			record.UpdatedYear = Math.Max(record.UpdatedYear, currentYear);
			if (record.Quantity <= 0) Resources.Remove(key);
		}
		MarkChanged();
		return true;
	}

	internal static IReadOnlyList<XjCraftResourceArchiveRecord> ReadResources()
	{
		if (Resources.Count == 0) return Array.Empty<XjCraftResourceArchiveRecord>();
		List<XjCraftResourceArchiveRecord> result = new List<XjCraftResourceArchiveRecord>(Resources.Count);
		foreach (XjCraftResourceArchiveRecord record in Resources.Values) result.Add(Clone(record));
		result.Sort((left, right) =>
		{
			int byScope = left.OwnerScope.CompareTo(right.OwnerScope);
			if (byScope != 0) return byScope;
			int byOwner = left.OwnerId.CompareTo(right.OwnerId);
			return byOwner != 0 ? byOwner : string.Compare(left.ResourceId, right.ResourceId, StringComparison.Ordinal);
		});
		return result;
	}

	internal static bool TransferSectWarehouse(
		long sourceSectId,
		long targetSectId,
		int currentYear,
		out int resourceStacks,
		out int resourceQuantity,
		out int talismanBatches,
		out int talismanQuantity,
		out int interruptedTasks)
	{
		resourceStacks = 0;
		resourceQuantity = 0;
		talismanBatches = 0;
		talismanQuantity = 0;
		interruptedTasks = 0;
		if (sourceSectId <= 0L || targetSectId <= 0L || sourceSectId == targetSectId) return false;

		XjCraftOwnerKey sourceOwner = new XjCraftOwnerKey(XjCraftOwnerScope.Sect, sourceSectId);
		XjCraftOwnerKey targetOwner = new XjCraftOwnerKey(XjCraftOwnerScope.Sect, targetSectId);
		bool changed = false;

		List<XjCraftResourceKey> sourceResourceKeys = new List<XjCraftResourceKey>();
		foreach (KeyValuePair<XjCraftResourceKey, XjCraftResourceArchiveRecord> pair in Resources)
		{
			if (pair.Key.Owner.Equals(sourceOwner) && pair.Value != null && pair.Value.Quantity > 0)
			{
				sourceResourceKeys.Add(pair.Key);
			}
		}
		for (int i = 0; i < sourceResourceKeys.Count; i++)
		{
			XjCraftResourceKey sourceKey = sourceResourceKeys[i];
			if (!Resources.TryGetValue(sourceKey, out XjCraftResourceArchiveRecord source)) continue;
			int amount = Math.Max(0, source.Quantity);
			if (amount <= 0) continue;
			XjCraftResourceKey targetKey = new XjCraftResourceKey(targetOwner, source.ResourceId);
			if (!Resources.TryGetValue(targetKey, out XjCraftResourceArchiveRecord target))
			{
				target = new XjCraftResourceArchiveRecord
				{
					OwnerScope = targetOwner.Scope,
					OwnerId = targetOwner.OwnerId,
					ResourceId = source.ResourceId
				};
				Resources[targetKey] = target;
			}
			int before = Math.Max(0, target.Quantity);
			long next = ClampResourceStack(source.ResourceId, (long)before + amount);
			int accepted = (int)Math.Max(0L, next - before);
			if (accepted <= 0) continue;
			target.Quantity = next >= int.MaxValue ? int.MaxValue : (int)next;
			target.UpdatedYear = Math.Max(Math.Max(0, currentYear), Math.Max(target.UpdatedYear, source.UpdatedYear));
			source.Quantity = Math.Max(0, source.Quantity - accepted);
			if (source.Quantity <= 0) Resources.Remove(sourceKey);
			resourceStacks++;
			resourceQuantity += accepted;
			changed = true;
		}

		List<long> sourceBatchIds = new List<long>();
		foreach (KeyValuePair<long, XjTalismanBatchArchiveRecord> pair in TalismanBatchesById)
		{
			XjTalismanBatchArchiveRecord batch = pair.Value;
			if (batch != null && batch.OwnerScope == sourceOwner.Scope && batch.OwnerId == sourceOwner.OwnerId && batch.Quantity > 0)
			{
				sourceBatchIds.Add(pair.Key);
			}
		}
		for (int i = 0; i < sourceBatchIds.Count; i++)
		{
			long batchId = sourceBatchIds[i];
			if (!TalismanBatchesById.TryGetValue(batchId, out XjTalismanBatchArchiveRecord batch) || batch == null || batch.Quantity <= 0) continue;
			XjTalismanOwnerTypeKey sourceTypeKey = new XjTalismanOwnerTypeKey(sourceOwner, batch.TalismanId);
			XjTalismanOwnerTypeKey targetTypeKey = new XjTalismanOwnerTypeKey(targetOwner, batch.TalismanId);
			if (!TalismanBatchIdsByOwnerType.TryGetValue(targetTypeKey, out List<long> targetIds))
			{
				targetIds = new List<long>();
				TalismanBatchIdsByOwnerType[targetTypeKey] = targetIds;
			}
			int targetCount = GetTalismanCount(targetOwner, batch.TalismanId);
			int accepted = Math.Min(Math.Max(0, batch.Quantity), Math.Max(0, TalismanStackCap - targetCount));
			if (accepted <= 0) continue;

			XjTalismanBatchArchiveRecord targetBatch = null;
			for (int j = 0; j < targetIds.Count; j++)
			{
				if (TalismanBatchesById.TryGetValue(targetIds[j], out XjTalismanBatchArchiveRecord existing)
					&& existing != null && existing.Quantity > 0)
				{
					targetBatch = existing;
					break;
				}
			}
			if (targetBatch != null)
			{
				targetBatch.Quantity += accepted;
				if (batch.CraftedYear >= targetBatch.CraftedYear)
				{
					targetBatch.CraftedYear = batch.CraftedYear;
					targetBatch.CrafterActorId = batch.CrafterActorId;
					targetBatch.TaskId = batch.TaskId;
				}
				batch.Quantity -= accepted;
				if (batch.Quantity <= 0)
				{
					if (TalismanBatchIdsByOwnerType.TryGetValue(sourceTypeKey, out List<long> sourceIds))
					{
						sourceIds.Remove(batchId);
						if (sourceIds.Count == 0) TalismanBatchIdsByOwnerType.Remove(sourceTypeKey);
					}
					TalismanBatchesById.Remove(batchId);
				}
			}
			else
			{
				// A source batch is already capped at 200, so an empty target can accept it whole.
				if (TalismanBatchIdsByOwnerType.TryGetValue(sourceTypeKey, out List<long> sourceIds))
				{
					sourceIds.Remove(batchId);
					if (sourceIds.Count == 0) TalismanBatchIdsByOwnerType.Remove(sourceTypeKey);
				}
				batch.OwnerScope = targetOwner.Scope;
				batch.OwnerId = targetOwner.OwnerId;
				batch.Quantity = accepted;
				targetIds.Add(batchId);
			}
			talismanBatches++;
			talismanQuantity += accepted;
			changed = true;
		}
		foreach (List<long> ids in TalismanBatchIdsByOwnerType.Values) ids.Sort();

		foreach (XjCraftTaskArchiveRecord task in TasksById.Values)
		{
			if (task == null || !task.IsOpen || task.OwnerScope != sourceOwner.Scope || task.OwnerId != sourceOwner.OwnerId) continue;
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "宗门覆灭，工坊停转";
			task.LastAdvancedYear = Math.Max(task.LastAdvancedYear, Math.Max(0, currentYear));
			OpenTaskIdByActor.Remove(task.ActorId);
			interruptedTasks++;
			changed = true;
		}

		if (changed) MarkChanged();
		return changed;
	}

	internal static bool HasTalismanBatch(long batchId) => batchId > 0L && TalismanBatchesById.ContainsKey(batchId);

	internal static bool TryAddTalismanBatch(XjTalismanBatchArchiveRecord batch)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || batch == null || batch.BatchId <= 0L || batch.OwnerId <= 0L
			|| string.IsNullOrWhiteSpace(batch.TalismanId) || batch.Quantity <= 0 || TalismanBatchesById.ContainsKey(batch.BatchId)) return false;
		XjTalismanBatchArchiveRecord copy = Clone(batch);
		XjCraftOwnerKey owner = new XjCraftOwnerKey(copy.OwnerScope, copy.OwnerId);
		int existingCount = GetTalismanCount(owner, copy.TalismanId);
		copy.Quantity = Math.Min(copy.Quantity, Math.Max(0, TalismanStackCap - existingCount));
		if (copy.Quantity <= 0) return false;
		XjTalismanOwnerTypeKey key = new XjTalismanOwnerTypeKey(owner, copy.TalismanId);
		if (!TalismanBatchIdsByOwnerType.TryGetValue(key, out List<long> ids))
		{
			ids = new List<long>();
			TalismanBatchIdsByOwnerType.Add(key, ids);
		}
		if (TryMergeTalismanBatch(ids, copy))
		{
			MarkChanged();
			return true;
		}
		TalismanBatchesById.Add(copy.BatchId, copy);
		ids.Add(copy.BatchId);
		ids.Sort();
		MarkChanged();
		return true;
	}

	internal static int GetTalismanCount(XjCraftOwnerKey owner, string talismanId)
	{
		if (!owner.IsValid || string.IsNullOrWhiteSpace(talismanId)) return 0;
		XjTalismanOwnerTypeKey key = new XjTalismanOwnerTypeKey(owner, talismanId.Trim());
		if (!TalismanBatchIdsByOwnerType.TryGetValue(key, out List<long> ids)) return 0;
		long total = 0L;
		for (int i = 0; i < ids.Count; i++) if (TalismanBatchesById.TryGetValue(ids[i], out XjTalismanBatchArchiveRecord batch)) total += Math.Max(0, batch.Quantity);
		return total >= int.MaxValue ? int.MaxValue : (int)total;
	}

	internal static bool TryConsumeTalisman(XjCraftOwnerKey owner, string talismanId, int amount, out string sourceBatchKey)
	{
		sourceBatchKey = string.Empty;
		if (!XjWorldSchemaGuard.GameplayEnabled || !owner.IsValid || string.IsNullOrWhiteSpace(talismanId) || amount <= 0 || GetTalismanCount(owner, talismanId) < amount) return false;
		XjTalismanOwnerTypeKey key = new XjTalismanOwnerTypeKey(owner, talismanId.Trim());
		List<long> ids = TalismanBatchIdsByOwnerType[key];
		int remaining = amount;
		for (int i = 0; i < ids.Count && remaining > 0;)
		{
			long id = ids[i];
			if (!TalismanBatchesById.TryGetValue(id, out XjTalismanBatchArchiveRecord batch))
			{
				ids.RemoveAt(i);
				continue;
			}
			int take = Math.Min(remaining, batch.Quantity);
			batch.Quantity -= take;
			remaining -= take;
			if (sourceBatchKey.Length == 0) sourceBatchKey = id.ToString(CultureInfo.InvariantCulture);
			if (batch.Quantity <= 0)
			{
				TalismanBatchesById.Remove(id);
				ids.RemoveAt(i);
			}
			else i++;
		}
		if (ids.Count == 0) TalismanBatchIdsByOwnerType.Remove(key);
		MarkChanged();
		return remaining == 0;
	}

	internal static bool TryAssignCarry(long actorId, XjCraftOwnerKey owner, string talismanId, int currentYear)
	{
		if (actorId <= 0L || !owner.IsValid || string.IsNullOrWhiteSpace(talismanId)) return false;
		string key = BuildCarryKey(actorId, talismanId);
		if (CarriesByActorType.TryGetValue(key, out XjTalismanCarryArchiveRecord existing) && existing.Quantity > 0) return false;
		if (GetActorCarryCount(actorId) >= 4 || !TryConsumeTalisman(owner, talismanId, 1, out string sourceBatchKey)) return false;
		StoreCarry(key, new XjTalismanCarryArchiveRecord
		{
			ActorId = actorId,
			TalismanId = talismanId.Trim(),
			Quantity = 1,
			AssignedYear = Math.Max(0, currentYear),
			SourceBatchKey = sourceBatchKey
		});
		MarkChanged();
		return true;
	}

	internal static bool TryConsumeCarry(long actorId, string talismanId)
	{
		string key = BuildCarryKey(actorId, talismanId);
		if (actorId <= 0L || !CarriesByActorType.TryGetValue(key, out XjTalismanCarryArchiveRecord carry) || carry.Quantity <= 0) return false;
		carry.Quantity--;
		AdjustCarryCount(actorId, -1);
		if (carry.Quantity <= 0)
		{
			CarriesByActorType.Remove(key);
			RemoveCarryKey(actorId, key);
		}
		MarkChanged();
		return true;
	}

	internal static bool HasCarry(long actorId, string talismanId)
	{
		return actorId > 0L && CarriesByActorType.TryGetValue(BuildCarryKey(actorId, talismanId), out XjTalismanCarryArchiveRecord carry) && carry.Quantity > 0;
	}

	internal static IReadOnlyList<XjTalismanBatchArchiveRecord> ReadTalismanBatches()
	{
		List<XjTalismanBatchArchiveRecord> result = new List<XjTalismanBatchArchiveRecord>(TalismanBatchesById.Count);
		foreach (XjTalismanBatchArchiveRecord batch in TalismanBatchesById.Values) if (batch.Quantity > 0) result.Add(Clone(batch));
		result.Sort((left, right) => left.BatchId.CompareTo(right.BatchId));
		return result;
	}

	internal static IReadOnlyList<XjTalismanCarryArchiveRecord> ReadCarries()
	{
		List<XjTalismanCarryArchiveRecord> result = new List<XjTalismanCarryArchiveRecord>(CarriesByActorType.Count);
		foreach (XjTalismanCarryArchiveRecord carry in CarriesByActorType.Values) if (carry.Quantity > 0) result.Add(Clone(carry));
		result.Sort((left, right) =>
		{
			int actor = left.ActorId.CompareTo(right.ActorId);
			return actor != 0 ? actor : string.Compare(left.TalismanId, right.TalismanId, StringComparison.Ordinal);
		});
		return result;
	}

	internal static void ForgetActor(long actorId)
	{
		if (actorId <= 0L) return;
		bool changed = false;
		if (OpenTaskIdByActor.TryGetValue(actorId, out long taskId) && TasksById.TryGetValue(taskId, out XjCraftTaskArchiveRecord task))
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "主持者离场，任务中断";
			task.LastAdvancedYear = System.Math.Max(task.LastAdvancedYear, 0);
			changed = true;
		}
		changed |= OpenTaskIdByActor.Remove(actorId);
		if (CarryKeysByActor.TryGetValue(actorId, out List<string> carryKeys))
		{
			for (int i = 0; i < carryKeys.Count; i++) CarriesByActorType.Remove(carryKeys[i]);
			CarryKeysByActor.Remove(actorId);
			CarryCountByActor.Remove(actorId);
			changed = true;
		}
		if (changed) MarkChanged();
	}

	internal static void ExportArchiveRecords(XjCraftArchiveBundle target)
	{
		if (target == null) return;
		target.Tasks.Clear();
		target.Resources.Clear();
		target.TalismanBatches.Clear();
		target.TalismanCarries.Clear();
		foreach (XjCraftTaskArchiveRecord task in ReadTasks()) target.Tasks.Add(task);
		foreach (XjCraftResourceArchiveRecord resource in ReadResources()) target.Resources.Add(resource);
		foreach (XjTalismanBatchArchiveRecord batch in ReadTalismanBatches()) target.TalismanBatches.Add(batch);
		foreach (XjTalismanCarryArchiveRecord carry in ReadCarries()) target.TalismanCarries.Add(carry);
	}

	internal static void ImportArchiveRecords(XjCraftArchiveBundle source)
	{
		Clear();
		if (source == null) return;
		if (source.Tasks != null)
		{
			List<XjCraftTaskArchiveRecord> ordered = new List<XjCraftTaskArchiveRecord>();
			for (int i = 0; i < source.Tasks.Count; i++)
			{
				XjCraftTaskArchiveRecord task = source.Tasks[i];
				if (IsValidTask(task)) ordered.Add(task);
			}
			ordered.Sort((left, right) =>
			{
				int byActor = left.ActorId.CompareTo(right.ActorId);
				if (byActor != 0) return byActor;
				int byCreated = left.CreatedYear.CompareTo(right.CreatedYear);
				return byCreated != 0 ? byCreated : left.TaskId.CompareTo(right.TaskId);
			});
			for (int i = 0; i < ordered.Count; i++)
			{
				XjCraftTaskArchiveRecord task = ordered[i];
				if (TasksById.ContainsKey(task.TaskId)) continue;
				XjCraftTaskArchiveRecord copy = Clone(task);
				if (copy.IsOpen && OpenTaskIdByActor.ContainsKey(copy.ActorId))
				{
					copy.Status = XjCraftTaskStatus.Interrupted;
					copy.FailureReason = "读档发现同一角色存在重复主任务，已中断后续任务";
				}
				TasksById.Add(copy.TaskId, copy);
				if (copy.IsOpen) OpenTaskIdByActor.Add(copy.ActorId, copy.TaskId);
			}
		}
		if (source.Resources != null)
		{
			for (int i = 0; i < source.Resources.Count; i++)
			{
				XjCraftResourceArchiveRecord resource = source.Resources[i];
				XjCraftOwnerKey owner = new XjCraftOwnerKey(resource?.OwnerScope ?? 0, resource?.OwnerId ?? 0L);
				if (resource == null || !owner.IsValid || string.IsNullOrWhiteSpace(resource.ResourceId) || resource.Quantity <= 0) continue;
				XjCraftResourceArchiveRecord copy = Clone(resource);
				copy.Quantity = (int)ClampResourceStack(copy.ResourceId, copy.Quantity);
				if (copy.Quantity > 0)
				{
					Resources[new XjCraftResourceKey(owner, copy.ResourceId.Trim())] = copy;
				}
			}
		}
		if (source.TalismanBatches != null)
		{
			for (int i = 0; i < source.TalismanBatches.Count; i++) ImportBatch(source.TalismanBatches[i]);
		}
		if (source.TalismanCarries != null)
		{
			for (int i = 0; i < source.TalismanCarries.Count; i++)
			{
				XjTalismanCarryArchiveRecord carry = source.TalismanCarries[i];
				if (carry == null || carry.ActorId <= 0L || string.IsNullOrWhiteSpace(carry.TalismanId) || carry.Quantity <= 0) continue;
				StoreCarry(BuildCarryKey(carry.ActorId, carry.TalismanId), Clone(carry));
			}
		}
	}

	internal static void Clear()
	{
		TasksById.Clear();
		OpenTaskIdByActor.Clear();
		Resources.Clear();
		TalismanBatchesById.Clear();
		TalismanBatchIdsByOwnerType.Clear();
		CarriesByActorType.Clear();
		CarryKeysByActor.Clear();
		CarryCountByActor.Clear();
	}

	private static void ImportBatch(XjTalismanBatchArchiveRecord batch)
	{
		if (batch == null || batch.BatchId <= 0L || batch.OwnerId <= 0L || string.IsNullOrWhiteSpace(batch.TalismanId) || batch.Quantity <= 0 || TalismanBatchesById.ContainsKey(batch.BatchId)) return;
		XjTalismanBatchArchiveRecord copy = Clone(batch);
		XjCraftOwnerKey owner = new XjCraftOwnerKey(copy.OwnerScope, copy.OwnerId);
		int existingCount = GetTalismanCount(owner, copy.TalismanId);
		copy.Quantity = Math.Min(copy.Quantity, Math.Max(0, TalismanStackCap - existingCount));
		if (copy.Quantity <= 0) return;
		XjTalismanOwnerTypeKey key = new XjTalismanOwnerTypeKey(owner, copy.TalismanId);
		if (!TalismanBatchIdsByOwnerType.TryGetValue(key, out List<long> ids))
		{
			ids = new List<long>();
			TalismanBatchIdsByOwnerType.Add(key, ids);
		}
		if (TryMergeTalismanBatch(ids, copy)) return;
		TalismanBatchesById.Add(copy.BatchId, copy);
		ids.Add(copy.BatchId);
		ids.Sort();
	}

	// A batch is an inventory stack, not an event-log row. 0.9.6起符箓不再区分成品品质，
	// 长期世界只保留每个持有者、每种符箓的一份聚合库存。
	private static bool TryMergeTalismanBatch(List<long> ids, XjTalismanBatchArchiveRecord incoming, long excludedBatchId = 0L)
	{
		if (ids == null || incoming == null) return false;
		for (int i = 0; i < ids.Count; i++)
		{
			long id = ids[i];
			if (id == excludedBatchId || !TalismanBatchesById.TryGetValue(id, out XjTalismanBatchArchiveRecord existing)
				|| existing == null || existing.Quantity <= 0) continue;
			long next = Math.Min(TalismanStackCap, (long)Math.Max(0, existing.Quantity) + Math.Max(0, incoming.Quantity));
			if (next <= existing.Quantity) return true;
			existing.Quantity = (int)next;
			if (incoming.CraftedYear >= existing.CraftedYear)
			{
				existing.CraftedYear = incoming.CraftedYear;
				existing.CrafterActorId = incoming.CrafterActorId;
				existing.TaskId = incoming.TaskId;
			}
			return true;
		}
		return false;
	}

	private static int GetActorCarryCount(long actorId)
	{
		return actorId > 0L && CarryCountByActor.TryGetValue(actorId, out int count) ? Math.Max(0, count) : 0;
	}

	private static void StoreCarry(string key, XjTalismanCarryArchiveRecord carry)
	{
		if (string.IsNullOrWhiteSpace(key) || carry == null || carry.ActorId <= 0L || carry.Quantity <= 0) return;
		if (CarriesByActorType.TryGetValue(key, out XjTalismanCarryArchiveRecord existing))
		{
			AdjustCarryCount(existing.ActorId, -Math.Max(0, existing.Quantity));
			RemoveCarryKey(existing.ActorId, key);
		}
		CarriesByActorType[key] = carry;
		if (!CarryKeysByActor.TryGetValue(carry.ActorId, out List<string> keys))
		{
			keys = new List<string>(4);
			CarryKeysByActor.Add(carry.ActorId, keys);
		}
		keys.Add(key);
		AdjustCarryCount(carry.ActorId, Math.Max(0, carry.Quantity));
	}

	private static void RemoveCarryKey(long actorId, string key)
	{
		if (actorId <= 0L || !CarryKeysByActor.TryGetValue(actorId, out List<string> keys)) return;
		keys.Remove(key);
		if (keys.Count == 0) CarryKeysByActor.Remove(actorId);
	}

	private static void AdjustCarryCount(long actorId, int delta)
	{
		if (actorId <= 0L || delta == 0) return;
		int next = (CarryCountByActor.TryGetValue(actorId, out int current) ? current : 0) + delta;
		if (next <= 0) CarryCountByActor.Remove(actorId);
		else CarryCountByActor[actorId] = next;
	}

	private static string BuildCarryKey(long actorId, string talismanId) => actorId.ToString(CultureInfo.InvariantCulture) + "|" + (talismanId ?? string.Empty).Trim();

	private static bool IsValidTask(XjCraftTaskArchiveRecord task)
	{
		return task != null && task.TaskId > 0L && task.ActorId > 0L && task.OwnerId > 0L
			&& !string.IsNullOrWhiteSpace(task.ProfessionId) && !string.IsNullOrWhiteSpace(task.BlueprintId);
	}

	private static int GetTerminalYear(XjCraftTaskArchiveRecord task)
	{
		return Math.Max(0, Math.Max(task?.LastAdvancedYear ?? 0, Math.Max(task?.DueYear ?? 0, task?.CreatedYear ?? 0)));
	}

	private static bool AreSameTask(XjCraftTaskArchiveRecord left, XjCraftTaskArchiveRecord right)
	{
		return left != null && right != null && left.TaskId == right.TaskId && left.ActorId == right.ActorId
			&& left.OwnerScope == right.OwnerScope && left.OwnerId == right.OwnerId
			&& string.Equals(left.ProfessionId, right.ProfessionId, StringComparison.Ordinal)
			&& string.Equals(left.BlueprintId, right.BlueprintId, StringComparison.Ordinal);
	}

	private static void MarkChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Craft | XjCodexDirtyFlags.Formation | XjCodexDirtyFlags.Conflict);
	}

	private static XjCraftTaskArchiveRecord Clone(XjCraftTaskArchiveRecord source)
	{
		return new XjCraftTaskArchiveRecord
		{
			SchemaVersion = source.SchemaVersion,
			TaskId = source.TaskId,
			ProfessionId = source.ProfessionId ?? string.Empty,
			OwnerScope = source.OwnerScope,
			OwnerId = source.OwnerId,
			ActorId = source.ActorId,
			BlueprintId = source.BlueprintId ?? string.Empty,
			TaskKind = source.TaskKind ?? string.Empty,
			CreatedYear = source.CreatedYear,
			DueYear = source.DueYear,
			LastAdvancedYear = source.LastAdvancedYear,
			Status = source.Status ?? XjCraftTaskStatus.Planned,
			MaterialCommitmentId = source.MaterialCommitmentId ?? string.Empty,
			MaterialsCommitted = source.MaterialsCommitted,
			OutcomeCalculated = source.OutcomeCalculated,
			Success = source.Success,
			Quality = source.Quality,
			Quantity = source.Quantity,
			OutputStored = source.OutputStored,
			ProgressApplied = source.ProgressApplied,
			HistoryWritten = source.HistoryWritten,
			ResolutionKey = source.ResolutionKey ?? string.Empty,
			FailureReason = source.FailureReason ?? string.Empty
		};
	}

	private static XjCraftResourceArchiveRecord Clone(XjCraftResourceArchiveRecord source) => new XjCraftResourceArchiveRecord
	{
		SchemaVersion = source.SchemaVersion, OwnerScope = source.OwnerScope, OwnerId = source.OwnerId,
		ResourceId = source.ResourceId ?? string.Empty, Quantity = source.Quantity, UpdatedYear = source.UpdatedYear
	};

	private static XjTalismanBatchArchiveRecord Clone(XjTalismanBatchArchiveRecord source) => new XjTalismanBatchArchiveRecord
	{
		SchemaVersion = source.SchemaVersion, BatchId = source.BatchId, OwnerScope = source.OwnerScope, OwnerId = source.OwnerId,
		TalismanId = source.TalismanId ?? string.Empty, Quantity = source.Quantity, Quality = 1,
		CrafterActorId = source.CrafterActorId, CraftedYear = source.CraftedYear, TaskId = source.TaskId
	};

	private static XjTalismanCarryArchiveRecord Clone(XjTalismanCarryArchiveRecord source) => new XjTalismanCarryArchiveRecord
	{
		SchemaVersion = source.SchemaVersion, ActorId = source.ActorId, TalismanId = source.TalismanId ?? string.Empty,
		Quantity = source.Quantity, AssignedYear = source.AssignedYear, SourceBatchKey = source.SourceBatchKey ?? string.Empty
	};

	private static long ClampResourceStack(string resourceId, long quantity)
	{
		if (IsFormationMaterialResource(resourceId))
		{
			return Math.Min(FormationResourceStackCap, Math.Max(0L, quantity));
		}
		return Math.Min(TalismanStackCap, quantity < 0L ? 0L : quantity);
	}

	private static bool IsFormationMaterialResource(string resourceId)
	{
		if (string.IsNullOrWhiteSpace(resourceId))
		{
			return false;
		}
		string id = resourceId.Trim();
		return string.Equals(id, XjCraftCollaborationSystem.FormationFlagLow, StringComparison.Ordinal)
			|| string.Equals(id, XjCraftCollaborationSystem.FormationFlagHigh, StringComparison.Ordinal)
			|| string.Equals(id, XjCraftCollaborationSystem.FormationPlateLow, StringComparison.Ordinal)
			|| string.Equals(id, XjCraftCollaborationSystem.FormationPlateHigh, StringComparison.Ordinal)
			|| string.Equals(id, XjCraftCollaborationSystem.FormationRune, StringComparison.Ordinal)
			|| string.Equals(id, XjCraftCollaborationSystem.SpiritMaterial, StringComparison.Ordinal);
	}
}
