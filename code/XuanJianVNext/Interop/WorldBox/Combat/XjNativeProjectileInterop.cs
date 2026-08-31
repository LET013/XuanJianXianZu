using HarmonyLib;

namespace XuanJianVNext.Interop.WorldBox.Combat;

/// <summary>
/// O(1) native projectile access used by combat guards. Harmony private-member
/// access stays at the interop boundary and is resolved once into a FieldRef.
/// </summary>
internal static class XjNativeProjectileInterop
{
    private static readonly AccessTools.FieldRef<Projectile, BaseSimObject> InitiatorRef =
        AccessTools.FieldRefAccess<Projectile, BaseSimObject>("by_who");

    internal static BaseSimObject ResolveInitiator(Projectile projectile)
    {
        if (projectile == null) return null;
        try
        {
            return InitiatorRef(projectile);
        }
        catch
        {
            return null;
        }
    }
}
