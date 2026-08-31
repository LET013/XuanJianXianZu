using System;
using HarmonyLib;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Interop.WorldBox.Legacy.Patches;

/// <summary>
/// 原生王位变化的窄观察口。帝明阳不再预先随机诞生；只有明阳修士被原生国政
/// 真正立为国王后，仙国权威层才把这次现实王位转化为“帝明阳 + 仙国法统”。
/// </summary>
internal static class XjVNextPatchesXianGuo
{
    // 不再 Prefix 阻断 Kingdom.setKing。原生换王是一个完整事务，半途 return 会跳过
    // WorldBox 自己的 profession / history / diplomacy / cache 等副作用，极易制造“原生认为
    // 已换王、部分容器仍指向旧王”的半状态。仙朝只在 Postfix 观察事实，并把不合法的
    // 王位投影放入低频后台修复队列。

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(Kingdom), "setKing", new Type[] { typeof(Actor) })]
    private static void XuanJianVNext_Kingdom_SetKing_XianGuoPostfix(
        Kingdom __instance,
        [HarmonyArgument(0)] Actor sovereign)
    {
        XjXianGuoSystem.OnNativeSovereignChanged(__instance, sovereign);
    }
}
