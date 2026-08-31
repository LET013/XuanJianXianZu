using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 今释高命数“宿世直证”入口。它与常规修持晋升、法师/怜愍年度越阶证摩诃完全分开：
/// 只在角色自己的释修命数首次跨入一个更高档位时做一次确定性判定，失败后该档永不重抽；
/// 因而可以极少量出现“入释不久便显出数世摩诃，甚至九世俱现而直证法相”的人物，
/// 又不会因为寿命足够长而让所有高命数释修最终必定中奖。
/// </summary>
internal static class XjShiFateBreakthroughSystem
{
	internal static bool TryResolve(Actor actor, in XjShiSnapshot snapshot, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0
			|| !XjCultivationPathRules.IsShi(actor)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe)
			|| string.Equals(snapshot.RebirthState, XjShiRebirthStateIds.Recovering, StringComparison.Ordinal)
			|| !XjZhantanlinSystem.IsPlaced) return false;

		float mingShu = XjShiMingShuSystem.GetValue(actor);
		int band = ResolveBand(mingShu);
		if (band <= 0) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiFateDirectLeapBand, out int resolvedBand);
		if (resolvedBand >= band) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		int totalChance = ResolveTotalChance(band);
		int dharmaFormChance = ResolveDharmaFormChance(band);
		int roll = XjDeterministicHash.PositiveIndex(actorId + band * 1000003L,
			"shi_fate_direct_attainment_v1|" + band, 10000);
		if (roll >= totalChance)
		{
			// 这一档命数已经“显过一次而未应”，以后只在跨入更高档时重新判定。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiFateDirectLeapBand, band);
			return false;
		}

		bool seekDharmaForm = dharmaFormChance > 0 && roll < dharmaFormChance;
		int targetLife = seekDharmaForm ? 9 : ResolveMoHeLife(actorId, band);
		if (!TryAttainDirectMoHe(actor, snapshot, annualYear, targetLife))
		{
			// 命数判定本身已经成功，但若108摩诃位等结构暂时无空缺，不消耗该档。
			// 后续仍以同一个固定结果重试，不会每年重新抽奖。
			return false;
		}

		bool attainedDharmaForm = seekDharmaForm
			&& XjShiHighRealmSystem.TryAttainFateDharmaForm(actor, annualYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiFateDirectLeapBand, band);
		XjThreeBookWriter.RecordShiFateDirectAttainment(actor, annualYear, targetLife, attainedDharmaForm);
		if (!attainedDharmaForm)
		{
			XjWorldHistoryStore.RecordActorEvent(actor,
				"命数骤然昭显，宿世格位归一，不循寻常次第而直成第" + targetLife + "世摩诃。",
				XjShiTraitIds.MoHe);
		}
		return true;
	}

	private static bool TryAttainDirectMoHe(Actor actor, in XjShiSnapshot snapshot,
		int annualYear, int targetLife)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		XjShiDomainRecord zhantanlin = XjShiDomainState.EnsureZhantanlin(annualYear);
		if (zhantanlin == null || string.IsNullOrWhiteSpace(zhantanlin.DomainId)
			|| !XjShiDomainState.IsDomainAvailableForMoHeClaim(zhantanlin.DomainId, actorId)
			|| !XjShiDomainState.TryClaimMoHePosition(zhantanlin.DomainId, actorId, annualYear)) return false;

		if (string.Equals(snapshot.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
			XjShiWorldRegistry.ReleaseAttachment(actorId, annualYear);

		int life = Math.Clamp(targetLife, 1, 9);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, life);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, Math.Max(0, life - 1));
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiPractice, out float currentPractice);
		float lifePracticeFloor = XjShiCatalog.MoHePracticeThreshold + Math.Max(0, life - 1) * 6000f;
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, Math.Max(currentPractice, lifePracticeFloor));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, zhantanlin.DomainId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, zhantanlin.DomainId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, XjShiJinDiStatusIds.Manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Attached);
		return XjShiState.TrySetRealm(actor, XjShiRealmIds.MoHe, annualYear, manualOverride: false);
	}

	private static int ResolveBand(float mingShu)
	{
		if (mingShu >= XjShiCatalog.FateDirectLeapBand5MingShu) return 5;
		if (mingShu >= XjShiCatalog.FateDirectLeapBand4MingShu) return 4;
		if (mingShu >= XjShiCatalog.FateDirectLeapBand3MingShu) return 3;
		if (mingShu >= XjShiCatalog.FateDirectLeapBand2MingShu) return 2;
		if (mingShu >= XjShiCatalog.FateDirectLeapBand1MingShu) return 1;
		return 0;
	}

	private static int ResolveTotalChance(int band)
	{
		return band switch
		{
			5 => XjShiCatalog.FateDirectLeapBand5ChancePerTenThousand,
			4 => XjShiCatalog.FateDirectLeapBand4ChancePerTenThousand,
			3 => XjShiCatalog.FateDirectLeapBand3ChancePerTenThousand,
			2 => XjShiCatalog.FateDirectLeapBand2ChancePerTenThousand,
			_ => XjShiCatalog.FateDirectLeapBand1ChancePerTenThousand
		};
	}

	private static int ResolveDharmaFormChance(int band)
	{
		if (band >= 5) return XjShiCatalog.FateDirectDharmaFormBand5ChancePerTenThousand;
		if (band == 4) return XjShiCatalog.FateDirectDharmaFormBand4ChancePerTenThousand;
		return 0;
	}

	private static int ResolveMoHeLife(long actorId, int band)
	{
		int min;
		int max;
		switch (band)
		{
			case 1: min = 2; max = 3; break;
			case 2: min = 3; max = 5; break;
			case 3: min = 5; max = 7; break;
			case 4: min = 7; max = 9; break;
			default: return 9;
		}
		return min + XjDeterministicHash.PositiveIndex(actorId + band * 7919L,
			"shi_fate_direct_life_v1|" + band, max - min + 1);
	}
}
