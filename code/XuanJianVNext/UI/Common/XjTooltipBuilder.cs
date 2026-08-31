using System;
using System.Globalization;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Core;

namespace XuanJianVNext.UI.Common;

internal static class XjTooltipBuilder
{
	internal static string BuildCaiQiTooltip(string resourceId, string displayName)
	{
		string normalized = string.IsNullOrWhiteSpace(resourceId) ? string.Empty : resourceId.Trim();
		string name = string.IsNullOrWhiteSpace(displayName) ? normalized : displayName.Trim();
		if (string.Equals(normalized, "zaqi", System.StringComparison.Ordinal))
		{
			return RegisterAndReturn("杂气，吞之速成，然筑基无望，慎入。");
		}

		if (string.Equals(normalized, "taiyinyuehua", System.StringComparison.Ordinal))
		{
			return RegisterAndReturn("太阴月华，乃广寒之精、太阴之气极端凝聚所化，纯白如雾，可筑基修炼、可入高阶灵物，为太阴道途之根基");
		}

		return RegisterAndReturn((string.IsNullOrWhiteSpace(name) ? "纳气" : name) + "，乃天地道途所凝之灵粹，修士吐纳涵养，沾之可筑基、润之可证道。");
	}

	internal static void BuildGongFaTooltip(
		XjFamilyGongFaWarehouseUIItem item,
		out string title,
		out string description,
		out string details)
	{
		bool isQiuJinFa = string.Equals(item.SourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, System.StringComparison.Ordinal);
		bool isCaiQiFa = string.Equals(item.SourceType, XjFamilyCaiQiWarehouse.ResourceTypeCaiQiFa, System.StringComparison.Ordinal);
		string type = isQiuJinFa ? "求金法" : isCaiQiFa ? "采气法" : "功法";
		string name = string.IsNullOrWhiteSpace(item.Name) ? type : item.Name.Trim();
		title = name;
		if (isCaiQiFa)
		{
			string daoTu = XjDisplayNameSanitizer.GameTerm(item.DaoTu, string.Empty);
			description = "采气法。";
			details = JoinTooltipLines(
				"类型：采气法",
				string.IsNullOrWhiteSpace(daoTu) ? "可用道途：未定" : "可用道途：" + daoTu,
				"收录：" + Math.Max(1, item.Count).ToString(CultureInfo.InvariantCulture) + "份");
			RegisterTooltipText(title, description, details);
			return;
		}

		int normalizedGrade = XjGongFaGradeText.Normalize(item.Grade);
		string grade = XjGongFaGradeText.Format(normalizedGrade);
		XjFuQiCoreDefinition fuQiDefinition = default;
		bool isFuQiMethod = !isQiuJinFa && IsFuQiMethodItem(item, out fuQiDefinition);
		description = isQiuJinFa
			? "求金法。"
			: isFuQiMethod
				? "此法以性命为炉、真息为薪，随修者心境与道行层层蜕变；黄冠守其形，真人得其真，真君羽士则令法意与金性同归一身。"
				: GetGongFaGradeDescription(normalizedGrade);
		if (isQiuJinFa)
		{
			details = JoinTooltipLines(
				"类型：求金法",
				string.IsNullOrWhiteSpace(item.DaoTu) ? string.Empty : "可用道途：" + XjDisplayNameSanitizer.GameTerm(item.DaoTu, "未定"),
				string.IsNullOrWhiteSpace(item.BoundGongFaName) ? string.Empty : "关联功法：" + item.BoundGongFaName.Trim(),
				string.IsNullOrWhiteSpace(item.BoundAuthority) ? string.Empty : "权柄：" + item.BoundAuthority.Trim());
			RegisterTooltipText(title, description, details);
			return;
		}

		details = JoinTooltipLines(
			isFuQiMethod
				? "类型：服气养性功法"
				: "类型：" + (normalizedGrade >= 6 ? "紫府金丹六品功法" : normalizedGrade == 5 ? "紫府金丹五品功法" : "紫府金丹主修功法"),
			"品级：" + grade,
			isFuQiMethod ? "修炼方式：服气养性" : "修炼方式：紫府金丹",
			string.IsNullOrWhiteSpace(item.DaoTu) ? string.Empty : "可用道途：" + XjDisplayNameSanitizer.GameTerm(item.DaoTu, "未定"),
			isFuQiMethod && !string.IsNullOrWhiteSpace(fuQiDefinition.DisplayName)
				? "本命核心：" + fuQiDefinition.DisplayName
				: string.Empty,
			isFuQiMethod && !string.IsNullOrWhiteSpace(fuQiDefinition.MethodEffect)
				? "法门作用：" + fuQiDefinition.MethodEffect
				: string.Empty,
			!isFuQiMethod && !string.IsNullOrWhiteSpace(item.MappedXianJi) ? "映射神通：" + item.MappedXianJi.Trim() : string.Empty);
		RegisterTooltipText(title, description, details);
	}

	private static bool IsFuQiMethodItem(
		XjFamilyGongFaWarehouseUIItem item,
		out XjFuQiCoreDefinition definition)
	{
		definition = default;
		string daoTu = (item.DaoTu ?? string.Empty).Trim();
		string name = (item.Name ?? string.Empty).Trim();
		if (daoTu.Length == 0 || name.Length == 0 || !XjFuQiCoreCatalog.TryResolveByDaoTu(daoTu, out definition))
		{
			return false;
		}

		if (string.Equals(name, definition.MethodName, StringComparison.Ordinal))
		{
			return true;
		}

		// 兼容0.9.5首版已经入库的根类服气功法名，同时避免仅凭“养性篇/养命篇”
		// 之类泛词把紫府金丹功法误判为服气养性。
		return XjFuQiCoreCatalog.IsKnownMethodName(name);
	}

	private static string GetGongFaGradeDescription(int grade)
	{
		return grade switch
		{
			1 => "一品：筑基之始，引气入门的粗浅法门。",
			2 => "二品：炼气之基，可筑仙基的入门功法。",
			3 => "三品：奠基之法，稳固道途的根基要诀。",
			4 => "四品：通幽之径，直指紫府的上乘法门。",
			5 => "五品：求金之阶，可窥金丹的玄妙典籍。",
			6 => "六品：金丹门槛，成丹前所需的圆满传承。",
			_ => "无品：尚无明确功法描述。"
		};
	}

	internal static string JoinTooltipLines(params string[] lines)
	{
		if (lines == null || lines.Length == 0)
		{
			return string.Empty;
		}

		System.Text.StringBuilder builder = new System.Text.StringBuilder(64);
		for (int i = 0; i < lines.Length; i++)
		{
			if (string.IsNullOrWhiteSpace(lines[i]))
			{
				continue;
			}

			if (builder.Length > 0)
			{
				builder.Append('\n');
			}
			builder.Append(lines[i].Trim());
		}

		return builder.ToString();
	}

	private static string RegisterAndReturn(string text)
	{
		XjNativeHoverTooltip.RegisterPassthrough(text);
		return text;
	}

	private static void RegisterTooltipText(string title, string description, string details)
	{
		XjNativeHoverTooltip.RegisterPassthrough(title, description, details);
	}
}
