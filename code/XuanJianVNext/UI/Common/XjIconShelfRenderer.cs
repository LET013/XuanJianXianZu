using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace XuanJianVNext.UI.Common;

internal readonly struct XjIconShelfMetrics
{
	internal readonly int ItemCount;
	internal readonly bool IconMode;
	internal readonly float FrameHeight;
	internal readonly float SectionHeight;
	internal readonly Vector2 CellSize;
	internal readonly Vector2 Spacing;
	internal readonly int Columns;

	internal XjIconShelfMetrics(
		int itemCount,
		bool iconMode,
		float frameHeight,
		float sectionHeight,
		Vector2 cellSize,
		Vector2 spacing,
		int columns)
	{
		ItemCount = Mathf.Max(1, itemCount);
		IconMode = iconMode;
		FrameHeight = frameHeight;
		SectionHeight = sectionHeight;
		CellSize = cellSize;
		Spacing = spacing;
		Columns = columns;
	}
}

internal static class XjIconShelfRenderer
{
	// 原生 UnitGenealogyElement 的稳定内容宽度。0.5.4 家族传承同样以 218 为基准。
	internal const float DefaultFrameWidth = 218f;
	internal const float DefaultContentWidth = 206f;
	internal const float DefaultIconCellSize = 34f;
	internal const float DefaultIconVisualSize = 25f;
	internal const float DefaultRowSpacing = 5f;
	internal const int DefaultIconColumns = 7;

	internal static XjIconShelfMetrics CalculateMetrics(
		int itemCount,
		bool iconMode,
		float textCellWidth = DefaultContentWidth,
		int iconColumns = DefaultIconColumns,
		float iconCellSize = DefaultIconCellSize,
		float rowSpacing = DefaultRowSpacing)
	{
		int safeCount = Mathf.Max(1, itemCount);
		int columns = iconMode ? Mathf.Max(1, iconColumns) : 1;
		float cellWidth = iconMode ? iconCellSize : textCellWidth;
		float cellHeight = iconMode ? iconCellSize : 24f;
		int rows = Mathf.Max(1, Mathf.CeilToInt((float)safeCount / columns));
		float frameHeight = Mathf.Max(42f, rows * cellHeight + (rows - 1) * rowSpacing + 10f);
		float sectionHeight = Mathf.Max(74f, 30f + frameHeight);
		return new XjIconShelfMetrics(
			safeCount,
			iconMode,
			frameHeight,
			sectionHeight,
			new Vector2(cellWidth, cellHeight),
			new Vector2(rowSpacing, rowSpacing),
			columns);
	}

	internal static float ResolveFrameHeight(in XjIconShelfMetrics metrics, int columns)
	{
		int safeColumns = Mathf.Max(1, columns);
		int rows = Mathf.Max(1, Mathf.CeilToInt((float)Mathf.Max(1, metrics.ItemCount) / safeColumns));
		return Mathf.Max(42f,
			rows * metrics.CellSize.y
			+ (rows - 1) * metrics.Spacing.y
			+ 10f);
	}

	internal static float ResolveSectionHeight(float frameHeight)
	{
		return Mathf.Max(74f, 30f + Mathf.Max(42f, frameHeight));
	}

	internal static Transform CreateFrame(
		Transform parent,
		string name,
		float width,
		float height,
		float yOffset,
		in XjIconShelfMetrics metrics,
		Color color)
	{
		if (parent == null)
		{
			return null;
		}

		GameObject frameObject = new GameObject(string.IsNullOrWhiteSpace(name) ? "XjIconShelfFrame" : name, typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(GridLayoutGroup));
		frameObject.transform.SetParent(parent, worldPositionStays: false);
		RectTransform rect = frameObject.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.5f, 1f);
		rect.anchorMax = new Vector2(0.5f, 1f);
		rect.pivot = new Vector2(0.5f, 1f);
		rect.anchoredPosition = new Vector2(0f, yOffset);
		rect.sizeDelta = new Vector2(width, height);

		LayoutElement layout = frameObject.GetComponent<LayoutElement>();
		layout.minWidth = width;
		layout.preferredWidth = width;
		layout.flexibleWidth = 0f;
		layout.minHeight = height;
		layout.preferredHeight = height;
		layout.flexibleHeight = 0f;

		Image image = frameObject.GetComponent<Image>();
		image.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement") ?? image.sprite;
		image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
		image.color = color;
		image.raycastTarget = false;

		GridLayoutGroup grid = frameObject.GetComponent<GridLayoutGroup>();
		grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
		grid.startAxis = GridLayoutGroup.Axis.Horizontal;
		grid.childAlignment = TextAnchor.UpperCenter;
		grid.cellSize = metrics.CellSize;
		grid.spacing = metrics.Spacing;
		grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		grid.constraintCount = Mathf.Max(1, metrics.Columns);

