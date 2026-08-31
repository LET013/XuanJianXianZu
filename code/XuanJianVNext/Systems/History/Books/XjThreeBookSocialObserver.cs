using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.History.Books;

/// <summary>
/// 修士低频社交纪事。复用现有宗门/城市增量索引，每名修士十年最多检查12名候选，
/// 不扫描全世界、不创建后台关系网。伴侣优先读取WorldBox原生关系；结识与深交是三书专用事实。
/// </summary>
internal static class XjThreeBookSocialObserver
{
	private const int DeepFriendshipYears = 10;
	private const int MaxCandidatesChecked = 12;
	private const int MaxAcquaintancesPerActor = 3;
	private const int MaxCloseFriendsPerActor = 2;

	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || !actor.isAlive()) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !XjCultivatorCache.IsCultivator(actorId)
			|| !XjDetectionGate.IsEntityMaintenanceSlot(
				XjEntityDetectionJob.ThreeBookSocialObservation,
				actorId,
				currentYear)) return;

		long nativePartnerId = 0L;
		if (XjNativeRelationshipInterop.TryResolvePartner(actor, out Actor partner)
			&& partner?.data != null
			&& XjCultivatorCache.IsCultivator(((BaseSystemData)partner.data).id))
		{
			nativePartnerId = ((BaseSystemData)partner.data).id;
			XjThreeBookWriter.RecordDaoCompanion(actor, partner, currentYear);
		}

		if (!TrySelectCandidate(actor, actorId, nativePartnerId, currentYear, out Actor candidate, out string basis)) return;
		long candidateId = ((BaseSystemData)candidate.data).id;
		if (actorId > candidateId) return; // 每一对只由稳定ID较小者推进。

		string acquaintanceKey = BuildSourceKey(XjThreeBookEventTypes.PersonalAcquaintance, actorId, candidateId);
		string closeKey = BuildSourceKey(XjThreeBookEventTypes.PersonalCloseFriend, actorId, candidateId);
		if (XjPersonalBiographyStore.TryGetBySourceFact(acquaintanceKey, out XjThreeBookArchiveRecord acquaintance))
		{
			if (!XjPersonalBiographyStore.ContainsSourceFact(closeKey)
				&& currentYear - acquaintance.Year >= DeepFriendshipYears
				&& XjPersonalBiographyStore.CountEvents(actorId, XjThreeBookEventTypes.PersonalCloseFriend, MaxCloseFriendsPerActor) < MaxCloseFriendsPerActor
				&& XjPersonalBiographyStore.CountEvents(candidateId, XjThreeBookEventTypes.PersonalCloseFriend, MaxCloseFriendsPerActor) < MaxCloseFriendsPerActor
				&& XjDeterministicHash.PositiveIndex(actorId + candidateId + currentYear, "threebook.close_friend", 100) < 35)
			{
				XjThreeBookWriter.RecordCloseFriend(actor, candidate, currentYear);
			}
			return;
		}

		if (XjPersonalBiographyStore.CountEvents(actorId, XjThreeBookEventTypes.PersonalAcquaintance, MaxAcquaintancesPerActor) >= MaxAcquaintancesPerActor
			|| XjPersonalBiographyStore.CountEvents(candidateId, XjThreeBookEventTypes.PersonalAcquaintance, MaxAcquaintancesPerActor) >= MaxAcquaintancesPerActor) return;
		XjThreeBookWriter.RecordAcquaintance(actor, candidate, currentYear, basis);
	}

	private static bool TrySelectCandidate(Actor actor, long actorId, long excludedActorId, int currentYear, out Actor selected, out string basis)
	{
		selected = null;
		basis = string.Empty;
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		IReadOnlyList<long> ids = sectId > 0L ? XjSectAuthorityStore.GetActorIdsForSect(sectId) : null;
		if (ids == null || ids.Count < 2) ids = ResolveCityActorIds(actor.city);
		if (ids == null || ids.Count < 2) return false;

		int start = XjDeterministicHash.PositiveIndex(actorId, "threebook.social.start|" + currentYear, ids.Count);
		long actorFamily = ResolveFamilyId(actorId);
		string actorDaoTu = ResolveDaoTu(actor);
		int bestScore = int.MinValue;
		int checkedCount = 0;
		int candidateBudget = XjRuntimeWorkBudget.ScaleCount(MaxCandidatesChecked, 4);
		for (int offset = 0; offset < ids.Count && checkedCount < candidateBudget; offset++)
		{
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L || candidateId == actorId || candidateId == excludedActorId
				|| !XjScheduler.ResolveActor(candidateId, out Actor candidate)
				|| candidate?.data == null || !candidate.isAlive() || !XjCultivatorCache.IsCultivator(candidateId)) continue;
			checkedCount++;
			long candidateFamily = ResolveFamilyId(candidateId);
			if (actorFamily > 0L && candidateFamily == actorFamily) continue;
			int score = 0;
			string candidateDaoTu = ResolveDaoTu(candidate);
			if (!string.IsNullOrWhiteSpace(actorDaoTu) && string.Equals(actorDaoTu, candidateDaoTu, StringComparison.Ordinal)) score += 30;
			if (sectId > 0L && XjSectRepository.ResolveActorSectId(candidate) == sectId) score += 20;
			if (ReferenceEquals(actor.city, candidate.city) && actor.city != null) score += 10;
			score += XjDeterministicHash.PositiveIndex(actorId + candidateId, "threebook.social.tie|" + currentYear, 10);
			if (selected == null || score > bestScore || score == bestScore && candidateId < ((BaseSystemData)selected.data).id)
			{
				selected = candidate;
				bestScore = score;
			}
		}
		if (selected == null) return false;
		string selectedDaoTu = ResolveDaoTu(selected);
		basis = !string.IsNullOrWhiteSpace(actorDaoTu) && string.Equals(actorDaoTu, selectedDaoTu, StringComparison.Ordinal)
			? "同道修行"
			: sectId > 0L ? "同在一门" : "同城修行";
		return true;
	}

	private static IReadOnlyList<long> ResolveCityActorIds(City city)
	{
		if (XjSectCultivatorCityIndex.TryGetActorIds(city, out List<long> ids)) return ids;
		return Array.Empty<long>();
	}

	private static long ResolveFamilyId(long actorId)
	{
		return actorId > 0L && XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId) ? familyId : 0L;
	}

	private static string ResolveDaoTu(Actor actor)
	{
		return actor?.data != null && XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			? (daoTu ?? string.Empty).Trim()
			: string.Empty;
	}

	private static string BuildSourceKey(string eventType, long actorId, long relatedActorId)
	{
		return "personal|social|" + eventType + "|" + actorId + "|" + relatedActorId;
	}
}
