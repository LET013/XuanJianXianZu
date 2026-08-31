using System;
using System.Collections.Generic;
using XuanJianVNext.Architecture.Bootstrap;
using XuanJianVNext.Architecture.Persistence;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Death;

namespace XuanJianVNext.Architecture;

/// <summary>
/// Small facade used by feature packages. Startup invokes each package registrar
/// once; the package then declares only the capabilities it owns without editing
/// scheduler internals or the central world archive.
/// </summary>
internal static class XjFeatureRegistration
{
	internal static void Module(
		string id,
		int order,
		Action initialize,
		IReadOnlyList<string> dependencies = null,
		Action clearRuntime = null)
	{
		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			id, order, initialize, dependencies, clearRuntime));
	}

	internal static void BackgroundLane(
		string id,
		int order,
		Func<bool> hasPending,
		Action tick,
		XjRuntimeLanePolicy policy)
	{
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(id, order, hasPending, tick, policy));
	}

	internal static void BackgroundLane(
		string id,
		int order,
		Func<bool> hasPending,
		Action<XjCooperativeBudget> tick,
		XjRuntimeLanePolicy policy)
	{
		XjBackgroundLaneRegistry.Register(new XjBackgroundLaneDescriptor(id, order, hasPending, tick, policy));
	}

	internal static void DeathLane(
		string id,
		int order,
		Func<bool> hasPending,
		Action<int, XjDeathActorResolver> tick)
	{
		XjDeathLaneRegistry.Register(new XjDeathLaneDescriptor(id, order, hasPending, tick));
	}

	internal static void WorldLifecycle(
		string id,
		int order,
		Action<int> loaded,
		Action cleared)
	{
		XjWorldLifecycleRegistry.Register(new XjWorldLifecycleDescriptor(id, order, loaded, cleared));
	}

	internal static void AnnualCultivationPath(
		string id,
		int order,
		Func<Actor, bool> matches,
		Action<Actor, int> prepare,
		Action<Actor, int> progress,
		Func<Actor, int> combatTier = null)
	{
		XjAnnualCultivationPathRegistry.Register(new XjAnnualCultivationPathDescriptor(
			id, order, matches, prepare, progress, combatTier));
	}

	internal static void AnnualActorExtension(
		string id,
		int order,
		Func<XjAnnualSecondaryContext, bool> hasQueueInterest,
		Func<XjAnnualSecondaryContext, bool> shouldExecute,
		Action<XjAnnualSecondaryContext> execute)
	{
		XjAnnualActorExtensionRegistry.Register(new XjAnnualActorExtensionDescriptor(
			id, order, hasQueueInterest, shouldExecute, execute));
	}

	internal static void ArchiveDocument(
		string moduleId,
		int order,
		int currentSchemaVersion,
		Func<string> exportPayload,
		Action<int, string> importPayload)
	{
		XjModuleArchiveRegistry.Register(new XjModuleArchiveContributor(
			moduleId, order, currentSchemaVersion, exportPayload, importPayload));
	}
}
