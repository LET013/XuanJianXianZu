using System;
using System.Collections.Generic;
using NeoModLoader.General;

namespace XuanJianVNext.Data.Alchemy;

/// <summary>
/// 炼丹文本的唯一解析入口。运行时优先读取 locale；locale 尚未加载时使用稳定中文回退，
/// 避免系统日志、历史记录与 UI 各自维护另一套名称。
/// </summary>
internal static class XjAlchemyText
{
	private static readonly Dictionary<string, string> fallback = new(StringComparer.Ordinal)
	{
		["xuanjian.alchemy.material.low"] = "下品药材",
		["xuanjian.alchemy.material.low.desc"] = "旧存档中的下品药材总量，载入时会拆分为具体药草。",
		["xuanjian.alchemy.material.high"] = "上品药材",
		["xuanjian.alchemy.material.high.desc"] = "旧存档中的上品药材总量，载入时会拆分为具体药草。",
		["xuanjian.alchemy.material.di_mai_cao"] = "地脉草",
		["xuanjian.alchemy.material.di_mai_cao.desc"] = "贴地而生，草根含有温和土气，是蛇元丹的主药。",
		["xuanjian.alchemy.material.zi_su_ye"] = "紫苏叶",
		["xuanjian.alchemy.material.zi_su_ye.desc"] = "叶脉微紫，药性温润，可调和地脉草的浊气。",
		["xuanjian.alchemy.material.yin_hao_hao"] = "银蒿",
		["xuanjian.alchemy.material.yin_hao_hao.desc"] = "茎叶覆银霜，善于收敛散乱气血，是养元丹的主药。",
		["xuanjian.alchemy.material.bai_zhi_hua"] = "白芷花",
		["xuanjian.alchemy.material.bai_zhi_hua.desc"] = "花香清正，可通脉和血，用作养元丹佐药。",
		["xuanjian.alchemy.material.wu_ling_jun"] = "五灵菌",
		["xuanjian.alchemy.material.wu_ling_jun.desc"] = "菌盖生五色细纹，可聚拢散逸灵机，是开府丹的主药。",
		["xuanjian.alchemy.material.qing_xu_cao"] = "青须草",
		["xuanjian.alchemy.material.qing_xu_cao.desc"] = "叶尖生青色细须，能缓缓滋养气海，是玉芽丹的主药。",
		["xuanjian.alchemy.material.chi_zhu_zhi"] = "赤珠芝",
		["xuanjian.alchemy.material.chi_zhu_zhi.desc"] = "芝冠如赤珠，药力迅烈，可用于炼制会秋丹。",
		["xuanjian.alchemy.material.zi_sui_lan"] = "紫穗兰",
		["xuanjian.alchemy.material.zi_sui_lan.desc"] = "花穗蕴有柔和木气，可护持受损经脉。",
		["xuanjian.alchemy.material.han_ai"] = "寒艾",
		["xuanjian.alchemy.material.han_ai.desc"] = "艾叶冷香凝神，可压下紫府杂念，是清神丹的主药。",
		["xuanjian.alchemy.material.huang_ya_cao"] = "黄芽草",
		["xuanjian.alchemy.material.huang_ya_cao.desc"] = "叶心生淡黄嫩芽，能缓和药性冲突，常作复方引药。",
		["xuanjian.alchemy.material.qing_luo_ye"] = "青络叶",
		["xuanjian.alchemy.material.qing_luo_ye.desc"] = "叶脉如青络相连，可疏通气血并承接多味药力。",
		["xuanjian.alchemy.material.jin_zhan_hua"] = "金盏花",
		["xuanjian.alchemy.material.jin_zhan_hua.desc"] = "花冠金黄，药性温暖，善于唤回衰弱生机。",
		["xuanjian.alchemy.material.yun_zhi_pian"] = "云芝片",
		["xuanjian.alchemy.material.yun_zhi_pian.desc"] = "层叠如云的菌芝薄片，可吸附杂质并稳定丹液。",
		["xuanjian.alchemy.material.ku_yang_sui"] = "枯阳穗",
		["xuanjian.alchemy.material.ku_yang_sui.desc"] = "枯黄穗粒内仍存一线阳气，可在药力衰竭时续接生机。",
		["xuanjian.alchemy.material.chi_gen_shen"] = "赤根参",
		["xuanjian.alchemy.material.chi_gen_shen.desc"] = "根须赤红，能补益血气，是疗伤复方中的常用辅材。",
		["xuanjian.alchemy.material.pan_si_teng"] = "盘丝藤",
		["xuanjian.alchemy.material.pan_si_teng.desc"] = "藤条盘曲如丝，可将散乱药力束成一线。",
		["xuanjian.alchemy.material.bi_jie_cao"] = "碧节草",
		["xuanjian.alchemy.material.bi_jie_cao.desc"] = "茎节层层泛碧，药性平稳，适合调和多味复方。",
		["xuanjian.alchemy.material.chi_yan_hua"] = "赤焰花",
		["xuanjian.alchemy.material.chi_yan_hua.desc"] = "花心如火而不灼手，能催化五灵菌中的灵机。",
		["xuanjian.alchemy.material.yue_hua_si"] = "月华丝",
		["xuanjian.alchemy.material.yue_hua_si.desc"] = "夜间吐出的银白草丝，可纯化筑基层次药力。",
		["xuanjian.alchemy.material.xuan_yin_teng"] = "玄阴藤",
		["xuanjian.alchemy.material.xuan_yin_teng.desc"] = "藤汁幽寒，能锁住赤珠芝骤发的药性。",
		["xuanjian.alchemy.material.bing_po_lian"] = "冰魄莲",
		["xuanjian.alchemy.material.bing_po_lian.desc"] = "莲瓣凝霜不化，可镇定经脉伤势。",
		["xuanjian.alchemy.material.bi_mu_zhi"] = "碧木脂",
		["xuanjian.alchemy.material.bi_mu_zhi.desc"] = "古木凝成的碧色灵脂，能温养神意、稳固玄府。",
		["xuanjian.alchemy.material.han_jing_cao"] = "寒晶草",
		["xuanjian.alchemy.material.han_jing_cao.desc"] = "叶面结晶如冰，可收束紫府重创后的散乱真元。",
		["xuanjian.alchemy.material.jin_shen"] = "金参",
		["xuanjian.alchemy.material.jin_shen.desc"] = "参体带淡金纹路，补益精气，是天一吐萃、南宫玄绥与存神养身诸丹的重要佐药。",
		["xuanjian.alchemy.material.yu_sui_zhu"] = "玉髓竹",
		["xuanjian.alchemy.material.yu_sui_zhu.desc"] = "竹节内凝有玉髓般的灵液，可承载金性。",
		["xuanjian.alchemy.material.zi_yue_lan"] = "紫月兰",
		["xuanjian.alchemy.material.zi_yue_lan.desc"] = "只在月华极盛时开放，花心可缓和求金时的冲突。",
		["xuanjian.alchemy.material.bi_luo_teng"] = "碧螺藤",
		["xuanjian.alchemy.material.bi_luo_teng.desc"] = "藤身盘成碧螺，能锁住将散未散的生机。",
		["xuanjian.alchemy.material.shuang_xin_hua"] = "霜心花",
		["xuanjian.alchemy.material.shuang_xin_hua.desc"] = "花心凝霜而瓣色莹白，可镇住重伤后的灼乱气血。",
		["xuanjian.alchemy.material.qing_lin_sui"] = "青麟穗",
		["xuanjian.alchemy.material.qing_lin_sui.desc"] = "穗片层叠如青鳞，能护持脏腑与经络不散。",
		["xuanjian.alchemy.material.chi_hui_xue_teng"] = "赤虺血藤",
		["xuanjian.alchemy.material.chi_hui_xue_teng.desc"] = "藤纹如赤虺盘绕，药性猛烈，可催动沉寂血气。",
		["xuanjian.alchemy.material.liu_huo_ling"] = "流火翎",
		["xuanjian.alchemy.material.liu_huo_ling.desc"] = "叶形似燃烧羽翎，蕴有流动而不暴烈的火性。",
		["xuanjian.alchemy.material.zi_yan_luo"] = "紫烟萝",
		["xuanjian.alchemy.material.zi_yan_luo.desc"] = "花叶常笼淡紫烟气，可收敛神魂震荡与药毒。",
		["xuanjian.alchemy.material.chi_shan_hu_zhi"] = "赤珊瑚芝",
		["xuanjian.alchemy.material.chi_shan_hu_zhi.desc"] = "芝体如赤珊瑚分枝，内蕴浓郁生机，是回命复方的定鼎之药。",
		["xuanjian.alchemy.pill.she_yuan"] = "蛇元丹",
		["xuanjian.alchemy.pill.yang_yuan"] = "养元丹",
		["xuanjian.alchemy.pill.sui_yuan"] = "开府丹",
		["xuanjian.alchemy.pill.yu_ya"] = "玉芽丹",
		["xuanjian.alchemy.pill.hui_qiu"] = "会秋丹",
		["xuanjian.alchemy.pill.hu_mai"] = "护脉丹",
		["xuanjian.alchemy.pill.qing_shen"] = "清神丹",
		["xuanjian.alchemy.pill.tian_yi_tu_cui"] = "天一吐萃丹",
		["xuanjian.alchemy.pill.cheng_jin"] = "承金丹",
		["xuanjian.alchemy.pill.bao_yi_wen_jin"] = "抱一温金丹",
		["xuanjian.alchemy.pill.she_yuan.desc"] = "胎息修士服用后增加500点真元，每3年最多服用一次。",
		["xuanjian.alchemy.pill.yang_yuan.desc"] = "胎息、炼气修士受伤时服用，恢复最大生命的25%，每1年最多服用一次。",
		["xuanjian.alchemy.pill.sui_yuan.desc"] = "筑基修士突破紫府时自动服用，使本次突破成功率+8%，每8年最多服用一次。",
		["xuanjian.alchemy.pill.yu_ya.desc"] = "炼气修士服用后增加1000点真元，每2年最多服用一次。",
		["xuanjian.alchemy.pill.hui_qiu.desc"] = "筑基修士服用后增加2000点真元，每5年最多服用一次。",
		["xuanjian.alchemy.pill.hu_mai.desc"] = "筑基修士受伤时服用，恢复最大生命的35%，每2年最多服用一次。",
		["xuanjian.alchemy.pill.qing_shen.desc"] = "紫府修士领悟下一门神通时自动服用，使本次领悟成功率+12%，每2年最多服用一次。",
		["xuanjian.alchemy.pill.tian_yi_tu_cui.desc"] = "紫府或金丹修士受伤时服用，恢复最大生命的60%，每8年最多服用一次。",
		["xuanjian.alchemy.recipe.tian_yi_tu_cui"] = "天一吐萃丹方",
		["xuanjian.alchemy.pill.cheng_jin.desc"] = "紫府修士突破金丹时自动服用，使本次求金成功率+5%，每20年最多服用一次。",
		["xuanjian.alchemy.pill.bao_yi_wen_jin.desc"] = "服气真人神妙圆满且正在温养金性时自动服用，使当前温养工程缩短5年，每20年最多服用一次。",
		["xuanjian.alchemy.pill.bao_yi_wen_jin.short"] = "服气真人金性温养工程缩短5年。",
		["xuanjian.alchemy.pill.san_quan"] = "三全破境灵丹",
		["xuanjian.alchemy.pill.san_quan.desc"] = "炼气或筑基修士冲击本境内部关隘时自动服用，使本次冲关成功率+6%；不替代大境突破丹，每8年最多服用一次。",
		["xuanjian.alchemy.pill.nan_gong_xuan_sui"] = "南宫玄绥丹",
		["xuanjian.alchemy.pill.nan_gong_xuan_sui.desc"] = "紫府修士服用后在体内开辟玄宫，药效分10年炼化，每年增加600点真元，冷却10年。",
		["xuanjian.alchemy.pill.cun_shen_yang_shen"] = "存神养身丹",
		["xuanjian.alchemy.pill.cun_shen_yang_shen.desc"] = "黄冠温养本命核心、修持神妙归身时自动服用，使当前养身工程缩短6年，每10年最多服用一次。",
		["xuanjian.alchemy.pill.cun_shen_yang_shen.short"] = "黄冠养身工程缩短6年。",
		["xuanjian.alchemy.pill.ming_zhen_he_shen"] = "明真合神丹",
		["xuanjian.alchemy.pill.ming_zhen_he_shen.desc"] = "紫府修士满足下一门神通领悟条件时自动服用，药效持续10年，使判定间隔由5年缩短为3年。",
		["xuanjian.alchemy.pill.xing_ming_he_zhen"] = "性命合真丹",
		["xuanjian.alchemy.pill.xing_ming_he_zhen.desc"] = "服气真人性命合炼、求取神妙圆满时自动服用，使当前圆满工程缩短8年，每20年最多服用一次。",
		["xuanjian.alchemy.pill.xing_ming_he_zhen.short"] = "服气真人圆满工程缩短8年。",
		["xuanjian.alchemy.pill.jiu_yao_yan_shou"] = "九曜延寿丹",
		["xuanjian.alchemy.pill.jiu_yao_yan_shou.desc"] = "紫府或金丹修士寿元将尽时自动服用，固定增寿500年；冷却100年，且每名角色一生只能服用一次。",
		["xuanjian.alchemy.pill.yu_lu_hui_ming"] = "玉露回命丹",
		["xuanjian.alchemy.pill.yu_lu_hui_ming.desc"] = "胎息至金丹修士陷入危急时可主动服用，恢复最大生命的60%；每5年最多服用一次。",
		["xuanjian.alchemy.pill.yu_lu_hui_ming.short"] = "危急时主动服用，恢复60%最大生命。",
		["xuanjian.alchemy.recipe.san_quan"] = "三全破境灵丹方",
		["xuanjian.alchemy.recipe.nan_gong_xuan_sui"] = "南宫玄绥丹方",
		["xuanjian.alchemy.recipe.cun_shen_yang_shen"] = "存神养身丹方",
		["xuanjian.alchemy.recipe.cun_shen_yang_shen.desc"] = "以紫穗兰、冰魄莲与金参炼制存神养身丹，只承接服气黄冠的神妙归身修持。",
		["xuanjian.alchemy.recipe.ming_zhen_he_shen"] = "明真合神丹方",
		["xuanjian.alchemy.recipe.xing_ming_he_zhen"] = "性命合真丹方",
		["xuanjian.alchemy.recipe.xing_ming_he_zhen.desc"] = "调和寒艾、玄阴藤、碧木脂与紫月兰炼制性命合真丹，只承接服气真人的性命合炼。",
		["xuanjian.alchemy.recipe.bao_yi_wen_jin"] = "抱一温金丹方",
		["xuanjian.alchemy.recipe.bao_yi_wen_jin.desc"] = "以玉髓竹与紫月兰炼制抱一温金丹，只用于服气真人神妙圆满后的金性温养。",
		["xuanjian.alchemy.recipe.jiu_yao_yan_shou"] = "九曜延寿丹方",
		["xuanjian.alchemy.recipe.jiu_yao_yan_shou.desc"] = "九种上品药材各九份与一缕金丹金性合炼，一炉只成一枚九曜延寿丹；基础成丹率20%。",
		["xuanjian.alchemy.recipe.yu_lu_hui_ming"] = "玉露回命丹方",
		["xuanjian.alchemy.recipe.yu_lu_hui_ming.desc"] = "汇合八味下品调和药与七味上品生机药，炼成可在战中救急的玉露回命丹。",
	};

