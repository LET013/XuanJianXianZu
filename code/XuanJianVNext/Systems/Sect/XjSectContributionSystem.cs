using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.Systems.Sect;

internal static class XjSectContributionSystem
{
	internal static bool RecordCraftContribution(Actor actor, float points, int currentYear, string responsibility)
	{
		return TryResolveActorSeat(actor, out long sectId, out long familyId)
			&& XjSectRepository.TryApplyFamilyContribution(sectId, familyId, 0f, points, currentYear, responsibility);
	}

	internal static bool RecordGeneralContribution(Actor actor, float points, int currentYear, string responsibility)
	{
		return TryResolveActorSeat(actor, out long sectId, out long familyId)
			&& XjSectRepository.TryApplyFamilyContribution(sectId, familyId, points, 0f, currentYear, responsibility);
	}

	internal static bool TryResolveActorSeat(Actor actor, out long sectId, out long familyId)
	{
		sectId = 0L;
		familyId = 0L;
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyId) || familyId <= 0L) return false;
		sectId = XjSectRepository.ResolveActorSectId(actor);
		return sectId > 0L;
	}
}
