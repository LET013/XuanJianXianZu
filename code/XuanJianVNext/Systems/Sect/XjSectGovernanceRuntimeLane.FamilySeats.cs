using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectGovernanceRuntimeLane
{		private static void PublishSectSeats(long sectId, int currentYear)
		{
			if (!XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)) return;
			if (CachedFamilyById.Count == 0 && CachedSeatsBySect.Count == 0 && CachedCityCountsBySect.Count == 0)
			{
				BuildProjectionCache();
			}
			CachedCityCountsBySect.TryGetValue(sectId, out Dictionary<long, int> cityCounts);
			cityCounts ??= EmptyCityCounts;
			if (!CachedSeatsBySect.TryGetValue(sectId, out List<XjSectFamilySeatArchiveRecord> seats))
			{
				seats = RentProjectionSeatList();
				CachedSeatsBySect[sectId] = seats;
			}
			HashSet<long> seatFamilies = SeatFamilyScratch;
			seatFamilies.Clear();
			for (int i = 0; i < seats.Count; i++) if (seats[i] != null && seats[i].FamilyId > 0L) seatFamilies.Add(seats[i].FamilyId);
			for (int i = 0; i < sect.FamilyIds.Count; i++)
			{
				long familyId = sect.FamilyIds[i];
				if (!seatFamilies.Add(familyId)) continue;
				XjSectFamilySeatArchiveRecord created = XjSectRepository.EnsureFamilySeat(sectId, familyId, currentYear);
				if (created == null) continue;
				seats.Add(created);
			}
			float maxStrength = 1f;
			float maxTerritory = 1f;
			float maxContribution = 1f;
			float maxCraft = 1f;
			Dictionary<long, float> rawStrength = RawStrengthScratch;
			Dictionary<long, int> realmRankByFamily = RealmRankScratch;
			rawStrength.Clear();
			realmRankByFamily.Clear();
			bool hasJinDanFamily = false;
			foreach (XjSectFamilySeatArchiveRecord seat in seats)
			{
				CachedFamilyById.TryGetValue(seat.FamilyId, out XjFamilyLedgerAggregate aggregate);
				int realmRank = ResolveFamilyRealmRank(aggregate);
				realmRankByFamily[seat.FamilyId] = realmRank;
				if (realmRank >= 3) hasJinDanFamily = true;
				float strength = aggregate.AliveCount * 3f
					+ aggregate.CultivatorCount * 20f
					+ aggregate.ZiFuCount * 900f
					+ aggregate.JinDanCount * 5000f
					+ aggregate.HighestRealmOrder * 80f;
				rawStrength[seat.FamilyId] = strength;
				maxStrength = Math.Max(maxStrength, strength);
				cityCounts.TryGetValue(seat.FamilyId, out int cityCount);
				maxTerritory = Math.Max(maxTerritory, cityCount);
				maxContribution = Math.Max(maxContribution, seat.ContributionScore);
				maxCraft = Math.Max(maxCraft, seat.CraftScore);
			}
			float totalVoice = 0f;
			Dictionary<long, float> voiceByFamily = VoiceScratch;
			voiceByFamily.Clear();
			foreach (XjSectFamilySeatArchiveRecord seat in seats)
			{
				cityCounts.TryGetValue(seat.FamilyId, out int cityCount);
				float office = seat.FamilyId == sect.FounderFamilyId ? 100f : 0f;
				realmRankByFamily.TryGetValue(seat.FamilyId, out int realmRank);
				float rankBonus = realmRank >= 3 ? 120f : realmRank >= 2 ? (hasJinDanFamily ? 55f : 90f) : realmRank >= 1 ? 8f : 0f;
				float voice = rankBonus
					+ rawStrength[seat.FamilyId] / maxStrength * 35f
					+ cityCount / maxTerritory * 20f
					+ seat.ContributionScore / maxContribution * 20f
					+ seat.CraftScore / maxCraft * 15f
					+ office / 100f * 10f;
				voiceByFamily[seat.FamilyId] = voice;
				totalVoice += voice;
			}
			foreach (XjSectFamilySeatArchiveRecord seat in seats)
			{
				cityCounts.TryGetValue(seat.FamilyId, out int cityCount);
				float voice = voiceByFamily[seat.FamilyId];
				float share = totalVoice <= 0f ? 0f : voice / totalVoice;
				string tier = share >= 0.55f ? XjSectVoiceTier.Dominant
					: share >= 0.35f ? XjSectVoiceTier.First
					: share >= 0.20f ? XjSectVoiceTier.Major
					: share >= 0.10f ? XjSectVoiceTier.Elder
					: XjSectVoiceTier.Ordinary;
				realmRankByFamily.TryGetValue(seat.FamilyId, out int realmRank);
				tier = ApplyRealmRankToVoiceTier(tier, realmRank, hasJinDanFamily);
				XjSectRepository.TryUpdateFamilySeatProjection(sectId, seat.FamilyId, cityCount, rawStrength[seat.FamilyId], cityCount,
					seat.FamilyId == sect.FounderFamilyId ? 100f : 0f, voice, tier, currentYear);
				bool highRealmFamily = realmRank >= 2;
				float annualObligation = highRealmFamily ? 0f : CalculateAnnualSupplyObligation(cityCount, share, seat.FamilyId == sect.FounderFamilyId);
				float delivered = highRealmFamily ? seat.SupplyDebt : CalculateAnnualSupplyDelivered(seat, realmRank);
				float naturalBuffer = cityCount > 0 ? 0.75f : 2.25f;
				float nextDebt = highRealmFamily ? 0f : Math.Clamp(seat.SupplyDebt + annualObligation - delivered - naturalBuffer, 0f, 100f);
				if (!highRealmFamily && cityCount <= 0)
				{
					nextDebt = 0f;
				}
				string responsibility = ResolveFamilyResponsibility(realmRank, cityCount, share, seat.FamilyId == sect.FounderFamilyId);
				XjSectRepository.TryUpdateFamilySupply(sectId, seat.FamilyId, nextDebt, responsibility, currentYear);
				float nextPrivilegeHeat = CalculateFamilyPrivilegeHeat(seat, share, tier, cityCount, nextDebt, realmRank);
				int incidentCount = seat.PrivilegeIncidentCount;
				int lastIncidentYear = seat.LastPrivilegeIncidentYear;
				if (ShouldRecordFamilyPrivilegeIncident(nextPrivilegeHeat, seat.LastPrivilegeIncidentYear, currentYear)
					&& TrySelectPredationVictim(seats, seat.FamilyId, realmRank, realmRankByFamily, out long victimFamilyId)
					&& XjSectRepository.TryApplyFamilyPredation(sectId, seat.FamilyId, victimFamilyId, realmRank, currentYear, out _))
				{
					string familyName = XjFamilyDisplayNameResolver.Resolve(seat.FamilyId);
					bool seizedTreasure = XjFamilyLingWuWarehouse.TryTransferFirstTreasure(
						victimFamilyId, seat.FamilyId, 0L, familyName, currentYear, out string treasureName);
					incidentCount++;
					lastIncidentYear = currentYear;
					RecordFamilyPrivilegeIncident(
						sect, seat.FamilyId, victimFamilyId, nextPrivilegeHeat, realmRank,
						seizedTreasure ? treasureName : string.Empty, currentYear);
				}
				if (ShouldPunishSupplyDebt(nextDebt, realmRank, lastIncidentYear, currentYear))
				{
					incidentCount++;
					lastIncidentYear = currentYear;
					float severity = nextDebt >= 90f ? 0.28f : 0.18f;
					XjSectRepository.TryApplyFamilySupplyPenalty(sectId, seat.FamilyId, severity, currentYear);
					RecordFamilySupplyPenalty(sect, seat.FamilyId, nextDebt, severity, currentYear);
				}
				XjSectRepository.TryUpdateFamilyPrivilege(sectId, seat.FamilyId, nextPrivilegeHeat, incidentCount, lastIncidentYear, currentYear);
			}
			ReconcileDominantFamily(sect, seats, realmRankByFamily, voiceByFamily, currentYear);
			XjWorldArchiveSystem.MarkChanged();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Conflict);
		}

		private static void ReconcileDominantFamily(
			XjSectArchiveRecord sect,
			IReadOnlyList<XjSectFamilySeatArchiveRecord> seats,
			Dictionary<long, int> realmRankByFamily,
			IReadOnlyDictionary<long, float> voiceByFamily,
			int currentYear)
		{
			if (sect == null || seats == null || seats.Count == 0 || currentYear <= 0) return;
			long currentDominant = sect.DominantFamilyId > 0L ? sect.DominantFamilyId : sect.FounderFamilyId;
			realmRankByFamily.TryGetValue(currentDominant, out int currentRank);
			long bestFamilyId = 0L;
			float bestVoice = -1f;
			int bestRank = -1;
			for (int i = 0; i < seats.Count; i++)
			{
				XjSectFamilySeatArchiveRecord seat = seats[i];
				if (seat == null || seat.FamilyId <= 0L) continue;
				realmRankByFamily.TryGetValue(seat.FamilyId, out int rank);
				if (rank < 2) continue;
				float voice = voiceByFamily != null && voiceByFamily.TryGetValue(seat.FamilyId, out float currentVoice)
					? currentVoice
					: seat.VoiceScore;
				if (rank > bestRank || (rank == bestRank && voice > bestVoice)
					|| (rank == bestRank && Math.Abs(voice - bestVoice) < 0.001f && seat.FamilyId < bestFamilyId))
				{
					bestFamilyId = seat.FamilyId;
					bestVoice = voice;
					bestRank = rank;
				}
			}
			if (bestFamilyId <= 0L || bestFamilyId == currentDominant || currentRank >= 2) return;
			if (!XjSectRepository.TryUpdateDominantFamily(sect.SectId, bestFamilyId, currentYear)) return;
			string oldName = XjFamilyDisplayNameResolver.Resolve(currentDominant);
			string newName = XjFamilyDisplayNameResolver.Resolve(bestFamilyId);
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				sect.Name + "掌宗易姓",
				oldName + "已无紫府金丹镇压宗门，" + newName + "后来居上，接掌" + sect.Name + "宗门大权。",
				4,
				sectId: sect.SectId,
				familyId: bestFamilyId,
				relatedFamilyId: currentDominant,
				cityId: sect.CapitalCityId,
				year: currentYear,
				eventType: "SectDominantFamilyChanged",
				visibilityFlags: (int)(XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
				mirrorToWorldLog: false);
		}

		private static float CalculateAnnualSupplyObligation(int cityCount, float voiceShare, bool isFounderFamily)
		{
			float share = Math.Clamp(voiceShare, 0f, 1f);
			if (cityCount <= 0)
			{
				return 0f;
			}
			return 0.8f + cityCount * 1.1f + share * 3.5f;
		}

		private static float CalculateAnnualSupplyDelivered(XjSectFamilySeatArchiveRecord seat, int realmRank)
		{
			if (seat == null) return 0f;
			float craftAndContribution = seat.ContributionScore * 0.10f + seat.CraftScore * 0.14f;
			float baseParticipation = 1.3f + Math.Max(0, realmRank) * 1.1f;
			return Math.Max(0f, craftAndContribution + baseParticipation);
		}

		private static float CalculateFamilyPrivilegeHeat(XjSectFamilySeatArchiveRecord seat, float voiceShare, string voiceTier, int cityCount, float supplyDebt, int realmRank)
		{
			if (seat == null) return 0f;
			float pressure = 0f;
			if (voiceTier == XjSectVoiceTier.Dominant) pressure += 28f;
			else if (voiceTier == XjSectVoiceTier.First) pressure += 18f;
			else if (voiceTier == XjSectVoiceTier.Major) pressure += 10f;
			else if (voiceTier == XjSectVoiceTier.Elder) pressure += 4f;
			if (realmRank >= 3) pressure += 14f;
			else if (realmRank >= 2) pressure += 8f;
			pressure += Math.Max(0f, voiceShare - 0.20f) * 35f;
			pressure += Math.Max(0, cityCount) * 1.5f;
			if (realmRank < 2) pressure += Math.Max(0f, supplyDebt - 45f) * 0.10f;
			return Math.Clamp(seat.PrivilegeHeat * 0.72f + pressure, 0f, 100f);
		}

		private static bool ShouldRecordFamilyPrivilegeIncident(float privilegeHeat, int lastIncidentYear, int currentYear)
		{
			if (currentYear <= 0 || privilegeHeat < 70f) return false;
			return lastIncidentYear <= 0 || currentYear - lastIncidentYear >= 12;
		}

		private static bool TrySelectPredationVictim(
			IReadOnlyList<XjSectFamilySeatArchiveRecord> seats,
			long aggressorFamilyId,
			int aggressorRealmRank,
			IReadOnlyDictionary<long, int> realmRankByFamily,
			out long victimFamilyId)
		{
			victimFamilyId = 0L;
			if (seats == null || aggressorFamilyId <= 0L || aggressorRealmRank < 2) return false;
			int bestRank = int.MaxValue;
			float bestValue = float.MinValue;
			for (int i = 0; i < seats.Count; i++)
			{
				XjSectFamilySeatArchiveRecord candidate = seats[i];
				if (candidate == null || candidate.FamilyId <= 0L || candidate.FamilyId == aggressorFamilyId) continue;
				int candidateRank = 0;
				if (realmRankByFamily != null) realmRankByFamily.TryGetValue(candidate.FamilyId, out candidateRank);
				if (candidateRank >= aggressorRealmRank) continue;
				float value = candidate.ContributionScore + candidate.CraftScore * 0.75f + candidate.VoiceScore * 0.50f;
				if (candidateRank < bestRank
					|| (candidateRank == bestRank && value > bestValue)
					|| (candidateRank == bestRank && Math.Abs(value - bestValue) < 0.001f && candidate.FamilyId < victimFamilyId))
				{
					victimFamilyId = candidate.FamilyId;
					bestRank = candidateRank;
					bestValue = value;
				}
			}
			return victimFamilyId > 0L;
		}

		private static bool ShouldPunishSupplyDebt(float supplyDebt, int realmRank, int lastIncidentYear, int currentYear)
		{
			if (currentYear <= 0 || realmRank >= 2 || supplyDebt < 88f) return false;
			if (lastIncidentYear > 0 && currentYear - lastIncidentYear < 20) return false;
			return XjDeterministicHash.PositiveIndex(currentYear, "sect.supply.penalty", 5) == 0 || supplyDebt >= 98f;
		}

		private static void RecordFamilyPrivilegeIncident(
			XjSectArchiveRecord sect,
			long familyId,
			long victimFamilyId,
			float privilegeHeat,
			int realmRank,
			string seizedTreasure,
			int currentYear)
		{
			if (sect == null || familyId <= 0L) return;
			string familyName = XjFamilyDisplayNameResolver.Resolve(familyId);
			string victimName = XjFamilyDisplayNameResolver.Resolve(victimFamilyId);
			string titleSuffix = string.IsNullOrWhiteSpace(seizedTreasure) ? "压服旁支" : "强取家藏";
			string pressure = realmRank >= 3 ? "凭金丹威势" : "倚紫府声势";
			string result = string.IsNullOrWhiteSpace(seizedTreasure)
				? pressure + "迫使" + victimName + "让出宗门功绩与百艺份额，其话语权随之受损"
				: pressure + "夺去" + victimName + "家藏的" + seizedTreasure + "，并将其宗门功绩与百艺份额划入本家";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Family,
				familyName + titleSuffix,
				familyName + result + "。",
				privilegeHeat >= 80f ? 4 : 3,
				sectId: sect.SectId,
				familyId: familyId,
				cityId: sect.CapitalCityId,
				year: currentYear,
				eventType: "SectFamilyPrivilegeIncident",
				visibilityFlags: (int)(XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
				mirrorToWorldLog: false);
		}

		private static void RecordFamilySupplyPenalty(XjSectArchiveRecord sect, long familyId, float supplyDebt, float severity, int currentYear)
		{
			if (sect == null || familyId <= 0L) return;
			string familyName = XjFamilyDisplayNameResolver.Resolve(familyId);
			string action = severity >= 0.25f ? "夺其名额，削其俸给" : "责令补供，暂削话语";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				familyName + "供养追责",
				sect.Name + "清点宗门共库，见" + familyName + "欠缴已至" + Math.Round(supplyDebt) + "，遂" + action + "。",
				severity >= 0.25f ? 4 : 3,
				sectId: sect.SectId,
				familyId: familyId,
				cityId: sect.CapitalCityId,
				year: currentYear,
				eventType: "SectFamilySupplyPenalty",
				visibilityFlags: (int)(XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
				mirrorToWorldLog: false);
			XjThreeBookWriter.RecordFamilyDiscipline(
				familyId,
				familyName,
				sect.SectId,
				sect.Name,
				currentYear,
				"supply-penalty",
				"供养欠缴已至" + Math.Round(supplyDebt) + "，" + action,
				severity >= 0.25f);
		}

		private static int ResolveFamilyRealmRank(XjFamilyLedgerAggregate aggregate)
		{
			if (aggregate.JinDanCount > 0 || aggregate.HighestRealmOrder >= XjRealmHelper.GetOrder(XjRealmIds.JinDan)) return 3;
			if (aggregate.ZiFuCount > 0 || aggregate.HighestRealmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) return 2;
			if (aggregate.HighestRealmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZhuJi)) return 1;
			return 0;
		}

		private static string ApplyRealmRankToVoiceTier(string tier, int realmRank, bool hasJinDanFamily)
		{
			if (realmRank >= 3)
			{
				return IsVoiceTierAtLeast(tier, XjSectVoiceTier.First) ? tier : XjSectVoiceTier.First;
			}
			if (realmRank >= 2)
			{
				if (hasJinDanFamily && IsVoiceTierAtLeast(tier, XjSectVoiceTier.First)) return XjSectVoiceTier.Major;
				return IsVoiceTierAtLeast(tier, XjSectVoiceTier.Major) ? tier : XjSectVoiceTier.Major;
			}
			return tier;
		}

		private static bool IsVoiceTierAtLeast(string tier, string minimum)
		{
			return VoiceTierRank(tier) >= VoiceTierRank(minimum);
		}

		private static int VoiceTierRank(string tier)
		{
			if (tier == XjSectVoiceTier.Dominant) return 5;
			if (tier == XjSectVoiceTier.First) return 4;
			if (tier == XjSectVoiceTier.Major) return 3;
			if (tier == XjSectVoiceTier.Elder) return 2;
			return 1;
		}

		private static string ResolveFamilyResponsibility(int realmRank, int cityCount, float voiceShare, bool isFounderFamily)
		{
			string cityPart = cityCount > 0 ? "治理" + cityCount + "城" : string.Empty;
			string sharePart = voiceShare >= 0.45f ? "主掌宗门资源" : "按话语权取用宗门资源";
			if (realmRank >= 3)
			{
				return string.IsNullOrWhiteSpace(cityPart) ? "金丹家族" + sharePart : "金丹家族" + cityPart + "并" + sharePart;
			}
			if (realmRank >= 2)
			{
				return string.IsNullOrWhiteSpace(cityPart) ? "紫府家族参掌宗门" : "紫府家族" + cityPart + "并参掌宗门";
			}
			return cityCount > 0
				? "治理" + cityCount + "城并承担宗门供养"
				: isFounderFamily ? "开宗家族与宗门共务" : "附属家族供养";
		}
}

