using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
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
		if (actor?.data == null)
		{
			return string.Empty;
		}

		List<XjQiYuDongTianRewardCandidate> candidates = BuildRewardCandidates(actor, record, currentYear);
		for (int i = 0; candidates.Count == 0 && i < XjDongTianRules.RewardCandidateRetryCount; i++)
		{
			candidates = BuildRewardCandidates(actor, record, currentYear);
		}

		ShuffleRewardCandidates(candidates);
		int targetCount = UnityEngine.Random.value < XjDongTianRules.SecondRewardChance ? 2 : 1;
		List<string> rewardLines = new List<string>(targetCount);
		List<string> rewardTypes = new List<string>(targetCount);
		for (int i = 0; i < candidates.Count && rewardLines.Count < targetCount; i++)
		{
			string rewardText = candidates[i].TryApply?.Invoke() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(rewardText))
			{
				continue;
			}

			rewardLines.Add(rewardText);
			rewardTypes.Add(candidates[i].RewardType);
		}

		if (rewardLines.Count == 0)
		{
			string defaultReward = ApplyZhenYuanReward(actor, XjDongTianRules.ZhenYuanBaseAmount);
			if (!string.IsNullOrWhiteSpace(defaultReward))
			{
				rewardLines.Add(defaultReward);
				rewardTypes.Add("ZhenYuan");
			}
		}

		rewardType = rewardTypes.Count == 0 ? string.Empty : string.Join("+", rewardTypes);
		return rewardLines.Count == 0 ? string.Empty : string.Join("、", rewardLines);
	}

	private static List<XjQiYuDongTianRewardCandidate> BuildRewardCandidates(Actor actor, XjDongTianRecord record, int currentYear)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return new List<XjQiYuDongTianRewardCandidate>();
		}

		List<XjQiYuDongTianRewardCandidate> result = new List<XjQiYuDongTianRewardCandidate>(8);
		TryAddAlchemyRecipeRewardCandidate(actor, record, currentYear, result);
		TryAddGongFaRewardCandidate(actor, record, result);
		TryAddZhenYuanRewardCandidate(actor, result);
		TryAddMingShuRewardCandidate(actor, result);
		TryAddFaBaoRewardCandidate(actor, record, currentYear, result);
		TryAddJinXingRewardCandidate(actor, record, currentYear, result);
		TryAddSecretRealmMethodRewardCandidate(actor, record, currentYear, result);
		return result;
	}

	private static void TryAddAlchemyRecipeRewardCandidate(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		List<XjQiYuDongTianRewardCandidate> result)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= XjDongTianRules.AlchemyRecipeChance
			|| !XjAlchemyOwnerResolver.TryGetPersonal(actor, out XjAlchemyOwnerKey personal)) return;

		int actorTier = XjRealmSuppression.GetRealmTier(actor);
		List<string> available = new List<string>();
		for (int i = 0; i < XjAlchemyCatalog.OrderedRecipeIds.Length; i++)
		{
			string recipeId = XjAlchemyCatalog.OrderedRecipeIds[i];
			if (XjAlchemyInventoryRegistry.HasRecipe(personal, recipeId)
				|| !XjAlchemyCatalog.TryGetRecipe(recipeId, out XjAlchemyRecipeDef recipe)
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
		}));
	}

	private static int ResolveAlchemyRealmTier(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return XjRealmSuppression.TierZhuJi;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return XjRealmSuppression.TierLianQi;
		return XjRealmSuppression.TierTaiXi;
	}

	private static void TryAddGongFaRewardCandidate(Actor actor, XjDongTianRecord record, List<XjQiYuDongTianRewardCandidate> result)
	{
		if (actor?.data == null || result == null || !TryRollGongFaReward(out int targetGrade, out string rewardText))
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

		result.Add(new XjQiYuDongTianRewardCandidate("GongFa", () => ApplyGongFaReward(actor, name, targetGrade, daoTu, rewardText)));
	}

	private static bool TryRollGongFaReward(out int targetGrade, out string rewardText)
	{
		targetGrade = 0;
		rewardText = string.Empty;
		float roll = UnityEngine.Random.value;
		if (roll < XjDongTianRules.SixGradeGongFaChance)
		{
			targetGrade = 6;
			rewardText = "六品功法";
			return true;
		}

		if (roll < XjDongTianRules.FiveGradeGongFaChance)
		{
			targetGrade = 5;
			rewardText = "五品功法";
			return true;
		}

		if (roll < XjDongTianRules.FourGradeGongFaChance)
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

	private static void TryAddZhenYuanRewardCandidate(Actor actor, List<XjQiYuDongTianRewardCandidate> result)
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

		result.Add(new XjQiYuDongTianRewardCandidate("ZhenYuan", () => ApplyZhenYuanReward(actor, amount)));
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

	private static void TryAddMingShuRewardCandidate(Actor actor, List<XjQiYuDongTianRewardCandidate> result)
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

		result.Add(new XjQiYuDongTianRewardCandidate("MingShu", () => ApplyMingShuReward(actor, amount)));
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

	private static void TryAddFaBaoRewardCandidate(Actor actor, XjDongTianRecord record, int currentYear, List<XjQiYuDongTianRewardCandidate> result)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= XjDongTianRules.FaBaoChance)
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
		}));
	}

	private static void TryAddSecretRealmMethodRewardCandidate(Actor actor, XjDongTianRecord record, int currentYear, List<XjQiYuDongTianRewardCandidate> result)
	{
		if (actor?.data == null || result == null || UnityEngine.Random.value >= 0.08f) return;
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		if (sectId <= 0L) return;
		if (XjSecretRealmRegistry.TryGetBySectId(sectId, out XjSecretRealmArchiveRecord existing) && existing.ConstructionMethodKnown) return;
		long capturedSectId = sectId;
		result.Add(new XjQiYuDongTianRewardCandidate("SecretRealmMethod", () =>
		{
			return XjSecretRealmRegistry.TryGrantConstructionMethod(capturedSectId, "奇遇洞天·" + (record.DisplayName ?? string.Empty), currentYear)
				? "洞天营造之法"
				: string.Empty;
		}));
	}

	private static void TryAddJinXingRewardCandidate(
		Actor actor,
		XjDongTianRecord record,
		int currentYear,
		List<XjQiYuDongTianRewardCandidate> result)
	{
		if (actor?.data == null
			|| result == null
			|| !record.Found
			|| string.IsNullOrWhiteSpace(record.RecordId)
			|| UnityEngine.Random.value >= XjDongTianRules.JinXingChance
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
		}));
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
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		if (!string.IsNullOrWhiteSpace(daoTu))
		{
			return daoTu.Trim();
		}

		List<string> daoTus = BuildRewardDaoTuCandidates(actor, record);
		return daoTus.Count == 0 ? string.Empty : daoTus[0];
	}

	private static List<string> BuildRewardDaoTuCandidates(Actor actor, XjDongTianRecord record)
	{
		List<string> result = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		if (actor?.data != null && XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu))
		{
			AddDaoTuCandidate(actorDaoTu, result, seen);
		}

		if (!string.IsNullOrWhiteSpace(record.RelatedDaoTuIds))
		{
			string[] parts = record.RelatedDaoTuIds.Split(new[] { ',', '，', '/', '、' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++)
			{
				AddDaoTuCandidate(parts[i], result, seen);
			}
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

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名修士" : name.Trim();
		}
		catch
		{
			return "未名修士";
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
