using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.WeaponArt;

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
		bool clearJinDanIdentity = !KeepsJinDanIdentity(realmId)
			|| (string.Equals(previousRealmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
				&& string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal));
		XjActorAccessor.SetString(actor, XjActorDataKeys.RealmId, realmId);
		if (realmChanged)
		{
			// 先切断旧境界瓶颈储备，再写入新境界年份。
			// 否则旧档或特殊资质可能把炼气/筑基蓄积真元无上限释放到下一境界。
			XjBottleneckEventSystem.ResetForRealmTransition(actor, previousRealmId, realmId);
			int enteredYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmEnteredYear, Math.Max(0, enteredYear));
		}
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuEnteredYear, out int storedZiFuYear) || storedZiFuYear <= 0))
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
		if (IsBelowZiFu(realmId))
		{
			XjQiuJinFaAccessor.Clear(actor, "RealmBelowZiFu");
		}
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		XjCultivatorCache.CheckAndUpdate(actor);
		XjWeaponArtSystem.OnRealmChanged(actor);
		XjHighRealmMovement.ReconcileFlightStateAfterRealmWrite(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjCultivationSeed.RefreshChuShenForCultivationState(actor);
		return true;
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
		if (actor?.data == null || !RequiresDaoTu(realmId))
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
		catch
		{
		}
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
		string normalized = (daoTu ?? string.Empty).Trim();
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(normalized)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalized, out _))
		{
			return false;
		}
		if (string.Equals(normalized, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
			&& !XjQingXuanKongZhengSystem.CanEnterQingXuan(actor))
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
		return true;
	}

	internal static bool TrySetDaoTuMetadataOnly(Actor actor, string daoTu, bool syncVisibleTraits)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(normalized)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalized, out _))
		{
			return false;
		}
		if (string.Equals(normalized, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
			&& !XjQingXuanKongZhengSystem.CanEnterQingXuan(actor))
		{
			return false;
		}

		RestoreDaoTuMetadataOnly(actor, normalized, syncVisibleTraits);
		return true;
	}

	internal static void RestoreDaoTuMetadataOnly(Actor actor, string daoTu, bool syncVisibleTraits)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, (daoTu ?? string.Empty).Trim());
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
	}

	internal static bool ClearDaoTu(Actor actor, bool syncVisibleTraits)
	{
		if (actor?.data == null)
		{
			return false;
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
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	private static bool KeepsJinDanIdentity(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	private static bool RequiresDaoTu(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	private static bool IsBelowZiFu(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal);
	}
}
