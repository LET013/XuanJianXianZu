using System;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.History.Books;

internal static partial class XjThreeBookWriter
{
    internal static void RecordQingXuanFoundationFormed(Actor actor, string foundationName, int year, string source)
    {
        if (actor?.data == null) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;
        ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
        string actorName = SafeActorName(actor);
        string name = SafeName(foundationName, "无名仙基");
        string sourceText = string.IsNullOrWhiteSpace(source) ? string.Empty : "此基由" + source.Trim() + "而来，";
        RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShenTongComprehended,
            "personal|qingxuan-foundation|" + actorId + "|" + name,
            XjWorldHistoryCategory.Cultivation, "青宣仙基", "仙基初成",
            actorName + "于青宣道途之中结成【" + name + "】仙基。" + sourceText
                + "此时尚非神通，仍待核心神通〖玄羊子〗抬举升格。",
            2, false, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
    }

    internal static void RecordQingXuanFoundationRaised(Actor actor, string foundationName, int year, int raisedCount)
    {
        if (actor?.data == null) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;
        ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
        string actorName = SafeActorName(actor);
        string name = SafeName(foundationName, "无名仙基");
        RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShenTongComprehended,
            "personal|qingxuan-uplift|" + actorId + "|" + name,
            XjWorldHistoryCategory.Cultivation, "青宣神通", "玄羊子抬举仙基",
            actorName + "以核心神通〖玄羊子〗抬举【" + name + "】仙基，使其脱去基形、升格为上位神通。"
                + "至此其青宣神通已有" + Math.Max(1, raisedCount) + "门。",
            3, false, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
    }

    internal static void RecordQingXuanFiveShenTongReady(Actor actor, int year)
    {
        if (actor?.data == null) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;
        ResolveActorAffiliations(actor, out long familyId, out string familyName, out long sectId, out string sectName);
        string actorName = SafeActorName(actor);
        RecordPersonal(actorId, actorName, year, XjThreeBookEventTypes.PersonalShenTongComprehended,
            "personal|qingxuan-five-shentong|" + actorId,
            XjWorldHistoryCategory.Cultivation, "青宣五神通", "五神通齐备",
            actorName + "以〖玄羊子〗先后抬举四道仙基，使五基尽数升格为神通。"
                + "青宣特殊五神通结构由此在金门之前圆满，方可继续推演求金法、谋求空证。",
            4, true, familyId, familyName, sectId, sectName, result: XjHistoryResult.Success);
    }
}
