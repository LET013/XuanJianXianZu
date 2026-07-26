using System;

namespace XuanJianVNext.Data.WeaponArt;

internal static class XjWeaponArtKinds
{
	internal const string Sword = "剑";
	internal const string Blade = "刀";
	internal const string Spear = "枪";
	internal const string Bow = "弓";
	// WorldBox 原生武器资产把刀与剑统一放在 sword_* 系列，无法从资产ID可靠区分。
	// 该值只用于识别原生装备候选，不会作为角色的终身器艺写入存档。
	internal const string NativeBladeSword = "刀剑";

	internal static readonly string[] All = { Sword, Blade, Spear, Bow };

	internal static bool IsSupported(string value)
	{
		string kind = (value ?? string.Empty).Trim();
		return string.Equals(kind, Sword, StringComparison.Ordinal)
			|| string.Equals(kind, Blade, StringComparison.Ordinal)
			|| string.Equals(kind, Spear, StringComparison.Ordinal)
			|| string.Equals(kind, Bow, StringComparison.Ordinal);
	}

	internal static bool IsEquipmentCandidate(string value)
	{
		string kind = (value ?? string.Empty).Trim();
		return IsSupported(kind) || string.Equals(kind, NativeBladeSword, StringComparison.Ordinal);
	}
}

internal static class XjWeaponArtRanks
{
	internal const int None = 0;
	internal const int Mang = 1;
	internal const int Qi = 2;
	internal const int Yuan = 3;
	internal const int Yi = 4;

	internal static int RequiredProficiency(int targetRank) => targetRank switch
	{
		Mang => 20,
		Qi => 60,
		Yuan => 120,
		Yi => 200,
		_ => int.MaxValue
	};

	internal static string Suffix(int rank) => rank switch
	{
		Mang => "芒",
		Qi => "气",
		Yuan => "元",
		Yi => "意",
		_ => "未入门"
	};
}

internal readonly struct XjWeaponArtState
{
	internal readonly bool Found;
	internal readonly string Kind;
	internal readonly int Rank;
	internal readonly int Proficiency;
	internal readonly int FailureCount;
	internal readonly int LastInsightYear;
	internal readonly int IntentYear;
	internal readonly string Alias;
	internal readonly string ManualId;
	internal readonly string ManualName;
	internal readonly int ManualGrade;

	internal XjWeaponArtState(
		bool found,
		string kind,
		int rank,
		int proficiency,
		int failureCount,
		int lastInsightYear,
		int intentYear,
		string alias,
		string manualId,
		string manualName,
		int manualGrade)
	{
		Found = found;
		Kind = kind ?? string.Empty;
		Rank = Math.Clamp(rank, XjWeaponArtRanks.None, XjWeaponArtRanks.Yi);
		Proficiency = Math.Max(0, proficiency);
		FailureCount = Math.Max(0, failureCount);
		LastInsightYear = Math.Max(0, lastInsightYear);
		IntentYear = Math.Max(0, intentYear);
		Alias = alias ?? string.Empty;
		ManualId = manualId ?? string.Empty;
		ManualName = manualName ?? string.Empty;
		ManualGrade = Math.Clamp(manualGrade, 0, 6);
	}
}
