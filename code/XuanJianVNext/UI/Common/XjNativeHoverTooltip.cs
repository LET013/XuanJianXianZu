using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.UI.Common;

/// <summary>
/// Configures WorldBox's native TipButton tooltip fields using the same
/// passthrough-localization approach as 0.5.4.
/// </summary>
internal static class XjNativeHoverTooltip
{
	private const int MaxRuntimeTextSlots = 2048;
	private const int MaxRawPassthroughTextKeys = 2048;
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

		string details = TryGetStringMember(tip, "text_description_2");
		Ensure(tip, tip.textOnClick ?? string.Empty, tip.textOnClickDescription ?? string.Empty, details);
	}

	internal static void Ensure(TipButton tip, string title, string description, string details)
	{
		if (tip == null)
		{
			return;
		}

		string safeTitle = ResolveDisplayText(title);
		string safeDescription = ResolveDisplayText(description);
		string safeDetails = ResolveDisplayText(details);
		EnsureNativeButton(tip);
		// Native tooltip entry points are inconsistent: some localize these fields,
		// while others display them verbatim. Register the text as a passthrough key,
		// but store the actual text in every field so runtime slot keys never leak.
		EnsurePassthrough(safeTitle, safeDescription, safeDetails);
		tip.textOnClick = safeTitle;
		tip.textOnClickDescription = safeDescription;
		TrySetStringMember(tip, "text_description_2", safeDetails);
		TrySetStringMember(tip, "textOnHover", safeTitle);
		TrySetStringMember(tip, "text_on_hover", safeTitle);
		TrySetStringMember(tip, "text_description", safeDescription);
		TrySetStringMember(tip, "description", safeDetails);
		TrySetStringMember(tip, "tip", safeTitle);
	}

	internal static void RepairHierarchy(Transform root)
	{
		if (root == null) return;
		TipButton[] tips = root.GetComponentsInChildren<TipButton>(true);
		for (int i = 0; i < tips.Length; i++) Ensure(tips[i]);
	}

	internal static string ResolveDisplayText(string value)
	{
		string text = value ?? string.Empty;
		const string prefix = "xuanjian.runtime.tooltip.slot.";
		if (!text.StartsWith(prefix, StringComparison.Ordinal)) return text;
		if (!int.TryParse(text.Substring(prefix.Length), out int slot)
			|| slot < 0 || slot >= RuntimeSlotValues.Length)
		{
			return string.Empty;
		}
		string runtimeValue = RuntimeSlotValues[slot];
		if (!string.IsNullOrWhiteSpace(runtimeValue)) return runtimeValue;
		try
		{
			string localized = LM.Get(text);
			return string.IsNullOrWhiteSpace(localized) || string.Equals(localized, text, StringComparison.Ordinal)
				? string.Empty
				: localized;
		}
		catch
		{
			return string.Empty;
		}
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
		return "xuanjian.runtime.tooltip.slot." + slot.ToString("D4");
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
			// 固定槽允许覆盖旧动态文本；即使其他路径先注册，也不再创建新 key。
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
		catch (System.Exception xjCaught173) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Common/XjNativeHoverTooltip.cs:173", xjCaught173); }
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
		if (RegisteredTexts.Count > MaxRawPassthroughTextKeys)
		{
			RegisteredTexts.Remove(text);
			return;
		}
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

	private static string TryGetStringMember(object instance, string memberName)
	{
		if (instance == null || string.IsNullOrWhiteSpace(memberName)) return string.Empty;
		return XjNativeReflectionInterop.ReadMemberValue(instance, memberName) as string ?? string.Empty;
	}

	private static void TrySetStringMember(object instance, string memberName, string value)
	{
		if (instance == null || string.IsNullOrWhiteSpace(memberName)) return;
		XjNativeReflectionInterop.TryWriteMemberValue(instance, memberName, value ?? string.Empty);
	}
}
