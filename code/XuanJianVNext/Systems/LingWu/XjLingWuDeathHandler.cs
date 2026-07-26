using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.LingWu;

internal static class XjLingWuDeathHandler
{
	internal static bool Handle(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found
			|| snapshot.ActorId <= 0L
			|| snapshot.FamilyStableId <= 0L
			|| !XjRealmHelper.IsRealm(snapshot.RealmId, "ZiFu")
			|| !XjLingWuCatalog.TryResolveByDaoTu(snapshot.DaoTu, out XjLingWuDef definition))
		{
			return false;
		}

		// The protected chronicle key is also the idempotency gate. Death archival can be
		// retried by save recovery, but one ZiFu death may create only one LingWu.
		if (!XjChronicleWriter.RecordLingWuAppeared(snapshot, definition))
		{
			return false;
		}

		if (!XjFamilyLingWuWarehouse.TryAdd(
			snapshot.FamilyStableId,
			definition,
			snapshot.ActorId,
			snapshot.Name,
			snapshot.Year))
		{
			return false;
		}

		string actorName = string.IsNullOrWhiteSpace(snapshot.Name) ? "一位紫府修士" : snapshot.Name;
		string text = "【灵物现世】" + actorName + "身故后道痕不散，凝成“" + definition.Name + "”，已归入其家族重宝仓库。";
		XjBroadcastSystem.BroadcastBLevelWorldEvent(text, XjEventIconCatalog.LingWuAppear);
		return true;
	}
}
