using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Alchemy;

internal enum XjAlchemyUiItemKind
{
	Material,
	Pill,
	Recipe
}

internal readonly struct XjAlchemyUiItem
{
	internal readonly XjAlchemyUiItemKind Kind;
	internal readonly string Id;
	internal readonly string DisplayName;
	internal readonly string IconPath;
	internal readonly int Count;
	internal readonly string Description;
	internal readonly string Details;

	internal XjAlchemyUiItem(XjAlchemyUiItemKind kind, string id, string displayName, string iconPath, int count, string description, string details)
	{
		Kind = kind;
		Id = id ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		IconPath = iconPath ?? string.Empty;
		Count = Math.Max(0, count);
		Description = description ?? string.Empty;
		Details = details ?? string.Empty;
	}
}

internal static class XjAlchemyUiModel
{
	internal static XjAlchemyInventorySnapshot ReadSnapshot(XjAlchemyOwnerKey owner, int currentYear)
	{
		if (currentYear > 0) XjAlchemyInventoryRegistry.PruneExpiredPills(owner, currentYear);
		return XjAlchemyInventoryRegistry.ReadSnapshot(owner);
	}

	internal static XjAlchemyUiItem[] BuildMaterials(XjAlchemyOwnerKey owner)
	{
		return BuildMaterials(ReadSnapshot(owner, 0));
	}

	internal static XjAlchemyUiItem[] BuildMaterials(XjAlchemyInventorySnapshot snapshot)
	{
		if (snapshot == null) return Array.Empty<XjAlchemyUiItem>();
		List<XjAlchemyUiItem> result = new();
		foreach (KeyValuePair<string, int> item in snapshot.Materials.OrderBy(value => value.Key, StringComparer.Ordinal))
		{
			if (item.Value <= 0 || !XjAlchemyCatalog.TryGetMaterial(item.Key, out XjAlchemyMaterialDef definition)) continue;
			string name = XjAlchemyText.MaterialName(item.Key);
			string description = XjAlchemyText.MaterialDescription(item.Key);
			string grade = definition.Grade == XjAlchemyMaterialGrade.High ? "上品药材" : "下品药材";
			result.Add(new XjAlchemyUiItem(XjAlchemyUiItemKind.Material, item.Key, name, definition.IconPath, item.Value, description, grade + "\n库存：" + item.Value + "份"));
		}
		return result.ToArray();
	}

	internal static XjAlchemyUiItem[] BuildPills(XjAlchemyOwnerKey owner, int currentYear)
	{
		return BuildPills(ReadSnapshot(owner, currentYear));
	}

	internal static XjAlchemyUiItem[] BuildPills(XjAlchemyInventorySnapshot snapshot)
	{
		if (snapshot == null) return Array.Empty<XjAlchemyUiItem>();
		List<XjAlchemyUiItem> result = new();
		foreach (IGrouping<string, XjAlchemyPillBatchState> group in snapshot.PillBatches
			.Where(batch => batch.Quantity > 0).GroupBy(batch => batch.PillId, StringComparer.Ordinal))
		{
			if (!XjAlchemyCatalog.TryGetPill(group.Key, out XjAlchemyPillDef pill) || !pill.ProductionEnabled) continue;
			int total = group.Sum(batch => batch.Quantity);
			int nearestExpiry = group.Min(batch => batch.ExpireYear);
			bool lastingEffect = pill.EffectType == XjAlchemyEffectType.ShenTongTrainingSpeed
				|| pill.EffectType == XjAlchemyEffectType.SustainedZhenYuan;
			bool immediateEffect = pill.EffectType == XjAlchemyEffectType.ZhenYuan
				|| pill.EffectType == XjAlchemyEffectType.HealingPercent
				|| pill.EffectType == XjAlchemyEffectType.LifespanYears;
			string effectMode = lastingEffect
				? (pill.EffectType == XjAlchemyEffectType.SustainedZhenYuan
					? "紫府修炼时自动服用，药效分十年逐年炼化"
					: "满足神通修炼条件时自动服用，药效持续十年")
				: immediateEffect
					? "即时生效；服丹动作持续6秒，不是属性持续时间"
					: "仅作用于下一次对应判定，判定时立即消耗";
			string details = "药效：" + XjAlchemyText.PillShortDescription(group.Key)
				+ "\n库存：" + total + "枚\n最近失效：玄鉴历" + nearestExpiry + "年\n生效方式：" + effectMode;
			if (pill.EffectType == XjAlchemyEffectType.ZhenYuan)
				details += "\n修炼丹共用冷却：3年";
			else if (pill.CooldownYears > 0)
				details += "\n独立冷却：" + pill.CooldownYears + "年";
			result.Add(new XjAlchemyUiItem(
				XjAlchemyUiItemKind.Pill,
				group.Key,
				XjAlchemyText.PillName(group.Key),
				pill.IconPath,
				total,
				XjAlchemyText.PillDescription(group.Key),
				details));
		}
		return result.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
	}

