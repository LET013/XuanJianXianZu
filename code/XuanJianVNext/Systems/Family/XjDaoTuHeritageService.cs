using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Family;

internal readonly struct XjDaoTuTraditionSummary
{
	internal readonly bool Found;
	internal readonly string DaoTu;
	internal readonly int InfluencePercent;
	internal readonly int HighestRealmOrder;
	internal readonly int SourceCount;
	internal readonly int DominantScore;

	internal XjDaoTuTraditionSummary(
		bool found,
		string daoTu,
		int influencePercent,
		int highestRealmOrder,
		int sourceCount,
		int dominantScore)
	{
		Found = found;
		DaoTu = daoTu ?? string.Empty;
		InfluencePercent = Math.Max(0, Math.Min(100, influencePercent));
		HighestRealmOrder = Math.Max(0, highestRealmOrder);
		SourceCount = Math.Max(0, sourceCount);
		DominantScore = Math.Max(0, dominantScore);
	}
}

internal readonly struct XjDaoTuControlSummary
{
	internal readonly int HeldPositions;
	internal readonly int ActivePositions;
	internal readonly int TotalCapacity;
	internal readonly bool HoldsFruit;
	internal readonly bool IsMonopoly;
	internal readonly string Display;

	internal XjDaoTuControlSummary(
		int heldPositions,
		int activePositions,
		int totalCapacity,
		bool holdsFruit,
		bool isMonopoly,
		string display)
	{
		HeldPositions = Math.Max(0, heldPositions);
		ActivePositions = Math.Max(0, activePositions);
		TotalCapacity = Math.Max(1, totalCapacity);
		HoldsFruit = holdsFruit;
		IsMonopoly = isMonopoly;
		Display = display ?? string.Empty;
	}
}

/// <summary>
/// 家族与宗门的主传承道途唯一解析器。高境层级、同道累积与位序掌握均从真实账本读取；
/// 后代定路与仙鉴展示共用同一结论，避免规则和文案各维护一套。
/// </summary>
internal static class XjDaoTuHeritageService
{
	private sealed class ControlCacheEntry
	{
		internal int RegistryRevision;
		internal int EntityRevision;
		internal string DaoTu = string.Empty;
		internal XjDaoTuControlSummary Summary;
	}

	private const int MaxFamilyControlCacheEntries = 2048;
	private const int MaxSectControlCacheEntries = 1024;

	private static int _cachedRegistryRevision = int.MinValue;
	private static IReadOnlyList<XjGuoWeiRegistryEntry> _cachedActiveEntries = Array.Empty<XjGuoWeiRegistryEntry>();
	private static Dictionary<long, ControlCacheEntry> FamilyControlCache = new Dictionary<long, ControlCacheEntry>();
	private static Dictionary<long, ControlCacheEntry> SectControlCache = new Dictionary<long, ControlCacheEntry>();

	internal static int RuntimeControlCacheCount => FamilyControlCache.Count + SectControlCache.Count;

	internal static void Clear()
	{
		_cachedRegistryRevision = int.MinValue;
		_cachedActiveEntries = Array.Empty<XjGuoWeiRegistryEntry>();
		FamilyControlCache.Clear();
		SectControlCache.Clear();
	}

	internal static void ReleaseRuntimeCacheStorage()
	{
		_cachedRegistryRevision = int.MinValue;
		_cachedActiveEntries = Array.Empty<XjGuoWeiRegistryEntry>();
		FamilyControlCache = new Dictionary<long, ControlCacheEntry>();
		SectControlCache = new Dictionary<long, ControlCacheEntry>();
	}

	private static void EnsureControlCacheCapacity(
		Dictionary<long, ControlCacheEntry> cache,
		long entityId,
		int maximumEntries)
	{
		if (cache == null || entityId <= 0L || maximumEntries <= 0) return;
		if (cache.Count >= maximumEntries && !cache.ContainsKey(entityId)) cache.Clear();
	}

