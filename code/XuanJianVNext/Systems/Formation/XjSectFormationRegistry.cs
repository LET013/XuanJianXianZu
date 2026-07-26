using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Formation;

internal static class XjSectFormationRegistry
{
	private const int BrokenRepairLockYears = 3;
	private static readonly Dictionary<long, XjSectFormationArchiveRecord> BySectId = new Dictionary<long, XjSectFormationArchiveRecord>();
	private static readonly Dictionary<long, long> SectIdByFormationId = new Dictionary<long, long>();

	internal static int Count => BySectId.Count;

	internal static XjSectFormationArchiveRecord EnsurePlanned(long sectId, int year)
	{
		if (sectId <= 0L) return null;
		if (BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord existing))
		{
			EnsureDurabilityScale(existing);
			return existing;
		}

		long formationId = BuildUniqueFormationId(sectId, year);
		XjSectFormationArchiveRecord record = new XjSectFormationArchiveRecord
		{
			FormationId = formationId,
			SectId = sectId,
			CreatedYear = Math.Max(0, year),
			BuildState = XjSectFormationBuildState.Unplanned,
			CompletionGate = XjSectFormationBalance.CompletionGate
		};
		BySectId.Add(sectId, record);
		SectIdByFormationId.Add(formationId, sectId);
		return record;
	}

	internal static bool TryBeginCommission(long sectId, int grade, string blueprintId, long leadFormationMasterId, long taskId, int currentYear)
	{
		XjSectFormationArchiveRecord record = EnsurePlanned(sectId, currentYear);
		if (!XjWorldSchemaGuard.GameplayEnabled || record == null || taskId <= 0L || record.BuildTaskId > 0L || record.Grade > 0) return false;
		int normalizedGrade = XjSectFormationBalance.ClampGrade(grade);
		record.Grade = normalizedGrade;
		record.BlueprintId = blueprintId ?? string.Empty;
		record.MaxDurability = XjSectFormationBalance.GetMaxDurability(normalizedGrade);
		record.CurrentDurability = 0;
		record.OccupationSpeedMultiplier = XjSectFormationBalance.GetOccupationMultiplier(normalizedGrade);
		record.CompletionGate = XjSectFormationBalance.CompletionGate;
		record.LeadFormationMasterId = System.Math.Max(0L, leadFormationMasterId);
		record.RequiredMaterialGrade = normalizedGrade;
		record.BuildTaskId = taskId;
		record.BuildState = XjSectFormationBuildState.Deploying;
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	internal static bool TryResumeCommission(long sectId, long leadFormationMasterId, long taskId)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || taskId <= 0L || !BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord record)
			|| record == null || record.Grade <= 0 || record.CurrentDurability > 0 || record.BuildTaskId > 0L
			|| !string.Equals(record.BuildState, XjSectFormationBuildState.Deploying, StringComparison.Ordinal)) return false;
		record.BuildTaskId = taskId;
		record.LeadFormationMasterId = System.Math.Max(0L, leadFormationMasterId);
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	internal static bool TryCompleteCommission(long sectId, long taskId, int durabilityPenalty, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || !BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord record)
			|| record == null || taskId <= 0L || record.BuildTaskId != taskId || record.Grade <= 0) return false;
		record.MaxDurability = System.Math.Max(100, XjSectFormationBalance.GetMaxDurability(record.Grade) - System.Math.Max(0, durabilityPenalty));
		record.CurrentDurability = record.MaxDurability;
		record.BuildTaskId = 0L;
		record.BuildState = XjSectFormationBuildState.Operational;
		record.LastRepairYear = System.Math.Max(record.LastRepairYear, currentYear);
		record.RuntimeVersion++;
		XjCenturyAnnalsStore.ObserveSectEvent(
			"SectFormationCompleted",
			Math.Max(0, currentYear),
			sectId,
			string.Empty,
			4,
			"护宗大阵建成，品阶" + record.Grade.ToString(CultureInfo.InvariantCulture) + "，耐久" + record.MaxDurability.ToString(CultureInfo.InvariantCulture));
		MarkChanged();
		return true;
	}

	internal static bool ForceEstablishFoundingSupremeFormation(long sectId, long founderActorId, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L) return false;
		XjSectFormationArchiveRecord record = EnsurePlanned(sectId, currentYear);
		if (record == null) return false;
		const int grade = 3;
		XjFormationProjectDefinition project = XjFormationMaterialCatalog.Get(grade);
		int maxDurability = XjSectFormationBalance.GetMaxDurability(grade);
		record.Grade = grade;
		record.BlueprintId = project.BlueprintId;
		record.MaxDurability = maxDurability;
		record.CurrentDurability = maxDurability;
		record.OccupationSpeedMultiplier = XjSectFormationBalance.GetOccupationMultiplier(grade);
		record.CompletionGate = XjSectFormationBalance.CompletionGate;
		record.LeadFormationMasterId = Math.Max(0L, founderActorId);
		record.BuildTaskId = 0L;
		record.RepairTaskId = 0L;
		record.RequiredMaterialGrade = grade;
		record.BuildState = XjSectFormationBuildState.Operational;
		record.CreatedYear = Math.Max(record.CreatedYear, Math.Max(0, currentYear));
		record.LastRepairYear = Math.Max(record.LastRepairYear, Math.Max(0, currentYear));
		record.RuntimeVersion++;
		XjCenturyAnnalsStore.ObserveSectEvent(
			"SectFormationFounded",
			Math.Max(0, currentYear),
			sectId,
			string.Empty,
			4,
			"开宗护宗大阵升起，品阶" + grade.ToString(CultureInfo.InvariantCulture) + "，耐久" + maxDurability.ToString(CultureInfo.InvariantCulture),
			founderActorId);
		MarkChanged();
		return true;
	}

	internal static bool TryDelayCommission(long sectId, long taskId, long leadFormationMasterId)
	{
		if (!BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord record) || record == null || record.BuildTaskId != taskId) return false;
		record.LeadFormationMasterId = System.Math.Max(0L, leadFormationMasterId);
		record.BuildState = XjSectFormationBuildState.Deploying;
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	internal static bool TryBeginRepair(long sectId, long leadFormationMasterId, long taskId, int currentYear = 0)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || taskId <= 0L || !BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord record)
			|| record == null || record.Grade <= 0 || record.CurrentDurability >= record.MaxDurability || record.RepairTaskId > 0L) return false;
		if (IsBrokenRepairLocked(record, currentYear)) return false;
		record.RepairTaskId = taskId;
		record.LeadFormationMasterId = System.Math.Max(0L, leadFormationMasterId);
		record.BuildState = XjSectFormationBuildState.Repairing;
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	internal static int CompleteRepair(long sectId, long taskId, int repairPoints, int currentYear)
	{
		if (!BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord record) || record == null || record.RepairTaskId != taskId) return 0;
		record.RepairTaskId = 0L;
		int applied = ApplyRepair(sectId, repairPoints, currentYear);
		if (applied > 0 && BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord current) && current != null
			&& string.Equals(current.BuildState, XjSectFormationBuildState.Operational, StringComparison.Ordinal))
		{
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectFormationRepaired",
				Math.Max(0, currentYear),
				sectId,
				string.Empty,
				3,
				"护宗大阵修复完成，恢复耐久" + applied.ToString(CultureInfo.InvariantCulture));
		}
		if (applied <= 0)
		{
			record.BuildState = record.CurrentDurability <= 0 ? XjSectFormationBuildState.Broken : XjSectFormationBuildState.Damaged;
			record.RuntimeVersion++;
			MarkChanged();
		}
		return applied;
	}

	internal static bool TryGet(long sectId, out XjSectFormationArchiveRecord record) => BySectId.TryGetValue(sectId, out record);

	internal static bool IsBrokenRepairLocked(XjSectFormationArchiveRecord record, int currentYear)
	{
		if (record == null || currentYear <= 0 || record.LastDamageYear <= 0 || record.CurrentDurability > 0
			|| !string.Equals(record.BuildState, XjSectFormationBuildState.Broken, StringComparison.Ordinal))
		{
			return false;
		}
		return currentYear - record.LastDamageYear < BrokenRepairLockYears;
	}

	internal static IReadOnlyList<XjSectFormationArchiveRecord> ReadAll()
	{
		if (BySectId.Count == 0) return Array.Empty<XjSectFormationArchiveRecord>();
		List<XjSectFormationArchiveRecord> result = new List<XjSectFormationArchiveRecord>(BySectId.Count);
		foreach (XjSectFormationArchiveRecord record in BySectId.Values)
		{
			if (record != null) result.Add(Clone(record));
		}
		result.Sort((left, right) => left.SectId.CompareTo(right.SectId));
		return result;
	}
	internal static bool TryGetOperational(long sectId, out XjSectFormationArchiveRecord record) => BySectId.TryGetValue(sectId, out record) && record != null && record.IsOperational;

	internal static int ApplyOccupationDamage(long sectId, int damage, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || damage <= 0 || !TryGetOperational(sectId, out XjSectFormationArchiveRecord record)) return 0;
		int applied = Math.Min(record.CurrentDurability, damage);
		if (applied <= 0) return 0;
		record.CurrentDurability -= applied;
		record.LastDamageYear = Math.Max(0, currentYear);
		record.RuntimeVersion++;
		if (record.CurrentDurability <= 0)
		{
			record.CurrentDurability = 0;
			record.BuildState = XjSectFormationBuildState.Broken;
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectFormationBroken",
				Math.Max(0, currentYear),
				sectId,
				string.Empty,
				5,
				"护宗大阵被攻破，损失耐久" + applied.ToString(CultureInfo.InvariantCulture));
		}
		else if (!string.Equals(record.BuildState, XjSectFormationBuildState.Repairing, StringComparison.Ordinal))
		{
			record.BuildState = XjSectFormationBuildState.Damaged;
			if (record.MaxDurability > 0 && applied >= Math.Max(1, record.MaxDurability / 4))
			{
				XjCenturyAnnalsStore.ObserveSectEvent(
					"SectFormationDamaged",
					Math.Max(0, currentYear),
					sectId,
					string.Empty,
					3,
					"护宗大阵重创，损失耐久" + applied.ToString(CultureInfo.InvariantCulture));
			}
		}
		MarkChanged();
		return applied;
	}

	internal static int ForceBreakByPopulationRout(long sectId, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || !TryGetOperational(sectId, out XjSectFormationArchiveRecord record)) return 0;
		int applied = Math.Max(0, record.CurrentDurability);
		if (applied <= 0) return 0;
		record.CurrentDurability = 0;
		record.BuildState = XjSectFormationBuildState.Broken;
		record.LastDamageYear = Math.Max(0, currentYear);
		record.RepairTaskId = 0L;
		record.RuntimeVersion++;
		XjCenturyAnnalsStore.ObserveSectEvent(
			"SectFormationBroken",
			Math.Max(0, currentYear),
			sectId,
			string.Empty,
			5,
			"护宗大阵因人口溃散而破灭，损失耐久" + applied.ToString(CultureInfo.InvariantCulture));
		MarkChanged();
		return applied;
	}

	internal static int ApplyRepair(long sectId, int repairPoints, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || repairPoints <= 0 || !BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord record) || record == null || record.Grade <= 0 || record.MaxDurability <= 0) return 0;
		if (IsBrokenRepairLocked(record, currentYear)) return 0;
		int missing = Math.Max(0, record.MaxDurability - record.CurrentDurability);
		int applied = Math.Min(missing, repairPoints);
		if (applied <= 0) return 0;
		record.CurrentDurability += applied;
		record.LastRepairYear = Math.Max(0, currentYear);
		record.BuildState = record.CurrentDurability >= record.MaxDurability ? XjSectFormationBuildState.Operational : XjSectFormationBuildState.Damaged;
		record.RuntimeVersion++;
		MarkChanged();
		return applied;
	}

	internal static bool RescaleDurabilityForSectPower(long sectId, int ziFuCount, int jinDanCount)
	{
		if (!BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord record) || record == null || record.Grade <= 0) return false;
		return RescaleDurability(record, XjSectFormationBalance.GetMaxDurability(record.Grade, ziFuCount, jinDanCount));
	}

	internal static void ReleaseLeadActorTasks(long actorId)
	{
		if (actorId <= 0L) return;
		bool changed = false;
		foreach (XjSectFormationArchiveRecord record in BySectId.Values)
		{
			if (record == null || record.LeadFormationMasterId != actorId) continue;
			bool recordChanged = false;
			if (record.BuildTaskId > 0L)
			{
				record.BuildTaskId = 0L;
				record.BuildState = XjSectFormationBuildState.Deploying;
				recordChanged = true;
			}
			if (record.RepairTaskId > 0L)
			{
				record.RepairTaskId = 0L;
				record.BuildState = record.CurrentDurability <= 0 ? XjSectFormationBuildState.Broken : XjSectFormationBuildState.Damaged;
				recordChanged = true;
			}
			if (recordChanged)
			{
				record.LeadFormationMasterId = 0L;
				record.RuntimeVersion++;
				changed = true;
			}
		}
		if (changed) MarkChanged();
	}

	internal static void RemoveForSect(long sectId)
	{
		if (!BySectId.TryGetValue(sectId, out XjSectFormationArchiveRecord record)) return;
		BySectId.Remove(sectId);
		if (record != null) SectIdByFormationId.Remove(record.FormationId);
	}

	internal static void ExportArchiveRecords(List<XjSectFormationArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		List<long> sectIds = new List<long>(BySectId.Keys);
		sectIds.Sort();
		for (int i = 0; i < sectIds.Count; i++) target.Add(Clone(BySectId[sectIds[i]]));
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjSectFormationArchiveRecord> source)
	{
		Clear();
		if (source == null) return;
		for (int i = 0; i < source.Count; i++)
		{
			XjSectFormationArchiveRecord item = source[i];
			if (item == null || item.SectId <= 0L || item.FormationId <= 0L) continue;
			XjSectFormationArchiveRecord copy = Clone(item);
			Normalize(copy);
			if (BySectId.TryGetValue(copy.SectId, out XjSectFormationArchiveRecord previous) && previous?.FormationId > 0L)
			{
				SectIdByFormationId.Remove(previous.FormationId);
			}
			BySectId[copy.SectId] = copy;
			SectIdByFormationId[copy.FormationId] = copy.SectId;
		}
	}

	internal static void ReconcileTaskLinksAfterLoad()
	{
		foreach (XjSectFormationArchiveRecord record in BySectId.Values)
		{
			if (record == null) continue;
			bool changed = false;
			if (record.BuildTaskId > 0L && record.RepairTaskId > 0L)
			{
				if (record.CurrentDurability <= 0) record.RepairTaskId = 0L;
				else record.BuildTaskId = 0L;
				changed = true;
			}
			if (record.BuildTaskId > 0L && !IsValidLinkedTask(record.BuildTaskId, record.SectId, "SectFormationCommission"))
			{
				record.BuildTaskId = 0L;
				record.LeadFormationMasterId = 0L;
				record.BuildState = record.Grade > 0 && record.CurrentDurability <= 0
					? XjSectFormationBuildState.Deploying
					: record.BuildState;
				changed = true;
			}
			if (record.RepairTaskId > 0L && !IsValidLinkedTask(record.RepairTaskId, record.SectId, "SectFormationRepair"))
			{
				record.RepairTaskId = 0L;
				record.LeadFormationMasterId = 0L;
				record.BuildState = record.CurrentDurability <= 0 ? XjSectFormationBuildState.Broken
					: record.CurrentDurability >= record.MaxDurability ? XjSectFormationBuildState.Operational
					: XjSectFormationBuildState.Damaged;
				changed = true;
			}
			if (changed) record.RuntimeVersion++;
		}
	}

	internal static void Clear()
	{
		BySectId.Clear();
		SectIdByFormationId.Clear();
	}

	private static bool IsValidLinkedTask(long taskId, long sectId, string taskKind)
	{
		return XjCraftDomainRegistry.TryGetTaskById(taskId, out XjCraftTaskArchiveRecord task)
			&& task != null
			&& task.IsOpen
			&& task.OwnerScope == XjCraftOwnerScope.Sect
			&& task.OwnerId == sectId
			&& string.Equals(task.ProfessionId, XjCraftProfessionIds.Formation, StringComparison.Ordinal)
			&& string.Equals(task.TaskKind, taskKind, StringComparison.Ordinal);
	}

	private static void MarkChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
			XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Formation | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Conflict);
	}

	private static long BuildUniqueFormationId(long sectId, int year)
	{
		long id = XjDeterministicHash.PositiveHash(sectId, "xuanjian.sect.formation.v1|" + Math.Max(0, year).ToString(CultureInfo.InvariantCulture));
		if (id <= 0L) id = sectId;
		while (SectIdByFormationId.ContainsKey(id)) id = id == long.MaxValue ? 1L : id + 1L;
		return id;
	}

	private static void Normalize(XjSectFormationArchiveRecord record)
	{
		record.SchemaVersion = XjFormationDomainSchema.CurrentVersion;
		record.Grade = Math.Clamp(record.Grade, 0, 3);
		EnsureDurabilityScale(record);
		record.MaxDurability = Math.Max(0, record.MaxDurability);
		record.CurrentDurability = Math.Clamp(record.CurrentDurability, 0, record.MaxDurability);
		record.OccupationSpeedMultiplier = Math.Clamp(record.OccupationSpeedMultiplier, 0.30f, 1f);
		record.CompletionGate = Math.Clamp(record.CompletionGate, 0.90f, 0.999f);
		record.BuildState ??= XjSectFormationBuildState.Unplanned;
		record.BlueprintId ??= string.Empty;
		if (record.BuildTaskId > 0L && record.Grade > 0) record.BuildState = XjSectFormationBuildState.Deploying;
		else if (record.RepairTaskId > 0L && record.Grade > 0) record.BuildState = XjSectFormationBuildState.Repairing;
		else if (record.Grade > 0 && record.CurrentDurability <= 0) record.BuildState = XjSectFormationBuildState.Broken;
	}

	private static void EnsureDurabilityScale(XjSectFormationArchiveRecord record)
	{
		if (record == null || record.Grade <= 0) return;
		int desiredMax = XjSectFormationBalance.GetMaxDurability(record.Grade);
		if (desiredMax <= 0 || record.MaxDurability >= desiredMax) return;
		RescaleDurability(record, desiredMax);
	}

	private static bool RescaleDurability(XjSectFormationArchiveRecord record, int desiredMax)
	{
		if (record == null || record.Grade <= 0 || desiredMax <= 0 || record.MaxDurability == desiredMax) return false;
		int oldMax = Math.Max(1, record.MaxDurability);
		float ratio = record.CurrentDurability <= 0 ? 0f : Math.Clamp(record.CurrentDurability / (float)oldMax, 0f, 1f);
		record.MaxDurability = desiredMax;
		record.CurrentDurability = Math.Clamp((int)Math.Round(desiredMax * ratio), 0, desiredMax);
		if (record.CurrentDurability >= record.MaxDurability && record.Grade > 0) record.BuildState = XjSectFormationBuildState.Operational;
		else if (record.CurrentDurability <= 0 && record.Grade > 0) record.BuildState = XjSectFormationBuildState.Broken;
		else if (!string.Equals(record.BuildState, XjSectFormationBuildState.Repairing, StringComparison.Ordinal)) record.BuildState = XjSectFormationBuildState.Damaged;
		record.RuntimeVersion++;
		MarkChanged();
		return true;
	}

	private static XjSectFormationArchiveRecord Clone(XjSectFormationArchiveRecord source)
	{
		return new XjSectFormationArchiveRecord
		{
			SchemaVersion = source.SchemaVersion,
			FormationId = source.FormationId,
			SectId = source.SectId,
			BlueprintId = source.BlueprintId ?? string.Empty,
			Grade = source.Grade,
			BuildState = source.BuildState ?? XjSectFormationBuildState.Unplanned,
			CurrentDurability = source.CurrentDurability,
			MaxDurability = source.MaxDurability,
			OccupationSpeedMultiplier = source.OccupationSpeedMultiplier,
			CompletionGate = source.CompletionGate,
			LeadFormationMasterId = source.LeadFormationMasterId,
			BuildTaskId = source.BuildTaskId,
			RepairTaskId = source.RepairTaskId,
			RequiredMaterialGrade = source.RequiredMaterialGrade,
			CreatedYear = source.CreatedYear,
			LastDamageYear = source.LastDamageYear,
			LastRepairYear = source.LastRepairYear,
			RuntimeVersion = source.RuntimeVersion
		};
	}
}
