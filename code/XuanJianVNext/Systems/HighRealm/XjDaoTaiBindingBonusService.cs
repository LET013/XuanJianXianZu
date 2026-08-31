using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjDaoTaiBindingBonusService
{
	internal static bool TryGetSignature(long actorId, out string signature)
	{
		signature = string.Empty;
		if (!XjFruitPositionWorldState.TryGetDaoTaiBinding(actorId, out XjDaoTaiPositionBindingArchiveRecord binding)
			|| binding == null)
		{
			return false;
		}
		signature = "daotai_binding|" + binding.PrimaryPositionId + "|" + binding.SecondaryPositionId + "|" + binding.SecondaryKind;
		return true;
	}

	internal static bool TryGetProfile(Actor actor, out XjFaBaoBonusProfile profile)
	{
		profile = default;
		if (actor?.data == null || !XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjFruitPositionWorldState.TryGetDaoTaiBinding(actorId, out XjDaoTaiPositionBindingArchiveRecord binding)
			|| binding == null
			|| !XjDaoTaiDualPositionSystem.TryResolveBindingPair(binding, out _, out XjDerivedPositionArchiveRecord derived)
			|| derived == null)
		{
			return false;
		}

		if (string.Equals(derived.PositionType, XjGuoWeiCalculator.YuWei, System.StringComparison.Ordinal))
		{
			profile = new XjFaBaoBonusProfile(
				0.08f, 0.04f, 0f, 0.18f, 0.20f, 0f, 0.12f, 0f,
				healbackBonus: 0.12f,
				lifespanBonus: 0.25f,
				breakthroughChanceBonus: 0.03f);
			return true;
		}
		if (string.Equals(derived.PositionType, XjGuoWeiCalculator.RunWei, System.StringComparison.Ordinal))
		{
			profile = new XjFaBaoBonusProfile(
				0.04f, 0.16f, 0.18f, 0f, 0f, 0.08f, 0f, 0.04f,
				accuracyBonus: 0.08f,
				critBonus: 0.08f,
				attackSpeedBonus: 0.12f,
				sameRealmDamageBonus: 0.12f,
				shieldBreakBonus: 0.08f,
				breakthroughChanceBonus: 0.05f,
				trueDamageRatio: 0.03f);
			return true;
		}
		return false;
	}
}
