using UnityEngine;
using UnityEngine.UI;

namespace XuanJianVNext.UI.Common;

/// <summary>
/// UnitGenealogyElement 借壳 UI 的唯一横向布局入口。
/// 宽度只服从原生家谱内容区/父级视口，不允许业务模块自行扩大画布。
/// </summary>
internal static class XjNativeGenealogyLayout
{
	internal const float NativeFrameWidth = XjIconShelfRenderer.DefaultFrameWidth;
	internal const float MinimumFrameWidth = 120f;
	internal const float GridHorizontalInset = 12f;
	internal const int GridPaddingLeft = 8;
	internal const int GridPaddingRight = 8;
	internal const int GridPaddingTop = 5;
	internal const int GridPaddingBottom = 5;

	internal static float ResolveFrameWidth(Transform target)
	{
		float resolved = NativeFrameWidth;
		Transform current = target;
		int depth = 0;
		while (current != null && depth++ < 10)
		{
			RectTransform rect = current.GetComponent<RectTransform>();
			if (rect != null)
			{
				float candidate = Mathf.Abs(rect.rect.width);
				if (candidate < 1f)
				{
					candidate = Mathf.Abs(rect.sizeDelta.x);
				}

				if (candidate >= MinimumFrameWidth)
				{
					resolved = Mathf.Min(resolved, candidate);
				}
			}

			LayoutElement layout = current.GetComponent<LayoutElement>();
			if (layout != null)
			{
				float preferred = layout.preferredWidth;
				if (preferred >= MinimumFrameWidth)
				{
					resolved = Mathf.Min(resolved, preferred);
				}
				float minimum = layout.minWidth;
				if (minimum >= MinimumFrameWidth)
				{
					resolved = Mathf.Min(resolved, minimum);
				}
			}
			current = current.parent;
		}

		return Mathf.Clamp(resolved, MinimumFrameWidth, NativeFrameWidth);
	}

	internal static float ResolveGridWidth(float frameWidth)
	{
		return Mathf.Max(108f, frameWidth - GridHorizontalInset);
	}

	internal static int ResolveColumnCount(
		float gridWidth,
		Vector2 cellSize,
		Vector2 spacing,
		int requestedColumns)
	{
		float innerWidth = Mathf.Max(1f, gridWidth - GridPaddingLeft - GridPaddingRight);
		float stride = Mathf.Max(1f, cellSize.x + spacing.x);
		int fittingColumns = Mathf.FloorToInt((innerWidth + spacing.x) / stride);
		return Mathf.Clamp(fittingColumns, 1, Mathf.Max(1, requestedColumns));
	}

	internal static void ApplyFixedWidth(Transform target, float width)
	{
		if (target == null)
		{
			return;
		}

		float safeWidth = Mathf.Clamp(width, MinimumFrameWidth, NativeFrameWidth);
		LayoutElement layout = target.GetComponent<LayoutElement>() ?? target.gameObject.AddComponent<LayoutElement>();
		layout.minWidth = safeWidth;
		layout.preferredWidth = safeWidth;
		layout.flexibleWidth = 0f;

		RectTransform rect = target.GetComponent<RectTransform>();
		if (rect == null)
		{
			return;
		}

		rect.anchorMin = new Vector2(0.5f, 1f);
		rect.anchorMax = new Vector2(0.5f, 1f);
		rect.pivot = new Vector2(0.5f, 1f);
		rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
		rect.sizeDelta = new Vector2(safeWidth, rect.sizeDelta.y);
	}

	internal static float NormalizeRoot(Transform root)
	{
		if (root == null)
		{
			return NativeFrameWidth;
		}

		float width = ResolveFrameWidth(root);
		ApplyFixedWidth(root, width);

		VerticalLayoutGroup stack = root.GetComponent<VerticalLayoutGroup>();
		if (stack != null)
		{
			stack.childAlignment = TextAnchor.UpperCenter;
			stack.childControlWidth = false;
			stack.childForceExpandWidth = false;
		}

		return width;
	}
}
