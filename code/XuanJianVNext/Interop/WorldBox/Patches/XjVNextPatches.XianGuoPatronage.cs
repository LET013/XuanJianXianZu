using System;
using HarmonyLib;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	/// <summary>
	/// 百官离开原国家时，仙国法持玄必须即时撤销；不能等到下一年度才保留一整年的借境战力。
	/// 这里只处理带持玄标记的人物，普通人口 setKingdom 路径为一个极轻的字段门禁。
	/// </summary>
	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "setKingdom", new Type[] { typeof(Kingdom) })]
	public static void XuanJianVNext_Actor_SetKingdom_XianGuoPatronagePostfix(Actor __instance)
	{
		XjXianGuoSystem.OnActorNativeKingdomChanged(__instance);
	}
}
