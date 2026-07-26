using System;
using System.Collections.Generic;
using System.Linq;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Data.Alchemy;

internal enum XjAlchemyOwnerScope
{
	Personal = 1,
	Family = 2,
	ZongMen = 3
}

internal enum XjAlchemyMaterialGrade
{
	Low = 1,
	High = 2
}

/// <summary>
/// 丹药效果必须对应可执行的运行时行为，禁止只保留抽象标签。
/// </summary>
internal enum XjAlchemyEffectType
{
	ZhenYuan = 1,
	BreakthroughChance = 2,
	HealingPercent = 3,
	ShenTongChance = 4,
	LifespanYears = 5,
	BottleneckChance = 6,
	ShenTongTrainingSpeed = 7,
	SustainedZhenYuan = 8
}

internal enum XjAlchemyHistoryLevel
{
	Personal = 1,
	FamilyOrZongMen = 2,
	World = 3
}

internal enum XjAlchemyQuality
{
	Normal = 1,
	Superior = 2
}

internal enum XjAlchemyTaskStatus
{
	Planned = 0,
	MaterialCommitted = 1,
	InProgress = 2,
	Resolved = 3,
	ProductStored = 4,
	Completed = 5,
	Interrupted = 6
}

internal readonly struct XjAlchemyOwnerKey : IEquatable<XjAlchemyOwnerKey>
{
	internal readonly XjAlchemyOwnerScope Scope;
	internal readonly long OwnerId;

	internal XjAlchemyOwnerKey(XjAlchemyOwnerScope scope, long ownerId)
	{
		Scope = scope;
		OwnerId = ownerId;
	}

	internal bool IsValid => OwnerId > 0L
		&& (Scope == XjAlchemyOwnerScope.Personal
			|| Scope == XjAlchemyOwnerScope.Family
			|| Scope == XjAlchemyOwnerScope.ZongMen);

	public bool Equals(XjAlchemyOwnerKey other)
	{
		return Scope == other.Scope && OwnerId == other.OwnerId;
	}

	public override bool Equals(object obj)
	{
		return obj is XjAlchemyOwnerKey other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine((int)Scope, OwnerId);
	}

	public override string ToString()
	{
		return Scope + ":" + OwnerId;
	}
}

internal sealed class XjAlchemyMaterialDef
{
	internal string Id { get; }
	internal string NameKey { get; }
	internal string DescriptionKey { get; }
	internal XjAlchemyMaterialGrade Grade { get; }
	internal string IconPath { get; }
	internal bool IsLegacyAggregate { get; }

	internal XjAlchemyMaterialDef(
		string id,
		string nameKey,
		string descriptionKey,
		XjAlchemyMaterialGrade grade,
		string iconPath,
		bool isLegacyAggregate = false)
	{
		Id = id ?? string.Empty;
		NameKey = nameKey ?? string.Empty;
		DescriptionKey = descriptionKey ?? string.Empty;
		Grade = grade;
		IconPath = iconPath ?? string.Empty;
		IsLegacyAggregate = isLegacyAggregate;
	}
}

internal sealed class XjAlchemyPillDef
{
	internal string Id { get; }
	internal string NameKey { get; }
	internal string DescriptionKey { get; }
	internal string ShortDescriptionKey { get; }
	internal string MinApplicableRealmId { get; }
	internal string MaxApplicableRealmId { get; }
	internal XjAlchemyEffectType EffectType { get; }
	internal float EffectValue { get; }
	internal string EffectTargetRealmId { get; }
	internal int CooldownYears { get; }
	internal int ShelfLifeYears { get; }
	internal XjAlchemyHistoryLevel HistoryLevel { get; }
	internal string IconPath { get; }
	internal bool ProductionEnabled { get; }

	internal XjAlchemyPillDef(
		string id,
		string nameKey,
		string descriptionKey,
		string shortDescriptionKey,
		string minApplicableRealmId,
		string maxApplicableRealmId,
		XjAlchemyEffectType effectType,
		float effectValue,
		string effectTargetRealmId,
		int cooldownYears,
		int shelfLifeYears,
		XjAlchemyHistoryLevel historyLevel,
		string iconPath,
		bool productionEnabled)
	{
		Id = id ?? string.Empty;
		NameKey = nameKey ?? string.Empty;
		DescriptionKey = descriptionKey ?? string.Empty;
		ShortDescriptionKey = shortDescriptionKey ?? string.Empty;
		MinApplicableRealmId = minApplicableRealmId ?? string.Empty;
		MaxApplicableRealmId = maxApplicableRealmId ?? string.Empty;
		EffectType = effectType;
		EffectValue = Math.Max(0f, effectValue);
		EffectTargetRealmId = effectTargetRealmId ?? string.Empty;
		CooldownYears = Math.Max(0, cooldownYears);
		ShelfLifeYears = Math.Max(1, shelfLifeYears);
		HistoryLevel = historyLevel;
		IconPath = iconPath ?? string.Empty;
		ProductionEnabled = productionEnabled;
	}
}

internal readonly struct XjAlchemyIngredient
{
	internal string MaterialId { get; }
	internal int Count { get; }

	internal XjAlchemyIngredient(string materialId, int count)
	{
		MaterialId = materialId ?? string.Empty;
		Count = Math.Max(0, count);
	}
}

