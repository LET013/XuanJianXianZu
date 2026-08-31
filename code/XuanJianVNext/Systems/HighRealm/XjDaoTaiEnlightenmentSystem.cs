using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 道胎点化只作用于自身已确认家族中的有资质成员。一次点化只产生一种结果：
/// 境界提升一阶（最高到紫府/真人同阶），或资质提升一品（最高到天公垂目）。
/// 候选来自家族成员索引，不扫描全世界，也不在 UI 读取时运行；每名道胎成功点化后冷却二百年。
/// </summary>
internal static class XjDaoTaiEnlightenmentSystem
{
	internal const int MinimumIntervalYears = 200;
	internal const int MaximumEnlightenedAptitude = 5;
	private static readonly int ZiFuOrder = XjRealmHelper.GetOrder(XjRealmIds.ZiFu);

	private readonly struct Candidate
	{
		internal readonly Actor Actor;
		internal readonly long ActorId;
		internal readonly bool CanRaiseRealm;
		internal readonly bool CanRaiseAptitude;

		internal Candidate(Actor actor, long actorId, bool canRaiseRealm, bool canRaiseAptitude)
		{
			Actor = actor;
			ActorId = actorId;
			CanRaiseRealm = canRaiseRealm;
			CanRaiseAptitude = canRaiseAptitude;
		}
	}

	internal static void TickDaoTai(Actor daoTai, int currentYear)
	{
		if (daoTai?.data == null || currentYear <= 0 || !XjDaoTaiSpellScale.IsDaoTaiActor(daoTai)) return;
		long daoTaiId = ((BaseSystemData)daoTai.data).id;
		if (daoTaiId <= 0L) return;
		if (XjActorAccessor.TryGetInt(daoTai, XjActorDataKeys.XjDaoTaiLastEnlightenmentYear, out int lastYear)
			&& currentYear - lastYear < MinimumIntervalYears)
		{
			return;
		}

		// 果位之主已成道胎后，可按原著口径把本道余位藏入自家紫府，
		// 炼成“躲在道胎之下”的神丹。神丹不取得可见果余闰位，根锚点仍是这位道胎。
		if (TryBestowShenDanFromFruitYuWei(daoTai, currentYear))
		{
			XjActorAccessor.SetInt(daoTai, XjActorDataKeys.XjDaoTaiLastEnlightenmentYear, currentYear);
			return;
		}
		if (!TryPickTarget(daoTaiId, currentYear, out Candidate candidate)) return;

		XjActorAccessor.SetInt(daoTai, XjActorDataKeys.XjDaoTaiLastEnlightenmentYear, currentYear);
		ApplyEnlightenment(daoTai, candidate, currentYear);
	}


