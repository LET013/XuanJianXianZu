using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.LongShu;

internal static class XjLongShuKillRewardSystem
{
	internal static bool TryGrant(Actor longShu, in XjDeathSnapshot snapshot)
	{
		if (!XjLongShuSystem.IsLongShu(longShu)
			|| snapshot.LastAttackerId <= 0L
			|| !XjScheduler.ResolveActor(snapshot.LastAttackerId, out Actor killer)
			|| killer?.data == null
			|| !killer.isAlive()
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(snapshot.LastAttackerId, out long familyId)
			|| familyId <= 0L)
		{
			return false;
		}

		string daoTu = snapshot.DaoTu;
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			XjActorAccessor.TryGetString(killer, XjActorDataKeys.DaoTu, out daoTu);
		}
		if (!XjLingWuCatalog.TryResolveByDaoTu(daoTu, out XjLingWuDef definition))
		{
			return false;
		}

		string killerName = killer.getName() ?? "修士";
		if (!XjFamilyLingWuWarehouse.TryAdd(familyId, definition, snapshot.LastAttackerId, killerName, snapshot.Year))
		{
			return false;
		}

		string longShuName = string.IsNullOrWhiteSpace(snapshot.Name) ? "龙属" : snapshot.Name;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Family,
			"斩龙得灵",
			killerName + "斩杀" + longShuName + "，剖其龙躯，得“" + definition.Name + "”一件，收入家族重宝仓库。",
			3,
			actorId: snapshot.LastAttackerId,
			actorName: killerName,
			familyId: familyId,
			year: snapshot.Year);
		XjBroadcastSystem.BroadcastBLevelWorldEvent("【斩龙得灵】" + killerName + "斩杀" + longShuName + "，得“" + definition.Name + "”。", XjEventIconCatalog.LingWuAppear);
		return true;
	}
}
