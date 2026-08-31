using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.UI.Common;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.UI.ZongMen;

internal readonly struct XjZongMenUiGongFaItem
{
	internal readonly string Name;
	internal readonly int Grade;
	internal readonly string DaoTu;
	internal readonly string MappedXianJi;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly int Year;

	internal XjZongMenUiGongFaItem(string name, int grade, string daoTu, string mappedXianJi, long actorId, string actorName, int year)
	{
		Name = name ?? string.Empty;
		Grade = grade < 0 ? 0 : grade;
		DaoTu = daoTu ?? string.Empty;
		MappedXianJi = mappedXianJi ?? string.Empty;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

internal readonly struct XjZongMenUiQiuJinFaItem
{
	internal readonly string Name;
	internal readonly string SourceGongFaName;
	internal readonly string DaoTu;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly int Year;

	internal XjZongMenUiQiuJinFaItem(string name, string sourceGongFaName, string daoTu, long actorId, string actorName, int year)
	{
		Name = name ?? string.Empty;
		SourceGongFaName = sourceGongFaName ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

internal readonly struct XjZongMenUiCaiQiItem
{
	internal readonly string ResourceId;
	internal readonly string ResourceName;
	internal readonly string DaoTu;
	internal readonly int Amount;
	internal readonly string SourcePlace;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly int Year;

	internal XjZongMenUiCaiQiItem(
		string resourceId,
		string resourceName,
		string daoTu,
		int amount,
		string sourcePlace,
		long actorId,
		string actorName,
		int year)
	{
		ResourceId = resourceId ?? string.Empty;
		ResourceName = resourceName ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		Amount = amount < 0 ? 0 : amount;
		SourcePlace = sourcePlace ?? string.Empty;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

internal readonly struct XjZongMenUiCaiQiFaItem
{
	internal readonly string Name;
	internal readonly string DaoTu;
	internal readonly string SourcePlace;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly int Year;

	internal XjZongMenUiCaiQiFaItem(string name, string daoTu, string sourcePlace, long actorId, string actorName, int year)
	{
		Name = name ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		SourcePlace = sourcePlace ?? string.Empty;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

internal readonly struct XjZongMenUiReadModel
{
	internal readonly XjSectIdentitySnapshot Identity;
	internal readonly IReadOnlyList<string> OverviewItems;
	internal readonly IReadOnlyList<XjZongMenUiGongFaItem> GongFaItems;
	internal readonly IReadOnlyList<XjZongMenUiQiuJinFaItem> QiuJinFaItems;
	internal readonly IReadOnlyList<XjZongMenUiCaiQiItem> CaiQiItems;
	internal readonly IReadOnlyList<XjZongMenUiCaiQiFaItem> CaiQiFaItems;

	private XjZongMenUiReadModel(
		XjSectIdentitySnapshot identity,
		IReadOnlyList<string> overviewItems,
		IReadOnlyList<XjZongMenUiGongFaItem> gongFaItems,
		IReadOnlyList<XjZongMenUiQiuJinFaItem> qiuJinFaItems,
		IReadOnlyList<XjZongMenUiCaiQiItem> caiQiItems,
		IReadOnlyList<XjZongMenUiCaiQiFaItem> caiQiFaItems)
	{
		Identity = identity;
		OverviewItems = overviewItems ?? System.Array.Empty<string>();
		GongFaItems = gongFaItems ?? System.Array.Empty<XjZongMenUiGongFaItem>();
		QiuJinFaItems = qiuJinFaItems ?? System.Array.Empty<XjZongMenUiQiuJinFaItem>();
		CaiQiItems = caiQiItems ?? System.Array.Empty<XjZongMenUiCaiQiItem>();
		CaiQiFaItems = caiQiFaItems ?? System.Array.Empty<XjZongMenUiCaiQiFaItem>();
	}

	internal static XjZongMenUiReadModel BuildForActor(Actor actor)
	{
		XjSectIdentitySnapshot identity = XjSectIdentityReader.BuildIdentity(actor);
		if (!identity.Found || identity.ZongMenId <= 0L)
		{
			return Empty(identity);
		}

		XjSectCityData.TryResolveZongMenCity(identity.ZongMenId, out City zongMenCity);
		return new XjZongMenUiReadModel(
			identity,
			BuildOverview(zongMenCity),
			BuildPavilion(identity.ZongMenId),
			BuildQiuJinFaPavilion(identity.ZongMenId),
			BuildCaiQiWarehouse(identity.ZongMenId),
			BuildCaiQiFaWarehouse(identity.ZongMenId));
	}

	internal static XjZongMenUiReadModel BuildForCity(City city)
	{
		XjSectIdentitySnapshot identity = XjSectIdentityReader.BuildForCity(city);
		if (!identity.Found || identity.ZongMenId <= 0L)
		{
			return Empty(identity);
		}

		return new XjZongMenUiReadModel(
			identity,
			BuildOverview(city),
			BuildPavilion(identity.ZongMenId),
			BuildQiuJinFaPavilion(identity.ZongMenId),
			BuildCaiQiWarehouse(identity.ZongMenId),
			BuildCaiQiFaWarehouse(identity.ZongMenId));
	}

	internal static IReadOnlyList<XjZongMenUiGongFaItem> BuildPavilion(long zongMenId)
	{
		IReadOnlyList<XjSectGongFaPavilionEntry> entries = XjSectGongFaPavilion.ReadGongFaEntries(zongMenId);
		if (entries.Count == 0)
		{
			return System.Array.Empty<XjZongMenUiGongFaItem>();
		}

		List<XjZongMenUiGongFaItem> items = new List<XjZongMenUiGongFaItem>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectGongFaPavilionEntry entry = entries[i];
			if (!entry.Found || string.IsNullOrWhiteSpace(entry.Name))
			{
				continue;
			}

			items.Add(new XjZongMenUiGongFaItem(entry.Name, entry.Grade, entry.DaoTu, entry.MappedXianJi, entry.ActorId, entry.ActorName, entry.Year));
		}

		return items.Count == 0 ? System.Array.Empty<XjZongMenUiGongFaItem>() : items;
	}

	internal static IReadOnlyList<XjZongMenUiQiuJinFaItem> BuildQiuJinFaPavilion(long zongMenId)
	{
		IReadOnlyList<XjSectGongFaPavilionEntry> entries = XjSectGongFaPavilion.ReadQiuJinFaEntries(zongMenId);
		if (entries.Count == 0)
		{
			return System.Array.Empty<XjZongMenUiQiuJinFaItem>();
		}

		List<XjZongMenUiQiuJinFaItem> items = new List<XjZongMenUiQiuJinFaItem>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectGongFaPavilionEntry entry = entries[i];
			if (!entry.Found || string.IsNullOrWhiteSpace(entry.Name))
			{
				continue;
			}

			items.Add(new XjZongMenUiQiuJinFaItem(entry.Name, entry.SourceGongFaName, entry.DaoTu, entry.ActorId, entry.ActorName, entry.Year));
		}

		return items.Count == 0 ? System.Array.Empty<XjZongMenUiQiuJinFaItem>() : items;
	}

	internal static IReadOnlyList<XjZongMenUiCaiQiItem> BuildCaiQiWarehouse(long zongMenId)
	{
		IReadOnlyList<XjSectCaiQiWarehouseEntry> entries = XjSectCaiQiWarehouse.ReadCaiQiResources(zongMenId);
		if (entries.Count == 0)
		{
			return System.Array.Empty<XjZongMenUiCaiQiItem>();
		}

		List<XjZongMenUiCaiQiItem> items = new List<XjZongMenUiCaiQiItem>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectCaiQiWarehouseEntry entry = entries[i];
			if (!entry.Found || entry.Amount <= 0)
			{
				continue;
			}

			string name = ResolveCaiQiResourceName(entry.ResourceId, entry.ResourceName);
			items.Add(new XjZongMenUiCaiQiItem(entry.ResourceId, name, entry.DaoTu, entry.Amount, entry.SourcePlace, entry.ActorId, entry.ActorName, entry.Year));
		}

		return items.Count == 0 ? System.Array.Empty<XjZongMenUiCaiQiItem>() : items;
	}

	private static string ResolveCaiQiResourceName(string resourceId, string resourceName)
	{
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string displayName))
		{
			return displayName;
		}
		if (string.Equals(resourceId, "zaqi", System.StringComparison.Ordinal))
		{
			return "杂气";
		}
		return string.IsNullOrWhiteSpace(resourceName) ? "未名先天之气" : resourceName.Trim();
	}

	internal static IReadOnlyList<XjZongMenUiCaiQiFaItem> BuildCaiQiFaWarehouse(long zongMenId)
	{
		IReadOnlyList<XjSectCaiQiWarehouseEntry> entries = XjSectCaiQiWarehouse.ReadCaiQiFaResources(zongMenId);
		if (entries.Count == 0)
		{
			return System.Array.Empty<XjZongMenUiCaiQiFaItem>();
		}

		List<XjZongMenUiCaiQiFaItem> items = new List<XjZongMenUiCaiQiFaItem>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectCaiQiWarehouseEntry entry = entries[i];
			if (!entry.Found || string.IsNullOrWhiteSpace(entry.ResourceName))
			{
				continue;
			}

			items.Add(new XjZongMenUiCaiQiFaItem(entry.ResourceName, entry.DaoTu, entry.SourcePlace, entry.ActorId, entry.ActorName, entry.Year));
		}

		return items.Count == 0 ? System.Array.Empty<XjZongMenUiCaiQiFaItem>() : items;
	}

	private static XjZongMenUiReadModel Empty(XjSectIdentitySnapshot identity)
	{
		return new XjZongMenUiReadModel(
			identity,
			System.Array.Empty<string>(),
			System.Array.Empty<XjZongMenUiGongFaItem>(),
			System.Array.Empty<XjZongMenUiQiuJinFaItem>(),
			System.Array.Empty<XjZongMenUiCaiQiItem>(),
			System.Array.Empty<XjZongMenUiCaiQiFaItem>());
	}

	private static IReadOnlyList<string> BuildOverview(City city)
	{
		if (!XjSectCityData.HasZongMen(city))
		{
			return System.Array.Empty<string>();
		}

		List<string> items = new List<string>(10);
		string founder = XjSectCityData.GetFounderName(city);
		string zongZhu = XjSectCityData.GetZongZhuName(city);
		if (!string.IsNullOrWhiteSpace(founder))
		{
			items.Add("开山祖师：" + founder);
		}
		if (!string.IsNullOrWhiteSpace(zongZhu))
		{
			items.Add("宗主：" + zongZhu);
		}
		items.Add("宗主代数：第" + XjSectCityData.GetGeneration(city).ToString(CultureInfo.InvariantCulture) + "代");
		items.Add("门人：" + XjSectCityData.GetStoredMemberCount(city).ToString(CultureInfo.InvariantCulture));
		items.Add("山峰：" + XjSectCityData.GetPeaks(city).Count.ToString(CultureInfo.InvariantCulture));
		int elders = XjSectCityData.GetSupremeElderCount(city);
		items.Add("洞天驻修：" + elders.ToString(CultureInfo.InvariantCulture));

		XjSectCityTraitState traitState = XjSectCityTraitAccessor.BuildState(city);
		if (traitState.ScaleLevel > 0) items.Add("宗门规模：" + traitState.ScaleLevel.ToString(CultureInfo.InvariantCulture));
		if (traitState.SpiritVeinLevel > 0) items.Add("灵脉等级：" + traitState.SpiritVeinLevel.ToString(CultureInfo.InvariantCulture));
		if (traitState.FormationLevel > 0) items.Add("护山阵势：" + traitState.FormationLevel.ToString(CultureInfo.InvariantCulture));
		if (!string.IsNullOrWhiteSpace(traitState.EnvironmentState)) items.Add("宗门环境：" + traitState.EnvironmentState.Trim());
		return items;
	}
}

internal readonly struct XjZongMenDisplaySection
{
	internal readonly string Title;
	internal readonly string[] Items;

	internal XjZongMenDisplaySection(string title, string[] items)
	{
		Title = title ?? string.Empty;
		Items = items ?? new[] { XjZongMenDisplayFormatter.EmptyText };
	}
}

internal static class XjZongMenDisplayFormatter
{
	internal const string EmptyText = "暂无";

	internal static XjZongMenDisplaySection[] BuildSections(in XjZongMenUiReadModel model)
	{
		return new[]
		{
			BuildIdentitySection(model.Identity),
			new XjZongMenDisplaySection("宗门概况", ToArrayOrEmpty(model.OverviewItems)),
			BuildGongFaSection(model.GongFaItems),
			BuildQiuJinFaSection(model.QiuJinFaItems),
			BuildCaiQiSection(model.CaiQiItems),
			BuildCaiQiFaSection(model.CaiQiFaItems)
		};
	}

	private static string[] ToArrayOrEmpty(IReadOnlyList<string> items)
	{
		if (items == null || items.Count == 0)
		{
			return new[] { EmptyText };
		}

		string[] result = new string[items.Count];
		for (int i = 0; i < items.Count; i++)
		{
			result[i] = items[i];
		}
		return result;
	}

	internal static string FormatSections(IReadOnlyList<XjZongMenDisplaySection> sections)
	{
		if (sections == null || sections.Count == 0)
		{
			return EmptyText;
		}

		List<string> lines = new List<string>();
		for (int i = 0; i < sections.Count; i++)
		{
			XjZongMenDisplaySection section = sections[i];
			if (string.IsNullOrWhiteSpace(section.Title))
			{
				continue;
			}

			lines.Add(section.Title);
			string[] items = section.Items;
			if (items == null || items.Length == 0)
			{
				lines.Add("  " + EmptyText);
				continue;
			}

			for (int j = 0; j < items.Length; j++)
			{
				lines.Add("  " + (string.IsNullOrWhiteSpace(items[j]) ? EmptyText : items[j]));
			}
		}

		return lines.Count == 0 ? EmptyText : string.Join("\n", lines);
	}

	private static XjZongMenDisplaySection BuildIdentitySection(in XjSectIdentitySnapshot identity)
	{
		if (!identity.Found || identity.ZongMenId <= 0L)
		{
			return new XjZongMenDisplaySection("宗门身份", new[] { "暂无宗门" });
		}

		List<string> items = new List<string>
		{
			"宗门：" + identity.ZongMenName,
			"身份：" + NormalizeRankForDisplay(identity.Rank)
		};

		if (identity.JoinYear > 0)
		{
			items.Add((identity.ActorId > 0L ? "入门：" : "创立：") + XjChronology.FormatYear(identity.JoinYear));
		}

		return new XjZongMenDisplaySection("宗门身份", items.ToArray());
	}

	private static string NormalizeRankForDisplay(string rank)
	{
		string value = string.IsNullOrWhiteSpace(rank) ? string.Empty : rank.Trim();
		if (value.Contains("宗主")) return "宗主";
		if (value.Contains("峰主")) return "峰主";
		if (value.Contains("洞天") || value.Contains("老祖")) return "洞天修士";
		if (value.Contains("长老")) return "长老";
		if (value.Contains("弟子")) return "弟子";
		if (value.Contains("门人")) return "门人";
		return string.IsNullOrWhiteSpace(value) ? "门人" : value;
	}

	private static XjZongMenDisplaySection BuildGongFaSection(IReadOnlyList<XjZongMenUiGongFaItem> items)
	{
		if (items == null || items.Count == 0)
		{
			return new XjZongMenDisplaySection("宗门功法库", new[] { "暂无功法" });
		}

		List<string> lines = new List<string>(items.Count);
		for (int i = 0; i < items.Count; i++)
		{
			XjZongMenUiGongFaItem item = items[i];
			lines.Add(
				item.Name
				+ " · "
				+ FormatGongFaGrade(item.Grade)
				+ " · "
				+ EmptyWhenMissing(item.DaoTu)
				+ FormatSource(item.ActorName, item.Year));
		}

		return new XjZongMenDisplaySection("宗门功法库", lines.ToArray());
	}

	private static XjZongMenDisplaySection BuildQiuJinFaSection(IReadOnlyList<XjZongMenUiQiuJinFaItem> items)
	{
		if (items == null || items.Count == 0)
		{
			return new XjZongMenDisplaySection("宗门求金法库", new[] { "暂无求金法" });
		}

		List<string> lines = new List<string>(items.Count);
		for (int i = 0; i < items.Count; i++)
		{
			XjZongMenUiQiuJinFaItem item = items[i];
			string sourceGongFa = string.IsNullOrWhiteSpace(item.SourceGongFaName) ? "来源功法未载" : item.SourceGongFaName;
			lines.Add(
				item.Name
				+ " · "
				+ sourceGongFa
				+ " · "
				+ EmptyWhenMissing(item.DaoTu)
				+ FormatSource(item.ActorName, item.Year));
		}

		return new XjZongMenDisplaySection("宗门求金法库", lines.ToArray());
	}

	private static XjZongMenDisplaySection BuildCaiQiSection(IReadOnlyList<XjZongMenUiCaiQiItem> items)
	{
		if (items == null || items.Count == 0)
		{
			return new XjZongMenDisplaySection("宗门纳气库", new[] { "暂无纳气" });
		}

		List<string> lines = new List<string>(items.Count);
		for (int i = 0; i < items.Count; i++)
		{
			XjZongMenUiCaiQiItem item = items[i];
			lines.Add(
				item.ResourceName
				+ " x"
				+ item.Amount.ToString(CultureInfo.InvariantCulture)
				+ " · "
				+ EmptyWhenMissing(item.DaoTu)
				+ FormatPlaceAndYear(item.SourcePlace, item.Year));
		}

		return new XjZongMenDisplaySection("宗门纳气库", lines.ToArray());
	}

	private static XjZongMenDisplaySection BuildCaiQiFaSection(IReadOnlyList<XjZongMenUiCaiQiFaItem> items)
	{
		if (items == null || items.Count == 0)
		{
			return new XjZongMenDisplaySection("宗门采气法库", new[] { "暂无采气法" });
		}

		List<string> lines = new List<string>(items.Count);
		for (int i = 0; i < items.Count; i++)
		{
			XjZongMenUiCaiQiFaItem item = items[i];
			lines.Add(
				item.Name
				+ " · "
				+ EmptyWhenMissing(item.DaoTu)
				+ FormatPlaceAndYear(item.SourcePlace, item.Year));
		}

		return new XjZongMenDisplaySection("宗门采气法库", lines.ToArray());
	}

	private static string FormatGongFaGrade(int grade)
	{
		return XuanJianVNext.Data.GongFa.XjGongFaGradeText.Format(grade);
	}

	private static string EmptyWhenMissing(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? "未载" : value;
	}

	private static string FormatSource(string actorName, int year)
	{
		List<string> parts = new List<string>(2);
		if (!string.IsNullOrWhiteSpace(actorName))
		{
			parts.Add("来源：" + actorName);
		}

		if (year > 0)
		{
			parts.Add(XjChronology.FormatYear(year));
		}

		return parts.Count == 0 ? string.Empty : " · " + string.Join(" · ", parts);
	}

	private static string FormatPlaceAndYear(string place, int year)
	{
		List<string> parts = new List<string>(2);
		if (!string.IsNullOrWhiteSpace(place))
		{
			parts.Add(place);
		}

		if (year > 0)
		{
			parts.Add(XjChronology.FormatYear(year));
		}

		return parts.Count == 0 ? string.Empty : " · " + string.Join(" · ", parts);
	}
}
