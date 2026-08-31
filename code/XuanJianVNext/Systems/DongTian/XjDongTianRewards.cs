using System;
using System.Collections.Generic;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.DongTian;

internal static partial class XjDongTianRegistry
{
private static string ApplyRewardSet(Actor actor, XjDongTianRecord record, int currentYear, out string rewardType)
	{
		rewardType = string.Empty;
		if (actor?.data == null) return string.Empty;
		// 水月照真洞天有自己的“请凭函/因缘觐见”结算链，绝不进入普通洞天奖励池。
		// ResolveExplorerRecord 已在更上层分流渊照记录，这里不再保留一个不存在的旧奖励入口。
		if (XjDongTianRules.IsPermanentWorldSite(record.QiYuDongTianId)) return string.Empty;

		XjDongTianRewardProfile profile = XjDongTianRules.GetRewardProfile(record.QiYuDongTianId);
		bool relatedDaoTu = IsRelatedDaoTuExplorer(actor, record);
		List<XjQiYuDongTianRewardCandidate> candidates = BuildRewardCandidates(actor, record, currentYear, profile, relatedDaoTu);
		for (int i = 0; candidates.Count == 0 && i < XjDongTianRules.RewardCandidateRetryCount; i++)
		{
			candidates = BuildRewardCandidates(actor, record, currentYear, profile, relatedDaoTu);
		}

		ShuffleRewardCandidates(candidates);
		candidates.Sort((left, right) => right.Priority.CompareTo(left.Priority));
		float secondChance = Math.Clamp(profile.SecondRewardChance + (relatedDaoTu ? 0.05f : 0f), 0f, 0.85f);
		int targetCount = UnityEngine.Random.value < secondChance ? 2 : 1;
		List<string> rewardLines = new List<string>(targetCount);
		List<string> rewardTypes = new List<string>(targetCount);
		for (int i = 0; i < candidates.Count && rewardLines.Count < targetCount; i++)
		{
			string rewardText = candidates[i].TryApply?.Invoke() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(rewardText)) continue;
			rewardLines.Add(rewardText);
			rewardTypes.Add(candidates[i].RewardType);
		}

		if (rewardLines.Count == 0)
		{
			bool fuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
			string defaultReward = fuQi
				? ApplyHuiGuangReward(actor, 1f)
				: ApplyZhenYuanReward(actor, XjDongTianRules.ZhenYuanBaseAmount * profile.ZhenYuanMultiplier);
			if (!string.IsNullOrWhiteSpace(defaultReward))
			{
				rewardLines.Add(defaultReward);
				rewardTypes.Add(fuQi ? "HuiGuang" : "ZhenYuan");
			}
		}

