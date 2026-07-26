using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;

namespace XuanJianVNext.UI.Common;

/// <summary>
/// Configures WorldBox's native TipButton tooltip fields using the same
/// passthrough-localization approach as 0.5.4.
/// </summary>
internal static class XjNativeHoverTooltip
{
	private const string RuntimeSlotPrefix = "xuanjian.runtime.tooltip.slot.";
	private const int MaxRuntimeTextSlots = 2048;
	private static readonly HashSet<string> RegisteredTexts = new HashSet<string>(StringComparer.Ordinal);
	private static readonly Dictionary<string, int> RuntimeSlotBySignature = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly string[] RuntimeSlotSignatures = new string[MaxRuntimeTextSlots];
	private static readonly string[] RuntimeSlotValues = new string[MaxRuntimeTextSlots];
	private static readonly Dictionary<string, string> RegisteredRuntimeValues = new Dictionary<string, string>(StringComparer.Ordinal);
	private static int _nextRuntimeSlot;
	private static LocalizedTextManager _lastManager;

	internal static void Ensure(TipButton tip)
	{
		if (tip == null)
		{
			return;
		}

		Ensure(tip, tip.textOnClick ?? string.Empty, tip.textOnClickDescription ?? string.Empty, string.Empty);
	}

	internal static void Ensure(TipButton tip, string title, string description, string details)
	{
		if (tip == null)
		{
			return;
		}

		string safeTitle = NormalizeDisplayText(title);
		string safeDescription = NormalizeDisplayText(description);
		string safeDetails = NormalizeDisplayText(details);
		EnsureNativeButton(tip);
		EnsurePassthrough(safeTitle, safeDescription, safeDetails);

		// Native tooltip paths differ between WorldBox tabs. Store direct text as
		// passthrough keys so unresolved runtime slot ids never leak to players.
		tip.textOnClick = safeTitle;
		tip.textOnClickDescription = safeDescription;
		TrySetStringMember(tip, "text_description_2", safeDetails);
		ApplyDirectTooltipFallbacks(tip, safeTitle, safeDescription, safeDetails);
	}

	private static void ApplyDirectTooltipFallbacks(TipButton tip, string title, string description, string details)
	{
		TrySetStringMember(tip, "textOnHover", title);
		TrySetStringMember(tip, "text_on_hover", title);
		TrySetStringMember(tip, "text_description", description);
		TrySetStringMember(tip, "description", details);
		TrySetStringMember(tip, "tip", title);
		TrySetStringMember(tip, "tooltip", title);
		TrySetStringMember(tip, "tooltipTitle", title);
		TrySetStringMember(tip, "tooltip_title", title);
		TrySetStringMember(tip, "tooltipDescription", description);
		TrySetStringMember(tip, "tooltip_description", description);
	}

	internal static void RegisterPassthrough(params string[] texts)
	{
		EnsurePassthrough(texts);
	}

	private static string RegisterRuntimeText(string role, string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;
		string cleanRole = string.IsNullOrWhiteSpace(role) ? "text" : role.Trim();
		string signature = cleanRole + "|" + text;
		if (!RuntimeSlotBySignature.TryGetValue(signature, out int slot))
		{
			slot = _nextRuntimeSlot++ % MaxRuntimeTextSlots;
			string previousSignature = RuntimeSlotSignatures[slot];
			if (!string.IsNullOrWhiteSpace(previousSignature)) RuntimeSlotBySignature.Remove(previousSignature);
			RuntimeSlotSignatures[slot] = signature;
			RuntimeSlotValues[slot] = text;
			RuntimeSlotBySignature[signature] = slot;
		}

		string key = BuildRuntimeSlotKey(slot);
		LocalizedTextManager manager = LocalizedTextManager.instance;
		if (manager != null)
		{
			if (!ReferenceEquals(_lastManager, manager))
			{
				RegisteredTexts.Clear();
				RegisteredRuntimeValues.Clear();
				_lastManager = manager;
				FlushRuntimeTexts(manager);
			}
			RegisterRuntimeKey(key, text);
		}
		return key;
	}

	private static string BuildRuntimeSlotKey(int slot)
	{
		return RuntimeSlotPrefix + slot.ToString("D4");
	}

	internal static string NormalizeDisplayText(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;
		string value = text.Trim();
		if (!value.StartsWith(RuntimeSlotPrefix, StringComparison.Ordinal)) return value;
		string suffix = value.Substring(RuntimeSlotPrefix.Length);
		if (int.TryParse(suffix, out int slot)
			&& slot >= 0
			&& slot < RuntimeSlotValues.Length
			&& !string.IsNullOrWhiteSpace(RuntimeSlotValues[slot]))
		{
			return RuntimeSlotValues[slot];
		}
		return string.Empty;
	}
	private static void FlushRuntimeTexts(LocalizedTextManager manager)
	{
		if (manager == null) return;
		for (int slot = 0; slot < RuntimeSlotValues.Length; slot++)
		{
			string value = RuntimeSlotValues[slot];
			if (!string.IsNullOrWhiteSpace(value)) RegisterRuntimeKey(BuildRuntimeSlotKey(slot), value);
		}
	}

	private static void RegisterRuntimeKey(string key, string value)
	{
		if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
		if (RegisteredRuntimeValues.TryGetValue(key, out string registered)
			&& string.Equals(registered, value, StringComparison.Ordinal)) return;
		try
		{
			LocalizedTextManager.add(key, value, false, string.Empty, true);
			RegisteredRuntimeValues[key] = value;
		}
		catch
		{
			// 固定槽允许覆盖旧动态文本；其他路径先注册时不再创建新 key。
		}
	}

	private static void EnsureNativeButton(TipButton tip)
	{
		try
		{
			Button button = tip.GetComponent<Button>() ?? tip.gameObject.AddComponent<Button>();
			button.transition = Selectable.Transition.ColorTint;
			if (button.targetGraphic == null)
			{
				button.targetGraphic = tip.GetComponent<Graphic>();
			}
		}
		catch
		{
		}
	}

	private static void EnsurePassthrough(params string[] texts)
	{
		LocalizedTextManager manager = LocalizedTextManager.instance;
		if (manager == null || texts == null)
		{
			return;
		}

		if (!ReferenceEquals(_lastManager, manager))
		{
			RegisteredTexts.Clear();
			RegisteredRuntimeValues.Clear();
			_lastManager = manager;
			FlushRuntimeTexts(manager);
		}

		for (int i = 0; i < texts.Length; i++)
		{
			RegisterText(texts[i]);
		}
	}

	private static void RegisterText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}

		RegisterSingleText(text);
		string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			string line = lines[lineIndex]?.Trim();
			if (!string.IsNullOrWhiteSpace(line)) RegisterSingleText(line);
		}
	}

	private static void RegisterSingleText(string text)
	{
		if (string.IsNullOrWhiteSpace(text) || !RegisteredTexts.Add(text)) return;
		try
		{
			LocalizedTextManager.add(text, text, false, string.Empty, true);
		}
		catch
		{
			// Keep the key cached even when another module registered it first.
			// Repeated registration only produces noisy "already exists" warnings.
		}
	}

	private static void TrySetStringMember(object instance, string memberName, string value)
	{
		if (instance == null || string.IsNullOrWhiteSpace(memberName))
		{
			return;
		}

		try
		{
			Type type = instance.GetType();
			FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.FieldType == typeof(string))
			{
				field.SetValue(instance, value ?? string.Empty);
				return;
			}

			PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite && property.PropertyType == typeof(string))
			{
				property.SetValue(instance, value ?? string.Empty, null);
			}
		}
		catch
		{
		}
	}
}

