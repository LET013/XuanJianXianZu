using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Craft;

namespace XuanJianVNext.Systems.Talisman;

internal static class XjTalismanCombatService
{
	internal static void ApplyActorDamageTalismans(Actor attacker, Actor defender, ref float damage)
	{
		if (damage <= 0f) return;
		if (defender?.data == null) return;
		long defenderId = ((BaseSystemData)defender.data).id;
		float health = Math.Max(0f, XjSafeCore.GetHealthSafe(defender, 0f));
		if (health <= 0f || damage < health * 0.35f) return;
		if (XjCraftDomainRegistry.HasCarry(defenderId, XjTalismanCatalog.Protection)
			&& XjCraftDomainRegistry.TryConsumeCarry(defenderId, XjTalismanCatalog.Protection))
		{
			damage *= 0.40f;
		}
	}

	internal static bool TryConsumeBreakFormation(Actor actor)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L && XjCraftDomainRegistry.TryConsumeCarry(actorId, XjTalismanCatalog.BreakFormation);
	}

	internal static float TryConsumeBreakthroughAid(Actor actor, string targetRealmId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(targetRealmId)) return 0f;
		float bonus = ResolveBreakthroughAidBonus(targetRealmId);
		if (bonus <= 0f) return 0f;
		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L && XjCraftDomainRegistry.TryConsumeCarry(actorId, XjTalismanCatalog.BreakthroughAid)
			? bonus : 0f;
	}

	internal static float TryConsumeShenTongAid(Actor actor)
	{
		if (actor?.data == null) return 0f;
		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L && XjCraftDomainRegistry.TryConsumeCarry(actorId, XjTalismanCatalog.CalmSpirit)
			? 0.06f : 0f;
	}

	private static float ResolveBreakthroughAidBonus(string targetRealmId)
	{
		if (string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 0.03f;
		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 0.04f;
		if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return 0.02f;
		return 0f;
	}
}
