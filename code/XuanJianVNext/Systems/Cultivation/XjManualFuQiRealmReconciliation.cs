using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 手动补录服气境界的权威入口。编辑器显式赋予真君羽士／服气道胎时，
/// 只构造合法的高境位序与载荷；绝不借用自然突破的神丹、重伤、转世或死亡分支。
/// </summary>
internal static class XjManualFuQiRealmReconciliation
{
	internal static void ReconcileGrant(Actor actor, string targetRealmId, string sourceRealmId)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiRealm(targetRealmId))
		{
			return;
		}

		if (XjManualHighRealmGrantPolicy.IsProtectedHighRealmTrait(targetRealmId))
		{
			// 可见高境特质若是权威状态同步写入，重新同步后会自然恢复；
			// 若只是玩家手动塞入，则没有后台高境记录可投影，最终保持移除。
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}

		string sourceRealm = XjRealmHelper.NormalizeId(sourceRealmId);
		if (XjCultivationPathRules.IsZiFuRealm(sourceRealm))
		{
			// 紫金修法不可反向转入服气；撤回刚刚添加的可见特质。
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}

		if (XjCultivationPathRules.TryGetPath(actor, out string existingPath)
			&& string.Equals(existingPath, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal))
		{
			// 仅有手动道途、尚无紫金境界时允许改建为服气；已有紫金境界已在上方拒绝。
			XjCultivationPathTransitions.ClearAll(actor);
		}

		EnsureManualAptitude(actor, targetRealmId);
		if (!EnsureFuQiPath(actor)
			|| !XjFuQiCoreRouter.TryResolveActorCore(actor, out XjFuQiCoreDefinition definition)
			|| !definition.GameplayImplemented)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}

		int currentYear = Math.Max(1, ResolveCurrentYear(actor));
		CompleteCorePrerequisites(actor, definition, currentYear);

		bool written;
		if (string.Equals(targetRealmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			written = XjFuQiStateTransitions.TrySetRealm(
				actor, XjRealmIds.HuangGuan, syncVisibleTraits: false, clearManualRemoved: true);
		}
		else if (string.Equals(targetRealmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal))
		{
			PrepareManualZhenJunCandidate(actor, currentYear);
			bool hasZhenJunPayload = XjJinDanAccessor.BuildState(actor).Found
				&& string.Equals(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter), XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
			if (!hasZhenJunPayload)
			{
				hasZhenJunPayload = XjFuQiCultivationSystem.TryCompleteManualZhenJunStrict(
					actor, currentYear, definition, publishNarrativeEvents: false);
			}
			written = hasZhenJunPayload && XjFuQiStateTransitions.TrySetRealm(
				actor, XjRealmIds.FuQiDaoTai, syncVisibleTraits: false, clearManualRemoved: true);
		}
		else
		{
			written = XjFuQiStateTransitions.TrySetRealm(
				actor, XjRealmIds.FuQiZhenRen, syncVisibleTraits: false, clearManualRemoved: true);
			if (written)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiBodyProjectStartYear, 0);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiBodyProjectCompleteYear, 0);
				if (string.Equals(targetRealmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
				{
					PrepareManualZhenJunCandidate(actor, currentYear);
					written = XjFuQiCultivationSystem.TryCompleteManualZhenJunStrict(
						actor, currentYear, definition, publishNarrativeEvents: false);
				}
				else
				{
					XjFuQiCultivationSystem.EnsureManualPerfectionProject(actor, currentYear, definition);
				}
			}
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string actualRealmId);
		string actualRealm = XjRealmHelper.NormalizeId(actualRealmId);
		bool hasFuQiRealm = XjCultivationPathRules.IsFuQiRealm(actualRealm)
			|| string.Equals(actualRealm, XjRealmIds.ShenDan, StringComparison.Ordinal);
		if (hasFuQiRealm
			&& !string.Equals(definition.DaoTuRootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
		{
			XjDaoTuManifestRegistry.MarkFuQiManifested(
				definition.DaoTuRootId,
				GetActorId(actor),
				currentYear);
		}

		FinalizeManualGrant(actor, currentYear);
	}

	private static void EnsureManualAptitude(Actor actor, string targetRealmId)
	{
		// 四档已存在合法的破格服气路线。编辑器补录最低只抬到四档，
		// 并先补齐该资质对应的命数/道慧基础曲线，避免测试角色仍落在旧低值。
		int minimum = 4;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int stored);
		int normalized = stored >= 1 && stored <= 6 ? stored : 0;
		int target = Math.Max(normalized, minimum);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, target);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(actor, target);
		XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(actor, target);
	}

	private static bool EnsureFuQiPath(Actor actor)
	{
		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			XjFuQiSensingSystem.EnsureEnteredPathSuccess(actor);
			if (XjFuQiCoreRouter.TryResolveActorCore(actor, out _))
			{
				return true;
			}
		}

		if (HasTrait(actor, "XjLongGengDaoTong")
			|| XjFuQiSwordWorldState.IsEstablished && HasTrait(actor, "XjLongGengDaoTong"))
		{
			return XjCultivationPathTransitions.TrySetInitialPath(
				actor,
				XjCultivationPathIds.FuQiYangXing,
				string.Empty,
				XjFuQiLineageIds.Sword,
				syncVisibleTraits: false);
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string storedDaoTu)
			&& TryEnterDaoTuPath(actor, storedDaoTu))
		{
			return true;
		}

		string[] traitIds = XjDaoTuVisibleTraitCatalog.AllTraitIds;
		for (int i = 0; i < traitIds.Length; i++)
		{
			string traitId = traitIds[i];
			if (!string.IsNullOrWhiteSpace(traitId)
				&& HasTrait(actor, traitId)
				&& XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out string traitDaoTu)
				&& TryEnterDaoTuPath(actor, traitDaoTu))
			{
				return true;
			}
		}

		return TryPickFuQiDaoTu(actor, out string pickedDaoTu)
			&& TryEnterDaoTuPath(actor, pickedDaoTu);
	}

	private static bool TryEnterDaoTuPath(Actor actor, string daoTu)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		return normalized.Length > 0
			&& XjDaoTuCatalog.TryResolve(normalized, out XjDaoTuDefinition definition)
			&& definition.SupportsFuQi
			&& XjDaoTuManifestRegistry.CanManifestFuQi(definition.RootId)
			&& XjFuQiCoreCatalog.TryGetByRootId(definition.RootId, out XjFuQiCoreDefinition core)
			&& core.GameplayImplemented
			&& !string.Equals(definition.RootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal)
			&& XjCultivationPathTransitions.TrySetInitialPath(
				actor,
				XjCultivationPathIds.FuQiYangXing,
				normalized,
				string.Empty,
				syncVisibleTraits: false);
	}

	private static bool TryPickFuQiDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		IReadOnlyList<XjDaoTuVisibleTraitEntry> entries = XjDaoTuVisibleTraitCatalog.Entries;
		if (entries == null || entries.Count == 0)
		{
			return false;
		}

		List<string> candidates = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < entries.Count; i++)
		{
			string displayName = (entries[i].DisplayName ?? string.Empty).Trim();
			if (displayName.Length == 0
				|| !seen.Add(displayName)
				|| !XjDaoTuCatalog.TryResolve(displayName, out XjDaoTuDefinition definition)
				|| !definition.SupportsFuQi
				|| !XjDaoTuManifestRegistry.CanManifestFuQi(definition.RootId)
				|| string.Equals(definition.RootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal)
				|| !XjFuQiCoreCatalog.TryGetByRootId(definition.RootId, out XjFuQiCoreDefinition core)
				|| !core.GameplayImplemented)
			{
				continue;
			}
			candidates.Add(displayName);
		}

		if (candidates.Count == 0)
		{
			return false;
		}
		int index = XjDeterministicHash.PositiveIndex(GetActorId(actor), "manual_fuqi_daotu", candidates.Count);
		daoTu = candidates[index];
		return true;
	}

	private static void CompleteCorePrerequisites(
		Actor actor,
		in XjFuQiCoreDefinition definition,
		int currentYear)
	{
		XjFuQiSensingSystem.MarkPathEntrySuccess(actor, definition.DaoTuRootId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 10000);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiShenMiaoId, definition.CoreId);

		if (string.Equals(definition.DaoTuRootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSwordQi, 128);
			XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiShenMiaoId, XjFuQiSwordDaoSystem.YangQingMingId);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiYangQingMingCompletedYear, currentYear);
		}
	}

	private static void PrepareManualZhenJunCandidate(Actor actor, int currentYear)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiPerfectionProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, currentYear);
		// 编辑器高境补录只准备确定性的真君候选状态；后续由 Strict 入口
		// 申请合法果/余/闰位，不进入任何自然求证失败或神丹分流。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5HighRealmRouteChecked, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5StayFuQi, 1);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		// 编辑器显式授予真君属于人工覆盖；四档在这里直接放行候选资格，
		// 自然角色仍只能通过首次感气时固化的高命数+高道慧资格。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank4EligibilityChecked, aptitude == 4 ? 1 : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank4ZhenJunEligible, aptitude == 4 ? 1 : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingReady, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanLastAttemptYear, 0);
	}

	private static void FinalizeManualGrant(Actor actor, int currentYear)
	{
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		XjCultivationStateTransitions.RestoreHealthAfterRealmPromotion(actor);
		XjCultivationSeed.EnsureSeedState(actor);
		XjCultivatorCache.CheckAndUpdate(actor);
		XjFuQiCandidateIndex.Observe(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string actualRealm);
		actualRealm = XjRealmHelper.NormalizeId(actualRealm);
		if (string.Equals(actualRealm, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| string.Equals(actualRealm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(actualRealm, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
			|| string.Equals(actualRealm, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
			XjBloodlineRefreshLane.Request(actor, currentYear);
		}
		if (!string.IsNullOrWhiteSpace(actualRealm))
		{
			if (string.Equals(actualRealm, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
				|| string.Equals(actualRealm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
				|| string.Equals(actualRealm, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal))
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
				if (string.Equals(actualRealm, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal))
				{
					long actorId = GetActorId(actor);
					bool firstDaoTaiRegistration = actorId > 0L && !XjDaoTaiPresenceArchive.IsRegisteredDaoTai(actorId);
					XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
						actor,
						actualRealm,
						currentYear,
						applyMingShuReward: false,
						syncVisibleTraits: false);
					XjRealmTitleApplyService.ApplyOnDaoTaiAscension(actor, daoTu);
					XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
					XjDaoTaiGongFaService.EnsureGradeSeven(actor, currentYear);
					XjDaoTaiPresenceArchive.ObserveLiveDaoTai(actor, currentYear);
					if (firstDaoTaiRegistration)
					{
						string guoWei = XjDaoTaiMeritSystem.ResolveGuoWei(actor);
						XjDaoTaiAscensionSystem.BroadcastDaoTaiAscension(actor, daoTu, guoWei, currentYear);
					}
				}
				else
				{
					XjRealmTitleApplyService.ApplyOnPromotion(actor, actualRealm, daoTu);
				}
			}
			XjAutoCollectSystem.TryCollectRealm(actor, actualRealm, "ManualFuQiRealmGrant");
		}
		XjManualCultivationWake.EnsureAwake(actor);
	}

	private static bool HasTrait(Actor actor, string traitId)
	{
		try
		{
			return actor?.data != null && actor.hasTrait(traitId);
		}
		catch (System.Exception xjCaught282_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjManualFuQiRealmReconciliation.cs:282", xjCaught282_1);
			
			return false;
		}
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static int ResolveCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}
}