internal sealed class XjAlchemyRecipeDef
{
	internal string Id { get; }
	internal string NameKey { get; }
	internal string DescriptionKey { get; }
	internal string IconPath { get; }
	internal string PillId { get; }
	internal string MinCrafterRealmId { get; }
	internal IReadOnlyList<XjAlchemyIngredient> Ingredients { get; }
	internal IReadOnlyDictionary<string, int> RequiredMaterials { get; }
	internal string MainMaterialId { get; }
	internal int MainMaterialCount { get; }
	internal string AssistantMaterialId { get; }
	internal int AssistantMaterialCount { get; }
	internal int BaseDurationYears { get; }
	internal float BaseSuccessRate { get; }
	internal int MinYield { get; }
	internal int MaxYield { get; }
	internal int ShelfLifeYears { get; }
	internal int Difficulty { get; }
	internal string TransferRule { get; }
	internal XjAlchemyHistoryLevel HistoryLevel { get; }
	internal bool ProductionEnabled { get; }

	internal XjAlchemyRecipeDef(
		string id,
		string nameKey,
		string descriptionKey,
		string iconPath,
		string pillId,
		string minCrafterRealmId,
		IReadOnlyList<XjAlchemyIngredient> ingredients,
		int baseDurationYears,
		float baseSuccessRate,
		int minYield,
		int maxYield,
		int shelfLifeYears,
		int difficulty,
		string transferRule,
		XjAlchemyHistoryLevel historyLevel,
		bool productionEnabled)
	{
		Id = id ?? string.Empty;
		NameKey = nameKey ?? string.Empty;
		DescriptionKey = descriptionKey ?? string.Empty;
		IconPath = iconPath ?? string.Empty;
		PillId = pillId ?? string.Empty;
		MinCrafterRealmId = minCrafterRealmId ?? string.Empty;

		Dictionary<string, int> requiredMaterials = new(StringComparer.Ordinal);
		List<XjAlchemyIngredient> normalizedIngredients = new(ingredients?.Count ?? 0);
		if (ingredients != null)
		{
			for (int i = 0; i < ingredients.Count; i++)
			{
				XjAlchemyIngredient ingredient = ingredients[i];
				if (string.IsNullOrWhiteSpace(ingredient.MaterialId) || ingredient.Count <= 0) continue;
				if (requiredMaterials.TryGetValue(ingredient.MaterialId, out int existing))
				{
					requiredMaterials[ingredient.MaterialId] = existing + ingredient.Count;
					for (int n = 0; n < normalizedIngredients.Count; n++)
					{
						if (!string.Equals(normalizedIngredients[n].MaterialId, ingredient.MaterialId, StringComparison.Ordinal)) continue;
						normalizedIngredients[n] = new XjAlchemyIngredient(ingredient.MaterialId, existing + ingredient.Count);
						break;
					}
				}
				else
				{
					requiredMaterials[ingredient.MaterialId] = ingredient.Count;
					normalizedIngredients.Add(ingredient);
				}
			}
		}
		Ingredients = normalizedIngredients;
		RequiredMaterials = requiredMaterials;
		MainMaterialId = normalizedIngredients.Count > 0 ? normalizedIngredients[0].MaterialId : string.Empty;
		MainMaterialCount = normalizedIngredients.Count > 0 ? normalizedIngredients[0].Count : 0;
		AssistantMaterialId = normalizedIngredients.Count > 1 ? normalizedIngredients[1].MaterialId : string.Empty;
		AssistantMaterialCount = normalizedIngredients.Count > 1 ? normalizedIngredients[1].Count : 0;
		BaseDurationYears = Math.Max(1, baseDurationYears);
		BaseSuccessRate = Math.Clamp(baseSuccessRate, 0f, 1f);
		MinYield = Math.Max(1, minYield);
		MaxYield = Math.Max(MinYield, maxYield);
		ShelfLifeYears = Math.Max(1, shelfLifeYears);
		Difficulty = Math.Max(1, difficulty);
		TransferRule = transferRule ?? string.Empty;
		HistoryLevel = historyLevel;
		ProductionEnabled = productionEnabled;
	}
}

internal static class XjAlchemyCatalog
{
	internal const string RecipeIconPath = "GameResources/item/Arts/danyao/DanFang.png";
	// 0.6.1 旧存档兼容占位：载入时会按品阶拆分为具体药草，生产链不再使用抽象药材。
	internal const string MaterialLow = "xj_alchemy_material_low";
	internal const string MaterialHigh = "xj_alchemy_material_high";

	internal const string MaterialDiMaiCao = "xj_alchemy_material_di_mai_cao";
	internal const string MaterialZiSuYe = "xj_alchemy_material_zi_su_ye";
	internal const string MaterialYinHaoHao = "xj_alchemy_material_yin_hao_hao";
	internal const string MaterialBaiZhiHua = "xj_alchemy_material_bai_zhi_hua";
	internal const string MaterialWuLingJun = "xj_alchemy_material_wu_ling_jun";
	internal const string MaterialQingXuCao = "xj_alchemy_material_qing_xu_cao";
	internal const string MaterialChiZhuZhi = "xj_alchemy_material_chi_zhu_zhi";
	internal const string MaterialZiSuiLan = "xj_alchemy_material_zi_sui_lan";
	internal const string MaterialHanAi = "xj_alchemy_material_han_ai";
	internal const string MaterialHuangYaCao = "xj_alchemy_material_huang_ya_cao";
	internal const string MaterialQingLuoYe = "xj_alchemy_material_qing_luo_ye";
	internal const string MaterialJinZhanHua = "xj_alchemy_material_jin_zhan_hua";
	internal const string MaterialYunZhiPian = "xj_alchemy_material_yun_zhi_pian";
	internal const string MaterialKuYangSui = "xj_alchemy_material_ku_yang_sui";
	internal const string MaterialChiGenShen = "xj_alchemy_material_chi_gen_shen";
	internal const string MaterialPanSiTeng = "xj_alchemy_material_pan_si_teng";
	internal const string MaterialBiJieCao = "xj_alchemy_material_bi_jie_cao";

