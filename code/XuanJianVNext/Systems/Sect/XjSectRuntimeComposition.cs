using XuanJianVNext.Architecture;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Sect runtime composition boundary. World lifecycle and the bounded Sect
/// background lane live with the feature rather than inside native-event or
/// scheduler composition.
/// </summary>
internal static class XjSectRuntimeComposition
{
	private const int BackgroundVisitBudget = 5;
	private static bool _initialized;

	internal static void Initialize()
	{
		if (_initialized) return;
		_initialized = true;
		XjFeatureRegistration.WorldLifecycle(
			"world.sect",
			20,
			XjSectRuntimeLane.InitializeAfterLoad,
			cleared: null);
		XjFeatureRegistration.BackgroundLane(
			"background.sect",
			60,
			() => !XjWorldBootstrapLane.HasPending && XjSectRuntimeLane.HasPending,
			TickBackground,
			new XjRuntimeLanePolicy(
				BackgroundVisitBudget,
				0.85d,
				XjRuntimeBacklogPolicy.CoalesceLatest,
				XjRuntimeYearSemantics.LatestPeriod));
	}

	internal static void ClearRuntime()
	{
		// Sect owns its own runtime teardown. Keep feature-local state out of the
		// global cache registry so resets cannot silently grow another god method.
		XjSectRuntimeLane.Clear();
		XjSectLectureSystem.Clear();
		XjSectTaskSystem.Clear();
		XjSectTransmissionCoverageSystem.Clear();
		XjSectGongFaPavilion.Clear();
		XjSectCaiQiWarehouse.Clear();
		XjSectKnowledgeWriter.Clear();
		XjSectCultivatorCityIndex.Clear();
		XjSectCityData.ClearRuntimeCache();
		XjNationSectRebellionGuard.ClearRuntimeState();
		XjSectFormationOccupationAdapter.Clear();
		XjSectFormationRegistry.Clear();
		XjSectWarSystem.Clear();
		XjSectRepository.Clear();
	}

	private static void TickBackground(XjCooperativeBudget budget)
	{
		long sample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.BackgroundZongMen, 0);
		XjSectRuntimeLane.Tick(budget);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.BackgroundZongMen, sample);
	}
}
