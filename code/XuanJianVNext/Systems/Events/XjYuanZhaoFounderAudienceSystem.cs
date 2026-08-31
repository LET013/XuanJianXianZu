using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Events;

/// <summary>
/// 水月照真洞天的“因缘觐见”唯一调度器。
///
/// 0.9.8.17 起，水月照真不再是高境修士可以凭境界自由探索的公共洞天：
/// - 紫府/真人/金丹/真君羽士只有被道尊主动牵引，才会形成一次性入洞许可；
/// - 宗门、国家、洞天属地均不能组织远征或取得通行权；
/// - 道尊永远只有事件层 PresenceId，不创建 Actor、不落地图、不参加 AI/寿尽/战斗；
/// - 所谓“应身现世”仅是水月映照、天象与权柄层投影，不创建任何可选中实体。
///
/// 事件结构借“高位人物只在关键缘法上给一句、一道、一次许可”的叙事方式：
/// 普通高境不会因为走到洞口就见到道尊，真正的觐见只发生在道统承继、源道问路、
/// 或权柄来路已经触及渊照本身时。
/// </summary>
internal static class XjYuanZhaoFounderAudienceSystem
{
	internal const string ReasonHeirAudience = "YuanZhaoHeirAudience";
	internal const string ReasonHeirZhengWeiAudience = "YuanZhaoZhengWeiAudience";
	internal const string ReasonSourceDaoInquiry = "YuanZhaoSourceDaoInquiry";
	internal const string ReasonAuthorityArbitration = "YuanZhaoAuthorityArbitration";

	private const int EvaluationIntervalYears = 20;
	private const int FounderSilenceYears = 50;
	private const int AudienceCooldownYears = 300;
	private const int InvitationDurationYears = 8;
	private const int AuthorityProjectionCooldownYears = 120;
	private const int SourceProjectionCooldownYears = 80;
	private const int AuthorityScrutinyYears = 15;
	private const int SourceDaoPeakYiXiang = 5000;

	internal static void TickYear(int currentYear)
	{
		if (currentYear <= 0
			|| !XjYuanZhaoKongZhengEvent.IsTriggered
			|| !XjYuanZhaoKongZhengEvent.IsLegacyDongTianReady)
		{
			return;
		}

		ReconcilePendingInvitation(currentYear);
		if (TryGetPendingAudienceActorId(currentYear, out _)) return;
		// 百余年才流出一次的【照真请凭函】与道尊正式传召不并发。
		// 持函者尚未作出选择时，道尊不会同时另开一条正式觐见线。
		if (XjYuanZhaoKongZhengEvent.TryGetActiveFounderCredential(out _, out int credentialUntil)
			&& credentialUntil >= currentYear) return;

		// 空证之后先有一段真正的“隐世”。这样源道旧修不会在道尊刚闭门的同一年
		// 就因为境界够高排队求见，也给新生渊照道统留下自然生长的时间。
		if (currentYear < XjYuanZhaoKongZhengEvent.TriggeredYear + FounderSilenceYears) return;

		int lastInviteYear = XjYuanZhaoKongZhengEvent.FounderLastAudienceInviteYear;
		if (lastInviteYear > 0 && currentYear < lastInviteYear + AudienceCooldownYears) return;

		int origin = Math.Max(1, XjYuanZhaoKongZhengEvent.TriggeredYear);
		if ((currentYear - origin) % EvaluationIntervalYears != 0) return;

		if (!TryPickAudienceCandidate(currentYear, out Actor actor, out string reason)) return;
		ScheduleAudience(actor, currentYear, reason);
	}

	internal static bool TryGetPendingAudienceActorId(int currentYear, out long actorId)
	{
		actorId = 0L;
		if (!XjYuanZhaoKongZhengEvent.TryGetPendingFounderAudience(out long pendingActorId, out int untilYear, out _)
			|| pendingActorId <= 0L || untilYear < currentYear)
		{
			return false;
		}
		if (!XjScheduler.ResolveActor(pendingActorId, out Actor actor)
			|| !HasActiveInvitation(actor, currentYear, out _))
		{
			return false;
		}
		actorId = pendingActorId;
		return true;
	}

