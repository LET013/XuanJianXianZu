using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Alchemy;

internal static class XjAlchemyOwnerResolver
{
	internal static bool TryGetPersonal(Actor actor, out XjAlchemyOwnerKey owner)
	{
		owner = default;
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		owner = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Personal, actorId);
		return true;
	}

	internal static bool TryGetFamily(Actor actor, out XjAlchemyOwnerKey owner)
	{
		owner = default;
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		if (XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity identity)
			&& identity.Found && identity.FamilyStableIdValue > 0L)
		{
			owner = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Family, identity.FamilyStableIdValue);
			return true;
		}
		if (XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord indexed)
			&& indexed.Found && indexed.RootActorId > 0L)
		{
			owner = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Family, indexed.RootActorId);
			return true;
		}
		return false;
	}

	internal static bool TryGetZongMen(Actor actor, out XjAlchemyOwnerKey owner)
	{
		owner = default;
		if (actor?.data == null) return false;
		long sectId = XjSectRepository.ResolveActorSectId(actor);
		if (sectId <= 0L) return false;
		owner = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.ZongMen, sectId);
		return true;
	}
}
