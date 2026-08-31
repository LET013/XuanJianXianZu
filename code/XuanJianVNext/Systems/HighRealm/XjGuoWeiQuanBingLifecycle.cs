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
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.DongTian;

using XuanJianVNext.Systems.History;
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
		List<string> local = PickLocalAuthorities(actor, daoTu, guoWei, count);
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
			? (lockUntilYear > 0 ? "果位初定，承继转世果位封锁" : "果位初定，承继转世果位钟爱")
			: "果位初定";
		string inherited = ResolveIntegratedAuthoritySetForPosition(actor, daoTu);
		string inheritedSources = ResolveIntegratedAuthoritySourcesForPosition(actor, daoTu);
		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true, actorId, actor.getName(), daoTu, guoWei, string.Join(",", local),
			inherited, string.Empty, string.Empty, favored, pendingDaoTu, lockUntilYear, false, 0,
			forceFavored ? "果位初定，果位钟爱应验" : summary, "Active", year, 0, string.Empty,
			inheritedSources));
		if (forceFavored) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjForceGuoWeiZhongAiOnJinDan, 0);
	}

	/// <summary>
	/// 道行/修持跨过显权阈值后，补齐本地权柄快照。只处理已经登记的高境角色，
	/// 不重复增加道统兴盛度，也不改动余闰的根道/显道。
	/// </summary>
	internal static void RefreshProgressAuthorities(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		XjJinDanState jinDan = XjJinDanAccessor.BuildPositionCarrierState(actor);
		if (!jinDan.Found) return;
		if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string restoredDaoTu);
			if (string.IsNullOrWhiteSpace(restoredDaoTu))
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out restoredDaoTu);
			InitializeOnJinDan(actor, restoredDaoTu, jinDan.GuoWei, jinDan.SuccessYear > 0 ? jinDan.SuccessYear : currentYear);
			if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out state)) return;
		}

		int desiredCount = ResolveLocalAuthorityCount(actor, state.GuoWei);
		List<string> desired = PickLocalAuthorities(actor, state.DaoTu, state.GuoWei, desiredCount);
		string primaryDesiredText = string.Join(",", desired);
		string desiredText = BuildEffectiveLocalAuthorityText(actor, primaryDesiredText);
		string inherited = ResolveIntegratedAuthoritySetForPosition(actor, state.DaoTu);
		string inheritedSources = ResolveIntegratedAuthoritySourcesForPosition(actor, state.DaoTu);
		bool localSame = string.Equals(NormalizeAuthoritySet(state.LocalQuanBing), NormalizeAuthoritySet(desiredText), StringComparison.Ordinal);
		bool inheritedSame = string.Equals(NormalizeAuthoritySet(state.SeizedQuanBing), NormalizeAuthoritySet(inherited), StringComparison.Ordinal)
			&& string.Equals(state.SeizedQuanBingSources ?? string.Empty, inheritedSources, StringComparison.Ordinal);
		if (localSame && inheritedSame) return;

		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true, state.ActorId, state.ActorName, state.DaoTu, state.GuoWei, desiredText,
			inherited, state.ForeignQuanBing, state.WithdrawnToDongTian,
			state.GuoWeiZhongAi, state.PendingExternalZhengWeiDaoTu, state.LockUntilYear,
			state.IntegrationRetreatActive, state.IntegrationRetreatEndYear,
			localSame ? "果位融权承继同步" : "道行渐深，本道权柄由潜而显", state.LifecycleStatus, state.AcquiredYear,
			state.ReleasedYear, state.ReleaseReason, inheritedSources));
		XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(actor);

		string positionType = XjGuoWeiRegistry.ResolveTypeFromName(state.GuoWei);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanAuthorityScope, out string existingScope);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanAuthorityScope,
			MergeAuthorityScope(existingScope, primaryDesiredText));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string sourceDaoTu);
		XjDaoLineageStateRegistry.OnPromotion(
			actorId, actor.getName(), sourceDaoTu, state.DaoTu, positionType, primaryDesiredText.Replace(',', '|'),
			currentYear, affectVitality: false);
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

	internal static bool TryGrantRandomAdjacentDaoTuAuthority(Actor actor, int year)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		if (!jinDan.Found) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		if (string.IsNullOrWhiteSpace(daoTu)) return false;

		if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state))
		{
			InitializeOnJinDan(actor, daoTu, jinDan.GuoWei, year);
			if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out state)) return false;
		}

		string currentDaoTu = string.IsNullOrWhiteSpace(state.DaoTu) ? daoTu.Trim() : state.DaoTu.Trim();
		List<string> adjacentDaoTus = BuildAdjacentDaoTuAuthorityCandidates(currentDaoTu);
		Shuffle(adjacentDaoTus);
		for (int i = 0; i < adjacentDaoTus.Count; i++)
		{
			string sourceDaoTu = adjacentDaoTus[i];
			IReadOnlyList<string> authorities = XjGuoWeiAuthorityCatalog.Get(sourceDaoTu);
			List<int> indices = BuildShuffledIndices(authorities.Count);
			for (int j = 0; j < indices.Count; j++)
			{
				string authority = authorities[indices[j]];
				if (string.IsNullOrWhiteSpace(authority)
					|| XjGuoWeiQuanBingRegistry.IsAuthorityLost(sourceDaoTu, authority)
					|| StateContainsAuthority(state, authority))
				{
					continue;
				}

				using (XjHighRealmAggregateStore.BeginReduction(actorId, year))
				{
					if (!XjDaoLineageStateRegistry.OnAuthoritySeized(
						actorId, actor.getName(), sourceDaoTu, currentDaoTu, authority, year, true)) continue;
					// 天道干涉同样只把权柄融入当前道途果位。受益者得到功绩与一次夺柄经历，
					// 但不是把“易”柄永久绑在个人身上。
					SyncActiveFruitIntegratedAuthorities(currentDaoTu, year, "天道干涉，外道权柄归入果位");
					RefreshProgressAuthorities(actor, year);
					XjThreeBookWriter.RecordQuanBingSeized(actor, sourceDaoTu, authority, year, "天道干涉");
					XjQuanBingStruggleSystem.NotifyAuthoritySeized(actor, sourceDaoTu, authority, false);
					XjShenTongMutationService.OnAuthoritySeized(actor, sourceDaoTu, authority, year, "天道干涉");
					return true;
				}
			}
		}

		return false;
	}

	internal static bool TryBuildReadOnlyRecoveryState(Actor actor, out XjGuoWeiQuanBingState state)
	{
		state = default;
		if (actor?.data == null || !actor.isAlive())
		{
			return false;
		}

		XjJinDanState jinDan = XjJinDanAccessor.BuildPositionCarrierState(actor);
		if (!jinDan.Found)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu);
		XjHighRealmDaoStateService.ResolvePositionIdentity(
			actor, jinDan.GuoWei, out _, out string daoTu);
		if (string.IsNullOrWhiteSpace(daoTu)) daoTu = actorDaoTu;
		string guoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(jinDan.GuoWei);
		string guoWeiType = XjGuoWeiRegistry.ResolveTypeFromName(guoWei);
		if (string.Equals(guoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			&& !string.Equals(
				XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(guoWei), daoTu, StringComparison.Ordinal))
		{
			guoWei = XjGuoWeiCalculator.BuildGuoWeiSlotName(
				daoTu, guoWeiType, XjGuoWeiCalculator.ResolveSlotIndex(guoWei));
		}
		if (string.IsNullOrWhiteSpace(daoTu) || string.IsNullOrWhiteSpace(guoWei))
		{
			return false;
		}

		int acquiredYear = Math.Max(0, jinDan.SuccessYear);
		int count = ResolveLocalAuthorityCount(actor, guoWei);
		List<string> local = PickLocalAuthorities(actor, daoTu, guoWei, count);
		string effectiveLocal = BuildEffectiveLocalAuthorityText(actor, string.Join(",", local));
		state = new XjGuoWeiQuanBingState(
			true,
			actorId,
			actor.getName(),
			daoTu,
			guoWei,
			effectiveLocal,
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
		string summary = "果位钟爱转世承载，意象不足未入果位封锁";
		if (yiXiang >= XjGuoWeiQuanBingRules.PendingZhengWeiReincarnationYiXiangCost)
		{
			int nextYiXiang = Math.Max(0, yiXiang - XjGuoWeiQuanBingRules.PendingZhengWeiReincarnationYiXiangCost);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, nextYiXiang);
			// 0.5.4锁期从原身陨落时开始，不因转世载体晚出生而重新延长500年。
			lockUntilYear = XjGuoWeiQuanBingRegistry.TryGetZhengWeiLock(daoTu, normalizedYear, out int inheritedLockUntilYear)
				? inheritedLockUntilYear
				: normalizedYear + XjGuoWeiQuanBingRules.ExternalZhengWeiLockYears;
			summary = "果位钟爱转世承载，果位封锁至" + XjChronology.FormatYear(lockUntilYear);
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

		RefreshLocalAuthoritiesForStage(actor, state, currentYear);
		if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out state)) return;

		// 融合闭关优先于战争强制出关。夺柄者可在五十年道争中退出十年，
		// 融合完成后若战争尚未结束，下一年度重新加入战场。
		if (state.IntegrationRetreatActive
			&& (state.IntegrationRetreatEndYear <= 0 || currentYear < state.IntegrationRetreatEndYear))
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
			return;
		}

		if (state.IntegrationRetreatActive && currentYear >= state.IntegrationRetreatEndYear)
		{
			if (string.IsNullOrWhiteSpace(state.ForeignQuanBing))
			{
				RecordCopy(state, state.LocalQuanBing, state.SeizedQuanBing, string.Empty, string.Empty,
					state.GuoWeiZhongAi, state.PendingExternalZhengWeiDaoTu, state.LockUntilYear, false, 0,
					"果位承继闭关完成", state.SeizedQuanBingSources);
				XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
			}
			else
			{
				string sourceDaoTu = state.PendingExternalZhengWeiDaoTu;
				string foreignAuthorities = state.ForeignQuanBing;
				bool adjacent = XjXianJiCatalog.IsAdjacentDaoTu(state.DaoTu, sourceDaoTu)
					|| XjXianJiCatalog.IsAdjacentDaoTu(sourceDaoTu, state.DaoTu);
				float successChance = XjGuoWeiQuanBingRules.NonAdjacentAuthorityIntegrationSuccessChance;
				if (adjacent)
				{
					string position = state.GuoWei ?? string.Empty;
					successChance = position.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) ? 1f
						: position.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal) ? 0.85f
						: position.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? 0.8f
						: successChance;
				}
				// 外道夺得“渊照本权”后若触发过水月应照，融合期间只是被迫把来路/异契
				// 说清楚，因此降低成功率而非被脚本判死；道尊不替天地直接裁定胜负。
				successChance = XjYuanZhaoFounderAudienceSystem.AdjustAuthorityIntegrationChance(
					actor, sourceDaoTu, currentYear, successChance);
				bool success = PositiveRoll(actorId, currentYear, "quanbing_integration") < (int)(successChance * 100f);
				string[] resolved = foreignAuthorities.Split(new[] { ',', '，', '|', '、' }, StringSplitOptions.RemoveEmptyEntries);
				// 正常入口每次只会产生一柄待融权柄。旧档若出现多柄混在同一
				// 闭关记录中，统一按失败归还，避免逐柄结算造成部分成功、部分失败。
				if (resolved.Length != 1) success = false;
				bool attemptedSuccess = success;
				bool stateResolved = resolved.Length > 0;
				for (int i = 0; i < resolved.Length; i++)
				{
					string authority = resolved[i]?.Trim();
					if (string.IsNullOrWhiteSpace(authority)) continue;
					stateResolved &= XjDaoLineageStateRegistry.OnAuthorityIntegrationResolved(
						actorId, actor.getName(), sourceDaoTu, state.DaoTu, authority, currentYear, success);
				}
				// 成功结算若因旧档配对不完整被拒绝，再沿失败链尝试归还，
				// 避免清空角色待融账本后仍把原道留在无来源的“裂”。
				if (!stateResolved && attemptedSuccess)
				{
					stateResolved = resolved.Length > 0;
					for (int i = 0; i < resolved.Length; i++)
					{
						string authority = resolved[i]?.Trim();
						if (string.IsNullOrWhiteSpace(authority)) continue;
						stateResolved &= XjDaoLineageStateRegistry.OnAuthorityIntegrationResolved(
							actorId, actor.getName(), sourceDaoTu, state.DaoTu, authority, currentYear, success: false);
					}
					success = false;
				}
				if (!stateResolved) success = false;

				// 合道成功的外道根柄归入“目标道途果位”而非夺柄者个人。
				// 夺柄者得到功绩与历史记录；谁真正持有该果位，谁承接这些“易”柄。
				if (success)
				{
					SyncActiveFruitIntegratedAuthorities(state.DaoTu, currentYear, "外道权柄融入果位");
				}
				if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState latest))
				{
					latest = state;
				}
				string inherited = ResolveIntegratedAuthoritySetForPosition(actor, latest.DaoTu);
				string inheritedSources = ResolveIntegratedAuthoritySourcesForPosition(actor, latest.DaoTu);
				RecordCopy(latest, latest.LocalQuanBing, inherited, string.Empty, string.Empty,
					latest.GuoWeiZhongAi, string.Empty, 0, false, 0,
					success ? "外道权柄合道完成，权柄归入本道果位" : "外道权柄合道未成，权柄遁回原道", inheritedSources);
				XjThreeBookWriter.RecordQuanBingIntegrated(actor, sourceDaoTu, foreignAuthorities, currentYear, success);
				if (!success) XjShenTongMutationService.OnAuthorityReturned(actor, currentYear, "外道权柄合道失败");
				XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
			}

			if (XjQuanBingStruggleSystem.IsFinalConflict) return;
			return;
		}

		// 兼容旧档中“已经持有临时外道权柄但尚未写入融合闭关”的状态。
		if (!state.IntegrationRetreatActive
			&& !XjSectDongTianLifecycle.IsRetreatActive(actor)
			&& !string.IsNullOrWhiteSpace(state.ForeignQuanBing))
		{
			RecordCopy(state, state.LocalQuanBing, state.SeizedQuanBing, state.ForeignQuanBing, "已归洞天合道",
				state.GuoWeiZhongAi, state.PendingExternalZhengWeiDaoTu, state.LockUntilYear, true,
				currentYear + XjGuoWeiQuanBingRules.ExternalAuthorityIntegrationYears, "外道权柄合道闭关");
			XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
			return;
		}

		if (XjQuanBingStruggleSystem.IsFinalConflict)
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
			return;
		}

		if (XjClosedCultivationGuard.IsInClosedCultivation(actor)
			&& !XjSectDongTianLifecycle.IsRetreatActive(actor))
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
			? "果位钟爱应验，该果位自陨落之年封锁至" + XjChronology.FormatYear(lockUntilYear)
			: "果位持柄者陨落；本道根柄归藏，已融外道权柄仍归果位道统";

		// 先把果位钟爱锁写入历史账本，再处理击杀者承继。这样锁期内的普通
		// 余位/闰位不会趁死者释放果位的同一结算瞬间抢占果位。
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

		// 先以死亡快照结算正位承继或外道夺柄；此时根权柄仍保持“执”，
		// 状态机可以精确验证被夺柄确由死者执掌。结算结束后，其余未被夺走
		// 的本道权柄才正常藏回道统，借柄则沿失败链归还原道。
		TryTransferOnDeath(snapshot, victim);
		XjDaoLineageStateRegistry.OnHolderReleased(
			victim.ActorId,
			victim.DaoTu,
			victim.GuoWei,
			string.Empty,
			snapshot.Year,
			penalizeVitality: true);
	}

	private static void TryTransferOnDeath(in XjDeathSnapshot snapshot, in XjGuoWeiQuanBingState victim)
	{
		if (snapshot.LastAttackerId <= 0L) return;
		Actor killer = ResolveKnownActor(snapshot.LastAttackerId);
		if (killer?.data == null) return;
		XjJinDanState killerCarrier = XjJinDanAccessor.BuildPositionCarrierState(killer);
		if (!killerCarrier.Found) return;
		long killerId = ((BaseSystemData)killer.data).id;
		XjQuanBingStruggleSystem.NotifyParticipantKill(killer, snapshot.ActorId);
		XjActorAccessor.TryGetString(killer, XjActorDataKeys.DaoTu, out string killerDaoTu);
		string killerGuoWei = killerCarrier.GuoWei;
		if (!XjGuoWeiQuanBingRegistry.TryGet(killerId, out XjGuoWeiQuanBingState killerState))
		{
			InitializeOnJinDan(killer, killerDaoTu, killerGuoWei, snapshot.Year);
			if (!XjGuoWeiQuanBingRegistry.TryGet(killerId, out killerState)) return;
		}

		// 常态死亡不发生权柄夺取。权柄之争期间为完整概率；受害者正在合道闭关时，
		// 允许以较低概率追夺。只有“真实果位持有者”才承载本道六柄以及融入果位的外道柄。
		float chanceMultiplier = XjQuanBingStruggleSystem.IsFinalConflict
			? 1f
			: (victim.IntegrationRetreatActive ? XjGuoWeiQuanBingRules.PostIntegrationSeizeChanceMultiplier : 0f);
		if (chanceMultiplier <= 0f) return;

		bool sameDaoTu = string.Equals(killerDaoTu, victim.DaoTu, StringComparison.Ordinal);
		bool victimIsZhengWei = (victim.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
		bool victimHoldsFruit = victimIsZhengWei;
		if (!victimHoldsFruit
			&& XjDaoLineageStateRegistry.TryResolveActiveFruitHolder(victim.DaoTu, out long resolvedFruitHolderId, out _)
			&& resolvedFruitHolderId == victim.ActorId)
		{
			// 余/闰成道胎后补得果位时，普通果位 Registry 没有第二条活动记录，
			// 但其双位账本中的果位同样是真实承柄位。
			victimHoldsFruit = true;
		}
		if (!victimHoldsFruit) return;

		string victimFruitGuoWei = victimIsZhengWei
			? victim.GuoWei
			: XjGuoWeiCalculator.BuildGuoWeiSlotName(victim.DaoTu, XjGuoWeiCalculator.ZhengWei, 1);

		if (sameDaoTu)
		{
			if (!CanInheritSameDaoTuZhengWei(killerState)) return;
			float successionChance = XjGuoWeiQuanBingRules.SameDaoTuZhengWeiInheritanceChance * chanceMultiplier;
			if (PositiveRoll(killerId + victim.ActorId, snapshot.Year, "quanbing_zhengwei_succession") >= (int)(successionChance * 100f)) return;
			if (!TryPromoteSameDaoTuSuccessor(killer, killerState, victim, victimFruitGuoWei, snapshot.Year)) return;
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

		float externalChance = XjGuoWeiQuanBingRules.ExternalZhengWeiAuthoritySeizeChance * chanceMultiplier;
		if (PositiveRoll(killerId + victim.ActorId, snapshot.Year, "quanbing_external_zhengwei") >= (int)(externalChance * 100f)) return;

		using (XjHighRealmAggregateStore.BeginReduction(killerId, snapshot.Year))
		{
			// 第一优先：从该果位仍真正执掌的本道六柄中夺一柄。直接读取根权柄目录，
			// 由状态机验证 HolderActorId，避免“余/闰为本位、道胎后补果位”的角色因个人快照
			// 没列出六柄而无法被正常夺权。
			IReadOnlyList<string> nativeRoots = XjGuoWeiAuthorityCatalog.Get(victim.DaoTu);
			string externalAuthority = string.Empty;
			for (int authorityIndex = 0; authorityIndex < nativeRoots.Count; authorityIndex++)
			{
				string candidate = nativeRoots[authorityIndex]?.Trim();
				if (string.IsNullOrWhiteSpace(candidate)) continue;
				if (!XjDaoLineageStateRegistry.OnAuthoritySeized(
					killerId, killer.getName(), victim.DaoTu, killerDaoTu, candidate, snapshot.Year, false, victim.ActorId)) continue;
				externalAuthority = candidate;
				break;
			}

			if (!string.IsNullOrWhiteSpace(externalAuthority))
			{
				RecordCopy(killerState, killerState.LocalQuanBing,
					ResolveIntegratedAuthoritySetForPosition(killer, killerDaoTu),
					Merge(killerState.ForeignQuanBing, externalAuthority),
					"已归洞天合道", killerState.GuoWeiZhongAi,
					victim.DaoTu,
					0,
					true, snapshot.Year + XjGuoWeiQuanBingRules.ExternalAuthorityIntegrationYears,
					"夺取外道果位权柄，退入洞天合道",
					ResolveIntegratedAuthoritySourcesForPosition(killer, killerDaoTu));
				XjClosedCultivationGuard.MarkClosedCultivation(killer, true);
				XjThreeBookWriter.RecordQuanBingSeized(killer, victim.DaoTu, externalAuthority, snapshot.Year, "夺取外道正位权柄");
				XjQuanBingStruggleSystem.NotifyAuthoritySeized(killer, victim.DaoTu, externalAuthority, true);
				XjShenTongMutationService.OnAuthoritySeized(killer, victim.DaoTu, externalAuthority, snapshot.Year, "夺取外道正位权柄");
				return;
			}

			// 第二优先：本道根柄已被夺尽或本次没有可夺根柄时，可再夺这枚果位此前融入的外道权柄。
			// 此类权柄已经完成过一次“易”，再夺时直接从旧果位迁入新道途果位，不随旧持柄者死亡消失。
			string integratedAuthorities = XjDaoLineageStateRegistry.BuildIntegratedAuthoritySet(victim.DaoTu);
			string integratedSources = XjDaoLineageStateRegistry.BuildIntegratedAuthoritySources(victim.DaoTu);
			string[] inherited = integratedAuthorities.Split(new[] { ',', '，', '|', '、' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < inherited.Length; i++)
			{
				string candidate = inherited[i]?.Trim();
				if (string.IsNullOrWhiteSpace(candidate)
					|| !TryResolveSeizedAuthoritySource(integratedSources, candidate, out string originalSourceDaoTu))
				{
					continue;
				}
				if (!XjDaoLineageStateRegistry.OnIntegratedAuthorityReseized(
					killerId, killer.getName(), originalSourceDaoTu, victim.DaoTu, killerDaoTu, candidate, snapshot.Year))
				{
					continue;
				}

				SyncActiveFruitIntegratedAuthorities(victim.DaoTu, snapshot.Year, "果位融权被夺");
				SyncActiveFruitIntegratedAuthorities(killerDaoTu, snapshot.Year, "夺得已融权柄");
				RefreshProgressAuthorities(killer, snapshot.Year);
				// 夺柄者未必就是本道果位持有者。尤其原道余/闰修士替本道夺回根柄时，
				// 真正承接权柄的是现任果位，因此还要即时刷新果主持柄快照，不能等下一年。
				if (XjDaoLineageStateRegistry.TryResolveActiveFruitHolder(killerDaoTu, out long targetFruitHolderId, out _)
					&& targetFruitHolderId > 0L && targetFruitHolderId != killerId
					&& XjActorRegistry.ResolveKnownOrWorld(targetFruitHolderId, out Actor targetFruitHolder)
					&& XjSafeCore.IsAliveActor(targetFruitHolder))
				{
					RefreshProgressAuthorities(targetFruitHolder, snapshot.Year);
				}
				XjThreeBookWriter.RecordQuanBingSeized(killer, originalSourceDaoTu, candidate, snapshot.Year, "夺取已融果位权柄");
				XjQuanBingStruggleSystem.NotifyAuthoritySeized(killer, originalSourceDaoTu, candidate, true);
				XjShenTongMutationService.OnAuthoritySeized(killer, originalSourceDaoTu, candidate, snapshot.Year, "夺取已融果位权柄");
				return;
			}
		}
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
		string fruitGuoWei,
		int currentYear)
	{
		if (killer?.data == null
			|| string.IsNullOrWhiteSpace(fruitGuoWei)
			|| string.IsNullOrWhiteSpace(victim.DaoTu))
		{
			return false;
		}

		XjJinDanState killerJinDan = XjJinDanAccessor.BuildState(killer);
		long actorId = ((BaseSystemData)killer.data).id;
		string newJinXing = XjJinXingCalculator.Calculate(victim.DaoTu, actorId);
		if (string.IsNullOrWhiteSpace(newJinXing)) newJinXing = killerJinDan.JinXing;
		if (!killerJinDan.Found
			|| !XjGuoWeiRegistry.TryClaim(
				killer,
				victim.DaoTu,
				newJinXing,
				fruitGuoWei,
				currentYear))
		{
			return false;
		}

		XjGuoWeiRegistry.ReleaseForActor(actorId, killerJinDan.GuoWei);
		XjDaoLineageStateRegistry.OnHolderReleased(actorId, killerState.DaoTu, killerJinDan.GuoWei,
			string.Empty, currentYear, penalizeVitality: false);
		XjJinDanAccessor.WriteSuccess(
			killer,
			newJinXing,
			fruitGuoWei,
			killerJinDan.SuccessYear > 0 ? killerJinDan.SuccessYear : currentYear);
		string practice = (killerState.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			? "变" : "移";
		XjHighRealmDaoStateService.ApplyRespectPositionChange(
			killer, XjGuoWeiCalculator.ZhengWei, fruitGuoWei, newJinXing, currentYear, practice);

		int localCount = ResolveLocalAuthorityCount(killer, fruitGuoWei);
		string localAuthorities = string.Join(",", PickLocalAuthorities(killer, victim.DaoTu, fruitGuoWei, localCount));
		string favored = string.IsNullOrWhiteSpace(killerState.GuoWeiZhongAi)
			? ResolveGuoWeiZhongAiOnZhengWei(killer, victim.DaoTu, fruitGuoWei, currentYear)
			: killerState.GuoWeiZhongAi;
		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true,
			killerState.ActorId,
			killerState.ActorName,
			victim.DaoTu,
			fruitGuoWei,
			localAuthorities,
			XjDaoLineageStateRegistry.BuildIntegratedAuthoritySet(victim.DaoTu),
			killerState.ForeignQuanBing,
			"已归洞天承继果位",
			favored,
			killerState.PendingExternalZhengWeiDaoTu,
			killerState.LockUntilYear,
			true,
			currentYear + XjGuoWeiQuanBingRules.ExternalAuthorityIntegrationYears,
			"承继同道果位，退入洞天稳固果位",
			"Active",
			killerState.AcquiredYear,
			0,
			string.Empty,
			XjDaoLineageStateRegistry.BuildIntegratedAuthoritySources(victim.DaoTu)));
		XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(killer);
		XjDaoLineageStateRegistry.OnNativeAuthoritySucceeded(
			victim.ActorId, actorId, killer.getName(), victim.DaoTu, localAuthorities, currentYear);
		SyncActiveFruitIntegratedAuthorities(victim.DaoTu, currentYear, "承继果位及其融权");
		XjClosedCultivationGuard.MarkClosedCultivation(killer, true);
		XjThreeBookWriter.RecordZhengWeiSuccession(killer, victim.ActorName, victim.DaoTu, fruitGuoWei, currentYear, false);
		return true;
	}

	private static Actor ResolveKnownActor(long actorId)
	{
		return XjScheduler.ResolveActor(actorId, out Actor actor) ? actor : null;
	}

	private static List<string> PickLocalAuthorities(string daoTu, string guoWei, int count)
	{
		return PickLocalAuthorities(null, daoTu, guoWei, count);
	}

	private static List<string> PickLocalAuthorities(Actor actor, string daoTu, string guoWei, int count)
	{
		List<string> result = new List<string>();
		if (count <= 0) return result;

		string normalizedGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		string type = XjGuoWeiRegistry.ResolveTypeFromName(normalizedGuoWei);
		IReadOnlyList<string> catalog = XjDaoLineageStateRegistry.GetUsableAuthorityNames(
			daoTu, allowDormantFallback: string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal));

		// 0.9.6.5 起，证道时已经把真实“权辖”固化到角色。
		// 果位从显权中逐项展开；闰位优先保留目标道、借入根道和交感权柄，
		// 使实际权柄快照与“借X闰Y”的宣法一致，而不是重新套固定目录。
		if (actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanAuthorityScope, out string storedScope)
			&& !string.IsNullOrWhiteSpace(storedScope))
		{
			List<string> scoped = BuildScopedAuthorityCandidates(actor, daoTu, type, storedScope);
			HashSet<string> allowedZhengWei = string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
				? new HashSet<string>(catalog ?? Array.Empty<string>(), StringComparer.Ordinal)
				: null;
			for (int i = 0; i < scoped.Count && result.Count < count; i++)
			{
				// 正位旧档权辖必须重新通过当前状态机可用目录校验，
				// 避免把已经失、裂的根柄或旧派生词继续写入实际持柄快照。
				if (allowedZhengWei != null && !allowedZhengWei.Contains(scoped[i])) continue;
				TryAddDerivedAuthority(result, scoped[i], count);
			}
		}

		if (string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			if (catalog != null)
			{
				for (int i = 0; i < catalog.Count && result.Count < count; i++)
				{
					if (!XjGuoWeiQuanBingRegistry.IsAuthorityLost(daoTu, catalog[i]))
						TryAddDerivedAuthority(result, catalog[i], count);
				}
			}
			return result;
		}

		// 新制角色若已有权辖，余闰优先使用其真实显权/借权/交感权；
		// 数量不足时才读取首次创证位置的稳定派生权柄。
		if (result.Count >= count) return result;
		if (XjFruitPositionWorldState.TryGetPosition(normalizedGuoWei, out XjDerivedPositionArchiveRecord position))
		{
			TryAddDerivedAuthority(result, position.DerivedAuthority, count);
			TryAddDerivedAuthority(result, position.SecondaryDerivedAuthority, count);
			if (result.Count < count
				&& !string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				TryAddDerivedAuthority(
					result,
					XjDerivedAuthorityNameBuilder.Build(
						daoTu,
						type,
						position.SecondaryAuthority,
						position.PrimaryAuthority,
						position.ExternalDaoTu,
						position.SlotIndex + 17,
						position.FounderActorId + position.FoundedYear + 7919L),
					count);
			}
			return result;
		}

		if (catalog == null || catalog.Count == 0) return result;
		// 极早期旧档若在模块位置文档补齐前请求只读恢复，按位置ID生成
		// 稳定派生名；绝不把果位的完整根权柄直接发给余闰持有者。
		int slot = ResolveGuoWeiSlotIndex(normalizedGuoWei);
		int primaryIndex = Math.Max(0, slot - 1) % catalog.Count;
		int secondaryIndex = catalog.Count > 1 ? (primaryIndex + 1) % catalog.Count : primaryIndex;
		string primary = catalog[primaryIndex];
		string secondary = catalog[secondaryIndex];
		TryAddDerivedAuthority(
			result,
			XjDerivedAuthorityNameBuilder.Build(daoTu, type, primary, secondary, string.Empty, slot, 0L),
			count);
		if (!string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			TryAddDerivedAuthority(
				result,
				XjDerivedAuthorityNameBuilder.Build(daoTu, type, secondary, primary, string.Empty, slot + 17, 7919L),
				count);
		}
		return result;
	}

	private static List<string> BuildScopedAuthorityCandidates(
		Actor actor, string manifestDaoTu, string positionType, string storedScope)
	{
		List<string> raw = new List<string>();
		string[] parts = (storedScope ?? string.Empty).Split(
			new[] { '|', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			string value = parts[i]?.Trim();
			if (!string.IsNullOrWhiteSpace(value) && !raw.Contains(value)) raw.Add(value);
		}
		if (!string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) return raw;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string sourceDaoTu);
		IReadOnlyList<string> manifestCatalog = XjGuoWeiAuthorityCatalog.Get(manifestDaoTu);
		IReadOnlyList<string> sourceCatalog = XjGuoWeiAuthorityCatalog.Get(sourceDaoTu);
		List<string> target = new List<string>();
		List<string> borrowed = new List<string>();
		List<string> crossed = new List<string>();
		List<string> other = new List<string>();
		for (int i = 0; i < raw.Count; i++)
		{
			string value = raw[i];
			if (value.EndsWith("交感", StringComparison.Ordinal)) crossed.Add(value);
			else if (ContainsAuthorityName(sourceCatalog, value)
				&& !string.Equals(sourceDaoTu?.Trim(), manifestDaoTu?.Trim(), StringComparison.Ordinal)) borrowed.Add(value);
			else if (ContainsAuthorityName(manifestCatalog, value)) target.Add(value);
			else other.Add(value);
		}

		List<string> ordered = new List<string>();
		if (target.Count > 0) ordered.Add(target[0]);
		if (borrowed.Count > 0) ordered.Add(borrowed[0]);
		if (crossed.Count > 0) ordered.Add(crossed[0]);
		for (int i = 1; i < target.Count; i++) if (!ordered.Contains(target[i])) ordered.Add(target[i]);
		for (int i = 1; i < borrowed.Count; i++) if (!ordered.Contains(borrowed[i])) ordered.Add(borrowed[i]);
		for (int i = 1; i < crossed.Count; i++) if (!ordered.Contains(crossed[i])) ordered.Add(crossed[i]);
		for (int i = 0; i < other.Count; i++) if (!ordered.Contains(other[i])) ordered.Add(other[i]);
		return ordered;
	}

	private static bool ContainsAuthorityName(IReadOnlyList<string> values, string target)
	{
		if (values == null || string.IsNullOrWhiteSpace(target)) return false;
		for (int i = 0; i < values.Count; i++)
		{
			if (string.Equals(values[i], target, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static List<string> BuildAdjacentDaoTuAuthorityCandidates(string currentDaoTu)
	{
		List<string> result = new List<string>();
		if (string.IsNullOrWhiteSpace(currentDaoTu)) return result;
		IReadOnlyList<string> daoTus = XjGuoWeiAuthorityCatalog.GetAllDaoTus();
		for (int i = 0; i < daoTus.Count; i++)
		{
			string daoTu = daoTus[i];
			if (string.IsNullOrWhiteSpace(daoTu)
				|| !XjXianJiCatalog.IsAdjacentDaoTu(currentDaoTu, daoTu)
				|| XjGuoWeiAuthorityCatalog.Get(daoTu).Count == 0)
			{
				continue;
			}
			result.Add(daoTu.Trim());
		}
		return result;
	}

	private static List<int> BuildShuffledIndices(int count)
	{
		List<int> result = new List<int>();
		for (int i = 0; i < count; i++) result.Add(i);
		Shuffle(result);
		return result;
	}

	private static void Shuffle<T>(List<T> values)
	{
		if (values == null) return;
		for (int i = values.Count - 1; i > 0; i--)
		{
			int index = UnityEngine.Random.Range(0, i + 1);
			T value = values[i];
			values[i] = values[index];
			values[index] = value;
		}
	}


	private static string ResolveIntegratedAuthoritySetForPosition(Actor actor, string daoTu)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu)) return string.Empty;
		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjDaoLineageStateRegistry.TryResolveActiveFruitHolder(daoTu, out long holderId, out _)
			&& holderId == actorId
				? XjDaoLineageStateRegistry.BuildIntegratedAuthoritySet(daoTu)
				: string.Empty;
	}

	private static string ResolveIntegratedAuthoritySourcesForPosition(Actor actor, string daoTu)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu)) return string.Empty;
		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjDaoLineageStateRegistry.TryResolveActiveFruitHolder(daoTu, out long holderId, out _)
			&& holderId == actorId
				? XjDaoLineageStateRegistry.BuildIntegratedAuthoritySources(daoTu)
				: string.Empty;
	}

	private static void SyncActiveFruitIntegratedAuthorities(string daoTu, int currentYear, string summary)
	{
		if (string.IsNullOrWhiteSpace(daoTu)
			|| !XjDaoLineageStateRegistry.TryResolveActiveFruitHolder(daoTu, out long holderId, out _)
			|| holderId <= 0L
			|| !XjGuoWeiQuanBingRegistry.TryGet(holderId, out XjGuoWeiQuanBingState holderState)
			|| !holderState.Found
			|| !string.Equals(holderState.LifecycleStatus, "Active", StringComparison.Ordinal))
		{
			return;
		}

		string desired = XjDaoLineageStateRegistry.BuildIntegratedAuthoritySet(daoTu);
		string desiredSources = XjDaoLineageStateRegistry.BuildIntegratedAuthoritySources(daoTu);
		if (string.Equals(NormalizeAuthoritySet(holderState.SeizedQuanBing), NormalizeAuthoritySet(desired), StringComparison.Ordinal)
			&& string.Equals(holderState.SeizedQuanBingSources ?? string.Empty, desiredSources, StringComparison.Ordinal))
		{
			return;
		}

		RecordCopy(
			holderState,
			holderState.LocalQuanBing,
			desired,
			holderState.ForeignQuanBing,
			holderState.WithdrawnToDongTian,
			holderState.GuoWeiZhongAi,
			holderState.PendingExternalZhengWeiDaoTu,
			holderState.LockUntilYear,
			holderState.IntegrationRetreatActive,
			holderState.IntegrationRetreatEndYear,
			string.IsNullOrWhiteSpace(summary) ? "果位融权承继同步" : summary,
			desiredSources);
		if (XjActorRegistry.ResolveKnownOrWorld(holderId, out Actor holder)
			&& XjSafeCore.IsAliveActor(holder))
		{
			XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(holder);
		}
	}

	private static bool TryResolveSeizedAuthoritySource(string rawSources, string authority, out string sourceDaoTu)
	{
		sourceDaoTu = string.Empty;
		if (string.IsNullOrWhiteSpace(rawSources) || string.IsNullOrWhiteSpace(authority)) return false;
		string[] entries = rawSources.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < entries.Length; i++)
		{
			string entry = entries[i]?.Trim();
			if (string.IsNullOrWhiteSpace(entry)) continue;
			int colon = entry.IndexOf(':');
			if (colon <= 0) continue;
			string entryAuthority = entry.Substring(0, colon).Trim();
			if (!string.Equals(entryAuthority, authority.Trim(), StringComparison.Ordinal)) continue;
			string source = entry.Substring(colon + 1).Trim();
			int slash = source.IndexOf('/');
			if (slash >= 0) source = source.Substring(0, slash).Trim();
			if (source.Length == 0) return false;
			sourceDaoTu = source;
			return true;
		}
		return false;
	}

	private static bool StateContainsAuthority(in XjGuoWeiQuanBingState state, string authority)
	{
		if (string.IsNullOrWhiteSpace(authority)) return false;
		return ContainsAuthority(state.LocalQuanBing, authority)
			|| ContainsAuthority(state.SeizedQuanBing, authority)
			|| ContainsAuthority(state.ForeignQuanBing, authority)
			|| ContainsAuthority(state.WithdrawnToDongTian, authority);
	}

	private static bool ContainsAuthority(string source, string authority)
	{
		string normalized = authority.Trim();
		string[] parts = (source ?? string.Empty).Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			if (string.Equals(parts[i].Trim(), normalized, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static void TryAddDerivedAuthority(List<string> target, string authority, int maximum)
	{
		if (target == null || target.Count >= maximum || string.IsNullOrWhiteSpace(authority)) return;
		string normalized = authority.Trim();
		if (!target.Contains(normalized)) target.Add(normalized);
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

	private static void RefreshLocalAuthoritiesForStage(Actor actor, in XjGuoWeiQuanBingState state, int currentYear)
	{
		int desiredCount = ResolveLocalAuthorityCount(actor, state.GuoWei);
		List<string> local = PickLocalAuthorities(actor, state.DaoTu, state.GuoWei, desiredCount);
		string primaryDesired = string.Join(",", local);
		string desired = BuildEffectiveLocalAuthorityText(actor, primaryDesired);
		string inherited = ResolveIntegratedAuthoritySetForPosition(actor, state.DaoTu);
		string inheritedSources = ResolveIntegratedAuthoritySourcesForPosition(actor, state.DaoTu);
		bool localSame = string.Equals(NormalizeAuthoritySet(state.LocalQuanBing), NormalizeAuthoritySet(desired), StringComparison.Ordinal);
		bool inheritedSame = string.Equals(NormalizeAuthoritySet(state.SeizedQuanBing), NormalizeAuthoritySet(inherited), StringComparison.Ordinal)
			&& string.Equals(state.SeizedQuanBingSources ?? string.Empty, inheritedSources, StringComparison.Ordinal);
		if (localSame && inheritedSame)
		{
			return;
		}

		RecordCopy(state, desired, inherited, state.ForeignQuanBing, state.WithdrawnToDongTian,
			state.GuoWeiZhongAi, state.PendingExternalZhengWeiDaoTu, state.LockUntilYear, state.IntegrationRetreatActive,
			state.IntegrationRetreatEndYear, localSame ? "果位融权承继同步" : "权柄随金丹道行显化", inheritedSources);
		if (actor?.data != null)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanAuthorityScope, out string existingScope);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanAuthorityScope, MergeAuthorityScope(existingScope, primaryDesired));
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string sourceDaoTu);
			XjDaoLineageStateRegistry.OnPromotion(
				state.ActorId, state.ActorName, sourceDaoTu, state.DaoTu,
				XjGuoWeiRegistry.ResolveTypeFromName(state.GuoWei), primaryDesired.Replace(',', '|'),
				Math.Max(1, currentYear), affectVitality: false);
		}
	}

	private static string BuildEffectiveLocalAuthorityText(Actor actor, string primaryAuthorityText)
	{
		string effective = XjDaoTaiDualPositionSystem.MergeEffectiveAuthorityScope(actor, primaryAuthorityText);
		return string.IsNullOrWhiteSpace(effective) ? string.Empty : effective.Replace('|', ',');
	}

	private static string MergeAuthorityScope(string current, string actual)
	{
		List<string> values = new List<string>();
		string combined = (current ?? string.Empty) + "|" + (actual ?? string.Empty);
		string[] parts = combined.Split(new[] { '|', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			string value = parts[i]?.Trim();
			if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value)) values.Add(value);
		}
		return string.Join("|", values);
	}

	private static string NormalizeAuthoritySet(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return string.Empty;
		string[] parts = value.Split(new[] { ',', '，', '|', '、' }, StringSplitOptions.RemoveEmptyEntries);
		Array.Sort(parts, StringComparer.Ordinal);
		return string.Join("|", parts);
	}

	private static int ResolveLocalAuthorityCount(Actor actor, string guoWei)
	{
		string value = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		int progress = 0;
		if (actor?.data != null)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanDaoXing, out int daoXing);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhenJunXiuChi, out int xiuChi);
			progress = Math.Max(daoXing, xiuChi);
		}
		if (value.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			if (XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return XjGuoWeiQuanBingRules.QuanBingCountPerDaoTu;

			// 正位果位按金丹/真君羽士小境界显化权柄：初三、中四、后五、巅峰六。
			if (progress >= 6000) return 6;
			if (progress >= 3000) return 5;
			if (progress >= 1000) return 4;
			return 3;
		}
		if (value.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)) return progress >= 6000 ? 2 : 1;
		if (value.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) return progress >= 4000 ? 3 : 2;
		return 0;
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


	private static int PositiveRoll(long seed, int year, string salt)
	{
		return XjDeterministicHash.PositiveIndex(seed + year, salt, 100);
	}
}
