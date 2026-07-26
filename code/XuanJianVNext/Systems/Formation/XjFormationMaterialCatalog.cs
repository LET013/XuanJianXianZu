using System.Collections.Generic;
using XuanJianVNext.Systems.Craft;

namespace XuanJianVNext.Systems.Formation;

internal sealed class XjFormationProjectDefinition
{
	internal XjFormationProjectDefinition(int grade, string blueprintId, int durationYears, int repairDurationYears,
		int flagsLow, int flagsHigh, int platesLow, int platesHigh, int runes, int spiritMaterials)
	{
		Grade = grade;
		BlueprintId = blueprintId;
		DurationYears = durationYears;
		RepairDurationYears = repairDurationYears;
		List<(string ResourceId, int Amount)> costs = new List<(string ResourceId, int Amount)>();
		if (flagsLow > 0) costs.Add((XjCraftCollaborationSystem.FormationFlagLow, flagsLow));
		if (flagsHigh > 0) costs.Add((XjCraftCollaborationSystem.FormationFlagHigh, flagsHigh));
		if (platesLow > 0) costs.Add((XjCraftCollaborationSystem.FormationPlateLow, platesLow));
		if (platesHigh > 0) costs.Add((XjCraftCollaborationSystem.FormationPlateHigh, platesHigh));
		if (runes > 0) costs.Add((XjCraftCollaborationSystem.FormationRune, runes));
		if (spiritMaterials > 0) costs.Add((XjCraftCollaborationSystem.SpiritMaterial, spiritMaterials));
		Costs = costs;
	}

	internal int Grade { get; }
	internal string BlueprintId { get; }
	internal int DurationYears { get; }
	internal int RepairDurationYears { get; }
	internal IReadOnlyList<(string ResourceId, int Amount)> Costs { get; }
}

internal static class XjFormationMaterialCatalog
{
	private static readonly XjFormationProjectDefinition GradeOne = new XjFormationProjectDefinition(
		1, "xj_sect_formation_basic", 4, 2, 4, 0, 1, 0, 6, 3);
	private static readonly XjFormationProjectDefinition GradeTwo = new XjFormationProjectDefinition(
		2, "xj_sect_formation_advanced", 8, 3, 0, 8, 0, 2, 18, 12);
	private static readonly XjFormationProjectDefinition GradeThree = new XjFormationProjectDefinition(
		3, "xj_sect_formation_supreme", 14, 5, 0, 16, 0, 5, 32, 28);

	internal static XjFormationProjectDefinition Get(int grade)
	{
		return XjSectFormationBalance.ClampGrade(grade) switch
		{
			1 => GradeOne,
			2 => GradeTwo,
			_ => GradeThree
		};
	}

	internal static IReadOnlyList<(string ResourceId, int Amount)> BuildRepairCosts(int grade, int missingDurability, int maxDurability)
	{
		XjFormationProjectDefinition project = Get(grade);
		float ratio = maxDurability <= 0 ? 1f : System.Math.Clamp((float)missingDurability / maxDurability, 0.10f, 1f);
		List<(string ResourceId, int Amount)> result = new List<(string ResourceId, int Amount)>();
		for (int i = 0; i < project.Costs.Count; i++)
		{
			(string resourceId, int amount) = project.Costs[i];
			result.Add((resourceId, System.Math.Max(1, (int)System.Math.Ceiling(amount * ratio * 0.45f))));
		}
		return result;
	}
}
