using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace XuanJianVNext.Core;

/// <summary>
/// High-speed-safe stale actor-id maintenance.
///
/// The lane stores stable ids only. It never scans World.world.units and never
/// rebuilds retained containers. Healthy ids rotate through a bounded audit ring.
/// The first native-index miss moves an id into a much smaller suspect ring;
/// suspects are rechecked at most once per maintenance pass and must miss several
/// passes before eviction. O(1) linked-list removal lets explicit death/removal
/// delete its probe immediately instead of leaving unbounded queue tombstones.
/// </summary>
internal static class XjStaleActorIdEviction
{
	private sealed class Probe
	{
		internal readonly long ActorId;
		internal byte ConsecutiveMisses;
		internal bool IsSuspect;
		internal LinkedListNode<Probe> Node;

		internal Probe(long actorId)
		{
			ActorId = actorId;
		}
	}

	private const int MaximumTrackedActorIds = 65536;
	private const byte RequiredConsecutiveMisses = 4;
	private static readonly LinkedList<Probe> HealthyActorIds = new LinkedList<Probe>();
	private static readonly LinkedList<Probe> SuspectActorIds = new LinkedList<Probe>();
	private static Dictionary<long, Probe> ProbesByActorId = new Dictionary<long, Probe>();

	internal static int TrackedCount => ProbesByActorId.Count;
	internal static int SuspectCount => SuspectActorIds.Count;
	internal static bool HasTrackedIds => ProbesByActorId.Count > 0;

	internal static void Track(long actorId)
	{
		if (actorId <= 0L || ProbesByActorId.ContainsKey(actorId))
		{
			return;
		}

		if (ProbesByActorId.Count >= MaximumTrackedActorIds)
		{
			XjPerformanceTelemetry.ObserveQueue("staleActorIdTrackerSaturated", ProbesByActorId.Count);
			return;
		}

		Probe probe = new Probe(actorId);
		probe.Node = HealthyActorIds.AddLast(probe);
		ProbesByActorId.Add(actorId, probe);
	}

	/// <summary>
	/// Consumes a strictly bounded slice. Rings are snapshotted at pass start, so a
	/// recovered suspect or newly suspected healthy id cannot be checked twice in
	/// one Tick. onConfirmedStale must only clear current/reconstructible runtime
	/// state and must never fabricate death, history, inheritance or rewards.
	/// </summary>
	internal static int Tick(int maxChecks, double timeBudgetMs, Action<long> onConfirmedStale)
	{
		if (maxChecks <= 0 || timeBudgetMs <= 0d || ProbesByActorId.Count == 0
			|| World.world?.units == null)
		{
			return 0;
		}

		int processed = 0;
		int evicted = 0;
		long started = Stopwatch.GetTimestamp();
		int suspectsAtPassStart = SuspectActorIds.Count;
		int healthyAtPassStart = HealthyActorIds.Count;

		for (int i = 0; i < suspectsAtPassStart && processed < maxChecks; i++)
		{
			if (BudgetExpired(started, processed, timeBudgetMs))
			{
				break;
			}

			Probe probe = TakeFirst(SuspectActorIds);
			if (probe == null)
			{
				break;
			}
			processed++;

			if (XjActorRegistry.TryResolveLiveWorldActor(probe.ActorId, out _))
			{
				MoveToHealthy(probe);
				continue;
			}

			probe.ConsecutiveMisses = probe.ConsecutiveMisses >= byte.MaxValue
				? byte.MaxValue
				: (byte)(probe.ConsecutiveMisses + 1);
			if (probe.ConsecutiveMisses < RequiredConsecutiveMisses)
			{
				RequeueSuspect(probe);
				continue;
			}

			ProbesByActorId.Remove(probe.ActorId);
			probe.Node = null;
			evicted++;
			ConfirmStale(probe.ActorId, onConfirmedStale);
		}

		for (int i = 0; i < healthyAtPassStart && processed < maxChecks; i++)
		{
			if (BudgetExpired(started, processed, timeBudgetMs))
			{
				break;
			}

			Probe probe = TakeFirst(HealthyActorIds);
			if (probe == null)
			{
				break;
			}
			processed++;

			if (XjActorRegistry.TryResolveLiveWorldActor(probe.ActorId, out _))
			{
				RequeueHealthy(probe);
			}
			else
			{
				probe.ConsecutiveMisses = 1;
				probe.IsSuspect = true;
				probe.Node = SuspectActorIds.AddLast(probe);
			}
		}

		if (ProbesByActorId.Count > 0)
		{
			XjPerformanceTelemetry.ObserveQueue("staleActorIdsTracked", ProbesByActorId.Count);
		}
		if (SuspectActorIds.Count > 0)
		{
			XjPerformanceTelemetry.ObserveQueue("staleActorIdsSuspect", SuspectActorIds.Count);
		}
		if (evicted > 0)
		{
			XjPerformanceTelemetry.ObserveQueue("staleActorIdsEvictedPerPass", evicted);
		}
		return processed;
	}

	internal static void Forget(long actorId)
	{
		if (actorId <= 0L || !ProbesByActorId.TryGetValue(actorId, out Probe probe))
		{
			return;
		}

		ProbesByActorId.Remove(actorId);
		if (probe.Node?.List != null)
		{
			probe.Node.List.Remove(probe.Node);
		}
		probe.Node = null;
	}

	/// <summary>
	/// Low-speed hard-compact hook. Semantic entries are unchanged; only the
	/// Dictionary bucket array is rebuilt so an old population spike does not pin
	/// storage forever. Callers must hold the <=2x quiet-maintenance gate.
	/// </summary>
	internal static void RebuildStorage()
	{
		ProbesByActorId = ProbesByActorId.Count == 0
			? new Dictionary<long, Probe>()
			: new Dictionary<long, Probe>(ProbesByActorId);
	}

	internal static void Clear()
	{
		HealthyActorIds.Clear();
		SuspectActorIds.Clear();
		ProbesByActorId.Clear();
	}

	private static Probe TakeFirst(LinkedList<Probe> list)
	{
		LinkedListNode<Probe> node = list?.First;
		if (node == null)
		{
			return null;
		}
		Probe probe = node.Value;
		list.RemoveFirst();
		probe.Node = null;
		return probe;
	}

	private static void MoveToHealthy(Probe probe)
	{
		probe.ConsecutiveMisses = 0;
		probe.IsSuspect = false;
		probe.Node = HealthyActorIds.AddLast(probe);
	}

	private static void RequeueHealthy(Probe probe)
	{
		probe.IsSuspect = false;
		probe.Node = HealthyActorIds.AddLast(probe);
	}

	private static void RequeueSuspect(Probe probe)
	{
		probe.IsSuspect = true;
		probe.Node = SuspectActorIds.AddLast(probe);
	}

	private static void ConfirmStale(long actorId, Action<long> onConfirmedStale)
	{
		try
		{
			onConfirmedStale?.Invoke(actorId);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Core/StaleActorIdEviction.confirm", ex);
		}
	}

	private static bool BudgetExpired(long started, int processed, double timeBudgetMs)
	{
		return processed > 0
			&& (processed & 3) == 0
			&& (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency >= timeBudgetMs;
	}
}
