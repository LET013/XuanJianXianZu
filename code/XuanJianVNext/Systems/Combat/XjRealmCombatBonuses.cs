using System;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Combat;

internal static class XjRealmCombatBonuses
{
	internal static bool TryGetProfile(Actor actor, out XjFaBaoBonusProfile profile)
	{
		profile = default;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| string.IsNullOrWhiteSpace(realmId))
		{
			return false;
		}

		return TryGetProfile(XjRealmSuppression.GetRealmTier(actor), out profile);
	}

	internal static bool TryGetProfile(int realmTier, out XjFaBaoBonusProfile profile)
	{
		profile = default;
		if (realmTier == XjRealmSuppression.TierJinDan)
		{
			profile = new XjFaBaoBonusProfile(
				0f, 0f,
				0.18f,
				0.10f,
				0.18f,
				0.10f,
				0.15f,
				0.06f,
				0.08f,
				0.05f,
				0.003f,
				0f, 0f, 0f,
				0.10f,
				0.08f,
				0.08f,
				0.08f,
				0.08f,
				0.06f,
				0.10f);
			return true;
		}

		if (realmTier == XjRealmSuppression.TierZiFu)
		{
			profile = new XjFaBaoBonusProfile(
				0f, 0f,
				0.10f,
				0.06f,
				0.12f,
				0.06f,
				0.08f,
				0.03f,
				0.05f,
				0.03f,
				0.002f,
				0f, 0f, 0f,
				0.06f,
				0.05f,
				0.05f,
				0.04f,
				0.04f,
				0.04f,
				0.05f);
			return true;
		}

		if (realmTier == XjRealmSuppression.TierZhuJi)
		{
			profile = new XjFaBaoBonusProfile(
				0f, 0f,
				0.05f,
				0.03f,
				0.06f,
				0.03f,
				0.04f,
				0f,
				0.03f,
				0f,
				0.001f,
				0f, 0f, 0f,
				0.03f,
				0.02f,
				0.02f,
				0f,
				0f,
				0.02f,
				0.02f);
			return true;
		}

		return false;
	}
}
