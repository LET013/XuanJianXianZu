using UnityEngine;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Native renderer compatibility reads for actor presentation metrics.
/// </summary>
internal static class XjNativeActorVisualInterop
{
    private static readonly string[] RendererMemberNames =
    {
        "sprite_renderer", "spriteRenderer", "current_sprite_renderer", "renderer"
    };

    internal static bool TryResolveSpriteRenderer(Actor actor, out SpriteRenderer renderer)
    {
        renderer = null;
        if (actor == null) return false;
        for (int i = 0; i < RendererMemberNames.Length; i++)
        {
            if (XjNativeReflectionInterop.ReadMemberValue(actor, RendererMemberNames[i]) is SpriteRenderer found)
            {
                renderer = found;
                return true;
            }
        }
        return false;
    }
}
