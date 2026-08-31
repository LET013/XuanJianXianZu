using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.UI.Common;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.UI.Codex;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.UI.DongTian;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.UI.ActorInfo;

internal static class XjActorInfoPanelRenderer
{
	private const string PanelName = "XuanJianInfoPanel";
	private const string TextName = "XuanJianInfo";
	private const float PanelWidth = 180f;
	private const float PanelHeight = 344f;

	private static long _lastActorId;
	private static int _lastWorldYear = -1;
	private static string _lastText = string.Empty;
	private static XjActorRevisionToken _lastRevisionToken;
	private static int _lastRelationsRevisionHash;
	private static bool _hasLastRevisionToken;

	internal static void Invalidate()
	{
		_lastText = string.Empty;
		_lastRevisionToken = default;
		_lastRelationsRevisionHash = 0;
		_hasLastRevisionToken = false;
	}

	internal static void Refresh(UnitWindow window)
	{
		if (window == null || window.actor == null || !((NanoObject)window.actor).isAlive())
		{
			return;
		}

		Transform background = ((Component)(object)window).transform.Find("Background");
		if (background == null)
		{
			return;
		}

		long actorId = GetActorId(window.actor);
		int worldYear = World.world?.map_stats?.year ?? 0;
		if (!ShouldDisplayFor(window.actor, actorId))
		{
			HidePanel(background, actorId, worldYear);
			return;
		}

		Text existingText = FindExistingText(background);
		XjActorRevisionToken revisionToken = XjActorStateRevisionStore.GetToken(actorId);
		int relationsRevisionHash = XjActorSupplementReadModelStore.GetRelationsRevisionHash(actorId, in revisionToken);
		bool due = actorId != _lastActorId
			|| worldYear != _lastWorldYear
			|| !_hasLastRevisionToken
			|| revisionToken != _lastRevisionToken
			|| relationsRevisionHash != _lastRelationsRevisionHash
			|| existingText == null
			|| string.IsNullOrEmpty(_lastText);
		if (!due)
		{
			return;
		}

		Text text = EnsurePanel(background, window.actor, actorId, out ScrollRect scrollRect);
		if (text == null)
		{
			return;
		}

		string formatted = XjActorInfoDisplayFormatter.Format(window.actor);
		if (!string.Equals(text.text, formatted, System.StringComparison.Ordinal))
		{
			text.text = formatted;
			if (actorId != _lastActorId)
			{
				ResetScroll(scrollRect);
			}
			else if (scrollRect?.content != null)
			{
				LayoutRebuilder.MarkLayoutForRebuild(scrollRect.content);
			}
		}
		_lastActorId = actorId;
		_lastWorldYear = worldYear;
		_lastText = formatted;
		_lastRevisionToken = revisionToken;
		_lastRelationsRevisionHash = relationsRevisionHash;
		_hasLastRevisionToken = true;
	}

