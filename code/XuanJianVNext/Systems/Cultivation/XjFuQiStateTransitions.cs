using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 服气三境的权威境界写入口。这里只处理境界事实、可见特质与通用缓存，
/// 并同步服气自身唯一的一部本命功法；不触发采气、仙基、神通或求金法。
/// </summary>
internal static class XjFuQiStateTransitions
{
	internal static bool TrySetDaoTuMetadataOnly(Actor actor, string daoTu, bool syncVisibleTraits)
	{
		bool imperialLocked = actor?.data != null && XjXianGuoSystem.IsDiMingYang(actor);
		string normalized = imperialLocked
			? XjXianGuoSystem.MingYangDaoTu
			: (daoTu ?? string.Empty).Trim();
		if (actor?.data == null
			|| !XjCultivationPathRules.IsFuQiYangXing(actor)
			|| string.IsNullOrWhiteSpace(normalized)
			|| !XjDaoTuCatalog.SupportsFuQi(normalized)
			|| !XjDaoTuCatalog.TryResolveRootId(normalized, out string targetRootId))
		{
			return false;
		}

		string sourceRootId = string.Empty;
		if (imperialLocked)
		{
			// 旧档若连服气根道元数据也漂移，帝明阳以明阳根道重新收口；
			// 不允许旧的果位显道/兼容迁移继续把帝统身份钉在外道根上。
			sourceRootId = targetRootId;
		}
		else
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiDaoTuRootId, out sourceRootId);
			if (string.IsNullOrWhiteSpace(sourceRootId)
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string sourceDaoTu))
			{
				XjDaoTuCatalog.TryResolveRootId(sourceDaoTu, out sourceRootId);
			}
		}
		if (string.IsNullOrWhiteSpace(sourceRootId)
			|| !string.Equals(sourceRootId, targetRootId, StringComparison.Ordinal)
			|| !XjFuQiCoreCatalog.TryGetByRootId(sourceRootId, out XjFuQiCoreDefinition rootDefinition))
		{
			return false;
		}

		XjFuQiCoreDefinition specialized = XjFuQiCoreCatalog.SpecializeForDaoTu(in rootDefinition, normalized);
		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, normalized);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiDaoTuRootId, specialized.DaoTuRootId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCoreType, specialized.CoreTypeId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCoreId, specialized.CoreId);
		long actorId = ((BaseSystemData)actor.data).id;
		XjFuQiMethodRules.ForgetRuntimeCache(actorId);
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		XjCultivatorCache.CheckAndUpdate(actor);
		XjFuQiCandidateIndex.Observe(actor);
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		try
		{
			actor.clearTraitCache();
			actor.setStatsDirty();
			actor.updateStats();
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report(
				"code/XuanJianVNext/Systems/Cultivation/XjFuQiStateTransitions.cs:TrySetDaoTuMetadataOnly",
				ex);
		}
		return true;
	}

	internal static bool TrySetRealm(Actor actor, string realmId, bool syncVisibleTraits, bool clearManualRemoved)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		bool sharedShenDanTarget = string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal);
		if (actor?.data == null || (!XjCultivationPathRules.IsFuQiRealm(normalized) && !sharedShenDanTarget))
		{
			return false;
		}
		// 帝明阳本人绝不进入共享神丹；限制是 actor-local，不读取“世界是否已有帝明阳”。
		if (sharedShenDanTarget && XjXianGuoSystem.IsDiMingYang(actor)) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if ((string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal)
				&& !XjFuQiAptitudeRules.CanReachHuangGuan(aptitude))
			|| (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
				&& !XjFuQiAptitudeRules.CanReachZhenRen(aptitude))
			|| ((string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal) || sharedShenDanTarget)
				&& !XjFuQiAptitudeRules.CanAttemptZhenJunYuShi(actor)))
		{
			return false;
		}
		// 先验证旧境界，再写修炼体系；不能在发现紫金旧境界后才回退，
		// 否则会留下“路径已改、境界未改”的半提交状态。
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string previousRealmId);
		string previousNormalized = XjRealmHelper.NormalizeId(previousRealmId);
		if (!string.IsNullOrWhiteSpace(previousNormalized)
			&& !XjCultivationPathRules.IsFuQiRealm(previousNormalized)
			&& !string.Equals(previousNormalized, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return false;
		}
		if (IsProtectedFuQiHighRealmIdentity(actor)
			&& IsLowerRealmRewrite(previousNormalized, normalized))
		{
			return false;
		}
		if (!XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string existingDaoTu);
			bool initialized = !string.IsNullOrWhiteSpace(existingDaoTu)
				&& XjCultivationPathTransitions.TrySetInitialPath(
					actor, XjCultivationPathIds.FuQiYangXing, existingDaoTu, string.Empty, syncVisibleTraits: false);
			if (!initialized)
			{
				bool forceSword = false;
				try { forceSword = actor.hasTrait("XjLongGengDaoTong"); } catch (System.Exception xjCaught54) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjFuQiStateTransitions.cs:54", xjCaught54); }
				initialized = forceSword && XjCultivationPathTransitions.TrySetInitialPath(
					actor, XjCultivationPathIds.FuQiYangXing, string.Empty, XjFuQiLineageIds.Sword, syncVisibleTraits: false);
			}
			if (!initialized) return false;
		}

		bool changed = !string.Equals(previousNormalized, normalized, StringComparison.Ordinal);
		XjActorAccessor.SetString(actor, XjActorDataKeys.RealmId, normalized);
		if (changed)
		{
			int year = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			int safeYear = Math.Max(0, year);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmEnteredYear, safeYear);
			if (safeYear > 0 && string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal)
				&& (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiHuangGuanEnteredYear, out int huangGuanYear)
					|| huangGuanYear <= 0))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiHuangGuanEnteredYear, safeYear);
			}
			if (safeYear > 0 && string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
				&& (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiZhenRenEnteredYear, out int zhenRenYear)
					|| zhenRenYear <= 0))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiZhenRenEnteredYear, safeYear);
			}
			int previousTier = XjRealmSuppression.GetRealmTierFromIdForRuntime(previousNormalized);
			int nextTier = XjRealmSuppression.GetRealmTierFromIdForRuntime(normalized);
			if (nextTier > previousTier && nextTier >= XjRealmSuppression.TierZiFu)
			{
				XjVanillaDeathGuard.MarkRealmPromotionGrace(actor);
			}
			else if (nextTier < XjRealmSuppression.TierZiFu)
			{
				XjVanillaDeathGuard.ClearRealmPromotionGrace(actor);
			}
		}
		if (clearManualRemoved)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmManualRemoved, 0);
		}
		// 服气境界同样在权威写口直接兑现原生伴生特质，避免内部晋升使用
		// syncVisibleTraits=false 时漏掉真人/真君羽士对应的原生特质。
		XjVisibleTraitSync.EnsureRealmNativeTraits(actor, normalized);
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		XjCultivatorCache.CheckAndUpdate(actor);
		XjFuQiCandidateIndex.Observe(actor);
		XjWeaponArtSystem.OnRealmChanged(actor);
		XjHighRealmMovement.ReconcileFlightStateAfterRealmWrite(actor);
		try
		{
			actor.clearTraitCache();
			actor.setStatsDirty();
			actor.updateStats();
		}
		catch (System.Exception xjCaught96_2)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjFuQiStateTransitions.cs:96", xjCaught96_2);
			
			XjCombatHotPathCache.Refresh(actor);
		}
		if (changed)
		{
			XjCultivationStateTransitions.RestoreHealthAfterRealmPromotion(actor);
		}
		XjCultivationSeed.RefreshChuShenForCultivationState(actor);
		if (changed)
		{
			int transitionYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			XjStageZeroObservation.RecordRealmTransition(actor, previousRealmId, normalized);
			XjSemanticDiagnostics.RecordRealmTransition(actor, previousRealmId, normalized, transitionYear);
			if (XjFuQiCoreRouter.TryResolveActorCore(actor, out XjFuQiCoreDefinition definition))
			{
				XjFuQiMethodRules.EnsureRealGongFaEntity(
					actor,
					in definition,
					XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
			}
			// 黄冠入口没有经过 XjRealmPromotionHelper；只在这一层补一次帝明阳破境观察。
			// 真人/真君羽士会在各自完整成功事务后由公共后处理观察，避免半提交时提前反哺。
			if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal))
			{
				XjDiMingYangCityLordUpliftSystem.ObserveRealmPromotion(actor, normalized, transitionYear);
			}
		}
		return true;
	}

	private static bool IsProtectedFuQiHighRealmIdentity(Actor actor)
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
