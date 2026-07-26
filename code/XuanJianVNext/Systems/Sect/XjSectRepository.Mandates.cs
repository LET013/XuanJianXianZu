using System;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 1.0宗门世家只复用既有席位字段。这里不保存支持率、派系或法旨队列，
/// 仅提交一次性争位结果和已经真实执行的宗主法旨。
/// </summary>
internal static partial class XjSectRepository
{
	internal static bool TryRecordMandateYear(long sectId, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || currentYear <= 0
			|| !BySectId.TryGetValue(sectId, out XjSectArchiveRecord sect) || sect == null)
		{
			return false;
		}

		int baseline = Math.Max(sect.FoundingYear, sect.LastMandateYear);
		if (baseline > 0 && currentYear - baseline < 20) return false;
		sect.LastMandateYear = currentYear;
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.History | XjCodexDirtyFlags.Conflict);
		return true;
	}

	internal static bool TryApplySovereignContestOutcome(
		long sectId,
		long winnerFamilyId,
		long loserFamilyId,
		int currentYear)
	{
		if (sectId <= 0L || winnerFamilyId <= 0L || loserFamilyId <= 0L || winnerFamilyId == loserFamilyId) return false;
		XjSectFamilySeatArchiveRecord winner = EnsureFamilySeat(sectId, winnerFamilyId, currentYear);
		XjSectFamilySeatArchiveRecord loser = EnsureFamilySeat(sectId, loserFamilyId, currentYear);
		if (winner == null || loser == null) return false;

		winner.ContributionScore = Math.Max(0f, winner.ContributionScore + 8f);
		winner.VoiceScore = Math.Max(0f, winner.VoiceScore + 6f);
		winner.Responsibility = "宗主争位胜出";
		winner.LastContributionYear = Math.Max(winner.LastContributionYear, currentYear);
		winner.LastPublishedYear = Math.Max(winner.LastPublishedYear, currentYear);

		loser.VoiceScore = Math.Max(0f, loser.VoiceScore * 0.92f);
		loser.SupplyDebt = Math.Clamp(loser.SupplyDebt + 6f, 0f, 100f);
		loser.Responsibility = "宗主争位失利";
		loser.LastPublishedYear = Math.Max(loser.LastPublishedYear, currentYear);

		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Conflict);
		return true;
	}

	internal static bool TryApplyMandateSupplyDemand(long sectId, long familyId, int currentYear)
	{
		if (sectId <= 0L || familyId <= 0L) return false;
		XjSectFamilySeatArchiveRecord seat = EnsureFamilySeat(sectId, familyId, currentYear);
		if (seat == null || seat.SupplyDebt < 70f) return false;
		seat.ContributionScore = Math.Max(0f, seat.ContributionScore * 0.90f);
		seat.CraftScore = Math.Max(0f, seat.CraftScore * 0.94f);
		seat.VoiceScore = Math.Max(0f, seat.VoiceScore * 0.94f);
		seat.Responsibility = "宗主法旨·催缴供奉";
		seat.LastPublishedYear = Math.Max(seat.LastPublishedYear, currentYear);
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Conflict);
		return true;
	}

	internal static bool TryApplyMandatePrivilegeRebuke(long sectId, long familyId, int currentYear)
	{
		if (sectId <= 0L || familyId <= 0L) return false;
		XjSectFamilySeatArchiveRecord seat = EnsureFamilySeat(sectId, familyId, currentYear);
		if (seat == null || seat.PrivilegeHeat < 70f) return false;
		seat.PrivilegeHeat = Math.Max(0f, seat.PrivilegeHeat - 20f);
		seat.VoiceScore = Math.Max(0f, seat.VoiceScore * 0.90f);
		seat.Responsibility = "宗主法旨·整饬强族";
		seat.LastPublishedYear = Math.Max(seat.LastPublishedYear, currentYear);
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Conflict);
		return true;
	}
}
