using System;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Systems.DongTian;

namespace XuanJianVNext.Patches;

[HarmonyPatch]
internal partial class XjVNextPatches
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Building), "getColorForMinimap", new[] { typeof(WorldTile) })]
	private static bool XuanJian_Building_GetColorForMinimap_DongTianGuard_Prefix(
		Building __instance,
		ref Color32 __result)
	{
		if (!XjDongTianBuildingSafety.IsXuanJianDongTianBuilding(__instance))
		{
			return true;
		}

		__result = new Color32(0, 0, 0, 0);
		return false;
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Building), "getColorForMinimap", new[] { typeof(WorldTile) })]
	private static Exception XuanJian_Building_GetColorForMinimap_DongTianGuard_Finalizer(
		Building __instance,
		Exception __exception,
		ref Color32 __result)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is NullReferenceException
			&& XjDongTianBuildingSafety.IsXuanJianDongTianBuilding(__instance))
		{
			__result = new Color32(0, 0, 0, 0);
			return null;
		}

		return __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BuildingMapIcon), "getColor")]
	private static bool XuanJian_BuildingMapIcon_GetColor_DongTianGuard_Prefix(
		BuildingMapIcon __instance,
		ref Color32 __result)
	{
		if (!XjDongTianBuildingSafety.IsRegisteredDongTianMapIcon(__instance))
		{
			return true;
		}

		__result = new Color32(0, 0, 0, 0);
		return false;
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(BuildingMapIcon), "getColor")]
	private static Exception XuanJian_BuildingMapIcon_GetColor_NullGuard_Finalizer(
		BuildingMapIcon __instance,
		Exception __exception,
		ref Color32 __result)
	{
		if (__exception is NullReferenceException
			&& XjDongTianBuildingSafety.IsRegisteredDongTianMapIcon(__instance))
		{
			// 只拦截从玄鉴洞天资产剥离出的旧地图图标，避免吞掉其他建筑
			// 的真实空引用。洞天本身不参与王国染色，透明返回即可。
			__result = new Color32(0, 0, 0, 0);
			return null;
		}

		return __exception;
	}
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(GroupSpriteObject), "setSprite", new[] { typeof(Sprite) })]
	private static bool XuanJian_GroupSpriteObject_SetSprite_NullSpriteGuard_Prefix(Sprite pSprite)
	{
		return pSprite != null;
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(GroupSpriteObject), "setSprite", new[] { typeof(Sprite) })]
	private static Exception XuanJian_GroupSpriteObject_SetSprite_NullSpriteGuard_Finalizer(Exception __exception)
	{
		return __exception is NullReferenceException ? null : __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(QuantumSpriteLibrary), "drawBuildings")]
	private static bool XuanJian_QuantumSpriteLibrary_DrawBuildings_DongTianGuard_Prefix(QuantumSpriteAsset pAsset)
	{
		return !XjDongTianBuildingSafety.ShouldBypassQuantumDraw(pAsset);
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(QuantumSpriteLibrary), "drawBuildings")]
	private static Exception XuanJian_QuantumSpriteLibrary_DrawBuildings_NullSpriteGuard_Finalizer(Exception __exception)
	{
		return __exception is NullReferenceException ? null : __exception;
	}
	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(QuantumSpriteManager), "updateSystems")]
	private static Exception XuanJian_QuantumSpriteManager_UpdateSystems_DongTianSpriteGuard_Finalizer(Exception __exception)
	{
		if (__exception is not NullReferenceException)
		{
			return __exception;
		}

		string stack = __exception.StackTrace ?? string.Empty;
		return stack.IndexOf("GroupSpriteObject.setSprite", StringComparison.Ordinal) >= 0
			|| stack.IndexOf("QuantumSpriteLibrary.drawBuildings", StringComparison.Ordinal) >= 0
			? null
			: __exception;
	}
}
