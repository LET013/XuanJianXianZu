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

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{
		internal static void ExportArchiveRecords(
			List<XjSectArchiveRecord> sectRecords,
			List<XjCityFamilyGovernanceArchiveRecord> governanceRecords,
			List<XjSectFamilySeatArchiveRecord> familySeatRecords,
			List<XjSectMemberArchiveRecord> memberRecords)
		{
			if (sectRecords != null)
			{
				sectRecords.Clear();
				List<long> ids = new List<long>(BySectId.Keys);
				ids.Sort();
				for (int i = 0; i < ids.Count; i++) sectRecords.Add(Clone(BySectId[ids[i]]));
			}
			if (governanceRecords != null)
			{
				governanceRecords.Clear();
				List<long> ids = new List<long>(GovernanceByCityId.Keys);
				ids.Sort();
				for (int i = 0; i < ids.Count; i++) governanceRecords.Add(Clone(GovernanceByCityId[ids[i]]));
			}
			if (familySeatRecords != null)
			{
				familySeatRecords.Clear();
				foreach (XjSectFamilySeatArchiveRecord record in ReadAllFamilySeats()) familySeatRecords.Add(record);
			}
			XjSectAuthorityStore.ExportArchiveRecords(memberRecords);
		}

		internal static void ImportArchiveRecords(
			IReadOnlyList<XjSectArchiveRecord> sectRecords,
			IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governanceRecords,
			IReadOnlyList<XjSectFamilySeatArchiveRecord> familySeatRecords,
			IReadOnlyList<XjSectMemberArchiveRecord> memberRecords)
		{
			Clear();
			if (sectRecords != null)
			{
				for (int i = 0; i < sectRecords.Count; i++)
				{
					XjSectArchiveRecord source = sectRecords[i];
					if (source == null || source.SectId <= 0L) continue;
					XjSectArchiveRecord copy = Clone(source);
					BySectId[copy.SectId] = copy;
				}
			}
			if (governanceRecords != null)
			{
				for (int i = 0; i < governanceRecords.Count; i++)
				{
					XjCityFamilyGovernanceArchiveRecord source = governanceRecords[i];
					if (source == null || source.CityId <= 0L || source.GoverningFamilyId <= 0L) continue;
					GovernanceByCityId[source.CityId] = Clone(source);
				}
			}
			if (familySeatRecords != null)
			{
				for (int i = 0; i < familySeatRecords.Count; i++)
				{
					XjSectFamilySeatArchiveRecord source = familySeatRecords[i];
					if (source == null || source.SectId <= 0L || source.FamilyId <= 0L) continue;
					FamilySeats[(source.SectId, source.FamilyId)] = Clone(source);
				}
			}
			foreach (XjCityFamilyGovernanceArchiveRecord governance in GovernanceByCityId.Values)
			{
				if (governance.SectId > 0L) EnsureFamilySeat(governance.SectId, governance.GoverningFamilyId, governance.ConfirmedYear);
			}
			XjSectAuthorityStore.ImportArchiveRecords(memberRecords);
			if (RepairSectNameInvariantsAfterImport() > 0)
			{
				XjWorldArchiveSystem.MarkChanged();
			}
		}

		internal static void Clear()
		{
			// Repository teardown owns the shared read-model cache. The authority store
			// clears only its own indexes; import still performs its own cache barrier.
			XjSectReadModel.Shared.Clear();
			BySectId.Clear();
			GovernanceByCityId.Clear();
			FamilySeats.Clear();
			ClearSovereignRuntimeState();
			ClearPeakRuntimeState();
			XjSectAuthorityStore.Clear();
		}


		private static XjSectArchiveRecord Clone(XjSectArchiveRecord source)
		{
			int prosperity = source.SchemaVersion < 5 ? 60 : Math.Clamp(source.ProsperityValue, 0, 100);
			int peakProsperity = source.SchemaVersion < 6
				? prosperity
				: Math.Clamp(Math.Max(prosperity, source.PeakProsperityValue), 0, 100);
			string prosperityTier = source.SchemaVersion < 6 || string.IsNullOrWhiteSpace(source.ProsperityTier)
				? XjSectProsperitySystem.ResolveTier(prosperity)
				: source.ProsperityTier.Trim();
			return new XjSectArchiveRecord
			{
				SchemaVersion = XjSectDomainSchema.CurrentVersion,
				SectId = source.SectId,
				KingdomId = source.KingdomId,
				PredecessorKingdomName = source.PredecessorKingdomName ?? string.Empty,
				Name = source.Name ?? string.Empty,
				Status = source.Status ?? XjSectStatus.SectRegime,
				FoundingYear = source.FoundingYear,
				FounderActorId = source.FounderActorId,
				FounderName = source.FounderName ?? string.Empty,
				FounderFamilyId = source.FounderFamilyId,
				DominantFamilyId = source.DominantFamilyId,
				SovereignActorId = source.SovereignActorId,
				SovereignGeneration = Math.Max(1, source.SovereignGeneration),
				LastRecruitYear = source.LastRecruitYear,
				CapitalCityId = source.CapitalCityId,
				FormationId = source.FormationId,
				SecretRealmId = source.SecretRealmId,
				LastTaskYear = Math.Max(0, source.LastTaskYear),
				LastMandateYear = Math.Max(0, source.LastMandateYear),
				LastTaskSummary = source.LastTaskSummary ?? string.Empty,
				LastSecretRealmTrainingYear = source.LastSecretRealmTrainingYear,
				LastSecretRealmTrainingSummary = source.LastSecretRealmTrainingSummary ?? string.Empty,
				ProsperityValue = prosperity,
				PeakProsperityValue = peakProsperity,
				LastProsperityYear = Math.Max(0, source.LastProsperityYear),
				ConsecutiveCriticalPeriods = Math.Max(0, source.ConsecutiveCriticalPeriods),
				ProsperityTier = prosperityTier,
				ProsperitySummary = source.ProsperitySummary ?? string.Empty,
				CityIds = source.CityIds == null ? new List<long>() : new List<long>(source.CityIds),
				FamilyIds = source.FamilyIds == null ? new List<long>() : new List<long>(source.FamilyIds),
				Peaks = ClonePeaks(source.Peaks, source.SectId)
			};
		}

		private static List<XjSectPeakArchiveRecord> ClonePeaks(List<XjSectPeakArchiveRecord> source, long sectId)
		{
			if (source == null || source.Count == 0) return new List<XjSectPeakArchiveRecord>();
			List<XjSectPeakArchiveRecord> result = new List<XjSectPeakArchiveRecord>(source.Count);
			for (int i = 0; i < source.Count; i++)
			{
				XjSectPeakArchiveRecord peak = source[i];
				if (peak == null || peak.PeakId < XjSectPeakIds.FirstRegular) continue;
				result.Add(new XjSectPeakArchiveRecord
				{
					SchemaVersion = peak.SchemaVersion,
					SectId = peak.SectId > 0L ? peak.SectId : sectId,
					PeakId = peak.PeakId,
					PeakName = peak.PeakName ?? string.Empty,
					PeakMasterActorId = peak.PeakMasterActorId,
					PeakMasterName = peak.PeakMasterName ?? string.Empty,
					FounderActorId = peak.FounderActorId,
					FoundedYear = peak.FoundedYear,
					LastConfirmedYear = peak.LastConfirmedYear
				});
			}
			result.Sort((left, right) => left.PeakId.CompareTo(right.PeakId));
			return result;
		}
}