		return frameObject.transform;
	}

	internal static GameObject CreateIconSlot(
		Transform parent,
		string name,
		Sprite sprite,
		string fallbackText,
		int count,
		string tooltipTitle,
		string tooltipDescription,
		string tooltipDetails,
		Font font,
		float cellSize = DefaultIconCellSize,
		float iconSize = DefaultIconVisualSize,
		bool showCountWhenOne = false)
	{
		if (parent == null)
		{
			return null;
		}

		GameObject slot = new GameObject(string.IsNullOrWhiteSpace(name) ? "XjIconShelfSlot" : name, typeof(RectTransform), typeof(LayoutElement), typeof(Image));
		slot.transform.SetParent(parent, worldPositionStays: false);
		slot.transform.localScale = Vector3.one;

		LayoutElement layout = slot.GetComponent<LayoutElement>();
		layout.minWidth = cellSize;
		layout.preferredWidth = cellSize;
		layout.minHeight = cellSize;
		layout.preferredHeight = cellSize;
		layout.flexibleWidth = 0f;
		layout.flexibleHeight = 0f;

		Image background = slot.GetComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		background.type = background.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
		background.color = new Color(1f, 1f, 1f, 0f);
		background.raycastTarget = true;

		if (sprite != null)
		{
			GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
			icon.transform.SetParent(slot.transform, worldPositionStays: false);
			RectTransform iconRect = icon.GetComponent<RectTransform>();
			iconRect.anchorMin = new Vector2(0.5f, 0.5f);
			iconRect.anchorMax = new Vector2(0.5f, 0.5f);
			iconRect.pivot = new Vector2(0.5f, 0.5f);
			iconRect.sizeDelta = new Vector2(iconSize, iconSize);
			iconRect.anchoredPosition = Vector2.zero;
			Image image = icon.GetComponent<Image>();
			image.sprite = sprite;
			image.preserveAspect = true;
			image.raycastTarget = false;
		}
		else
		{
			CreateFallbackText(slot.transform, fallbackText, font);
		}

		if (count > 1 || (showCountWhenOne && count > 0))
		{
			CreateCountText(slot.transform, count, font);
		}

		TipButton tip = slot.GetComponent<TipButton>() ?? slot.AddComponent<TipButton>();
		XjNativeHoverTooltip.Ensure(
			tip,
			string.IsNullOrWhiteSpace(tooltipTitle) ? "玄鉴" : tooltipTitle.Trim(),
			tooltipDescription ?? string.Empty,
			tooltipDetails ?? string.Empty);
		return slot;
	}

	private static void CreateFallbackText(Transform parent, string text, Font font)
	{
		GameObject textObject = new GameObject("EmptyText", typeof(RectTransform), typeof(Text));
		textObject.transform.SetParent(parent, worldPositionStays: false);
		RectTransform rect = textObject.GetComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = new Vector2(2f, 2f);
		rect.offsetMax = new Vector2(-2f, -2f);
		Text label = textObject.GetComponent<Text>();
		label.font = LocalizedTextManager.current_font ?? font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.fontSize = 7;
		label.resizeTextForBestFit = true;
		label.resizeTextMinSize = 5;
		label.resizeTextMaxSize = 8;
		label.alignment = TextAnchor.MiddleCenter;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		label.color = Color.white;
		label.raycastTarget = false;
		label.text = string.IsNullOrWhiteSpace(text) ? "玄鉴" : text.Trim();
	}

	private static void CreateCountText(Transform parent, int count, Font font)
	{
		GameObject countObject = new GameObject("Count", typeof(RectTransform), typeof(Text));
		countObject.transform.SetParent(parent, worldPositionStays: false);
		RectTransform rect = countObject.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(1f, 0f);
		rect.anchorMax = new Vector2(1f, 0f);
		rect.pivot = new Vector2(1f, 0f);
		rect.sizeDelta = new Vector2(22f, 12f);
		rect.anchoredPosition = new Vector2(-1f, 1f);
		Text label = countObject.GetComponent<Text>();
		label.font = LocalizedTextManager.current_font ?? font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.fontSize = 8;
		label.resizeTextForBestFit = true;
		label.resizeTextMinSize = 5;
		label.resizeTextMaxSize = 9;
		label.alignment = TextAnchor.LowerRight;
		label.horizontalOverflow = HorizontalWrapMode.Overflow;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		label.color = Color.white;
		label.raycastTarget = false;
		label.text = count.ToString(CultureInfo.InvariantCulture);
	}
}