	internal static bool TryResolveFamilyTradition(
		long familyId,
		long excludedActorId,
		out XjDaoTuTraditionSummary summary)
	{
		summary = default;
		if (familyId <= 0L) return false;
		return TryResolveTradition(
			XjFamilyReadModel.Shared.GetFamilyMembers(familyId),
			familyId,
			excludedActorId,
			out summary);
	}

	internal static bool TryResolveTradition(
		IEnumerable<Actor> actors,
		long stableSeed,
		long excludedActorId,
		out XjDaoTuTraditionSummary summary)
	{
		summary = default;
		if (actors == null) return false;
		int ziFuOrder = XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
		int highestOrder = 0;
		int influencePercent = 0;
		Dictionary<string, int> scores = new Dictionary<string, int>(StringComparer.Ordinal);
		Dictionary<string, int> sourceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (Actor actor in actors)
		{
			if (actor?.data == null || !actor.isAlive()) continue;
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L || actorId == excludedActorId) continue;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			int order = XjRealmHelper.GetOrder(realmId);
			if (order < ziFuOrder || !TryReadDaoTu(actor, out string daoTu)) continue;

			if (order > highestOrder)
			{
				highestOrder = order;
				influencePercent = ResolveInfluence(realmId);
				scores.Clear();
				sourceCounts.Clear();
			}
			if (order != highestOrder) continue;
			scores.TryGetValue(daoTu, out int score);
			scores[daoTu] = score + ResolveWeight(realmId);
			sourceCounts.TryGetValue(daoTu, out int count);
			sourceCounts[daoTu] = count + 1;
		}
		if (scores.Count == 0) return false;