	internal const string MaterialChiYanHua = "xj_alchemy_material_chi_yan_hua";
	internal const string MaterialYueHuaSi = "xj_alchemy_material_yue_hua_si";
	internal const string MaterialXuanYinTeng = "xj_alchemy_material_xuan_yin_teng";
	internal const string MaterialBingPoLian = "xj_alchemy_material_bing_po_lian";
	internal const string MaterialBiMuZhi = "xj_alchemy_material_bi_mu_zhi";
	internal const string MaterialHanJingCao = "xj_alchemy_material_han_jing_cao";
	internal const string MaterialJinShen = "xj_alchemy_material_jin_shen";
	internal const string MaterialYuSuiZhu = "xj_alchemy_material_yu_sui_zhu";
	internal const string MaterialZiYueLan = "xj_alchemy_material_zi_yue_lan";
	internal const string MaterialBiLuoTeng = "xj_alchemy_material_bi_luo_teng";
	internal const string MaterialShuangXinHua = "xj_alchemy_material_shuang_xin_hua";
	internal const string MaterialQingLinSui = "xj_alchemy_material_qing_lin_sui";
	internal const string MaterialChiHuiXueTeng = "xj_alchemy_material_chi_hui_xue_teng";
	internal const string MaterialLiuHuoLing = "xj_alchemy_material_liu_huo_ling";
	internal const string MaterialZiYanLuo = "xj_alchemy_material_zi_yan_luo";
	internal const string MaterialChiShanHuZhi = "xj_alchemy_material_chi_shan_hu_zhi";

	// 炼气层次丹药
	internal const string PillSheYuan = "xj_alchemy_pill_she_yuan";
	internal const string PillYangYuan = "xj_alchemy_pill_yang_yuan";
	internal const string PillSuiYuan = "xj_alchemy_pill_sui_yuan";
	// 筑基层次丹药
	internal const string PillYuYa = "xj_alchemy_pill_yu_ya";
	internal const string PillHuiQiu = "xj_alchemy_pill_hui_qiu";
	internal const string PillHuMai = "xj_alchemy_pill_hu_mai";
	// 紫府层次丹药
	internal const string PillQingShen = "xj_alchemy_pill_qing_shen";
	internal const string PillHuanZhen = "xj_alchemy_pill_huan_zhen";
	internal const string PillTianYiTuCui = PillHuanZhen;
	internal const string PillChengJin = "xj_alchemy_pill_cheng_jin";
	internal const string PillSanQuan = "xj_alchemy_pill_san_quan";
	internal const string PillXuJiu = "xj_alchemy_pill_xu_jiu";
	internal const string PillNanGongXuanSui = PillXuJiu;
	internal const string PillHuiShuiXuanDao = "xj_alchemy_pill_hui_shui_xuan_dao";
	internal const string PillMingZhenHeShen = PillHuiShuiXuanDao;
	internal const string PillJiuYaoYanShou = "xj_alchemy_pill_jiu_yao_yan_shou";
	internal const string PillYuLuHuiMing = "xj_alchemy_pill_yu_lu_hui_ming";

	internal const string RecipeSheYuan = "xj_alchemy_recipe_she_yuan";
	internal const string RecipeYangYuan = "xj_alchemy_recipe_yang_yuan";
	internal const string RecipeSuiYuan = "xj_alchemy_recipe_sui_yuan";
	internal const string RecipeYuYa = "xj_alchemy_recipe_yu_ya";
	internal const string RecipeHuiQiu = "xj_alchemy_recipe_hui_qiu";
	internal const string RecipeHuMai = "xj_alchemy_recipe_hu_mai";
	internal const string RecipeQingShen = "xj_alchemy_recipe_qing_shen";
	internal const string RecipeHuanZhen = "xj_alchemy_recipe_huan_zhen";
	internal const string RecipeTianYiTuCui = RecipeHuanZhen;
	internal const string RecipeChengJin = "xj_alchemy_recipe_cheng_jin";
	internal const string RecipeSanQuan = "xj_alchemy_recipe_san_quan";
	internal const string RecipeXuJiu = "xj_alchemy_recipe_xu_jiu";
	internal const string RecipeNanGongXuanSui = RecipeXuJiu;
	internal const string RecipeHuiShuiXuanDao = "xj_alchemy_recipe_hui_shui_xuan_dao";
	internal const string RecipeMingZhenHeShen = RecipeHuiShuiXuanDao;
	internal const string RecipeJiuYaoYanShou = "xj_alchemy_recipe_jiu_yao_yan_shou";
	internal const string RecipeYuLuHuiMing = "xj_alchemy_recipe_yu_lu_hui_ming";

	internal static readonly string[] CoreLowMaterialIds =
	{
		MaterialDiMaiCao, MaterialZiSuYe, MaterialYinHaoHao, MaterialBaiZhiHua, MaterialWuLingJun,
		MaterialQingXuCao, MaterialChiZhuZhi, MaterialZiSuiLan, MaterialHanAi
	};

	internal static readonly string[] SupplementaryLowMaterialIds =
	{
		MaterialHuangYaCao, MaterialQingLuoYe, MaterialJinZhanHua, MaterialYunZhiPian,
		MaterialKuYangSui, MaterialChiGenShen, MaterialPanSiTeng, MaterialBiJieCao
	};

