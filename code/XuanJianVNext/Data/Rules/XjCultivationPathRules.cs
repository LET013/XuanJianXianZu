using System;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Data.Rules;

/// <summary>
/// 修炼体系是角色修炼身份的权威事实源。境界强弱比较由 CombatTier 负责，
/// 功法、采气、仙基、求金等玩法入口必须先检查修炼体系，不能仅凭境界层级判断。
/// </summary>
internal static class XjCultivationPathIds
{
	internal const string ZiFuJinDan = "zifu_jindan";
	internal const string FuQiYangXing = "fuqi_yangxing";
	internal const string Shi = "shi";
}

internal static class XjFuQiLineageIds
{
	// 长庚道统不再以未命名占位对外投影。
	internal const string Sword = "fuqi_sword";
}

internal static class XjCultivationPathRules
{
	internal static bool TryGetPath(Actor actor, out string path)
	{
		path = string.Empty;
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.CultivationPath, out path)
			&& IsKnownPath(path);
	}

	internal static bool IsKnownPath(string path)
	{
		return string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal)
			|| string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal)
			|| string.Equals(path, XjCultivationPathIds.Shi, StringComparison.Ordinal);
	}

	internal static bool IsZiFuJinDan(Actor actor)
	{
		return TryGetPath(actor, out string path)
			&& string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal);
	}

	internal static bool IsFuQiYangXing(Actor actor)
	{
		return TryGetPath(actor, out string path)
			&& string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal);
	}

	internal static bool IsShi(Actor actor)
	{
		return TryGetPath(actor, out string path)
			&& string.Equals(path, XjCultivationPathIds.Shi, StringComparison.Ordinal);
	}

	internal static bool IsZiFuRealm(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalized, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	internal static bool IsFuQiRealm(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	/// <summary>
	/// 紫府与服气真人是两条修法在家族、宗门和世界玩法中的同阶身份。
	/// 神通来源、功法、采气与求金过程仍按修炼体系隔离。
	/// </summary>
	internal static bool IsZhenRenEquivalentRealm(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalized, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal);
	}

	internal static bool IsZhenRenEquivalent(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		return IsZhenRenEquivalentRealm(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
	}

	/// <summary>
	/// 金丹与真君羽士是两条修法对同一终境的不同称谓。除服气寿元单独结算外，
	/// 果位、洞天、法宝、轮回与战斗层级通过此口径共享金丹结果；功法、采气、
	/// 仙基、神通与求金过程仍由修炼体系严格隔离。
	/// </summary>
	internal static bool IsJinDanEquivalentRealm(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
	}

	internal static bool IsJinDanEquivalent(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		return IsJinDanEquivalentRealm(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
	}
}

/// <summary>
/// 服气养性的资质上限先作为统一规则落盘，后续神妙推进只调用这里，
/// 避免剑道实现再次复制一套互相偏移的资质判断。
/// </summary>
internal static class XjFuQiAptitudeRules
{
	// 四档资质不再被服气入口硬锁死；是否真正感气仍由命数+道慧的一次性判定决定。
	internal static bool CanSenseDaoQi(int aptitude) => aptitude >= 4 && aptitude <= 6;
	internal static bool CanReachHuangGuan(int aptitude) => CanSenseDaoQi(aptitude);
	internal static bool CanReachZhenRen(int aptitude) => CanSenseDaoQi(aptitude);
	internal static bool CanAttemptZhenJunYuShi(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude))
		{
			return false;
		}
		if (aptitude == 6) return true;
		if (aptitude == 5)
		{
			return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiRank5HighRealmRouteChecked, out int checkedFlag)
				&& checkedFlag > 0
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiRank5StayFuQi, out int stayFuQi)
				&& stayFuQi > 0;
		}
		return aptitude == 4
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiRank4EligibilityChecked, out int rank4Checked)
			&& rank4Checked > 0
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiRank4ZhenJunEligible, out int rank4Eligible)
			&& rank4Eligible > 0;
	}
}
