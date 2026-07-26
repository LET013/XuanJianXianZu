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
		if (actor?.data == null || currentYear <= 0) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		int offset = XjDeterministicHash.PositiveIndex(actorId, "zifu_lingwu_opportunity_interval", IntervalYears);
		return (currentYear + offset) % IntervalYears == 0;
	}

	internal static void TryGrant(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || !actor.isAlive() || !IsDue(actor, currentYear)) return;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!string.Equals(realmId, XjRealmIds.ZiFu, System.StringComparison.Ordinal)) return;

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
		if (XjDeterministicHash.PositiveIndex(actorId + currentYear, salt, 10000) >= ChancePerTenThousand)
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
}
