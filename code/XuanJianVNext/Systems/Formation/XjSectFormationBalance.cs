using System;

namespace XuanJianVNext.Systems.Formation;

internal static class XjSectFormationBalance
{
	internal const float CompletionGate = 0.99f;
	internal const int MaximumContributors = 12;
	internal const int MaximumAssistantFormationMasters = 3;
	internal const int PopulationCollapseConquestThreshold = 100;
	internal const int FormationRoutPopulationThreshold = 200;

	internal static int ClampGrade(int grade) => Math.Clamp(grade, 1, 3);

	internal static int GetMaxDurability(int grade)
	{
		return ClampGrade(grade) switch
		{
			1 => 2000,
			2 => 6000,
			_ => 10000
		};
	}

	internal static int GetMaxDurability(int grade, int ziFuCount, int jinDanCount)
	{
		int baseDurability = GetMaxDurability(grade);
		double multiplier = 1d;
		int ziFu = Math.Max(0, ziFuCount);
		int jinDan = Math.Max(0, jinDanCount);
		if (ziFu >= 2) multiplier *= 1d + ziFu * 0.1d;
		if (jinDan >= 1) multiplier *= jinDan + 1d;
		double scaled = baseDurability * multiplier;
		if (scaled >= int.MaxValue) return int.MaxValue;
		return Math.Max(baseDurability, (int)Math.Round(scaled));
	}

	internal static float GetOccupationMultiplier(int grade)
	{
		return ClampGrade(grade) switch
		{
			1 => 0.75f,
			2 => 0.60f,
			_ => 0.45f
		};
	}

	internal static float GetGradeResistance(int grade)
	{
		return ClampGrade(grade) switch
		{
			1 => 1f,
			2 => 0.80f,
			_ => 0.65f
		};
	}
}
