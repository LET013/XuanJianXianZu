using UnityEngine;

namespace XuanJianVNext.UI.Foundation;

/// <summary>
/// 0.9.9统一界面设计令牌。所有玄鉴自绘窗口共用同一组层级、字号与间距，
/// 页面不再各自硬编码一套“差不多”的颜色和9/10/11号字。
/// </summary>
internal enum XjUiTextRole : byte
{
    WindowTitle = 0,
    SectionTitle = 1,
    Body = 2,
    Meta = 3,
    Emphasis = 4
}

internal static class XjUiTheme
{
    internal const float SpaceXs = 4f;
    internal const float SpaceSm = 6f;
    internal const float SpaceMd = 10f;
    internal const float SpaceLg = 14f;
    internal const float CardPadding = 10f;
    internal const float MinimumCardHeight = 78f;
    internal const float MinimumKeyValueCellWidth = 150f;

    // Unity UI typography. IMGUI uses the current WorldBox skin as its base size instead.
    internal const int RankWindowTitleSize = 15;
    internal const int RankSectionTitleSize = 10;
    internal const int RankBodySize = 9;
    internal const int RankMetaSize = 7;
    internal const int RankIndexSize = 10;

    internal static readonly Color SurfaceWindow = new Color(0.20f, 0.27f, 0.23f, 1f);
    internal static readonly Color SurfacePanel = new Color(0.16f, 0.23f, 0.20f, 0.98f);
    internal static readonly Color SurfaceRaised = new Color(0.29f, 0.31f, 0.25f, 0.98f);
    internal static readonly Color SurfaceDeep = new Color(0.09f, 0.14f, 0.12f, 1f);
    internal static readonly Color SurfaceScroll = new Color(0.07f, 0.11f, 0.095f, 0.99f);
    internal static readonly Color SurfaceGrid = new Color(0.16f, 0.18f, 0.15f, 0.98f);
    internal static readonly Color Frame = new Color(0.31f, 0.35f, 0.28f, 1f);
    internal static readonly Color FrameGold = new Color(0.67f, 0.50f, 0.20f, 0.92f);

    internal static readonly Color AccentGold = new Color(1f, 0.84f, 0.38f, 1f);
    internal static readonly Color AccentGoldMuted = new Color(0.88f, 0.68f, 0.29f, 0.90f);
    internal static readonly Color AccentMint = new Color(0.53f, 0.83f, 0.72f, 0.90f);
    internal static readonly Color AccentBlue = new Color(0.45f, 0.84f, 1f, 1f);
    internal static readonly Color AccentGreen = new Color(0.48f, 0.95f, 0.62f, 1f);
    internal static readonly Color TextPrimary = new Color(0.92f, 0.94f, 0.87f, 1f);
    internal static readonly Color TextMuted = new Color(0.68f, 0.70f, 0.64f, 1f);
    internal static readonly Color Danger = new Color(0.85f, 0.12f, 0.08f, 1f);

    internal static GUIStyle CreateImGuiLabel(
        XjUiTextRole role = XjUiTextRole.Body,
        TextAnchor alignment = TextAnchor.UpperLeft,
        bool wordWrap = true)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = alignment,
            wordWrap = wordWrap,
            clipping = wordWrap ? TextClipping.Overflow : TextClipping.Clip,
            richText = true
        };
        int baseSize = GUI.skin.label.fontSize > 0 ? GUI.skin.label.fontSize : 14;
        style.fontSize = role switch
        {
            XjUiTextRole.WindowTitle => baseSize + 7,
            XjUiTextRole.SectionTitle => baseSize + 3,
            XjUiTextRole.Meta => Mathf.Max(9, baseSize - 1),
            XjUiTextRole.Emphasis => baseSize + 1,
            _ => baseSize
        };
        style.fontStyle = role == XjUiTextRole.WindowTitle
            || role == XjUiTextRole.SectionTitle
            || role == XjUiTextRole.Emphasis
            ? FontStyle.Bold
            : FontStyle.Normal;
        return style;
    }

    internal static GUIStyle CreateImGuiButton(
        XjUiTextRole role = XjUiTextRole.Body,
        TextAnchor alignment = TextAnchor.MiddleCenter,
        bool wordWrap = true)
    {
        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            alignment = alignment,
            wordWrap = wordWrap,
            clipping = wordWrap ? TextClipping.Overflow : TextClipping.Clip,
            richText = true,
            padding = new RectOffset(10, 10, 7, 7)
        };
        int baseSize = GUI.skin.button.fontSize > 0 ? GUI.skin.button.fontSize : 14;
        style.fontSize = role switch
        {
            XjUiTextRole.WindowTitle => baseSize + 5,
            XjUiTextRole.SectionTitle => baseSize + 2,
            XjUiTextRole.Meta => Mathf.Max(9, baseSize - 1),
            XjUiTextRole.Emphasis => baseSize + 1,
            _ => baseSize
        };
        style.fontStyle = role == XjUiTextRole.WindowTitle
            || role == XjUiTextRole.SectionTitle
            || role == XjUiTextRole.Emphasis
            ? FontStyle.Bold
            : FontStyle.Normal;
        return style;
    }

}
