using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Sect;

	internal static partial class XjSectGovernanceRuntimeLane
{		private static void ReconcileSovereign(long sectId, int currentYear)
		{
			XjSectRepository.TryReconcileSovereign(sectId, currentYear, recordHistory: true);
		}

		private static Dictionary<long, float> BuildVoiceByFamily(long sectId)
		{
			Dictionary<long, float> result = new Dictionary<long, float>();
			if (CachedSeatsBySect.TryGetValue(sectId, out List<XjSectFamilySeatArchiveRecord> seats))
			{
				for (int i = 0; i < seats.Count; i++)
				{
					XjSectFamilySeatArchiveRecord cached = seats[i];
					if (cached == null || cached.SectId != sectId || cached.FamilyId <= 0L) continue;
					result[cached.FamilyId] = XjSectRepository.TryGetFamilySeat(sectId, cached.FamilyId, out XjSectFamilySeatArchiveRecord liveSeat)
						? liveSeat.VoiceScore
						: cached.VoiceScore;
				}
				return result;
			}
	
			IReadOnlyList<XjSectFamilySeatArchiveRecord> allSeats = XjSectRepository.ReadAllFamilySeats();
			for (int i = 0; i < allSeats.Count; i++)
			{
				XjSectFamilySeatArchiveRecord seat = allSeats[i];
				if (seat == null || seat.SectId != sectId || seat.FamilyId <= 0L) continue;
				result[seat.FamilyId] = seat.VoiceScore;
			}
			return result;
		}
}

