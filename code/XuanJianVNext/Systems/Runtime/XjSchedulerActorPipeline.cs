using System;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LingWu;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Runtime;

internal enum XjAnnualPipelineStage : byte
{
	Prepare = 0,
	Progression = 1,
	Finalize = 2
}

[Flags]
internal enum XjAnnualInterest : ushort
{
	None = 0,
	Progression = 1 << 0,
	Breakthrough = 1 << 1,
	HighRealm = 1 << 2,
	FaBao = 1 << 3,
	LostFaBao = 1 << 4,
	AutoCollect = 1 << 5,
	ZongMen = 1 << 6,
	JinDan = 1 << 7
}

internal readonly struct XjAnnualActorProfile
{
	internal readonly int RealmTier;
	internal readonly string RealmId;
	internal readonly XjAnnualInterest Interest;

	internal XjAnnualActorProfile(int realmTier, string realmId, XjAnnualInterest interest)
	{
		RealmTier = realmTier;
		RealmId = realmId ?? string.Empty;
		Interest = interest;
	}

	internal bool Has(XjAnnualInterest interest) => (Interest & interest) != 0;
	internal bool NeedsFinalize => (Interest & (XjAnnualInterest.FaBao
		| XjAnnualInterest.LostFaBao
		| XjAnnualInterest.AutoCollect
		| XjAnnualInterest.ZongMen
		| XjAnnualInterest.JinDan)) != 0;
}

/// <summary>
/// Per-cultivator orchestration invoked by the scheduler.
/// Domain rules remain in their owning systems.
/// </summary>
internal static class XjSchedulerActorPipeline
{
	private const float ZhenYuanGainPerStep = 1f;
	internal static bool ProcessStage(
		Actor actor,
		int annualYear,
		XjAnnualPipelineStage stage,
		Action<long> enqueueJinDanCombat,
		out XjAnnualPipelineStage nextStage)
	{
		nextStage = stage;
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0
			|| IsBoatLikeActor(actor))
		{
			return false;
		}
		int qualifiedYear = XjScheduler.ReadCultivationQualifiedYear(actor);
		if (qualifiedYear > 0 && annualYear < qualifiedYear)
		{
			return false;
		}