	internal static string Resolve(string key, string defaultValue = "")
	{
		if (string.IsNullOrWhiteSpace(key)) return defaultValue ?? string.Empty;
		try
		{
			string localized = LM.Get(key);
			if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
				return localized;
		}
		catch (System.Exception xjCaught148) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Data/Alchemy/XjAlchemyText.cs:148", xjCaught148); }
		if (fallback.TryGetValue(key, out string value)) return value;
		return !string.IsNullOrWhiteSpace(defaultValue) ? defaultValue : string.Empty;
	}

	internal static string MaterialName(string materialId)
	{
		return XjAlchemyCatalog.TryGetMaterial(materialId, out XjAlchemyMaterialDef definition)
			? Resolve(definition.NameKey, "未名药草")
			: "未名药草";
	}

	internal static string MaterialDescription(string materialId)
	{
		return XjAlchemyCatalog.TryGetMaterial(materialId, out XjAlchemyMaterialDef definition)
			? Resolve(definition.DescriptionKey, "可用于炼丹的药草。")
			: "可用于炼丹的药草。";
	}

	internal static string PillName(string pillId)
	{
		return XjAlchemyCatalog.TryGetPill(pillId, out XjAlchemyPillDef definition)
			? Resolve(definition.NameKey, "未名丹药")
			: "未名丹药";
	}

