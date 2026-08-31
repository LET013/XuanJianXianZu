using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.Broadcast;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修专属公告适配层。事实记录仍由各事务入口写入仙鉴与三书，
/// 此处只把已经落地的结果投递到统一公告总线，受“释修公告”开关与年度配额控制。
/// </summary>
internal static class XjShiAnnouncementSystem
{
	private static readonly HashSet<long> SuppressedEnteredActorIds = new HashSet<long>();

	internal static void SuppressNextEntered(Actor actor)
	{
		if (actor?.data == null) return;
		try
		{
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId > 0L) SuppressedEnteredActorIds.Add(actorId);
		}
		catch (Exception ex) { XjExceptionDiagnostics.Report("XjShiAnnouncementSystem.SuppressNextEntered", ex); }
	}

	internal static void CancelSuppressedEntered(Actor actor)
	{
		if (actor?.data == null) return;
		try { SuppressedEnteredActorIds.Remove(((BaseSystemData)actor.data).id); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("XjShiAnnouncementSystem.CancelSuppressedEntered", ex); }
	}

	private static bool ConsumeSuppressedEntered(Actor actor)
	{
		if (actor?.data == null) return false;
		try { return SuppressedEnteredActorIds.Remove(((BaseSystemData)actor.data).id); }
		catch { return false; }
	}

	internal static void ClearRuntime()
	{
		SuppressedEnteredActorIds.Clear();
	}

	internal static void OnEntered(Actor actor, string tradition, string direction)
	{
		if (actor?.data == null || ConsumeSuppressedEntered(actor)) return;
		// 入释属于人物履历，事实已由业务事务写入史册，不再投递顶部天下公告。
	}

	internal static void OnRealmChanged(Actor actor, string previousRealm, string newRealm)
	{
		if (actor?.data == null || string.Equals(previousRealm, newRealm, StringComparison.Ordinal)) return;
		XjShiTitleSystem.EnsureForActor(actor);
		bool major = XjShiCatalog.GetRank(newRealm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe);
		if (!major) return;
		string display = actor.getName();
		XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot);
		bool ancient = string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
		string previous = XjShiCatalog.GetRealmDisplay(previousRealm);
		string text;
		if (string.Equals(newRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			text = ancient
				? "【金地应身】" + display + "清静求妙，不借诸位，以己身证金地、立应身；自" + previous + "证摩诃。"
				: "【摩诃不退】" + display + "宿世格位归一，真灵高举，位、形、念三不退；自" + previous
					+ "证第" + Math.Max(1, snapshot.CurrentLife) + "世摩诃。";
		}
		else if (string.Equals(newRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal))
		{
			text = ancient
				? "【应身圆成】" + display + "金地为基，应身照世，本性与法意俱显；自" + previous + "证成法相。"
				: "【法相应土】" + display + "金地稳应身，本愿合真灵，诸相由虚转实；自" + previous + "证成法相。";
		}
		else if (string.Equals(newRealm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
		{
			text = ancient
				? "【古释世尊】" + display + "金地、应身、本性俱圆，所证自成一统；世尊位成。"
				: "【今释世尊】" + display + "本愿、应身、真灵俱圆，法相高举真土；世尊名位显世。";
		}
		else
		{
			text = "【释位晋升】" + display + "法意更深，自" + previous + "进为" + XjShiCatalog.GetRealmDisplay(newRealm) + "。";
		}

		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(text, XjAnnouncementCategory.Shi,
			duration: 7f, color: "#C89B45", iconId: XjShiCatalog.GetRealmTraitId(newRealm));
		// 法师、怜愍等中低境晋升在构造公告文案前已退出，只保留人物/释门履历。
	}

	internal static void OnAncientDuhua(Actor actor, string targetName, int amount, float selfAward)
	{
		// 古释点化是人物因缘，不是天下级事件。事务入口已经把双方结果写入仙鉴与三书；
		// 此处刻意不再构造公告文本，避免十五年点化在高人口世界产生无意义字符串分配。
	}

	internal static void OnAncientReturnToVoid(string actorName)
	{
		// 古释寿尽归空保留死亡/释门履历，不再额外占用顶部公告。
	}

	internal static void OnDharmaFormStageChanged(Actor actor, string previousStage, string nextStage, bool setback)
	{
		// 法相内部阶段进退不是新的境界晋升，只记释修履历。
	}

	internal static void OnLianMinSeatPromoted(Actor actor, string previousSeat, string nextSeat, string patronName)
	{
		// 莲座迁升属于释门内部位次变化，只记履历。
	}

	internal static void OnMoHeReincarnationBegun(Actor actor, int currentLife, string anchorName)
	{
		if (actor?.data == null) return;
		int life = Math.Clamp(currentLife, 1, 9);
		int nextLife = Math.Clamp(life + 1, 1, 9);
		string text = "【摩诃转世】" + actor.getName() + "第" + life + "世修持已足，自散肉身；真灵归于"
			+ (string.IsNullOrWhiteSpace(anchorName) ? "所系释土" : anchorName)
			+ "，待第" + nextLife + "世再临。";
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(text, XjAnnouncementCategory.Shi,
			duration: 6.5f, color: "#C8AF75", iconId: XjShiTraitIds.MoHe);
	}

	internal static void OnTempleMaster(Actor actor, string jinDiName)
	{
		if (actor?.data == null) return;
		string text = "【庙主得地】" + actor.getName() + "感得北世尊应身碎片，掌握"
			+ (string.IsNullOrWhiteSpace(jinDiName) ? "一处金地" : jinDiName) + "，受尊为庙主。";
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(text, XjAnnouncementCategory.Shi,
			duration: 7f, color: "#E0B44D", iconId: XjShiTraitIds.MoHe);
	}

	internal static void OnHeavenFragment(Actor actor, string fragmentName, bool completed, string heavenName)
	{
		if (actor?.data == null) return;
		string text = completed
			? "【玄天重成】" + actor.getName() + "聚齐同源金地，重组" + heavenName + "，得见世尊应身。"
			: "【金地归位】" + actor.getName() + "牵引" + fragmentName + "归于同源应身。";
		if (completed)
			XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(text, XjAnnouncementCategory.Shi,
				duration: 8f, color: "#D28F32", iconId: XjShiTraitIds.DharmaForm);
		// 单枚金地归位只记释门履历；聚齐并重组三十二天时才公告。
	}

	internal static void OnHighRealmTrueSpiritAnnihilated(string actorName, string realmName, string attackerName)
	{
		string victimDisplay = XjStringHelper.DisplayNameWithoutRealmSuffix(actorName, "一名高位释修");
		string realm = string.IsNullOrWhiteSpace(realmName) ? "摩诃以上" : realmName.Trim();
		string attackerDisplay = string.IsNullOrWhiteSpace(attackerName)
			? string.Empty
			: XjStringHelper.DisplayNameWithoutRealmSuffix(attackerName, string.Empty);
		bool hasKiller = attackerDisplay.Length > 0;
		string title = hasKiller ? "【斩灭真灵】" : "【真灵俱灭】";
		string result = hasKiller
			? "为" + attackerDisplay + "斩却真灵"
			: "真灵俱灭";
		string text = title + realm + "·" + victimDisplay + result
			+ "，诸世因缘自此断绝，再不得归返释土。";
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(text, XjAnnouncementCategory.Shi,
			duration: 8f, color: "#B64040", iconId: XjShiTraitIds.MoHe);
	}

	internal static void OnTrueSpiritResult(
		string actorName, bool returned, bool annihilated, string anchorName, string attackerName = "")
	{
		string victimDisplay = XjStringHelper.DisplayNameWithoutRealmSuffix(actorName, "一名今释");
		string attackerDisplay = string.IsNullOrWhiteSpace(attackerName)
			? string.Empty
			: XjStringHelper.DisplayNameWithoutRealmSuffix(attackerName, string.Empty);
		string anchorDisplay = string.IsNullOrWhiteSpace(anchorName) ? "所系承载地" : anchorName.Trim();
		string text;
		if (annihilated)
			text = "【真灵俱灭】" + victimDisplay + "真灵尽灭，不复归于释土。";
		else if (returned)
			text = attackerDisplay.Length > 0
				? "【真灵归土】" + victimDisplay + "被" + attackerDisplay + "击杀，真灵逃脱，归入"
					+ anchorDisplay + "等待重塑。"
				: "【真灵归土】" + victimDisplay + "真灵逃脱，归入" + anchorDisplay + "等待重塑。";
		else return;
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(text, XjAnnouncementCategory.Shi,
			duration: 7f, color: annihilated ? "#A84A4A" : "#C8AF75", iconId: XjShiTraitIds.MoHe);
	}

	internal static void OnBodyRebuilt(string actorName, string anchorName)
	{
		// 真灵归土时已经发布一次高位结果；肉身重塑避免二次重复公告。
	}

	internal static void OnSentientConsumption(Actor actor, string action, string targetName, bool major)
	{
		// 七相摄生属于释修修持过程，结果进史册，不逐次弹天下公告。
	}

	internal static void OnAncientLegacyJinDiEvent(string domainName, string formerOwnerName, string eventId,
		string discovererName, string rewardText)
	{
		string eventName = XjAncientShiLegacyEventIds.GetDisplay(eventId);
		if (string.IsNullOrWhiteSpace(eventName)) return;
		string land = string.IsNullOrWhiteSpace(domainName) ? "一处古释遗金地" : domainName.Trim();
		string former = string.IsNullOrWhiteSpace(formerOwnerName) ? "古释旧主" : formerOwnerName.Trim();
		string finder = string.IsNullOrWhiteSpace(discovererName)
			? "，尚无人得入其中"
			: "，为" + discovererName.Trim() + "所感";
		string result = string.IsNullOrWhiteSpace(rewardText) ? string.Empty : "（" + rewardText.Trim() + "）";
		string detail = string.Equals(eventId, XjAncientShiLegacyEventIds.ResponseBodyAwakening, StringComparison.Ordinal)
			? "旧应身由寂转灵，自此常显于世"
			: string.Equals(eventId, XjAncientShiLegacyEventIds.DharmaArchive, StringComparison.Ordinal)
				? "遗地开露旧藏，前人法意重见天日"
				: "隐世遗地短暂显露，旧日气机再现";
		string text = "【" + eventName + "】" + former + "遗下的" + land + detail + finder + result + "。";
		if (string.Equals(eventId, XjAncientShiLegacyEventIds.ResponseBodyAwakening, StringComparison.Ordinal))
			XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(text, XjAnnouncementCategory.Shi,
				duration: 7.5f, color: "#C89B45", iconId: XjShiTraitIds.Ancient);
		else
			XjBroadcastSystem.ShowRecordedCategorizedWorldTip(text, XjAnnouncementCategory.Shi,
				duration: 6f, color: "#D9C78A", iconId: XjShiTraitIds.Ancient);
	}

	internal static void OnSanctuaryConverted(Actor actor, string formerPath)
	{
		// 释土摄化属于人物转修履历，不再单独作为天下级公告。
	}

}
