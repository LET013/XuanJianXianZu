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
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{		internal static bool TryUpdateGovernanceChallenge(long cityId, long challengerFamilyId, int challengeStartYear,
			int consecutiveYears, float incumbentScore, float challengerScore, int currentYear, string state)
		{
			if (!GovernanceByCityId.TryGetValue(cityId, out XjCityFamilyGovernanceArchiveRecord record)) return false;
			record.ChallengerFamilyId = Math.Max(0L, challengerFamilyId);
			record.ChallengeStartYear = Math.Max(0, challengeStartYear);
			record.ChallengeConsecutiveYears = Math.Max(0, consecutiveYears);
			record.IncumbentControlScore = Math.Max(0f, incumbentScore);
			record.ChallengerControlScore = Math.Max(0f, challengerScore);
			record.LastChallengeYear = Math.Max(record.LastChallengeYear, currentYear);
			record.State = string.IsNullOrWhiteSpace(state) ? "Stable" : state.Trim();
			return true;
		}

		internal static bool TryReplaceGovernance(long cityId, long newFamilyId, int currentYear, string state)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || newFamilyId <= 0L || !GovernanceByCityId.TryGetValue(cityId, out XjCityFamilyGovernanceArchiveRecord record)) return false;
			if (record.GoverningFamilyId == newFamilyId) return false;
			record.GoverningFamilyId = newFamilyId;
			record.ConfirmedYear = Math.Max(0, currentYear);
			record.LastChallengeYear = Math.Max(0, currentYear);
			record.ChallengerFamilyId = 0L;
			record.ChallengeStartYear = 0;
			record.ChallengeConsecutiveYears = 0;
			record.IncumbentControlScore = 0f;
			record.ChallengerControlScore = 0f;
			record.State = string.IsNullOrWhiteSpace(state) ? XjCityFamilyGovernanceState.Stable : state.Trim();
			if (record.SectId > 0L) EnsureFamilySeat(record.SectId, newFamilyId, currentYear);
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.City | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Conflict);
			return true;
		}

		internal static bool TryUpdateGovernanceSect(long cityId, long sectId, int currentYear, string state)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || cityId <= 0L || !GovernanceByCityId.TryGetValue(cityId, out XjCityFamilyGovernanceArchiveRecord record)) return false;
			long normalizedSectId = sectId > 0L && BySectId.ContainsKey(sectId) ? sectId : 0L;
			string normalizedState = string.IsNullOrWhiteSpace(state)
				? (normalizedSectId > 0L ? XjCityFamilyGovernanceState.Stable : XjCityFamilyGovernanceState.Prominent)
				: state.Trim();
			if (record.SectId == normalizedSectId && string.Equals(record.State, normalizedState, StringComparison.Ordinal))
			{
				return false;
			}
	
			record.SectId = normalizedSectId;
			record.State = normalizedState;
			record.LastChallengeYear = Math.Max(record.LastChallengeYear, currentYear);
			if (normalizedSectId > 0L)
			{
				EnsureFamilySeat(normalizedSectId, record.GoverningFamilyId, currentYear);
				if (BySectId.TryGetValue(normalizedSectId, out XjSectArchiveRecord sect))
				{
					if (!sect.CityIds.Contains(cityId))
					{
						sect.CityIds.Add(cityId);
						sect.CityIds.Sort();
					}
					if (record.GoverningFamilyId > 0L && !sect.FamilyIds.Contains(record.GoverningFamilyId))
					{
						sect.FamilyIds.Add(record.GoverningFamilyId);
						sect.FamilyIds.Sort();
					}
				}
			}
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.City | XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Conflict);
			return true;
		}

	
		internal static bool TryAssignGovernance(long sectId, long cityId, long familyId, int currentYear, string state)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || cityId <= 0L || familyId <= 0L || !BySectId.ContainsKey(sectId)) return false;
			return TryAssignCityGovernance(sectId, cityId, familyId, currentYear, state);
		}

		internal static bool TryAssignCityGovernance(long sectId, long cityId, long familyId, int currentYear, string state)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || cityId <= 0L || familyId <= 0L) return false;
			long normalizedSectId = sectId > 0L && BySectId.ContainsKey(sectId) ? sectId : 0L;
			if (GovernanceByCityId.ContainsKey(cityId)) return false;
			GovernanceByCityId[cityId] = new XjCityFamilyGovernanceArchiveRecord
			{
				CityId = cityId,
				SectId = normalizedSectId,
				GoverningFamilyId = familyId,
				ConfirmedYear = Math.Max(0, currentYear),
				State = string.IsNullOrWhiteSpace(state)
					? (normalizedSectId > 0L ? XjCityFamilyGovernanceState.Stable : XjCityFamilyGovernanceState.Prominent)
					: state.Trim()
			};
			if (normalizedSectId > 0L && BySectId.TryGetValue(normalizedSectId, out XjSectArchiveRecord sect))
			{
				if (!sect.CityIds.Contains(cityId))
				{
					sect.CityIds.Add(cityId);
					sect.CityIds.Sort();
				}
				if (!sect.FamilyIds.Contains(familyId))
				{
					sect.FamilyIds.Add(familyId);
					sect.FamilyIds.Sort();
				}
				EnsureFamilySeat(normalizedSectId, familyId, currentYear);
			}
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.City | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect);
			return true;
		}

		internal static IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> ReadAllGovernance()
		{
			if (GovernanceByCityId.Count == 0) return Array.Empty<XjCityFamilyGovernanceArchiveRecord>();
			List<XjCityFamilyGovernanceArchiveRecord> result = new List<XjCityFamilyGovernanceArchiveRecord>(GovernanceByCityId.Count);
			foreach (XjCityFamilyGovernanceArchiveRecord record in GovernanceByCityId.Values)
			{
				if (record != null) result.Add(Clone(record));
			}
			result.Sort((left, right) => left.CityId.CompareTo(right.CityId));
			return result;
		}

	
		private static void EnsureCapitalGovernance(XjSectArchiveRecord record, long familyId, int year)
		{
			if (record.CapitalCityId <= 0L || familyId <= 0L) return;
			GovernanceByCityId[record.CapitalCityId] = new XjCityFamilyGovernanceArchiveRecord
			{
				CityId = record.CapitalCityId,
				SectId = record.SectId,
				GoverningFamilyId = familyId,
				ConfirmedYear = year,
				State = XjCityFamilyGovernanceState.Stable
			};
		}

}

