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
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectGovernanceRuntimeLane
{	
		private static void EvaluateCity(long cityId, int currentYear)
		{
			City city = ResolveCity(cityId);
			if (city?.data == null) return;
			Dictionary<long, float> scores = ControlScoreScratch;
			BuildControlScores(city, scores);
			if (scores.Count == 0) return;
			long currentSectId = ResolveSectIdForCity(city);
			long bestFamilyId = 0L;
			float bestScore = 0f;
			foreach (KeyValuePair<long, float> pair in scores)
			{
				if (bestFamilyId == 0L || pair.Value > bestScore || (Math.Abs(pair.Value - bestScore) < 0.001f && pair.Key < bestFamilyId))
				{
					bestFamilyId = pair.Key;
					bestScore = pair.Value;
				}
			}
			if (!XjSectRepository.TryGetGovernance(cityId, out XjCityFamilyGovernanceArchiveRecord governance))
			{
				XjSectRepository.TryAssignCityGovernance(
					currentSectId,
					cityId,
					bestFamilyId,
					currentYear,
					currentSectId > 0L ? XjCityFamilyGovernanceState.Stable : XjCityFamilyGovernanceState.Prominent);
				return;
			}
			if (governance.SectId != currentSectId)
			{
				XjSectRepository.TryUpdateGovernanceSect(
					cityId,
					currentSectId,
					currentYear,
					currentSectId > 0L ? XjCityFamilyGovernanceState.Stable : XjCityFamilyGovernanceState.Prominent);
			}
			scores.TryGetValue(governance.GoverningFamilyId, out float incumbentScore);
			if (incumbentScore <= 0f)
			{
				XjSectRepository.TryReplaceGovernance(cityId, bestFamilyId, currentYear, XjCityFamilyGovernanceState.Reassigned);
				return;
			}
			if (bestFamilyId == governance.GoverningFamilyId || bestScore < incumbentScore * ChallengeMultiplier)
			{
				XjSectRepository.TryUpdateGovernanceChallenge(
					cityId,
					0L,
					0,
					0,
					incumbentScore,
					bestScore,
					currentYear,
					currentSectId > 0L ? XjCityFamilyGovernanceState.Stable : XjCityFamilyGovernanceState.Prominent);
				return;
			}
			bool continues = governance.ChallengerFamilyId == bestFamilyId && governance.LastChallengeYear == currentYear - 1;
			int startYear = continues && governance.ChallengeStartYear > 0 ? governance.ChallengeStartYear : currentYear;
			int years = continues ? governance.ChallengeConsecutiveYears + 1 : 1;
			XjSectRepository.TryUpdateGovernanceChallenge(cityId, bestFamilyId, startYear, years, incumbentScore, bestScore, currentYear, XjCityFamilyGovernanceState.Challenged);
			if (years < RequiredConsecutiveYears) return;
			long formerFamilyId = governance.GoverningFamilyId;
			if (!XjSectRepository.TryReplaceGovernance(
				cityId,
				bestFamilyId,
				currentYear,
				currentSectId > 0L ? XjCityFamilyGovernanceState.Stable : XjCityFamilyGovernanceState.Prominent)) return;
			string cityName = string.IsNullOrWhiteSpace(city.data.name) ? "无名城" : city.data.name.Trim();
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Family,
				cityName + "改封",
				"治理家族连续十年取得压倒性在地优势，城镇治理权由" + XjFamilyDisplayNameResolver.Resolve(formerFamilyId) + "转交" + XjFamilyDisplayNameResolver.Resolve(bestFamilyId) + "。",
				3,
				sectId: governance.SectId,
				familyId: bestFamilyId,
				relatedFamilyId: formerFamilyId,
				cityId: cityId,
				year: currentYear,
				eventType: "SectCityGovernanceTransferred",
				visibilityFlags: (int)(XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
				mirrorToWorldLog: false);
		}

		private static void BuildControlScores(City city, Dictionary<long, float> result)
		{
			result.Clear();
			if (city?.data == null) return;
			IReadOnlyList<XjCityFamilyCultivatorAggregate> aggregates = XjZongMenCultivatorCityIndex.ReadFamilyAggregates(city);
			for (int i = 0; i < aggregates.Count; i++)
			{
				XjCityFamilyCultivatorAggregate score = aggregates[i];
				if (score.FamilyId <= 0L || score.MemberCount <= 0) continue;
				result[score.FamilyId] = score.TotalRealmWeight * 0.55f
					+ RealmWeight(score.HighestRealmOrder) * 0.20f
					+ score.MemberCount * 15f
					+ score.MemberCount * 10f;
			}
		}

		private static IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> BuildProjectionCache()
		{
			ClearProjectionCache();
	
			IReadOnlyList<XjFamilyLedgerAggregate> families = XjFamilyMemberLedger.ReadAggregateSnapshot();
			for (int i = 0; i < families.Count; i++)
			{
				XjFamilyLedgerAggregate family = families[i];
				if (family.FamilyStableId > 0L) CachedFamilyById[family.FamilyStableId] = family;
			}
	
			BuildHousePoliticsProjectionCache();

			IReadOnlyList<XjSectFamilySeatArchiveRecord> allSeats = XjSectRepository.ReadAllFamilySeats();
			for (int i = 0; i < allSeats.Count; i++)
			{
				XjSectFamilySeatArchiveRecord seat = allSeats[i];
				if (seat == null || seat.SectId <= 0L || seat.FamilyId <= 0L) continue;
				if (!CachedSeatsBySect.TryGetValue(seat.SectId, out List<XjSectFamilySeatArchiveRecord> seats))
				{
					seats = RentProjectionSeatList();
					CachedSeatsBySect[seat.SectId] = seats;
				}
				seats.Add(seat);
			}
	
			IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance = XjSectRepository.ReadAllGovernance();
			for (int i = 0; i < governance.Count; i++)
			{
				XjCityFamilyGovernanceArchiveRecord record = governance[i];
				if (record == null || record.SectId <= 0L || record.GoverningFamilyId <= 0L) continue;
				if (!CachedCityCountsBySect.TryGetValue(record.SectId, out Dictionary<long, int> counts))
				{
					counts = RentProjectionCityCounts();
					CachedCityCountsBySect[record.SectId] = counts;
				}
				counts.TryGetValue(record.GoverningFamilyId, out int count);
				counts[record.GoverningFamilyId] = count + 1;
			}
			return governance;
		}
}

