using XuanJianVNext.Data.History;
using XuanJianVNext.Data.LingWu;

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
		RecordPersonal(
			actorId,
			actorName,
			year,
			XjThreeBookEventTypes.PersonalLingWuObtained,
			"personal|zifu-lingwu-opportunity|" + actorId + "|" + year + "|" + definition.Id,
			XjWorldHistoryCategory.Cultivation,
			"机缘",
			"紫府得灵",
			actorName + "静修之际感应天地灵机，于道痕汇聚处寻得“" + definition.Name + "”，遂收入家族重宝仓库。",
			2,
			false,
			familyId,
			familyName,
			sectId,
			sectName);
	}
}
