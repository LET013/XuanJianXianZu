using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Chronicle;

internal static class XjFamilyChronicleDisplayFormatter
{
	internal const string EmptyDisplayText = "族谱尚无大事入卷。";
	private const int MaxDisplayEntries = 50;
	internal static readonly string[] OrderedTypeLabels =
	{
		"破境", "传承", "身故", "族仇", "斗法", "族事"
	};

	internal readonly struct ChronicleUiEntry
	{
		internal readonly int Year;
		internal readonly string TypeLabel;
		internal readonly string Text;

		internal ChronicleUiEntry(int year, string typeLabel, string text)
		{
			Year = Math.Max(0, year);
			TypeLabel = string.IsNullOrWhiteSpace(typeLabel) ? "族事" : typeLabel.Trim();
			Text = string.IsNullOrWhiteSpace(text) ? EmptyDisplayText : text.Trim();
		}
	}

	internal static ChronicleUiEntry[] BuildEntriesForActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return Array.Empty<ChronicleUiEntry>();
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out long familyId) || familyId <= 0L)
		{
			return Array.Empty<ChronicleUiEntry>();
		}

		IReadOnlyList<XjChronicleEvent> events = XjChronicleReadModel.Shared.ReadFamilyChronicle(familyId);
		var list = new List<ChronicleUiEntry>(Math.Min(events.Count, MaxDisplayEntries));
		int start = Math.Max(0, events.Count - MaxDisplayEntries);
		for (int i = start; i < events.Count; i++)
		{
			XjChronicleEvent entry = events[i];
			if (entry == null || !entry.Found || string.IsNullOrWhiteSpace(entry.Summary))
			{
				continue;
			}

			list.Add(new ChronicleUiEntry(entry.Timestamp, ResolveTypeLabel(entry.EventType), entry.Summary));
		}
		return list.ToArray();
	}

	internal static string[] BuildItemsForFamily(long familyId)
	{
		IReadOnlyList<XjChronicleEvent> events = XjChronicleReadModel.Shared.ReadFamilyChronicle(familyId);
		var list = new List<string>(Math.Min(events.Count, MaxDisplayEntries));
		int start = Math.Max(0, events.Count - MaxDisplayEntries);
		for (int i = start; i < events.Count; i++)
		{
			XjChronicleEvent entry = events[i];
			if (entry != null && entry.Found && !string.IsNullOrWhiteSpace(entry.Summary))
			{
				list.Add(entry.Summary);
			}
		}
		return list.ToArray();
	}

	private static string ResolveTypeLabel(string eventType)
	{
		return eventType switch
		{
			"BreakthroughSuccess:JinDan" or "BreakthroughSuccess:ZiFu" or "BreakthroughSuccess:ZhuJi"
				or "RealmBreakthrough" or "JinDanSucceeded" or "ShenDanSucceeded" or "JieLinSucceeded" or "JinXingObtained" => "破境",
			"GongFaGenerated" or "CaiQiFaObtained" or "QiuJinFaComprehended" or "FaBaoObtained"
				or "FaBaoUpgraded" or "ZongMenBackflow" or "GongFaLost" or "LingWuAppeared"
				or "JinDanResidualAcquired" => "传承",
			"ActorDied" or "JinDanFailureDemonized" or "DongTianDeath" or "JinDanResidualAppeared" => "身故",
			"FamilyVendettaCreated" or "FamilyVendettaAvenged" or "FamilyVendettaFailed" or "FamilyVendettaCounterKilled" or "FamilyVendettaClosed" => "族仇",
			"HighTierSpellTriggered" or "RenDanRefined" or "BreakthroughBlocked"
				or "DongTianSurvived" => "斗法",
			_ => "族事"
		};
	}
}

internal static class XjFamilyChronicleWindow
{
	private const string WindowId = "XuanJianChronicleWindow";
	private const float WindowWidth = 460f;
	private const float WindowHeight = 340f;
	private const float LeftPanelWidth = 104f;

	private static ScrollWindow _window;
	private static Font _font;
	private static RectTransform _root;
	private static RectTransform _typeContent;
	private static RectTransform _entryContent;
	private static ScrollRect _typeScroll;
	private static ScrollRect _entryScroll;
	private static Text _emptyText;
	private static Text _countText;
	private static bool _initialized;
	private static string _currentTypeLabel = string.Empty;
	private static XjFamilyChronicleDisplayFormatter.ChronicleUiEntry[] _currentEntries =
		Array.Empty<XjFamilyChronicleDisplayFormatter.ChronicleUiEntry>();

