using System;
using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Localization;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{

	[HarmonyPostfix]
	[HarmonyPatch(typeof(KingdomManager), "makeNewCivKingdom", new Type[] { typeof(Actor), typeof(string), typeof(bool) })]
	private static void XuanJian_KingdomManager_MakeNewCivKingdom_Sinicize_Postfix(Kingdom __result)
	{
		XjNativeMetaNameSinicizer.Normalize(__result);
		XjWorldLookupIndex.ObserveKingdom(__result);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(FamilyManager), "newFamily", new Type[] { typeof(Actor), typeof(WorldTile), typeof(Actor) })]
	private static bool XuanJian_FamilyManager_NewFamily_InvalidInputGuard_Prefix(
		[HarmonyArgument(0)] Actor actor,
		[HarmonyArgument(1)] WorldTile tile,
		[HarmonyArgument(2)] Actor lover,
		ref Family __result)
	{
		// WorldBox性繁殖AI偶尔会把已经失效的NanoObject继续送进newFamily；原生
		// 随后在get_id()/CoreSystemObject.getID()直接NRE。无有效父母或落点时本次
		// 生育事务本就无法成立，直接取消比让AI主循环持续抛异常安全。
		if (actor?.data == null || tile == null || (lover != null && lover.data == null))
		{
			__result = null;
			return false;
		}
		return true;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(FamilyManager), "newFamily", new Type[] { typeof(Actor), typeof(WorldTile), typeof(Actor) })]
	private static void XuanJian_FamilyManager_NewFamily_Sinicize_Postfix(Family __result)
	{
		XjNativeMetaNameSinicizer.Normalize(__result);
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(FamilyManager), "newFamily", new Type[] { typeof(Actor), typeof(WorldTile), typeof(Actor) })]
	private static Exception XuanJian_FamilyManager_NewFamily_NullReference_Finalizer(Exception __exception, ref Family __result)
	{
		if (__exception is not NullReferenceException || ContainsXuanJianStack(__exception)) return __exception;
		// 仅吞掉newFamily内部这一类NRE：原调用已经无法产出合法Family，返回null让
		// BehCheckForBabies下一次AI机会重新判定，避免异常沿Actor AI主循环向上冒泡。
		__result = null;
		return null;
	}

	// HF4 移除了 MapBox/ActorManager 的旧故障恢复补丁，也一并移走了原先定义在
	// World patch partial 中的共享辅助函数；FamilyManager finalizer 仍需要判断异常
	// 是否经过玄鉴代码。局部保留这个无状态判断，避免重新引入已经删除的恢复系统。
	private static bool ContainsXuanJianStack(Exception exception)
	{
		try
		{
			return (exception?.StackTrace ?? string.Empty).IndexOf("XuanJianVNext", StringComparison.Ordinal) >= 0;
		}
		catch
		{
			// 栈本身不可读时宁可让异常继续抛出，也不要误吞玄鉴自身异常。
			return true;
		}
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
