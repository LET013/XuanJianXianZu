using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.YaoShu;

/// <summary>
/// 大圣化生时唤醒少量对应的原生智慧动物。妖民完整沿用相应 civ_* 模板的
/// 原生城市、家族、宗门与 AI 逻辑，只额外标记为可修炼妖民。
/// </summary>
internal static class XjYaoShuSapientSpecies
{
	internal const string YaoMinTraitId = "XjYaoShuYaoMin";
	private const int SpawnCount = 3;
	private const int ProbeBudget = 12;
	private static readonly Dictionary<string, string> AssetIds = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ "chunhuo", "civ_chicken" }, { "jinghai", "dragon" },
		{ "bailin", "civ_rhino" }, { "baixiang", "civ_buffalo" },
		{ "tingsongli", "civ_cat" }, { "jinchifu", "civ_bee" },
		{ "xuanlei", "civ_crocodile" }, { "yuanzhao", "civ_crocodile" },
		{ "zheqi", "civ_crocodile" }, { "kanshui", "civ_buffalo" },
		{ "taiyin", "civ_cat" }, { "ziqi", "civ_bee" }
	};

	internal static void Init()
	{
		// 不注册、不克隆派生 ActorAsset；大圣化生时直接生成原生 civ_* / dragon。
	}

	internal static bool IsYaoMin(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjYaoShuYaoMin, out int marked)
			&& marked > 0;
	}

	internal static float GetCultivationSpeedMultiplier(Actor actor) => IsYaoMin(actor) ? 0.75f : 1f;

	internal static void TryAwakenForGreatSage(string sageSlotId, WorldTile origin, int currentYear)
	{
		if (origin == null || string.IsNullOrWhiteSpace(sageSlotId)) return;
		if (!AssetIds.TryGetValue(sageSlotId, out string assetId) || AssetManager.actor_library?.get(assetId) == null) return;
		MapBox world = World.world;
		if (world?.units == null) return;
		for (int i = 0; i < SpawnCount; i++)
		{
			if (!TryFindTile(origin, sageSlotId, currentYear, i, out WorldTile tile)) continue;
			Actor actor = null;
			try
			{
				actor = world.units.spawnNewUnit(assetId, tile, false, false, 0f, null, false, true);
				if (actor?.data == null) continue;
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjYaoShuYaoMin, 1);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 0);
				XjExternalUnitTransferContext.EnterTraitTransfer();
				try { if (!actor.hasTrait(YaoMinTraitId)) actor.addTrait(YaoMinTraitId, false); }
				finally { XjExternalUnitTransferContext.ExitTraitTransfer(); }
				actor.clearTraitCache();
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("YaoShuYaoMin.Spawn." + sageSlotId, ex);
				if (actor?.data != null) XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
			}
		}
	}

	private static bool TryFindTile(WorldTile origin, string slot, int year, int ordinal, out WorldTile result)
	{
		result = null;
		long seed = XjDeterministicHash.PositiveHash(year + ordinal * 71L, "yaoshu.yaomin." + slot);
		for (int i = 0; i < ProbeBudget; i++)
		{
			int x = origin.pos.x + XjDeterministicHash.PositiveIndex(seed + i * 17L, "x", 9) - 4;
			int y = origin.pos.y + XjDeterministicHash.PositiveIndex(seed + i * 31L, "y", 9) - 4;
			if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height) continue;
			WorldTile tile = World.world.GetTileSimple(x, y);
			if (tile?.Type != null && !tile.Type.lava && !tile.hasBuilding()) { result = tile; return true; }
		}
		return false;
	}
}
