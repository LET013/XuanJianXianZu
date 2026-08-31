using System;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Version-tolerant native entity removal fallback. Used only when normal domain
/// cleanup cannot leave a half-spawned WorldBox object behind.
/// </summary>
internal static class XjNativeEntityLifecycleInterop
{
    private static readonly string[] ActorRemoveMethods =
    {
        "removeFromWorld", "remove", "killHimself", "kill", "destroy", "Destroy"
    };

    internal static bool TryRemoveActor(Actor actor)
    {
        if (actor == null) return false;
        for (int i = 0; i < ActorRemoveMethods.Length; i++)
        {
            if (XjNativeReflectionInterop.TryInvokeCompatible(
                    actor, ActorRemoveMethods[i], Array.Empty<object>(), out _, out _))
            {
                return true;
            }
        }

        object manager = World.world?.units;
        return manager != null
            && XjNativeReflectionInterop.TryInvokeCompatible(
                manager, "removeObject", new object[] { actor }, out _, out _);
    }
}
