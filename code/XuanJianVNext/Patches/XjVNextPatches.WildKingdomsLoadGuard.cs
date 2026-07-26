using System;
using HarmonyLib;
using UnityEngine;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	private static int _wildKingdomsDirtyBuildingsGuardLogCount;

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(WildKingdomsManager), "updateDirtyBuildings")]
	private static Exception XuanJianVNext_WildKingdomsManager_UpdateDirtyBuildings_LoadGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is not NullReferenceException)
		{
			return __exception;
		}

		LogWildKingdomsDirtyBuildingsGuard("updateDirtyBuildings", __exception);
		return null;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(WildKingdomsManager), "beginChecksBuildings")]
	private static Exception XuanJianVNext_WildKingdomsManager_BeginChecksBuildings_LoadGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is not NullReferenceException)
		{
			return __exception;
		}

		LogWildKingdomsDirtyBuildingsGuard("beginChecksBuildings", __exception);
		return null;
	}

	private static void LogWildKingdomsDirtyBuildingsGuard(string method, Exception exception)
	{
		if (_wildKingdomsDirtyBuildingsGuardLogCount >= 8)
		{
			return;
		}

		_wildKingdomsDirtyBuildingsGuardLogCount++;
		Debug.LogWarning("[玄鉴] 已拦截野外建筑归属清理空引用，读档继续。method=" + method + ", ex=" + exception.GetType().Name);
	}
}
