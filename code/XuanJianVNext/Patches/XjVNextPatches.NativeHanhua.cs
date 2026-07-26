using HarmonyLib;
using XuanJianVNext.Systems.Localization;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(FamilyManager), "newFamily")]
	private static void XuanJian_FamilyManager_NewFamily_Sinicize_Postfix(Family __result)
	{
		XjNativeMetaNameSinicizer.Normalize(__result);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Family), "loadData")]
	private static void XuanJian_Family_LoadData_Sinicize_Postfix(Family __instance)
	{
		XjNativeMetaNameSinicizer.Normalize(__instance);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(ReligionManager), "newReligion")]
	private static void XuanJian_ReligionManager_NewReligion_Sinicize_Postfix(Religion __result)
	{
		XjNativeMetaNameSinicizer.Normalize(__result);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Religion), "loadData")]
	private static void XuanJian_Religion_LoadData_Sinicize_Postfix(Religion __instance)
	{
		XjNativeMetaNameSinicizer.Normalize(__instance);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(BookManager), "generateNewBook")]
	private static void XuanJian_BookManager_GenerateNewBook_Sinicize_Postfix(Book __result)
	{
		XjNativeMetaNameSinicizer.Normalize(__result);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(BookManager), "newBook")]
	private static void XuanJian_BookManager_NewBook_Sinicize_Postfix(Book __result)
	{
		XjNativeMetaNameSinicizer.Normalize(__result);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Book), "loadData")]
	private static void XuanJian_Book_LoadData_Sinicize_Postfix(Book __instance)
	{
		XjNativeMetaNameSinicizer.Normalize(__instance);
	}
}
