using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 服气资格只在修炼身份首次确定时判定一次。五、六档沿既有入口；四档仅当
/// 先天命数与道慧同时处于高位时破格。直系传承只提高概率，不会形成年度重抽。
/// 一旦进入服气养性便立即写为感气成功；不存在年度感气进度或等待期。
/// </summary>
internal static class XjFuQiSensingSystem
{
	internal const int ResultPending = 0;
	internal const int ResultSuccess = 1;
	internal const int ResultFailure = 2;

	internal static bool ResolveOnce(Actor actor, string daoTuOrRoot, int aptitude, out bool sensed)
	{
		sensed = false;
		if (actor?.data == null
			|| !XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)
			|| !definition.SupportsFuQi)
		{
			return false;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiSenseResult, out int stored)
			&& (stored == ResultSuccess || stored == ResultFailure)
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiSensedQiId, out string storedRoot)
			&& string.Equals((storedRoot ?? string.Empty).Trim(), definition.RootId, StringComparison.Ordinal))
		{
			sensed = stored == ResultSuccess;
			return true;
		}

		int currentYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
		long actorId = ((BaseSystemData)actor.data).id;
		bool forceSense = false;
		try { forceSense = actor.hasTrait("XjLongGengDaoTong"); } catch (System.Exception xjCaught42) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjFuQiSensingSystem.cs:42", xjCaught42); }

		float chance = forceSense ? 1f : ResolveSenseChance(actor, definition.RootId, aptitude);
		sensed = chance > 0f
			&& actorId > 0L
			&& XjDeterministicHash.Roll01(actorId, Math.Max(1, currentYear), "fuqi_sense_once", definition.RootId) < chance;

		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiSensedQiId, definition.RootId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseYear, Math.Max(0, currentYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseResult, sensed ? ResultSuccess : ResultFailure);
		return true;
	}

	internal static bool MarkPathEntrySuccess(Actor actor, string daoTuOrRoot)
	{
		if (actor?.data == null
			|| !XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)
			|| !definition.SupportsFuQi)
		{
			return false;
		}

		int currentYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiSensedQiId, definition.RootId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseYear, Math.Max(0, currentYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseResult, ResultSuccess);
		return true;
	}

	internal static bool EnsureEnteredPathSuccess(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor)) return false;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiDaoTuRootId, out string rootId)
			&& MarkPathEntrySuccess(actor, rootId)) return true;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& MarkPathEntrySuccess(actor, daoTu)) return true;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiLineageId, out string lineage)
			&& string.Equals(lineage, XjFuQiLineageIds.Sword, StringComparison.Ordinal))
		{
			return MarkPathEntrySuccess(actor, XjDaoTuRootIds.LongGeng);
		}
		return false;
	}

	internal static float ResolveSenseChance(Actor actor, string daoTuRootId, int aptitude)
	{
		if (!XjFuQiAptitudeRules.CanSenseDaoQi(aptitude)) return 0f;

		float huiGuangFactor = XjDaoHuiPolicy.Normalize01(XjTalentOpportunityRules.ReadHuiGuang(actor));
		// 感气属于天资分流，只读先天命数；后天积命不反向改变出生时的感气资性。
		float mingShuFactor = Math.Clamp(XjTalentOpportunityRules.ReadMingShu(actor) / 100f, 0f, 1f);
		return XjFuQiBalancePolicy.ResolveSenseChance(
			aptitude,
			huiGuangFactor,
			mingShuFactor,
			HasDirectFuQiParentOnRoot(actor, daoTuRootId));
	}

	private static bool HasDirectFuQiParentOnRoot(Actor actor, string rootId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(rootId)) return false;
		return IsMatchingParent(actor.data.parent_id_1, rootId)
			|| IsMatchingParent(actor.data.parent_id_2, rootId);
	}

	private static bool IsMatchingParent(long actorId, string rootId)
	{
		if (actorId <= 0L
			|| !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor parent)
			|| !XjCultivationPathRules.IsFuQiYangXing(parent)) return false;
		if (XjActorAccessor.TryGetString(parent, XjActorDataKeys.FuQiDaoTuRootId, out string storedRoot)
			&& string.Equals((storedRoot ?? string.Empty).Trim(), rootId, StringComparison.Ordinal)) return true;
		return XjActorAccessor.TryGetString(parent, XjActorDataKeys.DaoTu, out string daoTu)
			&& XjDaoTuCatalog.TryResolveRootId(daoTu, out string parentRoot)
			&& string.Equals(parentRoot, rootId, StringComparison.Ordinal);
	}
}
