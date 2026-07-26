using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;

namespace XuanJianVNext.UI.Common;

internal readonly struct XjRecordTableRow
{
	internal readonly string Title;
	internal readonly string Details;
	internal readonly float Height;

	internal XjRecordTableRow(string title, string details, float height = 56f)
	{
		Title = title ?? string.Empty;
		Details = details ?? string.Empty;
		Height = height;
	}
}

/// <summary>
/// Shared 0.5.4-style record window shell. Restored visual features:
/// alternating row backgrounds, Shadow effects, navigation buttons, Outline on title.
/// </summary>
internal static class XjRecordTableWindow
{
	private sealed class WindowState
	{
		internal ScrollWindow Window;
		internal RectTransform Content;
		internal ScrollRect Scroll;
		internal Text EmptyText;
		internal Text BottomSummary;
	}

	private static readonly Dictionary<string, WindowState> States = new Dictionary<string, WindowState>();

	internal static void Show(
		string windowId,
		string titleKey,
		string iconPath,
		string summary,
		string empty,
		IReadOnlyList<XjRecordTableRow> rows,
		float width = 500f,
		float height = 380f)
	{
		bool createdNow = !States.TryGetValue(windowId, out WindowState state);
		if (createdNow)
		{
			state = Create(windowId, titleKey, iconPath, width, height);
			if (state == null)
			{
				return;
			}
			States[windowId] = state;
		}

		Refresh(state, empty, rows);
		XjWindowOpenGuard.Show(state.Window, windowId, createdNow);
	}

	private static WindowState Create(string windowId, string titleKey, string iconPath, float width, float height)
	{
		ScrollWindow window = WindowCreator.CreateEmptyWindow(windowId, titleKey, iconPath);
		Transform background = window == null ? null : ((Component)window).transform.Find("Background");
		if (background == null)
		{
			return null;
		}

		RectTransform backgroundRect = background.GetComponent<RectTransform>();
		if (backgroundRect != null)
		{
			backgroundRect.sizeDelta = new Vector2(width, height);
		}

		// 滚动区
		GameObject scrollObject = new GameObject("RecordScroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		scrollObject.transform.SetParent(background, false);
		RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
		scrollRect.anchorMin = Vector2.zero;
		scrollRect.anchorMax = Vector2.one;
		scrollRect.offsetMin = new Vector2(8f, 28f);
		scrollRect.offsetMax = new Vector2(-8f, -70f);
		Image scrollImage = scrollObject.GetComponent<Image>();
		scrollImage.color = new Color(0f, 0f, 0f, 0.28f);
		scrollImage.raycastTarget = true;
		scrollObject.GetComponent<Mask>().showMaskGraphic = true;

		GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform));
		viewportObject.transform.SetParent(scrollObject.transform, false);
		RectTransform viewport = viewportObject.GetComponent<RectTransform>();
		viewport.anchorMin = Vector2.zero;
		viewport.anchorMax = Vector2.one;
		viewport.offsetMin = Vector2.zero;
		viewport.offsetMax = Vector2.zero;

		GameObject contentObject = new GameObject("Content", typeof(RectTransform));
		contentObject.transform.SetParent(viewportObject.transform, false);
		RectTransform content = contentObject.GetComponent<RectTransform>();
		content.anchorMin = new Vector2(0f, 1f);
		content.anchorMax = new Vector2(1f, 1f);
		content.pivot = new Vector2(0.5f, 1f);
		content.anchoredPosition = Vector2.zero;
		content.sizeDelta = Vector2.zero;

		ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
		scroll.horizontal = false;
		scroll.vertical = true;
		scroll.movementType = ScrollRect.MovementType.Clamped;
		scroll.scrollSensitivity = 30f;
		scroll.viewport = viewport;
		scroll.content = content;

		Text emptyText = CreateText(background, "Empty", 13, TextAnchor.MiddleCenter, new Color(0.65f, 0.65f, 0.65f));
		SetRect(emptyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
			new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(260f, 48f));

		// 底部导航按钮（同 0.5.4 真君录/果位权柄）
		CreateNavButton(background, "ToTop", "到顶", new Vector2(-80f, 12f), () =>
		{
			if (scroll != null) scroll.verticalNormalizedPosition = 1f;
		});
		CreateNavButton(background, "ToBottom", "到底", new Vector2(80f, 12f), () =>
		{
			if (scroll != null) scroll.verticalNormalizedPosition = 0f;
		});

		// 底部计数（同 0.5.4）
		Text bottomSummary = CreateText(background, "BottomSummary", 9, TextAnchor.MiddleCenter,
			new Color(0.88f, 0.8f, 0.55f, 0.95f));
		SetRect(bottomSummary.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
			new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(-16f, 16f));