	private static Text FindExistingText(Transform background)
	{
		Transform panel = background == null
			? null
			: XjUiSurfaceOwnership.FindSingleRoot(background, PanelName, "info", nameof(XjActorInfoPanelRenderer));
		Transform textTransform = panel?.Find("Viewport/" + TextName);
		return textTransform == null ? null : textTransform.GetComponent<Text>();
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static bool ShouldDisplayFor(Actor actor, long actorId)
	{
		if (actor?.data == null || actorId <= 0L)
		{
			return false;
		}

		// Ordinary mortals do not need the cultivation read model, bottleneck,
		// craft and high-realm projections. Keep special Xuanjian identities and
		// manually granted cultivation candidates visible during bootstrap.
		return XjRuntimeActorInterestIndex.HasStatInterest(actorId)
			|| XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(actor)
			|| XjCultivationEligibility.HasExplicitCultivationGrant(actor);
	}

	private static void HidePanel(Transform background, long actorId, int worldYear)
	{
		Transform panel = background == null
			? null
			: XjUiSurfaceOwnership.FindSingleRoot(background, PanelName, "info", nameof(XjActorInfoPanelRenderer));
		if (panel != null && panel.gameObject.activeSelf)
		{
			panel.gameObject.SetActive(false);
		}

		_lastActorId = actorId;
		_lastWorldYear = worldYear;
		_lastText = string.Empty;
		_lastRevisionToken = XjActorStateRevisionStore.GetToken(actorId);
		_lastRelationsRevisionHash = XjActorSupplementReadModelStore.GetRelationsRevisionHash(actorId, in _lastRevisionToken);
		_hasLastRevisionToken = true;
	}

	private static Text EnsurePanel(Transform parent, Actor actor, long actorId, out ScrollRect scrollRect)
	{
		scrollRect = null;
		Transform panelTransform = XjUiSurfaceOwnership.FindSingleRoot(parent, PanelName, "info", nameof(XjActorInfoPanelRenderer));
		GameObject panelObject;
		if (panelTransform == null)
		{
			panelObject = new GameObject(PanelName, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(ScrollRect));
			panelObject.transform.SetParent(parent, false);
			panelTransform = panelObject.transform;
			XjUiSurfaceOwnership.Claim(panelTransform, "info", nameof(XjActorInfoPanelRenderer));
		}
		else
		{
			panelObject = panelTransform.gameObject;
		}
		if (!panelObject.activeSelf)
		{
			panelObject.SetActive(true);
		}

		RectTransform panelRect = panelObject.GetComponent<RectTransform>();
		if (panelRect == null)
		{
			return null;
		}

		panelRect.localPosition = new Vector3(256f, 40f);
		panelRect.localScale = Vector3.one;
		panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

		Image panelImage = panelObject.GetComponent<Image>();
		if (panelImage != null)
		{
			panelImage.color = new Color(0.018f, 0.045f, 0.052f, 0.48f);
			panelImage.raycastTarget = false;
		}

		Outline panelOutline = panelObject.GetComponent<Outline>();
		if (panelOutline != null)
		{
			panelOutline.effectColor = new Color(0.72f, 0.88f, 0.72f, 0.46f);
			panelOutline.effectDistance = new Vector2(1.5f, -1.5f);
			panelOutline.useGraphicAlpha = true;
		}

		scrollRect = panelObject.GetComponent<ScrollRect>();
		if (scrollRect == null)
		{
			scrollRect = panelObject.AddComponent<ScrollRect>();
		}

		scrollRect.horizontal = false;
		scrollRect.vertical = true;
		scrollRect.movementType = ScrollRect.MovementType.Clamped;
		scrollRect.inertia = true;
		scrollRect.scrollSensitivity = 24f;

		RectTransform viewportRect = EnsureViewport(panelTransform, out Transform viewportTransform);
		if (viewportRect == null || viewportTransform == null)
		{
			return null;
		}

		Text text = EnsureText(viewportTransform);
		if (text == null)
		{
			return null;
		}
		EnsurePanelOrnaments(panelTransform);
		bool isShi = XjCultivationPathRules.IsShi(actor);
		EnsureChronicleButton(panelTransform, actorId, fullWidth: isShi);
		if (isShi) RemoveChild(panelTransform, "SectEnrollmentButton");
		else EnsureSectEnrollmentButton(panelTransform, actor, actorId);
		bool isDaoTai = XjDaoTaiSpellScale.IsDaoTaiActor(actor);
		EnsureDaoTaiPresenceButtons(panelTransform, actor, actorId, isDaoTai);
		int worldYear = World.world?.map_stats?.year ?? 0;
		bool hasYuanZhaoCredential = XjYuanZhaoCredentialSystem.HasPlayerInteraction(actor, worldYear);
		if (hasYuanZhaoCredential) EnsureYuanZhaoCredentialButton(panelTransform, actor, actorId, worldYear);
		else RemoveChild(panelTransform, "YuanZhaoCredentialButton");
		SetChildActive(panelTransform, "BottomSeal", !hasYuanZhaoCredential);
		SetChildActive(panelTransform, "BottomCarving", !hasYuanZhaoCredential);
		RemoveLegacyFuQiTransferButton(panelTransform);
		float bottomInset = isDaoTai ? 69f : 43f;
		if (hasYuanZhaoCredential) bottomInset = Mathf.Max(bottomInset, 74f);
		viewportRect.offsetMin = new Vector2(9f, bottomInset);
		viewportRect.offsetMax = new Vector2(-9f, -43f);

		RectTransform textRect = text.GetComponent<RectTransform>();
		scrollRect.viewport = viewportRect;
		scrollRect.content = textRect;
		return text;
	}

	private static void EnsurePanelOrnaments(Transform panelTransform)
	{
		if (panelTransform == null) return;
		EnsureDecorativeText(panelTransform, "PanelTitle", "◇玄鉴照录◇", new Vector2(5f, -7f), new Vector2(82f, 27f), TextAnchor.MiddleLeft, 9, new Color(0.93f, 0.82f, 0.48f, 1f), true);
		EnsureDecorativeText(panelTransform, "BottomSeal", "云纹 · 照世 · 留真", new Vector2(0f, 35f), new Vector2(150f, 8f), TextAnchor.MiddleCenter, 6, new Color(0.54f, 0.78f, 0.70f, 0.72f), false, true);
		EnsureDecorativeLine(panelTransform, "TopCarving", new Vector2(0f, -36f), new Vector2(-14f, 1.5f), new Color(0.58f, 0.82f, 0.72f, 0.45f), true);
		EnsureDecorativeLine(panelTransform, "BottomCarving", new Vector2(0f, 43f), new Vector2(-14f, 1.5f), new Color(0.58f, 0.82f, 0.72f, 0.38f), false);
	}

	private static void EnsureDecorativeText(
		Transform parent, string name, string value, Vector2 position, Vector2 size, TextAnchor alignment, int fontSize, Color color, bool topAnchor, bool bottomAnchor = false)
	{
		Transform child = parent.Find(name);
		GameObject obj = child == null ? new GameObject(name, typeof(RectTransform), typeof(Text)) : child.gameObject;
		if (child == null) obj.transform.SetParent(parent, false);
		RectTransform rect = obj.GetComponent<RectTransform>();
		if (bottomAnchor)
		{
			rect.anchorMin = new Vector2(0.5f, 0f);
			rect.anchorMax = new Vector2(0.5f, 0f);
			rect.pivot = new Vector2(0.5f, 0f);
		}
		else if (topAnchor)
		{
			rect.anchorMin = new Vector2(0f, 1f);
			rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
		}
		rect.anchoredPosition = position;
		rect.sizeDelta = size;
		Text text = obj.GetComponent<Text>();
		text.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		text.text = value;
		text.fontSize = fontSize;
		text.alignment = alignment;
		text.color = color;
		text.raycastTarget = false;
	}

	private static void EnsureDecorativeLine(Transform parent, string name, Vector2 position, Vector2 size, Color color, bool topAnchor)
	{
		Transform child = parent.Find(name);
		GameObject obj = child == null ? new GameObject(name, typeof(RectTransform), typeof(Image)) : child.gameObject;
		if (child == null) obj.transform.SetParent(parent, false);
		RectTransform rect = obj.GetComponent<RectTransform>();
		rect.anchorMin = topAnchor ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
		rect.anchorMax = rect.anchorMin;
		rect.pivot = topAnchor ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
		rect.anchoredPosition = position;
		rect.sizeDelta = new Vector2(PanelWidth + size.x, size.y);
		Image image = obj.GetComponent<Image>();
		image.color = color;
		image.raycastTarget = false;
	}

	private static void EnsureButtonOutline(GameObject buttonObject, Color color)
	{
		if (buttonObject == null) return;
		Outline outline = buttonObject.GetComponent<Outline>();
		if (outline == null) outline = buttonObject.AddComponent<Outline>();
		outline.effectColor = color;
		outline.effectDistance = new Vector2(1f, -1f);
		outline.useGraphicAlpha = true;
	}

	private static void EnsureChronicleButton(Transform panelTransform, long actorId, bool fullWidth)
	{
		if (panelTransform == null || actorId <= 0L) return;
		const string buttonName = "ChronicleButton";
		Transform buttonTransform = panelTransform.Find(buttonName);
		GameObject buttonObject;
		if (buttonTransform == null)
		{
			buttonObject = new GameObject(buttonName, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
			buttonObject.transform.SetParent(panelTransform, false);
			GameObject labelObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
			labelObject.transform.SetParent(buttonObject.transform, false);
		}
		else buttonObject = buttonTransform.gameObject;

		RectTransform rect = buttonObject.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(0f, 0f);
		rect.anchorMax = new Vector2(fullWidth ? 1f : 0.5f, 0f);
		rect.pivot = new Vector2(0.5f, 0f);
		rect.offsetMin = new Vector2(8f, 7f);
		rect.offsetMax = new Vector2(fullWidth ? -8f : -4f, 35f);

		Image image = buttonObject.GetComponent<Image>();
		image.color = new Color(0.10f, 0.30f, 0.30f, 0.96f);
		EnsureButtonOutline(buttonObject, new Color(0.55f, 0.88f, 0.78f, 0.72f));
		Button button = buttonObject.GetComponent<Button>();
		button.onClick.RemoveAllListeners();
		button.onClick.AddListener(() => XjCodexWindow.ShowHistoryActor(actorId));

		Transform labelTransform = buttonObject.transform.Find("Text");
		Text label = labelTransform == null ? null : labelTransform.GetComponent<Text>();
		if (label == null) return;
		RectTransform labelRect = label.GetComponent<RectTransform>();
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;
		label.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.text = "◇ 阅修士列传 ◇";
		label.fontSize = 9;
		label.alignment = TextAnchor.MiddleCenter;
		label.color = new Color(0.92f, 0.98f, 0.95f, 1f);
		label.raycastTarget = false;
	}

	private static void EnsureSectEnrollmentButton(Transform panelTransform, Actor actor, long actorId)
	{
		if (panelTransform == null || actor?.data == null || actorId <= 0L) return;
		const string buttonName = "SectEnrollmentButton";
		Transform buttonTransform = panelTransform.Find(buttonName);
		GameObject buttonObject;
		if (buttonTransform == null)
		{
			buttonObject = new GameObject(buttonName, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
			buttonObject.transform.SetParent(panelTransform, false);
			GameObject labelObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
			labelObject.transform.SetParent(buttonObject.transform, false);
		}
		else buttonObject = buttonTransform.gameObject;

		RectTransform rect = buttonObject.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.5f, 0f);
		rect.anchorMax = new Vector2(1f, 0f);
		rect.pivot = new Vector2(0.5f, 0f);
		rect.offsetMin = new Vector2(4f, 7f);
		rect.offsetMax = new Vector2(-8f, 35f);

		Image image = buttonObject.GetComponent<Image>();
		image.color = new Color(0.38f, 0.25f, 0.075f, 0.97f);
		EnsureButtonOutline(buttonObject, new Color(0.95f, 0.78f, 0.35f, 0.76f));
		Button button = buttonObject.GetComponent<Button>();
		button.onClick.RemoveAllListeners();
		button.onClick.AddListener(() => XjCodexWindow.ShowSectEnrollment(actorId));

		XjSectIdentitySnapshot identity = XjSectIdentityReader.BuildIdentity(actor);
		Transform labelTransform = buttonObject.transform.Find("Text");
		Text label = labelTransform == null ? null : labelTransform.GetComponent<Text>();
		if (label == null) return;
		RectTransform labelRect = label.GetComponent<RectTransform>();
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;
		label.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.text = identity.Found ? "◇ 调整山门" : "◇ 收入山门";
		label.fontSize = 9;
		label.alignment = TextAnchor.MiddleCenter;
		label.color = new Color(1f, 0.92f, 0.72f, 1f);
		label.raycastTarget = false;
	}

	private static void EnsureYuanZhaoCredentialButton(Transform panelTransform, Actor actor, long actorId, int worldYear)
	{
		if (panelTransform == null || actor?.data == null || actorId <= 0L) return;
		string labelText = XjYuanZhaoCredentialSystem.ResolvePanelButtonLabel(actor, worldYear);
		if (string.IsNullOrWhiteSpace(labelText))
		{
			RemoveChild(panelTransform, "YuanZhaoCredentialButton");
			return;
		}

		const string buttonName = "YuanZhaoCredentialButton";
		Transform buttonTransform = panelTransform.Find(buttonName);
		GameObject buttonObject;
		if (buttonTransform == null)
		{
			buttonObject = new GameObject(buttonName, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
			buttonObject.transform.SetParent(panelTransform, false);
			GameObject labelObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
			labelObject.transform.SetParent(buttonObject.transform, false);
		}
		else buttonObject = buttonTransform.gameObject;

		RectTransform rect = buttonObject.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(0f, 0f);
		rect.anchorMax = new Vector2(1f, 0f);
		rect.pivot = new Vector2(0.5f, 0f);
		rect.offsetMin = new Vector2(8f, 40f);
		rect.offsetMax = new Vector2(-8f, 68f);

		Image image = buttonObject.GetComponent<Image>();
		image.color = new Color(0.08f, 0.22f, 0.32f, 0.97f);
		EnsureButtonOutline(buttonObject, new Color(0.58f, 0.82f, 0.94f, 0.82f));
		Button button = buttonObject.GetComponent<Button>();
		button.onClick.RemoveAllListeners();
		button.onClick.AddListener(() => XjYuanZhaoExplorationWindow.Show(actorId));

		Transform labelTransform = buttonObject.transform.Find("Text");
		Text label = labelTransform == null ? null : labelTransform.GetComponent<Text>();
		if (label == null) return;
		RectTransform labelRect = label.GetComponent<RectTransform>();
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;
		label.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.text = labelText;
		label.fontSize = 9;
		label.alignment = TextAnchor.MiddleCenter;
		label.color = new Color(0.88f, 0.96f, 1f, 1f);
		label.raycastTarget = false;
	}

	private static void SetChildActive(Transform parent, string name, bool active)
	{
		Transform child = parent?.Find(name);
		if (child != null && child.gameObject.activeSelf != active) child.gameObject.SetActive(active);
	}

	private static void EnsureDaoTaiPresenceButtons(Transform panelTransform, Actor actor, long actorId, bool isDaoTai)
	{
		if (panelTransform == null) return;
		RemoveChild(panelTransform, "DaoTaiBeyondWorldButton");
		RemoveChild(panelTransform, "DaoTaiTravelButton");
	}


	private static void RemoveChild(Transform parent, string name)
	{
		Transform child = parent?.Find(name);
		if (child != null) Object.Destroy(child.gameObject);
	}


	private static void RemoveLegacyFuQiTransferButton(Transform panelTransform)
	{
		if (panelTransform == null) return;
		const string buttonName = "FuQiToZiFuButton";
		Transform buttonTransform = panelTransform.Find(buttonName);
		if (buttonTransform != null)
		{
			Object.Destroy(buttonTransform.gameObject);
		}
	}

	private static RectTransform EnsureViewport(Transform panelTransform, out Transform viewportTransform)
	{
		viewportTransform = panelTransform.Find("Viewport");
		GameObject viewportObject;
		if (viewportTransform == null)
		{
			viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
			viewportObject.transform.SetParent(panelTransform, false);
			viewportTransform = viewportObject.transform;
		}
		else
		{
			viewportObject = viewportTransform.gameObject;
		}

		RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
		if (viewportRect == null)
		{
			return null;
		}

		viewportRect.anchorMin = Vector2.zero;
		viewportRect.anchorMax = Vector2.one;
		viewportRect.offsetMin = new Vector2(9f, 43f);
		viewportRect.offsetMax = new Vector2(-9f, -43f);

		Image viewportImage = viewportObject.GetComponent<Image>();
		if (viewportImage != null)
		{
			viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
			viewportImage.raycastTarget = true;
		}

		Mask viewportMask = viewportObject.GetComponent<Mask>();
		if (viewportMask != null)
		{
			viewportMask.showMaskGraphic = false;
		}

		return viewportRect;
	}

	private static Text EnsureText(Transform viewportTransform)
	{
		Transform textTransform = viewportTransform.Find(TextName);
		Text text = textTransform == null ? null : textTransform.GetComponent<Text>();
		if (text == null)
		{
			GameObject textObject = new GameObject(TextName, typeof(RectTransform), typeof(Text), typeof(ContentSizeFitter));
			textObject.transform.SetParent(viewportTransform, false);
			text = textObject.GetComponent<Text>();
		}

		ContentSizeFitter fitter = text.GetComponent<ContentSizeFitter>();
		if (fitter == null)
		{
			fitter = ((Component)(object)text).gameObject.AddComponent<ContentSizeFitter>();
		}

		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		RectTransform textRect = text.GetComponent<RectTransform>();
		textRect.anchorMin = new Vector2(0f, 1f);
		textRect.anchorMax = new Vector2(1f, 1f);
		textRect.pivot = new Vector2(0.5f, 1f);
		textRect.anchoredPosition = Vector2.zero;
		textRect.sizeDelta = Vector2.zero;

		ConfigureText(text);
		return text;
	}

	private static void ConfigureText(Text text)
	{
		if (text == null)
		{
			return;
		}

		text.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		text.supportRichText = true;
		text.resizeTextForBestFit = false;
		text.fontSize = 9;
		text.lineSpacing = 0.9f;
		text.color = new Color(0.97f, 0.98f, 1f, 1f);
		text.alignment = TextAnchor.UpperLeft;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.raycastTarget = false;

		Shadow shadow = text.GetComponent<Shadow>();
		if (shadow == null)
		{
			shadow = ((Component)(object)text).gameObject.AddComponent<Shadow>();
		}

		shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
		shadow.effectDistance = new Vector2(1f, -1f);
		shadow.useGraphicAlpha = true;
	}

	private static void ResetScroll(ScrollRect scrollRect)
	{
		if (scrollRect == null)
		{
			return;
		}

		if (scrollRect.content != null)
		{
			LayoutRebuilder.MarkLayoutForRebuild(scrollRect.content);
		}

		scrollRect.StopMovement();
		scrollRect.verticalNormalizedPosition = 1f;
	}
}
