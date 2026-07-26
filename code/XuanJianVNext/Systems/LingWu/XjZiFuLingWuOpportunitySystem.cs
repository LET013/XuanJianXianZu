using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.LingWu;

/// <summary>
/// 紫府五年一次的灵物机缘。只在家族缺少对应紫府灵物时判定，
/// 作为本命灵宝素材的稀缺兜底；金丹不进入此事件。
/// </summary>
internal static class XjZiFuLingWuOpportunitySystem
{
	internal const int IntervalYears = 5;
	internal const int ChancePerTenThousand = 1000; // 每五年判定一次，单次10%。

	internal static bool IsDue(Actor actor, int currentYear)
	{
		return TryResolveDueYear(actor, currentYear, out _);
	}

	internal static void TryGrant(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || !actor.isAlive()) return;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!string.Equals(realmId, XjRealmIds.ZiFu, System.StringComparison.Ordinal)
			|| !TryResolveDueYear(actor, currentYear, out int opportunityYear)
			|| !XjProgressionOpportunityClock.HasExecutionSlot(
				actor, XjActorDataKeys.XjZiFuLingWuLastExecutionYear, currentYear)) return;

		XjProgressionOpportunityClock.MarkExecuted(
			actor, XjActorDataKeys.XjZiFuLingWuLastExecutionYear, currentYear);
		XjProgressionOpportunityClock.ConsumeIntervalDueYear(
			actor,
			XjActorDataKeys.XjZiFuLingWuNextOpportunityYear,
			opportunityYear,
			IntervalYears);
		XjStageZeroObservation.RecordOpportunityDebtConsumed("ZiFuLingWu", opportunityYear, currentYear);

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyStableId)
			|| familyStableId <= 0L
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu)
			|| !XjLingWuCatalog.TryResolveByDaoTu(daoTu, out XjLingWuDef definition)
			|| XjArtifactForgeFuel.HasZiFuForgeFuel(actor, daoTu))
		{
			return;
		}

		string salt = "zifu_lingwu_opportunity|" + familyStableId + "|" + daoTu;
		if (XjDeterministicHash.PositiveIndex(actorId + opportunityYear, salt, 10000) >= ChancePerTenThousand)
		{
			return;
		}

		string actorName = actor.getName() ?? "紫府修士";
		if (!XjFamilyLingWuWarehouse.TryAddLingWu(
			familyStableId,
			definition,
			actorId,
			actorName,
			currentYear))
		{
			return;
		}

		XjThreeBookWriter.RecordZiFuLingWuOpportunity(actor, definition, currentYear);
		string body = actorName + "静修紫府时偶得天地灵机，于道痕汇聚处寻得“" + definition.Name
			+ "”，已收入家族重宝仓库。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Cultivation,
			"紫府得灵",
			body,
			2,
			actorId: actorId,
			actorName: actorName,
			familyId: familyStableId,
			year: currentYear,
			iconIdOverride: XjEventIconCatalog.LingWuAppear,
			eventType: "ZiFuLingWuOpportunity",
			mirrorToWorldLog: false);
		XjBroadcastSystem.BroadcastBLevelWorldEvent("【紫府得灵】" + body, XjEventIconCatalog.LingWuAppear);
	}
	private static bool TryResolveDueYear(Actor actor, int currentYear, out int dueYear)
	{
		dueYear = 0;
		if (actor?.data == null || currentYear <= 0) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;

		int offset = XjDeterministicHash.PositiveIndex(actorId, "zifu_lingwu_opportunity_interval", IntervalYears);
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuLingWuNextOpportunityYear, out int nextDueYear)
			|| nextDueYear <= 0)
		{
			// 旧档没有可证明的历史灵物判定年份，首次迁移从当前年开始建钟，
			// 不按紫府进入年凭空补发已经无法核验的历史机会。之后所有跳年都会保债。
			int baseline = currentYear;
			nextDueYear = Math.Max(1, baseline);
			for (int i = 0; i < IntervalYears; i++, nextDueYear++)
			{
				if ((nextDueYear + offset) % IntervalYears == 0) break;
			}
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuLingWuNextOpportunityYear, nextDueYear);
		}

		dueYear = nextDueYear;
		return dueYear <= currentYear;
	}

}
