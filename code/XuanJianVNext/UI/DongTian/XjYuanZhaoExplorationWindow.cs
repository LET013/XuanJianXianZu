using System;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.UI.ActorInfo;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.DongTian;

/// <summary>
/// “照真请凭函”玩家交互窗口。
///
/// UI 原则：
/// - 叙事正文独立滚动，正文再长也不得挤压选择按钮；
/// - 三个事件选择固定在窗口底部安全区，收念/关闭再单独占一行；
/// - 所有关键按钮都采用锚点布局，不依赖正文高度，不会因文本溢出而被遮住；
/// - 仅驱动 XjYuanZhaoCredentialSystem 的持久化选择状态，不移动 Actor，
///   不创建洞天地图角色，也不把道尊实体化。
/// </summary>
internal static class XjYuanZhaoExplorationWindow
{
	private const string WindowId = "XuanJianYuanZhaoExplorationWindow";
	private const float PreferredWindowWidth = 460f;
	private const float PreferredWindowHeight = 520f;
	private const float MinimumWindowWidth = 360f;
	private const float MinimumWindowHeight = 430f;

	// 固定安全区。正文只允许存在于 BodyTop ~ BodyBottom 之间。
	private const float HeaderTop = 38f;
	private const float HeaderHeight = 52f;
	private const float BodyTop = 98f;
	private const float BodyBottom = 154f;
	private const float StatusBottom = 124f;
	private const float StatusHeight = 24f;
	private const float ChoiceHeight = 24f;
	private const float ChoiceGap = 4f;
	private const float ChoiceBottom0 = 94f;
	private const float ChoiceBottom1 = ChoiceBottom0 - ChoiceHeight - ChoiceGap;
	private const float ChoiceBottom2 = ChoiceBottom1 - ChoiceHeight - ChoiceGap;
	private const float FooterBottom = 7f;
	private const float FooterHeight = 23f;

	private static readonly Color ColorBackdrop = new Color(0.035f, 0.075f, 0.090f, 0.46f);
	private static readonly Color ColorHeader = new Color(0.055f, 0.150f, 0.180f, 0.94f);
	private static readonly Color ColorCard = new Color(0.035f, 0.100f, 0.125f, 0.88f);
	private static readonly Color ColorStatus = new Color(0.070f, 0.170f, 0.190f, 0.92f);
	private static readonly Color ColorLine = new Color(0.48f, 0.78f, 0.86f, 0.72f);
	private static readonly Color ColorTitle = new Color(0.92f, 0.86f, 0.63f, 1f);
	private static readonly Color ColorText = new Color(0.94f, 0.98f, 1f, 1f);
	private static readonly Color ColorMuted = new Color(0.72f, 0.83f, 0.86f, 1f);

	private static ScrollWindow _window;
	private static RectTransform _backgroundRect;
	private static RectTransform _bodyContentRect;
	private static ScrollRect _bodyScroll;
	private static Text _titleText;
	private static Text _kickerText;
	private static Text _bodyText;
	private static Text _statusText;
	private static Button _choice0;
	private static Button _choice1;
	private static Button _choice2;
	private static Button _endButton;
	private static Button _closeButton;
	private static long _actorId;
	private static string _localStatus = string.Empty;
	private static string _lastRenderedTitle = string.Empty;

	internal static void Show(long actorId)
	{
		_actorId = actorId;
		_localStatus = string.Empty;
		bool createdNow = _window == null;
		EnsureWindow();
		ApplyAdaptiveWindowSize();
		Refresh();
		XjWindowOpenGuard.Show(_window, WindowId, createdNow);
	}

	private static void EnsureWindow()
	{
		if (_window != null) return;
		_window = WindowCreator.CreateEmptyWindow(WindowId, "xuanjian.yuanzhao.exploration", "ui/Icons/event/DongTianOpen");
		Transform background = _window.transform.Find("Background");
		if (background == null) return;
		_backgroundRect = background.GetComponent<RectTransform>();
		ApplyAdaptiveWindowSize();

		CreateBackdrop(background);
		CreateHeader(background);
		CreateBodyScroll(background);
		CreateStatusBar(background);

		_choice0 = CreateChoiceButton(background, "Choice0", ChoiceBottom0, new Color(0.085f, 0.315f, 0.365f, 0.98f));
		_choice1 = CreateChoiceButton(background, "Choice1", ChoiceBottom1, new Color(0.070f, 0.260f, 0.315f, 0.98f));
		_choice2 = CreateChoiceButton(background, "Choice2", ChoiceBottom2, new Color(0.055f, 0.215f, 0.270f, 0.98f));

		_closeButton = CreateFooterButton(background, "Close", "关闭", true, new Color(0.12f, 0.19f, 0.21f, 0.98f));
		_endButton = CreateFooterButton(background, "End", "收念归身", false, new Color(0.38f, 0.25f, 0.09f, 0.98f));
		_closeButton.onClick.AddListener(() => ScrollWindow.showWindow(WindowId));
	}

