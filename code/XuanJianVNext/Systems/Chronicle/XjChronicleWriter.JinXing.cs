using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.Systems.Chronicle;

internal static partial class XjChronicleWriter
{
	internal static bool RecordJinDanResidualAppeared(in XjDeathSnapshot snapshot, string jinXing, int amount)
	{
		if (!snapshot.Found
			|| snapshot.FamilyStableId <= 0L
			|| snapshot.ActorId <= 0L
			|| amount <= 0
			|| string.IsNullOrWhiteSpace(jinXing))
		{
			return false;
		}

		string actorName = string.IsNullOrWhiteSpace(snapshot.Name) ? "族中真君" : snapshot.Name;
		string sourceRealm = snapshot.IsYuYiXian ? "郁仪仙" : snapshot.IsJieLinXian ? "结璘仙" : "金丹真君";
		XjChronicleEvent chronicleEvent = new XjChronicleEvent(
			true,
			snapshot.FamilyStableId,
			snapshot.ActorId,
			XjChronicleEventTypes.JinDanResidualAppeared,
			snapshot.Year,
			"金性遗留入库",
			actorName + "以" + sourceRealm + "之身陨落，金性不散，遗下" + FormatJinXingAmount(amount) + "“" + jinXing + "”，收入家族重宝仓库。",
			5,
			true,
			true,
			false,
			"Ok",
			"jindan.residual_death",
			snapshot.RealmId);
		string eventKey = "JinDanResidualAppeared:" + snapshot.ActorId + ":" + snapshot.Year + ":" + jinXing;
		return XjFamilyChronicleMemory.Shared.Append(chronicleEvent, eventKey);
	}

	internal static bool RecordJinDanResidualAcquiredFromDongTian(
		Actor actor,
		int year,
		string jinXing,
		string dongTianName,
		string recordId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(jinXing))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		if (!XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out long familyStableId) || familyStableId <= 0L)
		{
			if (!XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord identity)
				|| !identity.Found
				|| identity.RootActorId <= 0L)
			{
				return false;
			}
			familyStableId = identity.RootActorId;
		}

		string place = string.IsNullOrWhiteSpace(dongTianName) ? "奇遇洞天" : dongTianName.Trim();
		XjChronicleEvent chronicleEvent = new XjChronicleEvent(
			true,
			familyStableId,
			actorId,
			XjChronicleEventTypes.JinDanResidualAcquired,
			year,
			"洞天得金性",
			actor.getName() + "于“" + place + "”中得一缕“" + jinXing + "”，收入家族重宝仓库。",
			4,
			true,
			true,
			false,
			"Ok",
			"dongtian.residual_jinxing",
			string.Empty);
		string eventKey = "JinDanResidualAcquired:" + actorId + ":" + year + ":" + (recordId ?? string.Empty) + ":" + jinXing;
		return XjFamilyChronicleMemory.Shared.Append(chronicleEvent, eventKey);
	}

	private static string FormatJinXingAmount(int amount)
	{
		return amount == 1 ? "一缕" : amount.ToString() + "缕";
	}
}
