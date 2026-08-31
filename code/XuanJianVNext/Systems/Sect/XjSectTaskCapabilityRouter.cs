using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门共务的能力匹配器。只读取现有家族成员索引，每个席位最多检查32名存活成员，
/// 不创建新的队伍、任务实体或常驻AI。原拟承办家族能力不足时，将任务转派给更合适的席位。
/// </summary>
internal static class XjSectTaskCapabilityRouter
{
	private const int MaxMembersPerSeat = 32;

	internal readonly struct Route
	{
		internal readonly long LeadFamilyId;
		internal readonly string LeadFamilyName;
		internal readonly long PreferredFamilyId;
		internal readonly string PreferredFamilyName;
		internal readonly int CapabilityScore;
		internal readonly bool WasReassigned;

		internal Route(long leadFamilyId, string leadFamilyName, long preferredFamilyId,
			string preferredFamilyName, int capabilityScore, bool wasReassigned)
		{
			LeadFamilyId = leadFamilyId;
			LeadFamilyName = leadFamilyName ?? string.Empty;
			PreferredFamilyId = preferredFamilyId;
			PreferredFamilyName = preferredFamilyName ?? string.Empty;
			CapabilityScore = Math.Max(0, capabilityScore);
			WasReassigned = wasReassigned;
		}
	}

	internal static Route Resolve(
		XjSectArchiveRecord sect,
		IReadOnlyList<XjSectFamilySeatArchiveRecord> seats,
		IReadOnlyDictionary<long, int> governedCityCounts,
		int taskKind,
		int currentYear)
	{
		if (sect == null || seats == null || seats.Count == 0)
			return new Route(0L, string.Empty, 0L, string.Empty, 0, false);

		int preferredIndex = XjDeterministicHash.PositiveIndex(
			sect.SectId + currentYear,
			"sect.task.preferred.seat",
			seats.Count);
		XjSectFamilySeatArchiveRecord preferred = seats[Math.Clamp(preferredIndex, 0, seats.Count - 1)];
		long preferredId = preferred?.FamilyId ?? 0L;
		string preferredName = preferredId > 0L ? XjFamilyDisplayNameResolver.Resolve(preferredId) : string.Empty;

		XjSectFamilySeatArchiveRecord bestSeat = preferred;
		int bestScore = preferred == null ? 0 : ResolveSeatCapability(preferred, governedCityCounts, taskKind);
		for (int i = 0; i < seats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord seat = seats[i];
			if (seat == null || seat.FamilyId <= 0L) continue;
			int score = ResolveSeatCapability(seat, governedCityCounts, taskKind);
			if (bestSeat == null || score > bestScore
				|| score == bestScore && seat.VoiceScore > bestSeat.VoiceScore
				|| score == bestScore && Math.Abs(seat.VoiceScore - bestSeat.VoiceScore) < 0.01f
					&& seat.FamilyId < bestSeat.FamilyId)
			{
				bestSeat = seat;
				bestScore = score;
			}
		}

		long leadId = bestSeat?.FamilyId ?? preferredId;
		string leadName = leadId > 0L ? XjFamilyDisplayNameResolver.Resolve(leadId) : string.Empty;
		bool reassigned = preferredId > 0L && leadId > 0L && preferredId != leadId;
		return new Route(leadId, leadName, preferredId, preferredName, bestScore, reassigned);
	}

	private static int ResolveSeatCapability(
		XjSectFamilySeatArchiveRecord seat,
		IReadOnlyDictionary<long, int> governedCityCounts,
		int taskKind)
	{
		if (seat == null || seat.FamilyId <= 0L) return 0;
		int bestProfessionRank = 0;
		int bestRealmOrder = 0;
		int checkedMembers = 0;
		foreach (Actor actor in XjFamilyReadModel.Shared.GetFamilyMembers(seat.FamilyId))
		{
			if (actor?.data == null || !actor.isAlive()) continue;
			int rank = taskKind switch
			{
				0 => XjCraftProficiencySystem.GetAlchemyRank(actor),
				1 => XjCraftProficiencySystem.GetArtifactRank(actor),
				_ => Math.Max(XjCraftProficiencySystem.GetFormationRank(actor), XjCraftProficiencySystem.GetTalismanRank(actor))
			};
			if (rank > bestProfessionRank) bestProfessionRank = rank;
			int realmOrder = XjRealmHelper.GetOrder(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
			if (realmOrder > bestRealmOrder) bestRealmOrder = realmOrder;
			if (++checkedMembers >= MaxMembersPerSeat) break;
		}

		int cityCount = 0;
		governedCityCounts?.TryGetValue(seat.FamilyId, out cityCount);
		int voice = Math.Max(0, (int)Math.Round(seat.VoiceScore));
		return bestProfessionRank * 1000
			+ Math.Min(99, bestRealmOrder) * 20
			+ Math.Max(0, cityCount) * 15
			+ Math.Min(100, voice);
	}
}
