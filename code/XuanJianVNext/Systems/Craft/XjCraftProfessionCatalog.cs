using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.Craft;

internal readonly struct XjCraftProfessionDefinition
{
	internal XjCraftProfessionDefinition(string traitId, string displayName, int selectionWeight, string proficiencyKey, string rankKey)
	{
		TraitId = traitId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		SelectionWeight = Math.Max(0, selectionWeight);
		ProficiencyKey = proficiencyKey ?? string.Empty;
		RankKey = rankKey ?? string.Empty;
	}

	internal string TraitId { get; }
	internal string DisplayName { get; }
	internal int SelectionWeight { get; }
	internal string ProficiencyKey { get; }
	internal string RankKey { get; }
}

internal static class XjCraftProfessionCatalog
{
	internal static readonly XjCraftProfessionDefinition Alchemy = new XjCraftProfessionDefinition(XjCraftTraitRules.AlchemyTraitId, "炼丹", 25, string.Empty, XjActorDataKeys.XjCraftAlchemyRank);
	internal static readonly XjCraftProfessionDefinition Artifact = new XjCraftProfessionDefinition(XjCraftTraitRules.ArtifactRefiningTraitId, "炼器", 25, string.Empty, XjActorDataKeys.XjArtifactRefinerRank);
	internal static readonly XjCraftProfessionDefinition Talisman = new XjCraftProfessionDefinition(XjCraftTraitRules.TalismanTraitId, "符箓", 25, XjActorDataKeys.XjTalismanProficiency, XjActorDataKeys.XjTalismanRank);
	internal static readonly XjCraftProfessionDefinition Formation = new XjCraftProfessionDefinition(XjCraftTraitRules.FormationTraitId, "阵法", 25, XjActorDataKeys.XjFormationProficiency, XjActorDataKeys.XjFormationRank);

	internal static readonly IReadOnlyList<XjCraftProfessionDefinition> All = new[] { Alchemy, Artifact, Talisman, Formation };

	internal static bool TryGet(string traitId, out XjCraftProfessionDefinition definition)
	{
		for (int i = 0; i < All.Count; i++)
		{
			if (string.Equals(All[i].TraitId, traitId, StringComparison.Ordinal))
			{
				definition = All[i];
				return true;
			}
		}
		definition = default;
		return false;
	}
}
