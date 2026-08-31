using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 初始身份遵循“先定道途、再判服气资格”。资格通过便立即视为感气成功并
/// 进入服气养性；未通过则以紫府金丹法修同一道途。感气不是年度项目，
/// 不会再额外等待若干年。
/// </summary>
internal static class XjCultivationPathEntrySystem
{
	internal static string EnsureInitialPath(Actor actor, int aptitude)
	{
		if (actor?.data == null) return string.Empty;
		if (XjCultivationPathRules.TryGetPath(actor, out string existingPath)) return existingPath;

		// 果位钟爱转世在首次定路时直接恢复原道途，不参与家族主流、
		// 随机道途、服气感应或并古入口抽取。
		if (XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out string favoredDaoTu))
		{
			// 兼容旧版“绝对1000年渊照”留下的钟爱转世记录：钟爱本身保留，
			// 但在新时间轴真正空证且水月照真气源落地前，不允许转世载体预先生出渊照修法。
			if (string.Equals(favoredDaoTu, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal)
				&& !XjDaoTuManifestRegistry.CanEnterLaterFoundedPath(XjDaoTuRootIds.YuanZhao))
			{
				return string.Empty;
			}
			if (!XjCultivationPathTransitions.TrySetInitialPath(
				actor, XjCultivationPathIds.ZiFuJinDan, favoredDaoTu, string.Empty, syncVisibleTraits: false)
				|| !XjCultivationStateTransitions.TrySetDaoTu(actor, favoredDaoTu, false))
			{
				return string.Empty;
			}
			XjFamilyDaoTuRules.RememberInitialDaoTuOrigin(actor, favoredDaoTu);
			XjGongFaProgression.EnsureEntryGongFa(actor, XjActorCultivationSnapshotBuilder.Build(actor));
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return XjCultivationPathIds.ZiFuJinDan;
		}

		// 本入口只负责仙修。释修缘法在五岁本地执行器中先于资质抽取独立判定，
		// 不允许“刚得到 XjZz → 同一事务又被古释抢走修炼路径”的体系耦合。
		if (aptitude < 1 || aptitude > 6) return string.Empty;

		bool forceLongGeng = false;
		try { forceLongGeng = actor.hasTrait("XjLongGengDaoTong"); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("XjCultivationPathEntrySystem.forceLongGeng", ex); }
		// 明确手动赋予的长庚道统仍保持最高优先级，不受自然传法覆盖。
		if (forceLongGeng && TryEnterNamelessSwordPath(actor, aptitude)) return XjCultivationPathIds.FuQiYangXing;

		// 落霞山门人已在五岁资质落定时择出；此处只读取其当时已写入的道途偏好，
		// 因此不会在角色已有道途后再把人转成虹霞或戊土。
		if (XjHongXiaLuoXiaEvent.TryResolveDiscipleDaoTu(actor, out string luoXiaDaoTu))
		{
			string recruitedPath = EnterResolvedDaoTu(actor, aptitude, luoXiaDaoTu);
			if (!string.IsNullOrWhiteSpace(recruitedPath))
			{
				return recruitedPath;
			}
		}

		// 家学先于无名剑道随机感应，已有明确传承者不会因为世上出现长庚而
		// 大量被改写道途。手动赋予长庚道统仍然拥有最高优先级。
		if (XjFamilyDaoTuRules.TryResolveInitialInheritedDaoTu(actor, out string inheritedDaoTu))
		{
			return EnterResolvedDaoTu(actor, aptitude, inheritedDaoTu);
		}

		if (!forceLongGeng && TryEnterNamelessSwordPath(actor, aptitude)) return XjCultivationPathIds.FuQiYangXing;

