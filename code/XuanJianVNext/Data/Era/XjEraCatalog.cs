using System.Collections.Generic;
using XuanJianVNext.Data.Cultivation;

namespace XuanJianVNext.Data.Era;

internal readonly struct XjEraDefinition
{
	internal XjEraDefinition(
		string id,
		string iconPath,
		string backgroundPath,
		int rate,
		string titleColorHex,
		string lightColorHex,
		bool overlayMoon,
		bool overlaySun,
		bool globalUnfreezeWorld,
		int temperatureDamageBonus)
	{
		Id = id ?? string.Empty;
		IconPath = iconPath ?? string.Empty;
		BackgroundPath = backgroundPath ?? string.Empty;
		Rate = rate < 0 ? 0 : rate;
		TitleColorHex = titleColorHex ?? string.Empty;
		LightColorHex = lightColorHex ?? string.Empty;
		OverlayMoon = overlayMoon;
		OverlaySun = overlaySun;
		GlobalUnfreezeWorld = globalUnfreezeWorld;
		TemperatureDamageBonus = temperatureDamageBonus;
	}

	internal string Id { get; }
	internal string IconPath { get; }
	internal string BackgroundPath { get; }
	internal int Rate { get; }
	internal string TitleColorHex { get; }
	internal string LightColorHex { get; }
	internal bool OverlayMoon { get; }
	internal bool OverlaySun { get; }
	internal bool GlobalUnfreezeWorld { get; }
	internal int TemperatureDamageBonus { get; }
}

internal static class XjEraCatalog
{
	internal static readonly IReadOnlyList<XjEraDefinition> All = new[]
	{
		new XjEraDefinition("age_taiyin", "ui/Icons/ages/iconAgeTaiYin", "ui/Icons/ages/iconAgeTaiYin", 3, "#8DFFF3", "#8DFFF3", true, false, false, 0),
		new XjEraDefinition("age_taiyang", "ui/Icons/ages/iconAgeTaiYang", "ui/Icons/ages/iconAgeTaiYang", 3, "#FFD700", "#FFD700", false, true, true, 0),
		new XjEraDefinition("age_jinde", "ui/Icons/ages/iconAgeJinDe", "ui/Icons/ages/iconAgeJinDe", 3, "#FFD700", "#FFD700", false, false, true, 0),
		new XjEraDefinition("age_mude", "ui/Icons/ages/iconAgeMuDe", "ui/Icons/ages/iconAgeMuDe", 3, "#00FF00", "#00FF00", false, false, true, 0),
		new XjEraDefinition("age_shuide", "ui/Icons/ages/iconAgeShuiDe", "ui/Icons/ages/iconAgeShuiDe", 3, "#0000FF", "#0000FF", false, false, false, 0),
		new XjEraDefinition("age_huode", "ui/Icons/ages/iconAgeHuoDe", "ui/Icons/ages/iconAgeHuoDe", 3, "#FF0000", "#FF0000", false, false, true, 0),
		new XjEraDefinition("age_tude", "ui/Icons/ages/iconAgeTuDe", "ui/Icons/ages/iconAgeTuDe", 3, "#A0522D", "#A0522D", false, false, true, 0),
		new XjEraDefinition("age_qingqi", "ui/Icons/ages/iconAgeQingQi", "ui/Icons/ages/iconAgeQingQi", 3, "#F0F8FF", "#F0F8FF", false, false, true, 0),
		new XjEraDefinition("age_tianlei", "ui/Icons/ages/iconAgeTianLei", "ui/Icons/ages/iconAgeTianLei", 3, "#800080", "#800080", false, false, true, 0),
		// 并古纪元使用独立图标资源；资源可独立迭代，不与纪元注册逻辑耦合。
		new XjEraDefinition("age_binggu", "ui/Icons/ages/iconAgeBingGu", "ui/Icons/ages/iconAgeBingGu", 3, "#C6A6D8", "#B68BC8", false, false, true, 0)
	};

