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
	// Stage1 saves may persist this value after core progression. Stage2 treats
	// it as a legacy maintenance marker and migrates it into the maintenance lane.
	Finalize = 2
}

internal enum XjAnnualMaintenanceStage : byte
{
	Identity = 0,
	Ancillary = 1,
	Assets = 2
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
				// Finalize is no longer executed on the core lane. The scheduler consumes
				// this value only as a Stage1 save migration marker.
				default:
					return false;
			}
		}
	}

	internal static bool ProcessMaintenanceStage(
		Actor actor,
		int fromYearInclusive,
		int annualYear,
		XjAnnualMaintenanceStage stage,
		out XjAnnualMaintenanceStage nextStage)
	{
		nextStage = stage;
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0
			|| IsBoatLikeActor(actor))
		{
			return false;
		}

		int qualifiedYear = XjScheduler.ReadCultivationQualifiedYear(actor);
		int fromYear = Math.Max(Math.Max(1, fromYearInclusive), qualifiedYear > 0 ? qualifiedYear : 1);
		if (annualYear < fromYear) return false;

		using (XjAnnualExecutionContext.Enter(annualYear))
		{
			switch (stage)
			{
				case XjAnnualMaintenanceStage.Identity:
					ProcessMaintenanceIdentityStage(actor, fromYear, annualYear);
					nextStage = XjAnnualMaintenanceStage.Ancillary;
					return true;
				case XjAnnualMaintenanceStage.Ancillary:
					ProcessMaintenanceAncillaryStage(actor, fromYear, annualYear);
					nextStage = XjAnnualMaintenanceStage.Assets;
					return true;
				case XjAnnualMaintenanceStage.Assets:
					ProcessMaintenanceAssetsStage(actor, fromYear, annualYear);
					return false;
				default:
					return false;
			}
		}
	}

	internal static bool HasSecondaryAnnualInterest(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		int realmTier = ResolveRealmTier(actor);
		return XjWeaponArtSystem.HasAnnualInterest(actor)
			|| XjCraftActorIndex.Contains(actorId)
			|| realmTier >= XjRealmSuppression.TierZhuJi
			|| realmTier >= XjRealmSuppression.TierJinDan
			|| (realmTier > XjRealmSuppression.TierNone && XjFamilyFaBaoWarehouse.HasLostEntries);
	}

	/// <summary>
	/// Exact-year secondary gameplay. Unlike coalesced compatibility maintenance,
	/// these systems change proficiency, production or annual chance outcomes and
	/// therefore consume every queued logical year in order.
	/// </summary>
	internal static void ProcessSecondaryAnnual(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0 || IsBoatLikeActor(actor)) return;
		string realmIdAtYear = ResolveRealmIdAtYear(actor, annualYear);
		using (XjAnnualExecutionContext.Enter(annualYear, actor, realmIdAtYear))
		{
			long actorId = ((BaseSystemData)actor.data).id;
			int realmTier = XjRealmSuppression.GetRealmTierFromIdForRuntime(realmIdAtYear);
			if (XjWeaponArtSystem.IsActiveInYear(actor, annualYear))
			{
				RunSecondaryStep(actorId, annualYear, "WeaponArt", () => XjWeaponArtSystem.TickActor(actor, annualYear));
			}
			if (XjCraftActorIndex.Contains(actorId)
				&& XjCraftTraitRules.IsActiveInYear(actor, annualYear))
			{
				RunSecondaryStep(actorId, annualYear, "Craft", () =>
				{
					long craftSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualCraft, 31);
					try { XjCraftAnnualRouter.TickActor(actor, annualYear); }
					finally { XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualCraft, craftSample); }
				});
			}
			if (realmTier >= XjRealmSuppression.TierZhuJi)
			{
				RunSecondaryStep(actorId, annualYear, "FaBaoForge", () =>
				{
					XjFaBaoAcquisition.TryForgeAnnualIfMissing(actor, realmIdAtYear, annualYear);
					XjEquipmentForgeConsumer.TryForgeAnnual(actor, realmIdAtYear, annualYear);
				});
			}
			if (realmTier > XjRealmSuppression.TierNone)
			{
				RunSecondaryStep(actorId, annualYear, "LostFaBao", () => ProcessLostFaBaoDiscovery(actor, annualYear));
			}
			if (realmTier >= XjRealmSuppression.TierJinDan)
			{
				RunSecondaryStep(actorId, annualYear, "JinDanGift", () => XjJinDanBreakthroughSystem.TickAnnualGift(actor, annualYear));
			}
		}
	}

	private static bool ProcessPrepareStage(Actor actor, int annualYear, out XjAnnualPipelineStage nextStage)
	{
		nextStage = XjAnnualPipelineStage.Progression;
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjVanillaDeathGuard.EnforceHardLifespanLimit(actor))
		{
			return false;
		}
		if (XjYinSiTraitLifecycle.IsYinSi(actor))
		{
			XjYinSiTraitLifecycle.EnsureTransientState(actor);
			return false;
		}

		int realmTier = ResolveRealmTier(actor);
		bool isZiFuOrAbove = realmTier >= XjRealmSuppression.TierZiFu;
		bool isJinDan = realmTier >= XjRealmSuppression.TierJinDan;
		bool isYaoXie = XjTrueDamageSystem.IsJinXingYaoXie(actor);
		EnsureMissingDaoTuForEnteredCultivator(actor, realmTier);
		XjProgressionCandidateState candidateState =
			XjCultivatorCandidateIndex.GetOrRefreshProgression(actor, annualYear);

		// These are progression-state compatibility repairs. High-realm detection
		// reads them in the immediately following core stage, so they remain here.
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

		// Stage1 opportunity clocks belong to progression, not compatibility maintenance.
		if (realmTier == XjRealmSuppression.TierZiFu
			&& !isYaoXie
			&& XjZiFuLingWuOpportunitySystem.IsDue(actor, annualYear))
		{
			XjZiFuLingWuOpportunitySystem.TryGrant(actor, annualYear);
		}

		if (isZiFuOrAbove && IsNearNaturalLifespan(actor))
		{
			// Borrowing needs a stable family id. Only the near-death subset receives
			// this targeted repair on the guaranteed core lane.
			if (!XjLongShuSystem.IsLongShu(actor)
				&& (!XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity identity)
					|| !identity.Found
					|| identity.FamilyStableIdValue <= 0L))
			{
				XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
			}
			XjJinDanResidualJinXing.TryBorrowForReincarnation(actor, annualYear);
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int legacyXianJiCount)
			&& legacyXianJiCount > 0)
		{
			XjXianJiAccessor.ReconcileRealmLimit(actor);
		}
		bool caiQiDue = realmTier <= XjRealmSuppression.TierLianQi
			&& candidateState.ShouldProcessCaiQi(annualYear);
		XjStageZeroObservation.RecordCandidateRouting("CaiQi", caiQiDue);
		if (caiQiDue)
		{
			ProcessCaiQiCompletion(actor, annualYear);
			XjCultivatorCandidateIndex.RefreshProgression(actor, annualYear);
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
		nextStage = XjAnnualPipelineStage.Progression;
		XjManualRealmTraitReconciliation.TickPendingManualJinDan(actor, annualYear);
		XjAnnualActorProfile profile = ResolveProgressionProfile(actor);
		long snapshotSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualSnapshot, 31);
		XjActorCultivationSnapshot annualSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, profile.RealmTier);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualSnapshot, snapshotSample);
		long growthSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualCultivationGrowth, 31);
		bool didGrow = ProcessCultivationGrowth(actor, annualSnapshot, annualYear);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualCultivationGrowth, growthSample);
		if (didGrow)
		{
			XjProgressionCandidateState candidateState =
				XjCultivatorCandidateIndex.RefreshAfterGrowth(actor, annualYear);
			bool qingXuanChecked = false;
			bool qingXuanDue = candidateState.Has(XjProgressionCandidateFlags.QingXuan)
				&& ShouldProcessQingXuan(actor, annualSnapshot);
			XjStageZeroObservation.RecordCandidateRouting("QingXuan", qingXuanDue);
			if (qingXuanDue)
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
				annualSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualSnapshot, rebuildSnapshotSample);
			}
			else if (XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float postGrowthZhenYuan))
			{
				annualSnapshot = annualSnapshot.WithZhenYuan(postGrowthZhenYuan);
			}

			candidateState = XjCultivatorCandidateIndex.GetOrRefreshProgression(actor, annualYear);
			bool gongFaDue = candidateState.ShouldProcessGongFa(annualYear);
			XjStageZeroObservation.RecordCandidateRouting("GongFa", gongFaDue);
			if (gongFaDue && ShouldProcessGongFa(actor, annualSnapshot, profile.RealmTier, annualYear))
			{
				long gongFaSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualGongFa, 31);
				ProcessGongFa(actor, annualSnapshot, annualYear);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualGongFa, gongFaSample);
				annualSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
				candidateState = XjCultivatorCandidateIndex.RefreshProgression(actor, annualYear);
			}

			bool highRealmDue = candidateState.Has(XjProgressionCandidateFlags.HighRealm);
			XjStageZeroObservation.RecordCandidateRouting("HighRealm", highRealmDue);
			if (highRealmDue)
			{
				long highRealmSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualHighRealm, 31);
				ProcessHighRealm(actor, annualSnapshot, annualYear, enqueueJinDanCombat);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualHighRealm, highRealmSample);
				annualSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
				candidateState = XjCultivatorCandidateIndex.RefreshProgression(actor, annualYear);
			}

			bool breakthroughDue = candidateState.ShouldProcessBreakthrough(annualYear);
			XjStageZeroObservation.RecordCandidateRouting("Breakthrough", breakthroughDue);
			if (breakthroughDue && ShouldProcessBreakthrough(annualSnapshot))
			{
				long breakthroughSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualBreakthrough, 31);
				ProcessBreakthrough(actor, annualSnapshot, annualYear);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualBreakthrough, breakthroughSample);
				XjCultivatorCandidateIndex.RefreshProgression(actor, annualYear);
			}
		}

		// The scheduler commits the core cursor immediately after this stage. All
		// family/sect/equipment and secondary yearly work is queued independently.
		return false;
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

	private static XjAnnualActorProfile ResolveFinalizeProfile(Actor actor, int fromYearInclusive, int currentYear)
	{
		if (actor?.data == null)
		{
			return new XjAnnualActorProfile(XjRealmSuppression.TierNone, string.Empty, XjAnnualInterest.None);
		}

		int realmTier = ResolveRealmTier(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		XjAnnualInterest interest = XjAnnualInterest.None;
		long actorId = ((BaseSystemData)actor.data).id;
		bool needsPersonalZiFuLingBao = XjFaBaoForgePolicy.NeedsPersonalZiFuLingBao(actor);
		if (realmTier >= XjRealmSuppression.TierZhuJi
			&& (needsPersonalZiFuLingBao
				|| (IsEquipmentMaintenanceDue(actorId, realmTier, fromYearInclusive, currentYear)
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
				&& XjDetectionGate.IsEntityMaintenanceDueBetween(
					XjEntityDetectionJob.SectIdentityRefresh, actorId, fromYearInclusive, currentYear)))
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
		return XjQingXuanKongZhengSystem.HasAnnualInterest(actor, snapshot.RealmId, snapshot.DaoTu);
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
				XjGongFaAptitudeRules.GetRealmGradeCap(realmTier)));
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

	private static bool IsEquipmentMaintenanceDue(
		long actorId,
		int realmTier,
		int fromYearInclusive,
		int currentYear)
	{
		if (realmTier >= XjRealmSuppression.TierJinDan) return true;
		XjEntityDetectionJob job = realmTier >= XjRealmSuppression.TierZiFu
			? XjEntityDetectionJob.ZiFuEquipmentMaintenance
			: XjEntityDetectionJob.ZhuJiEquipmentMaintenance;
		return XjDetectionGate.IsEntityMaintenanceDueBetween(job, actorId, fromYearInclusive, currentYear);
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

	private static void RunSecondaryStep(long actorId, int annualYear, string label, Action action)
	{
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.LogError(
				"[玄鉴][年度次级车道] actor=" + actorId
				+ " year=" + annualYear
				+ " step=" + (label ?? string.Empty)
				+ " ex=" + ex);
		}
	}

	private static string ResolveRealmIdAtYear(Actor actor, int annualYear)
	{
		string currentRealmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (actor?.data == null || annualYear <= 0 || string.IsNullOrWhiteSpace(currentRealmId))
		{
			return currentRealmId ?? string.Empty;
		}

		// Once the requested logical year reaches the latest transition, the live
		// realm is authoritative. Earlier years are reconstructed from persistent
		// threshold timestamps so a lagging secondary lane cannot grant ZiFu/JinDan
		// production before those prerequisites actually existed.
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int currentEnteredYear)
			&& currentEnteredYear > 0
			&& annualYear >= currentEnteredYear)
		{
			return currentRealmId;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanYear, out int shenDanYear)
			&& shenDanYear > 0 && annualYear >= shenDanYear)
		{
			return XjRealmIds.ShenDan;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int jinDanYear)
			&& jinDanYear > 0 && annualYear >= jinDanYear)
		{
			return XjRealmIds.JinDan;
		}
		int ziFuYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(actor);
		if (ziFuYear > 0 && annualYear >= ziFuYear)
		{
			return XjRealmIds.ZiFu;
		}
		int zhuJiYear = XjCultivationStateTransitions.ReadZhuJiEnteredYear(actor);
		if (zhuJiYear > 0 && annualYear >= zhuJiYear)
		{
			return XjRealmIds.ZhuJi;
		}

		// Exact distinction below ZhuJi does not affect secondary production; use
		// LianQi as the safe non-forging historical realm for an already-qualified
		// cultivator whose older transition timestamp is unavailable.
		return XjRealmIds.LianQi;
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

	private static void ProcessMaintenanceIdentityStage(Actor actor, int fromYearInclusive, int currentYear)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		int realmTier = ResolveRealmTier(actor);
		bool isZiFuOrAbove = realmTier >= XjRealmSuppression.TierZiFu;
		bool isYaoXie = XjTrueDamageSystem.IsJinXingYaoXie(actor);

		if (isZiFuOrAbove)
		{
			XjSectHighRealmResidenceSystem.Enforce(actor, currentYear);
		}
		if (!XjLongShuSystem.IsLongShu(actor))
		{
			if (isZiFuOrAbove)
			{
				bool hasConfirmedFamily = XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity identity)
					&& identity.Found
					&& identity.FamilyStableIdValue > 0L;
				if (!hasConfirmedFamily
					|| XjFamilyMemberIndex.Shared.IsActorPending(actorId)
					|| !actor.hasClan())
				{
					XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
				}
			}
			else if (XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.FamilyIdentityRepair, actorId, fromYearInclusive, currentYear)
				&& !XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out _)
				&& !XjFamilyMemberIndex.Shared.IsActorPending(actorId))
			{
				XjFamilyMemberIndex.Shared.AddActorToFamily(actor);
			}
		}

		if (isZiFuOrAbove
			|| XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.FamilyBranchAndSurname, actorId, fromYearInclusive, currentYear))
		{
			if (XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity maintenanceIdentity)
				&& maintenanceIdentity.Found)
			{
				XjFamilyMemberIndex.Shared.ReconcileCityBranch(actor, currentYear);
				XjFamilySurnamePolicy.EnsureForConfirmedActor(actor);
			}
		}
		if (!isYaoXie && actor.hasTrait("madness"))
		{
			XjVisibleTraitSync.EnsureCultivatorNoMadness(actor);
		}
		if (isZiFuOrAbove
			|| (realmTier >= XjRealmSuppression.TierZhuJi
				&& HasZongMenIdentity(actor)
				&& XjDetectionGate.IsEntityMaintenanceDueBetween(
					XjEntityDetectionJob.SectCityObservation, actorId, fromYearInclusive, currentYear)))
		{
			XjZongMenCultivatorCityIndex.Observe(actor);
		}
		if (isZiFuOrAbove
			&& XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.QiuJinWarehouseReconcile, actorId, fromYearInclusive, currentYear))
		{
			XjQiuJinFaWarehouseReconciler.ReconcileActor(actor, currentYear);
		}
	}

	private static void ProcessMaintenanceAncillaryStage(Actor actor, int fromYearInclusive, int currentYear)
	{
		XjAnnualActorProfile profile = ResolveProgressionProfile(actor);
		long actorId = ((BaseSystemData)actor.data).id;
		if (profile.RealmTier > XjRealmSuppression.TierNone
			&& XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.TalismanDistribution, actorId, fromYearInclusive, currentYear))
		{
			long talismanSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualTalisman, 31);
			XjTalismanDistributionSystem.TickActor(actor, currentYear);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualTalisman, talismanSample);
		}
		if (profile.RealmTier > XjRealmSuppression.TierNone
			&& XjDetectionGate.TryResolveLatestEntityMaintenanceYear(
				XjEntityDetectionJob.ThreeBookSocialObservation,
				actorId,
				fromYearInclusive,
				currentYear,
				out int socialYear))
		{
			long socialSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualThreeBookSocial, 31);
			XjThreeBookSocialObserver.TickActor(actor, socialYear);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualThreeBookSocial, socialSample);
		}
	}

	private static void ProcessMaintenanceAssetsStage(Actor actor, int fromYearInclusive, int currentYear)
	{
		XjAnnualActorProfile profile = ResolveFinalizeProfile(actor, fromYearInclusive, currentYear);
		if (profile.Has(XjAnnualInterest.FaBao))
		{
			XjEquipmentForgeConsumer.ReconcileManagedItemLimit(actor, profile.RealmId);
			XjFaBaoEquipmentSync.TryBorrowFamilyFaBao(actor, profile.RealmId, currentYear);
			XjFaBaoEquipmentSync.TryEnsureGeneratedEquipment(actor);
		}
		if (profile.Has(XjAnnualInterest.AutoCollect)) XjAutoCollectSystem.TickActor(actor, profile.RealmId);
		if (profile.Has(XjAnnualInterest.ZongMen)) ProcessZongMen(actor, profile.RealmId, fromYearInclusive, currentYear);
		if (profile.Has(XjAnnualInterest.JinDan))
		{
			XjGuoWeiRegistry.ReconcileLiveActor(actor);
		}
	}

	private static bool ShouldAttemptLostFaBaoDiscovery(Actor actor, int currentYear)
	{
		if (!XjFamilyFaBaoWarehouse.HasLostEntriesAtOrBeforeYear(currentYear)
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
		int previousYear = 0;
		if (currentYear > 0)
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLastCultivationYear, out int lastYear))
			{
				previousYear = lastYear;
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

		XjStageZeroObservation.RecordCultivationGrowth(previousYear, currentYear, elapsedYears);
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
		bool promoted = result.CanPromote
			&& XjCultivationStateTransitions.TrySetRealm(actor, targetRule.RealmId, true);
		XjStageZeroObservation.RecordBreakthroughResult(
			targetRule.RealmId,
			promoted ? result.ReasonCode : (result.CanPromote ? "RealmWriteRejected" : result.ReasonCode),
			promoted);
		if (!promoted)
		{
			return;
		}

		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
			actor,
			targetRule.RealmId,
			currentYear,
			syncVisibleTraits: false,
			restoreHealth: false);

		XjActorCultivationSnapshot postSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
		string postDaoTu = postSnapshot.DaoTu;
		if (string.IsNullOrWhiteSpace(postDaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(postDaoTu.Trim(), out _))
		{
			XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out postDaoTu);
			postSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
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
			postSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
			postDaoTu = postSnapshot.DaoTu;
			XjGongFaProgression.EnsureEntryGongFa(actor, postSnapshot);
		}

		if (string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			// 旧档或跨版本角色可能在筑基阶段漏写了首门仙基。紫府公告前
			// 只按真实功法映射补写，失败则不发布带“未知”占位的公告。
			XjZiFuProgression.EnsureZhuJiFoundationXianJi(actor, postDaoTu, currentYear);
			postSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
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

	private static void ProcessZongMen(Actor actor, string realmId, int fromYearInclusive, int currentYear)
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
			|| XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.SectWarehouseReconcile, actorId, fromYearInclusive, currentYear);
		if (needsWarehouseReconcile)
		{
			XjGongFaWarehouseReconciler.ReconcileActor(actor, currentYear);
			XjQianKunDaiSystem.UpdateState(actor);
		}
	}

}
