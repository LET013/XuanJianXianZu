using System;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// Marks the narrow native WorldBox trait-editor transaction that is allowed to
/// translate XuanJian visible projection traits into authoritative cultivation state.
/// Direct Actor.addTrait/removeTrait calls from scripts, saved-unit copiers and other
/// mods are not manual edits and must never gain this authority implicitly.
/// </summary>
internal static class XjManualTraitEditContext
{
    [ThreadStatic]
    private static int _depth;

    internal static bool IsActive => _depth > 0;

    internal static void Enter()
    {
        if (_depth < int.MaxValue)
        {
            _depth++;
        }
    }

    internal static void Exit()
    {
        if (_depth > 0)
        {
            _depth--;
        }
    }
}