	internal static XjAlchemyUiItem[] BuildRecipes(XjAlchemyOwnerKey owner)
	{
		return BuildRecipes(ReadSnapshot(owner, 0));
	}

	internal static XjAlchemyUiItem[] BuildRecipes(XjAlchemyInventorySnapshot snapshot)
	{
		if (snapshot == null) return Array.Empty<XjAlchemyUiItem>();
		List<XjAlchemyUiItem> result = new();
		for (int i = 0; i < snapshot.Recipes.Count; i++)
		{
			XjAlchemyRecipeHolding holding = snapshot.Recipes[i];
			if (!XjAlchemyCatalog.TryGetRecipe(holding.RecipeId, out XjAlchemyRecipeDef recipe)) continue;
			string material = XjAlchemyText.FormatRecipeMaterials(recipe);
			string sourceText = XjDisplayNameSanitizer.EventSource(holding.Source);
			if (string.IsNullOrWhiteSpace(sourceText)) sourceText = "丹房收录";
			bool isJiuYao = string.Equals(holding.RecipeId, XjAlchemyCatalog.RecipeJiuYaoYanShou, StringComparison.Ordinal);
			string extraMaterial = isJiuYao ? "\n额外材料：金丹金性1缕" : string.Empty;
			string details = "来源：" + sourceText
				+ "\n撰录年份：玄鉴历" + holding.AcquiredYear + "年"
				+ "\n境界层次：" + FormatRealm(recipe.MinCrafterRealmId)
				+ "\n药材：" + material + extraMaterial
				+ "\n炼制时间：" + recipe.BaseDurationYears + "年"
				+ "\n基础成丹率：" + (int)Math.Round(recipe.BaseSuccessRate * 100f) + "%"
				+ "\n基础一炉产量：" + recipe.MinYield + (recipe.MaxYield > recipe.MinYield ? "-" + recipe.MaxYield : string.Empty) + "枚"
				+ (isJiuYao
					? "\n四艺品级与经验：提高成丹率；一炉恒定1枚"
					: "\n四艺品级与经验：提高成丹率和实际一炉产量")
				+ "\n成丹效果：" + XjAlchemyText.PillDescription(recipe.PillId);
			result.Add(new XjAlchemyUiItem(XjAlchemyUiItemKind.Recipe, holding.RecipeId,
				XjAlchemyText.RecipeName(holding.RecipeId), XjAlchemyCatalog.RecipeIconPath, 1,
				XjAlchemyText.RecipeDescription(holding.RecipeId), details));
		}
		return result.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
	}

	private static string FormatRealm(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return "金丹级";
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return "紫府级";
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return "筑基级";
		return "炼气级";
	}

	internal static void CreateIconCell(Transform parent, in XjAlchemyUiItem item, Font font, string objectName)
	{
		if (parent == null) return;
		Sprite sprite = XjIconLoader.TryLoadSpritePath(item.IconPath);
		if (sprite == null && !item.IconPath.StartsWith("GameResources/", StringComparison.OrdinalIgnoreCase))
			sprite = XjIconLoader.TryLoadSpritePath("GameResources/" + item.IconPath);

		// 0.6.3.2 预留统一丹方图。当前发布包不携带 DanFang.png，
		// 玩家放入资源后自动启用；缺图期间回退到对应丹药图，不出现空白格。
		if (sprite == null
			&& item.Kind == XjAlchemyUiItemKind.Recipe
			&& XjAlchemyCatalog.TryGetRecipe(item.Id, out XjAlchemyRecipeDef recipe)
			&& XjAlchemyCatalog.TryGetPill(recipe.PillId, out XjAlchemyPillDef pill))
		{
			sprite = XjIconLoader.TryLoadSpritePath(pill.IconPath);
		}
		string fallback = item.Kind == XjAlchemyUiItemKind.Material ? "药" : item.Kind == XjAlchemyUiItemKind.Pill ? "丹" : "方";
		XjIconShelfRenderer.CreateIconSlot(parent, objectName, sprite, fallback, Math.Max(1, item.Count),
			string.IsNullOrWhiteSpace(item.DisplayName) ? fallback : item.DisplayName,
			item.Description, item.Details, font,
			showCountWhenOne: item.Kind == XjAlchemyUiItemKind.Material);
	}

}
