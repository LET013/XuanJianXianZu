using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.XianGuo;

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
		// 只有仙国法的制度性持玄境界属于只读投影；紫府/真人、金丹/真君羽士与
		// 两路道胎都允许从编辑器手动补录，并由下方统一协调器构造对应权威状态。
		if (XjManualHighRealmGrantPolicy.IsProtectedHighRealmTrait(traitId))
		{
			RemoveBlockedManualHighRealmTrait(actor, traitId);
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

	private static void RemoveBlockedManualHighRealmTrait(Actor actor, string traitId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(traitId) || !actor.hasTrait(traitId)) return;
		try
		{
			_isReconciling = true;
			actor.removeTrait(traitId);
		}
		finally
		{
			_isReconciling = false;
		}
		// 若角色本来就由真实事务处于该高境，重新从权威状态投影回来；
		// 若只是手动塞入特质，则不会留下任何境界状态。
		XjVisibleTraitSync.SyncCultivationTraits(actor);
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

	/// <summary>
	/// 制度性外力（当前仅帝明阳国势反哺）推动紫府金丹道城主“借”一重大境界。
	/// 这里与人物真实修炼状态完全隔离：只建立仙国法持玄投影，不写 RealmId、真元、
	/// 功法、仙基、金性、果位、权柄、果位意象或金丹状态，也不触发真实高境成功事件链。
	/// </summary>
	internal static bool TryGrantInstitutionalZiJinPromotion(
		Actor actor,
		string targetRealmId,
		int currentYear,
		string source)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsZiFuJinDan(actor))
		{
			return false;
		}

		string target = XjRealmHelper.NormalizeId(targetRealmId);
		int targetTier = target switch
		{
			var id when string.Equals(id, XjRealmIds.ZhuJi, StringComparison.Ordinal) => XjRealmSuppression.TierZhuJi,
			var id when string.Equals(id, XjRealmIds.ZiFu, StringComparison.Ordinal) => XjRealmSuppression.TierZiFu,
			var id when string.Equals(id, XjRealmIds.JinDan, StringComparison.Ordinal) => XjRealmSuppression.TierJinDan,
			_ => XjRealmSuppression.TierNone
		};
		if (targetTier <= XjRealmSuppression.TierNone) return false;

		int realTier = XjRealmSuppression.GetRealmTier(actor);
		int currentEffectiveTier = XjXianGuoSystem.ResolveInstitutionalEffectiveTier(actor, realTier);
		if (targetTier != currentEffectiveTier + 1) return false;

		return XjXianGuoSystem.TryGrantInstitutionalProjection(
			actor,
			targetTier,
			Math.Max(1, currentYear),
			string.IsNullOrWhiteSpace(source) ? "帝明阳国势反哺" : source.Trim());
	}

	internal static void HandleRemoved(Actor actor, string traitId, bool removed)
	{
		if (!removed || _isReconciling || XjCultivationStateTransitions.IsVisibleTraitSyncActive)
		{
			return;
		}
		if (string.Equals(traitId, XjXianGuoSystem.InstitutionalZhuJiTraitId, StringComparison.Ordinal)
			|| string.Equals(traitId, XjXianGuoSystem.InstitutionalZiFuTraitId, StringComparison.Ordinal)
			|| string.Equals(traitId, XjXianGuoSystem.InstitutionalFakeJinDanTraitId, StringComparison.Ordinal))
		{
			// 旧档可能仍残留早期的三枚仙国境界特质；若外部手动触碰它们，只按当前持玄权威重新投影。
			XjVisibleTraitSync.SyncCultivationTraits(actor);
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
			long actorId = ((BaseSystemData)actor.data).id;
			string requested = daoTu.Trim();
			if (actorId > 0L
				&& XjDaoTuVisibleTraitCatalog.TryResolveCanonicalDisplayName(requested, actorId, out string canonicalRequested)
				&& !string.IsNullOrWhiteSpace(canonicalRequested))
			{
				requested = canonicalRequested;
			}

			XjJinDanState carrier = XjJinDanAccessor.BuildPositionCarrierState(actor);
			if (carrier.Found)
			{
				// A formed position is a world-level identity, not a removable UI tag. A plain
				// trait edit may correct a RunWei manifest projection, but it must not silently
				// release/rebuild fruits, authorities and JinXing behind the player's back.
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string rootDaoTu);
				XjHighRealmDaoStateService.ResolvePositionIdentity(
					actor, carrier.GuoWei, out string sourceDaoTu, out string manifestDaoTu);
				bool isRunWei = string.Equals(
					XjGuoWeiRegistry.ResolveTypeFromName(carrier.GuoWei),
					XjGuoWeiCalculator.RunWei,
					StringComparison.Ordinal);
				bool matchesRoot = string.Equals((rootDaoTu ?? string.Empty).Trim(), requested, StringComparison.Ordinal)
					|| string.Equals((sourceDaoTu ?? string.Empty).Trim(), requested, StringComparison.Ordinal);
				bool matchesRunManifest = isRunWei
					&& string.Equals((manifestDaoTu ?? string.Empty).Trim(), requested, StringComparison.Ordinal);

				if (matchesRoot || matchesRunManifest)
				{
					XjVisibleTraitSync.SyncDaoTuTrait(actor, string.IsNullOrWhiteSpace(rootDaoTu) ? sourceDaoTu : rootDaoTu);
				}
				else
				{
					// Reject immediately and restore the position-authoritative projection. The old
					// behavior accepted the tag briefly and only reverted it during annual GuoWei
					// reconciliation, which looked like a random DaoTu jump.
					XjVisibleTraitSync.SyncCultivationTraits(actor);
				}
				return;
			}

			if (XjCultivationStateTransitions.TrySetDaoTu(actor, requested, true))
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string committedDaoTu);
				if (string.Equals((committedDaoTu ?? string.Empty).Trim(), requested, StringComparison.Ordinal))
				{
					// Explicit editor choice outranks later family/CaiQi preference assignment, but
						// active imperial/favored-position locks still take precedence in the central
						// DaoTu transaction. This is an admin/editor override, never a natural roll.
					XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTuManualOverride, committedDaoTu.Trim());
					MarkManualDaoTuDiscovered(actor, committedDaoTu);
				}
			}
			XjManualCultivationWake.EnsureAwake(actor, ensureMinimumAptitude: false);
		}
		finally
		{
			_isReconciling = false;
		}
	}

	private static bool TrySetManualDaoTuMetadata(Actor actor, string daoTu)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		string normalized = daoTu.Trim();
		if (!string.Equals(normalized, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal))
		{
			return XjCultivationStateTransitions.TrySetDaoTuMetadataOnly(actor, normalized, false);
		}

		// 手动选择青宣只写入道途身份，不得借此提前开放采气。青宣采气仍由
		// 首位空证真君的正式事务单向解锁。
		if (!XjCultivationPathTransitions.TryEnsureZiFuJinDan(actor, syncVisibleTraits: false))
		{
			return false;
		}
		XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, normalized, false);
		return true;
	}

	private static void MarkManualDaoTuDiscovered(Actor actor, string daoTu)
	{
		if (actor?.data == null
			|| !XjDaoTuCatalog.TryResolve(daoTu, out XjDaoTuDefinition definition)
			|| !definition.IsBingGu)
		{
			return;
		}

		XjDaoTuManifestRegistry.MarkDiscovered(
			definition.RootId,
			GetActorId(actor),
			Math.Max(1, GetCurrentYear(actor)));
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
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string authoritativeDaoTu);
			if (!string.IsNullOrWhiteSpace(authoritativeDaoTu))
			{
				XjVisibleTraitSync.SyncDaoTuTrait(actor, authoritativeDaoTu);
			}
		}
		finally
		{
			_isReconciling = false;
		}
	}


	private static void ReconcileRealmGrant(Actor actor, string targetRealmId, int targetRank, string previousRealmId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(targetRealmId))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string currentRealmId);
		string sourceRealmId = string.IsNullOrWhiteSpace(previousRealmId) ? currentRealmId : previousRealmId;
		if (XjCultivationPathRules.IsFuQiRealm(targetRealmId))
		{
			XjManualFuQiRealmReconciliation.ReconcileGrant(actor, targetRealmId, sourceRealmId);
			return;
		}
		if (string.Equals(targetRealmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			&& XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			// 手动点选紫府金丹道胎是显式调试编辑，允许从服气身份切换到紫金道胎。
			XjCultivationPathTransitions.ClearAll(actor);
		}

		// 手动给服气真人添加紫府特质时，必须走正式单向转修事务，不能由通用
		// 境界补录把服气身份与紫府境界强行叠在一起。默认保持原道途；原道途
		// 尚无紫金修法时，使用当前可选列表中的首项。
		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& XjCultivationPathRules.IsFuQiYangXing(actor)
			&& string.Equals(XjRealmHelper.NormalizeId(sourceRealmId), XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string preferredDaoTu);
			IReadOnlyList<XjFuQiToZiFuTargetOption> options = XjFuQiToZiFuTransitionSystem.BuildTargetOptions(actor);
			string targetDaoTu = string.Empty;
			for (int i = 0; i < options.Count; i++)
			{
				if (string.Equals(options[i].DaoTu, (preferredDaoTu ?? string.Empty).Trim(), StringComparison.Ordinal))
				{
					targetDaoTu = options[i].DaoTu;
					break;
				}
			}
			if (string.IsNullOrWhiteSpace(targetDaoTu) && options.Count > 0) targetDaoTu = options[0].DaoTu;
			if (string.IsNullOrWhiteSpace(targetDaoTu)
				|| !XjFuQiToZiFuTransitionSystem.TryConvert(actor, targetDaoTu, out _))
			{
				XjVisibleTraitSync.SyncCultivationTraits(actor);
			}
			return;
		}
		if (XjCultivationPathRules.IsZiFuJinDan(actor) && XjCultivationPathRules.IsFuQiRealm(targetRealmId))
		{
			// 紫金道绝对不能反向进入服气养性。
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}

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
		if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.DaoTai, StringComparison.Ordinal))
		{
			// 道途在资质之后才最终确定；并火等运行期修正必须按最终道途再校验一次。
			EnsureManualFruitPositionDaoHui(actor);
		}

		if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			if (XjCultivationPathRules.IsFuQiYangXing(actor)
				&& !TryConvertManualFuQiActorToZiFu(actor))
			{
				XjVisibleTraitSync.SyncCultivationTraits(actor);
				return;
			}
			currentRank = GetRealmRank(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
			ReconcileManualJinDanGrant(actor, currentRank, targetRank);
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
			EnsureManualDaoTaiSupplement(actor, targetRealmId);
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
			XjBloodlineRefreshLane.Request(actor, GetCurrentYear(actor));
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
		EnsureManualDaoTaiSupplement(actor, targetRealmId);
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

	private static bool TryConvertManualFuQiActorToZiFu(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return actor?.data != null;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string preferredDaoTu);
		IReadOnlyList<XjFuQiToZiFuTargetOption> options = XjFuQiToZiFuTransitionSystem.BuildTargetOptions(actor);
		string targetDaoTu = string.Empty;
		for (int i = 0; i < options.Count; i++)
		{
			if (string.Equals(options[i].DaoTu, (preferredDaoTu ?? string.Empty).Trim(), StringComparison.Ordinal))
			{
				targetDaoTu = options[i].DaoTu;
				break;
			}
		}
		if (string.IsNullOrWhiteSpace(targetDaoTu) && options.Count > 0)
		{
			targetDaoTu = options[0].DaoTu;
		}
		return !string.IsNullOrWhiteSpace(targetDaoTu)
			&& XjFuQiToZiFuTransitionSystem.TryConvert(actor, targetDaoTu, out _);
	}

	private static void ReconcileManualJinDanGrant(Actor actor, int currentRank, int targetRank)
	{
		if (actor?.data == null)
		{
			return;
		}

		int currentYear = Math.Max(1, GetCurrentYear(actor));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}

		// 手动金丹是编辑器的一次性事务。先保存会被“紫府准备态”改写的三类核心投影，
		// 如果果位事务最终无法提交，就恢复到编辑前状态，不留下五神通大真人。
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string originalRealmId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int originalRealmEnteredYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhuJiEnteredYear, out int originalZhuJiEnteredYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuEnteredYear, out int originalZiFuEnteredYear);
		XjXianJiState originalXianJi = XjXianJiAccessor.BuildState(actor);
		string originalXianJiIds = originalXianJi.Ids == null ? string.Empty : string.Join("|", originalXianJi.Ids);
		bool hadGongFaSnapshot = XjActorGongFaCollection.TryExportSerialized(actor, out int originalGongFaVersion, out string originalGongFaJson);
		XjQiuJinFaState originalQiuJinFa = XjQiuJinFaAccessor.BuildState(actor);

		XjCultivationSeed.EnsureSeedState(actor);
		bool isPromotion = targetRank > currentRank;

		if (currentRank < GetRealmRank(XjRealmIds.ZiFu)
			&& !XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false))
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}
		EnsureMinimumZhenYuan(actor, XjRealmIds.ZiFu);
		EnsureManualZiFuFoundation(actor, XjRealmIds.ZiFu);

		bool committed = EnsureHighRealmGongFaSet(actor, daoTu)
			&& XjJinDanAccessor.BuildState(actor).Found;
		if (!committed)
		{
			RollbackManualJinDanGrant(
				actor, originalRealmId, originalRealmEnteredYear, originalZhuJiEnteredYear, originalZiFuEnteredYear,
				originalXianJiIds, originalXianJi.LastYear, hadGongFaSnapshot,
				originalGongFaVersion, originalGongFaJson, in originalQiuJinFa);
			XjManualCultivationWake.EnsureAwake(actor);
			return;
		}

		XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.JinDan, false);
		if (isPromotion)
		{
			// 命数、血脉与家族外部副作用只允许在果位事务成功后提交。
			ApplyManualRealmMingShuRewards(actor, currentRank, targetRank);
			XjBloodlineRefreshLane.Request(actor, currentYear);
		}
		EnsureManualHighRealmFamilyBeforeInheritance(actor, targetRank);
		EnsureMinimumZhenYuan(actor, XjRealmIds.JinDan);
		// 手动授予金丹也必须走与正常破境相同的高境后处理。此前这里漏掉了
		// DaoTaiMerit 的观察入口，导致首位手动金丹的道胎功绩恒为零。
		// 不发放寿元类手动奖励，只补事件登记、功绩与高境索引。
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
			actor,
			XjRealmIds.JinDan,
			currentYear,
			applyMingShuReward: false,
			syncVisibleTraits: false,
			restoreHealth: false);
		XjRealmTitleApplyService.ApplyOnPromotion(actor, XjRealmIds.JinDan, daoTu);
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		XjCultivationStateTransitions.RestoreHealthAfterRealmPromotion(actor);
		EnsureManualHighRealmCompanionTraits(actor, targetRank);
		EnsureVisibleRealmTrait(actor, XjRealmIds.JinDan);
		EnsureManualJinDanZongMen(actor, XjRealmIds.JinDan);
		RunManualJinDanSuccessEventChain(actor);
		EnsureManualLongShuTrigger(actor, XjRealmIds.JinDan);
		XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.JinDan, "ManualRealmGrant");
		RefreshManualRealmFamilyInheritance(actor);
		XjManualCultivationWake.EnsureAwake(actor);
	}

	private static void RollbackManualJinDanGrant(
		Actor actor,
		string originalRealmId,
		int originalRealmEnteredYear,
		int originalZhuJiEnteredYear,
		int originalZiFuEnteredYear,
		string originalXianJiIds,
		int originalXianJiLastYear,
		bool hadGongFaSnapshot,
		int originalGongFaVersion,
		string originalGongFaJson,
		in XjQiuJinFaState originalQiuJinFa)
	{
		if (actor?.data == null) return;

		ClearManualJinDanPending(actor);
		// 任何已经写到一半的金性/果位/权柄必须先由高境唯一清理入口释放。
		XjJinDanAccessor.ClearSuccess(actor);
		XjCultivationStateTransitions.RestoreRealmSnapshotForRecovery(
			actor,
			originalRealmId,
			originalRealmEnteredYear,
			originalZhuJiEnteredYear,
			originalZiFuEnteredYear,
			"ManualJinDanRollback");

		XjXianJiAccessor.RestoreSnapshot(actor, originalXianJiIds, originalXianJiLastYear);
		if (hadGongFaSnapshot && !string.IsNullOrWhiteSpace(originalGongFaJson))
		{
			XjActorGongFaCollection.TryRestoreSerialized(
				actor, originalGongFaVersion, originalGongFaJson, "ManualJinDanRollback");
		}
		else
		{
			XjActorGongFaCollection.ReconcileWithActor(actor, "ManualJinDanRollback");
			XjActorGongFaCollection.ReconcileGradeCap(actor, "ManualJinDanRollback");
		}

		if (originalQiuJinFa.Found)
		{
			XjQiuJinFaAccessor.WriteState(actor, originalQiuJinFa);
		}
		else
		{
			XjQiuJinFaAccessor.Clear(actor, "ManualJinDanRollback");
		}
		XjVisibleTraitSync.SyncCultivationTraits(actor);
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
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal))
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
		XjSectCityData.HandleJinDanPromotion(actor, currentYear);
	}

	private static void EnsureManualDaoTaiSupplement(Actor actor, string realmId)
	{
		if (actor?.data == null)
		{
			return;
		}
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (!string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			&& !string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal))
		{
			return;
		}

		int currentYear = Math.Max(1, GetWorldYear(actor));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		long actorId = GetActorId(actor);
		bool firstDaoTaiRegistration = actorId > 0L && !XjDaoTaiPresenceArchive.IsRegisteredDaoTai(actorId);
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
			actor,
			normalized,
			currentYear,
			applyMingShuReward: false,
			syncVisibleTraits: false);
		if (string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal))
		{
			// 紫府金丹道的道胎与服气道胎共用果位、权柄和金性展示模板。
			// 手动赋予时需先补齐金丹高境状态；旧流程中间会短暂回写金丹，
			// 却没有稳定恢复道胎，导致右侧照录只剩道途字段。
			EnsureHighRealmGongFaSet(actor, daoTu);
			EnsureManualJinDanState(actor, daoTu, currentYear);
			if (XjJinDanAccessor.BuildState(actor).Found)
			{
				XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.DaoTai, false);
			}
		}
		XjRealmTitleApplyService.ApplyOnDaoTaiAscension(actor, daoTu);
		XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
		XjGongFaProgression.EnsureRealmMinimumGrade(actor, normalized, daoTu);
		XjDaoTaiGongFaService.EnsureGradeSeven(actor, currentYear);
		XjGuoWeiQuanBingLifecycle.RefreshProgressAuthorities(actor, currentYear);
		XjAutoCollectSystem.TryCollectRealm(actor, normalized, "ManualDaoTaiRealmGrant");
		XjDaoTaiPresenceArchive.ObserveLiveDaoTai(actor, currentYear);
		if (firstDaoTaiRegistration)
		{
			string guoWei = XjDaoTaiMeritSystem.ResolveGuoWei(actor);
			XjDaoTaiAscensionSystem.BroadcastDaoTaiAscension(actor, daoTu, guoWei, currentYear);
		}
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
		XjAptitudeEffectRules.EnsureHuiGuangMinimumBoundToAptitude(actor, target);

		// 玩家/调试器显式点选金丹或道胎属于一次性状态命令，不应再被自然求金
		// 的随机初始道慧挡回紫府。只在显式编辑入口把有效道慧补到果位最低线；
		// 并火存在境界修正，因此用有界递增校验而不是假设存档值等于有效值。
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal))
		{
			EnsureManualFruitPositionDaoHui(actor);
		}
	}

	private static void EnsureManualFruitPositionDaoHui(Actor actor)
	{
		if (actor?.data == null || XjDaoHuiPolicy.Read(actor) + 0.001f >= XjDaoHuiPolicy.FruitPositionThreshold)
		{
			return;
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float stored);
		float candidate = XjDaoHuiPolicy.Clamp(Math.Max(stored, XjDaoHuiPolicy.FruitPositionThreshold));
		while (candidate < XjDaoHuiPolicy.Maximum
			&& XjDaoHuiPolicy.ApplyDaoTuRealmModifier(actor, candidate) + 0.001f < XjDaoHuiPolicy.FruitPositionThreshold)
		{
			candidate = XjDaoHuiPolicy.Clamp(candidate + 1f);
		}
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, candidate);
	}

	private static int ResolveMinimumManualAptitude(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return 6;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return 5;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return 4;
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
		bool explicitlySelectedQingXuan = actor.hasTrait("XjQingXuanDaoTu");
		if (!string.IsNullOrWhiteSpace(daoTu)
			&& XjDaoTuVisibleTraitCatalog.TryResolveTraitId(daoTu, out _)
			&& (!string.Equals(daoTu, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
				|| XjQingXuanKongZhengSystem.CanEnterQingXuan(actor)
				|| explicitlySelectedQingXuan))
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
					|| XjQingXuanKongZhengSystem.CanEnterQingXuan(actor)
					|| string.Equals(traitId, "XjQingXuanDaoTu", StringComparison.Ordinal))
				&& TrySetManualDaoTuMetadata(actor, resolvedDaoTu))
			{
				return resolvedDaoTu;
			}
		}

		if (TryPickManualDaoTu(actor, out string pickedDaoTu)
			&& TrySetManualDaoTuMetadata(actor, pickedDaoTu))
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
				|| string.Equals(displayName.Trim(), XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
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
			EnsureHighRealmGongFaSet(actor, daoTu);
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

	internal static bool EnsureHighRealmGongFaSet(Actor actor, string daoTu)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		int currentYear = GetCurrentYear(actor);
		if (!EnsureManualJinDanXianJiSet(actor, daoTu, currentYear))
		{
			return false;
		}
		XjActorGongFaCollection.ReconcileWithActor(actor, "ManualJinDanEntry");

		if (XjActorGongFaCollection.HasJinDanGongFaSet(actor))
		{
			if (!EnsureQiuJinFaWithoutDomainEvents(actor, daoTu, string.Empty, publishFamilyInheritance: false))
			{
				return false;
			}
			if (!EnsureManualJinDanState(actor, daoTu, currentYear))
			{
				return false;
			}
			AddManualQiuJinFaToConfirmedFamily(actor, XjQiuJinFaAccessor.BuildState(actor));
			AddManualJinDanCanonicalGongFaToConfirmedFamily(actor, currentYear);
			return true;
		}

		if (!XjActorGongFaCollection.TryPrepareManualJinDanGrade5Set(
			actor,
			daoTu,
			"金丹境补录",
			out XjActorGongFaCollection.Record sourceGrade5))
		{
			return false;
		}

		if (!EnsureQiuJinFaWithoutDomainEvents(actor, daoTu, string.Empty, publishFamilyInheritance: false))
		{
			return false;
		}
		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		if (!qiuJinFa.Found || !qiuJinFa.Ready)
		{
			return false;
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
			return false;
		}

		if (!XjActorGongFaCollection.HasJinDanGongFaSet(actor)
			|| !EnsureManualJinDanState(actor, daoTu, currentYear))
		{
			return false;
		}
		AddManualQiuJinFaToConfirmedFamily(actor, XjQiuJinFaAccessor.BuildState(actor));
		AddManualJinDanCanonicalGongFaToConfirmedFamily(actor, currentYear);
		return true;
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
		if (string.Equals(daoTu, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
			&& !XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.QingXuan))
		{
			return;
		}
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
		}
		// 金丹依赖禁止从“补一部功法”这样的局部 helper 触发。
		// 显式金丹只能由 ReconcileManualJinDanGrant 一次性提交/回滚，
		// 否则未来任何复用本方法的调用者都可能重新造出紫府五神通半状态。
	}

	/// <summary>
	/// 显式赋予金丹特质时，将五门神通收束为可判定果位的三池结构。
	/// 自然修炼的紫府数据不经过此入口，合法的异道神通也不会被全局迁移。
	/// </summary>
	private static bool EnsureManualJinDanXianJiSet(Actor actor, string daoTu, int currentYear)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		daoTu = daoTu.Trim();
		if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(daoTu, out string visibleDaoTu))
		{
			daoTu = visibleDaoTu;
		}
		if (XjZiJinSwordDaoCatalog.IsLongGeng(daoTu) && !XjFuQiSwordWorldState.IsEstablished)
		{
			return false;
		}

		XjXianJiState existing = XjXianJiAccessor.BuildState(actor);
		string existingType = XjGuoWeiCalculator.Calculate(actor, daoTu, existing);
		if (existing.Count >= XjXianJiState.MaxCount
			&& !string.Equals(existingType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal)
			&& HasOnlyManualJinDanPools(daoTu, existing.Ids))
		{
			return true;
		}

		bool hasLower = false;
		bool hasAdjacent = false;
		string preferredAdjacentDaoTu = string.Empty;
		for (int i = 0; existing.Ids != null && i < existing.Ids.Length; i++)
		{
			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(daoTu, existing.Ids[i]);
			if (kind == XjXianJiPoolKind.Lower) hasLower = true;
			if (kind == XjXianJiPoolKind.Adjacent)
			{
				hasAdjacent = true;
				if (string.IsNullOrWhiteSpace(preferredAdjacentDaoTu))
				{
					XjXianJiCatalog.TryGetAdjacentDaoTuForXianJi(
						daoTu, existing.Ids[i], out preferredAdjacentDaoTu);
				}
			}
		}

		string targetType = hasAdjacent
			? XjGuoWeiCalculator.RunWei
			: hasLower ? XjGuoWeiCalculator.YuWei : XjGuoWeiCalculator.ZhengWei;
		List<string> result = new List<string>(XjXianJiState.MaxCount);
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		long seed = GetActorId(actor) + Math.Max(1, currentYear) * 131L;

		if (string.Equals(targetType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			AppendExistingManualPool(daoTu, existing.Ids, XjXianJiPoolKind.Native, string.Empty, 5, result, seen);
			if (!FillManualPool(daoTu, XjXianJiPoolKind.Native, string.Empty, 5, seed, result, seen))
			{
				return false;
			}
		}
		else if (string.Equals(targetType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			int nativeTarget = XjQingXuanKongZhengSystem.IsQingXuanDaoTu(daoTu) ? 4 : 3;
			int lowerTarget = XjXianJiState.MaxCount - nativeTarget;
			AppendExistingManualPool(daoTu, existing.Ids, XjXianJiPoolKind.Native, string.Empty, nativeTarget, result, seen);
			if (!FillManualPool(daoTu, XjXianJiPoolKind.Native, string.Empty, nativeTarget, seed, result, seen))
			{
				return false;
			}
			AppendExistingManualPool(daoTu, existing.Ids, XjXianJiPoolKind.Lower, string.Empty, lowerTarget, result, seen);
			if (!FillManualPool(daoTu, XjXianJiPoolKind.Lower, string.Empty, lowerTarget, seed + 7001L, result, seen))
			{
				return false;
			}
		}
		else
		{
			// 闰位的唯一合法结构与自然修炼完全一致：四门根道上位 + 一门显道上位。
			// 旧手动补录曾写成 3+2，正是“厥阴闰太阴后被显道反向重写”的另一入口。
			AppendExistingManualPool(daoTu, existing.Ids, XjXianJiPoolKind.Native, string.Empty, 4, result, seen);
			if (!FillManualPool(daoTu, XjXianJiPoolKind.Native, string.Empty, 4, seed, result, seen))
			{
				return false;
			}

			AppendExistingManualPool(
				daoTu, existing.Ids, XjXianJiPoolKind.Adjacent, preferredAdjacentDaoTu, 1, result, seen);
			if (string.IsNullOrWhiteSpace(preferredAdjacentDaoTu))
			{
				for (int i = 0; i < result.Count; i++)
				{
					if (XjXianJiCatalog.TryGetAdjacentDaoTuForXianJi(daoTu, result[i], out preferredAdjacentDaoTu)) break;
				}
			}
			if (CountManualPool(daoTu, result, XjXianJiPoolKind.Adjacent, preferredAdjacentDaoTu) < 1)
			{
				if (!XjXianJiCatalog.TryPickFromPool(
					daoTu, XjXianJiPoolKind.Adjacent, seed + 9001L, result.ToArray(), string.Empty, out string firstAdjacent)
					|| string.IsNullOrWhiteSpace(firstAdjacent)
					|| !seen.Add(firstAdjacent))
				{
					return false;
				}
				result.Add(firstAdjacent);
				XjXianJiCatalog.TryGetAdjacentDaoTuForXianJi(daoTu, firstAdjacent, out preferredAdjacentDaoTu);
			}
			if (!FillManualPool(daoTu, XjXianJiPoolKind.Adjacent, preferredAdjacentDaoTu, 1, seed + 11003L, result, seen))
			{
				return false;
			}
		}

		if (result.Count != XjXianJiState.MaxCount)
		{
			return false;
		}
		XjXianJiState rebuilt = new XjXianJiState(true, result.Count, result.ToArray(), Math.Max(0, currentYear), "ManualJinDanThreePool");
		string rebuiltType = XjGuoWeiCalculator.Calculate(actor, daoTu, rebuilt);
		if (string.Equals(rebuiltType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal)
			|| !HasOnlyManualJinDanPools(daoTu, result.ToArray()))
		{
			return false;
		}

		string[] ids = result.ToArray();
		if (!XjActorGongFaCollection.ReplaceAllForManualDaoTu(actor, daoTu, ids, "金丹境三池补录"))
		{
			return false;
		}
		return XjXianJiAccessor.RestoreSnapshot(actor, string.Join("|", ids), Math.Max(0, currentYear));
	}

	private static bool HasOnlyManualJinDanPools(string daoTu, string[] ids)
	{
		if (ids == null || ids.Length < XjXianJiState.MaxCount) return false;
		for (int i = 0; i < XjXianJiState.MaxCount; i++)
		{
			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(daoTu, ids[i]);
			if (kind != XjXianJiPoolKind.Native
				&& kind != XjXianJiPoolKind.Lower
				&& kind != XjXianJiPoolKind.Adjacent)
			{
				return false;
			}
		}
		return true;
	}

	private static void AppendExistingManualPool(
		string daoTu,
		string[] existingIds,
		XjXianJiPoolKind poolKind,
		string preferredAdjacentDaoTu,
		int targetCount,
		List<string> result,
		HashSet<string> seen)
	{
		for (int i = 0; existingIds != null && i < existingIds.Length
			&& CountManualPool(daoTu, result, poolKind, preferredAdjacentDaoTu) < targetCount; i++)
		{
			string id = XjXianJiCatalog.NormalizeXianJiId(existingIds[i]);
			if (string.IsNullOrWhiteSpace(id)
				|| XjXianJiCatalog.GetPoolKind(daoTu, id) != poolKind
				|| (poolKind == XjXianJiPoolKind.Adjacent
					&& !string.IsNullOrWhiteSpace(preferredAdjacentDaoTu)
					&& (!XjXianJiCatalog.TryGetAdjacentDaoTuForXianJi(daoTu, id, out string owner)
						|| !string.Equals(owner, preferredAdjacentDaoTu, StringComparison.Ordinal)))
				|| !seen.Add(id))
			{
				continue;
			}
			result.Add(id);
		}
	}

	private static bool FillManualPool(
		string daoTu,
		XjXianJiPoolKind poolKind,
		string preferredAdjacentDaoTu,
		int targetCount,
		long seed,
		List<string> result,
		HashSet<string> seen)
	{
		int guard = 0;
		while (CountManualPool(daoTu, result, poolKind, preferredAdjacentDaoTu) < targetCount
			&& guard++ < XjXianJiState.MaxCount * 4)
		{
			if (!XjXianJiCatalog.TryPickFromPool(
				daoTu,
				poolKind,
				seed + guard * 7919L,
				result.ToArray(),
				preferredAdjacentDaoTu,
				out string id)
				|| string.IsNullOrWhiteSpace(id)
				|| !seen.Add(id))
			{
				return false;
			}
			result.Add(id);
		}
		return CountManualPool(daoTu, result, poolKind, preferredAdjacentDaoTu) >= targetCount;
	}

	private static int CountManualPool(
		string daoTu,
		List<string> ids,
		XjXianJiPoolKind poolKind,
		string preferredAdjacentDaoTu)
	{
		int count = 0;
		for (int i = 0; ids != null && i < ids.Count; i++)
		{
			if (XjXianJiCatalog.GetPoolKind(daoTu, ids[i]) != poolKind) continue;
			if (poolKind == XjXianJiPoolKind.Adjacent
				&& !string.IsNullOrWhiteSpace(preferredAdjacentDaoTu)
				&& (!XjXianJiCatalog.TryGetAdjacentDaoTuForXianJi(daoTu, ids[i], out string owner)
					|| !string.Equals(owner, preferredAdjacentDaoTu, StringComparison.Ordinal)))
			{
				continue;
			}
			count++;
		}
		return count;
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
				&& XjXianJiCatalog.IsAvailableForProgression(daoTu, ordinal, state.Ids, false, false, mappedId))
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
				|| !XjXianJiCatalog.IsAvailableForProgression(daoTu, ordinal, state.Ids, false, false, mappedId))
			{
				continue;
			}

			id = mappedId;
			gongFaName = record.Name;
			return true;
		}

		long actorId = GetActorId(actor);
		int currentYear = GetCurrentYear(actor);
		bool manifested = XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(daoTu);
		bool picked = actor.hasTrait("ChuShen8")
			? XjXianJiCatalog.TryPickDaoZhuForProgression(
				daoTu, ordinal, actorId + currentYear * 131L, state.Ids, manifested, out id)
			: ordinal <= 3
				? XjXianJiCatalog.TryPickUpperForProgression(
					daoTu,
					ordinal,
					actorId + currentYear * 131L,
					state.Ids,
					out id)
				: XjXianJiCatalog.TryPickForProgression(
				daoTu,
				ordinal,
				actorId + currentYear * 131L,
				state.Ids,
				manifested,
				false,
				out id);
		return picked && !string.IsNullOrWhiteSpace(id);
	}

	internal static bool EnsureDebugQiuJinFa(Actor actor, string daoTu, string sourceGongFaName)
	{
		return EnsureQiuJinFaWithoutDomainEvents(actor, daoTu, sourceGongFaName);
	}

	private static bool EnsureQiuJinFaWithoutDomainEvents(
		Actor actor,
		string daoTu,
		string sourceGongFaName,
		bool publishFamilyInheritance = true)
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
			if (publishFamilyInheritance)
			{
				AddManualQiuJinFaToConfirmedFamily(actor, existing);
			}
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
		if (publishFamilyInheritance)
		{
			AddManualQiuJinFaToConfirmedFamily(actor, XjQiuJinFaAccessor.BuildState(actor));
		}
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

	private static bool EnsureManualJinDanState(Actor actor, string daoTu, int currentYear)
	{
		if (actor?.data == null)
		{
			return false;
		}

		daoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		if (XjJinDanAccessor.BuildState(actor).Found)
		{
			ClearManualJinDanPending(actor);
			XjGuoWeiRegistry.ReconcileLiveActor(actor);
			EnsureManualJinDanYiXiang(actor, currentYear);
			return true;
		}

		if (!EnsureManualJinDanXianJiSet(actor, daoTu, currentYear))
		{
			return false;
		}

		XjXianJiState xianJiState = XjXianJiAccessor.BuildState(actor);
		string calculatedType = XjGuoWeiCalculator.Calculate(actor, daoTu, xianJiState);
		string resolvedRunSourceDaoTu = string.Empty;
		string resolvedRunManifestDaoTu = string.Empty;
		if (string.Equals(calculatedType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			&& !XjGuoWeiCalculator.TryResolveIntercalaryIdentity(
				actor, xianJiState, out resolvedRunSourceDaoTu, out resolvedRunManifestDaoTu))
		{
			calculatedType = XjGuoWeiCalculator.NoDoor;
		}
		string[] searchTypes = BuildManualGuoWeiSearchOrder(daoTu, xianJiState, calculatedType);
		long actorId = GetActorId(actor);
		int safeYear = Math.Max(1, currentYear);
		string promotionDaoTitle = XjHighRealmDaoStateService.ResolvePromotionDaoTitle(actor);
		bool everyResolvedTypeOccupied = searchTypes.Length > 0;

		for (int i = 0; i < searchTypes.Length; i++)
		{
			string candidateType = searchTypes[i];
			string candidateDaoTu = string.Equals(candidateType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				? resolvedRunManifestDaoTu
				: XjGuoWeiCalculator.ResolveManifestDaoTu(daoTu, xianJiState, candidateType);
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
			string intercalarySourceDaoTu = string.Equals(candidateType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				? resolvedRunSourceDaoTu
				: string.Empty;
			string sourceDaoTu = string.IsNullOrWhiteSpace(intercalarySourceDaoTu) ? candidateDaoTu : intercalarySourceDaoTu;
			string jinXing = XjHighRealmDaoStateService.BuildPromotionJinXing(
				actor, sourceDaoTu, candidateDaoTu, candidateType, XjJinXingCalculator.Calculate(candidateDaoTu, actorId));
			if (string.IsNullOrWhiteSpace(jinXing)
				|| !XjGuoWeiRegistry.TryClaim(actor, candidateDaoTu, jinXing, guoWei, safeYear, intercalarySourceDaoTu))
			{
				return false;
			}

			bool preserveIntercalaryCultivation = string.Equals(
				candidateType,
				XjGuoWeiCalculator.RunWei,
				StringComparison.Ordinal);
			if (preserveIntercalaryCultivation)
			{
				// 闰位当前修炼道途永远保持根道；candidateDaoTu 是果位显道，
				// 只写入高境身份/Registry，绝不能反写 DaoTu 后再把四根一显倒置。
				XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, sourceDaoTu, false);
			}
			else if (!XjCultivationStateTransitions.TrySetDaoTu(actor, candidateDaoTu, false))
			{
				XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
				return false;
			}

			if (!XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.JinDan, false))
			{
				if (preserveIntercalaryCultivation)
				{
					XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, daoTu, false);
				}
				else
				{
					XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, false);
				}
				XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
				return false;
			}

			XjJinDanAccessor.WriteSuccess(actor, jinXing, guoWei, safeYear);
			if (!XjJinDanAccessor.BuildState(actor).Found)
			{
				XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false);
				if (preserveIntercalaryCultivation)
				{
					XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, daoTu, false);
				}
				else
				{
					XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, false);
				}
				XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
				return false;
			}

			XjHighRealmDaoStateService.InitializeOnPromotion(
				actor, sourceDaoTu, candidateDaoTu, candidateType, guoWei, jinXing, safeYear,
				isFuQi: false, daoTitleOverride: promotionDaoTitle);
			ClearManualJinDanPending(actor);
			XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, candidateDaoTu, guoWei, safeYear);
			EnsureManualJinDanYiXiang(actor, safeYear);
			return true;
		}

		if (everyResolvedTypeOccupied)
		{
			// 显式编辑是一笔一次性事务。对应位序已满时本次命令失败并回滚，
			// 绝不能留下“紫府五神通”再由年度管线暗中等待自动晋升。
			return false;
		}

		// 规则封锁、隐藏果位、数据异常和同年抢位同样只让本次编辑失败。
		return false;
	}

	/// <summary>
	/// 0.9.9.7 以前的手动金丹会把失败编辑登记成年度 pending，并留下紫府五神通。
	/// Phase 6 起手动编辑必须一次性提交；年度管线这里只清理旧档债务，不再替玩家自动晋升。
	/// </summary>
	internal static void CleanupLegacyPendingManualJinDan(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjManualJinDanPending, out int pending)
			|| pending <= 0)
		{
			return;
		}

		ClearManualJinDanPending(actor);
		if (!XjJinDanAccessor.BuildState(actor).Found)
		{
			XjXianJiAccessor.ReconcileRealmLimit(actor);
			XjActorGongFaCollection.ReconcileWithActor(actor, "LegacyManualJinDanPendingCleanup");
			XjActorGongFaCollection.ReconcileGradeCap(actor, "LegacyManualJinDanPendingCleanup");
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
		_ = daoTu;
		_ = xianJiState;
		// 手动补录也必须服从神通结构的唯一果位类型：
		// 五上位只能等本道途果位，余位与闰位同样不能因槽位占用互相降格。
		if (string.Equals(calculatedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			return new[] { XjGuoWeiCalculator.ZhengWei };
		}
		if (string.Equals(calculatedType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			return new[] { XjGuoWeiCalculator.YuWei };
		}
		if (string.Equals(calculatedType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			return new[] { XjGuoWeiCalculator.RunWei };
		}
		return Array.Empty<string>();
	}

	private static void EnsureManualJinDanYiXiang(Actor actor, int currentYear)
	{
		if (actor?.data == null) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang) && yiXiang > 0)
		{
			// 旧调试/手动补录可能只留下兼容投影；借显式导入入口补齐真实道行。
			XjHighRealmDaoStateService.RestoreImportedProgressMetadata(actor, yiXiang, overwriteExisting: false);
			return;
		}

		long actorId = GetActorId(actor);
		int importedProgress = XjDeterministicHash.PositiveIndex(
			actorId + Math.Max(0, currentYear), "manual_jindan_yixiang", 300) + 1;
		XjHighRealmDaoStateService.RestoreImportedProgressMetadata(actor, importedProgress, overwriteExisting: false);
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

		if (string.Equals(traitId, XjRealmIds.DaoTai, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.DaoTai;
			rank = 6;
			return true;
		}

		if (string.Equals(traitId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.ShenDan;
			rank = 5;
			return true;
		}

		if (string.Equals(traitId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.HuangGuan;
			rank = 3;
			return true;
		}

		if (string.Equals(traitId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.FuQiZhenRen;
			rank = 4;
			return true;
		}

		if (string.Equals(traitId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.ZhenJunYuShi;
			rank = 5;
			return true;
		}

		if (string.Equals(traitId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal))
		{
			realmId = XjRealmIds.FuQiDaoTai;
			rank = 6;
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
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)) return 6;
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return 3;
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return 4;
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)) return 5;
		if (string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return 6;
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
		catch (System.Exception xjCaught1816_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjManualRealmTraitReconciliation.cs:1816", xjCaught1816_1);
			
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
