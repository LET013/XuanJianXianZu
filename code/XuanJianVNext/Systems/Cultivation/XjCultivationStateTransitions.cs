using System;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.YaoShu;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// Owns writes to the authoritative cultivation identity fields.
/// Trait synchronization is a projection of these fields and must not be
/// interpreted as an external/manual trait edit.
/// </summary>
internal static class XjCultivationStateTransitions
{
	private static int _visibleTraitSyncDepth;

	internal static bool IsVisibleTraitSyncActive => _visibleTraitSyncDepth > 0;

	internal static void EnterVisibleTraitSync()
	{
		_visibleTraitSyncDepth++;
	}

	internal static void ExitVisibleTraitSync()
	{
		if (_visibleTraitSyncDepth > 0)
		{
			_visibleTraitSyncDepth--;
		}
	}

	internal static bool TrySetRealm(Actor actor, string realmId, bool syncVisibleTraits, bool clearManualRemoved = true)
	{
		if (actor?.data == null
			|| !IsKnownRealm(realmId))
		{
			return false;
		}

		// 帝明阳与神丹双向互斥，但该门禁只检查当前角色本人。
		// 绝不能因为当世存在帝明阳，就阻断其他紫金/服气修士成就神丹。
		if (string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ShenDan, StringComparison.Ordinal)
			&& XjXianGuoSystem.IsDiMingYang(actor))
		{
			return false;
		}

