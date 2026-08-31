using System.Collections.Generic;
using XuanJianVNext.Architecture.Bootstrap;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Architecture.Runtime;

/// <summary>
/// Release-time guardrails for the runtime architecture introduced during the
/// 0.9.9.7 -> 1.0 stabilization passes. It validates registration/ownership
/// invariants only; it never scans world entities.
/// </summary>
internal static class XjRuntimeArchitectureInvariantAudit
{
	private static bool _ran;

	internal static void RunAfterBootstrap()
	{
		if (_ran) return;
		_ran = true;

		XjScheduler.EnsureRuntimeLaneRegistration();
		List<string> issues = new List<string>();
		XjFeatureModuleCatalog.CollectInvariantIssues(issues);
		XjBackgroundLaneRegistry.CollectInvariantIssues(issues);
		XjUnifiedDetectionRuntime.CollectInvariantIssues(issues);
		XjPerformanceTelemetry.ObserveQueue("architectureInvariantIssues", issues.Count);

		if (issues.Count == 0)
		{
			UnityEngine.Debug.Log(
				"[玄鉴][架构自检] OK | "
				+ XjFeatureModuleCatalog.BuildSummary() + " | "
				+ XjBackgroundLaneRegistry.BuildSummary() + " | detection "
				+ XjUnifiedDetectionRuntime.BuildSummary());
			return;
		}

		for (int i = 0; i < issues.Count; i++)
		{
			UnityEngine.Debug.LogWarning("[玄鉴][架构自检] " + issues[i]);
		}
	}
}
