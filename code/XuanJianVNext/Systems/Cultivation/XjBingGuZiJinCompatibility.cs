using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 并古九途拥有自己的采气地、先天之气与资源图标。紫府金丹修法直接
/// 消费同名并古之气，不再借五德或十二炁作为技术代理。
/// 青宣是特殊例外：感气失败者先修宣土，待青宣空证真君真正成立后才开放青宣之气。
/// </summary>
internal static class XjBingGuZiJinCompatibility
{
	internal static bool TryResolveZiJinEntryDaoTu(Actor actor, string daoTu, out string ziJinDaoTu)
	{
		ziJinDaoTu = (daoTu ?? string.Empty).Trim();
		if (!XjDaoTuCatalog.TryResolve(ziJinDaoTu, out XjDaoTuDefinition definition) || !definition.IsBingGu) return ziJinDaoTu.Length > 0;
		if (string.Equals(definition.RootId, XjDaoTuRootIds.QingXuan, StringComparison.Ordinal))
		{
			ziJinDaoTu = XjQingXuanKongZhengSystem.SourceDaoTu;
			return true;
		}
		return definition.SupportsZiJin && XjDaoTuManifestRegistry.CanManifestZiJin(definition.RootId);
	}

	internal static bool TryResolveCaiQiProxyDaoTu(Actor actor, string daoTuOrRoot, out string proxyDaoTu)
	{
		proxyDaoTu = string.Empty;
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition) || !definition.IsBingGu) return false;
		if (string.Equals(definition.RootId, XjDaoTuRootIds.QingXuan, StringComparison.Ordinal)
			&& !XjDaoTuManifestRegistry.IsCaiQiUnlocked(definition.RootId))
		{
			proxyDaoTu = XjQingXuanKongZhengSystem.SourceDaoTu;
			return true;
		}

		proxyDaoTu = definition.DisplayName;
		return !string.IsNullOrWhiteSpace(proxyDaoTu);
	}

	internal static bool TryResolveCaiQiProxyResourceId(Actor actor, string daoTuOrRoot, out string resourceId)
	{
		resourceId = string.Empty;
		return TryResolveCaiQiProxyDaoTu(actor, daoTuOrRoot, out string proxyDaoTu)
			&& XjCaiQiCatalog.TryGetEntryByDisplayName(proxyDaoTu, out XjCaiQiCatalogEntry entry)
			&& XjCaiQiCatalog.TryGetOldResourceIdByBranchId(entry.BranchId, out resourceId)
			&& !string.IsNullOrWhiteSpace(resourceId);
	}

	internal static bool ShouldPreserveCurrentDaoTu(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& XjDaoTuCatalog.TryResolve(daoTu, out XjDaoTuDefinition definition)
			&& definition.IsBingGu
			&& !string.Equals(definition.RootId, XjDaoTuRootIds.QingXuan, StringComparison.Ordinal);
	}
}
