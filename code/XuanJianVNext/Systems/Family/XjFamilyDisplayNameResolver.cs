using System;
using System.Collections.Generic;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Family;

internal static class XjFamilyDisplayNameResolver
{
	private static readonly string[] CompoundSurnames =
	{
		"欧阳", "太史", "端木", "上官", "司马", "东方", "独孤", "南宫", "万俟", "闻人",
		"夏侯", "诸葛", "尉迟", "公羊", "赫连", "澹台", "皇甫", "宗政", "濮阳", "公冶",
		"太叔", "申屠", "公孙", "慕容", "仲孙", "钟离", "长孙", "宇文", "司徒", "鲜于",
		"司空", "闾丘", "子车", "亓官", "司寇", "巫马", "公西", "颛孙", "壤驷", "公良",
		"漆雕", "乐正", "宰父", "谷梁", "拓跋", "夹谷", "轩辕", "令狐", "段干", "百里",
		"呼延", "东郭", "南门", "羊舌", "微生", "梁丘", "左丘", "东门", "西门", "第五"
	};

	internal static string Resolve(long familyStableId)
	{
		if (familyStableId <= 0L)
		{
			return string.Empty;
		}

		if (XjFamilyMemberLedger.TryGetAggregate(familyStableId, out XjFamilyLedgerAggregate aggregate)
			&& !string.IsNullOrWhiteSpace(aggregate.DisplayName))
		{
			string normalized = NormalizeDisplayName(aggregate.DisplayName);
			if (!string.IsNullOrWhiteSpace(normalized))
			{
				return normalized;
			}
		}

		if (XjFamilyMemberLedger.TryGetByActorId(familyStableId, out XjFamilyMemberLedgerEntry rootEntry)
			&& rootEntry.Found
			&& !string.IsNullOrWhiteSpace(rootEntry.Name))
		{
			string displayName = FromActorName(rootEntry.Name);
			if (!string.IsNullOrWhiteSpace(displayName))
			{
				return displayName;
			}
		}

		if ((XjFamilyMemberIndex.Shared.TryGetActor(familyStableId, out Actor rootActor)
				|| XjScheduler.ResolveActor(familyStableId, out rootActor))
			&& rootActor?.data != null)
		{
			string displayName = FromActorName(rootActor.getName());
			if (!string.IsNullOrWhiteSpace(displayName))
			{
				return displayName;
			}
		}

		IReadOnlyList<XjFamilyMemberLedgerEntry> aliveMembers = XjFamilyMemberLedger.ReadFamilyAlive(familyStableId);
		for (int i = 0; i < aliveMembers.Count; i++)
		{
			string displayName = FromActorName(aliveMembers[i].Name);
			if (!string.IsNullOrWhiteSpace(displayName))
			{
				return displayName;
			}
		}

		return "未名氏";
	}

	internal static string FromActorName(string actorName)
	{
		string surname = ExtractSurname(actorName);
		return string.IsNullOrWhiteSpace(surname) ? string.Empty : surname + "氏";
	}

	private static string NormalizeDisplayName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return string.Empty;
		}
		if (IsSyntheticFamilyName(text))
		{
			return string.Empty;
		}
		if (text.EndsWith("氏", StringComparison.Ordinal))
		{
			return text;
		}
		return FromActorName(text);
	}

	private static bool IsSyntheticFamilyName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.EndsWith("氏", StringComparison.Ordinal))
		{
			text = text.Substring(0, text.Length - 1).Trim();
		}
		if (text.StartsWith("氏族#", StringComparison.Ordinal))
		{
			return true;
		}
		if (text.StartsWith("家族", StringComparison.Ordinal))
		{
			string rest = text.Substring(2).Trim().TrimStart('#', '-', '_', ' ');
			if (rest.Length == 0)
			{
				return true;
			}
			bool numeric = true;
			for (int i = 0; i < rest.Length; i++)
			{
				if (!char.IsDigit(rest[i]))
				{
					numeric = false;
					break;
				}
			}
			return numeric;
		}
		return text.StartsWith("第", StringComparison.Ordinal) && text.EndsWith("族", StringComparison.Ordinal);
	}

	private static string ExtractSurname(string value)
	{
		string text = CleanBaseName(value);
		if (text.Length == 0)
		{
			return string.Empty;
		}
		for (int i = 0; i < CompoundSurnames.Length; i++)
		{
			if (text.StartsWith(CompoundSurnames[i], StringComparison.Ordinal))
			{
				return CompoundSurnames[i];
			}
		}
		return text.Substring(0, 1);
	}

	private static string CleanBaseName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		int separator = text.IndexOf('·');
		if (separator >= 0 && separator + 1 < text.Length)
		{
			text = text.Substring(separator + 1).Trim();
		}
		int realmSeparator = text.LastIndexOf('-');
		if (realmSeparator > 0)
		{
			text = text.Substring(0, realmSeparator).Trim();
		}
		return text;
	}
}
