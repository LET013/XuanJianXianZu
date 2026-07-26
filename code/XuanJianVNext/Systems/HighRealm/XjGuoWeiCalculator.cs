using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;
internal static class XjGuoWeiCalculator
{
	internal const string ZhengWei = "正位";
	internal const string RunWei = "闰位";
	internal const string YuWei = "余位";
	internal const string NoDoor = "无门";

	internal static string Calculate(string daoTu, in XjXianJiState xianJiState)
	{
		if (xianJiState.Count < XjXianJiState.MaxCount || xianJiState.Ids == null)
		{
			return NoDoor;
		}

		int nativeCount = 0;
		int lowerCount = 0;
		int adjacentCount = 0;
		int otherCount = 0;
		for (int i = 0; i < xianJiState.Ids.Length && i < XjXianJiState.MaxCount; i++)
		{
			switch (XjXianJiCatalog.GetPoolKind(daoTu, xianJiState.Ids[i]))
			{
			case XjXianJiPoolKind.Native:
				nativeCount++;
				break;
			case XjXianJiPoolKind.Lower:
				lowerCount++;
				break;
			case XjXianJiPoolKind.Adjacent:
				adjacentCount++;
				break;
			default:
				otherCount++;
				break;
			}
		}

		if (otherCount > 0)
		{
			return NoDoor;
		}
		if (string.Equals(NormalizeDaoTu(daoTu), "青宣", StringComparison.Ordinal))
		{
			if (nativeCount >= 5 && lowerCount == 0 && adjacentCount == 0)
			{
				return ZhengWei;
			}
			if (nativeCount >= 4 && lowerCount == 1 && adjacentCount == 0)
			{
				return YuWei;
			}
			return NoDoor;
		}

		if (nativeCount >= 5 && lowerCount == 0 && adjacentCount == 0)
		{
			return ZhengWei;
		}

		if (adjacentCount > 0 && nativeCount >= 2)
		{
			return RunWei;
		}

		if (nativeCount >= 3 && lowerCount >= 2 && adjacentCount == 0)
		{
			return YuWei;
		}

		return NoDoor;
	}

	internal static string ResolveManifestDaoTu(string daoTu, in XjXianJiState xianJiState, string guoWeiType)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return string.Empty;
		}

		if (!string.Equals(guoWeiType, RunWei, System.StringComparison.Ordinal)
			|| xianJiState.Ids == null)
		{
			return normalizedDaoTu;
		}

		for (int i = 0; i < xianJiState.Ids.Length && i < XjXianJiState.MaxCount; i++)
		{
			if (XjXianJiCatalog.TryGetAdjacentDaoTuForXianJi(normalizedDaoTu, xianJiState.Ids[i], out string adjacentDaoTu)
				&& !string.IsNullOrWhiteSpace(adjacentDaoTu))
			{
				return adjacentDaoTu;
			}
		}

		return normalizedDaoTu;
	}

	internal static string GenerateGuoWeiName(string daoTu, string guoWeiType, long seed)
	{
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(guoWeiType) || string.Equals(guoWeiType, NoDoor, System.StringComparison.Ordinal))
		{
			return NoDoor;
		}
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return NoDoor;
		}
		if (!XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalizedDaoTu, out _))
		{
			return NoDoor;
		}

		int maxSlot = ResolveGuoWeiSlotCount(guoWeiType);
		int slotIndex = maxSlot <= 1 ? 1 : XjDeterministicHash.PositiveIndex(seed, normalizedDaoTu + "|" + guoWeiType, maxSlot) + 1;
		return BuildGuoWeiSlotName(normalizedDaoTu, guoWeiType, slotIndex);
	}

	private static int ResolveGuoWeiSlotCount(string guoWeiType)
	{
		if (string.Equals(guoWeiType, ZhengWei, System.StringComparison.Ordinal))
		{
			return 1;
		}
		if (string.Equals(guoWeiType, YuWei, System.StringComparison.Ordinal))
		{
			return XjGuoWeiQuanBingRules.YuWeiSlotCount;
		}
		if (string.Equals(guoWeiType, RunWei, System.StringComparison.Ordinal))
		{
			return XjGuoWeiQuanBingRules.RunWeiSlotCount;
		}
		return 1;
	}

	internal static string BuildGuoWeiSlotName(string daoTu, string guoWeiType, int slotIndex)
	{
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		string normalizedType = string.IsNullOrWhiteSpace(guoWeiType) ? NoDoor : guoWeiType.Trim();
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return NoDoor;
		}

		if (slotIndex <= 1)
		{
			return normalizedDaoTu + normalizedType;
		}

		return normalizedDaoTu + ToChineseNumber(slotIndex) + normalizedType;
	}

	internal static string GetDisplayGuoWeiName(string guoWeiName)
	{
		if (string.IsNullOrWhiteSpace(guoWeiName))
		{
			return string.Empty;
		}

		if (guoWeiName.EndsWith(RunWei, System.StringComparison.Ordinal))
		{
			return StripGuoWeiDisplayOrdinal(guoWeiName, RunWei);
		}

		if (guoWeiName.EndsWith(YuWei, System.StringComparison.Ordinal))
		{
			return StripGuoWeiDisplayOrdinal(guoWeiName, YuWei);
		}

		return guoWeiName;
	}

	private static string NormalizeDaoTu(string daoTu)
	{
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		return !string.IsNullOrWhiteSpace(normalizedDaoTu) && XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalizedDaoTu, out _)
			? normalizedDaoTu
			: string.Empty;
	}

	private static string StripGuoWeiDisplayOrdinal(string guoWeiName, string weiType)
	{
		int suffixStart = guoWeiName.Length - weiType.Length;
		int ordinalStart = suffixStart;
		while (ordinalStart > 0 && IsGuoWeiOrdinalChar(guoWeiName[ordinalStart - 1]))
		{
			ordinalStart--;
		}

		return ordinalStart == suffixStart
			? guoWeiName
			: guoWeiName.Substring(0, ordinalStart) + weiType;
	}

	private static bool IsGuoWeiOrdinalChar(char c)
	{
		return char.IsDigit(c)
			|| c == '零'
			|| c == '一'
			|| c == '二'
			|| c == '三'
			|| c == '四'
			|| c == '五'
			|| c == '六'
			|| c == '七'
			|| c == '八'
			|| c == '九'
			|| c == '十';
	}

	private static string ToChineseNumber(int number)
	{
		return number switch
		{
			2 => "二",
			3 => "三",
			4 => "四",
			5 => "五",
			6 => "六",
			7 => "七",
			8 => "八",
			9 => "九",
			10 => "十",
			_ => number.ToString()
		};
	}
}

