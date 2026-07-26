using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{
	internal const int MaxSectPeaks = 13;
	internal const int MaxSectCityCount = 9;

	private static readonly string[] PeakNames =
	{
		"朝真峰", "栖霞峰", "鸣玉峰", "观澜峰", "承露峰", "玄霜峰", "照影峰",
		"藏锋峰", "青岚峰", "云台峰", "问道峰", "含章峰", "镇岳峰"
	};

	private static readonly Dictionary<long, XjSectArchiveRecord> BySectId = new Dictionary<long, XjSectArchiveRecord>();
	private static readonly Dictionary<long, XjCityFamilyGovernanceArchiveRecord> GovernanceByCityId = new Dictionary<long, XjCityFamilyGovernanceArchiveRecord>();
	private static readonly Dictionary<(long SectId, long FamilyId), XjSectFamilySeatArchiveRecord> FamilySeats = new Dictionary<(long SectId, long FamilyId), XjSectFamilySeatArchiveRecord>();

	internal static int Count => BySectId.Count;
	internal static bool TryGetBySectId(long sectId, out XjSectArchiveRecord record) => BySectId.TryGetValue(sectId, out record);

	internal static long ResolveActorSectId(Actor actor)
	{
		return TryGetByActor(actor, out XjSectArchiveRecord record) && record?.SectId > 0L ? record.SectId : 0L;
	}

	internal static bool TryGetByActor(Actor actor, out XjSectArchiveRecord record)
	{
		record = null;
		if (actor?.data == null) return false;
		long actorSectId = TryReadActorSectIdForResolve(actor);
		if (actorSectId > 0L && BySectId.TryGetValue(actorSectId, out record) && IsEstablishedSect(record)) return true;
		if (TryGetByCity(actor.city, out record)) return true;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L
			&& XjZongMenCultivatorCityIndex.TryGetSectId(actorId, out long indexedSectId)
			&& BySectId.TryGetValue(indexedSectId, out record)
			&& IsEstablishedSect(record))
		{
			return true;
		}
		return false;
	}

	internal static bool TryGetByCity(City city, out XjSectArchiveRecord record)
	{
		record = null;
		if (city?.data == null) return false;

		long citySectId = XjZongMenCityData.GetZongMenId(city);
		if (citySectId > 0L && BySectId.TryGetValue(citySectId, out record))
		{
			if (IsEstablishedSect(record)) return true;
			record = null;
			return false;
		}

		long cityId = city.data.id;
		if (cityId > 0L
			&& GovernanceByCityId.TryGetValue(cityId, out XjCityFamilyGovernanceArchiveRecord governance)
			&& governance?.SectId > 0L
			&& BySectId.TryGetValue(governance.SectId, out record))
		{
			if (IsEstablishedSect(record)) return true;
			record = null;
			return false;
		}

		record = null;
		return false;
	}

	private static bool IsEstablishedSect(XjSectArchiveRecord record)
	{
		if (record == null || record.SectId <= 0L) return false;
		if (string.Equals(record.Status, XjSectStatus.Extinct, StringComparison.Ordinal)) return false;
		return true;
	}

	internal static bool TryGetGovernance(long cityId, out XjCityFamilyGovernanceArchiveRecord record)
	{
		record = null;
		return cityId > 0L && GovernanceByCityId.TryGetValue(cityId, out record);
	}

	internal static bool TryGetFamilySeat(long sectId, long familyId, out XjSectFamilySeatArchiveRecord record)
	{
		record = null;
		return sectId > 0L && familyId > 0L && FamilySeats.TryGetValue((sectId, familyId), out record);
	}

	private static long TryReadActorSectIdForResolve(Actor actor)
	{
		if (actor?.data == null) return 0L;
		if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenId, out long sectId) && sectId > 0L) return sectId;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenId, out int legacy) && legacy > 0) return legacy;
		return 0L;
	}

}










