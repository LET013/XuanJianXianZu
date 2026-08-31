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
using XuanJianVNext.Systems.Sect;

using XuanJianVNext.Architecture.Presentation;
namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{	
		internal static bool TryCreateSoftSplit(
			XjSectArchiveRecord parent,
			City capital,
			Actor founder,
			long founderFamilyId,
			IReadOnlyList<long> cityIds,
			int currentYear,
			out XjSectArchiveRecord created)
		{
			created = null;
			if (!XjWorldSchemaGuard.GameplayEnabled || parent == null || capital?.data == null
				|| founder?.data == null || !founder.isAlive() || XjCultivationPathRules.IsShi(founder)
				|| founderFamilyId <= 0L
				|| cityIds == null || cityIds.Count <= 0)
			{
				return false;
			}
	
			long founderId = ((BaseSystemData)founder.data).id;
			if (founderId <= 0L || parent.SectId <= 0L) return false;
			if (!BySectId.TryGetValue(parent.SectId, out XjSectArchiveRecord liveParent) || liveParent == null) return false;
			parent = liveParent;
			int year = Math.Max(Math.Max(0, currentYear), Math.Max(Math.Max(0, XjYearTracker.CurrentYear), Math.Max(0, World.world?.map_stats?.year ?? 0)));
			int ziFuEnteredYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(founder);
			if (ziFuEnteredYear > 0) year = Math.Max(year, ziFuEnteredYear);
			string sectName = XjSectCityData.GenerateZongMenName(capital, founder);
			long nameSeed = (capital.data?.id ?? 0L) ^ founderId ^ year;
			if (string.IsNullOrWhiteSpace(sectName) || !IsSectNameAvailable(sectName))
			{
				sectName = BuildUniqueFallbackSectName(nameSeed, "宗");
			}
			List<long> transferredCityIds = new List<long>(1);
			for (int i = 0; i < cityIds.Count && transferredCityIds.Count < 1; i++)
			{
				City city = ResolveCityById(cityIds[i]);
				if (city?.data == null || parent.CityIds == null || !parent.CityIds.Contains(city.data.id)) continue;
				long cityId = city.data.id;
				if (cityId > 0L) transferredCityIds.Add(cityId);
			}
			if (transferredCityIds.Count <= 0)
			{
				return false;
			}
	
			long newKingdomId = capital.kingdom?.data?.id ?? 0L;
			long sectId = BuildUniqueSectId(capital.data.id > 0L ? capital.data.id : newKingdomId, year);
	
			XjSectArchiveRecord record = new XjSectArchiveRecord
			{
				SectId = sectId,
				KingdomId = newKingdomId,
				PredecessorKingdomName = parent.Name ?? string.Empty,
				Name = sectName.Trim(),
				Status = XjSectStatus.SectRegime,
				FoundingYear = year,
				FounderActorId = founderId,
				FounderName = SafeActorName(founder),
				FounderFamilyId = founderFamilyId,
				DominantFamilyId = founderFamilyId,
				SovereignActorId = founderId,
				CapitalCityId = capital.data.id,
				ProsperityValue = 20,
				PeakProsperityValue = 20,
				ProsperityTier = "初建"
			};
			record.FamilyIds.Add(founderFamilyId);
			for (int i = 0; i < transferredCityIds.Count; i++)
			{
				long cityId = transferredCityIds[i];
				if (cityId > 0L && !record.CityIds.Contains(cityId)) record.CityIds.Add(cityId);
			}
			record.CityIds.Sort();
			XjSectFormationArchiveRecord formation = XjSectFormationRegistry.EnsurePlanned(sectId, year);
			record.FormationId = formation?.FormationId ?? 0L;
	
			BySectId.Add(sectId, record);
			XjSectAuthorityStore.RegisterSectCities(record);
			XjSectFormationRegistry.ForceEstablishFoundingSupremeFormation(record.SectId, founderId, year);
			RefreshFormationPower(record);
			if (XjSectFormationRegistry.TryGet(record.SectId, out XjSectFormationArchiveRecord built) && built != null)
			{
				record.FormationId = built.FormationId;
			}
			EnsureFamilySeat(record.SectId, founderFamilyId, year);
	
			for (int i = 0; i < record.CityIds.Count; i++)
			{
				long cityId = record.CityIds[i];
				if (GovernanceByCityId.TryGetValue(cityId, out XjCityFamilyGovernanceArchiveRecord governance))
				{
					if (governance.GoverningFamilyId > 0L && !record.FamilyIds.Contains(governance.GoverningFamilyId))
					{
						record.FamilyIds.Add(governance.GoverningFamilyId);
						EnsureFamilySeat(record.SectId, governance.GoverningFamilyId, year);
					}
					governance.SectId = record.SectId;
					governance.State = XjCityFamilyGovernanceState.Stable;
					governance.ChallengerFamilyId = 0L;
					governance.ChallengeStartYear = 0;
					governance.ChallengeConsecutiveYears = 0;
				}
				else
				{
					GovernanceByCityId[cityId] = new XjCityFamilyGovernanceArchiveRecord
					{
						CityId = cityId,
						SectId = record.SectId,
						GoverningFamilyId = founderFamilyId,
						ConfirmedYear = year,
						State = XjCityFamilyGovernanceState.Stable
					};
				}
				parent.CityIds.Remove(cityId);
			}
			if (parent.CityIds.Count > 0 && !parent.CityIds.Contains(parent.CapitalCityId))
			{
				parent.CapitalCityId = parent.CityIds[0];
			}
	
			if (XjSectCityData.HasZongMen(capital)) XjSectCityData.Clear(capital);
			XjSectCityData.TryCreateZongMenWithFounder(capital, founder, year, null, record.SectId, record.Name, allowExistingIdentity: true);
			XjSectCityData.EnsureFamilyMembersInZongMen(capital, founderFamilyId, year, "SoftSplitFounderFamily");
			SetActorFounderMirror(founder, record, year);
	
			string foundingBody = SafeActorName(founder) + "于" + SafeDataName(capital.data, "山门") + "别开宗脉，自" + EmptyText(parent.Name, "旧宗") + "另立山门。";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				record.Name + "另立新宗",
				foundingBody,
				5,
				true,
				actorId: founderId,
				actorName: record.FounderName,
				sectId: record.SectId,
				familyId: founderFamilyId,
				cityId: record.CapitalCityId,
				year: year,
				eventType: "ZongMenFounded:SoftSplit",
				visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate));
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
				foundingBody, XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.Sect, duration: 9f, color: "#D6BE86", delayFrames: 1);
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectSoftSplitCreated",
				year,
				record.SectId,
				record.Name,
				5,
				record.Name + "自" + EmptyText(parent.Name, "旧宗") + "分出，山门立于" + SafeDataName(capital.data, "此城"),
				founderId,
				record.FounderName,
				founderFamilyId);
	
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
				XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect
				| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.City
				| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family
				| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Formation
				| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
			XjPresentationHooks.MarkSectMapDirty();
			created = record;
			return true;
		}

		internal static bool RepairFoundingChronology(XjSectArchiveRecord record, Actor founder)
		{
			if (record == null || founder?.data == null || record.FounderActorId <= 0L
				|| ((BaseSystemData)founder.data).id != record.FounderActorId)
			{
				return false;
			}
			int ziFuEnteredYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(founder);
			if (ziFuEnteredYear <= 0) return false;
			int correctedYear = Math.Max(record.FoundingYear, ziFuEnteredYear);
			if (correctedYear <= record.FoundingYear) return false;
			record.FoundingYear = correctedYear;
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
				XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.History);
			return true;
		}

		internal static bool TryCreate(City capital, Actor founder, long founderFamilyId, int currentYear, out XjSectArchiveRecord created)
		{
			created = null;
			if (!XjWorldSchemaGuard.GameplayEnabled || capital?.data == null || founder?.data == null
				|| XjCultivationPathRules.IsShi(founder) || founderFamilyId <= 0L) return false;
			long founderId = ((BaseSystemData)founder.data).id;
			if (founderId <= 0L || founder.city?.data == null || founder.city.data.id != capital.data.id) return false;
			if (TryGetByCity(capital, out _) || XjSectCityData.HasZongMen(capital)) return false;
			int year = Math.Max(Math.Max(0, currentYear), Math.Max(Math.Max(0, XjYearTracker.CurrentYear), Math.Max(0, World.world?.map_stats?.year ?? 0)));
			int ziFuEnteredYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(founder);
			if (ziFuEnteredYear > 0) year = Math.Max(year, ziFuEnteredYear);
			Kingdom kingdomShell = capital.kingdom;
			long kingdomId = kingdomShell?.data?.id ?? 0L;
			long sectId = BuildUniqueSectId(capital.data.id, year);
			string sectName = XjSectCityData.GenerateZongMenName(capital, founder);
			if (string.IsNullOrWhiteSpace(sectName) || !IsSectNameAvailable(sectName))
			{
				sectName = BuildUniqueFallbackSectName(sectId ^ founderId ^ year, "宗");
			}
	
			XjSectArchiveRecord record = new XjSectArchiveRecord
			{
				SectId = sectId,
				KingdomId = kingdomId,
				PredecessorKingdomName = (kingdomShell?.name ?? string.Empty).Trim(),
				Name = sectName.Trim(),
				Status = XjSectStatus.SectRegime,
				FoundingYear = year,
				FounderActorId = founderId,
				FounderName = founder.getName() ?? string.Empty,
				FounderFamilyId = founderFamilyId,
				SovereignActorId = founderId,
				CapitalCityId = capital.data.id,
				ProsperityValue = 20,
				PeakProsperityValue = 20,
				ProsperityTier = "初建"
			};
			record.FamilyIds.Add(founderFamilyId);
			CollectFoundingCities(capital, record.CityIds);
			if (record.CityIds.Count <= 0) return false;
			XjSectFormationArchiveRecord formation = XjSectFormationRegistry.EnsurePlanned(sectId, year);
			record.FormationId = formation?.FormationId ?? 0L;
	
			BySectId.Add(sectId, record);
			XjSectAuthorityStore.RegisterSectCities(record);
			XjSectFormationRegistry.ForceEstablishFoundingSupremeFormation(record.SectId, founderId, year);
			RefreshFormationPower(record);
			EnsureCapitalGovernance(record, founderFamilyId, year);
	
			if (!XjSectCityData.TryCreateZongMenWithFounder(capital, founder, year, null, sectId, record.Name, allowExistingIdentity: true))
			{
				RollbackCreate(founder, record);
				return false;
			}
	
			XjSectCityData.EnsureFamilyMembersInZongMen(
				capital, founderFamilyId, year, "FounderFamilyGathered", preserveExistingSect: true);
			SetActorFounderMirror(founder, record, year);
			EnsureFamilySeat(record.SectId, founderFamilyId, year);
			XjCityFamilyGovernanceSystem.InitializeForSect(null, record, founderFamilyId, year);
			string foundingBody = EmptyText(record.FounderName, "首位本土紫府") + "于" + SafeDataName(capital.data, "山门") + "立宗，"
				+ "以此城为山门立下宗脉，护宗大阵同日升起。";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				record.Name + "开宗立道",
				foundingBody,
				5,
				true,
				actorId: founderId,
				actorName: record.FounderName,
				sectId: record.SectId,
				familyId: founderFamilyId,
				cityId: record.CapitalCityId,
				year: year,
				eventType: "ZongMenFounded",
				visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate));
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
				foundingBody, XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.Sect, duration: 10f, color: "#D6BE86", delayFrames: 1);
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectCreated",
				year,
				record.SectId,
				record.Name,
				5,
				record.Name + "开宗立道，山门立于" + SafeDataName(capital.data, "此城") + "，护宗大阵同日升起",
				founderId,
				record.FounderName,
				founderFamilyId);
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Sect | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.City | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Family | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Formation | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.Conflict | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
			XjPresentationHooks.MarkSectMapDirty();
			created = record;
			return true;
		}

		private static bool EnsureFoundingFormationInvariant(XjSectArchiveRecord record, int currentYear)
		{
			if (record == null || record.SectId <= 0L) return false;
			if (record.FormationId > 0L
				&& XjSectFormationRegistry.TryGet(record.SectId, out XjSectFormationArchiveRecord existing)
				&& existing != null
				&& existing.Grade > 0)
			{
				return false;
			}
	
			long leadActorId = record.FounderActorId > 0L ? record.FounderActorId : record.SovereignActorId;
			int year = record.FoundingYear > 0 ? record.FoundingYear : Math.Max(0, currentYear);
			if (!XjSectFormationRegistry.ForceEstablishFoundingSupremeFormation(record.SectId, leadActorId, year)) return false;
			if (XjSectFormationRegistry.TryGet(record.SectId, out XjSectFormationArchiveRecord created) && created != null)
			{
				record.FormationId = created.FormationId;
			}
			RefreshFormationPower(record);
			return true;
		}

		private static long BuildUniqueSectId(long seedId, int year)
		{
			long id = XjDeterministicHash.PositiveHash(seedId, "xuanjian.sect.v2|" + year.ToString(CultureInfo.InvariantCulture));
			if (id <= 0L) id = Math.Max(1L, seedId);
			while (BySectId.ContainsKey(id)) id = id == long.MaxValue ? 1L : id + 1L;
			return id;
		}

		private static bool SequenceEqual(List<long> left, List<long> right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Count != right.Count) return false;
			for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false;
			return true;
		}

	private static City ResolveCityById(long cityId)
	{
		return XjWorldLookupIndex.TryResolveCity(cityId, out City city) ? city : null;
	}

		private static void CollectFoundingCities(City capital, List<long> target)
		{
			target.Clear();
			if (capital?.data == null || capital.data.id <= 0L) return;
			target.Add(capital.data.id);
		}

		private static void SetActorFounderMirror(Actor founder, XjSectArchiveRecord record, int year)
		{
			if (founder?.data == null || record?.SectId <= 0L) return;
			XjSectCommands.EnrollMember(record.SectId, founder, year, XjSectMemberRole.Sovereign);
			XjSectCommands.ChangeSovereign(record.SectId, founder, year, founding: true);
			XjActorAccessor.SetString(founder, XjActorDataKeys.XjZongMenFoundedCityId, record.CapitalCityId.ToString(CultureInfo.InvariantCulture));
			XjActorAccessor.SetInt(founder, XjActorDataKeys.XjZongMenFoundedYear, year);
		}

		private static void SetActorSovereignMirror(Actor actor, XjSectArchiveRecord record, int year)
		{
			if (actor?.data == null || record?.SectId <= 0L) return;
			XjSectCommands.EnrollMember(record.SectId, actor, year);
			XjSectCommands.ChangeSovereign(record.SectId, actor, year, founding: false);
		}


		private static void ClearPreviousSovereignMirror(long previousActorId, XjSectArchiveRecord record, int year)
		{
			if (previousActorId <= 0L || record == null) return;
			if (!XjSectAuthorityStore.TryGetMember(previousActorId, out XjSectMemberArchiveRecord member) || member.SectId != record.SectId) return;
			XjSectCommands.RemoveFromRoles(record.SectId, previousActorId, year, includeSovereign: false);
		}

		private static void SynchronizeCapitalSovereignMirror(XjSectArchiveRecord record, Actor actor, int year)
		{
			if (record == null || actor?.data == null || record.CapitalCityId <= 0L) return;
			City city = ResolveCityById(record.CapitalCityId);
			if (city?.data == null || XjSectCityData.GetZongMenId(city) != record.SectId) return;
			XjSectMembershipService.AssignZongZhu(city, actor, year, founding: false, "SectSovereignUpdated");
		}

		private static void RollbackCreate(Actor founder, XjSectArchiveRecord record)
		{
			long founderId = founder?.data != null ? ((BaseSystemData)founder.data).id : 0L;
			BySectId.Remove(record.SectId);
			XjSectAuthorityStore.UnregisterSect(record.SectId);
			GovernanceByCityId.Remove(record.CapitalCityId);
			FamilySeats.Remove((record.SectId, record.FounderFamilyId));
			XjSectFormationRegistry.RemoveForSect(record.SectId);
			if (founderId > 0L) XjSectProjection.ClearActor(founderId);
		}

}
