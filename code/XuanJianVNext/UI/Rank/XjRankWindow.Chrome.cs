using System;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;

namespace XuanJianVNext.UI.Rank;

/// <summary>
/// 0.9.9排行榜窗口壳层。只处理WorldBox原生关闭控件适配、自绘关闭按钮和窗口chrome，
/// 排行数据、筛选、虚拟列表与布局不再与原生窗口兼容细节混在同一大文件。
/// </summary>
internal static partial class XjRankWindow
{
	private static void RemoveCloseButtonBackground()
	{
		RestoreNativeCloseButton();
	}

	private static void RestoreNativeCloseButton()
	{
		if (window == null)
		{
			return;
		}

		Transform background = ((Component)window).transform.Find("Background") ?? ((Component)window).transform;
		Transform legacy = background.Find(CustomCloseButtonName);
		if (legacy != null)
		{
			UnityEngine.Object.Destroy(legacy.gameObject);
		}

		Transform closeRoot = ((Component)window).transform.Find("CloseBackground") ?? FindCloseButtonTransform();
		if (closeRoot == null)
		{
			return;
		}
		if (closeRoot is RectTransform closeRect)
		{
			// Keep the native close hierarchy and only apply the same anchor used by WuJi's leaderboard.
			closeRect.anchoredPosition = new Vector2(WindowWidth / 2f - 8f, WindowHeight / 2f - 8f);
		}

		Graphic[] graphics = closeRoot.GetComponentsInChildren<Graphic>(true);
		for (int i = 0; i < graphics.Length; i++)
		{
			Graphic graphic = graphics[i];
			if (graphic == null)
			{
				continue;
			}
			graphic.enabled = true;
			graphic.raycastTarget = graphic.transform == closeRoot || graphic.GetComponent<Button>() != null;
			graphic.color = new Color(graphic.color.r, graphic.color.g, graphic.color.b, 1f);
			try { graphic.canvasRenderer.SetAlpha(1f); } catch (System.Exception xjCaughtCloseGraphic) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Rank/XjRankWindow.cs:RestoreNativeCloseButton", xjCaughtCloseGraphic); }
		}

		Button[] nativeButtons = closeRoot.GetComponentsInChildren<Button>(true);
		for (int i = 0; i < nativeButtons.Length; i++)
		{
			if (nativeButtons[i] == null)
			{
				continue;
			}
			nativeButtons[i].enabled = true;
			nativeButtons[i].interactable = true;
		}
	}


