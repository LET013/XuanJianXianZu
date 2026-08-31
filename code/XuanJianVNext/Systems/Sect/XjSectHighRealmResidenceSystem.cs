namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 高境宗门成员不再被强制迁回山门；这里只保留阵法工程需要的山门位置判断。
/// </summary>
internal static class XjSectHighRealmResidenceSystem
{
	internal static bool IsAtSectGate(Actor actor, long sectId)
	{
		if (actor?.data == null || sectId <= 0L
			|| !XjSectOwnership.TryResolvePrimaryCity(sectId, out City homeCity)) return false;
		City currentCity = actor.city;
		return currentCity?.data != null && homeCity?.data != null
			&& (ReferenceEquals(currentCity, homeCity) || currentCity.data.id == homeCity.data.id);
	}
}