	private static void ApplyAdaptiveWindowSize()
	{
		if (_backgroundRect == null) return;
		float width = PreferredWindowWidth;
		float height = PreferredWindowHeight;
		Canvas canvas = _backgroundRect.GetComponentInParent<Canvas>();
		RectTransform canvasRect = canvas == null ? null : canvas.GetComponent<RectTransform>();
		if (canvasRect != null && canvasRect.rect.width > 0f && canvasRect.rect.height > 0f)
		{
			// 给窗口四周至少留一圈可拖拽/关闭余量。正文区域本身可滚动，因此宁可缩正文，
			// 也不让窗口底部动作区跑出画布。
			width = Mathf.Min(PreferredWindowWidth, Mathf.Max(MinimumWindowWidth, canvasRect.rect.width - 48f));
			height = Mathf.Min(PreferredWindowHeight, Mathf.Max(MinimumWindowHeight, canvasRect.rect.height - 56f));
		}
		_backgroundRect.sizeDelta = new Vector2(width, height);
	}

	private static void Refresh()
	{
		if (_window == null || !TryResolveActor(out Actor actor))
		{
			SetUnavailable("持函人的水纹已经断去，此函再无所应。");
			return;
		}
		SetButtonLabel(_endButton, "收念归身");
		SetButtonLabel(_closeButton, "关闭");
		int currentYear = Math.Max(1, World.world?.map_stats?.year ?? 1);
		if (!XjYuanZhaoCredentialSystem.TryGetView(actor, currentYear, out XjYuanZhaoCredentialSystem.XjYuanZhaoExplorationView view)
			|| !view.Found)
		{
			SetUnavailable("此刻无函可照，水月之缘尚未临身。");
			return;
		}

		bool nodeChanged = !string.Equals(_lastRenderedTitle, view.Title, StringComparison.Ordinal);
		_lastRenderedTitle = view.Title ?? string.Empty;
		_titleText.text = view.Title;
		_kickerText.text = ResolveKicker(view.State);
		_bodyText.text = BuildBodyText(view.Body, _localStatus);
		_statusText.text = FormatStatus(view.Status);
		RebuildBody(nodeChanged);

		if (view.State == 1)
		{
			ConfigureChoice(_choice0, view.Choice0, true, () =>
			{
				int year = Math.Max(1, World.world?.map_stats?.year ?? 1);
				XjYuanZhaoCredentialSystem.TryBeginExploration(actor, year, out string message);
				_localStatus = message;
				XjActorInfoPanelRenderer.Invalidate();
				Refresh();
			});
			ConfigureChoice(_choice1, view.Choice1, true, () => ScrollWindow.showWindow(WindowId));
			ConfigureChoice(_choice2, view.Choice2, true, () =>
			{
				int year = Math.Max(1, World.world?.map_stats?.year ?? 1);
				XjYuanZhaoCredentialSystem.TryDeclineCredential(actor, year, out string message);
				_localStatus = message;
				XjActorInfoPanelRenderer.Invalidate();
				Refresh();
			});
			ConfigureFooter(false);
			return;
		}

		if (view.State == 2)
		{
			ConfigureChoice(_choice0, view.Choice0, !string.IsNullOrWhiteSpace(view.Choice0), () => Choose(actor, 0));
			ConfigureChoice(_choice1, view.Choice1, !string.IsNullOrWhiteSpace(view.Choice1), () => Choose(actor, 1));
			ConfigureChoice(_choice2, view.Choice2, !string.IsNullOrWhiteSpace(view.Choice2), () => Choose(actor, 2));
			ConfigureFooter(view.CanEndExploration);
			_endButton.onClick.RemoveAllListeners();
			if (view.CanEndExploration)
			{
				_endButton.onClick.AddListener(() =>
				{
					int year = Math.Max(1, World.world?.map_stats?.year ?? 1);
					XjYuanZhaoCredentialSystem.TryEndExploration(actor, year, out string message);
					_localStatus = message;
					XjActorInfoPanelRenderer.Invalidate();
					Refresh();
				});
			}
			return;
		}

		ConfigureChoice(_choice0, string.Empty, false, null);
		ConfigureChoice(_choice1, string.Empty, false, null);
		ConfigureChoice(_choice2, string.Empty, false, null);
		ConfigureFooter(false);
	}


