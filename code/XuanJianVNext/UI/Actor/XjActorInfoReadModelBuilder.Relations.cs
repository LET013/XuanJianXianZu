using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{		private static string BuildFamilySummary(long actorId)
		{
			if (actorId <= 0L)
			{
				return "暂无家族";
			}
	
			if (XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity))
			{
				string familyName = ResolveFamilyDisplayName(identity.FamilyStableIdValue);
				string branch = XjFamilyMemberLedger.IsMigrationBranchFamily(identity.FamilyStableIdValue) ? "迁城分家" : "本家";
				string summary = (string.IsNullOrWhiteSpace(familyName) ? "未名氏" : familyName)
					+ "-" + branch + "-第" + identity.Generation.ToString(CultureInfo.InvariantCulture) + "代";
				if (XjFamilyMemberLedger.IsMigrationBranchFamily(identity.FamilyStableIdValue))
				{
					if (XjFamilyMemberLedger.TryGetBranchSourceFamilyId(identity.FamilyStableIdValue, out long sourceFamilyId))
					{
						summary += "\n    来源主家：" + ResolveFamilyDisplayName(sourceFamilyId);
					}
				}
				if (XjFamilyMemberLedger.TryGetFamilyOriginCityId(identity.FamilyStableIdValue, out long originCityId))
				{
					summary += "\n    发源城：" + ResolveCityDisplayName(originCityId);
				}
				return summary;
			}
	
			return XjFamilyReadModel.Shared.IsPending(actorId) ? "父系待确认" : "暂无父系家族";
		}

		private static string ResolveFamilyDisplayName(long familyStableId)
		{
			if (familyStableId <= 0L) return "未知主家";
			return XjFamilyDisplayNameResolver.Resolve(familyStableId);
		}

		private static string ResolveCityDisplayName(long cityId)
		{
			if (cityId <= 0L || !XjWorldLookupIndex.TryResolveCity(cityId, out City city) || city?.data == null)
			{
				return cityId > 0L ? "城镇" + cityId.ToString(CultureInfo.InvariantCulture) : "未知城镇";
			}
			try
			{
				string name = ((BaseSystemData)city.data).name;
				return string.IsNullOrWhiteSpace(name) ? "城镇" + cityId.ToString(CultureInfo.InvariantCulture) : name.Trim();
			}
			catch
			{
				return "城镇" + cityId.ToString(CultureInfo.InvariantCulture);
			}
		}

		private static string BuildBloodlineSummary(long actorId)
		{
			if (actorId <= 0L || !XjFamilyReadModel.Shared.TryGetBloodlineDetails(actorId, out XjBloodlineDisplayState state))
			{
				return string.Empty;
			}
	
			// The 0.5.4 actor panel shows only the bloodline quality in this field.
			return TrimBloodlineQuality(state.Quality);
		}

	
		private static string BuildQuanBingSummary(in XjGuoWeiQuanBingState state)
		{
			if (!state.Found)
			{
				return string.Empty;
			}
	
			System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>(4);
			AppendQuanBingPart(parts, string.Empty, state.LocalQuanBing);
			AppendQuanBingPart(parts, "夺得：", state.SeizedQuanBing);
			AppendQuanBingPart(parts, "外道：", state.ForeignQuanBing);
			AppendQuanBingPart(parts, "洞天：", state.WithdrawnToDongTian);
			return string.Join(" - ", parts);
		}

		private static void AppendQuanBingPart(System.Collections.Generic.List<string> parts, string prefix, string values)
		{
			if (parts == null || string.IsNullOrWhiteSpace(values))
			{
				return;
			}
	
			parts.Add((prefix ?? string.Empty) + values.Trim().Replace(",", "、"));
		}

		private static string TrimBloodlineQuality(string quality)
		{
			if (string.IsNullOrWhiteSpace(quality))
			{
				return "尘息";
			}
	
			string value = quality.Trim();
			int separator = value.IndexOf('：');
			if (separator >= 0 && separator + 1 < value.Length)
			{
				value = value.Substring(separator + 1).Trim();
			}
	
			return string.IsNullOrWhiteSpace(value) ? "尘息" : value;
		}

		private static string BuildFatherStatusSummary(long actorId)
		{
			if (actorId <= 0L)
			{
				return string.Empty;
			}
	
			if (XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out _))
			{
				return "已确认父系";
			}
	
			if (!XjFamilyReadModel.Shared.IsPending(actorId))
			{
				return string.Empty;
			}
	
			System.Collections.Generic.IReadOnlyList<XjFamilyPendingDisplayItem> pendingItems =
				XjFamilyReadModel.Shared.BuildPendingDisplayItems(0L, actorId);
			return pendingItems.Count > 0 ? pendingItems[0].DisplayText : "待父系确认";
		}

		private static string BuildZongMenSummary(Actor actor, in XjZongMenIdentitySnapshot item)
		{
			if (!item.Found)
			{
				return "暂无宗门";
			}
	
			string rank = NormalizeZongMenRankForDisplay(item.Rank);
			string year = item.JoinYear > 0 ? "\n    入门：" + item.JoinYear.ToString(CultureInfo.InvariantCulture) + "年" : string.Empty;
			return item.ZongMenName + "\n    身份：" + rank + year;
		}

		private static string NormalizeZongMenRankForDisplay(string rank)
		{
			string value = string.IsNullOrWhiteSpace(rank) ? string.Empty : rank.Trim();
			if (value.Contains("宗主")) return "宗主";
			if (value.Contains("峰主")) return "峰主";
			if (value.Contains("老祖")) return "老祖";
			if (value.Contains("长老")) return "长老";
			if (value.Contains("弟子")) return "弟子";
			if (value.Contains("门人")) return "门人";
			return string.IsNullOrWhiteSpace(value) ? "门人" : value;
		}

		private static string BuildFaBaoSummary(in XjFaBaoDisplayState state)
		{
			if (!state.Found || string.IsNullOrWhiteSpace(state.FaBaoName))
			{
				return string.Empty;
			}
	
			string label = state.ClassName.Contains("紫府", StringComparison.Ordinal) ? "灵宝" : "法宝";
			return label + "：" + state.FaBaoName.Trim();
		}

		private static string BuildQianKunDaiSummary(long actorId)
		{
			return XjQianKunDaiRegistry.GetSummary(actorId);
		}
}

