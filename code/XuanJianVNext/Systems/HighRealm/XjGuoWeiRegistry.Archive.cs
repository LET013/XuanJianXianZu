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
{		internal static void ExportArchiveRecords(List<XjWorldArchiveGuoWeiRecord> records)
		{
			if (records == null)
			{
				return;
			}

			foreach (XjGuoWeiRegistryEntry entry in historyEntriesByActorId.Values)
			{
				XjGuoWeiRegistryEntry normalized = NormalizeActiveHistoryEntry(entry);
				if (!normalized.Found || normalized.ActorId <= 0L || string.IsNullOrWhiteSpace(normalized.GuoWei))
				{
					continue;
				}

				records.Add(new XjWorldArchiveGuoWeiRecord
				{
					ActorId = normalized.ActorId,
					ActorName = normalized.ActorName,
					FamilyName = normalized.FamilyName,
					DaoTu = normalized.DaoTu,
					JinXing = normalized.JinXing,
					GuoWei = normalized.GuoWei,
					Year = normalized.Year,
					LifecycleStatus = normalized.LifecycleStatus,
					EndedYear = normalized.EndedYear,
					EndReason = normalized.EndReason
				});
			}
		}

		internal static void ImportArchiveRecords(IEnumerable<XjWorldArchiveGuoWeiRecord> records)
		{
			activeEntriesByGuoWei.Clear();
			historyEntriesByActorId.Clear();
			revision++;
			if (records == null)
			{
				return;
			}

			foreach (XjWorldArchiveGuoWeiRecord record in records)
			{
				if (record == null || record.ActorId <= 0L || string.IsNullOrWhiteSpace(record.GuoWei))
				{
					continue;
				}

				string status = NormalizeLifecycleStatus(record.LifecycleStatus, record.EndedYear);
				XjGuoWeiRegistryEntry candidate = new XjGuoWeiRegistryEntry(
					true,
					record.ActorId,
					record.ActorName,
					record.FamilyName,
					record.DaoTu,
					XjJinXingNamePolicy.NormalizeLegacyName(record.JinXing),
					XjGuoWeiCalculator.NormalizeGuoWeiName(record.GuoWei),
					record.Year,
					status,
					record.EndedYear,
					record.EndReason);

				if (!historyEntriesByActorId.TryGetValue(candidate.ActorId, out XjGuoWeiRegistryEntry existingHistory)
					|| ShouldPreferHistory(candidate, existingHistory))
				{
					historyEntriesByActorId[candidate.ActorId] = candidate;
				}
			}

			foreach (XjGuoWeiRegistryEntry entry in historyEntriesByActorId.Values)
			{
				if (!entry.IsActive)
				{
					continue;
				}
				if (IsHiddenYinSiZhengWei(entry.DaoTu, ResolveTypeFromName(entry.GuoWei), entry.GuoWei))
				{
					// 旧版可能已开放阴司果位或斩养封锁位。保留历史账本供仙鉴查阅，
					// 但不恢复为当前占位；活体随后由可写回填迁移到合法果位。
					continue;
				}

				string key = NormalizeKey(entry.GuoWei);
				if (!activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry occupied)
					|| ShouldPreferActive(entry, occupied))
				{
					activeEntriesByGuoWei[key] = entry;
				}
			}
		}

		internal static void BackfillHistoricalRecords(
			IEnumerable<XjGuoWeiQuanBingArchiveData> authorityRecords,
			IEnumerable<XjWorldArchiveReincarnationRecord> reincarnationRecords,
			IEnumerable<XjWorldArchiveDeathRecord> deathRecords)
		{
			bool changed = false;
			if (authorityRecords != null)
			{
				foreach (XjGuoWeiQuanBingArchiveData record in authorityRecords)
				{
					if (record == null || record.ActorId <= 0L || string.IsNullOrWhiteSpace(record.GuoWei)
						|| historyEntriesByActorId.ContainsKey(record.ActorId))
					{
						continue;
					}

					bool active = string.Equals(record.LifecycleStatus, StatusActive, StringComparison.Ordinal)
						&& record.ReleasedYear <= 0;
					XjGuoWeiRegistryEntry entry = new XjGuoWeiRegistryEntry(
						true,
						record.ActorId,
						record.ActorName,
						string.Empty,
						record.DaoTu,
						string.Empty,
						XjGuoWeiCalculator.NormalizeGuoWeiName(record.GuoWei),
						record.AcquiredYear,
						active ? StatusActive : StatusDeceased,
						record.ReleasedYear,
						string.IsNullOrWhiteSpace(record.ReleaseReason) ? (active ? string.Empty : EndReasonDeath) : record.ReleaseReason);
					historyEntriesByActorId[entry.ActorId] = entry;
					if (entry.IsActive)
					{
						TryAddActiveImported(entry);
					}
					changed = true;
				}
			}

			if (reincarnationRecords != null)
			{
				foreach (XjWorldArchiveReincarnationRecord record in reincarnationRecords)
				{
					if (record == null || record.ActorId <= 0L || string.IsNullOrWhiteSpace(record.GuoWei)
						|| historyEntriesByActorId.ContainsKey(record.ActorId))
					{
						continue;
					}

					historyEntriesByActorId[record.ActorId] = new XjGuoWeiRegistryEntry(
						true,
						record.ActorId,
						record.ActorName,
						string.Empty,
						record.DaoTu,
						XjJinXingNamePolicy.NormalizeLegacyName(record.JinXing),
						XjGuoWeiCalculator.NormalizeGuoWeiName(record.GuoWei),
						0,
						StatusDeceased,
						record.DeathYear,
						EndReasonDeath);
					changed = true;
				}
			}

			if (deathRecords != null)
			{
				foreach (XjWorldArchiveDeathRecord record in deathRecords)
				{
					if (record == null || record.ActorId <= 0L || string.IsNullOrWhiteSpace(record.GuoWei)
						|| historyEntriesByActorId.ContainsKey(record.ActorId))
					{
						continue;
					}

					historyEntriesByActorId[record.ActorId] = new XjGuoWeiRegistryEntry(
						true,
						record.ActorId,
						record.Name,
						string.Empty,
						record.DaoTu,
						XjJinXingNamePolicy.NormalizeLegacyName(record.JinXing),
						XjGuoWeiCalculator.NormalizeGuoWeiName(record.GuoWei),
						0,
						StatusDeceased,
						record.Year,
						EndReasonDeath);
					changed = true;
				}
			}

			if (changed)
			{
				Touch(protectedCommit: false);
			}
		}

		private static void TryAddActiveImported(in XjGuoWeiRegistryEntry entry)
		{
			if (IsHiddenYinSiZhengWei(entry.DaoTu, ResolveTypeFromName(entry.GuoWei), entry.GuoWei))
			{
				return;
			}

			string key = NormalizeKey(entry.GuoWei);
			if (TryFindActiveTypeConflict(
					ResolveTypeFromName(entry.GuoWei),
					entry.DaoTu,
					entry.ActorId,
					out string conflictKey,
					out XjGuoWeiRegistryEntry conflict))
			{
				if (ShouldPreferActive(entry, conflict))
				{
					activeEntriesByGuoWei.Remove(conflictKey);
					activeEntriesByGuoWei[key] = entry;
				}
				return;
			}

			if (!activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry occupied)
				|| ShouldPreferActive(entry, occupied))
			{
				activeEntriesByGuoWei[key] = entry;
			}
		}
}

