using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 特质编辑器到释修权威状态的唯一桥接。今古释与僧侣—摩诃可用于中低境补录；
/// 法相、世尊只允许由权威高境状态机投影，任何直接赋予都会被撤回。
/// </summary>
internal static class XjShiTraitReconciliation
{
	private static bool _isReconciling;

	internal static void HandleGranted(Actor actor, string traitId, bool result)
	{
		if (_isReconciling
			|| XjCultivationStateTransitions.IsVisibleTraitSyncActive
			|| actor?.data == null
			|| string.IsNullOrWhiteSpace(traitId)
			|| (!result && !actor.hasTrait(traitId))
			|| !XjShiVisibleTraitSync.IsShiTrait(traitId))
		{
			return;
		}

		// 法相、世尊只能由真实高境状态机写入。直接赋予 XjRealm25/26 只撤销
		// 可见投影，不再调用 TryApplyManualRealmRecord 生成金地、位次或世尊席位。
		if (XjManualHighRealmGrantPolicy.IsProtectedHighRealmTrait(traitId))
		{
			try
			{
				_isReconciling = true;
				if (actor.hasTrait(traitId)) actor.removeTrait(traitId);
				if (XjCultivationPathRules.IsShi(actor)) XjShiVisibleTraitSync.Sync(actor);
			}
			finally
			{
				_isReconciling = false;
			}
			return;
		}

		// 这里只收束“特质编辑器手动赋予古释”的玩法：已有其他道途时拒绝手动挂古释。
		// 古释的首批种子、遗经自悟等自动诞生入口不经过这条人工转换限制；
		// 若权威状态已经是古释，后续可见特质同步也必须正常放行。
		if (XjShiCatalog.TryResolveTraditionByTrait(traitId, out string requestedTradition)
			&& string.Equals(requestedTradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			&& XjCultivationPathRules.TryGetPath(actor, out _)
			&& (!XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot existingAncient)
				|| !string.Equals(existingAncient.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)))
		{
			try
			{
				_isReconciling = true;
				if (actor.hasTrait(traitId)) actor.removeTrait(traitId);
				if (XjCultivationPathRules.IsShi(actor)) XjShiVisibleTraitSync.Sync(actor);
			}
			finally
			{
				_isReconciling = false;
			}
			return;
		}

		XjCultivationEligibility.RecordManualCultivationGrant(actor);
		try
		{
			_isReconciling = true;
			int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
			bool applied = false;
			if (XjShiCatalog.TryResolveTraditionByTrait(traitId, out string tradition))
			{
				applied = XjShiState.TryApplyManualTraditionRecord(actor, tradition, year);
			}
			else if (XjShiCatalog.TryResolveRealmByTrait(traitId, out string realm))
			{
				applied = XjShiState.TryApplyManualRealmRecord(actor, realm, year);
			}

			if (!applied)
			{
				if (actor.hasTrait(traitId)) actor.removeTrait(traitId);
				if (XjCultivationPathRules.IsShi(actor)) XjShiVisibleTraitSync.Sync(actor);
			}
		}
		finally
		{
			_isReconciling = false;
		}
	}

	internal static void HandleRemoved(Actor actor, string traitId, bool removed)
	{
		if (!removed
			|| _isReconciling
			|| XjCultivationStateTransitions.IsVisibleTraitSyncActive
			|| actor?.data == null
			|| !XjShiVisibleTraitSync.IsShiTrait(traitId)
			|| !XjCultivationPathRules.IsShi(actor))
		{
			return;
		}

		try
		{
			_isReconciling = true;
			// 可见特质是只读投影，删除投影不能抹掉完整修炼体系。
			XjShiVisibleTraitSync.Sync(actor);
		}
		finally
		{
			_isReconciling = false;
		}
	}
}
