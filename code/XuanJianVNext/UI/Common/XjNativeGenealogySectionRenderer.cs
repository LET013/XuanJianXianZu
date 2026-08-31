using UnityEngine;
using UnityEngine.UI;

namespace XuanJianVNext.UI.Common;

internal static class XjNativeGenealogySectionRenderer
{
	internal static bool TryPrepare(
		Transform sectionRoot,
		string title,
		Font fallbackFont,
		in XjIconShelfMetrics metrics,
		out Transform contentGrid,
		bool clearChildren = true)
	{
		contentGrid = null;
		if (sectionRoot == null)
		{
			return false;
		}

		contentGrid = FindContentGrid(sectionRoot);
		if (contentGrid == null || contentGrid == sectionRoot)
		{
			contentGrid = CreateContentGrid(sectionRoot);
		}

		Text titleText = FindTitle(sectionRoot, contentGrid) ?? CreateTitle(sectionRoot, fallbackFont);
		ApplyTitle(sectionRoot, contentGrid, titleText, title, fallbackFont);
		if (clearChildren)
		{
			ClearChildren(contentGrid);
		}
		contentGrid.gameObject.SetActive(true);

		float frameWidth = XjNativeGenealogyLayout.ResolveFrameWidth(sectionRoot);
		float gridWidth = XjNativeGenealogyLayout.ResolveGridWidth(frameWidth);
		int columns = metrics.IconMode
			? XjNativeGenealogyLayout.ResolveColumnCount(gridWidth, metrics.CellSize, metrics.Spacing, metrics.Columns)
			: 1;
		float frameHeight = XjIconShelfRenderer.ResolveFrameHeight(metrics, columns);
		float sectionHeight = XjIconShelfRenderer.ResolveSectionHeight(frameHeight);

		XjNativeGenealogyLayout.ApplyFixedWidth(sectionRoot, frameWidth);
		LayoutElement sectionLayout = sectionRoot.GetComponent<LayoutElement>()
			?? sectionRoot.gameObject.AddComponent<LayoutElement>();
		sectionLayout.minHeight = sectionHeight;
		sectionLayout.preferredHeight = sectionHeight;
		sectionLayout.flexibleHeight = 0f;

		RectTransform sectionRect = sectionRoot.GetComponent<RectTransform>();
		if (sectionRect != null)
		{
			sectionRect.sizeDelta = new Vector2(frameWidth, sectionHeight);
		}

		LayoutElement gridLayoutElement = contentGrid.GetComponent<LayoutElement>()
			?? contentGrid.gameObject.AddComponent<LayoutElement>();
		gridLayoutElement.minWidth = gridWidth;
		gridLayoutElement.preferredWidth = gridWidth;
		gridLayoutElement.flexibleWidth = 0f;
		gridLayoutElement.minHeight = frameHeight;
		gridLayoutElement.preferredHeight = frameHeight;
		gridLayoutElement.flexibleHeight = 0f;

		RectTransform gridRect = contentGrid.GetComponent<RectTransform>();
		if (gridRect != null)
		{
			gridRect.anchorMin = new Vector2(0.5f, 1f);
			gridRect.anchorMax = new Vector2(0.5f, 1f);
			gridRect.pivot = new Vector2(0.5f, 1f);
			gridRect.anchoredPosition = new Vector2(0f, gridRect.anchoredPosition.y);
			gridRect.sizeDelta = new Vector2(gridWidth, frameHeight);
		}

		GridLayoutGroup grid = contentGrid.GetComponent<GridLayoutGroup>()
			?? contentGrid.gameObject.AddComponent<GridLayoutGroup>();
		grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
		grid.startAxis = GridLayoutGroup.Axis.Horizontal;
		grid.childAlignment = TextAnchor.UpperCenter;
		grid.cellSize = metrics.IconMode
			? metrics.CellSize
			: new Vector2(
				Mathf.Max(24f, gridWidth - XjNativeGenealogyLayout.GridPaddingLeft - XjNativeGenealogyLayout.GridPaddingRight),
				metrics.CellSize.y);
		grid.spacing = metrics.Spacing;
		grid.padding = new RectOffset(
			XjNativeGenealogyLayout.GridPaddingLeft,
			XjNativeGenealogyLayout.GridPaddingRight,
			XjNativeGenealogyLayout.GridPaddingTop,
			XjNativeGenealogyLayout.GridPaddingBottom);
		grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		grid.constraintCount = columns;

		Image rootImage = sectionRoot.GetComponent<Image>();
		if (rootImage != null)
		{
			rootImage.raycastTarget = false;
		}
		return true;
	}

