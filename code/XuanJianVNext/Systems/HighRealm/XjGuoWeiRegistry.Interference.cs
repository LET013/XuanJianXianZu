using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjGuoWeiRegistry
{	
		internal static bool TryResolveZhengWeiSchemerInterference(
			string daoTu,
			string guoWeiType,
			long actorId,
			out XjGuoWeiRegistryEntry schemer,
			out float successMultiplier)
		{
			schemer = default;
			successMultiplier = 1f;
			string normalizedDaoTu = Normalize(daoTu);
			string normalizedType = Normalize(guoWeiType);
			if (string.IsNullOrWhiteSpace(normalizedDaoTu)
				|| actorId <= 0L
				|| (!string.Equals(normalizedType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
					&& !string.Equals(normalizedType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
				|| activeEntriesByGuoWei.Count == 0)
			{
				return false;
			}
	
			foreach (XjGuoWeiRegistryEntry entry in activeEntriesByGuoWei.Values)
			{
				if (!entry.Found
					|| !entry.IsActive
					|| entry.ActorId <= 0L
					|| entry.ActorId == actorId
					|| !string.Equals(entry.DaoTu, normalizedDaoTu, StringComparison.Ordinal)
					|| !string.Equals(ResolveTypeFromName(entry.GuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
					|| !XjScheduler.ResolveActor(entry.ActorId, out Actor zhengWeiActor)
					|| !HasSchemerTrait(zhengWeiActor))
				{
					continue;
				}
	
				long seed = actorId ^ (entry.ActorId * 1103515245L);
				string salt = "guowei_zhengwei_schemer|" + normalizedDaoTu + "|" + normalizedType;
				if (XjDeterministicHash.PositiveIndex(seed, salt, 100) >= (int)(ZhengWeiSchemerInterferenceChance * 100f))
				{
					continue;
				}
	
				schemer = entry;
				successMultiplier = ZhengWeiSchemerSuppressionMultiplier;
				return true;
			}
	
			return false;
		}

	
		private static bool HasSchemerTrait(Actor actor)
		{
			if (actor?.data == null)
			{
				return false;
			}
	
			for (int i = 0; i < ZhengWeiSchemerTraitIds.Length; i++)
			{
				if (actor.hasTrait(ZhengWeiSchemerTraitIds[i]))
				{
					return true;
				}
			}
			return false;
		}
}

