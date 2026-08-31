using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 道胎位格的“存在性”统一判定。世尊在战力上已经等效道胎，因此只在明确的
/// 生存/换身/行迹语义上共享道胎规则；这里绝不把世尊反写成仙道 RealmId，
/// 也不让释修因此获得道胎功法、果位、器物等仙道权限。
/// </summary>
internal static class XjDaoTaiEquivalentExistenceRules
{
	internal static bool IsProtectedExistence(Actor actor)
	{
		return XjDaoTaiSpellScale.IsDaoTaiActor(actor) || IsWorldHonored(actor);
	}

	internal static bool IsWorldHonored(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		return string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal);
	}

	internal static string ResolvePresenceRealmId(Actor actor)
	{
		if (IsWorldHonored(actor)) return XjShiRealmIds.WorldHonored;
		return XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
	}

	internal static string ResolvePresenceTitle(Actor actor)
	{
		return IsWorldHonored(actor) ? "世尊" : "道胎";
	}

	internal static string ResolvePresenceTitle(string realmId)
	{
		return string.Equals((realmId ?? string.Empty).Trim(), XjShiRealmIds.WorldHonored, StringComparison.Ordinal)
			? "世尊"
			: "道胎";
	}
}
