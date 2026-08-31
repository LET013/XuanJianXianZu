using System;
using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	/// <summary>
	/// 原生“不灭岩浆”法则 world_law_forever_lava 的实际效果，是在
	/// WorldBehaviourActionLava.tryToMoveLava 入口直接返回，不再让岩浆冷却、
	/// 降级或继续流动。玩家若已开启该法则，完全交给原生逻辑；玩家未开启时，
	/// 仅对旃檀林自身的 lava3 环执行同样的原生停滞语义，不改变世界其他岩浆。
	/// </summary>
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(WorldBehaviourActionLava), "tryToMoveLava", new Type[] { typeof(WorldTile) })]
	private static bool XuanJianVNext_WorldBehaviourActionLava_TryToMoveLava_ZhantanlinForeverLava_Prefix(
		WorldTile __0, ref bool __result)
	{
		// 世界法则本身已经开启时不重复拦截，让原生代码自行在同一入口返回。
		if (WorldLawLibrary.world_law_forever_lava != null
			&& WorldLawLibrary.world_law_forever_lava.isEnabled()) return true;
		if (!XjZhantanlinSystem.IsPermanentLavaBoundaryTile(__0)) return true;
		__result = false;
		return false;
	}

	/// <summary>
	/// 旃檀林不允许原生群系自动生成动物。入口放在生成前，避免先造实体、
	/// 再依赖年度净界销毁所带来的短时AI与渲染负担。
	/// </summary>
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(ActorManager), "spawnNewUnit", new Type[]
	{
		typeof(string), typeof(WorldTile), typeof(bool), typeof(bool), typeof(float),
		typeof(Subspecies), typeof(bool), typeof(bool)
	})]
	private static bool XuanJianVNext_ActorManager_SpawnNewUnit_ZhantanlinAnimalGuard_Prefix(
		string __0,
		WorldTile __1,
		ref Actor __result)
	{
		// AVBS的复制/变形属于明确的模组主动生成，不是旃檀林群系自动刷动物。
		// 必须放行，否则AVBS可能拿到null新单位后继续执行ActorTool.copyUnitToOtherUnit。
		if (XjExternalUnitTransferContext.IsExplicitExternalGeneration) return true;
		if (!XjZhantanlinSystem.ShouldBlockAnimalSpawn(__0, __1)) return true;
		__result = null;
		return false;
	}

	/// <summary>
	/// 旃檀林不属于任何原生国家。所有城市扩张最终都必须经过 City.addZone，
	/// 在写入前拒绝与佛土相交的 TileZone；旧档已有占领由旃檀林年度自愈剥离。
	/// </summary>
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(City), "addZone", new Type[] { typeof(TileZone) })]
	private static bool XuanJianVNext_City_AddZone_ZhantanlinTerritoryGuard_Prefix(TileZone __0)
	{
		return !XjZhantanlinSystem.ShouldBlockTerritoryClaim(__0);
	}

	/// <summary>阻止任何原生建筑在旃檀林/三重实体封界内生成；旧存量在放置/读档同步时一次性清除。</summary>
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BuildingManager), "addBuilding", new Type[]
	{
		typeof(string), typeof(WorldTile), typeof(bool), typeof(bool), typeof(BuildPlacingType)
	})]
	private static bool XuanJianVNext_BuildingManager_AddBuilding_ZhantanlinDomainGuard_Prefix(
		string __0,
		WorldTile __1,
		ref Building __result)
	{
		if (!XjZhantanlinSystem.ShouldBlockBuildingSpawn(__0, __1)) return true;
		__result = null;
		return false;
	}
}
