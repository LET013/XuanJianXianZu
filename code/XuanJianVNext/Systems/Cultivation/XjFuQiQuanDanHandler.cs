using System;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.History.Books;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 全丹服气入门：以性命为炉温养本命丹性。本命丹性是修炼核心，
/// 不属于炼丹百艺，不读取丹方、药材、丹药库存，也不产生可交易丹药。
/// </summary>
internal static class XjFuQiQuanDanHandler
{
    internal static void TickEntry(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
    {
        if (!CanProcess(actor, currentYear, in definition)) return;
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, out int lastYear)
            && lastYear == currentYear) return;
        XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, currentYear);

        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
        if (!XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)) return;
        if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
            || completeYear <= 0)
        {
            int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 36, 50, 44, 62, 54, 74, "fuqi_quandan_natal_elixir_years");
            XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectStartYear, currentYear);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, currentYear + duration);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 0);
            return;
        }

        XjFuQiEntryProjectProgress.Update(actor, currentYear, completeYear);
        if (currentYear < completeYear) return;
        XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 10000);
        XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiShenMiaoId, definition.CoreId);
        if (!XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.HuangGuan, true, true))
        {
            XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiShenMiaoId, string.Empty);
            return;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        XjDaoTuManifestRegistry.MarkFuQiManifested(XjDaoTuRootIds.QuanDan, actorId, currentYear);
        XjThreeBookWriter.RecordFuQiQuanDanHuangGuan(actor, currentYear);
    }

    internal static void ApplyLectureAid(Actor actor, int currentYear, int years, in XjFuQiCoreDefinition definition)
    {
        if (!CanProcess(actor, currentYear, in definition) || years <= 0) return;
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
        if (!XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)) return;
        if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
            || completeYear <= 0)
        {
            int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 36, 50, 44, 62, 54, 74, "fuqi_quandan_natal_elixir_years");
            XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectStartYear, currentYear);
            completeYear = currentYear + duration;
            XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, completeYear);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 0);
        }
        if (completeYear > currentYear)
        {
            completeYear = Math.Max(currentYear + 1, completeYear - years);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, completeYear);
            XjFuQiEntryProjectProgress.Update(actor, currentYear, completeYear);
        }
    }

    internal static string BuildEntrySummary(Actor actor, in XjFuQiCoreDefinition definition, int currentYear)
    {
        if (actor?.data == null) return string.Empty;
        StringBuilder builder = new StringBuilder(224);
        builder.AppendLine("道途：全丹");
        builder.AppendLine("感气：已感应全丹之气");
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
            && completeYear > 0)
        {
            builder.Append("本命丹性：温养中");
            int remaining = Math.Max(0, completeYear - currentYear);
            if (remaining > 0) builder.Append("，尚需").Append(remaining).Append('年');
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("本命丹性：尚待温养");
        }
        builder.Append("修法性质：以性命为炉，不使用丹方、药材或丹药库存，不占炼丹百艺槽位");
        return builder.ToString().TrimEnd();
    }

    private static bool CanProcess(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
    {
        if (actor?.data == null || currentYear <= 0 || !definition.GameplayImplemented
            || !string.Equals(definition.HandlerId, XjFuQiHandlerIds.QuanDan, StringComparison.Ordinal)) return false;
        return !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            || string.IsNullOrWhiteSpace(XjRealmHelper.NormalizeId(realmId));
    }

}
