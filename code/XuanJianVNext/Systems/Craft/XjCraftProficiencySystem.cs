using System;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Craft;

/// <summary>
/// 四艺统一采用炼气、筑基、紫府、金丹四个品级。
/// 品级决定能处理的最高层次；经验在当前品级内提高对应效率，达到阈值且修士本身具备对应境界后，
/// 于年度结算中自动晋级。炼丹、符箓提高成功率与产量；阵法提高布阵速度与维修效果；
/// 炼器提高普通装备稀有度及法器、灵宝、法宝词条数值。成品不再生成下中上极品质分支。
/// </summary>
internal static class XjCraftProficiencySystem
{
	internal const int RankNone = 0;
	internal const int RankLianQi = 1;
	internal const int RankZhuJi = 2;
	internal const int RankZiFu = 3;
	internal const int RankJinDan = 4;


	private const int CurrentRankSchema = 3;
	private const int ZhuJiThreshold = 40;
	private const int ZiFuThreshold = 120;
	private const int JinDanThreshold = 300;
	private const int MasteryCeiling = 600;

	private const int TrainingProficiencyPoints = 1;
	private const int FaQiProficiencyPoints = 10;
	private const int LingBaoProficiencyPoints = 20;
	private const int FaBaoProficiencyPoints = 30;
	private const int NativeTrainingAttemptIntervalYears = 1;

	internal static int GetAlchemyRank(Actor actor)
	{
		if (!XjCraftTraitRules.CanPracticeAlchemy(actor) || actor?.data == null) return RankNone;
		return ReadOrMigrateRank(actor, XjActorDataKeys.XjCraftAlchemyRank, GetAlchemyProficiency(actor));
	}

	internal static float GetAlchemySuccessBonus(Actor actor)
	{
		if (actor?.data == null) return 0f;
		int proficiency = GetAlchemyProficiency(actor);
		return ResolveSuccessBonus(GetAlchemyRank(actor), proficiency);
	}

	internal static string GetAlchemyRankDisplayName(Actor actor) => GetRankDisplayName(GetAlchemyRank(actor), "炼丹");

	internal static string GetRankTierDisplayName(int rank) => rank <= RankNone ? "未入艺" : RankName(rank);

	internal static int GetArtifactRank(Actor actor)
	{
		if (!XjCraftTraitRules.CanRefineArtifacts(actor) || actor?.data == null) return RankNone;
		int proficiency = GetArtifactProficiency(actor, out _, out _, out _, out _);
		return ReadOrMigrateRank(actor, XjActorDataKeys.XjArtifactRefinerRank, proficiency);
	}


	internal static string GetArtifactRankDisplayName(Actor actor) => GetRankDisplayName(GetArtifactRank(actor), "炼器");

	internal static int GetTalismanRank(Actor actor)
	{
		return GetSimpleProfessionRank(actor, XjCraftTraitRules.TalismanTraitId,
			XjActorDataKeys.XjTalismanProficiency, XjActorDataKeys.XjTalismanRank);
	}

	internal static int GetFormationRank(Actor actor)
	{
		return GetSimpleProfessionRank(actor, XjCraftTraitRules.FormationTraitId,
			XjActorDataKeys.XjFormationProficiency, XjActorDataKeys.XjFormationRank);
	}

	internal static float GetTalismanSuccessBonus(Actor actor)
	{
		int proficiency = GetSimpleProficiency(actor, XjActorDataKeys.XjTalismanProficiency);
		return ResolveSuccessBonus(GetTalismanRank(actor), proficiency);
	}


	internal static void RecordTalismanProgress(Actor actor, int points = 1)
	{
		RecordSimpleProfessionProgress(actor, XjCraftTraitRules.TalismanTraitId,
			XjActorDataKeys.XjTalismanProficiency, points);
	}

	internal static void RecordFormationProgress(Actor actor, int points = 1)
	{
		RecordSimpleProfessionProgress(actor, XjCraftTraitRules.FormationTraitId,
			XjActorDataKeys.XjFormationProficiency, points);
	}

