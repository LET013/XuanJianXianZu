using System;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Craft;

namespace XuanJianVNext.Systems.Talisman;

internal static class XjTalismanDistributionSystem
{
	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive() || currentYear <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		int realmOrder = XjRealmHelper.GetOrder(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		if (realmOrder <= 0) return;
		if (!NeedsCarryAssignment(actorId, realmOrder)) return;
		if (!XjCraftOwnerResolver.TryResolvePreferred(actor, out XjCraftOwnerKey owner)) return;
		XjCraftDomainRegistry.TryAssignCarry(actorId, owner, XjTalismanCatalog.Protection, currentYear);
		if (realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZhuJi))
		{
			XjCraftDomainRegistry.TryAssignCarry(actorId, owner, XjTalismanCatalog.BreakthroughAid, currentYear);
			if (realmOrder >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu))
			{
				XjCraftDomainRegistry.TryAssignCarry(actorId, owner, XjTalismanCatalog.BreakFormation, currentYear);
				XjCraftDomainRegistry.TryAssignCarry(actorId, owner, XjTalismanCatalog.CalmSpirit, currentYear);
			}
		}
		else
		{
			XjCraftDomainRegistry.TryAssignCarry(actorId, owner, XjTalismanCatalog.Swift, currentYear);
		}
	}

	private static bool NeedsCarryAssignment(long actorId, int realmOrder)
	{
		if (!XjCraftDomainRegistry.HasCarry(actorId, XjTalismanCatalog.Protection)) return true;
		if (realmOrder < XjRealmHelper.GetOrder(XjRealmIds.ZhuJi))
		{
			return !XjCraftDomainRegistry.HasCarry(actorId, XjTalismanCatalog.Swift);
		}
		if (!XjCraftDomainRegistry.HasCarry(actorId, XjTalismanCatalog.BreakthroughAid)) return true;
		if (realmOrder < XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) return false;
		return !XjCraftDomainRegistry.HasCarry(actorId, XjTalismanCatalog.BreakFormation)
			|| !XjCraftDomainRegistry.HasCarry(actorId, XjTalismanCatalog.CalmSpirit);
	}
}
