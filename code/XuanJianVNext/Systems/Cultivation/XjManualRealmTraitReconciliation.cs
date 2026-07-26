using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// Applies the deterministic state expected after an explicit external realm or
/// DaoTu trait edit. Harmony bridges live in Patches and only delegate here.
/// </summary>
internal static class XjManualRealmTraitReconciliation
{
	private static bool _isReconciling;

	internal static string CaptureBeforeGrant(Actor actor, string traitId)
	{
		return CaptureRealmBeforeAdd(actor, traitId);
	}

	internal static void HandleGranted(Actor actor, string traitId, bool result, string previousRealmId)
	{
		if (_isReconciling
			|| XjCultivationStateTransitions.IsVisibleTraitSyncActive
			|| actor?.data == null
			|| string.IsNullOrWhiteSpace(traitId)
			|| (!result && !actor.hasTrait(traitId))
			|| !IsAlive(actor))
		{
			return;
		}
		XjCultivationEligibility.RecordManualCultivationGrant(actor);

		if (!XjCultivationEligibility.CanCultivate(actor))
		{
			RemoveUnsupportedCultivationTrait(actor, traitId);
			return;
		}

		if (TryResolveDaoTuTrait(traitId, out string daoTu))
		{
			ReconcileDaoTuGrant(actor, daoTu);
			return;
		}

		if (!TryResolveRealm(traitId, out string targetRealmId, out int targetRank))
		{
			return;
		}

		try
		{
			_isReconciling = true;
			ReconcileRealmGrant(actor, targetRealmId, targetRank, previousRealmId);
		}
		finally
		{
			_isReconciling = false;
		}
	}

	private static void RemoveUnsupportedCultivationTrait(Actor actor, string traitId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(traitId) || !actor.hasTrait(traitId))
		{
			return;
		}

		bool isCultivationTrait = TryResolveRealm(traitId, out _, out _)
			|| TryResolveDaoTuTrait(traitId, out _)
			|| traitId.StartsWith("XjZz", StringComparison.Ordinal)
			|| traitId.StartsWith("ChuShen", StringComparison.Ordinal)
			|| string.Equals(traitId, "YinSi1", StringComparison.Ordinal);
		if (!isCultivationTrait)
		{
			return;
		}

