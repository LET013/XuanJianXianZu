using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.LingWu;

internal static class XjLingWuDeathHandler
{
	internal static bool Handle(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found
			|| snapshot.ActorId <= 0L
			|| !XjCultivationPathRules.IsZhenRenEquivalentRealm(snapshot.RealmId)
			|| !XjLingWuCatalog.TryResolveByDaoTu(snapshot.DaoTu, out XjLingWuDef definition))
		{
			return false;
		}

		long recipientFamilyId = snapshot.FamilyStableId;
		long killerFamilyId = 0L;
		bool hasValidKiller = snapshot.LastAttackerId > 0L
			&& snapshot.LastAttackerId != snapshot.ActorId
			&& XuanJianVNext.Core.XjScheduler.ResolveActor(snapshot.LastAttackerId, out Actor killer)
			&& killer?.data != null
			&& killer.isAlive();
		if (hasValidKiller
			&& !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(snapshot.LastAttackerId, out killerFamilyId))
		{
			return false;
		}

		bool claimedByKiller = hasValidKiller && killerFamilyId > 0L && killerFamilyId != snapshot.FamilyStableId;
		bool sameFamilyKiller = hasValidKiller && killerFamilyId > 0L && killerFamilyId == snapshot.FamilyStableId;
		if (claimedByKiller)
		{
			recipientFamilyId = killerFamilyId;
		}

		if (recipientFamilyId <= 0L)
		{
			return false;
		}

		string eventKey = XjChronicleWriter.BuildLingWuAppearedEventKey(snapshot, definition);
		if (XjFamilyChronicleMemory.Shared.ContainsEventKey(eventKey))
		{
			return false;
		}

		if (!XjFamilyLingWuWarehouse.TryAdd(
			recipientFamilyId,
			definition,
			snapshot.ActorId,
			snapshot.Name,
			snapshot.Year))
		{
			return false;
		}

		if (!XjChronicleWriter.RecordLingWuAppeared(
			snapshot, definition, recipientFamilyId, snapshot.ActorId, snapshot.Name, claimedByKiller))
		{
			return false;
		}

		string realmDisplay = XjRealmHelper.GetDisplayName(snapshot.RealmId);
		string actorName = string.IsNullOrWhiteSpace(snapshot.Name)
			? "一位" + (string.IsNullOrWhiteSpace(realmDisplay) ? "真人" : realmDisplay + "修士")
			: snapshot.Name;
		string text = claimedByKiller
			? "【夺灵归族】" + actorName + "陨于" + (string.IsNullOrWhiteSpace(snapshot.LastAttackerName) ? "击杀者" : snapshot.LastAttackerName) + "之手，道痕凝成" + definition.Name + "，已归入击杀者家族重宝仓库。"
			: sameFamilyKiller
				? "【灵物归族】" + actorName + "陨于同族纷争，道痕凝成" + definition.Name + "，已归入本族重宝仓库。"
				: "【灵物现世】" + actorName + "身故后道痕不散，凝成" + definition.Name + "，已归入其家族重宝仓库。";
		XjBroadcastSystem.BroadcastBLevelWorldEvent(text, XjEventIconCatalog.LingWuAppear, XjAnnouncementCategory.LingWu);
		return true;
	}
}
