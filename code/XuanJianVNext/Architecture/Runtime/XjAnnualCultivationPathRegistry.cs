using System;
using System.Collections.Generic;
using XuanJianVNext.Core;

namespace XuanJianVNext.Architecture.Runtime;

/// <summary>
/// Annual actor lifecycle adapter owned by a cultivation path module. Third-path
/// systems (Shi and future paths) register here instead of adding path-specific
/// Prepare/Progression branches to the central actor pipeline.
/// </summary>
internal sealed class XjAnnualCultivationPathDescriptor
{
	internal string Id { get; }
	internal int Order { get; }
	internal Func<Actor, bool> Matches { get; }
	internal Action<Actor, int> Prepare { get; }
	internal Action<Actor, int> Progress { get; }
	internal Func<Actor, int> CombatTier { get; }

	internal XjAnnualCultivationPathDescriptor(
		string id,
		int order,
		Func<Actor, bool> matches,
		Action<Actor, int> prepare,
		Action<Actor, int> progress,
		Func<Actor, int> combatTier = null)
	{
		Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Path adapter id is required.", nameof(id)) : id.Trim();
		Order = order;
		Matches = matches ?? throw new ArgumentNullException(nameof(matches));
		Prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
		Progress = progress ?? throw new ArgumentNullException(nameof(progress));
		CombatTier = combatTier;
	}
}

internal static class XjAnnualCultivationPathRegistry
{
	private static readonly List<XjAnnualCultivationPathDescriptor> Adapters = new List<XjAnnualCultivationPathDescriptor>();
	private static readonly HashSet<string> Ids = new HashSet<string>(StringComparer.Ordinal);
	private static bool _orderDirty = true;

	internal static void Register(XjAnnualCultivationPathDescriptor descriptor)
	{
		if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
		if (!Ids.Add(descriptor.Id)) throw new InvalidOperationException("Duplicate annual cultivation path adapter: " + descriptor.Id);
		Adapters.Add(descriptor);
		_orderDirty = true;
	}

	internal static bool TryPrepare(Actor actor, int annualYear)
	{
		if (!TryResolve(actor, out XjAnnualCultivationPathDescriptor adapter)) return false;
		try { adapter.Prepare(actor, annualYear); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("Architecture/Runtime/path-prepare:" + adapter.Id, ex); }
		return true;
	}

	internal static bool TryProgress(Actor actor, int annualYear)
	{
		if (!TryResolve(actor, out XjAnnualCultivationPathDescriptor adapter)) return false;
		try { adapter.Progress(actor, annualYear); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("Architecture/Runtime/path-progress:" + adapter.Id, ex); }
		return true;
	}

	internal static bool TryGetCombatTier(Actor actor, out int tier)
	{
		tier = 0;
		if (!TryResolve(actor, out XjAnnualCultivationPathDescriptor adapter) || adapter.CombatTier == null) return false;
		try
		{
			tier = adapter.CombatTier(actor);
			return true;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Architecture/Runtime/path-tier:" + adapter.Id, ex);
			return false;
		}
	}

	private static bool TryResolve(Actor actor, out XjAnnualCultivationPathDescriptor adapter)
	{
		adapter = null;
		if (actor?.data == null) return false;
		Seal();
		for (int i = 0; i < Adapters.Count; i++)
		{
			XjAnnualCultivationPathDescriptor candidate = Adapters[i];
			try
			{
				if (!candidate.Matches(actor)) continue;
				adapter = candidate;
				return true;
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("Architecture/Runtime/path-match:" + candidate.Id, ex);
			}
		}
		return false;
	}

	private static void Seal()
	{
		if (!_orderDirty) return;
		Adapters.Sort((left, right) =>
		{
			int order = left.Order.CompareTo(right.Order);
			return order != 0 ? order : string.CompareOrdinal(left.Id, right.Id);
		});
		_orderDirty = false;
	}
}