	internal static readonly string[] LowMaterialIds =
	{
		MaterialDiMaiCao, MaterialZiSuYe, MaterialYinHaoHao, MaterialBaiZhiHua, MaterialWuLingJun,
		MaterialQingXuCao, MaterialChiZhuZhi, MaterialZiSuiLan, MaterialHanAi,
		MaterialHuangYaCao, MaterialQingLuoYe, MaterialJinZhanHua, MaterialYunZhiPian,
		MaterialKuYangSui, MaterialChiGenShen, MaterialPanSiTeng, MaterialBiJieCao
	};

	internal static readonly string[] CoreHighMaterialIds =
	{
		MaterialChiYanHua, MaterialYueHuaSi, MaterialXuanYinTeng, MaterialBingPoLian, MaterialBiMuZhi,
		MaterialHanJingCao, MaterialJinShen, MaterialYuSuiZhu, MaterialZiYueLan
	};

	internal static readonly string[] SupplementaryHighMaterialIds =
	{
		MaterialBiLuoTeng, MaterialShuangXinHua, MaterialQingLinSui, MaterialChiHuiXueTeng,
		MaterialLiuHuoLing, MaterialZiYanLuo, MaterialChiShanHuZhi
	};

	internal static readonly string[] HighMaterialIds =
	{
		MaterialChiYanHua, MaterialYueHuaSi, MaterialXuanYinTeng, MaterialBingPoLian, MaterialBiMuZhi,
		MaterialHanJingCao, MaterialJinShen, MaterialYuSuiZhu, MaterialZiYueLan,
		MaterialBiLuoTeng, MaterialShuangXinHua, MaterialQingLinSui, MaterialChiHuiXueTeng,
		MaterialLiuHuoLing, MaterialZiYanLuo, MaterialChiShanHuZhi
	};

	internal static readonly string[] LongevityMaterialIds =
	{
		MaterialChiYanHua, MaterialYueHuaSi, MaterialXuanYinTeng, MaterialBingPoLian, MaterialBiMuZhi,
		MaterialHanJingCao, MaterialJinShen, MaterialYuSuiZhu, MaterialZiYueLan
	};

	internal static readonly string[] OrderedRecipeIds =
	{
		RecipeSheYuan, RecipeYangYuan, RecipeSuiYuan,
		RecipeYuYa, RecipeHuiQiu, RecipeHuMai,
		RecipeQingShen, RecipeTianYiTuCui, RecipeChengJin,
		RecipeSanQuan, RecipeNanGongXuanSui, RecipeMingZhenHeShen, RecipeJiuYaoYanShou
	};

	private static readonly Dictionary<string, XjAlchemyMaterialDef> materials = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, XjAlchemyPillDef> pills = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, XjAlchemyRecipeDef> recipes = new(StringComparer.Ordinal);
	private static bool initialized;

	internal static IReadOnlyDictionary<string, XjAlchemyMaterialDef> Materials
	{
		get { Init(); return materials; }
	}

	internal static IReadOnlyDictionary<string, XjAlchemyPillDef> Pills
	{
		get { Init(); return pills; }
	}

	internal static IReadOnlyDictionary<string, XjAlchemyRecipeDef> Recipes
	{
		get { Init(); return recipes; }
	}