	private static void HideNativeCloseTransform(Transform closeRoot, bool disableButton)
	{
		if (closeRoot == null)
		{
			return;
		}

		Graphic[] graphics = closeRoot.GetComponentsInChildren<Graphic>(true);
		for (int i = 0; i < graphics.Length; i++)
		{
			Graphic graphic = graphics[i];
			if (graphic == null || IsCustomCloseTransform(graphic.transform))
			{
				continue;
			}
			graphic.raycastTarget = false;
			graphic.color = new Color(graphic.color.r, graphic.color.g, graphic.color.b, 0f);
			try { graphic.canvasRenderer.SetAlpha(0f); } catch (System.Exception xjCaughtCloseGraphic) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Rank/XjRankWindow.cs:HideNativeCloseTransform", xjCaughtCloseGraphic); }
			graphic.enabled = false;
		}

		if (disableButton)
		{
			Button[] buttons = closeRoot.GetComponentsInChildren<Button>(true);
			for (int i = 0; i < buttons.Length; i++)
			{
				if (buttons[i] == null || IsCustomCloseTransform(((Component)buttons[i]).transform))
				{
					continue;
				}
				buttons[i].interactable = false;
				buttons[i].enabled = false;
			}
		}

		MonoBehaviour[] behaviours = closeRoot.GetComponentsInChildren<MonoBehaviour>(true);
		for (int i = 0; i < behaviours.Length; i++)
		{
			MonoBehaviour behaviour = behaviours[i];
			if (behaviour == null || IsCustomCloseTransform(((Component)behaviour).transform))
			{
				continue;
			}
			string typeName = behaviour.GetType().Name ?? string.Empty;
			if (typeName.IndexOf("tooltip", StringComparison.OrdinalIgnoreCase) >= 0
				|| typeName.IndexOf("tip", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				behaviour.enabled = false;
			}
		}
	}

	private static void EnsureCustomCloseButton()
	{
		if (window == null)
		{
			return;
		}

		Transform background = ((Component)window).transform.Find("Background") ?? ((Component)window).transform;
		Transform existing = background.Find(CustomCloseButtonName);
		GameObject closeObject;
		if (existing == null)
		{
			closeObject = new GameObject(CustomCloseButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
			closeObject.transform.SetParent(background, false);
		}
		else
		{
			closeObject = existing.gameObject;
		}

		RectTransform rect = closeObject.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(1f, 1f);
		rect.anchorMax = new Vector2(1f, 1f);
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.anchoredPosition = new Vector2(-13f, -13f);
		rect.sizeDelta = new Vector2(24f, 24f);

		Image hitArea = closeObject.GetComponent<Image>();
		hitArea.sprite = null;
		hitArea.type = Image.Type.Simple;
		hitArea.color = new Color(0.19f, 0.21f, 0.17f, 0.98f);
		hitArea.raycastTarget = true;

		Button button = closeObject.GetComponent<Button>();
		button.targetGraphic = hitArea;
		button.transition = Selectable.Transition.None;
		button.onClick.RemoveAllListeners();
		button.onClick.AddListener(CloseRankWindow);
		button.enabled = true;
		button.interactable = true;

		Transform glyphTransform = closeObject.transform.Find(TransparentCloseGlyphName);
		Text glyph = glyphTransform == null ? null : glyphTransform.GetComponent<Text>();
		if (glyph == null)
		{
			GameObject glyphObject = new GameObject(TransparentCloseGlyphName, typeof(RectTransform), typeof(Text));
			glyphObject.transform.SetParent(closeObject.transform, false);
			SetRect(glyphObject, Vector2.zero, Vector2.one, offsetMin: Vector2.zero, offsetMax: Vector2.zero);
			glyph = glyphObject.GetComponent<Text>();
			glyph.raycastTarget = false;
		}
		glyph.text = "×";
		glyph.alignment = TextAnchor.MiddleCenter;
		glyph.fontSize = 19;
		glyph.fontStyle = FontStyle.Bold;
		glyph.color = new Color(0.92f, 0.08f, 0.04f, 1f);
		if (glyph.font == null)
		{
			glyph.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		}
		glyph.enabled = true;
		glyph.maskable = true;
		try { glyph.canvasRenderer.SetAlpha(1f); } catch (System.Exception xjCaughtCloseGlyph) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Rank/XjRankWindow.cs:EnsureCustomCloseButton", xjCaughtCloseGlyph); }
		closeObject.transform.SetAsLastSibling();
	}

	private static void CloseRankWindow()
	{
		if (window == null)
		{
			return;
		}

		GameObject windowObject = ((Component)window).gameObject;
		if (windowObject == null || !windowObject.activeInHierarchy) return;

		RecycleVisibleCards();
		// 普通关闭也直接走 hide，避免对“当前就是本窗口”的 showWindow 触发 showSameWindow 重入。
		window.hide();
	}

	private static bool IsCustomCloseTransform(Transform transform)
	{
		while (transform != null)
		{
			string name = transform.name ?? string.Empty;
			if (string.Equals(name, CustomCloseButtonName, StringComparison.Ordinal)
				|| string.Equals(name, TransparentCloseGlyphName, StringComparison.Ordinal))
			{
				return true;
			}
			transform = transform.parent;
		}
		return false;
	}

	private static bool LooksLikeNativeCloseTransform(Transform transform)
	{
		string name = transform == null ? string.Empty : transform.name ?? string.Empty;
		return name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0
			|| name.IndexOf("buttonclose", StringComparison.OrdinalIgnoreCase) >= 0
			|| string.Equals(name, "x", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(name, "X", StringComparison.Ordinal);
	}

	private static bool IsNativeCloseTransform(Transform transform)
	{
		while (transform != null)
		{
			if (IsCustomCloseTransform(transform))
			{
				return false;
			}
			if (LooksLikeNativeCloseTransform(transform))
			{
				return true;
			}
			transform = transform.parent;
		}
		return false;
	}


	private static Transform FindCloseButtonTransform()
	{
		Transform root = ((Component)window).transform;
		string[] paths =
		{
			"CloseButton",
			"ButtonClose",
			"Close",
			"Background/CloseButton",
			"Background/ButtonClose",
			"Background/Close"
		};
		for (int i = 0; i < paths.Length; i++)
		{
			Transform found = root.Find(paths[i]);
			if (found != null)
			{
				return found;
			}
		}

		Button[] buttons = ((Component)window).GetComponentsInChildren<Button>(true);
		for (int i = 0; i < buttons.Length; i++)
		{
			Transform current = ((Component)buttons[i]).transform;
			if (IsCustomCloseTransform(current))
			{
				continue;
			}
			string name = current.name ?? string.Empty;
			if (name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("x", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return current;
			}
		}
		return null;
	}
}
