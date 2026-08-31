using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 服气玩法统一路由。各Handler只负责无境界阶段如何取得本命核心；
/// 黄冠以后统一进入XjFuQiCultivationSystem，避免再次复制性命双修状态机。
/// </summary>
internal static class XjFuQiCoreRouter
{
	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor)) return;
		if (XjCultivationPathTransitions.ReconcileFuQiDaoTuIdentity(actor))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string repairedDaoTu);
			XjVisibleTraitSync.SyncDaoTuTrait(actor, repairedDaoTu);
			XjRuntimeActorInterestIndex.Observe(actor);
		}
		if (!TryResolveActorCore(actor, out XjFuQiCoreDefinition definition) || !definition.GameplayImplemented) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiCoreId, out string storedCoreId);
		if (!string.Equals((storedCoreId ?? string.Empty).Trim(), definition.CoreId, StringComparison.Ordinal))
		{
			// 0.9.4.34曾把具体道途压成根类核心；旧档在既有年度入口原位迁移，
			// 例如“土德道气”恢复为“宝土道气”，不新增全人口扫描。
			XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCoreId, definition.CoreId);
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		XjAptitudeEffectRules.EnsureHuiGuangMinimumBoundToAptitude(actor, aptitude);
		XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, currentYear);
		XjFuQiMethodRules.ApplyAnnualEraAcceleration(actor, currentYear, definition.DaoTuRootId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
		if (string.IsNullOrWhiteSpace(normalizedRealm))
		{
			if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.Sword, StringComparison.Ordinal))
			{
				XjFuQiSwordDaoSystem.TickEntryActor(actor, currentYear);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.GenericDaoQi, StringComparison.Ordinal))
			{
				XjFuQiGenericDaoQiHandler.TickEntry(actor, currentYear, definition);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.NatalTalisman, StringComparison.Ordinal))
			{
				XjFuQiNatalTalismanHandler.TickEntry(actor, currentYear, definition);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.QingXuan, StringComparison.Ordinal))
			{
				XjFuQiQingXuanHandler.TickEntry(actor, currentYear, definition);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.HengZhu, StringComparison.Ordinal))
			{
				XjFuQiHengZhuHandler.TickEntry(actor, currentYear, definition);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.QuanDan, StringComparison.Ordinal))
			{
				XjFuQiQuanDanHandler.TickEntry(actor, currentYear, definition);
			}
			else if (XjFuQiBingGuCoreHandler.CanHandle(in definition))
			{
				XjFuQiBingGuCoreHandler.TickEntry(actor, currentYear, in definition);
			}
			return;
		}
		XjFuQiCultivationSystem.TickActor(actor, currentYear, definition);
	}

	internal static string BuildDisplaySummary(Actor actor)
	{
		if (!TryResolveActorCore(actor, out XjFuQiCoreDefinition definition) || !definition.GameplayImplemented) return string.Empty;
		int currentYear = XjAnnualExecutionContext.ResolveYear(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
		string detail;
		if (string.IsNullOrWhiteSpace(normalizedRealm))
		{
			if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.Sword, StringComparison.Ordinal))
			{
				detail = XjFuQiSwordDaoSystem.BuildEntryDisplaySummary(actor);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.GenericDaoQi, StringComparison.Ordinal))
			{
				detail = XjFuQiGenericDaoQiHandler.BuildEntrySummary(actor, definition, currentYear);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.NatalTalisman, StringComparison.Ordinal))
			{
				detail = XjFuQiNatalTalismanHandler.BuildEntrySummary(actor, definition, currentYear);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.QingXuan, StringComparison.Ordinal))
			{
				detail = XjFuQiQingXuanHandler.BuildEntrySummary(actor, definition, currentYear);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.HengZhu, StringComparison.Ordinal))
			{
				detail = XjFuQiHengZhuHandler.BuildEntrySummary(actor, definition, currentYear);
			}
			else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.QuanDan, StringComparison.Ordinal))
			{
				detail = XjFuQiQuanDanHandler.BuildEntrySummary(actor, definition, currentYear);
			}
			else if (XjFuQiBingGuCoreHandler.CanHandle(in definition))
			{
				detail = XjFuQiBingGuCoreHandler.BuildEntrySummary(actor, in definition, currentYear);
			}
			else
			{
				detail = string.Empty;
			}
		}
		else
		{
			detail = XjFuQiCultivationSystem.BuildDisplaySummary(actor, definition, currentYear);
		}
		return XjFuQiMethodRules.BuildDisplaySummary(actor, definition, currentYear, detail);
	}

	internal static bool TryResolveActorCore(Actor actor, out XjFuQiCoreDefinition definition)
	{
		definition = default;
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiDaoTuRootId, out string rootId)
			&& XjFuQiCoreCatalog.TryGetByRootId(rootId, out XjFuQiCoreDefinition rootDefinition))
		{
			definition = XjFuQiCoreCatalog.SpecializeForDaoTu(in rootDefinition, daoTu);
			return true;
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiLineageId, out string lineage)
			&& string.Equals(lineage, XjFuQiLineageIds.Sword, StringComparison.Ordinal))
		{
			return XjFuQiCoreCatalog.TryGetByRootId(XjDaoTuRootIds.LongGeng, out definition);
		}
		if (!string.IsNullOrWhiteSpace(daoTu))
		{
			return XjFuQiCoreCatalog.TryResolveByDaoTu(daoTu, out definition);
		}
		return false;
	}

	internal static void InitializeActorCore(Actor actor, string daoTuRootId)
	{
		if (actor?.data == null || !XjFuQiCoreCatalog.TryGetByRootId(daoTuRootId, out XjFuQiCoreDefinition definition)) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		definition = XjFuQiCoreCatalog.SpecializeForDaoTu(in definition, daoTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiDaoTuRootId, definition.DaoTuRootId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCoreType, definition.CoreTypeId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCoreId, definition.CoreId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiEraLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, 0);
	}

	internal static void ClearActorCore(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiDaoTuRootId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCoreType, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCoreId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiEraLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, 0);
	}
}
