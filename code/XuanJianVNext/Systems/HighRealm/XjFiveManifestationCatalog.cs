using System;
using System.Collections.Generic;

namespace XuanJianVNext.Systems.HighRealm;

internal enum XjFiveManifestationKind
{
	Unknown = 0,
	Cang = 1,
	Yun = 2,
	Zheng = 3,
	Bian = 4,
	Shou = 5
}

/// <summary>
/// 五现只描述道途的天然行法，不与“一道唯一果位”的位置类型混用。
/// 正位“不闰”、收位“不余”均落实为极高门槛，而非绝对禁止。
/// </summary>
internal static class XjFiveManifestationCatalog
{
	private static readonly Dictionary<string, XjFiveManifestationKind> Entries =
		new Dictionary<string, XjFiveManifestationKind>(StringComparer.Ordinal)
		{
			["库金"] = XjFiveManifestationKind.Cang,
			["逍金"] = XjFiveManifestationKind.Yun,
			["兑金"] = XjFiveManifestationKind.Zheng,
			["庚金"] = XjFiveManifestationKind.Bian,
			["齐金"] = XjFiveManifestationKind.Shou,

			["角木"] = XjFiveManifestationKind.Cang,
			["乙木"] = XjFiveManifestationKind.Cang,
			["保木"] = XjFiveManifestationKind.Yun,
			["正木"] = XjFiveManifestationKind.Zheng,
			["巽木"] = XjFiveManifestationKind.Zheng,
			["更木"] = XjFiveManifestationKind.Bian,
			["集木"] = XjFiveManifestationKind.Shou,

			["牝水"] = XjFiveManifestationKind.Cang,
			["府水"] = XjFiveManifestationKind.Yun,
			["弱水"] = XjFiveManifestationKind.Yun,
			["坎水"] = XjFiveManifestationKind.Zheng,
			["渌水"] = XjFiveManifestationKind.Bian,
			["合水"] = XjFiveManifestationKind.Shou,

			["牡火"] = XjFiveManifestationKind.Cang,
			["真火"] = XjFiveManifestationKind.Yun,
			["离火"] = XjFiveManifestationKind.Zheng,
			["灴火"] = XjFiveManifestationKind.Bian,
			["并火"] = XjFiveManifestationKind.Shou,

			["宝土"] = XjFiveManifestationKind.Cang,
			["戊土"] = XjFiveManifestationKind.Yun,
			["艮土"] = XjFiveManifestationKind.Zheng,
			["宣土"] = XjFiveManifestationKind.Bian,
			["社土"] = XjFiveManifestationKind.Bian,
			["归土"] = XjFiveManifestationKind.Shou,
			["稷土"] = XjFiveManifestationKind.Shou
		};

	internal static XjFiveManifestationKind Resolve(string daoTu)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		return normalized.Length > 0 && Entries.TryGetValue(normalized, out XjFiveManifestationKind value)
			? value
			: XjFiveManifestationKind.Unknown;
	}

	internal static bool IsDifficultPosition(string daoTu, string positionType)
	{
		XjFiveManifestationKind manifestation = Resolve(daoTu);
		return (manifestation == XjFiveManifestationKind.Zheng
				&& string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
			|| (manifestation == XjFiveManifestationKind.Shou
				&& string.Equals(positionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal));
	}

	internal static string GetDisplayName(XjFiveManifestationKind kind)
	{
		return kind switch
		{
			XjFiveManifestationKind.Cang => "藏位（蓄藏）",
			XjFiveManifestationKind.Yun => "蕴位（不拒）",
			XjFiveManifestationKind.Zheng => "正位（不闰）",
			XjFiveManifestationKind.Bian => "变位（不居）",
			XjFiveManifestationKind.Shou => "收位（不余）",
			_ => "未定"
		};
	}
}
