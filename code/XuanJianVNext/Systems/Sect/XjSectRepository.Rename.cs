using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Map;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{
	internal static bool TryRenameSect(long sectId, string nextName, out string message)
	{
		message = string.Empty;
		if (!XjWorldSchemaGuard.GameplayEnabled)
		{
			message = "当前世界未启用玄鉴玩法。";
			return false;
		}

		if (sectId <= 0L || !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record) || record == null)
		{
			message = "未找到宗门。";
			return false;
		}

		string normalized = NormalizeSectName(nextName);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			message = "宗门名不能为空。";
			return false;
		}

		if (normalized.Length > 12)
		{
			normalized = normalized.Substring(0, 12);
		}

		if (string.Equals(record.Name ?? string.Empty, normalized, StringComparison.Ordinal))
		{
			message = "宗名未变。";
			return true;
		}

		record.Name = normalized;
		SyncRenamedSectMirrors(record, normalized);
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Sect
			| XjCodexDirtyFlags.City
			| XjCodexDirtyFlags.Family
			| XjCodexDirtyFlags.Formation
			| XjCodexDirtyFlags.Conflict
			| XjCodexDirtyFlags.History);
		XjSectMapLayerSystem.MarkDirty();
		message = "宗名已更改。";
		return true;
	}

	private static void SyncRenamedSectMirrors(XjSectArchiveRecord record, string sectName)
	{
		if (record == null || record.SectId <= 0L || string.IsNullOrWhiteSpace(sectName))
		{
			return;
		}

		string normalized = sectName.Trim();
		if (record.KingdomId > 0L
			&& XjWorldLookupIndex.TryResolveKingdom(record.KingdomId, out Kingdom kingdom)
			&& kingdom?.data != null)
		{
			XjWorldBoxKingdomBridge.TryRenameKingdom(kingdom, normalized, out _);
		}

		SyncRenamedSectCityMirrors(record, normalized);
		SyncRenamedSectActorMirrors(record, normalized);
		XjAdventureRealmClaimSystem.TryRenameSectReferences(record.SectId, normalized);
	}

	private static void SyncRenamedSectCityMirrors(XjSectArchiveRecord record, string sectName)
	{
		if (record.CityIds == null || record.CityIds.Count == 0)
		{
			return;
		}

		for (int i = 0; i < record.CityIds.Count; i++)
		{
			long cityId = record.CityIds[i];
			if (cityId <= 0L
				|| !XjWorldLookupIndex.TryResolveCity(cityId, out City city)
				|| city?.data == null)
			{
				continue;
			}

			XjZongMenCityData.RebindSectMirror(city, record.SectId, sectName);
		}
	}

	private static void SyncRenamedSectActorMirrors(XjSectArchiveRecord record, string sectName)
	{
		HashSet<long> visited = new HashSet<long>();
		SyncRenamedSectActor(record.FounderActorId, record.SectId, sectName, visited);
		SyncRenamedSectActor(record.SovereignActorId, record.SectId, sectName, visited);
		if (record.Peaks != null)
		{
			for (int i = 0; i < record.Peaks.Count; i++)
			{
				SyncRenamedSectActor(record.Peaks[i]?.PeakMasterActorId ?? 0L, record.SectId, sectName, visited);
			}
		}

		IReadOnlyList<long> indexedActorIds = XjZongMenCultivatorCityIndex.GetActorIdsForSect(record.SectId);
		for (int i = 0; i < indexedActorIds.Count; i++)
		{
			SyncRenamedSectActor(indexedActorIds[i], record.SectId, sectName, visited);
		}

		if (record.CityIds == null || record.CityIds.Count == 0)
		{
			return;
		}

		for (int i = 0; i < record.CityIds.Count; i++)
		{
			long cityId = record.CityIds[i];
			if (cityId <= 0L
				|| !XjWorldLookupIndex.TryResolveCity(cityId, out City city)
				|| city?.data == null
				|| city.units == null)
			{
				continue;
			}

			for (int j = 0; j < city.units.Count; j++)
			{
				Actor actor = city.units[j];
				if (actor?.data == null)
				{
					continue;
				}

				SyncRenamedSectActor(actor, record.SectId, sectName, visited);
			}
		}
	}

	private static void SyncRenamedSectActor(long actorId, long sectId, string sectName, HashSet<long> visited)
	{
		if (actorId <= 0L
			|| !XjScheduler.ResolveActor(actorId, out Actor actor)
			|| actor?.data == null)
		{
			return;
		}

		SyncRenamedSectActor(actor, sectId, sectName, visited);
	}

	private static void SyncRenamedSectActor(Actor actor, long sectId, string sectName, HashSet<long> visited)
	{
		if (actor?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L && !visited.Add(actorId))
		{
			return;
		}

		long actorSectId = ResolveActorSectId(actor);
		if (actorSectId != sectId)
		{
			return;
		}

		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjZongMenId, sectId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenName, sectName);
	}

	private static string NormalizeSectName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string text = value.Trim();
		text = text.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", string.Empty);
		text = text.Replace("/", string.Empty).Replace("\\", string.Empty);
		while (text.Contains("  "))
		{
			text = text.Replace("  ", " ");
		}

		return text;
	}
}
