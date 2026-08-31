using System;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Systems.DongTian;

namespace XuanJianVNext.Patches;

[HarmonyPatch]
internal partial class XjVNextPatches
{
	// 落霞山旧档中可能已保存过贴图目录拼写未兼容时创建的建筑。正常资产加载会在
	// 下一帧恢复贴图；在此之前不要让该一座洞天建筑反复打断原生并行渲染。
	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Building), "calculateMainSprite")]
	private static Exception XuanJian_Building_CalculateMainSprite_DongTianGuard_Finalizer(
		Building __instance,
		Exception __exception)
	{
		return __exception is NullReferenceException
			&& XjDongTianBuildingSafety.IsXuanJianDongTianBuilding(__instance)
			? null
			: __exception;
	}

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

		// 保留原生 BuildingMapIcon 对象供 QuantumSprite 使用，只把洞天图标画成透明。
		// 不能再把 map_icon 引用置 null，否则近景量子建筑批次会进入不完整对象图。
		__result = new Color32(0, 0, 0, 0);
		return false;
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(BuildingMapIcon), "getColor")]
	private static Exception XuanJian_BuildingMapIcon_GetColor_DongTianGuard_Finalizer(
		BuildingMapIcon __instance,
		Exception __exception,
		ref Color32 __result)
	{
		if (__exception is NullReferenceException
			&& XjDongTianBuildingSafety.IsRegisteredDongTianMapIcon(__instance))
		{
			__result = new Color32(0, 0, 0, 0);
			return null;
		}

		return __exception;
	}

}
