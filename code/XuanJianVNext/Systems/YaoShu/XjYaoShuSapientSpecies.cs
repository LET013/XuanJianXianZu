using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.YaoShu;

/// <summary>
/// 妖属大圣对应的原生妖民生态。
///
/// 边界：
/// - 不克隆第二套 civ ActorAsset；妖民始终是 WorldBox 原生 civ_* / dragon；
/// - 大圣首次点化只负责投放小型创始种群，后续人口完全走原生 BabyMaker；
/// - 新生儿只继承父母已有的妖民身份，不把同物种的全世界野生动物自动转妖；
/// - 人口通过 XjYaoShuPopulationIndex 增量维护，灭绝后由仍存世的大圣在十年战略脉冲中低频再点化；
/// - 不持有 Actor 引用，不增加全图扫描。
/// </summary>
internal static class XjYaoShuSapientSpecies
{
	internal const string ModuleId = "world.yaoshu-sapient-species";
	internal const string YaoMinTraitId = "XjYaoShuYaoMin";
	private const int FounderWaveSize = 4;
	private const int ProbeBudget = 40;
	private const int ExtinctionReseedCooldownYears = 30;

	private sealed class SpeciesState
	{
		internal bool Awakened;
		internal int FirstAwakenedYear;
		internal int LastAwakenedYear;
		internal int NextReseedYear;
		internal int FounderWaves;
	}

