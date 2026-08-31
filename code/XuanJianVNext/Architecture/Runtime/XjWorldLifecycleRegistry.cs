using System;
using System.Collections.Generic;
using XuanJianVNext.Core;

namespace XuanJianVNext.Architecture.Runtime;

/// <summary>
/// World load/clear hooks owned by feature modules. Native patch/event ingress
/// publishes the lifecycle once; feature packages subscribe here instead of
/// expanding XjInternalEventBus with domain-specific calls.
/// </summary>
internal sealed class XjWorldLifecycleDescriptor
{
	internal string Id { get; }
	internal int Order { get; }
	internal Action<int> Loaded { get; }
	internal Action Cleared { get; }

	internal XjWorldLifecycleDescriptor(
		string id,
		int order,
		Action<int> loaded,
		Action cleared)
	{
		Id = string.IsNullOrWhiteSpace(id)
			? throw new ArgumentException("World lifecycle id is required.", nameof(id))
			: id.Trim();
		Order = order;
		Loaded = loaded;
		Cleared = cleared;
	}
}

internal static class XjWorldLifecycleRegistry
{
	private static readonly List<XjWorldLifecycleDescriptor> Hooks = new List<XjWorldLifecycleDescriptor>();
	private static readonly HashSet<string> Ids = new HashSet<string>(StringComparer.Ordinal);
	private static bool _orderDirty = true;

	internal static void Register(XjWorldLifecycleDescriptor descriptor)
	{
		if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
		if (!Ids.Add(descriptor.Id)) throw new InvalidOperationException("Duplicate world lifecycle hook: " + descriptor.Id);
		Hooks.Add(descriptor);
		_orderDirty = true;
	}

	internal static void PublishLoaded(int currentYear)
	{
		Seal();
		for (int i = 0; i < Hooks.Count; i++)
		{
			XjWorldLifecycleDescriptor hook = Hooks[i];
			if (hook.Loaded == null) continue;
			try { hook.Loaded(Math.Max(0, currentYear)); }
			catch (Exception ex) { XjExceptionDiagnostics.Report("Architecture/Runtime/world-loaded:" + hook.Id, ex); }
		}
	}

	internal static void PublishCleared()
	{
		Seal();
		// Clear in reverse order to mirror module dependency teardown.
		for (int i = Hooks.Count - 1; i >= 0; i--)
		{
			XjWorldLifecycleDescriptor hook = Hooks[i];
			if (hook.Cleared == null) continue;
			try { hook.Cleared(); }
			catch (Exception ex) { XjExceptionDiagnostics.Report("Architecture/Runtime/world-cleared:" + hook.Id, ex); }
		}
	}

	private static void Seal()
	{
		if (!_orderDirty) return;
		Hooks.Sort((left, right) =>
		{
			int order = left.Order.CompareTo(right.Order);
			return order != 0 ? order : string.CompareOrdinal(left.Id, right.Id);
		});
		_orderDirty = false;
	}
}
