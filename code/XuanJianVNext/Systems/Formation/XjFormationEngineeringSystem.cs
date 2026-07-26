using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.Systems.Formation;

internal static class XjFormationEngineeringSystem
{
	private const string CommissionTaskKind = "SectFormationCommission";
	private const string RepairTaskKind = "SectFormationRepair";
	private const string FamilyCommissionTaskKind = "FamilyFormationCommission";
	private const string FamilyFormationGuardResource = "xj_family_formation_guard";

	internal static void TickActor(Actor actor, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || actor?.data == null || !actor.isAlive() || currentYear <= 0
			|| !XjCraftTraitRules.CanPracticeFormations(actor)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		if (!XjCraftOwnerResolver.TryResolveSect(actor, out XjCraftOwnerKey owner))
		{
			if (XjCraftOwnerResolver.TryResolveFamily(actor, out XjCraftOwnerKey familyOwner))
			{
				TickFamilyFormationActor(actor, familyOwner, currentYear);
			}
			return;
		}
		long sectId = owner.OwnerId;
		// 宗门工程只由实际驻守山门的阵法师承担，避免迁城残留的成员镜像跨国维修旧阵。
		if (!XjSectHighRealmResidenceSystem.IsAtSectGate(actor, sectId)) return;
		GatherBasicMaterials(actor, owner, currentYear);
		if (XjCraftDomainRegistry.TryGetOpenTask(actorId, out XjCraftTaskArchiveRecord openTask))
		{
			if (string.Equals(openTask.ProfessionId, XjCraftProfessionIds.Formation, StringComparison.Ordinal))
			{
				AdvanceTask(actor, sectId, openTask, currentYear);
			}
			return;
		}

		XjSectFormationArchiveRecord formation = XjSectFormationRegistry.EnsurePlanned(sectId, currentYear);
		if (formation == null) return;
		if (formation.Grade <= 0 && string.Equals(formation.BuildState, XjSectFormationBuildState.Unplanned, StringComparison.Ordinal))
		{
			TryCreateCommissionTask(actor, owner, formation, currentYear);
			return;
		}
		if (formation.Grade > 0 && formation.CurrentDurability <= 0 && formation.BuildTaskId <= 0L
			&& string.Equals(formation.BuildState, XjSectFormationBuildState.Deploying, StringComparison.Ordinal))
		{
			TryResumeCommissionTask(actor, owner, formation, currentYear);
			return;
		}
		if (formation.Grade > 0 && formation.CurrentDurability < formation.MaxDurability * 0.90f && formation.RepairTaskId <= 0L)
		{
			TryCreateRepairTask(actor, owner, formation, currentYear);
		}
	}

	private static void TickFamilyFormationActor(Actor actor, XjCraftOwnerKey owner, int currentYear)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		GatherBasicMaterials(actor, owner, currentYear);
		if (XjCraftDomainRegistry.TryGetOpenTask(actorId, out XjCraftTaskArchiveRecord openTask))
		{
			if (string.Equals(openTask.ProfessionId, XjCraftProfessionIds.Formation, StringComparison.Ordinal)
				&& string.Equals(openTask.TaskKind, FamilyCommissionTaskKind, StringComparison.Ordinal))
			{
				AdvanceFamilyTask(actor, openTask, currentYear);
			}
			return;
		}

