using System;
using HarmonyLib;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Patches;

/// <summary>
/// Native UI quarantine. Previous broad avatar/tooltip/banner/SelectedUnitTab
/// finalizers swallowed NullReferenceException after WorldBox had already partially
/// mutated its UI, leaving half-updated objects that were retried on the next frame.
///
/// The sole remaining hook is the explicit user trait-editor transaction marker. It
/// does not alter UI output and its finalizer never suppresses the native exception.
/// </summary>
internal partial class XjVNextPatches
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(ActorTraitsEditor), "addTrait", new[] { typeof(ActorTrait) })]
    private static void XuanJian_ActorTraitsEditor_AddTrait_ManualContext_Prefix()
    {
        XjManualTraitEditContext.Enter();
    }

    [HarmonyFinalizer]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(ActorTraitsEditor), "addTrait", new[] { typeof(ActorTrait) })]
    private static Exception XuanJian_ActorTraitsEditor_AddTrait_ManualContext_Finalizer(Exception __exception)
    {
        XjManualTraitEditContext.Exit();
        return __exception;
    }
}
