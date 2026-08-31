using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.DongTian;

internal static class XjSecretRealmConstructionSystem
{
	private const string TaskKind = "SecretRealmConstruction";
	private static IReadOnlyList<XjSecretRealmArchiveRecord> PendingAnnualRealms = Array.Empty<XjSecretRealmArchiveRecord>();
	private static int _pendingAnnualYear;
	private static int _pendingAnnualIndex;

	internal static bool TickActor(Actor actor, int currentYear)
	{
		if (!XuanJianVNext.Systems.Archive.XjWorldSchemaGuard.GameplayEnabled || actor?.data == null || !actor.isAlive()
			|| currentYear <= 0 || !XjCraftTraitRules.CanPracticeFormations(actor)
			|| !XjCraftOwnerResolver.TryResolveSect(actor, out XjCraftOwnerKey owner)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;

		if (XjCraftDomainRegistry.TryGetOpenTask(actorId, out XjCraftTaskArchiveRecord openTask))
		{
			if (string.Equals(openTask.TaskKind, TaskKind, StringComparison.Ordinal))
			{
				// 已开工的玄韬工程仍逐年检查到期，确保既有 DueYear 不因性能降频而延后。
				AdvanceTask(actor, openTask, currentYear);
				return true;
			}
			return false;
		}

		// 只有“发现/开启新的秘境工程”进入三年稳定ID错峰；活跃工程不受此门禁影响。
		if ((currentYear + (int)(actorId % 3L + 3L) % 3) % 3 != 0) return false;

		XjSecretRealmArchiveRecord realm = XjSecretRealmRegistry.EnsureForSect(owner.OwnerId, currentYear);
		if (realm == null || realm.ActiveTaskId > 0L) return false;
		if (!realm.ConstructionMethodKnown)
		{
			XjSecretRealmRegistry.TryGrantConstructionMethod(owner.OwnerId, "宗门阵法师推演", currentYear);
			XjSecretRealmRegistry.TryGetBySectId(owner.OwnerId, out realm);
		}
		if (realm == null || !realm.ConstructionMethodKnown) return false;
		string stage = ResolveNextWorkStage(realm);
		if (stage.Length == 0) return false;
		if (!CanHostStage(actor, realm, stage)) return false;
		ProjectDefinition project = GetProject(stage);
		if (project.DurationYears <= 0) return false;
		SeedSectProjectResources(owner, project.Costs, currentYear);
		if (!realm.HasRetainedStageProgress && !HasResources(owner, project.Costs)) return false;
		if (realm.HasRetainedStageProgress) TryResumeTask(actor, owner, realm, stage, currentYear);
		else TryCreateTask(actor, owner, realm, stage, currentYear);
		return true;
	}

	internal static bool HasPendingForYear(int currentYear)
	{
		return currentYear > 0
			&& _pendingAnnualYear == currentYear
			&& _pendingAnnualIndex < PendingAnnualRealms.Count;
	}

	internal static bool BeginYear(int currentYear)
	{
		if (currentYear <= 0) return false;
		if (HasPendingForYear(currentYear)) return true;
		ClearPendingAnnualWork();
		PendingAnnualRealms = XjSecretRealmRegistry.ReadAll();
		_pendingAnnualYear = currentYear;
		_pendingAnnualIndex = 0;
		if (PendingAnnualRealms.Count > 0) return true;
		ClearPendingAnnualWork();
		return false;
	}

	internal static bool TickPending(int itemBudget, double timeBudgetMs)
	{
		if (_pendingAnnualYear <= 0 || _pendingAnnualIndex >= PendingAnnualRealms.Count)
		{
			ClearPendingAnnualWork();
			return true;
		}

		XjCooperativeBudget budget = new XjCooperativeBudget(
			Math.Max(1, itemBudget),
			timeBudgetMs,
			XjRuntimeFramePriority.Background);
		while (_pendingAnnualIndex < PendingAnnualRealms.Count && budget.TryTake())
		{
			XjSecretRealmArchiveRecord realm = PendingAnnualRealms[_pendingAnnualIndex++];
			ProcessAnnualRealm(realm, _pendingAnnualYear);
		}

		if (_pendingAnnualIndex < PendingAnnualRealms.Count) return false;
		ClearPendingAnnualWork();
		return true;
	}

	internal static void TickYear(int currentYear)
	{
		if (!BeginYear(currentYear)) return;
		while (_pendingAnnualIndex < PendingAnnualRealms.Count)
		{
			XjSecretRealmArchiveRecord realm = PendingAnnualRealms[_pendingAnnualIndex++];
			ProcessAnnualRealm(realm, currentYear);
		}
		ClearPendingAnnualWork();
	}

	private static void ProcessAnnualRealm(XjSecretRealmArchiveRecord realm, int currentYear)
	{
		if (realm == null || realm.SectId <= 0L) return;
		if (realm.SittingJinDanActorId <= 0L
			|| !XjScheduler.ResolveActor(realm.SittingJinDanActorId, out Actor sitting)
			|| sitting?.data == null
			|| !sitting.isAlive())
		{
			TryAssignAvailableJinDan(realm.SectId, currentYear);
		}
	}

	internal static void ClearPendingAnnualWork()
	{
		PendingAnnualRealms = Array.Empty<XjSecretRealmArchiveRecord>();
		_pendingAnnualYear = 0;
		_pendingAnnualIndex = 0;
	}

	private static string ResolveNextWorkStage(XjSecretRealmArchiveRecord realm)
	{
		if (realm == null) return string.Empty;
		return realm.Stage switch
		{
			XjSecretRealmStage.None => XjSecretRealmStage.SurveyingVoid,
			XjSecretRealmStage.LayingXuanTao => XjSecretRealmStage.LayingXuanTao,
			XjSecretRealmStage.SuppressingTreasure => XjSecretRealmStage.SuppressingTreasure,
			XjSecretRealmStage.NourishingSpace => XjSecretRealmStage.NourishingSpace,
			XjSecretRealmStage.StabilizingEntrance => XjSecretRealmStage.StabilizingEntrance,
			XjSecretRealmStage.Fudi when realm.SittingJinDanActorId > 0L => XjSecretRealmStage.UpgradingDongtian,
			_ => string.Empty
		};
	}

	private static bool CanHostStage(Actor actor, XjSecretRealmArchiveRecord realm, string stage)
	{
		int rank = XjCraftProficiencySystem.GetFormationRank(actor);
		int ordinal = GetProject(stage).StageOrdinal;
		if (ordinal <= 0 || rank < XjCraftProficiencySystem.RankZhuJi) return false;
		// 筑基级可完成勘定与铺设，紫府级可主持完整福地工程，金丹级方可升格洞天。
		if (ordinal <= 2) return rank >= XjCraftProficiencySystem.RankZhuJi;
		if (ordinal <= 5) return rank >= XjCraftProficiencySystem.RankZiFu;
		return realm.SittingJinDanActorId > 0L && rank >= XjCraftProficiencySystem.RankJinDan;
	}

	private static void TryCreateTask(Actor actor, XjCraftOwnerKey owner, XjSecretRealmArchiveRecord realm, string stage, int currentYear)
	{
		ProjectDefinition project = GetProject(stage);
		if (project.DurationYears <= 0 || !HasResources(owner, project.Costs)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		long taskId = BuildTaskId(actorId, realm.SectId, stage, currentYear);
		XjCraftTaskArchiveRecord task = new XjCraftTaskArchiveRecord
		{
			TaskId = taskId,
			ProfessionId = XjCraftProfessionIds.Formation,
			OwnerScope = owner.Scope,
			OwnerId = owner.OwnerId,
			ActorId = actorId,
			BlueprintId = "xj_secret_realm_" + stage,
			TaskKind = TaskKind,
			CreatedYear = currentYear,
			DueYear = currentYear + ResolveBuildDuration(actor, project.DurationYears),
			LastAdvancedYear = currentYear,
			Status = XjCraftTaskStatus.InProgress,
			MaterialsCommitted = true,
			MaterialCommitmentId = "secret.realm|" + taskId.ToString(CultureInfo.InvariantCulture),
			ResolutionKey = "secret.realm.resolve|" + taskId.ToString(CultureInfo.InvariantCulture),
			Quantity = project.StageOrdinal,
			FailureReason = stage
		};
		if (!XjCraftDomainRegistry.TryCommitTaskWithResources(task, owner, project.Costs, currentYear)) return;
		if (!XjSecretRealmRegistry.TryBeginStage(realm.SectId, stage, actorId, taskId, currentYear, task.DueYear))
		{
			XjCraftDomainRegistry.TryAbortTaskAndRefund(taskId, owner, project.Costs, currentYear, "玄韬工程状态冲突");
		}
	}

	private static void TryResumeTask(Actor actor, XjCraftOwnerKey owner, XjSecretRealmArchiveRecord realm, string stage, int currentYear)
	{
		ProjectDefinition project = GetProject(stage);
		if (project.DurationYears <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		long taskId = BuildTaskId(actorId, realm.SectId, stage + ".resume", currentYear);
		XjCraftTaskArchiveRecord task = new XjCraftTaskArchiveRecord
		{
			TaskId = taskId, ProfessionId = XjCraftProfessionIds.Formation, OwnerScope = owner.Scope, OwnerId = owner.OwnerId,
			ActorId = actorId, BlueprintId = "xj_secret_realm_" + stage, TaskKind = TaskKind, CreatedYear = currentYear,
			DueYear = currentYear + ResolveBuildDuration(actor, Math.Max(2, project.DurationYears / 2)), LastAdvancedYear = currentYear,
			Status = XjCraftTaskStatus.InProgress, MaterialsCommitted = true,
			MaterialCommitmentId = "secret.realm.resume|" + taskId.ToString(CultureInfo.InvariantCulture),
			ResolutionKey = "secret.realm.resume.resolve|" + taskId.ToString(CultureInfo.InvariantCulture),
			Quantity = project.StageOrdinal, FailureReason = stage
		};
		if (!XjCraftDomainRegistry.TryRegisterTask(task)) return;
		if (!XjSecretRealmRegistry.TryResumeStage(realm.SectId, actorId, taskId, currentYear, task.DueYear))
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "玄韬接续工程状态冲突";
			XjCraftDomainRegistry.TryWriteTask(task);
		}
	}

	private static void AdvanceTask(Actor actor, XjCraftTaskArchiveRecord task, int currentYear)
	{
		if (currentYear < task.DueYear)
		{
			if (currentYear > task.LastAdvancedYear) { task.LastAdvancedYear = currentYear; XjCraftDomainRegistry.TryWriteTask(task); }
			return;
		}
		if (!XjSecretRealmRegistry.TryGetBySectId(task.OwnerId, out XjSecretRealmArchiveRecord realm) || realm.ActiveTaskId != task.TaskId)
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "玄韬工程记录已失效";
			XjCraftDomainRegistry.TryWriteTask(task);
			return;
		}
		string stage = realm.Stage;
		int difficulty = Math.Clamp(task.Quantity, 1, 6);
		// 玄韬工程不再生成阵法品质或随机劣化结果；阵法品级只负责层次门槛，经验只缩短工期。
		if (!CanHostStage(actor, realm, stage))
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "主持阵法师品级不足，玄韬工程暂止";
			XjCraftDomainRegistry.TryWriteTask(task);
			return;
		}
		if (!XjSecretRealmRegistry.TryCompleteStage(realm.SectId, task.TaskId, stage, 0, currentYear))
		{
			task.Status = XjCraftTaskStatus.Interrupted;
			task.FailureReason = "玄韬工程验收失去有效记录";
			XjCraftDomainRegistry.TryWriteTask(task);
			return;
		}
		task.Success = true;
		task.OutcomeCalculated = true;
		task.OutputStored = true;
		task.ProgressApplied = true;
		task.Status = XjCraftTaskStatus.Completed;
		task.LastAdvancedYear = currentYear;
		XjCraftProficiencySystem.RecordFormationProgress(actor, 6 + difficulty * 2);
		XjSectContributionSystem.RecordCraftContribution(actor, 18f + difficulty * 6f, currentYear, "主持玄韬秘境工程");
		WriteHistory(actor, realm, stage, currentYear);
		XjCraftDomainRegistry.TryWriteTask(task);
	}