	internal static void ShowForActor(Actor actor)
	{
		bool createdNow = !_initialized;
		EnsureInit();
		if (_window == null)
		{
			return;
		}

		_currentEntries = XjFamilyChronicleDisplayFormatter.BuildEntriesForActor(actor);
		_currentTypeLabel = string.Empty;
		Refresh();
		XjWindowOpenGuard.Show(_window, WindowId, createdNow);
		ForceRebuild();
		ResetScrollPositions();
	}

	private static void EnsureInit()
	{
		if (_initialized)
		{
			return;
		}

		_window = WindowCreator.CreateEmptyWindow(WindowId, "xuanjian.family.chronicle", "ui/icons/iconJianBook");
		if (_window == null)
		{
			return;
		}

		Transform background = _window.transform.Find("Background");
		if (background == null)
		{
			return;
		}

		RectTransform backgroundRect = background.GetComponent<RectTransform>();
		if (backgroundRect != null)
		{
			backgroundRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
		}

		_font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		BuildStaticLayout(background);
		_initialized = true;
	}

	private static void BuildStaticLayout(Transform background)
	{
		GameObject rootObject = new GameObject("ChronicleRoot", typeof(RectTransform));
		rootObject.transform.SetParent(background, false);
		_root = rootObject.GetComponent<RectTransform>();
		_root.anchorMin = Vector2.zero;
		_root.anchorMax = Vector2.one;
		_root.offsetMin = new Vector2(10f, 12f);
		_root.offsetMax = new Vector2(-10f, -54f);

		GameObject leftPanel = CreatePanel("TypePanel", _root, new Color(0f, 0f, 0f, 0.16f));
		RectTransform leftRect = leftPanel.GetComponent<RectTransform>();
		leftRect.anchorMin = new Vector2(0f, 0f);
		leftRect.anchorMax = new Vector2(0f, 1f);
		leftRect.pivot = new Vector2(0f, 0.5f);
		leftRect.anchoredPosition = Vector2.zero;
		leftRect.sizeDelta = new Vector2(LeftPanelWidth, 0f);

		Text typeTitle = CreateText(leftPanel.transform, "TypeTitle", "纪事分类", 11, TextAnchor.MiddleCenter,
			new Color(1f, 0.72f, 0.18f), FontStyle.Bold);
		SetRect(typeTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
			new Vector2(0f, -5f), new Vector2(0f, 24f));
		CreateTypeScroll(leftPanel.transform);

		GameObject rightPanel = CreatePanel("EntryPanel", _root, new Color(0f, 0f, 0f, 0.08f));
		RectTransform rightRect = rightPanel.GetComponent<RectTransform>();
		rightRect.anchorMin = Vector2.zero;
		rightRect.anchorMax = Vector2.one;
		rightRect.offsetMin = new Vector2(LeftPanelWidth + 8f, 0f);
		rightRect.offsetMax = Vector2.zero;

		_countText = CreateText(rightPanel.transform, "Count", "共 0 条", 9, TextAnchor.MiddleLeft,
			new Color(0.72f, 0.72f, 0.72f), FontStyle.Normal);
		SetRect(_countText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
			new Vector2(8f, -5f), new Vector2(-16f, 20f));
		CreateEntryScroll(rightPanel.transform);

		_emptyText = CreateText(rightPanel.transform, "Empty", XjFamilyChronicleDisplayFormatter.EmptyDisplayText,
			14, TextAnchor.MiddleCenter, new Color(0.68f, 0.68f, 0.68f), FontStyle.Normal);
		_emptyText.horizontalOverflow = HorizontalWrapMode.Wrap;
		_emptyText.verticalOverflow = VerticalWrapMode.Overflow;
		SetRect(_emptyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
			new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(250f, 70f));
	}

	private static void CreateTypeScroll(Transform parent)
	{
		_typeScroll = CreateScroll("TypeScroll", parent, new Vector2(5f, 6f), new Vector2(-5f, -32f), out _typeContent);
		VerticalLayoutGroup layout = _typeContent.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.childAlignment = TextAnchor.UpperCenter;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		layout.spacing = 5f;
		layout.padding = new RectOffset(2, 2, 4, 4);
		ContentSizeFitter fitter = _typeContent.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
	}

	private static void CreateEntryScroll(Transform parent)
	{
		_entryScroll = CreateScroll("EntryScroll", parent, new Vector2(6f, 6f), new Vector2(-6f, -28f), out _entryContent);
		VerticalLayoutGroup layout = _entryContent.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.childAlignment = TextAnchor.UpperLeft;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		layout.spacing = 7f;
		layout.padding = new RectOffset(4, 4, 5, 6);
		ContentSizeFitter fitter = _entryContent.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
	}