	private static readonly Dictionary<string, string> AssetIds = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ "chunhuo", "civ_chicken" }, { "jinghai", "dragon" },
		{ "bailin", "civ_rhino" }, { "baixiang", "civ_buffalo" },
		{ "tingsongli", "civ_cat" }, { "jinchifu", "civ_bee" },
		{ "xuanlei", "civ_crocodile" }, { "yuanzhao", "civ_crocodile" },
		{ "zheqi", "civ_crocodile" }, { "kanshui", "civ_buffalo" },
		{ "taiyin", "civ_cat" }, { "ziqi", "civ_bee" }
	};

	// 固定顺序只用于小型存档文档，避免 Dictionary 顺序影响 payload。
	private static readonly string[] SpeciesAssetOrder =
	{
		"civ_chicken", "dragon", "civ_rhino", "civ_buffalo", "civ_cat", "civ_bee", "civ_crocodile"
	};

	private static readonly Dictionary<string, SpeciesState> StatesByAssetId = CreateStates();
	private static bool _populationRegistryRecoveryPending = true;

	internal static void Init()
	{
		// 不注册、不克隆派生 ActorAsset；生态只叠加在原生物种与原生生育链上。
	}

	internal static bool IsYaoMin(Actor actor)
	{
		if (actor?.data == null) return false;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjYaoShuYaoMin, out int marked) && marked > 0)
		{
			return true;
		}
		try { return actor.hasTrait(YaoMinTraitId); }
		catch { return false; }
	}

	internal static float GetCultivationSpeedMultiplier(Actor actor) => IsYaoMin(actor) ? 0.75f : 1f;

	internal static bool TryResolveSupportedAssetId(Actor actor, out string assetId)
	{
		string actualAssetId = (actor?.asset?.id ?? actor?.data?.asset_id ?? string.Empty).Trim();
		return TryCanonicalizeNativeAssetId(actualAssetId, out assetId);
	}

	internal static bool TryResolveAssetIdForSlot(string sageSlotId, out string assetId)
	{
		assetId = string.Empty;
		if (string.IsNullOrWhiteSpace(sageSlotId)) return false;
		return AssetIds.TryGetValue(sageSlotId.Trim(), out assetId) && !string.IsNullOrWhiteSpace(assetId);
	}

	internal static int GetPopulationCountForSlot(string sageSlotId)
	{
		EnsurePopulationIndexRecovered();
		return TryResolveAssetIdForSlot(sageSlotId, out string assetId)
			? XjYaoShuPopulationIndex.GetCount(assetId)
			: 0;
	}

	/// <summary>
	/// 载档后人口索引本身不做持久化，而是从玄鉴已经通过本地回调认识的 ActorRegistry
	/// 做一次性恢复。只在载档后执行一次，且不枚举 World.world.units。
	/// </summary>
	internal static void ReconcileAfterLoad(int currentYear)
	{
		_populationRegistryRecoveryPending = true;
		EnsurePopulationIndexRecovered();
	}

	/// <summary>
	/// 大圣首次化生时点化对应原生妖属。若该妖属已有存续妖民，不重复生人；
	/// 只有第一次觉醒或已灭绝且到达再点化年份时才投创始种群。
	/// </summary>
	internal static void TryAwakenForGreatSage(string sageSlotId, WorldTile origin, int currentYear)
	{
		if (!TryResolveAssetIdForSlot(sageSlotId, out string assetId)) return;
		currentYear = Math.Max(0, currentYear);
		EnsurePopulationIndexRecovered();
		SpeciesState state = StatesByAssetId[assetId];
		bool changed = false;
		if (!state.Awakened)
		{
			state.Awakened = true;
			state.FirstAwakenedYear = currentYear;
			state.LastAwakenedYear = currentYear;
			state.NextReseedYear = 0;
			changed = true;
		}
		else if (state.LastAwakenedYear != currentYear)
		{
			state.LastAwakenedYear = currentYear;
			changed = true;
		}

		if (origin != null && XjYaoShuPopulationIndex.GetCount(assetId) <= 0
			&& currentYear >= state.NextReseedYear)
		{
			if (SpawnFounderWave(assetId, sageSlotId, origin, currentYear) > 0)
			{
				state.FounderWaves++;
				state.NextReseedYear = 0;
				changed = true;
			}
		}
		if (changed) MarkChanged();
	}

	/// <summary>
	/// 由大圣既有的十年战略脉冲调用。正常繁衍完全不进入这里；只有人口计数归零后，
	/// 且再点化冷却结束，才允许再次投放一小批创始个体。
	/// </summary>
	internal static void TryMaintainForGreatSage(string sageSlotId, WorldTile origin, int currentYear)
	{
		if (origin == null || !TryResolveAssetIdForSlot(sageSlotId, out string assetId)) return;
		EnsurePopulationIndexRecovered();
		SpeciesState state = StatesByAssetId[assetId];
		if (!state.Awakened)
		{
			TryAwakenForGreatSage(sageSlotId, origin, currentYear);
			return;
		}
		if (XjYaoShuPopulationIndex.GetCount(assetId) > 0) return;
		currentYear = Math.Max(0, currentYear);
		if (currentYear < state.NextReseedYear) return;
		if (SpawnFounderWave(assetId, sageSlotId, origin, currentYear) <= 0) return;
		state.FounderWaves++;
		state.LastAwakenedYear = currentYear;
		state.NextReseedYear = 0;
		MarkChanged();
	}

	/// <summary>
	/// 原生 BabyMaker 成功后继承妖民身份。只有父母之一已经是妖民，且子代仍属于
	/// 七类已支持原生妖属时才转入玄鉴妖民链，避免“某大圣出现后全世界同物种瞬间成妖”。
	/// </summary>
	internal static void ObserveNativeBirth(Actor child, Actor parent1, Actor parent2)
	{
		if (child?.data == null || (!IsYaoMin(parent1) && !IsYaoMin(parent2))) return;
		if (!TryResolveSupportedAssetId(child, out _)) return;
		EnsureYaoMinIdentity(child, resetAptitudeCheck: true);
	}

	internal static void ObserveKnownActor(Actor actor)
	{
		if (actor?.data == null) return;
		XjYaoShuPopulationIndex.Observe(actor);
	}

	internal static void OnActorUnavailable(long actorId)
	{
		XjYaoShuPopulationIndex.Remove(actorId);
	}

	internal static void OnPopulationExtinct(string assetId, int currentYear)
	{
		if (string.IsNullOrWhiteSpace(assetId)
			|| !StatesByAssetId.TryGetValue(assetId.Trim(), out SpeciesState state)
			|| !state.Awakened)
		{
			return;
		}
		currentYear = Math.Max(0, currentYear);
		int next = currentYear + ExtinctionReseedCooldownYears;
		if (state.NextReseedYear >= next) return;
		state.NextReseedYear = next;
		MarkChanged();
	}

	internal static string ExportPayload()
	{
		string[] parts = new string[SpeciesAssetOrder.Length];
		for (int i = 0; i < SpeciesAssetOrder.Length; i++)
		{
			string assetId = SpeciesAssetOrder[i];
			SpeciesState state = StatesByAssetId[assetId];
			parts[i] = assetId + ","
				+ (state.Awakened ? "1" : "0") + ","
				+ state.FirstAwakenedYear.ToString(CultureInfo.InvariantCulture) + ","
				+ state.LastAwakenedYear.ToString(CultureInfo.InvariantCulture) + ","
				+ state.NextReseedYear.ToString(CultureInfo.InvariantCulture) + ","
				+ state.FounderWaves.ToString(CultureInfo.InvariantCulture);
		}
		return string.Join(";", parts);
	}

	internal static void ImportPayload(int schemaVersion, string payload)
	{
		ClearRuntime();
		if (schemaVersion <= 0 || string.IsNullOrWhiteSpace(payload)) return;
		string[] parts = payload.Split(';');
		for (int i = 0; i < parts.Length; i++)
		{
			string[] fields = (parts[i] ?? string.Empty).Split(',');
			if (fields.Length == 0) continue;
			string assetId = (fields[0] ?? string.Empty).Trim();
			if (!StatesByAssetId.TryGetValue(assetId, out SpeciesState state)) continue;
			if (fields.Length > 1) state.Awakened = fields[1] == "1";
			if (fields.Length > 2 && int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int first))
				state.FirstAwakenedYear = Math.Max(0, first);
			if (fields.Length > 3 && int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int last))
				state.LastAwakenedYear = Math.Max(0, last);
			if (fields.Length > 4 && int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int next))
				state.NextReseedYear = Math.Max(0, next);
			if (fields.Length > 5 && int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int waves))
				state.FounderWaves = Math.Max(0, waves);
		}
	}

	internal static void ClearRuntime()
	{
		for (int i = 0; i < SpeciesAssetOrder.Length; i++)
		{
			SpeciesState state = StatesByAssetId[SpeciesAssetOrder[i]];
			state.Awakened = false;
			state.FirstAwakenedYear = 0;
			state.LastAwakenedYear = 0;
			state.NextReseedYear = 0;
			state.FounderWaves = 0;
		}
		XjYaoShuPopulationIndex.ResetForNewWorld();
		_populationRegistryRecoveryPending = true;
	}

	private static Dictionary<string, SpeciesState> CreateStates()
	{
		Dictionary<string, SpeciesState> result = new Dictionary<string, SpeciesState>(StringComparer.Ordinal);
		for (int i = 0; i < SpeciesAssetOrder.Length; i++) result[SpeciesAssetOrder[i]] = new SpeciesState();
		return result;
	}

	private static int SpawnFounderWave(string assetId, string sageSlotId, WorldTile origin, int currentYear)
	{
		if (origin == null || string.IsNullOrWhiteSpace(assetId)
			|| !TryResolveNativeActorAssetId(assetId, out string spawnAssetId)) return 0;
		MapBox world = World.world;
		if (world?.units == null) return 0;
		int spawnedCount = 0;
		for (int i = 0; i < FounderWaveSize; i++)
		{
			if (!TryFindTile(origin, sageSlotId, assetId, currentYear, i, out WorldTile tile)) continue;
			Actor actor = null;
			try
			{
				actor = world.units.spawnNewUnit(spawnAssetId, tile, false, false, 0f, null, false, true);
				if (actor?.data == null) continue;
				EnsureYaoMinIdentity(actor, resetAptitudeCheck: true);
				spawnedCount++;
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("YaoShuYaoMin.Founder." + sageSlotId, ex);
				if (actor?.data != null) XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
			}
		}
		return spawnedCount;
	}

	private static void EnsureYaoMinIdentity(Actor actor, bool resetAptitudeCheck)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjYaoShuYaoMin, 1);
		if (resetAptitudeCheck) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 0);
		XjExternalUnitTransferContext.EnterTraitTransfer();
		try
		{
			if (AssetManager.traits?.get(YaoMinTraitId) != null && !actor.hasTrait(YaoMinTraitId))
			{
				actor.addTrait(YaoMinTraitId, false);
			}
		}
		finally
		{
			XjExternalUnitTransferContext.ExitTraitTransfer();
		}
		actor.clearTraitCache();
		XjActorRegistry.Register(actor, out _);
		XjYaoShuPopulationIndex.Observe(actor);
		actor.setStatsDirty();
	}

	private static void EnsurePopulationIndexRecovered()
	{
		if (!_populationRegistryRecoveryPending || XjActorRegistry.Count <= 0) return;
		_populationRegistryRecoveryPending = false;
		IReadOnlyList<Actor> known = XjActorRegistry.Snapshot();
		for (int i = 0; i < known.Count; i++)
		{
			XjYaoShuPopulationIndex.Observe(known[i]);
		}
	}

	private static bool TryCanonicalizeNativeAssetId(string actualAssetId, out string canonicalAssetId)
	{
		canonicalAssetId = string.Empty;
		if (string.IsNullOrWhiteSpace(actualAssetId)) return false;
		actualAssetId = actualAssetId.Trim();
		if (StatesByAssetId.ContainsKey(actualAssetId))
		{
			canonicalAssetId = actualAssetId;
			return true;
		}
		// 部分 WorldBox 动物没有 civ_* 变体（例如不同版本中的 bee）。
		// 生态存档仍使用稳定 canonical id，运行时只把原生 plain asset 映射回来。
		if (!actualAssetId.StartsWith("civ_", StringComparison.Ordinal))
		{
			string civCandidate = "civ_" + actualAssetId;
			if (StatesByAssetId.ContainsKey(civCandidate))
			{
				canonicalAssetId = civCandidate;
				return true;
			}
		}
		return false;
	}

	private static bool TryResolveNativeActorAssetId(string canonicalAssetId, out string actualAssetId)
	{
		actualAssetId = string.Empty;
		if (string.IsNullOrWhiteSpace(canonicalAssetId) || AssetManager.actor_library == null) return false;
		canonicalAssetId = canonicalAssetId.Trim();
		if (AssetManager.actor_library.get(canonicalAssetId) != null)
		{
			actualAssetId = canonicalAssetId;
			return true;
		}
		if (canonicalAssetId.StartsWith("civ_", StringComparison.Ordinal))
		{
			string plain = canonicalAssetId.Substring(4);
			if (AssetManager.actor_library.get(plain) != null)
			{
				actualAssetId = plain;
				return true;
			}
		}
		return false;
	}

	private static bool TryFindTile(
		WorldTile origin,
		string slot,
		string assetId,
		int year,
		int ordinal,
		out WorldTile result)
	{
		result = null;
		if (origin == null || World.world == null) return false;
		bool preferWater = string.Equals(assetId, "dragon", StringComparison.Ordinal);
		long seed = XjDeterministicHash.PositiveHash(year + ordinal * 71L, "yaoshu.yaomin." + slot + "." + assetId);
		for (int i = 0; i < ProbeBudget; i++)
		{
			int radius = 4 + i / 10;
			int span = radius * 2 + 1;
			int x = origin.pos.x + XjDeterministicHash.PositiveIndex(seed + i * 17L, "x", span) - radius;
			int y = origin.pos.y + XjDeterministicHash.PositiveIndex(seed + i * 31L, "y", span) - radius;
			if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height) continue;
			WorldTile tile = World.world.GetTileSimple(x, y);
			if (!IsSuitableFounderTile(tile, preferWater)) continue;
			result = tile;
			return true;
		}

		// 地图极端地形下宁可放宽一次水陆偏好，也不为寻找出生点扫描全图。
		for (int i = 0; i < ProbeBudget / 2; i++)
		{
			int radius = 6 + i / 8;
			int span = radius * 2 + 1;
			int x = origin.pos.x + XjDeterministicHash.PositiveIndex(seed + 4001L + i * 23L, "fx", span) - radius;
			int y = origin.pos.y + XjDeterministicHash.PositiveIndex(seed + 5003L + i * 29L, "fy", span) - radius;
			if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height) continue;
			WorldTile tile = World.world.GetTileSimple(x, y);
			if (tile?.Type == null || tile.Type.lava || tile.hasBuilding()) continue;
			result = tile;
			return true;
		}
		return false;
	}

	private static bool IsSuitableFounderTile(WorldTile tile, bool preferWater)
	{
		if (tile?.Type == null || tile.Type.lava || tile.hasBuilding()) return false;
		bool water = tile.Type.ocean || tile.Type.liquid;
		if (!water)
		{
			string id = tile.Type.id ?? string.Empty;
			water = id.IndexOf("ocean", StringComparison.OrdinalIgnoreCase) >= 0
				|| id.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0
				|| id.IndexOf("sea", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return preferWater ? water : !water;
	}

	private static void MarkChanged()
	{
		XjWorldArchiveSystem.MarkModuleChanged(ModuleId);
	}
}