	private static bool TryBestowShenDanFromFruitYuWei(Actor daoTai, int currentYear)
	{
		if (daoTai?.data == null) return false;
		long daoTaiId = ((BaseSystemData)daoTai.data).id;
		XjActorAccessor.TryGetString(daoTai, XjActorDataKeys.DaoTu, out string daoTu);
		if (daoTaiId <= 0L
			|| string.IsNullOrWhiteSpace(daoTu)
			|| !XjDaoLineageStateRegistry.TryResolveActiveFruitHolder(daoTu, out long fruitHolderId, out _)
			|| fruitHolderId != daoTaiId
			|| !XjFamilyReadModel.Shared.TryGetConfirmedIdentity(daoTaiId, out XjFamilyIdentity identity))
		{
			return false;
		}

		// 原文是“果位之主的道胎仙人，把余位主动塞入紫府修士体内”。
		// 因此不要求道胎自己先兼持余位，只要求本道在当世确实能生余位。
		XjFruitPositionCapacity capacity = XjFruitPositionWorldState.GetCapacity(daoTu);
		if (capacity.Residual <= 0) return false;
		int yuSlot = XjDeterministicHash.PositiveIndex(
			daoTaiId + currentYear, "daotai_bestow_shendan_yuwei_v2", capacity.Residual) + 1;
		string yuWeiId = XjGuoWeiCalculator.BuildGuoWeiSlotName(daoTu, XjGuoWeiCalculator.YuWei, yuSlot);
		if (!XjShenDanRegistry.TryResolveDaoTaiYuAnchor(daoTaiId, yuWeiId, out XjGuoWeiRegistryEntry anchor))
		{
			return false;
		}

		List<Actor> candidates = new List<Actor>();
		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(identity.FamilyStableIdValue))
		{
			if (!XjSafeCore.IsAliveActor(member) || member.data == null) continue;
			long memberId = ((BaseSystemData)member.data).id;
			if (memberId <= 0L || memberId == daoTaiId) continue;
			string realmId = XjRealmHelper.NormalizeId(
				XjRealmHelper.GetUnifiedId(member, XjRealmHelper.GetTraitSnapshotForRouter));
			if (!string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) continue;
			if (!XjCultivationPathRules.TryGetPath(member, out string path)
				|| !string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal)) continue;
			if (!XjActorAccessor.TryGetInt(member, XjActorDataKeys.XjZz, out int aptitude) || aptitude <= 0) continue;
			if (XjShenDanAccessor.BuildState(member).Found) continue;
			if (!XjShenDanRegistry.CanAttachToAnchor(anchor, memberId, out _, out _)) continue;
			candidates.Add(member);
		}
		if (candidates.Count == 0) return false;

		candidates.Sort((left, right) =>
		{
			long leftId = left?.data == null ? long.MaxValue : ((BaseSystemData)left.data).id;
			long rightId = right?.data == null ? long.MaxValue : ((BaseSystemData)right.data).id;
			return leftId.CompareTo(rightId);
		});
		Actor target = candidates[XjDeterministicHash.PositiveIndex(
			daoTaiId + currentYear, "daotai_yuwei_shendan_target_v2", candidates.Count)];
		if (target?.data == null) return false;
		long targetId = ((BaseSystemData)target.data).id;
		if (targetId <= 0L) return false;

		XjActorAccessor.TryGetString(target, XjActorDataKeys.DaoTu, out string originalDaoTu);
		if (!XjCultivationStateTransitions.TrySetDaoTu(target, daoTu, false)) return false;
		if (!XjCultivationStateTransitions.TrySetRealm(target, XjRealmIds.ShenDan, false))
		{
			if (!string.IsNullOrWhiteSpace(originalDaoTu))
				XjCultivationStateTransitions.TrySetDaoTu(target, originalDaoTu, false);
			return false;
		}

		string daoTaiName = XjDisplayNameSanitizer.Clean(daoTai.getName(), "无名道胎");
		string targetName = XjDisplayNameSanitizer.Clean(target.getName(), "无名紫府");
		XjShenDanAccessor.WriteSuccess(target, yuWeiId, daoTaiId, daoTaiName, currentYear);
		if (!XjShenDanAccessor.BuildState(target).Found
			|| !XjShenDanRegistry.TryRegister(targetId, daoTaiId))
		{
			XjShenDanAccessor.ClearSuccess(target);
			XjCultivationStateTransitions.TrySetRealm(target, XjRealmIds.ZiFu, false);
			if (!string.IsNullOrWhiteSpace(originalDaoTu))
				XjCultivationStateTransitions.TrySetDaoTu(target, originalDaoTu, false);
			return false;
		}

		// 原文明确：“余位往我心肺里藏了七十一日，为我炼就位格，从此多了七百一十年寿”。
		// 七十一日只作为一次高境事件的叙事期，不引入逐日扫描；+710寿元落为稳定角色数据。
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjDaoTaiShenDanLifespanBonus, 710);
		try { target.setStatsDirty(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("XjDaoTaiEnlightenmentSystem.SetStatsDirty", ex); }
		XjYinSiTraitLifecycle.EnsureRemovedFromJinDan(target);
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(target, XjRealmIds.ShenDan, currentYear);
		// 道胎以余位直接炼成神丹会绕过普通求金前置；位格落成后补足五门本道上位神通。
		XjShenDanXianJiCompletionService.EnsureFiveShenTong(
			target, daoTu, currentYear, "道胎炼神丹补全", out _);
		XjRealmTitleApplyService.ApplyOnPromotion(target, XjRealmIds.ShenDan, daoTu);
		XjAutoCollectSystem.TryCollectRealm(target, XjRealmIds.ShenDan, "DaoTaiYuWeiShenDan");

		string body = XjChronology.FormatYear(currentYear) + "，道胎" + daoTaiName + "取本道一道余位，藏入族中紫府"
			+ targetName + "心肺七十一日，为其炼成神丹位格，增寿七百一十年，自此托身于该道胎之下。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Cultivation,
			"道胎炼神丹",
			body,
			5,
			true,
			actorId: targetId,
			actorName: targetName,
			year: currentYear,
			iconIdOverride: XjEventIconCatalog.JinDanUpgrade,
			eventType: "DaoTaiShenDanBestowal");
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			target,
			body,
			body,
			XjEventIconCatalog.JinDanUpgrade);
		XjFamilyDomainEventRouter.Publish(
			XjFamilyDomainEvent.ShenDanSucceeded(target, yuWeiId, daoTu, daoTaiName));
		return true;
	}

	private static bool TryPickTarget(long daoTaiId, int currentYear, out Candidate selected)
	{
		selected = default;
		if (!XjFamilyReadModel.Shared.TryGetConfirmedIdentity(daoTaiId, out XjFamilyIdentity identity)) return false;

		List<Candidate> candidates = new List<Candidate>();
		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(identity.FamilyStableIdValue))
		{
			if (!XjSafeCore.IsAliveActor(member) || member.data == null) continue;
			long memberId = ((BaseSystemData)member.data).id;
			if (memberId <= 0L || memberId == daoTaiId) continue;
			if (!XjActorAccessor.TryGetInt(member, XjActorDataKeys.XjZz, out int aptitude)
				|| aptitude <= 0 || aptitude > 6) continue;

			string realmId = XjRealmHelper.GetUnifiedId(member, XjRealmHelper.GetTraitSnapshotForRouter);
			int realmOrder = XjRealmHelper.GetOrder(realmId);
			bool canRaiseRealm = realmOrder < ZiFuOrder;
			bool canRaiseAptitude = aptitude < MaximumEnlightenedAptitude;
			if (!canRaiseRealm && !canRaiseAptitude) continue;
			candidates.Add(new Candidate(member, memberId, canRaiseRealm, canRaiseAptitude));
		}
		if (candidates.Count == 0) return false;
		candidates.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
		selected = candidates[XjDeterministicHash.PositiveIndex(
			daoTaiId + currentYear, "daotai_family_enlightenment_target_v2", candidates.Count)];
		return selected.Actor?.data != null;
	}

	private static bool ApplyEnlightenment(Actor daoTai, Candidate candidate, int currentYear)
	{
		Actor target = candidate.Actor;
		if (daoTai?.data == null || target?.data == null) return false;
		bool raiseRealm = candidate.CanRaiseRealm && (!candidate.CanRaiseAptitude
			|| XjDeterministicHash.PositiveIndex(candidate.ActorId + currentYear, "daotai_enlightenment_choice_v2", 2) == 0);

		string result;
		if (raiseRealm)
		{
			if (!TryRaiseRealm(daoTai, target, out string previousRealm, out string nextRealm))
			{
				if (!candidate.CanRaiseAptitude || !TryRaiseAptitude(target, out int oldAptitude, out int newAptitude)) return false;
				result = "使其资质由【" + XjDisplayNameSanitizer.AptitudeName(oldAptitude)
					+ "】提升至【" + XjDisplayNameSanitizer.AptitudeName(newAptitude) + "】。";
			}
			else
			{
				result = "使其境界由" + previousRealm + "提升至" + nextRealm + "。";
			}
		}
		else
		{
			if (!TryRaiseAptitude(target, out int oldAptitude, out int newAptitude))
			{
				if (!candidate.CanRaiseRealm || !TryRaiseRealm(daoTai, target, out string previousRealm, out string nextRealm)) return false;
				result = "使其境界由" + previousRealm + "提升至" + nextRealm + "。";
			}
			else
			{
				result = "使其资质由【" + XjDisplayNameSanitizer.AptitudeName(oldAptitude)
					+ "】提升至【" + XjDisplayNameSanitizer.AptitudeName(newAptitude) + "】。";
			}
		}

		string daoTaiName = XjDisplayNameSanitizer.Clean(daoTai.getName(), "无名道胎");
		string targetName = XjDisplayNameSanitizer.Clean(target.getName(), "无名修士");
		string body = XjChronology.FormatYear(currentYear) + "，" + daoTaiName + "择族中有资质者" + targetName
			+ "亲自点化，" + result;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Cultivation,
			"道胎点化",
			body,
			3,
			true,
			actorId: candidate.ActorId,
			actorName: targetName,
			year: currentYear,
			iconIdOverride: XjEventIconCatalog.JinDanUpgrade,
			eventType: "DaoTaiFamilyEnlightenment");
		return true;
	}

	private static bool TryRaiseAptitude(Actor target, out int previous, out int next)
	{
		previous = 0;
		next = 0;
		if (target?.data == null
			|| !XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjZz, out previous)
			|| previous <= 0 || previous >= MaximumEnlightenedAptitude) return false;
		next = Math.Min(MaximumEnlightenedAptitude, previous + 1);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjZz, next);
		XjActorAccessor.TryGetFloat(target, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.SetFloat(target, XjActorDataKeys.HuiGuang, Math.Max(huiGuang, 46f + next * 7f));
		return true;
	}

	private static bool TryRaiseRealm(Actor daoTai, Actor target, out string previousDisplay, out string nextDisplay)
	{
		previousDisplay = "凡俗";
		nextDisplay = string.Empty;
		if (target?.data == null) return false;

		string currentRealm = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(target, XjRealmHelper.GetTraitSnapshotForRouter));
		int currentOrder = XjRealmHelper.GetOrder(currentRealm);
		if (currentOrder >= ZiFuOrder) return false;
		if (currentRealm.Length > 0) previousDisplay = XjRealmHelper.GetDisplayName(currentRealm);

		XjActorAccessor.TryGetString(daoTai, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = string.IsNullOrWhiteSpace(daoTu) ? "清炁" : daoTu.Trim();
		bool fuQi = XjCultivationPathRules.IsFuQiYangXing(target);
		if (!XjCultivationPathRules.TryGetPath(target, out _))
		{
			if (!XjCultivationPathTransitions.TrySetInitialPath(
				target, XjCultivationPathIds.ZiFuJinDan, daoTu, string.Empty, syncVisibleTraits: false)) return false;
			fuQi = false;
		}

		string targetRealm;
		if (fuQi)
		{
			targetRealm = currentOrder < XjRealmHelper.GetOrder(XjRealmIds.HuangGuan)
				? XjRealmIds.HuangGuan
				: XjRealmIds.FuQiZhenRen;
			if (!XjFuQiStateTransitions.TrySetRealm(target, targetRealm, syncVisibleTraits: true, clearManualRemoved: true)) return false;
		}
		else
		{
			int taiXiOrder = XjRealmHelper.GetOrder(XjRealmIds.TaiXi);
			int lianQiOrder = XjRealmHelper.GetOrder(XjRealmIds.LianQi);
			targetRealm = currentOrder < taiXiOrder
				? XjRealmIds.TaiXi
				: currentOrder == taiXiOrder
					? XjRealmIds.LianQi
					: currentOrder == lianQiOrder
						? XjRealmIds.ZhuJi
						: XjRealmIds.ZiFu;
			if (!XjCultivationStateTransitions.TrySetRealm(target, targetRealm, syncVisibleTraits: true)) return false;
			XjGongFaProgression.EnsureRealmMinimumGrade(target, targetRealm, daoTu);
		}

		if (XjRealmRules.TryGet(targetRealm, out XjRealmRule rule))
		{
			XjActorAccessor.TryGetFloat(target, XjActorDataKeys.ZhenYuan, out float currentZhenYuan);
			if (currentZhenYuan < rule.RequiredZhenYuan)
				XjActorAccessor.SetFloat(target, XjActorDataKeys.ZhenYuan, (float)Math.Floor(rule.RequiredZhenYuan));
		}
		XjActorAccessor.TryGetFloat(target, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.SetFloat(target, XjActorDataKeys.HuiGuang, Math.Max(huiGuang, 60f + XjRealmHelper.GetOrder(targetRealm) * 5f));
		nextDisplay = XjRealmHelper.GetDisplayName(targetRealm);
		return true;
	}
}
