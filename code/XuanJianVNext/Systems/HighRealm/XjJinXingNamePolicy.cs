using System;
using System.Collections.Generic;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjJinXingNamePolicy
{
	internal const string CanonicalDuiJinFruitName = "申白兑金上酉性";

	private static readonly string[] ResidualHeads = { "含章", "玄津", "复命", "青华", "白藏", "太素" };
	private static readonly string[] ResidualTails = { "藏真", "养素", "回元", "承命", "返照", "摄神" };
	private static readonly string[] IntercalaryHeads = { "太冲", "玉枢", "紫微", "洞阳", "玄置", "旁通" };
	private static readonly string[] IntercalaryTails = { "转枢", "间行", "借化", "越序", "合机", "易象", "反证", "通玄", "迁位" };

	internal static string NormalizeLegacyName(string value)
	{
		string normalized = (value ?? string.Empty).Trim();
		return IsRemovedLegacyName(normalized)
			? CanonicalDuiJinFruitName
			: normalized;
	}

	internal static bool IsRemovedLegacyName(string value)
	{
		// 仅用于无扫描地兼容极少数旧档。已废名称不再作为完整词条、别名或历史名保留。
		return string.Equals((value ?? string.Empty).Trim(), string.Concat("金一", "太元", "上青性"), StringComparison.Ordinal);
	}

	internal static string NormalizeOrBuild(
		string daoTu,
		string positionType,
		string existing,
		string primaryAuthority,
		string externalDaoTu,
		int slot,
		long seed)
	{
		string normalizedExisting = NormalizeLegacyName(existing);
		if (string.Equals(positionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			&& string.Equals((daoTu ?? string.Empty).Trim(), "兑金", StringComparison.Ordinal))
		{
			return CanonicalDuiJinFruitName;
		}
		if (string.Equals(positionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			if (!string.IsNullOrWhiteSpace(normalizedExisting)
				&& !XjJinXingCalculator.NeedsDaoTuNormalization(normalizedExisting, daoTu)
				&& !IsLegacyPositionSyntheticName(normalizedExisting, daoTu))
			{
				return normalizedExisting;
			}
			string zhengStem = XjRealmTitleNameLibrary.GeneratePositionJinXingStem(
				daoTu, positionType, primaryAuthority, externalDaoTu, slot, seed);
			return string.IsNullOrWhiteSpace(zhengStem) ? normalizedExisting : zhengStem + "性";
		}
		if (string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			&& normalizedExisting.Contains("闰", StringComparison.Ordinal)
			&& normalizedExisting.EndsWith("性", StringComparison.Ordinal)
			&& !IsLegacyPositionSyntheticName(normalizedExisting, daoTu))
		{
			// 新版闰位金性承载根道、尊号与真名，必须由证道者个人保存，
			// 不能再被果位槽位的通用派生名覆盖。
			return normalizedExisting;
		}
		// 余、闰位仍需自己的稳定名字，但三段全取本道途词池：
		// “桂魄太阴清辉性”而非“太冲太阴转枢性”。
		string stem = XjRealmTitleNameLibrary.GeneratePositionJinXingStem(
			daoTu, positionType, primaryAuthority, externalDaoTu, slot, seed);
		return string.IsNullOrWhiteSpace(stem) ? normalizedExisting : stem + "性";
	}

	internal static bool IsLegacyPositionSyntheticName(string value, string daoTu)
	{
		string text = (value ?? string.Empty).Trim();
		if (!text.EndsWith("性", StringComparison.Ordinal)) return false;
		string body = ResolveDaoBody(daoTu, string.Empty, string.Empty, residual: false);
		if (body.Length == 0) return false;
		return IsLegacyPositionSyntheticName(text, body, ResidualHeads, ResidualTails)
			|| IsLegacyPositionSyntheticName(text, body, IntercalaryHeads, IntercalaryTails);
	}

	private static bool IsLegacyPositionSyntheticName(string text, string body, string[] heads, string[] tails)
	{
		for (int i = 0; i < heads.Length; i++)
		{
			if (!text.StartsWith(heads[i], StringComparison.Ordinal)) continue;
			string remainder = text.Substring(heads[i].Length);
			if (remainder.Length <= body.Length + 1) continue;
			if (!remainder.StartsWith(body, StringComparison.Ordinal)) continue;
			string tail = remainder.Substring(body.Length, remainder.Length - body.Length - 1);
			for (int j = 0; j < tails.Length; j++)
			{
				if (string.Equals(tail, tails[j], StringComparison.Ordinal)) return true;
			}
		}
		return false;
	}

	private static string ResolveDaoBody(string daoTu, string primaryAuthority, string externalDaoTu, bool residual)
	{
		_ = residual;
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(normalizedDaoTu)) return TakeTwo(normalizedDaoTu);
		if (!string.IsNullOrWhiteSpace(externalDaoTu)) return TakeTwo(externalDaoTu);
		return TakeTwo(primaryAuthority);
	}

	private static string TakeTwo(string value)
	{
		string normalized = (value ?? string.Empty).Trim();
		return normalized.Length <= 2 ? normalized : normalized.Substring(0, 2);
	}

}
