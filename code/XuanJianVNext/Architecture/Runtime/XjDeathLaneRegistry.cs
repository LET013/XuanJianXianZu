using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Death;

namespace XuanJianVNext.Architecture.Runtime;

internal sealed class XjDeathLaneDescriptor
{
	internal string Id { get; }
	internal int Order { get; }
	internal Func<bool> HasPending { get; }
	internal Action<int, XjDeathActorResolver> Tick { get; }

	internal XjDeathLaneDescriptor(
		string id,
		int order,
		Func<bool> hasPending,
		Action<int, XjDeathActorResolver> tick)
	{
		Id = string.IsNullOrWhiteSpace(id)
			? throw new ArgumentException("Death lane id is required.", nameof(id))
			: id.Trim();
		Order = order;
		HasPending = hasPending ?? throw new ArgumentNullException(nameof(hasPending));
		Tick = tick ?? throw new ArgumentNullException(nameof(tick));
	}
}

/// <summary>
/// Critical death finalizers retain one-lane-per-cadence semantics while becoming
/// independently registerable and auditable.
/// </summary>
internal static class XjDeathLaneRegistry
{
	private static readonly List<XjDeathLaneDescriptor> Lanes = new List<XjDeathLaneDescriptor>();
	private static readonly HashSet<string> Ids = new HashSet<string>(StringComparer.Ordinal);
	private static int _cursor;
	private static bool _orderDirty = true;

	internal static void Register(XjDeathLaneDescriptor descriptor)
	{
		if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
		if (!Ids.Add(descriptor.Id)) throw new InvalidOperationException("Duplicate death lane id: " + descriptor.Id);
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
			foreach (XjDeathLaneDescriptor lane in Lanes)
			{
				try
				{
					if (lane.HasPending()) return true;
				}
				catch (Exception ex)
				{
					XjExceptionDiagnostics.Report("Architecture/Runtime/death-interest:" + lane.Id, ex);
				}
			}
			return false;
		}
	}

	internal static bool TickNext(int budget, XjDeathActorResolver resolver)
	{
		Seal();
		int count = Lanes.Count;
		for (int attempt = 0; attempt < count; attempt++)
		{
			int index = _cursor;
			_cursor = count == 0 ? 0 : (_cursor + 1) % count;
			XjDeathLaneDescriptor lane = Lanes[index];
			bool pending;
			try
			{
				pending = lane.HasPending();
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("Architecture/Runtime/death-interest:" + lane.Id, ex);
				continue;
			}
			if (!pending) continue;

			try
			{
				lane.Tick(Math.Max(1, budget), resolver);
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("Architecture/Runtime/death-tick:" + lane.Id, ex);
			}
			return true;
		}
		return false;
	}

	internal static void ResetCursor()
	{
		_cursor = 0;
	}
}