		if (!XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.FormationFamilyCommission, owner.OwnerId, currentYear))
		{
			return;
		}

		int resolvedGrade = ResolveBuildGrade(actor);
		if (resolvedGrade <= 0)
		{
			return;
		}
		int grade = Math.Clamp(resolvedGrade, 1, 2);

		float chance = 0.35f;
		if (XjDeterministicHash.Roll01(actorId, currentYear, "family.formation.start", owner.OwnerId.ToString(CultureInfo.InvariantCulture)) > chance)
		{
			return;
		}

		TryCreateFamilyFormationTask(actor, owner, grade, currentYear);
	}

	private static void GatherBasicMaterials(Actor actor, XjCraftOwnerKey owner, int currentYear)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.FormationMaterialGather, actorId, currentYear)) return;
		int rank = XjCraftProficiencySystem.GetFormationRank(actor);
		XjCraftDomainRegistry.TryAddResource(owner, XjCraftCollaborationSystem.FormationRune, 5 + rank * 3, currentYear);
		XjCraftDomainRegistry.TryAddResource(owner, XjCraftCollaborationSystem.SpiritMaterial, Math.Max(3, rank + 1), currentYear);
		XjCraftDomainRegistry.TryAddResource(owner, XjCraftCollaborationSystem.FormationFlagLow, 3 + rank * 2, currentYear);
		if (rank >= XjCraftProficiencySystem.RankZiFu)
		{
			XjCraftDomainRegistry.TryAddResource(owner, XjCraftCollaborationSystem.FormationFlagHigh, 2 + rank / 2, currentYear);
			XjCraftDomainRegistry.TryAddResource(owner, XjCraftCollaborationSystem.FormationPlateHigh, 2, currentYear);
		}
		XjCraftProficiencySystem.RecordFormationProgress(actor, 1);
	}

	private static void TryCreateCommissionTask(Actor actor, XjCraftOwnerKey owner, XjSectFormationArchiveRecord formation, int currentYear)
	{
		int grade = ResolveBuildGrade(actor);
		if (grade <= 0) return;
		XjFormationProjectDefinition project = XjFormationMaterialCatalog.Get(grade);
		if (!HasResources(owner, project.Costs)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		long taskId = BuildTaskId(actorId, formation.SectId, CommissionTaskKind, currentYear);
		XjCraftTaskArchiveRecord task = new XjCraftTaskArchiveRecord
		{
			TaskId = taskId,
			ProfessionId = XjCraftProfessionIds.Formation,
			OwnerScope = owner.Scope,
			OwnerId = owner.OwnerId,
			ActorId = actorId,
			BlueprintId = project.BlueprintId,
			TaskKind = CommissionTaskKind,
			CreatedYear = currentYear,
			DueYear = currentYear + ResolveBuildDuration(actor, project.DurationYears),
			LastAdvancedYear = currentYear,
			Status = XjCraftTaskStatus.InProgress,
			MaterialsCommitted = true,
			MaterialCommitmentId = "formation.build|" + taskId.ToString(CultureInfo.InvariantCulture),
			ResolutionKey = "formation.build.resolve|" + taskId.ToString(CultureInfo.InvariantCulture),
			Quantity = grade
		};
		if (!XjCraftDomainRegistry.TryCommitTaskWithResources(task, owner, project.Costs, currentYear)) return;
		if (!XjSectFormationRegistry.TryBeginCommission(formation.SectId, grade, project.BlueprintId, actorId, taskId, currentYear))
		{
			XjCraftDomainRegistry.TryAbortTaskAndRefund(taskId, owner, project.Costs, currentYear, "宗门大阵工程状态冲突");
		}
	}

	private static void TryResumeCommissionTask(Actor actor, XjCraftOwnerKey owner, XjSectFormationArchiveRecord formation, int currentYear)
	{
		if (formation == null || ResolveBuildGrade(actor) < formation.Grade) return;
		long actorId = ((BaseSystemData)actor.data).id;
		XjFormationProjectDefinition project = XjFormationMaterialCatalog.Get(formation.Grade);
		long taskId = BuildTaskId(actorId, formation.SectId, CommissionTaskKind + ".resume", currentYear);
		XjCraftTaskArchiveRecord task = new XjCraftTaskArchiveRecord
		{
			TaskId = taskId, ProfessionId = XjCraftProfessionIds.Formation, OwnerScope = owner.Scope, OwnerId = owner.OwnerId, ActorId = actorId,
			BlueprintId = formation.BlueprintId, TaskKind = CommissionTaskKind, CreatedYear = currentYear,
			DueYear = currentYear + ResolveBuildDuration(actor, project.DurationYears), LastAdvancedYear = currentYear,
			Status = XjCraftTaskStatus.InProgress, MaterialsCommitted = true,
			MaterialCommitmentId = "formation.resume|" + taskId.ToString(CultureInfo.InvariantCulture),
			ResolutionKey = "formation.resume.resolve|" + taskId.ToString(CultureInfo.InvariantCulture), Quantity = formation.Grade, Quality = 1
		};
		if (!XjCraftDomainRegistry.TryRegisterTask(task) || !XjSectFormationRegistry.TryResumeCommission(formation.SectId, actorId, taskId))
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "接续大阵工程状态冲突";
			XjCraftDomainRegistry.TryWriteTask(task);
		}
	}

	private static void TryCreateRepairTask(Actor actor, XjCraftOwnerKey owner, XjSectFormationArchiveRecord formation, int currentYear)
	{
		if (formation == null || ResolveBuildGrade(actor) < formation.Grade) return;
		int missing = Math.Max(0, formation.MaxDurability - formation.CurrentDurability);
		if (missing <= 0) return;
		if (XjSectFormationRegistry.IsBrokenRepairLocked(formation, currentYear)) return;
		XjFormationProjectDefinition project = XjFormationMaterialCatalog.Get(formation.Grade);
		IReadOnlyList<(string ResourceId, int Amount)> costs = XjFormationMaterialCatalog.BuildRepairCosts(formation.Grade, missing, formation.MaxDurability);
		if (!HasResources(owner, costs)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		long taskId = BuildTaskId(actorId, formation.SectId, RepairTaskKind, currentYear);
		XjCraftTaskArchiveRecord task = new XjCraftTaskArchiveRecord
		{
			TaskId = taskId,
			ProfessionId = XjCraftProfessionIds.Formation,
			OwnerScope = owner.Scope,
			OwnerId = owner.OwnerId,
			ActorId = actorId,
			BlueprintId = formation.BlueprintId,
			TaskKind = RepairTaskKind,
			CreatedYear = currentYear,
			DueYear = currentYear + ResolveBuildDuration(actor, project.RepairDurationYears),
			LastAdvancedYear = currentYear,
			Status = XjCraftTaskStatus.InProgress,
			MaterialsCommitted = true,
			MaterialCommitmentId = "formation.repair|" + taskId.ToString(CultureInfo.InvariantCulture),
			ResolutionKey = "formation.repair.resolve|" + taskId.ToString(CultureInfo.InvariantCulture),
			Quantity = missing
		};
		if (!XjCraftDomainRegistry.TryCommitTaskWithResources(task, owner, costs, currentYear)) return;
		if (!XjSectFormationRegistry.TryBeginRepair(formation.SectId, actorId, taskId, currentYear))
		{
			XjCraftDomainRegistry.TryAbortTaskAndRefund(taskId, owner, costs, currentYear, "宗门大阵维修状态冲突");
		}
	}

	private static void AdvanceTask(Actor actor, long sectId, XjCraftTaskArchiveRecord task, int currentYear)
	{
		if (currentYear < task.DueYear)
		{
			if (currentYear > task.LastAdvancedYear)
			{
				task.LastAdvancedYear = currentYear;
				XjCraftDomainRegistry.TryWriteTask(task);
			}
			return;
		}
		if (string.Equals(task.TaskKind, CommissionTaskKind, StringComparison.Ordinal)) ResolveCommission(actor, sectId, task, currentYear);
		else if (string.Equals(task.TaskKind, RepairTaskKind, StringComparison.Ordinal)) ResolveRepair(actor, sectId, task, currentYear);
		else
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "未知阵法工程";
			XjCraftDomainRegistry.TryWriteTask(task);
		}
	}

	private static void ResolveCommission(Actor actor, long sectId, XjCraftTaskArchiveRecord task, int currentYear)
	{
		int grade = Math.Clamp(task.Quantity, 1, 3);
		// 阵法品级与经验只决定可搭建层次和工程速度，不再生成品质或随机劣化结果。
		int durabilityPenalty = 0;
		if (!XjSectFormationRegistry.TryCompleteCommission(sectId, task.TaskId, durabilityPenalty, currentYear))
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "大阵工程验收失去有效宗门记录";
			XjCraftDomainRegistry.TryWriteTask(task);
			return;
		}
		task.Success = true;
		task.OutcomeCalculated = true;
		task.OutputStored = true;
		task.ProgressApplied = true;
		task.Status = XjCraftTaskStatus.Completed;
		task.LastAdvancedYear = currentYear;
		XjCraftProficiencySystem.RecordFormationProgress(actor, 4 + grade * 3);
		XjSectContributionSystem.RecordCraftContribution(actor, 10f + grade * 8f, currentYear, "主持宗门大阵建设");
		string formationName = FormatFormationName(grade);
		WriteHistory(actor, "宗门大阵建成", "主持完成" + formationName + "部署。", grade >= 2 ? 4 : 3, currentYear);
		XjCraftDomainRegistry.TryWriteTask(task);
	}

	private static void TryCreateFamilyFormationTask(Actor actor, XjCraftOwnerKey owner, int grade, int currentYear)
	{
		if (!owner.IsValid || owner.Scope != XjCraftOwnerScope.Family)
		{
			return;
		}

		int safeGrade = Math.Clamp(grade, 1, 2);
		XjFormationProjectDefinition project = XjFormationMaterialCatalog.Get(safeGrade);
		if (!HasResources(owner, project.Costs))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		long taskId = BuildTaskId(actorId, owner.OwnerId, FamilyCommissionTaskKind, currentYear);
		XjCraftTaskArchiveRecord task = new XjCraftTaskArchiveRecord
		{
			TaskId = taskId,
			ProfessionId = XjCraftProfessionIds.Formation,
			OwnerScope = owner.Scope,
			OwnerId = owner.OwnerId,
			ActorId = actorId,
			BlueprintId = project.BlueprintId,
			TaskKind = FamilyCommissionTaskKind,
			CreatedYear = currentYear,
			DueYear = currentYear + ResolveBuildDuration(actor, project.DurationYears),
			LastAdvancedYear = currentYear,
			Status = XjCraftTaskStatus.InProgress,
			MaterialsCommitted = true,
			MaterialCommitmentId = "family.formation.build|" + taskId.ToString(CultureInfo.InvariantCulture),
			ResolutionKey = "family.formation.resolve|" + taskId.ToString(CultureInfo.InvariantCulture),
			Quantity = safeGrade
		};
		if (!XjCraftDomainRegistry.TryCommitTaskWithResources(task, owner, project.Costs, currentYear))
		{
			return;
		}
	}

	private static void AdvanceFamilyTask(Actor actor, XjCraftTaskArchiveRecord task, int currentYear)
	{
		if (currentYear < task.DueYear)
		{
			if (currentYear > task.LastAdvancedYear)
			{
				task.LastAdvancedYear = currentYear;
				XjCraftDomainRegistry.TryWriteTask(task);
			}
			return;
		}

		int grade = Math.Clamp(task.Quantity, 1, 2);
		// 家族阵法同样只受布阵速度影响，完成后效果固定。

		task.Success = true;
		task.OutcomeCalculated = true;
		task.OutputStored = true;
		task.ProgressApplied = true;
		task.Status = XjCraftTaskStatus.Completed;
		task.LastAdvancedYear = currentYear;
		XjCraftProficiencySystem.RecordFormationProgress(actor, 3 + grade * 2);
		EnsureFamilyFormationGuard(new XjCraftOwnerKey(task.OwnerScope, task.OwnerId), grade, currentYear);
		string formationName = FormatFamilyFormationName(grade);
		WriteHistory(actor, "家族阵法建成", "主持完成" + formationName + "布设，本族修士获得守御加护。", 2, currentYear);
		XjCraftDomainRegistry.TryWriteTask(task);
	}

	private static void EnsureFamilyFormationGuard(XjCraftOwnerKey owner, int grade, int currentYear)
	{
		if (!owner.IsValid || owner.Scope != XjCraftOwnerScope.Family) return;
		int safeGrade = Math.Clamp(grade, 1, 2);
		int currentGrade = XjCraftDomainRegistry.GetResourceCount(owner, FamilyFormationGuardResource);
		if (currentGrade < safeGrade)
		{
			XjCraftDomainRegistry.TryAddResource(owner, FamilyFormationGuardResource, safeGrade - currentGrade, currentYear);
		}

		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(owner.OwnerId))
		{
			if (member?.data == null || !member.isAlive()) continue;
			XjActorAccessor.TryGetInt(member, XjActorDataKeys.XjFamilyFormationGuardGrade, out int existingGrade);
			if (existingGrade >= safeGrade) continue;
			XjActorAccessor.SetInt(member, XjActorDataKeys.XjFamilyFormationGuardGrade, safeGrade);
			XjCombatHotPathCache.Refresh(member);
		}
	}

	private static void ResolveRepair(Actor actor, long sectId, XjCraftTaskArchiveRecord task, int currentYear)
	{
		if (!XjSectFormationRegistry.TryGet(sectId, out XjSectFormationArchiveRecord formation) || formation == null)
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "维修对象已不存在";
			XjCraftDomainRegistry.TryWriteTask(task);
			return;
		}
		int intended = Math.Max(1, task.Quantity);
		int points = Math.Max(1, (int)Math.Round(intended * XjCraftProficiencySystem.GetFormationRepairMultiplier(actor)));
		int applied = XjSectFormationRegistry.CompleteRepair(sectId, task.TaskId, points, currentYear);
		task.Success = applied > 0;
		task.Quantity = applied;
		task.OutcomeCalculated = true;
		task.OutputStored = true;
		task.ProgressApplied = true;
		task.Status = XjCraftTaskStatus.Completed;
		task.LastAdvancedYear = currentYear;
		XjCraftProficiencySystem.RecordFormationProgress(actor, 3 + formation.Grade);
		XjSectContributionSystem.RecordCraftContribution(actor, Math.Max(1f, applied / 100f), currentYear, "维修宗门大阵");
		if (formation.CurrentDurability <= 0 || applied >= formation.MaxDurability / 3)
		{
			string formationName = FormatFormationName(formation.Grade);
			WriteHistory(actor, "宗门大阵维修", "主持修复" + formationName + "耐久" + applied + "点。", 2, currentYear);
		}
		XjCraftDomainRegistry.TryWriteTask(task);
	}

	internal static void HandleActorRemoved(long actorId)
	{
		XjSectFormationRegistry.ReleaseLeadActorTasks(actorId);
	}

	private static int ResolveBuildDuration(Actor actor, int baseYears)
	{
		float speed = Math.Max(1f, XjCraftProficiencySystem.GetFormationBuildSpeedMultiplier(actor));
		return Math.Max(1, (int)Math.Ceiling(Math.Max(1, baseYears) / speed));
	}

	private static int ResolveBuildGrade(Actor actor)
	{
		int rank = XjCraftProficiencySystem.GetFormationRank(actor);
		if (rank >= XjCraftProficiencySystem.RankJinDan) return 3;
		if (rank >= XjCraftProficiencySystem.RankZiFu) return 2;
		if (rank >= XjCraftProficiencySystem.RankZhuJi) return 1;
		return 0;
	}

	private static bool HasResources(XjCraftOwnerKey owner, IReadOnlyList<(string ResourceId, int Amount)> costs)
	{
		for (int i = 0; i < costs.Count; i++) if (XjCraftDomainRegistry.GetResourceCount(owner, costs[i].ResourceId) < costs[i].Amount) return false;
		return true;
	}

	private static long BuildTaskId(long actorId, long sectId, string kind, int currentYear)
	{
		long id = XjDeterministicHash.PositiveHash(actorId, "xuanjian.formation.task.v1|" + sectId.ToString(CultureInfo.InvariantCulture)
			+ "|" + kind + "|" + currentYear.ToString(CultureInfo.InvariantCulture));
		return id <= 0L ? Math.Max(1L, actorId) : id;
	}

	private static void WriteHistory(Actor actor, string title, string body, int importance, int year)
	{
		// 阵法建设与维修属于宗门日常工程，不写入史册。
	}


	internal static string FormatFormationName(int grade)
	{
		return grade switch
		{
			1 => "家族小阵",
			2 => "城镇守御阵",
			3 => "宗门大阵",
			_ => "未名阵法"
		};
	}

	private static string FormatFamilyFormationName(int grade)
	{
		return grade >= 2 ? "家族守御阵" : "家族小阵";
	}

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名阵法师" : name.Trim();
		}
		catch
		{
			return "未名阵法师";
		}
	}
}