	private static int ResolveBuildDuration(Actor actor, int baseYears)
	{
		float speed = Math.Max(1f, XjCraftProficiencySystem.GetFormationBuildSpeedMultiplier(actor));
		return Math.Max(1, (int)Math.Ceiling(Math.Max(1, baseYears) / speed));
	}

	private static void TryAssignAvailableJinDan(long sectId, int currentYear)
	{
		if (!XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect.CityIds == null) return;
		IReadOnlyDictionary<long, List<long>> actorIdsByCityId = XjSectCultivatorCityIndex.GetCityIndex();
		long bestId = 0L;
		for (int c = 0; c < sect.CityIds.Count; c++)
		{
			long cityId = sect.CityIds[c];
			if (!actorIdsByCityId.TryGetValue(cityId, out List<long> actorIds) || actorIds == null) continue;
			for (int i = 0; i < actorIds.Count; i++)
			{
				long actorId = actorIds[i];
				if (actorId <= 0L || (bestId > 0L && actorId >= bestId) || !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()
					|| ReadActorSectId(actor) != sectId) continue;
				string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
				if (XjHighRealmIdentity.IsZhenJun(realmId)) bestId = actorId;
			}
		}
		if (bestId > 0L) XjSecretRealmRegistry.TryAssignSittingJinDan(sectId, bestId, currentYear);
	}

