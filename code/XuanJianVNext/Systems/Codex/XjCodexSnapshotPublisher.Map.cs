using System;
using UnityEngine;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.Codex;

internal static partial class XjCodexSnapshotPublisher
{
    private static bool TryReadCityMapPosition(City city, out int tileX, out int tileY)
    {
        tileX = -1;
        tileY = -1;
        if (city == null || !XjNativeMapPositionInterop.TryResolve(city, out Vector3 position)) return false;
        tileX = Mathf.Clamp(Mathf.RoundToInt(position.x), 0, Math.Max(0, MapBox.width - 1));
        tileY = Mathf.Clamp(Mathf.RoundToInt(position.y), 0, Math.Max(0, MapBox.height - 1));
        return true;
    }
}
