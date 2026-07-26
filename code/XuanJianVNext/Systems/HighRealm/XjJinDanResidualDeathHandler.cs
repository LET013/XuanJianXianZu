using System;
using System.Globalization;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjJinDanResidualDeathHandler
{
	internal static bool Handle(in XjDeathSnapshot snapshot)
	{
		bool isJinDan = XjRealmHelper.IsRealm(snapshot.RealmId, "JinDan");
		if (!snapshot.Found
			|| snapshot.ActorId <= 0L
			|| snapshot.FamilyStableId <= 0L
			|| (!isJinDan && !snapshot.IsJieLinXian))
		{
			return false;
		}

		string jinXing = ResolveJinXing(snapshot);
		int amount = snapshot.IsJieLinXian ? 1 : snapshot.JinDanStageIndex + 1;
		if (!XjFamilyLingWuWarehouse.TryAddJinXing(
			snapshot.FamilyStableId,
			jinXing,
			amount,
			snapshot.ActorId,
			snapshot.Name,
			snapshot.Year))
		{
			return false;
		}

		// 仓库写入是权威结果；纪事失败不再回滚或阻断死亡归档。
		XjChronicleWriter.RecordJinDanResidualAppeared(snapshot, jinXing, amount);

		string actorName = string.IsNullOrWhiteSpace(snapshot.Name) ? "一位真君" : snapshot.Name;
		string amountText = FormatAmount(amount);
		string text = "【金性遗留】" + actorName + "身故后金性不散，遗下" + amountText + "“" + jinXing + "”，已归入其家族重宝仓库。";
		XjBroadcastSystem.BroadcastBLevelWorldEvent(text, XjEventIconCatalog.JinXingLegacy);
		return true;
	}

	private static string FormatAmount(int amount)
	{
		return amount == 1
			? "一缕"
			: amount.ToString(CultureInfo.InvariantCulture) + "缕";
	}

	private static string ResolveJinXing(in XjDeathSnapshot snapshot)
	{
		if (!string.IsNullOrWhiteSpace(snapshot.JinXing))
		{
			return snapshot.JinXing.Trim();
		}

		return string.IsNullOrWhiteSpace(snapshot.DaoTu)
			? "未定金性"
			: snapshot.DaoTu.Trim() + "金性";
	}
}
