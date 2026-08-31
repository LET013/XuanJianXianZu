using System;

namespace XuanJianVNext.Systems.History;

/// <summary>
/// 史册只收录可正常显示的中文人物、家族与宗门名。尊号、境界后缀和中文标点可保留，
/// 拉丁字母、数字编号、noname 与内部键值一律视为无效名称，避免代码名进入天下纪事和三卷史册。
/// </summary>
internal static class XjHistoryNamePolicy
{
	internal static bool IsChineseDisplayName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0 || HasInternalNameMarker(text) || IsPlaceholderName(text)) return false;
		bool hasHan = false;
		for (int i = 0; i < text.Length; i++)
		{
			char ch = text[i];
			if (IsHan(ch))
			{
				hasHan = true;
				continue;
			}
			if (char.IsLetterOrDigit(ch)) return false;
			if (char.IsWhiteSpace(ch) || IsAllowedPunctuation(ch)) continue;
			return false;
		}
		return hasHan;
	}

	internal static bool IsChineseActorName(string value) => IsChineseDisplayName(value);
	internal static bool IsChineseFamilyName(string value) => IsChineseDisplayName(value);
	internal static bool IsChineseSectName(string value) => IsChineseDisplayName(value);

	internal static bool HasInternalNameMarker(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0) return false;
		return text.IndexOf("noname", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("actor#", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("actor_", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("unit#", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("unit_", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("family#", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("family_", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("宗门#", StringComparison.Ordinal) >= 0
			|| text.IndexOf("sect#", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("sect_", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	internal static bool ContainsLatinLetter(string value)
	{
		string text = value ?? string.Empty;
		for (int i = 0; i < text.Length; i++)
		{
			char ch = text[i];
			if (ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z') return true;
		}
		return false;
	}

	private static bool IsPlaceholderName(string text)
	{
		return string.Equals(text, "未名修士", StringComparison.Ordinal)
			|| string.Equals(text, "未名氏", StringComparison.Ordinal)
			|| string.Equals(text, "旧宗门", StringComparison.Ordinal)
			|| string.Equals(text, "某宗", StringComparison.Ordinal)
			|| string.Equals(text, "某家", StringComparison.Ordinal);
	}

	private static bool IsHan(char ch)
	{
		return ch >= '\u3400' && ch <= '\u4DBF'
			|| ch >= '\u4E00' && ch <= '\u9FFF'
			|| ch >= '\uF900' && ch <= '\uFAFF';
	}

	private static bool IsAllowedPunctuation(char ch)
	{
		return ch == '·' || ch == '•' || ch == '・' || ch == '-'
			|| ch == '—' || ch == '－' || ch == '（' || ch == '）'
			|| ch == '(' || ch == ')' || ch == '《' || ch == '》'
			|| ch == '【' || ch == '】' || ch == '、' || ch == '，'
			|| ch == '。' || ch == '：' || ch == ':' || ch == '；'
			|| ch == '！' || ch == '？' || ch == '“' || ch == '”'
			|| ch == '「' || ch == '」' || ch == '『' || ch == '』';
	}
}