	internal static void Init()
	{
		if (initialized) return;
		materials.Clear();
		pills.Clear();
		recipes.Clear();

		Add(new XjAlchemyMaterialDef(MaterialLow, "xuanjian.alchemy.material.low", "xuanjian.alchemy.material.low.desc",
			XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-1.png", true));
		Add(new XjAlchemyMaterialDef(MaterialHigh, "xuanjian.alchemy.material.high", "xuanjian.alchemy.material.high.desc",
			XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-1.png", true));

		AddMaterial(MaterialDiMaiCao, "di_mai_cao", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-1.png");
		AddMaterial(MaterialZiSuYe, "zi_su_ye", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-2.png");
		AddMaterial(MaterialYinHaoHao, "yin_hao_hao", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-3.png");
		AddMaterial(MaterialBaiZhiHua, "bai_zhi_hua", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-4.png");
		AddMaterial(MaterialWuLingJun, "wu_ling_jun", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-5.png");
		AddMaterial(MaterialQingXuCao, "qing_xu_cao", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-6.png");
		AddMaterial(MaterialChiZhuZhi, "chi_zhu_zhi", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-7.png");
		AddMaterial(MaterialZiSuiLan, "zi_sui_lan", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-8.png");
		AddMaterial(MaterialHanAi, "han_ai", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-9.png");
		AddMaterial(MaterialHuangYaCao, "huang_ya_cao", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-10.png");
		AddMaterial(MaterialQingLuoYe, "qing_luo_ye", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-11.png");
		AddMaterial(MaterialJinZhanHua, "jin_zhan_hua", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-12.png");
		AddMaterial(MaterialYunZhiPian, "yun_zhi_pian", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-13.png");
		AddMaterial(MaterialKuYangSui, "ku_yang_sui", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-14.png");
		AddMaterial(MaterialChiGenShen, "chi_gen_shen", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-15.png");
		AddMaterial(MaterialPanSiTeng, "pan_si_teng", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-16.png");
		AddMaterial(MaterialBiJieCao, "bi_jie_cao", XjAlchemyMaterialGrade.Low, "GameResources/item/Arts/yaocai/LowYC/LowYc-17.png");

		AddMaterial(MaterialChiYanHua, "chi_yan_hua", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-1.png");
		AddMaterial(MaterialYueHuaSi, "yue_hua_si", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-2.png");
		AddMaterial(MaterialXuanYinTeng, "xuan_yin_teng", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-3.png");
		AddMaterial(MaterialBingPoLian, "bing_po_lian", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-4.png");
		AddMaterial(MaterialBiMuZhi, "bi_mu_zhi", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-5.png");
		AddMaterial(MaterialHanJingCao, "han_jing_cao", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-6.png");
		AddMaterial(MaterialJinShen, "jin_shen", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-7.png");
		AddMaterial(MaterialYuSuiZhu, "yu_sui_zhu", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-8.png");
		AddMaterial(MaterialZiYueLan, "zi_yue_lan", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-9.png");
		AddMaterial(MaterialBiLuoTeng, "bi_luo_teng", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-10.png");
		AddMaterial(MaterialShuangXinHua, "shuang_xin_hua", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-11.png");
		AddMaterial(MaterialQingLinSui, "qing_lin_sui", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-12.png");
		AddMaterial(MaterialChiHuiXueTeng, "chi_hui_xue_teng", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-13.png");
		AddMaterial(MaterialLiuHuoLing, "liu_huo_ling", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-14.png");
		AddMaterial(MaterialZiYanLuo, "zi_yan_luo", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-15.png");
		AddMaterial(MaterialChiShanHuZhi, "chi_shan_hu_zhi", XjAlchemyMaterialGrade.High, "GameResources/item/Arts/yaocai/HighYC/HighYc-16.png");

		const string pill1 = "GameResources/item/Arts/danyao/DanYao-1.png";
		const string pill2 = "GameResources/item/Arts/danyao/DanYao-2.png";
		const string pill3 = "GameResources/item/Arts/danyao/DanYao-3.png";
		const string pill4 = "GameResources/item/Arts/danyao/DanYao-4.png";
		const string pill5 = "GameResources/item/Arts/danyao/DanYao-5.png";
		const string pill6 = "GameResources/item/Arts/danyao/DanYao-6.png";
		const string pill7 = "GameResources/item/Arts/danyao/DanYao-7.png";
		const string pill8 = "GameResources/item/Arts/danyao/DanYao-8.png";
		const string pill9 = "GameResources/item/Arts/danyao/DanYao-9.png";
		const string pill10 = "GameResources/item/Arts/danyao/DanYao-10.png";
		const string pill11 = "GameResources/item/Arts/danyao/DanYao-11.png";
		const string pill12 = "GameResources/item/Arts/danyao/DanYao-12.png";
		const string pill13 = "GameResources/item/Arts/danyao/DanYao-13.png";
		const string pill14 = "GameResources/item/Arts/danyao/DanYao-14.png";

		AddPill(PillSheYuan, "she_yuan", XjRealmIds.TaiXi, XjRealmIds.TaiXi,
			XjAlchemyEffectType.ZhenYuan, 500f, string.Empty, 3, 20, XjAlchemyHistoryLevel.FamilyOrZongMen, pill1);
		AddPill(PillYangYuan, "yang_yuan", XjRealmIds.TaiXi, XjRealmIds.LianQi,
			XjAlchemyEffectType.HealingPercent, 0.25f, string.Empty, 1, 18, XjAlchemyHistoryLevel.FamilyOrZongMen, pill2);
		AddPill(PillSuiYuan, "sui_yuan", XjRealmIds.ZhuJi, XjRealmIds.ZhuJi,
			XjAlchemyEffectType.BreakthroughChance, 0.08f, XjRealmIds.ZiFu, 8, 30, XjAlchemyHistoryLevel.World, pill3);
		AddPill(PillYuYa, "yu_ya", XjRealmIds.LianQi, XjRealmIds.LianQi,
			XjAlchemyEffectType.ZhenYuan, 1000f, string.Empty, 2, 25, XjAlchemyHistoryLevel.FamilyOrZongMen, pill4);
		AddPill(PillHuiQiu, "hui_qiu", XjRealmIds.ZhuJi, XjRealmIds.ZhuJi,
			XjAlchemyEffectType.ZhenYuan, 2000f, string.Empty, 5, 15, XjAlchemyHistoryLevel.FamilyOrZongMen, pill5);
		AddPill(PillHuMai, "hu_mai", XjRealmIds.ZhuJi, XjRealmIds.ZhuJi,
			XjAlchemyEffectType.HealingPercent, 0.35f, string.Empty, 2, 22, XjAlchemyHistoryLevel.FamilyOrZongMen, pill6);
		AddPill(PillQingShen, "qing_shen", XjRealmIds.ZiFu, XjRealmIds.ZiFu,
			XjAlchemyEffectType.ShenTongChance, 0.12f, string.Empty, 2, 35, XjAlchemyHistoryLevel.FamilyOrZongMen, pill7);
		AddPill(PillTianYiTuCui, "tian_yi_tu_cui", XjRealmIds.ZiFu, XjRealmIds.JinDan,
			XjAlchemyEffectType.HealingPercent, 0.60f, string.Empty, 8, 35, XjAlchemyHistoryLevel.FamilyOrZongMen, pill8);
		AddPill(PillChengJin, "cheng_jin", XjRealmIds.ZiFu, XjRealmIds.ZiFu,
			XjAlchemyEffectType.BreakthroughChance, 0.05f, XjRealmIds.JinDan, 20, 40, XjAlchemyHistoryLevel.World, pill9);
		AddPill(PillSanQuan, "san_quan", XjRealmIds.LianQi, XjRealmIds.ZhuJi,
			XjAlchemyEffectType.BottleneckChance, 0.06f, string.Empty, 8, 25, XjAlchemyHistoryLevel.FamilyOrZongMen, pill10);
		AddPill(PillNanGongXuanSui, "nan_gong_xuan_sui", XjRealmIds.ZiFu, XjRealmIds.ZiFu,
			XjAlchemyEffectType.SustainedZhenYuan, 600f, string.Empty, 10, 35, XjAlchemyHistoryLevel.FamilyOrZongMen, pill11);
		AddPill(PillMingZhenHeShen, "ming_zhen_he_shen", XjRealmIds.ZiFu, XjRealmIds.ZiFu,
			XjAlchemyEffectType.ShenTongTrainingSpeed, 0.50f, string.Empty, 20, 40, XjAlchemyHistoryLevel.FamilyOrZongMen, pill12);
		AddPill(PillJiuYaoYanShou, "jiu_yao_yan_shou", XjRealmIds.ZiFu, XjRealmIds.JinDan,
			XjAlchemyEffectType.LifespanYears, 500f, string.Empty, 100, 100, XjAlchemyHistoryLevel.World, pill13);
		// 玉露回命丹仅保留旧档兼容：旧库存读档时迁移为天一吐萃丹，不再生产或展示。
		AddPill(PillYuLuHuiMing, "yu_lu_hui_ming", XjRealmIds.TaiXi, XjRealmIds.JinDan,
			XjAlchemyEffectType.HealingPercent, 0.60f, string.Empty, 5, 60, XjAlchemyHistoryLevel.FamilyOrZongMen, pill14, false);

		// 每种丹药仍只对应一张固定丹方；新增复方允许真实药草跨方复用，但禁止抽象药材与重复配方。
		AddRecipe(RecipeSheYuan, "she_yuan", pill1, PillSheYuan, XjRealmIds.LianQi,
			MaterialDiMaiCao, 4, MaterialZiSuYe, 2, 2, 0.72f, 4, 8, 20, 1,
			"personal_family_or_authorized_zongmen", XjAlchemyHistoryLevel.FamilyOrZongMen);
		AddRecipe(RecipeYangYuan, "yang_yuan", pill2, PillYangYuan, XjRealmIds.LianQi,
			MaterialYinHaoHao, 3, MaterialBaiZhiHua, 2, 2, 0.68f, 3, 6, 18, 1,
			"personal_family_or_authorized_zongmen", XjAlchemyHistoryLevel.FamilyOrZongMen);
		AddRecipe(RecipeSuiYuan, "sui_yuan", pill3, PillSuiYuan, XjRealmIds.ZhuJi,
			MaterialWuLingJun, 3, MaterialChiYanHua, 1, 3, 0.48f, 1, 3, 30, 2,
			"family_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.World);
		AddRecipe(RecipeYuYa, "yu_ya", pill4, PillYuYa, XjRealmIds.LianQi,
			MaterialQingXuCao, 3, MaterialYueHuaSi, 1, 3, 0.60f, 2, 5, 25, 2,
			"family_or_authorized_zongmen", XjAlchemyHistoryLevel.FamilyOrZongMen);
		AddRecipe(RecipeHuiQiu, "hui_qiu", pill5, PillHuiQiu, XjRealmIds.ZhuJi,
			MaterialChiZhuZhi, 2, MaterialXuanYinTeng, 2, 4, 0.50f, 1, 3, 15, 3,
			"family_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.FamilyOrZongMen);
		AddRecipe(RecipeHuMai, "hu_mai", pill6, PillHuMai, XjRealmIds.ZhuJi,
			MaterialZiSuiLan, 3, MaterialBingPoLian, 2, 4, 0.58f, 2, 4, 22, 2,
			"family_or_authorized_zongmen", XjAlchemyHistoryLevel.FamilyOrZongMen);
		AddRecipe(RecipeQingShen, "qing_shen", pill7, PillQingShen, XjRealmIds.ZiFu,
			MaterialHanAi, 2, MaterialBiMuZhi, 3, 5, 0.40f, 1, 3, 35, 3,
			"family_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.FamilyOrZongMen);
		AddRecipe(RecipeTianYiTuCui, "tian_yi_tu_cui", pill8, PillTianYiTuCui, XjRealmIds.ZiFu,
			MaterialHanJingCao, 3, MaterialJinShen, 3, 6, 0.35f, 1, 2, 30, 4,
			"family_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.FamilyOrZongMen);
		AddRecipe(RecipeChengJin, "cheng_jin", pill9, PillChengJin, XjRealmIds.ZiFu,
			MaterialYuSuiZhu, 4, MaterialZiYueLan, 4, 8, 0.15f, 1, 2, 40, 5,
			"family_core_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.World);

		AddRecipeMulti(RecipeSanQuan, "san_quan", pill10, PillSanQuan, XjRealmIds.ZhuJi,
			new[]
			{
				new XjAlchemyIngredient(MaterialWuLingJun, 4),
				new XjAlchemyIngredient(MaterialYueHuaSi, 2),
				new XjAlchemyIngredient(MaterialChiYanHua, 1)
			},
			4, 0.45f, 1, 2, 25, 3,
			"family_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.World);
		AddRecipeMulti(RecipeNanGongXuanSui, "nan_gong_xuan_sui", pill11, PillNanGongXuanSui, XjRealmIds.ZiFu,
			new[]
			{
				new XjAlchemyIngredient(MaterialZiSuiLan, 4),
				new XjAlchemyIngredient(MaterialBingPoLian, 3),
				new XjAlchemyIngredient(MaterialJinShen, 2)
			},
			6, 0.32f, 1, 2, 35, 4,
			"family_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.FamilyOrZongMen);
		AddRecipeMulti(RecipeMingZhenHeShen, "ming_zhen_he_shen", pill12, PillMingZhenHeShen, XjRealmIds.ZiFu,
			new[]
			{
				new XjAlchemyIngredient(MaterialHanAi, 3),
				new XjAlchemyIngredient(MaterialXuanYinTeng, 3),
				new XjAlchemyIngredient(MaterialBiMuZhi, 2),
				new XjAlchemyIngredient(MaterialZiYueLan, 1)
			},
			6, 0.30f, 1, 2, 25, 4,
			"family_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.FamilyOrZongMen);
		AddRecipeMulti(RecipeJiuYaoYanShou, "jiu_yao_yan_shou", pill13, PillJiuYaoYanShou, XjRealmIds.ZiFu,
			new[]
			{
				new XjAlchemyIngredient(MaterialChiYanHua, 9),
				new XjAlchemyIngredient(MaterialYueHuaSi, 9),
				new XjAlchemyIngredient(MaterialXuanYinTeng, 9),
				new XjAlchemyIngredient(MaterialBingPoLian, 9),
				new XjAlchemyIngredient(MaterialBiMuZhi, 9),
				new XjAlchemyIngredient(MaterialHanJingCao, 9),
				new XjAlchemyIngredient(MaterialJinShen, 9),
				new XjAlchemyIngredient(MaterialYuSuiZhu, 9),
				new XjAlchemyIngredient(MaterialZiYueLan, 9)
			},
			12, 0.20f, 1, 1, 100, 7,
			"family_core_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.World);
		AddRecipeMulti(RecipeYuLuHuiMing, "yu_lu_hui_ming", pill14, PillYuLuHuiMing, XjRealmIds.ZiFu,
			new[]
			{
				new XjAlchemyIngredient(MaterialHuangYaCao, 1),
				new XjAlchemyIngredient(MaterialQingLuoYe, 1),
				new XjAlchemyIngredient(MaterialJinZhanHua, 1),
				new XjAlchemyIngredient(MaterialYunZhiPian, 1),
				new XjAlchemyIngredient(MaterialKuYangSui, 1),
				new XjAlchemyIngredient(MaterialChiGenShen, 1),
				new XjAlchemyIngredient(MaterialPanSiTeng, 1),
				new XjAlchemyIngredient(MaterialBiJieCao, 1),
				new XjAlchemyIngredient(MaterialBiLuoTeng, 1),
				new XjAlchemyIngredient(MaterialShuangXinHua, 1),
				new XjAlchemyIngredient(MaterialQingLinSui, 1),
				new XjAlchemyIngredient(MaterialChiHuiXueTeng, 1),
				new XjAlchemyIngredient(MaterialLiuHuoLing, 1),
				new XjAlchemyIngredient(MaterialZiYanLuo, 1),
				new XjAlchemyIngredient(MaterialChiShanHuZhi, 1)
			},
			8, 0.30f, 1, 2, 60, 5,
			"family_secret_or_authorized_zongmen", XjAlchemyHistoryLevel.World, false);

		Validate();
		initialized = true;
	}

	internal static bool TryGetMaterial(string id, out XjAlchemyMaterialDef definition)
	{
		Init();
		return materials.TryGetValue(id ?? string.Empty, out definition);
	}

	internal static bool TryGetPill(string id, out XjAlchemyPillDef definition)
	{
		Init();
		return pills.TryGetValue(id ?? string.Empty, out definition);
	}

	internal static bool TryGetRecipe(string id, out XjAlchemyRecipeDef definition)
	{
		Init();
		return recipes.TryGetValue(id ?? string.Empty, out definition);
	}

	internal static IReadOnlyList<string> GetMaterialIds(XjAlchemyMaterialGrade grade)
	{
		return grade == XjAlchemyMaterialGrade.High ? HighMaterialIds : LowMaterialIds;
	}

	internal static bool IsLegacyAggregateMaterial(string materialId)
	{
		return string.Equals(materialId, MaterialLow, StringComparison.Ordinal)
			|| string.Equals(materialId, MaterialHigh, StringComparison.Ordinal);
	}

	private static void AddMaterial(string id, string keySuffix, XjAlchemyMaterialGrade grade, string iconPath)
	{
		Add(new XjAlchemyMaterialDef(
			id,
			"xuanjian.alchemy.material." + keySuffix,
			"xuanjian.alchemy.material." + keySuffix + ".desc",
			grade,
			iconPath));
	}

	private static void AddPill(
		string id,
		string keySuffix,
		string minRealm,
		string maxRealm,
		XjAlchemyEffectType effectType,
		float effectValue,
		string effectTargetRealmId,
		int cooldownYears,
		int shelfLifeYears,
		XjAlchemyHistoryLevel historyLevel,
		string iconPath,
		bool productionEnabled = true)
	{
		Add(new XjAlchemyPillDef(
			id,
			"xuanjian.alchemy.pill." + keySuffix,
			"xuanjian.alchemy.pill." + keySuffix + ".desc",
			"xuanjian.alchemy.pill." + keySuffix + ".short",
			minRealm,
			maxRealm,
			effectType,
			effectValue,
			effectTargetRealmId,
			cooldownYears,
			shelfLifeYears,
			historyLevel,
			iconPath,
			productionEnabled));
	}

	private static void AddRecipe(
		string id,
		string keySuffix,
		string iconPath,
		string pillId,
		string minCrafterRealmId,
		string mainMaterialId,
		int mainMaterialCount,
		string assistantMaterialId,
		int assistantMaterialCount,
		int baseDurationYears,
		float baseSuccessRate,
		int minYield,
		int maxYield,
		int shelfLifeYears,
		int difficulty,
		string transferRule,
		XjAlchemyHistoryLevel historyLevel,
		bool productionEnabled = true)
	{
		AddRecipeMulti(
			id, keySuffix, iconPath, pillId, minCrafterRealmId,
			new[]
			{
				new XjAlchemyIngredient(mainMaterialId, mainMaterialCount),
				new XjAlchemyIngredient(assistantMaterialId, assistantMaterialCount)
			},
			baseDurationYears, baseSuccessRate, minYield, maxYield, shelfLifeYears,
			difficulty, transferRule, historyLevel, productionEnabled);
	}

	private static void AddRecipeMulti(
		string id,
		string keySuffix,
		string iconPath,
		string pillId,
		string minCrafterRealmId,
		IReadOnlyList<XjAlchemyIngredient> ingredients,
		int baseDurationYears,
		float baseSuccessRate,
		int minYield,
		int maxYield,
		int shelfLifeYears,
		int difficulty,
		string transferRule,
		XjAlchemyHistoryLevel historyLevel,
		bool productionEnabled = true)
	{
		Add(new XjAlchemyRecipeDef(
			id,
			"xuanjian.alchemy.recipe." + keySuffix,
			"xuanjian.alchemy.recipe." + keySuffix + ".desc",
			iconPath,
			pillId,
			minCrafterRealmId,
			ingredients,
			baseDurationYears,
			baseSuccessRate,
			minYield,
			maxYield,
			shelfLifeYears,
			difficulty,
			transferRule,
			historyLevel,
			productionEnabled));
	}

	private static void Add(XjAlchemyMaterialDef definition)
	{
		if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || !materials.TryAdd(definition.Id, definition))
			throw new InvalidOperationException("炼丹药材定义重复或无效。" + definition?.Id);
	}

	private static void Add(XjAlchemyPillDef definition)
	{
		if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || !pills.TryAdd(definition.Id, definition))
			throw new InvalidOperationException("丹药定义重复或无效。" + definition?.Id);
	}

	private static void Add(XjAlchemyRecipeDef definition)
	{
		if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || !recipes.TryAdd(definition.Id, definition))
			throw new InvalidOperationException("丹方定义重复或无效。" + definition?.Id);
	}

	private static void Validate()
	{
		HashSet<string> mappedPills = new(StringComparer.Ordinal);
		HashSet<string> fixedFormulas = new(StringComparer.Ordinal);
		foreach (XjAlchemyRecipeDef recipe in recipes.Values)
		{
			if (!pills.TryGetValue(recipe.PillId, out XjAlchemyPillDef pill))
				throw new InvalidOperationException("丹方引用了不存在的丹药：" + recipe.Id + " -> " + recipe.PillId);
			if (!mappedPills.Add(recipe.PillId))
				throw new InvalidOperationException("同一种丹药只能对应一张固定丹方：" + recipe.PillId);
			if (recipe.Ingredients == null || recipe.Ingredients.Count == 0)
				throw new InvalidOperationException("丹方没有真实药材：" + recipe.Id);

			List<string> formulaParts = new(recipe.Ingredients.Count);
			for (int i = 0; i < recipe.Ingredients.Count; i++)
			{
				XjAlchemyIngredient ingredient = recipe.Ingredients[i];
				if (ingredient.Count <= 0 || !materials.ContainsKey(ingredient.MaterialId))
					throw new InvalidOperationException("丹方引用了无效药材：" + recipe.Id + " -> " + ingredient.MaterialId);
				if (IsLegacyAggregateMaterial(ingredient.MaterialId))
					throw new InvalidOperationException("生产丹方禁止使用抽象药材：" + recipe.Id);
				formulaParts.Add(ingredient.MaterialId + ":" + ingredient.Count);
			}

			if (string.IsNullOrWhiteSpace(recipe.NameKey)
				|| string.IsNullOrWhiteSpace(recipe.DescriptionKey)
				|| string.IsNullOrWhiteSpace(recipe.IconPath)
				|| !string.Equals(recipe.IconPath, pill.IconPath, StringComparison.Ordinal))
				throw new InvalidOperationException("丹方命名、描述或图片未与丹药绑定：" + recipe.Id);

			string formula = string.Join("|", formulaParts);
			if (!fixedFormulas.Add(formula))
				throw new InvalidOperationException("丹方药材组合重复，无法区分固定炼制法：" + recipe.Id);
		}

		if (mappedPills.Count != pills.Count)
			throw new InvalidOperationException("存在没有固定丹方的丹药定义。");
		foreach (XjAlchemyPillDef pill in pills.Values)
		{
			if (pill.ProductionEnabled && !recipes.Values.Any(recipe => recipe.ProductionEnabled && string.Equals(recipe.PillId, pill.Id, StringComparison.Ordinal)))
				throw new InvalidOperationException("启用生产的丹药没有启用丹方：" + pill.Id);
		}
		if (OrderedRecipeIds.Any(id => !recipes.ContainsKey(id)))
			throw new InvalidOperationException("丹方顺序表引用了不存在的丹方。");
		if (!recipes.TryGetValue(RecipeJiuYaoYanShou, out XjAlchemyRecipeDef longevity)
			|| longevity.Ingredients.Count != LongevityMaterialIds.Length
			|| LongevityMaterialIds.Any(id => !longevity.RequiredMaterials.TryGetValue(id, out int count) || count != 9)
			|| longevity.MinYield != 1 || longevity.MaxYield != 1)
			throw new InvalidOperationException("九曜延寿丹药草必须为九种上品药材各九份；运行时另需消耗家族重宝仓库中的金丹金性一缕，并且一炉只成一枚。");
	}
}
