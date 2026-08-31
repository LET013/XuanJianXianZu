using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Formation;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{
	internal static bool RefreshFormationPower(long sectId)
	{
		if (sectId <= 0L || !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)) return false;
		return RefreshFormationPower(record);
	}

	internal static int ResolveSectRealmRank(long sectId)
	{
		CountSectHighRealms(sectId, out int ziFuOnlyCount, out int jinDanCount);
		if (jinDanCount > 0) return XjRealmSuppression.TierJinDan;
		if (ziFuOnlyCount > 0) return XjRealmSuppression.TierZiFu;
		return XjRealmSuppression.TierNone;
	}

	private static bool RefreshFormationPower(XjSectArchiveRecord record)
	{
		if (!IsEstablishedSect(record)) return false;
		CountSectHighRealms(record.SectId, out int ziFuOnlyCount, out int jinDanCount);
		return XjSectFormationRegistry.RescaleDurabilityForSectPower(
			record.SectId, ziFuOnlyCount, jinDanCount, Math.Clamp(record.ProsperityValue, 0, 100));
	}

	private static void CountSectHighRealms(long sectId, out int ziFuOnlyCount, out int jinDanCount)
	{
		ziFuOnlyCount = 0;
		jinDanCount = 0;
		if (sectId <= 0L) return;
		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sectId);
		if (actorIds == null || actorIds.Count == 0) return;
		for (int i = 0; i < actorIds.Count; i++)
		{
			long actorId = actorIds[i];
			if (actorId <= 0L || !XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)) continue;
			if (realmTier >= XjRealmSuppression.TierJinDan) jinDanCount++;
			else if (realmTier >= XjRealmSuppression.TierZiFu) ziFuOnlyCount++;
		}
	}
}