	private static ScrollRect CreateScroll(
		string name,
		Transform parent,
		Vector2 offsetMin,
		Vector2 offsetMax,
		out RectTransform content)
	{
		GameObject scrollObject = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
		scrollObject.transform.SetParent(parent, false);
		RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
		scrollRectTransform.anchorMin = Vector2.zero;
		scrollRectTransform.anchorMax = Vector2.one;
		scrollRectTransform.offsetMin = offsetMin;
		scrollRectTransform.offsetMax = offsetMax;

		GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
		viewportObject.transform.SetParent(scrollObject.transform, false);
		RectTransform viewport = viewportObject.GetComponent<RectTransform>();
		viewport.anchorMin = Vector2.zero;
		viewport.anchorMax = Vector2.one;
		viewport.offsetMin = Vector2.zero;
		viewport.offsetMax = Vector2.zero;

		GameObject contentObject = new GameObject("Content", typeof(RectTransform));
		contentObject.transform.SetParent(viewportObject.transform, false);
		content = contentObject.GetComponent<RectTransform>();
		content.anchorMin = new Vector2(0f, 1f);
		content.anchorMax = new Vector2(1f, 1f);
		content.pivot = new Vector2(0.5f, 1f);
		content.anchoredPosition = Vector2.zero;
		content.sizeDelta = Vector2.zero;

		ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
		scroll.horizontal = false;
		scroll.vertical = true;
		scroll.movementType = ScrollRect.MovementType.Clamped;
		scroll.scrollSensitivity = 26f;
		scroll.viewport = viewport;
		scroll.content = content;
		return scroll;
	}

	internal static void Refresh()
	{
		if (_typeContent == null || _entryContent == null)
		{
			return;
		}

		ClearChildren(_typeContent);
		ClearChildren(_entryContent);
		BuildTypeButtons();

		int shownCount = 0;
		for (int i = 0; i < _currentEntries.Length; i++)
		{
			XjFamilyChronicleDisplayFormatter.ChronicleUiEntry entry = _currentEntries[i];
			if (!string.IsNullOrEmpty(_currentTypeLabel)
				&& !string.Equals(entry.TypeLabel, _currentTypeLabel, StringComparison.Ordinal))
			{
				continue;
			}

			CreateEntryCard(_entryContent, entry);
			shownCount++;
		}

		if (_countText != null)
		{
			_countText.text = string.IsNullOrEmpty(_currentTypeLabel)
				? "共 " + shownCount + " 条"
				: "【" + _currentTypeLabel + "】共 " + shownCount + " 条";
		}

		if (_emptyText != null)
		{
			_emptyText.text = _currentEntries.Length == 0
				? XjFamilyChronicleDisplayFormatter.EmptyDisplayText
				: "该分类暂无纪事。";
			_emptyText.gameObject.SetActive(shownCount == 0);
			_emptyText.transform.SetAsLastSibling();
		}

		ForceRebuild();
		ResetScrollPositions();
	}

	private static void BuildTypeButtons()
	{
		CreateTypeButton(_typeContent, "全部", string.IsNullOrEmpty(_currentTypeLabel), () =>
		{
			if (string.IsNullOrEmpty(_currentTypeLabel))
			{
				return;
			}
			_currentTypeLabel = string.Empty;
			Refresh();
		});

		for (int i = 0; i < XjFamilyChronicleDisplayFormatter.OrderedTypeLabels.Length; i++)
		{
			string label = XjFamilyChronicleDisplayFormatter.OrderedTypeLabels[i];
			CreateTypeButton(_typeContent, label, string.Equals(_currentTypeLabel, label, StringComparison.Ordinal), () =>
			{
				if (string.Equals(_currentTypeLabel, label, StringComparison.Ordinal))
				{
					return;
				}
				_currentTypeLabel = label;
				Refresh();
			});
		}
	}

	private static void CreateTypeButton(Transform parent, string text, bool selected, Action onClick)
	{
		GameObject buttonObject = new GameObject("Type_" + text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
		buttonObject.transform.SetParent(parent, false);
		LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
		layout.minHeight = 27f;
		layout.preferredHeight = 27f;

		Image image = buttonObject.GetComponent<Image>();
		image.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		image.type = Image.Type.Sliced;
		image.color = selected ? new Color(0.34f, 0.42f, 0.48f, 1f) : new Color(0.12f, 0.12f, 0.12f, 0.84f);

		Button button = buttonObject.GetComponent<Button>();
		button.targetGraphic = image;
		button.transition = Selectable.Transition.None;
		button.onClick.AddListener(() => onClick?.Invoke());

		Text label = CreateText(buttonObject.transform, "Label", text, 10, TextAnchor.MiddleCenter, Color.white,
			selected ? FontStyle.Bold : FontStyle.Normal);
		SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
			new Vector2(4f, 0f), new Vector2(-4f, 0f));
	}

