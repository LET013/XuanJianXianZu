using System;
using System.Collections.Generic;
using System.Diagnostics;
using XuanJianVNext.Core;

namespace XuanJianVNext.Architecture.Runtime;

internal enum XjRuntimeBacklogPolicy : byte
{
	Exact = 0,
	CoalesceLatest = 1,
	BoundedBestEffort = 2
}

internal enum XjRuntimeYearSemantics : byte
{
	None = 0,
	ExactYear = 1,
	CurrentState = 2,
	LatestPeriod = 3
}

/// <summary>
/// Mandatory declaration for every background lane. The registry turns the
/// item/time fields into a cooperative slice for budget-aware lanes; legacy
/// parameterless lanes remain one atomic owner call and are still checked before
/// entry plus diagnosed afterwards. Every lane must also declare backlog and
/// year semantics.
/// </summary>
internal readonly struct XjRuntimeLanePolicy
{
	internal XjRuntimeLanePolicy(
		int maxItemsPerPass,
		double maxMillisecondsPerPass,
		XjRuntimeBacklogPolicy backlogPolicy,
		XjRuntimeYearSemantics yearSemantics)
	{
		if (maxItemsPerPass <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxItemsPerPass), "Background lane item budget must be positive.");
		}
		if (maxMillisecondsPerPass <= 0d)
		{
			throw new ArgumentOutOfRangeException(nameof(maxMillisecondsPerPass), "Background lane time budget must be positive.");
		}

		MaxItemsPerPass = maxItemsPerPass;
		MaxMillisecondsPerPass = maxMillisecondsPerPass;
		BacklogPolicy = backlogPolicy;
		YearSemantics = yearSemantics;
	}

	internal int MaxItemsPerPass { get; }
	internal double MaxMillisecondsPerPass { get; }
	internal XjRuntimeBacklogPolicy BacklogPolicy { get; }
	internal XjRuntimeYearSemantics YearSemantics { get; }
}

internal sealed class XjBackgroundLaneDescriptor
{
	internal string Id { get; }
	internal int Order { get; }
	internal Func<bool> HasPending { get; }
	internal Action<XjCooperativeBudget> Tick { get; }
	internal XjRuntimeLanePolicy Policy { get; }
	internal bool IsAtomicOwnerCall { get; }

	internal XjBackgroundLaneDescriptor(
		string id,
		int order,
		Func<bool> hasPending,
		Action<XjCooperativeBudget> tick,
		XjRuntimeLanePolicy policy)
	{
		Id = string.IsNullOrWhiteSpace(id)
			? throw new ArgumentException("Background lane id is required.", nameof(id))
			: id.Trim();
		Order = order;
		HasPending = hasPending ?? throw new ArgumentNullException(nameof(hasPending));
		Tick = tick ?? throw new ArgumentNullException(nameof(tick));
		Policy = policy;
		IsAtomicOwnerCall = false;
	}

	internal XjBackgroundLaneDescriptor(
		string id,
		int order,
		Func<bool> hasPending,
		Action tick,
		XjRuntimeLanePolicy policy)
		: this(
			id,
			order,
			hasPending,
			budget =>
			{
				// Legacy lanes are explicitly atomic: one cooperative item authorizes
				// one owner call. They still obey the shared frame/deadline gate before
				// entering; count-based lanes should use the budget-aware overload.
				if (budget.TryTake()) tick();
			},
			policy)
	{
		if (tick == null) throw new ArgumentNullException(nameof(tick));
		IsAtomicOwnerCall = true;
	}
}

/// <summary>
/// Round-robin registry for low-frequency work. Adding a feature lane no longer
/// expands a fixed switch or a manually mirrored HasBackgroundWork expression.
/// </summary>
internal static class XjBackgroundLaneRegistry
{
	private static readonly List<XjBackgroundLaneDescriptor> Lanes = new List<XjBackgroundLaneDescriptor>();
	private static readonly HashSet<string> Ids = new HashSet<string>(StringComparer.Ordinal);
	private static int _cursor;
	private static bool _orderDirty = true;

	private static readonly HashSet<string> RequiredCooperativeLaneIds = new HashSet<string>(StringComparer.Ordinal)
	{
		"background.world-lookup",
		"background.dongtian",
		"background.caiqi-city",
		"background.caiqi-site",
		"background.sect",
		"background.high-realm-army",
		"background.invariant-reconcile"
	};