	internal static bool HasActiveInvitation(Actor actor, int currentYear, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || !actor.isAlive() || currentYear <= 0) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.YuanZhaoAudienceInviteUntilYear, out int untilYear)
			|| untilYear < currentYear
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.YuanZhaoAudienceReason, out reason)
			|| string.IsNullOrWhiteSpace(reason))
		{
			reason = string.Empty;
			return false;
		}
		return IsKnownAudienceReason(reason);
	}

	internal static bool TryGetAudienceReasonForResolution(Actor actor, int currentYear, out string reason)
	{
		if (HasActiveInvitation(actor, currentYear, out reason)) return true;
		reason = string.Empty;
		if (actor?.data == null || !actor.isAlive()
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.YuanZhaoAudienceReservedYear, out int reservedYear)
			|| reservedYear <= 0
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.YuanZhaoAudienceReason, out reason)
			|| string.IsNullOrWhiteSpace(reason)
			|| !IsKnownAudienceReason(reason))
		{
			reason = string.Empty;
			return false;
		}
		// 一旦被洞天运行时正式预约，就视为已经循倒影踏入因果门槛；即使高倍速年度
		// 追赶使实际结算晚于邀请截止年，也不能把这次已经发生的觐见凭空作废。
		return true;
	}

	internal static void MarkAudienceReserved(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoAudienceReservedYear, currentYear);
		XjYuanZhaoKongZhengEvent.ClearPendingFounderAudience(actorId);
	}

	internal static void MarkAudienceResolved(Actor actor, int currentYear, bool teaching)
	{
		if (actor?.data == null || currentYear <= 0) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoAudienceResolvedYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoAudienceInviteUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoAudienceReservedYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoAudienceReason, string.Empty);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.YuanZhaoAudienceCount, out int count);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoAudienceCount, Math.Min(99, Math.Max(0, count) + 1));
		if (teaching)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.YuanZhaoFounderTeachingCount, out int teachingCount);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoFounderTeachingCount,
				Math.Min(99, Math.Max(0, teachingCount) + 1));
		}
		XjYuanZhaoKongZhengEvent.RecordFounderAudienceResolved(teaching);
	}

	/// <summary>
	/// 权柄之争真正发生“夺柄”后才可能触发水月应身。道尊不杀人、不夺回权柄、
	/// 不指定胜者；只有外道强行融合渊照本权时，会被“照见异契”而降低本次融合把握。
	/// </summary>
	internal static void OnAuthoritySeized(
		Actor killer,
		string victimDaoTu,
		string authority,
		bool externalAuthority,
		int currentYear)
	{
		if (killer?.data == null || currentYear <= 0 || string.IsNullOrWhiteSpace(authority)
			|| !XjYuanZhaoKongZhengEvent.IsTriggered || !XjYuanZhaoKongZhengEvent.IsLegacyDongTianReady)
		{
			return;
		}

		XjActorAccessor.TryGetString(killer, XjActorDataKeys.DaoTu, out string killerDaoTu);
		string source = (victimDaoTu ?? string.Empty).Trim();
		string holder = (killerDaoTu ?? string.Empty).Trim();
		bool sourceIsYuanZhao = string.Equals(source, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal);
		bool holderIsYuanZhao = string.Equals(holder, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal);
		bool sourceIsOrigin = IsOriginDaoTu(source);

		if (sourceIsYuanZhao && !holderIsYuanZhao)
		{
			if (!XjYuanZhaoKongZhengEvent.TryReserveFounderProjection(
				currentYear, AuthorityProjectionCooldownYears, authorityIntervention: true)) return;

			XjActorAccessor.SetInt(killer, XjActorDataKeys.YuanZhaoAuthorityScrutinyUntilYear,
				currentYear + AuthorityScrutinyYears);
			BroadcastAuthorityProjection(
				killer,
				currentYear,
				"外道夺取渊照权柄‘" + authority.Trim() + "’之际，天下静水同时映出一轮无光之月。月下不见人身，只见一线玄纹落入所夺权柄：其来路、旧契与异处俱被照明。此后融合仍由夺柄者自证，道尊不替天地裁胜负，却也不许异契借混沌蒙混过关。",
				"YuanZhaoFounderAuthorityIntervention");
			return;
		}

		// 渊照传人从太阴/坎水夺得相邻权柄，或同源权柄在终局中发生关键转手时，
		// 只做极低频“见证式应身”，不给胜率、伤害或果位加成。
		if ((holderIsYuanZhao && sourceIsOrigin) || (sourceIsOrigin && externalAuthority))
		{
			if (!XjYuanZhaoKongZhengEvent.TryReserveFounderProjection(
				currentYear, SourceProjectionCooldownYears, authorityIntervention: false)) return;
			BroadcastAuthorityProjection(
				killer,
				currentYear,
				"权柄‘" + authority.Trim() + "’易手时，远近水面忽然同起月纹，水上只有一道玄纹与重叠月轮，自始至终不见人形，旋即归寂。水月只照见此柄从何处来、将往何处去，并未替任何一方出手。",
				"YuanZhaoFounderSourceProjection");
		}
	}

	internal static float AdjustAuthorityIntegrationChance(
		Actor actor,
		string sourceDaoTu,
		int currentYear,
		float successChance)
	{
		float normalized = Math.Clamp(successChance, 0f, 1f);
		if (actor?.data == null || currentYear <= 0
			|| !string.Equals((sourceDaoTu ?? string.Empty).Trim(), XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal)
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.YuanZhaoAuthorityScrutinyUntilYear, out int untilYear)
			|| untilYear < currentYear)
		{
			return normalized;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string ownDaoTu);
		if (string.Equals((ownDaoTu ?? string.Empty).Trim(), XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal))
		{
			return normalized;
		}

		// “干预”只让外道难以把渊照本权伪装成自己道统的一部分，不构成必败。
		return Math.Clamp(normalized * 0.65f, 0.05f, 1f);
	}

	private static void ReconcilePendingInvitation(int currentYear)
	{
		if (!XjYuanZhaoKongZhengEvent.TryGetPendingFounderAudience(out long actorId, out int untilYear, out _)) return;
		if (untilYear >= currentYear
			&& XjScheduler.ResolveActor(actorId, out Actor actor)
			&& HasActiveInvitation(actor, currentYear, out _)) return;
		XjYuanZhaoKongZhengEvent.ClearPendingFounderAudience(actorId);
	}

	private static bool TryPickAudienceCandidate(int currentYear, out Actor picked, out string reason)
	{
		picked = null;
		reason = string.Empty;
		IReadOnlyList<long> actorIds = XjCultivatorCache.GetZhenRenOrHigherIds();
		if (actorIds == null || actorIds.Count == 0) return false;

		int bestScore = int.MinValue;
		long bestActorId = long.MaxValue;
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(actorIds[i], out Actor actor)
				|| !TryScoreCandidate(actor, currentYear, out int score, out string candidateReason)) continue;
			long actorId = ((BaseSystemData)actor.data).id;
			if (score > bestScore || (score == bestScore && actorId < bestActorId))
			{
				bestScore = score;
				bestActorId = actorId;
				picked = actor;
				reason = candidateReason;
			}
		}
		return picked != null && bestScore > int.MinValue;
	}

	private static bool TryScoreCandidate(Actor actor, int currentYear, out int score, out string reason)
	{
		score = int.MinValue;
		reason = string.Empty;
		if (actor?.data == null || !actor.isAlive()
			|| HasActiveInvitation(actor, currentYear, out _)
			|| XjYuanZhaoCredentialSystem.HasPendingCredentialInteraction(actor, currentYear)) return false;

		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		bool zhenRenEquivalent = XjCultivationPathRules.IsZhenRenEquivalentRealm(realmId);
		bool jinDanEquivalent = XjCultivationPathRules.IsJinDanEquivalentRealm(realmId);
		if (!zhenRenEquivalent && !jinDanEquivalent) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;

		if (string.Equals(daoTu, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal))
		{
			bool isZhengWei = jinDanEquivalent
				&& XjGuoWeiQuanBingRegistry.TryGet(actorId, out XuanJianVNext.Data.HighRealm.XjGuoWeiQuanBingState state)
				&& (state.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
			reason = isZhengWei ? ReasonHeirZhengWeiAudience : ReasonHeirAudience;
			if (XjDongTianRegistry.HasYuanZhaoAudienceRecord(actor, reason)) return false;
			int shenTongCount = XjCultivationPathRules.IsZiFuJinDan(actor)
				? XjXianJiAccessor.GetEffectiveShenTongCount(actor)
				: 0;
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
			score = (jinDanEquivalent ? 1_200_000 : 800_000)
				+ (isZhengWei ? 350_000 : 0)
				+ shenTongCount * 35_000
				+ Math.Min(120_000, (int)Math.Max(0f, huiGuang) * 1000)
				+ XjDeterministicHash.PositiveIndex(actorId + currentYear, "yuanzhao_audience_heir", 9999);
			return true;
		}

		if (!IsOriginDaoTu(daoTu) || !jinDanEquivalent) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang);
		// 权柄之争本身也不是“自动通行证”。只有真正入局的太阴/坎水金丹同级，
		// 才可能因所争之柄已经触及渊照源流而被点名问契；没有入局的人仍按源道疑难处理。
		bool authorityCause = XjQuanBingStruggleSystem.IsFinalConflict
			&& XjQuanBingStruggleSystem.IsCurrentParticipant(actor);
		if (!authorityCause && yiXiang < SourceDaoPeakYiXiang) return false;

		// 太阴/坎水并无“出身即通行”的特权。非道争年份还要再过一次低频缘法判定，
		// 表示只有真正碰到源流疑难、而非单纯境界够高，才会被水月照见。
		if (!authorityCause
			&& XjDeterministicHash.Roll01(actorId, currentYear, "yuanzhao_source_inquiry", daoTu) >= 0.20f)
		{
			return false;
		}

		reason = authorityCause ? ReasonAuthorityArbitration : ReasonSourceDaoInquiry;
		if (XjDongTianRegistry.HasYuanZhaoAudienceRecord(actor, reason)) return false;
		score = 350_000 + Math.Min(300_000, Math.Max(0, yiXiang) * 50)
			+ (authorityCause ? 180_000 : 0)
			+ XjDeterministicHash.PositiveIndex(actorId + currentYear, "yuanzhao_audience_source", 9999);
		return true;
	}

	private static void ScheduleAudience(Actor actor, int currentYear, string reason)
	{
		if (actor?.data == null || currentYear <= 0 || !IsKnownAudienceReason(reason)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		int untilYear = currentYear + InvitationDurationYears;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoAudienceInviteYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoAudienceInviteUntilYear, untilYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoAudienceReason, reason);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoAudienceReservedYear, 0);
		XjYuanZhaoKongZhengEvent.SetPendingFounderAudience(actorId, currentYear, untilYear, reason);

		string actorName = XjStringHelper.ActorName(actor, "无名修士");
		string reasonText = string.Equals(reason, ReasonSourceDaoInquiry, StringComparison.Ordinal)
			? "其所持太阴/坎水源流已走到难以自解之处"
			: string.Equals(reason, ReasonAuthorityArbitration, StringComparison.Ordinal)
				? "其已身在权柄终局，所争旧契恰与渊照源流相触"
				: string.Equals(reason, ReasonHeirZhengWeiAudience, StringComparison.Ordinal)
					? "其既承渊照正果，道统与创道之人第一次真正相接"
					: "其所修渊照已足以被洞中之人照见";
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor,
			"【水月传召】" + actorName + "夜观静水，水中月影忽不随天象，反照其神识而去。" + reasonText
				+ "，遂得一次入水月照真之许可。此非洞门大开，也不能携人同往；期限内唯其一人可循倒影入洞。",
			iconId: XjEventIconCatalog.DongTianOpen,
			category: XjAnnouncementCategory.DongTian);
		RecordAudienceHistory(actor, currentYear, "YuanZhaoAudienceInvited",
			actorName + "得水月传召",
			actorName + "并未叩开洞门，只在静水倒影中得了一次因缘许可。" + reasonText + "；许可不可转授，亦不能由宗门、国朝代为进入。",
			3);
	}

	private static void BroadcastAuthorityProjection(Actor actor, int currentYear, string text, string eventType)
	{
		XjBroadcastSystem.BroadcastSLevelWorldEvent("【水月应身】" + text, color: "#8FB6D8");
		RecordAudienceHistory(actor, currentYear, eventType,
			"水月应身照权",
			text + "水月一映即寂，所见只是道意借天象留痕，并非道尊真身临世。",
			5,
			worldVisible: true);
	}

	private static void RecordAudienceHistory(
		Actor actor,
		int currentYear,
		string eventType,
		string title,
		string detail,
		int importance,
		bool worldVisible = false)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		int x = -1;
		int y = -1;
		long cityId = 0L;
		try
		{
			WorldTile tile = actor == null ? null : ((BaseSimObject)actor).current_tile;
			if (tile != null) { x = tile.pos.x; y = tile.pos.y; }
			if (actor?.city?.data != null) cityId = actor.city.data.id;
		}
		catch { }
		XjHistoryVisibility visibility = XjHistoryVisibility.Personal | XjHistoryVisibility.CenturyCandidate;
		if (worldVisible) visibility |= XjHistoryVisibility.World;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.SecretRealm,
			title,
			detail,
			importance,
			actorId: actorId,
			actorName: XjStringHelper.ActorName(actor, "无名修士"),
			cityId: cityId,
			year: currentYear,
			locationX: x,
			locationY: y,
			eventType: eventType,
			visibilityFlags: (int)visibility,
			mirrorToWorldLog: false);
	}

	private static bool IsKnownAudienceReason(string reason)
	{
		return string.Equals(reason, ReasonHeirAudience, StringComparison.Ordinal)
			|| string.Equals(reason, ReasonHeirZhengWeiAudience, StringComparison.Ordinal)
			|| string.Equals(reason, ReasonSourceDaoInquiry, StringComparison.Ordinal)
			|| string.Equals(reason, ReasonAuthorityArbitration, StringComparison.Ordinal);
	}

	private static bool IsOriginDaoTu(string daoTu)
	{
		string value = (daoTu ?? string.Empty).Trim();
		return string.Equals(value, XjYuanZhaoKongZhengEvent.SourceTaiYin, StringComparison.Ordinal)
			|| string.Equals(value, XjYuanZhaoKongZhengEvent.SourceKanShui, StringComparison.Ordinal);
	}
}
