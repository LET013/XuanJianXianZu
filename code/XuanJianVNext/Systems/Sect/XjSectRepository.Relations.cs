using System;
using XuanJianVNext.Data.Sect;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{
	internal static bool TryResolveFamilySectId(long familyId, out long sectId)
	{
		sectId = 0L;
		if (familyId <= 0L || FamilySeats.Count == 0)
		{
			return false;
		}

		long best = 0L;
		float bestVoice = float.MinValue;
		int bestJoinedYear = int.MinValue;
		foreach (var pair in FamilySeats)
		{
			if (pair.Key.FamilyId != familyId
				|| pair.Key.SectId <= 0L
				|| pair.Value == null
				|| string.Equals(pair.Value.State, XjSectFamilySeatState.Suspended, StringComparison.Ordinal)
				|| !BySectId.TryGetValue(pair.Key.SectId, out XjSectArchiveRecord sect)
				|| !IsEstablishedSect(sect)
				|| !HasValidSectCity(pair.Key.SectId))
			{
				continue;
			}

			float voice = pair.Value.VoiceScore;
			int joinedYear = pair.Value.JoinedYear;
			if (best <= 0L
				|| voice > bestVoice
				|| (Math.Abs(voice - bestVoice) < 0.001f && joinedYear > bestJoinedYear)
				|| (Math.Abs(voice - bestVoice) < 0.001f && joinedYear == bestJoinedYear && pair.Key.SectId < best))
			{
				best = pair.Key.SectId;
				bestVoice = voice;
				bestJoinedYear = joinedYear;
			}
		}

		sectId = best;
		return sectId > 0L;
	}

	internal static bool HasValidSectCity(long sectId)
	{
		return CountValidSectCities(sectId) > 0;
	}

	internal static int CountValidSectCities(long sectId)
	{
		if (sectId <= 0L
			|| !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)
			|| !IsEstablishedSect(record)
			|| record.CityIds == null
			|| record.CityIds.Count == 0)
		{
			return 0;
		}

		int count = 0;
		for (int i = 0; i < record.CityIds.Count; i++)
		{
			long cityId = record.CityIds[i];
			if (cityId <= 0L)
			{
				continue;
			}

			City city = ResolveCityById(cityId);
			if (city?.data != null) count++;
		}
		return count;
	}
}
