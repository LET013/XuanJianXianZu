using System;
using HarmonyLib;
using XuanJianVNext.UI.Rank;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPrefix]
	[HarmonyPatch(typeof(ScrollWindow), nameof(ScrollWindow.hide))]
	private static bool XuanJianVNext_ScrollWindow_Hide_RankRightClickGuard_Prefix(ScrollWindow __instance)
	{
		return !XjRankWindow.ShouldBlockRightClickClose(__instance);
	}
	[HarmonyPrefix]
	[HarmonyPatch(typeof(ScrollWindow), "hideAllEvent")]
	private static bool XuanJianVNext_ScrollWindow_HideAllEvent_RankRightClickGuard_Prefix()
	{
		return !XjRankWindow.ShouldBlockGlobalRightClickClose();
	}

	// WindowLibrary 的关闭动画结束后会异步调用原版 WindowHistory.popHistory。
	// 若动画期间窗口历史已被玄鉴跳转或原版逻辑清空，原版会读取失效索引并让
	// DOTween 连续报告“YOU SHOULD RESTART THE GAME”。只吞掉这一类陈旧索引异常，
	// 其他异常仍原样上抛，避免掩盖真实故障。
	[HarmonyFinalizer]
	[HarmonyPatch(typeof(WindowHistory), "popHistory")]
	private static Exception XuanJianVNext_WindowHistory_PopHistory_StaleTween_Finalizer(Exception __exception)
	{
		return __exception is ArgumentOutOfRangeException || __exception is IndexOutOfRangeException
			? null
			: __exception;
	}

}