		int bestScore = int.MinValue;
		List<string> best = new List<string>();
		foreach (KeyValuePair<string, int> pair in scores)
		{
			if (pair.Value > bestScore)
			{
				bestScore = pair.Value;
				best.Clear();
				best.Add(pair.Key);
			}
			else if (pair.Value == bestScore)
			{
				best.Add(pair.Key);
			}
		}
		best.Sort(StringComparer.Ordinal);
		string selected = best[XjDeterministicHash.PositiveIndex(
			stableSeed, "family_high_realm_tradition_v1", best.Count)];
		sourceCounts.TryGetValue(selected, out int sources);
		summary = new XjDaoTuTraditionSummary(
			true, selected, influencePercent, highestOrder, sources, bestScore);
		return true;
	}

	internal static XjDaoTuControlSummary ResolveFamilyControl(long familyId, string daoTu)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		int registryRevision = XjGuoWeiRegistry.Revision;
		int entityRevision = XjRelationEntityRevisionStore.GetFamilyRevision(familyId);
		if (familyId > 0L
			&& FamilyControlCache.TryGetValue(familyId, out ControlCacheEntry cached)
			&& cached.RegistryRevision == registryRevision
			&& cached.EntityRevision == entityRevision
			&& string.Equals(cached.DaoTu, normalized, StringComparison.Ordinal)) return cached.Summary;
		XjDaoTuControlSummary summary = ResolveControl(normalized, actor =>
		{
			if (actor?.data == null || familyId <= 0L) return false;
			long actorId = ((BaseSystemData)actor.data).id;
			return XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity)
				&& identity.FamilyStableIdValue == familyId;
		});
		if (familyId > 0L)
		{
			EnsureControlCacheCapacity(FamilyControlCache, familyId, MaxFamilyControlCacheEntries);
			FamilyControlCache[familyId] = new ControlCacheEntry
			{
				RegistryRevision = registryRevision,
				EntityRevision = entityRevision,
				DaoTu = normalized,
				Summary = summary
			};
		}
		return summary;
	}

	internal static XjDaoTuControlSummary ResolveSectControl(long sectId, string daoTu)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		int registryRevision = XjGuoWeiRegistry.Revision;
		int entityRevision = XjRelationEntityRevisionStore.GetSectRevision(sectId);
		if (sectId > 0L
			&& SectControlCache.TryGetValue(sectId, out ControlCacheEntry cached)
			&& cached.RegistryRevision == registryRevision
			&& cached.EntityRevision == entityRevision
			&& string.Equals(cached.DaoTu, normalized, StringComparison.Ordinal)) return cached.Summary;
		XjDaoTuControlSummary summary = ResolveControl(normalized, actor => actor?.data != null
			&& sectId > 0L
			&& XjSectRepository.ResolveActorSectId(actor) == sectId);
		if (sectId > 0L)
		{
			EnsureControlCacheCapacity(SectControlCache, sectId, MaxSectControlCacheEntries);
			SectControlCache[sectId] = new ControlCacheEntry
			{
				RegistryRevision = registryRevision,
				EntityRevision = entityRevision,
				DaoTu = normalized,
				Summary = summary
			};
		}
		return summary;
	}

	private static XjDaoTuControlSummary ResolveControl(string daoTu, Func<Actor, bool> belongs)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		if (normalized.Length == 0 || belongs == null)
		{
			return new XjDaoTuControlSummary(0, 0, 1, false, false, "尚未形成位序掌握");
		}

		XjFruitPositionCapacity capacity = XjFruitPositionWorldState.GetCapacity(normalized);
		int totalCapacity = 1 + capacity.Residual + capacity.Intercalary;
		int active = 0;
		int held = 0;
		bool holdsFruit = false;
		IReadOnlyList<XjGuoWeiRegistryEntry> entries = ReadActivePositionEntries();
		for (int i = 0; i < entries.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = entries[i];
			if (!entry.Found || !entry.IsActive
				|| !string.Equals(entry.DaoTu, normalized, StringComparison.Ordinal)) continue;
			active++;
			if (!XjScheduler.ResolveActor(entry.ActorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !belongs(actor)) continue;
			held++;
			if (string.Equals(
				XjGuoWeiRegistry.ResolveTypeFromName(entry.GuoWei),
				XjGuoWeiCalculator.ZhengWei,
				StringComparison.Ordinal))
			{
				holdsFruit = true;
			}
		}

		bool monopoly = holdsFruit && active > 0 && held == active;
		bool completeMonopoly = monopoly && active == totalCapacity;
		string display;
		if (completeMonopoly)
		{
			display = "圆满垄断 · 当前可开的果余闰全部归属本脉";
		}
		else if (monopoly)
		{
			display = "当世垄断 · 全部在位席次归本脉（本脉持位" + held + " · 当世在位" + active + "）";
		}
		else if (holdsFruit)
		{
			display = "掌握果位 · 本脉持位" + held + " · 当世在位" + Math.Max(1, active);
		}
		else if (held > 0 && held * 2 > Math.Max(1, active))
		{
			display = "主导本道 · 本脉持位" + held + " · 当世在位" + active;
		}
		else if (held > 0)
		{
			display = "涉足本道 · 本脉持位" + held + " · 当世在位" + active;
		}
		else
		{
			display = "尚未持有本道位序 · 当世在位" + active;
		}
		return new XjDaoTuControlSummary(held, active, totalCapacity, holdsFruit, monopoly, display);
	}

	private static IReadOnlyList<XjGuoWeiRegistryEntry> ReadActivePositionEntries()
	{
		int revision = XjGuoWeiRegistry.Revision;
		if (_cachedRegistryRevision == revision) return _cachedActiveEntries;
		_cachedActiveEntries = XjGuoWeiRegistry.ReadActiveEntries();
		_cachedRegistryRevision = revision;
		return _cachedActiveEntries;
	}

	private static bool TryReadDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null) return false;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out daoTu)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out daoTu);
		}
		daoTu = (daoTu ?? string.Empty).Trim();
		return daoTu.Length > 0 && XjDaoTuVisibleTraitCatalog.TryResolveTraitId(daoTu, out _);
	}

	private static int ResolveInfluence(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return 100;
		if (string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return 97;
		return 90;
	}

	private static int ResolveWeight(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return 12;
		if (string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return 6;
		return 3;
	}
}
