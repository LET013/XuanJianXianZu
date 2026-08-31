using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修自身境界到综合战斗位格的单向投影。它只服务战斗、排行与寿元，
/// 不会反向授予筑基、紫府、金丹的仙道权限。
/// </summary>
internal static class XjShiPowerRules
{
	internal static int GetCombatTier(Actor actor)
	{
		if (!TryRead(actor, out string realm, out float practice, out _, out _, out _, out _))
		{
			return XjRealmSuppression.TierNone;
		}

		if (string.Equals(realm, XjShiRealmIds.Monk, StringComparison.Ordinal))
		{
			return practice < 1000f ? XjRealmSuppression.TierTaiXi : XjRealmSuppression.TierLianQi;
		}
		if (string.Equals(realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			return XjRealmSuppression.TierZhuJi;
		}
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return XjRealmSuppression.TierJinDan;
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return XjRealmSuppression.TierDaoTai;
		return XjRealmSuppression.TierNone;
	}

	internal static string GetEquivalentRealmId(Actor actor)
	{
		return GetCombatTier(actor) switch
		{
			XjRealmSuppression.TierTaiXi => XjRealmIds.TaiXi,
			XjRealmSuppression.TierLianQi => XjRealmIds.LianQi,
			XjRealmSuppression.TierZhuJi => XjRealmIds.ZhuJi,
			XjRealmSuppression.TierZiFu => XjRealmIds.ZiFu,
			XjRealmSuppression.TierJinDan => XjRealmIds.JinDan,
			XjRealmSuppression.TierDaoTai => XjRealmIds.DaoTai,
			_ => string.Empty
		};
	}

	internal static int GetEquivalentStageOrder(Actor actor)
	{
		int tier = GetCombatTier(actor);
		int level = GetCombatLevel(actor);
		if (tier == XjRealmSuppression.TierTaiXi) return Math.Max(1, Math.Min(6, level));
		if (tier == XjRealmSuppression.TierLianQi) return Math.Max(1, Math.Min(9, level - 6));
		if (tier == XjRealmSuppression.TierZhuJi) return Math.Max(1, Math.Min(3, level - 15));
		if (tier == XjRealmSuppression.TierZiFu) return Math.Max(1, Math.Min(5, level - 18));
		// 法相四层分别对齐金丹初、中、后、巅峰；旧逻辑把所有法相固定为
		// 金丹初期，排行榜与实际高位成长脱节。世尊仍是单一的道胎等效层。
		if (tier == XjRealmSuppression.TierJinDan) return Math.Max(1, Math.Min(4, level - 23));
		if (tier == XjRealmSuppression.TierDaoTai) return 1;
		return 0;
	}

	internal static int GetCombatLevel(Actor actor)
	{
		if (!TryRead(actor, out string realm, out float practice, out int currentLife,
			out int completedLives, out string seatId, out string rebirthState))
		{
			return 0;
		}

		if (string.Equals(realm, XjShiRealmIds.Monk, StringComparison.Ordinal))
		{
			if (practice < 1000f) return 1 + Math.Min(5, Math.Max(0, (int)(practice / 200f)));
			float progress = Math.Min(1f, Math.Max(0f,
				(practice - 1000f) / Math.Max(1f, XjShiCatalog.DharmaMasterPracticeThreshold - 1000f)));
			return 7 + Math.Min(8, Math.Max(0, (int)(progress * 9f)));
		}

		if (string.Equals(realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal))
		{
			// 法师整体数值按筑基后期落地，高境转世恢复期也按筑基后期战斗层级处理。
			return 18;
		}
		if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			int seatRank = Math.Max(1, XjShiCatalog.GetSeatRank(seatId));
			return 15 + Math.Min(3, seatRank); // 萨陲16、发慧17、金莲18。
		}
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			int life = Math.Max(1, Math.Min(9, Math.Max(currentLife, completedLives + 1)));
			// 按原著位阶图将摩诃世数映射到紫府神通层：
			// 1-2世=紫府初期一神通，3-4世=紫府初期二神通，
			// 5-6世=紫府中期三神通，7世=紫府后期四神通，8-9世=五法俱全/圆满。
			if (life <= 2) return 19;
			if (life <= 4) return 20;
			if (life <= 6) return 21;
			if (life == 7) return 22;
			return 23;
		}
		if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string stage);
			if (string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal)) return 25;
			if (string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal)) return 26;
			if (string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal)) return 27;
			return 24;
		}
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return 28;
		return 0;
	}

	/// <summary>
	/// 释修寿元由当前释修境界单权威结算：僧侣基础寿元为三百年；
	/// 自法师起以一千年为基准，每提升一个大境界寿元翻倍。
	/// 轮回恢复期仍按当前法师境界取得一千年寿元，不再额外压低肉身寿限。
	/// </summary>
	internal static float GetLifespanYears(Actor actor)
	{
		if (!TryRead(actor, out string realm, out _, out _, out _, out _, out _)) return 80f;
		if (string.Equals(realm, XjShiRealmIds.Monk, StringComparison.Ordinal)) return 300f;
		if (string.Equals(realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)) return 1000f;
		if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)) return 2000f;
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) return 4000f;
		if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return 8000f;
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return 16000f;
		return 80f;
	}

	private static bool TryRead(Actor actor, out string realm, out float practice,
		out int currentLife, out int completedLives, out string seatId, out string rebirthState)
	{
		realm = string.Empty;
		practice = 0f;
		currentLife = 1;
		completedLives = 0;
		seatId = string.Empty;
		rebirthState = string.Empty;
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out realm);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiPractice, out practice);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCurrentLife, out currentLife);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCompletedLives, out completedLives);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiSeatId, out seatId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthState, out rebirthState);
		return XjShiCatalog.IsKnownRealm(realm);
	}
}
