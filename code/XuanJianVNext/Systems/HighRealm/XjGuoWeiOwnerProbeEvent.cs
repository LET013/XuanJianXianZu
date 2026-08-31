using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Broadcast;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 果位隐主揭示事件。只在求金者已经通过成功率、金性与权柄前置，
/// 最后因同道途目标位真实被占而失败时调用；不做年度扫描。
/// </summary>
internal static class XjGuoWeiOwnerProbeEvent
{
	internal const int MinimumHiddenYears = 100;

	internal static bool TryRevealOnOccupiedAttempt(
		Actor seeker,
		string daoTu,
		string guoWeiType,
		int currentYear,
		string attemptedPositionId = "")
	{
		if (seeker?.data == null || currentYear <= 0) return false;
		long seekerId = ((BaseSystemData)seeker.data).id;
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		string normalizedType = XjGuoWeiCalculator.NormalizePositionType(guoWeiType);
		if (seekerId <= 0L
			|| normalizedDaoTu.Length == 0
			|| normalizedType.Length == 0
			|| !TryResolveProbedHolder(
				seekerId, normalizedDaoTu, normalizedType, currentYear, attemptedPositionId, out XjGuoWeiRegistryEntry selected)
			|| currentYear - selected.Year < MinimumHiddenYears
			|| XjFruitPositionWorldState.HasRevealedHolder(selected.GuoWei, selected.ActorId)
			|| !XjFruitPositionWorldState.TryRecordHolderRevealed(
				selected.GuoWei, selected.ActorId, seekerId, currentYear))
		{
			return false;
		}

		int hiddenYears = Math.Max(MinimumHiddenYears, currentYear - selected.Year);
		string historyText = XjAnnouncementText.BuildGuoWeiOwnerRevealedByProbe(
			seeker.getName(), normalizedDaoTu, selected.GuoWei, hiddenYears);
		string tipText = XjAnnouncementText.BuildGuoWeiOwnerRevealedByProbeTip(
			seeker.getName(), selected.GuoWei);

		XjBroadcastSystem.BroadcastSLevelDomainEvent(
			XjWorldHistoryCategory.World,
			XjAnnouncementEventTypes.GuoWeiOwnerRevealedByProbe,
			historyText,
			tipText,
			actorId: seekerId,
			actorName: seeker.getName(),
			// 此事件只揭示“果位有主”，不公开隐伏真君身份；
			// 持位者ID仅保存在果位世界状态中用于去重。
			relatedActorId: 0L,
			relatedActorName: string.Empty,
			result: XjHistoryResult.Failure,
			year: currentYear,
			color: "#A982C4",
			duration: 10f,
			iconId: XjEventIconCatalog.JinDanFail,
			announcementCategory: XjAnnouncementCategory.AuthorityPosition);
		return true;
	}

	private static bool TryResolveProbedHolder(
		long seekerId,
		string daoTu,
		string guoWeiType,
		int currentYear,
		string attemptedPositionId,
		out XjGuoWeiRegistryEntry selected)
	{
		selected = default;
		IReadOnlyList<XjGuoWeiRegistryEntry> entries = XjGuoWeiRegistry.ReadActiveEntries();
		string exactPosition = XjGuoWeiCalculator.NormalizeGuoWeiName(attemptedPositionId);
		if (exactPosition.Length > 0)
		{
			return TryFindExactActiveHolder(entries, seekerId, daoTu, guoWeiType, exactPosition, out selected);
		}

		int slotCount = XjFruitPositionWorldState.ResolveSlotCount(daoTu, guoWeiType);
		if (slotCount <= 0) return false;
		int start = slotCount <= 1
			? 1
			: XjDeterministicHash.PositiveIndex(
				seekerId + currentYear, daoTu + "|" + guoWeiType, slotCount) + 1;
		for (int offset = 0; offset < slotCount; offset++)
		{
			int slot = ((start - 1 + offset) % slotCount) + 1;
			string candidatePosition = XjGuoWeiCalculator.BuildGuoWeiSlotName(daoTu, guoWeiType, slot);
			if (TryFindExactActiveHolder(
				entries, seekerId, daoTu, guoWeiType, candidatePosition, out selected))
			{
				// 与果位可用性解析器保持相同顺序：第一个真实阻断本次落位的
				// 在世持位者，就是此次试探所揭示的对象。
				return true;
			}
		}
		return false;
	}

	private static bool TryFindExactActiveHolder(
		IReadOnlyList<XjGuoWeiRegistryEntry> entries,
		long seekerId,
		string daoTu,
		string guoWeiType,
		string positionId,
		out XjGuoWeiRegistryEntry selected)
	{
		selected = default;
		string normalizedPosition = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		for (int i = 0; i < entries.Count; i++)
		{
			XjGuoWeiRegistryEntry candidate = entries[i];
			if (!candidate.Found
				|| !candidate.IsActive
				|| candidate.ActorId <= 0L
				|| candidate.ActorId == seekerId
				|| !string.Equals(candidate.DaoTu, daoTu, StringComparison.Ordinal)
				|| !string.Equals(candidate.GuoWei, normalizedPosition, StringComparison.Ordinal)
				|| !string.Equals(
					XjGuoWeiRegistry.ResolveTypeFromName(candidate.GuoWei),
					guoWeiType,
					StringComparison.Ordinal)
				|| candidate.Year <= 0
				|| !XjGuoWeiRegistry.TryGetStrictActiveEntryByActorId(candidate.ActorId, out XjGuoWeiRegistryEntry strict)
				|| !string.Equals(strict.GuoWei, candidate.GuoWei, StringComparison.Ordinal))
			{
				continue;
			}
			selected = strict;
			return true;
		}
		return false;
	}
}