	private static void Choose(Actor actor, int index)
	{
		int currentYear = Math.Max(1, World.world?.map_stats?.year ?? 1);
		XjYuanZhaoCredentialSystem.TryChoose(actor, index, currentYear, out string message);
		_localStatus = message;
		XjActorInfoPanelRenderer.Invalidate();
		Refresh();
	}

	private static void SetUnavailable(string message)
	{
		if (_titleText != null) _titleText.text = "水月照真";
		if (_kickerText != null) _kickerText.text = "因缘已散";
		if (_bodyText != null) _bodyText.text = message ?? string.Empty;
		if (_statusText != null) _statusText.text = string.IsNullOrWhiteSpace(_localStatus) ? "水纹已寂" : _localStatus;
		ConfigureChoice(_choice0, string.Empty, false, null);
		ConfigureChoice(_choice1, string.Empty, false, null);
		ConfigureChoice(_choice2, string.Empty, false, null);
		ConfigureFooter(false);
		RebuildBody(true);
	}

	private static string ResolveKicker(int state)
	{
		return state switch
		{
			1 => "水月照真　请凭函",
			2 => "水月照真　因缘试境",
			3 => "水月照真　游历已结",
			_ => "水月照真"
		};
	}

	private static string BuildBodyText(string body, string localStatus)
	{
		string normalizedBody = body?.Trim() ?? string.Empty;
		string normalizedStatus = localStatus?.Trim() ?? string.Empty;
		// 选择结果通常已经被 CredentialSystem 写入下一页正文。只有没有被正文包含时，
		// 才额外显示一次“水月回响”，避免旧 UI 那样正文和状态栏重复两遍同一句话。
		if (!string.IsNullOrWhiteSpace(normalizedStatus)
			&& normalizedBody.IndexOf(normalizedStatus, StringComparison.Ordinal) < 0)
		{
			return "<color=#8FD5DF>【水月回响】</color>\n" + normalizedStatus + "\n\n" + normalizedBody;
		}
		return normalizedBody;
	}

	private static string FormatStatus(string status)
	{
		if (string.IsNullOrWhiteSpace(status)) return "水纹平静";
		return status.Trim().Replace(" · ", "　　");
	}

