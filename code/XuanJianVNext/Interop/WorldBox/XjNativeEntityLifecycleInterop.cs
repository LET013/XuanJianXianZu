using System;
using XuanJianVNext.Core;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Version-tolerant native entity removal fallback. Used only when normal domain
/// cleanup cannot leave a half-spawned WorldBox object behind.
///
/// Removal is treated as a small transaction: prefer non-death native detach/remove
/// methods, verify that the WorldBox unit index no longer resolves the same actor,
/// then unregister the same stable id from XuanJian runtime caches. Death methods are
/// deliberately last-resort so high-realm death protection cannot turn spawn rollback
/// into a surviving half-initialized actor.
/// </summary>
internal static class XjNativeEntityLifecycleInterop
{
    private static readonly string[] ActorDetachMethods =
    {
        "removeFromWorld", "remove", "destroy", "Destroy"
    };

    private static readonly string[] ActorDeathFallbackMethods =
    {
        "killHimself", "kill"
    };

    internal static bool TryRemoveActor(Actor actor)
    {
        if (actor == null) return false;

        long actorId = ResolveActorId(actor);

        // Prefer Actor-native detach/remove APIs. A compatible invocation is not by
        // itself proof of removal; some WorldBox methods only detach spatial state.
        for (int i = 0; i < ActorDetachMethods.Length; i++)
        {
            if (!TryInvokeActorMethod(actor, ActorDetachMethods[i])) continue;
            if (!IsStillIndexedAsSameActor(actorId, actor))
            {
                FinalizeRuntimeRemoval(actorId);
                return true;
            }
        }

        // ActorManager owns the authoritative unit index and is the safest fallback
        // when an Actor method did not fully commit removal.
        object manager = World.world?.units;
        if (manager != null
            && XjNativeReflectionInterop.TryInvokeCompatible(
                manager, "removeObject", new object[] { actor }, out _, out _)
            && !IsStillIndexedAsSameActor(actorId, actor))
        {
            FinalizeRuntimeRemoval(actorId);
            return true;
        }

        // Only as a final compatibility fallback use death semantics. This is kept
        // behind direct removal so ZhenJun/DaoTai death guards cannot preserve an
        // invalid synthetic actor during spawn rollback.
        for (int i = 0; i < ActorDeathFallbackMethods.Length; i++)
        {
            if (!TryInvokeActorMethod(actor, ActorDeathFallbackMethods[i])) continue;
            if (!IsStillIndexedAsSameActor(actorId, actor))
            {
                FinalizeRuntimeRemoval(actorId);
                return true;
            }
        }

        return false;
    }

    private static bool TryInvokeActorMethod(Actor actor, string methodName)
    {
        return actor != null
            && XjNativeReflectionInterop.TryInvokeCompatible(
                actor, methodName, Array.Empty<object>(), out _, out _);
    }

    private static long ResolveActorId(Actor actor)
    {
        try
        {
            return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
        }
        catch
        {
            return 0L;
        }
    }

    private static bool IsStillIndexedAsSameActor(long actorId, Actor actor)
    {
        if (actorId <= 0L) return false;
        try
        {
            Actor indexed = World.world?.units?.get(actorId);
            return indexed != null && ReferenceEquals(indexed, actor);
        }
        catch
        {
            // If the authoritative manager cannot resolve the id during cleanup, do
            // not keep a synthetic actor alive only because the liveness probe failed.
            return false;
        }
    }

    private static void FinalizeRuntimeRemoval(long actorId)
    {
        if (actorId <= 0L) return;
        try
        {
            XjActorRegistry.Unregister(actorId);
        }
        catch
        {
            // Native removal has already committed. Registry cleanup is best-effort;
            // its bounded stale-id lane will remove any remaining cache entry later.
        }
    }
}
