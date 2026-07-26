using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjGuoWeiRegistry
{		private static XjGuoWeiRegistryEntry NormalizeActiveHistoryEntry(in XjGuoWeiRegistryEntry entry)
		{
			if (!entry.Found || !entry.IsActive)
			{
				return entry;
			}
	
			string key = NormalizeKey(entry.GuoWei);
			bool stillOwnsActiveSlot = activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry active)
				&& active.ActorId == entry.ActorId;
			if (!stillOwnsActiveSlot
				&& string.Equals(ResolveTypeFromName(entry.GuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				foreach (XjGuoWeiRegistryEntry activeEntry in activeEntriesByGuoWei.Values)
				{
					if (activeEntry.ActorId == entry.ActorId
						&& string.Equals(activeEntry.DaoTu, entry.DaoTu, StringComparison.Ordinal)
						&& string.Equals(ResolveTypeFromName(activeEntry.GuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
					{
						stillOwnsActiveSlot = true;
						break;
					}
				}
			}
	
			if (stillOwnsActiveSlot)
			{
				return entry;
			}
	
			return new XjGuoWeiRegistryEntry(
				entry.Found,
				entry.ActorId,
				entry.ActorName,
				entry.FamilyName,
				entry.DaoTu,
				entry.JinXing,
				entry.GuoWei,
				entry.Year,
				StatusReleased,
				entry.EndedYear,
				string.IsNullOrWhiteSpace(entry.EndReason) ? EndReasonReassigned : entry.EndReason);
		}

		private static bool ShouldPreferActive(in XjGuoWeiRegistryEntry candidate, in XjGuoWeiRegistryEntry existing)
		{
			if (!existing.Found)
			{
				return true;
			}
	
			int candidateYear = NormalizeSortYear(candidate.Year);
			int existingYear = NormalizeSortYear(existing.Year);
			if (candidateYear != existingYear)
			{
				return candidateYear < existingYear;
			}
	
			return candidate.ActorId < existing.ActorId;
		}

		private static bool ShouldPreferHistory(in XjGuoWeiRegistryEntry candidate, in XjGuoWeiRegistryEntry existing)
		{
			if (!existing.Found)
			{
				return true;
			}
	
			if (candidate.EndedYear != existing.EndedYear)
			{
				return candidate.EndedYear > existing.EndedYear;
			}
			if (candidate.IsActive != existing.IsActive)
			{
				return !candidate.IsActive;
			}
			if (candidate.Year != existing.Year)
			{
				return candidate.Year > 0 && (existing.Year <= 0 || candidate.Year < existing.Year);
			}
			return CountCompleteness(candidate) > CountCompleteness(existing);
		}

		private static int CountCompleteness(in XjGuoWeiRegistryEntry entry)
		{
			int score = 0;
			if (!string.IsNullOrWhiteSpace(entry.ActorName)) score++;
			if (!string.IsNullOrWhiteSpace(entry.FamilyName)) score++;
			if (!string.IsNullOrWhiteSpace(entry.DaoTu)) score++;
			if (!string.IsNullOrWhiteSpace(entry.JinXing)) score++;
			if (!string.IsNullOrWhiteSpace(entry.GuoWei)) score++;
			if (entry.Year > 0) score++;
			if (entry.EndedYear > 0) score++;
			return score;
		}

		private static bool EntriesEqual(in XjGuoWeiRegistryEntry left, in XjGuoWeiRegistryEntry right)
		{
			return left.Found == right.Found
				&& left.ActorId == right.ActorId
				&& string.Equals(left.ActorName, right.ActorName, StringComparison.Ordinal)
				&& string.Equals(left.FamilyName, right.FamilyName, StringComparison.Ordinal)
				&& string.Equals(left.DaoTu, right.DaoTu, StringComparison.Ordinal)
				&& string.Equals(left.JinXing, right.JinXing, StringComparison.Ordinal)
				&& string.Equals(left.GuoWei, right.GuoWei, StringComparison.Ordinal)
				&& left.Year == right.Year
				&& string.Equals(left.LifecycleStatus, right.LifecycleStatus, StringComparison.Ordinal)
				&& left.EndedYear == right.EndedYear
				&& string.Equals(left.EndReason, right.EndReason, StringComparison.Ordinal);
		}

		private static string NormalizeLifecycleStatus(string value, int endedYear)
		{
			string normalized = Normalize(value);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				return endedYear > 0 ? StatusDeceased : StatusActive;
			}
			if (string.Equals(normalized, StatusReleased, StringComparison.Ordinal)
				|| string.Equals(normalized, StatusDeceased, StringComparison.Ordinal))
			{
				return StatusDeceased;
			}
			return StatusActive;
		}

		private static string ResolveFamilyName(Actor actor)
		{
			try
			{
				if (actor?.clan?.data == null)
				{
					return string.Empty;
				}
				return ((BaseSystemData)actor.clan.data).name ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}

		private static int NormalizeSortYear(int year)
		{
			return year <= 0 ? int.MaxValue : year;
		}

		private static string NormalizeKey(string value)
		{
			string normalized = Normalize(value);
			return string.IsNullOrWhiteSpace(normalized)
				? "empty:" + activeEntriesByGuoWei.Count.ToString(CultureInfo.InvariantCulture)
				: normalized;
		}

		private static string Normalize(string value)
		{
			return XjStringHelper.Normalize(value);
		}

		private static void Touch(bool protectedCommit)
		{
			revision++;
			XjWorldArchiveSystem.MarkChanged();
			if (protectedCommit)
			{
				XjWorldArchiveSystem.RequestProtectedCommit();
			}
		}
}

