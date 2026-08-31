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
/// 执孛、司天、都卫共用的轻量服气入门Handler。三者只在数据层区分本命核心、
/// 修炼文本和固定年度区间，不建立分身实体、逐帧天象监听、城阵或全图地域扫描。
/// 核心初成后统一交给XjFuQiCultivationSystem处理求身、圆满、生金性与求证。
/// </summary>
internal static class XjFuQiBingGuCoreHandler
{
	private readonly struct Profile
	{
		internal readonly string HandlerId;
		internal readonly string DaoTuName;
		internal readonly string CoreLabel;
		internal readonly string ProgressVerb;
		internal readonly string NatureDescription;
		internal readonly string HistoryKey;
		internal readonly string HistoryTitle;
		internal readonly string HistoryDetail;
		internal readonly int Aptitude6Min;
		internal readonly int Aptitude6Max;
		internal readonly int Aptitude5Min;
		internal readonly int Aptitude5Max;
		internal readonly int Aptitude4Min;
		internal readonly int Aptitude4Max;
		internal readonly string DurationSalt;

		internal Profile(
			string handlerId,
			string daoTuName,
			string coreLabel,
			string progressVerb,
			string natureDescription,
			string historyKey,
			string historyTitle,
			string historyDetail,
			int aptitude6Min,
			int aptitude6Max,
			int aptitude5Min,
			int aptitude5Max,
			int aptitude4Min,
			int aptitude4Max,
			string durationSalt)
		{
			HandlerId = handlerId;
			DaoTuName = daoTuName;
			CoreLabel = coreLabel;
			ProgressVerb = progressVerb;
			NatureDescription = natureDescription;
			HistoryKey = historyKey;
			HistoryTitle = historyTitle;
			HistoryDetail = historyDetail;
			Aptitude6Min = aptitude6Min;
			Aptitude6Max = aptitude6Max;
			Aptitude5Min = aptitude5Min;
			Aptitude5Max = aptitude5Max;
			Aptitude4Min = aptitude4Min;
			Aptitude4Max = aptitude4Max;
			DurationSalt = durationSalt;
		}
	}

	internal static bool CanHandle(in XjFuQiCoreDefinition definition)
	{
		return TryResolveProfile(definition.HandlerId, out _);
	}

	internal static void TickEntry(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
	{
		if (!TryResolveProfile(definition.HandlerId, out Profile profile)
			|| !CanProcess(actor, currentYear, in definition, in profile)) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, out int lastYear)
			&& lastYear == currentYear) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreLastAnnualYear, currentYear);

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (!XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)) return;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
			|| completeYear <= 0)
		{
			int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(
				actor, aptitude,
				profile.Aptitude6Min, profile.Aptitude6Max,
				profile.Aptitude5Min, profile.Aptitude5Max,
				profile.Aptitude4Min, profile.Aptitude4Max,
				profile.DurationSalt);
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
		XjThreeBookWriter.RecordFuQiSpecialCoreHuangGuan(
			actor,
			profile.HistoryKey,
			profile.HistoryTitle,
			profile.HistoryDetail,
			currentYear);
	}

	internal static void ApplyLectureAid(Actor actor, int currentYear, int years, in XjFuQiCoreDefinition definition)
	{
		if (!TryResolveProfile(definition.HandlerId, out Profile profile)
			|| !CanProcess(actor, currentYear, in definition, in profile)
			|| years <= 0) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (!XjFuQiAptitudeRules.CanReachHuangGuan(aptitude)) return;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
			|| completeYear <= 0)
		{
			int duration = XjFuQiEntryProjectProgress.ResolveEntryYears(
				actor, aptitude,
				profile.Aptitude6Min, profile.Aptitude6Max,
				profile.Aptitude5Min, profile.Aptitude5Max,
				profile.Aptitude4Min, profile.Aptitude4Max,
				profile.DurationSalt);
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
		if (actor?.data == null || !TryResolveProfile(definition.HandlerId, out Profile profile)) return string.Empty;
		StringBuilder builder = new StringBuilder(224);
		builder.Append("道途：").AppendLine(profile.DaoTuName);
		builder.Append("感气：已感应").Append(profile.DaoTuName).AppendLine("之气");
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out int completeYear)
			&& completeYear > 0)
		{
			builder.Append(profile.CoreLabel).Append('：').Append(profile.ProgressVerb).Append("中");
			int remaining = Math.Max(0, completeYear - currentYear);
			if (remaining > 0) builder.Append("，尚需").Append(remaining).Append('年');
			builder.AppendLine();
		}
		else
		{
			builder.Append(profile.CoreLabel).Append("：尚待").Append(profile.ProgressVerb).AppendLine();
		}
		builder.Append("修法性质：").Append(profile.NatureDescription);
		return builder.ToString().TrimEnd();
	}

	private static bool CanProcess(
		Actor actor,
		int currentYear,
		in XjFuQiCoreDefinition definition,
		in Profile profile)
	{
		if (actor?.data == null || currentYear <= 0 || !definition.GameplayImplemented
			|| !string.Equals(definition.HandlerId, profile.HandlerId, StringComparison.Ordinal)) return false;
		return !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| string.IsNullOrWhiteSpace(XjRealmHelper.NormalizeId(realmId));
	}

private static bool TryResolveProfile(string handlerId, out Profile profile)
	{
		if (string.Equals(handlerId, XjFuQiHandlerIds.ZhiBo, StringComparison.Ordinal))
		{
			profile = new Profile(
				XjFuQiHandlerIds.ZhiBo,
				"执孛",
				"本命形相",
				"温养",
				"以形神分合与变化温养性命，不生成常驻幻身、分身实体或额外AI",
				"zhibo",
				"本命形相初定",
				"感应执孛之气，历年温养本命形相，使形神的分合变化归于自身性命。形相初定，遂求得本命神妙，位列黄冠。",
				35, 49, 43, 61, 53, 73,
				"fuqi_zhibo_mutable_form_years");
			return true;
		}
		if (string.Equals(handlerId, XjFuQiHandlerIds.SiTian, StringComparison.Ordinal))
		{
			profile = new Profile(
				XjFuQiHandlerIds.SiTian,
				"司天",
				"本命天序",
				"推演",
				"只用固定年度项目观天定序，不进行逐帧天象扫描、全图监听或实时推演",
				"sitian",
				"本命天序初定",
				"感应司天之气，观天象、辨时序，历年推演本命天序。天序与性命初步相应，遂求得本命神妙，位列黄冠。",
				38, 54, 47, 65, 58, 78,
				"fuqi_sitian_celestial_order_years");
			return true;
		}
		if (string.Equals(handlerId, XjFuQiHandlerIds.DuWei, StringComparison.Ordinal))
		{
			profile = new Profile(
				XjFuQiHandlerIds.DuWei,
				"都卫",
				"本命方镇",
				"温养",
				"以四方山川之理温养性命，不生成城阵、占领规则或实时地域扫描",
				"duwei",
				"本命方镇初定",
				"感应都卫之气，取四方山川之理温养本命方镇。方镇只系于自身性命而不落城阵，初定后遂位列黄冠。",
				36, 52, 45, 63, 56, 76,
				"fuqi_duwei_directional_guard_years");
			return true;
		}
		profile = default;
		return false;
	}
}
