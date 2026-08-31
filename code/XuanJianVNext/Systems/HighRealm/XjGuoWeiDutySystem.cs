using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 果位持有者的“职责”不另造日常任务条，而落在真实发生的护道、守传承等高位行为上。
/// 本版只给宗门传承补缺提供低频查询入口；后续若接入争位、权柄、洞天/法界事件，
/// 也必须由对应事件真实触发，不做逐年全世界扫描。
/// </summary>
internal static class XjGuoWeiDutySystem
{
	internal static bool TryResolveSectFruitHolder(long sectId, string daoTu, out Actor holder)
	{
		holder = null;
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (sectId <= 0L || normalizedDaoTu.Length == 0) return false;

		IReadOnlyList<XjGuoWeiRegistryEntry> entries = XjGuoWeiRegistry.ReadActiveEntries();
		for (int i = 0; i < entries.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = entries[i];
			if (!entry.Found || !entry.IsActive
				|| !string.Equals(entry.DaoTu, normalizedDaoTu, StringComparison.Ordinal)
				|| !string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(entry.GuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
				|| entry.ActorId <= 0L
				|| !XjScheduler.ResolveActor(entry.ActorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive()
				|| XjSectRepository.ResolveActorSectId(actor) != sectId)
			{
				continue;
			}

			holder = actor;
			return true;
		}
		return false;
	}
}
