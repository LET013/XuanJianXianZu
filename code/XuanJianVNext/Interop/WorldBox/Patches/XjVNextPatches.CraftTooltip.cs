using System;
using HarmonyLib;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Craft;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(TooltipLibrary), nameof(TooltipLibrary.showTrait))]
	[HarmonyPriority(Priority.Low)]
	private static void XuanJianVNext_TooltipLibrary_ShowTrait_Craft_Postfix(Tooltip pTooltip, TooltipData pData)
	{
		if (pTooltip?.stats_description == null || pTooltip.stats_values == null || pData?.trait == null) return;
		string traitId = pData.trait.id ?? string.Empty;
		if (!XjCraftTraitRules.IsCraftTraitId(traitId)) return;

		Actor actor = SelectedUnit.unit;
		if (!XjCraftProficiencySystem.TryBuildTooltipRows(actor, traitId, out string labels, out string values)) return;
		AppendRows(pTooltip.stats_description, labels);
		AppendRows(pTooltip.stats_values, values);
	}

	private static void AppendRows(UnityEngine.UI.Text text, string rows)
	{
		if (text == null || string.IsNullOrWhiteSpace(rows)) return;
		text.text = string.IsNullOrWhiteSpace(text.text) ? rows : text.text.TrimEnd() + "\n" + rows;
	}
}