	internal static string GetDisplayName(string ageId)
	{
		return ageId switch
		{
			"age_taiyin" => "太阴纪元",
			"age_taiyang" => "太阳纪元",
			"age_jinde" => "金德纪元",
			"age_mude" => "木德纪元",
			"age_shuide" => "水德纪元",
			"age_huode" => "火德纪元",
			"age_tude" => "土德纪元",
			"age_qingqi" => "清炁纪元",
			"age_tianlei" => "天雷纪元",
			"age_binggu" => "并古纪元",
			_ => "未知纪元"
		};
	}

	internal static bool Contains(string ageId)
	{
		if (string.IsNullOrWhiteSpace(ageId))
		{
			return false;
		}

		for (int i = 0; i < All.Count; i++)
		{
			if (All[i].Id == ageId)
			{
				return true;
			}
		}

		return false;
	}

	internal static bool TryResolveAgeIdForDaoTu(string daoTuOrGroup, out string ageId)
	{
		ageId = string.Empty;
		string value = (daoTuOrGroup ?? string.Empty).Trim();
		if (value.Length == 0)
		{
			return false;
		}

		// 先走统一道途目录，保证中文名、根 ID、采气分支和道途特质 ID
		// 都归入同一纪元。并古九途不再借用五德纪元。
		if (XjDaoTuCatalog.TryResolve(value, out XjDaoTuDefinition definition))
		{
			ageId = definition.RootId switch
			{
				XjDaoTuRootIds.XiaoKui or
				XjDaoTuRootIds.ShangWu or
				XjDaoTuRootIds.YuZhen or
				XjDaoTuRootIds.HengZhu or
				XjDaoTuRootIds.QingXuan or
				XjDaoTuRootIds.QuanDan or
				XjDaoTuRootIds.ZhiBo or
				XjDaoTuRootIds.SiTian or
				XjDaoTuRootIds.DuWei => "age_binggu",
				XjDaoTuRootIds.SanYin => "age_taiyin",
				XjDaoTuRootIds.SanYang => "age_taiyang",
				XjDaoTuRootIds.SanLei => "age_tianlei",
				XjDaoTuRootIds.JinDe or XjDaoTuRootIds.LongGeng => "age_jinde",
				XjDaoTuRootIds.MuDe => "age_mude",
				XjDaoTuRootIds.ShuiDe => "age_shuide",
				XjDaoTuRootIds.HuoDe => "age_huode",
				XjDaoTuRootIds.TuDe => "age_tude",
				XjDaoTuRootIds.ShiErQi => "age_qingqi",
				_ => string.Empty
			};
			if (ageId.Length > 0)
			{
				return true;
			}
		}

		// 兼容洞天/旧档可能传入的道途组 ID，以及未纳入统一目录的历史别名。
		if (value is XjDaoTuGroupIds.BingGu or "XjBingGu" or "并古" or "并古九道" or "鸺葵" or "上巫" or "玉真" or "衡祝" or "青宣" or "全丹" or "执孛" or "司天" or "都卫") ageId = "age_binggu";
		else if (value is "三阴" or "太阴" or "少阴" or "厥阴") ageId = "age_taiyin";
		else if (value is "三阳" or "太阳" or "明阳" or "少阳") ageId = "age_taiyang";
		else if (value is "金德" or "兑金" or "逍金" or "库金" or "齐金" or "庚金" or "长庚") ageId = "age_jinde";
		else if (value is "木德" or "角木" or "正木" or "集木" or "更木" or "保木") ageId = "age_mude";
		else if (value is "水德" or "坎水" or "渌水" or "合水" or "府水" or "牝水") ageId = "age_shuide";
		else if (value is "火德" or "离火" or "灴火" or "并火" or "真火" or "牡火") ageId = "age_huode";
		else if (value is "土德" or "艮土" or "戊土" or "归土" or "宝土" or "宣土") ageId = "age_tude";
		else if (value is "三雷" or "天雷" or "玄雷" or "霄雷" or "元雷") ageId = "age_tianlei";
		else if (value == "十二炁" || value.EndsWith("炁", System.StringComparison.Ordinal) || value is "上仪" or "下仪") ageId = "age_qingqi";

		return ageId.Length > 0;
	}
}
