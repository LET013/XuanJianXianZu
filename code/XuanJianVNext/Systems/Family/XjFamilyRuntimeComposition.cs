using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Architecture;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// Family-owned world/runtime state. Keeping these clears behind one feature
/// boundary prevents XjCacheRegistry from becoming a second domain composition
/// root and makes reset ownership auditable.
/// </summary>
internal static class XjFamilyRuntimeComposition
{
	internal static void Initialize()
	{
		// Family indexes are mutation-driven and require no eager startup work.
		XjFeatureRegistration.ArchiveDocument(
			XjSongXuanEasterEggSystem.ModuleId,
			66,
			2,
			XjSongXuanEasterEggSystem.ExportPayload,
			XjSongXuanEasterEggSystem.ImportPayload);
		XjFeatureRegistration.ArchiveDocument(
			XjZhangYanEasterEggSystem.ModuleId,
			66,
			1,
			XjZhangYanEasterEggSystem.ExportPayload,
			XjZhangYanEasterEggSystem.ImportPayload);
	}

	internal static void ClearRuntime()
	{
		// Module-level teardown owns shared derived caches. Lower authority containers
		// only clear their own state so one world reset does not fan out the same cache
		// invalidation through multiple layers.
		XjFamilyReadModel.Shared.ClearCache();
		XjFamilyBloodlineAggregateCache.Clear();
		XjDaoTuHeritageService.Clear();
		XjFamilyIdentityIndex.Clear();
		XjFamilyMemberIndex.Shared.Clear();
		XjFamilyMemberLedger.Clear();
		XjFamilySurnameRegistry.Clear();
		XjFamilySurnamePolicy.ClearRuntimeCache();
		XjSongXuanEasterEggSystem.ClearRuntime();
		XjZhangYanEasterEggSystem.ClearRuntime();
		XjFamilyCaiQiWarehouse.Clear();
		XjFamilyGongFaWarehouse.Clear();
		XjFamilyFaBaoWarehouse.Clear();
		XjFamilyWarehouseWriter.Clear();
	}
}