	internal static int Count => Lanes.Count;

	internal static void Register(XjBackgroundLaneDescriptor descriptor)
	{
		if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
		if (!Ids.Add(descriptor.Id)) throw new InvalidOperationException("Duplicate background lane id: " + descriptor.Id);
		Lanes.Add(descriptor);
		_orderDirty = true;
	}

	internal static void Seal()
	{
		if (!_orderDirty) return;
		Lanes.Sort((left, right) =>
		{
			int order = left.Order.CompareTo(right.Order);
			return order != 0 ? order : string.CompareOrdinal(left.Id, right.Id);
		});
		_orderDirty = false;
		if (_cursor >= Lanes.Count) _cursor = 0;
	}

	internal static bool HasPending
	{
		get
		{
			Seal();
			foreach (XjBackgroundLaneDescriptor lane in Lanes)
			{
				try
				{
					if (lane.HasPending()) return true;
				}
				catch (Exception ex)
				{
					XjExceptionDiagnostics.Report("Architecture/Runtime/background-interest:" + lane.Id, ex);
				}
			}
			return false;
		}
	}

	internal static bool TickNext()
	{
		if (!XjRuntimeFrameGovernor.CanContinue(XjRuntimeFramePriority.Background)) return false;
		Seal();
		int count = Lanes.Count;
		for (int attempt = 0; attempt < count; attempt++)
		{
			int index = _cursor;
			_cursor = count == 0 ? 0 : (_cursor + 1) % count;
			XjBackgroundLaneDescriptor lane = Lanes[index];
			bool pending;
			try
			{
				pending = lane.HasPending();
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("Architecture/Runtime/background-interest:" + lane.Id, ex);
				continue;
			}
			if (!pending) continue;

			long started = Stopwatch.GetTimestamp();
			long diagnosticSample = XjRuntimeDiagnostics.BeginNamedSample();
			XjCooperativeBudget cooperativeBudget = new XjCooperativeBudget(
				lane.Policy.MaxItemsPerPass,
				lane.Policy.MaxMillisecondsPerPass,
				XjRuntimeFramePriority.Background);
			try
			{
				lane.Tick(cooperativeBudget);
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("Architecture/Runtime/background-tick:" + lane.Id, ex);
			}
			finally
			{
				// Lane IDs already carry the "background." namespace. Do not prefix them
				// a second time or diagnostics become background.background.<id>.
				XjRuntimeDiagnostics.EndNamedSample(lane.Id, diagnosticSample);
				double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
				if (elapsedMs > lane.Policy.MaxMillisecondsPerPass)
				{
					int rounded = elapsedMs >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(elapsedMs));
					XjPerformanceTelemetry.ObserveQueue("laneOverrun." + lane.Id, rounded);
				}
				XjRuntimeFrameGovernor.MarkPotentialOverrun(lane.Id);
			}
			return true;
		}
		return false;
	}

	internal static void CollectInvariantIssues(List<string> issues)
	{
		if (issues == null) return;
		Seal();
		HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < Lanes.Count; i++)
		{
			XjBackgroundLaneDescriptor lane = Lanes[i];
			found.Add(lane.Id);
			if (lane.IsAtomicOwnerCall && lane.Policy.MaxItemsPerPass != 1)
			{
				issues.Add("后台车道 " + lane.Id + " 使用 atomic owner call，但声明 MaxItemsPerPass=" + lane.Policy.MaxItemsPerPass);
			}
			if (RequiredCooperativeLaneIds.Contains(lane.Id) && lane.IsAtomicOwnerCall)
			{
				issues.Add("后台车道 " + lane.Id + " 必须直接消费 XjCooperativeBudget，禁止退回参数less owner call");
			}
		}

		foreach (string laneId in RequiredCooperativeLaneIds)
		{
			if (!found.Contains(laneId)) issues.Add("缺少发布前必须存在的 cooperative 后台车道：" + laneId);
		}
	}

	internal static string BuildSummary()
	{
		Seal();
		int atomic = 0;
		for (int i = 0; i < Lanes.Count; i++) if (Lanes[i].IsAtomicOwnerCall) atomic++;
		return "lanes=" + Lanes.Count + ", atomic=" + atomic + ", cooperative=" + (Lanes.Count - atomic);
	}

	internal static void ResetCursor()
	{
		_cursor = 0;
	}
}