		rewardType = rewardTypes.Count == 0 ? string.Empty : string.Join("+", rewardTypes);
		return rewardLines.Count == 0 ? string.Empty : string.Join("、", rewardLines);
	}

	private static List<XjQiYuDongTianRewardCandidate> BuildRewardCandidates(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		XjDongTianRewardProfile profile,
		bool relatedDaoTu)
	{
		if (!XjSafeCore.IsAliveActor(actor)) return new List<XjQiYuDongTianRewardCandidate>();

		List<XjQiYuDongTianRewardCandidate> result = new List<XjQiYuDongTianRewardCandidate>(14);
		bool fuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		bool ziJin = XjCultivationPathRules.IsZiFuJinDan(actor);
		float rareMultiplier = relatedDaoTu ? XjDongTianRules.RelatedDaoTuRareRewardMultiplier : 1f;

		TryAddAlchemyRecipeRewardCandidate(actor, record, currentYear, result, profile.AlchemyRecipeChance * rareMultiplier);
		TryAddZhenYuanRewardCandidate(actor, result, profile.ZhenYuanMultiplier);
		TryAddMingShuRewardCandidate(actor, result, profile.MingShuMultiplier);
		TryAddSecretRealmMethodRewardCandidate(actor, record, currentYear, result, profile.SecretRealmMethodChance * rareMultiplier);
		TryAddLingWuRewardCandidate(actor, record, currentYear, result, profile.LingWuChance * rareMultiplier);
		TryAddCaiQiRewardCandidate(actor, record, currentYear, result, profile.CaiQiChance * rareMultiplier);
		TryAddLifespanRewardCandidate(actor, record, currentYear, result, profile.LifespanChance * rareMultiplier);
		TryAddFormationMaterialRewardCandidate(actor, record, currentYear, result, profile.FormationMaterialChance * rareMultiplier);

		if (fuQi || profile.HuiGuangChance > 0.60f)
		{
			TryAddHuiGuangRewardCandidate(actor, record, currentYear, result, profile.HuiGuangChance * rareMultiplier);
		}
		if (ziJin)
		{
			TryAddGongFaRewardCandidate(actor, record, result, profile.GongFaChanceMultiplier);
			TryAddFaBaoRewardCandidate(actor, record, currentYear, result, profile.FaBaoChance * rareMultiplier);
			TryAddJinXingRewardCandidate(actor, record, currentYear, result, profile.JinXingChance * rareMultiplier);
		}
		return result;
	}

	private static void TryAddAlchemyRecipeRewardCandidate(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		List<XjQiYuDongTianRewardCandidate> result,
		float chance)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= Math.Clamp(chance, 0f, 0.85f)
			|| !XjAlchemyOwnerResolver.TryGetPersonal(actor, out XjAlchemyOwnerKey personal)) return;

		int actorTier = XjRealmSuppression.GetRealmTier(actor);
		bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		List<string> available = new List<string>();
		for (int i = 0; i < XjAlchemyCatalog.OrderedRecipeIds.Length; i++)
		{
			string recipeId = XjAlchemyCatalog.OrderedRecipeIds[i];
			if (XjAlchemyInventoryRegistry.HasRecipe(personal, recipeId)
				|| !XjAlchemyCatalog.TryGetRecipe(recipeId, out XjAlchemyRecipeDef recipe)
				|| recipe.PathAffinity == XjAlchemyCultivationPath.FuQi && !isFuQi
				|| recipe.PathAffinity == XjAlchemyCultivationPath.ZiJin && isFuQi
				|| actorTier < ResolveAlchemyRealmTier(recipe.MinCrafterRealmId)) continue;
			available.Add(recipeId);
		}
		if (available.Count == 0) return;

		long actorId = ((BaseSystemData)actor.data).id;
		int index = XjDeterministicHash.PositiveIndex(actorId + currentYear,
			"dongtian_alchemy_recipe|" + record.RecordId, available.Count);
		string selectedRecipeId = available[index];
		result.Add(new XjQiYuDongTianRewardCandidate("AlchemyRecipe", () =>
		{
			if (!XjAlchemyInventoryRegistry.TryGrantRecipe(personal, selectedRecipeId, "洞天奇遇", actorId, currentYear, true))
				return string.Empty;
			string recipeName = XjAlchemyText.RecipeName(selectedRecipeId);
			XjChronicleWriter.RecordAlchemyRecipe(actor, currentYear, recipeName, "alchemy.recipe.dongtian");
			XjBroadcastSystem.BroadcastBLevelActorEvent(actor,
				actor.getName() + "于" + record.DisplayName + "中得获《" + recipeName + "》。",
				iconId: XjEventIconCatalog.DanFangAcquire);
			return "丹方-" + recipeName;
		}, 1));
	}

	private static int ResolveAlchemyRealmTier(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return XjRealmSuppression.TierJinDan;
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return XjRealmSuppression.TierZhuJi;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return XjRealmSuppression.TierZhuJi;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return XjRealmSuppression.TierLianQi;
		return XjRealmSuppression.TierTaiXi;
	}

	private static void TryAddGongFaRewardCandidate(Actor actor, XjDongTianRecord record, List<XjQiYuDongTianRewardCandidate> result, float chanceMultiplier)
	{
		if (actor?.data == null || result == null || !TryRollGongFaReward(chanceMultiplier, out int targetGrade, out string rewardText))
		{
			return;
		}

		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		if (current.Found && current.Grade >= targetGrade)
		{
			return;
		}

		string daoTu = ResolveRewardDaoTu(actor, record);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		string name = XjGongFaNameLibrary.GenerateName(daoTu, targetGrade, actorId + targetGrade * 4099L);
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		result.Add(new XjQiYuDongTianRewardCandidate("GongFa", () => ApplyGongFaReward(actor, name, targetGrade, daoTu, rewardText), 1));
	}

	private static bool TryRollGongFaReward(float chanceMultiplier, out int targetGrade, out string rewardText)
	{
		targetGrade = 0;
		rewardText = string.Empty;
		float multiplier = Math.Max(0.1f, chanceMultiplier);
		float roll = UnityEngine.Random.value;
		if (roll < Math.Clamp(XjDongTianRules.SixGradeGongFaChance * multiplier, 0f, 0.12f))
		{
			targetGrade = 6;
			rewardText = "六品功法";
			return true;
		}

		if (roll < Math.Clamp(XjDongTianRules.FiveGradeGongFaChance * multiplier, 0f, 0.55f))
		{
			targetGrade = 5;
			rewardText = "五品功法";
			return true;
		}

		if (roll < Math.Clamp(XjDongTianRules.FourGradeGongFaChance * multiplier, 0f, 0.85f))
		{
			targetGrade = 4;
			rewardText = "四品功法";
			return true;
		}

		return false;
	}

	private static string ApplyGongFaReward(Actor actor, string name, int grade, string daoTu, string rewardText)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(name) || !XjGongFaDefinition.IsValidGrade(grade))
		{
			return string.Empty;
		}

		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		if (current.Found && current.Grade >= grade)
		{
			return string.Empty;
		}

		XjGongFaState reward = new XjGongFaState(true, name, grade, 0, 0f, daoTu, grade > XjGongFaAccessor.MaxActiveGrade, "QiYuDongTian");
		XjGongFaAccessor.WriteState(actor, reward);
		XjGongFaAccessor.WriteSource(actor, "洞天机缘");
		if (current.Found)
		{
			XjGongFaProgression.PublishGongFaPromoted(actor, reward);
		}
		else
		{
			XjGongFaProgression.PublishGongFaObtained(actor, reward);
		}

		return rewardText;
	}

	private static void TryAddZhenYuanRewardCandidate(Actor actor, List<XjQiYuDongTianRewardCandidate> result, float amountMultiplier)
	{
		if (actor?.data == null || result == null || !TryRollThresholdReward(
			XjDongTianRules.ZhenYuanTopAmount,
			XjDongTianRules.ZhenYuanTopChance,
			XjDongTianRules.ZhenYuanMidAmount,
			XjDongTianRules.ZhenYuanMidChance,
			XjDongTianRules.ZhenYuanLowAmount,
			XjDongTianRules.ZhenYuanLowChance,
			XjDongTianRules.ZhenYuanBaseAmount,
			XjDongTianRules.ZhenYuanBaseChance,
			out float amount))
		{
			return;
		}

		result.Add(new XjQiYuDongTianRewardCandidate("ZhenYuan", () => ApplyZhenYuanReward(actor, amount * Math.Max(0.1f, amountMultiplier))));
	}

	private static string ApplyZhenYuanReward(Actor actor, float amount)
	{
		if (actor?.data == null || amount <= 0f)
		{
			return string.Empty;
		}

		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		float before = snapshot.ZhenYuan;
		float realmCap = XjCultivationGrowthRules.ApplyRealmCap(snapshot, float.MaxValue);
		float perExplorationLimit = Math.Max(1f, (float)Math.Floor(realmCap * 0.10f));
		float effectiveAmount = Math.Min(amount, perExplorationLimit);
		float after = XjCultivationGrowthRules.ApplyRealmCap(snapshot, before + effectiveAmount);
		after = XjBottleneckEventSystem.ApplyGrowthGate(actor, in snapshot, after);
		float delta = (float)Math.Floor(Math.Max(0f, after - before));
		if (delta <= 0f)
		{
			return string.IsNullOrWhiteSpace(XjBottleneckEventSystem.BuildStatusSummary(actor))
				? string.Empty
				: "真元已蓄积于瓶颈";
		}

		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, after);
		return "真元+" + ((int)delta).ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static void TryAddMingShuRewardCandidate(Actor actor, List<XjQiYuDongTianRewardCandidate> result, float amountMultiplier)
	{
		if (actor?.data == null || result == null || !TryRollThresholdReward(
			XjDongTianRules.MingShuTopAmount,
			XjDongTianRules.MingShuTopChance,
			XjDongTianRules.MingShuMidAmount,
			XjDongTianRules.MingShuMidChance,
			XjDongTianRules.MingShuLowAmount,
			XjDongTianRules.MingShuLowChance,
			XjDongTianRules.MingShuBaseAmount,
			XjDongTianRules.MingShuBaseChance,
			out float amount))
		{
			return;
		}

		result.Add(new XjQiYuDongTianRewardCandidate("MingShu", () => ApplyMingShuReward(actor, amount * Math.Max(0.1f, amountMultiplier))));
	}

	private static string ApplyMingShuReward(Actor actor, float amount)
	{
		if (actor?.data == null || amount <= 0f)
		{
			return string.Empty;
		}

		int delta = (int)Math.Floor(amount);
		XjMingShuState.AddAcquired(actor, delta);
		return "命数+" + delta.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static void TryAddHuiGuangRewardCandidate(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		List<XjQiYuDongTianRewardCandidate> result,
		float chance)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= Math.Clamp(chance, 0f, 0.90f)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int amount = 1 + XjDeterministicHash.PositiveIndex(
			actorId + currentYear,
			"dongtian_fuqi_huiguang|" + (record.RecordId ?? string.Empty),
			3);
		result.Add(new XjQiYuDongTianRewardCandidate("HuiGuang", () => ApplyHuiGuangReward(actor, amount)));
	}

	private static string ApplyHuiGuangReward(Actor actor, float amount)
	{
		if (actor?.data == null || amount <= 0f) return string.Empty;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float before);
		float after = XjDaoHuiPolicy.Add(before, (float)Math.Floor(amount), XjDaoHuiPolicy.RareGrowthCeiling);
		int delta = (int)Math.Floor(Math.Max(0f, after - before));
		if (delta <= 0) return string.Empty;
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, after);
		return "道慧+" + delta.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static void TryAddFaBaoRewardCandidate(Actor actor, XjDongTianRecord record, int currentYear, List<XjQiYuDongTianRewardCandidate> result, float chance)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= Math.Clamp(chance, 0f, 0.50f))
		{
			return;
		}

		List<string> daoTus = BuildRewardDaoTuCandidates(actor, record);
		if (daoTus.Count == 0)
		{
			return;
		}

		result.Add(new XjQiYuDongTianRewardCandidate("FaBao", () =>
		{
			for (int i = 0; i < daoTus.Count; i++)
			{
				string daoTu = daoTus[i];
				if (XjFaBaoAcquisition.TryGrantDongTianReward(actor, daoTu, currentYear, out string displayName))
				{
					return daoTu + "法宝-" + displayName;
				}
			}

			return string.Empty;
		}, 2));
	}

	private static void TryAddSecretRealmMethodRewardCandidate(Actor actor, XjDongTianRecord record, int currentYear, List<XjQiYuDongTianRewardCandidate> result, float chance)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= Math.Clamp(chance, 0f, 0.85f)) return;
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		if (sectId <= 0L) return;
		if (XjSecretRealmRegistry.TryGetBySectId(sectId, out XjSecretRealmArchiveRecord existing) && existing.ConstructionMethodKnown) return;
		long capturedSectId = sectId;
		result.Add(new XjQiYuDongTianRewardCandidate("SecretRealmMethod", () =>
		{
			return XjSecretRealmRegistry.TryGrantConstructionMethod(capturedSectId, "奇遇洞天·" + (record.DisplayName ?? string.Empty), currentYear)
				? "洞天营造之法"
				: string.Empty;
		}, 2));
	}

	private static void TryAddJinXingRewardCandidate(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		List<XjQiYuDongTianRewardCandidate> result,
		float chance)
	{
		if (actor?.data == null
			|| result == null
			|| !record.Found
			|| string.IsNullOrWhiteSpace(record.RecordId)
			|| UnityEngine.Random.value >= Math.Clamp(chance, 0f, 0.10f)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| XjJinDanResidualJinXing.HasValidGrant(actor))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !TryResolveRewardFamily(actorId, out long familyStableId))
		{
			return;
		}

		string daoTu = ResolveRewardDaoTu(actor, record);
		string jinXing = string.IsNullOrWhiteSpace(daoTu) ? "洞天遗留金性" : daoTu + "金性";
		result.Add(new XjQiYuDongTianRewardCandidate("JinXing", () =>
		{
			return XjJinDanResidualJinXing.TryGrantFromQiYuDongTian(
				actor,
				jinXing,
				record.RecordId,
				familyStableId,
				currentYear,
				record.DisplayName)
				? "金丹遗留金性×1（已入家族重宝仓库）"
				: string.Empty;
		}, 3));
	}

	private static void TryAddLingWuRewardCandidate(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		List<XjQiYuDongTianRewardCandidate> result,
		float chance)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= Math.Clamp(chance, 0f, 0.50f)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !TryResolveRewardFamily(actorId, out long familyStableId)
			|| !TryResolveThematicDaoTu(actor, record, out string daoTu)
			|| !XjLingWuCatalog.TryResolveByDaoTu(daoTu, out XjLingWuDef definition)) return;
		string actorName = XjStringHelper.ActorName(actor, "未名修士");
		result.Add(new XjQiYuDongTianRewardCandidate("LingWu", () =>
		{
			return XjFamilyLingWuWarehouse.TryAddLingWu(familyStableId, definition, actorId, actorName, currentYear)
				? "稀有灵物-“" + definition.Name + "”（已入家族重宝仓库）"
				: string.Empty;
		}, 2));
	}

	private static void TryAddCaiQiRewardCandidate(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		List<XjQiYuDongTianRewardCandidate> result,
		float chance)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= Math.Clamp(chance, 0f, 0.85f)
			|| !TryResolveThematicCaiQi(actor, record, out XjCaiQiCatalogEntry entry)) return;
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		if (sectId <= 0L || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int amount = 1 + XjDeterministicHash.PositiveIndex(actorId + currentYear, "dongtian_caiqi|" + record.RecordId, 3);
		string resourceId = entry.OldResourceId;
		string actorName = XjStringHelper.ActorName(actor, "未名修士");
		result.Add(new XjQiYuDongTianRewardCandidate("CaiQi", () =>
		{
			return XjSectCaiQiWarehouse.TryAddCaiQiResource(
				sectId, sect.Name, resourceId, amount, actorId, actorName,
				"奇遇洞天·" + record.DisplayName, currentYear)
				? "先天之气-“" + entry.DisplayName + "之气”×" + amount
				: string.Empty;
		}, 2));
	}

	private static void TryAddLifespanRewardCandidate(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		List<XjQiYuDongTianRewardCandidate> result,
		float chance)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= Math.Clamp(chance, 0f, 0.50f)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int years = 20 + 10 * XjDeterministicHash.PositiveIndex(actorId + currentYear, "dongtian_lifespan|" + record.RecordId, 4);
		result.Add(new XjQiYuDongTianRewardCandidate("Lifespan", () =>
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyLifespanBonus, out int existing);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAlchemyLifespanBonus, Math.Max(0, existing) + years);
			actor.setStatsDirty();
			return "寿元上限+" + years + "年";
		}, 2));
	}

	private static void TryAddFormationMaterialRewardCandidate(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		List<XjQiYuDongTianRewardCandidate> result,
		float chance)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= Math.Clamp(chance, 0f, 0.85f)
			|| !XjCraftOwnerResolver.TryResolvePreferred(actor, out XjCraftOwnerKey owner)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int runeCount = 2 + XjDeterministicHash.PositiveIndex(actorId + currentYear, "dongtian_rune|" + record.RecordId, 4);
		int materialCount = 1 + XjDeterministicHash.PositiveIndex(actorId + currentYear, "dongtian_spirit_material|" + record.RecordId, 3);
		bool highFlag = XjDeterministicHash.PositiveIndex(actorId + currentYear, "dongtian_high_flag|" + record.RecordId, 3) == 0;
		result.Add(new XjQiYuDongTianRewardCandidate("FormationMaterial", () =>
		{
			bool changed = XjCraftDomainRegistry.TryAddResource(owner, XjCraftCollaborationSystem.FormationRune, runeCount, currentYear);
			changed |= XjCraftDomainRegistry.TryAddResource(owner, XjCraftCollaborationSystem.SpiritMaterial, materialCount, currentYear);
			if (highFlag) changed |= XjCraftDomainRegistry.TryAddResource(owner, XjCraftCollaborationSystem.FormationFlagHigh, 1, currentYear);
			return changed
				? "阵法资材-阵纹×" + runeCount + "、灵材×" + materialCount + (highFlag ? "、上品阵旗×1" : string.Empty)
				: string.Empty;
		}, 2));
	}

	private static bool TryResolveThematicDaoTu(Actor actor, XjDongTianRecord record, out string daoTu)
	{
		List<string> daoTus = BuildRewardDaoTuCandidates(actor, record);
		daoTu = daoTus.Count == 0 ? string.Empty : daoTus[0];
		return !string.IsNullOrWhiteSpace(daoTu);
	}

	private static bool TryResolveThematicCaiQi(Actor actor, XjDongTianRecord record, out XjCaiQiCatalogEntry entry)
	{
		entry = default;
		List<string> daoTus = BuildRewardDaoTuCandidates(actor, record);
		for (int i = 0; i < daoTus.Count; i++)
		{
			if (!XjDongTianRules.IsRelatedDaoTu(record.QiYuDongTianId, record.RelatedDaoTuIds, daoTus[i])) continue;
			if (XjCaiQiCatalog.TryGetEntryByDisplayName(daoTus[i], out entry)
				&& !string.IsNullOrWhiteSpace(entry.OldResourceId)) return true;
		}
		return false;
	}

	private static bool IsRelatedDaoTuExplorer(Actor actor, XjDongTianRecord record)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& XjDongTianRules.IsRelatedDaoTu(record.QiYuDongTianId, record.RelatedDaoTuIds, daoTu);
	}

	private static bool TryResolveRewardFamily(long actorId, out long familyStableId)
	{
		familyStableId = 0L;
		if (actorId <= 0L) return false;
		if (XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out familyStableId) && familyStableId > 0L)
		{
			return true;
		}
		if (XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord identity)
			&& identity.Found
			&& identity.RootActorId > 0L)
		{
			familyStableId = identity.RootActorId;
			return true;
		}
		return false;
	}

	private static bool TryRollThresholdReward(
		float topReward,
		float topThreshold,
		float midReward,
		float midThreshold,
		float lowReward,
		float lowThreshold,
		float baseReward,
		float baseThreshold,
		out float reward)
	{
		reward = 0f;
		float roll = UnityEngine.Random.value;
		if (roll < topThreshold)
		{
			reward = topReward;
			return true;
		}

		if (roll < midThreshold)
		{
			reward = midReward;
			return true;
		}

		if (roll < lowThreshold)
		{
			reward = lowReward;
			return true;
		}

		if (roll < baseThreshold)
		{
			reward = baseReward;
			return true;
		}

		return false;
	}

	private static string ResolveRewardDaoTu(Actor actor, XjDongTianRecord record)
	{
		List<string> daoTus = BuildRewardDaoTuCandidates(actor, record);
		return daoTus.Count == 0 ? string.Empty : daoTus[0];
	}

	private static List<string> BuildRewardDaoTuCandidates(Actor actor, XjDongTianRecord record)
	{
		List<string> result = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		string actorDaoTu = string.Empty;
		long actorId = 0L;
		bool actorMatches = false;
		if (actor?.data != null)
		{
			actorId = ((BaseSystemData)actor.data).id;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out actorDaoTu);
			actorMatches = XjDongTianRules.IsRelatedDaoTu(record.QiYuDongTianId, record.RelatedDaoTuIds, actorDaoTu);
			if (actorMatches)
			{
				AddDaoTuCandidate(actorDaoTu, result, seen);
			}
		}

		if (!string.IsNullOrWhiteSpace(record.RelatedDaoTuIds))
		{
			string[] parts = record.RelatedDaoTuIds.Split(new[] { ',', '，', '/', '、' }, StringSplitOptions.RemoveEmptyEntries);
			int start = 0;
			if (!actorMatches && parts.Length > 1)
			{
				long seed = actorId + Math.Max(record.AnchorYear, record.CreatedYear);
				start = XjDeterministicHash.PositiveIndex(seed, "dongtian_reward_daotu|" + (record.RecordId ?? string.Empty), parts.Length);
			}
			for (int offset = 0; offset < parts.Length; offset++)
			{
				AddDaoTuCandidate(parts[(start + offset) % parts.Length], result, seen);
			}
		}

		// 洞天产出优先体现洞天本身的道途。仅在旧档洞天没有关联道途时，才退回探索者自身道途。
		if (result.Count == 0)
		{
			AddDaoTuCandidate(actorDaoTu, result, seen);
		}

		return result;
	}

	private static void AddDaoTuCandidate(string daoTu, List<string> result, HashSet<string> seen)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized) || result == null || seen == null || !seen.Add(normalized))
		{
			return;
		}

		result.Add(normalized);
	}

	private static void ShuffleRewardCandidates(List<XjQiYuDongTianRewardCandidate> candidates)
	{
		if (candidates == null || candidates.Count <= 1)
		{
			return;
		}

		for (int i = candidates.Count - 1; i > 0; i--)
		{
			int swapIndex = UnityEngine.Random.Range(0, i + 1);
			XjQiYuDongTianRewardCandidate value = candidates[i];
			candidates[i] = candidates[swapIndex];
			candidates[swapIndex] = value;
		}
	}

	private static List<XjQiYuDongTianExplorerArchiveData> ExportExplorerRecords(IReadOnlyList<XjQiYuDongTianExplorerRecord> records)
	{
		List<XjQiYuDongTianExplorerArchiveData> result = new List<XjQiYuDongTianExplorerArchiveData>();
		if (records == null)
		{
			return result;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjQiYuDongTianExplorerRecord record = records[i];
			result.Add(new XjQiYuDongTianExplorerArchiveData
			{
				ExplorerActorId = record.ExplorerActorId,
				ExplorerActorName = record.ExplorerActorName,
				RealmId = record.RealmId,
				RealmDisplay = record.RealmDisplay,
				DaoTu = record.DaoTu,
				DistanceToAnchor = record.DistanceToAnchor,
				ReservedYear = record.ReservedYear,
				Resolved = record.Resolved,
				ResolvedYear = record.ResolvedYear,
				DeathResolved = record.DeathResolved,
				RewardApplied = record.RewardApplied,
				RewardType = record.RewardType,
				ResultSummary = record.ResultSummary,
				DeathPending = record.DeathPending,
				DeathPendingYear = record.DeathPendingYear,
				DeathFinalized = record.DeathFinalized,
				DeathFinalizedYear = record.DeathFinalizedYear
			});
		}

		return result;
	}

	private static IReadOnlyList<XjQiYuDongTianExplorerRecord> ImportExplorerRecords(IEnumerable<XjQiYuDongTianExplorerArchiveData> records)
	{
		if (records == null)
		{
			return Array.Empty<XjQiYuDongTianExplorerRecord>();
		}

		List<XjQiYuDongTianExplorerRecord> result = new List<XjQiYuDongTianExplorerRecord>();
		foreach (XjQiYuDongTianExplorerArchiveData record in records)
		{
			if (record == null || record.ExplorerActorId <= 0L)
			{
				continue;
			}

			result.Add(new XjQiYuDongTianExplorerRecord(
				record.ExplorerActorId,
				record.ExplorerActorName,
				record.RealmId,
				record.RealmDisplay,
				record.DaoTu,
				record.DistanceToAnchor,
				record.ReservedYear,
				record.Resolved,
				record.ResolvedYear,
				record.DeathResolved,
				record.RewardApplied,
				record.RewardType,
				record.ResultSummary,
				record.DeathPending,
				record.DeathPendingYear,
				record.DeathFinalized,
				record.DeathFinalizedYear));
		}

		return result;
	}

	private static bool TryGetCurrentWorldYear(out int currentYear)
	{
		currentYear = 0;
		MapBox world = World.world;
		if ((UnityEngine.Object)(object)world == null)
		{
			return false;
		}

		currentYear = (int)Math.Floor(Math.Max(0.0, world.getCurWorldTime() / 60.0)) + 1;
		return currentYear > 0;
	}
}
