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
		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjSectAuthorityStore.TryGetSectId(actorId, out long sectId)
			&& BySectId.TryGetValue(sectId, out record)
			&& IsEstablishedSect(record);
	}

	internal static bool TryGetByCity(City city, out XjSectArchiveRecord record)
	{
		record = null;
		long cityId = city?.data?.id ?? 0L;
		return cityId > 0L
			&& XjSectAuthorityStore.TryGetSectIdByCity(cityId, out long sectId)
			&& BySectId.TryGetValue(sectId, out record)
			&& IsEstablishedSect(record);
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

}










