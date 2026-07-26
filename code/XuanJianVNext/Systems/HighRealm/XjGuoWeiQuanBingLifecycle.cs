using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjGuoWeiQuanBingLifecycle
{
	internal static void InitializeOnJinDan(Actor actor, string daoTu, string guoWei, int year)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu) || string.IsNullOrWhiteSpace(guoWei)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;

		XjGuoWeiQuanBingState pending = default;
		bool hasPendingReincarnatedZhengWei = XjGuoWeiQuanBingRegistry.TryGet(actorId, out pending)
			&& string.Equals(pending.LifecycleStatus, "PendingReincarnatedZhengWei", StringComparison.Ordinal);
		if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out _) && !hasPendingReincarnatedZhengWei) return;

		int count = ResolveLocalAuthorityCount(actor, guoWei);
		List<string> local = PickLocalAuthorities(daoTu, guoWei, count);
		bool forceFavored = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjForceGuoWeiZhongAiOnJinDan, out int forceFlag)
			&& forceFlag > 0
			&& guoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
		string favored = forceFavored
			? "果位钟爱"
			: hasPendingReincarnatedZhengWei && !string.IsNullOrWhiteSpace(pending.GuoWeiZhongAi)
				? pending.GuoWeiZhongAi
				: ResolveGuoWeiZhongAiOnZhengWei(actor, daoTu, guoWei, year);
		string pendingDaoTu = hasPendingReincarnatedZhengWei ? pending.PendingExternalZhengWeiDaoTu : string.Empty;
		int lockUntilYear = hasPendingReincarnatedZhengWei ? pending.LockUntilYear : 0;
		string summary = hasPendingReincarnatedZhengWei
			? (lockUntilYear > 0 ? "果位初定，承继转世正位封锁" : "果位初定，承继转世果位钟爱")
			: "果位初定";
		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true, actorId, actor.getName(), daoTu, guoWei, string.Join(",", local),
			string.Empty, string.Empty, string.Empty, favored, pendingDaoTu, lockUntilYear, false, 0,
			forceFavored ? "果位初定，果位钟爱应验" : summary, "Active", year, 0, string.Empty));
		if (forceFavored) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjForceGuoWeiZhongAiOnJinDan, 0);
	}

	internal static bool ForceGuoWeiZhongAi(Actor actor, int year)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state)
			&& state.Found
			&& state.GuoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			RecordCopy(state, state.LocalQuanBing, state.SeizedQuanBing, state.ForeignQuanBing, state.WithdrawnToDongTian,
				"果位钟爱", state.PendingExternalZhengWeiDaoTu, state.LockUntilYear, state.IntegrationRetreatActive,
				state.IntegrationRetreatEndYear, "成丹之时，果位钟爱应验");
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjForceGuoWeiZhongAiOnJinDan, 0);
			XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(actor);
			return true;
		}

		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		if (!jinDan.Found || !jinDan.GuoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		InitializeOnJinDan(actor, daoTu, jinDan.GuoWei, year);
		return XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState created)
			&& string.Equals(created.GuoWeiZhongAi, "果位钟爱", StringComparison.Ordinal);
	}

	internal static bool TryBuildReadOnlyRecoveryState(Actor actor, out XjGuoWeiQuanBingState state)
	{
		state = default;
		if (actor?.data == null || !actor.isAlive())
		{
			return false;
		}

		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		if (!jinDan.Found)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		string guoWei = jinDan.GuoWei;
		if (string.IsNullOrWhiteSpace(daoTu) || string.IsNullOrWhiteSpace(guoWei))
		{
			return false;
		}

		int acquiredYear = Math.Max(0, jinDan.SuccessYear);
		int count = ResolveLocalAuthorityCount(actor, guoWei);
		List<string> local = PickLocalAuthorities(daoTu, guoWei, count);
		state = new XjGuoWeiQuanBingState(
			true,
			actorId,
			actor.getName(),
			daoTu,
			guoWei,
			string.Join(",", local),
			string.Empty,
			string.Empty,
			string.Empty,
			ResolveGuoWeiZhongAiOnZhengWei(actor, daoTu, guoWei, acquiredYear),
			string.Empty,
			0,
			false,
			0,
			"旧档本地权柄重建；夺取与外道权柄无可恢复快照",
			"Active",
			acquiredYear,
			0,
			string.Empty);
		return true;
	}

	internal static void RecordReincarnatedZhengWeiHeir(
		Actor actor,
		string daoTu,
		string guoWei,
		string guoWeiZhongAi,
		int yiXiang,
		int currentYear,
		string sourceActorName)
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(daoTu)
			|| string.IsNullOrWhiteSpace(guoWeiZhongAi))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}

		if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState existing)
			&& !string.Equals(existing.LifecycleStatus, "PendingReincarnatedZhengWei", StringComparison.Ordinal))
		{
			return;
		}

		int normalizedYear = currentYear < 0 ? 0 : currentYear;
		string normalizedGuoWei = string.IsNullOrWhiteSpace(guoWei) ? daoTu.Trim() + XjGuoWeiCalculator.ZhengWei : guoWei.Trim();
		int lockUntilYear = 0;
		string summary = "果位钟爱转世承载，意象不足未入正位封锁";
		if (yiXiang >= XjGuoWeiQuanBingRules.PendingZhengWeiReincarnationYiXiangCost)
		{
			int nextYiXiang = Math.Max(0, yiXiang - XjGuoWeiQuanBingRules.PendingZhengWeiReincarnationYiXiangCost);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, nextYiXiang);
			// 0.5.4锁期从原身陨落时开始，不因转世载体晚出生而重新延长500年。
			lockUntilYear = XjGuoWeiQuanBingRegistry.TryGetZhengWeiLock(daoTu, normalizedYear, out int inheritedLockUntilYear)
				? inheritedLockUntilYear
				: normalizedYear + XjGuoWeiQuanBingRules.ExternalZhengWeiLockYears;
			summary = "果位钟爱转世承载，正位封锁至" + lockUntilYear + "年";
		}

		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true,
			actorId,
			actor.getName(),
			daoTu,
			normalizedGuoWei,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			guoWeiZhongAi,
			daoTu,
			lockUntilYear,
			false,
			0,
			string.IsNullOrWhiteSpace(sourceActorName) ? summary : summary + "（承自" + sourceActorName.Trim() + "）",
			"PendingReincarnatedZhengWei",
			normalizedYear,
			0,
			string.Empty));
	}

	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state)
			|| !string.Equals(state.LifecycleStatus, "Active", StringComparison.Ordinal))
			return;

		if (XjQuanBingStruggleSystem.IsActive)
		{
			// 参战者解除普通洞天闭关；已经夺得外道权柄并进入十年合道
			// 的胜者保持退场，不再被战争年度 Tick 强行放出。
			bool withdrawn = state.IntegrationRetreatActive
				|| !string.IsNullOrWhiteSpace(state.WithdrawnToDongTian);
			XjClosedCultivationGuard.MarkClosedCultivation(actor, withdrawn);
			return;
		}

		RefreshLocalAuthoritiesForStage(actor, state);
		if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out state))
		{
			return;
		}

		if (state.IntegrationRetreatActive && (state.IntegrationRetreatEndYear <= 0 || currentYear < state.IntegrationRetreatEndYear))
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
			return;
		}

		if (!state.IntegrationRetreatActive
			&& !XjZongMenDongTianLifecycle.IsRetreatActive(actor)
			&& state.LockUntilYear > 0
			&& currentYear >= state.LockUntilYear
			&& !string.IsNullOrWhiteSpace(state.ForeignQuanBing))
		{
			RecordCopy(state, state.LocalQuanBing, state.SeizedQuanBing, state.ForeignQuanBing, state.WithdrawnToDongTian,
				state.GuoWeiZhongAi, state.PendingExternalZhengWeiDaoTu, state.LockUntilYear, true,
				currentYear + XjGuoWeiQuanBingRules.ExternalAuthorityIntegrationYears, "外道权柄合道闭关");
			XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
			return;
		}

		if (state.IntegrationRetreatActive
			&& currentYear >= state.IntegrationRetreatEndYear
			&& string.IsNullOrWhiteSpace(state.ForeignQuanBing))
		{
			RecordCopy(state, state.LocalQuanBing, state.SeizedQuanBing, string.Empty, string.Empty,
				state.GuoWeiZhongAi, state.PendingExternalZhengWeiDaoTu, state.LockUntilYear, false, 0,
				"正位承继闭关完成", state.SeizedQuanBingSources);
			XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
			return;
		}

		if (state.IntegrationRetreatActive && currentYear >= state.IntegrationRetreatEndYear)
		{
			bool success = PositiveRoll(actorId, currentYear, "quanbing_integration") < (int)(XjGuoWeiQuanBingRules.NonAdjacentAuthorityIntegrationSuccessChance * 100f);
			if (success)
			{
				RecordPermanentLostAuthorities(state.PendingExternalZhengWeiDaoTu, state.DaoTu, state.ForeignQuanBing, currentYear);
			}
			string seized = success ? Merge(state.SeizedQuanBing, state.ForeignQuanBing) : state.SeizedQuanBing;
			// 合道失败时外道权柄遁回原道，成功时则正式并入；两种结果
			// 都必须清空临时外道权柄与退场标记。
			string foreign = string.Empty;
			string seizedSources = success
				? MergeAuthoritySources(
					state.SeizedQuanBingSources,
					BuildSeizedAuthoritySources(state.ForeignQuanBing, state.PendingExternalZhengWeiDaoTu, "外道正位", "外道融入"))
				: state.SeizedQuanBingSources;
			RecordCopy(state, state.LocalQuanBing, seized, foreign, string.Empty,
				state.GuoWeiZhongAi, string.Empty, 0, false, 0, success ? "外道权柄合道完成" : "外道权柄合道未成，权柄遁回原道", seizedSources);
			XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
			return;
		}

		if (XjClosedCultivationGuard.IsInClosedCultivation(actor)
			&& !XjZongMenDongTianLifecycle.IsRetreatActive(actor))
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
		}
	}

	internal static void ReleaseFromSnapshot(XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L || !XjGuoWeiQuanBingRegistry.TryGet(snapshot.ActorId, out XjGuoWeiQuanBingState victim))
			return;

		XjQuanBingStruggleSystem.NotifyJinDanDeath(snapshot.ActorId);
		bool favoredZhengWeiLock = !string.IsNullOrWhiteSpace(victim.GuoWeiZhongAi)
			&& (victim.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			&& snapshot.JinDanYiXiang >= XjGuoWeiQuanBingRules.PendingZhengWeiReincarnationYiXiangCost;
		int lockUntilYear = favoredZhengWeiLock
			? Math.Max(victim.LockUntilYear, snapshot.Year + XjGuoWeiQuanBingRules.ExternalZhengWeiLockYears)
			: victim.LockUntilYear;
		string pendingDaoTu = favoredZhengWeiLock
			? victim.DaoTu
			: victim.PendingExternalZhengWeiDaoTu;
		string releaseSummary = favoredZhengWeiLock
			? "果位钟爱应验，正位自陨落之年封锁至" + lockUntilYear + "年"
			: "果位权柄已随身死释放";

		// 先把果位钟爱锁写入历史账本，再处理击杀者承继。这样锁期内的普通
		// 余位/闰位不会趁死者释放果位的同一结算瞬间抢占正位。
		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true, victim.ActorId, victim.ActorName, victim.DaoTu, victim.GuoWei,
			victim.LocalQuanBing, victim.SeizedQuanBing, victim.ForeignQuanBing,
			victim.WithdrawnToDongTian, victim.GuoWeiZhongAi,
			pendingDaoTu, lockUntilYear, false, 0,
			releaseSummary, "Released",
			victim.AcquiredYear, snapshot.Year, "Death", victim.SeizedQuanBingSources));

		if (favoredZhengWeiLock)
		{
			string lockAnnouncement = XjAnnouncementText.BuildGuoWeiZhongAiZhengWeiLocked(
				victim.ActorName,
				victim.DaoTu,
				snapshot.Year,
				lockUntilYear);
			XjBroadcastSystem.BroadcastSLevelDomainEvent(
				XjWorldHistoryCategory.LifeAndDeath,
				XjAnnouncementEventTypes.GuoWeiZhongAiZhengWeiLocked,
				lockAnnouncement,
				actorId: victim.ActorId,
				actorName: victim.ActorName,
				result: XjHistoryResult.Death,
				year: snapshot.Year,
				color: "#8F68C6",
				duration: 10f,
				iconId: XjEventIconCatalog.HistoryLifeDeath);
		}

		TryTransferOnDeath(snapshot, victim);
	}

	private static void TryTransferOnDeath(in XjDeathSnapshot snapshot, in XjGuoWeiQuanBingState victim)
	{
		if (snapshot.LastAttackerId <= 0L) return;
		Actor killer = ResolveKnownActor(snapshot.LastAttackerId);
		if (killer?.data == null || !XjJinDanAccessor.BuildState(killer).Found) return;
		long killerId = ((BaseSystemData)killer.data).id;
		XjActorAccessor.TryGetString(killer, XjActorDataKeys.DaoTu, out string killerDaoTu);
		XjActorAccessor.TryGetString(killer, XjActorDataKeys.XjJinDanGuoWei, out string killerGuoWei);
		if (!XjGuoWeiQuanBingRegistry.TryGet(killerId, out XjGuoWeiQuanBingState killerState))
		{
			InitializeOnJinDan(killer, killerDaoTu, killerGuoWei, snapshot.Year);
			if (!XjGuoWeiQuanBingRegistry.TryGet(killerId, out killerState)) return;
		}

		string victimAuthorities = victim.LocalQuanBing;
		if (string.IsNullOrWhiteSpace(victimAuthorities)) return;

		// 0.5.4 口径：常态死亡不发生权柄夺取；权柄之争中，余位/闰位击杀本道途或相邻道途正位均可转正。
		// 仅权柄之争期间为完整概率，
		// 或受害者正在权柄合道闭关时以 0.8 倍概率被追夺。
		float chanceMultiplier = XjQuanBingStruggleSystem.IsActive
			? 1f
			: (victim.IntegrationRetreatActive ? XjGuoWeiQuanBingRules.PostIntegrationSeizeChanceMultiplier : 0f);
		if (chanceMultiplier <= 0f) return;

		bool sameDaoTu = string.Equals(killerDaoTu, victim.DaoTu, StringComparison.Ordinal);
		bool victimIsZhengWei = victim.GuoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
		bool victimIsYuWei = victim.GuoWei.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal);
		bool victimIsRunWei = victim.GuoWei.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal);

		if (victimIsZhengWei)
		{
			if (sameDaoTu)
			{
				if (!CanInheritSameDaoTuZhengWei(killerState)) return;
				float successionChance = XjGuoWeiQuanBingRules.SameDaoTuZhengWeiInheritanceChance * chanceMultiplier;
				if (PositiveRoll(killerId + victim.ActorId, snapshot.Year, "quanbing_zhengwei_succession") >= (int)(successionChance * 100f)) return;
				if (!TryPromoteSameDaoTuSuccessor(killer, killerState, victim, snapshot.Year)) return;
				string sameDaoAnnouncement = XjAnnouncementText.BuildSameDaoTuZhengWeiSuccession(
					killerState.ActorName,
					victim.ActorName,
					victim.DaoTu,
					killerState.GuoWei);
				XjBroadcastSystem.BroadcastSLevelDomainEvent(
					XjWorldHistoryCategory.World,
					XjAnnouncementEventTypes.SameDaoTuZhengWeiSuccession,
					sameDaoAnnouncement,
					actorId: killerId,
					actorName: killerState.ActorName,
					relatedActorId: victim.ActorId,
					relatedActorName: victim.ActorName,
					result: XjHistoryResult.Transfer,
					year: snapshot.Year,
					color: "#D9822B",
					duration: 10f,
					iconId: XjEventIconCatalog.HistoryWorld);
				return;
			}

			bool killerIsYuOrRun = CanInheritSameDaoTuZhengWei(killerState);
			bool killerIsZhengWei = (killerState.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
			if (!killerIsZhengWei
				&& killerIsYuOrRun
				&& XjXianJiCatalog.IsAdjacentDaoTu(killerDaoTu, victim.DaoTu))
			{
				float adjacentSuccessionChance = XjGuoWeiQuanBingRules.SameDaoTuZhengWeiInheritanceChance * chanceMultiplier;
				if (PositiveRoll(killerId + victim.ActorId, snapshot.Year, "quanbing_adjacent_zhengwei_succession") >= (int)(adjacentSuccessionChance * 100f)) return;
				if (!TryPromoteAdjacentDaoTuSuccessor(killer, killerState, victim, snapshot.Year)) return;
				string adjacentDaoAnnouncement = XjAnnouncementText.BuildAdjacentDaoTuZhengWeiSuccession(
					killerState.ActorName,
					victim.ActorName,
					killerDaoTu,
					victim.DaoTu,
					killerState.GuoWei);
				XjBroadcastSystem.BroadcastSLevelDomainEvent(
					XjWorldHistoryCategory.World,
					XjAnnouncementEventTypes.AdjacentDaoTuZhengWeiSuccession,
					adjacentDaoAnnouncement,
					actorId: killerId,
					actorName: killerState.ActorName,
					relatedActorId: victim.ActorId,
					relatedActorName: victim.ActorName,
					result: XjHistoryResult.Transfer,
					year: snapshot.Year,
					color: "#C96A3D",
					duration: 10f,
					iconId: XjEventIconCatalog.HistoryWorld);
				return;
			}

			float externalChance = XjGuoWeiQuanBingRules.ExternalZhengWeiAuthoritySeizeChance * chanceMultiplier;
			if (PositiveRoll(killerId + victim.ActorId, snapshot.Year, "quanbing_external_zhengwei") >= (int)(externalChance * 100f)) return;
			string externalAuthority = First(victimAuthorities);
			if (string.IsNullOrWhiteSpace(externalAuthority)) return;
			RecordCopy(killerState, killerState.LocalQuanBing,
				killerState.SeizedQuanBing,
				Merge(killerState.ForeignQuanBing, externalAuthority),
				"已归洞天合道", killerState.GuoWeiZhongAi,
				victim.DaoTu,
				0,
				true, snapshot.Year + XjGuoWeiQuanBingRules.ExternalAuthorityIntegrationYears,
				"夺取外道正位权柄，退入洞天合道",
				killerState.SeizedQuanBingSources);
			XjClosedCultivationGuard.MarkClosedCultivation(killer, true);
			XjQuanBingStruggleSystem.NotifyAuthoritySeized(killer, victim.DaoTu, externalAuthority, true);
			return;
		}

		// 余位、闰位的本地权柄仅能被外道金丹夺取；同道之间不互夺本地权柄。
		if ((!victimIsYuWei && !victimIsRunWei) || sameDaoTu) return;
		float localChance = XjGuoWeiQuanBingRules.LocalQuanBingSeizeChance * chanceMultiplier;
		if (PositiveRoll(killerId + victim.ActorId, snapshot.Year, "quanbing_local_seize") >= (int)(localChance * 100f)) return;

		string one = First(victimAuthorities);
		if (string.IsNullOrWhiteSpace(one)) return;
		string oneSource = BuildSeizedAuthoritySources(one, victim.ActorName, victim.GuoWei, "夺取");
		RecordCopy(killerState, killerState.LocalQuanBing,
			Merge(killerState.SeizedQuanBing, one),
			killerState.ForeignQuanBing,
			killerState.WithdrawnToDongTian, killerState.GuoWeiZhongAi,
			killerState.PendingExternalZhengWeiDaoTu,
			killerState.LockUntilYear,
			killerState.IntegrationRetreatActive, killerState.IntegrationRetreatEndYear,
			"夺取权柄",
			MergeAuthoritySources(killerState.SeizedQuanBingSources, oneSource));
		XjQuanBingStruggleSystem.NotifyAuthoritySeized(killer, victim.DaoTu, one, false);
	}

	private static bool CanInheritSameDaoTuZhengWei(in XjGuoWeiQuanBingState killerState)
	{
		string guoWei = killerState.GuoWei ?? string.Empty;
		return guoWei.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
			|| guoWei.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal);
	}

	private static bool TryPromoteSameDaoTuSuccessor(
		Actor killer,
		in XjGuoWeiQuanBingState killerState,
		in XjGuoWeiQuanBingState victim,
		int currentYear)
	{
		if (killer?.data == null
			|| string.IsNullOrWhiteSpace(victim.GuoWei)
			|| string.IsNullOrWhiteSpace(victim.DaoTu))
		{
			return false;
		}

		XjJinDanState killerJinDan = XjJinDanAccessor.BuildState(killer);
		if (!killerJinDan.Found
			|| !XjGuoWeiRegistry.TryClaim(
				killer,
				victim.DaoTu,
				killerJinDan.JinXing,
				victim.GuoWei,
				currentYear))
		{
			return false;
		}

		XjJinDanAccessor.WriteSuccess(
			killer,
			killerJinDan.JinXing,
			victim.GuoWei,
			killerJinDan.SuccessYear > 0 ? killerJinDan.SuccessYear : currentYear);

		int localCount = ResolveLocalAuthorityCount(killer, victim.GuoWei);
		string localAuthorities = string.Join(",", PickLocalAuthorities(victim.DaoTu, victim.GuoWei, localCount));
		string favored = string.IsNullOrWhiteSpace(killerState.GuoWeiZhongAi)
			? ResolveGuoWeiZhongAiOnZhengWei(killer, victim.DaoTu, victim.GuoWei, currentYear)
			: killerState.GuoWeiZhongAi;
		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true,
			killerState.ActorId,
			killerState.ActorName,
			victim.DaoTu,
			victim.GuoWei,
			localAuthorities,
			killerState.SeizedQuanBing,
			killerState.ForeignQuanBing,
			"已归洞天承继正位",
			favored,
			killerState.PendingExternalZhengWeiDaoTu,
			killerState.LockUntilYear,
			true,
			currentYear + XjGuoWeiQuanBingRules.ExternalAuthorityIntegrationYears,
			"承继同道正位，退入洞天稳固果位",
			"Active",
			killerState.AcquiredYear,
			0,
			string.Empty,
			killerState.SeizedQuanBingSources));
		XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(killer);
		XjClosedCultivationGuard.MarkClosedCultivation(killer, true);
		return true;
	}

	private static bool TryPromoteAdjacentDaoTuSuccessor(
		Actor killer,
		in XjGuoWeiQuanBingState killerState,
		in XjGuoWeiQuanBingState victim,
		int currentYear)
	{
		if (killer?.data == null
			|| string.IsNullOrWhiteSpace(victim.GuoWei)
			|| string.IsNullOrWhiteSpace(victim.DaoTu)
			|| !XjXianJiCatalog.IsAdjacentDaoTu(killerState.DaoTu, victim.DaoTu)
			|| !CanInheritSameDaoTuZhengWei(killerState)
			|| (killerState.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			return false;
		}

		XjJinDanState killerJinDan = XjJinDanAccessor.BuildState(killer);
		if (!killerJinDan.Found
			|| !XjGuoWeiRegistry.TryClaim(killer, victim.DaoTu, killerJinDan.JinXing, victim.GuoWei, currentYear))
		{
			return false;
		}

		string previousDaoTu = killerState.DaoTu;
		if (!XjCultivationStateTransitions.TrySetDaoTuMetadataOnly(killer, victim.DaoTu, true))
		{
			RollbackAdjacentZhengWeiClaim(killer, killerJinDan, previousDaoTu, victim.GuoWei, currentYear);
			return false;
		}

		XjJinDanAccessor.WriteSuccess(
			killer,
			killerJinDan.JinXing,
			victim.GuoWei,
			killerJinDan.SuccessYear > 0 ? killerJinDan.SuccessYear : currentYear);
		XjJinDanState promotedState = XjJinDanAccessor.BuildState(killer);
		if (!promotedState.Found || !string.Equals(promotedState.GuoWei, victim.GuoWei, StringComparison.Ordinal))
		{
			RollbackAdjacentZhengWeiClaim(killer, killerJinDan, previousDaoTu, victim.GuoWei, currentYear);
			return false;
		}
		int localCount = ResolveLocalAuthorityCount(killer, victim.GuoWei);
		string localAuthorities = string.Join(",", PickLocalAuthorities(victim.DaoTu, victim.GuoWei, localCount));
		string favored = ResolveGuoWeiZhongAiOnZhengWei(killer, victim.DaoTu, victim.GuoWei, currentYear);
		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true,
			killerState.ActorId,
			killerState.ActorName,
			victim.DaoTu,
			victim.GuoWei,
			localAuthorities,
			killerState.SeizedQuanBing,
			killerState.ForeignQuanBing,
			"已归洞天承继相邻正位",
			favored,
			string.Empty,
			0,
			true,
			currentYear + XjGuoWeiQuanBingRules.ExternalAuthorityIntegrationYears,
			"由" + previousDaoTu + "余闰位转入相邻道途，承继正位并退入洞天稳固果位",
			"Active",
			killerState.AcquiredYear,
			0,
			string.Empty,
			killerState.SeizedQuanBingSources));
		XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(killer);
		XjClosedCultivationGuard.MarkClosedCultivation(killer, true);
		return true;
	}

	private static void RollbackAdjacentZhengWeiClaim(
		Actor killer,
		in XjJinDanState originalState,
		string originalDaoTu,
		string claimedGuoWei,
		int currentYear)
	{
		if (killer?.data == null) return;
		long actorId = ((BaseSystemData)killer.data).id;
		XjGuoWeiRegistry.ReleaseForActor(actorId, claimedGuoWei);
		XjCultivationStateTransitions.TrySetDaoTuMetadataOnly(killer, originalDaoTu, true);
		XjJinDanAccessor.WriteSuccess(
			killer,
			originalState.JinXing,
			originalState.GuoWei,
			originalState.SuccessYear > 0 ? originalState.SuccessYear : currentYear);
		if (!string.IsNullOrWhiteSpace(originalState.GuoWei))
		{
			XjGuoWeiRegistry.TryClaim(
				killer,
				originalDaoTu,
				originalState.JinXing,
				originalState.GuoWei,
				originalState.SuccessYear > 0 ? originalState.SuccessYear : currentYear);
		}
	}

	private static Actor ResolveKnownActor(long actorId)
	{
		return XjScheduler.ResolveActor(actorId, out Actor actor) ? actor : null;
	}

	private static List<string> PickLocalAuthorities(string daoTu, string guoWei, int count)
	{
		List<string> result = new List<string>();
		IReadOnlyList<string> catalog = XjGuoWeiAuthorityCatalog.Get(daoTu);
		if (catalog == null || catalog.Count == 0 || count <= 0)
		{
			return result;
		}

		int startIndex = 0;
		int candidateCount = catalog.Count;
		if ((guoWei ?? string.Empty).Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			startIndex = (ResolveGuoWeiSlotIndex(guoWei) - 1) % XjGuoWeiQuanBingRules.YuWeiSlotCount * 2;
			candidateCount = 2;
		}
		else if ((guoWei ?? string.Empty).Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			startIndex = (ResolveGuoWeiSlotIndex(guoWei) - 1) % XjGuoWeiQuanBingRules.RunWeiSlotCount;
			candidateCount = 1;
		}

		for (int offset = 0; offset < candidateCount && result.Count < count; offset++)
		{
			int index = startIndex + offset;
			if (index < 0 || index >= catalog.Count)
			{
				continue;
			}

			string authority = catalog[index];
			if (!XjGuoWeiQuanBingRegistry.IsAuthorityLost(daoTu, authority))
			{
				result.Add(authority);
			}
		}
		return result;
	}

	private static int ResolveGuoWeiSlotIndex(string guoWei)
	{
		string value = guoWei ?? string.Empty;
		string type = value.EndsWith(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
			? XjGuoWeiCalculator.YuWei
			: value.EndsWith(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				? XjGuoWeiCalculator.RunWei
				: string.Empty;
		for (int i = 2; i <= 9; i++)
		{
			if (value.EndsWith(ToChineseOrdinal(i) + type, StringComparison.Ordinal))
			{
				return i;
			}
		}

		return 1;
	}

	private static string ToChineseOrdinal(int value)
	{
		return value switch
		{
			2 => "二",
			3 => "三",
			4 => "四",
			5 => "五",
			6 => "六",
			7 => "七",
			8 => "八",
			9 => "九",
			_ => string.Empty
		};
	}

	private static void RefreshLocalAuthoritiesForStage(Actor actor, in XjGuoWeiQuanBingState state)
	{
		int desiredCount = ResolveLocalAuthorityCount(actor, state.GuoWei);
		List<string> local = PickLocalAuthorities(state.DaoTu, state.GuoWei, desiredCount);
		string desired = string.Join(",", local);
		if (string.Equals(state.LocalQuanBing ?? string.Empty, desired, StringComparison.Ordinal))
		{
			return;
		}

		RecordCopy(state, desired, state.SeizedQuanBing, state.ForeignQuanBing, state.WithdrawnToDongTian,
			state.GuoWeiZhongAi, state.PendingExternalZhengWeiDaoTu, state.LockUntilYear, state.IntegrationRetreatActive,
			state.IntegrationRetreatEndYear, "权柄随金丹道行显化");
	}

	private static int ResolveLocalAuthorityCount(Actor actor, string guoWei)
	{
		int stage = ResolveJinDanStageIndex(actor);
		string value = guoWei ?? string.Empty;
		if (value.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			return stage switch
			{
				0 => 2,
				1 => 4,
				_ => XjGuoWeiQuanBingRules.QuanBingCountPerDaoTu
			};
		}

		if (value.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			return stage switch
			{
				0 => 0,
				1 => 1,
				_ => 2
			};
		}

		if (value.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			return stage >= 2 ? 1 : 0;
		}

		return 0;
	}

	private static void RecordPermanentLostAuthorities(string sourceDaoTu, string targetDaoTu, string authorities, int year)
	{
		if (string.IsNullOrWhiteSpace(sourceDaoTu) || string.IsNullOrWhiteSpace(authorities))
		{
			return;
		}

		string[] parts = authorities.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(parts[i]))
			{
				XjGuoWeiQuanBingRegistry.RecordLostAuthority(sourceDaoTu, parts[i], targetDaoTu, year, "外道权柄合道完成");
			}
		}
	}

	private static string First(string raw)
	{
		string[] parts = (raw ?? string.Empty).Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
		return parts.Length == 0 ? string.Empty : parts[0].Trim();
	}

	private static string Merge(string left, string right)
	{
		HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
		foreach (string raw in new[] { left, right })
		{
			string[] parts = (raw ?? string.Empty).Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++) if (!string.IsNullOrWhiteSpace(parts[i])) values.Add(parts[i].Trim());
		}
		return string.Join(",", values);
	}

	private static string ResolveGuoWeiZhongAiOnZhengWei(Actor actor, string daoTu, string guoWei, int year)
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(daoTu)
			|| string.IsNullOrWhiteSpace(guoWei)
			|| !guoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			return string.Empty;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return string.Empty;
		}

		int chance = (int)Math.Floor(GetZhengWeiGuoWeiZhongAiChance(actor) * 10000f);
		int roll = XjDeterministicHash.PositiveIndex(actorId + year, daoTu + "|guowei_zhongai", 10000);
		return roll < chance ? "果位钟爱" : string.Empty;
	}

	private static float GetZhengWeiGuoWeiZhongAiChance(Actor actor)
	{
		switch (ResolveJinDanStageIndex(actor))
		{
			case 3:
				return 0.8f;
			case 2:
				return 0.6f;
			case 1:
				return 0.4f;
			default:
				return 0.2f;
		}
	}

	private static int ResolveJinDanStageIndex(Actor actor)
	{
		if (actor?.data == null || !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang))
		{
			return 0;
		}

		yiXiang = XjFaBaoBonusService.GetEffectiveJinDanYiXiang(actor, yiXiang);

		if (yiXiang >= 6000)
		{
			return 3;
		}

		if (yiXiang >= 3000)
		{
			return 2;
		}

		if (yiXiang >= 1000)
		{
			return 1;
		}

		return 0;
	}

	private static void RecordCopy(in XjGuoWeiQuanBingState state, string local, string seized, string foreign, string withdrawn,
		string favored, string pendingDaoTu, int lockUntil, bool retreat, int retreatEnd, string summary, string seizedSources = null)
	{
		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true, state.ActorId, state.ActorName, state.DaoTu, state.GuoWei, local, seized, foreign, withdrawn,
			favored, pendingDaoTu, lockUntil, retreat, retreatEnd, summary, state.LifecycleStatus,
			state.AcquiredYear, state.ReleasedYear, state.ReleaseReason, seizedSources ?? state.SeizedQuanBingSources));
	}

	private static string BuildSeizedAuthoritySources(string authorities, string sourceName, string sourceGuoWei, string reason)
	{
		if (string.IsNullOrWhiteSpace(authorities))
		{
			return string.Empty;
		}

		string source = string.IsNullOrWhiteSpace(sourceName) ? "未知来源" : sourceName.Trim();
		if (!string.IsNullOrWhiteSpace(sourceGuoWei))
		{
			source += "/" + sourceGuoWei.Trim();
		}

		if (!string.IsNullOrWhiteSpace(reason))
		{
			source += "/" + reason.Trim();
		}

		List<string> entries = new List<string>();
		string[] parts = authorities.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			string authority = parts[i]?.Trim();
			if (!string.IsNullOrWhiteSpace(authority))
			{
				entries.Add(authority + ":" + source);
			}
		}

		return string.Join(";", entries);
	}

	private static string MergeAuthoritySources(string left, string right)
	{
		HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
		foreach (string raw in new[] { left, right })
		{
			string[] parts = (raw ?? string.Empty).Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(parts[i]))
				{
					values.Add(parts[i].Trim());
				}
			}
		}

		return string.Join(";", values);
	}

	private static int PositiveRoll(long seed, int year, string salt)
	{
		return XjDeterministicHash.PositiveIndex(seed + year, salt, 100);
	}
}
