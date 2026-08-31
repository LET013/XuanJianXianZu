using System;
using System.Collections.Generic;
using XuanJianVNext.Core;

namespace XuanJianVNext.Architecture.Runtime;

internal readonly struct XjAnnualSecondaryContext
{
	internal readonly Actor Actor;
	internal readonly long ActorId;
	internal readonly int AnnualYear;
	internal readonly string RealmId;
	internal readonly int RealmTier;
	internal readonly bool IsFuQi;
	internal readonly bool IsFuQiHighRealm;
	internal readonly bool IsShi;

	internal bool IsFuQiLowRealm => IsFuQi && !IsFuQiHighRealm;
	internal bool BlocksImmortalAssets => IsShi || IsFuQiLowRealm;

	internal XjAnnualSecondaryContext(
		Actor actor,
		long actorId,
		int annualYear,
		string realmId,
		int realmTier,
		bool isFuQi,
		bool isFuQiHighRealm,
		bool isShi)
	{
		Actor = actor;
		ActorId = actorId;
		AnnualYear = annualYear;
		RealmId = realmId ?? string.Empty;
		RealmTier = realmTier;
		IsFuQi = isFuQi;
		IsFuQiHighRealm = isFuQiHighRealm;
		IsShi = isShi;
	}
}

internal delegate void XjAnnualSecondaryStepRunner(
	long actorId,
	int annualYear,
	string stepId,
	Action action);

internal sealed class XjAnnualActorExtensionDescriptor
{
	internal string Id { get; }
	internal int Order { get; }
	internal Func<XjAnnualSecondaryContext, bool> HasQueueInterest { get; }
	internal Func<XjAnnualSecondaryContext, bool> ShouldExecute { get; }
	internal Action<XjAnnualSecondaryContext> Execute { get; }

	internal XjAnnualActorExtensionDescriptor(
		string id,
		int order,
		Func<XjAnnualSecondaryContext, bool> hasQueueInterest,
		Func<XjAnnualSecondaryContext, bool> shouldExecute,
		Action<XjAnnualSecondaryContext> execute)
	{
		Id = string.IsNullOrWhiteSpace(id)
			? throw new ArgumentException("Annual actor extension id is required.", nameof(id))
			: id.Trim();
		Order = order;
		HasQueueInterest = hasQueueInterest ?? throw new ArgumentNullException(nameof(hasQueueInterest));
		ShouldExecute = shouldExecute ?? throw new ArgumentNullException(nameof(shouldExecute));
		Execute = execute ?? throw new ArgumentNullException(nameof(execute));
	}
}

/// <summary>
/// Extension point for exact-year secondary actor gameplay. New annual systems
/// register one descriptor and no longer modify scheduler queue gates and the
/// execution chain separately.
/// </summary>
internal static class XjAnnualActorExtensionRegistry
{
	private static readonly List<XjAnnualActorExtensionDescriptor> Extensions =
		new List<XjAnnualActorExtensionDescriptor>();
	private static readonly HashSet<string> Ids = new HashSet<string>(StringComparer.Ordinal);
	private static bool _orderDirty = true;

	internal static int Count => Extensions.Count;

	internal static void Register(XjAnnualActorExtensionDescriptor descriptor)
	{
		if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
		if (!Ids.Add(descriptor.Id)) throw new InvalidOperationException("Duplicate annual actor extension id: " + descriptor.Id);
		Extensions.Add(descriptor);
		_orderDirty = true;
	}

	internal static bool HasQueueInterest(in XjAnnualSecondaryContext context)
	{
		Seal();
		foreach (XjAnnualActorExtensionDescriptor extension in Extensions)
		{
			try
			{
				if (extension.HasQueueInterest(context)) return true;
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("Architecture/Runtime/annual-interest:" + extension.Id, ex);
			}
		}
		return false;
	}

	internal static void ExecuteAll(
		in XjAnnualSecondaryContext context,
		XjAnnualSecondaryStepRunner runner)
	{
		Seal();
		foreach (XjAnnualActorExtensionDescriptor extension in Extensions)
		{
			bool shouldExecute;
			try
			{
				shouldExecute = extension.ShouldExecute(context);
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("Architecture/Runtime/annual-execute-gate:" + extension.Id, ex);
				continue;
			}
			if (!shouldExecute) continue;

			XjAnnualSecondaryContext captured = context;
			runner?.Invoke(
				context.ActorId,
				context.AnnualYear,
				extension.Id,
				() => extension.Execute(captured));
		}
	}

	private static void Seal()
	{
		if (!_orderDirty) return;
		Extensions.Sort((left, right) =>
		{
			int order = left.Order.CompareTo(right.Order);
			return order != 0 ? order : string.CompareOrdinal(left.Id, right.Id);
		});
		_orderDirty = false;
	}
}
