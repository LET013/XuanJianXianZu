using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 首位空证长庚成功的真君羽士在开道城市留下世界唯一剑碑。
/// 剑碑不建立建筑AI，只保存城市锚点；角色年度结算时恰在该城，才进行一次低频感悟。
/// </summary>
internal static class XjLongGengSwordSteleSystem
{
	internal const string Inscription = "楼陵紫观寻常道，太衍华央一小宫。\n撞破青锋割日月，天公惧我半韬功。";
	private const int InsightIntervalYears = 20;

	internal static bool EnsureCreated(Actor founder, int currentYear)
	{
		if (!XjFuQiSwordWorldState.TryCreateSwordStele(founder, currentYear, Inscription)) return false;
		string founderName = SafeActorName(founder);
		string cityName = XjFuQiSwordWorldState.SwordSteleCityName;
		string body = founderName + "空证长庚后，于" + (string.IsNullOrWhiteSpace(cityName) ? "开道之地" : cityName)
			+ "留下长庚剑碑。碑上刻曰：\n" + Inscription + "\n后世剑修可由碑文剑痕参悟剑意。";
		long actorId = ((BaseSystemData)founder.data).id;
		long cityId = founder.city?.data?.id ?? 0L;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Cultivation,
			"长庚剑碑立世",
			body,
			5,
			isProtected: true,
			actorId: actorId,
			actorName: founderName,
			cityId: cityId,
			year: currentYear,
			eventType: "LongGengSwordSteleEstablished",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate));
		XjBroadcastSystem.ShowRecordedWorldTip(
			"【长庚剑碑】" + body,
			duration: 6f,
			iconId: XjEventIconCatalog.GongFaAcquire);
		return true;
	}

	internal static void TickActor(Actor actor, int currentYear)
	{
		if (!XjSafeCore.IsAliveActor(actor) || currentYear <= 0) return;
		// 初代空证时若角色暂时没有城市，不永久丢失剑碑。其后每年只由初代本人
		// 在进入稳定城市后补建一次，不增加世界扫描。
		if (!XjFuQiSwordWorldState.HasSwordStele
			&& (!XjFuQiSwordWorldState.IsFoundingActor(actor) || !EnsureCreated(actor, currentYear))) return;
		if (!XjFuQiSwordWorldState.IsActorAtSwordStele(actor)) return;
		bool fuQiSwordCandidate = XjFuQiSwordDaoSystem.IsSwordCandidate(actor);
		bool swordArtist = XjWeaponArtSystem.HasBoundKind(actor, out string kind)
			&& string.Equals(kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal);
		if (!fuQiSwordCandidate && !swordArtist) return;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLongGengSwordSteleLastInsightYear, out int lastYear);
		if (lastYear > 0 && currentYear - lastYear < InsightIntervalYears) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjLongGengSwordSteleLastInsightYear, currentYear);

		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		float chance = Math.Clamp(0.04f + Math.Max(0f, huiGuang) / 1600f + Math.Max(0, aptitude - 2) * 0.01f, 0.04f, 0.16f);
		if (XjDeterministicHash.Roll01(actorId, currentYear, "longgeng_sword_stele", "insight") >= chance) return;

		string result;
		string intentName = string.Empty;
		bool gained = fuQiSwordCandidate
			&& XjFuQiSwordDaoSystem.TryComprehendFromSwordStele(actor, currentYear, out intentName);
		if (gained)
		{
			result = "由碑文剑痕观得前人剑意“" + intentName + "”";
		}
		else if (!XjWeaponArtSystem.TryReceiveSwordSteleInsight(actor, currentYear, out result))
		{
			return;
		}

		XjFuQiSwordWorldState.RecordSwordSteleInsight();
		string actorName = SafeActorName(actor);
		string body = actorName + "在长庚剑碑前驻足观悟，" + result + "。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Cultivation,
			actorName + "参悟长庚剑碑",
			body,
			2,
			actorId: actorId,
			actorName: actorName,
			cityId: actor.city?.data?.id ?? 0L,
			year: currentYear,
			eventType: "LongGengSwordSteleInsight",
			visibilityFlags: (int)XjHistoryVisibility.Personal,
			mirrorToWorldLog: false);
	}

	private static string SafeActorName(Actor actor)
	{
		try { return actor?.getName() ?? "无名剑修"; }
		catch (System.Exception xjCaught103_1) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjLongGengSwordSteleSystem.cs:103", xjCaught103_1);
			 return "无名剑修"; }
	}
}
