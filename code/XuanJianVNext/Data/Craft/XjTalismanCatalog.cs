using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Data.Craft;

internal sealed class XjTalismanDefinition
{
	internal XjTalismanDefinition(string id, string displayName, string iconPath, string minRealmId, int durationYears,
		float baseSuccessRate, int minYield, int maxYield, string paperId, int paperCount,
		string inkId, int inkCount, int difficulty)
	{
		Id = id ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		IconPath = iconPath ?? string.Empty;
		MinRealmId = minRealmId ?? string.Empty;
		DurationYears = Math.Max(1, durationYears);
		BaseSuccessRate = Math.Clamp(baseSuccessRate, 0.05f, 0.95f);
		MinYield = Math.Max(1, minYield);
		MaxYield = Math.Max(MinYield, maxYield);
		PaperId = paperId ?? string.Empty;
		PaperCount = Math.Max(1, paperCount);
		InkId = inkId ?? string.Empty;
		InkCount = Math.Max(1, inkCount);
		Difficulty = Math.Max(1, difficulty);
	}

	internal string Id { get; }
	internal string DisplayName { get; }
	internal string IconPath { get; }
	internal string MinRealmId { get; }
	internal int DurationYears { get; }
	internal float BaseSuccessRate { get; }
	internal int MinYield { get; }
	internal int MaxYield { get; }
	internal string PaperId { get; }
	internal int PaperCount { get; }
	internal string InkId { get; }
	internal int InkCount { get; }
	internal int Difficulty { get; }
}

internal static class XjTalismanCatalog
{
	internal const string LowPaper = "xj_talisman_paper_low";
	internal const string HighPaper = "xj_talisman_paper_high";
	internal const string LowInk = "xj_talisman_ink_low";
	internal const string HighInk = "xj_talisman_ink_high";
	internal const string BrushDurability = "xj_talisman_brush_durability";

	internal const string Protection = "xj_talisman_protection";
	internal const string Swift = "xj_talisman_swift";
	internal const string BreakFormation = "xj_talisman_break_formation";
	internal const string BreakthroughAid = "xj_talisman_breakthrough_aid";
	internal const string CalmSpirit = "xj_talisman_calm_spirit";

	internal const string ProtectionIconPath = "GameResources/item/Arts/fulu/FuLu-HuShen.png";
	internal const string SwiftIconPath = "GameResources/item/Arts/fulu/FuLu-ShenXing.png";
	internal const string BreakFormationIconPath = "GameResources/item/Arts/fulu/FuLu-PoZhen.png";
	internal const string BreakthroughAidIconPath = "GameResources/item/Arts/fulu/FuLu-PoZhang.png";
	internal const string CalmSpiritIconPath = "GameResources/item/Arts/fulu/FuLu-ZhenShen.png";

	private static readonly IReadOnlyList<XjTalismanDefinition> AllDefinitions = new[]
	{
		new XjTalismanDefinition(Protection, "护身符", ProtectionIconPath, XjRealmIds.LianQi, 2, 0.72f, 1, 3, LowPaper, 2, LowInk, 1, 1),
		new XjTalismanDefinition(Swift, "神行符", SwiftIconPath, XjRealmIds.LianQi, 2, 0.68f, 1, 3, LowPaper, 2, LowInk, 1, 2),
		new XjTalismanDefinition(BreakFormation, "破阵符", BreakFormationIconPath, XjRealmIds.ZhuJi, 4, 0.48f, 1, 2, HighPaper, 2, HighInk, 2, 5),
		new XjTalismanDefinition(BreakthroughAid, "破障符", BreakthroughAidIconPath, XjRealmIds.ZhuJi, 3, 0.46f, 1, 2, HighPaper, 2, HighInk, 1, 4),
		new XjTalismanDefinition(CalmSpirit, "镇神符", CalmSpiritIconPath, XjRealmIds.ZiFu, 4, 0.42f, 1, 2, HighPaper, 2, HighInk, 2, 5)
	};

	internal static IReadOnlyList<XjTalismanDefinition> All => AllDefinitions;

	internal static bool TryGet(string id, out XjTalismanDefinition definition)
	{
		for (int i = 0; i < AllDefinitions.Count; i++)
		{
			if (string.Equals(AllDefinitions[i].Id, id, StringComparison.Ordinal))
			{
				definition = AllDefinitions[i];
				return true;
			}
		}
		definition = null;
		return false;
	}

	internal static string EffectSummary(string id)
	{
		if (string.Equals(id, Protection, StringComparison.Ordinal)) return "受伤遇险时护身，降低身损风险。";
		if (string.Equals(id, Swift, StringComparison.Ordinal)) return "远行与任务支援更轻快。";
		if (string.Equals(id, BreakFormation, StringComparison.Ordinal)) return "攻伐或破阵时辅助破开阵势。";
		if (string.Equals(id, BreakthroughAid, StringComparison.Ordinal)) return "突破瓶颈时护持气机。";
		if (string.Equals(id, CalmSpirit, StringComparison.Ordinal)) return "镇定神意，压制高境杂念。";
		return "随身携带，在对应判定中消耗。";
	}
}
