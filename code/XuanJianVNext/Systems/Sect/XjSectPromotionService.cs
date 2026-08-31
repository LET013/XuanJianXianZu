using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.GongFa;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Realm-promotion effects that belong to the sect domain.  Promotion callers must not
/// round-trip through the legacy city-mirror service merely to confirm membership.
/// </summary>
internal static class XjSectPromotionService
{
	internal static bool TryPromoteZiFu(Actor actor, City representativeCity, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive() || representativeCity?.data == null
			|| !XjSectOwnership.TryResolve(representativeCity, out XjSectArchiveRecord sect)
			|| sect?.SectId <= 0L)
		{
			return false;
		}

		XjSectMembershipService.EnsureMember(
			representativeCity,
			actor,
			System.Math.Max(0, currentYear),
			"ZiFuPromotionEnsureMember");
		XjSectRepository.ReconcilePeaks(sect.SectId, System.Math.Max(0, currentYear));
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "ZongMenZiFuPromotion");

		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member)
			&& member?.SectId == sect.SectId;
	}
}
