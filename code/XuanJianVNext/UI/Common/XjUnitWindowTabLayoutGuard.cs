using System.Collections.Generic;
using UnityEngine;

namespace XuanJianVNext.UI.Common;

internal static class XjUnitWindowTabLayoutGuard
{
	private const int ComfortableTabCount = 7;
	private const float MinimumScale = 0.54f;
	private const float CompactGap = 1f;
	private const float MinimumStep = 22f;

	internal static void Apply(ScrollWindow scrollWindow)
	{
		Transform root = scrollWindow?.tabs?.transform;
		if (root == null)
		{
			return;
		}

		List<XjUnitWindowTabOriginalLayout> tabs = new List<XjUnitWindowTabOriginalLayout>();
		for (int i = 0; i < root.childCount; i++)
		{
			Transform child = root.GetChild(i);
			if (child?.GetComponent<WindowMetaTab>() != null)
			{
				XjUnitWindowTabOriginalLayout original = child.GetComponent<XjUnitWindowTabOriginalLayout>()
					?? child.gameObject.AddComponent<XjUnitWindowTabOriginalLayout>();
				original.CaptureIfNeeded();
				tabs.Add(original);
			}
		}

		int count = tabs.Count;
		float scale = count <= ComfortableTabCount
			? 1f
			: Mathf.Clamp((float)ComfortableTabCount / count, MinimumScale, 1f);
		if (tabs.Count == 0)
		{
			return;
		}

		tabs.Sort(CompareTabsByOriginalPosition);
		float maxHeight = 0f;
		for (int i = 0; i < tabs.Count; i++)
		{
			maxHeight = Mathf.Max(maxHeight, tabs[i].OriginalHeight);
		}
		float step = Mathf.Max(MinimumStep, maxHeight * scale * 0.86f + CompactGap);
		float topY = tabs[0].OriginalAnchoredPosition.y;
		for (int i = 0; i < tabs.Count; i++)
		{
			Vector2 originalPosition = tabs[i].OriginalAnchoredPosition;
			tabs[i].Apply(scale, new Vector2(originalPosition.x, topY - i * step));
		}
	}

	private static int CompareTabsByOriginalPosition(XjUnitWindowTabOriginalLayout left, XjUnitWindowTabOriginalLayout right)
	{
		int y = right.OriginalAnchoredPosition.y.CompareTo(left.OriginalAnchoredPosition.y);
		return y != 0 ? y : left.OriginalSiblingIndex.CompareTo(right.OriginalSiblingIndex);
	}
}

internal sealed class XjUnitWindowTabOriginalLayout : MonoBehaviour
{
	private bool captured;
	private Vector3 originalScale;
	private Vector2 originalSize;
	private Vector2 originalAnchoredPosition;
	private int originalSiblingIndex;

	internal Vector2 OriginalAnchoredPosition => originalAnchoredPosition;
	internal float OriginalHeight => originalSize.y > 0f ? originalSize.y : 36f;
	internal int OriginalSiblingIndex => originalSiblingIndex;

	internal void CaptureIfNeeded()
	{
		RectTransform rect = transform as RectTransform;
		if (!captured)
		{
			captured = true;
			originalScale = transform.localScale;
			originalSize = rect != null ? rect.sizeDelta : Vector2.zero;
			originalAnchoredPosition = rect != null ? rect.anchoredPosition : Vector2.zero;
			originalSiblingIndex = transform.GetSiblingIndex();
		}
	}

	internal void Apply(float scale, Vector2 packedAnchoredPosition)
	{
		CaptureIfNeeded();
		RectTransform rect = transform as RectTransform;
		transform.localScale = originalScale * scale;
		if (rect != null && originalSize.y > 0f)
		{
			rect.sizeDelta = new Vector2(originalSize.x, originalSize.y * scale);
			rect.anchoredPosition = packedAnchoredPosition;
		}
	}
}
