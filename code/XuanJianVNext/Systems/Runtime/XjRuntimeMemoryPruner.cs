using XuanJianVNext.Core;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// 只在年度世界流水线末尾运行的内存收口器。它不删除玩法状态，
/// 只裁剪已受容量保护的数据、释放可重建快照，并定期重建裁剪后的容器。
/// </summary>
internal static class XjRuntimeMemoryPruner
{
	private const int SoftCleanupIntervalYears = 5;
	private const int HardCompactIntervalYears = 25;

	internal static void RunAnnual(int currentYear)
	{
		if (currentYear <= 0 || currentYear % SoftCleanupIntervalYears != 0) return;

		XjActorRegistry.CleanupInvalid(256, XjCultivatorCache.Remove);
		XjActorRegistry.ReleaseSnapshotCache();
		XjCodexSnapshotPublisher.ReleaseSnapshotIfClosed();

		bool hardCompact = currentYear % HardCompactIntervalYears == 0;
		XjPersonalBiographyStore.CompactMemory(hardCompact);
		XjFamilyChronicleBookStore.CompactMemory(hardCompact);
		XjSectChronicleStore.CompactMemory(hardCompact);
		XjThreeBookDeferredFamilyFacts.CompactMemory();

		if (hardCompact)
		{
			XjWorldHistoryStore.PruneSuppressedRecords();
			XjWorldHistoryStore.CompactMemory();
		}
	}
}
