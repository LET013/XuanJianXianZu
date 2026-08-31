using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 初始修炼方式只允许写入一次。道途先于修炼方式确定：感气成功以服气养性
/// 修同一道途，感气失败则以紫府金丹法修同一道途。紫金不得反向进入服气。
/// </summary>
internal static class XjCultivationPathTransitions
{
	internal static bool TrySetInitialPath(
		Actor actor,
		string path,
		string fuQiLineageId,
		bool syncVisibleTraits)
	{
		return TrySetInitialPath(actor, path, string.Empty, fuQiLineageId, syncVisibleTraits);
	}

	internal static bool TrySetInitialPath(
		Actor actor,
		string path,
		string daoTu,
		string fuQiLineageId,
		bool syncVisibleTraits)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsKnownPath(path)) return false;
		// 宋玄是支持者彩蛋中的固定人物事实：只走紫府金丹主链，终局道途固定玄雷。
		// 这里卡住所有普通入道旁路，避免出生后先被服气/释修/随机道途写脏，再到五岁补救。
		if (XjSongXuanEasterEggSystem.IsSongXuan(actor) || XjZhangYanEasterEggSystem.IsZhangYan(actor))
		{
			if (!string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal)) return false;
			daoTu = XjSongXuanEasterEggSystem.IsSongXuan(actor) ? XjSongXuanEasterEggSystem.DaoTu : XjZhangYanEasterEggSystem.DaoTu;
			fuQiLineageId = string.Empty;
		}
		// 果位钟爱转世属于既成正位契合，只能回到紫府金丹体系。
		// 其他入口若试图把它改为服气或释修，直接拒绝而不是先写错再年度修复。
		if (XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out _)
			&& !string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal))
		{
			return false;
		}
		// 释修必须经过 XjShiState.TryEnter 完成仙释互斥、旧修法归档与独立状态初始化。
		if (string.Equals(path, XjCultivationPathIds.Shi, StringComparison.Ordinal)) return false;
		if (XjCultivationPathRules.TryGetPath(actor, out string existingPath))
		{
			return string.Equals(existingPath, path, StringComparison.Ordinal);
		}

		// 服气基础入口允许四至六档：五、六档按既有规则，四档必须在自然感气时
		// 由高命数+高道慧破格；所有旁路仍统一经过此处，避免任意低品资质写入。
		if (string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
			if (!XjFuQiAptitudeRules.CanSenseDaoQi(aptitude)) return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string existingRealmId);
		string existingRealm = XjRealmHelper.NormalizeId(existingRealmId);
		if ((string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal)
				&& XjCultivationPathRules.IsZiFuRealm(existingRealm))
			|| (string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal)
				&& XjCultivationPathRules.IsFuQiRealm(existingRealm)))
		{
			return false;
		}

		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		string rootId = string.Empty;
		if (string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal))
		{
			if (normalizedDaoTu.Length > 0)
			{
				if (!XjDaoTuCatalog.TryResolve(normalizedDaoTu, out XjDaoTuDefinition definition)
					|| !definition.SupportsFuQi
					|| !XjFuQiCoreCatalog.TryGetByRootId(definition.RootId, out XjFuQiCoreDefinition core)
					|| !core.GameplayImplemented)
				{
					return false;
				}
				rootId = definition.RootId;
				long actorId = ((BaseSystemData)actor.data).id;
				if (!XjDaoTuVisibleTraitCatalog.TryResolveCanonicalDisplayName(
					normalizedDaoTu,
					actorId,
					out normalizedDaoTu))
				{
					return false;
				}
			}
			else if (string.Equals((fuQiLineageId ?? string.Empty).Trim(), XjFuQiLineageIds.Sword, StringComparison.Ordinal))
			{
				rootId = XjDaoTuRootIds.LongGeng;
			}
			else
			{
				return false;
			}
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.CultivationPath, path);
		if (string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal))
		{
			bool sword = string.Equals(rootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal);
			XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiLineageId, sword ? XjFuQiLineageIds.Sword : string.Empty);
			// 首位开道者命名前仍保留无名状态；长庚一经显世，后续养青冥修士
			// 必须从入道当年就写入真实道途，不能继续制造“未知道途”半状态。
			string storedDaoTu = sword
				? (XjFuQiSwordWorldState.IsEstablished ? XjFuQiSwordWorldState.EstablishedDaoName : string.Empty)
				: normalizedDaoTu;
			XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, storedDaoTu);
			// 写入服气身份即代表感气已经成功；不能再留下pending或失败结果，
			// 也不创建任何按年推进的感气项目。
			XjFuQiSensingSystem.MarkPathEntrySuccess(actor, rootId);
			ResetFuQiProgress(actor);
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int fuQiAptitude) && fuQiAptitude == 4)
			{
				// 四档服气资格只在首次入道时固化一次；后续命数/道慧变化不会年度重抽。
				XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank4EligibilityChecked, 1);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank4ZhenJunEligible,
					XjTalentOpportunityRules.IsExceptionalRank4FuQiTalent(actor) ? 1 : 0);
			}
			XjFuQiCoreRouter.InitializeActorCore(actor, rootId);
			XjGongFaAccessor.Clear(actor);
			if (XjFuQiCoreRouter.TryResolveActorCore(actor, out XjFuQiCoreDefinition fuQiCore))
			{
				XjFuQiMethodRules.EnsureRealGongFaEntity(
					actor,
					in fuQiCore,
					XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
			}
			XjXianJiAccessor.RestoreSnapshot(actor, string.Empty, 0);
			XjQiuJinFaAccessor.Clear(actor);
			XjJinDanAccessor.ClearSuccess(actor);
			XjShenDanAccessor.ClearSuccess(actor);
		}
		else
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiLineageId, string.Empty);
			XjFuQiCoreRouter.ClearActorCore(actor);
			ResetFuQiProgress(actor);
		}

		if (syncVisibleTraits) XjVisibleTraitSync.SyncCultivationTraits(actor);
		XjCultivatorCache.CheckAndUpdate(actor);
		XjFuQiCandidateIndex.Observe(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		XjCombatHotPathCache.Refresh(actor);
		return true;
	}

	/// <summary>
	/// 已完成资格判断/旧状态清理的领域事务，用此入口写修法身份元数据。
	/// 它不初始化服气核心、释修法脉或境界，因此只能由转修、转世、读档迁移等
	/// 已拥有完整事务语义的调用者使用；普通入道必须走 TrySetInitialPath。
	/// </summary>
	internal static bool SetPathMetadataForAuthorityTransition(Actor actor, string path)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsKnownPath(path)) return false;
		if ((XjSongXuanEasterEggSystem.IsSongXuan(actor) || XjZhangYanEasterEggSystem.IsZhangYan(actor))
			&& !string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal)) return false;
		XjActorAccessor.SetString(actor, XjActorDataKeys.CultivationPath, path);
		return true;
	}

	/// <summary>
	/// 修复0.9.4.33及更早手动服气补录只保存根类名称的旧角色。这里不改修法、
	/// 核心或境界，只把根类还原为一个稳定的具体道途身份，供特质、果位与照录共用。
	/// </summary>
	internal static bool ReconcileFuQiDaoTuIdentity(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return false;
		}
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string stored)
			|| string.IsNullOrWhiteSpace(stored)
			|| XjDaoTuVisibleTraitCatalog.TryResolveTraitId(stored, out _))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjDaoTuVisibleTraitCatalog.TryResolveCanonicalDisplayName(stored, actorId, out string canonical)
			|| string.IsNullOrWhiteSpace(canonical)
			|| string.Equals(canonical, stored.Trim(), StringComparison.Ordinal))
		{
			return false;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, canonical);
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out string origin)
			|| string.IsNullOrWhiteSpace(origin)
			|| string.Equals(origin.Trim(), stored.Trim(), StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, canonical);
		}
		return true;
	}

	internal static bool TryEnsureZiFuJinDan(Actor actor, bool syncVisibleTraits)
	{
		if (actor?.data == null) return false;
		if (XjCultivationPathRules.TryGetPath(actor, out string existingPath))
		{
			return string.Equals(existingPath, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal);
		}
		return TrySetInitialPath(actor, XjCultivationPathIds.ZiFuJinDan, string.Empty, syncVisibleTraits);
	}

	internal static void ClearAll(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetString(actor, XjActorDataKeys.CultivationPath, string.Empty);
		XjShiState.ClearDataOnly(actor);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiLineageId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiSensedQiId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseResult, XjFuQiSensingSystem.ResultPending);
		XjFuQiCoreRouter.ClearActorCore(actor);
		ResetFuQiProgress(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecision, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecisionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchStartedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchMask, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordCurrentOrdinal, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordProjectCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordCompletedYear, 0);
		XjFuQiCandidateIndex.Observe(actor);
	}

	internal static void ResetFuQiProgress(Actor actor)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiEraLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank4ZhenJunEligible, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSwordQi, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiStudiedIntentIds, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCurrentIntentId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiIntentStudyStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiIntentStudyCompleteYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiShenMiaoId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiYangQingMingCompletedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSwordLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiBodyProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiBodyProjectCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiPerfectionProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank4EligibilityChecked, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5HighRealmRouteChecked, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5StayFuQi, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingReady, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanLastAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanFailureCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanSuccessYear, 0);
	}
}
