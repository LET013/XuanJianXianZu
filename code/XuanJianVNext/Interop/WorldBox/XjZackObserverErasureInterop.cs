using System;
using System.Reflection;
using HarmonyLib;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>对已安装的扎克体系做窄域兼容；未安装时完全不注册任何补丁。</summary>
internal static class XjZackObserverErasureInterop
{
    private static bool _initialized;

    internal static void Init(Harmony harmony)
    {
        if (_initialized || harmony == null) return;

        Type erasePowerType = AccessTools.TypeByName("ZhaKe.ErasePower");
        MethodInfo eraseMethod = erasePowerType == null
            ? null
            : AccessTools.Method(erasePowerType, "EraseUnitCompletely", new[] { typeof(Actor) });
        if (eraseMethod == null) return;

        HarmonyMethod prefix = new HarmonyMethod(typeof(XjZackObserverErasureInterop), nameof(EraseUnitCompletelyPrefix))
        {
            priority = Priority.First,
            before = new[] { "ZhaKe.Observer" }
        };
        harmony.Patch(eraseMethod, prefix: prefix);
        _initialized = true;
    }

    private static bool EraseUnitCompletelyPrefix(Actor pActor)
    {
        // 观察者攻击委托与全局循环都会先到 ErasePower；道胎在这里截断，
        // 保证不会在 getHit 之前被第三方直接从世界容器移除。
        return !XjZackObserverErasure.IsDaoTaiProtectedFromExternalErase(pActor);
    }
}
