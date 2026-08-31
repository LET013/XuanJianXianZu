using System;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 广增承载的事件入口：度化、击败异脉高修、金地聚合。这里只接收已经发生的
/// 结果并写入命数/承载增长，绝不自行扫描战斗或人口。
/// </summary>
internal static class XjShiExpansionSystem
{
	internal static void OnSuccessfulPreaching(Actor teacher, Actor target, int annualYear,
		int targetTierBefore, float targetMingShu)
	{
		if (teacher?.data == null || target?.data == null || annualYear <= 0) return;
		long targetId = ((BaseSystemData)target.data).id;
		bool important = targetTierBefore >= XjRealmSuppression.TierLianQi || targetMingShu >= 80f;
		int contribution = important ? 3 : 1;
		float mingShu = important ? 5f : 1f;
		XjShiDomainState.AddContribution(teacher, contribution, annualYear);
		XjShiMingShuSystem.TryGrantEvent(teacher, annualYear,
			"convert:" + targetId, mingShu, "conversion");
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiLastExpansionYear, annualYear);
		XjShiSentientConsumptionSystem.OnSuccessfulPreaching(teacher, target, annualYear, important);
		XjThreeBookWriter.RecordShiPreaching(teacher, target, annualYear, important);
	}

	internal static void OnRealmOrPositionAttained(Actor actor, int annualYear,
		string realm, string seatId, bool selfProvedJinDi, bool becameLiangLi)
	{
		if (actor?.data == null || annualYear <= 0 || !XjCultivationPathRules.IsShi(actor)) return;
		_ = becameLiangLi; // 旧签名兼容；当前释修不再存在“主持／量力”身份。
		long actorId = ((BaseSystemData)actor.data).id;
		float award = 0f;
		string key = string.Empty;
		string eventType = "position";
		if (selfProvedJinDi)
		{
			award = 80f;
			key = "self_prove_jindi:" + actorId;
			eventType = "domain";
		}
		else if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			award = 50f;
			key = "attain_mohe:" + actorId;
		}
		else if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			award = string.Equals(seatId, XjShiSeatIds.JinLian, StringComparison.Ordinal) ? 20f : 10f;
			key = "attain_lianmin:" + seatId + ":" + actorId;
		}
		if (award > 0f)
		{
			XjShiMingShuSystem.TryGrantEvent(actor, annualYear, key, award, eventType);
			XjThreeBookWriter.RecordShiPositionAttained(actor, annualYear, realm, seatId, selfProvedJinDi, becameLiangLi);
		}
	}

	internal static void OnHighShiKilled(Actor victimActor, in XjDeathSnapshot deathSnapshot, in XjShiSnapshot victim)
	{
		if (!deathSnapshot.Found || deathSnapshot.LastAttackerId <= 0L
			|| deathSnapshot.LastAttackerId == deathSnapshot.ActorId
			|| !XjActorRegistry.ResolveKnownOrWorld(deathSnapshot.LastAttackerId, out Actor killer)
			|| killer?.data == null || !killer.isAlive() || !XjCultivationPathRules.IsShi(killer)
			|| !XjShiState.TryBuildSnapshot(killer, out XjShiSnapshot killerSnapshot)) return;
		XjActorAccessor.TryGetString(killer, XjActorDataKeys.ShiLineageId, out string killerLineage);
		XjActorAccessor.TryGetString(victimActor, XjActorDataKeys.ShiLineageId, out string victimLineage);
		if (!string.IsNullOrWhiteSpace(killerLineage)
			&& string.Equals(killerLineage, victimLineage, StringComparison.Ordinal)) return;

		int victimRank = XjShiCatalog.GetRank(victim.Realm);
		float mingShu;
		int contribution;
		if (victimRank >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)) { mingShu = 80f; contribution = 80; }
		else if (victimRank >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe)) { mingShu = 40f; contribution = 40; }
		else if (victimRank >= XjShiCatalog.GetRank(XjShiRealmIds.LianMin)) { mingShu = 10f; contribution = 15; }
		else return;

		int year = Math.Max(1, deathSnapshot.Year);
		XjShiMingShuSystem.TryGrantEvent(killer, year,
			"defeat_shi:" + deathSnapshot.ActorId, mingShu, "battle");
		XjShiDomainState.AddContribution(killer, contribution, year);
		XjActorAccessor.SetInt(killer, XjActorDataKeys.ShiLastExpansionYear, year);
		XjWorldHistoryStore.RecordActorEvent(killer,
			"与异脉高修交锋，击败" + (string.IsNullOrWhiteSpace(deathSnapshot.Name) ? "一名释修" : deathSnapshot.Name)
				+ "，夺其一分命数以广释土。", XjShiCatalog.GetRealmTraitId(killerSnapshot.Realm));
		XjThreeBookWriter.RecordShiHighRealmVictory(killer, deathSnapshot.ActorId, deathSnapshot.Name, victim.Realm, year);
	}

	internal static void TickHighRealmExpansion(Actor actor, int annualYear)
	{
		if (actor?.data == null || annualYear <= 0 || !actor.isAlive()
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(snapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.MoHe)) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiLastJinDiAbsorptionYear, out int lastYear);
		if (lastYear > 0 && annualYear - lastYear < XjShiCatalog.JinDiAbsorptionIntervalYears) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if ((annualYear + actorId) % XjShiCatalog.JinDiAbsorptionIntervalYears != 0L) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastJinDiAbsorptionYear, annualYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		int chance = XjShiLineagePolicy.GetJinDiAbsorptionChanceBasis(lineageId)
			+ Math.Min(2500, (int)XjShiMingShuSystem.GetEffectiveValue(actor, annualYear) * 3);
		if (XuanJianVNext.Core.XjDeterministicHash.PositiveIndex(actorId + annualYear,
			"shi_absorb_jindi_v1", 10000) >= Math.Min(8500, chance)) return;
		TryCommitJinDiAbsorption(actor, annualYear, out _);
	}

	internal static bool TryCommitJinDiAbsorption(Actor actor, int annualYear, out string absorbedDomainId)
	{
		absorbedDomainId = string.Empty;
		if (actor?.data == null || annualYear <= 0 || !actor.isAlive()
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !XjShiDomainState.TryAbsorbJinDi(actor, annualYear, out absorbedDomainId)) return false;
		XjShiMingShuSystem.TryGrantEvent(actor, annualYear,
			"absorb_jindi:" + absorbedDomainId, 100f, "domain");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastJinDiAbsorptionYear, annualYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastExpansionYear, annualYear);
		XjWorldHistoryStore.RecordActorEvent(actor,
			"所系承载地聚合一处无主金地，摩诃位与怜愍位承载随之增长。", XjShiCatalog.GetRealmTraitId(snapshot.Realm));
		XjBroadcastSystem.BroadcastBLevelWorldEvent(actor.getName()
			+ "所系承载地聚合无主金地，承载随之大增。");
		XjThreeBookWriter.RecordShiJinDiAbsorbed(actor, absorbedDomainId, annualYear);
		return true;
	}
}
