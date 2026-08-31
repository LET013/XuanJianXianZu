using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.Alchemy;

/// <summary>
/// Low-frequency, actor-routed alchemy production. Material gathering shares the
/// existing annual cultivator lane; recipe/task work is gated by the alchemy trait.
/// No world scan, no historical-task scan and no unconditional pill-batch scan is
/// allowed on this path.
/// </summary>
internal static class XjAlchemyAnnualSystem
{
	private readonly struct RecipeOpportunity
	{
		internal readonly string RecipeId;
		internal readonly int MinimumCraftRank;
		internal readonly int ChancePerHundred;
		internal readonly int Bit;

		internal RecipeOpportunity(string recipeId, int minimumCraftRank, int chancePerHundred, int bit)
		{
			RecipeId = recipeId;
			MinimumCraftRank = minimumCraftRank;
			ChancePerHundred = chancePerHundred;
			Bit = bit;
		}
	}

	private static readonly RecipeOpportunity[] RecipeOpportunities =
	{
		new(XjAlchemyCatalog.RecipeSheYuan, XjCraftProficiencySystem.RankLianQi, 18, 1 << 0),
		new(XjAlchemyCatalog.RecipeYangYuan, XjCraftProficiencySystem.RankLianQi, 6, 1 << 1),
		new(XjAlchemyCatalog.RecipeSuiYuan, XjCraftProficiencySystem.RankZhuJi, 5, 1 << 2),
		new(XjAlchemyCatalog.RecipeYuYa, XjCraftProficiencySystem.RankLianQi, 14, 1 << 3),
		new(XjAlchemyCatalog.RecipeHuiQiu, XjCraftProficiencySystem.RankZhuJi, 10, 1 << 4),
		new(XjAlchemyCatalog.RecipeHuMai, XjCraftProficiencySystem.RankZhuJi, 4, 1 << 5),
		new(XjAlchemyCatalog.RecipeQingShen, XjCraftProficiencySystem.RankZiFu, 4, 1 << 6),
		new(XjAlchemyCatalog.RecipeTianYiTuCui, XjCraftProficiencySystem.RankZiFu, 2, 1 << 7),
		new(XjAlchemyCatalog.RecipeChengJin, XjCraftProficiencySystem.RankZiFu, 2, 1 << 8),
		new(XjAlchemyCatalog.RecipeSanQuan, XjCraftProficiencySystem.RankZhuJi, 6, 1 << 9),
		new(XjAlchemyCatalog.RecipeNanGongXuanSui, XjCraftProficiencySystem.RankZiFu, 4, 1 << 10),
		new(XjAlchemyCatalog.RecipeMingZhenHeShen, XjCraftProficiencySystem.RankZiFu, 3, 1 << 11),
		new(XjAlchemyCatalog.RecipeJiuYaoYanShou, XjCraftProficiencySystem.RankZiFu, 1, 1 << 12),
		new(XjAlchemyCatalog.RecipeBaoYiWenJin, XjCraftProficiencySystem.RankZiFu, 2, 1 << 14),
		new(XjAlchemyCatalog.RecipeCunShenYangShen, XjCraftProficiencySystem.RankZiFu, 4, 1 << 15),
		new(XjAlchemyCatalog.RecipeXingMingHeZhen, XjCraftProficiencySystem.RankZiFu, 3, 1 << 16)
	};

