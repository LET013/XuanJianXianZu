using System;
using XuanJianVNext.Architecture.Presentation;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Interop;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Doctrine;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Performance;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.YaoShu;
using XuanJianVNext.UI;
using XuanJianVNext.Traits;

namespace XuanJianVNext.Architecture.Bootstrap;

/// <summary>
/// The only composition root for startup. Feature implementations stay in their
/// owner namespaces; dependency order lives here instead of InterestingTrait.cs.
/// </summary>
internal static class XjModBootstrap
{
	private static bool _registered;

	internal static void Initialize(string patchOwnerId, Action loadRuntimeSettings)
	{
		if (!_registered)
		{
			RegisterBuiltInModules(patchOwnerId, loadRuntimeSettings);
			_registered = true;
		}
		XjFeatureModuleCatalog.InitializeAll();
		XjRuntimeArchitectureInvariantAudit.RunAfterBootstrap();
		UnityEngine.Debug.Log("[玄鉴][模块] initialization completed: " + XjFeatureModuleCatalog.BuildSummary());
	}

	private static void RegisterBuiltInModules(string patchOwnerId, Action loadRuntimeSettings)
	{
		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"core.localization",
			10,
			XjLocalizationRuntimeMarker.MarkLoaded));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"interop.worldbox-patches",
			20,
			() =>
			{
				if (!XjWorldBoxPatchCatalog.ApplyAll(patchOwnerId))
				{
					throw new InvalidOperationException(
						"玄鉴核心运行补丁未能完整安装：" + XjWorldBoxPatchCatalog.Summary);
				}
				UnityEngine.Debug.Log("[玄鉴][补丁] " + XjWorldBoxPatchCatalog.Summary);
			},
			new[] { "core.localization" }));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"core.safe-runtime",
			30,
			XjSafeCore.Init,
			new[] { "interop.worldbox-patches" }));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"core.runtime-settings",
			40,
			loadRuntimeSettings ?? (() => { }),
			new[] { "core.safe-runtime" }));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"runtime.performance",
			45,
			XjPerformanceBenchmarkInstaller.Initialize,
			new[] { "core.runtime-settings" },
			XjPerformanceBenchmarkInstaller.Clear));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"content.assets",
			50,
			() =>
			{
				XjVNextAssetRegistration.Init();
				XjAlchemyCatalog.Init();
				XjAlchemyStatusAssets.Init();
				XjLongShuSystem.Init();
				XjYaoShuGreatSageSystem.Init();
				XjLongShuDongTianSystem.Init();
				XjWorldHistoryRegistry.Init();
			},
			new[] { "runtime.performance" }));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"world.eras",
			60,
			XjEraAssetRegistration.Init,
			new[] { "content.assets" }));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"world.entities",
			70,
			XjDongTianEntitySystem.Init,
			new[] { "world.eras" }));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"runtime.family",
			72,
			XjFamilyRuntimeComposition.Initialize,
			new[] { "world.entities" },
			XjFamilyRuntimeComposition.ClearRuntime));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"runtime.events",
			73,
			XjWorldEventRuntimeComposition.Initialize,
			new[] { "world.entities" },
			XjWorldEventRuntimeComposition.ClearRuntime));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"runtime.sect",
			74,
			XjSectRuntimeComposition.Initialize,
			new[] { "runtime.family" },
			XjSectRuntimeComposition.ClearRuntime));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"runtime.shi",
			75,
			XjShiRuntimeComposition.Initialize,
			new[] { "world.entities" },
			XjShiRuntimeComposition.ClearRuntime));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"runtime.doctrine",
			76,
			XjDoctrineRuntimeComposition.Initialize,
			new[] { "runtime.sect", "runtime.shi" },
			XjDoctrineRuntimeComposition.ClearRuntime));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"runtime.xianguo",
			77,
			XjXianGuoRuntimeComposition.Initialize,
			new[] { "runtime.sect", "runtime.shi", "runtime.doctrine" },
			XjXianGuoRuntimeComposition.ClearRuntime));

		XjFeatureModuleCatalog.Register(new XjFeatureModuleDescriptor(
			"ui.main",
			80,
			() =>
			{
				XjUiPresentationBridge.Register();
				XjVNextUiManager.Init();
				XjPresentationHooks.SetFpsOverlayEnabled(XjRuntimeSettings.ShowFpsOverlayEnabled);
			},
			new[] { "runtime.sect", "runtime.shi", "runtime.doctrine", "runtime.xianguo" }));
	}
}
