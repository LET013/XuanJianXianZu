using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	private const string FaBaoStatsMarker = "\u200B\u200C\u200D\uFEFF";
	private const string LegacyFaBaoStatsMarker = "\u200B\u200C玄鉴" + "法宝词条\u200D\uFEFF";
	private static readonly FieldInfo FaBaoItemWindowTypeTextField = AccessTools.Field(typeof(ItemWindow), "_text_item_type");
	private static readonly FieldInfo FaBaoItemWindowNameInputField = AccessTools.Field(typeof(WindowMetaGeneric<Item, ItemData>), "_name_input");
	private static readonly FieldInfo FaBaoItemWindowNameTextField =
		FaBaoItemWindowNameInputField?.FieldType?.GetField("textField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Item), nameof(Item.calculateValues))]
	private static void XuanJianVNext_Item_CalculateValues_FaBao_Postfix(Item __instance)
	{
		if (XjFaBaoEquipmentSync.TryReadFaBaoState(__instance, out var state))
		{
			XjFaBaoEquipmentSync.ApplyNativeClassStats(__instance, state.ClassName);
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Item), nameof(Item.getItemDescription))]
	private static void XuanJianVNext_Item_GetItemDescription_FaBao_Postfix(Item __instance, ref string __result)
	{
		if (!XjFaBaoEquipmentSync.TryReadFaBaoItem(__instance, out _, out _, out string description, out _))
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(description))
		{
			XjNativeHoverTooltip.RegisterPassthrough(description);
			__result = Toolbox.coloredString(description.Trim(), "#FFFFFF");
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(ItemWindow), "showStatsRows")]
	private static bool XuanJianVNext_ItemWindow_ShowStatsRows_FaBao_Prefix(ItemWindow __instance)
	{
		Item item = __instance?.meta_object;
		if (!XjFaBaoEquipmentSync.TryReadFaBaoItem(item, out _, out _, out _, out string affixes))
		{
			return true;
		}

		ShowFaBaoItemWindowNativeRows(__instance, item);
		ShowFaBaoItemWindowAffixRows(__instance, affixes);
		ShowFaBaoItemWindowRow(__instance, "耐久度", "100-100");

		return false;
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(BaseUnlockableAsset), nameof(BaseUnlockableAsset.getSprite))]
	private static void XuanJianVNext_BaseUnlockableAsset_GetSprite_EquipmentIcon_Postfix(
		BaseUnlockableAsset __instance,
		ref Sprite __result)
	{
		if (__instance is EquipmentAsset equipment
			&& XjFaBaoEquipmentAssets.TryGetOwnIcon(equipment, out Sprite sprite))
		{
			__result = sprite;
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(AugmentationButton<EquipmentAsset>), "load")]
	private static void XuanJianVNext_AugmentationButton_Load_HideVanillaEquipment_Postfix(
		AugmentationButton<EquipmentAsset> __instance,
		[HarmonyArgument(0)] EquipmentAsset asset)
	{
		if (__instance == null || asset == null)
		{
			return;
		}

		try
		{
			bool hide = XjFaBaoEquipmentAssets.ShouldHideVanillaEditorAsset(asset);
			if (__instance.gameObject.activeSelf == hide)
			{
				__instance.gameObject.SetActive(!hide);
			}
		}
		catch (System.Exception xjCaught101) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.FaBaoEquipment.cs:101", xjCaught101); }
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(ItemWindow), "showTopPartInformation")]
	private static void XuanJianVNext_ItemWindow_ShowTopPartInformation_FaBao_Postfix(ItemWindow __instance)
	{
		Item item = __instance?.meta_object ?? SelectedMetas.selected_item;
		if (!XjFaBaoEquipmentSync.TryReadFaBaoItem(item, out string name, out string className, out _, out _))
		{
			return;
		}

		try
		{
			Text typeText = FaBaoItemWindowTypeTextField?.GetValue(__instance) as Text;
			if (typeText != null)
			{
				typeText.text = string.IsNullOrWhiteSpace(className) ? "玄鉴法宝" : className.Trim();
				typeText.color = Toolbox.makeColor("#FF9B2F");
				XjNativeHoverTooltip.RegisterPassthrough(typeText.text);
			}

			if (FaBaoItemWindowNameInputField != null && FaBaoItemWindowNameTextField != null)
			{
				object nameInput = FaBaoItemWindowNameInputField.GetValue(__instance);
				Text nameText = FaBaoItemWindowNameTextField.GetValue(nameInput) as Text;
				if (nameText != null && !string.IsNullOrWhiteSpace(name))
				{
					nameText.text = name.Trim();
					nameText.color = Toolbox.makeColor("#FF9B2F");
					XjNativeHoverTooltip.RegisterPassthrough(nameText.text);
				}
			}
		}
		catch (System.Exception xjCaught138) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.FaBaoEquipment.cs:138", xjCaught138); }
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(TooltipLibrary), "showEquipment")]
	private static void XuanJianVNext_TooltipLibrary_ShowEquipment_FaBao_Postfix(Tooltip pTooltip, TooltipData pData)
	{
		Item item = pData?.item;
		if (!XjFaBaoEquipmentSync.TryReadFaBaoItem(item, out string name, out string className, out string description, out string affixes))
		{
			return;
		}

		try
		{
			if (pTooltip?.name != null)
			{
				pTooltip.name.text = Toolbox.coloredText(name, "#FF9B2F", false);
			}

			Text typeText = GetEquipmentTypeText(pTooltip);
			if (typeText != null)
			{
				typeText.text = string.IsNullOrWhiteSpace(className) ? "玄鉴法宝" : className;
				typeText.color = Toolbox.makeColor("#FF9B2F");
			}

			string body = BuildFaBaoEquipmentTooltipDescription(description, affixes);
			if (!string.IsNullOrWhiteSpace(body))
			{
				pTooltip.setDescription(Toolbox.coloredString(body, "#FFFFFF"), null);
			}

			ApplyFaBaoAffixStats(pTooltip, className, affixes);
		}
		catch (System.Exception xjCaught176) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.FaBaoEquipment.cs:176", xjCaught176); }
	}

	private static Text GetEquipmentTypeText(Tooltip tooltip)
	{
		if (tooltip == null)
		{
			return null;
		}

		try
		{
			Transform row = tooltip.transform.Find("Equipment Type/EquipmentText");
			return row == null ? null : row.GetComponent<Text>();
		}
		catch
		{
			return null;
		}
	}

	private static string BuildFaBaoEquipmentTooltipDescription(string description, string affixes)
	{
		string desc = StripVerticalSeparators((description ?? string.Empty).Trim());
		return desc;
	}

	private static void ApplyFaBaoAffixStats(Tooltip tooltip, string className, string affixes)
	{
		if (tooltip == null || tooltip.stats_description == null || tooltip.stats_values == null)
		{
			return;
		}

		StripFaBaoStats(tooltip.stats_description);
		StripFaBaoStats(tooltip.stats_values);
		tooltip.stats_description.text = StripVerticalSeparators(tooltip.stats_description.text);
		tooltip.stats_values.text = StripVerticalSeparators(tooltip.stats_values.text);
		ExtractFaBaoNativeTailRows(
			tooltip.stats_description,
			tooltip.stats_values,
			out string durabilityLabel,
			out string durabilityValue,
			out string killsLabel,
			out string killsValue);

		bool markerWritten = false;
		string[] parts = SplitFaBaoAffixParts(affixes);
		for (int i = 0; i < parts.Length; i++)
		{
			if (!TrySplitAffix(parts[i], out string label, out string value))
			{
				continue;
			}

			AppendFaBaoTooltipStatRow(
				tooltip.stats_description,
				tooltip.stats_values,
				Toolbox.coloredText(CompactFaBaoAffixLabel(label), "#45FFFE", false),
				Toolbox.coloredText("+" + value, "#43FF43", false),
				ref markerWritten);
		}

		AppendFaBaoTooltipStatRow(
			tooltip.stats_description,
			tooltip.stats_values,
			string.IsNullOrWhiteSpace(durabilityLabel) ? "耐久度" : durabilityLabel,
			string.IsNullOrWhiteSpace(durabilityValue) ? Toolbox.coloredText("100-100", "#FFB84A", false) : durabilityValue,
			ref markerWritten);

		// 原生装备提示依赖布局组件按行测量高度。自定义法宝词条写入后必须
		// 立刻重建，否则旧高度会让数行属性挤在同一位置。
		Canvas.ForceUpdateCanvases();
		if (tooltip.transform is RectTransform tooltipRect)
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);
		}

		_ = killsLabel;
		_ = killsValue;
	}

	private static void ExtractFaBaoNativeTailRows(
		Text descriptions,
		Text values,
		out string durabilityLabel,
		out string durabilityValue,
		out string killsLabel,
		out string killsValue)
	{
		durabilityLabel = string.Empty;
		durabilityValue = string.Empty;
		killsLabel = string.Empty;
		killsValue = string.Empty;
		if (descriptions == null || values == null)
		{
			return;
		}

		string[] descriptionLines = SplitTooltipStatLines(descriptions.text);
		string[] valueLines = SplitTooltipStatLines(values.text);
		int lineCount = Math.Max(descriptionLines.Length, valueLines.Length);
		List<string> keptDescriptions = new List<string>(lineCount);
		List<string> keptValues = new List<string>(lineCount);
		for (int i = 0; i < lineCount; i++)
		{
			string descriptionLine = i < descriptionLines.Length ? descriptionLines[i] : string.Empty;
			string valueLine = i < valueLines.Length ? valueLines[i] : string.Empty;
			string visibleDescription = NormalizeTooltipStatLine(descriptionLine);
			string visibleValue = NormalizeTooltipStatLine(valueLine);

			if (IsFaBaoDurabilityLine(visibleDescription))
			{
				durabilityLabel = StripVerticalSeparators(descriptionLine);
				durabilityValue = StripVerticalSeparators(valueLine);
				continue;
			}

			if (IsFaBaoKillsLine(visibleDescription))
			{
				killsLabel = descriptionLine;
				killsValue = valueLine;
				continue;
			}

			if (IsStandaloneVerticalSeparator(visibleDescription)
				|| IsStandaloneVerticalSeparator(visibleValue)
				|| IsFaBaoTooltipSeparator(visibleDescription, visibleValue))
			{
				continue;
			}

			// 原生装备行偶尔将列分隔符嵌在同一条富文本里；不能把原始行
			// 直接写回，否则会在玄鉴词条前残留极小的竖线。
			keptDescriptions.Add(StripVerticalSeparators(descriptionLine));
			keptValues.Add(StripVerticalSeparators(valueLine));
		}

		TrimTrailingEmptyTooltipRows(keptDescriptions, keptValues);
		descriptions.text = string.Join("\n", keptDescriptions);
		values.text = string.Join("\n", keptValues);
	}

	private static void AppendFaBaoTooltipStatRow(
		Text descriptions,
		Text values,
		string description,
		string value,
		ref bool markerWritten)
	{
		if (descriptions == null || values == null)
		{
			return;
		}

		EnsureTooltipStatLineBreak(descriptions);
		EnsureTooltipStatLineBreak(values);
		string marker = markerWritten ? string.Empty : FaBaoStatsMarker;
		descriptions.text += marker + StripVerticalSeparators(description ?? string.Empty) + "\n";
		values.text += marker + StripVerticalSeparators(value ?? string.Empty) + "\n";
		markerWritten = true;
	}

	private static string[] SplitTooltipStatLines(string text)
	{
		return string.IsNullOrEmpty(text)
			? Array.Empty<string>()
			: text.Replace("\r", string.Empty).Split('\n');
	}

	private static string NormalizeTooltipStatLine(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return string.Empty;
		}

		StringBuilder builder = new StringBuilder(line.Length);
		bool insideTag = false;
		for (int i = 0; i < line.Length; i++)
		{
			char current = line[i];
			if (current == '<')
			{
				insideTag = true;
				continue;
			}
			if (insideTag)
			{
				if (current == '>')
				{
					insideTag = false;
				}
				continue;
			}
			if (!char.IsWhiteSpace(current) && current != '\u200B' && current != '\u200C' && current != '\u200D' && current != '\uFEFF')
			{
				builder.Append(current);
			}
		}
		return builder.ToString();
	}

	private static string StripVerticalSeparators(string value)
	{
		if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
		return value
			.Replace("|", string.Empty)
			.Replace("│", string.Empty)
			.Replace("┃", string.Empty)
			.Replace("¦", string.Empty)
			.Replace("丨", string.Empty);
	}

	private static bool IsFaBaoDurabilityLine(string visibleDescription)
	{
		return visibleDescription.IndexOf("耐久", StringComparison.Ordinal) >= 0
			|| visibleDescription.IndexOf("durability", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsFaBaoKillsLine(string visibleDescription)
	{
		return visibleDescription.IndexOf("击杀", StringComparison.Ordinal) >= 0
			|| visibleDescription.IndexOf("kills", StringComparison.OrdinalIgnoreCase) >= 0
			|| visibleDescription.IndexOf("killcount", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsFaBaoTooltipSeparator(string visibleDescription, string visibleValue)
	{
		return (IsStandaloneVerticalSeparator(visibleDescription)
				&& (string.IsNullOrEmpty(visibleValue) || IsStandaloneVerticalSeparator(visibleValue)))
			|| (IsStandaloneVerticalSeparator(visibleValue)
				&& string.IsNullOrEmpty(visibleDescription));
	}

	private static bool IsStandaloneVerticalSeparator(string value)
	{
		if (string.IsNullOrEmpty(value) || value.Length > 4)
		{
			return false;
		}

		for (int i = 0; i < value.Length; i++)
		{
			char current = value[i];
			if (current != '|' && current != '│' && current != '┃' && current != '¦' && current != '丨')
			{
				return false;
			}
		}
		return true;
	}

	private static void TrimTrailingEmptyTooltipRows(List<string> descriptions, List<string> values)
	{
		while (descriptions.Count > 0 && values.Count > 0)
		{
			int last = descriptions.Count - 1;
			if (!string.IsNullOrWhiteSpace(descriptions[last]) || !string.IsNullOrWhiteSpace(values[last]))
			{
				break;
			}
			descriptions.RemoveAt(last);
			values.RemoveAt(last);
		}
	}

	private static void ShowFaBaoItemWindowAffixRows(ItemWindow window, string affixes)
	{
		if (window == null || string.IsNullOrWhiteSpace(affixes))
		{
			return;
		}

		string[] parts = SplitFaBaoAffixParts(affixes);
		for (int i = 0; i < parts.Length; i++)
		{
			if (!TrySplitAffix(parts[i], out string label, out string value))
			{
				continue;
			}

			ShowFaBaoItemWindowRow(window, label, "+" + value);
		}
	}

	private static void ShowFaBaoItemWindowNativeRows(ItemWindow window, Item item)
	{
		if (window == null || item == null)
		{
			return;
		}

		ShowFaBaoItemWindowNativeRow(window, item, "damage", "伤害", integer: true);
		ShowFaBaoItemWindowNativeRow(window, item, "attack_speed", "攻击", integer: true);
		ShowFaBaoItemWindowNativeRow(window, item, "health", "生命值", integer: true);
		ShowFaBaoItemWindowNativeRow(window, item, "armor", "护甲", integer: true);
		ShowFaBaoItemWindowNativeRow(window, item, "speed", "移速", integer: true);
		ShowFaBaoItemWindowNativeRow(window, item, "dodge", "闪避", percentage: true);
		ShowFaBaoItemWindowNativeRow(window, item, "crit", "暴击", percentage: true);
	}

	private static void ShowFaBaoItemWindowNativeRow(
		ItemWindow window,
		Item item,
		string statId,
		string label,
		bool integer = false,
		bool percentage = false)
	{
		float value = 0f;
		try
		{
			BaseStats stats = item.getFullStats();
			if (stats == null)
			{
				return;
			}
			value = stats[statId];
		}
		catch
		{
			return;
		}

		if (Math.Abs(value) < 0.0001f)
		{
			return;
		}

		string formatted = percentage
			? "+" + Math.Round(value * 100f).ToString("0") + "%"
			: integer
				? "+" + Math.Round(value).ToString("0")
				: "+" + value.ToString("0.##");
		ShowFaBaoItemWindowRow(window, label, formatted);
	}

	private static void ShowFaBaoItemWindowRow(ItemWindow window, string label, string value)
	{
		if (window == null || string.IsNullOrWhiteSpace(label))
		{
			return;
		}

		string originalLabel = StripVerticalSeparators(label.Trim());
		string safeLabel = CompactFaBaoAffixLabel(originalLabel);
		string safeValue = string.IsNullOrWhiteSpace(value) ? string.Empty : StripVerticalSeparators(value.Trim());
		XjNativeHoverTooltip.RegisterPassthrough(safeLabel, string.Equals(safeLabel, originalLabel, StringComparison.Ordinal) ? safeValue : originalLabel + " " + safeValue);
		try
		{
			window.showStatRow(safeLabel, safeValue, null, MetaType.None, -1L, false, null, null, null, false);
		}
		catch
		{
			try
			{
				window.showStatRow(safeLabel, safeValue, MetaType.None, -1L, null, null, null);
			}
			catch (System.Exception xjCaught537) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.FaBaoEquipment.cs:537", xjCaught537); }
		}
	}

	private static string CompactFaBaoAffixLabel(string label)
	{
		string safe = (label ?? string.Empty).Trim();
		if (safe.Length <= 8)
		{
			return safe;
		}

		if (safe.Contains("果位意象", StringComparison.Ordinal)) return "果位意象";
		if (safe.Contains("耐久", StringComparison.Ordinal)) return "耐久";
		if (safe.Contains("生命", StringComparison.Ordinal)) return "生命";
		if (safe.Contains("伤害", StringComparison.Ordinal)) return "伤害";
		if (safe.Contains("闪避", StringComparison.Ordinal)) return "闪避";
		if (safe.Contains("受暴击降低", StringComparison.Ordinal)) return "抗暴";
		if (safe.Contains("减伤", StringComparison.Ordinal)) return "减伤";
		if (safe.Contains("速度", StringComparison.Ordinal)) return "速度";
		if (safe.Contains("真元", StringComparison.Ordinal)) return "真元";
		return safe.Length <= 10 ? safe : safe.Substring(0, 10);
	}

	private static string[] SplitFaBaoAffixParts(string affixes)
	{
		if (string.IsNullOrWhiteSpace(affixes))
		{
			return Array.Empty<string>();
		}

		List<string> parts = new List<string>();
		string[] slashParts = affixes.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < slashParts.Length; i++)
		{
			string segment = slashParts[i];
			string[] legacyParts = segment.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
			if (legacyParts.Length == 1 && CountPlusSigns(segment) > 1)
			{
				legacyParts = segment.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
			}

			for (int j = 0; j < legacyParts.Length; j++)
			{
				string part = legacyParts[j].Trim();
				if (!string.IsNullOrWhiteSpace(part))
				{
					parts.Add(part);
				}
			}
		}

		return parts.ToArray();
	}

	private static int CountPlusSigns(string value)
	{
		int count = 0;
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] == '+') count++;
		}
		return count;
	}

	private static bool TrySplitAffix(string raw, out string label, out string value)
	{
		label = string.Empty;
		value = string.Empty;
		string part = (raw ?? string.Empty).Trim();
		int plus = part.IndexOf('+');
		if (plus <= 0 || plus >= part.Length - 1)
		{
			return false;
		}

		label = part.Substring(0, plus).Trim();
		value = part.Substring(plus + 1).Trim();
		return label.Length > 0 && value.Length > 0;
	}

	private static void StripFaBaoStats(Text text)
	{
		if (text == null || string.IsNullOrEmpty(text.text))
		{
			return;
		}

		int marker = text.text.IndexOf(FaBaoStatsMarker, StringComparison.Ordinal);
		int legacyMarker = text.text.IndexOf(LegacyFaBaoStatsMarker, StringComparison.Ordinal);
		if (legacyMarker >= 0 && (marker < 0 || legacyMarker < marker))
		{
			marker = legacyMarker;
		}
		if (marker >= 0)
		{
			text.text = text.text.Substring(0, marker).TrimEnd();
		}
	}

	private static void EnsureTooltipStatLineBreak(Text text)
	{
		if (text == null)
		{
			return;
		}

		if (string.IsNullOrEmpty(text.text) || text.text.EndsWith("\n", StringComparison.Ordinal))
		{
			return;
		}

		text.text += "\n";
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(ActorEquipmentSlot), "takeAwayItem")]
	private static bool XuanJianVNext_ActorEquipmentSlot_TakeAwayItem_BoundPrimaryGuard_Prefix(ActorEquipmentSlot __instance)
	{
		if (XjEquipmentForgeConsumer.IsControlledEquipmentChange) return true;
		Item equipped = __instance?.getItem();
		return equipped == null
			|| (!XjEquipmentForgeConsumer.IsBoundPrimaryRemovalLocked(equipped)
				&& !XjEquipmentForgeConsumer.IsLivingOwnerLocked(equipped));
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(ActorEquipmentSlot), "setItem")]
	private static bool XuanJianVNext_ActorEquipmentSlot_SetItem_FaBaoGuard_Prefix(
		ActorEquipmentSlot __instance,
		[HarmonyArgument("pItem")] ref Item item,
		[HarmonyArgument("pActor")] Actor actor)
	{
		if (actor?.data == null)
		{
			return true;
		}

		// 紫府本命灵宝与金丹本命法宝认主后不能被替换，也不能通过 setItem(null) 卸下。
		// 死亡清理与系统内部原位晋升使用受控通道，不在这里拦截。
		if (!XjEquipmentForgeConsumer.IsControlledEquipmentChange)
		{
			Item equipped = __instance?.getItem();
			if (equipped != null && !ReferenceEquals(equipped, item))
			{
				if (XjEquipmentForgeConsumer.IsBoundPrimaryRemovalLocked(equipped)
					|| XjEquipmentForgeConsumer.IsLivingOwnerLocked(equipped))
				{
					return false;
				}
			}
		}

		if (item?.data == null)
		{
			return true;
		}

		if (!XjWeaponArtSystem.CanEquipWeapon(actor, item))
		{
			return false;
		}

		if (!XjEquipmentForgeConsumer.CanEquipXuanJianFaBao(item, actor))
		{
			return false;
		}

		XjEquipmentForgeConsumer.InitializeGrantedFaBao(item, actor);
		if (item?.data == null)
			return true;

		item.data.get("xuanjian.fabao", out int marker, 0);
		if (marker != 1)
			return true;

		// 本命灵宝/本命法宝一经认主，永远不能装备到其他角色身上。
		// 原主人死亡后的器物去向由死亡档案与遗失池处理，不通过原生赠与链继承。
		item.data.get("xuanjian.fabao.owner_id", out long ownerId, 0L);
		if (ownerId <= 0L || ownerId == ((BaseSystemData)actor.data).id) return true;
		if (XjFaBaoEquipmentSync.IsBoundPrimaryWeapon(item)) return false;

		// 非本命槽位器物保持旧逻辑：原主人仍存活时禁止转移，死亡后可以重新认主。
		if (XjSafeCore.IsAliveActor(XjScheduler.ResolveActor(ownerId, out Actor owner) ? owner : null)) return false;
		item.data.set("xuanjian.fabao.owner_id", 0L);
		return true;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(ActorEquipmentSlot), "setItem")]
	private static void XuanJianVNext_ActorEquipmentSlot_SetItem_FaBaoClaim_Postfix(
		ActorEquipmentSlot __instance,
		[HarmonyArgument("pItem")] Item item,
		[HarmonyArgument("pActor")] Actor actor)
	{
		XjInternalEventBus.PublishNativeEquipmentSlotChanged(__instance, item, actor);
	}
}
