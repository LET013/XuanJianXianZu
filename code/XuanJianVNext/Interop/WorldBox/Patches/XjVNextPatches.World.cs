using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Visual;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(MapBox), "updateSimulation")]
	public static void XuanJianVNext_MapBox_UpdateSimulation_RuntimeCadence_Postfix(MapBox __instance, float pElapsed)
	{
		// 这是玄鉴进入原生主模拟的最外层边界。可选 AVBS 兼容或任一玄鉴调度项
		// 不能把异常带回 MapBox.updateSimulation，否则一个存档中的坏对象会直接
		// 中断整个原生世界循环，表现为无提示卡死/闪退。
		try
		{
			XjAvbsTraitPackInterop.Tick();
		}
		catch (System.Exception exception)
		{
			XjExceptionDiagnostics.Report("World.MapBox.updateSimulation.AVBS", exception);
		}
		try
		{
			XjInternalEventBus.PublishSimulationTick();
		}
		catch (System.Exception exception)
		{
			XjExceptionDiagnostics.Report("World.MapBox.updateSimulation.RuntimeCadence", exception);
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(MapBox), "finishingUpLoading")]
	public static void XuanJianVNext_MapBox_FinishingUpLoading_RuntimeSettings_Postfix()
	{
		try
		{
			XjInternalEventBus.PublishWorldLoaded();
		}
		catch (System.Exception exception)
		{
			// 读档后让玄鉴功能降级，不能因辅助档案或延迟体检阻断原生读档收尾。
			XjExceptionDiagnostics.Report("World.MapBox.finishingUpLoading", exception);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(MapBox), "clearWorld")]
	public static void XuanJianVNext_MapBox_ClearWorld_ReleasePooledVisuals_Prefix()
	{
		// 原生 clearWorld 会先执行 StackEffects.clear。玄鉴的紫府雾气与
		// 金丹/道胎光环复用 BaseEffect 对象池，因此必须在原生清池之前
		// 先 kill() 归还，不能等到下面的 Postfix 再清理。
		try
		{
			XjJinDanHaloVisualSystem.Clear();
			XjDaoTaiGazeVisualSystem.Clear();
		}
		catch (System.Exception exception)
		{
			// 清图阶段对象池可能已被原生提前回收；不能因此阻断 MapBox.clearWorld。
			XjExceptionDiagnostics.Report("World.MapBox.clearWorld.ReleaseVisuals", exception);
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(MapBox), "clearWorld")]
	public static void XuanJianVNext_MapBox_ClearWorld_ClearCache_Postfix()
	{
		try
		{
			XjAvbsTraitPackInterop.ClearRuntime();
			XjInternalEventBus.PublishWorldCleared();
		}
		catch (System.Exception exception)
		{
			XjExceptionDiagnostics.Report("World.MapBox.clearWorld.ClearRuntime", exception);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(WorldAgeManager), "checkAge")]
	public static bool XuanJianVNext_WorldAgeManager_CheckAge_UnknownAge_Prefix(WorldAgeAsset pAsset, ref WorldAgeAsset __result)
	{
		try
		{
			if (!XjEraRuntime.TryReplaceUnknownAge(pAsset, out WorldAgeAsset replacementAge))
			{
				return true;
			}

			__result = replacementAge;
			return false;
		}
		catch (System.Exception exception)
		{
			XjExceptionDiagnostics.Report("World.WorldAgeManager.checkAge", exception);
			return true;
		}
	}
}
