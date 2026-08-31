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
/// 三巫服气入门：鸺葵、上巫、玉真皆以本命符箓为性命双修核心。
/// 本命符箓是角色状态而非背包物品，不占百艺符箓槽，不可赠送、掉落或消耗。
/// </summary>
internal static class XjFuQiNatalTalismanHandler
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
			int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 30, 44, 38, 54, 48, 66, "fuqi_natal_talisman_years|" + definition.DaoTuRootId);
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
		XjThreeBookWriter.RecordFuQiNatalTalismanHuangGuan(actor, definition.DisplayName, currentYear);
	}

	internal static void ApplyLectureAid(Actor actor, int currentYear, int years, in XjFuQiCoreDefinition definition)
	{
		if (!CanProcess(actor, currentYear, in definition) || years <= 0) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (!XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)) return;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
			|| completeYear <= 0)
		{
			int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(actor, aptitude, 30, 44, 38, 54, 48, 66, "fuqi_natal_talisman_years|" + definition.DaoTuRootId);
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
		StringBuilder builder = new StringBuilder(192);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		builder.Append("道途：").AppendLine(string.IsNullOrWhiteSpace(daoTu) ? definition.DisplayName : daoTu.Trim());
		builder.Append("感气：已感应").Append(definition.DaoTuRootId == XjDaoTuRootIds.XiaoKui ? "鸺葵" : definition.DaoTuRootId == XjDaoTuRootIds.ShangWu ? "上巫" : "玉真").AppendLine("之气");
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear) && completeYear > 0)
		{
			builder.Append("本命符箓：凝炼中");
			int remaining = Math.Max(0, completeYear - currentYear);
			if (remaining > 0) builder.Append("，尚需").Append(remaining).Append('年');
			builder.AppendLine();
		}
		else
		{
			builder.AppendLine("本命符箓：尚待凝炼");
		}
		builder.Append("符箓性质：与性命相连，不入背包，不作消耗符箓");
		return builder.ToString().TrimEnd();
	}

	private static bool CanProcess(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
	{
		if (actor?.data == null || currentYear <= 0 || !definition.GameplayImplemented
			|| !string.Equals(definition.HandlerId, XjFuQiHandlerIds.NatalTalisman, StringComparison.Ordinal)) return false;
		return !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| string.IsNullOrWhiteSpace(XjRealmHelper.NormalizeId(realmId));
	}

}
