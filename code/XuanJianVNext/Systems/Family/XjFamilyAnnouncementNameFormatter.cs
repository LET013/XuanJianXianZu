using System;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 公告专用姓名清理。角色面板仍保留“尊号·姓名-境界”的完整显示，
/// 上修事件中只去掉重复的境界前缀与后缀。
/// </summary>
internal static class XjFamilyAnnouncementNameFormatter
{
	private static readonly string[] Realms =
	{
		"郁仪仙", "结璘仙", "神丹", "金丹", "紫府", "筑基", "炼气", "胎息"
	};

	internal static string FormatHighRealmActor(Actor actor, string fallback = "未名上修")
	{
		string value = actor?.getName();
		if (string.IsNullOrWhiteSpace(value)) return fallback;
		value = value.Trim();
		for (int i = 0; i < Realms.Length; i++)
		{
			string realm = Realms[i];
			string suffix = "-" + realm;
			string wideSuffix = "－" + realm;
			if (value.EndsWith(suffix, StringComparison.Ordinal))
			{
				value = value.Substring(0, value.Length - suffix.Length).TrimEnd();
				break;
			}
			if (value.EndsWith(wideSuffix, StringComparison.Ordinal))
			{
				value = value.Substring(0, value.Length - wideSuffix.Length).TrimEnd();
				break;
			}
		}

		for (int i = 0; i < Realms.Length; i++)
		{
			string realm = Realms[i];
			if (value.StartsWith(realm, StringComparison.Ordinal) && value.Length > realm.Length)
			{
				value = value.Substring(realm.Length).TrimStart();
				break;
			}
		}
		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}
}
