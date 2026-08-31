using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修高位年度闭环。原著确定高位依靠承载地、位次、轮回、法相层次与旃檀林，
/// 但没有给出绝对门槛和成功率；本系统只用集中工程参数补齐 WorldBox 可运行事务。
/// 所有入口均按年度或死亡事件运行，不进行逐帧扫描。
/// </summary>
internal static class XjShiHighRealmSystem
{
	internal static void EnsureActor(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return;
		int year = Math.Max(1, annualYear);
		RefreshTrueSpiritLock(actor, year);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiHighRealmAuditVersion, out int auditVersion);

		// 旧档可能已经把权威境界写成法相/世尊，却仍保留上一层可见特质；
		// 这会让实际 stats 仍停在旧档，排行榜于是出现世尊战力低于金丹巅峰。
		// 这里只对真实高位释修做 O(1) 投影核对，正常一致时不触发任何属性重建。
		if ((string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
			&& XjShiVisibleTraitSync.EnsureRealmProjection(actor, realm))
		{
			XjRealmSuppression.SyncCombatLevel(actor);
			XjCombatHotPathCache.Refresh(actor);
		}

		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			if (TryBeginVoluntaryReincarnation(actor, year)) return;
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
				XjShiWorldHonoredReadinessIds.Locked);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiYouTanLinAnchorId, string.Empty);
			// 七世起今释摩诃即可按极低概率感应无主金地；九世圆满且已掌握金地后方可证法相。
			XjShiDomainState.TryEstablishTempleMasterJinDi(actor, year);
			WarmModernTempleMasterFoundation(actor, year);
			EvaluateAndAttemptDharmaForm(actor, year);
		}
		else if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string dharmaTradition);
			if (string.Equals(dharmaTradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
				&& !XjShiDomainState.TryGetOwnedNorthJinDi(((BaseSystemData)actor.data).id, out _))
				XjShiDomainState.TryForceEstablishTempleMasterJinDi(actor, year, announce: false);
			EnsureDharmaFormAnchor(actor);
			XjShiDomainState.RefreshHeavenProjection(actor);
			NormalizeDharmaFormStage(actor, year);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormCandidateState,
				XjShiDharmaFormCandidateIds.Owner);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormCandidateSinceYear, 0);
			ProcessDharmaFormAnnual(actor, year);
		}
		else if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string honoredTradition);
			if (string.Equals(honoredTradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
				&& !XjShiDomainState.TryGetOwnedNorthJinDi(((BaseSystemData)actor.data).id, out _))
				XjShiDomainState.TryForceEstablishTempleMasterJinDi(actor, year, announce: false);
			EnsureDharmaFormAnchor(actor);
			XjShiDomainState.RefreshHeavenProjection(actor);
			NormalizeDharmaFormStage(actor, year);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage,
				XjShiDharmaFormStageIds.WorldHonoredPath);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormCandidateState,
				XjShiDharmaFormCandidateIds.Owner);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
				XjShiWorldHonoredReadinessIds.Completed);
		}
		else
		{
			ClearHighRealmProjection(actor);
		}

		if (auditVersion < XjShiCatalog.HighRealmAuditVersion)
		{
			MigrateHighRealmFields(actor, realm, year);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiHighRealmAuditVersion,
				XjShiCatalog.HighRealmAuditVersion);
			XjShiDomainState.Invalidate();
		}
	}

	/// <summary>
	/// 古释慧觉天地时由法师直接自证法相，不经过怜愍、摩诃，也不占用今释九世轮回。
	/// </summary>
	internal static bool TryAttainAncientDharmaForm(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)) return false;
		int year = Math.Max(1, annualYear);
		if (!XjShiDomainState.TryGetForActor(actor, year, out XjShiDomainRecord domain) || domain == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjShiDomainState.TryClaimDharmaFormPosition(domain.DomainId, actorId, year)
			|| !XjShiState.TrySetRealm(actor, XjShiRealmIds.DharmaForm, year,
				manualOverride: false, updateWorldRegistry: false)) return false;
		OnDharmaFormAttained(actor, domain.DomainId, year, debug: false);
		return true;
	}

	private static bool TryBeginVoluntaryReincarnation(Actor actor, int year)
	{
		if (actor?.data == null || !actor.isAlive()
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| !string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
			|| snapshot.CurrentLife >= 9
			|| string.Equals(snapshot.RebirthState, XjShiRebirthStateIds.Recovering, StringComparison.Ordinal)
			|| IsTrueSpiritLocked(actor, year)
			|| snapshot.RealmEnteredYear <= 0
			|| year - snapshot.RealmEnteredYear < XjShiCatalog.MoHeVoluntaryReincarnationMinimumYears
			|| !XjShiDomainState.TryGetForActor(actor, year, out XjShiDomainRecord domain)
			|| domain == null) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || XjReincarnation.HasPendingShi(actorId)
			|| !XjReincarnation.RecordVoluntaryShi(actor, year)) return false;
		string anchorName = XjShiDomainCatalog.GetDomainDisplayName(domain);
		int currentLife = Math.Clamp(snapshot.CurrentLife, 1, 9);
		bool died = XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)11, true,
			XjDeathCause.ShiVoluntaryReincarnation);
		if (died)
		{
			XjWorldHistoryStore.RecordActorEvent(actor,
				"今身修持已足，主动散去肉身，真灵归于所系承载地以启下一世。",
				XjShiTraitIds.MoHe);
			XjShiAnnouncementSystem.OnMoHeReincarnationBegun(actor, currentLife, anchorName);
			return true;
		}
		XjReincarnation.CancelPending(actorId);
		return false;
	}

	internal static bool IsTrueSpiritLocked(Actor actor, int annualYear)
	{
		if (actor?.data == null) return false;
		int year = Math.Max(1, annualYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiTrueSpiritLocked, out int locked);
		if (locked <= 0) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiTrueSpiritLockUntilYear, out int untilYear);
		if (untilYear > 0 && year >= untilYear)
		{
			ClearTrueSpiritLock(actor);
			return false;
		}
		return true;
	}

	internal static void ApplyTrueSpiritLock(Actor actor, int untilYear, string reason)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLocked, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLockUntilYear, Math.Max(0, untilYear));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTrueSpiritLockReason, reason ?? string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 1);
	}

	internal static void ClearTrueSpiritLock(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLocked, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLockUntilYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTrueSpiritLockReason, string.Empty);
	}

	/// <summary>
	/// 高命数宿世直证法相。调用者已经先把角色置为九世摩诃；此入口只补齐庙主金地、
	/// 法相位与最低承载结构，不读取普通修持/轮回门槛，也不触碰世尊唯一性。
	/// 若天下金地已无可用权属，则保留九世摩诃而不强抢他人金地。
	/// </summary>
	internal static bool TryAttainFateDharmaForm(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| !string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) return false;
		int year = Math.Max(1, annualYear);
		if (IsTrueSpiritLocked(actor, year)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjShiDomainState.TryForceEstablishTempleMasterJinDi(actor, year, announce: true)
			|| !XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
			|| domain == null || domain.OwnerActorId != actorId) return false;

		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, 8);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, 9);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice,
			Math.Max(ReadFloat(actor, XjActorDataKeys.ShiPractice), XjShiCatalog.DharmaFormPracticeThreshold));
		if (domain.Growth < XjShiCatalog.DharmaFormMinimumDomainGrowth)
			XjShiDomainState.AddHighRealmGrowth(domain.DomainId,
				XjShiCatalog.DharmaFormMinimumDomainGrowth - domain.Growth, year);
		if (!XjShiDomainState.TryClaimDharmaFormPosition(domain.DomainId, actorId, year)
			|| !XjShiState.TrySetRealm(actor, XjShiRealmIds.DharmaForm, year,
				manualOverride: false, updateWorldRegistry: false)) return false;
		OnDharmaFormAttained(actor, domain.DomainId, year, debug: false, fateDirect: true);
		return true;
	}

	internal static bool TryDebugSeedDharmaForm(Actor actor, int annualYear, bool emitNarrative = true)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)) return false;
		bool modern = string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
		bool ancient = string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
		bool legalSourceRealm = modern
			? string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
			: ancient && (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)
				|| string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal));
		if (!legalSourceRealm) return false;

		int year = Math.Max(1, annualYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (modern)
		{
			if (!XjShiDomainState.TryForceEstablishTempleMasterJinDi(actor, year, announce: emitNarrative))
				return false;
		}
		else
		{
			XjShiDomainState.EnsureAncientSelfProvedJinDi(actor, year);
		}
		if (!XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
			|| domain == null) return false;
		if (domain.OwnerActorId > 0L && domain.OwnerActorId != actorId
			&& XjShiDomainState.HasLiveDharmaFormOwner(domain.DomainId)) return false;

		if (modern)
		{
			int completedLives = Math.Clamp(
				Math.Max(snapshot.CompletedLives, XjShiCatalog.DharmaFormMinimumLives), 0, 8);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, completedLives);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, Math.Clamp(completedLives + 1, 1, 9));
		}
		else
		{
			// 古释不以轮回世数证明法相，旧档若带入摩诃世数也在此收回。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, 1);
		}
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice,
			Math.Max(snapshot.Practice, ResolveDharmaFormPracticeThreshold(snapshot.Tradition)));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu,
			Math.Max(XjShiMingShuSystem.GetValue(actor), XjShiCatalog.DharmaFormMinimumMingShu));
		if (domain.Growth < XjShiCatalog.DharmaFormMinimumDomainGrowth)
			XjShiDomainState.AddHighRealmGrowth(domain.DomainId,
				XjShiCatalog.DharmaFormMinimumDomainGrowth - domain.Growth, year);
		if (!XjShiDomainState.TryClaimDharmaFormPosition(domain.DomainId, actorId, year)) return false;
		if (!XjShiState.TrySetRealm(actor, XjShiRealmIds.DharmaForm, year,
			manualOverride: false, updateWorldRegistry: false, emitNarrative: emitNarrative)) return false;
		OnDharmaFormAttained(actor, domain.DomainId, year, debug: true, emitNarrative: emitNarrative);
		return true;
	}

	/// <summary>
	/// 内部迁移/诊断使用的世尊修复事务。它不向特质编辑器暴露；仍要求角色已成法相、
	/// 拥有承载地并严格执行古释／今释各一位世尊的唯一性。玩家不能用它手动越境。
	/// </summary>
	internal static bool TryDebugAttainWorldHonored(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)) return false;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return true;
		if (!string.Equals(snapshot.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return false;
		int year = Math.Max(1, annualYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& !XjShiDomainState.TryForceEstablishTempleMasterJinDi(actor, year, announce: true)) return false;
		if (HasLiveWorldHonored(snapshot.Tradition, actorId)
			|| !XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
			|| domain == null) return false;

		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage,
			XjShiDharmaFormStageIds.WorldHonoredPath);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear,
			Math.Max(1, year - XjShiCatalog.WorldHonoredMinimumPathYears));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice,
			Math.Max(ReadFloat(actor, XjActorDataKeys.ShiPractice), XjShiCatalog.WorldHonoredPracticeThreshold));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu,
			Math.Max(XjShiMingShuSystem.GetValue(actor), XjShiCatalog.WorldHonoredMingShuThreshold));
		if (domain.Growth < XjShiCatalog.WorldHonoredDomainGrowthThreshold)
		{
			XjShiDomainState.AddHighRealmGrowth(domain.DomainId,
				XjShiCatalog.WorldHonoredDomainGrowthThreshold - domain.Growth, year);
		}
		ClearTrueSpiritLock(actor);
		return AttemptWorldHonored(actor, year, debug: true);
	}

	internal static bool TryDebugAdvanceDharmaFormStage(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return false;
		int year = Math.Max(1, annualYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (!string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& !XjShiDomainState.TryForceEstablishTempleMasterJinDi(actor, year, announce: true)) return false;
		if (!XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out _)) return false;
		NormalizeDharmaFormStage(actor, year);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string current);
		if (string.Equals(current, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal))
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice,
				Math.Max(ReadFloat(actor, XjActorDataKeys.ShiPractice), XjShiCatalog.WorldHonoredPracticeThreshold));
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu,
				Math.Max(XjShiMingShuSystem.GetValue(actor), XjShiCatalog.WorldHonoredMingShuThreshold));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear,
				Math.Max(1, year - XjShiCatalog.WorldHonoredMinimumPathYears));
			if (XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
				&& domain.Growth < XjShiCatalog.WorldHonoredDomainGrowthThreshold)
			{
				XjShiDomainState.AddHighRealmGrowth(domain.DomainId,
					XjShiCatalog.WorldHonoredDomainGrowthThreshold - domain.Growth, year);
			}
			return AttemptWorldHonored(actor, year, debug: true);
		}

		string next = string.Equals(current, XjShiDharmaFormStageIds.OriginalVow, StringComparison.Ordinal)
			? XjShiDharmaFormStageIds.ResponseBody
			: string.Equals(current, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal)
				? XjShiDharmaFormStageIds.SelfReturned
				: XjShiDharmaFormStageIds.WorldHonoredPath;
		AdvanceStage(actor, next, year, debug: true);
		return true;
	}

	/// <summary>
	/// 九世今释摩诃一旦真正取得北世尊金地，法相前置不再继续按低境自然修持
	/// 的速度拖延上千年。该阶段每年同步温养“修持 / 命数 / 金地承载”，且三项
	/// 都只补到法相最低门槛；未满九世、未得地、古释与已成法相者均不受影响。
	/// </summary>
	private static void WarmModernTempleMasterFoundation(Actor actor, int year)
	{
		if (actor?.data == null || !actor.isAlive()
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| !string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
			|| snapshot.CompletedLives < XjShiCatalog.DharmaFormMinimumLives) return;

		if (IsTrueSpiritLocked(actor, year)) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDomainShockUntilYear, out int shockUntil);
		if (shockUntil >= year) return;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjShiDomainState.TryGetOwnedNorthJinDi(actorId, out XjShiDomainRecord domain)
			|| domain == null || domain.OwnerActorId != actorId
			|| !XjShiDomainState.IsDharmaFormFoundationStable(domain)) return;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiTempleMasterFoundationLastYear, out int lastYear);
		if (lastYear >= year) return;
		int elapsed = lastYear > 0 ? Math.Clamp(year - lastYear, 1, 10) : 1;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTempleMasterFoundationLastYear, year);

		float currentPractice = ReadFloat(actor, XjActorDataKeys.ShiPractice);
		if (currentPractice < XjShiCatalog.DharmaFormPracticeThreshold)
		{
			float nextPractice = Math.Min(XjShiCatalog.DharmaFormPracticeThreshold,
				currentPractice + XjShiCatalog.TempleMasterAnnualDharmaFormPracticeGain * elapsed);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, nextPractice);
		}

		XjShiMingShuSystem.GrantTempleMasterFoundation(actor,
			XjShiCatalog.TempleMasterAnnualDharmaFormMingShuGain * elapsed);

		if (domain.Growth < XjShiCatalog.DharmaFormMinimumDomainGrowth)
		{
			int growth = Math.Min(
				XjShiCatalog.DharmaFormMinimumDomainGrowth - domain.Growth,
				XjShiCatalog.TempleMasterAnnualDomainGrowthGain * elapsed);
			if (growth > 0) XjShiDomainState.AddHighRealmGrowth(domain.DomainId, growth, year);
		}
	}

	private static void EvaluateAndAttemptDharmaForm(Actor actor, int year)
	{
		if (!XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
			|| domain == null)
		{
			// 今释没有取得真实金地时只能继续感地，旃檀林整体不能替代法相根基。
			SetCandidateState(actor, XjShiDharmaFormCandidateIds.Insufficient, year);
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (domain.OwnerActorId > 0L && domain.OwnerActorId != actorId
			&& XjShiDomainState.HasLiveDharmaFormOwner(domain.DomainId))
		{
			SetCandidateState(actor, XjShiDharmaFormCandidateIds.LiveOwnerBlocks, year);
			return;
		}
		if (!MeetsDharmaFormThresholds(actor, snapshot, domain, year))
		{
			SetCandidateState(actor, XjShiDharmaFormCandidateIds.Insufficient, year);
			return;
		}
		if (!IsBestDharmaFormCandidate(actor, domain, year))
		{
			SetCandidateState(actor, XjShiDharmaFormCandidateIds.Eligible, year);
			return;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDharmaFormLastAttemptYear, out int actorLastAttempt);
		if (actorLastAttempt > 0
			&& year - actorLastAttempt < XjShiCatalog.DharmaFormAttemptIntervalYears)
		{
			SetCandidateState(actor, XjShiDharmaFormCandidateIds.AttemptCooldown, year);
			return;
		}
		if (!XjShiDomainState.TryBeginDharmaFormAttempt(domain.DomainId, actorId, year))
		{
			SetCandidateState(actor, XjShiDharmaFormCandidateIds.AttemptCooldown, year);
			return;
		}

		SetCandidateState(actor, XjShiDharmaFormCandidateIds.Eligible, year);
		bool guaranteed = XjShiWorldHonoredPosturePolicy.IsGuaranteedWorldHonored(actor);
		int chance = guaranteed ? 10000 : ResolveDharmaFormChance(actor, snapshot, domain, year);
		int roll = guaranteed ? 0 : XjDeterministicHash.PositiveIndex(actorId + year,
			"shi_dharma_form_attempt|" + domain.DomainId, 10000);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormLastAttemptYear, year);
		if (roll >= chance)
		{
			ApplyDharmaFormFailure(actor, domain, year);
			return;
		}

		if (!XjShiDomainState.TryClaimDharmaFormPosition(domain.DomainId, actorId, year)
			|| !XjShiState.TrySetRealm(actor, XjShiRealmIds.DharmaForm, year,
				manualOverride: false, updateWorldRegistry: false))
		{
			SetCandidateState(actor, XjShiDharmaFormCandidateIds.AttemptCooldown, year);
			return;
		}
		OnDharmaFormAttained(actor, domain.DomainId, year, debug: false);
	}


	private static bool MeetsDharmaFormThresholds(Actor actor, in XjShiSnapshot snapshot,
		XjShiDomainRecord domain, int year)
	{
		if (actor?.data == null || domain == null || IsTrueSpiritLocked(actor, year)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		bool modern = string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
		bool ancient = string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
		bool validFoundation;
		if (modern)
		{
			validFoundation = XjZhantanlinSystem.IsPlaced
				&& domain.IsNorthWorldHonoredFragment > 0
				&& string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				&& domain.OwnerActorId == actorId
				&& XjShiDomainState.IsDharmaFormFoundationStable(domain);
		}
		else
		{
			validFoundation = ancient
				&& domain.OwnerActorId == actorId
				&& domain.IsNorthWorldHonoredFragment <= 0
				&& string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				&& XjShiDomainState.IsDharmaFormFoundationStable(domain);
		}
		float threshold = ResolveDharmaFormPracticeThreshold(snapshot.Tradition);
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, year);
		bool livesReady = ancient || snapshot.CompletedLives >= XjShiCatalog.DharmaFormMinimumLives;
		return validFoundation
			&& snapshot.Practice >= threshold
			&& livesReady
			&& mingShu >= XjShiCatalog.DharmaFormMinimumMingShu
			&& domain.Growth >= XjShiCatalog.DharmaFormMinimumDomainGrowth;
	}

	private static bool IsBestDharmaFormCandidate(Actor actor, XjShiDomainRecord domain, int year)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		// 每块北世尊金地已有独立庙主权属，持地者只需与自己的根基相争，
		// 不再把整座旃檀林当作一个共享候选池。
		if (domain != null && domain.IsNorthWorldHonoredFragment > 0)
			return domain.OwnerActorId == actorId;

		long bestId = 0L;
		long bestScore = long.MinValue;
		var ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor candidate)
				|| candidate?.data == null || !candidate.isAlive()
				|| !XjCultivationPathRules.IsShi(candidate)
				|| !XjShiState.TryBuildSnapshot(candidate, out XjShiSnapshot candidateSnapshot)
				|| !string.Equals(candidateSnapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
				|| !XjShiDomainState.TryGetDharmaFormFoundation(candidate, year, out XjShiDomainRecord candidateDomain)
				|| candidateDomain == null
				|| !string.Equals(candidateDomain.DomainId, domain.DomainId, StringComparison.Ordinal)
				|| !MeetsDharmaFormThresholds(candidate, candidateSnapshot, candidateDomain, year)) continue;
			long score = ResolveDharmaFormScore(candidate, candidateSnapshot, candidateDomain, year);
			long id = ((BaseSystemData)candidate.data).id;
			if (bestId <= 0L || score > bestScore || score == bestScore && id < bestId)
			{
				bestId = id;
				bestScore = score;
			}
		}
		return bestId == actorId;
	}

	private static long ResolveDharmaFormScore(Actor actor, in XjShiSnapshot snapshot,
		XjShiDomainRecord domain, int year)
	{
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, year);
		return (long)Math.Floor(snapshot.Practice)
			+ snapshot.CompletedLives * 100000L
			+ (long)Math.Floor(mingShu) * 1000L
			+ snapshot.Alignment * 500L
			+ Math.Max(0, domain.Growth) * 100L;
	}

	private static int ResolveDharmaFormChance(Actor actor, in XjShiSnapshot snapshot,
		XjShiDomainRecord domain, int year)
	{
		float excessPractice = Math.Max(0f, snapshot.Practice - ResolveDharmaFormPracticeThreshold(snapshot.Tradition));
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, year);
		int chance = XjShiCatalog.DharmaFormBaseChancePerTenThousand
			+ Math.Max(0, snapshot.CompletedLives - XjShiCatalog.DharmaFormMinimumLives) * 400
			+ (int)Math.Floor(Math.Max(0f, mingShu - XjShiCatalog.DharmaFormMinimumMingShu) * 4f)
			+ Math.Max(0, snapshot.Alignment) * 8
			+ (int)Math.Floor(excessPractice / 500f)
			+ Math.Max(0, domain.Growth - XjShiCatalog.DharmaFormMinimumDomainGrowth);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		chance = XjShiLineagePolicy.ModifyDharmaFormChance(lineageId, chance);
		chance = Math.Clamp(chance, XjShiCatalog.DharmaFormBaseChancePerTenThousand,
			XjShiCatalog.DharmaFormMaximumChancePerTenThousand);
		return chance;
	}

	private static void ApplyDharmaFormFailure(Actor actor, XjShiDomainRecord domain, int year)
	{
		float practice = ReadFloat(actor, XjActorDataKeys.ShiPractice);
		float mingShu = XjShiMingShuSystem.GetValue(actor);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice,
			Math.Max(XjShiCatalog.MoHePracticeThreshold,
				practice * XjShiCatalog.DharmaFormFailurePracticeRetention));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu,
			Math.Max(0f, mingShu - XjShiCatalog.DharmaFormFailureMingShuLoss));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainShockUntilYear,
			year + XjShiCatalog.DharmaFormFailureShockYears);
		SetCandidateState(actor, XjShiDharmaFormCandidateIds.AttemptCooldown, year);
		XjWorldHistoryStore.RecordActorEvent(actor,
			"争取法相位未成，本性与承载地一并震荡，须重新温养后再证。",
			XjShiTraitIds.MoHe);
	}

	private static void OnDharmaFormAttained(Actor actor, string domainId, int year, bool debug, bool fateDirect = false, bool emitNarrative = true)
	{
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage,
			XjShiDharmaFormStageIds.OriginalVow);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormLastAttemptYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk, 0);
		EnsureDharmaFormAnchor(actor);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormCandidateState,
			XjShiDharmaFormCandidateIds.Owner);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
			XjShiWorldHonoredReadinessIds.Locked);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiHighRealmAuditVersion,
			XjShiCatalog.HighRealmAuditVersion);
		XjShiDomainState.AddHighRealmGrowth(domainId, 100, year);
		XjShiDomainState.Invalidate();
		XjShiDomainState.ReconcileFromActors(year, force: true);
		XjShiDomainState.RefreshHeavenProjection(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		bool modern = string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
		string naturalText;
		string debugText;
		if (!modern)
		{
			naturalText = "一朝慧觉天地，自修应身、自证金地而成古释法相。";
			debugText = "经陆江仙补录古释法相位，自证金地而成。";
		}
		else
		{
			naturalText = fateDirect
				? "命数骤然昭显，宿世九重因缘一时俱现，又与北世尊金地相应，不历现世九番轮回而直证法相，受尊为庙主。"
				: "历世修持圆满，掌握北世尊应身碎片，以金地稳住位格而成法相，受尊为庙主；真身自此永镇旃檀林。";
			debugText = "经陆江仙补录庙主金地与法相位，以所掌金地稳住应身；真身自此永镇旃檀林。";
		}
		if (emitNarrative)
			XjWorldHistoryStore.RecordActorEvent(actor, debug ? debugText : naturalText,
				XjShiTraitIds.DharmaForm);
	}

	private static void ProcessDharmaFormAnnual(Actor actor, int year)
	{
		// 庙主法相每二十五年可尝试牵引同源碎片，逐步重组原本的一重三十二天。
		XjShiDomainState.TryAttractSameHeavenFragment(actor, year);
		XjShiDomainState.RefreshHeavenProjection(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string stage);
		if (string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal))
			UpdateResponseBodyRisk(actor, year);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out stage);
		if (string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal))
		{
			UpdateWorldHonoredReadiness(actor, XjShiRealmIds.DharmaForm, year);
			AttemptWorldHonored(actor, year, debug: false);
			return;
		}

		if (!TryResolveNextStage(stage, out string nextStage, out float practiceThreshold,
			out float mingShuThreshold, out int domainGrowthThreshold, out int minimumYears))
		{
			UpdateWorldHonoredReadiness(actor, XjShiRealmIds.DharmaForm, year);
			return;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, out int stageYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDharmaFormLastAttemptYear, out int lastAttempt);
		if (stageYear <= 0) stageYear = year;
		if (year - stageYear < minimumYears
			|| lastAttempt > 0 && year - lastAttempt < XjShiCatalog.DharmaFormStageAttemptIntervalYears
			|| !XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
			|| ReadFloat(actor, XjActorDataKeys.ShiPractice) < practiceThreshold
			|| XjShiMingShuSystem.GetEffectiveValue(actor, year) < mingShuThreshold
			|| domain.Growth < domainGrowthThreshold
			|| !XjShiDomainState.IsDharmaFormFoundationStable(domain)
			|| IsTrueSpiritLocked(actor, year))
		{
			UpdateWorldHonoredReadiness(actor, XjShiRealmIds.DharmaForm, year);
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		bool guaranteed = XjShiWorldHonoredPosturePolicy.IsGuaranteedWorldHonored(actor);
		int chance = guaranteed ? 10000 : ResolveStageChance(actor, practiceThreshold, mingShuThreshold, domainGrowthThreshold, domain, year);
		int roll = guaranteed ? 0 : XjDeterministicHash.PositiveIndex(actorId + year,
			"shi_dharma_form_stage|" + stage + "|" + nextStage, 10000);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormLastAttemptYear, year);
		if (roll < chance)
		{
			AdvanceStage(actor, nextStage, year, debug: false);
		}
		else
		{
			int risk = ReadInt(actor, XjActorDataKeys.ShiResponseBodyRisk);
			if (string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal))
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk, Math.Min(10000, risk + 500));
			XjWorldHistoryStore.RecordActorEvent(actor,
				"法相层次未能进境，本性与应身相持，继续温养。",
				XjShiTraitIds.DharmaForm);
		}
		UpdateWorldHonoredReadiness(actor, XjShiRealmIds.DharmaForm, year);
	}

	private static void UpdateResponseBodyRisk(Actor actor, int year)
	{
		int risk = ReadInt(actor, XjActorDataKeys.ShiResponseBodyRisk);
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, year);
		bool stable = XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
			&& XjShiDomainState.IsDharmaFormFoundationStable(domain);
		int delta = 0;
		if (mingShu < XjShiCatalog.ResponseBodyMingShuThreshold) delta += 240;
		if (!stable) delta += 360;
		if (IsTrueSpiritLocked(actor, year)) delta += 500;
		if (delta <= 0) delta = -120;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		delta = XjShiLineagePolicy.ModifyResponseBodyRiskDelta(lineageId, delta);
		risk = Math.Clamp(risk + delta, 0, 10000);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk, risk);
		if (risk < XjShiCatalog.ResponseBodyRiskRelapseThreshold) return;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string previousStage);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage,
			XjShiDharmaFormStageIds.OriginalVow);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk, 2500);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice,
			Math.Max(XjShiCatalog.DharmaFormPracticeThreshold,
				ReadFloat(actor, XjActorDataKeys.ShiPractice) * 0.80f));
		ApplyTrueSpiritLock(actor, year + 30, "应身反客为主");
		if (domain != null) XjShiDomainState.ApplyHighRealmSetback(domain.DomainId, 150, year);
		XjWorldHistoryStore.RecordActorEvent(actor,
			"应身反客为主，法相层次退回本愿，真灵暂受应身牵制。",
			XjShiTraitIds.DharmaForm);
		XjThreeBookWriter.RecordShiDharmaFormStage(actor, year, previousStage,
			XjShiDharmaFormStageIds.OriginalVow, setback: true);
		XjShiAnnouncementSystem.OnDharmaFormStageChanged(actor, previousStage,
			XjShiDharmaFormStageIds.OriginalVow, setback: true);
		XjRealmSuppression.SyncCombatLevel(actor);
		XjCombatHotPathCache.Refresh(actor);
	}

	private static int ResolveStageChance(Actor actor, float practiceThreshold, float mingShuThreshold,
		int growthThreshold, XjShiDomainRecord domain, int year)
	{
		float practice = ReadFloat(actor, XjActorDataKeys.ShiPractice);
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, year);
		int chance = 4200
			+ (int)Math.Floor(Math.Max(0f, practice - practiceThreshold) / 200f)
			+ (int)Math.Floor(Math.Max(0f, mingShu - mingShuThreshold) * 6f)
			+ Math.Max(0, domain.Growth - growthThreshold) * 2;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		chance = XjShiLineagePolicy.ModifyDharmaFormStageChance(lineageId, chance);
		return Math.Clamp(chance, 2500, 8500);
	}

	private static void AdvanceStage(Actor actor, string nextStage, int year, bool debug)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string previousStage);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage, nextStage);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormLastAttemptYear, year);
		if (string.Equals(nextStage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal))
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk, 500);
		else if (string.Equals(nextStage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal))
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk, 0);
		if (XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain))
			XjShiDomainState.AddHighRealmGrowth(domain.DomainId,
				string.Equals(nextStage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal) ? 300 : 150,
				year);
		UpdateWorldHonoredReadiness(actor, XjShiRealmIds.DharmaForm, year);
		XjWorldHistoryStore.RecordActorEvent(actor,
			(debug ? "经陆江仙推进法相层次为“" : "法相修持进境，层次达到“")
				+ XjShiCatalog.GetDharmaFormStageDisplay(nextStage) + "”。",
			XjShiTraitIds.DharmaForm);
		XjThreeBookWriter.RecordShiDharmaFormStage(actor, year, previousStage, nextStage, setback: false);
		XjShiAnnouncementSystem.OnDharmaFormStageChanged(actor, previousStage, nextStage, setback: false);
		XjRealmSuppression.SyncCombatLevel(actor);
		XjCombatHotPathCache.Refresh(actor);
	}

	private static bool AttemptWorldHonored(Actor actor, int year, bool debug)
	{
		if (actor?.data == null
			|| !XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
			|| domain == null) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		long actorId = ((BaseSystemData)actor.data).id;
		// 古释、今释各只有一位世尊；七相/法脉不再各占一个世尊名额。
		if (HasLiveWorldHonored(tradition, actorId))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
				XjShiWorldHonoredReadinessIds.Locked);
			return false;
		}
		if (!debug && !MeetsWorldHonoredThresholds(actor, domain, year))
		{
			UpdateWorldHonoredReadiness(actor, XjShiRealmIds.DharmaForm, year);
			return false;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiWorldHonoredLastAttemptYear, out int actorLastAttempt);
		if (!debug && actorLastAttempt > 0
			&& year - actorLastAttempt < XjShiCatalog.WorldHonoredAttemptIntervalYears)
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
				XjShiWorldHonoredReadinessIds.AttemptCooldown);
			return false;
		}
		if (!debug && !XjShiDomainState.TryBeginWorldHonoredAttempt(domain.DomainId, actorId, year))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
				XjShiWorldHonoredReadinessIds.AttemptCooldown);
			return false;
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiWorldHonoredLastAttemptYear, year);
		bool guaranteed = debug || XjShiWorldHonoredPosturePolicy.IsGuaranteedWorldHonored(actor);
		int chance = guaranteed ? 10000 : ResolveWorldHonoredChance(actor, domain, year);
		int roll = guaranteed ? 0 : XjDeterministicHash.PositiveIndex(actorId + year,
			"shi_world_honored_attempt|" + tradition + "|" + lineageId, 10000);
		if (roll >= chance)
		{
			ApplyWorldHonoredFailure(actor, domain, year);
			return false;
		}

		if (!XjShiState.TrySetRealm(actor, XjShiRealmIds.WorldHonored, year,
			manualOverride: true, updateWorldRegistry: false)) return false;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
			XjShiWorldHonoredReadinessIds.Completed);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage,
			XjShiDharmaFormStageIds.WorldHonoredPath);
		XjShiDomainState.AddHighRealmGrowth(domain.DomainId, 1000, year);
		XjShiDomainState.Invalidate();
		XjShiDomainState.ReconcileFromActors(year, force: true);
		string eventText;
		if (debug)
		{
			eventText = "经陆江仙补齐证道条件，证成世尊。";
		}
		else if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& XjShiDomainState.HasReformedHeaven(actorId, out string heavenId)
			&& XjShiHeavenCatalog.TryParseHeavenId(heavenId, out int heavenIndex))
		{
			eventText = "聚齐同源金地，重组" + XjShiHeavenCatalog.GetHeavenDisplayName(heavenIndex)
				+ "，完整驾驭北世尊一重应身，终证今释世尊。";
		}
		else
		{
			eventText = "慧觉天地、功绩圆满，自成三十二重天，终证古释世尊。";
		}
		XjWorldHistoryStore.RecordActorEvent(actor, eventText, XjShiTraitIds.WorldHonored);
		return true;
	}

	private static bool MeetsWorldHonoredThresholds(Actor actor, XjShiDomainRecord domain, int year)
	{
		if (actor?.data == null || domain == null) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string stage);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, out int stageYear);
		long actorId = ((BaseSystemData)actor.data).id;
		bool modernHeavenReady = !string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiDomainState.HasReformedHeaven(actorId, out _);
		return string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal)
			&& year - Math.Max(1, stageYear) >= XjShiCatalog.WorldHonoredMinimumPathYears
			&& ReadFloat(actor, XjActorDataKeys.ShiPractice) >= XjShiCatalog.WorldHonoredPracticeThreshold
			&& XjShiMingShuSystem.GetEffectiveValue(actor, year) >= XjShiCatalog.WorldHonoredMingShuThreshold
			&& domain.Growth >= XjShiCatalog.WorldHonoredDomainGrowthThreshold
			&& XjShiDomainState.IsDharmaFormFoundationStable(domain)
			&& modernHeavenReady
			&& !IsTrueSpiritLocked(actor, year);
	}

	private static int ResolveWorldHonoredChance(Actor actor, XjShiDomainRecord domain, int year)
	{
		float practice = ReadFloat(actor, XjActorDataKeys.ShiPractice);
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, year);
		int chance = XjShiCatalog.WorldHonoredBaseChancePerTenThousand
			+ (int)Math.Floor(Math.Max(0f, practice - XjShiCatalog.WorldHonoredPracticeThreshold) / 500f)
			+ (int)Math.Floor(Math.Max(0f, mingShu - XjShiCatalog.WorldHonoredMingShuThreshold) * 10f)
			+ Math.Max(0, domain.Growth - XjShiCatalog.WorldHonoredDomainGrowthThreshold) * 2;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		chance = XjShiLineagePolicy.ModifyWorldHonoredChance(lineageId, chance);
		chance = Math.Clamp(chance, XjShiCatalog.WorldHonoredBaseChancePerTenThousand,
			XjShiCatalog.WorldHonoredMaximumChancePerTenThousand);
		return chance;
	}

	private static void ApplyWorldHonoredFailure(Actor actor, XjShiDomainRecord domain, int year)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiWorldHonoredFailureCount, out int failures);
		failures = Math.Max(0, failures) + 1;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiWorldHonoredFailureCount, failures);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice,
			Math.Max(XjShiCatalog.SelfReturnedPracticeThreshold,
				ReadFloat(actor, XjActorDataKeys.ShiPractice) * XjShiCatalog.WorldHonoredFailurePracticeRetention));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu,
			Math.Max(0f, XjShiMingShuSystem.GetValue(actor) - XjShiCatalog.WorldHonoredFailureMingShuLoss));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string previousStage);
		string setbackStage = failures % 2 == 0
			? XjShiDharmaFormStageIds.ResponseBody
			: XjShiDharmaFormStageIds.SelfReturned;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage, setbackStage);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiResponseBodyRisk,
			string.Equals(setbackStage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal) ? 5000 : 0);
		ApplyTrueSpiritLock(actor, year + XjShiCatalog.WorldHonoredFailureTrueSpiritLockYears,
			"冲击世尊失败");
		XjShiDomainState.ApplyHighRealmSetback(domain.DomainId,
			XjShiCatalog.WorldHonoredFailureDomainGrowthLoss, year);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
			XjShiWorldHonoredReadinessIds.AttemptCooldown);
		XjWorldHistoryStore.RecordActorEvent(actor,
			"冲击世尊未成，本性与真灵受创，法相层次退转，承载地亦受震荡。",
			XjShiTraitIds.DharmaForm);
		XjThreeBookWriter.RecordShiDharmaFormStage(actor, year, previousStage, setbackStage, setback: true);
		XjShiAnnouncementSystem.OnDharmaFormStageChanged(actor, previousStage, setbackStage, setback: true);
	}

	internal static bool HasLiveWorldHonored(string tradition, long excludeActorId)
	{
		var ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (ids[i] == excludeActorId
				|| !XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string otherTradition);
			if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)
				&& string.Equals(otherTradition, tradition, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static void NormalizeDharmaFormStage(Actor actor, int year)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string stage);
		if (!XjShiDharmaFormStageIds.IsKnown(stage)
			|| string.Equals(stage, XjShiDharmaFormStageIds.None, StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage,
				XjShiDharmaFormStageIds.OriginalVow);
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, out int entered);
		if (entered <= 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, year);
	}

	private static void UpdateWorldHonoredReadiness(Actor actor, string realm, int year)
	{
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
				XjShiWorldHonoredReadinessIds.Completed);
			return;
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string stage);
		string readiness = XjShiWorldHonoredReadinessIds.Locked;
		if (string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal))
		{
			if (XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
				&& MeetsWorldHonoredThresholds(actor, domain, year))
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiWorldHonoredLastAttemptYear, out int lastAttempt);
				readiness = lastAttempt > 0 && year - lastAttempt < XjShiCatalog.WorldHonoredAttemptIntervalYears
					? XjShiWorldHonoredReadinessIds.AttemptCooldown
					: XjShiWorldHonoredReadinessIds.Eligible;
			}
			else readiness = XjShiWorldHonoredReadinessIds.OnPath;
		}
		else if (string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal))
			readiness = XjShiWorldHonoredReadinessIds.StructuralReady;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness, readiness);
	}

	private static bool TryResolveNextStage(string current, out string next, out float practice,
		out float mingShu, out int growth, out int years)
	{
		next = string.Empty;
		practice = 0f;
		mingShu = 0f;
		growth = 0;
		years = 0;
		if (string.Equals(current, XjShiDharmaFormStageIds.OriginalVow, StringComparison.Ordinal))
		{
			next = XjShiDharmaFormStageIds.ResponseBody;
			practice = XjShiCatalog.ResponseBodyPracticeThreshold;
			mingShu = XjShiCatalog.ResponseBodyMingShuThreshold;
			growth = XjShiCatalog.ResponseBodyDomainGrowthThreshold;
			years = XjShiCatalog.ResponseBodyMinimumYears;
			return true;
		}
		if (string.Equals(current, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal))
		{
			next = XjShiDharmaFormStageIds.SelfReturned;
			practice = XjShiCatalog.SelfReturnedPracticeThreshold;
			mingShu = XjShiCatalog.SelfReturnedMingShuThreshold;
			growth = XjShiCatalog.SelfReturnedDomainGrowthThreshold;
			years = XjShiCatalog.SelfReturnedMinimumYears;
			return true;
		}
		if (string.Equals(current, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal))
		{
			next = XjShiDharmaFormStageIds.WorldHonoredPath;
			practice = XjShiCatalog.WorldHonoredPathPracticeThreshold;
			mingShu = XjShiCatalog.WorldHonoredPathMingShuThreshold;
			growth = XjShiCatalog.WorldHonoredPathDomainGrowthThreshold;
			years = XjShiCatalog.WorldHonoredPathMinimumYears;
			return true;
		}
		return false;
	}

	internal static int ResolveFoundationGrowthFloor(string realm, string stage)
	{
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
			return XjShiCatalog.WorldHonoredDomainGrowthThreshold;
		if (string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal))
			return XjShiCatalog.WorldHonoredPathDomainGrowthThreshold;
		if (string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal))
			return XjShiCatalog.SelfReturnedDomainGrowthThreshold;
		if (string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal))
			return XjShiCatalog.ResponseBodyDomainGrowthThreshold;
		if (XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm))
			return XjShiCatalog.DharmaFormMinimumDomainGrowth;
		return 0;
	}

	private static float ResolveFoundationMingShuFloor(string realm, string stage)
	{
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
			return XjShiCatalog.WorldHonoredMingShuThreshold;
		if (string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal))
			return XjShiCatalog.WorldHonoredPathMingShuThreshold;
		if (string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal))
			return XjShiCatalog.SelfReturnedMingShuThreshold;
		if (string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal))
			return XjShiCatalog.ResponseBodyMingShuThreshold;
		if (XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm))
			return XjShiCatalog.DharmaFormMinimumMingShu;
		return 0f;
	}

	private static void MigrateHighRealmFields(Actor actor, string realm, int year)
	{
		// RC11.4：一次性修复旧档法相承载层次。只在审计版本升级时补合法基础值，
		// 后续高位失败造成的真实损耗不会被年度运行反复抬回。
		if (XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
			if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
				XjShiDomainState.EnsureAncientSelfProvedJinDi(actor, year);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string migrationStage);
			float mingShuFloor = ResolveFoundationMingShuFloor(realm, migrationStage);
			if (mingShuFloor > 0f && XjShiMingShuSystem.GetValue(actor) < mingShuFloor)
				XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu, mingShuFloor);
			int growthFloor = ResolveFoundationGrowthFloor(realm, migrationStage);
			if (growthFloor > 0
				&& XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord foundation)
				&& foundation != null && foundation.Growth < growthFloor)
			{
				XjShiDomainState.AddHighRealmGrowth(foundation.DomainId, growthFloor - foundation.Growth, year);
			}
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, out int stageYear);
		if (stageYear <= 0 && XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm))
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, year);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiTrueSpiritLocked, out int locked);
		if (locked <= 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTrueSpiritLockUntilYear, 0);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiTrueSpiritLockReason, string.Empty);
		}
		// 0.9.8.9 起世尊拥有独立可见特质。旧档曾以法相特质代投影，
		// 只在一次性高位迁移中同步，避免把全体释修改成年度特质扫描。
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
			XjShiVisibleTraitSync.Sync(actor);
	}

	private static void RefreshTrueSpiritLock(Actor actor, int year)
	{
		if (!IsTrueSpiritLocked(actor, year))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, out int suppressed);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDomainLinkSeveredUntilYear, out int severedUntil);
			if (suppressed > 0 && severedUntil < year
				&& XjShiDomainState.TryGetDharmaFormFoundation(actor, year, out XjShiDomainRecord domain)
				&& XjShiDomainState.IsDharmaFormFoundationStable(domain))
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 0);
		}
	}

	private static void EnsureDharmaFormAnchor(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiYouTanLinAnchorId,
			string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
				? XjShiDomainCatalog.ZhantanlinDomainId
				: string.Empty);
	}

	private static void SetCandidateState(Actor actor, string state, int year)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormCandidateState, out string previous);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormCandidateState, state);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDharmaFormCandidateSinceYear, out int since);
		if (!string.Equals(previous, state, StringComparison.Ordinal) || since <= 0)
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormCandidateSinceYear, year);
	}

	private static float ResolveDharmaFormPracticeThreshold(string tradition)
	{
		return string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			? XjShiCatalog.AncientDharmaFormPracticeThreshold
			: XjShiCatalog.DharmaFormPracticeThreshold;
	}

	private static void ClearHighRealmProjection(Actor actor)
	{
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiYouTanLinAnchorId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormCandidateState,
			XjShiDharmaFormCandidateIds.None);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormCandidateSinceYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiWorldHonoredReadiness,
			XjShiWorldHonoredReadinessIds.Locked);
	}

	private static float ReadFloat(Actor actor, string key)
	{
		XjActorAccessor.TryGetFloat(actor, key, out float value);
		return Math.Max(0f, value);
	}

	private static int ReadInt(Actor actor, string key)
	{
		XjActorAccessor.TryGetInt(actor, key, out int value);
		return Math.Max(0, value);
	}
}
