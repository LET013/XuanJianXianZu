using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修角色状态的唯一写入口。释修只复用玄鉴的年度调度、角色索引、死亡归档
/// 与战斗位格，不复用仙道真元、功法、采气、仙基、求金、金丹或服气本命核心。
/// </summary>
internal static class XjShiState
{
	internal static bool TryBuildSnapshot(Actor actor, out XjShiSnapshot snapshot)
	{
		snapshot = default;
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiPractice, out float practice);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLawIds, out string lawIds);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiEntrySource, out string entrySource);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiRealmEnteredYear, out int realmEnteredYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiLastAnnualYear, out int lastAnnualYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCurrentLife, out int currentLife);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCompletedLives, out int completedLives);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPatronActorId, out string patronActorId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiSeatId, out string seatId);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiSeatProgress, out float seatProgress);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiBorrowedPower, out int borrowedPower);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiAlignment, out int alignment);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPositionStatus, out string positionStatus);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiIsMoHeLiangLi, out int isMoHeLiangLi);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiJinDiId, out string jinDiId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiJinDiStatus, out string jinDiStatus);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthAnchorId, out string rebirthAnchorId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthTargetRealm, out string rebirthTargetRealm);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthTargetSeat, out string rebirthTargetSeat);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthState, out string rebirthState);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiRebirthRecovery, out float rebirthRecovery);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiTrueSpiritLocked, out int trueSpiritLocked);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiSuccessionSourceActorId, out string successionSourceActorId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiSuccessionEligibleYear, out int successionEligibleYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string dharmaFormStage);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiVowId, out string vowId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiResponseBodyRisk, out int responseBodyRisk);

		snapshot = new XjShiSnapshot(
			XjShiCatalog.IsKnownTradition(tradition) && XjShiCatalog.IsKnownRealm(realm),
			tradition, realm, practice, lawIds, entrySource, realmEnteredYear, lastAnnualYear,
			currentLife, completedLives, patronActorId, seatId, seatProgress, borrowedPower,
			alignment, positionStatus, domainId, isMoHeLiangLi, jinDiId, jinDiStatus, rebirthAnchorId,
			rebirthTargetRealm, rebirthTargetSeat, rebirthState, rebirthRecovery,
			trueSpiritLocked, successionSourceActorId, successionEligibleYear,
			dharmaFormStage, vowId, responseBodyRisk);
		return snapshot.Found;
	}

	internal static bool TryEnter(Actor actor, string tradition, int currentYear, string entrySource)
	{
		return TryEnter(actor, tradition, currentYear, entrySource, 0L, string.Empty, string.Empty);
	}

	internal static bool TryEnter(Actor actor, string tradition, int currentYear, string entrySource,
		long masterActorId, string lineageId, string lawIds, bool manualOverride = false,
		bool ignoreFavoredDaoTuLock = false)
	{
		if (actor?.data == null || !actor.isAlive() || !XjShiCatalog.IsKnownTradition(tradition)) return false;
		// 宋玄固定紫府金丹·玄雷，不允许释修事务先清空仙道状态再在末端失败。
		if (XjSongXuanEasterEggSystem.IsSongXuan(actor) || XjZhangYanEasterEggSystem.IsZhangYan(actor)) return false;
		if (!XjShiEntrySystem.CanAddLivingTradition(actor, tradition)) return false;
		if (!XjCultivationEligibility.CanCultivate(actor)) return false;
		if (XjCultivationPathRules.IsShi(actor))
		{
			XjAptitudeTraitLifecycle.ClearForShiCommitment(actor);
			return TrySetTradition(actor, tradition, syncVisibleTraits: true);
		}
		if (!CanReplaceExistingCultivation(actor, manualOverride, ignoreFavoredDaoTuLock)) return false;

		ArchiveFormerCultivation(actor);
		XjCultivationPathTransitions.ClearAll(actor);
		ClearImmortalCultivationAuthority(actor);
		// 转入释修时先清掉旧仙修修法留下的伴生投影；提交释修身份后，
		// XjShiVisibleTraitSync 会按释修自身战斗等效境界重新投影同阶原生特质。
		XjVisibleTraitSync.ClearCultivationDerivedNativeTraits(actor);
		// 选定释门即放下仙道根骨：释修种子与 XjZz 不允许保留为可恢复的并行资格。
		XjAptitudeTraitLifecycle.ClearForShiCommitment(actor);

		int year = Math.Max(1, currentYear > 0 ? currentYear : XjAnnualExecutionContext.ResolveYear(actor));
		long actorId = ((BaseSystemData)actor.data).id;
		string resolvedEntrySource = string.IsNullOrWhiteSpace(entrySource)
			? XjShiSourceIds.ManualRecord : entrySource.Trim();
		string resolvedLineage = XjShiLineageIds.IsKnown(lineageId)
			? lineageId.Trim()
			: XjShiLineageIds.ResolveDefault(tradition, actorId);
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& string.Equals(resolvedEntrySource, XjShiSourceIds.ManualRecord, StringComparison.Ordinal)
			&& !XjShiLineageIds.IsConcreteModern(resolvedLineage))
		{
			resolvedLineage = XjShiLineageIds.ResolveManualModern(actorId);
		}
		_ = lawIds; // 旧调用参数仅用于二进制兼容；释修不立功法。
		if (!XjCultivationPathTransitions.SetPathMetadataForAuthorityTransition(actor, XjCultivationPathIds.Shi)) return false;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTradition, tradition);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRealm, XjShiRealmIds.Monk);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, 0f);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLawIds, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiEntrySource, resolvedEntrySource);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLineageId, resolvedLineage);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiMasterActorId,
			masterActorId > 0L ? masterActorId.ToString(CultureInfo.InvariantCulture) : string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastPreachingYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConvertedCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaRuleVersion, XjShiSentientConsumptionSystem.CurrentDuhuaRuleVersion);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDuhuaLedgerKeys, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAnnualCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDebt, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoLastScheduledYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAncientDuhuaLastYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAncientDuhuaCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAffinityConfirmed, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiRealmEnteredYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastPromotionYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiFateDirectLeapBand, 0);
		InitializeRelationshipFields(actor);
		XjShiMingShuSystem.Clear(actor);
		XjShiMingShuSystem.InitializeFromOrdinaryFate(actor);

		// 释修只看命数、师承与释门因缘，不再保留任何仙道资质。
		XjManualCultivationWake.EnsureAwake(actor, ensureMinimumAptitude: false, registerActor: false);
		RefreshRuntime(actor, syncVisibleTraits: true);
		XjShiSectPolicy.EnforceDetached(actor, year);
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			XjZhantanlinSystem.OnBecameModern(actor, year);
		}
		XjShiPracticeDirectionSystem.EnsureForActor(actor, year);
		XjShiEntrySystem.NotifyShiEntered();
		string masterName = masterActorId > 0L && XjActorRegistry.ResolveKnownOrWorld(masterActorId, out Actor master)
			&& master?.data != null ? master.getName() : string.Empty;
		string masterText = string.IsNullOrWhiteSpace(masterName) ? string.Empty : "，拜" + masterName + "为师";
		if (!string.Equals(resolvedEntrySource, XjShiSourceIds.Conversion, StringComparison.Ordinal))
		{
			XjWorldHistoryStore.RecordActorEvent(actor,
				"入释门" + masterText + "，循" + XjShiCatalog.GetTraditionDisplay(tradition)
					+ "·" + XjShiCatalog.GetLineageDisplay(resolvedLineage) + "修持，择"
					+ XjShiPracticeDirectionSystem.GetDisplay(actor, year) + "，初为僧侣。",
				XjShiTraitIds.Monk);
		}
		if (!string.Equals(resolvedEntrySource, XjShiSourceIds.Conversion, StringComparison.Ordinal))
		{
			XjThreeBookWriter.RecordShiEntered(actor, year, tradition, resolvedLineage,
				resolvedEntrySource, masterActorId, masterName);
		}
		XjShiAnnouncementSystem.OnEntered(actor, tradition,
			XjShiPracticeDirectionSystem.GetDisplay(actor, year));
		return true;
	}

	internal static bool TryEnterThroughTeacher(Actor student, Actor teacher, int currentYear, string entrySource)
	{
		if (student?.data == null || teacher?.data == null || ReferenceEquals(student, teacher)
			|| !teacher.isAlive() || !TryBuildSnapshot(teacher, out XjShiSnapshot teacherSnapshot)
			|| !string.Equals(teacherSnapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(teacherSnapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster)) return false;

		// 古释依原著只修证己身，不以师承/度化把他人纳入释门；古释的外向事务仅为清静点化命数。

		int sourceTier = XjRealmSuppression.GetRealmTier(student);
		if (sourceTier > XjRealmSuppression.TierLianQi)
		{
			return XjShiConversionSystem.TryConvertThroughTeacher(student, teacher, currentYear, entrySource);
		}

		XjActorAccessor.TryGetString(teacher, XjActorDataKeys.ShiLineageId, out string lineageId);
		long teacherId = ((BaseSystemData)teacher.data).id;
		return TryEnter(student, teacherSnapshot.Tradition, currentYear, entrySource,
			teacherId, lineageId, string.Empty);
	}

	internal static bool CanEnterThroughTeacher(Actor student, Actor teacher, int currentYear)
	{
		if (student?.data == null || teacher?.data == null || ReferenceEquals(student, teacher)
			|| !student.isAlive() || !teacher.isAlive()
			|| !TryBuildSnapshot(teacher, out XjShiSnapshot teacherSnapshot)
			|| !string.Equals(teacherSnapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return false;
		if (!XjShiEntrySystem.CanAddLivingModernShi(student)) return false;
		int sourceTier = XjRealmSuppression.GetRealmTier(student);
		if (sourceTier > XjRealmSuppression.TierLianQi)
			return XjShiConversionSystem.CanEnterThroughTeacher(student, teacher, currentYear);
		return CanEnter(student);
	}

	internal static bool CanEnter(Actor actor)
	{
		return actor?.data != null && actor.isAlive() && !XjCultivationPathRules.IsShi(actor)
			&& XjCultivationEligibility.CanCultivate(actor) && CanReplaceExistingCultivation(actor);
	}

	internal static bool TrySetTradition(Actor actor, string tradition, bool syncVisibleTraits)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)
			|| !XjShiCatalog.IsKnownTradition(tradition)) return false;
		if (!XjShiEntrySystem.CanAddLivingTradition(actor, tradition)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			&& (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
				|| string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))) return false;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTradition, tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string currentLineage);
		long actorId = ((BaseSystemData)actor.data).id;
		if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLineageId, XjShiLineageIds.NorthWorldHonored);
		}
		else if (string.Equals(currentLineage, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal)
			|| !XjShiLineageIds.IsKnown(currentLineage))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLineageId,
				XjShiLineageIds.ResolveDefaultModern(actorId));
		}
		int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		XjShiWorldRegistry.Invalidate();
		RefreshRuntime(actor, syncVisibleTraits);
		// 古/今释会改变金丹级统一品秩，必须立即刷新热缓存。
		XjCombatHotPathCache.Refresh(actor);
		XjShiSectPolicy.EnforceDetached(actor, year);
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			XjZhantanlinSystem.OnBecameModern(actor, year);
		}
		else
		{
			// 古释以内证金地立身，改回古释时立即解除旃檀林迁入标记。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinFirstEntry, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinNextReturnYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear, 0);
			if (XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm))
			{
				XjShiDomainState.EnsureAncientSelfProvedJinDi(actor, year);
			}
			else
			{
				ClearZhantanlinAnchorForAncient(actor);
			}
		}
		XjShiPracticeDirectionSystem.EnsureForActor(actor, year);
		XjZhantanlinSystem.SynchronizeSanctuaryPeace(actor);
		return true;
	}

	internal static bool TrySetRealm(Actor actor, string realm, int currentYear, bool manualOverride, bool updateWorldRegistry = true, bool emitNarrative = true)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor) || !XjShiCatalog.IsKnownRealm(realm)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string lockedCurrentRealm);
		// 世尊只能由高位证道事务或明确调试入口写入；普通境界晋升仍不能旁路生成。
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)
			&& !string.Equals(lockedCurrentRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)
			&& !manualOverride) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (XjZhantanlinSystem.RequiresPlacedConfinement(tradition, realm)
			&& !XjZhantanlinSystem.IsPlaced) return false;
		if ((string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
				|| string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
			&& !string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return false;

		int year = Math.Max(1, currentYear > 0 ? currentYear : XjAnnualExecutionContext.ResolveYear(actor));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string previousRealm);
		if (string.Equals(previousRealm, realm, StringComparison.Ordinal))
		{
			EnsureRealmSpecificState(actor, realm, year);
			if (updateWorldRegistry) XjShiWorldRegistry.Invalidate();
			RefreshRuntime(actor, syncVisibleTraits: true);
			XjCombatHotPathCache.Refresh(actor);
			XjZhantanlinSystem.EnforceActor(actor, year);
			return true;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRealm, realm);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiRealmEnteredYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastPromotionYear, year);
		EnsureRealmSpecificState(actor, realm, year);
		if (updateWorldRegistry) XjShiWorldRegistry.Invalidate();
		RefreshRuntime(actor, syncVisibleTraits: true);
		XjCombatHotPathCache.Refresh(actor);
		XjZhantanlinSystem.EnforceActor(actor, year);
		if (emitNarrative)
		{
			XjWorldHistoryStore.RecordActorEvent(actor,
				"释修有成，由" + XjShiCatalog.GetRealmDisplay(previousRealm)
					+ "进至" + XjShiCatalog.GetRealmDisplay(realm) + "。",
				XjShiCatalog.GetRealmTraitId(realm));
			XjAutoCollectSystem.TryCollectShiRealm(actor, realm, "ShiRealmPromotion");
			XjThreeBookWriter.RecordShiRealmChanged(actor, year, previousRealm, realm, manualOverride);
			XjShiAnnouncementSystem.OnRealmChanged(actor, previousRealm, realm);
		}
		return true;
	}

	internal static void GrantPracticeEvent(Actor actor, float amount)
	{
		if (actor?.data == null || amount <= 0f || !XjCultivationPathRules.IsShi(actor)) return;
		// 0.9.9.2：事件修持与年度自然修持共用今释位阶效率，避免事件收益绕过降速。
		if (TryBuildSnapshot(actor, out XjShiSnapshot snapshot))
		{
			amount *= ResolveTraditionPracticePace(snapshot);
		}
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiPractice, out float current);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, Math.Max(0f, current) + amount);
	}

	/// <summary>旧调用兼容入口；新版本只写释修命数待结算，不再折算为修持。</summary>
	internal static void GrantFateEvent(Actor actor, float amount)
	{
		XjShiMingShuSystem.QueueEvent(actor, amount);
	}

	internal static void PrepareActor(Actor actor, int annualYear)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return;
		EnsureConsistent(actor, annualYear);
		XjShiSentientConsumptionSystem.EnsureDuhuaRule(actor);
		// 三书只在修行事务/年度准备阶段补一次旧档基线，绝不在打开UI时造史。
		XjThreeBookWriter.EnsureShiBiographyBaseline(actor, Math.Max(1, annualYear));
		XjShiWorldRegistry.EnsureYear(annualYear);
	}

	internal static void TickActor(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0 || !XjCultivationPathRules.IsShi(actor)) return;
		if (!TryBuildSnapshot(actor, out XjShiSnapshot snapshot) || snapshot.LastAnnualYear == annualYear) return;

		int elapsedYears = snapshot.LastAnnualYear > 0 && annualYear > snapshot.LastAnnualYear
			? Math.Max(1, annualYear - snapshot.LastAnnualYear) : 1;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastAnnualYear, annualYear);
		XjShiMingShuSystem.TickAnnual(actor, annualYear);
		if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			XjAncientShiVowSystem.TickActor(actor, snapshot, annualYear);
		}
		XjAncientShiTempleSystem.TickActor(actor, snapshot, annualYear);
		float templePracticeMultiplier = string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			? XjAncientShiTempleSystem.GetPracticeMultiplier(actor) : 1f;
		float gained = ResolveAnnualPracticeGain(actor, snapshot, annualYear)
			* XjShiPracticeDirectionSystem.GetPracticeMultiplier(actor, annualYear)
			* templePracticeMultiplier * elapsedYears;
		float nextPractice = Math.Max(0f, snapshot.Practice + gained);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, nextPractice);
		XjShiEntrySystem.TryAnnualDuhua(actor, annualYear);

		if (string.Equals(snapshot.RebirthState, XjShiRebirthStateIds.Recovering, StringComparison.Ordinal))
		{
			if (ProcessRebirthRecovery(actor, snapshot, gained, annualYear)) return;
		}

		// 宿世命数直证与普通修持晋升分离。它只在命数跨档时判定一次，
		// 因此可以极少量地产生数世摩诃乃至法相，却不会因为活得够久而人人中奖。
		if (!string.Equals(snapshot.RebirthState, XjShiRebirthStateIds.Recovering, StringComparison.Ordinal)
			&& XjShiFateBreakthroughSystem.TryResolve(actor, snapshot, annualYear)) return;

		if (string.Equals(snapshot.Realm, XjShiRealmIds.Monk, StringComparison.Ordinal))
		{
			if (nextPractice >= XjShiCatalog.DharmaMasterPracticeThreshold
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiAffinityConfirmed, out int affinity)
				&& affinity > 0)
			{
				TrySetRealm(actor, XjShiRealmIds.DharmaMaster, annualYear, manualOverride: false);
				return;
			}
		}
		else if (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal))
		{
			if (ProcessDharmaMaster(actor, snapshot, nextPractice, annualYear)) return;
		}
		else if (string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			if (ProcessLianMin(actor, snapshot, gained, nextPractice, annualYear)) return;
		}
		else if (string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
			|| string.Equals(snapshot.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			|| string.Equals(snapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
		{
			EnsureHighRealmDomain(actor, annualYear);
			XjShiHighRealmSystem.EnsureActor(actor, annualYear);
			if (!actor.isAlive()) return;
			XjShiExpansionSystem.TickHighRealmExpansion(actor, annualYear);
		}

		RefreshRuntime(actor, syncVisibleTraits: false);
	}

	internal static bool ApplyReincarnation(Actor actor, XjShiReincarnationPayload payload,
		long sourceActorId, string sourceActorName, int currentYear)
	{
		if (actor?.data == null || payload == null || !actor.isAlive()) return false;
		if (XjSongXuanEasterEggSystem.IsSongXuan(actor) || XjZhangYanEasterEggSystem.IsZhangYan(actor)) return false;
		int year = Math.Max(1, currentYear);
		XjCultivationPathTransitions.ClearAll(actor);
		ClearImmortalCultivationAuthority(actor);
		XjVisibleTraitSync.ClearCultivationDerivedNativeTraits(actor);
		if (!XjCultivationPathTransitions.SetPathMetadataForAuthorityTransition(actor, XjCultivationPathIds.Shi)) return false;
		if (!string.IsNullOrWhiteSpace(payload.BaseName))
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, payload.BaseName.Trim());
		string restoredTradition = XjShiCatalog.IsKnownTradition(payload.Tradition)
			? payload.Tradition : XjShiTraditionIds.Modern;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTradition, restoredTradition);
		string identityRootActorId = string.IsNullOrWhiteSpace(payload.IdentityRootActorId)
			? sourceActorId.ToString(CultureInfo.InvariantCulture)
			: payload.IdentityRootActorId.Trim();
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiIdentityRootActorId, identityRootActorId);
		bool directMoHeReincarnation = !payload.IsTrueSpiritReturn
			&& string.Equals(restoredTradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& string.Equals(payload.PreviousRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRealm,
			directMoHeReincarnation ? XjShiRealmIds.MoHe : XjShiRealmIds.DharmaMaster);
		// 摩诃主动转世是同一人物续入下一世：直接保持摩诃境界并继承全部修持。
		// 真灵归返按死亡前真实境界原位重塑；只有更低位的兼容恢复链才从法师阶段接续。
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice,
			Math.Max(XjShiCatalog.DharmaMasterPracticeThreshold, payload.PreviousPractice));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLawIds, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPracticeDirectionId,
			XjShiPracticeDirectionIds.IsKnown(payload.PracticeDirectionId) ? payload.PracticeDirectionId : string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPracticeDirectionSource, payload.PracticeDirectionSource ?? string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiPracticeDirectionConfirmedYear, year);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiEntrySource, XjShiSourceIds.Reincarnation);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLineageId, payload.LineageId ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiMasterActorId, payload.MasterActorId ?? string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastPreachingYear, 0);
		// “度化”只统计真实死亡；v3展示人数按一具肉身记十人。转世继承时按旧规则版本归一化。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaRuleVersion, XjShiSentientConsumptionSystem.CurrentDuhuaRuleVersion);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDuhuaLedgerKeys, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAnnualCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDebt, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoLastScheduledYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAncientDuhuaLastYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAncientDuhuaCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConvertedCount,
			XjShiSentientConsumptionSystem.NormalizeInheritedDuhuaCount(
				payload.PreviousDuhuaRuleVersion, payload.PreviousConvertedCount));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSentientConsumptionCount,
			Math.Max(0, payload.PreviousSentientConsumptionCount));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPatronActorId, payload.PatronActorId ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, string.Empty);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiSeatProgress, 0f);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAlignment, Math.Clamp(payload.PreviousAlignment, 0, 100));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.ReincarnationReserved);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainMigrationVersion,
			string.IsNullOrWhiteSpace(payload.DomainId) ? 0 : XjShiDomainState.CurrentMigrationVersion);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainLinkSeveredUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainShockUntilYear,
			year + XjShiCatalog.DomainShockYears);
		string restoredDomainId = string.IsNullOrWhiteSpace(payload.DomainId) ? payload.JinDiId ?? string.Empty : payload.DomainId;
		bool lowRealmModernReturn = payload.IsTrueSpiritReturn
			&& string.Equals(restoredTradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& XjShiCatalog.GetRank(payload.PreviousRealm) < XjShiCatalog.GetRank(XjShiRealmIds.LianMin);
		if (lowRealmModernReturn)
		{
			restoredDomainId = XjShiDomainState.EnsureZhantanlin(year).DomainId;
		}
		else if ((payload.IsTrueSpiritReturn || directMoHeReincarnation)
			&& string.Equals(restoredTradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& XjShiDomainState.TryGet(restoredDomainId, out XjShiDomainRecord exactReturnDomain)
			&& exactReturnDomain != null)
		{
			// 怜愍、摩诃按死亡时记录的真灵挂靠金地原位归返，不在重塑时重新选土。
			restoredDomainId = exactReturnDomain.DomainId;
		}
		else
		{
			restoredDomainId = XjShiDomainState.EnsureLegacyRebirthDomain(actor, restoredDomainId,
				restoredTradition, payload.LineageId ?? string.Empty, year);
		}
		string restoredJinDiId = directMoHeReincarnation && !string.IsNullOrWhiteSpace(payload.JinDiId)
			? payload.JinDiId.Trim()
			: restoredDomainId;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, restoredDomainId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, restoredJinDiId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiIsMoHeLiangLi, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.WaitingForRebirth);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId,
			string.IsNullOrWhiteSpace(payload.RebirthAnchorId) ? restoredDomainId : payload.RebirthAnchorId);
		string rebirthTargetRealm = XjShiCatalog.IsKnownRealm(payload.PreviousRealm)
			? payload.PreviousRealm
			: XjShiRealmIds.LianMin;
		if (string.Equals(restoredTradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			if (string.Equals(rebirthTargetRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
				rebirthTargetRealm = XjShiRealmIds.DharmaMaster;
			else if (string.Equals(rebirthTargetRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
				rebirthTargetRealm = XjShiRealmIds.DharmaForm;
		}
		// 摩诃主动转世仍是同一摩诃，只推进世数；法相/世尊若走高位归返则仍由各自恢复链处理。
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthTargetRealm,
			directMoHeReincarnation ? string.Empty : rebirthTargetRealm);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthTargetSeat,
			XjShiCatalog.IsKnownSeat(payload.PreviousSeatId) ? payload.PreviousSeatId : string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthState,
			directMoHeReincarnation ? XjShiRebirthStateIds.Restored : XjShiRebirthStateIds.Recovering);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiRebirthRecovery, 0f);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLocked, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLockUntilYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTrueSpiritLockReason, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSuccessionSourceActorId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSuccessionEligibleYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage, payload.DharmaFormStage ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiVowId, payload.VowId ?? string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowDeclaredYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowProgress, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowLastProgressYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormLastAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTempleMasterFoundationLastYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiWorldHonoredLastAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiWorldHonoredFailureCount, 0);
		int restoredLife = payload.IsTrueSpiritReturn
			? Math.Max(1, Math.Min(9, payload.PreviousCurrentLife))
			: Math.Max(2, Math.Min(9, payload.PreviousCurrentLife + 1));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, restoredLife);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives,
			payload.IsTrueSpiritReturn
				? Math.Max(0, Math.Min(8, payload.PreviousCompletedLives))
				: Math.Max(1, Math.Min(8, restoredLife - 1)));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiRealmEnteredYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastPromotionYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAffinityConfirmed, 1);
		// 摩诃轮回继承前世法脉、修持、命数、法号、尊号与投释来历；普通命数仍
		// 遵守先天最多100，超额自动迁入后天命数。
		XjMingShuState.Normalize(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float currentOrdinaryMingShu);
		if (currentOrdinaryMingShu < payload.PreviousOrdinaryMingShu)
			XjMingShuState.AddAcquired(actor, payload.PreviousOrdinaryMingShu - currentOrdinaryMingShu);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu,
			Math.Max(0f, Math.Min(XjShiCatalog.MaximumShiMingShu, payload.PreviousShiMingShu)));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShuPending,
			Math.Max(0f, payload.PreviousShiMingShuPending));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastExpansionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastJinDiAbsorptionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLiangLiMingShuAwarded, 0);
		// 摩诃转世仍是同一真灵；宿世命数档的已判定状态随真灵继承，不能靠换肉身重抽。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiFateDirectLeapBand,
			Math.Clamp(payload.FateDirectLeapBand, 0, 5));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiHonorificTitle, payload.HonorificTitle ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaName, payload.DharmaName ?? string.Empty);
		XjShiTitleSystem.TransferReincarnationIdentity(actor, sourceActorId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConversionSourceTier, Math.Max(0, payload.ConversionSourceTier));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConversionYear, Math.Max(0, payload.ConversionYear));
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float currentHuiGuang);
		if (payload.SourceHuiGuang > currentHuiGuang)
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, payload.SourceHuiGuang);
		// 收藏只跟随摩诃主动转世，不跟随“死亡后真灵归返/重塑肉身”。
		// 僧侣、法师以及摩诃的死亡恢复，即便旧肉身曾被收藏，也不能把该状态自动带入新身。
		bool previousWasMoHeReincarnation = !payload.IsTrueSpiritReturn
			&& string.Equals(payload.PreviousRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal);
		if (payload.WasFavorite && previousWasMoHeReincarnation)
		{
			try { ((BaseSystemData)actor.data).favorite = true; }
			catch (Exception ex) { XjExceptionDiagnostics.Report("XjShiState.RestoreReincarnationFavorite", ex); }
		}

		// 金地所有权不能在高位归返尚未完成前就提前提交。旧实现先迁所有权、
		// 再抢摩诃/法相位；后半段一旦失败，新肉身会被删除，却留下指向这个
		// 已删除ActorId的金地，之后同一真灵会永久卡在Pending。下面按境界把
		// “位次 + 金地”作为一个小事务提交，失败时回滚所有权。
		if (payload.IsTrueSpiritReturn || directMoHeReincarnation)
		{
			string exactRealm = XjShiCatalog.IsKnownRealm(payload.PreviousRealm)
				? payload.PreviousRealm : XjShiRealmIds.DharmaMaster;
			if (string.Equals(restoredTradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
			{
				if (string.Equals(exactRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
					exactRealm = XjShiRealmIds.DharmaMaster;
				else if (string.Equals(exactRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
					exactRealm = XjShiRealmIds.DharmaForm;
			}
			string exactSeat = XjShiCatalog.IsKnownSeat(payload.PreviousSeatId)
				? payload.PreviousSeatId : string.Empty;
			long newActorId = ((BaseSystemData)actor.data).id;
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRealm, exactRealm);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, exactSeat);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthState, XjShiRebirthStateIds.Restored);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthTargetRealm, string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthTargetSeat, string.Empty);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiRebirthRecovery, 0f);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.Manifest);
			int exactRank = XjShiCatalog.GetRank(exactRealm);
			if (string.Equals(exactRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
			{
				if (TryResolvePatronTrueSpirit(payload.PatronActorId, out long patronId, out Actor patron, out bool patronPending)
					&& patron?.data != null && patron.isAlive()
					&& XjShiWorldRegistry.RegisterOrMoveAttachment(newActorId, patronId, year))
				{
					// 座主若已经转世，立刻把怜愍的挂靠ID迁到同一真灵最新肉身。
					XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPatronActorId,
						patronId.ToString(CultureInfo.InvariantCulture));
					XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus,
						XjShiPositionStatusIds.Attached);
					XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 0);
					XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower,
						XjShiWorldRegistry.ResolveBorrowedPower(exactSeat, patron));
				}
				else
				{
					_ = patronPending;
					XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus,
						XjShiPositionStatusIds.ReincarnationReserved);
				}
			}
			else if (exactRank >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
			{
				bool ancientHighRealm = string.Equals(restoredTradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
				if (ancientHighRealm)
				{
					// 古释高位归返只恢复自身金地与法相位，不创建、借用或短暂经过摩诃位。
					XjShiDomainState.EnsureAncientSelfProvedJinDi(actor, year);
					string ancientFoundationId = ReadString(actor, XjActorDataKeys.ShiDomainId);
					if (exactRank >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)
						&& (string.IsNullOrWhiteSpace(ancientFoundationId)
							|| !XjShiDomainState.TryClaimDharmaFormPosition(ancientFoundationId, newActorId, year))) return false;
				}
				else
				{
					int moHeRank = XjShiCatalog.GetRank(XjShiRealmIds.MoHe);
					int dharmaFormRank = XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm);
					bool hadSourceNorthJinDi = XjShiDomainState.TryGetOwnedNorthJinDi(sourceActorId, out _);

					if (exactRank == moHeRank)
					{
						string moHeDomainId = XjShiDomainState.EnsureZhantanlin(year).DomainId;
						// 108位只属于“仍是摩诃”的真灵。先恢复位，再迁庙主金地；
						// 若位次提交失败，旧真灵的金地完全不动。
						if (!XjShiDomainState.TryClaimReservedMoHePosition(
							moHeDomainId, newActorId, sourceActorId, year)) return false;
						XjShiDomainState.TransferNorthWorldHonoredOwnership(sourceActorId, actor, year);
					}
					else if (exactRank >= dharmaFormRank)
					{
						// 法相/世尊活着时本就不占108摩诃位，归返也绝不能先“借一格”
						// 再升法相，否则死亡的法相反而制造幽灵摩诃席位。
						XjShiDomainState.TransferNorthWorldHonoredOwnership(sourceActorId, actor, year);
						XjShiDomainState.TryGet(restoredDomainId, out XjShiDomainRecord sourceDomain);
						XjShiDomainRecord claimDomain = XjShiDomainState.EnsureModernDharmaFormDomain(
							actor, sourceDomain, year);
						if (claimDomain == null
							|| !XjShiDomainState.TryClaimDharmaFormPosition(claimDomain.DomainId, newActorId, year))
						{
							XjShiDomainState.RollbackNorthWorldHonoredOwnership(
								newActorId, hadSourceNorthJinDi ? sourceActorId : 0L, year);
							return false;
						}
					}
				}
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.None);
			}
			EnsureRealmSpecificState(actor, exactRealm, year);
		}
		// 直属怜愍系于同一真灵而非旧肉身。普通死亡归返和摩诃主动开启下一世
		// 都把旧座主ID迁到新身；主动轮回的新身此时已经恢复摩诃，不再经过重新证位。
		XjShiWorldRegistry.RebindDependents(sourceActorId, actor, year, payload.DependentActorIds);
		XjShiWorldRegistry.Invalidate();
		XjShiPracticeDirectionSystem.EnsureForActor(actor, year);
		RefreshRuntime(actor, syncVisibleTraits: true);

		XjZhantanlinSystem.EnforceActor(actor, year);
		XjWorldHistoryStore.RecordActorEvent(actor,
			payload.IsTrueSpiritReturn
				? actor.getName() + "真灵循所系承载地归返，于释土重塑肉身。"
				: directMoHeReincarnation
					? actor.getName() + "舍去前身，转入第" + restoredLife.ToString(CultureInfo.InvariantCulture)
						+ "世；仍为同一摩诃，姓名、境界、尊号、法号、法脉、修持与承载关系尽承前身。"
					: actor.getName() + "舍去前身，转入第" + restoredLife.ToString(CultureInfo.InvariantCulture)
						+ "世；姓名、尊号、法号与法脉皆承前世不改。",
			XjShiCatalog.GetRealmTraitId(ReadString(actor, XjActorDataKeys.ShiRealm)));
		string rebuiltAnchorName = XjShiDomainState.TryGet(restoredDomainId, out XjShiDomainRecord rebuiltDomain)
			? XjShiDomainCatalog.GetDomainDisplayName(rebuiltDomain) : "所系承载地";
		XjThreeBookWriter.RecordShiReincarnation(actor, year, payload.IsTrueSpiritReturn,
			rebuiltAnchorName, sourceActorName);
		if (payload.IsTrueSpiritReturn)
		{
			XjShiAnnouncementSystem.OnBodyRebuilt(actor.getName(), rebuiltAnchorName);
		}
		return true;
	}


	internal static bool TryReturnJinLianToShiTu(Actor actor, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive()
			|| !TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
			|| !string.Equals(snapshot.SeatId, XjShiSeatIds.JinLian, StringComparison.Ordinal)
			|| snapshot.TrueSpiritLocked > 0
			|| !string.Equals(snapshot.PositionStatus, XjShiPositionStatusIds.Attached, StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(snapshot.DomainId)
			|| !XjShiDomainState.TryGet(snapshot.DomainId, out XjShiDomainRecord domain)
			|| !string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal))
		{
			return false;
		}
		if (!XjShiWorldRegistry.TryResolveActorId(snapshot.PatronActorId, out long patronId)
			|| patronId <= 0L
			|| !XjActorRegistry.ResolveKnownOrWorld(patronId, out Actor patron)
			|| patron?.data == null || !patron.isAlive()
			|| !TryBuildSnapshot(patron, out XjShiSnapshot patronSnapshot)
			|| XjShiCatalog.GetRank(patronSnapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			return false;
		}

		try
		{
			if (!XjZhantanlinSystem.TryResolveRebirthTile(
				snapshot.RebirthAnchorId, snapshot.DomainId, snapshot.PatronActorId,
				((BaseSystemData)actor.data).id, out WorldTile anchorTile)
				|| anchorTile == null)
			{
				return false;
			}
			float maxHealth = XjSafeCore.GetMaxHealthSafe(actor);
			actor.setHealth(Math.Max(1, (int)Math.Ceiling(maxHealth)));
			actor.attackedBy = null;
			// 金莲座归返同样属于跨域实体迁移。统一走领域转移原语，
			// 由 spawnOn 完整重挂地图/Region，并终止旧 AI 任务；不能再在
			// spawnOn 后调用 setCurrentTilePosition 二次覆盖原生落格状态。
			if (!XjHighRealmMovement.TryImmediateDomainTransfer(actor, anchorTile)) return false;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiJinLianLastReturnYear, Math.Max(1, currentYear));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower,
				XjShiWorldRegistry.ResolveBorrowedPower(snapshot.SeatId, patron));
			RefreshRuntime(actor, syncVisibleTraits: false);
			XjWorldHistoryStore.RecordActorEvent(actor,
				"法身在外被毁，凭金莲座形念不退之力归返释土，重聚法身。",
				XjShiTraitIds.LianMin);
			return true;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjShiState.TryReturnJinLianToShiTu", ex);
			return false;
		}
	}

	internal static void ApplyDomainHiddenConsequence(Actor actor, XjShiDomainRecord domain, int annualYear)
	{
		if (actor?.data == null || domain == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return;
		int year = Math.Max(1, annualYear);
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainShockUntilYear,
			Math.Max(year + XjShiCatalog.DomainShockYears,
				ReadInt(actor, XjActorDataKeys.ShiDomainShockUntilYear)));

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPatronActorId, out string patronRaw);
			bool patronAlive = TryResolvePatronTrueSpirit(patronRaw, out long patronId, out Actor patron, out bool patronPending)
				&& patron?.data != null && patron.isAlive();
			if (!patronAlive)
			{
				if (patronPending)
				{
					XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus,
						XjShiPositionStatusIds.ReincarnationReserved);
					XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus,
						XjShiJinDiStatusIds.WaitingForRebirth);
					return;
				}
				ForceDependentFinalDeath(actor, year,
					"直属摩诃真灵俱灭，所借格位与性命同时断绝");
				return;
			}
			if (patronId > 0L && !string.Equals(patronRaw, patronId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPatronActorId, patronId.ToString(CultureInfo.InvariantCulture));
			// 承载地隐世而座主仍在时只收回借力，原依附关系不解除；待同一座主
			// 重新勾连释土／金地后自然恢复，不允许改投其他摩诃。
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus,
				XjShiPositionStatusIds.ReturnedToShiTu);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.Hidden);
			XjWorldHistoryStore.RecordActorEvent(actor,
				"承载释土彻底隐世，借来的格位暂被收回，退归原释土等待座主复明。",
				XjShiTraitIds.LianMin);
			return;
		}

		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiIsMoHeLiangLi, 0);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Displaced);
			XjWorldHistoryStore.RecordActorEvent(actor,
				"所系承载地隐世，摩诃位震荡，暂失承载之力。",
				XjShiTraitIds.MoHe);
		}
		else if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
		{
			bool worldHonored = string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal);
			int lockYears = worldHonored
				? XjShiCatalog.WorldHonoredDisturbanceLockYears
				: XjShiCatalog.YouTanLinDisturbanceLockYears;
			XjShiHighRealmSystem.ApplyTrueSpiritLock(actor, year + lockYears,
				"承载地隐世牵动旃檀林真身");
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainLinkSeveredUntilYear,
				year + lockYears);
			int risk = ReadInt(actor, XjActorDataKeys.ShiResponseBodyRisk);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk,
				Math.Min(10000, risk + XjShiCatalog.YouTanLinDisturbanceResponseBodyRisk));
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string stage);
			if (!worldHonored && string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal))
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage,
					XjShiDharmaFormStageIds.SelfReturned);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, year);
			}
			XjWorldHistoryStore.RecordActorEvent(actor,
				worldHonored
					? "承载地隐世牵动旃檀林真身，世尊真灵暂受封锁，须待承载复明。"
					: "承载地隐世牵动旃檀林真身，法相真灵与应身一并受创。",
				worldHonored ? XjShiTraitIds.WorldHonored : XjShiTraitIds.DharmaForm);
		}
		else if (string.Equals(ReadString(actor, XjActorDataKeys.ShiRebirthState),
			XjShiRebirthStateIds.Recovering, StringComparison.Ordinal))
		{
			XjWorldHistoryStore.RecordActorEvent(actor,
				"轮回复归所系释土隐世，前世修为恢复受阻。",
				XjShiTraitIds.DharmaMaster);
		}
	}

	internal static void EnsureConsistent(Actor actor, int currentYear)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (!XjShiCatalog.IsKnownRealm(realm))
		{
			realm = XjShiRealmIds.Monk;
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRealm, realm);
		}
		if (!XjShiCatalog.IsKnownTradition(tradition))
		{
			// 缺少释统时，仅明确携带古释特质且不处于今释专属境界者按古释恢复。
			bool explicitAncient = actor.hasTrait(XjShiTraitIds.Ancient)
				&& !actor.hasTrait(XjShiTraitIds.Modern);
			bool modernOnlyRealm = string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
				|| string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal);
			tradition = !explicitAncient || modernOnlyRealm
				? XjShiTraditionIds.Modern : XjShiTraditionIds.Ancient;
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTradition, tradition);
		}

		// 古释的合法路径只有僧侣→法师→法相→世尊。旧档若遗留古释怜愍/摩诃，
		// 直接修复境界而不改成今释，也不制造一条假的晋升史。
		if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			int repairYear = Math.Max(1, currentYear);
			if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
			{
				long actorId = ((BaseSystemData)actor.data).id;
				if (actorId > 0L) XjShiWorldRegistry.ReleaseAttachment(actorId, repairYear);
				if (TrySetRealm(actor, XjShiRealmIds.DharmaMaster, repairYear, manualOverride: true,
					updateWorldRegistry: false, emitNarrative: false))
					realm = XjShiRealmIds.DharmaMaster;
			}
			else if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
			{
				if (XjShiHighRealmSystem.TryDebugSeedDharmaForm(actor, repairYear, emitNarrative: false))
					realm = XjShiRealmIds.DharmaForm;
				else if (TrySetRealm(actor, XjShiRealmIds.DharmaMaster, repairYear, manualOverride: true,
					updateWorldRegistry: false, emitNarrative: false))
					realm = XjShiRealmIds.DharmaMaster;
			}
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		if (!XjShiLineageIds.IsKnown(lineageId))
		{
			long actorId = ((BaseSystemData)actor.data).id;
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLineageId,
				XjShiLineageIds.ResolveDefault(tradition, actorId));
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCurrentLife, out int currentLife);
		if (currentLife <= 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, 1);
		else if (currentLife > 9) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, 9);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCompletedLives, out int completedLives);
		if (completedLives < 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, 0);
		else if (completedLives > 8) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, 8);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiRealmEnteredYear, out int enteredYear);
		if (enteredYear <= 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiRealmEnteredYear, Math.Max(1, currentYear));
		EnsureRealmSpecificState(actor, realm, Math.Max(1, currentYear));
		XjShiHighRealmSystem.EnsureActor(actor, Math.Max(1, currentYear));
		if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			if (XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm))
				XjShiDomainState.EnsureAncientSelfProvedJinDi(actor, Math.Max(1, currentYear));
			else
				ClearZhantanlinAnchorForAncient(actor);
		}
		if (!actor.isAlive()) return;
		ClearImmortalCultivationAuthority(actor);
		XjZhantanlinSystem.EnforceActor(actor, Math.Max(1, currentYear));
		RefreshRuntime(actor, syncVisibleTraits: false);
	}

	internal static void ClearDataOnly(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTradition, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRealm, string.Empty);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, 0f);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLawIds, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiEntrySource, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLineageId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiMasterActorId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastPreachingYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConvertedCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaRuleVersion, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDuhuaLedgerKeys, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAnnualCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDebt, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoLastScheduledYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAncientDuhuaLastYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAncientDuhuaCount, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiHonorificTitle, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaName, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPracticeDirectionId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPracticeDirectionSource, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiPracticeDirectionConfirmedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSentientConsumptionCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSentientConsumptionLedgerYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSentientConsumptionLedgerKeys, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConversionSourceTier, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConversionYear, 0);
		InitializeRelationshipFields(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiRealmEnteredYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastPromotionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAffinityConfirmed, 0);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPendingFateEvents, 0f);
		XjShiMingShuSystem.Clear(actor);
		XjShiWorldRegistry.Invalidate();
		XjShiEntrySystem.InvalidatePresence();
		// 退出释修后只按实际位置保留旃檀林临时和睦，避免人物离林后永久残留。
		XjZhantanlinSystem.SynchronizeSanctuaryPeace(actor);
	}

	private static bool ProcessDharmaMaster(Actor actor, XjShiSnapshot snapshot, float nextPractice, int annualYear)
	{
		if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			// 正常路径仍优先进入怜愍体系；但当修持与命数都抵达高位时，法师可极低概率
			// 越过怜愍直接证摩诃。这样即使天下尚无摩诃，也不存在“无人可挂靠→永远无摩诃”的死锁。
			if (TryFateLeapToMoHe(actor, snapshot, nextPractice, annualYear, fromLianMin: false)) return true;
			if (nextPractice < XjShiCatalog.LianMinPracticeThreshold) return false;
			return TryAttachAsLianMin(actor, annualYear, preserveSeat: false);
		}

		if (nextPractice < XjShiCatalog.AncientDharmaFormPracticeThreshold) return false;
		float shiMingShu = XjShiMingShuSystem.GetEffectiveValue(actor, annualYear);
		if (shiMingShu < XjShiCatalog.AncientDharmaFormMinimumMingShu
			|| !XjAncientShiVowSystem.HasDharmaFormReadiness(actor)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		int baseChance = Math.Clamp(900 + (int)Math.Min(2400f, shiMingShu * 8f), 900, 3300);
		int chance = Math.Min(3700, baseChance + XjAncientShiTempleSystem.GetDharmaFormChanceBonusPerTenThousand(actor));
		bool hasYuanZhaoJinDiInsight = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.YuanZhaoAncientJinDiInsight, out int yuanZhaoInsight)
			&& yuanZhaoInsight > 0;
		if (hasYuanZhaoJinDiInsight) chance = Math.Max(chance, 8500);
		if (!XjShiWorldHonoredPosturePolicy.IsGuaranteedWorldHonored(actor))
		{
			int roll = XjDeterministicHash.PositiveIndex(actorId + annualYear, "ancient_shi_self_prove_jindi", 10000);
			if (roll >= chance) return false; // 悟印若本年未应，保留至下次合法自证节点，不按年反复发奖。
		}
		XjShiDomainState.EnsureAncientSelfProvedJinDi(actor, annualYear);
		if (hasYuanZhaoJinDiInsight) XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoAncientJinDiInsight, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId, string.Empty);
		// 古释不走今释九世轮回。慧觉天地并自证应身时，直接由法师跃迁为法相。
		return XjShiHighRealmSystem.TryAttainAncientDharmaForm(actor, annualYear);
	}

	private static bool TryFateLeapToMoHe(Actor actor, in XjShiSnapshot snapshot, float nextPractice,
		int annualYear, bool fromLianMin)
	{
		if (actor?.data == null || !actor.isAlive()
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| !XjZhantanlinSystem.IsPlaced) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, annualYear);
		int liveModernMoHe = XjShiWorldRegistry.GetLiveTraditionRealmCount(
			XjShiTraditionIds.Modern, XjShiRealmIds.MoHe, annualYear);
		bool scarcitySeed = liveModernMoHe <= XjShiCatalog.MoHeScarcityThreshold;
		int chance;

		if (scarcitySeed)
		{
			// 没有摩诃时，法师只要已经走到本应尝试怜愍的节点即可承担开宗补位；
			// 已有1~3位摩诃时，提高命数门槛到怜愍档，继续补到4位后关闭本保底。
			if (nextPractice < XjShiCatalog.LianMinPracticeThreshold) return false;
			float scarcityMingShuFloor = liveModernMoHe <= 0
				? XjShiCatalog.ManualDharmaMasterMingShuFloor
				: XjShiCatalog.ManualLianMinMingShuFloor;
			if (mingShu < scarcityMingShuFloor) return false;
			chance = liveModernMoHe switch
			{
				<= 0 => XjShiCatalog.MoHeScarcity0ChancePerTenThousand,
				1 => XjShiCatalog.MoHeScarcity1ChancePerTenThousand,
				2 => XjShiCatalog.MoHeScarcity2ChancePerTenThousand,
				_ => XjShiCatalog.MoHeScarcity3ChancePerTenThousand
			};
		}
		else
		{
			// 摩诃超过3位后完全恢复原有逻辑：72k修持、300释修命数、极低年判概率。
			if (nextPractice < XjShiCatalog.MoHeFateLeapPracticeThreshold
				|| mingShu < XjShiCatalog.MoHeFateLeapMingShuThreshold) return false;
			int fateExcess = Math.Max(0, (int)Math.Floor(mingShu - XjShiCatalog.MoHeFateLeapMingShuThreshold));
			int practiceExcessSteps = Math.Max(0, (int)Math.Floor(
				(nextPractice - XjShiCatalog.MoHeFateLeapPracticeThreshold) / 3000f));
			chance = XjShiCatalog.MoHeFateLeapBaseChancePerTenThousand
				+ fateExcess * 3 / 10 + Math.Min(80, practiceExcessSteps * 2);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
			chance = XjShiLineagePolicy.ModifyMoHeFateLeapChance(lineageId,
				Math.Min(XjShiCatalog.MoHeFateLeapMaximumChancePerTenThousand, chance));
		}

		string domainId = XjShiDomainState.EnsureZhantanlin(annualYear).DomainId;
		if (!XjShiDomainState.IsDomainAvailableForMoHeClaim(domainId, actorId)) return false;
		if (!XjShiWorldHonoredPosturePolicy.IsGuaranteedWorldHonored(actor))
		{
			int roll = XjDeterministicHash.PositiveIndex(actorId + annualYear,
				scarcitySeed
					? (fromLianMin ? "shi_lianmin_mohe_scarcity_seed_v1" : "shi_master_mohe_scarcity_seed_v1")
					: (fromLianMin ? "shi_lianmin_fate_leap_mohe_v1" : "shi_master_fate_leap_mohe_v1"),
				10000);
			if (roll >= chance) return false;
		}

		if (!XjShiDomainState.TryClaimMoHePosition(domainId, actorId, annualYear)) return false;
		if (fromLianMin) XjShiWorldRegistry.ReleaseAttachment(actorId, annualYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, domainId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, domainId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.Manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Attached);
		if (!TrySetRealm(actor, XjShiRealmIds.MoHe, annualYear, manualOverride: false)) return false;
		XjShiExpansionSystem.OnRealmOrPositionAttained(actor, annualYear, XjShiRealmIds.MoHe,
			string.Empty, selfProvedJinDi: false, becameLiangLi: false);
		XjWorldHistoryStore.RecordActorEvent(actor,
			scarcitySeed
				? "今释摩诃位数尚稀，命数与修持应运相合，不待下位接引，径证摩诃。"
				: "命数与修持骤然相合，不待下位接引，径证摩诃。",
			XjShiTraitIds.MoHe);
		return true;
	}


	private static bool TryAttachAsLianMin(Actor actor, int annualYear, bool preserveSeat)
	{
		if (actor?.data == null || !XjShiWorldRegistry.TryFindAvailablePatron(actor, annualYear, out Actor patron,
			out string domainId, out int alignment)) return false;
		long patronId = ((BaseSystemData)patron.data).id;
		long dependentId = ((BaseSystemData)actor.data).id;
		if (!XjShiWorldRegistry.RegisterOrMoveAttachment(dependentId, patronId, annualYear)) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiSeatId, out string existingSeat);
		string seatId = preserveSeat && XjShiCatalog.IsKnownSeat(existingSeat) ? existingSeat : XjShiSeatIds.SaTuo;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPatronActorId, patronId.ToString(CultureInfo.InvariantCulture));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, seatId);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiSeatProgress, 0f);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAlignment, alignment);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainLinkSeveredUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower, XjShiWorldRegistry.ResolveBorrowedPower(seatId, patron));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Attached);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, domainId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, domainId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.Manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSuccessionSourceActorId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSuccessionEligibleYear, 0);
		if (!string.Equals(ReadString(actor, XjActorDataKeys.ShiRealm), XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			if (!TrySetRealm(actor, XjShiRealmIds.LianMin, annualYear, manualOverride: false, updateWorldRegistry: false))
			{
				XjShiWorldRegistry.ReleaseAttachment(dependentId, annualYear);
				return false;
			}
		}
		else RefreshRuntime(actor, syncVisibleTraits: false);

		XjShiExpansionSystem.OnRealmOrPositionAttained(actor, annualYear, XjShiRealmIds.LianMin,
			seatId, selfProvedJinDi: false, becameLiangLi: false);
		XjWorldHistoryStore.RecordActorEvent(actor,
			"拜入" + patron.getName() + "摩诃位下，得" + XjShiCatalog.GetSeatDisplay(seatId) + "。",
			XjShiTraitIds.LianMin);
		return true;
	}



	private static bool ProcessLianMin(Actor actor, XjShiSnapshot snapshot, float gained,
		float nextPractice, int annualYear)
	{
		// 怜愍与神丹相同，位次与性命只系于最初直属摩诃，不能在座主死亡后
		// 改投他人或争夺摩诃位。旧档遗留的候证／孤位状态也在此收口为绝命。
		bool patronAlive = TryResolvePatronTrueSpirit(snapshot.PatronActorId, out long patronId, out Actor patron, out bool patronPending)
			&& patron?.data != null && patron.isAlive();
		if (!patronAlive)
		{
			if (patronPending)
			{
				// 座主真灵已经归返其所挂靠金地时，怜愍只暂失借力，等待同一真灵重塑。
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower, 0);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 1);
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus,
					XjShiPositionStatusIds.ReincarnationReserved);
				RefreshRuntime(actor, syncVisibleTraits: false);
				return false;
			}
			ForceDependentFinalDeath(actor, annualYear,
				"直属摩诃真灵俱灭，所借格位与性命同时断绝");
			return true;
		}

		if (patronId > 0L && !string.Equals(snapshot.PatronActorId, patronId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPatronActorId, patronId.ToString(CultureInfo.InvariantCulture));
			XjShiWorldRegistry.RegisterOrMoveAttachment(((BaseSystemData)actor.data).id, patronId, annualYear);
		}

		if (XjShiDomainState.IsBorrowingActive(actor, patron, annualYear))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Attached);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.Manifest);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower,
				XjShiWorldRegistry.ResolveBorrowedPower(snapshot.SeatId, patron));
			float seatProgress = Math.Max(0f, snapshot.SeatProgress + gained);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiSeatProgress, seatProgress);
			if (TryFateLeapToMoHe(actor, snapshot, nextPractice, annualYear, fromLianMin: true)) return true;
			if (string.Equals(snapshot.SeatId, XjShiSeatIds.SaTuo, StringComparison.Ordinal)
				&& nextPractice >= XjShiCatalog.FaHuiPracticeThreshold
				&& TryPromoteSeat(actor, XjShiSeatIds.FaHui, patron, annualYear))
			{
				return true;
			}
			if (string.Equals(snapshot.SeatId, XjShiSeatIds.FaHui, StringComparison.Ordinal)
				&& nextPractice >= XjShiCatalog.JinLianPracticeThreshold
				&& TryPromoteSeat(actor, XjShiSeatIds.JinLian, patron, annualYear))
			{
				return true;
			}
			RefreshRuntime(actor, syncVisibleTraits: false);
			return false;
		}

		// 座主仍在但金地／释土暂时隐世时，怜愍退回原承载地等待恢复；
		// 不允许改投其他摩诃，也不凭年度数值自行晋位。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 1);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.ReturnedToShiTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.Hidden);
		RefreshRuntime(actor, syncVisibleTraits: false);
		return false;
	}

	private static void ForceDependentFinalDeath(Actor actor, int annualYear, string reason)
	{
		if (actor?.data == null || !actor.isAlive()) return;
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLocked, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLockUntilYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTrueSpiritLockReason, reason ?? string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 1);
		XjShiWorldRegistry.ReleaseAttachment(actorId, Math.Max(1, annualYear));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Orphaned);
		XjWorldHistoryStore.RecordActorEvent(actor,
			(reason ?? "直属摩诃陨落") + "，怜愍绝命，不入轮回。", XjShiTraitIds.LianMin);
		XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)11, true, XjDeathCause.ScriptedFinality);
	}



	private static bool TryPromoteSeat(Actor actor, string targetSeat, Actor patron, int annualYear)
	{
		if (actor?.data == null || patron?.data == null
			|| !XjShiDomainState.IsBorrowingActive(actor, patron, annualYear)) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiLastPromotionYear, out int lastPromotion);
		if (lastPromotion == annualYear) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiAlignment, out int alignment);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiBorrowedPower, out int borrowedPower);
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		int chance = Math.Clamp(2800 + alignment * 45 + borrowedPower * 150
			+ (int)Math.Min(1200f, XjShiMingShuSystem.GetEffectiveValue(actor, annualYear) * 2f), 2800, 9000);
		chance = XjShiLineagePolicy.ModifySeatPromotionChance(lineageId, chance);
		if (!XjShiWorldHonoredPosturePolicy.IsGuaranteedWorldHonored(actor))
		{
			int roll = XjDeterministicHash.PositiveIndex(actorId + annualYear,
				"shi_seat_promote_" + targetSeat, 10000);
			if (roll >= chance) return false;
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiSeatId, out string previousSeat);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, targetSeat);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiSeatProgress, 0f);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower,
			XjShiWorldRegistry.ResolveBorrowedPower(targetSeat, patron));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastPromotionYear, annualYear);
		RefreshRuntime(actor, syncVisibleTraits: false);
		XjShiExpansionSystem.OnRealmOrPositionAttained(actor, annualYear, XjShiRealmIds.LianMin,
			targetSeat, selfProvedJinDi: false, becameLiangLi: false);
		XjWorldHistoryStore.RecordActorEvent(actor,
			"怜愍位次由" + XjShiCatalog.GetSeatDisplay(previousSeat) + "升为"
			+ XjShiCatalog.GetSeatDisplay(targetSeat) + "。", XjShiTraitIds.LianMin);
		XjShiAnnouncementSystem.OnLianMinSeatPromoted(actor, previousSeat, targetSeat, patron.getName());
		return true;
	}


	private static bool ProcessRebirthRecovery(Actor actor, XjShiSnapshot snapshot, float gained, int annualYear)
	{
		float recovery = Math.Max(0f, snapshot.RebirthRecovery + gained);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiRebirthRecovery, recovery);
		string targetRealm = snapshot.RebirthTargetRealm;
		if (!XjShiCatalog.IsKnownRealm(targetRealm))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthState, XjShiRebirthStateIds.None);
			return false;
		}

		bool ancient = string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
		if (ancient)
		{
			// 古释没有怜愍、摩诃。旧档若把恢复目标写成这两层，直接归一到合法古释路径。
			if (string.Equals(targetRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
			{
				TrySetRealm(actor, XjShiRealmIds.DharmaMaster, annualYear, manualOverride: true,
					updateWorldRegistry: false, emitNarrative: false);
				FinishRebirthRecovery(actor, annualYear);
				return true;
			}
			if (string.Equals(targetRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
			{
				targetRealm = XjShiRealmIds.DharmaForm;
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthTargetRealm, targetRealm);
			}

			if (string.Equals(targetRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
				|| string.Equals(targetRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
			{
				if (recovery < XjShiCatalog.RebirthDharmaFormRecoveryThreshold) return false;
				long actorId = ((BaseSystemData)actor.data).id;
				XjShiDomainState.EnsureAncientSelfProvedJinDi(actor, annualYear);
				string recoveryDomain = ReadString(actor, XjActorDataKeys.ShiDomainId);
				if (string.IsNullOrWhiteSpace(recoveryDomain)
					|| !XjShiDomainState.TryClaimDharmaFormPosition(recoveryDomain, actorId, annualYear))
				{
					XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Displaced);
					return false;
				}
				if (!TrySetRealm(actor, XjShiRealmIds.DharmaForm, annualYear, manualOverride: false)) return false;
				XjShiHighRealmSystem.EnsureActor(actor, annualYear);
				FinishRebirthRecovery(actor, annualYear);
				return true;
			}
			return false;
		}

		if (string.Equals(targetRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			if (recovery < XjShiCatalog.RebirthLianMinRecoveryThreshold) return false;
			if (!TryAttachAsLianMin(actor, annualYear, preserveSeat: false)) return false;
			string restoredSeat = XjShiCatalog.IsKnownSeat(snapshot.RebirthTargetSeat)
				? snapshot.RebirthTargetSeat : XjShiSeatIds.SaTuo;
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, restoredSeat);
			if (XjShiWorldRegistry.TryResolveLiveActor(ReadString(actor, XjActorDataKeys.ShiPatronActorId), out Actor restoredPatron))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower,
					XjShiWorldRegistry.ResolveBorrowedPower(restoredSeat, restoredPatron));
			}
			FinishRebirthRecovery(actor, annualYear);
			return true;
		}

		if (XjShiCatalog.GetRank(targetRealm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			if (recovery < XjShiCatalog.RebirthMoHeRecoveryThreshold) return false;
			long actorId = ((BaseSystemData)actor.data).id;
			string recoveryDomain = string.IsNullOrWhiteSpace(snapshot.DomainId) ? snapshot.JinDiId : snapshot.DomainId;
			if (!XjShiWorldRegistry.IsJinDiAvailableForClaim(recoveryDomain, actorId))
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Displaced);
				return false;
			}
			if (!XjShiDomainState.TryClaimMoHePosition(recoveryDomain, actorId, annualYear)) return false;
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, recoveryDomain);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, recoveryDomain);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.Manifest);
			TrySetRealm(actor, XjShiRealmIds.MoHe, annualYear, manualOverride: false);
			if (string.Equals(targetRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
				|| string.Equals(targetRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
			{
				if (recovery < XjShiCatalog.RebirthDharmaFormRecoveryThreshold) return true;
				// 今释法相复归时仍须恢复同一真灵原有的庙主金地，不能以整座旃檀林替代法相根基。
				XjShiDomainState.TryGet(recoveryDomain, out XjShiDomainRecord sourceDomain);
				XjShiDomainRecord claimDomain = XjShiDomainState.EnsureModernDharmaFormDomain(
					actor, sourceDomain, annualYear);
				if (claimDomain == null)
				{
					XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus,
						XjShiPositionStatusIds.Displaced);
					return false;
				}
				recoveryDomain = claimDomain.DomainId;
				if (!XjShiDomainState.TryClaimDharmaFormPosition(recoveryDomain, actorId, annualYear))
				{
					XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Displaced);
					return false;
				}
				if (!TrySetRealm(actor, XjShiRealmIds.DharmaForm, annualYear, manualOverride: false)) return false;
				XjShiHighRealmSystem.EnsureActor(actor, annualYear);
			}
			FinishRebirthRecovery(actor, annualYear);
			return true;
		}
		return false;
	}

	private static void FinishRebirthRecovery(Actor actor, int annualYear)
	{
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthState, XjShiRebirthStateIds.Restored);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthTargetRealm, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthTargetSeat, string.Empty);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiRebirthRecovery, 0f);
		string restoredRealm = ReadString(actor, XjActorDataKeys.ShiRealm);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus,
			string.Equals(restoredRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
				? XjShiPositionStatusIds.Attached
				: XjShiPositionStatusIds.None);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastPromotionYear, annualYear);
		XjShiWorldRegistry.Invalidate();
		XjWorldHistoryStore.RecordActorEvent(actor, "前尘修为复归，重新稳住释门位次。", XjShiCatalog.GetRealmTraitId(restoredRealm));
	}

	private static void EnsureRealmSpecificState(Actor actor, string realm, int currentYear)
	{
		if (actor?.data == null) return;
		if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiSeatId, out string seatId);
			if (!XjShiCatalog.IsKnownSeat(seatId)) XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, XjShiSeatIds.SaTuo);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPositionStatus, out string status);
			if (string.IsNullOrWhiteSpace(status)) XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Orphaned);
			return;
		}
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
		{
			ClearSeatAuthority(actor);
			EnsureHighRealmDomain(actor, currentYear);
			if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal))
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string stage);
				if (!XjShiDharmaFormStageIds.IsKnown(stage)
					|| string.Equals(stage, XjShiDharmaFormStageIds.None, StringComparison.Ordinal))
					XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage, XjShiDharmaFormStageIds.OriginalVow);
			}
			return;
		}
		if (XjShiCatalog.GetRank(realm) <= XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster))
		{
			ClearSeatAuthority(actor);
		}
	}

	private static void EnsureHighRealmDomain(Actor actor, int currentYear)
	{
		if (actor?.data == null) return;
		// 旧档或新晋高境只触发一次全表迁移；已迁移角色年度运行时不反复重建。
		if (XjShiDomainState.NeedsActorMigration(actor))
		{
			XjShiDomainState.Invalidate();
			XjShiDomainState.ReconcileFromActors(Math.Max(1, currentYear), force: true);
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCurrentLife, out int currentLife);
		if (currentLife <= 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, 1);
		else if (currentLife > 9) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, 9);
	}

	private static void InitializeRelationshipFields(Actor actor)
	{
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPatronActorId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, string.Empty);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiSeatProgress, 0f);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAlignment, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.None);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, string.Empty);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiDomainContribution, 0f);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiIsMoHeLiangLi, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainMigrationVersion, XjShiDomainState.CurrentMigrationVersion);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainLinkSeveredUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainShockUntilYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.None);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthTargetRealm, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthTargetSeat, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthState, XjShiRebirthStateIds.None);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiRebirthRecovery, 0f);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLocked, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLockUntilYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTrueSpiritLockReason, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTempleMaster, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTempleMasterFoundationLastYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSourceHeavenId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiOwnedHeavenFragments, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiRequiredHeavenFragments, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiReformedHeaven, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastHeavenAttractionYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSuccessionSourceActorId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSuccessionEligibleYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage, XjShiDharmaFormStageIds.None);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiVowId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowDeclaredYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowProgress, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowLastProgressYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiYouTanLinAnchorId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinFirstEntry, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinNextReturnYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormCandidateState, XjShiDharmaFormCandidateIds.None);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormCandidateSinceYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness, XjShiWorldHonoredReadinessIds.Locked);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormLastAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiWorldHonoredLastAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiWorldHonoredFailureCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiHighRealmAuditVersion, XjShiCatalog.HighRealmAuditVersion);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastExpansionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastJinDiAbsorptionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLiangLiMingShuAwarded, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiFateDirectLeapBand, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiHonorificTitle, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaName, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPracticeDirectionId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPracticeDirectionSource, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiPracticeDirectionConfirmedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSentientConsumptionCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaRuleVersion, XjShiSentientConsumptionSystem.CurrentDuhuaRuleVersion);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDuhuaLedgerKeys, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAnnualCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDebt, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoLastScheduledYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAncientDuhuaLastYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAncientDuhuaCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSentientConsumptionLedgerYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSentientConsumptionLedgerKeys, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConversionSourceTier, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConversionYear, 0);
	}

	private static void ClearSeatAuthority(Actor actor)
	{
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPatronActorId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, string.Empty);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiSeatProgress, 0f);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.None);
	}

	private static bool IsMoHeOrHigher(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		return XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe);
	}

	/// <summary>
	/// 怜愍挂靠的是“直属摩诃真灵”而不是某一具肉身。法相/世尊仍是同一座主，
	/// 座主转世后也应自动沿真灵链改挂最新肉身。只有整条真灵链既无活体、又无
	/// 待归返记录时，才允许触发怜愍绝命。
	/// </summary>
	private static bool TryResolvePatronTrueSpirit(string rawPatronActorId, out long patronActorId, out Actor patron, out bool pending)
	{
		patronActorId = 0L;
		patron = null;
		pending = false;
		if (!XjShiWorldRegistry.TryResolveActorId(rawPatronActorId, out long parsedId) || parsedId <= 0L) return false;
		patronActorId = parsedId;
		if (XjReincarnation.TryResolveLatestLineageActorId(parsedId, out long latestId) && latestId > 0L)
			patronActorId = latestId;
		if (XjActorRegistry.ResolveKnownOrWorld(patronActorId, out Actor resolved)
			&& IsMoHeOrHigher(resolved))
		{
			patron = resolved;
			return true;
		}
		pending = XjReincarnation.HasPendingShi(patronActorId)
			|| (parsedId != patronActorId && XjReincarnation.HasPendingShi(parsedId));
		return false;
	}

	private static bool CanReplaceExistingCultivation(Actor actor, bool manualOverride = false,
		bool ignoreFavoredDaoTuLock = false)
	{
		if (actor?.data == null) return false;
		if (!ignoreFavoredDaoTuLock && XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out _)) return false;
		if (!manualOverride && XjRealmSuppression.GetRealmTier(actor) > XjRealmSuppression.TierLianQi) return false;
		if (XjCultivationPathRules.TryGetPath(actor, out string path)
			&& !string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal)
			&& !string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal)) return false;
		return true;
	}

	private static void ArchiveFormerCultivation(Actor actor)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.CultivationPath, out string path);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realm);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiFormerCultivationPath, path ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiFormerRealmId, realm ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiFormerDaoTu, daoTu ?? string.Empty);
	}

	private static void ClearImmortalCultivationAuthority(Actor actor)
	{
		if (actor?.data == null) return;
		XjCultivationStateTransitions.ResetIdentityMetadataForAuthorityClear(actor, string.Empty);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, 0f);
		XjGongFaAccessor.Clear(actor);
		XjXianJiAccessor.RestoreSnapshot(actor, string.Empty, 0);
		XjQiuJinFaAccessor.Clear(actor, "ShiCultivationAuthority");
		XjJinDanAccessor.ClearSuccess(actor);
		XjShenDanAccessor.ClearSuccess(actor);
	}


	private static float ResolveAnnualPracticeGain(Actor actor, in XjShiSnapshot snapshot, int annualYear)
	{
		int year = Math.Max(1, annualYear);
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, year);
		// 释修修持只由命数根基与位次借力决定，不读取xjzz或慧光。
		float baseGain = 36f + Math.Min(500f, Math.Max(0f, mingShu)) * 0.60f;
		float positionFactor = string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
			? 1f + Math.Min(5, Math.Max(0, snapshot.BorrowedPower)) * 0.08f : 1f;
		float stabilityFactor = 1f;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDomainShockUntilYear, out int shockUntil);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, out int suppressed);
		if (shockUntil >= year) stabilityFactor *= 0.45f;
		if (suppressed > 0 && (string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
			|| string.Equals(snapshot.RebirthState, XjShiRebirthStateIds.Recovering, StringComparison.Ordinal)))
		{
			stabilityFactor *= 0.55f;
		}
		float traditionPace = ResolveTraditionPracticePace(snapshot);
		return Math.Max(1f, baseGain * positionFactor * stabilityFactor * traditionPace);
	}

	private static float ResolveTraditionPracticePace(in XjShiSnapshot snapshot)
	{
		if (!string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return 1f;

		if (string.Equals(snapshot.Realm, XjShiRealmIds.Monk, StringComparison.Ordinal))
			return XjShiCatalog.ModernMonkPracticePaceMultiplier;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal))
			return XjShiCatalog.ModernDharmaMasterPracticePaceMultiplier;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
			return XjShiCatalog.ModernLianMinPracticePaceMultiplier;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
			return XjShiCatalog.ModernMoHePracticePaceMultiplier;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal))
			return XjShiCatalog.ModernDharmaFormPracticePaceMultiplier;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
			return XjShiCatalog.ModernWorldHonoredPracticePaceMultiplier;

		return XjShiCatalog.ModernDharmaMasterPracticePaceMultiplier;
	}



	/// <summary>
	/// 旃檀林意向的强制摄化入口。优先保留原境界做等位投释；若高位事务因旧档位次
	/// 不完整而中途失败，但道途已经切入释修，则收口为有效今释，避免角色卡在半转换态。
	/// </summary>
	internal static bool TryForceConvertByZhantanlin(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		int year = Math.Max(1, annualYear);
		if (XjCultivationPathRules.IsShi(actor))
		{
			if (!TrySetTradition(actor, XjShiTraditionIds.Modern, syncVisibleTraits: true)) return false;
			EnsureManualIdentity(actor, XjShiTraditionIds.Modern, year);
			XjZhantanlinSystem.OnBecameModern(actor, year);
			return true;
		}

		// 真实踏入旃檀林的紫金/服气高修属于释土强制摄化，不再被“果位钟爱”
		// 的自然投释禁令挡住。该特例只存在于旃檀林边界，特质编辑器与师承仍保持原锁。
		if (XjRealmSuppression.GetRealmTier(actor) > XjRealmSuppression.TierLianQi
			&& XjShiConversionSystem.TryConvertByZhantanlin(actor, year)) return true;
		if (TryApplyManualTraditionRecord(actor, XjShiTraditionIds.Modern, year)) return true;
		if (XjCultivationPathRules.IsShi(actor))
		{
			if (!TrySetTradition(actor, XjShiTraditionIds.Modern, syncVisibleTraits: true)) return false;
			EnsureManualIdentity(actor, XjShiTraditionIds.Modern, year);
			RefreshRuntime(actor, syncVisibleTraits: true);
			XjZhantanlinSystem.OnBecameModern(actor, year);
			return true;
		}
		return false;
	}

	internal static bool TryApplyManualTraditionRecord(Actor actor, string tradition, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjShiCatalog.IsKnownTradition(tradition)) return false;
		int year = Math.Max(1, annualYear);
		if (XjCultivationPathRules.IsShi(actor)
			&& TryBuildSnapshot(actor, out XjShiSnapshot existing)
			&& string.Equals(existing.Tradition, tradition, StringComparison.Ordinal))
		{
			EnsureManualIdentity(actor, tradition, year);
			ApplyManualRealmFloors(actor, existing.Realm);
			XjShiWorldRegistry.Invalidate();
			RefreshRuntime(actor, syncVisibleTraits: true);
			return true;
		}

		// 紫府金丹道与服气养性高修被赋予今／古释时，按原境界等位投释，
		// 不再一律清空为僧侣。自然投释仍须满足师承、命数和真实位次。
		if (!XjCultivationPathRules.IsShi(actor)
			&& XjRealmSuppression.GetRealmTier(actor) > XjRealmSuppression.TierLianQi)
		{
			return XjShiConversionSystem.TryConvertManual(actor, tradition, year);
		}

		// 已是释修时改换今古释，仍属于完整重录：先释放旧位次/承载投影，再从僧侣补录。
		if (XjCultivationPathRules.IsShi(actor))
		{
			XjCultivationPathTransitions.ClearAll(actor);
			XjShiWorldRegistry.Invalidate();
			XjShiDomainState.Invalidate();
		}
		bool entered = TryEnter(actor, tradition, year, XjShiSourceIds.ManualRecord, 0L,
			ResolveManualLineage(tradition, ((BaseSystemData)actor.data).id),
			string.Empty, manualOverride: true);
		if (!entered) return false;
		ApplyManualRealmFloors(actor, XjShiRealmIds.Monk);
		RefreshRuntime(actor, syncVisibleTraits: true);
		return true;
	}

	internal static bool TryApplyManualRealmRecord(Actor actor, string targetRealm, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjShiCatalog.IsKnownRealm(targetRealm)) return false;
		// 双保险：即使未来有别的补录入口直接调用本方法，也不能手动制造法相/世尊。
		if (!XjManualHighRealmGrantPolicy.IsManualRealmRecordAllowed(targetRealm)) return false;
		int year = Math.Max(1, annualYear);
		bool explicitlyAncient = actor.hasTrait(XjShiTraitIds.Ancient)
			&& !actor.hasTrait(XjShiTraitIds.Modern);
		bool targetModernOnly = string.Equals(targetRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
			|| string.Equals(targetRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal);
		string tradition = targetModernOnly || !explicitlyAncient
			? XjShiTraditionIds.Modern : XjShiTraditionIds.Ancient;
		long actorId = ((BaseSystemData)actor.data).id;

		if (TryBuildSnapshot(actor, out XjShiSnapshot existing))
		{
			tradition = targetModernOnly ? XjShiTraditionIds.Modern : existing.Tradition;
			if (string.Equals(targetRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)
				&& XjShiHighRealmSystem.HasLiveWorldHonored(tradition, actorId)) return false;
			if (!string.Equals(existing.Realm, targetRealm, StringComparison.Ordinal)
				|| !string.Equals(existing.Tradition, tradition, StringComparison.Ordinal))
			{
				XjCultivationPathTransitions.ClearAll(actor);
				XjShiWorldRegistry.Invalidate();
				XjShiDomainState.Invalidate();
			}
		}
		else if (string.Equals(targetRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)
			&& XjShiHighRealmSystem.HasLiveWorldHonored(tradition, actorId))
		{
			return false;
		}

		if (!XjCultivationPathRules.IsShi(actor)
			&& !TryEnter(actor, tradition, year, XjShiSourceIds.ManualRecord, 0L,
				ResolveManualLineage(tradition, ((BaseSystemData)actor.data).id),
				string.Empty, manualOverride: true)) return false;

		if (!TrySetTradition(actor, tradition, syncVisibleTraits: false)) return false;
		EnsureManualIdentity(actor, tradition, year);
		ApplyManualRealmFloors(actor, targetRealm);
		if (string.Equals(targetRealm, XjShiRealmIds.Monk, StringComparison.Ordinal))
			return TrySetRealm(actor, targetRealm, year, manualOverride: true);
		if (string.Equals(targetRealm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal))
			return TrySetRealm(actor, targetRealm, year, manualOverride: true);
		if (string.Equals(targetRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			if (!TrySetTradition(actor, XjShiTraditionIds.Modern, syncVisibleTraits: false)) return false;
			if (TryAttachAsLianMin(actor, year, preserveSeat: false)) return true;
			// 世界尚无摩诃时允许补录为孤位萨陲，待承载地出现后再由年度事务归位。
			return TrySetRealm(actor, XjShiRealmIds.LianMin, year, manualOverride: true);
		}
		if (string.Equals(targetRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			if (!TrySetTradition(actor, XjShiTraditionIds.Modern, syncVisibleTraits: false)) return false;
			return TryDebugSeedMoHe(actor, year);
		}
		if (string.Equals(targetRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			|| string.Equals(targetRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
		{
			return TryApplyManualHighRealmRecord(actor, tradition, targetRealm, year);
		}
		return false;
	}

	private static bool TryApplyManualHighRealmRecord(Actor actor, string tradition, string targetRealm, int year)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			if (!TrySetTradition(actor, XjShiTraditionIds.Modern, syncVisibleTraits: false)
				|| !TryDebugSeedMoHe(actor, year))
			{
				return false;
			}
		}
		else if (!TrySetRealm(actor, XjShiRealmIds.DharmaMaster, year,
			manualOverride: true, updateWorldRegistry: false, emitNarrative: false))
		{
			return false;
		}

		if (!XjShiHighRealmSystem.TryDebugSeedDharmaForm(actor, year, emitNarrative: true))
		{
			return false;
		}

		return !string.Equals(targetRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)
			|| XjShiHighRealmSystem.TryDebugAttainWorldHonored(actor, year);
	}

	private static string ResolveManualLineage(string tradition, long actorId)
	{
		return string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			? XjShiLineageIds.NorthWorldHonored
			: XjShiLineageIds.ResolveManualModern(actorId);
	}

	private static void EnsureManualIdentity(Actor actor, string tradition, int annualYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		string resolved = string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			? XjShiLineageIds.NorthWorldHonored
			: XjShiLineageIds.IsConcreteModern(lineageId)
				? lineageId
				: XjShiLineageIds.ResolveManualModern(actorId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTradition, tradition);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLineageId, resolved);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLawIds, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiAffinityConfirmed, 1);
		XjShiPracticeDirectionSystem.EnsureForActor(actor, Math.Max(1, annualYear));
	}

	private static void ApplyManualRealmFloors(Actor actor, string targetRealm)
	{
		float ordinaryFloor;
		float practiceFloor;
		if (string.Equals(targetRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
		{ ordinaryFloor = XjShiCatalog.WorldHonoredMingShuThreshold; practiceFloor = XjShiCatalog.WorldHonoredPracticeThreshold; }
		else if (string.Equals(targetRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal))
		{ ordinaryFloor = XjShiCatalog.DharmaFormManualRecordMinimumMingShu; practiceFloor = XjShiCatalog.DharmaFormPracticeThreshold; }
		else if (string.Equals(targetRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{ ordinaryFloor = XjShiCatalog.ManualMoHeMingShuFloor; practiceFloor = XjShiCatalog.MoHePracticeThreshold; }
		else if (string.Equals(targetRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{ ordinaryFloor = XjShiCatalog.ManualLianMinMingShuFloor; practiceFloor = XjShiCatalog.LianMinPracticeThreshold; }
		else if (string.Equals(targetRealm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal))
		{ ordinaryFloor = XjShiCatalog.ManualDharmaMasterMingShuFloor; practiceFloor = XjShiCatalog.DharmaMasterPracticeThreshold; }
		else
		{ ordinaryFloor = XjShiCatalog.ManualMonkMingShuFloor; practiceFloor = 0f; }

		XjMingShuState.Normalize(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float ordinary);
		if (ordinary < ordinaryFloor) XjMingShuState.AddAcquired(actor, ordinaryFloor - ordinary);
		XjShiMingShuSystem.InitializeFromOrdinaryFate(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiPractice, out float currentPractice);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, Math.Max(currentPractice, practiceFloor));
	}

	internal static bool TryDebugSetLineage(Actor actor, string lineageId, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjShiLineageIds.IsKnown(lineageId)
			|| string.Equals(lineageId, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal)
			|| string.Equals(lineageId, XjShiLineageIds.ModernUnassigned, StringComparison.Ordinal)) return false;
		if (!XjCultivationPathRules.IsShi(actor))
		{
			if (!TryEnter(actor, XjShiTraditionIds.Modern, annualYear, XjShiSourceIds.ManualRecord,
				0L, lineageId, string.Empty)) return false;
		}
		else if (!TryBuildSnapshot(actor, out XjShiSnapshot current)
			|| !string.Equals(current.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			// 调试法脉只在今释内部切换，避免把古释金地与今释应土强行混写。
			return false;
		}
		if (!TrySetTradition(actor, XjShiTraditionIds.Modern, syncVisibleTraits: false)) return false;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLineageId, lineageId);
		XjShiWorldRegistry.Invalidate();
		XjShiDomainState.Invalidate();
		RefreshRuntime(actor, syncVisibleTraits: true);
		return true;
	}

	internal static bool TryDebugSeedMoHe(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)
			|| !TryBuildSnapshot(actor, out XjShiSnapshot snapshot)) return false;
		if (!string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return false;
		int year = Math.Max(1, annualYear);
		long actorId = ((BaseSystemData)actor.data).id;
		string domainId = XjShiDomainState.EnsureZhantanlin(year).DomainId;
		if (!XjShiDomainState.TryClaimMoHePosition(domainId, actorId, year)) return false;
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice,
			Math.Max(snapshot.Practice, XjShiCatalog.MoHePracticeThreshold));
		int projectedLife = Math.Clamp(Math.Max(snapshot.CurrentLife, snapshot.CompletedLives + 1), 1, 9);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, projectedLife);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, Math.Max(0, projectedLife - 1));
		bool promoted = TrySetRealm(actor, XjShiRealmIds.MoHe, year, manualOverride: false);
		if (promoted)
		{
			XjShiExpansionSystem.OnRealmOrPositionAttained(actor, year, XjShiRealmIds.MoHe,
				string.Empty, selfProvedJinDi: false, becameLiangLi: false);
		}
		return promoted;
	}

	internal static bool TryDebugAdvance(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !TryBuildSnapshot(actor, out XjShiSnapshot snapshot)) return false;
		int year = Math.Max(1, annualYear);
		if (string.Equals(snapshot.Realm, XjShiRealmIds.Monk, StringComparison.Ordinal))
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, XjShiCatalog.DharmaMasterPracticeThreshold);
			return TrySetRealm(actor, XjShiRealmIds.DharmaMaster, year, manualOverride: false);
		}
		if (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal))
		{
			if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
			{
				// 古释法师下一境就是法相（金丹级）。通用手动“推进位次”到此为止，
				// 不再预填修持/金地，也不调用内部迁移用的法相修复事务。
				return false;
			}
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, XjShiCatalog.LianMinPracticeThreshold);
			return TryAttachAsLianMin(actor, year, preserveSeat: false);
		}
		if (string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			if (string.Equals(snapshot.SeatId, XjShiSeatIds.SaTuo, StringComparison.Ordinal))
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, XjShiSeatIds.FaHui);
				XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, XjShiCatalog.FaHuiPracticeThreshold);
				RefreshRuntime(actor, syncVisibleTraits: true);
				XjShiExpansionSystem.OnRealmOrPositionAttained(actor, year, XjShiRealmIds.LianMin,
					XjShiSeatIds.FaHui, selfProvedJinDi: false, becameLiangLi: false);
				return true;
			}
			if (string.Equals(snapshot.SeatId, XjShiSeatIds.FaHui, StringComparison.Ordinal))
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSeatId, XjShiSeatIds.JinLian);
				XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, XjShiCatalog.JinLianPracticeThreshold);
				RefreshRuntime(actor, syncVisibleTraits: true);
				XjShiExpansionSystem.OnRealmOrPositionAttained(actor, year, XjShiRealmIds.LianMin,
					XjShiSeatIds.JinLian, selfProvedJinDi: false, becameLiangLi: false);
				return true;
			}
			return TryDebugSeedMoHe(actor, year);
		}
		// 法相不再由通用“推进位次”绕过承载所有权直接生成。
		// 使用独立的“补录法相”测试入口，确保应土主人、旃檀林锚点与境界事务原子提交。
		return false;
	}

	private static void ClearZhantanlinAnchorForAncient(Actor actor)
	{
		if (actor?.data == null) return;
		// 古释没有旃檀林真身锚，也不保留今释首次迁入标记。
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiYouTanLinAnchorId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinFirstEntry, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinNextReturnYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear, 0);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiJinDiId, out string jinDiId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthAnchorId, out string rebirthAnchorId);
		if (string.Equals(domainId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal))
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, string.Empty);
		if (string.Equals(jinDiId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal))
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, string.Empty);
		if (string.Equals(rebirthAnchorId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal))
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId, string.Empty);
	}

	private static int ReadInt(Actor actor, string key)
	{
		return XjActorAccessor.TryGetInt(actor, key, out int value) ? value : 0;
	}

	private static string ReadString(Actor actor, string key)
	{
		return XjActorAccessor.TryGetString(actor, key, out string value) ? value ?? string.Empty : string.Empty;
	}

	private static void RefreshRuntime(Actor actor, bool syncVisibleTraits)
	{
		if (actor?.data == null) return;
		XjRealmSuppression.SyncCombatLevel(actor);
		XjCultivatorCache.CheckAndUpdate(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		if (syncVisibleTraits) XjVisibleTraitSync.SyncCultivationTraits(actor);
		XjShiTitleSystem.EnsureForActor(actor);
		try { actor.setStatsDirty(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("XjShiState.RefreshRuntime.SetStatsDirty", ex); }
	}
}
