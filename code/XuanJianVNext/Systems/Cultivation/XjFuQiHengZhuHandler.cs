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
/// 衡祝服气入门：以巫祝法守火、以性命养祭，温养一份只属于自身的本命祭火。
/// 本命祭火不是火德道气、法术效果或地图祭坛，不创建物品与实时仪式状态机。
/// </summary>
internal static class XjFuQiHengZhuHandler
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
            int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 34, 48, 42, 60, 52, 72, "fuqi_hengzhu_ritual_fire_years");
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
        XjDaoTuManifestRegistry.MarkFuQiManifested(XjDaoTuRootIds.HengZhu, actorId, currentYear);
        XjThreeBookWriter.RecordFuQiHengZhuHuangGuan(actor, currentYear);
    }

    internal static void ApplyLectureAid(Actor actor, int currentYear, int years, in XjFuQiCoreDefinition definition)
    {
        if (!CanProcess(actor, currentYear, in definition) || years <= 0) return;
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
        if (!XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)) return;
        if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
            || completeYear <= 0)
        {
            int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 34, 48, 42, 60, 52, 72, "fuqi_hengzhu_ritual_fire_years");
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
        StringBuilder builder = new StringBuilder(208);
        builder.AppendLine("道途：衡祝");
        builder.AppendLine("感气：已感应衡祝之气");
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
            && completeYear > 0)
        {
            builder.Append("本命祭火：温养中");
            int remaining = Math.Max(0, completeYear - currentYear);
            if (remaining > 0) builder.Append("，尚需").Append(remaining).Append('年');
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("本命祭火：尚待奉持温养");
        }
        builder.Append("修法性质：以巫祝法守火养命，不等同火德功法，不建立祭坛或消耗祭品");
        return builder.ToString().TrimEnd();
    }

    private static bool CanProcess(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
    {
        if (actor?.data == null || currentYear <= 0 || !definition.GameplayImplemented
            || !string.Equals(definition.HandlerId, XjFuQiHandlerIds.HengZhu, StringComparison.Ordinal)) return false;
        return !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            || string.IsNullOrWhiteSpace(XjRealmHelper.NormalizeId(realmId));
    }

}
