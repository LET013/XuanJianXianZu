using System;
using System.Collections.Generic;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// Family-level bloodline aggregate used by age-five aptitude resolution.
/// Each family is scanned at most once between membership/bloodline changes.
/// </summary>
internal static class XjFamilyBloodlineAggregateCache
{
	private static readonly Dictionary<long, float> HighestAwakeningBonusByFamilyId = new Dictionary<long, float>();

	internal static float GetHighestAwakeningQualityBonus(long familyId)
	{
		if (familyId <= 0L)
		{
			return 0f;
		}

		if (HighestAwakeningBonusByFamilyId.TryGetValue(familyId, out float cached))
		{
			return cached;
		}

		float highest = 0f;
		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
		{
			highest = Math.Max(highest, XjBloodlineBirthRules.GetOwnAwakeningQualityBonusForAggregate(member));
			if (highest >= 0.2f)
			{
				break;
			}
		}

		HighestAwakeningBonusByFamilyId[familyId] = highest;
		return highest;
	}

	internal static void InvalidateFamily(long familyId)
	{
		if (familyId > 0L)
		{
			HighestAwakeningBonusByFamilyId.Remove(familyId);
		}
	}

	internal static void InvalidateActor(Actor actor)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		if (actorId > 0L
			&& XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity))
		{
			InvalidateFamily(identity.FamilyStableIdValue);
		}
	}

	internal static void Clear()
	{
		HighestAwakeningBonusByFamilyId.Clear();
	}
}
