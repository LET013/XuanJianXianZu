using XuanJianVNext.Core;

namespace XuanJianVNext.UI.Foundation;

/// <summary>
/// 玩家界面文本的统一出口：中文映射、内部标识阻断、富文本转义。
/// </summary>
internal static class XjUiSafeText
{
    internal static string Player(string value, string fallback = "—")
    {
        return XjDisplayNameSanitizer.Clean(value, fallback);
    }

    internal static string GameTerm(string value, string fallback = "未定")
    {
        return XjDisplayNameSanitizer.GameTerm(value, fallback);
    }

    internal static string Rich(string value, string fallback = "")
    {
        string clean = Player(value, fallback);
        return EscapeRichText(clean);
    }

    internal static string EscapeRichText(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