	internal static string PillDescription(string pillId)
	{
		return XjAlchemyCatalog.TryGetPill(pillId, out XjAlchemyPillDef definition)
			? Resolve(definition.DescriptionKey, "封存在玉瓶中的修行丹药。")
			: "封存在玉瓶中的修行丹药。";
	}

	internal static string PillShortDescription(string pillId)
	{
		if (!XjAlchemyCatalog.TryGetPill(pillId, out XjAlchemyPillDef definition))
			return "服用后产生对应丹药效果。";
		string fallbackText = PillDescription(pillId);
		return Resolve(definition.ShortDescriptionKey, fallbackText);
	}

	internal static string RecipeName(string recipeId)
	{
		if (!XjAlchemyCatalog.TryGetRecipe(recipeId, out XjAlchemyRecipeDef definition)) return "未名丹方";
		return Resolve(definition.NameKey, PillName(definition.PillId) + "方");
	}

	internal static string RecipeDescription(string recipeId)
	{
		if (!XjAlchemyCatalog.TryGetRecipe(recipeId, out XjAlchemyRecipeDef definition)) return "记载药材、火候与收丹之法。";
		return Resolve(definition.DescriptionKey,
			"以" + FormatRecipeMaterials(definition) + "炼制" + PillName(definition.PillId) + "的固定丹方。");
	}

	internal static string FormatRecipeMaterials(XjAlchemyRecipeDef definition)
	{
		if (definition?.Ingredients == null || definition.Ingredients.Count == 0) return "未载药材";
		List<string> parts = new(definition.Ingredients.Count);
		for (int i = 0; i < definition.Ingredients.Count; i++)
		{
			XjAlchemyIngredient ingredient = definition.Ingredients[i];
			if (ingredient.Count <= 0) continue;
			parts.Add(MaterialName(ingredient.MaterialId) + "×" + ingredient.Count);
		}
		return parts.Count == 0 ? "未载药材" : string.Join("，", parts);
	}
}