		using (XjAnnualExecutionContext.Enter(annualYear))
		{
			switch (stage)
			{
				case XjAnnualPipelineStage.Prepare:
					return ProcessPrepareStage(actor, annualYear, out nextStage);
				case XjAnnualPipelineStage.Progression:
					return ProcessProgressionStage(actor, annualYear, enqueueJinDanCombat, out nextStage);
				case XjAnnualPipelineStage.Finalize:
					ProcessFinalizeStage(actor, annualYear);
					return false;
				default:
					return false;
			}
		}
	}

	private static bool ProcessPrepareStage(Actor actor, int annualYear, out XjAnnualPipelineStage nextStage)
	{
		nextStage = XjAnnualPipelineStage.Progression;
		// 资格已经由 updateAge 的单一入口和调度器入队检查完成。
		// 读档专用的仪对影修复只在 bounded bootstrap 执行，年度不再重复。
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjVanillaDeathGuard.EnforceHardLifespanLimit(actor))
		{
			return false;
		}
		if (XjYinSiTraitLifecycle.IsYinSi(actor))
		{
			XjYinSiTraitLifecycle.EnsureTransientState(actor);
			nextStage = XjAnnualPipelineStage.Finalize;
			return false;
		}
		int realmTier = ResolveRealmTier(actor);
		bool isZiFuOrAbove = realmTier >= XjRealmSuppression.TierZiFu;
		bool isJinDan = realmTier >= XjRealmSuppression.TierJinDan;
		bool isYaoXie = XjTrueDamageSystem.IsJinXingYaoXie(actor);
		EnsureMissingDaoTuForEnteredCultivator(actor, realmTier);

		if (XjJinDanResidualJinXing.HasLegacyTrait(actor)
			|| XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanResidualJinXingSource, out string residualSource)
				&& !string.IsNullOrWhiteSpace(residualSource))
		{
			XjJinDanResidualJinXing.ReconcileSource(actor);
		}
		if (isZiFuOrAbove
			|| (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanFailedState, out string failedState)
				&& !string.IsNullOrWhiteSpace(failedState)))
		{
			XjJinDanBreakthroughSystem.ReconcileFailureDemonization(actor);
		}
		if (isJinDan)
		{
			XjGuoWeiQuanBingRegistry.ReconcileLiveActorReadOnly(actor);
			XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(actor);
		}
		if (isYaoXie)
		{
			XjTrueDamageSystem.EnsureJinXingYaoXieCompanion(actor);
		}
		if (realmTier < XjRealmSuppression.TierZhuJi
			&& !isYaoXie
			&& !XjLongShuSystem.IsLongShu(actor))
		{
			return ProcessLowRealmPrepareStage(actor, actorId, realmTier, annualYear);
		}

		if (!XjLongShuSystem.IsLongShu(actor))
		{
			if (isZiFuOrAbove)
			{
				bool hasConfirmedFamily = XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity highRealmIdentity)
					&& highRealmIdentity.Found
					&& highRealmIdentity.FamilyStableIdValue > 0L;
				if (!hasConfirmedFamily
					|| XjFamilyMemberIndex.Shared.IsActorPending(actorId)
					|| !actor.hasClan())
				{
					XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
				}
			}
			else if (XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.FamilyIdentityRepair, actorId, annualYear)
				&& !XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out _)
				&& !XjFamilyMemberIndex.Shared.IsActorPending(actorId))
			{
				XjFamilyMemberIndex.Shared.AddActorToFamily(actor);
			}
		}

		// City branch and surname repair are maintenance, not cultivation growth.
		// Stagger ordinary cultivators across five years; high realms retain yearly
		// reconciliation because their sect and city ownership may change directly.
		if (isZiFuOrAbove
			|| (XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.FamilyBranchAndSurname, actorId, annualYear)
				&& XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity maintenanceIdentity)
				&& maintenanceIdentity.Found))
		{
			XjFamilyMemberIndex.Shared.ReconcileCityBranch(actor, annualYear);

			// 原生婚姻/氏族可能让同一玄鉴父系家族出现不同姓氏。
			// 家族身份确认后按根角色姓氏统一，并在年度管线治理旧存档。
			XjFamilySurnamePolicy.EnsureForConfirmedActor(actor);
		}

		// 紫府本命灵宝只消耗紫府灵物。灵物机缘按角色错峰到五年槽，
		// 每五年判定一次10%，不再让所有紫府每年进入机缘逻辑。
		if (realmTier == XjRealmSuppression.TierZiFu
			&& !isYaoXie
			&& XjZiFuLingWuOpportunitySystem.IsDue(actor, annualYear))
		{
			XjZiFuLingWuOpportunitySystem.TryGrant(actor, annualYear);
		}

		// 金性借用依赖已经确认的家族稳定 ID，必须放在家族补录之后。
		// 只有寿元将尽者才可能满足借用条件，普通修士不应每年都查询家族重宝仓库。
		if (isZiFuOrAbove && IsNearNaturalLifespan(actor))
		{
			XjJinDanResidualJinXing.TryBorrowForReincarnation(actor, annualYear);
		}

		// 仙基上限修复只针对已写入仙基状态的角色。新角色、没有仙基的
		// 高境界修士不需要每年重新拆分并比较字符串。
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int legacyXianJiCount)
			&& legacyXianJiCount > 0)
		{
			XjXianJiAccessor.ReconcileRealmLimit(actor);
		}
		if (!isYaoXie && actor.hasTrait("madness"))
		{
			XjVisibleTraitSync.EnsureCultivatorNoMadness(actor);
		}
		if (isZiFuOrAbove
			|| (realmTier >= XjRealmSuppression.TierZhuJi
				&& XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.SectCityObservation, actorId, annualYear)
				&& HasZongMenIdentity(actor)))
		{
			XjZongMenCultivatorCityIndex.Observe(actor);
		}

		// 求金法仓库回流只用于旧状态修复；新获得、入族和入宗都有定向
		// 写入入口，不应让每一名修士每年扫描一次乾坤袋与两级仓库。
		if (isZiFuOrAbove && XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.QiuJinWarehouseReconcile, actorId, annualYear))
		{
			XjQiuJinFaWarehouseReconciler.ReconcileActor(actor, annualYear);
		}

		if (realmTier <= XjRealmSuppression.TierLianQi)
		{
			ProcessCaiQiCompletion(actor, annualYear);
		}
		if (XjAptitudeTraitLifecycle.HasAnnualInterest(actor, annualYear))
		{
			XjAptitudeTraitLifecycle.TickAnnual(actor, annualYear);
		}
		return true;
	}

	private static bool ProcessLowRealmPrepareStage(Actor actor, long actorId, int realmTier, int annualYear)
	{
		// 胎息、炼气和未入道资质者的年度职责只保留修炼链路必需项。
		// 家族/姓氏修复降为五年维护槽；宗门、法宝、金丹遗留和仙基修复
		// 都由筑基以上或对应状态写入入口负责，避免数百低境修士每年跑完整维护。
		if (XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.FamilyBranchAndSurname, actorId, annualYear))
		{
			if (XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity identity)
				&& identity.Found)
			{
				XjFamilyMemberIndex.Shared.ReconcileCityBranch(actor, annualYear);
				XjFamilySurnamePolicy.EnsureForConfirmedActor(actor);
			}
			else if (!XjFamilyMemberIndex.Shared.IsActorPending(actorId))
			{
				XjFamilyMemberIndex.Shared.AddActorToFamily(actor);
			}
		}

		if (actor.hasTrait("madness"))
		{
			XjVisibleTraitSync.EnsureCultivatorNoMadness(actor);
		}
		if (realmTier <= XjRealmSuppression.TierLianQi)
		{
			ProcessCaiQiCompletion(actor, annualYear);
		}
		if (XjAptitudeTraitLifecycle.HasAnnualInterest(actor, annualYear))
		{
			XjAptitudeTraitLifecycle.TickAnnual(actor, annualYear);
		}
		return true;
	}

	private static void EnsureMissingDaoTuForEnteredCultivator(Actor actor, int realmTier)
	{
		if (actor?.data == null
			|| (realmTier < XjRealmSuppression.TierLianQi
				&& XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierLianQi))
		{
			return;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& !string.IsNullOrWhiteSpace(daoTu)
			&& XjDaoTuVisibleTraitCatalog.TryResolveTraitId(daoTu, out _))
		{
			return;
		}

		XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out _);
	}

	private static bool ProcessProgressionStage(
		Actor actor,
		int annualYear,
		Action<long> enqueueJinDanCombat,
		out XjAnnualPipelineStage nextStage)
	{
		nextStage = XjAnnualPipelineStage.Finalize;
		XjManualRealmTraitReconciliation.TickPendingManualJinDan(actor, annualYear);
		XjAnnualActorProfile profile = ResolveProgressionProfile(actor);
		// 高阶宗门成员不能被原生迁城留在外邦；筑基及以下无需进入城市迁移桥。
		if (profile.RealmTier >= XjRealmSuppression.TierZiFu)
		{
			XjSectHighRealmResidenceSystem.Enforce(actor, annualYear);
		}
		long snapshotSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualSnapshot, 31);
		XjActorCultivationSnapshot annualSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, profile.RealmTier);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualSnapshot, snapshotSample);
		long growthSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualCultivationGrowth, 31);
		bool didGrow = ProcessCultivationGrowth(actor, annualSnapshot, annualYear);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualCultivationGrowth, growthSample);
		if (didGrow)
		{
			bool qingXuanChecked = false;
			if (ShouldProcessQingXuan(actor, annualSnapshot))
			{
				qingXuanChecked = true;
				long qingXuanSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualQingXuan, 31);
				XjQingXuanKongZhengSystem.TickActor(actor, annualSnapshot, annualYear);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualQingXuan, qingXuanSample);
			}
			if (qingXuanChecked
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string postQingXuanDaoTu)
				&& !string.Equals(postQingXuanDaoTu, annualSnapshot.DaoTu, StringComparison.Ordinal))
			{
				long rebuildSnapshotSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualSnapshot, 31);
				annualSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualSnapshot, rebuildSnapshotSample);
			}
			// Growth only mutates true essence. Re-read that one field instead of
			// rebuilding the full cultivation/FaBao snapshot in the same annual pass.
			else if (XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float postGrowthZhenYuan))
			{
				annualSnapshot = annualSnapshot.WithZhenYuan(postGrowthZhenYuan);
			}
			if (ShouldProcessGongFa(actor, annualSnapshot, profile.RealmTier, annualYear))
			{
				long gongFaSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualGongFa, 31);
				ProcessGongFa(actor, annualSnapshot, annualYear);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualGongFa, gongFaSample);
				// 功法升品、借法和道途校准都会影响后续神通/求金条件。
				// 只在真正进入过功法阶段后局部重建一次，避免同年继续使用旧快照。
				annualSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			}
			if (profile.Has(XjAnnualInterest.HighRealm))
			{
				long highRealmSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualHighRealm, 31);
				ProcessHighRealm(actor, annualSnapshot, annualYear, enqueueJinDanCombat);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualHighRealm, highRealmSample);
				annualSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			}
		}

		// ZiFu -> JinDan is owned by the high-realm pipeline and JinDan has no
		// standard next realm. Avoid building a CaiQi snapshot for both branches.
		if (didGrow
			&& profile.Has(XjAnnualInterest.Breakthrough)
			&& ShouldProcessBreakthrough(annualSnapshot))
		{
			long breakthroughSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualBreakthrough, 31);
			ProcessBreakthrough(actor, annualSnapshot, annualYear);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualBreakthrough, breakthroughSample);
		}

		// 器艺属于修士年度成长，不占百艺名额；只结算当前终身绑定的一门兵器道路。
		if (profile.RealmTier > XjRealmSuppression.TierNone)
		{
			XjWeaponArtSystem.TickActor(actor, annualYear);
		}

		// 修仙百艺复用既有修士年度管线，并由互斥职业路由保证每人只推进一项百艺主任务。
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjCraftActorIndex.Contains(actorId))
		{
			long craftSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualCraft, 31);
			XjCraftAnnualRouter.TickActor(actor, annualYear);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualCraft, craftSample);
		}
		if (profile.RealmTier > XjRealmSuppression.TierNone
			&& XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.TalismanDistribution, actorId, annualYear))
		{
			long talismanSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualTalisman, 31);
			XjTalismanDistributionSystem.TickActor(actor, annualYear);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualTalisman, talismanSample);
		}

		// 人生关系复用当前年度修士队列与宗门/城市增量索引。观察器内部五年分槽，
		// 每人最多检查12名候选，不增加全世界扫描。
		if (profile.RealmTier > XjRealmSuppression.TierNone)
		{
			long threeBookSocialSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualThreeBookSocial, 31);
			XjThreeBookSocialObserver.TickActor(actor, annualYear);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualThreeBookSocial, threeBookSocialSample);
		}

		// Breakthrough may have changed the actor's execution profile. Only actors
		// with an actual finalize concern consume the third annual queue stage.
		long finalizeResolveSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualFinalizeResolve, 31);
		bool needsFinalize = ResolveFinalizeProfile(actor, annualYear).NeedsFinalize;
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualFinalizeResolve, finalizeResolveSample);
		return needsFinalize;
	}

	private static XjAnnualActorProfile ResolveProgressionProfile(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjAnnualActorProfile(XjRealmSuppression.TierNone, string.Empty, XjAnnualInterest.None);
		}

		int realmTier = ResolveRealmTier(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (!string.IsNullOrWhiteSpace(realmId))
		{
			XjCultivationStateTransitions.EnsureDaoTuForRealm(actor, realmId, true);
		}
		XjAnnualInterest interest = XjAnnualInterest.Progression;
		if (realmTier >= XjRealmSuppression.TierZiFu)
		{
			interest |= XjAnnualInterest.HighRealm;
		}
		else
		{
			interest |= XjAnnualInterest.Breakthrough;
		}

		return new XjAnnualActorProfile(realmTier, realmId, interest);
	}

	private static XjAnnualActorProfile ResolveFinalizeProfile(Actor actor, int currentYear)
	{
		if (actor?.data == null)
		{
			return new XjAnnualActorProfile(XjRealmSuppression.TierNone, string.Empty, XjAnnualInterest.None);
		}

		int realmTier = ResolveRealmTier(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (!string.IsNullOrWhiteSpace(realmId))
		{
			XjCultivationStateTransitions.EnsureDaoTuForRealm(actor, realmId, true);
		}
		XjAnnualInterest interest = XjAnnualInterest.None;
		long actorId = ((BaseSystemData)actor.data).id;
		bool needsPersonalZiFuLingBao = XjFaBaoForgePolicy.NeedsPersonalZiFuLingBao(actor);
		if (realmTier >= XjRealmSuppression.TierZhuJi
			&& (needsPersonalZiFuLingBao
				|| (IsEquipmentMaintenanceDue(actorId, realmTier, currentYear)
					&& XjFaBaoEquipmentSync.HasAnnualInterest(actor))))
		{
			interest |= XjAnnualInterest.FaBao;
		}
		if (ShouldAttemptLostFaBaoDiscovery(actor, currentYear))
		{
			interest |= XjAnnualInterest.LostFaBao;
		}
		if (XjAutoCollectSystem.HasAnnualInterest(actor, realmId))
		{
			interest |= XjAnnualInterest.AutoCollect;
		}
		if (realmTier >= XjRealmSuppression.TierZiFu
			|| (realmTier >= XjRealmSuppression.TierZhuJi
				&& HasZongMenIdentity(actor)
				&& XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.SectIdentityRefresh, actorId, currentYear)))
		{
			interest |= XjAnnualInterest.ZongMen;
		}
		if (realmTier >= XjRealmSuppression.TierJinDan)
		{
			interest |= XjAnnualInterest.JinDan;
		}

		return new XjAnnualActorProfile(realmTier, realmId, interest);
	}

	private static bool ShouldProcessQingXuan(Actor actor, in XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null
			|| !string.Equals(snapshot.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| !string.Equals(snapshot.DaoTu, XjQingXuanKongZhengSystem.SourceDaoTu, StringComparison.Ordinal)
			|| XjQingXuanKongZhengSystem.CanEnterQingXuan(actor))
		{
			return false;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanQingCanQi, out int qingCanQi)
			&& qingCanQi > 0)
		{
			return true;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanChuYangJi, out int chuYangJi)
			&& chuYangJi > 0)
		{
			return true;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, out int foundation)
			&& foundation > 0)
		{
			return true;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjDeterministicHash.PositiveIndex(actorId, "qingxuan_entry_once", 1000) == 0;
	}

	private static bool ShouldProcessGongFa(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		int realmTier,
		int currentYear)
	{
		if (actor?.data == null || snapshot.XjZz <= 0)
		{
			return false;
		}

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int grade)
			|| grade <= 0
			|| grade > XjGongFaDefinition.MaxGrade
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaName, out string name)
			|| string.IsNullOrWhiteSpace(name))
		{
			return true;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaDaoTu, out string gongFaDaoTu);
		if (!string.IsNullOrWhiteSpace(snapshot.DaoTu)
			&& (string.IsNullOrWhiteSpace(gongFaDaoTu)
				|| !string.Equals((gongFaDaoTu ?? string.Empty).Trim(), snapshot.DaoTu.Trim(), StringComparison.Ordinal)))
		{
			return true;
		}

		int maximumAllowedGrade = Math.Min(
			XjGongFaDefinition.MaxGrade,
			Math.Min(
				XjGongFaAptitudeRules.GetAptitudeGradeCap(actor, snapshot.XjZz),
				ResolveGongFaRealmGradeCap(realmTier)));
		if (maximumAllowedGrade <= grade)
		{
			return false;
		}

		int nextGrade = grade + 1;
		if (nextGrade > XjGongFaDefinition.MaxGrade)
		{
			return false;
		}
		if (nextGrade == 6 && !snapshot.HasQiuJinFa)
		{
			return false;
		}

		return XjGongFaAttemptSchedule.IsDue(actor, nextGrade, currentYear);
	}

	private static int ResolveGongFaRealmGradeCap(int realmTier)
	{
		return realmTier switch
		{
			XjRealmSuppression.TierJinDan => 6,
			XjRealmSuppression.TierZiFu => 6,
			XjRealmSuppression.TierZhuJi => 5,
			XjRealmSuppression.TierLianQi => 4,
			XjRealmSuppression.TierTaiXi => 2,
			_ => 1
		};
	}

	private static bool ShouldProcessBreakthrough(in XjActorCultivationSnapshot snapshot)
	{
		return XjCultivationNextRealmResolver.TryGetNextRule(snapshot.RealmId, out XjRealmRule targetRule)
			&& targetRule.IsImplemented
			&& CanWriteBreakthroughRealmId(targetRule.RealmId)
			&& !targetRule.RequiresFiveXianJi
			&& snapshot.ZhenYuan >= targetRule.RequiredZhenYuan;
	}

	private static int ResolveRealmTier(Actor actor)
	{
		if (actor?.data == null)
		{
			return XjRealmSuppression.TierNone;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return XjCultivatorCache.TryGetRealmTier(actorId, out int cachedTier)
			? cachedTier
			: XjRealmSuppression.GetRealmTier(actor);
	}

	private static bool IsEquipmentMaintenanceDue(long actorId, int realmTier, int currentYear)
	{
		if (realmTier >= XjRealmSuppression.TierJinDan)
		{
			return true;
		}

		if (realmTier >= XjRealmSuppression.TierZiFu)
		{
			return XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.ZiFuEquipmentMaintenance, actorId, currentYear);
		}

		return XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.ZhuJiEquipmentMaintenance, actorId, currentYear);
	}

	private static bool IsNearNaturalLifespan(Actor actor)
	{
		if (actor?.stats == null)
		{
			return false;
		}

		float lifespan = Math.Max(0f, actor.stats["lifespan"]);
		return lifespan > 0f && Math.Max(0f, actor.getAge()) >= lifespan * 0.95f;
	}

	private static bool HasZongMenIdentity(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor) > 0L;
	}

	private static bool IsBoatLikeActor(Actor actor)
	{
		ActorAsset asset = actor?.asset;
		if (asset == null)
		{
			return false;
		}

		if (asset.is_boat)
		{
			return true;
		}

		string assetId = asset.id ?? string.Empty;
		return assetId.IndexOf("boat", StringComparison.OrdinalIgnoreCase) >= 0
			|| assetId.IndexOf("ship", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static void ProcessFinalizeStage(Actor actor, int currentYear)
	{
		XjAnnualActorProfile profile = ResolveFinalizeProfile(actor, currentYear);
		if (profile.Has(XjAnnualInterest.FaBao))
		{
			XjEquipmentForgeConsumer.ReconcileManagedItemLimit(actor, profile.RealmId);
			XjFaBaoEquipmentSync.TryBorrowFamilyFaBao(actor, profile.RealmId, currentYear);
			XjFaBaoEquipmentSync.TryEnsureGeneratedEquipment(actor);
			XjFaBaoAcquisition.TryForgeAnnualIfMissing(actor, profile.RealmId, currentYear);
			XjEquipmentForgeConsumer.TryForgeAnnual(actor, profile.RealmId, currentYear);
		}
		if (profile.Has(XjAnnualInterest.LostFaBao))
		{
			ProcessLostFaBaoDiscovery(actor, currentYear);
		}
		if (profile.Has(XjAnnualInterest.AutoCollect))
		{
			XjAutoCollectSystem.TickActor(actor, profile.RealmId);
		}
		if (profile.Has(XjAnnualInterest.ZongMen))
		{
			ProcessZongMen(actor, profile.RealmId, currentYear);
		}
		if (profile.Has(XjAnnualInterest.JinDan))
		{
			XjGuoWeiRegistry.ReconcileLiveActor(actor);
			XjJinDanBreakthroughSystem.TickAnnualGift(actor);
		}
	}

	private static bool ShouldAttemptLostFaBaoDiscovery(Actor actor, int currentYear)
	{
		if (!XjFamilyFaBaoWarehouse.HasLostEntries
			|| actor?.data == null
			|| currentYear <= 0)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjDeterministicHash.PositiveIndex(actorId + currentYear, "lost_fabao_discovery", 100) < 2;
	}

	private static void ProcessLostFaBaoDiscovery(Actor actor, int currentYear)
	{
		if (ShouldAttemptLostFaBaoDiscovery(actor, currentYear))
		{
			XjFamilyFaBaoWarehouse.TryDiscoverLostFaBao(actor, currentYear);
		}
	}

	private static bool ProcessCultivationGrowth(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
	{
		float elapsedYears = ZhenYuanGainPerStep;
		if (currentYear > 0)
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLastCultivationYear, out int lastYear))
			{
				if (lastYear == currentYear)
				{
					return false;
				}
				if (lastYear > currentYear)
				{
					// 旧档或跨世界恢复可能留下未来年份。修复为当前年前一年，
					// 避免角色永久认为本年已经修炼。
					lastYear = currentYear - 1;
				}
				if (lastYear > 0 && currentYear > lastYear)
				{
					elapsedYears = Math.Max(1, currentYear - lastYear);
				}
			}

			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjLastCultivationYear, currentYear);
		}

		// 高倍速年度请求合并到最新年份；遗漏年份只聚合被动真元增长，
		// 功法、瓶颈、丹药和突破仍只在当前年度执行一次，避免补算连跳境界。
		XjCultivationLocalExecutor.RunValidatedLocalStep(actor, elapsedYears, snapshot);
		return true;
	}

	private static void ProcessGongFa(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
	{
		XjGongFaProgression.TickActor(actor, snapshot);
		if (!string.Equals(snapshot.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return;
		}

		if (XjDaoXingStageRules.IsZhuJiLateOrHigher(
			snapshot.RealmId,
			snapshot.ZhenYuan,
			snapshot.XianJiCount))
		{
			XjZongMenCityData.HandleFoundationLatePromotion(actor, currentYear);
		}
	}

	private static void ProcessHighRealm(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear, Action<long> enqueueJinDanCombat)
	{
		var ctx = new XjHighRealmDetectionContext(actor, snapshot, currentYear, budget: 6);
		XjHighRealmDetectionPipeline.Tick(ref ctx);

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string postRealmId);
		if (string.Equals(postRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& !string.Equals(snapshot.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
				bool successChainHandledZongMen = XjActorAccessor.TryGetInt(
						actor,
						XjActorDataKeys.XjJinDanSuccessEventYear,
						out int successEventYear)
					&& successEventYear == currentYear;
			if (!successChainHandledZongMen)
			{
				XjZongMenCityData.HandleJinDanPromotion(actor, currentYear);
			}
			XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.JinDan, "JinDanPromotion");
		}

		if (string.Equals(postRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(postRealmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			enqueueJinDanCombat?.Invoke(((BaseSystemData)actor.data).id);
		}
	}

	private static void ProcessCaiQiCompletion(Actor actor, int currentYear)
	{
		if (!XjCaiQiActorAccessor.ShouldEnqueueForCaiQi(actor))
		{
			return;
		}

		if (XjCaiQiActorAccessor.GetNextCaiQiYear(actor) <= 0)
		{
			int interval = 3 + (int)(((ulong)((BaseSystemData)actor.data).id + (ulong)(long)currentYear) % 3uL);
			XjCaiQiActorAccessor.SetNextCaiQiYear(actor, currentYear + interval);
			XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Pending);
			return;
		}

		if (currentYear < XjCaiQiActorAccessor.GetNextCaiQiYear(actor))
		{
			return;
		}

		XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Active);
		if (TryConsumePreferredFamilyQi(actor, out XjCaiQiCatalogEntry familyQiEntry))
		{
			XjCaiQiActorAccessor.MarkCompleted(
				actor,
				XjCaiQiResultTypes.XianTianQi,
				familyQiEntry.PlaceTypeId,
				familyQiEntry.BranchId,
				"家族仓库");
			XjCaiQiFaAcquisition.TryAcquireFromCaiQiResult(
				actor,
				new XjCaiQiResolvedResult(
					true,
					XjCaiQiResultTypes.XianTianQi,
					familyQiEntry.PlaceTypeId,
					familyQiEntry.BranchId,
					"家族仓库",
					"FamilyWarehousePreferredQi"));
			ScheduleNextCaiQi(actor, currentYear);
			return;
		}

		XjActorCaiQiCandidate candidate = XjActorCaiQiCandidateResolver.TryResolve(actor);
		if (!candidate.Found)
		{
			if (IsDaoZhuActor(actor))
			{
				RetryCaiQiNextYear(actor, currentYear);
				return;
			}

			XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Failure, candidate.ReasonCode);
			ScheduleNextCaiQi(actor, currentYear);
			return;
		}

		XjCaiQiResolvedResult result = XjCaiQiResultResolver.Resolve(actor, candidate, currentYear);
		if (!result.Success)
		{
			if (IsDaoZhuActor(actor))
			{
				RetryCaiQiNextYear(actor, currentYear);
				return;
			}

			XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Failure, result.ReasonCode);
			ScheduleNextCaiQi(actor, currentYear);
			return;
		}

		XjCaiQiActorAccessor.MarkCompleted(actor, result.ResultType, result.PlaceTypeId, result.BranchId, result.SiteName);
		XjCaiQiFaAcquisition.TryAcquireFromCaiQiResult(actor, result);
		ScheduleNextCaiQi(actor, currentYear);
	}

	private static bool TryConsumePreferredFamilyQi(Actor actor, out XjCaiQiCatalogEntry entry)
	{
		entry = default;
		if (actor?.data == null
			|| !XjFamilyDaoTuRules.TryResolvePreferredDaoTu(actor, out string preferredDaoTu)
			|| !XjCaiQiCatalog.TryGetEntryByDisplayName(preferredDaoTu, out entry)
			|| !XjCaiQiCatalog.TryGetOldResourceIdByBranchId(entry.BranchId, out string resourceId)
			|| string.IsNullOrWhiteSpace(resourceId)
			|| !XjBreakthroughRules.TryResolveFamilyKey(actor, out string familyKey)
			|| !XjFamilyCaiQiWarehouse.TryGetCount(familyKey, resourceId, out int count)
			|| count <= 0)
		{
			return false;
		}

		return XjFamilyCaiQiWarehouse.TryConsume(familyKey, resourceId, 1);
	}

	private static bool IsDaoZhuActor(Actor actor)
	{
		return actor != null && actor.hasTrait("ChuShen8");
	}

	private static void RetryCaiQiNextYear(Actor actor, int currentYear)
	{
		XjCaiQiActorAccessor.SetNextCaiQiYear(actor, currentYear + 1);
		XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Pending);
	}

	private static void ScheduleNextCaiQi(Actor actor, int currentYear)
	{
		int interval = 3 + (int)(((ulong)((BaseSystemData)actor.data).id + (ulong)(long)currentYear) % 3uL);
		XjCaiQiActorAccessor.SetLastAndNextCaiQiYear(actor, currentYear, currentYear + interval);
		XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Cooldown);
	}

	private static void ProcessBreakthrough(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
	{
		if (!XjCultivationNextRealmResolver.TryGetNextRule(snapshot.RealmId, out XjRealmRule targetRule)
			|| !targetRule.IsImplemented
			|| !CanWriteBreakthroughRealmId(targetRule.RealmId)
			|| targetRule.RequiresFiveXianJi
			|| snapshot.ZhenYuan < targetRule.RequiredZhenYuan)
		{
			return;
		}

		bool needsCaiQiSnapshot = targetRule.RequiresCaiQi
			|| string.Equals(targetRule.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal);
		XjCaiQiSnapshot caiQiSnapshot = needsCaiQiSnapshot
			? XjCaiQiActorAccessor.BuildSnapshot(actor)
			: XjCaiQiSnapshot.Empty;
		XjCultivationRuleCheckResult checkResult = XjCultivationRuleValidator.Check(snapshot, targetRule, caiQiSnapshot);
		if (!checkResult.Passed)
		{
			return;
		}

		XjBreakthroughAttemptResult result = XjBreakthroughRules.Resolve(actor, snapshot, caiQiSnapshot, targetRule.RealmId);
		if (!result.CanPromote
			|| !XjCultivationStateTransitions.TrySetRealm(actor, targetRule.RealmId, true))
		{
			return;
		}

		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
			actor,
			targetRule.RealmId,
			currentYear,
			syncVisibleTraits: false,
			restoreHealth: false);

		XjActorCultivationSnapshot postSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		string postDaoTu = postSnapshot.DaoTu;
		if (string.IsNullOrWhiteSpace(postDaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(postDaoTu.Trim(), out _))
		{
			XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out postDaoTu);
			postSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			postDaoTu = postSnapshot.DaoTu;
		}
		// 名称、可见境界与排行榜索引属于境界写入的强一致投影，
		// 必须早于血脉、公告、宗门等非关键后处理。
		XjRealmTitleApplyService.ApplyOnPromotion(actor, targetRule.RealmId, postDaoTu);
		XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
		XjGongFaProgression.EnsureRealmMinimumGrade(actor, targetRule.RealmId, postDaoTu);

		if (string.Equals(targetRule.RealmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			XjGongFaProgression.EnsureEntryGongFa(actor, postSnapshot);
		}

		if (string.Equals(targetRule.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			string consumedCaiQiResourceId = XjBreakthroughRules.CommitCaiQiForBreakthrough(actor, caiQiSnapshot);
			if (string.Equals(consumedCaiQiResourceId, "zaqi", StringComparison.Ordinal)
				|| (string.IsNullOrWhiteSpace(consumedCaiQiResourceId) && XjCaiQiActorAccessor.IsCaiQiResultZaQi(actor)))
			{
				XjCaiQiActorAccessor.MarkLianQiByZaQi(actor);
			}
			if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out postDaoTu)
				|| string.IsNullOrWhiteSpace(postDaoTu)
				|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(postDaoTu.Trim(), out _))
			{
				XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out postDaoTu);
			}
			postSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			postDaoTu = postSnapshot.DaoTu;
			XjGongFaProgression.EnsureEntryGongFa(actor, postSnapshot);
		}

		if (string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			// 旧档或跨版本角色可能在筑基阶段漏写了首门仙基。紫府公告前
			// 只按真实功法映射补写，失败则不发布带“未知”占位的公告。
			XjZiFuProgression.EnsureZhuJiFoundationXianJi(actor, postDaoTu, currentYear);
			postSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			postDaoTu = postSnapshot.DaoTu;
		}

		XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
		if (string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(targetRule.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
		}
		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.RealmBreakthrough(actor, targetRule.RealmId, postDaoTu));
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
			actor,
			targetRule.RealmId,
			currentYear,
			applyMingShuReward: false,
			refreshBloodline: false);
		if (string.Equals(targetRule.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.ZhuJi, "ZhuJiPromotion");
		}

		if (string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			XjZongMenCityData.HandleZiFuPromotion(actor, currentYear);
			XjLongShuSystem.NotifyZiFuAppeared(actor);
			if (XjRuntimeSettings.BroadcastHighRealmEnabled
				&& XjAnnouncementText.TryBuildZiFuPromotion(actor, out string promotionText))
			{
				XjBroadcastSystem.BroadcastBLevelActorEvent(
					actor,
					promotionText,
					iconId: XjEventIconCatalog.ZiFuUpgrade);
			}
			XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.ZiFu, "ZiFuPromotion");
		}
	}

	private static bool CanWriteBreakthroughRealmId(string targetRealmId)
	{
		return string.Equals(targetRealmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal);
	}

	private static void ProcessZongMen(Actor actor, string realmId, int currentYear)
	{
		if (XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return;
		}

		bool isJinDanLike = string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
		bool canBorrowOrDongTian = string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| isJinDanLike;
		if (!canBorrowOrDongTian)
		{
			return;
		}

		XjZongMenIdentitySnapshot zongMenState = XjZongMenAccessor.BuildIdentity(actor);
		if (!zongMenState.Found || zongMenState.ZongMenId <= 0L)
		{
			if (isJinDanLike)
			{
				XjZongMenCityData.HandleJinDanPromotion(actor, currentYear);
			}
			else if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
			{
				XjZongMenCityData.HandleZiFuPromotion(actor, currentYear);
			}
			zongMenState = XjZongMenAccessor.BuildIdentity(actor);
			if (!zongMenState.Found || zongMenState.ZongMenId <= 0L)
			{
				return;
			}
		}

		XjZongMenCaiQiFaBorrow.TryBorrowForActor(actor, zongMenState);
		XjZongMenGongFaBorrow.TryBorrowForActor(actor, zongMenState);

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			XjZongMenDongTianLifecycle.TickAnnual(actor, currentYear);
		}

		// 筑基弟子数量通常远高于高阶修士。仓库回流与乾坤袋快照都需要
		// 读取传承/装备，改为错峰校验；入宗和获得新传承时仍由对应事件入口
		// 立即写入。紫府、金丹保留每年同步，避免宗门核心资产滞后。
		long actorId = ((BaseSystemData)actor.data).id;
		bool needsWarehouseReconcile = !string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.SectWarehouseReconcile, actorId, currentYear);
		if (needsWarehouseReconcile)
		{
			XjGongFaWarehouseReconciler.ReconcileActor(actor, currentYear);
			XjQianKunDaiSystem.UpdateState(actor);
		}
	}

}
