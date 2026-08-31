using System;
using UnityEngine;

namespace XuanJianVNext.UI.Foundation;

/// <summary>
/// UI统一测量规则。可变文本按实际宽度测高；只有固定三行实体卡允许固定高度。
/// </summary>
internal static class XjUiLayoutMetrics
{
    internal const float DefaultGap = 8f;
    internal const float DefaultVerticalPadding = 8f;
    internal const float ThreeLineCardHeight = 78f;

    internal static int ResolveGridColumns(float availableWidth, float minimumCardWidth, float gap = DefaultGap, int maximumColumns = 4)
    {
        if (availableWidth <= 0f || minimumCardWidth <= 0f) return 1;
        int columns = Mathf.FloorToInt((availableWidth + gap) / (minimumCardWidth + gap));
        return Mathf.Clamp(columns, 1, Math.Max(1, maximumColumns));
    }

    internal static float ResolveGridCardWidth(float availableWidth, int columns, float gap = DefaultGap)
    {
        int safeColumns = Math.Max(1, columns);
        return Mathf.Max(1f, (availableWidth - gap * (safeColumns - 1)) / safeColumns);
    }

    internal static float MeasureWrappedText(GUIStyle style, string text, float availableWidth, float verticalPadding = DefaultVerticalPadding)
    {
        if (style == null) return verticalPadding * 2f + 20f;
        float width = Mathf.Max(1f, availableWidth - style.padding.horizontal);
        float measured = style.CalcHeight(new GUIContent(text ?? string.Empty), width);
        return Mathf.Max(style.lineHeight, measured) + Math.Max(0f, verticalPadding) * 2f;
    }

    internal static string EllipsizeSingleLine(GUIStyle style, string value, float availableWidth)
    {
        string text = value ?? string.Empty;
        if (style == null || availableWidth <= 0f || style.CalcSize(new GUIContent(text)).x <= availableWidth) return text;
        const string ellipsis = "…";
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            string candidate = text.Substring(0, middle) + ellipsis;
            if (style.CalcSize(new GUIContent(candidate)).x <= availableWidth) low = middle;
            else high = middle - 1;
        }
        return text.Substring(0, Math.Max(0, low)) + ellipsis;
    }
}
