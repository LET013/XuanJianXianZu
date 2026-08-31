using System;
using System.Collections.Generic;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 家族象征物只从现有真实仓库派生，不创建第二份物品状态。
/// 仅供家族详情与百年世谱读取。
/// </summary>
internal static class XjFamilyHeritageProjection
{
	internal static string ResolveClanTreasure(long familyStableId)
	{
		if (familyStableId <= 0L)
		{
			return string.Empty;
		}

		IReadOnlyList<XjFamilyFaBaoWarehouseEntry> faBaoEntries = XjFamilyFaBaoWarehouse.ReadFamilyEntriesView(familyStableId);
		XjFamilyFaBaoWarehouseEntry bestFaBao = default;
		int bestFaBaoRank = 0;
		for (int i = 0; i < faBaoEntries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry candidate = faBaoEntries[i];
			if (!candidate.Found || string.IsNullOrWhiteSpace(candidate.FaBaoName)) continue;
			int rank = ResolveArtifactRank(candidate.ClassName);
			if (!bestFaBao.Found
				|| rank > bestFaBaoRank
				|| rank == bestFaBaoRank && candidate.Year > bestFaBao.Year
				|| rank == bestFaBaoRank && candidate.Year == bestFaBao.Year
					&& string.CompareOrdinal(candidate.FaBaoName, bestFaBao.FaBaoName) < 0)
			{
				bestFaBao = candidate;
				bestFaBaoRank = rank;
			}
		}
		if (bestFaBao.Found)
		{
			string className = string.IsNullOrWhiteSpace(bestFaBao.ClassName) ? "传承重宝" : bestFaBao.ClassName.Trim();
			return bestFaBao.FaBaoName.Trim() + "（" + className + "）";
		}

		return string.Empty;
	}

	internal static string ResolveSectTreasure(long sectId)
	{
		if (sectId <= 0L) return string.Empty;
		IReadOnlyList<XjFamilyFaBaoWarehouseEntry> entries = XjFamilyFaBaoWarehouse.ReadSectEntriesView(sectId);
		XjFamilyFaBaoWarehouseEntry best = default;
		int bestRank = 0;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry candidate = entries[i];
			if (!candidate.Found || string.IsNullOrWhiteSpace(candidate.FaBaoName)) continue;
			int rank = ResolveArtifactRank(candidate.ClassName);
			if (!best.Found
				|| rank > bestRank
				|| rank == bestRank && candidate.Year > best.Year
				|| rank == bestRank && candidate.Year == best.Year
					&& string.CompareOrdinal(candidate.FaBaoName, best.FaBaoName) < 0)
			{
				best = candidate;
				bestRank = rank;
			}
		}
		if (!best.Found) return string.Empty;
		string className = string.IsNullOrWhiteSpace(best.ClassName) ? "传承重宝" : best.ClassName.Trim();
		return best.FaBaoName.Trim() + "（" + className + "）";
	}

	private static int ResolveArtifactRank(string className)
	{
		string value = className?.Trim() ?? string.Empty;
		if (value.IndexOf("法宝", StringComparison.Ordinal) >= 0) return 4;
		if (value.IndexOf("灵宝", StringComparison.Ordinal) >= 0) return 3;
		if (value.IndexOf("法器", StringComparison.Ordinal) >= 0) return 2;
		return 1;
	}

}
