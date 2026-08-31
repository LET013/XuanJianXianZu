using System;
using System.Collections.Generic;
using XuanJianVNext.Core;

namespace XuanJianVNext.Architecture.Bootstrap;

/// <summary>
/// A coarse-grained feature boundary. New feature packages register one module
/// descriptor instead of adding another call to the mod entry point.
/// </summary>
internal sealed class XjFeatureModuleDescriptor
{
	internal string Id { get; }
	internal int Order { get; }
	internal IReadOnlyList<string> Dependencies { get; }
	internal Action Initialize { get; }
	internal Action ClearRuntime { get; }

	internal XjFeatureModuleDescriptor(
		string id,
		int order,
		Action initialize,
		IReadOnlyList<string> dependencies = null,
		Action clearRuntime = null)
	{
		Id = string.IsNullOrWhiteSpace(id)
			? throw new ArgumentException("Module id is required.", nameof(id))
			: id.Trim();
		Order = order;
		Initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
		Dependencies = dependencies ?? Array.Empty<string>();
		ClearRuntime = clearRuntime;
	}
}

internal enum XjFeatureModuleState : byte
{
	Registered = 0,
	Initializing = 1,
	Ready = 2,
	Failed = 3
}

/// <summary>
/// Deterministic dependency-aware module catalog. It deliberately does not use
/// reflection or assembly scanning: registrations are explicit, cheap and easy
/// to audit in a WorldBox mod environment.
/// </summary>
internal static class XjFeatureModuleCatalog
{
	private static readonly Dictionary<string, XjFeatureModuleDescriptor> Modules =
		new Dictionary<string, XjFeatureModuleDescriptor>(StringComparer.Ordinal);
	private static readonly Dictionary<string, XjFeatureModuleState> States =
		new Dictionary<string, XjFeatureModuleState>(StringComparer.Ordinal);
	private static readonly List<XjFeatureModuleDescriptor> InitializationOrder =
		new List<XjFeatureModuleDescriptor>();
	private static bool _initializationStarted;
	private static bool _initialized;

	internal static bool IsInitialized => _initialized;
	internal static int Count => Modules.Count;

	internal static bool IsModuleReady(string id)
	{
		string normalized = (id ?? string.Empty).Trim();
		return normalized.Length > 0
			&& States.TryGetValue(normalized, out XjFeatureModuleState state)
			&& state == XjFeatureModuleState.Ready;
	}

	internal static void Register(XjFeatureModuleDescriptor descriptor)
	{
		if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
		if (_initializationStarted)
		{
			throw new InvalidOperationException("Cannot register feature module after initialization has started: " + descriptor.Id);
		}
		if (Modules.ContainsKey(descriptor.Id))
		{
			throw new InvalidOperationException("Duplicate feature module id: " + descriptor.Id);
		}

		Modules.Add(descriptor.Id, descriptor);
		States.Add(descriptor.Id, XjFeatureModuleState.Registered);
	}

	internal static void InitializeAll()
	{
		if (_initialized) return;
		_initializationStarted = true;
		ValidateDependencies();

		List<XjFeatureModuleDescriptor> ordered = BuildInitializationOrder();
		foreach (XjFeatureModuleDescriptor module in ordered)
		{
			States[module.Id] = XjFeatureModuleState.Initializing;
			try
			{
				module.Initialize();
				States[module.Id] = XjFeatureModuleState.Ready;
				InitializationOrder.Add(module);
				UnityEngine.Debug.Log("[玄鉴][模块] ready: " + module.Id);
			}
			catch (Exception ex)
			{
				States[module.Id] = XjFeatureModuleState.Failed;
				XjExceptionDiagnostics.Report("Architecture/Bootstrap/module:" + module.Id, ex);
				throw new InvalidOperationException("玄鉴模块初始化失败: " + module.Id, ex);
			}
		}

		_initialized = true;
	}

	/// <summary>
	/// World/runtime reset extension point. Existing legacy resets remain where
	/// they are; new modules can own their cleanup without editing XjScheduler.
	/// </summary>
	internal static void ClearRegisteredRuntimeState()
	{
		for (int index = InitializationOrder.Count - 1; index >= 0; index--)
		{
			XjFeatureModuleDescriptor module = InitializationOrder[index];
			if (module.ClearRuntime == null) continue;
			try
			{
				module.ClearRuntime();
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("Architecture/Bootstrap/reset:" + module.Id, ex);
			}
		}
	}

