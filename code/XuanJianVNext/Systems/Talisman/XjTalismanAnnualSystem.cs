using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Talisman;

internal static class XjTalismanAnnualSystem
{

	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || !actor.isAlive() || !XjCraftTraitRules.CanPracticeTalismans(actor)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !XjCraftOwnerResolver.TryResolvePreferred(actor, out XjCraftOwnerKey owner)) return;
		TryGatherMaterials(actor, owner, currentYear);
		if (XjCraftDomainRegistry.TryGetOpenTask(actorId, out XjCraftTaskArchiveRecord task))
		{
			AdvanceTask(actor, owner, task, currentYear);
			return;
		}
		if (!XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.TalismanAutonomousPlan, actorId, currentYear)) return;
		TryCreateTask(actor, owner, currentYear);
	}

	private static void TryGatherMaterials(Actor actor, XjCraftOwnerKey owner, int currentYear)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.TalismanMaterialGather, actorId, currentYear)) return;
		int realmOrder = XjRealmHelper.GetOrder(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		bool high = realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZhuJi);
		int lowPaper = 3 + XjDeterministicHash.PositiveIndex(actorId + currentYear, "talisman.paper.low", 5);
		int lowInk = 2 + XjDeterministicHash.PositiveIndex(actorId + currentYear, "talisman.ink.low", 4);
		XjCraftDomainRegistry.TryAddResource(owner, XjTalismanCatalog.LowPaper, lowPaper, currentYear);
		XjCraftDomainRegistry.TryAddResource(owner, XjTalismanCatalog.LowInk, lowInk, currentYear);
		if (high && XjDeterministicHash.PositiveIndex(actorId + currentYear, "talisman.material.high", 100) < 60)
		{
			int highAmount = 1 + XjDeterministicHash.PositiveIndex(actorId + currentYear, "talisman.material.high.amount", 2);
			XjCraftDomainRegistry.TryAddResource(owner, XjTalismanCatalog.HighPaper, highAmount, currentYear);
			XjCraftDomainRegistry.TryAddResource(owner, XjTalismanCatalog.HighInk, highAmount, currentYear);
		}
		if (XjCraftDomainRegistry.GetResourceCount(owner, XjTalismanCatalog.BrushDurability) <= 0)
		{
			XjCraftDomainRegistry.TryAddResource(owner, XjTalismanCatalog.BrushDurability, high ? 24 : 14, currentYear);
		}
	}

	private static void TryCreateTask(Actor actor, XjCraftOwnerKey owner, int currentYear)
	{
		IReadOnlyList<XjTalismanDefinition> all = XjTalismanCatalog.All;
		List<XjTalismanDefinition> eligible = new List<XjTalismanDefinition>();
		int craftRank = XjCraftProficiencySystem.GetTalismanRank(actor);
		for (int i = 0; i < all.Count; i++)
		{
			XjTalismanDefinition definition = all[i];
			if (!XjCraftProficiencySystem.CanProcessRealm(actor, craftRank, definition.MinRealmId)) continue;
			if (!HasMaterials(owner, definition)) continue;
			eligible.Add(definition);
		}
		if (eligible.Count == 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		XjTalismanDefinition selected = eligible[XjDeterministicHash.PositiveIndex(actorId + currentYear, "talisman.recipe", eligible.Count)];
		List<(string ResourceId, int Amount)> costs = new List<(string ResourceId, int Amount)>
		{
			(selected.PaperId, selected.PaperCount),
			(selected.InkId, selected.InkCount),
			(XjTalismanCatalog.BrushDurability, 1)
		};
		long taskId = BuildTaskId(actorId, selected.Id, currentYear);
		XjCraftTaskArchiveRecord task = new XjCraftTaskArchiveRecord
		{
			TaskId = taskId,
			ProfessionId = XjCraftProfessionIds.Talisman,
			OwnerScope = owner.Scope,
			OwnerId = owner.OwnerId,
			ActorId = actorId,
			BlueprintId = selected.Id,
			TaskKind = "TalismanBatch",
			CreatedYear = currentYear,
			DueYear = currentYear + Math.Max(1, selected.DurationYears / 2),
			LastAdvancedYear = currentYear,
			Status = XjCraftTaskStatus.InProgress,
			MaterialsCommitted = true,
			MaterialCommitmentId = "talisman|" + taskId.ToString(CultureInfo.InvariantCulture),
			ResolutionKey = "talisman.resolve|" + taskId.ToString(CultureInfo.InvariantCulture)
		};
		XjCraftDomainRegistry.TryCommitTaskWithResources(task, owner, costs, currentYear);
	}

	private static void AdvanceTask(Actor actor, XjCraftOwnerKey owner, XjCraftTaskArchiveRecord task, int currentYear)
	{
		if (!string.Equals(task.ProfessionId, XjCraftProfessionIds.Talisman, StringComparison.Ordinal)) return;
		if (currentYear < task.DueYear)
		{
			if (currentYear > task.LastAdvancedYear)
			{
				task.LastAdvancedYear = currentYear;
				XjCraftDomainRegistry.TryWriteTask(task);
			}
			return;
		}
		if (!XjTalismanCatalog.TryGet(task.BlueprintId, out XjTalismanDefinition definition))
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "符法不存在";
			XjCraftDomainRegistry.TryWriteTask(task);
			return;
		}
		if (!task.OutcomeCalculated)
		{
			float huiGuang = XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float value) ? Math.Max(0f, value) : 0f;
			float chance = definition.BaseSuccessRate
				+ XjCraftProficiencySystem.GetTalismanSuccessBonus(actor)
				+ Math.Min(0.12f, huiGuang / 1800f)
				- definition.Difficulty * 0.015f;
			chance = Math.Clamp(chance, 0.18f, 0.96f);
			long actorId = ((BaseSystemData)actor.data).id;
			bool daoZhu = actor.hasTrait("ChuShen8");
			task.Success = daoZhu || XjDeterministicHash.Roll01(actorId, task.DueYear, definition.Id, task.ResolutionKey) <= chance;
			// 符箓只保留固定效果，不再生成成品品级；符师品级与经验只影响成功率和产出率。
			task.Quality = task.Success ? 1 : 0;
			int baseQuantity = task.Success
				? XjDeterministicHash.RollRange(actorId, task.DueYear, definition.Difficulty, definition.MinYield, definition.MaxYield)
				: 0;
			task.Quantity = task.Success ? XjCraftProficiencySystem.ResolveTalismanYield(actor, baseQuantity) : 0;
			task.OutcomeCalculated = true;
			task.Status = XjCraftTaskStatus.Resolved;
		}
		if (!task.Success && !task.OutputStored) task.OutputStored = true;
		if (task.Success && !task.OutputStored)
		{
			long batchId = XjDeterministicHash.PositiveHash(task.TaskId, "xuanjian.talisman.batch.v1");
			if (batchId <= 0L) batchId = task.TaskId;
			bool created = XjCraftDomainRegistry.TryAddTalismanBatch(new XjTalismanBatchArchiveRecord
			{
				BatchId = batchId,
				OwnerScope = task.OwnerScope,
				OwnerId = task.OwnerId,
				TalismanId = definition.Id,
				Quantity = task.Quantity,
				Quality = task.Quality,
				CrafterActorId = task.ActorId,
				CraftedYear = currentYear,
				TaskId = task.TaskId
			});
			if (!created && !XjCraftDomainRegistry.HasTalismanBatch(batchId))
			{
				task.FailureReason = "符箓批次入库失败，等待下次重试";
				task.LastAdvancedYear = currentYear;
				XjCraftDomainRegistry.TryWriteTask(task);
				return;
			}
			task.OutputStored = true;
			if (created) XjCraftCollaborationSystem.RecordTalismanOutput(actor, task.Quantity, currentYear);
		}
		if (!task.ProgressApplied)
		{
			XjCraftProficiencySystem.RecordTalismanProgress(actor, task.Success ? 3 + task.Quantity : 2);
			task.ProgressApplied = true;
			XjSectContributionSystem.RecordCraftContribution(actor, task.Success ? 2f + task.Quantity : 0.5f, currentYear, "符箓供给");
		}
		if (!task.HistoryWritten)
		{
			// 符箓属于高频四艺产出，不再生成公告或史册记录。
			task.HistoryWritten = true;
		}

		task.Status = XjCraftTaskStatus.Completed;
		task.LastAdvancedYear = currentYear;
		XjCraftDomainRegistry.TryWriteTask(task);
	}

	private static bool HasMaterials(XjCraftOwnerKey owner, XjTalismanDefinition definition)
	{
		return XjCraftDomainRegistry.GetResourceCount(owner, definition.PaperId) >= definition.PaperCount
			&& XjCraftDomainRegistry.GetResourceCount(owner, definition.InkId) >= definition.InkCount
			&& XjCraftDomainRegistry.GetResourceCount(owner, XjTalismanCatalog.BrushDurability) >= 1;
	}

	private static long BuildTaskId(long actorId, string talismanId, int currentYear)
	{
		long id = XjDeterministicHash.PositiveHash(actorId, "xuanjian.talisman.task.v1|" + talismanId + "|" + currentYear.ToString(CultureInfo.InvariantCulture));
		return id <= 0L ? Math.Max(1L, actorId) : id;
	}

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名符师" : name.Trim();
		}
		catch { return "未名符师"; }
	}
}
