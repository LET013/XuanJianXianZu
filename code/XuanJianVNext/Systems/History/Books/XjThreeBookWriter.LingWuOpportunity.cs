using XuanJianVNext.Data.History;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.History.Books;

internal static partial class XjThreeBookWriter
{
	internal static void RecordZiFuLingWuOpportunity(Actor actor, XjLingWuDef definition, int year)
	{
		if (actor?.data == null || definition == null || year <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;

		ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
		string actorName = SafeActorName(actor);
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		string realmDisplay = XjRealmHelper.GetDisplayName(realmId);
		if (string.IsNullOrWhiteSpace(realmDisplay)) realmDisplay = "真人";
		string eventTitle = realmDisplay + "得灵";
		RecordPersonal(
			actorId,
			actorName,
			year,
			XjThreeBookEventTypes.PersonalLingWuObtained,
			"personal|zifu-lingwu-opportunity|" + actorId + "|" + year + "|" + definition.Id,
			XjWorldHistoryCategory.Cultivation,
			"机缘",
			eventTitle,
			actorName + "于" + realmDisplay + "境静修时感应天地灵机，于道痕汇聚处寻得“" + definition.Name + "”，遂收入家族重宝仓库。",
			2,
			false,
			familyId,
			familyName,
			sectId,
			sectName);
	}
}