		return new WindowState { Window = window, Content = content, Scroll = scroll, EmptyText = emptyText, BottomSummary = bottomSummary };
	}

	private static void Refresh(WindowState state, string empty, IReadOnlyList<XjRecordTableRow> rows)
	{
		int count = rows != null ? rows.Count : 0;
		state.BottomSummary.text = (count > 0)
			? "共 " + count.ToString() + " 条记录"
			: "共 0 条记录";

		for (int i = state.Content.childCount - 1; i >= 0; i--)
		{
			Object.Destroy(state.Content.GetChild(i).gameObject);
		}

		bool hasRows = count > 0;
		state.EmptyText.gameObject.SetActive(!hasRows);
		state.EmptyText.text = empty ?? string.Empty;
		if (!hasRows)
		{
			state.Content.sizeDelta = Vector2.zero;
			return;
		}

		float y = -4f;
		const float rowSpacing = 3f;
		for (int i = 0; i < count; i++)
		{
			CreateRow(state.Content, rows[i], i, y);
			y -= rows[i].Height + rowSpacing;
		}
		state.Content.sizeDelta = new Vector2(0f, Mathf.Max(0f, -y + 2f));
	}

	private static void CreateRow(Transform parent, in XjRecordTableRow row, int index, float y)
	{
		GameObject rowObject = new GameObject("RecordRow", typeof(RectTransform), typeof(Image));
		rowObject.transform.SetParent(parent, false);
		RectTransform rowRect = rowObject.GetComponent<RectTransform>();
		rowRect.anchorMin = new Vector2(0f, 1f);
		rowRect.anchorMax = new Vector2(1f, 1f);
		rowRect.pivot = new Vector2(0.5f, 1f);
		rowRect.anchoredPosition = new Vector2(0f, y);
		rowRect.sizeDelta = new Vector2(0f, row.Height);

		Image background = rowObject.GetComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		background.type = background.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
		// 交替行背景（同 0.5.4）
		background.color = (index % 2 == 0)
			? new Color(1f, 1f, 1f, 0.92f)
			: new Color(0.92f, 0.92f, 0.92f, 0.92f);

		// 行阴影（同 0.5.4）
		Shadow rowShadow = rowObject.AddComponent<Shadow>();
		rowShadow.effectColor = new Color(0f, 0f, 0f, 0.16f);
		rowShadow.effectDistance = new Vector2(0f, -1f);

		// 标题行（金色，同 0.5.4）
		Text title = CreateText(rowObject.transform, "Title", 9, TextAnchor.UpperLeft, new Color(0.95f, 0.82f, 0.55f));
		SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
			new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(-24f, 20f));
		title.text = row.Title;
		Shadow titleShadow = title.gameObject.AddComponent<Shadow>();
		titleShadow.effectColor = new Color(0f, 0f, 0f, 0.25f);
		titleShadow.effectDistance = new Vector2(0f, -1f);

		// 详情（白色，支持换行）
		Text details = CreateText(rowObject.transform, "Details", 8, TextAnchor.UpperLeft, Color.white);
		SetRect(details.rectTransform, Vector2.zero, Vector2.one,
			new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(-24f, -row.Height + 10f));
		details.text = row.Details;
		details.horizontalOverflow = HorizontalWrapMode.Wrap;
		Shadow detailsShadow = details.gameObject.AddComponent<Shadow>();
		detailsShadow.effectColor = new Color(0f, 0f, 0f, 0.22f);
		detailsShadow.effectDistance = new Vector2(0f, -1f);
	}

	private static Text CreateText(Transform parent, string name, int size, TextAnchor alignment, Color color)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
		obj.transform.SetParent(parent, false);
		Text text = obj.GetComponent<Text>();
		text.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		text.fontSize = size;
		text.alignment = alignment;
		text.color = color;
		text.supportRichText = true;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Truncate;
		text.raycastTarget = false;
		return text;
	}

	private static void CreateNavButton(Transform parent, string name, string text, Vector2 anchoredPos, System.Action onClick)
	{
		GameObject buttonObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
		buttonObj.transform.SetParent(parent, false);
		RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
		buttonRect.anchorMin = new Vector2(0.5f, 0f);
		buttonRect.anchorMax = new Vector2(0.5f, 0f);
		buttonRect.pivot = new Vector2(0.5f, 0f);
		buttonRect.anchoredPosition = anchoredPos;
		buttonRect.sizeDelta = new Vector2(60f, 20f);

		Button button = buttonObj.GetComponent<Button>();
		Image buttonImage = buttonObj.GetComponent<Image>();
		buttonImage.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		buttonImage.type = Image.Type.Sliced;
		buttonImage.color = new Color(0.2f, 0.15f, 0.05f, 0.8f);

		GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
		textObj.transform.SetParent(buttonObj.transform, false);
		RectTransform textRect = textObj.GetComponent<RectTransform>();
		textRect.anchorMin = Vector2.zero;
		textRect.anchorMax = Vector2.one;
		textRect.offsetMin = Vector2.zero;
		textRect.offsetMax = Vector2.zero;
		Text label = textObj.GetComponent<Text>();
		label.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.fontSize = 9;
		label.alignment = TextAnchor.MiddleCenter;
		label.color = new Color(0.95f, 0.82f, 0.55f);
		label.text = text;

		button.onClick.AddListener(new UnityEngine.Events.UnityAction(onClick));
	}

	private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
	{
		rect.anchorMin = min;
		rect.anchorMax = max;
		rect.pivot = pivot;
		rect.anchoredPosition = position;
		rect.sizeDelta = size;
	}
}
