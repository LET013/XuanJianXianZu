using System;
using System.Collections.Generic;

namespace XuanJianVNext.Core;

/// <summary>
/// Compatibility transaction for explicit third-party actor replacement/clone operations.
/// Visible trait writes inside ActorTool.copyUnitToOtherUnit are transport data rather than
/// manual gameplay edits. Replacement sources are also marked across frames because WorldBox
/// may defer ActionLibrary.removeUnit into ActorManager.destroyObject after the caller returns.
/// </summary>
internal static class XjExternalUnitTransferContext
{
    [ThreadStatic]
    private static int _explicitExternalGenerationDepth;

    [ThreadStatic]
    private static int _traitTransferDepth;

    [ThreadStatic]
    private static Actor? _currentReplacementSource;

    private const int ReplacementRemovalLeaseTicks = 600;
    private const int CompletedReplacementRemovalLeaseTicks = 4;
    private static readonly string[] ExternalDeathReplacementTraitIds =
    {
        "Replicative Immortality",
        "Dragon Heart",
        "Magic Heart",
        "Human Morph",
        "Elf Morph",
        "Dwarf Morph",
        "Orc Morph",
        "Bandit Morph"
    };
    private static readonly Dictionary<Actor, int> PendingReplacementRemovals = new();
    private static readonly HashSet<Actor> CompletedReplacementRemovals = new();
    private static readonly List<Actor> ReplacementLeaseScratch = new();

    internal static bool IsExplicitExternalGeneration => _explicitExternalGenerationDepth > 0;
    internal static bool IsTraitTransferActive => _traitTransferDepth > 0;

    internal static bool HasExternalDeathReplacementTrait(Actor actor)
    {
        if (actor?.data == null) return false;
        try
        {
            for (int i = 0; i < ExternalDeathReplacementTraitIds.Length; i++)
            {
                if (actor.hasTrait(ExternalDeathReplacementTraitIds[i])) return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    internal static void EnterExplicitExternalGeneration(Actor? replacementSource)
    {
        _explicitExternalGenerationDepth++;
        if (_explicitExternalGenerationDepth == 1)
        {
            _currentReplacementSource = replacementSource;
        }

        if (replacementSource?.data != null)
        {
            PendingReplacementRemovals[replacementSource] = ReplacementRemovalLeaseTicks;
            CompletedReplacementRemovals.Remove(replacementSource);
        }
    }

    internal static void ExitExplicitExternalGeneration(bool keepReplacementRemovalMarker)
    {
        Actor? replacementSource = _currentReplacementSource;
        if (_explicitExternalGenerationDepth > 0)
        {
            _explicitExternalGenerationDepth--;
        }

        if (_explicitExternalGenerationDepth > 0)
        {
            return;
        }

        if (!keepReplacementRemovalMarker
            && replacementSource != null
            && !CompletedReplacementRemovals.Contains(replacementSource))
        {
            PendingReplacementRemovals.Remove(replacementSource);
        }

        _currentReplacementSource = null;
    }

    internal static bool IsExplicitReplacementRemoval(Actor actor)
    {
        if (actor == null)
        {
            return false;
        }

        return ReferenceEquals(actor, _currentReplacementSource)
            || PendingReplacementRemovals.ContainsKey(actor);
    }

    internal static void CompleteExplicitReplacementRemoval(Actor actor)
    {
        if (actor == null)
        {
            return;
        }

        // Keep a very short completed-removal lease instead of dropping the marker immediately.
        // AVBS Replicative Immortality runs from Actor.die.action_death: destroyObject can finish
        // before the outer Actor.die postfix executes. The short lease lets the death postfix still
        // recognize this as a replacement handoff and suppress false XuanJian death settlement.
        if (PendingReplacementRemovals.ContainsKey(actor))
        {
            PendingReplacementRemovals[actor] = CompletedReplacementRemovalLeaseTicks;
            CompletedReplacementRemovals.Add(actor);
        }
        if (ReferenceEquals(actor, _currentReplacementSource) && _explicitExternalGenerationDepth <= 0)
        {
            _currentReplacementSource = null;
        }
    }

    internal static void AdvancePendingReplacementRemovalLeases()
    {
        if (PendingReplacementRemovals.Count == 0)
        {
            return;
        }

        ReplacementLeaseScratch.Clear();
        foreach (Actor actor in PendingReplacementRemovals.Keys)
        {
            ReplacementLeaseScratch.Add(actor);
        }

        for (int i = 0; i < ReplacementLeaseScratch.Count; i++)
        {
            Actor actor = ReplacementLeaseScratch[i];
            if (!PendingReplacementRemovals.TryGetValue(actor, out int remaining))
            {
                continue;
            }

            remaining--;
            if (actor == null || actor.data == null || remaining <= 0)
            {
                PendingReplacementRemovals.Remove(actor);
                CompletedReplacementRemovals.Remove(actor);
            }
            else
            {
                PendingReplacementRemovals[actor] = remaining;
            }
        }
    }

    internal static void EnterTraitTransfer()
    {
        _traitTransferDepth++;
    }

    internal static void ExitTraitTransfer()
    {
        if (_traitTransferDepth > 0)
        {
            _traitTransferDepth--;
        }
    }

    internal static void Reset()
    {
        _explicitExternalGenerationDepth = 0;
        _traitTransferDepth = 0;
        _currentReplacementSource = null;
        PendingReplacementRemovals.Clear();
        CompletedReplacementRemovals.Clear();
        ReplacementLeaseScratch.Clear();
    }
}
