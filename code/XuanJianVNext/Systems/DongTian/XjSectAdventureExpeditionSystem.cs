using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.DongTian;

internal static partial class XjDongTianRegistry
{
	/// <summary>
	/// 已被强宗实际占据的奇遇洞天，不再只靠散修逐个撞机缘。宗门会由一名真实紫府
	/// 带队，并在同宗门人中优先挑选与洞天道途相合者。这里不要求炼丹/炼器/阵法职业，
	/// 因为洞天探索的核心仍是位格、道途与山门组织力。
	/// </summary>
	private static bool TryBuildClaimedSectExpedition(
		in XjDongTianRecord record,
		int currentYear,
		IReadOnlyList<long> actorIds,
		int reserveSlots,
		out List<XjQiYuDongTianExplorerRecord> team)
	{
		team = null;
		// 水月照真与落霞山均是永久世界道场，不存在“占据后由宗门组织远征”的玩法。
		if (XjDongTianRules.IsPermanentWorldSite(record.QiYuDongTianId)) return false;
		if (reserveSlots <= 0 || actorIds == null || actorIds.Count == 0
			|| !XjAdventureRealmClaimSystem.TryGetClaimRecord(record.RecordId, out XjAdventureRealmClaimArchiveRecord claim)
			|| claim == null || claim.ClaimSectId <= 0L
			|| !string.Equals(claim.State, XjAdventureRealmClaimState.Resolved, StringComparison.Ordinal)
			|| !XjSectRepository.TryGetBySectId(claim.ClaimSectId, out XjSectArchiveRecord sect)
			|| sect == null || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
			|| sect.ProsperityValue < 45)
		{
			return false;
		}

		int maxSquared = XjDongTianRules.MaxExploreRadius * XjDongTianRules.MaxExploreRadius;
		XjQiYuDongTianExplorerCandidate leader = default;
		int leaderScore = int.MinValue;
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(actorIds[i], out Actor actor)
				|| actor?.data == null || !actor.isAlive()
				|| XjSectRepository.ResolveActorSectId(actor) != claim.ClaimSectId
				|| !TryBuildExplorerCandidate(record, actor, maxSquared, out XjQiYuDongTianExplorerCandidate candidate, out int distanceSquared)
				|| !string.Equals(candidate.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
			{
				continue;
			}
			int score = (IsRelatedDaoTu(record, candidate.DaoTu) ? 500_000 : 0)
				+ candidate.Weight * 25_000 - Math.Min(24_999, candidate.Distance)
				- Math.Min(10_000, distanceSquared / 32);
			if (score > leaderScore || (score == leaderScore && candidate.ActorId < leader.ActorId))
			{
				leader = candidate;
				leaderScore = score;
			}
		}
		if (leaderScore == int.MinValue || leader.ActorId <= 0L) return false;

		team = new List<XjQiYuDongTianExplorerRecord>(Math.Min(3, reserveSlots));
		HashSet<long> selected = new HashSet<long> { leader.ActorId };
		team.Add(ToExplorerRecord(leader, currentYear));
		int targetCount = Math.Min(Math.Min(3, reserveSlots), Math.Max(1, 1 + sect.ProsperityValue / 35));
		while (team.Count < targetCount)
		{
			XjQiYuDongTianExplorerCandidate best = default;
			int bestScore = int.MinValue;
			for (int i = 0; i < actorIds.Count; i++)
			{
				if (!XjScheduler.ResolveActor(actorIds[i], out Actor actor)
					|| actor?.data == null || !actor.isAlive()
					|| XjSectRepository.ResolveActorSectId(actor) != claim.ClaimSectId
					|| !TryBuildExplorerCandidate(record, actor, maxSquared, out XjQiYuDongTianExplorerCandidate candidate, out _)
					|| selected.Contains(candidate.ActorId))
				{
					continue;
				}
				int score = (IsRelatedDaoTu(record, candidate.DaoTu) ? 700_000 : 0)
					+ candidate.Weight * 30_000 - Math.Min(29_999, candidate.Distance);
				if (score > bestScore || (score == bestScore && candidate.ActorId < best.ActorId))
				{
					best = candidate;
					bestScore = score;
				}
			}
			if (bestScore == int.MinValue || best.ActorId <= 0L) break;
			selected.Add(best.ActorId);
			team.Add(ToExplorerRecord(best, currentYear));
		}

		if (team.Count <= 0) return false;
		string sectName = string.IsNullOrWhiteSpace(sect.Name) ? "某宗" : sect.Name.Trim();
		string memberText = team.Count > 1 ? "并护送" + (team.Count - 1) + "名门人" : "独自领命";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.SecretRealm,
			sectName + "遣众探" + record.DisplayName,
			sectName + "占据【" + record.DisplayName + "】后，由紫府" + leader.ActorName + "领队" + memberText
				+ "入内；门人优先依洞天道途择取相合者，以高境护持降低无谓折损。",
			3,
			actorId: leader.ActorId,
			actorName: leader.ActorName,
			sectId: sect.SectId,
			cityId: record.AnchorCityId,
			year: currentYear,
			locationX: record.AnchorTileX,
			locationY: record.AnchorTileY,
			eventType: "SectAdventureExpedition",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
			mirrorToWorldLog: false);
		return true;
	}

	private static XjQiYuDongTianExplorerRecord ToExplorerRecord(in XjQiYuDongTianExplorerCandidate candidate, int currentYear)
	{
		return new XjQiYuDongTianExplorerRecord(
			candidate.ActorId, candidate.ActorName, candidate.RealmId, candidate.RealmDisplay,
			candidate.DaoTu, candidate.Distance, currentYear, false, 0, false, false, string.Empty, string.Empty);
	}

	/// <summary>
	/// 远征“保障”只降低组织队伍在洞天中的随机折损，不免疫洞天危险。
	/// 同年、同宗、同洞天且存在紫府领队才成立，旧的散修探索记录不受影响。
	/// </summary>
	private static float ResolveSectExpeditionDeathMultiplier(
		in XjDongTianRecord record,
		in XjQiYuDongTianExplorerRecord explorerRecord,
		Actor actor)
	{
		if (actor?.data == null || explorerRecord.ReservedYear <= 0 || record.ExplorerRecords == null) return 1f;
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		if (sectId <= 0L || !XjAdventureRealmClaimSystem.TryGetClaimRecord(record.RecordId, out XjAdventureRealmClaimArchiveRecord claim)
			|| claim == null || claim.ClaimSectId != sectId
			|| !string.Equals(claim.State, XjAdventureRealmClaimState.Resolved, StringComparison.Ordinal)) return 1f;

		long actorId = ((BaseSystemData)actor.data).id;
		bool hasZiFuLeader = false;
		bool selfIsLeader = false;
		for (int i = 0; i < record.ExplorerRecords.Count; i++)
		{
			XjQiYuDongTianExplorerRecord peer = record.ExplorerRecords[i];
			if (peer.ReservedYear != explorerRecord.ReservedYear
				|| !string.Equals(peer.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
				|| !XjScheduler.ResolveActor(peer.ExplorerActorId, out Actor peerActor)
				|| peerActor?.data == null || XjSectRepository.ResolveActorSectId(peerActor) != sectId) continue;
			hasZiFuLeader = true;
			if (peer.ExplorerActorId == actorId) selfIsLeader = true;
			break;
		}
		if (!hasZiFuLeader) return 1f;
		return selfIsLeader ? 0.78f : 0.55f;
	}
	/// <summary>
	/// 团队远征只把“真实发生的探索结果”整理为宗门纪事，不额外发奖励、不虚构奇遇。
	/// 同道修士体现为循道而入，非同道修士体现为紫府压阵；具体所得仍完全来自洞天原奖励集。
	/// </summary>
	private static void RecordSectExpeditionOutcome(
		in XjDongTianRecord record,
		in XjQiYuDongTianExplorerRecord explorerRecord,
		Actor actor,
		int currentYear,
		bool relatedDaoTu,
		bool rewardApplied,
		string rewardSummary)
	{
		if (actor?.data == null || currentYear <= 0
			|| !XjAdventureRealmClaimSystem.TryGetClaimRecord(record.RecordId, out XjAdventureRealmClaimArchiveRecord claim)
			|| claim == null || claim.ClaimSectId <= 0L
			|| XjSectRepository.ResolveActorSectId(actor) != claim.ClaimSectId
			|| !XjSectRepository.TryGetBySectId(claim.ClaimSectId, out XjSectArchiveRecord sect) || sect == null) return;

		string actorName = SafeExpeditionActorName(actor, explorerRecord.ExplorerActorName);
		string routeText = relatedDaoTu
			? "与洞天法脉相合，循道而入；紫府领队在侧护持"
			: "由紫府领队压阵，与同门结伴探入";
		string resultText = rewardApplied && !string.IsNullOrWhiteSpace(rewardSummary)
			? "，最终" + rewardSummary.Trim()
			: "，此行虽全身而返，却未得可记之获";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.SecretRealm,
			actorName + "随宗门探" + record.DisplayName,
			actorName + routeText + resultText + "。",
			2,
			actorId: ((BaseSystemData)actor.data).id,
			actorName: actorName,
			sectId: sect.SectId,
			cityId: record.AnchorCityId,
			year: currentYear,
			locationX: record.AnchorTileX,
			locationY: record.AnchorTileY,
			eventType: relatedDaoTu ? "SectAdventureOutcome:RelatedDaoTu" : "SectAdventureOutcome:Protected",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Sect),
			mirrorToWorldLog: false);
	}

	private static string SafeExpeditionActorName(Actor actor, string fallback)
	{
		try
		{
			string name = actor?.getName();
			if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
		}
		catch { }
		return string.IsNullOrWhiteSpace(fallback) ? "门人" : fallback.Trim();
	}

}
