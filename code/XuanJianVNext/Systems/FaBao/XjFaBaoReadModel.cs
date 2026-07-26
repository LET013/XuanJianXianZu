using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.Systems.FaBao;

internal readonly struct XjFaBaoDisplayState
{
	internal readonly bool Found;
	internal readonly string FaBaoId;
	internal readonly string FaBaoName;
	internal readonly string DaoTu;
	internal readonly string ClassName;
	internal readonly string Source;
	internal readonly int Year;
	internal readonly string DisplayText;

	internal XjFaBaoDisplayState(
		bool found,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year,
		string displayText)
	{
		Found = found;
		FaBaoId = faBaoId ?? string.Empty;
		FaBaoName = faBaoName ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		ClassName = className ?? string.Empty;
		Source = source ?? string.Empty;
		Year = year < 0 ? 0 : year;
		DisplayText = displayText ?? string.Empty;
	}
}

internal readonly struct XjFamilyFaBaoDisplayItem
{
	internal readonly bool Found;
	internal readonly string FaBaoId;
	internal readonly string FaBaoName;
	internal readonly string DaoTu;
	internal readonly string ClassName;
	internal readonly string Source;
	internal readonly string ActorName;
	internal readonly int Year;
	internal readonly string DisplayText;

	internal XjFamilyFaBaoDisplayItem(
		bool found,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		string actorName,
		int year,
		string displayText)
	{
		Found = found;
		FaBaoId = faBaoId ?? string.Empty;
		FaBaoName = faBaoName ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		ClassName = className ?? string.Empty;
		Source = source ?? string.Empty;
		ActorName = actorName ?? string.Empty;
		Year = year < 0 ? 0 : year;
		DisplayText = displayText ?? string.Empty;
	}
}

internal static class XjFaBaoReadModel
{
	internal static XjFaBaoDisplayState BuildForActor(Actor actor)
	{
		XjFaBaoState state = XjFaBaoAccessor.BuildState(actor);
		if (!state.Found)
		{
			return new XjFaBaoDisplayState(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, "暂无器物");
		}

		return new XjFaBaoDisplayState(
			true,
			state.Id,
			state.Name,
			state.DaoTu,
			state.ClassName,
			state.Source,
			state.Year,
			BuildDisplayText(state.Name, state.DaoTu, state.ClassName, state.Source, state.Year, string.Empty, XjFaBaoBonusService.BuildBonusText(state)));
	}

	internal static IReadOnlyList<XjFamilyFaBaoDisplayItem> BuildFamilyItems(long familyStableId)
	{
		IReadOnlyList<XjFamilyFaBaoWarehouseEntry> entries = XjFamilyWarehouseReadModel.Shared.ReadFamilyFaBaoEntries(familyStableId);
		if (entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		}

		List<XjFamilyFaBaoDisplayItem> items = new List<XjFamilyFaBaoDisplayItem>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry entry = entries[i];
			if (!entry.Found || string.IsNullOrWhiteSpace(entry.FaBaoName))
			{
				continue;
			}

			items.Add(new XjFamilyFaBaoDisplayItem(
				true,
				entry.FaBaoId,
				entry.FaBaoName,
				entry.DaoTu,
				entry.ClassName,
				entry.Source,
				entry.ActorName,
				entry.Year,
				BuildDisplayText(entry.FaBaoName, entry.DaoTu, entry.ClassName, entry.Source, entry.Year, entry.ActorName)));
		}

		items.Sort((left, right) =>
		{
			int display = string.Compare(left.DisplayText, right.DisplayText, System.StringComparison.Ordinal);
			return display != 0 ? display : string.Compare(left.FaBaoId, right.FaBaoId, System.StringComparison.Ordinal);
		});
		return items;
	}

	private static string BuildDisplayText(
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year,
		string actorName,
		string bonusText = "")
	{
		System.Text.StringBuilder builder = new System.Text.StringBuilder(48);
		builder.Append(string.IsNullOrWhiteSpace(faBaoName) ? "未名法宝" : faBaoName.Trim());

		List<string> parts = new List<string>(5);
		if (!string.IsNullOrWhiteSpace(daoTu))
		{
			parts.Add(daoTu.Trim());
		}

		if (!string.IsNullOrWhiteSpace(className))
		{
			parts.Add(className.Trim());
		}

		string displaySource = FormatSource(source);
		if (!string.IsNullOrWhiteSpace(displaySource))
		{
			parts.Add(displaySource);
		}

		if (year > 0)
		{
			parts.Add(year.ToString(System.Globalization.CultureInfo.InvariantCulture) + "年");
		}

		if (!string.IsNullOrWhiteSpace(actorName))
		{
			parts.Add(actorName.Trim());
		}

		if (!string.IsNullOrWhiteSpace(bonusText))
		{
			parts.Add(bonusText.Trim());
		}

		if (parts.Count > 0)
		{
            builder.Append("（");
            builder.Append(string.Join(" - ", parts));
            builder.Append("）");
		}

		return builder.ToString();
	}

	internal static string FormatSource(string source)
	{
		string key = (source ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(key))
		{
			return string.Empty;
		}

		return key switch
		{
			"JinDan" => "金丹成器",
			"ZiFuRefine" => "紫府炼器",
			"LingBaoUpgrade" => "灵宝升格",
			"JieLinGrant" => "结璘赐宝",
			"JieLinUpgrade" => "结璘升宝",
			"QiYuDongTian" => "洞天机缘",
			"EquipmentEditorGrant" => "赐器入命",
			"CultivatorSlotRefine" => "炼器入身",
			"CultivatorSlotUpgrade" => "灵宝升格",
			"LongShuBirth" => "龙属天成",
			"WarehouseSurplusCraft" => "余器入库",
			"EquippedItem" => "随身器物",
			_ => string.Empty
		};
	}
}