	private static readonly RecipeOpportunity[] RecipePriority =
	{
		RecipeOpportunities[12],
		RecipeOpportunities[8],
		RecipeOpportunities[13],
		RecipeOpportunities[7],
		RecipeOpportunities[11],
		RecipeOpportunities[15],
		RecipeOpportunities[10],
		RecipeOpportunities[14],
		RecipeOpportunities[6],
		RecipeOpportunities[9],
		RecipeOpportunities[2],
		RecipeOpportunities[4],
		RecipeOpportunities[3],
		RecipeOpportunities[5],
		RecipeOpportunities[1],
		RecipeOpportunities[0]
	};

	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive() || currentYear <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !XjAlchemyOwnerResolver.TryGetPersonal(actor, out XjAlchemyOwnerKey personal)) return;

		int tier = XjRealmSuppression.GetRealmTier(actor);
		if (tier < XjRealmSuppression.TierTaiXi) return;

		// 丹药消费与炼丹资格解耦：任何持有适用丹药的修士都能获得实际药效。
		XjAlchemyPillEffectSystem.TickAutomaticMaintenance(actor, currentYear);

		// All cultivators may gather materials, but only a successful acquisition
		// enters the family-routing path. The old implementation resolved a family
		// and pruned pill batches for every cultivator every year.
		bool materialChanged = AcquireMaterials(actor, actorId, tier, personal, currentYear);
		bool repositoryResolved = false;
		XjAlchemyOwnerKey repository = default;
		if (TryResolveInheritanceRepository(actor, actorId, currentYear, out repository))
		{
			repositoryResolved = true;
		}
		if (materialChanged && repositoryResolved)
		{
			XjAlchemyInventoryRegistry.TryTransferSurplusToFamily(personal, repository, 8, 2, currentYear);
		}

		XjCraftTraitRules.NormalizeExclusive(actor);
		bool canPracticeAlchemy = XjCraftTraitRules.CanPracticeAlchemy(actor);
		if (XjAlchemyRuntimeRegistry.TryGetOpenTaskForCrafter(actorId, out XjAlchemyTaskArchiveData openTask))
		{
			// Removing the trait pauses the already committed task. It is neither
			// resolved nor charged a second time.
			if (canPracticeAlchemy) AdvanceOrResolve(actor, openTask, currentYear);
			return;
		}
		if (!canPracticeAlchemy) return;

		int alchemyRank = XjCraftProficiencySystem.GetAlchemyRank(actor);
		AcquireAndWriteRecipes(actor, actorId, alchemyRank, personal, repositoryResolved ? repository : default, currentYear);
		if (!repositoryResolved && TryResolveInheritanceRepository(actor, actorId, currentYear, out repository))
		{
			repositoryResolved = true;
			// Alchemists also flush any surplus left from an earlier year in which
			// sect/family identity had not yet been available.
			XjAlchemyInventoryRegistry.TryTransferSurplusToFamily(personal, repository, 8, 2, currentYear);
		}

		if (repositoryResolved)
		{
			CopyPersonalRecipesToRepository(actorId, personal, repository, currentYear);
			if (TryCreateTask(actor, actorId, alchemyRank, repository, currentYear)) return;
		}
		TryCreateTask(actor, actorId, alchemyRank, personal, currentYear);
	}

	private static bool TryResolveInheritanceRepository(Actor actor, long actorId, int currentYear, out XjAlchemyOwnerKey owner)
	{
		bool hasFamily = XjAlchemyOwnerResolver.TryGetFamily(actor, out XjAlchemyOwnerKey familyOwner);
		bool hasSect = XjAlchemyOwnerResolver.TryGetZongMen(actor, out XjAlchemyOwnerKey sectOwner);
		if (hasFamily && hasSect)
		{
			// 丹道仍以家族传承为主，但保留约四分之一的宗门产出，
			// 避免修士同时具备家族与宗门身份时宗门丹库长期归零。
			bool routeToSect = XjDeterministicHash.PositiveIndex(
				actorId + currentYear, "alchemy_inheritance_repository", 4) == 0;
			owner = routeToSect ? sectOwner : familyOwner;
			return true;
		}
		if (hasFamily)
		{
			owner = familyOwner;
			return true;
		}
		if (hasSect)
		{
			owner = sectOwner;
			return true;
		}
		owner = default;
		return false;
	}

	private static bool AcquireMaterials(Actor actor, long actorId, int tier, XjAlchemyOwnerKey personal, int year)
	{
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyMaterialGatherYear, out int processedYear)
			&& processedYear == year) return false;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyMaterialGatherYear, year);

		string lowMaterialId = string.Empty;
		int lowAmount = 0;
		if (XjDeterministicHash.PositiveIndex(actorId + year, "alchemy_material_low", 100) < 75)
		{
			lowAmount = 2 + XjDeterministicHash.PositiveIndex(
				actorId, "alchemy_material_low_amount|" + year, tier >= XjRealmSuppression.TierZhuJi ? 4 : 3);
			bool supplementary = XjDeterministicHash.PositiveIndex(actorId + year, "alchemy_material_low_pool", 100) < 15;
			IReadOnlyList<string> lowPool = supplementary
				? XjAlchemyCatalog.SupplementaryLowMaterialIds
				: XjAlchemyCatalog.CoreLowMaterialIds;
			int lowIndex = XjDeterministicHash.PositiveIndex(actorId + year, "alchemy_material_low_kind", lowPool.Count);
			lowMaterialId = lowPool[lowIndex];
		}

		string highMaterialId = string.Empty;
		if (tier >= XjRealmSuppression.TierZhuJi
			&& XjDeterministicHash.PositiveIndex(actorId + year, "alchemy_material_high", 100) < 8)
		{
			bool supplementary = XjDeterministicHash.PositiveIndex(actorId + year, "alchemy_material_high_pool", 100) < 20;
			IReadOnlyList<string> highPool = supplementary
				? XjAlchemyCatalog.SupplementaryHighMaterialIds
				: XjAlchemyCatalog.CoreHighMaterialIds;
			int highIndex = XjDeterministicHash.PositiveIndex(actorId + year, "alchemy_material_high_kind", highPool.Count);
			highMaterialId = highPool[highIndex];
		}

		if (!XjAlchemyInventoryRegistry.TryAddGatheredMaterials(
			personal, lowMaterialId, lowAmount, highMaterialId, string.IsNullOrEmpty(highMaterialId) ? 0 : 1, year)) return false;
		// 药材采集只更新库存，不再生成公告或史册记录。
		return true;
	}

	private static void AcquireAndWriteRecipes(
		Actor actor,
		long actorId,
		int alchemyRank,
		XjAlchemyOwnerKey personal,
		XjAlchemyOwnerKey family,
		int year)
	{
		int ownedMask = XjAlchemyInventoryRegistry.GetRecipeMask(personal);
		int familyMask = family.IsValid ? XjAlchemyInventoryRegistry.GetRecipeMask(family) : 0;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		int huiGuangBonus = Math.Max(0, (int)Math.Floor((huiGuang - 40f) / 20f));
		bool daoZhu = IsDaoZhu(actor);

		for (int i = 0; i < RecipeOpportunities.Length; i++)
		{
			RecipeOpportunity opportunity = RecipeOpportunities[i];
			if ((ownedMask & opportunity.Bit) != 0
				|| alchemyRank < opportunity.MinimumCraftRank
				|| !CanUseRecipePath(actor, opportunity.RecipeId)) continue;
			if (TryGrantFuQiCounterpartFromLegacyRecipe(
					actorId, personal, family, opportunity, year))
			{
				ownedMask |= opportunity.Bit;
				continue;
			}
			if ((familyMask & opportunity.Bit) != 0)
			{
				if (XjAlchemyInventoryRegistry.TryGrantRecipe(personal, opportunity.RecipeId, "家族丹方", actorId, year, false))
				{
					ownedMask |= opportunity.Bit;
				}
				continue;
			}

			int chance = Math.Min(55, opportunity.ChancePerHundred + huiGuangBonus);
			if (!daoZhu && XjDeterministicHash.PositiveIndex(actorId + year, "alchemy_recipe|" + opportunity.RecipeId, 100) >= chance)
				continue;
			if (XjAlchemyInventoryRegistry.TryGrantRecipe(personal, opportunity.RecipeId, "道慧参悟", actorId, year, true))
			{
				XjChronicleWriter.RecordAlchemyRecipe(actor, year, ResolveRecipeName(opportunity.RecipeId), "道慧参悟");
				ownedMask |= opportunity.Bit;
			}
		}
	}

	private static void CopyPersonalRecipesToRepository(
		long actorId,
		XjAlchemyOwnerKey personal,
		XjAlchemyOwnerKey repository,
		int year)
	{
		int copiedMask = XjAlchemyInventoryRegistry.CopyMissingRecipesTo(
			personal,
			repository,
			repository.Scope == XjAlchemyOwnerScope.ZongMen ? "宗门撰录" : "族人撰录",
			actorId,
			year);
		if (copiedMask == 0) return;

		// 丹方领悟只落传承数据；此处负责把个人丹方同步到家族或宗门库。
	}

	private static bool TryCreateTask(Actor actor, long actorId, int alchemyRank, XjAlchemyOwnerKey owner, int year)
	{
		int recipeMask = XjAlchemyInventoryRegistry.GetRecipeMask(owner);
		if (recipeMask == 0) return false;
		XjAlchemyInventoryRegistry.PruneExpiredPills(owner, year);

		for (int i = 0; i < RecipePriority.Length; i++)
		{
			RecipeOpportunity opportunity = RecipePriority[i];
			if ((recipeMask & opportunity.Bit) == 0
				|| XjAlchemyRuntimeRegistry.HasOpenTask(owner, opportunity.RecipeId)
				|| !XjAlchemyCatalog.TryGetRecipe(opportunity.RecipeId, out XjAlchemyRecipeDef recipe)
				|| !recipe.ProductionEnabled
				|| !CanUseRecipePath(actor, opportunity.RecipeId)
				|| alchemyRank < GetCraftRankRequirement(recipe.MinCrafterRealmId)) continue;

			int target = GetTargetStock(opportunity.RecipeId);
			if (XjAlchemyInventoryRegistry.GetPillCount(owner, recipe.PillId, 0) >= target) continue;

			long jinXingFamilyId = 0L;
			string committedJinXing = string.Empty;
			if (string.Equals(opportunity.RecipeId, XjAlchemyCatalog.RecipeJiuYaoYanShou, StringComparison.Ordinal))
			{
				if (!XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out jinXingFamilyId)
					|| jinXingFamilyId <= 0L
					|| !XjFamilyLingWuWarehouse.TryConsumeFirstJinXing(jinXingFamilyId, out committedJinXing))
				{
					continue;
				}
			}

			if (!XjAlchemyInventoryRegistry.TryConsumeRecipeMaterialsAtomic(owner, recipe))
			{
				RefundCommittedJinXing(actor, actorId, jinXingFamilyId, committedJinXing, year);
				continue;
			}

			string taskId = BuildTaskId(actorId, owner, opportunity.RecipeId, year);
			XjAlchemyTaskArchiveData task = new()
			{
				TaskId = taskId,
				OwnerScope = (int)owner.Scope,
				OwnerId = owner.OwnerId,
				RecipeId = opportunity.RecipeId,
				CrafterActorId = actorId,
				CreatedYear = year,
				ExpectedCompleteYear = year + Math.Max(1, recipe.BaseDurationYears / 2),
				LastAdvancedYear = year,
				Status = (int)XjAlchemyTaskStatus.InProgress,
				DeterministicSeedKey = taskId + "|" + actorId + "|" + opportunity.RecipeId + "|" + year,
				MaterialCommitted = true
			};
			if (XjAlchemyRuntimeRegistry.TryRegisterTask(task)) return true;
			XjAlchemyInventoryRegistry.RefundRecipeMaterials(owner, recipe, year);
			RefundCommittedJinXing(actor, actorId, jinXingFamilyId, committedJinXing, year);
		}
		return false;
	}

	private static void RefundCommittedJinXing(Actor actor, long actorId, long familyId, string jinXing, int year)
	{
		if (familyId <= 0L || string.IsNullOrWhiteSpace(jinXing)) return;
		string actorName;
		try { actorName = actor?.getName() ?? "炼丹师"; }
		catch { actorName = "炼丹师"; }
		XjFamilyLingWuWarehouse.TryAddJinXing(familyId, jinXing, 1, actorId, actorName, year);
	}

	private static int GetCraftRankRequirement(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return XjCraftProficiencySystem.RankJinDan;
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return XjCraftProficiencySystem.RankZiFu;
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return XjCraftProficiencySystem.RankZhuJi;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return XjCraftProficiencySystem.RankJinDan;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return XjCraftProficiencySystem.RankZiFu;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return XjCraftProficiencySystem.RankZhuJi;
		return XjCraftProficiencySystem.RankLianQi;
	}

	private static int GetTargetStock(string recipeId)
	{
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeJiuYaoYanShou, StringComparison.Ordinal)) return 1;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeChengJin, StringComparison.Ordinal)
			|| string.Equals(recipeId, XjAlchemyCatalog.RecipeBaoYiWenJin, StringComparison.Ordinal)) return 3;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeSuiYuan, StringComparison.Ordinal)
			|| string.Equals(recipeId, XjAlchemyCatalog.RecipeSanQuan, StringComparison.Ordinal)
			|| string.Equals(recipeId, XjAlchemyCatalog.RecipeTianYiTuCui, StringComparison.Ordinal)) return 2;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeMingZhenHeShen, StringComparison.Ordinal)
			|| string.Equals(recipeId, XjAlchemyCatalog.RecipeXingMingHeZhen, StringComparison.Ordinal)
			|| string.Equals(recipeId, XjAlchemyCatalog.RecipeNanGongXuanSui, StringComparison.Ordinal)
			|| string.Equals(recipeId, XjAlchemyCatalog.RecipeCunShenYangShen, StringComparison.Ordinal)) return 5;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeHuiQiu, StringComparison.Ordinal)
			|| string.Equals(recipeId, XjAlchemyCatalog.RecipeYangYuan, StringComparison.Ordinal)
			|| string.Equals(recipeId, XjAlchemyCatalog.RecipeHuMai, StringComparison.Ordinal)) return 3;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeYuYa, StringComparison.Ordinal)) return 6;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeSheYuan, StringComparison.Ordinal)) return 8;
		return 4;
	}

	private static void AdvanceOrResolve(Actor actor, XjAlchemyTaskArchiveData task, int year)
	{
		if (task.LastAdvancedYear < year)
		{
			task.LastAdvancedYear = year;
			XjAlchemyRuntimeRegistry.TryWriteTask(task);
		}
		if (year < task.ExpectedCompleteYear) return;
		string resolvedRecipeId = string.Equals(task.RecipeId, XjAlchemyCatalog.RecipeYuLuHuiMing, StringComparison.Ordinal)
			? XjAlchemyCatalog.RecipeTianYiTuCui
			: task.RecipeId;
		resolvedRecipeId = ResolveLegacyOpenTaskRecipe(actor, resolvedRecipeId);
		if (!XjAlchemyCatalog.TryGetRecipe(resolvedRecipeId, out XjAlchemyRecipeDef recipe)
			|| !XjAlchemyCatalog.TryGetPill(recipe.PillId, out XjAlchemyPillDef pill))
		{
			task.Status = (int)XjAlchemyTaskStatus.Interrupted;
			task.OutcomeCalculated = task.Resolved = task.ProductStored = task.CrafterProgressApplied = task.HistoryWritten = true;
			XjAlchemyRuntimeRegistry.TryWriteTask(task);
			return;
		}

		if (!task.OutcomeCalculated)
		{
			CalculateOutcome(actor, task, recipe);
			task.OutcomeCalculated = true;
			task.Resolved = true;
			task.Status = (int)XjAlchemyTaskStatus.Resolved;
			XjAlchemyRuntimeRegistry.TryWriteTask(task);
		}

		XjAlchemyOwnerKey owner = new((XjAlchemyOwnerScope)task.OwnerScope, task.OwnerId);
		if (!task.ProductStored)
		{
			bool stored = !task.Success || task.YieldQuantity <= 0;
			if (task.Success && task.YieldQuantity > 0)
			{
				XjAlchemyPillBatchState batch = new(
					"alchemy_batch_" + task.TaskId,
					owner,
					recipe.PillId,
					task.YieldQuantity,
					Enum.IsDefined(typeof(XjAlchemyQuality), task.ProductQuality) ? (XjAlchemyQuality)task.ProductQuality : XjAlchemyQuality.Normal,
					task.CrafterActorId,
					year,
					year + pill.ShelfLifeYears,
					resolvedRecipeId,
					task.TaskId);
				stored = XjAlchemyInventoryRegistry.TryAddPillBatch(batch);
			}
			if (stored)
			{
				task.ProductStored = true;
				task.Status = (int)XjAlchemyTaskStatus.ProductStored;
				XjAlchemyRuntimeRegistry.TryWriteTask(task);
			}
		}

		XjAlchemyRuntimeRegistry.ApplyResolvedProgress(task.TaskId, task.Success, task.MajorAccident, year);
		if (!XjAlchemyRuntimeRegistry.TryReadTask(task.TaskId, out task)) return;
		if (!task.HistoryWritten)
		{
			string pillName = ResolvePillName(recipe.PillId);
			if (task.Success)
			{
				bool highGrade = IsHighGradePill(recipe, pill);
				XjSectContributionSystem.RecordCraftContribution(actor, highGrade ? 10f + task.YieldQuantity : 3f + task.YieldQuantity, year, "炼丹供给");
			}

			bool retainedLifespanPill = task.Success
				&& !task.MajorAccident
				&& pillName.IndexOf("延寿丹", StringComparison.Ordinal) >= 0;
			if (retainedLifespanPill)
			{
				string actorName = actor.getName();
				string eventText = actorName + "炼成《" + pillName + "》" + task.YieldQuantity + "枚，丹成入库。";
				XjChronicleWriter.RecordAlchemyResult(actor, year, pillName, task.YieldQuantity, true, false);
				XjThreeBookWriter.RecordRareCraft(actor, pillName, "延寿丹", task.YieldQuantity, year);
				XjWorldHistoryStore.RecordDomainEvent(
					XjWorldHistoryCategory.Craft,
					actorName + "炼成延寿丹",
					eventText,
					4,
					isProtected: true,
					actorId: task.CrafterActorId,
					actorName: actorName,
					year: year);
				if (XjRuntimeSettings.BroadcastTreasureMilestoneEnabled)
				{
					XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
						eventText,
						XjAnnouncementCategory.Treasure,
						duration: 8f,
						color: "#D94C4C",
						iconId: XjEventIconCatalog.HighDanYao);
				}
			}

			task.HistoryWritten = true;
			// Persist the history phase even if product storage is temporarily unable
			// to finish. A loaded resolved task can then resume without duplicating records.
			XjAlchemyRuntimeRegistry.TryWriteTask(task);
		}

		if (task.ProductStored && task.CrafterProgressApplied && task.HistoryWritten)
		{
			task.Status = (int)XjAlchemyTaskStatus.Completed;
			XjAlchemyRuntimeRegistry.TryWriteTask(task);
		}
	}

	private static bool IsHighGradePill(XjAlchemyRecipeDef recipe, XjAlchemyPillDef pill)
	{
		if (recipe == null || pill == null)
		{
			return false;
		}

		// “高阶丹药”按实际炼制/服用境界判定，而不是按世界历史级别判定。
		// 部分筑基丹药也会写入世界历史，若使用 HistoryLevel 会误走 HighDanYao。
		return GetRealmTier(recipe.MinCrafterRealmId) >= XjRealmSuppression.TierZiFu
			|| GetRealmTier(pill.MinApplicableRealmId) >= XjRealmSuppression.TierZiFu;
	}

	private static void CalculateOutcome(Actor actor, XjAlchemyTaskArchiveData task, XjAlchemyRecipeDef recipe)
	{
		int actorTier = XjRealmSuppression.GetRealmTier(actor);
		int minimumTier = GetRealmTier(recipe.MinCrafterRealmId);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		int proficiency = ResolveRecipeProficiency(task.CrafterActorId, task.RecipeId);
		float professionBonus = XjCraftProficiencySystem.GetAlchemySuccessBonus(actor);
		float chance = recipe.BaseSuccessRate
			+ professionBonus
			+ Math.Max(0, actorTier - minimumTier) * 0.10f
			+ Math.Clamp((huiGuang - 30f) / 320f, 0f, 0.25f)
			+ Math.Min(0.25f, proficiency * 0.005f)
			- Math.Max(0, recipe.Difficulty - 1) * 0.03f;
		chance = Math.Clamp(chance, 0.16f, 0.97f);
		if (IsDaoZhu(actor))
		{
			task.Success = true;
			task.MajorAccident = false;
		}
		else
		{
			float successRoll = XjDeterministicHash.Roll01(task.CrafterActorId, task.CreatedYear, task.RecipeId, task.DeterministicSeedKey + "|success");
			task.Success = successRoll < chance;
			task.MajorAccident = !task.Success && recipe.Difficulty >= 2
				&& XjDeterministicHash.Roll01(task.CrafterActorId, task.CreatedYear, task.RecipeId, task.DeterministicSeedKey + "|accident")
				< Math.Clamp(0.008f + recipe.Difficulty * 0.008f - proficiency * 0.00025f, 0.003f, 0.04f);
		}
		if (!task.Success)
		{
			task.YieldQuantity = 0;
			task.ProductQuality = (int)XjAlchemyQuality.Normal;
			return;
		}
		int span = recipe.MaxYield - recipe.MinYield + 1;
		int baseYield = recipe.MinYield + XjDeterministicHash.PositiveIndex(task.CrafterActorId, task.DeterministicSeedKey + "|yield", span);
		// 丹药只保留固定药效，不再生成常品/上品等品质分支；炼丹品级与经验只提高普通丹方的一炉产量。
		// 九曜延寿丹是消耗金丹金性的唯一重宝丹，一炉严格只成一枚。
		task.YieldQuantity = string.Equals(recipe.Id, XjAlchemyCatalog.RecipeJiuYaoYanShou, StringComparison.Ordinal)
			? 1
			: XjCraftProficiencySystem.ResolveAlchemyYield(actor, baseYield);
		task.ProductQuality = (int)XjAlchemyQuality.Normal;
	}

	private static bool IsDaoZhu(Actor actor)
	{
		return actor?.data != null && actor.hasTrait("ChuShen8");
	}

	private static string BuildTaskId(long actorId, XjAlchemyOwnerKey owner, string recipeId, int year)
	{
		string raw = actorId.ToString(CultureInfo.InvariantCulture) + "|" + owner + "|" + recipeId + "|" + year.ToString(CultureInfo.InvariantCulture);
		return "alchemy_task_" + XjDeterministicHash.StableHash(raw).ToString(CultureInfo.InvariantCulture);
	}

	private static int GetRealmTier(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return XjRealmSuppression.TierJinDan;
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return XjRealmSuppression.TierZhuJi;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return XjRealmSuppression.TierJinDan;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return XjRealmSuppression.TierZhuJi;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return XjRealmSuppression.TierLianQi;
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return XjRealmSuppression.TierTaiXi;
		return XjRealmSuppression.TierNone;
	}

	private static bool CanUseRecipePath(Actor actor, string recipeId)
	{
		if (!XjAlchemyCatalog.TryGetRecipe(recipeId, out XjAlchemyRecipeDef recipe)) return false;
		if (recipe.PathAffinity == XjAlchemyCultivationPath.Universal) return true;
		bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		return isFuQi
			? recipe.PathAffinity == XjAlchemyCultivationPath.FuQi
			: recipe.PathAffinity == XjAlchemyCultivationPath.ZiJin;
	}

	private static string ResolveLegacyOpenTaskRecipe(Actor actor, string recipeId)
	{
		if (!XjCultivationPathRules.IsFuQiYangXing(actor)) return recipeId;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeChengJin, StringComparison.Ordinal))
			return XjAlchemyCatalog.RecipeBaoYiWenJin;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeNanGongXuanSui, StringComparison.Ordinal))
			return XjAlchemyCatalog.RecipeCunShenYangShen;
		if (string.Equals(recipeId, XjAlchemyCatalog.RecipeMingZhenHeShen, StringComparison.Ordinal))
			return XjAlchemyCatalog.RecipeXingMingHeZhen;
		return recipeId;
	}

	private static bool TryGrantFuQiCounterpartFromLegacyRecipe(
		long actorId,
		XjAlchemyOwnerKey personal,
		XjAlchemyOwnerKey repository,
		RecipeOpportunity opportunity,
		int year)
	{
		if (!TryGetZiJinCounterpart(opportunity.RecipeId, out string oldRecipeId)
			|| !XjAlchemyInventoryRegistry.HasRecipe(personal, oldRecipeId)
				&& (!repository.IsValid || !XjAlchemyInventoryRegistry.HasRecipe(repository, oldRecipeId)))
		{
			return false;
		}

		return XjAlchemyInventoryRegistry.TryGrantRecipe(
			personal, opportunity.RecipeId, "旧方分流", actorId, year, false);
	}

	private static int ResolveRecipeProficiency(long actorId, string recipeId)
	{
		int proficiency = XjAlchemyRuntimeRegistry.GetRecipeProficiency(actorId, recipeId);
		if (proficiency > 0 || !TryGetZiJinCounterpart(recipeId, out string oldRecipeId)) return proficiency;
		return XjAlchemyRuntimeRegistry.GetRecipeProficiency(actorId, oldRecipeId);
	}

	private static bool TryGetZiJinCounterpart(string fuQiRecipeId, out string ziJinRecipeId)
	{
		if (string.Equals(fuQiRecipeId, XjAlchemyCatalog.RecipeBaoYiWenJin, StringComparison.Ordinal))
		{
			ziJinRecipeId = XjAlchemyCatalog.RecipeChengJin;
			return true;
		}
		if (string.Equals(fuQiRecipeId, XjAlchemyCatalog.RecipeCunShenYangShen, StringComparison.Ordinal))
		{
			ziJinRecipeId = XjAlchemyCatalog.RecipeNanGongXuanSui;
			return true;
		}
		if (string.Equals(fuQiRecipeId, XjAlchemyCatalog.RecipeXingMingHeZhen, StringComparison.Ordinal))
		{
			ziJinRecipeId = XjAlchemyCatalog.RecipeMingZhenHeShen;
			return true;
		}
		ziJinRecipeId = string.Empty;
		return false;
	}

	internal static string ResolvePillName(string pillId)
	{
		return XjAlchemyText.PillName(pillId);
	}

	internal static string ResolveRecipeName(string recipeId)
	{
		return XjAlchemyText.RecipeName(recipeId);
	}

}