		// 神丹是共用的金丹结果，不属于某一种修法的独占境界。
		// 服气修士进入神丹时保持 fuqi_yangxing 身份，不能在这里改写为紫府金丹。
		if (string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ShenDan, StringComparison.Ordinal)
			&& XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return XjFuQiStateTransitions.TrySetRealm(actor, realmId, syncVisibleTraits, clearManualRemoved);
		}
		if (XjCultivationPathRules.IsFuQiRealm(realmId))
		{
			return XjFuQiStateTransitions.TrySetRealm(actor, realmId, syncVisibleTraits, clearManualRemoved);
		}
		if (!XjCultivationPathRules.IsZiFuRealm(realmId)
			|| !XjCultivationPathTransitions.TryEnsureZiFuJinDan(actor, syncVisibleTraits: false))
		{
			return false;
		}

		if (XjCaiQiActorAccessor.IsLianQiByZaQi(actor)
			&& !string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return false;
		}

		if (RequiresDaoTu(realmId) && !EnsureDaoTuForRealm(actor, realmId, false))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string previousRealmId);
		bool realmChanged = !string.Equals(
			XjRealmHelper.NormalizeId(previousRealmId),
			XjRealmHelper.NormalizeId(realmId),
			StringComparison.Ordinal);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenDanGuoWei, out string previousShenDanGuoWei);
		bool leavingShenDan = !string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			&& (string.Equals(previousRealmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
				|| !string.IsNullOrWhiteSpace(previousShenDanGuoWei));
		if (IsProtectedHighRealmIdentity(actor)
			&& IsLowerRealmRewrite(previousRealmId, realmId))
		{
			return false;
		}
		bool clearJinDanIdentity = !KeepsJinDanIdentity(realmId)
			|| (string.Equals(previousRealmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
				&& string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal));
		XjActorAccessor.SetString(actor, XjActorDataKeys.RealmId, realmId);
		int transitionYear = 0;
		if (realmChanged)
		{
			// 先切断旧境界瓶颈储备，再写入新境界年份。
			// 否则旧档或特殊资质可能把炼气/筑基蓄积真元无上限释放到下一境界。
			XjBottleneckEventSystem.ResetForRealmTransition(actor, previousRealmId, realmId);
			transitionYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmEnteredYear, Math.Max(0, transitionYear));
			int previousTier = XjRealmSuppression.GetRealmTierFromIdForRuntime(previousRealmId);
			int nextTier = XjRealmSuppression.GetRealmTierFromIdForRuntime(realmId);
			if (nextTier > previousTier && nextTier >= XjRealmSuppression.TierZiFu)
			{
				XjVanillaDeathGuard.MarkRealmPromotionGrace(actor);
			}
			else if (nextTier < XjRealmSuppression.TierZiFu)
			{
				XjVanillaDeathGuard.ClearRealmPromotionGrace(actor);
			}
			if (previousTier > XjRealmSuppression.TierNone && nextTier <= previousTier)
			{
				// Downward/manual rewrites create an interval history that the compact
				// threshold model cannot reconstruct safely. Drop only the ambiguous
				// secondary debt at this boundary rather than applying future or former
				// realm production to the wrong years.
				XjScheduler.BaselineSecondaryAfterAmbiguousRealmChange(actor, transitionYear);
			}
		}
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			&& (realmChanged
				|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhuJiEnteredYear, out int storedZhuJiYear)
				|| storedZhuJiYear <= 0))
		{
			int zhuJiYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			if (!realmChanged
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int existingRealmYear)
				&& existingRealmYear > 0)
			{
				zhuJiYear = existingRealmYear;
			}
			if (zhuJiYear > 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZhuJiEnteredYear, zhuJiYear);
		}
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& (realmChanged
				|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuEnteredYear, out int storedZiFuYear)
				|| storedZiFuYear <= 0))
		{
			int ziFuYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			if (!realmChanged
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int existingRealmYear)
				&& existingRealmYear > 0)
			{
				ziFuYear = existingRealmYear;
			}
			if (ziFuYear > 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuEnteredYear, ziFuYear);
		}
		if (leavingShenDan)
		{
			XjShenDanAccessor.ClearSuccess(actor);
		}
		if (clearJinDanIdentity)
		{
			XjJinDanAccessor.ClearSuccess(actor);
		}
		if (clearManualRemoved)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmManualRemoved, 0);
		}
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu))
		{
			int currentYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			XjZiFuProgression.EnsureZhuJiFoundationXianJi(actor, daoTu, currentYear);
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string gongFaDaoTu)
			&& !string.IsNullOrWhiteSpace(gongFaDaoTu))
		{
			XjGongFaProgression.EnsureRealmMinimumGrade(actor, realmId, gongFaDaoTu);
		}
		if (realmChanged && string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			int ziFuEnteredYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastYear, Math.Max(0, ziFuEnteredYear));
			XjXianJiAccessor.Forget(actor);
		}
		XjXianJiAccessor.ReconcileRealmLimit(actor);
		XjActorGongFaCollection.ReconcileWithActor(actor, "RealmTransition");
		XjActorGongFaCollection.ReconcileGradeCap(actor, "RealmTransitionGradeCap");
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			// 只在本次真实筑基写入时处理，不对世界角色做额外扫描。
			XjYaoShuHalfBloodlineSystem.TryAwakenYaoIntentAtZhuJi(actor);
			XjXianGuoSystem.ObserveZhuJiIntentEligibility(actor,
				XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
		}
		if (IsBelowZiFu(realmId))
		{
			XjQiuJinFaAccessor.Clear(actor, "RealmBelowZiFu");
		}
		// 原生伴生特质属于境界事实的直接结果，不应依赖调用者是否选择刷新 UI 投影。
		// 突破事务在这里立即补齐；年度/读档 SyncCultivationTraits 仍保留为幂等自愈。
		XjVisibleTraitSync.EnsureRealmNativeTraits(actor, realmId);
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		XjCultivatorCache.CheckAndUpdate(actor);
		XjWeaponArtSystem.OnRealmChanged(actor);
		XjHighRealmMovement.ReconcileFlightStateAfterRealmWrite(actor);
		XjCombatHotPathCache.Refresh(actor);
		if (realmChanged)
		{
			// 境界写入必须立即使原生属性失效。年度寿尽回查会先刷新寿元，
			// 不能让新晋紫府继续携带筑基寿元进入下一次死亡判定。
			try { actor.clearTraitCache(); actor.setStatsDirty(); } catch (System.Exception xjCaught189) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjCultivationStateTransitions.cs:189", xjCaught189); }
		}
		XjCultivationSeed.RefreshChuShenForCultivationState(actor);
		if (realmChanged)
		{
			XjStageZeroObservation.RecordRealmTransition(actor, previousRealmId, realmId);
			XjSemanticDiagnostics.RecordRealmTransition(actor, previousRealmId, realmId, transitionYear);
		}
		return true;
	}

	internal static int ReadZhuJiEnteredYear(Actor actor)
	{
		if (actor?.data == null) return 0;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhuJiEnteredYear, out int storedYear) && storedYear > 0)
		{
			return storedYear;
		}

		// Legacy saves can only recover this threshold while the actor is still
		// ZhuJi. Stage6 does not replay pre-upgrade secondary years, so higher-realm
		// legacy actors safely establish this history on their next real transition.
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZhuJi, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int realmYear)
			&& realmYear > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZhuJiEnteredYear, realmYear);
			return realmYear;
		}
		return 0;
	}

	internal static int ReadZiFuEnteredYear(Actor actor)
	{
		if (actor?.data == null) return 0;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuEnteredYear, out int storedYear) && storedYear > 0)
		{
			return storedYear;
		}

		// 旧档只有“当前境界进入年份”。该值仅在角色当前仍为紫府时
		// 才能安全视作紫府年份；金丹以上角色的该字段已代表更高境界。
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int realmYear)
			&& realmYear > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuEnteredYear, realmYear);
			return realmYear;
		}
		return 0;
	}

	internal static bool EnsureDaoTuForRealm(Actor actor, string realmId, bool syncVisibleTraits)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !RequiresDaoTu(realmId))
		{
			return false;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string existing)
			&& !string.IsNullOrWhiteSpace(existing)
			&& XjDaoTuVisibleTraitCatalog.TryResolveTraitId(existing, out _))
		{
			return true;
		}

		if (XjIdentityTraitLifecycle.TryReconcileDaoTuFromVisibleTraits(actor)
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string visibleDaoTu)
			&& !string.IsNullOrWhiteSpace(visibleDaoTu))
		{
			if (syncVisibleTraits)
			{
				XjVisibleTraitSync.SyncCultivationTraits(actor);
			}
			return true;
		}

		bool ensured = XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out string familyDaoTu)
			&& !string.IsNullOrWhiteSpace(familyDaoTu);
		if (ensured && syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		return ensured;
	}

	internal static void RestoreHealthAfterRealmPromotion(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		try
		{
			actor.clearTraitCache();
			actor.setStatsDirty();
			actor.updateStats();
			float maxHealth = ((BaseSimObject)actor).getMaxHealth();
			int restoreAmount = maxHealth >= int.MaxValue
				? int.MaxValue
				: Math.Max(1, (int)Math.Ceiling(maxHealth));
			actor.restoreHealth(restoreAmount);
		}
		catch (System.Exception xjCaught297) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjCultivationStateTransitions.cs:297", xjCaught297); }
	}

	/// <summary>
	/// Corruption-recovery write for an already-established cultivation realm.
	/// This deliberately bypasses breakthrough gates, but still closes every cache/projection
	/// dependency that a raw RealmId write used to leave stale. Normal gameplay must use TrySetRealm.
	/// </summary>
	internal static bool RestoreRealmMetadataOnlyForRecovery(Actor actor, string realmId, string source)
	{
		if (actor?.data == null) return false;
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (!IsKnownRealm(normalized)) return false;

		XjActorAccessor.SetString(actor, XjActorDataKeys.RealmId, normalized);
		XjVisibleTraitSync.EnsureRealmNativeTraits(actor, normalized);
		XjXianJiAccessor.ReconcileRealmLimit(actor);
		string reason = string.IsNullOrWhiteSpace(source) ? "RealmRecovery" : "RealmRecovery:" + source.Trim();
		XjActorGongFaCollection.ReconcileWithActor(actor, reason);
		XjActorGongFaCollection.ReconcileGradeCap(actor, reason + ":GradeCap");
		XjCultivatorCache.CheckAndUpdate(actor);
		XjHighRealmMovement.ReconcileFlightStateAfterRealmWrite(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjCultivationSeed.RefreshChuShenForCultivationState(actor);
		try { actor.clearTraitCache(); actor.setStatsDirty(); }
		catch (System.Exception ex) { XjExceptionDiagnostics.Report("XjCultivationStateTransitions.RestoreRealmMetadataOnlyForRecovery", ex); }
		return true;
	}

	/// <summary>
	/// Restores a complete realm metadata snapshot after an explicit editor transaction aborts.
	/// The caller never writes RealmId/realm-year fields directly; rollback remains inside the
	/// same cultivation authority boundary as normal realm transitions.
	/// </summary>
	internal static bool RestoreRealmSnapshotForRecovery(
		Actor actor,
		string realmId,
		int realmEnteredYear,
		int zhuJiEnteredYear,
		int ziFuEnteredYear,
		string source)
	{
		if (actor?.data == null) return false;

		string normalized = XjRealmHelper.NormalizeId(realmId);
		bool restored;
		if (string.IsNullOrWhiteSpace(normalized))
		{
			restored = ClearRealm(actor, false, false);
		}
		else
		{
			restored = RestoreRealmMetadataOnlyForRecovery(actor, normalized, source);
		}
		if (!restored) return false;

		XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmEnteredYear, Math.Max(0, realmEnteredYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZhuJiEnteredYear, Math.Max(0, zhuJiEnteredYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuEnteredYear, Math.Max(0, ziFuEnteredYear));
		return true;
	}

	/// <summary>
	/// Narrow authority-clear boundary used by conversion/clone cleanup. It owns the raw RealmId/DaoTu
	/// reset so Shi, mirror and projection systems cannot each invent their own partial clear sequence.
	/// retainedRealmId may only be empty or a known realm (currently ZaQi cleanup retains LianQi).
	/// </summary>
	internal static bool ResetIdentityMetadataForAuthorityClear(Actor actor, string retainedRealmId)
	{
		if (actor?.data == null) return false;
		string normalized = string.IsNullOrWhiteSpace(retainedRealmId)
			? string.Empty
			: XjRealmHelper.NormalizeId(retainedRealmId);
		if (normalized.Length > 0 && !IsKnownRealm(normalized)) return false;

		XjActorAccessor.SetString(actor, XjActorDataKeys.RealmId, normalized);
		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, string.Empty);
		if (normalized.Length == 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmEnteredYear, 0);
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZhuJiEnteredYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuEnteredYear, 0);
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		XjCultivatorCache.CheckAndUpdate(actor);
		XjHighRealmMovement.ReconcileFlightStateAfterRealmWrite(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		return true;
	}

	internal static bool ClearRealm(Actor actor, bool syncVisibleTraits, bool markManualRemoved)
	{
		if (actor?.data == null || XjCaiQiActorAccessor.IsLianQiByZaQi(actor))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string previousRealmId);
		XjBottleneckEventSystem.ResetForRealmTransition(actor, previousRealmId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.RealmId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmEnteredYear, 0);
		XjVanillaDeathGuard.ClearRealmPromotionGrace(actor);
		XjShenDanAccessor.ClearSuccess(actor);
		XjJinDanAccessor.ClearSuccess(actor);
		XjXianJiAccessor.ReconcileRealmLimit(actor);
		XjActorGongFaCollection.ReconcileWithActor(actor, "RealmCleared");
		XjActorGongFaCollection.ReconcileGradeCap(actor, "RealmClearedGradeCap");
		XjQiuJinFaAccessor.Clear(actor, "RealmCleared");
		if (markManualRemoved)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmManualRemoved, 1);
		}
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		XjCultivatorCache.CheckAndUpdate(actor);
		XjHighRealmMovement.ReconcileFlightStateAfterRealmWrite(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjCultivationSeed.RefreshChuShenForCultivationState(actor);
		return true;
	}

	internal static bool TrySetDaoTu(Actor actor, string daoTu, bool syncVisibleTraits)
	{
		return XjDaoTuReplacementTransaction.ReplaceDaoTu(actor, daoTu, syncVisibleTraits);
	}

	// 只由XjDaoTuReplacementTransaction调用。紫府金丹内部功法、仙基和求金
	// 数据仍保持原状态机，不能被跨修法身份层直接复用。
	internal static bool TrySetZiJinDaoTuCore(Actor actor, string daoTu, bool syncVisibleTraits)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		// 帝明阳的“帝”是对明阳道统的不可逆帝统化，不是一个可以再被果位迁移、
		// 旧档修复或手动换道改成其他源道途的显示标签。只要帝明阳身份仍在，
		// 权威 DaoTu 永远钉在明阳；失去王位/尊卑法统只影响仙朝，不改修炼源道。
		if (actor?.data != null && (XjSongXuanEasterEggSystem.IsSongXuan(actor) || XjZhangYanEasterEggSystem.IsZhangYan(actor)))
		{
			normalized = XjSongXuanEasterEggSystem.IsSongXuan(actor) ? XjSongXuanEasterEggSystem.DaoTu : XjZhangYanEasterEggSystem.DaoTu;
		}
		else if (actor?.data != null && XjXianGuoSystem.IsDiMingYang(actor))
		{
			normalized = XjXianGuoSystem.MingYangDaoTu;
		}
		bool favoredLocked = actor?.data != null
			&& !XjXianGuoSystem.IsDiMingYang(actor)
			&& XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out string lockedDaoTu)
			&& string.Equals(normalized, lockedDaoTu, StringComparison.Ordinal);
		if (actor?.data == null
			|| !XjCultivationPathTransitions.TryEnsureZiFuJinDan(actor, syncVisibleTraits: false)
			|| string.IsNullOrWhiteSpace(normalized)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalized, out _))
		{
			return false;
		}
		if (string.Equals(normalized, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal)
			&& !XjDaoTuManifestRegistry.CanEnterLaterFoundedPath(XjDaoTuRootIds.YuanZhao))
		{
			return false;
		}
		if (!favoredLocked
			&& string.Equals(normalized, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
			&& !XjQingXuanKongZhengSystem.CanEnterQingXuan(actor))
		{
			return false;
		}
		if (!favoredLocked
			&& string.Equals(normalized, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
			&& !XjZiJinSwordDaoSystem.CanEnterLongGengDaoTu(actor))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string previousDaoTu);
		bool hadGongFaBefore = XjGongFaAccessor.BuildState(actor).Found;
		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, normalized);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude) && aptitude > 0)
		{
			XjGongFaProgression.EnsureEntryGongFa(actor, XjActorCultivationSnapshotBuilder.Build(actor));
			XjGongFaProgression.ReconcileDaoTu(actor, normalized, "道途确立");
			if (!XjActorGongFaCollection.IsConsistentWithDaoTu(actor, normalized))
			{
				// 道途与真实功法必须作为同一身份事务提交。若集合拒绝写入，
				// 恢复原道途，避免人物栏显示新道途而五部功法仍属于旧道途。
				XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, previousDaoTu ?? string.Empty);
				if (!hadGongFaBefore)
				{
					XjGongFaAccessor.Clear(actor);
				}
				else if (!string.IsNullOrWhiteSpace(previousDaoTu))
				{
					XjGongFaProgression.ReconcileDaoTu(actor, previousDaoTu, "道途写入回滚");
				}
				else
				{
					XjActorGongFaCollection.ClearDaoTuMetadata(actor, "道途写入回滚");
				}
				return false;
			}
		}
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		// DaoTu is now materialized in the combat hot profile. Refresh at the
		// authoritative write boundary so counter multipliers cannot use a stale
		// path after manual assignment or a breakthrough transition.
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		XjXianGuoSystem.ObserveDaoTu(actor, normalized,
			Math.Max(1, XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor)));
		return true;
	}

	internal static bool TrySetDaoTuMetadataOnly(Actor actor, string daoTu, bool syncVisibleTraits)
	{
		if (actor?.data == null)
		{
			return false;
		}
		bool songXuanLocked = XjSongXuanEasterEggSystem.IsSongXuan(actor);
		bool zhangYanLocked = XjZhangYanEasterEggSystem.IsZhangYan(actor);
		string normalized;
		bool favoredLocked;
		if (songXuanLocked || zhangYanLocked)
		{
			normalized = songXuanLocked ? XjSongXuanEasterEggSystem.DaoTu : XjZhangYanEasterEggSystem.DaoTu;
			favoredLocked = false;
		}
		else
		{
			// 非彩蛋角色完全沿用原有果位钟爱解析语义，避免为了宋玄改变其它角色的换道边界。
			normalized = XjGuoWeiFavoredDaoTuLock.ResolveRequestedDaoTu(actor, daoTu, out favoredLocked);
		}
		if (!XjCultivationPathTransitions.TryEnsureZiFuJinDan(actor, syncVisibleTraits: false)
			|| string.IsNullOrWhiteSpace(normalized)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalized, out _))
		{
			return false;
		}
		if (string.Equals(normalized, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal)
			&& !XjDaoTuManifestRegistry.CanEnterLaterFoundedPath(XjDaoTuRootIds.YuanZhao))
		{
			return false;
		}
		if (!favoredLocked
			&& string.Equals(normalized, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
			&& !XjQingXuanKongZhengSystem.CanEnterQingXuan(actor))
		{
			return false;
		}
		if (!favoredLocked
			&& string.Equals(normalized, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
			&& !XjZiJinSwordDaoSystem.CanEnterLongGengDaoTu(actor))
		{
			return false;
		}

		RestoreDaoTuMetadataOnly(actor, normalized, syncVisibleTraits);
		return true;
	}

	internal static void RestoreDaoTuMetadataOnly(Actor actor, string daoTu, bool syncVisibleTraits)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsZiFuJinDan(actor))
		{
			return;
		}

		string resolvedDaoTu = XjSongXuanEasterEggSystem.IsSongXuan(actor)
			? XjSongXuanEasterEggSystem.DaoTu
			: XjZhangYanEasterEggSystem.IsZhangYan(actor)
				? XjZhangYanEasterEggSystem.DaoTu
			: XjXianGuoSystem.IsDiMingYang(actor)
				? XjXianGuoSystem.MingYangDaoTu
				: XjGuoWeiFavoredDaoTuLock.ResolveRequestedDaoTu(actor, daoTu, out _);
		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, resolvedDaoTu);
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		XjXianGuoSystem.ObserveDaoTu(actor, resolvedDaoTu,
			Math.Max(1, XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor)));
	}

	internal static bool ClearDaoTu(Actor actor, bool syncVisibleTraits)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (XjSongXuanEasterEggSystem.IsSongXuan(actor))
		{
			return TrySetDaoTu(actor, XjSongXuanEasterEggSystem.DaoTu, syncVisibleTraits);
		}
		if (XjZhangYanEasterEggSystem.IsZhangYan(actor))
		{
			return TrySetDaoTu(actor, XjZhangYanEasterEggSystem.DaoTu, syncVisibleTraits);
		}
		if (XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out string lockedDaoTu))
		{
			return TrySetDaoTu(actor, lockedDaoTu, syncVisibleTraits);
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, string.Empty);
		XjActorGongFaCollection.ClearDaoTuMetadata(actor, "DaoTuCleared");
		XjQiuJinFaAccessor.Clear(actor, "DaoTuCleared");
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		return true;
	}

	internal static bool IsKnownRealm(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	private static bool KeepsJinDanIdentity(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	private static bool RequiresDaoTu(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	private static bool IsBelowZiFu(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal);
	}

	private static bool IsProtectedHighRealmIdentity(Actor actor)
	{
		return XjJinDanAccessor.BuildState(actor).Found
			|| XjShenDanAccessor.BuildState(actor).Found
			|| XjXuanJianShenTongSpecials.IsJieLinXian(actor)
			|| XjXuanJianShenTongSpecials.IsYuYiXian(actor)
			|| XjDaoTaiSpellScale.IsDaoTaiActor(actor);
	}

	private static bool IsLowerRealmRewrite(string previousRealmId, string nextRealmId)
	{
		int previousTier = XjRealmSuppression.GetRealmTierFromIdForRuntime(previousRealmId);
		int nextTier = XjRealmSuppression.GetRealmTierFromIdForRuntime(nextRealmId);
		return previousTier >= XjRealmSuppression.TierJinDan && nextTier > 0 && nextTier < previousTier;
	}
}
