using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{		internal static bool TryBindSecretRealm(long sectId, long realmId, string status)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || realmId <= 0L || !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)) return false;
			bool changed = record.SecretRealmId != realmId;
			record.SecretRealmId = realmId;
			string normalizedStatus = string.IsNullOrWhiteSpace(status) ? record.Status : status.Trim();
			if (!string.Equals(record.Status, normalizedStatus, StringComparison.Ordinal))
			{
				record.Status = normalizedStatus;
				changed = true;
			}
			if (!changed) return false;
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectSecretRealmBound",
				Math.Max(0, XjYearTracker.CurrentYear),
				record.SectId,
				record.Name,
				4,
				(record.Name ?? "某宗") + "绑定福地洞天，宗门形态转为" + FormatSectStatusForHistory(normalizedStatus));
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.SecretRealm);
			return true;
		}

		internal static bool TryUpdateSectStatus(long sectId, string status)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || string.IsNullOrWhiteSpace(status) || !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)) return false;
			string normalized = status.Trim();
			if (string.Equals(record.Status, normalized, StringComparison.Ordinal)) return false;
			string previous = record.Status ?? string.Empty;
			record.Status = normalized;
			int currentYear = Math.Max(0, XjYearTracker.CurrentYear);
			XjCenturyAnnalsStore.ObserveSectEvent(
				string.Equals(normalized, XjSectStatus.Extinct, StringComparison.Ordinal) ? "SectExtinct" : "SectStatusChanged",
				currentYear,
				record.SectId,
				record.Name,
				string.Equals(normalized, XjSectStatus.Extinct, StringComparison.Ordinal) ? 5 : 3,
				(record.Name ?? "某宗") + "状态由" + FormatSectStatusForHistory(previous) + "转为" + FormatSectStatusForHistory(normalized));
			if (string.Equals(normalized, XjSectStatus.Extinct, StringComparison.Ordinal))
			{
				XjThreeBookWriter.RecordSectExtinct(record.SectId, record.Name, currentYear, FormatSectStatusForHistory(previous));
			}
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect);
			return true;
		}

		internal static bool TryRecordSecretRealmTraining(long sectId, int currentYear, string summary)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || currentYear <= 0
				|| !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)
				|| record == null)
			{
				return false;
			}
			if (record.LastSecretRealmTrainingYear >= currentYear)
			{
				return false;
			}
			record.LastSecretRealmTrainingYear = currentYear;
			record.LastSecretRealmTrainingSummary = string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim();
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
				XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect
				| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.SecretRealm);
			return true;
		}

		private static string FormatSectStatusForHistory(string status)
		{
			if (string.Equals(status, XjSectStatus.SectRegime, StringComparison.Ordinal)) return "紫府宗门";
			if (string.Equals(status, XjSectStatus.FudiSect, StringComparison.Ordinal)) return "福地宗门";
			if (string.Equals(status, XjSectStatus.DongtianSect, StringComparison.Ordinal)) return "洞天宗门";
			if (string.Equals(status, XjSectStatus.LandlessSect, StringComparison.Ordinal)) return "失地宗门";
			if (string.Equals(status, XjSectStatus.Extinct, StringComparison.Ordinal)) return "宗门覆灭";
			return string.IsNullOrWhiteSpace(status) ? "未定" : status.Trim();
		}

		internal static bool TryUpdateDominantFamily(long sectId, long familyId, int currentYear)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || familyId <= 0L
				|| !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)) return false;
			if (record.DominantFamilyId == familyId) return false;
			record.DominantFamilyId = familyId;
			if (!record.FamilyIds.Contains(familyId))
			{
				record.FamilyIds.Add(familyId);
				record.FamilyIds.Sort();
			}
			EnsureFamilySeat(sectId, familyId, currentYear);
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectDominantFamilyChanged",
				Math.Max(0, currentYear),
				record.SectId,
				record.Name,
				3,
				(record.Name ?? "某宗") + "宗门主导家族更替为" + XjFamilyDisplayNameResolver.Resolve(familyId),
				familyStableId: familyId);
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
				XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect
				| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family
				| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
			return true;
		}

		internal static bool TryUpdateSovereign(long sectId, Actor actor, int currentYear)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || actor?.data == null || !actor.isAlive()
				|| !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)) return false;
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L || record.SovereignActorId == actorId) return false;
			long previousActorId = record.SovereignActorId;
			record.SovereignActorId = actorId;
			SetActorSovereignMirror(actor, record, Math.Max(0, currentYear));
			ClearPreviousSovereignMirror(previousActorId, record, Math.Max(0, currentYear));
			SynchronizeCapitalSovereignMirror(record, actor, Math.Max(0, currentYear));
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectSovereignChanged",
				Math.Max(0, currentYear),
				record.SectId,
				record.Name,
				4,
				(record.Name ?? "某宗") + "宗主更替，" + SafeActorName(actor) + "接掌宗门",
				actorId,
				SafeActorName(actor));
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
			return true;
		}

		internal static bool TryMarkSectTaskCompleted(long sectId, int currentYear, string summary)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || currentYear <= 0
				|| !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)) return false;
			record.LastTaskYear = Math.Max(record.LastTaskYear, currentYear);
			record.LastTaskSummary = (summary ?? string.Empty).Trim();
			if (!string.IsNullOrWhiteSpace(record.LastTaskSummary))
			{
				XjCenturyAnnalsStore.ObserveSectEvent(
					"SectTaskCompleted",
					Math.Max(0, currentYear),
					record.SectId,
					record.Name,
					2,
					(record.Name ?? "某宗") + "完成宗门任务：" + record.LastTaskSummary);
			}
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
				XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect
				| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family
				| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
			return true;
		}

		internal static IReadOnlyList<XjSectArchiveRecord> ReadAllSects()
		{
			if (BySectId.Count == 0) return Array.Empty<XjSectArchiveRecord>();
			List<XjSectArchiveRecord> result = new List<XjSectArchiveRecord>(BySectId.Count);
			foreach (XjSectArchiveRecord record in BySectId.Values)
			{
				if (IsEstablishedSect(record)) result.Add(Clone(record));
			}
			result.Sort((left, right) => left.SectId.CompareTo(right.SectId));
			return result;
		}
}

