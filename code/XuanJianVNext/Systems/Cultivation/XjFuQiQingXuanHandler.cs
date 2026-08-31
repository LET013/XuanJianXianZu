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
/// 服气青宣以神妙〖玄羊子〗为本命核心。玄羊子初成并晋升黄冠时，
/// 才算服气青宣真正显世，并由此开启后世紫府金丹青宣的世界门控。
/// </summary>
internal static class XjFuQiQingXuanHandler
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
			int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 32, 46, 40, 56, 50, 68, "fuqi_qingxuan_xuanyangzi_years");
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
		XjDaoTuManifestRegistry.MarkFuQiManifested(XjDaoTuRootIds.QingXuan, actorId, currentYear);
		XjThreeBookWriter.RecordFuQiQingXuanHuangGuan(actor, currentYear);
	}

	internal static void ApplyLectureAid(Actor actor, int currentYear, int years, in XjFuQiCoreDefinition definition)
	{
		if (!CanProcess(actor, currentYear, in definition) || years <= 0) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (!XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)) return;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
			|| completeYear <= 0)
		{
			int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 32, 46, 40, 56, 50, 68, "fuqi_qingxuan_xuanyangzi_years");
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
		StringBuilder builder = new StringBuilder(176);
		builder.AppendLine("道途：青宣");
		builder.AppendLine("感气：已感应青宣之气");
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear) && completeYear > 0)
		{
			builder.Append("神妙〖玄羊子〗：温养中");
			int remaining = Math.Max(0, completeYear - currentYear);
			if (remaining > 0) builder.Append("，尚需").Append(remaining).Append('年');
			builder.AppendLine();
		}
		else
		{
			builder.AppendLine("神妙〖玄羊子〗：尚待温养");
		}
		builder.Append("修法显现：玄羊子初成、晋升黄冠后，后世方可推演紫金青宣");
		return builder.ToString().TrimEnd();
	}

	private static bool CanProcess(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
	{
		if (actor?.data == null || currentYear <= 0 || !definition.GameplayImplemented
			|| !string.Equals(definition.HandlerId, XjFuQiHandlerIds.QingXuan, StringComparison.Ordinal)) return false;
		return !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| string.IsNullOrWhiteSpace(XjRealmHelper.NormalizeId(realmId));
	}

}
