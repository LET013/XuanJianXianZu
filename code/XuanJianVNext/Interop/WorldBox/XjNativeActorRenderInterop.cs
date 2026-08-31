using UnityEngine;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Version-tolerant access to ActorRenderData's native vector arrays.
/// Private member names stay at the interop edge; render/gameplay lanes use typed operations.
/// </summary>
internal static class XjNativeActorRenderInterop
{
    private static readonly string[] ClosedCultivationOffsetMembers =
    {
        "positions", "shadow_positions", "item_positions", "item_position"
    };

    internal static bool TryGetPositions(ActorRenderData renderData, out Vector3[] positions)
    {
        positions = null;
        if (renderData == null) return false;
        positions = XjNativeReflectionInterop.ReadMemberValue(renderData, "positions") as Vector3[];
        return positions != null;
    }

    internal static void ApplyClosedCultivationYOffset(ActorRenderData renderData, int index, float offset)
    {
        if (renderData == null || index < 0) return;
        for (int i = 0; i < ClosedCultivationOffsetMembers.Length; i++)
        {
            if (XjNativeReflectionInterop.ReadMemberValue(renderData, ClosedCultivationOffsetMembers[i]) is not Vector3[] values
                || index >= values.Length)
            {
                continue;
            }
            Vector3 value = values[index];
            values[index] = new Vector3(value.x, value.y + offset, value.z);
        }
    }
}
