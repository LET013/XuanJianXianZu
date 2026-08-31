using System;
using UnityEngine;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Resolves version-tolerant WorldBox map positions at the native boundary.
/// UI/domain callers receive only a Vector3 and never inspect native members.
/// </summary>
internal static class XjNativeMapPositionInterop
{
    private static readonly string[] NestedPositionMembers =
    {
        "current_position", "position", "world_position", "pos", "posV3",
        "city_center", "center", "main_tile", "tile", "current_tile"
    };

    private static readonly string[] TileResolverMethods =
    {
        "getTile", "getCenterTile", "getCityCenterTile", "GetCenterTile"
    };

    internal static bool TryResolve(object source, out Vector3 position)
    {
        return TryResolve(source, 0, out position);
    }


    internal static bool TryResolveTile(object source, out WorldTile tile)
    {
        tile = source as WorldTile;
        if (tile != null) return true;
        if (source == null) return false;
        for (int i = 0; i < TileResolverMethods.Length; i++)
        {
            if (XjNativeReflectionInterop.TryInvokeCompatible(
                    source, TileResolverMethods[i], Array.Empty<object>(), out object value, out _)
                && value is WorldTile resolved)
            {
                tile = resolved;
                return true;
            }
        }
        return false;
    }

    private static bool TryResolve(object source, int depth, out Vector3 position)
    {
        position = default;
        if (source == null || depth > 3) return false;

        if (source is Vector3 vector3)
        {
            position = vector3;
            return true;
        }
        if (source is Vector2 vector2)
        {
            position = new Vector3(vector2.x, vector2.y, 0f);
            return true;
        }
        if (TryReadFloatMember(source, "x", out float x)
            && TryReadFloatMember(source, "y", out float y))
        {
            position = new Vector3(x, y, 0f);
            return true;
        }

        for (int i = 0; i < NestedPositionMembers.Length; i++)
        {
            object value = XjNativeReflectionInterop.ReadMemberValue(source, NestedPositionMembers[i]);
            if (value != null
                && !ReferenceEquals(value, source)
                && TryResolve(value, depth + 1, out position))
            {
                return true;
            }
        }

        for (int i = 0; i < TileResolverMethods.Length; i++)
        {
            if (!XjNativeReflectionInterop.TryInvokeCompatible(
                    source,
                    TileResolverMethods[i],
                    Array.Empty<object>(),
                    out object value,
                    out _))
            {
                continue;
            }
            if (value != null
                && !ReferenceEquals(value, source)
                && TryResolve(value, depth + 1, out position))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryReadFloatMember(object source, string memberName, out float value)
    {
        value = 0f;
        object raw = XjNativeReflectionInterop.ReadMemberValue(source, memberName);
        if (raw == null) return false;
        try
        {
            value = Convert.ToSingle(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