	private static Transform FindContentGrid(Transform sectionRoot)
	{
		Transform named = sectionRoot.Find("Grid");
		if (named != null)
		{
			return named;
		}

		GridLayoutGroup grid = sectionRoot.GetComponentInChildren<GridLayoutGroup>(true);
		return grid != null ? grid.transform : null;
	}

	private static Transform CreateContentGrid(Transform sectionRoot)
	{
		GameObject gridObject = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
		gridObject.transform.SetParent(sectionRoot, false);
		RectTransform rect = gridObject.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.5f, 1f);
		rect.anchorMax = new Vector2(0.5f, 1f);
		rect.pivot = new Vector2(0.5f, 1f);
		rect.anchoredPosition = new Vector2(0f, -24f);
		return gridObject.transform;
	}

	private static Text FindTitle(Transform sectionRoot, Transform contentGrid)
	{
		Text[] texts = sectionRoot.GetComponentsInChildren<Text>(true);
		for (int i = 0; i < texts.Length; i++)
		{
			Text text = texts[i];
			if (text != null && !IsDescendantOf(text.transform, contentGrid))
			{
				return text;
			}
		}
		return null;
	}

	private static Text CreateTitle(Transform sectionRoot, Font fallbackFont)
	{
		GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(Text));
		titleObject.transform.SetParent(sectionRoot, false);
		RectTransform rect = titleObject.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(0f, 1f);
		rect.anchorMax = new Vector2(1f, 1f);
		rect.pivot = new Vector2(0.5f, 1f);
		rect.anchoredPosition = new Vector2(0f, 0f);
		rect.sizeDelta = new Vector2(0f, 24f);
		Text text = titleObject.GetComponent<Text>();
		text.font = LocalizedTextManager.current_font ?? fallbackFont;
		return text;
	}

	private static void ApplyTitle(Transform sectionRoot, Transform contentGrid, Text titleText, string title, Font fallbackFont)
	{
		Text[] texts = sectionRoot.GetComponentsInChildren<Text>(true);
		bool titleApplied = false;
		for (int i = 0; i < texts.Length; i++)
		{
			Text text = texts[i];
			if (text == null || IsDescendantOf(text.transform, contentGrid))
			{
				continue;
			}

			if (!titleApplied && (text == titleText || titleText == null))
			{
				ApplyTitleText(text, title, fallbackFont);
				titleApplied = true;
				continue;
			}

			if (!titleApplied)
			{
				ApplyTitleText(text, title, fallbackFont);
				titleApplied = true;
			}
			else
			{
				LocalizedText localized = text.GetComponent<LocalizedText>();
				if (localized != null)
				{
					localized.enabled = false;
				}
				text.text = string.Empty;
				text.raycastTarget = false;
			}
		}

		if (!titleApplied && titleText != null)
		{
			ApplyTitleText(titleText, title, fallbackFont);
		}
	}

	private static void ApplyTitleText(Text text, string title, Font fallbackFont)
	{
		LocalizedText localized = text.GetComponent<LocalizedText>();
		if (localized != null)
		{
			localized.enabled = false;
		}
		text.font = LocalizedTextManager.current_font ?? fallbackFont;
		text.fontSize = 10;
		text.alignment = TextAnchor.MiddleCenter;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.color = Color.white;
		text.supportRichText = false;
		text.raycastTarget = false;
		text.text = title ?? string.Empty;
	}

	private static void ClearChildren(Transform parent)
	{
		for (int i = parent.childCount - 1; i >= 0; i--)
		{
			UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
		}
	}

	private static bool IsDescendantOf(Transform candidate, Transform ancestor)
	{
		for (Transform current = candidate; current != null; current = current.parent)
		{
			if (current == ancestor)
			{
				return true;
			}
		}
		return false;
	}
}
