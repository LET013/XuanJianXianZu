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

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{	
		internal static XjSectFamilySeatArchiveRecord EnsureFamilySeat(long sectId, long familyId, int currentYear, string state = XjSectFamilySeatState.Formal)
		{
			if (sectId <= 0L || familyId <= 0L || !BySectId.ContainsKey(sectId)) return null;
			if (FamilySeats.TryGetValue((sectId, familyId), out XjSectFamilySeatArchiveRecord existing)) return existing;
			XjSectFamilySeatArchiveRecord created = new XjSectFamilySeatArchiveRecord
			{
				SectId = sectId,
				FamilyId = familyId,
				JoinedYear = Math.Max(0, currentYear),
				State = string.IsNullOrWhiteSpace(state) ? XjSectFamilySeatState.Formal : state.Trim()
			};
			FamilySeats.Add((sectId, familyId), created);
			if (BySectId.TryGetValue(sectId, out XjSectArchiveRecord sect) && !sect.FamilyIds.Contains(familyId))
			{
				sect.FamilyIds.Add(familyId);
				sect.FamilyIds.Sort();
			}
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectFamilySeatAdded",
				Math.Max(0, currentYear),
				sectId,
				BySectId.TryGetValue(sectId, out XjSectArchiveRecord record) ? record.Name : string.Empty,
				2,
				(BySectId.TryGetValue(sectId, out XjSectArchiveRecord named) ? named.Name : "某宗") + "新增家族席位：" + XjFamilyDisplayNameResolver.Resolve(familyId),
				familyStableId: familyId);
			return created;
		}

		internal static IReadOnlyList<XjSectFamilySeatArchiveRecord> ReadAllFamilySeats()
		{
			if (FamilySeats.Count == 0) return Array.Empty<XjSectFamilySeatArchiveRecord>();
			List<XjSectFamilySeatArchiveRecord> result = new List<XjSectFamilySeatArchiveRecord>(FamilySeats.Count);
			foreach (XjSectFamilySeatArchiveRecord record in FamilySeats.Values) if (record != null) result.Add(Clone(record));
			result.Sort((left, right) =>
			{
				int sect = left.SectId.CompareTo(right.SectId);
				return sect != 0 ? sect : left.FamilyId.CompareTo(right.FamilyId);
			});
			return result;
		}

		internal static bool TryApplyFamilyContribution(long sectId, long familyId, float contributionPoints, float craftPoints, int currentYear, string responsibility)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || familyId <= 0L) return false;
			XjSectFamilySeatArchiveRecord seat = EnsureFamilySeat(sectId, familyId, currentYear);
			if (seat == null) return false;
			int year = Math.Max(0, currentYear);
			ApplyContributionDecay(seat, year);
			seat.ContributionScore = Math.Max(0f, seat.ContributionScore + Math.Max(0f, contributionPoints));
			seat.CraftScore = Math.Max(0f, seat.CraftScore + Math.Max(0f, craftPoints));
			seat.SupplyDebt = Math.Max(0f, seat.SupplyDebt - Math.Max(0f, contributionPoints) * 0.20f - Math.Max(0f, craftPoints) * 0.35f);
			seat.LastContributionYear = Math.Max(seat.LastContributionYear, year);
			if (!string.IsNullOrWhiteSpace(responsibility)) seat.Responsibility = responsibility.Trim();
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Conflict);
			return true;
		}

		internal static bool TryUpdateFamilySeatProjection(long sectId, long familyId, int governingCityCount, float strengthScore,
			float territoryScore, float officeHistoryScore, float voiceScore, string voiceTier, int currentYear)
		{
			XjSectFamilySeatArchiveRecord seat = EnsureFamilySeat(sectId, familyId, currentYear);
			if (seat == null) return false;
			ApplyContributionDecay(seat, currentYear);
			seat.GoverningCityCount = Math.Max(0, governingCityCount);
			seat.StrengthScore = Math.Max(0f, strengthScore);
			seat.TerritoryScore = Math.Max(0f, territoryScore);
			seat.OfficeHistoryScore = Math.Max(0f, officeHistoryScore);
			seat.VoiceScore = Math.Max(0f, voiceScore);
			seat.VoiceTier = string.IsNullOrWhiteSpace(voiceTier) ? XjSectVoiceTier.Ordinary : voiceTier.Trim();
			seat.LastPublishedYear = Math.Max(seat.LastPublishedYear, currentYear);
			return true;
		}

		internal static bool TryUpdateFamilySupply(long sectId, long familyId, float supplyDebt, string responsibility, int currentYear)
		{
			XjSectFamilySeatArchiveRecord seat = EnsureFamilySeat(sectId, familyId, currentYear);
			if (seat == null) return false;
			seat.SupplyDebt = Math.Clamp(supplyDebt, 0f, 100f);
			if (!string.IsNullOrWhiteSpace(responsibility)) seat.Responsibility = responsibility.Trim();
			return true;
		}

		internal static bool TryUpdateFamilyPrivilege(long sectId, long familyId, float privilegeHeat, int incidentCount, int lastIncidentYear, int currentYear)
		{
			XjSectFamilySeatArchiveRecord seat = EnsureFamilySeat(sectId, familyId, currentYear);
			if (seat == null) return false;
			seat.PrivilegeHeat = Math.Clamp(privilegeHeat, 0f, 100f);
			seat.PrivilegeIncidentCount = Math.Max(0, incidentCount);
			seat.LastPrivilegeIncidentYear = Math.Max(0, lastIncidentYear);
			return true;
		}

		internal static bool TryApplyFamilySupplyPenalty(long sectId, long familyId, float severity, int currentYear)
		{
			XjSectFamilySeatArchiveRecord seat = EnsureFamilySeat(sectId, familyId, currentYear);
			if (seat == null) return false;
			float penalty = Math.Clamp(severity, 0.05f, 0.35f);
			seat.ContributionScore = Math.Max(0f, seat.ContributionScore * (1f - penalty));
			seat.CraftScore = Math.Max(0f, seat.CraftScore * (1f - penalty * 0.75f));
			seat.VoiceScore = Math.Max(0f, seat.VoiceScore * (1f - penalty * 0.5f));
			seat.Responsibility = "供养追责";
			seat.LastPublishedYear = Math.Max(seat.LastPublishedYear, currentYear);
			XjWorldArchiveSystem.MarkChanged();
			return true;
		}

		internal static bool TryApplyFamilyPredation(
			long sectId,
			long aggressorFamilyId,
			long victimFamilyId,
			int aggressorRealmRank,
			int currentYear,
			out float seizedContribution)
		{
			seizedContribution = 0f;
			if (sectId <= 0L || aggressorFamilyId <= 0L || victimFamilyId <= 0L || aggressorFamilyId == victimFamilyId
				|| aggressorRealmRank < 2)
			{
				return false;
			}
			XjSectFamilySeatArchiveRecord aggressor = EnsureFamilySeat(sectId, aggressorFamilyId, currentYear);
			XjSectFamilySeatArchiveRecord victim = EnsureFamilySeat(sectId, victimFamilyId, currentYear);
			if (aggressor == null || victim == null) return false;

			float pressure = aggressorRealmRank >= 3 ? 0.28f : 0.18f;
			float contributionLoss = Math.Max(1f, victim.ContributionScore * pressure);
			float craftLoss = Math.Max(0f, victim.CraftScore * pressure * 0.75f);
			seizedContribution = contributionLoss + craftLoss * 0.65f;
			victim.ContributionScore = Math.Max(0f, victim.ContributionScore - contributionLoss);
			victim.CraftScore = Math.Max(0f, victim.CraftScore - craftLoss);
			victim.VoiceScore = Math.Max(0f, victim.VoiceScore * (1f - pressure * 0.45f));
			victim.SupplyDebt = Math.Clamp(victim.SupplyDebt + (aggressorRealmRank >= 3 ? 12f : 8f), 0f, 100f);
			aggressor.ContributionScore += seizedContribution;
			aggressor.CraftScore += craftLoss * 0.35f;
			aggressor.VoiceScore += Math.Max(2f, seizedContribution * 0.12f);
			aggressor.LastContributionYear = Math.Max(aggressor.LastContributionYear, currentYear);
			victim.LastPublishedYear = Math.Max(victim.LastPublishedYear, currentYear);
			aggressor.LastPublishedYear = Math.Max(aggressor.LastPublishedYear, currentYear);
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Conflict);
			return true;
		}

	
		private static void ApplyContributionDecay(XjSectFamilySeatArchiveRecord seat, int currentYear)
		{
			if (seat == null || currentYear <= 0) return;
			int baseline = Math.Max(seat.LastPublishedYear, seat.LastContributionYear);
			if (baseline <= 0 || currentYear <= baseline) return;
			int delta = Math.Min(30, currentYear - baseline);
			float factor = MathF.Pow(0.94f, delta);
			seat.ContributionScore *= factor;
			seat.CraftScore *= factor;
		}
}

