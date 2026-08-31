using System;

namespace XuanJianVNext.Data.LingWu;

internal sealed class XjFamilyTreasureDef
{
	internal string Id { get; }
	internal string Category { get; }
	internal string DaoTuGroup { get; }
	internal string Name { get; }
	internal string Description { get; }
	internal string IconPath { get; }
	internal string Unit { get; }
	internal string SourceLabel { get; }

	internal XjFamilyTreasureDef(
		string id,
		string category,
		string daoTuGroup,
		string name,
		string description,
		string iconPath,
		string unit,
		string sourceLabel)
	{
		Id = id ?? string.Empty;
		Category = category ?? string.Empty;
		DaoTuGroup = daoTuGroup ?? string.Empty;
		Name = name ?? string.Empty;
		Description = description ?? string.Empty;
		IconPath = iconPath ?? string.Empty;
		Unit = unit ?? string.Empty;
		SourceLabel = sourceLabel ?? string.Empty;
	}
}

internal static class XjFamilyTreasureCatalog
{
	private const string JinXingPrefix = "xj_jindan_residual_jinxing:";
	internal const string JinXingIconPath = "GameResources/trait/JinDanJinXing.png";

	internal static string BuildJinXingTreasureId(string jinXing)
	{
		string normalized = NormalizeJinXing(jinXing);
		return normalized.Length == 0 ? string.Empty : JinXingPrefix + normalized;
	}

	internal static bool TryGetJinXingValue(string treasureId, out string jinXing)
	{
		jinXing = string.Empty;
		string id = (treasureId ?? string.Empty).Trim();
		if (!id.StartsWith(JinXingPrefix, StringComparison.Ordinal) || id.Length <= JinXingPrefix.Length)
		{
			return false;
		}

		jinXing = NormalizeJinXing(id.Substring(JinXingPrefix.Length));
		return jinXing.Length > 0;
	}

	internal static bool TryResolve(string treasureId, out XjFamilyTreasureDef definition)
	{
		definition = null;
		string id = (treasureId ?? string.Empty).Trim();
		if (XjLingWuCatalog.TryGet(id, out XjLingWuDef lingWu))
		{
			definition = new XjFamilyTreasureDef(
				lingWu.Id,
				"灵物",
				lingWu.DaoTuGroup,
				lingWu.Name,
				lingWu.Description,
				lingWu.IconPath,
				"件",
				"最近化生者");
			return true;
		}

		if (!TryGetJinXingValue(id, out string jinXing))
		{
			return false;
		}

		string daoTu = ResolveDaoTuDisplay(jinXing);
		definition = new XjFamilyTreasureDef(
			id,
			"金丹遗留金性",
			daoTu,
			"金丹遗留金性·" + daoTu,
			"真君或结璘仙身故后仍未散尽的一点金性，按修为深浅凝成数缕，收存于家族重宝仓库。奇遇洞天所得金性同样归入此处。",
			JinXingIconPath,
			"缕",
			"最近遗留者");
		return true;
	}

	private static string NormalizeJinXing(string jinXing)
	{
		string value = (jinXing ?? string.Empty).Trim();
		return value.Length == 0 ? string.Empty : value;
	}

	private static string ResolveDaoTuDisplay(string jinXing)
	{
		string value = NormalizeJinXing(jinXing);
		if (value.EndsWith("金性", StringComparison.Ordinal) && value.Length > 2)
		{
			value = value.Substring(0, value.Length - 2);
		}

		return string.IsNullOrWhiteSpace(value) ? "未定" : value;
	}
}
