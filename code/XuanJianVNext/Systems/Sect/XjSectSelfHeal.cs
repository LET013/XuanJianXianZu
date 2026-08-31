using System.Collections.Generic;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门权威状态的有界维护。这里只校验权威记录并触发单向投影，
/// 不再把 city.data 成员表或角色镜像写回业务状态。
/// </summary>
internal static class XjSectSelfHeal
{
	internal static bool Repair(in XjSectMaintenanceSnapshot snapshot)
	{
		City city = snapshot.City;
		if (!XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect) || sect == null) return false;
		bool changed = NormalizeStoredPeakNames(sect);
		List<long> members = new List<long>(XjSectAuthorityStore.GetActorIdsForSect(sect.SectId));
		for (int i = 0; i < members.Count; i++)
		{
			long actorId = members[i];
			Actor actor = XjScheduler.ResolveActor(actorId, out Actor resolved) ? resolved : null;
			if (actor?.data != null && actor.isAlive()
				&& XjCultivationEligibility.CanReceiveXuanJianContent(actor)
				&& XjCultivationEligibility.HasCultivationAptitudeTrait(actor)
				&& !XjCultivationPathRules.IsShi(actor)) continue;
			changed |= XjSectCommands.RemoveMember(sect.SectId, actorId, snapshot.CurrentYear);
		}

		if (sect.SovereignActorId > 0L
			&& (!XjSectAuthorityStore.TryGetMember(sect.SovereignActorId, out XjSectMemberArchiveRecord sovereign)
				|| sovereign.SectId != sect.SectId))
		{
			changed |= XjSectCommands.ClearSovereign(sect.SectId, snapshot.CurrentYear);
		}

		if (sect.Peaks != null)
		{
			for (int i = sect.Peaks.Count - 1; i >= 0; i--)
			{
				XjSectPeakArchiveRecord peak = sect.Peaks[i];
				if (peak == null || peak.PeakId < 2)
				{
					sect.Peaks.RemoveAt(i);
					changed = true;
					continue;
				}
				if (peak.PeakMasterActorId <= 0L) continue;
				if (XjSectAuthorityStore.TryGetMember(peak.PeakMasterActorId, out XjSectMemberArchiveRecord master)
					&& master.SectId == sect.SectId
					&& XjSectMemberRole.Normalize(master.Role) == XjSectMemberRole.PeakMaster
					&& master.PeakId == peak.PeakId) continue;
				changed |= XjSectCommands.ClearPeakMaster(sect.SectId, peak.PeakId, snapshot.CurrentYear);
			}
		}

		if (changed) XjSectAuthorityStore.MarkProjectionDirty(sect.SectId);
		return changed;
	}

	private static bool NormalizeStoredPeakNames(XjSectArchiveRecord sect)
	{
		if (sect?.Peaks == null) return false;
		bool changed = false;
		for (int i = 0; i < sect.Peaks.Count; i++)
		{
			XjSectPeakArchiveRecord peak = sect.Peaks[i];
			if (peak == null || peak.PeakId < XjSectPeakIds.FirstRegular) continue;
			string value = string.IsNullOrWhiteSpace(peak.PeakName) ? "无名峰" : peak.PeakName.Trim();
			if (!value.EndsWith("峰", System.StringComparison.Ordinal)) value += "峰";
			string next = value.Length <= 3 ? value : value.Substring(0, 2) + "峰";
			if (string.Equals(peak.PeakName ?? string.Empty, next, System.StringComparison.Ordinal)) continue;
			peak.PeakName = next;
			changed = true;
		}
		return changed;
	}
}