	internal static void CollectInvariantIssues(List<string> issues)
	{
		if (issues == null) return;
		if (!_initialized) issues.Add("FeatureModuleCatalog 尚未完成初始化");
		if (InitializationOrder.Count != Modules.Count)
		{
			issues.Add("FeatureModule 初始化序列数量与注册数量不一致：order=" + InitializationOrder.Count + ", registered=" + Modules.Count);
		}
		foreach (XjFeatureModuleDescriptor module in Modules.Values)
		{
			if (!States.TryGetValue(module.Id, out XjFeatureModuleState state) || state != XjFeatureModuleState.Ready)
			{
				issues.Add("FeatureModule 未处于 Ready：" + module.Id + " state=" + state);
			}
			if (module.Id.StartsWith("runtime.", StringComparison.Ordinal) && module.ClearRuntime == null)
			{
				issues.Add("运行态 FeatureModule 缺少 ClearRuntime ownership：" + module.Id);
			}
		}

		string[] requiredRuntimeOwners =
		{
			"runtime.performance",
			"runtime.family",
			"runtime.events",
			"runtime.sect",
			"runtime.shi",
			"runtime.doctrine",
			"runtime.xianguo"
		};
		for (int i = 0; i < requiredRuntimeOwners.Length; i++)
		{
			if (!Modules.ContainsKey(requiredRuntimeOwners[i]))
			{
				issues.Add("缺少发布前必须存在的运行态 ownership 模块：" + requiredRuntimeOwners[i]);
			}
		}
	}

	internal static string BuildSummary()
	{
		int ready = 0;
		int failed = 0;
		foreach (XjFeatureModuleState state in States.Values)
		{
			if (state == XjFeatureModuleState.Ready) ready++;
			else if (state == XjFeatureModuleState.Failed) failed++;
		}
		return "registered=" + Modules.Count + ", ready=" + ready + ", failed=" + failed;
	}

	private static void ValidateDependencies()
	{
		foreach (XjFeatureModuleDescriptor module in Modules.Values)
		{
			foreach (string dependency in module.Dependencies)
			{
				if (string.IsNullOrWhiteSpace(dependency) || !Modules.ContainsKey(dependency.Trim()))
				{
					throw new InvalidOperationException(
						"Module " + module.Id + " depends on missing module " + (dependency ?? "<null>"));
				}
			}
		}
	}

	private static List<XjFeatureModuleDescriptor> BuildInitializationOrder()
	{
		List<XjFeatureModuleDescriptor> candidates = new List<XjFeatureModuleDescriptor>(Modules.Values);
		candidates.Sort(CompareModules);
		List<XjFeatureModuleDescriptor> result = new List<XjFeatureModuleDescriptor>(candidates.Count);
		Dictionary<string, byte> visit = new Dictionary<string, byte>(StringComparer.Ordinal);
		foreach (XjFeatureModuleDescriptor module in candidates)
		{
			Visit(module, visit, result);
		}
		return result;
	}

	private static void Visit(
		XjFeatureModuleDescriptor module,
		Dictionary<string, byte> visit,
		List<XjFeatureModuleDescriptor> result)
	{
		if (visit.TryGetValue(module.Id, out byte state))
		{
			if (state == 2) return;
			if (state == 1) throw new InvalidOperationException("Feature module dependency cycle at: " + module.Id);
		}

		visit[module.Id] = 1;
		List<XjFeatureModuleDescriptor> dependencies = new List<XjFeatureModuleDescriptor>();
		foreach (string dependencyId in module.Dependencies)
		{
			dependencies.Add(Modules[dependencyId.Trim()]);
		}
		dependencies.Sort(CompareModules);
		foreach (XjFeatureModuleDescriptor dependency in dependencies)
		{
			Visit(dependency, visit, result);
		}
		visit[module.Id] = 2;
		if (!result.Contains(module)) result.Add(module);
	}

	private static int CompareModules(XjFeatureModuleDescriptor left, XjFeatureModuleDescriptor right)
	{
		int order = left.Order.CompareTo(right.Order);
		return order != 0 ? order : string.CompareOrdinal(left.Id, right.Id);
	}
}
