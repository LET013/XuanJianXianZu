using XuanJianVNext.Data.Craft;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Craft;

internal static class XjCraftOwnerResolver
{
	internal static bool TryResolvePreferred(Actor actor, out XjCraftOwnerKey owner)
	{
		return TryResolveSect(actor, out owner) || TryResolveFamily(actor, out owner) || TryResolvePersonal(actor, out owner);
	}

	internal static bool TryResolvePersonal(Actor actor, out XjCraftOwnerKey owner)
	{
		owner = default;
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		owner = new XjCraftOwnerKey(XjCraftOwnerScope.Personal, actorId);
		return true;
	}

	internal static bool TryResolveFamily(Actor actor, out XjCraftOwnerKey owner)
	{
		owner = default;
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId) || familyId <= 0L) return false;
		owner = new XjCraftOwnerKey(XjCraftOwnerScope.Family, familyId);
		return true;
	}

	internal static bool TryResolveSect(Actor actor, out XjCraftOwnerKey owner)
	{
		owner = default;
		if (actor?.data == null) return false;
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		if (sectId <= 0L) return false;
		owner = new XjCraftOwnerKey(XjCraftOwnerScope.Sect, sectId);
		return true;
	}
}
