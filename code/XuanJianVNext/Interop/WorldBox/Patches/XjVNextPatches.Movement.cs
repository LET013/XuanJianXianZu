using System;
using HarmonyLib;
using ai;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "goTo")]
	public static bool XuanJianVNext_Actor_GoTo_HighRealmMovement_Prefix(
		Actor __instance,
		WorldTile pTile,
		ref ExecuteEvent __result)
	{
		// 已经进入异常退避的角色最先退出，避免失效端点仍在每次 AI tick
		// 重复执行恢复/取消行为。只有退避结束后才重新验证路径端点。
		if (XjHighRealmMovement.TryBlockNativePathDuringBackoff(__instance, ref __result))
		{
			return false;
		}
		if (XjHighRealmMovement.TryBlockInvalidNativePathEndpoint(__instance, pTile, ref __result))
		{
			return false;
		}
		if (XjZhantanlinSystem.IsPlaced
			&& XjZhantanlinSystem.TryBlockTravel(__instance, pTile, ref __result))
		{
			return false;
		}
		long actorId = __instance?.data == null ? 0L : ((BaseSystemData)__instance.data).id;
		if (actorId > 0L
			&& !XjWorldBootstrapLane.HasPending
			&& !XjRuntimeActorInterestIndex.HasMovementInterest(actorId))
		{
			return true;
		}

		return !XjHighRealmMovement.TryHandleGoTo(__instance, pTile, ref __result);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "setTileTarget")]
	public static bool XuanJianVNext_Actor_SetTileTarget_SanctuaryIsolation_Prefix(Actor __instance, WorldTile __0)
	{
		// 从目标写入阶段阻止外界普通AI把旃檀林/光墙保存为长期tileTarget。
		// 这样不是“每次goTo都ClearPath”，而是根本不产生可重复的跨域寻路任务。
		return !XjZhantanlinSystem.IsPlaced
			|| !XjZhantanlinSystem.ShouldRejectExternalTileTarget(__instance, __0);
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(Actor), "goTo")]
	public static Exception XuanJianVNext_Actor_GoTo_RegionPathSafety_Finalizer(
		Actor __instance,
		WorldTile __0,
		ref ExecuteEvent __result,
		Exception __exception)
	{
		if (__exception is not NullReferenceException) return __exception;

		// 用户日志明确落在 RegionPathFinder.finalPath。根因层已经通过 spawnOn
		// 领域迁移、目标写入门和墙修复后的旧路径失效处理；这里是最后保险：
		// 一旦原生区域图仍因旧档/第三方地形出现空引用，终止当前AI行为并退避，
		// 防止同一批角色每帧重复抛完整异常把单核时间耗在日志与异常展开上。
		bool sanctuaryRelated = XjZhantanlinSystem.IsPathIsolationRelevant(__instance, __0);
		XjHighRealmMovement.RecoverNativePathFailure(__instance, __0, sanctuaryRelated);
		__result = ExecuteEvent.True;
		return null;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "updateFall")]
	public static bool XuanJianVNext_Actor_UpdateFall_HighRealmMovement_Prefix(Actor __instance)
	{
		// 先处理“普通文明单位被旧旃檀林目标拖入邻海”的稀有异常。方法内部
		// 对非水格立即返回，不会把领域几何/反射判断挂到所有普通角色热路径。
		if (XjZhantanlinSystem.IsPlaced
			&& XjZhantanlinSystem.TryRecoverExternalWaterStranding(__instance)) return false;

		long actorId = __instance?.data == null ? 0L : ((BaseSystemData)__instance.data).id;
		if (actorId > 0L
			&& !XjWorldBootstrapLane.HasPending
			&& !XjRuntimeActorInterestIndex.HasMovementInterest(actorId))
		{
			return true;
		}

		// updateFall is a native per-actor hot callback. Do not attach an always-on
		// Harmony postfix merely for diagnostics; interested high realms still run
		// the exact movement guard in this prefix.
		return !XjHighRealmMovement.ShouldSkipFall(__instance);
	}
}
