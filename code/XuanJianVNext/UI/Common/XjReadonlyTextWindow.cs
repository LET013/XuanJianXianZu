using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;

namespace XuanJianVNext.UI.Common;

internal sealed class XjReadonlyTextWindowUpdater : MonoBehaviour
{
	internal Action OnOpen;
	internal Action OnClose;

	private void OnEnable()
	{
		OnOpen?.Invoke();
	}

	private void OnDisable()
	{
		OnClose?.Invoke();
	}

}

internal static class XjReadonlyTextWindow
{
	private sealed class WindowState
	{
		internal ScrollWindow Window;
		internal Text BodyText;
		internal Func<string> Provider;
		internal float LastRefreshTime;
	}

	private const float WindowWidth = 420f;
	private const float WindowHeight = 430f;
	private const int MaxTextLength = 24000;

	private static readonly Dictionary<string, WindowState> States = new Dictionary<string, WindowState>(StringComparer.Ordinal);

	internal static void Show(string windowId, string titleKey, string iconKey, Func<string> provider)
	{
		if (string.IsNullOrWhiteSpace(windowId))
		{
			return;
		}

		bool createdNow = !States.ContainsKey(windowId);
		WindowState state = Ensure(windowId, titleKey, iconKey, provider);
		if (state?.Window == null)
		{
			return;
		}

		if (createdNow)
		{
			Refresh(windowId);
		}
		XjWindowOpenGuard.Show(state.Window, windowId, createdNow);
	}

	private static WindowState Ensure(string windowId, string titleKey, string iconKey, Func<string> provider)
	{
		if (States.TryGetValue(windowId, out WindowState state))
		{
			state.Provider = provider;
			return state;
		}

		state = new WindowState();
		state.Window = WindowCreator.CreateEmptyWindow(windowId, titleKey, iconKey);
		state.Provider = provider;
		States[windowId] = state;
		SetupWindow(windowId, state);
		return state;
	}

	private static void SetupWindow(string windowId, WindowState state)
	{
		if (state?.Window == null)
		{
			return;
		}

		Transform background = ((Component)state.Window).transform.Find("Background");
		if (background == null)
		{
			return;
		}

		RectTransform bgRect = ((Component)background).GetComponent<RectTransform>();
		if (bgRect != null)
		{
			bgRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
		}

		state.BodyText = CreateScrollText(background);
		XjReadonlyTextWindowUpdater updater = ((Component)state.Window).gameObject.AddComponent<XjReadonlyTextWindowUpdater>();
		updater.OnOpen = () => Refresh(windowId);
		updater.OnClose = () => { state.LastRefreshTime = 0f; };
	}

	private static Text CreateScrollText(Transform parent)
	{
		GameObject scrollObj = new GameObject("ReadonlyScrollView", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		scrollObj.transform.SetParent(parent, false);
		RectTransform scrollRectTransform = scrollObj.GetComponent<RectTransform>();
		scrollRectTransform.anchorMin = Vector2.zero;
		scrollRectTransform.anchorMax = Vector2.one;
		scrollRectTransform.offsetMin = new Vector2(12f, 16f);
		scrollRectTransform.offsetMax = new Vector2(-12f, -42f);

		Image image = scrollObj.GetComponent<Image>();
		if (image != null)
		{
			image.color = new Color(0f, 0f, 0f, 0.24f);
			image.raycastTarget = true;
		}

		Mask mask = scrollObj.GetComponent<Mask>();
		if (mask != null)
		{
			mask.showMaskGraphic = true;
		}

		GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform));
		viewportObj.transform.SetParent(scrollObj.transform, false);
		RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
		viewportRect.anchorMin = Vector2.zero;
		viewportRect.anchorMax = Vector2.one;
		viewportRect.offsetMin = Vector2.zero;
		viewportRect.offsetMax = Vector2.zero;

		GameObject textObj = new GameObject("ReadonlyText", typeof(RectTransform), typeof(Text), typeof(ContentSizeFitter));
		textObj.transform.SetParent(viewportObj.transform, false);
		RectTransform textRect = textObj.GetComponent<RectTransform>();
		textRect.anchorMin = new Vector2(0f, 1f);
		textRect.anchorMax = new Vector2(1f, 1f);
		textRect.pivot = new Vector2(0.5f, 1f);
		textRect.anchoredPosition = Vector2.zero;
		textRect.sizeDelta = new Vector2(-12f, 0f);

		ContentSizeFitter fitter = textObj.GetComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		Text text = textObj.GetComponent<Text>();
		text.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		text.fontSize = 10;
		text.supportRichText = true;
		text.alignment = TextAnchor.UpperLeft;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.color = new Color(0.96f, 0.97f, 1f, 1f);
		text.raycastTarget = false;

		ScrollRect scroll = scrollObj.GetComponent<ScrollRect>();
		scroll.horizontal = false;
		scroll.vertical = true;
		scroll.movementType = ScrollRect.MovementType.Clamped;
		scroll.scrollSensitivity = 28f;
		scroll.viewport = viewportRect;
		scroll.content = textRect;
		return text;
	}

	private static void Refresh(string windowId)
	{
		if (!States.TryGetValue(windowId, out WindowState state) || state?.BodyText == null)
		{
			return;
		}

		state.LastRefreshTime = Time.unscaledTime;
		string text;
		try
		{
			text = state.Provider == null ? string.Empty : state.Provider();
		}
		catch (Exception ex)
		{
			text = "窗口数据读取失败：" + ex.GetType().Name;
		}
		if (!string.IsNullOrEmpty(text) && text.Length > MaxTextLength)
		{
			text = text.Substring(0, MaxTextLength) + "\n\n……列表过长，已截断显示。";
		}
		state.BodyText.text = string.IsNullOrWhiteSpace(text) ? "暂无数据" : text;
		RectTransform rect = state.BodyText.GetComponent<RectTransform>();
		if (rect != null)
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
		}
	}
}
