using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Keeps one cultivation family on a stable sect line. A newly promoted ZiFu
/// joins the sect already held by the family's other high-realm cultivators;
/// it must not found a new sect and pull the whole family away from its assets.
/// </summary>
internal static class XjFamilySectContinuity
{
    private sealed class Candidate
    {
        internal long SectId;
        internal City City;
        internal int HighRealmMembers;
        internal int FoundingYear;
    }

    internal static bool TryJoinPreferredFamilySect(Actor actor, long familyId, int currentYear)
    {
        if (actor?.data == null || !actor.isAlive() || familyId <= 0L)
        {
            return false;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        if (!TryResolvePreferredFamilySect(actorId, familyId, out City city, out _))
        {
            // The first high-realm member may already be a disciple elsewhere.
            // Keep that valid membership instead of creating a second sect.
            if (!XjSectAuthorityStore.TryGetSectId(actorId, out long ownSectId)
                || ownSectId <= 0L
                || !XjSectOwnership.TryResolvePrimaryCity(ownSectId, out city))
            {
                return false;
            }
        }

        return XjSectPromotionService.TryPromoteZiFu(actor, city, Math.Max(0, currentYear));
    }

    internal static bool TryResolvePreferredFamilySect(
        long promotedActorId,
        long familyId,
        out City preferredCity,
        out long preferredSectId)
    {
        preferredCity = null;
        preferredSectId = 0L;
        if (familyId <= 0L)
        {
            return false;
        }

        Dictionary<long, Candidate> candidates = new Dictionary<long, Candidate>();
        foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
        {
            if (member?.data == null || !member.isAlive())
            {
                continue;
            }

            long memberId = ((BaseSystemData)member.data).id;
            if (memberId <= 0L || memberId == promotedActorId
                || XjRealmSuppression.GetRealmTier(member) < XjRealmSuppression.TierZiFu)
            {
                continue;
            }

            if (!XjSectAuthorityStore.TryGetMember(memberId, out XjSectMemberArchiveRecord membership)
                || membership?.SectId <= 0L
                || !XjSectRepository.TryGetBySectId(membership.SectId, out XjSectArchiveRecord sect)
                || sect == null
                || !XjSectOwnership.TryResolvePrimaryCity(sect, out City city))
            {
                continue;
            }

            if (!candidates.TryGetValue(sect.SectId, out Candidate candidate))
            {
                candidate = new Candidate
                {
                    SectId = sect.SectId,
                    City = city,
                    FoundingYear = sect.FoundingYear > 0 ? sect.FoundingYear : int.MaxValue
                };
                candidates.Add(candidate.SectId, candidate);
            }
            candidate.HighRealmMembers++;
        }

        Candidate best = null;
        foreach (Candidate candidate in candidates.Values)
        {
            if (candidate?.City?.data == null)
            {
                continue;
            }
            if (best == null
                || candidate.HighRealmMembers > best.HighRealmMembers
                || (candidate.HighRealmMembers == best.HighRealmMembers
                    && candidate.FoundingYear < best.FoundingYear)
                || (candidate.HighRealmMembers == best.HighRealmMembers
                    && candidate.FoundingYear == best.FoundingYear
                    && candidate.SectId < best.SectId))
            {
                best = candidate;
            }
        }

        if (best == null)
        {
            // A family's sect line survives its founding ZiFu. When no living
            // high-realm relative can vote, use the persisted family seat so a
            // later ZiFu does not found a replacement sect beside the old assets.
            if (!XjSectRepository.TryResolveFamilySectId(familyId, out long seatedSectId)
                || seatedSectId <= 0L
                || !XjSectOwnership.TryResolvePrimaryCity(seatedSectId, out City seatedCity))
            {
                return false;
            }
            preferredCity = seatedCity;
            preferredSectId = seatedSectId;
            return true;
        }
        preferredCity = best.City;
        preferredSectId = best.SectId;
        return true;
    }
}
