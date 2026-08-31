using System;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Narrow native Building/BuildingAsset compatibility boundary shared by洞天 systems.
/// </summary>
internal static class XjNativeBuildingInterop
{
    private static readonly string[] RendererMemberNames =
    {
        "spriteRenderer", "sprite_renderer", "current_sprite_renderer", "renderer"
    };

    private static readonly string[] RemoveMethodNames =
    {
        "removeFromWorld", "remove", "killHimself", "kill", "destroy", "Destroy"
    };

    internal static bool TryLoadSprites(BuildingAsset asset)
    {
        return asset != null
            && XjNativeReflectionInterop.TryInvokeCompatible(
                asset, "loadBuildingSprites", Array.Empty<object>(), out _, out _);
    }

    internal static BuildingMapIcon TryCloneMapIcon(BuildingMapIcon source)
    {
        if (source == null) return null;
        return XjNativeReflectionInterop.TryInvokeCompatible(
                source, "MemberwiseClone", Array.Empty<object>(), out object clone, out _)
            ? clone as BuildingMapIcon
            : null;
    }

    internal static void MarkSpriteDirty(Building building)
    {
        if (building == null) return;
        XjNativeReflectionInterop.TryWriteBooleanMember(building, "sprite_dirty", true);
    }

    internal static void SetVisible(Building building, bool visible)
    {
        if (building == null) return;
        XjNativeReflectionInterop.TryWriteBooleanMember(building, "is_visible", visible);
        MarkSpriteDirty(building);
        SetPresentationActive(building, visible);
    }

    internal static void SetPresentationActive(object target, bool visible)
    {
        if (target == null) return;
        object gameObject = XjNativeReflectionInterop.ReadMemberValue(target, "gameObject");
        if (gameObject != null)
        {
            XjNativeReflectionInterop.TryInvokeCompatible(
                gameObject, "SetActive", new object[] { visible }, out _, out _);
        }

        for (int i = 0; i < RendererMemberNames.Length; i++)
        {
            object renderer = XjNativeReflectionInterop.ReadMemberValue(target, RendererMemberNames[i]);
            if (renderer != null)
            {
                XjNativeReflectionInterop.TryWriteBooleanMember(renderer, "enabled", visible);
            }
        }
    }

    internal static bool TryDestroy(Building building)
    {
        if (building == null) return false;
        for (int i = 0; i < RemoveMethodNames.Length; i++)
        {
            if (XjNativeReflectionInterop.TryInvokeCompatible(
                    building, RemoveMethodNames[i], Array.Empty<object>(), out _, out _))
            {
                return true;
            }
        }

        try
        {
            WorldTile tile = ((BaseSimObject)building).current_tile;
            if (tile != null && ReferenceEquals(tile.building, building))
            {
                XjNativeReflectionInterop.TryWriteMemberValue(tile, "building", null);
            }
        }
        catch
        {
            // Fallback cleanup is best-effort; caller already treats false as failure.
        }
        return false;
    }
}
