using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.History.Books;

using XuanJianVNext.Architecture.Presentation;
namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{
	internal static bool TryRenameSect(long sectId, string nextName, out string message)
	{
		message = string.Empty;
		if (!XjWorldSchemaGuard.GameplayEnabled)
		{
			message = "当前世界未启用玄鉴玩法。";
			return false;
		}

		if (sectId <= 0L || !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record) || record == null)
		{
			message = "未找到宗门。";
			return false;
		}

		string normalized = NormalizeSectName(nextName);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			message = "宗门名不能为空。";
			return false;
		}

		if (normalized.Length > 12)
		{
			normalized = normalized.Substring(0, 12);
		}

		if (ContainsReservedSectTerm(normalized))
		{
			message = "“玄鉴”为特殊词组，不能用于宗门命名。";
			return false;
		}

		if (!IsSectNameAvailable(normalized, sectId))
		{
			message = "已有宗门使用相同宗名前缀，请更换宗名。";
			return false;
		}

		if (string.Equals(record.Name ?? string.Empty, normalized, StringComparison.Ordinal))
		{
			message = "宗名未变。";
			return true;
		}

		string previousName = record.Name ?? string.Empty;
		record.Name = normalized;
		int year = 0;
		try { year = Math.Max(0, World.world?.map_stats?.year ?? 0); } catch (System.Exception xjCaught48) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Sect/XjSectRepository.Rename.cs:48", xjCaught48); }
		XjThreeBookWriter.RecordSectRenamed(record.SectId, previousName, normalized, year);
		XjSectAuthorityStore.MarkProjectionDirty(record.SectId);
		XjAdventureRealmClaimSystem.TryRenameSectReferences(record.SectId, normalized);
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Sect
			| XjCodexDirtyFlags.City
			| XjCodexDirtyFlags.Family
			| XjCodexDirtyFlags.Formation
			| XjCodexDirtyFlags.Conflict
			| XjCodexDirtyFlags.History);
		XjPresentationHooks.MarkSectMapDirty();
		message = "宗名已更改。";
		return true;
	}

	private static string NormalizeSectName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string text = value.Trim();
		text = text.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", string.Empty);
		text = text.Replace("/", string.Empty).Replace("\\", string.Empty);
		while (text.Contains("  "))
		{
			text = text.Replace("  ", " ");
		}

		return text;
	}
}
