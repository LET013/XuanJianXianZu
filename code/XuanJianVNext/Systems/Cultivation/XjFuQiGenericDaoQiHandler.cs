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
/// 常见九途共用的服气入门Handler。感气已经在身份分流时完成，本Handler只负责
/// 长期温养对应道气，核心初成后请求公共状态机晋升黄冠。
/// </summary>
internal static class XjFuQiGenericDaoQiHandler
{
	internal static void TickEntry(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
	{
		if (actor?.data == null || currentYear <= 0 || !definition.GameplayImplemented) return;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& !string.IsNullOrWhiteSpace(XjRealmHelper.NormalizeId(realmId))) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, out int lastYear)
			&& lastYear == currentYear) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, currentYear);

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (!XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)) return;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
			|| completeYear <= 0)
		{
			int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 25, 38, 32, 46, 40, 58, "fuqi_core_entry_years|" + definition.DaoTuRootId);
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
		XjDaoTuManifestRegistry.MarkFuQiManifested(definition.DaoTuRootId, actorId, currentYear);
		XjThreeBookWriter.RecordFuQiCoreHuangGuan(actor, definition.DisplayName, currentYear);
	}

	internal static void ApplyLectureAid(Actor actor, int currentYear, int years, in XjFuQiCoreDefinition definition)
	{
		if (actor?.data == null || currentYear <= 0 || years <= 0 || !definition.GameplayImplemented) return;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& !string.IsNullOrWhiteSpace(XjRealmHelper.NormalizeId(realmId))) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (!XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)) return;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
			|| completeYear <= 0)
		{
			int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 25, 38, 32, 46, 40, 58, "fuqi_core_entry_years|" + definition.DaoTuRootId);
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
		StringBuilder builder = new StringBuilder(160);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		builder.Append("道途：").AppendLine(string.IsNullOrWhiteSpace(daoTu) ? definition.DisplayName : daoTu.Trim());
		builder.Append("感气：已感应").Append(definition.DisplayName).AppendLine();
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear) && completeYear > 0)
		{
			builder.Append("本命核心：温养中");
			int remaining = Math.Max(0, completeYear - currentYear);
			if (remaining > 0) builder.Append("，尚需").Append(remaining).Append('年');
			builder.AppendLine();
		}
		else
		{
			builder.AppendLine("本命核心：尚待开始温养");
		}
		return builder.ToString().TrimEnd();
	}

}