	private static void CreateEntryCard(
		Transform parent,
		in XjFamilyChronicleDisplayFormatter.ChronicleUiEntry entry)
	{
		GameObject card = new GameObject("ChronicleEntry", typeof(RectTransform), typeof(Image),
			typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
		card.transform.SetParent(parent, false);
		Image background = card.GetComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
		background.type = Image.Type.Sliced;
		background.color = new Color(0.08f, 0.09f, 0.11f, 0.34f);

		VerticalLayoutGroup cardLayout = card.GetComponent<VerticalLayoutGroup>();
		cardLayout.childAlignment = TextAnchor.UpperLeft;
		cardLayout.childControlWidth = true;
		cardLayout.childControlHeight = true;
		cardLayout.childForceExpandWidth = true;
		cardLayout.childForceExpandHeight = false;
		cardLayout.padding = new RectOffset(8, 8, 6, 7);
		cardLayout.spacing = 4f;

		ContentSizeFitter fitter = card.GetComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		card.GetComponent<LayoutElement>().minHeight = 48f;

		string heading = entry.Year > 0
			? "玄历" + entry.Year + "年 · 【" + entry.TypeLabel + "】"
			: "【" + entry.TypeLabel + "】";
		Text type = CreateAutoHeightText(card.transform, "Type", heading, 11,
			new Color(1f, 0.72f, 0.18f), FontStyle.Bold);
		type.alignment = TextAnchor.UpperLeft;
		CreateAutoHeightText(card.transform, "Body", entry.Text, 11,
			new Color(0.86f, 0.84f, 0.78f), FontStyle.Normal);
	}

	private static Text CreateAutoHeightText(
		Transform parent,
		string name,
		string text,
		int fontSize,
		Color color,
		FontStyle style)
	{
		Text label = CreateText(parent, name, text, fontSize, TextAnchor.UpperLeft, color, style);
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		ContentSizeFitter fitter = label.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		return label;
	}

	private static GameObject CreatePanel(string name, Transform parent, Color color)
	{
		GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
		panel.transform.SetParent(parent, false);
		Image image = panel.GetComponent<Image>();
		image.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
		image.type = Image.Type.Sliced;
		image.color = color;
		return panel;
	}

	private static Text CreateText(
		Transform parent,
		string name,
		string text,
		int fontSize,
		TextAnchor alignment,
		Color color,
		FontStyle style)
	{
		GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
		textObject.transform.SetParent(parent, false);
		Text label = textObject.GetComponent<Text>();
		label.font = _font;
		label.fontSize = fontSize;
		label.fontStyle = style;
		label.alignment = alignment;
		label.color = color;
		label.raycastTarget = false;
		label.supportRichText = false;
		label.text = text ?? string.Empty;
		return label;
	}

	private static void SetRect(
		RectTransform rect,
		Vector2 anchorMin,
		Vector2 anchorMax,
		Vector2 pivot,
		Vector2 anchoredPosition,
		Vector2 sizeDelta,
		Vector2? offsetMin = null,
		Vector2? offsetMax = null)
	{
		rect.anchorMin = anchorMin;
		rect.anchorMax = anchorMax;
		rect.pivot = pivot;
		rect.anchoredPosition = anchoredPosition;
		rect.sizeDelta = sizeDelta;
		if (offsetMin.HasValue)
		{
			rect.offsetMin = offsetMin.Value;
		}
		if (offsetMax.HasValue)
		{
			rect.offsetMax = offsetMax.Value;
		}
	}

	private static void ClearChildren(Transform parent)
	{
		if (parent == null)
		{
			return;
		}

		for (int i = parent.childCount - 1; i >= 0; i--)
		{
			GameObject child = parent.GetChild(i).gameObject;
			child.SetActive(false);
			UnityEngine.Object.Destroy(child);
		}
	}

	private static void ForceRebuild()
	{
		Canvas.ForceUpdateCanvases();
		if (_typeContent != null)
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(_typeContent);
		}
		if (_entryContent != null)
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(_entryContent);
		}
		if (_root != null)
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
		}
	}

	private static void ResetScrollPositions()
	{
		if (_typeScroll != null)
		{
			_typeScroll.verticalNormalizedPosition = 1f;
		}
		if (_entryScroll != null)
		{
			_entryScroll.verticalNormalizedPosition = 1f;
		}
		if (_typeContent != null)
		{
			_typeContent.anchoredPosition = Vector2.zero;
		}
		if (_entryContent != null)
		{
			_entryContent.anchoredPosition = Vector2.zero;
		}
	}
}