		try
		{
			_isReconciling = true;
			actor.removeTrait(traitId);
		}
		finally
		{
			_isReconciling = false;
		}
	}

	internal static void HandleRemoved(Actor actor, string traitId, bool removed)
	{
		if (!removed)
		{
			return;
		}
		ReconcileDaoTuRemoval(actor, traitId);
		ReconcileRealmRemoval(actor, traitId);
	}

	private static bool TryResolveDaoTuTrait(string traitId, out string daoTu)
	{
		return XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out daoTu)
			&& !string.IsNullOrWhiteSpace(daoTu);
	}

	private static void ReconcileDaoTuGrant(Actor actor, string daoTu)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu) || !IsAlive(actor))
		{
			return;
		}

		try
		{
			_isReconciling = true;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string previousDaoTu);
			bool preserveRunWeiCultivation = IsJinDanRunWeiAdjacentSwitch(actor, previousDaoTu, daoTu);
			if (preserveRunWeiCultivation)
			{
				// 金丹闰位改换相邻道途只改变道途身份；仙基、神通、功法与求金法
				// 均保持原状，不能经过通用道途写入触发功法重定。
				XjCultivationStateTransitions.TrySetDaoTuMetadataOnly(actor, daoTu, true);
			}
			else if (XjCultivationStateTransitions.TrySetDaoTuMetadataOnly(actor, daoTu, false))
			{
				if (!ReplaceManualDaoTuCultivation(actor, daoTu))
				{
					XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, previousDaoTu, true);
				}
				else
				{
					XjVisibleTraitSync.SyncCultivationTraits(actor);
				}
			}
			XjManualCultivationWake.EnsureAwake(actor, ensureMinimumAptitude: false);
		}
		finally
		{
			_isReconciling = false;
		}
	}

	private static bool IsJinDanRunWeiAdjacentSwitch(Actor actor, string previousDaoTu, string nextDaoTu)
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(previousDaoTu)
			|| string.IsNullOrWhiteSpace(nextDaoTu)
			|| !XjXianJiCatalog.IsAdjacentDaoTu(previousDaoTu, nextDaoTu)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return XjGuoWeiRegistry.TryGetHistoricalEntry(actorId, out XjGuoWeiRegistryEntry entry)
			&& entry.Found
			&& (entry.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal);
	}

	private static bool ReplaceManualDaoTuCultivation(Actor actor, string daoTu)
	{
		XjXianJiState existing = XjXianJiAccessor.BuildState(actor);
		if (!existing.Found || existing.Count <= 0)
		{
			return XjGongFaProgression.ReconcileDaoTu(actor, daoTu, "手动道途特质");
		}

		long actorId = ((BaseSystemData)actor.data).id;
		int currentYear = GetWorldYear(actor);
		List<string> replacements = new List<string>(existing.Count);
		for (int ordinal = 1; ordinal <= existing.Count; ordinal++)
		{
			string[] selected = replacements.ToArray();
			string pickedId;
			bool picked = ordinal <= 3
				? XjXianJiCatalog.TryPickUpperForProgression(
					daoTu, ordinal, actorId + currentYear * 131L, selected, out pickedId)
				: XjXianJiCatalog.TryPickForProgression(
					daoTu,
					ordinal,
					actorId + currentYear * 131L,
					selected,
					XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(daoTu),
					!XjLongShuSystem.IsLongShu(actor),
					out pickedId);
			if (!picked || string.IsNullOrWhiteSpace(pickedId))
			{
				return false;
			}
			replacements.Add(pickedId.Trim());
		}

		string[] ids = replacements.ToArray();
		if (!XjActorGongFaCollection.ReplaceAllForManualDaoTu(actor, daoTu, ids, "手动道途替换"))
		{
			return false;
		}
		return XjXianJiAccessor.RestoreSnapshot(actor, string.Join("|", ids), currentYear);
	}

	private static void ReconcileDaoTuRemoval(Actor actor, string traitId)
	{
		if (_isReconciling
			|| XjCultivationStateTransitions.IsVisibleTraitSyncActive
			|| actor?.data == null
			|| string.IsNullOrWhiteSpace(traitId)
			|| !TryResolveDaoTuTrait(traitId, out _)
			|| !IsAlive(actor))
		{
			return;
		}

		try
		{
			_isReconciling = true;
			string nextDaoTu = ResolveExistingDaoTuFromVisibleTraits(actor);
			if (string.IsNullOrWhiteSpace(nextDaoTu))
			{
				XjCultivationStateTransitions.ClearDaoTu(actor, true);
			}
			else
			{
				XjCultivationStateTransitions.TrySetDaoTu(actor, nextDaoTu, true);
			}
		}
		finally
		{
			_isReconciling = false;
		}
	}

	private static string ResolveExistingDaoTuFromVisibleTraits(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		string[] traitIds = XjDaoTuVisibleTraitCatalog.AllTraitIds;
		for (int i = 0; i < traitIds.Length; i++)
		{
			string traitId = traitIds[i];
			if (!string.IsNullOrWhiteSpace(traitId)
				&& actor.hasTrait(traitId)
				&& XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out string daoTu))
			{
				return daoTu;
			}
		}

		return string.Empty;
	}

	private static void ReconcileRealmGrant(Actor actor, string targetRealmId, int targetRank, string previousRealmId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(targetRealmId))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string currentRealmId);
		string sourceRealmId = string.IsNullOrWhiteSpace(previousRealmId) ? currentRealmId : previousRealmId;
		int currentRank = GetRealmRank(sourceRealmId);
		if (targetRank < currentRank)
		{
			// 1.0不支持任何境界回退。手动添加低境界特质只会被可见特质同步撤销。
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}
		if (!CanApplyManualRealmByAptitude(actor, targetRealmId))
		{
			// 仅无效角色/无效境界会到达这里。显式手动赋予境界时，
			// 资质由后续 EnsureManualAptitude 提升到该境界最低要求。
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}

		// 显式境界编辑仍须遵守统一初始化顺序：资质 → 道途 → 境界。
		// 旧顺序先调用 TrySetRealm，会被道途门禁拒绝，导致后续补录永远不执行。
		EnsureManualAptitude(actor, targetRealmId);
		if (string.IsNullOrWhiteSpace(EnsureManualDaoTu(actor)))
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}

		if (currentRank == targetRank)
		{
			if (!XjCultivationStateTransitions.TrySetRealm(actor, targetRealmId, false))
			{
				XjVisibleTraitSync.SyncCultivationTraits(actor);
				return;
			}
			XjCultivationSeed.EnsureSeedState(actor);
			EnsureManualHighRealmFamilyBeforeInheritance(actor, targetRank);
			EnsureMinimumZhenYuan(actor, targetRealmId);
			EnsureRealmGongFaWithoutDomainEvents(actor, targetRealmId);
			EnsureManualZiFuFoundation(actor, targetRealmId);
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			XjCultivationStateTransitions.RestoreHealthAfterRealmPromotion(actor);
			EnsureManualHighRealmCompanionTraits(actor, targetRank);
			EnsureVisibleRealmTrait(actor, targetRealmId);
			if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
			{
				RunManualJinDanSuccessEventChain(actor);
			}
			else
			{
				EnsureManualJinDanZongMen(actor, targetRealmId);
			}
			EnsureManualLongShuTrigger(actor, targetRealmId);
			XjAutoCollectSystem.TryCollectRealm(actor, targetRealmId, "ManualRealmGrant");
			RefreshManualRealmFamilyInheritance(actor);
			XjManualCultivationWake.EnsureAwake(actor);
			return;
		}

		if (!XjCultivationStateTransitions.TrySetRealm(actor, targetRealmId, false))
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}
		XjCultivationSeed.EnsureSeedState(actor);
		bool isPromotion = targetRank > currentRank;
		bool isFirstManualJinDan = isPromotion
			&& currentRank < 5
			&& targetRank == 5
			&& string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal);
		if (isPromotion)
		{
			ApplyManualRealmMingShuRewards(actor, currentRank, targetRank);
		}
		EnsureManualHighRealmFamilyBeforeInheritance(actor, targetRank);
		if (isPromotion)
		{
			XjBloodlineBirthRules.TryRefreshForRealmPromotion(actor, GetCurrentYear(actor));
		}
		EnsureMinimumZhenYuan(actor, targetRealmId);
		EnsureRealmGongFaWithoutDomainEvents(actor, targetRealmId);
		EnsureManualZiFuFoundation(actor, targetRealmId);

		if (isPromotion)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			XjRealmTitleApplyService.ApplyOnPromotion(actor, targetRealmId, daoTu);
		}
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		XjCultivationStateTransitions.RestoreHealthAfterRealmPromotion(actor);
		EnsureManualHighRealmCompanionTraits(actor, targetRank);
		EnsureVisibleRealmTrait(actor, targetRealmId);
		if (isFirstManualJinDan)
		{
			RunManualJinDanSuccessEventChain(actor);
		}
		else
		{
			EnsureManualJinDanZongMen(actor, targetRealmId);
		}
		EnsureManualLongShuTrigger(actor, targetRealmId);
		if (isPromotion
			&& string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& XjRuntimeSettings.BroadcastHighRealmEnabled
			&& XjAnnouncementText.TryBuildZiFuPromotion(actor, out string promotionText))
		{
			XjBroadcastSystem.BroadcastBLevelActorEvent(
				actor,
				promotionText,
				iconId: XjEventIconCatalog.ZiFuUpgrade);
		}
		XjAutoCollectSystem.TryCollectRealm(actor, targetRealmId, "ManualRealmGrant");
		RefreshManualRealmFamilyInheritance(actor);
		XjManualCultivationWake.EnsureAwake(actor);
	}

	private static void EnsureManualZiFuFoundation(Actor actor, string targetRealmId)
	{
		if (actor?.data == null
			|| !string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return;
		}

		XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out string daoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out daoTu);
		}
		XjZiFuProgression.EnsureZhuJiFoundationXianJi(actor, daoTu, GetCurrentYear(actor));
	}

	private static void EnsureManualHighRealmFamilyBeforeInheritance(Actor actor, int targetRank)
	{
		if (actor?.data != null && targetRank >= 4)
		{
			XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
		}
	}

	private static void RefreshManualRealmFamilyInheritance(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjCultivatorCache.CheckAndUpdate(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
		}
		else
		{
			XjFamilyMemberIndex.Shared.AddActorToFamily(actor);
		}
	}

	private static void RunManualJinDanSuccessEventChain(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		if (!jinDan.Found)
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		int currentYear = jinDan.SuccessYear > 0 ? jinDan.SuccessYear : GetWorldYear(actor);
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		if (!string.Equals(snapshot.DaoTu, daoTu, StringComparison.Ordinal))
		{
			snapshot = new XjActorCultivationSnapshot(
				snapshot.RealmId,
				daoTu,
				snapshot.ZhenYuan,
				snapshot.MingShu,
				snapshot.HuiGuang,
				snapshot.XjZz,
				snapshot.XjZzOverlayMask,
				snapshot.XianJiCount,
				snapshot.HasQiuJinFa);
		}

		XjJinDanBreakthroughSystem.RunJinDanSuccessEventChain(
			actor,
			daoTu,
			jinDan.JinXing,
			jinDan.GuoWei,
			currentYear,
			snapshot);
	}

	private static void EnsureManualJinDanZongMen(Actor actor, string realmId)
	{
		if (actor?.data == null
			|| XjLongShuSystem.IsLongShu(actor)
			|| !string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return;
		}

		int currentYear = GetWorldYear(actor);
		XjZongMenCityData.HandleJinDanPromotion(actor, currentYear);
	}

	private static void EnsureManualLongShuTrigger(Actor actor, string realmId)
	{
		if (actor?.data == null
			|| (!string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
				&& !string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)))
		{
			return;
		}

		XjLongShuSystem.NotifyZiFuAppeared(actor);
	}

	private static void EnsureManualHighRealmCompanionTraits(Actor actor, int targetRank)
	{
		if (actor?.data == null || targetRank < 4 || actor.hasTrait("tough"))
		{
			return;
		}

		if (AssetManager.traits?.get("tough") != null && actor.addTrait("tough", false))
		{
			actor.setStatsDirty();
		}
	}

	private static string CaptureRealmBeforeAdd(Actor actor, string traitId)
	{
		if (_isReconciling
			|| XjCultivationStateTransitions.IsVisibleTraitSyncActive
			|| actor?.data == null
			|| !TryResolveRealm(traitId, out _, out _))
		{
			return string.Empty;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		return realmId ?? string.Empty;
	}

	private static void ReconcileRealmRemoval(Actor actor, string traitId)
	{
		if (_isReconciling
			|| XjCultivationStateTransitions.IsVisibleTraitSyncActive
			|| actor?.data == null
			|| !TryResolveRealm(traitId, out string removedRealmId, out _))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string currentRealmId);
		if (!string.Equals(currentRealmId, removedRealmId, StringComparison.Ordinal))
		{
			return;
		}

		try
		{
			_isReconciling = true;
			// 1.0不支持境界回退；删除当前境界可见特质时立即按真实境界补回。
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		finally
		{
			_isReconciling = false;
		}
	}

	private static void ApplyManualRealmMingShuRewards(Actor actor, int currentRank, int targetRank)
	{
		if (actor?.data == null || targetRank <= currentRank)
		{
			return;
		}

		int startRank = Math.Max(0, currentRank) + 1;
		for (int rank = startRank; rank <= targetRank; rank++)
		{
			string realmId = ResolveRealmIdByRank(rank);
			if (!string.IsNullOrWhiteSpace(realmId))
			{
				XjBreakthroughRules.ApplySuccessMingShuReward(actor, realmId);
			}
		}
	}

	private static string ResolveRealmIdByRank(int rank)
	{
		if (rank == 1) return XjRealmIds.TaiXi;
		if (rank == 2) return XjRealmIds.LianQi;
		if (rank == 3) return XjRealmIds.ZhuJi;
		if (rank == 4) return XjRealmIds.ZiFu;
		if (rank == 5) return XjRealmIds.JinDan;
		return string.Empty;
	}

	private static bool CanApplyManualRealmByAptitude(Actor actor, string realmId)
	{
		// “手动赋予境界特质”是玩家/调试器的显式编辑，不应再被自然突破的资质门槛反向撤销。
		// 合法性由角色是否具备可写数据决定；随后 EnsureManualAptitude 会补齐目标境界最低资质。
		return actor?.data != null && !string.IsNullOrWhiteSpace(realmId);
	}

	private static void EnsureManualAptitude(Actor actor, string realmId)
	{
		if (actor?.data == null)
		{
			return;
		}

		int minimum = ResolveMinimumManualAptitude(realmId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz);
		int normalized = xjZz >= 1 && xjZz <= 6 ? xjZz : 0;
		int target = Math.Max(normalized, minimum);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, target);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(actor, target);
	}

	private static int ResolveMinimumManualAptitude(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return 5;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 4;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 3;
		return 1;
	}

	private static string EnsureManualDaoTu(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(daoTu)
			&& XjDaoTuVisibleTraitCatalog.TryResolveTraitId(daoTu, out _)
			&& (!string.Equals(daoTu, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
				|| XjQingXuanKongZhengSystem.CanEnterQingXuan(actor)))
		{
			return daoTu;
		}

		string[] traitIds = XjDaoTuVisibleTraitCatalog.AllTraitIds;
		for (int i = 0; i < traitIds.Length; i++)
		{
			string traitId = traitIds[i];
			if (string.IsNullOrWhiteSpace(traitId) || !actor.hasTrait(traitId))
			{
				continue;
			}

			if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out string resolvedDaoTu)
				&& !string.IsNullOrWhiteSpace(resolvedDaoTu)
				&& (!string.Equals(resolvedDaoTu, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
					|| XjQingXuanKongZhengSystem.CanEnterQingXuan(actor))
				&& XjCultivationStateTransitions.TrySetDaoTu(actor, resolvedDaoTu, false))
			{
				return resolvedDaoTu;
			}
		}

		if (TryPickManualDaoTu(actor, out string pickedDaoTu)
			&& XjCultivationStateTransitions.TrySetDaoTu(actor, pickedDaoTu, true))
		{
			return pickedDaoTu;
		}

		return string.Empty;
	}

	private static bool TryPickManualDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null)
		{
			return false;
		}

		IReadOnlyList<XjDaoTuVisibleTraitEntry> entries = XjDaoTuVisibleTraitCatalog.Entries;
		if (entries == null || entries.Count == 0)
		{
			return false;
		}

		List<string> candidates = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < entries.Count; i++)
		{
			string displayName = entries[i].DisplayName;
			if (string.IsNullOrWhiteSpace(displayName)
				|| string.Equals(displayName.Trim(), XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
				|| !seen.Add(displayName.Trim()))
			{
				continue;
			}

			candidates.Add(displayName.Trim());
		}

		if (candidates.Count == 0)
		{
			return false;
		}

		long actorId = GetActorId(actor);
		int index = XjDeterministicHash.PositiveIndex(actorId, "manual_realm_daotu", candidates.Count);
		daoTu = candidates[index];
		return !string.IsNullOrWhiteSpace(daoTu);
	}

	private static void EnsureMinimumZhenYuan(Actor actor, string realmId)
	{
		if (actor?.data == null || !XjRealmRules.TryGet(realmId, out XjRealmRule rule))
		{
			return;
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float current);
		if (current < rule.RequiredZhenYuan)
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, (float)Math.Floor(rule.RequiredZhenYuan));
		}
	}

	private static void EnsureRealmGongFaWithoutDomainEvents(Actor actor, string realmId)
	{
		if (actor?.data == null)
		{
			return;
		}

		int requiredGrade = ResolveManualRealmGongFaGrade(realmId);
		if (requiredGrade <= 0)
		{
			return;
		}

		string daoTu = EnsureManualDaoTu(actor);
		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			EnsureManualJinDanGongFaSet(actor, daoTu);
			EnsureManualCaiQiFa(actor, daoTu);
			return;
		}

		if (current.Found && current.Grade >= requiredGrade)
		{
			EnsureManualRealmHighRealmDependencies(actor, realmId, current);
			return;
		}

		string name = current.Found && !string.IsNullOrWhiteSpace(current.Name)
			? XjGongFaNameLibrary.NormalizeNameForGrade(current.Name, daoTu, requiredGrade)
			: XjGongFaNameLibrary.GenerateName(daoTu, requiredGrade, GetActorId(actor) + (requiredGrade * 1009L));
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		XjGongFaAccessor.WriteState(actor, new XjGongFaState(
			true,
			name,
			requiredGrade,
			0,
			0f,
			daoTu,
			requiredGrade > XjGongFaAccessor.MaxActiveGrade,
			"ManualRealmEntry"));
		XjGongFaAccessor.WriteSource(actor, ResolveManualRealmGongFaSource(realmId));
		EnsureManualCaiQiFa(actor, daoTu);
		EnsureManualRealmHighRealmDependencies(actor, realmId, XjGongFaAccessor.BuildState(actor));
	}

	private static void EnsureManualJinDanGongFaSet(Actor actor, string daoTu)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		int currentYear = GetCurrentYear(actor);
		EnsureXianJiCount(actor, daoTu, XjXianJiState.MaxCount);
		XjActorGongFaCollection.ReconcileWithActor(actor, "ManualJinDanEntry");

		if (XjActorGongFaCollection.HasJinDanGongFaSet(actor))
		{
			if (!EnsureQiuJinFaWithoutDomainEvents(actor, daoTu, string.Empty))
			{
				return;
			}
			AddManualJinDanCanonicalGongFaToConfirmedFamily(actor, currentYear);
			EnsureManualJinDanState(actor, daoTu, currentYear);
			return;
		}

		if (!XjActorGongFaCollection.TryPrepareManualJinDanGrade5Set(
			actor,
			daoTu,
			"金丹境补录",
			out XjActorGongFaCollection.Record sourceGrade5))
		{
			return;
		}

		if (!EnsureQiuJinFaWithoutDomainEvents(actor, daoTu, string.Empty))
		{
			return;
		}
		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		if (!qiuJinFa.Found || !qiuJinFa.Ready)
		{
			return;
		}

		string promotedName = XjGongFaNameLibrary.NormalizeNameForGrade(sourceGrade5.Name, daoTu, 6);
		if (string.IsNullOrWhiteSpace(promotedName))
		{
			promotedName = XjGongFaNameLibrary.GenerateName(daoTu, 6, GetActorId(actor) + 6006L);
		}
		if (string.IsNullOrWhiteSpace(promotedName)
			|| !XjActorGongFaCollection.PromoteBoundGrade5ToGrade6(
				actor,
				string.Empty,
				promotedName,
				daoTu,
				"金丹境补录"))
		{
			return;
		}

		AddManualJinDanCanonicalGongFaToConfirmedFamily(actor, currentYear);
		EnsureManualJinDanState(actor, daoTu, currentYear);
	}

	private static void AddManualJinDanCanonicalGongFaToConfirmedFamily(Actor actor, int currentYear)
	{
		if (actor?.data == null || XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| !XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord familyRecord)
			|| !familyRecord.Found
			|| !string.Equals(familyRecord.ReasonCode, XjFamilyIdentityReasons.Confirmed, StringComparison.Ordinal)
			|| familyRecord.RootActorId <= 0L)
		{
			return;
		}

		IReadOnlyList<XjGongFaInheritanceRecord> records = XjGongFaInheritanceSnapshot.BuildRecords(actor, 4);
		for (int i = 0; i < records.Count; i++)
		{
			XjGongFaInheritanceRecord record = records[i];
			if (!record.Found || string.IsNullOrWhiteSpace(record.Name) || record.Grade < 4)
			{
				continue;
			}

			XjFamilyGongFaWarehouse.AddGongFaToFamily(
				actorId,
				familyRecord.RootActorId,
				record.Name,
				record.Grade,
				currentYear,
				XjFamilyGongFaWarehouse.SourceTypeGongFa,
				record.DaoTu,
				string.Empty,
				record.MappedXianJi);
		}
	}

	private static void EnsureManualCaiQiFa(Actor actor, string daoTu)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		daoTu = daoTu.Trim();
		XjCaiQiFaState current = XjCaiQiFaAccessor.BuildState(actor);
		if (current.Found && string.Equals(current.DaoTu, daoTu, StringComparison.Ordinal))
		{
			AddManualCaiQiFaToConfirmedFamily(actor, current);
			return;
		}

		int currentYear = GetCurrentYear(actor);
		string name = XjCaiQiFaNameLibrary.GenerateCaiQiFaName(daoTu, GetActorId(actor) + currentYear + 7103L);
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		XjCaiQiFaState state = new XjCaiQiFaState(
			true,
			name,
			daoTu,
			"境界补录",
			currentYear,
			"ManualRealmEntry");
		XjCaiQiFaAccessor.WriteState(actor, state);
		AddManualCaiQiFaToConfirmedFamily(actor, state);
	}

	private static void AddManualCaiQiFaToConfirmedFamily(Actor actor, in XjCaiQiFaState state)
	{
		if (actor?.data == null || !state.Found || string.IsNullOrWhiteSpace(state.Name) || string.IsNullOrWhiteSpace(state.DaoTu))
		{
			return;
		}

		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.CaiQiFaObtained(actor, state.Name, state.DaoTu, state.SourcePlace));
	}

	private static int ResolveManualRealmGongFaGrade(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return 3;
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return 4;
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return 5;
		}

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return 6;
		}

		return 0;
	}

	private static string ResolveManualRealmGongFaSource(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return "金丹境补录";
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return "紫府境补录";
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return "筑基境补录";
		}

		return "入道补录";
	}

	private static void EnsureManualRealmHighRealmDependencies(Actor actor, string realmId, in XjGongFaState gongFa)
	{
		if (actor?.data == null || !gongFa.Found)
		{
			return;
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			EnsureXianJiCount(actor, gongFa.DaoTu, 1);
			return;
		}

		if (!string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return;
		}

		EnsureXianJiCount(actor, gongFa.DaoTu, XjXianJiState.MaxCount);
		EnsureQiuJinFaWithoutDomainEvents(actor, gongFa.DaoTu, gongFa.Name);
		EnsureManualJinDanState(actor, gongFa.DaoTu, GetCurrentYear(actor));
	}

	private static void EnsureXianJiCount(Actor actor, string daoTu, int targetCount)
	{
		if (actor?.data == null || targetCount <= 0)
		{
			return;
		}

		int currentYear = GetCurrentYear(actor);
		for (int guard = 0; guard < XjXianJiState.MaxCount; guard++)
		{
			XjXianJiState state = XjXianJiAccessor.BuildState(actor);
			if (state.Count >= targetCount || state.Count >= XjXianJiState.MaxCount)
			{
				return;
			}

			int ordinal = state.Count + 1;
			if (!TryResolveManualMappedXianJi(actor, daoTu, state, ordinal, out string id, out string gongFaName))
			{
				return;
			}

			if (!XjXianJiAccessor.Add(actor, id, currentYear, gongFaName, "手动境界补录"))
			{
				return;
			}
		}
	}

	private static bool TryResolveManualMappedXianJi(
		Actor actor,
		string daoTu,
		in XjXianJiState state,
		int ordinal,
		out string id,
		out string gongFaName)
	{
		id = string.Empty;
		gongFaName = string.Empty;
		if (actor?.data == null || ordinal <= 0 || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		if (ordinal == 1)
		{
			XjGongFaState current = XjGongFaAccessor.BuildState(actor);
			if (current.Found
				&& current.Grade >= 5
				&& XjXianJiCatalog.TryResolveMappedXianJi(daoTu, current.Name, out string mappedId)
				&& XjXianJiCatalog.IsAvailableForProgression(daoTu, ordinal, state.Ids, false, !XjLongShuSystem.IsLongShu(actor), mappedId))
			{
				id = mappedId;
				gongFaName = current.Name;
				return true;
			}
		}

		IReadOnlyList<XjActorGongFaCollection.Record> records = XjActorGongFaCollection.ReadRecords(actor);
		for (int i = 0; i < records.Count; i++)
		{
			XjActorGongFaCollection.Record record = records[i];
			string mappedId = (record.MappedXianJi ?? string.Empty).Trim();
			if (record.Grade < 5
				|| string.IsNullOrWhiteSpace(mappedId)
				|| !XjXianJiCatalog.IsAvailableForProgression(daoTu, ordinal, state.Ids, false, !XjLongShuSystem.IsLongShu(actor), mappedId))
			{
				continue;
			}

			id = mappedId;
			gongFaName = record.Name;
			return true;
		}

		return false;
	}

	internal static bool EnsureDebugQiuJinFa(Actor actor, string daoTu, string sourceGongFaName)
	{
		return EnsureQiuJinFaWithoutDomainEvents(actor, daoTu, sourceGongFaName);
	}

	private static bool EnsureQiuJinFaWithoutDomainEvents(Actor actor, string daoTu, string sourceGongFaName)
	{
		if (actor?.data == null)
		{
			return false;
		}

		daoTu = (daoTu ?? string.Empty).Trim();
		_ = sourceGongFaName;
		sourceGongFaName = string.Empty;
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		XjQiuJinFaState existing = XjQiuJinFaAccessor.BuildState(actor);
		if (existing.Found
			&& existing.Ready
			&& !string.IsNullOrWhiteSpace(existing.BoundAuthority)
			&& XjGuoWeiQuanBingRegistry.IsAuthorityAvailable(daoTu, existing.BoundAuthority))
		{
			AddManualQiuJinFaToConfirmedFamily(actor, existing);
			return true;
		}

		string name = string.Empty;
		string boundAuthority = string.Empty;
		long actorId = GetActorId(actor);
		for (int attempt = 0; attempt < 16; attempt++)
		{
			string candidate = XjQiuJinFaNameLibrary.GenerateName(daoTu, sourceGongFaName, actorId + 9009L + attempt * 7919L);
			if (string.IsNullOrWhiteSpace(candidate))
			{
				continue;
			}
			string authority = XjFamilyHighGradeTransmission.ResolveBoundAuthority(daoTu, candidate, sourceGongFaName);
			if (!string.IsNullOrWhiteSpace(authority)
				&& XjGuoWeiQuanBingRegistry.IsAuthorityAvailable(daoTu, authority))
			{
				name = candidate.Trim();
				boundAuthority = authority.Trim();
				break;
			}
		}
		if (string.IsNullOrWhiteSpace(boundAuthority))
		{
			IReadOnlyList<string> authorities = XjGuoWeiAuthorityCatalog.Get(daoTu);
			if (authorities == null || authorities.Count == 0)
			{
				return false;
			}
			int start = XjDeterministicHash.PositiveIndex(
				actorId,
				daoTu + "|" + sourceGongFaName + "|manual_qiujin_authority",
				authorities.Count);
			for (int offset = 0; offset < authorities.Count; offset++)
			{
				string candidateAuthority = authorities[(start + offset) % authorities.Count];
				if (XjGuoWeiQuanBingRegistry.IsAuthorityAvailable(daoTu, candidateAuthority))
				{
					boundAuthority = candidateAuthority;
					break;
				}
			}
		}
		if (string.IsNullOrWhiteSpace(boundAuthority))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(name))
		{
			name = XjQiuJinFaNameLibrary.GenerateName(daoTu, sourceGongFaName, actorId + 9009L);
		}
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		XjQiuJinFaAccessor.WriteState(actor, new XjQiuJinFaState(
			true,
			name,
			string.Empty,
			0,
			daoTu,
			true,
			GetCurrentYear(actor),
			"ManualRealmEntry",
			boundAuthority));
		AddManualQiuJinFaToConfirmedFamily(actor, XjQiuJinFaAccessor.BuildState(actor));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason, string.Empty);
		return true;
	}

	private static void AddManualQiuJinFaToConfirmedFamily(Actor actor, in XjQiuJinFaState state)
	{
		if (actor?.data == null || !state.Found || string.IsNullOrWhiteSpace(state.Name) || XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| !XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord familyRecord)
			|| !familyRecord.Found
			|| !string.Equals(familyRecord.ReasonCode, XjFamilyIdentityReasons.Confirmed, StringComparison.Ordinal)
			|| familyRecord.RootActorId <= 0L)
		{
			return;
		}

		XjFamilyGongFaWarehouse.AddGongFaToFamily(
			actorId,
			familyRecord.RootActorId,
			state.Name,
			state.SourceGongFaGrade > 0 ? state.SourceGongFaGrade : 5,
			state.LastYear,
			XjFamilyGongFaWarehouse.SourceTypeQiuJinFa,
			state.SourceDaoTu,
			state.SourceGongFaName,
			string.Empty,
			state.BoundAuthority);
	}

	private static void EnsureManualJinDanState(Actor actor, string daoTu, int currentYear)
	{
		if (actor?.data == null)
		{
			return;
		}

		daoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			MarkManualJinDanPending(actor, daoTu, currentYear);
			return;
		}

		if (XjJinDanAccessor.BuildState(actor).Found)
		{
			ClearManualJinDanPending(actor);
			XjGuoWeiRegistry.ReconcileLiveActor(actor);
			EnsureManualJinDanYiXiang(actor, currentYear);
			return;
		}

		XjXianJiState xianJiState = XjXianJiAccessor.BuildState(actor);
		string calculatedType = XjGuoWeiCalculator.Calculate(daoTu, xianJiState);
		string[] searchTypes = BuildManualGuoWeiSearchOrder(daoTu, xianJiState, calculatedType);
		long actorId = GetActorId(actor);
		int safeYear = Math.Max(1, currentYear);
		bool everyResolvedTypeOccupied = searchTypes.Length > 0;

		for (int i = 0; i < searchTypes.Length; i++)
		{
			string candidateType = searchTypes[i];
			string candidateDaoTu = XjGuoWeiCalculator.ResolveManifestDaoTu(daoTu, xianJiState, candidateType);
			if (string.IsNullOrWhiteSpace(candidateDaoTu))
			{
				candidateDaoTu = daoTu;
			}

			if (!XjGuoWeiRegistry.TryResolveAvailableGuoWeiDetailed(
				candidateDaoTu,
				candidateType,
				actorId,
				actorId + safeYear + i * 1009L,
				false,
				out _,
				out string guoWei,
				out XjGuoWeiAvailabilityReason availabilityReason))
			{
				if (availabilityReason != XjGuoWeiAvailabilityReason.Occupied)
				{
					everyResolvedTypeOccupied = false;
				}
				continue;
			}

			everyResolvedTypeOccupied = false;
			string jinXing = XjJinXingCalculator.Calculate(candidateDaoTu, actorId);
			if (string.IsNullOrWhiteSpace(jinXing)
				|| !XjGuoWeiRegistry.TryClaim(actor, candidateDaoTu, jinXing, guoWei, safeYear))
			{
				MarkManualJinDanPending(actor, daoTu, safeYear);
				return;
			}

			if (!XjCultivationStateTransitions.TrySetDaoTu(actor, candidateDaoTu, false))
			{
				XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
				MarkManualJinDanPending(actor, daoTu, safeYear);
				return;
			}

			if (!XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.JinDan, false))
			{
				XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, false);
				XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
				MarkManualJinDanPending(actor, daoTu, safeYear);
				return;
			}

			XjJinDanAccessor.WriteSuccess(actor, jinXing, guoWei, safeYear);
			if (!XjJinDanAccessor.BuildState(actor).Found)
			{
				XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false);
				XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, false);
				XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
				MarkManualJinDanPending(actor, daoTu, safeYear);
				return;
			}

			ClearManualJinDanPending(actor);
			XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, candidateDaoTu, guoWei, safeYear);
			EnsureManualJinDanYiXiang(actor, safeYear);
			return;
		}

		if (everyResolvedTypeOccupied)
		{
			ClearManualJinDanPending(actor);
			XuanJianVNext.Systems.Combat.XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)5, true);
			return;
		}

		// 规则封锁、隐藏果位、数据异常和同年抢位都不是“十个果位全满”。
		MarkManualJinDanPending(actor, daoTu, safeYear);
	}

	internal static void TickPendingManualJinDan(Actor actor, int currentYear)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjManualJinDanPending, out int pending)
			|| pending <= 0
			|| !XjSafeCore.IsAliveActor(actor))
		{
			return;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjManualJinDanPendingYear, out int pendingYear);
		if (currentYear > 0 && pendingYear >= currentYear)
		{
			return;
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjManualJinDanPendingDaoTu, out string daoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out daoTu);
		}
		EnsureManualJinDanState(actor, daoTu, currentYear);
		if (XjJinDanAccessor.BuildState(actor).Found)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			XjRealmTitleApplyService.ApplyOnPromotion(actor, XjRealmIds.JinDan, daoTu);
			EnsureManualJinDanZongMen(actor, XjRealmIds.JinDan);
			RunManualJinDanSuccessEventChain(actor);
			RefreshManualRealmFamilyInheritance(actor);
		}
	}

	private static void MarkManualJinDanPending(Actor actor, string daoTu, int currentYear)
	{
		if (actor?.data == null)
		{
			return;
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjManualJinDanPending, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjManualJinDanPendingYear, Math.Max(0, currentYear));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjManualJinDanPendingDaoTu, (daoTu ?? string.Empty).Trim());
		if (!XjJinDanAccessor.BuildState(actor).Found)
		{
			XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false);
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
	}

	private static void ClearManualJinDanPending(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjManualJinDanPending, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjManualJinDanPendingYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjManualJinDanPendingDaoTu, string.Empty);
	}

	private static string[] BuildManualGuoWeiSearchOrder(
		string daoTu,
		in XjXianJiState xianJiState,
		string calculatedType)
	{
		if (string.Equals(calculatedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			return new[] { XjGuoWeiCalculator.ZhengWei, XjGuoWeiCalculator.YuWei, XjGuoWeiCalculator.RunWei };
		}
		if (string.Equals(calculatedType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			return new[] { XjGuoWeiCalculator.YuWei, XjGuoWeiCalculator.RunWei, XjGuoWeiCalculator.ZhengWei };
		}
		if (string.Equals(calculatedType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			return new[] { XjGuoWeiCalculator.RunWei, XjGuoWeiCalculator.YuWei, XjGuoWeiCalculator.ZhengWei };
		}

		int nativeCount = 0;
		int lowerCount = 0;
		int adjacentCount = 0;
		int otherCount = 0;
		if (xianJiState.Ids != null)
		{
			for (int i = 0; i < xianJiState.Ids.Length && i < XjXianJiState.MaxCount; i++)
			{
				switch (XjXianJiCatalog.GetPoolKind(daoTu, xianJiState.Ids[i]))
				{
				case XjXianJiPoolKind.Native:
					nativeCount++;
					break;
				case XjXianJiPoolKind.Lower:
					lowerCount++;
					break;
				case XjXianJiPoolKind.Adjacent:
					adjacentCount++;
					break;
				default:
					otherCount++;
					break;
				}
			}
		}

		int zhengScore = Math.Abs(nativeCount - 5) + (lowerCount + adjacentCount) * 2 + otherCount * 3;
		int yuScore = Math.Abs(nativeCount - 3) + Math.Abs(lowerCount - 2) + adjacentCount * 2 + otherCount * 3;
		int runScore = Math.Max(0, 2 - nativeCount) * 2 + (adjacentCount > 0 ? 0 : 2) + otherCount * 3;
		if (runScore <= yuScore && runScore <= zhengScore)
		{
			return new[] { XjGuoWeiCalculator.RunWei, XjGuoWeiCalculator.YuWei, XjGuoWeiCalculator.ZhengWei };
		}
		if (yuScore <= zhengScore)
		{
			return new[] { XjGuoWeiCalculator.YuWei, XjGuoWeiCalculator.RunWei, XjGuoWeiCalculator.ZhengWei };
		}
		return new[] { XjGuoWeiCalculator.ZhengWei, XjGuoWeiCalculator.YuWei, XjGuoWeiCalculator.RunWei };
	}

	private static void EnsureManualJinDanYiXiang(Actor actor, int currentYear)
	{
		if (actor?.data == null
			|| (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang) && yiXiang > 0))
		{
			return;
		}

		long actorId = GetActorId(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang,
			XjDeterministicHash.PositiveIndex(actorId + Math.Max(0, currentYear), "manual_jindan_yixiang", 300) + 1);
	}

	private static bool TryResolveRealm(string traitId, out string realmId, out int rank)
	{
		realmId = string.Empty;
		rank = 0;

		if (string.Equals(traitId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.TaiXi;
			rank = 1;
			return true;
		}

		if (string.Equals(traitId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.LianQi;
			rank = 2;
			return true;
		}

		if (string.Equals(traitId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.ZhuJi;
			rank = 3;
			return true;
		}

		if (string.Equals(traitId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.ZiFu;
			rank = 4;
			return true;
		}

		if (string.Equals(traitId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.JinDan;
			rank = 5;
			return true;
		}

		return false;
	}

	private static int GetRealmRank(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return 1;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return 2;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 3;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 4;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return 5;
		return 0;
	}

	private static void EnsureVisibleRealmTrait(Actor actor, string realmId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(realmId))
		{
			return;
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		XjVisibleTraitSync.SyncRealmTrait(actor, realmId, zhenYuan);
	}

	private static bool IsAlive(Actor actor)
	{
		try
		{
			return actor != null && XjSafeCore.IsAliveActor(actor);
		}
		catch
		{
			return actor != null;
		}
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}

	private static int GetWorldYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}
}
