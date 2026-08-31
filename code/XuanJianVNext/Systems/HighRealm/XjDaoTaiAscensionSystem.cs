using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjDaoTaiAscensionSystem
{
	private const int BaseSuccessChancePercent = 10;
	private const int MaxSuccessChancePercent = 30;

	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || !XjSafeCore.IsAliveActor(actor) || currentYear <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		bool isDaoTai = XjDaoTaiSpellScale.IsDaoTaiActor(actor);
		if (!isDaoTai && XjFruitPositionWorldState.TryGetDaoTaiBinding(actorId, out _))
		{
			XjFruitPositionWorldState.ReleaseDaoTaiBinding(
				actorId, actor.getName(), (int)XjDaoHuiPolicy.Read(actor), currentYear, "RealmChanged");
		}
		if (isDaoTai)
		{
			XjDaoTaiPresenceArchive.ObserveLiveDaoTai(actor, currentYear);
			if (XjDaoTaiPresenceArchive.IsBodyArchived(actorId)) return;
			XjDaoTaiGongFaService.EnsureGradeSeven(actor, currentYear);
			XjGuoWeiQuanBingLifecycle.RefreshProgressAuthorities(actor, currentYear);
			XjDaoTaiDualPositionSystem.TickActor(actor, currentYear);
			XjDaoTaiEnlightenmentSystem.TickDaoTai(actor, currentYear);
			return;
		}
		XjDaoTaiMeritSystem.ReconcileHighRealmStatus(actor, currentYear);
		if (!XjDaoTaiMeritSystem.CanAttemptDaoTai(actor, out _)) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjDaoTaiLastAttemptYear, out int lastAttempt)
			&& lastAttempt >= currentYear)
		{
			return;
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjDaoTaiLastAttemptYear, currentYear);

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		string guoWei = XjDaoTaiMeritSystem.ResolveGuoWei(actor);
		XjDaoTaiMeritSystem.TryResolveDaoTaiPositionKind(guoWei, out string positionKind);
		if (XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor))
		{
			Promote(actor, daoTu, guoWei, positionKind, currentYear);
			return;
		}

		// RC11.3：仍不恢复任何“天霞/在册道胎狙击”前置灾劫。
		// 恢复RC11.1的权柄成功率加成口径，仅将基础改为10%、总上限改为30%。
		int authorityBonus = XjDaoTaiMeritSystem.CalculateAuthorityBreakthroughBonusPercent(actor);
		int successChancePercent = Math.Min(MaxSuccessChancePercent, BaseSuccessChancePercent + authorityBonus);
		if (Roll(actorId, currentYear, "success") >= successChancePercent)
		{
			RecordAttemptFailure(actor, daoTu, guoWei, currentYear);
			return;
		}

		Promote(actor, daoTu, guoWei, positionKind, currentYear);
	}

	private static void RecordAttemptFailure(Actor actor, string daoTu, string guoWei, int currentYear)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		string actorName = SafeActorName(actor);
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			actorName + "成胎未竟",
			XjChronology.FormatYear(currentYear) + "，" + actorName + "参合"
				+ (string.IsNullOrWhiteSpace(guoWei) ? "所持果义" : XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei))
				+ "尝试成胎，终未能定住胎机。",
			4,
			actorId: actorId,
			actorName: actorName,
			year: currentYear,
			iconIdOverride: XjEventIconCatalog.JinDanUpgrade,
			eventType: "DaoTaiAscensionFailed");
	}

	private static void Promote(Actor actor, string daoTu, string guoWei, string positionKind, int currentYear)
	{
		string currentRealm = XjRealmHelper.NormalizeId(
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId) ? realmId : string.Empty);
		string targetRealm = string.Equals(currentRealm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			? XjRealmIds.FuQiDaoTai
			: XjRealmIds.DaoTai;
		if (!XjCultivationStateTransitions.TrySetRealm(actor, targetRealm, true))
		{
			return;
		}
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(actor, targetRealm, currentYear, syncVisibleTraits: false);
		XjRealmTitleApplyService.ApplyOnDaoTaiAscension(actor, daoTu);
		XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
		// 成胎即承完整金丹级道途器物：武器、盔甲、头盔、靴、戒、佩饰共六件。
		// 不走炼器概率/资源，也不作为年度自动补装。
		XjEquipmentForgeConsumer.EnsureDaoTaiJinDanEquipmentSet(actor, daoTu, currentYear);
		XjDaoTaiGongFaService.EnsureGradeSeven(actor, currentYear);
		XjGuoWeiQuanBingLifecycle.RefreshProgressAuthorities(actor, currentYear);
		XjAutoCollectSystem.TryCollectRealm(actor, targetRealm, "DaoTaiAscension");
		XjDaoTaiPresenceArchive.RecordAscension(actor, daoTu, targetRealm, guoWei, positionKind, currentYear);
		XjDaoTaiDualPositionSystem.TickActor(actor, currentYear);
		BroadcastDaoTaiAscension(actor, daoTu, guoWei, currentYear);
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	internal static void BroadcastDaoTaiAscension(Actor actor, string daoTu, string guoWei, int currentYear)
	{
		if (actor?.data == null) return;
		string text = XjAnnouncementText.BuildDaoTaiPromotion(actor, daoTu, guoWei, currentYear);
		XjBroadcastSystem.BroadcastSLevelActorEvent(actor, text, text, "#A8D8FF", 10f, XjEventIconCatalog.JinDanUpgrade);
		return;
	}

	private static int Roll(long actorId, int year, string salt)
	{
		return XjDeterministicHash.PositiveIndex(actorId + year, "daotai_ascension_" + salt, 100);
	}

	private static string SafeActorName(Actor actor)
	{
		return XjDisplayNameSanitizer.Clean(actor?.getName() ?? string.Empty, "此修");
	}
}
