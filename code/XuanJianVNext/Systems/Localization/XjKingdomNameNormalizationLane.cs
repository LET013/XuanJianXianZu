using System.Collections.Generic;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Localization;

/// <summary>
/// One-time old-save kingdom-name reconciliation.
///
/// New kingdoms are normalized at their native creation hook, so there is no reason to
/// enqueue every kingdom every year.  Each world gets exactly one bounded initial pass;
/// afterwards this lane becomes dormant until the next world is loaded.
/// </summary>
internal static class XjKingdomNameNormalizationLane
{
	private static readonly List<long> PendingKingdomIds = new List<long>(32);
	private static int _cursor;
	private static bool _initialPassScheduled;

	internal static bool NeedsInitialSchedule => !_initialPassScheduled;
	internal static bool HasPending => _cursor < PendingKingdomIds.Count;
	internal static int PendingCount => HasPending ? PendingKingdomIds.Count - _cursor : 0;

	internal static void Schedule(int year)
	{
		if (year <= 0 || _initialPassScheduled) return;
		_initialPassScheduled = true;
		Begin();
	}

	internal static int Tick(int kingdomBudget, double timeBudgetMs)
	{
		if (kingdomBudget <= 0 || timeBudgetMs <= 0d || !HasPending)
		{
			return 0;
		}

		int processed = 0;
		XjCooperativeBudget budget = new XjCooperativeBudget(
			kingdomBudget,
			timeBudgetMs,
			XjRuntimeFramePriority.Background);
		while (_cursor < PendingKingdomIds.Count && budget.TryTake())
		{
			long kingdomId = PendingKingdomIds[_cursor++];
			if (XjWorldLookupIndex.TryResolveKingdom(kingdomId, out global::Kingdom kingdom))
			{
				XjNativeMetaNameSinicizer.Normalize(kingdom);
			}
			processed++;
		}

		if (_cursor >= PendingKingdomIds.Count)
		{
			PendingKingdomIds.Clear();
			_cursor = 0;
		}
		return processed;
	}

	internal static void Clear()
	{
		PendingKingdomIds.Clear();
		_cursor = 0;
		_initialPassScheduled = false;
	}

	private static void Begin()
	{
		PendingKingdomIds.Clear();
		_cursor = 0;
		IReadOnlyList<global::Kingdom> kingdoms = XjWorldLookupIndex.GetKingdomSnapshot();
		if (PendingKingdomIds.Capacity < kingdoms.Count) PendingKingdomIds.Capacity = kingdoms.Count;
		for (int i = 0; i < kingdoms.Count; i++)
		{
			global::Kingdom kingdom = kingdoms[i];
			if (kingdom?.data == null || kingdom.data.id <= 0L) continue;
			PendingKingdomIds.Add(kingdom.data.id);
		}
	}
}