	private static long ReadActorSectId(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor);
	}

	private static ProjectDefinition GetProject(string stage)
	{
		return stage switch
		{
			XjSecretRealmStage.SurveyingVoid => new ProjectDefinition(1, 5, new[] { (XjCraftCollaborationSystem.FormationRune, 8), (XjCraftCollaborationSystem.SpiritMaterial, 4) }),
			XjSecretRealmStage.LayingXuanTao => new ProjectDefinition(2, 10, new[] { (XjCraftCollaborationSystem.FormationFlagHigh, 8), (XjCraftCollaborationSystem.FormationPlateHigh, 2), (XjCraftCollaborationSystem.FormationRune, 16), (XjCraftCollaborationSystem.SpiritMaterial, 8) }),
			XjSecretRealmStage.SuppressingTreasure => new ProjectDefinition(3, 8, new[] { (XjCraftCollaborationSystem.FormationPlateHigh, 2), (XjCraftCollaborationSystem.SpiritMaterial, 12) }),
			XjSecretRealmStage.NourishingSpace => new ProjectDefinition(4, 15, new[] { (XjCraftCollaborationSystem.FormationFlagHigh, 12), (XjCraftCollaborationSystem.FormationRune, 24), (XjCraftCollaborationSystem.SpiritMaterial, 18) }),
			XjSecretRealmStage.StabilizingEntrance => new ProjectDefinition(5, 5, new[] { (XjCraftCollaborationSystem.FormationPlateHigh, 3), (XjCraftCollaborationSystem.FormationFlagHigh, 10), (XjCraftCollaborationSystem.FormationRune, 18) }),
			XjSecretRealmStage.UpgradingDongtian => new ProjectDefinition(6, 20, new[] { (XjCraftCollaborationSystem.FormationPlateHigh, 8), (XjCraftCollaborationSystem.FormationFlagHigh, 24), (XjCraftCollaborationSystem.FormationRune, 40), (XjCraftCollaborationSystem.SpiritMaterial, 30) }),
			_ => default
		};
	}

	private static bool HasResources(XjCraftOwnerKey owner, IReadOnlyList<(string ResourceId, int Amount)> costs)
	{
		if (costs == null || costs.Count == 0) return false;
		for (int i = 0; i < costs.Count; i++) if (XjCraftDomainRegistry.GetResourceCount(owner, costs[i].ResourceId) < costs[i].Amount) return false;
		return true;
	}

	private static void SeedSectProjectResources(XjCraftOwnerKey owner, IReadOnlyList<(string ResourceId, int Amount)> costs, int currentYear)
	{
		if (!owner.IsValid || owner.Scope != XjCraftOwnerScope.Sect || costs == null) return;
		for (int i = 0; i < costs.Count; i++)
		{
			(string resourceId, int amount) = costs[i];
			if (string.IsNullOrWhiteSpace(resourceId) || amount <= 0) continue;
			int current = XjCraftDomainRegistry.GetResourceCount(owner, resourceId);
			if (current < amount)
			{
				XjCraftDomainRegistry.TryAddResource(owner, resourceId, amount - current, currentYear);
			}
		}
	}

	private static long BuildTaskId(long actorId, long sectId, string stage, int currentYear)
	{
		long id = XjDeterministicHash.PositiveHash(actorId, "xuanjian.secret.realm.task.v1|" + sectId.ToString(CultureInfo.InvariantCulture) + "|" + stage + "|" + currentYear.ToString(CultureInfo.InvariantCulture));
		return id <= 0L ? Math.Max(1L, actorId) : id;
	}

	private static void WriteHistory(Actor actor, XjSecretRealmArchiveRecord realm, string stage, int currentYear)
	{
		string name;
		try { name = actor.getName(); } catch { name = "未名阵法师"; }
		string stageName = TranslateStage(stage);
		string title = stage == XjSecretRealmStage.StabilizingEntrance ? "福地营造成形"
			: stage == XjSecretRealmStage.UpgradingDongtian ? "宗门洞天升格"
			: "玄韬工程推进";
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.SecretRealm, title,
			name + "主持" + (realm.DisplayName ?? "宗门秘境") + "的" + stageName + "阶段完成。",
			stage == XjSecretRealmStage.UpgradingDongtian ? 5 : 4, actorId: ((BaseSystemData)actor.data).id,
			actorName: name, sectId: realm.SectId, cityId: realm.EntranceCityId, year: currentYear,
			iconIdOverride: XjEventIconCatalog.ZongMenCreation);
	}

	private static string TranslateStage(string stage)
	{
		if (stage == XjSecretRealmStage.SurveyingVoid) return "勘定太虚";
		if (stage == XjSecretRealmStage.LayingXuanTao) return "布置玄韬";
		if (stage == XjSecretRealmStage.SuppressingTreasure) return "镇压灵宝";
		if (stage == XjSecretRealmStage.NourishingSpace) return "滋养空间";
		if (stage == XjSecretRealmStage.StabilizingEntrance) return "稳定入口";
		if (stage == XjSecretRealmStage.UpgradingDongtian) return "洞天升格";
		return "秘境营造";
	}

	private readonly struct ProjectDefinition
	{
		internal ProjectDefinition(int stageOrdinal, int durationYears, IReadOnlyList<(string ResourceId, int Amount)> costs)
		{
			StageOrdinal = stageOrdinal;
			DurationYears = durationYears;
			Costs = costs;
		}
		internal int StageOrdinal { get; }
		internal int DurationYears { get; }
		internal IReadOnlyList<(string ResourceId, int Amount)> Costs { get; }
	}
}
