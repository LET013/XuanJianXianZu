using UnityEngine;

namespace XuanJianVNext.UI.Foundation;

/// <summary>
/// IMGUI通用绘制原语。页面只允许外层纵向滚动，组件自身不创建横向滚动。
/// </summary>
internal static class XjUiImGuiPrimitives
{
    internal static void DrawWrappedLabel(string text, GUIStyle style, float availableWidth, params GUILayoutOption[] options)
    {
        GUIStyle resolved = style ?? GUI.skin.label;
        bool previousWrap = resolved.wordWrap;
        TextClipping previousClipping = resolved.clipping;
        resolved.wordWrap = true;
        resolved.clipping = TextClipping.Overflow;
        float height = XjUiLayoutMetrics.MeasureWrappedText(resolved, XjUiSafeText.Player(text, string.Empty), availableWidth);
        GUILayout.Label(XjUiSafeText.Rich(text), resolved, CombineHeight(options, height));
        resolved.wordWrap = previousWrap;
        resolved.clipping = previousClipping;
    }

    internal static void DrawThreeLineCard(in XjUiEntityCard3Line model, GUIStyle lineStyle, float width)
    {
        GUIStyle style = lineStyle ?? GUI.skin.label;
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.Height(XjUiLayoutMetrics.ThreeLineCardHeight));
        GUILayout.Label(XjUiSafeText.Rich(XjUiLayoutMetrics.EllipsizeSingleLine(style, model.Name, width - 16f)), style);
        GUILayout.Label(XjUiSafeText.Rich(model.PrimaryValue), style);
        GUILayout.Label(XjUiSafeText.Rich(XjUiLayoutMetrics.EllipsizeSingleLine(style, model.RouteAndRealm, width - 16f)), style);
        GUILayout.EndVertical();
    }

    private static GUILayoutOption[] CombineHeight(GUILayoutOption[] options, float height)
    {
        int count = options?.Length ?? 0;
        GUILayoutOption[] combined = new GUILayoutOption[count + 1];
        for (int i = 0; i < count; i++) combined[i] = options[i];
        combined[count] = GUILayout.Height(height);
        return combined;
    }
}