	/// <summary>
	/// 年度统一晋级。经验达到阈值后直接晋级，但不能超过修士本身可承载的境界层次。
	/// </summary>
	internal static void TickRankProgression(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		string traitId = XjCraftTraitRules.GetPrimaryTraitId(actor);
		if (string.IsNullOrWhiteSpace(traitId)
			|| !TryResolveProfession(actor, traitId, out _, out string rankKey, out int proficiency)) return;

		int current = ReadOrMigrateRank(actor, rankKey, proficiency);
		int experienceRank = ResolveRankFromProficiency(proficiency);
		int realmCap = ResolveRealmRankCap(actor);
		int target = Math.Clamp(Math.Min(experienceRank, realmCap), RankLianQi, RankJinDan);
		if (target <= current) return;

		XjActorAccessor.SetInt(actor, rankKey, target);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCraftRankFailureCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCraftRankSchema, CurrentRankSchema);
		XjRuntimeActorInterestIndex.Observe(actor);
	}

	internal static int ResolveRequiredRankForRealm(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return RankJinDan;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return RankZiFu;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return RankZhuJi;
		return RankLianQi;
	}

	internal static bool CanProcessRealm(Actor actor, int craftRank, string realmId)
	{
		return actor?.data != null && craftRank >= ResolveRequiredRankForRealm(realmId);
	}

	internal static float GetProductionMultiplier(Actor actor, int rank, int proficiency)
	{
		float rankBase = rank switch
		{
			RankJinDan => 2.00f,
			RankZiFu => 1.60f,
			RankZhuJi => 1.25f,
			RankLianQi => 1.00f,
			_ => 1.00f
		};
		return rankBase + ResolveWithinRankProgress(rank, proficiency) * 0.25f;
	}

	internal static int ResolveAlchemyYield(Actor actor, int baseQuantity)
	{
		int proficiency = GetAlchemyProficiency(actor);
		float multiplier = GetProductionMultiplier(actor, GetAlchemyRank(actor), proficiency);
		return Math.Max(1, (int)Math.Floor(Math.Max(1, baseQuantity) * multiplier));
	}

	internal static int ResolveTalismanYield(Actor actor, int baseQuantity)
	{
		int proficiency = GetSimpleProficiency(actor, XjActorDataKeys.XjTalismanProficiency);
		float multiplier = GetProductionMultiplier(actor, GetTalismanRank(actor), proficiency);
		return Math.Max(1, (int)Math.Floor(Math.Max(1, baseQuantity) * multiplier));
	}

	internal static float GetFormationBuildSpeedMultiplier(Actor actor)
	{
		int proficiency = GetSimpleProficiency(actor, XjActorDataKeys.XjFormationProficiency);
		int rank = GetFormationRank(actor);
		float rankBase = rank switch
		{
			RankJinDan => 2.00f,
			RankZiFu => 1.60f,
			RankZhuJi => 1.30f,
			_ => 1.00f
		};
		return rankBase + ResolveWithinRankProgress(rank, proficiency) * 0.30f;
	}

	internal static float GetFormationRepairMultiplier(Actor actor)
	{
		int proficiency = GetSimpleProficiency(actor, XjActorDataKeys.XjFormationProficiency);
		int rank = GetFormationRank(actor);
		float rankBase = rank switch
		{
			RankJinDan => 2.50f,
			RankZiFu => 1.90f,
			RankZhuJi => 1.40f,
			_ => 1.00f
		};
		return rankBase + ResolveWithinRankProgress(rank, proficiency) * 0.35f;
	}

	internal static float GetArtifactStatMultiplier(Actor actor)
	{
		int proficiency = GetArtifactProficiency(actor, out _, out _, out _, out _);
		int rank = GetArtifactRank(actor);
		float rankBase = rank switch
		{
			RankJinDan => 1.40f,
			RankZiFu => 1.25f,
			RankZhuJi => 1.12f,
			_ => 1.00f
		};
		return rankBase + ResolveWithinRankProgress(rank, proficiency) * 0.12f;
	}

	internal static int GetNativeEquipmentRarityRolls(Actor actor)
	{
		int rank = GetArtifactRank(actor);
		int proficiency = GetArtifactProficiency(actor, out _, out _, out _, out _);
		return Math.Clamp(rank + (ResolveWithinRankProgress(rank, proficiency) >= 0.75f ? 1 : 0), 1, 5);
	}


	internal static string BuildAlchemyProgressSummary(Actor actor)
	{
		if (!XjCraftTraitRules.CanPracticeAlchemy(actor) || actor?.data == null) return string.Empty;
		int proficiency = GetAlchemyProficiency(actor);
		return BuildRankSummary("炼丹", GetAlchemyRank(actor), proficiency, "成功率与一炉产量");
	}

	internal static string BuildArtifactProgressSummary(Actor actor)
	{
		if (!XjCraftTraitRules.CanRefineArtifacts(actor) || actor?.data == null) return string.Empty;
		int proficiency = GetArtifactProficiency(actor, out int training, out int faQi, out int lingBao, out int faBao);
		int rank = GetArtifactRank(actor);
		string actualRealmId = XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId) ? realmId : string.Empty;
		string practiceRealmId = XjFaBaoForgePolicy.ResolvePracticeRealmId(actor, actualRealmId);
		bool nativeTraining = IsNativeTrainingRealm(practiceRealmId);
		string target = string.Equals(practiceRealmId, XjRealmIds.JinDan, StringComparison.Ordinal) ? "法宝"
			: string.Equals(practiceRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal) ? "灵宝"
			: string.Equals(practiceRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal) ? "法器"
			: nativeTraining ? "普通装备" : "暂无可炼器物";
		return GetRankDisplayName(rank, "炼器")
			+ "（经验" + proficiency
			+ "，练手" + training + "/法器" + faQi + "/灵宝" + lingBao + "/法宝" + faBao
			+ "，可处理至" + RankName(rank)
			+ "，普通装备稀有度投骰" + GetNativeEquipmentRarityRolls(actor) + "次"
			+ "，器物词条强度×" + GetArtifactStatMultiplier(actor).ToString("0.00", CultureInfo.InvariantCulture)
			+ "，当前炼制：" + target
			+ "，" + BuildArtifactAttemptStatus(actor, nativeTraining)
			+ "，" + BuildNextRankText(rank, proficiency) + "）";
	}

	internal static string BuildTalismanProgressSummary(Actor actor)
	{
		return BuildSimpleProfessionProgressSummary(actor, XjCraftTraitRules.TalismanTraitId,
			XjActorDataKeys.XjTalismanProficiency, "符箓", "成功率与产出率");
	}

	internal static string BuildFormationProgressSummary(Actor actor)
	{
		if (actor?.data == null || !actor.hasTrait(XjCraftTraitRules.FormationTraitId)
			|| !string.Equals(XjCraftTraitRules.GetPrimaryTraitId(actor), XjCraftTraitRules.FormationTraitId, StringComparison.Ordinal)) return string.Empty;
		int proficiency = GetSimpleProficiency(actor, XjActorDataKeys.XjFormationProficiency);
		int rank = GetFormationRank(actor);
		return GetRankDisplayName(rank, "阵法") + "（经验" + proficiency
			+ "，可处理：" + ResolveFormationLayerDisplay(rank)
			+ "，布阵速度×" + GetFormationBuildSpeedMultiplier(actor).ToString("0.00", CultureInfo.InvariantCulture)
			+ "，维修效果×" + GetFormationRepairMultiplier(actor).ToString("0.00", CultureInfo.InvariantCulture)
			+ "，" + BuildNextRankText(rank, proficiency) + "）";
	}

	internal static bool TryBuildTooltipRows(Actor actor, bool alchemy, out string labels, out string values)
	{
		return TryBuildTooltipRows(actor, alchemy ? XjCraftTraitRules.AlchemyTraitId : XjCraftTraitRules.ArtifactRefiningTraitId, out labels, out values);
	}

	internal static bool TryBuildTooltipRows(Actor actor, string traitId, out string labels, out string values)
	{
		labels = string.Empty;
		values = string.Empty;
		if (actor?.data == null || !XjCraftTraitRules.IsCraftTraitId(traitId)
			|| !TryResolveProfession(actor, traitId, out string profession, out string rankKey, out int proficiency)) return false;
		int rank = ReadOrMigrateRank(actor, rankKey, proficiency);
		labels = "四艺品级\n当前经验\n可处理层次\n效率提升\n晋级条件";
		string efficiency = string.Equals(traitId, XjCraftTraitRules.FormationTraitId, StringComparison.Ordinal)
			? "布阵×" + GetFormationBuildSpeedMultiplier(actor).ToString("0.00", CultureInfo.InvariantCulture)
				+ " / 维修×" + GetFormationRepairMultiplier(actor).ToString("0.00", CultureInfo.InvariantCulture)
			: string.Equals(traitId, XjCraftTraitRules.ArtifactRefiningTraitId, StringComparison.Ordinal)
				? "稀有度投骰" + GetNativeEquipmentRarityRolls(actor) + "次 / 词条强度×" + GetArtifactStatMultiplier(actor).ToString("0.00", CultureInfo.InvariantCulture)
				: "成功率+" + FormatPercent(ResolveSuccessBonus(rank, proficiency))
					+ " / 产出×" + GetProductionMultiplier(actor, rank, proficiency).ToString("0.00", CultureInfo.InvariantCulture);
		string processLayer = string.Equals(traitId, XjCraftTraitRules.FormationTraitId, StringComparison.Ordinal)
			? ResolveFormationLayerDisplay(rank)
			: RankName(rank);
		values = GetRankDisplayName(rank, profession)
			+ "\n" + proficiency
			+ "\n" + processLayer
			+ "\n" + efficiency
			+ "\n" + BuildNextRankText(rank, proficiency);
		return true;
	}

	internal static void RecordArtifactSuccess(Actor actor, string className)
	{
		if (!XjCraftTraitRules.CanRefineArtifacts(actor) || actor?.data == null) return;
		string key = XjFaBaoCatalog.IsZhuJiFaQi(className) ? XjActorDataKeys.XjArtifactFaQiSuccess
			: XjFaBaoCatalog.IsJinDanFaBao(className) ? XjActorDataKeys.XjArtifactFaBaoSuccess
			: XjActorDataKeys.XjArtifactLingBaoSuccess;
		XjActorAccessor.TryGetInt(actor, key, out int progress);
		XjActorAccessor.SetInt(actor, key, Math.Min(int.MaxValue, Math.Max(0, progress) + 1));
		int currentYear = 0;
		try { currentYear = World.world?.map_stats?.year ?? 0; } catch { }
		XjCraftCollaborationSystem.RecordArtifactOutput(actor, className, currentYear);
		float contribution = XjFaBaoCatalog.IsJinDanFaBao(className) ? 18f : XjFaBaoCatalog.IsZhuJiFaQi(className) ? 5f : 10f;
		XjSectContributionSystem.RecordCraftContribution(actor, contribution, currentYear, "炼器供给");
	}

	internal static void RecordArtifactTraining(Actor actor, int points = 1)
	{
		if (!XjCraftTraitRules.CanRefineArtifacts(actor) || actor?.data == null || points <= 0) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjArtifactTrainingSuccess, out int progress);
		int updated = progress > int.MaxValue - points ? int.MaxValue : Math.Max(0, progress) + points;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjArtifactTrainingSuccess, updated);
	}

	private static bool TryResolveProfession(Actor actor, string traitId, out string profession, out string rankKey, out int proficiency)
	{
		profession = string.Empty;
		rankKey = string.Empty;
		proficiency = 0;
		if (actor?.data == null || !string.Equals(XjCraftTraitRules.GetPrimaryTraitId(actor), traitId, StringComparison.Ordinal)) return false;
		if (string.Equals(traitId, XjCraftTraitRules.AlchemyTraitId, StringComparison.Ordinal))
		{
			profession = "炼丹"; rankKey = XjActorDataKeys.XjCraftAlchemyRank; proficiency = GetAlchemyProficiency(actor); return true;
		}
		if (string.Equals(traitId, XjCraftTraitRules.ArtifactRefiningTraitId, StringComparison.Ordinal))
		{
			profession = "炼器"; rankKey = XjActorDataKeys.XjArtifactRefinerRank;
			proficiency = GetArtifactProficiency(actor, out _, out _, out _, out _); return true;
		}
		if (string.Equals(traitId, XjCraftTraitRules.TalismanTraitId, StringComparison.Ordinal))
		{
			profession = "符箓"; rankKey = XjActorDataKeys.XjTalismanRank;
			proficiency = GetSimpleProficiency(actor, XjActorDataKeys.XjTalismanProficiency); return true;
		}
		if (string.Equals(traitId, XjCraftTraitRules.FormationTraitId, StringComparison.Ordinal))
		{
			profession = "阵法"; rankKey = XjActorDataKeys.XjFormationRank;
			proficiency = GetSimpleProficiency(actor, XjActorDataKeys.XjFormationProficiency); return true;
		}
		return false;
	}

	private static int ReadOrMigrateRank(Actor actor, string rankKey, int proficiency)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(rankKey)) return RankNone;
		XjActorAccessor.TryGetInt(actor, rankKey, out int storedRank);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjCraftRankSchema, out int schema);
		int rank;
		if (schema >= CurrentRankSchema)
		{
			rank = Math.Clamp(storedRank, RankLianQi, RankJinDan);
		}
		else if (storedRank > 0)
		{
			// 六阶旧档压缩为四境品级：一二阶炼气、三四阶筑基、五阶紫府、六阶金丹。
			rank = storedRank <= 2 ? RankLianQi : storedRank <= 4 ? RankZhuJi : storedRank == 5 ? RankZiFu : RankJinDan;
		}
		else
		{
			rank = ResolveRankFromProficiency(proficiency);
		}
		rank = Math.Min(rank, ResolveRealmRankCap(actor));
		XjActorAccessor.SetInt(actor, rankKey, rank);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCraftRankSchema, CurrentRankSchema);
		return rank;
	}

	private static int GetAlchemyProficiency(Actor actor)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		return Math.Max(0, XjAlchemyRuntimeRegistry.ReadCrafter(actorId).TotalProficiency);
	}

	private static int GetArtifactProficiency(Actor actor, out int training, out int faQi, out int lingBao, out int faBao)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjArtifactTrainingSuccess, out training);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjArtifactFaQiSuccess, out faQi);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjArtifactLingBaoSuccess, out lingBao);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjArtifactFaBaoSuccess, out faBao);
		training = Math.Max(0, training); faQi = Math.Max(0, faQi); lingBao = Math.Max(0, lingBao); faBao = Math.Max(0, faBao);
		long total = (long)training * TrainingProficiencyPoints + (long)faQi * FaQiProficiencyPoints
			+ (long)lingBao * LingBaoProficiencyPoints + (long)faBao * FaBaoProficiencyPoints;
		return total > int.MaxValue ? int.MaxValue : (int)total;
	}

	private static int GetSimpleProfessionRank(Actor actor, string traitId, string proficiencyKey, string rankKey)
	{
		if (actor?.data == null || !actor.hasTrait(traitId)
			|| !string.Equals(XjCraftTraitRules.GetPrimaryTraitId(actor), traitId, StringComparison.Ordinal)) return RankNone;
		return ReadOrMigrateRank(actor, rankKey, GetSimpleProficiency(actor, proficiencyKey));
	}

	private static int GetSimpleProficiency(Actor actor, string key)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(key)) return 0;
		XjActorAccessor.TryGetInt(actor, key, out int value);
		return Math.Max(0, value);
	}

	private static void RecordSimpleProfessionProgress(Actor actor, string traitId, string proficiencyKey, int points)
	{
		if (actor?.data == null || points <= 0 || !actor.hasTrait(traitId)
			|| !string.Equals(XjCraftTraitRules.GetPrimaryTraitId(actor), traitId, StringComparison.Ordinal)) return;
		int current = GetSimpleProficiency(actor, proficiencyKey);
		XjActorAccessor.SetInt(actor, proficiencyKey, current > int.MaxValue - points ? int.MaxValue : current + points);
	}

	private static string BuildSimpleProfessionProgressSummary(Actor actor, string traitId, string proficiencyKey, string profession, string efficiencyLabel)
	{
		if (actor?.data == null || !actor.hasTrait(traitId)
			|| !string.Equals(XjCraftTraitRules.GetPrimaryTraitId(actor), traitId, StringComparison.Ordinal)) return string.Empty;
		string rankKey = string.Equals(traitId, XjCraftTraitRules.TalismanTraitId, StringComparison.Ordinal)
			? XjActorDataKeys.XjTalismanRank : XjActorDataKeys.XjFormationRank;
		int proficiency = GetSimpleProficiency(actor, proficiencyKey);
		int rank = ReadOrMigrateRank(actor, rankKey, proficiency);
		return BuildRankSummary(profession, rank, proficiency, efficiencyLabel);
	}

	private static string BuildRankSummary(string profession, int rank, int proficiency, string efficiencyLabel)
	{
		return GetRankDisplayName(rank, profession) + "（经验" + proficiency
			+ "，可处理至" + RankName(rank)
			+ "，成功率+" + FormatPercent(ResolveSuccessBonus(rank, proficiency))
			+ "，产出×" + GetProductionMultiplier(null, rank, proficiency).ToString("0.00", CultureInfo.InvariantCulture)
			+ "，" + efficiencyLabel
			+ "，" + BuildNextRankText(rank, proficiency) + "）";
	}

	private static string BuildArtifactAttemptStatus(Actor actor, bool nativeTraining)
	{
		if (actor?.data == null) return "尚未尝试炼器";
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjFaBaoLastAttemptYear, out int lastYear);
		if (lastYear <= 0) return "尚未尝试炼器";
		int currentYear = 0;
		try { currentYear = World.world?.map_stats?.year ?? 0; } catch { }
		int readyYear = lastYear + (nativeTraining ? NativeTrainingAttemptIntervalYears : XjFaBaoForgePolicy.AttemptIntervalYears);
		return currentYear > 0 && currentYear >= readyYear
			? "上次炼制" + lastYear + "年，炼制周期已就绪"
			: "上次炼制" + lastYear + "年，下次可炼制" + readyYear + "年";
	}

	private static bool IsNativeTrainingRealm(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal);
	}

	private static int ResolveRankFromProficiency(int proficiency)
	{
		if (proficiency >= JinDanThreshold) return RankJinDan;
		if (proficiency >= ZiFuThreshold) return RankZiFu;
		if (proficiency >= ZhuJiThreshold) return RankZhuJi;
		return RankLianQi;
	}

	private static int ResolveRealmRankCap(Actor actor)
	{
		int tier = XjRealmSuppression.GetRealmTier(actor);
		if (tier >= XjRealmSuppression.TierJinDan) return RankJinDan;
		if (tier >= XjRealmSuppression.TierZiFu) return RankZiFu;
		if (tier >= XjRealmSuppression.TierZhuJi) return RankZhuJi;
		return RankLianQi;
	}

	private static int RequiredProficiency(int targetRank) => targetRank switch
	{
		RankZhuJi => ZhuJiThreshold,
		RankZiFu => ZiFuThreshold,
		RankJinDan => JinDanThreshold,
		_ => 0
	};

	private static float ResolveSuccessBonus(int rank, int proficiency)
	{
		float rankBase = rank switch
		{
			RankJinDan => 0.24f,
			RankZiFu => 0.16f,
			RankZhuJi => 0.08f,
			_ => 0f
		};
		return rankBase + ResolveWithinRankProgress(rank, proficiency) * 0.06f;
	}

	private static float ResolveWithinRankProgress(int rank, int proficiency)
	{
		int start = rank switch
		{
			RankJinDan => JinDanThreshold,
			RankZiFu => ZiFuThreshold,
			RankZhuJi => ZhuJiThreshold,
			_ => 0
		};
		int end = rank switch
		{
			RankJinDan => MasteryCeiling,
			RankZiFu => JinDanThreshold,
			RankZhuJi => ZiFuThreshold,
			_ => ZhuJiThreshold
		};
		if (end <= start) return 1f;
		return Math.Clamp((Math.Max(0, proficiency) - start) / (float)(end - start), 0f, 1f);
	}

	private static string ResolveFormationLayerDisplay(int rank) => rank switch
	{
		RankJinDan => "宗门大阵与洞天玄韬",
		RankZiFu => "城镇守御阵与福地玄韬",
		RankZhuJi => "家族小阵",
		_ => "阵材、阵纹与基础维护"
	};

	private static string BuildNextRankText(int rank, int proficiency)
	{
		if (rank >= RankJinDan) return proficiency >= MasteryCeiling ? "金丹级圆满" : "金丹级精进中";
		int threshold = RequiredProficiency(rank + 1);
		return proficiency >= threshold
			? "经验已足，待自身境界承载下一品级"
			: "距" + RankName(rank + 1) + "尚需" + Math.Max(0, threshold - proficiency) + "点经验";
	}

	private static string GetRankDisplayName(int rank, string profession)
	{
		if (rank <= RankNone) return "未入艺";
		return RankName(rank) + profession + "师";
	}

	private static string RankName(int rank) => rank switch
	{
		RankJinDan => "金丹级",
		RankZiFu => "紫府级",
		RankZhuJi => "筑基级",
		_ => "炼气级"
	};

	private static string FormatPercent(float value)
	{
		return Math.Round(Math.Max(0f, value) * 100f, 1).ToString("0.#", CultureInfo.InvariantCulture) + "%";
	}
}
