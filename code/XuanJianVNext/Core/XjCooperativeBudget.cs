using System;
using System.Diagnostics;

namespace XuanJianVNext.Core;

/// <summary>
/// A cooperative slice shared by cursor/queue based runtime work. Unlike a plain
/// local Stopwatch gate it also observes the rendered-frame governor after every
/// work item, so a lane cannot keep draining its private budget after the global
/// XuanJian budget has already been exhausted.
///
/// The budget never cancels an item that has already started. Callers keep their
/// cursor/state authoritative, finish one atomic item, then yield before taking
/// another item. This preserves annual/event semantics while making backlog work
/// resumable across rendered frames.
/// </summary>
internal sealed class XjCooperativeBudget
{
    private readonly XjRuntimeFramePriority _priority;
    private readonly long _deadlineTimestamp;
    private readonly bool _hasDeadline;
    private int _remainingItems;

    internal XjCooperativeBudget(
        int itemBudget,
        double timeBudgetMilliseconds,
        XjRuntimeFramePriority priority)
    {
        _remainingItems = Math.Max(0, itemBudget);
        _priority = priority;
        _hasDeadline = timeBudgetMilliseconds > 0d;
        if (_hasDeadline)
        {
            double clampedMilliseconds = Math.Max(0.01d, timeBudgetMilliseconds);
            double deadlineTicks = clampedMilliseconds * Stopwatch.Frequency / 1000d;
            _deadlineTimestamp = Stopwatch.GetTimestamp() + (long)Math.Ceiling(deadlineTicks);
        }
        else
        {
            _deadlineTimestamp = 0L;
        }
    }

    internal int RemainingItems => Math.Max(0, _remainingItems);

    internal double RemainingMilliseconds
    {
        get
        {
            if (!_hasDeadline) return XjRuntimeFrameGovernor.RemainingMilliseconds;
            long ticks = _deadlineTimestamp - Stopwatch.GetTimestamp();
            if (ticks <= 0L) return 0d;
            double localRemaining = ticks * 1000d / Stopwatch.Frequency;
            return Math.Max(0d, Math.Min(localRemaining, XjRuntimeFrameGovernor.RemainingMilliseconds));
        }
    }

    internal bool ShouldYield
    {
        get
        {
            if (_remainingItems <= 0) return true;
            if (!XjRuntimeFrameGovernor.CanContinue(_priority)) return true;
            return _hasDeadline && Stopwatch.GetTimestamp() >= _deadlineTimestamp;
        }
    }

    internal bool TryTake()
    {
        if (ShouldYield) return false;
        _remainingItems--;
        return true;
    }
}