	private static void RebuildBody(bool scrollToTop)
	{
		if (_bodyContentRect != null)
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(_bodyContentRect);
		}
		Canvas.ForceUpdateCanvases();
		if (scrollToTop && _bodyScroll != null)
		{
			_bodyScroll.verticalNormalizedPosition = 1f;
		}
	}

	private static void ConfigureChoice(Button button, string label, bool active, UnityEngine.Events.UnityAction action)
	{
		if (button == null) return;
		button.gameObject.SetActive(active);
		button.onClick.RemoveAllListeners();
		if (active && action != null) button.onClick.AddListener(action);
		Transform labelTransform = button.transform.Find("Text");
		Text text = labelTransform == null ? null : labelTransform.GetComponent<Text>();
		if (text != null) text.text = label ?? string.Empty;
	}


	private static void SetButtonLabel(Button button, string label)
	{
		if (button == null) return;
		Transform labelTransform = button.transform.Find("Text");
		Text text = labelTransform == null ? null : labelTransform.GetComponent<Text>();
		if (text != null) text.text = label ?? string.Empty;
	}


	private static void ConfigureFooter(bool showEnd)
	{
		if (_closeButton == null || _endButton == null) return;
		_endButton.gameObject.SetActive(showEnd);
		RectTransform closeRect = _closeButton.GetComponent<RectTransform>();
		RectTransform endRect = _endButton.GetComponent<RectTransform>();
		if (showEnd)
		{
			SetFooterRect(closeRect, leftHalf: true);
			SetFooterRect(endRect, leftHalf: false);
		}
		else
		{
			SetFullFooterRect(closeRect);
		}
	}

	private static bool TryResolveActor(out Actor actor)
	{
		actor = null;
		return _actorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(_actorId, out actor)
			&& actor?.data != null
			&& actor.isAlive();
	}

	private static void CreateBackdrop(Transform parent)
	{
		GameObject backdrop = new GameObject("YuanZhaoBackdrop", typeof(RectTransform), typeof(Image));
		backdrop.transform.SetParent(parent, false);
		RectTransform rect = backdrop.GetComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = new Vector2(12f, 12f);
		rect.offsetMax = new Vector2(-12f, -32f);
		Image image = backdrop.GetComponent<Image>();
		image.color = ColorBackdrop;
		image.raycastTarget = false;
		backdrop.transform.SetAsFirstSibling();
	}

	private static void CreateHeader(Transform parent)
	{
		GameObject panel = CreatePanel(parent, "HeaderPanel", ColorHeader, ColorLine);
		RectTransform rect = panel.GetComponent<RectTransform>();
		SetTopStretchRect(rect, 22f, 22f, HeaderTop, HeaderHeight);

		GameObject accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
		accent.transform.SetParent(panel.transform, false);
		RectTransform accentRect = accent.GetComponent<RectTransform>();
		accentRect.anchorMin = new Vector2(0f, 0f);
		accentRect.anchorMax = new Vector2(0f, 1f);
		accentRect.pivot = new Vector2(0f, 0.5f);
		accentRect.anchoredPosition = Vector2.zero;
		accentRect.sizeDelta = new Vector2(4f, 0f);
		accent.GetComponent<Image>().color = new Color(0.55f, 0.86f, 0.92f, 0.88f);
		accent.GetComponent<Image>().raycastTarget = false;

		_kickerText = CreateAnchoredText(panel.transform, "Kicker", new Vector2(0f, 0.56f), Vector2.one,
			new Vector2(14f, 0f), new Vector2(-14f, -4f), 8, TextAnchor.MiddleCenter, ColorMuted);
		_titleText = CreateAnchoredText(panel.transform, "Title", Vector2.zero, new Vector2(1f, 0.66f),
			new Vector2(14f, 4f), new Vector2(-14f, 0f), 13, TextAnchor.MiddleCenter, ColorTitle);
		_titleText.fontStyle = FontStyle.Bold;
	}

	private static void CreateBodyScroll(Transform parent)
	{
		GameObject card = CreatePanel(parent, "NarrativeCard", ColorCard, new Color(0.38f, 0.65f, 0.72f, 0.44f));
		RectTransform cardRect = card.GetComponent<RectTransform>();
		cardRect.anchorMin = Vector2.zero;
		cardRect.anchorMax = Vector2.one;
		cardRect.offsetMin = new Vector2(22f, BodyBottom);
		cardRect.offsetMax = new Vector2(-22f, -BodyTop);

		GameObject scrollObj = new GameObject("NarrativeScroll", typeof(RectTransform), typeof(ScrollRect));
		scrollObj.transform.SetParent(card.transform, false);
		RectTransform scrollRectTransform = scrollObj.GetComponent<RectTransform>();
		scrollRectTransform.anchorMin = Vector2.zero;
		scrollRectTransform.anchorMax = Vector2.one;
		scrollRectTransform.offsetMin = new Vector2(10f, 9f);
		scrollRectTransform.offsetMax = new Vector2(-8f, -9f);

		GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
		viewportObj.transform.SetParent(scrollObj.transform, false);
		RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
		viewportRect.anchorMin = Vector2.zero;
		viewportRect.anchorMax = Vector2.one;
		viewportRect.offsetMin = Vector2.zero;
		viewportRect.offsetMax = Vector2.zero;
		Image viewportImage = viewportObj.GetComponent<Image>();
		viewportImage.color = new Color(0f, 0f, 0f, 0.025f);
		viewportImage.raycastTarget = true;
		viewportObj.GetComponent<Mask>().showMaskGraphic = false;

		GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		contentObj.transform.SetParent(viewportObj.transform, false);
		_bodyContentRect = contentObj.GetComponent<RectTransform>();
		_bodyContentRect.anchorMin = new Vector2(0f, 1f);
		_bodyContentRect.anchorMax = new Vector2(1f, 1f);
		_bodyContentRect.pivot = new Vector2(0.5f, 1f);
		_bodyContentRect.anchoredPosition = Vector2.zero;
		_bodyContentRect.sizeDelta = new Vector2(-4f, 0f);
		VerticalLayoutGroup group = contentObj.GetComponent<VerticalLayoutGroup>();
		group.padding = new RectOffset(7, 8, 6, 8);
		group.spacing = 0f;
		group.childAlignment = TextAnchor.UpperLeft;
		group.childControlWidth = true;
		group.childControlHeight = true;
		group.childForceExpandWidth = true;
		group.childForceExpandHeight = false;
		ContentSizeFitter fitter = contentObj.GetComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		GameObject textObj = new GameObject("BodyText", typeof(RectTransform), typeof(Text), typeof(LayoutElement), typeof(ContentSizeFitter));
		textObj.transform.SetParent(contentObj.transform, false);
		_bodyText = textObj.GetComponent<Text>();
		_bodyText.font = ResolveFont();
		_bodyText.fontSize = 10;
		_bodyText.supportRichText = true;
		_bodyText.alignment = TextAnchor.UpperLeft;
		_bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
		_bodyText.verticalOverflow = VerticalWrapMode.Overflow;
		_bodyText.lineSpacing = 1.08f;
		_bodyText.color = ColorText;
		_bodyText.raycastTarget = false;
		LayoutElement textLayout = textObj.GetComponent<LayoutElement>();
		textLayout.minHeight = 32f;
		ContentSizeFitter textFitter = textObj.GetComponent<ContentSizeFitter>();
		textFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		_bodyScroll = scrollObj.GetComponent<ScrollRect>();
		_bodyScroll.horizontal = false;
		_bodyScroll.vertical = true;
		_bodyScroll.movementType = ScrollRect.MovementType.Clamped;
		_bodyScroll.inertia = true;
		_bodyScroll.decelerationRate = 0.18f;
		_bodyScroll.scrollSensitivity = 26f;
		_bodyScroll.viewport = viewportRect;
		_bodyScroll.content = _bodyContentRect;
	}

	private static void CreateStatusBar(Transform parent)
	{
		GameObject panel = CreatePanel(parent, "StatusPanel", ColorStatus, new Color(0.42f, 0.72f, 0.78f, 0.50f));
		RectTransform rect = panel.GetComponent<RectTransform>();
		SetBottomStretchRect(rect, 22f, 22f, StatusBottom, StatusHeight);
		_statusText = CreateAnchoredText(panel.transform, "Status", Vector2.zero, Vector2.one,
			new Vector2(8f, 2f), new Vector2(-8f, -2f), 9, TextAnchor.MiddleCenter, new Color(0.80f, 0.92f, 0.95f, 1f));
	}

	private static Button CreateChoiceButton(Transform parent, string name, float bottom, Color baseColor)
	{
		Button button = CreateButtonBase(parent, name, baseColor, new Color(0.55f, 0.85f, 0.91f, 0.72f));
		RectTransform rect = button.GetComponent<RectTransform>();
		SetBottomStretchRect(rect, 30f, 30f, bottom, ChoiceHeight);
		Text text = CreateAnchoredText(button.transform, "Text", Vector2.zero, Vector2.one,
			new Vector2(10f, 0f), new Vector2(-10f, 0f), 9, TextAnchor.MiddleCenter, new Color(0.96f, 0.99f, 1f, 1f));
		text.fontStyle = FontStyle.Bold;
		return button;
	}

	private static Button CreateFooterButton(Transform parent, string name, string label, bool leftHalf, Color baseColor)
	{
		Button button = CreateButtonBase(parent, name, baseColor, new Color(0.58f, 0.72f, 0.72f, 0.58f));
		SetFooterRect(button.GetComponent<RectTransform>(), leftHalf);
		Text text = CreateAnchoredText(button.transform, "Text", Vector2.zero, Vector2.one,
			new Vector2(8f, 0f), new Vector2(-8f, 0f), 9, TextAnchor.MiddleCenter, new Color(0.96f, 0.94f, 0.83f, 1f));
		text.fontStyle = FontStyle.Bold;
		text.text = label ?? string.Empty;
		return button;
	}

	private static Button CreateButtonBase(Transform parent, string name, Color baseColor, Color outlineColor)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
		obj.transform.SetParent(parent, false);
		Image image = obj.GetComponent<Image>();
		image.color = baseColor;
		Outline outline = obj.GetComponent<Outline>();
		outline.effectColor = outlineColor;
		outline.effectDistance = new Vector2(1f, -1f);
		outline.useGraphicAlpha = true;
		Button button = obj.GetComponent<Button>();
		ColorBlock colors = button.colors;
		colors.normalColor = Color.white;
		colors.highlightedColor = new Color(1.14f, 1.14f, 1.14f, 1f);
		colors.pressedColor = new Color(0.82f, 0.88f, 0.90f, 1f);
		colors.selectedColor = colors.highlightedColor;
		colors.disabledColor = new Color(0.50f, 0.55f, 0.56f, 0.55f);
		colors.colorMultiplier = 1f;
		colors.fadeDuration = 0.08f;
		button.colors = colors;
		button.navigation = new Navigation { mode = Navigation.Mode.None };
		return button;
	}

	private static GameObject CreatePanel(Transform parent, string name, Color fill, Color outlineColor)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline));
		obj.transform.SetParent(parent, false);
		Image image = obj.GetComponent<Image>();
		image.color = fill;
		image.raycastTarget = false;
		Outline outline = obj.GetComponent<Outline>();
		outline.effectColor = outlineColor;
		outline.effectDistance = new Vector2(1f, -1f);
		outline.useGraphicAlpha = true;
		return obj;
	}

	private static Text CreateAnchoredText(
		Transform parent,
		string name,
		Vector2 anchorMin,
		Vector2 anchorMax,
		Vector2 offsetMin,
		Vector2 offsetMax,
		int fontSize,
		TextAnchor alignment,
		Color color)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
		obj.transform.SetParent(parent, false);
		RectTransform rect = obj.GetComponent<RectTransform>();
		rect.anchorMin = anchorMin;
		rect.anchorMax = anchorMax;
		rect.offsetMin = offsetMin;
		rect.offsetMax = offsetMax;
		Text text = obj.GetComponent<Text>();
		text.font = ResolveFont();
		text.fontSize = fontSize;
		text.alignment = alignment;
		text.color = color;
		text.supportRichText = true;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.raycastTarget = false;
		return text;
	}

	private static Font ResolveFont()
	{
		return LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
	}

	private static void SetTopStretchRect(RectTransform rect, float left, float right, float top, float height)
	{
		if (rect == null) return;
		rect.anchorMin = new Vector2(0f, 1f);
		rect.anchorMax = new Vector2(1f, 1f);
		rect.pivot = new Vector2(0.5f, 1f);
		rect.offsetMin = new Vector2(left, -top - height);
		rect.offsetMax = new Vector2(-right, -top);
	}

	private static void SetBottomStretchRect(RectTransform rect, float left, float right, float bottom, float height)
	{
		if (rect == null) return;
		rect.anchorMin = new Vector2(0f, 0f);
		rect.anchorMax = new Vector2(1f, 0f);
		rect.pivot = new Vector2(0.5f, 0f);
		rect.offsetMin = new Vector2(left, bottom);
		rect.offsetMax = new Vector2(-right, bottom + height);
	}

	private static void SetFooterRect(RectTransform rect, bool leftHalf)
	{
		if (rect == null) return;
		if (leftHalf)
		{
			rect.anchorMin = new Vector2(0f, 0f);
			rect.anchorMax = new Vector2(0.5f, 0f);
			rect.offsetMin = new Vector2(30f, FooterBottom);
			rect.offsetMax = new Vector2(-4f, FooterBottom + FooterHeight);
		}
		else
		{
			rect.anchorMin = new Vector2(0.5f, 0f);
			rect.anchorMax = new Vector2(1f, 0f);
			rect.offsetMin = new Vector2(4f, FooterBottom);
			rect.offsetMax = new Vector2(-30f, FooterBottom + FooterHeight);
		}
	}

	private static void SetFullFooterRect(RectTransform rect)
	{
		if (rect == null) return;
		rect.anchorMin = new Vector2(0f, 0f);
		rect.anchorMax = new Vector2(1f, 0f);
		rect.offsetMin = new Vector2(72f, FooterBottom);
		rect.offsetMax = new Vector2(-72f, FooterBottom + FooterHeight);
	}
}