		// 没有家学时，先允许已成立后世道途的极低频自然接触；未命中者再由普通道途与九条并古道途按单条道途等权抽取。
		if (!XjFamilyDaoTuRules.TryResolveInitialDaoTu(actor, out string daoTu)) return string.Empty;
		return EnterResolvedDaoTu(actor, aptitude, daoTu);
	}

	private static string EnterResolvedDaoTu(Actor actor, int aptitude, string daoTu)
	{
		if (actor?.data == null
			|| !XjDaoTuCatalog.TryResolve(daoTu, out XjDaoTuDefinition definition)) return string.Empty;
		bool laterFoundedReady = definition.IsLaterFounded
			&& XjDaoTuManifestRegistry.CanEnterLaterFoundedPath(definition.RootId);
		if (!definition.IsCommonAncient && !definition.IsBingGu && !laterFoundedReady) return string.Empty;

		if (definition.IsBingGu)
		{
			long actorId = ((BaseSystemData)actor.data).id;
			int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
			XjDaoTuManifestRegistry.MarkDiscovered(definition.RootId, actorId, year);
		}

		bool sensed = false;
		if (definition.SupportsFuQi
			&& XjDaoTuManifestRegistry.CanManifestFuQi(definition.RootId)
			&& XjFuQiCoreCatalog.TryGetByRootId(definition.RootId, out XjFuQiCoreDefinition core)
			&& core.GameplayImplemented)
		{
			XjFuQiSensingSystem.ResolveOnce(actor, definition.RootId, aptitude, out sensed);
		}

		if (sensed
			&& XjCultivationPathTransitions.TrySetInitialPath(
				actor, XjCultivationPathIds.FuQiYangXing, daoTu, string.Empty, syncVisibleTraits: true))
		{
			XjFamilyDaoTuRules.RememberInitialDaoTuOrigin(actor, daoTu);
			return XjCultivationPathIds.FuQiYangXing;
		}

		if (!definition.SupportsZiJin
			|| !XjBingGuZiJinCompatibility.TryResolveZiJinEntryDaoTu(actor, daoTu, out string ziJinDaoTu))
		{
			return string.Empty;
		}
		if (!XjCultivationPathTransitions.TrySetInitialPath(
			actor, XjCultivationPathIds.ZiFuJinDan, ziJinDaoTu, string.Empty, syncVisibleTraits: false))
		{
			return string.Empty;
		}
		if (!XjCultivationStateTransitions.TrySetDaoTu(actor, ziJinDaoTu, false))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, string.Empty);
			XjCultivationPathTransitions.ClearAll(actor);
			return string.Empty;
		}

		// 并古道途拥有独立采气地与先天之气，角色真实道途始终保存为并古本身。
		if (definition.IsBingGu && !string.Equals(definition.RootId, XjDaoTuRootIds.QingXuan, StringComparison.Ordinal))
		{
			string finalDaoTu = XjGuoWeiFavoredDaoTuLock.ResolveRequestedDaoTu(actor, daoTu, out _);
			XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, finalDaoTu);
			XjDaoTuManifestRegistry.MarkZiJinManifested(definition.RootId, ((BaseSystemData)actor.data).id, Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor)));
		}
		else if (definition.IsLaterFounded)
		{
			// 后世空证道途只有在世界已经承认以后才能进入；首批真正修成者在这里把
			// 紫金显世状态写回档案，随后家族/宗门传承即可沿现有单权威模型扩散。
			XjDaoTuManifestRegistry.MarkZiJinManifested(
				definition.RootId, ((BaseSystemData)actor.data).id, Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor)));
		}
		XjFamilyDaoTuRules.RememberInitialDaoTuOrigin(actor, daoTu);
		XjGongFaProgression.EnsureEntryGongFa(actor, XjActorCultivationSnapshotBuilder.Build(actor));
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		return XjCultivationPathIds.ZiFuJinDan;
	}

	/// <summary>
	/// 长庚开道前仍是罕见的“无名剑道”：必须先有十六道剑意积累，再由高道慧者尝试开路。
	/// 长庚一经天地认位，规则即从“开道”切为“传道”：直系剑修后辈有强师承优势，
	/// 其余四至六档高道慧者也可自然感应，不再让已成立数百年的道统仍保持首创者级稀有度。
	/// </summary>
	private static bool TryEnterNamelessSwordPath(Actor actor, int aptitude)
	{
		bool force = false;
		try { force = actor.hasTrait("XjLongGengDaoTong"); } catch (System.Exception xjCaught103) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjCultivationPathEntrySystem.cs:103", xjCaught103); }
		if (!force)
		{
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L || aptitude < 4 || aptitude > 6) return false;
			int swordIntentCount = XjSwordIntentRegistry.Count;
			float huiGuang = XjTalentOpportunityRules.ReadHuiGuang(actor);
			bool directSwordParent = HasDirectSwordParent(actor);

			if (!XjFuQiSwordWorldState.IsEstablished)
			{
				if (swordIntentCount < XjFuQiSwordDaoSystem.StudiedIntentTarget
					|| !XjTalentOpportunityRules.CanEnterNamelessSwordPath(actor, aptitude)) return false;
				// 首位开道保持高门槛：这一段决定“天下有没有长庚”，不能因扩散优化变成常规抽取。
				int openingBasisPoints = aptitude >= 6 ? 1200 : aptitude == 5 ? 450 : 180;
				if (huiGuang >= 90f) openingBasisPoints += 400;
				openingBasisPoints += Math.Min(900, Math.Max(0, swordIntentCount - XjFuQiSwordDaoSystem.StudiedIntentTarget) * 100);
				if (directSwordParent) openingBasisPoints += 800;
				if (XjDeterministicHash.PositiveIndex(actorId, "nameless_sword_path_once_v3", 10000)
					>= Math.Min(3000, openingBasisPoints)) return false;
			}
			else
			{
				// 开道后的长庚应当像真实道统一样产生后学。直系传承最稳；非直系仍看资质与道慧。
				if (!directSwordParent && aptitude == 4 && !XjTalentOpportunityRules.IsExceptionalRank4FuQiTalent(actor)) return false;
				int inheritedBasisPoints = directSwordParent
					? (aptitude >= 6 ? 10000 : aptitude == 5 ? 9000 : 7000)
					: (aptitude >= 6 ? 3500 : aptitude == 5 ? 1800 : 700);
				if (huiGuang >= 90f) inheritedBasisPoints += 1000;
				else if (huiGuang >= 80f) inheritedBasisPoints += 500;
				inheritedBasisPoints += Math.Min(1000, Math.Max(0, swordIntentCount - XjFuQiSwordDaoSystem.StudiedIntentTarget) * 50);
				if (XjDeterministicHash.PositiveIndex(actorId, "longgeng_established_entry_once_v1", 10000)
					>= Math.Min(10000, inheritedBasisPoints)) return false;
			}
		}

		int year = XjAnnualExecutionContext.ResolveYear(actor);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiSensedQiId, XjDaoTuRootIds.LongGeng);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseYear, Math.Max(0, year));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseResult, XjFuQiSensingSystem.ResultSuccess);
		bool entered = XjCultivationPathTransitions.TrySetInitialPath(
			actor,
			XjCultivationPathIds.FuQiYangXing,
			string.Empty,
			XjFuQiLineageIds.Sword,
			syncVisibleTraits: true);
		if (entered && XjFuQiSwordWorldState.IsEstablished)
		{
			XjFuQiSwordWorldState.EnsureEstablishedDaoIdentity(actor, true);
			XjFamilyDaoTuRules.RememberInitialDaoTuOrigin(actor, XjFuQiSwordWorldState.EstablishedDaoName);
		}
		return entered;
	}

	private static bool HasDirectSwordParent(Actor actor)
	{
		if (actor?.data == null) return false;
		return IsSwordParent(actor.data.parent_id_1) || IsSwordParent(actor.data.parent_id_2);
	}

	private static bool IsSwordParent(long actorId)
	{
		return actorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor parent)
			&& XjCultivationPathRules.IsFuQiYangXing(parent)
			&& XjActorAccessor.TryGetString(parent, XjActorDataKeys.FuQiLineageId, out string lineage)
			&& string.Equals(lineage, XjFuQiLineageIds.Sword, StringComparison.Ordinal);
	}
}
